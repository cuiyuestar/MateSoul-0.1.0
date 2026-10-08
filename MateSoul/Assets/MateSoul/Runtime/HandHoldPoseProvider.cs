using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

namespace MateSoul.Runtime
{
    public interface IHandHoldPoseProvider
    {
        bool TryGetHandHoldPose(out Pose pose);
    }

    internal static class HandHoldPose
    {
        internal static bool TryGet(IXRSelectInteractor interactor, out Pose pose)
        {
            pose = default;
            if (interactor == null || interactor.transform == null ||
                !interactor.transform.gameObject.activeInHierarchy)
                return false;
            if (interactor is IHandHoldPoseProvider provider)
                return provider.TryGetHandHoldPose(out pose);
            if (interactor is XRBaseControllerInteractor controller)
                return TryRead(controller, controller.xrController == null
                    ? null : controller.xrController.transform, out pose);
            return false;
        }

        internal static bool TryRead(XRBaseControllerInteractor interactor,
            Transform source, out Pose pose)
        {
            pose = default;
            if (interactor == null || source == null || !source.gameObject.activeInHierarchy)
                return false;
            var controller = interactor.xrController;
            if (controller == null) return false;
            if (controller.enableInputTracking)
            {
                var required = InputTrackingState.Position | InputTrackingState.Rotation;
                if ((controller.currentControllerState.inputTrackingState & required) != required)
                    return false;
            }

            var position = source.position;
            var rotation = source.rotation;
            var rotationLength = rotation.x * rotation.x + rotation.y * rotation.y +
                                 rotation.z * rotation.z + rotation.w * rotation.w;
            if (!PhysicsBody.IsFinite(position) ||
                !PhysicsBody.IsFinite(rotation.x) || !PhysicsBody.IsFinite(rotation.y) ||
                !PhysicsBody.IsFinite(rotation.z) || !PhysicsBody.IsFinite(rotation.w) ||
                !PhysicsBody.IsFinite(rotationLength) || rotationLength < .001f)
                return false;
            pose = new Pose(position, rotation);
            return true;
        }
    }

}
