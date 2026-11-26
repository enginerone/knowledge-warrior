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
    /// Online-only Leaderboard, Warrior Profile Inspect (with Badges and Accuracy),
    /// and Live Cloud Account Session controls.
    /// </summary>
    public class LeaderboardModal : MonoBehaviour
    {
        public enum LeaderboardTab { AllTime, Weekly, BiomeMasters }

        private const int MaxDisplayedRows = 8;

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
        private readonly Image[] _tabBackgrounds = new Image[3];
        private readonly TMP_Text[] _tabLabels = new TMP_Text[3];
        private readonly List<RowWidgets> _rows = new();

        private TMP_Text _inspectNameText;
        private TMP_Text _inspectSubtitleText;
        private TMP_Text _inspectStatsText;
        private TMP_Text _inspectBadgesText;
        private Button _resetInspectButton;

        private TMP_Text _authStatusText;
        private TMP_Text _authFeedbackText;

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
            ModalFx.Present(_modalGroup, true);

            InspectAuthenticatedPlayer();
            RefreshAuthSection();
            SelectTab(_activeTab);
        }

        public void Hide()
        {
            if (_modalGroup == null) return;
            ModalFx.Present(_modalGroup, false);
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
            if (SyncManager.Instance != null && !SyncManager.Instance.IsOnline)
            {
                _currentEntries.Clear();
                RenderRows(_currentEntries);
                _statusText.text = "INTERNET CONNECTION LOST";
                _statusText.color = Red;
                SyncManager.Instance.ReportConnectionLost("Internet connection required to view live cloud rankings.");
                yield break;
            }

            string periodParam = tab switch
            {
                LeaderboardTab.Weekly => "weekly",
                LeaderboardTab.BiomeMasters => "biome",
                _ => "all"
            };

            string baseUrl = SyncManager.Instance != null
                ? SyncManager.Instance.EffectiveApiBaseUrl
                : SyncManager.ResolveApiBaseUrl();

            _statusText.text = "Fetching live online rankings...";
            _statusText.color = TextMuted;

            string url = $"{baseUrl}/leaderboard/global?limit=25&period={periodParam}";
            using var req = UnityWebRequest.Get(url);
            req.timeout = 8;

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.ConnectionError)
            {
                _currentEntries.Clear();
                RenderRows(_currentEntries);
                _statusText.text = "SERVER UNREACHABLE";
                _statusText.color = Red;
                SyncManager.Instance?.ReportConnectionLost($"Cannot reach leaderboard server: {req.error}");
                yield break;
            }

            if (req.result == UnityWebRequest.Result.Success && !string.IsNullOrWhiteSpace(req.downloadHandler.text))
            {
                try
                {
                    var response = JsonUtility.FromJson<LeaderboardResponseDto>(req.downloadHandler.text);
                    _currentEntries.Clear();
                    if (response?.leaderboard != null)
                    {
                        _currentEntries.AddRange(response.leaderboard);
                    }
                    RenderRows(_currentEntries);
                    _statusText.text = "LIVE ONLINE LEADERBOARD";
                    _statusText.color = Green;
                    yield break;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[LeaderboardModal] Failed to parse leaderboard JSON: {e.Message}");
                }
            }

            _currentEntries.Clear();
            RenderRows(_currentEntries);
            _statusText.text = "UNABLE TO LOAD RANKINGS";
            _statusText.color = Red;
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

                bool isMe = string.Equals(e.username, localName, StringComparison.OrdinalIgnoreCase);
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

        private void InspectAuthenticatedPlayer()
        {
            var p = ProgressManager.Current;
            int acc = p.questionsAnswered > 0 ? Mathf.RoundToInt(100f * p.correctAnswers / p.questionsAnswered) : 0;
            int totalBattles = p.battlesWon + p.battlesLost;
            int winRate = totalBattles > 0 ? Mathf.RoundToInt(100f * p.battlesWon / totalBattles) : 0;

            _inspectNameText.text = p.playerName;
            _inspectSubtitleText.text = $"Online Cloud Warrior  •  Global Level {p.level}";
            _inspectStatsText.text =
                $"Total XP: <color=#FFD24D>{p.totalXp:N0}</color>   |   Global Rank: <color=#4DE1FF>{(p.globalRank > 0 ? "#" + p.globalRank : "Unranked")}</color>\n" +
                $"Accuracy: <color=#3EE6A0>{acc}%</color> ({p.correctAnswers}/{p.questionsAnswered} correct)\n" +
                $"Battles Won: {p.battlesWon}W / {p.battlesLost}L ({winRate}% Win Rate)   |   Best Stage: {p.bestStage}";

            _inspectBadgesText.text = FormatBadges(
                firstWin: p.battlesWon >= 1,
                scholar: acc >= 80 && p.questionsAnswered >= 5,
                bossSlayer: p.bestStage >= 4,
                veteran: p.level >= 5 || p.totalXp >= 500,
                cloudLinked: true
            );

            if (_resetInspectButton != null) _resetInspectButton.gameObject.SetActive(false);
        }

        private void InspectEntry(LeaderboardEntryDto entry)
        {
            if (string.Equals(entry.username, ProgressManager.Current.playerName, StringComparison.OrdinalIgnoreCase))
            {
                InspectAuthenticatedPlayer();
                return;
            }

            int acc = entry.questionsAnswered > 0
                ? Mathf.RoundToInt(100f * entry.correctAnswers / entry.questionsAnswered)
                : 0;

            _inspectNameText.text = entry.username;
            _inspectSubtitleText.text = $"Rank #{entry.rank} Online Warrior  •  Global Level {Mathf.Max(1, entry.currentLevel)}";
            _inspectStatsText.text =
                $"Total XP: <color=#FFD24D>{entry.totalXp:N0}</color>   |   Global Rank: <color=#4DE1FF>#{entry.rank}</color>\n" +
                $"Recorded Accuracy: <color=#3EE6A0>{acc}%</color> ({entry.correctAnswers}/{entry.questionsAnswered} live answers)";

            _inspectBadgesText.text = FormatBadges(
                firstWin: entry.totalXp > 0,
                scholar: acc >= 80 && entry.questionsAnswered >= 5,
                bossSlayer: entry.currentLevel >= 4 || entry.totalXp >= 400,
                veteran: entry.currentLevel >= 5 || entry.totalXp >= 500,
                cloudLinked: true
            );

            if (_resetInspectButton != null) _resetInspectButton.gameObject.SetActive(true);
        }

        private static string FormatBadges(bool firstWin, bool scholar, bool bossSlayer, bool veteran, bool cloudLinked)
        {
            static string Pill(string title, bool unlocked) =>
                unlocked ? $"<color=#3EE6A0>[{title}]</color>" : $"<color=#647099>[Locked: {title}]</color>";

            return $"{Pill("First Blood", firstWin)}   {Pill("Sharp Mind 80%+", scholar)}   {Pill("Biome Boss Slayer", bossSlayer)}\n" +
                   $"{Pill("Veteran Lv.5+", veteran)}   {Pill("Online Verified", cloudLinked)}";
        }

        private void RefreshAuthSection()
        {
            var p = ProgressManager.Current;

            if (_hudButtonLabel != null)
            {
                _hudButtonLabel.text = p.globalRank > 0 ? $"RANK #{p.globalRank}" : "LEADERBOARD";
            }

            string user = AuthManager.Instance != null && !string.IsNullOrWhiteSpace(AuthManager.Instance.AuthenticatedUsername)
                ? AuthManager.Instance.AuthenticatedUsername
                : p.playerName;

            _authStatusText.text =
                $"Signed in as <color=#4DE1FF>@{user}</color>\n" +
                "Live cloud progression active. Every subject category starts at Level 1 and advances sequentially to Level 100.";
        }

        private void OnRefreshCloudDataClicked()
        {
            if (SyncManager.Instance == null) return;
            _authFeedbackText.color = Cyan;
            _authFeedbackText.text = "Refreshing live questions, subject levels, and rankings from server...";
            SyncManager.Instance.ForceSyncAll((ok, err) =>
            {
                _authFeedbackText.color = ok ? Green : Red;
                _authFeedbackText.text = ok
                    ? "Live cloud profile, subject levels, and question pool synchronized."
                    : (err ?? "Connection error while refreshing cloud data.");
                if (ok)
                {
                    InspectAuthenticatedPlayer();
                    SelectTab(_activeTab);
                    OnProfileUpdated?.Invoke();
                }
            });
        }

        private void OnLogoutClicked()
        {
            if (AuthManager.Instance == null) return;
            Hide();
            AuthManager.Instance.Logout();
        }

        private void HandleAuthChanged()
        {
            RefreshAuthSection();
            InspectAuthenticatedPlayer();
            OnProfileUpdated?.Invoke();
        }

        private void HandleSyncFinished(bool success)
        {
            if (_modalGroup != null && _modalGroup.interactable)
            {
                RefreshAuthSection();
                InspectAuthenticatedPlayer();
                if (success) SelectTab(_activeTab);
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

            Transform safeArea = canvasRoot.Find("SafeArea") ?? canvasRoot;
            var hudBtn = CreateButton("LeaderboardHudButton", safeArea, CardColor, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(-192f, -16f), new Vector2(214f, 54f), "LEADERBOARD", 18, Cyan, out _hudButtonLabel);
            hudBtn.onClick.AddListener(Show);

            var backdrop = CreateImage("Backdrop", rt, new Color(0.01f, 0.02f, 0.07f, 0.96f));
            Stretch(backdrop.rectTransform);
            _modalGroup = backdrop.gameObject.AddComponent<CanvasGroup>();

            var windowBorder = CreateImage("WindowBorder", backdrop.rectTransform, BorderColor);
            Place(windowBorder.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1020, 1820));

            var window = CreateImage("WindowFill", windowBorder.rectTransform, PanelColor);
            Stretch(window.rectTransform, 2, 2, 2, 2);
            var w = window.rectTransform;

            CreateText("HeaderTitle", w, "WARRIOR HALL OF FAME", 32, Gold, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -18), new Vector2(780, 44)));

            var closeBtn = CreateButton("CloseButton", w, Red, new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(1, 1), new Vector2(-28, -18), new Vector2(148, 50), "CLOSE", 21, TextMain, out _);
            closeBtn.onClick.AddListener(Hide);

            _statusText = CreateText("StatusText", w, "LIVE ONLINE LEADERBOARD", 19, Green, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -68), new Vector2(960, 32)));

            var leftCol = CreateImage("LeaderboardColumn", w, new Color(0.02f, 0.04f, 0.11f, 0.75f)).rectTransform;
            Place(leftCol, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -112), new Vector2(964, 836));

            string[] tabNames = { "ALL-TIME", "WEEKLY", "BIOME MASTERS" };
            for (int i = 0; i < 3; i++)
            {
                int tabIdx = i;
                var btn = CreateButton($"Tab_{tabNames[i]}", leftCol, CardColor, new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(0, 1), new Vector2(20 + i * 314, -16), new Vector2(296, 50), tabNames[i], 20, TextSoft, out var lbl);
                _tabBackgrounds[i] = btn.GetComponent<Image>();
                _tabLabels[i] = lbl;
                btn.onClick.AddListener(() => SelectTab((LeaderboardTab)tabIdx));
            }

            CreateText("ColHeaderRank", leftCol, "RANK", 18, TextMuted, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -78), new Vector2(90, 30)));
            CreateText("ColHeaderName", leftCol, "WARRIOR", 18, TextMuted, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(140, -78), new Vector2(440, 30)));
            CreateText("ColHeaderLv", leftCol, "LEVEL", 18, TextMuted, TextAlignmentOptions.Center, true,
                r => Place(r, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-210, -78), new Vector2(110, 30)));
            CreateText("ColHeaderXp", leftCol, "TOTAL XP", 18, TextMuted, TextAlignmentOptions.MidlineRight, true,
                r => Place(r, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-34, -78), new Vector2(160, 30)));

            _rows.Clear();
            for (int i = 0; i < MaxDisplayedRows; i++)
            {
                var rowBg = CreateImage($"Row_{i}", leftCol, i % 2 == 0 ? CardColor : RowAltColor);
                Place(rowBg.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                    new Vector2(0, -116 - i * 86), new Vector2(-40, 76));

                var rowBtn = rowBg.gameObject.AddComponent<Button>();
                rowBtn.targetGraphic = rowBg;

                var rankTxt = CreateText("Rank", rowBg.rectTransform, $"#{i + 1}", 25, Gold, TextAlignmentOptions.MidlineLeft, true,
                    r => Place(r, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(90, 50)));
                var nameTxt = CreateText("Name", rowBg.rectTransform, "Warrior", 25, TextMain, TextAlignmentOptions.MidlineLeft, true,
                    r => Place(r, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(120, 0), new Vector2(460, 50)));
                var lvTxt = CreateText("Level", rowBg.rectTransform, "LV 1", 23, Violet, TextAlignmentOptions.Center, true,
                    r => Place(r, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-190, 0), new Vector2(110, 50)));
                var xpTxt = CreateText("XP", rowBg.rectTransform, "0 XP", 24, TextSoft, TextAlignmentOptions.MidlineRight, true,
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

            var rightTop = CreateImage("ProfileInspectCard", w, CardColor).rectTransform;
            Place(rightTop, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -966), new Vector2(964, 464));

            CreateText("InspectHeader", rightTop, "WARRIOR PROFILE AND BADGES", 21, Cyan, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -18), new Vector2(560, 32)));

            _resetInspectButton = CreateButton("MyProfileBtn", rightTop, Violet, new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(1, 1), new Vector2(-22, -14), new Vector2(180, 40), "MY PROFILE", 19, DarkText, out _);
            _resetInspectButton.onClick.AddListener(InspectAuthenticatedPlayer);
            _resetInspectButton.gameObject.SetActive(false);

            _inspectNameText = CreateText("InspectName", rightTop, "Knowledge Warrior", 34, TextMain, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -56), new Vector2(-52, 46)));

            _inspectSubtitleText = CreateText("InspectSubtitle", rightTop, "Level 1", 22, Gold, TextAlignmentOptions.MidlineLeft, false,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -106), new Vector2(-52, 34)));

            _inspectStatsText = CreateText("InspectStats", rightTop, "", 22, TextSoft, TextAlignmentOptions.TopLeft, false,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -154), new Vector2(-52, 148)));
            _inspectStatsText.lineSpacing = 8f;

            _inspectBadgesText = CreateText("InspectBadges", rightTop, "", 20, TextMain, TextAlignmentOptions.TopLeft, true,
                r => Place(r, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(-52, 118)));
            _inspectBadgesText.lineSpacing = 10f;

            var rightBottom = CreateImage("CloudAccountCard", w, CardColor).rectTransform;
            Place(rightBottom, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(964, 346));

            CreateText("AuthHeader", rightBottom, "ONLINE CLOUD SESSION", 21, Cyan, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -18), new Vector2(-52, 32)));

            _authStatusText = CreateText("AuthStatus", rightBottom, "", 21, TextSoft, TextAlignmentOptions.TopLeft, false,
                r => Place(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -56), new Vector2(-52, 86)));

            var refreshBtn = CreateButton("RefreshCloudButton", rightBottom, Cyan, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(0, 1), new Vector2(26, -158), new Vector2(444, 68), "REFRESH CLOUD DATA", 22, DarkText, out _);
            refreshBtn.onClick.AddListener(OnRefreshCloudDataClicked);

            var logoutBtn = CreateButton("LogoutButton", rightBottom, RowAltColor, new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(1, 1), new Vector2(-26, -158), new Vector2(444, 68), "SIGN OUT", 22, TextMain, out _);
            logoutBtn.onClick.AddListener(OnLogoutClicked);

            _authFeedbackText = CreateText("AuthFeedback", rightBottom, "", 20, Green, TextAlignmentOptions.BottomLeft, false,
                r => Place(r, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(-52, 74)));

            ModalFx.Dress(_modalGroup, UIPalette.Cyan);
            ModalFx.DecorateByName((RectTransform)_modalGroup.transform, UIPalette.Cyan, false, "ProfileInspectCard", "CloudAccountCard", "LeaderboardColumn");
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
        }
    }
}
