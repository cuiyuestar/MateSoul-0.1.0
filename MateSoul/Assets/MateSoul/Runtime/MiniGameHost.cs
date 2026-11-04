using System;
using System.Collections.Generic;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed class MiniGameHost : MonoBehaviour
    {
        [SerializeField] private MiniGame[] games = Array.Empty<MiniGame>();
        public IReadOnlyList<MiniGame> Games => games;
        public MiniGame Active { get; private set; }
        public GameSession Session { get; private set; }
        public PlayerInteractionModeController Input { get; private set; }

        public void Configure(MiniGame[] catalog) => games = catalog;
        public void Bind(GameSession session)
        {
            Session = session;
            Input = FindObjectOfType<PlayerInteractionModeController>();
            foreach (var game in games) if (game != null) game.RegisterActions(session);
        }

        public bool TryStart(string id)
        {
            if (Session == null || Session.Paused) return false;
            if (Active != null)
            {
                Session.Notifications.ShowHint("已有小游戏正在进行，请先结束当前游戏。");
                return false;
            }
            if (Session.IsRestoringItems)
            {
                Session.Notifications.ShowHint(
                    "房间网格已就绪，正在恢复历史虚拟物品，请稍候。");
                return false;
            }
            if (Input != null && Input.PlacementActive)
            {
                Session.Notifications.ShowHint("请先完成或取消当前物品放置。");
                return false;
            }
            foreach (var game in games)
            {
                if (game == null || game.Id != id) continue;
                try
                {
                    Active = Instantiate(game, transform);
                    Active.Initialize(this);
                    return true;
                }
                catch (Exception error)
                {
                    Stop();
                    Session.Notifications.ShowHint($"小游戏启动失败：{error.Message}");
                    Debug.LogException(error);
                    return false;
                }
            }
            Session.Notifications.ShowHint("该小游戏尚未安装。");
            return false;
        }

        public void Stop()
        {
            if (Active == null) return;
            var game = Active;
            Active = null;
            try { game.End(); }
            finally
            {
                game.gameObject.SetActive(false);
                Destroy(game.gameObject);
                Session.Notifications.Clear();
            }
        }

        private void OnDestroy() => Stop();
    }
}
