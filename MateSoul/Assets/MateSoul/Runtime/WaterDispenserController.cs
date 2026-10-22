using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent]
    public sealed class WaterDispenserController : MonoBehaviour
    {
        private const int RaycastCapacity = 16;

        [Serializable]
        private sealed class PuddleState
        {
            public Renderer renderer;
            [NonSerialized] public float age;
            [NonSerialized] public bool active;
            [NonSerialized] public Vector3 baseScale;
            [NonSerialized] public MaterialPropertyBlock properties;
        }

        [SerializeField] private Transform outlet;
        [SerializeField] private Transform dripTrayTarget;
        [SerializeField] private Transform waterStream;
        [SerializeField] private Renderer streamRenderer;
        [SerializeField] private Renderer[] puddleRenderers = Array.Empty<Renderer>();
        [SerializeField, Min(.01f)] private float flowLitersPerSecond = .18f;
        [SerializeField, Min(.5f)] private float maximumRunSeconds = 8f;
        [SerializeField, Min(.1f)] private float maximumDropDistance = 1.4f;
        [SerializeField, Min(.001f)] private float streamRadius = .0045f;
        [SerializeField, Min(.1f)] private float puddleLifetime = 2.4f;
        [SerializeField, Min(.05f)] private float puddleInterval = .36f;

        private readonly RaycastHit[] raycastHits = new RaycastHit[RaycastCapacity];
        private PuddleState[] puddles = Array.Empty<PuddleState>();
        private PhysicsBody body;
        private MaterialPropertyBlock streamProperties;
        private float runRemaining;
        private float puddleCooldown;
        private int nextPuddle;
        private bool dispensing;

        public bool IsDispensing => dispensing;
        public float RunRemaining => runRemaining;
        public float FlowLitersPerSecond => flowLitersPerSecond;
        public int PuddlePoolSize => puddles.Length > 0
            ? puddles.Length
            : puddleRenderers?.Length ?? 0;
        public int ActivePuddleCount
        {
            get
            {
                var count = 0;
                for (var index = 0; index < puddles.Length; index++)
                    if (puddles[index].active) count++;
                return count;
            }
        }
        public Transform Outlet => outlet;
        public Transform WaterStream => waterStream;
        public event Action<bool> DispensingChanged;

        public void Configure(Transform waterOutlet, Transform trayTarget,
            Transform stream, Renderer streamVisual, Renderer[] pooledPuddles,
            float flowRate = .18f, float maximumSeconds = 8f)
        {
            if (waterOutlet == null || trayTarget == null || stream == null ||
                streamVisual == null || pooledPuddles == null ||
                pooledPuddles.Length < 2 ||
                Array.Exists(pooledPuddles, item => item == null) ||
                !PhysicsBody.IsFinite(flowRate) || flowRate <= 0 ||
                !PhysicsBody.IsFinite(maximumSeconds) || maximumSeconds < .5f)
                throw new ArgumentException(
                    "Water dispenser requires an outlet, tray, stream, puddle pool and finite flow.");
            outlet = waterOutlet;
            dripTrayTarget = trayTarget;
            waterStream = stream;
            streamRenderer = streamVisual;
            puddleRenderers = (Renderer[])pooledPuddles.Clone();
            flowLitersPerSecond = flowRate;
            maximumRunSeconds = maximumSeconds;
        }

        private void Awake()
        {
            ValidateConfiguration();
            body = GetComponent<PhysicsBody>();
            streamProperties = new MaterialPropertyBlock();
            puddles = new PuddleState[puddleRenderers.Length];
            for (var index = 0; index < puddles.Length; index++)
            {
                var renderer = puddleRenderers[index];
                puddles[index] = new PuddleState
                {
                    renderer = renderer,
                    baseScale = renderer.transform.localScale,
                    properties = new MaterialPropertyBlock()
                };
                renderer.gameObject.SetActive(false);
            }
            waterStream.gameObject.SetActive(false);
        }

        private void ValidateConfiguration()
        {
            if (outlet == null || dripTrayTarget == null || waterStream == null ||
                streamRenderer == null || puddleRenderers == null ||
                puddleRenderers.Length < 2 ||
                Array.Exists(puddleRenderers, item => item == null) ||
                !PhysicsBody.IsFinite(flowLitersPerSecond) ||
                flowLitersPerSecond <= 0 ||
                !PhysicsBody.IsFinite(maximumRunSeconds) ||
                maximumRunSeconds < .5f)
                throw new InvalidOperationException(
                    "Water dispenser prefab bindings are incomplete.");
        }

        public void ToggleDispensing() => SetDispensing(!dispensing);

        public void SetDispensing(bool value)
        {
            if (dispensing == value)
            {
                if (value) runRemaining = maximumRunSeconds;
                return;
            }
            dispensing = value;
            runRemaining = value ? maximumRunSeconds : 0;
            puddleCooldown = 0;
            if (!value && waterStream != null) waterStream.gameObject.SetActive(false);
            DispensingChanged?.Invoke(value);
        }

        private void Update()
        {
            UpdatePuddles(Time.deltaTime);
            if (!dispensing) return;
            if (body != null && body.IsPaused)
            {
                waterStream.gameObject.SetActive(false);
                return;
            }
            AdvanceFlow(Time.deltaTime);
        }

        private void AdvanceFlow(float deltaTime)
        {
            if (!PhysicsBody.IsFinite(deltaTime) || deltaTime <= 0) return;
            runRemaining -= deltaTime;
            if (runRemaining <= 0)
            {
                SetDispensing(false);
                return;
            }

            ResolveImpact(out var point, out var normal, out var receiver);
            UpdateStream(point);
            var requested = flowLitersPerSecond * deltaTime;
            var accepted = receiver == null ? 0 : receiver.Receive(requested, point);
            if (accepted + .000001f >= requested) return;
            puddleCooldown -= deltaTime;
            if (puddleCooldown > 0) return;
            puddleCooldown = puddleInterval;
            ActivatePuddle(point, normal);
        }

        private void ResolveImpact(out Vector3 point, out Vector3 normal,
            out WaterReceiver receiver)
        {
            receiver = null;
            var origin = outlet.position + Vector3.down * .002f;
            var fallbackDistance = Vector3.Distance(origin, dripTrayTarget.position);
            fallbackDistance = Mathf.Clamp(fallbackDistance, .05f, maximumDropDistance);
            point = origin + Vector3.down * fallbackDistance;
            normal = Vector3.up;

            var count = Physics.RaycastNonAlloc(origin, Vector3.down, raycastHits,
                maximumDropDistance, ~0, QueryTriggerInteraction.Ignore);
            var nearest = fallbackDistance + .003f;
            for (var index = 0; index < count; index++)
            {
                var hit = raycastHits[index];
                if (hit.collider == null || hit.distance >= nearest ||
                    hit.collider.transform.IsChildOf(transform) &&
                    hit.collider.GetComponentInParent<WaterReceiver>() == null)
                    continue;
                nearest = hit.distance;
                point = hit.point;
                normal = hit.normal.sqrMagnitude < .1f ? Vector3.up : hit.normal;
                receiver = hit.collider.GetComponentInParent<WaterReceiver>();
            }
        }

        private void UpdateStream(Vector3 endpoint)
        {
            var start = outlet.position;
            var delta = endpoint - start;
            var length = Mathf.Max(.002f, delta.magnitude);
            waterStream.gameObject.SetActive(true);
            waterStream.position = start + delta * .5f;
            waterStream.rotation = Quaternion.FromToRotation(Vector3.up, delta / length);
            waterStream.localScale = new Vector3(
                streamRadius * 2f,
                length * .5f,
                streamRadius * 2f);
            var pulse = .82f + Mathf.Sin(Time.time * 23f) * .08f;
            streamRenderer.GetPropertyBlock(streamProperties);
            SetColor(streamProperties, new Color(.35f, .76f, 1f, pulse));
            streamRenderer.SetPropertyBlock(streamProperties);
        }

        private void ActivatePuddle(Vector3 point, Vector3 normal)
        {
            if (puddles.Length == 0) return;
            var puddle = puddles[nextPuddle];
            nextPuddle = (nextPuddle + 1) % puddles.Length;
            puddle.active = true;
            puddle.age = 0;
            var target = puddle.renderer.transform;
            target.position = point + normal * .0012f;
            target.rotation = Quaternion.FromToRotation(Vector3.up, normal);
            target.localScale = puddle.baseScale;
            puddle.renderer.gameObject.SetActive(true);
            ApplyPuddleVisual(puddle, 0);
        }

        private void UpdatePuddles(float deltaTime)
        {
            for (var index = 0; index < puddles.Length; index++)
            {
                var puddle = puddles[index];
                if (!puddle.active) continue;
                puddle.age += Mathf.Max(0, deltaTime);
                var progress = Mathf.Clamp01(puddle.age / puddleLifetime);
                if (progress >= 1)
                {
                    puddle.active = false;
                    puddle.renderer.gameObject.SetActive(false);
                    continue;
                }
                ApplyPuddleVisual(puddle, progress);
            }
        }

        private static void ApplyPuddleVisual(PuddleState puddle, float progress)
        {
            var alpha = Mathf.Pow(1f - progress, 1.5f) * .48f;
            puddle.renderer.GetPropertyBlock(puddle.properties);
            SetColor(puddle.properties, new Color(.24f, .63f, .92f, alpha));
            puddle.renderer.SetPropertyBlock(puddle.properties);
            var horizontal = Mathf.Lerp(1f, 1.35f, progress);
            puddle.renderer.transform.localScale = new Vector3(
                puddle.baseScale.x * horizontal,
                puddle.baseScale.y,
                puddle.baseScale.z * horizontal);
        }

        private static void SetColor(MaterialPropertyBlock properties, Color color)
        {
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
        }

        private void OnDisable()
        {
            dispensing = false;
            runRemaining = 0;
            if (waterStream != null) waterStream.gameObject.SetActive(false);
            for (var index = 0; index < puddles.Length; index++)
            {
                puddles[index].active = false;
                if (puddles[index].renderer != null)
                    puddles[index].renderer.gameObject.SetActive(false);
            }
        }

#if UNITY_EDITOR
        public void AdvanceForTesting(float deltaTime)
        {
            UpdatePuddles(deltaTime);
            if (dispensing) AdvanceFlow(deltaTime);
        }
#endif
    }
}
