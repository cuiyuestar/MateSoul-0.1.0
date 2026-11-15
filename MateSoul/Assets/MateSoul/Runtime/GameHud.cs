using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using MateSoul.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace MateSoul.Runtime
{
    public sealed class GameHud : MonoBehaviour
    {
        private enum HudPage
        {
            None,
            Dialogue,
            Interaction,
            MiniGames,
            PickupItems,
            EatFood,
            Information,
            Items,
            Settings,
            AIConfiguration,
            AITutorial,
            FirstRun
        }

        private static readonly Color DockColor = new Color(.22f, .14f, .22f, .52f);
        private static readonly Color PanelColor = new Color(.14f, .10f, .16f, .92f);
        private static readonly Color ButtonColor = new Color(.82f, .66f, .80f, .86f);
        private static readonly Color SelectedColor = new Color(.86f, .72f, .84f, .94f);
        private static readonly Color TextColor = new Color(.98f, .95f, .98f);
        private static readonly Color MutedTextColor = new Color(.77f, .72f, .79f);
        private static readonly Color CardColor = new Color(.25f, .18f, .28f, .88f);
        private static readonly Color CardHoverColor = new Color(.39f, .28f, .41f, .96f);
        private static readonly Color PrimaryTextColor = new Color(.24f, .15f, .27f);
        private static readonly Color QuietColor = new Color(.32f, .24f, .35f, .64f);
        private static readonly Color DangerColor = new Color(.46f, .25f, .36f, .84f);
        private static readonly Color DialogueGlassColor = new Color(.72f, .63f, .86f, .68f);
        private static readonly Color DialogueHazeColor = new Color(.98f, .95f, 1f, .14f);
        private static readonly Color DialogueInputColor = new Color(.27f, .20f, .34f, .42f);
        private static readonly Color DialogueSendColor = new Color(.59f, .42f, .77f, .92f);
        private static readonly Color DialogueCloseColor = new Color(.34f, .28f, .40f, .82f);
        private static readonly Color VoiceIdleColor = new Color(.47f, .34f, .58f, .92f);
        private static readonly Color VoiceActiveColor = new Color(.80f, .32f, .40f, .96f);
        private static readonly ItemCategory[] CatalogCategories =
        {
            ItemCategory.Furniture,
            ItemCategory.Appliance,
            ItemCategory.Tableware,
            ItemCategory.Kitchenware,
            ItemCategory.Food,
            ItemCategory.Toy
        };
        private const int CatalogPageSize = 6;
        private const float HandToggleHoldSeconds = .5f;
        private const float HudDistance = .45f;
        private const float HudVerticalOffset = -.20f;
        private const float HudExpandedNavigationDrop = .02f;
        private const float HudDownwardAngle = 30f;
        private const float HudPitchFollowFraction = .25f;
        private const float HudPitchFollowLimit = 8f;
        // Subtitle gap: closed height, panel rests here, and how far it may rise.
        private const float SubtitleBaseY = 162f;
        private const float SubtitleBaseHeight = 38f;
        private const float SubtitleWidth = 760f;
        private const float ContentBaseY = 200f;
        private const float SubtitleMaxExtra = 150f;
        private const float HudYawDeadZone = 30f;
        private const float HudTranslationDeadZone = .2f;

        [SerializeField] private GameBootstrap bootstrap;
        private readonly Dictionary<HudPage, GameObject> pages = new Dictionary<HudPage, GameObject>();
        private readonly Dictionary<HudPage, Image> navigationBackgrounds = new Dictionary<HudPage, Image>();
        private readonly Dictionary<string, TMP_Text> personalityLabels = new Dictionary<string, TMP_Text>();
        private readonly List<Sprite> iconSprites = new List<Sprite>();
        private readonly List<Texture2D> iconTextures = new List<Texture2D>();
        private readonly List<Button> catalogButtons = new List<Button>();
        private readonly List<TMP_Text> catalogQuantityLabels = new List<TMP_Text>();
        private readonly List<HudPanelButton> pickupButtons = new List<HudPanelButton>();
        private ItemDefinition[] pickupDefinitions = Array.Empty<ItemDefinition>();
        private TMP_Text pickupPageLabel, pickupFeedback;
        private Button pickupReleaseButton;
        private ActionHandle pickupAction;
        private int pickupPage;
        private readonly List<HudPanelButton> eatButtons = new List<HudPanelButton>();
        private ItemDefinition[] eatDefinitions = Array.Empty<ItemDefinition>();
        private TMP_Text eatPageLabel, eatFeedback;
        private Button eatReleaseButton;
        private ActionHandle eatAction;
        private int eatPage;
        private readonly List<HudPanelButton> catalogCategoryButtons = new List<HudPanelButton>();
        private readonly List<IUIInteractor> hudPointers = new List<IUIInteractor>();
        private ItemDefinition[] catalog = Array.Empty<ItemDefinition>();
        private ItemDefinition[] categoryCatalog = Array.Empty<ItemDefinition>();
        private TMP_Text status, subtitle, selectedItem, interactionFeedback, catalogPageLabel;
        private TMP_Text autonomyLabel, pauseLabel, inputModeLabel;
        private TMP_Text hudVisibilityHint;
        private TMP_InputField input;
        private TMP_InputField aiAddress, aiModel, aiKey;
        private Button micButton;
        private TMP_Text micLabel, dialoguePlaceholder;
        private SpeechToTextRecorder voiceInput;
        // PICO 全屏应用一旦拉起系统输入法，整个 XR 容器会被切到后台并退回桌面，
        // 因此 HUD 输入框只使用内置键盘，不再打开系统软键盘。
        private RectTransform keyboardRoot;
        private TMP_InputField keyboardTarget;
        private bool keyboardShift;
        private const float KeyboardDialogueLift = 258f;
        private const float KeyboardKeyHeight = 42f;
        private const float KeyboardRowGap = 6f;
        private const float KeyboardRowTop = 56f;
        private const int KeyboardCandidateCount = 6;
        private bool keyboardChinese;
        private string pinyinBuffer = string.Empty;
        private TMP_Text keyboardComposition, keyboardModeLabel;
        private Button keyboardModeButton;
        private readonly List<(HudPanelButton button, TMP_Text label)> keyboardCandidateSlots =
            new List<(HudPanelButton, TMP_Text)>();
        private readonly List<string> keyboardCandidateValues = new List<string>(KeyboardCandidateCount);
        private readonly List<(TMP_Text label, string value)> keyboardLetterKeys =
            new List<(TMP_Text label, string value)>();
        private TMP_Text aiConfigurationStatus;
        private RectTransform aiRequiredHint;
        private Button aiSaveButton, aiCheckButton;
        private bool aiReturnToDialogue, aiCheckRunning;
        private int placementVisibilityFrame = -1;
        private readonly CancellationTokenSource hudLifetime = new CancellationTokenSource();
        private Button sendButton, recycleButton;
        private Button meshOcclusionToggle, environmentOcclusionToggle;
        private Button coverageGateToggle, meshVisualizationToggle;
        private Button environmentPresetButton;
        private TMP_Text resetFirstLaunchLabel;
        private bool resetFirstLaunchArmed;
        private Canvas canvas;
        private TMP_FontAsset font;
        private Material overlayMaterial;
        private VirtualItemFocusController focusController;
        private ItemPlacementPreviewController placementPreview;
        private Sprite roundedSprite;
        private Texture2D roundedTexture;
        private Sprite panelSprite;
        private Texture2D panelTexture;
        private RectTransform canvasRoot, hudRoot, contentRoot;
        private bool hudPoseInitialized, hudInteractionActive;
        private Vector3 hudFollowOrigin, hudTargetPosition;
        private Quaternion hudFollowRotation, hudTargetRotation;
        private RectTransform subtitleRect;
        private float subtitleGap, subtitleGapTarget;
        private RectTransform navigationRoot, dialogueRoot;
        private RectTransform personalitySettingsRoot, systemSettingsRoot;
        private CanvasGroup navigationGroup, dialogueGroup;
        private CanvasGroup contentGroup;
        private HudPanelButton personalityTab, systemTab;
        private HudPage activePage;
        private PlayerInteractionModeController interactionMode;
        private InputAction controllerHudToggle;
        private int selected, catalogPage;
        private ItemCategory selectedCategory = ItemCategory.Furniture;
        private bool hudVisible = true;
        private bool visibilityInputModeKnown, controllerToggleReady, handToggleReady;
        private bool handToggleLatched;
        private bool dialogueOpen;
        private PlayerInteractionMode visibilityInputMode;
        private float handToggleStarted = -1;
        private float nextRefresh;
        private Coroutine dialogueTransition;
        private Coroutine panelTransition;
        private string localMessage;
        private string message
        {
            get => localMessage;
            set
            {
                localMessage = value;
                if (!string.IsNullOrEmpty(value)) bootstrap.Notifications.ShowHint(value);
            }
        }
        private ActionHandle interactionAction;

        #region debug-point A-E:ui-quality-runtime-evidence
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        private bool uiQualityReported;
        private bool windowGlyphCoverageReported;
#endif
        #endregion

        public bool IsVisible => hudVisible && (placementPreview == null || !placementPreview.IsActive);
        public bool IsDialogueOpen => dialogueOpen;

        // Native keyboard gestures must not also click/deselect the world-space HUD.
        // TMP keeps isFocused until it has consumed the keyboard's final text/status.
        // The in-app HUD keyboard keeps isFocused without any soft keyboard, so the
        // suppression only applies while a real system keyboard is visible.
        public static bool IsTextInputFocused
        {
            get
            {
                var selectedObject = EventSystem.current?.currentSelectedGameObject;
                return selectedObject != null &&
                       selectedObject.TryGetComponent<TMP_InputField>(out var field) &&
                       field.isFocused && TouchScreenKeyboard.visible;
            }
        }

#region debug-point D:eyehand-pointer-stall
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private static bool debugLocalInputTrace;
        private XRUIInputModule debugUiModule;
        private float debugNextUiTrace;

        internal static string InputTraceString(string value)
            => "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";

        internal static void ReportInputTrace(string payload)
        {
            if (debugLocalInputTrace)
            {
                Debug.Log("MATESOUL_INPUT_TRACE " + payload);
                return;
            }
            var request = UnityWebRequest.Post("http://127.0.0.1:7789/event", payload, "application/json");
            request.timeout = 2;
            var operation = request.SendWebRequest();
            operation.completed += _ =>
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    debugLocalInputTrace = true;
                    Debug.Log("MATESOUL_INPUT_TRACE " + payload);
                }
                request.Dispose();
            };
        }

        private void TraceInputDown(GameObject target, PointerEventData data)
            => TraceInputEvent("down", target, data);
        private void TraceInputUp(GameObject target, PointerEventData data)
            => TraceInputEvent("up", target, data);
        private void TraceInputClick(GameObject target, PointerEventData data)
            => TraceInputEvent("click", target, data);

        private void TraceInputEvent(string stage, GameObject target, PointerEventData data)
        {
            if (target != null && !target.transform.IsChildOf(canvasRoot)) return;
            var source = debugUiModule.GetInteractor(data.pointerId);
            ReportInputTrace($"{{\"sessionId\":\"eyehand-pointer-stall\",\"runId\":\"post-fix-2\"," +
                $"\"hypothesisId\":\"D\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                $"\"location\":\"GameHud.UIEvent\",\"msg\":\"[DEBUG] {stage}\",\"data\":{{" +
                $"\"frame\":{Time.frameCount},\"pointer\":{data.pointerId}," +
                $"\"source\":{InputTraceString(source?.GetType().Name)}," +
                $"\"target\":{InputTraceString(target == null ? "" : target.name)}," +
                $"\"targetId\":{(target == null ? 0 : target.GetInstanceID())}," +
                $"\"press\":{InputTraceString(data.pointerPress == null ? "" : data.pointerPress.name)}," +
                $"\"eligible\":{(data.eligibleForClick ? 1 : 0)},\"page\":{InputTraceString(activePage.ToString())}," +
                $"\"transition\":{(dialogueTransition != null ? 1 : 0)}}}}}");
        }
#endif
#endregion

        public void Configure(GameBootstrap game) { bootstrap = game; }

        private void Start()
        {
            StartupTimeline.Mark("hud_start");
            focusController = bootstrap == null ? null : bootstrap.GetComponent<VirtualItemFocusController>();
            placementPreview = bootstrap == null
                ? null : bootstrap.GetComponent<ItemPlacementPreviewController>();
            if (placementPreview != null)
            {
                placementPreview.ConfirmRequested += OnHandPlacementConfirmed;
                placementPreview.ActiveChanged += OnPlacementActiveChanged;
            }
            interactionMode = FindObjectOfType<PlayerInteractionModeController>();
            hudPointers.AddRange(FindObjectsOfType<MonoBehaviour>(true).OfType<IUIInteractor>());
            CreateHudToggleActions();
            StartupTimeline.Mark("hud_font_load_start");
            font = AlwaysOnTopUi.TextFont;
            StartupTimeline.Mark("hud_font_load_end");
            roundedSprite = CreateRoundedSprite();
            panelSprite = CreatePanelSprite();
            overlayMaterial = AlwaysOnTopUi.CreateMaterial();
            var root = new GameObject("MateSoul HUD", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            canvas = root.AddComponent<Canvas>();
            canvasRoot = root.GetComponent<RectTransform>();
            var scaler = root.AddComponent<CanvasScaler>();
            AlwaysOnTopUi.ConfigureScaler(scaler);
            AlwaysOnTopUi.ConfigureRaycasters(root);

            if (Application.isEditor)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = .5f;
                hudRoot = AnchoredRect("Bottom HUD", root.transform, new Vector2(.5f, 0),
                    new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 20), new Vector2(920, 760));
            }
            else
            {
                canvas.renderMode = RenderMode.WorldSpace;
                canvasRoot.sizeDelta = new Vector2(920, 760);
                canvasRoot.localScale = Vector3.one * .00066f;
                hudRoot = StretchRect("Bottom HUD", root.transform);
                Recenter(true);
            }
            AlwaysOnTopUi.Configure(canvas);

            Debug.Log($"MATESOUL_UI worldSpace={canvas.renderMode == RenderMode.WorldSpace} " +
                      $"distance={HudDistance:F2} sortingOrder={canvas.sortingOrder} " +
                      $"shader={overlayMaterial.shader.name}");

            BuildNavigation();
            BuildDialogueBar();
            BuildSubtitle();
            BuildPages();
            BuildKeyboard();
            var hudLayer = LayerMask.NameToLayer(HudEyeHandInteractor.HudLayerName);
            if (hudLayer < 0) throw new InvalidOperationException("MateSoulHUD layer is missing.");
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = hudLayer;
            AlwaysOnTopUi.Apply(root.transform, overlayMaterial);
            SetActivePage(HudPage.None);
            SetHudVisible(true);
            if (bootstrap != null && bootstrap.IsFirstLaunchPending)
                StartCoroutine(ShowFirstLaunchPanel());
            if (!Application.isEditor)
            {
                Canvas.ForceUpdateCanvases();
                Recenter(true);
            }

            if (EventSystem.current == null)
            {
                var events = new GameObject("UI Event System", typeof(EventSystem), typeof(XRUIInputModule));
                events.transform.SetParent(transform, false);
            }
            var module = EventSystem.current.GetComponent<XRUIInputModule>();
            if (module == null) throw new InvalidOperationException("XR UI input module is missing.");
            if (Application.platform == RuntimePlatform.Android)
            {
                // PICO HUD input comes from tracked hands. Android keyboard touch/mouse
                // events belong to the system keyboard, not to the Unity canvas.
                module.enableMouseInput = false;
                module.enableTouchInput = false;
            }
            if (interactionMode != null)
            {
#region debug-point D:eyehand-pointer-stall
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                debugUiModule = module;
                module.pointerDown += TraceInputDown;
                module.pointerUp += TraceInputUp;
                module.pointerClick += TraceInputClick;
#endif
#endregion
                // Process contact first so gaze arbitration uses this frame's actual UI hit.
                if (interactionMode.Hands != null)
                    foreach (var hand in interactionMode.Hands) hand.Poke.Bind(module);
                var eyeHand = gameObject.AddComponent<HudEyeHandInteractor>();
                eyeHand.Configure(this, canvasRoot, interactionMode, module);
                hudPointers.Add(eyeHand);
            }
            StartupTimeline.Mark("hud_ready");
        }

        private void BuildNavigation()
        {
            var dock = AnchoredRect("Bottom Navigation", hudRoot, new Vector2(.5f, 0),
                new Vector2(.5f, 0), new Vector2(.5f, 0), Vector2.zero, new Vector2(824, 160));
            navigationRoot = dock;
            navigationGroup = dock.gameObject.AddComponent<CanvasGroup>();
            AddBackground(dock, DockColor);
            var labels = new[]
            {
                ("对话", HudPage.Dialogue),
                ("互动", HudPage.Interaction),
                ("信息", HudPage.Information),
                ("虚拟物品", HudPage.Items),
                ("设置", HudPage.Settings)
            };
            const float gap = 18;
            const float width = 145.6f;
            for (var i = 0; i < labels.Length; i++)
            {
                var entry = labels[i];
                var button = NavigationButton(entry.Item1, entry.Item2, 12 + i * (width + gap),
                    width, dock, () => TogglePage(entry.Item2));
                navigationBackgrounds.Add(entry.Item2, button.GetComponent<Image>());
            }
            hudVisibilityHint = Label(VisibilityHint(), 12, 137, 800, 18, 14, dock,
                TextAnchor.MiddleCenter, MutedTextColor, "展示隐藏UI提示");
            BuildOcclusionToggles(dock);
        }

        private void BuildOcclusionToggles(RectTransform dock)
        {
            var panel = AnchoredRect("Occlusion Toggles", dock, new Vector2(1f, 0),
                new Vector2(1f, 0), new Vector2(0f, 0), new Vector2(6, 0),
                new Vector2(120, 200));
            AddBackground(panel, DockColor);
            meshOcclusionToggle = Button("", 6, 6, 108, 34,
                ToggleMeshOcclusion, panel, 13, "实时网格遮挡");
            environmentOcclusionToggle = Button("", 6, 45, 108, 34,
                ToggleEnvironmentOcclusion, panel, 13, "环境深度遮挡");
            coverageGateToggle = Button("", 6, 84, 108, 34,
                ToggleCoverageGate, panel, 13, "覆盖度门控");
            meshVisualizationToggle = Button("", 6, 123, 108, 34,
                ToggleMeshVisualization, panel, 13, "网格可视化");
            environmentPresetButton = Button("", 6, 162, 108, 34,
                ApplyEnvironmentOnlyPreset, panel, 13,
                "一键实验：关网格遮挡与覆盖门控，只保留环境深度遮挡");
            RefreshOcclusionToggles();
        }

        private void ToggleMeshOcclusion()
        {
            RealityOcclusionMeshRenderer.SetMeshOcclusion(
                !RealityOcclusionMeshRenderer.MeshOcclusionAllowed);
            RefreshOcclusionToggles();
        }

        private void ToggleEnvironmentOcclusion()
        {
            RealityOcclusionMeshRenderer.SetEnvironmentOcclusion(
                !RealityOcclusionMeshRenderer.EnvironmentOcclusionAllowed);
            RefreshOcclusionToggles();
        }

        private void ToggleCoverageGate()
        {
            RealityOcclusionMeshRenderer.SetCoverageGate(
                !RealityOcclusionMeshRenderer.CoverageGateAllowed);
            RefreshOcclusionToggles();
        }

        private void ToggleMeshVisualization()
        {
            RealityOcclusionMeshRenderer.SetMeshVisualization(
                !RealityOcclusionMeshRenderer.MeshVisualizationAllowed);
            RefreshOcclusionToggles();
        }

        private void ApplyEnvironmentOnlyPreset()
        {
            RealityOcclusionMeshRenderer.SetEnvironmentOnlyPreset();
            RefreshOcclusionToggles();
        }

        private void RefreshOcclusionToggles()
        {
            RefreshOcclusionToggle(meshOcclusionToggle, "网格遮挡",
                RealityOcclusionMeshRenderer.MeshOcclusionAllowed);
            RefreshOcclusionToggle(environmentOcclusionToggle, "环境遮挡",
                RealityOcclusionMeshRenderer.EnvironmentOcclusionAllowed);
            RefreshOcclusionToggle(coverageGateToggle, "覆盖门控",
                RealityOcclusionMeshRenderer.CoverageGateAllowed);
            RefreshOcclusionToggle(meshVisualizationToggle, "网格可视化",
                RealityOcclusionMeshRenderer.MeshVisualizationAllowed);
            RefreshOcclusionToggle(environmentPresetButton, "纯环境实验",
                RealityOcclusionMeshRenderer.EnvironmentOcclusionAllowed &&
                !RealityOcclusionMeshRenderer.MeshOcclusionAllowed &&
                !RealityOcclusionMeshRenderer.CoverageGateAllowed);
        }

        private static void RefreshOcclusionToggle(Button button, string title, bool enabled)
        {
            if (button == null) return;
            button.GetComponent<Image>().color = enabled
                ? ButtonColor
                : new Color(.38f, .34f, .40f, .82f);
            var label = button.transform.Find("Label").GetComponent<TMP_Text>();
            label.text = $"{title} {(enabled ? "开" : "关")}";
            label.color = enabled ? TextColor : MutedTextColor;
        }

        private void BuildDialogueBar()
        {
            dialogueRoot = AnchoredRect("Dialogue Bar", hudRoot, new Vector2(.5f, 0),
                new Vector2(.5f, 0), new Vector2(.5f, 0), Vector2.zero, new Vector2(824, 112));
            dialogueGroup = dialogueRoot.gameObject.AddComponent<CanvasGroup>();
            AddBackground(dialogueRoot, DialogueGlassColor);
            var shadow = dialogueRoot.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(.12f, .07f, .16f, .32f);
            shadow.effectDistance = new Vector2(0, -7);
            shadow.useGraphicAlpha = true;

            var haze = Rect("Glass Haze", 5, 5, 814, 44, dialogueRoot);
            var hazeImage = AddBackground(haze, DialogueHazeColor);
            hazeImage.raycastTarget = false;

            micButton = Button("语音", 18, 20, 64, 72, ToggleVoiceInput,
                dialogueRoot, 19, "语音输入");
            micButton.GetComponent<Image>().color = VoiceIdleColor;
            micLabel = micButton.GetComponentInChildren<TMP_Text>();

            var inputRoot = Rect("对话输入", 90, 20, 496, 72, dialogueRoot);
            AddBackground(inputRoot, DialogueInputColor);
            input = inputRoot.gameObject.AddComponent<TMP_InputField>();
            var value = Label("", 18, 0, 460, 72, 21, inputRoot,
                TextAnchor.MiddleLeft, TextColor, "输入内容");
            dialoguePlaceholder = Label("输入想对冷月说的话…", 18, 0, 460, 72, 20, inputRoot,
                TextAnchor.MiddleLeft, new Color(.92f, .87f, .96f, .72f), "输入提示");
            input.textComponent = value;
            input.placeholder = dialoguePlaceholder;
            input.characterLimit = 1000;
            input.lineType = TMP_InputField.LineType.SingleLine;
#region debug-point C:input-glyphs
            input.onValueChanged.AddListener(ReportInputGlyphCoverage);
#endregion
            voiceInput = new SpeechToTextRecorder();
            voiceInput.PartialText += OnVoicePartial;
            voiceInput.Completed += OnVoiceCompleted;
            voiceInput.Failed += OnVoiceFailed;

            sendButton = Button("发送", 598, 20, 106, 72,
                SendDialogue, dialogueRoot, 20, "发送");
            sendButton.GetComponent<Image>().color = DialogueSendColor;
            var closeButton = Button("关闭", 716, 20, 90, 72,
                CloseDialogue, dialogueRoot, 18, "关闭");
            closeButton.GetComponent<Image>().color = DialogueCloseColor;
            aiRequiredHint = Rect("AI 未配置提示", 0, -68, 824, 60, dialogueRoot);
            AddBackground(aiRequiredHint, DialogueGlassColor);
            Label("先配置ai才能激活对话功能", 18, 10, 570, 40, 22,
                aiRequiredHint, TextAnchor.MiddleLeft, TextColor, "AI 配置提示");
            var configure = Button("点此配置ai", 600, 7, 206, 46,
                () => OpenAIConfiguration(true), aiRequiredHint, 21);
            configure.GetComponent<Image>().color = DialogueSendColor;
            RefreshAIAvailability();

            dialogueGroup.alpha = 0;
            dialogueGroup.interactable = false;
            dialogueGroup.blocksRaycasts = false;
            dialogueRoot.localScale = new Vector3(.08f, .88f, 1);
            dialogueRoot.anchoredPosition = new Vector2(0, -10);
            dialogueRoot.gameObject.SetActive(false);
        }

        private void BuildSubtitle()
        {
            // Sits in the gap between the page panel and the navigation buttons. The gap
            // is closed while she is silent and grows upward to fit an actual reply.
            var root = AnchoredRect("角色字幕", hudRoot, new Vector2(.5f, 0),
                new Vector2(.5f, 0), new Vector2(.5f, 0),
                new Vector2(0, SubtitleBaseY), new Vector2(SubtitleWidth, SubtitleBaseHeight));
            subtitleRect = root;
            subtitle = AlwaysOnTopUi.ConfigureText(root.gameObject.AddComponent<TextMeshProUGUI>());
            subtitle.fontSize = 24;
            subtitle.enableAutoSizing = false;
            subtitle.color = TextColor;
            subtitle.alignment = TextAlignmentOptions.Center;
            subtitle.raycastTarget = false;
            subtitle.enableWordWrapping = true;
            subtitle.overflowMode = TextOverflowModes.Truncate;
        }

        // Grows the gap to fit the reply instead of shrinking the font.
        private void UpdateSubtitleGap()
        {
            if (subtitle == null) return;
            var text = subtitle.text;
            if (string.IsNullOrEmpty(text))
            {
                subtitleGapTarget = 0f;
                return;
            }
            var preferred = subtitle.GetPreferredValues(text, SubtitleWidth - 16f, 0f).y;
            subtitleGapTarget = Mathf.Clamp(preferred + 8f - SubtitleBaseHeight, 0f,
                SubtitleMaxExtra);
        }

        private void AnimateSubtitleGap(float deltaTime)
        {
            if (subtitleRect == null) return;
            subtitleGap = Mathf.Lerp(subtitleGap, subtitleGapTarget,
                1f - Mathf.Exp(-10f * deltaTime));
            subtitleRect.sizeDelta =
                new Vector2(SubtitleWidth, SubtitleBaseHeight + subtitleGap);
            if (contentRoot != null)
                contentRoot.anchoredPosition =
                    new Vector2(contentRoot.anchoredPosition.x, ContentBaseY + subtitleGap);
        }

        private void BuildPages()
        {
            contentRoot = AnchoredRect("HUD Content", hudRoot, new Vector2(.5f, 0),
                new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 200), new Vector2(760, 448));
            AddPanelBackground(contentRoot, PanelColor);
            contentGroup = contentRoot.gameObject.AddComponent<CanvasGroup>();
            contentGroup.alpha = 0;
            contentGroup.interactable = contentGroup.blocksRaycasts = false;
            contentRoot.gameObject.SetActive(false);
            BuildInteractionPage();
            BuildMiniGamesPage();
            BuildPickupPage();
            BuildEatPage();
            BuildInformationPage();
            BuildItemsPage();
            BuildSettingsPage();
            BuildAIConfigurationPage();
            BuildAITutorialPage();
            BuildFirstRunPage();
        }

        private GameObject CreatePage(HudPage page, string name)
        {
            var root = StretchRect(name, contentRoot);
            root.gameObject.AddComponent<CanvasGroup>().alpha = 0;
            root.gameObject.SetActive(false);
            pages.Add(page, root.gameObject);
            return root.gameObject;
        }

        private void BuildInteractionPage()
        {
            var page = CreatePage(HudPage.Interaction, "互动栏目");
            PageHeader(page.transform, "选择互动", "和冷月一起，享受片刻陪伴", "互动标题");
            var activities = new[] { "一起散步", "坐下聊天", "躺下休息", "起身", "切水果" };
            for (var i = 0; i < activities.Length; i++)
            {
                var activity = activities[i];
                PanelButton(activity, 28 + i % 2 * 360, 112 + i / 2 * 72, 344, 60,
                    () => SelectTestActivity(activity), page.transform, 22, "活动/" + activity);
            }
            PanelButton("物品拾取", 388, 256, 344, 60, () => SetActivePage(HudPage.PickupItems),
                page.transform, 22, "活动/物品拾取", primary: true);
            PanelButton("吃东西", 28, 328, 344, 52, () => SetActivePage(HudPage.EatFood),
                page.transform, 22, "活动/吃东西", primary: true);
            PanelButton("小游戏  ›", 388, 328, 344, 52, () => SetActivePage(HudPage.MiniGames),
                page.transform, 22, "活动/小游戏", primary: true);
            var feedback = PanelCard("互动状态", 28, 390, 704, 44, page.transform);
            interactionFeedback = Label("请选择一项互动活动。", 14, 2, 476, 40, 16,
                feedback, TextAnchor.MiddleLeft, MutedTextColor, "互动反馈");
            PanelButton("取消切水果", 506, 4, 184, 36,
                () => Run(() => bootstrap.Session.CancelItemAction()),
                feedback, 18, "取消切水果", danger: true);
        }

        private void BuildPickupPage()
        {
            var page = CreatePage(HudPage.PickupItems, "物品拾取面板");
            PageHeader(page.transform, "物品拾取", "先放置物品，点击名称让冷月走过去拿起", "拾取标题");
            PanelButton("‹ 返回互动", 574, 20, 158, 44,
                () => SetActivePage(HudPage.Interaction), page.transform, 18, "拾取/返回", quiet: true);
            for (var slot = 0; slot < CatalogPageSize; slot++)
            {
                var capturedSlot = slot;
                pickupButtons.Add(PanelButton("", 28 + slot % 3 * 240, 112 + slot / 3 * 80, 224, 68,
                    () => PickUpItem(capturedSlot), page.transform, 18, "拾取槽位/" + slot));
            }
            PanelButton("‹", 28, 284, 68, 42, () => ChangePickupPage(-1),
                page.transform, 24, "拾取/上一页", quiet: true);
            pickupPageLabel = Label("", 108, 284, 544, 42, 17, page.transform,
                TextAnchor.MiddleCenter, MutedTextColor, "拾取页码");
            PanelButton("›", 664, 284, 68, 42, () => ChangePickupPage(1),
                page.transform, 24, "拾取/下一页", quiet: true);
            pickupFeedback = Label("仅列出房间内可移动的单手物品。", 28, 332, 704, 46, 17,
                page.transform, TextAnchor.MiddleLeft, MutedTextColor, "拾取状态");
            PanelButton("去放置物品", 28, 390, 344, 44, () => SetActivePage(HudPage.Items),
                page.transform, 18, "拾取/放置", quiet: true);
            pickupReleaseButton = PanelButton("放下 / 取消", 388, 390, 344, 44,
                () => Run(() => bootstrap.Session.Avatar.Cancel(pickupAction)),
                page.transform, 18, "拾取/放下", danger: true);
        }

        private void ChangePickupPage(int offset)
        {
            var count = Mathf.Max(1, (pickupDefinitions.Length + CatalogPageSize - 1) / CatalogPageSize);
            pickupPage = (pickupPage + offset + count) % count;
            RefreshPickupPage();
        }

        private void RefreshPickupPage()
        {
            var session = bootstrap == null ? null : bootstrap.Session;
            var placed = session == null ? Array.Empty<GameItem>() :
                session.ItemViews.Where(item => PickupItemAction.Supports(item) &&
                    session.Items.IsCurrent(item.Instance)).ToArray();
            pickupDefinitions = placed.Select(item => item.Definition).Distinct()
                .OrderBy(item => item.DisplayName).ThenBy(item => item.Id).ToArray();
            var count = Mathf.Max(1, (pickupDefinitions.Length + CatalogPageSize - 1) / CatalogPageSize);
            pickupPage = Mathf.Clamp(pickupPage, 0, count - 1);
            var running = pickupAction != null && !pickupAction.IsFinished;
            for (var slot = 0; slot < pickupButtons.Count; slot++)
            {
                var index = pickupPage * CatalogPageSize + slot;
                var button = pickupButtons[slot];
                button.gameObject.SetActive(index < pickupDefinitions.Length);
                if (index >= pickupDefinitions.Length) continue;
                var definition = pickupDefinitions[index];
                var available = placed.Count(item => item.Definition == definition && !item.Instance.IsOccupied);
                button.name = "拾取/" + definition.Id;
                button.transform.Find("Label").GetComponent<TMP_Text>().text =
                    definition.DisplayName + $"\n可拾取 {available}";
                button.interactable = session.ItemsReady && !session.Paused && !running && available > 0;
            }
            pickupPageLabel.text = pickupDefinitions.Length == 0
                ? "暂无可拾取物品，请先到物品背包放置"
                : $"{pickupPage + 1} / {count}  ·  共 {pickupDefinitions.Length} 种";
            pickupReleaseButton.interactable = running && session != null && !session.Paused;
            if (running)
                pickupFeedback.text = session.Avatar.Capture()
                    .FirstOrDefault(action => action.action_id == PickupItemAction.Id)?.phase ?? "正在准备拾取";
        }

        private void PickUpItem(int slot)
        {
            Run(() =>
            {
                if (pickupAction != null && !pickupAction.IsFinished) return;
                var index = pickupPage * CatalogPageSize + slot;
                if (index < 0 || index >= pickupDefinitions.Length) return;
                var session = bootstrap.Session;
                var definition = pickupDefinitions[index];
                var item = session.ItemViews.Where(value => PickupItemAction.Supports(value) &&
                        value.Definition == definition && session.Items.IsCurrent(value.Instance) &&
                        !value.Instance.IsOccupied)
                    .OrderBy(value => (value.Body.Pose.position - session.Motor.Position).sqrMagnitude)
                    .ThenBy(value => value.Instance.Id).FirstOrDefault();
                if (item == null)
                {
                    message = "该物品已被拿取或移除，请重新选择。";
                    pickupFeedback.text = message;
                    RefreshPickupPage();
                    return;
                }
                pickupAction = session.Submit(new AvatarIntent(PickupItemAction.Id, IntentSource.UI, item.Instance.Id));
                message = pickupAction.IsFinished ? pickupAction.Error : "冷月正在拾取" + definition.DisplayName + "。";
                pickupFeedback.text = message;
                RefreshPickupPage();
            });
        }

        private void BuildEatPage()
        {
            var page = CreatePage(HudPage.EatFood, "吃东西面板");
            PageHeader(page.transform, "吃东西", "只列出可食用的食物，冷月会分三口吃完", "吃东西标题");
            PanelButton("‹ 返回互动", 574, 20, 158, 44,
                () => SetActivePage(HudPage.Interaction), page.transform, 18, "吃东西/返回", quiet: true);
            for (var slot = 0; slot < CatalogPageSize; slot++)
            {
                var capturedSlot = slot;
                eatButtons.Add(PanelButton("", 28 + slot % 3 * 240, 112 + slot / 3 * 80, 224, 68,
                    () => EatPlacedItem(capturedSlot), page.transform, 18, "吃东西槽位/" + slot));
            }
            PanelButton("‹", 28, 284, 68, 42, () => ChangeEatPage(-1),
                page.transform, 24, "吃东西/上一页", quiet: true);
            eatPageLabel = Label("", 108, 284, 544, 42, 17, page.transform,
                TextAnchor.MiddleCenter, MutedTextColor, "吃东西页码");
            PanelButton("›", 664, 284, 68, 42, () => ChangeEatPage(1),
                page.transform, 24, "吃东西/下一页", quiet: true);
            eatFeedback = Label("仅列出房间内可食用的食物。", 28, 332, 704, 46, 17,
                page.transform, TextAnchor.MiddleLeft, MutedTextColor, "吃东西状态");
            PanelButton("去放置食物", 28, 390, 344, 44, () => SetActivePage(HudPage.Items),
                page.transform, 18, "吃东西/放置", quiet: true);
            eatReleaseButton = PanelButton("取消吃东西", 388, 390, 344, 44,
                () => Run(() => bootstrap.Session.Avatar.Cancel(eatAction)),
                page.transform, 18, "吃东西/取消", danger: true);
        }

        private void ChangeEatPage(int offset)
        {
            var count = Mathf.Max(1, (eatDefinitions.Length + CatalogPageSize - 1) / CatalogPageSize);
            eatPage = (eatPage + offset + count) % count;
            RefreshEatPage();
        }

        private void RefreshEatPage()
        {
            var session = bootstrap == null ? null : bootstrap.Session;
            var placed = session == null ? Array.Empty<GameItem>() :
                session.ItemViews.Where(item => EatItemAction.Supports(item) &&
                    session.Items.IsCurrent(item.Instance)).ToArray();
            eatDefinitions = placed.Select(item => item.Definition).Distinct()
                .OrderBy(item => item.DisplayName).ThenBy(item => item.Id).ToArray();
            var count = Mathf.Max(1, (eatDefinitions.Length + CatalogPageSize - 1) / CatalogPageSize);
            eatPage = Mathf.Clamp(eatPage, 0, count - 1);
            var running = eatAction != null && !eatAction.IsFinished;
            for (var slot = 0; slot < eatButtons.Count; slot++)
            {
                var index = eatPage * CatalogPageSize + slot;
                var button = eatButtons[slot];
                button.gameObject.SetActive(index < eatDefinitions.Length);
                if (index >= eatDefinitions.Length) continue;
                var definition = eatDefinitions[index];
                var available = placed.Count(item => item.Definition == definition && !item.Instance.IsOccupied);
                button.name = "吃东西/" + definition.Id;
                button.transform.Find("Label").GetComponent<TMP_Text>().text =
                    definition.DisplayName + $"\n可食用 {available}";
                button.interactable = session.ItemsReady && !session.Paused && !running && available > 0;
            }
            eatPageLabel.text = eatDefinitions.Length == 0
                ? "暂无可食用食物，请先到物品背包放置"
                : $"{eatPage + 1} / {count}  ·  共 {eatDefinitions.Length} 种";
            eatReleaseButton.interactable = running && session != null && !session.Paused;
            if (running)
                eatFeedback.text = session.Avatar.Capture()
                    .FirstOrDefault(action => action.action_id == EatItemAction.Id)?.phase ?? "正在准备吃东西";
        }

        private void EatPlacedItem(int slot)
        {
            Run(() =>
            {
                if (eatAction != null && !eatAction.IsFinished) return;
                var index = eatPage * CatalogPageSize + slot;
                if (index < 0 || index >= eatDefinitions.Length) return;
                var session = bootstrap.Session;
                var definition = eatDefinitions[index];
                var item = session.ItemViews.Where(value => EatItemAction.Supports(value) &&
                        value.Definition == definition && session.Items.IsCurrent(value.Instance) &&
                        !value.Instance.IsOccupied)
                    .OrderBy(value => (value.Body.Pose.position - session.Motor.Position).sqrMagnitude)
                    .ThenBy(value => value.Instance.Id).FirstOrDefault();
                if (item == null)
                {
                    message = "该食物已被拿取或吃掉，请重新选择。";
                    eatFeedback.text = message;
                    RefreshEatPage();
                    return;
                }
                eatAction = session.Submit(new AvatarIntent(EatItemAction.Id, IntentSource.UI, item.Instance.Id));
                message = eatAction.IsFinished ? eatAction.Error : "冷月正在吃" + definition.DisplayName + "。";
                eatFeedback.text = message;
                RefreshEatPage();
            });
        }

        private void BuildMiniGamesPage()
        {
            var page = CreatePage(HudPage.MiniGames, "小游戏二级页面");
            PageHeader(page.transform, "一起玩小游戏", "选择游戏，开始两个人的轻松时光");
            PanelButton("‹ 返回互动", 574, 20, 158, 44,
                () => SetActivePage(HudPage.Interaction), page.transform, 18, quiet: true);
            var index = 0;
            foreach (var game in bootstrap.MiniGames.Games)
            {
                if (game == null) continue;
                var id = game.Id;
                var card = PanelCard("游戏/" + id, 28, 112 + index * 76, 704, 64, page.transform);
                PanelButton(game.DisplayName, 10, 8, 214, 48, () =>
                {
                    if (bootstrap.MiniGames.TryStart(id)) SetActivePage(HudPage.None);
                }, card, 22, "小游戏/" + id, primary: true);
                Label(game.Description, 244, 8, 442, 48, 18,
                    card, TextAnchor.MiddleLeft, MutedTextColor);
                index++;
            }
            PanelButton("再来一局", 28, 362, 344, 58, () => Run(() =>
            {
                var host = bootstrap.MiniGames;
                if (host.Active == null) { message = "请先选择一个小游戏。"; return; }
                var id = host.Active.Id;
                host.Stop();
                if (host.TryStart(id)) SetActivePage(HudPage.None);
            }), page.transform, 21);
            PanelButton("结束小游戏", 388, 362, 344, 58, () =>
            {
                bootstrap.MiniGames.Stop();
                SetActivePage(HudPage.Interaction);
            }, page.transform, 21);
        }

        private void BuildInformationPage()
        {
            var page = CreatePage(HudPage.Information, "信息栏目");
            PageHeader(page.transform, "陪伴信息", "此刻的心情与陪伴状态", "信息标题");
            var card = PanelCard("陪伴状态", 28, 112, 704, 218, page.transform);
            Label("冷月 · 当前状态", 22, 18, 660, 34, 21, card,
                TextAnchor.MiddleLeft, SelectedColor);
            status = Label("", 22, 66, 660, 130, 20, card,
                TextAnchor.UpperLeft, TextColor, "角色状态");
            PanelButton("去走走", 28, 362, 224, 58, () => Run(() =>
            {
                var session = bootstrap.Session;
                if (session.Navigation.TryRandom(session.Motor.Position, new System.Random(), out var point))
                    session.Submit(new AvatarIntent("move", IntentSource.UI, payload: point));
            }), page.transform, 20, primary: true);
            PanelButton("留在这里", 268, 362, 224, 58, () => Run(() =>
            {
                var session = bootstrap.Session;
                session.Autonomy.Enabled = false;
                session.Submit(new AvatarIntent("stay", IntentSource.Player));
            }), page.transform, 20);
            PanelButton("切换服装", 508, 362, 224, 58, SwitchOutfit, page.transform, 20);
        }

        private void BuildItemsPage()
        {
            var page = CreatePage(HudPage.Items, "虚拟物品栏目");
            PageHeader(page.transform, "物品背包", "选择分类，点击物品即可取出", "物品标题");
            PanelButton("刷新库存", 574, 20, 158, 44,
                () => Run(() => bootstrap.Session.RefreshAllInventory()),
                page.transform, 17, "物品/刷新库存", quiet: true);
            catalog = bootstrap.CatalogItems.ToArray();
            const float categoryWidth = 110.6667f;
            const float categoryGap = 8;
            for (var index = 0; index < CatalogCategories.Length; index++)
            {
                var category = CatalogCategories[index];
                var button = PanelButton(category.GetDisplayName(),
                    28 + index * (categoryWidth + categoryGap), 94, categoryWidth, 42,
                    () => SelectCatalogCategory(category), page.transform, 16,
                    "物品分类/" + category.GetDisplayName(), quiet: true);
                catalogCategoryButtons.Add(button);
            }
            for (var slot = 0; slot < CatalogPageSize; slot++)
            {
                var capturedSlot = slot;
                var button = PanelButton("", 28 + slot % 3 * 240, 152 + slot / 3 * 80, 224, 68,
                    () => PlaceCatalogItem(capturedSlot), page.transform, 18,
                    "物品槽位/" + slot);
                catalogButtons.Add(button);
                var titleRect = button.transform.Find("Label") as RectTransform;
                titleRect.anchoredPosition = new Vector2(14, -8);
                titleRect.sizeDelta = new Vector2(122, 52);
                button.GetComponentInChildren<TMP_Text>().alignment = TextAlignmentOptions.Left;
                var badge = PanelCard("数量", 140, 10, 74, 48, button.transform);
                badge.GetComponent<Image>().color = QuietColor;
                catalogQuantityLabels.Add(Label("", 4, 0, 66, 48, 12, badge,
                    TextAnchor.MiddleCenter, MutedTextColor, "剩余数量"));
            }
            PanelButton("‹", 28, 312, 68, 42, () => ChangeCatalogPage(-1),
                page.transform, 24, "物品/上一页", quiet: true);
            catalogPageLabel = Label("", 108, 312, 544, 42, 17, page.transform,
                TextAnchor.MiddleCenter, MutedTextColor, "物品页码");
            PanelButton("›", 664, 312, 68, 42, () => ChangeCatalogPage(1),
                page.transform, 24, "物品/下一页", quiet: true);
            selectedItem = Label("", 28, 358, 704, 28, 17, page.transform,
                TextAnchor.MiddleLeft, TextColor, "当前物品");
            PanelButton("下个已放置", 28, 396, 224, 40, () => selected++, page.transform, 18);
            PanelButton("过去看看", 268, 396, 224, 40, VisitSelectedItem, page.transform, 18);
            recycleButton = PanelButton("回收当前物品", 508, 396, 224, 40,
                RemoveSelectedItem, page.transform, 18, danger: true);
            SelectCatalogCategory(selectedCategory);
        }

        private void SelectCatalogCategory(ItemCategory category)
        {
            selectedCategory = category;
            categoryCatalog = catalog.Where(item => item.Category == category).ToArray();
            catalogPage = 0;
            for (var index = 0; index < catalogCategoryButtons.Count; index++)
                catalogCategoryButtons[index].SetSelected(CatalogCategories[index] == category);
            RefreshCatalogPage();
        }

        private void ChangeCatalogPage(int offset)
        {
            var pageCount = Mathf.Max(1,
                (categoryCatalog.Length + CatalogPageSize - 1) / CatalogPageSize);
            catalogPage = (catalogPage + offset + pageCount) % pageCount;
            RefreshCatalogPage();
        }

        private void RefreshCatalogPage()
        {
            var pageCount = Mathf.Max(1,
                (categoryCatalog.Length + CatalogPageSize - 1) / CatalogPageSize);
            catalogPage = Mathf.Clamp(catalogPage, 0, pageCount - 1);
            for (var slot = 0; slot < catalogButtons.Count; slot++)
            {
                var index = catalogPage * CatalogPageSize + slot;
                var button = catalogButtons[slot];
                var visible = index < categoryCatalog.Length;
                button.gameObject.SetActive(visible);
                if (!visible) continue;
                var definition = categoryCatalog[index];
                button.name = "物品/" + definition.Id;
                var title = button.transform.Find("Label")?.GetComponent<TMP_Text>();
                if (title != null) title.text = definition.DisplayName;
            }
            RefreshCatalogQuantities();
            if (catalogPageLabel != null)
                catalogPageLabel.text = categoryCatalog.Length == 0 ? "该分类暂无可用物品" :
                    $"{catalogPage + 1} / {pageCount}  ·  共 {categoryCatalog.Length} 项";
        }

        private void PlaceCatalogItem(int slot)
        {
            var index = catalogPage * CatalogPageSize + slot;
            if (index < 0 || index >= categoryCatalog.Length) return;
            var definition = categoryCatalog[index];
            CancelRecycleSelection();
            if (placementPreview != null && placementPreview.IsActive &&
                !placementPreview.IsPreviewing(definition))
                placementPreview.Cancel();
            RunAsync(() => PlaceAsync(definition));
        }

        private void BuildSettingsPage()
        {
            var page = CreatePage(HudPage.Settings, "设置栏目");
            PageHeader(page.transform, "设置", "调整陪伴偏好与系统功能");
            personalityTab = PanelButton("角色性格", 28, 94, 344, 44,
                () => SetSettingsMode(true), page.transform, 19, "设置/角色性格", quiet: true);
            systemTab = PanelButton("系统", 388, 94, 344, 44,
                () => SetSettingsMode(false), page.transform, 19, "设置/系统", quiet: true);
            personalitySettingsRoot = Rect("角色性格设置", 28, 154, 704, 294, page.transform);
            systemSettingsRoot = Rect("系统设置", 28, 154, 704, 294, page.transform);
            var autonomyButton = PanelButton("自主活动：开启", 0, 0, 344, 52,
                () => Run(() => bootstrap.Session.Autonomy.Enabled = !bootstrap.Session.Autonomy.Enabled),
                systemSettingsRoot, 19);
            autonomyLabel = autonomyButton.GetComponentInChildren<TMP_Text>();
            var pauseButton = PanelButton("暂停", 360, 0, 344, 52,
                () => bootstrap.TogglePause(), systemSettingsRoot, 20, "暂停 / 继续");
            pauseLabel = pauseButton.GetComponentInChildren<TMP_Text>();
            PanelButton("重试定位", 0, 60, 344, 52,
                RetryLocalization, systemSettingsRoot, 20);
            PanelButton("保存", 360, 60, 344, 52,
                () => Run(() => { bootstrap.Session.Save(); message = "已保存"; }),
                systemSettingsRoot, 20, primary: true);
            PanelButton("重置界面", 0, 120, 344, 52, () =>
            {
                Recenter(true);
                message = "界面已重新定位";
            }, systemSettingsRoot, 20);
            PanelButton("AI配置", 360, 120, 344, 52,
                () => OpenAIConfiguration(false), systemSettingsRoot, 20, "设置/AI配置");
            var modeButton = PanelButton("交互模式", 0, 180, 344, 48, () => Run(() =>
            {
                var mode = bootstrap.MiniGames.Input;
                if (mode == null) return;
                if (Application.isEditor)
                    mode.SetMode(mode.CurrentMode == PlayerInteractionMode.Controller
                        ? PlayerInteractionMode.Gesture : PlayerInteractionMode.Controller);
                else message = "菜单可注视后捏合点选，也可食指触碰；物品需伸手靠近后握紧拾取，松手放下。";
            }), systemSettingsRoot, 20);
            inputModeLabel = modeButton.GetComponentInChildren<TMP_Text>();
            PanelButton("重启游戏", 360, 180, 344, 48, RestartGame,
                systemSettingsRoot, 20, "设置/重启游戏", danger: true);
            var resetButton = PanelButton("还原首次启动", 0, 238, 704, 48,
                RequestResetToFirstLaunch, systemSettingsRoot, 20,
                "设置/还原首次启动", danger: true);
            resetFirstLaunchLabel = resetButton.GetComponentInChildren<TMP_Text>();
            SetSettingsMode(true);
        }

        private void BuildFirstRunPage()
        {
            var page = CreatePage(HudPage.FirstRun, "首次启动引导");
            PageHeader(page.transform, "欢迎来到 MateSoul",
                "首次启动 · 建议先标定房间，获得最佳体验", "首次启动标题");
            var card = PanelCard("房间标定说明", 28, 96, 704, 172, page.transform);
            Label("冷月需要先认识你的房间。请使用 PICO 的房间标定功能扫描房间。\n" +
                  "标定后，角色行走、物品摆放、床椅识别与虚实遮挡才能正常工作；\n" +
                  "未标定房间时，许多功能会受到限制。",
                20, 18, 664, 136, 19, card, TextAnchor.MiddleLeft, TextColor,
                "房间标定说明文字");
            PanelButton("去标定房间", 28, 284, 344, 64, LaunchRoomCalibration,
                page.transform, 24, "首次启动/去标定房间", primary: true);
            PanelButton("稍后再说", 388, 284, 344, 64, () =>
            {
                bootstrap?.MarkFirstLaunchSeen();
                SetActivePage(HudPage.None);
                message = "你可以随时在“设置 → 重试定位”中重新检测房间。";
            }, page.transform, 22, "首次启动/稍后再说", quiet: true);
            Label("标定完成后回到游戏；若房间仍未更新，请在“设置 → 重试定位”中重新检测。",
                28, 362, 704, 52, 17, page.transform, TextAnchor.MiddleLeft,
                MutedTextColor, "首次启动提示");
        }

        private IEnumerator ShowFirstLaunchPanel()
        {
            yield return null;
            if (bootstrap == null || !bootstrap.IsFirstLaunchPending) yield break;
            SetActivePage(HudPage.FirstRun);
        }

        private async void LaunchRoomCalibration()
        {
            if (bootstrap == null) return;
            bootstrap.MarkFirstLaunchSeen();
            message = "正在打开 PICO 房间标定…";
            var error = await bootstrap.LaunchRoomCalibrationAsync();
            if (this == null || bootstrap == null) return;
            message = error ??
                "标定完成后回到游戏；若房间仍未更新，请在“设置 → 重试定位”中重新检测。";
        }

        private void RequestResetToFirstLaunch()
        {
            if (!resetFirstLaunchArmed)
            {
                resetFirstLaunchArmed = true;
                if (resetFirstLaunchLabel != null)
                    resetFirstLaunchLabel.text = "再次点击确认：清除存档并回到首次启动";
                message = "再次点击将清除游戏存档并回到首次启动；AI 配置会保留。";
                return;
            }
            DisarmResetToFirstLaunch();
            if (bootstrap == null) return;
            bootstrap.Session?.SuspendSaving();
            if (!bootstrap.ResetToFirstLaunch())
            {
                message = "无法清除存档，请稍后重试。";
                return;
            }
            message = "已还原到首次启动，正在重新载入…";
            StartCoroutine(ReloadSceneNextFrame());
        }

        private void DisarmResetToFirstLaunch()
        {
            resetFirstLaunchArmed = false;
            if (resetFirstLaunchLabel != null)
                resetFirstLaunchLabel.text = "还原首次启动";
        }

        private void RestartGame()
        {
            // 先保存当前会话，避免场景重载时丢失物品与房间状态。
            try { bootstrap.Session?.Save(); }
            catch (Exception error) { Debug.LogException(error, this); }
            StartCoroutine(ReloadSceneNextFrame());
        }

        private IEnumerator ReloadSceneNextFrame()
        {
            // 延后一帧再重载，避免在 UI 点击事件分发途中销毁整个 HUD。
            yield return null;
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0)
                SceneManager.LoadScene(scene.buildIndex, LoadSceneMode.Single);
            else
                SceneManager.LoadScene(scene.name, LoadSceneMode.Single);
        }

        private void BuildAIConfigurationPage()
        {
            var page = CreatePage(HudPage.AIConfiguration, "AI配置");
            PageHeader(page.transform, "AI配置", "连接你的 AI，开启与冷月的对话", "AI配置标题");
            PanelButton("返回", 612, 20, 120, 44, ReturnFromAIConfiguration,
                page.transform, 19, quiet: true);
            aiAddress = AIField("API 地址", "https://api.deepseek.com", 104, page.transform);
            aiModel = AIField("模型名称", "deepseek-v4-pro", 172, page.transform);
            aiKey = AIField("API Key", "填写你的 DeepSeek API Key", 240, page.transform, true);
            aiConfigurationStatus = Label("保存后立即生效；API Key 在本机加密保存。",
                28, 310, 704, 44, 17, page.transform, TextAnchor.MiddleLeft,
                MutedTextColor, "AI配置状态");
            aiSaveButton = PanelButton("保存配置", 28, 362, 224, 58,
                SaveAIConfiguration, page.transform, 20, primary: true);
            aiCheckButton = PanelButton("测试连接", 268, 362, 224, 58,
                CheckAIConfiguration, page.transform, 20);
            PanelButton("如何配置AI", 508, 362, 224, 58,
                () => SetActivePage(HudPage.AITutorial), page.transform, 20);
        }

        private TMP_InputField AIField(string title, string placeholder, float y,
            Transform parent, bool withPaste = false)
        {
            var width = withPaste ? 458 : 558;
            Label(title, 28, y, 132, 56, 19, parent, TextAnchor.MiddleLeft, MutedTextColor);
            var root = Rect(title + "输入框", 174, y, width, 56, parent);
            AddPanelBackground(root, CardColor);
            var field = root.gameObject.AddComponent<TMP_InputField>();
            field.textComponent = Label("", 18, 0, width - 36, 56, 19, root,
                TextAnchor.MiddleLeft, TextColor, title + "内容");
            field.placeholder = Label(placeholder, 18, 0, width - 36, 56, 18, root,
                TextAnchor.MiddleLeft, MutedTextColor, title + "提示");
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = withPaste ? 4096 : 300;
            if (withPaste)
                PanelButton("粘贴", 644, y, 88, 56, PasteAIConfigurationKey,
                    parent, 19, "AI配置/粘贴");
            return field;
        }

        private void PasteAIConfigurationKey()
        {
            if (aiKey == null) return;
            var clipboard = ReadClipboard();
            if (string.IsNullOrEmpty(clipboard))
            {
                aiConfigurationStatus.text = "剪贴板为空，无法粘贴。";
                return;
            }
            aiKey.text = clipboard.Trim();
            aiKey.ForceLabelUpdate();
            aiConfigurationStatus.text = "已粘贴剪贴板内容，请保存配置。";
        }

        private static string ReadClipboard()
        {
            try
            {
                var text = GUIUtility.systemCopyBuffer;
                if (!string.IsNullOrEmpty(text)) return text;
            }
            catch (Exception) { }
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var clipboard = activity.Call<AndroidJavaObject>(
                    "getSystemService", "clipboard");
                using var clip = clipboard.Call<AndroidJavaObject>("getPrimaryClip");
                if (clip != null && clip.Call<int>("getItemCount") > 0)
                {
                    using var item = clip.Call<AndroidJavaObject>("getItemAt", 0);
                    using var text = item.Call<AndroidJavaObject>("getText");
                    if (text != null) return text.Call<string>("toString");
                }
            }
            catch (Exception) { }
#endif
            return null;
        }

        private void BuildAITutorialPage()
        {
            var page = CreatePage(HudPage.AITutorial, "如何配置AI");
            PageHeader(page.transform, "如何配置AI", "跟随三个步骤，完成连接");
            PanelButton("返回配置", 578, 20, 154, 44,
                () => SetActivePage(HudPage.AIConfiguration), page.transform, 19, quiet: true);
            TutorialStep(page.transform, 1, 104, "创建密钥",
                "在 platform.deepseek.com 注册、充值并创建 API Key。");
            TutorialStep(page.transform, 2, 210, "填写连接信息",
                "默认地址 https://api.deepseek.com；也支持 OpenAI 协议的\n供应商 HTTPS 地址。填写模型名称与自己的 API Key。");
            TutorialStep(page.transform, 3, 316, "验证并保存",
                "点击「测试连接」，验证成功后「保存配置」。\n回到对话即可发送，更换配置无需重启游戏。");
        }

        private void OpenAIConfiguration(bool fromDialogue)
        {
            aiReturnToDialogue = fromDialogue;
            var saved = bootstrap.AIConfiguration;
            aiAddress.text = saved.endpoint;
            aiModel.text = saved.model;
            aiKey.text = saved.apiKey;
            aiConfigurationStatus.text = "保存后立即生效；API Key 在本机加密保存。";
            if (dialogueOpen) SetDialogueOpen(false);
            SetActivePage(HudPage.AIConfiguration);
        }

        private PlayerAIConfiguration ReadAIConfiguration() => PlayerAIConfiguration.Validate(
            aiAddress.text, aiModel.text, aiKey.text, bootstrap.AllowLocalAIHttp);

        private void SaveAIConfiguration()
        {
            try
            {
                bootstrap.SaveAIConfiguration(ReadAIConfiguration());
                aiConfigurationStatus.text = "AI配置已保存。";
                RefreshAIAvailability();
                if (aiReturnToDialogue) ReturnFromAIConfiguration();
            }
            catch (Exception error) { aiConfigurationStatus.text = error.Message; }
        }

        private async void CheckAIConfiguration()
        {
            if (aiCheckRunning) return;
            aiCheckRunning = true;
            aiSaveButton.interactable = aiCheckButton.interactable = false;
            aiAddress.interactable = aiModel.interactable = aiKey.interactable = false;
            try
            {
                var configuration = ReadAIConfiguration();
                aiConfigurationStatus.text = "正在验证 API Key 与模型…";
                await HttpLLMProvider.CheckConfigurationAsync(configuration, hudLifetime.Token);
                aiConfigurationStatus.text = "连接成功，API Key 与模型可用。请保存配置。";
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (aiConfigurationStatus != null) aiConfigurationStatus.text = error.Message;
            }
            finally
            {
                aiCheckRunning = false;
                if (aiSaveButton != null) aiSaveButton.interactable = true;
                if (aiCheckButton != null) aiCheckButton.interactable = true;
                if (aiAddress != null)
                    aiAddress.interactable = aiModel.interactable = aiKey.interactable = true;
            }
        }

        private void ReturnFromAIConfiguration()
        {
            if (aiReturnToDialogue) SetDialogueOpen(true);
            else { SetActivePage(HudPage.Settings); SetSettingsMode(false); }
        }

        private void RefreshAIAvailability()
        {
            var configured = bootstrap != null && bootstrap.AIConfiguration.IsConfigured;
            var thinking = bootstrap != null && bootstrap.Session != null &&
                bootstrap.Session.Conversation.State == MateSoul.Gameplay.ConversationState.Thinking;
            var capturing = voiceInput != null && voiceInput.IsCapturing;
            if (aiRequiredHint != null) aiRequiredHint.gameObject.SetActive(!configured);
            if (input != null) input.interactable = configured && !capturing;
            if (sendButton != null)
                sendButton.interactable = configured && bootstrap.Session != null &&
                    !thinking && !capturing;
            if (micButton != null)
                micButton.interactable = configured && (!thinking || capturing);
        }

        private void SetSettingsMode(bool personality)
        {
            personalityTab?.SetSelected(personality);
            systemTab?.SetSelected(!personality);
            if (personalitySettingsRoot != null)
                personalitySettingsRoot.gameObject.SetActive(personality);
            if (systemSettingsRoot != null)
                systemSettingsRoot.gameObject.SetActive(!personality);
        }

        private void EnsurePersonalityControls(GameSession session)
        {
            if (personalityLabels.Count > 0) return;
            var identity = session.Persona.Identity;
            Label($"{identity.name} · {identity.age}岁 · 女 · {identity.height_cm} cm",
                0, 0, 704, 28, 18, personalitySettingsRoot,
                TextAnchor.MiddleCenter, MutedTextColor, "角色基础信息");
            for (var index = 0; index < session.Persona.Traits.Count; index++)
            {
                var trait = session.Persona.Traits[index];
                var traitId = trait.id;
                var button = PanelButton("", index % 2 * 360, 44 + index / 2 * 72, 344, 60,
                    () => Run(() => session.SelectNextPersonality(traitId)),
                    personalitySettingsRoot, 18, "性格/" + traitId);
                personalityLabels.Add(traitId, button.GetComponentInChildren<TMP_Text>());
            }
        }

        private void TogglePage(HudPage page)
        {
            if (page == HudPage.Dialogue)
            {
                SetDialogueOpen(!dialogueOpen);
                return;
            }
            SetActivePage(activePage == page ? HudPage.None : page);
        }

        private void SetActivePage(HudPage page)
        {
            if (activePage == HudPage.FirstRun && page != HudPage.FirstRun)
                bootstrap?.MarkFirstLaunchSeen();
            if (activePage == HudPage.Settings && page != HudPage.Settings)
                DisarmResetToFirstLaunch();
            CloseKeyboard();
            if (page != HudPage.Items && placementPreview != null)
                placementPreview.Cancel();
            if (page != HudPage.Items) CancelRecycleSelection();
            activePage = page;
            if (page == HudPage.PickupItems) RefreshPickupPage();
            if (page == HudPage.EatFood) RefreshEatPage();
            if (panelTransition != null) StopCoroutine(panelTransition);
            if (page == HudPage.None && !contentRoot.gameObject.activeSelf)
            {
                foreach (var entry in pages) entry.Value.SetActive(false);
                panelTransition = null;
            }
            else panelTransition = StartCoroutine(AnimatePanel(page));
            RefreshNavigationSelection();
        }

        private IEnumerator AnimatePanel(HudPage target)
        {
            var opening = target != HudPage.None;
            var roots = pages.Values.ToArray();
            var groups = roots.Select(root => root.GetComponent<CanvasGroup>()).ToArray();
            var alphas = groups.Select(group => group.alpha).ToArray();
            var positions = roots.Select(root => ((RectTransform)root.transform).anchoredPosition).ToArray();
            var targetRoot = opening ? pages[target] : null;
            for (var index = 0; index < roots.Length; index++)
            {
                groups[index].interactable = groups[index].blocksRaycasts = false;
                if (roots[index] == targetRoot && !roots[index].activeSelf)
                {
                    roots[index].SetActive(true);
                    alphas[index] = groups[index].alpha = 0;
                    positions[index] = new Vector2(0, -10);
                }
            }
            contentRoot.gameObject.SetActive(true);
            contentGroup.interactable = contentGroup.blocksRaycasts = false;
            var startAlpha = contentGroup.alpha;
            var startPosition = contentRoot.anchoredPosition;
            if (startAlpha == 0) startPosition = new Vector2(0, 188);
            var duration = opening ? .26f : .18f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var ease = EaseOutQuint(Mathf.Clamp01(elapsed / duration));
                var pageEase = EaseInOutCubic(Mathf.Clamp01(elapsed / .18f));
                contentGroup.alpha = Mathf.Lerp(startAlpha, opening ? 1 : 0, ease);
                contentRoot.anchoredPosition = Vector2.Lerp(
                    startPosition, new Vector2(0, opening ? 200 : 188), ease);
                for (var index = 0; index < roots.Length; index++)
                {
                    if (!roots[index].activeSelf) continue;
                    var incoming = roots[index] == targetRoot;
                    groups[index].alpha = Mathf.Lerp(alphas[index], incoming ? 1 : 0, pageEase);
                    ((RectTransform)roots[index].transform).anchoredPosition = Vector2.Lerp(
                        positions[index], incoming ? Vector2.zero : new Vector2(0, 6), pageEase);
                }
                yield return null;
            }
            contentGroup.alpha = opening ? 1 : 0;
            contentRoot.anchoredPosition = new Vector2(0, 200);
            contentRoot.gameObject.SetActive(opening);
            contentGroup.interactable = contentGroup.blocksRaycasts = opening;
            for (var index = 0; index < roots.Length; index++)
            {
                var incoming = roots[index] == targetRoot;
                roots[index].SetActive(incoming);
                groups[index].alpha = incoming ? 1 : 0;
                groups[index].interactable = groups[index].blocksRaycasts = incoming;
                ((RectTransform)roots[index].transform).anchoredPosition = Vector2.zero;
            }
            panelTransition = null;
        }

        private void RefreshNavigationSelection()
        {
            foreach (var entry in navigationBackgrounds)
                entry.Value.color =
                    entry.Key == HudPage.Dialogue && dialogueOpen ||
                    entry.Key == activePage ||
                    entry.Key == HudPage.Interaction &&
                        (activePage == HudPage.MiniGames || activePage == HudPage.PickupItems ||
                         activePage == HudPage.EatFood)
                        ? SelectedColor
                        : ButtonColor;
        }

        private void SetDialogueOpen(bool open)
        {
            if (dialogueOpen == open && dialogueTransition == null) return;
            if (!open)
            {
                CloseKeyboard();
                StopVoiceInput();
            }
            if (open) SetActivePage(HudPage.None);
            RefreshAIAvailability();
            dialogueOpen = open;
            bootstrap?.Session?.SetDialogueAttention(open);
            RefreshNavigationSelection();
            if (dialogueTransition != null) StopCoroutine(dialogueTransition);
            dialogueTransition = StartCoroutine(AnimateDialogue(open));
        }

        private IEnumerator AnimateDialogue(bool open)
        {
            navigationRoot.gameObject.SetActive(true);
            dialogueRoot.gameObject.SetActive(true);
            navigationGroup.interactable = false;
            navigationGroup.blocksRaycasts = false;
            dialogueGroup.interactable = false;
            dialogueGroup.blocksRaycasts = false;

            var navigationStartAlpha = navigationGroup.alpha;
            var navigationStartPosition = navigationRoot.anchoredPosition;
            var navigationStartScale = navigationRoot.localScale;
            var dialogueStartAlpha = dialogueGroup.alpha;
            var dialogueStartPosition = dialogueRoot.anchoredPosition;
            var dialogueStartScale = dialogueRoot.localScale;
            var duration = open ? .62f : .58f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var navigationProgress = open
                    ? Mathf.Clamp01(elapsed / .34f)
                    : Mathf.Clamp01((elapsed - .06f) / .48f);
                var dialogueProgress = open
                    ? Mathf.Clamp01((elapsed - .1f) / .48f)
                    : Mathf.Clamp01(elapsed / .42f);
                var navigationEase = open
                    ? EaseInOutCubic(navigationProgress)
                    : EaseOutBack(navigationProgress);
                var dialogueEase = open
                    ? EaseOutQuint(dialogueProgress)
                    : EaseInOutCubic(dialogueProgress);

                navigationGroup.alpha = Mathf.Lerp(
                    navigationStartAlpha, open ? 0 : 1, navigationEase);
                navigationRoot.anchoredPosition = Vector2.LerpUnclamped(
                    navigationStartPosition, open ? new Vector2(0, -14) : Vector2.zero,
                    navigationEase);
                navigationRoot.localScale = Vector3.LerpUnclamped(
                    navigationStartScale, open ? new Vector3(.98f, .98f, 1) : Vector3.one,
                    navigationEase);

                dialogueGroup.alpha = Mathf.Lerp(
                    dialogueStartAlpha, open ? 1 : 0, dialogueEase);
                dialogueRoot.anchoredPosition = Vector2.LerpUnclamped(
                    dialogueStartPosition, open ? Vector2.zero : new Vector2(0, -7),
                    dialogueEase);
                var targetScale = open
                    ? Vector3.one
                    : new Vector3(.04f, .86f, 1);
                dialogueRoot.localScale = Vector3.LerpUnclamped(
                    dialogueStartScale, targetScale, dialogueEase);
                yield return null;
            }

            navigationGroup.alpha = open ? 0 : 1;
            navigationRoot.anchoredPosition = open ? new Vector2(0, -14) : Vector2.zero;
            navigationRoot.localScale = open ? new Vector3(.98f, .98f, 1) : Vector3.one;
            dialogueGroup.alpha = open ? 1 : 0;
            dialogueRoot.anchoredPosition = open ? Vector2.zero : new Vector2(0, -7);
            dialogueRoot.localScale = open ? Vector3.one : new Vector3(.04f, .86f, 1);
            navigationRoot.gameObject.SetActive(!open);
            dialogueRoot.gameObject.SetActive(open);
            navigationGroup.interactable = !open;
            navigationGroup.blocksRaycasts = !open;
            dialogueGroup.interactable = open;
            dialogueGroup.blocksRaycasts = open;
            dialogueTransition = null;
        }

        private static float EaseInOutCubic(float value)
            => value < .5f
                ? 4f * value * value * value
                : 1f - Mathf.Pow(-2f * value + 2f, 3f) * .5f;

        private static float EaseOutQuint(float value)
            => 1f - Mathf.Pow(1f - value, 5f);

        private static float EaseOutBack(float value)
        {
            const float overshoot = 1.35f;
            var shifted = value - 1f;
            return 1f + (overshoot + 1f) * shifted * shifted * shifted +
                   overshoot * shifted * shifted;
        }

        private void CreateHudToggleActions()
        {
            controllerHudToggle = new InputAction("展示/隐藏 UI", InputActionType.Button);
            controllerHudToggle.AddBinding("<XRController>{RightHand}/secondaryButton");
            controllerHudToggle.Enable();
        }

        private void UpdateHudVisibilityInput()
        {
            if (IsTextInputFocused)
            {
                controllerToggleReady = handToggleReady = handToggleLatched = false;
                handToggleStarted = -1;
                return;
            }
            if (placementPreview != null && placementPreview.IsActive ||
                placementVisibilityFrame == Time.frameCount) return;
            if (interactionMode == null)
                interactionMode = bootstrap?.MiniGames?.Input ??
                                  FindObjectOfType<PlayerInteractionModeController>();
            if (interactionMode == null) return;
            var mode = interactionMode.CurrentMode;
            if (!visibilityInputModeKnown || visibilityInputMode != mode)
            {
                visibilityInputModeKnown = true;
                visibilityInputMode = mode;
                controllerToggleReady = false;
                handToggleReady = false;
                handToggleLatched = false;
                handToggleStarted = -1;
            }

            if (mode == PlayerInteractionMode.Controller)
            {
                var pressed = controllerHudToggle != null &&
                              controllerHudToggle.ReadValue<float>() > .5f;
                if (!pressed)
                {
                    controllerToggleReady = true;
                    return;
                }
                if (!controllerToggleReady || !controllerHudToggle.WasPressedThisFrame()) return;
                controllerToggleReady = false;
                SetHudVisible(!hudVisible);
                return;
            }

            if (!interactionMode.CanToggleHandMenu)
            {
                handToggleReady = handToggleLatched = false;
                handToggleStarted = -1;
                return;
            }
            var left = interactionMode.Hands[0].Closed;
            var right = interactionMode.Hands[1].Closed;
            if (!left && !right)
            {
                handToggleReady = true;
                handToggleLatched = false;
                handToggleStarted = -1;
                return;
            }
            if (!handToggleReady || handToggleLatched) return;
            if (!left || !right)
            {
                handToggleStarted = -1;
                return;
            }
            if (handToggleStarted < 0)
            {
                handToggleStarted = Time.unscaledTime;
                return;
            }
            if (Time.unscaledTime - handToggleStarted < HandToggleHoldSeconds) return;
            handToggleLatched = true;
            SetHudVisible(!hudVisible);
        }

        private string VisibilityHint()
        {
            if (interactionMode == null)
                interactionMode = bootstrap?.MiniGames?.Input ??
                                  FindObjectOfType<PlayerInteractionModeController>();
            return "展示/隐藏UI：" +
                   (interactionMode == null
                       ? "右手 B 键 / 双手握拳"
                       : interactionMode.CurrentMode == PlayerInteractionMode.Controller
                           ? "右手 B 键"
                           : "空手远离物品，双手握拳");
        }

        private void SetHudVisible(bool visible)
        {
            if (!visible)
            {
                CloseKeyboard();
                StopVoiceInput();
            }
            hudVisible = visible;
            ApplyHudVisibility();
        }

        private void OnPlacementActiveChanged(bool active)
        {
            placementVisibilityFrame = Time.frameCount;
            controllerToggleReady = handToggleReady = handToggleLatched = false;
            handToggleStarted = -1;
            ApplyHudVisibility();
        }

        private void ApplyHudVisibility()
        {
            if (hudRoot != null) hudRoot.gameObject.SetActive(IsVisible);
            if (IsVisible || EventSystem.current == null || canvasRoot == null) return;
            var selectedObject = EventSystem.current.currentSelectedGameObject;
            if (selectedObject != null && selectedObject.transform.IsChildOf(canvasRoot))
                EventSystem.current.SetSelectedGameObject(null);
        }

        private void SelectTestActivity(string activity)
        {
            if (activity == "切水果")
            {
                Run(() =>
                {
                    interactionAction = bootstrap.Session.Submit(
                        new AvatarIntent(CutFruitAction.Id, IntentSource.UI));
                    message = interactionAction.IsFinished ? interactionAction.Error : "正在准备切水果";
                });
            }
            else if (activity == "坐下聊天")
            {
                Run(() =>
                {
                    var session = bootstrap.Session;
                    var seat = session.Chairs?.Nearest(session.Motor.Position);
                    if (seat == null) { message = session.Chairs?.Status ?? "房间仍在准备"; return; }
                    session.Autonomy.Enabled = false;
                    interactionAction = session.Submit(new AvatarIntent(
                        RealityInteractionIds.Sit, IntentSource.UI, seat.Id));
                    message = interactionAction.IsFinished ? interactionAction.Error : "正在走向椅子并对齐落座";
                });
            }
            else if (activity == "躺下休息")
            {
                Run(() =>
                {
                    var session = bootstrap.Session;
                    var bed = session.Beds?.Nearest(session.Motor.Position);
                    if (bed == null) { message = session.Beds?.Status ?? "房间仍在准备"; return; }
                    session.Autonomy.Enabled = false;
                    interactionAction = session.Submit(new AvatarIntent(
                        RealityInteractionIds.Lie, IntentSource.UI, bed.Id));
                    message = interactionAction.IsFinished
                        ? interactionAction.Error : "正在走向床或沙发并准备躺下";
                });
            }
            else if (activity == "起身")
            {
                Run(() =>
                {
                    interactionAction = bootstrap.Session.Submit(new AvatarIntent("stay", IntentSource.Player));
                    message = "正在起身";
                });
            }
            else
            message = $"“{activity}”尚在开发，测试按钮已响应。";
            if (interactionFeedback != null) interactionFeedback.text = message;
        }

        /// <summary>
        /// HUD 内置键盘：PICO 全屏应用不能安全使用系统输入法（会把 XR 容器切到后台），
        /// 输入框统一改为在应用内输入；中文通过拼音候选栏选择。
        /// </summary>
        private void BuildKeyboard()
        {
            keyboardRoot = AnchoredRect("屏幕键盘", hudRoot, new Vector2(.5f, 0),
                new Vector2(.5f, 0), new Vector2(.5f, 0), Vector2.zero,
                new Vector2(824, 250));
            AddPanelBackground(keyboardRoot, PanelColor);
            BuildKeyboardCandidates();
            const float keyHeight = KeyboardKeyHeight;
            const float gap = KeyboardRowGap;
            const float margin = 8;
            const float keyWidth = 61;
            KeyboardRow(0, keyHeight, gap, margin, keyWidth,
                "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "_");
            KeyboardRow(1, keyHeight, gap, margin, keyWidth,
                "q", "w", "e", "r", "t", "y", "u", "i", "o", "p", "@", ".");
            KeyboardLetterRow(2, keyHeight, gap, margin, keyWidth);
            KeyboardBottomRow(3, keyHeight, gap, margin);
            BindHudInput(input);
            BindHudInput(aiAddress);
            BindHudInput(aiModel);
            BindHudInput(aiKey);
            SetKeyboardChinese(false);
            keyboardRoot.gameObject.SetActive(false);
        }

        private void BuildKeyboardCandidates()
        {
            var bar = Rect("拼音候选栏", 12, 8, 800, 42, keyboardRoot);
            keyboardComposition = Label("英文输入", 6, 0, 122, 42, 18, bar,
                TextAnchor.MiddleLeft, MutedTextColor, "拼音组合");
            for (var index = 0; index < KeyboardCandidateCount; index++)
            {
                var slot = index;
                var button = PanelButton(string.Empty, 134 + index * 108, 0, 100, 42,
                    () => ChooseKeyboardCandidate(slot), bar, 20, "候选" + (index + 1),
                    quiet: true);
                keyboardCandidateSlots.Add((button, button.GetComponentInChildren<TMP_Text>()));
            }
        }

        private void KeyboardRow(int row, float height, float gap, float margin, float width,
            params string[] labels)
        {
            var y = KeyboardRowTop + row * (height + gap);
            for (var index = 0; index < labels.Length; index++)
            {
                var value = labels[index];
                KeyboardKey(value, margin + index * (width + gap), y, width, height,
                    () => KeyboardType(value));
            }
        }

        private void KeyboardLetterRow(int row, float height, float gap, float margin, float width)
        {
            var y = KeyboardRowTop + row * (height + gap);
            var x = margin;
            KeyboardKey("大写", x, y, width, height, ToggleKeyboardShift).image.color = QuietColor;
            x += width + gap;
            foreach (var value in new[] { "a", "s", "d", "f", "g", "h", "j", "k", "l", ":", "/" })
            {
                AddKeyboardLetter(value, x, y, width, height);
                x += width + gap;
            }
            keyboardModeButton = KeyboardKey("英文", x, y, width, height, ToggleKeyboardLanguage);
            keyboardModeLabel = keyboardModeButton.GetComponentInChildren<TMP_Text>();
        }

        private void KeyboardBottomRow(int row, float height, float gap, float margin)
        {
            const float keyWidth = 61;
            var y = KeyboardRowTop + row * (height + gap);
            var x = margin;
            foreach (var value in new[] { "z", "x", "c", "v", "b", "n", "m" })
            {
                AddKeyboardLetter(value, x, y, keyWidth, height);
                x += keyWidth + gap;
            }
            KeyboardKey("删除", x, y, 76, height, KeyboardBackspace);
            x += 76 + gap;
            KeyboardKey("空格", x, y, 140, height, KeyboardSpace);
            x += 140 + gap;
            KeyboardKey("完成", x, y, 100, height, FinishKeyboardInput).image.color = DialogueSendColor;
        }

        private void AddKeyboardLetter(string value, float x, float y, float width, float height)
        {
            var button = KeyboardKey(value, x, y, width, height,
                () => KeyboardType(keyboardShift ? value.ToUpperInvariant() : value));
            keyboardLetterKeys.Add((button.GetComponentInChildren<TMP_Text>(), value));
        }

        private Button KeyboardKey(string label, float x, float y, float width, float height,
            UnityEngine.Events.UnityAction action)
        {
            var button = Button(label, x, y, width, height, action, keyboardRoot, 20, "键 " + label);
            button.image.color = CardColor;
            return button;
        }

        private void BindHudInput(TMP_InputField field)
        {
            if (field == null) return;
            // 关键点：禁止 TMP 打开系统软键盘，否则 PICO 会关闭整个 XR 容器。
            field.shouldHideSoftKeyboard = true;
            field.onSelect.AddListener(_ => OpenKeyboard(field));
        }

        private void OpenKeyboard(TMP_InputField field)
        {
            if (field == null || keyboardRoot == null) return;
            keyboardTarget = field;
            // 对话默认中文，配置项默认英文，仍可用中/英键随时切换。
            SetKeyboardChinese(field == input);
            keyboardRoot.gameObject.SetActive(true);
            if (dialogueOpen && dialogueRoot != null)
                dialogueRoot.anchoredPosition = new Vector2(0, KeyboardDialogueLift);
        }

        private void CloseKeyboard()
        {
            var target = keyboardTarget;
            keyboardTarget = null;
            keyboardShift = false;
            pinyinBuffer = string.Empty;
            foreach (var entry in keyboardLetterKeys) entry.label.text = entry.value;
            if (target != null) target.DeactivateInputField();
            if (keyboardRoot != null) keyboardRoot.gameObject.SetActive(false);
            if (dialogueOpen && dialogueRoot != null) dialogueRoot.anchoredPosition = Vector2.zero;
        }

        private void FinishKeyboardInput()
        {
            if (keyboardChinese && pinyinBuffer.Length > 0) CommitKeyboardCandidate(0);
            CloseKeyboard();
        }

        private void ToggleKeyboardLanguage() => SetKeyboardChinese(!keyboardChinese);

        private void SetKeyboardChinese(bool chinese)
        {
            keyboardChinese = chinese;
            pinyinBuffer = string.Empty;
            if (keyboardModeLabel != null) keyboardModeLabel.text = chinese ? "中文" : "英文";
            if (keyboardModeButton != null)
                keyboardModeButton.image.color = chinese ? DialogueSendColor : QuietColor;
            RefreshKeyboardCandidates();
        }

        private void RefreshKeyboardCandidates()
        {
            keyboardCandidateValues.Clear();
            if (!keyboardChinese)
            {
                if (keyboardComposition != null) keyboardComposition.text = "英文输入";
            }
            else
            {
                if (keyboardComposition != null)
                    keyboardComposition.text = pinyinBuffer.Length == 0 ? "拼音输入" : pinyinBuffer;
                PinyinIme.Fill(pinyinBuffer, keyboardCandidateValues);
            }
            for (var index = 0; index < keyboardCandidateSlots.Count; index++)
            {
                var labelText = index < keyboardCandidateValues.Count
                    ? keyboardCandidateValues[index] : string.Empty;
                keyboardCandidateSlots[index].label.text = labelText;
                keyboardCandidateSlots[index].button.interactable = labelText.Length > 0;
            }
        }

        private void ChooseKeyboardCandidate(int index) => CommitKeyboardCandidate(index);

        private void CommitKeyboardCandidate(int index)
        {
            if (keyboardTarget == null || index < 0 || index >= keyboardCandidateValues.Count)
                return;
            keyboardTarget.text = (keyboardTarget.text ?? string.Empty) +
                                  keyboardCandidateValues[index];
            keyboardTarget.ForceLabelUpdate();
            pinyinBuffer = string.Empty;
            RefreshKeyboardCandidates();
        }

        private void KeyboardType(string value)
        {
            if (keyboardTarget == null) return;
            if (keyboardChinese && value.Length == 1 && PinyinIme.IsLetter(char.ToLowerInvariant(value[0])))
            {
                if (pinyinBuffer.Length >= 12) return;
                pinyinBuffer += char.ToLowerInvariant(value[0]);
                RefreshKeyboardCandidates();
                return;
            }
            if (keyboardChinese && pinyinBuffer.Length > 0) CommitKeyboardCandidate(0);
            // 内置键盘固定在光标末尾追加，避免依赖 TMP 在失焦后重置的 caret。
            keyboardTarget.text = (keyboardTarget.text ?? string.Empty) + value;
            keyboardTarget.ForceLabelUpdate();
        }

        private void KeyboardSpace()
        {
            if (keyboardChinese && pinyinBuffer.Length > 0)
            {
                CommitKeyboardCandidate(0);
                return;
            }
            KeyboardType(" ");
        }

        private void KeyboardBackspace()
        {
            if (keyboardTarget == null) return;
            if (keyboardChinese && pinyinBuffer.Length > 0)
            {
                pinyinBuffer = pinyinBuffer.Substring(0, pinyinBuffer.Length - 1);
                RefreshKeyboardCandidates();
                return;
            }
            var text = keyboardTarget.text ?? string.Empty;
            if (text.Length == 0) return;
            keyboardTarget.text = text.Substring(0, text.Length - 1);
            keyboardTarget.ForceLabelUpdate();
        }

        private void ToggleKeyboardShift()
        {
            keyboardShift = !keyboardShift;
            foreach (var entry in keyboardLetterKeys)
                entry.label.text = keyboardShift ? entry.value.ToUpperInvariant() : entry.value;
        }

        private void SendDialogue()
        {
            if (keyboardChinese && pinyinBuffer.Length > 0) CommitKeyboardCandidate(0);
            CloseKeyboard();
            if (voiceInput != null && voiceInput.IsCapturing)
            {
                voiceInput.Cancel();
                RefreshVoiceUi();
            }
            SendDialogueText(input.text);
        }

        private void SendDialogueText(string value)
        {
            if (!bootstrap.AIConfiguration.IsConfigured)
            {
                RefreshAIAvailability();
                return;
            }
#region debug-point A:send-button
            ReportDialogueHudAction("send");
#endregion
            RunAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                var conversation = bootstrap.Session.Conversation;
                if (conversation.State == MateSoul.Gameplay.ConversationState.Thinking)
                {
                    message = "冷月正在回复，请稍候。";
                    return;
                }
                var pending = conversation.SendAsync(value);
                if (conversation.State == MateSoul.Gameplay.ConversationState.Thinking)
                    input.text = "";
                await pending;
            });
        }

        private void ToggleVoiceInput()
        {
            if (voiceInput == null) return;
            if (voiceInput.IsCapturing)
            {
                voiceInput.Stop();
                RefreshVoiceUi();
                return;
            }
            if (!bootstrap.AIConfiguration.IsConfigured)
            {
                RefreshAIAvailability();
                return;
            }
            if (bootstrap.Session != null &&
                bootstrap.Session.Conversation.State == MateSoul.Gameplay.ConversationState.Thinking)
            {
                message = "冷月正在回复，请稍候。";
                return;
            }
            CloseKeyboard();
            input.text = string.Empty;
            voiceInput.Start();
            RefreshVoiceUi();
        }

        private void StopVoiceInput()
        {
            if (voiceInput == null || !voiceInput.IsCapturing) return;
            voiceInput.Cancel();
            RefreshVoiceUi();
        }

        private void OnVoicePartial(string text)
        {
            // 识别中间结果只放在占位提示里，避免每包结果都改写输入框触发额外回调。
            if (dialoguePlaceholder == null || string.IsNullOrEmpty(text)) return;
            dialoguePlaceholder.text = text;
        }

        private void OnVoiceCompleted(string text)
        {
            RefreshVoiceUi();
            if (string.IsNullOrWhiteSpace(text) || input == null) return;
            input.text = text;
            input.ForceLabelUpdate();
            SendDialogueText(text);
        }

        private void OnVoiceFailed(string error)
        {
            RefreshVoiceUi();
            message = error;
        }

        private void RefreshVoiceUi()
        {
            if (micButton == null) return;
            var phase = voiceInput == null ? SpeechToTextRecorder.Phase.Idle : voiceInput.State;
            var capturing = phase != SpeechToTextRecorder.Phase.Idle;
            micButton.GetComponent<Image>().color = capturing ? VoiceActiveColor : VoiceIdleColor;
            if (micLabel != null)
                micLabel.text = phase == SpeechToTextRecorder.Phase.Preparing ? "准备中" :
                    phase == SpeechToTextRecorder.Phase.Listening ? "停止" :
                    phase == SpeechToTextRecorder.Phase.Finishing ? "识别中" : "语音";
            if (dialoguePlaceholder != null)
                dialoguePlaceholder.text = capturing
                    ? "正在聆听，请说话…" : "输入想对冷月说的话…";
            RefreshAIAvailability();
        }

#region debug-point A:close-button
        private void CloseDialogue()
        {
            ReportDialogueHudAction("close");
            SetDialogueOpen(false);
        }

        private void ReportDialogueHudAction(string action)
        {
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            var conversation = bootstrap?.Session?.Conversation;
            var payload =
                $"{{\"sessionId\":\"dialogue-font-tofu\",\"runId\":\"pre-fix-iteration-3\"," +
                $"\"hypothesisId\":\"A\",\"location\":\"GameHud.{action}\"," +
                $"\"msg\":\"[DEBUG] dialogue HUD action\",\"data\":{{" +
                $"\"action\":\"{action}\",\"inputLength\":{input?.text?.Length ?? -1}," +
                $"\"conversationState\":\"{(conversation == null ? "no-session" : conversation.State.ToString())}\"," +
                $"\"sendInteractable\":{(sendButton != null && sendButton.interactable).ToString().ToLowerInvariant()}," +
                $"\"applicationFocused\":{Application.isFocused.ToString().ToLowerInvariant()}," +
                $"\"frame\":{Time.frameCount}}}}}";
            var request = UnityWebRequest.Post(
                "http://127.0.0.1:7785/event", payload, "application/json");
            var operation = request.SendWebRequest();
            operation.completed += _ => request.Dispose();
#endif
        }
#endregion

#region debug-point C:input-glyphs
        private void ReportInputGlyphCoverage(string value)
        {
            if (string.IsNullOrEmpty(value) || font == null) return;
            var covered = font.HasCharacters(value, out uint[] missingCharacters, true, true);
            var missingCodes = missingCharacters == null
                ? "" : string.Join(",", missingCharacters);
            var debugPayload = $"{{\"sessionId\":\"dialogue-font-tofu\",\"runId\":\"post-fix\"," +
                               $"\"hypothesisId\":\"C\",\"location\":\"GameHud.ReportInputGlyphCoverage\"," +
                               $"\"msg\":\"[DEBUG] input glyph coverage\",\"data\":{{" +
                               $"\"length\":{value.Length},\"covered\":{covered.ToString().ToLowerInvariant()}," +
                               $"\"missingCodes\":\"{missingCodes}\",\"populationMode\":\"{font.atlasPopulationMode}\"," +
                               $"\"characterCount\":{font.characterTable?.Count ?? -1}}}}}";
            UnityWebRequest.Post("http://127.0.0.1:7785/event", debugPayload, "application/json")
                .SendWebRequest();
        }
#endregion

        private void SwitchOutfit()
        {
            Run(() =>
            {
                var session = bootstrap.Session;
                var outfits = new[] { session.Wardrobe.InitialOutfitId }
                    .Concat(session.Wardrobe.Outfits.Select(o => o.OutfitId)).ToArray();
                var index = Array.IndexOf(outfits, session.Wardrobe.CurrentOutfitId);
                session.Submit(new AvatarIntent("outfit", IntentSource.UI,
                    payload: outfits[(index + 1) % outfits.Length]));
            });
        }

        private void VisitSelectedItem()
        {
            Run(() =>
            {
                var item = SelectedItem();
                if (item != null)
                    bootstrap.Session.Submit(new AvatarIntent("visit", IntentSource.UI, item.Instance.Id));
            });
        }

        private void RemoveSelectedItem()
        {
            Run(() =>
            {
                if (focusController == null)
                {
                    message = "回收选择器未初始化。";
                    return;
                }
                if (!focusController.RecycleSelectionActive)
                {
                    if (placementPreview != null) placementPreview.Cancel();
                    focusController.BeginRecycleSelection();
                    SetRecycleButtonLabel("确认回收");
                    message = "将一只手靠近物品，亮起后用另一只手点击确认回收。";
                    return;
                }
                var item = focusController.FocusedItem;
#region debug-point D:delete-selection
#if MATESOUL_ITEM_FOCUS_DIAGNOSTICS && DEVELOPMENT_BUILD && !UNITY_EDITOR
                var deletePayload = $"{{\"sessionId\":\"item-focus-interaction\",\"runId\":\"post-fix\"," +
                                    $"\"hypothesisId\":\"D\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                                    $"\"location\":\"GameHud.RemoveSelectedItem\"," +
                                    $"\"msg\":\"[DEBUG] delete button target resolved\",\"data\":{{" +
                                    $"\"focusId\":\"{focusController?.FocusedItem?.Instance?.Id ?? "none"}\"," +
                                    $"\"targetId\":\"{item?.Instance?.Id ?? "none"}\"" + "}}";
                UnityWebRequest.Post("http://127.0.0.1:7782/event", deletePayload, "application/json")
                    .SendWebRequest();
#endif
#endregion
                if (item == null)
                {
                    message = "手边没有可回收物品。";
                    return;
                }
                if (!bootstrap.Session.RecycleItem(item.Instance.Id)) return;
                CancelRecycleSelection();
            });
        }

        private void CancelRecycleSelection()
        {
            focusController?.CancelRecycleSelection();
            SetRecycleButtonLabel("回收当前物品");
        }

        private void SetRecycleButtonLabel(string value)
        {
            var label = recycleButton == null
                ? null : recycleButton.transform.Find("Label")?.GetComponent<TMP_Text>();
            if (label != null) label.text = value;
        }

        private void Recenter(bool immediate)
        {
            if (Application.isEditor) return;
            var activeCamera = Camera.main;
            if (activeCamera == null || canvasRoot == null || canvas == null) return;
            UpdateWorldPose(activeCamera.transform, immediate, Time.unscaledDeltaTime);
            canvas.worldCamera = activeCamera;
        }

        private void UpdateWorldPose(Transform cameraTransform, bool immediate, float deltaTime)
        {
            if (!immediate && hudInteractionActive) return;
            var forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
            var yaw = forward.sqrMagnitude >= .01f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                : hudPoseInitialized
                    ? hudFollowRotation
                    : Quaternion.Euler(0, cameraTransform.eulerAngles.y, 0);
            if (immediate || !hudPoseInitialized ||
                Quaternion.Angle(hudFollowRotation, yaw) > HudYawDeadZone ||
                Vector3.ProjectOnPlane(cameraTransform.position - hudFollowOrigin, Vector3.up)
                    .sqrMagnitude > HudTranslationDeadZone * HudTranslationDeadZone)
            {
                hudPoseInitialized = true;
                hudFollowOrigin = cameraTransform.position;
                hudFollowRotation = yaw;
            }
            // Height follows continuously; horizontal dead zones must not retain a crouched origin.
            hudFollowOrigin.y = cameraTransform.position.y;
            var headElevation = Mathf.Atan2(cameraTransform.forward.y, forward.magnitude) *
                                Mathf.Rad2Deg;
            // Smoothly approach the comfort limit so raising the head always starts returning.
            var elevation = -HudDownwardAngle + HudPitchFollowLimit *
                (float)Math.Tanh(headElevation * HudPitchFollowFraction / HudPitchFollowLimit);
            hudTargetRotation = hudFollowRotation * Quaternion.Euler(-elevation, 0, 0);
            var visibleCenter = CalculateVisibleCenter(hudFollowOrigin, hudTargetRotation);
            if (contentRoot != null && contentRoot.gameObject.activeSelf)
                visibleCenter -= Vector3.up * HudExpandedNavigationDrop;
            // Anchor the controls; expanded pages occupy the space above them.
            var referencePanel = dialogueOpen ? dialogueRoot : navigationRoot;
            var panelCenter = referencePanel == null ? Vector3.zero :
                canvasRoot.InverseTransformPoint(referencePanel.TransformPoint(referencePanel.rect.center));
            hudTargetPosition = visibleCenter -
                                hudTargetRotation * Vector3.Scale(panelCenter, canvasRoot.lossyScale);
            var amount = immediate ? 1f : 1f - Mathf.Exp(-7f * deltaTime);
            canvasRoot.position = Vector3.Lerp(canvasRoot.position, hudTargetPosition, amount);
            canvasRoot.rotation = Quaternion.Slerp(canvasRoot.rotation, hudTargetRotation, amount);
        }

        private static Vector3 CalculateVisibleCenter(Vector3 origin, Quaternion rotation)
            => origin + rotation * Vector3.forward * HudDistance +
               Vector3.up * HudVerticalOffset;

        private void LateUpdate()
        {
            AnimateSubtitleGap(Time.deltaTime);
            if (Application.isEditor) return;
            hudInteractionActive = false;
            if (EventSystem.current != null &&
                EventSystem.current.currentInputModule is XRUIInputModule module)
            {
                foreach (var interactor in hudPointers)
                {
                    if (!module.GetTrackedDeviceModel(interactor, out var model) ||
                        !(model.select || interactor is PicoHandPoke poke && poke.IsTouching(canvasRoot)) ||
                        model.currentRaycast.gameObject == null ||
                        !model.currentRaycast.gameObject.transform.IsChildOf(canvasRoot))
                        continue;
                    hudInteractionActive = true;
                    break;
                }
            }
            Recenter(false);
        }

        private GameItem SelectedItem()
        {
            if (focusController != null && focusController.RecycleSelectionActive)
                return focusController.FocusedItem;
            if (focusController != null && focusController.FocusedItem != null)
                return focusController.FocusedItem;
            var items = bootstrap.Session?.ItemViews.Where(i => i != null).ToArray();
            return items == null || items.Length == 0 ? null : items[selected % items.Length];
        }

        private void OnHandPlacementConfirmed(ItemDefinition definition)
            => RunAsync(() => PlaceAsync(definition));

        private async Task PlaceAsync(ItemDefinition definition)
        {
            var s = bootstrap.Session;
            if (definition.PlacementPreview)
            {
                if (placementPreview == null)
                    throw new InvalidOperationException("物品放置预览未初始化。");
                if (!placementPreview.IsPreviewing(definition))
                {
                    placementPreview.Begin(definition);
                    message = definition.PlacementSurface == ItemPlacementSurface.Wall
                        ? "把手伸到墙面附近，绿色时先张手，再握紧并松开确认；双手握拳1秒取消。"
                        : "移动手掌选择正下方位置，绿色时先张手，再握紧并松开确认；双手握拳1秒取消。";
                    if (interactionMode != null && !interactionMode.DirectHandsEnabled)
                        message = "选择绿色位置后按右手扳机确认，B键取消。";
                    return;
                }
                if (!placementPreview.CanPlace)
                {
                    message = $"{definition.DisplayName}投影无效，请选择绿色位置。";
                    return;
                }
                var before = s.GetRemainingQuantity(definition.Id);
                await s.PlaceItemAsync(definition.Id, placementPreview.Placement, false);
                if (s.GetRemainingQuantity(definition.Id) < before)
                    placementPreview.Cancel();
                return;
            }
            var origin = s.Motor.Position + Vector3.up * 1.6f;
            var forward = Vector3.forward;
            if (Camera.main != null)
            {
                origin = Camera.main.transform.position;
                forward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
            }
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            if (!s.TryResolveNearPlayerPlacement(definition,
                    new Pose(origin, Quaternion.LookRotation(forward)), out var placement))
            {
                message = "眼前没有可放置的平台，请看向附近桌面后重试。";
                return;
            }
            await s.PlaceItemAsync(definition.Id, placement, false, true);
        }

        private void RefreshCatalogQuantities()
        {
            // Runs 5x/s and allocates with LINQ per visible slot; tracked so a large
            // item count shows up as HUD cost instead of an unexplained spike.
            using var perf = PerfMonitor.Sample("Hud.Refresh");
            var session = bootstrap == null ? null : bootstrap.Session;
            for (var slot = 0; slot < catalogButtons.Count; slot++)
            {
                var index = catalogPage * CatalogPageSize + slot;
                if (index >= categoryCatalog.Length) continue;
                var definition = categoryCatalog[index];
                var remaining = session == null ? 0 :
                    session.GetRemainingQuantity(definition.Id);
                catalogQuantityLabels[slot].text =
                    (definition.UnlimitedInventory ? "可用 ∞" : $"可用 {remaining}") +
                    $"\n已放 {session?.ItemViews.Where(item => item.InventoryDefinitionId == definition.Id).Select(item => item.InventoryUnitId).Distinct().Count() ?? 0}";
                catalogButtons[slot].interactable = session != null &&
                    (definition.UnlimitedInventory || remaining > 0);
            }
        }

        private void Update()
        {
            voiceInput?.Tick();
            UpdateHudVisibilityInput();
#region debug-point D:eyehand-pointer-stall
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Time.unscaledTime >= debugNextUiTrace && navigationGroup != null && dialogueGroup != null)
            {
                debugNextUiTrace = Time.unscaledTime + 1f;
                ReportInputTrace($"{{\"sessionId\":\"eyehand-pointer-stall\",\"runId\":\"post-fix-2\"," +
                    $"\"hypothesisId\":\"D\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                    $"\"location\":\"GameHud.Update\",\"msg\":\"[DEBUG] UI access\",\"data\":{{" +
                    $"\"frame\":{Time.frameCount},\"focus\":{(Application.isFocused ? 1 : 0)}," +
                    $"\"visible\":{(hudVisible ? 1 : 0)},\"page\":{InputTraceString(activePage.ToString())}," +
                    $"\"transition\":{(dialogueTransition != null ? 1 : 0)}," +
                    $"\"navActive\":{(navigationRoot.gameObject.activeInHierarchy ? 1 : 0)}," +
                    $"\"navInteractable\":{(navigationGroup.interactable ? 1 : 0)}," +
                    $"\"navRaycasts\":{(navigationGroup.blocksRaycasts ? 1 : 0)}," +
                    $"\"dialogueActive\":{(dialogueRoot.gameObject.activeInHierarchy ? 1 : 0)}," +
                    $"\"dialogueInteractable\":{(dialogueGroup.interactable ? 1 : 0)}," +
                    $"\"dialogueRaycasts\":{(dialogueGroup.blocksRaycasts ? 1 : 0)}," +
                    $"\"sessionReady\":{(bootstrap?.Session != null ? 1 : 0)}}}}}");
            }
#endif
#endregion
            #region debug-point A-E:ui-quality-runtime-evidence
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            if (!uiQualityReported && Time.unscaledTime >= 2f)
            {
                uiQualityReported = true;
                ReportUiQuality();
            }
            if (!windowGlyphCoverageReported && Time.unscaledTime >= 2f)
            {
                windowGlyphCoverageReported = true;
                ReportWindowGlyphCoverage();
            }
#endif
            #endregion

            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .2f;
            var session = bootstrap == null ? null : bootstrap.Session;
            RefreshCatalogQuantities();
            RefreshAIAvailability();
            if (pickupAction != null && pickupAction.IsFinished)
            {
                message = pickupAction.Error ?? "物品拾取已结束，可再次选择物品。";
                pickupFeedback.text = message;
                pickupAction = null;
            }
            if (activePage == HudPage.PickupItems) RefreshPickupPage();
            if (eatAction != null && eatAction.IsFinished)
            {
                message = eatAction.Error ?? (eatAction.State == ActionState.Succeeded
                    ? "冷月吃完了。" : "吃东西已取消。");
                eatFeedback.text = message;
                eatAction = null;
                RefreshEatPage();
            }
            if (activePage == HudPage.EatFood) RefreshEatPage();
            if (interactionAction != null && interactionAction.IsFinished)
            {
                message = interactionAction.Error ?? (
                    interactionAction.Intent.ActionId == CutFruitAction.Id
                        ? (interactionAction.State == ActionState.Succeeded ? "水果已切好并装盘" : "切水果已取消") :
                    interactionAction.Intent.ActionId == "stay" ? "已起身" :
                    interactionAction.Intent.ActionId == RealityInteractionIds.Lie
                        ? "躺下互动已结束" : "落座互动已结束");
                interactionAction = null;
                if (interactionFeedback != null) interactionFeedback.text = message;
            }
            else if (interactionAction != null && interactionAction.Intent.ActionId == CutFruitAction.Id &&
                     interactionFeedback != null)
                interactionFeedback.text = "切水果：" + session.Avatar.Capture()
                    .FirstOrDefault(action => action.action_id == CutFruitAction.Id)?.phase;
            if (status != null)
                status.text = session == null ? bootstrap?.Status ?? "正在初始化…" :
                    $"{bootstrap.Status} · {(session.Paused ? "已暂停" : "陪伴中")}\n" +
                    $"心情：{session.Mood.ActiveId ?? "平静"}  ·  自主活动：{(session.Autonomy.Enabled ? "开启" : "关闭")}\n" +
                    $"{message ?? session.Notice}";
            if (selectedItem != null)
            {
                var item = SelectedItem();
                selectedItem.text = item == null
                    ? "房间内还没有虚拟物品"
                    : $"{(focusController != null && focusController.FocusedItem == item ? "瞄准物品" : "当前物品")}：" +
                      $"{item.Instance.DisplayName}（{session.Items.Count}）";
            }
            if (hudVisibilityHint != null) hudVisibilityHint.text = VisibilityHint();
            if (session == null) return;
            EnsurePersonalityControls(session);
            foreach (var trait in session.Persona.Traits)
            {
                var option = session.Persona.SelectedOptionDefinition(trait.id);
                personalityLabels[trait.id].text =
                    $"{trait.display_name}：{option.display_name}";
            }
            if (subtitle != null)
            {
                subtitle.text = !session.Subtitles ? "" :
                    session.SpontaneousReplies.VisibleText ??
                    bootstrap.MiniGames.Active?.DialogueLine ??
                    (session.Conversation.State == MateSoul.Gameplay.ConversationState.Thinking ? "正在想…" :
                    session.Conversation.Text ?? "");
                UpdateSubtitleGap();
            }
            if (inputModeLabel != null && bootstrap.MiniGames.Input != null)
            {
                var mode = bootstrap.MiniGames.Input;
                inputModeLabel.text = mode.DirectHandsEnabled
                    ? "菜单：眼手点选 / 触碰；物品：近距握取"
                    : "交互：编辑器手柄模拟（点击切换）";
            }
            if (autonomyLabel != null)
                autonomyLabel.text = $"自主活动：{(session.Autonomy.Enabled ? "开启" : "关闭")}";
            if (pauseLabel != null) pauseLabel.text = session.Paused ? "继续" : "暂停";
        }

        #region debug-point A-E:ui-quality-runtime-evidence
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        private void ReportUiQuality()
        {
            const string endpoint = "http://127.0.0.1:7783/event";
            var navText = GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(value => value.name == "功能名称");
            var icons = GetComponentsInChildren<Image>(true)
                .Where(value => value.name == "功能图标").ToArray();
            var icon = icons.FirstOrDefault();
            var importedIcon = icons.Skip(1).FirstOrDefault();
            var scaler = canvas == null ? null : canvas.GetComponent<CanvasScaler>();
            var camera = Camera.main;
            var corners = new Vector3[4];
            if (navText != null) navText.rectTransform.GetWorldCorners(corners);
            var projectedHeight = camera == null || navText == null
                ? -1f
                : Vector3.Distance(camera.WorldToScreenPoint(corners[0]),
                    camera.WorldToScreenPoint(corners[1]));
            var worldHeight = navText == null ? -1f : Vector3.Distance(corners[0], corners[1]);
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            var renderScaleProperty = pipeline?.GetType().GetProperty("renderScale");
            var msaaProperty = pipeline?.GetType().GetProperty("msaaSampleCount");
            var renderScale = renderScaleProperty == null
                ? -1f
                : Convert.ToSingle(renderScaleProperty.GetValue(pipeline));
            var pipelineMsaa = msaaProperty == null
                ? -1
                : Convert.ToInt32(msaaProperty.GetValue(pipeline));
            var pico = FindObjectOfType<ByteDance.PICO.XR.PXR_Manager>();
            var textTexture = navText?.fontSharedMaterial?.mainTexture;
            var fontAtlas = navText?.font?.atlasTextures?.FirstOrDefault();
            var textMaterial = navText == null ? null : navText.materialForRendering;
            var renderedTextTexture = textMaterial?.mainTexture;
            var textColor = navText == null ? Color.clear : navText.color;
            var faceColor = textMaterial != null && textMaterial.HasProperty(ShaderUtilities.ID_FaceColor)
                ? textMaterial.GetColor(ShaderUtilities.ID_FaceColor)
                : Color.clear;
            var gradientScale = textMaterial != null &&
                                textMaterial.HasProperty(ShaderUtilities.ID_GradientScale)
                ? textMaterial.GetFloat(ShaderUtilities.ID_GradientScale)
                : -1f;
            var textInfo = navText?.textInfo;
            var firstCharacterVisible = textInfo != null && textInfo.characterCount > 0 &&
                                        textInfo.characterInfo[0].isVisible;
            var textInfoVertexCount = textInfo == null || textInfo.meshInfo.Length == 0
                ? -1
                : textInfo.meshInfo[0].vertexCount;
            var zTestMode = textMaterial != null &&
                            textMaterial.HasProperty(ShaderUtilities.ShaderTag_ZTestMode)
                ? textMaterial.GetFloat(ShaderUtilities.ShaderTag_ZTestMode)
                : -1f;
            var iconTexture = icon == null ? null : icon.mainTexture;
            var importedIconTexture = importedIcon == null ? null : importedIcon.mainTexture;

            var a =
                $"{{\"sessionId\":\"xr-ui-blur\",\"runId\":\"post-fix\",\"hypothesisId\":\"A\"," +
                $"\"location\":\"GameHud.ReportUiQuality:text\",\"msg\":\"[DEBUG] text raster metrics\"," +
                $"\"data\":{{\"fontSize\":{navText?.fontSize ?? -1},\"pixelsPerUnit\":{navText?.pixelsPerUnit ?? -1:F3}," +
                $"\"fontMode\":\"{navText?.font?.atlasPopulationMode}\",\"fontAtlasWidth\":{fontAtlas?.width ?? -1}," +
                $"\"fontAtlasHeight\":{fontAtlas?.height ?? -1},\"materialTextureWidth\":{textTexture?.width ?? -1}," +
                $"\"materialTextureHeight\":{textTexture?.height ?? -1},\"atlasCount\":{navText?.font?.atlasTextureCount ?? -1}," +
                $"\"fontAsset\":\"{navText?.font?.name}\",\"sourceFont\":\"{navText?.font?.sourceFontFile?.name}\"," +
                $"\"fontCharacters\":{navText?.font?.characterTable?.Count ?? -1}," +
                $"\"textLength\":{navText?.text?.Length ?? -1},\"textCharacters\":{navText?.textInfo?.characterCount ?? -1}," +
                $"\"rectHeight\":{navText?.rectTransform.rect.height ?? -1:F3}," +
                $"\"requiredLineHeight\":{(navText == null ? -1f : navText.fontSize * navText.font.faceInfo.lineHeight / navText.font.faceInfo.pointSize):F3}}}}}";
            UnityWebRequest.Post(endpoint, a, "application/json").SendWebRequest();

            var b =
                $"{{\"sessionId\":\"xr-ui-blur\",\"runId\":\"post-fix\",\"hypothesisId\":\"B\"," +
                $"\"location\":\"GameHud.ReportUiQuality:xr\",\"msg\":\"[DEBUG] XR render metrics\"," +
                $"\"data\":{{\"screenWidth\":{Screen.width},\"screenHeight\":{Screen.height}," +
                $"\"eyeWidth\":{UnityEngine.XR.XRSettings.eyeTextureWidth}," +
                $"\"eyeHeight\":{UnityEngine.XR.XRSettings.eyeTextureHeight}," +
                $"\"eyeScale\":{UnityEngine.XR.XRSettings.eyeTextureResolutionScale:F3}," +
                $"\"urpRenderScale\":{renderScale:F3},\"quality\":\"{QualitySettings.names[QualitySettings.GetQualityLevel()]}\"," +
                $"\"qualityMsaa\":{QualitySettings.antiAliasing},\"pipelineMsaa\":{pipelineMsaa}," +
                $"\"cameraPixelWidth\":{camera?.pixelWidth ?? -1},\"cameraPixelHeight\":{camera?.pixelHeight ?? -1}," +
                $"\"cameraMsaa\":\"{camera?.allowMSAA}\",\"picoRecommendedMsaa\":\"{pico?.useRecommendedAntiAliasingLevel}\"" +
                "}}";
            UnityWebRequest.Post(endpoint, b, "application/json").SendWebRequest();

            var c =
                $"{{\"sessionId\":\"xr-ui-blur\",\"runId\":\"post-fix\",\"hypothesisId\":\"C\"," +
                $"\"location\":\"GameHud.ReportUiQuality:projection\",\"msg\":\"[DEBUG] projected text size\"," +
                $"\"data\":{{\"canvasScaleFactor\":\"{canvas?.scaleFactor ?? -1:F3}\"," +
                $"\"dynamicPixelsPerUnit\":\"{scaler?.dynamicPixelsPerUnit ?? -1:F3}\"," +
                $"\"projectedTextHeight\":\"{projectedHeight:F3}\",\"worldTextHeight\":\"{worldHeight:F5}\"," +
                $"\"canvasWorldScale\":\"{canvasRoot?.lossyScale.x ?? -1:F6}\",\"isRootCanvas\":\"{canvas?.isRootCanvas}\"," +
                $"\"cameraDistance\":\"{(camera == null || canvasRoot == null ? -1f : Vector3.Distance(camera.transform.position, canvasRoot.position)):F3}\"}}}}";
            UnityWebRequest.Post(endpoint, c, "application/json").SendWebRequest();

            var d =
                $"{{\"sessionId\":\"xr-ui-blur\",\"runId\":\"post-fix\",\"hypothesisId\":\"D\"," +
                $"\"location\":\"GameHud.ReportUiQuality:material\",\"msg\":\"[DEBUG] UI material metrics\"," +
                $"\"data\":{{\"textShader\":\"{textMaterial?.shader?.name}\"," +
                $"\"overlayShader\":\"{overlayMaterial?.shader?.name}\",\"textureFilter\":\"{textTexture?.filterMode}\"," +
                $"\"textureMips\":{textTexture?.mipmapCount ?? -1},\"textOverlay\":\"{navText?.isOverlay}\"," +
                $"\"renderTextureWidth\":{renderedTextTexture?.width ?? -1},\"renderTextureHeight\":{renderedTextTexture?.height ?? -1}," +
                $"\"textLength\":{navText?.text?.Length ?? -1},\"textCharacters\":{textInfo?.characterCount ?? -1}," +
                $"\"textInfoVertexCount\":{textInfoVertexCount},\"firstCharacterVisible\":\"{firstCharacterVisible}\"," +
                $"\"rendererCull\":\"{navText?.canvasRenderer.cull}\",\"zTestMode\":{zTestMode:F1}," +
                $"\"textColor\":\"{textColor.r:F3},{textColor.g:F3},{textColor.b:F3},{textColor.a:F3}\"," +
                $"\"faceColor\":\"{faceColor.r:F3},{faceColor.g:F3},{faceColor.b:F3},{faceColor.a:F3}\"," +
                $"\"rendererAlpha\":\"{navText?.canvasRenderer?.GetAlpha() ?? -1:F3}\"," +
                $"\"gradientScale\":\"{gradientScale:F3}\",\"vertexCount\":{navText?.mesh?.vertexCount ?? -1}}}}}";
            UnityWebRequest.Post(endpoint, d, "application/json").SendWebRequest();

            var e =
                $"{{\"sessionId\":\"xr-ui-blur\",\"runId\":\"post-fix\",\"hypothesisId\":\"E\"," +
                $"\"location\":\"GameHud.ReportUiQuality:icon\",\"msg\":\"[DEBUG] icon sampling metrics\"," +
                $"\"data\":{{\"iconWidth\":{iconTexture?.width ?? -1},\"iconHeight\":{iconTexture?.height ?? -1}," +
                $"\"iconFilter\":\"{iconTexture?.filterMode}\",\"iconMips\":{iconTexture?.mipmapCount ?? -1}," +
                $"\"importedIconWidth\":{importedIconTexture?.width ?? -1},\"importedIconHeight\":{importedIconTexture?.height ?? -1}," +
                $"\"importedIconMips\":{importedIconTexture?.mipmapCount ?? -1}," +
                $"\"picoSuperResolution\":\"{pico?.enableSuperResolution}\",\"picoAdaptiveResolution\":\"{pico?.adaptiveResolution}\"," +
                $"\"picoFoveation\":\"{pico?.foveationLevel}\",\"picoSharpening\":\"{pico?.sharpeningMode}\"" +
                "}}";
            UnityWebRequest.Post(endpoint, e, "application/json").SendWebRequest();
        }

        private void ReportWindowGlyphCoverage()
        {
            const string endpoint = "http://127.0.0.1:7784/event";
            var targets = new[]
            {
                new { Page = HudPage.Items, Hypothesis = "A,B" },
                new { Page = HudPage.Settings, Hypothesis = "A,B,D" }
            };
            foreach (var target in targets)
            {
                var labels = pages[target.Page].GetComponentsInChildren<TMP_Text>(true)
                    .Where(text => text != null && !string.IsNullOrEmpty(text.text))
                    .ToArray();
                var combined = string.Concat(labels.Select(text => text.text));
                var covered = font.HasCharacters(
                    combined, out uint[] missing, false, false);
                missing ??= Array.Empty<uint>();
                var missingCodes = string.Join(",", missing.Select(code => $"U+{code:X4}"));
                var windowText = string.Join("|", labels.Select(text =>
                    text.text.Replace("\\", "\\\\").Replace("\"", "\\\"")
                        .Replace("\r", "").Replace("\n", "\\n")));
                var payload =
                    $"{{\"sessionId\":\"window-missing-glyphs\",\"runId\":\"pre-fix\"," +
                    $"\"hypothesisId\":\"{target.Hypothesis}\",\"location\":\"GameHud.ReportWindowGlyphCoverage\"," +
                    $"\"msg\":\"[DEBUG] window font coverage\",\"data\":{{\"page\":\"{target.Page}\"," +
                    $"\"labelCount\":{labels.Length},\"fontCharacters\":{font.characterTable.Count}," +
                    $"\"covered\":\"{covered}\",\"missingCount\":{missing.Length}," +
                    $"\"missingCodes\":\"{missingCodes}\",\"windowText\":\"{windowText}\"}}}}";
                UnityWebRequest.Post(endpoint, payload, "application/json").SendWebRequest();
            }
        }
#endif
        #endregion

        private void Run(Action action)
        {
            if (bootstrap.Session == null || bootstrap.Session.Paused) return;
            try { message = null; action(); }
            catch (Exception error) { message = error.Message; }
        }
        private async void RetryLocalization()
        {
            if (bootstrap.Session?.Paused == true) return;
            try { message = null; await bootstrap.RetryLocalizationAsync(); }
            catch (Exception error) { message = error.Message; }
        }
        private async void RunAsync(Func<Task> action)
        {
            if (bootstrap.Session == null || bootstrap.Session.Paused) return;
            try { message = null; await action(); }
            catch (Exception error) { message = error.Message; }
        }

        private RectTransform Rect(string title, float x, float y, float w, float h, Transform parent)
        {
            var rect = new GameObject(title, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        private RectTransform AnchoredRect(string title, Transform parent, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(title, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private RectTransform StretchRect(string title, Transform parent)
        {
            return AnchoredRect(title, parent, Vector2.zero, Vector2.one,
                new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
        }

        private Image AddBackground(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            AlwaysOnTopUi.Apply(image, overlayMaterial);
            image.color = color;
            if (roundedSprite != null)
            {
                image.sprite = roundedSprite;
                image.type = Image.Type.Sliced;
            }
            return image;
        }

        private Image AddPanelBackground(RectTransform rect, Color color)
        {
            var image = AddBackground(rect, color);
            image.sprite = panelSprite;
            return image;
        }

        private RectTransform PanelCard(string title, float x, float y, float width, float height,
            Transform parent)
        {
            var rect = Rect(title, x, y, width, height, parent);
            AddPanelBackground(rect, CardColor).raycastTarget = false;
            return rect;
        }

        private void PageHeader(Transform parent, string title, string description,
            string objectName = "页面标题")
        {
            var accent = Rect("标题色块", 28, 22, 4, 28, parent);
            AddPanelBackground(accent, SelectedColor).raycastTarget = false;
            Label(title, 44, 16, 506, 38, 26, parent,
                TextAnchor.MiddleLeft, TextColor, objectName);
            Label(description, 28, 58, 704, 26, 17, parent,
                TextAnchor.MiddleLeft, MutedTextColor, "页面说明");
        }

        private void TutorialStep(Transform parent, int number, float y, string title, string body)
        {
            var card = PanelCard("配置步骤/" + number, 28, y, 704, 98, parent);
            var badge = PanelCard("步骤序号", 16, 20, 46, 46, card);
            badge.GetComponent<Image>().color = QuietColor;
            Label(number.ToString("00"), 0, 0, 46, 46, 19, badge,
                TextAnchor.MiddleCenter, SelectedColor);
            Label(title, 80, 10, 606, 30, 20, card, TextAnchor.MiddleLeft, TextColor);
            Label(body, 80, 40, 606, 54, 17, card, TextAnchor.MiddleLeft, MutedTextColor);
        }

        private HudPanelButton PanelButton(string title, float x, float y, float width, float height,
            UnityEngine.Events.UnityAction action, Transform parent, int fontSize,
            string objectName = null, bool primary = false, bool quiet = false, bool danger = false)
        {
            var rect = Rect(objectName ?? title, x, y, width, height, parent);
            var background = primary ? ButtonColor : danger ? DangerColor : quiet ? QuietColor : CardColor;
            var foreground = primary ? PrimaryTextColor : TextColor;
            var image = AddPanelBackground(rect, background);
            var label = Label(title, 12, 0, width - 24, height, fontSize, rect,
                TextAnchor.MiddleCenter, foreground, "Label");
            var button = rect.gameObject.AddComponent<HudPanelButton>();
            button.Configure(image, label, background, foreground,
                primary || quiet ? SelectedColor : CardHoverColor,
                primary || quiet ? PrimaryTextColor : TextColor);
            button.onClick.AddListener(action);
            return button;
        }

        private Sprite CreatePanelSprite()
        {
            const int size = 128;
            const float radius = 24;
            panelTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "MateSoul Panel Soft Corners",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var dx = Mathf.Max(Mathf.Abs(x + .5f - size * .5f) - (size * .5f - radius), 0);
                    var dy = Mathf.Max(Mathf.Abs(y + .5f - size * .5f) - (size * .5f - radius), 0);
                    var alpha = Mathf.Clamp01(radius + .5f - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255));
                }
            panelTexture.SetPixels32(pixels);
            panelTexture.Apply(false, true);
            return Sprite.Create(panelTexture, new Rect(0, 0, size, size), new Vector2(.5f, .5f),
                100, 0, SpriteMeshType.FullRect, Vector4.one * radius);
        }

        private Sprite CreateRoundedSprite()
        {
            const int size = 48;
            const int radius = 11;
            roundedTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "MateSoul UI Rounded Rectangle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var centerX = x < radius ? radius : x >= size - radius ? size - radius - 1 : x;
                    var centerY = y < radius ? radius : y >= size - radius ? size - radius - 1 : y;
                    var dx = x - centerX;
                    var dy = y - centerY;
                    var inside = dx * dx + dy * dy <= radius * radius;
                    pixels[y * size + x] = new Color32(255, 255, 255, inside ? (byte)255 : (byte)0);
                }
            }
            roundedTexture.SetPixels32(pixels);
            roundedTexture.Apply(false, true);
            return Sprite.Create(roundedTexture, new Rect(0, 0, size, size), new Vector2(.5f, .5f),
                100, 0, SpriteMeshType.FullRect, Vector4.one * radius);
        }

        private Sprite CreateIconSprite(HudPage page)
        {
            var referencePath = ReferenceIconPath(page);
            if (referencePath != null)
            {
                var referenceTexture = Resources.Load<Texture2D>(referencePath);
                if (referenceTexture == null)
                    throw new InvalidOperationException($"Missing HUD icon resource: {referencePath}");
                referenceTexture.filterMode = FilterMode.Bilinear;
                referenceTexture.wrapMode = TextureWrapMode.Clamp;
                var referenceSprite = Sprite.Create(referenceTexture,
                    new Rect(0, 0, referenceTexture.width, referenceTexture.height),
                    new Vector2(.5f, .5f), 100);
                iconSprites.Add(referenceSprite);
                return referenceSprite;
            }

            const int size = 96;
            const int samples = 4;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = $"MateSoul UI {page} Icon",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var hits = 0;
                    for (var sy = 0; sy < samples; sy++)
                    {
                        for (var sx = 0; sx < samples; sx++)
                        {
                            var px = ((x + (sx + .5f) / samples) / size) * 2 - 1;
                            var py = ((y + (sy + .5f) / samples) / size) * 2 - 1;
                            if (InsideIcon(page, px, py)) hits++;
                        }
                    }
                    pixels[y * size + x] = new Color32(255, 251, 226,
                        (byte)Mathf.RoundToInt(255f * hits / (samples * samples)));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100);
            iconTextures.Add(texture);
            iconSprites.Add(sprite);
            return sprite;
        }

        private static string ReferenceIconPath(HudPage page)
        {
            switch (page)
            {
                case HudPage.Interaction: return "MateSoulUI/Interaction";
                case HudPage.Information: return "MateSoulUI/Information";
                case HudPage.Items: return "MateSoulUI/Items";
                default: return null;
            }
        }

        private static bool InsideIcon(HudPage page, float x, float y)
        {
            switch (page)
            {
                case HudPage.Dialogue:
                {
                    var bubble = x * x / (.72f * .72f) + (y - .12f) * (y - .12f) / (.5f * .5f) <= 1;
                    var tail = InsideTriangle(x, y, -.5f, -.2f, -.62f, -.7f, -.08f, -.34f);
                    var holes = InsideCircle(x, y, -.27f, .14f, .075f) ||
                                InsideCircle(x, y, 0, .14f, .075f) ||
                                InsideCircle(x, y, .27f, .14f, .075f);
                    return (bubble || tail) && !holes;
                }
                case HudPage.Interaction:
                {
                    var palm = InsideRoundedRect(x, y, 0, -.2f, .38f, .4f, .16f);
                    var fingers = InsideCapsule(x, y, -.29f, .06f, -.29f, .61f, .08f) ||
                                  InsideCapsule(x, y, -.1f, .1f, -.1f, .79f, .08f) ||
                                  InsideCapsule(x, y, .1f, .1f, .1f, .84f, .08f) ||
                                  InsideCapsule(x, y, .29f, .06f, .29f, .7f, .08f);
                    var thumb = InsideCapsule(x, y, -.29f, -.06f, -.62f, .27f, .12f);
                    var wrist = InsideRoundedRect(x, y, 0, -.7f, .27f, .21f, .08f);
                    return palm || fingers || thumb || wrist;
                }
                case HudPage.Information:
                    return InsideCircle(x, y, 0, .57f, .15f) ||
                           InsideRoundedRect(x, y, 0, -.13f, .12f, .48f, .1f) ||
                           InsideRoundedRect(x, y, 0, -.66f, .36f, .09f, .05f);
                case HudPage.Items:
                {
                    var body = InsideRoundedRect(x, y, 0, -.12f, .59f, .58f, .17f);
                    var handle = InsideRoundedRect(x, y, 0, .58f, .25f, .2f, .11f) &&
                                 !InsideRoundedRect(x, y, 0, .57f, .12f, .09f, .04f);
                    var seam = InsideRoundedRect(x, y, 0, -.12f, .43f, .035f, .02f);
                    var pocket = InsideRoundedRect(x, y, 0, -.47f, .28f, .09f, .04f);
                    return (body || handle) && !seam && !pocket;
                }
                case HudPage.Settings:
                {
                    var radius = Mathf.Sqrt(x * x + y * y);
                    var shape = radius <= .48f;
                    for (var i = 0; i < 8; i++)
                    {
                        var angle = i * Mathf.PI * .25f;
                        var localX = x * Mathf.Cos(angle) + y * Mathf.Sin(angle);
                        var localY = -x * Mathf.Sin(angle) + y * Mathf.Cos(angle);
                        shape |= InsideRoundedRect(localX, localY, 0, .57f, .12f, .19f, .045f);
                    }
                    return shape && radius >= .2f;
                }
                default:
                    return false;
            }
        }

        private static bool InsideCircle(float x, float y, float centerX, float centerY, float radius)
        {
            var dx = x - centerX;
            var dy = y - centerY;
            return dx * dx + dy * dy <= radius * radius;
        }

        private static bool InsideRoundedRect(float x, float y, float centerX, float centerY,
            float halfWidth, float halfHeight, float radius)
        {
            var qx = Mathf.Abs(x - centerX) - (halfWidth - radius);
            var qy = Mathf.Abs(y - centerY) - (halfHeight - radius);
            return Mathf.Max(qx, qy) <= 0 ||
                   Mathf.Max(qx, 0) * Mathf.Max(qx, 0) + Mathf.Max(qy, 0) * Mathf.Max(qy, 0) <=
                   radius * radius;
        }

        private static bool InsideCapsule(float x, float y, float ax, float ay,
            float bx, float by, float radius)
        {
            var abx = bx - ax;
            var aby = by - ay;
            var lengthSquared = abx * abx + aby * aby;
            var amount = Mathf.Clamp01(((x - ax) * abx + (y - ay) * aby) / lengthSquared);
            var dx = x - (ax + abx * amount);
            var dy = y - (ay + aby * amount);
            return dx * dx + dy * dy <= radius * radius;
        }

        private static bool InsideTriangle(float x, float y, float ax, float ay,
            float bx, float by, float cx, float cy)
        {
            var d1 = (x - bx) * (ay - by) - (ax - bx) * (y - by);
            var d2 = (x - cx) * (by - cy) - (bx - cx) * (y - cy);
            var d3 = (x - ax) * (cy - ay) - (cx - ax) * (y - ay);
            return (d1 >= 0 && d2 >= 0 && d3 >= 0) || (d1 <= 0 && d2 <= 0 && d3 <= 0);
        }

        private TMP_Text Label(string value, float x, float y, float w, float h, int size,
            Transform parent, TextAnchor alignment, Color color, string objectName = "Label")
        {
            var font = AlwaysOnTopUi.TextFont;
            var minimumHeight = Mathf.Ceil(
                size * font.faceInfo.lineHeight / font.faceInfo.pointSize) + 1f;
            var text = AlwaysOnTopUi.ConfigureText(
                Rect(objectName, x, y, w, Mathf.Max(h, minimumHeight), parent)
                    .gameObject.AddComponent<TextMeshProUGUI>());
            text.fontSize = size;
            text.text = value;
            text.color = color;
            text.alignment = ToTmpAlignment(alignment);
            text.raycastTarget = false;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Truncate;
            text.ForceMeshUpdate(true, true);
            return text;
        }

        private static TextAlignmentOptions ToTmpAlignment(TextAnchor alignment)
        {
            switch (alignment)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                default: return TextAlignmentOptions.Center;
            }
        }

        private Button NavigationButton(string title, HudPage icon, float x, float width,
            Transform parent, UnityEngine.Events.UnityAction action)
        {
            var rect = Rect(title, x, 8, width, 127, parent);
            var image = AddBackground(rect, ButtonColor);
            var shadow = rect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(.08f, .04f, .09f, .38f);
            shadow.effectDistance = new Vector2(4, -4);
            shadow.useGraphicAlpha = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.onClick.AddListener(action);
            ConfigureButton(button, image);

            var iconRect = Rect("功能图标", width * .5f - 40, 7, 80, 80, rect);
            var iconImage = iconRect.gameObject.AddComponent<Image>();
            AlwaysOnTopUi.Apply(iconImage, overlayMaterial);
            iconImage.sprite = CreateIconSprite(icon);
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            var iconShadow = iconRect.gameObject.AddComponent<Shadow>();
            iconShadow.effectColor = new Color(.15f, .08f, .16f, .18f);
            iconShadow.effectDistance = new Vector2(1, -1);
            iconShadow.useGraphicAlpha = true;
            Label(title, 8, 96, width - 16, 25, 19, rect,
                TextAnchor.MiddleCenter, TextColor, "功能名称");
            return button;
        }

        private Button Button(string title, float x, float y, float width, float height,
            UnityEngine.Events.UnityAction action, Transform parent, int fontSize,
            string objectName = null)
        {
            var rect = Rect(objectName ?? title, x, y, width, height, parent);
            var image = AddBackground(rect, ButtonColor);
            var button = rect.gameObject.AddComponent<Button>();
            button.onClick.AddListener(action);
            ConfigureButton(button, image);
            Label(title, 0, 0, width, height, fontSize, rect,
                TextAnchor.MiddleCenter, TextColor, "Label");
            return button;
        }

        private static void ConfigureButton(Button button, Graphic target)
        {
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, .94f, 1f, 1);
            colors.pressedColor = new Color(.82f, .82f, .82f, 1);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(.55f, .55f, .55f, .6f);
            colors.colorMultiplier = 1;
            colors.fadeDuration = .08f;
            button.colors = colors;
            button.targetGraphic = target;
        }

        private void OnDestroy()
        {
#region debug-point D:eyehand-pointer-stall
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (debugUiModule != null)
            {
                debugUiModule.pointerDown -= TraceInputDown;
                debugUiModule.pointerUp -= TraceInputUp;
                debugUiModule.pointerClick -= TraceInputClick;
            }
#endif
#endregion
            if (placementPreview != null)
            {
                placementPreview.ConfirmRequested -= OnHandPlacementConfirmed;
                placementPreview.ActiveChanged -= OnPlacementActiveChanged;
            }
            hudLifetime.Cancel();
            hudLifetime.Dispose();
            voiceInput?.Dispose();
            CancelRecycleSelection();
            if (dialogueTransition != null) StopCoroutine(dialogueTransition);
            if (panelTransition != null) StopCoroutine(panelTransition);
            bootstrap?.Session?.SetDialogueAttention(false);
            controllerHudToggle?.Dispose();
            if (overlayMaterial != null) Destroy(overlayMaterial);
            foreach (var sprite in iconSprites) if (sprite != null) Destroy(sprite);
            foreach (var texture in iconTextures) if (texture != null) Destroy(texture);
            if (roundedSprite != null) Destroy(roundedSprite);
            if (roundedTexture != null) Destroy(roundedTexture);
            if (panelSprite != null) Destroy(panelSprite);
            if (panelTexture != null) Destroy(panelTexture);
        }
    }
}
