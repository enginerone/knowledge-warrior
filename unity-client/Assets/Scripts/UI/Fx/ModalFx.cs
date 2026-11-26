using System.Collections.Generic;
using UnityEngine;

namespace KnowledgeWarrior.UI
{
    /// <summary>Entry points modals use for animated presentation and decoration.</summary>
    public static class ModalFx
    {
        private const string AmbientName = "FX_Ambient";

        /// <summary>Shows or hides a modal backdrop with animation. The first hide is applied instantly.</summary>
        public static void Present(CanvasGroup group, bool visible)
        {
            if (group == null) return;
            var transition = ModalTransition.For(group, out bool created);
            if (visible) transition.Open();
            else if (created) transition.SnapHidden();
            else transition.Close();
        }

        /// <summary>Cards that should pop in one after another whenever the modal opens.</summary>
        public static void SetStagger(CanvasGroup group, IEnumerable<RectTransform> targets)
        {
            if (group == null) return;
            var transition = ModalTransition.For(group, out bool created);
            transition.SetStagger(targets);
            if (created) transition.SnapHidden();
        }

        /// <summary>Adds ambient motes behind the window and an ornamental frame to the window itself.</summary>
        public static void Dress(CanvasGroup group, Color accent, bool particles = true)
        {
            if (group == null) return;
            var backdrop = (RectTransform)group.transform;
            if (backdrop.Find(AmbientName) != null) return;

            RectTransform window = null;
            for (int i = 0; i < backdrop.childCount; i++)
            {
                window = backdrop.GetChild(i) as RectTransform;
                if (window != null) break;
            }

            if (window != null)
            {
                window.gameObject.AddComponent<PulseGlow>().Configure(0.6f, 1f, 1.2f);
                var fill = FillOf(window);
                if (fill != null) UIDecorator.DecoratePanel(fill, accent, 44f, 0.08f);
            }

            if (particles) AmbientParticles.Create(backdrop, 16, UIPalette.Ambient, true, 6f, 18f);
        }

        /// <summary>
        /// Decorates every descendant whose name matches. A trailing asterisk matches by prefix.
        /// Border and fill pairs are decorated on the fill.
        /// </summary>
        public static void DecorateByName(RectTransform root, Color accent, bool corners, params string[] names)
        {
            foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect.name.StartsWith("FX_") || !Matches(rect.name, names)) continue;
                var target = FillOf(rect) ?? rect;
                UIDecorator.DecoratePanel(target, accent, 26f, 0.10f, corners);
            }
        }

        private static bool Matches(string name, string[] patterns)
        {
            foreach (var pattern in patterns)
            {
                if (pattern.EndsWith("*") ? name.StartsWith(pattern.Substring(0, pattern.Length - 1)) : name == pattern)
                    return true;
            }
            return false;
        }

        /// <summary>Returns the inner fill of a border and fill pair, or null for a single rect.</summary>
        private static RectTransform FillOf(RectTransform border)
        {
            if (border.Find("Fill") is RectTransform named) return named;
            if (!border.name.EndsWith("Border")) return null;
            for (int i = 0; i < border.childCount; i++)
            {
                var child = border.GetChild(i);
                if (!child.name.StartsWith("FX_")) return child as RectTransform;
            }
            return null;
        }
    }
}
