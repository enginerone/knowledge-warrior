using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>
    /// Handle for the decorative layers added to a panel by <see cref="UIDecorator"/>.
    /// Lives on the panel's clip host so the accent color can be changed later.
    /// </summary>
    public class FxPanel : MonoBehaviour
    {
        private const float RingAlpha = 0.55f;

        public Image ring;
        public Image[] corners;

        /// <summary>Returns the decoration handle of a panel, or null when it was never decorated.</summary>
        public static FxPanel Of(RectTransform target)
        {
            if (target == null) return null;
            var clip = target.Find(UIDecorator.ClipName);
            return clip != null ? clip.GetComponent<FxPanel>() : null;
        }

        public void SetAccent(Color accent)
        {
            if (ring != null) ring.color = UIPalette.WithAlpha(accent, RingAlpha);
            if (corners == null) return;
            Color tint = Color.Lerp(accent, Color.white, 0.35f);
            foreach (var corner in corners)
            {
                if (corner != null) corner.color = tint;
            }
        }

        public void SetRingAlpha(float alpha)
        {
            if (ring == null) return;
            var c = ring.color;
            c.a = alpha;
            ring.color = c;
        }
    }
}
