using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    public enum AvatarHandSide { Left, Right }

    [Serializable]
    public struct WristRotationLimits
    {
        [Min(1)] public float flexionDegrees;
        [Min(1)] public float extensionDegrees;
        [Min(1)] public float ulnarDeviationDegrees;
        [Min(1)] public float radialDeviationDegrees;
        [Min(1)] public float pronationDegrees;
        [Min(1)] public float supinationDegrees;
        [Range(.1f, 1)] public float softLimitRatio;

        public static WristRotationLimits Comfortable => new WristRotationLimits
        {
            flexionDegrees = 45,
            extensionDegrees = 40,
            ulnarDeviationDegrees = 25,
            radialDeviationDegrees = 15,
            pronationDegrees = 65,
            supinationDegrees = 65,
            softLimitRatio = .78f
        };

        public void Validate()
        {
            if (!FinitePositive(flexionDegrees) || !FinitePositive(extensionDegrees) ||
                !FinitePositive(ulnarDeviationDegrees) || !FinitePositive(radialDeviationDegrees) ||
                !FinitePositive(pronationDegrees) || !FinitePositive(supinationDegrees) ||
                !PhysicsBody.IsFinite(softLimitRatio) || softLimitRatio <= 0 || softLimitRatio > 1)
                throw new ArgumentException("Wrist limits must be finite and positive.");
        }

        private static bool FinitePositive(float value)
            => PhysicsBody.IsFinite(value) && value > 0 && value <= 180;
    }

    [Serializable]
    public struct HandHoldTuning
    {
        [Range(.5f, .95f)] public float softReachRatio;
        [Range(.75f, .995f)] public float hardReachRatio;
        [Min(1)] public float positionResponse;
        [Min(1)] public float rotationResponse;
        [Min(1)] public float spring;
        [Min(0)] public float damping;
        [Min(0)] public float startForce;
        [Min(0)] public float stopForce;
        [Min(0)] public float movingResistance;
        [Min(.01f)] public float startDelay;
        [Min(.01f)] public float stopDelay;
        [Min(.001f)] public float effectiveMass;
        [Min(.1f)] public float maximumSpeed;
        [Min(.1f)] public float maximumAcceleration;
        [Min(.1f)] public float stoppingAcceleration;
        [Min(0)] public float velocityDamping;
        [Min(0)] public float playerMovementSpeed;
        [Range(-1, 1)] public float movementCoherence;
        [Min(.1f)] public float forceFilterHz;
        [Min(.01f)] public float maximumTargetSeparation;
        [Min(.01f)] public float blockedReleaseDelay;
        [Min(.01f)] public float trackingJumpDistance;
        [Min(.01f)] public float blendInSeconds;
        [Min(.01f)] public float blendOutSeconds;
        // Zero keeps instances serialized before these limits on the defaults.
        [Range(30, 160)] public float shoulderConeDegrees;
        [Min(0)] public float torsoRadius;
        public WristRotationLimits wrist;

        public static HandHoldTuning Default => new HandHoldTuning
        {
            softReachRatio = .84f,
            hardReachRatio = .97f,
            positionResponse = 28,
            rotationResponse = 20,
            spring = 300,
            damping = 15,
            startForce = 18,
            stopForce = 10,
            movingResistance = 11,
            startDelay = .12f,
            stopDelay = .20f,
            effectiveMass = 45,
            maximumSpeed = 1.1f,
            maximumAcceleration = 2.2f,
            stoppingAcceleration = 3f,
            velocityDamping = .8f,
            playerMovementSpeed = .10f,
            movementCoherence = .55f,
            forceFilterHz = 5,
            maximumTargetSeparation = .40f,
            blockedReleaseDelay = .30f,
            trackingJumpDistance = .45f,
            blendInSeconds = .15f,
            blendOutSeconds = .22f,
            shoulderConeDegrees = 100,
            torsoRadius = .16f,
            wrist = WristRotationLimits.Comfortable
        };

        public void Validate()
        {
            wrist.Validate();
            if (softReachRatio <= 0 || hardReachRatio <= softReachRatio || hardReachRatio >= 1 ||
                positionResponse <= 0 || rotationResponse <= 0 || spring <= 0 || damping < 0 ||
                startForce <= 0 || stopForce < 0 || stopForce >= startForce ||
                movingResistance < 0 || movingResistance >= startForce ||
                startDelay <= 0 || stopDelay <= 0 || effectiveMass <= 0 ||
                maximumSpeed <= 0 || maximumAcceleration <= 0 || stoppingAcceleration <= 0 ||
                velocityDamping < 0 || playerMovementSpeed < 0 || forceFilterHz <= 0 ||
                maximumTargetSeparation <= 0 || blockedReleaseDelay <= 0 ||
                trackingJumpDistance <= 0 || blendInSeconds <= 0 || blendOutSeconds <= 0)
                throw new ArgumentException("Hand-hold tuning is invalid.");
        }
    }

    public readonly struct WristConstraintResult
    {
        public readonly Quaternion Rotation;
        public readonly float Utilization;
        public readonly float FlexionDegrees;
        public readonly float DeviationDegrees;
        public readonly float TwistDegrees;

        public WristConstraintResult(Quaternion rotation, float utilization,
            float flexionDegrees, float deviationDegrees, float twistDegrees)
        {
            Rotation = rotation;
            Utilization = utilization;
            FlexionDegrees = flexionDegrees;
            DeviationDegrees = deviationDegrees;
            TwistDegrees = twistDegrees;
        }
    }

    public readonly struct HandReachResult
    {
        public readonly Vector3 Position;
        public readonly Vector3 Direction;
        public readonly float Extension;
        public readonly float Utilization;
        public readonly float Separation;

        public HandReachResult(Vector3 position, Vector3 direction, float extension,
            float utilization, float separation)
        {
            Position = position;
            Direction = direction;
            Extension = extension;
            Utilization = utilization;
            Separation = separation;
        }
    }

    public static class HandHoldMath
    {
        public static HandReachResult ConstrainReach(
            Vector3 shoulder, Vector3 desiredWrist, float armLength,
            float softReachRatio, float hardReachRatio)
        {
            if (!PhysicsBody.IsFinite(shoulder) || !PhysicsBody.IsFinite(desiredWrist) ||
                !PhysicsBody.IsFinite(armLength) || armLength <= 0 ||
                softReachRatio <= 0 || hardReachRatio <= softReachRatio || hardReachRatio >= 1)
                throw new ArgumentException("Reach inputs are invalid.");

            var offset = desiredWrist - shoulder;
            var distance = offset.magnitude;
            var direction = distance > .00001f ? offset / distance : Vector3.forward;
            var soft = armLength * softReachRatio;
            var hard = armLength * hardReachRatio;
            var position = shoulder + direction * Mathf.Min(distance, hard);
            return new HandReachResult(position, direction, Mathf.Max(0, distance - soft),
                Mathf.Clamp01(Mathf.InverseLerp(soft, hard, distance)),
                Vector3.Distance(position, desiredWrist));
        }

        // Keeps the pulled hand out of the torso volume so the arm cannot be
        // dragged through the chest or belly. The torso is approximated by a
        // capsule from the hips to the shoulder midpoint.
        public static Vector3 ConstrainToTorso(Vector3 wrist, Vector3 shoulderMid,
            Vector3 bodyBase, Vector3 bodyForward, float radius)
        {
            var axis = shoulderMid - bodyBase;
            var length = axis.magnitude;
            if (length < .001f || radius <= 0) return wrist;
            var direction = axis / length;
            var along = Mathf.Clamp(Vector3.Dot(wrist - bodyBase, direction), 0, length);
            var closest = bodyBase + direction * along;
            var radial = Vector3.ProjectOnPlane(wrist - closest, direction);
            if (radial.sqrMagnitude >= radius * radius) return wrist;
            if (radial.sqrMagnitude < .000001f)
                radial = Vector3.ProjectOnPlane(bodyForward, direction);
            if (radial.sqrMagnitude < .000001f) radial = Vector3.right;
            return closest + radial.normalized * radius;
        }

        // Caps the shoulder swing at a cone around the hanging direction. Past
        // that angle the hand stops instead of wrenching the arm out of its
        // socket or folding it behind the back.
        public static Vector3 ConstrainShoulderCone(Vector3 shoulder, Vector3 wrist,
            Vector3 down, float coneDegrees)
        {
            var offset = wrist - shoulder;
            var distance = offset.magnitude;
            if (distance < .0001f || coneDegrees <= 0 || coneDegrees >= 180) return wrist;
            var direction = offset / distance;
            down = NormalizeAxis(down, Vector3.down);
            var angle = Vector3.Angle(down, direction);
            if (angle <= coneDegrees) return wrist;
            var axis = Vector3.Cross(direction, down);
            if (axis.sqrMagnitude < .000001f)
                axis = Vector3.Cross(direction, Vector3.forward);
            var clamped = Quaternion.AngleAxis(angle - coneDegrees, axis.normalized) * direction;
            return shoulder + clamped * distance;
        }

        public static WristConstraintResult ConstrainWristDelta(
            Quaternion desiredDelta, Vector3 twistAxis, Vector3 flexAxis,
            WristRotationLimits limits)
        {
            limits.Validate();
            twistAxis = NormalizeAxis(twistAxis, Vector3.right);
            flexAxis = Vector3.ProjectOnPlane(flexAxis, twistAxis);
            flexAxis = NormalizeAxis(flexAxis, Vector3.up);
            var deviationAxis = NormalizeAxis(Vector3.Cross(twistAxis, flexAxis), Vector3.forward);
            desiredDelta = Normalize(desiredDelta);

            DecomposeSwingTwist(desiredDelta, twistAxis, out var swing, out var twist);
            var twistDegrees = SignedTwistDegrees(twist, twistAxis);
            var twistLimit = twistDegrees >= 0 ? limits.pronationDegrees : limits.supinationDegrees;
            var clampedTwist = Mathf.Clamp(twistDegrees, -limits.supinationDegrees, limits.pronationDegrees);

            swing.ToAngleAxis(out var swingDegrees, out var swingAxis);
            if (swingDegrees > 180) swingDegrees -= 360;
            if (!PhysicsBody.IsFinite(swingAxis) || Mathf.Abs(swingDegrees) < .0001f)
            {
                swingAxis = flexAxis;
                swingDegrees = 0;
            }
            var rotationVector = swingAxis.normalized * swingDegrees;
            var flexion = Vector3.Dot(rotationVector, flexAxis);
            var deviation = Vector3.Dot(rotationVector, deviationAxis);
            var flexLimit = flexion >= 0 ? limits.flexionDegrees : limits.extensionDegrees;
            var deviationLimit = deviation >= 0
                ? limits.ulnarDeviationDegrees : limits.radialDeviationDegrees;
            var ellipse = Mathf.Sqrt(
                flexion * flexion / (flexLimit * flexLimit) +
                deviation * deviation / (deviationLimit * deviationLimit));
            if (ellipse > 1)
            {
                flexion /= ellipse;
                deviation /= ellipse;
            }

            var swingVector = flexAxis * flexion + deviationAxis * deviation;
            var clampedSwing = swingVector.sqrMagnitude < .000001f
                ? Quaternion.identity
                : Quaternion.AngleAxis(swingVector.magnitude, swingVector.normalized);
            var utilization = Mathf.Max(ellipse, Mathf.Abs(twistDegrees) / twistLimit);
            return new WristConstraintResult(
                Normalize(clampedSwing * Quaternion.AngleAxis(clampedTwist, twistAxis)),
                Mathf.Max(0, utilization), flexion, deviation, clampedTwist);
        }

        public static Vector3 ProxyForce(float extension, Vector3 direction,
            float outwardSpeed, HandHoldTuning tuning)
        {
            if (extension <= 0) return Vector3.zero;
            direction = NormalizeAxis(direction, Vector3.forward);
            var magnitude = tuning.spring * extension +
                            tuning.damping * Mathf.Max(0, outwardSpeed);
            return direction * magnitude;
        }

        public static float ExpSmoothing(float response, float deltaSeconds)
            => 1 - Mathf.Exp(-Mathf.Max(0, response) * Mathf.Max(0, deltaSeconds));

        private static void DecomposeSwingTwist(Quaternion rotation, Vector3 axis,
            out Quaternion swing, out Quaternion twist)
        {
            var vector = new Vector3(rotation.x, rotation.y, rotation.z);
            var projection = Vector3.Project(vector, axis);
            twist = Normalize(new Quaternion(projection.x, projection.y, projection.z, rotation.w));
            swing = Normalize(rotation * Quaternion.Inverse(twist));
        }

        private static float SignedTwistDegrees(Quaternion twist, Vector3 axis)
        {
            twist = Normalize(twist);
            var vector = new Vector3(twist.x, twist.y, twist.z);
            var radians = 2 * Mathf.Atan2(Vector3.Dot(vector, axis), twist.w);
            return Mathf.DeltaAngle(0, radians * Mathf.Rad2Deg);
        }

        private static Quaternion Normalize(Quaternion value)
        {
            var magnitude = Mathf.Sqrt(value.x * value.x + value.y * value.y +
                                       value.z * value.z + value.w * value.w);
            if (!PhysicsBody.IsFinite(magnitude) || magnitude < .00001f)
                return Quaternion.identity;
            var sign = value.w < 0 ? -1f : 1f;
            return new Quaternion(value.x * sign / magnitude, value.y * sign / magnitude,
                value.z * sign / magnitude, value.w * sign / magnitude);
        }

        private static Vector3 NormalizeAxis(Vector3 value, Vector3 fallback)
            => PhysicsBody.IsFinite(value) && value.sqrMagnitude > .000001f
                ? value.normalized : fallback;
    }

    /// <summary>
    /// Turns sustained player locomotion into a walking command so the avatar
    /// keeps pace while the player leads it by the hand, instead of only moving
    /// once the arm has been stretched beyond its soft reach.
    /// </summary>
    public sealed class HandHoldLeadController
    {
        private const float velocityResponse = 12f;
        private const float engageDelay = .12f;
        private const float releaseSpeedRatio = .5f;
        // A held hand is stabilised by the arm and never matches the head speed, so
        // it only has to travel with the body instead of keeping pace with it.
        private const float handSpeedRatio = .3f;
        // Hand tracking drops a frame or two at a time; a brief disagreement must not
        // restart the whole engagement.
        private const float vetoTolerance = .1f;
        private Vector3 filteredVelocity;
        private float engageTime, vetoTimer;
        private bool engaged;

        public bool Engaged => engaged;
        public Vector3 CommandVelocity { get; private set; }

        public Vector3 Tick(Vector3 playerVelocity, Vector3 handVelocity,
            float deltaSeconds, HandHoldTuning tuning)
        {
            if (deltaSeconds <= 0) return CommandVelocity;
            playerVelocity.y = 0;
            handVelocity.y = 0;
            var playerSpeed = playerVelocity.magnitude;
            var handSpeed = handVelocity.magnitude;
            var enterSpeed = tuning.playerMovementSpeed;
            // Walking means the head travels and the held hand travels along with it.
            // A head turn moves the head but leaves the hand behind, which is what
            // this rejects; the hand no longer has to reach the head speed itself.
            var walking = playerSpeed >= enterSpeed &&
                          handSpeed >= playerSpeed * handSpeedRatio &&
                          Vector3.Dot(playerVelocity, handVelocity) > 0;
            vetoTimer = walking ? 0 : vetoTimer + deltaSeconds;
            filteredVelocity = Vector3.Lerp(filteredVelocity,
                walking ? playerVelocity : Vector3.zero,
                HandHoldMath.ExpSmoothing(velocityResponse, deltaSeconds));
            var speed = filteredVelocity.magnitude;
            var releaseSpeed = enterSpeed * releaseSpeedRatio;

            if (!engaged)
            {
                engageTime = speed >= enterSpeed && vetoTimer < vetoTolerance
                    ? engageTime + deltaSeconds : 0;
                if (engageTime >= engageDelay) engaged = true;
            }
            else if (speed < releaseSpeed)
            {
                engaged = false;
                engageTime = 0;
            }

            if (!engaged)
            {
                CommandVelocity = Vector3.MoveTowards(CommandVelocity, Vector3.zero,
                    tuning.stoppingAcceleration * deltaSeconds);
                return CommandVelocity;
            }

            // The filtered player speed is already smooth, so the command tracks it
            // one-to-one instead of ramping a second time; the extra acceleration
            // budget made the avatar trail the player by another quarter second.
            var direction = speed > .0001f ? filteredVelocity / speed : Vector3.zero;
            CommandVelocity = direction * Mathf.Min(speed, tuning.maximumSpeed);
            return CommandVelocity;
        }

        public void Reset()
        {
            filteredVelocity = Vector3.zero;
            engageTime = 0;
            vetoTimer = 0;
            engaged = false;
            CommandVelocity = Vector3.zero;
        }
    }

    public sealed class HandHoldTractionController
    {
        private Vector3 filteredForce;
        private float startTime, stopTime;

        public bool Engaged { get; private set; }
        public Vector3 CommandVelocity { get; private set; }
        public Vector3 FilteredForce => filteredForce;

        public Vector3 Tick(Vector3 proxyForce, Vector3 playerVelocity,
            Vector3 handVelocity, float deltaSeconds, HandHoldTuning tuning)
        {
            if (deltaSeconds <= 0) return CommandVelocity;
            proxyForce.y = 0;
            playerVelocity.y = 0;
            handVelocity.y = 0;
            var alpha = HandHoldMath.ExpSmoothing(tuning.forceFilterHz * Mathf.PI * 2, deltaSeconds);
            filteredForce = Vector3.Lerp(filteredForce, proxyForce, alpha);
            var magnitude = filteredForce.magnitude;
            var direction = magnitude > .001f ? filteredForce / magnitude : Vector3.zero;
            var coherentMovement = playerVelocity.magnitude >= tuning.playerMovementSpeed &&
                                   handVelocity.magnitude >= tuning.playerMovementSpeed &&
                                   Vector3.Dot(playerVelocity.normalized, handVelocity.normalized) >=
                                   tuning.movementCoherence &&
                                   Vector3.Dot(playerVelocity, direction) > 0;

            if (!Engaged)
            {
                startTime = magnitude >= tuning.startForce ||
                            coherentMovement && magnitude >= tuning.stopForce
                    ? startTime + deltaSeconds : 0;
                if (startTime >= tuning.startDelay)
                {
                    Engaged = true;
                    stopTime = 0;
                }
            }
            else
            {
                stopTime = magnitude < tuning.stopForce ? stopTime + deltaSeconds : 0;
                if (stopTime >= tuning.stopDelay)
                {
                    Engaged = false;
                    startTime = 0;
                }
            }

            var previous = CommandVelocity;
            if (!Engaged)
            {
                CommandVelocity = Vector3.MoveTowards(
                    CommandVelocity, Vector3.zero, tuning.stoppingAcceleration * deltaSeconds);
                return CommandVelocity;
            }

            var residual = Mathf.Max(0, magnitude - tuning.movingResistance);
            var next = CommandVelocity + direction * (residual / tuning.effectiveMass * deltaSeconds);
            next *= Mathf.Exp(-tuning.velocityDamping * deltaSeconds);
            if (coherentMovement)
            {
                var awaySpeed = Mathf.Max(0, Vector3.Dot(playerVelocity, direction));
                var feedForward = direction * Mathf.Min(awaySpeed, tuning.maximumSpeed);
                next = Vector3.MoveTowards(next, feedForward,
                    tuning.maximumAcceleration * deltaSeconds);
            }
            next = Vector3.ClampMagnitude(next, tuning.maximumSpeed);
            CommandVelocity = Vector3.MoveTowards(previous, next,
                tuning.maximumAcceleration * deltaSeconds);
            return CommandVelocity;
        }

        public void Reset()
        {
            filteredForce = Vector3.zero;
            CommandVelocity = Vector3.zero;
            startTime = 0;
            stopTime = 0;
            Engaged = false;
        }
    }
}
