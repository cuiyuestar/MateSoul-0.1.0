using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent]
    public sealed class ElectricKettleController : MonoBehaviour
    {
        public const float BoilDurationSeconds = 60f;
        public const float BoilingTemperatureCelsius = 100f;
        private const float MinimumWaterLiters = .001f;
        private const int MaximumBaseOverlaps = 12;

        [SerializeField] private WaterReceiver water;
        [SerializeField] private Transform contactPoint;
        [SerializeField] private ParticleSystem steam;
        [SerializeField] private AudioClip buttonBeep;
        [SerializeField] private AudioClip operatingHum;
        [SerializeField] private AudioSource buttonAudio;
        [SerializeField] private AudioSource operatingAudio;
        [SerializeField, Min(.05f)] private float baseDetectionRadius = .12f;
        [SerializeField, Min(.02f)] private float contactScanInterval = .1f;

        private readonly Collider[] overlaps = new Collider[MaximumBaseOverlaps];
        private GameItem item;
        private PhysicsBody body;
        private ElectricKettleBase activeBase;
        private float heatedSeconds;
        private float scanElapsed;
        private bool powered;
        private bool heating;

        public bool IsPoweredOn => powered;
        public bool IsHeating => heating;
        public float HeatedSeconds => heatedSeconds;
        public float HeatingProgress => Mathf.Clamp01(
            heatedSeconds / BoilDurationSeconds);
        public WaterReceiver Water => water;
        public ElectricKettleBase ActiveBase => activeBase;
        public float BaseDetectionRadius => baseDetectionRadius;
        public bool IsDocked => body != null && body.IsDocked;
        public int SnapCount { get; private set; }
        public int ButtonSoundPlayCount { get; private set; }
        public event Action<bool> PowerChanged;
        public event Action<bool> HeatingChanged;

        public void Configure(WaterReceiver receiver, Transform kettleContact,
            ParticleSystem steamEffect, AudioClip beep, AudioClip hum,
            AudioSource oneShotSource, AudioSource loopSource)
        {
            if (receiver == null || kettleContact == null || steamEffect == null ||
                beep == null || hum == null || oneShotSource == null ||
                loopSource == null || oneShotSource == loopSource)
                throw new ArgumentException(
                    "Electric kettle bindings are incomplete.");
            water = receiver;
            contactPoint = kettleContact;
            steam = steamEffect;
            buttonBeep = beep;
            operatingHum = hum;
            buttonAudio = oneShotSource;
            operatingAudio = loopSource;
            ConfigureAudioSources();
        }

        private void Awake()
        {
            if (water == null || contactPoint == null || steam == null)
                throw new InvalidOperationException(
                    "Electric kettle prefab bindings are incomplete.");
            item = GetComponent<GameItem>();
            body = GetComponent<PhysicsBody>();
            if (body == null)
                throw new InvalidOperationException(
                    "Electric kettle requires a PhysicsBody.");
            EnsureAudio();
            SetSteam(false);
            RefreshOperatingAudio();
        }

        private void Update() => Advance(Time.deltaTime);

        public void AdvanceForTesting(float deltaSeconds) => Advance(deltaSeconds);

        public bool TogglePower()
        {
            if (powered)
            {
                SetPower(false);
                return true;
            }
            if (water.CurrentLiters <= MinimumWaterLiters)
            {
                ShowHint("请先给壶里加水");
                return false;
            }
            heatedSeconds = water.CurrentTemperatureCelsius >=
                             BoilingTemperatureCelsius - .01f
                ? BoilDurationSeconds
                : Mathf.InverseLerp(
                    WaterReceiver.AmbientTemperatureCelsius,
                    BoilingTemperatureCelsius,
                    water.CurrentTemperatureCelsius) * BoilDurationSeconds;
            SetPower(true);
            return true;
        }

        public void PlayButtonSound()
        {
            EnsureAudio();
            buttonAudio.PlayOneShot(buttonBeep);
            ButtonSoundPlayCount++;
        }

        public void RefreshContactForTesting() => RefreshDocking();

        private void Advance(float deltaSeconds)
        {
            scanElapsed += Mathf.Max(0, deltaSeconds);
            if (scanElapsed >= contactScanInterval)
            {
                scanElapsed = 0;
                RefreshDocking();
            }
            if (!powered)
            {
                SetHeating(false);
                return;
            }
            if (water.CurrentLiters <= MinimumWaterLiters)
            {
                SetPower(false);
                ShowHint("请先给壶里加水");
                return;
            }
            if (!heating) return;
            heatedSeconds = Mathf.Min(
                BoilDurationSeconds, heatedSeconds + Mathf.Max(0, deltaSeconds));
            water.SetTemperature(Mathf.Lerp(
                WaterReceiver.AmbientTemperatureCelsius,
                BoilingTemperatureCelsius,
                HeatingProgress));
            if (heatedSeconds < BoilDurationSeconds) return;
            water.SetTemperature(BoilingTemperatureCelsius);
            SetPower(false);
            PlayButtonSound();
            ShowHint("水已烧开");
        }

        private void SetPower(bool value)
        {
            if (powered == value) return;
            powered = value;
            scanElapsed = contactScanInterval;
            if (!powered)
                SetHeating(false);
            else RefreshDocking();
            PowerChanged?.Invoke(powered);
        }

        private void RefreshDocking()
        {
            if (body == null) body = GetComponent<PhysicsBody>();
            if (body == null || body.IsPaused) return;
            if (body.IsFollowing || body.IsInteractionControlled ||
                item != null && item.IsHeld)
            {
                activeBase = null;
                SetHeating(false);
                return;
            }

            var nearbyBase = FindNearbyBase();
            if (body.IsDocked)
            {
                if (nearbyBase == null)
                {
                    body.Undock();
                    activeBase = null;
                }
                else activeBase = nearbyBase;
            }
            else if (nearbyBase != null)
                SnapToBase(nearbyBase);
            else activeBase = null;
            SetHeating(powered && body.IsDocked && activeBase != null);
        }

        private ElectricKettleBase FindNearbyBase()
        {
            var count = Physics.OverlapSphereNonAlloc(
                contactPoint.position, baseDetectionRadius, overlaps,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            ElectricKettleBase best = null;
            var bestDistance = float.PositiveInfinity;
            for (var index = 0; index < count; index++)
            {
                var candidate =
                    overlaps[index].GetComponentInParent<ElectricKettleBase>();
                if (candidate == null || candidate.ContactPoint == null) continue;
                var delta = candidate.ContactPoint.position - contactPoint.position;
                if (delta.sqrMagnitude >= bestDistance) continue;
                best = candidate;
                bestDistance = delta.sqrMagnitude;
            }
            return best;
        }

        private void SnapToBase(ElectricKettleBase targetBase)
        {
            var localContact = transform.InverseTransformPoint(
                contactPoint.position);
            var targetRotation = targetBase.transform.rotation;
            var targetPosition = targetBase.ContactPoint.position -
                                 targetRotation * Vector3.Scale(
                                     localContact, transform.lossyScale);
            body.Dock(new Pose(targetPosition, targetRotation));
            activeBase = targetBase;
            SnapCount++;
            Physics.SyncTransforms();
        }

        private void SetHeating(bool value)
        {
            if (heating == value)
            {
                RefreshOperatingAudio();
                return;
            }
            heating = value;
            SetSteam(heating);
            RefreshOperatingAudio();
            HeatingChanged?.Invoke(heating);
        }

        private void SetSteam(bool value)
        {
            if (steam == null) return;
            if (value)
            {
                if (!steam.isPlaying) steam.Play(true);
            }
            else if (steam.isPlaying || steam.isEmitting)
                steam.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void ShowHint(string message)
        {
            if (item == null) item = GetComponent<GameItem>();
            item?.Session?.Notifications.ShowHint(message);
        }

        private void EnsureAudio()
        {
            if (buttonBeep == null)
                buttonBeep = Resources.Load<AudioClip>(
                    HeatingBaseController.ButtonBeepResource);
            if (operatingHum == null)
                operatingHum = Resources.Load<AudioClip>(
                    HeatingBaseController.OperatingHumResource);
            if (buttonBeep == null || operatingHum == null)
                throw new InvalidOperationException(
                    "Electric kettle audio resources are missing.");
            if (buttonAudio == null)
                buttonAudio = gameObject.AddComponent<AudioSource>();
            if (operatingAudio == null)
                operatingAudio = gameObject.AddComponent<AudioSource>();
            ConfigureAudioSources();
        }

        private void ConfigureAudioSources()
        {
            buttonAudio.clip = null;
            buttonAudio.playOnAwake = false;
            buttonAudio.loop = false;
            buttonAudio.spatialBlend = 1f;
            buttonAudio.dopplerLevel = 0;
            buttonAudio.minDistance = .2f;
            buttonAudio.maxDistance = 4f;
            buttonAudio.volume = .62f;

            operatingAudio.clip = operatingHum;
            operatingAudio.playOnAwake = false;
            operatingAudio.loop = true;
            operatingAudio.spatialBlend = 1f;
            operatingAudio.dopplerLevel = 0;
            operatingAudio.minDistance = .25f;
            operatingAudio.maxDistance = 5f;
            operatingAudio.volume = .32f;
        }

        private void RefreshOperatingAudio()
        {
            if (operatingAudio == null || operatingHum == null) return;
            if (heating)
            {
                if (!operatingAudio.isPlaying) operatingAudio.Play();
            }
            else if (operatingAudio.isPlaying) operatingAudio.Stop();
        }

        private void OnDisable()
        {
            powered = false;
            SetHeating(false);
            if (buttonAudio != null) buttonAudio.Stop();
            if (operatingAudio != null) operatingAudio.Stop();
        }
    }
}
