using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MateSoul.Gameplay
{
    [Serializable]
    public sealed class SpontaneousReplyRequest
    {
        public string request_id;
        public string trigger_id;
        public string event_description;
        public string[] content_requirements;
        public string[] rules;
        public CharacterPersonaSnapshot persona;
    }

    [Serializable]
    public sealed class SpontaneousReplyResult
    {
        public string status;
        public string reply_text;
        public string response_emotion;
    }

    public interface ISpontaneousReplyProvider
    {
        Task<SpontaneousReplyResult> CompleteSpontaneousAsync(
            SpontaneousReplyRequest request, CancellationToken token);
    }

    public enum SpontaneousReplyState
    {
        Idle,
        Generating,
        Replied,
        Failed
    }

    public sealed class SpontaneousReplyCoordinator : IDisposable
    {
        private sealed class PendingPrompt
        {
            public string TriggerId;
            public string EventDescription;
            public string[] ContentRequirements;
            public string[] Rules;
        }

        private const float DefaultVisibilitySeconds = 8f;
        private readonly ISpontaneousReplyProvider provider;
        private readonly CharacterPersonaState persona;
        private readonly Queue<PendingPrompt> pending = new Queue<PendingPrompt>();
        private readonly HashSet<string> acceptedTriggers =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private bool processing;
        private float visibleSeconds;
        private bool disposed;

        public SpontaneousReplyCoordinator(
            ISpontaneousReplyProvider provider, CharacterPersonaState persona)
        {
            this.provider = provider;
            this.persona = persona ?? throw new ArgumentNullException(nameof(persona));
        }

        public SpontaneousReplyState State { get; private set; }
        public string VisibleText { get; private set; }
        public string Error { get; private set; }
        public ResponseEmotion? LastResponseEmotion { get; private set; }
        public int ReplySequence { get; private set; }
        public event Action Changed;

        public bool Enqueue(string triggerId, string eventDescription,
            IEnumerable<string> contentRequirements, IEnumerable<string> rules)
        {
            if (disposed || provider == null) return false;
            if (string.IsNullOrWhiteSpace(triggerId))
                throw new ArgumentException("A spontaneous reply requires a trigger id.");
            if (string.IsNullOrWhiteSpace(eventDescription))
                throw new ArgumentException(
                    "A spontaneous reply requires an event description.");
            var requirements = Normalize(contentRequirements, "content requirement");
            var constraints = Normalize(rules, "rule");
            if (!acceptedTriggers.Add(triggerId)) return false;
            pending.Enqueue(new PendingPrompt
            {
                TriggerId = triggerId,
                EventDescription = eventDescription.Trim(),
                ContentRequirements = requirements,
                Rules = constraints
            });
            StartNext();
            return true;
        }

        public void Tick(float seconds)
        {
            if (disposed || string.IsNullOrEmpty(VisibleText) || seconds <= 0) return;
            visibleSeconds -= seconds;
            if (visibleSeconds > 0) return;
            VisibleText = null;
            if (!processing && pending.Count == 0)
                State = SpontaneousReplyState.Idle;
            Changed?.Invoke();
        }

        private void StartNext()
        {
            if (processing || pending.Count == 0 || disposed) return;
            processing = true;
            _ = CompleteAsync(pending.Dequeue());
        }

        private async Task CompleteAsync(PendingPrompt prompt)
        {
            State = SpontaneousReplyState.Generating;
            Error = null;
            Changed?.Invoke();
            try
            {
                var result = await provider.CompleteSpontaneousAsync(
                    new SpontaneousReplyRequest
                    {
                        request_id = Guid.NewGuid().ToString("N"),
                        trigger_id = prompt.TriggerId,
                        event_description = prompt.EventDescription,
                        content_requirements = prompt.ContentRequirements,
                        rules = prompt.Rules,
                        persona = persona.Capture()
                    }, lifetime.Token);
                if (result == null || result.status != "ok" ||
                    string.IsNullOrWhiteSpace(result.reply_text))
                    throw new InvalidOperationException(
                        "系统性自发回复服务返回了无效结果。");
                if (!ResponseEmotionProtocol.TryParse(
                        result.response_emotion, out var responseEmotion))
                    throw new InvalidOperationException(
                        "系统性自发回复服务返回了无效情绪。");
                VisibleText = result.reply_text.Trim();
                visibleSeconds = DefaultVisibilitySeconds;
                LastResponseEmotion = responseEmotion;
                ReplySequence++;
                State = SpontaneousReplyState.Replied;
                Changed?.Invoke();
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                Error = error.Message;
                State = SpontaneousReplyState.Failed;
                Changed?.Invoke();
            }
            finally
            {
                processing = false;
                StartNext();
            }
        }

        private static string[] Normalize(
            IEnumerable<string> values, string description)
        {
            var result = values?.Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()).Distinct(StringComparer.Ordinal).ToArray()
                ?? Array.Empty<string>();
            if (result.Length == 0)
                throw new ArgumentException(
                    $"A spontaneous reply requires at least one {description}.");
            return result;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            pending.Clear();
            lifetime.Dispose();
            Changed = null;
        }
    }
}
