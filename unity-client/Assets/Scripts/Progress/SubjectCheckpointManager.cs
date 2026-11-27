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
    /// Stores independent per-subject battle checkpoints so players can quit a subject battle,
    /// enter another subject lobby, and resume right where they exited later.
    /// </summary>
    public static class SubjectCheckpointManager
    {
        private static readonly Dictionary<string, SubjectBattleCheckpoint> Checkpoints =
            new(StringComparer.OrdinalIgnoreCase);

        private static string PrefKeyPrefix =>
            (SyncManager.IsLocalDevelopmentEnvironment ? "kw_local_cp_" : "kw_cloud_cp_") +
            (AuthManager.Instance != null && !string.IsNullOrWhiteSpace(AuthManager.Instance.AuthenticatedUserId)
                ? AuthManager.Instance.AuthenticatedUserId
                : "guest") + "_";

        private static string NormalizeKey(string subject) =>
            string.IsNullOrWhiteSpace(subject) ? "Programming" : subject.Trim();

        private static string BuildPrefKey(string subject) =>
            PrefKeyPrefix + NormalizeKey(subject).ToLowerInvariant().Replace(" ", "_");

        public static void SaveCheckpoint(SubjectBattleCheckpoint checkpoint)
        {
            if (checkpoint == null || string.IsNullOrWhiteSpace(checkpoint.subject)) return;
            string key = NormalizeKey(checkpoint.subject);
            checkpoint.subject = key;
            checkpoint.stageIndex = Mathf.Clamp(checkpoint.stageIndex, 0, 3);
            checkpoint.hasCheckpoint = true;
            Checkpoints[key] = checkpoint;

            try
            {
                PlayerPrefs.SetString(BuildPrefKey(key), JsonUtility.ToJson(checkpoint));
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
                ClearLocalOnly(key);
                return;
            }

            if (TryGetCheckpoint(key, out var local) &&
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
            Checkpoints[key] = hydrated;
            try
            {
                PlayerPrefs.SetString(BuildPrefKey(key), JsonUtility.ToJson(hydrated));
                PlayerPrefs.Save();
            }
            catch
            {
                // Ignore storage errors for hydrated server checkpoints
            }
        }

        public static bool TryGetCheckpoint(string subject, out SubjectBattleCheckpoint checkpoint)
        {
            string key = NormalizeKey(subject);
            if (Checkpoints.TryGetValue(key, out checkpoint) && checkpoint != null && checkpoint.hasCheckpoint)
            {
                return true;
            }

            string raw = PlayerPrefs.GetString(BuildPrefKey(key), string.Empty);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    var loaded = JsonUtility.FromJson<SubjectBattleCheckpoint>(raw);
                    if (loaded != null && loaded.hasCheckpoint)
                    {
                        Checkpoints[key] = loaded;
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

        public static void ClearCheckpoint(string subject)
        {
            string key = NormalizeKey(subject);
            ClearLocalOnly(key);
        }

        public static void ClearInMemoryForLogout()
        {
            Checkpoints.Clear();
        }

        private static void ClearLocalOnly(string normalizedSubject)
        {
            Checkpoints.Remove(normalizedSubject);
            try
            {
                string prefKey = BuildPrefKey(normalizedSubject);
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
