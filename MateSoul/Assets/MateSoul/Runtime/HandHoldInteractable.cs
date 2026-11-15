using MateSoul.Core;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MateSoul.Runtime
{
    public sealed class HandHoldRequest
    {
        private readonly HandHoldInteractable socket;
        private readonly IXRSelectInteractor interactor;

        public AvatarHandSide Side { get; }
        internal bool ActionEnding { get; set; }

        internal HandHoldRequest(AvatarHandSide side, HandHoldInteractable socket,
            IXRSelectInteractor interactor)
        {
            Side = side;
            this.socket = socket;
            this.interactor = interactor;
        }

        public bool TryGetInteractorPose(out Pose pose)
        {
            pose = default;
            if (socket == null || interactor == null || !socket.IsHeldBy(interactor))
                return false;
            return HandHoldPose.TryGet(interactor, out pose);
        }

        public void SendHaptic(float amplitude, float duration)
        {
            if (interactor is XRBaseInputInteractor controller)
                controller.SendHapticImpulse(Mathf.Clamp01(amplitude), Mathf.Max(0, duration));
        }

        internal void EndSelection()
        {
            ActionEnding = true;
            if (socket != null) socket.EndSelection(this);
        }
    }

    [DisallowMultipleComponent, RequireComponent(typeof(SphereCollider))]
    public sealed class HandHoldInteractable : XRSimpleInteractable
    {
        public const float DefaultEntryDistance = .10f;
        // XRI re-validates IsSelectableBy every frame for a selected interactable,
        // so this is the radius at which a live hold is abandoned, not a hover
        // hysteresis. The character arm lags behind the player's palm while the
        // pair walks, and the hand-hold action owns the real release distance
        // (HandHoldTuning.maximumTargetSeparation), so the guard has to stay
        // well clear of that budget or it silently cancels the follow.
        public const float DefaultExitDistance = .70f;

        [SerializeField] private AvatarHandSide side;
        [SerializeField] private HandIKRig owner;
        [SerializeField, Min(.01f)] private float entryDistance = DefaultEntryDistance;
        [SerializeField, Min(.01f)] private float exitDistance = DefaultExitDistance;
        private HandHoldRequest request;
        private ActionHandle action;
        private bool cancelSelection;

        public AvatarHandSide Side => side;
        public float EntryDistance => entryDistance;
        public float ExitDistance => exitDistance;

        public void Configure(AvatarHandSide value, HandIKRig rig)
        {
            side = value;
            owner = rig;
            ValidateDistances();
            selectMode = InteractableSelectMode.Single;
            var shape = GetComponent<SphereCollider>();
            shape.enabled = true;
            shape.isTrigger = true;
            colliders.Clear();
            colliders.Add(shape);
        }

        private void OnValidate() => ValidateDistances();

        private void ValidateDistances()
        {
            if (!PhysicsBody.IsFinite(entryDistance) || entryDistance < .01f)
                entryDistance = DefaultEntryDistance;
            if (!PhysicsBody.IsFinite(exitDistance) || exitDistance < entryDistance)
                exitDistance = Mathf.Max(entryDistance, DefaultExitDistance);
        }

        protected override void OnEnable()
        {
            gameObject.layer = 0;
            base.OnEnable();
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            request = new HandHoldRequest(side, this, args.interactorObject);
            action = owner == null ? null : owner.RequestHold(request);
            if (action == null || action.State == ActionState.Rejected)
                cancelSelection = true;
#region debug-point C-D:handhold-selection
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            var payload = $"{{\"sessionId\":\"movement-handhold-stall\",\"runId\":\"post-fix\"," +
                $"\"hypothesisId\":\"C-D\",\"location\":\"HandHoldInteractable.OnSelectEntered\"," +
                $"\"msg\":\"[DEBUG] handhold selected\",\"data\":{{" +
                $"\"side\":\"{side}\",\"ownerAvailable\":{(owner != null && owner.IsAvailable).ToString().ToLowerInvariant()}," +
                $"\"actionState\":\"{(action != null ? action.State.ToString() : "null")}\"," +
                $"\"actionError\":\"{(action != null && action.Error != null ? action.Error.Replace("\"", "'") : "")}\"," +
                $"\"cancelSelection\":{cancelSelection.ToString().ToLowerInvariant()}," +
                $"\"interactor\":\"{args.interactorObject.transform.name.Replace("\"", "'")}\"}}}}";
            var debugRequest = UnityWebRequest.Post(
                "http://127.0.0.1:7781/event", payload, "application/json");
            debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
#endif
#endregion
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            if (request != null && !request.ActionEnding && action != null && !action.IsFinished)
                owner?.RequestRelease(action);
            request = null;
            action = null;
            cancelSelection = false;
#region debug-point D:handhold-selection-exit
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            var payload = $"{{\"sessionId\":\"movement-handhold-stall\",\"runId\":\"post-fix\"," +
                $"\"hypothesisId\":\"D\",\"location\":\"HandHoldInteractable.OnSelectExited\"," +
                $"\"msg\":\"[DEBUG] handhold selection exited\",\"data\":{{" +
                $"\"side\":\"{side}\",\"interactor\":\"{args.interactorObject.transform.name.Replace("\"", "'")}\"}}}}";
            var debugRequest = UnityWebRequest.Post(
                "http://127.0.0.1:7781/event", payload, "application/json");
            debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
#endif
#endregion
        }

        private void LateUpdate()
        {
            if (!cancelSelection || !isSelected || interactionManager == null) return;
            cancelSelection = false;
            interactionManager.SelectCancel(interactorsSelecting[0], this);
        }

        internal bool IsHeldBy(IXRSelectInteractor value)
        {
            if (!isSelected) return false;
            for (var index = 0; index < interactorsSelecting.Count; index++)
                if (ReferenceEquals(interactorsSelecting[index], value)) return true;
            return false;
        }

        internal void EndSelection(HandHoldRequest value)
        {
            if (!ReferenceEquals(request, value)) return;
            cancelSelection = true;
        }

        public bool IsWithinHoldRange(IXRSelectInteractor interactor)
        {
            if (!HandHoldPose.TryGet(interactor, out var pose)) return false;
            // XRI re-runs this every frame through IsSelectableBy while the socket is
            // selected, so the held radius is a hold-abandon distance, not hysteresis.
            var distance = IsHeldBy(interactor) ? exitDistance : entryDistance;
            return (pose.position - transform.position).sqrMagnitude <= distance * distance;
        }

        public override bool IsSelectableBy(IXRSelectInteractor interactor)
            => owner != null && owner.IsAvailable && IsWithinHoldRange(interactor) &&
               base.IsSelectableBy(interactor);
    }
}
