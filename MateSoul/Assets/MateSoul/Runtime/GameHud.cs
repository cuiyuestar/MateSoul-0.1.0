using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MateSoul.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
        private const int CatalogPageSize = 6;

        [SerializeField] private GameBootstrap bootstrap;
        private readonly Dictionary<HudPage, GameObject> pages = new Dictionary<HudPage, GameObject>();
        private readonly Dictionary<HudPage, Image> navigationBackgrounds = new Dictionary<HudPage, Image>();
        private readonly Dictionary<string, TMP_Text> personalityLabels = new Dictionary<string, TMP_Text>();
        private readonly List<Sprite> iconSprites = new List<Sprite>();
        private readonly List<Texture2D> iconTextures = new List<Texture2D>();
        private readonly List<Button> catalogButtons = new List<Button>();
        private ItemDefinition[] catalog = Array.Empty<ItemDefinition>();
        private TMP_Text status, subtitle, selectedItem, interactionFeedback, catalogPageLabel;
        private TMP_Text autonomyLabel, pauseLabel, subtitlesLabel, inputModeLabel;
        private TMP_InputField input;
        private Canvas canvas;
        private TMP_FontAsset font;
        private Material overlayMaterial;
        private VirtualItemFocusController focusController;
        private Sprite roundedSprite;
        private Texture2D roundedTexture;
        private RectTransform canvasRoot, hudRoot, contentRoot;
        private RectTransform personalitySettingsRoot, systemSettingsRoot;
        private HudPage activePage;
        private int selected, catalogPage;
        private float nextRefresh;
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

        public void Configure(GameBootstrap game) { bootstrap = game; }

        private void Start()
        {
            focusController = bootstrap == null ? null : bootstrap.GetComponent<VirtualItemFocusController>();
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
            BuildSubtitle();
            BuildPages();
            AlwaysOnTopUi.Apply(root.transform, overlayMaterial);
            SetActivePage(HudPage.None);

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
            BuildDialoguePage();
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

        private void BuildDialoguePage()
        {
            var page = CreatePage(HudPage.Dialogue, "对话栏目");
            Label("文字对话", 22, 16, 716, 34, 25, page.transform,
                TextAnchor.MiddleLeft, TextColor, "对话标题");
            Label("输入想对冷月说的话", 22, 58, 716, 28, 18, page.transform,
                TextAnchor.MiddleLeft, MutedTextColor, "输入说明");
            var inputRoot = Rect("对话输入", 22, 94, 716, 86, page.transform);
            AddBackground(inputRoot, new Color(.15f, .13f, .18f, .98f));
            input = inputRoot.gameObject.AddComponent<TMP_InputField>();
            var value = Label("", 14, 8, 688, 70, 20, inputRoot,
                TextAnchor.UpperLeft, TextColor, "输入内容");
            var placeholder = Label("输入想说的话…", 14, 8, 688, 70, 20, inputRoot,
                TextAnchor.UpperLeft, MutedTextColor, "输入提示");
            input.textComponent = value;
            input.placeholder = placeholder;
            input.characterLimit = 1000;
            input.lineType = TMP_InputField.LineType.MultiLineNewline;
            Button("发送", 22, 204, 220, 52, SendDialogue, page.transform, 21);
            Button("取消", 270, 204, 220, 52,
                () => Run(() => bootstrap.Session.Conversation.Cancel()), page.transform, 21);
            var subtitleButton = Button("字幕：开启", 518, 204, 220, 52,
                () => Run(() => bootstrap.Session.Subtitles = !bootstrap.Session.Subtitles), page.transform, 20);
            subtitlesLabel = subtitleButton.GetComponentInChildren<TMP_Text>();
        }

        private void BuildInteractionPage()
        {
            var page = CreatePage(HudPage.Interaction, "互动栏目");
            Label("选择互动", 22, 14, 716, 34, 25, page.transform,
                TextAnchor.MiddleLeft, TextColor, "互动标题");
            var activities = new[] { "一起散步", "坐下聊天", "躺床休息", "起身" };
            for (var i = 0; i < activities.Length; i++)
            {
                var activity = activities[i];
                Button(activity, 22, 58 + i * 44, 716, 40,
                    () => SelectTestActivity(activity), page.transform, 20, "活动/" + activity);
            }
            Button("小游戏", 22, 234, 716, 40, () => SetActivePage(HudPage.MiniGames),
                page.transform, 24, "活动/小游戏");
            interactionFeedback = Label("请选择一项互动活动。", 22, 286, 716, 28, 18,
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
            for (var slot = 0; slot < Mathf.Min(CatalogPageSize, catalog.Length); slot++)
            {
                var capturedSlot = slot;
                catalogButtons.Add(Button("", 22 + slot % 3 * 248, 50 + slot / 3 * 46, 220, 40,
                    () => PlaceCatalogItem(capturedSlot), page.transform, 18,
                    "物品槽位/" + slot));
            }
            Button("‹", 22, 145, 80, 34, () => ChangeCatalogPage(-1),
                page.transform, 24, "物品/上一页");
            catalogPageLabel = Label("", 110, 145, 540, 34, 17, page.transform,
                TextAnchor.MiddleCenter, MutedTextColor, "物品页码");
            Button("›", 658, 145, 80, 34, () => ChangeCatalogPage(1),
                page.transform, 24, "物品/下一页");
            selectedItem = Label("", 22, 185, 716, 32, 18, page.transform,
                TextAnchor.MiddleLeft, TextColor, "当前物品");
            Button("下个已放置", 22, 226, 220, 46, () => selected++, page.transform, 18);
            Button("过去看看", 270, 226, 220, 46, VisitSelectedItem, page.transform, 18);
            Button("删除当前物品", 518, 226, 220, 46, RemoveSelectedItem, page.transform, 18);
            RefreshCatalogPage();
        }

        private void ChangeCatalogPage(int offset)
        {
            var pageCount = Mathf.Max(1, (catalog.Length + CatalogPageSize - 1) / CatalogPageSize);
            catalogPage = (catalogPage + offset + pageCount) % pageCount;
            RefreshCatalogPage();
        }

        private void RefreshCatalogPage()
        {
            var pageCount = Mathf.Max(1, (catalog.Length + CatalogPageSize - 1) / CatalogPageSize);
            catalogPage = Mathf.Clamp(catalogPage, 0, pageCount - 1);
            for (var slot = 0; slot < catalogButtons.Count; slot++)
            {
                var index = catalogPage * CatalogPageSize + slot;
                var button = catalogButtons[slot];
                var visible = index < catalog.Length;
                button.gameObject.SetActive(visible);
                if (!visible) continue;
                var definition = catalog[index];
                button.name = "物品/" + definition.Id;
                button.GetComponentInChildren<TMP_Text>().text = definition.DisplayName;
            }
            if (catalogPageLabel != null)
                catalogPageLabel.text = catalog.Length == 0 ? "暂无可用物品" :
                    $"{catalogPage + 1} / {pageCount}  ·  共 {catalog.Length} 项";
        }

        private void PlaceCatalogItem(int slot)
        {
            var index = catalogPage * CatalogPageSize + slot;
            if (index < 0 || index >= catalog.Length) return;
            var definition = catalog[index];
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
            SetActivePage(activePage == page ? HudPage.None : page);
        }

        private void SetActivePage(HudPage page)
        {
            activePage = page;
            if (contentRoot != null) contentRoot.gameObject.SetActive(page != HudPage.None);
            foreach (var entry in pages) entry.Value.SetActive(entry.Key == page);
            foreach (var entry in navigationBackgrounds)
                entry.Value.color = entry.Key == page ||
                    entry.Key == HudPage.Interaction && page == HudPage.MiniGames ? SelectedColor : ButtonColor;
        }

        private void SelectTestActivity(string activity)
        {
            if (activity == "坐下聊天")
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

        private void SendDialogue()
        {
            RunAsync(async () =>
            {
                var value = input.text;
                if (string.IsNullOrWhiteSpace(value)) return;
                input.text = "";
                await bootstrap.Session.Conversation.SendAsync(value);
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
                var item = SelectedItem();
#region debug-point D:delete-selection
#if MATESOUL_ITEM_FOCUS_DIAGNOSTICS && DEVELOPMENT_BUILD && !UNITY_EDITOR
                var deletePayload = $"{{\"sessionId\":\"item-focus-interaction\",\"runId\":\"post-fix\"," +
                                    $"\"hypothesisId\":\"D\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                                    $"\"location\":\"GameHud.RemoveSelectedItem\"," +
                                    $"\"msg\":\"[DEBUG] delete button target resolved\",\"data\":{{" +
                                    $"\"focusId\":\"{focusController?.FocusedItem?.Instance?.Id ?? "none"}\"," +
                                    $"\"targetId\":\"{item?.Instance?.Id ?? "none"}\"" + "}}";
                UnityWebRequest.Post("http://10.0.2.2:7782/event", deletePayload, "application/json")
                    .SendWebRequest();
#endif
#endregion
                if (item != null) bootstrap.Session.RemoveItem(item.Instance.Id);
            });
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
            if (focusController != null && focusController.FocusedItem != null)
                return focusController.FocusedItem;
            var items = bootstrap.Session?.ItemViews.Where(i => i != null).ToArray();
            return items == null || items.Length == 0 ? null : items[selected % items.Length];
        }

        private async Task PlaceAsync(ItemDefinition definition)
        {
            var s = bootstrap.Session;
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
                new Pose(desired, Quaternion.LookRotation(-forward, Vector3.up)));
        }

        private void Update()
        {
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
            if (interactionAction != null && interactionAction.IsFinished)
            {
                message = interactionAction.Error ?? (
                    interactionAction.Intent.ActionId == "stay" ? "已起身" :
                    interactionAction.Intent.ActionId == RealityInteractionIds.Lie
                        ? "躺床互动已结束" : "落座互动已结束");
                interactionAction = null;
                if (interactionFeedback != null) interactionFeedback.text = message;
            }
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
            if (subtitlesLabel != null) subtitlesLabel.text = $"字幕：{(session.Subtitles ? "开启" : "关闭")}";
        }

        #region debug-point A-E:ui-quality-runtime-evidence
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        private void ReportUiQuality()
        {
            const string endpoint = "http://10.0.2.2:7783/event";
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
            const string endpoint = "http://10.0.2.2:7784/event";
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
            var rect = Rect(title, x, 10, width, 140, parent);
            var image = AddBackground(rect, ButtonColor);
            var shadow = rect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(.08f, .04f, .09f, .38f);
            shadow.effectDistance = new Vector2(4, -4);
            shadow.useGraphicAlpha = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.onClick.AddListener(action);
            ConfigureButton(button, image);

            var iconRect = Rect("功能图标", width * .5f - 40, 9, 80, 80, rect);
            var iconImage = iconRect.gameObject.AddComponent<Image>();
            AlwaysOnTopUi.Apply(iconImage, overlayMaterial);
            iconImage.sprite = CreateIconSprite(icon);
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            var iconShadow = iconRect.gameObject.AddComponent<Shadow>();
            iconShadow.effectColor = new Color(.15f, .08f, .16f, .18f);
            iconShadow.effectDistance = new Vector2(1, -1);
            iconShadow.useGraphicAlpha = true;
            Label(title, 8, 104, width - 16, 28, 20, rect,
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
            if (overlayMaterial != null) Destroy(overlayMaterial);
            foreach (var sprite in iconSprites) if (sprite != null) Destroy(sprite);
            foreach (var texture in iconTextures) if (texture != null) Destroy(texture);
            if (roundedSprite != null) Destroy(roundedSprite);
            if (roundedTexture != null) Destroy(roundedTexture);
        }
    }
}
