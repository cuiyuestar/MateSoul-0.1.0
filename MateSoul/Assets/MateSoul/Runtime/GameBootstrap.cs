using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ByteDance.PICO.XR;
using MateSoul.RoomNavigation;
using MateSoul.Wardrobe;
using UnityEngine;

namespace MateSoul.Runtime
{
    [RequireComponent(typeof(RuntimeRoomNavigation))]
    [RequireComponent(typeof(VirtualItemFocusController))]
    [RequireComponent(typeof(ItemPlacementPreviewController))]
    [RequireComponent(typeof(MiniGameHost))]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private GameObject character;
        [SerializeField] private TextAsset editorRoom;
        [SerializeField] private TextAsset characterPersona;
        [SerializeField] private ItemDefinition[] items;
        [SerializeField] private ConversationGatewaySettings conversationGateway;
        private CancellationTokenSource lifetime;
        private Transform itemRoot;
        private Animator animator;
        private RealityOcclusionMeshRenderer realityOcclusion;
        private bool manualPause, appPause, hasFocus = true;
        public GameSession Session { get; private set; }
        public SystemNotifications Notifications { get; } = new SystemNotifications();
        public MiniGameHost MiniGames => GetComponent<MiniGameHost>();
        public string Status { get; private set; } = "正在读取房间…";
        public RuntimeRoomNavigation Room => GetComponent<RuntimeRoomNavigation>();
        public IEnumerable<ItemDefinition> CatalogItems
        {
            get
            {
                if (items == null) yield break;
                foreach (var item in items)
                    if (item != null && item.CatalogVisible) yield return item;
            }
        }

        public void Configure(GameObject avatar, TextAsset mockRoom, ItemDefinition[] definitions,
            TextAsset persona = null, ConversationGatewaySettings gateway = null)
        {
            character = avatar; editorRoom = mockRoom; items = definitions;
            characterPersona = persona;
            conversationGateway = gateway;
        }

        private async void Start()
        {
            realityOcclusion = RealityOcclusionMeshRenderer.Install(Camera.main);
            gameObject.AddComponent<SystemNotificationPresenter>().Bind(Notifications);
            lifetime = new CancellationTokenSource();
            try { await InitializeAsync(lifetime.Token); }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                Status = $"启动失败：{error.Message}";
                Notifications.ShowHint(Status, 0);
                Debug.LogException(error, this);
            }
        }

        private async Task InitializeAsync(CancellationToken token)
        {
            character.SetActive(false);
            SetLayerRecursively(character.transform, LayerMask.NameToLayer("Ignore Raycast"));
            IRoomMapProvider provider;
            if (Application.isEditor) provider = new PicoMockRoomMapProvider(editorRoom);
            else
            {
                PXR_Manager.EnableVideoSeeThrough = true;
                if (SystemInfo.deviceModel.IndexOf("swan_arm64", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    SystemInfo.deviceName.IndexOf("pico_spaceos_emulator", StringComparison.OrdinalIgnoreCase) >= 0)
                    provider = new PicoSpatialMeshRoomMapProvider(TimeSpan.FromSeconds(15));
                else
                    provider = new PicoSceneCaptureRoomMapProvider(true);
            }
            var snapshot = await provider.LoadAsync(token);
            token.ThrowIfCancellationRequested();
            realityOcclusion?.ApplySnapshot(snapshot);
            Room.Build(snapshot, Application.isEditor);
            var spawn = snapshot.MainFloor.Centroid;
            if (!Application.isEditor && Camera.main != null)
            {
                spawn = Camera.main.transform.position +
                        Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized * 2.2f;
                spawn.y = snapshot.MainFloor.Centroid.y;
            }
            if (!Room.TrySamplePosition(spawn, snapshot.Bounds.extents.magnitude, out spawn))
                throw new InvalidOperationException("房间内没有可用的角色出生点。");
            // Initial placement belongs to session assembly, before action ownership begins.
            character.transform.position = spawn + Vector3.up * .03f;
            character.SetActive(true);
            animator = character.GetComponentInChildren<Animator>();
            var motor = character.GetComponent<AvatarMotor>();
            motor.Bind(animator);
            AvatarIdleVariation.Attach(character, animator);
            var socialAttentionRig = AvatarSocialAttentionRig.Attach(character, animator);
            itemRoot = new GameObject("Room Items").transform;
            itemRoot.SetParent(transform, false);
            var anchors = Application.isEditor
                ? (ISpatialAnchorService)new EditorRoomAnchorService() : new PicoSpatialAnchorService();
            try
            {
                var aiProvider = new HttpLLMProvider(conversationGateway);
                Session = new GameSession(motor, new RoomNavigationService(Room),
                    character.GetComponentInChildren<LengyueWardrobeController>(), items, itemRoot,
                    new JsonGameSaveStore(Path.Combine(Application.persistentDataPath, "matesoul-v1.json")),
                    anchors, aiProvider, CharacterPersonaLoader.Load(characterPersona),
                    Notifications, spontaneousReplyProvider: aiProvider);
                ResponseEmotionAudio.Attach(
                    character, Session.Conversation, Session.SpontaneousReplies);
            }
            catch
            {
                if (Session != null)
                {
                    Session.Dispose();
                    Session = null;
                }
                else anchors.Dispose();
                throw;
            }
            var playerHead = Camera.main != null ? Camera.main.transform : null;
            Session.ConfigureSocialAttention(socialAttentionRig, playerHead);
            Session.ConfigureHandHold(character.GetComponent<HandIKRig>(), playerHead);
            Session.ConfigureRealityItems(snapshot,
                character.GetComponent<AvatarSeatingRig>(),
                character.GetComponent<AvatarLyingRig>());
            MiniGames.Bind(Session);
            ApplyPause();
            await Session.RestoreItemsAsync();
            token.ThrowIfCancellationRequested();
            Status = Session.ItemRestoreFailed ? "房间已就绪，旧物品定位失败" : "房间已就绪";
            Debug.Log($"MATESOUL_READY source={snapshot.Source} items={Session.Items.Count} " +
                      $"itemState={(Session.ItemsReady ? "ready" : "recovery")} navMs={Room.LastBuildMilliseconds}");
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root) SetLayerRecursively(child, layer);
        }

        private void Update()
        {
            Session?.Tick(Time.deltaTime);
        }
        public void TogglePause() { manualPause = !manualPause; ApplyPause(); }
        private void OnApplicationPause(bool paused)
        {
            appPause = paused;
            ApplyPause();
        }
        private void OnApplicationFocus(bool focused)
        {
            hasFocus = focused;
            ApplyPause();
        }
        private void ApplyPause()
        {
            var paused = manualPause || appPause || !hasFocus;
            if (paused && MiniGames.Active != null)
            {
                MiniGames.Stop();
                Notifications.ShowHint("小游戏已因暂停结束，可从互动栏重新开始。");
            }
            try { Session?.SetPaused(paused); }
            catch (Exception error) { Status = $"存档失败：{error.Message}"; Debug.LogException(error, this); }
            if (animator != null) animator.speed = paused ? 0 : 1;
        }
        private void OnDestroy()
        {
            lifetime?.Cancel();
            MiniGames.Stop();
            if (Session != null)
            {
                try { Session.Save(); }
                catch (Exception error) { Debug.LogException(error); }
                finally { Session.Dispose(); }
            }
            lifetime?.Dispose();
        }
    }
}
