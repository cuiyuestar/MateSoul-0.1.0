using System;
using System.IO;
using System.Linq;
using MateSoul.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public static class RadioBuilder
    {
        public const string PrefabPath = "Assets/MateSoul/Content/Props/Radio.prefab";
        public const string DefinitionPath = "Assets/MateSoul/Content/Props/Radio.asset";
        public const string ModelPath =
            "Assets/MateSoul/Content/PropsSource/Radio/LavenderRetroRadio_240x90x160mm.fbx";
        public const string BlueDiscoPath =
            "Assets/MateSoul/Content/Audio/Radio/BlueDiscoDance.wav";
        public const string BlueHourPath =
            "Assets/MateSoul/Content/Audio/Radio/BlueHour.wav";
        public const string SunsetGlowPath =
            "Assets/MateSoul/Content/Audio/Radio/SunsetGlow.wav";
        public const string TuningStaticPath =
            "Assets/MateSoul/Content/Audio/Radio/TuningStatic.wav";
        private const string MaterialsPath = "Assets/MateSoul/Content/Props/RadioMaterials";
        private const string FocusMaterialPath =
            "Assets/MateSoul/Content/ItemFocusHighlight.mat";

        [MenuItem("MateSoul/Game/Build Radio Asset")]
        public static void BuildAsset() => Build();

        public static ItemDefinition Build(Material focusMaterial = null)
        {
            focusMaterial ??= AssetDatabase.LoadAssetAtPath<Material>(FocusMaterialPath);
            if (focusMaterial == null)
                throw new InvalidOperationException($"交互部件光圈材质缺失：{FocusMaterialPath}");
            ConfigureModelImporter();
            var stations = new[]
            {
                ConfigureAudio(BlueDiscoPath, true),
                ConfigureAudio(BlueHourPath, true),
                ConfigureAudio(SunsetGlowPath, true)
            };
            var tuningNoise = ConfigureAudio(TuningStaticPath, false);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (source == null) throw new InvalidOperationException($"收音机模型导入失败：{ModelPath}");

            Directory.CreateDirectory(MaterialsPath);
            var root = new GameObject("复古收音机");
            root.SetActive(false);
            try
            {
                var view = (GameObject)PrefabUtility.InstantiatePrefab(source);
                view.name = "Model";
                view.transform.SetParent(root.transform, false);
                var bounds = BoundsOf(view);
                if (bounds.size.z > bounds.size.y)
                {
                    view.transform.localRotation = Quaternion.Euler(-90, 0, 0);
                    bounds = BoundsOf(view);
                }
                var tuningPivot = Find(view, "Radio_TuningKnob_Pivot");
                if (tuningPivot.position.z > bounds.center.z)
                {
                    view.transform.localRotation =
                        Quaternion.Euler(0, 180, 0) * view.transform.localRotation;
                    bounds = BoundsOf(view);
                }
                view.transform.position += new Vector3(
                    -bounds.center.x, -bounds.min.y, -bounds.center.z);
                bounds = BoundsOf(view);
                var expected = new Vector3(.24f, .268f, .09f);
                if (Mathf.Abs(bounds.size.x - expected.x) > .015f ||
                    Mathf.Abs(bounds.size.y - expected.y) > .015f ||
                    Mathf.Abs(bounds.size.z - expected.z) > .015f)
                    throw new InvalidOperationException(
                        $"收音机导入尺寸错误：{bounds.size}，期望 {expected}。");

                foreach (var renderer in view.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials
                        .Select(ConvertMaterial).ToArray();

                var collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(0, .08f, 0);
                collider.size = new Vector3(.24f, .16f, .09f);
                var physics = new BodySettings(1.8f, PhysicsContentBuilder.Plastic);
                PhysicsContentBuilder.ConfigureBody(root, physics);

                var channelA = root.AddComponent<AudioSource>();
                var channelB = root.AddComponent<AudioSource>();
                var noise = root.AddComponent<AudioSource>();
                var controller = root.AddComponent<RadioController>();
                var front = bounds.min.z - .008f;
                var powerPivot = Find(view, "Radio_PowerSwitch_Pivot");
                tuningPivot = Find(view, "Radio_TuningKnob_Pivot");
                var volumePivot = Find(view, "Radio_VolumeKnob_Pivot");
                var powerSlider = FindRenderer(view, "Radio_PowerSwitch_Slider");
                var needlePivot = Find(view, "Radio_DialNeedle_Pivot");
                var powerOffset = ConfigurePowerSwitchRange(powerPivot,
                    powerSlider,
                    FindRenderer(view, "Radio_PowerSwitch_Socket"),
                    FindRenderer(view, "Radio_Label_Off"),
                    FindRenderer(view, "Radio_Label_On"));
                IndicatorRange(needlePivot,
                    FindRenderer(view, "Radio_Frequency_Window"),
                    FindRenderer(view, "Radio_DialNeedle"),
                    out var indicatorMinimum, out var indicatorMaximum);

                var powerArea = MakeControl("Power Switch", root.transform,
                    ControlPosition(root.transform, powerPivot, front), new Vector2(42, 34));
                var power = powerArea.gameObject.AddComponent<RadioPowerButton>();
                ConfigureSelectable(power, powerArea.GetComponent<Image>());
                power.Configure(controller, powerPivot, powerOffset);
                ConfigureInteractionFocus(power, focusMaterial, powerSlider);

                var tuningArea = MakeControl("Tuning Knob", root.transform,
                    ControlPosition(root.transform, tuningPivot, front), new Vector2(68, 68));
                var tuning = tuningArea.gameObject.AddComponent<RadioKnobControl>();
                ConfigureSelectable(tuning, tuningArea.GetComponent<Image>());
                tuning.Configure(controller, RadioKnobKind.Tuning, tuningPivot, needlePivot,
                    indicatorMinimum, indicatorMaximum, .5f, 1f, null, null);
                ConfigureInteractionFocus(tuning, focusMaterial,
                    tuningPivot.GetComponentsInChildren<Renderer>(true));

                var volumeArea = MakeControl("Volume Knob", root.transform,
                    ControlPosition(root.transform, volumePivot, front), new Vector2(68, 68));
                var volume = volumeArea.gameObject.AddComponent<RadioKnobControl>();
                ConfigureSelectable(volume, volumeArea.GetComponent<Image>());
                var lowLabel = FindRenderer(view, "Radio_Label_Low");
                var middleLabel = FindRenderer(view, "Radio_Label_Volume");
                var highLabel = FindRenderer(view, "Radio_Label_High");
                var volumePointerDirection = IncreasingPointerDirection(
                    volumeArea, lowLabel, middleLabel, highLabel);
                volume.Configure(controller, RadioKnobKind.Volume, volumePivot, null,
                    Vector3.zero, Vector3.zero, .38f, volumePointerDirection,
                    lowLabel, highLabel);
                ConfigureInteractionFocus(volume, focusMaterial,
                    volumePivot.GetComponentsInChildren<Renderer>(true));

                controller.Configure(stations, tuningNoise, channelA, channelB, noise,
                    tuning, volume);
                root.SetActive(true);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(DefinitionPath);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ItemDefinition>();
                    AssetDatabase.CreateAsset(definition, DefinitionPath);
                }
                definition.Configure("radio", "复古收音机", prefab, new[] { "visit" }, true,
                    new Vector2(.24f, .09f), .268f, focusMaterial, ItemCategory.Appliance);
                definition.ConfigurePhysics(physics, true);
                definition.ConfigurePlacementFacing(180f);
                definition.ClearBreakable();
                definition.ClearImpactSound();
                definition.ConfigurePlacementParts();
                EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssets();
                Debug.Log($"RADIO_ASSET_BUILT bounds={bounds.size} stations={stations.Length}");
                return definition;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void ConfigureModelImporter()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException($"收音机模型导入失败：{ModelPath}");
            if (!importer.importAnimation && !importer.importCameras && !importer.importLights)
                return;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();
        }

        private static AudioClip ConfigureAudio(string path, bool music)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
                throw new InvalidOperationException($"收音机音频导入失败：{path}");
            var sample = importer.defaultSampleSettings;
            importer.forceToMono = true;
            importer.loadInBackground = music;
            sample.preloadAudioData = !music;
            sample.loadType = music
                ? AudioClipLoadType.Streaming
                : AudioClipLoadType.DecompressOnLoad;
            sample.compressionFormat = music
                ? AudioCompressionFormat.Vorbis
                : AudioCompressionFormat.PCM;
            sample.quality = music ? .72f : 1f;
            sample.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;
            importer.defaultSampleSettings = sample;
            importer.SaveAndReimport();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) throw new InvalidOperationException($"收音机音频加载失败：{path}");
            return clip;
        }

        private static RectTransform MakeControl(string name, Transform parent,
            Vector3 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localPosition = position;
            // The radio face is on -Z; face UI canvases toward the player so pointer X is not mirrored.
            rect.localRotation = Quaternion.Euler(0, 180, 0);
            rect.localScale = Vector3.one * .001f;
            rect.sizeDelta = size;
            var canvas = rect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
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

        private static void ConfigureInteractionFocus(
            Selectable control, Material material, params Renderer[] renderers)
        {
            var focus = control.GetComponent<ItemInteractionFocusTarget>() ??
                        control.gameObject.AddComponent<ItemInteractionFocusTarget>();
            focus.Configure(control, material, renderers);
            EditorUtility.SetDirty(focus);
        }

        private static Vector3 ControlPosition(Transform root, Transform pivot, float front)
        {
            var position = root.InverseTransformPoint(pivot.position);
            position.z = front;
            return position;
        }

        private static Transform Find(GameObject root, string name)
        {
            var result = root.GetComponentsInChildren<Transform>(true)
                .SingleOrDefault(value => value.name == name);
            if (result == null)
                throw new InvalidOperationException($"收音机模型缺少控制节点：{name}");
            return result;
        }

        private static Renderer FindRenderer(GameObject root, string name)
        {
            var result = root.GetComponentsInChildren<Renderer>(true)
                .SingleOrDefault(value => value.name == name);
            if (result == null)
                throw new InvalidOperationException($"收音机模型缺少渲染节点：{name}");
            return result;
        }

        private static Vector3 ConfigurePowerSwitchRange(Transform pivot, Renderer slider,
            Renderer socket, Renderer offLabel, Renderer onLabel)
        {
            var worldDirection = onLabel.bounds.center - offLabel.bounds.center;
            if (worldDirection.sqrMagnitude < .000001f)
                throw new InvalidOperationException("收音机开关标签无法确定开机方向。");
            worldDirection.Normalize();
            var socketHalf = ProjectedExtent(socket.bounds.extents, worldDirection);
            var sliderHalf = ProjectedExtent(slider.bounds.extents, worldDirection);
            var socketCenter = Vector3.Dot(socket.bounds.center, worldDirection);
            var currentCenter = Vector3.Dot(slider.bounds.center, worldDirection);
            var offCenter = socketCenter - socketHalf + sliderHalf + .001f;
            var onCenter = socketCenter + socketHalf - sliderHalf - .001f;
            if (onCenter <= offCenter)
                throw new InvalidOperationException("收音机开关槽不足以容纳滑块行程。");
            pivot.position += worldDirection * (offCenter - currentCenter);
            return pivot.parent.InverseTransformVector(
                worldDirection * (onCenter - offCenter));
        }

        private static float ProjectedExtent(Vector3 extents, Vector3 direction)
            => Mathf.Abs(direction.x) * extents.x +
               Mathf.Abs(direction.y) * extents.y +
               Mathf.Abs(direction.z) * extents.z;

        private static void IndicatorRange(Transform pivot, Renderer window, Renderer needle,
            out Vector3 minimum, out Vector3 maximum)
        {
            var margin = needle.bounds.extents.x + .002f;
            if (window.bounds.size.x <= margin * 2)
                throw new InvalidOperationException("收音机频率窗不足以容纳指针。");
            var left = pivot.position;
            var right = pivot.position;
            left.x = window.bounds.min.x + margin;
            right.x = window.bounds.max.x - margin;
            var leftLocal = pivot.parent.InverseTransformPoint(left);
            var rightLocal = pivot.parent.InverseTransformPoint(right);
            minimum = pivot.localPosition;
            maximum = pivot.localPosition;
            minimum.x = Mathf.Min(leftLocal.x, rightLocal.x);
            maximum.x = Mathf.Max(leftLocal.x, rightLocal.x);
        }

        private static float IncreasingPointerDirection(RectTransform control, Renderer low,
            Renderer middle, Renderer high)
        {
            var lowAngle = ControlAngle(control, low.bounds.center);
            var middleAngle = ControlAngle(control, middle.bounds.center);
            var highAngle = ControlAngle(control, high.bounds.center);
            var first = Mathf.DeltaAngle(lowAngle, middleAngle);
            var second = Mathf.DeltaAngle(middleAngle, highAngle);
            if (Mathf.Abs(first) < 15f || Mathf.Abs(second) < 15f ||
                Mathf.Sign(first) != Mathf.Sign(second))
                throw new InvalidOperationException(
                    "收音机音量标签无法确定从小到大的连续旋转方向。");
            return Mathf.Sign(first);
        }

        private static float ControlAngle(RectTransform control, Vector3 worldPosition)
        {
            var point = control.InverseTransformPoint(worldPosition);
            return Mathf.Atan2(point.x, point.y) * Mathf.Rad2Deg;
        }

        private static Bounds BoundsOf(GameObject view)
        {
            var renderers = view.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException("收音机模型没有渲染网格。");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static Material ConvertMaterial(Material source)
        {
            if (source == null) return null;
            var safeName = string.Join("_", source.name.Split(Path.GetInvalidFileNameChars()));
            var path = MaterialsPath + "/" + safeName + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is missing.");
            if (material == null)
            {
                material = new Material(shader) { name = source.name };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            material.SetColor("_BaseColor", RadioColor(source.name));
            material.SetFloat("_Metallic",
                source.name.Contains("Metal") || source.name.Contains("Silver") ? .7f : 0f);
            material.SetFloat("_Smoothness",
                source.name.Contains("Speaker") ? .18f : source.name.Contains("Window") ? .58f : .45f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Color RadioColor(string name)
        {
            if (name.Contains("LightLavender")) return new Color(.83f, .68f, .88f);
            if (name.Contains("Lavender_ABS")) return new Color(.68f, .45f, .78f);
            if (name.Contains("Ivory")) return new Color(.94f, .87f, .9f);
            if (name.Contains("Pink")) return new Color(.96f, .49f, .66f);
            if (name.Contains("DeepPurple")) return new Color(.22f, .12f, .29f);
            if (name.Contains("Window")) return new Color(.19f, .13f, .24f);
            if (name.Contains("Needle")) return new Color(1f, .3f, .48f);
            if (name.Contains("Speaker")) return new Color(.18f, .11f, .23f);
            return new Color(.74f, .7f, .76f);
        }
    }
}
