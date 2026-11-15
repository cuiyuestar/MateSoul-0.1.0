using System;
using MateSoul.Gameplay;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

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
        private bool activeSnap;
        private ItemGripAnchor stableGrip;
        private PicoDirectHandInteractor directHand;
        private IXRSelectInteractor drivingHand;
        [SerializeField] private bool snapToInteractor;
        public bool SnapToInteractor => snapToInteractor;
        public IXRSelectInteractor DrivingHand => drivingHand;
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
            drivingHand = interactor;
            directHand = interactor as PicoDirectHandInteractor;
            activeSnap = snapToInteractor && directHand == null;
            if (item == null) item = GetComponent<GameItem>();
            if (item != null)
            {
                playerLease = item.Registry.TryReserve(item.Instance, null, this,
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
                    foreach (var selected in interactorsSelecting)
                        (selected as PicoDirectHandInteractor)?.SetHeldBody(null);
                });
            }
            if (directHand != null)
                directHand.AttachToSurface(body, item == null ? null : item.Definition.Interaction);
            stableGrip = directHand != null ? directHand.StableGrip : null;
            var attach = directHand != null
                ? interactor.GetAttachTransform(this)
                : activeSnap
                ? interactor.transform
                : GetAttachTransform(interactor);
            if (activeSnap)
            {
                localAttach = new Pose(Vector3.zero, Quaternion.identity);
                var target = new Pose(attach.position, attach.rotation);
                Physics.SyncTransforms();
                var canOccupy = body.CanOccupy(target);
                if (canOccupy) body.Place(target);
                body.BeginFollow(target);
                return;
            }
            if (directHand != null)
            {
                directHand.SetHeldBody(body);
                // Snap after the hand has been told to ignore this body, otherwise
                // the hand's own colliders veto the destination.
                directHand.ApplyPendingSnap(body);
            }
            localAttach = stableGrip != null ? stableGrip.Profile.Grip :
                new Pose(transform.InverseTransformPoint(attach.position),
                    Quaternion.Inverse(transform.rotation) * attach.rotation);
#region debug-point C:grip-grab-regression
#if UNITY_EDITOR
            try { using (var client = new System.Net.WebClient()) client.UploadString("http://127.0.0.1:7790/event", "{\"sessionId\":\"grip-grab-regression\",\"runId\":\"pre-fix\",\"hypothesisId\":\"C\",\"msg\":\"[DEBUG] grab attach\",\"data\":{\"body\":" + JsonUtility.ToJson(body.Pose.position) + ",\"localAttach\":" + JsonUtility.ToJson(localAttach.position) + ",\"interactorAttach\":" + JsonUtility.ToJson(interactor.GetAttachTransform(this).position) + "}}"); } catch (System.Net.WebException) { }
#endif
#endregion
            // A direct hand holds the body rigidly: it is placed on the hand
            // target every frame instead of being pulled by a spring. That removes
            // the hand/item chase and the perceived delay between opening the hand
            // and the item falling.
            var rigid = directHand != null || stableGrip != null;
            body.BeginFollow(body.Pose, rigid,
                stableGrip != null && stableGrip.Profile.capability == "cutting-tool");
            Debug.Log($"MATESOUL_GRAB_HOLD item={name} rigid={rigid} " +
                      $"anchor={stableGrip != null} " +
                      $"hand={(directHand == null ? "none" : directHand.name)}");
        }

        protected override void Drop()
        {
            if (body != null && body.IsFollowing)
            {
                // Selection exits before this frame's held-body update. Read the
                // release packet directly instead of averaging old body motion.
                // A tracking hiccup on the release frame falls back to the last
                // trustworthy sample so a thrown item keeps its momentum.
                if (directHand != null && directHand.IntentionalRelease &&
                    directHand.TryGetReleaseMotion(body.Rigidbody.worldCenterOfMass,
                        out var velocity, out var angular))
                {
                    body.EndHandFollow(velocity, angular);
                    Debug.Log($"MATESOUL_GRAB_THROW item={name} speed={velocity.magnitude:F2} " +
                              $"angular={angular.magnitude:F2} {ReleasePose()}");
                }
                else
                {
                    body.EndFollow(true);
                    Debug.Log($"MATESOUL_GRAB_THROW item={name} speed=0 reason=" +
                              (directHand == null ? "no-hand" : directHand.IntentionalRelease
                                  ? "no-motion" : "not-intentional") +
                              " " + ReleasePose());
                }
            }
            directHand?.SetHeldBody(null);
            playerLease?.Complete();
            playerLease = null;
            directHand = null;
            drivingHand = null;
            stableGrip = null;
        }

        private string ReleasePose()
        {
            var pose = body.Pose;
            var up = pose.rotation * Vector3.up;
            return $"pos=({pose.position.x:F2},{pose.position.y:F2},{pose.position.z:F2}) " +
                   $"up=({up.x:F2},{up.y:F2},{up.z:F2})";
        }

        protected override void OnSelectEntering(SelectEnterEventArgs args)
        {
            base.OnSelectEntering(args);
            if (interactorsSelecting.Count > 1 && args.interactorObject is PicoDirectHandInteractor incoming)
            {
                incoming.AttachToSurface(body, item?.Definition.Interaction);
                incoming.SetHeldBody(body);
            }
        }

        protected override void OnSelectExiting(SelectExitEventArgs args)
        {
            var driverLeaving = ReferenceEquals(drivingHand, args.interactorObject);
            base.OnSelectExiting(args);
            (args.interactorObject as PicoDirectHandInteractor)?.SetHeldBody(null);
            if (!driverLeaving || interactorsSelecting.Count == 0) return;
            drivingHand = interactorsSelecting[0];
            directHand = drivingHand as PicoDirectHandInteractor;
            stableGrip = directHand?.StableGrip;
            activeSnap = false;
            var attach = drivingHand.GetAttachTransform(this);
            // Transfer the driver's frame without dropping, restarting physics,
            // releasing the item reservation, or snapping to a new world pose.
            localAttach = new Pose(transform.InverseTransformPoint(attach.position),
                Quaternion.Inverse(transform.rotation) * attach.rotation);
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
                            select.isSelectActive && !IsSelectableBy(select))
                        {
                            item.ReportUnavailable();
                            break;
                        }
            }
            if (!isSelected || phase != XRInteractionUpdateOrder.UpdatePhase.Dynamic) return;
            var interactor = drivingHand;
            if (interactor == null) return;
            if (directHand != null && directHand.IsTrackingSuspended)
            {
                body.HoldFollowPose();
                return;
            }
            var controller = (interactor as XRBaseInputInteractor)?.xrController;
            if (!interactor.transform.gameObject.activeInHierarchy ||
                controller != null && controller.enableInputTracking &&
                (controller.currentControllerState.inputTrackingState &
                 (InputTrackingState.Position | InputTrackingState.Rotation)) !=
                (InputTrackingState.Position | InputTrackingState.Rotation))
            {
                CancelGrab();
                return;
            }
            var attach = activeSnap ? interactor.transform : interactor.GetAttachTransform(this);
            var rotation = activeSnap
                ? attach.rotation
                : attach.rotation * Quaternion.Inverse(localAttach.rotation);
            var position = activeSnap
                ? attach.position
                : attach.position - rotation * Vector3.Scale(localAttach.position, transform.lossyScale);
            if (!PhysicsBody.IsFinite(position) || Vector3.Distance(position, body.Pose.position) > 1.5f)
            {
#region debug-point C-D:grip-grab-regression
#if UNITY_EDITOR
                try { using (var client = new System.Net.WebClient()) client.UploadString("http://127.0.0.1:7790/event", "{\"sessionId\":\"grip-grab-regression\",\"runId\":\"pre-fix\",\"hypothesisId\":\"C-D\",\"msg\":\"[DEBUG] cancelled by target distance\",\"data\":{\"body\":" + JsonUtility.ToJson(body.Pose.position) + ",\"target\":" + JsonUtility.ToJson(position) + "}}"); } catch (System.Net.WebException) { }
#endif
#endregion
                CancelGrab();
                return;
            }
            var target = new Pose(position, rotation);
            if (directHand == null) body.SetFollowTarget(target);
            else
            {
                directHand.GetFollowMotion(position, out var velocity, out var angular);
                body.SetFollowTarget(target, velocity, angular);
            }
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
                playerLease != null && playerLease.IsValid &&
                ReferenceEquals(playerLease.Owner, this) &&
                (interactorsSelecting.Contains(interactor) ||
                 interactor is PicoDirectHandInteractor && directHand != null &&
                 interactorsSelecting.Count < 2);
            var baseSelectable = base.IsSelectableBy(interactor);
            var selectable = permitted && playerGrabbable && !physicsBody.IsPaused &&
                             !physicsBody.IsContact && !physicsBody.IsInteractionControlled && baseSelectable;
            return selectable;
        }
    }
}
