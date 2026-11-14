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
    public static class PickupAnimationBuilder
    {
        public const string DirectoryPath = "Assets/MateSoul/Content/ItemActions/Pickup";
        public const string SetPath = DirectoryPath + "/PickupAnimations.asset";
        public const string GesturePath = "Assets/MateSoul/Content/ItemActions/Gesture";
        public const string KnifeGesturePath = GesturePath + "/HandGesture_Knife.anim";
        public const string KnifeGestureSource =
            GesturePath + "/Source/Pickup_Knife_01_InPlace.fbx";

        [MenuItem("MateSoul/Game/Import Pickup Animations")]
        public static void Import()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(GameSceneBuilder.CharacterPath);
            var set = AssetDatabase.LoadAssetAtPath<PickupAnimationSet>(SetPath);
            if (!set)
            {
                set = ScriptableObject.CreateInstance<PickupAnimationSet>();
                AssetDatabase.CreateAsset(set, SetPath);
            }
            set.animations = new[] { "Floor", "Low", "Table", "High" }.Select(variant =>
            {
                var clip = Retarget(character, variant);
                return Calibrate(character, clip, clip.length * .5f);
            }).ToArray();
            var controller = character.GetComponentInChildren<Animator>().runtimeAnimatorController
                as AnimatorController;
            BindController(controller, set);
            var instance = PrefabUtility.LoadPrefabContents(GameSceneBuilder.CharacterPath);
            try
            {
                instance.GetComponent<AvatarItemRig>().ConfigurePickups(set);
                PrefabUtility.SaveAsPrefabAsset(instance, GameSceneBuilder.CharacterPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(instance); }
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            Debug.Log("MATESOUL_PICKUP_IMPORTED " + JsonUtility.ToJson(set));
        }

        [MenuItem("MateSoul/Game/Import High Pickup Animation")]
        public static void ImportHigh()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(GameSceneBuilder.CharacterPath);
            var set = AssetDatabase.LoadAssetAtPath<PickupAnimationSet>(SetPath);
            if (!character || !set)
                throw new InvalidOperationException("Production pickup content must be configured first.");
            var clip = Retarget(character, "High");
            var high = Calibrate(character, clip, clip.length * .5f);
            set.animations = set.animations.Where(value => value.clip != clip)
                .Concat(new[] { high }).OrderBy(value => value.contactGrip.y).ToArray();
            BindController(character.GetComponentInChildren<Animator>().runtimeAnimatorController
                as AnimatorController, set);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            Debug.Log($"MATESOUL_HIGH_PICKUP_IMPORTED seconds={high.Duration:F4} " +
                $"contact={high.contactTime:F4} grip={high.contactGrip}");
        }

        private static AnimationClip Retarget(GameObject character, string variant)
            => Retarget(character, DirectoryPath + "/Source/Pickup_" + variant + "_01_InPlace.fbx",
                DirectoryPath + "/Pickup_" + variant + ".anim", variant);

        public static AnimationClip RetargetAuthoredItem(GameObject character,
            string sourcePath, string outputPath) => Retarget(character, sourcePath, outputPath, null);

        [MenuItem("MateSoul/Game/Import Knife Grip Gesture")]
        public static void ImportKnifeGesture()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(GameSceneBuilder.CharacterPath);
            if (!character)
                throw new InvalidOperationException("Character prefab is missing.");
            if (AssetImporter.GetAtPath(KnifeGestureSource) == null)
                throw new InvalidOperationException(
                    "Knife grip gesture source is missing: " + KnifeGestureSource);
            var clip = RetargetHandGesture(character, KnifeGestureSource, KnifeGesturePath);
            var profile = AssetDatabase.LoadAssetAtPath<ItemInteractionProfile>(
                "Assets/MateSoul/Content/ItemActions/Knife.asset");
            if (!profile)
                throw new InvalidOperationException("Knife item profile is missing.");
            profile.handGesture = clip;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log($"MATESOUL_KNIFE_GESTURE_IMPORTED seconds={clip.length:F4} " +
                $"frames={Mathf.RoundToInt(clip.length * clip.frameRate) + 1}");
        }

        // The authored clip only moves the right hand. Baking the whole body
        // would make AnimationClip.SampleAnimation overwrite the live pose.
        public static AnimationClip RetargetHandGesture(GameObject character,
            string sourcePath, string outputPath)
        {
            PrepareAnimatedSource(sourcePath);
            var sourceClip = LoadAnimatedClip(sourcePath);
            var source = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath));
            var target = Object.Instantiate(character);
            source.hideFlags = target.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                foreach (var root in new[] { source, target })
                    foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true))
                        behaviour.enabled = false;
                var sourceBones = BoneMap(source);
                var targetBones = target.GetComponentsInChildren<Transform>(true)
                    .GroupBy(value => value.name)
                    .ToDictionary(group => group.Key, group => group.First());
                // The authored gesture rig and the runtime avatar use different
                // bone naming, so the fifteen right-hand fingers are mapped by
                // convention instead of by name lookup.
                var fingers = new[]
                {
                    ("thumb", "Thumb"), ("index", "Index"), ("middle", "Middle"),
                    ("ring", "Ring"), ("little", "Pinky")
                };
                var mapped = fingers.SelectMany(finger => Enumerable.Range(1, 3).Select(joint =>
                        targetBones["lengyue_" + finger.Item1 + "_0" + joint + "_r"]))
                    .ToArray();
                var sources = fingers.SelectMany(finger => Enumerable.Range(1, 3).Select(joint =>
                        sourceBones[finger.Item2 + "_0" + joint + "_R"]))
                    .ToArray();
                if (mapped.Length != 15)
                    throw new InvalidOperationException("Hand gesture target binding is incomplete.");
                var sourcePalm = sourceBones["Hand_R"];
                var palm = targetBones["lengyue_hand_r"];
                // The two rigs share the hand shape but not the bone axes, so
                // the authored pose is replayed as a set of bone directions
                // expressed in the palm's geometric frame. Only rotations that
                // aim each phalanx are transferred, which keeps the grip shape
                // independent of either rig's bone orientation convention.
                var align = PalmFrame(palm, mapped[3], mapped[6], mapped[12]) *
                    Quaternion.Inverse(PalmFrame(sourcePalm, sources[3], sources[6], sources[12]));
                var targetRestLocal = mapped.Select(value => value.localRotation).ToArray();
                var curves = mapped.Select(_ => Enumerable.Range(0, 4)
                    .Select(index => new AnimationCurve()).ToArray()).ToArray();
                var clip = new AnimationClip
                {
                    name = Path.GetFileNameWithoutExtension(outputPath),
                    frameRate = sourceClip.frameRate
                };
                var frameRate = sourceClip.frameRate > 0 ? sourceClip.frameRate : 30f;
                var frames = Mathf.Max(1, Mathf.RoundToInt(sourceClip.length * frameRate));
                for (var frame = 0; frame <= frames; frame++)
                {
                    var time = Mathf.Min(sourceClip.length, frame / frameRate);
                    sourceClip.SampleAnimation(source, time);
                    for (var index = 0; index < mapped.Length; index++)
                        mapped[index].localRotation = targetRestLocal[index];
                    for (var finger = 0; finger < 5; finger++)
                    {
                        var start = finger * 3;
                        for (var joint = 0; joint < 2; joint++)
                        {
                            var bone = mapped[start + joint];
                            var desired = align * (sources[start + joint + 1].position -
                                sources[start + joint].position).normalized;
                            var middle = mapped[start + joint + 1];
                            var before = middle.rotation;
                            bone.rotation = Quaternion.FromToRotation(
                                (middle.position - bone.position).normalized, desired) * bone.rotation;
                            // The distal phalanx has no child to aim at, so it
                            // repeats the bend just applied to its parent.
                            if (joint == 1)
                                mapped[start + 2].rotation =
                                    middle.rotation * Quaternion.Inverse(before) *
                                    mapped[start + 2].rotation;
                        }
                    }
                    for (var index = 0; index < mapped.Length; index++)
                    {
                        var q = mapped[index].localRotation;
                        var values = new[] { q.x, q.y, q.z, q.w };
                        for (var channel = 0; channel < values.Length; channel++)
                            curves[index][channel].AddKey(time, values[channel]);
                    }
                }
                var properties = new[] { "m_LocalRotation.x", "m_LocalRotation.y",
                    "m_LocalRotation.z", "m_LocalRotation.w" };
                for (var index = 0; index < mapped.Length; index++)
                    for (var channel = 0; channel < properties.Length; channel++)
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(
                            AnimationUtility.CalculateTransformPath(mapped[index], target.transform),
                            typeof(Transform), properties[channel]), curves[index][channel]);
                clip.EnsureQuaternionContinuity();
                var sourcePinch = Vector3.Distance(
                    sourcePalm.InverseTransformPoint(sources[2].position),
                    sourcePalm.InverseTransformPoint(sources[5].position));
                var targetPinch = Vector3.Distance(
                    palm.InverseTransformPoint(mapped[2].position),
                    palm.InverseTransformPoint(mapped[5].position));
                var bendError = 0f;
                for (var index = 0; index < mapped.Length; index += 3)
                    bendError = Mathf.Max(bendError, Mathf.Abs(
                        Bend(sources[index], sources[index + 1], sources[index + 2]) -
                        Bend(mapped[index], mapped[index + 1], mapped[index + 2])));
                Debug.Log($"MATESOUL_KNIFE_GESTURE_BAKED bones={mapped.Length} frames={frames} " +
                    $"sourcePinch={sourcePinch:F4} targetPinch={targetPinch:F4} " +
                    $"bendError={bendError:F1} sourceSide={PalmSide(sources):F4} " +
                    $"targetSide={PalmSide(mapped):F4}");
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);
                if (existing)
                {
                    EditorUtility.CopySerialized(clip, existing);
                    Object.DestroyImmediate(clip);
                }
                else { AssetDatabase.CreateAsset(clip, outputPath); existing = clip; }
                EditorUtility.SetDirty(existing);
                return existing;
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(target);
            }
        }

        private static Dictionary<string, Transform> BoneMap(GameObject root)
            => root.GetComponentsInChildren<Transform>(true)
                .GroupBy(value => value.name)
                .ToDictionary(group => group.Key, group => group.First());

        private static float Bend(Transform root, Transform middle, Transform tip)
            => Vector3.Angle(middle.position - root.position, tip.position - middle.position);

        // Builds the palm frame from joint positions so that two rigs with
        // different bone axes still agree on where the fingers point.
        private static Quaternion PalmFrame(Transform palm, Transform index,
            Transform middle, Transform little)
        {
            var forward = (middle.position - palm.position).normalized;
            var side = (index.position - little.position).normalized;
            var normal = Vector3.Cross(side, forward).normalized;
            return Quaternion.LookRotation(forward, Vector3.Cross(forward, normal));
        }

        // Signed distance of the middle fingertip from the knuckle plane tells
        // whether the baked pose curls towards the palm or towards the back.
        private static float PalmSide(Transform[] joints)
        {
            var index = joints[3].position;
            var middle = joints[6].position;
            var little = joints[12].position;
            var forward = (middle - (index + little) * .5f).normalized;
            var side = (index - little).normalized;
            return Vector3.Dot(joints[8].position - middle, Vector3.Cross(forward, side));
        }

        private static int Depth(Transform value, Transform root)
        {
            var depth = 0;
            for (var current = value; current && current != root; current = current.parent)
                depth++;
            return depth;
        }

        private static void PrepareAnimatedSource(string sourcePath)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(sourcePath);
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.resampleCurves = true;
            importer.optimizeGameObjects = false;
            importer.preserveHierarchy = true;
            importer.SaveAndReimport();
        }

        private static AnimationClip LoadAnimatedClip(string sourcePath)
            => AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>()
                .Where(value => !value.name.StartsWith("__preview__", StringComparison.Ordinal))
                .OrderByDescending(value => AnimationUtility.GetCurveBindings(value)
                    .Count(binding => binding.type == typeof(Transform))).First();

        private static AnimationClip Retarget(GameObject character, string sourcePath,
            string outputPath, string variant)
        {
            PrepareAnimatedSource(sourcePath);
            var sourceClip = LoadAnimatedClip(sourcePath);
            var source = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath));
            var reference = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Characters/LengyueCharacter/Lengyue/Runtime/Models/lengyue_model_garment.fbx"));
            var target = Object.Instantiate(character);
            source.hideFlags = reference.hideFlags = target.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                foreach (var root in new[] { source, reference, target })
                    foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true))
                        behaviour.enabled = false;
                var sourceBones = source.GetComponentsInChildren<Transform>(true)
                    .GroupBy(value => value.name).ToDictionary(group => group.Key, group => group.First());
                var referenceBones = reference.GetComponentsInChildren<Transform>(true)
                    .GroupBy(value => value.name).ToDictionary(group => group.Key, group => group.First());
                var sourceBasis = sourceBones["lengyue_pelvis_root"];
                var referenceBasis = referenceBones["lengyue_pelvis_root"];
                var targetBones = target.GetComponentsInChildren<Transform>(true);
                var names = new HashSet<string> { "lengyue_hips", "lengyue_spine", "lengyue_chest", "lengyue_neck", "lengyue_head" };
                foreach (var side in new[] { "_l", "_r" })
                {
                    foreach (var name in new[] { "lengyue_clavicle", "lengyue_upper_arm", "lengyue_forearm", "lengyue_hand",
                        "lengyue_thigh", "lengyue_shin", "lengyue_foot", "lengyue_toe", "lengyue_forearm_twist" }) names.Add(name + side);
                    foreach (var finger in new[] { "lengyue_thumb", "lengyue_index", "lengyue_middle", "lengyue_ring", "lengyue_little" })
                        for (int joint = 1; joint <= 3; joint++) names.Add(finger + "_0" + joint + side);
                }
                var mapped = targetBones.Where(value => names.Contains(
                    value.name == "lengyue_head_01" ? "lengyue_head" : value.name)).ToArray();
                if (mapped.Length != names.Count)
                    throw new InvalidOperationException("Pickup target body binding is incomplete.");
                var sources = mapped.Select(value => sourceBones[
                    value.name == "lengyue_head_01" ? "lengyue_head" : value.name]).ToArray();
                // FBX prefab transforms hold the first animated pose. The shared
                // reference model supplies the actual unposed skeleton.
                var sourceRest = sources.Select(value =>
                    Quaternion.Inverse(referenceBasis.rotation) *
                    referenceBones[value.name].rotation).ToArray();
                var targetRest = mapped.Select(value => value.rotation).ToArray();
                var targetHips = mapped.Single(value => value.name == "lengyue_hips");
                var sourceHips = sourceBones["lengyue_hips"];
                var sourceRestHip = Relative(referenceBones["lengyue_hips"], referenceBasis);
                var targetRestHip = targetHips.position;
                float LegLength(Transform thigh, Transform knee, Transform foot) =>
                    Vector3.Distance(thigh.position, knee.position) +
                    Vector3.Distance(knee.position, foot.position);
                Transform Target(string name) => mapped.Single(value => value.name == name);
                var scale = LegLength(Target("lengyue_thigh_l"), Target("lengyue_shin_l"), Target("lengyue_foot_l")) /
                    LegLength(referenceBones["lengyue_thigh_l"], referenceBones["lengyue_shin_l"], referenceBones["lengyue_foot_l"]);
                var feet = new[] { "l", "r" }.Select(side => new
                {
                    thigh = Target("lengyue_thigh_" + side), knee = Target("lengyue_shin_" + side),
                    foot = Target("lengyue_foot_" + side),
                    restFoot = Target("lengyue_foot_" + side).position,
                    restKnee = Target("lengyue_shin_" + side).position,
                    referenceFoot = Relative(referenceBones["lengyue_foot_" + side], referenceBasis),
                    referenceKnee = Relative(referenceBones["lengyue_shin_" + side], referenceBasis),
                    sourceFoot = sourceBones["lengyue_foot_" + side], sourceKnee = sourceBones["lengyue_shin_" + side]
                }).ToArray();
                var curves = mapped.Select(_ => Enumerable.Range(0, 7)
                    .Select(index => new AnimationCurve()).ToArray()).ToArray();
                var clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(outputPath),
                    frameRate = sourceClip.frameRate };
                var report = "name,sourceRestRotation,targetRestRotation\n";
                for (int i = 0; i < mapped.Length; i++)
                    report += mapped[i].name + "," + sourceRest[i] + "," + targetRest[i] + "\n";
                File.WriteAllText("/tmp/Pickup_" + variant + "_bindings.csv", report);
                for (int frame = 0; frame <= Mathf.RoundToInt(sourceClip.length * clip.frameRate); frame++)
                {
                    var time = Mathf.Min(sourceClip.length, frame / clip.frameRate);
                    // Align the stable grasp holds to normalized time .5 so the
                    // authored height variants can blend without lifting too early.
                    var sourceContact = variant == "Table" ? .8f : variant == "High" ? 1f : 1.2f;
                    var half = sourceClip.length * .5f;
                    var sourceTime = variant == null ? time : time <= half
                        ? time / half * sourceContact
                        : sourceContact + (time - half) / half * (sourceClip.length - sourceContact);
                    sourceClip.SampleAnimation(source, sourceTime);
                    // World-space rest deltas absorb the FBX's different joint axes,
                    // the additional PelvisRoot, and the production UpperChest.
                    for (int i = 0; i < mapped.Length; i++)
                        mapped[i].rotation = Quaternion.Inverse(sourceBasis.rotation) * sources[i].rotation *
                            Quaternion.Inverse(sourceRest[i]) * targetRest[i];
                    targetHips.position = targetRestHip +
                        (Relative(sourceHips, sourceBasis) - sourceRestHip) * scale;
                    foreach (var leg in feet)
                    {
                        var goal = leg.restFoot +
                            (Relative(leg.sourceFoot, sourceBasis) - leg.referenceFoot) * scale;
                        // Keep the production sole above the floor while preserving
                        // authored stepping and heel lifts from the larger source rig.
                        goal.y = Mathf.Max(goal.y, leg.restFoot.y);
                        var pole = leg.restKnee +
                            (Relative(leg.sourceKnee, sourceBasis) - leg.referenceKnee) * scale;
                        var rotation = leg.foot.rotation;
                        AvatarSeatingRig.Solve(leg.thigh, leg.knee, leg.foot, goal, pole - leg.thigh.position);
                        leg.foot.rotation = rotation;
                    }
                    if (variant == "Table")
                    {
                        // Adapt the short source reach to the production table rim.
                        // This is baked into the clip, leaving runtime IK for contact.
                        var reachWeight = sourceTime <= .8f
                            ? Mathf.SmoothStep(0, 1, sourceTime / .8f)
                            : 1 - Mathf.SmoothStep(0, 1, (sourceTime - .8f) / (sourceClip.length - .8f));
                        var sides = new[] { "_l", "_r" };
                        var goals = sides.Select(side => Target("lengyue_hand" + side).position +
                            Vector3.forward * (.14f * reachWeight)).ToArray();
                        var spine = Target("lengyue_spine");
                        spine.rotation = Quaternion.AngleAxis(18 * reachWeight, Vector3.right) * spine.rotation;
                        for (var sideIndex = 0; sideIndex < sides.Length; sideIndex++)
                        {
                            var side = sides[sideIndex];
                            var shoulder = Target("lengyue_upper_arm" + side);
                            var elbow = Target("lengyue_forearm" + side);
                            var wrist = Target("lengyue_hand" + side);
                            var rotation = wrist.rotation;
                            AvatarSeatingRig.Solve(shoulder, elbow, wrist,
                                goals[sideIndex],
                                elbow.position - shoulder.position);
                            wrist.rotation = rotation;
                        }
                    }
                    for (int i = 0; i < mapped.Length; i++)
                    {
                        var p = mapped[i].localPosition;
                        var q = mapped[i].localRotation;
                        var values = new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w };
                        for (int channel = 0; channel < values.Length; channel++)
                            curves[i][channel].AddKey(time, values[channel]);
                    }
                }
                var properties = new[] { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
                    "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
                for (int i = 0; i < mapped.Length; i++)
                    for (int channel = 0; channel < properties.Length; channel++)
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(
                            AnimationUtility.CalculateTransformPath(mapped[i], target.transform),
                            typeof(Transform), properties[channel]), curves[i][channel]);
                clip.EnsureQuaternionContinuity();
                var path = outputPath;
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (existing)
                {
                    EditorUtility.CopySerialized(clip, existing);
                    Object.DestroyImmediate(clip);
                }
                else { AssetDatabase.CreateAsset(clip, path); existing = clip; }
                EditorUtility.SetDirty(existing);
                return existing;
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(reference);
                Object.DestroyImmediate(target);
            }
        }

        private static Vector3 Relative(Transform bone, Transform basis) =>
            Quaternion.Inverse(basis.rotation) * (bone.position - basis.position);

        public static void BindController(AnimatorController controller, PickupAnimationSet set)
        {
            if (!set) return;
            var layer = controller.layers.FirstOrDefault(value => value.name == PickupAnimationSet.LayerName);
            if (layer == null)
            {
                controller.AddLayer(PickupAnimationSet.LayerName);
                layer = controller.layers.Last();
            }
            if (!controller.parameters.Any(value => value.name == PickupAnimationSet.HeightParameter))
                controller.AddParameter(PickupAnimationSet.HeightParameter, AnimatorControllerParameterType.Float);
            var state = layer.stateMachine.states.Select(value => value.state)
                .SingleOrDefault(value => value.name == PickupAnimationSet.StateName) ??
                layer.stateMachine.AddState(PickupAnimationSet.StateName);
            foreach (var child in layer.stateMachine.states)
                if (child.state != state) layer.stateMachine.RemoveState(child.state);
            var tree = state.motion as BlendTree;
            if (!tree)
            {
                tree = new BlendTree { name = PickupAnimationSet.StateName };
                AssetDatabase.AddObjectToAsset(tree, controller);
            }
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = PickupAnimationSet.HeightParameter;
            tree.useAutomaticThresholds = false;
            tree.children = set.animations.Select(value => new ChildMotion
                { motion = value.clip, threshold = value.contactGrip.y, timeScale = 1 }).ToArray();
            state.motion = tree;
            state.speed = 0;
            state.writeDefaultValues = false;
            layer.stateMachine.defaultState = state;
            EditorUtility.SetDirty(tree);
            EditorUtility.SetDirty(controller);
        }

        private static PickupAnimation Calibrate(GameObject character, AnimationClip clip, float contactTime)
        {
            var instance = Object.Instantiate(character);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var bones = instance.GetComponentsInChildren<Transform>(true);
                var hand = bones.Single(value => value.name == "lengyue_hand_r");
                var middle = bones.Single(value => value.name == "lengyue_middle_01_r");
                var hips = bones.Single(value => value.name == "lengyue_hips");
                var leftFoot = bones.Single(value => value.name == "lengyue_foot_l");
                var rightFoot = bones.Single(value => value.name == "lengyue_foot_r");
                var gripOffset = hand.InverseTransformPoint(Vector3.Lerp(hand.position, middle.position, .55f));
                var result = new PickupAnimation { clip = clip, contactTime = contactTime };
                var samples = "";
                for (int frame = 0; frame <= Mathf.RoundToInt(clip.length * clip.frameRate); frame++)
                {
                    var time = Mathf.Min(clip.length, frame / clip.frameRate);
                    clip.SampleAnimation(instance, time);
                    var grip = instance.transform.InverseTransformPoint(hand.TransformPoint(gripOffset));
                    samples += $"{time:F4},{grip.x:F5},{grip.y:F5},{grip.z:F5}," +
                        $"{hips.position.y:F5},{leftFoot.position.y:F5},{rightFoot.position.y:F5}\n";
                }
                clip.SampleAnimation(instance, contactTime);
                result.contactGrip = instance.transform.InverseTransformPoint(hand.TransformPoint(gripOffset));
                File.WriteAllText("/tmp/" + clip.name + "_trajectory.csv",
                    "time,gripX,gripY,gripZ,hipY,leftFootY,rightFootY\n" + samples);
                if (result.contactTime <= 0 || result.contactTime >= clip.length ||
                    result.contactGrip.y < -.1f || result.contactGrip.y > 1.5f)
                    throw new InvalidOperationException("Pickup contact calibration failed: " + clip.name);
                return result;
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}
