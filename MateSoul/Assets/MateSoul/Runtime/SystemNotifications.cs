using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MateSoul.Runtime
{
    // Session-owned message bus. Game systems never create or reference UI objects.
    public sealed class SystemNotifications
    {
        public sealed class HintMessage
        {
            internal HintMessage(long id, string text, float remaining)
            {
                Id = id;
                Text = text;
                Remaining = remaining;
            }

            public long Id { get; }
            public string Text { get; }
            internal float Remaining { get; set; }
        }

        private const float DefaultHintSeconds = 5f;
        private readonly List<HintMessage> hints = new List<HintMessage>();
        private readonly ReadOnlyCollection<HintMessage> visibleHints;
        private long nextHintId;
        private float resultRemaining;

        public SystemNotifications()
        {
            visibleHints = hints.AsReadOnly();
        }

        // Kept for callers that only need the newest hint.
        public string Hint => hints.Count == 0 ? null : hints[0].Text;
        public IReadOnlyList<HintMessage> Hints => visibleHints;
        public string Result { get; private set; }
        public event Action Changed;

        public void ShowHint(string text, float seconds = DefaultHintSeconds)
        {
            if (string.IsNullOrEmpty(text)) return;
            hints.Insert(0, new HintMessage(++nextHintId, text, seconds));
            Changed?.Invoke();
        }

        public void ShowResult(string text, float seconds)
        {
            Result = text;
            resultRemaining = seconds;
            Changed?.Invoke();
        }

        public void Clear()
        {
            hints.Clear();
            Result = null;
            Changed?.Invoke();
        }

        public void Tick(float seconds)
        {
            var changed = false;
            for (var index = hints.Count - 1; index >= 0; index--)
            {
                var hint = hints[index];
                if (hint.Remaining <= 0) continue;
                hint.Remaining -= seconds;
                if (hint.Remaining > 0) continue;
                hints.RemoveAt(index);
                changed = true;
            }
            if (Result != null && resultRemaining > 0 && (resultRemaining -= seconds) <= 0)
            { Result = null; changed = true; }
            if (changed) Changed?.Invoke();
        }
    }
}
