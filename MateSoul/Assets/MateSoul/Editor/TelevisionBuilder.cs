using System;
using System.IO;
using System.Linq;
using MateSoul.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public static class TelevisionBuilder
    {
        public const string PrefabPath = "Assets/MateSoul/Content/Props/Television.prefab";
        public const string DefinitionPath = "Assets/MateSoul/Content/Props/Television.asset";
        public const string FirstProgramPath =
            "Assets/MateSoul/Content/Video/PixelSpaceGameProgram.mp4";
        private const string Source = "Assets/MateSoul/Content/PropsSource/Television";
        private const string Materials = "Assets/MateSoul/Content/Props/TelevisionMaterials";
        private const string FocusMaterialPath =
            "Assets/MateSoul/Content/ItemFocusHighlight.mat";

        [MenuItem("MateSoul/Game/Build Television Asset")]
        public static void BuildAsset() => Build();

        public static ItemDefinition Build(Material focusMaterial = null)
        {
            focusMaterial = ResolveFocusMaterial(focusMaterial);
            var programs = LoadPrograms();
            // Preserve source settings and hand-authored prefab changes on scene rebuild.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) prefab = CreatePrefab(programs, focusMaterial);
            else prefab = UpgradePrefab(programs, focusMaterial);
            var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(DefinitionPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<ItemDefinition>();
                AssetDatabase.CreateAsset(definition, DefinitionPath);
            }
            definition.Configure("television", "复古电视", prefab, new[] { "visit" }, true,
                new Vector2(.48f, .45f), .566f, focusMaterial, ItemCategory.Appliance);
            definition.ConfigurePhysics(new BodySettings(18f, PhysicsContentBuilder.Metal), true);
            definition.ConfigurePlacement(
                true, true, ItemPlacementSurface.Ground, true);
            definition.ConfigurePlacementFacing(180f);
            definition.ConfigureSurfaceSupport(.85f);
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
            return definition;
        }

        private static GameObject CreatePrefab(VideoClip[] programs, Material focusMaterial)
        {
            Directory.CreateDirectory(Source);
            Directory.CreateDirectory(Materials);
            var art = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../ArtSource/SapphiArt/Props/Television"));
            CopyIfMissing(Path.Combine(art, "exports/LavenderRetroTV_480x450x420mm.fbx"),
                Source + "/LavenderRetroTV_480x450x420mm.fbx");
            foreach (var name in new[] { "electronics_atlas.png", "screen_basecolor.png" })
                CopyIfMissing(Path.Combine(art, "textures", name), Source + "/" + name);
            AssetDatabase.Refresh();
            var importer = (ModelImporter)AssetImporter.GetAtPath(Source + "/LavenderRetroTV_480x450x420mm.fbx");
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(importer.assetPath);
            var root = new GameObject("复古电视");
            root.SetActive(false);
            try
            {
                var view = (GameObject)PrefabUtility.InstantiatePrefab(model);
                view.name = "Model";
                view.transform.SetParent(root.transform, false);
                var button = Find(view, "TV_Power_Button");
                var bounds = BoundsOf(view);
                if (bounds.size.z > bounds.size.y)
                {
                    // This FBX preserves Blender's Z-up axes instead of converting to Unity Y-up.
                    view.transform.localRotation = Quaternion.Euler(-90, 0, 0);
                    bounds = BoundsOf(view);
                }
                // Normalize this asset's front to -Z, matching the existing item approach offset.
                if (button.bounds.center.z > bounds.center.z)
                    view.transform.localRotation =
                        Quaternion.Euler(0, 180, 0) * view.transform.localRotation;
                bounds = BoundsOf(view);
                view.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                bounds = BoundsOf(view);
                if (Mathf.Abs(bounds.size.x - .48f) > .01f ||
                    Mathf.Abs(bounds.size.y - .5657f) > .01f ||
                    Mathf.Abs(bounds.size.z - .45f) > .01f)
                    throw new InvalidOperationException($"Television dimensions are incorrect: {bounds.size}");

                foreach (var renderer in view.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(ConvertMaterial).ToArray();
                var screen = Find(view, "TV_CRT_ConvexGlass");
                screen.sharedMaterial = CreateScreenMaterial();
                var collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(0, .21f, 0);
                collider.size = new Vector3(.48f, .42f, .45f);
                var obstacle = root.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = collider.center;
                obstacle.size = collider.size;
                obstacle.carving = true;
                var decoder = root.AddComponent<VideoPlayer>();
                ConfigurePlayer(decoder);
                var speaker = root.AddComponent<AudioSource>();
                speaker.playOnAwake = false;
                var television = root.AddComponent<TelevisionController>();

                var statusCanvas = MakeCanvas("Screen Status", root.transform,
                    new Vector3(screen.bounds.center.x, screen.bounds.center.y, screen.bounds.min.z - .002f),
                    new Vector2(300, 100));
                var label = statusCanvas.gameObject.AddComponent<Text>();
                label.font = AssetDatabase.LoadAssetAtPath<Font>(UiFontAssetBuilder.SourcePath);
                if (label.font == null)
                    throw new InvalidOperationException(
                        $"{UiFontAssetBuilder.SourcePath} is missing.");
                label.fontSize = 22;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = new Color(.7f, .8f, .85f);
                label.raycastTarget = false;
                television.Configure(screen, Find(view, "TV_LED_1"), label, decoder, speaker);
                television.ConfigurePrograms(programs);

                // A stable hit area stays in front of the housing while the mesh moves inward.
                var hitArea = MakeCanvas("Power Button", root.transform,
                    new Vector3(button.bounds.center.x, button.bounds.center.y,
                        Mathf.Min(button.bounds.min.z, -.225f) - .011f), new Vector2(48, 48));
                var mouse = hitArea.gameObject.AddComponent<GraphicRaycaster>();
                // Desktop mouse support is an editor fallback. XR keeps physical
                // occlusion through TrackedDeviceGraphicRaycaster below.
                mouse.blockingObjects = GraphicRaycaster.BlockingObjects.None;
                var tracked = hitArea.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                tracked.checkFor3DOcclusion = true;
                var hit = hitArea.gameObject.AddComponent<Image>();
                hit.color = Color.clear;
                var interaction = hitArea.gameObject.AddComponent<TelevisionPowerButton>();
                interaction.targetGraphic = hit;
                interaction.transition = Selectable.Transition.None;
                interaction.navigation = new Navigation { mode = Navigation.Mode.None };
                interaction.Configure(television, button.transform,
                    button.transform.parent.InverseTransformVector(Vector3.forward * .003f));
                ConfigureInteractionFocus(interaction, button, focusMaterial);
                EnsurePhysicalPressSurface(root);
                PhysicsContentBuilder.ConfigureBody(root, new BodySettings(18f, PhysicsContentBuilder.Metal));

                root.SetActive(true);
                var result = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"TELEVISION_ASSET_BUILT bounds={bounds.size} button={button.bounds.center} " +
                          $"screen={screen.bounds.center}");
                return result;
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject UpgradePrefab(VideoClip[] programs, Material focusMaterial)
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                EnsurePhysicalPressSurface(root);
                var button = root.GetComponentInChildren<TelevisionPowerButton>(true);
                var buttonRenderer = root.GetComponentsInChildren<Renderer>(true)
                    .Single(renderer => renderer.name == "TV_Power_Button");
                ConfigureInteractionFocus(button, buttonRenderer, focusMaterial);
                PhysicsContentBuilder.ConfigureBody(root, new BodySettings(18f, PhysicsContentBuilder.Metal));
                var television = root.GetComponent<TelevisionController>();
                if (television == null)
                    throw new InvalidOperationException("Television controller is missing.");
                ConfigurePlayer(root.GetComponent<VideoPlayer>());
                television.ConfigurePrograms(programs);
                EditorUtility.SetDirty(television);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        private static Material ResolveFocusMaterial(Material material)
        {
            material ??= AssetDatabase.LoadAssetAtPath<Material>(FocusMaterialPath);
            if (material == null)
                throw new InvalidOperationException($"交互部件光圈材质缺失：{FocusMaterialPath}");
            return material;
        }

        private static void ConfigureInteractionFocus(
            TelevisionPowerButton control, Renderer renderer, Material material)
        {
            if (control == null || renderer == null)
                throw new InvalidOperationException("电视开关缺少交互光圈绑定。");
            var focus = control.GetComponent<ItemInteractionFocusTarget>() ??
                        control.gameObject.AddComponent<ItemInteractionFocusTarget>();
            focus.Configure(control, material, renderer);
            EditorUtility.SetDirty(focus);
        }

        private static void ConfigurePlayer(VideoPlayer player)
        {
            if (player == null) throw new InvalidOperationException("Television video player is missing.");
            player.playOnAwake = false;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.aspectRatio = VideoAspectRatio.FitOutside;
            player.isLooping = true;
            player.waitForFirstFrame = true;
            EditorUtility.SetDirty(player);
        }

        private static VideoClip[] LoadPrograms()
        {
            var first = AssetDatabase.LoadAssetAtPath<VideoClip>(FirstProgramPath);
            if (first == null)
                throw new InvalidOperationException($"电视节目导入失败：{FirstProgramPath}");
            return new[] { first };
        }

        private static bool EnsurePhysicalPressSurface(GameObject root)
        {
            var button = root.GetComponentInChildren<TelevisionPowerButton>(true);
            if (button == null)
                throw new InvalidOperationException("Television power button is missing.");
            button.interactable = true;
            var mouse = button.GetComponent<GraphicRaycaster>();
            if (mouse != null) mouse.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            var buttonPosition = button.transform.localPosition;
            buttonPosition.z = -.24f;
            button.transform.localPosition = buttonPosition;
            var surface = root.GetComponentInChildren<TelevisionPhysicalPressSurface>(true);
            var created = surface == null;
            var physical = created ? new GameObject("Physical Press Surface") : surface.gameObject;
            physical.transform.SetParent(root.transform, false);
            physical.transform.localPosition = buttonPosition;
            physical.transform.localRotation = Quaternion.identity;
            var trigger = physical.GetComponent<BoxCollider>() ?? physical.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = Vector3.forward * .003f;
            trigger.size = new Vector3(.032f, .032f, .012f);
            (surface ?? physical.AddComponent<TelevisionPhysicalPressSurface>()).Configure(button, .006f);
            return created;
        }

        private static RectTransform MakeCanvas(string name, Transform parent, Vector3 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localPosition = position;
            rect.localScale = Vector3.one * .001f;
            rect.sizeDelta = size;
            var canvas = rect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            return rect;
        }

        private static Renderer Find(GameObject view, string name)
            => view.GetComponentsInChildren<Renderer>(true).Single(renderer => renderer.name == name);

        private static Bounds BoundsOf(GameObject view)
        {
            var renderers = view.GetComponentsInChildren<Renderer>(true);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static void CopyIfMissing(string source, string destination)
        {
            if (!File.Exists(destination)) File.Copy(source, destination);
        }

        private static Material ConvertMaterial(Material original)
        {
            var path = Materials + "/" + original.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = original.name };
            material.SetColor("_BaseColor", original.color);
            material.SetFloat("_Smoothness", .55f);
            if (original.name == "TV_ABS_and_PadPrint")
            {
                material.SetTexture("_BaseMap",
                    AssetDatabase.LoadAssetAtPath<Texture2D>(Source + "/electronics_atlas.png"));
                material.SetColor("_BaseColor", Color.white);
            }
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", Color.black);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Material CreateScreenMaterial()
        {
            var path = Materials + "/TelevisionScreen.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("MateSoul/Television Screen")) { name = "Television Screen" };
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Source + "/screen_basecolor.png"));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
