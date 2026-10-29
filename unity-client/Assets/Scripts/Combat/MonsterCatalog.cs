using System.Collections.Generic;
using KnowledgeWarrior.Progress;
using UnityEngine;

namespace KnowledgeWarrior.Combat
{
    /// <summary>
    /// Builds subject-themed and tier-scaled monster and boss stages (3 monsters + 1 boss per run),
    /// as well as 1v1 Real-User Challenger definitions at the player's exact subject level.
    /// </summary>
    public static class MonsterCatalog
    {
        private static readonly Dictionary<string, Sprite> ProceduralSprites = new();

        public static List<EnemyDefinition> BuildStagesForSubjectAndLevel(
            string subject,
            int subjectLevel,
            IReadOnlyList<EnemyDefinition> baseSceneStages)
        {
            int clampedLevel = Mathf.Clamp(subjectLevel, 1, SubjectTierSystem.MaxLevel);
            var tier = SubjectTierSystem.GetTierForLevel(clampedLevel);
            int alignedDifficulty = SubjectTierSystem.GetAlignedQuestionDifficulty(clampedLevel);

            Sprite slimeSprite = (baseSceneStages != null && baseSceneStages.Count > 0) ? baseSceneStages[0].sprite : null;
            Sprite impSprite = (baseSceneStages != null && baseSceneStages.Count > 1) ? baseSceneStages[1].sprite : slimeSprite;
            Sprite golemSprite = (baseSceneStages != null && baseSceneStages.Count > 2) ? baseSceneStages[2].sprite : impSprite;
            Sprite bossSprite = (baseSceneStages != null && baseSceneStages.Count > 3) ? baseSceneStages[3].sprite : golemSprite;

            var names = GetSubjectMonsterRoster(subject, tier);
            var colors = GetSubjectTints(subject, tier);

            int baseHp = ProgressManager.MaxHpForLevel(clampedLevel);
            int baseAtk = ProgressManager.AttackForLevel(clampedLevel);

            return new List<EnemyDefinition>
            {
                new()
                {
                    displayName = names.stage1Monster,
                    sprite = slimeSprite != null ? slimeSprite : GetOrCreateMonsterSprite(names.stage1Monster, colors[0], false),
                    tint = colors[0],
                    worldHeight = 2.4f,
                    attackGesture = AttackGestureType.MonsterClawSwipe,
                    maxHp = Mathf.RoundToInt(baseHp * 0.65f),
                    attack = Mathf.RoundToInt(baseAtk * 0.65f),
                    xpReward = 25 + clampedLevel * 4,
                    isBoss = false,
                    maxDifficulty = alignedDifficulty,
                    subject = subject
                },
                new()
                {
                    displayName = names.stage2Monster,
                    sprite = impSprite != null ? impSprite : GetOrCreateMonsterSprite(names.stage2Monster, colors[1], false),
                    tint = colors[1],
                    worldHeight = 2.65f,
                    attackGesture = AttackGestureType.MonsterVenomBolt,
                    maxHp = Mathf.RoundToInt(baseHp * 0.82f),
                    attack = Mathf.RoundToInt(baseAtk * 0.80f),
                    xpReward = 35 + clampedLevel * 5,
                    isBoss = false,
                    maxDifficulty = alignedDifficulty,
                    subject = subject
                },
                new()
                {
                    displayName = names.stage3Monster,
                    sprite = golemSprite != null ? golemSprite : GetOrCreateMonsterSprite(names.stage3Monster, colors[2], false),
                    tint = colors[2],
                    worldHeight = 2.95f,
                    attackGesture = AttackGestureType.MonsterCrushSlam,
                    maxHp = Mathf.RoundToInt(baseHp * 1.02f),
                    attack = Mathf.RoundToInt(baseAtk * 0.95f),
                    xpReward = 50 + clampedLevel * 6,
                    isBoss = false,
                    maxDifficulty = alignedDifficulty,
                    subject = subject
                },
                new()
                {
                    displayName = names.stage4Boss,
                    sprite = bossSprite != null ? bossSprite : GetOrCreateMonsterSprite(names.stage4Boss, colors[3], true),
                    tint = colors[3],
                    worldHeight = 3.85f,
                    attackGesture = AttackGestureType.BossInfernoBreath,
                    maxHp = Mathf.RoundToInt(baseHp * 1.40f),
                    attack = Mathf.RoundToInt(baseAtk * 1.15f),
                    xpReward = 90 + clampedLevel * 10,
                    isBoss = true,
                    maxDifficulty = alignedDifficulty,
                    subject = subject
                }
            };
        }

        public static EnemyDefinition BuildRealUserOpponent(
            string opponentUserId,
            string opponentUsername,
            string subject,
            int exactSubjectLevel,
            int opponentSubjectPoints,
            Sprite heroSpriteFallback)
        {
            int clampedLevel = Mathf.Clamp(exactSubjectLevel, 1, SubjectTierSystem.MaxLevel);
            var tier = SubjectTierSystem.GetTierForLevel(clampedLevel);
            int alignedDifficulty = SubjectTierSystem.GetAlignedQuestionDifficulty(clampedLevel);
            Color badgeColor = SubjectTierSystem.GetBadgeColor(tier);

            return new EnemyDefinition
            {
                displayName = $"{opponentUsername} (LV {clampedLevel} {SubjectTierSystem.GetBadgeName(tier)})",
                sprite = heroSpriteFallback != null ? heroSpriteFallback : GetOrCreateMonsterSprite(opponentUsername, badgeColor, false),
                tint = Color.Lerp(Color.white, badgeColor, 0.38f),
                worldHeight = 3.1f,
                attackGesture = AttackGestureType.RivalWarriorStrike,
                maxHp = ProgressManager.MaxHpForLevel(clampedLevel),
                attack = ProgressManager.AttackForLevel(clampedLevel),
                xpReward = 60 + clampedLevel * 8,
                isBoss = false,
                isRealUserOpponent = true,
                opponentUserId = opponentUserId,
                opponentSubjectLevel = clampedLevel,
                opponentSubjectPoints = Mathf.Max(100, opponentSubjectPoints),
                maxDifficulty = alignedDifficulty,
                subject = subject
            };
        }

        private static (string stage1Monster, string stage2Monster, string stage3Monster, string stage4Boss) GetSubjectMonsterRoster(
            string subject,
            BadgeTier tier)
        {
            string tierPrefix = tier switch
            {
                BadgeTier.Beginner => "Initiate",
                BadgeTier.Intermediate => "Adept",
                BadgeTier.Advanced => "Arcane",
                BadgeTier.Expert => "Grandmaster",
                _ => "Initiate"
            };

            string key = (subject ?? string.Empty).Trim().ToLowerInvariant();
            return key switch
            {
                "programming" => (
                    $"{tierPrefix} Syntax Slime",
                    $"{tierPrefix} Off-By-One Imp",
                    $"{tierPrefix} Stack Overflow Golem",
                    $"BOSS: {tierPrefix} Infinite Loop Lich"
                ),
                "databases" => (
                    $"{tierPrefix} Unindexed Wisp",
                    $"{tierPrefix} Orphaned Row Stalker",
                    $"{tierPrefix} Deadlock Construct",
                    $"BOSS: {tierPrefix} Relational Leviathan"
                ),
                "networking" => (
                    $"{tierPrefix} Dropped Packet Sprite",
                    $"{tierPrefix} Latency Phantom",
                    $"{tierPrefix} Broadcast Storm Brute",
                    $"BOSS: {tierPrefix} BGP Hijack Wyrm"
                ),
                "web development" => (
                    $"{tierPrefix} Broken Tag Crawler",
                    $"{tierPrefix} CORS Specter",
                    $"{tierPrefix} Unhandled Promise Gargoyle",
                    $"BOSS: {tierPrefix} DOM Reflow Colossus"
                ),
                "cybersecurity" => (
                    $"{tierPrefix} Phishing Lure Mimic",
                    $"{tierPrefix} Brute-Force Hound",
                    $"{tierPrefix} Ransomware Juggernaut",
                    $"BOSS: {tierPrefix} Zero-Day Archfiend"
                ),
                "computer systems" => (
                    $"{tierPrefix} Bit-Flip Gremlin",
                    $"{tierPrefix} Cache Miss Banshee",
                    $"{tierPrefix} Thermal Throttle Titan",
                    $"BOSS: {tierPrefix} Kernel Panic Behemoth"
                ),
                _ => (
                    $"{tierPrefix} Glitch Slime",
                    $"{tierPrefix} Logic Imp",
                    $"{tierPrefix} Memory Leak Golem",
                    $"BOSS: {tierPrefix} Null-Pointer Dragon"
                )
            };
        }

        private static Color[] GetSubjectTints(string subject, BadgeTier tier)
        {
            Color tierAccent = SubjectTierSystem.GetBadgeColor(tier);
            string key = (subject ?? string.Empty).Trim().ToLowerInvariant();
            Color[] baseColors = key switch
            {
                "programming" => new[]
                {
                    new Color(0.55f, 1.00f, 0.72f),
                    new Color(1.00f, 0.84f, 0.38f),
                    new Color(0.95f, 0.58f, 0.30f),
                    new Color(1.00f, 0.42f, 0.38f)
                },
                "databases" => new[]
                {
                    new Color(0.76f, 0.58f, 1.00f),
                    new Color(0.95f, 0.52f, 0.88f),
                    new Color(0.56f, 0.72f, 1.00f),
                    new Color(0.92f, 0.36f, 1.00f)
                },
                "networking" => new[]
                {
                    new Color(0.38f, 0.94f, 1.00f),
                    new Color(0.46f, 0.72f, 1.00f),
                    new Color(0.28f, 1.00f, 0.84f),
                    new Color(0.25f, 0.62f, 1.00f)
                },
                "web development" => new[]
                {
                    new Color(1.00f, 0.68f, 0.36f),
                    new Color(1.00f, 0.50f, 0.42f),
                    new Color(0.98f, 0.82f, 0.32f),
                    new Color(1.00f, 0.40f, 0.25f)
                },
                "cybersecurity" => new[]
                {
                    new Color(1.00f, 0.44f, 0.64f),
                    new Color(1.00f, 0.32f, 0.42f),
                    new Color(0.88f, 0.42f, 0.96f),
                    new Color(1.00f, 0.24f, 0.36f)
                },
                "computer systems" => new[]
                {
                    new Color(0.38f, 0.96f, 0.62f),
                    new Color(0.56f, 0.92f, 0.40f),
                    new Color(0.32f, 0.86f, 0.88f),
                    new Color(0.20f, 1.00f, 0.52f)
                },
                _ => new[]
                {
                    Color.white,
                    new Color(0.85f, 0.92f, 1.00f),
                    new Color(1.00f, 0.82f, 0.62f),
                    new Color(1.00f, 0.58f, 0.48f)
                }
            };

            for (int i = 0; i < baseColors.Length; i++)
            {
                baseColors[i] = Color.Lerp(baseColors[i], tierAccent, 0.28f);
                baseColors[i].a = 1f;
            }
            return baseColors;
        }

        private static Sprite GetOrCreateMonsterSprite(string key, Color color, bool isBoss)
        {
            if (ProceduralSprites.TryGetValue(key, out var existing) && existing != null)
            {
                return existing;
            }

            int size = isBoss ? 128 : 96;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[size * size];
            Vector2 center = new(size * 0.5f, size * 0.5f);
            float radius = size * 0.42f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    if (dist <= radius)
                    {
                        float edge = 1f - (dist / radius);
                        pixels[y * size + x] = Color.Lerp(color * 0.45f, color, edge);
                    }
                    else
                    {
                        pixels[y * size + x] = Color.clear;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.0f), 32f);
            ProceduralSprites[key] = sprite;
            return sprite;
        }
    }
}

