using System;
using System.IO;
using System.Linq;
using MateSoul.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public static class BowlingContentBuilder
    {
        public const string GamePath = "Assets/MateSoul/Content/MiniGames/BowlingGame.prefab";
        public const string BallPath = "Assets/MateSoul/Content/MiniGames/BowlingBall.prefab";
        public const string PinPath = "Assets/MateSoul/Content/MiniGames/BowlingPin.prefab";
        private const string Source = "Assets/MateSoul/Content/PropsSource/Bowling";
        private const string Materials = "Assets/MateSoul/Content/MiniGames/BowlingMaterials";
        private const string Audio = "Assets/MateSoul/Content/Audio/ThirdParty/CrowdCheer_Short_CC-BY-4.0.mp3";

        [MenuItem("MateSoul/Game/Build Bowling Content")]
        public static BowlingGame Build()
        {
            Directory.CreateDirectory(Source);
            Directory.CreateDirectory(Materials);
            Directory.CreateDirectory("Assets/MateSoul/Content/MiniGames");
            var repository = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            Copy(Path.Combine(repository,
                    "ArtSource/SapphiArt/Props/BowlingBall/exports/LavenderChibiBowlingBall_218mm.fbx"),
                Source + "/LavenderChibiBowlingBall_218mm.fbx");
            Copy(Path.Combine(repository,
                    "ArtSource/SapphiArt/Props/BowlingPin/exports/LavenderChibiBowlingPin_380mm.fbx"),
                Source + "/LavenderChibiBowlingPin_380mm.fbx");
            Copy(Path.Combine(repository,
                    "ArtSource/SapphiArt/Props/BowlingBall/textures/bowling_character_decal.png"),
                Source + "/bowling_character_decal.png");
            AssetDatabase.Refresh();
            ConfigureImporter(Source + "/LavenderChibiBowlingBall_218mm.fbx");
            ConfigureImporter(Source + "/LavenderChibiBowlingPin_380mm.fbx");

            var ball = BuildBall();
            var pin = BuildPin();
            var cheer = AssetDatabase.LoadAssetAtPath<AudioClip>(Audio);
            if (cheer == null) throw new InvalidOperationException("保龄球欢呼音效导入失败。");
            var root = new GameObject("保龄球");
            try
            {
                var game = root.AddComponent<BowlingGame>();
                game.Configure(pin.GetComponent<BowlingPin>(), ball.GetComponent<BowlingBall>(), cheer, null);
                PrefabUtility.SaveAsPrefabAsset(root, GamePath);
                AssetDatabase.SaveAssets();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GamePath);
                return prefab.GetComponent<BowlingGame>();
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject BuildBall()
        {
            var root = new GameObject("保龄球");
            try
            {
                var view = InstantiateModel(Source + "/LavenderChibiBowlingBall_218mm.fbx", root);
                var bounds = BoundsOf(view);
                view.transform.position -= bounds.center;
                ConvertMaterials(view, "Ball");
                var collider = root.AddComponent<SphereCollider>();
                collider.radius = BowlingGame.BallRadius;
                PhysicsContentBuilder.ConfigureBody(root,
                    new BodySettings(6.5f, PhysicsContentBuilder.BowlingBall, .015f, .025f));
                var grab = root.AddComponent<PhysicsGrabInteractable>();
                grab.throwOnDetach = true;
                grab.ConfigureSnap(true);
                EditorUtility.SetDirty(grab);
                root.AddComponent<BowlingBall>();
                return PrefabUtility.SaveAsPrefabAsset(root, BallPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject BuildPin()
        {
            var root = new GameObject("保龄球瓶");
            try
            {
                var view = InstantiateModel(Source + "/LavenderChibiBowlingPin_380mm.fbx", root);
                var bounds = BoundsOf(view);
                if (bounds.size.z > bounds.size.y)
                {
                    view.transform.localRotation = Quaternion.Euler(-90, 0, 0);
                    bounds = BoundsOf(view);
                }
                view.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                bounds = BoundsOf(view);
                if (Mathf.Abs(bounds.size.x - .121f) > .008f ||
                    Mathf.Abs(bounds.size.y - .38f) > .008f ||
                    Mathf.Abs(bounds.size.z - .121f) > .008f)
                    throw new InvalidOperationException($"保龄球瓶导入尺寸错误：{bounds.size}");
                ConvertMaterials(view, "Pin");
                AddCapsule(root, "Lower Body Collider", new Vector3(0, .12f, 0), .06f, .24f);
                AddCapsule(root, "Neck Collider", new Vector3(0, .265f, 0), .032f, .17f);
                var head = new GameObject("Head Collider", typeof(SphereCollider));
                head.transform.SetParent(root.transform, false);
                head.transform.localPosition = new Vector3(0, .342f, 0);
                head.GetComponent<SphereCollider>().radius = .038f;
                PhysicsContentBuilder.ConfigureBody(root,
                    new BodySettings(1.55f, PhysicsContentBuilder.BowlingPin, .01f, .035f));
                root.GetComponent<Rigidbody>().centerOfMass = new Vector3(0, .145f, 0);
                root.AddComponent<BowlingPin>();
                return PrefabUtility.SaveAsPrefabAsset(root, PinPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject InstantiateModel(string path, GameObject root)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) throw new InvalidOperationException($"保龄球模型导入失败：{path}");
            var view = (GameObject)PrefabUtility.InstantiatePrefab(model);
            view.name = "Model";
            view.transform.SetParent(root.transform, false);
            if (view.GetComponentsInChildren<Renderer>().Length == 0)
                throw new InvalidOperationException($"{path} 没有可渲染网格。");
            return view;
        }

        private static void ConfigureImporter(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;
            importer.SaveAndReimport();
        }

        private static void ConvertMaterials(GameObject view, string prefix)
        {
            foreach (var renderer in view.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(source =>
                {
                    var safeName = string.Concat(source.name.Select(c =>
                        char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_'));
                    var path = $"{Materials}/{prefix}_{safeName}.mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null)
                    {
                        material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                        { name = prefix + "_" + source.name };
                        AssetDatabase.CreateAsset(material, path);
                    }
                    material.SetColor("_BaseColor", source.HasProperty("_Color")
                        ? source.color : Color.white);
                    var texture = source.mainTexture;
                    if (texture == null && source.name.IndexOf("decal", StringComparison.OrdinalIgnoreCase) >= 0)
                        texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Source + "/bowling_character_decal.png");
                    material.SetTexture("_BaseMap", texture);
                    material.SetFloat("_Smoothness", prefix == "Ball" ? .72f : .58f);
                    if (source.name.IndexOf("decal", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        material.SetFloat("_Surface", 1);
                        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                        material.SetFloat("_ZWrite", 0);
                        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        material.renderQueue = (int)RenderQueue.Transparent;
                    }
                    EditorUtility.SetDirty(material);
                    return material;
                }).ToArray();
        }

        private static void AddCapsule(GameObject root, string name, Vector3 center,
            float radius, float height)
        {
            var child = new GameObject(name, typeof(CapsuleCollider));
            child.transform.SetParent(root.transform, false);
            var collider = child.GetComponent<CapsuleCollider>();
            collider.center = center;
            collider.radius = radius;
            collider.height = height;
            collider.direction = 1;
        }

        private static Bounds BoundsOf(GameObject view)
        {
            var renderers = view.GetComponentsInChildren<Renderer>(true);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static void Copy(string source, string assetPath)
        {
            if (!File.Exists(source)) throw new FileNotFoundException("保龄球源资产不存在。", source);
            var destination = Path.GetFullPath(Path.Combine(Application.dataPath,
                assetPath.Substring("Assets/".Length)));
            if (!File.Exists(destination) ||
                !File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(destination)))
                File.Copy(source, destination, true);
        }
    }
}
