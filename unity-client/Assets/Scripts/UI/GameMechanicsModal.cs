using System;
using KnowledgeWarrior.Audio;
using KnowledgeWarrior.Progress;
using KnowledgeWarrior.Visuals;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>
    /// Pre-battle Game Mechanics briefing overlay displayed before the player begins
    /// or resumes a subject expedition in every curriculum biome.
    /// </summary>
    public class GameMechanicsModal : MonoBehaviour
    {
        private static readonly Color BackdropColor = new(0.01f, 0.02f, 0.08f, 0.94f);
        private static readonly Color PanelColor = new(0.05f, 0.07f, 0.18f, 0.98f);
        private static readonly Color CardColor = new(0.08f, 0.11f, 0.26f, 0.96f);
        private static readonly Color Cyan = new(0.30f, 0.88f, 1f, 1f);
        private static readonly Color Gold = new(1f, 0.82f, 0.30f, 1f);
        private static readonly Color Green = new(0.24f, 0.90f, 0.63f, 1f);
        private static readonly Color Orange = new(1f, 0.55f, 0.28f, 1f);
        private static readonly Color Violet = new(0.61f, 0.42f, 1f, 1f);
        private static readonly Color TextMain = new(0.93f, 0.95f, 1f, 1f);
        private static readonly Color TextSoft = new(0.76f, 0.82f, 0.96f, 1f);
        private static readonly Color DarkText = new(0.05f, 0.07f, 0.19f, 1f);

        private CanvasGroup _modalGroup;
        private Image _windowBorder;
        private Image _topBar;
        private RectTransform _windowFill;
        private TMP_Text _subjectBannerText;
        private TMP_Text _tierInfoText;
        private TMP_Text _coinRuleText;
        private Button _startBattleBtn;
        private TMP_Text _startBattleLabel;
        private Button _backBtn;

        private Action _onConfirmStart;
        private Action _onBackToLobby;

        public static GameMechanicsModal EnsureOnCanvas(Canvas canvas)
        {
            var existing = canvas.GetComponentInChildren<GameMechanicsModal>(true);
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject("GameMechanicsModal", typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(canvas.transform, false);
            var modal = go.AddComponent<GameMechanicsModal>();
            modal.BuildRuntimeUI();
            return modal;
        }

        public void ShowForSubject(
            string biomeTitle,
            string subject,
            bool isResumingCheckpoint,
            Action onConfirmStart,
            Action onBackToLobby = null)
        {
            if (_modalGroup == null) BuildRuntimeUI();

            _onConfirmStart = onConfirmStart;
            _onBackToLobby = onBackToLobby;

            string safeSubject = string.IsNullOrWhiteSpace(subject) ? "Programming" : subject;
            string safeBiome = string.IsNullOrWhiteSpace(biomeTitle) ? "Citadel of Code" : biomeTitle;
            int subjLevel = ProgressManager.GetSubjectLevel(safeSubject);
            var tier = SubjectTierSystem.GetTierForLevel(subjLevel);
            var theme = ThemeManager.BuildThemeSpec(safeSubject, tier);
            int reviveCost = SubjectTierSystem.GetReviveCoinCostForLevel(subjLevel);

            _windowBorder.color = theme.primaryAccent;
            _topBar.color = theme.primaryAccent;
            FxPanel.Of(_windowFill)?.SetAccent(theme.primaryAccent);

            _subjectBannerText.text = $"{safeSubject.ToUpperInvariant()} — {safeBiome.ToUpperInvariant()}";
            _subjectBannerText.color = theme.primaryAccent;

            bool isOffline = OfflinePracticeManager.IsOfflinePracticeMode;
            _tierInfoText.text = isOffline
                ? $"OFFLINE PRACTICE BRIEFING  •  Subject Level: LV {subjLevel}/100 ({SubjectTierSystem.GetBadgeName(tier)} Tier)"
                : $"EXPEDITION BRIEFING  •  Subject Level: LV {subjLevel}/100 ({SubjectTierSystem.GetBadgeName(tier)} Badge • Difficulty {(int)tier})";

            _coinRuleText.text = isOffline
                ? "Offline Practice awards 0 XP and 0 Coins while keeping your online progress untouched. Review every explanation after each turn."
                : $"• Monster Victory (Stages 1 to 3): +{SubjectTierSystem.StageWinCoinReward} Coins and Subject XP\n" +
                  $"• Boss Victory (Stage 4): +{SubjectTierSystem.BossWinCoinReward} Coins and Boss XP Bonus\n" +
                  $"• Defeat Revive Cost: {reviveCost} Coins ({SubjectTierSystem.GetBadgeName(tier)} Tier) to retry your stage";

            if (_startBattleLabel != null)
            {
                _startBattleLabel.text = isResumingCheckpoint ? "RESUME BATTLE" : "START BATTLE";
            }

            if (_backBtn != null)
            {
                _backBtn.gameObject.SetActive(onBackToLobby != null);
            }

            transform.SetAsLastSibling();
            ModalFx.Present(_modalGroup, true);
        }

        public void Hide()
        {
            if (_modalGroup == null) return;
            ModalFx.Present(_modalGroup, false);
        }

        private void BuildRuntimeUI()
        {
            var rt = (RectTransform)transform;
            Stretch(rt);

            var backdrop = CreateImage("Backdrop", rt, BackdropColor);
            Stretch(backdrop.rectTransform);
            _modalGroup = backdrop.gameObject.AddComponent<CanvasGroup>();

            _windowBorder = CreateImage("MechanicsWindowBorder", backdrop.rectTransform, Cyan);
            Place(_windowBorder.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1020f, 1780f));

            _windowFill = CreateImage("MechanicsWindowFill", _windowBorder.rectTransform, PanelColor).rectTransform;
            Stretch(_windowFill, 2f, 2f, 2f, 2f);

            _topBar = CreateImage("TopBannerBar", _windowFill, Cyan);
            Place(_topBar.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, 8f));

            var logoFrame = CreateImage("BrandLogoFrame", _windowFill, new Color(Gold.r, Gold.g, Gold.b, 0.65f));
            Place(logoFrame.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(30f, -26f), new Vector2(88f, 88f));
            var logoImg = CreateImage("BrandLogo", logoFrame.rectTransform, Color.white);
            Stretch(logoImg.rectTransform, 3f, 3f, 3f, 3f);
            logoImg.sprite = UISprites.BrandLogo;
            logoImg.preserveAspect = true;

            CreateText("HeaderTitle", _windowFill, "GAME MECHANICS", 34f, Gold, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    new Vector2(134f, -24f), new Vector2(-164f, 44f)));

            _subjectBannerText = CreateText("SubjectBanner", _windowFill, "PROGRAMMING — CITADEL OF CODE", 22f, Cyan, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    new Vector2(134f, -72f), new Vector2(-164f, 34f)));

            _tierInfoText = CreateText("TierInfoText", _windowFill, "", 19f, TextSoft, TextAlignmentOptions.MidlineLeft, false,
                r => Place(r, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -124f), new Vector2(-60f, 34f)));

            CreateRuleCard(
                "CombatTurnsCard",
                Cyan,
                -176f,
                292f,
                "1. ATTACK AND DEFEND TURNS",
                "• ATTACK TURN: Answer the curriculum question accurately to strike the monster.\n" +
                "• STREAK COMBOS AND CRITS: Consecutive correct answers build a damage multiplier and trigger Critical Hits.\n" +
                "• DEFEND TURN: Answer accurately to raise your Arcane Shield and block all incoming monster damage.");

            CreateRuleCard(
                "GauntletStagesCard",
                Orange,
                -490f,
                274f,
                "2. 4-STAGE MONSTER AND BOSS EXPEDITION",
                "• Battle through 3 Subject Monsters (Stages 1 to 3) followed by 1 Biome Boss (Stage 4).\n" +
                "• Your Hero restores 35% Max HP between victorious stages.\n" +
                "• Stage progress saves automatically so you can pause or switch subjects anytime.");

            _coinRuleText = CreateRuleCard(
                "CoinsAndReviveCard",
                Gold,
                -786f,
                274f,
                "3. COIN REWARDS AND REVIVE SYSTEM",
                "• Monster Victory (Stages 1 to 3): +15 Coins and Subject XP.\n" +
                "• Boss Victory (Stage 4): +25 Coins and Boss XP Bonus.\n" +
                "• Spend Coins to revive on the same stage after defeat, or restart from Stage 1.");

            CreateRuleCard(
                "ProgressionCard",
                Violet,
                -1082f,
                292f,
                "4. SEQUENTIAL LEVELING AND QUESTION MODES",
                "• Every subject starts at Level 1 and advances sequentially up to Level 100 across Beginner, Intermediate, Advanced, and Expert tiers.\n" +
                "• Face Multiple Choice, True or False, Fill in the Blank, Sequence Ordering, and Spot the Bug challenges that never repeat once seen.");

            _backBtn = CreateButton("BackToLobbyBtn", _windowFill, new Color(0.14f, 0.19f, 0.38f, 1f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-240f, 44f), new Vector2(360f, 88f), "BACK TO LOBBY", 22f, TextMain, out _);
            _backBtn.onClick.AddListener(() =>
            {
                BattleAudioManager.Instance?.PlayButtonClick();
                Hide();
                _onBackToLobby?.Invoke();
            });

            _startBattleBtn = CreateButton("StartBattleBtn", _windowFill, Green,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(180f, 44f), new Vector2(440f, 88f), "START BATTLE", 26f, DarkText, out _startBattleLabel);
            _startBattleBtn.onClick.AddListener(() =>
            {
                BattleAudioManager.Instance?.PlayButtonClick();
                Hide();
                _onConfirmStart?.Invoke();
            });

            ModalFx.Dress(_modalGroup, UIPalette.Cyan);
            ModalFx.DecorateByName((RectTransform)_modalGroup.transform, UIPalette.Cyan, false,
                "CombatTurnsCardBorder", "GauntletStagesCardBorder", "CoinsAndReviveCardBorder", "ProgressionCardBorder");
            Hide();
        }

        private TMP_Text CreateRuleCard(
            string baseName,
            Color accent,
            float topY,
            float height,
            string title,
            string body)
        {
            var border = CreateImage(baseName + "Border", _windowFill, new Color(accent.r, accent.g, accent.b, 0.65f));
            Place(border.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, topY), new Vector2(960f, height));

            var fill = CreateImage("Fill", border.rectTransform, CardColor).rectTransform;
            Stretch(fill, 2f, 2f, 2f, 2f);

            var bar = CreateImage("AccentBar", fill, accent);
            Place(bar.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, 6f));

            CreateText("Title", fill, title, 23f, accent, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -20f), new Vector2(-44f, 34f)));

            var bodyTxt = CreateText("Body", fill, body, 20f, TextSoft, TextAlignmentOptions.TopLeft, false,
                r => Place(r, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -64f), new Vector2(-44f, height - 80f)));
            bodyTxt.lineSpacing = 6f;
            bodyTxt.enableAutoSizing = true;
            bodyTxt.fontSizeMin = 15f;
            bodyTxt.fontSizeMax = 20f;
            return bodyTxt;
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
            string label, float fontSize, Color textColor, out TMP_Text labelText)
        {
            var img = CreateImage(name, parent, bgColor);
            Place(img.rectTransform, anchorMin, anchorMax, pivot, pos, size);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            img.gameObject.AddComponent<ButtonFX>();
            labelText = CreateText("Label", img.rectTransform, label, fontSize, textColor, TextAlignmentOptions.Center, true, Stretch);
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

        private static void Stretch(RectTransform rt) => Stretch(rt, 0f, 0f, 0f, 0f);

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
