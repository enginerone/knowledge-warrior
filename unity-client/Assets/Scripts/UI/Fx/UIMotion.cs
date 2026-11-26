using System.Collections;
using UnityEngine;

namespace KnowledgeWarrior.UI
{
    /// <summary>Small reusable motions for any UI rect: punch, pop-in and horizontal shake.</summary>
    public class UIMotion : MonoBehaviour
    {
        private Coroutine _scaleRoutine;
        private Coroutine _shakeRoutine;
        private Vector2 _shakeOrigin;
        private bool _shaking;

        /// <summary>Quick scale bump that settles back to normal size.</summary>
        public static void Punch(RectTransform rect, float amount = 0.12f, float duration = 0.28f) =>
            Of(rect)?.RunScale(PunchRoutine(rect, amount, duration));

        /// <summary>Overshooting pop from a smaller size to normal size.</summary>
        public static void Pop(RectTransform rect, float from = 0.9f, float duration = 0.28f) =>
            Of(rect)?.RunScale(PopRoutine(rect, from, duration));

        /// <summary>Damped horizontal shake in pixels.</summary>
        public static void Shake(RectTransform rect, float pixels = 18f, float duration = 0.38f)
        {
            var motion = Of(rect);
            if (motion == null) return;
            if (motion._shakeRoutine != null) motion.StopCoroutine(motion._shakeRoutine);
            if (motion._shaking) rect.anchoredPosition = motion._shakeOrigin;
            motion._shakeRoutine = motion.StartCoroutine(motion.ShakeRoutine(rect, pixels, duration));
        }

        private static UIMotion Of(RectTransform rect)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy) return null;
            var motion = rect.GetComponent<UIMotion>();
            return motion != null ? motion : rect.gameObject.AddComponent<UIMotion>();
        }

        private void OnDisable()
        {
            var rect = (RectTransform)transform;
            if (_shaking) rect.anchoredPosition = _shakeOrigin;
            _shaking = false;
            _scaleRoutine = _shakeRoutine = null;
        }

        private void RunScale(IEnumerator routine)
        {
            if (_scaleRoutine != null) StopCoroutine(_scaleRoutine);
            _scaleRoutine = StartCoroutine(routine);
        }

        private static IEnumerator PunchRoutine(RectTransform rect, float amount, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                rect.localScale = Vector3.one * (1f + amount * Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI));
                yield return null;
            }
            rect.localScale = Vector3.one;
        }

        private static IEnumerator PopRoutine(RectTransform rect, float from, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                rect.localScale = Vector3.one * Mathf.LerpUnclamped(from, 1f, EaseOutBack(Mathf.Clamp01(t / duration)));
                yield return null;
            }
            rect.localScale = Vector3.one;
        }

        private IEnumerator ShakeRoutine(RectTransform rect, float pixels, float duration)
        {
            _shakeOrigin = rect.anchoredPosition;
            _shaking = true;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                float offset = Mathf.Sin(k * Mathf.PI * 7f) * pixels * (1f - k);
                rect.anchoredPosition = _shakeOrigin + new Vector2(offset, 0f);
                yield return null;
            }
            rect.anchoredPosition = _shakeOrigin;
            _shaking = false;
        }

        public static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }
    }
}
