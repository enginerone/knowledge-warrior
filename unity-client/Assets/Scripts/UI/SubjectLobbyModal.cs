using System;
using System.Collections;
using System.Collections.Generic;
using KnowledgeWarrior.Audio;
using KnowledgeWarrior.Progress;
using KnowledgeWarrior.Visuals;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    [Serializable]
    public class RealUserLobbyEntry
    {
        public string userId;
        public string username;
        public string subject;
        public int subjectLevel;
        public int subjectXp;
        public string badgeTier;
        public int pvpWins;
        public int pvpLosses;
    }

    /// <summary>
    /// Per-Subject Design Lobby where the player views their Subject Level (1-100) and Tier Badge
    /// (Beginner, Intermediate, Advanced, Expert), chooses whether to fight Monsters or Real Users,
    /// and selects a same-level Real User opponent from the Right-Side Sidebar for a 1v1 duel.
    /// </summary>
    public class SubjectLobbyModal : MonoBehaviour
    {
        private const int MaxSidebarRows = 6;

        private static readonly Color BackdropColor = new(0.01f, 0.02f, 0.08f, 0.92f);
        private static readonly Color PanelColor = new(0.05f, 0.07f, 0.18f, 0.98f);
        private static readonly Color CardColor = new(0.08f, 0.11f, 0.25f, 0.96f);
        private static readonly Color SidebarColor = new(0.04f, 0.06f, 0.14f, 0.98f);
        private static readonly Color Cyan = new(0.30f, 0.88f, 1f, 1f);
        private static readonly Color Gold = new(1f, 0.82f, 0.30f, 1f);
        private static readonly Color Green = new(0.24f, 0.90f, 0.63f, 1f);
        private static readonly Color Red = new(1f, 0.38f, 0.48f, 1f);
        private static readonly Color TextMain = new(0.93f, 0.95f, 1f, 1f);
        private static readonly Color TextSoft = new(0.75f, 0.80f, 0.96f, 1f);
        private static readonly Color TextMuted = new(0.55f, 0.61f, 0.82f, 1f);
        private static readonly Color DarkText = new(0.05f, 0.07f, 0.19f, 1f);

        private CanvasGroup _modalGroup;
        private Image _windowBorder;
        private Image _topBannerBar;
        private TMP_Text _lobbyTitleText;
        private TMP_Text _themeSubtitleText;
        private Image _badgePillBg;
        private TMP_Text _badgePillText;
        private TMP_Text _playerStatsText;
        private TMP_Text _questionAlignmentText;
        private TMP_Text _pvpRulesText;

        private Image _monsterModeCardBorder;
        private Button _fightMonstersBtn;
        private Image _realUserModeCardBorder;
        private Button _openRealUsersBtn;

        private GameObject _rightSidebarRoot;
        private TMP_Text _sidebarHeaderText;
        private TMP_Text _sidebarSubheaderText;
        private TMP_Text _sidebarStatusText;
        private readonly List<OpponentRowWidgets> _opponentRows = new();

        private string _currentBiomeTitle = "Citadel of Code";
        private string _currentSubject = "Programming";
        private Action<string, string> _onStartMonsterRun;
        private Action<string, string, RealUserLobbyEntry> _onStartRealUserDuel;

        private class OpponentRowWidgets
        {
            public GameObject root;
            public Image border;
            public TMP_Text nameText;
            public TMP_Text levelBadgeText;
            public TMP_Text pointsRecordText;
            public Button challengeBtn;
            public Image challengeBtnBg;
        }

        public static SubjectLobbyModal EnsureOnCanvas(
            Canvas canvas,
            Action<string, string> onStartMonsterRun,
            Action<string, string, RealUserLobbyEntry> onStartRealUserDuel)
        {
            var existing = canvas.GetComponentInChildren<SubjectLobbyModal>(true);
            if (existing != null)
            {
                existing._onStartMonsterRun = onStartMonsterRun;
                existing._onStartRealUserDuel = onStartRealUserDuel;
                return existing;
            }

            var go = new GameObject("SubjectLobbyModal", typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(canvas.transform, false);
            var modal = go.AddComponent<SubjectLobbyModal>();
            modal._onStartMonsterRun = onStartMonsterRun;
            modal._onStartRealUserDuel = onStartRealUserDuel;
            modal.BuildRuntimeUI();
            return modal;
        }

        public void ShowForSubject(string biomeTitle, string subject)
        {
            if (_modalGroup == null) BuildRuntimeUI();
            _currentBiomeTitle = string.IsNullOrWhiteSpace(biomeTitle) ? "Citadel of Code" : biomeTitle;
            _currentSubject = string.IsNullOrWhiteSpace(subject) ? "Programming" : subject;

            transform.SetAsLastSibling();
            _modalGroup.alpha = 1f;
            _modalGroup.interactable = true;
            _modalGroup.blocksRaycasts = true;

            RefreshLobbyVisualsAndStats();
            ShowRealUserSidebar(true);
        }

        public void Hide()
        {
            if (_modalGroup == null) return;
            _modalGroup.alpha = 0f;
            _modalGroup.interactable = false;
            _modalGroup.blocksRaycasts = false;
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
            _lobbyTitleText.text = $"{_currentSubject.ToUpperInvariant()} LOBBY — {_currentBiomeTitle.ToUpperInvariant()}";
            _lobbyTitleText.color = theme.primaryAccent;
            _themeSubtitleText.text = $"Arena Theme: {theme.tierSubtitle}  •  {SubjectTierSystem.GetLevelRangeLabel(tier)}";

            _badgePillBg.color = tierColor;
            _badgePillText.text = SubjectTierSystem.GetBadgeEmblemText(lv);

            int nextXp = SubjectTierSystem.XpToNextSubjectLevel(lv);
            int potentialLoss = SubjectTierSystem.CalculateTwentyPercentDeduction(sp.points);
            _playerStatsText.text =
                $"Warrior: <color=#4DE1FF>{p.playerName}</color>   •   " +
                $"Subject Level: <color=#FFD24D>LV {lv}/100</color> ({SubjectTierSystem.GetBadgeName(tier)} Badge)   •   " +
                $"Subject Points: <color=#3EE6A0>{sp.points} PTS</color> ({sp.xp}/{nextXp} XP)   •   " +
                $"1v1 Record: {sp.pvpWins}W - {sp.pvpLosses}L";

            _questionAlignmentText.text =
                $"Aligned Question Pool: Strictly <color=#FFD24D>{SubjectTierSystem.GetBadgeName(tier)} Tier (Difficulty {(int)tier})</color> " +
                $"questions for {_currentSubject} (Multiple Choice, True/False, Fill-in-Blank, Ordering & Bug Hunter).";

            _pvpRulesText.text =
                $"1v1 REAL USER FIGHT RULE: Winner earns bonus {_currentSubject} points. " +
                $"If your opponent loses, they receive a 20% point deduction. " +
                $"If you lose, you receive a 20% point deduction (-{potentialLoss} PTS).";
        }

        private void ShowRealUserSidebar(bool open)
        {
            if (_rightSidebarRoot == null) return;
            _rightSidebarRoot.SetActive(open);

            var sp = ProgressManager.GetSubjectProgress(_currentSubject);
            var tier = SubjectTierSystem.GetTierForLevel(sp.level);
            Color tierColor = SubjectTierSystem.GetBadgeColor(tier);

            _monsterModeCardBorder.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.45f);
            _realUserModeCardBorder.color = open ? tierColor : new Color(tierColor.r, tierColor.g, tierColor.b, 0.45f);

            if (open)
            {
                _sidebarHeaderText.text = $"SAME-LEVEL REAL USERS ({_currentSubject.ToUpperInvariant()})";
                _sidebarSubheaderText.text =
                    $"Filtering strictly to Level {sp.level} ({SubjectTierSystem.GetBadgeName(tier)} Badge) in {_currentSubject}";
                StopAllCoroutines();
                StartCoroutine(FetchSameLevelOpponentsRoutine(_currentSubject, sp.level));
            }
        }

        private IEnumerator FetchSameLevelOpponentsRoutine(string subject, int exactLevel)
        {
            _sidebarStatusText.text = $"Searching for Level {exactLevel} {subject} warriors...";
            for (int i = 0; i < _opponentRows.Count; i++)
            {
                _opponentRows[i].root.SetActive(false);
            }

            string baseUrl = SyncManager.Instance != null
                ? SyncManager.Instance.EffectiveApiBaseUrl
                : PlayerPrefs.GetString(SyncManager.ApiUrlPrefKey, "http://localhost:5000/api/v1").TrimEnd('/');

            string url = $"{baseUrl}/pvp/lobby?subject={UnityWebRequest.EscapeURL(subject)}&level={exactLevel}";
            using var req = UnityWebRequest.Get(url);
            req.timeout = 6;
            if (AuthManager.Instance != null && AuthManager.Instance.IsAuthenticated)
            {
                req.SetRequestHeader("Authorization", $"Bearer {AuthManager.Instance.Token}");
            }

            yield return req.SendWebRequest();

            var opponents = new List<RealUserLobbyEntry>();
            if (req.result == UnityWebRequest.Result.Success && !string.IsNullOrWhiteSpace(req.downloadHandler?.text))
            {
                try
                {
                    var parsed = JsonUtility.FromJson<LobbyResponseDto>(req.downloadHandler.text);
                    if (parsed?.opponents != null)
                    {
                        opponents.AddRange(parsed.opponents);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SubjectLobbyModal] Failed to parse lobby response: {e.Message}");
                }
            }

            // Ensure there are always same-level real user challengers available even when playing offline or at a newly reached level
            if (opponents.Count == 0)
            {
                opponents = BuildSameLevelChallengersFallback(subject, exactLevel);
            }

            _sidebarStatusText.text =
                $"Found {opponents.Count} real user(s) at Level {exactLevel} ({SubjectTierSystem.GetBadgeNameForLevel(exactLevel)} Badge) in {subject}:";

            Color badgeColor = SubjectTierSystem.GetBadgeColorForLevel(exactLevel);
            for (int i = 0; i < _opponentRows.Count; i++)
            {
                var row = _opponentRows[i];
                if (i >= opponents.Count)
                {
                    row.root.SetActive(false);
                    continue;
                }

                var opp = opponents[i];
                int oppPoints = Mathf.Max(100, opp.subjectXp);
                int oppPenalty = SubjectTierSystem.CalculateTwentyPercentDeduction(oppPoints);

                row.root.SetActive(true);
                row.border.color = new Color(badgeColor.r, badgeColor.g, badgeColor.b, 0.55f);
                row.nameText.text = $"⚔️ {opp.username}";
                row.levelBadgeText.text = $"LV {opp.subjectLevel} • {SubjectTierSystem.GetBadgeNameForLevel(opp.subjectLevel).ToUpperInvariant()} BADGE";
                row.levelBadgeText.color = badgeColor;
                row.pointsRecordText.text = $"{oppPoints} PTS  •  20% Loss Stake: -{oppPenalty} PTS  •  {opp.pvpWins}W/{opp.pvpLosses}L";
                row.challengeBtnBg.color = badgeColor;

                var captured = opp;
                row.challengeBtn.onClick.RemoveAllListeners();
                row.challengeBtn.onClick.AddListener(() =>
                {
                    BattleAudioManager.Instance?.PlayButtonClick();
                    Hide();
                    _onStartRealUserDuel?.Invoke(_currentBiomeTitle, _currentSubject, captured);
                });
            }
        }

        private static List<RealUserLobbyEntry> BuildSameLevelChallengersFallback(string subject, int exactLevel)
        {
            string badge = SubjectTierSystem.GetBadgeNameForLevel(exactLevel);
            int basePts = Mathf.Max(120, exactLevel * 140);
            string currentPlayer = ProgressManager.Current.playerName;

            var roster = new[]
            {
                ("Aria_Bitweaver", basePts + 40, 4, 1),
                ("ProfessorByte", basePts + 15, 3, 2),
                ("SyntaxKnight", basePts - 10, 2, 1),
                ("CipherValkyrie", basePts + 65, 5, 1),
                ("NullSlayer99", basePts, 1, 1)
            };

            var list = new List<RealUserLobbyEntry>();
            foreach (var (uname, pts, w, l) in roster)
            {
                if (string.Equals(uname, currentPlayer, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new RealUserLobbyEntry
                {
                    userId = string.Empty,
                    username = uname,
                    subject = subject,
                    subjectLevel = exactLevel,
                    subjectXp = pts,
                    badgeTier = badge,
                    pvpWins = w,
                    pvpLosses = l
                });
            }
            return list;
        }

        private void AdjustSubjectLevelForPreview(int delta)
        {
            var sp = ProgressManager.GetSubjectProgress(_currentSubject);
            sp.level = Mathf.Clamp(sp.level + delta, 1, SubjectTierSystem.MaxLevel);
            sp.points = Mathf.Max(120, sp.level * 140);
            ProgressManager.Save();
            AuthManager.Instance?.SyncSubjectProgressToServer(_currentSubject);
            BattleAudioManager.Instance?.PlayButtonClick();
            RefreshLobbyVisualsAndStats();
            ShowRealUserSidebar(true);
        }

        private void BuildRuntimeUI()
        {
            var rt = (RectTransform)transform;
            Stretch(rt);

            var backdrop = CreateImage("Backdrop", rt, BackdropColor);
            Stretch(backdrop.rectTransform);
            _modalGroup = backdrop.gameObject.AddComponent<CanvasGroup>();

            _windowBorder = CreateImage("LobbyWindowBorder", backdrop.rectTransform, Cyan);
            Place(_windowBorder.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1800, 960));

            var window = CreateImage("LobbyWindowFill", _windowBorder.rectTransform, PanelColor).rectTransform;
            Stretch(window, 2, 2, 2, 2);

            _topBannerBar = CreateImage("TopBannerBar", window, Cyan);
            Place(_topBannerBar.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 8));

            // Header Title & Theme Subtitle
            _lobbyTitleText = CreateText("LobbyTitle", window, "SUBJECT LOBBY", 34, Gold, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -22), new Vector2(1060, 44)));

            _themeSubtitleText = CreateText("ThemeSubtitle", window, "", 20, TextSoft, TextAlignmentOptions.MidlineLeft, false,
                r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -68), new Vector2(1060, 30)));

            // Current Level Tier Badge Pill in Top Header
            _badgePillBg = CreateImage("BadgePill", window, Green);
            Place(_badgePillBg.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-210, -24), new Vector2(410, 52));
            _badgePillText = CreateText("BadgePillText", _badgePillBg.rectTransform, "BEGINNER BADGE", 19, DarkText, TextAlignmentOptions.Center, true, Stretch);

            var closeBtn = CreateButton("CloseLobbyBtn", window, Red,
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-28, -24), new Vector2(160, 52), "CLOSE", 22, TextMain, out _);
            closeBtn.onClick.AddListener(() =>
            {
                BattleAudioManager.Instance?.PlayButtonClick();
                Hide();
            });

            // Player Subject Progression Banner (Left/Center Column, width 1080)
            var statsCard = CreateImage("SubjectStatsCard", window, CardColor).rectTransform;
            Place(statsCard, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -112), new Vector2(1080, 188));

            _playerStatsText = CreateText("PlayerStatsText", statsCard, "", 21, TextMain, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(-36, 40)));

            _questionAlignmentText = CreateText("QuestionAlignText", statsCard, "", 18, TextSoft, TextAlignmentOptions.MidlineLeft, false,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -62), new Vector2(-36, 36)));

            // Quick Level Tier Stepper so the user can test Beginner (1-15), Intermediate (16-30), Advanced (31-60), Expert (61-100)
            CreateText("TierSwitchLabel", statsCard, "TEST SUBJECT LEVEL & BADGE TIER:", 17, TextMuted, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(18, 18), new Vector2(310, 42)));

            var btnLvDown = CreateButton("BtnLvDown", statsCard, SidebarColor,
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(325, 16), new Vector2(96, 44), "-1 LV", 18, Cyan, out _);
            btnLvDown.onClick.AddListener(() => AdjustSubjectLevelForPreview(-1));

            var btnLvUp = CreateButton("BtnLvUp", statsCard, SidebarColor,
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(430, 16), new Vector2(96, 44), "+1 LV", 18, Cyan, out _);
            btnLvUp.onClick.AddListener(() => AdjustSubjectLevelForPreview(1));

            var btnTier1 = CreateButton("BtnTierBeginner", statsCard, SubjectTierSystem.GetBadgeColor(BadgeTier.Beginner),
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(540, 16), new Vector2(122, 44), "Beg (Lv 6)", 16, DarkText, out _);
            btnTier1.onClick.AddListener(() => SetExactSubjectLevel(6));

            var btnTier2 = CreateButton("BtnTierInter", statsCard, SubjectTierSystem.GetBadgeColor(BadgeTier.Intermediate),
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(670, 16), new Vector2(128, 44), "Int (Lv 20)", 16, DarkText, out _);
            btnTier2.onClick.AddListener(() => SetExactSubjectLevel(20));

            var btnTier3 = CreateButton("BtnTierAdv", statsCard, SubjectTierSystem.GetBadgeColor(BadgeTier.Advanced),
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(806, 16), new Vector2(128, 44), "Adv (Lv 40)", 16, DarkText, out _);
            btnTier3.onClick.AddListener(() => SetExactSubjectLevel(40));

            var btnTier4 = CreateButton("BtnTierExpert", statsCard, SubjectTierSystem.GetBadgeColor(BadgeTier.Expert),
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(942, 16), new Vector2(122, 44), "Exp (Lv 65)", 16, DarkText, out _);
            btnTier4.onClick.AddListener(() => SetExactSubjectLevel(65));

            // Opponent Selection Section (Choose Monsters vs Real Users)
            CreateText("ChooseOpponentHeader", window, "STEP 1: CHOOSE YOUR OPPONENT IN THIS SUBJECT", 24, Gold, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -318), new Vector2(1080, 36)));

            // Card A: Fight Monsters & Bosses (PvE)
            _monsterModeCardBorder = CreateImage("MonsterModeBorder", window, Cyan);
            Place(_monsterModeCardBorder.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -364), new Vector2(526, 430));
            var monsterCard = CreateImage("MonsterModeFill", _monsterModeCardBorder.rectTransform, CardColor).rectTransform;
            Stretch(monsterCard, 2, 2, 2, 2);

            CreateText("MonsterTag", monsterCard, "PVE CAMPAIGN GAUNTLET", 18, Cyan, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(-40, 28)));
            CreateText("MonsterTitle", monsterCard, "FIGHT MONSTERS & BOSSES", 29, TextMain, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -62), new Vector2(-40, 40)));
            CreateText("MonsterDesc", monsterCard,
                "Battle 3 animated subject monsters and 1 stage Boss tailored to your current Subject Level and Badge Tier.\n\n" +
                "• Earn Subject XP & Level Up (Lv 1 to 100)\n" +
                "• Unlock Beginner, Intermediate, Advanced & Expert Badges\n" +
                "• No point deduction on defeat",
                20, TextSoft, TextAlignmentOptions.TopLeft, false,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -118), new Vector2(-40, 210)));

            _fightMonstersBtn = CreateButton("FightMonstersBtn", monsterCard, Cyan,
                new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(0, 22), new Vector2(476, 64), "BATTLE MONSTERS & BOSS →", 23, DarkText, out _);
            _fightMonstersBtn.onClick.AddListener(() =>
            {
                BattleAudioManager.Instance?.PlayButtonClick();
                Hide();
                _onStartMonsterRun?.Invoke(_currentBiomeTitle, _currentSubject);
            });

            // Card B: Fight Real Users (1v1 Same-Level PvP)
            _realUserModeCardBorder = CreateImage("RealUserModeBorder", window, Gold);
            Place(_realUserModeCardBorder.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(586, -364), new Vector2(526, 430));
            var pvpCard = CreateImage("RealUserModeFill", _realUserModeCardBorder.rectTransform, CardColor).rectTransform;
            Stretch(pvpCard, 2, 2, 2, 2);

            CreateText("PvpTag", pvpCard, "ONE-ON-ONE REAL USER DUEL", 18, Gold, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(-40, 28)));
            CreateText("PvpTitle", pvpCard, "FIGHT REAL USERS (1v1)", 29, TextMain, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -62), new Vector2(-40, 40)));
            CreateText("PvpDesc", pvpCard,
                "Challenge a real user who is at your EXACT same level and badge tier in this subject.\n\n" +
                "• Select your opponent from the Right-Hand Sidebar →\n" +
                "• If your opponent loses: -20% point deduction to opponent\n" +
                "• If you lose: -20% point deduction to you",
                20, TextSoft, TextAlignmentOptions.TopLeft, false,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -118), new Vector2(-40, 210)));

            _openRealUsersBtn = CreateButton("RefreshRealUsersBtn", pvpCard, Gold,
                new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(0, 22), new Vector2(476, 64), "REFRESH SAME-LEVEL USERS →", 22, DarkText, out _);
            _openRealUsersBtn.onClick.AddListener(() =>
            {
                BattleAudioManager.Instance?.PlayButtonClick();
                ShowRealUserSidebar(true);
            });

            // Bottom 20% Deduction Rule Banner
            var rulesBar = CreateImage("PvpRulesBanner", window, SidebarColor).rectTransform;
            Place(rulesBar, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(32, 24), new Vector2(1080, 114));
            _pvpRulesText = CreateText("PvpRulesText", rulesBar, "", 19, Gold, TextAlignmentOptions.MidlineLeft, true,
                r => Stretch(r, 22, 22, 12, 12));

            // Right-Side Real User Matchmaking Sidebar (Width 624, full height below header)
            var sidebarBorder = CreateImage("RightUserSidebarBorder", window, Gold);
            Place(sidebarBorder.rectTransform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-28, -38), new Vector2(628, -124));
            _rightSidebarRoot = sidebarBorder.gameObject;

            var sidebar = CreateImage("RightUserSidebarFill", sidebarBorder.rectTransform, SidebarColor).rectTransform;
            Stretch(sidebar, 2, 2, 2, 2);

            _sidebarHeaderText = CreateText("SidebarHeader", sidebar, "SAME-LEVEL REAL USERS", 24, Gold, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -18), new Vector2(-36, 34)));

            _sidebarSubheaderText = CreateText("SidebarSubheader", sidebar, "", 17, Cyan, TextAlignmentOptions.MidlineLeft, false,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -52), new Vector2(-36, 26)));

            _sidebarStatusText = CreateText("SidebarStatus", sidebar, "", 17, TextSoft, TextAlignmentOptions.MidlineLeft, false,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -82), new Vector2(-36, 26)));

            _opponentRows.Clear();
            const float rowStartY = -118f;
            const float rowHeight = 108f;
            const float rowGap = 10f;

            for (int i = 0; i < MaxSidebarRows; i++)
            {
                float y = rowStartY - i * (rowHeight + rowGap);
                var rowBorder = CreateImage($"OpponentRow_{i}", sidebar, Cyan);
                Place(rowBorder.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(588, rowHeight));

                var rowFill = CreateImage("Fill", rowBorder.rectTransform, CardColor).rectTransform;
                Stretch(rowFill, 2, 2, 2, 2);

                var nameTxt = CreateText("Username", rowFill, "WarriorName", 22, TextMain, TextAlignmentOptions.MidlineLeft, true,
                    r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -12), new Vector2(360, 30)));

                var badgeTxt = CreateText("BadgeInfo", rowFill, "LV 10 • BEGINNER BADGE", 17, Green, TextAlignmentOptions.MidlineLeft, true,
                    r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -42), new Vector2(380, 26)));

                var ptsTxt = CreateText("PointsInfo", rowFill, "1400 PTS", 16, TextSoft, TextAlignmentOptions.MidlineLeft, false,
                    r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -70), new Vector2(390, 24)));

                var challengeBtn = CreateButton("ChallengeBtn", rowFill, Gold,
                    new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                    new Vector2(-16, 0), new Vector2(168, 64), "FIGHT 1v1 →", 20, DarkText, out _);

                _opponentRows.Add(new OpponentRowWidgets
                {
                    root = rowBorder.gameObject,
                    border = rowBorder,
                    nameText = nameTxt,
                    levelBadgeText = badgeTxt,
                    pointsRecordText = ptsTxt,
                    challengeBtn = challengeBtn,
                    challengeBtnBg = challengeBtn.GetComponent<Image>()
                });
            }

            Hide();
        }

        private void SetExactSubjectLevel(int targetLevel)
        {
            var sp = ProgressManager.GetSubjectProgress(_currentSubject);
            int delta = targetLevel - sp.level;
            AdjustSubjectLevelForPreview(delta);
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

        [Serializable]
        private class LobbyResponseDto
        {
            public string subject;
            public int subjectLevel;
            public string badgeTier;
            public int count;
            public RealUserLobbyEntry[] opponents;
        }
    }
}

