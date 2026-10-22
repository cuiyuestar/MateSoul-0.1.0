using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.AI;
using Debug = UnityEngine.Debug;

namespace MateSoul.RoomNavigation
{
    [DisallowMultipleComponent]
    public sealed class RoomSurfaceCollider : MonoBehaviour
    {
        public int SourceId { get; private set; } = -1;
        public void Bind(int sourceId) => SourceId = sourceId;
    }

    [DisallowMultipleComponent]
    public sealed class RuntimeRoomNavigation : MonoBehaviour
    {
        public const float CharacterRadius = 0.25f;
        public const float CharacterHeight = 1.65f;

        [SerializeField, Min(0f)]
        private float obstacleSafetyMargin = 0.08f;

        [SerializeField, Min(0.1f)]
        private float sampleDistance = 0.75f;

        private readonly List<UnityEngine.Object> ownedObjects = new List<UnityEngine.Object>();
        private NavMeshDataInstance navMeshInstance;
        private GameObject geometryRoot;

        public RoomMapSnapshot Snapshot { get; private set; }
        public bool IsReady => navMeshInstance.valid && Snapshot != null;
        public long LastBuildMilliseconds { get; private set; }

        public void Build(RoomMapSnapshot snapshot, bool showDebugGeometry)
        {
            Clear();

            var validation = snapshot?.Validate() ??
                             RoomMapValidationResult.Invalid("Room map snapshot is null.");
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(validation.Error);
            }

            if (NavMesh.GetSettingsCount() == 0)
            {
                throw new InvalidOperationException("No NavMesh agent settings are configured.");
            }

            var stopwatch = Stopwatch.StartNew();
            Snapshot = snapshot;
            geometryRoot = new GameObject("GeneratedRoomGeometry");
            geometryRoot.transform.SetParent(transform, false);

            var floorSources = new List<NavMeshBuildSource>();
            var obstacleSources = new List<NavMeshBuildSource>();
            var surfaceSources = new List<NavMeshBuildSource>();
            var usesSurfaceMeshes = snapshot.SurfaceMeshes.Count > 0;
            foreach (var floor in snapshot.Floors)
            {
                var floorMesh = RoomGeometry.CreateHorizontalPolygonMesh(
                    floor.Vertices,
                    $"Floor_{floor.Id}");
                ownedObjects.Add(floorMesh);
                floorSources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Mesh,
                    sourceObject = floorMesh,
                    transform = Matrix4x4.identity,
                    area = 0
                });
                CreateFloorGeometry(floor, floorMesh, showDebugGeometry);
            }

            var notWalkableArea = NavMesh.GetAreaFromName("Not Walkable");
            foreach (var obstacle in snapshot.Obstacles)
            {
                if (usesSurfaceMeshes &&
                    !IsReliableSpatialObstacle(
                        obstacle, snapshot.MainFloor.Centroid.y))
                    continue;
                var size = obstacle.Size;
                size.x += obstacleSafetyMargin * 2f;
                size.z += obstacleSafetyMargin * 2f;
                obstacleSources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(obstacle.Center, obstacle.Rotation, Vector3.one),
                    size = size,
                    area = notWalkableArea
                });
                CreateObstacleGeometry(obstacle, showDebugGeometry);
            }

            foreach (var surface in snapshot.SurfaceMeshes)
            {
                var surfaceMesh = CreateSurfaceMesh(surface);
                ownedObjects.Add(surfaceMesh);
                surfaceSources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Mesh,
                    sourceObject = surfaceMesh,
                    transform = Matrix4x4.identity,
                    area = 0
                });
                var colliderObject = new GameObject($"Surface_{surface.Id}");
                colliderObject.transform.SetParent(geometryRoot.transform, false);
                colliderObject.AddComponent<MeshCollider>().sharedMesh = surfaceMesh;
                colliderObject.AddComponent<RoomSurfaceCollider>().Bind(surface.SourceId);
            }

            var settings = NavMesh.GetSettingsByIndex(0);
            settings.agentRadius = CharacterRadius;
            settings.agentHeight = CharacterHeight;
            settings.agentClimb = 0.2f;
            settings.agentSlope = 45f;
            settings.minRegionArea = 0.25f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = 0.08f;
            var bounds = snapshot.Bounds;
            bounds.Expand(new Vector3(2f, 4f, 2f));
            var sources = new List<NavMeshBuildSource>(floorSources);
            sources.AddRange(obstacleSources);

            BuildAndAddNavMesh(settings, sources, bounds);
            stopwatch.Stop();
            LastBuildMilliseconds = stopwatch.ElapsedMilliseconds;

            if (!TrySamplePosition(snapshot.MainFloor.Centroid, bounds.extents.magnitude, out _))
            {
                var triangulation = NavMesh.CalculateTriangulation();
                Debug.LogWarning(
                    $"[RoomNavigation] Full build produced no sample. " +
                    $"vertices={triangulation.vertices.Length} floorSources={floorSources.Count} " +
                    $"obstacleSources={obstacleSources.Count} surfaceSources={surfaceSources.Count} " +
                    $"bounds={bounds} " +
                    $"agentType={settings.agentTypeID} radius={settings.agentRadius:F2} " +
                    $"height={settings.agentHeight:F2} slope={settings.agentSlope:F1}");

                RemoveNavMeshInstance();
                if (usesSurfaceMeshes)
                {
                    BuildAndAddNavMesh(settings, floorSources, bounds);
                    if (TrySamplePosition(
                            snapshot.MainFloor.Centroid,
                            bounds.extents.magnitude, out _))
                    {
                        Debug.LogWarning(
                            "[RoomNavigation] Spatial-mesh obstacle bounds removed the floor; " +
                            "using the floor hull with physical collision avoidance.");
                        stopwatch.Stop();
                        LastBuildMilliseconds = stopwatch.ElapsedMilliseconds;
                        return;
                    }
                    Clear();
                    throw new InvalidOperationException(
                        "The spatial-mesh floor hull did not produce a Runtime NavMesh.");
                }
                BuildAndAddNavMesh(settings, floorSources, bounds);
                if (TrySamplePosition(snapshot.MainFloor.Centroid, bounds.extents.magnitude, out _))
                {
                    Clear();
                    throw new InvalidOperationException(
                        "Room obstacles removed the complete walkable surface.");
                }

                Clear();
                throw new InvalidOperationException(
                    "The floor geometry did not produce a Runtime NavMesh.");
            }
        }

        private static bool IsReliableSpatialObstacle(
            RoomObstacle obstacle, float floorHeight)
        {
            if (obstacle.Kind == RoomObstacleKind.Wall ||
                obstacle.Kind == RoomObstacleKind.VirtualWall ||
                obstacle.Kind == RoomObstacleKind.Lamp ||
                obstacle.Kind == RoomObstacleKind.AirConditioner ||
                obstacle.Kind == RoomObstacleKind.Screen)
                return false;
            var bottom = obstacle.Center.y - obstacle.Size.y * .5f;
            var top = obstacle.Center.y + obstacle.Size.y * .5f;
            return bottom <= floorHeight + .15f &&
                   top >= floorHeight + .25f &&
                   obstacle.Size.x <= 3.5f &&
                   obstacle.Size.z <= 3.5f;
        }

        private static Mesh CreateSurfaceMesh(RoomSurfaceMesh surface)
        {
            var vertices = new Vector3[surface.Vertices.Count];
            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i] = surface.Vertices[i];
            }

            var indices = new int[surface.Indices.Count];
            for (var i = 0; i < indices.Length; i++)
            {
                indices[i] = surface.Indices[i];
            }

            var mesh = new Mesh
            {
                name = $"Surface_{surface.Id}",
                indexFormat = vertices.Length > ushort.MaxValue
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
                vertices = vertices,
                triangles = indices
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        public bool TrySamplePosition(Vector3 desiredPosition, float maxDistance, out Vector3 sampledPosition)
        {
            if (NavMesh.SamplePosition(
                    desiredPosition,
                    out var hit,
                    Mathf.Max(sampleDistance, maxDistance),
                    NavMesh.AllAreas))
            {
                sampledPosition = hit.position;
                return true;
            }

            sampledPosition = default;
            return false;
        }

        public bool TryGetRandomReachablePoint(
            Vector3 origin,
            System.Random random,
            float minimumDistance,
            out Vector3 destination,
            out NavMeshPath path)
        {
            destination = default;
            path = new NavMeshPath();
            if (!IsReady || Snapshot.MainFloor == null)
            {
                return false;
            }

            var floorBounds = CalculateFloorBounds(Snapshot.MainFloor);
            for (var attempt = 0; attempt < 48; attempt++)
            {
                var desired = new Vector3(
                    Mathf.Lerp(floorBounds.min.x, floorBounds.max.x, (float)random.NextDouble()),
                    floorBounds.center.y,
                    Mathf.Lerp(floorBounds.min.z, floorBounds.max.z, (float)random.NextDouble()));
                if (!TrySamplePosition(desired, sampleDistance, out var sampled) ||
                    Vector3.Distance(origin, sampled) < minimumDistance)
                {
                    continue;
                }

                path.ClearCorners();
                if (!NavMesh.CalculatePath(origin, sampled, NavMesh.AllAreas, path) ||
                    path.status != NavMeshPathStatus.PathComplete)
                {
                    continue;
                }

                destination = sampled;
                return true;
            }

            return false;
        }

        public void Clear()
        {
            RemoveNavMeshInstance();

            if (geometryRoot != null)
            {
                DestroyOwnedObject(geometryRoot);
                geometryRoot = null;
            }

            foreach (var ownedObject in ownedObjects)
            {
                if (ownedObject != null)
                {
                    DestroyOwnedObject(ownedObject);
                }
            }

            ownedObjects.Clear();
            Snapshot = null;
            LastBuildMilliseconds = 0;
        }

        private void BuildAndAddNavMesh(
            NavMeshBuildSettings settings,
            List<NavMeshBuildSource> sources,
            Bounds bounds)
        {
            var navMeshData = NavMeshBuilder.BuildNavMeshData(
                settings,
                sources,
                bounds,
                Vector3.zero,
                Quaternion.identity);
            if (navMeshData == null)
            {
                throw new InvalidOperationException("Runtime NavMesh build returned no data.");
            }

            ownedObjects.Add(navMeshData);
            navMeshInstance = NavMesh.AddNavMeshData(navMeshData);
        }

        private void RemoveNavMeshInstance()
        {
            if (navMeshInstance.valid)
            {
                navMeshInstance.Remove();
            }
        }

        private void OnDestroy()
        {
            Clear();
        }

        private void CreateFloorGeometry(
            RoomFloorRegion floor,
            Mesh floorMesh,
            bool showDebugGeometry)
        {
            var floorObject = new GameObject($"Floor_{floor.Id}");
            floorObject.transform.SetParent(geometryRoot.transform, false);
            var filter = floorObject.AddComponent<MeshFilter>();
            filter.sharedMesh = floorMesh;
            var collider = floorObject.AddComponent<MeshCollider>();
            collider.sharedMesh = floorMesh;

            if (!showDebugGeometry)
            {
                return;
            }

            var renderer = floorObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateMaterial(
                new Color(0.18f, 0.42f, 0.48f, 1f),
                $"FloorMaterial_{floor.Id}");
        }

        private void CreateObstacleGeometry(RoomObstacle obstacle, bool showDebugGeometry)
        {
            GameObject obstacleObject;
            if (showDebugGeometry)
            {
                obstacleObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var renderer = obstacleObject.GetComponent<MeshRenderer>();
                var isWall = obstacle.Kind == RoomObstacleKind.Wall ||
                             obstacle.Kind == RoomObstacleKind.VirtualWall;
                renderer.sharedMaterial = CreateMaterial(
                    GetObstacleColor(obstacle.Kind),
                    $"ObstacleMaterial_{obstacle.Id}",
                    isWall);
            }
            else
            {
                obstacleObject = new GameObject();
                obstacleObject.AddComponent<BoxCollider>();
            }

            obstacleObject.name = $"{obstacle.Kind}_{obstacle.Id}";
            obstacleObject.transform.SetParent(geometryRoot.transform, false);
            obstacleObject.transform.SetPositionAndRotation(obstacle.Center, obstacle.Rotation);
            obstacleObject.transform.localScale = obstacle.Size;
        }

        private Material CreateMaterial(
            Color color,
            string materialName,
            bool transparent = false)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader)
            {
                name = materialName,
                color = color
            };
            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat(
                    "_DstBlend",
                    (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            ownedObjects.Add(material);
            return material;
        }

        private static Bounds CalculateFloorBounds(RoomFloorRegion floor)
        {
            var bounds = new Bounds(floor.Vertices[0], Vector3.zero);
            for (var i = 1; i < floor.Vertices.Count; i++)
            {
                bounds.Encapsulate(floor.Vertices[i]);
            }

            return bounds;
        }

        private static Color GetObstacleColor(RoomObstacleKind kind)
        {
            switch (kind)
            {
                case RoomObstacleKind.Wall:
                case RoomObstacleKind.VirtualWall:
                    return new Color(0.42f, 0.45f, 0.5f, 0.28f);
                case RoomObstacleKind.Table:
                    return new Color(0.55f, 0.31f, 0.18f, 1f);
                case RoomObstacleKind.Sofa:
                case RoomObstacleKind.Chair:
                    return new Color(0.48f, 0.29f, 0.55f, 1f);
                default:
                    return new Color(0.55f, 0.48f, 0.35f, 1f);
            }
        }

        private static void DestroyOwnedObject(UnityEngine.Object value)
        {
            if (Application.isPlaying)
            {
                Destroy(value);
            }
            else
            {
                DestroyImmediate(value);
            }
        }
    }
}
