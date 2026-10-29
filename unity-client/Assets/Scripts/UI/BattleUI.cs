using System;
using System.Collections;
using KnowledgeWarrior.Combat;
using KnowledgeWarrior.Data;
using KnowledgeWarrior.Progress;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace KnowledgeWarrior.UI
{
    /// <summary>
    /// All battle UI: HUD (HP/XP and Subject Tier Badge), multi-mode question panel,
    /// feedback/explanation, banners, result card, and floating damage numbers.
    /// </summary>
    public class BattleUI : MonoBehaviour
    {
        private static readonly string[] Letters = { "A", "B", "C", "D" };

        [Header("General")]
        [SerializeField] private Camera worldCamera;
        [SerializeField] private RectTransform popupLayer;

        [Header("Player HUD")]
        [SerializeField] private TMP_Text playerNameText;
        [SerializeField] private TMP_Text playerLevelText;
        [SerializeField] private TMP_Text playerHpText;
        [SerializeField] private RectTransform playerHpFill;
        [SerializeField] private RectTransform playerHpGhost;
        [SerializeField] private RectTransform playerXpFill;
        [SerializeField] private TMP_Text playerXpText;

        [Header("Enemy HUD")]
        [SerializeField] private TMP_Text enemyNameText;
        [SerializeField] private TMP_Text enemyHpText;
        [SerializeField] private RectTransform enemyHpFill;
        [SerializeField] private RectTransform enemyHpGhost;
        [SerializeField] private GameObject bossTag;

        [Header("Top Bar")]
        [SerializeField] private TMP_Text stageText;
        [SerializeField] private TMP_Text streakText;

        [Header("Question Panel")]
        [SerializeField] private CanvasGroup questionPanel;
        [SerializeField] private Image turnBadge;
        [SerializeField] private TMP_Text turnText;
        [SerializeField] private TMP_Text turnHintText;
        [SerializeField] private TMP_Text topicText;
        [SerializeField] private TMP_Text questionText;
        [SerializeField] private AnswerButton[] answerButtons = new AnswerButton[4];

        [Header("Feedback")]
        [SerializeField] private CanvasGroup feedbackGroup;
        [SerializeField] private TMP_Text feedbackTitle;
        [SerializeField] private TMP_Text feedbackBody;
        [SerializeField] private Button continueButton;

        [Header("Banner")]
        [SerializeField] private CanvasGroup bannerGroup;
        [SerializeField] private TMP_Text bannerTitle;
        [SerializeField] private TMP_Text bannerSubtitle;

        [Header("Result")]
        [SerializeField] private CanvasGroup resultGroup;
        [SerializeField] private RectTransform resultCard;
        [SerializeField] private Image resultAccent;
        [SerializeField] private TMP_Text resultTitle;
        [SerializeField] private TMP_Text resultBody;
        [SerializeField] private TMP_Text resultButtonLabel;
        [SerializeField] private Button resultButton;

        [Header("Colors")]
        [SerializeField] private Color attackColor = new(1f, 0.55f, 0.30f);
        [SerializeField] private Color defendColor = new(0.30f, 0.72f, 1f);
        [SerializeField] private Color correctColor = new(0.30f, 0.95f, 0.70f);
        [SerializeField] private Color wrongColor = new(1f, 0.42f, 0.52f);

        private Action<int> _onAnswer;
        private Action _onContinue;
        private Action _onResult;
        private bool _answersEnabled;

        private float _playerHpTarget = 1f, _playerGhost = 1f, _playerHpShown = 1f;
        private float _enemyHpTarget = 1f, _enemyGhost = 1f, _enemyHpShown = 1f;
        private float _ghostDelay;
        private Coroutine _feedbackRoutine, _resultRoutine, _panelRoutine;

        public void Initialize(Action<int> onAnswer, Action onContinue, Action onResult)
        {
            _onAnswer = onAnswer;
            _onContinue = onContinue;
            _onResult = onResult;

            for (int i = 0; i < answerButtons.Length; i++)
            {
                if (answerButtons[i] != null) answerButtons[i].Bind(i, SelectAnswer);
            }
            continueButton.onClick.AddListener(() => _onContinue?.Invoke());
            resultButton.onClick.AddListener(() => _onResult?.Invoke());

            SetGroup(feedbackGroup, false);
            SetGroup(bannerGroup, false);
            SetGroup(resultGroup, false);
            SetGroup(questionPanel, false);
            if (streakText != null) streakText.text = string.Empty;
        }

        public void SetPlayerInfo(string playerName, int level, int xp, int xpToNext, string subject = null)
        {
            var tier = SubjectTierSystem.GetTierForLevel(level);
            string badgeName = SubjectTierSystem.GetBadgeName(tier);
            Color badgeColor = SubjectTierSystem.GetBadgeColor(tier);
            string hex = ColorUtility.ToHtmlStringRGB(badgeColor);

            playerNameText.text = string.IsNullOrWhiteSpace(subject)
                ? playerName
                : $"{playerName} <size=75%><color=#{hex}>[{badgeName.ToUpperInvariant()}]</color></size>";

            playerLevelText.text = $"LV {level} • {badgeName}";
            playerLevelText.color = badgeColor;

            float ratio = xpToNext > 0 ? Mathf.Clamp01((float)xp / xpToNext) : 1f;
            SetFill(playerXpFill, ratio);
            if (playerXpText != null)
            {
                int pts = !string.IsNullOrWhiteSpace(subject) ? ProgressManager.GetSubjectPoints(subject) : xp;
                playerXpText.text = $"{pts} PTS  ({xp}/{xpToNext} XP)";
            }
        }

        public void SetPlayerHp(int current, int max)
        {
            float target = max > 0 ? (float)current / max : 0f;
            if (target > _playerHpTarget) _playerGhost = target;
            _playerHpTarget = target;
            _ghostDelay = 0.35f;
            playerHpText.text = $"{current} / {max}";
        }

        public void SetEnemyHp(int current, int max)
        {
            float target = max > 0 ? (float)current / max : 0f;
            if (target > _enemyHpTarget) _enemyGhost = target;
            _enemyHpTarget = target;
            _ghostDelay = 0.35f;
            enemyHpText.text = $"{current} / {max}";
        }

        public void SetEnemyName(string enemyName, bool isBoss)
        {
            enemyNameText.text = enemyName;
            if (bossTag != null) bossTag.SetActive(isBoss);
            _enemyHpTarget = _enemyGhost = _enemyHpShown = 1f;
        }

        public void SetStage(string text) => stageText.text = text;

        public void SetStreak(int streak)
        {
            if (streakText == null) return;
            streakText.text = streak >= 2 ? $"STREAK x{streak}" : string.Empty;
        }

        public void SetTurn(TurnType turn)
        {
            bool attack = turn == TurnType.Attack;
            turnText.text = attack ? "ATTACK" : "DEFEND";
            turnBadge.color = attack ? attackColor : defendColor;
            turnHintText.text = attack ? "Answer correctly to strike!" : "Answer correctly to block!";
        }

        public void ShowQuestion(QuestionData q)
        {
            string modeLabel = q.GetQuestionModeLabel();
            string tierLabel = q.GetAlignedTierLabel();
            topicText.text = $"[{modeLabel}]  {q.subject}  ·  {q.topic}  ·  {tierLabel}";
            questionText.text = q.question;

            for (int i = 0; i < answerButtons.Length; i++)
            {
                var b = answerButtons[i];
                if (b == null) continue;
                string badgeLetter = GetChoicePrefix(q.questionType, i);
                b.SetContent(badgeLetter, q.choices[i]);
                b.SetInteractable(true);
                b.PopIn(0.05f + i * 0.06f);
            }

            HideFeedback();
            _answersEnabled = true;
            ShowQuestionPanel();
        }

        private static string GetChoicePrefix(string questionType, int index)
        {
            string mode = (questionType ?? string.Empty).Trim().ToLowerInvariant();
            if (mode == "true_false")
            {
                return index == 0 ? "T/F" : Letters[index];
            }
            if (mode == "ordering")
            {
                return $"#{index + 1}";
            }
            if (mode == "spot_bug")
            {
                return $"L{index + 1}";
            }
            return Letters[index];
        }

        public void RevealAnswer(int selected, int correct)
        {
            _answersEnabled = false;
            for (int i = 0; i < answerButtons.Length; i++)
            {
                var b = answerButtons[i];
                if (b == null) continue;
                b.SetInteractable(false);
                if (i == correct) b.SetState(AnswerButton.VisualState.Correct);
                else if (i == selected) b.SetState(AnswerButton.VisualState.Wrong);
                else b.SetState(AnswerButton.VisualState.Dimmed);
            }
        }

        public void ShowFeedback(bool correct, string headline, string explanation)
        {
            feedbackTitle.text = headline;
            feedbackTitle.color = correct ? correctColor : wrongColor;
            feedbackBody.text = string.IsNullOrWhiteSpace(explanation) ? "" : explanation;
            if (_feedbackRoutine != null) StopCoroutine(_feedbackRoutine);
            _feedbackRoutine = StartCoroutine(FadeGroup(feedbackGroup, true, 0.2f));
        }

        public void HideQuestionPanel()
        {
            _answersEnabled = false;
            HideFeedback();
            if (_panelRoutine != null) StopCoroutine(_panelRoutine);
            _panelRoutine = StartCoroutine(FadeGroup(questionPanel, false, 0.2f));
        }

        private void ShowQuestionPanel()
        {
            if (questionPanel.alpha >= 0.99f && questionPanel.interactable) return;
            if (_panelRoutine != null) StopCoroutine(_panelRoutine);
            _panelRoutine = StartCoroutine(FadeGroup(questionPanel, true, 0.25f));
        }

        private void HideFeedback()
        {
            if (_feedbackRoutine != null) StopCoroutine(_feedbackRoutine);
            SetGroup(feedbackGroup, false);
        }

        private void SelectAnswer(int index)
        {
            if (!_answersEnabled) return;
            _answersEnabled = false;
            _onAnswer?.Invoke(index);
        }

        public IEnumerator ShowBanner(string title, string subtitle, Color color)
        {
            bannerTitle.text = title;
            bannerTitle.color = color;
            bannerSubtitle.text = subtitle;

            var rt = (RectTransform)bannerGroup.transform;
            float t = 0f;
            bannerGroup.alpha = 0f;
            bannerGroup.gameObject.SetActive(true);
            while (t < 0.3f)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / 0.3f);
                bannerGroup.alpha = k;
                rt.localScale = new Vector3(1f, Mathf.Lerp(0.2f, 1f, 1f - (1f - k) * (1f - k)), 1f);
                yield return null;
            }
            rt.localScale = Vector3.one;
            yield return new WaitForSecondsRealtime(1.1f);
            yield return FadeGroup(bannerGroup, false, 0.3f);
        }

        public void ShowResult(string title, string body, string buttonLabel, Color accent)
        {
            resultTitle.text = title;
            resultTitle.color = accent;
            resultBody.text = body;
            resultButtonLabel.text = buttonLabel;
            if (resultAccent != null) resultAccent.color = accent;
            if (_resultRoutine != null) StopCoroutine(_resultRoutine);
            _resultRoutine = StartCoroutine(ResultInRoutine());
        }

        public void HideResult()
        {
            if (_resultRoutine != null) StopCoroutine(_resultRoutine);
            SetGroup(resultGroup, false);
        }

        private IEnumerator ResultInRoutine()
        {
            SetGroup(resultGroup, true);
            resultGroup.alpha = 0f;
            float t = 0f;
            const float duration = 0.35f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                resultGroup.alpha = k;
                resultCard.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1f, EaseOutBack(k));
                yield return null;
            }
            resultGroup.alpha = 1f;
            resultCard.localScale = Vector3.one;
        }

        public void SpawnPopup(Vector3 worldPosition, string message, Color color, float scale = 1f)
        {
            if (popupLayer == null) return;
            var cam = worldCamera != null ? worldCamera : Camera.main;
            if (cam == null) return;

            Vector2 screen = cam.WorldToScreenPoint(worldPosition);
            var canvas = popupLayer.GetComponentInParent<Canvas>();
            Camera uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(popupLayer, screen, uiCam, out var local))
                DamagePopup.Create(popupLayer, local, message, color, scale);
        }

        private void Update()
        {
            AnimateBars();
            HandleKeyboardShortcuts();
        }

        private void AnimateBars()
        {
            float dt = Time.unscaledDeltaTime;
            float fast = 1f - Mathf.Exp(-14f * dt);
            _playerHpShown = Mathf.Lerp(_playerHpShown, _playerHpTarget, fast);
            _enemyHpShown = Mathf.Lerp(_enemyHpShown, _enemyHpTarget, fast);

            if (_ghostDelay > 0f) _ghostDelay -= dt;
            else
            {
                float slow = 1f - Mathf.Exp(-4f * dt);
                _playerGhost = Mathf.Lerp(_playerGhost, _playerHpTarget, slow);
                _enemyGhost = Mathf.Lerp(_enemyGhost, _enemyHpTarget, slow);
            }

            SetFill(playerHpFill, _playerHpShown);
            SetFill(playerHpGhost, Mathf.Max(_playerGhost, _playerHpShown));
            SetFill(enemyHpFill, _enemyHpShown);
            SetFill(enemyHpGhost, Mathf.Max(_enemyGhost, _enemyHpShown));
        }

        /// <summary>1-4 or A-D to select an answer, Space or Enter to continue.</summary>
        private void HandleKeyboardShortcuts()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return;

            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null &&
                es.currentSelectedGameObject.GetComponent<TMP_InputField>() != null)
            {
                return;
            }

            if (_answersEnabled)
            {
                if (kb.digit1Key.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) SelectAnswer(0);
                else if (kb.digit2Key.wasPressedThisFrame || kb.bKey.wasPressedThisFrame) SelectAnswer(1);
                else if (kb.digit3Key.wasPressedThisFrame || kb.cKey.wasPressedThisFrame) SelectAnswer(2);
                else if (kb.digit4Key.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) SelectAnswer(3);
            }

            bool confirm = kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
            if (!confirm) return;
            if (resultGroup.interactable) _onResult?.Invoke();
            else if (feedbackGroup.interactable) _onContinue?.Invoke();
#endif
        }

        private static void SetFill(RectTransform fill, float ratio)
        {
            if (fill == null) return;
            fill.anchorMax = new Vector2(Mathf.Clamp01(ratio), fill.anchorMax.y);
        }

        private static void SetGroup(CanvasGroup group, bool visible)
        {
            if (group == null) return;
            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        private static IEnumerator FadeGroup(CanvasGroup group, bool visible, float duration)
        {
            if (group == null) yield break;
            group.interactable = visible;
            group.blocksRaycasts = visible;
            float from = group.alpha, to = visible ? 1f : 0f, t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
                yield return null;
            }
            group.alpha = to;
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }
    }
}
