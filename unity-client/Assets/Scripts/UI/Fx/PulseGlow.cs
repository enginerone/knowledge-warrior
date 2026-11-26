using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>
    /// Breathes the opacity of a graphic or canvas group. Uses the renderer alpha for graphics so
    /// the graphic's own color is never touched.
    /// </summary>
    public class PulseGlow : MonoBehaviour
    {
        private float _minAlpha = 0.5f;
        private float _maxAlpha = 1f;
        private float _speed = 1.6f;

        private CanvasGroup _group;
        private Graphic _graphic;
        private float _phase;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            _graphic = GetComponent<Graphic>();
            _phase = Random.value * Mathf.PI * 2f;
        }

        public void Configure(float minAlpha, float maxAlpha, float speed)
        {
            _minAlpha = minAlpha;
            _maxAlpha = maxAlpha;
            _speed = speed;
        }

        private void OnDisable()
        {
            if (_group != null) _group.alpha = 1f;
            else if (_graphic != null) _graphic.canvasRenderer.SetAlpha(1f);
        }

        private void Update()
        {
            float wave = (Mathf.Sin(Time.unscaledTime * _speed + _phase) + 1f) * 0.5f;
            float alpha = Mathf.Lerp(_minAlpha, _maxAlpha, wave);
            if (_group != null) _group.alpha = alpha;
            else if (_graphic != null) _graphic.canvasRenderer.SetAlpha(alpha);
        }
    }
}
