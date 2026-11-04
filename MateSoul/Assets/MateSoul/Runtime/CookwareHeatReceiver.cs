using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent]
    public sealed class CookwareHeatReceiver : MonoBehaviour
    {
        private const int MaximumOverlaps = 128;
        public const float MinimumFoodOverlapRatio = .55f;
        [SerializeField] private Vector3 localBottomCenter;
        [SerializeField, Min(.01f)] private float contactRadius = .112f;
        [SerializeField, Min(.01f)] private float interiorRadius = .124f;
        [SerializeField, Min(.01f)] private float interiorHeight = .07f;
        [SerializeField, Min(.05f)] private float scanInterval = .2f;

        private readonly Collider[] overlaps = new Collider[MaximumOverlaps];
        private readonly CookableFood[] processed = new CookableFood[MaximumOverlaps];
        private readonly CookableFood[] heatedFoods = new CookableFood[MaximumOverlaps];
        private HeatingBaseController heatSource;
        private PhysicsBody body;
        private float currentTemperature;
        private float elapsed;
        private int heatedFoodCount;

        public Vector3 ContactCenter => transform.TransformPoint(localBottomCenter);
        public Vector3 ContactNormal => transform.up;
        public float ContactRadius => contactRadius;
        public float InteriorRadius => interiorRadius;
        public float InteriorHeight => interiorHeight;
        public float CurrentTemperature => currentTemperature;
        public bool IsHeated => heatSource != null && heatSource.IsPoweredOn;

        public void Configure(Vector3 bottomCenter, float panContactRadius,
            float panInteriorRadius, float panInteriorHeight)
        {
            if (panContactRadius <= 0 || panInteriorRadius <= 0 ||
                panInteriorHeight <= 0 ||
                !PhysicsBody.IsFinite(panContactRadius) ||
                !PhysicsBody.IsFinite(panInteriorRadius) ||
                !PhysicsBody.IsFinite(panInteriorHeight))
                throw new ArgumentException("Cookware heat dimensions must be finite and positive.");
            localBottomCenter = bottomCenter;
            contactRadius = panContactRadius;
            interiorRadius = panInteriorRadius;
            interiorHeight = panInteriorHeight;
        }

        private void Awake() => body = GetComponent<PhysicsBody>();

        private void Update()
        {
            if (!IsHeated || body != null && body.IsPaused)
            {
                elapsed = 0;
                StopHeatingFoods();
                return;
            }
            elapsed += Time.deltaTime;
            if (elapsed < scanInterval) return;
            var duration = elapsed;
            elapsed = 0;
            HeatContainedFoods(duration);
        }

        internal void SetHeatSource(HeatingBaseController source, float temperature)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            heatSource = source;
            currentTemperature = temperature;
            for (var index = 0; index < heatedFoodCount; index++)
                if (heatedFoods[index] != null)
                    heatedFoods[index].SetHeating(true, temperature);
        }

        internal void ClearHeatSource(HeatingBaseController source)
        {
            if (heatSource != source) return;
            heatSource = null;
            currentTemperature = 0;
            elapsed = 0;
            StopHeatingFoods();
        }

        public void AdvanceForTesting(float durationSeconds)
        {
            if (durationSeconds <= 0 || !IsHeated) return;
            HeatContainedFoods(durationSeconds);
        }

        public bool Contains(CookableFood food)
        {
            if (food == null || !food.IsCookable) return false;
            var local = transform.InverseTransformPoint(food.WorldCenter) -
                        localBottomCenter;
            if (local.y < -.006f || local.y > interiorHeight) return false;
            var centerDistance = new Vector2(local.x, local.z).magnitude;
            var foodRadius = food.InPanRadius;
            var foodArea = Mathf.PI * foodRadius * foodRadius;
            var overlap = HeatingBaseController.CircleOverlapArea(
                interiorRadius, foodRadius, centerDistance);
            return foodArea > 0 &&
                   overlap / foodArea >= MinimumFoodOverlapRatio;
        }

        private void HeatContainedFoods(float durationSeconds)
        {
            var worldCenter = transform.TransformPoint(
                localBottomCenter + Vector3.up * interiorHeight * .5f);
            var halfExtents = new Vector3(
                interiorRadius, interiorHeight * .5f, interiorRadius);
            var count = Physics.OverlapBoxNonAlloc(
                worldCenter, halfExtents, overlaps, transform.rotation,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            var processedCount = 0;
            for (var index = 0; index < count; index++)
            {
                var food = overlaps[index].GetComponentInParent<CookableFood>();
                if (food == null || AlreadyProcessed(food, processedCount) ||
                    !Contains(food))
                    continue;
                processed[processedCount++] = food;
                food.SetHeating(true, currentTemperature);
                food.ApplyHeat(currentTemperature, durationSeconds);
            }
            for (var index = 0; index < heatedFoodCount; index++)
            {
                var food = heatedFoods[index];
                if (food != null && !AlreadyProcessed(food, processedCount))
                    food.SetHeating(false);
            }
            Array.Clear(heatedFoods, 0, heatedFoodCount);
            Array.Copy(processed, heatedFoods, processedCount);
            heatedFoodCount = processedCount;
            Array.Clear(processed, 0, processedCount);
        }

        private bool AlreadyProcessed(CookableFood food, int count)
        {
            for (var index = 0; index < count; index++)
                if (processed[index] == food) return true;
            return false;
        }

        private void StopHeatingFoods()
        {
            for (var index = 0; index < heatedFoodCount; index++)
                if (heatedFoods[index] != null)
                    heatedFoods[index].SetHeating(false);
            Array.Clear(heatedFoods, 0, heatedFoodCount);
            heatedFoodCount = 0;
        }

        private void OnDisable()
        {
            if (heatSource != null) ClearHeatSource(heatSource);
        }
    }
}
