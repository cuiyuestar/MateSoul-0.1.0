#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using MateSoul.RoomNavigation;
using UnityEngine;
using UnityEngine.AI;

namespace MateSoul.Runtime
{
    // Passive development-player check. Never submits actions or changes gameplay state.
    public sealed class DevelopmentNavigationProbe : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return;
            var game = FindObjectOfType<GameBootstrap>();
            if (game != null) game.gameObject.AddComponent<DevelopmentNavigationProbe>();
        }

        private IEnumerator Start()
        {
            var game = GetComponent<GameBootstrap>();
            var deadline = Time.realtimeSinceStartup + 45;
            while (game.Session == null && Time.realtimeSinceStartup < deadline) yield return null;
            var report = new Report();
            if (game.Session == null)
            {
                report.error = game.Status;
                Finish(report);
                yield break;
            }
            var motor = (AvatarMotor)game.Session.Motor;
            var animator = motor.GetComponentInChildren<Animator>();
            var geometry = game.Room.transform.Find("GeneratedRoomGeometry");
            var previous = motor.Position;
            var moving = false;
            var wallDeadline = Time.realtimeSinceStartup + 180;
            report.source = game.Room.Snapshot.Source;
            report.meshBlocks = game.Room.Snapshot.SurfaceMeshes.Count;
            report.floorAreaM2 = game.Room.Snapshot.MainFloor.Area;
            report.navMeshBuildMs = game.Room.LastBuildMilliseconds;
            report.start = previous;
            while (report.gameSeconds < 30 && Time.realtimeSinceStartup < wallDeadline)
            {
                yield return null;
                if (game.Session.Paused) continue;
                var position = motor.Position;
                report.frames++;
                report.gameSeconds += Time.deltaTime;
                report.distanceM += Vector3.ProjectOnPlane(position - previous, Vector3.up).magnitude;
                report.maxAnimatorSpeed = Mathf.Max(report.maxAnimatorSpeed, animator.GetFloat("Speed"));
                if (motor.IsMoving && !moving) report.movementStarts++;
                moving = motor.IsMoving;
                previous = position;
                if (!RoomGeometry.ContainsPointXZ(game.Room.Snapshot.MainFloor.Vertices, position))
                    report.outsideFloorFrames++;
                if (!NavMesh.SamplePosition(position, out var hit, .2f, NavMesh.AllAreas) ||
                    Vector3.ProjectOnPlane(hit.position - position, Vector3.up).magnitude > .08f)
                    report.offNavMeshFrames++;
                // Shrink the body by 5 cm; floor contact and the controller's skin are not penetration.
                var radius = RuntimeRoomNavigation.CharacterRadius - .05f;
                var lower = position + Vector3.up * (radius + .06f);
                var upper = position + Vector3.up * (RuntimeRoomNavigation.CharacterHeight - radius);
                foreach (var collider in Physics.OverlapCapsule(lower, upper, radius, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (!collider.transform.IsChildOf(geometry)) continue;
                    report.roomPenetrationFrames++;
                    break;
                }
            }
            report.end = motor.Position;
            report.passed = report.gameSeconds >= 30 && report.distanceM >= .5f &&
                report.movementStarts > 0 && report.maxAnimatorSpeed > .1f &&
                report.outsideFloorFrames == 0 && report.offNavMeshFrames == 0 && report.roomPenetrationFrames == 0;
            if (!report.passed) report.error = "Movement or room-boundary invariant failed; inspect report values.";
            Finish(report);
        }

        private void Finish(Report report)
        {
            var json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(Application.persistentDataPath, "framework-navigation.json"), json);
            Debug.Log($"MATESOUL_NAVIGATION_{(report.passed ? "PASS" : "FAIL")}\n{json}");
            Destroy(this);
        }

        [Serializable]
        private sealed class Report
        {
            public bool passed;
            public string source, error;
            public int meshBlocks, frames, movementStarts, outsideFloorFrames, offNavMeshFrames, roomPenetrationFrames;
            public long navMeshBuildMs;
            public float floorAreaM2, gameSeconds, distanceM, maxAnimatorSpeed;
            public Vector3 start, end;
        }
    }
}
#endif
