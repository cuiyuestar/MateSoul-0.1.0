using System;
using System.Linq;
using UnityEngine;

namespace MateSoul.Runtime
{
    // Animator evaluates first, this full-body contact pose second, cloth support last.
    [DefaultExecutionOrder(11010)]
    public sealed class AvatarLyingRig : MonoBehaviour
    {
        [SerializeField] private Transform hips, spine;
        [SerializeField] private Transform leftLeg, leftKnee, leftFoot;
        [SerializeField] private Transform rightLeg, rightKnee, rightFoot;
        [SerializeField] private Transform leftArm, leftElbow, leftHand;
        [SerializeField] private Transform rightArm, rightElbow, rightHand;
        [SerializeField] private Transform[] fingers;
        [SerializeField] private Quaternion[] relaxedFingers;
        private Transform[] bones;
        private Quaternion[] neutral;
        private Vector3 neutralHips;
        private BedTarget bed;
        private MateSoul.Cloth.SkirtPoseGuard[] skirts;

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
        }

        public string Check(BedTarget target)
        {
            if (!IsConfigured) return "角色缺少躺卧骨骼绑定。";
            if (target.Width < .75f || target.Length < 1.65f)
                return "床面不足以容纳角色。";
            var height = target.Center.y - target.FloorY;
            if (height < .35f || height > .9f) return "床面高度不适合安全躺卧。";
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

        public void Release()
        {
            Weight = 0;
            bed = null;
            if (bones == null) return;
            hips.localPosition = neutralHips;
            for (var index = 0; index < bones.Length; index++)
                bones[index].localRotation = neutral[index];
            foreach (var skirt in skirts)
                skirt.SetSeatSupport(Vector3.zero, Quaternion.identity, Vector2.zero, 0);
        }

        private void LateUpdate()
        {
            if (bed == null) return;
            if (Weight > 0)
            {
                hips.localPosition = Vector3.Lerp(hips.localPosition, neutralHips, Weight);
                for (var index = 0; index < bones.Length; index++)
                    bones[index].localRotation = Quaternion.Slerp(
                        bones[index].localRotation, neutral[index], Weight);
                for (var index = 0; index < fingers.Length; index++)
                    fingers[index].localRotation = Quaternion.Slerp(
                        fingers[index].localRotation, relaxedFingers[index], Weight);
            }
        }
    }
}
