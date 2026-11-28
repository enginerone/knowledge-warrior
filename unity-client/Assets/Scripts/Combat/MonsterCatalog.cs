using System;
using System.Collections.Generic;
using KnowledgeWarrior.Progress;
using UnityEngine;

namespace KnowledgeWarrior.Combat
{
    /// <summary>
    /// Builds subject-themed and tier-scaled monster and boss stages (3 monsters + 1 boss per run)
    /// using the dedicated left-facing subject monster sprites so every opponent stands face-to-face with the Hero.
    /// </summary>
    public static class MonsterCatalog
    {
        private static readonly Dictionary<string, Sprite> LoadedMonsterSprites = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Sprite> ProceduralSprites = new();

        private struct StageTemplate
        {
            public string name;
            public string resourceName;
            public float worldHeight;
            public AttackGestureType gesture;
            public float hpFactor;
            public float atkFactor;
            public int baseXp;
            public int xpPerLevel;
            public bool isBoss;
            public float tintStrength;
        }

        public static List<EnemyDefinition> BuildStagesForSubjectAndLevel(
            string subject,
            int subjectLevel,
            IReadOnlyList<EnemyDefinition> baseSceneStages)
        {
            int clampedLevel = Mathf.Clamp(subjectLevel, 1, SubjectTierSystem.MaxLevel);
            var tier = SubjectTierSystem.GetTierForLevel(clampedLevel);
            int alignedDifficulty = SubjectTierSystem.GetAlignedQuestionDifficulty(clampedLevel);

            var templates = GetSubjectStageTemplates(subject, tier);
            var colors = GetSubjectTints(subject, tier);

            int baseHp = ProgressManager.MaxHpForLevel(clampedLevel);
            int baseAtk = ProgressManager.AttackForLevel(clampedLevel);

            var result = new List<EnemyDefinition>(templates.Length);
            for (int i = 0; i < templates.Length; i++)
            {
                var tpl = templates[i];
                Sprite fallbackSceneSprite = (baseSceneStages != null && i < baseSceneStages.Count)
                    ? baseSceneStages[i].sprite
                    : null;
                Sprite sprite = LoadMonsterSprite(tpl.resourceName) ?? fallbackSceneSprite;
                Color tint = Color.Lerp(Color.white, colors[i], tpl.tintStrength);
                tint.a = 1f;

                if (sprite == null)
                {
                    sprite = GetOrCreateMonsterSprite(tpl.name, colors[i], tpl.isBoss);
                }

                result.Add(new EnemyDefinition
                {
                    displayName = tpl.name,
                    sprite = sprite,
                    tint = tint,
                    worldHeight = tpl.worldHeight,
                    attackGesture = tpl.gesture,
                    maxHp = Mathf.RoundToInt(baseHp * tpl.hpFactor),
                    attack = Mathf.RoundToInt(baseAtk * tpl.atkFactor),
                    xpReward = tpl.baseXp + clampedLevel * tpl.xpPerLevel,
                    isBoss = tpl.isBoss,
                    maxDifficulty = alignedDifficulty,
                    subject = subject
                });
            }

            return result;
        }

        public static (string stage1Monster, string stage2Monster, string stage3Monster, string stage4Boss) GetSubjectMonsterRoster(
            string subject,
            BadgeTier tier)
        {
            var templates = GetSubjectStageTemplates(subject, tier);
            return (templates[0].name, templates[1].name, templates[2].name, templates[3].name);
        }

        private static Sprite LoadMonsterSprite(string resourceName)
        {
            if (string.IsNullOrWhiteSpace(resourceName)) return null;
            if (LoadedMonsterSprites.TryGetValue(resourceName, out var cached) && cached != null)
            {
                return cached;
            }

            var tex = Resources.Load<Texture2D>($"Monsters/{resourceName}");
            if (tex != null)
            {
                var sprite = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0f),
                    100f);
                sprite.name = resourceName;
                LoadedMonsterSprites[resourceName] = sprite;
                return sprite;
            }

            var loadedSprite = Resources.Load<Sprite>($"Monsters/{resourceName}");
            if (loadedSprite != null)
            {
                LoadedMonsterSprites[resourceName] = loadedSprite;
                return loadedSprite;
            }

            return null;
        }

        private static StageTemplate[] GetSubjectStageTemplates(string subject, BadgeTier tier)
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
                "programming" => new[]
                {
                    MakeStage($"{tierPrefix} Syntax Bug Swarm", "Monster_Programming", 2.15f, AttackGestureType.MonsterClawSwipe, 0.65f, 0.65f, 25, 4, false, 0.08f),
                    MakeStage($"{tierPrefix} Off-By-One Imp", "Enemy_GlitchImp", 2.05f, AttackGestureType.MonsterVenomBolt, 0.82f, 0.80f, 35, 5, false, 0.12f),
                    MakeStage($"{tierPrefix} Binary Logic Golem", "Monster_WebDevelopment", 2.25f, AttackGestureType.MonsterCrushSlam, 1.02f, 0.95f, 50, 6, false, 0.12f),
                    MakeStage($"BOSS: {tierPrefix} Null-Pointer Archdragon", "Boss_NullPointerDragon", 2.70f, AttackGestureType.BossInfernoBreath, 1.40f, 1.15f, 90, 10, true, 0.16f)
                },
                "web development" => new[]
                {
                    MakeStage($"{tierPrefix} DOM Block Construct", "Monster_WebDevelopment", 2.15f, AttackGestureType.MonsterClawSwipe, 0.65f, 0.65f, 25, 4, false, 0.08f),
                    MakeStage($"{tierPrefix} Broken Tag Swarm", "Monster_Programming", 2.15f, AttackGestureType.MonsterVenomBolt, 0.82f, 0.80f, 35, 5, false, 0.12f),
                    MakeStage($"{tierPrefix} Unhandled Promise Gargoyle", "Monster_ComputerSystems", 2.25f, AttackGestureType.MonsterCrushSlam, 1.02f, 0.95f, 50, 6, false, 0.12f),
                    MakeStage($"BOSS: {tierPrefix} Hypertext Reflow Colossus", "Monster_WebDevelopment", 2.65f, AttackGestureType.BossInfernoBreath, 1.40f, 1.15f, 90, 10, true, 0.22f)
                },
                "networking" => new[]
                {
                    MakeStage($"{tierPrefix} Wi-Fi Signal Wraith", "Monster_Networking", 2.20f, AttackGestureType.MonsterClawSwipe, 0.65f, 0.65f, 25, 4, false, 0.08f),
                    MakeStage($"{tierPrefix} Dropped Packet Slime", "Enemy_SyntaxSlime", 1.95f, AttackGestureType.MonsterVenomBolt, 0.82f, 0.80f, 35, 5, false, 0.12f),
                    MakeStage($"{tierPrefix} Packet Sniffer Arachnid", "Monster_Cybersecurity", 2.20f, AttackGestureType.MonsterCrushSlam, 1.02f, 0.95f, 50, 6, false, 0.12f),
                    MakeStage($"BOSS: {tierPrefix} BGP Hijack Overlord", "Monster_Networking", 2.65f, AttackGestureType.BossInfernoBreath, 1.40f, 1.15f, 90, 10, true, 0.22f)
                },
                "databases" => new[]
                {
                    MakeStage($"{tierPrefix} SQL Shield Warden", "Monster_Databases", 2.20f, AttackGestureType.MonsterClawSwipe, 0.65f, 0.65f, 25, 4, false, 0.08f),
                    MakeStage($"{tierPrefix} Orphaned Schema Goblin", "Monster_SoftwareEngineering", 2.10f, AttackGestureType.MonsterVenomBolt, 0.82f, 0.80f, 35, 5, false, 0.12f),
                    MakeStage($"{tierPrefix} Deadlock Shard Golem", "Monster_WebDevelopment", 2.25f, AttackGestureType.MonsterCrushSlam, 1.02f, 0.95f, 50, 6, false, 0.12f),
                    MakeStage($"BOSS: {tierPrefix} Relational Lich Commander", "Monster_Databases", 2.65f, AttackGestureType.BossInfernoBreath, 1.40f, 1.15f, 90, 10, true, 0.22f)
                },
                "cybersecurity" => new[]
                {
                    MakeStage($"{tierPrefix} Access-Denied Arachnid", "Monster_Cybersecurity", 2.10f, AttackGestureType.MonsterClawSwipe, 0.65f, 0.65f, 25, 4, false, 0.08f),
                    MakeStage($"{tierPrefix} Phishing Cable Phantom", "Monster_Networking", 2.15f, AttackGestureType.MonsterVenomBolt, 0.82f, 0.80f, 35, 5, false, 0.12f),
                    MakeStage($"{tierPrefix} Malware Swarm Construct", "Monster_Programming", 2.20f, AttackGestureType.MonsterCrushSlam, 1.02f, 0.95f, 50, 6, false, 0.12f),
                    MakeStage($"BOSS: {tierPrefix} Zero-Day Ransomware Spider", "Monster_Cybersecurity", 2.60f, AttackGestureType.BossInfernoBreath, 1.40f, 1.15f, 90, 10, true, 0.22f)
                },
                "computer systems" => new[]
                {
                    MakeStage($"{tierPrefix} Circuit-Winged Gremlin", "Monster_ComputerSystems", 2.15f, AttackGestureType.MonsterClawSwipe, 0.65f, 0.65f, 25, 4, false, 0.08f),
                    MakeStage($"{tierPrefix} Silicon Block Golem", "Monster_WebDevelopment", 2.20f, AttackGestureType.MonsterVenomBolt, 0.82f, 0.80f, 35, 5, false, 0.12f),
                    MakeStage($"{tierPrefix} Overclocked Core Drone", "Monster_ArtificialIntelligence", 2.15f, AttackGestureType.MonsterCrushSlam, 1.02f, 0.95f, 50, 6, false, 0.12f),
                    MakeStage($"BOSS: {tierPrefix} Kernel Panic Gargoyle", "Monster_ComputerSystems", 2.65f, AttackGestureType.BossInfernoBreath, 1.40f, 1.15f, 90, 10, true, 0.22f)
                },
                "artificial intelligence" => new[]
                {
                    MakeStage($"{tierPrefix} Neural Core Sentinel", "Monster_ArtificialIntelligence", 2.10f, AttackGestureType.MonsterClawSwipe, 0.65f, 0.65f, 25, 4, false, 0.08f),
                    MakeStage($"{tierPrefix} Overfitting Bug Swarm", "Monster_Programming", 2.15f, AttackGestureType.MonsterVenomBolt, 0.82f, 0.80f, 35, 5, false, 0.12f),
                    MakeStage($"{tierPrefix} Tensor Logic Construct", "Monster_WebDevelopment", 2.25f, AttackGestureType.MonsterCrushSlam, 1.02f, 0.95f, 50, 6, false, 0.12f),
                    MakeStage($"BOSS: {tierPrefix} Singularity Omni-Core", "Monster_ArtificialIntelligence", 2.60f, AttackGestureType.BossInfernoBreath, 1.40f, 1.15f, 90, 10, true, 0.22f)
                },
                "ui/ux & software engineering" or "software engineering" or "ui/ux" => new[]
                {
                    MakeStage($"{tierPrefix} Scope-Creep Spec Goblin", "Monster_SoftwareEngineering", 2.15f, AttackGestureType.MonsterClawSwipe, 0.65f, 0.65f, 25, 4, false, 0.08f),
                    MakeStage($"{tierPrefix} Dark-Pattern Bug Swarm", "Monster_Programming", 2.15f, AttackGestureType.MonsterVenomBolt, 0.82f, 0.80f, 35, 5, false, 0.12f),
                    MakeStage($"{tierPrefix} Legacy Monolith Golem", "Monster_WebDevelopment", 2.25f, AttackGestureType.MonsterCrushSlam, 1.02f, 0.95f, 50, 6, false, 0.12f),
                    MakeStage($"BOSS: {tierPrefix} Endless Backlog Bureaucrat", "Monster_SoftwareEngineering", 2.65f, AttackGestureType.BossInfernoBreath, 1.40f, 1.15f, 90, 10, true, 0.22f)
                },
                _ => new[]
                {
                    MakeStage($"{tierPrefix} Glitch Slime", "Enemy_SyntaxSlime", 1.95f, AttackGestureType.MonsterClawSwipe, 0.65f, 0.65f, 25, 4, false, 0.08f),
                    MakeStage($"{tierPrefix} Logic Imp", "Enemy_GlitchImp", 2.05f, AttackGestureType.MonsterVenomBolt, 0.82f, 0.80f, 35, 5, false, 0.12f),
                    MakeStage($"{tierPrefix} Memory Leak Golem", "Monster_WebDevelopment", 2.25f, AttackGestureType.MonsterCrushSlam, 1.02f, 0.95f, 50, 6, false, 0.12f),
                    MakeStage($"BOSS: {tierPrefix} Null-Pointer Dragon", "Boss_NullPointerDragon", 2.70f, AttackGestureType.BossInfernoBreath, 1.40f, 1.15f, 90, 10, true, 0.16f)
                }
            };
        }

        private static StageTemplate MakeStage(
            string name,
            string resourceName,
            float worldHeight,
            AttackGestureType gesture,
            float hpFactor,
            float atkFactor,
            int baseXp,
            int xpPerLevel,
            bool isBoss,
            float tintStrength)
        {
            return new StageTemplate
            {
                name = name,
                resourceName = resourceName,
                worldHeight = worldHeight,
                gesture = gesture,
                hpFactor = hpFactor,
                atkFactor = atkFactor,
                baseXp = baseXp,
                xpPerLevel = xpPerLevel,
                isBoss = isBoss,
                tintStrength = tintStrength
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
                "artificial intelligence" => new[]
                {
                    new Color(0.36f, 0.94f, 1.00f),
                    new Color(0.72f, 0.54f, 1.00f),
                    new Color(0.32f, 0.88f, 0.94f),
                    new Color(0.96f, 0.42f, 0.92f)
                },
                "ui/ux & software engineering" or "software engineering" or "ui/ux" => new[]
                {
                    new Color(1.00f, 0.84f, 0.40f),
                    new Color(0.40f, 0.92f, 0.86f),
                    new Color(0.78f, 0.60f, 1.00f),
                    new Color(1.00f, 0.54f, 0.28f)
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

