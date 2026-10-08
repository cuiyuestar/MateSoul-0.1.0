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
    public sealed class PhysicsBody : MonoBehaviour
    {
        [SerializeField] private BodySettings settings;
        private Rigidbody rigidBody;
        private Collider[] shapes;
        private readonly Collider[] overlaps = new Collider[64];
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private Pose followTarget;
        private Vector3 pausedVelocity, pausedAngularVelocity;
        private bool initialized, following, contact, wasSleeping;
        private bool forceNeedsContinuousDetection;
        private float quietTime;
        public Rigidbody Rigidbody => rigidBody != null ? rigidBody : rigidBody = GetComponent<Rigidbody>();
        public IReadOnlyList<Collider> Shapes => shapes;
        public bool IsPaused { get; private set; }
        public bool IsFollowing => following;
        public bool IsContact => contact;
        public bool IsSettled => initialized && !following && (Rigidbody.IsSleeping() || quietTime >= .5f);
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
            var all = GetComponentsInChildren<Collider>(true);
            if (GetComponentsInChildren<Rigidbody>(true).Any(body => body != rb))
                throw new InvalidOperationException($"{name}: compound items must have one root Rigidbody.");
            shapes = all.Where(shape => !shape.isTrigger).ToArray();
            if (shapes.Length == 0 || shapes.Any(shape =>
                    !(shape is BoxCollider || shape is SphereCollider || shape is CapsuleCollider ||
                      shape is MeshCollider mesh && mesh.convex)))
                throw new InvalidOperationException($"{name}: a dynamic body needs primitive or convex colliders.");
            settings = value;
            foreach (var shape in shapes) shape.sharedMaterial = value.material;
            rb.mass = value.mass;
            rb.drag = value.linearDrag;
            rb.angularDrag = value.angularDrag;
            rb.useGravity = true;
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.maxAngularVelocity = 30;
            rb.maxDepenetrationVelocity = 2;
            rb.solverIterations = 8;
            rb.solverVelocityIterations = 2;
            initialized = true;
        }

        public void AddForce(Vector3 force, ForceMode mode = ForceMode.Force)
        {
            if (!IsFinite(force)) throw new ArgumentException("Force must be finite.");
            if (IsPaused || contact) return;
            var velocityChange = mode == ForceMode.VelocityChange ? force :
                mode == ForceMode.Impulse ? force / Rigidbody.mass :
                mode == ForceMode.Acceleration ? force * Time.fixedDeltaTime :
                force * (Time.fixedDeltaTime / Rigidbody.mass);
            if ((Rigidbody.velocity + velocityChange).sqrMagnitude > 4)
            {
                forceNeedsContinuousDetection = true;
                Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }
            Rigidbody.AddForce(force, mode);
        }

        public void AddTorque(Vector3 torque, ForceMode mode = ForceMode.Force)
        {
            if (!IsFinite(torque)) throw new ArgumentException("Torque must be finite.");
            if (!IsPaused && !contact) Rigidbody.AddTorque(torque, mode);
        }

        public void BeginFollow(Pose target)
        {
            ValidatePose(target);
            if (IsPaused || contact) throw new InvalidOperationException("Body is unavailable for grabbing.");
            following = true;
            followTarget = target;
            quietTime = 0;
            Rigidbody.WakeUp();
        }

        public void SetFollowTarget(Pose target)
        {
            ValidatePose(target);
            followTarget = target;
        }

        public void EndFollow()
        {
            following = false; // Actual collision-resolved velocities are the release velocities.
            quietTime = 0;
        }

        public void SetContact(bool value)
        {
            if (IsPaused) throw new InvalidOperationException("Cannot change contact ownership while paused.");
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
                SetKinematic(contact);
                if (!contact)
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

        public bool CanOccupy(Pose pose, float tolerance = .012f, Transform ignoredRoot = null,
            ISet<int> ignoredRoomSurfaceSources = null)
        {
            ValidatePose(pose);
            foreach (var shape in shapes)
            {
                if (!shape.enabled || !shape.gameObject.activeInHierarchy) continue;
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
                    if (other.attachedRigidbody == Rigidbody ||
                        ignoredRoot != null && other.transform.IsChildOf(ignoredRoot) ||
                        Physics.GetIgnoreCollision(shape, other)) continue;
                    var roomSurface = other.GetComponent<RoomSurfaceCollider>();
                    if (roomSurface != null && ignoredRoomSurfaceSources != null &&
                        ignoredRoomSurfaceSources.Contains(roomSurface.SourceId)) continue;
                    if (Physics.ComputePenetration(shape, position, rotation, other,
                            other.transform.position, other.transform.rotation, out _, out var depth) &&
                        depth > tolerance)
                    {
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
                                        : "http://10.0.2.2:7784/event",
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
                                    "http://10.0.2.2:7782/event", payload, "application/json")
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
            var bounds = shapes[0].bounds;
            foreach (var shape in shapes) bounds.Encapsulate(shape.bounds);
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
            if (!initialized || IsPaused || contact) return;
            var rb = Rigidbody;
            if (following)
            {
                var desired = Vector3.ClampMagnitude((followTarget.position - rb.position) * 16f, 6f);
                rb.AddForce(Vector3.ClampMagnitude((desired - rb.velocity) / Time.fixedDeltaTime,
                    60f) - Physics.gravity, ForceMode.Acceleration);
                var delta = followTarget.rotation * Quaternion.Inverse(rb.rotation);
                delta.ToAngleAxis(out var degrees, out var axis);
                if (degrees > 180) degrees -= 360;
                var desiredAngular = IsFinite(axis)
                    ? Vector3.ClampMagnitude(axis * (degrees * Mathf.Deg2Rad * 16f), 12f)
                    : Vector3.zero;
                rb.AddTorque(Vector3.ClampMagnitude((desiredAngular - rb.angularVelocity) /
                    Time.fixedDeltaTime, 80f), ForceMode.Acceleration);
            }
            var fast = following || forceNeedsContinuousDetection || rb.velocity.sqrMagnitude > 4f;
            forceNeedsContinuousDetection = false;
            var mode = fast ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.Discrete;
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
