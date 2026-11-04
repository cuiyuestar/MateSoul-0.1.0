using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    public abstract class MiniGame : MonoBehaviour
    {
        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public abstract string Description { get; }
        public virtual string DialogueLine => null;
        public virtual RuntimeActivitySnapshot CaptureRuntimeContext() =>
            new RuntimeActivitySnapshot
            {
                id = Id,
                name = DisplayName,
                state = "active",
                phase = "active"
            };
        public virtual void RegisterActions(GameSession session) { }
        protected MiniGameHost Host { get; private set; }
        protected GameSession Session => Host.Session;
        protected SystemNotifications Notifications => Session.Notifications;
        public void Initialize(MiniGameHost host) { Host = host; Begin(); }
        protected abstract void Begin();
        public abstract void End();
    }
}
