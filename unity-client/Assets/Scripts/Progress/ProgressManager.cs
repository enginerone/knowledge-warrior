using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KnowledgeWarrior.Progress
{
    /// <summary>A battle result waiting to be pushed to the server sync queue.</summary>
    [Serializable]
    public class PendingScore
    {
        public string localId;
        public string subject;
        public int xpGained;
        public int stageIndex;
        public string enemyName;
        public bool victory;
        public bool isPvpDuel;
        public int correctAnswers;
        public int totalAnswers;
        public long timestampUtc;
    }

    /// <summary>Everything saved on the device. Serialized with JsonUtility.</summary>
    [Serializable]
    public class PlayerProgress
    {
        public string deviceId;
        public string playerName = "Knowledge Warrior";
        public int level = 1;
        public int xp;
        public int totalXp;
        public int globalRank;
        public long lastSyncedUtc;
        public int battlesWon;
        public int battlesLost;
        public int questionsAnswered;
        public int correctAnswers;
        public int bestStage;
        public List<SubjectProgressData> subjectProgress = new();
        public List<PendingScore> pendingSync = new();
    }

    /// <summary>Loads and saves <see cref="PlayerProgress"/> to persistentDataPath and manages global and per-subject levels (1 to 100).</summary>
    public static class ProgressManager
    {
        public const string FileName = "progress.json";
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

        public static event Action<PendingScore> OnBattleRecorded;

        private static PlayerProgress _current;

        public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

        public static PlayerProgress Current
        {
            get
            {
                if (_current == null) Load();
                return _current;
            }
        }

        /// <summary>XP needed to advance from <paramref name="level"/> to level + 1.</summary>
        public static int XpToNextLevel(int level) => 100 + (Mathf.Clamp(level, 1, MaxLevel) - 1) * 40;

        public static int MaxHpForLevel(int level) => 100 + (Mathf.Clamp(level, 1, MaxLevel) - 1) * 14;
        public static int AttackForLevel(int level) => 20 + (Mathf.Clamp(level, 1, MaxLevel) - 1) * 3;

        public static void Load()
        {
            try
            {
                if (File.Exists(SavePath))
                {
                    _current = JsonUtility.FromJson<PlayerProgress>(File.ReadAllText(SavePath));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ProgressManager] Save file unreadable, starting fresh. {e.Message}");
            }

            _current ??= new PlayerProgress();
            _current.pendingSync ??= new List<PendingScore>();
            _current.subjectProgress ??= new List<SubjectProgressData>();

            EnsureDefaultSubjects(_current);

            if (string.IsNullOrWhiteSpace(_current.deviceId))
            {
                string hwId = SystemInfo.deviceUniqueIdentifier;
                _current.deviceId = !string.IsNullOrWhiteSpace(hwId) && hwId != SystemInfo.unsupportedIdentifier
                    ? hwId
                    : Guid.NewGuid().ToString("N");
                Save();
            }
        }

        public static void Save()
        {
            try
            {
                string tmp = SavePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(Current, true));
                if (File.Exists(SavePath)) File.Delete(SavePath);
                File.Move(tmp, SavePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ProgressManager] Failed to save progress: {e.Message}");
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
                    if (list[i].points <= 0) list[i].points = list[i].level * 120;
                    return list[i];
                }
            }

            var created = new SubjectProgressData
            {
                subject = normalized,
                level = 1,
                xp = 0,
                points = 120,
                pvpWins = 0,
                pvpLosses = 0
            };
            list.Add(created);
            return created;
        }

        public static int GetSubjectLevel(string subject) => GetSubjectProgress(subject).level;

        public static int GetSubjectPoints(string subject) => GetSubjectProgress(subject).points;

        public static BadgeTier GetSubjectBadgeTier(string subject) =>
            SubjectTierSystem.GetTierForLevel(GetSubjectLevel(subject));

        /// <summary>Adds XP globally and to the specified subject, returning how many subject levels were gained.</summary>
        public static int AddXp(int amount, string subject = null)
        {
            if (amount <= 0) return 0;
            var p = Current;
            p.totalXp += amount;
            p.xp += amount;

            int globalLevelsGained = 0;
            while (p.level < MaxLevel && p.xp >= XpToNextLevel(p.level))
            {
                p.xp -= XpToNextLevel(p.level);
                p.level++;
                globalLevelsGained++;
            }
            if (p.level >= MaxLevel) p.xp = 0;

            int subjectLevelsGained = 0;
            if (!string.IsNullOrWhiteSpace(subject))
            {
                subjectLevelsGained = AddSubjectXp(subject, amount);
            }

            Save();
            return Mathf.Max(globalLevelsGained, subjectLevelsGained);
        }

        public static int AddSubjectXp(string subject, int amount)
        {
            if (amount <= 0) return 0;
            var sp = GetSubjectProgress(subject);
            sp.points += amount;
            sp.xp += amount;

            int gained = 0;
            while (sp.level < MaxLevel && sp.xp >= SubjectTierSystem.XpToNextSubjectLevel(sp.level))
            {
                sp.xp -= SubjectTierSystem.XpToNextSubjectLevel(sp.level);
                sp.level++;
                gained++;
            }
            if (sp.level >= MaxLevel) sp.xp = 0;
            Save();
            return gained;
        }

        /// <summary>
        /// Applies the 20% point deduction rule when losing a 1v1 real-user battle in the given subject.
        /// </summary>
        public static int ApplyPvpTwentyPercentDeduction(string subject)
        {
            var sp = GetSubjectProgress(subject);
            int deduction = SubjectTierSystem.CalculateTwentyPercentDeduction(sp.points);
            sp.points = Mathf.Max(0, sp.points - deduction);
            sp.pvpLosses++;

            var p = Current;
            p.totalXp = Mathf.Max(0, p.totalXp - deduction);
            Save();
            return deduction;
        }

        public static void RecordPvpWin(string subject, int bonusPoints)
        {
            var sp = GetSubjectProgress(subject);
            sp.pvpWins++;
            AddXp(bonusPoints, subject);
        }

        public static void SetSubjectProgressFromServer(string subject, int level, int points, int pvpWins, int pvpLosses)
        {
            var sp = GetSubjectProgress(subject);
            sp.level = Mathf.Clamp(level, 1, MaxLevel);
            sp.points = Mathf.Max(0, points);
            sp.pvpWins = Mathf.Max(sp.pvpWins, pvpWins);
            sp.pvpLosses = Mathf.Max(sp.pvpLosses, pvpLosses);
            Save();
        }

        /// <summary>Records a battle result locally and queues it for upload when online.</summary>
        public static void RecordBattle(PendingScore score)
        {
            var p = Current;
            if (score.victory) p.battlesWon++; else p.battlesLost++;
            p.questionsAnswered += score.totalAnswers;
            p.correctAnswers += score.correctAnswers;
            if (score.victory) p.bestStage = Mathf.Max(p.bestStage, score.stageIndex + 1);

            score.localId = Guid.NewGuid().ToString("N");
            score.timestampUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            p.pendingSync.Add(score);
            Save();
            OnBattleRecorded?.Invoke(score);
        }

        /// <summary>Removes scores that were acknowledged by the backend sync endpoint and updates rank metadata.</summary>
        public static void MarkScoresSynced(IEnumerable<string> syncedLocalIds, int globalRank = 0)
        {
            var p = Current;
            var idSet = new HashSet<string>(syncedLocalIds);
            p.pendingSync.RemoveAll(s => s != null && idSet.Contains(s.localId));
            if (globalRank > 0) p.globalRank = globalRank;
            p.lastSyncedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Save();
        }

        /// <summary>Wipes local progress and clears any saved authentication session.</summary>
        public static void ResetProgress()
        {
            _current = new PlayerProgress();
            EnsureDefaultSubjects(_current);
            PlayerPrefs.DeleteKey(SyncManager.AuthTokenPrefKey);
            PlayerPrefs.DeleteKey(AuthManager.UsernamePrefKey);
            PlayerPrefs.DeleteKey(AuthManager.EmailPrefKey);
            PlayerPrefs.DeleteKey(AuthManager.UserIdPrefKey);
            PlayerPrefs.Save();
            Save();
        }

        private static void EnsureDefaultSubjects(PlayerProgress progress)
        {
            foreach (string subj in DefaultSubjects)
            {
                bool exists = false;
                for (int i = 0; i < progress.subjectProgress.Count; i++)
                {
                    if (string.Equals(progress.subjectProgress[i].subject, subj, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    progress.subjectProgress.Add(new SubjectProgressData
                    {
                        subject = subj,
                        level = 1,
                        xp = 0,
                        points = 120,
                        pvpWins = 0,
                        pvpLosses = 0
                    });
                }
            }
        }

        private static string NormalizeSubjectName(string subject)
        {
            if (string.IsNullOrWhiteSpace(subject)) return "Programming";
            return subject.Trim();
        }
    }
}
