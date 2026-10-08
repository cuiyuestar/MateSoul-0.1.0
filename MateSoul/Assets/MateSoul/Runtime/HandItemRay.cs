using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

namespace MateSoul.Runtime
{
    // Selection ray can come from the eyes; the attachment always belongs to the hand.
    public sealed class HandItemRay : XRRayInteractor, IHandHoldPoseProvider
    {
        [SerializeField] private PlayerInteractionModeController mode;
        [SerializeField] private XRRayInteractor gaze;
        [SerializeField] private Transform handAttach;
        public void Configure(PlayerInteractionModeController modes, XRRayInteractor eyeRay,
            Transform attachment)
        { mode = modes; gaze = eyeRay; handAttach = attachment; }

        public override Transform GetAttachTransform(IXRInteractable interactable)
            => handAttach == null ? base.GetAttachTransform(interactable) : handAttach;

        public bool TryGetHandHoldPose(out Pose pose)
            => HandHoldPose.TryRead(this, handAttach, out pose);

        public override void GetValidTargets(List<IXRInteractable> targets)
        {
            targets.Clear();
            if (mode == null || mode.PlacementActive || xrController == null ||
                xrController.currentControllerState == null) return;
            var tracking = xrController.currentControllerState.inputTrackingState;
            if ((tracking & InputTrackingState.Position) == 0 ||
                (tracking & InputTrackingState.Rotation) == 0) return;
            if (mode.CurrentMode == PlayerInteractionMode.EyeHand)
            {
                if (gaze == null || !gaze.gameObject.activeInHierarchy ||
                    (gaze.xrController.currentControllerState.inputTrackingState &
                     InputTrackingState.Rotation) == 0) return;
                gaze.GetValidTargets(targets);
            }
            else base.GetValidTargets(targets);
        }

        public override void PreprocessInteractor(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            enableUIInteraction = mode != null && mode.CurrentMode == PlayerInteractionMode.Gesture;
            base.PreprocessInteractor(updatePhase);
        }
    }
}
