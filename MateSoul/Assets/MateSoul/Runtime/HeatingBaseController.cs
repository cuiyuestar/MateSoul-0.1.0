using System;
using TMPro;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent]
    public sealed class HeatingBaseController : MonoBehaviour
    {
        public const string ButtonBeepResource = "CookingEffects/ButtonBeep";
        public const string OperatingHumResource = "CookingEffects/InductionHum";
        public const int MinimumTemperature = 40;
        public const int MaximumTemperature = 100;
        public const int TemperatureStep = 10;
        public const int DefaultTemperature = 60;
        private const int MaximumOverlaps = 24;

        [SerializeField] private Transform heatingSurface;
        [SerializeField] private TMP_Text temperatureDisplay;
        [SerializeField] private Renderer displaySurface;
        [SerializeField] private AudioClip buttonBeep;
        [SerializeField] private AudioClip operatingHum;
        [SerializeField] private AudioSource buttonAudio;
        [SerializeField] private AudioSource operatingAudio;
        [SerializeField, Min(.01f)] private float heatingRadius = .115f;
        [SerializeField, Range(.1f, 1f)] private float minimumContactRatio = .55f;
        [SerializeField, Min(.05f)] private float scanInterval = .2f;
        [SerializeField] private int temperature = DefaultTemperature;

        private readonly Collider[] overlaps = new Collider[MaximumOverlaps];
        private MaterialPropertyBlock displayProperties;
        private CookwareHeatReceiver activeReceiver;
        private PhysicsBody body;
        private float elapsed;
        private float currentContactRatio;
        private bool powered;

        public bool IsPoweredOn => powered;
        public int Temperature => temperature;
        public float HeatingRadius => heatingRadius;
        public float MinimumContactRatio => minimumContactRatio;
        public float CurrentContactRatio => currentContactRatio;
        public Transform HeatingSurface => heatingSurface;
        public CookwareHeatReceiver ActiveReceiver => activeReceiver;
        public AudioSource ButtonAudio => buttonAudio;
        public AudioSource OperatingAudio => operatingAudio;
        public bool IsOperating => powered && activeReceiver != null &&
                                   (body == null || !body.IsPaused);
        public int ButtonSoundPlayCount { get; private set; }
        public event Action<bool> PowerChanged;
        public event Action<int> TemperatureChanged;

        public void Configure(Transform surface, float radius, TMP_Text display,
            Renderer screen)
        {
            if (surface == null || display == null || screen == null ||
                radius <= 0 || !PhysicsBody.IsFinite(radius))
                throw new ArgumentException(
                    "Heating base requires a surface, positive radius and display bindings.");
            heatingSurface = surface;
            heatingRadius = radius;
            temperatureDisplay = display;
            displaySurface = screen;
            RefreshDisplay();
        }

        public void ConfigureAudio(AudioClip beep, AudioClip hum,
            AudioSource oneShotSource, AudioSource loopSource)
        {
            if (beep == null || hum == null ||
                oneShotSource == null || loopSource == null ||
                oneShotSource == loopSource)
                throw new ArgumentException(
                    "Heating base audio bindings are incomplete.");
            buttonBeep = beep;
            operatingHum = hum;
            buttonAudio = oneShotSource;
            operatingAudio = loopSource;
            ConfigureAudioSources();
        }

        private void Awake()
        {
            if (heatingSurface == null || temperatureDisplay == null ||
                displaySurface == null)
                throw new InvalidOperationException(
                    "Heating base prefab bindings are incomplete.");
            body = GetComponent<PhysicsBody>();
            temperature = QuantizeTemperature(temperature);
            EnsureAudio();
            RefreshDisplay();
            RefreshOperatingAudio();
        }

        private void Update()
        {
            if (!powered || body != null && body.IsPaused)
            {
                elapsed = 0;
                if (activeReceiver != null) ClearReceiver();
                RefreshOperatingAudio();
                return;
            }
            elapsed += Time.deltaTime;
            if (elapsed < scanInterval) return;
            elapsed = 0;
            RefreshContact();
        }

        public void TogglePower() => SetPower(!powered);

        public void SetPower(bool value)
        {
            if (powered == value) return;
            powered = value;
            if (!powered) ClearReceiver();
            else RefreshContact();
            RefreshDisplay();
            RefreshOperatingAudio();
            PowerChanged?.Invoke(powered);
        }

        public void AdjustTemperature(int direction)
        {
            if (!powered || direction == 0) return;
            SetTemperature(temperature + Math.Sign(direction) * TemperatureStep);
        }

        public void SetTemperature(int value)
        {
            var next = QuantizeTemperature(value);
            if (temperature == next) return;
            temperature = next;
            if (activeReceiver != null)
                activeReceiver.SetHeatSource(this, temperature);
            RefreshDisplay();
            RefreshOperatingAudio();
            TemperatureChanged?.Invoke(temperature);
        }

        public void PlayButtonSound()
        {
            EnsureAudio();
            buttonAudio.PlayOneShot(buttonBeep);
            ButtonSoundPlayCount++;
        }

        public bool CanUse(HeatingBaseButtonKind kind)
        {
            if (kind == HeatingBaseButtonKind.Power) return true;
            if (!powered) return false;
            return kind == HeatingBaseButtonKind.Increase
                ? temperature < MaximumTemperature
                : temperature > MinimumTemperature;
        }

        public void RefreshContactForTesting() => RefreshContact();

        public float EvaluateContactRatio(CookwareHeatReceiver receiver)
        {
            if (receiver == null || heatingSurface == null) return 0;
            var up = transform.up;
            var alignment = Vector3.Dot(up, receiver.ContactNormal);
            if (alignment < Mathf.Cos(12f * Mathf.Deg2Rad)) return 0;
            var delta = receiver.ContactCenter - heatingSurface.position;
            var verticalGap = Vector3.Dot(delta, up);
            if (verticalGap < -.004f || verticalGap > .014f) return 0;
            var planar = delta - up * verticalGap;
            var area = CircleOverlapArea(
                heatingRadius, receiver.ContactRadius, planar.magnitude);
            var panArea = Mathf.PI * receiver.ContactRadius * receiver.ContactRadius;
            return panArea <= 0 ? 0 : Mathf.Clamp01(area / panArea);
        }

        public static float CircleOverlapArea(float firstRadius, float secondRadius,
            float centerDistance)
        {
            if (firstRadius <= 0 || secondRadius <= 0 ||
                !PhysicsBody.IsFinite(firstRadius) ||
                !PhysicsBody.IsFinite(secondRadius) ||
                !PhysicsBody.IsFinite(centerDistance))
                return 0;
            var distance = Mathf.Abs(centerDistance);
            if (distance >= firstRadius + secondRadius) return 0;
            if (distance <= Mathf.Abs(firstRadius - secondRadius))
            {
                var smaller = Mathf.Min(firstRadius, secondRadius);
                return Mathf.PI * smaller * smaller;
            }
            var firstAngle = 2f * Mathf.Acos(Mathf.Clamp(
                (distance * distance + firstRadius * firstRadius -
                 secondRadius * secondRadius) /
                (2f * distance * firstRadius), -1f, 1f));
            var secondAngle = 2f * Mathf.Acos(Mathf.Clamp(
                (distance * distance + secondRadius * secondRadius -
                 firstRadius * firstRadius) /
                (2f * distance * secondRadius), -1f, 1f));
            return .5f * firstRadius * firstRadius *
                   (firstAngle - Mathf.Sin(firstAngle)) +
                   .5f * secondRadius * secondRadius *
                   (secondAngle - Mathf.Sin(secondAngle));
        }

        private void RefreshContact()
        {
            if (!powered)
            {
                ClearReceiver();
                return;
            }
            var center = heatingSurface.position + transform.up * .009f;
            var halfExtents = new Vector3(heatingRadius, .022f, heatingRadius);
            var count = Physics.OverlapBoxNonAlloc(
                center, halfExtents, overlaps, transform.rotation,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            CookwareHeatReceiver best = null;
            var bestRatio = 0f;
            for (var index = 0; index < count; index++)
            {
                var receiver =
                    overlaps[index].GetComponentInParent<CookwareHeatReceiver>();
                if (receiver == null) continue;
                var ratio = EvaluateContactRatio(receiver);
                if (ratio <= bestRatio) continue;
                best = receiver;
                bestRatio = ratio;
            }
            if (bestRatio < minimumContactRatio) best = null;
            if (activeReceiver != best)
            {
                ClearReceiver();
                activeReceiver = best;
            }
            currentContactRatio = best == null ? 0 : bestRatio;
            if (best != null) best.SetHeatSource(this, temperature);
            RefreshOperatingAudio();
        }

        private void ClearReceiver()
        {
            if (activeReceiver != null)
                activeReceiver.ClearHeatSource(this);
            activeReceiver = null;
            currentContactRatio = 0;
            RefreshOperatingAudio();
        }

        private void EnsureAudio()
        {
            if (buttonBeep == null)
                buttonBeep = Resources.Load<AudioClip>(ButtonBeepResource);
            if (operatingHum == null)
                operatingHum = Resources.Load<AudioClip>(OperatingHumResource);
            if (buttonBeep == null || operatingHum == null)
                throw new InvalidOperationException(
                    "Heating base audio resources are missing.");
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
            buttonAudio.rolloffMode = AudioRolloffMode.Logarithmic;
            buttonAudio.minDistance = .25f;
            buttonAudio.maxDistance = 4f;
            buttonAudio.volume = .62f;

            operatingAudio.clip = operatingHum;
            operatingAudio.playOnAwake = false;
            operatingAudio.loop = true;
            operatingAudio.spatialBlend = 1f;
            operatingAudio.dopplerLevel = 0;
            operatingAudio.rolloffMode = AudioRolloffMode.Logarithmic;
            operatingAudio.minDistance = .35f;
            operatingAudio.maxDistance = 5f;
            operatingAudio.volume = .34f;
        }

        private void RefreshOperatingAudio()
        {
            if (operatingAudio == null || operatingHum == null) return;
            operatingAudio.pitch = Mathf.Lerp(
                .94f, 1.06f,
                Mathf.InverseLerp(
                    MinimumTemperature, MaximumTemperature, temperature));
            if (IsOperating)
            {
                if (!operatingAudio.isPlaying) operatingAudio.Play();
            }
            else if (operatingAudio.isPlaying) operatingAudio.Stop();
        }

        private void RefreshDisplay()
        {
            if (temperatureDisplay != null)
            {
                temperatureDisplay.text = powered
                    ? $"{temperature}°C"
                    : string.Empty;
                temperatureDisplay.color = powered
                    ? new Color(1f, .48f, .20f, 1f)
                    : new Color(.12f, .02f, .01f, .12f);
            }
            if (displaySurface == null) return;
            if (displayProperties == null)
                displayProperties = new MaterialPropertyBlock();
            displayProperties.SetColor(
                "_EmissionColor",
                powered
                    ? new Color(.12f, .006f, .002f)
                    : Color.black);
            displaySurface.SetPropertyBlock(displayProperties);
        }

        private static int QuantizeTemperature(int value)
        {
            var clamped = Mathf.Clamp(
                value, MinimumTemperature, MaximumTemperature);
            return Mathf.RoundToInt(clamped / (float)TemperatureStep) *
                   TemperatureStep;
        }

        private void OnDisable()
        {
            powered = false;
            ClearReceiver();
            if (buttonAudio != null) buttonAudio.Stop();
            if (operatingAudio != null) operatingAudio.Stop();
            RefreshDisplay();
        }
    }
}
