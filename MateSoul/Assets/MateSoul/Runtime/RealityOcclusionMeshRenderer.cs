using System;
using System.Collections.Generic;
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

        private readonly Dictionary<Guid, MeshEntry> nativeMeshes =
            new Dictionary<Guid, MeshEntry>();
        private readonly Dictionary<Guid, PxrSpatialMeshInfo> pendingMeshes =
            new Dictionary<Guid, PxrSpatialMeshInfo>();
        private readonly List<MeshEntry> snapshotMeshes = new List<MeshEntry>();
        private readonly MeasuredMeshRetention retention = new MeasuredMeshRetention();
        private readonly List<XRMeshSubsystem> startedSubsystems =
            new List<XRMeshSubsystem>();
        private Material depthMaterial;
        private RoomMapSnapshot snapshot;
        private RuntimeRoomNavigation roomNavigation;
        private bool meshDepthEnabled;
#region debug-point A-D:live-room-state
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private float debugNextRoomReport;
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
                retention.Clear();
                pendingMeshes.Clear();
                roomNavigation?.ClearLiveSurfaces();
            }
            this.snapshot = snapshot;
            roomNavigation = navigation;
            BuildSnapshotMeshes(snapshot);
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
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var changed = false;
            // Coalesce each UUID and spend at most 2ms between complete mesh cooks.
            // One block may exceed that budget; it is never partially installed.
            while (pendingMeshes.Count > 0 && (!changed || watch.Elapsed.TotalMilliseconds < 2))
            {
                var block = pendingMeshes.First().Value;
                pendingMeshes.Remove(block.uuid);
                if (block.state == MeshChangeState.Removed)
                {
                    retention.MarkUnobserved(block.uuid);
                    continue;
                }

                // The SDK sends the complete cache, including Unchanged blocks.
                // Do not rebuild and re-cook their physics meshes on every update.
                if (block.state == MeshChangeState.Unchanged &&
                    nativeMeshes.ContainsKey(block.uuid))
                    continue;
                if (block.vertices == null || block.indices == null ||
                    !IsValidPose(block.position, block.rotation) ||
                    !TryCreateMesh(
                        $"Reality Native Mesh {block.uuid:N}",
                        block.vertices,
                        Array.ConvertAll(block.indices, value => (int)value),
                        out var mesh))
                    continue;

                // Cook and register the replacement before retiring old collision.
                var replacement = CreateEntry(
                    mesh.name,
                    mesh,
                    block.position,
                    block.rotation,
                    true,
                    block.uuid.GetHashCode());
                foreach (var obsolete in retention.Observe(block)) RemoveNativeMesh(obsolete);
                if (nativeMeshes.TryGetValue(block.uuid, out var previous))
                    DestroyEntry(previous);
                nativeMeshes[block.uuid] = replacement;
                UpdateNavigationSurface(block.uuid, replacement);
                changed = true;
            }
            if (changed)
            {
                UpdateDepthRendering(true);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.Log($"[RealityGeometry] liveMeshColliders={nativeMeshes.Count} " +
                          $"pendingMeshBlocks={pendingMeshes.Count} " +
                          $"snapshotDepthMeshes={snapshotMeshes.Count} " +
                          $"meshDepthEnabled={meshDepthEnabled} semanticDepthBoxes=0");
#endif
            }
        }

        private void LateUpdate()
        {
            ApplyPendingMeshes();
            UpdateDepthRendering();
#region debug-point A-D:live-room-state
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Time.unscaledTime >= debugNextRoomReport)
            {
                debugNextRoomReport = Time.unscaledTime + 2f;
                var manager = GetComponent<PXR_EnvironmentDepthManager>();
                var raw = Shader.GetGlobalTexture("_EnvironmentDepthTexture");
                var preprocessed = Shader.GetGlobalTexture("_PreprocessedEnvironmentDepthTexture");
                Debug.Log("MATESOUL_GEOMETRY_TRACE " +
                    $"{{\"sessionId\":\"live-room-geometry\",\"runId\":\"post-fix\"," +
                    $"\"hypothesisId\":\"A-D\",\"location\":\"RealityOcclusionMeshRenderer.LateUpdate\"," +
                    $"\"msg\":\"[DEBUG] live geometry state\",\"data\":{{" +
                    $"\"nativeMeshes\":{nativeMeshes.Count},\"snapshotMeshes\":{snapshotMeshes.Count}," +
                    $"\"retainedMeshes\":{retention.RetainedCount}," +
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
            var enabled = nativeMeshes.Count > 0;
            if (!force && meshDepthEnabled == enabled) return;
            meshDepthEnabled = enabled;
            foreach (var entry in nativeMeshes.Values)
                entry.Renderer.enabled = enabled;
            foreach (var entry in snapshotMeshes)
                entry.Renderer.enabled = nativeMeshes.Count == 0;
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
            ClearEntries(nativeMeshes.Values);
            nativeMeshes.Clear();
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
