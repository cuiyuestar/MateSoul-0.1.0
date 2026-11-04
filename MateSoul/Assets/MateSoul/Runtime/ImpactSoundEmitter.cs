using System;
using UnityEngine;
using UnityEngine.Networking;

namespace MateSoul.Runtime
{
    public enum ImpactSoundSeverity
    {
        None,
        Light,
        Medium,
        Heavy
    }

    [Serializable]
    public struct ImpactSoundSettings
    {
        public bool enabled;
        [Min(1f)] public float lightForceThreshold;
        [Min(1f)] public float mediumForceThreshold;
        [Min(1f)] public float heavyForceThreshold;
        public AudioClip sound;

        public ImpactSoundSettings(float lightForce, float mediumForce, float heavyForce,
            AudioClip clip)
        {
            enabled = true;
            lightForceThreshold = lightForce;
            mediumForceThreshold = mediumForce;
            heavyForceThreshold = heavyForce;
            sound = clip;
        }

        public void Validate()
        {
            if (!enabled) return;
            if (!PhysicsBody.IsFinite(lightForceThreshold) || lightForceThreshold <= 0 ||
                !PhysicsBody.IsFinite(mediumForceThreshold) ||
                mediumForceThreshold <= lightForceThreshold ||
                !PhysicsBody.IsFinite(heavyForceThreshold) ||
                heavyForceThreshold <= mediumForceThreshold || sound == null)
                throw new ArgumentException(
                    "Impact audio requires ordered force thresholds and a sound clip.");
        }
    }

    [DisallowMultipleComponent, RequireComponent(typeof(GameItem))]
    public sealed class ImpactSoundEmitter : MonoBehaviour
    {
        private const float ArmingDelay = .25f;
        private const float Cooldown = .18f;

        private GameItem item;
        private PhysicsBody body;
        private ImpactSoundSettings settings;
        private AudioSource source;
        private float armedAt;
        private float nextAllowedAt;
        private bool pausedPlayback;

        public AudioSource Source => source;
        public ImpactSoundSeverity LastSeverity { get; private set; }
        public float LastForce { get; private set; }
        public float LastVolume { get; private set; }
        public int PlayCount { get; private set; }

        public void Configure(GameItem owner, ImpactSoundSettings value)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            value.Validate();
            if (!value.enabled)
                throw new ArgumentException("ImpactSoundEmitter requires enabled settings.");
            item = owner;
            body = owner.Body;
            settings = value;
            armedAt = Time.fixedTime + ArmingDelay;
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = .2f;
            source.spread = 25f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = .35f;
            source.maxDistance = 7f;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision == null) return;
            var other = collision.collider.GetComponentInParent<ImpactSoundEmitter>();
            if (other != null && other != this && GetInstanceID() > other.GetInstanceID()) return;
            var force = collision.impulse.magnitude / Mathf.Max(Time.fixedDeltaTime, .001f);
            HandleImpact(force);
        }

        private void HandleImpact(float force)
        {
            if (item == null || body == null || source == null ||
                Time.fixedTime < armedAt || Time.unscaledTime < nextAllowedAt ||
                !PhysicsBody.IsFinite(force) || force < settings.lightForceThreshold ||
                item.IsHeld || item.IsReserved || body.IsFollowing || body.IsPaused || body.IsContact)
                return;

            var severity = force >= settings.heavyForceThreshold
                ? ImpactSoundSeverity.Heavy
                : force >= settings.mediumForceThreshold
                    ? ImpactSoundSeverity.Medium
                    : ImpactSoundSeverity.Light;
            var volume = severity == ImpactSoundSeverity.Heavy
                ? .95f
                : severity == ImpactSoundSeverity.Medium ? .72f : .46f;
            var pitch = severity == ImpactSoundSeverity.Heavy
                ? .94f
                : severity == ImpactSoundSeverity.Medium ? 1f : 1.04f;
            source.Stop();
            source.clip = settings.sound;
            source.volume = volume;
            source.pitch = pitch;
            source.Play();
            LastSeverity = severity;
            LastForce = force;
            LastVolume = volume;
            PlayCount++;
            nextAllowedAt = Time.unscaledTime + Cooldown;
#region debug-point C:impact-audio
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            if (PlayCount <= 20 || PlayCount % 25 == 0)
            {
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                var payload =
                    $"{{\"sessionId\":\"emulator-memory-growth\",\"runId\":\"interaction-correlation\"," +
                    $"\"hypothesisId\":\"C\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                    $"\"location\":\"ImpactSoundEmitter.HandleImpact\",\"msg\":\"[DEBUG] impact audio played\"," +
                    $"\"data\":{{\"definition\":\"{item.Definition.Id}\",\"count\":{PlayCount}," +
                    $"\"severity\":\"{severity}\",\"force\":{force.ToString("R", culture)}}}}}";
                var request = UnityWebRequest.Post(
                    "http://127.0.0.1:7791/event", payload, "application/json");
                var operation = request.SendWebRequest();
                operation.completed += _ => request.Dispose();
            }
#endif
#endregion
        }

        private void Update()
        {
            if (body == null || source == null) return;
            if (body.IsPaused && source.isPlaying)
            {
                source.Pause();
                pausedPlayback = true;
            }
            else if (!body.IsPaused && pausedPlayback)
            {
                source.UnPause();
                pausedPlayback = false;
            }
        }

        private void OnDisable()
        {
            pausedPlayback = false;
            if (source != null) source.Stop();
        }

#if UNITY_EDITOR
        public void ApplyImpactForTesting(float force)
        {
            armedAt = 0;
            HandleImpact(force);
        }
#endif
    }
}
