using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ByteDance.PICO.XR;
using MateSoul.RoomNavigation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;
using Unity.XR.CoreUtils;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent]
    public sealed class RealityOcclusionMeshRenderer : MonoBehaviour
    {
        private const string SpatialMeshShaderResource =
            "RealityOcclusion/SpatialMeshDepth";
        // 快照网格来自上一次房间捕获；重新标定后会整体错位，形成“隐形墙”裁切角色。
        // 只用与当前房间一致、且不会包住观察者的快照网格写深度。
        private const float SnapshotViewerMargin = .25f;
        private const float SnapshotMaximumExtent = 12f;
        private const float SnapshotMinimumViewerDistance = .3f;
        private const float SnapshotFloorTolerance = .6f;
        private const float SnapshotRoomMargin = .5f;
        // Blocks the SDK stops observing are kept for collision continuity, but a
        // block that stays unseen this long is stale room geometry from an earlier
        // calibration and must stop writing depth.
        private const float RetainedMeshLifetime = 10f;
        // Runtime spatial-mesh blocks need the same suspicion as snapshot meshes:
        // a block measured under an earlier tracking origin, or carried over from
        // stale runtime scan data, lands inside the current room as an invisible
        // depth-only plane that slices the avatar. Reject the physically impossible
        // ones instead of trusting every block the SDK reports.
        private const float NativeViewerMargin = .2f;
        private const float NativeMinimumViewerDistance = .15f;
        private const float NativeMaximumExtent = 12f;
        private const float NativeFloorTolerance = .35f;
        private const float NativeRoomMargin = 2f;
        // Two independent occluders can hide the avatar: the depth-only mesh proxies
        // (SpatialMeshDepth) and the environment-depth alpha composite. They are
        // switched independently at runtime so a single device run can tell which
        // one produces the slice.
        private const string OcclusionFlagsFile = "occlusion-flags.txt";
        private const float InstallLogInterval = 5f;
        // One MeshCollider cook over CookWarnMs is already most of a 90Hz frame (11.1ms);
        // log it so the spike can be attributed to spatial-mesh collision.
        private const float CookWarnMs = 8f;
        // Backlog above this many queued blocks means the 2ms cook budget cannot keep
        // up with the SDK update rate and collision is falling behind the room.
        private const int BacklogWarnBlocks = 8;
        // 覆盖度门控：角色屏幕投影区域已经被实时网格覆盖时，环境深度合成在该
        // 区域不再提供额外遮挡，整趟合成可以直接跳过，角色也就不会被它裁切。
        private const int CoverageSampleSteps = 5;
        private const float CoverageSampleThreshold = .85f;
        // 只有比角色更近的网格才可能替代环境深度去遮挡角色；容差用于容忍
        // 网格与角色包围盒在深度方向的轻微交叠。
        private const float CoverageDepthTolerance = .25f;
        private const float CoverageLogInterval = 5f;
        private static bool meshOcclusionAllowed = true;
        private static bool environmentOcclusionAllowed = true;
        private static bool coverageGateAllowed = true;
        private static bool meshVisualizationAllowed;
        private static RealityOcclusionMeshRenderer activeInstance;
        private static Transform coverageTarget;
        private static float nextFlagReload;
        // HUD 切换后必须压住文件热重载，否则按钮会立刻被旧的
        // occlusion-flags.txt 覆盖回原值。
        private static float suppressFlagReloadUntil;

        private readonly Dictionary<Guid, InstallLog> installLogs =
            new Dictionary<Guid, InstallLog>();
        private readonly Dictionary<Guid, MeshEntry> nativeMeshes =
            new Dictionary<Guid, MeshEntry>();
        private readonly Dictionary<Guid, PxrSpatialMeshInfo> pendingMeshes =
            new Dictionary<Guid, PxrSpatialMeshInfo>();
        private readonly List<MeshEntry> snapshotMeshes = new List<MeshEntry>();
        private readonly MeasuredMeshRetention retention = new MeasuredMeshRetention();
        private readonly List<Guid> expiredMeshes = new List<Guid>();
        private readonly List<Guid> rejectedByRoom = new List<Guid>();
        private readonly List<XRMeshSubsystem> startedSubsystems =
            new List<XRMeshSubsystem>();
        private readonly List<Rect> coverageRects = new List<Rect>();
        private readonly List<Renderer> coverageRenderers = new List<Renderer>();
        private Material depthMaterial;
        private RoomMapSnapshot snapshot;
        private RuntimeRoomNavigation roomNavigation;
        private Camera viewCamera;
        private XROrigin trackedOrigin;
        private Transform coverageRendererSource;
        private bool meshDepthEnabled;
        private int coverageFrame = -1;
        private bool coverageResult;
        private bool lastCoverageResult;
        private float coverageRatio;
        private int coverageMeshCount;
        private string coverageReason = "none";
        private float nextCoverageLog;
        // 门控只记录「被多少块覆盖」，无法回答「是哪几块、它们比角色近多少、
        // 是否真的压在角色中心」。这三组字段只保存最近的 4 块，够定位误导来源。
        private readonly Guid[] coverageNearestIds = new Guid[4];
        private readonly float[] coverageNearestDepths = new float[4];
        private readonly bool[] coverageNearestHits = new bool[4];
        private int coverageNearestCount;
        private float coverageTargetDepth;
        private Rect coverageTargetRect;
        private int coverageFlips;
        private int coverageContributors;
        private int coverageEvaluations;
        private float nextDepthStateLog;
        private float nextNativePrune;
        // Geometry decisions are logged in every build: a device report with no
        // logs cannot distinguish "the wall was rejected" from "the wall never
        // arrived".
        private float nextRejectionLog;
        private int rejectedSinceLastLog;
        private int installsSinceSummary;
        private int installsSuppressed;
        private float nextInstallSummary;
        private float nextCookLog;
        private float nextBacklogLog;
        // 贴近/包住观察者的网格块仍然要参与碰撞（床面、桌面就在这些块里），
        // 只是不能写深度，否则会在眼前生成一层隐形遮挡面。这些块照常安装，
        // 但渲染器保持关闭，等观察者离开后重新开启。
        private readonly HashSet<Guid> depthSuppressed = new HashSet<Guid>();
        private float nextSuppressedDepthLog;
#region debug-point A-D:live-room-state
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private float debugNextRoomReport;
        private int rejectedEnclosingViewer;
        private int rejectedAgainstViewer;
        private int rejectedBelowFloor;
        private int rejectedOutsideRoom;
        private int rejectedOversized;
        private int rejectedInvalidBlock;
#endif
#endregion
        public int NativeMeshCount => nativeMeshes.Count;
        public int SnapshotMeshCount => snapshotMeshes.Count;
        public Material DepthMaterial => depthMaterial;
        public bool MeshDepthEnabled => meshDepthEnabled;
        public bool HasLiveCollision => nativeMeshes.Count > 0;
        public bool HasPendingMeshes => pendingMeshes.Count > 0;
        public static bool EnvironmentDepthAvailable =>
            !Application.isEditor && !PicoDeviceEnvironment.IsEmulator;
        public static bool MeshOcclusionAllowed => meshOcclusionAllowed;
        public static bool EnvironmentOcclusionAllowed => environmentOcclusionAllowed;
        public static bool CoverageGateAllowed => coverageGateAllowed;
        public static bool MeshVisualizationAllowed => meshVisualizationAllowed;
        public static int ActiveNativeMeshCount =>
            activeInstance != null ? activeInstance.nativeMeshes.Count : 0;
        public static int CurrentCoverageGated =>
            activeInstance != null && activeInstance.coverageResult ? 1 : 0;
        public static bool MeshDepthActive => activeInstance != null && activeInstance.meshDepthEnabled;

        // 角色根节点由 GameBootstrap 注入；覆盖度门控用它算屏幕投影区域。
        public static Transform CoverageTarget
        {
            get => coverageTarget;
            set
            {
                if (coverageTarget == value) return;
                coverageTarget = value;
                activeInstance?.InvalidateCoverage();
            }
        }

        private struct InstallLog
        {
            public Vector3 Center;
            public Vector3 Size;
            public float NextAllowed;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void LoadOcclusionFlags()
        {
            TryReadOcclusionFlags(out meshOcclusionAllowed, out environmentOcclusionAllowed,
                out coverageGateAllowed, out meshVisualizationAllowed, out var source);
            Debug.Log($"MATESOUL_OCCLUSION_FLAGS mesh={(meshOcclusionAllowed ? 1 : 0)} " +
                      $"env={(environmentOcclusionAllowed ? 1 : 0)} " +
                      $"gate={(coverageGateAllowed ? 1 : 0)} " +
                      $"viz={(meshVisualizationAllowed ? 1 : 0)} source={source}");
        }

        private static bool ReloadOcclusionFlags()
        {
            if (Time.unscaledTime < suppressFlagReloadUntil) return false;
            if (Time.unscaledTime < nextFlagReload) return false;
            nextFlagReload = Time.unscaledTime + 2f;
            if (!TryReadOcclusionFlags(out var mesh, out var environment, out var gate,
                    out var viz, out var source))
                return false;
            if (mesh == meshOcclusionAllowed && environment == environmentOcclusionAllowed &&
                gate == coverageGateAllowed && viz == meshVisualizationAllowed)
                return false;
            meshOcclusionAllowed = mesh;
            environmentOcclusionAllowed = environment;
            coverageGateAllowed = gate;
            meshVisualizationAllowed = viz;
            SpatialMeshVisualizer.RefreshVisibility();
            Debug.Log($"MATESOUL_OCCLUSION_FLAGS mesh={(mesh ? 1 : 0)} " +
                      $"env={(environment ? 1 : 0)} gate={(gate ? 1 : 0)} " +
                      $"viz={(viz ? 1 : 0)} source={source} changed=True");
            return true;
        }

        private static bool TryReadOcclusionFlags(
            out bool mesh, out bool environment, out bool coverageGate,
            out bool visualization, out string source)
        {
            mesh = meshOcclusionAllowed;
            environment = environmentOcclusionAllowed;
            coverageGate = coverageGateAllowed;
            visualization = meshVisualizationAllowed;
            source = "default";
            try
            {
                var path = Path.Combine(
                    Application.persistentDataPath, "diagnostics", OcclusionFlagsFile);
                if (!File.Exists(path)) return false;
                source = path;
                foreach (var line in File.ReadAllLines(path))
                {
                    var separator = line.IndexOf('=');
                    if (separator <= 0) continue;
                    var key = line.Substring(0, separator).Trim().ToLowerInvariant();
                    if (key != "mesh" && key != "env" && key != "environment" &&
                        key != "gate" && key != "viz")
                        continue;
                    var enabled = line.Substring(separator + 1).Trim() != "0";
                    if (key == "mesh") mesh = enabled;
                    else if (key == "env" || key == "environment") environment = enabled;
                    else if (key == "gate") coverageGate = enabled;
                    else visualization = enabled;
                }
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        // HUD 按钮入口：先改内存开关，让当前帧就生效，再把开关写回文件，
        // 以便下次启动沿用同一组合。
        public static void SetMeshOcclusion(bool enabled)
        {
            if (meshOcclusionAllowed == enabled) return;
            meshOcclusionAllowed = enabled;
            suppressFlagReloadUntil = Time.unscaledTime + 10f;
            WriteOcclusionFlags("ui");
        }

        public static void SetEnvironmentOcclusion(bool enabled)
        {
            if (environmentOcclusionAllowed == enabled) return;
            environmentOcclusionAllowed = enabled;
            suppressFlagReloadUntil = Time.unscaledTime + 10f;
            WriteOcclusionFlags("ui");
        }

        public static void SetCoverageGate(bool enabled)
        {
            if (coverageGateAllowed == enabled) return;
            coverageGateAllowed = enabled;
            suppressFlagReloadUntil = Time.unscaledTime + 10f;
            activeInstance?.InvalidateCoverage();
            WriteOcclusionFlags("ui");
        }

        public static void SetMeshVisualization(bool enabled)
        {
            if (meshVisualizationAllowed == enabled) return;
            meshVisualizationAllowed = enabled;
            suppressFlagReloadUntil = Time.unscaledTime + 10f;
            WriteOcclusionFlags("ui");
            SpatialMeshVisualizer.RefreshVisibility();
        }

        // 一键切到「纯环境深度」定位组合：关网格遮挡、关覆盖度门控，只留环境深度合成。
        // 这样角色被真实表面挡住时，画面若仍然不裁切，就能把问题锁定在
        // 「alpha 没写进画面」或「写进去了但合成器没采用」这两者之一。
        public static void SetEnvironmentOnlyPreset()
        {
            meshOcclusionAllowed = false;
            environmentOcclusionAllowed = true;
            coverageGateAllowed = false;
            suppressFlagReloadUntil = Time.unscaledTime + 10f;
            activeInstance?.InvalidateCoverage();
            WriteOcclusionFlags("ui-env-only-preset");
        }

        private static void WriteOcclusionFlags(string reason)
        {
            var path = Path.Combine(
                Application.persistentDataPath, "diagnostics", OcclusionFlagsFile);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,
                    $"mesh={(meshOcclusionAllowed ? 1 : 0)}\n" +
                    $"env={(environmentOcclusionAllowed ? 1 : 0)}\n" +
                    $"gate={(coverageGateAllowed ? 1 : 0)}\n" +
                    $"viz={(meshVisualizationAllowed ? 1 : 0)}\n");
            }
            catch (IOException error)
            {
                Debug.LogWarning($"MATESOUL_OCCLUSION_FLAGS write failed: {error.Message}");
            }
            catch (UnauthorizedAccessException error)
            {
                Debug.LogWarning($"MATESOUL_OCCLUSION_FLAGS write failed: {error.Message}");
            }
            Debug.Log($"MATESOUL_OCCLUSION_FLAGS mesh={(meshOcclusionAllowed ? 1 : 0)} " +
                      $"env={(environmentOcclusionAllowed ? 1 : 0)} " +
                      $"gate={(coverageGateAllowed ? 1 : 0)} " +
                      $"viz={(meshVisualizationAllowed ? 1 : 0)} " +
                      $"source={path} reason={reason}");
        }

        public static RealityOcclusionMeshRenderer Install(Camera xrCamera)
        {
            if (Application.isEditor || xrCamera == null) return null;

            var origin = xrCamera.GetComponentInParent<XROrigin>();
            var host = origin != null ? origin.gameObject : xrCamera.transform.root.gameObject;
            if (EnvironmentDepthAvailable)
            {
                var manager = host.GetComponent<PXR_EnvironmentDepthManager>() ??
                              host.AddComponent<PXR_EnvironmentDepthManager>();
                manager.SetTrackingOrigin(origin != null && origin.CameraFloorOffsetObject != null
                    ? origin.CameraFloorOffsetObject.transform : host.transform);
            }
            return host.GetComponent<RealityOcclusionMeshRenderer>() ??
                   host.AddComponent<RealityOcclusionMeshRenderer>();
        }

        private void Awake()
        {
            activeInstance = this;
            trackedOrigin = GetComponent<XROrigin>();
            var shader = Resources.Load<Shader>(SpatialMeshShaderResource);
            if (shader == null)
                throw new InvalidOperationException(
                    $"Reality occlusion shader is missing: {SpatialMeshShaderResource}.");
            depthMaterial = new Material(shader)
            {
                name = "Reality Spatial Mesh Depth (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private void OnEnable()
        {
            PXR_Manager.SpatialMeshDataUpdated += OnSpatialMeshDataUpdated;
            foreach (var entry in nativeMeshes.Values) entry.GameObject.SetActive(true);
            StartMeshSubsystems();
            RefreshCachedNativeMeshes();
            if (snapshot != null) BuildSnapshotMeshes(snapshot);
        }

        private void OnDisable()
        {
            PXR_Manager.SpatialMeshDataUpdated -= OnSpatialMeshDataUpdated;
            pendingMeshes.Clear();
            foreach (var entry in nativeMeshes.Values) entry.GameObject.SetActive(false);
            foreach (var entry in snapshotMeshes) entry.GameObject.SetActive(false);
            foreach (var subsystem in startedSubsystems)
                if (subsystem != null && subsystem.running)
                    subsystem.Stop();
            startedSubsystems.Clear();
        }

        public void ApplySnapshot(RoomMapSnapshot snapshot, RuntimeRoomNavigation navigation = null)
        {
            if (this.snapshot != null && snapshot != null && this.snapshot.Id != snapshot.Id)
            {
                ClearEntries(nativeMeshes.Values);
                nativeMeshes.Clear();
                depthSuppressed.Clear();
                retention.Clear();
                pendingMeshes.Clear();
                roomNavigation?.ClearLiveSurfaces();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                rejectedEnclosingViewer = rejectedAgainstViewer = rejectedBelowFloor = 0;
                rejectedOutsideRoom = rejectedOversized = rejectedInvalidBlock = 0;
                rejectedSinceLastLog = 0;
#endif
            }
            this.snapshot = snapshot;
            roomNavigation = navigation;
            BuildSnapshotMeshes(snapshot);
            // Entries built before the room map arrived were not gated against it.
            // Drop the ones that cannot belong to this room before re-queueing the
            // SDK cache, otherwise stale blocks keep writing depth for the session.
            if (snapshot != null)
            {
                rejectedByRoom.Clear();
                foreach (var pair in nativeMeshes)
                    if (NativeMeshRejection(pair.Value.Renderer.bounds) != null)
                        rejectedByRoom.Add(pair.Key);
                foreach (var id in rejectedByRoom) RemoveNativeMesh(id);
                rejectedByRoom.Clear();
            }
            // A room replacement clears native entries. Requeue the SDK cache even
            // if no new mesh event arrives while the headset is stationary.
            RefreshCachedNativeMeshes();
            foreach (var pair in nativeMeshes)
                UpdateNavigationSurface(pair.Key, pair.Value);
        }

        private void BuildSnapshotMeshes(RoomMapSnapshot room)
        {
            ClearEntries(snapshotMeshes);
            snapshotMeshes.Clear();
            if (room == null) return;

            // Only measured surface meshes can provide depth. Floor polygons and
            // semantic boxes are navigation proxies and are not rendered here.
            foreach (var surface in room.SurfaceMeshes)
            {
                if (!TryCreateMesh(
                        $"Reality Surface {surface.Id}",
                        surface.Vertices,
                        surface.Indices,
                        out var mesh))
                    continue;
                snapshotMeshes.Add(CreateEntry(mesh.name, mesh, Vector3.zero,
                    Quaternion.identity, false, surface.SourceId));
            }
            UpdateDepthRendering(true);
        }

        private void RefreshCachedNativeMeshes()
        {
            if (Application.isEditor) return;
            var cached = PXR_Plugin.MixedReality.SpatialMeshData.Values
                .Where(mesh => mesh.state != MeshChangeState.Removed)
                .ToList();
            if (cached.Count > 0) OnSpatialMeshDataUpdated(cached);
        }

        private void OnSpatialMeshDataUpdated(List<PxrSpatialMeshInfo> blocks)
        {
            if (blocks == null) return;
            foreach (var block in blocks)
                if (block.state != MeshChangeState.Unchanged ||
                    !nativeMeshes.ContainsKey(block.uuid))
                    pendingMeshes[block.uuid] = block;
        }

        private void ApplyPendingMeshes()
        {
            using var perfApply = PerfMonitor.Sample("Occlusion.Apply");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var now = Time.unscaledTime;
            var changed = false;
            // Expire retained blocks before cooking new ones: after re-calibration the
            // old volume is never re-observed, and its depth would slice the avatar.
            retention.Expire(now, RetainedMeshLifetime, expiredMeshes);
            if (expiredMeshes.Count > 0)
            {
                Debug.Log($"MATESOUL_GEOMETRY_RETENTION_EXPIRED count={expiredMeshes.Count} " +
                          $"ids=[{string.Join(",", expiredMeshes.Select(id => id.ToString("N")))}] " +
                          $"retainedAfter={retention.RetainedCount} " +
                          $"camera={ViewCameraDescription()}");
                foreach (var expired in expiredMeshes) RemoveNativeMesh(expired);
                expiredMeshes.Clear();
                changed = true;
            }
            // Coalesce each UUID and spend at most 2ms between complete mesh cooks.
            // One block may exceed that budget; it is never partially installed.
            while (pendingMeshes.Count > 0 && (!changed || watch.Elapsed.TotalMilliseconds < 2))
            {
                var block = pendingMeshes.First().Value;
                pendingMeshes.Remove(block.uuid);
                if (block.state == MeshChangeState.Removed)
                {
                    retention.MarkUnobserved(block.uuid, now);
                    continue;
                }

                // The SDK sends the complete cache, including Unchanged blocks.
                // Do not rebuild and re-cook their physics meshes on every update.
                if (block.state == MeshChangeState.Unchanged &&
                    nativeMeshes.ContainsKey(block.uuid))
                    continue;
                if (block.vertices == null || block.indices == null ||
                    block.vertices.Length < 3 ||
                    !IsValidPose(block.position, block.rotation))
                {
                    RecordRejectedBlock(block, default, "invalid-block");
                    // An update that became invalid must not leave its previous
                    // geometry writing depth.
                    RemoveNativeMesh(block.uuid);
                    continue;
                }

                // A block is only allowed to write depth and drive collision when it
                // can physically belong to the current room.
                var blockBounds = MeasureBlock(block);
                var rejection = NativeMeshRejection(blockBounds);
                // A block that only surrounds the viewer (the wall you leaned into,
                // the ceiling above your head) still has to collide: the bed and
                // table tops inside it are what items are placed on. Dropping the
                // whole block is what made placed items fall through. Only its
                // depth write is unsafe, so it is installed with the renderer off.
                var viewerProximity = rejection == "encloses-viewer" ||
                                      rejection == "against-viewer";
                if (rejection != null && !viewerProximity)
                {
                    RecordRejectedBlock(block, blockBounds, rejection);
                    RemoveNativeMesh(block.uuid);
                    continue;
                }
                if (viewerProximity && now >= nextSuppressedDepthLog)
                {
                    nextSuppressedDepthLog = now + 2f;
                    Debug.Log($"MATESOUL_GEOMETRY_SUPPRESS_DEPTH uuid={block.uuid:N} " +
                              $"reason={rejection} collision=kept " +
                              $"center={blockBounds.center:F2} size={blockBounds.size:F2} " +
                              $"camera={ViewCameraDescription()}");
                }
                if (!TryCreateMesh(
                        $"Reality Native Mesh {block.uuid:N}",
                        block.vertices,
                        Array.ConvertAll(block.indices, value => (int)value),
                        out var mesh))
                {
                    RecordRejectedBlock(block, blockBounds, "mesh-build-failed");
                    RemoveNativeMesh(block.uuid);
                    continue;
                }

                // Cook and register the replacement before retiring old collision.
                // MeshCollider cooking is synchronous and scales with block size, so a
                // single large block can blow the frame even though the loop budget is 2ms.
                var cookStart = watch.Elapsed.TotalMilliseconds;
                var replacement = CreateEntry(
                    mesh.name,
                    mesh,
                    block.position,
                    block.rotation,
                    true,
                    block.uuid.GetHashCode());
                var cookMs = watch.Elapsed.TotalMilliseconds - cookStart;
                // 只压掉深度写入，保留 MeshCollider：物品落在床面/桌面上靠的就是
                // 这些块的碰撞体。
                if (viewerProximity)
                {
                    replacement.Renderer.enabled = false;
                    depthSuppressed.Add(block.uuid);
                }
                else
                {
                    depthSuppressed.Remove(block.uuid);
                }
                PerfMonitor.Count("Mesh.Cook");
                if (cookMs >= CookWarnMs && now >= nextCookLog)
                {
                    nextCookLog = now + 1f;
                    Debug.Log($"MATESOUL_PERF_COOK uuid={block.uuid:N} ms={cookMs:F1} " +
                              $"verts={block.vertices.Length} tris={block.indices.Length / 3} " +
                              $"pending={pendingMeshes.Count} frame={Time.frameCount}");
                }
                foreach (var obsolete in retention.Observe(block, now)) RemoveNativeMesh(obsolete);
                if (nativeMeshes.TryGetValue(block.uuid, out var previous))
                    DestroyEntry(previous);
                nativeMeshes[block.uuid] = replacement;
                UpdateNavigationSurface(block.uuid, replacement);
                changed = true;
                // Record what is allowed to write depth, so a later clipping report
                // can be matched against the block that produced it. The SDK re-sends
                // the same block many times per second; logging every rebuild fills the
                // device log before the reported artifact even appears.
                installsSinceSummary++;
                if (!ShouldLogInstall(block.uuid, blockBounds, now))
                {
                    installsSuppressed++;
                    continue;
                }
                var installCamera = ViewCamera;
                var installFloor = snapshot?.MainFloor != null
                    ? snapshot.MainFloor.Centroid.y : 0f;
                var installDistance = installCamera != null
                    ? Mathf.Sqrt(blockBounds.SqrDistance(installCamera.transform.position))
                    : -1f;
                Debug.Log($"MATESOUL_GEOMETRY_INSTALL uuid={block.uuid:N} " +
                          $"state={block.state} " +
                          $"center={blockBounds.center} size={blockBounds.size} " +
                          $"minY={blockBounds.min.y:F2} floorY={installFloor:F2} " +
                          $"cameraDistance={installDistance:F2} " +
                          $"verts={block.vertices.Length} tris={block.indices.Length / 3} " +
                          $"camera={ViewCameraDescription()}");
            }
            if (changed)
            {
                UpdateDepthRendering(true);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.Log($"MATESOUL_GEOMETRY_LIVE nativeMeshes={nativeMeshes.Count} " +
                          $"pendingMeshBlocks={pendingMeshes.Count} " +
                          $"snapshotDepthMeshes={snapshotMeshes.Count} " +
                          $"retainedMeshes={retention.RetainedCount} " +
                          $"meshDepthEnabled={meshDepthEnabled} " +
                          $"camera={ViewCameraDescription()}");
#endif
            }
            if (pendingMeshes.Count > 0)
            {
                PerfMonitor.Count("Mesh.Backlog");
                if (pendingMeshes.Count >= BacklogWarnBlocks && now >= nextBacklogLog)
                {
                    nextBacklogLog = now + 1f;
                    Debug.Log($"MATESOUL_PERF_BACKLOG pending={pendingMeshes.Count} " +
                              $"native={nativeMeshes.Count} " +
                              $"applyMs={watch.Elapsed.TotalMilliseconds:F1} frame={Time.frameCount}");
                }
            }
        }

        private bool ShouldLogInstall(Guid id, Bounds bounds, float now)
        {
            if (!installLogs.TryGetValue(id, out var logged))
            {
                installLogs[id] = new InstallLog
                {
                    Center = bounds.center,
                    Size = bounds.size,
                    NextAllowed = now + InstallLogInterval
                };
                return true;
            }
            var moved = (logged.Center - bounds.center).sqrMagnitude > .09f ||
                        (logged.Size - bounds.size).sqrMagnitude > .09f;
            if (!moved || now < logged.NextAllowed) return false;
            logged.Center = bounds.center;
            logged.Size = bounds.size;
            logged.NextAllowed = now + InstallLogInterval;
            installLogs[id] = logged;
            return true;
        }

        private void ReportInstalls()
        {
            if (Time.unscaledTime < nextInstallSummary) return;
            nextInstallSummary = Time.unscaledTime + 10f;
            if (installsSinceSummary == 0) return;
            Debug.Log($"MATESOUL_GEOMETRY_INSTALL_SUMMARY installs={installsSinceSummary} " +
                      $"suppressed={installsSuppressed} blocks={installLogs.Count} " +
                      $"nativeMeshes={nativeMeshes.Count} retained={retention.RetainedCount} " +
                      $"pending={pendingMeshes.Count} meshDepthEnabled={meshDepthEnabled} " +
                      $"meshOcclusion={(meshOcclusionAllowed ? 1 : 0)} " +
                      $"envOcclusion={(environmentOcclusionAllowed ? 1 : 0)} " +
                      $"camera={ViewCameraDescription()}");
            installsSinceSummary = 0;
            installsSuppressed = 0;
        }

        private void LateUpdate()
        {
            ApplyPendingMeshes();
            // Re-validate installed entries: a runtime tracking-origin recenter moves
            // every measured block relative to the room without any SDK change event.
            PruneNativeMeshes();
            var flagsChanged = ReloadOcclusionFlags();
            UpdateDepthRendering(flagsChanged);
            ReportDepthState();
            ReportInstalls();
#region debug-point A-D:live-room-state
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Time.unscaledTime >= debugNextRoomReport)
            {
                debugNextRoomReport = Time.unscaledTime + 2f;
                var manager = GetComponent<PXR_EnvironmentDepthManager>();
                var raw = Shader.GetGlobalTexture("_EnvironmentDepthTexture");
                var preprocessed = Shader.GetGlobalTexture("_PreprocessedEnvironmentDepthTexture");
                var camera = ViewCamera;
                var viewer = camera != null ? camera.transform.position : Vector3.zero;
                var origin = trackedOrigin != null ? trackedOrigin.transform.position : Vector3.zero;
                var floor = snapshot?.MainFloor != null ? snapshot.MainFloor.Centroid.y : 0f;
                var nearest = float.MaxValue;
                var withinViewerRange = 0;
                var nearestId = "none";
                var nearestCenter = Vector3.zero;
                var nearestSize = Vector3.zero;
                foreach (var pair in nativeMeshes)
                {
                    var bounds = pair.Value.Renderer.bounds;
                    var distance = bounds.SqrDistance(viewer);
                    if (distance < nearest)
                    {
                        nearest = distance;
                        nearestId = pair.Key.ToString("N");
                        nearestCenter = bounds.center;
                        nearestSize = bounds.size;
                    }
                    if (distance <= 2.25f) withinViewerRange++;
                }
                var nearestDistance = nearest == float.MaxValue ? -1f : Mathf.Sqrt(nearest);
                var snapshotEnabled = 0;
                foreach (var entry in snapshotMeshes)
                    if (entry.Renderer.enabled) snapshotEnabled++;
                Debug.Log("MATESOUL_GEOMETRY_TRACE " +
                    $"{{\"sessionId\":\"live-room-geometry\",\"runId\":\"post-fix\"," +
                    $"\"hypothesisId\":\"A-D\",\"location\":\"RealityOcclusionMeshRenderer.LateUpdate\"," +
                    $"\"msg\":\"[DEBUG] live geometry state\",\"data\":{{" +
                    $"\"cameraX\":{viewer.x:F2},\"cameraY\":{viewer.y:F2},\"cameraZ\":{viewer.z:F2}," +
                    $"\"originX\":{origin.x:F2},\"originY\":{origin.y:F2},\"originZ\":{origin.z:F2}," +
                    $"\"floorY\":{floor:F2},\"snapshotId\":\"{snapshot?.Id}\"," +
                    $"\"snapshotSource\":\"{snapshot?.Source}\"," +
                    $"\"snapshotMeshes\":{snapshotMeshes.Count}," +
                    $"\"snapshotMeshesEnabled\":{snapshotEnabled}," +
                    $"\"nativeMeshes\":{nativeMeshes.Count}," +
                    $"\"pendingMeshBlocks\":{pendingMeshes.Count}," +
                    $"\"retainedMeshes\":{retention.RetainedCount}," +
                    $"\"nearestOccluderDistance\":{nearestDistance:F2}," +
                    $"\"nearestOccluderId\":\"{nearestId}\"," +
                    $"\"nearestOccluderCenter\":\"{nearestCenter.x:F2},{nearestCenter.y:F2},{nearestCenter.z:F2}\"," +
                    $"\"nearestOccluderSize\":\"{nearestSize.x:F2},{nearestSize.y:F2},{nearestSize.z:F2}\"," +
                    $"\"nativeOccludersWithin1_5m\":{withinViewerRange}," +
                    $"\"rejectedEnclosesViewer\":{rejectedEnclosingViewer}," +
                    $"\"rejectedAgainstViewer\":{rejectedAgainstViewer}," +
                    $"\"rejectedBelowFloor\":{rejectedBelowFloor}," +
                    $"\"rejectedOutsideRoom\":{rejectedOutsideRoom}," +
                    $"\"rejectedOversized\":{rejectedOversized}," +
                    $"\"rejectedInvalid\":{rejectedInvalidBlock}," +
                    $"\"meshDepth\":{meshDepthEnabled.ToString().ToLowerInvariant()}," +
                    $"\"depthAvailable\":{(manager != null && manager.IsDepthAvailable).ToString().ToLowerInvariant()}," +
                    $"\"depthSwitch\":{Shader.GetGlobalFloat("_EnvironmentOcclusionEnabled")}," +
                    $"\"rawWidth\":{(raw != null ? raw.width : 0)}," +
                    $"\"processedWidth\":{(preprocessed != null ? preprocessed.width : 0)}," +
                    $"\"floorColliders\":{(roomNavigation != null ? roomNavigation.GetComponentsInChildren<MeshCollider>().Length : 0)}}}}}");
            }
#endif
#endregion
        }

        private void UpdateDepthRendering(bool force = false)
        {
            // Keep measured real-world occlusion even when depth textures exist.
            // Texture availability does not prove the depth composite is sufficient.
            var enabled = meshOcclusionAllowed && nativeMeshes.Count > 0;
            if (force || meshDepthEnabled != enabled)
            {
                meshDepthEnabled = enabled;
                foreach (var pair in nativeMeshes)
                    pair.Value.Renderer.enabled =
                        enabled && !depthSuppressed.Contains(pair.Key);
            }
            // 快照网格每帧校验：错位/包住观察者的网格不写深度。
            UpdateSnapshotDepth();
        }

        // 每 2 秒一条的网格遮挡链路状态：回答「到底有多少块在写深度、其中多少块
        // 比角色更近、门控此刻是否把环境合成关掉、环境合成多久没跑了」。
        private void ReportDepthState()
        {
            if (Time.unscaledTime < nextDepthStateLog) return;
            nextDepthStateLog = Time.unscaledTime + 2f;
            var camera = ViewCamera;
            var enabled = 0;
            var inFront = 0;
            var targetDepth = 0f;
            Bounds target = default;
            var hasTarget = camera != null && TryGetCoverageTargetBounds(out target);
            if (hasTarget) targetDepth = camera.WorldToViewportPoint(target.center).z;
            foreach (var pair in nativeMeshes)
            {
                var renderer = pair.Value.Renderer;
                if (renderer == null) continue;
                if (renderer.enabled) enabled++;
                if (hasTarget &&
                    camera.WorldToViewportPoint(renderer.bounds.center).z <=
                    targetDepth + CoverageDepthTolerance)
                    inFront++;
            }
            var snapshotEnabled = 0;
            foreach (var entry in snapshotMeshes)
                if (entry.Renderer != null && entry.Renderer.enabled) snapshotEnabled++;
            Debug.Log($"MATESOUL_OCCLUSION_MESH depth={(meshDepthEnabled ? 1 : 0)} " +
                      $"allowed={(meshOcclusionAllowed ? 1 : 0)} " +
                      $"material={(depthMaterial != null ? 1 : 0)} " +
                      $"nativeMeshes={nativeMeshes.Count} renderersEnabled={enabled} " +
                      $"inFrontOfTarget={inFront} targetDepth={targetDepth:F2} " +
                      $"snapshot={snapshotMeshes.Count} snapshotEnabled={snapshotEnabled} " +
                      $"rejectedSinceLog={rejectedSinceLastLog} " +
                      $"gate={(coverageGateAllowed ? 1 : 0)} gated={CurrentCoverageGated} " +
                      $"flips={coverageFlips} " +
                      $"envPassAge={RealityEnvironmentOcclusionFeature.CompositeAgeFrames} " +
                      $"camera={ViewCameraDescription()}");
        }

        // 覆盖度门控的唯一入口：渲染特性据此决定是否跳过整趟环境深度合成。
        // 返回 true 表示角色区域已被实时网格接管，环境合成不再必要。
        public static bool EnvironmentCoverageSufficient(Camera camera)
        {
            if (!coverageGateAllowed) return false;
            var instance = activeInstance;
            return instance != null && instance.EvaluateEnvironmentCoverage(camera);
        }

        private void InvalidateCoverage()
        {
            coverageFrame = -1;
            coverageRendererSource = null;
            coverageRenderers.Clear();
            coverageResult = false;
            coverageReason = "none";
        }

        private bool EvaluateEnvironmentCoverage(Camera camera)
        {
            // 网格深度关闭时网格不写深度，跳过环境合成会让角色穿透真实墙体。
            if (camera == null || !meshDepthEnabled || nativeMeshes.Count == 0)
            {
                // 门控被绕过时必须清掉上一帧的结论，否则诊断日志会继续显示
                // gate=1，与「环境合成正在执行」互相矛盾。
                coverageResult = false;
                coverageRatio = 0f;
                coverageMeshCount = 0;
                coverageContributors = 0;
                coverageNearestCount = 0;
                coverageReason = camera == null
                    ? "no-camera"
                    : (!meshDepthEnabled ? "mesh-off" : "no-native");
                return false;
            }
            if (coverageFrame == Time.frameCount) return coverageResult;
            coverageFrame = Time.frameCount;
            coverageEvaluations++;
            coverageMeshCount = 0;
            coverageRatio = 0f;
            coverageContributors = 0;
            coverageNearestCount = 0;

            if (!TryGetCoverageTargetBounds(out var target) ||
                !TryProjectBounds(camera, target, out var targetRect, out var targetDepth))
            {
                coverageResult = false;
                ReportCoverageDecision("no-target");
                return false;
            }
            if (targetRect.width < .02f || targetRect.height < .02f)
            {
                coverageResult = false;
                ReportCoverageDecision("target-small");
                return false;
            }
            coverageTargetRect = targetRect;
            coverageTargetDepth = targetDepth;

            coverageRects.Clear();
            foreach (var pair in nativeMeshes)
            {
                if (!TryProjectBounds(camera, pair.Value.Renderer.bounds,
                        out var rect, out var depth))
                    continue;
                // 只有比角色更近的网格才可能替代环境深度去遮挡角色；角色背后的
                // 墙虽然屏幕投影重叠，但挡不住角色，不能算作覆盖。
                if (depth > targetDepth + CoverageDepthTolerance) continue;
                coverageRects.Add(rect);
                coverageMeshCount++;
                coverageContributors++;
                TrackCoverageNearest(pair.Key, depth, targetDepth, rect, targetRect);
            }

            var hits = 0;
            var samples = CoverageSampleSteps * CoverageSampleSteps;
            for (var row = 0; row < CoverageSampleSteps; row++)
            {
                var y = Mathf.Lerp(targetRect.yMin, targetRect.yMax,
                    (row + .5f) / CoverageSampleSteps);
                for (var column = 0; column < CoverageSampleSteps; column++)
                {
                    var x = Mathf.Lerp(targetRect.xMin, targetRect.xMax,
                        (column + .5f) / CoverageSampleSteps);
                    var point = new Vector2(x, y);
                    for (var index = 0; index < coverageRects.Count; index++)
                    {
                        if (!coverageRects[index].Contains(point)) continue;
                        hits++;
                        break;
                    }
                }
            }

            coverageRatio = hits / (float)samples;
            coverageResult = coverageRatio >= CoverageSampleThreshold;
            ReportCoverageDecision(coverageResult ? "covered" : "sparse");
            return coverageResult;
        }

        private bool TryGetCoverageTargetBounds(out Bounds bounds)
        {
            bounds = default;
            var target = coverageTarget;
            if (target == null) return false;
            if (coverageRendererSource != target)
            {
                coverageRenderers.Clear();
                coverageRenderers.AddRange(target.GetComponentsInChildren<Renderer>(true));
                coverageRendererSource = target;
            }
            var found = false;
            foreach (var renderer in coverageRenderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return found && bounds.size.sqrMagnitude > 0f;
        }

        private static bool TryProjectBounds(Camera camera, Bounds bounds,
            out Rect rect, out float depth)
        {
            rect = default;
            depth = camera.WorldToViewportPoint(bounds.center).z;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3((corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                var point = camera.WorldToViewportPoint(
                    bounds.center + Vector3.Scale(bounds.extents, offset));
                // 角点落在相机后方时投影会翻转，宁可不把这个网格算进覆盖。
                if (point.z <= .05f) return false;
                min = Vector2.Min(min, new Vector2(point.x, point.y));
                max = Vector2.Max(max, new Vector2(point.x, point.y));
            }
            rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return rect.width > 0f && rect.height > 0f;
        }

        // 拒绝块与角色屏幕投影的重叠比：-1 表示任一方投影无效（例如块把
        // 观察者包住时角点在相机后方），这时该块通常正是“贴身墙”。
        private float CoverageOverlap(Bounds bounds, Camera camera)
        {
            if (camera == null || !TryGetCoverageTargetBounds(out var target)) return -1f;
            if (!TryProjectBounds(camera, target, out var targetRect, out _)) return -1f;
            if (!TryProjectBounds(camera, bounds, out var rect, out _)) return -1f;
            var area = targetRect.width * targetRect.height;
            if (area <= 0f) return -1f;
            var width = Mathf.Max(0f, Mathf.Min(rect.xMax, targetRect.xMax) -
                                      Mathf.Max(rect.xMin, targetRect.xMin));
            var height = Mathf.Max(0f, Mathf.Min(rect.yMax, targetRect.yMax) -
                                       Mathf.Max(rect.yMin, targetRect.yMin));
            return width * height / area;
        }

        // 保留最近的 4 个候选块：门控误判几乎都来自「包围盒很大、中心很近，
        // 但真实表面并不在角色前面」的块，日志里必须能看到它们的 id 与深度差。
        private void TrackCoverageNearest(Guid id, float depth, float targetDepth,
            Rect rect, Rect targetRect)
        {
            var delta = depth - targetDepth;
            var hit = rect.Contains(targetRect.center);
            var index = coverageNearestCount;
            if (index < coverageNearestIds.Length)
            {
                coverageNearestCount++;
            }
            else
            {
                index = 0;
                for (var i = 1; i < coverageNearestCount; i++)
                    if (coverageNearestDepths[i] > coverageNearestDepths[index]) index = i;
                if (delta >= coverageNearestDepths[index]) return;
            }
            coverageNearestIds[index] = id;
            coverageNearestDepths[index] = delta;
            coverageNearestHits[index] = hit;
        }

        private string DescribeCoverageNearest()
        {
            if (coverageNearestCount == 0) return "none";
            var text = string.Empty;
            for (var i = 0; i < coverageNearestCount; i++)
            {
                if (i > 0) text += ",";
                text += $"{coverageNearestIds[i].ToString("N").Substring(0, 8)}:" +
                        $"d={coverageNearestDepths[i]:F2}:" +
                        $"c={(coverageNearestHits[i] ? 1 : 0)}";
            }
            return text;
        }

        private void ReportCoverageDecision(string reason)
        {
            var resultChanged = coverageEvaluations > 1 && coverageResult != lastCoverageResult;
            if (resultChanged) coverageFlips++;
            var changed = reason != coverageReason || resultChanged;
            if (!changed && Time.unscaledTime < nextCoverageLog) return;
            nextCoverageLog = Time.unscaledTime + CoverageLogInterval;
            coverageReason = reason;
            lastCoverageResult = coverageResult;
            Debug.Log($"MATESOUL_OCCLUSION_COVERAGE gated={(coverageResult ? 1 : 0)} " +
                      $"reason={reason} ratio={coverageRatio:F2} " +
                      $"meshes={coverageMeshCount} " +
                      $"targetDepth={coverageTargetDepth:F2} " +
                      $"targetRect=({coverageTargetRect.xMin:F2}," +
                      $"{coverageTargetRect.yMin:F2},{coverageTargetRect.xMax:F2}," +
                      $"{coverageTargetRect.yMax:F2}) " +
                      $"nearest=[{DescribeCoverageNearest()}] " +
                      $"flips={coverageFlips} evals={coverageEvaluations} " +
                      $"envPassAge={RealityEnvironmentOcclusionFeature.CompositeAgeFrames} " +
                      $"compositeFrames={RealityEnvironmentOcclusionFeature.CompositeFrames} " +
                      $"meshDepth={(meshDepthEnabled ? 1 : 0)} " +
                      $"nativeMeshes={nativeMeshes.Count} " +
                      $"camera={ViewCameraDescription()}");
        }

        private void UpdateSnapshotDepth()
        {
            var camera = viewCamera != null ? viewCamera : viewCamera = Camera.main;
            var usable = meshOcclusionAllowed && nativeMeshes.Count == 0 &&
                         IsSnapshotAligned(camera);
            foreach (var entry in snapshotMeshes)
                entry.Renderer.enabled = usable &&
                                         IsSnapshotMeshReliable(entry.Renderer.bounds, camera);
        }

        private bool IsSnapshotAligned(Camera camera)
        {
            if (snapshot == null) return false;
            if (camera == null) return true;
            var position = camera.transform.position;
            // 1) 观察者必须仍在快照房间的水平范围内，否则快照来自别的房间/旧空间。
            var room = snapshot.Bounds;
            if (room.size.x > 0 && room.size.z > 0)
            {
                var margin = SnapshotRoomMargin;
                if (position.x < room.min.x - margin || position.x > room.max.x + margin ||
                    position.z < room.min.z - margin || position.z > room.max.z + margin)
                    return false;
            }
            // 2) 地面高度必须与当前跟踪空间一致（重新标定后两者的差值会明显变大）。
            var floor = snapshot.MainFloor != null ? snapshot.MainFloor.Centroid.y : 0f;
            var eyeAboveFloor = position.y - floor;
            return eyeAboveFloor >= 1.6f - SnapshotFloorTolerance &&
                   eyeAboveFloor <= 1.6f + SnapshotFloorTolerance + .4f;
        }

        private static bool IsSnapshotMeshReliable(Bounds bounds, Camera camera)
        {
            var size = bounds.size;
            if (size.x > SnapshotMaximumExtent || size.y > SnapshotMaximumExtent ||
                size.z > SnapshotMaximumExtent)
                return false;
            if (camera == null) return true;
            var position = camera.transform.position;
            var enclosing = bounds;
            enclosing.Expand(SnapshotViewerMargin * 2f);
            if (enclosing.Contains(position)) return false;
            return bounds.SqrDistance(position) >=
                   SnapshotMinimumViewerDistance * SnapshotMinimumViewerDistance;
        }

        private static Bounds MeasureBlock(PxrSpatialMeshInfo block)
        {
            var measured = new Bounds(block.position + block.rotation * block.vertices[0],
                Vector3.zero);
            foreach (var vertex in block.vertices)
                measured.Encapsulate(block.position + block.rotation * vertex);
            return measured;
        }

        // Returns the rejection name, or null when the block may write depth.
        // Viewer-proximity checks are transient (walking up to a wall), so the
        // periodic prune skips them and only drops geometry that cannot belong to
        // this room at all.
        private string NativeMeshRejection(Bounds bounds, bool checkViewer = true)
        {
            var size = bounds.size;
            if (size.x > NativeMaximumExtent || size.y > NativeMaximumExtent ||
                size.z > NativeMaximumExtent)
                return "oversized";

            var camera = ViewCamera;
            var floorY = snapshot?.MainFloor != null ? snapshot.MainFloor.Centroid.y : 0f;
            // A capture whose floor disagrees with the viewer's eye height is not a
            // trusted reference; vetoing blocks against it would delete real geometry.
            var floorTrusted = camera == null || snapshot?.MainFloor == null ||
                               (camera.transform.position.y - floorY >= 1f &&
                                camera.transform.position.y - floorY <= 2.4f);
            if (floorTrusted && snapshot?.MainFloor != null &&
                bounds.max.y < floorY - NativeFloorTolerance)
                return "below-floor";

            if (camera == null) return null;
            var viewer = camera.transform.position;
            if (checkViewer)
            {
                var enclosing = bounds;
                enclosing.Expand(NativeViewerMargin);
                if (enclosing.Contains(viewer)) return "encloses-viewer";
                if (bounds.SqrDistance(viewer) <
                    NativeMinimumViewerDistance * NativeMinimumViewerDistance)
                    return "against-viewer";
            }
            // Only veto blocks when the observer is inside the calibrated room: that
            // is the case where the room bounds are authoritative for this session.
            var room = snapshot != null ? snapshot.Bounds : default(Bounds);
            if (room.size.x > 0 && room.size.z > 0 &&
                ContainsXZ(room, viewer, NativeRoomMargin))
            {
                room.Expand(NativeRoomMargin);
                if (!room.Intersects(bounds)) return "outside-room";
            }
            return null;
        }

        private static bool ContainsXZ(Bounds bounds, Vector3 point, float margin)
        {
            return point.x >= bounds.min.x - margin && point.x <= bounds.max.x + margin &&
                   point.z >= bounds.min.z - margin && point.z <= bounds.max.z + margin;
        }

        private Camera ViewCamera =>
            viewCamera != null ? viewCamera : viewCamera = Camera.main;

        private void RecordRejectedBlock(PxrSpatialMeshInfo block, Bounds bounds,
            string reason)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            switch (reason)
            {
                case "encloses-viewer": rejectedEnclosingViewer++; break;
                case "against-viewer": rejectedAgainstViewer++; break;
                case "below-floor": rejectedBelowFloor++; break;
                case "outside-room": rejectedOutsideRoom++; break;
                case "oversized": rejectedOversized++; break;
                default: rejectedInvalidBlock++; break;
            }
#endif
            rejectedSinceLastLog++;
            if (Time.unscaledTime < nextRejectionLog) return;
            nextRejectionLog = Time.unscaledTime + 1f;
            var camera = ViewCamera;
            var distance = camera != null
                ? Mathf.Sqrt(bounds.SqrDistance(camera.transform.position))
                : -1f;
            var floor = snapshot?.MainFloor != null ? snapshot.MainFloor.Centroid.y : 0f;
            // 被拒绝的块是否原本正在写深度、是否压在角色屏幕区域上：如果
            // 两者都成立，这次拒绝就直接拿掉了唯一真正的遮挡物。
            var wasWriting = nativeMeshes.TryGetValue(block.uuid, out var installed) &&
                             installed.Renderer != null && installed.Renderer.enabled;
            var avatarOverlap = CoverageOverlap(bounds, camera);
            Debug.Log($"MATESOUL_GEOMETRY_REJECT uuid={block.uuid:N} " +
                      $"state={block.state} reason={reason} " +
                      $"rejectedSinceLastLog={rejectedSinceLastLog} " +
                      $"wasWriting={(wasWriting ? 1 : 0)} " +
                      $"avatarOverlap={avatarOverlap:F2} " +
                      $"verts={block.vertices?.Length ?? 0} " +
                      $"tris={(block.indices?.Length ?? 0) / 3} " +
                      $"center={bounds.center} size={bounds.size} " +
                      $"minY={bounds.min.y:F2} maxY={bounds.max.y:F2} floorY={floor:F2} " +
                      $"cameraDistance={distance:F2} camera={ViewCameraDescription()} " +
                      $"room={RoomDescription()}");
            rejectedSinceLastLog = 0;
        }

        private string RoomDescription()
        {
            if (snapshot == null) return "none";
            var room = snapshot.Bounds;
            return $"source={snapshot.Source} id={snapshot.Id} " +
                   $"center={room.center} size={room.size}";
        }

        // Installed entries were validated once, at install time. A tracking-origin
        // recenter or a room map arriving late can invalidate them without any new
        // SDK event, leaving depth-only planes inside the room. Re-check them, but
        // only for room-level impossibilities so walking near a wall cannot delete
        // a legitimate occluder.
        private void PruneNativeMeshes()
        {
            if (nativeMeshes.Count == 0) return;
            if (Time.unscaledTime < nextNativePrune) return;
            nextNativePrune = Time.unscaledTime + 1f;
            // 观察者走动或重新定位后，「贴近观察者」这个前提会失效，之前停写
            // 深度的块要恢复，否则真实墙体永远不再遮挡角色。
            var restored = ReleaseRestoredDepth();
            rejectedByRoom.Clear();
            foreach (var pair in nativeMeshes)
                if (NativeMeshRejection(pair.Value.Renderer.bounds, false) != null)
                    rejectedByRoom.Add(pair.Key);
            if (rejectedByRoom.Count == 0)
            {
                if (restored) UpdateDepthRendering(true);
                return;
            }
            Debug.Log($"MATESOUL_GEOMETRY_PRUNE count={rejectedByRoom.Count} " +
                      $"ids=[{string.Join(",", rejectedByRoom.Select(id => id.ToString("N")))}] " +
                      $"remaining={nativeMeshes.Count - rejectedByRoom.Count} " +
                      $"camera={ViewCameraDescription()} room={RoomDescription()}");
            foreach (var id in rejectedByRoom) RemoveNativeMesh(id);
            rejectedByRoom.Clear();
            UpdateDepthRendering(true);
        }

        // 若块已不再包住/贴着观察者，恢复它的深度写入。
        private bool ReleaseRestoredDepth()
        {
            if (depthSuppressed.Count == 0) return false;
            var restored = false;
            foreach (var id in depthSuppressed.ToArray())
            {
                if (!nativeMeshes.TryGetValue(id, out var entry))
                {
                    depthSuppressed.Remove(id);
                    continue;
                }
                if (NativeMeshRejection(entry.Renderer.bounds) != null) continue;
                depthSuppressed.Remove(id);
                restored = true;
            }
            return restored;
        }

        private string ViewCameraDescription()
        {
            var camera = ViewCamera;
            if (camera == null) return "none";
            var position = camera.transform.position;
            return $"{position.x:F2},{position.y:F2},{position.z:F2}";
        }

        private void UpdateNavigationSurface(Guid id, MeshEntry entry)
        {
            if (roomNavigation == null) return;
            roomNavigation.UpdateLiveSurface(id.ToString("N"), entry.Mesh,
                entry.GameObject.transform.localToWorldMatrix);
        }

        private MeshEntry CreateEntry(
            string objectName,
            Mesh mesh,
            Vector3 position,
            Quaternion rotation,
            bool physicalSurface,
            int sourceId)
        {
            var meshObject = new GameObject(objectName);
            if (physicalSurface)
                meshObject.layer = InteractionPhysicsLayers.RealitySurface;
            meshObject.transform.SetParent(transform, false);
            meshObject.transform.SetPositionAndRotation(position, rotation);
            meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = meshObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = depthMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.enabled = meshDepthEnabled;
            if (physicalSurface)
            {
                // Share the measured mesh between rendering and static physics;
                // updates/removals own both resources together.
                var collider = meshObject.AddComponent<MeshCollider>();
                collider.cookingOptions =
                    MeshColliderCookingOptions.CookForFasterSimulation |
                    MeshColliderCookingOptions.EnableMeshCleaning |
                    MeshColliderCookingOptions.WeldColocatedVertices |
                    MeshColliderCookingOptions.UseFastMidphase;
                collider.sharedMesh = mesh;
                meshObject.AddComponent<RoomSurfaceCollider>().Bind(sourceId);
            }
            return new MeshEntry(meshObject, mesh, renderer, sourceId);
        }

        private void RemoveNativeMesh(Guid id)
        {
            if (!nativeMeshes.TryGetValue(id, out var entry)) return;
            DestroyEntry(entry);
            nativeMeshes.Remove(id);
            installLogs.Remove(id);
            depthSuppressed.Remove(id);
            roomNavigation?.RemoveLiveSurface(id.ToString("N"));
        }

        private static bool TryCreateMesh(
            string meshName,
            IReadOnlyList<Vector3> vertices,
            IReadOnlyList<int> indices,
            out Mesh mesh)
        {
            mesh = null;
            if (vertices == null || indices == null ||
                vertices.Count < 3 || indices.Count < 3 ||
                indices.Count % 3 != 0)
                return false;

            var vertexArray = new Vector3[vertices.Count];
            for (var index = 0; index < vertexArray.Length; index++)
            {
                if (!PhysicsBody.IsFinite(vertices[index])) return false;
                vertexArray[index] = vertices[index];
            }

            var indexArray = new int[indices.Count];
            for (var index = 0; index < indexArray.Length; index++)
            {
                if (indices[index] < 0 || indices[index] >= vertexArray.Length)
                    return false;
                indexArray[index] = indices[index];
            }

            mesh = new Mesh
            {
                name = meshName,
                indexFormat = vertexArray.Length > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16,
                vertices = vertexArray,
                triangles = indexArray
            };
            mesh.RecalculateBounds();
            return true;
        }

        private static bool IsValidPose(Vector3 position, Quaternion rotation)
        {
            var norm = rotation.x * rotation.x + rotation.y * rotation.y +
                       rotation.z * rotation.z + rotation.w * rotation.w;
            return PhysicsBody.IsFinite(position) && PhysicsBody.IsFinite(norm) && norm > .001f;
        }

        private void StartMeshSubsystems()
        {
            if (Application.isEditor) return;
            InteractionPhysicsLayers.Configure();
            var subsystems = new List<XRMeshSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            foreach (var subsystem in subsystems)
            {
                if (subsystem == null || subsystem.running) continue;
                subsystem.Start();
                startedSubsystems.Add(subsystem);
            }
        }

        private static void ClearEntries(IEnumerable<MeshEntry> entries)
        {
            foreach (var entry in entries) DestroyEntry(entry);
        }

        private static void DestroyEntry(MeshEntry entry)
        {
            entry.GameObject.SetActive(false);
            DestroyOwned(entry.GameObject);
            DestroyOwned(entry.Mesh);
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        private void OnDestroy()
        {
            if (activeInstance == this) activeInstance = null;
            ClearEntries(nativeMeshes.Values);
            nativeMeshes.Clear();
            depthSuppressed.Clear();
            retention.Clear();
            roomNavigation?.ClearLiveSurfaces();
            ClearEntries(snapshotMeshes);
            snapshotMeshes.Clear();
            DestroyOwned(depthMaterial);
        }

        private readonly struct MeshEntry
        {
            public MeshEntry(GameObject gameObject, Mesh mesh, MeshRenderer renderer, int sourceId)
            {
                GameObject = gameObject;
                Mesh = mesh;
                Renderer = renderer;
                SourceId = sourceId;
            }

            public GameObject GameObject { get; }
            public Mesh Mesh { get; }
            public MeshRenderer Renderer { get; }
            public int SourceId { get; }
        }
    }
}
