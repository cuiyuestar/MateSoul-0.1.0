using System;
using MateSoul.Core;
using MateSoul.Gameplay;
using MateSoul.Wardrobe;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed class MoveAction : IAvatarAction, IActionRuntimeStatus
    {
        private readonly IAvatarMotor motor;
        private readonly INavigationService navigation;
        private readonly Func<Vector3> destination;
        private Vector3[] path;
        private bool started, cancelled;
        private float repathSeconds;
        public MoveAction(IAvatarMotor motor, INavigationService navigation, Func<Vector3> destination)
        {
            this.motor = motor; this.navigation = navigation; this.destination = destination;
        }
        public string PhaseId => "moving";
        public string PostureId => "standing";
        public string Check()
            => navigation.TryPath(motor.Position, destination(), out path) ? null : "No complete route to the target.";
        public void Start() { started = true; repathSeconds = 1; motor.Begin(path); }
        public ActionProgress Tick(float dt)
        {
            if (cancelled) return ActionProgress.Succeeded;
            repathSeconds -= dt;
            if (repathSeconds <= 0)
            {
                if (!navigation.TryPath(motor.Position, destination(), out path))
                    throw new InvalidOperationException("The route is no longer reachable.");
                motor.Begin(path);
                repathSeconds = 1;
            }
            return motor.Tick(dt) ? ActionProgress.Succeeded : ActionProgress.Running;
        }
        public void Cancel(ActionStopReason reason) { cancelled = true; }
        public void Dispose() { if (started) motor.Stop(); }
    }

    public sealed class StayAction : IAvatarAction, IActionRuntimeStatus
    {
        private readonly IAvatarMotor motor;
        private float remaining;
        private bool started;
        public StayAction(IAvatarMotor motor, float seconds = 3f) { this.motor = motor; remaining = seconds; }
        public string PhaseId => "staying";
        public string PostureId => "standing";
        public string Check() => null;
        public void Start()
        {
            started = true;
            if (motor is AvatarMotor avatar) avatar.EndBodyContact();
            motor.Stop();
        }
        public ActionProgress Tick(float dt)
        {
            remaining -= dt;
            return remaining <= 0 ? ActionProgress.Succeeded : ActionProgress.Running;
        }
        public void Cancel(ActionStopReason reason) { remaining = 0; }
        public void Dispose() { if (started) motor.Stop(); }
    }

    public sealed class VisitItemAction : IAvatarAction, IActionRuntimeStatus
    {
        private readonly ItemRegistry items;
        private readonly string id;
        private readonly Func<GameItem> resolve;
        private readonly IAvatarMotor motor;
        private readonly INavigationService navigation;
        private MoveAction movement;
        private ItemReservation reservation;
        private GameItem target;
        private float lookSeconds = 2f;
        private bool cancelled, arrived;

        public VisitItemAction(ItemRegistry items, string id, Func<GameItem> resolve,
            IAvatarMotor motor, INavigationService navigation)
        {
            this.items = items; this.id = id; this.resolve = resolve;
            this.motor = motor; this.navigation = navigation;
        }
        public string PhaseId => arrived ? "observing" : "approaching";
        public string PostureId => "standing";
        public string Check()
        {
            target = resolve();
            if (target == null || !items.TryGet(id, out var item) || item.IsOccupied || !item.Supports("visit"))
                return "Item is unavailable.";
            movement = new MoveAction(motor, navigation, () => target.ApproachPosition);
            return movement.Check();
        }
        public void Start()
        {
            reservation = items.Reserve(id, "visit", this);
            if (reservation == null) throw new InvalidOperationException("Item is occupied.");
            movement.Start();
        }
        public ActionProgress Tick(float dt)
        {
            if (cancelled) return ActionProgress.Succeeded;
            if (!reservation.IsValid || target == null) throw new InvalidOperationException("Item was removed.");
            if (!arrived)
            {
                arrived = movement.Tick(dt) == ActionProgress.Succeeded;
                if (!arrived) return ActionProgress.Running;
            }
            motor.Face(target.transform.position, dt);
            lookSeconds -= dt;
            return lookSeconds <= 0 ? ActionProgress.Succeeded : ActionProgress.Running;
        }
        public void Cancel(ActionStopReason reason) { cancelled = true; }
        public void Dispose()
        {
            try { movement?.Dispose(); }
            finally { reservation?.Dispose(); }
        }
    }

    public sealed class ChangeOutfitAction : IAvatarAction, IActionRuntimeStatus
    {
        private readonly LengyueWardrobeController wardrobe;
        private readonly string outfit;
        public ChangeOutfitAction(LengyueWardrobeController wardrobe, string outfit)
        {
            this.wardrobe = wardrobe; this.outfit = outfit;
        }
        public string PhaseId => "switching";
        public string PostureId => null;
        public string Check() => wardrobe == null || string.IsNullOrWhiteSpace(outfit) ? "Outfit is unavailable." : null;
        public void Start()
        {
            if (!wardrobe.TrySwitchOutfit(outfit, out var error)) throw new InvalidOperationException(error);
        }
        public ActionProgress Tick(float dt) => ActionProgress.Succeeded;
        public void Cancel(ActionStopReason reason) { }
        public void Dispose() { }
    }
}
