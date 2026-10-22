using System;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent]
    public sealed class ResponseEmotionAudio : MonoBehaviour
    {
        private const string ResourceRoot = "ResponseEmotionAudio/";

        [SerializeField] private AudioClip[] normalClips = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] happyClips = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] sadClips = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] angryClips = Array.Empty<AudioClip>();
        private ConversationOrchestrator conversation;
        private SpontaneousReplyCoordinator spontaneousReplies;
        private AudioSource source;
        private int handledReplySequence;
        private int handledSpontaneousReplySequence;

        public AudioClip LastPlayedClip { get; private set; }
        public ResponseEmotion? LastPlayedEmotion { get; private set; }
        public AudioSource Source => source;

        public static ResponseEmotionAudio Attach(
            GameObject character, ConversationOrchestrator conversation,
            SpontaneousReplyCoordinator spontaneousReplies = null)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            var player = character.GetComponent<ResponseEmotionAudio>() ??
                         character.AddComponent<ResponseEmotionAudio>();
            player.Configure(
                Resources.LoadAll<AudioClip>(ResourceRoot + "Normal"),
                Resources.LoadAll<AudioClip>(ResourceRoot + "Happy"),
                Resources.LoadAll<AudioClip>(ResourceRoot + "Sad"),
                Resources.LoadAll<AudioClip>(ResourceRoot + "Angry"));
            player.Bind(conversation, spontaneousReplies);
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

        public AudioClip Play(ResponseEmotion emotion)
        {
            var clips = ClipsFor(emotion);
            if (clips == null || clips.Length == 0)
                throw new InvalidOperationException($"No audio clips are configured for {emotion}.");
            EnsureSource();
            var clip = clips[UnityEngine.Random.Range(0, clips.Length)];
            source.Stop();
            source.clip = clip;
            source.Play();
            LastPlayedClip = clip;
            LastPlayedEmotion = emotion;
            return clip;
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
            Play(conversation.LastResponseEmotion.Value);
        }

        private void OnSpontaneousReplyChanged()
        {
            if (spontaneousReplies.State != SpontaneousReplyState.Replied ||
                spontaneousReplies.ReplySequence <= handledSpontaneousReplySequence)
                return;
            handledSpontaneousReplySequence = spontaneousReplies.ReplySequence;
            if (!isActiveAndEnabled ||
                !spontaneousReplies.LastResponseEmotion.HasValue) return;
            Play(spontaneousReplies.LastResponseEmotion.Value);
        }

        private AudioClip[] ClipsFor(ResponseEmotion emotion)
        {
            switch (emotion)
            {
                case ResponseEmotion.Normal: return normalClips;
                case ResponseEmotion.Happy: return happyClips;
                case ResponseEmotion.Sad: return sadClips;
                case ResponseEmotion.Angry: return angryClips;
                default: throw new ArgumentOutOfRangeException(nameof(emotion));
            }
        }

        private void EnsureSource()
        {
            if (source != null) return;
            var animator = GetComponentInChildren<Animator>(true);
            var head = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Head)
                : null;
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
            if (source != null) source.Stop();
        }

        private void OnDestroy()
        {
            if (conversation != null) conversation.Changed -= OnConversationChanged;
            if (spontaneousReplies != null)
                spontaneousReplies.Changed -= OnSpontaneousReplyChanged;
            if (source == null) return;
            source.Stop();
            Destroy(source);
        }
    }
}
