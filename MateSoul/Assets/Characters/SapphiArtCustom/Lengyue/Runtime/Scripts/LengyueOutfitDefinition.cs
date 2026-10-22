using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MateSoul.Wardrobe
{
    public enum LengyueOutfitPartRole
    {
        Garment,
        SkinCoverage,
        NeckExtension,
        Shoes,
        Accessory,
        Other
    }

    [Serializable]
    public sealed class LengyueOutfitPartDefinition
    {
        [SerializeField] string partName;
        [SerializeField] LengyueOutfitPartRole role;
        [SerializeField] Mesh mesh;
        [SerializeField] Material[] materials = Array.Empty<Material>();
        [SerializeField] string rootBoneName;
        [SerializeField] string[] boneNames = Array.Empty<string>();
        [SerializeField] Vector3 localPosition;
        [SerializeField] Quaternion localRotation = Quaternion.identity;
        [SerializeField] Vector3 localScale = Vector3.one;
        [SerializeField] Bounds localBounds;
        [SerializeField] SkinQuality skinQuality = SkinQuality.Bone4;
        [SerializeField] bool updateWhenOffscreen = true;
        [SerializeField] ShadowCastingMode shadowCastingMode =
            ShadowCastingMode.On;
        [SerializeField] bool receiveShadows = true;

        public string PartName => partName;
        public LengyueOutfitPartRole Role => role;
        public Mesh Mesh => mesh;
        public Material[] Materials => materials;
        public string RootBoneName => rootBoneName;
        public string[] BoneNames => boneNames;
        public Vector3 LocalPosition => localPosition;
        public Quaternion LocalRotation => localRotation;
        public Vector3 LocalScale => localScale;
        public Bounds LocalBounds => localBounds;
        public SkinQuality SkinQuality => skinQuality;
        public bool UpdateWhenOffscreen => updateWhenOffscreen;
        public ShadowCastingMode ShadowCastingMode => shadowCastingMode;
        public bool ReceiveShadows => receiveShadows;

        public LengyueOutfitPartDefinition(
            string partName,
            LengyueOutfitPartRole role,
            Mesh mesh,
            Material[] materials,
            string rootBoneName,
            string[] boneNames,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale,
            Bounds localBounds,
            SkinQuality skinQuality,
            bool updateWhenOffscreen,
            ShadowCastingMode shadowCastingMode,
            bool receiveShadows)
        {
            this.partName = partName;
            this.role = role;
            this.mesh = mesh;
            this.materials = materials ?? Array.Empty<Material>();
            this.rootBoneName = rootBoneName;
            this.boneNames = boneNames ?? Array.Empty<string>();
            this.localPosition = localPosition;
            this.localRotation = localRotation;
            this.localScale = localScale;
            this.localBounds = localBounds;
            this.skinQuality = skinQuality;
            this.updateWhenOffscreen = updateWhenOffscreen;
            this.shadowCastingMode = shadowCastingMode;
            this.receiveShadows = receiveShadows;
        }

        public bool TryValidate(out string error)
        {
            if (string.IsNullOrWhiteSpace(partName))
            {
                error = "Outfit part name is empty.";
                return false;
            }
            if (!mesh)
            {
                error = $"Outfit part '{partName}' has no mesh.";
                return false;
            }
            if (materials == null || materials.Length != mesh.subMeshCount)
            {
                error =
                    $"Outfit part '{partName}' needs exactly " +
                    $"{mesh.subMeshCount} materials.";
                return false;
            }
            for (int i = 0; i < materials.Length; i++)
                if (!materials[i])
                {
                    error =
                        $"Outfit part '{partName}' material {i} is missing.";
                    return false;
                }
            if (boneNames == null || boneNames.Length == 0 ||
                boneNames.Length != mesh.bindposes.Length)
            {
                error =
                    $"Outfit part '{partName}' bone-name count does not " +
                    "match its bind poses.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(rootBoneName))
            {
                error = $"Outfit part '{partName}' has no root bone name.";
                return false;
            }
            for (int i = 0; i < boneNames.Length; i++)
                if (string.IsNullOrWhiteSpace(boneNames[i]))
                {
                    error =
                        $"Outfit part '{partName}' bone name {i} is empty.";
                    return false;
                }
            if (localScale.x == 0 || localScale.y == 0 || localScale.z == 0)
            {
                error = $"Outfit part '{partName}' has a zero local scale.";
                return false;
            }
            error = null;
            return true;
        }
    }

    [CreateAssetMenu(
        menuName = "MateSoul/Lengyue Outfit Definition",
        fileName = "Lengyue_Outfit")]
    public sealed class LengyueOutfitDefinition : ScriptableObject
    {
        [SerializeField] string outfitId = "new-outfit";
        [SerializeField] string displayName = "New Outfit";
        [SerializeField] bool enableSleeveCloth;
        [SerializeField] bool enableSkirtPoseGuard;
        [SerializeField] LengyueOutfitPartDefinition[] parts =
            Array.Empty<LengyueOutfitPartDefinition>();

        public string OutfitId => outfitId;
        public string DisplayName => displayName;
        public bool EnableSleeveCloth => enableSleeveCloth;
        public bool EnableSkirtPoseGuard => enableSkirtPoseGuard;
        public IReadOnlyList<LengyueOutfitPartDefinition> Parts => parts;

        public void Configure(
            string id,
            string label,
            bool usesSleeveCloth,
            bool usesSkirtPoseGuard,
            LengyueOutfitPartDefinition[] outfitParts)
        {
            outfitId = id == null ? string.Empty : id.Trim();
            displayName = string.IsNullOrWhiteSpace(label)
                ? outfitId
                : label.Trim();
            enableSleeveCloth = usesSleeveCloth;
            enableSkirtPoseGuard = usesSkirtPoseGuard;
            parts = outfitParts ?? Array.Empty<LengyueOutfitPartDefinition>();
        }

        public LengyueOutfitPartDefinition FindPart(
            LengyueOutfitPartRole role)
        {
            if (parts == null) return null;
            foreach (var part in parts)
                if (part != null && part.Role == role)
                    return part;
            return null;
        }

        public bool TryValidate(out string error)
        {
            if (!IsValidOutfitId(outfitId))
            {
                error =
                    "Outfit ID must contain only letters, digits, '-' or '_'.";
                return false;
            }
            if (parts == null || parts.Length == 0)
            {
                error = $"Outfit '{outfitId}' has no parts.";
                return false;
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            int garmentCount = 0;
            foreach (var part in parts)
            {
                if (part == null)
                {
                    error = $"Outfit '{outfitId}' contains a null part.";
                    return false;
                }
                if (!part.TryValidate(out error))
                {
                    error = $"Outfit '{outfitId}': {error}";
                    return false;
                }
                if (!names.Add(part.PartName))
                {
                    error =
                        $"Outfit '{outfitId}' repeats part " +
                        $"'{part.PartName}'.";
                    return false;
                }
                if (part.Role == LengyueOutfitPartRole.Garment)
                    garmentCount++;
            }
            if (enableSleeveCloth && garmentCount != 1)
            {
                error =
                    $"Outfit '{outfitId}' must have exactly one garment " +
                    "part when sleeve cloth is enabled.";
                return false;
            }
            error = null;
            return true;
        }

        public static bool IsValidOutfitId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            foreach (char character in value)
                if (!char.IsLetterOrDigit(character) &&
                    character != '-' && character != '_')
                    return false;
            return true;
        }
    }
}
