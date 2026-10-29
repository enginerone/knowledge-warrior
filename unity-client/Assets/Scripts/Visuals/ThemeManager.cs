using System.Collections.Generic;
using KnowledgeWarrior.Progress;
using UnityEngine;

namespace KnowledgeWarrior.Visuals
{
    public struct SubjectTierTheme
    {
        public string subject;
        public string biomeTitle;
        public string tierSubtitle;
        public BadgeTier tier;
        public Color primaryAccent;
        public Color secondaryAccent;
        public Color skyTop;
        public Color skyHorizon;
        public Color groundColor;
        public Color gridGlow;
    }

    /// <summary>
    /// Generates and applies unique arena themes and background designs for every Subject
    /// AND for every Badge Tier (Beginner Lv 1-15, Intermediate Lv 16-30, Advanced Lv 31-60, Expert Lv 61-100).
    /// </summary>
    public class ThemeManager : MonoBehaviour
    {
        public static ThemeManager Instance { get; private set; }

        private readonly Dictionary<string, Sprite> _generatedBackgrounds = new();
        private SpriteRenderer _backgroundRenderer;

        public SubjectTierTheme CurrentTheme { get; private set; }

        public static ThemeManager EnsureInstance()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ThemeManager");
            return go.AddComponent<ThemeManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void ApplyTheme(string subject, int subjectLevel)
        {
            var tier = SubjectTierSystem.GetTierForLevel(subjectLevel);
            var theme = BuildThemeSpec(subject, tier);
            CurrentTheme = theme;

            if (_backgroundRenderer == null)
            {
                var bgGo = GameObject.Find("Background");
                if (bgGo != null) _backgroundRenderer = bgGo.GetComponent<SpriteRenderer>();
            }

            if (Camera.main != null)
            {
                Camera.main.backgroundColor = theme.skyTop;
            }

            if (_backgroundRenderer != null)
            {
                _backgroundRenderer.sprite = GetOrCreateArenaBackgroundSprite(theme);
                _backgroundRenderer.color = Color.white;
                var fitter = _backgroundRenderer.GetComponent<BackgroundFitter>();
                if (fitter != null)
                {
                    fitter.enabled = false;
                    fitter.enabled = true;
                }
            }
        }

        public static SubjectTierTheme BuildThemeSpec(string subject, BadgeTier tier)
        {
            string key = (subject ?? string.Empty).Trim().ToLowerInvariant();
            var (biomeTitle, basePrimary, baseSecondary, baseSky, baseGround) = key switch
            {
                "programming" => (
                    "Citadel of Code",
                    new Color(1.00f, 0.82f, 0.28f),
                    new Color(0.20f, 0.92f, 0.68f),
                    new Color(0.05f, 0.09f, 0.18f),
                    new Color(0.08f, 0.14f, 0.22f)
                ),
                "web development" => (
                    "Hypertext Harbor",
                    new Color(1.00f, 0.56f, 0.24f),
                    new Color(0.28f, 0.86f, 1.00f),
                    new Color(0.11f, 0.07f, 0.20f),
                    new Color(0.16f, 0.10f, 0.24f)
                ),
                "networking" => (
                    "Packet Peaks",
                    new Color(0.26f, 0.90f, 1.00f),
                    new Color(0.36f, 0.54f, 1.00f),
                    new Color(0.03f, 0.10f, 0.22f),
                    new Color(0.05f, 0.15f, 0.28f)
                ),
                "databases" => (
                    "Relational Ruins",
                    new Color(0.68f, 0.44f, 1.00f),
                    new Color(0.95f, 0.42f, 0.82f),
                    new Color(0.09f, 0.05f, 0.20f),
                    new Color(0.14f, 0.08f, 0.26f)
                ),
                "cybersecurity" => (
                    "Cipher Sanctum",
                    new Color(1.00f, 0.34f, 0.54f),
                    new Color(1.00f, 0.76f, 0.28f),
                    new Color(0.15f, 0.04f, 0.12f),
                    new Color(0.20f, 0.06f, 0.15f)
                ),
                "computer systems" => (
                    "Silicon Foundry",
                    new Color(0.24f, 0.94f, 0.60f),
                    new Color(0.45f, 0.88f, 1.00f),
                    new Color(0.04f, 0.13f, 0.12f),
                    new Color(0.07f, 0.18f, 0.16f)
                ),
                _ => (
                    "Binary Woods",
                    new Color(0.30f, 0.88f, 1.00f),
                    new Color(0.61f, 0.42f, 1.00f),
                    new Color(0.05f, 0.08f, 0.18f),
                    new Color(0.08f, 0.12f, 0.24f)
                )
            };

            Color tierTint = SubjectTierSystem.GetBadgeColor(tier);
            string tierDesignName = tier switch
            {
                BadgeTier.Beginner => "Initiate Courtyard (Beginner Tier Design)",
                BadgeTier.Intermediate => "Adept Bastion (Intermediate Tier Design)",
                BadgeTier.Advanced => "Arcane Nexus (Advanced Tier Design)",
                BadgeTier.Expert => "Grandmaster Astral Core (Expert Tier Design)",
                _ => "Initiate Courtyard"
            };

            float tierIntensity = tier switch
            {
                BadgeTier.Beginner => 0.18f,
                BadgeTier.Intermediate => 0.34f,
                BadgeTier.Advanced => 0.52f,
                BadgeTier.Expert => 0.72f,
                _ => 0.2f
            };

            Color skyTop = Color.Lerp(baseSky, tierTint * 0.38f, tierIntensity * 0.5f);
            skyTop.a = 1f;
            Color skyHorizon = Color.Lerp(basePrimary * 0.48f, tierTint * 0.78f, tierIntensity);
            skyHorizon.a = 1f;
            Color ground = Color.Lerp(baseGround, tierTint * 0.28f, tierIntensity * 0.45f);
            ground.a = 1f;
            Color gridGlow = Color.Lerp(basePrimary, tierTint, 0.45f);
            gridGlow.a = 1f;

            return new SubjectTierTheme
            {
                subject = string.IsNullOrWhiteSpace(subject) ? "Programming" : subject,
                biomeTitle = biomeTitle,
                tierSubtitle = tierDesignName,
                tier = tier,
                primaryAccent = basePrimary,
                secondaryAccent = baseSecondary,
                skyTop = skyTop,
                skyHorizon = skyHorizon,
                groundColor = ground,
                gridGlow = gridGlow
            };
        }

        private Sprite GetOrCreateArenaBackgroundSprite(SubjectTierTheme theme)
        {
            string cacheKey = $"{theme.subject.ToLowerInvariant()}_{theme.tier}";
            if (_generatedBackgrounds.TryGetValue(cacheKey, out var cached) && cached != null)
            {
                return cached;
            }

            const int width = 512;
            const int height = 288;
            int groundY = Mathf.RoundToInt(height * 0.28f);

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color[width * height];
            string subjectKey = theme.subject.Trim().ToLowerInvariant();
            int tierIndex = (int)theme.tier;

            // 1. Sky gradient + Tier-specific celestial emblem (Moon / Twin Rings / Arcane Portal / Astral Eclipse)
            Vector2 emblemCenter = new(width * 0.5f, height * (0.64f + 0.03f * tierIndex));
            float emblemRadius = 26f + tierIndex * 11f;

            for (int y = groundY; y < height; y++)
            {
                float ky = (float)(y - groundY) / (height - groundY);
                Color rowColor = Color.Lerp(theme.skyHorizon, theme.skyTop, Mathf.Pow(ky, 0.75f));

                for (int x = 0; x < width; x++)
                {
                    Color c = rowColor;
                    float dist = Vector2.Distance(new Vector2(x, y), emblemCenter);

                    if (theme.tier == BadgeTier.Beginner)
                    {
                        if (dist < emblemRadius)
                            c = Color.Lerp(c, theme.primaryAccent, (1f - dist / emblemRadius) * 0.55f);
                    }
                    else if (theme.tier == BadgeTier.Intermediate)
                    {
                        float ring = Mathf.Abs(dist - emblemRadius);
                        if (ring < 3.5f)
                            c = Color.Lerp(c, theme.secondaryAccent, 1f - ring / 3.5f);
                    }
                    else if (theme.tier == BadgeTier.Advanced)
                    {
                        float ring1 = Mathf.Abs(dist - emblemRadius);
                        float ring2 = Mathf.Abs(dist - emblemRadius * 0.62f);
                        if (ring1 < 3f || ring2 < 2.5f)
                            c = Color.Lerp(c, theme.gridGlow, 0.85f);
                    }
                    else
                    {
                        // Expert Astral Eclipse with radial energy beams
                        if (dist < emblemRadius * 0.72f)
                        {
                            c = Color.Lerp(c, new Color(0.02f, 0.02f, 0.06f, 1f), 0.9f);
                        }
                        else if (dist < emblemRadius * 1.15f)
                        {
                            float corona = 1f - (dist - emblemRadius * 0.72f) / (emblemRadius * 0.43f);
                            c = Color.Lerp(c, theme.primaryAccent, Mathf.Clamp01(corona));
                        }
                    }

                    // Star / data motes in upper sky
                    int hash = (x * 73856093) ^ (y * 19349663) ^ (tierIndex * 83492791);
                    if ((hash & 1023) == 0 && y > groundY + 30)
                    {
                        c = Color.Lerp(c, Color.white, 0.75f);
                    }

                    pixels[y * width + x] = c;
                }
            }

            // 2. Subject-distinct architectural silhouette along the horizon
            for (int x = 0; x < width; x++)
            {
                int structureHeight = CalculateSubjectSilhouetteHeight(subjectKey, tierIndex, x);
                int topY = Mathf.Min(height - 1, groundY + structureHeight);
                Color silhouetteBase = Color.Lerp(theme.groundColor, Color.black, 0.45f);

                for (int y = groundY; y < topY; y++)
                {
                    Color sc = silhouetteBase;
                    bool isWindowOrCircuit = ((x + tierIndex * 3) % 14 < 2) && ((y - groundY) % 10 < 4);
                    if (isWindowOrCircuit)
                    {
                        sc = Color.Lerp(sc, theme.primaryAccent, 0.65f);
                    }
                    pixels[y * width + x] = sc;
                }
            }

            // 3. Battleground floor with perspective grid & tier-distinct energy lines
            for (int y = 0; y < groundY; y++)
            {
                float depth = 1f - (float)y / groundY;
                Color floorRow = Color.Lerp(theme.groundColor * 1.25f, theme.groundColor * 0.55f, depth);
                floorRow.a = 1f;

                bool isHorizontalLine = (y == groundY - 1) || (y == groundY - 2) ||
                                        (Mathf.RoundToInt(Mathf.Pow(depth, 0.6f) * (10 + tierIndex * 4)) % 2 == 0 && y % (8 - tierIndex) == 0);

                for (int x = 0; x < width; x++)
                {
                    Color fc = floorRow;
                    float centeredX = (x - width * 0.5f) / (0.25f + depth * 1.35f);
                    bool isPerspectiveRay = Mathf.Abs(centeredX % 28f) < 1.3f;

                    if (isHorizontalLine || isPerspectiveRay)
                    {
                        float glowStrength = (y >= groundY - 2) ? 0.95f : (0.28f + 0.12f * tierIndex);
                        fc = Color.Lerp(fc, theme.gridGlow, glowStrength);
                    }

                    pixels[y * width + x] = fc;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 32f);
            sprite.name = $"ArenaBG_{cacheKey}";
            _generatedBackgrounds[cacheKey] = sprite;
            return sprite;
        }

        private static int CalculateSubjectSilhouetteHeight(string subjectKey, int tierIndex, int x)
        {
            int tierBoost = tierIndex * 9;
            switch (subjectKey)
            {
                case "programming":
                    // Stepped citadel towers
                    return 22 + tierBoost + ((x / 24) * 37 % (36 + tierBoost));
                case "databases":
                    // Crystalline monolithic pillars
                    int col = x % 48;
                    return (col > 10 && col < 38) ? 28 + tierBoost + Mathf.RoundToInt(Mathf.Sin(x * 0.08f) * 18f) : 12;
                case "networking":
                    // Sharp signal relay peaks and laser spires
                    return 18 + tierBoost + Mathf.RoundToInt(Mathf.PingPong(x * 0.9f, 44f + tierBoost));
                case "web development":
                    // Arched holographic domes and harbor towers
                    return 20 + tierBoost + Mathf.RoundToInt(Mathf.Abs(Mathf.Sin(x * 0.045f)) * (38f + tierBoost));
                case "cybersecurity":
                    // Fortress battlements and bastion walls
                    int crenel = (x / 16) % 2 == 0 ? 18 : 6;
                    return 26 + tierBoost + crenel;
                case "computer systems":
                    // Industrial foundry reactors and heat sinks
                    return 20 + tierBoost + ((x % 32 < 20) ? 28 : 10);
                default:
                    return 20 + tierBoost + Mathf.RoundToInt(Mathf.PerlinNoise(x * 0.03f, tierIndex) * 35f);
            }
        }
    }
}

