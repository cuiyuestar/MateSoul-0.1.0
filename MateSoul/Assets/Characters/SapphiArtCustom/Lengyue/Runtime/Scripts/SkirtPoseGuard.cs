using System;
using System.Collections.Generic;
using UnityEngine;

namespace MateSoul.Cloth
{
    /// <summary>
    /// Deterministic late-update guard for the LavenderCasual skirt.
    /// It owns the inner and outer skirt bones; body clips must not contain
    /// skirt curves.
    /// </summary>
    [DefaultExecutionOrder(12000)]
    public sealed class SkirtPoseGuard : MonoBehaviour
    {
        [Range(0f, 120f)] public float maxSupportedFlex = 90f;
        [Range(.5f, 2f)] public float responseMultiplier = 1f;
        [Range(10f, 90f)] public float maxResponseDegrees = 90f;
        [Range(0f, 20f)] public float avoidanceStartDegrees = 3f;
        [Range(1f, 45f)] public float avoidanceFullDegrees = 10f;
        [Range(0f, 20f)] public float innerSideDrapeDegrees = 14f;
        [Range(0f, 20f)] public float outerSideDrapeDegrees = 17f;
        [Range(0f, 15f)] public float frontBackDrapeDegrees;
        [Range(0f, .25f)] public float drapeRadialContraction = .16f;
        [Range(0f, 30f)] public float drapeReleaseStartDegrees = 5f;
        [Range(1f, 45f)] public float drapeReleaseEndDegrees = 14f;
        [Range(0f, 1f)] public float gravityResponse = .5f;
        [Range(0f, 2f)] public float inertiaResponse = .45f;
        [Range(1f, 3f)] public float lateralResponse = 1.5f;
        [Range(1f, 8f)] public float springFrequency = 2.6f;
        [Range(.1f, 1.5f)] public float dampingRatio = .58f;
        [Range(1f, 20f)] public float maxSecondaryDegrees = 7f;
        [Range(0f, 1f)] public float secondaryTipResponse = .48f;
        [Range(1f, 30f)] public float accelerationSmoothing = 10f;
        [Range(1f, 30f)] public float maxAcceleration = 6f;
        public bool autoBind = true;
        public Transform hips;
        public Transform leftLeg;
        public Transform leftKnee;
        public Transform rightLeg;
        public Transform rightKnee;

        public float LastLeftFlex { get; private set; }
        public float LastRightFlex { get; private set; }
        public Vector3 RestLeftDirection => restLeftDirection;
        public Vector3 RestRightDirection => restRightDirection;
        public Vector3 LastLeftDirection { get; private set; }
        public Vector3 LastRightDirection { get; private set; }
        public Vector2 SecondaryAngles => secondaryAngles;
        public float LastGravityTargetAngle { get; private set; }
        public float LastInertiaTargetAngle { get; private set; }
        public float MaxSecondaryAngle { get; private set; }
        public float LastDrapeWeight { get; private set; }
        public float LastAvoidanceWeight { get; private set; }
        public float RestHemHalfWidth { get; private set; }
        public float CurrentHemHalfWidth { get; private set; }
        public float RestHemDepthRadius { get; private set; }
        public float CurrentHemDepthRadius { get; private set; }
        public float LastBaselineHemHalfWidth { get; private set; }
        public float LastBaselineHemDepthRadius { get; private set; }
        public float LastDrapedHemHalfWidth { get; private set; }
        public float LastDrapedHemDepthRadius { get; private set; }
        public bool IsInitialized => initialized;

        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        readonly Dictionary<string, Quaternion> restRotations = new Dictionary<string, Quaternion>();
        readonly Dictionary<string, Vector3> restPositions = new Dictionary<string, Vector3>();
        [SerializeField, HideInInspector] bool hasRestDirections;
        [SerializeField, HideInInspector] Vector3 serializedRestLeftDirection;
        [SerializeField, HideInInspector] Vector3 serializedRestRightDirection;
        Vector3 restLeftDirection;
        Vector3 restRightDirection;
        Vector3 lastHipsPosition;
        Vector3 lastHipsVelocity;
        Vector3 filteredAcceleration;
        Quaternion restHipsRelativeRotation;
        Vector2 secondaryAngles;
        Vector2 secondaryVelocity;
        bool dynamicsInitialized;
        bool initialized;
        Vector3 supportCenter;
        Quaternion supportRotation;
        Vector2 supportHalfSize;
        float supportWeight;

        public void SetSeatSupport(Vector3 center, Quaternion rotation, Vector2 halfSize, float weight)
        {
            supportCenter = center;
            supportRotation = rotation;
            supportHalfSize = halfSize;
            supportWeight = Mathf.Clamp01(weight);
        }

        static readonly string[] SkirtBones = {
            "Skirt_Front_01", "Skirt_Front_02",
            "Skirt_FrontSide_01_L", "Skirt_FrontSide_02_L",
            "Skirt_Side_01_L", "Skirt_Side_02_L",
            "Skirt_BackSide_01_L", "Skirt_BackSide_02_L",
            "Skirt_Back_01", "Skirt_Back_02",
            "Skirt_BackSide_01_R", "Skirt_BackSide_02_R",
            "Skirt_Side_01_R", "Skirt_Side_02_R",
            "Skirt_FrontSide_01_R", "Skirt_FrontSide_02_R",
            "OuterSkirt_Front_01_L", "OuterSkirt_Front_02_L",
            "OuterSkirt_Side_01_L", "OuterSkirt_Side_02_L",
            "OuterSkirt_Back_01_L", "OuterSkirt_Back_02_L",
            "OuterSkirt_Back_03_L",
            "OuterSkirt_Front_01_R", "OuterSkirt_Front_02_R",
            "OuterSkirt_Side_01_R", "OuterSkirt_Side_02_R",
            "OuterSkirt_Back_01_R", "OuterSkirt_Back_02_R",
            "OuterSkirt_Back_03_R"
        };

        static readonly string[] SecondaryRoots = {
            "Skirt_Front_01",
            "Skirt_FrontSide_01_L", "Skirt_Side_01_L",
            "Skirt_BackSide_01_L", "Skirt_Back_01",
            "Skirt_BackSide_01_R", "Skirt_Side_01_R",
            "Skirt_FrontSide_01_R",
            "OuterSkirt_Front_01_L", "OuterSkirt_Side_01_L",
            "OuterSkirt_Back_01_L",
            "OuterSkirt_Front_01_R", "OuterSkirt_Side_01_R",
            "OuterSkirt_Back_01_R"
        };

        static readonly string[] SecondaryTips = {
            "Skirt_Front_02",
            "Skirt_FrontSide_02_L", "Skirt_Side_02_L",
            "Skirt_BackSide_02_L", "Skirt_Back_02",
            "Skirt_BackSide_02_R", "Skirt_Side_02_R",
            "Skirt_FrontSide_02_R",
            "OuterSkirt_Front_02_L", "OuterSkirt_Side_02_L",
            "OuterSkirt_Back_02_L", "OuterSkirt_Back_03_L",
            "OuterSkirt_Front_02_R", "OuterSkirt_Side_02_R",
            "OuterSkirt_Back_02_R", "OuterSkirt_Back_03_R"
        };

        static readonly string[] DrapeMidBones = {
            "Skirt_Front_02",
            "Skirt_FrontSide_02_L", "Skirt_Side_02_L",
            "Skirt_BackSide_02_L", "Skirt_Back_02",
            "Skirt_BackSide_02_R", "Skirt_Side_02_R",
            "Skirt_FrontSide_02_R",
            "OuterSkirt_Front_02_L", "OuterSkirt_Side_02_L",
            "OuterSkirt_Back_02_L",
            "OuterSkirt_Front_02_R", "OuterSkirt_Side_02_R",
            "OuterSkirt_Back_02_R"
        };

        void Awake()
        {
            if (autoBind) BindByName();
            Initialize();
        }

        public void SetActiveForOutfit(bool active)
        {
            if (!active)
            {
                if (enabled) enabled = false;
                else RestoreRestPose();
                return;
            }
            enabled = true;
            if (autoBind) BindByName();
            Initialize();
            ResetDynamics();
        }

        public void RestoreRestPose()
        {
            if (!initialized || !HasAllBones()) return;
            foreach (string name in SkirtBones)
            {
                bones[name].localRotation = restRotations[name];
                bones[name].localPosition = restPositions[name];
            }
        }

        bool HasAllBones()
        {
            if (bones.Count != SkirtBones.Length) return false;
            foreach (string name in SkirtBones)
                if (!bones.TryGetValue(name, out Transform bone) || !bone) return false;
            return true;
        }

        public void BindByName()
        {
            var lookup = new Dictionary<string, Transform>();
            foreach (var item in transform.root.GetComponentsInChildren<Transform>(true))
                if (!lookup.ContainsKey(item.name)) lookup.Add(item.name, item);
            Transform Required(string name)
            {
                if (!lookup.TryGetValue(name, out var value))
                    throw new InvalidOperationException("Missing pose-guard bone: " + name);
                return value;
            }
            hips = Required("Hips");
            leftLeg = Required("Leg_L");
            leftKnee = Required("Knee_L");
            rightLeg = Required("Leg_R");
            rightKnee = Required("Knee_R");
            bones.Clear();
            foreach (string name in SkirtBones) bones.Add(name, Required(name));
        }

        public void Initialize()
        {
            if (!hips || !leftLeg || !leftKnee || !rightLeg || !rightKnee
                || bones.Count != SkirtBones.Length)
            {
                initialized = false;
                return;
            }
            if (initialized && restRotations.Count == SkirtBones.Length)
                foreach (string name in SkirtBones)
                {
                    bones[name].localRotation = restRotations[name];
                    bones[name].localPosition = restPositions[name];
                }
            restRotations.Clear();
            restPositions.Clear();
            foreach (string name in SkirtBones)
            {
                restRotations.Add(name, bones[name].localRotation);
                restPositions.Add(name, bones[name].localPosition);
            }
            if (!hasRestDirections)
            {
                serializedRestLeftDirection = transform.InverseTransformDirection(
                    (leftKnee.position - leftLeg.position).normalized);
                serializedRestRightDirection = transform.InverseTransformDirection(
                    (rightKnee.position - rightLeg.position).normalized);
                hasRestDirections = true;
            }
            restLeftDirection = serializedRestLeftDirection;
            restRightDirection = serializedRestRightDirection;
            restHipsRelativeRotation =
                Quaternion.Inverse(transform.rotation) * hips.rotation;
            MeasureHemRadii(
                out float restHalfWidth,
                out float restDepthRadius);
            RestHemHalfWidth = restHalfWidth;
            RestHemDepthRadius = restDepthRadius;
            CurrentHemHalfWidth = restHalfWidth;
            CurrentHemDepthRadius = restDepthRadius;
            initialized = true;
            ResetDynamics();
        }

        public void ResetDynamics()
        {
            secondaryAngles = Vector2.zero;
            secondaryVelocity = Vector2.zero;
            filteredAcceleration = Vector3.zero;
            lastHipsPosition = hips ? hips.position : Vector3.zero;
            lastHipsVelocity = Vector3.zero;
            LastGravityTargetAngle = 0;
            LastInertiaTargetAngle = 0;
            MaxSecondaryAngle = 0;
            LastDrapeWeight = 0;
            LastAvoidanceWeight = 0;
            dynamicsInitialized = hips != null;
        }

        static float SagittalAngle(Vector3 direction) =>
            Mathf.Atan2(direction.z, -direction.y) * Mathf.Rad2Deg;

        float Flex(Transform leg, Transform knee, Vector3 rest)
        {
            Vector3 current = transform.InverseTransformDirection(
                (knee.position - leg.position).normalized);
            return Mathf.Clamp(Mathf.DeltaAngle(SagittalAngle(rest),
                SagittalAngle(current)), -maxSupportedFlex, maxSupportedFlex);
        }

        void RotateWorld(string name, Vector3 axis, float degrees)
        {
            var bone = bones[name];
            bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }

        void LateUpdate() => Evaluate(Time.deltaTime);

        public void Evaluate() =>
            Evaluate(Time.deltaTime > 0 ? Time.deltaTime : 1f / 60f);

        public void Evaluate(float deltaTime)
        {
            if (!initialized || !HasAllBones())
            {
                if (autoBind) BindByName();
                Initialize();
            }
            if (!initialized) return;
            foreach (string name in SkirtBones)
            {
                bones[name].localRotation = restRotations[name];
                bones[name].localPosition = restPositions[name];
            }

            LastLeftDirection = transform.InverseTransformDirection(
                (leftKnee.position - leftLeg.position).normalized);
            LastRightDirection = transform.InverseTransformDirection(
                (rightKnee.position - rightLeg.position).normalized);
            LastLeftFlex = Flex(leftLeg, leftKnee, restLeftDirection);
            LastRightFlex = Flex(rightLeg, rightKnee, restRightDirection);
            float maximumFlex = Mathf.Max(
                Mathf.Abs(LastLeftFlex),
                Mathf.Abs(LastRightFlex));
            LastDrapeWeight = 1f - SmoothRange(
                maximumFlex,
                drapeReleaseStartDegrees,
                drapeReleaseEndDegrees);
            LastAvoidanceWeight = Mathf.Max(
                SmoothRange(
                    Mathf.Abs(LastLeftFlex),
                    avoidanceStartDegrees,
                    avoidanceFullDegrees),
                SmoothRange(
                    Mathf.Abs(LastRightFlex),
                    avoidanceStartDegrees,
                    avoidanceFullDegrees));
            float leftForward = Response(LastLeftFlex);
            float rightForward = Response(LastRightFlex);
            float leftBack = Response(-LastLeftFlex);
            float rightBack = Response(-LastRightFlex);
            float sharedForward = Mathf.Max(leftForward, rightForward);
            float sharedBack = Mathf.Max(leftBack, rightBack);
            Quaternion secondaryFrame = SecondaryFrameRotation();
            Vector3 rightAxis =
                secondaryFrame * Vector3.right;
            Vector3 forwardAxis =
                secondaryFrame * Vector3.forward;

            MeasureHemRadii(
                out float baselineHalfWidth,
                out float baselineDepthRadius);
            LastBaselineHemHalfWidth = baselineHalfWidth;
            LastBaselineHemDepthRadius = baselineDepthRadius;
            ApplyDrape(
                LastDrapeWeight, rightAxis, forwardAxis);
            MeasureHemRadii(
                out float drapedHalfWidth,
                out float drapedDepthRadius);
            LastDrapedHemHalfWidth = drapedHalfWidth;
            LastDrapedHemDepthRadius = drapedDepthRadius;
            RotateWorld("Skirt_Front_01", rightAxis, -sharedForward * .70f);
            RotateWorld("Skirt_Front_02", rightAxis, -sharedForward * .15f);
            ApplySide("L", -1f, leftForward, leftBack, rightAxis, forwardAxis);
            ApplySide("R", 1f, rightForward, rightBack, rightAxis, forwardAxis);
            RotateWorld("Skirt_Back_01", rightAxis, sharedBack * .30f);
            RotateWorld("Skirt_Back_02", rightAxis, sharedBack * .08f);
            ApplyOuterSide(
                "L", -1f, leftForward, leftBack,
                rightAxis, forwardAxis);
            ApplyOuterSide(
                "R", 1f, rightForward, rightBack,
                rightAxis, forwardAxis);
            UpdateSecondaryMotion(deltaTime);
            ApplySecondaryMotion(rightAxis, forwardAxis);
            ApplySeatSupport();
            MeasureHemRadii(
                out float halfWidth,
                out float depthRadius);
            CurrentHemHalfWidth = halfWidth;
            CurrentHemDepthRadius = depthRadius;
        }

        void ApplySeatSupport()
        {
            if (supportWeight <= 0) return;
            var inverse = Quaternion.Inverse(supportRotation);
            // The cloth owner applies contact after its own drape and inertia.
            // Imported meshes and source bone transforms remain unchanged.
            foreach (var name in SkirtBones)
            {
                var bone = bones[name];
                var point = inverse * (bone.position - supportCenter);
                if (Mathf.Abs(point.x) > supportHalfSize.x + .04f ||
                    Mathf.Abs(point.z) > supportHalfSize.y + .04f || point.y >= .018f) continue;
                var position = bone.position;
                position.y = Mathf.Lerp(position.y, supportCenter.y + .018f, supportWeight);
                bone.position = position;
            }
        }

        void UpdateSecondaryMotion(float deltaTime)
        {
            float dt = Mathf.Clamp(deltaTime, 1f / 240f, .05f);
            if (!dynamicsInitialized ||
                Vector3.Distance(lastHipsPosition, hips.position) > .35f)
            {
                ResetDynamics();
                return;
            }

            Vector3 hipsVelocity =
                (hips.position - lastHipsPosition) / dt;
            Vector3 acceleration =
                (hipsVelocity - lastHipsVelocity) / dt;
            acceleration = Vector3.ClampMagnitude(
                acceleration, maxAcceleration);
            float blend = 1f - Mathf.Exp(
                -accelerationSmoothing * dt);
            filteredAcceleration = Vector3.Lerp(
                filteredAcceleration, acceleration, blend);
            lastHipsPosition = hips.position;
            lastHipsVelocity = hipsVelocity;

            Quaternion frame = SecondaryFrameRotation();
            Vector3 localGravity =
                Quaternion.Inverse(frame) * Vector3.down;
            Vector3 effectiveDown =
                Vector3.down * Physics.gravity.magnitude -
                filteredAcceleration;
            if (effectiveDown.sqrMagnitude < 1e-6f)
                effectiveDown = Vector3.down;
            Vector3 localEffective =
                Quaternion.Inverse(frame) *
                effectiveDown.normalized;
            Vector2 gravityTarget =
                TiltFromDown(localGravity) * gravityResponse;
            Vector2 inertiaTarget =
                (TiltFromDown(localEffective) -
                    TiltFromDown(localGravity)) * inertiaResponse;
            gravityTarget.y *= lateralResponse;
            inertiaTarget.y *= lateralResponse;
            Vector2 target = Vector2.ClampMagnitude(
                gravityTarget + inertiaTarget,
                maxSecondaryDegrees);
            LastGravityTargetAngle = gravityTarget.magnitude;
            LastInertiaTargetAngle = inertiaTarget.magnitude;

            float omega = 2f * Mathf.PI * springFrequency;
            int substeps = Mathf.Max(
                1, Mathf.CeilToInt(dt / SecondaryStepSeconds));
            float step = dt / substeps;
            for (int i = 0; i < substeps; i++)
            {
                Vector2 accelerationStep =
                    (target - secondaryAngles) * (omega * omega) -
                    secondaryVelocity *
                    (2f * dampingRatio * omega);
                secondaryVelocity += accelerationStep * step;
                secondaryAngles += secondaryVelocity * step;
                if (secondaryAngles.magnitude >
                    maxSecondaryDegrees)
                {
                    secondaryAngles = secondaryAngles.normalized *
                        maxSecondaryDegrees;
                    secondaryVelocity *= .25f;
                }
            }
            MaxSecondaryAngle = Mathf.Max(
                MaxSecondaryAngle, secondaryAngles.magnitude);
        }

        const float SecondaryStepSeconds = 1f / 90f;

        Quaternion SecondaryFrameRotation()
        {
            return hips.rotation *
                Quaternion.Inverse(restHipsRelativeRotation);
        }

        static Vector2 TiltFromDown(Vector3 localDirection)
        {
            return new Vector2(
                -Mathf.Atan2(
                    localDirection.z, -localDirection.y) *
                    Mathf.Rad2Deg,
                Mathf.Atan2(
                    localDirection.x, -localDirection.y) *
                    Mathf.Rad2Deg);
        }

        void ApplySecondaryMotion(
            Vector3 rightAxis, Vector3 forwardAxis)
        {
            foreach (string name in SecondaryRoots)
            {
                float weight = SecondaryRootWeight(name);
                RotateWorld(
                    name, rightAxis,
                    secondaryAngles.x * weight);
                RotateWorld(
                    name, forwardAxis,
                    secondaryAngles.y * weight);
            }
            foreach (string name in SecondaryTips)
            {
                float weight =
                    SecondaryRootWeight(name) *
                    secondaryTipResponse;
                RotateWorld(
                    name, rightAxis,
                    secondaryAngles.x * weight);
                RotateWorld(
                    name, forwardAxis,
                    secondaryAngles.y * weight);
            }
        }

        static float SecondaryRootWeight(string name)
        {
            float weight = name.Contains("_Side_")
                ? 1.12f
                : name.Contains("FrontSide") ||
                    name.Contains("BackSide")
                    ? .98f
                    : .84f;
            return name.StartsWith(
                "OuterSkirt_", StringComparison.Ordinal)
                ? weight * 1.06f
                : weight;
        }

        static float SmoothRange(
            float value, float start, float end)
        {
            float upper = Mathf.Max(start + .001f, end);
            float t = Mathf.InverseLerp(start, upper, value);
            return t * t * (3f - 2f * t);
        }

        float Response(float flex)
        {
            float positive = Mathf.Max(0, flex);
            float activation = SmoothRange(
                positive,
                avoidanceStartDegrees,
                avoidanceFullDegrees);
            return Mathf.Min(
                positive * responseMultiplier * activation,
                maxResponseDegrees);
        }

        void ApplyDrape(
            float weight, Vector3 rightAxis,
            Vector3 forwardAxis)
        {
            if (weight <= 0) return;
            float frontBack = frontBackDrapeDegrees * weight;
            float innerSide = innerSideDrapeDegrees * weight;
            float outerSide = outerSideDrapeDegrees * weight;

            ApplyDrapeContraction(weight);
            RotateWorld(
                "Skirt_Front_01", rightAxis, frontBack);
            RotateWorld(
                "Skirt_Front_02", rightAxis, frontBack * .35f);
            RotateWorld(
                "Skirt_Back_01", rightAxis, -frontBack);
            RotateWorld(
                "Skirt_Back_02", rightAxis, -frontBack * .35f);
            ApplyInnerDrapeSide(
                "L", -1f, innerSide, frontBack,
                rightAxis, forwardAxis);
            ApplyInnerDrapeSide(
                "R", 1f, innerSide, frontBack,
                rightAxis, forwardAxis);
            ApplyOuterDrapeSide(
                "L", -1f, outerSide, frontBack,
                rightAxis, forwardAxis);
            ApplyOuterDrapeSide(
                "R", 1f, outerSide, frontBack,
                rightAxis, forwardAxis);
        }

        void ApplyInnerDrapeSide(
            string side, float sign, float sideDrape,
            float frontBackDrape, Vector3 rightAxis,
            Vector3 forwardAxis)
        {
            RotateWorld(
                "Skirt_FrontSide_01_" + side,
                rightAxis, frontBackDrape * .62f);
            RotateWorld(
                "Skirt_FrontSide_02_" + side,
                rightAxis, frontBackDrape * .22f);
            RotateWorld(
                "Skirt_FrontSide_01_" + side,
                forwardAxis, -sign * sideDrape * .58f);
            RotateWorld(
                "Skirt_FrontSide_02_" + side,
                forwardAxis, -sign * sideDrape * .20f);
            RotateWorld(
                "Skirt_Side_01_" + side,
                forwardAxis, -sign * sideDrape);
            RotateWorld(
                "Skirt_Side_02_" + side,
                forwardAxis, -sign * sideDrape * .38f);
            RotateWorld(
                "Skirt_BackSide_01_" + side,
                rightAxis, -frontBackDrape * .62f);
            RotateWorld(
                "Skirt_BackSide_02_" + side,
                rightAxis, -frontBackDrape * .22f);
            RotateWorld(
                "Skirt_BackSide_01_" + side,
                forwardAxis, -sign * sideDrape * .58f);
            RotateWorld(
                "Skirt_BackSide_02_" + side,
                forwardAxis, -sign * sideDrape * .20f);
        }

        void ApplyOuterDrapeSide(
            string side, float sign, float sideDrape,
            float frontBackDrape, Vector3 rightAxis,
            Vector3 forwardAxis)
        {
            RotateWorld(
                "OuterSkirt_Front_01_" + side,
                rightAxis, frontBackDrape);
            RotateWorld(
                "OuterSkirt_Front_02_" + side,
                rightAxis, frontBackDrape * .40f);
            RotateWorld(
                "OuterSkirt_Front_01_" + side,
                forwardAxis, -sign * sideDrape * .46f);
            RotateWorld(
                "OuterSkirt_Front_02_" + side,
                forwardAxis, -sign * sideDrape * .18f);
            RotateWorld(
                "OuterSkirt_Side_01_" + side,
                forwardAxis, -sign * sideDrape);
            RotateWorld(
                "OuterSkirt_Side_02_" + side,
                forwardAxis, -sign * sideDrape * .42f);
            RotateWorld(
                "OuterSkirt_Back_01_" + side,
                rightAxis, -frontBackDrape);
            RotateWorld(
                "OuterSkirt_Back_02_" + side,
                rightAxis, -frontBackDrape * .34f);
            RotateWorld(
                "OuterSkirt_Back_03_" + side,
                rightAxis, -frontBackDrape * .12f);
            RotateWorld(
                "OuterSkirt_Back_01_" + side,
                forwardAxis, -sign * sideDrape * .46f);
            RotateWorld(
                "OuterSkirt_Back_02_" + side,
                forwardAxis, -sign * sideDrape * .18f);
        }

        void ApplyDrapeContraction(float weight)
        {
            float lateralScale =
                1f - drapeRadialContraction * weight;
            float depthScale =
                1f - drapeRadialContraction * .72f * weight;
            foreach (string name in DrapeMidBones)
            {
                Transform bone = bones[name];
                Vector3 local = transform.InverseTransformPoint(
                    bone.position);
                local.x *= lateralScale;
                local.z *= depthScale;
                bone.position = transform.TransformPoint(local);
            }
        }

        void MeasureHemRadii(
            out float halfWidth, out float depthRadius)
        {
            halfWidth = 0;
            depthRadius = 0;
            string[] sideLandmarks = {
                "OuterSkirt_Side_02_L",
                "OuterSkirt_Side_02_R"
            };
            foreach (string name in sideLandmarks)
            {
                Vector3 local = transform.InverseTransformPoint(
                    bones[name].position);
                halfWidth = Mathf.Max(
                    halfWidth, Mathf.Abs(local.x));
            }
            string[] depthLandmarks = {
                "OuterSkirt_Front_02_L",
                "OuterSkirt_Front_02_R",
                "OuterSkirt_Back_03_L",
                "OuterSkirt_Back_03_R"
            };
            foreach (string name in depthLandmarks)
            {
                Vector3 local = transform.InverseTransformPoint(
                    bones[name].position);
                depthRadius = Mathf.Max(
                    depthRadius, Mathf.Abs(local.z));
            }
        }

        void ApplySide(string side, float sign, float forward, float backward,
            Vector3 rightAxis, Vector3 forwardAxis)
        {
            RotateWorld("Skirt_FrontSide_01_" + side, rightAxis, -forward * .78f);
            RotateWorld("Skirt_FrontSide_02_" + side, rightAxis, -forward * .18f);
            RotateWorld("Skirt_Side_01_" + side, rightAxis, -forward * .10f);
            RotateWorld("Skirt_Side_02_" + side, rightAxis, -forward * .03f);
            RotateWorld("Skirt_FrontSide_01_" + side, forwardAxis, sign * forward * .06f);
            RotateWorld("Skirt_BackSide_01_" + side, rightAxis, backward * .55f);
            RotateWorld("Skirt_BackSide_02_" + side, rightAxis, backward * .15f);
        }

        void ApplyOuterSide(
            string side, float sign, float forward, float backward,
            Vector3 rightAxis, Vector3 forwardAxis)
        {
            RotateWorld(
                "OuterSkirt_Front_01_" + side,
                rightAxis, -forward * .62f);
            RotateWorld(
                "OuterSkirt_Front_02_" + side,
                rightAxis, -forward * .14f);
            RotateWorld(
                "OuterSkirt_Side_01_" + side,
                rightAxis, -forward * .12f + backward * .16f);
            RotateWorld(
                "OuterSkirt_Side_02_" + side,
                rightAxis, -forward * .04f + backward * .05f);
            RotateWorld(
                "OuterSkirt_Back_01_" + side,
                rightAxis, backward * .45f);
            RotateWorld(
                "OuterSkirt_Back_02_" + side,
                rightAxis, backward * .14f);
            RotateWorld(
                "OuterSkirt_Back_03_" + side,
                rightAxis, backward * .05f);
            RotateWorld(
                "OuterSkirt_Side_01_" + side,
                forwardAxis, sign * (forward + backward) * .06f);
        }

        void OnDisable()
        {
            RestoreRestPose();
            secondaryAngles = Vector2.zero;
            secondaryVelocity = Vector2.zero;
            filteredAcceleration = Vector3.zero;
            dynamicsInitialized = false;
        }
    }
}
