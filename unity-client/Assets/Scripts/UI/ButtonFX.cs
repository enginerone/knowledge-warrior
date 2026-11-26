using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>
    /// Interactive feedback for any button: hover and press scale, bevel skin, hover brightening,
    /// touch ripple and a shine sweep on hover (or at intervals for primary buttons).
    /// </summary>
    public class ButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private const float HoverOverlayAlpha = 0.10f;
        private const float PressOverlayAlpha = 0.20f;

        [SerializeField] private float hoverScale = 1.03f;
        [SerializeField] private float pressScale = 0.95f;
        [SerializeField] private float speed = 18f;
        [SerializeField] private bool skin = true;
        [SerializeField] private bool idleShine;

        private Selectable _selectable;
        private RectTransform _target;
        private RectTransform _clip;
        private Image _overlay;
        private ShineSweep _shine;
        private float _overlayAlpha;
        private bool _hover;
        private bool _pressed;

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
            _target = transform as RectTransform;
            if (skin && _target != null) BuildSkin();
        }

        private void OnDisable()
        {
            _hover = _pressed = false;
            _overlayAlpha = 0f;
            if (_overlay != null) _overlay.canvasRenderer.SetAlpha(0f);
            if (_target != null) _target.localScale = Vector3.one;
        }

        /// <summary>Makes a primary button glint at regular intervals so it draws the eye.</summary>
        public void EnableIdleShine()
        {
            idleShine = true;
            if (_shine != null) _shine.Configure(true, 3.5f, 6.5f);
        }

        private void BuildSkin()
        {
            _overlay = UIDecorator.DecorateButton(_target);
            _clip = UIDecorator.ClipHost(_target);
            _shine = UIDecorator.AddShine(_target, 0.22f, idleShine, 3.5f, 6.5f);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hover = true;
            if (_shine != null && IsInteractable()) _shine.Trigger();
        }

        public void OnPointerExit(PointerEventData eventData) { _hover = false; _pressed = false; }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pressed = true;
            if (_clip == null || !IsInteractable()) return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_clip, eventData.position, eventData.pressEventCamera, out var local))
            {
                float size = Mathf.Max(_clip.rect.width, _clip.rect.height) * 2.2f;
                RippleFX.Spawn(_clip, local, size, new Color(1f, 1f, 1f, 0.35f));
            }
        }

        public void OnPointerUp(PointerEventData eventData) => _pressed = false;

        private bool IsInteractable() => _selectable == null || _selectable.IsInteractable();

        private void Update()
        {
            bool interactable = IsInteractable();
            UpdateOverlay(interactable);

            float goal = !interactable ? 1f : _pressed ? pressScale : _hover ? hoverScale : 1f;
            // Only drive the scale while interacting so other animations (pop-in/punch) are not overridden.
            if (!_hover && !_pressed && Mathf.Approximately(_target.localScale.x, 1f)) return;
            if (!_hover && !_pressed && !interactable) return;
            float s = Mathf.Lerp(_target.localScale.x, goal, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
            _target.localScale = new Vector3(s, s, 1f);
        }

        private void UpdateOverlay(bool interactable)
        {
            if (_overlay == null) return;
            float goal = !interactable ? 0f : _pressed ? PressOverlayAlpha : _hover ? HoverOverlayAlpha : 0f;
            if (Mathf.Approximately(_overlayAlpha, goal)) return;
            _overlayAlpha = Mathf.MoveTowards(_overlayAlpha, goal, Time.unscaledDeltaTime * 1.2f);
            _overlay.canvasRenderer.SetAlpha(_overlayAlpha);
        }
    }
}
