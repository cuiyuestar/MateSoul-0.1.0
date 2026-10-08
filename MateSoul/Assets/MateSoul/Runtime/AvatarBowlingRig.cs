using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DefaultExecutionOrder(11000)]
    public sealed class AvatarBowlingRig : MonoBehaviour
    {
        [SerializeField] private Transform hips, spine, upperArm, forearm, hand;
        [SerializeField] private Transform leftLeg, leftKnee, leftFoot, rightLeg, rightKnee, rightFoot;
        private Vector3 target;
        private float weight;
        public bool IsConfigured => hips && spine && upperArm && forearm && hand &&
            leftLeg && leftKnee && leftFoot && rightLeg && rightKnee && rightFoot;

        public void Configure(Transform[] bones)
        {
            if (bones.Length != 11) throw new ArgumentException("Eleven bowling rig bindings required.");
            hips = bones[0]; spine = bones[1]; upperArm = bones[2]; forearm = bones[3]; hand = bones[4];
            leftLeg = bones[5]; leftKnee = bones[6]; leftFoot = bones[7];
            rightLeg = bones[8]; rightKnee = bones[9]; rightFoot = bones[10];
        }

        public void Reach(Vector3 ballCenter, float amount)
        {
            target = ballCenter + Vector3.up * .08f;
            weight = Mathf.Clamp01(amount);
        }

        public void Release() => weight = 0;

        private void LateUpdate()
        {
            if (weight <= 0 || !IsConfigured) return;
            var left = leftFoot.position;
            var right = rightFoot.position;
            hips.position -= transform.up * (.29f * weight);
            spine.rotation = Quaternion.AngleAxis(16 * weight, transform.right) * spine.rotation;
            AvatarSeatingRig.Solve(leftLeg, leftKnee, leftFoot, left, transform.forward);
            AvatarSeatingRig.Solve(rightLeg, rightKnee, rightFoot, right, transform.forward);
            AvatarSeatingRig.Solve(upperArm, forearm, hand,
                Vector3.Lerp(hand.position, target, weight), transform.right);
        }
    }
}
