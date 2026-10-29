using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>One of the four multiple-choice buttons (A/B/C/D).</summary>
    [RequireComponent(typeof(Button))]
    public class AnswerButton : MonoBehaviour
    {
        public enum VisualState { Normal, Correct, Wrong, Dimmed }

        [SerializeField] private Button button;
        [SerializeField] private Image background;
        [SerializeField] private Image letterBadge;
        [SerializeField] private TMP_Text letterText;
        [SerializeField] private TMP_Text label;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Colors")]
        [SerializeField] private Color normalColor = new(0.11f, 0.14f, 0.32f, 0.96f);
        [SerializeField] private Color badgeColor = new(0.30f, 0.88f, 1f, 1f);
        [SerializeField] private Color correctColor = new(0.17f, 0.80f, 0.60f, 1f);
        [SerializeField] private Color wrongColor = new(1f, 0.36f, 0.48f, 1f);

        private int _index;
        private Action<int> _onClick;
        private Coroutine _popRoutine;

        public Button Button => button;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            button.onClick.AddListener(() => _onClick?.Invoke(_index));
        }

        public void Bind(int index, Action<int> onClick)
        {
            _index = index;
            _onClick = onClick;
        }

        public void SetContent(string letter, string text)
        {
            if (letterText != null) letterText.text = letter;
            if (label != null) label.text = text;
            SetState(VisualState.Normal);
        }

        public void SetInteractable(bool value) => button.interactable = value;

        public void SetState(VisualState state)
        {
            Color bg = normalColor, badge = badgeColor;
            float alpha = 1f;
            switch (state)
            {
                case VisualState.Correct: bg = correctColor; badge = Color.white; break;
                case VisualState.Wrong: bg = wrongColor; badge = Color.white; break;
                case VisualState.Dimmed: alpha = 0.45f; break;
            }
            if (background != null) background.color = bg;
            if (letterBadge != null) letterBadge.color = badge;
            if (letterText != null) letterText.color = state is VisualState.Correct or VisualState.Wrong ? bg : new Color(0.05f, 0.07f, 0.18f);
            if (canvasGroup != null) canvasGroup.alpha = alpha;

            if (state is VisualState.Correct or VisualState.Wrong) Punch();
        }

        /// <summary>Staggered pop-in when a new question appears.</summary>
        public void PopIn(float delay)
        {
            if (_popRoutine != null) StopCoroutine(_popRoutine);
            _popRoutine = StartCoroutine(PopRoutine(delay));
        }

        private void Punch()
        {
            if (_popRoutine != null) StopCoroutine(_popRoutine);
            _popRoutine = StartCoroutine(PunchRoutine());
        }

        private IEnumerator PopRoutine(float delay)
        {
            var rt = (RectTransform)transform;
            rt.localScale = Vector3.one * 0.85f;
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            yield return new WaitForSecondsRealtime(delay);

            float t = 0f;
            const float duration = 0.22f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                rt.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1f, EaseOutBack(k));
                if (canvasGroup != null) canvasGroup.alpha = k;
                yield return null;
            }
            rt.localScale = Vector3.one;
            if (canvasGroup != null) canvasGroup.alpha = 1f;
        }

        private IEnumerator PunchRoutine()
        {
            var rt = (RectTransform)transform;
            float t = 0f;
            const float duration = 0.25f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                rt.localScale = Vector3.one * (1f + 0.06f * Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI));
                yield return null;
            }
            rt.localScale = Vector3.one;
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }
    }
}
