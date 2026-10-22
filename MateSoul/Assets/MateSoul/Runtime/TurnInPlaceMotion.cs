using UnityEngine;

namespace MateSoul.Runtime
{
    public readonly struct TurnMotionSample
    {
        public readonly bool IsTurning;
        public readonly float RemainingAngle;
        public readonly float AngularSpeed;
        public readonly float DeltaAngle;
        public readonly float NormalizedRate;

        public TurnMotionSample(bool isTurning, float remainingAngle,
            float angularSpeed, float deltaAngle, float normalizedRate)
        {
            IsTurning = isTurning;
            RemainingAngle = remainingAngle;
            AngularSpeed = angularSpeed;
            DeltaAngle = deltaAngle;
            NormalizedRate = normalizedRate;
        }
    }

    // Converts an arbitrary target yaw into a stable, acceleration-limited turn cycle.
    public sealed class TurnInPlaceMotion
    {
        // Small yaw errors may slide gently. A visible corrective step begins only
        // after the accumulated error is large enough to read as intentional.
        public const float EnterAngle = 32f;
        public const float ExitAngle = 7f;
        public const float ReverseAngle = 42f;
        public const float OppositeDirectionLockAngle = 165f;
        public const float ReferenceAngularSpeed = 120f;

        private const float MinimumAngularSpeed = 36f;
        private const float MicroTurnAngularSpeed = 18f;
        private const float FullSpeedAngle = 120f;
        private const float Acceleration = 360f;
        private const float Deceleration = 540f;
        private const float SettledAngularSpeed = .5f;

        private bool active;
        private float direction;
        private float angularSpeed;

        public bool IsTurning => active || Mathf.Abs(angularSpeed) > SettledAngularSpeed;
        public float AngularSpeed => angularSpeed;

        public TurnMotionSample Tick(float signedRemainingAngle,
            float maximumAngularSpeed, float deltaSeconds)
        {
            var remaining = Mathf.DeltaAngle(0, signedRemainingAngle);
            var magnitude = Mathf.Abs(remaining);
            var requestedDirection = Mathf.Sign(remaining);
            if (!active && magnitude >= EnterAngle)
            {
                active = true;
                direction = requestedDirection == 0 ? 1 : requestedDirection;
            }
            else if (active && magnitude <= ExitAngle)
            {
                active = false;
            }
            else if (active && requestedDirection != 0 && requestedDirection != direction)
            {
                if (magnitude < ReverseAngle)
                    active = false;
                else if (magnitude < OppositeDirectionLockAngle)
                    direction = requestedDirection;
            }

            var targetSpeed = 0f;
            if (active)
            {
                var maximum = Mathf.Max(1, Mathf.Abs(maximumAngularSpeed));
                var minimum = Mathf.Min(MinimumAngularSpeed, maximum);
                var factor = Mathf.InverseLerp(ExitAngle, FullSpeedAngle, magnitude);
                factor = factor * factor * (3f - 2f * factor);
                targetSpeed = direction * Mathf.Lerp(minimum, maximum, factor);
            }

            var acceleration = Mathf.Abs(targetSpeed) > Mathf.Abs(angularSpeed)
                ? Acceleration : Deceleration;
            angularSpeed = Mathf.MoveTowards(
                angularSpeed, targetSpeed, acceleration * Mathf.Max(0, deltaSeconds));
            if (!active && Mathf.Abs(angularSpeed) <= SettledAngularSpeed)
                angularSpeed = 0;

            var delta = 0f;
            var directionIsValid = requestedDirection == direction ||
                                   magnitude >= OppositeDirectionLockAngle;
            if (magnitude > 0 && Mathf.Abs(angularSpeed) > SettledAngularSpeed &&
                directionIsValid &&
                Mathf.Sign(angularSpeed) == direction)
            {
                delta = direction * Mathf.Min(
                    Mathf.Abs(angularSpeed) * Mathf.Max(0, deltaSeconds), magnitude);
            }
            else if (!active && angularSpeed == 0 && magnitude > 0)
            {
                delta = requestedDirection * Mathf.Min(
                    MicroTurnAngularSpeed * Mathf.Max(0, deltaSeconds), magnitude);
            }

            return new TurnMotionSample(IsTurning, remaining, angularSpeed, delta,
                Mathf.Clamp(angularSpeed / ReferenceAngularSpeed, -1, 1));
        }

        public void Reset()
        {
            active = false;
            direction = 0;
            angularSpeed = 0;
        }
    }
}
