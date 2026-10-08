using System;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using NVector = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace MateSoul.Hair
{
    [DefaultExecutionOrder(11000)]
    public sealed class HairSpringPhysics : MonoBehaviour
    {
        [Serializable]
        public struct Bone
        {
            public Transform transform;
            public Vector3 tailLocal;
            public int parent;
            public bool full, balanced;
            public float stiffness, drag, gravity;
            public float lateralInertiaResponse;
            public float maxLateralDegrees, maxDepthDegrees;
        }

        [Serializable]
        public struct ColliderSphere
        {
            public Transform transform;
            public Vector3 offset;
            public float radius;
        }

        public Bone[] bones = Array.Empty<Bone>();
        public ColliderSphere[] colliders = Array.Empty<ColliderSphere>();
        public HairSpringSolver.Quality quality = HairSpringSolver.Quality.Balanced;
        [Range(1, 30)] public float maxBendDegrees = 18;
        [Range(1, 30)] public float maxChainBendDegrees = 15.6f;
        [Range(0, .01f)] public float particleRadius = .0015f;
        public Transform directionReference;
        public Vector3 wind;
        public float teleportDistance = .35f;
        public float teleportAngle = 60;
        public double LastUpdateMilliseconds { get; private set; }
        public int LastSimulatedJoints { get; private set; }
        public int LastSubsteps { get; private set; }
        public int ResetCount { get; private set; }
        public float MaxBoneAngle { get; private set; }
        public float MaxChainAngle { get; private set; }
        public float MaxLateralAngle { get; private set; }
        public float MaxDepthAngle { get; private set; }
        public float LateralDrive { get; private set; }
        public int LastCollisionCorrections { get; private set; }
        public bool IsInitialized => solver != null;
        static readonly ProfilerMarker Marker = new ProfilerMarker("MateSoul.HairSpringPhysics");
        HairSpringSolver solver;
        HairSpringSolver.Sphere[] worldColliders;
        Quaternion[] restRotations;
        Vector3 lastPosition, scale;
        Quaternion lastRotation;
        Quaternion responseRestOffset;
        Quaternion lastResponseRotation;
        Vector3 lastResponsePosition;
        Vector3 lastResponseVelocity;
        float accumulator;
        HairSpringSolver.Quality previousQuality;

        public static NVector ToN(Vector3 p) => new NVector(p.x, p.y, p.z);
        public static NQuaternion ToN(Quaternion q) => new NQuaternion(q.x, q.y, q.z, q.w);
        static Quaternion ToUnity(NQuaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W);

        void Start() => Initialize();

        public void Initialize()
        {
            if (bones.Length == 0) return;
            if (solver != null) RestoreRest();
            scale = transform.lossyScale;
            if (scale.x <= 0 || Mathf.Abs(scale.x - scale.y) > .001f
                || Mathf.Abs(scale.x - scale.z) > .001f)
                throw new InvalidOperationException("Hair physics requires positive uniform root scale");
            var joints = new HairSpringSolver.Joint[bones.Length];
            restRotations = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                var b = bones[i];
                if (!b.transform || b.parent >= i || b.parent < -1)
                    throw new InvalidOperationException("Invalid hair bone definition: " + i);
                var parent = b.parent < 0 ? transform : bones[b.parent].transform;
                Quaternion inverse = Quaternion.Inverse(parent.rotation);
                joints[i] = new HairSpringSolver.Joint {
                    Name = b.transform.name, Parent = b.parent,
                    LocalPosition = ToN(inverse * (b.transform.position - parent.position)),
                    LocalRotation = ToN(inverse * b.transform.rotation),
                    TailLocal = ToN(Quaternion.Inverse(b.transform.rotation) *
                        b.transform.TransformVector(b.tailLocal)),
                    Full = b.full, Balanced = b.balanced,
                    Stiffness = b.stiffness, Drag = b.drag,
                    Gravity = b.gravity,
                    LateralInertiaResponse =
                        Mathf.Max(0, b.lateralInertiaResponse),
                    MaxLateralDegrees =
                        Mathf.Max(0, b.maxLateralDegrees),
                    MaxDepthDegrees =
                        Mathf.Max(0, b.maxDepthDegrees)
                };
                restRotations[i] = b.transform.localRotation;
            }
            solver = new HairSpringSolver(joints);
            worldColliders = new HairSpringSolver.Sphere[colliders.Length];
            Transform response = directionReference
                ? directionReference
                : transform;
            responseRestOffset =
                Quaternion.Inverse(transform.root.rotation) *
                response.rotation;
            ResetSimulation();
        }

        public void ResetSimulation()
        {
            if (solver == null) return;
            RestoreRest();
            lastPosition = transform.position;
            lastRotation = transform.rotation;
            lastResponseRotation = ResponseRotation();
            lastResponsePosition = ResponseTransform().position;
            lastResponseVelocity = Vector3.zero;
            LateralDrive = 0;
            solver.Reset(ToN(lastPosition), ToN(lastRotation));
            accumulator = 0;
            previousQuality = quality;
            LastUpdateMilliseconds = 0;
            ResetCount++;
        }

        void RestoreRest()
        {
            if (restRotations == null) return;
            for (int i = 0; i < bones.Length; i++)
                if (bones[i].transform) bones[i].transform.localRotation = restRotations[i];
        }

        void OnDisable() => ResetSimulation();
        void OnApplicationPause(bool paused) { if (paused) ResetSimulation(); }
        void OnApplicationFocus(bool focused) { if (!focused) ResetSimulation(); }
        void LateUpdate() => Advance(Time.deltaTime);

        public void Advance(float deltaTime)
        {
            LastUpdateMilliseconds = 0;
            LastSubsteps = LastSimulatedJoints = LastCollisionCorrections = 0;
            MaxChainAngle = 0;
            MaxLateralAngle = MaxDepthAngle = 0;
            if (solver == null) Initialize();
            if (solver == null) return;
            if ((transform.lossyScale - scale).sqrMagnitude > 1e-6f)
            {
                Initialize();
                return;
            }
            if (quality != previousQuality || deltaTime > .12f
                || Vector3.Distance(lastPosition, transform.position) > teleportDistance
                || Quaternion.Angle(lastRotation, transform.rotation) > teleportAngle)
            {
                ResetSimulation();
                return;
            }
            if (quality == HairSpringSolver.Quality.Off)
            {
                lastPosition = transform.position;
                lastRotation = transform.rotation;
                lastResponseRotation = ResponseRotation();
                lastResponsePosition = ResponseTransform().position;
                lastResponseVelocity = Vector3.zero;
                LateralDrive = 0;
                return;
            }
            if (deltaTime <= 0) return;
            long start = Stopwatch.GetTimestamp();
            using (Marker.Auto())
            {
                solver.MaxBendDegrees = maxBendDegrees;
                solver.MaxChainBendDegrees = maxChainBendDegrees;
                solver.ParticleRadius = particleRadius * scale.x;
                for (int i = 0; i < colliders.Length; i++)
                {
                    var c = colliders[i];
                    if (!c.transform) throw new InvalidOperationException("Missing hair collider transform");
                    Vector3 s = c.transform.lossyScale;
                    worldColliders[i] = new HairSpringSolver.Sphere {
                        Center = ToN(c.transform.TransformPoint(c.offset)),
                        Radius = c.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z))
                    };
                }
                accumulator += deltaTime;
                // Bound catch-up after stalls; reset rather than accumulating unstable velocity.
                if (accumulator > HairSpringSolver.StepSeconds * 5)
                {
                    ResetSimulation();
                    return;
                }
                int steps = Mathf.FloorToInt(accumulator / HairSpringSolver.StepSeconds);
                Quaternion responseRotation = ResponseRotation();
                Transform response = ResponseTransform();
                Vector3 responseVelocity =
                    (response.position - lastResponsePosition) /
                    deltaTime;
                Vector3 responseAcceleration = Vector3.ClampMagnitude(
                    (responseVelocity - lastResponseVelocity) /
                    deltaTime, 12f);
                Quaternion responseDelta =
                    responseRotation *
                    Quaternion.Inverse(lastResponseRotation);
                responseDelta.ToAngleAxis(
                    out float responseAngle, out Vector3 responseAxis);
                if (responseAngle > 180f)
                    responseAngle -= 360f;
                Vector3 angularVelocity = responseAxis *
                    (responseAngle * Mathf.Deg2Rad / deltaTime);
                Quaternion inverseResponse =
                    Quaternion.Inverse(responseRotation);
                Vector3 localAcceleration =
                    inverseResponse * responseAcceleration;
                Vector3 localAngularVelocity =
                    inverseResponse * angularVelocity;
                Vector3 localGravity =
                    inverseResponse * Vector3.down;
                float targetLateralDrive = Mathf.Clamp(
                    -localAcceleration.x * .012f -
                    localAngularVelocity.z * .045f +
                    localGravity.x * .18f,
                    -.2f, .2f);
                LateralDrive = Mathf.Lerp(
                    LateralDrive, targetLateralDrive,
                    1f - Mathf.Exp(-10f * deltaTime));
                for (int i = 0; i < steps; i++)
                {
                    float t = (i + 1f) / steps;
                    solver.Step(ToN(Vector3.Lerp(lastPosition, transform.position, t)),
                        ToN(Quaternion.Slerp(lastRotation, transform.rotation, t)),
                        ToN(Quaternion.Slerp(
                            lastResponseRotation,
                            responseRotation, t)),
                        worldColliders, ToN(Vector3.down), ToN(wind),
                        LateralDrive, quality);
                    LastSubsteps++;
                    LastCollisionCorrections += solver.LastCollisionCorrections;
                    MaxChainAngle = Mathf.Max(
                        MaxChainAngle,
                        solver.LastMaxChainAngleDegrees);
                    MaxLateralAngle = Mathf.Max(
                        MaxLateralAngle,
                        solver.LastMaxLateralAngleDegrees);
                    MaxDepthAngle = Mathf.Max(
                        MaxDepthAngle,
                        solver.LastMaxDepthAngleDegrees);
                }
                accumulator -= steps * HairSpringSolver.StepSeconds;
                if (steps > 0)
                {
                    LastSimulatedJoints = solver.LastSimulatedJoints;
                    MaxBoneAngle = 0;
                    for (int i = 0; i < bones.Length; i++)
                    {
                        bool active = quality == HairSpringSolver.Quality.Full
                            ? bones[i].full
                            : quality == HairSpringSolver.Quality.Balanced && bones[i].balanced;
                        if (!active) continue;
                        bones[i].transform.rotation = ToUnity(solver.Rotations[i]);
                        MaxBoneAngle = Mathf.Max(MaxBoneAngle,
                            Quaternion.Angle(restRotations[i], bones[i].transform.localRotation));
                    }
                    lastPosition = transform.position;
                    lastRotation = transform.rotation;
                    lastResponseRotation = responseRotation;
                    lastResponsePosition = response.position;
                    lastResponseVelocity = responseVelocity;
                }
            }
            LastUpdateMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        }

        Quaternion ResponseRotation()
        {
            Transform response = directionReference
                ? directionReference
                : transform;
            return response.rotation *
                Quaternion.Inverse(responseRestOffset);
        }

        Transform ResponseTransform() =>
            directionReference ? directionReference : transform;
    }
}
