using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MateSoul.Runtime;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public static class ItemActionContentBuilder
    {
        private const string Content = "Assets/MateSoul/Content";
        private const string Actions = Content + "/ItemActions";
        private const string Source = Content + "/PropsSource/Apple";
        private const float PalmPadDepth = .018f;
        private const float PinchReach = .070f;

        [MenuItem("MateSoul/Game/Build Item Actions")]
        public static void Build()
        {
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition", new[] { Content })
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<ItemDefinition>)
                .Where(item => item.Id != "apple-quarter").ToArray();
            var definitions = ConfigureCatalog(catalog);
            var character = PrefabUtility.LoadPrefabContents(GameSceneBuilder.CharacterPath);
            try
            {
                ConfigureCharacter(character);
                PrefabUtility.SaveAsPrefabAsset(character, GameSceneBuilder.CharacterPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(character); }
            // Update only the scene's catalog reference; retain its authored layout.
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(GameSceneBuilder.ScenePath);
            var bootstrap = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<GameBootstrap>(true)).Single();
            var serialized = new SerializedObject(bootstrap);
            var items = serialized.FindProperty("items");
            if (items == null) throw new InvalidOperationException("Scene item catalog field is missing.");
            var quarter = definitions.Single(item => item.Id == "apple-quarter");
            var containsQuarter = Enumerable.Range(0, items.arraySize)
                .Any(index => items.GetArrayElementAtIndex(index).objectReferenceValue == quarter);
            if (!containsQuarter)
            {
                items.InsertArrayElementAtIndex(items.arraySize);
                items.GetArrayElementAtIndex(items.arraySize - 1).objectReferenceValue = quarter;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[ItemActions] content built");
        }

        public static ItemDefinition[] ConfigureCatalog(ItemDefinition[] catalog)
        {
            Directory.CreateDirectory(Actions);
            AssetDatabase.Refresh();
            var normal = AssetImporter.GetAtPath(Source + "/apple_flesh_normal.png") as TextureImporter;
            if (normal != null && normal.textureType != TextureImporterType.NormalMap)
            {
                normal.textureType = TextureImporterType.NormalMap;
                normal.SaveAndReimport();
            }
            var quarter = BuildQuarter(catalog.Single(item => item.Id == "apple"));
            foreach (var item in catalog)
            {
                ItemInteractionProfile profile = null;
                if (item.Id == "apple")
                {
                    profile = Profile("Apple", "cuttable-fruit", new Vector3(0, .065f, -.008f));
                    profile.cutResult = quarter;
                }
                else if (item.Id == "knife")
                {
                    profile = Profile("Knife", "cutting-tool", Vector3.zero);
                    var root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(item.Prefab));
                    try
                    {
                        var renderers = root.GetComponentsInChildren<Renderer>();
                        var handle = renderers.Where(renderer =>
                            renderer.name.IndexOf("Handle", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                        if (handle.Length == 0) throw new InvalidOperationException("Knife handle mesh is missing.");
                        var bounds = handle[0].bounds;
                        foreach (var renderer in handle.Skip(1)) bounds.Encapsulate(renderer.bounds);
                        profile.gripPosition = root.transform.InverseTransformPoint(bounds.center);
                        var blade = renderers.First(renderer =>
                            renderer.name.IndexOf("Blade", StringComparison.OrdinalIgnoreCase) >= 0);
                        var bladeBounds = blade.bounds;
                        profile.bladeStart = root.transform.InverseTransformPoint(
                            new Vector3(bladeBounds.min.x, bladeBounds.center.y, bladeBounds.min.z + .02f));
                        profile.bladeEnd = root.transform.InverseTransformPoint(
                            new Vector3(bladeBounds.min.x, bladeBounds.center.y, bladeBounds.max.z - .02f));
                        Debug.Log($"[ItemActions] knife grip={profile.gripPosition:F4} " +
                                  $"blade={profile.bladeStart:F4} -> {profile.bladeEnd:F4}");
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                else if (item.Id == "plate" || item.Id == "plate-medium" || item.Id == "plate-large")
                {
                    profile = Profile(item.name, "food-receiver",
                        new Vector3(item.Footprint.x * .36f, item.PlacementHeight * .65f, 0));
                    profile.receivingHeight = item.PlacementHeight + .002f;
                }
                else if (item.Id == "table")
                {
                    profile = Profile("DiningTable", "food-workstation", Vector3.zero);
                    profile.workPosition = new Vector3(.06f, .759f, -.16f);
                    profile.platePosition = new Vector3(-.26f, .759f, -.16f);
                    profile.toolPosition = new Vector3(.17f, .759f, -.14f);
                    profile.approachPosition = new Vector3(0, 0, -.65f);
                }
                if (profile == null)
                    profile = Profile(item.name, "character-grip", Vector3.zero);
                ConfigureGripGeometry(item, profile);
                if (item.Id == "knife")
                {
                    var gesture = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                        PickupAnimationBuilder.KnifeGesturePath);
                    if (gesture) profile.handGesture = gesture;
                }
                item.ConfigureInteraction(profile);
                EditorUtility.SetDirty(profile);
                EditorUtility.SetDirty(item);
            }
            // The generic grip rewrite above must not undo the prop poses that
            // were measured from the authored clips.
            ItemUseAnimationBuilder.FitGripForContent();
            AssetDatabase.SaveAssets();
            return catalog.Concat(new[] { quarter }).ToArray();
        }

        private static ItemInteractionProfile Profile(string name, string capability, Vector3 grip)
        {
            var path = Actions + "/" + name + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<ItemInteractionProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<ItemInteractionProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            profile.capability = capability;
            profile.gripPosition = grip;
            profile.carryEuler = Vector3.zero;
            return profile;
        }

        private static ItemDefinition BuildQuarter(ItemDefinition apple)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(
                Source + "/GameApple_Quarter_45x45x84mm.fbx");
            if (source == null) throw new InvalidOperationException("Missing quarter apple model.");
            var root = new GameObject("苹果块");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                model.transform.SetParent(root.transform, false);
                var renderers = model.GetComponentsInChildren<Renderer>();
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                model.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                if (Mathf.Abs(bounds.size.y - .084f) > .005f ||
                    bounds.size.x > .05f || bounds.size.z > .05f)
                    throw new InvalidOperationException("Quarter apple axis/scale is incorrect: " + bounds.size);
                foreach (var renderer in renderers)
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(AppleMaterial).ToArray();
                var collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(0, .042f, 0);
                collider.size = new Vector3(.044f, .084f, .044f);
                var physics = new BodySettings(.045f, apple.Physics.material);
                PhysicsContentBuilder.ConfigureBody(root, physics);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, Content + "/Props/AppleQuarter.prefab");
                var path = Content + "/Props/AppleQuarter.asset";
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ItemDefinition>();
                    AssetDatabase.CreateAsset(definition, path);
                }
                definition.Configure("apple-quarter", "苹果块", prefab, new[] { "visit" }, false,
                    new Vector2(.045f, .045f), .084f, apple.FocusMaterial, ItemCategory.Food);
                definition.ConfigurePhysics(physics, true);
                definition.ConfigureInventory(0);
                var profile = Profile("AppleQuarter", "food-piece", new Vector3(0, .06f, 0));
                profile.assemblyOffset = new Vector3(bounds.center.x + Mathf.Sign(bounds.center.x) * .003f,
                    0, bounds.center.z + Mathf.Sign(bounds.center.z) * .003f);
                Debug.Log($"[ItemActions] quarter size={bounds.size:F4} assembly={profile.assemblyOffset:F4}");
                ConfigureGripGeometry(definition, profile);
                definition.ConfigureInteraction(profile, true);
                EditorUtility.SetDirty(profile);
                EditorUtility.SetDirty(definition);
                return definition;
            }
            finally { Object.DestroyImmediate(root); }
        }

        private readonly struct GripSpec
        {
            public readonly ItemGripKind Kind;
            public readonly CharacterHandPose Pose;
            public readonly float Clearance;
            public readonly float Curl;

            public GripSpec(ItemGripKind kind, CharacterHandPose pose,
                float clearance, float curl)
            {
                Kind = kind;
                Pose = pose;
                Clearance = clearance;
                Curl = curl;
            }
        }

        private static GripSpec GripSpecFor(string id)
        {
            switch (id)
            {
                case "knife":
                case "frying-pan":
                case "frying-pan-body":
                case "spatula":
                    return new GripSpec(
                        ItemGripKind.Handle, CharacterHandPose.Power, .002f, .9f);
                case "spoon":
                case "chopsticks":
                case "chopstick-left":
                case "chopstick-right":
                    return new GripSpec(
                        ItemGripKind.Handle, CharacterHandPose.Pinch, .0015f, .72f);
                case "mug":
                case "radio":
                    return new GripSpec(
                        ItemGripKind.LoopHandle, CharacterHandPose.Hook, .002f, .82f);
                case "television":
                case "table":
                case "chair":
                case "water-dispenser":
                case "heating-base":
                    return new GripSpec(
                        ItemGripKind.LargeTwoHand,
                        CharacterHandPose.TwoHandSupport, .004f, .62f);
                case "plate":
                case "plate-medium":
                case "plate-large":
                case "dartboard":
                case "playing-cards":
                case "remote-control":
                case "clock":
                case "raw-steak":
                    return new GripSpec(
                        ItemGripKind.ThinEdge, CharacterHandPose.Pinch, .002f, .7f);
                default:
                    return new GripSpec(
                        ItemGripKind.Generic, CharacterHandPose.Support, .003f, .68f);
            }
        }

        private static void ConfigureGripGeometry(
            ItemDefinition item, ItemInteractionProfile profile)
        {
            var spec = GripSpecFor(item.Id);
            var fallback = new Bounds(
                new Vector3(0, item.PlacementHeight * .5f, 0),
                new Vector3(
                    Mathf.Max(.005f, item.Footprint.x),
                    Mathf.Max(.005f, item.PlacementHeight),
                    Mathf.Max(.005f, item.Footprint.y)));
            var overall = fallback;
            Bounds? namedGrip = null;
            if (item.Prefab != null)
            {
                var path = AssetDatabase.GetAssetPath(item.Prefab);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var renderers = root.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length > 0)
                        overall = LocalBounds(root.transform, renderers);
                    var gripRenderers = renderers.Where(renderer =>
                        renderer.name.IndexOf("handle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        renderer.name.IndexOf("grip", StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToArray();
                    if (gripRenderers.Length > 0)
                        namedGrip = LocalBounds(root.transform, gripRenderers);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            Pose primary;
            Pose? secondary = null;
            Vector3 gripSize;
            Vector3 shapeOffset;
            switch (spec.Kind)
            {
                case ItemGripKind.Handle:
                    ConfigureHandleGrip(overall, namedGrip, spec.Clearance,
                        out primary, out gripSize, out shapeOffset);
                    break;
                case ItemGripKind.LoopHandle:
                    ConfigureLoopGrip(overall, namedGrip, spec.Clearance,
                        out primary, out gripSize, out shapeOffset);
                    break;
                case ItemGripKind.ThinEdge:
                    ConfigureThinGrip(overall,
                        out primary, out gripSize, out shapeOffset);
                    break;
                case ItemGripKind.LargeTwoHand:
                    ConfigureTwoHandGrip(overall, spec.Clearance,
                        out primary, out var second, out gripSize,
                        out shapeOffset);
                    secondary = second;
                    break;
                default:
                    ConfigureGenericGrip(overall, spec.Clearance,
                        out primary, out gripSize, out shapeOffset);
                    break;
            }
            profile.ConfigureGrip(spec.Kind, spec.Pose, primary,
                gripSize, shapeOffset, spec.Clearance, spec.Curl, secondary);
            Debug.Log($"[ItemGrip] {item.Id} kind={spec.Kind} " +
                      $"primary={primary.position:F4} size={gripSize:F4} " +
                      $"shapeOffset={shapeOffset:F4}" +
                      (secondary.HasValue
                          ? $" secondary={secondary.Value.position:F4}" : string.Empty));
        }

        private static void ConfigureHandleGrip(Bounds overall, Bounds? named,
            float clearance, out Pose primary, out Vector3 gripSize,
            out Vector3 shapeOffset)
        {
            var grip = named ?? overall;
            var direction = overall.center - grip.center;
            if (direction.sqrMagnitude < .0001f)
            {
                var size = overall.size;
                direction = size.x >= size.z ? Vector3.right : Vector3.forward;
                var extent = size.x >= size.z ? overall.extents.x : overall.extents.z;
                grip.center = overall.center - direction * extent * .55f;
            }
            direction.y = 0;
            if (direction.sqrMagnitude < .0001f) direction = Vector3.forward;
            direction.Normalize();
            var dimensions = new[] { grip.size.x, grip.size.y, grip.size.z }
                .OrderBy(value => value).ToArray();
            var diameter = Mathf.Clamp(dimensions[0], .006f, .08f);
            var length = Mathf.Clamp(dimensions[2], .04f, .18f);
            gripSize = new Vector3(diameter, diameter, length);
            var rotation = Quaternion.FromToRotation(
                Vector3.forward, direction);
            var support = diameter * .5f + clearance + PalmPadDepth;
            primary = new Pose(
                grip.center + rotation * Vector3.up * support, rotation);
            shapeOffset = Vector3.down * support;
        }

        private static void ConfigureLoopGrip(Bounds overall, Bounds? named,
            float clearance, out Pose primary, out Vector3 gripSize,
            out Vector3 shapeOffset)
        {
            var grip = named ?? new Bounds(
                overall.center + Vector3.right * overall.extents.x,
                new Vector3(.018f,
                    Mathf.Clamp(overall.size.y * .5f, .05f, .14f), .018f));
            var direction = overall.center - grip.center;
            direction.y = 0;
            if (direction.sqrMagnitude < .0001f) direction = Vector3.left;
            direction.Normalize();
            var thickness = Mathf.Clamp(
                Mathf.Min(grip.size.x, grip.size.z), .008f, .03f);
            gripSize = new Vector3(thickness, thickness,
                Mathf.Clamp(grip.size.y, .04f, .15f));
            var rotation = Quaternion.FromToRotation(
                Vector3.forward, direction);
            var support = thickness * .5f + clearance + PalmPadDepth;
            primary = new Pose(
                grip.center + rotation * Vector3.up * support, rotation);
            shapeOffset = Vector3.down * support;
        }

        private static void ConfigureThinGrip(Bounds overall,
            out Pose primary, out Vector3 gripSize,
            out Vector3 shapeOffset)
        {
            Vector3 edge;
            Vector3 inward;
            float edgeLength;
            if (overall.size.x >= overall.size.z)
            {
                edge = overall.center + Vector3.right * overall.extents.x;
                inward = Vector3.left;
                edgeLength = overall.size.z;
            }
            else
            {
                edge = overall.center + Vector3.forward * overall.extents.z;
                inward = Vector3.back;
                edgeLength = overall.size.x;
            }
            var thickness = Mathf.Clamp(
                Mathf.Min(overall.size.x,
                    Mathf.Min(overall.size.y, overall.size.z)), .003f, .035f);
            gripSize = new Vector3(
                thickness, Mathf.Clamp(thickness + .01f, .012f, .04f),
                Mathf.Clamp(edgeLength * .3f, .025f, .08f));
            var rotation = Quaternion.FromToRotation(
                Vector3.forward, inward);
            primary = new Pose(
                edge - rotation * Vector3.forward * PinchReach, rotation);
            shapeOffset = Vector3.forward * PinchReach;
        }

        private static void ConfigureTwoHandGrip(Bounds overall,
            float clearance, out Pose primary, out Pose secondary,
            out Vector3 gripSize, out Vector3 shapeOffset)
        {
            var y = overall.min.y + overall.size.y * .42f;
            var z = overall.center.z - overall.extents.z;
            var inset = Mathf.Clamp(overall.size.x * .08f, .025f, .08f);
            var right = new Vector3(overall.max.x - inset, y, z);
            var left = new Vector3(overall.min.x + inset, y, z);
            gripSize = new Vector3(
                Mathf.Clamp(overall.size.z * .15f, .025f, .07f),
                Mathf.Clamp(overall.size.y * .12f, .035f, .09f),
                Mathf.Clamp(overall.size.y * .25f, .06f, .16f));
            var primaryRotation = Quaternion.LookRotation(
                Vector3.left, Vector3.back);
            var secondaryRotation = Quaternion.LookRotation(
                Vector3.right, Vector3.back);
            var support = gripSize.y * .5f + clearance + PalmPadDepth;
            primary = new Pose(
                right + primaryRotation * Vector3.up * support,
                primaryRotation);
            secondary = new Pose(
                left + secondaryRotation * Vector3.up * support,
                secondaryRotation);
            shapeOffset = Vector3.down * support;
        }

        private static void ConfigureGenericGrip(Bounds overall,
            float clearance, out Pose primary, out Vector3 gripSize,
            out Vector3 shapeOffset)
        {
            var support = overall.extents.y + clearance + PalmPadDepth;
            primary = new Pose(
                overall.center + Vector3.up * support,
                Quaternion.identity);
            gripSize = new Vector3(
                Mathf.Max(overall.size.x, .012f),
                Mathf.Max(overall.size.y, .012f),
                Mathf.Max(overall.size.z, .012f));
            shapeOffset = Vector3.down * support;
        }

        private static Bounds LocalBounds(
            Transform root, IReadOnlyList<Renderer> renderers)
        {
            var initialized = false;
            var result = new Bounds();
            foreach (var renderer in renderers)
            {
                var bounds = renderer.bounds;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var world = bounds.center + Vector3.Scale(
                        bounds.extents, new Vector3(x, y, z));
                    var local = root.InverseTransformPoint(world);
                    if (!initialized)
                    {
                        result = new Bounds(local, Vector3.zero);
                        initialized = true;
                    }
                    else result.Encapsulate(local);
                }
            }
            return initialized ? result :
                new Bounds(Vector3.zero, Vector3.one * .01f);
        }

        private static Material AppleMaterial(Material imported)
        {
            var name = imported.name;
            var path = Actions + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            var flesh = name.IndexOf("Flesh", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("FreshCut", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0;
            var skin = name.IndexOf("Skin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       name.IndexOf("CutPeel", StringComparison.OrdinalIgnoreCase) >= 0;
            if (flesh || skin)
            {
                var stem = flesh ? "apple_flesh" : "apple_skin";
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(
                    Source + "/" + stem + "_basecolor.png"));
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(
                    Source + "/" + stem + "_normal.png"));
                material.EnableKeyword("_NORMALMAP");
                material.SetColor("_BaseColor", Color.white);
            }
            else material.SetColor("_BaseColor", imported.HasProperty("_Color")
                ? imported.color : new Color(.15f, .06f, .02f));
            material.SetFloat("_Smoothness", flesh ? .35f : .6f);
            EditorUtility.SetDirty(material);
            return material;
        }

        public static void ConfigureCharacter(GameObject character)
        {
            Directory.CreateDirectory(Actions);
            AssetDatabase.Refresh();
            var animator = character.GetComponentInChildren<Animator>(true);
            var controller = animator.runtimeAnimatorController as AnimatorController;
            if (controller == null) throw new InvalidOperationException("Generic avatar controller required.");
            var transforms = animator.GetComponentsInChildren<Transform>(true);
            Transform Bone(string name) => transforms.Single(value => value.name == name);
            var rig = character.GetComponent<AvatarItemRig>() ?? character.AddComponent<AvatarItemRig>();
            rig.Configure(animator, new[] { Bone("lengyue_upper_arm_r"), Bone("lengyue_forearm_r"), Bone("lengyue_hand_r"),
                    Bone("lengyue_index_01_r"), Bone("lengyue_middle_01_r"), Bone("lengyue_little_01_r") },
                new[] { Bone("lengyue_upper_arm_l"), Bone("lengyue_forearm_l"), Bone("lengyue_hand_l"),
                    Bone("lengyue_index_01_l"), Bone("lengyue_middle_01_l"), Bone("lengyue_little_01_l") },
                new[] { Bone("lengyue_hips"), Bone("lengyue_spine"), Bone("lengyue_neck"), Bone("lengyue_head_01"),
                    Bone("lengyue_thigh_l"), Bone("lengyue_shin_l"), Bone("lengyue_foot_l"),
                    Bone("lengyue_thigh_r"), Bone("lengyue_shin_r"), Bone("lengyue_foot_r") });
            var layer = controller.layers.FirstOrDefault(value => value.name == "ItemInteraction");
            if (layer == null)
            {
                controller.AddLayer("ItemInteraction");
                layer = controller.layers.Last();
            }
            var validStates = new HashSet<string>(
                Enum.GetNames(typeof(ItemMotion)));
            foreach (var child in layer.stateMachine.states)
                if (!validStates.Contains(child.state.name))
                    layer.stateMachine.RemoveState(child.state);
            AssetDatabase.DeleteAsset(Actions + "/FloorReach.anim");
            AssetDatabase.DeleteAsset(Actions + "/TableReach.anim");
            foreach (ItemMotion motion in Enum.GetValues(typeof(ItemMotion)))
            {
                var clip = Bake(character, animator, motion, Bone);
                var state = layer.stateMachine.states.FirstOrDefault(value =>
                    value.state.name == motion.ToString()).state ??
                    layer.stateMachine.AddState(motion.ToString());
                state.motion = clip;
                state.speed = 0;
                state.writeDefaultValues = false;
            }
            EditorUtility.SetDirty(controller);
            var pickups = AssetDatabase.LoadAssetAtPath<PickupAnimationSet>(PickupAnimationBuilder.SetPath);
            if (pickups)
            {
                rig.ConfigurePickups(pickups);
                PickupAnimationBuilder.BindController(controller, pickups);
            }
            if (AssetDatabase.IsValidFolder(ItemUseAnimationBuilder.DirectoryPath))
                ItemUseAnimationBuilder.BindController(controller);
            EditorUtility.SetDirty(rig);
        }

        private static AnimationClip Bake(GameObject character, Animator animator,
            ItemMotion motion, Func<string, Transform> bone)
        {
            var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/Characters/LengyueCharacter/Lengyue/Runtime/Animations/Idle01/lengyue_clip_idle01_combined.anim");
            idle.SampleAnimation(animator.gameObject, 0);
            var names = new[] { "lengyue_upper_arm_l", "lengyue_forearm_l", "lengyue_hand_l",
                "lengyue_upper_arm_r", "lengyue_forearm_r", "lengyue_hand_r" };
            var bones = names.Select(bone).ToArray();
            var positions = bones.Select(value => value.localPosition).ToArray();
            var rotations = bones.Select(value => value.localRotation).ToArray();
            var leftHand = bone("lengyue_hand_l").position;
            var rightHand = bone("lengyue_hand_r").position;
            var curves = new Dictionary<string, AnimationCurve[]>();
            foreach (var value in bones)
                curves.Add(AnimationUtility.CalculateTransformPath(value, animator.transform),
                    Enumerable.Range(0, 7).Select(_ => new AnimationCurve()).ToArray());
            for (var frame = 0; frame <= 30; frame++)
            {
                var t = frame / 30f;
                var amount = Mathf.SmoothStep(0, 1, t);
                for (var i = 0; i < bones.Length; i++)
                    bones[i].SetLocalPositionAndRotation(positions[i], rotations[i]);
                var y = motion == ItemMotion.Reach ? .85f :
                    motion == ItemMotion.Carry ? 1.28f : 1.0f;
                var z = motion == ItemMotion.Carry ? .43f : .44f;
                if (motion == ItemMotion.Cut) y -= .1f * amount;
                var target = character.transform.TransformPoint(new Vector3(.21f, y, z));
                AvatarSeatingRig.Solve(bone("lengyue_upper_arm_r"), bone("lengyue_forearm_r"), bone("lengyue_hand_r"),
                    Vector3.Lerp(rightHand, target, motion == ItemMotion.Carry ? 1 : amount),
                    character.transform.right);
                var left = character.transform.TransformPoint(new Vector3(-.18f,
                    motion == ItemMotion.Reach ? .85f : 1.0f, .30f));
                AvatarSeatingRig.Solve(bone("lengyue_upper_arm_l"), bone("lengyue_forearm_l"), bone("lengyue_hand_l"),
                    Vector3.Lerp(leftHand, left, motion == ItemMotion.Carry ? 1 : amount),
                    -character.transform.right);
                foreach (var value in bones)
                {
                    var keys = curves[AnimationUtility.CalculateTransformPath(value, animator.transform)];
                    var p = value.localPosition; var q = value.localRotation;
                    keys[0].AddKey(t, p.x); keys[1].AddKey(t, p.y); keys[2].AddKey(t, p.z);
                    keys[3].AddKey(t, q.x); keys[4].AddKey(t, q.y);
                    keys[5].AddKey(t, q.z); keys[6].AddKey(t, q.w);
                }
            }
            var path = Actions + "/" + motion + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = motion.ToString(), frameRate = 30 };
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();
            var properties = new[] { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
                "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
            foreach (var pair in curves)
            {
                for (var i = 0; i < 7; i++)
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve(pair.Key, typeof(Transform), properties[i]), pair.Value[i]);
            }
            clip.EnsureQuaternionContinuity();
            for (var i = 0; i < bones.Length; i++)
                bones[i].SetLocalPositionAndRotation(positions[i], rotations[i]);
            EditorUtility.SetDirty(clip);
            return clip;
        }
    }
}
