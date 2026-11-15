using System;
using System.Collections.Generic;
using UnityEngine;

namespace MateSoul.Runtime
{
    public enum RadioGesture { Play, Switch, Stop, VolumeUp, VolumeDown }

    // Picks the radio up, performs its authored gesture and then applies the
    // requested playback change before putting it back.
    public sealed class RadioMusicAction : ItemActionRun
    {
        public const string PlayId = "play-music";
        public const string SwitchId = "switch-music";
        public const string StopId = "stop-music";
        public const string VolumeUpId = "volume-up";
        public const string VolumeDownId = "volume-down";
        private readonly RadioGesture gesture;
        protected override string ActionName
        {
            get
            {
                switch (gesture)
                {
                    case RadioGesture.Switch: return "切换音乐";
                    case RadioGesture.Stop: return "关闭收音机";
                    case RadioGesture.VolumeUp: return "调大音量";
                    case RadioGesture.VolumeDown: return "调小音量";
                    default: return "播放音乐";
                }
            }
        }
        protected override string StartNotice
        {
            get
            {
                switch (gesture)
                {
                    case RadioGesture.Switch: return "冷月正在切换收音机的音乐。";
                    case RadioGesture.Stop: return "冷月正在关掉收音机。";
                    case RadioGesture.VolumeUp: return "冷月正在调大收音机的音量。";
                    case RadioGesture.VolumeDown: return "冷月正在调小收音机的音量。";
                    default: return "冷月正在打开收音机。";
                }
            }
        }

        public RadioMusicAction(GameSession session, string instanceId, RadioGesture gesture)
            : base(session, new ItemActionRequirement(
                "radio", null, "复古收音机", instanceId ?? string.Empty))
        {
            this.gesture = gesture;
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
            yield return new RadioMusicStep(item.View, gesture);
            yield return new ItemTransferStep("放回" + item.View.Definition.DisplayName,
                Session, Rig, item, RunId, () => origin, dock,
                fromHeld: true);
        }
    }

    // Applies playback after the gesture, so a rejected interaction never
    // changes the station. A switch request while powered off starts music.
    public sealed class RadioMusicStep : IItemActionStep
    {
        private const float VolumeStep = .2f;
        private readonly GameItem view;
        private readonly RadioGesture gesture;
        private bool logged;
        public string Label
        {
            get
            {
                switch (gesture)
                {
                    case RadioGesture.Switch: return "切换音乐";
                    case RadioGesture.Stop: return "关闭收音机";
                    case RadioGesture.VolumeUp: return "调大音量";
                    case RadioGesture.VolumeDown: return "调小音量";
                    default: return "播放音乐";
                }
            }
        }

        public RadioMusicStep(GameItem view, RadioGesture gesture)
        { this.view = view; this.gesture = gesture; }

        public void Start()
        {
            var radio = view.GetComponent<RadioController>();
            if (radio == null) throw new InvalidOperationException("这个物品不是收音机。");
            var count = radio.Stations.Count;
            if (count == 0) throw new InvalidOperationException("收音机里没有音乐。");
            switch (gesture)
            {
                case RadioGesture.Stop:
                    radio.SetPower(false);
                    return;
                case RadioGesture.VolumeUp:
                    radio.SetVolume(radio.Volume + VolumeStep);
                    return;
                case RadioGesture.VolumeDown:
                    radio.SetVolume(radio.Volume - VolumeStep);
                    return;
            }
            var powered = radio.IsPoweredOn;
            var next = count == 1 ? 0 : UnityEngine.Random.Range(0, count);
            if (gesture == RadioGesture.Switch && powered && count > 1)
            {
                // Switching must land on a different station than the current one.
                next = UnityEngine.Random.Range(0, count - 1);
                if (next >= radio.StationIndex) next++;
            }
            radio.SetTuning(count == 1 ? 0f : next / (float)(count - 1));
            if (!powered) radio.SetPower(true);
        }

        public bool Tick(float dt)
        {
            if (!logged)
            {
                logged = true;
                var radio = view.GetComponent<RadioController>();
                // Release builds compile out the debug probes, so the applied
                // playback state has to be readable from the plain device log.
                Debug.Log($"MATESOUL_RADIO gesture={gesture} powered={radio.IsPoweredOn} " +
                    $"station={radio.StationIndex + 1}/{radio.Stations.Count} " +
                    $"volume={Mathf.RoundToInt(radio.Volume * 100)}%");
            }
            return true;
        }
        public void Dispose() { }
    }
}
