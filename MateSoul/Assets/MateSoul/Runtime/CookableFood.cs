using System;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    public enum FoodDoneness
    {
        Raw,
        Medium,
        MediumWell,
        WellDone,
        Burned
    }

    [DisallowMultipleComponent]
    public sealed class CookableFood : MonoBehaviour, IRuntimeWorldFactSource
    {
        public const float AmbientTemperature = 20f;
        public const float MediumDoneness = 50f;
        public const float MediumWellDoneness = 70f;
        public const float WellDoneDoneness = 100f;
        public const float MaximumDoneness = 120f;

        [SerializeField] private GameObject rawVisualRoot;
        [SerializeField] private GameObject cookedVisualRoot;
        [SerializeField, HideInInspector] private float doneness;

        private MaterialPropertyBlock properties;
        private Renderer[] cookedRenderers;
        private CookingEffects effects;
        private CookingSettings settings;
        private GameItem item;
        private FoodDoneness stage;
        private bool configured;

        public float Doneness => doneness;
        public FoodDoneness Stage => stage;
        public bool IsBurned => stage == FoodDoneness.Burned;
        public Vector3 WorldCenter => WorldBounds.center;
        public float InPanRadius
        {
            get
            {
                var bounds = WorldBounds;
                return Mathf.Min(bounds.extents.x, bounds.extents.z);
            }
        }
        // The body may replace the authored box with generated mesh colliders and
        // disable it; a disabled collider reports zero bounds, so never read the
        // first child collider for containment geometry.
        private Bounds WorldBounds
        {
            get
            {
                var shapes = item != null && item.Body != null ? item.Body.Shapes : null;
                if (shapes != null && shapes.Count > 0)
                {
                    var bounds = shapes[0].bounds;
                    for (var index = 1; index < shapes.Count; index++)
                        if (shapes[index] != null) bounds.Encapsulate(shapes[index].bounds);
                    return bounds;
                }
                var fallback = GetComponentInChildren<Collider>(true);
                return fallback != null
                    ? fallback.bounds
                    : new Bounds(transform.position, Vector3.one * .001f);
            }
        }
        public bool IsCookable => configured && settings.enabled;
        public bool IsHeating => effects != null && effects.IsHeating;
        public CookingEffects Effects => effects;
        public GameItem Item => item;
        public GameObject RawVisualRoot => rawVisualRoot;
        public GameObject CookedVisualRoot => cookedVisualRoot;
        public event Action<FoodDoneness> DonenessChanged;

        public bool TryCaptureRuntimeWorldFact(
            out RuntimeWorldFactSnapshot fact)
        {
            if (!IsHeating && stage == FoodDoneness.Raw)
            {
                fact = null;
                return false;
            }
            fact = RuntimeWorldFact.Create(
                this,
                "cooking",
                IsHeating ? "heating" : stage.ToString().ToLowerInvariant(),
                $"doneness={Mathf.RoundToInt(doneness)}%");
            return true;
        }

        public void ConfigureVisual(GameObject rawRoot, GameObject cookedRoot)
        {
            if (rawRoot == null || cookedRoot == null || rawRoot == cookedRoot ||
                rawRoot.transform.parent != cookedRoot.transform.parent)
                throw new ArgumentException(
                    "Cookable visuals require distinct sibling model roots.");
            if (rawRoot.GetComponentsInChildren<Renderer>(true).Length == 0 ||
                cookedRoot.GetComponentsInChildren<Renderer>(true).Length == 0)
                throw new ArgumentException(
                    "Cookable visual roots must contain renderers.");
            cookedRoot.transform.localPosition = rawRoot.transform.localPosition;
            cookedRoot.transform.localRotation = rawRoot.transform.localRotation;
            cookedRoot.transform.localScale = rawRoot.transform.localScale;
            rawVisualRoot = rawRoot;
            cookedVisualRoot = cookedRoot;
            cookedRenderers = cookedRoot.GetComponentsInChildren<Renderer>(true);
            ApplyVisual();
        }

        public void Configure(CookingSettings value, GameItem owner = null)
        {
            value.Validate();
            if (!value.enabled)
                throw new ArgumentException("CookableFood cannot bind a disabled marker.");
            if (GetComponentInChildren<Collider>(true) == null)
                throw new InvalidOperationException("Cookable food requires a collider.");
            settings = value;
            item = owner;
            effects = GetComponent<CookingEffects>() ??
                      gameObject.AddComponent<CookingEffects>();
            configured = true;
            RestoreState(owner?.Instance?.StateId);
            ApplyStage(Classify(doneness), false);
        }

        public void ApplyHeat(float temperatureCelsius, float durationSeconds)
        {
            if (!configured || !settings.enabled || durationSeconds <= 0 ||
                !PhysicsBody.IsFinite(temperatureCelsius) ||
                !PhysicsBody.IsFinite(durationSeconds) ||
                item != null && item.Body != null && item.Body.IsPaused)
                return;
            var gain = CalculateDonenessGain(
                temperatureCelsius, durationSeconds,
                settings.donenessPerDegreeSecond);
            if (gain <= 0) return;
            doneness = Mathf.Min(MaximumDoneness, doneness + gain);
            ApplyStage(Classify(doneness), true);
        }

        internal void SetHeating(bool value, float temperatureCelsius = 0)
        {
            if (!configured || !settings.enabled) return;
            if (value && item != null && item.Body != null && item.Body.IsPaused)
                value = false;
            if (effects == null)
                effects = GetComponent<CookingEffects>() ??
                          gameObject.AddComponent<CookingEffects>();
            effects.SetHeating(value, temperatureCelsius);
        }

        public static float CalculateDonenessGain(float temperatureCelsius,
            float durationSeconds, float donenessPerDegreeSecond = .025f)
        {
            if (!PhysicsBody.IsFinite(temperatureCelsius) ||
                !PhysicsBody.IsFinite(durationSeconds) ||
                !PhysicsBody.IsFinite(donenessPerDegreeSecond) ||
                durationSeconds <= 0 || donenessPerDegreeSecond <= 0)
                return 0;
            return Mathf.Max(0, temperatureCelsius - AmbientTemperature) *
                   durationSeconds * donenessPerDegreeSecond;
        }

        public static FoodDoneness Classify(float value)
        {
            if (value > WellDoneDoneness) return FoodDoneness.Burned;
            if (value >= WellDoneDoneness) return FoodDoneness.WellDone;
            if (value >= MediumWellDoneness) return FoodDoneness.MediumWell;
            if (value >= MediumDoneness) return FoodDoneness.Medium;
            return FoodDoneness.Raw;
        }

        private void RestoreState(string stateId)
        {
            switch (stateId)
            {
                case "cooking.medium":
                    doneness = MediumDoneness;
                    break;
                case "cooking.medium-well":
                    doneness = MediumWellDoneness;
                    break;
                case "cooking.well-done":
                    doneness = WellDoneDoneness;
                    break;
                case "cooking.burned":
                    doneness = MaximumDoneness;
                    break;
                default:
                    doneness = Mathf.Clamp(doneness, 0, MaximumDoneness);
                    break;
            }
        }

        private void ApplyStage(FoodDoneness next, bool publish)
        {
            var changed = stage != next;
            stage = next;
            if (item?.Instance != null)
                item.Instance.SetState(StateId(next));
            ApplyVisual();
            if (changed)
                Debug.Log($"MATESOUL_COOK_STAGE food={name} stage={stage} " +
                          $"doneness={doneness:F1}");
            if (publish && changed) DonenessChanged?.Invoke(stage);
        }

        private void ApplyVisual()
        {
            if (rawVisualRoot == null || cookedVisualRoot == null) return;
            var cooked = stage >= FoodDoneness.Medium;
            rawVisualRoot.SetActive(!cooked);
            cookedVisualRoot.SetActive(cooked);
            if (cookedRenderers == null || cookedRenderers.Length == 0)
                cookedRenderers =
                    cookedVisualRoot.GetComponentsInChildren<Renderer>(true);
            if (stage == FoodDoneness.Burned)
            {
                if (properties == null) properties = new MaterialPropertyBlock();
                properties.SetColor("_BaseColor", new Color(.24f, .16f, .10f, 1f));
                foreach (var renderer in cookedRenderers)
                    if (renderer != null) renderer.SetPropertyBlock(properties);
            }
            else
                foreach (var renderer in cookedRenderers)
                    if (renderer != null) renderer.SetPropertyBlock(null);
        }

        private static string StateId(FoodDoneness value)
        {
            switch (value)
            {
                case FoodDoneness.Medium: return "cooking.medium";
                case FoodDoneness.MediumWell: return "cooking.medium-well";
                case FoodDoneness.WellDone: return "cooking.well-done";
                case FoodDoneness.Burned: return "cooking.burned";
                default: return "cooking.raw";
            }
        }
    }
}
