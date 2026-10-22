using System;
using MateSoul.Gameplay;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

namespace MateSoul.Runtime
{
    [RequireComponent(typeof(PhysicsBody))]
    public class PhysicsGrabInteractable : XRGrabInteractable
    {
        private PhysicsBody body;
        private GameItem item;
        private ItemReservation playerLease;
        private Pose localAttach;
        private bool playerGrabbable = true;
        [SerializeField] private bool snapToInteractor;
        public bool SnapToInteractor => snapToInteractor;
        public bool PlayerGrabbable
        {
            get => playerGrabbable;
            set
            {
                playerGrabbable = value;
                if (!value) CancelGrab();
            }
        }

        public void ConfigureSnap(bool value) => snapToInteractor = value;

        protected override void Grab()
        {
            body = GetComponent<PhysicsBody>();
            var interactor = interactorsSelecting[0];
            if (item == null) item = GetComponent<GameItem>();
            if (item != null)
            {
                playerLease = item.Registry.TryReserve(item.Instance, null, interactor,
                    Guid.NewGuid(), ItemControlState.PlayerHeld);
                if (playerLease == null)
                {
                    item.ReportUnavailable();
                    CancelGrab();
                    return;
                }
                playerLease.OnRelease(_ =>
                {
                    if (body != null && body.IsFollowing) body.EndFollow();
                });
            }
            var attach = snapToInteractor
                ? interactor.transform
                : GetAttachTransform(interactor);
            if (snapToInteractor)
            {
                localAttach = new Pose(Vector3.zero, Quaternion.identity);
                var target = new Pose(attach.position, attach.rotation);
                Physics.SyncTransforms();
                var canOccupy = body.CanOccupy(target);
                if (canOccupy) body.Place(target);
                body.BeginFollow(target);
                return;
            }
            localAttach = new Pose(transform.InverseTransformPoint(attach.position),
                Quaternion.Inverse(transform.rotation) * attach.rotation);
            body.BeginFollow(body.Pose);
        }

        protected override void Drop()
        {
            if (body != null && body.IsFollowing) body.EndFollow();
            playerLease?.Complete();
            playerLease = null;
        }

        // XRI owns selection/dynamic ray attachment. Only PhysicsBody writes motion.
        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase phase)
        {
            if (phase == XRInteractionUpdateOrder.UpdatePhase.Dynamic)
            {
                if (item == null) item = GetComponent<GameItem>();
                if (item != null && item.IsReserved)
                    for (var index = 0; index < interactorsHovering.Count; index++)
                        if (interactorsHovering[index] is IXRSelectInteractor select &&
                            select.isSelectActive)
                        {
                            item.ReportUnavailable();
                            break;
                        }
            }
            if (!isSelected || phase != XRInteractionUpdateOrder.UpdatePhase.Dynamic) return;
            var interactor = interactorsSelecting[0];
            var controller = (interactor as XRBaseControllerInteractor)?.xrController;
            if (!interactor.transform.gameObject.activeInHierarchy ||
                controller != null && controller.enableInputTracking &&
                (controller.currentControllerState.inputTrackingState &
                 (InputTrackingState.Position | InputTrackingState.Rotation)) !=
                (InputTrackingState.Position | InputTrackingState.Rotation))
            {
                CancelGrab();
                return;
            }
            var attach = snapToInteractor ? interactor.transform : interactor.GetAttachTransform(this);
            var rotation = snapToInteractor
                ? attach.rotation
                : attach.rotation * Quaternion.Inverse(localAttach.rotation);
            var position = snapToInteractor
                ? attach.position
                : attach.position - rotation * Vector3.Scale(localAttach.position, transform.lossyScale);
            if (!PhysicsBody.IsFinite(position) || Vector3.Distance(position, body.Pose.position) > 1.5f)
            {
                CancelGrab();
                return;
            }
            body.SetFollowTarget(new Pose(position, rotation));
        }

        public void CancelGrab()
        {
            while (isSelected)
                interactionManager.SelectCancel(interactorsSelecting[0], this);
        }

        public override bool IsSelectableBy(IXRSelectInteractor interactor)
        {
            var physicsBody = GetComponent<PhysicsBody>();
            var item = GetComponent<GameItem>();
            var permitted = item == null || !item.Instance.IsOccupied ||
                playerLease != null && playerLease.IsValid && ReferenceEquals(playerLease.Owner, interactor);
            var baseSelectable = base.IsSelectableBy(interactor);
            var selectable = permitted && playerGrabbable && !physicsBody.IsPaused &&
                             !physicsBody.IsContact && !physicsBody.IsInteractionControlled && baseSelectable;
            return selectable;
        }
    }
}
