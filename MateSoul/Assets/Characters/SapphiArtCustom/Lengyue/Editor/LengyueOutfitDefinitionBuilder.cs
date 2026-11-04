using System;
using System.Collections.Generic;
using System.Linq;
using MateSoul.Wardrobe;
using UnityEditor;
using UnityEngine;

namespace MateSoul.Editor
{
    public static class LengyueOutfitDefinitionBuilder
    {
        [MenuItem(
            "MateSoul/Lengyue/Wardrobe/Create Definition From Selection")]
        public static void CreateFromSelection()
        {
            GameObject root = Selection.activeGameObject;
            if (!root)
            {
                EditorUtility.DisplayDialog(
                    "Lengyue Wardrobe",
                    "Select the imported outfit root or an outfit prefab.",
                    "OK");
                return;
            }

            string defaultName =
                "Lengyue_" + SanitizeId(root.name) + ".asset";
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Lengyue Outfit Definition",
                defaultName,
                "asset",
                "Choose a location for the outfit definition.",
                LengyueLayeredDressBuilder.Root + "/Runtime/Wardrobe");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var definition = BuildDefinition(
                    root,
                    SanitizeId(root.name),
                    root.name);
                AssetDatabase.CreateAsset(definition, path);
                AssetDatabase.SaveAssets();
                Selection.activeObject = definition;
                EditorGUIUtility.PingObject(definition);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Lengyue Wardrobe",
                    exception.Message,
                    "OK");
            }
        }

        public static LengyueOutfitDefinition BuildDefinition(
            GameObject sourceRoot,
            string outfitId,
            string displayName)
        {
            if (!sourceRoot)
                throw new ArgumentNullException(nameof(sourceRoot));
            var renderers = sourceRoot
                .GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException(
                    "The selected outfit contains no skinned renderers.");

            var parts = new List<LengyueOutfitPartDefinition>();
            bool sleeveCloth = false;
            bool skirtGuard = false;
            foreach (var renderer in renderers)
            {
                ValidateRenderer(renderer);
                LengyueOutfitPartRole role =
                    InferRole(renderer.name);
                if (role == LengyueOutfitPartRole.Garment)
                    sleeveCloth |= HasSleeveClothMetadata(
                        renderer.sharedMesh);
                skirtGuard |= renderer.bones.Any(
                    bone => bone &&
                        (bone.name.StartsWith(
                            "Skirt_", StringComparison.Ordinal) ||
                         bone.name.StartsWith(
                            "OuterSkirt_", StringComparison.Ordinal)));

                Matrix4x4 relative =
                    sourceRoot.transform.worldToLocalMatrix *
                    renderer.transform.localToWorldMatrix;
                Decompose(
                    relative,
                    out Vector3 position,
                    out Quaternion rotation,
                    out Vector3 scale);
                parts.Add(new LengyueOutfitPartDefinition(
                    renderer.name,
                    role,
                    renderer.sharedMesh,
                    (Material[])renderer.sharedMaterials.Clone(),
                    renderer.rootBone.name,
                    renderer.bones.Select(bone => bone.name).ToArray(),
                    position,
                    rotation,
                    scale,
                    renderer.localBounds,
                    renderer.quality,
                    renderer.updateWhenOffscreen,
                    renderer.shadowCastingMode,
                    renderer.receiveShadows));
            }

            var duplicate = parts
                .GroupBy(part => part.PartName, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
                throw new InvalidOperationException(
                    $"Outfit repeats renderer name '{duplicate.Key}'.");

            var definition =
                ScriptableObject.CreateInstance<LengyueOutfitDefinition>();
            definition.name = displayName;
            definition.Configure(
                outfitId,
                displayName,
                sleeveCloth,
                skirtGuard,
                parts.ToArray());
            if (!definition.TryValidate(out string error))
            {
                UnityEngine.Object.DestroyImmediate(definition);
                throw new InvalidOperationException(error);
            }
            return definition;
        }

        static void ValidateRenderer(SkinnedMeshRenderer renderer)
        {
            if (!renderer.sharedMesh)
                throw new InvalidOperationException(
                    $"Renderer '{renderer.name}' has no mesh.");
            if (!renderer.rootBone)
                throw new InvalidOperationException(
                    $"Renderer '{renderer.name}' has no root bone.");
            if (renderer.bones == null ||
                renderer.bones.Any(bone => !bone))
                throw new InvalidOperationException(
                    $"Renderer '{renderer.name}' has missing bones.");
        }

        static LengyueOutfitPartRole InferRole(string name)
        {
            string lower = name.ToLowerInvariant();
            if (lower.Contains("skincoverage"))
                return LengyueOutfitPartRole.SkinCoverage;
            if (lower.Contains("neckextension"))
                return LengyueOutfitPartRole.NeckExtension;
            if (lower.Contains("shoe") || lower.Contains("boot"))
                return LengyueOutfitPartRole.Shoes;
            if (lower.Contains("garment") ||
                lower.Contains("dress") ||
                lower.Contains("outfit"))
                return LengyueOutfitPartRole.Garment;
            if (lower.Contains("accessory") ||
                lower.Contains("brooch"))
                return LengyueOutfitPartRole.Accessory;
            return LengyueOutfitPartRole.Other;
        }

        static bool HasSleeveClothMetadata(Mesh mesh)
        {
            Vector2[] parts = mesh.uv2;
            if (parts == null || parts.Length != mesh.vertexCount)
                return false;
            foreach (Vector2 value in parts)
            {
                int part = Mathf.RoundToInt(value.x);
                if (part == 3 || part == 5) return true;
            }
            return false;
        }

        static string SanitizeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "new-outfit";
            var result = new char[value.Length];
            int count = 0;
            foreach (char character in value.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(character))
                    result[count++] = character;
                else if ((character == '-' || character == '_' ||
                    char.IsWhiteSpace(character)) &&
                    count > 0 && result[count - 1] != '-')
                    result[count++] = '-';
            }
            string id = new string(result, 0, count).Trim('-');
            return string.IsNullOrEmpty(id) ? "new-outfit" : id;
        }

        static void Decompose(
            Matrix4x4 matrix,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            position = matrix.GetColumn(3);
            Vector3 right = matrix.GetColumn(0);
            Vector3 up = matrix.GetColumn(1);
            Vector3 forward = matrix.GetColumn(2);
            scale = new Vector3(
                right.magnitude,
                up.magnitude,
                forward.magnitude);
            if (matrix.determinant < 0)
            {
                scale.x = -scale.x;
                right = -right;
            }
            if (Mathf.Abs(scale.x) < 1e-8f ||
                Mathf.Abs(scale.y) < 1e-8f ||
                Mathf.Abs(scale.z) < 1e-8f)
                throw new InvalidOperationException(
                    "Outfit renderer has a zero transform scale.");
            rotation = Quaternion.LookRotation(
                forward / scale.z,
                up / scale.y);
        }
    }
}
