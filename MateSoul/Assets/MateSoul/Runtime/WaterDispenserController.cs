using System;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent]
    public sealed class WaterDispenserController : MonoBehaviour, IRuntimeWorldFactSource
    {
        [SerializeField] private Transform outlet;
        [SerializeField] private Transform dripTrayTarget;
        [SerializeField] private Transform waterStream;
        [SerializeField] private Renderer streamRenderer;
        [SerializeField] private Renderer[] puddleRenderers = Array.Empty<Renderer>();
        [SerializeField, Min(.01f)] private float flowLitersPerSecond = .18f;
        [SerializeField, Min(.5f)] private float maximumRunSeconds = 8f;
        private PhysicsBody body;
        private FluidStream fluid;
        private double emissionTime;
        private float runRemaining;
        private bool dispensing;
        public bool IsDispensing => dispensing;
        public float RunRemaining => runRemaining;
        public float FlowLitersPerSecond => flowLitersPerSecond;
        public int PuddlePoolSize => puddleRenderers.Length;
        public int ActivePuddleCount => fluid != null ? fluid.SettledCount : 0;
        public Transform Outlet => outlet;
        public Transform WaterStream => waterStream;
        public FluidStream Fluid => fluid;
        public event Action<bool> DispensingChanged;

        public bool TryCaptureRuntimeWorldFact(out RuntimeWorldFactSnapshot fact)
        {
            fact = !dispensing ? null : RuntimeWorldFact.Create(this, "appliance", "dispensing",
                $"remaining_seconds={Mathf.CeilToInt(runRemaining)};puddles={ActivePuddleCount}");
            return fact != null;
        }

        public void Configure(Transform waterOutlet, Transform trayTarget,
            Transform stream, Renderer streamVisual, Renderer[] pooledPuddles,
            float flowRate = .18f, float maximumSeconds = 8f)
        {
            if (waterOutlet == null || trayTarget == null || stream == null ||
                streamVisual == null || pooledPuddles == null ||
                !PhysicsBody.IsFinite(flowRate) || flowRate <= 0 ||
                !PhysicsBody.IsFinite(maximumSeconds) || maximumSeconds < .5f)
                throw new ArgumentException("Water dispenser bindings are incomplete.");
            outlet = waterOutlet; dripTrayTarget = trayTarget;
            waterStream = stream; streamRenderer = streamVisual;
            puddleRenderers = (Renderer[])pooledPuddles.Clone();
            flowLitersPerSecond = flowRate; maximumRunSeconds = maximumSeconds;
        }

        private void Awake()
        {
            if (outlet == null || waterStream == null || streamRenderer == null)
                throw new InvalidOperationException("Water dispenser bindings are incomplete.");
            body = GetComponent<PhysicsBody>();
            fluid = GetComponent<FluidStream>() ?? gameObject.AddComponent<FluidStream>();
            fluid.Configure(streamRenderer.sharedMaterial);
            waterStream.gameObject.SetActive(false);
            foreach (var puddle in puddleRenderers)
                if (puddle != null) puddle.gameObject.SetActive(false);
        }

        public void ToggleDispensing() => SetDispensing(!dispensing);

        public void SetDispensing(bool value)
        {
            if (dispensing == value) { if (value) runRemaining = maximumRunSeconds; return; }
            dispensing = value;
            runRemaining = value ? maximumRunSeconds : 0;
            emissionTime = 0;
            DispensingChanged?.Invoke(value);
        }

        private void Update() => AdvanceFlow(Time.deltaTime);

        private void AdvanceFlow(float delta)
        {
            if (!dispensing || !PhysicsBody.IsFinite(delta) || delta <= 0 ||
                body != null && body.IsPaused) return;
            var elapsed = Mathf.Min(delta, runRemaining);
            runRemaining -= elapsed;
            emissionTime += elapsed;
            while (emissionTime + .000001 >= .02)
            {
                emissionTime = Math.Max(0, emissionTime - .02);
                fluid.Emit(outlet.position, Vector3.down * .15f,
                    flowLitersPerSecond * .02f, WaterReceiver.AmbientTemperatureCelsius,
                    new Color(.4f, .8f, 1f, .65f));
            }
            if (runRemaining <= 0) SetDispensing(false);
        }

        private void OnDisable() { dispensing = false; runRemaining = 0; emissionTime = 0; }

#if UNITY_EDITOR
        public void AdvanceForTesting(float delta)
        {
            for (var remaining = delta; remaining > 0; remaining -= .02f)
            {
                var step = Mathf.Min(.02f, remaining);
                AdvanceFlow(step);
                fluid.Advance(step);
            }
        }
#endif
    }
}
