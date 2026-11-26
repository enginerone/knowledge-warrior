using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>Diagonal light band that sweeps across its parent, on demand or at random intervals.</summary>
    [RequireComponent(typeof(Image))]
    public class ShineSweep : MonoBehaviour
    {
        private const float Duration = 0.75f;

        private float _minInterval = 5f;
        private float _maxInterval = 9f;
        private bool _idle = true;

        private RectTransform _rect;
        private RectTransform _host;
        private Image _image;
        private float _wait;
        private float _time = -1f;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _host = _rect.parent as RectTransform;
            _image = GetComponent<Image>();
            _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0.5f, 0.5f);
            _rect.localRotation = Quaternion.Euler(0f, 0f, 16f);
            _image.canvasRenderer.SetAlpha(0f);
            _wait = Random.Range(1f, _maxInterval);
        }

        private void OnDisable()
        {
            _time = -1f;
            if (_image != null) _image.canvasRenderer.SetAlpha(0f);
        }

        public void Configure(bool idle, float minInterval, float maxInterval)
        {
            _idle = idle;
            _minInterval = minInterval;
            _maxInterval = Mathf.Max(minInterval, maxInterval);
        }

        public void Trigger() => _time = 0f;

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_time < 0f)
            {
                if (!_idle) return;
                _wait -= dt;
                if (_wait > 0f) return;
                Trigger();
                _wait = Random.Range(_minInterval, _maxInterval);
                return;
            }

            _time += dt;
            float k = _time / Duration;
            if (k >= 1f)
            {
                _time = -1f;
                _image.canvasRenderer.SetAlpha(0f);
                return;
            }

            float width = _host.rect.width;
            float height = _host.rect.height;
            float band = Mathf.Max(60f, width * 0.28f);
            float eased = 1f - (1f - k) * (1f - k);
            _rect.sizeDelta = new Vector2(band, height * 2.2f);
            _rect.anchoredPosition = new Vector2(Mathf.Lerp(-(width * 0.5f + band), width * 0.5f + band, eased), 0f);
            _image.canvasRenderer.SetAlpha(Mathf.Sin(k * Mathf.PI));
        }
    }
}
