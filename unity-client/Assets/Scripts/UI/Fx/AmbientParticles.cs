using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>
    /// Slowly rising, twinkling motes behind a panel or the battle HUD. Runs on its own nested
    /// canvas so moving motes never force the parent canvas to rebuild its mesh.
    /// </summary>
    public class AmbientParticles : MonoBehaviour
    {
        private struct Mote
        {
            public RectTransform rect;
            public CanvasRenderer renderer;
            public float x01;
            public float y01;
            public float risePixels;
            public float swayAmplitude;
            public float swayFrequency;
            public float phase;
            public float baseAlpha;
            public float twinkleSpeed;
        }

        private Mote[] _motes;
        private RectTransform _rect;
        private CanvasGroup _parentGroup;

        public static AmbientParticles Create(RectTransform parent, int count, Color[] palette, bool stars,
            float minSize, float maxSize)
        {
            var go = new GameObject("FX_Ambient", typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.SetAsFirstSibling();

            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            go.AddComponent<Canvas>();
            var particles = go.AddComponent<AmbientParticles>();
            particles.Build(count, palette, stars, minSize, maxSize);
            return particles;
        }

        private void Build(int count, Color[] palette, bool stars, float minSize, float maxSize)
        {
            _rect = (RectTransform)transform;
            _motes = new Mote[count];

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Mote", typeof(RectTransform));
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false);

                var image = go.AddComponent<Image>();
                image.sprite = stars && i % 3 == 0 ? UISprites.Star : UISprites.Radial;
                image.color = palette[Random.Range(0, palette.Length)];
                image.raycastTarget = false;

                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                float size = Random.Range(minSize, maxSize);
                rt.sizeDelta = new Vector2(size, size);

                _motes[i] = new Mote
                {
                    rect = rt,
                    renderer = image.canvasRenderer,
                    x01 = Random.value,
                    y01 = Random.value,
                    risePixels = Random.Range(18f, 55f),
                    swayAmplitude = Random.Range(10f, 36f),
                    swayFrequency = Random.Range(0.4f, 1.1f),
                    phase = Random.value * Mathf.PI * 2f,
                    baseAlpha = Random.Range(0.25f, 0.65f),
                    twinkleSpeed = Random.Range(1.2f, 3.2f),
                };
            }
        }

        private void Start() => _parentGroup = GetComponentInParent<CanvasGroup>();

        private void Update()
        {
            if (_motes == null) return;
            if (_parentGroup != null && _parentGroup.alpha < 0.01f) return;

            float width = _rect.rect.width;
            float height = _rect.rect.height;
            if (width < 1f || height < 1f) return;

            float dt = Time.unscaledDeltaTime;
            float time = Time.unscaledTime;

            for (int i = 0; i < _motes.Length; i++)
            {
                ref var m = ref _motes[i];
                m.y01 += m.risePixels / height * dt;
                if (m.y01 > 1.05f)
                {
                    m.y01 = -0.05f;
                    m.x01 = Random.value;
                }

                float sway = Mathf.Sin(time * m.swayFrequency + m.phase) * m.swayAmplitude;
                m.rect.anchoredPosition = new Vector2((m.x01 - 0.5f) * width + sway, (m.y01 - 0.5f) * height);

                float edgeFade = Mathf.Clamp01(Mathf.Min(m.y01 * 6f, (1f - m.y01) * 6f));
                float twinkle = 0.45f + 0.55f * Mathf.Sin(time * m.twinkleSpeed + m.phase);
                m.renderer.SetAlpha(m.baseAlpha * twinkle * edgeFade);
            }
        }
    }
}
