using System;
using KnowledgeWarrior.Progress;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>
    /// Full-screen Registration -> Login -> Forgot Password (Email OTP) authentication gate.
    ///
    /// Flow:
    ///   1. Every new user starts on the Registration Page (Unique Username + Real Email + Password).
    ///   2. After successful registration in PostgreSQL, automatically proceeds to the Login Page.
    ///   3. On the Login Page, authenticates Username + Password against the database record.
    ///   4. Forgot Password sends a 6-digit OTP to their email, verifies the OTP, and updates the password.
    /// </summary>
    public class AuthGateModal : MonoBehaviour
    {
        public enum AuthPage { Register, Login, ForgotPassword }

        private static readonly Color BackdropColor = new(0.02f, 0.03f, 0.10f, 0.96f);
        private static readonly Color PanelColor = new(0.05f, 0.07f, 0.18f, 0.98f);
        private static readonly Color InputBgColor = new(0.03f, 0.04f, 0.12f, 0.98f);
        private static readonly Color BorderColor = new(0.30f, 0.88f, 1f, 0.42f);
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
        private TMP_Text _headerTitle;
        private TMP_Text _headerSubtitle;
        private TMP_Text _feedbackText;

        // Tab buttons
        private Image _regTabBg;
        private TMP_Text _regTabLabel;
        private Image _loginTabBg;
        private TMP_Text _loginTabLabel;

        // Page roots
        private GameObject _registerPageRoot;
        private GameObject _loginPageRoot;
        private GameObject _forgotPageRoot;

        // Register inputs
        private TMP_InputField _regUsernameInput;
        private TMP_InputField _regEmailInput;
        private TMP_InputField _regPasswordInput;

        // Login inputs
        private TMP_InputField _loginUsernameInput;
        private TMP_InputField _loginPasswordInput;

        // Forgot Password (Email OTP) inputs
        private TMP_InputField _forgotEmailInput;
        private TMP_InputField _forgotOtpInput;
        private TMP_InputField _forgotNewPasswordInput;
        private Button _openInboxPreviewBtn;
        private TMP_InputField _serverUrlInput;

        private Action _onLoginSuccess;

        public static AuthGateModal EnsureOnCanvas(Canvas canvas, Action onLoginSuccess)
        {
            var existing = canvas.GetComponentInChildren<AuthGateModal>(true);
            if (existing != null)
            {
                existing._onLoginSuccess = onLoginSuccess;
                return existing;
            }

            var go = new GameObject("AuthGateModal", typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(canvas.transform, false);
            var modal = go.AddComponent<AuthGateModal>();
            modal._onLoginSuccess = onLoginSuccess;
            modal.BuildRuntimeUI();
            return modal;
        }

        public void Show(AuthPage initialPage = AuthPage.Register)
        {
            if (_modalGroup == null) BuildRuntimeUI();
            transform.SetAsLastSibling();
            _modalGroup.alpha = 1f;
            _modalGroup.interactable = true;
            _modalGroup.blocksRaycasts = true;
            SwitchPage(initialPage, clearFeedback: true);
        }

        public void Hide()
        {
            if (_modalGroup == null) return;
            _modalGroup.alpha = 0f;
            _modalGroup.interactable = false;
            _modalGroup.blocksRaycasts = false;
        }

        public void SwitchPage(AuthPage page, bool clearFeedback = true)
        {
            if (clearFeedback && _feedbackText != null)
            {
                _feedbackText.text = string.Empty;
            }

            _registerPageRoot.SetActive(page == AuthPage.Register);
            _loginPageRoot.SetActive(page == AuthPage.Login);
            _forgotPageRoot.SetActive(page == AuthPage.ForgotPassword);

            if (_regTabBg != null && _loginTabBg != null)
            {
                bool isReg = page == AuthPage.Register;
                bool isLogin = page == AuthPage.Login;
                _regTabBg.color = isReg ? Cyan : InputBgColor;
                _regTabLabel.color = isReg ? DarkText : TextSoft;
                _loginTabBg.color = isLogin ? Cyan : InputBgColor;
                _loginTabLabel.color = isLogin ? DarkText : TextSoft;
            }

            switch (page)
            {
                case AuthPage.Register:
                    _headerTitle.text = "STEP 1: WARRIOR REGISTRATION";
                    _headerSubtitle.text = "Create your unique username and link a real email address for account recovery.";
                    break;
                case AuthPage.Login:
                    _headerTitle.text = "STEP 2: WARRIOR LOGIN";
                    _headerSubtitle.text = "Sign in with your registered username and password verified against PostgreSQL.";
                    break;
                case AuthPage.ForgotPassword:
                    _headerTitle.text = "FORGOT PASSWORD — EMAIL OTP VERIFICATION";
                    _headerSubtitle.text = "Request a 6-digit OTP to your registered email, verify it, and set a new password.";
                    break;
            }
        }

        private void OnRegisterSubmit()
        {
            ApplyServerUrlIfEdited();
            if (AuthManager.Instance == null) return;

            string user = _regUsernameInput.text;
            string email = _regEmailInput.text;
            string pass = _regPasswordInput.text;

            SetFeedback("Registering your warrior account in PostgreSQL...", TextSoft);

            AuthManager.Instance.Register(user, email, pass, (success, msg) =>
            {
                if (!success)
                {
                    SetFeedback(msg, Red);
                    return;
                }

                // Proceed directly to the Login Page after successful registration!
                _loginUsernameInput.text = user.Trim();
                _loginPasswordInput.text = string.Empty;
                _regPasswordInput.text = string.Empty;
                SwitchPage(AuthPage.Login, clearFeedback: false);
                SetFeedback(msg, Green);
            });
        }

        private void OnLoginSubmit()
        {
            ApplyServerUrlIfEdited();
            if (AuthManager.Instance == null) return;

            string user = _loginUsernameInput.text;
            string pass = _loginPasswordInput.text;

            SetFeedback("Verifying credentials against database record...", TextSoft);

            AuthManager.Instance.Login(user, pass, (success, msg) =>
            {
                if (!success)
                {
                    SetFeedback(msg, Red);
                    return;
                }

                _loginPasswordInput.text = string.Empty;
                SetFeedback(msg, Green);
                Hide();
                _onLoginSuccess?.Invoke();
            });
        }

        private void OnSendOtpClicked()
        {
            ApplyServerUrlIfEdited();
            if (AuthManager.Instance == null) return;
            string email = _forgotEmailInput.text;
            if (_openInboxPreviewBtn != null) _openInboxPreviewBtn.gameObject.SetActive(false);

            SetFeedback("Sending 6-digit verification OTP to your email...", TextSoft);
            AuthManager.Instance.RequestPasswordResetOtp(email, (success, msg) =>
            {
                SetFeedback(msg, success ? Gold : Red);
                if (success && _openInboxPreviewBtn != null && !string.IsNullOrWhiteSpace(AuthManager.Instance.LastOtpPreviewUrl))
                {
                    _openInboxPreviewBtn.gameObject.SetActive(true);
                }
            });
        }

        private void OnVerifyOtpOnlyClicked()
        {
            if (AuthManager.Instance == null) return;
            AuthManager.Instance.VerifyPasswordResetOtp(_forgotEmailInput.text, _forgotOtpInput.text, (success, msg) =>
            {
                SetFeedback(msg, success ? Green : Red);
            });
        }

        private void OnResetPasswordSubmit()
        {
            if (AuthManager.Instance == null) return;
            string email = _forgotEmailInput.text;
            string otp = _forgotOtpInput.text;
            string newPass = _forgotNewPasswordInput.text;

            SetFeedback("Verifying OTP and updating your password...", TextSoft);
            AuthManager.Instance.ResetPasswordWithOtp(email, otp, newPass, (success, msg, resolvedUsername) =>
            {
                if (!success)
                {
                    SetFeedback(msg, Red);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(resolvedUsername))
                {
                    _loginUsernameInput.text = resolvedUsername;
                }
                _loginPasswordInput.text = string.Empty;
                _forgotOtpInput.text = string.Empty;
                _forgotNewPasswordInput.text = string.Empty;

                SwitchPage(AuthPage.Login, clearFeedback: false);
                SetFeedback(msg, Green);
            });
        }

        private void ApplyServerUrlIfEdited()
        {
            if (_serverUrlInput != null && !string.IsNullOrWhiteSpace(_serverUrlInput.text))
            {
                SyncManager.SetCustomApiBaseUrl(_serverUrlInput.text);
            }
        }

        private void SetFeedback(string message, Color color)
        {
            if (_feedbackText == null) return;
            _feedbackText.color = color;
            _feedbackText.text = message;
        }

        private void BuildRuntimeUI()
        {
            var rt = (RectTransform)transform;
            Stretch(rt);

            var backdrop = CreateImage("Backdrop", rt, BackdropColor);
            Stretch(backdrop.rectTransform);
            _modalGroup = backdrop.gameObject.AddComponent<CanvasGroup>();

            var cardBorder = CreateImage("AuthCardBorder", backdrop.rectTransform, BorderColor);
            Place(cardBorder.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 26), new Vector2(960, 820));

            // Server API URL Bar at bottom of screen (for switching LAN IP / USB / Cloud URL on phone)
            var urlBar = CreateImage("ServerUrlBar", backdrop.rectTransform, PanelColor).rectTransform;
            Place(urlBar, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(960, 56));
            CreateText("ServerUrlLabel", urlBar, "API SERVER:", 18, TextMuted, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(150, 40)));
            _serverUrlInput = CreateInputField("ServerUrlInput", urlBar, "http://192.168.1.55:5000/api/v1", false, new Vector2(70, -4), new Vector2(760, 48));
            _serverUrlInput.text = SyncManager.Instance != null
                ? SyncManager.Instance.EffectiveApiBaseUrl
                : "http://192.168.1.55:5000/api/v1";
            _serverUrlInput.onEndEdit.AddListener(val => SyncManager.SetCustomApiBaseUrl(val));

            var card = CreateImage("AuthCardFill", cardBorder.rectTransform, PanelColor).rectTransform;
            Stretch(card, 2, 2, 2, 2);

            // Top Brand & Title
            CreateText("BrandBadge", card, "⚔️ KNOWLEDGE WARRIOR DATABASE AUTHENTICATION", 21, Cyan, TextAlignmentOptions.Center, true,
                r => Place(r, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(860, 32)));

            _headerTitle = CreateText("HeaderTitle", card, "STEP 1: WARRIOR REGISTRATION", 36, Gold, TextAlignmentOptions.Center, true,
                r => Place(r, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -64), new Vector2(860, 46)));

            _headerSubtitle = CreateText("HeaderSubtitle", card, "", 22, TextSoft, TextAlignmentOptions.Center, false,
                r => Place(r, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -114), new Vector2(860, 34)));

            // Step Switcher Tabs (1. Registration Page | 2. Login Page)
            var regTabBtn = CreateButton("TabRegister", card, Cyan, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(1f, 1f), new Vector2(-10, -162), new Vector2(390, 54), "1. REGISTRATION PAGE", 22, DarkText, out _regTabLabel);
            _regTabBg = regTabBtn.GetComponent<Image>();
            regTabBtn.onClick.AddListener(() => SwitchPage(AuthPage.Register));

            var loginTabBtn = CreateButton("TabLogin", card, InputBgColor, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(0f, 1f), new Vector2(10, -162), new Vector2(390, 54), "2. LOGIN PAGE", 22, TextSoft, out _loginTabLabel);
            _loginTabBg = loginTabBtn.GetComponent<Image>();
            loginTabBtn.onClick.AddListener(() => SwitchPage(AuthPage.Login));

            // Feedback Banner
            _feedbackText = CreateText("FeedbackText", card, "", 22, Green, TextAlignmentOptions.Center, true,
                r => Place(r, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -228), new Vector2(840, 52)));

            // Page 1: Registration Page
            var regRt = CreateContainer("RegisterPage", card);
            _registerPageRoot = regRt.gameObject;

            CreateLabel("LblUser", regRt, "UNIQUE USERNAME", new Vector2(0, -290));
            _regUsernameInput = CreateInputField("RegUsername", regRt, "Enter a unique username...", false, new Vector2(0, -326), new Vector2(800, 62));

            CreateLabel("LblEmail", regRt, "REAL EMAIL ADDRESS (FOR PASSWORD RESET OTP)", new Vector2(0, -408));
            _regEmailInput = CreateInputField("RegEmail", regRt, "Enter your registered email address...", false, new Vector2(0, -444), new Vector2(800, 62));

            CreateLabel("LblPass", regRt, "PASSWORD (MIN. 6 CHARACTERS)", new Vector2(0, -526));
            _regPasswordInput = CreateInputField("RegPassword", regRt, "Create a strong password...", true, new Vector2(0, -562), new Vector2(800, 62));

            var regSubmitBtn = CreateButton("RegisterSubmitBtn", regRt, Cyan, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(0.5f, 1), new Vector2(0, -654), new Vector2(800, 72), "REGISTER & PROCEED TO LOGIN →", 26, DarkText, out _);
            regSubmitBtn.onClick.AddListener(OnRegisterSubmit);

            var toLoginBtn = CreateButton("AlreadyHaveAccountBtn", regRt, InputBgColor, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(0.5f, 1), new Vector2(0, -740), new Vector2(460, 48), "Already registered? Go to Login Page →", 20, Cyan, out _);
            toLoginBtn.onClick.AddListener(() => SwitchPage(AuthPage.Login));

            // Page 2: Login Page
            var loginRt = CreateContainer("LoginPage", card);
            _loginPageRoot = loginRt.gameObject;

            CreateLabel("LblLoginUser", loginRt, "REGISTERED USERNAME", new Vector2(0, -304));
            _loginUsernameInput = CreateInputField("LoginUsername", loginRt, "Enter your registered username...", false, new Vector2(0, -340), new Vector2(800, 64));

            CreateLabel("LblLoginPass", loginRt, "PASSWORD", new Vector2(0, -430));
            _loginPasswordInput = CreateInputField("LoginPassword", loginRt, "Enter your password...", true, new Vector2(0, -466), new Vector2(800, 64));

            var loginSubmitBtn = CreateButton("LoginSubmitBtn", loginRt, Green, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(0.5f, 1), new Vector2(0, -564), new Vector2(800, 74), "SIGN IN TO KNOWLEDGE WARRIOR →", 26, DarkText, out _);
            loginSubmitBtn.onClick.AddListener(OnLoginSubmit);

            var forgotBtn = CreateButton("ForgotPasswordBtn", loginRt, Violet, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(1f, 1f), new Vector2(-10, -660), new Vector2(390, 56), "FORGOT PASSWORD? (EMAIL OTP)", 20, DarkText, out _);
            forgotBtn.onClick.AddListener(() =>
            {
                if (!string.IsNullOrWhiteSpace(_regEmailInput.text) && string.IsNullOrWhiteSpace(_forgotEmailInput.text))
                {
                    _forgotEmailInput.text = _regEmailInput.text;
                }
                SwitchPage(AuthPage.ForgotPassword);
            });

            var backToRegBtn = CreateButton("BackToRegisterBtn", loginRt, InputBgColor, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(0f, 1f), new Vector2(10, -660), new Vector2(390, 56), "NEW USER? REGISTER FIRST", 20, Cyan, out _);
            backToRegBtn.onClick.AddListener(() => SwitchPage(AuthPage.Register));

            // Page 3: Forgot Password (Email OTP)
            var forgotRt = CreateContainer("ForgotPasswordPage", card);
            _forgotPageRoot = forgotRt.gameObject;

            CreateLabel("LblForgotEmail", forgotRt, "1. REGISTERED EMAIL ADDRESS", new Vector2(0, -290));
            _forgotEmailInput = CreateInputField("ForgotEmail", forgotRt, "Enter your registered email...", false, new Vector2(-120, -326), new Vector2(560, 60));

            var sendOtpBtn = CreateButton("SendOtpBtn", forgotRt, Gold, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(0.5f, 1), new Vector2(290, -326), new Vector2(220, 60), "SEND OTP", 22, DarkText, out _);
            sendOtpBtn.onClick.AddListener(OnSendOtpClicked);

            CreateLabel("LblForgotOtp", forgotRt, "2. 6-DIGIT EMAIL OTP CODE", new Vector2(0, -408));
            _forgotOtpInput = CreateInputField("ForgotOtp", forgotRt, "Enter 6-digit OTP from email...", false, new Vector2(-120, -444), new Vector2(560, 60));

            var verifyOtpBtn = CreateButton("VerifyOtpBtn", forgotRt, Cyan, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(0.5f, 1), new Vector2(290, -444), new Vector2(220, 60), "VERIFY OTP", 22, DarkText, out _);
            verifyOtpBtn.onClick.AddListener(OnVerifyOtpOnlyClicked);

            CreateLabel("LblNewPass", forgotRt, "3. NEW PASSWORD (MIN. 6 CHARACTERS)", new Vector2(0, -526));
            _forgotNewPasswordInput = CreateInputField("ForgotNewPass", forgotRt, "Enter your new password...", true, new Vector2(0, -562), new Vector2(800, 60));

            var savePassBtn = CreateButton("SaveNewPassBtn", forgotRt, Green, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(0.5f, 1), new Vector2(0, -648), new Vector2(800, 70), "CHANGE PASSWORD & RETURN TO LOGIN →", 24, DarkText, out _);
            savePassBtn.onClick.AddListener(OnResetPasswordSubmit);

            var cancelForgotBtn = CreateButton("CancelForgotBtn", forgotRt, InputBgColor, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(1f, 1f), new Vector2(-10, -734), new Vector2(360, 48), "← Back to Login Page", 20, TextSoft, out _);
            cancelForgotBtn.onClick.AddListener(() => SwitchPage(AuthPage.Login));

            _openInboxPreviewBtn = CreateButton("OpenEmailInboxBtn", forgotRt, Violet, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(0f, 1f), new Vector2(10, -734), new Vector2(390, 48), "📬 OPEN EMAIL INBOX (VIEW OTP)", 19, DarkText, out _);
            _openInboxPreviewBtn.onClick.AddListener(() =>
            {
                if (AuthManager.Instance != null && !string.IsNullOrWhiteSpace(AuthManager.Instance.LastOtpPreviewUrl))
                {
                    Application.OpenURL(AuthManager.Instance.LastOtpPreviewUrl);
                }
            });
            _openInboxPreviewBtn.gameObject.SetActive(false);

            SwitchPage(AuthPage.Register);
        }

        private static RectTransform CreateContainer(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Stretch(rt);
            return rt;
        }

        private static void CreateLabel(string name, Transform parent, string text, Vector2 centerTopPos)
        {
            CreateText(name, parent, text, 19, TextMuted, TextAlignmentOptions.MidlineLeft, true,
                r => Place(r, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), centerTopPos, new Vector2(800, 28)));
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
            Vector2 centerTopPos, Vector2 size)
        {
            var border = CreateImage(name + "Border", parent, BorderColor);
            Place(border.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), centerTopPos, size);

            var bg = CreateImage(name, border.rectTransform, InputBgColor);
            Stretch(bg.rectTransform, 2, 2, 2, 2);

            var textArea = new GameObject("TextArea", typeof(RectTransform)).GetComponent<RectTransform>();
            textArea.SetParent(bg.rectTransform, false);
            Stretch(textArea, 20, 20, 8, 8);

            var placeholderTxt = CreateText("Placeholder", textArea, placeholder, 23, TextMuted, TextAlignmentOptions.MidlineLeft, false, Stretch);
            var inputTxt = CreateText("Text", textArea, "", 25, TextMain, TextAlignmentOptions.MidlineLeft, false, Stretch);

            var input = bg.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = textArea;
            input.textComponent = inputTxt;
            input.placeholder = placeholderTxt;
            input.contentType = isPassword ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
            input.characterLimit = 64;
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

