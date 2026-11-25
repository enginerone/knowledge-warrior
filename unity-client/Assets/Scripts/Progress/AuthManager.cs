using System;
using System.Collections;
using System.Text;
using KnowledgeWarrior.Data;
using UnityEngine;
using UnityEngine.Networking;

namespace KnowledgeWarrior.Progress
{
    /// <summary>
    /// Online-only authentication and live PostgreSQL profile session manager.
    /// Handles account registration, persistent login verification, live per-subject
    /// progression loading, and email OTP password recovery.
    /// </summary>
    public class AuthManager : MonoBehaviour
    {
        public static string UsernamePrefKey =>
            SyncManager.IsLocalDevelopmentEnvironment ? "kw_local_auth_username" : "kw_cloud_auth_username";

        public static string EmailPrefKey =>
            SyncManager.IsLocalDevelopmentEnvironment ? "kw_local_auth_email" : "kw_cloud_auth_email";

        public static string UserIdPrefKey =>
            SyncManager.IsLocalDevelopmentEnvironment ? "kw_local_auth_user_id" : "kw_cloud_auth_user_id";

        public static AuthManager Instance { get; private set; }

        [SerializeField] private int requestTimeoutSeconds = 15;

        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Token);
        public bool IsBusy { get; private set; }
        public string Token => PlayerPrefs.GetString(SyncManager.AuthTokenPrefKey, string.Empty);
        public string AuthenticatedUsername => PlayerPrefs.GetString(UsernamePrefKey, string.Empty);
        public string AuthenticatedEmail => PlayerPrefs.GetString(EmailPrefKey, string.Empty);
        public string AuthenticatedUserId => PlayerPrefs.GetString(UserIdPrefKey, string.Empty);
        public string LastOtpPreviewUrl { get; private set; }

        public event Action OnAuthStateChanged;

        private Coroutine _usernameCheckCoroutine;

        private string ApiBaseUrl =>
            SyncManager.Instance != null
                ? SyncManager.Instance.EffectiveApiBaseUrl
                : SyncManager.ResolveApiBaseUrl();

        public static bool ValidateUsernameFormat(string rawInput, out string normalizedUsername, out string errorMessage)
        {
            normalizedUsername = (rawInput ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedUsername))
            {
                errorMessage = "A unique username is required.";
                return false;
            }

            if (normalizedUsername.Length < 3 || normalizedUsername.Length > 30)
            {
                errorMessage = "Username must be between 3 and 30 characters long.";
                return false;
            }

            for (int i = 0; i < normalizedUsername.Length; i++)
            {
                char c = normalizedUsername[i];
                bool allowed =
                    (c >= 'a' && c <= 'z') ||
                    (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') ||
                    c == '_' ||
                    c == '-' ||
                    c == '.';
                if (!allowed)
                {
                    errorMessage = "Username may only contain letters, numbers, underscores, hyphens, and periods without spaces.";
                    return false;
                }
            }

            errorMessage = null;
            return true;
        }

        public static bool ValidateGmailAddress(string rawInput, bool forForgotPassword, out string normalizedEmail, out string errorMessage)
        {
            normalizedEmail = (rawInput ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedEmail))
            {
                errorMessage = forForgotPassword
                    ? "Please enter the registered @gmail.com address linked to your account."
                    : "A valid @gmail.com address is required for registration.";
                return false;
            }

            string[] parts = normalizedEmail.Split('@');
            if (parts.Length != 2 || !string.Equals(parts[1], "gmail.com", StringComparison.Ordinal))
            {
                errorMessage = forForgotPassword
                    ? "Only registered @gmail.com accounts are allowed in Forgot Password."
                    : "Registration requires a valid @gmail.com account. Non-Gmail addresses are rejected.";
                return false;
            }

            string localPart = parts[0];
            if (localPart.Length < 6 || localPart.Length > 30)
            {
                errorMessage = "Invalid Gmail address. The username before @gmail.com must be between 6 and 30 characters long.";
                return false;
            }

            if (localPart.StartsWith(".", StringComparison.Ordinal) ||
                localPart.EndsWith(".", StringComparison.Ordinal) ||
                localPart.Contains("..", StringComparison.Ordinal))
            {
                errorMessage = "Invalid Gmail address. Gmail usernames cannot start or end with a period or contain consecutive periods.";
                return false;
            }

            int alphanumericCount = 0;
            for (int i = 0; i < localPart.Length; i++)
            {
                char c = localPart[i];
                bool isAlphaNum = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
                if (isAlphaNum)
                {
                    alphanumericCount++;
                }
                else if (c != '.')
                {
                    errorMessage = "Invalid Gmail address. Gmail usernames can only contain letters, numbers, and periods before @gmail.com.";
                    return false;
                }
            }

            if (alphanumericCount < 6)
            {
                errorMessage = "Invalid Gmail address. Gmail usernames must contain at least 6 letters or numbers before @gmail.com.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        public void CheckUsernameAvailability(string username, Action<bool, string> onComplete)
        {
            if (!ValidateUsernameFormat(username, out string cleanUser, out string formatErr))
            {
                onComplete?.Invoke(false, formatErr);
                return;
            }

            if (SyncManager.Instance != null && !SyncManager.Instance.IsOnline)
            {
                return;
            }

            if (_usernameCheckCoroutine != null)
            {
                StopCoroutine(_usernameCheckCoroutine);
            }
            _usernameCheckCoroutine = StartCoroutine(CheckUsernameAvailabilityRoutine(cleanUser, onComplete));
        }

        private IEnumerator CheckUsernameAvailabilityRoutine(string cleanUser, Action<bool, string> onComplete)
        {
            string encoded = UnityWebRequest.EscapeURL(cleanUser);
            using var req = UnityWebRequest.Get($"{ApiBaseUrl}/auth/check-username?username={encoded}");
            req.timeout = 8;
            yield return req.SendWebRequest();
            _usernameCheckCoroutine = null;

            if (req.result != UnityWebRequest.Result.Success || string.IsNullOrWhiteSpace(req.downloadHandler?.text))
            {
                string err = TryExtractErrorMessage(req.downloadHandler?.text);
                if (!string.IsNullOrWhiteSpace(err))
                {
                    onComplete?.Invoke(false, err);
                }
                yield break;
            }

            try
            {
                var dto = JsonUtility.FromJson<UsernameAvailabilityDto>(req.downloadHandler.text);
                if (dto != null)
                {
                    onComplete?.Invoke(dto.available, dto.message);
                }
            }
            catch
            {
                // Ignore transient availability check parse errors
            }
        }

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
                ProgressManager.Current.playerName = AuthenticatedUsername;
            }
        }

        public void Register(string username, string email, string password, Action<bool, string> onComplete)
        {
            if (IsBusy) return;
            if (SyncManager.Instance != null && !SyncManager.Instance.IsOnline)
            {
                onComplete?.Invoke(false, "Internet connection required. Connect to Wi-Fi or mobile data before registering.");
                return;
            }

            if (!ValidateUsernameFormat(username, out string cleanUser, out string userErr))
            {
                onComplete?.Invoke(false, userErr);
                return;
            }

            if (!ValidateGmailAddress(email, forForgotPassword: false, out string cleanEmail, out string emailErr))
            {
                onComplete?.Invoke(false, emailErr);
                return;
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            {
                onComplete?.Invoke(false, "Password must be at least 6 characters long.");
                return;
            }

            var payload = new RegisterRequestDto
            {
                username = cleanUser,
                email = cleanEmail,
                password = password,
            };

            StartCoroutine(RegisterRoutine($"{ApiBaseUrl}/auth/register", JsonUtility.ToJson(payload), onComplete));
        }

        public void Login(string username, string password, Action<bool, string> onComplete)
        {
            if (IsBusy) return;
            if (SyncManager.Instance != null && !SyncManager.Instance.IsOnline)
            {
                onComplete?.Invoke(false, "Internet connection required. Connect to Wi-Fi or mobile data before signing in.");
                return;
            }

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

        public void RequestPasswordResetOtp(string email, Action<bool, string> onComplete)
        {
            if (IsBusy) return;
            if (SyncManager.Instance != null && !SyncManager.Instance.IsOnline)
            {
                onComplete?.Invoke(false, "Internet connection required to send a password recovery OTP.");
                return;
            }

            if (!ValidateGmailAddress(email, forForgotPassword: true, out string cleanEmail, out string emailErr))
            {
                onComplete?.Invoke(false, emailErr);
                return;
            }

            LastOtpPreviewUrl = null;
            var payload = new ForgotPasswordRequestDto { email = cleanEmail };
            StartCoroutine(SimplePostRoutine($"{ApiBaseUrl}/auth/forgot-password", JsonUtility.ToJson(payload), (ok, msg, _) =>
            {
                onComplete?.Invoke(ok, msg);
            }));
        }

        public void VerifyPasswordResetOtp(string email, string otp, Action<bool, string> onComplete)
        {
            if (IsBusy) return;
            if (SyncManager.Instance != null && !SyncManager.Instance.IsOnline)
            {
                onComplete?.Invoke(false, "Internet connection required to verify OTP.");
                return;
            }

            if (!ValidateGmailAddress(email, forForgotPassword: true, out string cleanEmail, out string emailErr))
            {
                onComplete?.Invoke(false, emailErr);
                return;
            }

            string cleanOtp = (otp ?? string.Empty).Trim();
            if (cleanOtp.Length != 6)
            {
                onComplete?.Invoke(false, "Please enter the 6-digit OTP sent to your registered Gmail.");
                return;
            }

            var payload = new VerifyOtpRequestDto { email = cleanEmail, otp = cleanOtp };
            StartCoroutine(SimplePostRoutine($"{ApiBaseUrl}/auth/verify-otp", JsonUtility.ToJson(payload), (ok, msg, _) =>
            {
                onComplete?.Invoke(ok, msg);
            }));
        }

        public void ResetPasswordWithOtp(string email, string otp, string newPassword, Action<bool, string, string> onComplete)
        {
            if (IsBusy) return;
            if (SyncManager.Instance != null && !SyncManager.Instance.IsOnline)
            {
                onComplete?.Invoke(false, "Internet connection required to update your password.", null);
                return;
            }

            if (!ValidateGmailAddress(email, forForgotPassword: true, out string cleanEmail, out string emailErr))
            {
                onComplete?.Invoke(false, emailErr, null);
                return;
            }

            string cleanOtp = (otp ?? string.Empty).Trim();
            if (cleanOtp.Length != 6 || string.IsNullOrWhiteSpace(newPassword))
            {
                onComplete?.Invoke(false, "Registered Gmail address, 6-digit OTP, and new password are required.", null);
                return;
            }

            if (newPassword.Length < 6)
            {
                onComplete?.Invoke(false, "New password must be at least 6 characters long.", null);
                return;
            }

            var payload = new ResetPasswordRequestDto
            {
                email = cleanEmail,
                otp = cleanOtp,
                newPassword = newPassword,
            };

            StartCoroutine(SimplePostRoutine($"{ApiBaseUrl}/auth/reset-password", JsonUtility.ToJson(payload), onComplete));
        }

        public void Logout()
        {
            ClearStoredCredentials();
            ProgressManager.ResetSessionState();
            FindAnyObjectByType<QuestionManager>()?.ResetSeenQuestionsForLogout();
            OnAuthStateChanged?.Invoke();
        }

        /// <summary>
        /// Verifies the stored JWT against GET /api/v1/auth/me and hydrates live per-subject
        /// progression from GET /api/v1/sync/subject-progress.
        /// Callback parameters: (bool networkOk, bool authenticatedOk, string errorMessage).
        /// </summary>
        public IEnumerator ValidateSessionAndLoadProgressRoutine(Action<bool, bool, string> onComplete)
        {
            if (!IsAuthenticated)
            {
                onComplete?.Invoke(true, false, null);
                yield break;
            }

            if (SyncManager.Instance != null && !SyncManager.Instance.IsOnline)
            {
                onComplete?.Invoke(false, false, "No internet connection detected. Connect to the internet to verify your account.");
                yield break;
            }

            using (var meReq = UnityWebRequest.Get($"{ApiBaseUrl}/auth/me"))
            {
                meReq.timeout = requestTimeoutSeconds;
                meReq.SetRequestHeader("Authorization", $"Bearer {Token}");
                yield return meReq.SendWebRequest();

                if (meReq.result == UnityWebRequest.Result.ConnectionError ||
                    meReq.result == UnityWebRequest.Result.DataProcessingError)
                {
                    string err = $"Cannot reach authentication server: {meReq.error}";
                    SyncManager.Instance?.ReportConnectionLost(err);
                    onComplete?.Invoke(false, false, err);
                    yield break;
                }

                if (meReq.result != UnityWebRequest.Result.Success)
                {
                    string err = TryExtractErrorMessage(meReq.downloadHandler?.text) ?? "Saved session expired. Please sign in again.";
                    ClearStoredCredentials();
                    ProgressManager.ResetSessionState();
                    onComplete?.Invoke(true, false, err);
                    yield break;
                }
            }

            bool profileOk = false;
            string profileErr = null;
            yield return RefreshAuthenticatedProfileRoutine((ok, err) =>
            {
                profileOk = ok;
                profileErr = err;
            });

            if (!profileOk)
            {
                onComplete?.Invoke(false, false, profileErr ?? "Failed to load live subject progression from PostgreSQL.");
                yield break;
            }

            OnAuthStateChanged?.Invoke();
            onComplete?.Invoke(true, true, null);
        }

        public IEnumerator RefreshAuthenticatedProfileRoutine(Action<bool, string> onComplete = null)
        {
            if (!IsAuthenticated)
            {
                onComplete?.Invoke(false, "Not signed in.");
                yield break;
            }

            using var req = UnityWebRequest.Get($"{ApiBaseUrl}/sync/subject-progress");
            req.timeout = requestTimeoutSeconds;
            req.SetRequestHeader("Authorization", $"Bearer {Token}");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.ConnectionError ||
                req.result == UnityWebRequest.Result.DataProcessingError)
            {
                string err = $"Lost connection while loading subject progression: {req.error}";
                SyncManager.Instance?.ReportConnectionLost(err);
                onComplete?.Invoke(false, err);
                yield break;
            }

            if (req.result != UnityWebRequest.Result.Success || string.IsNullOrWhiteSpace(req.downloadHandler?.text))
            {
                string err = TryExtractErrorMessage(req.downloadHandler?.text) ?? $"Failed to load subject progress ({req.responseCode}).";
                onComplete?.Invoke(false, err);
                yield break;
            }

            try
            {
                var res = JsonUtility.FromJson<SubjectProgressListDto>(req.downloadHandler.text);
                if (res?.player != null)
                {
                    ProgressManager.ApplyServerPlayerSummary(
                        res.player.username,
                        res.player.currentLevel,
                        res.player.totalXp,
                        res.player.globalRank,
                        res.player.battlesWon,
                        res.player.battlesLost,
                        res.player.questionsAnswered,
                        res.player.correctAnswers);
                }

                if (res?.subjects != null)
                {
                    foreach (var item in res.subjects)
                    {
                        if (item == null || string.IsNullOrWhiteSpace(item.subject)) continue;
                        ProgressManager.SetSubjectProgressFromServer(
                            item.subject,
                            item.subjectLevel,
                            item.subjectXp,
                            item.subjectPoints);
                    }
                }

                var qm = FindAnyObjectByType<QuestionManager>();
                if (qm != null)
                {
                    qm.SetSeenQuestionIds(res?.seenQuestionIds, resetExisting: true);
                }

                OnAuthStateChanged?.Invoke();
                onComplete?.Invoke(true, null);
            }
            catch (Exception e)
            {
                onComplete?.Invoke(false, $"Could not parse subject progress response: {e.Message}");
            }
        }

        private IEnumerator RegisterRoutine(string url, string jsonBody, Action<bool, string> onComplete)
        {
            IsBusy = true;
            UnityWebRequest req = BuildJsonPost(url, jsonBody);
            yield return SendWithLocalhostFallbackRoutine(req, "/auth/register", jsonBody, updatedReq => req = updatedReq);
            IsBusy = false;

            using (req)
            {
                string responseText = req.downloadHandler?.text ?? string.Empty;
                if (req.result == UnityWebRequest.Result.ConnectionError)
                {
                    string err = $"Cannot connect to server: {req.error}";
                    SyncManager.Instance?.ReportConnectionLost(err);
                    onComplete?.Invoke(false, err);
                    yield break;
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    string errMsg = TryExtractErrorMessage(responseText) ?? $"Registration failed ({req.responseCode}): {req.error}";
                    onComplete?.Invoke(false, errMsg);
                    yield break;
                }

                var res = JsonUtility.FromJson<AuthResponseDto>(responseText);
                onComplete?.Invoke(true, res?.message ?? "Registration successful! Please log in with your username and password.");
            }
        }

        private IEnumerator LoginRoutine(string url, string jsonBody, Action<bool, string> onComplete)
        {
            IsBusy = true;
            UnityWebRequest req = BuildJsonPost(url, jsonBody);
            yield return SendWithLocalhostFallbackRoutine(req, "/auth/login", jsonBody, updatedReq => req = updatedReq);
            IsBusy = false;

            using (req)
            {
                string responseText = req.downloadHandler?.text ?? string.Empty;
                if (req.result == UnityWebRequest.Result.ConnectionError)
                {
                    string err = $"Cannot connect to server: {req.error}";
                    SyncManager.Instance?.ReportConnectionLost(err);
                    onComplete?.Invoke(false, err);
                    yield break;
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    string errMsg = TryExtractErrorMessage(responseText) ?? $"Login failed ({req.responseCode}): {req.error}";
                    onComplete?.Invoke(false, errMsg);
                    yield break;
                }

                AuthResponseDto res;
                try
                {
                    res = JsonUtility.FromJson<AuthResponseDto>(responseText);
                }
                catch (Exception e)
                {
                    onComplete?.Invoke(false, $"Auth parse error: {e.Message}");
                    yield break;
                }

                if (res == null || string.IsNullOrWhiteSpace(res.token) || res.user == null)
                {
                    onComplete?.Invoke(false, "Invalid response from authentication server.");
                    yield break;
                }

                ProgressManager.ResetSessionState();
                PlayerPrefs.SetString(SyncManager.AuthTokenPrefKey, res.token);
                PlayerPrefs.SetString(UsernamePrefKey, res.user.username ?? string.Empty);
                PlayerPrefs.SetString(EmailPrefKey, res.user.email ?? string.Empty);
                PlayerPrefs.SetString(UserIdPrefKey, res.user.id ?? string.Empty);
                PlayerPrefs.Save();

                ProgressManager.ApplyServerPlayerSummary(
                    res.user.username,
                    res.user.currentLevel,
                    res.user.totalXp,
                    0,
                    res.user.battlesWon,
                    res.user.battlesLost,
                    0,
                    0);

                bool hydratedOk = false;
                string hydrateErr = null;
                IsBusy = true;
                yield return RefreshAuthenticatedProfileRoutine((ok, err) =>
                {
                    hydratedOk = ok;
                    hydrateErr = err;
                });
                IsBusy = false;

                if (!hydratedOk)
                {
                    onComplete?.Invoke(false, hydrateErr ?? "Connected to account, failed to load live subject progress.");
                    yield break;
                }

                OnAuthStateChanged?.Invoke();
                onComplete?.Invoke(true, res.message ?? $"Authenticated as {res.user.username}!");
            }
        }

        private IEnumerator SimplePostRoutine(string url, string jsonBody, Action<bool, string, string> onComplete)
        {
            IsBusy = true;
            string endpointSuffix = ExtractEndpointSuffix(url);
            UnityWebRequest req = BuildJsonPost(url, jsonBody);
            yield return SendWithLocalhostFallbackRoutine(req, endpointSuffix, jsonBody, updatedReq => req = updatedReq);
            IsBusy = false;

            using (req)
            {
                string responseText = req.downloadHandler?.text ?? string.Empty;
                if (req.result == UnityWebRequest.Result.ConnectionError)
                {
                    string err = $"Cannot connect to server: {req.error}";
                    SyncManager.Instance?.ReportConnectionLost(err);
                    onComplete?.Invoke(false, err, null);
                    yield break;
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    string errMsg = TryExtractErrorMessage(responseText) ?? $"Request failed ({req.responseCode}): {req.error}";
                    onComplete?.Invoke(false, errMsg, null);
                    yield break;
                }

                try
                {
                    var res = JsonUtility.FromJson<GenericAuthMessageDto>(responseText);
                    if (SyncManager.IsLocalDevelopmentEnvironment && !string.IsNullOrWhiteSpace(res?.previewUrl))
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
        }

        private IEnumerator SendWithLocalhostFallbackRoutine(
            UnityWebRequest initialReq,
            string endpointSuffix,
            string jsonBody,
            Action<UnityWebRequest> onReplacedRequest)
        {
            yield return initialReq.SendWebRequest();

            if (SyncManager.IsLocalDevelopmentEnvironment &&
                !Application.platform.Equals(RuntimePlatform.Android) &&
                initialReq.result == UnityWebRequest.Result.ConnectionError &&
                !string.IsNullOrWhiteSpace(endpointSuffix) &&
                !initialReq.url.StartsWith(SyncManager.DefaultLocalApiUrl, StringComparison.OrdinalIgnoreCase))
            {
                string fallbackUrl = $"{SyncManager.DefaultLocalApiUrl}{endpointSuffix}";
                var retryReq = BuildJsonPost(fallbackUrl, jsonBody);
                yield return retryReq.SendWebRequest();

                if (retryReq.result != UnityWebRequest.Result.ConnectionError)
                {
                    initialReq.Dispose();
                    SyncManager.SetCustomApiBaseUrl(SyncManager.DefaultLocalApiUrl);
                    onReplacedRequest?.Invoke(retryReq);
                    yield break;
                }
                retryReq.Dispose();
            }
        }

        private static void ClearStoredCredentials()
        {
            PlayerPrefs.DeleteKey(SyncManager.AuthTokenPrefKey);
            PlayerPrefs.DeleteKey(UsernamePrefKey);
            PlayerPrefs.DeleteKey(EmailPrefKey);
            PlayerPrefs.DeleteKey(UserIdPrefKey);
            PlayerPrefs.Save();
        }

        private static string ExtractEndpointSuffix(string fullUrl)
        {
            int idx = fullUrl.IndexOf("/auth/", StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? fullUrl.Substring(idx) : string.Empty;
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
            public int battlesWon;
            public int battlesLost;
        }

        [Serializable]
        private class SubjectProgressPlayerDto
        {
            public string id;
            public string username;
            public int totalXp;
            public int currentLevel;
            public int globalRank;
            public int battlesWon;
            public int battlesLost;
            public int questionsAnswered;
            public int correctAnswers;
        }

        [Serializable]
        private class SubjectProgressItemDto
        {
            public string subject;
            public int subjectLevel;
            public int subjectXp;
            public int subjectPoints;
            public int xpToNextLevel;
            public string badgeTier;
        }

        [Serializable]
        private class SubjectProgressListDto
        {
            public SubjectProgressPlayerDto player;
            public string[] seenQuestionIds;
            public SubjectProgressItemDto[] subjects;
        }

        [Serializable]
        private class UsernameAvailabilityDto
        {
            public bool available;
            public string message;
            public string error;
        }

        [Serializable]
        private class ErrorResponseDto
        {
            public string error;
        }
    }
}
