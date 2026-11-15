using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MateSoul.Core;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent]
    public sealed class ResponseEmotionAudio : MonoBehaviour
    {
        public const string AngryActionId = "response-emotion-angry";
        public const string SadActionId = "response-emotion-sad";
        public const string HappyActionId = "response-emotion-happy";
        public const string HappyExpressionActionId = "express-happy";
        public const string SadExpressionActionId = "express-sad";
        public const string AngryExpressionActionId = "express-angry";
        public const string EmotionLayerName = "Emotion";
        public const string AngryStateName = "Angry";
        public const string SadStateName = "Sad";
        public const string HappyStateName = "Happy";
        public const string AngryClipName = "lengyue_clip_angry01";
        public const string SadClipName = "lengyue_clip_sad01";
        public const string HappyClipName = "lengyue_clip_happy01";
        private const string ResourceRoot = "ResponseEmotionAudio/";
        private static readonly ControlChannels PerformanceChannels =
            ControlChannels.Locomotion | ControlChannels.FullBody |
            ControlChannels.Gaze | ControlChannels.Face;

        [SerializeField] private AudioClip[] normalClips = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] happyClips = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] sadClips = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] angryClips = Array.Empty<AudioClip>();
        private ConversationOrchestrator conversation;
        private SpontaneousReplyCoordinator spontaneousReplies;
        private readonly Queue<(ResponseEmotion emotion, AudioClip clip)> pendingEmotions =
            new Queue<(ResponseEmotion, AudioClip)>();
        private GameSession session;
        private AudioSource source;
        private Animator animator;
        private readonly Dictionary<ResponseEmotion, SynchronizedPerformance> performances =
            new Dictionary<ResponseEmotion, SynchronizedPerformance>();
        private SynchronizedPerformance activePerformance;
        private ActionHandle emotionAction;
        private int emotionLayer = -1;
        private SkinnedMeshRenderer faceRenderer;
        private bool faceProbeLogged;
        private int poseProbeStage;
        private readonly Dictionary<string, Transform> poseBones =
            new Dictionary<string, Transform>(StringComparer.Ordinal);
        private static readonly string[] PoseBoneNames =
        {
            "lengyue_hips", "lengyue_spine", "lengyue_chest", "lengyue_upper_chest",
            "lengyue_upper_arm_l", "lengyue_upper_arm_r",
            "lengyue_forearm_l", "lengyue_forearm_r",
            "lengyue_thigh_l", "lengyue_thigh_r",
            "lengyue_shin_l", "lengyue_shin_r",
            "lengyue_hand_l", "lengyue_hand_r",
            "lengyue_foot_l", "lengyue_foot_r", "lengyue_head_01"
        };
        private int synchronizedStartFrame;
        private float emotionLayerWeight;
        private const float EmotionFadeSeconds = .12f;
        private int handledReplySequence;
        private int handledSpontaneousReplySequence;
        private ResponseEmotion? lastBlockedEmotion;
        private string lastBlockedReason;
        private int lastBlockedFrame = -60;

        public AudioClip LastPlayedClip { get; private set; }
        public ResponseEmotion? LastPlayedEmotion { get; private set; }
        public AudioSource Source => source;
        public bool SynchronizedEmotionActive => activePerformance != null;
        public bool SynchronizedAngryActive =>
            activePerformance?.Emotion == ResponseEmotion.Angry;
        public bool SynchronizedSadActive =>
            activePerformance?.Emotion == ResponseEmotion.Sad;
        public float SynchronizedAngryNormalizedTime =>
            PerformanceFor(ResponseEmotion.Angry)?.NormalizedTime ?? 0;
        public float SynchronizedSadNormalizedTime =>
            PerformanceFor(ResponseEmotion.Sad)?.NormalizedTime ?? 0;
        public AnimationClip AngryAnimation => PerformanceFor(ResponseEmotion.Angry)?.Animation;
        public AnimationClip SadAnimation => PerformanceFor(ResponseEmotion.Sad)?.Animation;

        public static ResponseEmotionAudio Attach(
            GameObject character, ConversationOrchestrator conversation,
            SpontaneousReplyCoordinator spontaneousReplies = null)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            var player = character.GetComponent<ResponseEmotionAudio>() ??
                         character.AddComponent<ResponseEmotionAudio>();
            player.Bind(conversation, spontaneousReplies);
            return player;
        }

        public static ResponseEmotionAudio Attach(
            GameObject character, GameSession session,
            ConversationOrchestrator conversation,
            SpontaneousReplyCoordinator spontaneousReplies = null)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            var player = Attach(character, conversation, spontaneousReplies);
            player.BindRuntime(session);
            return player;
        }

        public void Configure(AudioClip[] normal, AudioClip[] happy, AudioClip[] sad, AudioClip[] angry)
        {
            normalClips = CopyAndValidate(normal, ResponseEmotion.Normal);
            happyClips = CopyAndValidate(happy, ResponseEmotion.Happy);
            sadClips = CopyAndValidate(sad, ResponseEmotion.Sad);
            angryClips = CopyAndValidate(angry, ResponseEmotion.Angry);
        }

        public void Bind(ConversationOrchestrator value,
            SpontaneousReplyCoordinator spontaneous = null)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (conversation != null) conversation.Changed -= OnConversationChanged;
            if (spontaneousReplies != null)
                spontaneousReplies.Changed -= OnSpontaneousReplyChanged;
            conversation = value;
            handledReplySequence = value.ReplySequence;
            conversation.Changed += OnConversationChanged;
            spontaneousReplies = spontaneous;
            if (spontaneousReplies == null) return;
            handledSpontaneousReplySequence = spontaneousReplies.ReplySequence;
            spontaneousReplies.Changed += OnSpontaneousReplyChanged;
        }

        public void BindRuntime(GameSession value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (session != null && session != value)
                throw new InvalidOperationException(
                    "Response emotion runtime is already bound to another session.");
            session = value;
            animator = GetComponentInChildren<Animator>(true);
            if (animator == null)
                throw new InvalidOperationException(
                    "Synchronized emotion responses require an Animator.");
            emotionLayer = animator.GetLayerIndex(EmotionLayerName);
            if (emotionLayer < 0)
                throw new InvalidOperationException(
                    "Animator is missing the Emotion layer.");
            faceRenderer = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(value => value.name == "lengyue_face_root");
            foreach (var name in PoseBoneNames)
            {
                var bone = animator.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(value => value.name == name);
                if (bone != null) poseBones[name] = bone;
            }
            Debug.Log($"MATESOUL_EMOTION_POSE_BONES found={poseBones.Count}/{PoseBoneNames.Length}");
            RegisterPerformance(ResponseEmotion.Angry, AngryActionId,
                AngryStateName, AngryClipName);
            RegisterPerformance(ResponseEmotion.Sad, SadActionId,
                SadStateName, SadClipName);
            // Happy is optional so a missing authored clip cannot block startup.
            RegisterPerformance(ResponseEmotion.Happy, HappyActionId,
                HappyStateName, HappyClipName, false);
            RegisterExpressionActions();
        }

        private void RegisterExpressionActions()
        {
            RegisterExpression(ResponseEmotion.Happy, HappyExpressionActionId,
                "Smile or laugh: play a happy face and body performance. Use when the player " +
                "asks you to smile, laugh or look happy.");
            RegisterExpression(ResponseEmotion.Sad, SadExpressionActionId,
                "Cry or look sad: play the crying face and body performance. Use when the " +
                "player asks you to cry or look sad.");
            RegisterExpression(ResponseEmotion.Angry, AngryExpressionActionId,
                "Look angry: play the angry face and body performance. Use when the player " +
                "asks you to be angry or show an angry face.");
        }

        private void RegisterExpression(ResponseEmotion emotion,
            string actionId, string description)
        {
            if (!performances.TryGetValue(emotion, out var performance)) return;
            if (session.Catalog.TryGet(actionId, out _)) return;
            session.Catalog.Register(new ActionDefinition(
                actionId, description, PerformanceChannels,
                intent => new SynchronizedEmotionAction(
                    this, performance, LongestClip(emotion)),
                timeoutSeconds: 8f, exposeToAI: true, exitTimeoutSeconds: .25f));
        }

        private static bool IsExpressionAction(string actionId)
            => actionId == HappyExpressionActionId ||
               actionId == SadExpressionActionId ||
               actionId == AngryExpressionActionId;

        private AudioClip LongestClip(ResponseEmotion emotion)
        {
            var clips = ClipsFor(emotion);
            return clips == null || clips.Length == 0
                ? null
                : clips.OrderByDescending(value => value.length).First();
        }

        private SynchronizedPerformance RegisterPerformance(ResponseEmotion emotion, string actionId,
            string stateName, string clipName, bool required = true)
        {
            if (performances.TryGetValue(emotion, out var existing)) return existing;
            var animation = animator.runtimeAnimatorController?.animationClips
                .FirstOrDefault(clip => clip != null && clip.name == clipName);
            if (animation == null || animation.length <= 0)
            {
                if (required)
                    throw new InvalidOperationException(
                        $"Animator is missing the {emotion} animation clip.");
                Debug.LogWarning(
                    $"MATESOUL_EMOTION_CLIP_MISSING emotion={emotion} clip={clipName}");
                return null;
            }
            var performance = new SynchronizedPerformance(emotion, actionId,
                animation, Animator.StringToHash(EmotionLayerName + "." + stateName));
            performances.Add(emotion, performance);
            Debug.Log(
                $"MATESOUL_EMOTION_PERFORMANCE emotion={emotion} clip={clipName} " +
                $"seconds={animation.length:F3} state={stateName}");
            if (session.Catalog.TryGet(actionId, out _)) return performance;
            session.Catalog.Register(new ActionDefinition(
                actionId,
                $"Play the synchronized {emotion} response performance",
                PerformanceChannels,
                intent => new SynchronizedEmotionAction(
                    this, performance, intent.Payload as AudioClip),
                timeoutSeconds: 6f,
                exposeToAI: false,
                exitTimeoutSeconds: .25f));
            return performance;
        }

        private SynchronizedPerformance PerformanceFor(ResponseEmotion emotion)
            => performances.TryGetValue(emotion, out var performance) ? performance : null;

        public AudioClip Play(ResponseEmotion emotion)
        {
            var clips = ClipsFor(emotion);
            if (clips == null || clips.Length == 0)
                throw new InvalidOperationException($"No audio clips are configured for {emotion}.");
            var performance = PerformanceFor(emotion);
            var clip = performance != null
                ? LongestClip(emotion)
                : clips[UnityEngine.Random.Range(0, clips.Length)];
            Debug.Log(
                $"MATESOUL_EMOTION dispatch emotion={emotion} " +
                $"mode={(performance != null ? "performance" : "audio-only")} " +
                $"clip={(clip != null ? clip.name : "none")} " +
                $"seconds={(clip != null ? clip.length : 0f):F3}");
            if (session != null)
            {
                pendingEmotions.Enqueue((emotion, clip));
                Debug.Log($"MATESOUL_EMOTION queued emotion={emotion} pending={pendingEmotions.Count}");
            }
            else
                PlayAudio(emotion, clip);
            return clip;
        }

        private void PlayAudio(ResponseEmotion emotion, AudioClip clip)
        {
            EnsureSource();
            source.Stop();
            source.clip = clip;
            source.Play();
            LastPlayedClip = clip;
            LastPlayedEmotion = emotion;
        }

        public int ClipCount(ResponseEmotion emotion)
        {
            var clips = ClipsFor(emotion);
            return clips == null ? 0 : clips.Length;
        }

        private void OnConversationChanged()
        {
            if (conversation.State != ConversationState.Replied ||
                conversation.ReplySequence <= handledReplySequence)
                return;
            handledReplySequence = conversation.ReplySequence;
            if (!isActiveAndEnabled || !conversation.LastResponseEmotion.HasValue) return;
            // An explicit expression action already played this performance; do not
            // start a second copy from the reply tone classification.
            var lastAction = conversation.LastAction;
            if (lastAction != null && lastAction.State != ActionState.Rejected &&
                IsExpressionAction(lastAction.Intent.ActionId))
            {
                Debug.Log(
                    $"MATESOUL_EMOTION auto-skip explicit={lastAction.Intent.ActionId} " +
                    $"state={lastAction.State}");
                return;
            }
            Dispatch(conversation.LastResponseEmotion.Value);
        }

        private void OnSpontaneousReplyChanged()
        {
            if (spontaneousReplies.State != SpontaneousReplyState.Replied ||
                spontaneousReplies.ReplySequence <= handledSpontaneousReplySequence)
                return;
            handledSpontaneousReplySequence = spontaneousReplies.ReplySequence;
            if (!isActiveAndEnabled ||
                !spontaneousReplies.LastResponseEmotion.HasValue) return;
            Dispatch(spontaneousReplies.LastResponseEmotion.Value);
        }

        private void Dispatch(ResponseEmotion emotion)
        {
            Play(emotion);
        }

        private void Update()
        {
            if (session == null) return;
            if (emotionAction != null && emotionAction.IsFinished)
            {
                Debug.Log($"MATESOUL_EMOTION action-finished state={emotionAction.State} " +
                          $"error={emotionAction.Error}");
                emotionAction = null;
            }
            if (emotionAction != null || SynchronizedEmotionActive ||
                pendingEmotions.Count == 0)
                return;
            if (session.Paused || session.Avatar.Suspended)
            {
                ReportBlocked(pendingEmotions.Peek().emotion,
                    session.Paused ? "session-paused" : "avatar-suspended");
                return;
            }

            var pending = pendingEmotions.Peek();
            var performance = PerformanceFor(pending.emotion);
            if (performance == null)
            {
                pendingEmotions.Dequeue();
                Debug.Log($"MATESOUL_EMOTION audio-only emotion={pending.emotion} " +
                          $"clip={pending.clip?.name} pending={pendingEmotions.Count}");
                PlayAudio(pending.emotion, pending.clip);
                return;
            }

            if (session.Avatar.HasBlockingClaim(PerformanceChannels, IntentSource.AI))
            {
                ReportBlocked(pending.emotion, "ai-action-owns-channels");
                return;
            }
            pendingEmotions.Dequeue();
            var handle = session.Avatar.Submit(new AvatarIntent(
                performance.ActionId, IntentSource.AI, payload: pending.clip));
            Debug.Log(
                $"MATESOUL_EMOTION submit emotion={pending.emotion} action={performance.ActionId} " +
                $"state={handle.State} error={handle.Error} pending={pendingEmotions.Count}");
            if (!handle.IsFinished)
            {
                emotionAction = handle;
                return;
            }
            if (handle.Error != null)
                Debug.LogError(
                    $"Synchronized {pending.emotion} response failed: " + handle.Error, this);
        }

        private void ReportBlocked(ResponseEmotion emotion, string reason)
        {
            if (lastBlockedEmotion == emotion && lastBlockedReason == reason &&
                Time.frameCount - lastBlockedFrame < 60)
                return;
            lastBlockedEmotion = emotion;
            lastBlockedReason = reason;
            lastBlockedFrame = Time.frameCount;
            Debug.Log($"MATESOUL_EMOTION blocked emotion={emotion} reason={reason} " +
                      $"pending={pendingEmotions.Count}");
        }

        private sealed class SynchronizedPerformance
        {
            public readonly ResponseEmotion Emotion;
            public readonly string ActionId;
            public readonly AnimationClip Animation;
            public readonly int StateHash;
            public float NormalizedTime;

            public SynchronizedPerformance(ResponseEmotion emotion, string actionId,
                AnimationClip animation, int stateHash)
            {
                Emotion = emotion;
                ActionId = actionId;
                Animation = animation;
                StateHash = stateHash;
            }
        }

        private string ValidateSynchronizedPerformance(
            SynchronizedPerformance performance, AudioClip clip)
        {
            if (!isActiveAndEnabled)
                return "Response emotion runtime is unavailable.";
            if (clip == null || clip.length <= 0)
                return "Synchronized emotion audio is missing.";
            if (animator == null || emotionLayer < 0 ||
                performance == null || performance.Animation == null)
                return "Synchronized emotion animation is unavailable.";
            return null;
        }

        private void BeginSynchronizedPerformance(
            SynchronizedPerformance performance, AudioClip clip)
        {
            var error = ValidateSynchronizedPerformance(performance, clip);
            if (error != null) throw new InvalidOperationException(error);
            EnsureSource();
            source.Stop();
            source.clip = clip;
            source.timeSamples = 0;
            LogBonePose("before-emotion");
            // Fade the layer in over a few frames so the first pose blends out of
            // the idle stance instead of snapping, matching the item-use layers.
            emotionLayerWeight = 0f;
            animator.SetLayerWeight(emotionLayer, 0f);
            animator.Play(performance.StateHash, emotionLayer, 0);
            animator.Update(0);
            source.Play();
            synchronizedStartFrame = Time.frameCount;
            performance.NormalizedTime = 0;
            activePerformance = performance;
            faceProbeLogged = false;
            poseProbeStage = 0;
            LastPlayedClip = clip;
            LastPlayedEmotion = performance.Emotion;
            LogState("t0");
            LogBonePose("t0");
            Debug.Log(
                $"MATESOUL_EMOTION begin emotion={performance.Emotion} " +
                $"state={performance.StateHash} animation={performance.Animation.name} " +
                $"animationSeconds={performance.Animation.length:F3} " +
                $"audio={(clip != null ? clip.name : "none")} " +
                $"audioSeconds={(clip != null ? clip.length : 0f):F3} " +
                $"layer={emotionLayer} weight={animator.GetLayerWeight(emotionLayer):F2} " +
                $"fade={EmotionFadeSeconds}");
        }

        private bool TickSynchronizedPerformance()
        {
            if (activePerformance == null) return false;
            if (emotionLayerWeight < 1f)
            {
                emotionLayerWeight = Mathf.MoveTowards(
                    emotionLayerWeight, 1f, Time.deltaTime / EmotionFadeSeconds);
                animator.SetLayerWeight(emotionLayer, emotionLayerWeight);
            }
            if (source != null && source.isPlaying)
            {
                var elapsed = source.clip == null ||
                              source.clip.frequency <= 0
                    ? 0
                    : source.timeSamples / (float)source.clip.frequency;
                activePerformance.NormalizedTime = Mathf.Clamp01(
                    elapsed / activePerformance.Animation.length);
            }
            else
            {
                if (Time.frameCount <= synchronizedStartFrame) return true;
                // Audio can be shorter than the authored clip (for example the laugh
                // recording). In that case finish the performance on its own clock
                // instead of cutting the body and face halfway through.
                activePerformance.NormalizedTime = Mathf.Clamp01(
                    activePerformance.NormalizedTime +
                    Time.deltaTime / Mathf.Max(.0001f, activePerformance.Animation.length));
            }
            animator.Play(
                activePerformance.StateHash, emotionLayer,
                Mathf.Min(activePerformance.NormalizedTime, .999f));
            ProbePose();
            ProbeFaceWeights();
            return activePerformance.NormalizedTime < 1f;
        }

        // Key-bone sampling turns "the limbs look twisted" into concrete numbers
        // that can be compared against the idle baseline in the same session.
        private void ProbePose()
        {
            if (activePerformance == null) return;
            float normalized = activePerformance.NormalizedTime;
            if (poseProbeStage == 0 && normalized >= .2f)
            {
                poseProbeStage = 1;
            }
            else if (poseProbeStage == 1 && normalized >= .5f)
            {
                poseProbeStage = 2;
            }
            else if (poseProbeStage == 2 && normalized >= .8f)
            {
                poseProbeStage = 3;
            }
            else
            {
                return;
            }
            animator.Update(0);
            string tag = "t" + normalized.ToString("F1");
            LogState(tag);
            LogBonePose(tag);
        }

        private void LogState(string tag)
        {
            if (animator == null || emotionLayer < 0) return;
            var info = animator.GetCurrentAnimatorStateInfo(emotionLayer);
            Debug.Log(
                $"MATESOUL_EMOTION_STATE {tag} layer={emotionLayer} " +
                $"expected={activePerformance?.StateHash ?? 0} actual={info.fullPathHash} " +
                $"stateNormalized={info.normalizedTime:F2} " +
                $"performanceNormalized={activePerformance?.NormalizedTime ?? 0:F2} " +
                $"weight={animator.GetLayerWeight(emotionLayer):F2}");
        }

        private void LogBonePose(string tag)
        {
            if (poseBones.Count == 0) return;
            var builder = new StringBuilder();
            foreach (var name in PoseBoneNames)
            {
                if (!poseBones.TryGetValue(name, out var bone))
                {
                    builder.Append(name).Append("=missing ");
                    continue;
                }
                var euler = bone.localRotation.eulerAngles;
                builder.Append(name).Append("=(")
                    .Append(euler.x.ToString("F0")).Append(",")
                    .Append(euler.y.ToString("F0")).Append(",")
                    .Append(euler.z.ToString("F0")).Append(") ");
            }
            if (poseBones.TryGetValue("lengyue_hips", out var hips))
            {
                foreach (var name in new[]
                         {
                             "lengyue_hand_l", "lengyue_hand_r",
                             "lengyue_foot_l", "lengyue_foot_r", "lengyue_head_01"
                         })
                {
                    if (!poseBones.TryGetValue(name, out var bone)) continue;
                    var offset = hips.InverseTransformPoint(bone.position);
                    builder.Append(name).Append("HipRel=(")
                        .Append(offset.x.ToString("F2")).Append(",")
                        .Append(offset.y.ToString("F2")).Append(",")
                        .Append(offset.z.ToString("F2")).Append(") ");
                }
            }
            Debug.Log($"MATESOUL_EMOTION_POSE {tag} {builder}");
        }

        // Runtime evidence that the authored facial morphs actually reach the face
        // renderer through the Emotion layer, not just the body bones.
        private void ProbeFaceWeights()
        {
            if (faceProbeLogged || activePerformance == null ||
                activePerformance.NormalizedTime < .2f)
                return;
            faceProbeLogged = true;
            if (faceRenderer == null)
            {
                Debug.LogWarning("MATESOUL_EMOTION_FACE missing renderer=lengyue_face_root");
                return;
            }
            animator.Update(0);
            var mesh = faceRenderer.sharedMesh;
            float max = 0;
            string maxShape = "none";
            for (int index = 0; index < mesh.blendShapeCount; index++)
            {
                float weight = faceRenderer.GetBlendShapeWeight(index);
                if (weight <= max) continue;
                max = weight;
                maxShape = mesh.GetBlendShapeName(index);
            }
            Debug.Log(
                $"MATESOUL_EMOTION_FACE emotion={activePerformance.Emotion} " +
                $"shapes={mesh.blendShapeCount} max={max:F1} maxShape={maxShape} " +
                $"joyL={Weight("Fcl_EYE_Joy_L"):F1} joyR={Weight("Fcl_EYE_Joy_R"):F1} " +
                $"mouthJoy={Weight("Fcl_MTH_Joy"):F1}");

            float Weight(string shape)
            {
                int index = mesh.GetBlendShapeIndex(shape);
                return index < 0 ? -1f : faceRenderer.GetBlendShapeWeight(index);
            }
        }

        private void EndSynchronizedPerformance()
        {
            if (activePerformance == null) return;
            Debug.Log(
                $"MATESOUL_EMOTION end emotion={activePerformance.Emotion} " +
                $"normalized={activePerformance.NormalizedTime:F2}");
            if (source != null) source.Stop();
            if (animator != null && emotionLayer >= 0)
                animator.SetLayerWeight(emotionLayer, 0);
            emotionLayerWeight = 0f;
            activePerformance = null;
        }

        private sealed class SynchronizedEmotionAction :
            IAvatarAction, IActionRuntimeStatus
        {
            private readonly ResponseEmotionAudio owner;
            private readonly SynchronizedPerformance performance;
            private readonly AudioClip clip;
            private bool cancelled;
            private bool started;

            public SynchronizedEmotionAction(
                ResponseEmotionAudio owner, SynchronizedPerformance performance, AudioClip clip)
            {
                this.owner = owner;
                this.performance = performance;
                this.clip = clip;
            }

            public string PhaseId => "performing";
            public string PostureId => "standing";

            public string Check()
                => owner == null
                    ? "Response emotion runtime is unavailable."
                    : owner.ValidateSynchronizedPerformance(performance, clip);

            public void Start()
            {
                if (owner == null)
                    throw new InvalidOperationException(
                        "Response emotion runtime is unavailable.");
                owner.BeginSynchronizedPerformance(performance, clip);
                started = true;
            }

            public ActionProgress Tick(float deltaSeconds)
                => cancelled || owner == null ||
                   !owner.TickSynchronizedPerformance()
                    ? ActionProgress.Succeeded
                    : ActionProgress.Running;

            public void Cancel(ActionStopReason reason)
            {
                cancelled = true;
                if (started && owner != null) owner.EndSynchronizedPerformance();
                started = false;
            }

            public void Dispose()
            {
                if (started && owner != null) owner.EndSynchronizedPerformance();
                started = false;
            }
        }

        private AudioClip[] ClipsFor(ResponseEmotion emotion)
        {
            switch (emotion)
            {
                case ResponseEmotion.Normal: return EnsureLoaded(ref normalClips, emotion);
                case ResponseEmotion.Happy: return EnsureLoaded(ref happyClips, emotion);
                case ResponseEmotion.Sad: return EnsureLoaded(ref sadClips, emotion);
                case ResponseEmotion.Angry: return EnsureLoaded(ref angryClips, emotion);
                default: throw new ArgumentOutOfRangeException(nameof(emotion));
            }
        }

        private static AudioClip[] EnsureLoaded(ref AudioClip[] clips, ResponseEmotion emotion)
        {
            if (clips == null || clips.Length == 0)
                clips = CopyAndValidate(
                    Resources.LoadAll<AudioClip>(ResourceRoot + emotion), emotion);
            return clips;
        }

        private void EnsureSource()
        {
            if (source != null) return;
            var animator = GetComponentInChildren<Animator>(true);
            var head = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Head)
                : animator == null
                    ? null
                    : animator.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(value => value.name == "lengyue_head_01");
            source = (head != null ? head.gameObject : gameObject).AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = .6f;
            source.maxDistance = 8f;
            source.volume = .85f;
        }

        private static AudioClip[] CopyAndValidate(AudioClip[] clips, ResponseEmotion emotion)
        {
            if (clips == null || clips.Length == 0)
                throw new ArgumentException($"At least one {emotion} audio clip is required.");
            var copy = new AudioClip[clips.Length];
            for (var index = 0; index < clips.Length; index++)
            {
                if (clips[index] == null)
                    throw new ArgumentException($"{emotion} audio clips cannot contain null.");
                copy[index] = clips[index];
            }
            return copy;
        }

        private void OnDisable()
        {
            pendingEmotions.Clear();
            if (session != null && emotionAction != null)
                session.Avatar.Cancel(emotionAction);
            emotionAction = null;
            EndSynchronizedPerformance();
            if (source != null) source.Stop();
        }

        private void OnDestroy()
        {
            if (conversation != null) conversation.Changed -= OnConversationChanged;
            if (spontaneousReplies != null)
                spontaneousReplies.Changed -= OnSpontaneousReplyChanged;
            pendingEmotions.Clear();
            EndSynchronizedPerformance();
            if (source == null) return;
            source.Stop();
            Destroy(source);
        }
    }
}
