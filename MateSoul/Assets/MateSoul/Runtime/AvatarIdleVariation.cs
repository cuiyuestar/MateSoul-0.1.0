using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent]
    public sealed class AvatarIdleVariation : MonoBehaviour
    {
        private const float MaximumIdleSpeed = .05f;
        private const int VariantCount = 3;
        private static readonly int LocomotionState =
            Animator.StringToHash("Locomotion");
        private static readonly int Idle02State =
            Animator.StringToHash("Idle02");
        private static readonly int Idle03State =
            Animator.StringToHash("Idle03");

        [SerializeField, Min(1)] private int minimumLoops = 1;
        [SerializeField, Min(1)] private int maximumLoops = 2;

        private Animator animator;
        private AvatarMotor motor;
        private System.Random random;
        private int speedParameter;
        private int socialAttentionParameter;
        private int variantParameter;
        private float remainingSeconds;
        private bool scheduled;

        public int CurrentVariant { get; private set; }

        public static AvatarIdleVariation Attach(
            GameObject character, Animator characterAnimator)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            var variation = character.GetComponent<AvatarIdleVariation>();
            if (variation == null)
                variation = character.AddComponent<AvatarIdleVariation>();
            variation.Bind(characterAnimator);
            return variation;
        }

        public void Bind(Animator characterAnimator, int seed = 0)
        {
            animator = characterAnimator != null
                ? characterAnimator
                : throw new ArgumentNullException(nameof(characterAnimator));
            motor = GetComponent<AvatarMotor>();
            speedParameter = Animator.StringToHash("Speed");
            socialAttentionParameter = Animator.StringToHash("SocialAttention");
            variantParameter = Animator.StringToHash("IdleVariant");
            RequireParameter(speedParameter, AnimatorControllerParameterType.Float);
            RequireParameter(
                socialAttentionParameter, AnimatorControllerParameterType.Bool);
            RequireParameter(variantParameter, AnimatorControllerParameterType.Int);

            minimumLoops = Mathf.Max(1, minimumLoops);
            maximumLoops = Mathf.Max(minimumLoops, maximumLoops);
            random = new System.Random(seed != 0
                ? seed
                : unchecked(gameObject.GetInstanceID() * 397 ^ Environment.TickCount));
            CurrentVariant = 0;
            animator.SetInteger(variantParameter, CurrentVariant);
            remainingSeconds = 0;
            scheduled = false;
        }

        private void Update()
        {
            if (animator != null) Tick(Time.unscaledDeltaTime);
        }

        public void Tick(float deltaSeconds)
        {
            if (!CanRotate())
                return;
            if (!scheduled)
            {
                remainingSeconds = CurrentClipLength() *
                                   random.Next(minimumLoops, maximumLoops + 1);
                scheduled = true;
                return;
            }

            remainingSeconds -= Mathf.Max(0, deltaSeconds);
            if (remainingSeconds > 0) return;

            int candidate = random.Next(VariantCount - 1);
            if (candidate >= CurrentVariant) candidate++;
            CurrentVariant = candidate;
            animator.SetInteger(variantParameter, CurrentVariant);
            scheduled = false;
        }

        private bool CanRotate()
        {
            if (!animator.isActiveAndEnabled || animator.IsInTransition(0))
                return false;
            if (animator.GetFloat(speedParameter) > MaximumIdleSpeed ||
                animator.GetBool(socialAttentionParameter))
                return false;
            if (motor != null &&
                (motor.ContactPosture != null || motor.IsTurnStepActive))
                return false;
            for (int layer = 1; layer < animator.layerCount; layer++)
                if (animator.GetLayerWeight(layer) > .01f)
                    return false;

            int state = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            return state == LocomotionState ||
                   state == Idle02State ||
                   state == Idle03State;
        }

        private float CurrentClipLength()
        {
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            float bestWeight = -1;
            float length = 4;
            foreach (var clip in clips)
            {
                if (clip.clip == null || clip.weight <= bestWeight) continue;
                bestWeight = clip.weight;
                length = Mathf.Max(.1f, clip.clip.length);
            }
            return length;
        }

        private void RequireParameter(
            int hash, AnimatorControllerParameterType expectedType)
        {
            foreach (var parameter in animator.parameters)
                if (parameter.nameHash == hash && parameter.type == expectedType)
                    return;
            throw new InvalidOperationException(
                $"Animator parameter {hash} must exist as {expectedType}.");
        }
    }
}
