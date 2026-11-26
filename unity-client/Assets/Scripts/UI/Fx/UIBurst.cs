using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>One-shot particle bursts (sparks and confetti) drawn above the whole canvas.</summary>
    public static class UIBurst
    {
        private static readonly Color[] ConfettiColors =
        {
            UIPalette.Gold, UIPalette.Cyan, UIPalette.Pink, UIPalette.Green, UIPalette.Violet,
        };

        /// <summary>Radial sparkle burst from the center of a UI element.</summary>
        public static void Sparks(RectTransform source, Color color, int count = 14, float speed = 520f)
        {
            var emitter = BurstEmitter.Create(source);
            if (emitter == null) return;
            Vector2 origin = emitter.LocalPointOf(source, source.rect.center);

            for (int i = 0; i < count; i++)
            {
                float angle = Random.value * Mathf.PI * 2f;
                float velocity = speed * Random.Range(0.35f, 1f);
                emitter.Add(UISprites.Star, color, origin,
                    new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * velocity,
                    Random.Range(22f, 46f), Random.Range(0.5f, 0.85f), 260f, Random.Range(-180f, 180f), false);
            }
        }

        /// <summary>Falling confetti shower from the top edge of a UI element.</summary>
        public static void Confetti(RectTransform source, int count = 42)
        {
            var emitter = BurstEmitter.Create(source);
            if (emitter == null) return;

            for (int i = 0; i < count; i++)
            {
                var local = new Vector2(Random.Range(source.rect.xMin, source.rect.xMax), source.rect.yMax - 20f);
                Vector2 origin = emitter.LocalPointOf(source, local);
                var velocity = new Vector2(Random.Range(-260f, 260f), Random.Range(380f, 900f));
                emitter.Add(null, ConfettiColors[Random.Range(0, ConfettiColors.Length)], origin, velocity,
                    Random.Range(14f, 26f), Random.Range(1.3f, 2.1f), 980f, Random.Range(-420f, 420f), true);
            }
        }
    }
}
