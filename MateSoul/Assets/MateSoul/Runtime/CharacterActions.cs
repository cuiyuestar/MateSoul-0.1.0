using System;
using MateSoul.Core;
using MateSoul.Gameplay;
using MateSoul.Wardrobe;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed class MoveAction : IAvatarAction, IActionRuntimeStatus
    {
        private const float MinimumProgressDistance = .015f;
        private const float BlockedDuration = 12f;
        private readonly IAvatarMotor motor;
        private readonly INavigationService navigation;
        private readonly Func<Vector3> destination;
        private readonly float arrivalDistance;
        private Vector3[] path;
        private bool started, cancelled;
        private float blockedSeconds;
        private float bestRemainingDistance;
        private Vector3 progressDestination;
#region debug-point A-B-D:movement-progress
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private float debugNextMovementReport;
        private float debugNextBedStallProgressReport;
#endif
#endregion
        public MoveAction(IAvatarMotor motor, INavigationService navigation, Func<Vector3> destination,
            float arrivalDistance = 0)
        {
            this.motor = motor; this.navigation = navigation; this.destination = destination;
            this.arrivalDistance = arrivalDistance;
        }
        public string PhaseId => "moving";
        public string PostureId => "standing";
        public string Check()
            => navigation.TryPath(motor.Position, destination(), out path) ? null : "No complete route to the target.";
        public void Start()
        {
            started = true;
            blockedSeconds = 0;
            progressDestination = destination();
            bestRemainingDistance = Vector3.ProjectOnPlane(
                progressDestination - motor.Position, Vector3.up).magnitude;
#region debug-point A-D:initial-path
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var debugCorners = path == null ? "" :
                string.Join(",", Array.ConvertAll(path, value => JsonUtility.ToJson(value)));
            var debugPayload = $"{{\"sessionId\":\"movement-blocked\",\"runId\":\"post-fix-2\"," +
                $"\"hypothesisId\":\"A-D\",\"location\":\"MoveAction.Start\"," +
                $"\"msg\":\"[DEBUG] initial action path\",\"data\":{{\"origin\":" +
                $"{JsonUtility.ToJson(motor.Position)},\"destination\":" +
                $"{JsonUtility.ToJson(destination())},\"corners\":[{debugCorners}]}}}}";
#if UNITY_EDITOR
            const string debugEndpoint = "http://127.0.0.1:7794/event";
#else
            const string debugEndpoint = "http://127.0.0.1:7794/event";
#endif
            var debugRequest = UnityEngine.Networking.UnityWebRequest.Post(
                debugEndpoint, debugPayload, "application/json");
            debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
#endif
#endregion
            motor.Begin(path);
        }
        public ActionProgress Tick(float dt)
        {
            if (cancelled) return ActionProgress.Succeeded;
            var currentDestination = destination();
            if (Vector3.ProjectOnPlane(
                    currentDestination - progressDestination, Vector3.up).sqrMagnitude > .01f)
            {
                if (!navigation.TryPath(motor.Position, currentDestination, out path))
                    throw new InvalidOperationException("The route is no longer reachable.");
#region debug-point A-D:repath
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                var debugCorners = path == null ? "" :
                    string.Join(",", Array.ConvertAll(path, value => JsonUtility.ToJson(value)));
                var debugPayload = $"{{\"sessionId\":\"movement-blocked\",\"runId\":\"post-fix-2\"," +
                    $"\"hypothesisId\":\"A-D\",\"location\":\"MoveAction.Tick.repath\"," +
                    $"\"msg\":\"[DEBUG] moved destination repath\",\"data\":{{\"origin\":" +
                    $"{JsonUtility.ToJson(motor.Position)},\"destination\":" +
                    $"{JsonUtility.ToJson(currentDestination)},\"corners\":[{debugCorners}]}}}}";
#if UNITY_EDITOR
                const string debugEndpoint = "http://127.0.0.1:7794/event";
#else
                const string debugEndpoint = "http://127.0.0.1:7794/event";
#endif
                var debugRequest = UnityEngine.Networking.UnityWebRequest.Post(
                    debugEndpoint, debugPayload, "application/json");
                debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
#endif
#endregion
                motor.Begin(path);
                progressDestination = currentDestination;
                bestRemainingDistance = Vector3.ProjectOnPlane(
                    currentDestination - motor.Position, Vector3.up).magnitude;
                blockedSeconds = 0;
            }
            var remainingDistance = Vector3.ProjectOnPlane(
                currentDestination - motor.Position, Vector3.up).magnitude;
            if (arrivalDistance > 0 && remainingDistance <= arrivalDistance)
            {
                motor.Stop();
                return ActionProgress.Succeeded;
            }
            if (motor.Tick(dt)) return ActionProgress.Succeeded;

            var progress = bestRemainingDistance - remainingDistance;
#region debug-point D-E:bed-stall-progress
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Time.unscaledTime >= debugNextBedStallProgressReport)
            {
                debugNextBedStallProgressReport = Time.unscaledTime + .25f;
                var debugMotor = motor as AvatarMotor;
                var debugVelocity = debugMotor != null
                    ? debugMotor.Body.Rigidbody.velocity : Vector3.zero;
                var debugPayload =
                    $"{{\"sessionId\":\"avatar-bed-corner-stall\",\"runId\":\"post-fix\"," +
                    $"\"hypothesisId\":\"D-E\",\"location\":\"MoveAction.Tick\"," +
                    $"\"msg\":\"[DEBUG] movement progress sample\",\"data\":{{" +
                    $"\"position\":{JsonUtility.ToJson(motor.Position)}," +
                    $"\"destination\":{JsonUtility.ToJson(currentDestination)}," +
                    $"\"velocity\":{JsonUtility.ToJson(debugVelocity)}," +
                    $"\"remainingDistance\":{remainingDistance}," +
                    $"\"bestRemainingDistance\":{bestRemainingDistance}," +
                    $"\"progress\":{progress},\"blockedSeconds\":{blockedSeconds}," +
                    $"\"physicalAvoidanceFailed\":{(debugMotor != null && debugMotor.PhysicalAvoidanceFailed).ToString().ToLowerInvariant()}}}}}";
#if UNITY_EDITOR
                const string debugEndpoint = "http://127.0.0.1:7778/event";
#else
                const string debugEndpoint = "http://127.0.0.1:7778/event";
#endif
                var debugRequest = UnityEngine.Networking.UnityWebRequest.Post(
                    debugEndpoint, debugPayload, "application/json");
                debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
            }
#endif
#endregion
#region debug-point B-D:stall-window
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Time.unscaledTime >= debugNextMovementReport)
            {
                debugNextMovementReport = Time.unscaledTime + .5f;
                var avatar = motor as AvatarMotor;
                var velocity = avatar != null
                    ? avatar.Body.Rigidbody.velocity : Vector3.zero;
                var debugPayload =
                    $"{{\"sessionId\":\"movement-blocked\",\"runId\":\"post-fix-2\"," +
                    $"\"hypothesisId\":\"B-D\",\"location\":\"MoveAction.Tick\"," +
                    $"\"msg\":\"[DEBUG] movement progress window\",\"data\":{{\"dt\":{dt}," +
                    $"\"progress\":{progress},\"blockedSeconds\":{blockedSeconds}," +
                    $"\"position\":{JsonUtility.ToJson(motor.Position)}," +
                    $"\"destination\":{JsonUtility.ToJson(destination())}," +
                    $"\"velocity\":{JsonUtility.ToJson(velocity)}}}}}";
#if UNITY_EDITOR
                const string debugEndpoint = "http://127.0.0.1:7794/event";
#else
                const string debugEndpoint = "http://127.0.0.1:7794/event";
#endif
                var debugRequest = UnityEngine.Networking.UnityWebRequest.Post(
                    debugEndpoint, debugPayload, "application/json");
                debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
            }
#endif
#endregion
            if (progress >= MinimumProgressDistance)
            {
                bestRemainingDistance = remainingDistance;
                blockedSeconds = 0;
            }
            else
            {
                blockedSeconds += dt;
            }
            if (motor is AvatarMotor physicalMotor &&
                physicalMotor.PhysicalAvoidanceFailed)
                blockedSeconds = BlockedDuration;
            if (blockedSeconds >= BlockedDuration)
            {
#region debug-point B-C-D:blocked-admission
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                var avatar = motor as AvatarMotor;
                var velocity = avatar != null
                    ? avatar.Body.Rigidbody.velocity : Vector3.zero;
                var debugPayload =
                    $"{{\"sessionId\":\"movement-blocked\",\"runId\":\"post-fix-2\"," +
                    $"\"hypothesisId\":\"B-C-D\",\"location\":\"MoveAction.Tick.blocked\"," +
                    $"\"msg\":\"[DEBUG] movement blocked admission\",\"data\":{{" +
                    $"\"progress\":{progress},\"blockedSeconds\":{blockedSeconds}," +
                    $"\"position\":{JsonUtility.ToJson(motor.Position)}," +
                    $"\"destination\":{JsonUtility.ToJson(destination())}," +
                    $"\"velocity\":{JsonUtility.ToJson(velocity)}}}}}";
#if UNITY_EDITOR
                const string debugEndpoint = "http://127.0.0.1:7794/event";
#else
                const string debugEndpoint = "http://127.0.0.1:7794/event";
#endif
                var debugRequest = UnityEngine.Networking.UnityWebRequest.Post(
                    debugEndpoint, debugPayload, "application/json");
                debugRequest.SendWebRequest().completed += _ => debugRequest.Dispose();
#endif
#endregion
                motor.Stop();
                throw new InvalidOperationException(
                    "Movement is blocked by an obstacle.");
            }
            return ActionProgress.Running;
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
