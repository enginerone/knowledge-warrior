using TMPro;
using UnityEngine;

namespace KnowledgeWarrior.UI
{
    /// <summary>Floating combat text ("24", "MISS", "BLOCKED") that pops, rises and fades.</summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class DamagePopup : MonoBehaviour
    {
        private const float Lifetime = 0.95f;
        private const float RiseDistance = 120f;

        private TextMeshProUGUI _text;
        private RectTransform _rect;
        private Vector2 _start;
        private float _age;
        private float _scale = 1f;
        private float _drift;

        public static DamagePopup Create(RectTransform parent, Vector2 anchoredPosition, string message, Color color, float scale)
        {
            var go = new GameObject("DamagePopup", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(DamagePopup));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = message;
            text.color = color;
            text.fontSize = 64f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.richText = false;
            text.outlineWidth = 0.22f;
            text.outlineColor = new Color32(10, 12, 30, 255);

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(600f, 120f);
            rt.anchoredPosition = anchoredPosition;

            var popup = go.GetComponent<DamagePopup>();
            popup._scale = scale;
            popup._drift = Random.Range(-30f, 30f);
            return popup;
        }

        private void Awake()
        {
            _text = GetComponent<TextMeshProUGUI>();
            _rect = (RectTransform)transform;
        }

        private void Start()
        {
            _start = _rect.anchoredPosition;
        }

        private void Update()
        {
            _age += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_age / Lifetime);

            // Quick overshoot pop, then settle.
            float pop = k < 0.15f ? Mathf.Lerp(0.4f, 1.25f, k / 0.15f) : Mathf.Lerp(1.25f, 1f, Mathf.Clamp01((k - 0.15f) / 0.2f));
            _rect.localScale = Vector3.one * (pop * _scale);
            _rect.anchoredPosition = _start + new Vector2(_drift * k, RiseDistance * (1f - (1f - k) * (1f - k)));

            var c = _text.color;
            c.a = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
            _text.color = c;

            if (_age >= Lifetime) Destroy(gameObject);
        }
    }
}
