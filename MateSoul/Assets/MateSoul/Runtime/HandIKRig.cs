using System;
using MateSoul.Core;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace MateSoul.Runtime
{
    public readonly struct HandHoldRigSample
    {
        public readonly Vector3 TensionDirection;
        public readonly float Extension;
        public readonly float ReachUtilization;
        public readonly float RotationUtilization;
        public readonly float TargetSeparation;
        public readonly bool TrackingJump;

        public HandHoldRigSample(Vector3 tensionDirection, float extension,
            float reachUtilization, float rotationUtilization,
            float targetSeparation, bool trackingJump)
        {
            TensionDirection = tensionDirection;
            Extension = extension;
            ReachUtilization = reachUtilization;
            RotationUtilization = rotationUtilization;
            TargetSeparation = targetSeparation;
            TrackingJump = trackingJump;
        }
    }

    [DisallowMultipleComponent]
    public sealed class HandIKRig : MonoBehaviour
    {
        [Serializable]
        private sealed class Arm
        {
            public AvatarHandSide side;
            public Transform upperArm;
            public Transform forearm;
            public Transform hand;
            public Transform index;
            public Transform middle;
            public Transform pinky;
            public Transform forearmTwist;
            public Transform target;
            public Transform hint;
            public HandHoldInteractable socket;
            public TwoBoneIKConstraint constraint;
            public LineRenderer flexionArc;
            public LineRenderer deviationArc;
            public LineRenderer twistArc;

            [NonSerialized] public float length;
            [NonSerialized] public Vector3 twistAxis;
            [NonSerialized] public Vector3 flexAxis;
            [NonSerialized] public Vector3 poleInRoot;
            [NonSerialized] public Vector3 socketLocalPosition;
            [NonSerialized] public Quaternion socketLocalRotation;
            [NonSerialized] public Quaternion baseHandInForearm;
            [NonSerialized] public Quaternion twistRest;
            [NonSerialized] public float latestTwist;
        }

        [SerializeField] private Arm left = new Arm { side = AvatarHandSide.Left };
        [SerializeField] private Arm right = new Arm { side = AvatarHandSide.Right };
        [SerializeField] private Transform worldTargets;
        [SerializeField] private HandHoldTuning tuning;
        private GameSession session;
        private Transform playerHead;
        private HandHoldRequest activeRequest;
        private Arm activeArm;
        private Vector3 gripOffsetPosition;
        private Quaternion gripOffsetRotation;
        private Vector3 filteredGripPosition;
        private Quaternion filteredGripRotation;
        private Vector3 previousInteractorPosition;
        private float weight;

        public bool IsAvailable => session != null && !session.Paused && isActiveAndEnabled;
        public bool IsConfigured => IsArmConfigured(left) && IsArmConfigured(right) && worldTargets != null;
        public HandHoldTuning Tuning => tuning;
        public float Weight => weight;

        public void ConfigureArm(AvatarHandSide side, Transform upperArm, Transform forearm,
            Transform hand, Transform index, Transform middle, Transform pinky,
            Transform forearmTwist, Transform target, Transform hint,
            HandHoldInteractable socket, TwoBoneIKConstraint constraint,
            LineRenderer flexionArc, LineRenderer deviationArc, LineRenderer twistArc)
        {
            var arm = side == AvatarHandSide.Left ? left : right;
            arm.side = side;
            arm.upperArm = upperArm;
            arm.forearm = forearm;
            arm.hand = hand;
            arm.index = index;
            arm.middle = middle;
            arm.pinky = pinky;
            arm.forearmTwist = forearmTwist;
            arm.target = target;
            arm.hint = hint;
            arm.socket = socket;
            arm.constraint = constraint;
            arm.flexionArc = flexionArc;
            arm.deviationArc = deviationArc;
            arm.twistArc = twistArc;
            socket.Configure(side, this);
        }

        public void Configure(Transform targetRoot, HandHoldTuning value)
        {
            worldTargets = targetRoot;
            value.Validate();
            tuning = value;
        }

        public void Bind(GameSession gameSession, Transform head)
        {
            session = gameSession ?? throw new ArgumentNullException(nameof(gameSession));
            playerHead = head;
            if (!IsConfigured) throw new InvalidOperationException("Hand-hold rig is incomplete.");
        }

        private void Awake()
        {
            if (tuning.softReachRatio <= 0) tuning = HandHoldTuning.Default;
            tuning.Validate();
            SetWeightImmediate(0);
            SetDisplay(left, false);
            SetDisplay(right, false);
            if (IsArmConfigured(left)) CacheSocketPose(left);
            if (IsArmConfigured(right)) CacheSocketPose(right);
        }

        public ActionHandle RequestHold(HandHoldRequest request)
        {
            if (!IsAvailable || request == null) return null;
            var id = request.Side == AvatarHandSide.Left ? HandHoldAction.LeftId : HandHoldAction.RightId;
            return session.Submit(new AvatarIntent(id, IntentSource.Player, payload: request));
        }

        public void RequestRelease(ActionHandle handle)
        {
            if (session != null && handle != null && !handle.IsFinished)
                session.Avatar.Cancel(handle);
        }

        public string Check(HandHoldRequest request)
        {
            if (!IsConfigured) return "角色牵手骨骼未完整配置。";
            if (request == null || !request.TryGetInteractorPose(out _))
                return "玩家手部跟踪不可用。";
            if (activeRequest != null && !ReferenceEquals(activeRequest, request))
                return "角色一次只能牵住一只手。";
            return null;
        }

        public void BeginHold(HandHoldRequest request)
        {
            var error = Check(request);
            if (error != null) throw new InvalidOperationException(error);
            request.TryGetInteractorPose(out var interactor);
            activeRequest = request;
            activeArm = request.Side == AvatarHandSide.Left ? left : right;
            InitializeArm(activeArm);

            gripOffsetPosition = Quaternion.Inverse(interactor.rotation) *
                                 (activeArm.socket.transform.position - interactor.position);
            gripOffsetRotation = Quaternion.Inverse(interactor.rotation) *
                                 activeArm.socket.transform.rotation;
            filteredGripPosition = activeArm.socket.transform.position;
            filteredGripRotation = activeArm.socket.transform.rotation;
            previousInteractorPosition = interactor.position;
            activeArm.target.SetPositionAndRotation(activeArm.hand.position, activeArm.hand.rotation);
            UpdateHint(activeArm, activeArm.hand.position);
            SetDisplay(activeArm, true);
            SetWeightImmediate(0);
        }

        public HandHoldRigSample UpdateTarget(Pose interactor, float deltaSeconds)
        {
            if (activeArm == null) throw new InvalidOperationException("No hand-hold target is active.");
            var jump = Vector3.Distance(previousInteractorPosition, interactor.position) >
                       tuning.trackingJumpDistance;
            previousInteractorPosition = interactor.position;

            var desiredGripPosition = interactor.position + interactor.rotation * gripOffsetPosition;
            var desiredGripRotation = interactor.rotation * gripOffsetRotation;
            filteredGripPosition = Vector3.Lerp(filteredGripPosition, desiredGripPosition,
                HandHoldMath.ExpSmoothing(tuning.positionResponse, deltaSeconds));
            filteredGripRotation = Quaternion.Slerp(filteredGripRotation, desiredGripRotation,
                HandHoldMath.ExpSmoothing(tuning.rotationResponse, deltaSeconds));

            var desiredWristRotation = filteredGripRotation *
                                       Quaternion.Inverse(activeArm.socketLocalRotation);
            var desiredWristPosition = filteredGripPosition -
                                       desiredWristRotation * activeArm.socketLocalPosition;
            var reach = HandHoldMath.ConstrainReach(activeArm.upperArm.position,
                desiredWristPosition, activeArm.length,
                tuning.softReachRatio, tuning.hardReachRatio);

            var desiredHandInForearm = Quaternion.Inverse(activeArm.forearm.rotation) *
                                       desiredWristRotation;
            var desiredDelta = Quaternion.Inverse(activeArm.baseHandInForearm) *
                               desiredHandInForearm;
            var wrist = HandHoldMath.ConstrainWristDelta(desiredDelta,
                activeArm.twistAxis, activeArm.flexAxis, tuning.wrist);
            var constrainedRotation = activeArm.forearm.rotation *
                                      activeArm.baseHandInForearm * wrist.Rotation;
            activeArm.target.SetPositionAndRotation(reach.Position, constrainedRotation);
            activeArm.latestTwist = wrist.TwistDegrees;
            UpdateHint(activeArm, reach.Position);
            UpdateDisplay(activeArm, wrist.Utilization);

            return new HandHoldRigSample(reach.Direction, reach.Extension,
                reach.Utilization, wrist.Utilization, reach.Separation, jump);
        }

        public bool BlendTowards(float target, float deltaSeconds)
        {
            var duration = target > weight ? tuning.blendInSeconds : tuning.blendOutSeconds;
            weight = Mathf.MoveTowards(weight, Mathf.Clamp01(target), deltaSeconds / duration);
            if (activeArm != null) activeArm.constraint.weight = Mathf.SmoothStep(0, 1, weight);
            return Mathf.Approximately(weight, target);
        }

        public void EndHold()
        {
            if (activeArm != null)
            {
                activeArm.constraint.weight = 0;
                if (activeArm.forearmTwist != null)
                    activeArm.forearmTwist.localRotation = activeArm.twistRest;
                SetDisplay(activeArm, false);
            }
            weight = 0;
            activeArm = null;
            activeRequest = null;
        }

        private void LateUpdate()
        {
            if (activeArm != null && activeArm.forearmTwist != null && weight > 0)
                activeArm.forearmTwist.localRotation =
                    Quaternion.AngleAxis(activeArm.latestTwist * .45f, activeArm.twistAxis) *
                    activeArm.twistRest;
            SyncSocket(left);
            SyncSocket(right);
        }

        private void InitializeArm(Arm arm)
        {
            arm.length = Vector3.Distance(arm.upperArm.position, arm.forearm.position) +
                         Vector3.Distance(arm.forearm.position, arm.hand.position);
            CacheSocketPose(arm);
            arm.baseHandInForearm = Quaternion.Inverse(arm.forearm.rotation) * arm.hand.rotation;
            arm.twistAxis = arm.forearm.InverseTransformDirection(
                arm.hand.position - arm.forearm.position).normalized;
            var across = arm.index.position - arm.pinky.position;
            arm.flexAxis = arm.forearm.InverseTransformDirection(across);
            arm.flexAxis = Vector3.ProjectOnPlane(arm.flexAxis, arm.twistAxis).normalized;
            var line = (arm.hand.position - arm.upperArm.position).normalized;
            var pole = Vector3.ProjectOnPlane(arm.forearm.position - arm.upperArm.position, line);
            if (pole.sqrMagnitude < .0001f)
                pole = transform.right * (arm.side == AvatarHandSide.Left ? -1 : 1);
            arm.poleInRoot = transform.InverseTransformDirection(pole.normalized);
            if (arm.forearmTwist != null) arm.twistRest = arm.forearmTwist.localRotation;
        }

        private static void CacheSocketPose(Arm arm)
        {
            arm.socketLocalPosition = arm.hand.InverseTransformPoint(arm.socket.transform.position);
            arm.socketLocalRotation = Quaternion.Inverse(arm.hand.rotation) *
                                      arm.socket.transform.rotation;
        }

        private static void SyncSocket(Arm arm)
        {
            if (!IsArmConfigured(arm)) return;
            arm.socket.transform.SetPositionAndRotation(
                arm.hand.TransformPoint(arm.socketLocalPosition),
                arm.hand.rotation * arm.socketLocalRotation);
        }

        private void UpdateHint(Arm arm, Vector3 wrist)
        {
            var offset = wrist - arm.upperArm.position;
            var direction = offset.sqrMagnitude > .000001f ? offset.normalized : transform.forward;
            var pole = Vector3.ProjectOnPlane(transform.TransformDirection(arm.poleInRoot), direction);
            if (pole.sqrMagnitude < .0001f)
                pole = Vector3.ProjectOnPlane(transform.forward, direction);
            arm.hint.position = arm.upperArm.position + direction * (arm.length * .45f) +
                                pole.normalized * (arm.length * .55f);
        }

        private void UpdateDisplay(Arm arm, float utilization)
        {
            var color = utilization <= tuning.wrist.softLimitRatio
                ? new Color(.15f, .9f, .45f, .8f)
                : Color.Lerp(new Color(1f, .75f, .1f, .9f), new Color(1f, .12f, .08f, .95f),
                    Mathf.InverseLerp(tuning.wrist.softLimitRatio, 1, Mathf.Clamp01(utilization)));
            var center = arm.hand.position;
            var twist = arm.forearm.TransformDirection(arm.twistAxis).normalized;
            var flex = arm.forearm.TransformDirection(arm.flexAxis).normalized;
            var deviation = Vector3.Cross(twist, flex).normalized;
            DrawArc(arm.flexionArc, center, flex, twist, .065f,
                -tuning.wrist.extensionDegrees, tuning.wrist.flexionDegrees, color);
            DrawArc(arm.deviationArc, center, deviation, twist, .072f,
                -tuning.wrist.radialDeviationDegrees, tuning.wrist.ulnarDeviationDegrees, color);
            DrawArc(arm.twistArc, center - twist * .025f, twist, flex, .038f,
                -tuning.wrist.supinationDegrees, tuning.wrist.pronationDegrees, color);
        }

        private static void DrawArc(LineRenderer line, Vector3 center, Vector3 axis,
            Vector3 radial, float radius, float minimum, float maximum, Color color)
        {
            if (line == null) return;
            const int segments = 25;
            line.enabled = true;
            line.positionCount = segments;
            line.startColor = color;
            line.endColor = color;
            radial = Vector3.ProjectOnPlane(radial, axis).normalized * radius;
            for (var index = 0; index < segments; index++)
            {
                var amount = (float)index / (segments - 1);
                line.SetPosition(index, center +
                    Quaternion.AngleAxis(Mathf.Lerp(minimum, maximum, amount), axis) * radial);
            }
        }

        private static void SetDisplay(Arm arm, bool visible)
        {
            if (arm.flexionArc != null) arm.flexionArc.enabled = visible;
            if (arm.deviationArc != null) arm.deviationArc.enabled = visible;
            if (arm.twistArc != null) arm.twistArc.enabled = visible;
        }

        private void SetWeightImmediate(float value)
        {
            weight = Mathf.Clamp01(value);
            if (left.constraint != null) left.constraint.weight = 0;
            if (right.constraint != null) right.constraint.weight = 0;
            if (activeArm != null) activeArm.constraint.weight = weight;
        }

        private static bool IsArmConfigured(Arm arm)
            => arm != null && arm.upperArm != null && arm.forearm != null && arm.hand != null &&
               arm.index != null && arm.middle != null && arm.pinky != null &&
               arm.target != null && arm.hint != null && arm.socket != null &&
               arm.constraint != null;

        private void OnDisable() => EndHold();

    }
}
