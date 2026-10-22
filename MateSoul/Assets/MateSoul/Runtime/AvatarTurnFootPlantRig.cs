using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent, DefaultExecutionOrder(10900)]
    public sealed class AvatarTurnFootPlantRig : MonoBehaviour
    {
        private const float CaptureWeight = .12f;
        private const float ReleaseWeight = .04f;
        private const float BlendSpeed = 8f;

        [SerializeField] private Animator animator;
        [SerializeField] private AvatarMotor motor;
        [SerializeField] private Transform characterRoot;
        [SerializeField] private Transform leftLeg;
        [SerializeField] private Transform leftKnee;
        [SerializeField] private Transform leftFoot;
        [SerializeField] private Transform rightLeg;
        [SerializeField] private Transform rightKnee;
        [SerializeField] private Transform rightFoot;
        [SerializeField, HideInInspector] private float leftPlantWeight;
        [SerializeField, HideInInspector] private float rightPlantWeight;

        private Pose leftAnchor;
        private Pose rightAnchor;
        private bool leftAnchored;
        private bool rightAnchored;
        private float blend;
        private float appliedLeftWeight;
        private float appliedRightWeight;

        public bool IsConfigured => animator != null && motor != null &&
                                    characterRoot != null &&
                                    leftLeg != null && leftKnee != null && leftFoot != null &&
                                    rightLeg != null && rightKnee != null && rightFoot != null;
        public float LeftWeight => appliedLeftWeight;
        public float RightWeight => appliedRightWeight;
        public Transform LeftFoot => leftFoot;
        public Transform RightFoot => rightFoot;

        // Slow the body slightly while both feet share load at a step transition.
        public float RotationScale
        {
            get
            {
                var sharedLoad = Mathf.Min(appliedLeftWeight, appliedRightWeight);
                return Mathf.Lerp(1f, .55f, Mathf.Clamp01(sharedLoad * 2f));
            }
        }

        public void Configure(Animator characterAnimator, AvatarMotor avatarMotor,
            Transform root, Transform leftUpperLeg, Transform leftLowerLeg,
            Transform leftFootBone, Transform rightUpperLeg,
            Transform rightLowerLeg, Transform rightFootBone)
        {
            animator = characterAnimator;
            motor = avatarMotor;
            characterRoot = root;
            leftLeg = leftUpperLeg;
            leftKnee = leftLowerLeg;
            leftFoot = leftFootBone;
            rightLeg = rightUpperLeg;
            rightKnee = rightLowerLeg;
            rightFoot = rightFootBone;
            ResetState();
        }

        private void OnEnable() => ResetState();

        private void OnDisable() => ResetState();

        private void LateUpdate()
        {
            if (!IsConfigured) return;
            var turning = motor.IsTurning &&
                          Mathf.Abs(animator.GetFloat("TurnRate")) > .02f &&
                          animator.GetFloat("Speed") < .12f;
            blend = Mathf.MoveTowards(blend, turning ? 1 : 0,
                BlendSpeed * Time.deltaTime);

            appliedLeftWeight = ApplyFoot(leftLeg, leftKnee, leftFoot,
                Mathf.Clamp01(leftPlantWeight) * blend,
                ref leftAnchor, ref leftAnchored);
            appliedRightWeight = ApplyFoot(rightLeg, rightKnee, rightFoot,
                Mathf.Clamp01(rightPlantWeight) * blend,
                ref rightAnchor, ref rightAnchored);

            if (!turning && blend <= 0)
            {
                leftAnchored = false;
                rightAnchored = false;
            }
        }

        private float ApplyFoot(Transform upper, Transform lower, Transform foot,
            float weight, ref Pose anchor, ref bool anchored)
        {
            if (!anchored && weight >= CaptureWeight)
            {
                anchor = new Pose(foot.position, foot.rotation);
                anchored = true;
            }
            else if (anchored && weight <= ReleaseWeight)
            {
                anchored = false;
            }
            if (!anchored) return 0;
            var solveWeight = Mathf.SmoothStep(0, 1,
                Mathf.InverseLerp(CaptureWeight, .65f, weight));

            var footRotation = foot.rotation;
            // Blend the endpoint, then solve the leg once. Blending joint
            // rotations after IK makes a near-straight leg sweep an arc away
            // from its planted point. Ease the contact in promptly and retain
            // positional support until the release envelope is nearly clear.
            var positionWeight = 1f - Mathf.Pow(1f - solveWeight, 3);
            var target = Vector3.Lerp(foot.position, anchor.position, positionWeight);
            AvatarSeatingRig.Solve(upper, lower, foot,
                target, characterRoot.forward);
            foot.rotation = Quaternion.Slerp(
                footRotation, anchor.rotation, solveWeight);
            return solveWeight;
        }

        private void ResetState()
        {
            blend = 0;
            appliedLeftWeight = 0;
            appliedRightWeight = 0;
            leftAnchored = false;
            rightAnchored = false;
        }
    }
}
