using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MateSoul.Editor
{
    // The side-sleep loop was authored on the generic Blender skeleton, so its
    // bone names (Hips/Spine/UpperArm_L/...) do not match the Lengyue rig.
    // This builder retargets it onto the canonical skeleton and registers it as
    // an override layer the lying rig can fade in once the body is on the bed.
    public static class SleepAnimationBuilder
    {
        public const string Source =
            "Assets/Characters/LengyueCharacter/Lengyue/Runtime/Animations/Lying/" +
            "Sidesleep_01_InPlace.fbx";
        public const string OutputClip =
            "Assets/Characters/LengyueCharacter/Lengyue/Runtime/Animations/Lying/" +
            "lengyue_clip_sidesleep.anim";
        public const string RuntimeController =
            "Assets/MateSoul/Content/Avatar.controller";
        // Clips played by the runtime controller must bind against the skeleton the
        // player loads (lengyue_armature_root/...), not the authoring prefab under
        // the character folder, which has a different bone chain.
        public const string TargetPrefab =
            "Assets/MateSoul/Content/lengyue_avatar_runtime.prefab";
        // Must match AvatarLyingRig.SleepLayerName / SleepStateName.
        public const string LayerName = "Lying";
        public const string StateName = "Sleep";
        private const string ClipName = "lengyue_clip_sidesleep";
        // Amplitude and rate of the generated breathing overlay, in degrees.
        private const float BreathingDegrees = 1.8f;
        private const int BreathingCycles = 2;
        // 源动作几乎没有位移，生成的呼吸循环至少要这么长，否则一帧的源时长会把
        // 呼吸压成高频抖动。
        private const float MinimumLoopSeconds = 4f;
        private const string ShapePrefix = "blendShape.";

        // The take carries no motion of its own: the side-sleep shape lives in the
        // source model's *rest* pose (PelvisRoot is rotated -90° on X, Hips 85° on
        // Z). A local delta retarget therefore evaluates to the target rest pose,
        // i.e. a standing T-pose. Instead each bone is aimed at the same child
        // direction it has in the authored pose.
        private sealed class Bone
        {
            public Transform source;
            public Transform target;
            public string targetPath;
            public Quaternion targetRestLocal;
            public Transform referenceSourceChild;
            public Transform referenceTargetChild;
            public Quaternion previousRotation;
            public bool hasPreviousRotation;
            public readonly AnimationCurve[] rotation = Empty(4);

            public static AnimationCurve[] Empty(int count)
                => Enumerable.Range(0, count).Select(_ => new AnimationCurve()).ToArray();
        }

        // Parents must be evaluated before their children when rebuilding a pose.
        private sealed class PoseNode
        {
            public Transform transform;
            public Transform parent;
            public Quaternion restLocal;
            public Bone bone;
            public AnimationCurve[] curves;
        }

        [MenuItem("MateSoul/Lengyue/Import Side Sleep")]
        public static void ImportMenu()
        {
            var clip = Import();
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                RuntimeController);
            if (controller != null)
            {
                ConfigureLayer(controller, clip);
                AssetDatabase.SaveAssets();
            }
        }

        // Called from the production controller build, so a normal package build
        // picks the sleep loop up without any manual menu step.
        public static void ConfigureRuntimeLayer(AnimatorController controller)
        {
            if (controller == null) return;
            try
            {
                // Rebuild unconditionally: the retarget changed shape completely,
                // and a cached .anim from an earlier build would keep the packed
                // game on the old (standing) pose.
                var clip = Import();
                ConfigureLayer(controller, clip);
            }
            catch (Exception error)
            {
                // A missing or broken sleep clip must never block packaging: the
                // lying rig falls back to its previous static pose.
                Debug.LogWarning("MATESOUL_SLEEP_IMPORT_FAILED " + error.Message);
            }
        }

        // Packaged builds reuse the saved scene and skip the authoring builders, so
        // this has to run from the build entry point as well: the controller the
        // scene references must own the Lying layer, otherwise the lying rig has
        // nothing to play and stays in its frozen stand pose.
        public static void PrepareForBuild()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                RuntimeController);
            if (controller == null)
            {
                Debug.LogWarning("MATESOUL_SLEEP_IMPORT_FAILED controller_missing " +
                                 RuntimeController);
                return;
            }
            ConfigureRuntimeLayer(controller);
            AssetDatabase.SaveAssets();
        }

        public static void ConfigureLayer(AnimatorController controller,
            AnimationClip clip)
        {
            if (controller == null || clip == null) return;
            var index = Array.FindIndex(controller.layers,
                layer => layer.name == LayerName);
            if (index < 0)
            {
                controller.AddLayer(LayerName);
                index = controller.layers.Length - 1;
            }
            var layers = controller.layers;
            var layer = layers[index];
            layer.defaultWeight = 0;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            layer.avatarMask = null;
            var state = layer.stateMachine.states
                            .Select(child => child.state)
                            .FirstOrDefault(value => value.name == StateName) ??
                        layer.stateMachine.AddState(StateName);
            state.motion = clip;
            state.speed = 1;
            state.writeDefaultValues = false;
            if (layer.stateMachine.defaultState == null)
                layer.stateMachine.defaultState = state;
            layers[index] = layer;
            controller.layers = layers;
            EditorUtility.SetDirty(controller);
            Debug.Log($"MATESOUL_SLEEP_LAYER_READY layer={LayerName} " +
                      $"state={StateName} clip={clip.name} " +
                      $"seconds={clip.length:F2}");
        }

        public static AnimationClip Import()
        {
            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
            if (!sourcePrefab)
            {
                // The source take is no longer part of the project: keep shipping
                // the last baked clip instead of failing the package build.
                var baked = AssetDatabase.LoadAssetAtPath<AnimationClip>(OutputClip);
                if (baked != null)
                {
                    Debug.Log("MATESOUL_SLEEP_SOURCE_RETIRED reuse=" + OutputClip);
                    return baked;
                }
                throw new InvalidOperationException(
                    "Sleep source model is missing: " + Source);
            }
            ConfigureImporter(Source);
            var sourceClip = LongestClip(Source);
            var targetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TargetPrefab);
            if (!targetPrefab)
                throw new InvalidOperationException(
                    "Runtime Lengyue prefab is missing: " + TargetPrefab);

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(OutputClip);
            bool created = clip == null;
            if (created)
                clip = new AnimationClip { name = ClipName };
            clip.ClearCurves();
            clip.frameRate = sourceClip.frameRate;
            clip.wrapMode = WrapMode.Loop;
            var mapping = BuildClip(clip, sourceClip, sourcePrefab, targetPrefab);
            // A clip with bindings but no keys plays silently: the rig would freeze
            // on the binding pose and the bug is invisible outside the .anim file.
            if (mapping.keyed == 0)
                throw new InvalidOperationException(
                    "Retargeted sleep clip has no keyframes.");
            clip.EnsureQuaternionContinuity();

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            settings.loopBlend = true;
            settings.startTime = 0;
            // clip.length is the cached length of the asset being overwritten, so it
            // can be longer than the keys just written and would append a frozen tail.
            settings.stopTime = mapping.seconds;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            if (created)
                AssetDatabase.CreateAsset(clip, OutputClip);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            Debug.Log($"MATESOUL_SLEEP_IMPORTED bones={mapping.mapped} " +
                      $"keyed={mapping.keyed} seconds={mapping.seconds:F2} " +
                      $"fps={clip.frameRate:F0} " +
                      $"start={mapping.startSeconds:F2} " +
                      $"unmapped={mapping.unmapped.Count} " +
                      $"unmappedBones={string.Join(",", mapping.unmapped.Take(12))}");
            Debug.Log($"MATESOUL_SLEEP_SHAPES target={mapping.shapesTarget} " +
                      $"animated={mapping.shapesAnimated} static={mapping.shapesStatic} " +
                      $"unmapped={mapping.shapeUnmapped.Count} " +
                      $"unmappedShapes={string.Join(",", mapping.shapeUnmapped.Take(12))}");
            if (mapping.shapesTarget > 0 &&
                mapping.shapesAnimated + mapping.shapesStatic == 0)
                Debug.LogWarning("MATESOUL_SLEEP_SHAPES_EMPTY 目标骨架有形态键但没有任何表情被写入");
            return clip;
        }

        private sealed class MappingReport
        {
            public int mapped;
            public int keyed;
            public float seconds;
            public float startSeconds;
            public int shapesTarget;
            public int shapesAnimated;
            public int shapesStatic;
            public readonly List<string> unmapped = new List<string>();
            public readonly List<string> shapeUnmapped = new List<string>();
        }

        private sealed class ShapeTarget
        {
            public string path;
        }

        private static MappingReport BuildClip(AnimationClip clip,
            AnimationClip source, GameObject sourcePrefab,
            GameObject targetPrefab)
        {
            var report = new MappingReport();
            var sourceTransforms = sourcePrefab
                .GetComponentsInChildren<Transform>(true)
                .GroupBy(item => item.name)
                .ToDictionary(group => group.Key, group => group.ToArray());
            var targetTransforms = targetPrefab
                .GetComponentsInChildren<Transform>(true)
                .GroupBy(item => item.name)
                .ToDictionary(group => group.Key, group => group.ToArray());

            var bones = new List<Bone>();
            var bySource = new Dictionary<Transform, Bone>();
            var usedTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in sourceTransforms)
            {
                if (group.Value.Length != 1) continue;
                var sourceBone = group.Value[0];
                string targetName = MapName(sourceBone.name);
                if (targetName == null) continue;
                if (!targetTransforms.TryGetValue(targetName, out var targetMatches) ||
                    targetMatches.Length != 1)
                {
                    if (!report.unmapped.Contains(targetName))
                        report.unmapped.Add(targetName);
                    continue;
                }
                if (!usedTargets.Add(targetName)) continue;
                var targetBone = targetMatches[0];
                var bone = new Bone
                {
                    source = sourceBone,
                    target = targetBone,
                    targetPath = AnimationUtility.CalculateTransformPath(
                        targetBone, targetPrefab.transform),
                    targetRestLocal = targetBone.localRotation
                };
                bones.Add(bone);
                bySource.Add(sourceBone, bone);
            }
            if (bones.Count == 0)
                throw new InvalidOperationException(
                    "Sleep source bones do not map onto the Lengyue skeleton.");

            // Each bone aims at the child bone that carries the most information
            // about its direction (the longest offset, i.e. the actual limb).
            foreach (var bone in bones)
            {
                var longest = 0f;
                foreach (Transform child in bone.source)
                {
                    if (!bySource.TryGetValue(child, out var childBone)) continue;
                    var length = child.localPosition.sqrMagnitude;
                    if (length < 1e-6f ||
                        childBone.target.localPosition.sqrMagnitude < 1e-6f) continue;
                    if (length <= longest) continue;
                    longest = length;
                    bone.referenceSourceChild = child;
                    bone.referenceTargetChild = childBone.target;
                }
            }

            var curves = SourceRotationCurves(source, sourcePrefab);
            var sourceOrder = BuildOrder(sourcePrefab.transform, bySource, curves);
            // BuildOrder resolves bones by Transform, so the target hierarchy needs
            // its own lookup. Reusing bySource here matched nothing, which left
            // every generated curve empty and made the runtime play a blank loop.
            var byTarget = bones.ToDictionary(bone => bone.target);
            var targetOrder = BuildOrder(targetPrefab.transform, byTarget, null);

            // 源动作基本是一帧静止姿态（两个相同的关键帧），自身时长可能只有一帧，
            // 直接用它当循环会让呼吸抖动成高频噪声。给生成的呼吸一个正常人节奏的
            // 循环时长。
            float duration = Mathf.Max(source.length, MinimumLoopSeconds);
            int frameCount = Mathf.Max(1, Mathf.RoundToInt(duration * source.frameRate));
            float step = duration / frameCount;
            report.seconds = duration;
            report.startSeconds = 0;
            var sourceWorld = new Dictionary<Transform, Quaternion>();
            var targetWorld = new Dictionary<Transform, Quaternion>();
            for (int frame = 0; frame <= frameCount; frame++)
            {
                float time = Mathf.Min(duration, frame * step);
                // Rebuild the authored pose in world space, top down.
                sourceWorld.Clear();
                foreach (var node in sourceOrder)
                {
                    var local = node.curves != null
                        ? EvaluateQuaternion(node.curves, time)
                        : node.restLocal;
                    sourceWorld[node.transform] = node.parent != null
                        ? sourceWorld[node.parent] * local
                        : local;
                }
                targetWorld.Clear();
                foreach (var node in targetOrder)
                {
                    var parentWorld = node.parent != null
                        ? targetWorld[node.parent]
                        : Quaternion.identity;
                    if (node.bone == null)
                    {
                        targetWorld[node.transform] = parentWorld * node.restLocal;
                        continue;
                    }
                    var restWorld = parentWorld * node.bone.targetRestLocal;
                    var world = restWorld;
                    if (node.bone.referenceSourceChild != null &&
                        sourceWorld.TryGetValue(node.bone.source, out var authored))
                    {
                        var aimed = authored *
                            node.bone.referenceSourceChild.localPosition;
                        var current = restWorld *
                            node.bone.referenceTargetChild.localPosition;
                        if (aimed.sqrMagnitude > 1e-8f && current.sqrMagnitude > 1e-8f)
                            world = Quaternion.FromToRotation(
                                current.normalized, aimed.normalized) * restWorld;
                    }
                    targetWorld[node.transform] = world;
                    var rotation = Quaternion.Inverse(parentWorld) * world *
                        Breathing(node.bone.target.name, time, duration);
                    // Unity interpolates quaternion curves per component, so a
                    // sign flip between neighbouring keys would spin the bone.
                    if (node.bone.hasPreviousRotation &&
                        Quaternion.Dot(node.bone.previousRotation, rotation) < 0)
                        rotation = new Quaternion(-rotation.x, -rotation.y,
                            -rotation.z, -rotation.w);
                    node.bone.previousRotation = rotation;
                    node.bone.hasPreviousRotation = true;
                    AddKeys(node.bone.rotation, time, rotation);
                }
            }

            foreach (var bone in bones)
                SetTransformCurves(clip, bone.targetPath,
                    "m_LocalRotation", bone.rotation, "xyzw");
            ApplyBlendShapes(clip, source, sourcePrefab, targetPrefab, report);
            report.mapped = bones.Count;
            report.keyed = bones.Count(bone => bone.rotation[0].length > 0);
            return report;
        }

        private static Dictionary<Transform, AnimationCurve[]> SourceRotationCurves(
            AnimationClip source, GameObject sourcePrefab)
        {
            const string prefix = "m_LocalRotation.";
            var curves = new Dictionary<Transform, AnimationCurve[]>();
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                if (binding.type != typeof(Transform) ||
                    !binding.propertyName.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                int channel = "xyzw".IndexOf(
                    binding.propertyName[binding.propertyName.Length - 1]);
                if (channel < 0) continue;
                var transform = sourcePrefab.transform.Find(binding.path);
                if (transform == null) continue;
                if (!curves.TryGetValue(transform, out var channels))
                    curves[transform] = channels = Bone.Empty(4);
                channels[channel] = AnimationUtility.GetEditorCurve(source, binding);
            }
            return curves;
        }

        // 表情不写在骨骼曲线上：它绑在 SkinnedMeshRenderer 的 blendShape.<name> 上，
        // 只重定向骨骼会让脸停在默认权重。这里按形态键名把源表情搬到目标网格。
        private static void ApplyBlendShapes(AnimationClip clip, AnimationClip source,
            GameObject sourcePrefab, GameObject targetPrefab, MappingReport report)
        {
            var targets = new Dictionary<string, List<ShapeTarget>>(StringComparer.Ordinal);
            foreach (var renderer in targetPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (!mesh) continue;
                var path = AnimationUtility.CalculateTransformPath(
                    renderer.transform, targetPrefab.transform);
                for (var index = 0; index < mesh.blendShapeCount; index++)
                {
                    var name = mesh.GetBlendShapeName(index);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!targets.TryGetValue(name, out var list))
                        targets[name] = list = new List<ShapeTarget>();
                    list.Add(new ShapeTarget { path = path });
                }
            }
            report.shapesTarget = targets.Count;

            var written = new HashSet<string>(StringComparer.Ordinal);
            var duration = Mathf.Max(report.seconds, .01f);
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                if (binding.type != typeof(SkinnedMeshRenderer) ||
                    !binding.propertyName.StartsWith(ShapePrefix, StringComparison.Ordinal))
                    continue;
                var name = binding.propertyName.Substring(ShapePrefix.Length);
                if (!targets.TryGetValue(name, out var matches))
                {
                    if (!report.shapeUnmapped.Contains(name))
                        report.shapeUnmapped.Add(name);
                    continue;
                }
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve == null || curve.length == 0) continue;
                // 源 take 基本静止；一帧长的曲线在循环里会抖成噪声，直接常量化。
                var baked = curve.keys[curve.length - 1].time >= .05f
                    ? curve
                    : AnimationCurve.Constant(0f, duration, curve.Evaluate(0f));
                foreach (var match in matches)
                {
                    if (!written.Add(match.path + "|" + name)) continue;
                    AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding
                    {
                        path = match.path,
                        type = typeof(SkinnedMeshRenderer),
                        propertyName = binding.propertyName
                    }, baked);
                    report.shapesAnimated++;
                }
            }

            // 源动作没有表情曲线时，表情烘在模型的默认权重里，同样要搬过来。
            if (written.Count > 0) return;
            foreach (var renderer in sourcePrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (!mesh) continue;
                for (var index = 0; index < mesh.blendShapeCount; index++)
                {
                    var weight = renderer.GetBlendShapeWeight(index);
                    if (weight <= .01f) continue;
                    var name = mesh.GetBlendShapeName(index);
                    if (!targets.TryGetValue(name, out var matches))
                    {
                        if (!report.shapeUnmapped.Contains(name))
                            report.shapeUnmapped.Add(name);
                        continue;
                    }
                    foreach (var match in matches)
                    {
                        if (!written.Add(match.path + "|" + name)) continue;
                        AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding
                        {
                            path = match.path,
                            type = typeof(SkinnedMeshRenderer),
                            propertyName = ShapePrefix + name
                        }, AnimationCurve.Constant(0f, duration, weight));
                        report.shapesStatic++;
                    }
                }
            }
        }

        // Breadth-first, so every parent is evaluated before its children.
        // boneByTransform must be keyed by the same hierarchy as root.
        private static List<PoseNode> BuildOrder(Transform root,
            Dictionary<Transform, Bone> boneByTransform,
            Dictionary<Transform, AnimationCurve[]> curves)
        {
            var order = new List<PoseNode>();
            var queue = new Queue<Transform>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var transform = queue.Dequeue();
                boneByTransform.TryGetValue(transform, out var bone);
                AnimationCurve[] rotation = null;
                if (curves != null &&
                    curves.TryGetValue(transform, out var channels) &&
                    channels.All(curve => curve != null && curve.length > 0))
                    rotation = channels;
                order.Add(new PoseNode
                {
                    transform = transform,
                    parent = transform == root ? null : transform.parent,
                    restLocal = transform.localRotation,
                    bone = bone,
                    curves = rotation
                });
                foreach (Transform child in transform) queue.Enqueue(child);
            }
            return order;
        }

        // The take is a still frame: its only motion is a 0.02° chest drift. A slow
        // breath keeps the loop reading as sleep without inventing a new pose.
        private static Quaternion Breathing(string targetName, float time, float duration)
        {
            float weight;
            if (targetName == "lengyue_chest") weight = 1f;
            else if (targetName == "lengyue_spine") weight = .45f;
            else if (targetName == "lengyue_head_01") weight = .6f;
            else return Quaternion.identity;
            float angle = BreathingDegrees * weight *
                Mathf.Sin(time / duration * Mathf.PI * 2f * BreathingCycles);
            return Quaternion.AngleAxis(angle, Vector3.right);
        }

        // Generic Blender skeleton -> Lengyue bone names.
        private static string MapName(string sourceName)
        {
            if (string.IsNullOrEmpty(sourceName) || IsRigHelper(sourceName))
                return null;
            string name = ToSnake(sourceName);
            if (name.Length == 0) return null;
            if (name.StartsWith("lower_arm_", StringComparison.Ordinal))
                name = "forearm_" + name.Substring("lower_arm_".Length);
            else if (name == "lower_arm")
                name = "forearm";
            else if (name.StartsWith("leg_", StringComparison.Ordinal))
                name = "thigh_" + name.Substring("leg_".Length);
            else if (name == "leg")
                name = "thigh";
            else if (name.StartsWith("knee_", StringComparison.Ordinal))
                name = "shin_" + name.Substring("knee_".Length);
            else if (name == "knee")
                name = "shin";
            else if (name.StartsWith("shoulder_", StringComparison.Ordinal))
                name = "clavicle_" + name.Substring("shoulder_".Length);
            else if (name == "shoulder")
                name = "clavicle";
            else if (name == "head")
                name = "head_01";
            else if (name == "head_end")
                name = "head_tip";
            else if (name.StartsWith("backhair_", StringComparison.Ordinal))
                name = "hair_back_" + name.Substring("backhair_".Length);
            else if (name.StartsWith("h_detail_", StringComparison.Ordinal))
                name = "hair_detail_" + name.Substring("h_detail_".Length);
            name = name.Replace("front_side_", "frontside_")
                .Replace("back_side_", "backside_");
            return "lengyue_" + name;
        }

        private static bool IsRigHelper(string name)
            => name.StartsWith("MCH_", StringComparison.Ordinal) ||
               name.StartsWith("DRV_", StringComparison.Ordinal) ||
               name.StartsWith("CTRL_", StringComparison.Ordinal) ||
               name.StartsWith("ORG_", StringComparison.Ordinal) ||
               name.StartsWith("DEF-", StringComparison.Ordinal) ||
               name.EndsWith("_IK", StringComparison.Ordinal) ||
               name.EndsWith("_Fulcrum", StringComparison.Ordinal) ||
               name.StartsWith("RootNode", StringComparison.Ordinal) ||
               name.StartsWith("Armature", StringComparison.Ordinal);

        private static string ToSnake(string value)
        {
            var builder = new StringBuilder(value.Length + 8);
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (char.IsUpper(character))
                {
                    if (index > 0 && value[index - 1] != '_' &&
                        !char.IsUpper(value[index - 1]))
                        builder.Append('_');
                    builder.Append(char.ToLowerInvariant(character));
                }
                else
                {
                    builder.Append(character);
                }
            }
            while (builder.ToString().Contains("__"))
                builder.Replace("__", "_");
            return builder.ToString().Trim('_');
        }

        private static void ConfigureImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!importer)
            {
                AssetDatabase.ImportAsset(path,
                    ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(path) as ModelImporter;
            }
            if (!importer)
                throw new InvalidOperationException(
                    "Could not configure FBX importer: " + path);
            // Reimporting on every build would be needlessly slow; the settings
            // only have to be applied once.
            if (importer.importAnimation &&
                importer.animationType == ModelImporterAnimationType.Generic &&
                importer.avatarSetup == ModelImporterAvatarSetup.NoAvatar &&
                importer.materialImportMode == ModelImporterMaterialImportMode.None &&
                importer.animationCompression == ModelImporterAnimationCompression.Off &&
                importer.resampleCurves &&
                !importer.optimizeGameObjects &&
                importer.preserveHierarchy)
                return;
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

        private static AnimationClip LongestClip(string path)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Where(item => !item.name.StartsWith("__preview__",
                    StringComparison.Ordinal))
                .OrderByDescending(item => item.length)
                .FirstOrDefault();
            if (!clip)
                throw new InvalidOperationException(
                    "FBX contains no animation clip: " + path);
            return clip;
        }

        private static void SetTransformCurves(AnimationClip clip, string path,
            string property, AnimationCurve[] curves, string channels)
        {
            for (int channel = 0; channel < curves.Length; channel++)
            {
                var curve = curves[channel];
                for (int key = 0; key < curve.length; key++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, key,
                        AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve, key,
                        AnimationUtility.TangentMode.Linear);
                }
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(path, typeof(Transform),
                        property + "." + channels[channel]), curve);
            }
        }

        private static Quaternion EvaluateQuaternion(AnimationCurve[] curves,
            float time)
            => Quaternion.Normalize(new Quaternion(curves[0].Evaluate(time),
                curves[1].Evaluate(time), curves[2].Evaluate(time),
                curves[3].Evaluate(time)));

        private static void AddKeys(AnimationCurve[] curves, float time,
            Quaternion value)
        {
            curves[0].AddKey(time, value.x);
            curves[1].AddKey(time, value.y);
            curves[2].AddKey(time, value.z);
            curves[3].AddKey(time, value.w);
        }
    }
}
