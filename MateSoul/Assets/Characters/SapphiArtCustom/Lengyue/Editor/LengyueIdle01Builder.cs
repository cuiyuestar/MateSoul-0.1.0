using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public static class LengyueIdle01Builder
    {
        public const string BodySource =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Idle01/" +
            "Lengyue_Idle01_Body_InPlace.fbx";
        public const string FaceSource =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Idle01/" +
            "Lengyue_Idle01_BlendShape_InPlace.fbx";
        public const string OutputClip =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Idle01/" +
            "Lengyue_Idle01_Combined.anim";

        private const string RuntimeController = "Assets/MateSoul/Content/Avatar.controller";
        private const string SourceController =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/" +
            "Lengyue_Locomotion.controller";

        [Serializable]
        private sealed class ImportReport
        {
            public string bodySource;
            public string faceSource;
            public string outputClip;
            public float lengthSeconds;
            public float frameRate;
            public int mappedBones;
            public int bodyBindings;
            public int facialBindings;
            public string[] facialShapes;
            public string[] excludedBones;
            public string[] missingBones;
            public bool bodyIntegrated;
            public bool facialSourceValid;
            public bool runtimeControllerUpdated;
            public bool loopEnabled;
            public float sampledPoseRotationDegrees;
            public string facialSourceIssue;
        }

        private sealed class RetargetBone
        {
            public string sourceName;
            public string targetPath;
            public Vector3 sourceRestPosition;
            public Vector3 sourceRestScale;
            public Vector3 targetRestPosition;
            public Vector3 targetRestScale;
            public Quaternion previousRotation;
            public bool hasPreviousRotation;
            public readonly AnimationCurve[] sourcePosition = new AnimationCurve[3];
            public readonly AnimationCurve[] sourceRotation = new AnimationCurve[4];
            public readonly AnimationCurve[] sourceScale = new AnimationCurve[3];
            public readonly AnimationCurve[] position =
                { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            public readonly AnimationCurve[] rotation =
            {
                new AnimationCurve(), new AnimationCurve(),
                new AnimationCurve(), new AnimationCurve()
            };
            public readonly AnimationCurve[] scale =
                { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
        }

        [MenuItem("MateSoul/Lengyue/Import Idle 01")]
        public static void Import()
        {
            ConfigureImporter(BodySource);
            ConfigureImporter(FaceSource);

            var bodySource = LongestClip(BodySource);
            var faceSource = LongestClip(FaceSource);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                LengyueLayeredDressBuilder.Prefab);
            if (!prefab)
                throw new InvalidOperationException("Canonical Lengyue prefab is missing.");

            var report = new ImportReport
            {
                bodySource = BodySource,
                faceSource = FaceSource,
                outputClip = OutputClip,
                frameRate = bodySource.frameRate
            };
            var clip = BuildClip(bodySource, faceSource, prefab, report);
            report.runtimeControllerUpdated = UpdateControllers(clip);
            ValidateClip(clip, prefab, report);
            report.bodyIntegrated = report.mappedBones > 0 &&
                                    report.sampledPoseRotationDegrees > .1f &&
                                    report.loopEnabled &&
                                    report.runtimeControllerUpdated;
            report.facialSourceValid = report.facialBindings > 0;
            if (!report.facialSourceValid)
                report.facialSourceIssue =
                    "The supplied FBX contains no SkinnedMeshRenderer blendShape curves. " +
                    "Its only imported animation targets a hair mesh transform.";

            WriteReport(report);
            AssetDatabase.SaveAssets();
            Debug.Log($"LENGYUE_IDLE01_BODY_IMPORTED bones={report.mappedBones} " +
                      $"seconds={report.lengthSeconds:F3} faceCurves={report.facialBindings}");
            if (!report.facialSourceValid)
                Debug.LogWarning("LENGYUE_IDLE01_FACE_SOURCE_INVALID " +
                                 report.facialSourceIssue);
        }

        private static void ConfigureImporter(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!importer)
                throw new InvalidOperationException("Could not configure FBX importer: " + path);
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
                .Where(item => !item.name.StartsWith("__preview__", StringComparison.Ordinal))
                .OrderByDescending(item => item.length)
                .FirstOrDefault();
            if (!clip)
                throw new InvalidOperationException("FBX contains no animation clip: " + path);
            return clip;
        }

        private static AnimationClip BuildClip(AnimationClip bodySource,
            AnimationClip faceSource, GameObject prefab, ImportReport report)
        {
            var targets = prefab.GetComponentsInChildren<Transform>(true)
                .GroupBy(item => item.name)
                .ToDictionary(group => group.Key, group => group.ToArray());
            var includedBones = new HashSet<string>();
            var excludedBones = new HashSet<string>();
            var missingBones = new HashSet<string>();
            var bodyBindings = AnimationUtility.GetCurveBindings(bodySource)
                .Where(binding => binding.type == typeof(Transform) &&
                                  IsTransformProperty(binding.propertyName))
                .ToArray();

            foreach (var binding in bodyBindings)
            {
                string sourceName = Leaf(binding.path);
                if (IsExcluded(sourceName))
                {
                    excludedBones.Add(sourceName);
                    continue;
                }
                string targetName = TargetName(sourceName);
                if (!targets.TryGetValue(targetName, out Transform[] matches) ||
                    matches.Length != 1)
                {
                    missingBones.Add(sourceName);
                    continue;
                }
                includedBones.Add(sourceName);
            }
            if (missingBones.Count > 0)
                throw new InvalidOperationException(
                    "Idle 01 target bones are missing: " +
                    string.Join(", ", missingBones.OrderBy(item => item)));

            var generated = new AnimationClip
            {
                name = "Lengyue_Idle01_Combined",
                frameRate = bodySource.frameRate,
                wrapMode = WrapMode.Loop
            };
            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BodySource);
            var sourceInstance = Object.Instantiate(sourcePrefab);
            sourceInstance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var sourceTransforms = sourceInstance.GetComponentsInChildren<Transform>(true)
                    .GroupBy(item => item.name)
                    .ToDictionary(group => group.Key, group => group.ToArray());
                var bones = includedBones.OrderBy(item => item).Select(sourceName =>
                {
                    if (!sourceTransforms.TryGetValue(
                            sourceName, out Transform[] sourceMatches) ||
                        sourceMatches.Length != 1)
                        throw new InvalidOperationException(
                            "Idle 01 source transform is not unique: " + sourceName);
                    Transform source = sourceMatches[0];
                    Transform target = targets[TargetName(sourceName)][0];
                    return new RetargetBone
                    {
                        sourceName = sourceName,
                        targetPath = AnimationUtility.CalculateTransformPath(
                            target, prefab.transform),
                        sourceRestPosition = source.localPosition,
                        sourceRestScale = source.localScale,
                        targetRestPosition = target.localPosition,
                        targetRestScale = target.localScale
                    };
                }).ToArray();
                var byName = bones.ToDictionary(item => item.sourceName);
                foreach (var binding in bodyBindings)
                {
                    if (!byName.TryGetValue(Leaf(binding.path), out RetargetBone bone))
                        continue;
                    if (AssignSourceCurve(bone, binding.propertyName,
                            AnimationUtility.GetEditorCurve(bodySource, binding)))
                        report.bodyBindings++;
                }
                foreach (var bone in bones)
                    if (bone.sourcePosition.Any(curve => curve == null) ||
                        bone.sourceRotation.Any(curve => curve == null) ||
                        bone.sourceScale.Any(curve => curve == null))
                        throw new InvalidOperationException(
                            "Idle 01 has incomplete transform curves: " + bone.sourceName);

                int frames = Mathf.RoundToInt(
                    bodySource.length * bodySource.frameRate) + 1;
                for (int frame = 0; frame < frames; frame++)
                {
                    float time = Mathf.Min(
                        bodySource.length, frame / bodySource.frameRate);
                    foreach (var bone in bones)
                    {
                        Vector3 sourcePosition = EvaluateVector(
                            bone.sourcePosition, time);
                        Quaternion rotation = EvaluateQuaternion(
                            bone.sourceRotation, time);
                        if (bone.hasPreviousRotation &&
                            Quaternion.Dot(bone.previousRotation, rotation) < 0)
                            rotation = new Quaternion(
                                -rotation.x, -rotation.y, -rotation.z, -rotation.w);
                        bone.previousRotation = rotation;
                        bone.hasPreviousRotation = true;
                        Vector3 sourceScale = EvaluateVector(bone.sourceScale, time);
                        AddKeys(bone.position, time,
                            bone.targetRestPosition +
                            sourcePosition - bone.sourceRestPosition);
                        AddKeys(bone.rotation, time, rotation);
                        AddKeys(bone.scale, time, new Vector3(
                            bone.targetRestScale.x * SafeRatio(
                                sourceScale.x, bone.sourceRestScale.x),
                            bone.targetRestScale.y * SafeRatio(
                                sourceScale.y, bone.sourceRestScale.y),
                            bone.targetRestScale.z * SafeRatio(
                                sourceScale.z, bone.sourceRestScale.z)));
                    }
                }
                foreach (var bone in bones)
                {
                    SetTransformCurves(generated, bone.targetPath,
                        "m_LocalPosition", bone.position, "xyz");
                    SetTransformCurves(generated, bone.targetPath,
                        "m_LocalRotation", bone.rotation, "xyzw");
                    SetTransformCurves(generated, bone.targetPath,
                        "m_LocalScale", bone.scale, "xyz");
                }
                report.mappedBones = bones.Length;
            }
            finally
            {
                Object.DestroyImmediate(sourceInstance);
            }

            report.facialShapes = CopyFacialCurves(faceSource, generated, prefab,
                out int facialBindings);
            report.facialBindings = facialBindings;
            report.excludedBones = excludedBones.OrderBy(item => item).ToArray();
            report.missingBones = missingBones.OrderBy(item => item).ToArray();
            generated.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(generated);
            settings.loopTime = true;
            settings.loopBlend = true;
            AnimationUtility.SetAnimationClipSettings(generated, settings);

            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(OutputClip);
            if (existing)
            {
                EditorUtility.CopySerialized(generated, existing);
                existing.name = generated.name;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(generated);
            }
            else
            {
                AssetDatabase.CreateAsset(generated, OutputClip);
                existing = generated;
            }
            AssetDatabase.SaveAssets();
            report.lengthSeconds = existing.length;
            return existing;
        }

        private static string[] CopyFacialCurves(AnimationClip source,
            AnimationClip destination, GameObject prefab, out int copied)
        {
            var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .GroupBy(item => item.name)
                .ToDictionary(group => group.Key, group => group.ToArray());
            var shapes = new HashSet<string>();
            copied = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                if (binding.type != typeof(SkinnedMeshRenderer) ||
                    !binding.propertyName.StartsWith(
                        "blendShape.", StringComparison.Ordinal))
                    continue;
                string rendererName = Leaf(binding.path);
                string shapeName = binding.propertyName.Substring("blendShape.".Length);
                if (!renderers.TryGetValue(rendererName, out SkinnedMeshRenderer[] matches) ||
                    matches.Length != 1 ||
                    matches[0].sharedMesh.GetBlendShapeIndex(shapeName) < 0)
                    throw new InvalidOperationException(
                        $"Idle 01 facial binding has no unique target: " +
                        $"{rendererName}/{shapeName}");
                string targetPath = AnimationUtility.CalculateTransformPath(
                    matches[0].transform, prefab.transform);
                AnimationUtility.SetEditorCurve(destination,
                    EditorCurveBinding.FloatCurve(targetPath,
                        typeof(SkinnedMeshRenderer), binding.propertyName),
                    AnimationUtility.GetEditorCurve(source, binding));
                shapes.Add(rendererName + "/" + shapeName);
                copied++;
            }
            return shapes.OrderBy(item => item).ToArray();
        }

        private static bool UpdateControllers(AnimationClip clip)
        {
            var runtime = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                RuntimeController);
            if (!runtime) throw new InvalidOperationException("Runtime controller is missing.");
            var locomotion = runtime.layers
                .SelectMany(layer => layer.stateMachine.states)
                .Select(child => child.state)
                .Single(state => state.name == "Locomotion");
            var blendTree = locomotion.motion as BlendTree;
            if (!blendTree || blendTree.children.Length < 2)
                throw new InvalidOperationException(
                    "Runtime locomotion blend tree is invalid.");
            var children = blendTree.children;
            children[0].motion = clip;
            blendTree.children = children;
            EditorUtility.SetDirty(blendTree);
            EditorUtility.SetDirty(runtime);

            var source = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                SourceController);
            if (source)
            {
                var idle = source.layers
                    .SelectMany(layer => layer.stateMachine.states)
                    .Select(child => child.state)
                    .SingleOrDefault(state => state.name == "idle");
                if (idle)
                {
                    idle.motion = clip;
                    EditorUtility.SetDirty(idle);
                    EditorUtility.SetDirty(source);
                }
            }
            return true;
        }

        private static void ValidateClip(AnimationClip clip, GameObject prefab,
            ImportReport report)
        {
            var instance = Object.Instantiate(prefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var bones = new[] { "Hips", "Spine", "Chest", "Head 1",
                        "UpperArm_L", "UpperArm_R", "Hand_L", "Hand_R" }
                    .Select(name => instance.GetComponentsInChildren<Transform>(true)
                        .Single(item => item.name == name))
                    .ToArray();
                clip.SampleAnimation(instance, 0f);
                var start = bones.Select(item => item.localRotation).ToArray();
                clip.SampleAnimation(instance, clip.length * .5f);
                report.sampledPoseRotationDegrees = bones
                    .Select((bone, index) =>
                        Quaternion.Angle(start[index], bone.localRotation))
                    .Max();
                report.loopEnabled =
                    AnimationUtility.GetAnimationClipSettings(clip).loopTime;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static bool AssignSourceCurve(RetargetBone bone,
            string property, AnimationCurve curve)
        {
            int separator = property.LastIndexOf('.');
            if (separator < 0 || separator == property.Length - 1) return false;
            int channel = "xyzw".IndexOf(property[separator + 1]);
            string group = property.Substring(0, separator);
            if (group == "m_LocalPosition" && channel >= 0 && channel < 3)
                bone.sourcePosition[channel] = curve;
            else if (group == "m_LocalRotation" && channel >= 0 && channel < 4)
                bone.sourceRotation[channel] = curve;
            else if (group == "m_LocalScale" && channel >= 0 && channel < 3)
                bone.sourceScale[channel] = curve;
            else
                return false;
            return true;
        }

        private static void SetTransformCurves(AnimationClip clip, string path,
            string property, AnimationCurve[] curves, string channels)
        {
            for (int channel = 0; channel < curves.Length; channel++)
            {
                var curve = curves[channel];
                for (int key = 0; key < curve.length; key++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(
                        curve, key, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(
                        curve, key, AnimationUtility.TangentMode.Linear);
                }
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(path, typeof(Transform),
                        property + "." + channels[channel]), curve);
            }
        }

        private static Vector3 EvaluateVector(AnimationCurve[] curves, float time)
            => new Vector3(curves[0].Evaluate(time), curves[1].Evaluate(time),
                curves[2].Evaluate(time));

        private static Quaternion EvaluateQuaternion(AnimationCurve[] curves, float time)
            => Quaternion.Normalize(new Quaternion(curves[0].Evaluate(time),
                curves[1].Evaluate(time), curves[2].Evaluate(time),
                curves[3].Evaluate(time)));

        private static void AddKeys(AnimationCurve[] curves, float time, Vector3 value)
        {
            curves[0].AddKey(time, value.x);
            curves[1].AddKey(time, value.y);
            curves[2].AddKey(time, value.z);
        }

        private static void AddKeys(AnimationCurve[] curves, float time, Quaternion value)
        {
            curves[0].AddKey(time, value.x);
            curves[1].AddKey(time, value.y);
            curves[2].AddKey(time, value.z);
            curves[3].AddKey(time, value.w);
        }

        private static float SafeRatio(float value, float basis)
            => Mathf.Abs(basis) > 1e-6f ? value / basis : 1f;

        private static bool IsTransformProperty(string property)
            => property.StartsWith("m_LocalPosition.", StringComparison.Ordinal) ||
               property.StartsWith("m_LocalRotation.", StringComparison.Ordinal) ||
               property.StartsWith("m_LocalScale.", StringComparison.Ordinal);

        private static string TargetName(string sourceName)
            => sourceName == "Head" ? "Head 1" : sourceName;

        private static string Leaf(string path)
        {
            int separator = path.LastIndexOf('/');
            return separator < 0 ? path : path.Substring(separator + 1);
        }

        private static bool IsExcluded(string name)
            => name == "HairRoot" ||
               name == "Lengyue_HairRig_v002" ||
               name.StartsWith("H_", StringComparison.Ordinal) ||
               name.StartsWith("Hair_", StringComparison.Ordinal) ||
               name.StartsWith("Backhair_", StringComparison.Ordinal) ||
               name.StartsWith("Skirt_", StringComparison.Ordinal) ||
               name.StartsWith("OuterSkirt_", StringComparison.Ordinal) ||
               name.StartsWith("CTRL_", StringComparison.Ordinal) ||
               name.StartsWith("DRV_", StringComparison.Ordinal) ||
               name.StartsWith("MCH_", StringComparison.Ordinal) ||
               name.EndsWith("_IK", StringComparison.Ordinal) ||
               name.Contains("_IK.") ||
               name.EndsWith("_Fulcrum", StringComparison.Ordinal);

        private static void WriteReport(ImportReport report)
        {
            string directory = Path.GetFullPath(
                Application.dataPath + "/../../ArtSource/SapphiArt/Animation/Idle01/reports");
            Directory.CreateDirectory(directory);
            File.WriteAllText(directory + "/unity_import.json",
                JsonUtility.ToJson(report, true));
        }
    }
}
