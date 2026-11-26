using System;
using System.Collections.Generic;
using KnowledgeWarrior.Audio;
using KnowledgeWarrior.Combat;
using KnowledgeWarrior.Progress;
using KnowledgeWarrior.Visuals;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>
    /// Per-Subject Expedition Lobby where the player views their sequential Subject Level (1 to 100),
    /// XP progress toward the next level, Tier Badge (Beginner, Intermediate, Advanced, Expert),
    /// and the 4 subject-themed Monsters and Stage Boss scaled to their current level.
    /// Level skipping or manual level overrides are strictly disallowed.
    /// </summary>
    public class SubjectLobbyModal : MonoBehaviour
    {
        private const int StagePreviewCount = 4;

        private static readonly Color BackdropColor = new(0.01f, 0.02f, 0.08f, 0.92f);
        private static readonly Color PanelColor = new(0.05f, 0.07f, 0.18f, 0.98f);
        private static readonly Color CardColor = new(0.08f, 0.11f, 0.25f, 0.96f);
        private static readonly Color SidebarColor = new(0.04f, 0.06f, 0.14f, 0.98f);
        private static readonly Color TrackColor = new(0.02f, 0.03f, 0.09f, 0.95f);
        private static readonly Color Cyan = new(0.30f, 0.88f, 1f, 1f);
        private static readonly Color Gold = new(1f, 0.82f, 0.30f, 1f);
        private static readonly Color Green = new(0.24f, 0.90f, 0.63f, 1f);
        private static readonly Color Red = new(1f, 0.38f, 0.48f, 1f);
        private static readonly Color BossAccent = new(1f, 0.55f, 0.25f, 1f);
        private static readonly Color TextMain = new(0.93f, 0.95f, 1f, 1f);
        private static readonly Color TextSoft = new(0.75f, 0.80f, 0.96f, 1f);
        private static readonly Color TextMuted = new(0.55f, 0.61f, 0.82f, 1f);
        private static readonly Color DarkText = new(0.05f, 0.07f, 0.19f, 1f);

        private CanvasGroup _modalGroup;
        private Image _windowBorder;
        private Image _topBannerBar;
        private RectTransform _windowFill;
        private TMP_Text _lobbyTitleText;
        private TMP_Text _themeSubtitleText;
        private Image _badgePillBg;
        private TMP_Text _badgePillText;
        private TMP_Text _playerStatsText;
        private TMP_Text _questionAlignmentText;
        private TMP_Text _levelProgressLabelText;
        private RectTransform _levelProgressFill;
        private Image _levelProgressFillImage;
        private TMP_Text _expeditionSummaryText;

        private Image _monsterModeCardBorder;
        private Button _fightMonstersBtn;
        private TMP_Text _rosterHeaderText;
        private TMP_Text _rosterSubheaderText;
        private readonly List<MonsterStageRowWidgets> _stageRows = new();

        private string _currentBiomeTitle = "Citadel of Code";
        private string _currentSubject = "Programming";
        private Action<string, string> _onStartMonsterRun;

        private class MonsterStageRowWidgets
        {
            public GameObject root;
            public Image border;
            public TMP_Text stageTagText;
            public TMP_Text nameText;
            public TMP_Text statsText;
        }

        public static SubjectLobbyModal EnsureOnCanvas(
            Canvas canvas,
            Action<string, string> onStartMonsterRun)
        {
            var existing = canvas.GetComponentInChildren<SubjectLobbyModal>(true);
            if (existing != null)
            {
                existing._onStartMonsterRun = onStartMonsterRun;
                return existing;
            }

            var go = new GameObject("SubjectLobbyModal", typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(canvas.transform, false);
            var modal = go.AddComponent<SubjectLobbyModal>();
            modal._onStartMonsterRun = onStartMonsterRun;
            modal.BuildRuntimeUI();
            return modal;
        }

        public void ShowForSubject(string biomeTitle, string subject)
        {
            if (_modalGroup == null) BuildRuntimeUI();
            _currentBiomeTitle = string.IsNullOrWhiteSpace(biomeTitle) ? "Citadel of Code" : biomeTitle;
            _currentSubject = string.IsNullOrWhiteSpace(subject) ? "Programming" : subject;

            transform.SetAsLastSibling();
            ModalFx.Present(_modalGroup, true);

            RefreshLobbyVisualsAndStats();
        }

        public void Hide()
        {
            if (_modalGroup == null) return;
            ModalFx.Present(_modalGroup, false);
        }

        private void RefreshLobbyVisualsAndStats()
        {
            var p = ProgressManager.Current;
            var sp = ProgressManager.GetSubjectProgress(_currentSubject);
            int lv = sp.level;
            var tier = SubjectTierSystem.GetTierForLevel(lv);
            Color tierColor = SubjectTierSystem.GetBadgeColor(tier);
            var theme = ThemeManager.BuildThemeSpec(_currentSubject, tier);

            if (ThemeManager.Instance != null)
            {
                ThemeManager.Instance.ApplyTheme(_currentSubject, lv);
            }
            if (BattleAudioManager.Instance != null)
            {
                BattleAudioManager.Instance.PlaySubjectThemeMusic(_currentSubject, tier, false);
            }

            _windowBorder.color = theme.primaryAccent;
            _topBannerBar.color = theme.primaryAccent;
            FxPanel.Of(_windowFill)?.SetAccent(theme.primaryAccent);
            _lobbyTitleText.text = $"{_currentSubject.ToUpperInvariant()} LOBBY — {_currentBiomeTitle.ToUpperInvariant()}";
            _lobbyTitleText.color = theme.primaryAccent;
            _themeSubtitleText.text = $"Arena Theme: {theme.tierSubtitle}  •  {SubjectTierSystem.GetLevelRangeLabel(tier)}";

            _badgePillBg.color = tierColor;
            _badgePillText.text = SubjectTierSystem.GetBadgeEmblemText(lv);

            int nextXp = SubjectTierSystem.XpToNextSubjectLevel(lv);
            _playerStatsText.text =
                $"Warrior: <color=#4DE1FF>{p.playerName}</color>   •   " +
                $"Subject Level: <color=#FFD24D>LV {lv}/100</color> ({SubjectTierSystem.GetBadgeName(tier)} Badge)   •   " +
                $"Total Subject Points: <color=#3EE6A0>{sp.points} PTS</color>";

            _questionAlignmentText.text =
                $"Aligned Question Pool: Strictly <color=#FFD24D>{SubjectTierSystem.GetBadgeName(tier)} Tier (Difficulty {(int)tier})</color> " +
                $"live server questions for {_currentSubject} across all 5 combat question modes.";

            if (lv >= SubjectTierSystem.MaxLevel)
            {
                _levelProgressLabelText.text = "MAXIMUM SUBJECT LEVEL REACHED (LV 100 EXPERT TIER)";
                SetFill(_levelProgressFill, 1f);
            }
            else
            {
                float ratio = nextXp > 0 ? Mathf.Clamp01((float)sp.xp / nextXp) : 0f;
                _levelProgressLabelText.text =
                    $"SEQUENTIAL PROGRESS TO LV {lv + 1}:  {sp.xp} / {nextXp} XP   •   Starts at Level 1 and advances one level at a time";
                SetFill(_levelProgressFill, ratio);
            }
            if (_levelProgressFillImage != null)
            {
                _levelProgressFillImage.color = tierColor;
            }

            _monsterModeCardBorder.color = tierColor;
            _expeditionSummaryText.text =
                $"ONLINE EXPEDITION REWARDS: Defeat all 3 {_currentSubject} monsters and the Stage 4 Boss to earn Subject XP. " +
                $"Every subject starts at Level 1 and advances sequentially to Level 100 on the online server without level skipping.";

            RefreshMonsterRosterSidebar(_currentSubject, lv, tier, tierColor);
        }

        private void RefreshMonsterRosterSidebar(string subject, int level, BadgeTier tier, Color tierColor)
        {
            _rosterHeaderText.text = $"STAGE MONSTER ROSTER ({subject.ToUpperInvariant()})";
            _rosterSubheaderText.text =
                $"Scaled to Level {level} ({SubjectTierSystem.GetBadgeName(tier)} Tier) in {_currentBiomeTitle}";

            var previewStages = MonsterCatalog.BuildStagesForSubjectAndLevel(subject, level, null);
            for (int i = 0; i < _stageRows.Count; i++)
            {
                var row = _stageRows[i];
                if (i >= previewStages.Count)
                {
                    row.root.SetActive(false);
                    continue;
                }

                var stage = previewStages[i];
                bool isBoss = stage.isBoss;
                Color rowAccent = isBoss ? BossAccent : tierColor;

                row.root.SetActive(true);
                row.border.color = new Color(rowAccent.r, rowAccent.g, rowAccent.b, isBoss ? 0.85f : 0.55f);
                row.stageTagText.text = isBoss
                    ? $"STAGE {i + 1} • FINAL BIOME BOSS"
                    : $"STAGE {i + 1} • SUBJECT MONSTER";
                row.stageTagText.color = rowAccent;
                row.nameText.text = stage.displayName;
                row.statsText.text =
                    $"HP: {stage.maxHp}   •   ATK: {stage.attack}   •   Victory Bonus: +{stage.xpReward} XP";
            }
        }

        private void BuildRuntimeUI()
        {
            var rt = (RectTransform)transform;
            Stretch(rt);

            var backdrop = CreateImage("Backdrop", rt, BackdropColor);
            Stretch(backdrop.rectTransform);
            _modalGroup = backdrop.gameObject.AddComponent<CanvasGroup>();

            _windowBorder = CreateImage("LobbyWindowBorder", backdrop.rectTransform, Cyan);
            Place(_windowBorder.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1020, 1820));

            var window = CreateImage("LobbyWindowFill", _windowBorder.rectTransform, PanelColor).rectTransform;
            Stretch(window, 2, 2, 2, 2);
            _windowFill = window;

            _topBannerBar = CreateImage("TopBannerBar", window, Cyan);
            Place(_topBannerBar.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 8));

            _lobbyTitleText = CreateText("LobbyTitle", window, "SUBJECT LOBBY", 29, Gold, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -20), new Vector2(610, 44)));
            _lobbyTitleText.enableAutoSizing = true;
            _lobbyTitleText.fontSizeMin = 20;
            _lobbyTitleText.fontSizeMax = 29;

            var settingsBtn = CreateButton("SettingsLobbyBtn", window, CardColor,
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-190, -18), new Vector2(150, 50), "SETTINGS", 20, Cyan, out _);
            settingsBtn.onClick.AddListener(() => SettingsModal.Instance?.Show());

            var closeBtn = CreateButton("CloseLobbyBtn", window, Red,
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-28, -18), new Vector2(150, 50), "CLOSE", 21, TextMain, out _);
            closeBtn.onClick.AddListener(() =>
            {
                BattleAudioManager.Instance?.PlayButtonClick();
                Hide();
            });

            _themeSubtitleText = CreateText("ThemeSubtitle", window, "", 18, TextSoft, TextAlignmentOptions.MidlineLeft, false,
                r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -74), new Vector2(560, 36)));

            _badgePillBg = CreateImage("BadgePill", window, Green);
            Place(_badgePillBg.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-28, -74), new Vector2(380, 42));
            _badgePillText = CreateText("BadgePillText", _badgePillBg.rectTransform, "BEGINNER BADGE", 17, DarkText, TextAlignmentOptions.Center, true, Stretch);

            var statsCard = CreateImage("SubjectStatsCard", window, CardColor).rectTransform;
            Place(statsCard, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -132), new Vector2(964, 214));

            _playerStatsText = CreateText("PlayerStatsText", statsCard, "", 20, TextMain, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(-36, 40)));
            _playerStatsText.enableAutoSizing = true;
            _playerStatsText.fontSizeMin = 16;
            _playerStatsText.fontSizeMax = 20;

            _questionAlignmentText = CreateText("QuestionAlignText", statsCard, "", 17, TextSoft, TextAlignmentOptions.TopLeft, false,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(-36, 54)));

            _levelProgressLabelText = CreateText("LevelProgressLabel", statsCard, "", 16, TextMuted, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 48), new Vector2(-36, 30)));

            var xpTrack = CreateImage("SubjectXpTrack", statsCard, TrackColor).rectTransform;
            Place(xpTrack, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(-36, 22));

            _levelProgressFillImage = CreateImage("SubjectXpFill", xpTrack, Green);
            _levelProgressFill = _levelProgressFillImage.rectTransform;
            Stretch(_levelProgressFill);
            SetFill(_levelProgressFill, 0f);

            var sidebarBorder = CreateImage("MonsterRosterPanelBorder", window, Cyan);
            Place(sidebarBorder.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -364), new Vector2(964, 836));

            var sidebar = CreateImage("MonsterRosterPanelFill", sidebarBorder.rectTransform, SidebarColor).rectTransform;
            Stretch(sidebar, 2, 2, 2, 2);

            _rosterHeaderText = CreateText("RosterHeader", sidebar, "STAGE MONSTER ROSTER", 23, Gold, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(-36, 32)));

            _rosterSubheaderText = CreateText("RosterSubheader", sidebar, "", 18, Cyan, TextAlignmentOptions.MidlineLeft, false,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -50), new Vector2(-36, 26)));

            _stageRows.Clear();
            const float rowStartY = -92f;
            const float rowHeight = 168f;
            const float rowGap = 14f;

            for (int i = 0; i < StagePreviewCount; i++)
            {
                float y = rowStartY - i * (rowHeight + rowGap);
                var rowBorder = CreateImage($"MonsterStageRow_{i}", sidebar, Cyan);
                Place(rowBorder.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(924, rowHeight));

                var rowFill = CreateImage("Fill", rowBorder.rectTransform, CardColor).rectTransform;
                Stretch(rowFill, 2, 2, 2, 2);

                var stageTagTxt = CreateText("StageTag", rowFill, "STAGE 1 • SUBJECT MONSTER", 17, Cyan, TextAlignmentOptions.MidlineLeft, true,
                    r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(-36, 26)));

                var nameTxt = CreateText("MonsterName", rowFill, "Monster Name", 25, TextMain, TextAlignmentOptions.MidlineLeft, true,
                    r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -52), new Vector2(-36, 38)));

                var statsTxt = CreateText("MonsterStats", rowFill, "HP: 100  •  ATK: 20  •  +30 XP", 19, TextSoft, TextAlignmentOptions.MidlineLeft, false,
                    r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -104), new Vector2(-36, 34)));

                _stageRows.Add(new MonsterStageRowWidgets
                {
                    root = rowBorder.gameObject,
                    border = rowBorder,
                    stageTagText = stageTagTxt,
                    nameText = nameTxt,
                    statsText = statsTxt
                });
            }

            _monsterModeCardBorder = CreateImage("MonsterModeBorder", window, Cyan);
            Place(_monsterModeCardBorder.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(964, 576));
            var monsterCard = CreateImage("MonsterModeFill", _monsterModeCardBorder.rectTransform, CardColor).rectTransform;
            Stretch(monsterCard, 2, 2, 2, 2);

            CreateText("MonsterTag", monsterCard, "4-STAGE ONLINE MONSTER AND BOSS EXPEDITION", 19, Cyan, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(-44, 28)));
            CreateText("MonsterTitle", monsterCard, "BATTLE SUBJECT MONSTERS AND STAGE BOSS", 29, TextMain, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -56), new Vector2(-44, 38)));
            CreateText("MonsterDesc", monsterCard,
                "Conquer 3 subject-themed monsters and 1 powerful Biome Boss scaled directly to your current Subject Level and Badge Tier.\n\n" +
                "• Answer aligned live questions accurately on Attack turns to unleash combo strikes and critical hits\n" +
                "• Answer accurately on Defend turns to raise your arcane shield and block incoming monster attacks\n" +
                "• Start at Level 1 in every subject and advance strictly one level at a time up to Level 100",
                20, TextSoft, TextAlignmentOptions.TopLeft, false,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -108), new Vector2(-44, 232)));

            _expeditionSummaryText = CreateText("ExpeditionSummaryText", monsterCard, "", 18, Gold, TextAlignmentOptions.TopLeft, true,
                r => Place(r, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 118), new Vector2(-44, 96)));

            _fightMonstersBtn = CreateButton("FightMonstersBtn", monsterCard, Cyan,
                new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(0, 22), new Vector2(860, 80), "BATTLE MONSTERS AND BOSS", 26, DarkText, out _);
            _fightMonstersBtn.onClick.AddListener(() =>
            {
                BattleAudioManager.Instance?.PlayButtonClick();
                Hide();
                _onStartMonsterRun?.Invoke(_currentBiomeTitle, _currentSubject);
            });

            ModalFx.Dress(_modalGroup, UIPalette.Cyan);
            ModalFx.DecorateByName((RectTransform)_modalGroup.transform, UIPalette.Cyan, true, "MonsterRosterPanelBorder", "MonsterModeBorder");
            ModalFx.DecorateByName((RectTransform)_modalGroup.transform, UIPalette.Cyan, false, "SubjectStatsCard", "MonsterStageRow_*");
            Hide();
        }

        private static void SetFill(RectTransform fill, float ratio)
        {
            if (fill == null) return;
            fill.anchorMax = new Vector2(Mathf.Clamp01(ratio), fill.anchorMax.y);
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
