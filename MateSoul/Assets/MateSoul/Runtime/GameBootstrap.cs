using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ByteDance.PICO.XR;
using MateSoul.Gameplay;
using MateSoul.RoomNavigation;
using MateSoul.Wardrobe;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

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
        [SerializeField, FormerlySerializedAs("conversationGateway")]
        private ModelConnectionSettings modelConnection;
        private const string SaveFileName = "matesoul-v1.json";
        private const string FirstLaunchSeenKey = "matesoul.first-launch.room-calibration";
        private CancellationTokenSource lifetime;
        private Task startupTask;
        private Transform itemRoot;
        private Animator animator;
        private RealityOcclusionMeshRenderer realityOcclusion;
        private readonly System.Diagnostics.Stopwatch startup = new System.Diagnostics.Stopwatch();
        private string startupRoomSource;
        private bool manualPause, appPause, hasFocus = true;
#region debug-point A-E:item-memory-periodic-state
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        private float nextItemMemoryReport;
        private float previousItemMemoryReportTime;
        private int previousItemMemoryReportFrame;
#endif
#endregion
        public GameSession Session { get; private set; }
        private PlayerAIConfiguration aiConfiguration;
        public PlayerAIConfiguration AIConfiguration =>
            aiConfiguration ?? (aiConfiguration = PlayerAIConfiguration.Load(modelConnection));
        public bool AllowLocalAIHttp => modelConnection != null &&
                                        modelConnection.AllowLocalHttpInDevelopment;

        public void SaveAIConfiguration(PlayerAIConfiguration configuration)
        {
            configuration.Save();
            aiConfiguration = configuration;
        }

        public bool IsFirstLaunchPending => PlayerPrefs.GetInt(FirstLaunchSeenKey, 0) == 0;

        public void MarkFirstLaunchSeen()
        {
            PlayerPrefs.SetInt(FirstLaunchSeenKey, 1);
            PlayerPrefs.Save();
        }

        // 仅清除游戏存档与首次启动标记，AI 配置（加密存储/编辑器 PlayerPrefs）保持不动。
        public bool ResetToFirstLaunch()
        {
            Session?.SuspendSaving();
            try
            {
                var path = Path.Combine(Application.persistentDataPath, SaveFileName);
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            }
            catch (Exception error)
            {
                Debug.LogException(error, this);
                return false;
            }
            PlayerPrefs.DeleteKey(FirstLaunchSeenKey);
            PlayerPrefs.Save();
            return true;
        }

        // 拉起 PICO 系统的房间标定（Room Capture），返回 null 表示已成功打开。
        public async Task<string> LaunchRoomCalibrationAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var stateResult = PXR_MixedReality.GetSenseDataProviderState(
                    PxrSenseDataProviderType.SceneCapture, out var state);
                if (stateResult != PxrResult.SUCCESS ||
                    state != PxrSenseDataProviderState.Running)
                {
                    var startResult = await PXR_MixedReality.StartSenseDataProvider(
                        PxrSenseDataProviderType.SceneCapture);
                    if (startResult != PxrResult.SUCCESS)
                        return $"无法启动房间标定：{startResult}";
                }
                var result = await PXR_MixedReality.StartSceneCaptureAsync();
                return result == PxrResult.SUCCESS ? null : $"无法打开房间标定：{result}";
            }
            catch (Exception error)
            {
                Debug.LogException(error, this);
                return "无法打开房间标定：" + error.Message;
            }
#else
            await Task.Yield();
            return "房间标定需要在 PICO 头显中运行游戏后使用。";
#endif
        }

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
            TextAsset persona = null, ModelConnectionSettings connection = null)
        {
            character = avatar; editorRoom = mockRoom; items = definitions;
            characterPersona = persona;
            modelConnection = connection;
        }

        private void Awake() => StartupTimeline.Mark("bootstrap_awake");

        private async void Start()
        {
            startup.Start();
            TraceStartup("bootstrap_start");
            RenderPipelineManager.endCameraRendering += OnStartupCameraRendered;
            realityOcclusion = RealityOcclusionMeshRenderer.Install(Camera.main);
            RealityOcclusionMeshRenderer.CoverageTarget =
                character != null ? character.transform : null;
            SpatialMeshVisualizer.Install(
                realityOcclusion != null ? realityOcclusion.transform : null);
            EnvironmentDepthDiagnostics.Install(Camera.main,
                character != null ? character.transform : null);
            gameObject.AddComponent<SystemNotificationPresenter>().Bind(Notifications);
            await RetryLocalizationAsync();
        }

        public Task RetryLocalizationAsync()
        {
            if (startupTask != null && !startupTask.IsCompleted) return startupTask;
            if (Session != null) return Session.RestoreItemsAsync();
            lifetime?.Dispose();
            lifetime = new CancellationTokenSource();
            Status = "正在找回房间，请保持头显唤醒并环视…";
            Notifications.Clear();
            startup.Restart();
            startupTask = StartSessionAsync(lifetime.Token);
            return startupTask;
        }

        private async Task StartSessionAsync(CancellationToken token)
        {
            try { await InitializeAsync(token); }
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
            var store = new JsonGameSaveStore(
                Path.Combine(Application.persistentDataPath, SaveFileName));
            var save = store.Load();
            TraceStartup("save_loaded");
            var anchors = Application.isEditor
                ? (ISpatialAnchorService)new EditorRoomAnchorService() : new PicoSpatialAnchorService();
            try
            {
                var preparedAnchor = string.IsNullOrEmpty(save.roomAnchorId)
                    ? null : PrepareRoomAnchorAsync(anchors, save.roomAnchorId, token);
                if (preparedAnchor != null)
                    _ = preparedAnchor.ContinueWith(completed => { var observed = completed.Exception; },
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                await InitializeRoomAsync(token, store, save, anchors, preparedAnchor);
            }
            catch
            {
                if (!token.IsCancellationRequested) lifetime.Cancel();
                if (Session != null)
                {
                    Session.Dispose();
                    Session = null;
                }
                else anchors.Dispose();
                if (character != null) character.SetActive(false);
                animator = null;
                startupRoomSource = null;
                if (itemRoot != null) Destroy(itemRoot.gameObject);
                itemRoot = null;
                throw;
            }
        }

        private async Task<Pose> PrepareRoomAnchorAsync(
            ISpatialAnchorService anchors, string id, CancellationToken token)
        {
            TraceStartup("anchor_start");
            try
            {
                var pose = await anchors.LoadAsync(id, token);
                TraceStartup("anchor_located");
                return pose;
            }
            catch
            {
                TraceStartup("anchor_unavailable");
                throw;
            }
        }

        private async Task InitializeRoomAsync(CancellationToken token, IGameSaveStore store,
            GameSaveData save, ISpatialAnchorService anchors, Task<Pose> preparedAnchor)
        {
            character.SetActive(false);
            SetLayerRecursively(character.transform, LayerMask.NameToLayer("Ignore Raycast"));
            TraceStartup("room_load_start");
            var snapshot = await LoadRoomMapAsync(token);
            TraceStartup("room_loaded");
            token.ThrowIfCancellationRequested();
            var spawn = await BuildRoomAndFindSpawnAsync(snapshot, token);
            if (!spawn.HasValue && !Application.isEditor &&
                !PicoSpatialMeshRoomMapProvider.IsSource(snapshot.Source))
            {
                // A returned capture can still disagree with the current measured
                // floor. Use the existing measured-room path before enabling the avatar.
                snapshot = await LoadSpatialMeshRoomAsync(
                    "Captured room has no spawn supported by current measured geometry.", token);
                spawn = await BuildRoomAndFindSpawnAsync(snapshot, token);
            }
            if (!spawn.HasValue)
                throw new InvalidOperationException(
                    "房间内没有可用的角色出生点。请扫视地面后重试定位。");
            if (!Application.isEditor && realityOcclusion != null)
                TraceStartup("reality_collision_ready");
#region debug-point F:spawn-search-result
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.Log($"MATESOUL_SPAWN_FOUND position={spawn:F5}");
#endif
#endregion
            // Initial placement belongs to session assembly, before action ownership begins.
            character.transform.position = spawn.Value + Vector3.up * .03f;
            var physicalResponse = AvatarPhysicalResponse.Install(character);
            StartupTimeline.Mark("avatar_enable_start");
            character.SetActive(true);
            StartupTimeline.Mark("avatar_enable_end");
            animator = character.GetComponentInChildren<Animator>();
            var motor = character.GetComponent<AvatarMotor>();
            motor.Bind(animator);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var boundOnNavMesh = Room.TrySamplePosition(
                motor.Position, .12f, out var boundSample);
            Debug.Log($"MATESOUL_SPAWN_BOUND requested={spawn:F5} " +
                      $"actual={motor.Position:F5} onNavMesh={boundOnNavMesh} " +
                      $"sample={(boundOnNavMesh ? boundSample.ToString("F5") : "none")}");
#endif
            physicalResponse.Bind(motor);
            AvatarIdleVariation.Attach(character, animator);
            var socialAttentionRig = AvatarSocialAttentionRig.Attach(character, animator);
            TraceStartup("avatar_bound");
            RealityAmbientLighting.Install(gameObject);
#region debug-point B-D:visible-animator-ownership
#if MATESOUL_DEVICE_PROBES && (DEVELOPMENT_BUILD || UNITY_EDITOR)
            var allAnimators = character.GetComponentsInChildren<Animator>(true);
            var skinnedRenderers = character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var enabledRenderers = 0;
            var drivenRenderers = 0;
            foreach (var renderer in skinnedRenderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                enabledRenderers++;
                if (renderer.rootBone != null &&
                    renderer.rootBone.IsChildOf(animator.transform))
                    drivenRenderers++;
            }
            var controllerName = animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.name : "null";
            var payload = $"{{\"sessionId\":\"avatar-stuck-walk\",\"runId\":\"post-fix\"," +
                          $"\"hypothesisId\":\"B-D\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                          $"\"location\":\"GameBootstrap.InitializeAsync\",\"msg\":\"[DEBUG] visible animator ownership\"," +
                          $"\"data\":{{\"character\":\"{character.name}\",\"animator\":\"{animator.name}\"," +
                          $"\"animatorId\":{animator.GetInstanceID()},\"animatorCount\":{allAnimators.Length}," +
                          $"\"controller\":\"{controllerName}\",\"rendererCount\":{skinnedRenderers.Length}," +
                          $"\"enabledRendererCount\":{enabledRenderers},\"drivenRendererCount\":{drivenRenderers}}}}}";
#if UNITY_EDITOR
            const string debugEndpoint = "http://127.0.0.1:7785/event";
#else
            const string debugEndpoint = "http://127.0.0.1:7785/event";
#endif
            UnityEngine.Networking.UnityWebRequest.Post(
                debugEndpoint, payload, "application/json").SendWebRequest();
#endif
#endregion
            itemRoot = new GameObject("Room Items").transform;
            itemRoot.SetParent(transform, false);
            var aiProvider = new HttpLLMProvider(modelConnection, () => AIConfiguration);
            Session = new GameSession(motor, new RoomNavigationService(Room),
                character.GetComponentInChildren<LengyueWardrobeController>(), items, itemRoot,
                store, anchors, aiProvider, CharacterPersonaLoader.Load(characterPersona),
                Notifications, spontaneousReplyProvider: aiProvider,
                initialSave: save, preparedRoomAnchor: preparedAnchor);
            physicalResponse.BindSession(Session);
            TraceStartup("session_created");
            ResponseEmotionAudio.Attach(
                character, Session, Session.Conversation,
                Session.SpontaneousReplies);
            GesturePerformance.Attach(character, Session);
            TraceStartup("audio_bound");
            var playerHead = Camera.main != null ? Camera.main.transform : null;
            Session.ConfigureSocialAttention(socialAttentionRig, playerHead);
            Session.ConfigureHandHold(character.GetComponent<HandIKRig>(), playerHead);
            Session.ConfigureRealityItems(snapshot,
                character.GetComponent<AvatarSeatingRig>(),
                character.GetComponent<AvatarLyingRig>());
            MiniGames.Bind(Session);
            Session.ConfigureAIWorldContext(
                snapshot, GetComponent<VirtualItemFocusController>(), MiniGames);
#if MATESOUL_DEVICE_PROBES && DEVELOPMENT_BUILD
            gameObject.AddComponent<SeatingDeviceProbe>().Configure(this, character);
            gameObject.AddComponent<DialogueDeviceProbe>().Configure(this);
#endif
            TraceStartup("world_configured");
            ApplyPause();
            startupRoomSource = snapshot.Source;
            TraceStartup("item_restore_start");
            await Session.RestoreItemsAsync();
            token.ThrowIfCancellationRequested();
            TraceStartup(Session.ItemsReady ? "items_restored" : "items_restore_pending");
            ReportStartupReady();
        }

        private async Task<Vector3?> BuildRoomAndFindSpawnAsync(
            RoomMapSnapshot snapshot, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Room.Build(snapshot, Application.isEditor);
            TraceStartup("navmesh_built");
            realityOcclusion?.ApplySnapshot(snapshot, Room);
            TraceStartup("occlusion_applied");
            var desired = snapshot.MainFloor.Centroid;
            if (!Application.isEditor && Camera.main != null)
            {
                desired = Camera.main.transform.position +
                          Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized * 2.2f;
                desired.y = snapshot.MainFloor.Centroid.y;
            }
            // Search the complete room even when the player faces beyond a wall.
#region debug-point F:spawn-search-coverage
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.Log($"MATESOUL_SPAWN_SEARCH desired={desired:F5} " +
                      $"roomCenter={snapshot.Bounds.center:F5} " +
                      $"oldDistance={snapshot.Bounds.extents.magnitude:F5} " +
                      $"searchDistance={Vector3.Distance(desired, snapshot.Bounds.center) + snapshot.Bounds.extents.magnitude:F5}");
#endif
#endregion
            return await WaitForCharacterSpawnAsync(desired, TimeSpan.FromSeconds(8), token);
        }

        private async Task<Vector3?> WaitForCharacterSpawnAsync(
            Vector3 desired, TimeSpan timeout, CancellationToken token)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            do
            {
                token.ThrowIfCancellationRequested();
                // A mesh batch is installed across frames. A wall-only first block
                // does not prove that the measured floor can support the character.
                if ((realityOcclusion == null ||
                     realityOcclusion.HasLiveCollision && !realityOcclusion.HasPendingMeshes) &&
                    Room.TryFindSpawnPosition(desired, out var spawn))
                    return spawn;
                if (watch.Elapsed >= timeout) break;
                await Task.Delay(50, token);
            } while (true);
            return null;
        }

        private async Task<RoomMapSnapshot> LoadRoomMapAsync(CancellationToken token)
        {
            if (Application.isEditor)
                return await new PicoMockRoomMapProvider(editorRoom).LoadAsync(token);

            PXR_Manager.EnableVideoSeeThrough = true;
            if (PicoDeviceEnvironment.IsEmulator)
                return await new PicoSpatialMeshRoomMapProvider(
                    TimeSpan.FromSeconds(15), TraceStartup).LoadAsync(token);

            var fallbackReason = "Scene Capture query deadline exceeded.";
            try
            {
                using (var sceneTimeout = CancellationTokenSource
                           .CreateLinkedTokenSource(token))
                {
                    var sceneTask = new PicoSceneCaptureRoomMapProvider(
                        TraceStartup, TimeSpan.FromSeconds(12))
                        .LoadAsync(sceneTimeout.Token);
                    var deadline = Task.Delay(TimeSpan.FromSeconds(12), token);
                    if (await Task.WhenAny(sceneTask, deadline) == sceneTask)
                        return await sceneTask;
                    token.ThrowIfCancellationRequested();
                    sceneTimeout.Cancel();
                    _ = sceneTask.ContinueWith(completed =>
                        {
                            var observed = completed.Exception;
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted |
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }
            catch (TimeoutException error)
            {
                fallbackReason = error.Message;
            }
            return await LoadSpatialMeshRoomAsync(fallbackReason, token);
        }

        private async Task<RoomMapSnapshot> LoadSpatialMeshRoomAsync(
            string reason, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            TraceStartup("scene_fallback_spatial_mesh");
            Debug.LogWarning($"MATESOUL_ROOM_FALLBACK reason={reason}");
            Status = "正在根据实时空间网格重新定位房间…";
            return await new PicoSpatialMeshRoomMapProvider(
                TimeSpan.FromSeconds(12), TraceStartup).LoadAsync(token);
        }

        private void ReportStartupReady()
        {
            if (startupRoomSource == null || Session == null || Session.IsRestoringItems) return;
            Status = Session.ItemRestoreFailed ? "房间已就绪，旧物品定位失败" : "房间已就绪";
            TraceStartup("business_ready");
            Debug.Log($"MATESOUL_READY source={startupRoomSource} items={Session.Items.Count} " +
                      $"itemState={(Session.ItemsReady ? "ready" : "recovery")} navMs={Room.LastBuildMilliseconds}");
            startupRoomSource = null;
        }

        private void TraceStartup(string stage)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.Log($"MATESOUL_STARTUP stage={stage} elapsedMs={startup.ElapsedMilliseconds} frame={Time.frameCount}");
#endif
            StartupTimeline.Mark(stage);
        }

        private void OnStartupCameraRendered(ScriptableRenderContext context, Camera camera)
        {
            if (camera != Camera.main) return;
            RenderPipelineManager.endCameraRendering -= OnStartupCameraRendered;
            // This is Unity's render callback, not proof of a frame displayed by the XR compositor.
            TraceStartup("first_main_camera_render");
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root) SetLayerRecursively(child, layer);
        }

        private void Update()
        {
            Session?.Tick(Time.deltaTime);
            ReportStartupReady();
#region debug-point A-E:item-memory-periodic-state
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            if (Session == null || Time.unscaledTime < nextItemMemoryReport) return;
            var now = Time.unscaledTime;
            var elapsed = now - previousItemMemoryReportTime;
            var frames = Time.frameCount - previousItemMemoryReportFrame;
            var itemCount = 0;
            var heldCount = 0;
            var awakeBodyCount = 0;
            var poweredRadioCount = 0;
            foreach (var view in Session.ItemViews)
            {
                if (view == null) continue;
                itemCount++;
                if (view.IsHeld) heldCount++;
                if (view.Body?.Rigidbody != null && !view.Body.Rigidbody.IsSleeping())
                    awakeBodyCount++;
                var radio = view.GetComponent<RadioController>();
                if (radio != null && radio.IsPoweredOn) poweredRadioCount++;
            }
            var debrisCount = 0;
            var fragmentCount = 0;
            foreach (var value in Session.Debris)
            {
                if (value == null) continue;
                debrisCount++;
                fragmentCount += value.FragmentCount;
            }
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var fps = elapsed > .001f ? frames / elapsed : 0f;
            var payload =
                $"{{\"sessionId\":\"emulator-memory-growth\",\"runId\":\"interaction-correlation\"," +
                $"\"hypothesisId\":\"A-E\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                $"\"location\":\"GameBootstrap.Update\",\"msg\":\"[DEBUG] interaction runtime sample\"," +
                $"\"data\":{{\"fps\":{fps.ToString("R", culture)},\"items\":{itemCount}," +
                $"\"held\":{heldCount},\"awakeBodies\":{awakeBodyCount}," +
                $"\"debris\":{debrisCount},\"fragments\":{fragmentCount}," +
                $"\"poweredRadios\":{poweredRadioCount},\"frame\":{Time.frameCount}," +
                $"\"targetFrameRate\":{Application.targetFrameRate}}}}}";
            var request = UnityEngine.Networking.UnityWebRequest.Post(
                "http://127.0.0.1:7791/event", payload, "application/json");
            var operation = request.SendWebRequest();
            operation.completed += _ => request.Dispose();
            previousItemMemoryReportTime = now;
            previousItemMemoryReportFrame = Time.frameCount;
            nextItemMemoryReport = now + 2f;
#endif
#endregion
        }
        public void TogglePause() { manualPause = !manualPause; ApplyPause(); }
        private void OnApplicationPause(bool paused)
        {
            appPause = paused;
#region debug-point B:application-pause
            ReportDialogueLifecycle("GameBootstrap.OnApplicationPause", "pause-callback");
#endregion
            ApplyPause();
        }
        private void OnApplicationFocus(bool focused)
        {
            hasFocus = focused;
#region debug-point B:application-focus
            ReportDialogueLifecycle("GameBootstrap.OnApplicationFocus", "focus-callback");
#endregion
            ApplyPause();
        }
        private void ApplyPause()
        {
            var paused = manualPause || appPause || !hasFocus;
#region debug-point B:apply-pause
            ReportDialogueLifecycle("GameBootstrap.ApplyPause", paused ? "pause-requested" : "resume-requested");
#endregion
            if (paused && MiniGames.Active != null)
            {
                MiniGames.Stop();
                Notifications.ShowHint("小游戏已因暂停结束，可从互动栏重新开始。");
            }
            try { Session?.SetPaused(paused); }
            catch (Exception error) { Status = $"存档失败：{error.Message}"; Debug.LogException(error, this); }
            if (animator != null) animator.speed = paused ? 0 : 1;
        }
#region debug-point B-C:application-lifecycle-report
        private void ReportDialogueLifecycle(string location, string reason)
        {
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            var state = Session == null ? "no-session" : Session.Conversation.State.ToString();
            var payload =
                $"{{\"sessionId\":\"dialogue-font-tofu\",\"runId\":\"pre-fix-iteration-3\"," +
                $"\"hypothesisId\":\"B-C\",\"location\":\"{location}\"," +
                $"\"msg\":\"[DEBUG] application lifecycle\",\"data\":{{" +
                $"\"reason\":\"{reason}\",\"manualPause\":{manualPause.ToString().ToLowerInvariant()}," +
                $"\"appPause\":{appPause.ToString().ToLowerInvariant()}," +
                $"\"hasFocus\":{hasFocus.ToString().ToLowerInvariant()}," +
                $"\"applicationFocused\":{Application.isFocused.ToString().ToLowerInvariant()}," +
                $"\"conversationState\":\"{state}\",\"frame\":{Time.frameCount}}}}}";
            var request = UnityEngine.Networking.UnityWebRequest.Post(
                "http://127.0.0.1:7785/event", payload, "application/json");
            var operation = request.SendWebRequest();
            operation.completed += _ => request.Dispose();
#endif
        }
#endregion
        private void OnDestroy()
        {
#region debug-point C:bootstrap-destroy
            ReportDialogueLifecycle("GameBootstrap.OnDestroy", "bootstrap-destroy");
#endregion
            lifetime?.Cancel();
            RenderPipelineManager.endCameraRendering -= OnStartupCameraRendered;
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
