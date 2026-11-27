using System;
using System.IO;
using KnowledgeWarrior.Data;
using UnityEngine;

namespace KnowledgeWarrior.Progress
{
    /// <summary>
    /// Manages Limited-Access Offline Practice Mode so players without an internet signal
    /// can practice cached subject questions and read explanations without modifying online XP,
    /// levels, coins, or leaderboard progression.
    /// </summary>
    public static class OfflinePracticeManager
    {
        private const string OfflineCacheFileName = "offline_practice_bank.json";

        public static bool IsOfflinePracticeMode { get; private set; }
        public static event Action<bool> OnOfflinePracticeModeChanged;

        private static string CacheFilePath =>
            Path.Combine(Application.persistentDataPath, OfflineCacheFileName);

        public static void EnterOfflinePracticeMode()
        {
            if (IsOfflinePracticeMode) return;
            IsOfflinePracticeMode = true;
            OnOfflinePracticeModeChanged?.Invoke(true);
        }

        public static void ExitOfflinePracticeMode()
        {
            if (!IsOfflinePracticeMode) return;
            IsOfflinePracticeMode = false;
            OnOfflinePracticeModeChanged?.Invoke(false);
        }

        public static void SaveOfflinePracticeQuestionBank(string rawQuestionsJson)
        {
            if (string.IsNullOrWhiteSpace(rawQuestionsJson)) return;
            try
            {
                File.WriteAllText(CacheFilePath, rawQuestionsJson);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OfflinePracticeManager] Failed to cache practice question bank: {ex.Message}");
            }
        }

        public static bool TryLoadOfflinePracticeQuestions(QuestionManager questionManager, out string sourceLabel)
        {
            sourceLabel = "unavailable";
            if (questionManager == null) return false;

            if (File.Exists(CacheFilePath))
            {
                try
                {
                    string cachedJson = File.ReadAllText(CacheFilePath);
                    if (questionManager.ApplyOfflinePracticeQuestionsJson(cachedJson, out _))
                    {
                        sourceLabel = "local practice cache";
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[OfflinePracticeManager] Could not read cached practice bank: {ex.Message}");
                }
            }

            string bundledPath = Path.Combine(Application.dataPath, "Data", "questions.json");
            if (File.Exists(bundledPath))
            {
                try
                {
                    string bundledJson = File.ReadAllText(bundledPath);
                    if (questionManager.ApplyOfflinePracticeQuestionsJson(bundledJson, out _))
                    {
                        sourceLabel = "bundled practice bank";
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[OfflinePracticeManager] Could not read bundled questions: {ex.Message}");
                }
            }

            var resourceAsset = Resources.Load<TextAsset>("questions");
            if (resourceAsset != null && !string.IsNullOrWhiteSpace(resourceAsset.text))
            {
                if (questionManager.ApplyOfflinePracticeQuestionsJson(resourceAsset.text, out _))
                {
                    sourceLabel = "embedded resources";
                    return true;
                }
            }

            return false;
        }
    }
}
