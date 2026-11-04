using System.Collections;
using System;
using System.Linq;
using System.Reflection;
using ByteDance.PICO.XR;
using MateSoul.RoomNavigation;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class RoomNavigationSurfaceMeshTests
    {
        [Test]
        public void SpatialMeshFloorKeepsMeasuredConcavity()
        {
            var vertices = new[]
            {
                new Vector3(0,0,0), new Vector3(4,0,0),
                new Vector3(4,0,1), new Vector3(0,0,1),
                new Vector3(0,0,1), new Vector3(1,0,1),
                new Vector3(1,0,4), new Vector3(0,0,4)
            };
            var block = new PxrSpatialMeshInfo
            {
                uuid = Guid.NewGuid(),
                state = MeshChangeState.Added,
                position = Vector3.zero,
                rotation = Quaternion.identity,
                vertices = vertices,
                indices = new ushort[] { 0,2,1, 0,3,2, 4,6,5, 4,7,6 },
                labels = Enumerable.Repeat(
                    PxrSemanticLabel.Floor, vertices.Length).ToArray()
            };
            var provider = new PicoSpatialMeshRoomMapProvider(TimeSpan.FromSeconds(1));
            var method = typeof(PicoSpatialMeshRoomMapProvider).GetMethod(
                "BuildSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);

            var snapshot = (RoomMapSnapshot)method.Invoke(
                provider, new object[] { new[] { block } });

            Assert.That(snapshot.Validate().IsValid, Is.True);
            Assert.That(RoomGeometry.ContainsPointXZ(
                snapshot.MainFloor.Vertices, new Vector3(.5f, 0, 3f)), Is.True);
            Assert.That(RoomGeometry.ContainsPointXZ(
                snapshot.MainFloor.Vertices, new Vector3(3f, 0, .5f)), Is.True);
            Assert.That(RoomGeometry.ContainsPointXZ(
                snapshot.MainFloor.Vertices, new Vector3(3f, 0, 3f)), Is.False,
                "A measured concavity must not be filled by a global convex hull.");
        }

        [Test]
        public void UnknownHorizontalGeometryAtTrackingFloorCanSeedMeasuredFloor()
        {
            var cameraRoot = new GameObject("Tracking floor");
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(cameraRoot.transform, false);
            cameraObject.transform.localPosition = Vector3.up * 1.65f;
            cameraObject.AddComponent<Camera>();
            try
            {
                var block = HorizontalBlock(0f, 1.5f, PxrSemanticLabel.Unknown);
                var provider = new PicoSpatialMeshRoomMapProvider(
                    TimeSpan.FromSeconds(1), trackingFloorYOverride: 0f);
                var method = typeof(PicoSpatialMeshRoomMapProvider).GetMethod(
                    "BuildSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);

                var snapshot = (RoomMapSnapshot)method.Invoke(
                    provider, new object[] { new[] { block } });

                Assert.That(snapshot.MainFloor, Is.Not.Null);
                Assert.That(snapshot.Validate(
                    PicoSpatialMeshRoomMapProvider.MinimumMeasuredFloorArea).IsValid, Is.True);
                Assert.That(snapshot.MainFloor.Centroid.y, Is.EqualTo(0).Within(.01f));
            }
            finally
            {
                Object.DestroyImmediate(cameraRoot);
            }
        }

        [Test]
        public void UnknownHorizontalGeometryAboveTrackingFloorIsNotWalkableFloor()
        {
            var cameraRoot = new GameObject("Tracking floor");
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(cameraRoot.transform, false);
            cameraObject.transform.localPosition = Vector3.up * 1.65f;
            cameraObject.AddComponent<Camera>();
            try
            {
                var block = HorizontalBlock(.75f, 2f, PxrSemanticLabel.Unknown);
                var provider = new PicoSpatialMeshRoomMapProvider(
                    TimeSpan.FromSeconds(1), trackingFloorYOverride: 0f);
                var method = typeof(PicoSpatialMeshRoomMapProvider).GetMethod(
                    "BuildSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);

                var snapshot = (RoomMapSnapshot)method.Invoke(
                    provider, new object[] { new[] { block } });

                Assert.That(snapshot.MainFloor, Is.Null,
                    "An unlabeled table-height surface must not become locomotion floor.");
            }
            finally
            {
                Object.DestroyImmediate(cameraRoot);
            }
        }

        [UnityTest]
        public IEnumerator SpawnRequiresMeasuredFloorSupportWhenLiveMeshExists()
        {
            yield return new EnterPlayMode();
            var root = new GameObject("Measured spawn support test");
            var liveObject = new GameObject("Measured floor");
            try
            {
                var room = root.AddComponent<RuntimeRoomNavigation>();
                room.Build(new RoomMapSnapshot(
                    "calibrated-floor",
                    "test",
                    new[]
                    {
                        new RoomFloorRegion("floor", new[]
                        {
                            new Vector3(-2,0,-2), new Vector3(-2,0,2),
                            new Vector3(2,0,2), new Vector3(2,0,-2)
                        })
                    },
                    Array.Empty<RoomObstacle>()), false);
                var measured = RoomGeometry.CreateHorizontalPolygonMesh(new[]
                {
                    new Vector3(-2,0,-2), new Vector3(-2,0,2),
                    new Vector3(0,0,2), new Vector3(0,0,-2)
                }, "Measured left floor");
                liveObject.AddComponent<MeshFilter>().sharedMesh = measured;
                liveObject.AddComponent<MeshCollider>().sharedMesh = measured;
                liveObject.AddComponent<RoomSurfaceCollider>().Bind(73);
                room.UpdateLiveSurface(
                    "left-floor", measured, liveObject.transform.localToWorldMatrix);
                Physics.SyncTransforms();

                Assert.That(room.TryFindSpawnPosition(
                    new Vector3(1.5f, 0, 0), out var spawn), Is.True);
                Assert.That(spawn.x, Is.LessThanOrEqualTo(.1f),
                    "Unsupported calibrated floor must not be selected over measured support.");
            }
            finally
            {
                Object.Destroy(root);
                Object.Destroy(liveObject);
            }
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator SpatialMeshUsesGroundedFurnitureAsNavigationObstacle()
        {
            yield return new EnterPlayMode();
            var root = new GameObject("Spatial obstacle navigation test");
            try
            {
                var room = root.AddComponent<RuntimeRoomNavigation>();
                room.Build(new RoomMapSnapshot(
                    "spatial-obstacles",
                    "test",
                    new[]
                    {
                        new RoomFloorRegion("floor", new[]
                        {
                            new Vector3(-2, 0, -2),
                            new Vector3(-2, 0, 2),
                            new Vector3(2, 0, 2),
                            new Vector3(2, 0, -2)
                        })
                    },
                    new[]
                    {
                        new RoomObstacle(
                            "bed", RoomObstacleKind.Bed,
                            new Vector3(0, .5f, 0),
                            Quaternion.identity,
                            Vector3.one)
                    },
                    new[]
                    {
                        new RoomSurfaceMesh(
                            "bed-top",
                            new[]
                            {
                                new Vector3(-.5f, 1, -.5f),
                                new Vector3(-.5f, 1, .5f),
                                new Vector3(.5f, 1, .5f),
                                new Vector3(.5f, 1, -.5f)
                            },
                            new[] { 0, 1, 2, 0, 2, 3 },
                            7)
                    }), false);

                var navigation = new RoomNavigationService(room);
                Assert.That(navigation.TryPath(
                    new Vector3(-1.5f, 0, 0),
                    new Vector3(1.5f, 0, 0),
                    out var corners), Is.True);
                Assert.That(corners, Has.Length.GreaterThan(2),
                    "The path must route around grounded furniture.");
            }
            finally
            {
                Object.Destroy(root);
            }
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator SpawnProbeDoesNotIgnoreSiblingRoomGeometry()
        {
            yield return new EnterPlayMode();
            var root = new GameObject("Spawn collision test");
            try
            {
                var character = new GameObject("Character");
                character.transform.SetParent(root.transform);
                var capsule = character.AddComponent<CapsuleCollider>();
                capsule.center = Vector3.up * .825f;
                capsule.height = 1.65f;
                capsule.radius = .25f;
                var obstacle = new GameObject("Room obstacle");
                obstacle.transform.SetParent(root.transform);
                obstacle.transform.position = Vector3.up * .5f;
                obstacle.AddComponent<BoxCollider>().size =
                    new Vector3(1, 1, 1);
                Physics.SyncTransforms();

                var method = typeof(RoomNavigationBootstrap).GetMethod(
                    "CanOccupySpawn",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);
                Assert.That((bool)method.Invoke(null, new object[]
                {
                    capsule,
                    new Pose(Vector3.up * .025f, Quaternion.identity)
                }), Is.False);
                Assert.That((bool)method.Invoke(null, new object[]
                {
                    capsule,
                    new Pose(new Vector3(2, .025f, 0), Quaternion.identity)
                }), Is.True);
            }
            finally
            {
                Object.Destroy(root);
            }
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator FurnitureSurfaceMeshDoesNotReplaceWalkableFloor()
        {
            yield return new EnterPlayMode();
            var root = new GameObject("Surface mesh navigation test");
            try
            {
                var room = root.AddComponent<RuntimeRoomNavigation>();
                room.Build(new RoomMapSnapshot(
                    "surface-navigation",
                    "test",
                    new[]
                    {
                        new RoomFloorRegion("floor", new[]
                        {
                            new Vector3(-2, 0, -2),
                            new Vector3(-2, 0, 2),
                            new Vector3(2, 0, 2),
                            new Vector3(2, 0, -2)
                        })
                    },
                    System.Array.Empty<RoomObstacle>(),
                    new[]
                    {
                        new RoomSurfaceMesh(
                            "furniture-top",
                            new[]
                            {
                                new Vector3(-.5f, .5f, -.5f),
                                new Vector3(-.5f, .5f, .5f),
                                new Vector3(.5f, .5f, .5f),
                                new Vector3(.5f, .5f, -.5f)
                            },
                            new[] { 0, 1, 2, 0, 2, 3 },
                            42)
                    }), false);

                Assert.That(room.TrySamplePosition(
                    Vector3.zero, .1f, out var sample), Is.True);
                Assert.That(sample.y, Is.EqualTo(0).Within(.08f),
                    "Furniture tops must not replace the locomotion floor.");
                var navigation = new RoomNavigationService(room);
                Assert.That(navigation.TryPath(
                    new Vector3(-1, 0, -1),
                    new Vector3(1, 0, 1),
                    out var corners), Is.True);
                Assert.That(corners, Has.Length.GreaterThan(1));
            }
            finally
            {
                Object.Destroy(root);
            }
            yield return null;
            yield return new ExitPlayMode();
        }

        private static PxrSpatialMeshInfo HorizontalBlock(
            float height,
            float size,
            PxrSemanticLabel label)
        {
            return new PxrSpatialMeshInfo
            {
                uuid = Guid.NewGuid(),
                state = MeshChangeState.Added,
                position = Vector3.zero,
                rotation = Quaternion.identity,
                vertices = new[]
                {
                    new Vector3(0, height, 0),
                    new Vector3(size, height, 0),
                    new Vector3(size, height, size),
                    new Vector3(0, height, size)
                },
                indices = new ushort[] { 0, 2, 1, 0, 3, 2 },
                labels = Enumerable.Repeat(label, 4).ToArray()
            };
        }
    }
}
