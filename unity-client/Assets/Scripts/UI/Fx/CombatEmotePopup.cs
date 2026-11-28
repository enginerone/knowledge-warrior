using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    public enum CombatEmoteType
    {
        Triumph,
        CritFury,
        ShieldBlock,
        SweatMiss,
        MonsterTaunt,
        MonsterShock,
        BossRoar,
        DefeatDizzy
    }

    /// <summary>
    /// Animated RPG emote speech bubble with a procedural icon emblem and crisp callout label
    /// displayed above the Hero or Monster during battle triggers.
    /// </summary>
    public class CombatEmotePopup : MonoBehaviour
    {
        private const float Lifetime = 1.15f;
        private const float PopDuration = 0.18f;
        private const float FloatDistance = 42f;

        private RectTransform _rect;
        private CanvasGroup _group;
        private Vector2 _startPos;
        private float _age;
        private float _wobblePhase;

        public static CombatEmotePopup Create(
            RectTransform parent,
            Vector2 anchoredPosition,
            CombatEmoteType emote,
            string customLabel = null)
        {
            var (accent, defaultLabel) = GetStyle(emote);
            string labelText = string.IsNullOrWhiteSpace(customLabel) ? defaultLabel : customLabel;

            var go = new GameObject("CombatEmotePopup", typeof(RectTransform), typeof(CanvasGroup), typeof(CombatEmotePopup));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(230f, 64f);
            rt.anchoredPosition = anchoredPosition;
            rt.localScale = Vector3.one * 0.3f;

            var group = go.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            group.alpha = 0f;

            var bgGo = new GameObject("Fill", typeof(RectTransform));
            bgGo.layer = go.layer;
            bgGo.transform.SetParent(rt, false);
            var bgRt = (RectTransform)bgGo.transform;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = new Vector2(2f, 2f);
            bgRt.offsetMax = new Vector2(-2f, -2f);
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(0.05f, 0.08f, 0.20f, 0.94f);
            bgImg.raycastTarget = false;

            var glossGo = new GameObject("Gloss", typeof(RectTransform));
            glossGo.layer = go.layer;
            glossGo.transform.SetParent(bgRt, false);
            var glossRt = (RectTransform)glossGo.transform;
            glossRt.anchorMin = new Vector2(0f, 0.45f);
            glossRt.anchorMax = Vector2.one;
            glossRt.offsetMin = Vector2.zero;
            glossRt.offsetMax = Vector2.zero;
            var glossImg = glossGo.AddComponent<Image>();
            glossImg.sprite = UISprites.VerticalGradient;
            glossImg.color = new Color(1f, 1f, 1f, 0.14f);
            glossImg.raycastTarget = false;

            var borderGo = new GameObject("Ring", typeof(RectTransform));
            borderGo.layer = go.layer;
            borderGo.transform.SetParent(rt, false);
            var borderRt = (RectTransform)borderGo.transform;
            borderRt.anchorMin = Vector2.zero;
            borderRt.anchorMax = Vector2.one;
            borderRt.offsetMin = Vector2.zero;
            borderRt.offsetMax = Vector2.zero;
            var borderImg = borderGo.AddComponent<Image>();
            borderImg.sprite = UISprites.Ring;
            borderImg.type = Image.Type.Sliced;
            borderImg.color = accent;
            borderImg.raycastTarget = false;

            var iconGo = new GameObject("EmoteIcon", typeof(RectTransform));
            iconGo.layer = go.layer;
            iconGo.transform.SetParent(rt, false);
            var iconRt = (RectTransform)iconGo.transform;
            iconRt.anchorMin = new Vector2(0f, 0.5f);
            iconRt.anchorMax = new Vector2(0f, 0.5f);
            iconRt.pivot = new Vector2(0f, 0.5f);
            iconRt.anchoredPosition = new Vector2(14f, 0f);
            iconRt.sizeDelta = new Vector2(36f, 36f);
            var iconImg = iconGo.AddComponent<Image>();
            iconImg.sprite = UISprites.GetEmoteIcon(emote);
            iconImg.color = accent;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            var labelGo = new GameObject("EmoteLabel", typeof(RectTransform));
            labelGo.layer = go.layer;
            labelGo.transform.SetParent(rt, false);
            var labelRt = (RectTransform)labelGo.transform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(58f, 6f);
            labelRt.offsetMax = new Vector2(-12f, -6f);

            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text = labelText;
            tmp.color = Color.Lerp(accent, Color.white, 0.35f);
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 18f;
            tmp.fontSizeMax = 26f;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.richText = false;
            tmp.outlineWidth = 0.2f;
            tmp.outlineColor = new Color32(10, 12, 30, 255);
            tmp.raycastTarget = false;

            if (emote == CombatEmoteType.CritFury || emote == CombatEmoteType.BossRoar)
            {
                UIBurst.Sparks(rt, accent, 10, 360f);
            }

            var popup = go.GetComponent<CombatEmotePopup>();
            popup._rect = rt;
            popup._group = group;
            popup._startPos = anchoredPosition;
            popup._wobblePhase = Random.Range(0f, Mathf.PI * 2f);
            return popup;
        }

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _group = GetComponent<CanvasGroup>();
        }

        private void Update()
        {
            _age += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_age / Lifetime);

            float popK = Mathf.Clamp01(_age / PopDuration);
            float scale = _age < PopDuration ? EaseOutBack(popK) : 1f;
            _rect.localScale = Vector3.one * scale;

            float damper = 1f - k;
            float wobble = Mathf.Sin(_age * 14f + _wobblePhase) * 3.5f * damper;
            _rect.localRotation = Quaternion.Euler(0f, 0f, wobble);

            float riseK = 1f - (1f - k) * (1f - k);
            _rect.anchoredPosition = _startPos + new Vector2(0f, FloatDistance * riseK);

            if (_group != null)
            {
                if (_age < 0.08f) _group.alpha = _age / 0.08f;
                else if (k > 0.65f) _group.alpha = Mathf.Clamp01(1f - (k - 0.65f) / 0.35f);
                else _group.alpha = 1f;
            }

            if (_age >= Lifetime) Destroy(gameObject);
        }

        private static (Color accent, string defaultLabel) GetStyle(CombatEmoteType emote)
        {
            return emote switch
            {
                CombatEmoteType.Triumph => (UIPalette.Gold, "READY!"),
                CombatEmoteType.CritFury => (UIPalette.Orange, "CRITICAL!"),
                CombatEmoteType.ShieldBlock => (UIPalette.Cyan, "GUARD!"),
                CombatEmoteType.SweatMiss => (new Color(0.55f, 0.85f, 1f, 1f), "OOPS!"),
                CombatEmoteType.MonsterTaunt => (UIPalette.Violet, "GRRR!"),
                CombatEmoteType.MonsterShock => (UIPalette.Red, "ARGH!"),
                CombatEmoteType.BossRoar => (new Color(1f, 0.42f, 0.22f, 1f), "ROAR!"),
                CombatEmoteType.DefeatDizzy => (UIPalette.Pink, "KO!"),
                _ => (UIPalette.Cyan, "!")
            };
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }
    }
}
