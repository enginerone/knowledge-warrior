using TMPro;
using UnityEngine;

namespace KnowledgeWarrior.UI
{
    /// <summary>Escalating streak counter: tier colors, a punch and a spark burst on every new streak level.</summary>
    public class StreakFX : MonoBehaviour
    {
        private TMP_Text _text;
        private RectTransform _rect;
        private int _last;

        public static StreakFX Install(TMP_Text text)
        {
            if (text == null) return null;
            var fx = text.GetComponent<StreakFX>();
            if (fx == null) fx = text.gameObject.AddComponent<StreakFX>();
            fx._text = text;
            fx._rect = text.rectTransform;
            return fx;
        }

        public void Apply(int streak)
        {
            if (streak < 2)
            {
                _last = 0;
                return;
            }

            Color tier = streak >= 8 ? UIPalette.Pink : streak >= 5 ? UIPalette.Orange : UIPalette.Gold;
            _text.color = tier;

            if (streak > _last)
            {
                UIMotion.Punch(_rect, 0.35f, 0.4f);
                UIBurst.Sparks(_rect, tier, 8 + Mathf.Min(streak, 10), 420f);
            }
            _last = streak;
        }
    }
}
