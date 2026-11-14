using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    public enum ItemCategory
    {
        Furniture,
        Appliance,
        Tableware,
        Kitchenware,
        Food,
        Toy
    }

    public enum ItemPlacementSurface
    {
        Ground,
        Wall
    }

    public static class ItemCategoryDisplay
    {
        public static string GetDisplayName(this ItemCategory category)
        {
            switch (category)
            {
                case ItemCategory.Furniture: return "家具";
                case ItemCategory.Appliance: return "电器";
                case ItemCategory.Tableware: return "餐具";
                case ItemCategory.Kitchenware: return "厨具";
                case ItemCategory.Food: return "食品";
                case ItemCategory.Toy: return "玩具";
                default: throw new ArgumentOutOfRangeException(nameof(category), category, null);
            }
        }
    }

    public enum BreakSeverity
    {
        Light,
        Heavy
    }

    [Serializable]
    public struct BreakableSettings
    {
        public bool enabled;
        [Min(1f)] public float lightForceThreshold;
        [Min(1f)] public float heavyForceThreshold;
        [Range(2, 8)] public int lightFragmentCount;
        [Range(4, 24)] public int heavyFragmentCount;
        [Min(1f)] public float debrisLifetime;
        public GameObject lightDebrisPrefab;
        public GameObject heavyDebrisPrefab;
        public AudioClip breakSound;

        public BreakableSettings(float lightForce, float heavyForce, int lightFragments = 4,
            int heavyFragments = 12, float lifetime = 20f,
            GameObject lightPrefab = null, GameObject heavyPrefab = null,
            AudioClip sound = null)
        {
            enabled = true;
            lightForceThreshold = lightForce;
            heavyForceThreshold = heavyForce;
            lightFragmentCount = lightFragments;
            heavyFragmentCount = heavyFragments;
            debrisLifetime = lifetime;
            lightDebrisPrefab = lightPrefab;
            heavyDebrisPrefab = heavyPrefab;
            breakSound = sound;
        }

        public void Validate()
        {
            if (!enabled) return;
            if (!PhysicsBody.IsFinite(lightForceThreshold) || lightForceThreshold <= 0 ||
                !PhysicsBody.IsFinite(heavyForceThreshold) ||
                heavyForceThreshold <= lightForceThreshold ||
                lightFragmentCount < 2 || lightFragmentCount > 8 ||
                heavyFragmentCount < 4 || heavyFragmentCount > 24 ||
                heavyFragmentCount <= lightFragmentCount ||
                !PhysicsBody.IsFinite(debrisLifetime) || debrisLifetime < 1 ||
                (lightDebrisPrefab == null) != (heavyDebrisPrefab == null) ||
                breakSound == null)
                throw new ArgumentException(
                    "Breakable content requires ordered force thresholds, more heavy fragments, " +
                    "a finite lifetime, a complete optional debris prefab pair, and a break sound.");
        }
    }

    [Serializable]
    public struct CookingSettings
    {
        public bool enabled;
        [Min(.001f)] public float donenessPerDegreeSecond;

        public CookingSettings(float response = .025f)
        {
            enabled = true;
            donenessPerDegreeSecond = response;
        }

        public void Validate()
        {
            if (!enabled) return;
            if (!PhysicsBody.IsFinite(donenessPerDegreeSecond) ||
                donenessPerDegreeSecond <= 0)
                throw new ArgumentException(
                    "Cookable food requires a finite positive thermal response.");
        }
    }

    [Serializable]
    public struct CuttableSettings
    {
        public bool enabled;
        public Material interiorMaterial;
        public Mesh collisionProxy;
        public Vector3 origin;
        public Vector3 axis;
        [Min(.01f)] public float axisLength;
        [Min(.001f)] public float maximumRadius;
        public AnimationCurve radiusProfile;
        [Range(1, 4)] public int maximumCutDepth;
        [Range(.01f, .45f)] public float minimumPieceVolumeRatio;

        public CuttableSettings(Material cutMaterial, Mesh proxy, Vector3 localOrigin,
            Vector3 localAxis, float length, float radius, AnimationCurve profile,
            int depth = 3, float minimumRatio = .03f)
        {
            enabled = true;
            interiorMaterial = cutMaterial;
            collisionProxy = proxy;
            origin = localOrigin;
            axis = localAxis;
            axisLength = length;
            maximumRadius = radius;
            radiusProfile = profile;
            maximumCutDepth = depth;
            minimumPieceVolumeRatio = minimumRatio;
        }

        public Vector3 Axis => axis.sqrMagnitude < .001f ? Vector3.up : axis.normalized;

        public void Validate()
        {
            if (!enabled) return;
            if (interiorMaterial == null || collisionProxy == null ||
                !PhysicsBody.IsFinite(origin) || !PhysicsBody.IsFinite(axis) ||
                axis.sqrMagnitude < .001f ||
                !PhysicsBody.IsFinite(axisLength) || axisLength <= 0 ||
                !PhysicsBody.IsFinite(maximumRadius) || maximumRadius <= 0 ||
                radiusProfile == null || radiusProfile.length < 2 ||
                maximumCutDepth < 1 || maximumCutDepth > 4 ||
                !PhysicsBody.IsFinite(minimumPieceVolumeRatio) ||
                minimumPieceVolumeRatio < .01f || minimumPieceVolumeRatio > .45f)
                throw new ArgumentException(
                    "Cuttable content needs an interior material, readable convex proxy, " +
                    "finite local axis, radius profile, cut limit, and piece threshold.");
        }

        public float RadiusAt(Vector3 localPosition)
        {
            var distance = Vector3.Dot(localPosition - origin, Axis);
            var normalized = Mathf.Clamp01(distance / axisLength);
            var scale = radiusProfile == null || radiusProfile.length == 0
                ? 1f
                : Mathf.Max(.04f, radiusProfile.Evaluate(normalized));
            return maximumRadius * scale;
        }

        public void Basis(out Vector3 first, out Vector3 second)
        {
            var direction = Axis;
            var reference = Mathf.Abs(Vector3.Dot(direction, Vector3.forward)) < .9f
                ? Vector3.forward
                : Vector3.right;
            first = Vector3.Cross(direction, reference).normalized;
            second = Vector3.Cross(direction, first).normalized;
        }

        public Vector2 CrossSectionUv(Vector3 localPosition)
        {
            Basis(out var first, out var second);
            var distance = Vector3.Dot(localPosition - origin, Axis);
            var center = origin + Axis * distance;
            var radial = localPosition - center;
            var diameter = Mathf.Max(.001f, RadiusAt(localPosition) * 2f);
            return new Vector2(.5f + Vector3.Dot(radial, first) / diameter,
                .5f + Vector3.Dot(radial, second) / diameter);
        }

        public Vector2 LongitudinalUv(Vector3 localPosition)
        {
            Basis(out var first, out _);
            var relative = localPosition - origin;
            var distance = Vector3.Dot(relative, Axis);
            var center = origin + Axis * distance;
            var radius = Mathf.Max(.001f, RadiusAt(localPosition));
            return new Vector2(.5f + Vector3.Dot(localPosition - center, first) /
                (radius * 2f), distance / Mathf.Max(.001f, axisLength));
        }
    }

    [Serializable]
    public struct ItemPlacementPart
    {
        [SerializeField] private ItemDefinition definition;
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Vector3 localEulerAngles;

        public ItemDefinition Definition => definition;
        public Vector3 LocalPosition => localPosition;
        public Quaternion LocalRotation => Quaternion.Euler(localEulerAngles);

        public ItemPlacementPart(ItemDefinition item, Vector3 position, Quaternion rotation)
        {
            definition = item;
            localPosition = position;
            localEulerAngles = rotation.eulerAngles;
        }

        public void Validate(ItemDefinition owner)
        {
            if (definition == null || definition == owner || definition.IsPlacementGroup)
                throw new ArgumentException("Placement parts must reference non-group item definitions.");
        }
    }

    [CreateAssetMenu(menuName = "MateSoul/Item")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private GameObject prefab;
        [SerializeField] private string[] capabilities = { "visit" };
        [SerializeField] private Vector3 approachOffset = new Vector3(0, 0, -.65f);
        [SerializeField] private bool catalogVisible;
        [SerializeField] private ItemCategory category;
        [SerializeField] private Vector2 footprint = new Vector2(.5f, .5f);
        [SerializeField, Min(.1f)] private float placementHeight = .5f;
        [SerializeField] private Material focusMaterial;
        [SerializeField] private BodySettings physics;
        [SerializeField] private bool allowSurfacePlacement;
        [SerializeField, Min(0)] private int inventoryLimit = 1;
        [SerializeField] private bool unlimitedInventory;
        [SerializeField] private bool restockDaily;
        [SerializeField] private bool fixedAfterPlacement;
        [SerializeField] private bool placementPreview;
        [SerializeField] private ItemPlacementSurface placementSurface;
        [SerializeField] private bool immovableAfterPlacement;
        [SerializeField] private float placementFacingYaw;
        [SerializeField, Range(.5f, 1f)] private float minimumSurfaceSupportRatio = 1f;
        [SerializeField] private bool providesPlacementSurface;
        [SerializeField] private BreakableSettings breakable;
        [SerializeField] private ImpactSoundSettings impactSound;
        [SerializeField] private CookingSettings cooking;
        [SerializeField] private CuttableSettings cuttable;
        [SerializeField] private ItemInteractionProfile interaction;
        [SerializeField] private ItemUseAnimation useAnimation;
        [SerializeField] private bool allowAutonomousUse;
        [SerializeField] private bool edible;
        [SerializeField] private bool consumedProduct;
        [SerializeField] private ItemPlacementPart[] placementParts = Array.Empty<ItemPlacementPart>();
        public string Id => id;
        public string DisplayName => displayName;
        public GameObject Prefab => prefab;
        public string[] Capabilities => (string[])capabilities.Clone();
        public Vector3 ApproachOffset => approachOffset;
        public bool CatalogVisible => catalogVisible;
        public ItemCategory Category => category;
        public Vector2 Footprint => footprint;
        public float PlacementHeight => placementHeight;
        public Material FocusMaterial => focusMaterial;
        public BodySettings Physics => physics;
        public bool AllowSurfacePlacement => allowSurfacePlacement;
        public int InventoryLimit => inventoryLimit;
        public bool UnlimitedInventory => unlimitedInventory;
        public bool RestockDaily => restockDaily;
        public bool FixedAfterPlacement => fixedAfterPlacement;
        public bool PlacementPreview => placementPreview;
        public ItemPlacementSurface PlacementSurface => placementSurface;
        public bool ImmovableAfterPlacement => immovableAfterPlacement;
        public float PlacementFacingYaw => placementFacingYaw;
        public float MinimumSurfaceSupportRatio => minimumSurfaceSupportRatio > 0
            ? Mathf.Clamp(minimumSurfaceSupportRatio, .5f, 1f)
            : 1f;
        public bool ProvidesPlacementSurface => providesPlacementSurface;
        public BreakableSettings Breakable => breakable;
        public ImpactSoundSettings ImpactSound => impactSound;
        public CookingSettings Cooking => cooking;
        public CuttableSettings Cuttable => cuttable;
        public ItemInteractionProfile Interaction => interaction;
        public ItemUseAnimation UseAnimation => useAnimation;
        public bool AllowAutonomousUse => allowAutonomousUse;
        public bool ConsumedProduct => consumedProduct;
        public bool IsEdible => edible;

        public void ConfigureEdible(bool value) => edible = value;

        public void ConfigureUse(ItemUseAnimation animation, bool autonomous)
        {
            if (autonomous && (animation == null || animation.clip == null))
                throw new ArgumentException("Autonomous item use requires an authored animation.");
            useAnimation = animation;
            allowAutonomousUse = autonomous;
        }

        public void ConfigureInteraction(ItemInteractionProfile value, bool isConsumedProduct = false)
        {
            interaction = value;
            consumedProduct = isConsumedProduct;
        }
        public bool IsPlacementGroup => placementParts != null && placementParts.Length > 0;
        public ItemPlacementPart[] PlacementParts => placementParts == null
            ? Array.Empty<ItemPlacementPart>()
            : (ItemPlacementPart[])placementParts.Clone();

        public void ConfigurePhysics(BodySettings value, bool onSurfaces = false)
        {
            value.Validate();
            physics = value;
            allowSurfacePlacement = onSurfaces;
        }

        public void ConfigureInventory(int limit, bool daily = false)
        {
            if (limit < 0 || daily && limit == 0)
                throw new ArgumentException("Inventory limits must be non-negative.");
            inventoryLimit = limit;
            unlimitedInventory = false;
            restockDaily = daily;
        }

        public void ConfigureUnlimitedInventory()
        {
            inventoryLimit = 0;
            unlimitedInventory = true;
            restockDaily = false;
        }

        public void ConfigurePlacement(bool fixedInPlace, bool showPreview = false,
            ItemPlacementSurface surface = ItemPlacementSurface.Ground,
            bool immovable = false)
        {
            if (showPreview != immovable)
                throw new ArgumentException(
                    "Placement previews are reserved for immovable items.");
            if (surface == ItemPlacementSurface.Wall && !showPreview)
                throw new ArgumentException("Wall items require placement previews.");
            if (immovable && !fixedInPlace)
                throw new ArgumentException("Immovable items must also prohibit direct carrying.");
            fixedAfterPlacement = fixedInPlace;
            placementPreview = showPreview;
            placementSurface = surface;
            immovableAfterPlacement = immovable;
        }

        public void ConfigurePlacementFacing(float yawDegrees)
        {
            if (!PhysicsBody.IsFinite(yawDegrees))
                throw new ArgumentException("Placement facing yaw must be finite.");
            placementFacingYaw = Mathf.Repeat(yawDegrees, 360f);
        }

        public void ConfigureSurfaceSupport(float minimumRatio)
        {
            if (!PhysicsBody.IsFinite(minimumRatio) ||
                minimumRatio < .5f || minimumRatio > 1f)
                throw new ArgumentException(
                    "Surface support ratio must be between 0.5 and 1.");
            minimumSurfaceSupportRatio = minimumRatio;
        }

        public void ConfigurePlacementSurface(bool value) => providesPlacementSurface = value;

        public Quaternion ResolvePlacementRotation(Vector3 facesPlayer)
        {
            var direction = Vector3.ProjectOnPlane(facesPlayer, Vector3.up).normalized;
            if (direction.sqrMagnitude < .01f) direction = Vector3.forward;
            return Quaternion.LookRotation(direction, Vector3.up) *
                   Quaternion.Euler(0, placementFacingYaw, 0);
        }

        public void ConfigureBreakable(BreakableSettings value)
        {
            value.Validate();
            breakable = value;
        }

        public void ClearBreakable() => breakable = default;

        public void ConfigureImpactSound(ImpactSoundSettings value)
        {
            value.Validate();
            impactSound = value;
        }

        public void ClearImpactSound() => impactSound = default;

        public void ConfigureCooking(CookingSettings value)
        {
            value.Validate();
            cooking = value;
        }

        public void ClearCooking() => cooking = default;

        public void ConfigureCuttable(CuttableSettings value)
        {
            value.Validate();
            cuttable = value;
        }

        public void ClearCuttable() => cuttable = default;

        public void ConfigurePlacementParts(params ItemPlacementPart[] parts)
        {
            placementParts = parts == null
                ? Array.Empty<ItemPlacementPart>()
                : (ItemPlacementPart[])parts.Clone();
            foreach (var part in placementParts) part.Validate(this);
        }

        public void Configure(string definitionId, string title, GameObject view,
            string[] supportedCapabilities, bool visibleInCatalog, Vector2 groundFootprint,
            float height, Material highlight, ItemCategory itemCategory)
        {
            if (string.IsNullOrWhiteSpace(definitionId) || view == null ||
                groundFootprint.x <= 0 || groundFootprint.y <= 0 || height <= 0)
                throw new ArgumentException("An item needs an ID, prefab, and positive dimensions.");
            id = definitionId;
            displayName = title;
            prefab = view;
            capabilities = (string[])supportedCapabilities.Clone();
            catalogVisible = visibleInCatalog;
            category = itemCategory;
            footprint = groundFootprint;
            placementHeight = height;
            focusMaterial = highlight;
        }
    }
}
