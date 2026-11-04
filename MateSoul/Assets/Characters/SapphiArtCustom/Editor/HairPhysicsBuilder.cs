using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MateSoul.Hair;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public static class HairPhysicsBuilder
    {
        const string Vendor = "Assets/SapphiArt/SapphiArtchan";
        const string Base = "Assets/Characters/SapphiArtCustom";
        const string Target = Base + "/Hair/V002Physics";
        [Serializable] class Definition {
            public Joint[] bones = Array.Empty<Joint>();
            public Landmark[] landmarks = Array.Empty<Landmark>();
            public Sphere[] colliders = Array.Empty<Sphere>();
        }
        [Serializable] class Joint {
            public string name = ""; public int parent; public float[] tailWorld = Array.Empty<float>();
            public bool full, balanced; public float stiffness, drag, gravity;
        }
        [Serializable] class Landmark { public string name = ""; public float[] position = Array.Empty<float>(); }
        [Serializable] class Sphere { public float[] center = Array.Empty<float>(); public float radius; }
        static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);
        static Transform Find(GameObject go, string name) =>
            go.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static Quaternion Basis(Vector3 head, Vector3 top, Vector3 left, Vector3 right) =>
            Quaternion.LookRotation(Vector3.Cross((left-right).normalized,(top-head).normalized),(top-head).normalized);
        static Matrix4x4 LandmarkFrame(Vector3 head, Vector3 top, Vector3 left, Vector3 right) =>
            Matrix4x4.TRS(head, Basis(head, top, left, right), Vector3.one * Vector3.Distance(left, right));
        static Matrix4x4 CoordinateFrame(Vector3[] p)
        {
            var matrix = Matrix4x4.identity;
            matrix.SetColumn(0, new Vector4(p[1].x-p[0].x,p[1].y-p[0].y,p[1].z-p[0].z,0));
            matrix.SetColumn(1, new Vector4(p[2].x-p[0].x,p[2].y-p[0].y,p[2].z-p[0].z,0));
            matrix.SetColumn(2, new Vector4(p[3].x-p[0].x,p[3].y-p[0].y,p[3].z-p[0].z,0));
            matrix.SetColumn(3, new Vector4(p[0].x,p[0].y,p[0].z,1));
            return matrix;
        }

        [MenuItem("MateSoul/Hair Lab/Build v002 Physics And Benchmark")]
        public static void Build()
        {
            try { Run(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        static void Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string reports = Path.Combine(repo, "ArtSource/SapphiArt/HairLab/v002/physics/reports");
            Directory.CreateDirectory(reports);
            foreach (string sub in new[] { "Materials", "Prefabs", "Scenes", "Settings" })
                Directory.CreateDirectory(Target + "/" + sub);
            AssetDatabase.Refresh();
            string fbxPath = Target + "/Models/Lengyue_Bob_Physics_v002.fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
            Check(importer != null, "Stage exported FBX before running builder");
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = false;
            importer.optimizeGameObjects = false;
            importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.skinWeights = ModelImporterSkinWeights.Custom;
            importer.maxBonesPerVertex = 4;
            importer.SaveAndReimport();
            string texturePath = Target + "/Textures/Lengyue_Bob_v002_Albedo.png";
            var textureImporter = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            textureImporter.mipmapEnabled = true;
            textureImporter.isReadable = false;
            textureImporter.sRGBTexture = true;
            textureImporter.SetPlatformTextureSettings(new TextureImporterPlatformSettings {
                name = "Android", overridden = true, maxTextureSize = 1024,
                format = TextureImporterFormat.ASTC_6x6, compressionQuality = 50
            });
            textureImporter.SaveAndReimport();
            var definition = JsonUtility.FromJson<Definition>(
                AssetDatabase.LoadAssetAtPath<TextAsset>(Target + "/hair-physics-definition.json").text);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var character = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(Vendor + "/OBJ/SapphiArtchan.prefab"));
            character.name = "SapphiArt_v002_AutoPhysics";
            var originalSkins = character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var originalMeshes = originalSkins.Select(s => s.sharedMesh).ToArray();
            var transforms = character.GetComponentsInChildren<Transform>(true);
            var rest = transforms.Select(t => (t.localPosition, t.localRotation, t.localScale)).ToArray();
            foreach (var c in character.GetComponentsInChildren<MonoBehaviour>(true))
                if (c && c.GetType().Name == "SapphiArtChan_AnimManager") c.enabled = false;
            foreach (string name in new[] { "hairFront", "hairBack", "hairAcce", "tongue", "TopTeeth" })
                Find(character, name).GetComponent<Renderer>().enabled = false;
            var hair = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath));
            var imported = new[] { "Head", "Head_End", "Eye_L", "Eye_R" }
                .Select(n => Find(hair, "PhysicsAlign_" + n).position).ToArray();
            var targets = new[] { "Head 1", "Head_End", "Eye_L", "Eye_R" }
                .Select(n => Find(character, n).position).ToArray();
            var sourceFrame = LandmarkFrame(imported[0], imported[1], imported[2], imported[3]);
            var targetFrame = LandmarkFrame(targets[0], targets[1], targets[2], targets[3]);
            var alignment = targetFrame * sourceFrame.inverse;
            hair.transform.SetPositionAndRotation(alignment.GetColumn(3), alignment.rotation);
            hair.transform.localScale = alignment.lossyScale;
            float alignmentError = Enumerable.Range(0,4).Max(i =>
                Vector3.Distance(Find(hair, definition.landmarks[i].name).position, targets[i]));
            Check(alignmentError < .001f, "FBX alignment failed " + alignmentError);
            hair.transform.SetParent(Find(character, "Head 1"), true);
            foreach (var animator in hair.GetComponentsInChildren<Animator>(true))
                animator.enabled = false;
            var skin = hair.GetComponentsInChildren<SkinnedMeshRenderer>().Single();
            Check(skin.bones.Length == 247, "Imported hair bone count mismatch");
            int triangles = (int)Enumerable.Range(0,skin.sharedMesh.subMeshCount)
                .Sum(i => (long)skin.sharedMesh.GetIndexCount(i))/3;
            Check(triangles == 23666, "Imported triangle count mismatch");
            skin.updateWhenOffscreen = false;
            skin.quality = SkinQuality.Bone4;
            var bounds = skin.localBounds;
            bounds.Expand(.08f / skin.transform.lossyScale.x);
            skin.localBounds = bounds;
            var sourcePoints = definition.landmarks.Select(l => V(l.position)).ToArray();
            // Four noncoplanar landmarks preserve Blender/Unity handedness too.
            Matrix4x4 blenderToWorld = CoordinateFrame(targets) * CoordinateFrame(sourcePoints).inverse;
            var physics = hair.AddComponent<HairSpringPhysics>();
            physics.bones = definition.bones.Select(b => {
                var bone = Find(hair,b.name);
                return new HairSpringPhysics.Bone {
                    transform = bone, parent = b.parent,
                    tailLocal = bone.InverseTransformPoint(blenderToWorld.MultiplyPoint3x4(V(b.tailWorld))),
                    full = b.full, balanced = b.balanced,
                    stiffness = b.stiffness, drag = b.drag,
                    gravity = b.gravity,
                    lateralInertiaResponse = 0f,
                    maxLateralDegrees = 0f,
                    maxDepthDegrees = 0f
                };
            }).ToArray();
            physics.colliders = definition.colliders.Select(c => new HairSpringPhysics.ColliderSphere {
                transform = hair.transform,
                offset = hair.transform.InverseTransformPoint(blenderToWorld.MultiplyPoint3x4(V(c.center))),
                radius = c.radius * blenderToWorld.lossyScale.x / hair.transform.lossyScale.x
            }).ToArray();
            physics.quality = HairSpringSolver.Quality.Balanced;
            physics.Initialize();
            var cache = new Dictionary<Material, Material>();
            foreach (var renderer in character.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == skin) continue;
                renderer.sharedMaterials = renderer.sharedMaterials.Select(original => {
                    if (!cache.TryGetValue(original, out var mat))
                    {
                        mat = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
                        mat.name = original.name + "_PhysicsURP";
                        if (original.HasProperty("_MainTex")) mat.SetTexture("_BaseMap",original.GetTexture("_MainTex"));
                        mat.SetColor("_BaseColor",Color.white);
                        mat.SetColor("_SpecColor",Color.black);
                        cache.Add(original,SaveMaterial(mat));
                    }
                    return cache[original];
                }).ToArray();
            }
            var hairMaterial = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            hairMaterial.name = "Lengyue_v002_PhysicsURP";
            hairMaterial.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            hairMaterial.SetColor("_SpecColor",Color.black);
            skin.sharedMaterial = SaveMaterial(hairMaterial);
            Check(originalSkins.Select((s,i)=>s.sharedMesh==originalMeshes[i]).All(x=>x),"Original mesh modified");
            Check(transforms.Select((t,i)=>(t.localPosition,t.localRotation,t.localScale)==rest[i]).All(x=>x),
                "Original rest pose modified");
            Check(originalSkins.Sum(s=>s.sharedMesh.blendShapeCount)==22,"Original expressions missing");
            physics.ResetSimulation();
            var prefab = PrefabUtility.SaveAsPrefabAsset(character,Target+"/Prefabs/SapphiArt_v002_Physics.prefab");
            Check(PrefabUtility.GetPrefabAssetType(prefab)==PrefabAssetType.Variant,"Expected Prefab Variant");
            // Disable body animation only in the controlled A/B scene, not in the saved prefab.
            character.GetComponent<Animator>().enabled = false;
            var camera = new GameObject("Benchmark Camera",typeof(Camera)).GetComponent<Camera>();
            camera.tag="MainCamera";
            camera.transform.position=new Vector3(0,1.50f,2.6f);
            camera.transform.LookAt(new Vector3(0,1.28f,0));
            camera.fieldOfView=38;
            camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.3f,.3f,.32f);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            var light=new GameObject("Key",typeof(Light)).GetComponent<Light>();
            light.type=LightType.Directional;
            light.transform.rotation=Quaternion.Euler(35,-30,0);
            light.shadows=LightShadows.None;
            light.intensity=1;
            RenderSettings.ambientMode=AmbientMode.Flat;
            RenderSettings.ambientLight=new Color(.6f,.6f,.6f);
            var probe=new GameObject("Hair Performance Capture").AddComponent<HairPerformanceProbe>();
            probe.physics=physics; probe.hairRenderer=skin; probe.head=Find(character,"Head 1");
            if (Path.GetFileName(Directory.GetParent(Application.dataPath).FullName)==".hairlab-unity")
            {
                var renderer=ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer,Target+"/Settings/Renderer.asset");
                var pipeline=UniversalRenderPipelineAsset.Create(renderer);
                pipeline.msaaSampleCount=1;
                AssetDatabase.CreateAsset(pipeline,Target+"/Settings/Pipeline.asset");
                GraphicsSettings.defaultRenderPipeline=pipeline;
                QualitySettings.renderPipeline=pipeline;
            }
            EditorSceneManager.SaveScene(scene,Target+"/Scenes/HairPhysicsBenchmark.unity");
            AssetDatabase.SaveAssets();
            File.WriteAllText(Path.Combine(reports,"unity_import.json"),JsonUtility.ToJson(new ImportReport {
                unity=Application.unityVersion,vertices=skin.sharedMesh.vertexCount,triangles=triangles,
                bones=skin.bones.Length,blendShapes=originalSkins.Sum(s=>s.sharedMesh.blendShapeCount),
                alignmentErrorM=alignmentError,materials=skin.sharedMaterials.Length,
                meshBytes=UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(skin.sharedMesh)
            },true));
            Debug.Log("V002_PHYSICS_IMPORT_PASS");
            if(Application.isBatchMode) EditorApplication.Exit(0);
        }

        [Serializable] class ImportReport {
            public string unity; public int vertices,triangles,bones,blendShapes,materials;
            public float alignmentErrorM; public long meshBytes;
        }
        static Material SaveMaterial(Material mat)
        {
            string path=Target+"/Materials/"+mat.name+".mat";
            var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(existing) { EditorUtility.CopySerialized(mat,existing); Object.DestroyImmediate(mat); return existing; }
            AssetDatabase.CreateAsset(mat,path);
            return mat;
        }
    }
}
