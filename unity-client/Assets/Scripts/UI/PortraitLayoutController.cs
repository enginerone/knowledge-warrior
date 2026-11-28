using KnowledgeWarrior.Combat;
using KnowledgeWarrior.Visuals;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeWarrior.UI
{
    /// <summary>
    /// Applies the 1080x1920 Portrait layout to the active camera, side-by-side upper arena fighters,
    /// CanvasScaler, Battle HUD, and 1x4 vertically stacked Answer Deck at runtime so existing scenes
    /// render accurately in Portrait even before regenerating Assets/Scenes/Battle.unity.
    /// </summary>
    public static class PortraitLayoutController
    {
        public const float ArenaGroundY = 1.05f;
        public const float HeroX = -1.45f;
        public const float EnemyX = 1.45f;
        public const float PortraitLungeDistance = 1.65f;
        public const float PortraitOrthoSize = 5.6f;

        public static void ApplyPortraitArenaAndHud(
            BattleUI ui,
            CombatUnit player,
            CombatUnit enemy,
            GameObject backgroundObject)
        {
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.orientation = ScreenOrientation.Portrait;

            var cam = Camera.main;
            if (cam != null && cam.orthographic)
            {
                cam.orthographicSize = PortraitOrthoSize;
                cam.transform.position = new Vector3(0f, 0f, -10f);
            }

            if (backgroundObject != null)
            {
                var fitter = backgroundObject.GetComponent<BackgroundFitter>();
                if (fitter != null)
                {
                    fitter.ConfigureGround(0.42f, ArenaGroundY);
                }
            }

            if (player != null)
            {
                player.SetArenaPlacement(new Vector3(HeroX, ArenaGroundY, 0f), PortraitLungeDistance);
            }
            if (enemy != null)
            {
                enemy.SetArenaPlacement(new Vector3(EnemyX, ArenaGroundY, 0f), PortraitLungeDistance);
            }

            if (ui == null) return;

            var canvas = ui.GetComponent<Canvas>();
            var scaler = ui.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080f, 1920f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            }

            Transform root = canvas != null ? canvas.transform : ui.transform;
            Transform safe = root.Find("SafeArea");
            if (safe != null)
            {
                ApplyTopBarAndFighterHuds(safe);
                ApplyQuestionDeck(safe);
            }

            ApplyBannerAndResult(root);
        }

        private static void ApplyTopBarAndFighterHuds(Transform safe)
        {
            if (safe.Find("StagePill") is RectTransform stagePill)
            {
                Place(stagePill, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-79f, -16f), new Vector2(402f, 54f));
                var stageTxt = stagePill.GetComponentInChildren<TMP_Text>(true);
                if (stageTxt != null)
                {
                    stageTxt.enableAutoSizing = true;
                    stageTxt.fontSizeMin = 14f;
                    stageTxt.fontSizeMax = 20f;
                }
            }

            if (safe.Find("StreakText") is RectTransform streakRt)
            {
                Place(streakRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -232f), new Vector2(460f, 40f));
                var streakTxt = streakRt.GetComponent<TMP_Text>();
                if (streakTxt != null) streakTxt.fontSize = 26f;
            }

            if (safe.Find("PlayerHUD") is RectTransform playerHud)
            {
                Place(playerHud, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(20f, -84f), new Vector2(510f, 138f));

                Transform fill = playerHud.Find("Fill") ?? playerHud;
                if (fill.Find("Name") is RectTransform pName)
                {
                    Place(pName, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                        new Vector2(16f, -10f), new Vector2(326f, 36f));
                    var t = pName.GetComponent<TMP_Text>();
                    if (t != null)
                    {
                        t.enableAutoSizing = true;
                        t.fontSizeMin = 16f;
                        t.fontSizeMax = 25f;
                    }
                }
                if (fill.Find("Level") is RectTransform pLv)
                {
                    Place(pLv, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                        new Vector2(-16f, -10f), new Vector2(148f, 36f));
                    var t = pLv.GetComponent<TMP_Text>();
                    if (t != null)
                    {
                        t.enableAutoSizing = true;
                        t.fontSizeMin = 15f;
                        t.fontSizeMax = 22f;
                    }
                }
                if (fill.Find("HPTrack") is RectTransform hpTrack)
                {
                    hpTrack.anchorMin = new Vector2(0f, 1f);
                    hpTrack.anchorMax = new Vector2(1f, 1f);
                    hpTrack.pivot = new Vector2(0.5f, 1f);
                    hpTrack.offsetMin = new Vector2(16f, -54f - 28f);
                    hpTrack.offsetMax = new Vector2(-16f, -54f);
                }
                if (fill.Find("XPTrack") is RectTransform xpTrack)
                {
                    xpTrack.anchorMin = new Vector2(0f, 1f);
                    xpTrack.anchorMax = new Vector2(1f, 1f);
                    xpTrack.pivot = new Vector2(0.5f, 1f);
                    xpTrack.offsetMin = new Vector2(16f, -98f - 14f);
                    xpTrack.offsetMax = new Vector2(-176f, -98f);
                }
                if (fill.Find("XPText") is RectTransform xpTxt)
                {
                    Place(xpTxt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                        new Vector2(-16f, -90f), new Vector2(154f, 28f));
                    var t = xpTxt.GetComponent<TMP_Text>();
                    if (t != null)
                    {
                        t.enableAutoSizing = true;
                        t.fontSizeMin = 13f;
                        t.fontSizeMax = 17f;
                    }
                }
            }

            EnsureCoinHud(safe);

            if (safe.Find("EnemyHUD") is RectTransform enemyHud)
            {
                Place(enemyHud, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-20f, -84f), new Vector2(510f, 138f));

                Transform fill = enemyHud.Find("Fill") ?? enemyHud;
                if (fill.Find("BossTag") is RectTransform bossTag)
                {
                    Place(bossTag, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                        new Vector2(16f, -12f), new Vector2(92f, 32f));
                }
                if (fill.Find("Name") is RectTransform eName)
                {
                    Place(eName, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                        new Vector2(-16f, -10f), new Vector2(374f, 36f));
                    var t = eName.GetComponent<TMP_Text>();
                    if (t != null)
                    {
                        t.enableAutoSizing = true;
                        t.fontSizeMin = 15f;
                        t.fontSizeMax = 24f;
                    }
                }
                if (fill.Find("HPTrack") is RectTransform eHpTrack)
                {
                    eHpTrack.anchorMin = new Vector2(0f, 1f);
                    eHpTrack.anchorMax = new Vector2(1f, 1f);
                    eHpTrack.pivot = new Vector2(0.5f, 1f);
                    eHpTrack.offsetMin = new Vector2(16f, -54f - 28f);
                    eHpTrack.offsetMax = new Vector2(-16f, -54f);
                }
            }
        }

        private static void ApplyQuestionDeck(Transform safe)
        {
            if (safe.Find("QuestionPanel") is not RectTransform qPanel) return;

            qPanel.anchorMin = new Vector2(0f, 0f);
            qPanel.anchorMax = new Vector2(1f, 0f);
            qPanel.pivot = new Vector2(0.5f, 0f);
            qPanel.offsetMin = new Vector2(20f, 20f);
            qPanel.offsetMax = new Vector2(-20f, 20f + 1040f);

            Transform fill = qPanel.Find("Fill") ?? qPanel;

            if (fill.Find("TurnBadge") is RectTransform badge)
            {
                Place(badge, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(22f, -18f), new Vector2(180f, 46f));
            }

            if (fill.Find("TurnHint") is RectTransform hint)
            {
                Place(hint, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(100f, -18f), new Vector2(-244f, 46f));
                var t = hint.GetComponent<TMP_Text>();
                if (t != null) t.fontSize = 23f;
            }

            if (fill.Find("TopicText") is RectTransform topic)
            {
                Place(topic, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -70f), new Vector2(-44f, 34f));
                var t = topic.GetComponent<TMP_Text>();
                if (t != null)
                {
                    t.alignment = TextAlignmentOptions.MidlineLeft;
                    t.fontSize = 20f;
                }
            }

            if (fill.Find("QuestionText") is RectTransform qText)
            {
                qText.anchorMin = new Vector2(0f, 1f);
                qText.anchorMax = new Vector2(1f, 1f);
                qText.pivot = new Vector2(0.5f, 1f);
                qText.offsetMin = new Vector2(22f, -112f - 236f);
                qText.offsetMax = new Vector2(-22f, -112f);
                var t = qText.GetComponent<TMP_Text>();
                if (t != null)
                {
                    t.enableAutoSizing = true;
                    t.fontSizeMin = 20f;
                    t.fontSizeMax = 34f;
                }
            }

            if (fill.Find("Answers") is RectTransform grid)
            {
                grid.anchorMin = new Vector2(0f, 0f);
                grid.anchorMax = new Vector2(1f, 0f);
                grid.pivot = new Vector2(0.5f, 0f);
                grid.offsetMin = new Vector2(22f, 22f);
                grid.offsetMax = new Vector2(-22f, 22f + 646f);

                const float cardHeight = 148f;
                const float gap = 18f;
                for (int i = 0; i < grid.childCount && i < 4; i++)
                {
                    if (grid.GetChild(i) is not RectTransform ansRt) continue;
                    float topOffset = -i * (cardHeight + gap);
                    ansRt.anchorMin = new Vector2(0f, 1f);
                    ansRt.anchorMax = new Vector2(1f, 1f);
                    ansRt.pivot = new Vector2(0.5f, 1f);
                    ansRt.offsetMin = new Vector2(0f, topOffset - cardHeight);
                    ansRt.offsetMax = new Vector2(0f, topOffset);
                }
            }

            if (fill.Find("Feedback") is RectTransform fb)
            {
                fb.anchorMin = new Vector2(0f, 1f);
                fb.anchorMax = new Vector2(1f, 1f);
                fb.pivot = new Vector2(0.5f, 1f);
                fb.offsetMin = new Vector2(12f, -12f - 344f);
                fb.offsetMax = new Vector2(-12f, -12f);

                if (fb.Find("Title") is RectTransform fbTitle)
                {
                    fbTitle.anchorMin = new Vector2(0f, 1f);
                    fbTitle.anchorMax = new Vector2(1f, 1f);
                    fbTitle.pivot = new Vector2(0.5f, 1f);
                    fbTitle.offsetMin = new Vector2(22f, -16f - 46f);
                    fbTitle.offsetMax = new Vector2(-22f, -16f);
                }

                if (fb.Find("Explanation") is RectTransform fbBody)
                {
                    fbBody.anchorMin = new Vector2(0f, 0f);
                    fbBody.anchorMax = new Vector2(1f, 1f);
                    fbBody.offsetMin = new Vector2(22f, 96f);
                    fbBody.offsetMax = new Vector2(-22f, -68f);
                }

                if (fb.Find("ContinueButton") is RectTransform contBtn)
                {
                    Place(contBtn, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                        new Vector2(0f, 14f), new Vector2(420f, 70f));
                }
            }
        }

        private static void ApplyBannerAndResult(Transform root)
        {
            if (root.Find("Banner") is RectTransform banner)
            {
                banner.anchorMin = new Vector2(0f, 0.5f);
                banner.anchorMax = new Vector2(1f, 0.5f);
                banner.pivot = new Vector2(0.5f, 0.5f);
                banner.anchoredPosition = new Vector2(0f, 380f);
                banner.sizeDelta = new Vector2(0f, 188f);

                if (banner.Find("Title") is RectTransform bTitle && bTitle.GetComponent<TMP_Text>() is TMP_Text bt)
                {
                    bt.fontSize = 62f;
                }
                if (banner.Find("Subtitle") is RectTransform bSub && bSub.GetComponent<TMP_Text>() is TMP_Text bs)
                {
                    bs.fontSize = 32f;
                }
            }

            if (root.Find("Result") is RectTransform result && result.Find("Card") is RectTransform card)
            {
                Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(960f, 680f));
            }
        }

        private static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        public static RectTransform EnsureCoinHud(Transform safe)
        {
            TMP_Text unused = null;
            return EnsureCoinHud(safe, ref unused);
        }

        /// <summary>
        /// Ensures the dedicated CoinHUD container exists beneath PlayerHUD, assigns the coin icon sprite,
        /// and binds <paramref name="coinText"/> if not yet assigned.
        /// </summary>
        public static RectTransform EnsureCoinHud(Transform safe, ref TMP_Text coinText)
        {
            if (safe == null) return null;

            RectTransform coinHud = safe.Find("CoinHUD") as RectTransform;
            if (coinHud == null)
            {
                var hudTemplate = safe.Find("PlayerHUD")?.GetComponent<Image>();
                var xpFont = safe.Find("PlayerHUD/Fill/XPText")?.GetComponent<TMP_Text>()?.font;

                var borderGo = new GameObject("CoinHUD", typeof(RectTransform));
                borderGo.layer = safe.gameObject.layer;
                borderGo.transform.SetParent(safe, false);
                coinHud = (RectTransform)borderGo.transform;

                var borderImg = borderGo.AddComponent<Image>();
                if (hudTemplate != null && hudTemplate.sprite != null)
                {
                    borderImg.sprite = hudTemplate.sprite;
                    borderImg.type = hudTemplate.type;
                }
                borderImg.pixelsPerUnitMultiplier = 1.8f;
                borderImg.color = new Color(1f, 0.82f, 0.30f, 0.48f);
                borderImg.raycastTarget = false;

                var fillGo = new GameObject("Fill", typeof(RectTransform));
                fillGo.layer = safe.gameObject.layer;
                fillGo.transform.SetParent(coinHud, false);
                var fillRt = (RectTransform)fillGo.transform;
                fillRt.anchorMin = Vector2.zero;
                fillRt.anchorMax = Vector2.one;
                fillRt.pivot = new Vector2(0.5f, 0.5f);
                fillRt.offsetMin = new Vector2(2f, 2f);
                fillRt.offsetMax = new Vector2(-2f, -2f);

                var fillImg = fillGo.AddComponent<Image>();
                if (hudTemplate != null && hudTemplate.sprite != null)
                {
                    fillImg.sprite = hudTemplate.sprite;
                    fillImg.type = hudTemplate.type;
                }
                fillImg.pixelsPerUnitMultiplier = 1.8f;
                fillImg.color = new Color(0.04f, 0.06f, 0.16f, 0.90f);
                fillImg.raycastTarget = false;

                var iconGo = new GameObject("CoinIcon", typeof(RectTransform));
                iconGo.layer = safe.gameObject.layer;
                iconGo.transform.SetParent(fillRt, false);
                var iconRt = (RectTransform)iconGo.transform;
                Place(iconRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(12f, 0f), new Vector2(28f, 28f));
                var iconImg = iconGo.AddComponent<Image>();
                iconImg.sprite = UISprites.Coin;
                iconImg.color = Color.white;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;

                var textGo = new GameObject("CoinText", typeof(RectTransform));
                textGo.layer = safe.gameObject.layer;
                textGo.transform.SetParent(fillRt, false);
                var textRt = (RectTransform)textGo.transform;
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.one;
                textRt.pivot = new Vector2(0.5f, 0.5f);
                textRt.offsetMin = new Vector2(48f, 4f);
                textRt.offsetMax = new Vector2(-12f, -4f);

                var tmp = textGo.AddComponent<TextMeshProUGUI>();
                if (xpFont != null) tmp.font = xpFont;
                tmp.text = "0 COINS";
                tmp.color = UIPalette.Gold;
                tmp.fontStyle = FontStyles.Bold;
                tmp.alignment = TextAlignmentOptions.MidlineLeft;
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 14f;
                tmp.fontSizeMax = 20f;
                tmp.raycastTarget = false;
            }

            Place(coinHud, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, -232f), new Vector2(210f, 46f));

            RectTransform fill = (coinHud.Find("Fill") as RectTransform) ?? coinHud;
            if (fill.Find("CoinIcon") is RectTransform iconTransform && iconTransform.GetComponent<Image>() is Image icon)
            {
                Place(iconTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(12f, 0f), new Vector2(28f, 28f));
                icon.sprite = UISprites.Coin;
                icon.color = Color.white;
                icon.preserveAspect = true;
            }

            if (fill.Find("CoinText") is RectTransform txtTransform && txtTransform.GetComponent<TMP_Text>() is TMP_Text txt)
            {
                txt.enableAutoSizing = true;
                txt.fontSizeMin = 14f;
                txt.fontSizeMax = 20f;
                txt.color = UIPalette.Gold;
                txt.alignment = TextAlignmentOptions.MidlineLeft;
                if (coinText == null) coinText = txt;
            }

            return fill;
        }
    }
}
