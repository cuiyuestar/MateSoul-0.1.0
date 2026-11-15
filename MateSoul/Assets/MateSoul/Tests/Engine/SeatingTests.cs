using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MateSoul.Gameplay;
using MateSoul.Core;
using MateSoul.Editor;
using MateSoul.RoomNavigation;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class SeatingTests
    {
        private static List<RealityTriangle> Chair(Vector3 origin, float yaw = 0, bool back = true, bool hole = false)
        {
            var triangles = new List<RealityTriangle>();
            var rotation = Quaternion.Euler(0, yaw, 0);
            Vector3 P(float x, float y, float z) => origin + rotation * new Vector3(x, y, z);
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            { triangles.Add(new RealityTriangle(a, b, c, true)); triangles.Add(new RealityTriangle(a, c, d, true)); }
            for (var x = 0; x < 4; x++)
                for (var z = 0; z < 4; z++)
                {
                    if (hole && x == 1 && z == 1) continue;
                    var x0 = -.24f + x * .12f; var z0 = -.22f + z * .11f;
                    Quad(P(x0, .45f, z0), P(x0 + .12f, .45f, z0),
                        P(x0 + .12f, .45f, z0 + .11f), P(x0, .45f, z0 + .11f));
                }
            if (back) Quad(P(-.24f, .5f, -.22f), P(.24f, .5f, -.22f),
                P(.24f, .9f, -.22f), P(-.24f, .9f, -.22f));
            return triangles;
        }

        private static List<RealityTriangle> Stool(Vector3 origin, float yaw = 0, bool hole = false)
        {
            var triangles = new List<RealityTriangle>();
            var rotation = Quaternion.Euler(0, yaw, 0);
            Vector3 P(float x, float z) => origin + rotation * new Vector3(x, .352f, z);
            for (var x = 0; x < 4; x++)
                for (var z = 0; z < 4; z++)
                {
                    if (hole && x == 1 && z == 1) continue;
                    var x0 = -.155f + x * .0775f; var z0 = -.155f + z * .0775f;
                    var a = P(x0, z0); var b = P(x0 + .0775f, z0);
                    var c = P(x0 + .0775f, z0 + .0775f); var d = P(x0, z0 + .0775f);
                    triangles.Add(new RealityTriangle(a, b, c, true));
                    triangles.Add(new RealityTriangle(a, c, d, true));
                }
            return triangles;
        }

        private static List<RealityTriangle> UnlabelledChair(Vector3 origin, bool back = true)
            => Chair(origin, back: back)
                .Select(triangle => new RealityTriangle(
                    triangle.A, triangle.B, triangle.C, false, 7))
                .ToList();

        [TestCase(0f)]
        [TestCase(37f)]
        [TestCase(90f)]
        [TestCase(173f)]
        public void FitsActualSurfaceAndBackrestUnderYaw(float yaw)
        {
            var origin = new Vector3(1.2f, .13f, -2.4f);
            var seats = SeatGeometry.Extract(Chair(origin, yaw), origin.y);
            Assert.That(seats.Count, Is.EqualTo(1));
            Assert.That(Vector3.Distance(seats[0].Center, origin + Vector3.up * .45f), Is.LessThan(.002f));
            Assert.That(Vector3.Angle(seats[0].Forward, Quaternion.Euler(0, yaw, 0) * Vector3.forward), Is.LessThan(1));
            Assert.That(seats[0].Width, Is.EqualTo(.48f).Within(.002f));
        }

        [Test]
        public void ChairsSharingSemanticCategoryRemainSeparate()
        {
            var mesh = Chair(Vector3.zero);
            mesh.AddRange(Chair(Vector3.right * 1.2f, 90));
            Assert.That(SeatGeometry.Extract(mesh, 0).Count, Is.EqualTo(2));
        }

        [Test]
        public void MissingCenterPatchIsNotBridged()
            => Assert.That(SeatGeometry.Extract(Chair(Vector3.zero, hole: true), 0), Is.Empty);

        [Test]
        public void NoBackrestMeansNoAutomaticFacingGuess()
            => Assert.That(SeatGeometry.Extract(Chair(Vector3.zero, back: false), 0), Is.Empty);

        [Test]
        public void CompactSemanticPatchWithoutBackrestIsRejected()
        {
            var geometry = Stool(Vector3.zero, 31);
            Assert.That(SeatGeometry.Extract(geometry, 0), Is.Empty);
        }

        [Test]
        public void CompactSemanticStoolWithMissingSupportIsRejected()
            => Assert.That(SeatGeometry.Extract(Stool(Vector3.zero, hole: true), 0), Is.Empty);

        [Test]
        public void UnlabelledChairWithCoherentUpperStructureIsDetected()
        {
            var seat = SeatGeometry.Extract(UnlabelledChair(Vector3.zero), 0).Single();
            Assert.That(seat.GeometryInferred, Is.True);
            Assert.That(seat.Backless, Is.False);
            Assert.That(seat.Width, Is.EqualTo(.48f).Within(.002f));
            Assert.That(seat.Depth, Is.EqualTo(.44f).Within(.002f));
        }

        [Test]
        public void UnlabelledTableWithoutUpperStructureIsRejected()
            => Assert.That(SeatGeometry.Extract(UnlabelledChair(Vector3.zero, false), 0), Is.Empty);

        [Test]
        public void TableHeightIsNotAcceptedAsASeat()
            => Assert.That(SeatGeometry.Extract(Chair(Vector3.up * .28f), 0), Is.Empty);

        [Test]
        public void UnlabeledObstacleBlocksSitSweep()
        {
            var mesh = Chair(Vector3.zero);
            var seat = SeatGeometry.Extract(mesh, 0).Single();
            Assert.That(SeatGeometry.HasClearance(seat, mesh), Is.True);
            mesh.Add(new RealityTriangle(new Vector3(-.3f, .8f, .35f), new Vector3(.3f, .8f, .35f),
                new Vector3(0, 1.4f, .35f), false));
            Assert.That(SeatGeometry.HasClearance(seat, mesh), Is.False);
        }

        [Test]
        public void TargetChairMeshIsNotTreatedAsAnExternalObstacle()
        {
            var mesh = UnlabelledChair(Vector3.zero);
            var seat = SeatGeometry.Extract(mesh, 0).Single();
            var contact = new RealityTriangle(
                new Vector3(-.3f, .8f, .35f), new Vector3(.3f, .8f, .35f),
                new Vector3(0, 1.4f, .35f), false, seat.SourceId);
            mesh.Add(contact);
            Assert.That(SeatGeometry.HasClearance(seat, mesh), Is.True);

            mesh.Add(new RealityTriangle(contact.A, contact.B, contact.C, false, seat.SourceId + 1));
            Assert.That(SeatGeometry.HasClearance(seat, mesh), Is.False);
        }

        [UnityTest]
        public IEnumerator TargetChairSurfaceIsIgnoredButExternalObstacleStillBlocks()
        {
            yield return new EnterPlayMode();
            yield return ExerciseTargetChairContactSweep();
            yield return new ExitPlayMode();
        }

        private IEnumerator ExerciseTargetChairContactSweep()
        {
            var roomObject = new GameObject("Seat contact sweep diagnostic room");
            var character = new GameObject("Seat contact sweep diagnostic avatar");
            character.AddComponent<Rigidbody>();
            character.AddComponent<CapsuleCollider>();
            var material = new PhysicsMaterial("Seat contact sweep diagnostic material");
            character.AddComponent<PhysicsBody>().Configure(new BodySettings(50, material));
            var animator = character.AddComponent<Animator>();
            var motor = character.AddComponent<AvatarMotor>();
            motor.Bind(animator);
            var geometry = UnlabelledChair(Vector3.zero);
            var seat = SeatGeometry.Extract(geometry, 0).Single();
            geometry.Add(new RealityTriangle(
                new Vector3(-.12f, .50f, -.18f), new Vector3(.12f, .50f, -.18f),
                new Vector3(.12f, .50f, .08f), (int)RealitySemantic.Unknown, seat.SourceId));
            geometry.Add(new RealityTriangle(
                new Vector3(-.12f, .50f, -.18f), new Vector3(.12f, .50f, .08f),
                new Vector3(-.12f, .50f, .08f), (int)RealitySemantic.Unknown, seat.SourceId));
            Assert.That(SeatGeometry.HasClearance(seat, geometry), Is.True);
            var fixture = PhysicsRuntimeTests.AddRealityCollider(geometry, roomObject.transform);
            roomObject.transform.Find("Captured fixture collision").gameObject
                .AddComponent<RoomSurfaceCollider>().Bind(seat.SourceId);
            var dock = seat.Dock + Vector3.up * .03f;
            motor.Body.Place(new Pose(dock, seat.Rotation));
            Physics.SyncTransforms();
            motor.BeginBodyContact(dock, seat.Center.y, seat.SourceId);
            var root = new Vector3(seat.Center.x, dock.y, seat.Center.z) -
                       seat.Forward * .079f;
            Assert.DoesNotThrow(() => motor.SetContactRoot(root, seat.Rotation));

            motor.Body.Place(new Pose(dock, seat.Rotation));
            var obstacle = new GameObject("External seat obstacle", typeof(BoxCollider));
            obstacle.transform.SetParent(roomObject.transform);
            obstacle.transform.position = root + Vector3.up * .85f;
            obstacle.GetComponent<BoxCollider>().size = new Vector3(.3f, .4f, .3f);
            Physics.SyncTransforms();
            Assert.Throws<InvalidOperationException>(() =>
                motor.SetContactRoot(root, seat.Rotation));
            Object.Destroy(character);
            Object.Destroy(roomObject);
            Object.Destroy(fixture);
            Object.Destroy(material);
            yield return null;
        }

        [Test]
        public void GeometryJumpInvalidatesReservationAndRequiresRestabilization()
        {
            using (var registry = new ItemRegistry())
            using (var directory = new RealityItemDirectory(registry))
            using (var scanner = new RealChairScanner(directory, Room()))
            {
                var now = Time.unscaledTime;
                scanner.Observe(Chair(Vector3.zero), now);
                scanner.Observe(Chair(Vector3.zero), now + .1f);
                Assert.That(registry.Count, Is.Zero);
                scanner.Observe(Chair(Vector3.zero), now + .2f);
                var seat = scanner.Targets.Single();
                using (var reservation = registry.Reserve(seat.Id, "sit", this))
                {
                    scanner.Observe(Chair(Vector3.right * .08f), now + .3f);
                    Assert.That(reservation.IsValid, Is.False);
                    Assert.That(registry.Count, Is.Zero);
                }
            }
        }

        [Test]
        public void MissingMeshRevokesAvailabilityWithoutInventingMovement()
        {
            using (var registry = new ItemRegistry())
            using (var directory = new RealityItemDirectory(registry))
            using (var scanner = new RealChairScanner(directory, Room()))
            {
                for (var i = 0; i < 3; i++) scanner.Observe(Chair(Vector3.zero), Time.unscaledTime);
                var locked = scanner.Targets.Single().Copy();
                // Two empty publishes are tolerated so a chair in the headset's blind
                // spot survives; the third one must revoke the seat.
                for (var i = 0; i < 3; i++)
                    scanner.Observe(new List<RealityTriangle>(), Time.unscaledTime);
                Assert.That(scanner.IsSafe(locked), Is.False);
                Assert.That(registry.Count, Is.Zero);
            }
        }

        [Test]
        public void StaticSemanticSnapshotRemainsAvailableUntilInvalidated()
        {
            using (var registry = new ItemRegistry())
            using (var directory = new RealityItemDirectory(registry))
            using (var scanner = new RealChairScanner(directory, Room()))
            {
                for (var i = 0; i < 3; i++) scanner.Observe(Chair(Vector3.zero), Time.unscaledTime + i);
                var target = scanner.Targets.Single();
                target.ObservedAt = -1000;
                Assert.That(scanner.Targets.Single(), Is.SameAs(target));
                scanner.Invalidate();
                Assert.That(scanner.Targets, Is.Empty);
            }
        }

        [Test]
        public void StaticSemanticSnapshotStabilizesFromCachedGeometry()
        {
            using (var registry = new ItemRegistry())
            using (var directory = new RealityItemDirectory(registry))
            using (var scanner = new RealChairScanner(directory, Room()))
            {
                var now = Time.unscaledTime;
                scanner.Observe(Chair(Vector3.zero), now);
                Assert.That(scanner.Targets, Is.Empty);
                scanner.Tick(now + .5f);
                Assert.That(scanner.Targets, Is.Empty);
                scanner.Tick(now + 1f);
                var target = scanner.Targets.Single();
                Assert.That(target.Backless, Is.False);
                scanner.Observe(Chair(Vector3.zero), now + 10f);
                Assert.That(scanner.Targets.Single(), Is.SameAs(target));
            }
        }

        [UnityTest]
        public IEnumerator WalkSitStandAndTargetLossReleaseBodyAndReservation()
        {
            yield return new EnterPlayMode();
            // Create captured locals after the play-mode domain reload.
            yield return ExerciseAction();
            yield return new ExitPlayMode();
        }

        private IEnumerator ExerciseAction()
        {
            var roomObject = new GameObject("Seat action regression room");
            var room = roomObject.AddComponent<RuntimeRoomNavigation>();
            room.Build(Room(), false);
            var character = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GameSceneBuilder.CharacterPath));
            character.transform.position = new Vector3(0, .03f, 1.9f);
            var motor = character.GetComponent<AvatarMotor>();
            motor.Bind(character.GetComponentInChildren<Animator>());
            var rig = character.GetComponent<AvatarSeatingRig>();
            var fixtureMesh = PhysicsRuntimeTests.AddRealityCollider(Chair(Vector3.zero), roomObject.transform);
            using (var registry = new ItemRegistry())
            using (var directory = new RealityItemDirectory(registry))
            using (var scanner = new RealChairScanner(directory, Room()))
            {
                var geometry = Chair(Vector3.zero);
                for (var i = 0; i < 3; i++) scanner.Observe(geometry, Time.unscaledTime);
                var seat = scanner.Targets.Single().Copy();
                var catalog = new ActionCatalog();
                catalog.Register(new ActionDefinition("sit", "sit", ControlChannels.FullBody | ControlChannels.Locomotion,
                    _ => new SitOnChairAction(scanner, registry, seat.Id, motor, new RoomNavigationService(room), rig)));
                using (var coordinator = new AvatarActionCoordinator(catalog))
                {
                    for (var cycle = 0; cycle < 2; cycle++)
                    {
                        var handle = coordinator.Submit(new AvatarIntent("sit", IntentSource.UI));
                        for (var frame = 0; frame < 450 && rig.Weight < .999f && !handle.IsFinished; frame++)
                        {
                            scanner.Observe(geometry, Time.unscaledTime);
                            coordinator.Tick(.02f);
                            yield return null;
                        }
                        Assert.That(handle.State, Is.EqualTo(ActionState.Running), handle.Error);
                        Assert.That(rig.Weight, Is.EqualTo(1).Within(.001f));
                        Assert.That(motor.Body.IsContact, Is.True);
                        Assert.That(character.GetComponent<CapsuleCollider>().enabled, Is.True);
                        Assert.That(motor.Body.CanOccupy(motor.Body.Pose), Is.True);
                        yield return null;
                        Assert.That(Vector3.Distance(rig.PelvisPosition,
                            seat.Center + seat.Forward * .015f + Vector3.up * rig.ContactOffset), Is.LessThan(.003f));
                        Assert.That(rig.LeftFootPosition.y, Is.EqualTo(.105f).Within(.005f));
                        Assert.That(rig.RightFootPosition.y, Is.EqualTo(.105f).Within(.005f));
                        Assert.That(Quaternion.Angle(character.transform.rotation, seat.Rotation), Is.LessThan(1));
                        var settledSpine = rig.SpineRotation;
                        for (var frame = 0; frame < 30; frame++)
                        {
                            scanner.Observe(geometry, Time.unscaledTime);
                            coordinator.Tick(.02f);
                            yield return null;
                        }
                        Assert.That(Quaternion.Angle(rig.SpineRotation, settledSpine), Is.LessThan(.1f),
                            "The seated spine pose must not accumulate lean over subsequent frames.");
                        if (cycle == 0) coordinator.Cancel(handle);
                        else scanner.Invalidate();
                        for (var frame = 0; frame < 110 && !handle.IsFinished; frame++)
                        {
                            if (cycle == 0) scanner.Observe(geometry, Time.unscaledTime);
                            coordinator.Tick(.02f);
                            yield return null;
                        }
                        Assert.That(handle.State, Is.EqualTo(cycle == 0 ? ActionState.Cancelled : ActionState.Failed), handle.Error);
                        Assert.That(rig.Weight, Is.Zero);
                        Assert.That(character.GetComponent<CapsuleCollider>().enabled, Is.True);
                        Assert.That(motor.Body.Rigidbody.isKinematic, Is.False);
                        Assert.That(coordinator.Leases.IsOccupied(ControlChannels.FullBody), Is.False);
                        Assert.That(Vector3.ProjectOnPlane(character.transform.position - seat.Dock, Vector3.up).magnitude,
                            Is.LessThan(.10f));
                    }
                }
            }
            Object.Destroy(character); Object.Destroy(roomObject); Object.Destroy(fixtureMesh);
        }

        private static RoomMapSnapshot Room() => new RoomMapSnapshot("test", "fixture", new[]
        {
            new RoomFloorRegion("floor", new[] { new Vector3(-3,0,-3), new Vector3(-3,0,3),
                new Vector3(3,0,3), new Vector3(3,0,-3) })
        }, Array.Empty<RoomObstacle>());
    }
}
