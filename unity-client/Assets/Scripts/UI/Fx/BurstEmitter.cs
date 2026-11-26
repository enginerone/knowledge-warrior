using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>
    /// Simulates a short-lived set of UI particles on its own nested canvas and destroys itself
    /// when every particle has faded. Created through <see cref="UIBurst"/>.
    /// </summary>
    public class BurstEmitter : MonoBehaviour
    {
        private class Particle
        {
            public RectTransform rect;
            public CanvasRenderer renderer;
            public Vector2 position;
            public Vector2 velocity;
            public float gravity;
            public float spin;
            public float life;
            public float age;
        }

        private readonly List<Particle> _particles = new();
        private RectTransform _root;
        private RectTransform _rect;

        public static BurstEmitter Create(RectTransform source)
        {
            if (source == null) return null;
            var canvas = source.GetComponentInParent<Canvas>();
            if (canvas == null) return null;

            var rootRect = canvas.rootCanvas.transform as RectTransform;
            var go = new GameObject("FX_Burst", typeof(RectTransform));
            go.layer = rootRect.gameObject.layer;
            go.transform.SetParent(rootRect, false);
            go.transform.SetAsLastSibling();

            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.AddComponent<Canvas>();

            var emitter = go.AddComponent<BurstEmitter>();
            emitter._root = rootRect;
            emitter._rect = rt;
            return emitter;
        }

        public Vector2 LocalPointOf(RectTransform source, Vector2 pointInSource)
        {
            Vector3 world = source.TransformPoint(pointInSource);
            Vector3 local = _rect.InverseTransformPoint(world);
            return new Vector2(local.x, local.y);
        }

        public void Add(Sprite sprite, Color color, Vector2 position, Vector2 velocity, float size,
            float life, float gravity, float spin, bool rectangle)
        {
            var go = new GameObject("Particle", typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);

            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = rectangle ? new Vector2(size, size * 0.55f) : new Vector2(size, size);
            rt.anchoredPosition = position;
            rt.localRotation = Quaternion.Euler(0f, 0f, Random.value * 360f);

            _particles.Add(new Particle
            {
                rect = rt,
                renderer = image.canvasRenderer,
                position = position,
                velocity = velocity,
                gravity = gravity,
                spin = spin,
                life = life,
            });
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            bool anyAlive = false;

            foreach (var p in _particles)
            {
                p.age += dt;
                float k = p.age / p.life;
                if (k >= 1f)
                {
                    if (p.rect != null) p.rect.gameObject.SetActive(false);
                    continue;
                }

                anyAlive = true;
                p.velocity.y -= p.gravity * dt;
                p.velocity *= 1f - 1.4f * dt;
                p.position += p.velocity * dt;
                p.rect.anchoredPosition = p.position;
                p.rect.localRotation *= Quaternion.Euler(0f, 0f, p.spin * dt);
                p.rect.localScale = Vector3.one * Mathf.Lerp(1f, 0.4f, k * k);
                p.renderer.SetAlpha(1f - k * k);
            }

            if (!anyAlive && _particles.Count > 0) Destroy(gameObject);
        }
    }
}
