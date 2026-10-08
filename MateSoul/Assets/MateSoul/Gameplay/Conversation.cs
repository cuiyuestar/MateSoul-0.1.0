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
        public ConversationRequest(string input, IReadOnlyList<DialogueMessage> history,
            IReadOnlyList<ActionDefinition> actions, IReadOnlyList<ItemInstance> targets,
            IReadOnlyList<string> moods, CharacterContextSnapshot characterContext,
            float contextCaptureMilliseconds)
        {
            Input = input; History = history; Actions = actions; Targets = targets; Moods = moods;
            CharacterContext = characterContext;
            ContextCaptureMilliseconds = contextCaptureMilliseconds;
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
        private readonly ILLMProvider provider;
        private readonly IConversationContextProvider dialogueContext;
        private readonly ICharacterContextProvider characterContext;
        private readonly ActionCatalog catalog;
        private readonly AvatarActionCoordinator avatar;
        private readonly ItemRegistry items;
        private readonly MoodSystem mood;
        private CancellationTokenSource pending;
        private bool disposed;
        public ConversationState State { get; private set; }
        public string Text { get; private set; }
        public string ActionError { get; private set; }
        public ActionHandle LastAction { get; private set; }
        public event Action Changed;

        public ConversationOrchestrator(ILLMProvider provider, IConversationContextProvider context,
            ActionCatalog catalog, AvatarActionCoordinator avatar, ItemRegistry items, MoodSystem mood,
            ICharacterContextProvider characterContext)
        {
            this.provider = provider; dialogueContext = context; this.catalog = catalog;
            this.avatar = avatar; this.items = items; this.mood = mood;
            this.characterContext = characterContext ??
                throw new ArgumentNullException(nameof(characterContext));
        }

        public async Task SendAsync(string input, CancellationToken cancellationToken = default)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ConversationOrchestrator));
            if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("Message is empty.");
            if (avatar.HasClaim(ControlChannels.Dialogue))
                throw new InvalidOperationException("冷月正在完成小游戏回合，请稍候。");
            Cancel();
            var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestCancellation.CancelAfter(TimeSpan.FromSeconds(30));
            pending = requestCancellation;
            State = ConversationState.Thinking;
            Text = null;
            ActionError = null;
            LastAction = null;
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
                var reply = await provider.CompleteAsync(request, requestCancellation.Token);
                requestCancellation.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(pending, requestCancellation)) return;
                if (avatar.HasClaim(ControlChannels.Dialogue))
                    throw new InvalidOperationException("冷月正在完成小游戏回合，请稍候。");
                if (reply == null || string.IsNullOrWhiteSpace(reply.reply_text) || reply.reply_text.Length > 4096)
                    throw new InvalidOperationException("模型回复缺少有效的 reply_text。");

                if (reply.action != null && !string.IsNullOrEmpty(reply.action.id) && reply.action.id != "none")
                {
                    if (!catalog.TryGet(reply.action.id, out var action) || !action.ExposeToAI)
                        ActionError = "模型请求了未开放的动作。";
                    else if (action.RequiresTarget &&
                        (!items.TryGet(reply.action.target_id, out var target) || !target.Supports(action.TargetCapability)))
                        ActionError = "动作目标不存在或不支持此能力。";
                    else
                    {
                        LastAction = avatar.Submit(new AvatarIntent(
                            action.Id, IntentSource.AI, reply.action.target_id));
                        if (LastAction.State == ActionState.Rejected) ActionError = LastAction.Error;
                    }
                }
                if (reply.mood_signals != null)
                    foreach (var signal in reply.mood_signals)
                        if (signal != null && mood.Contains(signal.id) &&
                            !float.IsNaN(signal.delta) && !float.IsInfinity(signal.delta))
                            mood.Apply(signal.id, Math.Max(-32, Math.Min(32, signal.delta)));
                dialogueContext.Append(input, reply.reply_text);
                Text = reply.reply_text;
                State = ConversationState.Replied;
            }
            catch (OperationCanceledException)
            {
                if (ReferenceEquals(pending, requestCancellation))
                {
                    State = ConversationState.Cancelled;
                    Text = "对话已取消或超时。";
                }
            }
            catch (Exception exception)
            {
                if (ReferenceEquals(pending, requestCancellation))
                {
                    State = ConversationState.Failed;
                    Text = exception.Message;
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
