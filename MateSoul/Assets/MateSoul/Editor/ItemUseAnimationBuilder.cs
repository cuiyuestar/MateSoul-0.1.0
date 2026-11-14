using System;
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
            var clip = AvatarItemAnimationRetarget.Retarget(
                DirectoryPath + "/Source/HugToy_01_InPlace.fbx", DirectoryPath + "/HugToy_01.anim");
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
