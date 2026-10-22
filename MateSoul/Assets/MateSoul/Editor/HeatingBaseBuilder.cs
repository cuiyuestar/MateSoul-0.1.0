using System;
using System.IO;
using System.Linq;
using MateSoul.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public static class HeatingBaseBuilder
    {
        public const string PrefabPath =
            "Assets/MateSoul/Content/Props/HeatingBase.prefab";
        public const string DefinitionPath =
            "Assets/MateSoul/Content/Props/HeatingBase.asset";
        public const string ModelPath =
            "Assets/MateSoul/Content/PropsSource/CookingSet/" +
            "ElectricHeatingBase_330x300x55mm.fbx";

        private const string MaterialsPath =
            "Assets/MateSoul/Content/Props/Materials/HeatingBase";
        private const string FocusMaterialPath =
            "Assets/MateSoul/Content/ItemFocusHighlight.mat";
        private const string FontPath =
            "Assets/MateSoul/Runtime/Resources/MateSoulUI/" +
            "NotoSansCJKsc-Dynamic-SDF.asset";
        private const string PowerIconPath =
            "Assets/MateSoul/Content/Props/HeatingBasePowerIcon.png";

        [MenuItem("MateSoul/Game/Build Heating Base Asset")]
        public static void BuildAsset() => Build();

        public static ItemDefinition Build(Material focusMaterial = null)
        {
            focusMaterial ??= AssetDatabase.LoadAssetAtPath<Material>(FocusMaterialPath);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (focusMaterial == null || font == null)
                throw new InvalidOperationException(
                    "加热底座缺少交互高亮材质或中文 TMP 字体。");
            var cookingEffects = CookingEffectsAssetBuilder.Build();
            var powerIcon = BuildPowerIcon();
            ConfigureModelImporter();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (source == null)
                throw new InvalidOperationException($"加热底座模型导入失败：{ModelPath}");

            Directory.CreateDirectory(MaterialsPath);
            var root = new GameObject("平底锅加热底座");
            root.SetActive(false);
            try
            {
                var view = (GameObject)PrefabUtility.InstantiatePrefab(source);
                view.name = "Model";
                view.transform.SetParent(root.transform, false);
                var bounds = BoundsOf(view);
                var expected = new Vector3(.330f, .06015f, .3135f);
                var tolerance = new Vector3(.005f, .004f, .005f);
                if (Mathf.Abs(bounds.size.x - expected.x) > tolerance.x ||
                    Mathf.Abs(bounds.size.y - expected.y) > tolerance.y ||
                    Mathf.Abs(bounds.size.z - expected.z) > tolerance.z)
                    throw new InvalidOperationException(
                        $"加热底座导入尺寸错误：{bounds.size}，期望 {expected}。");
                view.transform.position += new Vector3(
                    -bounds.center.x, -bounds.min.y, -bounds.center.z);
                bounds = BoundsOf(view);
                foreach (var renderer in view.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials
                        .Select(ConvertMaterial).ToArray();

                var bodyCollider = root.AddComponent<BoxCollider>();
                bodyCollider.center = new Vector3(0, .030f, 0);
                bodyCollider.size = new Vector3(.330f, .060f, .300f);
                bodyCollider.material = PhysicsContentBuilder.Metal;
                var physics = new BodySettings(2.4f, PhysicsContentBuilder.Metal);
                PhysicsContentBuilder.ConfigureBody(root, physics);

                var heatingSurfaceRenderer = FindRenderer(
                    view, "HeatingBase_HeatingSurface");
                var heatingSurface = new GameObject(
                    "Heating Surface Contact").transform;
                heatingSurface.SetParent(root.transform, false);
                heatingSurface.position = new Vector3(
                    heatingSurfaceRenderer.bounds.center.x,
                    heatingSurfaceRenderer.bounds.max.y,
                    heatingSurfaceRenderer.bounds.center.z);
                var displayRenderer = FindRenderer(
                    view, "HeatingBase_TemperatureDisplay");
                var powerPivot = Find(view, "HeatingBase_PowerButton_Pivot");
                var minusPivot = Find(view, "HeatingBase_MinusButton_Pivot");
                var plusPivot = Find(view, "HeatingBase_PlusButton_Pivot");
                var powerVisual = FindRenderer(view, "HeatingBase_PowerButton_Cap");
                var minusVisual = FindRenderer(view, "HeatingBase_MinusButton_Cap");
                var plusVisual = FindRenderer(view, "HeatingBase_PlusButton_Cap");
                var negativeZFace = displayRenderer.bounds.center.z < bounds.center.z;

                var displayArea = MakePanel(
                    "Temperature Display UI", root.transform,
                    ControlPosition(root.transform, displayRenderer.transform,
                        ControlFront(displayRenderer, negativeZFace)),
                    new Vector2(64, 24), negativeZFace, false);
                var display = AddText(displayArea, font, string.Empty, 18,
                    new Color(1f, .48f, .20f, 1f));
                display.raycastTarget = false;

                var controller = root.AddComponent<HeatingBaseController>();
                controller.Configure(heatingSurface, .115f, display, displayRenderer);
                var buttonAudio = root.AddComponent<AudioSource>();
                var operatingAudio = root.AddComponent<AudioSource>();
                controller.ConfigureAudio(
                    cookingEffects.ButtonBeep,
                    cookingEffects.InductionHum,
                    buttonAudio,
                    operatingAudio);
                CreateButton(root, controller, focusMaterial, font,
                    HeatingBaseButtonKind.Power, powerPivot, powerVisual,
                    "Power Button", null, powerIcon,
                    ControlFront(powerVisual, negativeZFace), negativeZFace);
                CreateButton(root, controller, focusMaterial, font,
                    HeatingBaseButtonKind.Decrease, minusPivot, minusVisual,
                    "Temperature Minus", "−", null,
                    ControlFront(minusVisual, negativeZFace), negativeZFace);
                CreateButton(root, controller, focusMaterial, font,
                    HeatingBaseButtonKind.Increase, plusPivot, plusVisual,
                    "Temperature Plus", "+", null,
                    ControlFront(plusVisual, negativeZFace), negativeZFace);

                root.SetActive(true);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(DefinitionPath);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ItemDefinition>();
                    AssetDatabase.CreateAsset(definition, DefinitionPath);
                }
                definition.Configure(
                    "heating-base", "平底锅加热底座", prefab,
                    new[] { "visit" }, true, new Vector2(.330f, .3135f),
                    .06015f, focusMaterial, ItemCategory.Appliance);
                definition.ConfigurePhysics(physics, true);
                definition.ConfigureInventory(1);
                definition.ConfigurePlacement(true);
                definition.ClearBreakable();
                definition.ClearImpactSound();
                definition.ClearCooking();
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

        private static void CreateButton(GameObject root,
            HeatingBaseController controller, Material focusMaterial,
            TMP_FontAsset font, HeatingBaseButtonKind kind, Transform pivot,
            Renderer visual, string name, string symbol, Sprite icon, float front,
            bool negativeZFace)
        {
            var area = MakePanel(name, root.transform,
                ControlPosition(root.transform, pivot, front),
                new Vector2(36, 34), negativeZFace, true);
            var hit = area.GetComponent<Image>();
            var button = area.gameObject.AddComponent<HeatingBaseButton>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var inwardWorld = (negativeZFace ? Vector3.forward : Vector3.back) * .0025f;
            var inwardLocal = pivot.parent.InverseTransformVector(inwardWorld);
            button.Configure(controller, kind, pivot, inwardLocal);
            if (icon != null) AddIcon(area, icon);
            else
            {
                var label = AddText(area, font, symbol, 20, Color.white);
                label.raycastTarget = false;
            }
            var focus = area.gameObject.AddComponent<ItemInteractionFocusTarget>();
            focus.Configure(button, focusMaterial, visual);
        }

        private static void AddIcon(RectTransform parent, Sprite sprite)
        {
            var icon = new GameObject("Power Icon", typeof(RectTransform))
                .AddComponent<Image>();
            var rect = icon.rectTransform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(.18f, .18f);
            rect.anchorMax = new Vector2(.82f, .82f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            icon.sprite = sprite;
            icon.color = Color.white;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
        }

        private static Sprite BuildPowerIcon()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "HeatingBasePowerIcon";
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var point = new Vector2(
                        (x + .5f) / size * 2f - 1f,
                        (y + .5f) / size * 2f - 1f);
                    var radius = point.magnitude;
                    var ring = radius >= .62f && radius <= .78f &&
                               !(point.y > .48f && Mathf.Abs(point.x) < .34f);
                    var stem = Mathf.Abs(point.x) <= .09f &&
                               point.y >= .05f && point.y <= .82f;
                    pixels[y * size + x] = ring || stem
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            Directory.CreateDirectory(Path.GetDirectoryName(PowerIconPath) ?? string.Empty);
            File.WriteAllBytes(PowerIconPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(PowerIconPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(PowerIconPath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("电源按钮图标导入失败。");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(PowerIconPath);
        }

        private static RectTransform MakePanel(string name, Transform parent,
            Vector3 position, Vector2 size, bool negativeZFace, bool raycast)
        {
            var rect = new GameObject(name, typeof(RectTransform))
                .GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localPosition = position;
            rect.localRotation = Quaternion.Euler(
                0, negativeZFace ? 0 : 180, 0);
            rect.localScale = Vector3.one * .001f;
            rect.sizeDelta = size;
            var canvas = rect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            if (raycast)
            {
                var mouse = rect.gameObject.AddComponent<GraphicRaycaster>();
                mouse.blockingObjects = GraphicRaycaster.BlockingObjects.None;
                var tracked =
                    rect.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                tracked.checkFor3DOcclusion = true;
            }
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = raycast;
            return rect;
        }

        private static TextMeshProUGUI AddText(RectTransform parent,
            TMP_FontAsset font, string text, float fontSize, Color color)
        {
            var label = new GameObject("Label", typeof(RectTransform))
                .AddComponent<TextMeshProUGUI>();
            var rect = label.rectTransform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            label.font = font;
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        private static Vector3 ControlPosition(
            Transform root, Transform control, float front)
        {
            var position = root.InverseTransformPoint(control.position);
            position.z = root.InverseTransformPoint(
                new Vector3(control.position.x, control.position.y, front)).z;
            return position;
        }

        private static float ControlFront(Renderer renderer, bool negativeZFace)
            => negativeZFace
                ? renderer.bounds.min.z - .0006f
                : renderer.bounds.max.z + .0006f;

        private static Transform Find(GameObject root, string name)
        {
            var result = root.GetComponentsInChildren<Transform>(true)
                .SingleOrDefault(value => value.name == name);
            if (result == null)
                throw new InvalidOperationException($"加热底座模型缺少节点：{name}");
            return result;
        }

        private static Renderer FindRenderer(GameObject root, string name)
        {
            var result = root.GetComponentsInChildren<Renderer>(true)
                .SingleOrDefault(value => value.name == name);
            if (result == null)
                throw new InvalidOperationException($"加热底座模型缺少渲染节点：{name}");
            return result;
        }

        private static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException("加热底座模型没有渲染网格。");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1))
                bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static void ConfigureModelImporter()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException($"加热底座模型导入失败：{ModelPath}");
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();
        }

        private static Material ConvertMaterial(Material source)
        {
            if (source == null) return null;
            var path = MaterialsPath + "/" + source.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is missing.");
            if (material == null)
            {
                material = new Material(shader) { name = source.name };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            var color = source.HasProperty("_BaseColor")
                ? source.GetColor("_BaseColor") : source.color;
            material.SetColor("_BaseColor", color);
            if (source.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", source.GetFloat("_Metallic"));
            if (source.HasProperty("_Glossiness"))
                material.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));
            if (source.name == "MAT_HeatingBase_DisplayGlass")
            {
                material.SetColor("_EmissionColor", Color.black);
                material.EnableKeyword("_EMISSION");
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
