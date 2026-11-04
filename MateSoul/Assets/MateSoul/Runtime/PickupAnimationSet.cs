using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    [Serializable]
    public sealed class PickupAnimation
    {
        public AnimationClip clip;
        public float contactTime;
        public Vector3 contactGrip;
        public float Duration => clip.length;
        public string StateName => clip.name;
    }

    public sealed class PickupPerformance
    {
        public readonly float Duration;
        public float contactTime => Duration * .5f;
        public readonly Vector3 contactGrip;

        public PickupPerformance(PickupAnimation lower, PickupAnimation upper, float blend)
        {
            Duration = Mathf.Lerp(lower.Duration, upper.Duration, blend);
            contactGrip = Vector3.Lerp(lower.contactGrip, upper.contactGrip, blend);
        }
    }

    [CreateAssetMenu(menuName = "MateSoul/Pickup Animations")]
    public sealed class PickupAnimationSet : ScriptableObject
    {
        public const string LayerName = "Pickup";
        public const string StateName = "PickupHeightBlend";
        public const string HeightParameter = "PickupHeight";
        public const float MaximumCorrection = .15f;
        public const float ContactTolerance = .025f;
        public const float HeightTolerance = .35f;
        public PickupAnimation[] animations = Array.Empty<PickupAnimation>();

        public PickupPerformance Select(float height, float characterScale)
        {
            if (animations.Length == 0 ||
                height < animations[0].contactGrip.y * characterScale - HeightTolerance ||
                height > animations[animations.Length - 1].contactGrip.y * characterScale + HeightTolerance)
                throw new InvalidOperationException(
                    "这个高度暂时没有合适的拾取动作，请调整物品的放置高度。");
            var localHeight = height / characterScale;
            for (var index = 1; index < animations.Length; index++)
            {
                var lower = animations[index - 1];
                var upper = animations[index];
                if (localHeight > upper.contactGrip.y) continue;
                return new PickupPerformance(lower, upper,
                    Mathf.InverseLerp(lower.contactGrip.y, upper.contactGrip.y, localHeight));
            }
            var last = animations[animations.Length - 1];
            return new PickupPerformance(last, last, 0);
        }
    }
}
