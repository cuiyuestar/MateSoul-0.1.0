using System;
using System.Collections.Generic;
using MateSoul.Gameplay;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace MateSoul.Runtime
{
    public enum TelevisionState { Off, NoSignal, Preparing, Playing, Paused, Ended, Error }

    /// <summary>Owns this television's power, decoder, audio and screen texture.</summary>
    public sealed class TelevisionController : MonoBehaviour, IRuntimeWorldFactSource
    {
        [Header("Programs")]
        [SerializeField] private VideoClip[] programs = Array.Empty<VideoClip>();
        [SerializeField] private int programIndex = -1;
        [Header("Active source (clip takes priority over URL)")]
        [SerializeField] private VideoClip videoClip;
        [SerializeField] private string videoUrl;
        [SerializeField, Range(0, 1)] private float volume = .65f;
        [SerializeField, Min(1)] private float prepareTimeout = 30f;
        [Header("Prefab bindings")]
        [SerializeField] private Renderer screen;
        [SerializeField] private Renderer indicator;
        [SerializeField] private Text statusLabel;
        [SerializeField] private VideoPlayer player;
        [SerializeField] private AudioSource speaker;
        private MaterialPropertyBlock screenProperties;
        private MaterialPropertyBlock lightProperties;
        private RenderTexture videoTexture;
        private float brightness, prepareStarted;
        private bool applicationPaused;
        public TelevisionState State { get; private set; } = TelevisionState.Off;
        public bool IsPoweredOn => State != TelevisionState.Off;
        public string LastError { get; private set; }
        public event Action<TelevisionState> StateChanged;
        public VideoPlayer Player => player;
        public IReadOnlyList<VideoClip> Programs => programs;
        public int ProgramIndex => programIndex;
        public VideoClip CurrentProgram => programIndex >= 0 && programIndex < programs.Length
            ? programs[programIndex] : null;

        public bool TryCaptureRuntimeWorldFact(
            out RuntimeWorldFactSnapshot fact)
        {
            if (!IsPoweredOn)
            {
                fact = null;
                return false;
            }
            var program = CurrentProgram == null
                ? programIndex >= 0 ? $"program-{programIndex + 1}" : "external-source"
                : CurrentProgram.name;
            fact = RuntimeWorldFact.Create(
                this, "device", State.ToString().ToLowerInvariant(),
                $"program={program}");
            return true;
        }

        public void Configure(Renderer display, Renderer powerLight, Text label,
            VideoPlayer decoder, AudioSource audio)
        {
            screen = display;
            indicator = powerLight;
            statusLabel = label;
            player = decoder;
            speaker = audio;
        }

        private void Awake()
        {
            if (screen == null || indicator == null || statusLabel == null ||
                player == null || speaker == null)
                throw new InvalidOperationException("Television prefab bindings are incomplete.");
            screenProperties = new MaterialPropertyBlock();
            lightProperties = new MaterialPropertyBlock();
            player.playOnAwake = false;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.aspectRatio = VideoAspectRatio.FitOutside;
            player.isLooping = true;
            player.waitForFirstFrame = true;
            player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            player.controlledAudioTrackCount = 1;
            player.SetTargetAudioSource(0, speaker);
            speaker.playOnAwake = false;
            speaker.spatialBlend = 1;
            speaker.rolloffMode = AudioRolloffMode.Logarithmic;
            speaker.minDistance = .5f;
            speaker.maxDistance = 8f;
            player.prepareCompleted += Prepared;
            player.errorReceived += Failed;
            player.loopPointReached += Ended;
            RefreshDisplay();
        }

        public void TogglePower() => SetPower(!IsPoweredOn);

        public void ConfigurePrograms(VideoClip[] clips)
        {
            if (clips == null || Array.Exists(clips, clip => clip == null))
                throw new ArgumentException("Television programs cannot contain null clips.", nameof(clips));
            programs = (VideoClip[])clips.Clone();
            programIndex = programs.Length == 0 ? -1 : 0;
            videoClip = CurrentProgram;
            videoUrl = string.Empty;
            if (Application.isPlaying && IsPoweredOn) BeginSource();
        }

        public void SelectProgram(int index)
        {
            if (index < 0 || index >= programs.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            programIndex = index;
            videoClip = programs[index];
            videoUrl = string.Empty;
            if (IsPoweredOn) BeginSource();
        }

        public void SetPower(bool powered)
        {
            if (!isActiveAndEnabled || powered == IsPoweredOn) return;
            if (powered) BeginSource();
            else PowerOff();
        }

        public void SetVideoClip(VideoClip clip)
        {
            programIndex = -1;
            videoClip = clip;
            videoUrl = string.Empty;
            if (IsPoweredOn) BeginSource();
        }

        public void SetVideoUrl(string url)
        {
            if (!string.IsNullOrWhiteSpace(url) && !ValidUrl(url))
                throw new ArgumentException("Use an absolute http, https or file video URL.", nameof(url));
            programIndex = -1;
            videoClip = null;
            videoUrl = url?.Trim();
            if (IsPoweredOn) BeginSource();
        }

        private static bool ValidUrl(string url)
            => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp ||
                uri.Scheme == Uri.UriSchemeFile);

        private void BeginSource()
        {
            StopDecoder();
            LastError = null;
            if (videoClip == null && string.IsNullOrWhiteSpace(videoUrl))
            {
                SetState(TelevisionState.NoSignal);
                return;
            }
            if (videoClip == null && !ValidUrl(videoUrl))
            {
                Fail("Invalid video URL.");
                return;
            }
            // Screen is 358 x 258 mm. FitOutside preserves aspect ratio and center-crops
            // excess image area so every program fills the glass without letterboxing.
            videoTexture = new RenderTexture(1024, 738, 0, RenderTextureFormat.ARGB32)
            {
                name = "Television Video (Instance)", filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            videoTexture.Create();
            var previous = RenderTexture.active;
            RenderTexture.active = videoTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = previous;
            player.targetTexture = videoTexture;
            player.source = videoClip != null ? VideoSource.VideoClip : VideoSource.Url;
            player.clip = videoClip;
            player.url = videoClip == null ? videoUrl : string.Empty;
            player.isLooping = true;
            speaker.volume = volume;
            prepareStarted = Time.unscaledTime;
            SetState(TelevisionState.Preparing);
            player.Prepare();
        }

        private void Prepared(VideoPlayer source)
        {
            if (State != TelevisionState.Preparing || !source.isPrepared) return;
            if (source.audioTrackCount > 0)
            {
                source.EnableAudioTrack(0, true);
                source.SetTargetAudioSource(0, speaker);
            }
            if (applicationPaused) SetState(TelevisionState.Paused);
            else
            {
                source.Play();
                SetState(TelevisionState.Playing);
            }
        }

        private void Failed(VideoPlayer source, string message)
        {
            if (IsPoweredOn) Fail(message);
        }

        private void Fail(string message)
        {
            LastError = message;
            StopDecoder();
            SetState(TelevisionState.Error);
            Debug.LogWarning($"TELEVISION_VIDEO_ERROR {message}", this);
        }

        private void Ended(VideoPlayer source)
        {
            if (State == TelevisionState.Playing && !source.isLooping)
                SetState(TelevisionState.Ended);
        }

        private void Update()
        {
            if (State == TelevisionState.Preparing && !applicationPaused &&
                Time.unscaledTime - prepareStarted >= prepareTimeout)
                Fail("Video preparation timed out.");
            brightness = Mathf.MoveTowards(brightness, IsPoweredOn ? 1 : 0,
                Time.unscaledDeltaTime / .28f);
            RefreshDisplay();
        }

        private void SetState(TelevisionState next)
        {
            State = next;
            statusLabel.text = next == TelevisionState.NoSignal ? "NO SIGNAL" :
                next == TelevisionState.Preparing ? "LOADING" :
                next == TelevisionState.Error ? "VIDEO ERROR" : string.Empty;
            RefreshDisplay();
            StateChanged?.Invoke(next);
        }

        private void RefreshDisplay()
        {
            screenProperties.SetFloat("_ScreenPower", brightness);
            screenProperties.SetFloat("_VideoReady",
                State == TelevisionState.Playing || State == TelevisionState.Paused ||
                State == TelevisionState.Ended ? 1 : 0);
            screenProperties.SetTexture("_VideoTex", videoTexture != null
                ? (Texture)videoTexture : Texture2D.blackTexture);
            screen.SetPropertyBlock(screenProperties);
            lightProperties.SetColor("_EmissionColor", IsPoweredOn
                ? new Color(.3f, 1f, .4f) : new Color(.2f, .015f, .01f));
            indicator.SetPropertyBlock(lightProperties);
        }

        private void PowerOff()
        {
            // Change state before Stop so late decoder callbacks cannot turn the TV on.
            SetState(TelevisionState.Off);
            StopDecoder();
            RefreshDisplay();
        }

        private void StopDecoder()
        {
            player.Stop();
            speaker.Stop();
            player.targetTexture = null;
            if (videoTexture == null) return;
            videoTexture.Release();
            Destroy(videoTexture);
            videoTexture = null;
        }

        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            if (paused && State == TelevisionState.Playing)
            {
                player.Pause();
                SetState(TelevisionState.Paused);
            }
            else if (!paused && State == TelevisionState.Paused)
            {
                player.Play();
                SetState(TelevisionState.Playing);
            }
            if (!paused && State == TelevisionState.Preparing) prepareStarted = Time.unscaledTime;
        }

        private void OnDisable()
        {
            if (player == null) return;
            brightness = 0;
            PowerOff();
        }

        private void OnDestroy()
        {
            if (player == null) return;
            player.prepareCompleted -= Prepared;
            player.errorReceived -= Failed;
            player.loopPointReached -= Ended;
        }
    }
}
