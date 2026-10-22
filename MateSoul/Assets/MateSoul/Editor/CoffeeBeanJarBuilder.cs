using System;
using System.IO;
using System.Linq;
using MateSoul.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public static class CoffeeBeanJarBuilder
    {
        public const string PrefabPath =
            "Assets/MateSoul/Content/Props/CoffeeBeanJar.prefab";
        public const string BeanPrefabPath =
            "Assets/MateSoul/Content/Props/CoffeeBean.prefab";
        public const string DefinitionPath =
            "Assets/MateSoul/Content/Props/CoffeeBeanJar.asset";
        public const string ModelPath =
            "Assets/MateSoul/Content/PropsSource/CoffeeBeanJar/" +
            "CoffeeBeanJar_120x120x194mm.fbx";
        public const string BeanModelPath =
            "Assets/MateSoul/Content/PropsSource/CoffeeBeanJar/" +
            "CoffeeBean_Single_12mm.fbx";

        private const string MaterialsRoot =
            "Assets/MateSoul/Content/Props/Materials/CoffeeBeanJar";

        [MenuItem("MateSoul/Game/Build Coffee Bean Jar Asset")]
        public static void BuildAsset()
        {
            var focus = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/MateSoul/Content/ItemFocusHighlight.mat");
            Build(focus);
        }

        public static ItemDefinition Build(Material focusMaterial)
        {
            if (focusMaterial == null)
                throw new InvalidOperationException("咖啡豆罐交互光圈材质缺失。");
            ConfigureImporter(ModelPath);
            ConfigureImporter(BeanModelPath);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var beanSource =
                AssetDatabase.LoadAssetAtPath<GameObject>(BeanModelPath);
            if (source == null)
                throw new InvalidOperationException($"咖啡豆罐模型导入失败：{ModelPath}");
            if (beanSource == null)
                throw new InvalidOperationException(
                    $"咖啡豆模型导入失败：{BeanModelPath}");

            Directory.CreateDirectory(MaterialsRoot);
            var beanPrefab = BuildBeanPrefab(beanSource);
            var beanRenderer = beanPrefab.GetComponentInChildren<MeshRenderer>(true);
            var beanFilter = beanRenderer?.GetComponent<MeshFilter>();
            if (beanRenderer == null || beanFilter?.sharedMesh == null)
                throw new InvalidOperationException("独立咖啡豆缺少渲染网格。");
            var root = new GameObject("咖啡豆罐");
            root.SetActive(false);
            try
            {
                var view = (GameObject)PrefabUtility.InstantiatePrefab(source);
                view.name = "Model";
                view.transform.SetParent(root.transform, false);
                PrefabUtility.UnpackPrefabInstance(view,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                var bounds = BoundsOf(view);
                var expected = new Vector3(.12f, .194f, .12f);
                var tolerance = new Vector3(.004f, .005f, .004f);
                if (Mathf.Abs(bounds.size.x - expected.x) > tolerance.x ||
                    Mathf.Abs(bounds.size.y - expected.y) > tolerance.y ||
                    Mathf.Abs(bounds.size.z - expected.z) > tolerance.z)
                    throw new InvalidOperationException(
                        $"咖啡豆罐导入尺寸错误：{bounds.size}，期望 {expected}。");
                view.transform.position += new Vector3(
                    -bounds.center.x, -bounds.min.y, -bounds.center.z);
                foreach (var renderer in view.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials
                        .Select(ConvertMaterial).ToArray();

                var lidPivot = Find(view, "CoffeeBeanJar_Lid_Pivot");
                var pourOrigin = Find(view, "CoffeeBeanJar_PourOrigin");
                var lidRenderer = FindRenderer(view, "CoffeeBeanJar_WoodLid");
                var lidSeal = Find(view, "CoffeeBeanJar_LidSeal");
                var lidMesh = lidRenderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (lidMesh == null)
                    throw new InvalidOperationException("咖啡豆罐木盖缺少 MeshFilter。");
                foreach (var interior in view.GetComponentsInChildren<Renderer>(true)
                             .Where(renderer => renderer.name.StartsWith(
                                 "CoffeeBeanJar_InteriorBean",
                                 StringComparison.Ordinal)).ToArray())
                    Object.DestroyImmediate(interior.gameObject);

                var bodyCollider = root.AddComponent<CapsuleCollider>();
                bodyCollider.center = new Vector3(0, .091f, 0);
                bodyCollider.radius = .054f;
                bodyCollider.height = .182f;
                bodyCollider.direction = 1;

                var lidCollider = lidRenderer.gameObject.AddComponent<BoxCollider>();
                lidCollider.center = lidMesh.bounds.center;
                lidCollider.size = lidMesh.bounds.size;

                var physics = new BodySettings(
                    1.05f, PhysicsContentBuilder.Glass, .035f, .08f);
                PhysicsContentBuilder.ConfigureBody(root, physics);

                var fillObject = new GameObject("Coffee Bean Fill Instances");
                fillObject.transform.SetParent(root.transform, false);
                var fill = fillObject.AddComponent<CoffeeBeanVolumeRenderer>();
                fill.Configure(beanFilter.sharedMesh, beanRenderer.sharedMaterial,
                    720, .043f, .148f, .018f, 20261017);

                var socketObject = new GameObject("Coffee Bean Jar Lid Socket");
                var socket = socketObject.transform;
                socket.SetParent(lidPivot.parent, false);
                socket.localPosition = lidPivot.localPosition;
                socket.localRotation = lidPivot.localRotation;
                socket.localScale = lidPivot.localScale;
                lidSeal.SetParent(lidPivot, true);
                lidPivot.SetParent(root.transform, true);

                var lidPhysics = new BodySettings(
                    .085f, PhysicsContentBuilder.Wood, .04f, .12f);
                PhysicsContentBuilder.ConfigureBody(
                    lidPivot.gameObject, lidPhysics);
                var lid = lidPivot.gameObject.AddComponent<CoffeeBeanJarLid>();
                lid.Configure(root.transform, socket, lidCollider,
                    .018f, 14f, .08f);
                var interactable =
                    lidPivot.gameObject.AddComponent<CoffeeBeanJarLidInteractable>();
                interactable.Configure(lid);
                var focus =
                    lidPivot.gameObject.AddComponent<ItemInteractionFocusTarget>();
                focus.Configure(null, focusMaterial, lidRenderer);

                var controller = root.AddComponent<CoffeeBeanJarController>();
                controller.Configure(lid, fill, pourOrigin,
                    beanPrefab, 336, 8, .06f, .52f, 24, 2);

                root.SetActive(true);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                var definition =
                    AssetDatabase.LoadAssetAtPath<ItemDefinition>(DefinitionPath);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ItemDefinition>();
                    AssetDatabase.CreateAsset(definition, DefinitionPath);
                }
                definition.Configure("coffee-bean-jar", "咖啡豆罐", prefab,
                    new[] { "visit" }, true, new Vector2(.12f, .12f), .194f,
                    focusMaterial, ItemCategory.Kitchenware);
                definition.ConfigurePhysics(physics, true);
                definition.ConfigureInventory(2);
                definition.ConfigurePlacement(false, false);
                definition.ClearBreakable();
                definition.ClearImpactSound();
                definition.ConfigurePlacementParts();
                EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssets();
                return definition;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static CoffeeBean BuildBeanPrefab(GameObject source)
        {
            var root = new GameObject("Coffee Bean");
            root.SetActive(false);
            try
            {
                var view = (GameObject)PrefabUtility.InstantiatePrefab(source);
                view.name = "Model";
                view.transform.SetParent(root.transform, false);
                var bounds = BoundsOf(view);
                view.transform.position -= bounds.center;
                foreach (var renderer in view.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials
                        .Select(ConvertMaterial).ToArray();
                var shape = root.AddComponent<BoxCollider>();
                shape.size = new Vector3(
                    Mathf.Max(.004f, bounds.size.x * .92f),
                    Mathf.Max(.003f, bounds.size.y * .88f),
                    Mathf.Max(.003f, bounds.size.z * .88f));
                shape.sharedMaterial = PhysicsContentBuilder.Food;
                var rigidBody = root.AddComponent<Rigidbody>();
                rigidBody.mass = .00016f;
                rigidBody.useGravity = false;
                rigidBody.isKinematic = true;
                rigidBody.interpolation = RigidbodyInterpolation.Interpolate;
                rigidBody.collisionDetectionMode = CollisionDetectionMode.Discrete;
                var bean = root.AddComponent<CoffeeBean>();
                bean.Configure();
                root.SetActive(true);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, BeanPrefabPath);
                return prefab.GetComponent<CoffeeBean>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void ConfigureImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException($"咖啡豆资产导入失败：{path}");
            if (!importer.importAnimation && !importer.importCameras &&
                !importer.importLights)
                return;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();
        }

        private static Material ConvertMaterial(Material source)
        {
            if (source == null) return null;
            Directory.CreateDirectory(MaterialsRoot);
            var path = MaterialsRoot + "/" + source.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("URP Lit shader is missing.");
            if (material == null)
            {
                material = new Material(shader) { name = source.name };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            var color = source.HasProperty("_BaseColor")
                ? source.GetColor("_BaseColor")
                : source.color;
            material.SetColor("_BaseColor", color);
            var baseMap = source.HasProperty("_BaseMap")
                ? source.GetTexture("_BaseMap")
                : source.HasProperty("_MainTex")
                    ? source.GetTexture("_MainTex")
                    : null;
            if (baseMap != null) material.SetTexture("_BaseMap", baseMap);
            if (source.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", source.GetFloat("_Metallic"));
            if (source.HasProperty("_Glossiness"))
                material.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));
            if (source.name.Contains("Glass"))
                ConfigureTransparent(material, .18f);
            else ConfigureOpaque(material);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureTransparent(Material material, float alpha)
        {
            var color = material.GetColor("_BaseColor");
            color.a = alpha;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetFloat("_Smoothness", .92f);
        }

        private static void ConfigureOpaque(Material material)
        {
            material.SetFloat("_Surface", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.SetFloat("_ZWrite", 1);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = -1;
        }

        private static Transform Find(GameObject root, string name)
        {
            var result = root.GetComponentsInChildren<Transform>(true)
                .SingleOrDefault(value => value.name == name);
            if (result == null)
                throw new InvalidOperationException($"咖啡豆罐模型缺少节点：{name}");
            return result;
        }

        private static Renderer FindRenderer(GameObject root, string name)
        {
            var result = root.GetComponentsInChildren<Renderer>(true)
                .SingleOrDefault(value => value.name == name);
            if (result == null)
                throw new InvalidOperationException(
                    $"咖啡豆罐模型缺少渲染节点：{name}");
            return result;
        }

        private static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException("咖啡豆资产没有渲染网格。");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
    }
}
