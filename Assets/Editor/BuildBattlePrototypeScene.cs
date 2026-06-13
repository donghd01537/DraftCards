#if UNITY_EDITOR
using System.IO;
using DraftCards.Core;
using DraftCards.Managers;
using DraftCards.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DraftCards.EditorTools
{
    public static class BuildBattlePrototypeScene
    {
        private const string ScenePath = "Assets/Scenes/BattlePrototype.unity";
        private const string PrefabFolder = "Assets/Prefabs/UI";
        private const string CardPrefabPath = PrefabFolder + "/CardViewPrefab.prefab";
        private const string UnitPrefabPath = PrefabFolder + "/UnitGroupViewPrefab.prefab";
        private const string BattlefieldBackgroundPath = "Assets/Art/Backgrounds/background.png";
        private const string MpPoolSpritePath = "Assets/Art/UI/MPPool_Orb.png";
        private const string FightButtonSpritePath = "Assets/Art/UI/FightButton_Frame.png";
        private const string UnitShadowSpritePath = "Assets/Art/Effects/UnitShadow.png";

        private static readonly Color BackgroundColor = new(0.10f, 0.11f, 0.14f);
        private static readonly Color PlayerLineColor = new(0.18f, 0.24f, 0.36f, 0.65f);
        private static readonly Color EnemyLineColor = new(0.36f, 0.18f, 0.20f, 0.65f);
        private static readonly Color PanelColor = new(0.16f, 0.17f, 0.20f, 0.90f);
        private static readonly Color ButtonColor = new(0.30f, 0.55f, 0.85f);
        private static readonly Vector2 CardViewSize = new(176f, 240f);

        [MenuItem("DraftCards/Build Battle Prototype Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "DraftCards",
                    "Stop Play Mode first (press the Play button at the top to exit), then run this menu again.",
                    "OK");
                return;
            }

            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PrefabFolder);
            EnsureFolder("Assets/Scenes");
            EnsureFolder("Assets/Art/UI");

            GameObject cardPrefab = BuildCardViewPrefab();
            GameObject unitPrefab = BuildUnitGroupViewPrefab();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildCamera();
            BuildEventSystem();

            Canvas canvas = BuildCanvas();

            Image backgroundImage = BuildBackground(canvas.transform);

            Transform playerArea = BuildLineArea(canvas.transform, "PlayerArea", leftAnchor: 0.04f, rightAnchor: 0.26f);
            FormationLineView playerBack = BuildFormationLine(playerArea, "Line_PlayerBack", FormationLine.Back, isPlayerSide: true);
            FormationLineView playerMiddle = BuildFormationLine(playerArea, "Line_PlayerMiddle", FormationLine.Middle, isPlayerSide: true);
            FormationLineView playerFront = BuildFormationLine(playerArea, "Line_PlayerFront", FormationLine.Front, isPlayerSide: true);

            Transform enemyArea = BuildLineArea(canvas.transform, "EnemyArea", leftAnchor: 0.74f, rightAnchor: 0.96f);
            FormationLineView enemyFront = BuildFormationLine(enemyArea, "Line_EnemyFront", FormationLine.Front, isPlayerSide: false);
            FormationLineView enemyMiddle = BuildFormationLine(enemyArea, "Line_EnemyMiddle", FormationLine.Middle, isPlayerSide: false);
            FormationLineView enemyBack = BuildFormationLine(enemyArea, "Line_EnemyBack", FormationLine.Back, isPlayerSide: false);

            RectTransform battleFieldRoot = BuildBattleFieldRoot(canvas.transform);

            TMP_Text mpText = BuildMpText(canvas.transform);
            Button confirmButton = BuildConfirmButton(canvas.transform);

            Transform handContainer = BuildHandContainer(canvas.transform);

            GameObject managers = new("Managers");
            DeckManager deckManager = managers.AddComponent<DeckManager>();
            HandManager handManager = managers.AddComponent<HandManager>();
            MPManager mpManager = managers.AddComponent<MPManager>();
            BattlefieldManager battlefieldManager = managers.AddComponent<BattlefieldManager>();
            UpgradeManager upgradeManager = managers.AddComponent<UpgradeManager>();
            CardPlayManager cardPlayManager = managers.AddComponent<CardPlayManager>();
            GameManager gameManager = managers.AddComponent<GameManager>();
            UIManager uiManager = managers.AddComponent<UIManager>();
            BattlefieldView battlefieldView = managers.AddComponent<BattlefieldView>();

            CardView cardViewComponent = cardPrefab.GetComponent<CardView>();
            UnitGroupView unitViewComponent = unitPrefab.GetComponent<UnitGroupView>();

            WireSerialized(cardPlayManager,
                ("_mpManager", mpManager),
                ("_handManager", handManager),
                ("_deckManager", deckManager),
                ("_battlefieldView", battlefieldView),
                ("_battlefieldManager", battlefieldManager),
                ("_upgradeManager", upgradeManager));
            WireSerialized(gameManager,
                ("_deckManager", deckManager),
                ("_handManager", handManager),
                ("_cardPlayManager", cardPlayManager),
                ("_mpManager", mpManager),
                ("_battlefieldManager", battlefieldManager),
                ("_battlefieldView", battlefieldView));
            WireSerialized(uiManager,
                ("_mpManager", mpManager),
                ("_handManager", handManager),
                ("_cardPlayManager", cardPlayManager),
                ("_gameManager", gameManager),
                ("_deckManager", deckManager),
                ("_battlefieldView", battlefieldView),
                ("_handContainer", handContainer),
                ("_cardViewPrefab", cardViewComponent),
                ("_mpText", mpText),
                ("_endButton", confirmButton));
            WireSerialized(battlefieldView,
                ("_cardPlayManager", cardPlayManager),
                ("_battlefieldManager", battlefieldManager),
                ("_backgroundImage", backgroundImage),
                ("_backgroundSprite", backgroundImage.sprite),
                ("_battleFieldRoot", battleFieldRoot),
                ("_smokeSprite", AssetDatabase.LoadAssetAtPath<Sprite>(UnitShadowSpritePath)));
            WireArray(battlefieldView, "_playerLines", new Object[] { playerBack, playerMiddle, playerFront });
            WireArray(battlefieldView, "_enemyLines", new Object[] { enemyFront, enemyMiddle, enemyBack });
            WireSerialized(battlefieldView, ("_unitViewPrefab", unitViewComponent));

            UnityEventTools.AddPersistentListener(confirmButton.onClick, gameManager.OnConfirmPressed);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };

            Debug.Log("[DraftCards] BattlePrototype scene rebuilt (landscape 16:9) at " + ScenePath +
                      ". If you already created cards, just re-assign Starting Deck on Managers > DeckManager and press Play.");
        }

        // --- Scene parts ---------------------------------------------------

        private static void BuildCamera()
        {
            GameObject cameraGo = new("Main Camera");
            cameraGo.tag = "MainCamera";
            Camera cam = cameraGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BackgroundColor;
            cam.orthographic = true;
            cameraGo.AddComponent<AudioListener>();
        }

        private static void BuildEventSystem()
        {
            GameObject eventSystem = new("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        private static Canvas BuildCanvas()
        {
            GameObject canvasGo = new("Canvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private static RectTransform BuildBattleFieldRoot(Transform parent)
        {
            GameObject go = NewUIObject("BattleField", parent);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            return rect;
        }

        private static Image BuildBackground(Transform parent)
        {
            GameObject bg = NewUIObject("Background", parent);
            Image image = bg.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BattlefieldBackgroundPath);
            image.color = image.sprite == null ? BackgroundColor : Color.white;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = false;
            Stretch(bg);
            return image;
        }

        private static Transform BuildLineArea(Transform parent, string name, float leftAnchor, float rightAnchor)
        {
            GameObject area = NewUIObject(name, parent);
            RectTransform rect = area.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(leftAnchor, 0.42f);
            rect.anchorMax = new Vector2(rightAnchor, 0.73f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            HorizontalLayoutGroup layout = area.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return area.transform;
        }

        private static FormationLineView BuildFormationLine(Transform parent, string name, FormationLine line, bool isPlayerSide)
        {
            GameObject go = NewUIObject(name, parent);
            Image bg = go.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0f);
            bg.raycastTarget = false;

            LayoutElement layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.flexibleWidth = 1f;
            layoutElement.flexibleHeight = 1f;
            layoutElement.minWidth = 0f;

            VerticalLayoutGroup layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 10, 10);
            layout.spacing = 6;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // TODO: remove later — debug label so the Front/Middle/Back lanes are visible during development.
            string label = $"{(isPlayerSide ? "P" : "E")} {line}";
            TMP_Text title = BuildText(go.transform, "Title", label, 28, TextAlignmentOptions.Center, GameFontRole.Title);
            title.rectTransform.sizeDelta = new Vector2(0, 40);
            title.color = new Color(1f, 1f, 1f, 0.6f);
            title.fontStyle = TMPro.FontStyles.Bold;

            GameObject unitContainerGo = NewUIObject("UnitContainer", go.transform);
            VerticalLayoutGroup unitLayout = unitContainerGo.AddComponent<VerticalLayoutGroup>();
            unitLayout.spacing = 6;
            unitLayout.childAlignment = TextAnchor.UpperCenter;
            unitLayout.childForceExpandHeight = false;
            unitLayout.childForceExpandWidth = true;
            unitLayout.childControlWidth = true;
            unitLayout.childControlHeight = false;
            unitContainerGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 500);

            FormationLineView view = go.AddComponent<FormationLineView>();
            WireSerialized(view,
                ("_line", line),
                ("_isPlayerSide", isPlayerSide),
                ("_titleText", title),
                ("_unitContainer", unitContainerGo.transform));
            return view;
        }

        private static void BuildCenterDivider(Transform parent)
        {
            GameObject go = NewUIObject("CenterDivider", parent);
            Image image = go.AddComponent<Image>();
            image.color = new Color(0, 0, 0, 0.35f);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.26f, 0.22f);
            rect.anchorMax = new Vector2(0.74f, 0.96f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            TMP_Text vs = BuildText(go.transform, "VS", "VS", 80, TextAlignmentOptions.Center, GameFontRole.Title);
            Stretch(vs.gameObject);
        }

        private static Transform BuildHandContainer(Transform parent)
        {
            GameObject go = NewUIObject("HandContainer", parent);
            Image image = go.AddComponent<Image>();
            image.color = new Color(0, 0, 0, 0f);
            image.raycastTarget = false;

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.20f, 0.04f);
            rect.anchorMax = new Vector2(0.80f, 0.22f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            // No HorizontalLayoutGroup — UIManager positions cards in a fan manually.
            return go.transform;
        }

        private static PreviewPanel BuildPreviewPanel(Transform parent)
        {
            GameObject panel = NewUIObject("PreviewPanel", parent);
            Image bg = panel.AddComponent<Image>();
            bg.color = PanelColor;

            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.40f, 0.22f);
            rect.anchorMax = new Vector2(0.60f, 0.42f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 4;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            TMP_Text header = BuildText(panel.transform, "Header", "PREVIEW", 28, TextAlignmentOptions.Center, GameFontRole.Title);
            TMP_Text atkText = BuildText(panel.transform, "AttackText", "ATK -", 24, TextAlignmentOptions.MidlineLeft);
            TMP_Text hpText = BuildText(panel.transform, "HpText", "HP -", 24, TextAlignmentOptions.MidlineLeft);
            TMP_Text countText = BuildText(panel.transform, "CountText", "x-", 24, TextAlignmentOptions.MidlineLeft);
            TMP_Text lineText = BuildText(panel.transform, "LineText", "Line", 24, TextAlignmentOptions.MidlineLeft);
            TMP_Text supports = BuildText(panel.transform, "Supports", "", 20, TextAlignmentOptions.TopLeft);

            PreviewPanel view = panel.AddComponent<PreviewPanel>();
            WireSerialized(view,
                ("_root", panel),
                ("_attackText", atkText),
                ("_hpText", hpText),
                ("_countText", countText),
                ("_lineText", lineText),
                ("_appliedSupportsText", supports));
            panel.SetActive(false);
            _ = header;
            return view;
        }

        private static TMP_Text BuildMpText(Transform parent)
        {
            GameObject root = NewUIObject("MPPool", parent);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.025f, 0.045f);
            rootRect.anchorMax = new Vector2(0.125f, 0.225f);
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            Image orb = root.AddComponent<Image>();
            orb.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(MpPoolSpritePath);
            orb.color = orb.sprite == null ? new Color(0.12f, 0.28f, 0.70f) : Color.white;
            orb.preserveAspect = true;
            orb.raycastTarget = false;

            TMP_Text text = BuildText(root.transform, "MPText", "10", 48, TextAlignmentOptions.Center);
            text.fontStyle = FontStyles.Bold;
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.02f, 0.04f);
            rect.anchorMax = new Vector2(0.98f, 0.92f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return text;
        }

        private static Button BuildConfirmButton(Transform parent)
        {
            GameObject go = NewUIObject("EndButton", parent);
            Image image = go.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(FightButtonSpritePath);
            image.color = image.sprite == null ? ButtonColor : Color.white;
            image.preserveAspect = true;
            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.82f, 0.055f);
            rect.anchorMax = new Vector2(0.98f, 0.175f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            TMP_Text label = BuildText(go.transform, "Label", "FIGHT", 38, TextAlignmentOptions.Center, GameFontRole.Title);
            label.color = new Color(0.10f, 0.10f, 0.10f);
            Stretch(label.gameObject);
            return button;
        }

        // --- CardView prefab -----------------------------------------------

        private static GameObject BuildCardViewPrefab()
        {
            GameObject card = new("CardViewPrefab", typeof(RectTransform));
            RectTransform rect = card.GetComponent<RectTransform>();
            rect.sizeDelta = CardViewSize;

            // Artwork fills the entire card. Acts as the Button target.
            Image artImage = card.AddComponent<Image>();
            artImage.color = Color.white;
            artImage.preserveAspect = false;
            artImage.raycastTarget = true;

            Button button = card.AddComponent<Button>();
            button.targetGraphic = artImage;
            ColorBlock cb = button.colors;
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);
            button.colors = cb;

            GameObject selectedBorder = NewUIObject("SelectedBorder", card.transform);
            Image borderImage = selectedBorder.AddComponent<Image>();
            borderImage.color = new Color(1f, 0.9f, 0.2f, 0f);
            Outline outline = selectedBorder.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.9f, 0.2f, 1f);
            outline.effectDistance = new Vector2(4, 4);
            Stretch(selectedBorder);
            selectedBorder.SetActive(false);

            CardView view = card.AddComponent<CardView>();
            WireSerialized(view,
                ("_artwork", artImage),
                ("_selectedBorder", selectedBorder),
                ("_fixedCardSize", CardViewSize),
                ("_scaleArtworkToFixedSize", true),
                ("_selectPulseScale", 1.15f),
                ("_selectPulseDuration", 0.18f),
                ("_selectFlyDuration", 0.32f),
                ("_selectReturnDuration", 0.18f));

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(card, CardPrefabPath);
            Object.DestroyImmediate(card);
            return prefab;
        }

        // --- UnitGroupView prefab ------------------------------------------

        private static GameObject BuildUnitGroupViewPrefab()
        {
            GameObject unit = new("UnitGroupViewPrefab", typeof(RectTransform));
            RectTransform rect = unit.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(120, 160);

            // Ground shadow under the sprite — drawn first so it renders below.
            GameObject shadow = NewUIObject("Shadow", unit.transform);
            Image shadowImage = shadow.AddComponent<Image>();
            shadowImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UnitShadowSpritePath);
            shadowImage.preserveAspect = true;
            shadowImage.raycastTarget = false;
            shadowImage.color = new Color(1f, 1f, 1f, 0.7f);
            RectTransform shadowRect = shadow.GetComponent<RectTransform>();
            shadowRect.anchorMin = new Vector2(0.5f, 0.5f);
            shadowRect.anchorMax = new Vector2(0.5f, 0.5f);
            shadowRect.pivot = new Vector2(0.5f, 0.5f);
            shadowRect.sizeDelta = new Vector2(60, 18);
            shadowRect.anchoredPosition = new Vector2(0f, -20f);

            GameObject container = NewUIObject("SpriteContainer", unit.transform);
            SetAnchors(container, Vector2.zero, Vector2.one);
            // No layout group: UnitGroupView centers each sprite slot on the container
            // (anchoredPosition zero, pivot 0.5). A VerticalLayoutGroup positioned children
            // by their UNSCALED rect size, which pushed scaled-down sprites (e.g. the tall
            // Cyclop) off-center vertically and fought the MoveBounceAnimator's localPosition.

            MoveBounceAnimator bounce = container.AddComponent<MoveBounceAnimator>();
            WireSerialized(bounce, ("_target", container.transform));

            UnitGroupView view = unit.AddComponent<UnitGroupView>();
            WireSerialized(view,
                ("_spriteContainer", container.GetComponent<RectTransform>()),
                ("_shadow", shadowRect));

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(unit, UnitPrefabPath);
            Object.DestroyImmediate(unit);
            return prefab;
        }

        // --- Helpers --------------------------------------------------------

        private static GameObject NewUIObject(string name, Transform parent)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(GameObject go)
        {
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetAnchors(GameObject go, Vector2 min, Vector2 max)
        {
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static TMP_Text BuildText(
            Transform parent,
            string name,
            string text,
            float size,
            TextAlignmentOptions align,
            GameFontRole fontRole = GameFontRole.Normal)
        {
            GameObject go = NewUIObject(name, parent);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.color = Color.white;
            GameFonts.Apply(tmp, fontRole);
            return tmp;
        }

        private static void WireArray(Object target, string fieldName, Object[] values)
        {
            SerializedObject so = new(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null || !prop.isArray)
            {
                Debug.LogWarning($"[DraftCards] Could not find array field '{fieldName}' on {target.GetType().Name}");
                return;
            }
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireSerialized(Object target, params (string field, object value)[] assignments)
        {
            SerializedObject so = new(target);
            foreach ((string field, object value) in assignments)
            {
                SerializedProperty prop = so.FindProperty(field);
                if (prop == null)
                {
                    Debug.LogWarning($"[DraftCards] Could not find field '{field}' on {target.GetType().Name}");
                    continue;
                }
                AssignToProperty(prop, value);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignToProperty(SerializedProperty prop, object value)
        {
            switch (value)
            {
                case bool b:
                    prop.boolValue = b;
                    break;
                case int i:
                    prop.intValue = i;
                    break;
                case float f:
                    prop.floatValue = f;
                    break;
                case Vector2 v2:
                    prop.vector2Value = v2;
                    break;
                case System.Enum e:
                    prop.intValue = System.Convert.ToInt32(e);
                    break;
                case Object unityObj:
                    prop.objectReferenceValue = unityObj;
                    break;
                case null:
                    prop.objectReferenceValue = null;
                    break;
                default:
                    Debug.LogWarning($"[DraftCards] Unsupported field value type {value.GetType()}");
                    break;
            }
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string leaf = Path.GetFileName(assetPath);

            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
