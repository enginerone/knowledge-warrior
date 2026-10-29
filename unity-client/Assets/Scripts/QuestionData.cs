using System;
using System.Collections.Generic;

namespace KnowledgeWarrior.Data
{
    /// <summary>
    /// A single combat question supporting multiple interactive game formats:
    /// Multiple Choice, True/False Blitz, Fill-in-the-Blank, Sequence Ordering, and Bug Hunter.
    /// Field names are camelCase to match the JSON produced by the backend and AI ingestion pipeline.
    /// </summary>
    [Serializable]
    public class QuestionData
    {
        public const int ChoiceCount = 4;

        public string id;
        public string subject;
        public string topic;
        public string questionType = "multiple_choice";

        /// <summary>
        /// 1 = Beginner (Levels 1-15), 2 = Intermediate (Levels 16-30),
        /// 3 = Advanced (Levels 31-60), 4 = Expert (Levels 61-100).
        /// </summary>
        public int difficulty = 1;
        public string question;
        public string[] choices;
        public int correctIndex;
        public string explanation;
        public int xpReward = 20;

        public string GetQuestionModeLabel()
        {
            string mode = (questionType ?? "multiple_choice").Trim().ToLowerInvariant();
            return mode switch
            {
                "true_false" => "TRUE / FALSE BLITZ",
                "fill_blank" => "FILL IN THE BLANK",
                "ordering" => "SEQUENCE ORDERING",
                "spot_bug" => "SPOT THE BUG",
                _ => "MULTIPLE CHOICE"
            };
        }

        public string GetAlignedTierLabel() => difficulty switch
        {
            1 => "Beginner (Lv 1-15)",
            2 => "Intermediate (Lv 16-30)",
            3 => "Advanced (Lv 31-60)",
            4 => "Expert (Lv 61-100)",
            _ => "Beginner (Lv 1-15)"
        };

        /// <summary>Validates the question so malformed data never reaches the combat UI.</summary>
        public bool IsValid(out string error)
        {
            if (string.IsNullOrWhiteSpace(id)) { error = "missing id"; return false; }
            if (string.IsNullOrWhiteSpace(question)) { error = "missing question text"; return false; }
            if (choices == null || choices.Length != ChoiceCount) { error = $"needs exactly {ChoiceCount} choices"; return false; }
            for (int i = 0; i < choices.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(choices[i])) { error = $"choice {i} is empty"; return false; }
            }
            if (correctIndex < 0 || correctIndex >= choices.Length) { error = "correctIndex out of range"; return false; }
            if (difficulty < 1 || difficulty > 4) { error = "difficulty must be 1-4"; return false; }

            error = null;
            return true;
        }

        /// <summary>
        /// Returns a copy with the choices shuffled and <see cref="correctIndex"/> remapped,
        /// preventing players from memorizing fixed option positions.
        /// </summary>
        public QuestionData CreateShuffledCopy(Random rng)
        {
            var order = new List<int> { 0, 1, 2, 3 };
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            var copy = (QuestionData)MemberwiseClone();
            copy.choices = new string[choices.Length];
            for (int newIndex = 0; newIndex < order.Count; newIndex++)
            {
                int oldIndex = order[newIndex];
                copy.choices[newIndex] = choices[oldIndex];
                if (oldIndex == correctIndex) copy.correctIndex = newIndex;
            }
            return copy;
        }
    }

    /// <summary>
    /// Root object of questions.json wrapped for Unity JsonUtility serialization.
    /// </summary>
    [Serializable]
    public class QuestionBank
    {
        public int version;
        public string versionHash;
        public int count;
        public string updatedAt;
        public QuestionData[] questions;
    }
}
