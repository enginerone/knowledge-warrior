using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>
    /// Adds decorative layers (gloss, glowing frame, shine sweep, ornamental corners) to any UI
    /// rect. Layers live inside a masked "clip host" that copies the target's sprite shape so
    /// effects never leak outside rounded panels. Every call is idempotent.
    /// </summary>
    public static class UIDecorator
    {
        public const string ClipName = "FX_Clip";
        private const string GlossName = "FX_Gloss";
        private const string ShadeName = "FX_Shade";
        private const string EdgeName = "FX_Edge";
        private const string HoverName = "FX_Hover";
        private const string RingName = "FX_Ring";
        private const string ShineName = "FX_Shine";
        private const string CornersName = "FX_Corners";

        /// <summary>Returns the masked container that holds every clipped effect of the target.</summary>
        public static RectTransform ClipHost(RectTransform target)
        {
            if (target.Find(ClipName) is RectTransform existing) return existing;

            var clip = NewRect(ClipName, target);
            Stretch(clip);

            var image = clip.gameObject.AddComponent<Image>();
            var source = target.GetComponent<Image>();
            if (source != null && source.sprite != null)
            {
                image.sprite = source.sprite;
                image.type = source.type;
                image.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
            }
            image.color = Color.white;
            image.raycastTarget = false;

            var mask = clip.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            clip.gameObject.AddComponent<FxPanel>();
            clip.SetAsFirstSibling();
            return clip;
        }

        /// <summary>Adds a light top gloss and an optional dark bottom shade.</summary>
        public static void AddGloss(RectTransform target, float glossAlpha, float shadeAlpha, float heightFraction)
        {
            var clip = ClipHost(target);

            if (clip.Find(GlossName) == null)
            {
                var gloss = NewImage(GlossName, clip, UISprites.VerticalGradient, new Color(1f, 1f, 1f, glossAlpha));
                var rt = gloss.rectTransform;
                rt.anchorMin = new Vector2(0f, 1f - heightFraction);
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }

            if (shadeAlpha > 0f && clip.Find(ShadeName) == null)
            {
                var shade = NewImage(ShadeName, clip, UISprites.VerticalGradient, new Color(0f, 0f, 0.05f, shadeAlpha));
                var rt = shade.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = new Vector2(1f, heightFraction);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                rt.localScale = new Vector3(1f, -1f, 1f);
            }
        }

        /// <summary>Adds a glowing inner frame and returns its image so callers can recolor it.</summary>
        public static Image AddRing(RectTransform target, Color color)
        {
            var clip = ClipHost(target);
            if (clip.Find(RingName) is RectTransform existing) return existing.GetComponent<Image>();

            var ring = NewImage(RingName, clip, UISprites.Ring, color);
            ring.type = Image.Type.Sliced;
            Stretch(ring.rectTransform);
            clip.GetComponent<FxPanel>().ring = ring;
            return ring;
        }

        /// <summary>Adds a diagonal shine band that sweeps across the target.</summary>
        public static ShineSweep AddShine(RectTransform target, float alpha, bool idle, float minInterval, float maxInterval)
        {
            var clip = ClipHost(target);
            if (clip.Find(ShineName) is RectTransform existing) return existing.GetComponent<ShineSweep>();

            var band = NewImage(ShineName, clip, UISprites.ShineBand, new Color(1f, 1f, 1f, alpha));
            var sweep = band.gameObject.AddComponent<ShineSweep>();
            sweep.Configure(idle, minInterval, maxInterval);
            return sweep;
        }

        /// <summary>Adds four mirrored ornamental brackets that gently pulse.</summary>
        public static void AddCorners(RectTransform target, Color color, float size)
        {
            if (target.Find(CornersName) != null) return;

            var host = NewRect(CornersName, target);
            Stretch(host);
            host.SetSiblingIndex(Mathf.Min(1, target.childCount - 1));

            var group = host.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            host.gameObject.AddComponent<PulseGlow>().Configure(0.55f, 1f, 1.4f);

            const float inset = 8f;
            float offset = inset + size * 0.5f;
            var anchors = new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) };
            var flips = new[] { new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f) };

            var images = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                var corner = NewImage("Corner_" + i, host, UISprites.Corner, Color.Lerp(color, Color.white, 0.35f));
                var rt = corner.rectTransform;
                rt.anchorMin = rt.anchorMax = anchors[i];
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = new Vector2(
                    anchors[i].x < 0.5f ? offset : -offset,
                    anchors[i].y < 0.5f ? offset : -offset);
                rt.localScale = new Vector3(flips[i].x, flips[i].y, 1f);
                images[i] = corner;
            }

            ClipHost(target).GetComponent<FxPanel>().corners = images;
        }

        /// <summary>Full decorative treatment for a window, card or HUD panel.</summary>
        public static FxPanel DecoratePanel(RectTransform target, Color accent, float cornerSize = 40f,
            float gloss = 0.10f, bool corners = true, bool shine = true)
        {
            if (target == null) return null;

            AddGloss(target, gloss, 0f, 0.6f);
            AddRing(target, UIPalette.WithAlpha(accent, 0.55f));
            if (shine) AddShine(target, 0.10f, true, 6f, 11f);
            if (corners && cornerSize > 0f) AddCorners(target, accent, cornerSize);

            var panel = FxPanel.Of(target);
            panel?.SetAccent(accent);
            return panel;
        }

        /// <summary>Bevel, top highlight line and hover overlay for a button. Returns the hover overlay.</summary>
        public static Image DecorateButton(RectTransform target)
        {
            var clip = ClipHost(target);
            AddGloss(target, 0.20f, 0.22f, 0.55f);

            if (clip.Find(EdgeName) == null)
            {
                var edge = NewImage(EdgeName, clip, null, new Color(1f, 1f, 1f, 0.35f));
                var rt = edge.rectTransform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(8f, -2f);
                rt.offsetMax = new Vector2(-8f, 0f);
            }

            if (clip.Find(HoverName) is RectTransform existing) return existing.GetComponent<Image>();

            var hover = NewImage(HoverName, clip, null, Color.white);
            Stretch(hover.rectTransform);
            hover.canvasRenderer.SetAlpha(0f);
            return hover;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var rt = NewRect(name, parent);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
