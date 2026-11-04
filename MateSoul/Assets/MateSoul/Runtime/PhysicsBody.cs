using System;
using System.Collections.Generic;
using System.Linq;
using MateSoul.RoomNavigation;
using UnityEngine;

namespace MateSoul.Runtime
{
    [Serializable]
    public struct BodySettings
    {
        [Min(.001f)] public float mass;
        public PhysicMaterial material;
        [Min(0)] public float linearDrag;
        [Min(0)] public float angularDrag;

        public BodySettings(float kilograms, PhysicMaterial surface, float drag = .02f,
            float rotationDrag = .05f)
        {
            mass = kilograms; material = surface;
            linearDrag = drag; angularDrag = rotationDrag;
        }

        public void Validate()
        {
            if (!PhysicsBody.IsFinite(mass) || mass < .001f || mass > 10000 ||
                material == null || !PhysicsBody.IsFinite(linearDrag) || linearDrag < 0 ||
                !PhysicsBody.IsFinite(angularDrag) || angularDrag < 0)
                throw new ArgumentException("Physics content requires a finite mass, material and non-negative drag.");
            if (!PhysicsBody.IsFinite(material.staticFriction) || material.staticFriction < 0 ||
                !PhysicsBody.IsFinite(material.dynamicFriction) || material.dynamicFriction < 0 ||
                !PhysicsBody.IsFinite(material.bounciness) || material.bounciness < 0 || material.bounciness > 1)
                throw new ArgumentException("Physics material coefficients must be finite and non-negative.");
        }
    }

    // The shared owner of Rigidbody state. XR and gameplay submit motion targets here.
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
    public sealed partial class PhysicsBody : MonoBehaviour
    {
        [SerializeField] private BodySettings settings;
        private Rigidbody rigidBody;
        private Collider[] shapes;
        private readonly Collider[] overlaps = new Collider[128];
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private Pose followTarget;
        private Vector3 followVelocity, followAngularVelocity;
        private Vector3 pausedVelocity, pausedAngularVelocity;
        private bool initialized, following, contact, docked, wasSleeping;
        private bool forceNeedsContinuousDetection;
        private bool stableFollow, stableCutting, followGravity;
        private RigidbodyInterpolation followInterpolation;
        private float quietTime;
        public Rigidbody Rigidbody => rigidBody != null ? rigidBody : rigidBody = GetComponent<Rigidbody>();
        public IReadOnlyList<Collider> Shapes => shapes;
        public bool IsPaused { get; private set; }
        public bool IsFollowing => following;
        public bool IsStableFollowing => following && stableFollow;
        public bool IsContact => contact;
        public bool IsDocked => docked;
        public bool IsSettled => initialized && !following &&
            (docked || Rigidbody.IsSleeping() || quietTime >= .5f);
        public Pose Pose => new Pose(Rigidbody.position, Rigidbody.rotation);

        private void Awake()
        {
            // Dynamically added components are configured by their creating owner in the same frame.
            if (settings.material != null) Initialize();
        }

        public void Initialize()
        {
            if (!initialized) Configure(settings);
        }

        public void RotateTowards(Quaternion target, float degrees)
            => Rigidbody.MoveRotation(Quaternion.RotateTowards(Rigidbody.rotation, target, degrees));

        public void MoveContact(Pose target)
        {
            if (!contact || IsPaused) throw new InvalidOperationException("Contact motion has no active owner.");
            Rigidbody.MovePosition(target.position);
            Rigidbody.MoveRotation(target.rotation);
        }

        public void Configure(BodySettings value)
        {
            value.Validate();
            var rb = Rigidbody;
            settings = value;
            RefreshColliderCache();
            rb.mass = value.mass;
            rb.drag = value.linearDrag;
            rb.angularDrag = value.angularDrag;
            rb.useGravity = true;
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            // Covers the first simulation after placement/release, before the
            // velocity-based sweep selection gets its next FixedUpdate.
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.maxAngularVelocity = 30;
            rb.maxDepenetrationVelocity = 2;
            rb.solverIterations = 8;
            rb.solverVelocityIterations = 2;
            docked = false;
            initialized = true;
        }

        public void RefreshColliders()
        {
            if (settings.material == null)
                throw new InvalidOperationException(
                    $"{name}: physics settings must be configured before collider refresh.");
            RefreshColliderCache();
            if (!Rigidbody.isKinematic)
            {
                Rigidbody.ResetCenterOfMass();
                Rigidbody.ResetInertiaTensor();
            }
        }

        private void RefreshColliderCache()
        {
            var rb = Rigidbody;
            if (GetComponentsInChildren<Rigidbody>(true).Any(body => body != rb))
                throw new InvalidOperationException(
                    $"{name}: compound items must have one root Rigidbody.");
            shapes = GetComponentsInChildren<Collider>(true)
                .Where(shape => shape.enabled && !shape.isTrigger)
                .ToArray();
            if (shapes.Length == 0 || shapes.Any(shape =>
                    !(shape is BoxCollider || shape is SphereCollider ||
                      shape is CapsuleCollider ||
                      shape is MeshCollider mesh && mesh.convex)))
                throw new InvalidOperationException(
                    $"{name}: a dynamic body needs primitive or convex colliders.");
            foreach (var shape in shapes)
                shape.sharedMaterial = settings.material;
            ClearFollowScratch();
            if (followShapes != null) ResizeFollowShapes();
        }

        public void AddForce(Vector3 force, ForceMode mode = ForceMode.Force)
        {
            if (!IsFinite(force)) throw new ArgumentException("Force must be finite.");
            if (IsPaused || contact || IsInteractionControlled || IsStableFollowing) return;
            var velocityChange = mode == ForceMode.VelocityChange ? force :
                mode == ForceMode.Impulse ? force / Rigidbody.mass :
                mode == ForceMode.Acceleration ? force * Time.fixedDeltaTime :
                force * (Time.fixedDeltaTime / Rigidbody.mass);
            if ((Rigidbody.velocity + velocityChange).sqrMagnitude > 4)
            {
                forceNeedsContinuousDetection = true;
                Rigidbody.collisionDetectionMode = shapes.All(shape => !(shape is MeshCollider))
                    ? CollisionDetectionMode.ContinuousDynamic
                    : CollisionDetectionMode.ContinuousSpeculative;
            }
            Rigidbody.AddForce(force, mode);
        }

        public void AddTorque(Vector3 torque, ForceMode mode = ForceMode.Force)
        {
            if (!IsFinite(torque)) throw new ArgumentException("Torque must be finite.");
            if (!IsPaused && !contact && !IsInteractionControlled && !IsStableFollowing)
                Rigidbody.AddTorque(torque, mode);
        }

        // A tracked hand requests bounded contact motion; gameplay forces remain independent.
        public void ApplyHandPush(Vector3 direction, float approachSpeed,
            float penetration, Vector3 point)
        {
            if (!IsFinite(direction) || !IsFinite(point) ||
                !IsFinite(approachSpeed) || !IsFinite(penetration))
                throw new ArgumentException("Hand contact must be finite.");
            if (IsPaused || contact || following || docked ||
                IsInteractionControlled || Rigidbody.isKinematic)
                return;
            direction = direction.normalized;
            var rb = Rigidbody;
            var bodySpeed = Vector3.Dot(rb.GetPointVelocity(point), direction);
            var closingSpeed = Mathf.Clamp(approachSpeed - bodySpeed - .035f, 0, 4f);
            // A tracked hand has finite effective mass. Transfer only closing
            // momentum; resting penetration cannot keep accelerating an item.
            const float handMass = .35f;
            var reducedMass = handMass * rb.mass / (handMass + rb.mass);
            var impulse = reducedMass * closingSpeed;
            if (impulse <= 0) return;
            AddForce(direction * impulse, ForceMode.Impulse);
            var torque = Vector3.Cross(point - Rigidbody.worldCenterOfMass,
                direction * impulse);
            AddTorque(Vector3.ClampMagnitude(torque, impulse * .15f), ForceMode.Impulse);
        }

        public void BeginFollow(Pose target, bool stableGrip = false, bool cuttingTool = false)
        {
            ValidatePose(target);
            if (docked) Undock();
            if (IsPaused || contact || IsInteractionControlled)
                throw new InvalidOperationException("Body is unavailable for grabbing.");
            following = true;
            stableFollow = stableGrip;
            stableCutting = cuttingTool;
            followTarget = target;
            followVelocity = followAngularVelocity = Vector3.zero;
            quietTime = 0;
            if (stableFollow)
            {
                EnsureFollowScratch();
                followGravity = Rigidbody.useGravity;
                followInterpolation = Rigidbody.interpolation;
                SetKinematic(true);
                Rigidbody.useGravity = false;
                Rigidbody.interpolation = RigidbodyInterpolation.None;
                // Manual occupancy checks below avoid an infinitely heavy
                // tracked tool flinging dynamic objects through a table.
                Rigidbody.detectCollisions = false;
            }
            Rigidbody.WakeUp();
        }

        public void SetFollowTarget(Pose target)
            => SetFollowTarget(target, Vector3.zero, Vector3.zero);

        public void SetFollowTarget(Pose target, Vector3 velocity, Vector3 angularVelocity)
        {
            ValidatePose(target);
            if (!IsFinite(velocity) || !IsFinite(angularVelocity))
                throw new ArgumentException("Follow motion must be finite.");
            followTarget = target;
            followVelocity = Vector3.ClampMagnitude(velocity, 4f);
            followAngularVelocity = Vector3.ClampMagnitude(angularVelocity, 12f);
            if (IsStableFollowing && !IsPaused) ApplyStableFollow(target);
        }

        private void ApplyStableFollow(Pose target)
        {
            var from = Pose;
            var resolved = from;
            var movement = target.position - from.position;
            var distance = movement.magnitude;
            var travelLimit = distance;
            // Thin bodies can cross a mesh between occupancy samples. Sweep
            // translation continuously while retaining manual rotation checks.
            if (distance > .0001f)
                foreach (var hit in Rigidbody.SweepTestAll(
                             movement / distance, distance + .002f, QueryTriggerInteraction.Ignore))
                {
                    if (Vector3.Dot(movement, hit.normal) >= 0) continue;
                    if (stableCutting && hit.collider.GetComponentInParent<GameItem>() is GameItem cuttable &&
                        cuttable.Definition != null && cuttable.Definition.Cuttable.enabled)
                        continue;
                    travelLimit = Mathf.Min(travelLimit, Mathf.Max(0, hit.distance - .002f));
                }
            var count = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(
                distance / .025f,
                Quaternion.Angle(from.rotation, target.rotation) / 10f)), 1, 60);
            var maximumAmount = distance > .0001f ? travelLimit / distance : 1f;
            if (PrepareFollowCandidates(from, target, count, maximumAmount))
            {
                for (var index = 0; index < followSampleCount; index++)
                {
                    var sample = followSamples[index];
                    if (!CanOccupyFollowSample(sample)) break;
                    resolved = sample;
                }
            }
            Place(resolved);
            if (Vector3.Distance(resolved.position, target.position) > .003f)
                followVelocity = followAngularVelocity = Vector3.zero;
        }

        public void EndFollow(bool discardVelocity = false)
        {
#region debug-point A:elevated-release-entry
#if UNITY_EDITOR
            if (Environment.GetEnvironmentVariable("MATESOUL_ELEVATED_RUN") != null)
                try { using (var client = new System.Net.WebClient()) client.UploadString("http://127.0.0.1:7833/event", $"{{\"sessionId\":\"elevated-hand-release\",\"runId\":\"{Environment.GetEnvironmentVariable("MATESOUL_ELEVATED_RUN")}\",\"hypothesisId\":\"A\",\"location\":\"PhysicsBody.EndFollow.entry\",\"msg\":\"[DEBUG] release entry\",\"data\":{{\"name\":\"{name}\",\"stable\":{stableFollow.ToString().ToLowerInvariant()},\"kinematic\":{Rigidbody.isKinematic.ToString().ToLowerInvariant()},\"collisions\":{Rigidbody.detectCollisions.ToString().ToLowerInvariant()},\"pose\":{JsonUtility.ToJson(Pose.position)},\"shapeCount\":{shapes.Length}}}}}"); } catch (System.Net.WebException) { }
#endif
#endregion
            if (stableFollow)
            {
                Rigidbody.detectCollisions = true;
                Rigidbody.useGravity = followGravity;
                Rigidbody.interpolation = followInterpolation;
                stableFollow = stableCutting = false;
                SetKinematic(IsPaused || contact || docked || IsInteractionControlled);
                if (!Rigidbody.isKinematic)
                {
                    Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    Rigidbody.velocity = discardVelocity ? Vector3.zero : followVelocity;
                    Rigidbody.angularVelocity = discardVelocity ? Vector3.zero : followAngularVelocity;
                }
            }
            following = false; // Actual collision-resolved velocities are the release velocities.
            ClearFollowScratch();
            followVelocity = followAngularVelocity = Vector3.zero;
            if (discardVelocity && !Rigidbody.isKinematic)
            {
                Rigidbody.velocity = Vector3.zero;
                Rigidbody.angularVelocity = Vector3.zero;
            }
            quietTime = 0;
#region debug-point A:elevated-release-exit
#if UNITY_EDITOR
            if (Environment.GetEnvironmentVariable("MATESOUL_ELEVATED_RUN") != null)
                try { using (var client = new System.Net.WebClient()) client.UploadString("http://127.0.0.1:7833/event", $"{{\"sessionId\":\"elevated-hand-release\",\"runId\":\"{Environment.GetEnvironmentVariable("MATESOUL_ELEVATED_RUN")}\",\"hypothesisId\":\"A\",\"location\":\"PhysicsBody.EndFollow.exit\",\"msg\":\"[DEBUG] release exit\",\"data\":{{\"name\":\"{name}\",\"following\":{following.ToString().ToLowerInvariant()},\"kinematic\":{Rigidbody.isKinematic.ToString().ToLowerInvariant()},\"collisions\":{Rigidbody.detectCollisions.ToString().ToLowerInvariant()},\"gravity\":{Rigidbody.useGravity.ToString().ToLowerInvariant()},\"velocity\":{JsonUtility.ToJson(Rigidbody.velocity)},\"mode\":\"{Rigidbody.collisionDetectionMode}\"}}}}"); } catch (System.Net.WebException) { }
#endif
#endregion
        }

        public void EndHandFollow(Vector3 recentVelocity, Vector3 recentAngularVelocity)
        {
            if (!IsFinite(recentVelocity) || !IsFinite(recentAngularVelocity))
                throw new ArgumentException("Release motion must be finite.");
            var canThrow = following && !IsPaused && (!Rigidbody.isKinematic || stableFollow);
            EndFollow(stableFollow);
            if (!canThrow) return;
            var velocity = Vector3.ClampMagnitude(recentVelocity, 4f);
            // Keep fresh hand momentum in free space, but do not inject it through
            // a wall/table that physically blocks the released body.
            if (velocity.sqrMagnitude > .0001f)
            {
                var horizon = Mathf.Max(.02f, Time.fixedDeltaTime);
                var direction = velocity.normalized;
                foreach (var hit in Rigidbody.SweepTestAll(direction,
                             velocity.magnitude * horizon + .005f,
                             QueryTriggerInteraction.Ignore))
                {
                    if (Vector3.Dot(direction, hit.normal) >= 0) continue;
                    var allowed = Mathf.Max(0, hit.distance - .005f) / horizon;
                    var intoSurface = -Vector3.Dot(velocity, hit.normal);
                    velocity += hit.normal * Mathf.Max(0, intoSurface - allowed);
                }
            }
            Rigidbody.velocity = velocity;
            Rigidbody.angularVelocity = Vector3.ClampMagnitude(recentAngularVelocity, 12f);
            if (velocity.sqrMagnitude > 4f)
                Rigidbody.collisionDetectionMode = shapes.All(shape => !(shape is MeshCollider))
                    ? CollisionDetectionMode.ContinuousDynamic
                    : CollisionDetectionMode.ContinuousSpeculative;
#region debug-point A-C:elevated-release-motion
#if UNITY_EDITOR
            if (Environment.GetEnvironmentVariable("MATESOUL_ELEVATED_RUN") != null)
                try { using (var client = new System.Net.WebClient()) client.UploadString("http://127.0.0.1:7833/event", $"{{\"sessionId\":\"elevated-hand-release\",\"runId\":\"{Environment.GetEnvironmentVariable("MATESOUL_ELEVATED_RUN")}\",\"hypothesisId\":\"A-C\",\"location\":\"PhysicsBody.EndHandFollow.exit\",\"msg\":\"[DEBUG] fresh release motion\",\"data\":{{\"name\":\"{name}\",\"requested\":{JsonUtility.ToJson(recentVelocity)},\"velocity\":{JsonUtility.ToJson(Rigidbody.velocity)},\"angular\":{JsonUtility.ToJson(Rigidbody.angularVelocity)},\"mode\":\"{Rigidbody.collisionDetectionMode}\"}}}}"); } catch (System.Net.WebException) { }
#endif
#endregion
        }

        public void SetContact(bool value)
        {
            if (IsPaused) throw new InvalidOperationException("Cannot change contact ownership while paused.");
            if (IsInteractionControlled) throw new InvalidOperationException("An interaction owns this body.");
            if (docked) Undock();
            if (following) EndFollow(true);
            following = false;
            contact = value;
            SetKinematic(value);
            quietTime = 0;
        }

        public void SetPaused(bool value)
        {
            if (IsPaused == value) return;
            var rb = Rigidbody;
            if (value)
            {
                pausedVelocity = rb.velocity;
                pausedAngularVelocity = rb.angularVelocity;
                wasSleeping = rb.IsSleeping();
                SetKinematic(true);
            }
            else
            {
                SetKinematic(contact || docked || IsInteractionControlled || stableFollow);
                if (!contact && !docked && !IsInteractionControlled && !stableFollow)
                {
                    rb.velocity = pausedVelocity;
                    rb.angularVelocity = pausedAngularVelocity;
                    if (wasSleeping) rb.Sleep();
                }
            }
            IsPaused = value;
        }

        private void SetKinematic(bool value)
        {
            if (value) Rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            Rigidbody.isKinematic = value;
            if (value) Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        public void Place(Pose pose)
        {
            ValidatePose(pose);
            Rigidbody.position = pose.position;
            Rigidbody.rotation = pose.rotation.normalized;
            // Initialization/validated contact transitions only, never normal release.
            transform.SetPositionAndRotation(Rigidbody.position, Rigidbody.rotation);
            if (!Rigidbody.isKinematic)
            {
                Rigidbody.velocity = Vector3.zero;
                Rigidbody.angularVelocity = Vector3.zero;
            }
            quietTime = 0;
        }

        public void Dock(Pose pose)
        {
            ValidatePose(pose);
            if (IsPaused || following || contact || IsInteractionControlled)
                throw new InvalidOperationException(
                    "Body is unavailable for docking.");
            Place(pose);
            var rb = Rigidbody;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity = false;
            docked = true;
            SetKinematic(true);
            quietTime = 0;
        }

        public void Undock()
        {
            if (!docked) return;
            docked = false;
            if (IsPaused) return;
            var rb = Rigidbody;
            rb.useGravity = true;
            SetKinematic(contact || IsInteractionControlled);
            if (!rb.isKinematic)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.WakeUp();
            }
            quietTime = 0;
        }

        public bool CanOccupy(Pose pose, float tolerance = .012f, Transform ignoredRoot = null,
            ISet<int> ignoredRoomSurfaceSources = null, Collider ignoredCollider = null,
            Transform ignoredColliderRoot = null, Action<Collider, float> blocked = null,
            bool ignoreCuttable = false)
        {
            ValidatePose(pose);
            foreach (var shape in shapes)
            {
                if (!shape.enabled || !shape.gameObject.activeInHierarchy ||
                    shape.GetComponent<AvatarBodyPartCollider>() != null)
                    continue;
                var relativePosition = Quaternion.Inverse(transform.rotation) *
                                       (shape.transform.position - transform.position);
                var position = pose.position + pose.rotation * relativePosition;
                var rotation = pose.rotation * Quaternion.Inverse(transform.rotation) * shape.transform.rotation;
                // Rotation-independent broad phase includes off-centre compound shapes.
                var radius = shape.bounds.extents.magnitude +
                             Vector3.Distance(shape.bounds.center, shape.transform.position);
                var count = Physics.OverlapSphereNonAlloc(position, radius + .02f, overlaps,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore);
                if (count == overlaps.Length) return false;
                for (var index = 0; index < count; index++)
                {
                    var other = overlaps[index];
                    if (other == ignoredCollider || other.attachedRigidbody == Rigidbody ||
                        ignoredRoot != null && other.transform.IsChildOf(ignoredRoot) ||
                        ignoredColliderRoot != null &&
                        other.transform.IsChildOf(ignoredColliderRoot) ||
                        Physics.GetIgnoreCollision(shape, other)) continue;
                    if (ignoreCuttable && other.GetComponentInParent<GameItem>() is GameItem cuttable &&
                        cuttable.Definition != null && cuttable.Definition.Cuttable.enabled)
                        continue;
                    var roomSurface = other.GetComponent<RoomSurfaceCollider>();
                    if (roomSurface != null && ignoredRoomSurfaceSources != null &&
                        ignoredRoomSurfaceSources.Contains(roomSurface.SourceId)) continue;
                    if (Physics.ComputePenetration(shape, position, rotation, other,
                            other.transform.position, other.transform.rotation, out _, out var depth) &&
                        depth > tolerance)
                    {
                        blocked?.Invoke(other, depth);
#region debug-point B-C-D:saved-item-blocker
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
                        if (IsPaused && GetComponent<GameItem>() != null)
                        {
                            var debugPayload =
                                $"{{\"sessionId\":\"saved-items-restore-loss\",\"runId\":\"post-fix\"," +
                                $"\"hypothesisId\":\"B-C-D\",\"location\":\"PhysicsBody.CanOccupy\"," +
                                $"\"msg\":\"[DEBUG] restored item penetration blocker\",\"data\":{{" +
                                $"\"body\":\"{name.Replace("\"", "'")}\"," +
                                $"\"shape\":\"{shape.name.Replace("\"", "'")}\"," +
                                $"\"blocker\":\"{other.name.Replace("\"", "'")}\"," +
                                $"\"blockerRoot\":\"{other.transform.root.name.Replace("\"", "'")}\"," +
                                $"\"blockerBody\":\"{(other.attachedRigidbody == null ? "none" : other.attachedRigidbody.name.Replace("\"", "'"))}\"," +
                                $"\"blockerIsSelf\":{(other.attachedRigidbody == Rigidbody).ToString().ToLowerInvariant()}," +
                                $"\"blockerIsItem\":{(other.GetComponentInParent<GameItem>() != null).ToString().ToLowerInvariant()}," +
                                $"\"blockerSource\":{(roomSurface == null ? -1 : roomSurface.SourceId)}," +
                                $"\"depth\":{depth.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}," +
                                $"\"tolerance\":{tolerance.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}," +
                                $"\"pose\":{JsonUtility.ToJson(pose.position)}}}}}";
                            var debugRequest = UnityEngine.Networking.UnityWebRequest.Post(
                                "http://127.0.0.1:7781/event", debugPayload, "application/json");
                            debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
                        }
#endif
#endregion
#region debug-point A-B-C-D:avatar-contact-blocker
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        if (contact && GetComponent<AvatarMotor>() != null)
                        {
                            var culture = System.Globalization.CultureInfo.InvariantCulture;
                            var payload = $"{{\"sessionId\":\"chair-contact-false-block\",\"runId\":\"post-fix\"," +
                                          $"\"hypothesisId\":\"A-B-C-D\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                                          $"\"location\":\"PhysicsBody.CanOccupy\",\"msg\":\"[DEBUG] avatar contact sweep blocked\",\"data\":{{" +
                                          $"\"body\":\"{name.Replace("\"", "'")}\",\"shape\":\"{shape.name.Replace("\"", "'")}\"," +
                                          $"\"blocker\":\"{other.name.Replace("\"", "'")}\",\"blockerRoot\":\"{other.transform.root.name.Replace("\"", "'")}\"," +
                                          $"\"blockerParent\":\"{(other.transform.parent == null ? "none" : other.transform.parent.name.Replace("\"", "'"))}\"," +
                                          $"\"blockerBody\":\"{(other.attachedRigidbody == null ? "none" : other.attachedRigidbody.name.Replace("\"", "'"))}\"," +
                                          $"\"blockerLayer\":{other.gameObject.layer},\"blockerSourceId\":{(roomSurface == null ? -1 : roomSurface.SourceId)}," +
                                          $"\"depth\":{depth.ToString("R", culture)}," +
                                          $"\"sampleX\":{pose.position.x.ToString("R", culture)},\"sampleY\":{pose.position.y.ToString("R", culture)}," +
                                          $"\"sampleZ\":{pose.position.z.ToString("R", culture)},\"currentX\":{Rigidbody.position.x.ToString("R", culture)}," +
                                          $"\"currentY\":{Rigidbody.position.y.ToString("R", culture)},\"currentZ\":{Rigidbody.position.z.ToString("R", culture)}," +
                                          $"\"transformBodyDelta\":{Vector3.Distance(transform.position, Rigidbody.position).ToString("R", culture)}" +
                                          $"}}}}";
                            UnityEngine.Networking.UnityWebRequest.Post(
                                    Application.isEditor
                                        ? "http://127.0.0.1:7784/event"
                                        : "http://127.0.0.1:7784/event",
                                    payload, "application/json")
                                .SendWebRequest();
                        }
#endif
#endregion
#region debug-point A-B-D:bowling-reset-blocker
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
                        if (GetComponent<BowlingPin>() != null)
                        {
                            var payload = $"{{\"sessionId\":\"bowling-pin-reset-blocked\",\"runId\":\"post-fix\"," +
                                          $"\"hypothesisId\":\"A-B-D\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                                          $"\"location\":\"PhysicsBody.CanOccupy\",\"msg\":\"[DEBUG] bowling pin reset blocked\",\"data\":{{" +
                                          $"\"bodyId\":{GetInstanceID()},\"body\":\"{name.Replace("\"", "'")}\"," +
                                          $"\"shape\":\"{shape.name.Replace("\"", "'")}\"," +
                                          $"\"blocker\":\"{other.name.Replace("\"", "'")}\"," +
                                          $"\"blockerRoot\":\"{other.transform.root.name.Replace("\"", "'")}\"," +
                                          $"\"blockerBody\":\"{(other.attachedRigidbody == null ? "none" : other.attachedRigidbody.name.Replace("\"", "'"))}\"," +
                                          $"\"blockerIsPin\":{(other.GetComponentInParent<BowlingPin>() != null).ToString().ToLowerInvariant()}," +
                                          $"\"depth\":{depth.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}," +
                                          $"\"tolerance\":{tolerance.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}," +
                                          $"\"transformBodyDelta\":{Vector3.Distance(transform.position, Rigidbody.position).ToString("R", System.Globalization.CultureInfo.InvariantCulture)}," +
                                          $"\"target\":{JsonUtility.ToJson(pose.position)}," +
                                          $"\"current\":{JsonUtility.ToJson(Rigidbody.position)}" + "}}";
                            UnityEngine.Networking.UnityWebRequest.Post(
                                    "http://127.0.0.1:7782/event", payload, "application/json")
                                .SendWebRequest();
                        }
#endif
#endregion
                        return false;
                    }
                }
            }
            return true;
        }

        public bool HasSupport(ISet<PhysicsBody> acceptedSupports = null, float probeDistance = .13f)
        {
            var hasBounds = false;
            var bounds = default(Bounds);
            foreach (var shape in shapes)
            {
                if (shape.GetComponent<AvatarBodyPartCollider>() != null)
                    continue;
                if (!hasBounds)
                {
                    bounds = shape.bounds;
                    hasBounds = true;
                }
                else bounds.Encapsulate(shape.bounds);
            }
            if (!hasBounds) return false;
            // Five probes allow rotated objects and feet without treating side contact as support.
            for (var probe = 0; probe < 5; probe++)
            {
                var origin = new Vector3(bounds.center.x, bounds.min.y + .045f, bounds.center.z);
                if (probe > 0)
                    origin += new Vector3((probe % 2 == 0 ? 1 : -1) * bounds.extents.x * .75f,
                        0, (probe > 2 ? 1 : -1) * bounds.extents.z * .75f);
                var count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, probeDistance,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore);
                if (count == hits.Length) return false;
                for (var index = 0; index < count; index++)
                {
                    var hit = hits[index];
                    if (hit.rigidbody == Rigidbody || hit.normal.y < .55f) continue;
                    if (hit.rigidbody == null) return true;
                    var support = hit.rigidbody.GetComponent<PhysicsBody>();
                    if (acceptedSupports == null || support != null && acceptedSupports.Contains(support))
                        return true;
                }
            }
            return false;
        }

        // Authored contact motion is sampled at <=2 cm / 3 degrees, including rotation.
        public bool IsPathClear(Pose from, Pose to, ISet<int> ignoredRoomSurfaceSources = null)
        {
            var steps = Mathf.CeilToInt(Mathf.Max(Vector3.Distance(from.position, to.position) / .02f,
                Quaternion.Angle(from.rotation, to.rotation) / 3f));
            for (var index = 1; index <= Mathf.Max(1, steps); index++)
            {
                var t = (float)index / Mathf.Max(1, steps);
                if (!CanOccupy(new Pose(Vector3.Lerp(from.position, to.position, t),
                        Quaternion.Slerp(from.rotation, to.rotation, t)),
                        ignoredRoomSurfaceSources: ignoredRoomSurfaceSources)) return false;
            }
            return true;
        }

        private void FixedUpdate()
        {
            if (!initialized || IsPaused || contact || docked || stableFollow ||
                IsInteractionControlled) return;
            var rb = Rigidbody;
            if (following)
            {
                var desired = Vector3.ClampMagnitude(
                    followVelocity + (followTarget.position - rb.position) * 16f, 6f);
                rb.AddForce(Vector3.ClampMagnitude((desired - rb.velocity) / Time.fixedDeltaTime,
                    60f) - Physics.gravity, ForceMode.Acceleration);
                var delta = followTarget.rotation * Quaternion.Inverse(rb.rotation);
                delta.ToAngleAxis(out var degrees, out var axis);
                if (degrees > 180) degrees -= 360;
                var desiredAngular = IsFinite(axis)
                    ? Vector3.ClampMagnitude(
                        followAngularVelocity + axis * (degrees * Mathf.Deg2Rad * 16f), 12f)
                    : Vector3.zero;
                rb.AddTorque(Vector3.ClampMagnitude((desiredAngular - rb.angularVelocity) /
                    Time.fixedDeltaTime, 80f), ForceMode.Acceleration);
            }
            var fast = following || forceNeedsContinuousDetection || rb.velocity.sqrMagnitude > 4f;
            forceNeedsContinuousDetection = false;
            var mode = fast && shapes.All(shape => !(shape is MeshCollider))
                ? CollisionDetectionMode.ContinuousDynamic
                : CollisionDetectionMode.ContinuousSpeculative;
            if (rb.collisionDetectionMode != mode) rb.collisionDetectionMode = mode;
            quietTime = rb.velocity.sqrMagnitude < .0025f && rb.angularVelocity.sqrMagnitude < .01f
                ? quietTime + Time.fixedDeltaTime : 0;
        }

        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        public static void ValidatePose(Pose pose)
        {
            var q = pose.rotation;
            var norm = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (!IsFinite(pose.position) || !IsFinite(norm) || norm < .001f)
                throw new ArgumentException("Pose must contain a finite position and a non-zero rotation.");
        }
    }
}
