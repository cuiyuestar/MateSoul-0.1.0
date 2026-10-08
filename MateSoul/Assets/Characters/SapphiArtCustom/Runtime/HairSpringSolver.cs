// Verlet integration and sphere projection derived from UniVRM v0.99.4.
// See ThirdParty/UniVRM-SpringBone for MIT license and upstream source.
using System;
using System.Numerics;

namespace MateSoul.Hair
{
    /// <summary>Allocation-free, fixed-step spring solver. Units: meters.</summary>
    public sealed class HairSpringSolver
    {
        public struct Joint
        {
            public string Name;
            public int Parent;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 TailLocal;
            public bool Full;
            public bool Balanced;
            public float Stiffness, Drag, Gravity;
            public float LateralInertiaResponse;
            public float MaxLateralDegrees, MaxDepthDegrees;
        }

        public struct Sphere
        {
            public Vector3 Center;
            public float Radius;
        }

        public enum Quality { Off, Balanced, Full }
        public readonly Joint[] Joints;
        public readonly Vector3[] Positions;
        public readonly Quaternion[] Rotations;
        public readonly Vector3[] Tails;
        readonly Vector3[] previous;
        readonly Quaternion[] referenceRotations;
        public int LastSimulatedJoints { get; private set; }
        public int LastCollisionCorrections { get; private set; }
        public float LastMaxChainAngleDegrees { get; private set; }
        public float LastMaxLateralAngleDegrees { get; private set; }
        public float LastMaxDepthAngleDegrees { get; private set; }
        public const float StepSeconds = 1f / 90f;
        public float ParticleRadius = .0015f;
        public float MaxBendDegrees = 18f;
        public float MaxChainBendDegrees = 12f;

        public HairSpringSolver(Joint[] joints)
        {
            Joints = joints;
            Positions = new Vector3[joints.Length];
            Rotations = new Quaternion[joints.Length];
            Tails = new Vector3[joints.Length];
            previous = new Vector3[joints.Length];
            referenceRotations = new Quaternion[joints.Length];
            for (int i = 0; i < joints.Length; i++)
                if (joints[i].Parent >= i || joints[i].Parent < -1)
                    throw new ArgumentException("Joints must be ordered parent before child");
        }

        public void Reset(Vector3 rootPosition, Quaternion rootRotation)
        {
            for (int i = 0; i < Joints.Length; i++)
            {
                ReferenceBasis(i, rootRotation);
                Basis(i, rootPosition, rootRotation, out var rotation);
                Rotations[i] = rotation;
                previous[i] = Tails[i] = Positions[i] + Vector3.Transform(Joints[i].TailLocal, rotation);
            }
        }

        void ReferenceBasis(int i, Quaternion rootRotation)
        {
            var joint = Joints[i];
            var parentRotation = joint.Parent < 0
                ? rootRotation
                : referenceRotations[joint.Parent];
            referenceRotations[i] = Quaternion.Normalize(
                parentRotation * joint.LocalRotation);
        }

        void Basis(int i, Vector3 root, Quaternion rotation, out Quaternion basis)
        {
            var joint = Joints[i];
            var parentRotation = joint.Parent < 0 ? rotation : Rotations[joint.Parent];
            var parentPosition = joint.Parent < 0 ? root : Positions[joint.Parent];
            Positions[i] = parentPosition + Vector3.Transform(joint.LocalPosition, parentRotation);
            basis = Quaternion.Normalize(parentRotation * joint.LocalRotation);
        }

        public void Step(Vector3 root, Quaternion rotation,
            Quaternion responseRotation, Sphere[] colliders,
            Vector3 gravityDirection, Vector3 wind,
            float lateralDrive, Quality quality)
        {
            LastSimulatedJoints = LastCollisionCorrections = 0;
            LastMaxChainAngleDegrees = 0;
            LastMaxLateralAngleDegrees = 0;
            LastMaxDepthAngleDegrees = 0;
            for (int i = 0; i < Joints.Length; i++)
            {
                var j = Joints[i];
                ReferenceBasis(i, rotation);
                Basis(i, root, rotation, out var basis);
                var restDirection = Vector3.Transform(j.TailLocal, basis);
                var referenceDirection = Vector3.Transform(
                    j.TailLocal, referenceRotations[i]);
                float length = j.TailLocal.Length();
                bool active = quality == Quality.Full ? j.Full : quality == Quality.Balanced && j.Balanced;
                if (!active || length < 1e-6f)
                {
                    Rotations[i] = basis;
                    previous[i] = Tails[i] = Positions[i] + restDirection;
                    continue;
                }
                LastSimulatedJoints++;
                var direction = restDirection / length;
                var lateralAxis = Vector3.Transform(
                    Vector3.UnitX, responseRotation);
                // UniVRM's per-step drag/force semantics are held at a fixed 90 Hz.
                var next = Tails[i] +
                    (Tails[i] - previous[i]) * (1 - j.Drag)
                    + direction * j.Stiffness * StepSeconds
                    + (gravityDirection * j.Gravity + wind +
                        lateralAxis * lateralDrive *
                        j.LateralInertiaResponse) * StepSeconds;
                next = Limit(
                    next, Positions[i], direction,
                    Normalize(referenceDirection, direction), length);
                next = LimitDirectionalOffset(
                    next, Positions[i],
                    Normalize(referenceDirection, direction),
                    responseRotation, length,
                    j.MaxLateralDegrees, j.MaxDepthDegrees);
                for (int pass = 0; pass < 3; pass++)
                {
                    foreach (var sphere in colliders)
                    {
                        float radius = sphere.Radius + ParticleRadius;
                        var delta = next - sphere.Center;
                        if (delta.LengthSquared() >= radius * radius) continue;
                        LastCollisionCorrections++;
                        var normal = Normalize(delta, direction);
                        next = sphere.Center + normal * radius;
                        next = Limit(
                            next, Positions[i], direction,
                            Normalize(referenceDirection, direction), length);
                        next = LimitDirectionalOffset(
                            next, Positions[i],
                            Normalize(referenceDirection, direction),
                            responseRotation, length,
                            j.MaxLateralDegrees, j.MaxDepthDegrees);
                    }
                }
                previous[i] = Tails[i];
                Tails[i] = next;
                Vector3 solvedDirection = Normalize(
                    next - Positions[i], referenceDirection);
                Vector3 localSolved = Vector3.Transform(
                    solvedDirection, Quaternion.Inverse(responseRotation));
                Vector3 localReference = Vector3.Transform(
                    Normalize(referenceDirection, direction),
                    Quaternion.Inverse(responseRotation));
                if (j.MaxLateralDegrees > 0)
                    LastMaxLateralAngleDegrees = Math.Max(
                        LastMaxLateralAngleDegrees,
                        OffsetDegrees(localSolved.X - localReference.X));
                if (j.MaxDepthDegrees > 0)
                    LastMaxDepthAngleDegrees = Math.Max(
                        LastMaxDepthAngleDegrees,
                        OffsetDegrees(localSolved.Z - localReference.Z));
                float chainDot = Math.Max(-1f, Math.Min(1f, Vector3.Dot(
                    solvedDirection,
                    Normalize(referenceDirection, direction))));
                LastMaxChainAngleDegrees = Math.Max(
                    LastMaxChainAngleDegrees,
                    (float)Math.Acos(chainDot) * 180f / (float)Math.PI);
                Rotations[i] = Quaternion.Normalize(FromTo(direction, next - Positions[i]) * basis);
            }
        }

        Vector3 Limit(
            Vector3 next, Vector3 head, Vector3 rest,
            Vector3 chainRest, float length)
        {
            var direction = Normalize(next - head, rest);
            for (int i = 0; i < 2; i++)
            {
                direction = LimitDirection(
                    direction, rest, MaxBendDegrees);
                direction = LimitDirection(
                    direction, chainRest, MaxChainBendDegrees);
            }
            return head + direction * length;
        }

        static Vector3 LimitDirectionalOffset(
            Vector3 next, Vector3 head, Vector3 chainRest,
            Quaternion responseRotation, float length,
            float maxLateralDegrees, float maxDepthDegrees)
        {
            if (maxLateralDegrees <= 0 && maxDepthDegrees <= 0)
                return next;
            Quaternion inverse = Quaternion.Inverse(responseRotation);
            Vector3 localDirection = Vector3.Transform(
                Normalize(next - head, chainRest), inverse);
            Vector3 localRest = Vector3.Transform(chainRest, inverse);
            if (maxLateralDegrees > 0)
            {
                float limit = (float)Math.Sin(
                    maxLateralDegrees * Math.PI / 180f);
                localDirection.X = Clamp(
                    localDirection.X,
                    localRest.X - limit,
                    localRest.X + limit);
            }
            if (maxDepthDegrees > 0)
            {
                float limit = (float)Math.Sin(
                    maxDepthDegrees * Math.PI / 180f);
                localDirection.Z = Clamp(
                    localDirection.Z,
                    localRest.Z - limit,
                    localRest.Z + limit);
            }
            float horizontalSquared =
                localDirection.X * localDirection.X +
                localDirection.Z * localDirection.Z;
            if (horizontalSquared >= 1f)
            {
                float scale = .999999f /
                    (float)Math.Sqrt(horizontalSquared);
                localDirection.X *= scale;
                localDirection.Z *= scale;
                horizontalSquared =
                    localDirection.X * localDirection.X +
                    localDirection.Z * localDirection.Z;
            }
            float vertical = (float)Math.Sqrt(
                Math.Max(0, 1f - horizontalSquared));
            float verticalSign = Math.Abs(localRest.Y) > 1e-5f
                ? Math.Sign(localRest.Y)
                : Math.Sign(localDirection.Y);
            localDirection.Y = vertical * (
                verticalSign == 0 ? 1f : verticalSign);
            return head + Vector3.Transform(
                localDirection, responseRotation) * length;
        }

        static float OffsetDegrees(float normalizedOffset)
        {
            return (float)Math.Asin(Math.Min(
                1f, Math.Abs(normalizedOffset))) *
                180f / (float)Math.PI;
        }

        static float Clamp(float value, float minimum, float maximum) =>
            Math.Max(minimum, Math.Min(maximum, value));

        static Vector3 LimitDirection(
            Vector3 direction, Vector3 rest, float maxDegrees)
        {
            float dot = Math.Max(
                -1f, Math.Min(1f, Vector3.Dot(rest, direction)));
            float angle = (float)Math.Acos(dot);
            float max = Math.Max(0, maxDegrees) *
                (float)Math.PI / 180f;
            if (angle > max)
            {
                var bend = FromTo(rest, direction);
                direction = Vector3.Transform(rest, Quaternion.Slerp(Quaternion.Identity, bend, max / angle));
            }
            return Normalize(direction, rest);
        }

        static Vector3 Normalize(Vector3 v, Vector3 fallback)
        {
            return v.LengthSquared() > 1e-16f ? Vector3.Normalize(v) : fallback;
        }

        static Quaternion FromTo(Vector3 from, Vector3 to)
        {
            from = Normalize(from, Vector3.UnitY);
            to = Normalize(to, from);
            float dot = Vector3.Dot(from, to);
            if (dot < -.99999f)
            {
                var axis = Vector3.Cross(from, Math.Abs(from.X) < .8f ? Vector3.UnitX : Vector3.UnitY);
                return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), (float)Math.PI);
            }
            return Quaternion.Normalize(new Quaternion(Vector3.Cross(from, to), 1 + dot));
        }
    }
}
