using System;
using System.Collections.Generic;
using ByteDance.PICO.XR;
using UnityEngine;

namespace MateSoul.Runtime
{
    public readonly struct HandContactPoint
    {
        public readonly Vector3 Position;
        public readonly float Radius;
        public readonly bool Valid;

        public HandContactPoint(Vector3 position, float radius, bool valid = true)
        {
            Position = position;
            Radius = radius;
            Valid = valid;
        }
    }

    public readonly struct HandGripSample
    {
        public readonly HandContactPoint Palm;
        public readonly HandContactPoint Web;
        public readonly HandContactPoint Thumb;
        public readonly HandContactPoint Index;
        public readonly HandContactPoint Middle;
        public readonly HandContactPoint Ring;
        public readonly HandContactPoint Little;

        public HandGripSample(
            HandContactPoint palm,
            HandContactPoint web,
            HandContactPoint thumb,
            HandContactPoint index,
            HandContactPoint middle,
            HandContactPoint ring,
            HandContactPoint little)
        {
            Palm = palm;
            Web = web;
            Thumb = thumb;
            Index = index;
            Middle = middle;
            Ring = ring;
            Little = little;
        }
    }

    public readonly struct HandGripContact
    {
        public readonly bool IsGrip;
        public readonly int ContactCount;
        public readonly float ClosestDistance;

        public HandGripContact(bool isGrip, int contactCount, float closestDistance)
        {
            IsGrip = isGrip;
            ContactCount = contactCount;
            ClosestDistance = closestDistance;
        }
    }

    public static class HandGripEvaluator
    {
        // Acquisition tolerance is deliberately separate from physical contact.
        public const float ContactAllowance = .032f;
        public const float FallbackPalmDistance = .065f;

        public static HandGripContact Evaluate(
            HandGripSample sample,
            IReadOnlyList<Collider> target,
            float extraAllowance = 0)
        {
            if (target == null || target.Count == 0)
                return new HandGripContact(false, 0, float.PositiveInfinity);

            var validCount = 0;
            var closest = float.PositiveInfinity;
            var palm = Touches(sample.Palm);
            var web = Touches(sample.Web);
            var thumb = Touches(sample.Thumb);
            var index = Touches(sample.Index);
            var middle = Touches(sample.Middle);
            var ring = Touches(sample.Ring);
            var little = Touches(sample.Little);
            var fingers = (index ? 1 : 0) + (middle ? 1 : 0) +
                          (ring ? 1 : 0) + (little ? 1 : 0);
            var contacts = (palm ? 1 : 0) + (web ? 1 : 0) +
                           (thumb ? 1 : 0) + fingers;
            var qualified = thumb && fingers >= 1 ||
                            palm ||
                            web && fingers >= 1;
            if (!qualified && validCount < 3 && sample.Palm.Valid)
                qualified = Distance(sample.Palm.Position, target) <=
                            FallbackPalmDistance + extraAllowance;
            return new HandGripContact(qualified, contacts, closest);

            bool Touches(HandContactPoint point)
            {
                if (!point.Valid) return false;
                validCount++;
                var distance = Distance(point.Position, target);
                closest = Mathf.Min(closest, distance);
                return distance <= Mathf.Max(.003f, point.Radius) +
                       ContactAllowance + extraAllowance;
            }
        }

        public static float Distance(Vector3 point, IReadOnlyList<Collider> target)
        {
            var closest = float.PositiveInfinity;
            for (var index = 0; index < target.Count; index++)
            {
                var shape = target[index];
                if (shape == null || !shape.enabled ||
                    !shape.gameObject.activeInHierarchy)
                    continue;
                float distance;
                if (shape is MeshCollider mesh && !mesh.convex)
                    distance = Mathf.Sqrt(shape.bounds.SqrDistance(point));
                else
                    distance = Vector3.Distance(point, shape.ClosestPoint(point));
                closest = Mathf.Min(closest, distance);
            }
            return closest;
        }

        public static bool TryClosestPoint(Vector3 origin, IReadOnlyList<Collider> target,
            out Vector3 point)
        {
            point = origin;
            var best = float.PositiveInfinity;
            for (var i = 0; i < target.Count; i++)
            {
                var shape = target[i];
                if (shape == null || !shape.enabled || !shape.gameObject.activeInHierarchy) continue;
                var candidate = shape is MeshCollider mesh && !mesh.convex
                    ? mesh.bounds.ClosestPoint(origin) : shape.ClosestPoint(origin);
                var distance = (candidate - origin).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                point = candidate;
            }
            return !float.IsPositiveInfinity(best);
        }
    }

    [DisallowMultipleComponent]
    public sealed class TrackedHandPhysicsSegment : MonoBehaviour
    {
        public PicoTrackedHand Hand { get; private set; }
        public Vector3 Velocity { get; internal set; }
        internal bool ContactEnabled;
        private Collider shape;

        internal void Configure(PicoTrackedHand hand)
        {
            Hand = hand;
            shape = GetComponent<Collider>();
        }

        private void OnTriggerEnter(Collider other) => Observe(other);
        private void OnTriggerStay(Collider other) => Observe(other);

        private void Observe(Collider other)
        {
            if (ContactEnabled)
                Hand?.PhysicsProxy?.ObserveContact(shape, other, Velocity);
        }
    }

    [DisallowMultipleComponent]
    public sealed class TrackedHandPhysics : MonoBehaviour
    {
        private const float TrackingJumpDistance = .12f;

        private static readonly SegmentDefinition[] SegmentDefinitions =
        {
            new SegmentDefinition(HandJoint.JointWrist, HandJoint.JointPalm, 1.15f),
            new SegmentDefinition(HandJoint.JointThumbMetacarpal, HandJoint.JointThumbProximal, 1f),
            new SegmentDefinition(HandJoint.JointThumbProximal, HandJoint.JointThumbDistal, .95f),
            new SegmentDefinition(HandJoint.JointThumbDistal, HandJoint.JointThumbTip, .85f),
            new SegmentDefinition(HandJoint.JointIndexProximal, HandJoint.JointIndexIntermediate, .9f),
            new SegmentDefinition(HandJoint.JointIndexIntermediate, HandJoint.JointIndexDistal, .82f),
            new SegmentDefinition(HandJoint.JointIndexDistal, HandJoint.JointIndexTip, .72f),
            new SegmentDefinition(HandJoint.JointMiddleProximal, HandJoint.JointMiddleIntermediate, .95f),
            new SegmentDefinition(HandJoint.JointMiddleIntermediate, HandJoint.JointMiddleDistal, .85f),
            new SegmentDefinition(HandJoint.JointMiddleDistal, HandJoint.JointMiddleTip, .74f),
            new SegmentDefinition(HandJoint.JointRingProximal, HandJoint.JointRingIntermediate, .9f),
            new SegmentDefinition(HandJoint.JointRingIntermediate, HandJoint.JointRingDistal, .8f),
            new SegmentDefinition(HandJoint.JointRingDistal, HandJoint.JointRingTip, .7f),
            new SegmentDefinition(HandJoint.JointLittleProximal, HandJoint.JointLittleIntermediate, .82f),
            new SegmentDefinition(HandJoint.JointLittleIntermediate, HandJoint.JointLittleDistal, .72f),
            new SegmentDefinition(HandJoint.JointLittleDistal, HandJoint.JointLittleTip, .64f)
        };

        private readonly List<Proxy> proxies = new List<Proxy>();
        private PicoTrackedHand hand;
        private Proxy palm;
        private Proxy support;
        private PhysicsBody ignoredBody;
        private int activationDelay;
        private readonly Dictionary<PhysicsBody, PushContact> itemContacts =
            new Dictionary<PhysicsBody, PushContact>();
        private readonly Dictionary<AvatarPhysicalResponse, PushContact> avatarContacts =
            new Dictionary<AvatarPhysicalResponse, PushContact>();

        internal Collider PalmCollider => palm?.Shape;
        internal Collider FingerCollider(int finger, int segment)
            => proxies[1 + finger * 3 + segment].Shape;

        public IReadOnlyList<Collider> Colliders
        {
            get
            {
                var result = new Collider[proxies.Count + 2];
                result[0] = palm?.Shape;
                for (var index = 0; index < proxies.Count; index++)
                    result[index + 1] = proxies[index].Shape;
                result[result.Length - 1] = support?.Shape;
                return result;
            }
        }

        public void Configure(PicoTrackedHand owner)
        {
            if (hand != null) return;
            hand = owner ?? throw new ArgumentNullException(nameof(owner));
            InteractionPhysicsLayers.Configure();
            palm = CreateProxy("Palm Physics", true);
            support = CreateProxy("Palm Support", true);
            support.Shape.isTrigger = false;
            support.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var material = new PhysicMaterial("Palm Support") {
                staticFriction = .8f, dynamicFriction = .65f, bounciness = 0,
                frictionCombine = PhysicMaterialCombine.Maximum };
            support.Shape.sharedMaterial = material;
            foreach (var definition in SegmentDefinitions)
                proxies.Add(CreateProxy(
                    $"{definition.Start}-{definition.End}", false, definition));
        }

        public void SetHeldBody(PhysicsBody body)
        {
            if (ReferenceEquals(ignoredBody, body)) return;
            SetIgnored(ignoredBody, false);
            ignoredBody = body;
            SetIgnored(ignoredBody, true);
        }

        private void FixedUpdate()
        {
            if (hand == null || !hand.Tracked)
            {
                ClearContacts();
                SetActive(false);
                activationDelay = 1;
                return;
            }

            palm.Valid = UpdatePalm();
            var validSegments = 0;
            for (var index = 0; index < proxies.Count; index++)
            {
                var proxy = proxies[index];
                proxy.Valid = UpdateSegment(proxy);
                if (proxy.Valid) validSegments++;
            }
            if (!palm.Valid || validSegments < 8)
            {
                ClearContacts();
                SetActive(false);
                activationDelay = 1;
                return;
            }
            if (activationDelay > 0)
            {
                ClearContacts();
                activationDelay--;
                SetActive(false);
                return;
            }
            SetActive(true);
            UpdateSupport();
            if (!palm.Segment.ContactEnabled)
            {
                ClearContacts();
                return;
            }
            ApplyContacts();
        }

        private void UpdateSupport()
        {
            if (support == null || !hand.TryGetStableGripPose(out var pose)) return;
            var box = (BoxCollider)support.Shape;
            box.size = new Vector3(.075f, .022f, .085f);
            var delta = pose.position - support.PreviousPosition;
            var up = Mathf.Abs(Vector3.Dot(pose.rotation * Vector3.up, Vector3.up));
            var continuous = support.Initialized && delta.magnitude < TrackingJumpDistance;
            // A calm, roughly horizontal hand can carry weight. Fast gestures
            // use the bounded push sensors rather than an infinite-mass collider.
            support.Shape.enabled = continuous && up > .55f &&
                hand.PalmVelocity.magnitude < .8f;
            if (continuous)
            {
                support.Body.MovePosition(pose.position);
                support.Body.MoveRotation(pose.rotation);
            }
            else
            {
                support.Body.position = pose.position;
                support.Body.rotation = pose.rotation;
            }
            support.PreviousPosition = pose.position;
            support.Initialized = true;
        }

        internal void ObserveContact(Collider shape, Collider other, Vector3 velocity)
        {
            if (other.isTrigger || !Physics.ComputePenetration(
                    shape, shape.transform.position, shape.transform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out var outward, out var depth) || depth < .001f)
                return;
            var inward = -outward;
            // Keep the sign until PhysicsBody compares both contact velocities.
            // Clamping a withdrawing hand to zero would brake a released item.
            var approach = Vector3.Dot(velocity, inward);
            var part = other.GetComponent<AvatarBodyPartCollider>();
            var contact = new PushContact(
                inward, approach, depth,
                other.ClosestPoint(shape.bounds.center), part);
            if (part != null)
            {
                var avatar = part.GetComponentInParent<AvatarPhysicalResponse>();
                if (avatar != null &&
                    (!avatarContacts.TryGetValue(avatar, out var previous) ||
                     contact.Score > previous.Score))
                    avatarContacts[avatar] = contact;
                return;
            }
            if (other.attachedRigidbody == null) return;
            var body = other.attachedRigidbody.GetComponent<PhysicsBody>();
            if (body == null || body == ignoredBody || body.IsFollowing ||
                body.IsContact || body.IsPaused || body.IsInteractionControlled ||
                body.Rigidbody.isKinematic)
                return;
            // The solid palm already supplies the upward contact impulse.
            // Do not add a second sensor push while it is supporting weight.
            if (support.Shape.enabled && inward.y > .5f && approach < .8f &&
                support.Shape.bounds.Contains(new Vector3(other.bounds.center.x,
                    support.Shape.bounds.center.y, other.bounds.center.z)))
                return;
            if (!itemContacts.TryGetValue(body, out var itemPrevious) ||
                contact.Score > itemPrevious.Score)
                itemContacts[body] = contact;
        }

        private void ApplyContacts()
        {
            foreach (var pair in itemContacts)
                if (pair.Key != null && pair.Key != ignoredBody)
                    pair.Key.ApplyHandPush(
                        pair.Value.Direction, pair.Value.Speed,
                        pair.Value.Depth, pair.Value.Point);
            foreach (var pair in avatarContacts)
                if (pair.Key != null)
                    pair.Key.ReceiveContact(
                        pair.Value.Part, pair.Value.Direction,
                        pair.Value.Speed, pair.Value.Depth, pair.Value.Point);
            ClearContacts();
        }

        private void ClearContacts()
        {
            itemContacts.Clear();
            avatarContacts.Clear();
        }

        private bool UpdatePalm()
        {
            if (!hand.TryGetJoint(
                    HandJoint.JointPalm, out var pose, out var radius) ||
                !hand.TryGetJoint(
                    HandJoint.JointWrist, out var wrist, out _))
                return false;
            var scale = Mathf.Clamp(Vector3.Distance(
                pose.position, wrist.position) / .055f, .75f, 1.35f);
            var box = (BoxCollider)palm.Shape;
            box.size = new Vector3(.072f, .026f, .082f) * scale;
            return Move(palm, pose.position, pose.rotation);
        }

        private bool UpdateSegment(Proxy proxy)
        {
            var definition = proxy.Definition;
            if (!hand.TryGetJoint(definition.Start, out var start, out var startRadius) ||
                !hand.TryGetJoint(definition.End, out var end, out var endRadius))
                return false;
            var offset = end.position - start.position;
            if (offset.sqrMagnitude < .000004f) return false;
            var radius = Mathf.Clamp(
                Mathf.Max(startRadius, endRadius) * definition.RadiusScale,
                .006f, .014f);
            var capsule = (CapsuleCollider)proxy.Shape;
            capsule.radius = radius;
            capsule.height = Mathf.Max(radius * 2.05f, offset.magnitude + radius * 2f);
            var up = start.rotation * Vector3.up;
            if (Vector3.Cross(offset, up).sqrMagnitude < .000001f)
                up = start.rotation * Vector3.right;
            return Move(proxy, (start.position + end.position) * .5f,
                Quaternion.LookRotation(offset.normalized, up));
        }

        private static bool Move(Proxy proxy, Vector3 position, Quaternion rotation)
        {
            var delta = position - proxy.PreviousPosition;
            proxy.Segment.ContactEnabled = proxy.Initialized &&
                delta.sqrMagnitude <= TrackingJumpDistance * TrackingJumpDistance;
            if (!proxy.Initialized || delta.sqrMagnitude >
                TrackingJumpDistance * TrackingJumpDistance)
            {
                proxy.Body.position = position;
                proxy.Body.rotation = rotation;
                proxy.Segment.Velocity = Vector3.zero;
                proxy.Initialized = true;
                proxy.PreviousPosition = position;
                return true;
            }
            proxy.Segment.Velocity = Vector3.ClampMagnitude(
                delta / Mathf.Max(.001f, Time.fixedDeltaTime), 3f);
            // Sensors never transfer the infinite mass of a tracked kinematic hand.
            proxy.Body.position = position;
            proxy.Body.rotation = rotation;
            proxy.PreviousPosition = position;
            return true;
        }

        private Proxy CreateProxy(
            string name,
            bool box,
            SegmentDefinition definition = default)
        {
            var child = new GameObject(name);
            child.layer = InteractionPhysicsLayers.DirectHand;
            child.transform.SetParent(transform, false);
            var body = child.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            Collider shape;
            if (box)
            {
                shape = child.AddComponent<BoxCollider>();
            }
            else
            {
                var capsule = child.AddComponent<CapsuleCollider>();
                capsule.direction = 2;
                shape = capsule;
            }
            shape.isTrigger = true;
            shape.contactOffset = .002f;
            shape.enabled = false;
            var segment = child.AddComponent<TrackedHandPhysicsSegment>();
            segment.Configure(hand);
            return new Proxy(body, shape, segment, definition);
        }

        private void SetActive(bool value)
        {
            if (palm != null) palm.Shape.enabled = value && palm.Valid;
            if (!value && support != null) { support.Shape.enabled = false; support.Initialized = false; }
            foreach (var proxy in proxies)
                proxy.Shape.enabled = value && proxy.Valid;
        }

        private void SetIgnored(PhysicsBody body, bool ignored)
        {
            if (body == null || body.Shapes == null) return;
            foreach (var handShape in Colliders)
            {
                if (handShape == null) continue;
                foreach (var bodyShape in body.Shapes)
                    if (bodyShape != null)
                        Physics.IgnoreCollision(handShape, bodyShape, ignored);
            }
        }

        private void OnDisable()
        {
            ClearContacts();
            SetActive(false);
            SetHeldBody(null);
        }

        private void OnDestroy()
        {
            if (support?.Shape == null || support.Shape.sharedMaterial == null) return;
            if (Application.isPlaying) Destroy(support.Shape.sharedMaterial);
            else DestroyImmediate(support.Shape.sharedMaterial);
        }

        private readonly struct SegmentDefinition
        {
            public readonly HandJoint Start;
            public readonly HandJoint End;
            public readonly float RadiusScale;

            public SegmentDefinition(
                HandJoint start,
                HandJoint end,
                float radiusScale)
            {
                Start = start;
                End = end;
                RadiusScale = radiusScale;
            }
        }

        private sealed class Proxy
        {
            public readonly Rigidbody Body;
            public readonly Collider Shape;
            public readonly TrackedHandPhysicsSegment Segment;
            public readonly SegmentDefinition Definition;
            public bool Initialized, Valid;
            public Vector3 PreviousPosition;

            public Proxy(
                Rigidbody body,
                Collider shape,
                TrackedHandPhysicsSegment segment,
                SegmentDefinition definition)
            {
                Body = body;
                Shape = shape;
                Segment = segment;
                Definition = definition;
            }
        }

        private readonly struct PushContact
        {
            public readonly Vector3 Direction, Point;
            public readonly float Speed, Depth;
            public readonly AvatarBodyPartCollider Part;
            public float Score => Speed + Mathf.Min(Depth, .04f) * 4f;

            public PushContact(Vector3 direction, float speed, float depth,
                Vector3 point, AvatarBodyPartCollider part)
            {
                Direction = direction;
                Speed = speed;
                Depth = depth;
                Point = point;
                Part = part;
            }
        }
    }
}
