using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public static class SapphiArtHairLab
    {
        const string Vendor = "Assets/SapphiArt/SapphiArtchan";
        const string Custom = "Assets/Characters/SapphiArtCustom";
        const string HairPath = Custom + "/Hair/Models/Lengyue_Bob_v001.fbx";
        const string PrefabPath = Custom + "/Prefabs/SapphiArt_LengyueBob.prefab";
        static readonly string[] OldHair = { "hairFront", "hairBack", "hairAcce" };
        static readonly string[] Markers = { "Head", "Head_End", "Eye_L", "Eye_R" };
        static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        static readonly string Reports = Path.GetFullPath(
            Path.Combine(Application.dataPath, "../../ArtSource/SapphiArt/HairLab/reports"));
        static readonly string Previews = Path.GetFullPath(
            Path.Combine(Application.dataPath, "../../ArtSource/SapphiArt/HairLab/previews"));

        [Serializable]
        class Report
        {
            public string unityVersion;
            public string graphicsDevice;
            public bool prefabIsVariant;
            public bool originalMeshesUnchanged;
            public bool originalTransformsUnchanged;
            public bool originalBonesUnchanged;
            public int originalShapeCount;
            public int variantShapeCount;
            public int hairTriangles;
            public int hairMaterials;
            public int extraAnimators;
            public float alignmentErrorMeters;
            public float attachmentErrorMeters;
            public int nonNeutralAnimationSamples;
            public string[] sampledClips;
            public string[] mismatchedLegacyShapeBindings;
            public int renderedImages;
            public string limitations = "Rigid hair; not cloth. No PICO device performance test.";
        }

        struct Pose
        {
            public Transform transform;
            public Vector3 position, scale;
            public Quaternion rotation;
        }

        static Transform Find(GameObject root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
        }

        static void Check(bool pass, string message)
        {
            if (!pass) throw new InvalidOperationException(message);
        }

        static string Relative(Transform t, Transform root)
        {
            return AnimationUtility.CalculateTransformPath(t, root);
        }

        static Pose[] Capture(GameObject root)
        {
            return root.GetComponentsInChildren<Transform>(true).Select(t => new Pose {
                transform = t, position = t.localPosition, rotation = t.localRotation,
                scale = t.localScale }).ToArray();
        }

        static void Restore(Pose[] poses)
        {
            foreach (var p in poses)
            {
                p.transform.localPosition = p.position;
                p.transform.localRotation = p.rotation;
                p.transform.localScale = p.scale;
            }
        }

        static Material GetURPMaterial(Material source)
        {
            string id = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            if (Materials.TryGetValue(id, out var cached)) return cached;
            var material = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            material.name = source.name + "_HairLabURP";
            if (source.HasProperty("_MainTex"))
                material.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
            material.SetColor("_BaseColor", source.HasProperty("_Color")
                ? source.GetColor("_Color") : Color.white);
            material.SetColor("_SpecColor", Color.black);
            material.SetFloat("_Smoothness", 0);
            string path = Custom + "/Hair/Materials/" + material.name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(material, existing);
                Object.DestroyImmediate(material);
                material = existing;
            }
            else AssetDatabase.CreateAsset(material, path);
            Materials[id] = material;
            return material;
        }

        static void PrepareCharacter(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour && behaviour.GetType().Name == "SapphiArtChan_AnimManager")
                    behaviour.enabled = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(GetURPMaterial).ToArray();
            foreach (string name in new[] { "tongue", "TopTeeth" })
                Find(root, name).GetComponent<Renderer>().enabled = false;
            root.GetComponent<Animator>().enabled = false;
        }

        static Quaternion Basis(Vector3 head, Vector3 end, Vector3 left, Vector3 right)
        {
            var up = (end - head).normalized;
            var lateral = (left - right).normalized;
            return Quaternion.LookRotation(Vector3.Cross(lateral, up).normalized, up);
        }

        static float AttachHair(GameObject character, GameObject hair)
        {
            var source = Markers.Select(n => Find(hair, "Align_" + n).position).ToArray();
            var target = Markers.Select(n => Find(character, n == "Head" ? "Head 1" : n).position).ToArray();
            float scale = Vector3.Distance(target[2], target[3]) / Vector3.Distance(source[2], source[3]);
            var rotation = Basis(target[0], target[1], target[2], target[3])
                         * Quaternion.Inverse(Basis(source[0], source[1], source[2], source[3]));
            hair.transform.SetPositionAndRotation(target[0] - rotation * source[0] * scale, rotation);
            hair.transform.localScale = Vector3.one * scale;
            float error = 0;
            for (int i = 0; i < Markers.Length; i++)
                error = Mathf.Max(error, Vector3.Distance(Find(hair, "Align_" + Markers[i]).position, target[i]));
            Check(error < .001f, "FBX landmark alignment error: " + error);
            hair.transform.SetParent(Find(character, "Head 1"), true);
            PrefabUtility.RecordPrefabInstancePropertyModifications(hair.transform);
            return error;
        }

        static Camera CreateCamera()
        {
            var camera = new GameObject("HairLab Camera", typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.orthographic = true;
            camera.orthographicSize = .26f;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 30;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.42f, .42f, .44f);
            camera.allowHDR = false;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            return camera;
        }

        static void Render(Camera camera, string name, Vector3 position, Vector3 target, float size = .26f)
        {
            camera.transform.position = position;
            camera.transform.LookAt(target);
            camera.orthographicSize = size;
            var rt = new RenderTexture(800, 800, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            RenderPipeline.SubmitRenderRequest(camera,
                new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(800, 800, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 800, 800), 0, 0);
            image.Apply();
            var pixels = image.GetPixels32();
            int variation = pixels.Count(p => Math.Abs(p.r - pixels[0].r) > 20
                || Math.Abs(p.g - pixels[0].g) > 20 || Math.Abs(p.b - pixels[0].b) > 20);
            int errorPink = pixels.Count(p => p.r > 245 && p.b > 245 && p.g < 10);
            File.WriteAllBytes(Path.Combine(Previews, "Unity_" + name + ".png"), image.EncodeToPNG());
            RenderTexture.active = previous;
            Object.DestroyImmediate(image);
            rt.Release();
            Object.DestroyImmediate(rt);
            Check(variation > pixels.Length / 30, "Blank or incorrectly framed render: " + name);
            Check(errorPink < pixels.Length / 100, "Possible missing shader: " + name);
        }

        [MenuItem("MateSoul/Hair Lab/Build And Validate Bob Test")]
        public static void Build()
        {
            try { Run(); }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        static void Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Directory.CreateDirectory(Reports);
            Directory.CreateDirectory(Previews);
            foreach (string sub in new[] { "Hair/Materials", "Prefabs", "Scenes", "Settings" })
                Directory.CreateDirectory(Custom + "/" + sub);
            AssetDatabase.Refresh();
            Materials.Clear();
            var importer = (ModelImporter)AssetImporter.GetAtPath(HairPath);
            importer.importAnimation = false;
            importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();

            // Only the disposable validation project needs a pipeline installed.
            bool isolated = Path.GetFileName(Directory.GetParent(Application.dataPath).FullName) == ".hairlab-unity";
            if (isolated)
            {
                string pipelinePath = Custom + "/Settings/HairLabURP.asset";
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
                if (!pipeline)
                {
                    var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(renderer, Custom + "/Settings/HairLabRenderer.asset");
                    pipeline = UniversalRenderPipelineAsset.Create(renderer);
                    pipeline.msaaSampleCount = 1;
                    pipeline.supportsHDR = false;
                    AssetDatabase.CreateAsset(pipeline, pipelinePath);
                }
                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = pipeline;
                PlayerSettings.colorSpace = ColorSpace.Linear;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Vendor + "/OBJ/SapphiArtchan.prefab");
            var character = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
            character.name = "SapphiArt_LengyueBob";
            var originalPose = Capture(character);
            var skins = character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var originalMeshes = skins.Select(s => s.sharedMesh).ToArray();
            var originalBones = skins.Select(s => s.bones.ToArray()).ToArray();
            int shapes = originalMeshes.Sum(m => m.blendShapeCount);
            PrepareCharacter(character);
            foreach (string name in OldHair)
            {
                var renderer = Find(character, name).GetComponent<Renderer>();
                renderer.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            var hair = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(HairPath));
            hair.name = "Lengyue_Bob_Attachment";
            float alignmentError = AttachHair(character, hair);
            var hairRenderer = hair.GetComponentInChildren<MeshRenderer>();
            var hairMesh = hairRenderer.GetComponent<MeshFilter>().sharedMesh;
            var hairMaterial = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            hairMaterial.name = "Lengyue_Bob_Purple";
            hairMaterial.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(
                Custom + "/Hair/Textures/Lengyue_Bob_Albedo.png"));
            hairMaterial.SetColor("_BaseColor", Color.white);
            hairMaterial.SetColor("_SpecColor", Color.black);
            string hairMatPath = Custom + "/Hair/Materials/Lengyue_Bob_Purple.mat";
            var existingHairMaterial = AssetDatabase.LoadAssetAtPath<Material>(hairMatPath);
            if (existingHairMaterial)
            {
                EditorUtility.CopySerialized(hairMaterial, existingHairMaterial);
                Object.DestroyImmediate(hairMaterial);
                hairMaterial = existingHairMaterial;
            }
            else AssetDatabase.CreateAsset(hairMaterial, hairMatPath);
            hairRenderer.sharedMaterial = hairMaterial;
            PrefabUtility.RecordPrefabInstancePropertyModifications(hairRenderer);
            var report = new Report {
                unityVersion = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceName,
                originalMeshesUnchanged = skins.Select((s, i) => s.sharedMesh == originalMeshes[i]).All(x => x),
                originalBonesUnchanged = skins.Select((s, i) => s.bones.SequenceEqual(originalBones[i])).All(x => x),
                originalTransformsUnchanged = originalPose.All(p =>
                    p.position == p.transform.localPosition && p.rotation == p.transform.localRotation
                    && p.scale == p.transform.localScale),
                originalShapeCount = shapes, variantShapeCount = skins.Sum(s => s.sharedMesh.blendShapeCount),
                alignmentErrorMeters = alignmentError, hairTriangles = hairMesh.triangles.Length / 3,
                hairMaterials = hairRenderer.sharedMaterials.Length,
                extraAnimators = character.GetComponentsInChildren<Animator>(true).Length - 1
            };
            Check(report.originalMeshesUnchanged && report.originalBonesUnchanged
                && report.originalTransformsUnchanged, "Original character geometry or rig changed");
            Check(shapes == 22 && report.variantShapeCount == 22, "Expected 22 original BlendShapes");
            Check(report.extraAnimators == 0 && report.hairMaterials == 1, "Hair must not add a rig");
            Check(hairMesh.vertices.All(v => !float.IsNaN(v.sqrMagnitude) && !float.IsInfinity(v.sqrMagnitude)),
                "Non-finite hair vertex");
            foreach (var skin in skins)
                for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++)
                {
                    skin.SetBlendShapeWeight(i, 100);
                    Check(Mathf.Abs(skin.GetBlendShapeWeight(i) - 100) < .01f, "Shape write failed");
                    skin.SetBlendShapeWeight(i, 0);
                }

            var camera = CreateCamera();
            var light = new GameObject("HairLab Key", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(35, -25, 0);
            light.intensity = 1.1f;
            light.shadows = LightShadows.None;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.7f, .7f, .7f);
            var target = new Vector3(0, 1.58f, 0);
            Render(camera, "Front", new Vector3(0, 1.58f, 3), target);
            Render(camera, "Side", new Vector3(3, 1.58f, 0), target);
            Render(camera, "Back", new Vector3(0, 1.58f, -3), target);
            Render(camera, "ThreeQuarter", new Vector3(1.8f, 1.7f, 3), target);
            report.renderedImages = 4;

            var poses = Capture(character);
            var clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { Vendor + "/Animation" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<AnimationClip>).ToArray();
            var mismatches = new HashSet<string>();
            foreach (var clip in clips)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!binding.propertyName.StartsWith("blendShape.")) continue;
                    var transform = character.transform.Find(binding.path);
                    var skin = transform ? transform.GetComponent<SkinnedMeshRenderer>() : null;
                    if (!skin || skin.sharedMesh.GetBlendShapeIndex(binding.propertyName.Substring(11)) < 0)
                        mismatches.Add(clip.name + ": " + binding.propertyName);
                }
                for (int frame = 0; frame < 5; frame++)
                {
                    Restore(poses);
                    clip.SampleAnimation(character, clip.length * frame / 4);
                    if (poses.Any(p => Quaternion.Angle(p.rotation, p.transform.localRotation) > 1f
                        || Vector3.Distance(p.position, p.transform.localPosition) > .001f))
                        report.nonNeutralAnimationSamples++;
                    float error = Vector3.Distance(Find(hair, "Align_Head").position, Find(character, "Head 1").position);
                    report.attachmentErrorMeters = Mathf.Max(report.attachmentErrorMeters, error);
                    Check(error < .001f, "Hair detached during animation " + clip.name);
                }
            }
            Check(report.nonNeutralAnimationSamples > 0, "Animation sampling produced no movement");
            Restore(poses);
            report.sampledClips = clips.Select(c => c.name).ToArray();
            report.mismatchedLegacyShapeBindings = mismatches.OrderBy(s => s).ToArray();
            var head = Find(character, "Head 1");
            foreach (float angle in new[] { -40f, 40f })
            {
                Restore(poses);
                head.Rotate(character.transform.up, angle, Space.World);
                Render(camera, "HeadYaw" + angle, new Vector3(0, 1.58f, 3), target);
                report.renderedImages++;
            }
            Restore(poses);
            foreach (var skin in skins)
                for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++) skin.SetBlendShapeWeight(i, 0);
            var face = Find(character, "face").GetComponent<SkinnedMeshRenderer>();
            foreach (string suffix in new[] { "face_eyeHappy_L", "face_eyeHappy_R", "face_smile_BS" })
                for (int i = 0; i < face.sharedMesh.blendShapeCount; i++)
                    if (face.sharedMesh.GetBlendShapeName(i).EndsWith(suffix))
                        face.SetBlendShapeWeight(i, 65);
            Render(camera, "Smile", new Vector3(0, 1.58f, 3), target);
            report.renderedImages++;
            foreach (var skin in skins)
                for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++) skin.SetBlendShapeWeight(i, 0);
            character.GetComponent<Animator>().enabled = true;
            var saved = PrefabUtility.SaveAsPrefabAsset(character, PrefabPath);
            report.prefabIsVariant = PrefabUtility.GetPrefabAssetType(saved) == PrefabAssetType.Variant;
            Check(report.prefabIsVariant, "Expected a non-destructive Prefab Variant");

            // Two-character scene makes the original/modified comparison explicit.
            character.transform.position = new Vector3(.42f, 0, 0);
            var baseline = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
            baseline.name = "Original_Baseline";
            PrepareCharacter(baseline);
            baseline.GetComponent<Animator>().enabled = true;
            baseline.transform.position = new Vector3(-.42f, 0, 0);
            Render(camera, "Comparison", new Vector3(0, 1.56f, 3), new Vector3(0, 1.56f, 0), .58f);
            report.renderedImages++;
            camera.transform.position = new Vector3(0, 1.2f, 4);
            camera.transform.LookAt(new Vector3(0, .90f, 0));
            camera.orthographicSize = 1.05f;
            EditorSceneManager.SaveScene(scene, Custom + "/Scenes/SapphiArt_HairLab.unity");
            AssetDatabase.SaveAssets();
            File.WriteAllText(Path.Combine(Reports, "unity_validation.json"), JsonUtility.ToJson(report, true));
            Debug.Log("HAIRLAB_VALIDATION_PASS " + JsonUtility.ToJson(report));
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
