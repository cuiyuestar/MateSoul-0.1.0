using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    [CreateAssetMenu(menuName = "MateSoul/Bowling motion")]
    public sealed class BowlingMotion : ScriptableObject
    {
        public AnimationClip clip;
        public int pickupFrame = 20;
        public int fallbackFrame = 21;
        public int releaseFrame = 90;
        public Vector3 ballInHand;
        public Vector3 ballEulerInHand;
        public Vector3 pickupCenter;
        public float PickupTime => pickupFrame / clip.frameRate;
        public float FallbackTime => fallbackFrame / clip.frameRate;
        public float ReleaseTime => releaseFrame / clip.frameRate;

        public void Validate()
        {
            if (!clip || clip.frameRate <= 0 || pickupFrame < 1 ||
                fallbackFrame < pickupFrame || releaseFrame <= fallbackFrame ||
                ReleaseTime >= clip.length || !PhysicsBody.IsFinite(ballInHand) ||
                !PhysicsBody.IsFinite(ballEulerInHand) || !PhysicsBody.IsFinite(pickupCenter))
                throw new InvalidOperationException("保龄球动画或触球/出手帧配置不完整。");
        }
    }
}
