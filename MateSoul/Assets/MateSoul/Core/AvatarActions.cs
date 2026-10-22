using System;
using System.Collections.Generic;

namespace MateSoul.Core
{
    [Flags]
    public enum ControlChannels
    {
        None = 0,
        Locomotion = 1,
        Body = 2,
        LeftArm = 4,
        RightArm = 8,
        Gaze = 16,
        Face = 32,
        Appearance = 64,
        Dialogue = 128,
        All = Locomotion | Body | LeftArm | RightArm | Gaze | Face | Appearance | Dialogue,
        FullBody = Body | LeftArm | RightArm
    }

    public enum IntentSource { Ambient, Autonomous, AI, UI, Player, System }
    public enum ActionState { Queued, Running, Cancelling, Succeeded, Cancelled, Failed, Rejected }
    public enum ActionProgress { Running, Succeeded }
    public enum ActionStopReason { None, Requested, Preempted, Timeout, TargetLost, Paused, SessionEnded }

    // Coordinates and engine references belong to a typed payload owned by the caller.
    // Network protocols must translate into one of those payloads at their boundary.
    public sealed class AvatarIntent
    {
        public string ActionId { get; }
        public string TargetId { get; }
        public IntentSource Source { get; }
        public object Payload { get; }

        public AvatarIntent(string actionId, IntentSource source, string targetId = null, object payload = null)
        {
            ActionId = actionId;
            Source = source;
            TargetId = targetId;
            Payload = payload;
        }
    }

    public sealed class ActionHandle
    {
        public Guid Id { get; } = Guid.NewGuid();
        public AvatarIntent Intent { get; }
        public ActionState State { get; internal set; } = ActionState.Queued;
        public ActionStopReason StopReason { get; internal set; }
        public string Error { get; internal set; }
        public float ElapsedSeconds { get; internal set; }
        public bool IsFinished => State >= ActionState.Succeeded;

        internal ActionHandle(AvatarIntent intent) { Intent = intent; }
    }

    public interface IAvatarAction : IDisposable
    {
        // Side-effect-free admission. null means available.
        string Check();
        void Start();
        ActionProgress Tick(float deltaSeconds);
        // Request an exit phase; Tick continues while the action owns its channels.
        void Cancel(ActionStopReason reason);
    }

    public interface IActionRuntimeStatus
    {
        string PhaseId { get; }
        string PostureId { get; }
    }

    [Serializable]
    public sealed class AvatarActionSnapshot
    {
        public string action_id;
        public string target_id;
        public string source;
        public string state;
        public string phase;
        public string posture;
        public float elapsed_seconds;
    }

    public sealed class ActionDefinition
    {
        public string Id { get; }
        public string Description { get; }
        public ControlChannels Channels { get; }
        public bool RequiresTarget { get; }
        public string TargetCapability { get; }
        public bool ExposeToAI { get; }
        public float TimeoutSeconds { get; }
        public float ExitTimeoutSeconds { get; }
        internal Func<AvatarIntent, IAvatarAction> Factory { get; }

        public ActionDefinition(
            string id, string description, ControlChannels channels,
            Func<AvatarIntent, IAvatarAction> factory, float timeoutSeconds = 30f,
            string targetCapability = null, bool exposeToAI = true, float exitTimeoutSeconds = 3f)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Action ID is required.", nameof(id));
            if (timeoutSeconds <= 0 || exitTimeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            Id = id;
            Description = description;
            Channels = channels;
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            TimeoutSeconds = timeoutSeconds;
            ExitTimeoutSeconds = exitTimeoutSeconds;
            TargetCapability = targetCapability;
            RequiresTarget = targetCapability != null;
            ExposeToAI = exposeToAI;
        }
    }

    public sealed class ActionCatalog
    {
        private readonly Dictionary<string, ActionDefinition> definitions =
            new Dictionary<string, ActionDefinition>(StringComparer.Ordinal);

        public IEnumerable<ActionDefinition> Definitions => definitions.Values;

        public void Register(ActionDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            definitions.Add(definition.Id, definition);
        }

        public bool TryGet(string id, out ActionDefinition definition)
        {
            definition = null;
            return id != null && definitions.TryGetValue(id, out definition);
        }
    }
}
