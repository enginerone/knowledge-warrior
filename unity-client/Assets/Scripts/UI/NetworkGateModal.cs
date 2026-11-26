using System;
using KnowledgeWarrior.Audio;
using KnowledgeWarrior.Progress;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>
    /// Full-screen online connectivity gate that blocks all gameplay whenever internet data
    /// is unavailable or the Knowledge Warrior backend server cannot be reached.
    /// </summary>
    public class NetworkGateModal : MonoBehaviour
    {
        private static readonly Color BackdropColor = new(0.03f, 0.04f, 0.11f, 1f);
        private static readonly Color PanelColor = new(0.05f, 0.07f, 0.18f, 1f);
        private static readonly Color InputBgColor = new(0.03f, 0.04f, 0.12f, 0.98f);
        private static readonly Color BorderColor = new(1f, 0.38f, 0.48f, 0.65f);
        private static readonly Color Cyan = new(0.30f, 0.88f, 1f, 1f);
        private static readonly Color Gold = new(1f, 0.82f, 0.30f, 1f);
        private static readonly Color Red = new(1f, 0.38f, 0.48f, 1f);
        private static readonly Color TextMain = new(0.93f, 0.95f, 1f, 1f);
        private static readonly Color TextSoft = new(0.75f, 0.80f, 0.96f, 1f);
        private static readonly Color TextMuted = new(0.55f, 0.61f, 0.82f, 1f);
        private static readonly Color DarkText = new(0.05f, 0.07f, 0.19f, 1f);

        private CanvasGroup _modalGroup;
        private TMP_Text _titleText;
        private TMP_Text _bodyText;
        private TMP_Text _errorDetailText;
        private Button _retryButton;
        private TMP_Text _retryButtonLabel;
        private TMP_InputField _serverUrlInput;
        private Action _onRetryRequested;

        public bool IsVisible => _modalGroup != null && _modalGroup.interactable;

        public static NetworkGateModal EnsureOnCanvas(Canvas canvas, Action onRetryRequested)
        {
            var existing = canvas.GetComponentInChildren<NetworkGateModal>(true);
            if (existing != null)
            {
                existing._onRetryRequested = onRetryRequested;
                return existing;
            }

            var go = new GameObject("NetworkGateModal", typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(canvas.transform, false);
            var modal = go.AddComponent<NetworkGateModal>();
            modal._onRetryRequested = onRetryRequested;
            modal.BuildRuntimeUI();
            return modal;
        }

        private void OnEnable()
        {
            SyncManager.OnApiBaseUrlChanged += HandleApiBaseUrlChanged;
        }

        private void OnDisable()
        {
            SyncManager.OnApiBaseUrlChanged -= HandleApiBaseUrlChanged;
        }

        private void HandleApiBaseUrlChanged(string updatedUrl)
        {
            if (_serverUrlInput != null && !string.IsNullOrWhiteSpace(updatedUrl))
            {
                _serverUrlInput.SetTextWithoutNotify(updatedUrl);
            }
        }

        public void Show(string errorReason = null, bool isConnecting = false)
        {
            if (_modalGroup == null) BuildRuntimeUI();

            if (_serverUrlInput != null)
            {
                _serverUrlInput.SetTextWithoutNotify(
                    SyncManager.Instance != null
                        ? SyncManager.Instance.EffectiveApiBaseUrl
                        : SyncManager.ResolveApiBaseUrl());
            }

            transform.SetAsLastSibling();
            ModalFx.Present(_modalGroup, true);

            if (isConnecting)
            {
                SetConnectingState(errorReason ?? "Connecting to Knowledge Warrior online server...");
                return;
            }

            _titleText.text = "INTERNET CONNECTION REQUIRED";
            _titleText.color = Red;
            _bodyText.text =
                "Knowledge Warrior is a completely online game and does not operate without active internet data.\n" +
                "Live curriculum questions, warrior authentication, and sequential subject progression require a server connection.";
            _errorDetailText.text = string.IsNullOrWhiteSpace(errorReason)
                ? "Please enable Wi-Fi or mobile data and verify that the game server is reachable."
                : errorReason;
            _errorDetailText.color = Gold;

            if (_retryButton != null) _retryButton.interactable = true;
            if (_retryButtonLabel != null) _retryButtonLabel.text = "RETRY CONNECTION";
        }

        public void SetConnectingState(string statusMessage)
        {
            if (_modalGroup == null) BuildRuntimeUI();
            transform.SetAsLastSibling();
            ModalFx.Present(_modalGroup, true);

            _titleText.text = "CONNECTING TO ONLINE SERVER";
            _titleText.color = Cyan;
            _bodyText.text =
                "Verifying internet connectivity, loading live curriculum questions, and synchronizing your warrior profile...";
            _errorDetailText.text = statusMessage ?? "Establishing secure connection...";
            _errorDetailText.color = TextSoft;

            if (_retryButton != null) _retryButton.interactable = false;
            if (_retryButtonLabel != null) _retryButtonLabel.text = "CONNECTING";
        }

        public void Hide()
        {
            if (_modalGroup == null) return;
            ModalFx.Present(_modalGroup, false);
        }

        private void OnRetryClicked()
        {
            BattleAudioManager.Instance?.PlayButtonClick();
            if (_serverUrlInput != null && !string.IsNullOrWhiteSpace(_serverUrlInput.text))
            {
                SyncManager.SetCustomApiBaseUrl(_serverUrlInput.text);
            }
            SetConnectingState("Reconnecting to Knowledge Warrior server...");
            _onRetryRequested?.Invoke();
        }

        private void BuildRuntimeUI()
        {
            var rt = (RectTransform)transform;
            Stretch(rt);

            var backdrop = CreateImage("Backdrop", rt, BackdropColor);
            Stretch(backdrop.rectTransform);
            _modalGroup = backdrop.gameObject.AddComponent<CanvasGroup>();

            var cardBorder = CreateImage("GateCardBorder", backdrop.rectTransform, BorderColor);
            Place(cardBorder.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(960, 620));

            var card = CreateImage("GateCardFill", cardBorder.rectTransform, PanelColor).rectTransform;
            Stretch(card, 2, 2, 2, 2);

            CreateText("OnlineOnlyTag", card, "ONLINE-ONLY GAME MODE", 20, Cyan, TextAlignmentOptions.Center, true,
                r => Place(r, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(860, 30)));

            _titleText = CreateText("GateTitle", card, "INTERNET CONNECTION REQUIRED", 38, Red, TextAlignmentOptions.Center, true,
                r => Place(r, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -82), new Vector2(860, 52)));

            _bodyText = CreateText("GateBody", card, "", 23, TextSoft, TextAlignmentOptions.Center, false,
                r => Place(r, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -162), new Vector2(860, 150)));
            _bodyText.lineSpacing = 8f;

            _errorDetailText = CreateText("GateErrorDetail", card, "", 21, Gold, TextAlignmentOptions.Center, true,
                r => Place(r, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -340), new Vector2(860, 100)));

            _retryButton = CreateButton("RetryConnectionBtn", card, Cyan,
                new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(0, 44), new Vector2(500, 76), "RETRY CONNECTION", 26, DarkText, out _retryButtonLabel);
            _retryButton.onClick.AddListener(OnRetryClicked);

            if (SyncManager.IsLocalDevelopmentEnvironment)
            {
                var urlBar = CreateImage("ServerUrlBar", backdrop.rectTransform, PanelColor).rectTransform;
                Place(urlBar, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 32), new Vector2(960, 62));

                CreateText("ServerUrlLabel", urlBar, "API SERVER:", 18, TextMuted, TextAlignmentOptions.MidlineLeft, true,
                    r => Place(r, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(150, 40)));

                _serverUrlInput = CreateInputField("ServerUrlInput", urlBar, "Enter Knowledge Warrior API URL", new Vector2(70, -7), new Vector2(760, 48));
                _serverUrlInput.text = SyncManager.ResolveApiBaseUrl();
                _serverUrlInput.onEndEdit.AddListener(val => SyncManager.SetCustomApiBaseUrl(val));
            }

            ModalFx.Dress(_modalGroup, UIPalette.Orange);
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

        private static TMP_InputField CreateInputField(string name, Transform parent, string placeholder, Vector2 centerTopPos, Vector2 size)
        {
            var border = CreateImage(name + "Border", parent, new Color(0.30f, 0.88f, 1f, 0.38f));
            Place(border.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), centerTopPos, size);

            var bg = CreateImage(name, border.rectTransform, InputBgColor);
            Stretch(bg.rectTransform, 2, 2, 2, 2);

            var textArea = new GameObject("TextArea", typeof(RectTransform)).GetComponent<RectTransform>();
            textArea.SetParent(bg.rectTransform, false);
            Stretch(textArea, 18, 18, 6, 6);

            var placeholderTxt = CreateText("Placeholder", textArea, placeholder, 21, TextMuted, TextAlignmentOptions.MidlineLeft, false, Stretch);
            var inputTxt = CreateText("Text", textArea, "", 23, TextMain, TextAlignmentOptions.MidlineLeft, false, Stretch);

            var input = bg.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = textArea;
            input.textComponent = inputTxt;
            input.placeholder = placeholderTxt;
            input.characterLimit = 96;
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
    }
}
