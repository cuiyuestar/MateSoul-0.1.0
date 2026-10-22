using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace MateSoul.Runtime
{
    /// <summary>Owns one radio instance's power, station mixing and seamless loop fades.</summary>
    public sealed class RadioController : MonoBehaviour
    {
        private enum TransitionKind
        {
            None,
            Station,
            Loop
        }

        [SerializeField] private AudioClip[] stations = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip tuningNoise;
        [SerializeField] private AudioSource channelA;
        [SerializeField] private AudioSource channelB;
        [SerializeField] private AudioSource noiseChannel;
        [SerializeField] private RadioKnobControl tuningKnob;
        [SerializeField] private RadioKnobControl volumeKnob;
        [SerializeField, Range(0, 1)] private float tuning = .5f;
        [SerializeField, Range(0, 1)] private float volume = .38f;
        [SerializeField, Min(.1f)] private float stationCrossfadeDuration = 1.8f;
        [SerializeField, Min(.1f)] private float loopCrossfadeDuration = 2.4f;
        [SerializeField, Min(.05f)] private float powerFadeDuration = .3f;

        private AudioSource activeChannel;
        private AudioSource transitionFrom;
        private AudioSource transitionTo;
        private PhysicsBody body;
        private TransitionKind transitionKind;
        private float transitionElapsed;
        private float transitionDuration;
        private float powerGain;
        private int stationIndex = 1;
        private int desiredStationIndex = 1;
        private int transitionStationIndex = -1;
        private bool powered;
        private bool applicationPaused;
        private bool playbackPaused;
        private bool resumeA;
        private bool resumeB;
        private bool resumeNoise;

        public bool IsPoweredOn => powered;
        public int StationIndex => stationIndex;
        public int DesiredStationIndex => desiredStationIndex;
        public float Tuning => tuning;
        public float Volume => volume;
        public float OutputVolume => Mathf.Pow(volume, 2f) * .9f;
        public IReadOnlyList<AudioClip> Stations => stations;
        public AudioSource ActiveChannel => activeChannel;
        public AudioSource TransitionChannel => transitionTo;
        public AudioSource NoiseChannel => noiseChannel;
        public bool IsTuningTransition => transitionKind == TransitionKind.Station;
        public bool IsLoopTransition => transitionKind == TransitionKind.Loop;
        public float TransitionProgress => transitionKind == TransitionKind.None
            ? 1f
            : Mathf.Clamp01(transitionElapsed / transitionDuration);
        public event Action<bool> PowerChanged;
        public event Action<int> StationChanged;

        public void Configure(AudioClip[] stationClips, AudioClip staticClip,
            AudioSource first, AudioSource second, AudioSource noise,
            RadioKnobControl stationControl, RadioKnobControl volumeControl)
        {
            if (stationClips == null || stationClips.Length < 2 ||
                Array.Exists(stationClips, clip => clip == null) || staticClip == null ||
                first == null || second == null || noise == null ||
                first == second || first == noise || second == noise)
                throw new ArgumentException(
                    "Radio requires at least two stations, tuning noise and three unique sources.");
            stations = (AudioClip[])stationClips.Clone();
            tuningNoise = staticClip;
            channelA = first;
            channelB = second;
            noiseChannel = noise;
            tuningKnob = stationControl;
            volumeKnob = volumeControl;
            noiseChannel.clip = tuningNoise;
            stationIndex = desiredStationIndex =
                Mathf.Clamp(Mathf.RoundToInt(tuning * (stations.Length - 1)),
                    0, stations.Length - 1);
        }

        private void Awake()
        {
            if (stations == null || stations.Length < 2 ||
                Array.Exists(stations, clip => clip == null) || tuningNoise == null ||
                channelA == null || channelB == null || noiseChannel == null)
                throw new InvalidOperationException("Radio prefab bindings are incomplete.");
            body = GetComponent<PhysicsBody>();
            ConfigureMusicSource(channelA);
            ConfigureMusicSource(channelB);
            ConfigureNoiseSource(noiseChannel);
            noiseChannel.clip = tuningNoise;
            stationIndex = desiredStationIndex =
                Mathf.Clamp(Mathf.RoundToInt(tuning * (stations.Length - 1)),
                    0, stations.Length - 1);
        }

        private void Start()
        {
            if (tuningKnob != null) tuningKnob.SetNormalizedValue(tuning, false);
            if (volumeKnob != null) volumeKnob.SetNormalizedValue(volume, false);
        }

        public void TogglePower() => SetPower(!powered);

        public void SetPower(bool value)
        {
            if (powered == value) return;
            powered = value;
            if (powered)
            {
                stationIndex = desiredStationIndex;
                StartSteadyPlayback(stationIndex);
            }
            else
            {
                KeepDominantTransitionChannel();
                transitionKind = TransitionKind.None;
                transitionTo = transitionFrom = null;
                noiseChannel.Stop();
                noiseChannel.volume = 0;
            }
            PowerChanged?.Invoke(powered);
        }

        public void SetTuning(float value)
        {
            tuning = Mathf.Clamp01(value);
            desiredStationIndex = Mathf.Clamp(
                Mathf.RoundToInt(tuning * (stations.Length - 1)), 0, stations.Length - 1);
            if (!powered)
            {
                stationIndex = desiredStationIndex;
                return;
            }
            if (transitionKind == TransitionKind.None && desiredStationIndex != stationIndex)
                StartNextStationTransition();
        }

        public void SetVolume(float value)
        {
            volume = Mathf.Clamp01(value);
            ApplyMix();
        }

        private void Update()
        {
            var shouldPause = applicationPaused || body != null && body.IsPaused;
            if (shouldPause != playbackPaused) SetPlaybackPaused(shouldPause);
            if (playbackPaused) return;

            powerGain = Mathf.MoveTowards(powerGain, powered ? 1f : 0f,
                Time.unscaledDeltaTime / powerFadeDuration);
            if (!powered && powerGain <= 0)
            {
                StopAll();
                return;
            }

            if (transitionKind != TransitionKind.None)
            {
                transitionElapsed += Time.unscaledDeltaTime;
                if (transitionElapsed >= transitionDuration) CompleteTransition();
            }
            else if (powered)
            {
                if (desiredStationIndex != stationIndex) StartNextStationTransition();
                else TryStartLoopTransition();
            }
            ApplyMix();
        }

        private void StartSteadyPlayback(int index)
        {
            StopAll();
            activeChannel = channelA;
            activeChannel.clip = stations[index];
            activeChannel.time = 0;
            activeChannel.Play();
            ApplyMix();
        }

        private void StartNextStationTransition()
        {
            var next = stationIndex + Math.Sign(desiredStationIndex - stationIndex);
            StartTransition(TransitionKind.Station, next, stationCrossfadeDuration);
        }

        private void TryStartLoopTransition()
        {
            if (activeChannel == null || !activeChannel.isPlaying || activeChannel.clip == null)
                return;
            var fade = Mathf.Min(loopCrossfadeDuration, activeChannel.clip.length * .25f);
            if (activeChannel.clip.length - activeChannel.time > fade) return;
            StartTransition(TransitionKind.Loop, stationIndex, fade);
        }

        private void StartTransition(TransitionKind kind, int targetStation, float duration)
        {
            if (activeChannel == null)
            {
                stationIndex = targetStation;
                StartSteadyPlayback(stationIndex);
                return;
            }
            transitionKind = kind;
            transitionElapsed = 0;
            transitionDuration = Mathf.Max(.1f, duration);
            transitionStationIndex = targetStation;
            transitionFrom = activeChannel;
            transitionTo = activeChannel == channelA ? channelB : channelA;
            transitionTo.Stop();
            transitionTo.clip = stations[targetStation];
            transitionTo.time = 0;
            transitionTo.volume = 0;
            transitionTo.Play();
            if (kind == TransitionKind.Station)
            {
                noiseChannel.time = 0;
                noiseChannel.Play();
            }
        }

        private void CompleteTransition()
        {
            var kind = transitionKind;
            transitionFrom.Stop();
            transitionFrom.volume = 0;
            activeChannel = transitionTo;
            transitionFrom = transitionTo = null;
            transitionKind = TransitionKind.None;
            if (kind == TransitionKind.Station)
            {
                stationIndex = transitionStationIndex;
                noiseChannel.Stop();
                noiseChannel.volume = 0;
                StationChanged?.Invoke(stationIndex);
            }
            transitionStationIndex = -1;
        }

        private void KeepDominantTransitionChannel()
        {
            if (transitionKind == TransitionKind.None) return;
            var keepIncoming = TransitionProgress >= .5f;
            var keep = keepIncoming ? transitionTo : transitionFrom;
            var discard = keepIncoming ? transitionFrom : transitionTo;
            discard.Stop();
            discard.volume = 0;
            activeChannel = keep;
            if (keepIncoming && transitionKind == TransitionKind.Station)
                stationIndex = transitionStationIndex;
        }

        private void ApplyMix()
        {
            if (channelA == null || channelB == null || noiseChannel == null) return;
            var output = OutputVolume * powerGain;
            if (transitionKind == TransitionKind.None)
            {
                channelA.volume = activeChannel == channelA ? output : 0;
                channelB.volume = activeChannel == channelB ? output : 0;
                noiseChannel.volume = 0;
                return;
            }
            var progress = TransitionProgress;
            transitionFrom.volume = output * Mathf.Cos(progress * Mathf.PI * .5f);
            transitionTo.volume = output * Mathf.Sin(progress * Mathf.PI * .5f);
            noiseChannel.volume = transitionKind == TransitionKind.Station
                ? output * .52f * Mathf.Sin(progress * Mathf.PI)
                : 0;
        }

        private void SetPlaybackPaused(bool value)
        {
            playbackPaused = value;
            if (value)
            {
                resumeA = channelA.isPlaying;
                resumeB = channelB.isPlaying;
                resumeNoise = noiseChannel.isPlaying;
                if (resumeA) channelA.Pause();
                if (resumeB) channelB.Pause();
                if (resumeNoise) noiseChannel.Pause();
            }
            else
            {
                if (resumeA) channelA.UnPause();
                if (resumeB) channelB.UnPause();
                if (resumeNoise) noiseChannel.UnPause();
                resumeA = resumeB = resumeNoise = false;
            }
        }

        private void StopAll()
        {
            channelA.Stop();
            channelB.Stop();
            noiseChannel.Stop();
            channelA.volume = channelB.volume = noiseChannel.volume = 0;
            activeChannel = null;
            transitionFrom = transitionTo = null;
            transitionKind = TransitionKind.None;
            transitionStationIndex = -1;
        }

        private static void ConfigureMusicSource(AudioSource source)
        {
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0;
            source.spread = 35f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = .45f;
            source.maxDistance = 9f;
            source.volume = 0;
        }

        private static void ConfigureNoiseSource(AudioSource source)
        {
            ConfigureMusicSource(source);
            source.spread = 20f;
        }

        private void OnApplicationPause(bool value) => applicationPaused = value;

        private void OnDisable()
        {
            powered = false;
            powerGain = 0;
            playbackPaused = false;
            resumeA = resumeB = resumeNoise = false;
            if (channelA != null && channelB != null && noiseChannel != null) StopAll();
        }

#if UNITY_EDITOR
        public void SetPlaybackTimeForTesting(float seconds)
        {
            if (activeChannel == null || activeChannel.clip == null)
                throw new InvalidOperationException("Radio is not playing.");
            activeChannel.time = Mathf.Clamp(seconds, 0, activeChannel.clip.length - .01f);
        }

        public void SetTransitionDurationsForTesting(float stationSeconds, float loopSeconds)
        {
            stationCrossfadeDuration = Mathf.Max(.1f, stationSeconds);
            loopCrossfadeDuration = Mathf.Max(.1f, loopSeconds);
        }
#endif
    }
}
