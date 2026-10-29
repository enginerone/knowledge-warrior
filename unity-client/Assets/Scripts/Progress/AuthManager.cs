using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace KnowledgeWarrior.Progress
{
    /// <summary>
    /// Handles in-game player registration, persistent database-backed login authentication,
    /// automatic session restoration when reopening the application, and email OTP password recovery.
    /// </summary>
    public class AuthManager : MonoBehaviour
    {
        public const string UsernamePrefKey = "kw_auth_username";
        public const string EmailPrefKey = "kw_auth_email";
        public const string UserIdPrefKey = "kw_auth_user_id";

        public static AuthManager Instance { get; private set; }

        [SerializeField] private int requestTimeoutSeconds = 10;

        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Token);
        public bool IsBusy { get; private set; }
        public string Token => PlayerPrefs.GetString(SyncManager.AuthTokenPrefKey, string.Empty);
        public string AuthenticatedUsername => PlayerPrefs.GetString(UsernamePrefKey, string.Empty);
        public string AuthenticatedEmail => PlayerPrefs.GetString(EmailPrefKey, string.Empty);
        public string AuthenticatedUserId => PlayerPrefs.GetString(UserIdPrefKey, string.Empty);

        public event Action OnAuthStateChanged;

        private string ApiBaseUrl =>
            SyncManager.Instance != null
                ? SyncManager.Instance.EffectiveApiBaseUrl
                : PlayerPrefs.GetString(SyncManager.ApiUrlPrefKey, "http://localhost:5000/api/v1").TrimEnd('/');

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (IsAuthenticated && !string.IsNullOrWhiteSpace(AuthenticatedUsername))
            {
                var p = ProgressManager.Current;
                if (p.playerName != AuthenticatedUsername)
                {
                    p.playerName = AuthenticatedUsername;
                    ProgressManager.Save();
                }
            }
        }

        private void Start()
        {
            if (IsAuthenticated)
            {
                StartCoroutine(RefreshAuthenticatedProfileRoutine());
            }
        }

        /// <summary>
        /// Registers a new user account in PostgreSQL (<c>POST /api/v1/auth/register</c>).
        /// Requires a unique username, a real email address, and a password.
        /// </summary>
        public void Register(string username, string email, string password, Action<bool, string> onComplete)
        {
            if (IsBusy) return;
            string cleanUser = (username ?? string.Empty).Trim();
            string cleanEmail = (email ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(cleanUser) || string.IsNullOrWhiteSpace(cleanEmail) || string.IsNullOrWhiteSpace(password))
            {
                onComplete?.Invoke(false, "Username, a real email address, and password are all required.");
                return;
            }

            if (!cleanEmail.Contains("@") || !cleanEmail.Contains("."))
            {
                onComplete?.Invoke(false, "Please enter a valid email address for account recovery.");
                return;
            }

            var p = ProgressManager.Current;
            var payload = new RegisterRequestDto
            {
                username = cleanUser,
                email = cleanEmail,
                password = password,
                clientHash = SyncManager.ComputeClientHash(p.deviceId),
                totalXp = p.totalXp,
            };

            StartCoroutine(RegisterRoutine($"{ApiBaseUrl}/auth/register", JsonUtility.ToJson(payload), onComplete));
        }

        /// <summary>
        /// Backwards-compatible alias that registers the account with username, password, and email.
        /// </summary>
        public void RegisterAndLinkGuest(string username, string password, string email, Action<bool, string> onComplete)
        {
            Register(username, email, password, onComplete);
        }

        /// <summary>
        /// Authenticates against the PostgreSQL user record (<c>POST /api/v1/auth/login</c>)
        /// and persists the session so the user never needs to log in again when reopening the app.
        /// </summary>
        public void Login(string username, string password, Action<bool, string> onComplete)
        {
            if (IsBusy) return;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                onComplete?.Invoke(false, "Username and password are required.");
                return;
            }

            var payload = new LoginRequestDto
            {
                username = username.Trim(),
                password = password,
            };

            StartCoroutine(LoginRoutine($"{ApiBaseUrl}/auth/login", JsonUtility.ToJson(payload), onComplete));
        }

        public string LastOtpPreviewUrl { get; private set; }

        /// <summary>
        /// Step 1 of Forgot Password: sends a 6-digit OTP to the user's registered email (<c>POST /api/v1/auth/forgot-password</c>).
        /// </summary>
        public void RequestPasswordResetOtp(string email, Action<bool, string> onComplete)
        {
            if (IsBusy) return;
            string cleanEmail = (email ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cleanEmail) || !cleanEmail.Contains("@"))
            {
                onComplete?.Invoke(false, "Please enter your registered email address.");
                return;
            }

            LastOtpPreviewUrl = null;
            var payload = new ForgotPasswordRequestDto { email = cleanEmail };
            StartCoroutine(SimplePostRoutine($"{ApiBaseUrl}/auth/forgot-password", JsonUtility.ToJson(payload), (ok, msg, _) =>
            {
                onComplete?.Invoke(ok, msg);
            }));
        }

        /// <summary>
        /// Step 2 of Forgot Password: verifies the 6-digit OTP (<c>POST /api/v1/auth/verify-otp</c>).
        /// </summary>
        public void VerifyPasswordResetOtp(string email, string otp, Action<bool, string> onComplete)
        {
            if (IsBusy) return;
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(otp))
            {
                onComplete?.Invoke(false, "Please enter the 6-digit OTP sent to your email.");
                return;
            }

            var payload = new VerifyOtpRequestDto { email = email.Trim(), otp = otp.Trim() };
            StartCoroutine(SimplePostRoutine($"{ApiBaseUrl}/auth/verify-otp", JsonUtility.ToJson(payload), (ok, msg, _) =>
            {
                onComplete?.Invoke(ok, msg);
            }));
        }

        /// <summary>
        /// Step 3 of Forgot Password: verifies the OTP and sets the new password (<c>POST /api/v1/auth/reset-password</c>).
        /// </summary>
        public void ResetPasswordWithOtp(string email, string otp, string newPassword, Action<bool, string, string> onComplete)
        {
            if (IsBusy) return;
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(otp) || string.IsNullOrWhiteSpace(newPassword))
            {
                onComplete?.Invoke(false, "Email, 6-digit OTP, and new password are required.", null);
                return;
            }

            var payload = new ResetPasswordRequestDto
            {
                email = email.Trim(),
                otp = otp.Trim(),
                newPassword = newPassword,
            };

            StartCoroutine(SimplePostRoutine($"{ApiBaseUrl}/auth/reset-password", JsonUtility.ToJson(payload), onComplete));
        }

        /// <summary>Signs out of the authenticated account only when explicitly requested by the user.</summary>
        public void Logout()
        {
            PlayerPrefs.DeleteKey(SyncManager.AuthTokenPrefKey);
            PlayerPrefs.DeleteKey(UsernamePrefKey);
            PlayerPrefs.DeleteKey(EmailPrefKey);
            PlayerPrefs.DeleteKey(UserIdPrefKey);
            PlayerPrefs.Save();
            OnAuthStateChanged?.Invoke();
        }

        /// <summary>Silently syncs the user's per-subject progression to the backend.</summary>
        public void SyncSubjectProgressToServer(string subject)
        {
            if (!IsAuthenticated || string.IsNullOrWhiteSpace(subject)) return;
            var sp = ProgressManager.GetSubjectProgress(subject);
            var dto = new SubjectSyncRequestDto
            {
                subject = sp.subject,
                subjectLevel = sp.level,
                subjectXp = sp.points
            };
            StartCoroutine(PostAuthenticatedSilentRoutine($"{ApiBaseUrl}/pvp/subject-progress", JsonUtility.ToJson(dto)));
        }

        private IEnumerator RefreshAuthenticatedProfileRoutine()
        {
            using var req = UnityWebRequest.Get($"{ApiBaseUrl}/pvp/subject-progress");
            req.timeout = requestTimeoutSeconds;
            req.SetRequestHeader("Authorization", $"Bearer {Token}");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success && !string.IsNullOrWhiteSpace(req.downloadHandler?.text))
            {
                try
                {
                    var res = JsonUtility.FromJson<SubjectProgressListDto>(req.downloadHandler.text);
                    if (res?.subjects != null)
                    {
                        foreach (var item in res.subjects)
                        {
                            if (item == null || string.IsNullOrWhiteSpace(item.subject)) continue;
                            ProgressManager.SetSubjectProgressFromServer(
                                item.subject,
                                item.subjectLevel,
                                item.subjectXp,
                                item.pvpWins,
                                item.pvpLosses);
                        }
                        OnAuthStateChanged?.Invoke();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AuthManager] Could not parse subject progress: {e.Message}");
                }
            }
        }

        private IEnumerator PostAuthenticatedSilentRoutine(string url, string jsonBody)
        {
            using var req = BuildJsonPost(url, jsonBody);
            if (IsAuthenticated)
            {
                req.SetRequestHeader("Authorization", $"Bearer {Token}");
            }
            yield return req.SendWebRequest();
        }

        private IEnumerator RegisterRoutine(string url, string jsonBody, Action<bool, string> onComplete)
        {
            IsBusy = true;
            using var req = BuildJsonPost(url, jsonBody);
            yield return req.SendWebRequest();
            IsBusy = false;

            string responseText = req.downloadHandler?.text ?? string.Empty;
            if (req.result != UnityWebRequest.Result.Success)
            {
                string errMsg = TryExtractErrorMessage(responseText) ?? $"Registration failed ({req.responseCode}): {req.error}";
                onComplete?.Invoke(false, errMsg);
                yield break;
            }

            var res = JsonUtility.FromJson<AuthResponseDto>(responseText);
            onComplete?.Invoke(true, res?.message ?? "Registration successful! Please log in with your username and password.");
        }

        private IEnumerator LoginRoutine(string url, string jsonBody, Action<bool, string> onComplete)
        {
            IsBusy = true;
            using var req = BuildJsonPost(url, jsonBody);
            yield return req.SendWebRequest();
            IsBusy = false;

            string responseText = req.downloadHandler?.text ?? string.Empty;
            if (req.result != UnityWebRequest.Result.Success)
            {
                string errMsg = TryExtractErrorMessage(responseText) ?? $"Login failed ({req.responseCode}): {req.error}";
                onComplete?.Invoke(false, errMsg);
                yield break;
            }

            try
            {
                var res = JsonUtility.FromJson<AuthResponseDto>(responseText);
                if (res == null || string.IsNullOrWhiteSpace(res.token) || res.user == null)
                {
                    onComplete?.Invoke(false, "Invalid response from authentication server.");
                    yield break;
                }

                PlayerPrefs.SetString(SyncManager.AuthTokenPrefKey, res.token);
                PlayerPrefs.SetString(UsernamePrefKey, res.user.username ?? string.Empty);
                PlayerPrefs.SetString(EmailPrefKey, res.user.email ?? string.Empty);
                PlayerPrefs.SetString(UserIdPrefKey, res.user.id ?? string.Empty);
                PlayerPrefs.Save();

                ApplyServerProfile(res.user);
                StartCoroutine(RefreshAuthenticatedProfileRoutine());
                OnAuthStateChanged?.Invoke();

                if (SyncManager.Instance != null)
                {
                    SyncManager.Instance.TriggerSync(includeQuestions: false);
                }

                onComplete?.Invoke(true, res.message ?? $"Authenticated as {res.user.username}!");
            }
            catch (Exception e)
            {
                onComplete?.Invoke(false, $"Auth parse error: {e.Message}");
            }
        }

        private IEnumerator SimplePostRoutine(string url, string jsonBody, Action<bool, string, string> onComplete)
        {
            IsBusy = true;
            using var req = BuildJsonPost(url, jsonBody);
            yield return req.SendWebRequest();
            IsBusy = false;

            string responseText = req.downloadHandler?.text ?? string.Empty;
            if (req.result != UnityWebRequest.Result.Success)
            {
                string errMsg = TryExtractErrorMessage(responseText) ?? $"Request failed ({req.responseCode}): {req.error}";
                onComplete?.Invoke(false, errMsg, null);
                yield break;
            }

            try
            {
                var res = JsonUtility.FromJson<GenericAuthMessageDto>(responseText);
                if (!string.IsNullOrWhiteSpace(res?.previewUrl))
                {
                    LastOtpPreviewUrl = res.previewUrl;
                }
                onComplete?.Invoke(true, res?.message ?? "Success!", res?.username);
            }
            catch
            {
                onComplete?.Invoke(true, "Success!", null);
            }
        }

        private UnityWebRequest BuildJsonPost(string url, string jsonBody)
        {
            var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = requestTimeoutSeconds;
            req.SetRequestHeader("Content-Type", "application/json");
            return req;
        }

        private static void ApplyServerProfile(AuthUserDto user)
        {
            var p = ProgressManager.Current;
            p.playerName = user.username;

            if (user.totalXp > p.totalXp)
            {
                int delta = user.totalXp - p.totalXp;
                ProgressManager.AddXp(delta);
            }
            ProgressManager.Save();
        }

        private static string TryExtractErrorMessage(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var err = JsonUtility.FromJson<ErrorResponseDto>(json);
                return !string.IsNullOrWhiteSpace(err?.error) ? err.error : null;
            }
            catch
            {
                return null;
            }
        }

        [Serializable]
        private class RegisterRequestDto
        {
            public string username;
            public string email;
            public string password;
            public string clientHash;
            public int totalXp;
        }

        [Serializable]
        private class LoginRequestDto
        {
            public string username;
            public string password;
        }

        [Serializable]
        private class ForgotPasswordRequestDto
        {
            public string email;
        }

        [Serializable]
        private class VerifyOtpRequestDto
        {
            public string email;
            public string otp;
        }

        [Serializable]
        private class ResetPasswordRequestDto
        {
            public string email;
            public string otp;
            public string newPassword;
        }

        [Serializable]
        private class GenericAuthMessageDto
        {
            public string message;
            public string username;
            public string email;
            public string previewUrl;
        }

        [Serializable]
        private class AuthResponseDto
        {
            public string message;
            public string token;
            public AuthUserDto user;
        }

        [Serializable]
        private class AuthUserDto
        {
            public string id;
            public string username;
            public string email;
            public string role;
            public int totalXp;
            public int currentLevel;
        }

        [Serializable]
        private class SubjectSyncRequestDto
        {
            public string subject;
            public int subjectLevel;
            public int subjectXp;
        }

        [Serializable]
        private class SubjectProgressItemDto
        {
            public string subject;
            public int subjectLevel;
            public int subjectXp;
            public string badgeTier;
            public int pvpWins;
            public int pvpLosses;
        }

        [Serializable]
        private class SubjectProgressListDto
        {
            public SubjectProgressItemDto[] subjects;
        }

        [Serializable]
        private class ErrorResponseDto
        {
            public string error;
        }
    }
}
