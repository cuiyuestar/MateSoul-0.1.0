using System;
using System.IO;
using System.Linq;
using MateSoul.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public static class WaterDispenserBuilder
    {
        public const string PrefabPath =
            "Assets/MateSoul/Content/Props/WaterDispenser.prefab";
        public const string DefinitionPath =
            "Assets/MateSoul/Content/Props/WaterDispenser.asset";
        public const string ModelPath =
            "Assets/MateSoul/Content/PropsSource/WaterDispenser/" +
            "CreamFloorWaterDispenser_340x372x1400mm.fbx";

        private const string SourceRoot =
            "Assets/MateSoul/Content/PropsSource/WaterDispenser";
        private const string MaterialsRoot =
            "Assets/MateSoul/Content/Props/Materials/WaterDispenser";
        private const string WaterMaterialsRoot =
            "Assets/MateSoul/Content/Props/Materials/Water";

        private readonly struct ReceiverSpec
        {
            public readonly string Id;
            public readonly float Capacity;
            public readonly float OpeningRadius;
            public readonly Vector3 OpeningCenter;
            public readonly Vector3 FillBase;
            public readonly float FillHeight;
            public readonly float VisualRadius;

            public ReceiverSpec(string id, float capacity, float openingRadius,
                Vector3 openingCenter, Vector3 fillBase, float fillHeight,
                float visualRadius)
            {
                Id = id;
                Capacity = capacity;
                OpeningRadius = openingRadius;
                OpeningCenter = openingCenter;
                FillBase = fillBase;
                FillHeight = fillHeight;
                VisualRadius = visualRadius;
            }
        }

        [MenuItem("MateSoul/Game/Build Water Dispenser Asset")]
        public static void BuildAsset()
        {
            var focus = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/MateSoul/Content/ItemFocusHighlight.mat");
            Build(focus);
        }

        public static ItemDefinition Build(Material focusMaterial)
        {
            if (focusMaterial == null)
                throw new InvalidOperationException("饮水机交互部件光圈材质缺失。");
            ConfigureModelImporter();
            ConfigureNormalMap(SourceRoot + "/dispenser_plastic_normal.png");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (source == null)
                throw new InvalidOperationException($"饮水机模型导入失败：{ModelPath}");

            Directory.CreateDirectory(MaterialsRoot);
            var root = new GameObject("饮水机");
            root.SetActive(false);
            try
            {
                var view = (GameObject)PrefabUtility.InstantiatePrefab(source);
                view.name = "Model";
                view.transform.SetParent(root.transform, false);
                var bounds = BoundsOf(view);
                var expected = new Vector3(.34f, 1.4f, .372f);
                var tolerance = new Vector3(.008f, .010f, .008f);
                if (Mathf.Abs(bounds.size.x - expected.x) > tolerance.x ||
                    Mathf.Abs(bounds.size.y - expected.y) > tolerance.y ||
                    Mathf.Abs(bounds.size.z - expected.z) > tolerance.z)
                    throw new InvalidOperationException(
                        $"饮水机导入尺寸错误：{bounds.size}，期望 {expected}。");
                view.transform.position += new Vector3(
                    -bounds.center.x, -bounds.min.y, -bounds.center.z);
                bounds = BoundsOf(view);

                foreach (var renderer in view.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials
                        .Select(ConvertMaterial).ToArray();

                PhysicsContentBuilder.Box(root, "Lower Cabinet Collider",
                    new Vector3(0, .305f, 0), new Vector3(.34f, .57f, .372f));
                PhysicsContentBuilder.Box(root, "Left Upper Collider",
                    new Vector3(-.1425f, .795f, 0), new Vector3(.055f, .41f, .372f));
                PhysicsContentBuilder.Box(root, "Right Upper Collider",
                    new Vector3(.1425f, .795f, 0), new Vector3(.055f, .41f, .372f));
                PhysicsContentBuilder.Box(root, "Top Bridge Collider",
                    new Vector3(0, .9625f, 0), new Vector3(.34f, .105f, .372f));
                var physics = new BodySettings(18f, PhysicsContentBuilder.Plastic);
                PhysicsContentBuilder.ConfigureBody(root, physics);
                var obstacle = root.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = new Vector3(0, .7f, 0);
                obstacle.size = expected;
                obstacle.carving = true;

                var outlet = Find(view, "WaterDispenser_Nozzle_Outlet");
                var buttonPivot = Find(view, "WaterDispenser_Button_Pivot");
                var buttonVisual = FindRenderer(view, "WaterDispenser_DropButton");
                var trayRenderer = FindRenderer(view, "WaterDispenser_DripTrayBase");
                var trayTarget = new GameObject("WaterDispenser_DripTray_Target").transform;
                trayTarget.SetParent(root.transform, false);
                var trayPoint = trayRenderer.bounds.center;
                trayPoint.x = outlet.position.x;
                trayPoint.y = trayRenderer.bounds.max.y + .002f;
                trayPoint.z = outlet.position.z;
                trayTarget.position = trayPoint;

                var waterMaterial = BuildWaterMaterial(
                    "MAT_RunningWater", new Color(.26f, .68f, 1f, .72f), .74f);
                var streamRenderer = CreateWaterCylinder(
                    "Water Stream", root.transform, waterMaterial, .009f, .1f);
                var puddles = new Renderer[6];
                for (var index = 0; index < puddles.Length; index++)
                {
                    puddles[index] = CreateWaterCylinder(
                        $"Pooled Water Spot {index + 1:00}", root.transform,
                        waterMaterial, .075f, .0012f);
                    puddles[index].gameObject.SetActive(false);
                }
                streamRenderer.gameObject.SetActive(false);

                var controller = root.AddComponent<WaterDispenserController>();
                controller.Configure(outlet, trayTarget, streamRenderer.transform,
                    streamRenderer, puddles);

                var buttonFront = buttonVisual.bounds.center.z < bounds.center.z
                    ? bounds.min.z - .008f
                    : bounds.max.z + .008f;
                var negativeZFace = buttonFront < bounds.center.z;
                var controlPosition = root.transform.InverseTransformPoint(
                    buttonVisual.bounds.center);
                controlPosition.z = root.transform.InverseTransformPoint(
                    new Vector3(buttonPivot.position.x, buttonPivot.position.y, buttonFront)).z;
                var buttonArea = MakeControl("Water Button", root.transform,
                    controlPosition, new Vector2(78, 52),
                    negativeZFace);
                var button = buttonArea.gameObject.AddComponent<WaterDispenserButton>();
                ConfigureSelectable(button, buttonArea.GetComponent<Image>());
                var inwardWorld = Vector3.forward * (negativeZFace ? .005f : -.005f);
                var inwardLocal = buttonPivot.parent.InverseTransformVector(inwardWorld);
                button.Configure(controller, buttonPivot, inwardLocal);
                var focus = buttonArea.gameObject.AddComponent<ItemInteractionFocusTarget>();
                focus.Configure(button, focusMaterial, buttonVisual);

                root.SetActive(true);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(DefinitionPath);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ItemDefinition>();
                    AssetDatabase.CreateAsset(definition, DefinitionPath);
                }
                definition.Configure("water-dispenser", "饮水机", prefab,
                    new[] { "visit" }, true, new Vector2(.34f, .372f), 1.4f,
                    focusMaterial, ItemCategory.Furniture);
                definition.ConfigurePhysics(physics);
                definition.ConfigureInventory(1);
                definition.ConfigurePlacement(true, true,
                    ItemPlacementSurface.Ground, true);
                definition.ConfigurePlacementFacing(negativeZFace ? 180f : 0f);
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

        public static void ConfigureReceivers(ItemDefinition[] catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            var waterMaterial = BuildWaterMaterial(
                "MAT_ContainedWater", new Color(.24f, .64f, .96f, .52f), .82f);
            var specs = new[]
            {
                new ReceiverSpec("mug", .32f, .034f,
                    new Vector3(-.0165f, .095f, 0),
                    new Vector3(-.0165f, .018f, 0), .065f, .032f),
                new ReceiverSpec("bowl", .55f, .055f,
                    new Vector3(0, .070f, 0),
                    new Vector3(0, .017f, 0), .039f, .052f),
                new ReceiverSpec("bowl-medium", 1.2f, .072f,
                    new Vector3(0, .091304f, 0),
                    new Vector3(0, .022f, 0), .052f, .068f),
                new ReceiverSpec("bowl-large", 2.1f, .088f,
                    new Vector3(0, .111594f, 0),
                    new Vector3(0, .027f, 0), .064f, .083f),
                new ReceiverSpec("plate", .08f, .073f,
                    new Vector3(0, .025f, 0),
                    new Vector3(0, .018f, 0), .0045f, .070f),
                new ReceiverSpec("plate-medium", .13f, .092f,
                    new Vector3(0, .03125f, 0),
                    new Vector3(0, .022f, 0), .0055f, .088f),
                new ReceiverSpec("plate-large", .20f, .111f,
                    new Vector3(0, .0375f, 0),
                    new Vector3(0, .026f, 0), .0065f, .106f)
            };
            foreach (var spec in specs)
            {
                var definition = catalog.SingleOrDefault(item => item.Id == spec.Id);
                if (definition == null)
                    throw new InvalidOperationException(
                        $"接水容器缺少物品定义：{spec.Id}");
                ConfigureReceiver(definition, spec, waterMaterial);
            }
        }

        private static void ConfigureReceiver(ItemDefinition definition,
            ReceiverSpec spec, Material waterMaterial)
        {
            var path = AssetDatabase.GetAssetPath(definition.Prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var existing = root.transform.Find("Contained Water");
                if (existing != null) Object.DestroyImmediate(existing.gameObject);
                var visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                visual.name = "Contained Water";
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = spec.FillBase;
                visual.transform.localScale = new Vector3(
                    spec.VisualRadius * 2f, .0004f, spec.VisualRadius * 2f);
                Object.DestroyImmediate(visual.GetComponent<Collider>());
                visual.GetComponent<Renderer>().sharedMaterial = waterMaterial;
                visual.SetActive(false);

                var receiver = root.GetComponent<WaterReceiver>() ??
                               root.AddComponent<WaterReceiver>();
                receiver.Configure(spec.Capacity, spec.OpeningRadius,
                    spec.OpeningCenter, spec.FillBase, spec.FillHeight,
                    visual.transform);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Renderer CreateWaterCylinder(string name, Transform parent,
            Material material, float diameter, float height)
        {
            var water = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            water.name = name;
            water.transform.SetParent(parent, false);
            water.transform.localScale = new Vector3(diameter, height * .5f, diameter);
            Object.DestroyImmediate(water.GetComponent<Collider>());
            var renderer = water.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private static RectTransform MakeControl(string name, Transform parent,
            Vector3 position, Vector2 size, bool negativeZFace)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localPosition = position;
            rect.localRotation = negativeZFace
                ? Quaternion.Euler(0, 180, 0)
                : Quaternion.identity;
            rect.localScale = Vector3.one * .001f;
            rect.sizeDelta = size;
            rect.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var mouse = rect.gameObject.AddComponent<GraphicRaycaster>();
            mouse.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            var tracked = rect.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            tracked.checkFor3DOcclusion = true;
            var hit = rect.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            return rect;
        }

        private static void ConfigureSelectable(Selectable selectable, Graphic graphic)
        {
            selectable.targetGraphic = graphic;
            selectable.transition = Selectable.Transition.None;
            selectable.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        private static void ConfigureModelImporter()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException($"饮水机模型导入失败：{ModelPath}");
            if (!importer.importAnimation && !importer.importCameras && !importer.importLights)
                return;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();
        }

        private static void ConfigureNormalMap(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException($"饮水机法线贴图导入失败：{path}");
            if (importer.textureType == TextureImporterType.NormalMap &&
                !importer.sRGBTexture)
                return;
            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false;
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
            if (source.HasProperty("_BumpMap"))
            {
                var normal = source.GetTexture("_BumpMap");
                if (normal != null)
                {
                    material.SetTexture("_BumpMap", normal);
                    material.EnableKeyword("_NORMALMAP");
                }
            }
            if (source.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", source.GetFloat("_Metallic"));
            if (source.HasProperty("_Glossiness"))
                material.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));
            if (source.name.Contains("BottlePET") ||
                source.name.Contains("BottleWater"))
                ConfigureTransparent(material, color.a > .05f ? color.a : .35f);
            else ConfigureOpaque(material);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildWaterMaterial(string name, Color color,
            float smoothness)
        {
            Directory.CreateDirectory(WaterMaterialsRoot);
            var path = WaterMaterialsRoot + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("URP Lit shader is missing.");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_Smoothness", smoothness);
            ConfigureTransparent(material, color.a);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureTransparent(Material material, float alpha)
        {
            var color = material.GetColor("_BaseColor");
            color.a = Mathf.Clamp(alpha, .05f, .9f);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
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
                throw new InvalidOperationException($"饮水机模型缺少节点：{name}");
            return result;
        }

        private static Renderer FindRenderer(GameObject root, string name)
        {
            var result = root.GetComponentsInChildren<Renderer>(true)
                .SingleOrDefault(value => value.name == name);
            if (result == null)
                throw new InvalidOperationException($"饮水机模型缺少渲染节点：{name}");
            return result;
        }

        private static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException("饮水机模型没有渲染网格。");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
    }
}
