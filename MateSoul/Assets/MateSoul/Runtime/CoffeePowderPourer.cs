using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent, RequireComponent(typeof(PhysicsBody))]
    public sealed class CoffeePowderPourer : MonoBehaviour
    {
        private const int MaximumRaycastHits = 16;

        [SerializeField] private CoffeeGrinderController source;
        [SerializeField] private Transform outlet;
        [SerializeField] private ParticleSystem powderStream;
        [SerializeField, Min(1f)] private float unitsPerSecond = 24f;
        [SerializeField, Range(5f, 89f)] private float minimumTiltDegrees = 55f;
        [SerializeField, Min(.05f)] private float maximumDropDistance = .55f;

        private readonly RaycastHit[] hits = new RaycastHit[MaximumRaycastHits];
        private PhysicsBody body;
        private float unitAccumulator;

        public CoffeeGrinderController Source => source;
        public Transform Outlet => outlet;
        public ParticleSystem PowderStream => powderStream;
        public float UnitsPerSecond => unitsPerSecond;
        public float MinimumTiltDegrees => minimumTiltDegrees;
        public bool IsPouring { get; private set; }
        public int TransferredGroundUnits { get; private set; }

        public void Configure(CoffeeGrinderController grinder,
            Transform pourOutlet, ParticleSystem stream,
            float transferUnitsPerSecond = 24f, float tiltDegrees = 55f,
            float dropDistance = .55f)
        {
            if (grinder == null || pourOutlet == null || stream == null ||
                !pourOutlet.IsChildOf(transform) ||
                !stream.transform.IsChildOf(transform) ||
                !PhysicsBody.IsFinite(transferUnitsPerSecond) ||
                transferUnitsPerSecond <= 0 ||
                !PhysicsBody.IsFinite(tiltDegrees) ||
                tiltDegrees < 5f || tiltDegrees > 89f ||
                !PhysicsBody.IsFinite(dropDistance) || dropDistance < .05f)
                throw new ArgumentException(
                    "Coffee powder pourer bindings and limits are incomplete.");
            source = grinder;
            outlet = pourOutlet;
            powderStream = stream;
            unitsPerSecond = transferUnitsPerSecond;
            minimumTiltDegrees = tiltDegrees;
            maximumDropDistance = dropDistance;
            powderStream.Stop(
                true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void Awake()
        {
            if (source == null || outlet == null || powderStream == null)
                throw new InvalidOperationException(
                    "Coffee powder pourer bindings are incomplete.");
            body = GetComponent<PhysicsBody>();
            powderStream.Stop(
                true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void Update() => Advance(Time.deltaTime);

        public void AdvanceForTesting(float deltaSeconds)
            => Advance(deltaSeconds);

        public int TryPourInto(CoffeeBrewReceiver target, int requestedUnits,
            Vector3 targetWorldPoint)
        {
            var transferred = source.TransferGroundCoffeeTo(
                target, requestedUnits, targetWorldPoint);
            TransferredGroundUnits += transferred;
            return transferred;
        }

        private void Advance(float deltaSeconds)
        {
            if (!PhysicsBody.IsFinite(deltaSeconds) || deltaSeconds <= 0 ||
                !source.HasPowder ||
                body != null && body.IsPaused ||
                Vector3.Dot(transform.up, Vector3.up) >
                Mathf.Cos(minimumTiltDegrees * Mathf.Deg2Rad))
            {
                unitAccumulator = 0;
                SetStream(false);
                return;
            }

            if (!TryFindReceiver(out var target, out var targetPoint))
            {
                unitAccumulator = 0;
                SetStream(false);
                return;
            }

            unitAccumulator += unitsPerSecond * deltaSeconds;
            var requested = Mathf.FloorToInt(unitAccumulator);
            if (requested <= 0)
            {
                SetStream(true);
                return;
            }
            var transferred = TryPourInto(target, requested, targetPoint);
            unitAccumulator = Mathf.Max(0, unitAccumulator - transferred);
            if (transferred == 0)
                unitAccumulator = Mathf.Min(unitAccumulator, 1f);
            SetStream(transferred > 0 || unitAccumulator > 0);
        }

        private bool TryFindReceiver(
            out CoffeeBrewReceiver target, out Vector3 targetPoint)
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
                    hit.collider.GetComponentInParent<CoffeeBrewReceiver>();
                if (candidate == null) continue;
                var water = candidate.Water;
                var opening = water.transform.TransformPoint(
                    water.OpeningCenter);
                var horizontal = Vector3.ProjectOnPlane(
                    opening - outlet.position, Vector3.up);
                if (horizontal.sqrMagnitude >
                    water.OpeningRadius * water.OpeningRadius ||
                    !candidate.CanReceiveGroundCoffeeAt(opening))
                    continue;
                target = candidate;
                targetPoint = opening;
                nearest = hit.distance;
            }
            return target != null;
        }

        private void SetStream(bool visible)
        {
            IsPouring = visible;
            if (powderStream == null) return;
            if (visible)
            {
                if (!powderStream.isPlaying) powderStream.Play(true);
            }
            else if (powderStream.isPlaying || powderStream.isEmitting)
                powderStream.Stop(
                    true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void OnDisable()
        {
            unitAccumulator = 0;
            IsPouring = false;
            if (powderStream != null)
                powderStream.Stop(
                    true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
