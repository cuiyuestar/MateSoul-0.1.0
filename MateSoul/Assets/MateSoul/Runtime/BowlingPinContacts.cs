using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MateSoul.Runtime
{
    // Dynamic pin colliders receive the bowling simulation. Kinematic proxies block
    // every foreign body without feeding that impulse back into the dynamic pins.
    public sealed class BowlingPinContacts : IDisposable
    {
        private sealed class Proxy
        {
            public BowlingPin Pin;
            public Rigidbody Body;
            public Collider[] Shapes;
        }

        private readonly List<BowlingPin> pins;
        private readonly List<Collider> pinShapes;
        private readonly List<Collider> ballShapes;
        private readonly List<Collider> supports;
        private readonly List<Collider> foreign = new List<Collider>();
        private readonly List<Proxy> proxies = new List<Proxy>();
        private readonly HashSet<int> known = new HashSet<int>();
        private bool ballReleased;
        private float nextScan;
        public int ProtectedColliderCount => foreign.Count;

        public bool BallReleased
        {
            get => ballReleased;
            set
            {
                if (ballReleased == value) return;
                ballReleased = value;
                ApplyBallCollisionMode();
            }
        }

        public BowlingPinContacts(IEnumerable<BowlingPin> protectedPins, BowlingBall ball,
            IEnumerable<Collider> roomShapes)
        {
            pins = protectedPins.ToList();
            pinShapes = pins.SelectMany(pin => pin.Body.Shapes).ToList();
            ballShapes = ball == null ? new List<Collider>() : ball.Body.Shapes.ToList();
            supports = roomShapes.ToList();
            foreach (var pin in pins) CreateProxy(pin);
            IgnoreProxiesForGameplay();
            ApplyBallCollisionMode();
            RegisterForeignColliders();
        }

        public void Sync()
        {
            foreach (var proxy in proxies)
            {
                if (proxy.Pin == null || proxy.Body == null) continue;
                proxy.Body.MovePosition(proxy.Pin.Body.Pose.position);
                proxy.Body.MoveRotation(proxy.Pin.Body.Pose.rotation);
            }
            if (Time.fixedTime >= nextScan)
            {
                nextScan = Time.fixedTime + .5f;
                RegisterForeignColliders();
            }
        }

        private void CreateProxy(BowlingPin pin)
        {
            var root = new GameObject(pin.name + " Foreign Collision Shield");
            root.transform.SetPositionAndRotation(pin.Body.Pose.position, pin.Body.Pose.rotation);
            var rigidBody = root.AddComponent<Rigidbody>();
            rigidBody.useGravity = false;
            rigidBody.isKinematic = true;
            rigidBody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var shapes = new List<Collider>();
            foreach (var source in pin.Body.Shapes)
            {
                var child = new GameObject(source.name);
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = pin.transform.InverseTransformPoint(source.transform.position);
                child.transform.localRotation =
                    Quaternion.Inverse(pin.transform.rotation) * source.transform.rotation;
                child.transform.localScale = source.transform.lossyScale;
                Collider clone;
                if (source is SphereCollider sphere)
                {
                    var value = child.AddComponent<SphereCollider>();
                    value.center = sphere.center; value.radius = sphere.radius;
                    clone = value;
                }
                else if (source is CapsuleCollider capsule)
                {
                    var value = child.AddComponent<CapsuleCollider>();
                    value.center = capsule.center; value.radius = capsule.radius;
                    value.height = capsule.height; value.direction = capsule.direction;
                    clone = value;
                }
                else if (source is BoxCollider box)
                {
                    var value = child.AddComponent<BoxCollider>();
                    value.center = box.center; value.size = box.size;
                    clone = value;
                }
                else throw new InvalidOperationException("球瓶保护层只支持简单碰撞体。");
                clone.sharedMaterial = source.sharedMaterial;
                shapes.Add(clone);
            }
            proxies.Add(new Proxy { Pin = pin, Body = rigidBody, Shapes = shapes.ToArray() });
        }

        private void IgnoreProxiesForGameplay()
        {
            var gameplay = pinShapes.Concat(ballShapes).Concat(supports).ToArray();
            var proxyShapes = proxies.SelectMany(proxy => proxy.Shapes).ToArray();
            foreach (var proxy in proxyShapes)
                foreach (var shape in gameplay)
                    if (proxy != shape) Physics.IgnoreCollision(proxy, shape, true);
            for (var first = 0; first < proxyShapes.Length; first++)
                for (var second = first + 1; second < proxyShapes.Length; second++)
                    Physics.IgnoreCollision(proxyShapes[first], proxyShapes[second], true);
        }

        private void ApplyBallCollisionMode()
        {
            foreach (var ballShape in ballShapes)
            {
                foreach (var pinShape in pinShapes)
                    Physics.IgnoreCollision(ballShape, pinShape, !ballReleased);
                foreach (var proxy in proxies)
                    foreach (var proxyShape in proxy.Shapes)
                        Physics.IgnoreCollision(ballShape, proxyShape, ballReleased);
            }
        }

        private void RegisterForeignColliders()
        {
            var excluded = new HashSet<int>(pinShapes.Concat(ballShapes).Concat(supports)
                .Concat(proxies.SelectMany(proxy => proxy.Shapes)).Select(shape => shape.GetInstanceID()));
            foreach (var other in Object.FindObjectsOfType<Collider>(true))
            {
                if (other == null || other.isTrigger || excluded.Contains(other.GetInstanceID()) ||
                    !known.Add(other.GetInstanceID())) continue;
                foreign.Add(other);
                foreach (var pinShape in pinShapes) Physics.IgnoreCollision(pinShape, other, true);
            }
        }

        public void Dispose()
        {
            foreach (var other in foreign)
                if (other != null)
                    foreach (var pinShape in pinShapes)
                        if (pinShape != null) Physics.IgnoreCollision(pinShape, other, false);
            foreach (var ballShape in ballShapes)
                if (ballShape != null)
                    foreach (var pinShape in pinShapes)
                        if (pinShape != null) Physics.IgnoreCollision(ballShape, pinShape, false);
            foreach (var proxy in proxies)
            {
                if (proxy.Body == null) continue;
                if (Application.isPlaying) Object.Destroy(proxy.Body.gameObject);
                else Object.DestroyImmediate(proxy.Body.gameObject);
            }
            proxies.Clear();
            foreign.Clear();
        }
    }
}
