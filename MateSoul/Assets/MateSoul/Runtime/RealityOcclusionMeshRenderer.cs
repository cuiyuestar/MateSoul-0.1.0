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
        private readonly List<MeshEntry> snapshotMeshes = new List<MeshEntry>();
        private Material depthMaterial;
        private Mesh unitCube;
        private RoomMapSnapshot snapshot;
        private static bool? environmentDepthAvailable;

        public int NativeMeshCount => nativeMeshes.Count;
        public int SnapshotMeshCount => snapshotMeshes.Count;
        public Material DepthMaterial => depthMaterial;
        public static bool EnvironmentDepthAvailable
        {
            get
            {
                if (!environmentDepthAvailable.HasValue)
                    environmentDepthAvailable = !Application.isEditor && !IsPicoEmulator();
                return environmentDepthAvailable.Value;
            }
        }

        public static RealityOcclusionMeshRenderer Install(Camera xrCamera)
        {
            if (Application.isEditor || xrCamera == null) return null;

            var origin = xrCamera.GetComponentInParent<XROrigin>();
            var host = origin != null ? origin.gameObject : xrCamera.transform.root.gameObject;
            if (EnvironmentDepthAvailable &&
                host.GetComponent<PXR_EnvironmentDepthManager>() == null)
                host.AddComponent<PXR_EnvironmentDepthManager>();
            return host.GetComponent<RealityOcclusionMeshRenderer>() ??
                   host.AddComponent<RealityOcclusionMeshRenderer>();
        }

        private static bool IsPicoEmulator()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var build = new AndroidJavaClass("android.os.Build");
                if (ContainsEmulatorMarker(build.GetStatic<string>("FINGERPRINT")) ||
                    ContainsEmulatorMarker(build.GetStatic<string>("PRODUCT")) ||
                    ContainsEmulatorMarker(build.GetStatic<string>("MODEL")))
                    return true;
            }
            catch (Exception error)
            {
                Debug.LogWarning(
                    $"Could not read Android build identity; using Unity device identity: {error.Message}");
            }
#endif
            return ContainsEmulatorMarker(SystemInfo.deviceModel) ||
                   ContainsEmulatorMarker(SystemInfo.deviceName);
        }

        private static bool ContainsEmulatorMarker(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf("emulator", StringComparison.OrdinalIgnoreCase) >= 0;
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
            RefreshCachedNativeMeshes();
            if (snapshot != null) BuildSnapshotMeshes(snapshot);
        }

        private void OnDisable()
        {
            PXR_Manager.SpatialMeshDataUpdated -= OnSpatialMeshDataUpdated;
            ClearEntries(nativeMeshes.Values);
            nativeMeshes.Clear();
            ClearEntries(snapshotMeshes);
            snapshotMeshes.Clear();
        }

        public void ApplySnapshot(RoomMapSnapshot snapshot)
        {
            this.snapshot = snapshot;
            BuildSnapshotMeshes(snapshot);
        }

        private void BuildSnapshotMeshes(RoomMapSnapshot room)
        {
            ClearEntries(snapshotMeshes);
            snapshotMeshes.Clear();
            if (room == null) return;

            if (room.SurfaceMeshes.Count > 0)
            {
                foreach (var surface in room.SurfaceMeshes)
                {
                    if (!TryCreateMesh(
                            $"Reality Surface {surface.Id}",
                            surface.Vertices,
                            surface.Indices,
                            out var mesh))
                        continue;
                    snapshotMeshes.Add(CreateEntry(mesh.name, mesh, Vector3.zero,
                        Quaternion.identity, Vector3.one, true));
                }
                return;
            }

            foreach (var floor in room.Floors)
            {
                if (floor.Vertices.Count < 3) continue;
                var mesh = RoomGeometry.CreateHorizontalPolygonMesh(
                    floor.Vertices,
                    $"Reality Floor {floor.Id}");
                snapshotMeshes.Add(CreateEntry(mesh.name, mesh, Vector3.zero,
                    Quaternion.identity, Vector3.one, true));
            }

            unitCube ??= CreateUnitCubeMesh();
            foreach (var obstacle in room.Obstacles)
                snapshotMeshes.Add(CreateEntry(
                    $"Reality Obstacle {obstacle.Id}",
                    unitCube,
                    obstacle.Center,
                    obstacle.Rotation,
                    obstacle.Size,
                    false));
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
            {
                if (block.state == MeshChangeState.Removed)
                {
                    RemoveNativeMesh(block.uuid);
                    continue;
                }

                if (block.vertices == null || block.indices == null ||
                    !TryCreateMesh(
                        $"Reality Native Mesh {block.uuid:N}",
                        block.vertices,
                        Array.ConvertAll(block.indices, value => (int)value),
                        out var mesh))
                    continue;

                RemoveNativeMesh(block.uuid);
                nativeMeshes.Add(block.uuid, CreateEntry(
                    mesh.name,
                    mesh,
                    block.position,
                    block.rotation,
                    Vector3.one,
                    true));
            }
        }

        private MeshEntry CreateEntry(
            string objectName,
            Mesh mesh,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            bool ownsMesh)
        {
            var meshObject = new GameObject(objectName);
            meshObject.transform.SetParent(transform, false);
            meshObject.transform.SetPositionAndRotation(position, rotation);
            meshObject.transform.localScale = scale;
            meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = meshObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = depthMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
            return new MeshEntry(meshObject, mesh, ownsMesh);
        }

        private void RemoveNativeMesh(Guid id)
        {
            if (!nativeMeshes.TryGetValue(id, out var entry)) return;
            DestroyEntry(entry);
            nativeMeshes.Remove(id);
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
                vertexArray[index] = vertices[index];

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

        private static Mesh CreateUnitCubeMesh()
        {
            var vertices = new[]
            {
                new Vector3(-.5f, -.5f, -.5f), new Vector3(.5f, -.5f, -.5f),
                new Vector3(.5f, .5f, -.5f), new Vector3(-.5f, .5f, -.5f),
                new Vector3(-.5f, -.5f, .5f), new Vector3(.5f, -.5f, .5f),
                new Vector3(.5f, .5f, .5f), new Vector3(-.5f, .5f, .5f)
            };
            var triangles = new[]
            {
                0, 2, 1, 0, 3, 2, 1, 2, 6, 1, 6, 5,
                5, 6, 7, 5, 7, 4, 4, 7, 3, 4, 3, 0,
                3, 7, 6, 3, 6, 2, 4, 0, 1, 4, 1, 5
            };
            TryCreateMesh("Reality Occlusion Unit Cube", vertices, triangles, out var mesh);
            return mesh;
        }

        private static void ClearEntries(IEnumerable<MeshEntry> entries)
        {
            foreach (var entry in entries) DestroyEntry(entry);
        }

        private static void DestroyEntry(MeshEntry entry)
        {
            DestroyOwned(entry.GameObject);
            if (entry.OwnsMesh) DestroyOwned(entry.Mesh);
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        private void OnDestroy()
        {
            ClearEntries(snapshotMeshes);
            snapshotMeshes.Clear();
            DestroyOwned(unitCube);
            DestroyOwned(depthMaterial);
        }

        private readonly struct MeshEntry
        {
            public MeshEntry(GameObject gameObject, Mesh mesh, bool ownsMesh)
            {
                GameObject = gameObject;
                Mesh = mesh;
                OwnsMesh = ownsMesh;
            }

            public GameObject GameObject { get; }
            public Mesh Mesh { get; }
            public bool OwnsMesh { get; }
        }
    }
}
