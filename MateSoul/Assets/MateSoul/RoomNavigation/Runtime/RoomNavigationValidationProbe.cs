using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;

namespace MateSoul.RoomNavigation
{
    [DisallowMultipleComponent]
    public sealed class RoomNavigationValidationProbe : MonoBehaviour
    {
        [SerializeField]
        private RoomNavigationBootstrap bootstrap;

        [SerializeField, Min(2f)]
        private float validationDurationSeconds = 10f;

        public bool Finished { get; private set; }
        public bool Passed { get; private set; }

        public void Configure(RoomNavigationBootstrap target, float durationSeconds)
        {
            bootstrap = target;
            validationDurationSeconds = durationSeconds;
        }

        private IEnumerator Start()
        {
            var startupDeadline = Time.realtimeSinceStartup + 20f;
            while (bootstrap != null &&
                   bootstrap.State != RoomNavigationState.Ready &&
                   bootstrap.State != RoomNavigationState.Failed &&
                   Time.realtimeSinceStartup < startupDeadline)
            {
                yield return null;
            }

            if (bootstrap == null || bootstrap.State != RoomNavigationState.Ready)
            {
                Finish(false, bootstrap != null ? bootstrap.LastError : "Bootstrap reference is missing.");
                yield break;
            }

            var walker = bootstrap.Walker;
            var snapshot = bootstrap.Snapshot;
            var obstacleFootprints = new List<List<Vector3>>();
            if (snapshot.SurfaceMeshes.Count == 0)
            {
                foreach (var obstacle in snapshot.Obstacles)
                {
                    obstacleFootprints.Add(RoomGeometry.CreateObstacleFootprintXZ(obstacle));
                }
            }

            var outsideFloorFrames = 0;
            var obstaclePenetrationFrames = 0;
            var offNavMeshFrames = 0;
            var elapsed = 0f;
            while (elapsed < validationDurationSeconds)
            {
                var position = walker.transform.position;
                if (!RoomGeometry.ContainsPointXZ(snapshot.MainFloor.Vertices, position))
                {
                    outsideFloorFrames++;
                }

                foreach (var footprint in obstacleFootprints)
                {
                    if (RoomGeometry.ContainsPointXZ(footprint, position))
                    {
                        obstaclePenetrationFrames++;
                        break;
                    }
                }

                if (!NavMesh.SamplePosition(position, out _, 0.15f, NavMesh.AllAreas))
                {
                    offNavMeshFrames++;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            var validation = snapshot?.Validate() ??
                             RoomMapValidationResult.Invalid("Snapshot is missing.");
            var hasObstacleEvidence =
                snapshot.SurfaceMeshes.Count > 0 ||
                snapshot.Obstacles.Count >= 4;
            var passed = walker != null &&
                         walker.IsInitialized &&
                         validation.IsValid &&
                         hasObstacleEvidence &&
                         walker.DestinationsSelected > 0 &&
                         walker.TotalDistanceTravelled >= 0.5f &&
                         outsideFloorFrames == 0 &&
                         obstaclePenetrationFrames == 0 &&
                         offNavMeshFrames == 0;

            var error = passed
                ? string.Empty
                : "Navigation did not produce a valid room and measurable character movement.";
            Finish(
                passed,
                error,
                outsideFloorFrames,
                obstaclePenetrationFrames,
                offNavMeshFrames);
        }

        private void Finish(
            bool passed,
            string error,
            int outsideFloorFrames = 0,
            int obstaclePenetrationFrames = 0,
            int offNavMeshFrames = 0)
        {
            Finished = true;
            Passed = passed;

            var walker = bootstrap != null ? bootstrap.Walker : null;
            var snapshot = bootstrap != null ? bootstrap.Snapshot : null;
            var report = new ValidationReport
            {
                passed = passed,
                state = bootstrap != null ? bootstrap.State.ToString() : "Missing",
                source = snapshot != null ? snapshot.Source : string.Empty,
                floorAreaM2 = snapshot?.MainFloor != null ? snapshot.MainFloor.Area : 0f,
                obstacleCount = snapshot?.Obstacles.Count ?? 0,
                surfaceMeshCount = snapshot?.SurfaceMeshes.Count ?? 0,
                surfaceVertexCount = CountSurfaceVertices(snapshot),
                surfaceTriangleCount = CountSurfaceTriangles(snapshot),
                floorCentroid = snapshot?.MainFloor != null
                    ? snapshot.MainFloor.Centroid
                    : Vector3.zero,
                mapBoundsCenter = snapshot != null ? snapshot.Bounds.center : Vector3.zero,
                mapBoundsSize = snapshot != null ? snapshot.Bounds.size : Vector3.zero,
                finalCharacterPosition = walker != null
                    ? walker.transform.position
                    : Vector3.zero,
                destinationsSelected = walker != null ? walker.DestinationsSelected : 0,
                destinationsReached = walker != null ? walker.DestinationsReached : 0,
                distanceTravelledM = walker != null ? walker.TotalDistanceTravelled : 0f,
                outsideFloorFrames = outsideFloorFrames,
                obstaclePenetrationFrames = obstaclePenetrationFrames,
                offNavMeshFrames = offNavMeshFrames,
                error = error,
                unityVersion = Application.unityVersion
            };

            var json = JsonUtility.ToJson(report, true);
            var reportPath = GetReportPath();
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? string.Empty);
            File.WriteAllText(reportPath, json);

            Debug.Log(passed
                ? $"ROOM_NAVIGATION_PLAYMODE_PASS\n{json}"
                : $"ROOM_NAVIGATION_PLAYMODE_FAIL\n{json}");
        }

        private static string GetReportPath()
        {
#if UNITY_EDITOR
            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "../../项目沉淀资料/validation/room_navigation_v001.json"));
#else
            return Path.Combine(Application.persistentDataPath, "room_navigation_v001.json");
#endif
        }

        private static int CountSurfaceVertices(RoomMapSnapshot snapshot)
        {
            var count = 0;
            if (snapshot == null)
            {
                return count;
            }

            foreach (var surface in snapshot.SurfaceMeshes)
            {
                count += surface.Vertices.Count;
            }

            return count;
        }

        private static int CountSurfaceTriangles(RoomMapSnapshot snapshot)
        {
            var count = 0;
            if (snapshot == null)
            {
                return count;
            }

            foreach (var surface in snapshot.SurfaceMeshes)
            {
                count += surface.Indices.Count / 3;
            }

            return count;
        }

        [Serializable]
        private sealed class ValidationReport
        {
            public bool passed;
            public string state;
            public string source;
            public float floorAreaM2;
            public int obstacleCount;
            public int surfaceMeshCount;
            public int surfaceVertexCount;
            public int surfaceTriangleCount;
            public Vector3 floorCentroid;
            public Vector3 mapBoundsCenter;
            public Vector3 mapBoundsSize;
            public Vector3 finalCharacterPosition;
            public int destinationsSelected;
            public int destinationsReached;
            public float distanceTravelledM;
            public int outsideFloorFrames;
            public int obstaclePenetrationFrames;
            public int offNavMeshFrames;
            public string error;
            public string unityVersion;
        }
    }
}
