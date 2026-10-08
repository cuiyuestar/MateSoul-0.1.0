using System;

namespace MateSoul.Runtime
{
    // Session-owned message bus. Game systems never create or reference UI objects.
    public sealed class SystemNotifications
    {
        public string Hint { get; private set; }
        public string Result { get; private set; }
        public event Action Changed;
        private float hintRemaining, resultRemaining;

        public void ShowHint(string text, float seconds = 6)
        {
            Hint = text;
            hintRemaining = seconds;
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
            Hint = Result = null;
            Changed?.Invoke();
        }

        public void Tick(float seconds)
        {
            var changed = false;
            if (Hint != null && hintRemaining > 0 && (hintRemaining -= seconds) <= 0)
            { Hint = null; changed = true; }
            if (Result != null && resultRemaining > 0 && (resultRemaining -= seconds) <= 0)
            { Result = null; changed = true; }
            if (changed) Changed?.Invoke();
        }
    }
}
