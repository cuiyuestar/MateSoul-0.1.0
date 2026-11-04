using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent]
    public sealed class WaterReceiver : MonoBehaviour
    {
        public const float AmbientTemperatureCelsius = 20f;
        private const float MinimumVisibleHeight = .0008f;

        [SerializeField, Min(.01f)] private float capacityLiters = .3f;
        [SerializeField, Min(.005f)] private float openingRadius = .03f;
        [SerializeField] private Vector3 openingCenter;
        [SerializeField, Range(5f, 75f)] private float maximumTiltDegrees = 38f;
        [SerializeField] private Transform waterVisual;
        [SerializeField] private Vector3 fillCenter;
        [SerializeField, Min(.001f)] private float maximumWaterHeight = .04f;
        [SerializeField, Min(0f)] private float currentLiters;
        [SerializeField] private float currentTemperatureCelsius =
            AmbientTemperatureCelsius;

        private Vector3 waterVisualScale;
        private bool initialized;
        private static readonly HashSet<WaterReceiver> receivers = new HashSet<WaterReceiver>();
        private FluidStream spill;
        private PhysicsBody body;
        private float dryMass;
        public Color LiquidColor { get; set; } = new Color(.35f, .78f, .95f, .55f);

        public float CapacityLiters => capacityLiters;
        public float CurrentLiters => currentLiters;
        public float CurrentTemperatureCelsius => currentTemperatureCelsius;
        public float FillRatio => capacityLiters <= 0
            ? 0
            : Mathf.Clamp01(currentLiters / capacityLiters);
        public float OpeningRadius => openingRadius;
        public Vector3 OpeningCenter => openingCenter;
        public Transform WaterVisual => waterVisual;
        public event Action<WaterReceiver> ContentsChanged;
        public event Action<WaterReceiver> Emptied;

        public void Configure(float capacity, float radius, Vector3 opening,
            Vector3 fillBase, float fillHeight, Transform visual)
        {
            if (!PhysicsBody.IsFinite(capacity) || capacity <= 0 ||
                !PhysicsBody.IsFinite(radius) || radius < .005f ||
                !PhysicsBody.IsFinite(fillHeight) || fillHeight < .001f ||
                visual == null || visual == transform || !visual.IsChildOf(transform))
                throw new ArgumentException(
                    "Water receivers require finite capacity, opening, fill height and child visual.");
            capacityLiters = capacity;
            openingRadius = radius;
            openingCenter = opening;
            fillCenter = fillBase;
            maximumWaterHeight = fillHeight;
            waterVisual = visual;
            currentLiters = 0;
            currentTemperatureCelsius = AmbientTemperatureCelsius;
            initialized = false;
            InitializeVisual();
        }

        private void Awake()
        {
            ValidateConfiguration();
            InitializeVisual();
            body = GetComponent<PhysicsBody>();
            dryMass = body != null ? body.Rigidbody.mass : 0;
        }

        private void OnEnable() => receivers.Add(this);
        private void OnDisable() => receivers.Remove(this);

        public static WaterReceiver FindCrossedOpening(Vector3 from, Vector3 to,
            WaterReceiver ignored, out Vector3 entry, out float fraction)
        {
            entry = default;
            fraction = float.PositiveInfinity;
            WaterReceiver best = null;
            foreach (var receiver in receivers)
            {
                if (receiver == null || receiver == ignored) continue;
                var opening = receiver.transform.TransformPoint(receiver.openingCenter);
                var normal = receiver.transform.up;
                var a = Vector3.Dot(from - opening, normal);
                var b = Vector3.Dot(to - opening, normal);
                if (a < 0 || b > 0 || a - b < .000001f) continue;
                var amount = a / (a - b);
                var point = Vector3.Lerp(from, to, amount);
                if (amount >= fraction || !receiver.CanReceiveAt(point)) continue;
                fraction = amount;
                entry = point;
                best = receiver;
            }
            return best;
        }

        private void ValidateConfiguration()
        {
            if (!PhysicsBody.IsFinite(capacityLiters) || capacityLiters <= 0 ||
                !PhysicsBody.IsFinite(openingRadius) || openingRadius < .005f ||
                !PhysicsBody.IsFinite(maximumWaterHeight) || maximumWaterHeight < .001f ||
                waterVisual == null || !waterVisual.IsChildOf(transform))
                throw new InvalidOperationException("Water receiver bindings are incomplete.");
        }

        private void InitializeVisual()
        {
            if (waterVisual == null) return;
            waterVisualScale = waterVisual.localScale;
            // Imported vessels can be offset from the item root by handle-inclusive
            // centering. Bind water to the actual vessel mesh, not a catalog offset.
            var vessel = GetComponentsInChildren<Renderer>(true)
                .Where(value => value.transform != waterVisual &&
                    value.name.IndexOf("handle", StringComparison.OrdinalIgnoreCase) < 0 &&
                    (value.name.IndexOf("body", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     value.name.IndexOf("cup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     value.name.IndexOf("bowl", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     value.name.IndexOf("plate", StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderByDescending(value => value.bounds.size.x * value.bounds.size.z).FirstOrDefault();
            if (vessel != null)
            {
                var center = transform.InverseTransformPoint(vessel.bounds.center);
                openingCenter.x = fillCenter.x = center.x;
                openingCenter.z = fillCenter.z = center.z;
            }
            initialized = true;
            ApplyVisual();
        }

        private void Update()
        {
            if (currentLiters <= 0 || body != null && body.IsPaused ||
                GetComponent<LiquidPourer>() != null) return;
            var tilt = Vector3.Dot(transform.up, Vector3.up);
            if (tilt >= Mathf.Cos(maximumTiltDegrees * Mathf.Deg2Rad)) return;
            if (spill == null)
            {
                spill = GetComponent<FluidStream>() ?? gameObject.AddComponent<FluidStream>();
                spill.Configure(waterVisual.GetComponent<Renderer>().sharedMaterial);
            }
            var downhill = Vector3.ProjectOnPlane(Vector3.down, transform.up).normalized;
            if (downhill.sqrMagnitude < .01f) downhill = transform.forward;
            var point = transform.TransformPoint(openingCenter) + downhill * openingRadius;
            var amount = Mathf.Min(currentLiters, capacityLiters * (1f - Mathf.Max(0, tilt)) * Time.deltaTime);
            var emitted = spill.Emit(point, downhill * .25f, amount, currentTemperatureCelsius, LiquidColor);
            if (emitted > 0) Drain(emitted);
        }

        public bool CanReceiveAt(Vector3 worldPoint)
        {
            var upright = Vector3.Dot(transform.up, Vector3.up);
            if (upright < Mathf.Cos(maximumTiltDegrees * Mathf.Deg2Rad)) return false;
            var local = transform.InverseTransformPoint(worldPoint) - openingCenter;
            return local.x * local.x + local.z * local.z <=
                   openingRadius * openingRadius &&
                   Mathf.Abs(local.y) <= Mathf.Max(.08f, maximumWaterHeight * 2f);
        }

        public float Receive(float requestedLiters, Vector3 worldPoint)
            => Receive(
                requestedLiters, AmbientTemperatureCelsius, worldPoint);

        public float Receive(float requestedLiters,
            float incomingTemperatureCelsius, Vector3 worldPoint)
        {
            if (!PhysicsBody.IsFinite(requestedLiters) || requestedLiters <= 0 ||
                !PhysicsBody.IsFinite(incomingTemperatureCelsius) ||
                !CanReceiveAt(worldPoint))
                return 0;
            var accepted = Mathf.Min(requestedLiters, capacityLiters - currentLiters);
            if (accepted <= 0) return 0;
            var incomingTemperature = Mathf.Max(
                AmbientTemperatureCelsius, incomingTemperatureCelsius);
            currentTemperatureCelsius = currentLiters <= 0
                ? incomingTemperature
                : (currentTemperatureCelsius * currentLiters +
                   incomingTemperature * accepted) /
                  (currentLiters + accepted);
            currentLiters += accepted;
            ApplyVisual();
            ContentsChanged?.Invoke(this);
            return accepted;
        }

        public float Drain(float requestedLiters)
        {
            if (!PhysicsBody.IsFinite(requestedLiters) || requestedLiters <= 0)
                return 0;
            var drained = Mathf.Min(requestedLiters, currentLiters);
            if (drained <= 0) return 0;
            currentLiters -= drained;
            if (currentLiters <= .000001f)
            {
                currentLiters = 0;
                currentTemperatureCelsius = AmbientTemperatureCelsius;
            }
            ApplyVisual();
            ContentsChanged?.Invoke(this);
            if (currentLiters <= 0) Emptied?.Invoke(this);
            return drained;
        }

        public float TransferTo(WaterReceiver target, float requestedLiters,
            Vector3 targetWorldPoint)
        {
            if (target == null || target == this ||
                !PhysicsBody.IsFinite(requestedLiters) ||
                requestedLiters <= 0 || currentLiters <= 0)
                return 0;
            var available = Mathf.Min(requestedLiters, currentLiters);
            var accepted = target.Receive(
                available, currentTemperatureCelsius, targetWorldPoint);
            return accepted <= 0 ? 0 : Drain(accepted);
        }

        public void Empty()
        {
            if (currentLiters <= 0)
            {
                currentTemperatureCelsius = AmbientTemperatureCelsius;
                ApplyVisual();
                Emptied?.Invoke(this);
                return;
            }
            currentLiters = 0;
            currentTemperatureCelsius = AmbientTemperatureCelsius;
            ApplyVisual();
            ContentsChanged?.Invoke(this);
            Emptied?.Invoke(this);
        }

        public void SetTemperature(float celsius)
        {
            if (!PhysicsBody.IsFinite(celsius))
                throw new ArgumentException(
                    "Water temperature must be finite.", nameof(celsius));
            currentTemperatureCelsius = Mathf.Max(
                AmbientTemperatureCelsius, celsius);
            ContentsChanged?.Invoke(this);
        }

        private void ApplyVisual()
        {
            if (!initialized || waterVisual == null) return;
            var ratio = FillRatio;
            if (body != null)
            {
                var item = GetComponent<GameItem>();
                var emptyMass = item != null && item.Definition != null ? item.Definition.Physics.mass : dryMass;
                if (emptyMass > 0) body.Rigidbody.mass = emptyMass + currentLiters;
            }
            waterVisual.gameObject.SetActive(ratio > .0001f);
            if (ratio <= .0001f) return;
            var height = Mathf.Max(MinimumVisibleHeight, maximumWaterHeight * ratio);
            waterVisual.localScale = new Vector3(
                waterVisualScale.x,
                height * .5f,
                waterVisualScale.z);
            waterVisual.localPosition = fillCenter + Vector3.up * height * .5f;
        }

#if UNITY_EDITOR
        public void SetFillForTesting(float ratio)
        {
            currentLiters = capacityLiters * Mathf.Clamp01(ratio);
            if (currentLiters <= 0)
                currentTemperatureCelsius = AmbientTemperatureCelsius;
            ApplyVisual();
        }
#endif
    }
}
