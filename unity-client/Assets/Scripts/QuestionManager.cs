using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KnowledgeWarrior.Progress;
using UnityEngine;
using Random = System.Random;

namespace KnowledgeWarrior.Data
{
    /// <summary>
    /// Loads, validates, and serves questions aligned with the player's subject and level tier
    /// (Beginner Lv 1-15, Intermediate Lv 16-30, Advanced Lv 31-60, Expert Lv 61-100).
    /// </summary>
    public class QuestionManager : MonoBehaviour
    {
        public const string CacheFileName = "questions_cache.json";

        [Tooltip("The questions.json bundled inside the build (offline fallback).")]
        [SerializeField] private TextAsset bundledQuestions;

        [Tooltip("Shuffle the order of the 4 choices every time a question is shown.")]
        [SerializeField] private bool shuffleChoices = true;

        private readonly List<QuestionData> _questions = new();
        private readonly Dictionary<string, Queue<QuestionData>> _decks = new();
        private readonly Random _rng = new();
        private string _lastQuestionId;
        private string _lastQuestionNormalizedText;

        public static string CachePath => Path.Combine(Application.persistentDataPath, CacheFileName);

        public int Count => _questions.Count;
        public int BankVersion { get; private set; }
        public string BankVersionHash { get; private set; }
        public string LoadedFrom { get; private set; }
        public IReadOnlyList<QuestionData> All => _questions;

        public IReadOnlyList<string> Subjects =>
            _questions.Select(q => q.subject).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s).ToList();

        public event Action OnQuestionsLoaded;

        private void Awake()
        {
            Load();
        }

        /// <summary>Loads questions from local OTA cache first, falling back to the bundled file.</summary>
        public void Load()
        {
            if (File.Exists(CachePath))
            {
                try
                {
                    if (TryApply(File.ReadAllText(CachePath), "cache")) return;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[QuestionManager] Could not read cache, falling back to bundled questions. {e.Message}");
                }
            }

            if (bundledQuestions != null && TryApply(bundledQuestions.text, "bundled")) return;

            Debug.LogError("[QuestionManager] No questions could be loaded! Assign 'Bundled Questions' in the Inspector.");
        }

        /// <summary>
        /// Validates and saves a freshly downloaded bank to disk, then activates it.
        /// </summary>
        public bool SaveToCacheAndApply(string json)
        {
            if (!TryParse(json, out var bank, out var error))
            {
                Debug.LogWarning($"[QuestionManager] Rejected downloaded questions: {error}");
                return false;
            }

            if (LoadedFrom == "cache" &&
                !string.IsNullOrEmpty(bank.versionHash) &&
                string.Equals(bank.versionHash, BankVersionHash, StringComparison.Ordinal))
            {
                return true;
            }

            string tmp = CachePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(CachePath)) File.Delete(CachePath);
            File.Move(tmp, CachePath);

            Apply(bank, "cache");
            return true;
        }

        /// <summary>
        /// Draws the next question strictly aligned with the player's subject and current level tier:
        /// Beginner (Lv 1-15) -> Difficulty 1 only,
        /// Intermediate (Lv 16-30) -> Difficulty 2 only,
        /// Advanced (Lv 31-60) -> Difficulty 3 only,
        /// Expert (Lv 61-100) -> Difficulty 4 only.
        /// </summary>
        public QuestionData GetQuestionForSubjectAndLevel(string subject, int subjectLevel)
        {
            int exactTierDifficulty = SubjectTierSystem.GetAlignedQuestionDifficulty(subjectLevel);
            return GetNextQuestion(subject, exactTierDifficulty, exactDifficultyOnly: true);
        }

        /// <summary>
        /// Draws the next question matching the subject and difficulty filter.
        /// </summary>
        public QuestionData GetNextQuestion(string subject = null, int targetDifficulty = 1, bool exactDifficultyOnly = true)
        {
            if (_questions.Count == 0) return null;

            string key = $"{subject ?? "*"}|{targetDifficulty}|{exactDifficultyOnly}";
            if (!_decks.TryGetValue(key, out var deck) || deck.Count == 0)
            {
                List<QuestionData> pool;
                if (exactDifficultyOnly)
                {
                    pool = _questions.Where(q =>
                        (string.IsNullOrEmpty(subject) || string.Equals(q.subject, subject, StringComparison.OrdinalIgnoreCase)) &&
                        q.difficulty == targetDifficulty).ToList();
                }
                else
                {
                    pool = _questions.Where(q =>
                        (string.IsNullOrEmpty(subject) || string.Equals(q.subject, subject, StringComparison.OrdinalIgnoreCase)) &&
                        q.difficulty <= targetDifficulty).ToList();
                }

                // Fallback 1: Same subject, closest difficulty
                if (pool.Count == 0 && !string.IsNullOrEmpty(subject))
                {
                    pool = _questions
                        .Where(q => string.Equals(q.subject, subject, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(q => Mathf.Abs(q.difficulty - targetDifficulty))
                        .ToList();
                }

                // Fallback 2: Any subject at exact difficulty
                if (pool.Count == 0)
                {
                    pool = _questions.Where(q => q.difficulty == targetDifficulty).ToList();
                }

                // Fallback 3: Whole bank
                if (pool.Count == 0)
                {
                    pool = new List<QuestionData>(_questions);
                }

                Shuffle(pool);
                if (pool.Count > 1 &&
                    (pool[0].id == _lastQuestionId ||
                     NormalizeQuestionText(pool[0].question) == _lastQuestionNormalizedText))
                {
                    (pool[0], pool[pool.Count - 1]) = (pool[pool.Count - 1], pool[0]);
                }

                deck = new Queue<QuestionData>(pool);
                _decks[key] = deck;
            }

            var next = deck.Dequeue();
            _lastQuestionId = next.id;
            _lastQuestionNormalizedText = NormalizeQuestionText(next.question);
            return shuffleChoices ? next.CreateShuffledCopy(_rng) : next;
        }

        /// <summary>Parses and validates JSON. Invalid individual questions are skipped with a warning.</summary>
        public static bool TryParse(string json, out QuestionBank bank, out string error)
        {
            bank = null;
            if (string.IsNullOrWhiteSpace(json)) { error = "file is empty"; return false; }

            try
            {
                bank = JsonUtility.FromJson<QuestionBank>(json);
            }
            catch (Exception e)
            {
                error = $"invalid JSON: {e.Message}";
                return false;
            }

            if (bank?.questions == null || bank.questions.Length == 0)
            {
                error = "no 'questions' array found";
                return false;
            }

            var valid = new List<QuestionData>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenNormalizedTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var q in bank.questions)
            {
                if (q == null) continue;
                if (!q.IsValid(out var qError))
                {
                    Debug.LogWarning($"[QuestionManager] Skipping question '{q.id}': {qError}");
                    continue;
                }
                if (!seenIds.Add(q.id))
                {
                    Debug.LogWarning($"[QuestionManager] Skipping duplicate id '{q.id}'");
                    continue;
                }
                string normText = NormalizeQuestionText(q.question);
                if (!string.IsNullOrEmpty(normText) && !seenNormalizedTexts.Add(normText))
                {
                    Debug.LogWarning($"[QuestionManager] Skipping duplicate question text '{q.id}'");
                    continue;
                }
                valid.Add(q);
            }

            if (valid.Count == 0) { error = "no valid questions"; return false; }

            bank.questions = valid.ToArray();
            error = null;
            return true;
        }

        private bool TryApply(string json, string source)
        {
            if (!TryParse(json, out var bank, out var error))
            {
                Debug.LogWarning($"[QuestionManager] {source} questions invalid: {error}");
                return false;
            }
            Apply(bank, source);
            return true;
        }

        private void Apply(QuestionBank bank, string source)
        {
            _questions.Clear();
            _questions.AddRange(bank.questions);
            _decks.Clear();
            BankVersion = bank.version;
            BankVersionHash = bank.versionHash;
            LoadedFrom = source;
            string versionLabel = !string.IsNullOrEmpty(BankVersionHash)
                ? $"hash {BankVersionHash.Substring(0, Math.Min(8, BankVersionHash.Length))}"
                : $"v{BankVersion}";
            Debug.Log($"[QuestionManager] Loaded {_questions.Count} questions ({versionLabel}) from {source}.");
            OnQuestionsLoaded?.Invoke();
        }

        private void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private static string NormalizeQuestionText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var chars = text.Trim().ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
            return string.Join(" ", new string(chars).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
