using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>
    /// Displays the custom Knowledge Warrior brand intro screen on game startup instead of a plain engine logo.
    /// Runs on a top-level nested Canvas so authentication and server bootstrap proceed smoothly underneath.
    /// </summary>
    public class BrandIntroModal : MonoBehaviour
    {
        private static readonly Color BackdropColor = new(0.031f, 0.043f, 0.110f, 1f);
        private static readonly Color CardColor = new(0.055f, 0.082f, 0.21f, 0.96f);
        private static readonly Color Cyan = new(0.30f, 0.88f, 1f, 1f);
        private static readonly Color Gold = new(1f, 0.82f, 0.30f, 1f);
        private static readonly Color TextSoft = new(0.76f, 0.84f, 0.98f, 1f);

        private CanvasGroup _group;
        private RectTransform _logoRect;
        private RectTransform _progressFill;
        private Image _shineOverlay;
        private bool _hasPlayed;
        private bool _skipRequested;

        public static BrandIntroModal EnsureOnCanvas(Canvas parentCanvas)
        {
            var existing = parentCanvas.GetComponentInChildren<BrandIntroModal>(true);
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject("BrandIntroModal", typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parentCanvas.transform, false);

            var nestedCanvas = go.AddComponent<Canvas>();
            nestedCanvas.overrideSorting = true;
            nestedCanvas.sortingOrder = 500;
            go.AddComponent<GraphicRaycaster>();

            var modal = go.AddComponent<BrandIntroModal>();
            modal.BuildRuntimeUI();
            return modal;
        }

        public void PlayIntro(Action onFinished = null)
        {
            if (_group == null) BuildRuntimeUI();
            if (_hasPlayed)
            {
                gameObject.SetActive(false);
                onFinished?.Invoke();
                return;
            }

            _hasPlayed = true;
            _skipRequested = false;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            StartCoroutine(IntroSequenceRoutine(onFinished));
        }

        private IEnumerator IntroSequenceRoutine(Action onFinished)
        {
            _group.alpha = 1f;
            _group.interactable = true;
            _group.blocksRaycasts = true;

            const float holdDuration = 1.85f;
            const float fadeDuration = 0.38f;
            float elapsed = 0f;

            while (elapsed < holdDuration && !_skipRequested)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / holdDuration);

                if (_logoRect != null)
                {
                    float scale = t < 0.25f
                        ? Mathf.Lerp(0.84f, 1.04f, Mathf.Sin((t / 0.25f) * Mathf.PI * 0.5f))
                        : Mathf.Lerp(1.04f, 1.0f, (t - 0.25f) / 0.75f);
                    _logoRect.localScale = new Vector3(scale, scale, 1f);
                }

                if (_progressFill != null)
                {
                    _progressFill.anchorMax = new Vector2(Mathf.SmoothStep(0.05f, 1f, t), 1f);
                }

                if (_shineOverlay != null)
                {
                    float pulse = 0.08f + 0.12f * Mathf.Sin(elapsed * 4.5f);
                    _shineOverlay.color = new Color(Cyan.r, Cyan.g, Cyan.b, pulse);
                }

                yield return null;
            }

            float fadeElapsed = 0f;
            while (fadeElapsed < fadeDuration)
            {
                fadeElapsed += Time.unscaledDeltaTime;
                _group.alpha = 1f - Mathf.Clamp01(fadeElapsed / fadeDuration);
                yield return null;
            }

            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;
            gameObject.SetActive(false);
            onFinished?.Invoke();
        }

        private void BuildRuntimeUI()
        {
            var rt = (RectTransform)transform;
            Stretch(rt);

            var bg = CreateImage("Backdrop", rt, BackdropColor);
            Stretch(bg.rectTransform);
            _group = bg.gameObject.AddComponent<CanvasGroup>();

            var tapToSkip = bg.gameObject.AddComponent<Button>();
            tapToSkip.transition = Selectable.Transition.None;
            tapToSkip.onClick.AddListener(() => _skipRequested = true);

            AmbientParticles.Create(bg.rectTransform, 18, UIPalette.Ambient, true, 8f, 22f);

            var crestFrame = CreateImage("BrandCrestBorder", bg.rectTransform, new Color(Gold.r, Gold.g, Gold.b, 0.75f));
            Place(crestFrame.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 120f), new Vector2(268f, 268f));
            _logoRect = crestFrame.rectTransform;

            var crestFill = CreateImage("Fill", crestFrame.rectTransform, CardColor);
            Stretch(crestFill.rectTransform, 4f, 4f, 4f, 4f);

            _shineOverlay = CreateImage("AuraGlow", crestFill.rectTransform, new Color(Cyan.r, Cyan.g, Cyan.b, 0.12f));
            Stretch(_shineOverlay.rectTransform);

            var logoImg = CreateImage("BrandLogo", crestFill.rectTransform, Color.white);
            Stretch(logoImg.rectTransform, 8f, 8f, 8f, 8f);
            logoImg.sprite = UISprites.BrandLogo;
            logoImg.preserveAspect = true;

            CreateText("BrandTitle", bg.rectTransform, "KNOWLEDGE WARRIOR", 48f, Gold, TextAlignmentOptions.Center, true,
                r => Place(r, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, -76f), new Vector2(920f, 64f)));

            CreateText("BrandTagline", bg.rectTransform, "ARCANE CURRICULUM RPG", 24f, Cyan, TextAlignmentOptions.Center, true,
                r => Place(r, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, -132f), new Vector2(780f, 36f)));

            var track = CreateImage("LoadingTrack", bg.rectTransform, new Color(0.09f, 0.13f, 0.28f, 0.95f));
            Place(track.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -210f), new Vector2(480f, 14f));

            var fill = CreateImage("LoadingFill", track.rectTransform, Cyan);
            _progressFill = fill.rectTransform;
            Stretch(_progressFill);
            _progressFill.anchorMax = new Vector2(0.05f, 1f);

            CreateText("StudioCaption", bg.rectTransform, "KNOWLEDGE WARRIOR STUDIOS", 18f, TextSoft, TextAlignmentOptions.Center, false,
                r => Place(r, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 68f), new Vector2(640f, 30f)));
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = name == "Backdrop";
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
