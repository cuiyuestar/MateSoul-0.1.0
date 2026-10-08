using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MateSoul.Experiments.YummyCookie
{
    public static class YummyCookieBuilder
    {
        public const string Root = "Assets/Experiments/YummyCookie_v001";
        const string Baseline = "Assets/Characters/SapphiArtCustom/Lengyue";
        public static string Repository => Path.GetFullPath(Application.dataPath + "/../..");
        public static string Output => Repository + "/ArtSource/SapphiArt/Animation/YummyCookie_v001";
        static double nextPoll;

        [InitializeOnLoadMethod]
        static void Register()
        {
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll ||
                EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            foreach (string command in new[] { "refresh", "analyze", "build" })
            {
                string request = Repository + "/.dbg/yummy-cookie-v001/" + command;
                if (!File.Exists(request)) continue;
                File.Delete(request);
                try
                {
                    File.Delete(Output + "/reports/error.txt");
                    if (command == "refresh") AssetDatabase.Refresh();
                    else if (command == "analyze") Analyze();
                    else Build();
                }
                catch (Exception e)
                {
                    File.WriteAllText(Output + "/reports/error.txt", e.ToString());
                    Debug.LogException(e);
                }
                return;
            }
        }

        [Serializable] sealed class Landmark
        {
            public string name;
            public Vector3 position, localPosition;
            public Quaternion rotation;
        }
        [Serializable] sealed class Pose
        {
            public int frame;
            public Landmark[] landmarks;
            public Vector3 mouth;
        }
        [Serializable] sealed class Analysis
        {
            public string clip;
            public float duration, fps;
            public int[] mouthVertices;
            public Vector3[] faceAtFrame0;
            public Pose[] frames;
        }

        [MenuItem("MateSoul/Experiments/Yummy Cookie v001/Analyze Source Animation")]
        public static void Analyze()
        {
            Directory.CreateDirectory(Output + "/reports");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                Baseline + "/Runtime/Prefabs/Lengyue_GameCharacter.prefab");
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                Baseline + "/Runtime/Animations/Yummy/Lengyue_Yummy_BodyOnly.anim");
            GameObject character = Object.Instantiate(prefab);
            character.hideFlags = HideFlags.HideAndDontSave;
            var baked = new Mesh();
            try
            {
                foreach (var behaviour in character.GetComponentsInChildren<Behaviour>(true))
                    behaviour.enabled = false;
                var face = character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Single(r => r.name == "face");
                var delta = new Vector3[face.sharedMesh.vertexCount];
                int shape = Enumerable.Range(0, face.sharedMesh.blendShapeCount)
                    .Single(i => face.sharedMesh.GetBlendShapeName(i).EndsWith("face_O_BS"));
                face.sharedMesh.GetBlendShapeFrameVertices(shape, 0, delta, null, null);
                int[] ids = Enumerable.Range(0, delta.Length)
                    .Where(i => delta[i].sqrMagnitude > 0.000001f).ToArray();
                var bones = character.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name.StartsWith("Hand_") || t.name.StartsWith("Index_") ||
                        t.name.StartsWith("Thumb_") || t.name.StartsWith("Middle_") ||
                        t.name.StartsWith("Ring_") || t.name.StartsWith("Pinky_") ||
                        t.name == "Head 1" || t.name == "Neck").ToArray();
                var analysis = new Analysis {
                    clip = clip.name, duration = clip.length, fps = clip.frameRate,
                    mouthVertices = ids, frames = new Pose[173]
                };
                for (int f = 0; f < analysis.frames.Length; f++)
                {
                    clip.SampleAnimation(character, f / 30f);
                    face.BakeMesh(baked);
                    Vector3[] vertices = baked.vertices;
                    Vector3 mouth = Vector3.zero;
                    foreach (int i in ids) mouth += face.transform.TransformPoint(vertices[i]);
                    mouth /= ids.Length;
                    if (f == 0) analysis.faceAtFrame0 = vertices
                        .Select(face.transform.TransformPoint).ToArray();
                    analysis.frames[f] = new Pose {
                        frame = f, mouth = mouth,
                        landmarks = bones.Select(t => new Landmark {
                            name = t.name, position = t.position,
                            localPosition = t.localPosition, rotation = t.rotation
                        }).ToArray()
                    };
                }
                File.WriteAllText(Output + "/reports/animation_semantics.json",
                    JsonUtility.ToJson(analysis, true));
                Debug.Log("YUMMY_COOKIE_ANALYSIS_COMPLETE");
            }
            finally
            {
                Object.DestroyImmediate(baked);
                Object.DestroyImmediate(character);
            }
        }

        static Material LitMaterial(string name, string texture, float smoothness, Color tint)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            if (texture != null)
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(
                    Root + "/Props/" + texture));
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            string path = Root + "/Props/" + name + ".mat";
            var old = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (old)
            {
                EditorUtility.CopySerialized(material, old);
                Object.DestroyImmediate(material);
                return old;
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static GameObject CookieAsset(Material material)
        {
            string path = Root + "/Props/ChocolateChipCookie.fbx";
            var imported = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            try
            {
                var mesh = new Mesh { name = "Cookie_Combined", indexFormat = IndexFormat.UInt32 };
                var combine = imported.GetComponentsInChildren<MeshFilter>()
                    .SelectMany(f => Enumerable.Range(0, f.sharedMesh.subMeshCount)
                        .Select(s => new CombineInstance {
                            mesh = f.sharedMesh, subMeshIndex = s,
                            transform = f.transform.localToWorldMatrix
                        })).ToArray();
                mesh.CombineMeshes(combine, true, true);
                Vector3 center = mesh.bounds.center;
                mesh.vertices = mesh.vertices.Select(p => p - center).ToArray();
                mesh.RecalculateBounds();
                if (Mathf.Abs(mesh.bounds.size.x - .05f) > .001f ||
                    Mathf.Abs(mesh.bounds.size.y - .011f) > .001f)
                    throw new InvalidOperationException("Cookie import axes/scale incorrect: " + mesh.bounds);
                string meshPath = Root + "/Props/Cookie_Combined.asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath)) AssetDatabase.DeleteAsset(meshPath);
                AssetDatabase.CreateAsset(mesh, meshPath);
                var cookie = new GameObject("Cookie", typeof(MeshFilter), typeof(MeshRenderer));
                cookie.GetComponent<MeshFilter>().sharedMesh = mesh;
                cookie.GetComponent<MeshRenderer>().sharedMaterial = material;
                var result = PrefabUtility.SaveAsPrefabAsset(cookie, Root + "/Props/Cookie.prefab");
                Object.DestroyImmediate(cookie);
                return result;
            }
            finally { Object.DestroyImmediate(imported); }
        }

        [MenuItem("MateSoul/Experiments/Yummy Cookie v001/Build Isolated Demo Player")]
        public static void Build()
        {
            Directory.CreateDirectory(Output + "/build");
            Directory.CreateDirectory(Output + "/reports");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string name in new[] { "ChocolateChipCookie", "LavenderChibiPlate" })
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(Root + "/Props/" + name + ".fbx");
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importAnimation = false;
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
            var normalImporter = (TextureImporter)AssetImporter.GetAtPath(Root + "/Props/Cookie_Normal.png");
            normalImporter.textureType = TextureImporterType.NormalMap;
            normalImporter.SaveAndReimport();
            var cookieMaterial = LitMaterial("Cookie_Baked", "Cookie_BaseColor.png", .18f, Color.white);
            cookieMaterial.SetTexture("_BumpMap",
                AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Props/Cookie_Normal.png"));
            cookieMaterial.EnableKeyword("_NORMALMAP");
            cookieMaterial.SetFloat("_BumpScale", .7f);
            EditorUtility.SetDirty(cookieMaterial);
            var cookiePrefab = CookieAsset(cookieMaterial);
            var plateMaterials = new[] {
                LitMaterial("Plate_Top", "plate_top_basecolor.png", .7f, Color.white),
                LitMaterial("Plate_Edge", "plate_edge_basecolor.png", .7f, Color.white),
                LitMaterial("Plate_Underside", "plate_accent_basecolor.png", .65f, Color.white)
            };
            string scenePath = Root + "/YummyCookie_v001.unity";
            var setup = EditorSceneManager.GetSceneManagerSetup();
            Scene originalActive = SceneManager.GetActiveScene();
            Scene scene = default;
            BuildTarget previousTarget = EditorUserBuildSettings.activeBuildTarget;
            try
            {
                if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath))
                    AssetDatabase.CopyAsset(Baseline + "/Validation/Lengyue_Yummy_Capture.unity", scenePath);
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                // Rebuild only this disposable copy of the baseline capture scene.
                foreach (var root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
                string baselineScenePath = Baseline + "/Validation/Lengyue_Yummy_Capture.unity";
                Scene baselineScene = SceneManager.GetSceneByPath(baselineScenePath);
                bool openedBaseline = !baselineScene.IsValid() || !baselineScene.isLoaded;
                if (openedBaseline)
                    baselineScene = EditorSceneManager.OpenScene(
                        baselineScenePath, OpenSceneMode.Additive);
                var roots = baselineScene.GetRootGameObjects()
                    .Select(root => Object.Instantiate(root)).ToArray();
                foreach (var root in roots) SceneManager.MoveGameObjectToScene(root, scene);
                if (openedBaseline) EditorSceneManager.CloseScene(baselineScene, true);
                SceneManager.SetActiveScene(scene);
                var character = roots.Single(g => g.GetComponent<Animator>());
                character.name = "Lengyue_YummyCookie_v001";
                var camera = roots.SelectMany(g => g.GetComponentsInChildren<Camera>()).Single();
                Object.DestroyImmediate(camera.GetComponent<MateSoul.Validation.LengyueYummyCaptureProbe>());
                camera.name = "Cookie Demo Camera";
                camera.transform.position = new Vector3(.7f, 2.2f, 3.2f);
                camera.transform.LookAt(new Vector3(.08f, 1.38f, .12f));
                camera.orthographicSize = .53f;
                var demo = camera.gameObject.AddComponent<YummyCookieDemo>();
                demo.character = character;
                demo.renderCamera = camera;
                demo.clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    Baseline + "/Runtime/Animations/Yummy/Lengyue_Yummy_BodyOnly.anim");
                var bones = character.GetComponentsInChildren<Transform>(true);
                demo.hand = bones.Single(t => t.name == "Hand_R");
                demo.forearm = demo.hand.parent;
                demo.upperArm = demo.forearm.parent;
                demo.head = bones.Single(t => t.name == "Head 1");
                demo.outputDirectory = Output;
                var analysis = JsonUtility.FromJson<Analysis>(
                    File.ReadAllText(Output + "/reports/animation_semantics.json"));
                Pose bite = analysis.frames[21];
                var headPose = bite.landmarks.Single(p => p.name == "Head 1");
                demo.mouthHeadLocal = Quaternion.Inverse(headPose.rotation) *
                    (bite.mouth - headPose.position);
                demo.clip.SampleAnimation(character, 0);
                demo.gripLocalRotation = Quaternion.Inverse(demo.hand.rotation);
                Vector3 pickCenter = demo.hand.TransformPoint(demo.gripLocalPosition);
                var plateRoot = new GameObject("PlateAndCookies");
                SceneManager.MoveGameObjectToScene(plateRoot, scene);
                demo.plate = plateRoot.transform;
                // Pick the top cookie on the near-left part of a 14-cookie stack.
                Vector3 pickOffset = new Vector3(-.026f, .0385f, -.04503332f);
                plateRoot.transform.position = pickCenter - pickOffset;
                var dish = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Props/LavenderChibiPlate.fbx"), scene);
                dish.name = "LavenderChibiPlate";
                dish.transform.SetParent(plateRoot.transform, false);
                foreach (var r in dish.GetComponentsInChildren<MeshRenderer>())
                    r.sharedMaterials = plateMaterials;
                var pose = new GameObject("PickCookiePose");
                pose.transform.SetParent(plateRoot.transform, false);
                pose.transform.localPosition = pickOffset;
                demo.servingPose = pose.transform;
                // A center cookie, two rings of six, and one top cookie.
                // The ring's outer edge stays within radius 77 mm, inside the raised rim.
                int count = 0;
                var centerCookie = (GameObject)PrefabUtility.InstantiatePrefab(cookiePrefab, scene);
                centerCookie.name = "PlateCookie_" + count++;
                centerCookie.transform.SetParent(plateRoot.transform, false);
                centerCookie.transform.localPosition = new Vector3(0, .012f, 0);
                for (int layer = 0; layer < 2; layer++)
                    for (int index = 0; index < 6; index++)
                    {
                        float angle = index * Mathf.PI / 3;
                        var c = (GameObject)PrefabUtility.InstantiatePrefab(cookiePrefab, scene);
                        c.name = "PlateCookie_" + count++;
                        c.transform.SetParent(plateRoot.transform, false);
                        c.transform.localPosition = new Vector3(
                            Mathf.Cos(angle) * .052f, .0155f + layer * .0115f,
                            Mathf.Sin(angle) * .052f);
                        c.transform.localRotation = Quaternion.Euler(0, count * 67, 0);
                    }
                demo.cookie = (GameObject)PrefabUtility.InstantiatePrefab(cookiePrefab, scene);
                demo.cookie.name = "Cookie_ToEat";
                demo.cookie.transform.SetParent(plateRoot.transform, false);
                demo.cookie.transform.localPosition = pickOffset;
                demo.cookieCount = count + 1;
                var tableMat = LitMaterial("Table_Lavender", null, .28f,
                    new Color(.43f, .37f, .49f));
                var table = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                table.name = "ServingTable";
                SceneManager.MoveGameObjectToScene(table, scene);
                table.transform.position = plateRoot.transform.position - Vector3.up * .022f;
                table.transform.localScale = new Vector3(.38f, .022f, .38f);
                table.GetComponent<MeshRenderer>().sharedMaterial = tableMat;
                Object.DestroyImmediate(table.GetComponent<Collider>());
                var leg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                leg.name = "TablePedestal";
                SceneManager.MoveGameObjectToScene(leg, scene);
                leg.transform.position = new Vector3(table.transform.position.x,
                    (table.transform.position.y - .022f) / 2, table.transform.position.z);
                leg.transform.localScale = new Vector3(.075f, leg.transform.position.y, .075f);
                leg.GetComponent<MeshRenderer>().sharedMaterial = tableMat;
                Object.DestroyImmediate(leg.GetComponent<Collider>());
                PrefabUtility.SaveAsPrefabAsset(character, Root + "/Lengyue_YummyCookie_v001.prefab");
                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveScene(scene);
                File.WriteAllText(Output + "/reports/scene_setup.json",
                    "{\"cookieCount\":" + demo.cookieCount +
                    ",\"pickupSourceFrame\":0,\"biteSourceFrame\":22,\"leadInFrames\":24," +
                    "\"upperArm\":\"" + demo.upperArm.name + "\",\"forearm\":\"" +
                    demo.forearm.name + "\",\"platePosition\":" +
                    JsonUtility.ToJson(plateRoot.transform.position) + "}");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone,
                        BuildTarget.StandaloneOSX))
                    throw new InvalidOperationException("Could not switch to StandaloneOSX");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { scenePath }, target = BuildTarget.StandaloneOSX,
                    locationPathName = Output + "/build/YummyCookie_v001.app",
                    options = BuildOptions.None
                });
                File.WriteAllText(Output + "/reports/player_build.json",
                    "{\"result\":\"" + report.summary.result + "\",\"errors\":" +
                    report.summary.totalErrors + ",\"warnings\":" +
                    report.summary.totalWarnings + "}");
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("Cookie demo build failed");
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (setup.Any(s => s.isLoaded && !string.IsNullOrEmpty(s.path)))
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                else if (originalActive.IsValid() && originalActive.isLoaded)
                    SceneManager.SetActiveScene(originalActive);
                else
                    EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                if (previousTarget != EditorUserBuildSettings.activeBuildTarget)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(
                        BuildPipeline.GetBuildTargetGroup(previousTarget), previousTarget);
            }
        }
    }
}
