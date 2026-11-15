using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MateSoul.Runtime;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MateSoul.Editor
{
    public static class ItemUseAnimationBuilder
    {
        public const string DirectoryPath = "Assets/MateSoul/Content/ItemActions/Use";
        public const string DrinkPath = DirectoryPath + "/Drink.asset";
        public const string EatPath = DirectoryPath + "/Eat.asset";
        public const string HugToyPath = DirectoryPath + "/HugToy.asset";
        public const string PlayRadioPath = DirectoryPath + "/PlayRadio.asset";

        [MenuItem("MateSoul/Game/Import Drink Animation")]
        public static void ImportDrink()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(GameSceneBuilder.CharacterPath);
            var clip = PickupAnimationBuilder.RetargetAuthoredItem(character,
                DirectoryPath + "/Source/Drink_01_InPlace.fbx", DirectoryPath + "/Drink_01.anim");
            var mug = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Mug.asset");
            FitDrinkToMug(character, clip, mug);
            var animation = AssetDatabase.LoadAssetAtPath<ItemUseAnimation>(DrinkPath);
            if (!animation)
            {
                animation = ScriptableObject.CreateInstance<ItemUseAnimation>();
                AssetDatabase.CreateAsset(animation, DrinkPath);
            }
            animation.clip = clip;
            animation.actionLabel = "喝水";
            BindController(character.GetComponentInChildren<Animator>().runtimeAnimatorController
                as AnimatorController);
            mug.ConfigureUse(animation, true);
            EditorUtility.SetDirty(mug);
            EditorUtility.SetDirty(animation);
            AssetDatabase.SaveAssets();
            Debug.Log($"MATESOUL_DRINK_IMPORTED seconds={clip.length:F4} mugAutonomy={mug.AllowAutonomousUse}");
        }

        [MenuItem("MateSoul/Game/Import Hug Toy Animation")]
        public static void ImportHugToy()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(GameSceneBuilder.CharacterPath);
            var sourcePath = DirectoryPath + "/Source/HugToy_01_InPlace.fbx";
            var clip = AvatarItemAnimationRetarget.Retarget(
                sourcePath, DirectoryPath + "/HugToy_01.anim");
            var animation = AssetDatabase.LoadAssetAtPath<ItemUseAnimation>(HugToyPath);
            if (!animation)
            {
                animation = ScriptableObject.CreateInstance<ItemUseAnimation>();
                AssetDatabase.CreateAsset(animation, HugToyPath);
            }
            animation.clip = clip;
            animation.actionLabel = "抱玩偶";
            // A plain pickup hugs the doll once, then returns to the normal hold.
            animation.playOnPickup = true;
            BindController(character.GetComponentInChildren<Animator>().runtimeAnimatorController
                as AnimatorController);
            var doll = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/PlushDoll.asset");
            if (!doll) throw new InvalidOperationException("Missing plush doll definition.");
            FitGestureGrip(sourcePath, DirectoryPath + "/HugToy_01.anim", doll,
                "Stuffed_Shirt", "Head_Fabric");
            doll.ConfigureUse(animation, true);
            EditorUtility.SetDirty(doll);
            EditorUtility.SetDirty(animation);
            AssetDatabase.SaveAssets();
            Debug.Log($"MATESOUL_HUG_TOY_IMPORTED seconds={clip.length:F4} " +
                $"dollAutonomy={doll.AllowAutonomousUse}");
        }

        [MenuItem("MateSoul/Game/Import Radio Play Animation")]
        public static void ImportPlayRadio()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(GameSceneBuilder.CharacterPath);
            var clip = AvatarItemAnimationRetarget.Retarget(
                DirectoryPath + "/Source/PlayRadio_01_InPlace.fbx",
                DirectoryPath + "/PlayRadio_01.anim");
            var animation = AssetDatabase.LoadAssetAtPath<ItemUseAnimation>(PlayRadioPath);
            if (!animation)
            {
                animation = ScriptableObject.CreateInstance<ItemUseAnimation>();
                AssetDatabase.CreateAsset(animation, PlayRadioPath);
            }
            animation.clip = clip;
            animation.actionLabel = "播放音乐";
            // The radio's gesture is driven by play-music and switch-music, never
            // by an autonomous pickup.
            animation.playOnPickup = false;
            BindController(character.GetComponentInChildren<Animator>().runtimeAnimatorController
                as AnimatorController);
            var radio = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Radio.asset");
            if (!radio) throw new InvalidOperationException("Missing radio definition.");
            FitGestureGrip(DirectoryPath + "/Source/PlayRadio_01_InPlace.fbx",
                DirectoryPath + "/PlayRadio_01.anim", radio, "Radio_Handle_Grip");
            radio.ConfigureUse(animation, false);
            EditorUtility.SetDirty(radio);
            EditorUtility.SetDirty(animation);
            AssetDatabase.SaveAssets();
            Debug.Log($"MATESOUL_RADIO_ANIMATION_IMPORTED seconds={clip.length:F4}");
        }

        [MenuItem("MateSoul/Game/Import Eat Animation")]
        public static void ImportEat()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(GameSceneBuilder.CharacterPath);
            var clip = PickupAnimationBuilder.RetargetAuthoredItem(character,
                DirectoryPath + "/Source/Eating_01_InPlace.fbx", DirectoryPath + "/Eat_01.anim");
            var sound = ImportBiteSound(DirectoryPath + "/Source/Eating_01_Bite.wav");
            var animation = AssetDatabase.LoadAssetAtPath<ItemUseAnimation>(EatPath);
            if (!animation)
            {
                animation = ScriptableObject.CreateInstance<ItemUseAnimation>();
                AssetDatabase.CreateAsset(animation, EatPath);
            }
            animation.clip = clip;
            animation.biteSound = sound;
            animation.actionLabel = "吃东西";
            BindController(character.GetComponentInChildren<Animator>().runtimeAnimatorController
                as AnimatorController);
            var instance = PrefabUtility.LoadPrefabContents(GameSceneBuilder.CharacterPath);
            try
            {
                instance.GetComponent<AvatarItemRig>().ConfigureEat(animation);
                PrefabUtility.SaveAsPrefabAsset(instance, GameSceneBuilder.CharacterPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(instance); }
            MarkEdible("Assets/MateSoul/Content/Props/Cupcake.asset",
                "Assets/MateSoul/Content/Props/Cookie.asset",
                "Assets/MateSoul/Content/Props/Apple.asset",
                "Assets/MateSoul/Content/Props/AppleQuarter.asset");
            EditorUtility.SetDirty(animation);
            AssetDatabase.SaveAssets();
            Debug.Log($"MATESOUL_EAT_IMPORTED seconds={clip.length:F4} sound={sound.length:F4}");
        }

        private static AudioClip ImportBiteSound(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
                throw new InvalidOperationException("Missing bite audio source: " + path);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        private static void MarkEdible(params string[] paths)
        {
            foreach (var path in paths)
            {
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (!definition)
                    throw new InvalidOperationException("Missing edible item definition: " + path);
                definition.ConfigureEdible(true);
                EditorUtility.SetDirty(definition);
            }
        }

        // Content rebuilds regenerate every profile's grip geometry, so the
        // gesture fits have to be re-applied there as well or the props snap
        // back to their generic palm pose whenever "Build Item Actions" runs.
        public static void FitGripForContent()
        {
            FitGripForContent("Assets/MateSoul/Content/Props/PlushDoll.asset",
                DirectoryPath + "/Source/HugToy_01_InPlace.fbx",
                DirectoryPath + "/HugToy_01.anim",
                "Stuffed_Shirt", "Head_Fabric");
            FitGripForContent("Assets/MateSoul/Content/Props/Radio.asset",
                DirectoryPath + "/Source/PlayRadio_01_InPlace.fbx",
                DirectoryPath + "/PlayRadio_01.anim",
                "Radio_Handle_Grip");
        }

        private static void FitGripForContent(string itemPath, string sourcePath,
            string targetClipPath, params string[] preferredMeshes)
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(itemPath);
            if (item == null || item.Interaction == null ||
                AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath) == null ||
                AssetDatabase.LoadAssetAtPath<AnimationClip>(targetClipPath) == null)
                return;
            try
            {
                FitGestureGrip(sourcePath, targetClipPath, item, preferredMeshes);
            }
            catch (InvalidOperationException error)
            {
                Debug.LogWarning($"MATESOUL_USE_GRIP_SKIPPED item={item.Id} " +
                    error.Message);
            }
        }

        // The authored gesture already contains the prop posed exactly where the
        // artist wants it. The prop's authored mesh pose relative to the source
        // upper chest is mapped onto the production character's upper chest, and
        // the grip offset is then derived from the production hand at the same
        // authored frame. This deliberately avoids carrying any unit/axis
        // difference between the gesture FBX and the production prop prefab into
        // the grip anchor. Works for both the hug doll and the radio.
        private static void FitGestureGrip(string sourcePath, string targetClipPath,
            ItemDefinition item, params string[] preferredMeshes)
        {
            var sourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            var targetAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
                GameSceneBuilder.CharacterPath);
            var targetClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(targetClipPath);
            var itemPrefab = item.Prefab;
            var profile = item.Interaction;
            if (sourceAsset == null || targetAsset == null || targetClip == null ||
                itemPrefab == null || profile == null)
                throw new InvalidOperationException(
                    "Missing gesture source, target clip, character, item prefab, or grip profile.");
            var source = UnityEngine.Object.Instantiate(sourceAsset);
            var target = UnityEngine.Object.Instantiate(targetAsset);
            var itemInstance = UnityEngine.Object.Instantiate(itemPrefab);
            source.hideFlags = target.hideFlags = itemInstance.hideFlags =
                HideFlags.HideAndDontSave;
            try
            {
                foreach (var behaviour in source.GetComponentsInChildren<Behaviour>(true))
                    behaviour.enabled = false;
                foreach (var behaviour in target.GetComponentsInChildren<Behaviour>(true))
                    behaviour.enabled = false;
                foreach (var behaviour in itemInstance.GetComponentsInChildren<Behaviour>(true))
                    behaviour.enabled = false;
                var sourceMesh = FindSharedMesh(
                    source, itemInstance, preferredMeshes, out var itemMesh);
                if (sourceMesh == null || itemMesh == null)
                    throw new InvalidOperationException(
                        "The gesture does not contain this item's model.");
                var sourceClip = AssetDatabase.LoadAllAssetsAtPath(sourcePath)
                    .OfType<AnimationClip>()
                    .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                    .OrderByDescending(clip => AnimationUtility.GetCurveBindings(clip)
                        .Count(binding => binding.type == typeof(Transform)))
                    .First();
                var sourceBones = Bones(source);
                var targetBones = Bones(target);
                var sourceHand = sourceBones["J_Bip_R_Hand"];
                var sourceMiddle = sourceBones["J_Bip_R_Middle1"];
                var sourceIndex = sourceBones["J_Bip_R_Index1"];
                var sourcePinky = sourceBones["J_Bip_R_Little1"];
                var sourceChest = sourceBones["J_Bip_C_UpperChest"];
                var sourceHead = sourceBones["J_Bip_C_Head"];
                var targetHand = targetBones["lengyue_hand_r"];
                var targetMiddle = targetBones["lengyue_middle_01_r"];
                var targetIndex = targetBones["lengyue_index_01_r"];
                var targetPinky = targetBones["lengyue_little_01_r"];
                var targetChest = targetBones["lengyue_upper_chest"];
                var targetHead = targetBones["lengyue_head_01"];
                var meshLocal = itemInstance.transform.worldToLocalMatrix *
                                itemMesh.localToWorldMatrix;
                var meshPosition = (Vector3)meshLocal.GetColumn(3);
                var previousGrip = new Pose(
                    profile.gripPosition, Quaternion.Euler(profile.gripEuler));
                var shapeCenter = previousGrip.position +
                    previousGrip.rotation * profile.gripShapeOffset;
                var sourceRenderer = sourceMesh.GetComponent<Renderer>();
                var frameRate = sourceClip.frameRate > 0 ? sourceClip.frameRate : 30f;
                var frames = Mathf.RoundToInt(sourceClip.length * frameRate);
                var bestScore = float.PositiveInfinity;
                var bestTime = 0f;
                for (var frame = 0; frame <= frames; frame++)
                {
                    var time = frame / frameRate;
                    sourceClip.SampleAnimation(source, time);
                    var propCenter = sourceRenderer != null
                        ? sourceRenderer.bounds.center : sourceMesh.position;
                    var grip = GripFromHand(
                        sourceHand, sourceMiddle, sourceIndex, sourcePinky);
                    var score = Vector3.Distance(grip.position, propCenter);
                    if (score >= bestScore) continue;
                    bestScore = score;
                    bestTime = time;
                }
                // Evaluate both rigs on the same authored frame: the source
                // gives the prop's pose against the chest, the target gives the
                // production chest and the production hand that will hold it.
                sourceClip.SampleAnimation(source, bestTime);
                targetClip.SampleAnimation(target, bestTime);
                var authoredMesh = new Pose(sourceMesh.position, sourceMesh.rotation);
                var sourceGrip = GripFromHand(
                    sourceHand, sourceMiddle, sourceIndex, sourcePinky);
                var targetGrip = GripFromHand(
                    targetHand, targetMiddle, targetIndex, targetPinky);
                var chestOffsetPosition = Quaternion.Inverse(sourceChest.rotation) *
                    (authoredMesh.position - sourceChest.position);
                var chestOffsetRotation = Quaternion.Inverse(sourceChest.rotation) *
                    authoredMesh.rotation;
                var visualPosition = targetChest.position +
                    targetChest.rotation * chestOffsetPosition;
                var visualRotation = targetChest.rotation * chestOffsetRotation;
                var itemRotation = visualRotation *
                    Quaternion.Inverse(meshLocal.rotation);
                var itemPosition = visualPosition - itemRotation * meshPosition;
                var gripPosition = Quaternion.Inverse(itemRotation) *
                    (targetGrip.position - itemPosition);
                var gripRotation = Quaternion.Inverse(itemRotation) * targetGrip.rotation;
                profile.gripPosition = gripPosition;
                profile.gripEuler = gripRotation.eulerAngles;
                // Keep the authored grasp volume (the doll's chest, the radio's
                // handle) on the same physical part of the prop.
                profile.gripShapeOffset = Quaternion.Inverse(gripRotation) *
                    (shapeCenter - gripPosition);
                // The gesture poses both hands and all fingers around the prop;
                // the procedural finger solve would overwrite them and push the
                // fingers through the model.
                profile.authoredFingerPose = true;
                EditorUtility.SetDirty(profile);
                Debug.Log($"MATESOUL_USE_GRIP item={item.Id} mesh={sourceMesh.name} " +
                    $"time={bestTime:F2} handToProp={bestScore:F3} " +
                    $"chestOffset={chestOffsetPosition} " +
                    $"sourceHead={sourceHead.position} targetHead={targetHead.position} " +
                    $"sourceGrip={sourceGrip.position} targetGrip={targetGrip.position} " +
                    $"handAngle={Quaternion.Angle(sourceGrip.rotation, targetGrip.rotation):F1} " +
                    $"gripPosition={gripPosition} gripEuler={gripRotation.eulerAngles} " +
                    $"shapeCenter={shapeCenter} meshPosition={meshPosition}");
                // Plan A: bake the authored prop pose per frame, expressed in the
                // production right-hand bone's local space. Sampling this track at
                // the use-animation time reproduces the authored prop trajectory
                // (two-hand hug, re-grip on the handle) instead of one rigid offset.
                var holdPositions = new Vector3[frames + 1];
                var holdRotations = new Quaternion[frames + 1];
                for (var frame = 0; frame <= frames; frame++)
                {
                    var time = Mathf.Min(sourceClip.length, frame / frameRate);
                    sourceClip.SampleAnimation(source, time);
                    targetClip.SampleAnimation(target, time);
                    var authored = new Pose(sourceMesh.position, sourceMesh.rotation);
                    var chestPosition = Quaternion.Inverse(sourceChest.rotation) *
                        (authored.position - sourceChest.position);
                    var chestRotation = Quaternion.Inverse(sourceChest.rotation) *
                        authored.rotation;
                    var holdVisualPosition = targetChest.position +
                        targetChest.rotation * chestPosition;
                    var holdVisualRotation = targetChest.rotation * chestRotation;
                    var frameRotation = holdVisualRotation *
                        Quaternion.Inverse(meshLocal.rotation);
                    var framePosition = holdVisualPosition - frameRotation * meshPosition;
                    var inverseHand = Quaternion.Inverse(targetHand.rotation);
                    holdPositions[frame] = inverseHand *
                        (framePosition - targetHand.position);
                    holdRotations[frame] = (inverseHand * frameRotation).normalized;
                }
                profile.authoredHoldPositions = holdPositions;
                profile.authoredHoldRotations = holdRotations;
                profile.authoredHoldFrameRate = frameRate;
                var holdRange = 0f;
                for (var frame = 1; frame <= frames; frame++)
                    holdRange = Mathf.Max(holdRange,
                        Vector3.Distance(holdPositions[0], holdPositions[frame]));
                Debug.Log($"MATESOUL_USE_AUTHORED_HOLD item={item.Id} " +
                    $"frames={frames + 1} rate={frameRate:F1} drift={holdRange:F3} " +
                    $"hand={targetHand.name}");
                EditorUtility.SetDirty(profile);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(itemInstance);
            }
        }

        private static Pose GripFromHand(Transform hand, Transform middle,
            Transform index, Transform pinky)
        {
            var forward = middle.position - hand.position;
            var across = index.position - pinky.position;
            if (forward.sqrMagnitude < .000001f) forward = hand.forward;
            var up = -Vector3.Cross(forward, across);
            if (up.sqrMagnitude < .000001f) up = hand.up;
            return new Pose(
                Vector3.Lerp(hand.position, middle.position, .55f),
                Quaternion.LookRotation(forward, up.normalized));
        }

        private static Dictionary<string, Transform> Bones(GameObject root)
            => root.GetComponentsInChildren<Transform>(true)
                .GroupBy(bone => bone.name)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single());

        // The gesture exports its own copy of the prop. Its meshes match the
        // production prefab by name, so the shared mesh is the anchor that
        // transfers the authored root pose between the two models.
        private static Transform FindSharedMesh(GameObject source, GameObject item,
            string[] preferredMeshes, out Transform itemMesh)
        {
            var preferred = new HashSet<string>(preferredMeshes ?? Array.Empty<string>());
            var itemMeshes = new Dictionary<string, Transform>();
            foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = RendererMesh(renderer);
                if (mesh == null) continue;
                var key = MeshKey(mesh.name);
                if (!itemMeshes.ContainsKey(key)) itemMeshes[key] = renderer.transform;
            }
            Transform found = null;
            itemMesh = null;
            foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = RendererMesh(renderer);
                if (mesh == null) continue;
                var key = MeshKey(mesh.name);
                if (!itemMeshes.TryGetValue(key, out var candidate)) continue;
                if (preferred.Contains(key))
                {
                    itemMesh = candidate;
                    return renderer.transform;
                }
                if (found == null)
                {
                    found = renderer.transform;
                    itemMesh = candidate;
                }
            }
            if (found == null)
                Debug.LogWarning("MATESOUL_USE_GRIP_NO_MESH itemMeshes=" +
                    string.Join(",", itemMeshes.Keys));
            return found;
        }

        private static Mesh RendererMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skin) return skin.sharedMesh;
            if (!(renderer is MeshRenderer)) return null;
            var filter = renderer.GetComponent<MeshFilter>();
            return filter ? filter.sharedMesh : null;
        }

        private static string MeshKey(string name)
        {
            name = name.Trim();
            for (;;)
            {
                if (name.EndsWith(".001", StringComparison.Ordinal))
                {
                    name = name.Substring(0, name.Length - 4);
                    continue;
                }
                if (name.EndsWith("_Mesh", StringComparison.Ordinal))
                {
                    name = name.Substring(0, name.Length - 5);
                    continue;
                }
                return name;
            }
        }

        private static void FitDrinkToMug(GameObject character, AnimationClip clip, ItemDefinition mug)
        {
            var instance = UnityEngine.Object.Instantiate(character);
            instance.hideFlags = HideFlags.HideAndDontSave;
            var baked = new Mesh();
            try
            {
                foreach (var behaviour in instance.GetComponentsInChildren<Behaviour>(true))
                    behaviour.enabled = false;
                var bones = instance.GetComponentsInChildren<Transform>();
                Transform Bone(string name) => bones.Single(value => value.name == name);
                var hand = Bone("lengyue_hand_r");
                var elbow = Bone("lengyue_forearm_r");
                var shoulder = Bone("lengyue_upper_arm_r");
                var head = Bone("lengyue_head_01");
                var middle = Bone("lengyue_middle_01_r");
                var localGrip = hand.InverseTransformPoint(Vector3.Lerp(hand.position, middle.position, .55f));
                var forward = hand.InverseTransformDirection(middle.position - hand.position);
                var across = hand.InverseTransformDirection(Bone("lengyue_index_01_r").position - Bone("lengyue_little_01_r").position);
                var localGripRotation = Quaternion.LookRotation(forward, -Vector3.Cross(forward, across));
                var face = instance.GetComponentsInChildren<SkinnedMeshRenderer>()
                    .Single(value => value.name == "lengyue_face_root" &&
                        value.sharedMesh.GetBlendShapeIndex("Fcl_MTH_U") >= 0);
                face.BakeMesh(baked);
                var vertices = baked.vertices;
                var deltas = new Vector3[vertices.Length];
                face.sharedMesh.GetBlendShapeFrameVertices(face.sharedMesh.GetBlendShapeIndex("Fcl_MTH_U"),
                    0, deltas, null, null);
                var weightedMouth = Vector3.zero;
                var totalWeight = 0f;
                for (int i = 0; i < vertices.Length; i++)
                {
                    var weight = deltas[i].magnitude;
                    if (weight < .00001f) continue;
                    weightedMouth += face.transform.TransformPoint(vertices[i]) * weight;
                    totalWeight += weight;
                }
                if (totalWeight <= 0)
                    throw new InvalidOperationException("Production face has no mouth landmark.");
                var mouthLocal = head.InverseTransformPoint(weightedMouth / totalWeight);
                var receiver = mug.Prefab.GetComponent<WaterReceiver>();
                // WaterReceiver centers its opening on the vessel at Awake. The
                // prefab's serialized offset still includes the imported handle.
                var vessel = mug.Prefab.GetComponentsInChildren<Renderer>(true)
                    .Where(value => value.transform != receiver.WaterVisual &&
                        value.name.IndexOf("handle", StringComparison.OrdinalIgnoreCase) < 0 &&
                        (value.name.IndexOf("lengyue_body_root", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         value.name.IndexOf("cup", StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderByDescending(value => value.bounds.size.x * value.bounds.size.z).First();
                var openingCenter = receiver.OpeningCenter;
                var vesselCenter = mug.Prefab.transform.InverseTransformPoint(vessel.bounds.center);
                openingCenter.x = vesselCenter.x;
                openingCenter.z = vesselCenter.z;
                var arm = new[] { shoulder, elbow, hand };
                var curves = arm.Select(_ => Enumerable.Range(0, 4).Select(i => new AnimationCurve()).ToArray()).ToArray();
                var report = "seconds,rimError,tiltDegrees\n";
                for (int frame = 0; frame <= Mathf.RoundToInt(clip.length * clip.frameRate); frame++)
                {
                    var time = Mathf.Min(clip.length, frame / clip.frameRate);
                    clip.SampleAnimation(instance, time);
                    var sip = Mathf.SmoothStep(0, 1, (time - .4f) / .5f) *
                        (1 - Mathf.SmoothStep(0, 1, (time - 2.45f) / .6f));
                    var tilt = 55 * Mathf.SmoothStep(0, 1, (time - .65f) / 1.65f) *
                        (1 - Mathf.SmoothStep(0, 1, (time - 2.45f) / .6f));
                    var cupRotation = Quaternion.AngleAxis(-tilt, Vector3.right) * Quaternion.Euler(0, 180, 0);
                    var normal = cupRotation * Vector3.up;
                    var rimOffset = Vector3.ProjectOnPlane(Vector3.back, normal).normalized * receiver.OpeningRadius;
                    var mouth = head.TransformPoint(mouthLocal) + Vector3.forward * .002f;
                    var cup = new Pose(mouth - cupRotation * openingCenter - rimOffset, cupRotation);
                    var desired = mug.Interaction.WorldGrip(cup);
                    var handRotation = desired.rotation * Quaternion.Inverse(localGripRotation);
                    var rotation = Quaternion.Slerp(hand.rotation, handRotation, .4f + .6f * sip);
                    var authoredGrip = hand.TransformPoint(localGrip);
                    var position = Vector3.Lerp(authoredGrip, desired.position, sip) - rotation * localGrip;
                    AvatarSeatingRig.Solve(shoulder, elbow, hand, position, elbow.position - shoulder.position);
                    hand.rotation = rotation;
                    var actualGrip = new Pose(hand.TransformPoint(localGrip), hand.rotation * localGripRotation);
                    var actualCup = mug.Interaction.ItemFromGrip(actualGrip);
                    var actualRim = actualCup.position + actualCup.rotation * openingCenter + rimOffset;
                    report += $"{time},{Vector3.Distance(actualRim, mouth)},{tilt}\n";
                    for (int i = 0; i < arm.Length; i++)
                    {
                        var q = arm[i].localRotation;
                        var values = new[] { q.x, q.y, q.z, q.w };
                        for (int j = 0; j < 4; j++) curves[i][j].AddKey(time, values[j]);
                    }
                }
                var properties = new[] { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
                for (int i = 0; i < arm.Length; i++)
                    for (int j = 0; j < 4; j++)
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(
                            AnimationUtility.CalculateTransformPath(arm[i], instance.transform),
                            typeof(Transform), properties[j]), curves[i][j]);
                clip.EnsureQuaternionContinuity();
                EditorUtility.SetDirty(clip);
                File.WriteAllText("/tmp/MateSoul_Drink_MugFit.csv", report);
                File.WriteAllText("/tmp/MateSoul_Drink_MouthLocal.json", JsonUtility.ToJson(mouthLocal));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(baked);
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        public static void BindController(AnimatorController controller)
        {
            if (!controller) throw new InvalidOperationException("Missing production item controller.");
            var layer = controller.layers.FirstOrDefault(value => value.name == ItemUseAnimation.LayerName);
            if (layer == null)
            {
                controller.AddLayer(ItemUseAnimation.LayerName);
                layer = controller.layers.Last();
            }
            foreach (var guid in AssetDatabase.FindAssets("t:ItemUseAnimation", new[] { DirectoryPath }))
            {
                var animation = AssetDatabase.LoadAssetAtPath<ItemUseAnimation>(AssetDatabase.GUIDToAssetPath(guid));
                if (!animation.clip) continue;
                var state = layer.stateMachine.states.Select(value => value.state)
                    .SingleOrDefault(value => value.name == animation.clip.name) ??
                    layer.stateMachine.AddState(animation.clip.name);
                state.motion = animation.clip;
                state.speed = 0;
                state.writeDefaultValues = false;
                EditorUtility.SetDirty(state);
            }
            EditorUtility.SetDirty(controller);
        }
    }
}
