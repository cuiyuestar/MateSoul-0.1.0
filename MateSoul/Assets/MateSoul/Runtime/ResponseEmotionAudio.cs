using System;
using System.Collections.Generic;
using System.Linq;
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
        public const string EmotionLayerName = "Emotion";
        public const string AngryStateName = "Angry";
        public const string SadStateName = "Sad";
        public const string AngryClipName = "Lengyue_Angry01";
        public const string SadClipName = "Lengyue_Sad01";
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
        private int synchronizedStartFrame;
        private int handledReplySequence;
        private int handledSpontaneousReplySequence;

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
            RegisterPerformance(ResponseEmotion.Angry, AngryActionId,
                AngryStateName, AngryClipName);
            RegisterPerformance(ResponseEmotion.Sad, SadActionId,
                SadStateName, SadClipName);
        }

        private void RegisterPerformance(ResponseEmotion emotion, string actionId,
            string stateName, string clipName)
        {
            if (performances.ContainsKey(emotion)) return;
            var animation = animator.runtimeAnimatorController?.animationClips
                .FirstOrDefault(clip => clip != null && clip.name == clipName);
            if (animation == null || animation.length <= 0)
                throw new InvalidOperationException(
                    $"Animator is missing the {emotion} animation clip.");
            var performance = new SynchronizedPerformance(emotion, actionId,
                animation, Animator.StringToHash(EmotionLayerName + "." + stateName));
            performances.Add(emotion, performance);
            if (session.Catalog.TryGet(actionId, out _)) return;
            session.Catalog.Register(new ActionDefinition(
                actionId,
                $"Play the synchronized {emotion} response performance",
                PerformanceChannels,
                intent => new SynchronizedEmotionAction(
                    this, performance, intent.Payload as AudioClip),
                timeoutSeconds: 6f,
                exposeToAI: false,
                exitTimeoutSeconds: .25f));
        }

        private SynchronizedPerformance PerformanceFor(ResponseEmotion emotion)
            => performances.TryGetValue(emotion, out var performance) ? performance : null;

        public AudioClip Play(ResponseEmotion emotion)
        {
            var clips = ClipsFor(emotion);
            if (clips == null || clips.Length == 0)
                throw new InvalidOperationException($"No audio clips are configured for {emotion}.");
            var clip = PerformanceFor(emotion) != null
                ? clips.OrderByDescending(value => value.length).First()
                : clips[UnityEngine.Random.Range(0, clips.Length)];
            if (session != null)
                pendingEmotions.Enqueue((emotion, clip));
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
                emotionAction = null;
            if (emotionAction != null || SynchronizedEmotionActive ||
                pendingEmotions.Count == 0 || session.Paused ||
                session.Avatar.Suspended)
                return;

            var pending = pendingEmotions.Peek();
            var performance = PerformanceFor(pending.emotion);
            if (performance == null)
            {
                pendingEmotions.Dequeue();
                PlayAudio(pending.emotion, pending.clip);
                return;
            }

            if (session.Avatar.HasBlockingClaim(PerformanceChannels, IntentSource.AI))
                return;
            pendingEmotions.Dequeue();
            var handle = session.Avatar.Submit(new AvatarIntent(
                performance.ActionId, IntentSource.AI, payload: pending.clip));
            if (!handle.IsFinished)
            {
                emotionAction = handle;
                return;
            }
            if (handle.Error != null)
                Debug.LogError(
                    $"Synchronized {pending.emotion} response failed: " + handle.Error, this);
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
            if (clip.length + .01f < performance.Animation.length)
                return "Synchronized emotion audio must cover the complete animation.";
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
            animator.SetLayerWeight(emotionLayer, 1);
            animator.Play(performance.StateHash, emotionLayer, 0);
            animator.Update(0);
            source.Play();
            synchronizedStartFrame = Time.frameCount;
            performance.NormalizedTime = 0;
            activePerformance = performance;
            LastPlayedClip = clip;
            LastPlayedEmotion = performance.Emotion;
        }

        private bool TickSynchronizedPerformance()
        {
            if (activePerformance == null) return false;
            if (source != null && source.isPlaying)
            {
                var elapsed = source.clip == null ||
                              source.clip.frequency <= 0
                    ? 0
                    : source.timeSamples / (float)source.clip.frequency;
                activePerformance.NormalizedTime = Mathf.Clamp01(
                    elapsed / activePerformance.Animation.length);
                animator.Play(
                    activePerformance.StateHash, emotionLayer,
                    Mathf.Min(activePerformance.NormalizedTime, .999f));
                return true;
            }
            if (Time.frameCount <= synchronizedStartFrame) return true;
            activePerformance.NormalizedTime = 1;
            return false;
        }

        private void EndSynchronizedPerformance()
        {
            if (activePerformance == null) return;
            if (source != null) source.Stop();
            if (animator != null && emotionLayer >= 0)
                animator.SetLayerWeight(emotionLayer, 0);
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
                        .FirstOrDefault(value => value.name == "Head 1");
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
