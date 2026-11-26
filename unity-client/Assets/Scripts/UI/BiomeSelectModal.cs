using System;
using System.Collections.Generic;
using System.Linq;
using KnowledgeWarrior.Audio;
using KnowledgeWarrior.Data;
using KnowledgeWarrior.Progress;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>
    /// Subject Biome Selector overlay.
    /// Lists all subjects loaded in <see cref="QuestionManager"/>, displays the player's
    /// independent Level (1-100) and Tier Badge (Beginner, Intermediate, Advanced, Expert)
    /// for each subject, and opens the Subject Lobby to battle subject monsters and bosses.
    /// </summary>
    public class BiomeSelectModal : MonoBehaviour
    {
        [Serializable]
        public struct BiomeOption
        {
            public string biomeTitle;
            public string subjectFilter;
            public string topicsSummary;
            public int questionCount;
            public Color accentColor;
        }

        private const int MaxBiomeCards = 8;

        private static readonly Color PanelColor = new(0.04f, 0.06f, 0.16f, 0.96f);
        private static readonly Color CardColor = new(0.08f, 0.11f, 0.26f, 0.96f);
        private static readonly Color BorderColor = new(0.30f, 0.88f, 1f, 0.38f);
        private static readonly Color Cyan = new(0.30f, 0.88f, 1f, 1f);
        private static readonly Color Violet = new(0.61f, 0.42f, 1f, 1f);
        private static readonly Color Gold = new(1f, 0.82f, 0.30f, 1f);
        private static readonly Color Green = new(0.24f, 0.90f, 0.63f, 1f);
        private static readonly Color Orange = new(1f, 0.55f, 0.28f, 1f);
        private static readonly Color Pink = new(1f, 0.42f, 0.68f, 1f);
        private static readonly Color TextMain = new(0.93f, 0.95f, 1f, 1f);
        private static readonly Color TextSoft = new(0.75f, 0.80f, 0.96f, 1f);
        private static readonly Color TextMuted = new(0.55f, 0.61f, 0.82f, 1f);
        private static readonly Color DarkText = new(0.05f, 0.07f, 0.19f, 1f);

        private QuestionManager _questionManager;
        private Action<string, string> _onBiomeSelected;
        private CanvasGroup _modalGroup;
        private TMP_Text _subtitleText;
        private Button _closeButton;
        private string _activeSubject = "Programming";

        private readonly List<CardWidgets> _cards = new();

        private class CardWidgets
        {
            public GameObject root;
            public Image border;
            public FxPanel decor;
            public Image accentBar;
            public Button button;
            public TMP_Text biomeTitleText;
            public TMP_Text subjectBadgeText;
            public TMP_Text levelBadgeText;
            public TMP_Text topicsText;
            public TMP_Text countText;
            public Image launchBtnBg;
            public TMP_Text launchBtnText;
        }

        public static BiomeSelectModal EnsureOnCanvas(
            Canvas canvas,
            QuestionManager questionManager,
            Action<string, string> onBiomeSelected)
        {
            var existing = canvas.GetComponentInChildren<BiomeSelectModal>(true);
            if (existing != null)
            {
                existing.Configure(questionManager, onBiomeSelected);
                return existing;
            }

            var go = new GameObject("BiomeSelectModal", typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(canvas.transform, false);
            var modal = go.AddComponent<BiomeSelectModal>();
            modal.Configure(questionManager, onBiomeSelected);
            modal.BuildRuntimeUI(canvas.transform);
            return modal;
        }

        public void Configure(QuestionManager questionManager, Action<string, string> onBiomeSelected)
        {
            if (_questionManager != null)
            {
                _questionManager.OnQuestionsLoaded -= RefreshBiomeCards;
            }
            _questionManager = questionManager;
            _onBiomeSelected = onBiomeSelected;
            if (_questionManager != null)
            {
                _questionManager.OnQuestionsLoaded += RefreshBiomeCards;
            }
        }

        private void OnDestroy()
        {
            if (_questionManager != null)
            {
                _questionManager.OnQuestionsLoaded -= RefreshBiomeCards;
            }
        }

        public void Show(bool allowClose = true)
        {
            if (_modalGroup == null) BuildRuntimeUI(transform.parent);
            if (_closeButton != null) _closeButton.gameObject.SetActive(allowClose);

            RefreshBiomeCards();
            transform.SetAsLastSibling();

            ModalFx.Present(_modalGroup, true);
        }

        public void Hide()
        {
            if (_modalGroup == null) return;
            ModalFx.Present(_modalGroup, false);
        }

        private void SelectBiome(BiomeOption option)
        {
            BattleAudioManager.Instance?.PlayButtonClick();
            _activeSubject = string.IsNullOrWhiteSpace(option.subjectFilter) ? "Programming" : option.subjectFilter;
            Hide();
            _onBiomeSelected?.Invoke(option.biomeTitle, _activeSubject);
        }

        private void RefreshBiomeCards()
        {
            if (_cards.Count == 0 || _questionManager == null) return;

            var options = BuildBiomeOptions();
            var p = ProgressManager.Current;
            if (_subtitleText != null)
            {
                _subtitleText.text =
                    $"Warrior: <color=#4DE1FF>{p.playerName}</color>   •   " +
                    "Every subject starts at Level 1 and progresses one level at a time up to Level 100";
            }

            for (int i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i];
                if (i >= options.Count)
                {
                    c.root.SetActive(false);
                    continue;
                }

                var opt = options[i];
                string subjName = string.IsNullOrWhiteSpace(opt.subjectFilter) ? "Programming" : opt.subjectFilter;
                var sp = ProgressManager.GetSubjectProgress(subjName);
                var tier = SubjectTierSystem.GetTierForLevel(sp.level);
                int nextXp = SubjectTierSystem.XpToNextSubjectLevel(sp.level);
                Color badgeColor = SubjectTierSystem.GetBadgeColor(tier);
                bool isSelected = string.Equals(opt.subjectFilter ?? string.Empty, _activeSubject ?? string.Empty, StringComparison.OrdinalIgnoreCase);

                c.root.SetActive(true);
                c.border.color = isSelected ? opt.accentColor : new Color(opt.accentColor.r, opt.accentColor.g, opt.accentColor.b, 0.35f);
                c.accentBar.color = opt.accentColor;
                c.decor?.SetAccent(opt.accentColor);
                c.decor?.SetRingAlpha(isSelected ? 0.95f : 0.4f);
                c.biomeTitleText.text = opt.biomeTitle;
                c.subjectBadgeText.text = string.IsNullOrEmpty(opt.subjectFilter) ? "ALL SUBJECTS GAUNTLET" : opt.subjectFilter.ToUpperInvariant();
                c.subjectBadgeText.color = opt.accentColor;
                c.levelBadgeText.text = $"LV {sp.level}/100  •  {SubjectTierSystem.GetBadgeName(tier).ToUpperInvariant()} BADGE";
                c.levelBadgeText.color = badgeColor;
                int unseenCount = _questionManager.CountUnseenForSubject(subjName);
                c.topicsText.text = opt.topicsSummary;
                c.countText.text = sp.level >= SubjectTierSystem.MaxLevel
                    ? $"{unseenCount} Unseen Questions  •  {sp.points} PTS (MAX)"
                    : $"{unseenCount} Unseen Questions  •  {sp.xp}/{nextXp} XP";
                c.launchBtnBg.color = isSelected ? Green : opt.accentColor;
                c.launchBtnText.text = "OPEN SUBJECT LOBBY";

                var captured = opt;
                c.button.onClick.RemoveAllListeners();
                c.button.onClick.AddListener(() => SelectBiome(captured));
            }
        }

        private List<BiomeOption> BuildBiomeOptions()
        {
            var allQuestions = _questionManager != null ? _questionManager.All : Array.Empty<QuestionData>();
            var list = new List<BiomeOption>();

            if (_questionManager == null) return list;

            foreach (string subject in _questionManager.Subjects)
            {
                if (list.Count >= MaxBiomeCards) break;

                var matching = allQuestions
                    .Where(q => string.Equals(q.subject, subject, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var topics = matching
                    .Select(q => q.topic)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct()
                    .Take(4)
                    .ToList();

                var (title, color) = GetBiomeMetaForSubject(subject, list.Count);
                list.Add(new BiomeOption
                {
                    biomeTitle = title,
                    subjectFilter = subject,
                    topicsSummary = topics.Count > 0 ? "Topics: " + string.Join(", ", topics) : $"Master {subject} challenges.",
                    questionCount = matching.Count,
                    accentColor = color,
                });
            }

            return list;
        }

        private static (string biomeTitle, Color accent) GetBiomeMetaForSubject(string subject, int index)
        {
            string key = (subject ?? string.Empty).Trim().ToLowerInvariant();
            return key switch
            {
                "programming" => ("Citadel of Code", Gold),
                "web development" => ("Hypertext Harbor", Orange),
                "networking" => ("Packet Peaks", Cyan),
                "databases" => ("Relational Ruins", Violet),
                "cybersecurity" => ("Cipher Sanctum", Pink),
                "computer systems" => ("Silicon Foundry", Green),
                _ => ($"{subject} Realm", (index % 3) switch { 0 => Cyan, 1 => Violet, _ => Gold })
            };
        }

        private void BuildRuntimeUI(Transform canvasRoot)
        {
            var rt = (RectTransform)transform;
            Stretch(rt);

            Transform safeArea = canvasRoot.Find("SafeArea") ?? canvasRoot;
            var hudBtn = CreateButton("BiomeSelectHudButton", safeArea, CardColor,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, -16f), new Vector2(228f, 54f), "SUBJECT LOBBIES", 18, Gold);
            hudBtn.onClick.AddListener(() => Show(allowClose: true));

            var backdrop = CreateImage("Backdrop", rt, new Color(0.01f, 0.02f, 0.07f, 0.96f));
            Stretch(backdrop.rectTransform);
            _modalGroup = backdrop.gameObject.AddComponent<CanvasGroup>();

            var windowBorder = CreateImage("WindowBorder", backdrop.rectTransform, BorderColor);
            Place(windowBorder.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1020, 1820));

            var window = CreateImage("WindowFill", windowBorder.rectTransform, PanelColor);
            Stretch(window.rectTransform, 2, 2, 2, 2);
            var w = window.rectTransform;

            CreateText("HeaderTitle", w, "SELECT A SUBJECT BIOME", 34, Gold, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -20), new Vector2(590, 48)));

            _subtitleText = CreateText("HeaderSubtitle", w, "", 19, TextSoft, TextAlignmentOptions.TopLeft, false,
                r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -72), new Vector2(956, 56)));

            var settingsBtn = CreateButton("SettingsButton", w, CardColor,
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-200, -18), new Vector2(160, 52), "SETTINGS", 20, Violet);
            settingsBtn.onClick.AddListener(() => SettingsModal.Instance?.Show());

            _closeButton = CreateButton("CloseButton", w, new Color(1f, 0.36f, 0.48f, 1f),
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-28, -18), new Vector2(160, 52), "RESUME", 22, TextMain);
            _closeButton.onClick.AddListener(Hide);

            _cards.Clear();
            const float startX = 32f;
            const float startY = -148f;
            const float cardW = 468f;
            const float cardH = 380f;
            const float gapX = 20f;
            const float gapY = 20f;

            for (int i = 0; i < MaxBiomeCards; i++)
            {
                int col = i % 2;
                int row = i / 2;
                float x = startX + col * (cardW + gapX);
                float y = startY - row * (cardH + gapY);

                var border = CreateImage($"BiomeCard_{i}", w, BorderColor);
                Place(border.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, y), new Vector2(cardW, cardH));

                var cardFill = CreateImage("Fill", border.rectTransform, CardColor);
                Stretch(cardFill.rectTransform, 2, 2, 2, 2);
                var c = cardFill.rectTransform;
                var decor = UIDecorator.DecoratePanel(c, Cyan, 24f, 0.10f);

                var btn = border.gameObject.AddComponent<Button>();
                btn.targetGraphic = cardFill;
                border.gameObject.AddComponent<ButtonFX>();

                var accentBar = CreateImage("AccentBar", c, Cyan);
                Place(accentBar.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 8));

                var subjectBadge = CreateText("SubjectBadge", c, "SUBJECT", 18, Cyan, TextAlignmentOptions.MidlineLeft, true,
                    r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(-40, 26)));

                var biomeTitle = CreateText("BiomeTitle", c, "Biome Name", 28, TextMain, TextAlignmentOptions.MidlineLeft, true,
                    r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -52), new Vector2(-40, 38)));

                var levelBadge = CreateText("LevelBadge", c, "LV 1 • BEGINNER BADGE", 18, Green, TextAlignmentOptions.MidlineLeft, true,
                    r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -92), new Vector2(-40, 28)));

                var topics = CreateText("Topics", c, "Topics...", 19, TextSoft, TextAlignmentOptions.TopLeft, false,
                    r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -132), new Vector2(-40, 110)));

                var count = CreateText("Count", c, "Questions", 18, TextMuted, TextAlignmentOptions.MidlineLeft, true,
                    r => Place(r, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 84), new Vector2(-40, 26)));

                var launchBg = CreateImage("LaunchBar", c, Cyan);
                Place(launchBg.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(cardW - 44f, 54f));
                var launchTxt = CreateText("LaunchLabel", launchBg.rectTransform, "OPEN SUBJECT LOBBY", 20, DarkText, TextAlignmentOptions.Center, true, Stretch);

                _cards.Add(new CardWidgets
                {
                    root = border.gameObject,
                    border = border,
                    decor = decor,
                    accentBar = accentBar,
                    button = btn,
                    biomeTitleText = biomeTitle,
                    subjectBadgeText = subjectBadge,
                    levelBadgeText = levelBadge,
                    topicsText = topics,
                    countText = count,
                    launchBtnBg = launchBg,
                    launchBtnText = launchTxt,
                });
            }

            ModalFx.Dress(_modalGroup, UIPalette.Cyan);
            ModalFx.SetStagger(_modalGroup, _cards.Select(card => (RectTransform)card.root.transform));
            Hide();
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private static TMP_Text CreateText(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions align, bool bold, Action<RectTransform> layout)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.raycastTarget = false;
            layout?.Invoke((RectTransform)go.transform);
            return t;
        }

        private static Button CreateButton(string name, Transform parent, Color bgColor,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size,
            string label, float fontSize, Color textColor)
        {
            var img = CreateImage(name, parent, bgColor);
            Place(img.rectTransform, anchorMin, anchorMax, pivot, pos, size);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            img.gameObject.AddComponent<ButtonFX>();
            CreateText("Label", img.rectTransform, label, fontSize, textColor, TextAlignmentOptions.Center, true, Stretch);
            return btn;
        }

        private static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void Stretch(RectTransform rt) => Stretch(rt, 0, 0, 0, 0);

        private static void Stretch(RectTransform rt, float left, float right, float top, float bottom)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }
    }
}
