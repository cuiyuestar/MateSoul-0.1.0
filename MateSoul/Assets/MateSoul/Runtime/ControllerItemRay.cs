using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace MateSoul.Runtime
{
    public sealed class ControllerItemRay : XRRayInteractor, IHandHoldPoseProvider
    {
        public bool TryGetHandHoldPose(out Pose pose)
            => HandHoldPose.TryRead(this,
                xrController == null ? null : xrController.transform, out pose);
    }
}
