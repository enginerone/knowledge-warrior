using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>Expanding touch ripple that fades out and destroys itself.</summary>
    public class RippleFX : MonoBehaviour
    {
        private const float Duration = 0.55f;

        private RectTransform _rect;
        private CanvasRenderer _renderer;
        private float _age;
        private float _startAlpha;

        public static void Spawn(RectTransform clip, Vector2 localPosition, float maxSize, Color color)
        {
            var go = new GameObject("FX_Ripple", typeof(RectTransform));
            go.layer = clip.gameObject.layer;
            go.transform.SetParent(clip, false);

            var image = go.AddComponent<Image>();
            image.sprite = UISprites.Radial;
            image.color = new Color(color.r, color.g, color.b, 1f);
            image.raycastTarget = false;

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = localPosition;
            rt.sizeDelta = new Vector2(maxSize, maxSize);
            rt.localScale = Vector3.one * 0.05f;

            var ripple = go.AddComponent<RippleFX>();
            ripple._rect = rt;
            ripple._renderer = image.canvasRenderer;
            ripple._startAlpha = color.a;
            ripple._renderer.SetAlpha(color.a);
        }

        private void Update()
        {
            _age += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_age / Duration);
            float eased = 1f - Mathf.Pow(1f - k, 3f);
            _rect.localScale = Vector3.one * Mathf.Lerp(0.05f, 1f, eased);
            _renderer.SetAlpha(_startAlpha * (1f - k));
            if (k >= 1f) Destroy(gameObject);
        }
    }
}
