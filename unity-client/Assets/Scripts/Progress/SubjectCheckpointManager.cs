using System;
using System.Collections.Generic;
using UnityEngine;

namespace KnowledgeWarrior.Progress
{
    [Serializable]
    public class SubjectBattleCheckpoint
    {
        public string subject = "Programming";
        public string biomeTitle = "Citadel of Code";
        public int subjectLevel = 1;
        public int stageIndex;
        public int playerHp;
        public int playerMaxHp;
        public int enemyHp;
        public int enemyMaxHp;
        public int turnType;
        public int streak;
        public int battleCorrect;
        public int battleTotal;
        public int battleXp;
        public string currentQuestionId;
        public bool hasCheckpoint;
    }

    /// <summary>
    /// Stores independent per-subject battle checkpoints separately for Online Mode and
    /// Offline Practice Mode so players can quit, switch subjects, or reopen the game
    /// and resume right where they exited in either mode.
    /// </summary>
    public static class SubjectCheckpointManager
    {
        private static readonly Dictionary<string, SubjectBattleCheckpoint> OnlineCheckpoints =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, SubjectBattleCheckpoint> OfflineCheckpoints =
            new(StringComparer.OrdinalIgnoreCase);

        private static bool ResolveIsOffline(bool? isOfflineOverride) =>
            isOfflineOverride ?? OfflinePracticeManager.IsOfflinePracticeMode;

        private static Dictionary<string, SubjectBattleCheckpoint> GetMap(bool isOffline) =>
            isOffline ? OfflineCheckpoints : OnlineCheckpoints;

        private static string GetPrefKeyPrefix(bool isOffline)
        {
            string envPrefix = SyncManager.IsLocalDevelopmentEnvironment ? "kw_local_" : "kw_cloud_";
            string modeSegment = isOffline ? "offline_cp_" : "cp_";
            string userSegment = AuthManager.Instance != null && !string.IsNullOrWhiteSpace(AuthManager.Instance.AuthenticatedUserId)
                ? AuthManager.Instance.AuthenticatedUserId
                : "guest";
            return $"{envPrefix}{modeSegment}{userSegment}_";
        }

        private static string NormalizeKey(string subject) =>
            string.IsNullOrWhiteSpace(subject) ? "Programming" : subject.Trim();

        private static string BuildPrefKey(string subject, bool isOffline) =>
            GetPrefKeyPrefix(isOffline) + NormalizeKey(subject).ToLowerInvariant().Replace(" ", "_");

        public static void SaveCheckpoint(SubjectBattleCheckpoint checkpoint, bool? isOfflineOverride = null)
        {
            if (checkpoint == null || string.IsNullOrWhiteSpace(checkpoint.subject)) return;
            bool isOffline = ResolveIsOffline(isOfflineOverride);
            string key = NormalizeKey(checkpoint.subject);
            checkpoint.subject = key;
            checkpoint.stageIndex = Mathf.Clamp(checkpoint.stageIndex, 0, 3);
            checkpoint.hasCheckpoint = true;
            GetMap(isOffline)[key] = checkpoint;

            try
            {
                PlayerPrefs.SetString(BuildPrefKey(key, isOffline), JsonUtility.ToJson(checkpoint));
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SubjectCheckpointManager] Could not persist checkpoint: {ex.Message}");
            }
        }

        public static void ApplyServerCheckpoint(
            string subject,
            int stageIndex,
            int playerHp,
            int enemyHp,
            bool hasActiveCheckpoint)
        {
            string key = NormalizeKey(subject);
            if (!hasActiveCheckpoint)
            {
                ClearLocalOnly(key, isOffline: false);
                return;
            }

            if (TryGetCheckpoint(key, out var local, isOfflineOverride: false) &&
                local.hasCheckpoint &&
                local.stageIndex == stageIndex &&
                local.playerHp > 0 &&
                local.enemyHp > 0)
            {
                return;
            }

            var hydrated = new SubjectBattleCheckpoint
            {
                subject = key,
                stageIndex = Mathf.Clamp(stageIndex, 0, 3),
                playerHp = Mathf.Max(0, playerHp),
                enemyHp = Mathf.Max(0, enemyHp),
                hasCheckpoint = true
            };
            OnlineCheckpoints[key] = hydrated;
            try
            {
                PlayerPrefs.SetString(BuildPrefKey(key, isOffline: false), JsonUtility.ToJson(hydrated));
                PlayerPrefs.Save();
            }
            catch
            {
                // Ignore storage errors for hydrated server checkpoints
            }
        }

        public static bool TryGetCheckpoint(
            string subject,
            out SubjectBattleCheckpoint checkpoint,
            bool? isOfflineOverride = null)
        {
            bool isOffline = ResolveIsOffline(isOfflineOverride);
            var map = GetMap(isOffline);
            string key = NormalizeKey(subject);
            if (map.TryGetValue(key, out checkpoint) && checkpoint != null && checkpoint.hasCheckpoint)
            {
                return true;
            }

            string raw = PlayerPrefs.GetString(BuildPrefKey(key, isOffline), string.Empty);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    var loaded = JsonUtility.FromJson<SubjectBattleCheckpoint>(raw);
                    if (loaded != null && loaded.hasCheckpoint)
                    {
                        map[key] = loaded;
                        checkpoint = loaded;
                        return true;
                    }
                }
                catch
                {
                    // Ignore malformed checkpoint JSON
                }
            }

            checkpoint = null;
            return false;
        }

        public static void ClearCheckpoint(string subject, bool? isOfflineOverride = null)
        {
            bool isOffline = ResolveIsOffline(isOfflineOverride);
            string key = NormalizeKey(subject);
            ClearLocalOnly(key, isOffline);
        }

        public static void ClearInMemoryForLogout()
        {
            OnlineCheckpoints.Clear();
        }

        private static void ClearLocalOnly(string normalizedSubject, bool isOffline)
        {
            GetMap(isOffline).Remove(normalizedSubject);
            try
            {
                string prefKey = BuildPrefKey(normalizedSubject, isOffline);
                if (PlayerPrefs.HasKey(prefKey))
                {
                    PlayerPrefs.DeleteKey(prefKey);
                    PlayerPrefs.Save();
                }
            }
            catch
            {
                // Ignore storage cleanup error
            }
        }
    }
}
