using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    // Authored grip geometry, shared by tools and vessels. Selection and motion
    // remain owned by XRI and PhysicsBody respectively.
    [DisallowMultipleComponent]
    public sealed class ItemGripAnchor : MonoBehaviour
    {
        [SerializeField] private ItemInteractionProfile profile;
        [SerializeField, Min(.01f)] private float acquisitionAllowance = .025f;
        public ItemInteractionProfile Profile => profile;

        public static bool Supports(ItemInteractionProfile value)
            => value != null && (value.gripKind == ItemGripKind.Handle ||
                                 value.gripKind == ItemGripKind.LoopHandle);

        public void Configure(ItemInteractionProfile value)
        {
            if (!Supports(value)) throw new ArgumentException("A stable grip requires a handle profile.");
            value.ValidateGrip();
            profile = value;
        }

        public float Distance(Vector3 worldPoint)
        {
            var local = Quaternion.Inverse(profile.Grip.rotation) *
                        (transform.InverseTransformPoint(worldPoint) - profile.gripPosition);
            var grip = new Bounds(profile.gripShapeOffset, profile.gripSize);
            return Vector3.Scale(local - grip.ClosestPoint(local), transform.lossyScale).magnitude;
        }

        public bool IsInRange(Vector3 worldPoint)
            => isActiveAndEnabled && Supports(profile) &&
               Distance(worldPoint) <= acquisitionAllowance;

        public Pose ItemPose(Pose handGrip)
        {
            var rotation = handGrip.rotation * Quaternion.Inverse(profile.Grip.rotation);
            return new Pose(handGrip.position -
                rotation * Vector3.Scale(profile.gripPosition, transform.lossyScale), rotation);
        }
    }
}
