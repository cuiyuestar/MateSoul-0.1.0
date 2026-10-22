using System;
using System.Linq;
using UnityEngine;

namespace MateSoul.Runtime
{
    public enum ItemMotion { Reach, Carry, Cut }

    // One coordinator-owned animation layer, followed by bounded contact correction.
    [DefaultExecutionOrder(11100)]
    public sealed class AvatarItemRig : MonoBehaviour
    {
        private const float MinimumElbowAngle = 35f;
        private const float MaximumElbowAngle = 175f;
        private const float MaximumForearmRoll = 75f;
        private const float MaximumWristCorrection = 55f;
        [SerializeField] private Animator animator;
        [SerializeField] private Transform upper, elbow, hand, index, middle, pinky;
        [SerializeField] private Transform leftUpper, leftElbow, leftHand;
        [SerializeField] private Transform leftIndex, leftMiddle, leftPinky;
        [SerializeField] private Transform hips, spine, neck, head;
        [SerializeField] private Transform leftLeg, leftKnee, leftFoot;
        [SerializeField] private Transform rightLeg, rightKnee, rightFoot;
        private int layer = -1;
        private Guid owner;
        private Pose target;
        private bool hasTarget, orientHand;
        private Pose leftTarget;
        private bool hasLeftTarget;
        private float leftContactWeight;
        private float contactWeight;
        private Vector3 localGrip;
        private Quaternion localGripRotation;
        private Vector3 localLeftGrip;
        private Quaternion localLeftGripRotation;
        private PhysicsBody heldBody;
        private Pose itemFromGrip;
        private ItemInteractionProfile heldProfile;
        private bool twoHandAttachment;
        private Transform[][] rightFingerChains, leftFingerChains;
        private Quaternion[][] rightFingerBaseRotations;
        private Quaternion[][] leftFingerBaseRotations;
        private Quaternion[][] rightHeldFingerRotations;
        private bool heldFingerPoseCached;
        private Vector2 heldPinchContact;
        private float heldPinchIndexSide;
        private ItemMotion activeMotion;
        private float motionProgress;
        private float motionWeight;
        private ItemMotion debugMotion;
        private float debugProgress = -1;
        private int debugPoseReports;
        public bool IsConfigured => animator != null && upper && elbow && hand && index && middle && pinky &&
            hips && spine && neck && head &&
            leftLeg && leftKnee && leftFoot && rightLeg && rightKnee && rightFoot &&
            animator.GetLayerIndex("ItemInteraction") >= 0;
        public bool IsTwoHandConfigured => IsConfigured &&
            leftUpper && leftElbow && leftHand &&
            leftIndex && leftMiddle && leftPinky;
        public float ContactError { get; private set; }
        public float LeftContactError { get; private set; }
        public float ElbowAngle { get; private set; }
        public float ForearmRoll { get; private set; }
        public float WristCorrection { get; private set; }
        public float HeldAlignmentError { get; private set; }
        public float FingerPoseError { get; private set; }
        public float MinimumFingerClearance { get; private set; }
        public float MaximumFingerClearance { get; private set; }
        public float PalmClearance { get; private set; }
        public float MinimumPowerGripMcpFlexion { get; private set; }
        public float MaximumPowerGripMcpSpread { get; private set; }
        public float PostureLeanDegrees { get; private set; }
        public float PostureTorsoLeanDegrees { get; private set; }
        public float PostureCrouchDepth { get; private set; }
        public float PostureHeadPitch { get; private set; }
        public float LeftKneeAngle { get; private set; }
        public float RightKneeAngle { get; private set; }
        public float MaximumFootPlantError { get; private set; }
        public Pose HandGrip => new Pose(hand.TransformPoint(localGrip), hand.rotation * localGripRotation);
        public Pose LeftHandGrip => new Pose(
            leftHand.TransformPoint(localLeftGrip),
            leftHand.rotation * localLeftGripRotation);

        public void Configure(Animator value, Transform[] rightBones,
            Transform[] leftBones = null, Transform[] postureBones = null)
        {
            animator = value;
            if (rightBones == null || rightBones.Length != 6)
                throw new ArgumentException("Six right-arm grip bones are required.");
            upper = rightBones[0]; elbow = rightBones[1]; hand = rightBones[2];
            index = rightBones[3]; middle = rightBones[4]; pinky = rightBones[5];
            if (leftBones != null)
            {
                if (leftBones.Length != 6)
                    throw new ArgumentException("Six left-arm grip bones are required.");
                leftUpper = leftBones[0];
                leftElbow = leftBones[1];
                leftHand = leftBones[2];
                leftIndex = leftBones[3];
                leftMiddle = leftBones[4];
                leftPinky = leftBones[5];
            }
            if (postureBones != null)
            {
                if (postureBones.Length != 10)
                    throw new ArgumentException(
                        "Ten full-body posture bones are required.");
                hips = postureBones[0];
                spine = postureBones[1];
                neck = postureBones[2];
                head = postureBones[3];
                leftLeg = postureBones[4];
                leftKnee = postureBones[5];
                leftFoot = postureBones[6];
                rightLeg = postureBones[7];
                rightKnee = postureBones[8];
                rightFoot = postureBones[9];
            }
            rightFingerChains = BuildFingerChains(hand, "_R");
            leftFingerChains = leftHand != null
                ? BuildFingerChains(leftHand, "_L") : null;
        }

        public void Begin(Guid run)
        {
            if (owner != Guid.Empty || !IsConfigured)
                throw new InvalidOperationException("角色物品动作未配置或已被占用。");
            if (rightFingerChains == null)
                rightFingerChains = BuildFingerChains(hand, "_R");
            if (!FingerChainsComplete(rightFingerChains))
                throw new InvalidOperationException("角色右手缺少完整五指骨链。");
            if (leftHand != null && leftFingerChains == null)
                leftFingerChains = BuildFingerChains(leftHand, "_L");
            if (leftHand != null && !FingerChainsComplete(leftFingerChains))
                throw new InvalidOperationException("角色左手缺少完整五指骨链。");
            rightFingerBaseRotations = CaptureFingerRotations(
                rightFingerChains);
            leftFingerBaseRotations = leftFingerChains != null
                ? CaptureFingerRotations(leftFingerChains) : null;
            owner = run;
            rightHeldFingerRotations = null;
            heldFingerPoseCached = false;
            heldPinchContact = Vector2.zero;
            heldPinchIndexSide = 1;
            layer = animator.GetLayerIndex("ItemInteraction");
            localGrip = hand.InverseTransformPoint(Vector3.Lerp(hand.position, middle.position, .55f));
            var forward = hand.InverseTransformDirection(middle.position - hand.position);
            var across = hand.InverseTransformDirection(index.position - pinky.position);
            localGripRotation = Quaternion.LookRotation(forward, -Vector3.Cross(forward, across));
            if (leftHand != null)
            {
                localLeftGrip = leftHand.InverseTransformPoint(
                    Vector3.Lerp(leftHand.position, leftMiddle.position, .55f));
                var leftForward = leftHand.InverseTransformDirection(
                    leftMiddle.position - leftHand.position);
                var leftAcross = leftHand.InverseTransformDirection(
                    leftIndex.position - leftPinky.position);
                localLeftGripRotation = Quaternion.LookRotation(
                    leftForward, Vector3.Cross(leftForward, leftAcross));
            }
        }

        public void Motion(ItemMotion motion, float progress, float weight = 1)
        {
            if (owner == Guid.Empty) throw new InvalidOperationException("No item motion owner.");
            activeMotion = motion;
            motionProgress = Mathf.Clamp01(progress);
            motionWeight = Mathf.Clamp01(weight);
            animator.SetLayerWeight(layer, motionWeight);
            animator.Play("ItemInteraction." + motion, layer, motionProgress * .999f);
        }

        public void Reach(Pose grip, float weight = 1, bool matchRotation = false)
        {
            target = grip;
            contactWeight = Mathf.Clamp01(weight);
            orientHand = matchRotation;
            hasTarget = true;
        }

        public void ReachSecondary(Pose grip, float weight = 1)
        {
            if (leftHand == null)
                throw new InvalidOperationException(
                    "角色缺少双手抓握绑定。");
            leftTarget = grip;
            leftContactWeight = Mathf.Clamp01(weight);
            hasLeftTarget = true;
        }

        public void AttachHeld(Guid run, PhysicsBody body,
            ItemInteractionProfile profile)
        {
            if (owner != run || body == null || profile == null ||
                !body.IsInteractionControlled || heldBody != null)
                throw new InvalidOperationException("角色手部无法取得物品跟随权。");
            profile.ValidateGrip();
            heldProfile = profile;
            rightHeldFingerRotations = null;
            heldFingerPoseCached = false;
            heldPinchContact = Vector2.zero;
            heldPinchIndexSide = 1;
            twoHandAttachment = profile.RequiresTwoHands;
            if (twoHandAttachment)
            {
                if (leftHand == null || !hasLeftTarget)
                    throw new InvalidOperationException(
                        "大物件需要有效的双手抓握目标。");
                if (!body.TryMoveInteraction(run,
                        profile.ItemFromHands(HandGrip, LeftHandGrip)))
                    throw new InvalidOperationException(
                        "大物件无法对齐双手抓握点。");
            }
            else
            {
                var grip = HandGrip;
                var item = body.Pose;
                itemFromGrip = new Pose(
                    Quaternion.Inverse(grip.rotation) * (item.position - grip.position),
                    Quaternion.Inverse(grip.rotation) * item.rotation);
            }
            heldBody = body;
            SyncHeldBody();
        }

        public Pose GripForHeldPosition(Vector3 itemPosition)
        {
            if (heldBody == null || twoHandAttachment)
                throw new InvalidOperationException(
                    "角色当前没有可用的单手持物定位。");
            var rotation = HandGrip.rotation;
            return new Pose(
                itemPosition - rotation * itemFromGrip.position,
                rotation);
        }

        public void DetachHeld(Guid run)
        {
            if (owner != run) return;
            RestoreFingerPose(
                rightFingerChains, rightFingerBaseRotations);
            RestoreFingerPose(
                leftFingerChains, leftFingerBaseRotations);
            heldBody = null;
            heldProfile = null;
            rightHeldFingerRotations = null;
            heldFingerPoseCached = false;
            heldPinchContact = Vector2.zero;
            heldPinchIndexSide = 1;
            twoHandAttachment = false;
            itemFromGrip = default;
            HeldAlignmentError = 0;
            FingerPoseError = 0;
            MinimumFingerClearance = 0;
            MaximumFingerClearance = 0;
            PalmClearance = 0;
            MinimumPowerGripMcpFlexion = 0;
            MaximumPowerGripMcpSpread = 0;
        }

        public void ClearReach()
        {
            hasTarget = false;
            orientHand = false;
            hasLeftTarget = false;
            leftContactWeight = 0;
            ContactError = 0;
            LeftContactError = 0;
        }

        private void LateUpdate()
        {
            if (owner == Guid.Empty || !hasTarget) return;
            ApplyFullBodyPosture();
            var rotation = target.rotation * Quaternion.Inverse(localGripRotation);
            var wrist = target.position - rotation * localGrip;
            var upperLength = Vector3.Distance(upper.position, elbow.position);
            var forearmLength = Vector3.Distance(elbow.position, hand.position);
            var reach = upperLength + forearmLength;
            var delta = wrist - upper.position;
            var minimumReach = ReachForElbowAngle(
                upperLength, forearmLength, MinimumElbowAngle);
            var maximumReach = ReachForElbowAngle(
                upperLength, forearmLength, MaximumElbowAngle);
            var distance = Mathf.Clamp(delta.magnitude, minimumReach, maximumReach);
            var bounded = upper.position +
                          (delta.sqrMagnitude > .000001f
                              ? delta.normalized * distance
                              : transform.forward * minimumReach);
            var requested = Vector3.Lerp(hand.position, bounded, contactWeight);
            AvatarSeatingRig.Solve(upper, elbow, hand, requested,
                transform.right * .8f - transform.forward * .2f);
            ElbowAngle = Vector3.Angle(
                upper.position - elbow.position,
                hand.position - elbow.position);
            ForearmRoll = 0;
            WristCorrection = 0;
            if (orientHand && contactWeight > 0)
            {
                var forearmAxis = (hand.position - elbow.position).normalized;
                var currentUp = Vector3.ProjectOnPlane(
                    hand.rotation * Vector3.up, forearmAxis);
                var desiredUp = Vector3.ProjectOnPlane(
                    rotation * Vector3.up, forearmAxis);
                if (currentUp.sqrMagnitude > .0001f &&
                    desiredUp.sqrMagnitude > .0001f)
                {
                    ForearmRoll = Mathf.Clamp(
                        Vector3.SignedAngle(currentUp, desiredUp, forearmAxis),
                        -MaximumForearmRoll, MaximumForearmRoll) * contactWeight;
                    elbow.rotation = Quaternion.AngleAxis(
                        ForearmRoll, forearmAxis) * elbow.rotation;
                }
                var remaining = Quaternion.Angle(hand.rotation, rotation);
                WristCorrection = Mathf.Min(
                    remaining, MaximumWristCorrection * contactWeight);
                hand.rotation = Quaternion.RotateTowards(
                    hand.rotation, rotation, WristCorrection);
            }
            SolveSecondaryArm();
            ApplyFingerPose();
            ContactError = Vector3.Distance(HandGrip.position, target.position);
            LeftContactError = hasLeftTarget && leftHand != null
                ? Vector3.Distance(LeftHandGrip.position, leftTarget.position)
                : 0;
            SyncHeldBody();
        }

        private void ApplyFullBodyPosture()
        {
            PostureLeanDegrees = 0;
            PostureTorsoLeanDegrees = 0;
            PostureCrouchDepth = 0;
            PostureHeadPitch = 0;
            LeftKneeAngle = 0;
            RightKneeAngle = 0;
            MaximumFootPlantError = 0;
            if (activeMotion == ItemMotion.Carry || motionWeight <= 0 ||
                hips == null || spine == null || neck == null || head == null ||
                leftLeg == null || leftKnee == null || leftFoot == null ||
                rightLeg == null || rightKnee == null || rightFoot == null)
                return;

            var leftFootPosition = leftFoot.position;
            var rightFootPosition = rightFoot.position;
            var leftFootRotation = leftFoot.rotation;
            var rightFootRotation = rightFoot.rotation;
            var targetHeight = target.position.y - transform.position.y;
            var horizontalDistance = Vector3.ProjectOnPlane(
                target.position - transform.position, Vector3.up).magnitude;

            float lean;
            float crouch;
            if (activeMotion == ItemMotion.Cut)
            {
                lean = 14f;
                crouch = .035f;
            }
            else
            {
                var leanDemand = Mathf.Pow(
                    Mathf.InverseLerp(.98f, .35f, targetHeight), .45f);
                var extensionDemand = Mathf.InverseLerp(
                    .38f, .62f, horizontalDistance);
                var floorDemand = Mathf.InverseLerp(
                    .25f, 0, targetHeight);
                lean = Mathf.Min(57f,
                    Mathf.Min(41f,
                        leanDemand * 38f + extensionDemand * 3f) +
                    Mathf.Sqrt(floorDemand) * 21f);
                var crouchDemand = Mathf.InverseLerp(
                    .72f, .12f, targetHeight);
                var middleCrouch = Mathf.Sqrt(Mathf.InverseLerp(
                    .98f, .65f, targetHeight)) * .27f;
                crouch = Mathf.Max(
                    crouchDemand * .42f + floorDemand * .06f,
                    middleCrouch * .9f);
            }

            var blend = motionProgress * motionWeight;
            PostureLeanDegrees = lean * blend;
            PostureCrouchDepth = crouch * blend;
            hips.position += -transform.up * PostureCrouchDepth +
                             transform.forward * PostureCrouchDepth * .35f;
            spine.rotation = Quaternion.AngleAxis(
                PostureLeanDegrees, transform.right) * spine.rotation;

            var neckCompensation = PostureLeanDegrees * .45f;
            var headCompensation = PostureLeanDegrees * .37f;
            neck.rotation = Quaternion.AngleAxis(
                -neckCompensation, transform.right) * neck.rotation;
            head.rotation = Quaternion.AngleAxis(
                -headCompensation, transform.right) * head.rotation;
            PostureHeadPitch = PostureLeanDegrees -
                               neckCompensation - headCompensation;

            if (activeMotion == ItemMotion.Reach &&
                targetHeight >= .45f && targetHeight <= .98f)
            {
                var remaining = target.position - upper.position;
                var armReach = Vector3.Distance(
                                   upper.position, elbow.position) +
                               Vector3.Distance(
                                   elbow.position, hand.position);
                var assistDistance = Mathf.Clamp(
                    remaining.magnitude - armReach + .015f,
                    0, .18f) * blend;
                if (remaining.sqrMagnitude > .000001f &&
                    assistDistance > 0)
                {
                    var assist = remaining.normalized * assistDistance;
                    hips.position += assist;
                    PostureCrouchDepth += Mathf.Max(
                        0, Vector3.Dot(-assist, transform.up));
                }
            }

            AvatarSeatingRig.Solve(
                leftLeg, leftKnee, leftFoot,
                leftFootPosition, transform.forward);
            AvatarSeatingRig.Solve(
                rightLeg, rightKnee, rightFoot,
                rightFootPosition, transform.forward);
            leftFoot.rotation = leftFootRotation;
            rightFoot.rotation = rightFootRotation;
            LeftKneeAngle = Vector3.Angle(
                leftLeg.position - leftKnee.position,
                leftFoot.position - leftKnee.position);
            RightKneeAngle = Vector3.Angle(
                rightLeg.position - rightKnee.position,
                rightFoot.position - rightKnee.position);
            MaximumFootPlantError = Mathf.Max(
                Vector3.Distance(leftFoot.position, leftFootPosition),
                Vector3.Distance(rightFoot.position, rightFootPosition));
            PostureTorsoLeanDegrees = Vector3.Angle(
                head.position - hips.position, transform.up);
        }

        private void SolveSecondaryArm()
        {
            if (!hasLeftTarget || leftHand == null) return;
            var wrist = leftTarget.position - leftHand.rotation * localLeftGrip;
            var upperLength = Vector3.Distance(
                leftUpper.position, leftElbow.position);
            var forearmLength = Vector3.Distance(
                leftElbow.position, leftHand.position);
            var delta = wrist - leftUpper.position;
            var distance = Mathf.Clamp(delta.magnitude,
                ReachForElbowAngle(
                    upperLength, forearmLength, MinimumElbowAngle),
                ReachForElbowAngle(
                    upperLength, forearmLength, MaximumElbowAngle));
            var bounded = leftUpper.position +
                          (delta.sqrMagnitude > .000001f
                              ? delta.normalized * distance
                              : -transform.forward * distance);
            AvatarSeatingRig.Solve(leftUpper, leftElbow, leftHand,
                Vector3.Lerp(leftHand.position, bounded, leftContactWeight),
                -transform.right * .8f - transform.forward * .2f);
        }

        private void ApplyFingerPose()
        {
            if (heldProfile == null || heldBody == null) return;
            FingerPoseError = 0;
            MinimumFingerClearance = float.PositiveInfinity;
            MaximumFingerClearance = float.NegativeInfinity;
            if (!heldFingerPoseCached)
            {
                MinimumPowerGripMcpFlexion = float.PositiveInfinity;
                MaximumPowerGripMcpSpread = 0;
            }
            PalmClearance = PalmClearanceForGrip(
                HandGrip, hand.position, middle.position);
            PoseFingerChains(
                rightFingerChains, rightFingerBaseRotations,
                HandGrip, false);
            if (twoHandAttachment)
            {
                PalmClearance = Mathf.Min(PalmClearance,
                    PalmClearanceForGrip(
                        LeftHandGrip, leftHand.position,
                        leftMiddle.position));
                PoseFingerChains(
                    leftFingerChains, leftFingerBaseRotations,
                    LeftHandGrip, true);
            }
            if (float.IsPositiveInfinity(MinimumFingerClearance))
                MinimumFingerClearance = 0;
            if (float.IsNegativeInfinity(MaximumFingerClearance))
                MaximumFingerClearance = 0;
        }

        private void PoseFingerChains(
            Transform[][] chains, Quaternion[][] baseRotations,
            Pose grip, bool left)
        {
            if (chains == null || chains.Length != 5 ||
                baseRotations == null) return;
            RestoreFingerPose(chains, baseRotations);
            var stablePose =
                heldProfile.gripKind == ItemGripKind.Handle ||
                heldProfile.handPose == CharacterHandPose.Pinch;
            var useCachedHeldPose =
                stablePose &&
                !left && heldFingerPoseCached;
            if (useCachedHeldPose)
                RestoreFingerPose(chains, rightHeldFingerRotations);
            var pinchIndexSide = 1f;
            var pinchContact = Vector2.zero;
            if (heldProfile.handPose == CharacterHandPose.Pinch)
            {
                if (useCachedHeldPose)
                {
                    pinchIndexSide = heldPinchIndexSide;
                    pinchContact = heldPinchContact;
                }
                else
                {
                    var normal = grip.rotation * Vector3.up;
                    pinchIndexSide = Mathf.Sign(Vector3.Dot(
                        FingerTip(chains[1]) - grip.position, normal));
                    if (Mathf.Abs(pinchIndexSide) < .5f)
                        pinchIndexSide = 1;
                    pinchContact = PinchContactLocal(grip, chains);
                    heldPinchIndexSide = pinchIndexSide;
                    heldPinchContact = pinchContact;
                }
            }
            for (var finger = 0; finger < chains.Length; finger++)
            {
                if (heldProfile.handPose == CharacterHandPose.Pinch &&
                    finger > 1) continue;
                var targetPoint = heldProfile.handPose ==
                                  CharacterHandPose.Pinch
                    ? PinchTarget(grip, pinchContact,
                        finger == 1 ? pinchIndexSide : -pinchIndexSide)
                    : FingerTarget(grip, finger, left);
                if (!useCachedHeldPose &&
                    heldProfile.handPose == CharacterHandPose.Pinch)
                    PoseFingerAnatomically(
                        chains[finger], targetPoint,
                        grip.rotation * Vector3.up,
                        heldProfile.fingerCurl * .82f, finger == 0,
                        finger == 0 ? 42f : 18f);
                else if (!useCachedHeldPose &&
                         heldProfile.gripKind == ItemGripKind.Handle)
                {
                    if (finger == 0)
                    {
                        var curl = FingerCurlForTarget(
                            chains[finger], targetPoint, true);
                        PoseFingerAnatomically(
                            chains[finger], targetPoint,
                            grip.rotation * Vector3.up, curl,
                            true, 58f);
                    }
                    else
                    {
                        var powerPose = PosePowerGripFinger(
                            chains[finger],
                            baseRotations[finger],
                            targetPoint,
                            heldProfile.gripSize.x,
                            heldProfile.fingerCurl,
                            finger, left);
                        MinimumPowerGripMcpFlexion = Mathf.Min(
                            MinimumPowerGripMcpFlexion,
                            powerPose.x);
                        MaximumPowerGripMcpSpread = Mathf.Max(
                            MaximumPowerGripMcpSpread,
                            Mathf.Max(
                                Mathf.Abs(powerPose.y),
                                Mathf.Abs(powerPose.z)));
                    }
                }
                else if (!useCachedHeldPose)
                {
                    var curl = FingerCurlForTarget(
                        chains[finger], targetPoint, finger == 0);
                    PoseFingerAnatomically(
                        chains[finger], targetPoint,
                        grip.rotation * Vector3.up, curl,
                        finger == 0);
                }
                var tip = FingerTip(chains[finger]);
                FingerPoseError = Mathf.Max(
                    FingerPoseError, Vector3.Distance(tip, targetPoint));
                var clearance = FingerClearance(grip, tip);
                MinimumFingerClearance = Mathf.Min(
                    MinimumFingerClearance, clearance);
                MaximumFingerClearance = Mathf.Max(
                    MaximumFingerClearance, clearance);
            }
            if (stablePose && !left && !heldFingerPoseCached)
            {
                rightHeldFingerRotations =
                    CaptureFingerRotations(chains);
                heldFingerPoseCached = true;
            }
            if (float.IsPositiveInfinity(MinimumPowerGripMcpFlexion))
                MinimumPowerGripMcpFlexion = 0;
        }

        private Vector2 PinchContactLocal(
            Pose grip, Transform[][] chains)
        {
            var center = heldProfile.WorldGripShapeCenter(grip);
            var inverse = Quaternion.Inverse(grip.rotation);
            var thumb = inverse * (FingerTip(chains[0]) - center);
            var index = inverse * (FingerTip(chains[1]) - center);
            var clearance = heldProfile.fingerClearance;
            return new Vector2(
                Mathf.Clamp((thumb.x + index.x) * .5f,
                    -heldProfile.gripSize.x * .5f - clearance,
                    heldProfile.gripSize.x * .5f + clearance),
                Mathf.Clamp((thumb.z + index.z) * .5f,
                    -heldProfile.gripSize.z * .5f - clearance,
                    heldProfile.gripSize.z * .5f + clearance));
        }

        private Vector3 PinchTarget(
            Pose grip, Vector2 contact, float side)
        {
            var right = grip.rotation * Vector3.right;
            var normal = grip.rotation * Vector3.up;
            var forward = grip.rotation * Vector3.forward;
            var center = heldProfile.WorldGripShapeCenter(grip);
            var surface = heldProfile.gripSize.y * .5f +
                          heldProfile.fingerClearance;
            return center + right * contact.x +
                   forward * contact.y + normal * side * surface;
        }

        private Vector3 FingerTarget(Pose grip, int finger, bool left)
        {
            var center = heldProfile.WorldGripShapeCenter(grip);
            var local = Quaternion.Inverse(grip.rotation) *
                        (FingerTip((left
                            ? leftFingerChains : rightFingerChains)[finger]) -
                         center);
            if (heldProfile.gripKind == ItemGripKind.Handle)
            {
                var radius = heldProfile.GripRadius +
                             heldProfile.fingerClearance;
                var chain = (left
                    ? leftFingerChains : rightFingerChains)[finger];
                var baseLocal = Quaternion.Inverse(grip.rotation) *
                                (chain[0].position - center);
                var contact = HandleContactLocal(
                    baseLocal, radius,
                    heldProfile.gripSize.z * .5f,
                    finger == 0);
                return center + grip.rotation * contact;
            }
            if (heldProfile.gripKind == ItemGripKind.LoopHandle)
            {
                var radius = heldProfile.GripRadius +
                             heldProfile.fingerClearance;
                var radial = new Vector2(local.x, local.y);
                if (radial.sqrMagnitude < .000001f)
                    radial = finger == 0
                        ? new Vector2(left ? radius : -radius, 0)
                        : new Vector2(0, -radius);
                radial = radial.normalized * radius;
                local.x = radial.x;
                local.y = radial.y;
                local.z = Mathf.Clamp(local.z,
                    -heldProfile.gripSize.z * .5f,
                    heldProfile.gripSize.z * .5f);
                return center + grip.rotation * local;
            }

            var half = heldProfile.gripSize * .5f +
                       Vector3.one * heldProfile.fingerClearance;
            var normalizedRadius = Mathf.Sqrt(
                local.x * local.x / (half.x * half.x) +
                local.y * local.y / (half.y * half.y) +
                local.z * local.z / (half.z * half.z));
            if (normalizedRadius < .0001f)
                local = new Vector3(0, finger == 0
                    ? half.y : -half.y, 0);
            else
                local /= normalizedRadius;
            return center + grip.rotation * local;
        }

        internal static Vector3 HandleContactLocal(
            Vector3 fingerBase, float radius,
            float halfLength, bool thumb)
        {
            radius = Mathf.Max(.003f, radius);
            halfLength = Mathf.Max(0, halfLength);
            var axial = Mathf.Clamp(
                thumb
                    ? Mathf.Max(fingerBase.z, halfLength * .55f)
                    : fingerBase.z,
                -halfLength, halfLength);
            if (thumb)
            {
                var side = Mathf.Abs(fingerBase.x) > .001f
                    ? Mathf.Sign(fingerBase.x) : 1f;
                var opposition = new Vector2(
                    -side * .78f, .62f).normalized * radius;
                return new Vector3(
                    opposition.x, opposition.y, axial);
            }

            var lateral = Mathf.Clamp(
                fingerBase.x / .05f, -.58f, .58f);
            var underside = -Mathf.Sqrt(
                Mathf.Max(0, 1 - lateral * lateral));
            return new Vector3(
                lateral * radius, underside * radius, axial);
        }

        internal static Vector3 PowerGripFlexion(
            float diameter, float curl, int finger)
        {
            var closure = Mathf.InverseLerp(
                .055f, .012f, Mathf.Max(.001f, diameter));
            var fingerBias = Mathf.Clamp(finger - 1, 0, 3);
            curl = Mathf.Clamp01(curl);
            return new Vector3(
                (54f + closure * 18f + fingerBias * 2.5f) * curl,
                (76f + closure * 24f + fingerBias * 2f) * curl,
                (42f + closure * 18f + fingerBias * 1.5f) * curl);
        }

        private static Vector3 PosePowerGripFinger(
            Transform[] chain, Quaternion[] baseRotations,
            Vector3 targetPoint, float diameter, float curl,
            int finger, bool left)
        {
            if (chain == null || chain.Length != 3 ||
                baseRotations == null || baseRotations.Length != 3)
                return Vector3.zero;
            var preferred = PowerGripFlexion(
                diameter, curl, finger);
            var sign = left ? -1f : 1f;
            var best = preferred;
            var bestSpread = Vector2.zero;
            var bestScore = float.PositiveInfinity;
            for (var mcp = preferred.x - 8f;
                 mcp <= preferred.x + 8f; mcp += 4f)
            for (var pip = preferred.y - 10f;
                 pip <= preferred.y + 10f; pip += 5f)
            for (var dip = preferred.z - 8f;
                 dip <= preferred.z + 8f; dip += 4f)
            {
                var candidate = new Vector3(mcp, pip, dip);
                ApplyPowerGripPose(
                    chain, baseRotations, candidate,
                    Vector2.zero, sign);
                var error = Vector3.SqrMagnitude(
                    FingerTip(chain) - targetPoint);
                var bias = (candidate - preferred).sqrMagnitude * 2e-8f;
                var score = error + bias;
                if (score >= bestScore) continue;
                bestScore = score;
                best = candidate;
            }
            for (var spreadX = -10f; spreadX <= 10f; spreadX += 5f)
            for (var spreadY = -10f; spreadY <= 10f; spreadY += 5f)
            {
                var spread = new Vector2(spreadX, spreadY);
                ApplyPowerGripPose(
                    chain, baseRotations, best, spread, sign);
                var error = Vector3.SqrMagnitude(
                    FingerTip(chain) - targetPoint);
                var score = error + spread.sqrMagnitude * 5e-8f;
                if (score >= bestScore) continue;
                bestScore = score;
                bestSpread = spread;
            }
            for (var step = 2f; step >= .5f; step *= .5f)
            {
                for (var axis = 0; axis < 2; axis++)
                {
                    for (var direction = -1; direction <= 1;
                         direction += 2)
                    {
                        var candidate = bestSpread;
                        candidate[axis] = Mathf.Clamp(
                            candidate[axis] + step * direction,
                            -10f, 10f);
                        ApplyPowerGripPose(
                            chain, baseRotations, best,
                            candidate, sign);
                        var error = Vector3.SqrMagnitude(
                            FingerTip(chain) - targetPoint);
                        var score = error +
                                    candidate.sqrMagnitude * 1e-9f;
                        if (score >= bestScore) continue;
                        bestScore = score;
                        bestSpread = candidate;
                    }
                }
                for (var joint = 0; joint < 3; joint++)
                {
                    for (var direction = -1; direction <= 1;
                         direction += 2)
                    {
                        var candidate = best;
                        var range = joint == 1 ? 10f : 8f;
                        candidate[joint] = Mathf.Clamp(
                            candidate[joint] + step * direction,
                            preferred[joint] - range,
                            preferred[joint] + range);
                        ApplyPowerGripPose(
                            chain, baseRotations, candidate,
                            bestSpread, sign);
                        var error = Vector3.SqrMagnitude(
                            FingerTip(chain) - targetPoint);
                        var bias =
                            (candidate - preferred).sqrMagnitude * 2e-8f;
                        var score = error + bias;
                        if (score >= bestScore) continue;
                        bestScore = score;
                        best = candidate;
                    }
                }
            }
            ApplyPowerGripPose(
                chain, baseRotations, best, bestSpread, sign);
            return new Vector3(
                best.x, bestSpread.x, bestSpread.y);
        }

        private static void ApplyPowerGripPose(
            Transform[] chain, Quaternion[] baseRotations,
            Vector3 flexion, Vector2 spread, float sign)
        {
            chain[0].localRotation = baseRotations[0] *
                                     Quaternion.Euler(
                                         spread.x, spread.y,
                                         flexion.x * sign);
            chain[1].localRotation = baseRotations[1] *
                                     Quaternion.AngleAxis(
                                         flexion.y * sign,
                                         Vector3.forward);
            chain[2].localRotation = baseRotations[2] *
                                     Quaternion.AngleAxis(
                                         flexion.z * sign,
                                         Vector3.forward);
        }

        private float FingerClearance(Pose grip, Vector3 tip)
        {
            var center = heldProfile.WorldGripShapeCenter(grip);
            var offset = Quaternion.Inverse(grip.rotation) *
                         (tip - center);
            if (heldProfile.gripKind == ItemGripKind.Handle ||
                heldProfile.gripKind == ItemGripKind.LoopHandle)
            {
                return new Vector2(offset.x, offset.y).magnitude -
                       heldProfile.GripRadius;
            }
            if (heldProfile.gripKind == ItemGripKind.ThinEdge)
            {
                return Mathf.Abs(offset.y) -
                       heldProfile.gripSize.y * .5f;
            }
            var half = heldProfile.gripSize * .5f;
            var normalized = new Vector3(
                offset.x / half.x,
                offset.y / half.y,
                offset.z / half.z).magnitude;
            return (normalized - 1) *
                   Mathf.Min(half.x, Mathf.Min(half.y, half.z));
        }

        private float FingerCurlForTarget(
            Transform[] chain, Vector3 targetPoint, bool thumb)
        {
            var lastSegment = Vector3.Distance(
                chain[1].position, chain[2].position);
            var fingerLength =
                Vector3.Distance(chain[0].position, chain[1].position) +
                lastSegment * 1.65f;
            var targetDistance = Vector3.Distance(
                chain[0].position, targetPoint);
            var diameter = heldProfile.handPose ==
                           CharacterHandPose.Pinch
                ? heldProfile.gripSize.y
                : Mathf.Max(
                    heldProfile.gripSize.x,
                    heldProfile.gripSize.y);
            var sizeClosure = Mathf.InverseLerp(
                .11f, .015f, diameter);
            var distanceClosure = Mathf.InverseLerp(
                fingerLength * .95f,
                fingerLength * .4f,
                targetDistance);
            if (heldProfile.handPose == CharacterHandPose.Pinch)
                return Mathf.Clamp(
                    heldProfile.fingerCurl *
                    (1.45f + distanceClosure * .3f),
                    .7f, 1f);
            var curl = heldProfile.fingerCurl *
                       (.4f + sizeClosure * .45f +
                        distanceClosure * .3f);
            return Mathf.Clamp(curl * (thumb ? .88f : 1f),
                .12f, 1f);
        }

        private static void PoseFingerAnatomically(
            Transform[] chain, Vector3 targetPoint,
            Vector3 palmNormal, float curl, bool thumb,
            float abductionLimit = -1)
        {
            if (chain == null || chain.Length != 3) return;
            ApplyAbduction(
                chain[0], chain, palmNormal, targetPoint,
                (abductionLimit >= 0
                    ? abductionLimit
                    : thumb ? 22f : 8f) * curl);
            var applied0 = 0f;
            var applied1 = 0f;
            var applied2 = 0f;
            var direction0 = 0f;
            var direction1 = 0f;
            var direction2 = 0f;
            var maximum0 = (thumb ? 48f : 58f) * curl;
            var maximum1 = (thumb ? 62f : 92f) * curl;
            var maximum2 = (thumb ? 42f : 58f) * curl;
            for (var iteration = 0; iteration < 3; iteration++)
            {
                ApplyFlexionStep(
                    chain, 2, palmNormal, targetPoint,
                    maximum2, ref applied2, ref direction2);
                ApplyFlexionStep(
                    chain, 1, palmNormal, targetPoint,
                    maximum1, ref applied1, ref direction1);
                ApplyFlexionStep(
                    chain, 0, palmNormal, targetPoint,
                    maximum0, ref applied0, ref direction0);
            }
        }

        private static void ApplyAbduction(
            Transform joint, Transform[] chain, Vector3 palmNormal,
            Vector3 targetPoint, float maximum)
        {
            if (maximum <= .001f ||
                palmNormal.sqrMagnitude < .000001f)
                return;
            var from = FingerTip(chain) - joint.position;
            var to = targetPoint - joint.position;
            var angle = Mathf.Clamp(Vector3.SignedAngle(
                from, to, palmNormal), -maximum, maximum);
            joint.rotation = Quaternion.AngleAxis(
                angle, palmNormal.normalized) * joint.rotation;
        }

        private static void ApplyFlexionStep(
            Transform[] chain, int jointIndex,
            Vector3 palmNormal, Vector3 targetPoint,
            float maximum, ref float applied, ref float direction)
        {
            if (applied >= maximum - .001f) return;
            var joint = chain[jointIndex];
            var segment = jointIndex < 2
                ? chain[jointIndex + 1].position - joint.position
                : chain[2].position - chain[1].position;
            var axis = Vector3.Cross(segment, palmNormal);
            if (axis.sqrMagnitude < .000001f)
                axis = joint.right;
            axis.Normalize();
            var from = FingerTip(chain) - joint.position;
            var to = targetPoint - joint.position;
            var delta = Vector3.SignedAngle(from, to, axis);
            if (direction == 0)
                direction = Mathf.Sign(delta);
            var towardFlexion = delta * direction;
            if (towardFlexion <= .01f) return;
            var step = Mathf.Min(
                towardFlexion, maximum - applied,
                maximum * .5f);
            joint.rotation = Quaternion.AngleAxis(
                step * direction, axis) * joint.rotation;
            applied += step;
        }

        private float PalmClearanceForGrip(
            Pose grip, Vector3 wrist, Vector3 knuckle)
        {
            var center = heldProfile.WorldGripShapeCenter(grip);
            var segment = knuckle - wrist;
            var lengthSquared = segment.sqrMagnitude;
            var amount = lengthSquared > .000001f
                ? Mathf.Clamp01(Vector3.Dot(
                    center - wrist, segment) / lengthSquared)
                : 0;
            var closest = wrist + segment * amount;
            var support = heldProfile.gripKind ==
                          ItemGripKind.Handle ||
                          heldProfile.gripKind ==
                          ItemGripKind.LoopHandle
                ? heldProfile.GripRadius
                : Mathf.Min(
                    heldProfile.gripSize.x,
                    Mathf.Min(heldProfile.gripSize.y,
                        heldProfile.gripSize.z)) * .5f;
            return Vector3.Distance(center, closest) -
                   support - .018f;
        }

        private static float FingerJointLimit(CharacterHandPose pose)
        {
            switch (pose)
            {
                case CharacterHandPose.Power: return 72f;
                case CharacterHandPose.Hook: return 78f;
                case CharacterHandPose.Pinch: return 65f;
                case CharacterHandPose.TwoHandSupport: return 58f;
                default: return 62f;
            }
        }

        private static Vector3 FingerTip(Transform[] chain)
        {
            if (chain == null || chain.Length != 3) return Vector3.zero;
            var direction = chain[2].position - chain[1].position;
            return chain[2].position + direction * .65f;
        }

        private static Quaternion[][] CaptureFingerRotations(
            Transform[][] chains)
        {
            if (chains == null) return null;
            var result = new Quaternion[chains.Length][];
            for (var finger = 0; finger < chains.Length; finger++)
            {
                result[finger] = new Quaternion[chains[finger].Length];
                for (var joint = 0;
                     joint < chains[finger].Length; joint++)
                    result[finger][joint] =
                        chains[finger][joint].localRotation;
            }
            return result;
        }

        private static void RestoreFingerPose(
            Transform[][] chains, Quaternion[][] rotations)
        {
            if (chains == null || rotations == null) return;
            for (var finger = 0; finger < chains.Length; finger++)
            for (var joint = 0;
                 joint < chains[finger].Length; joint++)
                chains[finger][joint].localRotation =
                    rotations[finger][joint];
        }

        private static Transform[][] BuildFingerChains(
            Transform handRoot, string suffix)
        {
            if (handRoot == null) return null;
            var all = handRoot.GetComponentsInChildren<Transform>(true);
            var fingers = new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" };
            return fingers.Select(finger => Enumerable.Range(1, 3)
                .Select(segment => all.SingleOrDefault(value =>
                    value.name == $"{finger}_0{segment}{suffix}"))
                .ToArray()).ToArray();
        }

        private static bool FingerChainsComplete(Transform[][] chains)
            => chains != null && chains.Length == 5 &&
               chains.All(chain => chain != null && chain.Length == 3 &&
                   chain.All(joint => joint != null));

        private static float ReachForElbowAngle(
            float upperLength, float forearmLength, float angle)
            => Mathf.Sqrt(Mathf.Max(0,
                upperLength * upperLength + forearmLength * forearmLength -
                2 * upperLength * forearmLength * Mathf.Cos(angle * Mathf.Deg2Rad)));

        private void SyncHeldBody()
        {
            if (heldBody == null) return;
            var actualBefore = heldBody.Pose;
            Pose item;
            if (twoHandAttachment)
            {
                item = heldProfile.ItemFromHands(HandGrip, LeftHandGrip);
            }
            else
            {
                var grip = HandGrip;
                item = new Pose(
                    grip.position + grip.rotation * itemFromGrip.position,
                    grip.rotation * itemFromGrip.rotation);
            }
            if (!heldBody.TryMoveInteraction(owner, item))
                throw new InvalidOperationException("物品手部跟随控制权已失效。");
            HeldAlignmentError = Vector3.Distance(heldBody.Pose.position, item.position) +
                                 Quaternion.Angle(heldBody.Pose.rotation, item.rotation) *
                                 Mathf.Deg2Rad;
        }

        public void End(Guid run)
        {
            if (owner != run) return;
            if (layer >= 0 && animator != null) animator.SetLayerWeight(layer, 0);
            DetachHeld(run);
            owner = Guid.Empty;
            ClearReach();
            PostureLeanDegrees = 0;
            PostureTorsoLeanDegrees = 0;
            PostureCrouchDepth = 0;
            PostureHeadPitch = 0;
            LeftKneeAngle = 0;
            RightKneeAngle = 0;
            MaximumFootPlantError = 0;
        }

        private void OnDisable()
        {
            if (owner != Guid.Empty) End(owner);
        }
    }
}
