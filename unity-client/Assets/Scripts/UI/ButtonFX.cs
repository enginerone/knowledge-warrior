using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>Hover / press scale micro-animation for any button.</summary>
    public class ButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private float hoverScale = 1.03f;
        [SerializeField] private float pressScale = 0.95f;
        [SerializeField] private float speed = 18f;

        private Selectable _selectable;
        private RectTransform _target;
        private bool _hover;
        private bool _pressed;

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
            _target = transform as RectTransform;
        }

        private void OnDisable()
        {
            _hover = _pressed = false;
            if (_target != null) _target.localScale = Vector3.one;
        }

        public void OnPointerEnter(PointerEventData eventData) => _hover = true;
        public void OnPointerExit(PointerEventData eventData) { _hover = false; _pressed = false; }
        public void OnPointerDown(PointerEventData eventData) => _pressed = true;
        public void OnPointerUp(PointerEventData eventData) => _pressed = false;

        private void Update()
        {
            bool interactable = _selectable == null || _selectable.IsInteractable();
            float goal = !interactable ? 1f : _pressed ? pressScale : _hover ? hoverScale : 1f;
            // Only drive the scale while interacting so other animations (pop-in/punch) aren't overridden.
            if (!_hover && !_pressed && Mathf.Approximately(_target.localScale.x, 1f)) return;
            if (!_hover && !_pressed && !interactable) return;
            float s = Mathf.Lerp(_target.localScale.x, goal, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
            _target.localScale = new Vector3(s, s, 1f);
        }
    }
}
