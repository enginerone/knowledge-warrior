using System;
using System.Collections;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using KnowledgeWarrior.Data;
using UnityEngine;
using UnityEngine.Networking;

namespace KnowledgeWarrior.Progress
{
    [Serializable]
    public class BattleAnswerLogDto
    {
        public string questionId;
        public bool wasCorrect;
        public int responseTimeMs;
    }

    [Serializable]
    public class BattleResultSubmission
    {
        public string subject;
        public int xpGained;
        public bool victory;
        public int correctAnswers;
        public int totalAnswers;
        public BattleAnswerLogDto[] answerLogs;
    }

    /// <summary>
    /// Online connectivity monitor and live backend synchronization service.
    /// Enforces active internet reachability, loads live approved questions from PostgreSQL,
    /// and commits battle XP sequentially on the server without offline caches or queues.
    /// </summary>
    public class SyncManager : MonoBehaviour
    {
        public const string DefaultLocalApiUrl = "http://localhost:5000/api/v1";
        public const string DefaultAndroidLanApiUrl = "http://192.168.1.54:5000/api/v1";
        public const string DefaultCloudProductionApiUrl = "https://knowledge-warrior-api-production.up.railway.app/api/v1";
        private const string EnvironmentConfigResourcePath = "CloudEnvironmentConfig";

        public static bool IsLocalDevelopmentEnvironment => Application.isEditor || Debug.isDebugBuild;

        public static string AuthTokenPrefKey =>
            IsLocalDevelopmentEnvironment ? "kw_local_auth_token" : "kw_cloud_auth_token";

        public static string ApiUrlPrefKey =>
            IsLocalDevelopmentEnvironment ? "kw_local_api_base_url" : "kw_cloud_api_base_url";

        public static SyncManager Instance { get; private set; }
        public static event Action<string> OnApiBaseUrlChanged;

        private static bool _envConfigLoaded;
        private static EnvironmentConfigDto _cachedEnvConfig;

        [Header("Dependencies")]
        [SerializeField] private QuestionManager questionManager;

        [Header("Backend Configuration")]
        [Tooltip("Local Express API base URL used exclusively in Unity Editor and Development Builds.")]
        [SerializeField] private string apiBaseUrl = DefaultLocalApiUrl;
        [Tooltip("Cloud Production Railway HTTPS API base URL used exclusively in Release APK builds.")]
        [SerializeField] private string cloudProductionApiUrl = DefaultCloudProductionApiUrl;
        [Tooltip("HTTP request timeout in seconds.")]
        [Range(3, 30)] [SerializeField] private int requestTimeoutSeconds = 10;

        [Header("Connectivity Watchdog")]
        [Tooltip("Interval in seconds between live internet reachability checks.")]
        [Range(1f, 15f)] [SerializeField] private float reachabilityCheckInterval = 2f;

        public bool IsOnline => Application.internetReachability != NetworkReachability.NotReachable;
        public bool IsServerConnected { get; private set; }
        public bool IsSyncing { get; private set; }
        public string LastError { get; private set; }

        public event Action<bool, string> OnOnlineAvailabilityChanged;
        public event Action OnSyncStarted;
        public event Action<bool> OnSyncFinished;

        private bool _wasOnline;
        private float _nextCheckTime;

        public string EffectiveApiBaseUrl =>
            ResolveApiBaseUrl(IsLocalDevelopmentEnvironment ? apiBaseUrl : cloudProductionApiUrl);

        public static string ResolveApiBaseUrl(string serializedFallback = null)
        {
            var envConfig = LoadEnvironmentConfig();

            if (!IsLocalDevelopmentEnvironment)
            {
                string cloudCandidate = envConfig != null && !string.IsNullOrWhiteSpace(envConfig.cloudProductionApiUrl)
                    ? envConfig.cloudProductionApiUrl
                    : (!string.IsNullOrWhiteSpace(serializedFallback) ? serializedFallback : DefaultCloudProductionApiUrl);

                if (IsLocalOrPrivateNetworkUrl(cloudCandidate))
                {
                    cloudCandidate = DefaultCloudProductionApiUrl;
                }

                return NormalizeApiBaseUrl(cloudCandidate, DefaultCloudProductionApiUrl);
            }

            string overrideUrl = PlayerPrefs.GetString(ApiUrlPrefKey, string.Empty);
            string localDefault = envConfig != null && !string.IsNullOrWhiteSpace(envConfig.localDevelopmentApiUrl)
                ? envConfig.localDevelopmentApiUrl
                : DefaultLocalApiUrl;

            string raw = !string.IsNullOrWhiteSpace(overrideUrl)
                ? overrideUrl
                : (!string.IsNullOrWhiteSpace(serializedFallback) ? serializedFallback : localDefault);

#if UNITY_EDITOR || UNITY_STANDALONE
            if (IsStaleLocalDevLanUrl(raw))
            {
                raw = DefaultLocalApiUrl;
                if (!string.IsNullOrWhiteSpace(overrideUrl))
                {
                    PlayerPrefs.DeleteKey(ApiUrlPrefKey);
                    PlayerPrefs.Save();
                }
            }
#elif UNITY_ANDROID
            if (raw.Contains("localhost") || raw.Contains("127.0.0.1"))
            {
                raw = DefaultAndroidLanApiUrl;
            }
#endif
            return NormalizeApiBaseUrl(raw, DefaultLocalApiUrl);
        }

        private static EnvironmentConfigDto LoadEnvironmentConfig()
        {
            if (_envConfigLoaded) return _cachedEnvConfig;
            _envConfigLoaded = true;

            try
            {
                var asset = Resources.Load<TextAsset>(EnvironmentConfigResourcePath);
                if (asset != null && !string.IsNullOrWhiteSpace(asset.text))
                {
                    _cachedEnvConfig = JsonUtility.FromJson<EnvironmentConfigDto>(asset.text);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SyncManager] Failed to parse CloudEnvironmentConfig: {e.Message}");
            }

            return _cachedEnvConfig;
        }

        private static string NormalizeApiBaseUrl(string url, string fallback)
        {
            string clean = (string.IsNullOrWhiteSpace(url) ? fallback : url).Trim().TrimEnd('/');
            if (!clean.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
            {
                clean = $"{clean}/api/v1";
            }
            return clean;
        }

        private static bool IsLocalOrPrivateNetworkUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                return true;
            }

            string host = uri.Host ?? string.Empty;
            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(host, "127.0.0.1", StringComparison.Ordinal) ||
                   host.StartsWith("192.168.", StringComparison.Ordinal) ||
                   host.StartsWith("10.", StringComparison.Ordinal);
        }

        private static bool IsStaleLocalDevLanUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (uri.Port != 5000 || !uri.Host.StartsWith("192.168.", StringComparison.Ordinal))
            {
                return false;
            }

            string activeLanIp = DetectActiveLocalIpv4();
            return string.IsNullOrWhiteSpace(activeLanIp) || !string.Equals(uri.Host, activeLanIp, StringComparison.Ordinal);
        }

        private static string DetectActiveLocalIpv4()
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(unicast.Address))
                        {
                            return unicast.Address.ToString();
                        }
                    }
                }
            }
            catch
            {
                // Safely ignore restricted interface enumeration
            }
            return null;
        }

        public static void SetCustomApiBaseUrl(string url)
        {
            if (!IsLocalDevelopmentEnvironment)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                PlayerPrefs.DeleteKey(ApiUrlPrefKey);
            }
            else
            {
                PlayerPrefs.SetString(ApiUrlPrefKey, NormalizeApiBaseUrl(url, DefaultLocalApiUrl));
            }
            PlayerPrefs.Save();
            OnApiBaseUrlChanged?.Invoke(ResolveApiBaseUrl());
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            ProgressManager.PurgeLegacyLocalSaveFiles();
            QuestionManager.PurgeLegacyOfflineQuestionCache();

            if (questionManager == null)
            {
                questionManager = FindAnyObjectByType<QuestionManager>();
            }
        }

        private void Start()
        {
            _wasOnline = IsOnline;
            _nextCheckTime = Time.unscaledTime + reachabilityCheckInterval;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextCheckTime) return;
            _nextCheckTime = Time.unscaledTime + reachabilityCheckInterval;

            bool onlineNow = IsOnline;
            if (!onlineNow && (_wasOnline || IsServerConnected))
            {
                _wasOnline = false;
                ReportConnectionLost("Internet data connection was lost. Reconnect to Wi-Fi or mobile data to continue playing.");
                return;
            }

            _wasOnline = onlineNow;
        }

        public void ReportConnectionLost(string reason)
        {
            IsServerConnected = false;
            LastError = reason;
            OnOnlineAvailabilityChanged?.Invoke(false, reason);
        }

        /// <summary>
        /// Verifies internet data availability, checks backend health, and loads approved questions live from PostgreSQL.
        /// </summary>
        public IEnumerator VerifyOnlineAndLoadQuestionsRoutine(Action<bool, string> onComplete)
        {
            if (!IsOnline)
            {
                string msg = "No internet data connection detected. Connect to Wi-Fi or mobile data and try again.";
                ReportConnectionLost(msg);
                onComplete?.Invoke(false, msg);
                yield break;
            }

            if (questionManager == null)
            {
                questionManager = FindAnyObjectByType<QuestionManager>();
            }

            IsSyncing = true;
            OnSyncStarted?.Invoke();

            UnityWebRequest healthReq = UnityWebRequest.Get($"{EffectiveApiBaseUrl}/health");
            healthReq.timeout = requestTimeoutSeconds;
            yield return SendGetWithLocalhostFallbackRoutine(healthReq, "/health", r => healthReq = r);

            using (healthReq)
            {
                if (healthReq.result != UnityWebRequest.Result.Success)
                {
                    IsSyncing = false;
                    string err = $"Cannot reach Knowledge Warrior server at {EffectiveApiBaseUrl}: {healthReq.error}";
                    ReportConnectionLost(err);
                    OnSyncFinished?.Invoke(false);
                    onComplete?.Invoke(false, err);
                    yield break;
                }
            }

            UnityWebRequest qReq = UnityWebRequest.Get($"{EffectiveApiBaseUrl}/questions");
            qReq.timeout = requestTimeoutSeconds;
            AttachAuthHeaderIfPresent(qReq);
            yield return SendGetWithLocalhostFallbackRoutine(qReq, "/questions", r => qReq = r);

            using (qReq)
            {
                if (qReq.result != UnityWebRequest.Result.Success)
                {
                    IsSyncing = false;
                    string err = $"Failed to download live curriculum questions ({qReq.responseCode}): {qReq.error}";
                    ReportConnectionLost(err);
                    OnSyncFinished?.Invoke(false);
                    onComplete?.Invoke(false, err);
                    yield break;
                }

                string responseJson = qReq.downloadHandler?.text ?? string.Empty;
                string parseError = null;
                if (questionManager == null || !questionManager.ApplyOnlineQuestionsJson(responseJson, out parseError))
                {
                    IsSyncing = false;
                    string err = $"Live question bank error: {parseError ?? "QuestionManager missing"}";
                    ReportConnectionLost(err);
                    OnSyncFinished?.Invoke(false);
                    onComplete?.Invoke(false, err);
                    yield break;
                }
            }

            IsSyncing = false;
            IsServerConnected = true;
            LastError = null;
            OnOnlineAvailabilityChanged?.Invoke(true, null);
            OnSyncFinished?.Invoke(true);
            onComplete?.Invoke(true, null);
        }

        /// <summary>
        /// Submits a completed battle directly to POST /api/v1/sync/battle so PostgreSQL updates
        /// Subject XP and Subject Level sequentially (1 level at a time, never skipping levels).
        /// </summary>
        public IEnumerator SubmitBattleResultOnlineRoutine(
            BattleResultSubmission submission,
            Action<bool, string, int> onComplete)
        {
            if (!IsOnline)
            {
                string msg = "Internet connection lost before battle progress could be saved to the server.";
                ReportConnectionLost(msg);
                onComplete?.Invoke(false, msg, 0);
                yield break;
            }

            IsSyncing = true;
            OnSyncStarted?.Invoke();

            string jsonBody = JsonUtility.ToJson(submission);
            string url = $"{EffectiveApiBaseUrl}/sync/battle";

            using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = requestTimeoutSeconds;
            req.SetRequestHeader("Content-Type", "application/json");
            AttachAuthHeaderIfPresent(req);

            yield return req.SendWebRequest();
            IsSyncing = false;

            if (req.result == UnityWebRequest.Result.ConnectionError ||
                req.result == UnityWebRequest.Result.DataProcessingError)
            {
                string err = $"Lost connection to server while saving battle result: {req.error}";
                ReportConnectionLost(err);
                OnSyncFinished?.Invoke(false);
                onComplete?.Invoke(false, err, 0);
                yield break;
            }

            if (req.result != UnityWebRequest.Result.Success)
            {
                string err = $"Server rejected battle sync ({req.responseCode}): {req.error}";
                LastError = err;
                OnSyncFinished?.Invoke(false);
                onComplete?.Invoke(false, err, 0);
                yield break;
            }

            try
            {
                var res = JsonUtility.FromJson<BattleSyncResponseDto>(req.downloadHandler.text);
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

                int levelsGained = 0;
                if (res?.subjectProgress != null && !string.IsNullOrWhiteSpace(res.subjectProgress.subject))
                {
                    ProgressManager.SetSubjectProgressFromServer(
                        res.subjectProgress.subject,
                        res.subjectProgress.subjectLevel,
                        res.subjectProgress.subjectXp,
                        res.subjectProgress.subjectPoints);
                    levelsGained = Mathf.Max(0, res.subjectProgress.levelsGained);
                }

                IsServerConnected = true;
                LastError = null;
                OnSyncFinished?.Invoke(true);
                onComplete?.Invoke(true, null, levelsGained);
            }
            catch (Exception e)
            {
                string err = $"Failed to parse server battle response: {e.Message}";
                LastError = err;
                OnSyncFinished?.Invoke(false);
                onComplete?.Invoke(false, err, 0);
            }
        }

        public void ForceSyncAll(Action<bool, string> onComplete = null)
        {
            if (!isActiveAndEnabled) return;
            StartCoroutine(RefreshAllOnlineDataRoutine(onComplete));
        }

        /// <summary>
        /// Permanently marks a question as seen for the authenticated player on PostgreSQL
        /// immediately when it appears on screen, and triggers background AI replenishment if low.
        /// </summary>
        public void NotifyQuestionSeenOnline(string questionId, string subject, int difficulty)
        {
            if (!isActiveAndEnabled || !IsOnline || string.IsNullOrWhiteSpace(questionId)) return;
            string token = PlayerPrefs.GetString(AuthTokenPrefKey, string.Empty);
            if (string.IsNullOrWhiteSpace(token)) return;

            StartCoroutine(MarkQuestionSeenRoutine(questionId, subject, difficulty));
        }

        private IEnumerator MarkQuestionSeenRoutine(string questionId, string subject, int difficulty)
        {
            var payload = new MarkQuestionSeenDto
            {
                questionId = questionId,
                subject = subject,
                difficulty = difficulty
            };

            string jsonBody = JsonUtility.ToJson(payload);
            using var req = new UnityWebRequest($"{EffectiveApiBaseUrl}/questions/mark-seen", UnityWebRequest.kHttpVerbPOST);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = requestTimeoutSeconds;
            req.SetRequestHeader("Content-Type", "application/json");
            AttachAuthHeaderIfPresent(req);

            yield return req.SendWebRequest();
        }

        /// <summary>
        /// Requests live server replenishment for the given subject and difficulty tier,
        /// then reloads the online question bank so newly generated unseen questions become active.
        /// </summary>
        public IEnumerator ReplenishAndReloadSubjectQuestionsRoutine(
            string subject,
            int difficulty,
            Action<bool, string> onComplete)
        {
            if (!IsOnline)
            {
                string msg = "No internet connection detected while fetching new subject questions.";
                ReportConnectionLost(msg);
                onComplete?.Invoke(false, msg);
                yield break;
            }

            var payload = new ReplenishQuestionsRequestDto
            {
                subject = subject,
                difficulty = difficulty
            };

            string jsonBody = JsonUtility.ToJson(payload);
            using (var req = new UnityWebRequest($"{EffectiveApiBaseUrl}/questions/replenish", UnityWebRequest.kHttpVerbPOST))
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
                req.uploadHandler = new UploadHandlerRaw(bodyBytes);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = 22;
                req.SetRequestHeader("Content-Type", "application/json");
                AttachAuthHeaderIfPresent(req);
                yield return req.SendWebRequest();
            }

            yield return VerifyOnlineAndLoadQuestionsRoutine(onComplete);
        }

        private IEnumerator RefreshAllOnlineDataRoutine(Action<bool, string> onComplete)
        {
            bool ok = false;
            string err = null;
            yield return VerifyOnlineAndLoadQuestionsRoutine((qOk, qErr) =>
            {
                ok = qOk;
                err = qErr;
            });

            if (!ok)
            {
                onComplete?.Invoke(false, err);
                yield break;
            }

            if (AuthManager.Instance != null && AuthManager.Instance.IsAuthenticated)
            {
                yield return AuthManager.Instance.RefreshAuthenticatedProfileRoutine((pOk, pErr) =>
                {
                    ok = pOk;
                    err = pErr;
                });
            }

            onComplete?.Invoke(ok, err);
        }

        private IEnumerator SendGetWithLocalhostFallbackRoutine(
            UnityWebRequest initialReq,
            string endpointSuffix,
            Action<UnityWebRequest> onReplaced)
        {
            yield return initialReq.SendWebRequest();

            if (IsLocalDevelopmentEnvironment &&
                !Application.platform.Equals(RuntimePlatform.Android) &&
                initialReq.result == UnityWebRequest.Result.ConnectionError &&
                !string.IsNullOrWhiteSpace(endpointSuffix) &&
                !initialReq.url.StartsWith(DefaultLocalApiUrl, StringComparison.OrdinalIgnoreCase))
            {
                string fallbackUrl = $"{DefaultLocalApiUrl}{endpointSuffix}";
                var retryReq = UnityWebRequest.Get(fallbackUrl);
                retryReq.timeout = requestTimeoutSeconds;
                AttachAuthHeaderIfPresent(retryReq);
                yield return retryReq.SendWebRequest();

                if (retryReq.result != UnityWebRequest.Result.ConnectionError)
                {
                    initialReq.Dispose();
                    SetCustomApiBaseUrl(DefaultLocalApiUrl);
                    onReplaced?.Invoke(retryReq);
                    yield break;
                }
                retryReq.Dispose();
            }
        }

        private static void AttachAuthHeaderIfPresent(UnityWebRequest req)
        {
            string token = PlayerPrefs.GetString(AuthTokenPrefKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(token))
            {
                req.SetRequestHeader("Authorization", $"Bearer {token}");
            }
        }

        [Serializable]
        private class EnvironmentConfigDto
        {
            public string cloudProductionApiUrl;
            public string localDevelopmentApiUrl;
        }

        [Serializable]
        private class MarkQuestionSeenDto
        {
            public string questionId;
            public string subject;
            public int difficulty;
        }

        [Serializable]
        private class ReplenishQuestionsRequestDto
        {
            public string subject;
            public int difficulty;
        }

        [Serializable]
        private class BattleSyncResponseDto
        {
            public string status;
            public ServerPlayerSummaryDto player;
            public ServerSubjectProgressDto subjectProgress;
        }

        [Serializable]
        private class ServerPlayerSummaryDto
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
        private class ServerSubjectProgressDto
        {
            public string subject;
            public int subjectLevel;
            public int subjectXp;
            public int subjectPoints;
            public int xpToNextLevel;
            public string badgeTier;
            public int levelsGained;
        }
    }
}
