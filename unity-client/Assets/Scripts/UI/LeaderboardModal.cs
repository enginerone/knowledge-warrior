using System;
using System.Collections;
using System.Collections.Generic;
using KnowledgeWarrior.Progress;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>
    /// In-game Leaderboard, Warrior Profile Inspect (with Badges & Accuracy), and
    /// Guest-to-Registered Cloud Account Linking modal (Phase 3).
    ///
    /// Self-constructs its UI on the parent Canvas if not already generated in the scene,
    /// so it works out-of-the-box in both existing and newly built scenes.
    /// </summary>
    public class LeaderboardModal : MonoBehaviour
    {
        public enum LeaderboardTab { AllTime, Weekly, BiomeMasters }

        private const int MaxDisplayedRows = 8;

        // Palette matching BattleSceneBuilder
        private static readonly Color PanelColor = new(0.04f, 0.06f, 0.16f, 0.96f);
        private static readonly Color CardColor = new(0.08f, 0.11f, 0.26f, 0.95f);
        private static readonly Color RowAltColor = new(0.11f, 0.15f, 0.34f, 0.92f);
        private static readonly Color BorderColor = new(0.30f, 0.88f, 1f, 0.38f);
        private static readonly Color Cyan = new(0.30f, 0.88f, 1f, 1f);
        private static readonly Color Violet = new(0.61f, 0.42f, 1f, 1f);
        private static readonly Color Gold = new(1f, 0.82f, 0.30f, 1f);
        private static readonly Color Green = new(0.24f, 0.90f, 0.63f, 1f);
        private static readonly Color Red = new(1f, 0.36f, 0.48f, 1f);
        private static readonly Color TextMain = new(0.93f, 0.95f, 1f, 1f);
        private static readonly Color TextSoft = new(0.75f, 0.80f, 0.96f, 1f);
        private static readonly Color TextMuted = new(0.55f, 0.61f, 0.82f, 1f);
        private static readonly Color DarkText = new(0.05f, 0.07f, 0.19f, 1f);

        private CanvasGroup _modalGroup;
        private TMP_Text _hudButtonLabel;
        private TMP_Text _statusText;
        private Image[] _tabBackgrounds = new Image[3];
        private TMP_Text[] _tabLabels = new TMP_Text[3];
        private readonly List<RowWidgets> _rows = new();

        // Profile Inspect widgets
        private TMP_Text _inspectNameText;
        private TMP_Text _inspectSubtitleText;
        private TMP_Text _inspectStatsText;
        private TMP_Text _inspectBadgesText;
        private Button _resetInspectButton;

        // Auth / Account Linking widgets
        private TMP_Text _authStatusText;
        private TMP_Text _authFeedbackText;
        private GameObject _guestFormRoot;
        private GameObject _loggedInRoot;
        private TMP_InputField _usernameInput;
        private TMP_InputField _passwordInput;

        private LeaderboardTab _activeTab = LeaderboardTab.AllTime;
        private readonly List<LeaderboardEntryDto> _currentEntries = new();
        private Coroutine _fetchRoutine;

        public event Action OnProfileUpdated;

        private class RowWidgets
        {
            public GameObject root;
            public Image bg;
            public Button button;
            public TMP_Text rankText;
            public TMP_Text nameText;
            public TMP_Text levelText;
            public TMP_Text xpText;
        }

        public static LeaderboardModal EnsureOnCanvas(Canvas canvas, Action onProfileUpdated = null)
        {
            var existing = canvas.GetComponentInChildren<LeaderboardModal>(true);
            if (existing != null)
            {
                if (onProfileUpdated != null) existing.OnProfileUpdated += onProfileUpdated;
                return existing;
            }

            var go = new GameObject("LeaderboardModal", typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(canvas.transform, false);
            var modal = go.AddComponent<LeaderboardModal>();
            if (onProfileUpdated != null) modal.OnProfileUpdated += onProfileUpdated;
            modal.BuildRuntimeUI(canvas.transform);
            return modal;
        }

        private void Awake()
        {
            if (AuthManager.Instance == null)
            {
                new GameObject("AuthManager").AddComponent<AuthManager>();
            }
        }

        private void OnEnable()
        {
            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.OnAuthStateChanged += HandleAuthChanged;
            }
            if (SyncManager.Instance != null)
            {
                SyncManager.Instance.OnSyncFinished += HandleSyncFinished;
            }
        }

        private void OnDisable()
        {
            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.OnAuthStateChanged -= HandleAuthChanged;
            }
            if (SyncManager.Instance != null)
            {
                SyncManager.Instance.OnSyncFinished -= HandleSyncFinished;
            }
        }

        public void Show()
        {
            if (_modalGroup == null) BuildRuntimeUI(transform.parent);
            _modalGroup.alpha = 1f;
            _modalGroup.interactable = true;
            _modalGroup.blocksRaycasts = true;

            InspectLocalPlayer();
            RefreshAuthSection();
            SelectTab(_activeTab);
        }

        public void Hide()
        {
            if (_modalGroup == null) return;
            _modalGroup.alpha = 0f;
            _modalGroup.interactable = false;
            _modalGroup.blocksRaycasts = false;
        }

        private void SelectTab(LeaderboardTab tab)
        {
            _activeTab = tab;
            for (int i = 0; i < 3; i++)
            {
                bool active = i == (int)tab;
                if (_tabBackgrounds[i] != null)
                    _tabBackgrounds[i].color = active ? Cyan : CardColor;
                if (_tabLabels[i] != null)
                    _tabLabels[i].color = active ? DarkText : TextSoft;
            }

            if (_fetchRoutine != null) StopCoroutine(_fetchRoutine);
            _fetchRoutine = StartCoroutine(FetchLeaderboardRoutine(tab));
        }

        private IEnumerator FetchLeaderboardRoutine(LeaderboardTab tab)
        {
            string periodParam = tab switch
            {
                LeaderboardTab.Weekly => "weekly",
                LeaderboardTab.BiomeMasters => "biome",
                _ => "all"
            };

            string baseUrl = SyncManager.Instance != null
                ? SyncManager.Instance.EffectiveApiBaseUrl
                : "http://localhost:5000/api/v1";

            _statusText.text = "Fetching cloud rankings...";
            _statusText.color = TextMuted;

            string url = $"{baseUrl}/leaderboard/global?limit=25&period={periodParam}";
            using var req = UnityWebRequest.Get(url);
            req.timeout = 6;

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success && !string.IsNullOrWhiteSpace(req.downloadHandler.text))
            {
                try
                {
                    var response = JsonUtility.FromJson<LeaderboardResponseDto>(req.downloadHandler.text);
                    if (response?.leaderboard != null && response.leaderboard.Length > 0)
                    {
                        _currentEntries.Clear();
                        _currentEntries.AddRange(response.leaderboard);
                        RenderRows(_currentEntries);
                        int pending = SyncManager.Instance != null ? SyncManager.Instance.PendingCount : 0;
                        _statusText.text = pending > 0
                            ? $"LIVE CLOUD  •  {pending} battle(s) queued"
                            : "LIVE CLOUD LEADERBOARD";
                        _statusText.color = Green;
                        yield break;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[LeaderboardModal] Failed to parse leaderboard JSON: {e.Message}");
                }
            }

            // Offline / fallback leaderboard including the player's current local progress
            BuildOfflineFallbackEntries();
            RenderRows(_currentEntries);
            _statusText.text = "OFFLINE MODE (Local + Cached Rankings)";
            _statusText.color = Gold;
        }

        private void BuildOfflineFallbackEntries()
        {
            var p = ProgressManager.Current;
            var list = new List<LeaderboardEntryDto>
            {
                new() { username = p.playerName + " (You)", currentLevel = p.level, totalXp = p.totalXp, questionsAnswered = p.questionsAnswered, correctAnswers = p.correctAnswers, isLocal = true },
                new() { username = "Aria_Bitweaver", currentLevel = 6, totalXp = 1120, questionsAnswered = 48, correctAnswers = 44 },
                new() { username = "ProfessorByte", currentLevel = 5, totalXp = 840, questionsAnswered = 39, correctAnswers = 33 },
                new() { username = "NullSlayer99", currentLevel = 4, totalXp = 590, questionsAnswered = 30, correctAnswers = 24 },
                new() { username = "SyntaxKnight", currentLevel = 3, totalXp = 360, questionsAnswered = 22, correctAnswers = 17 },
                new() { username = "BinaryApprentice", currentLevel = 2, totalXp = 160, questionsAnswered = 12, correctAnswers = 9 },
            };

            if (_activeTab == LeaderboardTab.BiomeMasters)
                list.Sort((a, b) => b.currentLevel != a.currentLevel ? b.currentLevel.CompareTo(a.currentLevel) : b.totalXp.CompareTo(a.totalXp));
            else
                list.Sort((a, b) => b.totalXp.CompareTo(a.totalXp));

            for (int i = 0; i < list.Count; i++) list[i].rank = i + 1;

            _currentEntries.Clear();
            _currentEntries.AddRange(list);
        }

        private void RenderRows(List<LeaderboardEntryDto> entries)
        {
            string localName = ProgressManager.Current.playerName;
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                if (i >= entries.Count)
                {
                    r.root.SetActive(false);
                    continue;
                }

                var e = entries[i];
                r.root.SetActive(true);
                r.rankText.text = $"#{e.rank}";
                r.rankText.color = e.rank switch
                {
                    1 => Gold,
                    2 => new Color(0.82f, 0.88f, 0.98f),
                    3 => new Color(0.95f, 0.62f, 0.38f),
                    _ => TextSoft
                };

                bool isMe = e.isLocal || string.Equals(e.username, localName, StringComparison.OrdinalIgnoreCase);
                r.nameText.text = e.username;
                r.nameText.color = isMe ? Cyan : TextMain;
                r.levelText.text = $"LV {Mathf.Max(1, e.currentLevel)}";
                r.xpText.text = $"{e.totalXp:N0} XP";
                r.bg.color = isMe
                    ? new Color(0.14f, 0.28f, 0.48f, 0.95f)
                    : (i % 2 == 0 ? CardColor : RowAltColor);

                int captured = i;
                r.button.onClick.RemoveAllListeners();
                r.button.onClick.AddListener(() => InspectEntry(_currentEntries[captured]));
            }
        }

        private void InspectLocalPlayer()
        {
            var p = ProgressManager.Current;
            int acc = p.questionsAnswered > 0 ? Mathf.RoundToInt(100f * p.correctAnswers / p.questionsAnswered) : 0;
            int totalBattles = p.battlesWon + p.battlesLost;
            int winRate = totalBattles > 0 ? Mathf.RoundToInt(100f * p.battlesWon / totalBattles) : 0;
            bool linked = AuthManager.Instance != null && AuthManager.Instance.IsAuthenticated;

            _inspectNameText.text = p.playerName;
            _inspectSubtitleText.text = linked
                ? $"Cloud Connected Warrior  •  Level {p.level}"
                : $"Local Offline Warrior  •  Level {p.level}";
            _inspectStatsText.text =
                $"Total XP: <color=#FFD24D>{p.totalXp:N0}</color>   |   Global Rank: <color=#4DE1FF>{(p.globalRank > 0 ? "#" + p.globalRank : "Unranked")}</color>\n" +
                $"Accuracy: <color=#3EE6A0>{acc}%</color> ({p.correctAnswers}/{p.questionsAnswered} correct)\n" +
                $"Battles Won: {p.battlesWon}W / {p.battlesLost}L ({winRate}% Win Rate)   |   Best Stage: {p.bestStage}";

            _inspectBadgesText.text = FormatBadges(
                firstWin: p.battlesWon >= 1,
                scholar: acc >= 80 && p.questionsAnswered >= 5,
                bossSlayer: p.bestStage >= 4,
                veteran: p.level >= 5 || p.totalXp >= 500,
                cloudLinked: linked
            );

            if (_resetInspectButton != null) _resetInspectButton.gameObject.SetActive(false);
        }

        private void InspectEntry(LeaderboardEntryDto entry)
        {
            if (entry.isLocal || string.Equals(entry.username, ProgressManager.Current.playerName, StringComparison.OrdinalIgnoreCase))
            {
                InspectLocalPlayer();
                return;
            }

            int acc = entry.questionsAnswered > 0
                ? Mathf.RoundToInt(100f * entry.correctAnswers / entry.questionsAnswered)
                : 85;

            _inspectNameText.text = entry.username;
            _inspectSubtitleText.text = $"Rank #{entry.rank} Warrior  •  Level {Mathf.Max(1, entry.currentLevel)}";
            _inspectStatsText.text =
                $"Total XP: <color=#FFD24D>{entry.totalXp:N0}</color>   |   Global Rank: <color=#4DE1FF>#{entry.rank}</color>\n" +
                $"Recorded Accuracy: <color=#3EE6A0>{acc}%</color> ({entry.correctAnswers}/{entry.questionsAnswered} synced logs)";

            _inspectBadgesText.text = FormatBadges(
                firstWin: entry.totalXp > 0,
                scholar: acc >= 80 && entry.questionsAnswered >= 5,
                bossSlayer: entry.currentLevel >= 4 || entry.totalXp >= 400,
                veteran: entry.currentLevel >= 5 || entry.totalXp >= 500,
                cloudLinked: !entry.username.StartsWith("guest_", StringComparison.OrdinalIgnoreCase)
            );

            if (_resetInspectButton != null) _resetInspectButton.gameObject.SetActive(true);
        }

        private static string FormatBadges(bool firstWin, bool scholar, bool bossSlayer, bool veteran, bool cloudLinked)
        {
            static string Pill(string title, bool unlocked) =>
                unlocked ? $"<color=#3EE6A0>[{title}]</color>" : $"<color=#647099>[Locked: {title}]</color>";

            return $"{Pill("First Blood", firstWin)}   {Pill("Sharp Mind 80%+", scholar)}   {Pill("Null Dragon Slayer", bossSlayer)}\n" +
                   $"{Pill("Veteran Lv.5+", veteran)}   {Pill("Cloud Linked", cloudLinked)}";
        }

        private void RefreshAuthSection()
        {
            bool loggedIn = AuthManager.Instance != null && AuthManager.Instance.IsAuthenticated;
            var p = ProgressManager.Current;

            if (_hudButtonLabel != null)
            {
                _hudButtonLabel.text = p.globalRank > 0 ? $"RANK #{p.globalRank}" : "LEADERBOARD";
            }

            if (loggedIn)
            {
                _authStatusText.text = $"Signed in as <color=#4DE1FF>@{AuthManager.Instance.AuthenticatedUsername}</color> (Cloud Sync Active)";
                _guestFormRoot.SetActive(false);
                _loggedInRoot.SetActive(true);
            }
            else
            {
                string shortDevice = !string.IsNullOrEmpty(p.deviceId)
                    ? p.deviceId.Substring(0, Mathf.Min(8, p.deviceId.Length))
                    : "local";
                _authStatusText.text = $"Playing as <color=#FFD24D>Guest ({shortDevice})</color> — Link an account to claim your name on the Global Leaderboard:";
                _guestFormRoot.SetActive(true);
                _loggedInRoot.SetActive(false);
            }
        }

        private void OnRegisterClicked()
        {
            var gate = GetComponentInParent<Canvas>()?.GetComponentInChildren<AuthGateModal>(true);
            if (gate != null)
            {
                Hide();
                gate.Show(AuthGateModal.AuthPage.Register);
                return;
            }
        }

        private void OnLoginClicked()
        {
            if (AuthManager.Instance == null) return;
            if (!string.IsNullOrWhiteSpace(_usernameInput.text) && !string.IsNullOrWhiteSpace(_passwordInput.text))
            {
                _authFeedbackText.color = TextSoft;
                _authFeedbackText.text = "Signing in to cloud account...";
                AuthManager.Instance.Login(_usernameInput.text, _passwordInput.text, HandleAuthResult);
                return;
            }

            var gate = GetComponentInParent<Canvas>()?.GetComponentInChildren<AuthGateModal>(true);
            if (gate != null)
            {
                Hide();
                gate.Show(AuthGateModal.AuthPage.Login);
            }
        }

        private void OnLogoutClicked()
        {
            if (AuthManager.Instance == null) return;
            Hide();
            AuthManager.Instance.Logout();
        }

        private void OnSyncNowClicked()
        {
            if (SyncManager.Instance == null) return;
            _authFeedbackText.color = Cyan;
            _authFeedbackText.text = "Syncing progress & question bank...";
            SyncManager.Instance.ForceSyncAll();
        }

        private void HandleAuthResult(bool success, string message)
        {
            _authFeedbackText.color = success ? Green : Red;
            _authFeedbackText.text = message;
            if (success)
            {
                _passwordInput.text = string.Empty;
                RefreshAuthSection();
                InspectLocalPlayer();
                SelectTab(_activeTab);
                OnProfileUpdated?.Invoke();
            }
        }

        private void HandleAuthChanged()
        {
            RefreshAuthSection();
            InspectLocalPlayer();
            OnProfileUpdated?.Invoke();
        }

        private void HandleSyncFinished(bool success)
        {
            if (_modalGroup != null && _modalGroup.interactable)
            {
                RefreshAuthSection();
                InspectLocalPlayer();
                SelectTab(_activeTab);
            }
            else if (_hudButtonLabel != null && ProgressManager.Current.globalRank > 0)
            {
                _hudButtonLabel.text = $"RANK #{ProgressManager.Current.globalRank}";
            }
        }

        private void BuildRuntimeUI(Transform canvasRoot)
        {
            var rt = (RectTransform)transform;
            Stretch(rt);

            // 1. Add HUD button in SafeArea (top-center-right next to StagePill)
            Transform safeArea = canvasRoot.Find("SafeArea") ?? canvasRoot;
            var hudBtn = CreateButton("LeaderboardHudButton", safeArea, CardColor, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(410f, -24f), new Vector2(230f, 56f), "LEADERBOARD", 22, Cyan, out _hudButtonLabel);
            hudBtn.onClick.AddListener(Show);

            // 2. Full-screen modal backdrop
            var backdrop = CreateImage("Backdrop", rt, new Color(0.01f, 0.02f, 0.07f, 0.84f));
            Stretch(backdrop.rectTransform);
            _modalGroup = backdrop.gameObject.AddComponent<CanvasGroup>();

            var windowBorder = CreateImage("WindowBorder", backdrop.rectTransform, BorderColor);
            Place(windowBorder.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1740, 940));

            var window = CreateImage("WindowFill", windowBorder.rectTransform, PanelColor);
            Stretch(window.rectTransform, 2, 2, 2, 2);
            var w = window.rectTransform;

            // Header bar
            CreateText("HeaderTitle", w, "WARRIOR HALL OF FAME & CLOUD PROFILE", 36, Gold, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -22), new Vector2(920, 52)));

            _statusText = CreateText("StatusText", w, "LIVE CLOUD LEADERBOARD", 22, Green, TextAlignmentOptions.MidlineRight, true,
                r => Place(r, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-190, -22), new Vector2(540, 52)));

            var closeBtn = CreateButton("CloseButton", w, Red, new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(1, 1), new Vector2(-28, -20), new Vector2(136, 54), "CLOSE", 24, TextMain, out _);
            closeBtn.onClick.AddListener(Hide);

            // Left column: Leaderboard Tabs + Rows
            var leftCol = CreateImage("LeaderboardColumn", w, new Color(0.02f, 0.04f, 0.11f, 0.75f)).rectTransform;
            Place(leftCol, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(32, -32), new Vector2(880, -124));

            string[] tabNames = { "ALL-TIME", "WEEKLY", "BIOME MASTERS" };
            for (int i = 0; i < 3; i++)
            {
                int tabIdx = i;
                var btn = CreateButton($"Tab_{tabNames[i]}", leftCol, CardColor, new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(0, 1), new Vector2(20 + i * 284, -18), new Vector2(272, 52), tabNames[i], 22, TextSoft, out var lbl);
                _tabBackgrounds[i] = btn.GetComponent<Image>();
                _tabLabels[i] = lbl;
                btn.onClick.AddListener(() => SelectTab((LeaderboardTab)tabIdx));
            }

            // Table column headers
            CreateText("ColHeaderRank", leftCol, "RANK", 19, TextMuted, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -82), new Vector2(90, 32)));
            CreateText("ColHeaderName", leftCol, "WARRIOR", 19, TextMuted, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(140, -82), new Vector2(400, 32)));
            CreateText("ColHeaderLv", leftCol, "LEVEL", 19, TextMuted, TextAlignmentOptions.Center, true,
                r => Place(r, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-210, -82), new Vector2(110, 32)));
            CreateText("ColHeaderXp", leftCol, "TOTAL XP", 19, TextMuted, TextAlignmentOptions.MidlineRight, true,
                r => Place(r, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-34, -82), new Vector2(160, 32)));

            _rows.Clear();
            for (int i = 0; i < MaxDisplayedRows; i++)
            {
                var rowBg = CreateImage($"Row_{i}", leftCol, i % 2 == 0 ? CardColor : RowAltColor);
                Place(rowBg.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                    new Vector2(0, -120 - i * 82), new Vector2(-40, 74));

                var rowBtn = rowBg.gameObject.AddComponent<Button>();
                rowBtn.targetGraphic = rowBg;

                var rankTxt = CreateText("Rank", rowBg.rectTransform, $"#{i + 1}", 26, Gold, TextAlignmentOptions.MidlineLeft, true,
                    r => Place(r, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(90, 50)));
                var nameTxt = CreateText("Name", rowBg.rectTransform, "Warrior", 26, TextMain, TextAlignmentOptions.MidlineLeft, true,
                    r => Place(r, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(120, 0), new Vector2(420, 50)));
                var lvTxt = CreateText("Level", rowBg.rectTransform, "LV 1", 24, Violet, TextAlignmentOptions.Center, true,
                    r => Place(r, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-190, 0), new Vector2(110, 50)));
                var xpTxt = CreateText("XP", rowBg.rectTransform, "0 XP", 25, TextSoft, TextAlignmentOptions.MidlineRight, true,
                    r => Place(r, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(160, 50)));

                _rows.Add(new RowWidgets
                {
                    root = rowBg.gameObject,
                    bg = rowBg,
                    button = rowBtn,
                    rankText = rankTxt,
                    nameText = nameTxt,
                    levelText = lvTxt,
                    xpText = xpTxt
                });
            }

            // Right column: Top = Profile Inspect + Badges, Bottom = Cloud Account Linking
            var rightTop = CreateImage("ProfileInspectCard", w, CardColor).rectTransform;
            Place(rightTop, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-32, -94), new Vector2(776, 400));

            CreateText("InspectHeader", rightTop, "WARRIOR PROFILE & BADGES", 21, Cyan, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -18), new Vector2(460, 32)));

            _resetInspectButton = CreateButton("MyProfileBtn", rightTop, Violet, new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(1, 1), new Vector2(-22, -14), new Vector2(180, 40), "MY PROFILE", 19, DarkText, out _);
            _resetInspectButton.onClick.AddListener(InspectLocalPlayer);
            _resetInspectButton.gameObject.SetActive(false);

            _inspectNameText = CreateText("InspectName", rightTop, "Knowledge Warrior", 36, TextMain, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -56), new Vector2(-52, 46)));

            _inspectSubtitleText = CreateText("InspectSubtitle", rightTop, "Level 1", 23, Gold, TextAlignmentOptions.MidlineLeft, false,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -104), new Vector2(-52, 34)));

            _inspectStatsText = CreateText("InspectStats", rightTop, "", 23, TextSoft, TextAlignmentOptions.TopLeft, false,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(-52, 118)));
            _inspectStatsText.lineSpacing = 8f;

            _inspectBadgesText = CreateText("InspectBadges", rightTop, "", 21, TextMain, TextAlignmentOptions.TopLeft, true,
                r => Place(r, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(-52, 96)));
            _inspectBadgesText.lineSpacing = 10f;

            // Right bottom: Cloud Account & Sync Card
            var rightBottom = CreateImage("CloudAccountCard", w, CardColor).rectTransform;
            Place(rightBottom, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-32, 30), new Vector2(776, 390));

            CreateText("AuthHeader", rightBottom, "CLOUD ACCOUNT & GUEST LINKING", 21, Cyan, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -18), new Vector2(-52, 32)));

            _authStatusText = CreateText("AuthStatus", rightBottom, "", 22, TextSoft, TextAlignmentOptions.TopLeft, false,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -58), new Vector2(-52, 64)));

            // Guest form
            var guestRt = new GameObject("GuestForm", typeof(RectTransform)).GetComponent<RectTransform>();
            guestRt.SetParent(rightBottom, false);
            Stretch(guestRt);
            _guestFormRoot = guestRt.gameObject;

            _usernameInput = CreateInputField("UsernameInput", guestRt, "Enter Warrior Username...", false,
                new Vector2(26, -132), new Vector2(350, 58));
            _passwordInput = CreateInputField("PasswordInput", guestRt, "Enter Password...", true,
                new Vector2(398, -132), new Vector2(352, 58));

            var regBtn = CreateButton("RegisterLinkButton", guestRt, Cyan, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(0, 1), new Vector2(26, -210), new Vector2(350, 64), "LINK GUEST ACCOUNT", 22, DarkText, out _);
            regBtn.onClick.AddListener(OnRegisterClicked);

            var loginBtn = CreateButton("LoginButton", guestRt, Violet, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(0, 1), new Vector2(398, -210), new Vector2(352, 64), "EXISTING LOGIN", 22, DarkText, out _);
            loginBtn.onClick.AddListener(OnLoginClicked);

            // Logged-in actions
            var loggedRt = new GameObject("LoggedInActions", typeof(RectTransform)).GetComponent<RectTransform>();
            loggedRt.SetParent(rightBottom, false);
            Stretch(loggedRt);
            _loggedInRoot = loggedRt.gameObject;

            var syncBtn = CreateButton("SyncNowButton", loggedRt, Cyan, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(0, 1), new Vector2(26, -150), new Vector2(350, 68), "SYNC NOW", 24, DarkText, out _);
            syncBtn.onClick.AddListener(OnSyncNowClicked);

            var logoutBtn = CreateButton("LogoutButton", loggedRt, RowAltColor, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(0, 1), new Vector2(398, -150), new Vector2(352, 68), "SIGN OUT", 24, TextMain, out _);
            logoutBtn.onClick.AddListener(OnLogoutClicked);

            _authFeedbackText = CreateText("AuthFeedback", rightBottom, "", 21, Green, TextAlignmentOptions.BottomLeft, false,
                r => Place(r, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(-52, 64)));

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

        private static TMP_InputField CreateInputField(string name, Transform parent, string placeholder, bool isPassword,
            Vector2 topLeftPos, Vector2 size)
        {
            var bg = CreateImage(name, parent, new Color(0.03f, 0.05f, 0.13f, 0.98f));
            Place(bg.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), topLeftPos, size);

            var textArea = new GameObject("TextArea", typeof(RectTransform)).GetComponent<RectTransform>();
            textArea.SetParent(bg.rectTransform, false);
            Stretch(textArea, 16, 16, 8, 8);

            var placeholderTxt = CreateText("Placeholder", textArea, placeholder, 22, TextMuted, TextAlignmentOptions.MidlineLeft, false, Stretch);
            var inputTxt = CreateText("Text", textArea, "", 24, TextMain, TextAlignmentOptions.MidlineLeft, false, Stretch);

            var input = bg.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = textArea;
            input.textComponent = inputTxt;
            input.placeholder = placeholderTxt;
            input.contentType = isPassword ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
            input.characterLimit = 32;
            return input;
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
        private class LeaderboardResponseDto
        {
            public string period;
            public int count;
            public LeaderboardEntryDto[] leaderboard;
        }

        [Serializable]
        private class LeaderboardEntryDto
        {
            public int rank;
            public string id;
            public string username;
            public int totalXp;
            public int currentLevel;
            public int questionsAnswered;
            public int correctAnswers;
            public bool isLocal;
        }
    }
}

