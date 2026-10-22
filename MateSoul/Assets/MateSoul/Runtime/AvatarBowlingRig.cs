using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    // Authored pose is evaluated first; held physics follows the final hand in LateUpdate.
    [DefaultExecutionOrder(13000)]
    public sealed class AvatarBowlingRig : MonoBehaviour
    {
        public const float NearPickupDistance = .08f;
        public const float FallbackPickupDistance = .45f;
        public enum PickupTier { None, Near, Expanded, Unconditional }
        private static readonly int RollState = Animator.StringToHash("Bowling.Roll");
        [SerializeField] private Animator animator;
        [SerializeField] private Transform hand;
        [SerializeField] private BowlingMotion motion;
        private Guid owner;
        private PhysicsBody ball;
        private Action onReleased;
        private Vector3 direction;
        private float speed, requestedTime;
        private int layer;
        public bool IsConfigured => animator && hand && motion && motion.clip &&
                                    animator.GetLayerIndex("Bowling") >= 0;
        public BowlingMotion Motion => motion;
        public bool DidRelease { get; private set; }
        public PickupTier LastPickupTier { get; private set; }
        public float PickupError { get; private set; }
        public float HeldAlignmentError { get; private set; }
        public Pose BallAtHand => new Pose(hand.TransformPoint(motion.ballInHand),
            hand.rotation * Quaternion.Euler(motion.ballEulerInHand));

        public void Configure(Animator value, Transform rightHand, BowlingMotion content)
        {
            content.Validate();
            animator = value; hand = rightHand; motion = content;
        }

        public void Begin(Guid run, PhysicsBody body, Vector3 aim, float launchSpeed, Action released)
        {
            if (!IsConfigured || owner != Guid.Empty || run == Guid.Empty)
                throw new InvalidOperationException("保龄球动画未配置或已被占用。");
            motion.Validate();
            owner = run; ball = body; direction = aim; speed = launchSpeed;
            onReleased = released;
            DidRelease = false; LastPickupTier = PickupTier.None;
            requestedTime = 0; HeldAlignmentError = 0;
            layer = animator.GetLayerIndex("Bowling");
            animator.SetLayerWeight(layer, 1);
            Sample(0);
        }

        public void Sample(float seconds)
        {
            requestedTime = Mathf.Clamp(seconds, 0, motion.clip.length);
            // Freeze the state clock; the action's pause-aware clock owns all event times.
            animator.Play(RollState, layer, requestedTime / motion.clip.length);
        }

        public void SyncAfterAnimation()
        {
            if (owner == Guid.Empty || ball == null || ball.IsPaused || DidRelease) return;
            var pose = BallAtHand;
            if (LastPickupTier == PickupTier.None && requestedTime >= motion.PickupTime)
            {
                PickupError = Vector3.Distance(ball.Pose.position, pose.position);
                if (PickupError <= NearPickupDistance) LastPickupTier = PickupTier.Near;
                else if (requestedTime < motion.FallbackTime) return;
                else LastPickupTier = PickupError <= FallbackPickupDistance
                    ? PickupTier.Expanded : PickupTier.Unconditional;
                ball.BeginInteraction(owner);
            }
            if (LastPickupTier == PickupTier.None) return;
            if (!ball.TryMoveInteraction(owner, pose))
                throw new InvalidOperationException("保龄球手部跟随控制权已失效。");
            HeldAlignmentError = Vector3.Distance(ball.Pose.position, pose.position);
            if (requestedTime < motion.ReleaseTime) return;
            ball.EndInteraction(owner);
            ball.AddForce(direction * speed - ball.Rigidbody.velocity, ForceMode.VelocityChange);
            var spin = Vector3.Cross(Vector3.up, direction) * (speed / BowlingGame.BallRadius);
            ball.AddTorque(spin - ball.Rigidbody.angularVelocity, ForceMode.VelocityChange);
            DidRelease = true;
            onReleased?.Invoke();
        }

        private void LateUpdate() => SyncAfterAnimation();

        public void End(Guid run)
        {
            if (owner != run) return;
            if (ball != null) ball.EndInteraction(owner);
            animator.SetLayerWeight(layer, 0);
            owner = Guid.Empty; ball = null; onReleased = null;
        }

        private void OnDisable()
        {
            if (owner != Guid.Empty) End(owner);
        }
    }
}
