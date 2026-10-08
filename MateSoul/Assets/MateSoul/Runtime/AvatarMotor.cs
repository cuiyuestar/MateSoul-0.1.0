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
        [SerializeField] private Animator animator;
        [SerializeField] private float speed = 1.1f;
        [SerializeField] private float turnSpeed = 240f;
        [SerializeField] private float handHoldAcceleration = 2.2f;
        private PhysicsBody body;
        private CapsuleCollider capsule;
        private Vector3[] corners;
        private int corner;
        private int speedParameter;
        private bool contact;
        private Vector3 contactExit;
        private Vector3 desiredVelocity;
        private Quaternion facing;
        private bool handHoldDrive;
        private readonly HashSet<int> contactIgnoredSurfaceSources = new HashSet<int>();
        private readonly RaycastHit[] groundHits = new RaycastHit[12];
#region debug-point B:grounded-motion
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        private float debugNextMotorReport;
#endif
#endregion
        public PhysicsBody Body => body;
        public string ContactPosture => !contact ? null :
            Vector3.Dot(body.Pose.rotation * Vector3.up, Vector3.up) < .7f ? "lying" : "seated";
        public Vector3 Position => body != null ? body.Pose.position : transform.position;
        public bool IsMoving => corners != null;

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
            facing = body.Pose.rotation;
            speedParameter = Animator.StringToHash("Speed");
            animator.applyRootMotion = false;
        }

        public void Begin(Vector3[] path)
        {
            if (path == null || path.Length == 0) throw new ArgumentException("A complete path is required.");
            if (contact) EndBodyContact();
            handHoldDrive = false;
            corners = path;
            corner = 0;
        }

        public bool Tick(float deltaSeconds)
        {
            if (corners == null) return true;
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
            var actualSpeed = Vector3.ProjectOnPlane(body.Rigidbody.velocity, Vector3.up).magnitude;
            animator.SetFloat(speedParameter, actualSpeed);
            return false;
        }

        public void Face(Vector3 target, float deltaSeconds)
        {
            var direction = Vector3.ProjectOnPlane(target - Position, Vector3.up);
            if (direction.sqrMagnitude < .001f) return;
            facing = Quaternion.LookRotation(direction);
        }

        public void Stop()
        {
            corners = null;
            handHoldDrive = false;
            desiredVelocity = Vector3.zero;
            if (animator != null) animator.SetFloat(speedParameter, 0);
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
            if (animator != null) animator.SetFloat(speedParameter, 0);
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
                UnityWebRequest.Post("http://10.0.2.2:7783/event", payload, "application/json")
                    .SendWebRequest();
            }
#endif
#endregion
            if (grounded)
            {
                var target = Vector3.ProjectOnPlane(desiredVelocity, normal);
                var actual = Vector3.ProjectOnPlane(body.Rigidbody.velocity, normal);
                var acceleration = handHoldDrive ? handHoldAcceleration : 14f;
                body.AddForce(Vector3.ClampMagnitude((target - actual) / Time.fixedDeltaTime, acceleration),
                    ForceMode.Acceleration);
            }
            body.RotateTowards(facing, turnSpeed * Time.fixedDeltaTime);
            if (handHoldDrive && animator != null)
                animator.SetFloat(speedParameter,
                    Vector3.ProjectOnPlane(body.Rigidbody.velocity, Vector3.up).magnitude);
        }

        public void BeginBodyContact(Vector3 exit, float seatHeight, int seatSourceId = -1)
        {
            Stop();
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
