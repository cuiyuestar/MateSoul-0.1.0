using System;
using System.Collections.Generic;
using UnityEngine;

namespace MateSoul.Runtime
{
    // Picks the radio up, performs its authored gesture and then either starts
    // a random station or crossfades to a different one before putting it back.
    public sealed class RadioMusicAction : ItemActionRun
    {
        public const string PlayId = "play-music";
        public const string SwitchId = "switch-music";
        private readonly bool switchTrack;
        protected override string ActionName => switchTrack ? "切换音乐" : "播放音乐";
        protected override string StartNotice => switchTrack
            ? "冷月正在切换收音机的音乐。" : "冷月正在打开收音机。";

        public RadioMusicAction(GameSession session, string instanceId, bool switchTrack)
            : base(session, new ItemActionRequirement(
                "radio", null, "复古收音机", instanceId ?? string.Empty))
        {
            this.switchTrack = switchTrack;
        }

        protected override string CheckBindings()
        {
            var item = Bindings["radio"];
            var radio = item.View.GetComponent<RadioController>();
            if (radio == null) return "这个物品不是可以播放音乐的收音机。";
            var animation = item.View.Definition.UseAnimation;
            if (animation == null || animation.clip == null)
                return "收音机还没有配置播放音乐的动画。";
            if (!Rig.CanPlayUse(animation))
                return "角色缺少收音机的交互动画绑定。";
            if (!Rig.PickupAnimations) return "角色缺少拾取动画配置。";
            if (radio.Stations == null || radio.Stations.Count == 0)
                return "收音机里没有音乐。";
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
            var item = Bindings["radio"];
            var origin = item.View.Body.Pose;
            yield return new ItemTransferStep("拿起" + item.View.Definition.DisplayName,
                Session, Rig, item, RunId, finishAfterPickup: true);
            var dock = Motor.Position;
            yield return new HeldItemUseStep(Rig, item.View.Definition.UseAnimation);
            yield return new RadioMusicStep(item.View, switchTrack);
            yield return new ItemTransferStep("放回" + item.View.Definition.DisplayName,
                Session, Rig, item, RunId, () => origin, dock,
                fromHeld: true);
        }
    }

    // Applies playback after the gesture, so a rejected interaction never
    // changes the station. A switch request while powered off starts music.
    public sealed class RadioMusicStep : IItemActionStep
    {
        private readonly GameItem view;
        private readonly bool switchTrack;
        public string Label => switchTrack ? "切换音乐" : "播放音乐";

        public RadioMusicStep(GameItem view, bool switchTrack)
        { this.view = view; this.switchTrack = switchTrack; }

        public void Start()
        {
            var radio = view.GetComponent<RadioController>();
            if (radio == null) throw new InvalidOperationException("这个物品不是收音机。");
            var count = radio.Stations.Count;
            if (count == 0) throw new InvalidOperationException("收音机里没有音乐。");
            var powered = radio.IsPoweredOn;
            var next = count == 1 ? 0 : UnityEngine.Random.Range(0, count);
            if (switchTrack && powered && count > 1)
            {
                // Switching must land on a different station than the current one.
                next = UnityEngine.Random.Range(0, count - 1);
                if (next >= radio.StationIndex) next++;
            }
            radio.SetTuning(count == 1 ? 0f : next / (float)(count - 1));
            if (!powered) radio.SetPower(true);
        }

        public bool Tick(float dt) => true;
        public void Dispose() { }
    }
}
