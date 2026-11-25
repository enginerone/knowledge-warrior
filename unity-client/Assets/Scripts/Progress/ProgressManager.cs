using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KnowledgeWarrior.Progress
{
    /// <summary>
    /// In-memory session snapshot of the authenticated player's live PostgreSQL profile
    /// and per-subject progression (starting at Level 1 up to Level 100).
    /// </summary>
    [Serializable]
    public class PlayerProgress
    {
        public string playerName = "Knowledge Warrior";
        public int level = 1;
        public int totalXp;
        public int globalRank;
        public int battlesWon;
        public int battlesLost;
        public int questionsAnswered;
        public int correctAnswers;
        public int bestStage;
        public List<SubjectProgressData> subjectProgress = new();
    }

    /// <summary>
    /// Maintains the live online session progression state hydrated from the backend server.
    /// Does not persist offline save files or offline sync queues.
    /// </summary>
    public static class ProgressManager
    {
        private const string LegacySaveFileName = "progress.json";
        public const int MaxLevel = SubjectTierSystem.MaxLevel;

        private static readonly string[] DefaultSubjects =
        {
            "Programming",
            "Web Development",
            "Networking",
            "Databases",
            "Cybersecurity",
            "Computer Systems"
        };

        private static PlayerProgress _current;
        private static bool _legacyFilesPurged;

        public static PlayerProgress Current
        {
            get
            {
                if (!_legacyFilesPurged)
                {
                    PurgeLegacyLocalSaveFiles();
                }
                if (_current == null)
                {
                    _current = CreateCleanSessionState();
                }
                return _current;
            }
        }

        public static int XpToNextLevel(int level) => 100 + (Mathf.Clamp(level, 1, MaxLevel) - 1) * 40;
        public static int MaxHpForLevel(int level) => 100 + (Mathf.Clamp(level, 1, MaxLevel) - 1) * 14;
        public static int AttackForLevel(int level) => 20 + (Mathf.Clamp(level, 1, MaxLevel) - 1) * 3;

        /// <summary>
        /// Deletes any legacy offline progress.json files so stale or jumped local levels never override PostgreSQL.
        /// </summary>
        public static void PurgeLegacyLocalSaveFiles()
        {
            _legacyFilesPurged = true;
            try
            {
                string legacyPath = Path.Combine(Application.persistentDataPath, LegacySaveFileName);
                if (File.Exists(legacyPath)) File.Delete(legacyPath);
                if (File.Exists(legacyPath + ".tmp")) File.Delete(legacyPath + ".tmp");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ProgressManager] Could not remove legacy local save file: {e.Message}");
            }
        }

        public static SubjectProgressData GetSubjectProgress(string subject)
        {
            string normalized = NormalizeSubjectName(subject);
            var list = Current.subjectProgress;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].subject, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    list[i].level = Mathf.Clamp(list[i].level, 1, MaxLevel);
                    list[i].xp = Mathf.Max(0, list[i].xp);
                    list[i].points = Mathf.Max(0, list[i].points);
                    return list[i];
                }
            }

            var created = new SubjectProgressData
            {
                subject = normalized,
                level = 1,
                xp = 0,
                points = 0
            };
            list.Add(created);
            return created;
        }

        public static int GetSubjectLevel(string subject) => GetSubjectProgress(subject).level;

        public static int GetSubjectPoints(string subject) => GetSubjectProgress(subject).points;

        public static BadgeTier GetSubjectBadgeTier(string subject) =>
            SubjectTierSystem.GetTierForLevel(GetSubjectLevel(subject));

        public static void ApplyServerPlayerSummary(
            string playerName,
            int currentLevel,
            int totalXp,
            int globalRank,
            int battlesWon,
            int battlesLost,
            int questionsAnswered,
            int correctAnswers)
        {
            var p = Current;
            if (!string.IsNullOrWhiteSpace(playerName))
            {
                p.playerName = playerName.Trim();
            }
            p.level = Mathf.Clamp(currentLevel, 1, MaxLevel);
            p.totalXp = Mathf.Max(0, totalXp);
            if (globalRank > 0) p.globalRank = globalRank;
            p.battlesWon = Mathf.Max(0, battlesWon);
            p.battlesLost = Mathf.Max(0, battlesLost);
            p.questionsAnswered = Mathf.Max(0, questionsAnswered);
            p.correctAnswers = Mathf.Max(0, correctAnswers);
        }

        public static void SetSubjectProgressFromServer(string subject, int level, int xp, int points)
        {
            var sp = GetSubjectProgress(subject);
            sp.level = Mathf.Clamp(level, 1, MaxLevel);
            sp.xp = sp.level >= MaxLevel
                ? 0
                : Mathf.Clamp(xp, 0, Mathf.Max(0, SubjectTierSystem.XpToNextSubjectLevel(sp.level) - 1));
            sp.points = Mathf.Max(0, points);
        }

        public static void RecordStageClearedInSession(int stageIndex)
        {
            Current.bestStage = Mathf.Max(Current.bestStage, stageIndex + 1);
        }

        public static void ResetSessionState()
        {
            PurgeLegacyLocalSaveFiles();
            _current = CreateCleanSessionState();
        }

        public static void ResetProgress()
        {
            ResetSessionState();
            PlayerPrefs.DeleteKey(SyncManager.AuthTokenPrefKey);
            PlayerPrefs.DeleteKey(AuthManager.UsernamePrefKey);
            PlayerPrefs.DeleteKey(AuthManager.EmailPrefKey);
            PlayerPrefs.DeleteKey(AuthManager.UserIdPrefKey);
            PlayerPrefs.Save();
        }

        private static PlayerProgress CreateCleanSessionState()
        {
            var state = new PlayerProgress
            {
                level = 1,
                totalXp = 0,
                subjectProgress = new List<SubjectProgressData>()
            };

            foreach (string subj in DefaultSubjects)
            {
                state.subjectProgress.Add(new SubjectProgressData
                {
                    subject = subj,
                    level = 1,
                    xp = 0,
                    points = 0
                });
            }

            return state;
        }

        private static string NormalizeSubjectName(string subject)
        {
            if (string.IsNullOrWhiteSpace(subject)) return "Programming";
            return subject.Trim();
        }
    }
}
