using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MateSoul.Core;

namespace MateSoul.Gameplay
{
    [Serializable]
    public sealed class DialogueMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    public sealed class DialogueAction
    {
        public string id;
        public string target_id;
        public string destination;
        public string destination_kind;
    }

    public enum PlacementDestinationKind { Any, Real, Virtual }

    public sealed class PlacementDirective
    {
        public string Destination { get; set; }
        public PlacementDestinationKind Kind { get; set; }
        // The player's own words, so a placement can recover the item when the
        // model only named the destination ("把苹果捡起来放到床上").
        public string Request { get; set; }
    }

    public interface IPlacementCommandHandler
    {
        bool TryPlace(PlacementDirective directive, string itemInstanceId, out string failure);
    }

    public static class PlacementCommands
    {
        public const string ActionId = "place-item";
    }

    [Serializable]
    public sealed class MoodSignal
    {
        public string id;
        public float delta;
    }

    [Serializable]
    public sealed class ConversationReply
    {
        public string status;
        public string reply_text;
        public string response_emotion;
        public DialogueAction action;
        public MoodSignal[] mood_signals;
    }

    public sealed class ConversationRequest
    {
        public string Input { get; }
        public IReadOnlyList<DialogueMessage> History { get; }
        public IReadOnlyList<ActionDefinition> Actions { get; }
        public IReadOnlyList<ItemInstance> Targets { get; }
        public IReadOnlyList<string> Moods { get; }
        public CharacterContextSnapshot CharacterContext { get; }
        public float ContextCaptureMilliseconds { get; }
        // Set only when the runtime rejected an earlier requested action; the
        // model then explains the failure in character instead of inventing one.
        public string RuntimeNotice { get; }
        public ConversationRequest(string input, IReadOnlyList<DialogueMessage> history,
            IReadOnlyList<ActionDefinition> actions, IReadOnlyList<ItemInstance> targets,
            IReadOnlyList<string> moods, CharacterContextSnapshot characterContext,
            float contextCaptureMilliseconds, string runtimeNotice = null)
        {
            Input = input; History = history; Actions = actions; Targets = targets; Moods = moods;
            CharacterContext = characterContext;
            ContextCaptureMilliseconds = contextCaptureMilliseconds;
            RuntimeNotice = runtimeNotice;
        }
    }

    public interface ILLMProvider
    {
        Task<ConversationReply> CompleteAsync(ConversationRequest request, CancellationToken cancellationToken);
    }

    public interface IConversationContextProvider
    {
        IReadOnlyList<DialogueMessage> Read();
        void Append(string input, string reply);
    }

    public sealed class SessionConversationContext : IConversationContextProvider
    {
        private readonly List<DialogueMessage> messages = new List<DialogueMessage>();
        public IReadOnlyList<DialogueMessage> Read() => messages.ToArray();
        public void Append(string input, string reply)
        {
            messages.Add(new DialogueMessage { role = "user", content = input });
            messages.Add(new DialogueMessage { role = "assistant", content = reply });
            if (messages.Count > 16) messages.RemoveRange(0, messages.Count - 16);
        }
    }

    public enum ConversationState { Idle, Thinking, Replied, Cancelled, Failed }

    public sealed class ConversationOrchestrator : IDisposable
    {
        // Wired by the runtime layer; the gameplay assembly keeps no engine dependency.
        public static Action<string> Trace;
        private static void Report(string message) => Trace?.Invoke(message);
        private static string Short(string value, int limit)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= limit ? value : value.Substring(0, limit) + "…";
        }

        private readonly ILLMProvider provider;
        private readonly IConversationContextProvider dialogueContext;
        private readonly ICharacterContextProvider characterContext;
        private readonly ActionCatalog catalog;
        private readonly AvatarActionCoordinator avatar;
        private readonly ItemRegistry items;
        private readonly MoodSystem mood;
        private readonly IPlacementCommandHandler placement;
        private CancellationTokenSource pending;
        private bool disposed;
        public ConversationState State { get; private set; }
        public string Text { get; private set; }
        public string ActionError { get; private set; }
        public ActionHandle LastAction { get; private set; }
        public ResponseEmotion? LastResponseEmotion { get; private set; }
        public int ReplySequence { get; private set; }
        public event Action Changed;

        public ConversationOrchestrator(ILLMProvider provider, IConversationContextProvider context,
            ActionCatalog catalog, AvatarActionCoordinator avatar, ItemRegistry items, MoodSystem mood,
            ICharacterContextProvider characterContext, IPlacementCommandHandler placement = null)
        {
            this.provider = provider; dialogueContext = context; this.catalog = catalog;
            this.avatar = avatar; this.items = items; this.mood = mood;
            this.placement = placement;
            this.characterContext = characterContext ??
                throw new ArgumentNullException(nameof(characterContext));
        }

        public async Task SendAsync(string input, CancellationToken cancellationToken = default)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ConversationOrchestrator));
            if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("Message is empty.");
            if (avatar.HasClaim(ControlChannels.Dialogue))
                throw new InvalidOperationException("冷月正在完成小游戏回合，请稍候。");
            if (pending != null)
                throw new InvalidOperationException("冷月正在回复，请稍候。");
            var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestCancellation.CancelAfter(TimeSpan.FromSeconds(30));
            pending = requestCancellation;
            State = ConversationState.Thinking;
            Text = null;
            ActionError = null;
            LastAction = null;
            LastResponseEmotion = null;
            Changed?.Invoke();
            try
            {
                var actions = new List<ActionDefinition>();
                foreach (var definition in catalog.Definitions)
                    if (definition.ExposeToAI) actions.Add(definition);
                var targets = new List<ItemInstance>();
                foreach (var item in items.Items) if (!item.IsOccupied) targets.Add(item);
                var moods = new List<string>();
                foreach (var definition in mood.Definitions) moods.Add(definition.Id);
                var capture = Stopwatch.StartNew();
                var character = characterContext.Capture();
                capture.Stop();
                var request = new ConversationRequest(input, dialogueContext.Read(), actions, targets, moods,
                    character, (float)capture.Elapsed.TotalMilliseconds);
                var actionIds = new List<string>();
                foreach (var definition in actions) actionIds.Add(definition.Id);
                var targetIds = new List<string>();
                foreach (var item in targets) targetIds.Add(item.Id);
                Report($"request input={Short(input, 120)} actions=[{string.Join(",", actionIds)}] " +
                    $"targets=[{string.Join(",", targetIds)}] contextMs={capture.Elapsed.TotalMilliseconds:F0}");
                var reply = await provider.CompleteAsync(request, requestCancellation.Token);
                requestCancellation.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(pending, requestCancellation)) return;
                if (avatar.HasClaim(ControlChannels.Dialogue))
                    throw new InvalidOperationException("冷月正在完成小游戏回合，请稍候。");
                if (reply == null || string.IsNullOrWhiteSpace(reply.reply_text) || reply.reply_text.Length > 4096)
                    throw new InvalidOperationException("模型回复缺少有效的 reply_text。");
                var responseEmotion = ResponseEmotionProtocol.ParseOrDefault(reply.response_emotion);
                if (!ResponseEmotionProtocol.TryParse(reply.response_emotion, out _))
                    Report($"emotion fallback raw={reply.response_emotion}");
                Report($"reply text={Short(reply.reply_text, 90)} " +
                    $"emotion={reply.response_emotion} action={reply.action?.id} " +
                    $"target={reply.action?.target_id} destination={reply.action?.destination} " +
                    $"kind={reply.action?.destination_kind}");

                if (reply.action != null && !string.IsNullOrEmpty(reply.action.id) && reply.action.id != "none")
                {
                    if (!catalog.TryGet(reply.action.id, out var action) || !action.ExposeToAI)
                        ActionError = "模型请求了未开放的动作。";
                    else if (action.AcceptsDestination)
                    {
                        if (placement == null)
                            ActionError = "放置能力尚未配置。";
                        else
                        {
                            var directive = new PlacementDirective
                            {
                                Destination = reply.action.destination,
                                Kind = ParseDestinationKind(reply.action.destination_kind),
                                Request = input
                            };
                            if (!placement.TryPlace(directive, reply.action.target_id, out var failure))
                            {
                                ActionError = string.IsNullOrWhiteSpace(failure)
                                    ? "冷月没能找到合适的位置。" : failure;
                                var explained = await ExplainFailureAsync(input, moods, character,
                                    ActionError, requestCancellation.Token);
                                requestCancellation.Token.ThrowIfCancellationRequested();
                                if (!ReferenceEquals(pending, requestCancellation)) return;
                                if (explained != null)
                                {
                                    reply = explained;
                                    ResponseEmotionProtocol.TryParse(
                                        explained.response_emotion, out responseEmotion);
                                }
                            }
                        }
                    }
                    else if (action.RequiresTarget &&
                        (!items.TryGet(reply.action.target_id, out var target) || !target.Supports(action.TargetCapability)))
                        ActionError = "动作目标不存在或不支持此能力。";
                    else if (!action.AcceptsTarget && reply.action.target_id != null)
                        ActionError = "动作不接受目标。";
                    else
                    {
                        // Optional-target actions resolve real-world names inside the
                        // runtime ("table", 衣柜); an id that is not a registered item
                        // is delegated instead of being rejected here.
                        if (action.AcceptsTarget && !string.IsNullOrEmpty(reply.action.target_id) &&
                            !items.TryGet(reply.action.target_id, out _))
                            Report($"target delegated id={reply.action.target_id} " +
                                $"action={action.Id} reason=not-a-registered-item");
                        // A fresh spoken command supersedes the previous one instead of
                        // being blocked by it; a carried item is handed over in place.
                        LastAction = avatar.Submit(new AvatarIntent(
                            action.Id, IntentSource.AI, reply.action.target_id),
                            preemptSamePriority: true);
                        if (LastAction.State == ActionState.Rejected) ActionError = LastAction.Error;
                        Report($"submit action={action.Id} target={reply.action.target_id} " +
                            $"state={LastAction.State} error={LastAction.Error}");
                    }
                }
                else if (reply.action != null && !string.IsNullOrEmpty(reply.action.id))
                    Report($"action skipped id={reply.action.id} reason=no-action-or-none");
                if (ActionError != null) Report($"action error={ActionError}");
                if (reply.mood_signals != null)
                    foreach (var signal in reply.mood_signals)
                        if (signal != null && mood.Contains(signal.id) &&
                            !float.IsNaN(signal.delta) && !float.IsInfinity(signal.delta))
                            mood.Apply(signal.id, Math.Max(-32, Math.Min(32, signal.delta)));
                dialogueContext.Append(input, reply.reply_text);
                Text = reply.reply_text;
                LastResponseEmotion = responseEmotion;
                ReplySequence++;
                State = ConversationState.Replied;
            }
            catch (OperationCanceledException)
            {
                if (ReferenceEquals(pending, requestCancellation))
                {
                    State = ConversationState.Cancelled;
                    Text = "对话已取消或超时。";
                    Report("cancelled timeout=30s");
                }
            }
            catch (Exception exception)
            {
                if (ReferenceEquals(pending, requestCancellation))
                {
                    State = ConversationState.Failed;
                    Text = exception.Message;
                    Report($"failed type={exception.GetType().Name} error={exception.Message}");
                }
            }
            finally
            {
                if (ReferenceEquals(pending, requestCancellation))
                {
                    pending = null;
                    Changed?.Invoke();
                }
                requestCancellation.Dispose();
            }
        }

        private async Task<ConversationReply> ExplainFailureAsync(string input,
            IReadOnlyList<string> moods, CharacterContextSnapshot character,
            string failure, CancellationToken cancellationToken)
        {
            try
            {
                var request = new ConversationRequest(input, dialogueContext.Read(),
                    Array.Empty<ActionDefinition>(), Array.Empty<ItemInstance>(), moods,
                    character, 0f, failure);
                var reply = await provider.CompleteAsync(request, cancellationToken);
                if (reply == null || string.IsNullOrWhiteSpace(reply.reply_text) ||
                    reply.reply_text.Length > 4096 ||
                    !ResponseEmotionProtocol.TryParse(reply.response_emotion, out _))
                    return null;
                return reply;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return null; }
        }

        private static PlacementDestinationKind ParseDestinationKind(string value)
        {
            if (string.Equals(value, "real", StringComparison.OrdinalIgnoreCase))
                return PlacementDestinationKind.Real;
            if (string.Equals(value, "virtual", StringComparison.OrdinalIgnoreCase))
                return PlacementDestinationKind.Virtual;
            return PlacementDestinationKind.Any;
        }

        public void Cancel()
        {
            if (pending == null) return;
            var cancellation = pending;
            pending = null;
            cancellation.Cancel();
            State = ConversationState.Cancelled;
            Text = "对话已取消。";
            Changed?.Invoke();
        }

        public void Dispose()
        {
            if (disposed) return;
            Cancel();
            disposed = true;
        }
    }
}
