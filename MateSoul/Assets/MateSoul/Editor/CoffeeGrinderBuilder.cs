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
    public static class CoffeeGrinderBuilder
    {
        public const string GroupDefinitionPath =
            "Assets/MateSoul/Content/Props/CoffeeGrinder.asset";
        public const string MortarDefinitionPath =
            "Assets/MateSoul/Content/Props/CoffeeGrinderMortar.asset";
        public const string PestleDefinitionPath =
            "Assets/MateSoul/Content/Props/CoffeeGrinderPestle.asset";
        public const string MortarPrefabPath =
            "Assets/MateSoul/Content/Props/CoffeeGrinderMortar.prefab";
        public const string PestlePrefabPath =
            "Assets/MateSoul/Content/Props/CoffeeGrinderPestle.prefab";

        private const string SourceRoot =
            "Assets/MateSoul/Content/PropsSource/CoffeeGrinder/";
        private const string MortarModelPath =
            SourceRoot + "CoffeeGrinderMortar_180x140mm.fbx";
        private const string PestleModelPath =
            SourceRoot + "CoffeeGrinderPestle_255mm.fbx";
        private const string PowderModelPath =
            SourceRoot + "CoffeeGroundPowder_126mm.fbx";
        private const string MaterialsRoot =
            "Assets/MateSoul/Content/Props/Materials/CoffeeGrinder";

        [MenuItem("MateSoul/Game/Build Coffee Grinder Asset")]
        public static void BuildAsset()
        {
            var focus = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/MateSoul/Content/ItemFocusHighlight.mat");
            Build(focus);
        }

        public static ItemDefinition[] Build(Material focusMaterial)
        {
            if (focusMaterial == null)
                throw new InvalidOperationException("咖啡研磨套装交互光圈材质缺失。");
            foreach (var path in new[]
                     {
                         MortarModelPath, PestleModelPath, PowderModelPath
                     })
                ConfigureImporter(path);
            var mortarSource = LoadModel(MortarModelPath);
            var pestleSource = LoadModel(PestleModelPath);
            var powderSource = LoadModel(PowderModelPath);
            var beanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                CoffeeBeanJarBuilder.BeanPrefabPath);
            var beanRenderer = beanPrefab?.GetComponentInChildren<MeshRenderer>(true);
            var beanMesh = beanRenderer?.GetComponent<MeshFilter>()?.sharedMesh;
            if (beanRenderer == null || beanMesh == null)
                throw new InvalidOperationException(
                    "咖啡研磨容器依赖的独立咖啡豆 Prefab 尚未生成。");
            Directory.CreateDirectory(MaterialsRoot);

            var mortar = BuildMortar(
                mortarSource, powderSource, beanMesh,
                beanRenderer.sharedMaterial, focusMaterial);
            var pestle = BuildPestle(pestleSource, focusMaterial);
            var group = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                GroupDefinitionPath);
            if (group == null)
            {
                group = ScriptableObject.CreateInstance<ItemDefinition>();
                AssetDatabase.CreateAsset(group, GroupDefinitionPath);
            }
            group.Configure("coffee-grinder", "咖啡研磨套装", mortar.Prefab,
                new[] { "visit" }, true, new Vector2(.33f, .18f), .255f,
                focusMaterial, ItemCategory.Kitchenware);
            group.ConfigurePhysics(mortar.Physics, true);
            group.ConfigureInventory(1);
            group.ConfigurePlacement(false);
            group.ConfigurePlacementParts(
                new ItemPlacementPart(mortar, Vector3.zero, Quaternion.identity),
                new ItemPlacementPart(pestle, new Vector3(.135f, 0, 0),
                    Quaternion.identity));
            group.ClearBreakable();
            group.ClearImpactSound();
            EditorUtility.SetDirty(group);
            AssetDatabase.SaveAssets();
            return new[] { mortar, pestle, group };
        }

        public static void ConfigureBrewingReceivers(
            ItemDefinition[] catalog)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            var mug = catalog.SingleOrDefault(
                definition => definition.Id == "mug") ??
                      throw new InvalidOperationException(
                          "咖啡冲泡缺少马克杯定义。");
            var path = AssetDatabase.GetAssetPath(mug.Prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var water = root.GetComponent<WaterReceiver>() ??
                            throw new InvalidOperationException(
                                "马克杯必须先配置接水能力。");
                var receiver = root.GetComponent<CoffeeBrewReceiver>() ??
                               root.AddComponent<CoffeeBrewReceiver>();
                receiver.Configure(water);
                var existingSensor =
                    root.transform.Find("Coffee Ingredient Receiver");
                if (existingSensor != null)
                    Object.DestroyImmediate(existingSensor.gameObject);
                var sensorObject = new GameObject(
                    "Coffee Ingredient Receiver");
                sensorObject.transform.SetParent(root.transform, false);
                sensorObject.transform.localPosition =
                    water.OpeningCenter + Vector3.down * .012f;
                var sensorShape =
                    sensorObject.AddComponent<SphereCollider>();
                sensorShape.radius = water.OpeningRadius * .95f;
                sensorShape.isTrigger = true;
                sensorObject.AddComponent<CoffeeBrewIngredientSensor>()
                    .Configure(receiver);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static ItemDefinition BuildMortar(GameObject source,
            GameObject powderSource, Mesh beanMesh, Material beanMaterial,
            Material focusMaterial)
        {
            var root = new GameObject("咖啡研磨容器");
            root.SetActive(false);
            try
            {
                var view = InstantiateModel(source, root.transform, "Model",
                    new Vector3(.18f, .14f, .18f));
                var powder = InstantiateModel(powderSource, root.transform,
                    "Coffee Powder Visual", new Vector3(.126f, .028f, .126f),
                    alignToGround: false);
                powder.SetActive(false);

                AddMortarColliders(root);
                var physics = new BodySettings(
                    1.65f, PhysicsContentBuilder.Ceramic, .025f, .055f);
                PhysicsContentBuilder.ConfigureBody(root, physics);

                var center = Find(view, "CoffeeGrinder_InteriorCenter");
                var beanObject = new GameObject("Captured Coffee Beans");
                beanObject.transform.SetParent(root.transform, false);
                var beanVolume =
                    beanObject.AddComponent<CoffeeBeanVolumeRenderer>();
                beanVolume.Configure(beanMesh, beanMaterial, 336,
                    .058f, .064f, .050f, 20261018, false);
                beanVolume.SetVisibleCount(0);

                var particles = BuildPowderParticles(
                    root.transform, center.position,
                    powder.GetComponentInChildren<Renderer>(true)?.sharedMaterial);
                var controller = root.AddComponent<CoffeeGrinderController>();
                controller.Configure(beanVolume, powder, particles, center,
                    4f, .035f, .061f, .078f);
                var outlet = new GameObject(
                    "Coffee Powder Pour Origin").transform;
                outlet.SetParent(root.transform, false);
                outlet.position = center.position +
                                  root.transform.right * .064f +
                                  root.transform.up * .055f;
                var powderStream = BuildPowderPourStream(
                    root.transform, outlet.position,
                    powder.GetComponentInChildren<Renderer>(true)
                        ?.sharedMaterial);
                root.AddComponent<CoffeePowderPourer>().Configure(
                    controller, outlet, powderStream);

                var receiverObject = new GameObject("Coffee Bean Receiver");
                receiverObject.transform.SetParent(root.transform, false);
                receiverObject.transform.position = center.position;
                var receiverShape = receiverObject.AddComponent<BoxCollider>();
                receiverShape.center = Vector3.up * .039f;
                receiverShape.size = new Vector3(.122f, .078f, .122f);
                receiverShape.isTrigger = true;
                receiverObject.AddComponent<CoffeeGrinderReceiver>()
                    .Configure(controller);

                root.SetActive(true);
                var prefab = PrefabUtility.SaveAsPrefabAsset(
                    root, MortarPrefabPath);
                return ConfigureDefinition(
                    MortarDefinitionPath, "coffee-grinder-mortar",
                    "咖啡研磨容器", prefab, new Vector2(.18f, .18f), .14f,
                    physics, focusMaterial);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static ItemDefinition BuildPestle(GameObject source,
            Material focusMaterial)
        {
            var root = new GameObject("咖啡研磨棒");
            root.SetActive(false);
            try
            {
                var view = InstantiateModel(source, root.transform, "Model",
                    new Vector3(.052f, .255f, .052f));
                var collider = root.AddComponent<CapsuleCollider>();
                collider.center = Vector3.up * .1275f;
                collider.radius = .026f;
                collider.height = .255f;
                collider.direction = 1;
                var physics = new BodySettings(
                    .34f, PhysicsContentBuilder.Wood, .018f, .045f);
                PhysicsContentBuilder.ConfigureBody(root, physics);
                var tip = Find(view, "CoffeeGrinderPestle_Tip");
                root.AddComponent<CoffeePestle>().Configure(tip);
                root.SetActive(true);
                var prefab = PrefabUtility.SaveAsPrefabAsset(
                    root, PestlePrefabPath);
                return ConfigureDefinition(
                    PestleDefinitionPath, "coffee-grinder-pestle",
                    "咖啡研磨棒", prefab, new Vector2(.052f, .052f), .255f,
                    physics, focusMaterial);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static ItemDefinition ConfigureDefinition(string path,
            string id, string title, GameObject prefab, Vector2 footprint,
            float height, BodySettings physics, Material focusMaterial)
        {
            var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<ItemDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }
            definition.Configure(id, title, prefab, new[] { "visit" }, false,
                footprint, height, focusMaterial, ItemCategory.Kitchenware);
            definition.ConfigurePhysics(physics, true);
            definition.ConfigureInventory(0);
            definition.ConfigurePlacement(false);
            definition.ConfigurePlacementParts();
            definition.ClearBreakable();
            definition.ClearImpactSound();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static GameObject InstantiateModel(GameObject source,
            Transform parent, string name, Vector3 expected,
            bool alignToGround = true)
        {
            var view = (GameObject)PrefabUtility.InstantiatePrefab(source);
            view.name = name;
            view.transform.SetParent(parent, false);
            var renderers = view.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException($"{source.name}没有渲染网格。");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var tolerance = Vector3.Max(expected * .09f, Vector3.one * .006f);
            if (Mathf.Abs(bounds.size.x - expected.x) > tolerance.x ||
                Mathf.Abs(bounds.size.y - expected.y) > tolerance.y ||
                Mathf.Abs(bounds.size.z - expected.z) > tolerance.z)
                throw new InvalidOperationException(
                    $"{source.name}导入尺寸错误：{bounds.size}，期望 {expected}。");
            var offsetY = alignToGround ? -bounds.min.y : 0;
            view.transform.position += new Vector3(
                -bounds.center.x, offsetY, -bounds.center.z);
            foreach (var renderer in renderers)
                renderer.sharedMaterials = renderer.sharedMaterials
                    .Select(ConvertMaterial).ToArray();
            return view;
        }

        private static void AddMortarColliders(GameObject root)
        {
            var baseShape = root.AddComponent<BoxCollider>();
            baseShape.center = Vector3.up * .018f;
            baseShape.size = new Vector3(.17f, .036f, .17f);
            const int segments = 12;
            for (var index = 0; index < segments; index++)
            {
                var angle = index / (float)segments * Mathf.PI * 2;
                var wall = new GameObject($"Mortar Wall {index:00}");
                wall.transform.SetParent(root.transform, false);
                wall.transform.localPosition = new Vector3(
                    Mathf.Cos(angle) * .081f, .086f,
                    Mathf.Sin(angle) * .081f);
                wall.transform.localRotation =
                    Quaternion.Euler(0, -angle * Mathf.Rad2Deg, 0);
                var shape = wall.AddComponent<BoxCollider>();
                shape.size = new Vector3(.044f, .108f, .014f);
            }
        }

        private static ParticleSystem BuildPowderParticles(Transform parent,
            Vector3 center, Material powderMaterial)
        {
            var root = new GameObject("Coffee Powder Dust");
            root.transform.SetParent(parent, false);
            root.transform.position = center + Vector3.up * .055f;
            var particles = root.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = .8f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.55f, .95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.018f, .055f);
            main.startSize = new ParticleSystem.MinMaxCurve(.0012f, .0032f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(.18f, .075f, .028f, .72f),
                new Color(.38f, .19f, .075f, .45f));
            main.gravityModifier = -.012f;
            main.maxParticles = 96;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = particles.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0, 72)
            });
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = .052f;
            shape.radiusThickness = 1;
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = .012f;
            noise.frequency = .75f;
            noise.scrollSpeed = .18f;
            var color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(.32f, .15f, .055f), 0),
                    new GradientColorKey(new Color(.11f, .045f, .018f), 1)
                },
                new[]
                {
                    new GradientAlphaKey(0, 0),
                    new GradientAlphaKey(.68f, .12f),
                    new GradientAlphaKey(.38f, .65f),
                    new GradientAlphaKey(0, 1)
                });
            color.color = gradient;
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.sharedMaterial = BuildParticleMaterial(powderMaterial);
            return particles;
        }

        private static ParticleSystem BuildPowderPourStream(
            Transform parent, Vector3 position, Material powderMaterial)
        {
            var root = new GameObject("Coffee Powder Stream");
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            root.transform.localRotation = Quaternion.Euler(0, 90f, 0);
            var particles = root.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.35f, .65f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.055f, .11f);
            main.startSize = new ParticleSystem.MinMaxCurve(.001f, .0024f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(.16f, .06f, .018f, .76f),
                new Color(.34f, .15f, .045f, .58f));
            main.gravityModifier = .32f;
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = particles.emission;
            emission.rateOverTime = 42f;
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = .004f;
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.sharedMaterial = BuildParticleMaterial(powderMaterial);
            return particles;
        }

        private static Material BuildParticleMaterial(Material powderMaterial)
        {
            var path = MaterialsRoot + "/MAT_CoffeePowder_Particles.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                throw new InvalidOperationException("URP Particles/Unlit shader is missing.");
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "MAT_CoffeePowder_Particles"
                };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            var color = powderMaterial != null &&
                        powderMaterial.HasProperty("_BaseColor")
                ? powderMaterial.GetColor("_BaseColor")
                : new Color(.16f, .07f, .025f, .65f);
            color.a = .62f;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject LoadModel(string path)
            => AssetDatabase.LoadAssetAtPath<GameObject>(path) ??
               throw new InvalidOperationException($"咖啡研磨资产导入失败：{path}");

        private static Transform Find(GameObject root, string name)
            => root.GetComponentsInChildren<Transform>(true)
                   .SingleOrDefault(value => value.name == name) ??
               throw new InvalidOperationException($"咖啡研磨模型缺少节点：{name}");

        private static void ConfigureImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException($"咖啡研磨模型导入失败：{path}");
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
            var map = source.HasProperty("_BaseMap")
                ? source.GetTexture("_BaseMap")
                : source.HasProperty("_MainTex")
                    ? source.GetTexture("_MainTex")
                    : null;
            if (map != null) material.SetTexture("_BaseMap", map);
            if (source.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", source.GetFloat("_Metallic"));
            if (source.HasProperty("_Glossiness"))
                material.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
