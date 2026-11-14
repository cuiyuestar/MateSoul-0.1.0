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

        private struct ContainedEntry
        {
            public CookableFood Food;
            public GameItem Item;
            public bool Carriable;
        }

        private readonly Collider[] overlaps = new Collider[MaximumOverlaps];
        private readonly ContainedEntry[] processed = new ContainedEntry[MaximumOverlaps];
        private readonly ContainedEntry[] contained = new ContainedEntry[MaximumOverlaps];
        private HeatingBaseController heatSource;
        private PhysicsBody body;
        private Pose carryPose;
        private bool hasCarryPose, wasCarried;
        private float currentTemperature;
        private float elapsed;
        private float nextRejectLog;
        private int containedCount;

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
            var heated = IsHeated;
            // A carried pan is kinematic: items have to follow its teleporting pose
            // explicitly or fast motion leaves them behind and drops them out.
            var carried = body != null && body.IsStableFollowing;
            if ((body != null && body.IsPaused) || (!heated && !carried))
            {
                elapsed = 0;
                wasCarried = false;
                StopContainedFoods();
                return;
            }
            // Pick up whatever is already in the pan before the player moves it.
            if (carried && !wasCarried) elapsed = scanInterval;
            wasCarried = carried;
            elapsed += Time.deltaTime;
            if (elapsed < scanInterval) return;
            var duration = elapsed;
            elapsed = 0;
            HeatContainedFoods(heated ? duration : 0);
        }

        // Runs after the hand has written the pan pose for this frame, so the
        // contents keep their pan-local pose instead of being swept through.
        private void LateUpdate()
        {
            if (body == null || containedCount == 0 || !body.IsStableFollowing ||
                body.IsPaused)
            {
                hasCarryPose = false;
                return;
            }
            var pose = body.Pose;
            if (!hasCarryPose)
            {
                carryPose = pose;
                hasCarryPose = true;
                return;
            }
            if (pose.position == carryPose.position && pose.rotation == carryPose.rotation)
                return;
            var delta = pose.rotation * Quaternion.Inverse(carryPose.rotation);
            for (var index = 0; index < containedCount; index++)
            {
                var entry = contained[index];
                var item = entry.Item;
                if (!entry.Carriable || item == null || item.Body == null) continue;
                var target = item.Body;
                if (target.IsPaused || target.IsFollowing || target.IsInteractionControlled ||
                    target.IsContact || target.IsDocked)
                    continue;
                var current = target.Pose;
                target.Place(new Pose(
                    pose.position + delta * (current.position - carryPose.position),
                    delta * current.rotation));
            }
            carryPose = pose;
        }

        internal void SetHeatSource(HeatingBaseController source, float temperature)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var changed = heatSource != source ||
                          !Mathf.Approximately(currentTemperature, temperature);
            heatSource = source;
            currentTemperature = temperature;
            for (var index = 0; index < containedCount; index++)
                if (contained[index].Food != null)
                    contained[index].Food.SetHeating(true, temperature);
            if (changed)
                Debug.Log($"MATESOUL_COOK_HEAT pan={name} source=1 temp={temperature} " +
                          $"contained={containedCount}");
        }

        internal void ClearHeatSource(HeatingBaseController source)
        {
            if (heatSource != source) return;
            heatSource = null;
            currentTemperature = 0;
            elapsed = 0;
            StopContainedFoods();
            Debug.Log($"MATESOUL_COOK_HEAT pan={name} source=0");
        }

        public void AdvanceForTesting(float durationSeconds)
        {
            if (durationSeconds <= 0 || !IsHeated) return;
            HeatContainedFoods(durationSeconds);
        }

        public bool Contains(CookableFood food)
            => food != null && food.IsCookable &&
               Contains(food.WorldCenter, food.InPanRadius);

        private bool Contains(GameItem item)
        {
            if (!TryGetItemBounds(item, out var bounds)) return false;
            return Contains(bounds.center, Mathf.Min(bounds.extents.x, bounds.extents.z));
        }

        private static bool TryGetItemBounds(GameItem item, out Bounds bounds)
        {
            bounds = default;
            if (item == null || item.Body == null) return false;
            var shapes = item.Body.Shapes;
            if (shapes == null || shapes.Count == 0) return false;
            bounds = shapes[0].bounds;
            for (var index = 1; index < shapes.Count; index++)
                if (shapes[index] != null) bounds.Encapsulate(shapes[index].bounds);
            return true;
        }

        // Carrying is limited to loose items that actually fit inside the pan;
        // the heating base and other large bodies must never ride along.
        private bool IsCarriable(GameItem item, Bounds bounds)
            => item != null && item.Body != null &&
               !item.IsFixedAfterPlacement && !item.IsImmovableAfterPlacement &&
               Mathf.Max(bounds.extents.x, bounds.extents.z) <= interiorRadius;

        private bool Contains(Vector3 worldCenter, float radius)
        {
            var local = transform.InverseTransformPoint(worldCenter) - localBottomCenter;
            if (local.y < -.006f || local.y > interiorHeight) return false;
            var centerDistance = new Vector2(local.x, local.z).magnitude;
            var area = Mathf.PI * radius * radius;
            var overlap = HeatingBaseController.CircleOverlapArea(
                interiorRadius, radius, centerDistance);
            return area > 0 &&
                   overlap / area >= MinimumFoodOverlapRatio;
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
                var shape = overlaps[index];
                if (body != null && shape.attachedRigidbody == body.Rigidbody) continue;
                var food = shape.GetComponentInParent<CookableFood>();
                var item = food != null ? food.Item : shape.GetComponentInParent<GameItem>();
                if (food == null && item == null) continue;
                if (AlreadyProcessed(food, item, processedCount)) continue;
                var itemBounds = default(Bounds);
                var hasItem = item != null && TryGetItemBounds(item, out itemBounds);
                var contained = food != null
                    ? Contains(food)
                    : hasItem && Contains(itemBounds.center,
                        Mathf.Min(itemBounds.extents.x, itemBounds.extents.z));
                if (!contained)
                {
                    if (food != null) LogRejected(food);
                    continue;
                }
                processed[processedCount++] = new ContainedEntry
                {
                    Food = food,
                    Item = item,
                    Carriable = hasItem && IsCarriable(item, itemBounds)
                };
                if (food == null || durationSeconds <= 0) continue;
                food.SetHeating(true, currentTemperature);
                food.ApplyHeat(currentTemperature, durationSeconds);
            }
            for (var index = 0; index < containedCount; index++)
                if (contained[index].Food != null &&
                    !StillContained(contained[index].Food, contained[index].Item, processedCount))
                    contained[index].Food.SetHeating(false);
            var changed = processedCount != containedCount;
            Array.Clear(contained, 0, containedCount);
            Array.Copy(processed, contained, processedCount);
            containedCount = processedCount;
            Array.Clear(processed, 0, processedCount);
            if (changed)
                Debug.Log($"MATESOUL_COOK_SCAN pan={name} heated={(IsHeated ? 1 : 0)} " +
                          $"contained={containedCount} " +
                          $"pos=({transform.position.x:F2},{transform.position.y:F2}," +
                          $"{transform.position.z:F2}){DescribeContained()}");
        }

        private void LogRejected(CookableFood food)
        {
            if (Time.unscaledTime < nextRejectLog) return;
            nextRejectLog = Time.unscaledTime + 1f;
            var local = transform.InverseTransformPoint(food.WorldCenter) - localBottomCenter;
            var distance = new Vector2(local.x, local.z).magnitude;
            var area = Mathf.PI * food.InPanRadius * food.InPanRadius;
            var ratio = area > 0
                ? HeatingBaseController.CircleOverlapArea(
                    interiorRadius, food.InPanRadius, distance) / area
                : 0;
            Debug.Log($"MATESOUL_COOK_REJECT pan={name} food={food.name} " +
                      $"local=({local.x:F2},{local.y:F2},{local.z:F2}) " +
                      $"r={food.InPanRadius:F3} ratio={ratio:F2}");
        }

        private string DescribeContained()
        {
            if (containedCount == 0) return string.Empty;
            var text = " items=";
            for (var index = 0; index < containedCount; index++)
            {
                var entry = contained[index];
                var id = entry.Item != null && entry.Item.Definition != null
                    ? entry.Item.Definition.Id
                    : entry.Food != null ? entry.Food.name : "unknown";
                text += index == 0 ? id : "," + id;
            }
            return text;
        }

        private bool AlreadyProcessed(CookableFood food, GameItem item, int count)
        {
            for (var index = 0; index < count; index++)
            {
                if (food != null && processed[index].Food == food) return true;
                if (item != null && processed[index].Item == item) return true;
            }
            return false;
        }

        private bool StillContained(CookableFood food, GameItem item, int count)
        {
            for (var index = 0; index < count; index++)
            {
                if (food != null)
                {
                    if (processed[index].Food == food) return true;
                }
                else if (item != null && processed[index].Item == item) return true;
            }
            return false;
        }

        private void StopContainedFoods()
        {
            for (var index = 0; index < containedCount; index++)
                if (contained[index].Food != null)
                    contained[index].Food.SetHeating(false);
            Array.Clear(contained, 0, containedCount);
            containedCount = 0;
            hasCarryPose = false;
        }

        private void OnDisable()
        {
            hasCarryPose = false;
            if (heatSource != null) ClearHeatSource(heatSource);
            else StopContainedFoods();
        }
    }
}
