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
