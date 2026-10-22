using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent, RequireComponent(typeof(PhysicsBody))]
    public sealed class LiquidPourer : MonoBehaviour
    {
        private const int MaximumRaycastHits = 16;

        [SerializeField] private WaterReceiver source;
        [SerializeField] private Transform outlet;
        [SerializeField] private Renderer streamRenderer;
        [SerializeField, Min(.01f)] private float flowLitersPerSecond = .24f;
        [SerializeField, Range(5f, 89f)] private float minimumTiltDegrees = 52f;
        [SerializeField, Min(.05f)] private float maximumDropDistance = .65f;

        private readonly RaycastHit[] hits = new RaycastHit[MaximumRaycastHits];
        private PhysicsBody body;

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
            maximumDropDistance = dropDistance;
            streamRenderer.gameObject.SetActive(false);
        }

        private void Awake()
        {
            if (source == null || outlet == null || streamRenderer == null)
                throw new InvalidOperationException(
                    "Liquid pourer bindings are incomplete.");
            body = GetComponent<PhysicsBody>();
            streamRenderer.gameObject.SetActive(false);
        }

        private void Update() => Advance(Time.deltaTime);

        public void AdvanceForTesting(float deltaSeconds)
            => Advance(deltaSeconds);

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
                SetStream(false, outlet == null
                    ? transform.position
                    : outlet.position);
                return;
            }

            if (!TryFindReceiver(out var target, out var targetPoint))
            {
                SetStream(false, outlet.position);
                return;
            }

            var transferred = TryPourInto(
                target, flowLitersPerSecond * deltaSeconds, targetPoint);
            SetStream(transferred > 0, targetPoint);
        }

        private bool TryFindReceiver(
            out WaterReceiver target, out Vector3 targetPoint)
        {
            target = null;
            targetPoint = outlet.position;
            var count = Physics.RaycastNonAlloc(
                outlet.position + Vector3.down * .002f,
                Vector3.down, hits, maximumDropDistance,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            var nearest = float.PositiveInfinity;
            for (var index = 0; index < count; index++)
            {
                var hit = hits[index];
                if (hit.collider == null || hit.distance >= nearest ||
                    hit.collider.transform.IsChildOf(transform))
                    continue;
                var candidate =
                    hit.collider.GetComponentInParent<WaterReceiver>();
                if (candidate == null || candidate == source) continue;
                var opening = candidate.transform.TransformPoint(
                    candidate.OpeningCenter);
                var horizontal = Vector3.ProjectOnPlane(
                    opening - outlet.position, Vector3.up);
                if (horizontal.sqrMagnitude >
                    candidate.OpeningRadius * candidate.OpeningRadius)
                    continue;
                target = candidate;
                targetPoint = opening;
                nearest = hit.distance;
            }
            return target != null && target.CanReceiveAt(targetPoint);
        }

        private void SetStream(bool visible, Vector3 endpoint)
        {
            IsPouring = visible;
            if (streamRenderer == null) return;
            streamRenderer.gameObject.SetActive(visible);
            if (!visible) return;
            var start = outlet.position;
            var delta = endpoint - start;
            var length = Mathf.Max(.002f, delta.magnitude);
            streamRenderer.transform.position = start + delta * .5f;
            streamRenderer.transform.rotation =
                Quaternion.FromToRotation(Vector3.up, delta / length);
            var scale = streamRenderer.transform.localScale;
            streamRenderer.transform.localScale =
                new Vector3(scale.x, length * .5f, scale.z);
        }

        private void OnDisable()
        {
            if (streamRenderer != null)
                streamRenderer.gameObject.SetActive(false);
            IsPouring = false;
        }
    }
}
