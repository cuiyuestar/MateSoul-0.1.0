using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MateSoul.Runtime;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public static class AvatarWalk01Builder
    {
        public const string SourcePath =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Walk01/Walk_01.fbx";
        private const string ReferencePath =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Models/Lengyue_LayeredDress.fbx";
        private const string TargetPath = "Assets/MateSoul/Content/Lengyue_Runtime.prefab";
        private const string ReportPath =
            "/tmp/matesoul_walk01_measurements.json";

        private static readonly string[] BodyBones =
        {
            "Hips", "Spine", "Chest", "Neck", "Head",
            "Shoulder_L", "UpperArm_L", "Forearm_L", "Hand_L",
            "Shoulder_R", "UpperArm_R", "Forearm_R", "Hand_R",
            "Leg_L", "Knee_L", "Foot_L", "Toe_L",
            "Leg_R", "Knee_R", "Foot_R", "Toe_R"
        };

        private sealed class Bone
        {
            public Transform source, target;
            public string path;
            public Quaternion referenceRotation, targetRestRotation;
            public readonly List<Quaternion> rotations = new List<Quaternion>();
        }

        private sealed class Leg
        {
            public Transform sourceKnee, sourceFoot, thigh, knee, foot;
            public Vector3 referenceKnee, referenceFoot, restKnee, restFoot;
            public readonly List<Vector3> goals = new List<Vector3>();
            public readonly List<Vector3> poles = new List<Vector3>();
        }

        [Serializable]
        private sealed class Report
        {
            public string source = SourcePath;
            public string target = TargetPath;
            public string output = AvatarLocomotionBuilder.WalkPath;
            public float lengthSeconds, frameRate, legScale;
            public int mappedBones, bindingCount;
            public float naturalSpeed, strideMeters;
            public float leftFootExcursion, rightFootExcursion;
            public float maximumLoopPositionError, maximumLoopRotationError;
            public float maximumRootDrift;
            public float maximumFootRetargetError;
            public float maximumPelvisCorrection;
            public bool distanceTimingBaked;
            public float[] supportSpeeds;
            public Vector3[] leftFeet, rightFeet, leftToes, rightToes;
            public string[] paths;
        }

        [MenuItem("MateSoul/Lengyue/Import Walk 01")]
        public static void Import()
        {
            AssetDatabase.ImportAsset(SourcePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(SourcePath);
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.resampleCurves = true;
            importer.optimizeGameObjects = false;
            importer.preserveHierarchy = true;
            importer.SaveAndReimport();
            var sourceClip = AssetDatabase.LoadAllAssetsAtPath(SourcePath)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                .OrderByDescending(clip => clip.length).First();
            var source = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath));
            var reference = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ReferencePath));
            var target = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TargetPath));
            source.hideFlags = reference.hideFlags = target.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                DisableBehaviours(source);
                DisableBehaviours(reference);
                DisableBehaviours(target);
                var sourceTransforms = Transforms(source);
                var referenceTransforms = Transforms(reference);
                var targetTransforms = Transforms(target);
                var sourceBasis = sourceTransforms["PelvisRoot"];
                var referenceBasis = referenceTransforms["PelvisRoot"];
                var targetBasis = target.GetComponentInChildren<Animator>(true).transform;
                var names = BodyBones.Concat(sourceTransforms.Keys.Where(name =>
                    name.StartsWith("Thumb_", StringComparison.Ordinal) ||
                    name.StartsWith("Index_", StringComparison.Ordinal) ||
                    name.StartsWith("Middle_", StringComparison.Ordinal) ||
                    name.StartsWith("Ring_", StringComparison.Ordinal) ||
                    name.StartsWith("Pinky_", StringComparison.Ordinal))).ToArray();
                var bones = names.Select(name =>
                {
                    var destination = targetTransforms[name == "Head" ? "Head 1" : name];
                    return new Bone
                    {
                        source = sourceTransforms[name],
                        target = destination,
                        path = AnimationUtility.CalculateTransformPath(destination, targetBasis),
                        referenceRotation = Quaternion.Inverse(referenceBasis.rotation) *
                                            referenceTransforms[name].rotation,
                        targetRestRotation = Quaternion.Inverse(targetBasis.rotation) *
                                             destination.rotation
                    };
                }).OrderBy(bone => bone.path.Count(c => c == '/')).ToArray();
                var restPosition = targetBasis.InverseTransformPoint(targetTransforms["Hips"].position);
                var referenceHips = Quaternion.Inverse(referenceBasis.rotation) *
                    (referenceTransforms["Hips"].position - referenceBasis.position);
                var legScale = LegLength(targetTransforms) / LegLength(referenceTransforms);
                var legs = new[] { "L", "R" }.Select(side => new Leg
                {
                    sourceKnee = sourceTransforms["Knee_" + side],
                    sourceFoot = sourceTransforms["Foot_" + side],
                    thigh = targetTransforms["Leg_" + side],
                    knee = targetTransforms["Knee_" + side],
                    foot = targetTransforms["Foot_" + side],
                    referenceKnee = RelativeMetres(referenceTransforms["Knee_" + side], referenceBasis),
                    referenceFoot = RelativeMetres(referenceTransforms["Foot_" + side], referenceBasis),
                    restKnee = targetBasis.InverseTransformPoint(targetTransforms["Knee_" + side].position),
                    restFoot = targetBasis.InverseTransformPoint(targetTransforms["Foot_" + side].position)
                }).ToArray();
                var frames = Mathf.RoundToInt(sourceClip.length * sourceClip.frameRate);
                var hipsPositions = new List<Vector3>();
                for (var frame = 0; frame <= frames; frame++)
                {
                    sourceClip.SampleAnimation(source, frame / sourceClip.frameRate);
                    foreach (var bone in bones)
                    {
                        var rotation = Quaternion.Inverse(sourceBasis.rotation) * bone.source.rotation *
                                       Quaternion.Inverse(bone.referenceRotation) * bone.targetRestRotation;
                        bone.rotations.Add(rotation.normalized);
                    }
                    // FBX importers may put the centimetre conversion on PelvisRoot.
                    // Rotation-relative world offsets retain metres in both imports.
                    var delta = Quaternion.Inverse(sourceBasis.rotation) *
                        (sourceTransforms["Hips"].position - sourceBasis.position) - referenceHips;
                    if (delta.magnitude > .3f)
                        throw new InvalidOperationException("Walk hips displacement exceeds the rig's metre scale.");
                    hipsPositions.Add(restPosition + delta * legScale);
                    foreach (var leg in legs)
                    {
                        leg.goals.Add(leg.restFoot + legScale *
                            (RelativeMetres(leg.sourceFoot, sourceBasis) - leg.referenceFoot));
                        leg.poles.Add(leg.restKnee + legScale *
                            (RelativeMetres(leg.sourceKnee, sourceBasis) - leg.referenceKnee));
                    }
                }

                var generated = new AnimationClip
                {
                    name = "Lengyue_Walk_BodyOnly",
                    frameRate = sourceClip.frameRate,
                    wrapMode = WrapMode.Loop
                };
                var localRotations = bones.ToDictionary(bone => bone, _ => new List<Quaternion>());
                var report = new Report
                {
                    lengthSeconds = sourceClip.length, frameRate = sourceClip.frameRate,
                    legScale = legScale, mappedBones = bones.Length,
                    paths = bones.Select(bone => bone.path).ToArray()
                };
                // Remove the small endpoint residual across the cycle; keep the authored
                // motion and make the final pose identical to the first pose.
                var hipResidual = hipsPositions[frames] - hipsPositions[0];
                var bakedHips = new List<Vector3>();
                for (var frame = 0; frame <= frames; frame++)
                {
                    var phase = (float)frame / frames;
                    foreach (var bone in bones)
                    {
                        var residual = bone.rotations[frames] *
                                       Quaternion.Inverse(bone.rotations[0]);
                        var rotation = Quaternion.Slerp(Quaternion.identity,
                            Quaternion.Inverse(residual), phase) * bone.rotations[frame];
                        bone.target.rotation = targetBasis.rotation * rotation;
                    }
                    targetTransforms["Hips"].position = targetBasis.TransformPoint(
                        hipsPositions[frame] - hipResidual * phase);
                    var hips = targetTransforms["Hips"];
                    var authoredHeight = hips.position.y;
                    var reachableHeight = authoredHeight;
                    foreach (var leg in legs)
                    {
                        var goalResidual = leg.goals[frames] - leg.goals[0];
                        var goal = targetBasis.TransformPoint(leg.goals[frame] - goalResidual * phase);
                        var reach = Vector3.Distance(leg.thigh.position, leg.knee.position) +
                                    Vector3.Distance(leg.knee.position, leg.foot.position) - .001f;
                        var horizontal = Vector3.ProjectOnPlane(goal - leg.thigh.position, Vector3.up);
                        if (horizontal.sqrMagnitude >= reach * reach)
                            throw new InvalidOperationException("Walk foot target exceeds horizontal leg reach.");
                        var thighOffset = leg.thigh.position.y - hips.position.y;
                        reachableHeight = Mathf.Min(reachableHeight, goal.y +
                            Mathf.Sqrt(reach * reach - horizontal.sqrMagnitude) - thighOffset);
                    }
                    var pelvisCorrection = authoredHeight - reachableHeight;
                    if (pelvisCorrection > .05f)
                        throw new InvalidOperationException("Walk pelvis adaptation exceeds five centimetres.");
                    report.maximumPelvisCorrection = Mathf.Max(
                        report.maximumPelvisCorrection, pelvisCorrection);
                    hips.position = new Vector3(hips.position.x, reachableHeight, hips.position.z);
                    foreach (var leg in legs)
                    {
                        var residual = leg.goals[frames] - leg.goals[0];
                        var goal = targetBasis.TransformPoint(leg.goals[frame] - residual * phase);
                        var poleResidual = leg.poles[frames] - leg.poles[0];
                        SolveLeg(leg, goal, targetBasis.TransformPoint(
                            leg.poles[frame] - poleResidual * phase));
                        report.maximumFootRetargetError = Mathf.Max(report.maximumFootRetargetError,
                            Vector3.Distance(leg.foot.position, goal));
                    }
                    foreach (var bone in bones)
                        localRotations[bone].Add(bone.target.localRotation);
                    bakedHips.Add(hips.localPosition);
                }
                foreach (var bone in bones)
                    SetRotation(generated, bone.path, localRotations[bone], generated.frameRate);
                SetPosition(generated,
                    bones.Single(bone => bone.source.name == "Hips").path,
                    bakedHips,
                    generated.frameRate);
                // Bone lengths, clothes, hair and the root remain owned by the production rig.
                generated.EnsureQuaternionContinuity();
                var settings = AnimationUtility.GetAnimationClipSettings(generated);
                settings.loopTime = true;
                settings.loopBlend = false;
                settings.keepOriginalPositionXZ = true;
                settings.keepOriginalPositionY = true;
                AnimationUtility.SetAnimationClipSettings(generated, settings);
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    AvatarLocomotionBuilder.WalkPath);
                var events = AnimationUtility.GetAnimationEvents(existing);
                AnimationUtility.SetAnimationEvents(generated, events);
                BakeDistanceTiming(generated, target, bones.Select(bone => bone.path).ToArray(), report);
                Measure(generated, target, report);
                File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
                if (report.maximumFootRetargetError > .005f ||
                    report.leftToes.Min(point => point.y) < -.03f ||
                    report.rightToes.Min(point => point.y) < -.03f)
                    throw new InvalidOperationException(
                        $"Walk foot trajectory error={report.maximumFootRetargetError:F6}m, " +
                        $"toeY={report.leftToes.Min(point => point.y):F6},{report.rightToes.Min(point => point.y):F6}m");
                EditorUtility.CopySerialized(generated, existing);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
                Object.DestroyImmediate(generated);
                ConfigureWalkPlayback();
                Debug.Log($"MATESOUL_WALK01_IMPORTED bones={report.mappedBones} " +
                          $"seconds={report.lengthSeconds:F4} referenceSpeed={report.naturalSpeed:F5} " +
                          $"stride={report.strideMeters:F5}");
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(reference);
                Object.DestroyImmediate(target);
            }
        }

        private static Vector3 RelativeMetres(Transform bone, Transform basis)
            => Quaternion.Inverse(basis.rotation) * (bone.position - basis.position);

        private static void SolveLeg(Leg leg, Vector3 goal, Vector3 pole)
        {
            var origin = leg.thigh.position;
            var footRotation = leg.foot.rotation;
            var upper = Vector3.Distance(origin, leg.knee.position);
            var lower = Vector3.Distance(leg.knee.position, leg.foot.position);
            var axis = (goal - origin).normalized;
            var distance = Mathf.Clamp(Vector3.Distance(goal, origin),
                Mathf.Abs(upper - lower) + .0001f, upper + lower - .0001f);
            var along = (upper * upper - lower * lower + distance * distance) / (2 * distance);
            var bend = Vector3.ProjectOnPlane(pole - origin, axis).normalized;
            var kneeGoal = origin + axis * along +
                           bend * Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
            leg.thigh.rotation = Quaternion.FromToRotation(leg.knee.position - origin,
                kneeGoal - origin) * leg.thigh.rotation;
            leg.knee.rotation = Quaternion.FromToRotation(leg.foot.position - leg.knee.position,
                origin + axis * distance - leg.knee.position) * leg.knee.rotation;
            leg.foot.rotation = footRotation;
        }

        // Use distance as the animation clock during authoring. Runtime then only
        // needs actual metres per second / calibrated metres per second.
        private static void BakeDistanceTiming(AnimationClip clip, GameObject target,
            string[] paths, Report report)
        {
            const float sampleRate = 120;
            var count = Mathf.RoundToInt(clip.length * sampleRate) + 1;
            var feet = new[] { target.transform.Find(paths.Single(path => path.EndsWith("/Foot_L"))),
                target.transform.Find(paths.Single(path => path.EndsWith("/Foot_R"))) };
            var tracks = feet.Select(_ => new Vector3[count]).ToArray();
            for (var frame = 0; frame < count; frame++)
            {
                clip.SampleAnimation(target, frame / sampleRate);
                for (var side = 0; side < 2; side++)
                    tracks[side][frame] = feet[side].position;
            }
            var minimum = tracks.Select(track => track.Min(point => point.y)).ToArray();
            var distance = new float[count];
            for (var frame = 1; frame < count; frame++)
            {
                var side = 0;
                var leftHeight = (tracks[0][frame].y + tracks[0][frame - 1].y) * .5f - minimum[0];
                var rightHeight = (tracks[1][frame].y + tracks[1][frame - 1].y) * .5f - minimum[1];
                if (rightHeight < leftHeight) side = 1;
                var advance = tracks[side][frame - 1].z - tracks[side][frame].z;
                distance[frame] = distance[frame - 1] + Mathf.Max(.2f / sampleRate, advance);
            }
            var transforms = paths.Select(path => target.transform.Find(path)).ToArray();
            var rotations = transforms.Select(_ => new List<Quaternion>()).ToArray();
            var hips = new List<Vector3>();
            var hipsIndex = Array.FindIndex(transforms, bone => bone.name == "Hips");
            var cursor = 1;
            for (var frame = 0; frame < count; frame++)
            {
                var desired = distance[count - 1] * frame / (count - 1);
                while (cursor < count - 1 && distance[cursor] < desired) cursor++;
                var phase = Mathf.InverseLerp(distance[cursor - 1], distance[cursor], desired);
                var sourceTime = (cursor - 1 + phase) / sampleRate;
                clip.SampleAnimation(target, sourceTime);
                for (var index = 0; index < transforms.Length; index++)
                    rotations[index].Add(transforms[index].localRotation);
                hips.Add(transforms[hipsIndex].localPosition);
            }
            for (var index = 0; index < transforms.Length; index++)
                SetRotation(clip, paths[index], rotations[index], sampleRate);
            SetPosition(clip, paths[hipsIndex], hips, sampleRate);
            clip.EnsureQuaternionContinuity();
            report.naturalSpeed = distance[count - 1] / clip.length;
            report.strideMeters = distance[count - 1];
            report.distanceTimingBaked = true;
        }

        [MenuItem("MateSoul/Lengyue/Configure Walk Playback")]
        public static void ConfigureWalkPlayback()
        {
            // Patch the existing locomotion state. Rebuilding unrelated states would
            // unnecessarily couple this import to other content still being authored.
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
                AvatarLocomotionBuilder.ControllerPath);
            var parameter = controller.parameters.FirstOrDefault(item => item.name == "WalkPlaybackRate");
            if (parameter == null)
                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = "WalkPlaybackRate", type = AnimatorControllerParameterType.Float, defaultFloat = 1
                });
            var parameters = controller.parameters;
            parameters.Single(item => item.name == "WalkPlaybackRate").defaultFloat = 1;
            controller.parameters = parameters;
            var locomotion = controller.layers[0].stateMachine.states
                .Single(child => child.state.name == "Locomotion").state;
            var blend = (UnityEditor.Animations.BlendTree)locomotion.motion;
            var children = blend.children;
            children.Single(child => child.motion is AnimationClip clip &&
                AssetDatabase.GetAssetPath(clip) == AvatarLocomotionBuilder.WalkPath);
            for (var index = 0; index < children.Length; index++)
                if (AssetDatabase.GetAssetPath(children[index].motion) == AvatarLocomotionBuilder.WalkPath)
                    children[index].threshold = AvatarMotor.FullWalkBlendSpeed;
            blend.children = children;
            locomotion.speedParameter = "WalkPlaybackRate";
            locomotion.speedParameterActive = true;
            EditorUtility.SetDirty(blend);
            EditorUtility.SetDirty(locomotion);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
        }

        private static Dictionary<string, Transform> Transforms(GameObject root)
            => root.GetComponentsInChildren<Transform>(true)
                .GroupBy(transform => transform.name)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single());

        private static void DisableBehaviours(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true))
                behaviour.enabled = false;
        }

        private static float LegLength(IReadOnlyDictionary<string, Transform> bones)
            => Vector3.Distance(bones["Leg_L"].position, bones["Knee_L"].position) +
               Vector3.Distance(bones["Knee_L"].position, bones["Foot_L"].position);

        private static void SetRotation(AnimationClip clip, string path,
            IReadOnlyList<Quaternion> samples, float rate)
        {
            var curves = Enumerable.Range(0, 4).Select(_ => new AnimationCurve()).ToArray();
            var previous = samples[0];
            for (var frame = 0; frame < samples.Count; frame++)
            {
                var value = samples[frame];
                if (Quaternion.Dot(value, previous) < 0)
                    value = new Quaternion(-value.x, -value.y, -value.z, -value.w);
                previous = value;
                for (var channel = 0; channel < 4; channel++)
                    curves[channel].AddKey(frame / rate, value[channel]);
            }
            SetCurves(clip, path, "m_LocalRotation", "xyzw", curves);
        }

        private static void SetPosition(AnimationClip clip, string path,
            IReadOnlyList<Vector3> samples, float rate)
        {
            var curves = Enumerable.Range(0, 3).Select(_ => new AnimationCurve()).ToArray();
            for (var frame = 0; frame < samples.Count; frame++)
                for (var channel = 0; channel < 3; channel++)
                    curves[channel].AddKey(frame / rate, samples[frame][channel]);
            SetCurves(clip, path, "m_LocalPosition", "xyz", curves);
        }

        private static void SetCurves(AnimationClip clip, string path,
            string property, string channels, AnimationCurve[] curves)
        {
            for (var channel = 0; channel < curves.Length; channel++)
            {
                for (var key = 0; key < curves[channel].length; key++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curves[channel], key,
                        AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curves[channel], key,
                        AnimationUtility.TangentMode.Linear);
                }
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(
                    path, typeof(Transform), property + "." + channels[channel]), curves[channel]);
            }
        }

        private static void Measure(AnimationClip clip, GameObject target, Report report)
        {
            var transforms = Transforms(target);
            var count = Mathf.RoundToInt(clip.length * clip.frameRate) + 1;
            var feet = new[] { "Foot_L", "Foot_R", "Toe_L", "Toe_R" };
            var points = feet.Select(_ => new Vector3[count]).ToArray();
            var startRotations = new Quaternion[transforms.Count];
            var rootStart = target.transform.position;
            for (var frame = 0; frame < count; frame++)
            {
                clip.SampleAnimation(target, frame / clip.frameRate);
                for (var index = 0; index < feet.Length; index++)
                    points[index][frame] = target.transform.InverseTransformPoint(
                        transforms[feet[index]].position);
                if (frame == 0)
                    startRotations = transforms.Values.Select(bone => bone.localRotation).ToArray();
                report.maximumRootDrift = Mathf.Max(report.maximumRootDrift,
                    Vector3.Distance(rootStart, target.transform.position));
            }
            report.leftFeet = points[0];
            report.rightFeet = points[1];
            report.leftToes = points[2];
            report.rightToes = points[3];
            report.maximumLoopPositionError = points.Max(p => Vector3.Distance(p[0], p[count - 1]));
            report.maximumLoopRotationError = transforms.Values.Select((bone, index) =>
                Quaternion.Angle(startRotations[index], bone.localRotation)).Max();
            report.bindingCount = AnimationUtility.GetCurveBindings(clip).Length;
            report.leftFootExcursion = points[0].Max(p => p.z) - points[0].Min(p => p.z);
            report.rightFootExcursion = points[1].Max(p => p.z) - points[1].Min(p => p.z);
            var speeds = new List<float>();
            foreach (var track in points)
            {
                var minimum = track.Min(point => point.y);
                for (var frame = 1; frame < count - 1; frame++)
                {
                    var speed = (track[frame - 1].z - track[frame + 1].z) * clip.frameRate * .5f;
                    if (track[frame].y < minimum + .025f && speed > .25f)
                        speeds.Add(speed);
                }
            }
            if (speeds.Count < 8)
                throw new InvalidOperationException("Walk 01 has insufficient planted-foot samples.");
            var sorted = speeds.OrderBy(speed => speed).ToArray();
            report.supportSpeeds = sorted;
            if (!report.distanceTimingBaked)
            {
                report.naturalSpeed = sorted[sorted.Length / 2];
                report.strideMeters = report.naturalSpeed * clip.length;
            }
        }
    }
}
