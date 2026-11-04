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

        private static AnimationClip Retarget(GameObject character, string sourcePath,
            string outputPath, string variant)
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
            var sourceClip = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>()
                .Where(value => !value.name.StartsWith("__preview__", StringComparison.Ordinal))
                .OrderByDescending(value => AnimationUtility.GetCurveBindings(value)
                    .Count(binding => binding.type == typeof(Transform))).First();
            var source = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath));
            var reference = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Models/Lengyue_LayeredDress.fbx"));
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
                var sourceBasis = sourceBones["PelvisRoot"];
                var referenceBasis = referenceBones["PelvisRoot"];
                var targetBones = target.GetComponentsInChildren<Transform>(true);
                var names = new HashSet<string> { "Hips", "Spine", "Chest", "Neck", "Head" };
                foreach (var side in new[] { "_L", "_R" })
                {
                    foreach (var name in new[] { "Shoulder", "UpperArm", "Forearm", "Hand",
                        "Leg", "Knee", "Foot", "Toe", "ForearmTwist" }) names.Add(name + side);
                    foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" })
                        for (int joint = 1; joint <= 3; joint++) names.Add(finger + "_0" + joint + side);
                }
                var mapped = targetBones.Where(value => names.Contains(
                    value.name == "Head 1" ? "Head" : value.name)).ToArray();
                if (mapped.Length != names.Count)
                    throw new InvalidOperationException("Pickup target body binding is incomplete.");
                var sources = mapped.Select(value => sourceBones[
                    value.name == "Head 1" ? "Head" : value.name]).ToArray();
                // FBX prefab transforms hold the first animated pose. The shared
                // reference model supplies the actual unposed skeleton.
                var sourceRest = sources.Select(value =>
                    Quaternion.Inverse(referenceBasis.rotation) *
                    referenceBones[value.name].rotation).ToArray();
                var targetRest = mapped.Select(value => value.rotation).ToArray();
                var targetHips = mapped.Single(value => value.name == "Hips");
                var sourceHips = sourceBones["Hips"];
                var sourceRestHip = Relative(referenceBones["Hips"], referenceBasis);
                var targetRestHip = targetHips.position;
                float LegLength(Transform thigh, Transform knee, Transform foot) =>
                    Vector3.Distance(thigh.position, knee.position) +
                    Vector3.Distance(knee.position, foot.position);
                Transform Target(string name) => mapped.Single(value => value.name == name);
                var scale = LegLength(Target("Leg_L"), Target("Knee_L"), Target("Foot_L")) /
                    LegLength(referenceBones["Leg_L"], referenceBones["Knee_L"], referenceBones["Foot_L"]);
                var feet = new[] { "L", "R" }.Select(side => new
                {
                    thigh = Target("Leg_" + side), knee = Target("Knee_" + side),
                    foot = Target("Foot_" + side),
                    restFoot = Target("Foot_" + side).position,
                    restKnee = Target("Knee_" + side).position,
                    referenceFoot = Relative(referenceBones["Foot_" + side], referenceBasis),
                    referenceKnee = Relative(referenceBones["Knee_" + side], referenceBasis),
                    sourceFoot = sourceBones["Foot_" + side], sourceKnee = sourceBones["Knee_" + side]
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
                        var sides = new[] { "_L", "_R" };
                        var goals = sides.Select(side => Target("Hand" + side).position +
                            Vector3.forward * (.14f * reachWeight)).ToArray();
                        var spine = Target("Spine");
                        spine.rotation = Quaternion.AngleAxis(18 * reachWeight, Vector3.right) * spine.rotation;
                        for (var sideIndex = 0; sideIndex < sides.Length; sideIndex++)
                        {
                            var side = sides[sideIndex];
                            var shoulder = Target("UpperArm" + side);
                            var elbow = Target("Forearm" + side);
                            var wrist = Target("Hand" + side);
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
                var hand = bones.Single(value => value.name == "Hand_R");
                var middle = bones.Single(value => value.name == "Middle_01_R");
                var hips = bones.Single(value => value.name == "Hips");
                var leftFoot = bones.Single(value => value.name == "Foot_L");
                var rightFoot = bones.Single(value => value.name == "Foot_R");
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
