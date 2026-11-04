using System.Collections.Generic;
using UnityEngine;

namespace MateSoul.Runtime
{
    // Targeted and autonomous use share the same coordinator, lease and hand owner.
    public sealed class UseHeldItemAction : ItemActionRun
    {
        protected override string ActionName => "使用物品";
        protected override string StartNotice =>
            "冷月正在" + Bindings["item"].View.Definition.UseAnimation.actionLabel + "。";

        public UseHeldItemAction(GameSession session, string instanceId) : base(session,
            new ItemActionRequirement("item", "visit", "目标物品", instanceId ?? string.Empty)) { }

        public static bool Supports(GameItem item) => PickupItemAction.Supports(item) &&
            item.Definition.UseAnimation != null && item.Definition.UseAnimation.clip != null;

        protected override string CheckBindings()
        {
            var item = Bindings["item"];
            if (!Supports(item.View)) return "这个物品还没有使用动画。";
            if (!Rig.CanPlayUse(item.View.Definition.UseAnimation))
                return "角色缺少对应的物品使用动画绑定。";
            if (!Rig.PickupAnimations) return "角色缺少拾取动画配置。";
            try
            {
                Rig.PickupAnimations.Select(
                    item.Profile.WorldGrip(item.View.Body.Pose).position.y -
                    Session.Navigation.FloorHeight, Rig.transform.lossyScale.y);
            }
            catch (System.InvalidOperationException error) { return error.Message; }
            return null;
        }

        protected override IEnumerable<IItemActionStep> BuildSteps()
        {
            var item = Bindings["item"];
            var origin = item.View.Body.Pose;
            yield return new ItemTransferStep("拿起" + item.View.Definition.DisplayName,
                Session, Rig, item, RunId, finishAfterPickup: true);
            var dock = Motor.Position;
            yield return new HeldItemUseStep(Rig, item.View.Definition.UseAnimation);
            yield return new ItemTransferStep("放回" + item.View.Definition.DisplayName,
                Session, Rig, item, RunId, () => origin, dock,
                fromHeld: true);
        }
    }

    public sealed class HeldItemUseStep : IItemActionStep
    {
        private const float BlendSeconds = .25f;
        private readonly AvatarItemRig rig;
        private readonly ItemUseAnimation animation;
        private float elapsed;
        public string Label => animation.actionLabel;

        public HeldItemUseStep(AvatarItemRig rig, ItemUseAnimation animation)
        { this.rig = rig; this.animation = animation; }

        public void Start()
        {
            rig.ClearReach();
            rig.UseMotion(animation, 0, 0);
        }

        public bool Tick(float dt)
        {
            elapsed += dt;
            var weight = Mathf.SmoothStep(0, 1, elapsed / BlendSeconds) *
                (1 - Mathf.SmoothStep(0, 1, (elapsed - animation.clip.length) / BlendSeconds));
            rig.UseMotion(animation, Mathf.Min(elapsed, animation.clip.length), weight);
            return elapsed >= animation.clip.length + BlendSeconds;
        }

        public void Dispose() => rig.ClearUse();
    }
}
