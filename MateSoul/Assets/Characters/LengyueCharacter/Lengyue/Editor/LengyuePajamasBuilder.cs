using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MateSoul.Validation;
using MateSoul.Wardrobe;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public static class LengyuePajamasBuilder
    {
        const string Root = LengyueLayeredDressBuilder.Root;
        const string Folder = Root + "/Runtime/Wardrobe/PinkPajamas";
        const string Model = Folder + "/lengyue_model_pajamas.fbx";
        const string Definition = Folder + "/lengyue_pajamas_outfit.asset";
        const string ScenePath = Root + "/Validation/lengyue_validation_pajamas.unity";
        static string Repository => Path.GetFullPath(Application.dataPath + "/../..");
        static string Output => Repository + "/ArtSource/Lengyue/Wardrobe/PinkPajamas/v001";

        [Serializable] sealed class BuildReport
        {
            public string outfitId = "pink-pajamas";
            public string definition = Definition;
            public int renderers, vertices, triangles;
            public float maxRebindErrorM;
            public bool canonicalRestPreserved, catalogRegistered, passed;
            public string[] parts;
        }

        [InitializeOnLoadMethod] static void Register()
        {
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged -= OnState;
            EditorApplication.playModeStateChanged += OnState;
        }

        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (string command in new[] { "build", "play" })
            {
                string path = Repository + "/.dbg/pajamas-" + command;
                if (!File.Exists(path)) continue;
                File.Delete(path);
                try
                {
                    if (command == "build") Build();
                    else Play();
                }
                catch (Exception error)
                {
                    Directory.CreateDirectory(Output + "/reports");
                    File.WriteAllText(Output + "/reports/unity_error.txt", error.ToString());
                    Debug.LogException(error);
                }
            }
        }

        static void OnState(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            for (int i = SceneManager.sceneCount-1; i >= 0; i--)
                if (SceneManager.GetSceneAt(i).path == ScenePath && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(SceneManager.GetSceneAt(i), true);
        }

        [MenuItem("MateSoul/Lengyue/Wardrobe/Build Pink Pajamas")]
        public static void Build()
        {
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(Output + "/reports");
            File.Delete(Output + "/reports/unity_error.txt");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            Require(importer, "Missing sleepwear FBX");
            importer.isReadable = true;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.SaveAndReimport();
            var texImporter = (TextureImporter)AssetImporter.GetAtPath(Folder + "/lengyue_pajamas_tex_gingham.png");
            texImporter.wrapMode = TextureWrapMode.Repeat;
            texImporter.filterMode = FilterMode.Trilinear;
            texImporter.anisoLevel = 4;
            texImporter.SaveAndReimport();

            GameObject character = PrefabUtility.LoadPrefabContents(LengyueLayeredDressBuilder.Prefab);
            GameObject imported = null;
            try
            {
                var snapshots = character.GetComponentsInChildren<Transform>(true)
                    .Select(t => (t, t.localPosition, t.localRotation, t.localScale)).ToArray();
                var initial = character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Single(r => r.name == "lengyue_garment_main");
                var container = initial.transform.parent;
                var bones = character.GetComponentsInChildren<Transform>(true)
                    .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                Transform Resolve(string name) => bones[name == "lengyue_head" ? "lengyue_head_01" : name];
                var skinMaterial = character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Single(r => r.name == "lengyue_garment_skin_fill").sharedMaterial;
                imported = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
                SceneManager.MoveGameObjectToScene(imported, character.scene);
                imported.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var definitions = new List<LengyueOutfitPartDefinition>();
                var report = new BuildReport();
                var materials = new Dictionary<string, Material>();
                foreach (var renderer in imported.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Vector3[] before = LengyueLayeredDressBuilder.BakeWorld(renderer);
                    Mesh source = renderer.sharedMesh;
                    // Rebuild standard streams before changing bindposes: imported FBX
                    // packed skinning buffers can otherwise fail realtime GPU rendering.
                    var mesh = new Mesh {
                        name = PartName(renderer.name), indexFormat = source.indexFormat,
                        vertices = source.vertices, normals = source.normals,
                        tangents = source.tangents, uv = source.uv,
                        boneWeights = source.boneWeights, subMeshCount = source.subMeshCount
                    };
                    for (int sub = 0; sub < source.subMeshCount; sub++)
                        mesh.SetTriangles(source.GetTriangles(sub), sub);
                    mesh.RecalculateBounds();
                    Transform[] rebound = renderer.bones.Select(b => Resolve(b.name)).ToArray();
                    mesh.bindposes = rebound.Select(b =>
                        b.worldToLocalMatrix * renderer.transform.localToWorldMatrix).ToArray();
                    renderer.bones = rebound;
                    renderer.rootBone = Resolve(renderer.rootBone.name);
                    renderer.sharedMesh = mesh;
                    renderer.quality = SkinQuality.Bone4;
                    renderer.updateWhenOffscreen = true;
                    renderer.localBounds = mesh.bounds;
                    Vector3[] after = LengyueLayeredDressBuilder.BakeWorld(renderer);
                    float error = before.Zip(after, Vector3.Distance).Max();
                    report.maxRebindErrorM = Mathf.Max(report.maxRebindErrorM, error);
                    Require(error < .00002f, "Pajamas rebind moved vertices: " + renderer.name + " " + error);
                    string meshPath = Folder + "/" + PartName(renderer.name) + ".asset";
                    Save(mesh, meshPath);
                    renderer.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    bool coverage = renderer.name.Contains("Coverage");
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(sourceMaterial => {
                        if (coverage) return skinMaterial;
                        string name = sourceMaterial.name;
                        if (!materials.TryGetValue(name, out Material mat))
                        {
                            string fileName = "lengyue_pajamas_mat_" +
                                name.ToLowerInvariant().Replace(" ", "_");
                            mat = new Material(initial.sharedMaterial) { name = fileName };
                            mat.SetTexture("_BaseMap", name.Contains("Gingham")
                                ? AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/lengyue_pajamas_tex_gingham.png") : null);
                            mat.SetColor("_BaseColor",
                                name.Contains("Rose") ? new Color(.79f,.51f,.59f) :
                                name.Contains("Ivory") ? new Color(.96f,.88f,.87f) :
                                name.Contains("Button") ? new Color(.96f,.80f,.82f) : Color.white);
                            mat.SetColor("_ShadowColor", new Color(.81f,.65f,.70f));
                            mat.SetFloat("_DiffuseWrap", .45f);
                            mat.SetFloat("_ShadowSoftness", .18f);
                            mat.SetFloat("_UnlitBlend", .15f);
                            mat.SetFloat("_FabricAlbedoStrength", .025f);
                            mat.SetFloat("_FabricNormalStrength", .045f);
                            Save(mat, Folder + "/" + fileName + ".mat");
                            mat = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/" + fileName + ".mat");
                            materials.Add(name, mat);
                        }
                        return mat;
                    }).ToArray();
                    renderer.transform.SetParent(container, true);
                    definitions.Add(Part(renderer,
                        coverage ? LengyueOutfitPartRole.SkinCoverage :
                        renderer.name.Contains("Cap") ? LengyueOutfitPartRole.Accessory :
                        LengyueOutfitPartRole.Garment, PartName(renderer.name)));
                    report.vertices += renderer.sharedMesh.vertexCount;
                    report.triangles += renderer.sharedMesh.triangles.Length/3;
                    // Definition owns renderer data; imported objects never remain in the prefab.
                    Object.DestroyImmediate(renderer.gameObject);
                }
                var neck = character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Single(r => r.name == "lengyue_garment_neck_fill");
                definitions.Add(Part(neck, LengyueOutfitPartRole.NeckExtension, "lengyue_pajamas_neckfill_mesh"));
                var generated = ScriptableObject.CreateInstance<LengyueOutfitDefinition>();
                generated.Configure("pink-pajamas", "粉格睡衣与睡帽", false, false, definitions.ToArray());
                Require(generated.TryValidate(out string validationError), validationError);
                Save(generated, Definition);
                var definition = AssetDatabase.LoadAssetAtPath<LengyueOutfitDefinition>(Definition);
                var wardrobe = character.GetComponent<LengyueWardrobeController>();
                wardrobe.SetCatalog(wardrobe.Outfits.Where(o => o && o.OutfitId != "pink-pajamas")
                    .Concat(new[] { definition }).ToArray());
                Require(wardrobe.TryValidateConfiguration(out validationError), validationError);
                report.canonicalRestPreserved = snapshots.All(p => p.t &&
                    p.t.localPosition == p.localPosition && p.t.localRotation == p.localRotation &&
                    p.t.localScale == p.localScale);
                Require(report.canonicalRestPreserved, "Canonical rig was changed");
                report.renderers = definitions.Count;
                report.parts = definitions.Select(p => p.PartName).ToArray();
                report.catalogRegistered = wardrobe.Outfits.Contains(definition);
                report.passed = report.catalogRegistered && report.canonicalRestPreserved &&
                    report.maxRebindErrorM < .00002f;
                PrefabUtility.SaveAsPrefabAsset(character, LengyueLayeredDressBuilder.Prefab);
                AssetDatabase.SaveAssets();
                File.WriteAllText(Output + "/reports/unity_build.json", JsonUtility.ToJson(report, true));
                Debug.Log("PINK_PAJAMAS_BUILD_PASS\n" + JsonUtility.ToJson(report, true));
            }
            finally
            {
                if (imported) Object.DestroyImmediate(imported);
                PrefabUtility.UnloadPrefabContents(character);
            }
        }

        static string PartName(string rendererName)
        {
            int separator = rendererName.IndexOf('_');
            string tail = separator < 0
                ? rendererName
                : rendererName.Substring(separator + 1);
            if (tail == "Garments") return "lengyue_pajamas_garments_mesh";
            if (tail == "HandsCoverage") return "lengyue_pajamas_hands_mesh";
            if (tail == "SleepCap") return "lengyue_pajamas_sleepcap_mesh";
            return "lengyue_pajamas_" + tail.ToLowerInvariant() + "_mesh";
        }

        static LengyueOutfitPartDefinition Part(SkinnedMeshRenderer r,
            LengyueOutfitPartRole role, string name = null)
        {
            return new LengyueOutfitPartDefinition(name ?? r.name, role, r.sharedMesh,
                r.sharedMaterials, r.rootBone.name, r.bones.Select(b => b.name).ToArray(),
                r.transform.localPosition, r.transform.localRotation, r.transform.localScale,
                r.localBounds, r.quality, true, r.shadowCastingMode, r.receiveShadows);
        }

        static void Save(Object asset, string path)
        {
            Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            // Replacing a Mesh through CopySerialized retains Unity's old GPU
            // skinning buffer layout after vertex/weight-count changes.
            if (existing && asset is Mesh)
            {
                Require(AssetDatabase.DeleteAsset(path), "Cannot replace generated mesh " + path);
                existing = null;
            }
            if (existing)
            {
                EditorUtility.CopySerialized(asset, existing);
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(asset);
            }
            else AssetDatabase.CreateAsset(asset, path);
        }

        [MenuItem("MateSoul/Lengyue/Wardrobe/Test Pink Pajamas In Play Mode")]
        public static void Play()
        {
            Directory.CreateDirectory(Output + "/renders");
            File.Delete(Output + "/reports/unity_error.txt");
            var current = SceneManager.GetActiveScene();
            var currentRoots = current.GetRootGameObjects();
            bool previousCapture = !current.isDirty && currentRoots.Length == 3 &&
                currentRoots.Any(r => r.GetComponent<LengyuePajamasProbe>()) &&
                currentRoots.All(r => r.name == "lengyue_character_root" ||
                    r.name == "PajamasCamera" || r.name == "PajamasKey");
            bool emptyUntitled = SceneManager.sceneCount == 1 &&
                string.IsNullOrEmpty(current.path) && (current.rootCount == 0 || previousCapture);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                emptyUntitled ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var character = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(LengyueLayeredDressBuilder.Prefab), scene);
            var camera = new GameObject("PajamasCamera", typeof(Camera)).GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.34f,.35f,.38f);
            camera.orthographic = true;
            camera.orthographicSize = 1.02f;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 20;
            camera.allowHDR = false;
            camera.transform.position = new Vector3(0,.94f,4);
            camera.transform.LookAt(new Vector3(0,.94f,0));
            var light = new GameObject("PajamasKey", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.05f;
            light.color = new Color(1,.94f,.93f);
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(38,148,0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.27f,.28f,.34f);
            RenderSettings.skybox = null;
            var probe = camera.gameObject.AddComponent<LengyuePajamasProbe>();
            probe.character = character;
            probe.outputDirectory = Output;
            probe.clips = new[] { "Idle", "Walk", "Run", "Yummy" }.Select(name =>
                AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/Runtime/Animations/" +
                    (name == "Yummy" ? "Yummy/" : "") + "lengyue_clip_" + name.ToLowerInvariant() + "_bodyonly.anim")).ToArray();
            Require(probe.clips.All(c => c), "A validation clip is missing");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorApplication.isPlaying = true;
        }

        static void Require(bool condition, string error)
        {
            if (!condition) throw new InvalidOperationException(error);
        }
    }
}
