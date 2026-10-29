using System.Collections.Generic;
using System.Linq;
using KnowledgeWarrior.Combat;
using KnowledgeWarrior.Data;
using KnowledgeWarrior.Progress;
using KnowledgeWarrior.UI;
using KnowledgeWarrior.Visuals;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace KnowledgeWarrior.EditorTools
{
    /// <summary>
    /// Menu: Knowledge Warrior > Build Battle Scene
    /// Generates Assets/Scenes/Battle.unity with camera, background, fighters, managers and the
    /// complete landscape battle UI, with every reference already wired. Safe to re-run
    /// (it overwrites the generated scene, so put custom work in prefabs/other scenes or stop re-running it).
    /// </summary>
    public static class BattleSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Battle.unity";
        private const string QuestionsPath = "Assets/Data/questions.json";
        private const string BackgroundPath = "Assets/Art/Backgrounds/BG_BinaryWoods.jpg";
        private const string HeroPath = "Assets/Art/Sprites/Hero_KnowledgeWarrior.png";
        private const string SlimePath = "Assets/Art/Sprites/Enemy_SyntaxSlime.png";
        private const string ImpPath = "Assets/Art/Sprites/Enemy_GlitchImp.png";
        private const string BossPath = "Assets/Art/Sprites/Boss_NullPointerDragon.png";
        private const string ShadowPath = "Assets/Art/Sprites/Shadow.png";
        private const string RoundedPath = "Assets/Art/UI/UI_RoundedRect.png";
        private const string UnlitMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";

        private const float FeetY = -0.95f;

        // Palette
        private static readonly Color Navy = Hex("0B1026");
        private static readonly Color PanelColor = new(0.04f, 0.06f, 0.16f, 0.86f);
        private static readonly Color PanelSolid = new(0.06f, 0.08f, 0.20f, 0.98f);
        private static readonly Color BorderColor = new(0.30f, 0.88f, 1f, 0.35f);
        private static readonly Color Cyan = Hex("4DE1FF");
        private static readonly Color Violet = Hex("9B6BFF");
        private static readonly Color Gold = Hex("FFD24D");
        private static readonly Color TextMain = Hex("EEF2FF");
        private static readonly Color TextSoft = Hex("BFCBF5");
        private static readonly Color TextMuted = Hex("8C9BD0");
        private static readonly Color DarkText = Hex("0D1230");
        private static readonly Color HpGreen = Hex("3EE6A0");
        private static readonly Color HpRed = Hex("FF5C7A");
        private static readonly Color Ghost = new(1f, 0.92f, 0.75f, 0.9f);
        private static readonly Color Track = new(0f, 0f, 0f, 0.5f);

        private static Sprite _rounded;

        [MenuItem("Knowledge Warrior/Build Battle Scene", priority = 0)]
        public static void BuildBattleScene()
        {
            if (!EnsureTmpEssentials()) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            ApplyMobileLandscapeSettings(false);
            ConfigureArtImporters();

            _rounded = AssetDatabase.LoadAssetAtPath<Sprite>(RoundedPath);
            var questions = AssetDatabase.LoadAssetAtPath<TextAsset>(QuestionsPath);
            if (questions == null)
            {
                EditorUtility.DisplayDialog("Knowledge Warrior", $"Could not find {QuestionsPath}.", "OK");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = BuildCamera();
            BuildBackground(cam);
            var player = BuildFighter("Player", new Vector3(-4.6f, FeetY, 0f), Load(HeroPath), 3.1f, 1.0f);
            var enemy = BuildFighter("Enemy", new Vector3(4.6f, FeetY, 0f), Load(SlimePath), 2.4f, 1.1f);

            var qmGo = new GameObject("QuestionManager");
            var qm = qmGo.AddComponent<QuestionManager>();
            SetRef(qm, "bundledQuestions", questions);

            var smGo = new GameObject("SyncManager");
            var sm = smGo.AddComponent<SyncManager>();
            SetRef(sm, "questionManager", qm);

            var authGo = new GameObject("AuthManager");
            authGo.AddComponent<AuthManager>();

            BuildEventSystem();
            var ui = BuildCanvas(cam);

            var bmGo = new GameObject("BattleManager");
            var bm = bmGo.AddComponent<BattleManager>();
            var so = new SerializedObject(bm);
            so.FindProperty("questionManager").objectReferenceValue = qm;
            so.FindProperty("syncManager").objectReferenceValue = sm;
            so.FindProperty("ui").objectReferenceValue = ui;
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("enemy").objectReferenceValue = enemy;
            so.FindProperty("cameraShake").objectReferenceValue = cam.GetComponent<CameraShake>();
            FillStages(so.FindProperty("stages"));
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            Selection.activeGameObject = bmGo;

            EditorUtility.DisplayDialog("Knowledge Warrior",
                "Battle scene generated at " + ScenePath + ".\n\n" +
                "Tip: set the Game view to 1920x1080 (landscape) and press Play.\n" +
                "Keyboard: 1-4 / A-D to answer, Space to continue.", "Let's fight!");
        }

        [MenuItem("Knowledge Warrior/Apply Mobile Landscape Settings", priority = 20)]
        public static void ApplyMobileLandscapeSettingsMenu() => ApplyMobileLandscapeSettings(true);

        [MenuItem("Knowledge Warrior/Reset Local Progress", priority = 40)]
        public static void ResetProgress()
        {
            if (!EditorUtility.DisplayDialog("Reset progress", "Delete local level/XP/stats, clear saved login session, and start as a brand-new user on the Registration Page?", "Reset", "Cancel")) return;
            ProgressManager.ResetProgress();
            Debug.Log("[Knowledge Warrior] Local progress & auth session reset: " + ProgressManager.SavePath);
        }

        [MenuItem("Knowledge Warrior/Sign Out (Test New User Registration)", priority = 41)]
        public static void SignOutSavedUser()
        {
            PlayerPrefs.DeleteKey(SyncManager.AuthTokenPrefKey);
            PlayerPrefs.DeleteKey(AuthManager.UsernamePrefKey);
            PlayerPrefs.DeleteKey(AuthManager.EmailPrefKey);
            PlayerPrefs.Save();
            Debug.Log("[Knowledge Warrior] Signed out saved user. Next Play will start on the Registration Page.");
        }

        [MenuItem("Knowledge Warrior/Open Save Folder", priority = 42)]
        public static void OpenSaveFolder() => EditorUtility.RevealInFinder(Application.persistentDataPath);

        private static bool EnsureTmpEssentials()
        {
            if (Resources.Load<TMP_Settings>("TMP Settings") != null) return true;

            TMP_PackageResourceImporter.ImportResources(true, false, false);
            EditorUtility.DisplayDialog("Knowledge Warrior",
                "TextMesh Pro Essential Resources were missing and are being imported now.\n\n" +
                "When the import finishes, run 'Knowledge Warrior > Build Battle Scene' again.", "OK");
            return false;
        }

        private static void ApplyMobileLandscapeSettings(bool showDialog)
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            if (PlayerSettings.productName == "unity-client") PlayerSettings.productName = "Knowledge Warrior";
            AssetDatabase.SaveAssets();

            if (showDialog)
                EditorUtility.DisplayDialog("Knowledge Warrior", "Player Settings: orientation set to Landscape Left.", "OK");
        }

        private static void ConfigureArtImporters()
        {
            foreach (var path in new[] { HeroPath, SlimePath, ImpPath, BossPath })
                ConfigureSprite(path, SpriteAlignment.BottomCenter, Vector4.zero);
            ConfigureSprite(ShadowPath, SpriteAlignment.Center, Vector4.zero);
            ConfigureSprite(BackgroundPath, SpriteAlignment.Center, Vector4.zero);
            ConfigureSprite(RoundedPath, SpriteAlignment.Center, new Vector4(48, 48, 48, 48));
        }

        private static void ConfigureSprite(string path, SpriteAlignment alignment, Vector4 border)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                Debug.LogWarning($"[Knowledge Warrior] Missing art: {path}");
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 2048;
            importer.spriteBorder = border;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)alignment;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private static void AddSceneToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void FillStages(SerializedProperty stages)
        {
            var defs = new List<(string name, string sprite, Color tint, float height, int hp, int atk, int xp, bool boss, int diff)>
            {
                ("Syntax Slime", SlimePath, Color.white, 2.4f, 60, 12, 30, false, 1),
                ("Glitch Imp", ImpPath, Color.white, 2.8f, 85, 15, 45, false, 2),
                ("Corrupted Slime", SlimePath, Hex("E58CFF"), 2.9f, 110, 18, 60, false, 2),
                ("Null Pointer Dragon", BossPath, Color.white, 3.9f, 180, 24, 150, true, 3),
            };

            stages.arraySize = defs.Count;
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                var e = stages.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("displayName").stringValue = d.name;
                e.FindPropertyRelative("sprite").objectReferenceValue = Load(d.sprite);
                e.FindPropertyRelative("tint").colorValue = d.tint;
                e.FindPropertyRelative("worldHeight").floatValue = d.height;
                e.FindPropertyRelative("maxHp").intValue = d.hp;
                e.FindPropertyRelative("attack").intValue = d.atk;
                e.FindPropertyRelative("xpReward").intValue = d.xp;
                e.FindPropertyRelative("isBoss").boolValue = d.boss;
                e.FindPropertyRelative("maxDifficulty").intValue = d.diff;
                e.FindPropertyRelative("subject").stringValue = "";
            }
        }

        private static Camera BuildCamera()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.position = new Vector3(0f, 0f, -10f);
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Navy;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 100f;
            go.AddComponent<AudioListener>();
            go.AddComponent<CameraShake>();
            return cam;
        }

        private static void BuildBackground(Camera cam)
        {
            var go = new GameObject("Background");
            go.transform.position = new Vector3(0f, 0f, 10f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Load(BackgroundPath);
            sr.sortingOrder = -100;
            ApplyUnlit(sr);
            var fitter = go.AddComponent<BackgroundFitter>();
            var so = new SerializedObject(fitter);
            so.FindProperty("targetCamera").objectReferenceValue = cam;
            so.FindProperty("groundInImage").floatValue = 0.17f;
            so.FindProperty("groundWorldY").floatValue = FeetY;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static CombatUnit BuildFighter(string name, Vector3 position, Sprite sprite, float worldHeight, float shadowScale)
        {
            var root = new GameObject(name);
            root.transform.position = position;

            var shadow = new GameObject("Shadow");
            shadow.transform.SetParent(root.transform, false);
            shadow.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            shadow.transform.localScale = new Vector3(shadowScale, shadowScale * 0.8f, 1f);
            var ssr = shadow.AddComponent<SpriteRenderer>();
            ssr.sprite = Load(ShadowPath);
            ssr.color = new Color(0f, 0f, 0f, 0.55f);
            ssr.sortingOrder = 5;
            ApplyUnlit(ssr);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 10;
            ApplyUnlit(sr);
            if (sprite != null) visual.transform.localScale = Vector3.one * (worldHeight / sprite.bounds.size.y);

            var unit = root.AddComponent<CombatUnit>();
            SetRef(unit, "spriteRenderer", sr);
            return unit;
        }

        private static void BuildEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>(); // project uses the new Input System only
        }

        private static BattleUI BuildCanvas(Camera cam)
        {
            var canvasGo = new GameObject("BattleCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f; // landscape: keep vertical layout stable on wide phones
            canvasGo.AddComponent<GraphicRaycaster>();
            var ui = canvasGo.AddComponent<BattleUI>();
            var root = canvasGo.transform;

            var safe = Rect("SafeArea", root);
            Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            var refs = new Dictionary<string, Object> { ["worldCamera"] = cam };

            BuildPlayerHud(safe, refs);
            BuildEnemyHud(safe, refs);
            BuildTopBar(safe, refs);
            BuildQuestionPanel(safe, refs);

            var popupLayer = Rect("PopupLayer", root);
            Stretch(popupLayer);
            refs["popupLayer"] = popupLayer;

            BuildBanner(root, refs);
            BuildResult(root, refs);

            var so = new SerializedObject(ui);
            foreach (var kv in refs)
            {
                if (kv.Key.StartsWith("answer")) continue; // assigned to the array below
                var prop = so.FindProperty(kv.Key);
                if (prop == null) { Debug.LogWarning($"BattleUI has no field '{kv.Key}'"); continue; }
                prop.objectReferenceValue = kv.Value;
            }
            var answers = so.FindProperty("answerButtons");
            answers.arraySize = 4;
            for (int i = 0; i < 4; i++)
                answers.GetArrayElementAtIndex(i).objectReferenceValue = refs[$"answer{i}"];
            so.ApplyModifiedPropertiesWithoutUndo();
            return ui;
        }

        private static void BuildPlayerHud(RectTransform parent, Dictionary<string, Object> refs)
        {
            var panel = Panel("PlayerHUD", parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -20), new Vector2(620, 150));

            refs["playerNameText"] = Text("Name", panel, "Knowledge Warrior", 34, TextMain, TextAlignmentOptions.MidlineLeft, true,
                TopLeft(new Vector2(24, -14), new Vector2(420, 44)));
            refs["playerLevelText"] = Text("Level", panel, "LV 1", 30, Gold, TextAlignmentOptions.MidlineRight, true,
                TopRight(new Vector2(-24, -14), new Vector2(160, 44)));

            var (fill, ghost, label) = Bar("HP", panel, 24, 24, -66, 30, HpGreen);
            refs["playerHpFill"] = fill;
            refs["playerHpGhost"] = ghost;
            refs["playerHpText"] = label;

            var (xpFill, _, _) = Bar("XP", panel, 24, 180, -110, 14, Violet, withGhost: false, withLabel: false);
            refs["playerXpFill"] = xpFill;
            refs["playerXpText"] = Text("XPText", panel, "0 / 100 XP", 20, TextSoft, TextAlignmentOptions.MidlineRight, false,
                TopRight(new Vector2(-24, -102), new Vector2(150, 30)));
        }

        private static void BuildEnemyHud(RectTransform parent, Dictionary<string, Object> refs)
        {
            var panel = Panel("EnemyHUD", parent, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -20), new Vector2(620, 116));

            refs["enemyNameText"] = Text("Name", panel, "Syntax Slime", 34, TextMain, TextAlignmentOptions.MidlineRight, true,
                TopRight(new Vector2(-24, -14), new Vector2(440, 44)));

            var tag = Image("BossTag", panel, Hex("FF8A3D"), _rounded);
            Place(tag.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -18), new Vector2(110, 38));
            tag.pixelsPerUnitMultiplier = 3f;
            Text("Label", tag.rectTransform, "BOSS", 24, DarkText, TextAlignmentOptions.Center, true, StretchAll());
            tag.gameObject.SetActive(false);
            refs["bossTag"] = tag.gameObject;

            var (fill, ghost, label) = Bar("HP", panel, 24, 24, -66, 30, HpRed);
            refs["enemyHpFill"] = fill;
            refs["enemyHpGhost"] = ghost;
            refs["enemyHpText"] = label;
        }

        private static void BuildTopBar(RectTransform parent, Dictionary<string, Object> refs)
        {
            var pill = Image("StagePill", parent, PanelColor, _rounded);
            pill.pixelsPerUnitMultiplier = 2f;
            Place(pill.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(560, 56));
            refs["stageText"] = Text("StageText", pill.rectTransform, "Binary Woods  •  Stage 1/4", 26, TextSoft, TextAlignmentOptions.Center, true, StretchAll());

            var streak = Text("StreakText", parent, "", 34, Gold, TextAlignmentOptions.Center, true,
                r => Place(r, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -88), new Vector2(500, 50)));
            streak.outlineWidth = 0.2f;
            streak.outlineColor = new Color32(10, 12, 30, 255);
            refs["streakText"] = streak;
        }

        private static void BuildQuestionPanel(RectTransform parent, Dictionary<string, Object> refs)
        {
            // Border + inner fill gives a subtle glowing glass edge.
            var border = Image("QuestionPanel", parent, BorderColor, _rounded);
            border.pixelsPerUnitMultiplier = 1.4f;
            var panelRt = border.rectTransform;
            panelRt.anchorMin = new Vector2(0, 0);
            panelRt.anchorMax = new Vector2(1, 0);
            panelRt.pivot = new Vector2(0.5f, 0);
            panelRt.offsetMin = new Vector2(24, 20);
            panelRt.offsetMax = new Vector2(-24, 20 + 452);
            var group = border.gameObject.AddComponent<CanvasGroup>();
            refs["questionPanel"] = group;

            var fill = Image("Fill", panelRt, PanelColor, _rounded);
            fill.pixelsPerUnitMultiplier = 1.4f;
            Stretch(fill.rectTransform, 2, 2, 2, 2);
            fill.raycastTarget = true;
            var p = fill.rectTransform;

            // Header row
            var badge = Image("TurnBadge", p, Hex("FF8C4D"), _rounded);
            badge.pixelsPerUnitMultiplier = 2.5f;
            Place(badge.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -22), new Vector2(210, 52));
            refs["turnBadge"] = badge;
            refs["turnText"] = Text("TurnText", badge.rectTransform, "ATTACK", 30, DarkText, TextAlignmentOptions.Center, true, StretchAll());

            refs["turnHintText"] = Text("TurnHint", p, "Answer correctly to strike!", 26, TextSoft, TextAlignmentOptions.MidlineLeft, false,
                TopLeft(new Vector2(254, -22), new Vector2(560, 52)));

            var topic = Text("TopicText", p, "Subject  ·  Topic  ·  Easy", 24, TextMuted, TextAlignmentOptions.MidlineRight, false,
                TopRight(new Vector2(-26, -22), new Vector2(720, 52)));
            topic.richText = false;
            topic.overflowMode = TextOverflowModes.Ellipsis;
            refs["topicText"] = topic;

            // Question
            var q = Text("QuestionText", p, "Question text goes here?", 38, TextMain, TextAlignmentOptions.MidlineLeft, true, r =>
            {
                r.anchorMin = new Vector2(0, 1);
                r.anchorMax = new Vector2(1, 1);
                r.pivot = new Vector2(0.5f, 1);
                r.offsetMin = new Vector2(26, -88 - 116);
                r.offsetMax = new Vector2(-26, -88);
            });
            q.richText = false; // content may contain things like "<a>" (HTML questions)
            q.enableAutoSizing = true;
            q.fontSizeMin = 24;
            q.fontSizeMax = 38;
            refs["questionText"] = q;

            // Answers 2x2
            var grid = Rect("Answers", p);
            grid.anchorMin = new Vector2(0, 0);
            grid.anchorMax = new Vector2(1, 0);
            grid.pivot = new Vector2(0.5f, 0);
            grid.offsetMin = new Vector2(26, 24);
            grid.offsetMax = new Vector2(-26, 24 + 216);
            for (int i = 0; i < 4; i++) refs[$"answer{i}"] = AnswerButtonUI(grid, i);

            // Feedback overlay (covers header + question, keeps the coloured answers visible)
            var fb = Image("Feedback", p, PanelSolid, _rounded);
            fb.pixelsPerUnitMultiplier = 1.6f;
            var fbRt = fb.rectTransform;
            fbRt.anchorMin = new Vector2(0, 1);
            fbRt.anchorMax = new Vector2(1, 1);
            fbRt.pivot = new Vector2(0.5f, 1);
            fbRt.offsetMin = new Vector2(12, -12 - 196);
            fbRt.offsetMax = new Vector2(-12, -12);
            refs["feedbackGroup"] = fb.gameObject.AddComponent<CanvasGroup>();

            refs["feedbackTitle"] = Text("Title", fbRt, "Correct!", 36, HpGreen, TextAlignmentOptions.MidlineLeft, true, r =>
            {
                r.anchorMin = new Vector2(0, 1);
                r.anchorMax = new Vector2(1, 1);
                r.pivot = new Vector2(0.5f, 1);
                r.offsetMin = new Vector2(26, -16 - 50);
                r.offsetMax = new Vector2(-330, -16);
            });

            var body = Text("Explanation", fbRt, "Explanation goes here.", 27, Hex("DCE4FF"), TextAlignmentOptions.TopLeft, false, r =>
            {
                r.anchorMin = new Vector2(0, 0);
                r.anchorMax = new Vector2(1, 1);
                r.offsetMin = new Vector2(26, 16);
                r.offsetMax = new Vector2(-330, -72);
            });
            body.richText = false;
            body.enableAutoSizing = true;
            body.fontSizeMin = 18;
            body.fontSizeMax = 27;
            refs["feedbackBody"] = body;

            refs["continueButton"] = PrimaryButton("ContinueButton", fbRt, "CONTINUE", 30,
                new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-26, 0), new Vector2(280, 96), out _);
        }

        private static AnswerButton AnswerButtonUI(RectTransform grid, int index)
        {
            int col = index % 2, row = index / 2;
            var bg = Image($"Answer{(char)('A' + index)}", grid, new Color(0.11f, 0.14f, 0.32f, 0.96f), _rounded);
            bg.pixelsPerUnitMultiplier = 1.8f;
            var rt = bg.rectTransform;
            rt.anchorMin = new Vector2(col * 0.5f, row == 0 ? 0.5f : 0f);
            rt.anchorMax = new Vector2(col * 0.5f + 0.5f, row == 0 ? 1f : 0.5f);
            rt.offsetMin = new Vector2(col == 0 ? 0 : 8, row == 0 ? 7 : 0);
            rt.offsetMax = new Vector2(col == 0 ? -8 : 0, row == 0 ? 0 : -7);

            var button = bg.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = bg;
            var nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            var cg = bg.gameObject.AddComponent<CanvasGroup>();
            bg.gameObject.AddComponent<ButtonFX>();

            var badge = Image("LetterBadge", rt, Cyan, _rounded);
            badge.pixelsPerUnitMultiplier = 2.2f;
            badge.raycastTarget = false;
            Place(badge.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(66, 66));
            var letter = Text("Letter", badge.rectTransform, ((char)('A' + index)).ToString(), 34, DarkText, TextAlignmentOptions.Center, true, StretchAll());

            var label = Text("Label", rt, "Answer", 30, TextMain, TextAlignmentOptions.MidlineLeft, false, r =>
            {
                Stretch(r);
                r.offsetMin = new Vector2(104, 8);
                r.offsetMax = new Vector2(-20, -8);
            });
            label.richText = false;
            label.enableAutoSizing = true;
            label.fontSizeMin = 18;
            label.fontSizeMax = 30;

            var answer = bg.gameObject.AddComponent<AnswerButton>();
            var so = new SerializedObject(answer);
            so.FindProperty("button").objectReferenceValue = button;
            so.FindProperty("background").objectReferenceValue = bg;
            so.FindProperty("letterBadge").objectReferenceValue = badge;
            so.FindProperty("letterText").objectReferenceValue = letter;
            so.FindProperty("label").objectReferenceValue = label;
            so.FindProperty("canvasGroup").objectReferenceValue = cg;
            so.ApplyModifiedPropertiesWithoutUndo();
            return answer;
        }

        private static void BuildBanner(Transform root, Dictionary<string, Object> refs)
        {
            var strip = Image("Banner", root, new Color(0.02f, 0.03f, 0.10f, 0.78f), null);
            strip.raycastTarget = false;
            var rt = strip.rectTransform;
            rt.anchorMin = new Vector2(0, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0, 150);
            rt.sizeDelta = new Vector2(0, 230);
            refs["bannerGroup"] = strip.gameObject.AddComponent<CanvasGroup>();

            var title = Text("Title", rt, "BOSS BATTLE", 88, Gold, TextAlignmentOptions.Center, true, r =>
            {
                Stretch(r);
                r.offsetMin = new Vector2(0, 70);
            });
            title.characterSpacing = 6f;
            refs["bannerTitle"] = title;
            refs["bannerSubtitle"] = Text("Subtitle", rt, "Enemy Name", 42, TextMain, TextAlignmentOptions.Center, false, r =>
            {
                Stretch(r);
                r.offsetMax = new Vector2(0, -150);
            });
        }

        private static void BuildResult(Transform root, Dictionary<string, Object> refs)
        {
            var dim = Image("Result", root, new Color(0.01f, 0.02f, 0.06f, 0.7f), null);
            Stretch(dim.rectTransform);
            refs["resultGroup"] = dim.gameObject.AddComponent<CanvasGroup>();

            var border = Image("Card", dim.rectTransform, BorderColor, _rounded);
            border.pixelsPerUnitMultiplier = 1.2f;
            Place(border.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960, 620));
            refs["resultCard"] = border.rectTransform;

            var card = Image("Fill", border.rectTransform, PanelSolid, _rounded);
            card.pixelsPerUnitMultiplier = 1.2f;
            Stretch(card.rectTransform, 2, 2, 2, 2);
            var c = card.rectTransform;

            var accent = Image("Accent", c, HpGreen, _rounded);
            accent.pixelsPerUnitMultiplier = 6f;
            Place(accent.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -26), new Vector2(180, 10));
            refs["resultAccent"] = accent;

            var title = Text("Title", c, "VICTORY!", 84, HpGreen, TextAlignmentOptions.Center, true, r =>
            {
                r.anchorMin = new Vector2(0, 1);
                r.anchorMax = new Vector2(1, 1);
                r.pivot = new Vector2(0.5f, 1);
                r.offsetMin = new Vector2(30, -60 - 110);
                r.offsetMax = new Vector2(-30, -60);
            });
            title.characterSpacing = 4f;
            refs["resultTitle"] = title;

            var body = Text("Body", c, "Result details", 34, Hex("DCE4FF"), TextAlignmentOptions.Center, false, r =>
            {
                Stretch(r);
                r.offsetMin = new Vector2(50, 170);
                r.offsetMax = new Vector2(-50, -180);
            });
            body.richText = true; // used for the coloured LEVEL UP line
            body.lineSpacing = 12f;
            body.enableAutoSizing = true;
            body.fontSizeMin = 22;
            body.fontSizeMax = 34;
            refs["resultBody"] = body;

            refs["resultButton"] = PrimaryButton("ResultButton", c, "NEXT BATTLE", 36,
                new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(460, 104), out var label);
            refs["resultButtonLabel"] = label;
        }

        private static Button PrimaryButton(string name, RectTransform parent, string text, float size,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 sizeDelta, out TMP_Text label)
        {
            var img = Image(name, parent, Cyan, _rounded);
            img.pixelsPerUnitMultiplier = 1.6f;
            Place(img.rectTransform, anchorMin, anchorMax, pivot, pos, sizeDelta);
            var button = img.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.85f, 1f, 1f);
            colors.pressedColor = new Color(0.7f, 0.85f, 0.95f);
            colors.selectedColor = Color.white;
            button.colors = colors;
            var nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            img.gameObject.AddComponent<ButtonFX>();
            label = Text("Label", img.rectTransform, text, size, DarkText, TextAlignmentOptions.Center, true, StretchAll());
            label.characterSpacing = 3f;
            return button;
        }

        /// <summary>Rounded panel with a thin glowing border.</summary>
        private static RectTransform Panel(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var border = Image(name, parent, BorderColor, _rounded);
            border.pixelsPerUnitMultiplier = 1.6f;
            Place(border.rectTransform, anchorMin, anchorMax, pivot, pos, size);
            var fill = Image("Fill", border.rectTransform, PanelColor, _rounded);
            fill.pixelsPerUnitMultiplier = 1.6f;
            Stretch(fill.rectTransform, 2, 2, 2, 2);
            return fill.rectTransform;
        }

        /// <summary>Horizontal bar: dark track, optional delayed "ghost" fill, main fill and centered label.</summary>
        private static (RectTransform fill, RectTransform ghost, TMP_Text label) Bar(string name, RectTransform parent,
            float left, float right, float top, float height, Color color, bool withGhost = true, bool withLabel = true)
        {
            var track = Image(name + "Track", parent, Track, _rounded);
            track.pixelsPerUnitMultiplier = 5f;
            var rt = track.rectTransform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(left, top - height);
            rt.offsetMax = new Vector2(-right, top);

            RectTransform ghost = null;
            if (withGhost)
            {
                var g = Image(name + "Ghost", rt, Ghost, _rounded);
                g.pixelsPerUnitMultiplier = 5f;
                g.raycastTarget = false;
                ghost = g.rectTransform;
                Stretch(ghost);
            }

            var f = Image(name + "Fill", rt, color, _rounded);
            f.pixelsPerUnitMultiplier = 5f;
            f.raycastTarget = false;
            Stretch(f.rectTransform);

            TMP_Text label = null;
            if (withLabel)
            {
                label = Text(name + "Text", rt, "100 / 100", Mathf.Max(16, height * 0.72f), Color.white, TextAlignmentOptions.Center, true, StretchAll());
                label.outlineWidth = 0.25f;
                label.outlineColor = new Color32(0, 0, 0, 200);
            }
            return (f.rectTransform, ghost, label);
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image Image(string name, Transform parent, Color color, Sprite sprite)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = UnityEngine.UI.Image.Type.Sliced;
            }
            return img;
        }

        private static TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions alignment, bool bold, System.Action<RectTransform> layout)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = alignment;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            layout?.Invoke(rt);
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

        private static void Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static System.Action<RectTransform> StretchAll() => r => Stretch(r);

        private static System.Action<RectTransform> TopLeft(Vector2 pos, Vector2 size) =>
            r => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), pos, size);

        private static System.Action<RectTransform> TopRight(Vector2 pos, Vector2 size) =>
            r => Place(r, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), pos, size);

        private static Sprite Load(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

        private static void ApplyUnlit(SpriteRenderer sr)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(UnlitMaterialPath);
            if (mat != null) sr.sharedMaterial = mat;
        }

        private static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var c) ? c : Color.magenta;
    }
}
