using System;
using System.IO;
using System.Linq;
using ByteDance.PICO.XR;
using MateSoul.Animation;
using MateSoul.RoomNavigation;
using MateSoul.RoomNavigation.Editor;
using MateSoul.Runtime;
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
        private const string ConversationGatewayPath = Content + "/ConversationGateway.asset";
        private const string Source = "Assets/Characters/SapphiArtCustom/Lengyue/Runtime";

        [MenuItem("MateSoul/Game/Build Game Scene")]
        public static void BuildScene()
        {
            Directory.CreateDirectory(Content);
            Directory.CreateDirectory(Content + "/Props");
            Directory.CreateDirectory("Assets/MateSoul/Scenes");
            ConfigureInteractionCapabilities();
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
                    BuildConversationGateway());
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
            settings.handTrackingSupportType = HandTrackingSupport.ControllersAndHands;
            settings.eyeTracking = true;
            settings.spatialAnchor = true;
            settings.spatialMesh = true;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        private static ConversationGatewaySettings BuildConversationGateway()
        {
            var settings = AssetDatabase.LoadAssetAtPath<ConversationGatewaySettings>(
                ConversationGatewayPath);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<ConversationGatewaySettings>();
            settings.Configure("http://127.0.0.1:8787/v1/conversations/reply", 60, true);
            AssetDatabase.CreateAsset(settings, ConversationGatewayPath);
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
                    foreach (var guard in contents.GetComponentsInChildren<HeadPoseGuard>(true))
                        guard.enabled = false;
                    BindContactRigs(contents);
                    PhysicsContentBuilder.ConfigureCharacter(contents);
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
            var bowlingNames = new[] { "Hips", "Spine", "UpperArm_R", "Forearm_R", "Hand_R",
                "Leg_L", "Knee_L", "Foot_L", "Leg_R", "Knee_R", "Foot_R" };
            var bowling = character.GetComponent<AvatarBowlingRig>() ??
                          character.AddComponent<AvatarBowlingRig>();
            bowling.Configure(bowlingNames.Select(name =>
                transforms.Single(item => item.name == name)).ToArray());
            EditorUtility.SetDirty(bowling);
            BindHandHoldRig(character, transforms);
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
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var state = controller.layers[0].stateMachine.AddState("Locomotion");
            var blend = new BlendTree { name = "Idle Walk", blendParameter = "Speed", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(blend, controller);
            var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                Source + "/Animations/Idle01/Lengyue_Idle01_Combined.anim");
            var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>(Source + "/Animations/Lengyue_Walk_BodyOnly.anim");
            if (idle == null || walk == null) throw new InvalidOperationException("Locomotion clips are missing.");
            blend.AddChild(idle, 0);
            blend.AddChild(walk, 1.1f);
            state.motion = blend;
            controller.layers[0].stateMachine.defaultState = state;
            return controller;
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
            material.SetColor("_Color", new Color(1f, .05f, .45f, .95f));
            material.SetFloat("_OutlineWidth", .012f);
            material.SetFloat("_Intensity", 1.8f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static ItemDefinition[] BuildCatalog(Material focusMaterial)
        {
            var plateBreakable = BuildBreakableSettings("Plate",
                Content + "/PropsSource/Plate/LavenderChibiPlate_LightFracture.fbx",
                Content + "/PropsSource/Plate/LavenderChibiPlate_HeavyFracture.fbx",
                45f, 95f, 4, 22);
            var mugBreakable = BuildBreakableSettings("Mug",
                Content + "/PropsSource/Mug/LavenderChibiMug_LightFracture.fbx",
                Content + "/PropsSource/Mug/LavenderChibiMug_HeavyFracture.fbx",
                38f, 80f, 6, 22);
            return new[]
            {
                BuildLamp(focusMaterial),
                BuildProp("table", "桌子", "Table/LavenderChibiTable_600x400x750mm.fbx",
                    new Vector3(.6f, .75f, .4f), focusMaterial),
                BuildProp("chair", "椅子", "Chair/LavenderChibiChair_360x380x680mm.fbx",
                    new Vector3(.36f, .68f, .38f), focusMaterial),
                TelevisionBuilder.Build(),
                BuildCatalogProp("plate", "盘子",
                    Content + "/PropsSource/Plate/LavenderChibiPlate_200x25mm.fbx",
                    new Vector3(.2f, .025f, .2f), .42f, PhysicsContentBuilder.Ceramic, true,
                    focusMaterial, breakable: plateBreakable),
                BuildCatalogProp("mug", "马克杯",
                    Content + "/PropsSource/Mug/LavenderChibiMug_80x95mm.fbx",
                    new Vector3(.113f, .095f, .08f), .34f, PhysicsContentBuilder.Ceramic, true,
                    focusMaterial, breakable: mugBreakable),
                BuildCatalogProp("bowl", "碗",
                    Content + "/PropsSource/Bowl/LavenderChibiBowl_138mm.fbx",
                    new Vector3(.138f, .07f, .138f), .31f, PhysicsContentBuilder.Ceramic, true,
                    focusMaterial, breakable: new BreakableSettings(35f, 72f)),
                BuildCatalogProp("playing-cards", "扑克牌",
                    Content + "/PropsSource/PlayingCards/LavenderChibi_CardsAndBox_Packed.fbx",
                    new Vector3(.065f, .0185f, .091f), .11f, PhysicsContentBuilder.Paper, true,
                    focusMaterial, CatalogColliderShape.Box, true),
                BuildCatalogProp("spoon", "勺子",
                    Content + "/PropsSource/Spoon/LavenderChibiSpoon_140mm.fbx",
                    new Vector3(.025f, .012f, .14f), .04f, PhysicsContentBuilder.Ceramic, true,
                    focusMaterial),
                BuildCatalogProp("chopstick-left", "左筷子",
                    Content + "/PropsSource/Chopsticks/LavenderChibiChopsticks_230mm.fbx",
                    new Vector3(.007f, .007f, .23f), .02f, PhysicsContentBuilder.Ceramic, true,
                    focusMaterial, sourceObjectName: "Chopstick_Left", catalogVisible: false),
                BuildCatalogProp("chopstick-right", "右筷子",
                    Content + "/PropsSource/Chopsticks/LavenderChibiChopsticks_230mm.fbx",
                    new Vector3(.007f, .007f, .23f), .02f, PhysicsContentBuilder.Ceramic, true,
                    focusMaterial, sourceObjectName: "Chopstick_Right", catalogVisible: false),
                BuildChopsticks(focusMaterial),
                BuildKnife(focusMaterial),
                BuildCatalogProp("cupcake", "纸杯蛋糕",
                    Content + "/PropsSource/Cupcake/LavenderChibiCupcake_70x75mm.fbx",
                    new Vector3(.07f, .075f, .07f), .09f, PhysicsContentBuilder.Food, true,
                    focusMaterial),
                BuildCatalogProp("cookie", "曲奇",
                    "Assets/Experiments/YummyCookie_v001/Props/Cookie.prefab",
                    new Vector3(.05f, .011f, .05f), .025f, PhysicsContentBuilder.Food, true,
                    focusMaterial),
                BuildCatalogProp("apple", "苹果",
                    Content + "/PropsSource/Apple/GameApple_Full_90x90x103mm.fbx",
                    new Vector3(.09f, .103f, .09f), .16f, PhysicsContentBuilder.Food, true,
                    focusMaterial),
                BuildCatalogProp("dartboard", "飞镖盘",
                    Content + "/PropsSource/Dartboard/LavenderChibiDartboard_400x400x48mm.fbx",
                    new Vector3(.4f, .4f, .048f), 2.2f, PhysicsContentBuilder.Wood, false,
                    focusMaterial),
                BuildCatalogProp("bowling-ball", "保龄球",
                    Content + "/PropsSource/Bowling/LavenderChibiBowlingBall_218mm.fbx",
                    Vector3.one * .2183f, 6.5f, PhysicsContentBuilder.BowlingBall, false,
                    focusMaterial, CatalogColliderShape.Sphere),
                BuildCatalogProp("bowling-pin", "保龄球瓶",
                    Content + "/PropsSource/Bowling/LavenderChibiBowlingPin_380mm.fbx",
                    new Vector3(.121f, .38f, .121f), 1.55f, PhysicsContentBuilder.BowlingPin,
                    false, focusMaterial, CatalogColliderShape.Capsule),
                BuildCatalogProp("plush-doll", "冷月玩偶",
                    Content + "/PropsSource/PlushDoll/Lengyue_PlushDoll_300mm.fbx",
                    new Vector3(.2116f, .3f, .2015f), .38f, PhysicsContentBuilder.Fabric, true,
                    focusMaterial),
                BuildCatalogProp("remote-control", "遥控器",
                    Content + "/PropsSource/RemoteControl/LavenderRetroRemote_50x18x140mm.fbx",
                    new Vector3(.05f, .018f, .14f), .13f, PhysicsContentBuilder.Plastic, true,
                    focusMaterial, CatalogColliderShape.Box, true)
            };
        }

        private static ItemDefinition BuildChopsticks(Material focusMaterial)
        {
            var sourcePath = Content +
                             "/PropsSource/Chopsticks/LavenderChibiChopsticks_230mm.fbx";
            var group = BuildCatalogProp("chopsticks", "筷子", sourcePath,
                new Vector3(.007f, .007f, .23f), .04f, PhysicsContentBuilder.Ceramic, true,
                focusMaterial, sourceObjectName: "Chopstick_Left");
            var left = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                Content + "/Props/ChopstickLeft.asset");
            var right = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                Content + "/Props/ChopstickRight.asset");
            if (left == null || right == null)
                throw new InvalidOperationException("筷子组合缺少左右独立定义。");
            group.Configure(group.Id, group.DisplayName, group.Prefab, group.Capabilities, true,
                new Vector2(.016f, .23f), .007f, focusMaterial);
            group.ConfigurePlacementParts(
                new ItemPlacementPart(left, Vector3.left * .0045f, Quaternion.identity),
                new ItemPlacementPart(right, Vector3.right * .0045f, Quaternion.identity));
            EditorUtility.SetDirty(group);
            return group;
        }

        private static BreakableSettings BuildBreakableSettings(string itemName,
            string lightSourcePath, string heavySourcePath, float lightForce,
            float heavyForce, int lightCount, int heavyCount)
        {
            var light = BuildFracturePrefab(itemName, "Light", lightSourcePath, lightCount);
            var heavy = BuildFracturePrefab(itemName, "Heavy", heavySourcePath, heavyCount);
            return new BreakableSettings(lightForce, heavyForce, lightCount, heavyCount,
                lightPrefab: light, heavyPrefab: heavy);
        }

        private static GameObject BuildFracturePrefab(string itemName, string severity,
            string sourcePath, int expectedCount)
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
                        .Select(material => BuildCatalogMaterial(itemName, material)).ToArray();
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
                    true, new Vector2(.42f, .42f), .66f, focusMaterial);
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
                    true, new Vector2(.42f, .42f), .66f, focusMaterial);
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
                    new Vector2(dimensions.x, dimensions.z), dimensions.y, focusMaterial);
                definition.ConfigurePhysics(physics);
                EditorUtility.SetDirty(definition);
                return definition;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static ItemDefinition BuildCatalogProp(string id, string title, string sourcePath,
            Vector3 dimensions, float mass, PhysicMaterial physicsMaterial, bool allowSurfacePlacement,
            Material focusMaterial, CatalogColliderShape colliderShape = CatalogColliderShape.Box,
            bool layFlat = false, string sourceObjectName = null, bool catalogVisible = true,
            BreakableSettings breakable = default)
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
                        .Select(material => BuildCatalogMaterial(stem, material)).ToArray();

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
                if (Mathf.Max(dimensions.x, dimensions.z) >= .25f)
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
                    new Vector2(dimensions.x, dimensions.z), dimensions.y, focusMaterial);
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
            var bladeMaterial = BuildKnifeMaterial("KnifeBlade", new Color(.73f, .77f, .82f), .86f);
            var handleMaterial = BuildKnifeMaterial("KnifeHandle", new Color(.45f, .25f, .58f), .5f);
            var root = new GameObject("餐刀");
            try
            {
                var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blade.name = "Blade";
                blade.transform.SetParent(root.transform, false);
                blade.transform.localPosition = new Vector3(0, .003f, .0475f);
                blade.transform.localScale = new Vector3(.028f, .006f, .125f);
                blade.GetComponent<Renderer>().sharedMaterial = bladeMaterial;
                var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                handle.name = "Handle";
                handle.transform.SetParent(root.transform, false);
                handle.transform.localPosition = new Vector3(0, .007f, -.0625f);
                handle.transform.localScale = new Vector3(.025f, .014f, .095f);
                handle.GetComponent<Renderer>().sharedMaterial = handleMaterial;

                var physics = new BodySettings(.09f, PhysicsContentBuilder.Metal);
                PhysicsContentBuilder.ConfigureBody(root, physics);
                var prefabPath = Content + "/Props/Knife.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                var definitionPath = Content + "/Props/Knife.asset";
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(definitionPath);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ItemDefinition>();
                    AssetDatabase.CreateAsset(definition, definitionPath);
                }
                definition.Configure("knife", "餐刀", prefab, new[] { "visit" }, true,
                    new Vector2(.028f, .22f), .014f, focusMaterial);
                definition.ConfigurePhysics(physics, true);
                EditorUtility.SetDirty(definition);
                return definition;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static Material BuildKnifeMaterial(string name, Color color, float smoothness)
        {
            var path = Content + "/Props/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP Lit shader is missing.");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
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

            var gazeRoot = new GameObject("Gaze Pinch Ray");
            gazeRoot.transform.SetParent(parent, false);
            var gazeController = gazeRoot.AddComponent<ActionBasedController>();
            gazeController.positionAction = Action("Gaze Position", "<PXR_HMD>/eyeGazePosition");
            gazeController.rotationAction = Action("Gaze Rotation", "<PXR_HMD>/eyeGazeRotation");
            gazeController.isTrackedAction = Action("Gaze Tracked", "<PXR_HMD>/eyeGazeIsTracked");
            gazeController.trackingStateAction = Action("Gaze Tracking",
                "<PXR_HMD>/eyeGazeTrackingState");
            gazeController.uiPressAction = Action("Right Pinch UI Press",
                "<PicoHandInteraction>{RightHand}/pinchTouched", InputActionType.Button);
            gazeController.uiPressActionValue = Action("Right Pinch UI Value",
                "<PicoHandInteraction>{RightHand}/pinchValue");
            var gazeRay = gazeRoot.AddComponent<XRRayInteractor>();
            gazeRay.interactionManager = manager;
            gazeRay.enableUIInteraction = true;
            gazeRay.maxRaycastDistance = 8;
            gazeRay.raycastMask = Physics.DefaultRaycastLayers;
            gazeRay.hitDetectionType = XRRayInteractor.HitDetectionType.SphereCast;
            gazeRay.sphereCastRadius = .09f;
            gazeRay.hitClosestOnly = true;
            gazeRay.useForceGrab = false;
            var mode = parent.gameObject.AddComponent<PlayerInteractionModeController>();
            var handRoots = new GameObject[hands.Length];
            for (var index = 0; index < hands.Length; index++)
            {
                var hand = hands[index];
                var root = new GameObject(hand + " Hand Ray");
                handRoots[index] = root;
                root.transform.SetParent(parent, false);
                var controller = root.AddComponent<ActionBasedController>();
                var path = "<PicoHandInteraction>{" + hand + "}/";
                controller.positionAction = Action(hand + " Hand Aim Position", path + "pointer/position");
                controller.rotationAction = Action(hand + " Hand Aim Rotation", path + "pointer/rotation");
                controller.isTrackedAction = Action(hand + " Hand Tracked", path + "isTracked");
                controller.trackingStateAction = Action(hand + " Hand Tracking", path + "trackingState");
                controller.selectAction = Action(hand + " Pinch", path + "pinchTouched",
                    InputActionType.Button);
                controller.selectActionValue = Action(hand + " Pinch Value", path + "pinchValue");
                controller.uiPressAction = Action(hand + " Hand UI Press", path + "pointerActivated",
                    InputActionType.Button);
                controller.uiPressActionValue = Action(hand + " Hand UI Value",
                    path + "pointerActivateValue");
                var ray = root.AddComponent<HandItemRay>();
                ray.interactionManager = manager;
                ray.enableUIInteraction = true;
                ray.maxRaycastDistance = 8;
                ray.raycastMask = Physics.DefaultRaycastLayers;
                ray.hitDetectionType = XRRayInteractor.HitDetectionType.SphereCast;
                ray.sphereCastRadius = .06f;
                ray.hitClosestOnly = true;
                ray.useForceGrab = false;
                var attach = new GameObject(hand + " Pinch Attach");
                attach.transform.SetParent(parent, false);
                attach.AddComponent<HandPinchPose>().Configure(
                    Action(hand + " Pinch Position", path + "pinchPose/position"),
                    Action(hand + " Pinch Rotation", path + "pinchPose/rotation"));
                ray.Configure(mode, gazeRay, attach.transform);
                root.AddComponent<XRInteractorLineVisual>().lineWidth = .003f;
            }
            mode.Configure(controllerRoots, gazeRoot, handRoots: handRoots);
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

        public static void BuildAndroidDevelopment() => BuildAndroid(BuildOptions.Development);

        private static void BuildAndroid(BuildOptions options)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Unable to switch the Unity build target to Android.");
            UiFontAssetBuilder.Ensure();
            BuildScene();
            ConfigurePicoBuildSettings();
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/MateSoul_Framework.apk"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var development = (options & BuildOptions.Development) != 0;
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

        private static void ConfigurePicoBuildSettings()
        {
            const string path = "Assets/Resources/PXR_ProjectSetting.asset";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var settings = AssetDatabase.LoadAssetAtPath<PXR_ProjectSetting>(path);
            if (settings == null)
                throw new InvalidOperationException($"{path} could not be loaded.");

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
