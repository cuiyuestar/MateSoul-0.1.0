using System;
using System.Linq;
using MateSoul.Core;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DefaultExecutionOrder(12000)]
    [DisallowMultipleComponent]
    public sealed class AvatarSocialAttentionRig : MonoBehaviour
    {
        private const float HeadYawLimit = 48f;
        private const float HeadPitchUpLimit = 26f;
        private const float HeadPitchDownLimit = 20f;
        private const float EyeYawLimit = 12f;
        private const float EyePitchLimit = 8f;
        private const float BlendInResponse = 5.5f;
        private const float BlendOutResponse = 4f;
        private const float HeadYawSpeed = 90f;
        private const float HeadPitchSpeed = 70f;

        [SerializeField] private Transform characterRoot;
        [SerializeField] private Transform neck;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftEye;
        [SerializeField] private Transform rightEye;
        private Transform target;
        private bool tracking;
        private float weight;
        private float lastRawYaw;
        private float lastRawPitch;
        private float currentHeadPitch;
        private float rearYawSign;

        public bool IsConfigured =>
            characterRoot != null && neck != null && head != null &&
            leftEye != null && rightEye != null;
        public bool IsTracking => tracking;
        public Transform Target => target;
        public float Weight => weight;
        public float CurrentHeadYaw { get; private set; }
        public float TargetYaw { get; private set; }

        public static AvatarSocialAttentionRig Attach(GameObject character, Animator animator)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (animator == null) throw new ArgumentNullException(nameof(animator));
            var transforms = animator.GetComponentsInChildren<Transform>(true);
            Transform Bone(string name) =>
                transforms.FirstOrDefault(value => value.name == name);
            var rig = animator.GetComponent<AvatarSocialAttentionRig>() ??
                      animator.gameObject.AddComponent<AvatarSocialAttentionRig>();
            rig.Configure(character.transform, Bone("Neck"), Bone("Head 1"),
                Bone("Eye_L"), Bone("Eye_R"));
            return rig;
        }

        public void Configure(Transform root, Transform neckBone, Transform headBone,
            Transform leftEyeBone, Transform rightEyeBone)
        {
            characterRoot = root;
            neck = neckBone;
            head = headBone;
            leftEye = leftEyeBone;
            rightEye = rightEyeBone;
            if (!IsConfigured)
                throw new InvalidOperationException(
                    "角色缺少社交注视所需的 Neck、Head 1、Eye_L 或 Eye_R 骨骼。");
        }

        public void Begin(Transform lookTarget)
        {
            if (!IsConfigured) throw new InvalidOperationException("角色社交注视骨骼未配置。");
            if (lookTarget == null) throw new InvalidOperationException("玩家头部跟踪不可用。");
            if (target != lookTarget)
                rearYawSign = 0;
            target = lookTarget;
            tracking = true;
        }

        public void End(bool immediate)
        {
            tracking = false;
            target = null;
            if (immediate)
            {
                weight = 0;
                CurrentHeadYaw = 0;
                currentHeadPitch = 0;
                TargetYaw = 0;
                rearYawSign = 0;
                lastRawYaw = lastRawPitch = 0;
            }
        }

        private void LateUpdate()
        {
            var desiredWeight = tracking && target != null ? 1f : 0f;
            var response = desiredWeight > weight ? BlendInResponse : BlendOutResponse;
            weight = Mathf.Lerp(weight, desiredWeight,
                1f - Mathf.Exp(-response * Time.deltaTime));
            if (weight < .001f || !IsConfigured)
            {
                if (desiredWeight <= 0)
                {
                    weight = 0;
                    CurrentHeadYaw = 0;
                    currentHeadPitch = 0;
                    TargetYaw = 0;
                    rearYawSign = 0;
                    lastRawYaw = lastRawPitch = 0;
                }
                return;
            }

            if (target != null)
            {
                var origin = (leftEye.position + rightEye.position) * .5f;
                var direction = target.position - origin;
                if (direction.sqrMagnitude < .0001f) return;
                var localDirection = characterRoot.InverseTransformDirection(
                    direction.normalized);
                var horizontal = Mathf.Sqrt(
                    localDirection.x * localDirection.x +
                    localDirection.z * localDirection.z);
                lastRawYaw = Mathf.Atan2(
                    localDirection.x, localDirection.z) * Mathf.Rad2Deg;
                lastRawPitch = Mathf.Atan2(
                    localDirection.y, Mathf.Max(.001f, horizontal)) * Mathf.Rad2Deg;
            }
            var rawYaw = lastRawYaw;
            var rawPitch = lastRawPitch;
            if (Mathf.Abs(rawYaw) > 90f)
            {
                if (Mathf.Approximately(rearYawSign, 0))
                    rearYawSign = Mathf.Abs(CurrentHeadYaw) > 1f
                        ? Mathf.Sign(CurrentHeadYaw)
                        : Mathf.Sign(rawYaw);
            }
            else
            {
                rearYawSign = 0;
            }
            var continuousYaw = Mathf.Approximately(rearYawSign, 0)
                ? rawYaw
                : rearYawSign * 180f;
            var desiredHeadYaw = Mathf.Clamp(
                continuousYaw, -HeadYawLimit, HeadYawLimit) * weight;
            var desiredHeadPitch = Mathf.Clamp(
                rawPitch, -HeadPitchDownLimit, HeadPitchUpLimit) * weight;
            CurrentHeadYaw = Mathf.MoveTowards(
                CurrentHeadYaw, desiredHeadYaw, HeadYawSpeed * Time.deltaTime);
            currentHeadPitch = Mathf.MoveTowards(
                currentHeadPitch, desiredHeadPitch,
                HeadPitchSpeed * Time.deltaTime);
            TargetYaw = rawYaw;

            ApplyWorldDelta(
                neck, CurrentHeadYaw * .28f, currentHeadPitch * .22f);
            ApplyWorldDelta(
                head, CurrentHeadYaw * .72f, currentHeadPitch * .78f);

            var eyeYaw = Mathf.Clamp(
                continuousYaw - CurrentHeadYaw,
                -EyeYawLimit, EyeYawLimit) * weight;
            var eyePitch = Mathf.Clamp(
                rawPitch - currentHeadPitch,
                -EyePitchLimit, EyePitchLimit) * weight;
            ApplyWorldDelta(leftEye, eyeYaw, eyePitch);
            ApplyWorldDelta(rightEye, eyeYaw, eyePitch);
        }

        private void ApplyWorldDelta(Transform bone, float yaw, float pitch)
        {
            var rotation = Quaternion.AngleAxis(yaw, characterRoot.up) *
                           Quaternion.AngleAxis(-pitch, characterRoot.right);
            bone.rotation = rotation * bone.rotation;
        }
    }

    public sealed class SocialAttentionAction : IAvatarAction, IActionRuntimeStatus
    {
        public const string Id = "social-attention";
        private const float BodyTurnEnterAngle = 40f;
        private const float BodyTurnExitAngle = 14f;
        private const float BodyTurnSpeed = 48f;
        private const float NoticeSeconds = .45f;
        private readonly AvatarMotor motor;
        private readonly AvatarSocialAttentionRig rig;
        private readonly Transform target;
        private bool cancelled;
        private bool bodyFollowing;
        private bool socialPoseEngaged;
        private float noticeElapsed;
        private ActionStopReason stopReason;

        public SocialAttentionAction(AvatarMotor motor,
            AvatarSocialAttentionRig rig, Transform target)
        {
            this.motor = motor;
            this.rig = rig;
            this.target = target;
        }

        public string PhaseId => socialPoseEngaged
            ? "attending-player" : "noticing-player";
        public string PostureId => "standing";

        public string Check()
        {
            if (motor == null || rig == null || !rig.IsConfigured)
                return "Social attention is unavailable.";
            return target == null ? "Player tracking is unavailable." : null;
        }

        public void Start()
        {
            motor.Stop();
            motor.HoldFacing();
            rig.Begin(target);
        }

        public ActionProgress Tick(float deltaSeconds)
        {
            if (cancelled) return ActionProgress.Succeeded;
            if (target == null)
                throw new InvalidOperationException("Player tracking was lost.");
            if (!socialPoseEngaged)
            {
                noticeElapsed += deltaSeconds;
                if (noticeElapsed < NoticeSeconds)
                    return ActionProgress.Running;
                socialPoseEngaged = true;
                motor.SetSocialAttention(true);
                return ActionProgress.Running;
            }
            var direction = Vector3.ProjectOnPlane(
                target.position - motor.Position, Vector3.up);
            if (direction.sqrMagnitude < .001f) return ActionProgress.Running;
            var forward = Vector3.ProjectOnPlane(
                motor.Body.Pose.rotation * Vector3.forward, Vector3.up);
            var yaw = Vector3.SignedAngle(forward, direction, Vector3.up);
            if (!bodyFollowing &&
                Mathf.Abs(rig.CurrentHeadYaw) >= BodyTurnEnterAngle)
                bodyFollowing = true;
            if (bodyFollowing && Mathf.Abs(yaw) <= BodyTurnExitAngle)
            {
                bodyFollowing = false;
                motor.HoldFacing();
            }
            else if (bodyFollowing)
            {
                motor.FaceAtSpeed(target.position, BodyTurnSpeed);
            }
            return ActionProgress.Running;
        }

        public void Cancel(ActionStopReason reason)
        {
            cancelled = true;
            stopReason = reason;
            motor.SetSocialAttention(false);
            rig.End(reason != ActionStopReason.Requested);
        }

        public void Dispose()
        {
            rig.End(stopReason != ActionStopReason.None &&
                    stopReason != ActionStopReason.Requested);
            motor.SetSocialAttention(false);
            motor.Stop();
            motor.HoldFacing();
        }
    }
}
