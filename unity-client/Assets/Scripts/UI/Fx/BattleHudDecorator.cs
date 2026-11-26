using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>Scene objects of the battle canvas that receive decoration.</summary>
    public class BattleHudRefs
    {
        public RectTransform canvasRoot;
        public RectTransform playerHpFill;
        public RectTransform playerXpFill;
        public RectTransform enemyHpFill;
        public CanvasGroup questionPanel;
        public CanvasGroup feedbackGroup;
        public CanvasGroup bannerGroup;
        public RectTransform resultCard;
        public Image turnBadge;
        public TMP_Text streakText;
        public Button continueButton;
        public Button resultButton;
    }

    /// <summary>Effects the battle UI triggers while playing.</summary>
    public class BattleHudHandles
    {
        public StreakFX streak;
        public ShineSweep bannerShine;
        public RectTransform playerHud;
        public RectTransform enemyHud;
        public FxPanel resultPanel;
    }

    /// <summary>
    /// Dresses the battle canvas at runtime so scenes generated before the redesign get the same
    /// ornamental frames, glossy bars and ambient motes without being rebuilt.
    /// </summary>
    public static class BattleHudDecorator
    {
        public static BattleHudHandles Dress(BattleHudRefs refs)
        {
            var handles = new BattleHudHandles();
            var root = refs.canvasRoot;

            AmbientParticles.Create(root, 14, UIPalette.Ambient, true, 6f, 18f);

            DressFighterHud(refs.playerHpFill, UIPalette.Cyan, out handles.playerHud);
            DressFighterHud(refs.enemyHpFill, UIPalette.Red, out handles.enemyHud);

            HudBarFX.Install(refs.playerHpFill, true);
            HudBarFX.Install(refs.enemyHpFill, true);
            HudBarFX.Install(refs.playerXpFill, false);

            if (root.Find("SafeArea/StagePill") is RectTransform stagePill)
                UIDecorator.DecoratePanel(stagePill, UIPalette.Gold, 0f, 0.10f, false);

            if (refs.questionPanel != null && refs.questionPanel.transform.Find("Fill") is RectTransform questionFill)
                UIDecorator.DecoratePanel(questionFill, UIPalette.Cyan, 44f, 0.06f);

            if (refs.turnBadge != null)
            {
                UIDecorator.AddGloss(refs.turnBadge.rectTransform, 0.28f, 0.2f, 0.55f);
                UIDecorator.AddShine(refs.turnBadge.rectTransform, 0.25f, true, 3f, 6f);
            }

            if (refs.feedbackGroup != null)
                UIDecorator.DecoratePanel((RectTransform)refs.feedbackGroup.transform, UIPalette.Cyan, 0f, 0.08f, false);

            if (refs.bannerGroup != null)
            {
                var banner = (RectTransform)refs.bannerGroup.transform;
                UIDecorator.AddGloss(banner, 0.14f, 0f, 0.6f);
                UIDecorator.AddRing(banner, UIPalette.WithAlpha(UIPalette.Gold, 0.6f));
                handles.bannerShine = UIDecorator.AddShine(banner, 0.22f, false, 4f, 8f);
            }

            if (refs.resultCard != null && refs.resultCard.Find("Fill") is RectTransform resultFill)
                handles.resultPanel = UIDecorator.DecoratePanel(resultFill, UIPalette.Gold, 52f, 0.10f);

            if (refs.continueButton != null) refs.continueButton.GetComponent<ButtonFX>()?.EnableIdleShine();
            if (refs.resultButton != null) refs.resultButton.GetComponent<ButtonFX>()?.EnableIdleShine();

            handles.streak = StreakFX.Install(refs.streakText);
            return handles;
        }

        /// <summary>A fighter HUD panel is the grandparent of its HP fill (fill, track, panel fill).</summary>
        private static void DressFighterHud(RectTransform hpFill, Color accent, out RectTransform hudBorder)
        {
            hudBorder = null;
            if (hpFill == null || hpFill.parent == null) return;
            var panelFill = hpFill.parent.parent as RectTransform;
            if (panelFill == null) return;

            UIDecorator.DecoratePanel(panelFill, accent, 22f, 0.12f);
            hudBorder = panelFill.parent as RectTransform;
        }
    }
}
