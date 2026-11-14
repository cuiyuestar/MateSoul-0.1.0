using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        public const float CharacterRadius = 0.18f;
        public const float NavigationRadius = 0.16f;
        public const float CharacterHeight = 1.65f;
        public const float NavigationObstacleInset = 0.06f;

        [SerializeField, Min(0.1f)]
        private float sampleDistance = 0.75f;

        private readonly List<UnityEngine.Object> ownedObjects = new List<UnityEngine.Object>();
        private NavMeshDataInstance navMeshInstance;
        private GameObject geometryRoot;
        private BoxCollider measuredGroundSupport;
        private Bounds measuredGroundBounds;
        private NavMeshData navMeshData;
        private NavMeshBuildSettings buildSettings;
        private Bounds buildBounds;
        private readonly List<NavMeshBuildSource> stableFloorSources = new List<NavMeshBuildSource>();
        private readonly Dictionary<RoomObstacle, NavMeshBuildSource> calibratedObstacles =
            new Dictionary<RoomObstacle, NavMeshBuildSource>();
        private readonly Dictionary<string, LiveRoomSurface> liveSurfaces =
            new Dictionary<string, LiveRoomSurface>(StringComparer.Ordinal);
        private readonly RaycastHit[] supportHits = new RaycastHit[24];
        private AsyncOperation liveBuild;
        private bool liveDirty;
        private float nextLiveBuild;
        private float nextPerfLog;
        private Stopwatch liveBuildWatch;
#region debug-point A-C:spawn-probe-rate
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private float nextSpawnProbeReport;
#endif
#endregion
        public int LiveRevision { get; private set; }
        public int LiveObstacleCells { get; private set; }
        public int RetiredCalibratedObstacles { get; private set; }

        public RoomMapSnapshot Snapshot { get; private set; }
        public bool IsReady => navMeshInstance.valid && Snapshot != null;
        public long LastBuildMilliseconds { get; private set; }

        public void Build(RoomMapSnapshot snapshot, bool showDebugGeometry)
        {
            Clear();

            var validation = PicoSpatialMeshRoomMapProvider.IsSource(snapshot?.Source)
                ? PicoSpatialMeshRoomMapProvider.ValidateSnapshot(snapshot)
                : snapshot?.Validate() ??
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
            var surfaceSourceCount = snapshot.SurfaceMeshes.Count;
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
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.Log(
                    $"[RoomNavigation] obstacle id={obstacle.Id} kind={obstacle.Kind} " +
                    $"center={obstacle.Center} rotation={obstacle.Rotation.eulerAngles} " +
                    $"size={obstacle.Size} floorY={snapshot.MainFloor.Centroid.y:F3} " +
                    $"source={snapshot.Source}");
#endif
                if (usesSurfaceMeshes &&
                    !IsReliableSpatialObstacle(
                        obstacle, snapshot.MainFloor.Centroid.y))
                    continue;
                // NavMesh already erodes walkable space by agentRadius. Inset the
                // coarse semantic volume; live Spatial Mesh owns physical collision.
                var size = obstacle.Size;
                size.x = Mathf.Max(.05f,
                    size.x - NavigationObstacleInset * 2f);
                size.z = Mathf.Max(.05f,
                    size.z - NavigationObstacleInset * 2f);
                var obstacleSource = new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(obstacle.Center, obstacle.Rotation, Vector3.one),
                    size = size,
                    area = notWalkableArea
                };
                obstacleSources.Add(obstacleSource);
                calibratedObstacles.Add(obstacle, obstacleSource);
                CreateObstacleGeometry(obstacle, size, showDebugGeometry);
            }

            var settings = NavMesh.GetSettingsByIndex(0);
            settings.agentRadius = NavigationRadius;
            settings.agentHeight = CharacterHeight;
            settings.agentClimb = 0.2f;
            settings.agentSlope = 45f;
            // A small room can have a safe standing island after agent-radius erosion.
            // Keep it regardless of whether it crosses a voxel tile boundary.
            settings.minRegionArea = 0f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = 0.02f;
            settings.overrideTileSize = true;
            settings.tileSize = 64;
            var bounds = snapshot.Bounds;
            bounds.Expand(new Vector3(2f, 4f, 2f));
            stableFloorSources.AddRange(floorSources);
            buildSettings = settings;
            buildBounds = bounds;
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
                    $"obstacleSources={obstacleSources.Count} surfaceSources={surfaceSourceCount} " +
                    $"source={snapshot.Source} floorArea={validation.FloorArea:F2}m2 " +
                    $"floorCenter={snapshot.MainFloor.Centroid} bounds={bounds} " +
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

        public void SetLiveSurfaceSources(ISet<int> sourceIds)
        {
            // Live Spatial Mesh owns detailed reality collision. The calibrated
            // floor stays collidable across missing or updating live mesh blocks.
        }

        public void UpdateLiveSurface(string id, Mesh mesh, Matrix4x4 world)
        {
            if (!IsReady) return;
            ExpandMeasuredGroundSupport(mesh.bounds, world);
            var watch = Stopwatch.StartNew();
            var surface = new LiveRoomSurface(
                mesh, world, Snapshot.MainFloor.Centroid.y, Snapshot.Bounds);
            var projectMs = watch.Elapsed.TotalMilliseconds;
            if (projectMs >= 2f && Time.unscaledTime >= nextPerfLog)
            {
                nextPerfLog = Time.unscaledTime + 1f;
                Debug.Log($"MATESOUL_PERF_NAV_PROJECT block={id} ms={projectMs:F1} " +
                          $"cells={surface.Occupied.Count} frame={Time.frameCount}");
            }
            if (liveSurfaces.TryGetValue(id, out var previous) &&
                previous.Occupied.SetEquals(surface.Occupied) &&
                previous.Covered.SetEquals(surface.Covered)) return;
            liveSurfaces[id] = surface;
            liveDirty = true;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.Log($"MATESOUL_LIVE_NAV block={id} cells={liveSurfaces[id].Occupied.Count} " +
                      $"projectMs={watch.Elapsed.TotalMilliseconds:F2}");
#endif
        }

        public void RemoveLiveSurface(string id)
        {
            if (liveSurfaces.Remove(id)) liveDirty = true;
        }

        public void ClearLiveSurfaces()
        {
            if (liveSurfaces.Count == 0) return;
            liveSurfaces.Clear();
            liveDirty = true;
        }

        private void Update()
        {
            if (liveBuild != null)
            {
                if (!liveBuild.isDone) return;
                liveBuild = null;
                liveBuildWatch.Stop();
                LiveRevision++;
                // NavMeshBuilder.UpdateNavMeshDataAsync finishes on the main thread; a
                // long build shows up as a frame spike even though it started off-thread.
                var asyncMs = liveBuildWatch.Elapsed.TotalMilliseconds;
                if (asyncMs >= 4f)
                {
                    Debug.Log($"MATESOUL_PERF_NAV_ASYNC revision={LiveRevision} " +
                              $"asyncMs={asyncMs:F1} cells={LiveObstacleCells} " +
                              $"retired={RetiredCalibratedObstacles} frame={Time.frameCount}");
                }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.Log($"MATESOUL_LIVE_NAV revision={LiveRevision} " +
                          $"cells={LiveObstacleCells} retired={RetiredCalibratedObstacles} " +
                          $"asyncMs={liveBuildWatch.Elapsed.TotalMilliseconds:F2}");
#endif
            }
            if (!IsReady || !liveDirty || Time.unscaledTime < nextLiveBuild) return;
            var sourcesWatch = Stopwatch.StartNew();
            var occupied = new HashSet<Vector2Int>();
            var covered = new HashSet<Vector2Int>();
            foreach (var surface in liveSurfaces.Values)
            {
                occupied.UnionWith(surface.Occupied);
                covered.UnionWith(surface.Covered);
            }
            var sources = new List<NavMeshBuildSource>(stableFloorSources);
            RetiredCalibratedObstacles = 0;
            foreach (var pair in calibratedObstacles)
            {
                if (CanReplaceCalibration(pair.Key, covered))
                {
                    RetiredCalibratedObstacles++;
                    continue;
                }
                sources.Add(pair.Value);
            }
            // Stable row order and merged adjacent cells keep source hashing useful.
            foreach (var row in occupied.GroupBy(cell => cell.y).OrderBy(row => row.Key))
            {
                var xs = row.Select(cell => cell.x).OrderBy(x => x).ToArray();
                for (var i = 0; i < xs.Length; i++)
                {
                    var first = xs[i];
                    var last = first;
                    while (i + 1 < xs.Length && xs[i + 1] == last + 1) last = xs[++i];
                    var cellSize = LiveRoomSurface.CellSize;
                    sources.Add(new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Box,
                        transform = Matrix4x4.TRS(new Vector3(
                            (first + last + 1) * cellSize * .5f,
                            Snapshot.MainFloor.Centroid.y + CharacterHeight * .5f,
                            (row.Key + .5f) * cellSize), Quaternion.identity, Vector3.one),
                        size = new Vector3((last - first + 1) * cellSize, CharacterHeight, cellSize),
                        area = NavMesh.GetAreaFromName("Not Walkable")
                    });
                }
            }
            LiveObstacleCells = occupied.Count;
            liveDirty = false;
            nextLiveBuild = Time.unscaledTime + .25f;
            var sourcesMs = sourcesWatch.Elapsed.TotalMilliseconds;
            if (sourcesMs >= 3f && Time.unscaledTime >= nextPerfLog)
            {
                nextPerfLog = Time.unscaledTime + 1f;
                Debug.Log($"MATESOUL_PERF_NAV_SOURCES ms={sourcesMs:F1} " +
                          $"cells={occupied.Count} sources={sources.Count} " +
                          $"surfaces={liveSurfaces.Count} frame={Time.frameCount}");
            }
            liveBuildWatch = Stopwatch.StartNew();
            liveBuild = NavMeshBuilder.UpdateNavMeshDataAsync(
                navMeshData, buildSettings, sources, buildBounds);
        }

        private void ExpandMeasuredGroundSupport(Bounds localBounds, Matrix4x4 world)
        {
            if (measuredGroundSupport == null) return;
            // Retain observed coverage during block replacement. Only a room
            // rebuild resets it; navigation continues using its original floor.
            for (var corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3(
                    (corner & 1) == 0 ? -localBounds.extents.x : localBounds.extents.x,
                    (corner & 2) == 0 ? -localBounds.extents.y : localBounds.extents.y,
                    (corner & 4) == 0 ? -localBounds.extents.z : localBounds.extents.z);
                measuredGroundBounds.Encapsulate(
                    world.MultiplyPoint3x4(localBounds.center + offset));
            }
            measuredGroundSupport.transform.position = new Vector3(
                measuredGroundBounds.center.x, Snapshot.MainFloor.Centroid.y - .05f,
                measuredGroundBounds.center.z);
            measuredGroundSupport.size = new Vector3(
                measuredGroundBounds.size.x, .1f, measuredGroundBounds.size.z);
        }

        private static bool CanReplaceCalibration(RoomObstacle obstacle, ISet<Vector2Int> covered)
        {
            if (obstacle.Kind == RoomObstacleKind.Wall ||
                obstacle.Kind == RoomObstacleKind.VirtualWall) return false;
            var footprint = RoomGeometry.CreateObstacleFootprintXZ(obstacle);
            var min = footprint.Aggregate(Vector3.Min);
            var max = footprint.Aggregate(Vector3.Max);
            var samples = 0;
            var measured = 0;
            for (var x = Mathf.FloorToInt(min.x / LiveRoomSurface.CellSize);
                 x <= Mathf.FloorToInt(max.x / LiveRoomSurface.CellSize); x++)
                for (var z = Mathf.FloorToInt(min.z / LiveRoomSurface.CellSize);
                     z <= Mathf.FloorToInt(max.z / LiveRoomSurface.CellSize); z++)
                {
                    var cell = new Vector2Int(x, z);
                    var point = new Vector3((x + .5f) * LiveRoomSurface.CellSize, 0,
                        (z + .5f) * LiveRoomSurface.CellSize);
                    if (!RoomGeometry.ContainsPointXZ(footprint, point)) continue;
                    samples++;
                    if (covered.Contains(cell)) measured++;
                }
            return samples > 0 && measured == samples;
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

        public bool TryFindSpawnPosition(Vector3 desiredPosition, out Vector3 spawnPosition)
        {
            spawnPosition = default;
            if (!IsReady) return false;
            Physics.SyncTransforms();
            var best = Vector3.zero;
            var bestDistance = float.PositiveInfinity;
#region debug-point A-C:spawn-rejection
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var candidateCount = 0;
            var noSupportCount = 0;
            var outsideCount = 0;
            var overlapCount = 0;
            var blockingCollider = "";
#endif
#endregion
            var searchDistance = Vector3.Distance(desiredPosition, Snapshot.Bounds.center) +
                                 Snapshot.Bounds.extents.magnitude;
            if (TrySamplePosition(desiredPosition, searchDistance, out var nearest))
                Consider(nearest);
            var mesh = NavMesh.CalculateTriangulation();
            for (var i = 0; i < mesh.indices.Length; i += 3)
                Consider((mesh.vertices[mesh.indices[i]] +
                          mesh.vertices[mesh.indices[i + 1]] +
                          mesh.vertices[mesh.indices[i + 2]]) / 3f);
#region debug-point A-C:spawn-rejection-result
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Time.realtimeSinceStartup >= nextSpawnProbeReport ||
                !float.IsPositiveInfinity(bestDistance))
            {
                nextSpawnProbeReport = Time.realtimeSinceStartup + 1f;
                Debug.Log($"MATESOUL_SPAWN_PROBE run=post-fix candidates={candidateCount} " +
                    $"noSupport={noSupportCount} outside={outsideCount} overlap={overlapCount} " +
                    $"liveBlocks={liveSurfaces.Count} navVertices={mesh.vertices.Length} " +
                    $"blocker={blockingCollider} found={!float.IsPositiveInfinity(bestDistance)}");
            }
#endif
#endregion
            if (float.IsPositiveInfinity(bestDistance)) return false;
            spawnPosition = best;
            return true;

            void Consider(Vector3 point)
            {
#region debug-point A-C:spawn-candidate-count
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                candidateCount++;
#endif
#endregion
                if (liveSurfaces.Count > 0)
                {
                    if (!TryGetMeasuredFloorHeight(point, out var measuredY))
                    {
#region debug-point A:missing-measured-support
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        noSupportCount++;
#endif
#endregion
                        return;
                    }
                    // The persistent floor collider remains present between
                    // mesh updates. Spawn above both supports when measurement
                    // is a few centimetres below its calibrated top.
                    point.y = Mathf.Max(measuredY, Snapshot.MainFloor.Centroid.y);
                }
                var distance = (point - desiredPosition).sqrMagnitude;
                if (distance >= bestDistance) return;
                if (!RoomGeometry.ContainsPointXZ(Snapshot.MainFloor.Vertices, point))
                {
#region debug-point C:spawn-outside-floor
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    outsideCount++;
#endif
#endregion
                    return;
                }
                var lower = point + Vector3.up * (CharacterRadius + .03f);
                var upper = point + Vector3.up * (CharacterHeight - CharacterRadius + .03f);
                foreach (var collider in Physics.OverlapCapsule(
                             lower, upper, CharacterRadius, ~0, QueryTriggerInteraction.Ignore))
                    if (collider.transform.IsChildOf(geometryRoot.transform) ||
                        collider.GetComponent<RoomSurfaceCollider>() != null)
                    {
#region debug-point B:spawn-blocking-collider
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        overlapCount++;
                        blockingCollider = $"{collider.name}:{collider.bounds}";
#endif
#endregion
                        return;
                    }
                best = point;
                bestDistance = distance;
            }
        }

        private bool TryGetMeasuredFloorHeight(
            Vector3 point, out float floorHeight)
        {
            floorHeight = 0;
            var count = Physics.RaycastNonAlloc(
                point + Vector3.up * .35f,
                Vector3.down,
                supportHits,
                .7f,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            if (count == supportHits.Length) return false;
            var bestDistance = float.PositiveInfinity;
            for (var index = 0; index < count; index++)
            {
                var hit = supportHits[index];
                if (hit.collider == null ||
                    hit.normal.y < .7f ||
                    hit.collider.GetComponent<RoomSurfaceCollider>() == null ||
                    Mathf.Abs(hit.point.y - point.y) > .15f ||
                    hit.distance >= bestDistance)
                    continue;
                bestDistance = hit.distance;
                floorHeight = hit.point.y;
            }
            return !float.IsPositiveInfinity(bestDistance);
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
            if (navMeshData != null) NavMeshBuilder.Cancel(navMeshData);
            liveBuild = null;
            navMeshData = null;
            liveSurfaces.Clear();
            stableFloorSources.Clear();
            calibratedObstacles.Clear();
            liveDirty = false;
            LiveRevision = LiveObstacleCells = RetiredCalibratedObstacles = 0;
            RemoveNavMeshInstance();

            if (geometryRoot != null)
            {
                DestroyOwnedObject(geometryRoot);
                geometryRoot = null;
            }

            measuredGroundSupport = null;
            measuredGroundBounds = default;
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
            navMeshData = NavMeshBuilder.BuildNavMeshData(
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
            floorObject.layer = LayerMask.NameToLayer("RealitySurface");
            floorObject.AddComponent<MeshCollider>().sharedMesh = floorMesh;
            if (PicoSpatialMeshRoomMapProvider.IsSource(Snapshot.Source) &&
                ReferenceEquals(floor, Snapshot.MainFloor))
            {
                // Navigation selects a connected visible floor patch, often much
                // smaller than the measured room because furniture hides the floor.
                // Support dropped items across the measured room footprint at the
                // measured floor height. This finite base has no renderer or NavMesh.
                measuredGroundBounds = Snapshot.Bounds;
                var support = new GameObject("Measured Room Ground Support");
                support.transform.SetParent(floorObject.transform, false);
                support.layer = floorObject.layer;
                support.transform.position = new Vector3(
                    measuredGroundBounds.center.x, floor.Centroid.y - .05f,
                    measuredGroundBounds.center.z);
                measuredGroundSupport = support.AddComponent<BoxCollider>();
                measuredGroundSupport.size = new Vector3(
                    measuredGroundBounds.size.x, .1f, measuredGroundBounds.size.z);
            }

            if (!showDebugGeometry)
            {
                return;
            }

            var renderer = floorObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateMaterial(
                new Color(0.18f, 0.42f, 0.48f, 1f),
                $"FloorMaterial_{floor.Id}");
        }

        private void CreateObstacleGeometry(
            RoomObstacle obstacle,
            Vector3 navigationSize,
            bool showDebugGeometry)
        {
            if (!showDebugGeometry) return;
            var obstacleObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var collider = obstacleObject.GetComponent<Collider>();
            if (collider != null) DestroyOwnedObject(collider);
            var renderer = obstacleObject.GetComponent<MeshRenderer>();
            var isWall = obstacle.Kind == RoomObstacleKind.Wall ||
                         obstacle.Kind == RoomObstacleKind.VirtualWall;
            renderer.sharedMaterial = CreateMaterial(
                GetObstacleColor(obstacle.Kind),
                $"ObstacleMaterial_{obstacle.Id}",
                isWall);

            obstacleObject.name = $"{obstacle.Kind}_{obstacle.Id}";
            obstacleObject.transform.SetParent(geometryRoot.transform, false);
            obstacleObject.transform.SetPositionAndRotation(obstacle.Center, obstacle.Rotation);
            obstacleObject.transform.localScale = navigationSize;
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
