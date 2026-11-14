using System;
using System.Collections.Generic;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    // Eating shares the pickup ownership and carries the food through three chew loops.
    public sealed class EatItemAction : ItemActionRun
    {
        public const string Id = "eat-item";
        public const int Bites = 3;
        protected override string ActionName => "吃东西";
        protected override string StartNotice =>
            "冷月正在吃" + Bindings["item"].View.Definition.DisplayName + "。";

        public EatItemAction(GameSession session, string instanceId) : base(session,
            new ItemActionRequirement("item", null, "目标物品", instanceId ?? string.Empty)) { }

        public static bool Supports(GameItem item) => PickupItemAction.Supports(item) &&
            item.Definition.IsEdible;

        protected override string CheckBindings()
        {
            var item = Bindings["item"];
            if (!Supports(item.View)) return "请选择可以吃的食物。";
            if (Rig.EatAnimation == null || Rig.EatAnimation.biteSound == null)
                return "角色缺少吃东西动画或咀嚼音频。";
            if (!Rig.CanPlayUse(Rig.EatAnimation))
                return "角色缺少对应的吃东西动画绑定。";
            if (!Rig.PickupAnimations) return "角色缺少拾取动画配置。";
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
            yield return new ItemTransferStep("拿起" + item.View.Definition.DisplayName,
                Session, Rig, item, RunId, finishAfterPickup: true);
            yield return new EatHeldItemStep(Rig, Rig.EatAnimation, item.View, Bites);
            yield return new ConsumeEatenItemStep(Session, Rig, item, RunId);
        }
    }

    // The bite clip and its audio start on the same frame; the audio sample position drives
    // the animation so every loop seam stays aligned, and one wrap counts as one bite.
    public sealed class EatHeldItemStep : IItemActionStep
    {
        private const float BlendSeconds = .25f;
        private const float BiteShrinkSeconds = .35f;
        private readonly AvatarItemRig rig;
        private readonly ItemUseAnimation animation;
        private readonly GameItem view;
        private readonly int bites;
        private Transform model;
        private Vector3 modelScale;
        private AudioSource source;
        private Transform mouth;
        private int lastSamples, completed;
        private float elapsed;
        private bool finished;
        public string Label => animation.actionLabel;

        public EatHeldItemStep(AvatarItemRig rig, ItemUseAnimation animation,
            GameItem view, int bites)
        {
            this.rig = rig;
            this.animation = animation;
            this.view = view;
            this.bites = Mathf.Max(1, bites);
        }

        public void Start()
        {
            if (animation == null || animation.clip == null || animation.biteSound == null)
                throw new InvalidOperationException("吃东西动画缺少动画或咀嚼音频。");
            // Only the model child is scaled so the collider and the held pose stay intact.
            model = view.transform.Find("Model");
            if (model == null)
                foreach (Transform child in view.transform)
                    if (child.GetComponentsInChildren<Renderer>(true).Length > 0)
                    {
                        model = child;
                        break;
                    }
            if (model != null) modelScale = model.localScale;
            rig.ClearReach();
            rig.UseMotion(animation, 0, 0);
            // Keep the bite sound on its own emitter at the mouth so it attenuates and
            // pans from the character's head instead of the rig root at her feet.
            var emitter = new GameObject("EatBiteAudio");
            mouth = emitter.transform;
            // Parent to the head bone: the emitter then rides the animated head in the
            // same way as the character voice, with no one-frame lag from Update-time
            // writing the pose before LateUpdate.
            mouth.SetParent(rig.Head != null ? rig.Head : rig.transform, false);
            mouth.position = rig.MouthPosition;
            source = emitter.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = .6f;
            source.maxDistance = 8f;
            source.volume = .9f;
            source.clip = animation.biteSound;
            source.timeSamples = 0;
            source.Play();
        }

        public bool Tick(float dt)
        {
            elapsed += dt;
            var biteLength = animation.biteSound.length;
            var position = elapsed % biteLength;
            var clocked = source != null && source.isPlaying && source.clip != null;
            if (clocked)
            {
                var samples = source.timeSamples;
                if (samples < lastSamples) completed++;
                lastSamples = samples;
                position = samples / (float)source.clip.frequency;
                if (completed >= bites) return Finish();
            }
            else if ((completed = Mathf.FloorToInt(elapsed / biteLength)) >= bites)
                return Finish();
            ApplyBiteScale(position, biteLength);
            var fadeIn = Mathf.SmoothStep(0f, 1f, elapsed / BlendSeconds);
            var fadeOut = completed < bites - 1 ? 1f
                : 1f - Mathf.SmoothStep(0f, 1f,
                    (position - (biteLength - BlendSeconds)) / BlendSeconds);
            rig.UseMotion(animation, position / biteLength * animation.clip.length,
                fadeIn * fadeOut);
            return false;
        }

        // Shrink while the mouth closes so every bite visibly removes a third of the food.
        private void ApplyBiteScale(float position, float biteLength)
        {
            if (model == null) return;
            var closing = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                biteLength - BiteShrinkSeconds, biteLength, position));
            var remaining = 1f - Mathf.Clamp01((completed + closing) / bites);
            model.localScale = modelScale * remaining;
        }

        // The third bite ends here: hide the food in the same frame so it never lingers
        // after the animation, regardless of when the deferred Destroy runs.
        private bool Finish()
        {
            finished = true;
            if (model != null) model.gameObject.SetActive(false);
            return true;
        }

        public void Dispose()
        {
            if (source != null)
            {
                source.Stop();
                source.clip = null;
            }
            if (mouth != null) UnityEngine.Object.Destroy(mouth.gameObject);
            if (!finished && model != null) model.localScale = modelScale;
            source = null;
            mouth = null;
            rig.ClearUse();
        }
    }

    // The eaten item leaves the hand and the world in one commit; nothing is put back.
    public sealed class ConsumeEatenItemStep : IItemActionStep
    {
        private readonly GameSession session;
        private readonly AvatarItemRig rig;
        private readonly ItemActionBinding binding;
        private readonly Guid run;
        public string Label => "咽下";

        public ConsumeEatenItemStep(GameSession session, AvatarItemRig rig,
            ItemActionBinding binding, Guid run)
        { this.session = session; this.rig = rig; this.binding = binding; this.run = run; }

        public void Start()
        {
            rig.DetachHeld(run);
            session.CommitEat(binding, run);
        }

        public bool Tick(float dt) => true;
        public void Dispose() { }
    }
}
