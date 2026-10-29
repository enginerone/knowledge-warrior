using System;
using UnityEngine;

namespace KnowledgeWarrior.Progress
{
    public enum BadgeTier
    {
        Beginner = 1,
        Intermediate = 2,
        Advanced = 3,
        Expert = 4
    }

    [Serializable]
    public class SubjectProgressData
    {
        public string subject = "Programming";
        public int level = 1;
        public int xp = 0;
        public int points = 120;
        public int pvpWins = 0;
        public int pvpLosses = 0;
    }

    /// <summary>
    /// Central authority for per-subject levels (1 to 100), tier badges, aligned question difficulty,
    /// and the 20% point deduction rule for 1v1 real-user battles.
    /// </summary>
    public static class SubjectTierSystem
    {
        public const int MinLevel = 1;
        public const int MaxLevel = 100;
        public const float PvpLossDeductionRate = 0.20f;

        private static readonly Color BeginnerColor = new(0.24f, 0.90f, 0.63f, 1f);
        private static readonly Color IntermediateColor = new(0.30f, 0.88f, 1f, 1f);
        private static readonly Color AdvancedColor = new(0.72f, 0.48f, 1f, 1f);
        private static readonly Color ExpertColor = new(1f, 0.42f, 0.32f, 1f);

        public static BadgeTier GetTierForLevel(int level)
        {
            int clamped = Mathf.Clamp(level, MinLevel, MaxLevel);
            if (clamped <= 15) return BadgeTier.Beginner;
            if (clamped <= 30) return BadgeTier.Intermediate;
            if (clamped <= 60) return BadgeTier.Advanced;
            return BadgeTier.Expert;
        }

        public static int GetAlignedQuestionDifficulty(int level)
        {
            return (int)GetTierForLevel(level);
        }

        public static string GetBadgeName(BadgeTier tier) => tier switch
        {
            BadgeTier.Beginner => "Beginner",
            BadgeTier.Intermediate => "Intermediate",
            BadgeTier.Advanced => "Advanced",
            BadgeTier.Expert => "Expert",
            _ => "Beginner"
        };

        public static string GetBadgeNameForLevel(int level) => GetBadgeName(GetTierForLevel(level));

        public static string GetBadgeEmblemText(int level)
        {
            var tier = GetTierForLevel(level);
            return tier switch
            {
                BadgeTier.Beginner => $"[BRONZE SHIELD • BEGINNER LV {level}]",
                BadgeTier.Intermediate => $"[SILVER CREST • INTERMEDIATE LV {level}]",
                BadgeTier.Advanced => $"[ARCANE GOLD • ADVANCED LV {level}]",
                BadgeTier.Expert => $"[GRANDMASTER CROWN • EXPERT LV {level}]",
                _ => $"[BEGINNER LV {level}]"
            };
        }

        public static string GetLevelRangeLabel(BadgeTier tier) => tier switch
        {
            BadgeTier.Beginner => "Levels 1 - 15 (Beginner)",
            BadgeTier.Intermediate => "Levels 16 - 30 (Intermediate)",
            BadgeTier.Advanced => "Levels 31 - 60 (Advanced)",
            BadgeTier.Expert => "Levels 61 - 100 (Expert)",
            _ => "Levels 1 - 15 (Beginner)"
        };

        public static Color GetBadgeColor(BadgeTier tier) => tier switch
        {
            BadgeTier.Beginner => BeginnerColor,
            BadgeTier.Intermediate => IntermediateColor,
            BadgeTier.Advanced => AdvancedColor,
            BadgeTier.Expert => ExpertColor,
            _ => BeginnerColor
        };

        public static Color GetBadgeColorForLevel(int level) => GetBadgeColor(GetTierForLevel(level));

        public static int XpToNextSubjectLevel(int level)
        {
            int clamped = Mathf.Clamp(level, MinLevel, MaxLevel);
            return 80 + (clamped - 1) * 25;
        }

        public static int CalculateTwentyPercentDeduction(int currentPoints)
        {
            if (currentPoints <= 0) return 0;
            return Mathf.Max(1, Mathf.RoundToInt(currentPoints * PvpLossDeductionRate));
        }
    }
}

