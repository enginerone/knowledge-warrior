using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KnowledgeWarrior.Data;
using UnityEngine;
using UnityEngine.Networking;

namespace KnowledgeWarrior.Progress
{
    /// <summary>
    /// Asynchronous offline-first cloud synchronization manager (Phase 3).
    ///
    /// Responsibilities:
    ///   1. Watches <see cref="Application.internetReachability"/> for offline -> online transitions.
    ///   2. Batch-uploads queued <see cref="PendingScore"/> events from <see cref="ProgressManager"/>
    ///      to <c>POST /api/v1/sync/progress</c> via non-blocking <see cref="UnityWebRequest"/> coroutines.
    ///   3. Pulls over-the-air (OTA) approved question packs from <c>GET /api/v1/questions</c>
    ///      and persists them atomically to <c>Application.persistentDataPath/questions_cache.json</c>
    ///      via <see cref="QuestionManager.SaveToCacheAndApply"/>.
    /// </summary>
    public class SyncManager : MonoBehaviour
    {
        public const string AuthTokenPrefKey = "kw_auth_token";
        public const string ApiUrlPrefKey = "kw_api_base_url";
        private const string HmacSalt = "kw-offline-sync-v1";

        public static SyncManager Instance { get; private set; }

        [Header("Dependencies")]
        [SerializeField] private QuestionManager questionManager;

        [Header("Backend Configuration")]
        [Tooltip("Base URL of the Knowledge Warrior Express API (use LAN IP so Android phones on Wi-Fi can connect).")]
        [SerializeField] private string apiBaseUrl = "http://192.168.1.55:5000/api/v1";
        [Tooltip("HTTP request timeout in seconds so poor reception never stalls background coroutines.")]
        [Range(3, 30)] [SerializeField] private int requestTimeoutSeconds = 10;

        [Header("Sync Schedule")]
        [SerializeField] private bool syncOnStart = true;
        [SerializeField] private bool syncAfterEachBattle = true;
        [Tooltip("How often (seconds) to check Application.internetReachability and retry pending queue.")]
        [Range(2f, 60f)] [SerializeField] private float reachabilityCheckInterval = 5f;
        [Tooltip("Minimum seconds between automatic full question bank checks.")]
        [Range(15f, 600f)] [SerializeField] private float questionSyncCooldown = 60f;

        public bool IsOnline => Application.internetReachability != NetworkReachability.NotReachable;
        public bool IsSyncing { get; private set; }
        public int PendingCount => ProgressManager.Current.pendingSync?.Count ?? 0;
        public string LastError { get; private set; }

        public event Action<bool> OnNetworkReachabilityChanged;
        public event Action OnSyncStarted;
        public event Action<bool> OnSyncFinished;

        private bool _wasOnline;
        private float _nextCheckTime;
        private float _lastQuestionSyncTime = -999f;

        public string EffectiveApiBaseUrl
        {
            get
            {
                string overrideUrl = PlayerPrefs.GetString(ApiUrlPrefKey, string.Empty);
                string raw = !string.IsNullOrWhiteSpace(overrideUrl) ? overrideUrl : apiBaseUrl;
#if UNITY_ANDROID && !UNITY_EDITOR
                // On a physical Android phone, localhost refers to the phone itself; rewrite to PC LAN IP if not overridden.
                if (raw.Contains("localhost") || raw.Contains("127.0.0.1"))
                {
                    raw = "http://192.168.1.55:5000/api/v1";
                }
#endif
                return raw.TrimEnd('/');
            }
        }

        public static void SetCustomApiBaseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                PlayerPrefs.DeleteKey(ApiUrlPrefKey);
            }
            else
            {
                PlayerPrefs.SetString(ApiUrlPrefKey, url.Trim().TrimEnd('/'));
            }
            PlayerPrefs.Save();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (questionManager == null)
            {
                questionManager = FindFirstObjectByType<QuestionManager>();
            }
        }

        private void OnEnable()
        {
            ProgressManager.OnBattleRecorded += HandleBattleRecorded;
        }

        private void OnDisable()
        {
            ProgressManager.OnBattleRecorded -= HandleBattleRecorded;
        }

        private void Start()
        {
            _wasOnline = IsOnline;
            _nextCheckTime = Time.unscaledTime + reachabilityCheckInterval;

            if (syncOnStart && _wasOnline)
            {
                TriggerSync(includeQuestions: true);
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextCheckTime) return;
            _nextCheckTime = Time.unscaledTime + reachabilityCheckInterval;

            bool onlineNow = IsOnline;
            if (onlineNow != _wasOnline)
            {
                _wasOnline = onlineNow;
                Debug.Log($"[SyncManager] Network status changed: {(onlineNow ? "ONLINE" : "OFFLINE")}");
                OnNetworkReachabilityChanged?.Invoke(onlineNow);

                if (onlineNow)
                {
                    TriggerSync(includeQuestions: true);
                    return;
                }
            }

            // If we are online and still have unsynced battle scores queued, flush them in the background.
            if (onlineNow && !IsSyncing && PendingCount > 0)
            {
                bool shouldCheckQuestions = Time.unscaledTime - _lastQuestionSyncTime >= questionSyncCooldown;
                TriggerSync(includeQuestions: shouldCheckQuestions);
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && IsOnline && !IsSyncing)
            {
                bool shouldCheckQuestions = Time.unscaledTime - _lastQuestionSyncTime >= questionSyncCooldown;
                if (PendingCount > 0 || shouldCheckQuestions)
                {
                    TriggerSync(includeQuestions: shouldCheckQuestions);
                }
            }
        }

        private void HandleBattleRecorded(PendingScore _)
        {
            if (syncAfterEachBattle && IsOnline)
            {
                TriggerSync(includeQuestions: false);
            }
        }

        /// <summary>
        /// Starts a non-blocking background sync of pending progress events and (optionally) OTA questions.
        /// Safe to call at any time; no-ops if offline or already syncing.
        /// </summary>
        [ContextMenu("Force Cloud Sync Now")]
        public void ForceSyncAll() => TriggerSync(includeQuestions: true);

        public void TriggerSync(bool includeQuestions = true)
        {
            if (!isActiveAndEnabled || IsSyncing || !IsOnline) return;
            StartCoroutine(SyncAllRoutine(includeQuestions));
        }

        private IEnumerator SyncAllRoutine(bool includeQuestions)
        {
            IsSyncing = true;
            LastError = null;
            OnSyncStarted?.Invoke();

            bool ok = true;

            if (PendingCount > 0)
            {
                yield return PushPendingProgressRoutine(success => ok &= success);
            }

            if (includeQuestions && questionManager != null)
            {
                _lastQuestionSyncTime = Time.unscaledTime;
                yield return PullQuestionsRoutine(success => ok &= success);
            }

            IsSyncing = false;
            OnSyncFinished?.Invoke(ok);
        }

        private IEnumerator PushPendingProgressRoutine(Action<bool> onComplete)
        {
            var progress = ProgressManager.Current;
            var snapshot = progress.pendingSync
                .Where(s => s != null && !string.IsNullOrEmpty(s.localId))
                .ToList();

            if (snapshot.Count == 0)
            {
                onComplete?.Invoke(true);
                yield break;
            }

            string clientHash = ComputeClientHash(progress.deviceId);
            var payload = new SyncProgressRequest
            {
                events = snapshot.Select(s => new SyncProgressEventDto
                {
                    eventId = s.localId,
                    timestamp = DateTimeOffset.FromUnixTimeSeconds(
                        s.timestampUtc > 0 ? s.timestampUtc : DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    ).UtcDateTime.ToString("o"),
                    type = s.victory ? "STAGE_CLEAR" : "BATTLE_END",
                    stageId = $"stage-{s.stageIndex + 1}",
                    xpGained = s.xpGained,
                    questionsAnswered = s.totalAnswers,
                    correctAnswers = s.correctAnswers,
                    clientHash = clientHash,
                }).ToArray()
            };

            string jsonBody = JsonUtility.ToJson(payload);
            string url = $"{EffectiveApiBaseUrl}/sync/progress";

            using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = requestTimeoutSeconds;
            req.SetRequestHeader("Content-Type", "application/json");
            AttachAuthHeaderIfPresent(req);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                LastError = $"Progress sync failed ({req.responseCode}): {req.error}";
                Debug.LogWarning($"[SyncManager] {LastError} (events remain safely queued offline).");
                onComplete?.Invoke(false);
                yield break;
            }

            try
            {
                var response = JsonUtility.FromJson<SyncProgressResponse>(req.downloadHandler.text);
                int rank = response?.globalRank ?? 0;
                ProgressManager.MarkScoresSynced(snapshot.Select(s => s.localId), rank);
                Debug.Log($"[SyncManager] Synced {snapshot.Count} battle event(s). Server Total XP: {response?.totalXp}, Global Rank: #{rank}");
                onComplete?.Invoke(true);
            }
            catch (Exception e)
            {
                LastError = $"Failed to parse sync response: {e.Message}";
                Debug.LogWarning($"[SyncManager] {LastError}");
                onComplete?.Invoke(false);
            }
        }

        private IEnumerator PullQuestionsRoutine(Action<bool> onComplete)
        {
            string url = $"{EffectiveApiBaseUrl}/questions";
            using var req = UnityWebRequest.Get(url);
            req.timeout = requestTimeoutSeconds;
            AttachAuthHeaderIfPresent(req);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                LastError = $"Question sync failed ({req.responseCode}): {req.error}";
                Debug.LogWarning($"[SyncManager] {LastError} (using local cached/bundled questions).");
                onComplete?.Invoke(false);
                yield break;
            }

            string responseJson = req.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                onComplete?.Invoke(false);
                yield break;
            }

            // If the remote DB has 0 approved questions yet, keep our local bundled questions active.
            if (!QuestionManager.TryParse(responseJson, out var bank, out _))
            {
                Debug.Log("[SyncManager] Remote question bank is empty or unchanged; keeping local questions active.");
                onComplete?.Invoke(true);
                yield break;
            }

            bool applied = questionManager.SaveToCacheAndApply(responseJson);
            onComplete?.Invoke(applied);
        }

        private static void AttachAuthHeaderIfPresent(UnityWebRequest req)
        {
            string token = PlayerPrefs.GetString(AuthTokenPrefKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(token))
            {
                req.SetRequestHeader("Authorization", $"Bearer {token}");
            }
        }

        public static string ComputeClientHash(string deviceId)
        {
            string input = $"{deviceId ?? "unknown-device"}:{HmacSalt}";
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        [Serializable]
        private class SyncProgressRequest
        {
            public SyncProgressEventDto[] events;
        }

        [Serializable]
        private class SyncProgressEventDto
        {
            public string eventId;
            public string timestamp;
            public string type;
            public string stageId;
            public int xpGained;
            public int questionsAnswered;
            public int correctAnswers;
            public string clientHash;
        }

        [Serializable]
        private class SyncProgressResponse
        {
            public string status;
            public int processedEvents;
            public int currentLevel;
            public int totalXp;
            public int globalRank;
        }
    }
}

