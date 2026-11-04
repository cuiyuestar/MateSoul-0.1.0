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
        public bool ContainsFloorPoint(Vector3 point)
        {
            foreach (var floor in room.Snapshot.Floors)
                if (RoomGeometry.ContainsPointXZ(floor.Vertices, point)) return true;
            return false;
        }

        public bool TryPath(Vector3 origin, Vector3 destination, out Vector3[] corners)
        {
#region debug-point A-C-D:navigation-path-input
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var debugOriginOnMesh = NavMesh.SamplePosition(
                origin, out var debugOriginHit, .75f, NavMesh.AllAreas);
            var debugNearTargetOnMesh = TrySample(
                destination, .08f, out var debugNearTarget);
            var debugTargetOnMesh = TrySample(
                destination, .75f, out var debugTarget);
            var debugPath = new NavMeshPath();
            var debugCalculated = debugOriginOnMesh && debugTargetOnMesh &&
                                  NavMesh.CalculatePath(
                                      origin, debugTarget, NavMesh.AllAreas, debugPath);
            var debugPathCorners = debugPath.corners == null
                ? ""
                : string.Join(",", Array.ConvertAll(
                    debugPath.corners, value => JsonUtility.ToJson(value)));
            var debugTriangulation = NavMesh.CalculateTriangulation();
            var debugPayload =
                $"{{\"sessionId\":\"item-seat-navigation-fail\",\"runId\":\"post-fix\"," +
                $"\"hypothesisId\":\"A-C-D\",\"location\":\"RoomNavigationService.TryPath\"," +
                $"\"msg\":\"[DEBUG] navigation path input\",\"data\":{{" +
                $"\"frame\":{Time.frameCount},\"origin\":{JsonUtility.ToJson(origin)}," +
                $"\"destination\":{JsonUtility.ToJson(destination)}," +
                $"\"originOnMesh\":{debugOriginOnMesh.ToString().ToLowerInvariant()}," +
                $"\"originSample\":{JsonUtility.ToJson(debugOriginHit.position)}," +
                $"\"originError\":{(debugOriginOnMesh ? Vector3.Distance(origin, debugOriginHit.position) : -1)}," +
                $"\"nearTargetOnMesh\":{debugNearTargetOnMesh.ToString().ToLowerInvariant()}," +
                $"\"nearTarget\":{JsonUtility.ToJson(debugNearTarget)}," +
                $"\"nearTargetError\":{(debugNearTargetOnMesh ? Vector3.Distance(destination, debugNearTarget) : -1)}," +
                $"\"targetOnMesh\":{debugTargetOnMesh.ToString().ToLowerInvariant()}," +
                $"\"targetSample\":{JsonUtility.ToJson(debugTarget)}," +
                $"\"calculated\":{debugCalculated.ToString().ToLowerInvariant()}," +
                $"\"status\":\"{debugPath.status}\",\"corners\":[{debugPathCorners}]," +
                $"\"navVertices\":{debugTriangulation.vertices.Length}}}}}";
            var debugRequest = UnityWebRequest.Post(
                Application.isEditor
                    ? "http://127.0.0.1:7780/event"
                    : "http://127.0.0.1:7780/event",
                debugPayload, "application/json");
            debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
#endif
#endregion
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
        {
            var available = room.TryGetRandomReachablePoint(origin, random, .35f, out result, out _);
#region debug-point A:random-route-admission
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var shortAvailable = !available && room.TryGetRandomReachablePoint(
                origin, new System.Random(937), .35f, out var shortPoint, out _);
            var payload = $"{{\"sessionId\":\"movement-handhold-stall\",\"runId\":\"post-fix\"," +
                $"\"hypothesisId\":\"A\",\"location\":\"RoomNavigationService.TryRandom\"," +
                $"\"msg\":\"[DEBUG] random route admission\",\"data\":{{" +
                $"\"available\":{available.ToString().ToLowerInvariant()}," +
                $"\"shortAvailable\":{shortAvailable.ToString().ToLowerInvariant()}," +
                $"\"origin\":{JsonUtility.ToJson(origin)}}}}}";
            var request = UnityWebRequest.Post("http://127.0.0.1:7781/event", payload, "application/json");
            request.SendWebRequest().completed += _ => request.Dispose();
#endif
#endregion
            return available;
        }
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
        public const float FullWalkBlendSpeed = .12f;
        public const float WalkReferenceSpeed = .6221811f;
        private const float AnimationIdleSpeed = .05f;
        private const float TurnAnimationStartSpeed = .12f;
        private const float TurnBeforeMoveEnterAngle = 70f;
        private const float TurnBeforeMoveExitAngle = 30f;
        // One generated TurnStep lasts 1.067s. Keep root yaw within that contact
        // cadence instead of allowing the capsule to outrun the planted feet.
        private const float MaximumInPlaceTurnSpeed = 78f;
        private const float TurnStepSettleSpeed = 28f;
        private const float TurnStepEndTime = .96f;
        private const float TurnStepCompleteAngle = 1f;
        private const float TurnStepRecoverySeconds = .6f;
        private const float RepeatTurnStepAngle = 48f;
        private const float PhysicalAvoidanceActivationSeconds = .45f;
        private const float PhysicalAvoidanceHoldSeconds = .65f;
        private const float PhysicalAvoidanceFailureSeconds = 8f;
        [SerializeField] private Animator animator;
        [SerializeField] private float speed = .75f;
        [SerializeField] private float turnSpeed = 240f;
        [SerializeField] private float handHoldAcceleration = 2.2f;
        private PhysicsBody body;
        private CapsuleCollider capsule;
        private NavMeshAgent agent;
        private Vector3[] corners;
        private int corner;
        private int speedParameter, walkPlaybackRateParameter, turnRateParameter, socialAttentionParameter;
        private int turnStepLeftParameter, turnStepRightParameter, turnStepActiveParameter;
        private bool hasTurnRateParameter, hasSocialAttentionParameter;
        private bool hasTurnStepLeftParameter, hasTurnStepRightParameter, hasTurnStepActiveParameter;
        private bool turnStepRequested, turnStepActive;
        private float turnStepRecoverySeconds;
        private bool contact;
        private Vector3 contactExit;
        private Vector3 desiredVelocity;
        private Vector3 externalResponseVelocity;
        private Vector3 physicalStepOrigin, physicalStepOffset;
        private float physicalStepSeconds;
        private bool physicalStepActive;
        private Quaternion facing;
        private float facingSpeed;
        private bool handHoldDrive, waitingForFacing, dockFacing;
        private readonly TurnInPlaceMotion turnMotion = new TurnInPlaceMotion();
        private AvatarTurnFootPlantRig turnFootPlant;
        private Collider physicalBlocker;
        private Vector3 physicalAvoidanceNormal;
        private float physicalBlockSeconds, physicalAvoidanceSeconds, physicalAvoidanceSign;
        private float physicalContactIdleSeconds, physicalAvoidanceTotalSeconds;
        private readonly HashSet<int> contactIgnoredSurfaceSources = new HashSet<int>();
        private readonly RaycastHit[] groundHits = new RaycastHit[12];
#region debug-point B:grounded-motion
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        private float debugNextMotorReport;
#endif
#endregion
#region debug-point A-C:animation-state
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private float debugNextAnimationReport;
        private float debugNextMovementDriveReport;
        private float debugNextMovementCollisionReport;
        private float debugNextBedStallReport;
        private float debugNextBedCollisionReport;
        private float debugNextMovementHandholdReport;
        private Transform debugLeftLeg;
        private Transform debugRightLeg;
        private Quaternion debugPreviousLeftLeg;
        private Quaternion debugPreviousRightLeg;
#endif
#endregion
        public PhysicsBody Body => body;
        public string ContactPosture => !contact ? null :
            Vector3.Dot(body.Pose.rotation * Vector3.up, Vector3.up) < .7f ? "lying" : "seated";
        public Vector3 Position => body != null ? body.Pose.position : transform.position;
        public bool IsMoving => corners != null;
        public bool IsTurning => turnMotion.IsTurning;
        public bool IsTurnStepActive => turnStepActive;
        public bool SocialAttentionEnabled { get; private set; }
        public bool PhysicalAvoidanceFailed
            => physicalAvoidanceTotalSeconds >= PhysicalAvoidanceFailureSeconds;
        public Vector3 ExternalResponseVelocity => externalResponseVelocity;
        public bool IsPhysicalStepActive => physicalStepActive;
        public float PhysicalStepProgress => Mathf.Clamp01(physicalStepSeconds / .62f);

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
            agent.radius = RuntimeRoomNavigation.NavigationRadius;
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
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.avoidancePriority = 50;
            if (!agent.isOnNavMesh &&
                NavMesh.SamplePosition(Position, out var navHit, .5f, NavMesh.AllAreas))
                agent.Warp(navHit.position);
            agent.nextPosition = Position;
            agent.isStopped = true;
            facing = body.Pose.rotation;
            facingSpeed = turnSpeed;
            speedParameter = Animator.StringToHash("Speed");
            walkPlaybackRateParameter = Animator.StringToHash("WalkPlaybackRate");
            turnRateParameter = Animator.StringToHash("TurnRate");
            socialAttentionParameter = Animator.StringToHash("SocialAttention");
            turnStepLeftParameter = Animator.StringToHash("TurnStepLeft");
            turnStepRightParameter = Animator.StringToHash("TurnStepRight");
            turnStepActiveParameter = Animator.StringToHash("TurnStepActive");
            hasTurnRateParameter = HasFloatParameter(turnRateParameter);
            hasSocialAttentionParameter = HasParameter(
                socialAttentionParameter, AnimatorControllerParameterType.Bool);
            hasTurnStepLeftParameter = HasParameter(
                turnStepLeftParameter, AnimatorControllerParameterType.Trigger);
            hasTurnStepRightParameter = HasParameter(
                turnStepRightParameter, AnimatorControllerParameterType.Trigger);
            hasTurnStepActiveParameter = HasParameter(
                turnStepActiveParameter, AnimatorControllerParameterType.Bool);
            turnFootPlant = GetComponent<AvatarTurnFootPlantRig>();
            ResetTurnMotion();
            SocialAttentionEnabled = false;
            animator.applyRootMotion = false;
#region debug-point A-C:animation-state
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            foreach (var child in animator.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "Leg_L") debugLeftLeg = child;
                else if (child.name == "Leg_R") debugRightLeg = child;
            }
            debugPreviousLeftLeg = debugLeftLeg != null
                ? debugLeftLeg.localRotation : Quaternion.identity;
            debugPreviousRightLeg = debugRightLeg != null
                ? debugRightLeg.localRotation : Quaternion.identity;
#endif
#endregion
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
#region debug-point B-C-D:bed-stall-begin
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var debugCorners = string.Join(",",
                Array.ConvertAll(path, value => JsonUtility.ToJson(value)));
            var debugPayload =
                $"{{\"sessionId\":\"avatar-bed-corner-stall\",\"runId\":\"post-fix\"," +
                $"\"hypothesisId\":\"B-C-D\",\"location\":\"AvatarMotor.Begin\"," +
                $"\"msg\":\"[DEBUG] navigation begin\",\"data\":{{" +
                $"\"position\":{JsonUtility.ToJson(Position)}," +
                $"\"endpoint\":{JsonUtility.ToJson(endpoint)}," +
                $"\"corners\":[{debugCorners}],\"agentEnabled\":{agent.enabled.ToString().ToLowerInvariant()}," +
                $"\"onNavMesh\":{agent.isOnNavMesh.ToString().ToLowerInvariant()}," +
                $"\"isStopped\":{agent.isStopped.ToString().ToLowerInvariant()}," +
                $"\"hasPath\":{agent.hasPath.ToString().ToLowerInvariant()}," +
                $"\"pathPending\":{agent.pathPending.ToString().ToLowerInvariant()}," +
                $"\"pathStatus\":\"{agent.pathStatus}\",\"contact\":{contact.ToString().ToLowerInvariant()}}}}}";
#if UNITY_EDITOR
            const string debugEndpoint = "http://127.0.0.1:7778/event";
#else
            const string debugEndpoint = "http://127.0.0.1:7778/event";
#endif
            var debugRequest = UnityWebRequest.Post(
                debugEndpoint, debugPayload, "application/json");
            debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
#endif
#endregion
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
#region debug-point A-B-C-D-E:bed-stall-state
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Time.unscaledTime >= debugNextBedStallReport)
            {
                debugNextBedStallReport = Time.unscaledTime + .25f;
                var debugEndpointPosition = corners[corners.Length - 1];
                var debugForward = Vector3.ProjectOnPlane(
                    body.Pose.rotation * Vector3.forward, Vector3.up);
                var debugDrive = Vector3.ProjectOnPlane(desiredVelocity, Vector3.up);
                var debugAngle = debugDrive.sqrMagnitude > .0025f
                    ? Vector3.Angle(debugForward, debugDrive) : 0;
                var debugBlocker = physicalBlocker != null
                    ? physicalBlocker.name.Replace("\"", "'") : "none";
                var debugPayload =
                    $"{{\"sessionId\":\"avatar-bed-corner-stall\",\"runId\":\"post-fix\"," +
                    $"\"hypothesisId\":\"A-B-C-D-E\",\"location\":\"AvatarMotor.Tick\"," +
                    $"\"msg\":\"[DEBUG] navigation state sample\",\"data\":{{" +
                    $"\"position\":{JsonUtility.ToJson(Position)}," +
                    $"\"endpoint\":{JsonUtility.ToJson(debugEndpointPosition)}," +
                    $"\"corner\":{corner},\"cornerCount\":{corners.Length}," +
                    $"\"desiredVelocity\":{JsonUtility.ToJson(desiredVelocity)}," +
                    $"\"actualVelocity\":{JsonUtility.ToJson(body.Rigidbody.velocity)}," +
                    $"\"waitingForFacing\":{waitingForFacing.ToString().ToLowerInvariant()}," +
                    $"\"facingAngle\":{debugAngle},\"contact\":{contact.ToString().ToLowerInvariant()}," +
                    $"\"handHoldDrive\":{handHoldDrive.ToString().ToLowerInvariant()}," +
                    $"\"blocker\":\"{debugBlocker}\",\"blockSeconds\":{physicalBlockSeconds}," +
                    $"\"avoidanceSeconds\":{physicalAvoidanceSeconds}," +
                    $"\"avoidanceTotal\":{physicalAvoidanceTotalSeconds}," +
                    $"\"agentEnabled\":{(agent != null && agent.enabled).ToString().ToLowerInvariant()}," +
                    $"\"onNavMesh\":{(agent != null && agent.isOnNavMesh).ToString().ToLowerInvariant()}," +
                    $"\"isStopped\":{(agent != null && agent.isStopped).ToString().ToLowerInvariant()}," +
                    $"\"hasPath\":{(agent != null && agent.hasPath).ToString().ToLowerInvariant()}," +
                    $"\"pathPending\":{(agent != null && agent.pathPending).ToString().ToLowerInvariant()}," +
                    $"\"pathStatus\":\"{(agent != null ? agent.pathStatus.ToString() : "none")}\"," +
                    $"\"agentDesiredVelocity\":{JsonUtility.ToJson(agent != null ? agent.desiredVelocity : Vector3.zero)}}}}}";
#if UNITY_EDITOR
                const string debugEndpoint = "http://127.0.0.1:7778/event";
#else
                const string debugEndpoint = "http://127.0.0.1:7778/event";
#endif
                var debugRequest = UnityWebRequest.Post(
                    debugEndpoint, debugPayload, "application/json");
                debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
            }
#endif
#endregion
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
            EndPhysicalStep();
            corners = null;
            handHoldDrive = false;
            waitingForFacing = false;
            dockFacing = false;
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

        public void BeginPhysicalStep(Vector3 offset)
        {
            if (body == null || body.IsPaused || contact || handHoldDrive ||
                !PhysicsBody.IsFinite(offset))
                throw new InvalidOperationException("Balance step is unavailable.");
            Stop();
            HoldFacing();
            physicalStepOrigin = Position;
            physicalStepOffset = Vector3.ClampMagnitude(
                Vector3.ProjectOnPlane(offset, Vector3.up), .09f);
            physicalStepSeconds = 0;
            physicalStepActive = true;
        }

        public void EndPhysicalStep()
        {
            physicalStepActive = false;
            externalResponseVelocity = Vector3.zero;
        }

        public bool Dock(Vector3 position, Quaternion rotation, float dt,
            float positionTolerance = .015f)
        {
            Stop();
            var delta = Vector3.ProjectOnPlane(position - Position, Vector3.up);
            var positionReached = delta.magnitude < positionTolerance;
            desiredVelocity = positionReached ? Vector3.zero : Vector3.ClampMagnitude(delta * 8f, .35f);
            facing = rotation;
            var rotationReached = Quaternion.Angle(body.Pose.rotation, rotation) < 1f;
            dockFacing = !rotationReached;
            return positionReached && rotationReached;
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
#region debug-point A-B:movement-runtime-state
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            if (Time.unscaledTime >= debugNextMovementHandholdReport)
            {
                debugNextMovementHandholdReport = Time.unscaledTime + 1f;
                var payload = $"{{\"sessionId\":\"movement-handhold-stall\",\"runId\":\"post-fix\"," +
                    $"\"hypothesisId\":\"A-B\",\"location\":\"AvatarMotor.FixedUpdate\"," +
                    $"\"msg\":\"[DEBUG] movement runtime state\",\"data\":{{" +
                    $"\"position\":{JsonUtility.ToJson(Position)}," +
                    $"\"desiredVelocity\":{JsonUtility.ToJson(desiredVelocity)}," +
                    $"\"actualVelocity\":{JsonUtility.ToJson(body.Rigidbody.velocity)}," +
                    $"\"moving\":{(corners != null).ToString().ToLowerInvariant()}," +
                    $"\"handHoldDrive\":{handHoldDrive.ToString().ToLowerInvariant()}," +
                    $"\"grounded\":{grounded.ToString().ToLowerInvariant()}," +
                    $"\"bodyPaused\":{body.IsPaused.ToString().ToLowerInvariant()}," +
                    $"\"agentEnabled\":{(agent != null && agent.enabled).ToString().ToLowerInvariant()}," +
                    $"\"onNavMesh\":{(agent != null && agent.isOnNavMesh).ToString().ToLowerInvariant()}," +
                    $"\"isStopped\":{(agent != null && agent.isStopped).ToString().ToLowerInvariant()}," +
                    $"\"hasPath\":{(agent != null && agent.hasPath).ToString().ToLowerInvariant()}," +
                    $"\"pathStatus\":\"{(agent != null ? agent.pathStatus.ToString() : "none")}\"," +
                    $"\"blocker\":\"{(physicalBlocker != null ? physicalBlocker.name.Replace("\"", "'") : "none")}\"," +
                    $"\"avoidanceTotal\":{physicalAvoidanceTotalSeconds}}}}}";
                var request = UnityWebRequest.Post(
                    "http://127.0.0.1:7781/event", payload, "application/json");
                request.SendWebRequest().completed += _ => request.Dispose();
            }
#endif
#endregion
#region debug-point B:movement-drive
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (corners != null && Time.unscaledTime >= debugNextMovementDriveReport)
            {
                debugNextMovementDriveReport = Time.unscaledTime + .5f;
                var payload =
                    $"{{\"sessionId\":\"movement-blocked\",\"runId\":\"post-fix\"," +
                    $"\"hypothesisId\":\"B\",\"location\":\"AvatarMotor.FixedUpdate\"," +
                    $"\"msg\":\"[DEBUG] movement drive sample\",\"data\":{{" +
                    $"\"grounded\":{grounded.ToString().ToLowerInvariant()}," +
                    $"\"supportDistance\":{supportDistance},\"corner\":{corner}," +
                    $"\"cornerCount\":{corners.Length},\"position\":{JsonUtility.ToJson(Position)}," +
                    $"\"target\":{JsonUtility.ToJson(corners[Mathf.Min(corner, corners.Length - 1)])}," +
                    $"\"desiredVelocity\":{JsonUtility.ToJson(desiredVelocity)}," +
                    $"\"actualVelocity\":{JsonUtility.ToJson(body.Rigidbody.velocity)}}}}}";
#if UNITY_EDITOR
                const string endpoint = "http://127.0.0.1:7794/event";
#else
                const string endpoint = "http://127.0.0.1:7794/event";
#endif
                var request = UnityWebRequest.Post(endpoint, payload, "application/json");
                request.SendWebRequest().completed += _ => request.Dispose();
            }
#endif
#endregion
#region debug-point B:grounded-motion
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            if (corners != null && Time.unscaledTime >= debugNextMotorReport)
            {
                debugNextMotorReport = Time.unscaledTime + 1f;
                var position = Position;
                var velocity = body.Rigidbody.velocity;
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                var payload = $"{{\"sessionId\":\"bowling-companion-timeout\",\"runId\":\"post-fix\"," +
                              $"\"hypothesisId\":\"B\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                              $"\"location\":\"AvatarMotor.FixedUpdate\",\"msg\":\"[DEBUG] grounded motor sample\",\"data\":{{" +
                              $"\"grounded\":{grounded.ToString().ToLowerInvariant()},\"hitCount\":{count}," +
                              $"\"supportDistance\":{supportDistance.ToString("R", culture)}," +
                              $"\"capsuleRadius\":{capsule.radius.ToString("R", culture)}," +
                              $"\"capsuleHeight\":{capsule.height.ToString("R", culture)}," +
                              $"\"positionX\":{position.x.ToString("R", culture)}," +
                              $"\"positionY\":{position.y.ToString("R", culture)}," +
                              $"\"positionZ\":{position.z.ToString("R", culture)}," +
                              $"\"desiredX\":{desiredVelocity.x.ToString("R", culture)}," +
                              $"\"desiredY\":{desiredVelocity.y.ToString("R", culture)}," +
                              $"\"desiredZ\":{desiredVelocity.z.ToString("R", culture)}," +
                              $"\"velocityX\":{velocity.x.ToString("R", culture)}," +
                              $"\"velocityY\":{velocity.y.ToString("R", culture)}," +
                              $"\"velocityZ\":{velocity.z.ToString("R", culture)}," +
                              $"\"sleeping\":{body.Rigidbody.IsSleeping().ToString().ToLowerInvariant()}" + "}}";
                UnityWebRequest.Post("http://127.0.0.1:7783/event", payload, "application/json")
                    .SendWebRequest();
            }
#endif
#endregion
            if (grounded)
            {
                if (physicalStepActive)
                {
                    physicalStepSeconds += Time.fixedDeltaTime;
                    var targetPosition = physicalStepOrigin + physicalStepOffset *
                        Mathf.SmoothStep(0, 1, PhysicalStepProgress);
                    externalResponseVelocity = Vector3.ClampMagnitude(
                        Vector3.ProjectOnPlane(targetPosition - Position, Vector3.up) * 12f, .22f);
                    if (physicalStepSeconds >= .62f)
                        EndPhysicalStep();
                }
                var target = Vector3.ProjectOnPlane(
                    desiredVelocity + externalResponseVelocity, normal);
                var actual = Vector3.ProjectOnPlane(body.Rigidbody.velocity, normal);
                var acceleration = handHoldDrive ? handHoldAcceleration : 14f;
                body.AddForce(Vector3.ClampMagnitude((target - actual) / Time.fixedDeltaTime, acceleration),
                    ForceMode.Acceleration);
            }
            if (!physicalStepActive)
                externalResponseVelocity = Vector3.MoveTowards(
                    externalResponseVelocity, Vector3.zero,
                    1.4f * Time.fixedDeltaTime);
            UpdateFacing(Vector3.ProjectOnPlane(
                body.Rigidbody.velocity, Vector3.up).magnitude);
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

#region debug-point A-E:bed-stall-collision
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Mathf.Abs(normal.y) <= .7f &&
                Time.unscaledTime >= debugNextBedCollisionReport)
            {
                debugNextBedCollisionReport = Time.unscaledTime + .25f;
                var debugName = other != null
                    ? other.name.Replace("\"", "'") : "none";
                var debugObstacle = other != null
                    ? other.GetComponentInParent<NavMeshObstacle>() : null;
                var debugPayload =
                    $"{{\"sessionId\":\"avatar-bed-corner-stall\",\"runId\":\"post-fix\"," +
                    $"\"hypothesisId\":\"A-E\",\"location\":\"AvatarMotor.OnCollisionStay\"," +
                    $"\"msg\":\"[DEBUG] navigation collision\",\"data\":{{" +
                    $"\"collider\":\"{debugName}\",\"normal\":{JsonUtility.ToJson(normal)}," +
                    $"\"actualVelocity\":{JsonUtility.ToJson(body.Rigidbody.velocity)}," +
                    $"\"desiredVelocity\":{JsonUtility.ToJson(desiredVelocity)}," +
                    $"\"navObstacle\":{(debugObstacle != null).ToString().ToLowerInvariant()}," +
                    $"\"blockSeconds\":{physicalBlockSeconds}," +
                    $"\"avoidanceSeconds\":{physicalAvoidanceSeconds}," +
                    $"\"avoidanceTotal\":{physicalAvoidanceTotalSeconds}}}}}";
#if UNITY_EDITOR
                const string debugEndpoint = "http://127.0.0.1:7778/event";
#else
                const string debugEndpoint = "http://127.0.0.1:7778/event";
#endif
                var debugRequest = UnityWebRequest.Post(
                    debugEndpoint, debugPayload, "application/json");
                debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
            }
#endif
#endregion

#region debug-point C:movement-collision
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Mathf.Abs(normal.y) > .7f ||
                Time.unscaledTime < debugNextMovementCollisionReport) return;
            debugNextMovementCollisionReport = Time.unscaledTime + .5f;
            var item = other != null ? other.GetComponentInParent<GameItem>() : null;
            var obstacle = other != null ? other.GetComponentInParent<NavMeshObstacle>() : null;
            var name = other != null ? other.name.Replace("\"", "'") : "none";
            var state = item != null && item.Instance != null
                ? item.Instance.ControlState.ToString() : "none";
            var payload =
                $"{{\"sessionId\":\"movement-blocked\",\"runId\":\"post-fix-2\"," +
                $"\"hypothesisId\":\"C\",\"location\":\"AvatarMotor.OnCollisionStay\"," +
                $"\"msg\":\"[DEBUG] movement collision\",\"data\":{{\"collider\":\"{name}\"," +
                $"\"itemState\":\"{state}\",\"navObstacle\":{(obstacle != null).ToString().ToLowerInvariant()}," +
                $"\"normal\":{JsonUtility.ToJson(normal)},\"impulse\":{JsonUtility.ToJson(collision.impulse)}," +
                $"\"blockSeconds\":{physicalBlockSeconds}," +
                $"\"avoidanceSeconds\":{physicalAvoidanceSeconds}," +
                $"\"position\":{JsonUtility.ToJson(Position)}}}}}";
#if UNITY_EDITOR
            const string endpoint = "http://127.0.0.1:7794/event";
#else
            const string endpoint = "http://127.0.0.1:7794/event";
#endif
            var request = UnityWebRequest.Post(endpoint, payload, "application/json");
            request.SendWebRequest().completed += _ => request.Dispose();
#endif
#endregion
        }

        private void OnCollisionExit(Collision collision)
        {
            if (collision != null && collision.collider == physicalBlocker)
                physicalBlockSeconds = 0;
        }

        private void SetAnimationSpeed(float value)
        {
            if (animator != null)
            {
                animator.SetFloat(speedParameter,
                    value < AnimationIdleSpeed ? 0 : value);
                animator.SetFloat(walkPlaybackRateParameter,
                    value < AnimationIdleSpeed ? 1 : value / WalkReferenceSpeed);
            }
        }

        private void Update()
        {
            if (body == null || animator == null) return;
            // Update once per rendered frame from physics, including braking, docking
            // and hand holding. A blocked capsule must not keep advancing the gait.
            SetAnimationSpeed(body.IsPaused || contact ? 0 :
                Vector3.ProjectOnPlane(body.Rigidbody.velocity, Vector3.up).magnitude);
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

            var finishingStep = turnStepPlaying && !animator.IsInTransition(0) &&
                                animator.GetCurrentAnimatorStateInfo(0).normalizedTime >=
                                TurnStepEndTime;
            var delta = sample.DeltaAngle;
            // Docking needs exact facing even when the residual yaw is below the step threshold.
            if (dockFacing && !sample.IsTurning && !turnStepPlaying)
                delta = Mathf.Sign(remaining) * Mathf.Min(
                    Mathf.Abs(remaining), TurnStepSettleSpeed * Time.fixedDeltaTime);
            if (sample.IsTurning && (!turnStepPlaying || finishingStep))
            {
                var settleThreshold = turnStepActive
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
            SetAnimationTurnRate(turnStepPlaying && !finishingStep
                ? sample.NormalizedRate : 0);
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
            SetTurnStepActive(false);
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

        private void SetTurnStepActive(bool value)
        {
            turnStepActive = value;
            if (animator != null && hasTurnStepActiveParameter)
                animator.SetBool(turnStepActiveParameter, value);
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
                !hasTurnStepLeftParameter || !hasTurnStepRightParameter ||
                !hasTurnStepActiveParameter)
                return;

            if (inStep)
            {
                if (!turnStepActive || animator.IsInTransition(0)) return;
                if (animator.GetCurrentAnimatorStateInfo(0).normalizedTime <
                    TurnStepEndTime)
                {
                    turnStepRequested = false;
                    turnStepRecoverySeconds = TurnStepRecoverySeconds;
                    return;
                }
                if (!sample.IsTurning &&
                    Mathf.Abs(sample.RemainingAngle) <= TurnStepCompleteAngle)
                {
                    SetTurnStepActive(false);
                    turnStepRequested = false;
                    return;
                }
                // Keep the end pose during recovery; the next corrective step
                // continues directly, without yielding this facing episode to Idle.
                if (turnStepRequested || turnStepRecoverySeconds > 0 ||
                    Mathf.Abs(sample.RemainingAngle) < RepeatTurnStepAngle)
                    return;
            }
            else if (animator.IsInTransition(0) || turnStepRequested)
                return;
            if (!sample.IsTurning ||
                Mathf.Abs(sample.RemainingAngle) < TurnInPlaceMotion.EnterAngle ||
                Mathf.Abs(sample.NormalizedRate) < .02f)
                return;

            SetTurnStepActive(true);
            animator.ResetTrigger(sample.NormalizedRate < 0
                ? turnStepRightParameter : turnStepLeftParameter);
            animator.SetTrigger(sample.NormalizedRate < 0
                ? turnStepLeftParameter : turnStepRightParameter);
            turnStepRequested = true;
        }

#region debug-point A-C:animation-state
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private void LateUpdate()
        {
            if (animator == null || body == null ||
                Time.unscaledTime < debugNextAnimationReport) return;
            debugNextAnimationReport = Time.unscaledTime + .5f;
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            var firstName = clips.Length > 0 && clips[0].clip != null
                ? clips[0].clip.name : "none";
            var secondName = clips.Length > 1 && clips[1].clip != null
                ? clips[1].clip.name : "none";
            var firstWeight = clips.Length > 0 ? clips[0].weight : 0;
            var secondWeight = clips.Length > 1 ? clips[1].weight : 0;
            var leftDelta = debugLeftLeg != null
                ? Quaternion.Angle(debugPreviousLeftLeg, debugLeftLeg.localRotation) : -1;
            var rightDelta = debugRightLeg != null
                ? Quaternion.Angle(debugPreviousRightLeg, debugRightLeg.localRotation) : -1;
            if (debugLeftLeg != null) debugPreviousLeftLeg = debugLeftLeg.localRotation;
            if (debugRightLeg != null) debugPreviousRightLeg = debugRightLeg.localRotation;
            var velocity = Vector3.ProjectOnPlane(
                body.Rigidbody.velocity, Vector3.up).magnitude;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var controllerName = animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.name : "null";
            var payload = $"{{\"sessionId\":\"avatar-stuck-walk\",\"runId\":\"post-fix\"," +
                          $"\"hypothesisId\":\"A-C\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                          $"\"location\":\"AvatarMotor.LateUpdate\",\"msg\":\"[DEBUG] visible locomotion state\"," +
                          $"\"data\":{{\"animatorId\":{animator.GetInstanceID()},\"controller\":\"{controllerName}\"," +
                          $"\"animatorEnabled\":{animator.enabled.ToString().ToLowerInvariant()}," +
                          $"\"animatorPlaybackSpeed\":{animator.speed.ToString("R", culture)}," +
                          $"\"motorMoving\":{IsMoving.ToString().ToLowerInvariant()}," +
                          $"\"handHoldDrive\":{handHoldDrive.ToString().ToLowerInvariant()}," +
                          $"\"contact\":{contact.ToString().ToLowerInvariant()}," +
                          $"\"rigidbodySpeed\":{velocity.ToString("R", culture)}," +
                          $"\"desiredSpeed\":{desiredVelocity.magnitude.ToString("R", culture)}," +
                          $"\"speedParameter\":{animator.GetFloat(speedParameter).ToString("R", culture)}," +
                          $"\"clipCount\":{clips.Length},\"clip0\":\"{firstName}\"," +
                          $"\"weight0\":{firstWeight.ToString("R", culture)},\"clip1\":\"{secondName}\"," +
                          $"\"weight1\":{secondWeight.ToString("R", culture)}," +
                          $"\"leftLegDelta\":{leftDelta.ToString("R", culture)}," +
                          $"\"rightLegDelta\":{rightDelta.ToString("R", culture)}}}}}";
#if UNITY_EDITOR
            const string debugEndpoint = "http://127.0.0.1:7785/event";
#else
            const string debugEndpoint = "http://127.0.0.1:7785/event";
#endif
            UnityWebRequest.Post(
                debugEndpoint, payload, "application/json").SendWebRequest();
        }
#endif
#endregion

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
            // Keep the foot end, but stop the torso proxy below the neck: the
            // head rests on the pillow instead of blocking the torso landing.
            capsule.height = 1.4f;
            capsule.center = new Vector3(0, .725f, .145f);
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
