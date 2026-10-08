using MateSoul.Core;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

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
            if (interactor is XRBaseControllerInteractor controller)
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
        public const float DefaultExitDistance = .16f;

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
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            if (request != null && !request.ActionEnding && action != null && !action.IsFinished)
                owner?.RequestRelease(action);
            request = null;
            action = null;
            cancelSelection = false;
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
            var distance = IsHeldBy(interactor) ? exitDistance : entryDistance;
            return (pose.position - transform.position).sqrMagnitude <= distance * distance;
        }

        public override bool IsSelectableBy(IXRSelectInteractor interactor)
            => owner != null && owner.IsAvailable && IsWithinHoldRange(interactor) &&
               base.IsSelectableBy(interactor);
    }
}
