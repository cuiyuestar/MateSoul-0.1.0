using System;
using System.Collections.Generic;
using MateSoul.Core;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed partial class GameSession
    {
        private ActionHandle observedItemAction;
        public void CancelItemAction()
        {
            if (observedItemAction != null && !observedItemAction.IsFinished)
                Avatar.Cancel(observedItemAction);
        }

        private void ReportItemAction()
        {
            if (observedItemAction == null || !observedItemAction.IsFinished) return;
            if (observedItemAction.State != ActionState.Rejected)
                Notifications.ShowHint(observedItemAction.Error ??
                    (observedItemAction.Intent.ActionId == "visit" ||
                     observedItemAction.Intent.ActionId == UseHeldItemAction.Id
                        ? observedItemAction.State == ActionState.Succeeded
                            ? "物品互动已结束。" : "物品互动已取消。"
                     : observedItemAction.Intent.ActionId == PickupItemAction.Id
                        ? "物品拾取已结束。" :
                     observedItemAction.Intent.ActionId == EatItemAction.Id
                        ? observedItemAction.State == ActionState.Succeeded
                            ? "冷月吃饱了。" : "吃东西已取消。" :
                     observedItemAction.Intent.ActionId == RadioMusicAction.PlayId
                        ? observedItemAction.State == ActionState.Succeeded
                            ? "音乐已开始播放。" : "播放音乐已取消。" :
                     observedItemAction.Intent.ActionId == RadioMusicAction.SwitchId
                        ? observedItemAction.State == ActionState.Succeeded
                            ? "音乐已切换。" : "切换音乐已取消。" :
                     observedItemAction.State == ActionState.Succeeded
                        ? "水果切好了，已装入盘子。" : "切水果已取消。"));
            observedItemAction = null;
        }

        // Eating removes the food outright: no output exists, so the removal is the commit.
        internal void CommitEat(ItemActionBinding source, Guid runId)
        {
            if (source.Consumed || source.View == null || !source.Lease.IsValid ||
                source.Lease.InteractionId != runId)
                throw new InvalidOperationException("进食状态已失效。");
            var name = source.View.Definition.DisplayName;
            var view = source.View;
            source.Consumed = true;
            Items.Remove(view.Instance.Id);
            views.Remove(view.Instance.Id);
            view.Retire();
            UnityEngine.Object.Destroy(view.gameObject);
            try { Save(); }
            catch (Exception error)
            {
                Notifications.ShowHint(name + "已吃掉，但保存失败：" + error.Message);
                RequestPhysicsSave();
            }
        }

        // Pre-create and validate every output before removing the source. No await/callback
        // crosses the commit boundary; save failures keep the committed world and may retry.
        internal ItemActionBinding[] CommitCut(ItemActionBinding source, Pose[] poses,
            object owner, Guid runId)
        {
            if (source.Consumed || source.View == null || !source.Lease.IsValid ||
                source.Lease.InteractionId != runId)
                throw new InvalidOperationException("苹果切割状态已失效。");
            var result = source.Profile.cutResult;
            if (result == null || !result.ConsumedProduct || !definitions.ContainsKey(result.Id) ||
                poses == null || poses.Length != 4)
                throw new InvalidOperationException("苹果切割产物未配置。");
            var prepared = new List<ItemActionBinding>();
            var committed = false;
            try
            {
                foreach (var pose in poses)
                {
                    var id = Guid.NewGuid().ToString();
                    var view = Spawn(result, id, itemRoot.InverseTransformPoint(pose.position),
                        Quaternion.Inverse(itemRoot.rotation) * pose.rotation, "cut", result.Id, id);
                    var binding = new ItemActionBinding { View = view };
                    prepared.Add(binding);
                    binding.Lease = Items.TryReserve(view.Instance, result.Interaction.capability, owner, runId);
                    view.Stabilize(binding.Lease);
                    binding.Lease.Transition(ItemControlState.ReservedByCharacter,
                        ItemControlState.StagedForAction);
                }
                Physics.SyncTransforms();
                foreach (var binding in prepared)
                    if (!binding.View.Body.CanOccupy(binding.View.Body.Pose, .002f,
                            source.View.transform))
                        throw new InvalidOperationException("苹果切块区域被占用。");
                var sourceView = source.View;
                source.Consumed = true;
                Items.Remove(sourceView.Instance.Id);
                views.Remove(sourceView.Instance.Id);
                sourceView.Retire();
                UnityEngine.Object.Destroy(sourceView.gameObject);
                committed = true;
                try { Save(); }
                catch (Exception error)
                {
                    Notifications.ShowHint("苹果已切开，但保存失败：" + error.Message);
                    RequestPhysicsSave();
                }
                return prepared.ToArray();
            }
            finally
            {
                if (!committed)
                    foreach (var binding in prepared)
                    {
                        binding.Lease?.Dispose();
                        Items.Remove(binding.View.Instance.Id);
                        views.Remove(binding.View.Instance.Id);
                        binding.View.Retire();
                        UnityEngine.Object.Destroy(binding.View.gameObject);
                    }
            }
        }
    }
}
