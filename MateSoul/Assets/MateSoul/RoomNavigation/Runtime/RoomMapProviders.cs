using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.XR;

namespace MateSoul.RoomNavigation
{
    public interface IRoomMapProvider
    {
        string SourceName { get; }
        Task<RoomMapSnapshot> LoadAsync(CancellationToken cancellationToken);
    }

    public sealed class PicoMockRoomMapProvider : IRoomMapProvider
    {
        private const float WallThickness = 0.08f;
        private readonly TextAsset sceneData;

        public PicoMockRoomMapProvider(TextAsset sceneData)
        {
            this.sceneData = sceneData;
        }

        public string SourceName => "PICO SDK MockSceneData";

        public Task<RoomMapSnapshot> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sceneData == null || string.IsNullOrWhiteSpace(sceneData.text))
            {
                throw new InvalidOperationException("PICO SDK MockSceneData.json is not assigned.");
            }

            var wrapper = JsonUtility.FromJson<MockSceneAnchorCollection>(
                $"{{\"anchors\":{sceneData.text}}}");
            if (wrapper?.anchors == null || wrapper.anchors.Length == 0)
            {
                throw new InvalidOperationException("PICO SDK mock scene data contains no anchors.");
            }

            var floors = new List<RoomFloorRegion>();
            var obstacles = new List<RoomObstacle>();
            var realityItems = new List<RealityItemObservation>();
            var wallEndpoints = new List<Vector3>();
            var floorHeightSum = 0f;
            var floorHeightSamples = 0;

            foreach (var anchor in wrapper.anchors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rotation = NormalizeOrIdentity(anchor.Rotation);
                var label = ParseSemanticLabel(anchor.SemanticLabel);

                if (label == PxrSemanticLabel.Floor &&
                    anchor.PolygonVertices != null &&
                    anchor.PolygonVertices.Length >= 3)
                {
                    floors.Add(new RoomFloorRegion(
                        anchor.Guid,
                        TransformPolygon(anchor.PolygonVertices, anchor.Position, rotation)));
                    continue;
                }

                if ((label == PxrSemanticLabel.Wall || label == PxrSemanticLabel.VirtualWall) &&
                    anchor.Box2DInfo != null)
                {
                    var center = anchor.Position +
                                 rotation * new Vector3(
                                     anchor.Box2DInfo.Offset.x,
                                     anchor.Box2DInfo.Offset.y,
                                     0f);
                    var extent = anchor.Box2DInfo.Extent;
                    var halfWidth = extent.x * 0.5f;
                    var wallAxis = rotation * Vector3.right;
                    wallEndpoints.Add(center - wallAxis * halfWidth);
                    wallEndpoints.Add(center + wallAxis * halfWidth);
                    floorHeightSum += center.y - extent.y * 0.5f;
                    floorHeightSamples++;

                    obstacles.Add(new RoomObstacle(
                        anchor.Guid,
                        label == PxrSemanticLabel.VirtualWall
                            ? RoomObstacleKind.VirtualWall
                            : RoomObstacleKind.Wall,
                        center,
                        rotation,
                        new Vector3(extent.x, extent.y, WallThickness)));
                    continue;
                }

                var semantic = RealitySemanticCatalog.FromPico(label);
                var definition = RealitySemanticCatalog.Get(semantic);
                if (definition.IsItem && definition.Geometry == RealityGeometryKind.Box2D &&
                    anchor.Box2DInfo != null)
                {
                    var center = anchor.Position + rotation * new Vector3(
                        anchor.Box2DInfo.Offset.x, anchor.Box2DInfo.Offset.y, 0f);
                    realityItems.Add(new RealityItemObservation(anchor.Guid, semantic, center,
                        rotation, new Vector3(anchor.Box2DInfo.Extent.x,
                            anchor.Box2DInfo.Extent.y, WallThickness)));
                    continue;
                }

                if (definition.Geometry != RealityGeometryKind.Box3D || anchor.Box3DInfo == null)
                {
                    continue;
                }

                var localRotation = NormalizeOrIdentity(anchor.Box3DInfo.Rotation);
                var itemCenter = anchor.Position + rotation * anchor.Box3DInfo.Offset;
                var itemRotation = rotation * localRotation;
                if (definition.IsItem)
                    realityItems.Add(new RealityItemObservation(anchor.Guid, semantic,
                        itemCenter, itemRotation, anchor.Box3DInfo.Extent));
                if (!TryMapObstacleKind(label, out var kind)) continue;
                obstacles.Add(new RoomObstacle(
                    anchor.Guid,
                    kind,
                    itemCenter,
                    itemRotation,
                    anchor.Box3DInfo.Extent));
            }

            if (floors.Count == 0 && wallEndpoints.Count >= 3)
            {
                var floorHeight = floorHeightSamples > 0
                    ? floorHeightSum / floorHeightSamples
                    : 0f;
                var inferredFloor = RoomGeometry.ConvexHullXZ(wallEndpoints, floorHeight);
                floors.Add(new RoomFloorRegion("mock-inferred-floor", inferredFloor));
            }

            var snapshot = new RoomMapSnapshot(
                "pico-sdk-mock-room",
                SourceName,
                floors,
                obstacles,
                realityItems: realityItems);
            return Task.FromResult(snapshot);
        }

        private static IReadOnlyList<Vector3> TransformPolygon(
            IReadOnlyList<Vector2> localVertices,
            Vector3 position,
            Quaternion rotation)
        {
            var vertices = new List<Vector3>(localVertices.Count);
            foreach (var vertex in localVertices)
            {
                vertices.Add(position + rotation * new Vector3(vertex.x, vertex.y, 0f));
            }

            return vertices;
        }

        private static PxrSemanticLabel ParseSemanticLabel(string value)
        {
            return Enum.TryParse(value, true, out PxrSemanticLabel label)
                ? label
                : PxrSemanticLabel.Unknown;
        }

        private static Quaternion NormalizeOrIdentity(Quaternion value)
        {
            return Quaternion.Dot(value, value) > 0.0001f
                ? Quaternion.Normalize(value)
                : Quaternion.identity;
        }

        [Serializable]
        private sealed class MockSceneAnchorCollection
        {
            public MockSceneAnchor[] anchors;
        }

        [Serializable]
        private sealed class MockSceneAnchor
        {
            public string Guid;
            public Vector3 Position;
            public Quaternion Rotation;
            public string SemanticLabel;
            public MockBox3D Box3DInfo;
            public MockBox2D Box2DInfo;
            public Vector2[] PolygonVertices;
        }

        [Serializable]
        private sealed class MockBox2D
        {
            public Vector2 Offset;
            public Vector2 Extent;
        }

        [Serializable]
        private sealed class MockBox3D
        {
            public Vector3 Offset;
            public Quaternion Rotation;
            public Vector3 Extent;
        }

        internal static bool TryMapObstacleKind(PxrSemanticLabel label, out RoomObstacleKind kind)
        {
            switch (label)
            {
                case PxrSemanticLabel.Wall:
                    kind = RoomObstacleKind.Wall;
                    return true;
                case PxrSemanticLabel.VirtualWall:
                    kind = RoomObstacleKind.VirtualWall;
                    return true;
                case PxrSemanticLabel.Table:
                    kind = RoomObstacleKind.Table;
                    return true;
                case PxrSemanticLabel.Sofa:
                    kind = RoomObstacleKind.Sofa;
                    return true;
                case PxrSemanticLabel.Chair:
                    kind = RoomObstacleKind.Chair;
                    return true;
                case PxrSemanticLabel.Cabinet:
                    kind = RoomObstacleKind.Cabinet;
                    return true;
                case PxrSemanticLabel.Bed:
                    kind = RoomObstacleKind.Bed;
                    return true;
                case PxrSemanticLabel.Plant:
                    kind = RoomObstacleKind.Plant;
                    return true;
                case PxrSemanticLabel.Screen:
                    kind = RoomObstacleKind.Screen;
                    return true;
                case PxrSemanticLabel.Refrigerator:
                    kind = RoomObstacleKind.Refrigerator;
                    return true;
                case PxrSemanticLabel.WashingMachine:
                    kind = RoomObstacleKind.WashingMachine;
                    return true;
                case PxrSemanticLabel.AirConditioner:
                    kind = RoomObstacleKind.AirConditioner;
                    return true;
                case PxrSemanticLabel.Lamp:
                    kind = RoomObstacleKind.Lamp;
                    return true;
                default:
                    kind = RoomObstacleKind.Other;
                    return false;
            }
        }
    }

    public sealed class PicoSpatialMeshRoomMapProvider : IRoomMapProvider
    {
        public const string ProviderName = "PICO Spatial Mesh";
        public const float MinimumMeasuredFloorArea = 1f;
        private const float FloorCellSize = .16f;
        private const float TrackingFloorTolerance = .30f;
        private readonly TimeSpan timeout;
        private readonly Action<string> trace;
        private readonly float? trackingFloorYOverride;
        private readonly List<XRMeshSubsystem> meshSubsystems = new List<XRMeshSubsystem>();
        private readonly List<XRMeshSubsystem> startedSubsystems = new List<XRMeshSubsystem>();
        private int lastSemanticFloorTriangles;
        private int lastGeometricFloorTriangles;
        private float lastExpectedFloorY;
        private string lastFloorMode;

        public PicoSpatialMeshRoomMapProvider(
            TimeSpan timeout,
            Action<string> trace = null,
            float? trackingFloorYOverride = null)
        {
            this.timeout = timeout;
            this.trace = trace;
            this.trackingFloorYOverride = trackingFloorYOverride;
        }

        public string SourceName => ProviderName;

        public static bool IsSource(string source)
            => string.Equals(source, ProviderName, StringComparison.Ordinal);

        public async Task<RoomMapSnapshot> LoadAsync(CancellationToken cancellationToken)
        {
            trace?.Invoke("mesh_subsystem_wait_start");
            var deadline = DateTime.UtcNow + timeout;
            do
            {
                meshSubsystems.Clear();
                SubsystemManager.GetSubsystems(meshSubsystems);
                if (meshSubsystems.Count > 0) break;
                await Task.Delay(100, cancellationToken);
            }
            while (DateTime.UtcNow < deadline);
            if (meshSubsystems.Count == 0)
            {
                throw new InvalidOperationException("PICO XR mesh subsystem is unavailable.");
            }

            try
            {
                trace?.Invoke("mesh_subsystem_available");
                startedSubsystems.Clear();
                foreach (var subsystem in meshSubsystems)
                {
                    if (!subsystem.running)
                    {
                        subsystem.Start();
                        startedSubsystems.Add(subsystem);
                    }
                }

                trace?.Invoke("mesh_subsystem_started");
                var cached = PXR_Plugin.MixedReality.SpatialMeshData.Values
                    .Where(mesh => mesh.state != MeshChangeState.Removed)
                    .ToList();
                if (cached.Count > 0)
                {
                    var cachedSnapshot = BuildSnapshot(cached);
                    var cachedValidation = ValidateSnapshot(cachedSnapshot);
                    trace?.Invoke("mesh_cached_snapshot_built");
                    if (cachedValidation.IsValid)
                    {
                        Debug.Log(
                            $"[RoomNavigation] Cached spatial mesh blocks=" +
                            $"{cachedSnapshot.SurfaceMeshes.Count} " +
                            $"floorArea={cachedValidation.FloorArea:F2}m2 " +
                            $"floorY={cachedSnapshot.MainFloor.Centroid.y:F3} " +
                            $"bounds={cachedSnapshot.Bounds}");
                        return cachedSnapshot;
                    }
                    LogRejectedSnapshot("cache", cachedSnapshot, cachedValidation);
                }

                var lastResult = PxrResult.ERROR_RUNTIME_FAILURE;
                var queryCount = 0;
                var lastValidationError = string.Empty;
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    trace?.Invoke($"mesh_query_{++queryCount}_start");
                    var queryResult = await PXR_MixedReality.QueryMeshAnchorAsync(cancellationToken);
                    trace?.Invoke($"mesh_query_{queryCount}_done");
                    lastResult = queryResult.result;
                    if (queryResult.result == PxrResult.SUCCESS && queryResult.meshInfos.Count > 0)
                    {
                        var snapshot = BuildSnapshot(queryResult.meshInfos);
                        var validation = ValidateSnapshot(snapshot);
                        trace?.Invoke($"mesh_snapshot_{queryCount}_built");
                        if (validation.IsValid)
                        {
                            Debug.Log(
                                $"[RoomNavigation] Spatial mesh snapshot blocks=" +
                                $"{snapshot.SurfaceMeshes.Count} floorArea={validation.FloorArea:F2}m2 " +
                                $"floorY={snapshot.MainFloor.Centroid.y:F3} bounds={snapshot.Bounds}");
                            return snapshot;
                        }
                        if (queryCount == 1 ||
                            !string.Equals(lastValidationError, validation.Error,
                                StringComparison.Ordinal))
                            LogRejectedSnapshot(
                                $"query-{queryCount}", snapshot, validation);
                        lastValidationError = validation.Error;
                    }

                    await Task.Delay(500, cancellationToken);
                }
                while (DateTime.UtcNow < deadline);

                throw new InvalidOperationException(
                    $"PICO Spatial Mesh was not navigation-ready within " +
                    $"{timeout.TotalSeconds:F0}s. Last query result: {lastResult}.");
            }
            finally
            {
                foreach (var subsystem in startedSubsystems)
                {
                    if (subsystem.running)
                    {
                        subsystem.Stop();
                    }
                }
                startedSubsystems.Clear();
            }
        }

        internal static RoomMapValidationResult ValidateSnapshot(RoomMapSnapshot snapshot)
            => snapshot?.Validate(MinimumMeasuredFloorArea) ??
               RoomMapValidationResult.Invalid("Spatial mesh snapshot is null.");

        internal RoomMapSnapshot BuildSnapshot(IReadOnlyList<PxrSpatialMeshInfo> meshInfos)
        {
            var floorTriangles = new List<FloorTriangle>();
            var horizontalUnknownTriangles = new List<FloorTriangle>();
            var obstacles = new List<RoomObstacle>();
            var surfaceMeshes = new List<RoomSurfaceMesh>();
            var realityItems = new List<RealityItemObservation>();
            for (var blockIndex = 0; blockIndex < meshInfos.Count; blockIndex++)
            {
                var block = meshInfos[blockIndex];
                if (block.state == MeshChangeState.Removed ||
                    block.vertices == null ||
                    block.vertices.Length == 0)
                {
                    continue;
                }

                var matrix = Matrix4x4.TRS(block.position, block.rotation, Vector3.one);
                var obstacleBounds = new Dictionary<RoomObstacleKind, BoundsAccumulator>();
                var realityBounds = new Dictionary<RealitySemantic, BoundsAccumulator>();
                var worldVertices = new Vector3[block.vertices.Length];
                for (var vertexIndex = 0; vertexIndex < block.vertices.Length; vertexIndex++)
                {
                    var worldVertex = matrix.MultiplyPoint3x4(block.vertices[vertexIndex]);
                    worldVertices[vertexIndex] = worldVertex;
                    var label = block.labels != null && vertexIndex < block.labels.Length
                        ? block.labels[vertexIndex]
                        : PxrSemanticLabel.Unknown;

                    if (label == PxrSemanticLabel.Floor)
                    {
                        continue;
                    }

                    var semantic = RealitySemanticCatalog.FromPico(label);
                    if (RealitySemanticCatalog.Get(semantic).IsItem)
                    {
                        if (!realityBounds.TryGetValue(semantic, out var realityAccumulator))
                        {
                            realityAccumulator = new BoundsAccumulator();
                            realityBounds.Add(semantic, realityAccumulator);
                        }
                        realityAccumulator.Encapsulate(worldVertex);
                    }

                    if (!PicoMockRoomMapProvider.TryMapObstacleKind(label, out var kind))
                    {
                        continue;
                    }

                    if (!obstacleBounds.TryGetValue(kind, out var accumulator))
                    {
                        accumulator = new BoundsAccumulator();
                        obstacleBounds.Add(kind, accumulator);
                    }

                    accumulator.Encapsulate(worldVertex);
                }

                if (block.indices != null && block.indices.Length >= 3)
                {
                    var indices = new int[block.indices.Length];
                    for (var index = 0; index < block.indices.Length; index++)
                    {
                        indices[index] = block.indices[index];
                    }
                    for (var index = 0; index + 2 < indices.Length; index += 3)
                    {
                        var a = indices[index];
                        var b = indices[index + 1];
                        var c = indices[index + 2];
                        if (a < 0 || b < 0 || c < 0 ||
                            a >= worldVertices.Length ||
                            b >= worldVertices.Length ||
                            c >= worldVertices.Length)
                            continue;
                        var triangle = new FloorTriangle(
                            worldVertices[a], worldVertices[b], worldVertices[c]);
                        if (triangle.Area <= .0001f ||
                            Mathf.Abs(triangle.Normal.y) < .75f)
                            continue;
                        var triangleLabel = TriangleLabel(block, a, b, c);
                        if (triangleLabel == PxrSemanticLabel.Floor)
                            floorTriangles.Add(triangle);
                        else if (triangleLabel == PxrSemanticLabel.Unknown)
                            horizontalUnknownTriangles.Add(triangle);
                    }

                    surfaceMeshes.Add(new RoomSurfaceMesh(
                        $"mesh-{blockIndex}",
                        worldVertices,
                        indices,
                        block.uuid.GetHashCode()));
                }

                foreach (var item in realityBounds)
                {
                    if (!item.Value.HasValue) continue;
                    var definition = RealitySemanticCatalog.Get(item.Key);
                    realityItems.Add(new RealityItemObservation(
                        $"mesh-{blockIndex}-{definition.Id}",
                        item.Key,
                        item.Value.Bounds.center,
                        Quaternion.identity,
                        MinimumSize(item.Value.Bounds.size)));
                }

                foreach (var item in obstacleBounds)
                {
                    if (!item.Value.HasValue)
                    {
                        continue;
                    }

                    obstacles.Add(new RoomObstacle(
                        $"mesh-{blockIndex}-{item.Key}",
                        item.Key,
                        item.Value.Bounds.center,
                        Quaternion.identity,
                        MinimumSize(item.Value.Bounds.size)));
                }
            }

            lastExpectedFloorY = trackingFloorYOverride ??
                                 (Camera.main != null
                                     ? Camera.main.transform.root.position.y
                                     : 0f);
            lastSemanticFloorTriangles = floorTriangles.Count;
            var floor = BuildMeasuredFloor(floorTriangles);
            lastFloorMode = floor != null ? "semantic" : "none";
            var needsGeometricFloor = floor == null ||
                                      floor.Area < MinimumMeasuredFloorArea;
            if (needsGeometricFloor)
            {
                var geometricFloorTriangles = floorTriangles
                    .Concat(horizontalUnknownTriangles)
                    .Where(triangle =>
                        Mathf.Abs(triangle.Center.y - lastExpectedFloorY) <=
                        TrackingFloorTolerance)
                    .ToList();
                lastGeometricFloorTriangles = geometricFloorTriangles.Count;
                var geometricFloor = BuildMeasuredFloor(geometricFloorTriangles);
                if (geometricFloor != null &&
                    (floor == null || geometricFloor.Area > floor.Area))
                {
                    floor = geometricFloor;
                    lastFloorMode = "tracking-floor-geometry";
                }
            }
            else
            {
                lastGeometricFloorTriangles = 0;
            }
            var floors = floor == null
                ? Array.Empty<RoomFloorRegion>()
                : new[] { floor };

            return new RoomMapSnapshot(
                $"pico-spatial-mesh-{DateTime.UtcNow:yyyyMMddHHmmss}",
                SourceName,
                floors,
                obstacles,
                surfaceMeshes,
                realityItems);
        }

        private void LogRejectedSnapshot(
            string stage,
            RoomMapSnapshot snapshot,
            RoomMapValidationResult validation)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.LogWarning(
                $"[RoomNavigation] Spatial mesh rejected stage={stage} " +
                $"blocks={snapshot?.SurfaceMeshes.Count ?? 0} " +
                $"floors={snapshot?.Floors.Count ?? 0} " +
                $"floorArea={(snapshot?.MainFloor != null ? snapshot.MainFloor.Area : 0f):F2} " +
                $"semanticFloorTriangles={lastSemanticFloorTriangles} " +
                $"geometricFloorTriangles={lastGeometricFloorTriangles} " +
                $"expectedFloorY={lastExpectedFloorY:F3} mode={lastFloorMode} " +
                $"reason={validation.Error}");
#endif
        }

        private static RoomFloorRegion BuildMeasuredFloor(
            IReadOnlyList<FloorTriangle> triangles)
        {
            if (triangles == null || triangles.Count == 0) return null;
            var cells = new HashSet<Vector2Int>();
            var weightedHeight = 0f;
            var totalArea = 0f;
            foreach (var triangle in triangles)
            {
                weightedHeight += triangle.Center.y * triangle.Area;
                totalArea += triangle.Area;
                var minX = Mathf.FloorToInt(Mathf.Min(
                    triangle.A.x, Mathf.Min(triangle.B.x, triangle.C.x)) / FloorCellSize);
                var maxX = Mathf.FloorToInt(Mathf.Max(
                    triangle.A.x, Mathf.Max(triangle.B.x, triangle.C.x)) / FloorCellSize);
                var minZ = Mathf.FloorToInt(Mathf.Min(
                    triangle.A.z, Mathf.Min(triangle.B.z, triangle.C.z)) / FloorCellSize);
                var maxZ = Mathf.FloorToInt(Mathf.Max(
                    triangle.A.z, Mathf.Max(triangle.B.z, triangle.C.z)) / FloorCellSize);
                for (var x = minX; x <= maxX; x++)
                    for (var z = minZ; z <= maxZ; z++)
                    {
                        var cell = new Vector2Int(x, z);
                        if (IntersectsCell(triangle, cell)) cells.Add(cell);
                    }
            }
            if (cells.Count == 0 || totalArea <= 0) return null;

            var components = ConnectedComponents(cells);
            var camera = Camera.main;
            var preferred = components
                .Where(component =>
                    component.Count * FloorCellSize * FloorCellSize >=
                    MinimumMeasuredFloorArea)
                .OrderBy(component => camera == null ? 0f : component.Min(cell =>
                {
                    var center = CellCenter(cell);
                    var delta = new Vector2(
                        center.x - camera.transform.position.x,
                        center.y - camera.transform.position.z);
                    return delta.sqrMagnitude;
                }))
                .ThenByDescending(component => component.Count)
                .FirstOrDefault();
            if (preferred == null)
                preferred = components.OrderByDescending(component => component.Count)
                    .FirstOrDefault();
            if (preferred == null) return null;

            var loop = LargestBoundaryLoop(preferred);
            if (loop == null || loop.Count < 3) return null;
            var floorY = weightedHeight / totalArea;
            var vertices = new List<Vector3>(loop.Count);
            foreach (var point in loop)
                vertices.Add(new Vector3(
                    point.x * FloorCellSize, floorY, point.y * FloorCellSize));
            return new RoomFloorRegion("spatial-mesh-measured-floor", vertices);
        }

        private static List<HashSet<Vector2Int>> ConnectedComponents(
            HashSet<Vector2Int> cells)
        {
            var remaining = new HashSet<Vector2Int>(cells);
            var result = new List<HashSet<Vector2Int>>();
            var queue = new Queue<Vector2Int>();
            var offsets = new[]
            {
                Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down
            };
            while (remaining.Count > 0)
            {
                var first = remaining.First();
                var component = new HashSet<Vector2Int>();
                remaining.Remove(first);
                queue.Enqueue(first);
                while (queue.Count > 0)
                {
                    var cell = queue.Dequeue();
                    component.Add(cell);
                    foreach (var offset in offsets)
                    {
                        var neighbor = cell + offset;
                        if (!remaining.Remove(neighbor)) continue;
                        queue.Enqueue(neighbor);
                    }
                }
                result.Add(component);
            }
            return result;
        }

        private static List<Vector2Int> LargestBoundaryLoop(
            ISet<Vector2Int> cells)
        {
            var edges = new Dictionary<Vector2Int, List<Vector2Int>>();
            foreach (var cell in cells)
            {
                var bottomLeft = new Vector2Int(cell.x, cell.y);
                var bottomRight = new Vector2Int(cell.x + 1, cell.y);
                var topRight = new Vector2Int(cell.x + 1, cell.y + 1);
                var topLeft = new Vector2Int(cell.x, cell.y + 1);
                if (!cells.Contains(cell + Vector2Int.down))
                    AddEdge(bottomLeft, bottomRight);
                if (!cells.Contains(cell + Vector2Int.right))
                    AddEdge(bottomRight, topRight);
                if (!cells.Contains(cell + Vector2Int.up))
                    AddEdge(topRight, topLeft);
                if (!cells.Contains(cell + Vector2Int.left))
                    AddEdge(topLeft, bottomLeft);
            }

            var loops = new List<List<Vector2Int>>();
            while (edges.Count > 0)
            {
                var start = edges.First().Key;
                var loop = new List<Vector2Int>();
                var current = start;
                var guard = edges.Sum(pair => pair.Value.Count) + 1;
                while (guard-- > 0)
                {
                    loop.Add(current);
                    if (!edges.TryGetValue(current, out var nextValues) ||
                        nextValues.Count == 0)
                    {
                        loop.Clear();
                        break;
                    }
                    var next = nextValues[nextValues.Count - 1];
                    nextValues.RemoveAt(nextValues.Count - 1);
                    if (nextValues.Count == 0) edges.Remove(current);
                    current = next;
                    if (current == start) break;
                }
                if (loop.Count >= 3 && current == start)
                    loops.Add(SimplifyAxisAligned(loop));
            }
            return loops.OrderByDescending(loop =>
                Mathf.Abs(SignedArea(loop))).FirstOrDefault();

            void AddEdge(Vector2Int from, Vector2Int to)
            {
                if (!edges.TryGetValue(from, out var values))
                {
                    values = new List<Vector2Int>();
                    edges.Add(from, values);
                }
                values.Add(to);
            }
        }

        private static List<Vector2Int> SimplifyAxisAligned(
            IReadOnlyList<Vector2Int> loop)
        {
            var result = new List<Vector2Int>();
            for (var index = 0; index < loop.Count; index++)
            {
                var previous = loop[(index - 1 + loop.Count) % loop.Count];
                var current = loop[index];
                var next = loop[(index + 1) % loop.Count];
                var before = current - previous;
                var after = next - current;
                if (before.x * after.y - before.y * after.x != 0)
                    result.Add(current);
            }
            return result;
        }

        private static float SignedArea(IReadOnlyList<Vector2Int> vertices)
        {
            var area = 0f;
            for (var index = 0; index < vertices.Count; index++)
            {
                var current = vertices[index];
                var next = vertices[(index + 1) % vertices.Count];
                area += current.x * next.y - next.x * current.y;
            }
            return area * .5f;
        }

        private static bool IntersectsCell(
            FloorTriangle triangle, Vector2Int cell)
        {
            var center = CellCenter(cell);
            var a = new Vector2(triangle.A.x, triangle.A.z) - center;
            var b = new Vector2(triangle.B.x, triangle.B.z) - center;
            var c = new Vector2(triangle.C.x, triangle.C.z) - center;
            return !Separates(b - a) && !Separates(c - b) && !Separates(a - c);

            bool Separates(Vector2 edge)
            {
                var axis = new Vector2(-edge.y, edge.x);
                var radius = FloorCellSize * .5f *
                             (Mathf.Abs(axis.x) + Mathf.Abs(axis.y));
                var x = Vector2.Dot(a, axis);
                var y = Vector2.Dot(b, axis);
                var z = Vector2.Dot(c, axis);
                return Mathf.Min(x, Mathf.Min(y, z)) > radius ||
                       Mathf.Max(x, Mathf.Max(y, z)) < -radius;
            }
        }

        private static Vector2 CellCenter(Vector2Int cell)
            => new Vector2(
                (cell.x + .5f) * FloorCellSize,
                (cell.y + .5f) * FloorCellSize);

        private static PxrSemanticLabel TriangleLabel(
            PxrSpatialMeshInfo block, int a, int b, int c)
        {
            var first = VertexLabel(block, a);
            var second = VertexLabel(block, b);
            var third = VertexLabel(block, c);
            if (first == second || first == third) return first;
            return second == third ? second : PxrSemanticLabel.Unknown;
        }

        private static PxrSemanticLabel VertexLabel(
            PxrSpatialMeshInfo block, int index)
            => block.labels != null && index >= 0 && index < block.labels.Length
                ? block.labels[index]
                : PxrSemanticLabel.Unknown;

        private static Vector3 MinimumSize(Vector3 size)
            => new Vector3(Mathf.Max(size.x, .05f), Mathf.Max(size.y, .10f),
                Mathf.Max(size.z, .05f));

        private readonly struct FloorTriangle
        {
            public FloorTriangle(Vector3 a, Vector3 b, Vector3 c)
            {
                A = a;
                B = b;
                C = c;
            }

            public Vector3 A { get; }
            public Vector3 B { get; }
            public Vector3 C { get; }
            public Vector3 Center => (A + B + C) / 3f;
            public Vector3 Normal => Vector3.Cross(B - A, C - A).normalized;
            public float Area => Vector3.Cross(B - A, C - A).magnitude * .5f;
        }

        private sealed class BoundsAccumulator
        {
            public bool HasValue { get; private set; }
            public Bounds Bounds { get; private set; }

            public void Encapsulate(Vector3 point)
            {
                if (!HasValue)
                {
                    Bounds = new Bounds(point, Vector3.zero);
                    HasValue = true;
                    return;
                }

                var bounds = Bounds;
                bounds.Encapsulate(point);
                Bounds = bounds;
            }
        }
    }

    public sealed class PicoSceneCaptureRoomMapProvider : IRoomMapProvider
    {
        private const float WallThickness = 0.08f;
        private static readonly TimeSpan QueryInterval = TimeSpan.FromSeconds(2);
        private readonly Action<string> trace;
        private readonly TimeSpan? timeout;

        public PicoSceneCaptureRoomMapProvider(
            Action<string> trace = null,
            TimeSpan? timeout = null)
        {
            this.trace = trace;
            this.timeout = timeout;
        }

        public string SourceName => "PICO Room Capture";

        public async Task<RoomMapSnapshot> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var providerStarted = false;
            var deadline = timeout.HasValue
                ? DateTime.UtcNow + timeout.Value
                : DateTime.MaxValue;
            var updateVersion = 0;
            var dataUpdated = new TaskCompletionSource<bool>();
            Action onDataUpdated = () =>
            {
                updateVersion++;
                dataUpdated.TrySetResult(true);
            };
            // Localization may finish during provider startup or an in-flight query.
            // Keep this provider alive until the owning bootstrap is canceled. PICO can
            // identify and import a room map after its initial queries have returned empty.
            PXR_Manager.SceneAnchorDataUpdated += onDataUpdated;
#region debug-point A-B:room-map-activation
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var diagnosticQueries = 0;
            var diagnosticStart = System.Diagnostics.Stopwatch.StartNew();
            var settingsProbe = UnityEngine.Networking.UnityWebRequest.Post(
                "http://127.0.0.1:7792/event",
                $"{{\"sessionId\":\"room-map-activation\",\"runId\":\"post-fix\",\"hypothesisId\":\"A-B\",\"location\":\"RoomMapProviders.LoadAsync\",\"msg\":\"[DEBUG] room safety configuration\",\"data\":{{\"mrSafeguard\":{(PXR_ProjectSetting.GetProjectConfig().mrSafeguard ? 1 : 0)},\"focused\":{(Application.isFocused ? 1 : 0)}}}}}",
                "application/json");
            settingsProbe.timeout = 2;
            settingsProbe.SendWebRequest().completed += _ => settingsProbe.Dispose();
#endif
#endregion
            try
            {
                trace?.Invoke("scene_provider_start");
                var startResult = await PXR_MixedReality.StartSenseDataProvider(
                    PxrSenseDataProviderType.SceneCapture,
                    cancellationToken);
                if (startResult != PxrResult.SUCCESS)
                {
                    throw new InvalidOperationException(
                        $"Scene Capture provider failed to start: {startResult}.");
                }

                providerStarted = true;
                trace?.Invoke("scene_provider_started");
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow >= deadline)
                        throw new TimeoutException(
                            $"Scene Capture returned no usable room within " +
                            $"{timeout.Value.TotalSeconds:F0}s.");
                    dataUpdated = new TaskCompletionSource<bool>();
                    var queriedVersion = updateVersion;
                    var queryResult = await PXR_MixedReality.QuerySceneAnchorAsync(cancellationToken);
                    trace?.Invoke("scene_query_done");
#region debug-point B-C:room-map-query
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    var queryPayload =
                        $"{{\"sessionId\":\"room-map-activation\",\"runId\":\"post-fix\",\"hypothesisId\":\"B-C\",\"location\":\"RoomMapProviders.LoadAsync\",\"msg\":\"[DEBUG] current room query\",\"data\":{{\"result\":\"{queryResult.result}\",\"anchors\":{queryResult.anchorDictionary.Count},\"query\":{++diagnosticQueries},\"updates\":{updateVersion},\"elapsedMs\":{diagnosticStart.ElapsedMilliseconds},\"focused\":{(Application.isFocused ? 1 : 0)}}}}}";
                    Debug.Log("MATESOUL_ROOM_TRACE " + queryPayload);
                    var queryProbe = UnityEngine.Networking.UnityWebRequest.Post(
                        "http://127.0.0.1:7792/event", queryPayload, "application/json");
                    queryProbe.timeout = 2;
                    queryProbe.SendWebRequest().completed += _ => queryProbe.Dispose();
#endif
#endregion
                    cancellationToken.ThrowIfCancellationRequested();
                    if (queryResult.result == PxrResult.SUCCESS)
                    {
                        if (queryResult.anchorDictionary.Count > 0)
                        {
                            var snapshot = BuildSnapshot(queryResult.anchorDictionary, cancellationToken);
                            var validation = snapshot.Validate();
#region debug-point D:room-map-validation
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                            var snapshotProbe = UnityEngine.Networking.UnityWebRequest.Post(
                                "http://127.0.0.1:7792/event",
                                $"{{\"sessionId\":\"room-map-activation\",\"runId\":\"post-fix\",\"hypothesisId\":\"D\",\"location\":\"RoomMapProviders.BuildSnapshot\",\"msg\":\"[DEBUG] room validation\",\"data\":{{\"floors\":{snapshot.Floors.Count},\"obstacles\":{snapshot.Obstacles.Count},\"valid\":{(validation.IsValid ? 1 : 0)}}}}}",
                                "application/json");
                            snapshotProbe.timeout = 2;
                            snapshotProbe.SendWebRequest().completed += _ => snapshotProbe.Dispose();
#endif
#endregion
                            if (validation.IsValid)
                            {
                                trace?.Invoke("scene_snapshot_built");
                                return snapshot;
                            }
                        }
                    }
                    else if (queryResult.result != PxrResult.ERROR_RUNTIME_FAILURE)
                    {
                        throw new InvalidOperationException(
                            $"Scene Anchor query failed: {queryResult.result}.");
                    }
                    trace?.Invoke("scene_localization_wait");
                    if (queriedVersion != updateVersion) continue;
                    // The periodic query also covers platforms that omit a data-update event.
                    await Task.WhenAny(
                        dataUpdated.Task,
                        Task.Delay(QueryInterval, cancellationToken));
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            finally
            {
                PXR_Manager.SceneAnchorDataUpdated -= onDataUpdated;
                if (providerStarted)
                {
                    PXR_MixedReality.StopSenseDataProvider(PxrSenseDataProviderType.SceneCapture);
                }
            }
        }

        private RoomMapSnapshot BuildSnapshot(
            IReadOnlyDictionary<ulong, Guid> anchors,
            CancellationToken cancellationToken)
        {
            var floors = new List<RoomFloorRegion>();
            var obstacles = new List<RoomObstacle>();
            var realityItems = new List<RealityItemObservation>();

            foreach (var anchor in anchors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (PXR_MixedReality.GetSceneSemanticLabel(anchor.Key, out var label) != PxrResult.SUCCESS ||
                    PXR_MixedReality.LocateAnchor(anchor.Key, out var anchorPosition, out var anchorRotation) !=
                    PxrResult.SUCCESS)
                {
                    continue;
                }

                if (label == PxrSemanticLabel.Floor &&
                    PXR_MixedReality.GetScenePolygonData(anchor.Key, out var polygon) == PxrResult.SUCCESS &&
                    polygon.Length >= 3)
                {
                    var vertices = new List<Vector3>(polygon.Length);
                    foreach (var vertex in polygon)
                    {
                        vertices.Add(
                            anchorPosition + anchorRotation * new Vector3(vertex.x, vertex.y, 0f));
                    }

#region debug-point D:room-floor-raw-pose
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    Debug.Log($"MATESOUL_ROOM_FLOOR id={anchor.Value} " +
                              $"anchorPosition={anchorPosition:F5} anchorRotation={anchorRotation:F5} " +
                              $"vertices={string.Join(";", vertices.ConvertAll(vertex => vertex.ToString("F5")))}");
#endif
#endregion
                    floors.Add(new RoomFloorRegion(anchor.Value.ToString(), vertices));
                    continue;
                }

                if ((label == PxrSemanticLabel.Wall || label == PxrSemanticLabel.VirtualWall) &&
                    PXR_MixedReality.GetSceneBox2DData(anchor.Key, out var offset, out var extent) ==
                    PxrResult.SUCCESS)
                {
                    obstacles.Add(new RoomObstacle(
                        anchor.Value.ToString(),
                        label == PxrSemanticLabel.VirtualWall
                            ? RoomObstacleKind.VirtualWall
                            : RoomObstacleKind.Wall,
                        anchorPosition + anchorRotation * new Vector3(offset.x, offset.y, 0f),
                        anchorRotation,
                        new Vector3(extent.x, extent.y, WallThickness)));
                    continue;
                }

                var semantic = RealitySemanticCatalog.FromPico(label);
                var definition = RealitySemanticCatalog.Get(semantic);
                if (definition.IsItem && definition.Geometry == RealityGeometryKind.Box2D)
                {
                    if (PXR_MixedReality.GetSceneBox2DData(anchor.Key, out var itemOffset,
                            out var itemExtent) == PxrResult.SUCCESS)
                        realityItems.Add(new RealityItemObservation(anchor.Value.ToString(), semantic,
                            anchorPosition + anchorRotation * new Vector3(itemOffset.x, itemOffset.y, 0),
                            anchorRotation, new Vector3(itemExtent.x, itemExtent.y, WallThickness)));
                    continue;
                }

                if (PXR_MixedReality.GetSceneBox3DData(anchor.Key, out var localPosition,
                        out var localRotation, out var size) != PxrResult.SUCCESS) continue;
#region debug-point D:room-box-raw-pose
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.Log($"MATESOUL_ROOM_BOX id={anchor.Value} label={label} " +
                          $"anchorPosition={anchorPosition:F5} anchorRotation={anchorRotation:F5} " +
                          $"localPosition={localPosition:F5} localRotation={localRotation:F5} size={size:F5}");
#endif
#endregion
                var center = anchorPosition + anchorRotation * localPosition;
                // Room Capture can omit the optional Box3D rotation as (0,0,0,0).
                // Keep the anchor's orientation when no local rotation is supplied.
                localRotation = Quaternion.Dot(localRotation, localRotation) > .0001f
                    ? Quaternion.Normalize(localRotation) : Quaternion.identity;
                var rotation = anchorRotation * localRotation;
                if (definition.IsItem)
                    realityItems.Add(new RealityItemObservation(anchor.Value.ToString(), semantic,
                        center, rotation, size));
                if (!PicoMockRoomMapProvider.TryMapObstacleKind(label, out var kind)) continue;
                obstacles.Add(new RoomObstacle(
                    anchor.Value.ToString(),
                    kind,
                    center,
                    rotation,
                    size));
            }

            return new RoomMapSnapshot(
                $"pico-room-{DateTime.UtcNow:yyyyMMddHHmmss}",
                SourceName,
                floors,
                obstacles,
                realityItems: realityItems);
        }
    }
}
