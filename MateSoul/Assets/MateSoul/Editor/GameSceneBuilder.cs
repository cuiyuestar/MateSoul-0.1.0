using System;
using System.IO;
using System.Linq;
using ByteDance.PICO.XR;
using MateSoul.Animation;
using MateSoul.RoomNavigation;
using MateSoul.RoomNavigation.Editor;
using MateSoul.Runtime;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using Unity.XR.CoreUtils;

namespace MateSoul.Editor
{
    public static class GameSceneBuilder
    {
        private enum CatalogColliderShape { Box, Sphere, Capsule }

        public const string ScenePath = "Assets/MateSoul/Scenes/MateSoul.unity";
        public const string CharacterPath = "Assets/MateSoul/Content/Lengyue_Runtime.prefab";
        public const string PersonaPath = "Assets/MateSoul/Content/LengyuePersona.json";
        private const string Content = "Assets/MateSoul/Content";
        private const string ModelConnectionPath = Content + "/ModelConnection.asset";
        private const string Source = "Assets/Characters/SapphiArtCustom/Lengyue/Runtime";

        [MenuItem("MateSoul/Game/Build Game Scene")]
        public static void BuildScene()
        {
            Directory.CreateDirectory(Content);
            Directory.CreateDirectory(Content + "/Props");
            Directory.CreateDirectory("Assets/MateSoul/Scenes");
            ConfigureInteractionCapabilities();
            RealityOcclusionSetup.Configure();
            PhysicsContentBuilder.ConfigureWorld();
            AssetDatabase.Refresh();
            var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var restore = previous.IsValid() && previous.isLoaded &&
                          !string.IsNullOrEmpty(previous.path) && previous.path != ScenePath;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                restore ? NewSceneMode.Additive : NewSceneMode.Single);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            try
            {
                RoomNavigationSceneBuilder.CreateLighting();
                var rig = RoomNavigationSceneBuilder.CreateCameraRig();
                CreateControllerRays(rig.CameraFloorOffsetObject.transform);
                var character = (GameObject)PrefabUtility.InstantiatePrefab(BuildCharacter());
                character.SetActive(false);
                var root = new GameObject("MateSoul Game");
                var bootstrap = root.AddComponent<GameBootstrap>();
                var focusMaterial = BuildFocusMaterial();
                bootstrap.Configure(character,
                    AssetDatabase.LoadAssetAtPath<TextAsset>("Packages/com.bytedance.pico.xr/Assets/Resources/MockSceneData.json"),
                    BuildCatalog(focusMaterial), AssetDatabase.LoadAssetAtPath<TextAsset>(PersonaPath),
                    BuildModelConnection());
                var miniGames = root.GetComponent<MiniGameHost>();
                miniGames.Configure(new MiniGame[]
                {
                    BowlingContentBuilder.Build()
                });
                EditorUtility.SetDirty(miniGames);
                root.AddComponent<GameHud>().Configure(bootstrap);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                var old = EditorBuildSettings.scenes;
                var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>
                {
                    new EditorBuildSettingsScene(ScenePath, true)
                };
                foreach (var entry in old) if (entry.path != ScenePath) scenes.Add(entry);
                EditorBuildSettings.scenes = scenes.ToArray();
                AssetDatabase.SaveAssets();
                Debug.Log($"MATESOUL_SCENE_BUILT path={ScenePath}");
            }
            finally
            {
                if (restore)
                {
                    UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void ConfigureInteractionCapabilities()
        {
            var settings = PXR_ProjectSetting.GetProjectConfig();
            settings.handTracking = true;
            settings.handTrackingSupportType = HandTrackingSupport.HandsOnly;
            settings.highFrequencyHand = true;
            settings.eyeTracking = true;
            settings.spatialAnchor = true;
            settings.sceneCapture = true;
            settings.spatialMesh = true;
            settings.meshLod = PxrMeshLod.High;
            settings.planeDetection = true;
            settings.environmentDepth = true;
            settings.lightEstimation = true;
            settings.lightEstimationTextureResolution = PxrEnvironmentTextureResolution.Size128x128;
            settings.videoSeeThrough = true;
            // MateSoul uses a captured real room, so startup must adopt PICO's MR safety policy.
            settings.mrSafeguard = true;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        private static ModelConnectionSettings BuildModelConnection()
        {
            var settings = AssetDatabase.LoadAssetAtPath<ModelConnectionSettings>(
                ModelConnectionPath);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<ModelConnectionSettings>();
            settings.Configure(PlayerAIConfiguration.DefaultEndpoint, 25, false);
            AssetDatabase.CreateAsset(settings, ModelConnectionPath);
            return settings;
        }

        private static GameObject BuildCharacter()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath);
            if (existing != null)
            {
                var contents = PrefabUtility.LoadPrefabContents(CharacterPath);
                try
                {
                    // Contact authoring samples clips. Keep that temporary pose from
                    // becoming the prefab's serialized rest pose.
                    var poseTransforms = contents.GetComponentsInChildren<Transform>(true);
                    var posePositions = poseTransforms.Select(t => t.localPosition).ToArray();
                    var poseRotations = poseTransforms.Select(t => t.localRotation).ToArray();
                    var poseScales = poseTransforms.Select(t => t.localScale).ToArray();
                    var plant = contents.GetComponent<AvatarTurnFootPlantRig>();
                    var plantState = plant != null ? new SerializedObject(plant) : null;
                    var leftPlant = plantState?.FindProperty("leftPlantWeight").floatValue ?? 0;
                    var rightPlant = plantState?.FindProperty("rightPlantWeight").floatValue ?? 0;
                    foreach (var guard in contents.GetComponentsInChildren<HeadPoseGuard>(true))
                        guard.enabled = false;
                    var animator = contents.GetComponentInChildren<Animator>(true);
                    var controller = animator.runtimeAnimatorController as AnimatorController;
                    if (controller == null)
                        throw new InvalidOperationException("Production avatar controller is missing.");
                    AvatarLocomotionBuilder.ConfigureProductionController(controller);
                    BindContactRigs(contents);
                    PhysicsContentBuilder.ConfigureCharacter(contents);
                    for (var i = 0; i < poseTransforms.Length; i++)
                    {
                        poseTransforms[i].localPosition = posePositions[i];
                        poseTransforms[i].localRotation = poseRotations[i];
                        poseTransforms[i].localScale = poseScales[i];
                    }
                    if (plantState != null)
                    {
                        plantState.Update();
                        plantState.FindProperty("leftPlantWeight").floatValue = leftPlant;
                        plantState.FindProperty("rightPlantWeight").floatValue = rightPlant;
                        plantState.ApplyModifiedPropertiesWithoutUndo();
                    }
                    return PrefabUtility.SaveAsPrefabAsset(contents, CharacterPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source + "/Prefabs/Lengyue_GameCharacter.prefab");
            if (source == null) throw new InvalidOperationException("Production character prefab is missing.");
            var character = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                character.name = "Lengyue_Runtime";
                foreach (var guard in character.GetComponentsInChildren<HeadPoseGuard>(true)) guard.enabled = false;
                var animator = character.GetComponentInChildren<Animator>(true);
                animator.runtimeAnimatorController = BuildAnimator();
                animator.applyRootMotion = false;
                character.AddComponent<AvatarMotor>();
                PhysicsContentBuilder.ConfigureCharacter(character);
                BindContactRigs(character);
                return PrefabUtility.SaveAsPrefabAsset(character, CharacterPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(character); }
        }

        private static void BindContactRigs(GameObject character)
        {
            var transforms = character.GetComponentInChildren<Animator>(true)
                .GetComponentsInChildren<Transform>(true);
            var names = new[] { "Hips", "Spine", "Leg_L", "Knee_L", "Foot_L", "Toe_L",
                "Leg_R", "Knee_R", "Foot_R", "Toe_R", "UpperArm_L", "Forearm_L", "Hand_L",
                "UpperArm_R", "Forearm_R", "Hand_R", "Index_01_L", "Middle_01_L", "Pinky_01_L",
                "Index_01_R", "Middle_01_R", "Pinky_01_R" };
            var binding = names.Select(name => transforms.Single(item => item.name == name)).ToArray();
            var seating = character.GetComponent<AvatarSeatingRig>() ??
                          character.AddComponent<AvatarSeatingRig>();
            seating.Configure(binding);
            EditorUtility.SetDirty(seating);
            var lying = character.GetComponent<AvatarLyingRig>() ??
                        character.AddComponent<AvatarLyingRig>();
            lying.Configure(binding);
            EditorUtility.SetDirty(lying);
            ConfigureBowlingCharacter(character);
            BindHandHoldRig(character, transforms);
            BindTurnFootRig(character, transforms);
            ItemActionContentBuilder.ConfigureCharacter(character);
        }

        public static void ConfigureBowlingCharacter(GameObject character)
        {
            var motion = AssetDatabase.LoadAssetAtPath<BowlingMotion>(
                "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Bowling/BowlingMotion.asset");
            if (!motion) throw new InvalidOperationException("请先导入冷月保龄球动画。");
            var animator = character.GetComponentInChildren<Animator>(true);
            var controller = animator.runtimeAnimatorController as AnimatorController;
            if (!controller) throw new InvalidOperationException("保龄球需要角色 Animator Controller。");
            var index = Array.FindIndex(controller.layers, layer => layer.name == "Bowling");
            if (index < 0) { controller.AddLayer("Bowling"); index = controller.layers.Length - 1; }
            var layers = controller.layers;
            var layer = layers[index];
            layer.defaultWeight = 0;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            var state = layer.stateMachine.states.Select(child => child.state)
                .FirstOrDefault(value => value.name == "Roll") ?? layer.stateMachine.AddState("Roll");
            state.motion = motion.clip;
            state.speed = 0;
            state.writeDefaultValues = false;
            layer.stateMachine.defaultState = state;
            layers[index] = layer;
            controller.layers = layers;
            var hand = animator.GetComponentsInChildren<Transform>(true)
                .Single(bone => bone.name == "Hand_R");
            var rig = character.GetComponent<AvatarBowlingRig>() ?? character.AddComponent<AvatarBowlingRig>();
            rig.Configure(animator, hand, motion);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(rig);
        }

        private static void BindHandHoldRig(GameObject character, Transform[] transforms)
        {
            Transform Bone(string name) => transforms.Single(item => item.name == name);
            var animator = character.GetComponentInChildren<Animator>(true);
            var handRig = GetOrAdd<HandIKRig>(character);
            var rigRoot = FindOrCreate(character.transform, "HandHold Animation Rig");
            var rig = GetOrAdd<Rig>(rigRoot.gameObject);
            rig.weight = 1;
            var targets = FindOrCreate(character.transform, "HandHold World Targets");
            var material = BuildHandHoldLimitMaterial();

            ConfigureHandArm(handRig, rigRoot, targets, AvatarHandSide.Left,
                Bone("UpperArm_L"), Bone("Forearm_L"), Bone("Hand_L"),
                Bone("Index_01_L"), Bone("Middle_01_L"), Bone("Pinky_01_L"),
                Bone("ForearmTwist_L"), material);
            ConfigureHandArm(handRig, rigRoot, targets, AvatarHandSide.Right,
                Bone("UpperArm_R"), Bone("Forearm_R"), Bone("Hand_R"),
                Bone("Index_01_R"), Bone("Middle_01_R"), Bone("Pinky_01_R"),
                Bone("ForearmTwist_R"), material);
            handRig.Configure(targets, HandHoldTuning.Default);

            var builder = GetOrAdd<RigBuilder>(animator.gameObject);
            builder.layers.RemoveAll(layer => layer.rig == null || layer.rig == rig);
            builder.layers.Add(new RigLayer(rig));
            EditorUtility.SetDirty(builder);
            EditorUtility.SetDirty(handRig);
        }

        private static void BindTurnFootRig(GameObject character, Transform[] transforms)
        {
            Transform Bone(string name) => transforms.Single(
                item => item != null && item.name == name);
            var animator = character.GetComponentInChildren<Animator>(true);
            var motor = character.GetComponent<AvatarMotor>();
            if (motor == null)
                throw new InvalidOperationException("Avatar motor is required for turn foot planting.");

            var oldRigRoot = character.transform.Find("Turn Foot Plant Animation Rig");
            var oldTargets = character.transform.Find("Turn Foot Plant Targets");
            var oldRig = oldRigRoot != null ? oldRigRoot.GetComponent<Rig>() : null;
            var builder = GetOrAdd<RigBuilder>(animator.gameObject);
            builder.layers.RemoveAll(layer => layer.rig == null || layer.rig == oldRig);
            if (oldRigRoot != null) UnityEngine.Object.DestroyImmediate(oldRigRoot.gameObject);
            if (oldTargets != null) UnityEngine.Object.DestroyImmediate(oldTargets.gameObject);

            var controller = GetOrAdd<AvatarTurnFootPlantRig>(character);
            controller.Configure(animator, motor, character.transform,
                Bone("Leg_L"), Bone("Knee_L"), Bone("Foot_L"),
                Bone("Leg_R"), Bone("Knee_R"), Bone("Foot_R"));
            EditorUtility.SetDirty(builder);
            EditorUtility.SetDirty(controller);
        }

        private static void ConfigureHandArm(HandIKRig handRig, Transform rigRoot,
            Transform targetRoot, AvatarHandSide side, Transform upperArm,
            Transform forearm, Transform hand, Transform index, Transform middle,
            Transform pinky, Transform forearmTwist, Material material)
        {
            var label = side == AvatarHandSide.Left ? "Left" : "Right";
            var target = FindOrCreate(targetRoot, label + " Hand Target");
            var hint = FindOrCreate(targetRoot, label + " Elbow Hint");
            target.SetPositionAndRotation(hand.position, hand.rotation);
            var armDirection = (hand.position - upperArm.position).normalized;
            var pole = Vector3.ProjectOnPlane(forearm.position - upperArm.position, armDirection);
            if (pole.sqrMagnitude < .0001f)
                pole = CharacterSide(side) * handRig.transform.right;
            hint.position = upperArm.position + armDirection * .18f + pole.normalized * .22f;

            var constraintRoot = FindOrCreate(rigRoot, label + " Hand IK");
            var constraint = GetOrAdd<TwoBoneIKConstraint>(constraintRoot.gameObject);
            var data = constraint.data;
            data.root = upperArm;
            data.mid = forearm;
            data.tip = hand;
            data.target = target;
            data.hint = hint;
            data.targetPositionWeight = 1;
            data.targetRotationWeight = .8f;
            data.hintWeight = 1;
            data.maintainTargetPositionOffset = false;
            data.maintainTargetRotationOffset = false;
            constraint.data = data;
            constraint.weight = 0;

            // Keep interaction colliders outside the imported skeleton. Unity does not
            // reliably allow components to be added to nested source-prefab bones.
            var socketRoot = FindOrCreate(rigRoot, label + " Hand Hold Socket");
            var direction = middle.position - hand.position;
            var across = index.position - pinky.position;
            var palmNormal = Vector3.Cross(direction, across) *
                             (side == AvatarHandSide.Left ? 1 : -1);
            if (direction.sqrMagnitude < .000001f || palmNormal.sqrMagnitude < .000001f)
                throw new InvalidOperationException($"{label} palm landmarks are degenerate.");
            socketRoot.SetPositionAndRotation(
                hand.position + direction * .42f,
                Quaternion.LookRotation(direction.normalized, palmNormal.normalized));
            var shape = GetOrAdd<SphereCollider>(socketRoot.gameObject);
            shape.radius = .055f;
            shape.isTrigger = true;
            var socket = GetOrAdd<HandHoldInteractable>(socketRoot.gameObject);
            BuildHandHoldSimulatorMarker(socketRoot, label + " Palm Target (Editor Only)",
                .04f, BuildHandHoldSimulatorMaterial("Palm", new Color(.08f, .95f, .3f)));

            var flexionArc = BuildLimitArc(targetRoot, label + " Wrist Flexion", material);
            var deviationArc = BuildLimitArc(targetRoot, label + " Wrist Deviation", material);
            var twistArc = BuildLimitArc(targetRoot, label + " Forearm Twist", material);
            handRig.ConfigureArm(side, upperArm, forearm, hand, index, middle, pinky,
                forearmTwist, target, hint, socket, constraint,
                flexionArc, deviationArc, twistArc);
            EditorUtility.SetDirty(constraint);
            EditorUtility.SetDirty(socket);
        }

        private static float CharacterSide(AvatarHandSide side)
            => side == AvatarHandSide.Left ? -1 : 1;

        private static Transform FindOrCreate(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null) return child;
            var created = new GameObject(name).transform;
            created.SetParent(parent, false);
            return created;
        }

        private static LineRenderer BuildLimitArc(Transform parent, string name, Material material)
        {
            var root = FindOrCreate(parent, name);
            var line = GetOrAdd<LineRenderer>(root.gameObject);
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.widthMultiplier = .0035f;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static Material BuildHandHoldLimitMaterial()
        {
            var path = Content + "/HandHoldLimit.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("Sprites/Default shader is missing.");
            material = new Material(shader) { name = "Hand Hold Limit" };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Material BuildHandHoldSimulatorMaterial(string label, Color color)
        {
            var path = Content + "/HandHoldSimulator" + label + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                    throw new InvalidOperationException("URP Unlit shader is missing.");
                material = new Material(shader) { name = "Hand Hold Simulator " + label };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Renderer BuildHandHoldSimulatorMarker(Transform parent, string name,
            float diameter, Material material)
        {
            var marker = parent.Find(name);
            if (marker == null)
            {
                var primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                primitive.name = name;
                primitive.transform.SetParent(parent, false);
                marker = primitive.transform;
            }
            marker.gameObject.tag = "EditorOnly";
            marker.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
            marker.localPosition = Vector3.zero;
            marker.localRotation = Quaternion.identity;
            marker.localScale = Vector3.one * diameter;
            var collider = marker.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            var renderer = marker.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private static AnimatorController BuildAnimator()
        {
            var controller = AnimatorController.CreateAnimatorControllerAtPath(Content + "/Avatar.controller");
            AvatarLocomotionBuilder.ConfigureProductionController(controller);
            return controller;
        }

        [MenuItem("MateSoul/Game/Rebuild Avatar Turn System")]
        public static void BuildAvatarTurnSystem()
        {
            AvatarLocomotionBuilder.BuildProductionController();
            var contents = PrefabUtility.LoadPrefabContents(CharacterPath);
            try
            {
                var animator = contents.GetComponentInChildren<Animator>(true);
                var transforms = animator.GetComponentsInChildren<Transform>(true);
                BindTurnFootRig(contents, transforms);
                PrefabUtility.SaveAsPrefabAsset(contents, CharacterPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("MATESOUL_TURN_SYSTEM_BUILT prefab=" + CharacterPath);
        }

        private static Material BuildFocusMaterial()
        {
            var path = Content + "/ItemFocusHighlight.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("MateSoul/Item Focus Highlight");
                if (shader == null) throw new InvalidOperationException("Item focus shader is missing.");
                material = new Material(shader) { name = "Item Focus Highlight" };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_Color", new Color(1f, .16f, .48f, .7f));
            material.SetFloat("_OutlineWidth", .003f);
            material.SetFloat("_Intensity", .65f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static ItemDefinition[] BuildCatalog(Material focusMaterial)
        {
            var breakSound = BuildCeramicBreakSound();
            var metalImpact = new ImpactSoundSettings(120f, 350f, 850f,
                BuildMetalImpactSound());
            var plateBreakable = BuildBreakableSettings("Plate",
                Content + "/PropsSource/Plate/LavenderChibiPlate_LightFracture.fbx",
                Content + "/PropsSource/Plate/LavenderChibiPlate_HeavyFracture.fbx",
                45f, 95f, 4, 22, breakSound);
            var plateMediumBreakable = BuildBreakableSettings("PlateMedium",
                Content + "/PropsSource/Plate/LavenderChibiPlate_250x31mm_LightFracture.fbx",
                Content + "/PropsSource/Plate/LavenderChibiPlate_250x31mm_HeavyFracture.fbx",
                56f, 119f, 4, 22, breakSound, "Plate");
            var plateLargeBreakable = BuildBreakableSettings("PlateLarge",
                Content + "/PropsSource/Plate/LavenderChibiPlate_300x38mm_LightFracture.fbx",
                Content + "/PropsSource/Plate/LavenderChibiPlate_300x38mm_HeavyFracture.fbx",
                68f, 143f, 4, 22, breakSound, "Plate");
            var mugBreakable = BuildBreakableSettings("Mug",
                Content + "/PropsSource/Mug/LavenderChibiMug_LightFracture.fbx",
                Content + "/PropsSource/Mug/LavenderChibiMug_HeavyFracture.fbx",
                38f, 80f, 6, 22, breakSound);
            var table = BuildDiningTable(focusMaterial);
            var waterDispenser = WaterDispenserBuilder.Build(focusMaterial);
            var chair = BuildDiningChair(focusMaterial);
            var television = TelevisionBuilder.Build(focusMaterial);
            foreach (var definition in new[] { table, chair, television })
            {
                definition.ConfigureImpactSound(metalImpact);
                EditorUtility.SetDirty(definition);
            }
            var fryingPan = BuildFryingPan(focusMaterial);
            var heatingBase = HeatingBaseBuilder.Build(focusMaterial);
            var electricKettle = ElectricKettleBuilder.Build(focusMaterial);
            var coffeeBeanJar = CoffeeBeanJarBuilder.Build(focusMaterial);
            var coffeeGrinder = CoffeeGrinderBuilder.Build(focusMaterial);
            var condiments = CondimentBuilder.Build(focusMaterial);
            var catalog = new[]
            {
                BuildLamp(focusMaterial),
                table,
                waterDispenser,
                chair,
                television,
                RadioBuilder.Build(focusMaterial),
                BuildCatalogProp("plate", "小号盘子",
                    Content + "/PropsSource/Plate/LavenderChibiPlate_200x25mm.fbx",
                    new Vector3(.2f, .025f, .2f), .42f, PhysicsContentBuilder.Ceramic, true,
                    focusMaterial, ItemCategory.Tableware, breakable: plateBreakable),
                BuildCatalogProp("plate-medium", "中号盘子",
                    Content + "/PropsSource/Plate/LavenderChibiPlate_250x31mm.fbx",
                    new Vector3(.25f, .03125f, .25f), .82f,
                    PhysicsContentBuilder.Ceramic, true, focusMaterial, ItemCategory.Tableware,
                    breakable: plateMediumBreakable, materialGroup: "Plate",
                    blocksNavigation: false),
                BuildCatalogProp("plate-large", "大号盘子",
                    Content + "/PropsSource/Plate/LavenderChibiPlate_300x38mm.fbx",
                    new Vector3(.3f, .0375f, .3f), 1.42f,
                    PhysicsContentBuilder.Ceramic, true, focusMaterial, ItemCategory.Tableware,
                    breakable: plateLargeBreakable, materialGroup: "Plate",
                    blocksNavigation: false),
                BuildCatalogProp("mug", "马克杯",
                    Content + "/PropsSource/Mug/LavenderChibiMug_80x95mm.fbx",
                    new Vector3(.113f, .095f, .08f), .34f, PhysicsContentBuilder.Ceramic, true,
                    focusMaterial, ItemCategory.Tableware, breakable: mugBreakable),
                BuildCatalogProp("bowl", "小号碗",
                    Content + "/PropsSource/Bowl/LavenderChibiBowl_138mm.fbx",
                    new Vector3(.138f, .07f, .138f), .31f, PhysicsContentBuilder.Ceramic, true,
                    focusMaterial, ItemCategory.Tableware, breakable: new BreakableSettings(
                        35f, 72f, sound: breakSound)),
                BuildCatalogProp("bowl-medium", "中号碗",
                    Content + "/PropsSource/Bowl/LavenderChibiBowl_180mm.fbx",
                    new Vector3(.18f, .091304f, .18f), .69f,
                    PhysicsContentBuilder.Ceramic, true, focusMaterial, ItemCategory.Tableware,
                    breakable: new BreakableSettings(46f, 94f, sound: breakSound),
                    materialGroup: "Bowl"),
                BuildCatalogProp("bowl-large", "大号碗",
                    Content + "/PropsSource/Bowl/LavenderChibiBowl_220mm.fbx",
                    new Vector3(.22f, .111594f, .22f), 1.26f,
                    PhysicsContentBuilder.Ceramic, true, focusMaterial, ItemCategory.Tableware,
                    breakable: new BreakableSettings(56f, 115f, sound: breakSound),
                    materialGroup: "Bowl"),
                BuildCatalogProp("playing-cards", "扑克牌",
                    Content + "/PropsSource/PlayingCards/LavenderChibi_CardsAndBox_Packed.fbx",
                    new Vector3(.065f, .0185f, .091f), .11f, PhysicsContentBuilder.Paper, true,
                    focusMaterial, ItemCategory.Toy, CatalogColliderShape.Box, true),
                BuildCatalogProp("spoon", "勺子",
                    Content + "/PropsSource/Spoon/LavenderChibiSpoon_140mm.fbx",
                    new Vector3(.025f, .012f, .14f), .04f, PhysicsContentBuilder.Ceramic, true,
                    focusMaterial, ItemCategory.Tableware),
                BuildCatalogProp("chopstick-left", "左筷子",
                    Content + "/PropsSource/Chopsticks/LavenderChibiChopsticks_230mm.fbx",
                    new Vector3(.007f, .007f, .23f), .02f, PhysicsContentBuilder.Ceramic, true,
                    focusMaterial, ItemCategory.Tableware, sourceObjectName: "Chopstick_Left",
                    catalogVisible: false),
                BuildCatalogProp("chopstick-right", "右筷子",
                    Content + "/PropsSource/Chopsticks/LavenderChibiChopsticks_230mm.fbx",
                    new Vector3(.007f, .007f, .23f), .02f, PhysicsContentBuilder.Ceramic, true,
                    focusMaterial, ItemCategory.Tableware, sourceObjectName: "Chopstick_Right",
                    catalogVisible: false),
                BuildChopsticks(focusMaterial),
                BuildKnife(focusMaterial),
                BuildClock(focusMaterial),
                heatingBase,
                electricKettle[0],
                electricKettle[1],
                electricKettle[2],
                fryingPan[0],
                fryingPan[1],
                fryingPan[2],
                coffeeBeanJar,
                coffeeGrinder[0],
                coffeeGrinder[1],
                coffeeGrinder[2],
                condiments[0],
                condiments[1],
                condiments[2],
                BuildRawSteak(focusMaterial),
                BuildCatalogProp("cupcake", "纸杯蛋糕",
                    Content + "/PropsSource/Cupcake/LavenderChibiCupcake_70x75mm.fbx",
                    new Vector3(.07f, .075f, .07f), .09f, PhysicsContentBuilder.Food, true,
                    focusMaterial, ItemCategory.Food),
                BuildCatalogProp("cookie", "曲奇",
                    "Assets/Experiments/YummyCookie_v001/Props/Cookie.prefab",
                    new Vector3(.05f, .011f, .05f), .025f, PhysicsContentBuilder.Food, true,
                    focusMaterial, ItemCategory.Food),
                BuildCatalogProp("apple", "苹果",
                    Content + "/PropsSource/Apple/GameApple_Full_90x90x103mm.fbx",
                    new Vector3(.09f, .103f, .09f), .16f, PhysicsContentBuilder.Food, true,
                    focusMaterial, ItemCategory.Food),
                CarrotBuilder.Build(focusMaterial),
                BuildCatalogProp("dartboard", "飞镖盘",
                    Content + "/PropsSource/Dartboard/LavenderChibiDartboard_400x400x48mm.fbx",
                    new Vector3(.4f, .4f, .048f), 2.2f, PhysicsContentBuilder.Wood, false,
                    focusMaterial, ItemCategory.Toy),
                BuildCatalogProp("bowling-ball", "保龄球",
                    Content + "/PropsSource/Bowling/LavenderChibiBowlingBall_218mm.fbx",
                    Vector3.one * .2183f, 6.5f, PhysicsContentBuilder.BowlingBall, false,
                    focusMaterial, ItemCategory.Toy, CatalogColliderShape.Sphere),
                BuildCatalogProp("bowling-pin", "保龄球瓶",
                    Content + "/PropsSource/Bowling/LavenderChibiBowlingPin_380mm.fbx",
                    new Vector3(.121f, .38f, .121f), 1.55f, PhysicsContentBuilder.BowlingPin,
                    false, focusMaterial, ItemCategory.Toy, CatalogColliderShape.Capsule),
                BuildCatalogProp("plush-doll", "冷月玩偶",
                    Content + "/PropsSource/PlushDoll/Lengyue_PlushDoll_300mm.fbx",
                    new Vector3(.2116f, .3f, .2015f), .38f, PhysicsContentBuilder.Fabric, true,
                    focusMaterial, ItemCategory.Toy),
                BuildCatalogProp("remote-control", "遥控器",
                    Content + "/PropsSource/RemoteControl/LavenderRetroRemote_50x18x140mm.fbx",
                    new Vector3(.05f, .018f, .14f), .13f, PhysicsContentBuilder.Plastic, true,
                    focusMaterial, ItemCategory.Appliance, CatalogColliderShape.Box, true)
            };
            WaterDispenserBuilder.ConfigureReceivers(catalog);
            CoffeeGrinderBuilder.ConfigureBrewingReceivers(catalog);
            foreach (var definition in catalog)
            {
                definition.ConfigureInventory(definition.CatalogVisible ? 1 : 0);
                definition.ConfigurePlacement(false);
                EditorUtility.SetDirty(definition);
            }
            ConfigureItemPolicy(catalog, "companion-lamp", 0, visible: false);
            foreach (var id in new[]
                     {
                         "plate", "plate-medium", "plate-large",
                         "bowl", "bowl-medium", "bowl-large",
                         "spoon", "chopsticks", "mug"
                     })
                ConfigureItemPolicy(catalog, id, 2);
            ConfigureItemPolicy(catalog, "apple", 5, daily: true);
            ConfigureItemPolicy(catalog, "carrot", 5, daily: true);
            ConfigureItemPolicy(catalog, "table", 1, fixedAfterPlacement: true,
                placementPreview: true, immovableAfterPlacement: true);
            ConfigureItemPolicy(catalog, "water-dispenser", 1,
                fixedAfterPlacement: true, placementPreview: true,
                immovableAfterPlacement: true);
            ConfigureItemPolicy(catalog, "chair", 1,
                fixedAfterPlacement: true, placementPreview: true, visible: true,
                immovableAfterPlacement: true);
            ConfigureItemPolicy(catalog, "television", 1,
                fixedAfterPlacement: true, placementPreview: true,
                immovableAfterPlacement: true);
            ConfigureItemPolicy(catalog, "clock", 1,
                fixedAfterPlacement: true, placementPreview: true,
                placementSurface: ItemPlacementSurface.Wall,
                immovableAfterPlacement: true);
            ConfigureItemPolicy(catalog, "dartboard", 1,
                fixedAfterPlacement: true, placementPreview: true,
                placementSurface: ItemPlacementSurface.Wall,
                immovableAfterPlacement: true);
            ConfigureItemPolicy(catalog, "heating-base", 1,
                fixedAfterPlacement: true);
            ConfigureItemPolicy(catalog, "electric-kettle-base", 0,
                fixedAfterPlacement: true);
            ConfigureItemPolicy(catalog, "electric-kettle", 1);
            ConfigureItemPolicy(catalog, "coffee-bean-jar", 2);
            ConfigureItemPolicy(catalog, "coffee-bean-jar", 2);
            var rawSteak = catalog.Single(item => item.Id == "raw-steak");
            rawSteak.ConfigureUnlimitedInventory();
            EditorUtility.SetDirty(rawSteak);
            return ItemActionContentBuilder.ConfigureCatalog(catalog);
        }

        private static void ConfigureItemPolicy(ItemDefinition[] catalog, string id, int limit,
            bool daily = false, bool fixedAfterPlacement = false,
            bool placementPreview = false, bool? visible = null,
            ItemPlacementSurface placementSurface = ItemPlacementSurface.Ground,
            bool immovableAfterPlacement = false)
        {
            var definition = catalog.Single(item => item.Id == id);
            definition.Configure(definition.Id, definition.DisplayName, definition.Prefab,
                definition.Capabilities, visible ?? definition.CatalogVisible,
                definition.Footprint, definition.PlacementHeight, definition.FocusMaterial,
                definition.Category);
            definition.ConfigureInventory(limit, daily);
            definition.ConfigurePlacement(
                fixedAfterPlacement, placementPreview, placementSurface,
                immovableAfterPlacement);
            if (placementSurface == ItemPlacementSurface.Wall)
                definition.ConfigurePhysics(definition.Physics, false);
            EditorUtility.SetDirty(definition);
        }

        private static ItemDefinition BuildChopsticks(Material focusMaterial)
        {
            var sourcePath = Content +
                             "/PropsSource/Chopsticks/LavenderChibiChopsticks_230mm.fbx";
            var group = BuildCatalogProp("chopsticks", "筷子", sourcePath,
                new Vector3(.007f, .007f, .23f), .04f, PhysicsContentBuilder.Ceramic, true,
                focusMaterial, ItemCategory.Tableware, sourceObjectName: "Chopstick_Left");
            var left = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                Content + "/Props/ChopstickLeft.asset");
            var right = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                Content + "/Props/ChopstickRight.asset");
            if (left == null || right == null)
                throw new InvalidOperationException("筷子组合缺少左右独立定义。");
            group.Configure(group.Id, group.DisplayName, group.Prefab, group.Capabilities, true,
                new Vector2(.016f, .23f), .007f, focusMaterial, ItemCategory.Tableware);
            group.ConfigurePlacementParts(
                new ItemPlacementPart(left, Vector3.left * .0045f, Quaternion.identity),
                new ItemPlacementPart(right, Vector3.right * .0045f, Quaternion.identity));
            EditorUtility.SetDirty(group);
            return group;
        }

        private static ItemDefinition[] BuildFryingPan(Material focusMaterial)
        {
            const string root = Content + "/PropsSource/CookingSet/";
            ConfigureNormalMapTexture(root + "brushed_stainless_normal.png");
            ConfigureNormalMapTexture(root + "nonstick_normal.png");
            var panBody = BuildCatalogProp("frying-pan-body", "平底锅",
                root + "StainlessFryingPan_280mm.fbx",
                new Vector3(.553192f, .102617f, .28f), 1.3f,
                PhysicsContentBuilder.Metal, true, focusMaterial, ItemCategory.Kitchenware,
                catalogVisible: false, materialGroup: "CookingSet",
                blocksNavigation: false);
            var spatula = BuildCatalogProp("spatula", "锅铲",
                root + "SlottedSpatula_370mm.fbx",
                new Vector3(.369953f, .051475f, .099987f), .18f,
                PhysicsContentBuilder.Metal, true, focusMaterial, ItemCategory.Kitchenware,
                catalogVisible: false, materialGroup: "CookingSet",
                blocksNavigation: false);
            var group = BuildCatalogProp("frying-pan", "平底锅",
                root + "StainlessFryingPan_280mm.fbx",
                new Vector3(.553192f, .102617f, .28f), 1.3f,
                PhysicsContentBuilder.Metal, true, focusMaterial, ItemCategory.Kitchenware,
                materialGroup: "CookingSet", blocksNavigation: false);
            ConfigureFryingPanColliders(panBody);
            ConfigureFryingPanColliders(group);
            var spatulaRotation = Quaternion.Euler(0, -12f, 0);
            var spatulaPosition = CalculateSpatulaOnPanPosition(
                panBody.Prefab, spatula.Prefab, spatulaRotation);
            group.ConfigurePlacementParts(
                new ItemPlacementPart(panBody, Vector3.zero, Quaternion.identity),
                new ItemPlacementPart(spatula, spatulaPosition, spatulaRotation));
            EditorUtility.SetDirty(group);
            return new[] { panBody, spatula, group };
        }

        private static Vector3 CalculateSpatulaOnPanPosition(
            GameObject panPrefab, GameObject spatulaPrefab, Quaternion spatulaRotation)
        {
            var panPath = AssetDatabase.GetAssetPath(panPrefab);
            var spatulaPath = AssetDatabase.GetAssetPath(spatulaPrefab);
            var pan = PrefabUtility.LoadPrefabContents(panPath);
            var spatula = PrefabUtility.LoadPrefabContents(spatulaPath);
            try
            {
                var panBody = pan.GetComponentsInChildren<Renderer>(true)
                    .FirstOrDefault(renderer => renderer.name == "FryingPan_Body");
                var blade = spatula.GetComponentsInChildren<Renderer>(true)
                    .FirstOrDefault(renderer => renderer.name == "Spatula_SlottedBlade");
                var spatulaRenderers = spatula.GetComponentsInChildren<Renderer>(true);
                if (panBody == null || blade == null || spatulaRenderers.Length == 0)
                    throw new InvalidOperationException(
                        "平底锅或锅铲模型缺少组合定位所需的渲染节点。");
                var panCenter = pan.transform.InverseTransformPoint(panBody.bounds.center);
                var bladeCenter = spatula.transform.InverseTransformPoint(blade.bounds.center);
                var spatulaMinimumY = spatulaRenderers.Min(
                    renderer => spatula.transform.InverseTransformPoint(
                        renderer.bounds.min).y);
                var position = panCenter - spatulaRotation * bladeCenter;
                position.y = pan.transform.InverseTransformPoint(panBody.bounds.min).y +
                             .064f - spatulaMinimumY;
                return position;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(spatula);
                PrefabUtility.UnloadPrefabContents(pan);
            }
        }

        private static void ConfigureFryingPanColliders(ItemDefinition definition)
            => FryingPanColliderBuilder.Configure(definition);

        private static void AddBoxCollider(GameObject root, Vector3 center, Vector3 size)
        {
            var collider = root.AddComponent<BoxCollider>();
            collider.center = center;
            collider.size = size;
        }

        private static ItemDefinition BuildRawSteak(Material focusMaterial)
        {
            const string root = Content + "/PropsSource/CookingSet/";
            var cookingEffects = CookingEffectsAssetBuilder.Build();
            ConfigureNormalMapTexture(root + "steak_raw_atlas_normal.png");
            ConfigureNormalMapTexture(root + "steak_cooked_atlas_normal.png");
            var definition = BuildCatalogProp("raw-steak", "生牛排",
                root + "RibeyeSteak_Raw_180x135x28mm.fbx",
                new Vector3(.18f, .028f, .135f), .35f,
                PhysicsContentBuilder.Food, true, focusMaterial, ItemCategory.Food,
                materialGroup: "CookingSet", blocksNavigation: false);
            definition.ConfigureCooking(new CookingSettings(.025f));
            EditorUtility.SetDirty(definition);

            var cookedSource = AssetDatabase.LoadAssetAtPath<GameObject>(
                root + "RibeyeSteak_Cooked_180x135x28mm.fbx");
            if (cookedSource == null)
                throw new InvalidOperationException("熟牛排模型导入失败。");

            var path = AssetDatabase.GetAssetPath(definition.Prefab);
            var prefabRoot = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var rawModel = prefabRoot.transform.Find("Model");
                if (rawModel == null)
                    throw new InvalidOperationException("生牛排 Prefab 缺少模型根节点。");
                var cookedModel = (GameObject)PrefabUtility.InstantiatePrefab(
                    cookedSource, prefabRoot.transform);
                cookedModel.name = "Cooked Model";
                cookedModel.transform.localPosition = rawModel.localPosition;
                cookedModel.transform.localRotation = rawModel.localRotation;
                cookedModel.transform.localScale = rawModel.localScale;
                var cookedRenderers =
                    cookedModel.GetComponentsInChildren<Renderer>(true);
                if (cookedRenderers.Length == 0)
                    throw new InvalidOperationException("熟牛排模型没有渲染网格。");
                foreach (var renderer in cookedRenderers)
                    renderer.sharedMaterials = renderer.sharedMaterials
                        .Select(material => BuildCatalogMaterial(
                            "CookingSet", material)).ToArray();
                cookedModel.SetActive(false);
                var food = prefabRoot.GetComponent<CookableFood>() ??
                           prefabRoot.AddComponent<CookableFood>();
                food.ConfigureVisual(rawModel.gameObject, cookedModel);
                var effects = prefabRoot.GetComponent<CookingEffects>() ??
                              prefabRoot.AddComponent<CookingEffects>();
                effects.Configure(
                    cookingEffects.FoodSizzle,
                    cookingEffects.SmokeMaterial);
                var replyTrigger =
                    prefabRoot.GetComponent<CookingSpontaneousReplyTrigger>() ??
                    prefabRoot.AddComponent<CookingSpontaneousReplyTrigger>();
                replyTrigger.Configure(
                    "cooking.raw-steak.aroma",
                    "生牛排刚刚达到可见熟制阶段，空气中出现了明显的煎牛排香气。",
                    new[]
                    {
                        "冷月说自己闻到了很香的气味。",
                        "冷月询问玩家是否正在煎牛排。",
                        "只说一句简短、自然的中文。"
                    },
                    new[]
                    {
                        "不要断言玩家一定在煎牛排，要保留询问语气。",
                        "不要提及 AI、系统提示、熟度数值或内部机制。",
                        "不要请求动作、工具或外部信息。"
                    });
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefabRoot); }
            return definition;
        }

        private static AudioClip BuildCeramicBreakSound()
        {
            const string path = Content + "/Audio/CeramicBreak.mp3";
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
                throw new InvalidOperationException($"陶瓷破碎音效导入失败：{path}");
            var sample = importer.defaultSampleSettings;
            var changed = !importer.forceToMono || !sample.preloadAudioData ||
                          importer.loadInBackground ||
                          sample.loadType != AudioClipLoadType.DecompressOnLoad ||
                          sample.compressionFormat != AudioCompressionFormat.PCM ||
                          sample.sampleRateSetting != AudioSampleRateSetting.OptimizeSampleRate;
            if (changed)
            {
                importer.forceToMono = true;
                importer.loadInBackground = false;
                sample.preloadAudioData = true;
                sample.loadType = AudioClipLoadType.DecompressOnLoad;
                sample.compressionFormat = AudioCompressionFormat.PCM;
                sample.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;
                importer.defaultSampleSettings = sample;
                importer.SaveAndReimport();
            }
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
                throw new InvalidOperationException($"陶瓷破碎音效加载失败：{path}");
            return clip;
        }

        private static AudioClip BuildMetalImpactSound()
        {
            const string path = Content + "/Audio/MetalHeavyImpact.wav";
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
                throw new InvalidOperationException($"金属重物碰撞音效导入失败：{path}");
            var sample = importer.defaultSampleSettings;
            importer.forceToMono = true;
            importer.loadInBackground = false;
            sample.preloadAudioData = true;
            sample.loadType = AudioClipLoadType.DecompressOnLoad;
            sample.compressionFormat = AudioCompressionFormat.PCM;
            sample.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;
            importer.defaultSampleSettings = sample;
            importer.SaveAndReimport();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
                throw new InvalidOperationException($"金属重物碰撞音效加载失败：{path}");
            return clip;
        }

        private static BreakableSettings BuildBreakableSettings(string itemName,
            string lightSourcePath, string heavySourcePath, float lightForce,
            float heavyForce, int lightCount, int heavyCount, AudioClip breakSound,
            string materialGroup = null)
        {
            var light = BuildFracturePrefab(itemName, "Light", lightSourcePath, lightCount,
                materialGroup);
            var heavy = BuildFracturePrefab(itemName, "Heavy", heavySourcePath, heavyCount,
                materialGroup);
            return new BreakableSettings(lightForce, heavyForce, lightCount, heavyCount,
                lightPrefab: light, heavyPrefab: heavy, sound: breakSound);
        }

        private static GameObject BuildFracturePrefab(string itemName, string severity,
            string sourcePath, int expectedCount, string materialGroup = null)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
                throw new InvalidOperationException($"破碎模型导入失败：{sourcePath}");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                root.name = itemName + severity + "Fracture";
                var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length != expectedCount)
                    throw new InvalidOperationException(
                        $"{root.name} 包含 {renderers.Length} 个碎块，期望 {expectedCount} 个。");
                foreach (var renderer in renderers)
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                        throw new InvalidOperationException(
                            $"{root.name} 包含没有网格的碎块：{renderer.name}");
                    renderer.sharedMaterials = renderer.sharedMaterials
                        .Select(material => BuildCatalogMaterial(
                            materialGroup ?? itemName, material)).ToArray();
                }
                foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(collider);
                foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
                    UnityEngine.Object.DestroyImmediate(body);

                var directory = Content + "/Props/Breakables";
                Directory.CreateDirectory(directory);
                return PrefabUtility.SaveAsPrefabAsset(root,
                    $"{directory}/{itemName}{severity}Fracture.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static ItemDefinition BuildLamp(Material focusMaterial)
        {
            var path = Content + "/CompanionLamp.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (existing != null)
            {
                existing.Configure("companion-lamp", "陪伴灯", existing.Prefab, new[] { "visit" },
                    true, new Vector2(.42f, .42f), .66f, focusMaterial,
                    ItemCategory.Appliance);
                existing.ConfigurePhysics(new BodySettings(1.2f, PhysicsContentBuilder.Metal), true);
                var contents = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(existing.Prefab));
                try
                {
                    PhysicsContentBuilder.ConfigureBody(contents, existing.Physics);
                    PrefabUtility.SaveAsPrefabAsset(contents, AssetDatabase.GetAssetPath(existing.Prefab));
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                EditorUtility.SetDirty(existing);
                return existing;
            }
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = new Color(.38f, .65f, .73f);
            material.SetFloat("_Smoothness", .65f);
            AssetDatabase.CreateAsset(material, Content + "/Lamp.mat");
            var root = new GameObject("Companion Lamp");
            try
            {
                var baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                baseObject.transform.SetParent(root.transform, false);
                baseObject.transform.localPosition = new Vector3(0, .2f, 0);
                baseObject.transform.localScale = new Vector3(.28f, .2f, .28f);
                baseObject.GetComponent<Renderer>().sharedMaterial = material;
                var shade = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                shade.transform.SetParent(root.transform, false);
                shade.transform.localPosition = new Vector3(0, .48f, 0);
                shade.transform.localScale = Vector3.one * .36f;
                shade.GetComponent<Renderer>().sharedMaterial = material;
                var obstacle = root.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = new Vector3(0, .33f, 0);
                obstacle.size = new Vector3(.42f, .66f, .42f);
                obstacle.carving = true;
                PhysicsContentBuilder.ConfigureBody(root, new BodySettings(1.2f, PhysicsContentBuilder.Metal));
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, Content + "/CompanionLamp.prefab");
                var definition = ScriptableObject.CreateInstance<ItemDefinition>();
                definition.Configure("companion-lamp", "陪伴灯", prefab, new[] { "visit" },
                    true, new Vector2(.42f, .42f), .66f, focusMaterial,
                    ItemCategory.Appliance);
                definition.ConfigurePhysics(new BodySettings(1.2f, PhysicsContentBuilder.Metal), true);
                AssetDatabase.CreateAsset(definition, path);
                return definition;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static ItemDefinition BuildProp(string id, string title, string sourcePath,
            Vector3 dimensions, Material focusMaterial)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(
                Content + "/PropsSource/" + sourcePath);
            if (model == null) throw new InvalidOperationException($"{title}模型导入失败。");

            var root = new GameObject(title);
            try
            {
                var view = (GameObject)PrefabUtility.InstantiatePrefab(model);
                view.name = "Model";
                view.transform.SetParent(root.transform, false);
                var renderers = view.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) throw new InvalidOperationException($"{title}模型没有渲染网格。");
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                var importedSize = bounds.size;
                if (Mathf.Abs(importedSize.x - dimensions.x) > .05f ||
                    Mathf.Abs(importedSize.y - dimensions.y) > .05f ||
                    Mathf.Abs(importedSize.z - dimensions.z) > .05f)
                    throw new InvalidOperationException(
                        $"{title}导入尺寸错误：{importedSize}，期望 {dimensions}。");
                view.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);

                PhysicsContentBuilder.FurnitureShapes(root, id == "chair", dimensions);
                var physics = new BodySettings(id == "chair" ? 4.5f : 8f, PhysicsContentBuilder.Wood);
                PhysicsContentBuilder.ConfigureBody(root, physics);
                var obstacle = root.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = Vector3.up * dimensions.y * .5f;
                obstacle.size = dimensions;
                obstacle.carving = true;

                var prefabPath = Content + "/Props/" + char.ToUpperInvariant(id[0]) + id.Substring(1) + ".prefab";
                Directory.CreateDirectory(Content + "/Props");
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                var definitionPath = Content + "/Props/" + char.ToUpperInvariant(id[0]) +
                                     id.Substring(1) + ".asset";
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(definitionPath);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ItemDefinition>();
                    AssetDatabase.CreateAsset(definition, definitionPath);
                }
                definition.Configure(id, title, prefab, new[] { "visit" }, true,
                    new Vector2(dimensions.x, dimensions.z), dimensions.y, focusMaterial,
                    ItemCategory.Furniture);
                definition.ConfigurePhysics(physics);
                EditorUtility.SetDirty(definition);
                return definition;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static ItemDefinition BuildDiningTable(Material focusMaterial)
        {
            const string sourcePath = Content +
                                      "/PropsSource/Table/LavenderGinghamDiningTable_1200x700x755mm.fbx";
            ConfigureNormalMapTexture(
                Content + "/PropsSource/Table/tablecloth_weave_normal.png");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (model == null) throw new InvalidOperationException("餐桌模型导入失败。");

            var visualSize = new Vector3(1.2811f, .7547f, .7712f);
            var bodySize = new Vector3(1.2f, .75f, .7f);
            var root = new GameObject("餐桌");
            try
            {
                var view = (GameObject)PrefabUtility.InstantiatePrefab(model);
                view.name = "Model";
                view.transform.SetParent(root.transform, false);
                var renderers = view.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new InvalidOperationException("餐桌模型没有渲染网格。");
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                var importedSize = bounds.size;
                var tolerance = new Vector3(.015f, .008f, .015f);
                if (Mathf.Abs(importedSize.x - visualSize.x) > tolerance.x ||
                    Mathf.Abs(importedSize.y - visualSize.y) > tolerance.y ||
                    Mathf.Abs(importedSize.z - visualSize.z) > tolerance.z)
                    throw new InvalidOperationException(
                        $"餐桌导入尺寸错误：{importedSize}，期望 {visualSize}。");
                view.transform.position += new Vector3(
                    -bounds.center.x,
                    -bounds.min.y,
                    -bounds.center.z);
                foreach (var renderer in renderers)
                    renderer.sharedMaterials = renderer.sharedMaterials
                        .Select(material => BuildCatalogMaterial("Table", material)).ToArray();

                PhysicsContentBuilder.FurnitureShapes(root, false, bodySize);
                var physics = new BodySettings(18f, PhysicsContentBuilder.Wood);
                PhysicsContentBuilder.ConfigureBody(root, physics);
                var obstacle = root.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = Vector3.up * bodySize.y * .5f;
                obstacle.size = bodySize;
                obstacle.carving = true;

                var prefabPath = Content + "/Props/Table.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                var definitionPath = Content + "/Props/Table.asset";
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(definitionPath);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ItemDefinition>();
                    AssetDatabase.CreateAsset(definition, definitionPath);
                }
                definition.Configure("table", "餐桌", prefab, new[] { "visit" }, true,
                    new Vector2(bodySize.x, bodySize.z), visualSize.y, focusMaterial,
                    ItemCategory.Furniture);
                definition.ConfigurePhysics(physics);
                definition.ConfigurePlacementParts();
                EditorUtility.SetDirty(definition);
                return definition;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static ItemDefinition BuildDiningChair(Material focusMaterial)
        {
            const string sourcePath = Content +
                                      "/PropsSource/Chair/NaturalOakDiningChair_480x505x820mm.fbx";
            ConfigureNormalMapTexture(Content + "/PropsSource/Chair/chair_oak_normal.png");
            ConfigureNormalMapTexture(Content + "/PropsSource/Chair/chair_cushion_normal.png");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (model == null) throw new InvalidOperationException("餐椅模型导入失败。");

            var visualSize = new Vector3(.482f, .82f, .5109f);
            var bodySize = new Vector3(.48f, .82f, .505f);
            var root = new GameObject("餐椅");
            try
            {
                var view = (GameObject)PrefabUtility.InstantiatePrefab(model);
                view.name = "Model";
                view.transform.SetParent(root.transform, false);
                var renderers = view.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new InvalidOperationException("餐椅模型没有渲染网格。");
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                var importedSize = bounds.size;
                var tolerance = new Vector3(.010f, .008f, .010f);
                if (Mathf.Abs(importedSize.x - visualSize.x) > tolerance.x ||
                    Mathf.Abs(importedSize.y - visualSize.y) > tolerance.y ||
                    Mathf.Abs(importedSize.z - visualSize.z) > tolerance.z)
                    throw new InvalidOperationException(
                        $"餐椅导入尺寸错误：{importedSize}，期望 {visualSize}。");
                view.transform.position += new Vector3(
                    -bounds.center.x,
                    -bounds.min.y,
                    -bounds.center.z);
                foreach (var renderer in renderers)
                    renderer.sharedMaterials = renderer.sharedMaterials
                        .Select(material => BuildCatalogMaterial("Chair", material)).ToArray();

                PhysicsContentBuilder.FurnitureShapes(root, true, bodySize);
                var physics = new BodySettings(5.5f, PhysicsContentBuilder.Wood);
                PhysicsContentBuilder.ConfigureBody(root, physics);
                var obstacle = root.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = Vector3.up * bodySize.y * .5f;
                obstacle.size = bodySize;
                obstacle.carving = true;

                var prefabPath = Content + "/Props/Chair.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                var definitionPath = Content + "/Props/Chair.asset";
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(definitionPath);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ItemDefinition>();
                    AssetDatabase.CreateAsset(definition, definitionPath);
                }
                definition.Configure("chair", "餐椅", prefab, new[] { "visit" }, true,
                    new Vector2(bodySize.x, bodySize.z), visualSize.y, focusMaterial,
                    ItemCategory.Furniture);
                definition.ConfigurePhysics(physics);
                definition.ConfigurePlacementParts();
                EditorUtility.SetDirty(definition);
                return definition;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void ConfigureNormalMapTexture(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException($"法线贴图导入失败：{path}");
            if (importer.textureType == TextureImporterType.NormalMap) return;
            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false;
            importer.SaveAndReimport();
        }

        private static ItemDefinition BuildCatalogProp(string id, string title, string sourcePath,
            Vector3 dimensions, float mass, PhysicMaterial physicsMaterial, bool allowSurfacePlacement,
            Material focusMaterial, ItemCategory category,
            CatalogColliderShape colliderShape = CatalogColliderShape.Box,
            bool layFlat = false, string sourceObjectName = null, bool catalogVisible = true,
            BreakableSettings breakable = default, string materialGroup = null,
            bool? blocksNavigation = null)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null) throw new InvalidOperationException($"{title}模型导入失败：{sourcePath}");
            var stem = string.Concat(id.Split('-').Select(part =>
                char.ToUpperInvariant(part[0]) + part.Substring(1)));

            var root = new GameObject(title);
            try
            {
                var view = (GameObject)PrefabUtility.InstantiatePrefab(source);
                view.name = "Model";
                view.transform.SetParent(root.transform, false);
                var renderers = view.GetComponentsInChildren<Renderer>(true);
                if (!string.IsNullOrEmpty(sourceObjectName))
                {
                    var selected = view.GetComponentsInChildren<Transform>(true)
                        .SingleOrDefault(candidate => candidate.name == sourceObjectName);
                    if (selected == null)
                        throw new InvalidOperationException(
                            $"{title}模型缺少独立部件：{sourceObjectName}");
                    foreach (var renderer in renderers)
                        if (renderer.transform != selected &&
                            !renderer.transform.IsChildOf(selected))
                            UnityEngine.Object.DestroyImmediate(renderer.gameObject);
                    renderers = selected.GetComponentsInChildren<Renderer>(true);
                }
                if (renderers.Length == 0) throw new InvalidOperationException($"{title}模型没有渲染网格。");
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                var center = bounds.center;
                var importedSize = bounds.size;
                if (layFlat)
                {
                    var rotation = Quaternion.Euler(90, 0, 0);
                    view.transform.localRotation = rotation * view.transform.localRotation;
                    center = rotation * center;
                    importedSize = new Vector3(importedSize.x, importedSize.z, importedSize.y);
                }
                var tolerance = Vector3.Max(dimensions * .08f, Vector3.one * .006f);
                if (Mathf.Abs(importedSize.x - dimensions.x) > tolerance.x ||
                    Mathf.Abs(importedSize.y - dimensions.y) > tolerance.y ||
                    Mathf.Abs(importedSize.z - dimensions.z) > tolerance.z)
                    throw new InvalidOperationException(
                        $"{title}导入尺寸错误：{importedSize}，期望 {dimensions}。");
                view.transform.position += new Vector3(-center.x,
                    -(center.y - importedSize.y * .5f), -center.z);
                foreach (var renderer in renderers)
                    renderer.sharedMaterials = renderer.sharedMaterials
                        .Select(material => BuildCatalogMaterial(
                            materialGroup ?? stem, material)).ToArray();

                var colliderCenter = Vector3.up * dimensions.y * .5f;
                if (colliderShape == CatalogColliderShape.Sphere)
                {
                    var sphere = root.AddComponent<SphereCollider>();
                    sphere.center = colliderCenter;
                    sphere.radius = Mathf.Max(dimensions.x, dimensions.z) * .5f;
                }
                else if (colliderShape == CatalogColliderShape.Capsule)
                {
                    var capsule = root.AddComponent<CapsuleCollider>();
                    capsule.center = colliderCenter;
                    capsule.radius = Mathf.Max(dimensions.x, dimensions.z) * .5f;
                    capsule.height = dimensions.y;
                    capsule.direction = 1;
                }
                else
                {
                    var box = root.AddComponent<BoxCollider>();
                    box.center = colliderCenter;
                    box.size = dimensions;
                }
                var physics = new BodySettings(mass, physicsMaterial);
                PhysicsContentBuilder.ConfigureBody(root, physics);
                var shouldBlockNavigation = blocksNavigation ??
                                            Mathf.Max(dimensions.x, dimensions.z) >= .25f;
                if (shouldBlockNavigation)
                {
                    var obstacle = root.AddComponent<NavMeshObstacle>();
                    obstacle.shape = NavMeshObstacleShape.Box;
                    obstacle.center = colliderCenter;
                    obstacle.size = dimensions;
                    obstacle.carving = true;
                }

                var prefabPath = Content + "/Props/" + stem + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                var definitionPath = Content + "/Props/" + stem + ".asset";
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(definitionPath);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ItemDefinition>();
                    AssetDatabase.CreateAsset(definition, definitionPath);
                }
                definition.Configure(id, title, prefab, new[] { "visit" }, catalogVisible,
                    new Vector2(dimensions.x, dimensions.z), dimensions.y, focusMaterial,
                    category);
                definition.ConfigurePhysics(physics, allowSurfacePlacement);
                if (breakable.enabled) definition.ConfigureBreakable(breakable);
                else definition.ClearBreakable();
                definition.ConfigurePlacementParts();
                EditorUtility.SetDirty(definition);
                return definition;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static Material BuildCatalogMaterial(string itemName, Material source)
        {
            if (source == null) return null;
            var directory = Content + "/Props/Materials/" + itemName;
            Directory.CreateDirectory(directory);
            var safeName = string.Join("_", source.name.Split(Path.GetInvalidFileNameChars()));
            var path = directory + "/" + safeName + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is missing.");
            if (material == null)
            {
                material = new Material(shader) { name = source.name };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;

            var fractureSurface = source.name.StartsWith(
                "MAT_Ceramic_Fracture", StringComparison.Ordinal);
            var color = fractureSurface
                ? new Color(.88f, .82f, .94f, 1f)
                : source.HasProperty("_BaseColor")
                ? source.GetColor("_BaseColor") : source.color;
            material.SetColor("_BaseColor", color);
            var baseMap = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") :
                source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
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
            if (source.name == "MAT_Cookware_BrushedStainless")
                material.SetFloat("_Metallic", 1f);
            if (source.HasProperty("_Glossiness"))
                material.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));
            if (fractureSurface)
            {
                material.SetFloat("_Smoothness", .28f);
                if (material.HasProperty("_EmissionColor"))
                {
                    material.SetColor("_EmissionColor", color * .12f);
                    material.EnableKeyword("_EMISSION");
                }
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static ItemDefinition BuildKnife(Material focusMaterial)
        {
            return BuildCatalogProp("knife", "刀",
                Content + "/PropsSource/Knife/LavenderChefKnife_330mm.fbx",
                new Vector3(.05f, .024f, .33f), .24f, PhysicsContentBuilder.Metal, true,
                focusMaterial, ItemCategory.Kitchenware, CatalogColliderShape.Box, true);
        }

        private static ItemDefinition BuildClock(Material focusMaterial)
        {
            var definition = BuildCatalogProp("clock", "时钟",
                Content + "/PropsSource/Clock/LavenderChibiClock_160x40mm.fbx",
                new Vector3(.16f, .16f, .04f), .42f, PhysicsContentBuilder.Plastic, true,
                focusMaterial, ItemCategory.Appliance);
            var path = AssetDatabase.GetAssetPath(definition.Prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var transforms = root.GetComponentsInChildren<Transform>(true);
                var hour = transforms.SingleOrDefault(item =>
                    item.name == "Clock_HourHand_Pivot");
                var minute = transforms.SingleOrDefault(item =>
                    item.name == "Clock_MinuteHand_Pivot");
                if (hour == null || minute == null)
                    throw new InvalidOperationException("时钟模型缺少独立时针或分针枢轴。");
                ConfigureClockNumbers(root);
                var clock = root.GetComponent<RealTimeClock>() ??
                            root.AddComponent<RealTimeClock>();
                clock.Configure(hour, minute);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return definition;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void ConfigureClockNumbers(GameObject root)
        {
            var previous = root.transform.Find("Clock Numbers");
            if (previous != null)
                UnityEngine.Object.DestroyImmediate(previous.gameObject);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (font == null)
                throw new InvalidOperationException("Clock numeral font asset is missing.");

            var numbers = new GameObject("Clock Numbers").transform;
            numbers.SetParent(root.transform, false);
            foreach (var entry in new[]
                     {
                         ("12", new Vector2(0, .058f)),
                         ("3", new Vector2(.058f, 0)),
                         ("6", new Vector2(0, -.058f)),
                         ("9", new Vector2(-.058f, 0))
                     })
            {
                var label = new GameObject("Clock Number " + entry.Item1)
                    .AddComponent<TextMeshPro>();
                label.transform.SetParent(numbers, false);
                label.transform.localPosition =
                    new Vector3(entry.Item2.x, .08f + entry.Item2.y, -.0215f);
                label.transform.localScale = Vector3.one * .01f;
                label.rectTransform.sizeDelta = new Vector2(2f, 1.2f);
                label.font = font;
                label.fontSize = 1.35f;
                label.text = entry.Item1;
                label.color = new Color(.16f, .10f, .18f, 1f);
                label.alignment = TextAlignmentOptions.Center;
                label.enableWordWrapping = false;
                label.raycastTarget = false;
            }
        }

        private static void CreateControllerRays(Transform parent)
        {
            var manager = new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
            manager.transform.SetParent(parent, false);
            var hands = new[] { "LeftHand", "RightHand" };
            var controllerRoots = new GameObject[hands.Length];
            for (var index = 0; index < hands.Length; index++)
            {
                var hand = hands[index];
                var root = new GameObject(hand + " UI Ray");
                controllerRoots[index] = root;
                root.transform.SetParent(parent, false);
                var controller = root.AddComponent<ActionBasedController>();
                var path = "<XRController>{" + hand + "}/";
                controller.positionAction = Action(hand + " Position", path + "devicePosition");
                controller.rotationAction = Action(hand + " Rotation", path + "deviceRotation");
                controller.isTrackedAction = Action(hand + " Tracked", path + "isTracked");
                controller.trackingStateAction = Action(hand + " Tracking", path + "trackingState");
                var select = new InputAction(hand + " Grip", InputActionType.Button);
                select.AddBinding(path + "gripPressed");
                select.AddBinding(path + "grip").WithInteraction("Press(pressPoint=0.1)");
                select.AddBinding(hand == "RightHand" ? "<Keyboard>/o" : "<Keyboard>/e");
                controller.selectAction = new InputActionProperty(select);
                controller.selectActionValue = Action(hand + " Grip Value", path + "grip");
                var uiPress = new InputAction(hand + " UI Press", InputActionType.Button);
                uiPress.AddBinding(path + "triggerPressed");
                uiPress.AddBinding(hand == "RightHand" ? "<Keyboard>/u" : "<Keyboard>/q");
                controller.uiPressAction = new InputActionProperty(uiPress);
                controller.uiPressActionValue = Action(hand + " UI Value", path + "trigger");
                var ray = root.AddComponent<ControllerItemRay>();
                ray.interactionManager = manager;
                ray.enableUIInteraction = true;
                ray.maxRaycastDistance = 8;
                ray.raycastMask = Physics.DefaultRaycastLayers;
                ray.hitDetectionType = XRRayInteractor.HitDetectionType.SphereCast;
                ray.sphereCastRadius = .06f;
                ray.hitClosestOnly = true;
                ray.useForceGrab = false;
                var idleColor = hand == "RightHand"
                    ? new Color(1f, .28f, .62f)
                    : new Color(.12f, .58f, 1f);
                var marker = BuildHandHoldSimulatorMarker(root.transform,
                    hand + " Physical Pose (Editor Only)", .065f,
                    BuildHandHoldSimulatorMaterial(hand, idleColor));
                GetOrAdd<HandHoldSimulatorIndicator>(marker.gameObject)
                    .Configure(ray, marker, idleColor);
                root.AddComponent<XRInteractorLineVisual>().lineWidth = .003f;
            }

            var mode = parent.gameObject.AddComponent<PlayerInteractionModeController>();
            // Official tracked hands are installed at runtime by the mode controller.
            mode.Configure(controllerRoots, null);
        }

        private static InputActionProperty Action(string name, string binding, InputActionType type = InputActionType.Value)
            => new InputActionProperty(new InputAction(name, type, binding));

        [MenuItem("MateSoul/Game/Open Game Scene")]
        public static void OpenScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("MateSoul/Game/Build Android APK")]
        public static void BuildAndroid() => BuildAndroid(BuildOptions.None);

        [MenuItem("MateSoul/Game/Build Android Development APK")]
        public static void BuildAndroidDevelopment()
            => BuildAndroid(BuildOptions.Development | BuildOptions.CleanBuildCache);

        [MenuItem("MateSoul/Game/Build Current Scene Android Development APK")]
        public static void BuildCurrentSceneAndroidDevelopment()
            => BuildCurrentSceneAndroid(BuildOptions.Development | BuildOptions.CleanBuildCache);

        public static void BuildCurrentSceneAndroidIncrementalDevelopment()
            => BuildCurrentSceneAndroid(BuildOptions.Development);

        [MenuItem("MateSoul/Game/Build Current Scene Android APK")]
        public static void BuildCurrentSceneAndroidRelease()
            => BuildCurrentSceneAndroid(BuildOptions.None);

        private static void BuildCurrentSceneAndroid(BuildOptions options)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Unable to switch the Unity build target to Android.");
            var development = (options & BuildOptions.Development) != 0;
            ValidateModelConnectionForBuild(development);
            ConfigureInteractionCapabilities();
            var path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "../../Builds/MateSoul_Framework.apk"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var insecureHttpOption = PlayerSettings.insecureHttpOption;
            try
            {
                if (development) PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
                // Build the reviewed scene as saved; authoring builders would replace character assets.
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath }, locationPathName = path,
                    target = BuildTarget.Android, options = options
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException($"Android build failed: {report.summary.result}");
                Debug.Log($"MATESOUL_BUILD_PASS path={path} bytes={report.summary.totalSize} duration={report.summary.totalTime}");
            }
            finally { PlayerSettings.insecureHttpOption = insecureHttpOption; }
        }

#region debug-point A-D:diagnostic-build
        public static void BuildAndroidDialogueFontDiagnostics()
        {
            var insecureHttpOption = PlayerSettings.insecureHttpOption;
            try
            {
                PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
                BuildAndroid(BuildOptions.None);
            }
            finally
            {
                PlayerSettings.insecureHttpOption = insecureHttpOption;
            }
        }
#endregion

        private static void BuildAndroid(BuildOptions options)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Unable to switch the Unity build target to Android.");
            var development = (options & BuildOptions.Development) != 0;
            ValidateModelConnectionForBuild(development);
            UiFontAssetBuilder.Ensure();
            BuildScene();
            ConfigurePicoBuildSettings();
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/MateSoul_Framework.apk"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var insecureHttpOption = PlayerSettings.insecureHttpOption;
            try
            {
                if (development)
                    PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath }, locationPathName = path,
                    target = BuildTarget.Android,
                    options = development ? options : options | BuildOptions.CleanBuildCache
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException($"Android build failed: {report.summary.result}");
                Debug.Log($"MATESOUL_BUILD_PASS path={path} bytes={report.summary.totalSize} duration={report.summary.totalTime}");
            }
            finally
            {
                PlayerSettings.insecureHttpOption = insecureHttpOption;
            }
        }

        private static void ValidateModelConnectionForBuild(bool development)
        {
            var settings = AssetDatabase.LoadAssetAtPath<ModelConnectionSettings>(
                ModelConnectionPath);
            if (settings == null ||
                !Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint))
                throw new InvalidOperationException(
                    "ModelConnection.asset must contain an absolute provider endpoint.");
            if (endpoint.Scheme == Uri.UriSchemeHttps) return;
            if (development && settings.AllowLocalHttpInDevelopment &&
                endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback)
                return;
            throw new InvalidOperationException(
                development
                    ? "Development builds only allow HTTPS model APIs or explicitly enabled loopback test APIs."
                    : "Release builds require an HTTPS model API.");
        }

        private static void ConfigurePicoBuildSettings()
        {
            PlayerSettings.preserveFramebufferAlpha = true;
            const string path = "Assets/Resources/PXR_ProjectSetting.asset";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var settings = AssetDatabase.LoadAssetAtPath<PXR_ProjectSetting>(path);
            if (settings == null)
                throw new InvalidOperationException($"{path} could not be loaded.");

            settings.handTracking = true;
            settings.handTrackingSupportType = HandTrackingSupport.HandsOnly;
            settings.highFrequencyHand = true;
            settings.eyeTracking = true;
            settings.videoSeeThrough = true;
            settings.spatialAnchor = true;
            settings.sceneCapture = true;
            settings.spatialMesh = true;
            settings.planeDetection = true;
            settings.environmentDepth = true;
            settings.lightEstimation = true;
            settings.lightEstimationTextureResolution = PxrEnvironmentTextureResolution.Size128x128;
            settings.superResolution = false;
            settings.normalSharpening = false;
            settings.qualitySharpening = true;
            settings.fixedFoveatedSharpening = false;
            settings.selfAdaptiveSharpening = true;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
    }
}
