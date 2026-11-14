using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MateSoul.Core;
using MateSoul.Editor;
using MateSoul.Gameplay;
using MateSoul.RoomNavigation;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class BedInteractionTests
    {
        private static List<RealityTriangle> Bed(bool headboard = true,
            bool overlappingBlanket = true, float floor = .39f, float surface = .82f,
            float width = 1.9f, float length = 2.1f)
        {
            var triangles = new List<RealityTriangle>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int source)
            {
                triangles.Add(new RealityTriangle(a, b, c, (int)RealitySemantic.Bed, source));
                triangles.Add(new RealityTriangle(a, c, d, (int)RealitySemantic.Bed, source));
            }

            for (var x = 0; x < 7; x++)
                for (var z = 0; z < 9; z++)
                {
                    var x0 = -width * .5f + x * width / 7f;
                    var z0 = -length * .5f + z * length / 9f;
                    Quad(new Vector3(x0, surface, z0),
                        new Vector3(x0 + width / 7f, surface, z0),
                        new Vector3(x0 + width / 7f, surface, z0 + length / 9f),
                        new Vector3(x0, surface, z0 + length / 9f), 10);
                }
            Quad(new Vector3(-width * .5f, floor, -length * .5f),
                new Vector3(width * .5f, floor, -length * .5f),
                new Vector3(width * .5f, surface, -length * .5f),
                new Vector3(-width * .5f, surface, -length * .5f), 10);
            if (headboard)
                Quad(new Vector3(-width * .48f, surface, length * .48f),
                    new Vector3(width * .48f, surface, length * .48f),
                    new Vector3(width * .48f, 1.18f, length * .48f),
                    new Vector3(-width * .48f, 1.18f, length * .48f), 10);
            if (overlappingBlanket)
                Quad(new Vector3(-width * .43f, surface + .025f, -length * .21f),
                    new Vector3(width * .43f, surface + .025f, -length * .21f),
                    new Vector3(width * .43f, surface + .025f, length * .21f),
                    new Vector3(-width * .43f, surface + .025f, length * .21f), 20);
            return triangles;
        }

        private static List<RealityTriangle> Sofa(float width = .62f, float length = 1.9f,
            float surface = .45f)
        {
            var triangles = new List<RealityTriangle>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int source)
            {
                triangles.Add(new RealityTriangle(a, b, c, (int)RealitySemantic.Sofa, source));
                triangles.Add(new RealityTriangle(a, c, d, (int)RealitySemantic.Sofa, source));
            }

            for (var x = 0; x < 4; x++)
                for (var z = 0; z < 8; z++)
                {
                    var x0 = -width * .5f + x * width / 4f;
                    var z0 = -length * .5f + z * length / 8f;
                    Quad(new Vector3(x0, surface, z0),
                        new Vector3(x0 + width / 4f, surface, z0),
                        new Vector3(x0 + width / 4f, surface, z0 + length / 8f),
                        new Vector3(x0, surface, z0 + length / 8f), 30);
                }
            // A backrest runs along the long side instead of across an end.
            Quad(new Vector3(width * .5f, surface, -length * .5f),
                new Vector3(width * .5f, surface, length * .5f),
                new Vector3(width * .5f, surface + .42f, length * .5f),
                new Vector3(width * .5f, surface + .42f, -length * .5f), 30);
            Quad(new Vector3(width * .5f + .12f, surface, -length * .5f),
                new Vector3(width * .5f + .12f, surface, length * .5f),
                new Vector3(width * .5f + .12f, surface + .42f, length * .5f),
                new Vector3(width * .5f + .12f, surface + .42f, -length * .5f), 30);
            Quad(new Vector3(width * .5f, surface + .42f, -length * .5f),
                new Vector3(width * .5f + .12f, surface + .42f, -length * .5f),
                new Vector3(width * .5f + .12f, surface + .42f, length * .5f),
                new Vector3(width * .5f, surface + .42f, length * .5f), 30);
            return triangles;
        }

        [Test]
        public void ExtractsSofaWithoutHeadEndCue()
        {
            var target = BedGeometry.Extract(Sofa(), 0).Single();
            Assert.That(target.Semantic, Is.EqualTo(RealitySemantic.Sofa));
            Assert.That(target.Width, Is.EqualTo(.62f).Within(.02f));
            Assert.That(target.Length, Is.EqualTo(1.9f).Within(.02f));
            Assert.That(target.Center.y, Is.EqualTo(.45f).Within(.005f));
            Assert.That(target.HeadCue, Is.EqualTo(BedHeadCue.Default));
        }

        [Test]
        public void ExtractsOneBedFromOverlappingFrameAndBlanket()
        {
            var target = BedGeometry.Extract(Bed(), .39f).Single();
            Assert.That(target.Width, Is.EqualTo(1.9f).Within(.02f));
            Assert.That(target.Length, Is.EqualTo(2.1f).Within(.02f));
            Assert.That(target.Center.y, Is.EqualTo(.82f).Within(.005f));
            Assert.That(Vector3.Angle(target.Forward, Vector3.back), Is.LessThan(1f));
            Assert.That(target.SourceIds, Is.EquivalentTo(new[] { 10, 20 }));
        }

        [TestCase(.42f, .95f, 2.0f)]
        [TestCase(.84f, 1.9f, 2.3f)]
        public void MountCandidatesFollowObservedBedHeightAndFrameExtents(
            float surface, float width, float length)
        {
            var target = BedGeometry.Extract(Bed(floor: 0, surface: surface,
                width: width, length: length), 0).Single();
            var candidates = BedGeometry.MountCandidates(target).ToArray();
            Assert.That(candidates, Has.Length.EqualTo(3));
            Assert.That(candidates.All(candidate =>
                Mathf.Abs(candidate.IdealDock.y) < .001f), Is.True);
            Assert.That(target.ContactRoot.y, Is.EqualTo(surface + .035f).Within(.005f));
            Assert.That(candidates.Where(candidate => candidate.Edge != BedMountEdge.Foot)
                .All(candidate => Vector3.Distance(candidate.IdealDock, target.Center) >
                                  width * .5f + .1f), Is.True);
        }

        [Test]
        public void PlannerUsesReachableSideAndFacesTowardBed()
        {
            var geometry = Bed();
            var target = BedGeometry.Extract(geometry, .39f).Single();
            var navigation = new TestNavigation(point => point.x > 1f, .39f);
            Assert.That(BedMountPlanner.TryCreate(target, new Vector3(2, .39f, -2),
                navigation, geometry, out var plan), Is.True);
            Assert.That(plan.Edge, Is.EqualTo(BedMountEdge.Left));
            var inward = plan.Rotation * Vector3.forward;
            Assert.That(Vector3.Dot(inward,
                Vector3.ProjectOnPlane(target.Center - plan.Dock, Vector3.up).normalized),
                Is.GreaterThan(.99f));
        }

        [Test]
        public void BedWithoutAnUnambiguousHeadEndUsesDefaultDirection()
        {
            var target = BedGeometry.Extract(Bed(headboard: false), .39f).Single();
            Assert.That(target.HeadCue, Is.EqualTo(BedHeadCue.Default));
            Assert.That(target.Forward.sqrMagnitude, Is.EqualTo(1).Within(.001f));
        }

        [Test]
        public void ExternalObjectAboveBodyLaneBlocksLying()
        {
            var geometry = Bed();
            var target = BedGeometry.Extract(geometry, .39f).Single();
            Assert.That(BedGeometry.HasClearance(target, geometry), Is.True);
            geometry.Add(new RealityTriangle(new Vector3(-.25f, 1.05f, -.3f),
                new Vector3(.25f, 1.05f, -.3f), new Vector3(0, 1.35f, .3f),
                (int)RealitySemantic.Unknown, 99));
            Assert.That(BedGeometry.HasClearance(target, geometry), Is.False);
        }

        [Test]
        public void ScannerPublishesOneLieCapabilityAfterStableGeometry()
        {
            using (var registry = new ItemRegistry())
            using (var directory = new RealityItemDirectory(registry))
            using (var source = new RealityMeshSource(Room()))
            using (var scanner = new RealBedScanner(directory, Room(), source))
            {
                var geometry = Bed();
                for (var index = 0; index < 3; index++)
                    scanner.Observe(geometry, Time.unscaledTime + index * .1f);
                var target = scanner.Targets.Single();
                Assert.That(registry.TryGet(target.Id, out var item), Is.True);
                Assert.That(item.Supports(RealityInteractionIds.Lie), Is.True);
                Assert.That(directory.TryGet(target.Id, out var reality), Is.True);
                Assert.That(reality.Semantic, Is.EqualTo(RealitySemantic.Bed));
            }
        }

        [Test]
        public void ScannerPublishesLieCapabilityForSofa()
        {
            using (var registry = new ItemRegistry())
            using (var directory = new RealityItemDirectory(registry))
            using (var source = new RealityMeshSource(Room()))
            using (var scanner = new RealBedScanner(directory, Room(), source))
            {
                var geometry = Sofa();
                for (var index = 0; index < 3; index++)
                    scanner.Observe(geometry, Time.unscaledTime + index * .1f);
                var target = scanner.Targets.Single();
                Assert.That(registry.TryGet(target.Id, out var item), Is.True);
                Assert.That(item.Supports(RealityInteractionIds.Lie), Is.True);
                Assert.That(directory.TryGet(target.Id, out var reality), Is.True);
                Assert.That(reality.Semantic, Is.EqualTo(RealitySemantic.Sofa));
            }
        }

        [UnityTest]
        public IEnumerator WalkLieRiseAndTargetLossRestoreStandingBody()
        {
            yield return new EnterPlayMode();
            yield return ExerciseAction();
            yield return new ExitPlayMode();
        }

        private IEnumerator ExerciseAction()
        {
            var roomObject = new GameObject("Bed action regression room");
            var room = roomObject.AddComponent<RuntimeRoomNavigation>();
            room.Build(Room(), false);
            var character = Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>(GameSceneBuilder.CharacterPath));
            character.transform.position = new Vector3(2.2f, .03f, -2.2f);
            var motor = character.GetComponent<AvatarMotor>();
            motor.Bind(character.GetComponentInChildren<Animator>());
            var rig = character.GetComponent<AvatarLyingRig>();
            Assert.That(rig, Is.Not.Null);
            var fixtureMesh = PhysicsRuntimeTests.AddRealityCollider(Bed(), roomObject.transform);

            using (var registry = new ItemRegistry())
            using (var directory = new RealityItemDirectory(registry))
            using (var source = new RealityMeshSource(Room()))
            using (var scanner = new RealBedScanner(directory, Room(), source))
            {
                var geometry = Bed();
                for (var index = 0; index < 3; index++)
                    scanner.Observe(geometry, Time.unscaledTime);
                var bed = scanner.Targets.Single().Copy();
                var catalog = new ActionCatalog();
                LieOnBedAction action = null;
                catalog.Register(new ActionDefinition(RealityInteractionIds.Lie, "lie",
                    ControlChannels.FullBody | ControlChannels.Locomotion,
                    _ => action = new LieOnBedAction(scanner, registry, bed.Id, motor,
                        new RoomNavigationService(room), rig)));
                using (var coordinator = new AvatarActionCoordinator(catalog))
                {
                    for (var cycle = 0; cycle < 2; cycle++)
                    {
                        var handle = coordinator.Submit(new AvatarIntent(
                            RealityInteractionIds.Lie, IntentSource.UI));
                        var sawWalking = false;
                        var sawMountingBarrier = false;
                        var fractionalLyingWeight = false;
                        for (var frame = 0; frame < 600 && rig.Weight < .999f &&
                                            !handle.IsFinished; frame++)
                        {
                            scanner.Observe(geometry, Time.unscaledTime);
                            coordinator.Tick(.02f);
                            sawWalking |= action.PhaseId == "walking";
                            if (action.PhaseId == "walking" ||
                                action.PhaseId == "docking")
                            {
                                Assert.That(rig.Weight, Is.Zero);
                                Assert.That(character.GetComponent<CapsuleCollider>().enabled,
                                    Is.True);
                                Assert.That(Vector3.Angle(character.transform.up, Vector3.up),
                                    Is.LessThan(1f));
                            }
                            if (action.PhaseId == "mounting")
                            {
                                sawMountingBarrier = true;
                                Assert.That(rig.Weight, Is.Zero);
                                Assert.That(character.GetComponent<CapsuleCollider>().enabled,
                                    Is.True);
                                Assert.That(Vector3.Angle(character.transform.up, Vector3.up),
                                    Is.LessThan(1f));
                                Assert.That(Vector3.ProjectOnPlane(
                                        character.transform.position - action.ApproachPosition,
                                        Vector3.up).magnitude, Is.LessThan(.03f));
                            }
                            fractionalLyingWeight |= rig.Weight > 0 && rig.Weight < 1;
                            yield return null;
                        }
                        Assert.That(handle.State, Is.EqualTo(ActionState.Running), handle.Error);
                        if (cycle == 0) Assert.That(sawWalking, Is.True);
                        Assert.That(sawMountingBarrier, Is.True);
                        Assert.That(fractionalLyingWeight, Is.False,
                            "Lying must snap from standing instead of drifting to the bed.");
                        Assert.That(rig.Weight, Is.EqualTo(1).Within(.001f));
                        Assert.That(motor.Body.IsContact, Is.True);
                        Assert.That(motor.Body.CanOccupy(motor.Body.Pose), Is.True);
                        Assert.That(Vector3.Distance(character.transform.position,
                            bed.ContactRoot), Is.LessThan(.01f));
                        Assert.That(Quaternion.Angle(character.transform.rotation,
                            bed.LyingRotation), Is.LessThan(1f));

                        if (cycle == 0) coordinator.Cancel(handle);
                        else scanner.Invalidate();
                        for (var frame = 0; frame < 140 && !handle.IsFinished; frame++)
                        {
                            if (cycle == 0) scanner.Observe(geometry, Time.unscaledTime);
                            coordinator.Tick(.02f);
                            yield return null;
                        }
                        Assert.That(handle.State,
                            Is.EqualTo(cycle == 0 ? ActionState.Cancelled : ActionState.Failed),
                            handle.Error);
                        Assert.That(rig.Weight, Is.Zero);
                        Assert.That(character.GetComponent<CapsuleCollider>().enabled, Is.True);
                        Assert.That(motor.Body.Rigidbody.isKinematic, Is.False);
                        Assert.That(coordinator.Leases.IsOccupied(ControlChannels.FullBody), Is.False);
                        Assert.That(Vector3.ProjectOnPlane(
                                character.transform.position - action.ApproachPosition,
                                Vector3.up).magnitude, Is.LessThan(.12f));
                    }
                }
            }
            Object.Destroy(character);
            Object.Destroy(roomObject);
            Object.Destroy(fixtureMesh);
        }

        [UnityTest]
        public IEnumerator SideTransfersClearHeadboardAndRejectBlockedRoute()
        {
            yield return new EnterPlayMode();
            yield return ExerciseSideTransfers();
            yield return new ExitPlayMode();
        }

        private IEnumerator ExerciseSideTransfers()
        {
            var roomObject = new GameObject("Side transfer room");
            roomObject.AddComponent<RuntimeRoomNavigation>().Build(Room(), false);
            var mesh = PhysicsRuntimeTests.AddRealityCollider(Bed(), roomObject.transform);
            var character = Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>(GameSceneBuilder.CharacterPath));
            var motor = character.GetComponent<AvatarMotor>();
            motor.Bind(character.GetComponentInChildren<Animator>());
            var bed = BedGeometry.Extract(Bed(), 0).Single();
            foreach (var side in new[] { -1f, 1f })
            {
                var standing = new Pose(new Vector3(side * 1.36f, .03f, 0),
                    Quaternion.LookRotation(Vector3.left * side));
                motor.Body.Place(standing);
                motor.BeginLyingContact(standing.position, new Pose(bed.ContactRoot, bed.LyingRotation));
                Assert.That(motor.Body.CanOccupy(motor.Body.Pose), Is.True);
                motor.EndBodyContact();
                Assert.That(motor.Body.IsContact, Is.False);
                Assert.That(Vector3.Distance(motor.Position, standing.position), Is.LessThan(.01f));
            }
            var blocker = new GameObject("Transfer ceiling", typeof(BoxCollider));
            blocker.transform.SetParent(roomObject.transform);
            blocker.transform.position = new Vector3(1.36f, 2.0f, 0);
            blocker.GetComponent<BoxCollider>().size = new Vector3(.6f, .1f, .6f);
            Physics.SyncTransforms();
            var before = motor.Body.Pose;
            Assert.Throws<InvalidOperationException>(() =>
                motor.BeginLyingContact(before.position, new Pose(bed.ContactRoot, bed.LyingRotation)));
            Assert.That(motor.Body.IsContact, Is.False);
            Assert.That(motor.Body.Pose.position, Is.EqualTo(before.position));
            Assert.That(character.GetComponent<CapsuleCollider>().height,
                Is.EqualTo(RuntimeRoomNavigation.CharacterHeight));
            Object.Destroy(character);
            Object.Destroy(roomObject);
            Object.Destroy(mesh);
            yield return null;
        }

        private sealed class TestNavigation : INavigationService
        {
            private readonly Func<Vector3, bool> accepts;
            public TestNavigation(Func<Vector3, bool> accepts, float floorHeight)
            {
                this.accepts = accepts;
                FloorHeight = floorHeight;
            }
            public float FloorHeight { get; }
            public bool TryPath(Vector3 origin, Vector3 destination, out Vector3[] corners)
            {
                corners = accepts(destination)
                    ? new[] { origin, destination }
                    : null;
                return corners != null;
            }
            public bool TrySample(Vector3 point, float distance, out Vector3 result)
            {
                result = point;
                return accepts(point);
            }
            public bool TryRandom(Vector3 origin, System.Random random, out Vector3 result)
            {
                result = default;
                return false;
            }
        }

        private static RoomMapSnapshot Room() => new RoomMapSnapshot("test", "fixture", new[]
        {
            new RoomFloorRegion("floor", new[]
            {
                new Vector3(-3, 0, -3), new Vector3(-3, 0, 3),
                new Vector3(3, 0, 3), new Vector3(3, 0, -3)
            })
        }, new[]
        {
            new RoomObstacle("bed", RoomObstacleKind.Bed, new Vector3(0, .41f, 0),
                Quaternion.identity, new Vector3(1.9f, .82f, 2.1f))
        });
    }
}
