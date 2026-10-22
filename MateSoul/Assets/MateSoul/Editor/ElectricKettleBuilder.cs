using System;
using System.IO;
using System.Linq;
using MateSoul.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public static class ElectricKettleBuilder
    {
        public const string KettlePrefabPath =
            "Assets/MateSoul/Content/Props/ElectricKettleBody.prefab";
        public const string BasePrefabPath =
            "Assets/MateSoul/Content/Props/ElectricKettleBase.prefab";
        public const string KettleDefinitionPath =
            "Assets/MateSoul/Content/Props/ElectricKettleBody.asset";
        public const string BaseDefinitionPath =
            "Assets/MateSoul/Content/Props/ElectricKettleBase.asset";
        public const string GroupDefinitionPath =
            "Assets/MateSoul/Content/Props/ElectricKettle.asset";
        public const string KettleModelPath =
            "Assets/MateSoul/Content/PropsSource/ElectricKettle/" +
            "ElectricKettle_220x245x245mm.fbx";
        public const string BaseModelPath =
            "Assets/MateSoul/Content/PropsSource/ElectricKettle/" +
            "ElectricKettleBase_185x185x28mm.fbx";

        private const string MaterialsRoot =
            "Assets/MateSoul/Content/Props/Materials/ElectricKettle";
        private const string WaterMaterialsRoot =
            "Assets/MateSoul/Content/Props/Materials/Water";
        private const string SourceRoot =
            "Assets/MateSoul/Content/PropsSource/ElectricKettle";
        private const string BrushedBaseColorPath =
            SourceRoot + "/kettle_brushed_steel_basecolor.png";
        private const string BrushedNormalPath =
            SourceRoot + "/kettle_brushed_steel_normal.png";

        [MenuItem("MateSoul/Game/Build Electric Kettle Asset")]
        public static void BuildAsset()
        {
            var focus = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/MateSoul/Content/ItemFocusHighlight.mat");
            Build(focus);
        }

        public static ItemDefinition[] Build(Material focusMaterial)
        {
            if (focusMaterial == null)
                throw new InvalidOperationException(
                    "电热水壶缺少交互高亮材质。");
            ValidateGeneratedTextures();
            ConfigureTextureImporter(BrushedBaseColorPath, false);
            ConfigureTextureImporter(BrushedNormalPath, true);
            ConfigureImporter(KettleModelPath);
            ConfigureImporter(BaseModelPath);
            var kettleSource =
                AssetDatabase.LoadAssetAtPath<GameObject>(KettleModelPath);
            var baseSource =
                AssetDatabase.LoadAssetAtPath<GameObject>(BaseModelPath);
            if (kettleSource == null || baseSource == null)
                throw new InvalidOperationException(
                    "电热水壶或底座模型导入失败。");

            Directory.CreateDirectory(MaterialsRoot);
            var effects = CookingEffectsAssetBuilder.Build();
            var kettle = BuildKettle(kettleSource, focusMaterial, effects);
            var heatingBase = BuildBase(baseSource);
            var group = LoadOrCreateDefinition(GroupDefinitionPath);
            group.Configure(
                "electric-kettle", "电热水壶", kettle.Prefab,
                new[] { "visit" }, true, new Vector2(.32f, .21f),
                .285f, focusMaterial, ItemCategory.Appliance);
            group.ConfigurePhysics(kettle.Physics, true);
            group.ConfigureInventory(1);
            group.ConfigurePlacement(false);
            group.ClearBreakable();
            group.ClearImpactSound();
            group.ClearCooking();
            group.ConfigurePlacementParts(
                new ItemPlacementPart(
                    heatingBase, Vector3.zero, Quaternion.identity),
                new ItemPlacementPart(
                    kettle, Vector3.up * .034f, Quaternion.identity));
            EditorUtility.SetDirty(group);
            AssetDatabase.SaveAssets();
            return new[] { kettle, heatingBase, group };
        }

        private static ItemDefinition BuildKettle(GameObject source,
            Material focusMaterial, CookingEffectAssets effects)
        {
            var root = new GameObject("电热水壶");
            root.SetActive(false);
            try
            {
                var view = InstantiateModel(source, root.transform);
                var bounds = BoundsOf(view);
                view.transform.position += new Vector3(
                    -bounds.center.x, -bounds.min.y, -bounds.center.z);
                foreach (var renderer in view.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials
                        .Select(ConvertMaterial).ToArray();

                var bodyRenderer = FindRenderer(view, "Kettle_Body");
                var spoutRenderer = FindRenderer(view, "Kettle_Spout");
                var spoutOpening =
                    FindRenderer(view, "Kettle_SpoutOpening");
                FindRenderer(view, "Kettle_Handle");
                var switchRenderer = FindRenderer(view, "Kettle_Switch_Cap");
                var switchPivot = Find(view, "Kettle_Switch_Pivot");
                var contactPoint = Find(view, "Kettle_BaseContactPoint");
                var steamOrigin = Find(view, "Kettle_SteamOrigin");

                AddCapsule(root, bodyRenderer.bounds, .098f);
                AddBox(root, spoutRenderer.bounds);
                AddBox(root, new Vector3(.112f, .203f, 0),
                    new Vector3(.080f, .038f, .045f));
                AddBox(root, new Vector3(.149f, .125f, 0),
                    new Vector3(.036f, .125f, .045f));
                AddBox(root, new Vector3(.112f, .047f, 0),
                    new Vector3(.075f, .040f, .045f));
                var physics = new BodySettings(
                    1.25f, PhysicsContentBuilder.Metal, .028f, .07f);
                PhysicsContentBuilder.ConfigureBody(root, physics);

                var waterVisual = GameObject.CreatePrimitive(
                    PrimitiveType.Cylinder);
                waterVisual.name = "Contained Water";
                waterVisual.transform.SetParent(root.transform, false);
                waterVisual.transform.localPosition =
                    new Vector3(0, .038f, 0);
                waterVisual.transform.localScale =
                    new Vector3(.154f, .001f, .154f);
                Object.DestroyImmediate(waterVisual.GetComponent<Collider>());
                waterVisual.GetComponent<Renderer>().sharedMaterial =
                    BuildWaterMaterial();
                waterVisual.SetActive(false);
                var water = root.AddComponent<WaterReceiver>();
                water.Configure(
                    1.7f, .071f, new Vector3(0, .238f, 0),
                    new Vector3(0, .038f, 0), .155f,
                    waterVisual.transform);

                var steam = BuildSteam(steamOrigin, effects.SmokeMaterial);
                var controller = root.AddComponent<ElectricKettleController>();
                var buttonAudio = root.AddComponent<AudioSource>();
                var operatingAudio = root.AddComponent<AudioSource>();
                controller.Configure(
                    water, contactPoint, steam, effects.ButtonBeep,
                    effects.InductionHum, buttonAudio, operatingAudio);
                var pourOutlet = new GameObject(
                    "Kettle Pour Origin").transform;
                pourOutlet.SetParent(root.transform, false);
                pourOutlet.position = spoutOpening.bounds.center;
                var pourStream = BuildPourStream(
                    root.transform, pourOutlet.position);
                root.AddComponent<LiquidPourer>().Configure(
                    water, pourOutlet, pourStream);
                BuildSwitch(root, controller, switchPivot, switchRenderer,
                    focusMaterial);

                root.SetActive(true);
                var prefab =
                    PrefabUtility.SaveAsPrefabAsset(root, KettlePrefabPath);
                var definition =
                    LoadOrCreateDefinition(KettleDefinitionPath);
                definition.Configure(
                    "electric-kettle-body", "电热水壶", prefab,
                    new[] { "visit" }, false, new Vector2(.32f, .21f),
                    .25f, focusMaterial, ItemCategory.Appliance);
                definition.ConfigurePhysics(physics, true);
                definition.ConfigureInventory(0);
                definition.ConfigurePlacement(false);
                definition.ClearBreakable();
                definition.ClearImpactSound();
                definition.ClearCooking();
                definition.ConfigurePlacementParts();
                EditorUtility.SetDirty(definition);
                return definition;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static ItemDefinition BuildBase(GameObject source)
        {
            var root = new GameObject("电热水壶底座");
            root.SetActive(false);
            try
            {
                var view = InstantiateModel(source, root.transform);
                var bounds = BoundsOf(view);
                view.transform.position += new Vector3(
                    -bounds.center.x, -bounds.min.y, -bounds.center.z);
                foreach (var renderer in view.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials
                        .Select(ConvertMaterial).ToArray();
                bounds = BoundsOf(view);

                var collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(0, .014f, 0);
                collider.size = new Vector3(.185f, .028f, .185f);
                collider.material = PhysicsContentBuilder.Plastic;
                var physics = new BodySettings(
                    .45f, PhysicsContentBuilder.Plastic, .02f, .04f);
                PhysicsContentBuilder.ConfigureBody(root, physics);
                var baseContact = Find(view, "KettleBase_ContactPoint");
                var heatingBase = root.AddComponent<ElectricKettleBase>();
                heatingBase.Configure(baseContact);

                root.SetActive(true);
                var prefab =
                    PrefabUtility.SaveAsPrefabAsset(root, BasePrefabPath);
                var definition =
                    LoadOrCreateDefinition(BaseDefinitionPath);
                definition.Configure(
                    "electric-kettle-base", "电热水壶底座", prefab,
                    new[] { "visit" }, false, new Vector2(.185f, .185f),
                    Mathf.Max(.028f, bounds.size.y), null,
                    ItemCategory.Appliance);
                definition.ConfigurePhysics(physics, true);
                definition.ConfigureInventory(0);
                definition.ConfigurePlacement(true);
                definition.ClearBreakable();
                definition.ClearImpactSound();
                definition.ClearCooking();
                definition.ConfigurePlacementParts();
                EditorUtility.SetDirty(definition);
                return definition;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject InstantiateModel(
            GameObject source, Transform parent)
        {
            var view = (GameObject)PrefabUtility.InstantiatePrefab(source);
            view.name = "Model";
            view.transform.SetParent(parent, false);
            PrefabUtility.UnpackPrefabInstance(
                view, PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            return view;
        }

        private static void BuildSwitch(GameObject root,
            ElectricKettleController controller, Transform pivot,
            Renderer visual, Material focusMaterial)
        {
            var rect = new GameObject(
                "Kettle Power Switch", typeof(RectTransform))
                .GetComponent<RectTransform>();
            rect.SetParent(root.transform, false);
            rect.position = visual.bounds.center + Vector3.up * .010f;
            rect.localRotation = Quaternion.Euler(-90, 0, 0);
            rect.localScale = Vector3.one * .001f;
            rect.sizeDelta = new Vector2(54, 48);
            rect.gameObject.AddComponent<Canvas>().renderMode =
                RenderMode.WorldSpace;
            rect.gameObject.AddComponent<GraphicRaycaster>().blockingObjects =
                GraphicRaycaster.BlockingObjects.None;
            var tracked =
                rect.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            tracked.checkFor3DOcclusion = true;
            var hit = rect.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var button =
                rect.gameObject.AddComponent<ElectricKettlePowerButton>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            button.navigation =
                new Navigation { mode = Navigation.Mode.None };
            var pressedOffset =
                pivot.parent.InverseTransformVector(Vector3.down * .006f);
            button.Configure(controller, pivot, pressedOffset);
            var focus =
                rect.gameObject.AddComponent<ItemInteractionFocusTarget>();
            focus.Configure(button, focusMaterial, visual);
        }

        private static ParticleSystem BuildSteam(
            Transform origin, Material smokeMaterial)
        {
            var steamObject = new GameObject("Kettle Steam");
            steamObject.transform.SetParent(origin, false);
            steamObject.transform.localRotation =
                Quaternion.Euler(-68f, 0, 0);
            var steam = steamObject.AddComponent<ParticleSystem>();
            var main = steam.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.035f, .075f);
            main.startSize = new ParticleSystem.MinMaxCurve(.018f, .042f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 1f, 1f, .32f),
                new Color(1f, 1f, 1f, .68f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 48;
            var emission = steam.emission;
            emission.rateOverTime = 18f;
            var shape = steam.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = .005f;
            var noise = steam.noise;
            noise.enabled = true;
            noise.strength = .18f;
            noise.frequency = .45f;
            noise.scrollSpeed = .12f;
            var color = steam.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0),
                    new GradientColorKey(new Color(.91f, .94f, 1f), 1)
                },
                new[]
                {
                    new GradientAlphaKey(0, 0),
                    new GradientAlphaKey(.58f, .16f),
                    new GradientAlphaKey(0, 1)
                });
            color.color = gradient;
            var renderer =
                steamObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = smokeMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return steam;
        }

        private static Renderer BuildPourStream(
            Transform parent, Vector3 position)
        {
            var stream = GameObject.CreatePrimitive(
                PrimitiveType.Cylinder);
            stream.name = "Kettle Water Stream";
            stream.transform.SetParent(parent, false);
            stream.transform.position = position;
            stream.transform.localScale =
                new Vector3(.006f, .01f, .006f);
            Object.DestroyImmediate(stream.GetComponent<Collider>());
            var renderer = stream.GetComponent<Renderer>();
            renderer.sharedMaterial = BuildWaterMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            stream.SetActive(false);
            return renderer;
        }

        private static void AddCapsule(
            GameObject root, Bounds bounds, float radius)
        {
            var collider = root.AddComponent<CapsuleCollider>();
            collider.center =
                root.transform.InverseTransformPoint(bounds.center);
            collider.direction = 1;
            collider.radius = radius;
            collider.height = Mathf.Max(radius * 2f, bounds.size.y);
            collider.material = PhysicsContentBuilder.Metal;
        }

        private static void AddBox(GameObject root, Bounds bounds)
        {
            var collider = root.AddComponent<BoxCollider>();
            collider.center =
                root.transform.InverseTransformPoint(bounds.center);
            collider.size = bounds.size;
            collider.material = PhysicsContentBuilder.Metal;
        }

        private static void AddBox(
            GameObject root, Vector3 center, Vector3 size)
        {
            var collider = root.AddComponent<BoxCollider>();
            collider.center = center;
            collider.size = size;
            collider.material = PhysicsContentBuilder.Metal;
        }

        private static ItemDefinition LoadOrCreateDefinition(string path)
        {
            var definition =
                AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (definition != null) return definition;
            definition = ScriptableObject.CreateInstance<ItemDefinition>();
            AssetDatabase.CreateAsset(definition, path);
            return definition;
        }

        private static void ConfigureImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException(
                    $"电热水壶模型导入失败：{path}");
            if (!importer.importAnimation && !importer.importCameras &&
                !importer.importLights)
                return;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();
        }

        private static void ConfigureTextureImporter(
            string path, bool normalMap)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException(
                    $"电热水壶贴图导入失败：{path}");
            var expectedType = normalMap
                ? TextureImporterType.NormalMap
                : TextureImporterType.Default;
            if (importer.textureType == expectedType &&
                importer.sRGBTexture == !normalMap &&
                importer.wrapMode == TextureWrapMode.Repeat)
                return;
            importer.textureType = expectedType;
            importer.sRGBTexture = !normalMap;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }

        private static void ValidateGeneratedTextures()
        {
            var basePixels = LoadPngPixels(BrushedBaseColorPath);
            var averageLuminance = basePixels.Average(pixel =>
                (pixel.r + pixel.g + pixel.b) / (3f * byte.MaxValue));
            if (averageLuminance < .35f)
                throw new InvalidOperationException(
                    "电热水壶拉丝金属底色贴图异常：平均亮度过低。");

            var normalPixels = LoadPngPixels(BrushedNormalPath);
            var averageBlue = normalPixels.Average(
                pixel => pixel.b / (float)byte.MaxValue);
            if (averageBlue < .75f)
                throw new InvalidOperationException(
                    "电热水壶拉丝金属法线贴图异常：法线蓝通道无效。");
        }

        private static Color32[] LoadPngPixels(string assetPath)
        {
            var texture = new Texture2D(
                2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                var bytes = File.ReadAllBytes(Path.GetFullPath(assetPath));
                if (!ImageConversion.LoadImage(texture, bytes, false))
                    throw new InvalidOperationException(
                        $"电热水壶贴图无法解码：{assetPath}");
                return texture.GetPixels32();
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private static Material ConvertMaterial(Material source)
        {
            if (source == null) return null;
            Directory.CreateDirectory(MaterialsRoot);
            var path = MaterialsRoot + "/" + source.name + ".mat";
            var material =
                AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("URP Lit shader is missing.");
            if (material == null)
            {
                material = new Material(shader) { name = source.name };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            if (source.name.Contains("BrushedStainless"))
            {
                material.SetColor("_BaseColor", Color.white);
                material.SetTexture("_BaseMap",
                    AssetDatabase.LoadAssetAtPath<Texture2D>(
                        BrushedBaseColorPath));
                material.SetTexture("_BumpMap",
                    AssetDatabase.LoadAssetAtPath<Texture2D>(
                        BrushedNormalPath));
                material.SetFloat("_BumpScale", .42f);
                material.SetFloat("_Metallic", .94f);
                material.SetFloat("_Smoothness", .72f);
                material.EnableKeyword("_NORMALMAP");
            }
            else
            {
                material.SetTexture("_BaseMap", null);
                material.SetTexture("_BumpMap", null);
                material.DisableKeyword("_NORMALMAP");
                if (source.name.Contains("DarkPolymer"))
                {
                    material.SetColor(
                        "_BaseColor", new Color(.018f, .015f, .024f, 1));
                    material.SetFloat("_Metallic", 0);
                    material.SetFloat("_Smoothness", .36f);
                }
                else if (source.name.Contains("RedSwitch"))
                {
                    material.SetColor(
                        "_BaseColor", new Color(.64f, .035f, .022f, 1));
                    material.SetFloat("_Metallic", .12f);
                    material.SetFloat("_Smoothness", .66f);
                }
                else if (source.name.Contains("BlueIndicator"))
                {
                    material.SetColor(
                        "_BaseColor", new Color(.02f, .14f, .9f, 1));
                    material.SetFloat("_Metallic", .08f);
                    material.SetFloat("_Smoothness", .72f);
                    material.SetColor(
                        "_EmissionColor", new Color(.01f, .08f, 1f) * 1.5f);
                    material.EnableKeyword("_EMISSION");
                }
                else
                {
                    var color = source.HasProperty("_BaseColor")
                        ? source.GetColor("_BaseColor")
                        : source.color;
                    material.SetColor("_BaseColor", color);
                    material.SetFloat("_Metallic", .35f);
                    material.SetFloat("_Smoothness", .76f);
                }
            }
            material.SetFloat("_Surface", 0);
            material.SetFloat("_ZWrite", 1);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildWaterMaterial()
        {
            Directory.CreateDirectory(WaterMaterialsRoot);
            var path = WaterMaterialsRoot + "/MAT_KettleWater.mat";
            var material =
                AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("URP Lit shader is missing.");
            if (material == null)
            {
                material = new Material(shader) { name = "MAT_KettleWater" };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            material.SetColor(
                "_BaseColor", new Color(.22f, .62f, .96f, .48f));
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_Smoothness", .84f);
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat(
                "_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat(
                "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Transform Find(GameObject root, string name)
        {
            var result = root.GetComponentsInChildren<Transform>(true)
                .SingleOrDefault(value => value.name == name);
            if (result == null)
                throw new InvalidOperationException(
                    $"电热水壶模型缺少节点：{name}");
            return result;
        }

        private static Renderer FindRenderer(GameObject root, string name)
        {
            var result = root.GetComponentsInChildren<Renderer>(true)
                .SingleOrDefault(value => value.name == name);
            if (result == null)
                throw new InvalidOperationException(
                    $"电热水壶模型缺少渲染节点：{name}");
            return result;
        }

        private static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException(
                    "电热水壶模型没有渲染网格。");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
    }
}
