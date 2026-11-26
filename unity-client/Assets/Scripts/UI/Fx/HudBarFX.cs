using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>
    /// Glassy shimmer, a glowing leading edge and a low-health warning pulse for HP and XP bar fills.
    /// </summary>
    public class HudBarFX : MonoBehaviour
    {
        private RectTransform _rect;
        private Image _image;
        private Image _edge;
        private bool _lowPulse;

        public static HudBarFX Install(RectTransform fill, bool lowPulse)
        {
            if (fill == null) return null;
            var existing = fill.GetComponent<HudBarFX>();
            if (existing != null) return existing;

            var fx = fill.gameObject.AddComponent<HudBarFX>();
            fx._rect = fill;
            fx._image = fill.GetComponent<Image>();
            fx._lowPulse = lowPulse;

            UIDecorator.AddGloss(fill, 0.38f, 0.16f, 0.55f);
            UIDecorator.AddShine(fill, 0.30f, true, 2.5f, 5f);
            fx.BuildEdgeGlow();
            return fx;
        }

        private void BuildEdgeGlow()
        {
            var go = new GameObject("FX_BarEdge", typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);

            _edge = go.AddComponent<Image>();
            _edge.sprite = UISprites.Radial;
            _edge.raycastTarget = false;
            Color baseColor = _image != null ? _image.color : Color.white;
            _edge.color = UIPalette.WithAlpha(Color.Lerp(baseColor, Color.white, 0.55f), 0.6f);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(44f, 22f);
            rt.anchoredPosition = Vector2.zero;

            go.AddComponent<PulseGlow>().Configure(0.35f, 1f, 3f);
        }

        private void Update()
        {
            float ratio = _rect.anchorMax.x;
            if (_edge != null) _edge.enabled = ratio > 0.02f && ratio < 0.995f;

            if (_image == null) return;
            if (_lowPulse && ratio > 0f && ratio < 0.3f)
            {
                float wave = (Mathf.Sin(Time.unscaledTime * 7f) + 1f) * 0.5f;
                _image.canvasRenderer.SetAlpha(Mathf.Lerp(0.55f, 1f, wave));
            }
            else
            {
                _image.canvasRenderer.SetAlpha(1f);
            }
        }
    }
}
