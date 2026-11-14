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
        [SerializeField] private PickupAnimationSet pickupAnimations;
        [SerializeField] private ItemUseAnimation eatAnimation;
        private PickupPerformance authoredPickup;
        private ItemUseAnimation authoredUse;
        private bool pickupPlacement;
        private Vector3 pickupContactCorrection;
        private float pickupLiftCorrectionWeight;
        [SerializeField] private float pickupLeftAnkleHeight = .1f;
        [SerializeField] private float pickupRightAnkleHeight = .1f;
        public PickupAnimationSet PickupAnimations => pickupAnimations;
        public ItemUseAnimation EatAnimation => eatAnimation;
        // The head bone sits at the skull base; offset forward and slightly down in
        // character space so spatial audio is emitted from the mouth.
        private static readonly Vector3 MouthOffset = new Vector3(0f, -.035f, .075f);
        public Vector3 MouthPosition => head != null
            ? head.position + transform.rotation * MouthOffset
            : transform.position;
        public Transform Head => head;
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
        private enum HandGesturePhase { None, Closing, Holding, Opening, Releasing }
        private HandGesturePhase handGesturePhase;
        private Quaternion[][][] handGestureFrames;
        private Quaternion[][] handGestureBlendFrom;
        private float handGestureDuration = 1;
        private float handGestureLead = .05f;
        private float handGesturePlayhead;
        private float handGestureBlend;
        private ItemMotion activeMotion;
        private float motionProgress;
        private float motionWeight;
        private ItemMotion debugMotion;
        private float debugProgress = -1;
        private int debugPoseReports;
        private float nextUseReport;
        private Vector3 useRootStart;
        private float useRootMinY, useRootMaxY;
        private float useElbowLMin = -1f, useElbowLMax = -1f;
        private float useElbowRMin = -1f, useElbowRMax = -1f;
        private float useSpreadMin = -1f, useSpreadMax = -1f;
        private float useFirstSeconds, useLastSeconds;
        private int useSamples;
        private bool naturalHold;
        private AvatarIdleVariation idleVariation;
#region debug-point A-B-D:wrist-kinematics
#if UNITY_EDITOR
        private float debugNextWristReport;
        private float debugNextFingerGeometryReport;
        private float debugNextPostureReport;
        private float debugNextLowReachReport;
        private float debugNextHandleGripReport;
#endif
#endregion
#region debug-point A-E:item-action-jitter-state
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private float debugNextActionJitterReport;
        private float debugNextActionStateReport;
#endif
#endregion
#region debug-point A-B-C-D-E:held-item-hand-gap
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private float debugNextHandGapReport;
#endif
#endregion
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

        public void ConfigureEat(ItemUseAnimation value) => eatAnimation = value;

        public void ConfigurePickups(PickupAnimationSet value)
        {
            pickupAnimations = value;
            pickupLeftAnkleHeight = transform.InverseTransformPoint(leftFoot.position).y;
            pickupRightAnkleHeight = transform.InverseTransformPoint(rightFoot.position).y;
        }

        public void PickupMotion(PickupPerformance animation, float seconds)
        {
            if (owner == Guid.Empty) throw new InvalidOperationException("No item motion owner.");
            pickupPlacement = false;
            authoredPickup = animation;
            pickupLiftCorrectionWeight = 1 - Mathf.SmoothStep(0, 1,
                (seconds - animation.contactTime) / .3f);
            var pickupLayer = animator.GetLayerIndex(PickupAnimationSet.LayerName);
            animator.SetLayerWeight(layer, 0);
            animator.SetLayerWeight(pickupLayer, Mathf.SmoothStep(0, 1, seconds / .18f));
            animator.SetFloat(PickupAnimationSet.HeightParameter, animation.contactGrip.y);
            animator.Play(PickupAnimationSet.LayerName + "." + PickupAnimationSet.StateName,
                pickupLayer, Mathf.Clamp01(seconds / animation.Duration) * .999f);
        }

        public void PickupPlaceMotion(PickupPerformance animation, float seconds,
            Pose grip, float contactBlend, float entryBlend)
        {
            Motion(ItemMotion.Carry, 1);
            PickupMotion(animation, seconds);
            pickupPlacement = true;
            animator.SetLayerWeight(layer, 1);
            animator.SetLayerWeight(animator.GetLayerIndex(PickupAnimationSet.LayerName),
                Mathf.SmoothStep(0, 1, entryBlend));
            Reach(grip, contactBlend);
        }

        public void BlendPickupToCarry(float progress)
        {
            Motion(ItemMotion.Carry, 1);
            animator.SetLayerWeight(animator.GetLayerIndex(PickupAnimationSet.LayerName),
                1 - Mathf.SmoothStep(0, 1, progress));
        }

        // Fades the authored pickup and carry pose out while the item stays in
        // the hand, leaving the arms to the locomotion animation so she can
        // idle and walk with her hands hanging naturally.
        public void HoldNaturally(float blend)
        {
            if (owner == Guid.Empty) throw new InvalidOperationException("No item motion owner.");
            var value = Mathf.SmoothStep(0, 1, Mathf.Clamp01(blend));
            var pickupLayer = animator.GetLayerIndex(PickupAnimationSet.LayerName);
            if (pickupLayer >= 0) animator.SetLayerWeight(pickupLayer, 1 - value);
            // Only fade the carry layer down; never raise it back over the base
            // locomotion while the arms hand over to it.
            animator.SetLayerWeight(layer, Mathf.Min(animator.GetLayerWeight(layer), 1 - value));
            authoredPickup = null;
            pickupPlacement = false;
            contactWeight = 1 - value;
            naturalHold = true;
            if (value < 1) return;
            ClearReach();
            // Only idle03 and walking may play until the item interaction ends.
            if (idleVariation != null) idleVariation.SetHoldLocked(true);
        }

        public void ClearPickup()
        {
            authoredPickup = null;
            pickupPlacement = false;
            pickupContactCorrection = Vector3.zero;
            var pickupLayer = animator != null
                ? animator.GetLayerIndex(PickupAnimationSet.LayerName) : -1;
            if (pickupLayer >= 0) animator.SetLayerWeight(pickupLayer, 0);
        }

        public bool CanPlayUse(ItemUseAnimation animation)
        {
            var useLayer = animator != null ? animator.GetLayerIndex(ItemUseAnimation.LayerName) : -1;
            var playable = animation != null && animation.clip != null && useLayer >= 0 &&
                animator.HasState(useLayer, Animator.StringToHash(animation.StateName));
            // A rejected gesture used to fail silently, which made "the animation never
            // played" indistinguishable from "the controller has no such state".
            if (!playable)
                Debug.LogWarning($"MATESOUL_ITEM_USE_UNAVAILABLE " +
                    $"clip={(animation != null && animation.clip != null ? animation.clip.name : "none")} " +
                    $"useLayer={useLayer} state=" +
                    $"{(animation != null && animation.clip != null ? Animator.StringToHash(animation.StateName).ToString() : "none")}");
            return playable;
        }

        // Bend of a two-bone chain, in degrees. -1 means the bones are not bound.
        private static float ElbowFlex(Transform shoulder, Transform elbow, Transform wrist)
            => shoulder && elbow && wrist
                ? Vector3.Angle(shoulder.position - elbow.position,
                    wrist.position - elbow.position)
                : -1f;

        private static void Track(ref float min, ref float max, float value)
        {
            if (value < 0f) return;
            if (min < 0f || value < min) min = value;
            if (max < 0f || value > max) max = value;
        }

        public void UseMotion(ItemUseAnimation animation, float seconds, float weight)
        {
            if (owner == Guid.Empty || heldBody == null)
                throw new InvalidOperationException("物品使用动作需要有效的持物控制权。");
            Motion(ItemMotion.Carry, 1);
            if (authoredUse != animation)
            {
                authoredUse = animation;
                useRootStart = transform.position;
                useRootMinY = useRootMaxY = transform.position.y;
                useElbowLMin = useElbowLMax = -1f;
                useElbowRMin = useElbowRMax = -1f;
                useSpreadMin = useSpreadMax = -1f;
                useFirstSeconds = seconds;
                useLastSeconds = seconds;
                useSamples = 0;
                nextUseReport = 0f;
            }
            var useLayer = animator.GetLayerIndex(ItemUseAnimation.LayerName);
            animator.SetLayerWeight(useLayer, weight);
            animator.Play(animation.StateName, useLayer,
                Mathf.Clamp01(seconds / animation.clip.length) * .999f);
            // The gesture used to log nothing, so "did it play" and "is the pose right"
            // had no evidence at all. Report the state clock, every competing layer, the
            // arm bend (a hug must fold both elbows), how far apart the hands are, and
            // how the root drifts while the clip runs.
            useLastSeconds = seconds;
            useSamples++;
            var elbowL = ElbowFlex(leftUpper, leftElbow, leftHand);
            var elbowR = ElbowFlex(upper, elbow, hand);
            var spread = leftHand && hand ? Vector3.Distance(leftHand.position, hand.position) : -1f;
            Track(ref useElbowLMin, ref useElbowLMax, elbowL);
            Track(ref useElbowRMin, ref useElbowRMax, elbowR);
            Track(ref useSpreadMin, ref useSpreadMax, spread);
            useRootMinY = Mathf.Min(useRootMinY, transform.position.y);
            useRootMaxY = Mathf.Max(useRootMaxY, transform.position.y);
            if (Time.unscaledTime >= nextUseReport)
            {
                nextUseReport = Time.unscaledTime + .25f;
                var pickupLayer = animator.GetLayerIndex(PickupAnimationSet.LayerName);
                var info = animator.GetCurrentAnimatorStateInfo(useLayer);
                // The state hash alone cannot prove which clip the layer is actually
                // evaluating, so read the clip the animator reports for that layer.
                var clips = animator.GetCurrentAnimatorClipInfo(useLayer);
                var playing = clips.Length > 0 && clips[0].clip != null ? clips[0].clip.name : "none";
                var playingWeight = clips.Length > 0 ? clips[0].weight : 0f;
                var seatRig = GetComponent<AvatarSeatingRig>();
                var lyingRig = GetComponent<AvatarLyingRig>();
                // Naming the surface under her separates "the floor moved" from
                // "she was placed on furniture".
                var groundName = "none";
                var groundY = float.NaN;
                if (Physics.Raycast(transform.position + Vector3.up * .5f, Vector3.down,
                        out var ground, 3f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
                {
                    groundY = ground.point.y;
                    groundName = ground.collider.name;
                }
                // Velocity and the body flags say whether physics is pushing her or an
                // owner is driving her body directly.
                var body = GetComponent<Rigidbody>();
                var physique = GetComponent<PhysicsBody>();
                Debug.Log($"MATESOUL_ITEM_USE clip={animation.clip.name} " +
                    $"len={animation.clip.length:F2} fps={animation.clip.frameRate:F0} " +
                    $"t={seconds:F2} w={weight:F2} " +
                    $"state={info.shortNameHash} stateTime={info.normalizedTime:F2} " +
                    $"playing={playing} playingWeight={playingWeight:F2} " +
                    $"useLayer={animator.GetLayerWeight(useLayer):F2} " +
                    $"interaction={animator.GetLayerWeight(layer):F2} " +
                    $"pickup={(pickupLayer >= 0 ? animator.GetLayerWeight(pickupLayer) : 0f):F2} " +
                    $"seat={(seatRig ? seatRig.Weight : 0f):F2} " +
                    $"lie={(lyingRig ? lyingRig.Weight : 0f):F2} " +
                    $"elbowL={elbowL:F0} elbowR={elbowR:F0} spread={spread:F2} " +
                    $"root={transform.position} " +
                    $"hipsLocal={(hips ? hips.localPosition : Vector3.zero)} " +
                    $"hipsWorld={(hips ? hips.position : Vector3.zero)} " +
                    $"ground={groundY:F2} on={groundName} " +
                    $"velY={(body ? body.velocity.y : 0f):F2} " +
                    $"kinematic={(body ? body.isKinematic : false)} " +
                    $"following={(physique ? physique.IsFollowing : false)} " +
                    $"contact={(physique ? physique.IsContact : false)} " +
                    $"docked={(physique ? physique.IsDocked : false)} " +
                    $"paused={(physique ? physique.IsPaused : false)}");
            }
        }

        public void ClearUse()
        {
            if (authoredUse != null && useSamples > 0)
                Debug.Log($"MATESOUL_ITEM_USE_SUMMARY clip={authoredUse.clip.name} " +
                    $"samples={useSamples} t={useFirstSeconds:F2}..{useLastSeconds:F2} " +
                    $"rootY={useRootMinY:F2}..{useRootMaxY:F2} startY={useRootStart.y:F2} " +
                    $"elbowL={useElbowLMin:F0}..{useElbowLMax:F0} " +
                    $"elbowR={useElbowRMin:F0}..{useElbowRMax:F0} " +
                    $"spread={useSpreadMin:F2}..{useSpreadMax:F2}");
            authoredUse = null;
            useSamples = 0;
            var useLayer = animator != null ? animator.GetLayerIndex(ItemUseAnimation.LayerName) : -1;
            if (useLayer >= 0) animator.SetLayerWeight(useLayer, 0);
        }

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
            rightFingerChains = BuildFingerChains(hand, "_r");
            leftFingerChains = leftHand != null
                ? BuildFingerChains(leftHand, "_l") : null;
        }

        public void Begin(Guid run)
        {
            if (owner != Guid.Empty || !IsConfigured)
                throw new InvalidOperationException("角色物品动作未配置或已被占用。");
            if (rightFingerChains == null)
                rightFingerChains = BuildFingerChains(hand, "_r");
            if (!FingerChainsComplete(rightFingerChains))
                throw new InvalidOperationException("角色右手缺少完整五指骨链。");
            if (leftHand != null && leftFingerChains == null)
                leftFingerChains = BuildFingerChains(leftHand, "_l");
            if (leftHand != null && !FingerChainsComplete(leftFingerChains))
                throw new InvalidOperationException("角色左手缺少完整五指骨链。");
            rightFingerBaseRotations = CaptureFingerRotations(
                rightFingerChains);
            leftFingerBaseRotations = leftFingerChains != null
                ? CaptureFingerRotations(leftFingerChains) : null;
            owner = run;
            if (idleVariation == null) idleVariation = GetComponent<AvatarIdleVariation>();
            naturalHold = false;
            if (idleVariation != null) idleVariation.SetHoldLocked(false);
            ClearHandGesture();
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
#region debug-point B:rig-binding
#if UNITY_EDITOR
            var bindPayload = "{\"sessionId\":\"item-reach-layer\",\"runId\":\"post-fix\",\"hypothesisId\":\"B\",\"location\":\"AvatarItemRig.Begin\",\"msg\":\"[DEBUG] rig binding\",\"data\":{\"animator\":\"" + animator.name + "\",\"root\":\"" + transform.name + "\",\"scale\":" + JsonUtility.ToJson(animator.transform.lossyScale) + ",\"layer\":" + layer + "}}";
            var bindRequest = UnityEngine.Networking.UnityWebRequest.Post("http://127.0.0.1:7793/event", bindPayload, "application/json");
            bindRequest.SendWebRequest().completed += _ => bindRequest.Dispose();
#endif
#endregion
        }

        public void Motion(ItemMotion motion, float progress, float weight = 1)
        {
            if (owner == Guid.Empty) throw new InvalidOperationException("No item motion owner.");
            authoredPickup = null;
            var pickupLayer = animator.GetLayerIndex(PickupAnimationSet.LayerName);
            if (pickupLayer >= 0) animator.SetLayerWeight(pickupLayer, 0);
            activeMotion = motion;
            motionProgress = Mathf.Clamp01(progress);
            motionWeight = Mathf.Clamp01(weight);
            animator.SetLayerWeight(layer, motionWeight);
            animator.Play("ItemInteraction." + motion, layer, motionProgress * .999f);
#region debug-point A:requested-motion
#if UNITY_EDITOR
            if (motion != debugMotion || progress >= .99f && debugProgress < .99f)
            {
                debugMotion = motion;
                debugProgress = progress;
                var state = animator.GetCurrentAnimatorStateInfo(layer);
                var payload = FormattableString.Invariant($"{{\"sessionId\":\"item-reach-layer\",\"runId\":\"post-fix\",\"hypothesisId\":\"A\",\"location\":\"AvatarItemRig.Motion\",\"msg\":\"[DEBUG] requested motion\",\"data\":{{\"motion\":\"{motion}\",\"requestedTime\":{progress},\"weight\":{animator.GetLayerWeight(layer)},\"state\":{state.shortNameHash},\"expectedState\":{Animator.StringToHash(motion.ToString())},\"sampleTime\":{state.normalizedTime},\"animatorEnabled\":{animator.enabled.ToString().ToLowerInvariant()},\"culling\":\"{animator.cullingMode}\"}}}}");
                var request = UnityEngine.Networking.UnityWebRequest.Post("http://127.0.0.1:7793/event", payload, "application/json");
                request.SendWebRequest().completed += _ => request.Dispose();
            }
#endif
#endregion
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
            // Cookware keeps its colliders while held so food can be dropped into it,
            // but that also made the carried item collide with her own torso and push
            // her across the room. The holder and the held item must never collide.
            IgnoreHeldCollisions(true);
            SyncHeldBody();
            BeginHandGesture();
#region debug-point A-C:attach-transform
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var debugAttachedGrip = profile.WorldGrip(body.Pose);
            var debugRoundTrip = profile.ItemFromGrip(debugAttachedGrip);
            var debugAttachPayload =
                $"{{\"sessionId\":\"held-item-hand-gap\",\"runId\":\"post-fix\"," +
                $"\"hypothesisId\":\"A-C\",\"location\":\"AvatarItemRig.AttachHeld\"," +
                $"\"msg\":\"[DEBUG] held item attached\",\"data\":{{" +
                $"\"item\":\"{body.name}\",\"profile\":\"{profile.name}\"," +
                $"\"handGrip\":{JsonUtility.ToJson(HandGrip.position)}," +
                $"\"itemPosition\":{JsonUtility.ToJson(body.Pose.position)}," +
                $"\"profileGrip\":{JsonUtility.ToJson(debugAttachedGrip.position)}," +
                $"\"gripToHand\":{Vector3.Distance(debugAttachedGrip.position, HandGrip.position)}," +
                $"\"gripAngleToHand\":{Quaternion.Angle(debugAttachedGrip.rotation, HandGrip.rotation)}," +
                $"\"roundTripPositionError\":{Vector3.Distance(debugRoundTrip.position, body.Pose.position)}," +
                $"\"roundTripRotationError\":{Quaternion.Angle(debugRoundTrip.rotation, body.Pose.rotation)}," +
                $"\"itemFromGripPosition\":{JsonUtility.ToJson(itemFromGrip.position)}," +
                $"\"itemFromGripRotation\":{JsonUtility.ToJson(itemFromGrip.rotation)}}}}}";
            var debugAttachRequest = UnityEngine.Networking.UnityWebRequest.Post(
                Application.isEditor
                    ? "http://127.0.0.1:7782/event"
                    : "http://127.0.0.1:7782/event",
                debugAttachPayload, "application/json");
            debugAttachRequest.SendWebRequest().completed += _ => debugAttachRequest.Dispose();
#endif
#endregion
#region debug-point A-D-E:handle-attachment
#if UNITY_EDITOR
            if (profile.gripKind == ItemGripKind.Handle)
            {
                var debugGrip = profile.WorldGrip(body.Pose);
                var debugPayload =
                    $"{{\"sessionId\":\"knife-handle-grip\",\"runId\":\"post-fix\"," +
                    $"\"hypothesisId\":\"A-D-E\",\"location\":\"AvatarItemRig.AttachHeld\"," +
                    $"\"msg\":\"[DEBUG] handle attachment geometry\",\"data\":{{" +
                    $"\"item\":\"{body.name}\",\"profile\":\"{profile.name}\"," +
                    $"\"gripPosition\":{JsonUtility.ToJson(debugGrip.position)}," +
                    $"\"gripForward\":{JsonUtility.ToJson(debugGrip.rotation * Vector3.forward)}," +
                    $"\"gripUp\":{JsonUtility.ToJson(debugGrip.rotation * Vector3.up)}," +
                    $"\"gripSize\":{JsonUtility.ToJson(profile.gripSize)}," +
                    $"\"shapeOffset\":{JsonUtility.ToJson(profile.gripShapeOffset)}," +
                    $"\"shapeCenter\":{JsonUtility.ToJson(profile.WorldGripShapeCenter(debugGrip))}," +
                    $"\"handGripPosition\":{JsonUtility.ToJson(HandGrip.position)}," +
                    $"\"handGripForward\":{JsonUtility.ToJson(HandGrip.rotation * Vector3.forward)}}}}}";
                var debugRequest = UnityEngine.Networking.UnityWebRequest.Post(
                    "http://127.0.0.1:7779/event", debugPayload, "application/json");
                debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
            }
#endif
#endregion
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
            if (!BeginHandGestureRelease())
                RestoreFingerPose(
                    rightFingerChains, rightFingerBaseRotations);
            RestoreFingerPose(
                leftFingerChains, leftFingerBaseRotations);
            IgnoreHeldCollisions(false);
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

        // Physics.IgnoreCollision is a per-pair flag, so it is raised on attach and
        // cleared on detach; the carried item keeps colliding with the rest of the world.
        private void IgnoreHeldCollisions(bool ignored)
        {
            if (heldBody == null) return;
            var itemColliders = heldBody.GetComponentsInChildren<Collider>(true);
            var ownColliders = GetComponentsInChildren<Collider>(true);
            foreach (var item in itemColliders)
                foreach (var own in ownColliders)
                    Physics.IgnoreCollision(item, own, ignored);
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
            if (owner != Guid.Empty && authoredUse != null)
            {
                ApplyFingerPose();
                ApplyHandGesture(Time.deltaTime);
                SyncHeldBody();
                return;
            }
            if (owner != Guid.Empty && pickupAnimations &&
                animator.GetLayerWeight(animator.GetLayerIndex(PickupAnimationSet.LayerName)) > 0)
            {
                GroundPickupFoot(leftLeg, leftKnee, leftFoot, pickupLeftAnkleHeight);
                GroundPickupFoot(rightLeg, rightKnee, rightFoot, pickupRightAnkleHeight);
            }
            if (owner != Guid.Empty && authoredPickup != null)
            {
                ApplyAuthoredPickupContact();
                ApplyFingerPose();
                ApplyHandGesture(Time.deltaTime);
                SyncHeldBody();
                return;
            }
            if (owner != Guid.Empty && naturalHold)
            {
                // No arm override: the held item simply follows the hand while
                // the locomotion animation keeps the arms naturally down.
                ApplyFingerPose();
                ApplyHandGesture(Time.deltaTime);
                SyncHeldBody();
                return;
            }
            if (owner == Guid.Empty || !hasTarget)
            {
                // The release gesture keeps playing after detach, when the
                // interaction layer no longer drives the hand target.
                ApplyHandGesture(Time.deltaTime);
                return;
            }
#region debug-point E:action-idle-state
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Time.unscaledTime >= debugNextActionStateReport)
            {
                debugNextActionStateReport = Time.unscaledTime + .2f;
                var debugBaseState = animator.GetCurrentAnimatorStateInfo(0);
                var debugLayerState = layer >= 0
                    ? animator.GetCurrentAnimatorStateInfo(layer) : default;
                var debugStatePayload =
                    $"{{\"sessionId\":\"item-action-jitter\",\"runId\":\"post-fix\"," +
                    $"\"hypothesisId\":\"E\",\"location\":\"AvatarItemRig.LateUpdate.State\"," +
                    $"\"msg\":\"[DEBUG] action animator state\",\"data\":{{" +
                    $"\"motion\":\"{activeMotion}\",\"motionProgress\":{motionProgress}," +
                    $"\"motionWeight\":{motionWeight},\"baseState\":{debugBaseState.shortNameHash}," +
                    $"\"baseTime\":{debugBaseState.normalizedTime}," +
                    $"\"interactionState\":{debugLayerState.shortNameHash}," +
                    $"\"interactionTime\":{debugLayerState.normalizedTime}," +
                    $"\"target\":{JsonUtility.ToJson(target.position)}," +
                    $"\"held\":\"{(heldBody == null ? "none" : heldBody.name)}\"}}}}";
                var debugStateRequest = UnityEngine.Networking.UnityWebRequest.Post(
                    Application.isEditor
                        ? "http://127.0.0.1:7781/event"
                        : "http://127.0.0.1:7781/event",
                    debugStatePayload, "application/json");
                debugStateRequest.SendWebRequest().completed += _ => debugStateRequest.Dispose();
            }
#endif
#endregion
#region debug-point B-D:item-rig-pre-write
#if UNITY_EDITOR
            var debugAttentionHeadBeforePosture = head.rotation;
            var debugAttentionNeckBeforePosture = neck.rotation;
#endif
#endregion
            ApplyFullBodyPosture();
#region debug-point B-D:item-rig-post-write
#if UNITY_EDITOR
            var debugAttentionPayload =
                $"{{\"sessionId\":\"item-attention-head-snap\",\"runId\":\"post-fix\"," +
                $"\"hypothesisId\":\"B-D\",\"location\":\"AvatarItemRig.LateUpdate\"," +
                $"\"msg\":\"[DEBUG] item posture head write\",\"data\":{{" +
                $"\"frame\":{Time.frameCount},\"motion\":\"{activeMotion}\"," +
                $"\"motionProgress\":{motionProgress},\"motionWeight\":{motionWeight}," +
                $"\"target\":{JsonUtility.ToJson(target.position)}," +
                $"\"postureLean\":{PostureLeanDegrees}," +
                $"\"postureHeadPitch\":{PostureHeadPitch}," +
                $"\"headDelta\":{Quaternion.Angle(debugAttentionHeadBeforePosture, head.rotation)}," +
                $"\"neckDelta\":{Quaternion.Angle(debugAttentionNeckBeforePosture, neck.rotation)}," +
                $"\"headRotation\":{JsonUtility.ToJson(head.rotation)}}}}}";
            var debugAttentionRequest = UnityEngine.Networking.UnityWebRequest.Post(
                "http://127.0.0.1:7779/event", debugAttentionPayload, "application/json");
            debugAttentionRequest.SendWebRequest().completed += _ => debugAttentionRequest.Dispose();
#endif
#endregion
            var rotation = target.rotation * Quaternion.Inverse(localGripRotation);
            var wrist = target.position - rotation * localGrip;
            var upperLength = Vector3.Distance(upper.position, elbow.position);
            var forearmLength = Vector3.Distance(elbow.position, hand.position);
            var reach = upperLength + forearmLength;
            var delta = wrist - upper.position;
#region debug-point B-C-D:evaluated-pose
#if UNITY_EDITOR
            if (debugPoseReports < 3)
            {
                debugPoseReports++;
                Transform hips = upper;
                while (hips != null && hips.name != "lengyue_hips") hips = hips.parent;
                var payload = FormattableString.Invariant($"{{\"sessionId\":\"item-reach-layer\",\"runId\":\"post-fix\",\"hypothesisId\":\"B-C-D\",\"location\":\"AvatarItemRig.LateUpdate\",\"msg\":\"[DEBUG] evaluated pose\",\"data\":{{\"hips\":{JsonUtility.ToJson(hips.position)},\"shoulder\":{JsonUtility.ToJson(upper.position)},\"root\":{JsonUtility.ToJson(transform.position)},\"wrist\":{JsonUtility.ToJson(wrist)},\"handBefore\":{JsonUtility.ToJson(hand.position)},\"reach\":{reach},\"demand\":{delta.magnitude},\"sampleTime\":{animator.GetCurrentAnimatorStateInfo(layer).normalizedTime}}}}}");
                var request = UnityEngine.Networking.UnityWebRequest.Post("http://127.0.0.1:7793/event", payload, "application/json");
                request.SendWebRequest().completed += _ => request.Dispose();
            }
#endif
#endregion
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
#region debug-point A-B-D:wrist-kinematics
#if UNITY_EDITOR
            var debugHandPositionBefore = hand.position;
            var debugHandRotationBefore = hand.rotation;
            var debugHandLocalBefore = hand.localRotation;
            var debugElbowAngleBefore = Vector3.Angle(
                upper.position - elbow.position,
                hand.position - elbow.position);
#endif
#endregion
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
            ApplyHandGesture(Time.deltaTime);
#region debug-point A-E:item-action-jitter-state
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (heldBody != null && heldProfile != null &&
                (heldProfile.gripKind == ItemGripKind.Handle ||
                 heldProfile.gripKind == ItemGripKind.ThinEdge) &&
                Time.unscaledTime >= debugNextActionJitterReport)
            {
                debugNextActionJitterReport = Time.unscaledTime + .1f;
                var debugGrip = HandGrip;
                var debugCenter = heldProfile.WorldGripShapeCenter(debugGrip);
                var debugInverse = Quaternion.Inverse(debugGrip.rotation);
                var debugFingers = "";
                var debugCount = heldProfile.handPose == CharacterHandPose.Pinch
                    ? 2 : rightFingerChains.Length;
                var debugPinchIndexSide = 1f;
                var debugPinchContact = Vector2.zero;
                if (heldProfile.handPose == CharacterHandPose.Pinch)
                {
                    debugPinchIndexSide = Mathf.Sign(Vector3.Dot(
                        FingerTip(rightFingerChains[1]) - debugGrip.position,
                        debugGrip.rotation * Vector3.up));
                    if (Mathf.Abs(debugPinchIndexSide) < .5f)
                        debugPinchIndexSide = 1f;
                    debugPinchContact = PinchContactLocal(
                        debugGrip, rightFingerChains);
                }
                for (var debugFinger = 0; debugFinger < debugCount; debugFinger++)
                {
                    var debugChain = rightFingerChains[debugFinger];
                    var debugSide = debugFinger == 1
                        ? debugPinchIndexSide : -debugPinchIndexSide;
                    var debugTarget = heldProfile.handPose == CharacterHandPose.Pinch
                        ? PinchTarget(
                            debugGrip, debugPinchContact, debugSide)
                        : FingerTarget(debugGrip, debugFinger, false);
                    debugFingers += (debugFinger == 0 ? "" : ",") +
                        $"{{\"finger\":{debugFinger}," +
                        $"\"targetLocal\":{JsonUtility.ToJson(debugInverse * (debugTarget - debugCenter))}," +
                        $"\"tipLocal\":{JsonUtility.ToJson(debugInverse * (FingerTip(debugChain) - debugCenter))}," +
                        $"\"j0\":{JsonUtility.ToJson(debugChain[0].localEulerAngles)}," +
                        $"\"j1\":{JsonUtility.ToJson(debugChain[1].localEulerAngles)}," +
                        $"\"j2\":{JsonUtility.ToJson(debugChain[2].localEulerAngles)}}}";
                }
                var debugBaseState = animator.GetCurrentAnimatorStateInfo(0);
                var debugItemPose = heldBody.Pose;
                var debugPayload =
                    $"{{\"sessionId\":\"item-action-jitter\",\"runId\":\"post-fix\"," +
                    $"\"hypothesisId\":\"A-E\",\"location\":\"AvatarItemRig.LateUpdate\"," +
                    $"\"msg\":\"[DEBUG] held finger and animator sample\",\"data\":{{" +
                    $"\"item\":\"{heldBody.name}\",\"kind\":\"{heldProfile.gripKind}\"," +
                    $"\"handPose\":\"{heldProfile.handPose}\",\"motion\":\"{activeMotion}\"," +
                    $"\"motionWeight\":{motionWeight},\"itemPosition\":{JsonUtility.ToJson(debugItemPose.position)}," +
                    $"\"itemRotation\":{JsonUtility.ToJson(debugItemPose.rotation)}," +
                    $"\"handPosition\":{JsonUtility.ToJson(debugGrip.position)}," +
                    $"\"baseState\":{debugBaseState.shortNameHash}," +
                    $"\"baseNormalizedTime\":{debugBaseState.normalizedTime}," +
                    $"\"fingerPoseError\":{FingerPoseError},\"fingers\":[{debugFingers}]}}}}";
                var debugRequest = UnityEngine.Networking.UnityWebRequest.Post(
                    Application.isEditor
                        ? "http://127.0.0.1:7781/event"
                        : "http://127.0.0.1:7781/event",
                    debugPayload, "application/json");
                debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
            }
#endif
#endregion
#region debug-point A-B-C-E:handle-finger-wrap
#if UNITY_EDITOR
            if (heldBody != null && heldProfile != null &&
                heldProfile.gripKind == ItemGripKind.Handle &&
                Time.unscaledTime >= debugNextHandleGripReport)
            {
                debugNextHandleGripReport = Time.unscaledTime + .25f;
                var debugGrip = HandGrip;
                var debugCenter = heldProfile.WorldGripShapeCenter(debugGrip);
                var debugInverse = Quaternion.Inverse(debugGrip.rotation);
                var debugPalmAxis = (middle.position - hand.position).normalized;
                var debugPalmNormal = debugGrip.rotation * Vector3.up;
                var debugHandleAxis = debugGrip.rotation * Vector3.forward;
                var debugFingerData = "";
                for (var debugFinger = 0;
                     debugFinger < rightFingerChains.Length;
                     debugFinger++)
                {
                    var debugTarget = FingerTarget(
                        debugGrip, debugFinger, false);
                    var debugTargetLocal =
                        debugInverse * (debugTarget - debugCenter);
                    var debugTipLocal = debugInverse *
                        (FingerTip(rightFingerChains[debugFinger]) -
                         debugCenter);
                    var debugBase = rightFingerBaseRotations[debugFinger];
                    var debugMcpDelta = Quaternion.Inverse(debugBase[0]) *
                                        rightFingerChains[debugFinger][0]
                                            .localRotation;
                    var debugCurl = FingerCurlForTarget(
                        rightFingerChains[debugFinger],
                        debugTarget, debugFinger == 0);
                    debugFingerData +=
                        (debugFinger == 0 ? "" : ",") +
                        $"{{\"finger\":{debugFinger}," +
                        $"\"targetLocal\":{JsonUtility.ToJson(debugTargetLocal)}," +
                        $"\"targetAngle\":{Mathf.Atan2(debugTargetLocal.y, debugTargetLocal.x) * Mathf.Rad2Deg}," +
                        $"\"tipLocal\":{JsonUtility.ToJson(debugTipLocal)}," +
                        $"\"mcpDelta\":{JsonUtility.ToJson(debugMcpDelta.eulerAngles)}," +
                        $"\"curl\":{debugCurl}," +
                        $"\"clearance\":{FingerClearance(debugGrip, FingerTip(rightFingerChains[debugFinger]))}}}";
                }
                var debugPayload =
                    $"{{\"sessionId\":\"knife-handle-grip\",\"runId\":\"post-fix\"," +
                    $"\"hypothesisId\":\"A-B-C-E\",\"location\":\"AvatarItemRig.LateUpdate\"," +
                    $"\"msg\":\"[DEBUG] handle finger wrap\",\"data\":{{" +
                    $"\"item\":\"{heldBody.name}\",\"profile\":\"{heldProfile.name}\"," +
                    $"\"handleAxis\":{JsonUtility.ToJson(debugHandleAxis)}," +
                    $"\"palmAxis\":{JsonUtility.ToJson(debugPalmAxis)}," +
                    $"\"palmNormal\":{JsonUtility.ToJson(debugPalmNormal)}," +
                    $"\"axisAlignment\":{Vector3.Dot(debugHandleAxis, debugPalmAxis)}," +
                    $"\"palmClearance\":{PalmClearance}," +
                    $"\"minimumFingerClearance\":{MinimumFingerClearance}," +
                    $"\"maximumFingerClearance\":{MaximumFingerClearance}," +
                    $"\"fingerPoseError\":{FingerPoseError}," +
                    $"\"fingers\":[{debugFingerData}]}}}}";
                var debugRequest = UnityEngine.Networking.UnityWebRequest.Post(
                    "http://127.0.0.1:7779/event", debugPayload, "application/json");
                debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
            }
#endif
#endregion
            ContactError = Vector3.Distance(HandGrip.position, target.position);
            LeftContactError = hasLeftTarget && leftHand != null
                ? Vector3.Distance(LeftHandGrip.position, leftTarget.position)
                : 0;
            SyncHeldBody();
#region debug-point B-C-D:full-body-posture
#if UNITY_EDITOR
            if (Time.unscaledTime >= debugNextPostureReport)
            {
                debugNextPostureReport = Time.unscaledTime + .25f;
                var debugBones = animator.GetComponentsInChildren<Transform>(true);
                var debugHips = debugBones.Single(value => value.name == "lengyue_hips");
                var debugSpine = debugBones.Single(value => value.name == "lengyue_spine");
                var debugHead = debugBones.Single(value => value.name == "lengyue_head_01");
                var debugLeftLeg = debugBones.Single(value => value.name == "lengyue_thigh_l");
                var debugLeftKnee = debugBones.Single(value => value.name == "lengyue_shin_l");
                var debugLeftFoot = debugBones.Single(value => value.name == "lengyue_foot_l");
                var debugRightLeg = debugBones.Single(value => value.name == "lengyue_thigh_r");
                var debugRightKnee = debugBones.Single(value => value.name == "lengyue_shin_r");
                var debugRightFoot = debugBones.Single(value => value.name == "lengyue_foot_r");
                var debugLeftKneeAngle = Vector3.Angle(
                    debugLeftLeg.position - debugLeftKnee.position,
                    debugLeftFoot.position - debugLeftKnee.position);
                var debugRightKneeAngle = Vector3.Angle(
                    debugRightLeg.position - debugRightKnee.position,
                    debugRightFoot.position - debugRightKnee.position);
                var debugPayload =
                    $"{{\"sessionId\":\"item-posture-cut\",\"runId\":\"posture-post-fix\"," +
                    $"\"hypothesisId\":\"B-C-D\",\"location\":\"AvatarItemRig.LateUpdate\"," +
                    $"\"msg\":\"[DEBUG] full body reach posture\",\"data\":{{" +
                    $"\"motion\":\"{debugMotion}\",\"progress\":{debugProgress}," +
                    $"\"targetHeight\":{target.position.y - transform.position.y}," +
                    $"\"shoulderDemand\":{Vector3.Distance(upper.position, target.position)}," +
                    $"\"hips\":{JsonUtility.ToJson(debugHips.position)}," +
                    $"\"hipsLocal\":{JsonUtility.ToJson(debugHips.localPosition)}," +
                    $"\"spineLocalEuler\":{JsonUtility.ToJson(debugSpine.localEulerAngles)}," +
                    $"\"head\":{JsonUtility.ToJson(debugHead.position)}," +
                    $"\"headLocalEuler\":{JsonUtility.ToJson(debugHead.localEulerAngles)}," +
                    $"\"leanDegrees\":{PostureLeanDegrees}," +
                    $"\"crouchDepth\":{PostureCrouchDepth}," +
                    $"\"headPitch\":{PostureHeadPitch}," +
                    $"\"footPlantError\":{MaximumFootPlantError}," +
                    $"\"leftKneeAngle\":{debugLeftKneeAngle}," +
                    $"\"rightKneeAngle\":{debugRightKneeAngle}," +
                    $"\"leftFoot\":{JsonUtility.ToJson(debugLeftFoot.position)}," +
                    $"\"rightFoot\":{JsonUtility.ToJson(debugRightFoot.position)}," +
                    $"\"shoulder\":{JsonUtility.ToJson(upper.position)}," +
                    $"\"elbow\":{JsonUtility.ToJson(elbow.position)}," +
                    $"\"hand\":{JsonUtility.ToJson(hand.position)}," +
                    $"\"contactError\":{ContactError}}}}}";
                var debugRequest = UnityEngine.Networking.UnityWebRequest.Post(
                    "http://127.0.0.1:7787/event", debugPayload, "application/json");
                debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
            }
#endif
#endregion
#region debug-point G-H-I-J-K:finger-geometry
#if UNITY_EDITOR
            if (heldBody != null && heldProfile != null &&
                Time.unscaledTime >= debugNextFingerGeometryReport)
            {
                debugNextFingerGeometryReport = Time.unscaledTime + .25f;
                var debugItemPose = heldBody.Pose;
                var debugGrip = heldProfile.WorldGrip(debugItemPose);
                var debugShapeCenter =
                    heldProfile.WorldGripShapeCenter(debugGrip);
                var debugPalm = middle.position - hand.position;
                var debugPalmLength = debugPalm.magnitude;
                var debugPalmAxis = debugPalmLength > .0001f
                    ? debugPalm / debugPalmLength : hand.forward;
                var debugCenterOffset =
                    debugShapeCenter - hand.position;
                var debugCenterAlongPalm = Vector3.Dot(
                    debugCenterOffset, debugPalmAxis);
                var debugCenterFromPalmAxis = Vector3.ProjectOnPlane(
                    debugCenterOffset, debugPalmAxis).magnitude;
                var debugVolumeRadius = Mathf.Min(
                    heldProfile.gripSize.x,
                    Mathf.Min(heldProfile.gripSize.y,
                        heldProfile.gripSize.z)) * .5f;
                var debugPalmOverlap = Mathf.Max(
                    0, -PalmClearance);
                var debugFingerData = "";
                for (var debugFinger = 0;
                     debugFinger < rightFingerChains.Length;
                     debugFinger++)
                {
                    var debugChain = rightFingerChains[debugFinger];
                    var debugTip = FingerTip(debugChain);
                    debugFingerData +=
                        (debugFinger == 0 ? "" : ",") +
                        $"{{\"tip\":{JsonUtility.ToJson(debugTip)}," +
                        $"\"clearance\":{FingerClearance(HandGrip, debugTip)}," +
                        $"\"j0\":{JsonUtility.ToJson(debugChain[0].localEulerAngles)}," +
                        $"\"j1\":{JsonUtility.ToJson(debugChain[1].localEulerAngles)}," +
                        $"\"j2\":{JsonUtility.ToJson(debugChain[2].localEulerAngles)}}}";
                }
                var debugPayload =
                    $"{{\"sessionId\":\"wrist-grip-follow\",\"runId\":\"finger-post-fix\"," +
                    $"\"hypothesisId\":\"G-H-I-J-K\",\"location\":\"AvatarItemRig.LateUpdate\"," +
                    $"\"msg\":\"[DEBUG] grip volume and finger joints\",\"data\":{{" +
                    $"\"item\":\"{heldBody.name}\",\"profile\":\"{heldProfile.name}\"," +
                    $"\"kind\":\"{heldProfile.gripKind}\",\"handPose\":\"{heldProfile.handPose}\"," +
                    $"\"gripSize\":{JsonUtility.ToJson(heldProfile.gripSize)}," +
                    $"\"fingerClearance\":{heldProfile.fingerClearance}," +
                    $"\"fingerCurl\":{heldProfile.fingerCurl}," +
                    $"\"effectiveJointLimit\":{FingerJointLimit(heldProfile.handPose)}," +
                    $"\"solver\":\"constrained-analytic\",\"profileCurlApplied\":true," +
                    $"\"maximumJointOperationsPerHand\":50," +
                    $"\"itemPosition\":{JsonUtility.ToJson(debugItemPose.position)}," +
                    $"\"gripPosition\":{JsonUtility.ToJson(debugGrip.position)}," +
                    $"\"shapeCenter\":{JsonUtility.ToJson(debugShapeCenter)}," +
                    $"\"shapeOffset\":{JsonUtility.ToJson(heldProfile.gripShapeOffset)}," +
                    $"\"handRoot\":{JsonUtility.ToJson(hand.position)}," +
                    $"\"middleBase\":{JsonUtility.ToJson(middle.position)}," +
                    $"\"palmLength\":{debugPalmLength}," +
                    $"\"centerAlongPalm\":{debugCenterAlongPalm}," +
                    $"\"centerFromPalmAxis\":{debugCenterFromPalmAxis}," +
                    $"\"gripVolumeRadius\":{debugVolumeRadius}," +
                    $"\"estimatedPalmOverlap\":{debugPalmOverlap}," +
                    $"\"palmClearance\":{PalmClearance}," +
                    $"\"minimumFingerClearance\":{MinimumFingerClearance}," +
                    $"\"fingerPoseError\":{FingerPoseError}," +
                    $"\"fingers\":[{debugFingerData}]}}}}";
                var debugRequest = UnityEngine.Networking.UnityWebRequest.Post(
                    "http://127.0.0.1:7786/event", debugPayload, "application/json");
                debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
            }
#endif
#endregion
#region debug-point A-B-D:wrist-kinematics
#if UNITY_EDITOR
            if (Time.unscaledTime >= debugNextWristReport)
            {
                debugNextWristReport = Time.unscaledTime + .1f;
                var debugElbowAngleAfter = Vector3.Angle(
                    upper.position - elbow.position,
                    hand.position - elbow.position);
                var debugPayload =
                    $"{{\"sessionId\":\"wrist-grip-follow\",\"runId\":\"post-fix\"," +
                    $"\"hypothesisId\":\"A-B-D\",\"location\":\"AvatarItemRig.LateUpdate\"," +
                    $"\"msg\":\"[DEBUG] wrist IK kinematics\",\"data\":{{" +
                    $"\"motion\":\"{debugMotion}\",\"progress\":{debugProgress}," +
                    $"\"weight\":{contactWeight},\"targetPosition\":{JsonUtility.ToJson(target.position)}," +
                    $"\"targetRotation\":{JsonUtility.ToJson(target.rotation)}," +
                    $"\"handPositionBefore\":{JsonUtility.ToJson(debugHandPositionBefore)}," +
                    $"\"handPositionAfter\":{JsonUtility.ToJson(hand.position)}," +
                    $"\"handLocalBefore\":{JsonUtility.ToJson(debugHandLocalBefore.eulerAngles)}," +
                    $"\"handLocalAfter\":{JsonUtility.ToJson(hand.localEulerAngles)}," +
                    $"\"wristRotationApplied\":{Quaternion.Angle(debugHandRotationBefore, hand.rotation)}," +
                    $"\"forearmRoll\":{ForearmRoll},\"wristCorrection\":{WristCorrection}," +
                    $"\"targetRotationError\":{Quaternion.Angle(HandGrip.rotation, target.rotation)}," +
                    $"\"positionError\":{ContactError},\"elbowAngleBefore\":{debugElbowAngleBefore}," +
                    $"\"elbowAngleAfter\":{debugElbowAngleAfter}," +
                    $"\"heldAlignmentError\":{HeldAlignmentError}," +
                    $"\"upper\":{JsonUtility.ToJson(upper.position)}," +
                    $"\"elbow\":{JsonUtility.ToJson(elbow.position)}," +
                    $"\"hand\":{JsonUtility.ToJson(hand.position)}}}}}";
                var debugRequest = UnityEngine.Networking.UnityWebRequest.Post(
                    "http://127.0.0.1:7786/event", debugPayload, "application/json");
                debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
            }
#endif
#endregion
        }

        private void GroundPickupFoot(Transform thigh, Transform knee, Transform foot, float ankleHeight)
        {
            var floor = transform.position.y + ankleHeight * transform.lossyScale.y;
            if (foot.position.y >= floor) return;
            var goal = foot.position;
            goal.y = floor;
            var rotation = foot.rotation;
            AvatarSeatingRig.Solve(thigh, knee, foot, goal, knee.position - thigh.position);
            foot.rotation = rotation;
        }

        private void ApplyAuthoredPickupContact()
        {
            if (heldBody == null && (!hasTarget || contactWeight <= 0)) return;
            if (heldBody == null || pickupPlacement)
                pickupContactCorrection = Vector3.ClampMagnitude(
                    target.position - HandGrip.position, PickupAnimationSet.MaximumCorrection) * contactWeight;
            var correction = pickupContactCorrection *
                (heldBody != null && !pickupPlacement ? pickupLiftCorrectionWeight : 1);
            if (correction.sqrMagnitude < .00000001f)
            {
                if (heldBody == null || pickupPlacement)
                    ContactError = Vector3.Distance(HandGrip.position, target.position);
                return;
            }
            var wristRotation = hand.rotation;
            var bend = elbow.position - upper.position;
            AvatarSeatingRig.Solve(upper, elbow, hand, hand.position + correction, bend);
            hand.rotation = wristRotation;
            if (heldBody == null || pickupPlacement)
                ContactError = Vector3.Distance(HandGrip.position, target.position);
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
#region debug-point A:low-reach-before
#if UNITY_EDITOR
            var debugSpineBefore = spine.rotation;
            var debugHipsBefore = hips.position;
            var debugTorsoBefore = Vector3.Angle(
                head.position - hips.position, transform.up);
#endif
#endregion

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
#region debug-point A:low-reach-after
#if UNITY_EDITOR
            if (targetHeight <= .25f &&
                Time.unscaledTime >= debugNextLowReachReport)
            {
                debugNextLowReachReport = Time.unscaledTime + .25f;
                var debugPayload =
                    $"{{\"sessionId\":\"low-reach-table-fall\",\"runId\":\"post-fix\"," +
                    $"\"hypothesisId\":\"A\",\"location\":\"AvatarItemRig.ApplyFullBodyPosture\"," +
                    $"\"msg\":\"[DEBUG] low reach posture application\",\"data\":{{" +
                    $"\"targetHeight\":{targetHeight},\"horizontalDistance\":{horizontalDistance}," +
                    $"\"requestedLean\":{PostureLeanDegrees},\"crouchDepth\":{PostureCrouchDepth}," +
                    $"\"spineApplied\":{Quaternion.Angle(debugSpineBefore, spine.rotation)}," +
                    $"\"hipsDisplacement\":{JsonUtility.ToJson(hips.position - debugHipsBefore)}," +
                    $"\"torsoAngleBefore\":{debugTorsoBefore}," +
                    $"\"torsoAngleAfter\":{Vector3.Angle(head.position - hips.position, transform.up)}," +
                    $"\"headPitch\":{PostureHeadPitch},\"leftKnee\":{LeftKneeAngle}," +
                    $"\"rightKnee\":{RightKneeAngle},\"footError\":{MaximumFootPlantError}}}}}";
                var debugRequest = UnityEngine.Networking.UnityWebRequest.Post(
                    "http://127.0.0.1:7777/event", debugPayload, "application/json");
                debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
            }
#endif
#endregion
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
            var grip = HandGrip;
            if (!twoHandAttachment)
                grip = heldProfile.WorldGrip(new Pose(
                    grip.position + grip.rotation * itemFromGrip.position,
                    grip.rotation * itemFromGrip.rotation));
            PalmClearance = PalmClearanceForGrip(
                grip, hand.position, middle.position);
            PoseFingerChains(
                rightFingerChains, rightFingerBaseRotations,
                grip, false);
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

        // Profiles without an authored gesture keep the procedural finger solve.
        private void BeginHandGesture()
        {
            ClearHandGesture();
            var clip = heldProfile != null ? heldProfile.handGesture : null;
            if (clip == null || clip.length <= 0 ||
                !FingerChainsComplete(rightFingerChains)) return;
            var frameRate = clip.frameRate > 0 ? clip.frameRate : 30f;
            var frameCount = Mathf.Max(2,
                Mathf.RoundToInt(clip.length * frameRate) + 1);
            var frames = new Quaternion[frameCount][][];
            var entry = CaptureFingerRotations(rightFingerChains);
            var moved = false;
            for (var frame = 0; frame < frameCount; frame++)
            {
                clip.SampleAnimation(transform.gameObject,
                    Mathf.Min(clip.length, frame / frameRate));
                frames[frame] = CaptureFingerRotations(rightFingerChains);
                moved |= !SameHandGesturePose(frames[0], frames[frame]);
            }
            RestoreFingerPose(rightFingerChains, entry);
            if (!moved)
            {
                Debug.LogWarning(
                    "Hand gesture clip has no finger motion: " + clip.name, this);
                return;
            }
            handGestureFrames = frames;
            handGestureBlendFrom = entry;
            handGestureDuration = Mathf.Max(.001f, clip.length);
            handGestureLead = Mathf.Max(.001f, heldProfile.handGestureLeadTime);
            handGesturePlayhead = 0;
            handGestureBlend = 0;
            handGesturePhase = HandGesturePhase.Closing;
        }

        private bool BeginHandGestureRelease()
        {
            if (handGesturePhase == HandGesturePhase.None ||
                handGestureFrames == null) return false;
            handGestureBlendFrom = CaptureFingerRotations(rightFingerChains);
            handGestureBlend = 0;
            handGesturePhase = HandGesturePhase.Opening;
            return true;
        }

        private void ClearHandGesture()
        {
            handGesturePhase = HandGesturePhase.None;
            handGestureFrames = null;
            handGestureBlendFrom = null;
            handGesturePlayhead = 0;
            handGestureBlend = 0;
        }

        private void ApplyHandGesture(float deltaTime)
        {
            if (handGesturePhase == HandGesturePhase.None) return;
            if (handGestureFrames == null ||
                !FingerChainsComplete(rightFingerChains))
            {
                ClearHandGesture();
                return;
            }
            switch (handGesturePhase)
            {
                case HandGesturePhase.Closing:
                    handGesturePlayhead = Mathf.Min(1,
                        handGesturePlayhead + deltaTime / handGestureDuration);
                    handGestureBlend = Mathf.Min(1,
                        handGestureBlend + deltaTime / handGestureLead);
                    WriteHandGesture(handGesturePlayhead,
                        handGestureBlendFrom, handGestureBlend);
                    if (handGesturePlayhead >= 1 && handGestureBlend >= 1)
                        handGesturePhase = HandGesturePhase.Holding;
                    break;
                case HandGesturePhase.Holding:
                    WriteHandGesture(1, null, 1);
                    break;
                case HandGesturePhase.Opening:
                    handGesturePlayhead = Mathf.Max(0,
                        handGesturePlayhead - deltaTime / handGestureDuration);
                    handGestureBlend = Mathf.Min(1,
                        handGestureBlend + deltaTime / handGestureLead);
                    WriteHandGesture(handGesturePlayhead,
                        handGestureBlendFrom, handGestureBlend);
                    if (handGesturePlayhead <= 0 && handGestureBlend >= 1)
                    {
                        handGestureBlendFrom = handGestureFrames[0];
                        handGestureBlend = 0;
                        handGesturePhase = HandGesturePhase.Releasing;
                    }
                    break;
                case HandGesturePhase.Releasing:
                    handGestureBlend = Mathf.Min(1,
                        handGestureBlend + deltaTime / handGestureLead);
                    BlendHandGestureToBase(handGestureBlend);
                    if (handGestureBlend >= 1) ClearHandGesture();
                    break;
            }
        }

        private void WriteHandGesture(float playhead,
            Quaternion[][] from, float weight)
        {
            var frames = handGestureFrames;
            if (frames == null) return;
            var position = Mathf.Clamp01(playhead) * (frames.Length - 1);
            var lower = Mathf.Clamp(Mathf.FloorToInt(position), 0,
                frames.Length - 1);
            var upper = Mathf.Min(lower + 1, frames.Length - 1);
            var frac = position - lower;
            var blend = from == null ? 1 :
                Mathf.SmoothStep(0, 1, Mathf.Clamp01(weight));
            for (var finger = 0; finger < rightFingerChains.Length; finger++)
            for (var joint = 0; joint < rightFingerChains[finger].Length; joint++)
            {
                var value = Quaternion.Slerp(frames[lower][finger][joint],
                    frames[upper][finger][joint], frac);
                if (from != null)
                    value = Quaternion.Slerp(from[finger][joint], value, blend);
                rightFingerChains[finger][joint].localRotation = value;
            }
        }

        private void BlendHandGestureToBase(float weight)
        {
            var open = handGestureBlendFrom;
            if (open == null) return;
            var blend = Mathf.SmoothStep(0, 1, Mathf.Clamp01(weight));
            for (var finger = 0; finger < rightFingerChains.Length; finger++)
            for (var joint = 0; joint < rightFingerChains[finger].Length; joint++)
            {
                var chain = rightFingerChains[finger][joint];
                chain.localRotation = Quaternion.Slerp(
                    open[finger][joint], chain.localRotation, blend);
            }
        }

        private static bool SameHandGesturePose(
            Quaternion[][] first, Quaternion[][] second)
        {
            for (var finger = 0; finger < first.Length; finger++)
            for (var joint = 0; joint < first[finger].Length; joint++)
                if (Quaternion.Angle(first[finger][joint],
                        second[finger][joint]) > .01f) return false;
            return true;
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
            var fingers = new[] { "lengyue_thumb", "lengyue_index", "lengyue_middle", "lengyue_ring", "lengyue_little" };
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
#region debug-point A-B-C-D-E:held-follow
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Time.unscaledTime >= debugNextHandGapReport)
            {
                debugNextHandGapReport = Time.unscaledTime + .05f;
                var debugActualGrip = heldProfile.WorldGrip(heldBody.Pose);
                var debugExpectedGrip = heldProfile.WorldGrip(item);
                var debugFollowPayload =
                    $"{{\"sessionId\":\"held-item-hand-gap\",\"runId\":\"post-fix\"," +
                    $"\"hypothesisId\":\"A-B-C-D-E\",\"location\":\"AvatarItemRig.SyncHeldBody\"," +
                    $"\"msg\":\"[DEBUG] held item follow sample\",\"data\":{{" +
                    $"\"item\":\"{heldBody.name}\"," +
                    $"\"override\":false," +
                    $"\"beforePosition\":{JsonUtility.ToJson(actualBefore.position)}," +
                    $"\"expectedPosition\":{JsonUtility.ToJson(item.position)}," +
                    $"\"actualPosition\":{JsonUtility.ToJson(heldBody.Pose.position)}," +
                    $"\"handGrip\":{JsonUtility.ToJson(HandGrip.position)}," +
                    $"\"actualGrip\":{JsonUtility.ToJson(debugActualGrip.position)}," +
                    $"\"expectedGrip\":{JsonUtility.ToJson(debugExpectedGrip.position)}," +
                    $"\"actualGripToHand\":{Vector3.Distance(debugActualGrip.position, HandGrip.position)}," +
                    $"\"expectedGripToHand\":{Vector3.Distance(debugExpectedGrip.position, HandGrip.position)}," +
                    $"\"bodyToExpected\":{Vector3.Distance(heldBody.Pose.position, item.position)}," +
                    $"\"rigidbodyToTransform\":{Vector3.Distance(heldBody.Rigidbody.position, heldBody.transform.position)}," +
                    $"\"heldAlignmentError\":{HeldAlignmentError}}}}}";
                var debugFollowRequest = UnityEngine.Networking.UnityWebRequest.Post(
                    Application.isEditor
                        ? "http://127.0.0.1:7782/event"
                        : "http://127.0.0.1:7782/event",
                    debugFollowPayload, "application/json");
                debugFollowRequest.SendWebRequest().completed += _ => debugFollowRequest.Dispose();
            }
#endif
#endregion
        }

        public void End(Guid run)
        {
            if (owner != run) return;
            naturalHold = false;
            if (idleVariation != null) idleVariation.SetHoldLocked(false);
            if (layer >= 0 && animator != null) animator.SetLayerWeight(layer, 0);
            ClearUse();
            ClearPickup();
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
            ClearHandGesture();
        }
    }
}
