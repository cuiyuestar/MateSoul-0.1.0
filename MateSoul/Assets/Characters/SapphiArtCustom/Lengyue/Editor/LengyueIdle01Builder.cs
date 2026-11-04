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
        public const string Idle02BodySource =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Idle02/" +
            "Lengyue_Idle02_Body_InPlace.fbx";
        public const string Idle02FaceSource =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Idle02/" +
            "Lengyue_Idle02_BlendShape_InPlace.fbx";
        public const string Idle02OutputClip =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Idle02/" +
            "Lengyue_Idle02_Combined.anim";
        public const string SocialAttentionSource =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/" +
            "SocialAttention/idle_03_InPlace.fbx";
        public const string SocialAttentionClip =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/" +
            "SocialAttention/Lengyue_SocialAttention_Idle_BodyOnly.anim";

        private const string RuntimeController = "Assets/MateSoul/Content/Avatar.controller";
        private const string SourceController =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/" +
            "Lengyue_Locomotion.controller";
        private const float FullWalkBlendSpeed = MateSoul.Runtime.AvatarMotor.FullWalkBlendSpeed;

        [Serializable]
        private sealed class ImportReport
        {
            public string bodySource;
            public string faceSource;
            public string resolvedFaceSource;
            public string outputClip;
            public float lengthSeconds;
            public float frameRate;
            public int mappedBones;
            public int bodyBindings;
            public int facialBindings;
            public int sourceFacialBindings;
            public int designatedFacialBindings;
            public string[] facialShapes;
            public string[] excludedBones;
            public string[] missingBones;
            public bool bodyIntegrated;
            public bool facialSourceValid;
            public bool facialSourceCorrected;
            public bool facialFallbackGenerated;
            public bool runtimeControllerUpdated;
            public bool loopEnabled;
            public float sampledPoseRotationDegrees;
            public float sampledFacialWeightDelta;
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
            report.facialSourceValid = report.sourceFacialBindings > 0;
            if (!report.facialSourceValid)
                report.facialSourceIssue =
                    "The supplied FBX contains no SkinnedMeshRenderer blendShape curves. " +
                    "A generated neutral-expression fallback was added to the runtime clip.";

            WriteReport(report);
            AssetDatabase.SaveAssets();
            Debug.Log($"LENGYUE_IDLE01_BODY_IMPORTED bones={report.mappedBones} " +
                      $"seconds={report.lengthSeconds:F3} faceCurves={report.facialBindings}");
            if (!report.facialSourceValid)
                Debug.LogWarning("LENGYUE_IDLE01_FACE_SOURCE_INVALID " +
                                 report.facialSourceIssue);
        }

        [MenuItem("MateSoul/Lengyue/Import Idle 02")]
        public static void ImportIdle02()
        {
            ConfigureImporter(Idle02BodySource);
            ConfigureImporter(Idle02FaceSource);

            var bodySource = LongestClip(Idle02BodySource);
            var designatedFaceSource = LongestClip(Idle02FaceSource);
            int designatedFacialBindings = CountFacialBindings(designatedFaceSource);
            int bodyFacialBindings = CountFacialBindings(bodySource);
            var resolvedFaceSource = designatedFacialBindings > 0
                ? designatedFaceSource
                : bodyFacialBindings > 0 ? bodySource : null;
            if (!resolvedFaceSource)
                throw new InvalidOperationException(
                    "Idle 02 has no imported BlendShape animation curves in either FBX.");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                LengyueLayeredDressBuilder.Prefab);
            if (!prefab)
                throw new InvalidOperationException("Canonical Lengyue prefab is missing.");

            bool correctedSource = resolvedFaceSource == bodySource;
            var report = new ImportReport
            {
                bodySource = Idle02BodySource,
                faceSource = Idle02FaceSource,
                resolvedFaceSource = correctedSource
                    ? Idle02BodySource : Idle02FaceSource,
                outputClip = Idle02OutputClip,
                frameRate = bodySource.frameRate,
                designatedFacialBindings = designatedFacialBindings,
                facialSourceCorrected = correctedSource,
                facialSourceIssue = correctedSource
                    ? "The designated BlendShape FBX contains no BlendShape curves. " +
                      "The body FBX contains the animated facial shapes and was used."
                    : null
            };
            var clip = BuildClip(bodySource, resolvedFaceSource, prefab, report,
                Idle02BodySource, Idle02OutputClip,
                "Lengyue_Idle02_Combined", false);
            report.runtimeControllerUpdated = UpdateIdle02Controller();
            ValidateClip(clip, prefab, report);
            report.sampledFacialWeightDelta = SampleFacialWeightDelta(clip, prefab);
            report.bodyIntegrated = report.mappedBones > 0 &&
                                    report.sampledPoseRotationDegrees > .1f &&
                                    report.loopEnabled &&
                                    report.runtimeControllerUpdated;
            report.facialSourceValid = report.sourceFacialBindings > 0 &&
                                       report.sampledFacialWeightDelta > .1f;
            if (!report.bodyIntegrated)
                throw new InvalidOperationException(
                    "Idle 02 body animation validation failed.");
            if (!report.facialSourceValid)
                throw new InvalidOperationException(
                    "Idle 02 BlendShape playback validation failed.");

            WriteReport(report, "unity_import.json", "Idle02");
            AssetDatabase.SaveAssets();
            Debug.Log($"LENGYUE_IDLE02_IMPORTED bones={report.mappedBones} " +
                      $"seconds={report.lengthSeconds:F3} " +
                      $"faceCurves={report.facialBindings} " +
                      $"faceDelta={report.sampledFacialWeightDelta:F3} " +
                      $"correctedFaceSource={report.facialSourceCorrected}");
        }

        [MenuItem("MateSoul/Lengyue/Import Social Attention Idle")]
        public static void ImportSocialAttention()
        {
            ConfigureImporter(SocialAttentionSource);
            var source = LongestClip(SocialAttentionSource);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                LengyueLayeredDressBuilder.Prefab);
            if (!prefab)
                throw new InvalidOperationException("Canonical Lengyue prefab is missing.");

            var report = new ImportReport
            {
                bodySource = SocialAttentionSource,
                outputClip = SocialAttentionClip,
                frameRate = source.frameRate
            };
            var clip = BuildClip(source, null, prefab, report,
                SocialAttentionSource, SocialAttentionClip,
                "Lengyue_SocialAttention_Idle_BodyOnly", false);
            ValidateClip(clip, prefab, report);
            report.bodyIntegrated = report.mappedBones > 0 &&
                                    report.sampledPoseRotationDegrees > .1f &&
                                    report.loopEnabled;
            WriteReport(report, "social_attention_idle_import.json");
            AssetDatabase.SaveAssets();
            Debug.Log($"LENGYUE_SOCIAL_IDLE_IMPORTED bones={report.mappedBones} " +
                      $"seconds={report.lengthSeconds:F3}");
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
            => BuildClip(bodySource, faceSource, prefab, report,
                BodySource, OutputClip, "Lengyue_Idle01_Combined", true);

        private static AnimationClip BuildClip(AnimationClip bodySource,
            AnimationClip faceSource, GameObject prefab, ImportReport report,
            string bodySourcePath, string outputPath, string clipName,
            bool freezeLowerBody, bool loop = true, string[] referenceTransforms = null)
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
                if (IsExcluded(sourceName) || referenceTransforms != null &&
                    Array.IndexOf(referenceTransforms, sourceName) >= 0)
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
                name = clipName,
                frameRate = bodySource.frameRate,
                wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever
            };
            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(bodySourcePath);
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
                        float sampleTime = freezeLowerBody &&
                                           IsStationaryLowerBody(bone.sourceName)
                            ? 0 : time;
                        Vector3 sourcePosition = EvaluateVector(
                            bone.sourcePosition, sampleTime);
                        Quaternion rotation = EvaluateQuaternion(
                            bone.sourceRotation, sampleTime);
                        if (bone.hasPreviousRotation &&
                            Quaternion.Dot(bone.previousRotation, rotation) < 0)
                            rotation = new Quaternion(
                                -rotation.x, -rotation.y, -rotation.z, -rotation.w);
                        bone.previousRotation = rotation;
                        bone.hasPreviousRotation = true;
                        Vector3 sourceScale = EvaluateVector(
                            bone.sourceScale, sampleTime);
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

            if (faceSource != null)
            {
                report.facialShapes = CopyFacialCurves(faceSource, generated, prefab,
                    out int facialBindings);
                report.sourceFacialBindings = facialBindings;
                if (facialBindings == 0)
                {
                    report.facialShapes = AddFallbackFacialCurves(generated, prefab);
                    report.facialFallbackGenerated = true;
                }
                report.facialBindings = report.facialShapes.Length;
            }
            else
            {
                report.facialShapes = Array.Empty<string>();
            }
            report.excludedBones = excludedBones.OrderBy(item => item).ToArray();
            report.missingBones = missingBones.OrderBy(item => item).ToArray();
            generated.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(generated);
            settings.loopTime = loop;
            settings.loopBlend = loop;
            AnimationUtility.SetAnimationClipSettings(generated, settings);

            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);
            if (existing)
            {
                EditorUtility.CopySerialized(generated, existing);
                existing.name = generated.name;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(generated);
            }
            else
            {
                AssetDatabase.CreateAsset(generated, outputPath);
                existing = generated;
            }
            AssetDatabase.SaveAssets();
            report.lengthSeconds = existing.length;
            return existing;
        }

        // Authored one-shot actions use the same canonical bone/face remapping as idle clips.
        public static AnimationClip ImportAuthoredAction(string sourcePath,
            string outputPath, string clipName, GameObject targetPrefab, string[] referenceTransforms)
        {
            ConfigureImporter(sourcePath);
            var source = LongestClip(sourcePath);
            return BuildClip(source, source, targetPrefab, new ImportReport(),
                sourcePath, outputPath, clipName, false, false, referenceTransforms);
        }

        public static AnimationClip ImportAuthoredActionTracks(string sourcePath,
            string outputPath, string clipName, GameObject targetPrefab,
            string[] referenceTransforms, bool includeFacialCurves)
        {
            ConfigureImporter(sourcePath);
            var clips = AssetDatabase.LoadAllAssetsAtPath(sourcePath)
                .OfType<AnimationClip>()
                .Where(item => !item.name.StartsWith(
                    "__preview__", StringComparison.Ordinal))
                .ToArray();
            var body = clips
                .OrderByDescending(CountTransformBindings)
                .ThenByDescending(item => item.length)
                .FirstOrDefault();
            var face = clips
                .OrderByDescending(CountFacialBindings)
                .ThenByDescending(item => item.length)
                .FirstOrDefault();
            if (!body)
                throw new InvalidOperationException(
                    "FBX contains no body animation clip: " + sourcePath);
            if (includeFacialCurves && (!face || CountFacialBindings(face) == 0))
            {
                throw new InvalidOperationException(
                    "FBX contains no facial animation curves: " + sourcePath);
            }
            return BuildClip(body, includeFacialCurves ? face : null,
                targetPrefab, new ImportReport(),
                sourcePath, outputPath, clipName, false, false,
                referenceTransforms);
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
                if (!renderers.TryGetValue(
                        rendererName, out SkinnedMeshRenderer[] matches))
                    matches = renderers
                        .Where(entry => string.Equals(
                            entry.Key, rendererName,
                            StringComparison.OrdinalIgnoreCase))
                        .SelectMany(entry => entry.Value)
                        .ToArray();
                if (matches == null || matches.Length != 1)
                    throw new InvalidOperationException(
                        $"Idle 01 facial binding has no unique target: " +
                        $"{rendererName}/{shapeName}");
                string targetShapeName = ResolveBlendShapeName(
                    matches[0].sharedMesh, shapeName);
                if (targetShapeName == null)
                    throw new InvalidOperationException(
                        $"Idle 01 facial binding has no target shape: " +
                        $"{rendererName}/{shapeName}");
                string targetPath = AnimationUtility.CalculateTransformPath(
                    matches[0].transform, prefab.transform);
                AnimationUtility.SetEditorCurve(destination,
                    EditorCurveBinding.FloatCurve(targetPath,
                        typeof(SkinnedMeshRenderer),
                        "blendShape." + targetShapeName),
                    AnimationUtility.GetEditorCurve(source, binding));
                shapes.Add(rendererName + "/" + targetShapeName);
                copied++;
            }
            return shapes.OrderBy(item => item).ToArray();
        }

        private static int CountFacialBindings(AnimationClip clip)
            => AnimationUtility.GetCurveBindings(clip).Count(binding =>
                binding.type == typeof(SkinnedMeshRenderer) &&
                binding.propertyName.StartsWith(
                    "blendShape.", StringComparison.Ordinal));

        private static int CountTransformBindings(AnimationClip clip)
            => AnimationUtility.GetCurveBindings(clip).Count(binding =>
                binding.type == typeof(Transform) &&
                IsTransformProperty(binding.propertyName));

        private static string ResolveBlendShapeName(Mesh mesh, string sourceName)
        {
            if (mesh.GetBlendShapeIndex(sourceName) >= 0) return sourceName;
            var matches = Enumerable.Range(0, mesh.blendShapeCount)
                .Select(mesh.GetBlendShapeName)
                .Where(name => name.EndsWith(
                    sourceName, StringComparison.Ordinal))
                .ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        private static string[] AddFallbackFacialCurves(
            AnimationClip clip, GameObject prefab)
        {
            var shapes = new HashSet<string>();
            AddFacialCurve(clip, prefab, "face", "face_eye_close_L_BS", shapes,
                (0, 0), (58, 0), (61, 100), (63, 100), (66, 0),
                (152, 0), (155, 100), (157, 100), (160, 0),
                (246, 0), (249, 100), (251, 100), (254, 0), (275, 0));
            AddFacialCurve(clip, prefab, "face", "face_eye_close_R_BS", shapes,
                (0, 0), (58, 0), (61, 100), (63, 100), (66, 0),
                (152, 0), (155, 100), (157, 100), (160, 0),
                (246, 0), (249, 100), (251, 100), (254, 0), (275, 0));
            AddFacialCurve(clip, prefab, "face", "face_eyeHappy_L", shapes,
                (0, 3), (45, 5), (90, 2), (135, 6),
                (180, 3), (225, 5), (275, 3));
            AddFacialCurve(clip, prefab, "face", "face_eyeHappy_R", shapes,
                (0, 3), (45, 5), (90, 2), (135, 6),
                (180, 3), (225, 5), (275, 3));
            AddFacialCurve(clip, prefab, "face", "face_smile_BS", shapes,
                (0, 6), (45, 8), (90, 5), (135, 9),
                (180, 6), (225, 8), (275, 6));
            return shapes.OrderBy(item => item).ToArray();
        }

        private static void AddFacialCurve(AnimationClip clip, GameObject prefab,
            string rendererName, string shapeSuffix, ICollection<string> shapes,
            params (int frame, float value)[] samples)
        {
            var renderer = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Single(item => item.name == rendererName);
            var matches = Enumerable.Range(0, renderer.sharedMesh.blendShapeCount)
                .Select(renderer.sharedMesh.GetBlendShapeName)
                .Where(name => name.EndsWith(shapeSuffix, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    $"Expected one {rendererName} BlendShape ending in " +
                    $"{shapeSuffix}, found {matches.Length}.");

            var curve = new AnimationCurve(samples.Select(sample =>
                new Keyframe(sample.frame / 30f, sample.value)).ToArray());
            for (int key = 0; key < curve.length; key++)
            {
                AnimationUtility.SetKeyLeftTangentMode(
                    curve, key, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(
                    curve, key, AnimationUtility.TangentMode.Linear);
            }
            var targetPath = AnimationUtility.CalculateTransformPath(
                renderer.transform, prefab.transform);
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(targetPath,
                    typeof(SkinnedMeshRenderer), "blendShape." + matches[0]),
                curve);
            shapes.Add(rendererName + "/" + matches[0]);
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
            children[1].threshold = FullWalkBlendSpeed;
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

        private static bool UpdateIdle02Controller()
        {
            var runtime = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                RuntimeController);
            if (!runtime)
                throw new InvalidOperationException("Runtime controller is missing.");
            AvatarLocomotionBuilder.ConfigureProductionController(runtime);
            EditorUtility.SetDirty(runtime);
            AssetDatabase.SaveAssetIfDirty(runtime);
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

        private static float SampleFacialWeightDelta(
            AnimationClip clip, GameObject prefab)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip)
                .Where(binding => binding.type == typeof(SkinnedMeshRenderer) &&
                                  binding.propertyName.StartsWith(
                                      "blendShape.", StringComparison.Ordinal))
                .ToArray();
            if (bindings.Length == 0) return 0;

            var instance = Object.Instantiate(prefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .ToDictionary(
                        renderer => AnimationUtility.CalculateTransformPath(
                            renderer.transform, instance.transform));
                var samples = bindings.Select(binding =>
                {
                    if (!renderers.TryGetValue(binding.path, out var renderer))
                        throw new InvalidOperationException(
                            "Idle 02 facial renderer path is missing: " + binding.path);
                    string shapeName = binding.propertyName.Substring(
                        "blendShape.".Length);
                    int index = renderer.sharedMesh.GetBlendShapeIndex(shapeName);
                    if (index < 0)
                        throw new InvalidOperationException(
                            "Idle 02 facial shape is missing: " + shapeName);
                    return (renderer, index);
                }).ToArray();
                var minimum = Enumerable.Repeat(float.PositiveInfinity, samples.Length)
                    .ToArray();
                var maximum = Enumerable.Repeat(float.NegativeInfinity, samples.Length)
                    .ToArray();
                int frameCount = Mathf.CeilToInt(clip.length * clip.frameRate);
                for (int frame = 0; frame <= frameCount; frame++)
                {
                    clip.SampleAnimation(instance,
                        Mathf.Min(clip.length, frame / clip.frameRate));
                    for (int index = 0; index < samples.Length; index++)
                    {
                        float weight = samples[index].renderer.GetBlendShapeWeight(
                            samples[index].index);
                        minimum[index] = Mathf.Min(minimum[index], weight);
                        maximum[index] = Mathf.Max(maximum[index], weight);
                    }
                }
                return Enumerable.Range(0, samples.Length)
                    .Max(index => maximum[index] - minimum[index]);
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

        private static bool IsStationaryLowerBody(string name)
            => name == "Leg_L" || name == "Leg_R" ||
               name == "Knee_L" || name == "Knee_R" ||
               name == "Foot_L" || name == "Foot_R" ||
               name == "Toe_L" || name == "Toe_R" ||
               name.StartsWith("Mech_", StringComparison.Ordinal);

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

        private static void WriteReport(ImportReport report,
            string fileName = "unity_import.json",
            string animationName = "Idle01")
        {
            string directory = Path.GetFullPath(
                Application.dataPath + "/../../ArtSource/SapphiArt/Animation/" +
                animationName + "/reports");
            Directory.CreateDirectory(directory);
            File.WriteAllText(directory + "/" + fileName,
                JsonUtility.ToJson(report, true));
        }
    }
}
