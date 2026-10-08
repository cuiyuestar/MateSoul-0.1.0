using System;
using UnityEngine;

namespace MateSoul.Runtime
{
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

        public BreakableSettings(float lightForce, float heavyForce, int lightFragments = 4,
            int heavyFragments = 12, float lifetime = 20f,
            GameObject lightPrefab = null, GameObject heavyPrefab = null)
        {
            enabled = true;
            lightForceThreshold = lightForce;
            heavyForceThreshold = heavyForce;
            lightFragmentCount = lightFragments;
            heavyFragmentCount = heavyFragments;
            debrisLifetime = lifetime;
            lightDebrisPrefab = lightPrefab;
            heavyDebrisPrefab = heavyPrefab;
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
                (lightDebrisPrefab == null) != (heavyDebrisPrefab == null))
                throw new ArgumentException(
                    "Breakable content requires ordered force thresholds, more heavy fragments, " +
                    "a finite lifetime, and a complete optional debris prefab pair.");
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
        [SerializeField] private Vector2 footprint = new Vector2(.5f, .5f);
        [SerializeField, Min(.1f)] private float placementHeight = .5f;
        [SerializeField] private Material focusMaterial;
        [SerializeField] private BodySettings physics;
        [SerializeField] private bool allowSurfacePlacement;
        [SerializeField] private BreakableSettings breakable;
        [SerializeField] private ItemPlacementPart[] placementParts = Array.Empty<ItemPlacementPart>();
        public string Id => id;
        public string DisplayName => displayName;
        public GameObject Prefab => prefab;
        public string[] Capabilities => (string[])capabilities.Clone();
        public Vector3 ApproachOffset => approachOffset;
        public bool CatalogVisible => catalogVisible;
        public Vector2 Footprint => footprint;
        public float PlacementHeight => placementHeight;
        public Material FocusMaterial => focusMaterial;
        public BodySettings Physics => physics;
        public bool AllowSurfacePlacement => allowSurfacePlacement;
        public BreakableSettings Breakable => breakable;
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

        public void ConfigureBreakable(BreakableSettings value)
        {
            value.Validate();
            breakable = value;
        }

        public void ClearBreakable() => breakable = default;

        public void ConfigurePlacementParts(params ItemPlacementPart[] parts)
        {
            placementParts = parts == null
                ? Array.Empty<ItemPlacementPart>()
                : (ItemPlacementPart[])parts.Clone();
            foreach (var part in placementParts) part.Validate(this);
        }

        public void Configure(string definitionId, string title, GameObject view,
            string[] supportedCapabilities, bool visibleInCatalog, Vector2 groundFootprint,
            float height, Material highlight)
        {
            if (string.IsNullOrWhiteSpace(definitionId) || view == null ||
                groundFootprint.x <= 0 || groundFootprint.y <= 0 || height <= 0)
                throw new ArgumentException("An item needs an ID, prefab, and positive dimensions.");
            id = definitionId;
            displayName = title;
            prefab = view;
            capabilities = (string[])supportedCapabilities.Clone();
            catalogVisible = visibleInCatalog;
            footprint = groundFootprint;
            placementHeight = height;
            focusMaterial = highlight;
        }
    }
}
