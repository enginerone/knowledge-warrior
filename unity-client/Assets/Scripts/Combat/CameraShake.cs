using UnityEngine;

namespace KnowledgeWarrior.Combat
{
    /// <summary>Lightweight trauma-style camera shake for hit impact.</summary>
    [RequireComponent(typeof(Camera))]
    public class CameraShake : MonoBehaviour
    {
        [SerializeField] private float frequency = 28f;

        private Vector3 _basePosition;
        private float _timeLeft;
        private float _duration;
        private float _strength;

        private void Awake()
        {
            _basePosition = transform.localPosition;
        }

        public void Shake(float duration, float strength)
        {
            // Keep the stronger shake if one is already running.
            if (_timeLeft > 0f && strength < _strength * (_timeLeft / _duration)) return;
            _duration = Mathf.Max(0.01f, duration);
            _timeLeft = _duration;
            _strength = strength;
        }

        private void LateUpdate()
        {
            if (_timeLeft <= 0f)
            {
                transform.localPosition = _basePosition;
                return;
            }

            _timeLeft -= Time.deltaTime;
            float damper = Mathf.Clamp01(_timeLeft / _duration);
            float t = Time.time * frequency;
            var offset = new Vector3(
                (Mathf.PerlinNoise(t, 0.1f) - 0.5f) * 2f,
                (Mathf.PerlinNoise(0.1f, t) - 0.5f) * 2f,
                0f) * (_strength * damper * damper);
            transform.localPosition = _basePosition + offset;
        }
    }
}
