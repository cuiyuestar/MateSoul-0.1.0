using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class PhysicsRuntimeTests
    {
        private GameObject root;
        private PhysicMaterial material;

        private void SetUp()
        {
            root = new GameObject("Physics integration room");
            material = new PhysicMaterial("Physics test")
            {
                staticFriction = .6f, dynamicFriction = .45f, bounciness = .05f
            };
            var floor = new GameObject("Floor", typeof(BoxCollider));
            floor.transform.SetParent(root.transform);
            floor.transform.position = Vector3.down * .1f;
            var shape = floor.GetComponent<BoxCollider>();
            shape.size = new Vector3(20, .2f, 20);
            shape.sharedMaterial = material;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(root);
            Object.Destroy(material);
            yield return null;
            yield return new ExitPlayMode();
        }

        private PhysicsBody Box(string name, Vector3 position, float mass = 1)
        {
            var item = new GameObject(name);
            item.transform.SetParent(root.transform);
            item.transform.position = position;
            item.AddComponent<BoxCollider>().size = Vector3.one * .2f;
            var body = item.AddComponent<PhysicsBody>();
            body.Configure(new BodySettings(mass, material, 0, .05f));
            return body;
        }

        [UnityTest]
        public IEnumerator GravityImpulseAndSleepUseRealMassAndRejectInvalidInput()
        {
            yield return new EnterPlayMode();
            SetUp();
            yield return ExerciseMassAndGravity();
        }

        private IEnumerator ExerciseMassAndGravity()
        {
            var light = Box("1 kg", new Vector3(-2, 2, 0));
            var heavy = Box("4 kg", new Vector3(2, 2, 0), 4);
            light.AddForce(Vector3.forward * 4, ForceMode.Impulse);
            heavy.AddForce(Vector3.forward * 4, ForceMode.Impulse);
            yield return EngineTestFrames.FixedStep();
            yield return EngineTestFrames.FixedStep();
            Assert.That(light.Rigidbody.velocity.z / heavy.Rigidbody.velocity.z, Is.EqualTo(4).Within(.02f));
            Assert.That(light.Rigidbody.velocity.y, Is.LessThan(-.1f));
            Assert.That(light.Rigidbody.velocity.y, Is.EqualTo(heavy.Rigidbody.velocity.y).Within(.01f));
            Assert.Throws<ArgumentException>(() => light.AddForce(new Vector3(float.NaN, 0, 0)));
            Assert.Throws<ArgumentException>(() =>
                light.Configure(new BodySettings(-1, material)));
            Assert.Throws<ArgumentException>(() =>
                light.Configure(new BodySettings(float.PositiveInfinity, material)));
            Assert.Throws<ArgumentException>(() =>
                light.Place(new Pose(Vector3.zero, new Quaternion(0, 0, 0, 0))));
            yield return EngineTestFrames.Seconds(4);
            Assert.That(light.Pose.position.y, Is.EqualTo(.1f).Within(.02f));
            Assert.That(heavy.Pose.position.y, Is.EqualTo(.1f).Within(.02f));
            Assert.That(light.IsSettled && heavy.IsSettled, Is.True);
            Assert.That(light.HasSupport() && heavy.HasSupport(), Is.True);
        }

        [UnityTest]
        public IEnumerator CompoundCollidersShareOneMaterialAndRejectNestedBodies()
        {
            yield return new EnterPlayMode();
            SetUp();
            yield return ExerciseCompoundBody();
        }

        private IEnumerator ExerciseCompoundBody()
        {
            var body = Box("Compound", new Vector3(0, 1, 0));
            var foot = new GameObject("Child collider", typeof(SphereCollider));
            foot.transform.SetParent(body.transform, false);
            foot.transform.localPosition = new Vector3(.18f, 0, 0);
            foot.GetComponent<SphereCollider>().radius = .08f;
            body.Configure(new BodySettings(2, material));
            Assert.That(body.Shapes.Count, Is.EqualTo(2));
            Assert.That(body.Shapes.All(shape => shape.sharedMaterial == material), Is.True);
            Assert.That(body.Shapes.All(shape => shape.attachedRigidbody == body.Rigidbody), Is.True);
            var original = body.Shapes[0].sharedMaterial;
            body.AddTorque(Vector3.right * .5f, ForceMode.Impulse);
            yield return EngineTestFrames.Seconds(.2f);
            Assert.That(body.Shapes.All(shape => shape.sharedMaterial == original), Is.True,
                "Rotation must not select another surface material.");
            foot.AddComponent<Rigidbody>();
            Assert.Throws<InvalidOperationException>(() => body.Configure(new BodySettings(2, material)));
        }

        [UnityTest]
        public IEnumerator HeldBodyCannotCrossThinWallAndReleaseKeepsResolvedMomentum()
        {
            yield return new EnterPlayMode();
            SetUp();
            yield return ExerciseFollowAndRelease();
        }

        private IEnumerator ExerciseFollowAndRelease()
        {
            var body = Box("Held", new Vector3(0, 1, 0));
            var wall = new GameObject("Thin wall", typeof(BoxCollider));
            wall.transform.SetParent(root.transform);
            wall.transform.position = new Vector3(0, 1, .8f);
            wall.GetComponent<BoxCollider>().size = new Vector3(6, 2, .025f);
            body.BeginFollow(new Pose(new Vector3(0, 1, 2), Quaternion.identity));
            yield return EngineTestFrames.Seconds(.65f);
            Assert.That(body.Pose.position.z, Is.LessThan(.72f));
            Assert.That(body.Rigidbody.isKinematic, Is.False);
            for (var index = 0; index < 15; index++)
            {
                body.SetFollowTarget(new Pose(new Vector3(index * .035f, 1, .5f),
                    Quaternion.Euler(0, index * 4, 0)));
                yield return EngineTestFrames.FixedStep();
            }
            var before = body.Pose;
            var velocity = body.Rigidbody.velocity;
            var angular = body.Rigidbody.angularVelocity;
            Assert.That(velocity.x, Is.GreaterThan(.2f));
            Assert.That(Mathf.Abs(angular.y), Is.GreaterThan(.1f));
            body.EndFollow();
            Assert.That(body.Rigidbody.velocity, Is.EqualTo(velocity));
            Assert.That(body.Rigidbody.angularVelocity, Is.EqualTo(angular));
            Assert.That(body.Pose.position, Is.EqualTo(before.position));
            yield return EngineTestFrames.Seconds(.1f);
            Assert.That(body.Pose.position.x, Is.GreaterThan(before.position.x));
            Assert.That(body.Pose.position.y, Is.LessThan(before.position.y));
        }

        [UnityTest]
        public IEnumerator PauseFreezesFallingBodyAndRestoresVelocityWithoutCatchup()
        {
            yield return new EnterPlayMode();
            SetUp();
            yield return ExercisePause();
        }

        private IEnumerator ExercisePause()
        {
            var body = Box("Falling", new Vector3(0, 3, 0));
            body.AddForce(Vector3.right, ForceMode.Impulse);
            yield return EngineTestFrames.Seconds(.2f);
            var position = body.Pose.position;
            var rotation = body.Pose.rotation;
            var velocity = body.Rigidbody.velocity;
            body.SetPaused(true);
            body.AddForce(Vector3.one * 100, ForceMode.Impulse);
            Assert.Throws<InvalidOperationException>(() => body.BeginFollow(body.Pose));
            yield return EngineTestFrames.Seconds(.3f);
            Assert.That(body.Pose.position, Is.EqualTo(position));
            Assert.That(body.Pose.rotation, Is.EqualTo(rotation));
            body.SetPaused(false);
            Assert.That(body.Rigidbody.velocity, Is.EqualTo(velocity));
            Assert.That(body.Pose.position, Is.EqualTo(position),
                "Resuming must not catch up the time spent paused.");
            yield return EngineTestFrames.FixedStep();
            yield return EngineTestFrames.FixedStep();
            Assert.That(body.Pose.position.y, Is.LessThan(position.y));
        }

        [UnityTest]
        public IEnumerator FrictionStopsSlidingAndFastObjectDoesNotTunnelThroughFloor()
        {
            yield return new EnterPlayMode();
            SetUp();
            yield return ExerciseFrictionAndCcd();
        }

        private IEnumerator ExerciseFrictionAndCcd()
        {
            var slippery = new PhysicMaterial("Slippery") { staticFriction = 0, dynamicFriction = 0 };
            root.GetComponentInChildren<BoxCollider>().sharedMaterial = slippery;
            var low = Box("Low friction", new Vector3(-2, .11f, 0));
            var high = Box("High friction", new Vector3(2, .11f, 0));
            low.Configure(new BodySettings(1, slippery, 0, .05f));
            yield return EngineTestFrames.Seconds(.15f);
            low.AddForce(Vector3.forward * 2, ForceMode.Impulse);
            high.AddForce(Vector3.forward * 2, ForceMode.Impulse);
            yield return EngineTestFrames.Seconds(1);
            Assert.That(low.Pose.position.z, Is.GreaterThan(high.Pose.position.z + .4f));
            var fast = Box("Fast", new Vector3(0, .25f, 0));
            fast.AddForce(Vector3.down * 30, ForceMode.Impulse);
            yield return EngineTestFrames.Seconds(.3f);
            Assert.That(fast.Pose.position.y, Is.GreaterThan(.075f));
            Object.Destroy(slippery);
        }

        internal static Mesh AddRealityCollider(IEnumerable<RealityTriangle> triangles, Transform parent)
        {
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            foreach (var triangle in triangles)
            {
                var start = vertices.Count;
                vertices.Add(triangle.A); vertices.Add(triangle.B); vertices.Add(triangle.C);
                indices.AddRange(new[] { start, start + 1, start + 2, start + 2, start + 1, start });
            }
            var mesh = new Mesh { vertices = vertices.ToArray(), triangles = indices.ToArray() };
            mesh.RecalculateBounds();
            var collision = new GameObject("Captured fixture collision", typeof(MeshCollider));
            collision.transform.SetParent(parent, false);
            collision.GetComponent<MeshCollider>().sharedMesh = mesh;
            return mesh;
        }
    }
}
