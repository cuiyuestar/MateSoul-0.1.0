using System;
using System.Collections.Generic;
using UnityEngine;

namespace MateSoul.Runtime
{
    // A targeted pickup uses the same reservation, animation and physics owner as transfers.
    public sealed class PickupItemAction : ItemActionRun
    {
        public const string Id = "pickup-item";
        private ItemTransferStep transfer;
        protected override string ActionName => "拾取物品";
        protected override string StartNotice =>
            "冷月正在拾取" + Bindings["item"].View.Definition.DisplayName + "。";
        public GameItem HeldItem => Bindings.TryGetValue("item", out var binding)
            ? binding.View : null;
        public bool IsHolding => transfer != null && transfer.IsHolding;

        public bool TryRetarget(Func<Pose> destination, out string error)
        {
            if (!IsHolding)
            {
                error = "冷月还没有拿稳物品。";
                return false;
            }
            return transfer.TryRetarget(destination, out error);
        }

        public PickupItemAction(GameSession session, string instanceId) : base(session,
            new ItemActionRequirement("item", null, "目标物品", instanceId ?? string.Empty)) { }

        public static bool Supports(GameItem item) => item != null &&
            item.Definition.Interaction != null && !item.IsFixedAfterPlacement &&
            !item.Definition.Interaction.RequiresTwoHands;

        protected override string CheckBindings()
        {
            var item = Bindings["item"];
            if (!Supports(item.View))
                return "请选择可移动的单手物品。";
            if (!Rig.PickupAnimations)
                return "角色缺少拾取动画配置。";
            var animation = item.View.Definition.UseAnimation;
            if (animation != null && animation.playOnPickup && !Rig.CanPlayUse(animation))
                return "角色缺少对应的物品交互动画绑定。";
            try
            {
                Rig.PickupAnimations.Select(
                    item.Profile.WorldGrip(item.View.Body.Pose).position.y -
                    Session.Navigation.FloorHeight, Rig.transform.lossyScale.y);
            }
            catch (InvalidOperationException error) { return error.Message; }
            return null;
        }

        protected override IEnumerable<IItemActionStep> BuildSteps()
        {
            var item = Bindings["item"];
            transfer = new ItemTransferStep("拾取" + item.View.Definition.DisplayName,
                Session, Rig, item, RunId);
            yield return transfer;
        }
    }
}
