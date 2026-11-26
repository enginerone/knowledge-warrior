using UnityEngine;

namespace KnowledgeWarrior.UI
{
    /// <summary>Shared accent colors used by the decorative UI effects.</summary>
    public static class UIPalette
    {
        public static readonly Color Cyan = new(0.30f, 0.88f, 1f, 1f);
        public static readonly Color Violet = new(0.61f, 0.42f, 1f, 1f);
        public static readonly Color Gold = new(1f, 0.82f, 0.30f, 1f);
        public static readonly Color Green = new(0.24f, 0.90f, 0.63f, 1f);
        public static readonly Color Orange = new(1f, 0.55f, 0.28f, 1f);
        public static readonly Color Pink = new(1f, 0.42f, 0.68f, 1f);
        public static readonly Color Red = new(1f, 0.36f, 0.48f, 1f);

        /// <summary>Soft mix of accents used for floating ambient motes.</summary>
        public static readonly Color[] Ambient = { Cyan, Violet, Gold, Pink };

        public static Color WithAlpha(Color color, float alpha) => new(color.r, color.g, color.b, alpha);
    }
}
