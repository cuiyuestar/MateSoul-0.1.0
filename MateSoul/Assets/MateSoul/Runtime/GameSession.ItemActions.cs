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
                    (observedItemAction.State == ActionState.Succeeded
                        ? "水果切好了，已装入盘子。" : "切水果已取消。"));
            observedItemAction = null;
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
