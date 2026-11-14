using System;
using System.Linq;
using MateSoul.RoomNavigation;
using UnityEngine;

namespace MateSoul.Runtime
{
    // Animator evaluates first, this full-body contact pose second, cloth support last.
    [DefaultExecutionOrder(11010)]
    public sealed class AvatarLyingRig : MonoBehaviour
    {
        // Keep in sync with SleepAnimationBuilder.LayerName / StateName.
        private const string SleepLayerName = "Lying";
        private const string SleepStateName = "Sleep";
        private const float SleepBlendSeconds = .35f;
        private static readonly int SleepState = Animator.StringToHash(SleepStateName);

        [SerializeField] private Transform hips, spine;
        [SerializeField] private Transform leftLeg, leftKnee, leftFoot;
        [SerializeField] private Transform rightLeg, rightKnee, rightFoot;
        [SerializeField] private Transform leftArm, leftElbow, leftHand;
        [SerializeField] private Transform rightArm, rightElbow, rightHand;
        [SerializeField] private Transform[] fingers;
        [SerializeField] private Quaternion[] relaxedFingers;
        // Sampled bones sit near the middle of the body, so the lowest one keeps
        // roughly half a torso of clearance above the cushion.
        [SerializeField] private float sleepBoneClearance = .09f;
        private Transform[] bones;
        private Quaternion[] neutral;
        private Vector3 neutralHips;
        private BedTarget bed;
        private MateSoul.Cloth.SkirtPoseGuard[] skirts;
        private Animator animator;
        private int sleepLayer = -1;
        private bool sleepRequested, sleeping, sleepCalibrated;
        private float sleepWeight;
        private Transform sleepHead, sleepClavicleL, sleepClavicleR;
        private Transform[] sleepSamples;
        private Transform[] sleepTorso;
        private Quaternion sleepRotationFix = Quaternion.identity;
        private Quaternion sleepBaseLocalRotation = Quaternion.identity;
        private Vector3 sleepBaseLocalPosition;
        private float sleepHeightFix;

        public float Weight { get; private set; }
        public Vector3 PelvisPosition => hips.position;
        public bool IsConfigured => hips && spine &&
            leftLeg && leftKnee && leftFoot && rightLeg && rightKnee && rightFoot &&
            leftArm && leftElbow && leftHand && rightArm && rightElbow && rightHand;

        public void Configure(Transform[] binding)
        {
            if (binding == null || binding.Length != 22)
                throw new ArgumentException("Twenty-two character bones required.");
            hips = binding[0];
            spine = binding[1];
            leftLeg = binding[2];
            leftKnee = binding[3];
            leftFoot = binding[4];
            rightLeg = binding[6];
            rightKnee = binding[7];
            rightFoot = binding[8];
            leftArm = binding[10];
            leftElbow = binding[11];
            leftHand = binding[12];
            rightArm = binding[13];
            rightElbow = binding[14];
            rightHand = binding[15];
            fingers = leftHand.GetComponentsInChildren<Transform>()
                .Concat(rightHand.GetComponentsInChildren<Transform>())
                .Where(transform => transform != leftHand && transform != rightHand).ToArray();
            relaxedFingers = fingers.Select(transform => transform.localRotation).ToArray();
        }

        private void Initialize()
        {
            if (bones != null) return;
            if (!IsConfigured) throw new InvalidOperationException("角色缺少躺卧骨骼绑定。");
            bones = new[]
            {
                hips, spine, leftLeg, leftKnee, leftFoot,
                rightLeg, rightKnee, rightFoot,
                leftArm, leftElbow, leftHand, rightArm, rightElbow, rightHand
            };
            neutral = bones.Select(transform => transform.localRotation).ToArray();
            neutralHips = hips.localPosition;
            skirts = GetComponentsInChildren<MateSoul.Cloth.SkirtPoseGuard>(true);
            animator = GetComponentInChildren<Animator>(true);
            sleepHead = FindBone("lengyue_head_01", "lengyue_head");
            sleepClavicleL = FindBone("lengyue_clavicle_l");
            sleepClavicleR = FindBone("lengyue_clavicle_r");
            sleepSamples = bones
                .Concat(new[] { sleepHead, sleepClavicleL, sleepClavicleR })
                .Where(item => item != null)
                .Distinct()
                .ToArray();
            // 头会枕在枕头上，不能当作“必须贴床面”的参考；用躯干（肩/脊柱/胯）
            // 决定身体高度。
            sleepTorso = new[] { hips, spine, sleepClavicleL, sleepClavicleR }
                .Where(item => item != null)
                .Distinct()
                .ToArray();
        }

        private Transform FindBone(params string[] names)
        {
            var candidates = GetComponentsInChildren<Transform>(true);
            foreach (var name in names)
                foreach (var candidate in candidates)
                    if (candidate.name == name) return candidate;
            return null;
        }

        public string Check(BedTarget target)
        {
            if (!IsConfigured) return "角色缺少躺卧骨骼绑定。";
            var height = target.Center.y - target.FloorY;
            if (height < .25f || height > .9f) return "躺卧面高度不适合安全躺卧。";
            return null;
        }

        public void SetPose(BedTarget target, float weight)
        {
            Initialize();
            bed = target;
            Weight = Mathf.Clamp01(weight);
            foreach (var skirt in skirts)
                skirt.SetSeatSupport(target.Center, target.Rotation,
                    new Vector2(target.Width, target.Length) * .5f, Weight);
        }

        // The body is already on the seat, so the authored sleep loop can take
        // over the skeleton. Without it the contact pose is a frozen stand pose.
        public void BeginSleep()
        {
            Initialize();
            if (bed == null) return;
            sleepRequested = true;
            sleeping = false;
            sleepCalibrated = false;
            sleepWeight = 0;
        }

        public void Release()
        {
            StopSleep();
            Weight = 0;
            bed = null;
            if (bones == null) return;
            hips.localPosition = neutralHips;
            for (var index = 0; index < bones.Length; index++)
                bones[index].localRotation = neutral[index];
            foreach (var skirt in skirts)
                skirt.SetSeatSupport(Vector3.zero, Quaternion.identity, Vector2.zero, 0);
        }

        private void StopSleep()
        {
            sleepRequested = false;
            sleeping = false;
            sleepCalibrated = false;
            sleepWeight = 0;
            sleepRotationFix = Quaternion.identity;
            sleepHeightFix = 0;
            if (animator != null && sleepLayer >= 0)
                animator.SetLayerWeight(sleepLayer, 0);
        }

        private void LateUpdate()
        {
            if (bed == null) return;
            UpdateSleep();
            if (Weight <= 0) return;
            // The loop drives every bone; locking it towards the stand pose would
            // flatten the animation back into a plank.
            if (sleepRequested)
            {
                AlignSleepPose();
                return;
            }
            hips.localPosition = Vector3.Lerp(hips.localPosition, neutralHips, Weight);
            for (var index = 0; index < bones.Length; index++)
                bones[index].localRotation = Quaternion.Slerp(
                    bones[index].localRotation, neutral[index], Weight);
            for (var index = 0; index < fingers.Length; index++)
                fingers[index].localRotation = Quaternion.Slerp(
                    fingers[index].localRotation, relaxedFingers[index], Weight);
        }

        private void UpdateSleep()
        {
            if (!sleepRequested) return;
            if (animator == null)
            {
                sleepRequested = false;
                Debug.LogWarning("MATESOUL_SLEEP animator_missing");
                return;
            }
            if (sleepLayer < 0)
            {
                sleepLayer = animator.GetLayerIndex(SleepLayerName);
                if (sleepLayer < 0)
                {
                    sleepRequested = false;
                    Debug.LogWarning("MATESOUL_SLEEP layer_missing name=" + SleepLayerName);
                    return;
                }
            }
            if (!animator.HasState(sleepLayer, SleepState))
            {
                // Play() on a missing state would spam an Animator error every
                // frame, so report the mismatch once and stay in the static pose.
                sleepRequested = false;
                Debug.LogWarning("MATESOUL_SLEEP state_missing name=" + SleepStateName);
                return;
            }
            if (!sleeping)
            {
                animator.Play(SleepState, sleepLayer, 0f);
                // The clip was authored on another skeleton, so its world
                // orientation is unknown. Sample it once at full weight to derive
                // the alignment fix, then fade it in from the current pose -
                // otherwise the body would twist when the blend completes.
                animator.SetLayerWeight(sleepLayer, 1f);
                animator.Update(0f);
                CalibrateSleepPose();
                animator.SetLayerWeight(sleepLayer, 0f);
                animator.Update(0f);
                sleeping = true;
                Debug.Log($"MATESOUL_SLEEP start layer={sleepLayer} " +
                          $"state={SleepStateName} clip=fade_in");
            }
            sleepWeight = Mathf.MoveTowards(sleepWeight, 1f,
                Time.deltaTime / SleepBlendSeconds);
            animator.SetLayerWeight(sleepLayer, sleepWeight);
        }

        private void AlignSleepPose()
        {
            if (!sleepCalibrated) return;
            ApplySleepPose(Mathf.SmoothStep(0f, 1f, sleepWeight));
        }

        // The clip was authored on another skeleton, so its overall orientation
        // relative to the mattress is unknown. Measure the body axes once the
        // loop is playing, then keep the body on its side along the seat and
        // resting on the cushion.
        private void CalibrateSleepPose()
        {
            sleepCalibrated = true;
            if (bed == null || sleepHead == null || sleepClavicleL == null ||
                sleepClavicleR == null || leftFoot == null || rightFoot == null)
            {
                Debug.LogWarning("MATESOUL_SLEEP align_skipped missing_bones");
                return;
            }
            var footMid = (leftFoot.position + rightFoot.position) * .5f;
            var headDelta = sleepHead.position - footMid;
            var shoulders = Vector3.ProjectOnPlane(
                sleepClavicleR.position - sleepClavicleL.position,
                headDelta.normalized);
            if (headDelta.sqrMagnitude < .04f || shoulders.sqrMagnitude < .0004f)
            {
                Debug.LogWarning("MATESOUL_SLEEP align_skipped degenerate_axes");
                return;
            }
            var headAxis = headDelta.normalized;
            var xAxis = shoulders.normalized;
            var yAxis = headAxis;
            var current = Quaternion.LookRotation(Vector3.Cross(xAxis, yAxis), yAxis);
            // The teleport lays the body with its head towards -LyingAxis. A side
            // sleep keeps whichever side the clip chose, so the shoulder line only
            // decides which side faces the ceiling.
            var yTarget = -bed.LyingAxis.normalized;
            var xTarget = Vector3.Dot(xAxis, Vector3.up) >= 0
                ? Vector3.up
                : Vector3.down;
            var target = Quaternion.LookRotation(
                Vector3.Cross(xTarget, yTarget), yTarget);
            sleepRotationFix = target * Quaternion.Inverse(current);
            sleepBaseLocalRotation = hips.localRotation;
            sleepBaseLocalPosition = hips.localPosition;
            sleepHeightFix = 0;
            ApplySleepPose(1f);
            var lowest = sleepSamples.Min(item => item.position.y);
            // Hands and feet hang below the torso in a side sleep; using the lowest
            // bone of the whole skeleton would hoist the body into the air. The
            // torso is what has to rest on the mattress.
            var torso = sleepTorso.Length > 0
                ? sleepTorso.Min(item => item.position.y)
                : lowest;
            // 上限放宽是为了把“陷进床里”的身体抬回床面；下限收紧是为了避免
            // 又一次把整个人吊到半空。
            sleepHeightFix = Mathf.Clamp(
                bed.Center.y + sleepBoneClearance - torso, -.35f, .60f);
            ApplySleepPose(1f);
            Debug.Log($"MATESOUL_SLEEP aligned turn=" +
                      $"{Quaternion.Angle(Quaternion.identity, sleepRotationFix):F1} " +
                      $"headAxis={headAxis:F2} shoulder={xAxis:F2} " +
                      $"surface={bed.Center.y:F3} torso={torso:F3} " +
                      $"lowest={lowest:F3} lift={sleepHeightFix:F3}");
        }

        // Rebuilt from the calibrated base every frame, so it never accumulates.
        // The blend lets the fix grow with the layer weight instead of snapping.
        private void ApplySleepPose(float blend)
        {
            var rotation = Quaternion.Slerp(Quaternion.identity,
                sleepRotationFix, blend);
            var lift = sleepHeightFix * blend;
            var parent = hips.parent;
            if (parent == null)
            {
                hips.localRotation = Quaternion.Slerp(hips.localRotation,
                    rotation * sleepBaseLocalRotation, blend);
                hips.localPosition = Vector3.Lerp(hips.localPosition,
                    sleepBaseLocalPosition + Vector3.up * lift, blend);
                return;
            }
            var target = Quaternion.Inverse(parent.rotation) * rotation *
                         parent.rotation * sleepBaseLocalRotation;
            hips.localRotation = Quaternion.Slerp(hips.localRotation, target,
                blend);
            hips.localPosition = Vector3.Lerp(hips.localPosition,
                sleepBaseLocalPosition +
                parent.InverseTransformDirection(Vector3.up) * lift, blend);
        }
    }
}
