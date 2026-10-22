using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent, RequireComponent(typeof(PhysicsBody))]
    public sealed class CoffeeBeanJarLid : MonoBehaviour
    {
        [SerializeField] private Transform containerRoot;
        [SerializeField] private Transform socket;
        [SerializeField] private Collider physicalCollider;
        [SerializeField, Min(.005f)] private float snapDistance = .018f;
        [SerializeField, Range(1, 45)] private float snapAngle = 14f;
        [SerializeField, Min(.01f)] private float forceThreshold = .08f;
        [SerializeField, Min(0)] private float snapCooldown = .35f;

        private PhysicsBody body;
        private float detachedSeconds;
        private bool configured;

        public Transform Socket => socket;
        public Collider PhysicalCollider => physicalCollider;
        public bool IsAttached { get; private set; } = true;
        public bool IsOpen => !IsAttached;
        public float OpenRatio => IsAttached ? 0 : 1;
        public float ForceThreshold => forceThreshold;
        public float SnapDistance => snapDistance;
        public float SnapAngle => snapAngle;

        public void Configure(Transform owner, Transform lidSocket,
            Collider lidCollider, float attachDistance = .018f,
            float attachAngle = 14f, float impulseThreshold = .08f)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (lidSocket == null)
                throw new ArgumentNullException(nameof(lidSocket));
            if (lidCollider == null)
                throw new ArgumentNullException(nameof(lidCollider));
            if (!PhysicsBody.IsFinite(attachDistance) || attachDistance < .005f ||
                !PhysicsBody.IsFinite(attachAngle) ||
                attachAngle < 1 || attachAngle > 45 ||
                !PhysicsBody.IsFinite(impulseThreshold) || impulseThreshold <= 0)
                throw new ArgumentOutOfRangeException(nameof(attachDistance));
            containerRoot = owner;
            socket = lidSocket;
            physicalCollider = lidCollider;
            snapDistance = attachDistance;
            snapAngle = attachAngle;
            forceThreshold = impulseThreshold;
            configured = true;
        }

        private void Awake()
        {
            if (!configured && (containerRoot == null || socket == null ||
                                physicalCollider == null))
                throw new InvalidOperationException(
                    "Coffee bean jar lid bindings are incomplete.");
            configured = true;

            // A detachable lid is an independent body, not part of the jar's
            // compound collider hierarchy.
            transform.SetParent(containerRoot.parent, true);
            body = GetComponent<PhysicsBody>();
            body.Initialize();
            IgnoreContainerCollisions();
            Attach();
        }

        private void Update() => Advance(Time.deltaTime);

        public void AdvanceForTesting(float seconds)
        {
            if (!PhysicsBody.IsFinite(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            Advance(seconds);
        }

        private void Advance(float seconds)
        {
            if (!configured || body == null || seconds < 0) return;
            if (IsAttached)
            {
                if (body.IsFollowing || body.IsInteractionControlled)
                    Detach(false);
                else
                    body.Place(new Pose(socket.position, socket.rotation));
                return;
            }

            detachedSeconds += seconds;
            if (detachedSeconds < snapCooldown || body.IsFollowing ||
                body.IsInteractionControlled || body.IsPaused)
                return;
            var rb = body.Rigidbody;
            if (Vector3.Distance(rb.position, socket.position) > snapDistance ||
                Quaternion.Angle(rb.rotation, socket.rotation) > snapAngle ||
                rb.velocity.sqrMagnitude > .16f ||
                rb.angularVelocity.sqrMagnitude > 2.25f)
                return;
            Attach();
        }

        public bool ApplyImpulse(float impulse)
        {
            if (!PhysicsBody.IsFinite(impulse) || impulse < forceThreshold)
                return false;
            if (!Detach(true)) return false;
            body.AddForce(socket.up * Mathf.Min(.035f, impulse * .2f),
                ForceMode.Impulse);
            return true;
        }

        public bool DetachByHand() => Detach(false);

        private bool Detach(bool wakeBody)
        {
            if (!IsAttached) return false;
            body.Undock();
            IsAttached = false;
            detachedSeconds = 0;
            if (wakeBody) body.Rigidbody.WakeUp();
            return true;
        }

        private void Attach()
        {
            body.Dock(new Pose(socket.position, socket.rotation));
            IsAttached = true;
            detachedSeconds = 0;
            Physics.SyncTransforms();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsAttached || collision == null) return;
            ApplyImpulse(collision.impulse.magnitude);
        }

        private void IgnoreContainerCollisions()
        {
            foreach (var shape in containerRoot.GetComponentsInChildren<Collider>(true))
            {
                if (shape == physicalCollider || shape.isTrigger) continue;
                Physics.IgnoreCollision(physicalCollider, shape, true);
            }
        }
    }
}
