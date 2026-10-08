using System;
using System.Linq;
using UnityEngine;

namespace MateSoul.Runtime
{
    // Generic-rig bindings are authored on the runtime prefab, not resolved by name in gameplay.
    // Animator evaluates first, this contact pose second, cloth simulation last.
    [DefaultExecutionOrder(11000)]
    public sealed class AvatarSeatingRig : MonoBehaviour
    {
        [SerializeField] private Transform hips, spine;
        [SerializeField] private Transform leftLeg, leftKnee, leftFoot, leftToe;
        [SerializeField] private Transform rightLeg, rightKnee, rightFoot, rightToe;
        [SerializeField] private Transform leftArm, leftElbow, leftHand, rightArm, rightElbow, rightHand;
        [SerializeField] private Transform leftIndex, leftMiddle, leftPinky, rightIndex, rightMiddle, rightPinky;
        [SerializeField] private float pelvisAboveSeat = .17f;
        [SerializeField] private float ankleAboveFloor = .105f;
        [SerializeField] private Transform[] fingers;
        [SerializeField] private Quaternion[] relaxedFingers;
        private Transform[] bones;
        private Quaternion[] animated, neutral;
        private Quaternion leftSole, rightSole;
        private Quaternion leftPalm, rightPalm;
        private Vector3 startLeftFoot, startRightFoot;
        private Vector3 neutralHips;
        private SeatTarget seat;
        private MateSoul.Cloth.SkirtPoseGuard[] skirts;
        public float Weight { get; private set; }
        public Vector3 PelvisPosition => hips.position;
        public Quaternion SpineRotation => spine.rotation;
        public Vector3 LeftFootPosition => leftFoot.position;
        public Vector3 RightFootPosition => rightFoot.position;
        public float ContactOffset => pelvisAboveSeat;
        public bool IsConfigured => hips && spine && leftLeg && leftKnee && leftFoot && leftToe &&
            rightLeg && rightKnee && rightFoot && rightToe &&
            leftArm && leftElbow && leftHand && rightArm && rightElbow && rightHand &&
            leftIndex && leftMiddle && leftPinky && rightIndex && rightMiddle && rightPinky;

        public void Configure(Transform[] binding)
        {
            if (binding == null || binding.Length != 22) throw new ArgumentException("Twenty-two seating bones required.");
            hips = binding[0]; spine = binding[1];
            leftLeg = binding[2]; leftKnee = binding[3]; leftFoot = binding[4]; leftToe = binding[5];
            rightLeg = binding[6]; rightKnee = binding[7]; rightFoot = binding[8]; rightToe = binding[9];
            leftArm = binding[10]; leftElbow = binding[11]; leftHand = binding[12];
            rightArm = binding[13]; rightElbow = binding[14]; rightHand = binding[15];
            leftIndex = binding[16]; leftMiddle = binding[17]; leftPinky = binding[18];
            rightIndex = binding[19]; rightMiddle = binding[20]; rightPinky = binding[21];
            fingers = leftHand.GetComponentsInChildren<Transform>().Concat(rightHand.GetComponentsInChildren<Transform>())
                .Where(t => t != leftHand && t != rightHand).ToArray();
            relaxedFingers = fingers.Select(t => t.localRotation).ToArray();
        }

        private void Initialize()
        {
            if (bones != null) return;
            if (!IsConfigured) throw new InvalidOperationException("角色缺少落座骨骼绑定。");
            skirts = GetComponentsInChildren<MateSoul.Cloth.SkirtPoseGuard>(true);
            bones = new[] { hips, spine, leftLeg, leftKnee, leftFoot, rightLeg, rightKnee, rightFoot,
                leftArm, leftElbow, leftHand, rightArm, rightElbow, rightHand };
            animated = new Quaternion[bones.Length];
            neutral = new Quaternion[bones.Length];
            for (var i = 0; i < bones.Length; i++) neutral[i] = bones[i].localRotation;
            neutralHips = hips.localPosition;
            leftSole = SoleRotation(leftFoot, leftToe);
            rightSole = SoleRotation(rightFoot, rightToe);
            leftPalm = PalmRotation(leftHand, leftIndex, leftMiddle, leftPinky, 1);
            rightPalm = PalmRotation(rightHand, rightIndex, rightMiddle, rightPinky, -1);
        }

        private static Quaternion PalmRotation(Transform hand, Transform index, Transform middle, Transform pinky, int sign)
        {
            var forward = hand.InverseTransformDirection(middle.position - hand.position);
            var across = hand.InverseTransformDirection(index.position - pinky.position);
            var up = Vector3.Cross(forward, across) * sign;
            return Quaternion.Inverse(Quaternion.LookRotation(forward, up));
        }

        private Quaternion SoleRotation(Transform foot, Transform toe)
        {
            var inverse = Quaternion.Inverse(transform.rotation);
            var direction = inverse * (toe.position - foot.position);
            return Quaternion.FromToRotation(direction, new Vector3(0, -.05f, .136f)) *
                   inverse * foot.rotation;
        }

        public string Check(SeatTarget target)
        {
            if (!IsConfigured) return "角色缺少落座骨骼绑定。";
            if (target.Backless ? target.Width < .29f || target.Depth < .29f :
                target.Width < .36f || target.Depth < .34f) return "椅面不足以容纳角色。";
            if (target.Center.y - target.FloorY > .54f) return "椅面过高，双脚无法可靠落地。";
            return null;
        }

        public void SetPose(SeatTarget target, float weight)
        {
            Initialize();
            if (seat == null) { startLeftFoot = leftFoot.position; startRightFoot = rightFoot.position; }
            seat = target;
            Weight = Mathf.Clamp01(weight);
            foreach (var skirt in skirts)
                skirt.SetSeatSupport(target.Center, target.Rotation,
                    new Vector2(target.Width, target.Depth) * .5f, Weight);
        }

        public void Release()
        {
            Weight = 0;
            seat = null;
            if (bones == null) return;
            foreach (var skirt in skirts) skirt.SetSeatSupport(Vector3.zero, Quaternion.identity, Vector2.zero, 0);
            hips.localPosition = neutralHips;
            for (var i = 0; i < bones.Length; i++) bones[i].localRotation = neutral[i];
        }

        private void LateUpdate()
        {
            if (seat == null || Weight <= 0) return;
            var originalHips = hips.position;
            for (var i = 0; i < bones.Length; i++) animated[i] = bones[i].localRotation;
            // Neutral pelvis prevents idle sway from moving the skin's seat contact.
            hips.localRotation = Quaternion.Slerp(animated[0], neutral[0], Weight);
            hips.position = Vector3.Lerp(originalHips,
                seat.Center + seat.Forward * .015f + Vector3.up * pelvisAboveSeat, Weight);
            var localLeanAxis = Quaternion.Inverse(spine.parent.rotation) * seat.Right;
            spine.localRotation = Quaternion.AngleAxis(6f * Weight, localLeanAxis) * neutral[1];
            PoseLeg(leftLeg, leftKnee, leftFoot, leftSole, startLeftFoot, -.095f);
            PoseLeg(rightLeg, rightKnee, rightFoot, rightSole, startRightFoot, .095f);
            PoseArm(leftArm, leftElbow, leftHand, leftPalm, -.12f);
            PoseArm(rightArm, rightElbow, rightHand, rightPalm, .12f);
            for (var i = 8; i < bones.Length; i++)
                bones[i].localRotation = Quaternion.Slerp(animated[i], bones[i].localRotation, Weight);
            for (var i = 0; i < fingers.Length; i++)
                fingers[i].localRotation = Quaternion.Slerp(fingers[i].localRotation, relaxedFingers[i], Weight);
        }

        private void PoseLeg(Transform upper, Transform lower, Transform foot, Quaternion sole, Vector3 start, float side)
        {
            var ankle = seat.Center + seat.Right * side + seat.Forward * .39f;
            ankle.y = seat.FloorY + ankleAboveFloor;
            ankle = Vector3.Lerp(start, ankle, Weight);
            var rotation = foot.rotation;
            Solve(upper, lower, foot, ankle, seat.Forward);
            foot.rotation = Quaternion.Slerp(rotation, seat.Rotation * sole, Weight);
        }

        private void PoseArm(Transform upper, Transform lower, Transform hand, Quaternion palm, float side)
        {
            var target = seat.Center + seat.Right * side + seat.Forward * .25f + Vector3.up * .17f;
            Solve(upper, lower, hand, target, seat.Right * Mathf.Sign(side));
            hand.rotation = seat.Rotation * palm;
        }

        public static void Solve(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 bend)
        {
            var a = Vector3.Distance(upper.position, lower.position);
            var b = Vector3.Distance(lower.position, end.position);
            var offset = target - upper.position;
            var distance = Mathf.Clamp(offset.magnitude, Mathf.Abs(a - b) + .001f, a + b - .001f);
            var direction = offset.normalized;
            var along = (a * a - b * b + distance * distance) / (2 * distance);
            var perpendicular = Vector3.ProjectOnPlane(bend, direction).normalized;
            var elbow = upper.position + direction * along +
                        perpendicular * Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, elbow - upper.position) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(end.position - lower.position, target - lower.position) * lower.rotation;
        }
    }
}
