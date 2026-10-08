using System;
using System.Collections.Generic;
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
        private readonly TimeSpan timeout;
        private readonly List<XRMeshSubsystem> meshSubsystems = new List<XRMeshSubsystem>();

        public PicoSpatialMeshRoomMapProvider(TimeSpan timeout)
        {
            this.timeout = timeout;
        }

        public string SourceName => "PICO Emulator Spatial Mesh";

        public async Task<RoomMapSnapshot> LoadAsync(CancellationToken cancellationToken)
        {
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
                foreach (var subsystem in meshSubsystems)
                {
                    if (!subsystem.running)
                    {
                        subsystem.Start();
                    }
                }

                var lastResult = PxrResult.ERROR_RUNTIME_FAILURE;
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var queryResult = await PXR_MixedReality.QueryMeshAnchorAsync(cancellationToken);
                    lastResult = queryResult.result;
                    if (queryResult.result == PxrResult.SUCCESS && queryResult.meshInfos.Count > 0)
                    {
                        var snapshot = BuildSnapshot(queryResult.meshInfos);
                        var validation = snapshot.Validate();
                        if (validation.IsValid)
                        {
                            Debug.Log(
                                $"[RoomNavigation] Spatial mesh snapshot blocks=" +
                                $"{snapshot.SurfaceMeshes.Count} floorArea={validation.FloorArea:F2}m2 " +
                                $"floorY={snapshot.MainFloor.Centroid.y:F3} bounds={snapshot.Bounds}");
                            return snapshot;
                        }
                    }

                    await Task.Delay(500, cancellationToken);
                }
                while (DateTime.UtcNow < deadline);

                throw new InvalidOperationException(
                    $"PICO Emulator Spatial Mesh was not navigation-ready within " +
                    $"{timeout.TotalSeconds:F0}s. Last query result: {lastResult}.");
            }
            finally
            {
                foreach (var subsystem in meshSubsystems)
                {
                    if (subsystem.running)
                    {
                        subsystem.Stop();
                    }
                }
            }
        }

        internal RoomMapSnapshot BuildSnapshot(IReadOnlyList<PxrSpatialMeshInfo> meshInfos)
        {
            var floorPoints = new List<Vector3>();
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
                        floorPoints.Add(worldVertex);
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

            var floorHeight = 0f;
            foreach (var point in floorPoints)
            {
                floorHeight += point.y;
            }

            floorHeight = floorPoints.Count > 0 ? floorHeight / floorPoints.Count : 0f;
            var floorHull = RoomGeometry.ConvexHullXZ(floorPoints, floorHeight);
            var floors = floorHull.Count >= 3
                ? new[] { new RoomFloorRegion("spatial-mesh-floor", floorHull) }
                : Array.Empty<RoomFloorRegion>();

            return new RoomMapSnapshot(
                $"pico-spatial-mesh-{DateTime.UtcNow:yyyyMMddHHmmss}",
                SourceName,
                floors,
                obstacles,
                surfaceMeshes,
                realityItems);
        }

        private static Vector3 MinimumSize(Vector3 size)
            => new Vector3(Mathf.Max(size.x, .05f), Mathf.Max(size.y, .10f),
                Mathf.Max(size.z, .05f));

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
        private readonly bool requestCaptureWhenMissing;

        public PicoSceneCaptureRoomMapProvider(bool requestCaptureWhenMissing)
        {
            this.requestCaptureWhenMissing = requestCaptureWhenMissing;
        }

        public string SourceName => "PICO Room Capture";

        public async Task<RoomMapSnapshot> LoadAsync(CancellationToken cancellationToken)
        {
            var providerStarted = false;
            try
            {
                var startResult = await PXR_MixedReality.StartSenseDataProvider(
                    PxrSenseDataProviderType.SceneCapture,
                    cancellationToken);
                if (startResult != PxrResult.SUCCESS)
                {
                    throw new InvalidOperationException(
                        $"Scene Capture provider failed to start: {startResult}.");
                }

                providerStarted = true;
                var queryResult = await PXR_MixedReality.QuerySceneAnchorAsync(cancellationToken);
                if (queryResult.result != PxrResult.SUCCESS)
                {
                    throw new InvalidOperationException(
                        $"Scene Anchor query failed: {queryResult.result}.");
                }

                if (queryResult.anchorDictionary.Count == 0 && requestCaptureWhenMissing)
                {
                    var captureResult = await PXR_MixedReality.StartSceneCaptureAsync(cancellationToken);
                    if (captureResult != PxrResult.SUCCESS)
                    {
                        throw new InvalidOperationException(
                            $"Room Capture did not complete successfully: {captureResult}.");
                    }

                    queryResult = await PXR_MixedReality.QuerySceneAnchorAsync(cancellationToken);
                    if (queryResult.result != PxrResult.SUCCESS)
                    {
                        throw new InvalidOperationException(
                            $"Scene Anchor query after Room Capture failed: {queryResult.result}.");
                    }
                }

                if (queryResult.anchorDictionary.Count == 0)
                {
                    throw new InvalidOperationException("Room Capture returned no Scene Anchors.");
                }

                return BuildSnapshot(queryResult.anchorDictionary, cancellationToken);
            }
            finally
            {
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
                var center = anchorPosition + anchorRotation * localPosition;
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
