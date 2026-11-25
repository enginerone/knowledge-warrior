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
    /// Online-only question bank manager that stores approved curriculum questions fetched live
    /// from the Knowledge Warrior server, permanently excludes any question once it has appeared
    /// to the authenticated player, and locks each subject strictly to its own subject pool.
    /// </summary>
    public class QuestionManager : MonoBehaviour
    {
        private const string LegacyCacheFileName = "questions_cache.json";

        [Tooltip("Shuffle the order of the 4 choices every time a question is shown.")]
        [SerializeField] private bool shuffleChoices = true;

        private readonly List<QuestionData> _questions = new();
        private readonly Dictionary<string, Queue<QuestionData>> _decks = new();
        private readonly HashSet<string> _seenQuestionIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _seenNormalizedTexts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Random _rng = new();

        public int Count => _questions.Count;
        public int SeenCount => _seenQuestionIds.Count;
        public int BankVersion { get; private set; }
        public string BankVersionHash { get; private set; }
        public string LoadedFrom { get; private set; } = "unloaded";
        public IReadOnlyList<QuestionData> All => _questions;

        public IReadOnlyList<string> Subjects =>
            _questions.Select(q => q.subject).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s).ToList();

        public event Action OnQuestionsLoaded;

        private void Awake()
        {
            PurgeLegacyOfflineQuestionCache();
        }

        public static void PurgeLegacyOfflineQuestionCache()
        {
            try
            {
                string legacyPath = Path.Combine(Application.persistentDataPath, LegacyCacheFileName);
                if (File.Exists(legacyPath)) File.Delete(legacyPath);
                if (File.Exists(legacyPath + ".tmp")) File.Delete(legacyPath + ".tmp");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[QuestionManager] Could not remove legacy question cache: {e.Message}");
            }
        }

        public void ResetSeenQuestionsForLogout()
        {
            _seenQuestionIds.Clear();
            _seenNormalizedTexts.Clear();
            _decks.Clear();
        }

        public void SetSeenQuestionIds(IEnumerable<string> questionIds, bool resetExisting = false)
        {
            if (resetExisting)
            {
                _seenQuestionIds.Clear();
                _seenNormalizedTexts.Clear();
            }

            if (questionIds != null)
            {
                foreach (string rawId in questionIds)
                {
                    if (!string.IsNullOrWhiteSpace(rawId))
                    {
                        _seenQuestionIds.Add(rawId.Trim());
                    }
                }
            }

            SyncSeenNormalizedTextsWithLoadedBank();
            _decks.Clear();
            OnQuestionsLoaded?.Invoke();
        }

        public bool IsQuestionSeen(QuestionData q)
        {
            if (q == null) return true;
            if (!string.IsNullOrWhiteSpace(q.id) && _seenQuestionIds.Contains(q.id)) return true;
            string norm = NormalizeQuestionText(q.question);
            return !string.IsNullOrEmpty(norm) && _seenNormalizedTexts.Contains(norm);
        }

        public int CountUnseenForSubject(string subject)
        {
            if (string.IsNullOrWhiteSpace(subject)) return 0;
            return _questions.Count(q =>
                string.Equals(q.subject, subject, StringComparison.OrdinalIgnoreCase) &&
                !IsQuestionSeen(q));
        }

        public int CountUnseenForSubjectAndLevel(string subject, int subjectLevel)
        {
            if (string.IsNullOrWhiteSpace(subject)) return 0;
            int exactTierDifficulty = SubjectTierSystem.GetAlignedQuestionDifficulty(subjectLevel);
            return _questions.Count(q =>
                string.Equals(q.subject, subject, StringComparison.OrdinalIgnoreCase) &&
                q.difficulty == exactTierDifficulty &&
                !IsQuestionSeen(q));
        }

        public bool ApplyOnlineQuestionsJson(string json, out string error)
        {
            if (!TryParse(json, out var bank, out error))
            {
                Debug.LogWarning($"[QuestionManager] Rejected server question payload: {error}");
                return false;
            }

            if (bank.seenQuestionIds != null)
            {
                foreach (string seenId in bank.seenQuestionIds)
                {
                    if (!string.IsNullOrWhiteSpace(seenId))
                    {
                        _seenQuestionIds.Add(seenId.Trim());
                    }
                }
            }

            Apply(bank, "online-server");
            return true;
        }

        public void ClearLoadedQuestions()
        {
            _questions.Clear();
            _decks.Clear();
            BankVersion = 0;
            BankVersionHash = null;
            LoadedFrom = "unloaded";
        }

        /// <summary>
        /// Draws a unique, never-before-seen question strictly belonging to <paramref name="subject"/>
        /// and aligned to <paramref name="subjectLevel"/>. Once returned, the question is permanently
        /// marked seen and will never be returned again.
        /// </summary>
        public QuestionData GetQuestionForSubjectAndLevel(string subject, int subjectLevel)
        {
            int exactTierDifficulty = SubjectTierSystem.GetAlignedQuestionDifficulty(subjectLevel);
            return GetNextQuestion(subject, exactTierDifficulty, exactDifficultyOnly: true);
        }

        public QuestionData GetNextQuestion(string subject, int targetDifficulty = 1, bool exactDifficultyOnly = true)
        {
            if (_questions.Count == 0 || string.IsNullOrWhiteSpace(subject)) return null;

            string trimmedSubject = subject.Trim();
            string key = $"{trimmedSubject.ToLowerInvariant()}|{targetDifficulty}|{exactDifficultyOnly}";

            if (_decks.TryGetValue(key, out var existingDeck))
            {
                while (existingDeck.Count > 0 && IsQuestionSeen(existingDeck.Peek()))
                {
                    existingDeck.Dequeue();
                }
            }

            if (!_decks.TryGetValue(key, out var deck) || deck.Count == 0)
            {
                List<QuestionData> pool;
                if (exactDifficultyOnly)
                {
                    pool = _questions.Where(q =>
                        string.Equals(q.subject, trimmedSubject, StringComparison.OrdinalIgnoreCase) &&
                        q.difficulty == targetDifficulty &&
                        !IsQuestionSeen(q)).ToList();
                }
                else
                {
                    pool = _questions.Where(q =>
                        string.Equals(q.subject, trimmedSubject, StringComparison.OrdinalIgnoreCase) &&
                        q.difficulty <= targetDifficulty &&
                        !IsQuestionSeen(q)).ToList();
                }

                if (pool.Count == 0)
                {
                    return null;
                }

                Shuffle(pool);
                deck = new Queue<QuestionData>(pool);
                _decks[key] = deck;
            }

            var next = deck.Dequeue();
            MarkQuestionSeenLocally(next);
            return shuffleChoices ? next.CreateShuffledCopy(_rng) : next;
        }

        private void MarkQuestionSeenLocally(QuestionData q)
        {
            if (q == null) return;
            if (!string.IsNullOrWhiteSpace(q.id))
            {
                _seenQuestionIds.Add(q.id.Trim());
            }
            string norm = NormalizeQuestionText(q.question);
            if (!string.IsNullOrEmpty(norm))
            {
                _seenNormalizedTexts.Add(norm);
            }
        }

        private void SyncSeenNormalizedTextsWithLoadedBank()
        {
            foreach (var q in _questions)
            {
                if (q != null && !string.IsNullOrWhiteSpace(q.id) && _seenQuestionIds.Contains(q.id))
                {
                    string norm = NormalizeQuestionText(q.question);
                    if (!string.IsNullOrEmpty(norm))
                    {
                        _seenNormalizedTexts.Add(norm);
                    }
                }
            }
        }

        public static bool TryParse(string json, out QuestionBank bank, out string error)
        {
            bank = null;
            if (string.IsNullOrWhiteSpace(json)) { error = "server question response is empty"; return false; }

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
                error = "no approved questions found on server";
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
                    continue;
                }
                string normText = NormalizeQuestionText(q.question);
                if (!string.IsNullOrEmpty(normText) && !seenNormalizedTexts.Add(normText))
                {
                    continue;
                }
                valid.Add(q);
            }

            if (valid.Count == 0) { error = "no valid approved questions available"; return false; }

            bank.questions = valid.ToArray();
            error = null;
            return true;
        }

        private void Apply(QuestionBank bank, string source)
        {
            _questions.Clear();
            _questions.AddRange(bank.questions);
            SyncSeenNormalizedTextsWithLoadedBank();
            _decks.Clear();
            BankVersion = bank.version;
            BankVersionHash = bank.versionHash;
            LoadedFrom = source;
            string versionLabel = !string.IsNullOrEmpty(BankVersionHash)
                ? $"hash {BankVersionHash.Substring(0, Math.Min(8, BankVersionHash.Length))}"
                : $"v{BankVersion}";
            Debug.Log($"[QuestionManager] Loaded {_questions.Count} live questions ({versionLabel}, {_seenQuestionIds.Count} previously seen excluded) from {source}.");
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
