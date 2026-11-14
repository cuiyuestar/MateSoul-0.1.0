using System;
using System.IO;
using System.Linq;
using MateSoul.Wardrobe;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MateSoul.Editor
{
    public static class LengyueWardrobeValidation
    {
        const string RequestName = "wardrobe-validate";

        [Serializable]
        sealed class ValidationReport
        {
            public string productionPrefab;
            public bool productionControllerConfigured;
            public bool definitionGenerated;
            public bool validOutfitApplied;
            public bool bonesRebound;
            public bool invalidOutfitRejected;
            public bool failedSwitchPreservedCurrentOutfit;
            public bool initialOutfitRestored;
            public bool passed;
        }

        [InitializeOnLoadMethod]
        static void Register()
        {
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (EditorApplication.isCompiling ||
                EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            string request =
                Path.GetFullPath(Application.dataPath + "/../..") +
                "/.dbg/" + RequestName;
            if (!File.Exists(request)) return;
            File.Delete(request);
            try { Run(); }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                File.WriteAllText(
                    LengyueLayeredDressBuilder.Reports +
                    "/wardrobe_foundation_failed.txt",
                    exception.ToString());
            }
        }

        [MenuItem("MateSoul/Lengyue/Wardrobe/Validate Foundation")]
        public static void Run()
        {
            Directory.CreateDirectory(
                LengyueLayeredDressBuilder.Reports);
            var report = new ValidationReport {
                productionPrefab =
                    LengyueLayeredDressBuilder.Prefab
            };

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                LengyueLayeredDressBuilder.Prefab);
            Require(prefab, "Production Lengyue prefab is missing.");
            var production =
                prefab.GetComponent<LengyueWardrobeController>();
            Require(production,
                "Production prefab has no wardrobe controller.");
            report.productionControllerConfigured =
                production.TryValidateConfiguration(out string error) &&
                production.InitialOutfitId == "lavender-casual";
            Require(
                report.productionControllerConfigured,
                error ?? "Production wardrobe is not configured.");

            GameObject character = null;
            GameObject sourceOutfit = null;
            Mesh validMesh = null;
            Mesh invalidMesh = null;
            Material material = null;
            LengyueOutfitDefinition valid = null;
            LengyueOutfitDefinition invalid = null;
            try
            {
                character = new GameObject(
                    "WardrobeValidationCharacter");
                var skeleton =
                    new GameObject("lengyue_bind_joints").transform;
                skeleton.SetParent(character.transform, false);
                var hips = new GameObject("lengyue_hips").transform;
                hips.SetParent(skeleton, false);
                var container =
                    new GameObject("Outfits").transform;
                container.SetParent(character.transform, false);
                var initial =
                    new GameObject("Initial_Garment");
                initial.transform.SetParent(container, false);
                var initialRenderer =
                    initial.AddComponent<SkinnedMeshRenderer>();
                initialRenderer.rootBone = hips;
                initialRenderer.bones = new[] { hips };

                Shader shader =
                    Shader.Find("Universal Render Pipeline/Lit") ??
                    Shader.Find("Standard");
                Require(shader, "Validation shader is unavailable.");
                material = new Material(shader);
                validMesh = CreateMesh("ValidWardrobeMesh");
                invalidMesh = CreateMesh("InvalidWardrobeMesh");
                sourceOutfit = CreateSourceOutfit(
                    validMesh, material);
                valid =
                    LengyueOutfitDefinitionBuilder.BuildDefinition(
                        sourceOutfit,
                        "validation-outfit",
                        "Validation Outfit");
                report.definitionGenerated =
                    valid.TryValidate(out error) &&
                    valid.Parts.Count == 1 &&
                    valid.Parts[0].BoneNames.SequenceEqual(
                        new[] { "lengyue_hips" });
                Require(report.definitionGenerated,
                    error ?? "Outfit definition generation failed.");
                invalid = CreateDefinition(
                    "missing-bone-outfit",
                    invalidMesh,
                    material,
                    "MissingBone");

                var controller =
                    character.AddComponent<LengyueWardrobeController>();
                controller.ConfigureInitialOutfit(
                    skeleton,
                    container,
                    "initial",
                    "Initial",
                    new[] { initial },
                    initialRenderer,
                    false,
                    false,
                    null,
                    null);
                controller.SetCatalog(new[] { valid, invalid });
                controller.Initialize();

                Require(
                    controller.TrySwitchOutfit(
                        valid.OutfitId, out error),
                    error);
                report.validOutfitApplied =
                    controller.CurrentOutfitId ==
                        valid.OutfitId &&
                    !initial.activeSelf &&
                    controller.IsUsingRuntimeOutfit;
                var generated = character
                    .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Single(renderer =>
                        renderer.name == "lengyue_validation_garment");
                report.bonesRebound =
                    generated.rootBone == hips &&
                    generated.bones.Length == 1 &&
                    generated.bones[0] == hips;
                Require(report.validOutfitApplied,
                    "Valid outfit was not activated.");
                Require(report.bonesRebound,
                    "Valid outfit bones were not rebound.");

                Transform activeRuntimeRoot =
                    generated.transform.parent;
                bool switched = controller.TrySwitchOutfit(
                    invalid.OutfitId, out error);
                report.invalidOutfitRejected =
                    !switched &&
                    error != null &&
                    error.Contains("MissingBone");
                report.failedSwitchPreservedCurrentOutfit =
                    controller.CurrentOutfitId ==
                        valid.OutfitId &&
                    activeRuntimeRoot &&
                    activeRuntimeRoot.gameObject.activeSelf;
                Require(report.invalidOutfitRejected,
                    "Missing-bone outfit was accepted.");
                Require(
                    report.failedSwitchPreservedCurrentOutfit,
                    "Failed switch changed the current outfit.");

                Require(
                    controller.TrySwitchOutfit(
                        "initial", out error),
                    error);
                report.initialOutfitRestored =
                    controller.CurrentOutfitId == "initial" &&
                    initial.activeSelf &&
                    !controller.IsUsingRuntimeOutfit;
                Require(report.initialOutfitRestored,
                    "Initial outfit was not restored.");
                report.passed = true;
            }
            finally
            {
                if (character)
                    UnityEngine.Object.DestroyImmediate(character);
                if (sourceOutfit)
                    UnityEngine.Object.DestroyImmediate(sourceOutfit);
                if (valid)
                    UnityEngine.Object.DestroyImmediate(valid);
                if (invalid)
                    UnityEngine.Object.DestroyImmediate(invalid);
                if (validMesh)
                    UnityEngine.Object.DestroyImmediate(validMesh);
                if (invalidMesh)
                    UnityEngine.Object.DestroyImmediate(invalidMesh);
                if (material)
                    UnityEngine.Object.DestroyImmediate(material);
            }

            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(
                LengyueLayeredDressBuilder.Reports +
                "/wardrobe_foundation.json",
                json);
            Debug.Log(
                "LENGYUE_WARDROBE_VALIDATION_PASS\n" + json);
        }

        static LengyueOutfitDefinition CreateDefinition(
            string id,
            Mesh mesh,
            Material material,
            string boneName)
        {
            var definition =
                ScriptableObject.CreateInstance<
                    LengyueOutfitDefinition>();
            definition.Configure(
                id,
                id,
                false,
                false,
                new[] {
                    new LengyueOutfitPartDefinition(
                        "lengyue_validation_garment",
                        LengyueOutfitPartRole.Garment,
                        mesh,
                        new[] { material },
                        boneName,
                        new[] { boneName },
                        Vector3.zero,
                        Quaternion.identity,
                        Vector3.one,
                        mesh.bounds,
                        SkinQuality.Bone4,
                        true,
                        ShadowCastingMode.On,
                        true)
                });
            return definition;
        }

        static GameObject CreateSourceOutfit(
            Mesh mesh,
            Material material)
        {
            var root = new GameObject("lengyue_validation_outfit");
            var hips = new GameObject("lengyue_hips").transform;
            hips.SetParent(root.transform, false);
            var part = new GameObject("lengyue_validation_garment");
            part.transform.SetParent(root.transform, false);
            var renderer =
                part.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.rootBone = hips;
            renderer.bones = new[] { hips };
            renderer.localBounds = mesh.bounds;
            renderer.quality = SkinQuality.Bone4;
            renderer.updateWhenOffscreen = true;
            return root;
        }

        static Mesh CreateMesh(string name)
        {
            var mesh = new Mesh {
                name = name,
                vertices = new[] {
                    new Vector3(-.1f, 0, 0),
                    new Vector3(.1f, 0, 0),
                    new Vector3(0, .2f, 0)
                },
                triangles = new[] { 0, 1, 2 },
                boneWeights = new[] {
                    Weight(), Weight(), Weight()
                },
                bindposes = new[] { Matrix4x4.identity }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static BoneWeight Weight() =>
            new BoneWeight {
                boneIndex0 = 0,
                weight0 = 1
            };

        static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
