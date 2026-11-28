using System;
using UnityEngine;

namespace KnowledgeWarrior.UI
{
    /// <summary>
    /// Procedurally generated, cached white sprites for decorative UI effects. Every sprite is
    /// pure white with an alpha shape so it can be tinted with <c>Image.color</c>.
    /// </summary>
    public static class UISprites
    {
        private static Sprite _verticalGradient;
        private static Sprite _shineBand;
        private static Sprite _ring;
        private static Sprite _radial;
        private static Sprite _star;
        private static Sprite _corner;
        private static Sprite _coin;
        private static Sprite _shieldEmblem;
        private static Sprite _sweatDrop;
        private static Sprite _angerMark;
        private static Sprite _exclaimBurst;
        private static Sprite _brandLogo;

        /// <summary>Opaque at the top, transparent at the bottom.</summary>
        public static Sprite VerticalGradient => Get(ref _verticalGradient, BuildVerticalGradient);

        /// <summary>Soft horizontal band that peaks in the middle, used for diagonal shine sweeps.</summary>
        public static Sprite ShineBand => Get(ref _shineBand, BuildShineBand);

        /// <summary>Hollow nine-sliced frame with a crisp edge line and a soft inner glow.</summary>
        public static Sprite Ring => Get(ref _ring, BuildRing);

        /// <summary>Soft round dot.</summary>
        public static Sprite Radial => Get(ref _radial, BuildRadial);

        /// <summary>Four-point sparkle.</summary>
        public static Sprite Star => Get(ref _star, BuildStar);

        /// <summary>Top-left ornamental bracket with a diamond tip; mirror it for the other corners.</summary>
        public static Sprite Corner => Get(ref _corner, BuildCorner);

        /// <summary>Full-color metallic gold coin icon with inner bevel and diamond emblem.</summary>
        public static Sprite Coin => Get(ref _coin, BuildCoin);

        /// <summary>Heraldic shield icon for block emotes.</summary>
        public static Sprite ShieldEmblem => Get(ref _shieldEmblem, BuildShieldEmblem);

        /// <summary>Teardrop sweat icon for miss and recoil emotes.</summary>
        public static Sprite SweatDrop => Get(ref _sweatDrop, BuildSweatDrop);

        /// <summary>Four-arc anger vein mark for monster taunts.</summary>
        public static Sprite AngerMark => Get(ref _angerMark, BuildAngerMark);

        /// <summary>Exclamation burst icon for shock and boss roar emotes.</summary>
        public static Sprite ExclaimBurst => Get(ref _exclaimBurst, BuildExclaimBurst);

        /// <summary>Official Knowledge Warrior brand emblem loaded from Resources/UI.</summary>
        public static Sprite BrandLogo => Get(ref _brandLogo, LoadOrBuildBrandLogo);

        /// <summary>Returns the procedural sprite matching a combat emote type.</summary>
        public static Sprite GetEmoteIcon(CombatEmoteType emote) => emote switch
        {
            CombatEmoteType.ShieldBlock => ShieldEmblem,
            CombatEmoteType.SweatMiss => SweatDrop,
            CombatEmoteType.MonsterTaunt => AngerMark,
            CombatEmoteType.MonsterShock or CombatEmoteType.BossRoar => ExclaimBurst,
            _ => Star
        };

        private static Sprite Get(ref Sprite cache, Func<Sprite> build)
        {
            if (cache == null) cache = build();
            return cache;
        }

        private static Sprite BuildVerticalGradient() =>
            Build("FX_VerticalGradient", 4, 64, (x, y) => y / 63f);

        private static Sprite BuildShineBand() =>
            Build("FX_ShineBand", 64, 4, (x, y) =>
            {
                float d = 1f - Mathf.Abs(x - 31.5f) / 32f;
                return d * d;
            });

        private static Sprite BuildRing() =>
            Build("FX_Ring", 32, 32, (x, y) =>
            {
                int d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(31 - x, 31 - y));
                float line = Mathf.Clamp01(2f - d);
                float glow = d < 10 ? Mathf.Pow(1f - d / 10f, 2f) * 0.55f : 0f;
                return Mathf.Max(line, glow);
            }, new Vector4(10f, 10f, 10f, 10f));

        private static Sprite BuildRadial() =>
            Build("FX_Radial", 64, 64, (x, y) =>
            {
                float r = Mathf.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f)) / 32f;
                return r < 1f ? Mathf.Pow(1f - r, 2f) : 0f;
            });

        private static Sprite BuildStar() =>
            Build("FX_Star", 64, 64, (x, y) =>
            {
                float nx = Mathf.Abs((x - 31.5f) / 32f);
                float ny = Mathf.Abs((y - 31.5f) / 32f);
                float v = Mathf.Sqrt(nx) + Mathf.Sqrt(ny);
                return v < 1f ? Mathf.Pow(1f - v, 2f) * 1.4f : 0f;
            });

        private static Sprite BuildCorner() =>
            Build("FX_Corner", 64, 64, (x, y) =>
            {
                int top = 63 - y;
                float a = 0f;
                if (top <= 2 && x >= 14) a = Mathf.Max(a, 1f - (x - 14) / 50f);
                if (x <= 2 && top >= 14) a = Mathf.Max(a, 1f - (top - 14) / 50f);
                if ((top == 7 || top == 8) && x >= 18 && x <= 34) a = Mathf.Max(a, 0.6f * (1f - (x - 18) / 16f));
                if ((x == 7 || x == 8) && top >= 18 && top <= 34) a = Mathf.Max(a, 0.6f * (1f - (top - 18) / 16f));
                int manhattan = Mathf.Abs(x - 8) + Mathf.Abs(top - 8);
                if (manhattan >= 3 && manhattan <= 7) a = 1f;
                return a;
            });

        private static Sprite BuildCoin()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "FX_Coin",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            Color rimDark = new(0.68f, 0.42f, 0.06f, 1f);
            Color rimBright = new(1.00f, 0.88f, 0.36f, 1f);
            Color discWarm = new(0.96f, 0.72f, 0.14f, 1f);
            Color discDeep = new(0.84f, 0.56f, 0.08f, 1f);
            Color emblemBright = new(1.00f, 0.96f, 0.68f, 1f);
            Color specular = new(1.00f, 0.99f, 0.85f, 1f);

            var pixels = new Color32[size * size];
            const float center = 31.5f;
            const float outerRadius = 28.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01((outerRadius - dist) * 1.4f);
                    if (alpha <= 0f)
                    {
                        pixels[y * size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    // Directional light from top-left for metallic coin depth
                    float light = Mathf.Clamp01(0.5f + (-dx + dy) / (outerRadius * 2.2f));
                    Color col;

                    if (dist > 25.2f)
                    {
                        col = Color.Lerp(rimDark, rimBright, light * 0.45f);
                    }
                    else if (dist > 22.0f)
                    {
                        col = Color.Lerp(discWarm, rimBright, 0.55f + light * 0.45f);
                    }
                    else
                    {
                        col = Color.Lerp(discDeep, discWarm, light);

                        // Recessed circular inner groove
                        float groove = Mathf.Abs(dist - 18.8f);
                        if (groove < 1.35f)
                        {
                            float g = 1f - groove / 1.35f;
                            col = Color.Lerp(col, rimDark, g * 0.65f);
                        }

                        // Central embossed diamond emblem
                        float diamond = Mathf.Abs(dx) + Mathf.Abs(dy);
                        if (diamond < 9.5f)
                        {
                            float bevel = diamond > 7.2f ? 0.35f : 0.85f;
                            col = Color.Lerp(col, emblemBright, bevel * (0.55f + light * 0.45f));
                        }
                    }

                    // Subtle top-left specular gleam
                    float specDx = dx + 9.5f;
                    float specDy = dy - 9.5f;
                    float specDist = Mathf.Sqrt(specDx * specDx + specDy * specDy);
                    if (specDist < 9.5f && dist < 24.5f)
                    {
                        float spec = Mathf.Pow(1f - specDist / 9.5f, 2f);
                        col = Color.Lerp(col, specular, spec * 0.45f);
                    }

                    col.a = alpha;
                    pixels[y * size + x] = col;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect);
            sprite.name = "FX_Coin";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Sprite BuildShieldEmblem() =>
            Build("FX_ShieldEmblem", 64, 64, (x, y) =>
            {
                float nx = Mathf.Abs((x - 31.5f) / 22f);
                float ny = (y - 31.5f) / 24f;
                if (ny > 0.85f || ny < -0.95f) return 0f;
                float maxW = ny >= -0.1f ? 0.95f : Mathf.Lerp(0.95f, 0f, (-0.1f - ny) / 0.85f);
                if (nx > maxW) return 0f;
                float edge = Mathf.Min(maxW - nx, Mathf.Min(0.85f - ny, ny + 0.95f));
                return edge < 0.14f ? 1f : 0.72f;
            });

        private static Sprite BuildSweatDrop() =>
            Build("FX_SweatDrop", 64, 64, (x, y) =>
            {
                float nx = (x - 31.5f) / 18f;
                float ny = (y - 24f) / 26f;
                if (ny > 1f || ny < -0.75f) return 0f;
                float widthAtY = ny >= 0f ? Mathf.Lerp(0.92f, 0.05f, ny) : Mathf.Sqrt(Mathf.Max(0f, 1f - (ny / 0.75f) * (ny / 0.75f))) * 0.92f;
                float dist = Mathf.Abs(nx) / Mathf.Max(0.05f, widthAtY);
                return dist <= 1f ? Mathf.Clamp01((1f - dist) * 3f) : 0f;
            });

        private static Sprite BuildAngerMark() =>
            Build("FX_AngerMark", 64, 64, (x, y) =>
            {
                float dx = x - 31.5f;
                float dy = y - 31.5f;
                float ax = Mathf.Abs(dx);
                float ay = Mathf.Abs(dy);
                if (ax < 4f && ay < 4f) return 0f;
                float d1 = Mathf.Abs(Mathf.Sqrt((ax - 16f) * (ax - 16f) + (ay - 16f) * (ay - 16f)) - 11f);
                return d1 < 3.2f && ax > 4f && ay > 4f ? Mathf.Clamp01(1f - d1 / 3.2f) : 0f;
            });

        private static Sprite BuildExclaimBurst() =>
            Build("FX_ExclaimBurst", 64, 64, (x, y) =>
            {
                float dx = Mathf.Abs(x - 31.5f);
                float dy = y - 31.5f;
                bool stem = dx < 4.5f - (14f - Mathf.Clamp(dy, -6f, 18f)) * 0.08f && dy >= -4f && dy <= 22f;
                bool dot = dx * dx + (dy + 15f) * (dy + 15f) <= 28f;
                return (stem || dot) ? 1f : 0f;
            });

        private static Sprite LoadOrBuildBrandLogo()
        {
            var tex = Resources.Load<Texture2D>("UI/BrandLogo_KnowledgeWarrior");
            if (tex != null)
            {
                var sprite = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                sprite.name = "BrandLogo_KnowledgeWarrior";
                return sprite;
            }

            var loaded = Resources.Load<Sprite>("UI/BrandLogo_KnowledgeWarrior");
            return loaded != null ? loaded : ShieldEmblem;
        }

        private static Sprite Build(string name, int width, int height, Func<int, int, float> alpha, Vector4 border = default)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(x, y)) * 255f);
                    pixels[y * width + x] = new Color32(255, 255, 255, a);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, border);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
