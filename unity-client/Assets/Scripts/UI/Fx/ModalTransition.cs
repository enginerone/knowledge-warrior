using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KnowledgeWarrior.UI
{
    /// <summary>
    /// Animated open and close for a modal backdrop: fade, window slide-up with overshoot and
    /// staggered card entrances. Lives on the same object as the modal's <see cref="CanvasGroup"/>.
    /// </summary>
    public class ModalTransition : MonoBehaviour
    {
        private const float OpenTime = 0.30f;
        private const float CloseTime = 0.16f;
        private const float SlideOffset = 28f;
        private const float StaggerStep = 0.05f;
        private const float StaggerTime = 0.28f;

        private CanvasGroup _group;
        private RectTransform _content;
        private Vector2 _contentOrigin;
        private bool _originCaptured;
        private bool _visible;
        private Coroutine _routine;
        private readonly List<RectTransform> _stagger = new();

        public static ModalTransition For(CanvasGroup group, out bool created)
        {
            var transition = group.GetComponent<ModalTransition>();
            created = transition == null;
            if (created)
            {
                transition = group.gameObject.AddComponent<ModalTransition>();
                transition._group = group;
                transition._visible = false;
            }
            return transition;
        }

        public void SetStagger(IEnumerable<RectTransform> targets)
        {
            _stagger.Clear();
            _stagger.AddRange(targets);
        }

        public void Open()
        {
            _group.interactable = true;
            _group.blocksRaycasts = true;
            if (_visible && _group.alpha >= 0.99f) return;

            _visible = true;
            StopRoutine();
            if (!isActiveAndEnabled)
            {
                SnapTo(1f);
                return;
            }
            _routine = StartCoroutine(OpenRoutine());
        }

        public void Close()
        {
            _group.interactable = false;
            _group.blocksRaycasts = false;
            if (!_visible && _group.alpha <= 0.001f) return;

            _visible = false;
            StopRoutine();
            if (!isActiveAndEnabled)
            {
                SnapTo(0f);
                return;
            }
            _routine = StartCoroutine(CloseRoutine());
        }

        public void SnapHidden()
        {
            _visible = false;
            StopRoutine();
            _group.interactable = false;
            _group.blocksRaycasts = false;
            SnapTo(0f);
        }

        private void OnDisable()
        {
            StopRoutine();
            SnapTo(_visible ? 1f : 0f);
        }

        private void StopRoutine()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
        }

        private RectTransform ResolveContent()
        {
            if (_content != null) return _content;
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.name.StartsWith("FX_")) continue;
                _content = child as RectTransform;
                break;
            }
            if (_content != null && !_originCaptured)
            {
                _contentOrigin = _content.anchoredPosition;
                _originCaptured = true;
            }
            return _content;
        }

        private void SnapTo(float alpha)
        {
            _group.alpha = alpha;
            var content = ResolveContent();
            if (content != null)
            {
                content.localScale = Vector3.one;
                content.anchoredPosition = _contentOrigin;
            }
            foreach (var target in _stagger)
            {
                if (target == null) continue;
                target.localScale = Vector3.one;
                var cg = target.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = 1f;
            }
        }

        private IEnumerator OpenRoutine()
        {
            var content = ResolveContent();
            var cards = new List<(RectTransform rect, CanvasGroup group)>();
            foreach (var target in _stagger)
            {
                if (target == null || !target.gameObject.activeSelf) continue;
                var cg = target.GetComponent<CanvasGroup>();
                if (cg == null) cg = target.gameObject.AddComponent<CanvasGroup>();
                cg.alpha = 0f;
                target.localScale = Vector3.one * 0.85f;
                cards.Add((target, cg));
            }

            float startAlpha = _group.alpha;
            float total = OpenTime + StaggerStep * cards.Count + StaggerTime;
            float t = 0f;
            while (t < total)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / OpenTime);
                _group.alpha = Mathf.Lerp(startAlpha, 1f, k);
                if (content != null)
                {
                    content.localScale = Vector3.one * Mathf.LerpUnclamped(0.94f, 1f, UIMotion.EaseOutBack(k));
                    content.anchoredPosition = _contentOrigin + new Vector2(0f, SlideOffset * (1f - k) * (1f - k));
                }

                for (int i = 0; i < cards.Count; i++)
                {
                    float ck = Mathf.Clamp01((t - 0.08f - i * StaggerStep) / StaggerTime);
                    cards[i].group.alpha = ck;
                    cards[i].rect.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1f, UIMotion.EaseOutBack(ck));
                }
                yield return null;
            }

            SnapTo(1f);
            _routine = null;
        }

        private IEnumerator CloseRoutine()
        {
            var content = ResolveContent();
            float startAlpha = _group.alpha;
            float t = 0f;
            while (t < CloseTime)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / CloseTime);
                _group.alpha = Mathf.Lerp(startAlpha, 0f, k);
                if (content != null) content.localScale = Vector3.one * Mathf.Lerp(1f, 0.97f, k);
                yield return null;
            }

            SnapTo(0f);
            _routine = null;
        }
    }
}
