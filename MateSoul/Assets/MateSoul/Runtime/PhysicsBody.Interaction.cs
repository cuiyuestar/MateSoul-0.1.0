using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed partial class PhysicsBody
    {
        private Guid interactionOwner;
        private InteractionSnapshot interactionSnapshot;
        public bool IsInteractionControlled => interactionOwner != Guid.Empty;

        private struct InteractionSnapshot
        {
            public Pose Pose;
            public Vector3 Velocity, AngularVelocity;
            public bool Kinematic, Gravity, Sleeping, DetectCollisions;
            public RigidbodyConstraints Constraints;
            public CollisionDetectionMode Detection;
            public RigidbodyInterpolation Interpolation;
        }

        public void BeginInteraction(Guid owner)
        {
            if (owner == Guid.Empty)
                throw new InvalidOperationException("物品已被另一操作控制。");
            if (docked) Undock();
            if (IsPaused || following || IsInteractionControlled)
                throw new InvalidOperationException("物品已被另一操作控制。");
            var rb = Rigidbody;
            interactionSnapshot = new InteractionSnapshot
            {
                Pose = Pose, Velocity = rb.velocity, AngularVelocity = rb.angularVelocity,
                Kinematic = rb.isKinematic, Gravity = rb.useGravity,
                Sleeping = rb.IsSleeping(), Constraints = rb.constraints,
                Detection = rb.collisionDetectionMode,
                DetectCollisions = rb.detectCollisions,
                Interpolation = rb.interpolation
            };
            interactionOwner = owner;
            if (!rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            SetKinematic(true);
            rb.useGravity = false;
            // Cookware stays solid so food dropped into a carried pan can rest.
            rb.detectCollisions = !RetainCollisionsWhileHeld;
            rb.interpolation = RigidbodyInterpolation.None;
        }

        public bool SetInteractionCollisions(Guid owner, bool enabled)
        {
            if (owner == Guid.Empty || owner != interactionOwner || IsPaused) return false;
            Rigidbody.detectCollisions = enabled;
            return true;
        }

        // Hands a carried item over without releasing it, so a follow-up command
        // can continue from the held state instead of dropping and re-picking it.
        public bool TransferInteraction(Guid from, Guid to)
        {
            if (from == Guid.Empty || to == Guid.Empty || from != interactionOwner) return false;
            interactionOwner = to;
            return true;
        }

        // Re-asserts carry control after the reservation that owned it was released.
        // The original release snapshot is preserved so the item still falls normally
        // once it is finally put down.
        public bool ReassertInteraction(Guid owner)
        {
            if (owner == Guid.Empty || IsPaused) return false;
            interactionOwner = owner;
            SetKinematic(true);
            Rigidbody.useGravity = false;
            Rigidbody.velocity = Vector3.zero;
            Rigidbody.angularVelocity = Vector3.zero;
            Rigidbody.detectCollisions = !RetainCollisionsWhileHeld;
            return true;
        }

        public bool TryMoveInteraction(Guid owner, Pose target)
        {
            if (owner == Guid.Empty || owner != interactionOwner || IsPaused) return false;
            ValidatePose(target);
            // Held items are non-physical dependants. The interaction owner is the only pose
            // writer until release restores collision and gravity.
            Rigidbody.position = target.position;
            Rigidbody.rotation = target.rotation.normalized;
            transform.SetPositionAndRotation(Rigidbody.position, Rigidbody.rotation);
            return true;
        }

        public void EndInteraction(Guid owner)
        {
            if (owner == Guid.Empty || owner != interactionOwner) return;
            var rb = Rigidbody;
            var original = interactionSnapshot;
            try
            {
                rb.constraints = original.Constraints;
                rb.useGravity = original.Gravity;
                rb.detectCollisions = original.DetectCollisions;
                rb.interpolation = original.Interpolation;
                SetKinematic(original.Kinematic || IsPaused);
                if (!IsPaused)
                {
                    rb.collisionDetectionMode = original.Detection;
                    if (!original.Kinematic)
                    {
                        // Do not replay stale momentum after a long reservation.
                        rb.velocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                        if (original.Sleeping && HasSupport()) rb.Sleep();
                    }
                }
            }
            finally { interactionOwner = Guid.Empty; quietTime = 0; }
        }
    }
}
