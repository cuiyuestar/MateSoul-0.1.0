using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    public static class InteractionPhysicsLayers
    {
        public const string DirectHandName = "DirectHand";
        public const string AvatarDetailName = "AvatarDetail";
        public const string RealitySurfaceName = "RealitySurface";
        public const string AvatarLocomotionName = "AvatarLocomotion";

        private static bool configured;

        public static int DirectHand => RequiredLayer(DirectHandName);
        public static int AvatarDetail => RequiredLayer(AvatarDetailName);
        public static int RealitySurface => RequiredLayer(RealitySurfaceName);
        public static int AvatarLocomotion => RequiredLayer(AvatarLocomotionName);

        public static int DirectSelectionMask
        {
            get
            {
                var hand = LayerMask.NameToLayer(DirectHandName);
                var locomotion = LayerMask.NameToLayer(AvatarLocomotionName);
                var mask = Physics.DefaultRaycastLayers;
                if (hand >= 0) mask &= ~(1 << hand);
                if (locomotion >= 0) mask &= ~(1 << locomotion);
                return mask;
            }
        }

        public static void Configure()
        {
            if (configured) return;
            var hand = DirectHand;
            var avatar = AvatarDetail;
            var reality = RealitySurface;
            var locomotion = AvatarLocomotion;
            Physics.IgnoreLayerCollision(hand, hand, true);
            Physics.IgnoreLayerCollision(hand, reality, true);
            Physics.IgnoreLayerCollision(hand, locomotion, true);
            Physics.IgnoreLayerCollision(avatar, reality, true);
            Physics.IgnoreLayerCollision(locomotion, 0, true);
            configured = true;
        }

        private static int RequiredLayer(string name)
        {
            var layer = LayerMask.NameToLayer(name);
            if (layer < 0)
                throw new InvalidOperationException(
                    $"Required physics layer is missing: {name}.");
            return layer;
        }
    }
}
