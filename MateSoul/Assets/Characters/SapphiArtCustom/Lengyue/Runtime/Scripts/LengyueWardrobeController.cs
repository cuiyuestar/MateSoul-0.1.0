using System;
using System.Collections.Generic;
using MateSoul.Cloth;
using UnityEngine;
using UnityEngine.Networking;

namespace MateSoul.Wardrobe
{
    /// <summary>
    /// Owns one character skeleton and switches complete outfit sets atomically.
    /// A failed definition or bone bind leaves the current outfit untouched.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class LengyueWardrobeController : MonoBehaviour
    {
        [Header("Character")]
        [SerializeField] Transform skeletonRoot;
        [SerializeField] Transform outfitContainer;

        [Header("Initial Outfit")]
        [SerializeField] string initialOutfitId = "lavender-casual";
        [SerializeField] string initialDisplayName = "Lavender Casual";
        [SerializeField] GameObject[] initialOutfitObjects =
            Array.Empty<GameObject>();
        [SerializeField] SkinnedMeshRenderer initialGarment;
        [SerializeField] bool initialUsesSleeveCloth = true;
        [SerializeField] bool initialUsesSkirtPoseGuard = true;

        [Header("Catalog")]
        [SerializeField] LengyueOutfitDefinition[] outfits =
            Array.Empty<LengyueOutfitDefinition>();

        [Header("Simulation")]
        [SerializeField] LengyueSleeveCloth sleeveCloth;
        [SerializeField] SkirtPoseGuard skirtPoseGuard;

        GameObject runtimeOutfitRoot;
        SkinnedMeshRenderer currentGarment;
        string currentOutfitId;
        bool currentUsesSleeveCloth;
        bool currentUsesSkirtPoseGuard;
        bool initialized;

        public string InitialOutfitId => initialOutfitId;
        public string InitialDisplayName => initialDisplayName;
        public string CurrentOutfitId => currentOutfitId;
        public string CurrentDisplayName
        {
            get
            {
                if (string.Equals(currentOutfitId, initialOutfitId, StringComparison.Ordinal))
                    return initialDisplayName;
                if (outfits != null)
                    foreach (var outfit in outfits)
                        if (outfit && string.Equals(
                            outfit.OutfitId, currentOutfitId, StringComparison.Ordinal))
                            return outfit.DisplayName;
                throw new InvalidOperationException(
                    $"Current outfit '{currentOutfitId}' is not present in the wardrobe catalog.");
            }
        }
        public IReadOnlyList<LengyueOutfitDefinition> Outfits => outfits;
        public bool IsInitialized => initialized;
        public bool IsUsingRuntimeOutfit => runtimeOutfitRoot;
        public event Action<string> OutfitChanged;

        sealed class PartBinding
        {
            public LengyueOutfitPartDefinition definition;
            public Transform rootBone;
            public Transform[] bones;
        }

        void Awake() => Initialize();

        public void Initialize()
        {
            if (initialized) return;
            if (!TryValidateConfiguration(out string error))
                throw new InvalidOperationException(error);
            currentOutfitId = initialOutfitId;
            currentGarment = initialGarment;
            currentUsesSleeveCloth = initialUsesSleeveCloth;
            currentUsesSkirtPoseGuard = initialUsesSkirtPoseGuard;
            initialized = true;
            ConfigureSimulation(
                currentGarment,
                currentUsesSleeveCloth,
                currentUsesSkirtPoseGuard);
        }

        public void ConfigureInitialOutfit(
            Transform characterSkeletonRoot,
            Transform runtimeOutfitContainer,
            string outfitId,
            string displayName,
            GameObject[] outfitObjects,
            SkinnedMeshRenderer garment,
            bool usesSleeveCloth,
            bool usesSkirtPoseGuard,
            LengyueSleeveCloth cloth,
            SkirtPoseGuard skirtGuard)
        {
            skeletonRoot = characterSkeletonRoot;
            outfitContainer = runtimeOutfitContainer;
            initialOutfitId = outfitId;
            initialDisplayName = displayName;
            initialOutfitObjects =
                outfitObjects ?? Array.Empty<GameObject>();
            initialGarment = garment;
            initialUsesSleeveCloth = usesSleeveCloth;
            initialUsesSkirtPoseGuard = usesSkirtPoseGuard;
            sleeveCloth = cloth;
            skirtPoseGuard = skirtGuard;
            initialized = false;
        }

        public void SetCatalog(LengyueOutfitDefinition[] definitions)
        {
            outfits = definitions ?? Array.Empty<LengyueOutfitDefinition>();
        }

        public bool TrySwitchOutfit(string outfitId, out string error)
        {
            if (!initialized)
            {
                try { Initialize(); }
                catch (Exception exception)
                {
                    error = exception.Message;
                    return false;
                }
            }
            if (string.Equals(
                currentOutfitId, outfitId, StringComparison.Ordinal))
            {
                error = null;
                return true;
            }
            if (string.Equals(
                initialOutfitId, outfitId, StringComparison.Ordinal))
                return TrySwitchToInitial(out error);

            LengyueOutfitDefinition match = null;
            if (outfits != null)
                foreach (var candidate in outfits)
                    if (candidate &&
                        string.Equals(
                            candidate.OutfitId,
                            outfitId,
                            StringComparison.Ordinal))
                    {
                        match = candidate;
                        break;
                    }
            if (!match)
            {
                error = $"Unknown Lengyue outfit ID: '{outfitId}'.";
                return false;
            }
            return TrySwitchOutfit(match, out error);
        }

        public bool TrySwitchOutfit(
            LengyueOutfitDefinition definition,
            out string error)
        {
            if (!definition)
            {
                error = "Outfit definition is missing.";
                return false;
            }
            if (!initialized)
            {
                try { Initialize(); }
                catch (Exception exception)
                {
                    error = exception.Message;
                    return false;
                }
            }
            if (string.Equals(
                definition.OutfitId,
                initialOutfitId,
                StringComparison.Ordinal))
                return TrySwitchToInitial(out error);
            if (string.Equals(
                definition.OutfitId,
                currentOutfitId,
                StringComparison.Ordinal))
            {
                error = null;
                return true;
            }
            if (!TryBuildRuntimeOutfit(
                definition,
                out GameObject stagedRoot,
                out SkinnedMeshRenderer stagedGarment,
                out error))
                return false;

            GameObject previousRoot = runtimeOutfitRoot;
            SkinnedMeshRenderer previousGarment = currentGarment;
            string previousId = currentOutfitId;
            bool previousSleeve = currentUsesSleeveCloth;
            bool previousSkirt = currentUsesSkirtPoseGuard;
            try
            {
                stagedRoot.SetActive(true);
                ConfigureSimulation(
                    stagedGarment,
                    definition.EnableSleeveCloth,
                    definition.EnableSkirtPoseGuard);
                SetInitialObjectsActive(false);
                if (previousRoot) previousRoot.SetActive(false);

                runtimeOutfitRoot = stagedRoot;
                currentGarment = stagedGarment;
                currentOutfitId = definition.OutfitId;
                currentUsesSleeveCloth = definition.EnableSleeveCloth;
                currentUsesSkirtPoseGuard =
                    definition.EnableSkirtPoseGuard;
                if (previousRoot) DestroyGeneratedObject(previousRoot);
            }
            catch (Exception exception)
            {
                stagedRoot.SetActive(false);
                DestroyGeneratedObject(stagedRoot);
                if (previousRoot) previousRoot.SetActive(true);
                RestoreSimulation(
                    previousGarment, previousSleeve, previousSkirt);
                currentGarment = previousGarment;
                currentOutfitId = previousId;
                currentUsesSleeveCloth = previousSleeve;
                currentUsesSkirtPoseGuard = previousSkirt;
                error =
                    $"Outfit '{definition.OutfitId}' was not applied: " +
                    exception.Message;
                return false;
            }
            NotifyOutfitChanged();
            error = null;
            return true;
        }

        public void SwitchOutfit(string outfitId)
        {
            if (!TrySwitchOutfit(outfitId, out string error))
                throw new InvalidOperationException(error);
        }

        public bool TryValidateConfiguration(out string error)
        {
            if (!skeletonRoot)
            {
                error = "Wardrobe skeleton root is missing.";
                return false;
            }
            if (!outfitContainer)
            {
                error = "Wardrobe outfit container is missing.";
                return false;
            }
            if (!LengyueOutfitDefinition.IsValidOutfitId(initialOutfitId))
            {
                error = "Wardrobe initial outfit ID is invalid.";
                return false;
            }
            if (initialOutfitObjects == null ||
                initialOutfitObjects.Length == 0)
            {
                error = "Wardrobe initial outfit objects are missing.";
                return false;
            }
            foreach (var item in initialOutfitObjects)
                if (!item)
                {
                    error =
                        "Wardrobe initial outfit contains a missing object.";
                    return false;
                }
            if (!initialGarment)
            {
                error = "Wardrobe initial garment renderer is missing.";
                return false;
            }
            if (initialUsesSleeveCloth && !sleeveCloth)
            {
                error = "Wardrobe sleeve cloth component is missing.";
                return false;
            }
            if (initialUsesSkirtPoseGuard && !skirtPoseGuard)
            {
                error = "Wardrobe skirt pose guard is missing.";
                return false;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal) {
                initialOutfitId
            };
            if (outfits != null)
                foreach (var definition in outfits)
                {
                    if (!definition)
                    {
                        error =
                            "Wardrobe catalog contains a missing definition.";
                        return false;
                    }
                    if (!definition.TryValidate(out error)) return false;
                    if (!ids.Add(definition.OutfitId))
                    {
                        error =
                            $"Wardrobe outfit ID '{definition.OutfitId}' " +
                            "is duplicated.";
                        return false;
                    }
                    if (definition.EnableSleeveCloth && !sleeveCloth)
                    {
                        error =
                            $"Outfit '{definition.OutfitId}' needs the " +
                            "sleeve cloth component.";
                        return false;
                    }
                    if (definition.EnableSkirtPoseGuard && !skirtPoseGuard)
                    {
                        error =
                            $"Outfit '{definition.OutfitId}' needs the " +
                            "skirt pose guard.";
                        return false;
                    }
                }
            error = null;
            return true;
        }

        bool TrySwitchToInitial(out string error)
        {
            GameObject previousRoot = runtimeOutfitRoot;
            SkinnedMeshRenderer previousGarment = currentGarment;
            string previousId = currentOutfitId;
            bool previousSleeve = currentUsesSleeveCloth;
            bool previousSkirt = currentUsesSkirtPoseGuard;
            try
            {
                ConfigureSimulation(
                    initialGarment,
                    initialUsesSleeveCloth,
                    initialUsesSkirtPoseGuard);
                SetInitialObjectsActive(true);
                if (previousRoot) previousRoot.SetActive(false);

                runtimeOutfitRoot = null;
                currentGarment = initialGarment;
                currentOutfitId = initialOutfitId;
                currentUsesSleeveCloth = initialUsesSleeveCloth;
                currentUsesSkirtPoseGuard =
                    initialUsesSkirtPoseGuard;
                if (previousRoot) DestroyGeneratedObject(previousRoot);
            }
            catch (Exception exception)
            {
                SetInitialObjectsActive(false);
                if (previousRoot) previousRoot.SetActive(true);
                RestoreSimulation(
                    previousGarment, previousSleeve, previousSkirt);
                currentGarment = previousGarment;
                currentOutfitId = previousId;
                currentUsesSleeveCloth = previousSleeve;
                currentUsesSkirtPoseGuard = previousSkirt;
                error =
                    $"Initial outfit was not restored: " +
                    exception.Message;
                return false;
            }
            NotifyOutfitChanged();
            error = null;
            return true;
        }

        bool TryBuildRuntimeOutfit(
            LengyueOutfitDefinition definition,
            out GameObject root,
            out SkinnedMeshRenderer garment,
            out string error)
        {
            root = null;
            garment = null;
            if (!definition.TryValidate(out error)) return false;
            if (!TryBuildBoneLookup(
                out Dictionary<string, Transform> bones,
                out error))
                return false;

            var bindings = new List<PartBinding>();
            foreach (var part in definition.Parts)
            {
                if (!TryResolveBone(
                    bones, part.RootBoneName, out Transform rootBone))
                {
                    error =
                        $"Outfit '{definition.OutfitId}' is missing target " +
                        $"root bone '{part.RootBoneName}'.";
                    return false;
                }
                var rebound = new Transform[part.BoneNames.Length];
                for (int i = 0; i < rebound.Length; i++)
                    if (!TryResolveBone(
                        bones, part.BoneNames[i], out rebound[i]))
                    {
                        error =
                            $"Outfit '{definition.OutfitId}' part " +
                            $"'{part.PartName}' is missing target bone " +
                            $"'{part.BoneNames[i]}'.";
                        return false;
                    }
                bindings.Add(new PartBinding {
                    definition = part,
                    rootBone = rootBone,
                    bones = rebound
                });
            }

            root = new GameObject(
                $"Lengyue_Outfit_{definition.OutfitId}");
            root.SetActive(false);
            root.transform.SetParent(outfitContainer, false);
            int layer = initialGarment.gameObject.layer;
            try
            {
                foreach (var binding in bindings)
                {
                    var part = binding.definition;
                    var partObject = new GameObject(part.PartName);
                    partObject.layer = layer;
                    partObject.transform.SetParent(root.transform, false);
                    partObject.transform.localPosition = part.LocalPosition;
                    partObject.transform.localRotation = part.LocalRotation;
                    partObject.transform.localScale = part.LocalScale;
                    var renderer =
                        partObject.AddComponent<SkinnedMeshRenderer>();
                    renderer.sharedMesh = part.Mesh;
                    renderer.sharedMaterials =
                        (Material[])part.Materials.Clone();
                    renderer.rootBone = binding.rootBone;
                    renderer.bones = binding.bones;
                    renderer.localBounds =
                        part.LocalBounds.size.sqrMagnitude > 0
                            ? part.LocalBounds
                            : part.Mesh.bounds;
                    renderer.quality = part.SkinQuality;
                    renderer.updateWhenOffscreen =
                        part.UpdateWhenOffscreen;
                    renderer.shadowCastingMode =
                        part.ShadowCastingMode;
                    renderer.receiveShadows = part.ReceiveShadows;
                    if (part.Role ==
                        LengyueOutfitPartRole.Garment)
                    {
                        if (garment)
                            throw new InvalidOperationException(
                                "Outfit has more than one garment part.");
                        garment = renderer;
                    }
                }
                if (definition.EnableSleeveCloth && !garment)
                    throw new InvalidOperationException(
                        "Sleeve cloth outfit has no garment renderer.");
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                DestroyGeneratedObject(root);
                root = null;
                garment = null;
                error =
                    $"Outfit '{definition.OutfitId}' could not be built: " +
                    exception.Message;
                return false;
            }
        }

        bool TryBuildBoneLookup(
            out Dictionary<string, Transform> lookup,
            out string error)
        {
            lookup =
                new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var bone in
                skeletonRoot.GetComponentsInChildren<Transform>(true))
            {
                if (lookup.ContainsKey(bone.name))
                {
                    error =
                        $"Character skeleton repeats bone name " +
                        $"'{bone.name}'.";
                    return false;
                }
                lookup.Add(bone.name, bone);
            }
            error = null;
            return true;
        }

        static bool TryResolveBone(
            Dictionary<string, Transform> lookup,
            string sourceName,
            out Transform bone)
        {
            if (lookup.TryGetValue(sourceName, out bone)) return true;
            if (sourceName == "Head" &&
                lookup.TryGetValue("Head 1", out bone))
                return true;
            bone = null;
            return false;
        }

        void ConfigureSimulation(
            SkinnedMeshRenderer garment,
            bool useSleeveCloth,
            bool useSkirtGuard)
        {
            if (useSleeveCloth)
            {
                if (!sleeveCloth)
                    throw new InvalidOperationException(
                        "Sleeve cloth component is unavailable.");
                if (!garment)
                    throw new InvalidOperationException(
                        "Sleeve cloth garment is unavailable.");
                sleeveCloth.ConfigureForOutfit(garment, true);
            }
            else if (sleeveCloth)
                sleeveCloth.ConfigureForOutfit(null, false);

            if (useSkirtGuard)
            {
                if (!skirtPoseGuard)
                    throw new InvalidOperationException(
                        "Skirt pose guard is unavailable.");
                skirtPoseGuard.SetActiveForOutfit(true);
            }
            else if (skirtPoseGuard)
                skirtPoseGuard.SetActiveForOutfit(false);
        }

        void RestoreSimulation(
            SkinnedMeshRenderer garment,
            bool useSleeveCloth,
            bool useSkirtGuard)
        {
            try
            {
                ConfigureSimulation(
                    garment, useSleeveCloth, useSkirtGuard);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        void SetInitialObjectsActive(bool value)
        {
            if (initialOutfitObjects == null) return;
            foreach (var item in initialOutfitObjects)
                if (item) item.SetActive(value);
        }

        void NotifyOutfitChanged()
        {
            try { OutfitChanged?.Invoke(currentOutfitId); }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        static void DestroyGeneratedObject(UnityEngine.Object value)
        {
            if (!value) return;
            if (Application.isPlaying)
                Destroy(value);
            else
                DestroyImmediate(value);
        }
    }
}
