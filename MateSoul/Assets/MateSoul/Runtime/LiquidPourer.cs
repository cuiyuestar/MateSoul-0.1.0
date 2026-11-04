using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent, RequireComponent(typeof(PhysicsBody))]
    public sealed class LiquidPourer : MonoBehaviour
    {
        [SerializeField] private WaterReceiver source;
        [SerializeField] private Transform outlet;
        [SerializeField] private Renderer streamRenderer;
        [SerializeField, Min(.01f)] private float flowLitersPerSecond = .24f;
        [SerializeField, Range(5f, 89f)] private float minimumTiltDegrees = 52f;
        private PhysicsBody body;
        private FluidStream fluid;
        private double emissionTime;

        public WaterReceiver Source => source;
        public Transform Outlet => outlet;
        public Renderer StreamRenderer => streamRenderer;
        public float FlowLitersPerSecond => flowLitersPerSecond;
        public float MinimumTiltDegrees => minimumTiltDegrees;
        public bool IsPouring { get; private set; }
        public float TransferredLiters { get; private set; }

        public void Configure(WaterReceiver waterSource, Transform pourOutlet,
            Renderer stream, float flowRate = .24f, float tiltDegrees = 52f,
            float dropDistance = .65f)
        {
            if (waterSource == null || pourOutlet == null || stream == null ||
                !pourOutlet.IsChildOf(transform) ||
                !stream.transform.IsChildOf(transform) ||
                !PhysicsBody.IsFinite(flowRate) || flowRate <= 0 ||
                !PhysicsBody.IsFinite(tiltDegrees) ||
                tiltDegrees < 5f || tiltDegrees > 89f ||
                !PhysicsBody.IsFinite(dropDistance) || dropDistance < .05f)
                throw new ArgumentException(
                    "Liquid pourer bindings and limits are incomplete.");
            source = waterSource;
            outlet = pourOutlet;
            streamRenderer = stream;
            flowLitersPerSecond = flowRate;
            minimumTiltDegrees = tiltDegrees;
            streamRenderer.gameObject.SetActive(false);
        }

        private void Awake()
        {
            if (source == null || outlet == null || streamRenderer == null)
                throw new InvalidOperationException(
                    "Liquid pourer bindings are incomplete.");
            body = GetComponent<PhysicsBody>();
            streamRenderer.gameObject.SetActive(false);
            fluid = GetComponent<FluidStream>() ?? gameObject.AddComponent<FluidStream>();
            fluid.Configure(streamRenderer.sharedMaterial);
        }

        private void Update() => Advance(Time.deltaTime);

        public void AdvanceForTesting(float deltaSeconds)
        {
            for (var remaining = deltaSeconds; remaining > 0; remaining -= .02f)
            {
                var step = Mathf.Min(.02f, remaining);
                Advance(step);
                fluid?.Advance(step);
            }
        }

        public float TryPourInto(WaterReceiver target, float requestedLiters,
            Vector3 targetWorldPoint)
        {
            var transferred = source.TransferTo(
                target, requestedLiters, targetWorldPoint);
            TransferredLiters += transferred;
            return transferred;
        }

        private void Advance(float deltaSeconds)
        {
            if (!PhysicsBody.IsFinite(deltaSeconds) || deltaSeconds <= 0 ||
                source.CurrentLiters <= 0 ||
                body != null && body.IsPaused ||
                Vector3.Dot(transform.up, Vector3.up) >
                Mathf.Cos(minimumTiltDegrees * Mathf.Deg2Rad))
            {
                IsPouring = false;
                emissionTime = 0;
                if (streamRenderer != null) streamRenderer.gameObject.SetActive(false);
                return;
            }

            if (fluid == null)
            {
                fluid = GetComponent<FluidStream>() ?? gameObject.AddComponent<FluidStream>();
                fluid.Configure(streamRenderer.sharedMaterial);
            }
            streamRenderer.gameObject.SetActive(false);
            emissionTime += deltaSeconds;
            IsPouring = true;
            while (emissionTime + .000001 >= .02 && source.CurrentLiters > 0)
            {
                emissionTime = Math.Max(0, emissionTime - .02);
                var amount = Mathf.Min(source.CurrentLiters, flowLitersPerSecond * .02f);
                var emitted = fluid.Emit(outlet.position, Vector3.down * .2f,
                    amount, source.CurrentTemperatureCelsius, source.LiquidColor);
                if (emitted > 0) source.Drain(emitted);
            }
            TransferredLiters = fluid.ReceivedLiters;
        }

        private void OnDisable()
        {
            if (streamRenderer != null)
                streamRenderer.gameObject.SetActive(false);
            IsPouring = false;
        }
    }
}
