using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MateSoul.Animation;
using MateSoul.Cloth;
using MateSoul.Hair;
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
    public static class LengyueLayeredDressBuilder
    {
        public const string Root = "Assets/Characters/SapphiArtCustom/Lengyue";
        const string Runtime = Root + "/Runtime";
        public const string Prefab = Runtime + "/Prefabs/Lengyue_GameCharacter.prefab";
        const string Controller = Runtime + "/Animations/Lengyue_Locomotion.controller";
        const string Model =
            Runtime + "/Models/Lengyue_LayeredDress.fbx";
        const string CanonicalNeckMesh = Runtime +
            "/Meshes/Lengyue_LavenderCasual_NeckExtension.asset";
        const string FaceMaterial = Runtime +
            "/Materials/Lengyue_Toon_Face.mat";
        const string Atlas = Runtime + "/Textures/Lengyue_Clothing_Atlas.png";
        const string FabricDetail =
            Runtime + "/Textures/Lengyue_Fabric_Weave.png";
        const string ScenePath = Root + "/Validation/Lengyue_LayeredDress.unity";
        static string Repository => Path.GetFullPath(Application.dataPath + "/../..");
        public static string Evidence =>
            Repository + "/ArtSource/SapphiArt/Wardrobe/v004/evidence";
        public static string Reports =>
            Repository + "/ArtSource/SapphiArt/Wardrobe/v004/reports";
        static double nextPoll;

        [Serializable] sealed class BuildReport
        {
            public string prefab;
            public int bodyBones, garmentVertices, garmentTriangles,
                sleeveDrivenVertices;
            public bool originalBodyMeshesPreserved, restPosePreserved, broochRemoved,
                sleeveRuffleAccentTextureAssigned, noCollarVertices,
                canonicalNeckRestored, sleeveTargetRebound,
                legacyDependenciesRemoved, wardrobeConfigured;
            public int productionPrefabCount;
            public float maxRebindErrorM;
            public string[] vendorDependencies, legacyDependencies;
            public bool passed;
        }
        [Serializable] sealed class SeamReport
        {
            public int boundaryPairs, sampledFrames;
            public float maxGapM;
            public bool passed;
        }

        [InitializeOnLoadMethod] static void Register()
        {
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged -= OnPlayState;
            EditorApplication.playModeStateChanged += OnPlayState;
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll ||
                EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            foreach (string command in new[] { "build", "play" })
            {
                string request = Repository + "/.dbg/layered-dress-" + command;
                if (!File.Exists(request)) continue;
                File.Delete(request);
                try
                {
                    if (command == "build") Build();
                    else
                    {
                        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                        SceneManager.SetActiveScene(scene);
                        EditorApplication.isPlaying = true;
                    }
                }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        static void OnPlayState(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            for (int i = SceneManager.sceneCount-1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.path == ScenePath && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("MateSoul/Lengyue/Build Production Character")]
        public static void Build()
        {
            foreach (string dir in new[] { Runtime+"/Prefabs", Runtime+"/Meshes",
                Runtime+"/Materials", Root+"/Validation", Evidence, Reports })
                Directory.CreateDirectory(dir);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Controller);
            Require(controller, "Missing canonical Lengyue locomotion controller");
            controller.name = "Lengyue_Locomotion";
            EditorUtility.SetDirty(controller);
            var importer =
                (ModelImporter)AssetImporter.GetAtPath(Model);
            Require(importer, "Missing canonical v004 model: " + Model);
            importer.isReadable = true;
            importer.importAnimation = false;
            importer.animationType =
                ModelImporterAnimationType.Generic;
            importer.materialImportMode =
                ModelImporterMaterialImportMode.None;
            importer.meshCompression =
                ModelImporterMeshCompression.Off;
            importer.SaveAndReimport();
            Scene previous = SceneManager.GetActiveScene();
            bool restorePrevious = previous.IsValid() && previous.isLoaded &&
                !string.IsNullOrEmpty(previous.path);
            Scene work = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                restorePrevious ? NewSceneMode.Additive : NewSceneMode.Single);
            SceneManager.SetActiveScene(work);
            var otherRoots = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i) != work)
                    foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects())
                        if (root.activeSelf) { otherRoots.Add(root); root.SetActive(false); }
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(
                    Prefab);
                Require(source, "Missing canonical Lengyue production prefab");
                var character = (GameObject)PrefabUtility.InstantiatePrefab(source, work);
                PrefabUtility.UnpackPrefabInstance(character, PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                character.name = "Lengyue_GameCharacter";
                var rest = character.GetComponentsInChildren<Transform>(true)
                    .ToDictionary(t => t, t => (t.localPosition, t.localRotation, t.localScale));
                var existing = character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var original = existing.Single(r => r.name == "LavenderCasual_Garments");
                Transform garmentParent = original.transform.parent;
                var bones = original.bones.ToDictionary(t => t.name);
                var transforms = character.GetComponentsInChildren<Transform>(true)
                    .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                Transform Resolve(string name) => transforms[name == "Head" ? "Head 1" : name];
                var imported = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(Model), work);
                PrefabUtility.UnpackPrefabInstance(imported, PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                var garment = imported.GetComponentInChildren<SkinnedMeshRenderer>();
                Vector3[] before = BakeWorld(garment);
                // Rebuild canonical vertex streams. Copying the FBX mesh's packed
                // skinning buffers while replacing bindposes can render in BakeMesh
                // yet fail the realtime renderer's vertex-stride validation.
                Mesh sourceMesh = garment.sharedMesh;
                Mesh mesh = new Mesh {
                    name = "Lengyue_LayeredDress_Garments",
                    indexFormat = sourceMesh.indexFormat,
                    vertices = sourceMesh.vertices,
                    normals = sourceMesh.normals,
                    tangents = sourceMesh.tangents,
                    uv = sourceMesh.uv,
                    uv2 = sourceMesh.uv2,
                    boneWeights = sourceMesh.boneWeights,
                    subMeshCount = sourceMesh.subMeshCount
                };
                for (int sub = 0; sub < sourceMesh.subMeshCount; sub++)
                    mesh.SetTriangles(sourceMesh.GetTriangles(sub), sub);
                mesh.RecalculateBounds();
                bool noCollarVertices = mesh.uv2.All(uv =>
                    Mathf.RoundToInt(uv.x) != 6);
                int sleeveDrivenVertices = mesh.vertices
                    .Select((vertex, index) => (vertex, index))
                    .Count(item => {
                        int part = Mathf.RoundToInt(mesh.uv2[item.index].x);
                        return (part == 3 || part == 5) &&
                            Mathf.Abs(item.vertex.x) >= .125f;
                    });
                Transform[] rebound = garment.bones.Select(t => Resolve(t.name)).ToArray();
                mesh.bindposes = rebound.Select(b =>
                    b.worldToLocalMatrix * garment.transform.localToWorldMatrix).ToArray();
                garment.bones = rebound;
                garment.rootBone = Resolve(garment.rootBone.name);
                garment.sharedMesh = mesh;
                garment.quality = SkinQuality.Bone4;
                garment.updateWhenOffscreen = true;
                var material = new Material(original.sharedMaterial);
                material.name = "Lengyue_LayeredDress_Toon";
                var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(Atlas);
                Require(atlas, "Missing V004 accent atlas");
                var fabricDetail =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(FabricDetail);
                Require(fabricDetail, "Missing fabric weave detail");
                material.SetTexture("_BaseMap", atlas);
                material.SetTexture("_FabricDetailMap", fabricDetail);
                material.SetFloat("_ShadowSoftness", .18f);
                material.SetFloat("_DiffuseWrap", .42f);
                material.SetFloat("_UnlitBlend", .12f);
                material.SetFloat("_SpecularIntensity", .065f);
                material.SetFloat("_SpecularSoftness", .08f);
                material.SetFloat("_SpecularThreshold", .82f);
                material.SetFloat("_FabricDetailScale", 8f);
                material.SetFloat("_FabricAlbedoStrength", .06f);
                material.SetFloat("_FabricNormalStrength", .1f);
                material.SetFloat("_FabricSheenIntensity", .045f);
                material.SetFloat("_FabricSheenPower", 3.5f);
                material.SetColor(
                    "_FabricSheenColor", new Color(.93f, .88f, 1f));
                SaveAsset(material, Runtime+"/Materials/Lengyue_LayeredDress_Toon.mat");
                garment.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                    Runtime+"/Materials/Lengyue_LayeredDress_Toon.mat");
                Vector3[] after = BakeWorld(garment);
                float error = before.Zip(after, Vector3.Distance).Max();
                Require(error < 2e-5f, "Clothing rebind changed geometry: "+error);
                string meshPath =
                    Runtime+"/Meshes/Lengyue_LayeredDress_Garments.asset";
                if (AssetDatabase.LoadMainAssetAtPath(meshPath))
                    Require(AssetDatabase.DeleteAsset(meshPath),
                        "Could not replace the generated garment mesh");
                AssetDatabase.CreateAsset(mesh, meshPath);
                AssetDatabase.ImportAsset(
                    meshPath, ImportAssetOptions.ForceSynchronousImport);
                garment.sharedMesh =
                    AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                garment.localBounds = garment.sharedMesh.bounds;
                garment.transform.SetParent(garmentParent, true);
                garment.name = "LavenderCasual_Garments";
                Object.DestroyImmediate(original.gameObject);
                Object.DestroyImmediate(imported);
                var sleeveCloth = character.GetComponent<LengyueSleeveCloth>();
                Require(sleeveCloth, "Missing v10 sleeve cloth component");
                sleeveCloth.target = garment;
                EditorUtility.SetDirty(sleeveCloth);
                var neckRenderer = character
                    .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Single(r => r.name ==
                        "LavenderCasual_NeckExtension");
                var canonicalNeck =
                    AssetDatabase.LoadAssetAtPath<Mesh>(
                        CanonicalNeckMesh);
                var faceMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        FaceMaterial);
                Require(canonicalNeck &&
                    canonicalNeck.vertexCount == 70,
                    "Missing canonical pre-collar neck mesh");
                Require(faceMaterial,
                    "Missing canonical face material");
                neckRenderer.sharedMesh = canonicalNeck;
                neckRenderer.sharedMaterial = faceMaterial;
                neckRenderer.localBounds = canonicalNeck.bounds;
                var skinCoverageRenderer = character
                    .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Single(r => r.name ==
                        "LavenderCasual_SkinCoverage");
                var skirtGuard =
                    character.GetComponent<SkirtPoseGuard>();
                Require(skirtGuard,
                    "Missing LavenderCasual skirt pose guard");
                var wardrobe =
                    character.GetComponent<LengyueWardrobeController>();
                if (!wardrobe)
                    wardrobe =
                        character.AddComponent<LengyueWardrobeController>();
                wardrobe.ConfigureInitialOutfit(
                    transforms["BindJoints"],
                    garmentParent,
                    "lavender-casual",
                    "Lavender Casual",
                    new[] {
                        garment.gameObject,
                        skinCoverageRenderer.gameObject,
                        neckRenderer.gameObject
                    },
                    garment,
                    true,
                    true,
                    sleeveCloth,
                    skirtGuard);
                bool wardrobeConfigured =
                    wardrobe.TryValidateConfiguration(
                        out string wardrobeError);
                Require(wardrobeConfigured, wardrobeError);
                EditorUtility.SetDirty(wardrobe);
                var brooch = character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .FirstOrDefault(r => r.name == "Lengyue_StarBrooch");
                if (brooch) Object.DestroyImmediate(brooch.gameObject);
                var remaining = character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                bool bodyPreserved = existing.Where(r => r &&
                    r.name != "LavenderCasual_Garments" && r.name != "Lengyue_StarBrooch")
                    .All(r => remaining.Contains(r));
                bool restPreserved = rest.Where(p => p.Key).All(p =>
                    p.Key.localPosition == p.Value.Item1 &&
                    p.Key.localRotation == p.Value.Item2 && p.Key.localScale == p.Value.Item3);
                PrefabUtility.SaveAsPrefabAsset(character, Prefab);
                AssetDatabase.SaveAssets();
                string[] vendor = AssetDatabase.GetDependencies(Prefab, true)
                    .Where(p => p.StartsWith("Assets/SapphiArt/")).ToArray();
                string[] legacy = AssetDatabase.GetDependencies(Prefab, true)
                    .Where(p => p.StartsWith(Root+"/V0")).ToArray();
                int prefabCount = AssetDatabase.FindAssets("t:Prefab", new[] { Root })
                    .Select(AssetDatabase.GUIDToAssetPath).Count();
                var report = new BuildReport {
                    prefab = Prefab, bodyBones = bones.Count,
                    garmentVertices = mesh.vertexCount, garmentTriangles = mesh.triangles.Length/3,
                    sleeveDrivenVertices = sleeveDrivenVertices,
                    originalBodyMeshesPreserved = bodyPreserved, restPosePreserved = restPreserved,
                    maxRebindErrorM = error,
                    broochRemoved = !character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .Any(r => r.name == "Lengyue_StarBrooch"),
                    vendorDependencies = vendor, legacyDependencies = legacy,
                    legacyDependenciesRemoved = legacy.Length == 0,
                    productionPrefabCount = prefabCount,
                    sleeveRuffleAccentTextureAssigned =
                        garment.sharedMaterial.GetTexture("_BaseMap") == atlas,
                    noCollarVertices = noCollarVertices,
                    canonicalNeckRestored =
                        neckRenderer.sharedMesh == canonicalNeck &&
                        neckRenderer.sharedMaterial == faceMaterial,
                    sleeveTargetRebound = sleeveCloth.target == garment,
                    wardrobeConfigured = wardrobeConfigured &&
                        wardrobe.InitialOutfitId ==
                            "lavender-casual",
                    passed = bodyPreserved && restPreserved && bones.Count == 116 &&
                        vendor.Length == 0 && legacy.Length == 0 && prefabCount == 1 &&
                        garment.sharedMaterial.GetTexture("_BaseMap") == atlas &&
                        noCollarVertices && sleeveDrivenVertices == 10623 &&
                        neckRenderer.sharedMesh == canonicalNeck &&
                        neckRenderer.sharedMaterial == faceMaterial &&
                        sleeveCloth.target == garment &&
                        wardrobeConfigured &&
                        wardrobe.InitialOutfitId ==
                            "lavender-casual"
                };
                File.WriteAllText(Reports+"/unity_build.json", JsonUtility.ToJson(report, true));
                Require(report.passed, "Layered dress build validation failed");
                Camera camera = SetupCamera();
                var probe = camera.gameObject.AddComponent<LengyueLayeredDressProbe>();
                probe.character = character;
                probe.outputDirectory = Evidence;
                probe.reportDirectory = Reports;
                probe.enabled = true;
                EditorSceneManager.SaveScene(work, ScenePath);
                CaptureAndExport(character, camera);
                Debug.Log("LENGYUE_LAYERED_DRESS_BUILD_PASS\n"+JsonUtility.ToJson(report, true));
            }
            finally
            {
                foreach (var root in otherRoots) if (root) root.SetActive(true);
                if (restorePrevious && previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(work, true);
                }
                else
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        public static void BuildAndCaptureBatch()
        {
            try
            {
                Build();
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        static void SaveAsset(Object generated, string path)
        {
            Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing) { EditorUtility.CopySerialized(generated, existing); EditorUtility.SetDirty(existing); }
            else AssetDatabase.CreateAsset(generated, path);
        }

        static Camera SetupCamera()
        {
            Camera camera = new GameObject("Layered Dress Camera", typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.34f, .35f, .38f);
            camera.orthographic = true;
            camera.orthographicSize = .94f;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 20;
            camera.allowHDR = false;
            Light light = new GameObject("Layered Dress Key", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.05f;
            light.color = new Color(1f, .94f, .91f);
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(38, 148, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.27f, .28f, .34f);
            RenderSettings.skybox = null;
            SetCamera(camera, new Vector3(0, .91f, 3), new Vector3(0, .91f, 0), .97f);
            return camera;
        }

        static void CaptureAndExport(GameObject character, Camera camera)
        {
            var animator = character.GetComponent<Animator>();
            animator.enabled = false;
            var skirt = character.GetComponent<SkirtPoseGuard>();
            var head = character.GetComponent<HeadPoseGuard>();
            skirt.BindByName(); skirt.Initialize();
            head.BindByName(); head.Initialize();
            var hair = character.GetComponentInChildren<HairSpringPhysics>(true);
            hair.Initialize();
            var rest = character.GetComponentsInChildren<Transform>(true)
                .Select(t => (t, t.localPosition, t.localRotation, t.localScale)).ToArray();
            void Reset()
            {
                foreach (var p in rest)
                { p.t.localPosition = p.localPosition; p.t.localRotation = p.localRotation; p.t.localScale = p.localScale; }
            }
            var garment = character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Single(r => r.name == "LavenderCasual_Garments");
            var skin = character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Single(r => r.name == "LavenderCasual_SkinCoverage");
            var face = character.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "face");
            var neck = character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Single(r => r.name == "LavenderCasual_NeckExtension");
            Vector3[] restFace = BakeWorld(face), restNeck = BakeWorld(neck);
            var pairs = new List<(int first, int second)>();
            for (int i = 0; i < restNeck.Length; i++)
            {
                int nearest = 0;
                for (int j = 1; j < restFace.Length; j++)
                    if ((restFace[j]-restNeck[i]).sqrMagnitude <
                        (restFace[nearest]-restNeck[i]).sqrMagnitude) nearest = j;
                if (Vector3.Distance(restFace[nearest], restNeck[i]) < 2e-5f) pairs.Add((i, nearest));
            }
            var seamReport = new SeamReport { boundaryPairs = pairs.Count };
            string geometry = Reports+"/geometry";
            if (Directory.Exists(geometry)) Directory.Delete(geometry, true);
            Directory.CreateDirectory(geometry);
            WriteIndices(geometry+"/garment_indices.bin", garment.sharedMesh.triangles);
            WriteIndices(geometry+"/skin_indices.bin", skin.sharedMesh.triangles);
            WriteIndices(geometry+"/neck_indices.bin", neck.sharedMesh.triangles);
            WriteIndices(geometry+"/face_indices.bin", face.sharedMesh.triangles);
            WriteIndices(geometry+"/parts.bin", garment.sharedMesh.uv2.Select(v => Mathf.RoundToInt(v.x)).ToArray());
            WritePoints(geometry+"/rest_garment.bin", BakeWorld(garment));
            WritePoints(geometry+"/rest_skin.bin", BakeWorld(skin));
            WritePoints(geometry+"/rest_neck.bin", BakeWorld(neck));
            WritePoints(geometry+"/rest_face.bin", BakeWorld(face));
            foreach (var arm in character.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == "UpperArm_L" || t.name == "UpperArm_R"))
                arm.rotation = Quaternion.AngleAxis(-Mathf.Sign(arm.position.x)*68f, Vector3.forward)
                    * arm.rotation;
            skirt.Evaluate(); head.Evaluate();
            CaptureViews(character, camera, "reference_pose");
            WritePoints(geometry+"/reference_pose_000_garment.bin", BakeWorld(garment));
            WritePoints(geometry+"/reference_pose_000_skin.bin", BakeWorld(skin));
            WritePoints(geometry+"/reference_pose_000_neck.bin", BakeWorld(neck));
            WritePoints(geometry+"/reference_pose_000_face.bin", BakeWorld(face));
            Reset();
            foreach (string motion in new[] { "idle", "walk", "run" })
            {
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    Runtime+"/Animations/Lengyue_"+char.ToUpper(motion[0])+motion.Substring(1)+"_BodyOnly.anim");
                int frames = motion == "idle" ? 1 : Mathf.RoundToInt(clip.length*30)+1;
                for (int frame = 0; frame < frames; frame++)
                {
                    Reset();
                    clip.SampleAnimation(character, frame/30f);
                    skirt.Evaluate(); head.Evaluate();
                    Vector3[] posedFace = BakeWorld(face), posedNeck = BakeWorld(neck);
                    foreach (var pair in pairs)
                        seamReport.maxGapM = Mathf.Max(seamReport.maxGapM,
                            Vector3.Distance(posedNeck[pair.first], posedFace[pair.second]));
                    seamReport.sampledFrames++;
                    WritePoints(geometry+$"/{motion}_{frame:D3}_garment.bin", BakeWorld(garment));
                    WritePoints(geometry+$"/{motion}_{frame:D3}_skin.bin", BakeWorld(skin));
                    WritePoints(geometry+$"/{motion}_{frame:D3}_neck.bin", BakeWorld(neck));
                    WritePoints(geometry+$"/{motion}_{frame:D3}_face.bin", BakeWorld(face));
                    if ((motion == "idle" && frame == 0) || (motion == "walk" && frame == 8) ||
                        (motion == "run" && frame == 5))
                        CaptureViews(character, camera, motion);
                }
            }
            var bone = character.GetComponentsInChildren<Transform>(true)
                .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            Transform upperArmL = bone["UpperArm_L"], upperArmR = bone["UpperArm_R"];
            Transform forearmL = bone["Forearm_L"], forearmR = bone["Forearm_R"];
            Transform handL = bone["Hand_L"], handR = bone["Hand_R"];
            Transform legL = bone["Leg_L"], legR = bone["Leg_R"];
            Transform kneeL = bone["Knee_L"], kneeR = bone["Knee_R"];
            Transform footL = bone["Foot_L"], footR = bone["Foot_R"];
            float Side(Transform target) => Mathf.Sign(target.position.x);
            void Aim(Transform target, Transform child, Vector3 direction)
            {
                Vector3 current = (child.position-target.position).normalized;
                target.rotation = Quaternion.FromToRotation(
                    current, direction.normalized) * target.rotation;
            }
            void ArmsDown()
            {
                Aim(upperArmL, forearmL, new Vector3(Side(upperArmL)*.18f, -1, 0));
                Aim(upperArmR, forearmR, new Vector3(Side(upperArmR)*.18f, -1, 0));
            }
            void ArmsForward()
            {
                Aim(upperArmL, forearmL, new Vector3(Side(upperArmL)*.04f, 0, 1));
                Aim(upperArmR, forearmR, new Vector3(Side(upperArmR)*.04f, 0, 1));
            }
            void HighKnee(Transform leg, Transform knee)
            {
                Transform foot = leg == legL ? footL : footR;
                Aim(leg, knee, new Vector3(Side(leg)*.06f, .04f, 1));
                Aim(knee, foot, new Vector3(Side(leg)*.03f, -1, .08f));
            }
            void Lunge(
                Transform frontLeg, Transform frontKnee,
                Transform backLeg, Transform backKnee)
            {
                Transform frontFoot = frontLeg == legL ? footL : footR;
                Transform backFoot = backLeg == legL ? footL : footR;
                Aim(frontLeg, frontKnee,
                    new Vector3(Side(frontLeg)*.08f, -.28f, 1));
                Aim(frontKnee, frontFoot,
                    new Vector3(Side(frontLeg)*.04f, -1, .14f));
                Aim(backLeg, backKnee,
                    new Vector3(Side(backLeg)*.08f, -.72f, -.70f));
                Aim(backKnee, backFoot,
                    new Vector3(Side(backLeg)*.03f, -1, -.08f));
            }
            void Crouch(float side, bool armsForward)
            {
                Aim(legL, kneeL, new Vector3(Side(legL)*side, -.28f, .92f));
                Aim(legR, kneeR, new Vector3(Side(legR)*side, -.28f, .92f));
                Aim(kneeL, footL, new Vector3(Side(legL)*.10f, -1, -.32f));
                Aim(kneeR, footR, new Vector3(Side(legR)*.10f, -1, -.32f));
                if (armsForward) ArmsForward();
            }
            var stressPoses = new (string name, Action apply, bool capture)[] {
                ("t_pose", () => {}, false),
                ("arms_down", ArmsDown, true),
                ("arms_overhead", () => {
                    Aim(upperArmL, forearmL, new Vector3(Side(upperArmL)*.12f, 1, 0));
                    Aim(upperArmR, forearmR, new Vector3(Side(upperArmR)*.12f, 1, 0));
                }, true),
                ("arms_forward", ArmsForward, true),
                ("arms_backward", () => {
                    Aim(upperArmL, forearmL, new Vector3(Side(upperArmL)*.08f, 0, -1));
                    Aim(upperArmR, forearmR, new Vector3(Side(upperArmR)*.08f, 0, -1));
                }, false),
                ("elbows_flexed", () => {
                    ArmsDown();
                    Aim(forearmL, handL, new Vector3(Side(forearmL)*.10f, 1, .12f));
                    Aim(forearmR, handR, new Vector3(Side(forearmR)*.10f, 1, .12f));
                }, true),
                ("high_knee_left", () => HighKnee(legL, kneeL), true),
                ("high_knee_right", () => HighKnee(legR, kneeR), true),
                ("lunge_left", () => Lunge(legL, kneeL, legR, kneeR), true),
                ("lunge_right", () => Lunge(legR, kneeR, legL, kneeL), true),
                ("side_split", () => {
                    Aim(legL, kneeL, new Vector3(Side(legL), -.32f, 0));
                    Aim(legR, kneeR, new Vector3(Side(legR), -.32f, 0));
                }, true),
                ("cross_step", () => {
                    Aim(legL, kneeL, new Vector3(-Side(legL)*.42f, -1, 0));
                    Aim(legR, kneeR, new Vector3(-Side(legR)*.42f, -1, 0));
                }, true),
                ("crouch", () => Crouch(.12f, false), true),
                ("high_knee_arms_left", () => {
                    HighKnee(legL, kneeL); ArmsForward();
                }, false),
                ("high_knee_arms_right", () => {
                    HighKnee(legR, kneeR); ArmsForward();
                }, false),
                ("wide_crouch_arms", () => Crouch(.62f, true), true),
            };
            foreach (var pose in stressPoses)
            {
                Reset();
                pose.apply();
                skirt.Evaluate(); head.Evaluate();
                string name = "stress_"+pose.name+"_000";
                Vector3[] posedFace = BakeWorld(face), posedNeck = BakeWorld(neck);
                foreach (var pair in pairs)
                    seamReport.maxGapM = Mathf.Max(seamReport.maxGapM,
                        Vector3.Distance(posedNeck[pair.first], posedFace[pair.second]));
                seamReport.sampledFrames++;
                WritePoints(geometry+"/"+name+"_garment.bin", BakeWorld(garment));
                WritePoints(geometry+"/"+name+"_skin.bin", BakeWorld(skin));
                WritePoints(geometry+"/"+name+"_neck.bin", BakeWorld(neck));
                WritePoints(geometry+"/"+name+"_face.bin", BakeWorld(face));
                if (pose.capture)
                    CaptureViews(character, camera, "stress_"+pose.name);
            }
            Reset();
            animator.enabled = true;
            seamReport.passed = seamReport.boundaryPairs == 14 && seamReport.maxGapM < 2e-6f;
            File.WriteAllText(Reports+"/neck_seam.json", JsonUtility.ToJson(seamReport, true));
            Require(seamReport.passed, "Neck seam regression failed");
        }

        static void CaptureViews(GameObject character, Camera camera, string pose)
        {
            var skins = character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            var proxies = new List<GameObject>();
            var meshes = new List<Mesh>();
            try
            {
                foreach (var skin in skins)
                {
                    var mesh = new Mesh();
                    skin.BakeMesh(mesh);
                    meshes.Add(mesh);
                    var proxy = new GameObject("Pose_"+skin.name, typeof(MeshFilter), typeof(MeshRenderer));
                    proxy.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
                    proxy.transform.localScale = Vector3.one;
                    proxy.GetComponent<MeshFilter>().sharedMesh = mesh;
                    proxy.GetComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                    proxies.Add(proxy);
                    skin.enabled = false;
                }
                foreach (var view in new[] { "front", "side", "back", "detail" })
                {
                    Vector3 target = new Vector3(0, view == "detail" ? 1.205f : .91f, 0);
                    Vector3 direction = view == "side" ? Vector3.right :
                        view == "back" ? Vector3.back : Vector3.forward;
                    SetCamera(camera, target+direction*3, target, view == "detail" ? .255f : .97f);
                    Capture(camera, Evidence+"/"+pose+"_"+view+".png", 1200, 1200);
                }
            }
            finally
            {
                foreach (var skin in skins) skin.enabled = true;
                foreach (var proxy in proxies) Object.DestroyImmediate(proxy);
                foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
            }
        }

        public static Vector3[] BakeWorld(SkinnedMeshRenderer skin)
        {
            var mesh = new Mesh();
            skin.BakeMesh(mesh);
            Vector3[] result = mesh.vertices;
            Matrix4x4 matrix = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
            for (int i = 0; i < result.Length; i++) result[i] = matrix.MultiplyPoint3x4(result[i]);
            Object.DestroyImmediate(mesh);
            return result;
        }

        static void WriteIndices(string path, int[] indices)
        {
            using (var writer = new BinaryWriter(File.Create(path)))
                foreach (int index in indices) writer.Write(index);
        }
        static void WritePoints(string path, Vector3[] points)
        {
            using (var writer = new BinaryWriter(File.Create(path)))
                foreach (var p in points) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); }
        }
        static void SetCamera(Camera camera, Vector3 position, Vector3 target, float size)
        {
            camera.transform.position = position; camera.transform.LookAt(target);
            camera.orthographicSize = size;
        }
        static void Capture(Camera camera, string path, int width, int height)
        {
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(image);
            }
        }
        static void Require(bool valid, string message)
        { if (!valid) throw new InvalidOperationException(message); }
    }
}
