using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MateSoul.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
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
            Information,
            Items,
            Settings
        }

        private static readonly Color DockColor = new Color(.22f, .14f, .22f, .52f);
        private static readonly Color PanelColor = new Color(.14f, .10f, .16f, .92f);
        private static readonly Color ButtonColor = new Color(.82f, .66f, .80f, .86f);
        private static readonly Color SelectedColor = new Color(.86f, .72f, .84f, .94f);
        private static readonly Color TextColor = new Color(.98f, .95f, .98f);
        private static readonly Color MutedTextColor = new Color(.77f, .72f, .79f);
        private static readonly Color DialogueGlassColor = new Color(.72f, .63f, .86f, .68f);
        private static readonly Color DialogueHazeColor = new Color(.98f, .95f, 1f, .14f);
        private static readonly Color DialogueInputColor = new Color(.27f, .20f, .34f, .42f);
        private static readonly Color DialogueSendColor = new Color(.59f, .42f, .77f, .92f);
        private static readonly Color DialogueCloseColor = new Color(.34f, .28f, .40f, .82f);
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

        [SerializeField] private GameBootstrap bootstrap;
        private readonly Dictionary<HudPage, GameObject> pages = new Dictionary<HudPage, GameObject>();
        private readonly Dictionary<HudPage, Image> navigationBackgrounds = new Dictionary<HudPage, Image>();
        private readonly Dictionary<string, TMP_Text> personalityLabels = new Dictionary<string, TMP_Text>();
        private readonly List<Sprite> iconSprites = new List<Sprite>();
        private readonly List<Texture2D> iconTextures = new List<Texture2D>();
        private readonly List<Button> catalogButtons = new List<Button>();
        private readonly List<TMP_Text> catalogQuantityLabels = new List<TMP_Text>();
        private readonly List<Image> catalogCategoryBackgrounds = new List<Image>();
        private ItemDefinition[] catalog = Array.Empty<ItemDefinition>();
        private ItemDefinition[] categoryCatalog = Array.Empty<ItemDefinition>();
        private TMP_Text status, subtitle, selectedItem, interactionFeedback, catalogPageLabel;
        private TMP_Text autonomyLabel, pauseLabel, inputModeLabel;
        private TMP_Text hudVisibilityHint;
        private TMP_InputField input;
        private Button sendButton, recycleButton;
        private Canvas canvas;
        private TMP_FontAsset font;
        private Material overlayMaterial;
        private VirtualItemFocusController focusController;
        private ItemPlacementPreviewController placementPreview;
        private Sprite roundedSprite;
        private Texture2D roundedTexture;
        private RectTransform canvasRoot, hudRoot, contentRoot;
        private RectTransform navigationRoot, dialogueRoot;
        private RectTransform personalitySettingsRoot, systemSettingsRoot;
        private CanvasGroup navigationGroup, dialogueGroup;
        private HudPage activePage;
        private PlayerInteractionModeController interactionMode;
        private InputAction controllerHudToggle, leftHandFist, rightHandFist;
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


        public bool IsVisible => hudVisible;
        public bool IsDialogueOpen => dialogueOpen;

        public void Configure(GameBootstrap game) { bootstrap = game; }

        private void Start()
        {
            focusController = bootstrap == null ? null : bootstrap.GetComponent<VirtualItemFocusController>();
            placementPreview = bootstrap == null
                ? null : bootstrap.GetComponent<ItemPlacementPreviewController>();
            interactionMode = FindObjectOfType<PlayerInteractionModeController>();
            CreateHudToggleActions();
            font = AlwaysOnTopUi.TextFont;
            roundedSprite = CreateRoundedSprite();
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
                    new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 20), new Vector2(920, 620));
            }
            else
            {
                canvas.renderMode = RenderMode.WorldSpace;
                canvasRoot.sizeDelta = new Vector2(920, 620);
                canvasRoot.localScale = Vector3.one * .00108f;
                hudRoot = StretchRect("Bottom HUD", root.transform);
                Recenter(true);
            }
            AlwaysOnTopUi.Configure(canvas);

            BuildNavigation();
            BuildDialogueBar();
            BuildSubtitle();
            BuildPages();
            AlwaysOnTopUi.Apply(root.transform, overlayMaterial);
            SetActivePage(HudPage.None);
            SetHudVisible(true);

            if (EventSystem.current == null)
            {
                var events = new GameObject("UI Event System", typeof(EventSystem), typeof(XRUIInputModule));
                events.transform.SetParent(transform, false);
            }
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

            var inputRoot = Rect("对话输入", 18, 20, 568, 72, dialogueRoot);
            AddBackground(inputRoot, DialogueInputColor);
            input = inputRoot.gameObject.AddComponent<TMP_InputField>();
            var value = Label("", 18, 0, 532, 72, 21, inputRoot,
                TextAnchor.MiddleLeft, TextColor, "输入内容");
            var placeholder = Label("输入想对冷月说的话…", 18, 0, 532, 72, 20, inputRoot,
                TextAnchor.MiddleLeft, new Color(.92f, .87f, .96f, .72f), "输入提示");
            input.textComponent = value;
            input.placeholder = placeholder;
            input.characterLimit = 1000;
            input.lineType = TMP_InputField.LineType.SingleLine;

            sendButton = Button("发送", 598, 20, 106, 72,
                SendDialogue, dialogueRoot, 20, "发送");
            sendButton.GetComponent<Image>().color = DialogueSendColor;
            var closeButton = Button("关闭", 716, 20, 90, 72,
                CloseDialogue, dialogueRoot, 18, "关闭");
            closeButton.GetComponent<Image>().color = DialogueCloseColor;

            dialogueGroup.alpha = 0;
            dialogueGroup.interactable = false;
            dialogueGroup.blocksRaycasts = false;
            dialogueRoot.localScale = new Vector3(.08f, .88f, 1);
            dialogueRoot.anchoredPosition = new Vector2(0, -10);
            dialogueRoot.gameObject.SetActive(false);
        }

        private void BuildSubtitle()
        {
            var root = AnchoredRect("角色字幕", hudRoot, new Vector2(.5f, 0),
                new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 174), new Vector2(824, 64));
            subtitle = AlwaysOnTopUi.ConfigureText(root.gameObject.AddComponent<TextMeshProUGUI>());
            subtitle.fontSize = 24;
            subtitle.enableAutoSizing = true;
            subtitle.fontSizeMin = 18;
            subtitle.fontSizeMax = 26;
            subtitle.color = TextColor;
            subtitle.alignment = TextAlignmentOptions.Center;
            subtitle.raycastTarget = false;
            subtitle.enableWordWrapping = true;
            subtitle.overflowMode = TextOverflowModes.Truncate;
        }

        private void BuildPages()
        {
            contentRoot = AnchoredRect("HUD Content", hudRoot, new Vector2(.5f, 0),
                new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 383), new Vector2(760, 330));
            AddBackground(contentRoot, PanelColor);
            BuildInteractionPage();
            BuildMiniGamesPage();
            BuildInformationPage();
            BuildItemsPage();
            BuildSettingsPage();
        }

        private GameObject CreatePage(HudPage page, string name)
        {
            var root = StretchRect(name, contentRoot);
            pages.Add(page, root.gameObject);
            return root.gameObject;
        }

        private void BuildInteractionPage()
        {
            var page = CreatePage(HudPage.Interaction, "互动栏目");
            Label("选择互动", 22, 14, 716, 34, 25, page.transform,
                TextAnchor.MiddleLeft, TextColor, "互动标题");
            var activities = new[] { "一起散步", "坐下聊天", "躺床休息", "起身", "切水果" };
            for (var i = 0; i < activities.Length; i++)
            {
                var activity = activities[i];
                Button(activity, 22, 58 + i * 44, 716, 40,
                    () => SelectTestActivity(activity), page.transform, 20, "活动/" + activity);
            }
            Button("小游戏", 22, 278, 470, 40, () => SetActivePage(HudPage.MiniGames),
                page.transform, 24, "活动/小游戏");
            Button("取消切水果", 506, 278, 232, 40,
                () => Run(() => bootstrap.Session.CancelItemAction()),
                page.transform, 20, "取消切水果");
            interactionFeedback = Label("请选择一项互动活动。", 22, 326, 716, 28, 18,
                page.transform, TextAnchor.MiddleLeft, MutedTextColor, "互动反馈");
        }

        private void BuildMiniGamesPage()
        {
            var page = CreatePage(HudPage.MiniGames, "小游戏二级页面");
            Button("‹ 返回互动", 22, 16, 170, 40, () => SetActivePage(HudPage.Interaction), page.transform, 19);
            Label("一起玩小游戏", 210, 16, 500, 40, 25, page.transform,
                TextAnchor.MiddleLeft, TextColor);
            var index = 0;
            foreach (var game in bootstrap.MiniGames.Games)
            {
                if (game == null) continue;
                var id = game.Id;
                Button(game.DisplayName, 22, 76 + index * 66, 220, 54, () =>
                {
                    if (bootstrap.MiniGames.TryStart(id)) SetActivePage(HudPage.None);
                }, page.transform, 24, "小游戏/" + id);
                Label(game.Description, 262, 76 + index * 66, 472, 54, 19,
                    page.transform, TextAnchor.MiddleLeft, MutedTextColor);
                index++;
            }
            Button("再来一局", 22, 244, 344, 54, () => Run(() =>
            {
                var host = bootstrap.MiniGames;
                if (host.Active == null) { message = "请先选择一个小游戏。"; return; }
                var id = host.Active.Id;
                host.Stop();
                if (host.TryStart(id)) SetActivePage(HudPage.None);
            }), page.transform, 21);
            Button("结束小游戏", 394, 244, 344, 54, () =>
            {
                bootstrap.MiniGames.Stop();
                SetActivePage(HudPage.Interaction);
            }, page.transform, 21);
        }

        private void BuildInformationPage()
        {
            var page = CreatePage(HudPage.Information, "信息栏目");
            Label("陪伴信息", 22, 16, 716, 34, 25, page.transform,
                TextAnchor.MiddleLeft, TextColor, "信息标题");
            status = Label("", 22, 58, 716, 112, 20, page.transform,
                TextAnchor.UpperLeft, TextColor, "角色状态");
            Button("去走走", 22, 208, 220, 54, () => Run(() =>
            {
                var session = bootstrap.Session;
                if (session.Navigation.TryRandom(session.Motor.Position, new System.Random(), out var point))
                    session.Submit(new AvatarIntent("move", IntentSource.UI, payload: point));
            }), page.transform, 20);
            Button("留在这里", 270, 208, 220, 54, () => Run(() =>
            {
                var session = bootstrap.Session;
                session.Autonomy.Enabled = false;
                session.Submit(new AvatarIntent("stay", IntentSource.Player));
            }), page.transform, 20);
            Button("切换服装", 518, 208, 220, 54, SwitchOutfit, page.transform, 20);
        }

        private void BuildItemsPage()
        {
            var page = CreatePage(HudPage.Items, "虚拟物品栏目");
            Label("虚拟物品", 22, 10, 716, 34, 25, page.transform,
                TextAnchor.MiddleLeft, TextColor, "物品标题");
            catalog = bootstrap.CatalogItems.ToArray();
            const float categoryWidth = 112;
            const float categoryGap = 8.8f;
            for (var index = 0; index < CatalogCategories.Length; index++)
            {
                var category = CatalogCategories[index];
                var button = Button(category.GetDisplayName(),
                    22 + index * (categoryWidth + categoryGap), 45, categoryWidth, 34,
                    () => SelectCatalogCategory(category), page.transform, 16,
                    "物品分类/" + category.GetDisplayName());
                catalogCategoryBackgrounds.Add(button.GetComponent<Image>());
            }
            for (var slot = 0; slot < CatalogPageSize; slot++)
            {
                var capturedSlot = slot;
                var button = Button("", 22 + slot % 3 * 248, 88 + slot / 3 * 44, 220, 40,
                    () => PlaceCatalogItem(capturedSlot), page.transform, 18,
                    "物品槽位/" + slot);
                catalogButtons.Add(button);
                var titleRect = button.transform.Find("Label") as RectTransform;
                if (titleRect != null) titleRect.sizeDelta = new Vector2(180, 40);
                catalogQuantityLabels.Add(Label("", 184, 18, 28, 20, 14, button.transform,
                    TextAnchor.MiddleRight, TextColor, "剩余数量"));
            }
            Button("‹", 22, 179, 80, 34, () => ChangeCatalogPage(-1),
                page.transform, 24, "物品/上一页");
            catalogPageLabel = Label("", 110, 179, 540, 34, 17, page.transform,
                TextAnchor.MiddleCenter, MutedTextColor, "物品页码");
            Button("›", 658, 179, 80, 34, () => ChangeCatalogPage(1),
                page.transform, 24, "物品/下一页");
            selectedItem = Label("", 22, 219, 716, 32, 18, page.transform,
                TextAnchor.MiddleLeft, TextColor, "当前物品");
            Button("下个已放置", 22, 258, 220, 46, () => selected++, page.transform, 18);
            Button("过去看看", 270, 258, 220, 46, VisitSelectedItem, page.transform, 18);
            recycleButton = Button("回收当前物品", 518, 258, 220, 46,
                RemoveSelectedItem, page.transform, 18);
            SelectCatalogCategory(selectedCategory);
        }

        private void SelectCatalogCategory(ItemCategory category)
        {
            selectedCategory = category;
            categoryCatalog = catalog.Where(item => item.Category == category).ToArray();
            catalogPage = 0;
            for (var index = 0; index < catalogCategoryBackgrounds.Count; index++)
                catalogCategoryBackgrounds[index].color =
                    CatalogCategories[index] == category ? SelectedColor : ButtonColor;
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
            Button("角色性格", 22, 12, 344, 38,
                () => SetSettingsMode(true), page.transform, 18, "设置/角色性格");
            Button("系统", 394, 12, 344, 38,
                () => SetSettingsMode(false), page.transform, 18, "设置/系统");
            personalitySettingsRoot = StretchRect("角色性格设置", page.transform);
            systemSettingsRoot = StretchRect("系统设置", page.transform);
            var autonomyButton = Button("自主活动：开启", 22, 70, 220, 54,
                () => Run(() => bootstrap.Session.Autonomy.Enabled = !bootstrap.Session.Autonomy.Enabled),
                systemSettingsRoot, 19);
            autonomyLabel = autonomyButton.GetComponentInChildren<TMP_Text>();
            var pauseButton = Button("暂停", 270, 70, 220, 54,
                () => bootstrap.TogglePause(), systemSettingsRoot, 20, "暂停 / 继续");
            pauseLabel = pauseButton.GetComponentInChildren<TMP_Text>();
            Button("重试定位", 518, 70, 220, 54,
                () => RunAsync(() => bootstrap.Session.RestoreItemsAsync()), systemSettingsRoot, 20);
            Button("保存", 22, 146, 220, 54,
                () => Run(() => { bootstrap.Session.Save(); message = "已保存"; }),
                systemSettingsRoot, 20);
            Button("重置界面", 270, 146, 220, 54, () =>
            {
                Recenter(true);
                message = "界面已重新定位";
            }, systemSettingsRoot, 20);
            var modeButton = Button("交互模式", 22, 224, 716, 50, () => Run(() =>
            {
                var mode = bootstrap.MiniGames.Input;
                if (mode == null) return;
                if (mode.Automatic) mode.SetMode(PlayerInteractionMode.Controller);
                else if (mode.CurrentMode == PlayerInteractionMode.Controller) mode.SetMode(PlayerInteractionMode.EyeHand);
                else if (mode.CurrentMode == PlayerInteractionMode.EyeHand) mode.SetMode(PlayerInteractionMode.Gesture);
                else mode.ResumeAutomaticSwitching();
            }), systemSettingsRoot, 20);
            inputModeLabel = modeButton.GetComponentInChildren<TMP_Text>();
            SetSettingsMode(true);
        }

        private void SetSettingsMode(bool personality)
        {
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
                22, 55, 716, 28, 18, personalitySettingsRoot,
                TextAnchor.MiddleCenter, MutedTextColor, "角色基础信息");
            for (var index = 0; index < session.Persona.Traits.Count; index++)
            {
                var trait = session.Persona.Traits[index];
                var traitId = trait.id;
                var button = Button("", 22, 90 + index * 42, 716, 36,
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
            if (page != HudPage.Items && placementPreview != null)
                placementPreview.Cancel();
            if (page != HudPage.Items) CancelRecycleSelection();
            activePage = page;
            if (contentRoot != null) contentRoot.gameObject.SetActive(page != HudPage.None);
            foreach (var entry in pages) entry.Value.SetActive(entry.Key == page);
            RefreshNavigationSelection();
        }

        private void RefreshNavigationSelection()
        {
            foreach (var entry in navigationBackgrounds)
                entry.Value.color =
                    entry.Key == HudPage.Dialogue && dialogueOpen ||
                    entry.Key == activePage ||
                    entry.Key == HudPage.Interaction && activePage == HudPage.MiniGames
                        ? SelectedColor
                        : ButtonColor;
        }

        private void SetDialogueOpen(bool open)
        {
            if (dialogueOpen == open && dialogueTransition == null) return;
            if (open) SetActivePage(HudPage.None);
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
            leftHandFist = new InputAction("左手握拳", InputActionType.Button,
                "<PicoHandInteraction>{LeftHand}/graspFirm");
            rightHandFist = new InputAction("右手握拳", InputActionType.Button,
                "<PicoHandInteraction>{RightHand}/graspFirm");
            controllerHudToggle.Enable();
            leftHandFist.Enable();
            rightHandFist.Enable();
        }

        private void UpdateHudVisibilityInput()
        {
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

            var left = leftHandFist != null && leftHandFist.ReadValue<float>() > .5f;
            var right = rightHandFist != null && rightHandFist.ReadValue<float>() > .5f;
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
                           : "双手握拳");
        }

        private void SetHudVisible(bool visible)
        {
            hudVisible = visible;
            if (hudRoot != null) hudRoot.gameObject.SetActive(visible);
            if (visible || EventSystem.current == null || canvasRoot == null) return;
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
            else if (activity == "躺床休息")
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
                        ? interactionAction.Error : "正在走向床尾并准备躺下";
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

        private void CloseDialogue() => SetDialogueOpen(false);

        private void SendDialogue()
        {
            RunAsync(async () =>
            {
                var value = input.text;
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
                    message = "移动视野选择物品，光圈亮起后再次点击确认回收。";
                    return;
                }
                var item = focusController.FocusedItem;
                if (item == null)
                {
                    message = "视野中心没有可回收物品。";
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
            var cameraTransform = activeCamera.transform;
            var targetPosition = cameraTransform.position + cameraTransform.forward * 1.5f -
                                 cameraTransform.up * .7f;
            var targetRotation = cameraTransform.rotation;
            var amount = immediate ? 1f : 1f - Mathf.Exp(-7f * Time.unscaledDeltaTime);
            canvasRoot.position = Vector3.Lerp(canvasRoot.position, targetPosition, amount);
            canvasRoot.rotation = Quaternion.Slerp(canvasRoot.rotation, targetRotation, amount);
            canvas.worldCamera = activeCamera;
        }

        private void LateUpdate()
        {
            if (!Application.isEditor) Recenter(false);
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
                    var surface = definition.PlacementSurface == ItemPlacementSurface.Wall
                        ? "墙面" : "地面";
                    message = $"移动视线选择{surface}位置，绿色可放置；再次点击确认。";
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
            var desired = origin + forward * 1.45f;
            await s.PlaceItemAsync(definition.Id,
                new Pose(desired, definition.ResolvePlacementRotation(-forward)));
        }

        private void RefreshCatalogQuantities()
        {
            var session = bootstrap == null ? null : bootstrap.Session;
            for (var slot = 0; slot < catalogButtons.Count; slot++)
            {
                var index = catalogPage * CatalogPageSize + slot;
                if (index >= categoryCatalog.Length) continue;
                var definition = categoryCatalog[index];
                var remaining = session == null ? 0 :
                    session.GetRemainingQuantity(definition.Id);
                catalogQuantityLabels[slot].text =
                    definition.UnlimitedInventory ? "∞" : remaining.ToString();
                catalogButtons[slot].interactable = session != null &&
                    (definition.UnlimitedInventory || remaining > 0);
            }
        }

        private void Update()
        {
            UpdateHudVisibilityInput();

            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .2f;
            var session = bootstrap == null ? null : bootstrap.Session;
            RefreshCatalogQuantities();
            if (sendButton != null)
                sendButton.interactable = session != null &&
                    session.Conversation.State != MateSoul.Gameplay.ConversationState.Thinking;
            if (interactionAction != null && interactionAction.IsFinished)
            {
                message = interactionAction.Error ?? (
                    interactionAction.Intent.ActionId == CutFruitAction.Id
                        ? (interactionAction.State == ActionState.Succeeded ? "水果已切好并装盘" : "切水果已取消") :
                    interactionAction.Intent.ActionId == "stay" ? "已起身" :
                    interactionAction.Intent.ActionId == RealityInteractionIds.Lie
                        ? "躺床互动已结束" : "落座互动已结束");
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
                subtitle.text = !session.Subtitles ? "" :
                    session.SpontaneousReplies.VisibleText ??
                    bootstrap.MiniGames.Active?.DialogueLine ??
                    (session.Conversation.State == MateSoul.Gameplay.ConversationState.Thinking ? "正在想…" :
                    session.Conversation.Text ?? "");
            if (inputModeLabel != null && bootstrap.MiniGames.Input != null)
            {
                var mode = bootstrap.MiniGames.Input;
                var title = mode.CurrentMode == PlayerInteractionMode.Controller ? "手柄" :
                    mode.CurrentMode == PlayerInteractionMode.EyeHand ? "眼手" : "手势";
                inputModeLabel.text = $"交互模式：{(mode.Automatic ? "自动 / " : "")}{title}（点击切换）";
            }
            if (autonomyLabel != null)
                autonomyLabel.text = $"自主活动：{(session.Autonomy.Enabled ? "开启" : "关闭")}";
            if (pauseLabel != null) pauseLabel.text = session.Paused ? "继续" : "暂停";
        }


        private void Run(Action action)
        {
            if (bootstrap.Session == null || bootstrap.Session.Paused) return;
            try { message = null; action(); }
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
            CancelRecycleSelection();
            if (dialogueTransition != null) StopCoroutine(dialogueTransition);
            bootstrap?.Session?.SetDialogueAttention(false);
            controllerHudToggle?.Dispose();
            leftHandFist?.Dispose();
            rightHandFist?.Dispose();
            if (overlayMaterial != null) Destroy(overlayMaterial);
            foreach (var sprite in iconSprites) if (sprite != null) Destroy(sprite);
            foreach (var texture in iconTextures) if (texture != null) Destroy(texture);
            if (roundedSprite != null) Destroy(roundedSprite);
            if (roundedTexture != null) Destroy(roundedTexture);
        }
    }
}
