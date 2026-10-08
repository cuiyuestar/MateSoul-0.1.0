using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

namespace MateSoul.Runtime
{
    [RequireComponent(typeof(PhysicsBody))]
    public sealed class PhysicsGrabInteractable : XRGrabInteractable
    {
        private PhysicsBody body;
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
#region debug-point C:grip-grab-regression
#if UNITY_EDITOR
            try { using (var client = new System.Net.WebClient()) client.UploadString("http://127.0.0.1:7790/event", "{\"sessionId\":\"grip-grab-regression\",\"runId\":\"pre-fix\",\"hypothesisId\":\"C\",\"msg\":\"[DEBUG] grab attach\",\"data\":{\"body\":" + JsonUtility.ToJson(body.Pose.position) + ",\"localAttach\":" + JsonUtility.ToJson(localAttach.position) + ",\"interactorAttach\":" + JsonUtility.ToJson(interactor.GetAttachTransform(this).position) + "}}"); } catch (System.Net.WebException) { }
#endif
#endregion
            body.BeginFollow(body.Pose);
        }

        protected override void Drop() => body.EndFollow();

        // XRI owns selection/dynamic ray attachment. Only PhysicsBody writes motion.
        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase phase)
        {
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
#region debug-point C-D:grip-grab-regression
#if UNITY_EDITOR
                try { using (var client = new System.Net.WebClient()) client.UploadString("http://127.0.0.1:7790/event", "{\"sessionId\":\"grip-grab-regression\",\"runId\":\"pre-fix\",\"hypothesisId\":\"C-D\",\"msg\":\"[DEBUG] cancelled by target distance\",\"data\":{\"body\":" + JsonUtility.ToJson(body.Pose.position) + ",\"target\":" + JsonUtility.ToJson(position) + "}}"); } catch (System.Net.WebException) { }
#endif
#endregion
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
            var baseSelectable = base.IsSelectableBy(interactor);
            var selectable = playerGrabbable && !physicsBody.IsPaused &&
                             !physicsBody.IsContact && baseSelectable;
            return selectable;
        }
    }
}
