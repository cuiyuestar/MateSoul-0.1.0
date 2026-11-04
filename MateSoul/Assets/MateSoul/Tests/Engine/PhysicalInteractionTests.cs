using System.Collections;
using System.Linq;
using MateSoul.Editor;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class PhysicalInteractionTests
    {
        [Test]
        public void GripQualificationUsesTolerantMultiPointContact()
        {
            var target = new GameObject("Grip target", typeof(BoxCollider));
            try
            {
                target.GetComponent<BoxCollider>().size = Vector3.one * .2f;
                Physics.SyncTransforms();
                var colliders = new Collider[] { target.GetComponent<Collider>() };
                var contact = HandGripEvaluator.Evaluate(
                    new HandGripSample(
                        new HandContactPoint(Vector3.up, .02f, false),
                        new HandContactPoint(Vector3.up, .01f, false),
                        new HandContactPoint(new Vector3(.105f, .02f, 0), .008f),
                        new HandContactPoint(new Vector3(.105f, -.02f, 0), .007f),
                        default,
                        default,
                        default),
                    colliders);
                Assert.That(contact.IsGrip, Is.True,
                    "Thumb plus one finger contact should accept a side grip.");
                Assert.That(contact.ContactCount, Is.EqualTo(2));

                var fallback = HandGripEvaluator.Evaluate(
                    new HandGripSample(
                        new HandContactPoint(new Vector3(.13f, 0, 0), .02f),
                        default, default, default, default, default, default),
                    colliders);
                Assert.That(fallback.IsGrip, Is.True,
                    "A strong grasp can use close palm contact when finger joints are unavailable.");

                var distant = HandGripEvaluator.Evaluate(
                    new HandGripSample(
                        new HandContactPoint(Vector3.right, .02f),
                        default, default, default, default, default, default),
                    colliders);
                Assert.That(distant.IsGrip, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ComplexSingleBoxItemGetsConvexCompoundColliders()
        {
            var root = new GameObject("Compound item", typeof(BoxCollider));
            try
            {
                root.GetComponent<BoxCollider>().size = new Vector3(1, .2f, 1);
                for (var index = 0; index < 2; index++)
                {
                    var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    part.name = "Model part " + index;
                    part.transform.SetParent(root.transform, false);
                    part.transform.localPosition = new Vector3(
                        index == 0 ? -.35f : .35f, 0, 0);
                    part.transform.localScale = new Vector3(.2f, .2f, 1);
                    Object.DestroyImmediate(part.GetComponent<BoxCollider>());
                }

                Assert.That(RuntimeColliderRefiner.Ensure(root), Is.True);
                Assert.That(root.GetComponent<BoxCollider>().enabled, Is.False);
                Assert.That(root.GetComponentsInChildren<Collider>(true)
                    .Count(shape => shape.enabled && !shape.isTrigger), Is.EqualTo(2));
                Assert.That(root.GetComponent<RuntimeDetailedColliderSet>()
                    .GeneratedCount, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ProductionAvatarInstallsDetailedBodyAndUsesHandSurfaceForHolding()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                GameSceneBuilder.CharacterPath);
            Assert.That(prefab, Is.Not.Null);
            var avatar = Object.Instantiate(prefab);
            try
            {
                var response = AvatarPhysicalResponse.Install(avatar);
                Assert.That(response.BodyParts.Count, Is.GreaterThanOrEqualTo(15));
                Assert.That(response.BodyParts.Count(part =>
                    part.Region == AvatarBodyRegion.Hand), Is.EqualTo(2));
                Assert.That(response.BodyParts.All(part =>
                    part.gameObject.layer ==
                    LayerMask.NameToLayer(InteractionPhysicsLayers.AvatarDetailName)),
                    Is.True);
                foreach (var socket in avatar
                             .GetComponentsInChildren<HandHoldInteractable>(true))
                    Assert.That(socket.colliders.Any(shape =>
                        shape.GetComponent<AvatarBodyPartCollider>()?.Region ==
                        AvatarBodyRegion.Hand), Is.True);
                var body = avatar.GetComponent<PhysicsBody>();
                Assert.That(body.Shapes.Count, Is.GreaterThanOrEqualTo(16));
            }
            finally
            {
                Object.DestroyImmediate(avatar);
            }
        }

        [UnityTest]
        public IEnumerator RoomSnapshotGeometryIsNavigationOnly()
        {
            yield return new EnterPlayMode();
            var root = new GameObject("Navigation-only room proxy");
            try
            {
                var room = root.AddComponent<MateSoul.RoomNavigation.RuntimeRoomNavigation>();
                room.Build(new MateSoul.RoomNavigation.RoomMapSnapshot(
                    "navigation-only",
                    "test",
                    new[]
                    {
                        new MateSoul.RoomNavigation.RoomFloorRegion("floor", new[]
                        {
                            new Vector3(-1, 0, -1),
                            new Vector3(-1, 0, 1),
                            new Vector3(1, 0, 1),
                            new Vector3(1, 0, -1)
                        })
                    },
                    new[]
                    {
                        new MateSoul.RoomNavigation.RoomObstacle(
                            "table",
                            MateSoul.RoomNavigation.RoomObstacleKind.Table,
                            new Vector3(0, .4f, 0),
                            Quaternion.identity,
                            new Vector3(.5f, .8f, .5f))
                    }), false);

                Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty,
                    "Room Capture proxies must not participate in physical collision.");
                Assert.That(room.TrySamplePosition(
                    new Vector3(.8f, 0, 0), .3f, out _), Is.True);
            }
            finally
            {
                Object.Destroy(root);
            }
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator RelaxedAvatarAgentCanUseNarrowMeasuredGap()
        {
            yield return new EnterPlayMode();
            var root = new GameObject("Relaxed navigation gap");
            try
            {
                var room = root.AddComponent<MateSoul.RoomNavigation.RuntimeRoomNavigation>();
                room.Build(new MateSoul.RoomNavigation.RoomMapSnapshot(
                    "narrow-gap",
                    "test",
                    new[]
                    {
                        new MateSoul.RoomNavigation.RoomFloorRegion("floor", new[]
                        {
                            new Vector3(-1.5f, 0, -1.5f),
                            new Vector3(-1.5f, 0, 1.5f),
                            new Vector3(1.5f, 0, 1.5f),
                            new Vector3(1.5f, 0, -1.5f)
                        })
                    },
                    new[]
                    {
                        new MateSoul.RoomNavigation.RoomObstacle(
                            "left",
                            MateSoul.RoomNavigation.RoomObstacleKind.Cabinet,
                            new Vector3(-.815f, .5f, 0),
                            Quaternion.identity,
                            new Vector3(1.21f, 1, 2.4f)),
                        new MateSoul.RoomNavigation.RoomObstacle(
                            "right",
                            MateSoul.RoomNavigation.RoomObstacleKind.Cabinet,
                            new Vector3(.815f, .5f, 0),
                            Quaternion.identity,
                            new Vector3(1.21f, 1, 2.4f))
                    }), false);
                var navigation = new RoomNavigationService(room);
                Assert.That(navigation.TryPath(
                    new Vector3(0, 0, -1.3f),
                    new Vector3(0, 0, 1.3f),
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
    }
}
