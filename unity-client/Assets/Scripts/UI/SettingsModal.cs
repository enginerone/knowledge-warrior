using System;
using KnowledgeWarrior.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>
    /// In-game Settings overlay allowing players to independently turn Background Music
    /// and Combat Sound Effects on or off, adjust volume levels, mute all audio, or
    /// restore balanced audio defaults. All button labels remain strictly text-only.
    /// </summary>
    public class SettingsModal : MonoBehaviour
    {
        private static readonly Color BackdropColor = new(0.01f, 0.02f, 0.07f, 0.94f);
        private static readonly Color PanelColor = new(0.04f, 0.06f, 0.16f, 0.98f);
        private static readonly Color CardColor = new(0.08f, 0.11f, 0.26f, 0.96f);
        private static readonly Color SubButtonColor = new(0.12f, 0.16f, 0.35f, 0.96f);
        private static readonly Color TrackColor = new(0.02f, 0.03f, 0.09f, 0.96f);
        private static readonly Color BorderColor = new(0.30f, 0.88f, 1f, 0.40f);
        private static readonly Color Cyan = new(0.30f, 0.88f, 1f, 1f);
        private static readonly Color Violet = new(0.61f, 0.42f, 1f, 1f);
        private static readonly Color Gold = new(1f, 0.82f, 0.30f, 1f);
        private static readonly Color Green = new(0.24f, 0.90f, 0.63f, 1f);
        private static readonly Color Red = new(1f, 0.36f, 0.48f, 1f);
        private static readonly Color TextMain = new(0.93f, 0.95f, 1f, 1f);
        private static readonly Color TextSoft = new(0.75f, 0.80f, 0.96f, 1f);
        private static readonly Color TextMuted = new(0.55f, 0.61f, 0.82f, 1f);
        private static readonly Color DarkText = new(0.05f, 0.07f, 0.19f, 1f);

        public static SettingsModal Instance { get; private set; }

        private CanvasGroup _modalGroup;
        private TMP_Text _musicStatusLabel;
        private Slider _musicSlider;
        private Image _musicToggleBg;
        private TMP_Text _musicToggleLabel;

        private TMP_Text _sfxStatusLabel;
        private Slider _sfxSlider;
        private Image _sfxToggleBg;
        private TMP_Text _sfxToggleLabel;

        private Image _muteAllBg;
        private TMP_Text _muteAllLabel;
        private bool _suppressSliderCallback;

        public static SettingsModal EnsureOnCanvas(Canvas canvas)
        {
            var existing = canvas.GetComponentInChildren<SettingsModal>(true);
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            var go = new GameObject("SettingsModal", typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(canvas.transform, false);
            var modal = go.AddComponent<SettingsModal>();
            Instance = modal;
            modal.BuildRuntimeUI(canvas.transform);
            return modal;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            BattleAudioManager.EnsureInstance();
            if (BattleAudioManager.Instance != null)
            {
                BattleAudioManager.Instance.OnAudioSettingsChanged += RefreshAudioControls;
            }
        }

        private void OnDisable()
        {
            if (BattleAudioManager.Instance != null)
            {
                BattleAudioManager.Instance.OnAudioSettingsChanged -= RefreshAudioControls;
            }
        }

        public void Show()
        {
            if (_modalGroup == null) BuildRuntimeUI(transform.parent);
            BattleAudioManager.EnsureInstance();
            BattleAudioManager.Instance?.PlayButtonClick();

            RefreshAudioControls();
            transform.SetAsLastSibling();

            ModalFx.Present(_modalGroup, true);
        }

        public void Hide()
        {
            if (_modalGroup == null) return;
            ModalFx.Present(_modalGroup, false);
        }

        private void RefreshAudioControls()
        {
            var audio = BattleAudioManager.Instance;
            if (audio == null || _musicStatusLabel == null) return;

            _suppressSliderCallback = true;

            int musicPct = Mathf.RoundToInt(audio.MusicVolume * 100f);
            _musicStatusLabel.text = audio.MusicEnabled ? $"MUSIC ON ({musicPct}%)" : "MUSIC OFF";
            _musicStatusLabel.color = audio.MusicEnabled ? Green : Red;
            if (_musicSlider != null) _musicSlider.SetValueWithoutNotify(audio.MusicVolume);
            if (_musicToggleBg != null) _musicToggleBg.color = audio.MusicEnabled ? SubButtonColor : Green;
            if (_musicToggleLabel != null)
            {
                _musicToggleLabel.text = audio.MusicEnabled ? "TURN MUSIC OFF" : "TURN MUSIC ON";
                _musicToggleLabel.color = audio.MusicEnabled ? TextMain : DarkText;
            }

            int sfxPct = Mathf.RoundToInt(audio.SfxVolume * 100f);
            _sfxStatusLabel.text = audio.SfxEnabled ? $"SOUND ON ({sfxPct}%)" : "SOUND OFF";
            _sfxStatusLabel.color = audio.SfxEnabled ? Green : Red;
            if (_sfxSlider != null) _sfxSlider.SetValueWithoutNotify(audio.SfxVolume);
            if (_sfxToggleBg != null) _sfxToggleBg.color = audio.SfxEnabled ? SubButtonColor : Cyan;
            if (_sfxToggleLabel != null)
            {
                _sfxToggleLabel.text = audio.SfxEnabled ? "TURN SOUND EFFECTS OFF" : "TURN SOUND EFFECTS ON";
                _sfxToggleLabel.color = audio.SfxEnabled ? TextMain : DarkText;
            }

            if (_muteAllBg != null && _muteAllLabel != null)
            {
                bool allMuted = audio.IsMuted;
                _muteAllBg.color = allMuted ? Green : Red;
                _muteAllLabel.text = allMuted ? "UNMUTE ALL AUDIO" : "MUTE ALL AUDIO";
                _muteAllLabel.color = allMuted ? DarkText : TextMain;
            }

            _suppressSliderCallback = false;
        }

        private void BuildRuntimeUI(Transform canvasRoot)
        {
            var rt = (RectTransform)transform;
            Stretch(rt);

            Transform safeArea = canvasRoot != null ? (canvasRoot.Find("SafeArea") ?? canvasRoot) : rt;
            if (safeArea.Find("SettingsHudButton") == null)
            {
                var hudBtn = CreateButton(
                    "SettingsHudButton",
                    safeArea,
                    CardColor,
                    new Vector2(1f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(-20f, -16f),
                    new Vector2(160f, 54f),
                    "SETTINGS",
                    18f,
                    Violet,
                    out _);
                hudBtn.onClick.AddListener(Show);
            }

            var backdrop = CreateImage("Backdrop", rt, BackdropColor);
            Stretch(backdrop.rectTransform);
            _modalGroup = backdrop.gameObject.AddComponent<CanvasGroup>();

            var windowBorder = CreateImage("SettingsWindowBorder", backdrop.rectTransform, BorderColor);
            Place(windowBorder.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(920f, 980f));

            var window = CreateImage("SettingsWindowFill", windowBorder.rectTransform, PanelColor).rectTransform;
            Stretch(window, 2f, 2f, 2f, 2f);

            var accentTop = CreateImage("AccentBar", window, Cyan);
            Place(accentTop.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 8f));

            CreateText("HeaderTitle", window, "GAME SETTINGS", 32f, Gold, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -20f), new Vector2(620f, 46f)));

            var closeBtn = CreateButton("CloseSettingsBtn", window, Red,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-28f, -18f), new Vector2(148f, 50f), "CLOSE", 21f, TextMain, out _);
            closeBtn.onClick.AddListener(() =>
            {
                BattleAudioManager.Instance?.PlayButtonClick();
                Hide();
            });

            CreateText("Subtitle", window,
                "Turn background music or combat sound effects on or off at any time. Audio preferences are saved automatically.",
                19f, TextSoft, TextAlignmentOptions.TopLeft, false,
                r => Place(r, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -76f), new Vector2(860f, 52f)));

            BuildMusicCard(window);
            BuildSfxCard(window);
            BuildFooterButtons(window);

            ModalFx.Dress(_modalGroup, UIPalette.Violet);
            ModalFx.DecorateByName((RectTransform)_modalGroup.transform, UIPalette.Violet, true, "MusicSettingsCard", "SfxSettingsCard");
            Hide();
        }

        private void BuildMusicCard(RectTransform window)
        {
            var card = CreateImage("MusicSettingsCard", window, CardColor).rectTransform;
            Place(card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -142f), new Vector2(860f, 340f));

            CreateText("MusicTitle", card, "BACKGROUND MUSIC", 23f, Cyan, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -18f), new Vector2(480f, 34f)));

            _musicStatusLabel = CreateText("MusicStatus", card, "MUSIC ON", 20f, Green, TextAlignmentOptions.MidlineRight, true,
                r => Place(r, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -18f), new Vector2(300f, 34f)));

            CreateText("MusicDesc", card,
                "Calm biome-matched harp and marimba melodies filtered above 215 Hz so phone speakers never vibrate.",
                18f, TextMuted, TextAlignmentOptions.TopLeft, false,
                r => Place(r, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -58f), new Vector2(804f, 50f)));

            _musicSlider = CreateVolumeSlider("MusicVolumeSlider", card, new Vector2(0f, -124f), Cyan);
            _musicSlider.onValueChanged.AddListener(val =>
            {
                if (_suppressSliderCallback) return;
                BattleAudioManager.Instance?.SetMusicVolume(val);
            });

            var quietBtn = CreateButton("MusicPresetQuiet", card, SubButtonColor,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(28f, -194f), new Vector2(256f, 50f), "QUIET LEVEL", 18f, TextSoft, out _);
            quietBtn.onClick.AddListener(() =>
            {
                BattleAudioManager.Instance?.SetMusicVolume(0.10f);
                BattleAudioManager.Instance?.PlayButtonClick();
            });

            var balancedBtn = CreateButton("MusicPresetBalanced", card, SubButtonColor,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -194f), new Vector2(256f, 50f), "BALANCED LEVEL", 18f, Gold, out _);
            balancedBtn.onClick.AddListener(() =>
            {
                BattleAudioManager.Instance?.SetMusicVolume(BattleAudioManager.DefaultMusicVolume);
                BattleAudioManager.Instance?.PlayButtonClick();
            });

            var fullBtn = CreateButton("MusicPresetFull", card, SubButtonColor,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-28f, -194f), new Vector2(256f, 50f), "LOUDER LEVEL", 18f, TextSoft, out _);
            fullBtn.onClick.AddListener(() =>
            {
                BattleAudioManager.Instance?.SetMusicVolume(0.32f);
                BattleAudioManager.Instance?.PlayButtonClick();
            });

            var toggleBtn = CreateButton("MusicToggleBtn", card, SubButtonColor,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -260f), new Vector2(804f, 60f), "TURN MUSIC OFF", 22f, TextMain, out _musicToggleLabel);
            _musicToggleBg = toggleBtn.GetComponent<Image>();
            toggleBtn.onClick.AddListener(() =>
            {
                BattleAudioManager.Instance?.ToggleMusic();
                BattleAudioManager.Instance?.PlayButtonClick();
            });
        }

        private void BuildSfxCard(RectTransform window)
        {
            var card = CreateImage("SfxSettingsCard", window, CardColor).rectTransform;
            Place(card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -502f), new Vector2(860f, 340f));

            CreateText("SfxTitle", card, "SOUND EFFECTS", 23f, Cyan, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -18f), new Vector2(480f, 34f)));

            _sfxStatusLabel = CreateText("SfxStatus", card, "SOUND ON", 20f, Green, TextAlignmentOptions.MidlineRight, true,
                r => Place(r, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -18f), new Vector2(300f, 34f)));

            CreateText("SfxDesc", card,
                "Soft crystal chimes and gentle combat percussion cues without harsh static or low-frequency buzz.",
                18f, TextMuted, TextAlignmentOptions.TopLeft, false,
                r => Place(r, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -58f), new Vector2(804f, 50f)));

            _sfxSlider = CreateVolumeSlider("SfxVolumeSlider", card, new Vector2(0f, -124f), Violet);
            _sfxSlider.onValueChanged.AddListener(val =>
            {
                if (_suppressSliderCallback) return;
                BattleAudioManager.Instance?.SetSfxVolume(val);
            });

            var softBtn = CreateButton("SfxPresetSoft", card, SubButtonColor,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(28f, -194f), new Vector2(256f, 50f), "SOFT LEVEL", 18f, TextSoft, out _);
            softBtn.onClick.AddListener(() =>
            {
                BattleAudioManager.Instance?.SetSfxVolume(0.24f);
                BattleAudioManager.Instance?.PlayButtonClick();
            });

            var stdBtn = CreateButton("SfxPresetStandard", card, SubButtonColor,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -194f), new Vector2(256f, 50f), "STANDARD LEVEL", 18f, Gold, out _);
            stdBtn.onClick.AddListener(() =>
            {
                BattleAudioManager.Instance?.SetSfxVolume(BattleAudioManager.DefaultSfxVolume);
                BattleAudioManager.Instance?.PlayButtonClick();
            });

            var previewBtn = CreateButton("SfxTestBtn", card, SubButtonColor,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-28f, -194f), new Vector2(256f, 50f), "TEST SOUND", 18f, Cyan, out _);
            previewBtn.onClick.AddListener(() =>
            {
                if (BattleAudioManager.Instance != null)
                {
                    if (!BattleAudioManager.Instance.SfxEnabled)
                    {
                        BattleAudioManager.Instance.SetSfxEnabled(true);
                    }
                    BattleAudioManager.Instance.PlayHeroAttack(false);
                }
            });

            var toggleBtn = CreateButton("SfxToggleBtn", card, SubButtonColor,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -260f), new Vector2(804f, 60f), "TURN SOUND EFFECTS OFF", 22f, TextMain, out _sfxToggleLabel);
            _sfxToggleBg = toggleBtn.GetComponent<Image>();
            toggleBtn.onClick.AddListener(() =>
            {
                bool wasEnabled = BattleAudioManager.Instance != null && BattleAudioManager.Instance.SfxEnabled;
                BattleAudioManager.Instance?.ToggleSfx();
                if (!wasEnabled)
                {
                    BattleAudioManager.Instance?.PlayButtonClick();
                }
            });
        }

        private void BuildFooterButtons(RectTransform window)
        {
            var muteAllBtn = CreateButton("MuteAllAudioBtn", window, Red,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(28f, 28f), new Vector2(418f, 68f), "MUTE ALL AUDIO", 21f, TextMain, out _muteAllLabel);
            _muteAllBg = muteAllBtn.GetComponent<Image>();
            muteAllBtn.onClick.AddListener(() =>
            {
                if (BattleAudioManager.Instance == null) return;
                bool shouldEnable = BattleAudioManager.Instance.IsMuted;
                BattleAudioManager.Instance.SetAllAudioEnabled(shouldEnable);
                if (shouldEnable)
                {
                    BattleAudioManager.Instance.PlayButtonClick();
                }
            });

            var resetBtn = CreateButton("ResetAudioDefaultsBtn", window, Cyan,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-28f, 28f), new Vector2(418f, 68f), "RESET AUDIO DEFAULTS", 21f, DarkText, out _);
            resetBtn.onClick.AddListener(() =>
            {
                BattleAudioManager.Instance?.ResetToDefaults();
                BattleAudioManager.Instance?.PlayButtonClick();
            });
        }

        private static Slider CreateVolumeSlider(string name, RectTransform parent, Vector2 anchoredPos, Color fillColor)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.layer = LayerMask.NameToLayer("UI");
            root.transform.SetParent(parent, false);
            var rootRt = (RectTransform)root.transform;
            Place(rootRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), anchoredPos, new Vector2(804f, 46f));

            var bg = CreateImage("Background", rootRt, TrackColor);
            Place(bg.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 22f));

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.layer = LayerMask.NameToLayer("UI");
            fillArea.transform.SetParent(rootRt, false);
            var fillAreaRt = (RectTransform)fillArea.transform;
            Stretch(fillAreaRt, 0f, 0f, 12f, 12f);

            var fill = CreateImage("Fill", fillAreaRt, fillColor);
            Stretch(fill.rectTransform);

            var handleSlideArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleSlideArea.layer = LayerMask.NameToLayer("UI");
            handleSlideArea.transform.SetParent(rootRt, false);
            var handleAreaRt = (RectTransform)handleSlideArea.transform;
            Stretch(handleAreaRt, 18f, 18f, 0f, 0f);

            var handle = CreateImage("Handle", handleAreaRt, TextMain);
            handle.rectTransform.sizeDelta = new Vector2(36f, 42f);

            var slider = root.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;

            return slider;
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
