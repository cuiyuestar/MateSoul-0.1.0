using System;
using System.Collections.Generic;
using MateSoul.RoomNavigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Networking;

namespace MateSoul.Runtime
{
    public interface INavigationService
    {
        float FloorHeight { get; }
        bool TryPath(Vector3 origin, Vector3 destination, out Vector3[] corners);
        bool TrySample(Vector3 point, float distance, out Vector3 result);
        bool TryRandom(Vector3 origin, System.Random random, out Vector3 result);
    }

    public sealed class RoomNavigationService : INavigationService
    {
        private readonly RuntimeRoomNavigation room;
        public RoomNavigationService(RuntimeRoomNavigation room) { this.room = room; }
        public float FloorHeight => room.Snapshot.MainFloor.Centroid.y;
        public bool TryPath(Vector3 origin, Vector3 destination, out Vector3[] corners)
        {
            corners = null;
            if (!TrySample(destination, .75f, out var target)) return false;
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(origin, target, NavMesh.AllAreas, path) ||
                path.status != NavMeshPathStatus.PathComplete) return false;
            corners = path.corners;
            return corners.Length > 0;
        }
        public bool TrySample(Vector3 point, float distance, out Vector3 result)
            => room.TrySamplePosition(point, distance, out result);
        public bool TryRandom(Vector3 origin, System.Random random, out Vector3 result)
            => room.TryGetRandomReachablePoint(origin, random, 1.25f, out result, out _);
    }

    public interface IAvatarMotor
    {
        Vector3 Position { get; }
        void Begin(Vector3[] path);
        bool Tick(float deltaSeconds);
        void Stop();
        void Face(Vector3 target, float deltaSeconds);
    }

    // The action coordinator owns intent; PhysicsBody owns the actual movement.
    [RequireComponent(typeof(PhysicsBody), typeof(CapsuleCollider))]
    public sealed class AvatarMotor : MonoBehaviour, IAvatarMotor
    {
        private const float AnimationIdleSpeed = .05f;
        private const float TurnAnimationStartSpeed = .12f;
        private const float TurnBeforeMoveEnterAngle = 70f;
        private const float TurnBeforeMoveExitAngle = 30f;
        // One generated TurnStep lasts 1.067s. Keep root yaw within that contact
        // cadence instead of allowing the capsule to outrun the planted feet.
        private const float MaximumInPlaceTurnSpeed = 78f;
        private const float TurnStepSettleSpeed = 28f;
        private const float TurnStepRecoverySeconds = .6f;
        private const float RepeatTurnStepAngle = 48f;
        private const float PhysicalAvoidanceActivationSeconds = .3f;
        private const float PhysicalAvoidanceHoldSeconds = 1.25f;
        private const float PhysicalAvoidanceFailureSeconds = 8f;
        [SerializeField] private Animator animator;
        [SerializeField] private float speed = 1.1f;
        [SerializeField] private float turnSpeed = 240f;
        [SerializeField] private float handHoldAcceleration = 2.2f;
        private PhysicsBody body;
        private CapsuleCollider capsule;
        private NavMeshAgent agent;
        private Vector3[] corners;
        private int corner;
        private int speedParameter, turnRateParameter, socialAttentionParameter;
        private int turnStepLeftParameter, turnStepRightParameter;
        private bool hasTurnRateParameter, hasSocialAttentionParameter;
        private bool hasTurnStepLeftParameter, hasTurnStepRightParameter;
        private bool turnStepRequested, turnStepWasObserved, completedTurnStep;
        private float turnStepRecoverySeconds;
        private bool contact;
        private Vector3 contactExit;
        private Vector3 desiredVelocity;
        private Quaternion facing;
        private float facingSpeed;
        private bool handHoldDrive, waitingForFacing;
        private readonly TurnInPlaceMotion turnMotion = new TurnInPlaceMotion();
        private AvatarTurnFootPlantRig turnFootPlant;
        private Collider physicalBlocker;
        private Vector3 physicalAvoidanceNormal;
        private float physicalBlockSeconds, physicalAvoidanceSeconds, physicalAvoidanceSign;
        private float physicalContactIdleSeconds, physicalAvoidanceTotalSeconds;
        private readonly HashSet<int> contactIgnoredSurfaceSources = new HashSet<int>();
        private readonly RaycastHit[] groundHits = new RaycastHit[12];
        public PhysicsBody Body => body;
        public string ContactPosture => !contact ? null :
            Vector3.Dot(body.Pose.rotation * Vector3.up, Vector3.up) < .7f ? "lying" : "seated";
        public Vector3 Position => body != null ? body.Pose.position : transform.position;
        public bool IsMoving => corners != null;
        public bool IsTurning => turnMotion.IsTurning;
        public bool SocialAttentionEnabled { get; private set; }
        public bool PhysicalAvoidanceFailed
            => physicalAvoidanceTotalSeconds >= PhysicalAvoidanceFailureSeconds;

        public void Bind(Animator characterAnimator)
        {
            var initialPose = new Pose(transform.position, transform.rotation);
            animator = characterAnimator;
            body = GetComponent<PhysicsBody>();
            capsule = GetComponent<CapsuleCollider>();
            body.Initialize();
            body.Rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            SetStandingShape();
            body.Place(initialPose);
            agent = GetComponent<NavMeshAgent>();
            if (agent == null) agent = gameObject.AddComponent<NavMeshAgent>();
            agent.radius = RuntimeRoomNavigation.CharacterRadius;
            agent.height = RuntimeRoomNavigation.CharacterHeight;
            agent.baseOffset = 0;
            agent.speed = speed;
            agent.angularSpeed = turnSpeed;
            agent.acceleration = 4f;
            agent.stoppingDistance = .12f;
            agent.autoBraking = true;
            agent.autoRepath = true;
            agent.updatePosition = false;
            agent.updateRotation = false;
            agent.updateUpAxis = false;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            agent.avoidancePriority = 50;
            if (!agent.isOnNavMesh &&
                NavMesh.SamplePosition(Position, out var navHit, .5f, NavMesh.AllAreas))
                agent.Warp(navHit.position);
            agent.nextPosition = Position;
            agent.isStopped = true;
            facing = body.Pose.rotation;
            facingSpeed = turnSpeed;
            speedParameter = Animator.StringToHash("Speed");
            turnRateParameter = Animator.StringToHash("TurnRate");
            socialAttentionParameter = Animator.StringToHash("SocialAttention");
            turnStepLeftParameter = Animator.StringToHash("TurnStepLeft");
            turnStepRightParameter = Animator.StringToHash("TurnStepRight");
            hasTurnRateParameter = HasFloatParameter(turnRateParameter);
            hasSocialAttentionParameter = HasParameter(
                socialAttentionParameter, AnimatorControllerParameterType.Bool);
            hasTurnStepLeftParameter = HasParameter(
                turnStepLeftParameter, AnimatorControllerParameterType.Trigger);
            hasTurnStepRightParameter = HasParameter(
                turnStepRightParameter, AnimatorControllerParameterType.Trigger);
            turnFootPlant = GetComponent<AvatarTurnFootPlantRig>();
            turnMotion.Reset();
            SocialAttentionEnabled = false;
            animator.applyRootMotion = false;
        }

        public void Begin(Vector3[] path)
        {
            if (path == null || path.Length == 0) throw new ArgumentException("A complete path is required.");
            if (contact) EndBodyContact();
            handHoldDrive = false;
            waitingForFacing = false;
            ResetTurnMotion();
            corners = path;
            corner = 0;
            if (agent == null || !agent.enabled)
                throw new InvalidOperationException("Navigation avoidance is unavailable.");
            if (!agent.isOnNavMesh)
            {
                if (!NavMesh.SamplePosition(Position, out var hit, .5f, NavMesh.AllAreas) ||
                    !agent.Warp(hit.position))
                    throw new InvalidOperationException("Character is outside the navigation mesh.");
            }
            agent.nextPosition = Position;
            var endpoint = path[path.Length - 1];
            var destinationChanged = !agent.hasPath ||
                Vector3.ProjectOnPlane(agent.destination - endpoint, Vector3.up).sqrMagnitude > .0025f;
            agent.isStopped = false;
            if (destinationChanged && !agent.SetDestination(endpoint))
                throw new InvalidOperationException("Navigation avoidance rejected the destination.");
        }

        public bool Tick(float deltaSeconds)
        {
            if (corners == null) return true;
            physicalContactIdleSeconds += deltaSeconds;
            if (physicalAvoidanceSeconds > 0)
            {
                physicalAvoidanceTotalSeconds += deltaSeconds;
                if (!waitingForFacing)
                    physicalAvoidanceSeconds = Mathf.Max(
                        0, physicalAvoidanceSeconds - deltaSeconds);
            }
            if (physicalBlocker != null && physicalContactIdleSeconds > .5f &&
                physicalAvoidanceSeconds <= 0)
            {
                physicalBlocker = null;
                physicalBlockSeconds = physicalAvoidanceSign =
                    physicalContactIdleSeconds = physicalAvoidanceTotalSeconds = 0;
                physicalAvoidanceNormal = Vector3.zero;
            }
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.nextPosition = Position;
                var endpoint = corners[corners.Length - 1];
                var remaining = Vector3.ProjectOnPlane(endpoint - Position, Vector3.up).magnitude;
                if (!agent.pathPending && remaining <= agent.stoppingDistance)
                {
                    Stop();
                    return true;
                }
                var avoidanceVelocity = Vector3.ProjectOnPlane(agent.desiredVelocity, Vector3.up);
                if (physicalAvoidanceSeconds > 0)
                    avoidanceVelocity = PhysicalAvoidanceVelocity();
                if (avoidanceVelocity.sqrMagnitude > .0025f)
                {
                    desiredVelocity = Vector3.ClampMagnitude(avoidanceVelocity, speed);
                    Face(Position + desiredVelocity, deltaSeconds);
                    GateMovementForTurn();
                    var avoidanceActualSpeed = Vector3.ProjectOnPlane(
                        body.Rigidbody.velocity, Vector3.up).magnitude;
                    SetAnimationSpeed(avoidanceActualSpeed);
                    return false;
                }
            }
            var difference = corners[corner] - Position;
            difference.y = 0;
            while (difference.magnitude < .12f)
            {
                if (++corner >= corners.Length) { Stop(); return true; }
                difference = corners[corner] - Position;
                difference.y = 0;
            }
            var movement = difference.normalized * Mathf.Min(speed * deltaSeconds, difference.magnitude);
            desiredVelocity = deltaSeconds > 0 ? movement / deltaSeconds : Vector3.zero;
            Face(corners[corner], deltaSeconds);
            GateMovementForTurn();
            var actualSpeed = Vector3.ProjectOnPlane(body.Rigidbody.velocity, Vector3.up).magnitude;
            SetAnimationSpeed(actualSpeed);
            return false;
        }

        public void Face(Vector3 target, float deltaSeconds)
            => FaceAtSpeed(target, turnSpeed);

        public void FaceAtSpeed(Vector3 target, float degreesPerSecond)
        {
            var direction = Vector3.ProjectOnPlane(target - Position, Vector3.up);
            if (direction.sqrMagnitude < .001f) return;
            facing = Quaternion.LookRotation(direction);
            facingSpeed = Mathf.Clamp(degreesPerSecond, 1f, turnSpeed);
        }

        public void HoldFacing()
        {
            if (body != null) facing = body.Pose.rotation;
            facingSpeed = turnSpeed;
            ResetTurnMotion();
        }

        public void SetSocialAttention(bool enabled)
        {
            SocialAttentionEnabled = enabled;
            if (animator != null && hasSocialAttentionParameter)
                animator.SetBool(socialAttentionParameter, enabled);
            if (!enabled)
                ResetTurnMotion();
        }

        public void Stop()
        {
            corners = null;
            handHoldDrive = false;
            waitingForFacing = false;
            desiredVelocity = Vector3.zero;
            physicalBlocker = null;
            physicalBlockSeconds = physicalAvoidanceSeconds = physicalAvoidanceSign = 0;
            physicalContactIdleSeconds = physicalAvoidanceTotalSeconds = 0;
            physicalAvoidanceNormal = Vector3.zero;
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
                agent.nextPosition = Position;
            }
            SetAnimationSpeed(0);
        }

        private void GateMovementForTurn()
        {
            if (desiredVelocity.sqrMagnitude < .0025f || body == null) return;
            var forward = Vector3.ProjectOnPlane(
                body.Pose.rotation * Vector3.forward, Vector3.up);
            var direction = Vector3.ProjectOnPlane(desiredVelocity, Vector3.up);
            var angle = Vector3.Angle(forward, direction);
            if (!waitingForFacing && angle >= TurnBeforeMoveEnterAngle)
                waitingForFacing = true;
            if (!waitingForFacing) return;
            if (angle <= TurnBeforeMoveExitAngle)
            {
                waitingForFacing = false;
                return;
            }
            desiredVelocity = Vector3.zero;
        }

        private Vector3 PhysicalAvoidanceVelocity()
        {
            var normal = Vector3.ProjectOnPlane(physicalAvoidanceNormal, Vector3.up);
            if (normal.sqrMagnitude < .01f) return Vector3.zero;
            normal.Normalize();
            var tangent = Vector3.Cross(Vector3.up, normal).normalized *
                          physicalAvoidanceSign;
            return tangent * speed;
        }

        public void BeginHandHold()
        {
            if (contact) throw new InvalidOperationException("Body contact must finish before hand holding starts.");
            Stop();
            handHoldDrive = true;
        }

        public void SetHandHoldVelocity(Vector3 velocity)
        {
            if (!handHoldDrive || body == null || body.IsPaused || contact) return;
            velocity.y = 0;
            desiredVelocity = Vector3.ClampMagnitude(velocity, speed);
            if (desiredVelocity.sqrMagnitude > .0025f)
                facing = Quaternion.LookRotation(desiredVelocity.normalized);
        }

        public void EndHandHold()
        {
            if (!handHoldDrive) return;
            handHoldDrive = false;
            desiredVelocity = Vector3.zero;
            SetAnimationSpeed(0);
        }

        public bool Dock(Vector3 position, Quaternion rotation, float dt)
        {
            Stop();
            var delta = Vector3.ProjectOnPlane(position - Position, Vector3.up);
            desiredVelocity = Vector3.ClampMagnitude(delta * 8f, .35f);
            facing = rotation;
            return delta.magnitude < .015f && Quaternion.Angle(body.Pose.rotation, rotation) < 1f;
        }

        private void FixedUpdate()
        {
            if (body == null || body.IsPaused || contact) return;
            if (agent != null && agent.enabled && agent.isOnNavMesh)
                agent.nextPosition = Position;
            var normal = Vector3.up;
            var grounded = false;
            var supportDistance = -1f;
            var count = Physics.SphereCastNonAlloc(Position + Vector3.up * .32f, capsule.radius * .9f,
                Vector3.down, groundHits, .16f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            for (var index = 0; index < count; index++)
            {
                if (groundHits[index].rigidbody == body.Rigidbody || groundHits[index].normal.y < .7f) continue;
                normal = groundHits[index].normal;
                supportDistance = groundHits[index].distance;
                grounded = true;
                break;
            }
            if (grounded)
            {
                var target = Vector3.ProjectOnPlane(desiredVelocity, normal);
                var actual = Vector3.ProjectOnPlane(body.Rigidbody.velocity, normal);
                var acceleration = handHoldDrive ? handHoldAcceleration : 14f;
                body.AddForce(Vector3.ClampMagnitude((target - actual) / Time.fixedDeltaTime, acceleration),
                    ForceMode.Acceleration);
            }
            UpdateFacing(Vector3.ProjectOnPlane(
                body.Rigidbody.velocity, Vector3.up).magnitude);
            if (handHoldDrive && animator != null)
                SetAnimationSpeed(
                    Vector3.ProjectOnPlane(body.Rigidbody.velocity, Vector3.up).magnitude);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (corners == null || collision == null) return;
            var other = collision.collider;
            var normal = collision.contactCount > 0
                ? collision.GetContact(0).normal : Vector3.zero;
            var horizontalNormal = Vector3.ProjectOnPlane(normal, Vector3.up);
            if (Mathf.Abs(normal.y) <= .7f && horizontalNormal.sqrMagnitude > .01f)
                physicalContactIdleSeconds = 0;
            var actualSpeed = Vector3.ProjectOnPlane(
                body.Rigidbody.velocity, Vector3.up).magnitude;
            if (Mathf.Abs(normal.y) <= .7f &&
                horizontalNormal.sqrMagnitude > .01f &&
                Vector3.Dot(desiredVelocity, horizontalNormal.normalized) < -.05f &&
                actualSpeed < .12f)
            {
                if (physicalBlocker != other)
                {
                    physicalBlocker = other;
                    physicalBlockSeconds = 0;
                    var tangent = Vector3.Cross(
                        Vector3.up, horizontalNormal.normalized).normalized;
                    var destinationEndpoint = corners[corners.Length - 1];
                    var plus = Vector3.ProjectOnPlane(
                        destinationEndpoint - (Position + tangent), Vector3.up).sqrMagnitude;
                    var minus = Vector3.ProjectOnPlane(
                        destinationEndpoint - (Position - tangent), Vector3.up).sqrMagnitude;
                    physicalAvoidanceSign = Mathf.Abs(plus - minus) > .01f
                        ? (plus < minus ? 1 : -1)
                        : (Vector3.Dot(tangent, transform.right) >= 0 ? 1 : -1);
                }
                physicalBlockSeconds += Time.fixedDeltaTime;
                if (physicalBlockSeconds >= PhysicalAvoidanceActivationSeconds)
                {
                    physicalAvoidanceNormal = horizontalNormal.normalized;
                    physicalAvoidanceSeconds = PhysicalAvoidanceHoldSeconds;
                }
            }
            else if (physicalBlocker == other)
            {
                physicalBlockSeconds = 0;
            }


        }

        private void OnCollisionExit(Collision collision)
        {
            if (collision != null && collision.collider == physicalBlocker)
                physicalBlockSeconds = 0;
        }

        private void SetAnimationSpeed(float value)
        {
            if (animator != null)
                animator.SetFloat(speedParameter,
                    value < AnimationIdleSpeed ? 0 : value);
        }

        private void UpdateFacing(float actualSpeed)
        {
            turnStepRecoverySeconds = Mathf.Max(
                0, turnStepRecoverySeconds - Time.fixedDeltaTime);
            if (waitingForFacing && actualSpeed >= TurnAnimationStartSpeed)
            {
                ResetTurnMotion();
                return;
            }
            if (desiredVelocity.sqrMagnitude >= AnimationIdleSpeed * AnimationIdleSpeed ||
                actualSpeed >= TurnAnimationStartSpeed)
            {
                ResetTurnMotion();
                body.RotateTowards(facing, facingSpeed * Time.fixedDeltaTime);
                return;
            }

            var forward = Vector3.ProjectOnPlane(
                body.Pose.rotation * Vector3.forward, Vector3.up);
            var target = Vector3.ProjectOnPlane(facing * Vector3.forward, Vector3.up);
            var remaining = forward.sqrMagnitude > .0001f && target.sqrMagnitude > .0001f
                ? Vector3.SignedAngle(forward, target, Vector3.up) : 0;
            var sample = turnMotion.Tick(remaining,
                Mathf.Min(facingSpeed, MaximumInPlaceTurnSpeed), Time.fixedDeltaTime);
            var turnStepPlaying = IsTurnStepPlaying();
            UpdateTurnStepAnimation(sample, turnStepPlaying);

            var delta = sample.DeltaAngle;
            if (sample.IsTurning && !turnStepPlaying)
            {
                var settleThreshold = completedTurnStep
                    ? RepeatTurnStepAngle : TurnInPlaceMotion.EnterAngle;
                delta = Mathf.Abs(sample.RemainingAngle) < settleThreshold
                    ? Mathf.Sign(sample.RemainingAngle) * Mathf.Min(
                        Mathf.Abs(sample.RemainingAngle),
                        TurnStepSettleSpeed * Time.fixedDeltaTime)
                    : 0;
            }
            if (Mathf.Abs(delta) > .0001f)
            {
                var rotationScale = turnFootPlant != null
                    ? turnFootPlant.RotationScale : 1f;
                delta *= rotationScale;
                var step = Quaternion.AngleAxis(delta, Vector3.up) *
                           body.Pose.rotation;
                body.RotateTowards(step, Mathf.Abs(delta));
            }
            SetAnimationTurnRate(turnStepPlaying ? sample.NormalizedRate : 0);
        }

        private bool HasFloatParameter(int hash)
            => HasParameter(hash, AnimatorControllerParameterType.Float);

        private bool HasParameter(int hash, AnimatorControllerParameterType type)
        {
            if (animator == null) return false;
            foreach (var parameter in animator.parameters)
                if (parameter.nameHash == hash &&
                    parameter.type == type)
                    return true;
            return false;
        }

        private void ResetTurnMotion()
        {
            turnMotion.Reset();
            SetAnimationTurnRate(0);
            turnStepRequested = false;
            turnStepWasObserved = false;
            completedTurnStep = false;
            turnStepRecoverySeconds = 0;
            if (animator != null)
            {
                if (hasTurnStepLeftParameter)
                    animator.ResetTrigger(turnStepLeftParameter);
                if (hasTurnStepRightParameter)
                    animator.ResetTrigger(turnStepRightParameter);
            }
        }

        private void SetAnimationTurnRate(float value)
        {
            if (animator != null && hasTurnRateParameter)
                animator.SetFloat(turnRateParameter, value);
        }

        private bool IsTurnStepPlaying()
        {
            if (animator == null) return false;
            var current = animator.GetCurrentAnimatorStateInfo(0);
            var inStep = current.shortNameHash == Animator.StringToHash("TurnStepLeft") ||
                         current.shortNameHash == Animator.StringToHash("TurnStepRight");
            if (!animator.IsInTransition(0)) return inStep;
            var next = animator.GetNextAnimatorStateInfo(0);
            return inStep ||
                   next.shortNameHash == Animator.StringToHash("TurnStepLeft") ||
                   next.shortNameHash == Animator.StringToHash("TurnStepRight");
        }

        private void UpdateTurnStepAnimation(
            TurnMotionSample sample, bool inStep)
        {
            if (animator == null ||
                !hasTurnStepLeftParameter || !hasTurnStepRightParameter)
                return;

            if (inStep)
            {
                turnStepRequested = true;
                turnStepWasObserved = true;
                return;
            }
            if (animator.IsInTransition(0)) return;
            if (turnStepRequested)
            {
                if (!turnStepWasObserved) return;
                turnStepRequested = false;
                turnStepWasObserved = false;
                completedTurnStep = true;
                turnStepRecoverySeconds = TurnStepRecoverySeconds;
                return;
            }
            if (turnStepRecoverySeconds > 0) return;
            var triggerAngle = completedTurnStep
                ? RepeatTurnStepAngle : TurnInPlaceMotion.EnterAngle;
            if (!sample.IsTurning ||
                Mathf.Abs(sample.RemainingAngle) < triggerAngle ||
                Mathf.Abs(sample.NormalizedRate) < .02f)
                return;

            animator.ResetTrigger(sample.NormalizedRate < 0
                ? turnStepRightParameter : turnStepLeftParameter);
            animator.SetTrigger(sample.NormalizedRate < 0
                ? turnStepLeftParameter : turnStepRightParameter);
            turnStepRequested = true;
            turnStepWasObserved = false;
        }


        public void BeginBodyContact(Vector3 exit, float seatHeight, int seatSourceId = -1)
        {
            Stop();
            ResetTurnMotion();
            contactExit = exit;
            contactIgnoredSurfaceSources.Clear();
            if (seatSourceId != -1) contactIgnoredSurfaceSources.Add(seatSourceId);
            // A torso capsule leaves room for the chair; feet/legs remain animation driven.
            capsule.radius = .14f;
            capsule.height = .8f;
            capsule.center = Vector3.up * (seatHeight - Position.y + .43f);
            Physics.SyncTransforms();
            if (!body.CanOccupy(body.Pose,
                    ignoredRoomSurfaceSources: contactIgnoredSurfaceSources))
            {
                contactIgnoredSurfaceSources.Clear();
                SetStandingShape();
                throw new InvalidOperationException("落座碰撞代理没有足够净空。");
            }
            contact = true;
            body.SetContact(true);
        }

        public void SetContactRoot(Vector3 position, Quaternion rotation)
        {
            if (!contact) throw new InvalidOperationException("No seat contact owner.");
            var target = new Pose(position, rotation);
            Physics.SyncTransforms();
            if (!body.IsPathClear(body.Pose, target, contactIgnoredSurfaceSources))
                throw new InvalidOperationException("坐卧路径被物体阻挡。");
            body.MoveContact(target);
        }

        public void BeginLyingContact(Vector3 exit, Pose target)
        {
            Stop();
            ResetTurnMotion();
            contactExit = exit;
            contactIgnoredSurfaceSources.Clear();
            var from = body.Pose;
            // The authored lying pose is a snap, but it must have a clear raised transfer
            // route and a valid torso volume. No disabled collider or unchecked teleport.
            capsule.radius = .14f;
            capsule.height = 1.5f;
            capsule.center = new Vector3(0, .775f, .145f);
            Physics.SyncTransforms();
            if (!IsLyingTransferClear(from, target))
            {
                SetStandingShape();
                throw new InvalidOperationException("床边转移路径被物体阻挡。");
            }
            contact = true;
            body.SetContact(true);
            body.Place(target);
        }

        public void EndBodyContact()
        {
            if (!contact) return;
            var previousCenter = capsule.center;
            var previousHeight = capsule.height;
            var previousRadius = capsule.radius;
            // Restore standing only in verified free space. A newly placed item can block the exit.
            var exitRotation = Quaternion.Euler(0, facing.eulerAngles.y, 0);
            for (var ring = 0; ring <= 3; ring++)
            {
                for (var index = 0; index < (ring == 0 ? 1 : 8); index++)
                {
                    var angle = index * Mathf.PI / 4;
                    var candidate = new Pose(contactExit +
                        new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (ring * .3f), exitRotation);
                    SetStandingShape();
                    Physics.SyncTransforms();
                    var standingClear = body.CanOccupy(candidate) && HasStandingSupport(candidate.position);
                    capsule.center = previousCenter;
                    capsule.height = previousHeight;
                    capsule.radius = previousRadius;
                    Physics.SyncTransforms();
                    if (!standingClear || !IsExitPathClear(body.Pose, candidate)) continue;
                    SetStandingShape();
                    body.Place(candidate);
                    contact = false;
                    body.SetContact(false);
                    contactIgnoredSurfaceSources.Clear();
                    GetComponent<AvatarSeatingRig>()?.Release();
                    GetComponent<AvatarLyingRig>()?.Release();
                    return;
                }
            }
            capsule.center = previousCenter;
            capsule.height = previousHeight;
            capsule.radius = previousRadius;
            // Keep the supported pose and proxy. A later movement request can retry after
            // the obstacle is removed; never expand a standing capsule into occupied space.
            throw new InvalidOperationException("起身出口被占用，已保持当前姿态；移开障碍后可重试。");
        }

        private bool HasStandingSupport(Vector3 position)
        {
            var count = Physics.RaycastNonAlloc(position + Vector3.up * .15f, Vector3.down,
                groundHits, .3f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            if (count == groundHits.Length) return false;
            for (var index = 0; index < count; index++)
                if (groundHits[index].rigidbody != body.Rigidbody && groundHits[index].normal.y > .7f)
                    return true;
            return false;
        }

        public bool TryPlaceAfterRestore(INavigationService navigation)
        {
            var origin = Position;
            for (var ring = 0; ring <= 12; ring++)
            {
                for (var index = 0; index < (ring == 0 ? 1 : 16); index++)
                {
                    var angle = index * Mathf.PI / 8f;
                    var point = origin + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (ring * .3f);
                    point.y = navigation.FloorHeight;
                    if (!navigation.TrySample(point, .2f, out var floor) ||
                        Mathf.Abs(floor.y - navigation.FloorHeight) > .1f) continue;
                    var candidate = new Pose(floor + Vector3.up * .02f, body.Pose.rotation);
                    if (!body.CanOccupy(candidate) || !HasStandingSupport(candidate.position)) continue;
                    body.Place(candidate);
                    return true;
                }
            }
            return false;
        }

        private bool IsExitPathClear(Pose from, Pose to)
        {
            if (Vector3.Dot(from.rotation * Vector3.up, Vector3.up) > .7f)
                return body.IsPathClear(from, to, contactIgnoredSurfaceSources);
            return IsLyingTransferClear(to, from);
        }

        private bool IsLyingTransferClear(Pose standing, Pose lying)
        {
            // Lift upright before rotation so the head does not sweep through the bed edge.
            // At a side dock, align with the foot end outside the bed before lying sideways.
            // The same geometric route is checked in reverse when leaving the bed.
            var height = Mathf.Max(standing.position.y, lying.position.y) + .2f;
            var raised = new Pose(new Vector3(standing.position.x, height, standing.position.z),
                standing.rotation);
            var aligned = raised;
            var lengthAxis = Vector3.ProjectOnPlane(lying.rotation * Vector3.up, Vector3.up).normalized;
            var sideAxis = Vector3.Cross(Vector3.up, lengthAxis);
            if (Mathf.Abs(Vector3.Dot(standing.position - lying.position, sideAxis)) > .3f)
                aligned.position += lengthAxis * Vector3.Dot(lying.position - standing.position, lengthAxis);
            var rotated = new Pose(aligned.position, lying.rotation);
            var aboveBed = new Pose(new Vector3(lying.position.x, height, lying.position.z), lying.rotation);
            var startClear = body.CanOccupy(standing);
            var liftClear = body.IsPathClear(standing, raised);
            var alignClear = body.IsPathClear(raised, aligned);
            var rotateClear = body.IsPathClear(aligned, rotated);
            var slideClear = body.IsPathClear(rotated, aboveBed);
            var landClear = body.IsPathClear(aboveBed, lying);
            return startClear && liftClear && alignClear && rotateClear && slideClear && landClear;
        }

        private void SetStandingShape()
        {
            capsule.radius = RuntimeRoomNavigation.CharacterRadius;
            capsule.height = RuntimeRoomNavigation.CharacterHeight;
            capsule.center = Vector3.up * capsule.height * .5f;
        }

        private void OnDisable()
        {
            Stop();
            if (body == null || body.IsPaused || !contact) return;
            contact = false;
            body.SetContact(false);
            contactIgnoredSurfaceSources.Clear();
            GetComponent<AvatarSeatingRig>()?.Release();
            GetComponent<AvatarLyingRig>()?.Release();
        }
    }
}
