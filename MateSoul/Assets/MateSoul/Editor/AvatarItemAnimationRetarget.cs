using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    // The authored item gestures export the VRoid/VRM (J_Bip_*) skeleton while
    // the production avatar uses the lengyue_* rig. This bakes them bone by
    // bone with the same world-space rest deltas as the walk retarget.
    public static class AvatarItemAnimationRetarget
    {
        private const string TargetPath = GameSceneBuilder.CharacterPath;

        private static readonly Dictionary<string, string> BoneMap =
            new Dictionary<string, string>
            {
                { "J_Bip_C_Hips", "lengyue_hips" },
                { "J_Bip_C_Spine", "lengyue_spine" },
                { "J_Bip_C_Chest", "lengyue_chest" },
                { "J_Bip_C_UpperChest", "lengyue_upper_chest" },
                { "J_Bip_C_Neck", "lengyue_neck" },
                { "J_Bip_C_Head", "lengyue_head_01" },
                { "J_Bip_L_Shoulder", "lengyue_clavicle_l" },
                { "J_Bip_L_UpperArm", "lengyue_upper_arm_l" },
                { "J_Bip_L_LowerArm", "lengyue_forearm_l" },
                { "J_Bip_L_Hand", "lengyue_hand_l" },
                { "J_Bip_L_UpperLeg", "lengyue_thigh_l" },
                { "J_Bip_L_LowerLeg", "lengyue_shin_l" },
                { "J_Bip_L_Foot", "lengyue_foot_l" },
                { "J_Bip_L_ToeBase", "lengyue_toe_l" },
                { "J_Bip_R_Shoulder", "lengyue_clavicle_r" },
                { "J_Bip_R_UpperArm", "lengyue_upper_arm_r" },
                { "J_Bip_R_LowerArm", "lengyue_forearm_r" },
                { "J_Bip_R_Hand", "lengyue_hand_r" },
                { "J_Bip_R_UpperLeg", "lengyue_thigh_r" },
                { "J_Bip_R_LowerLeg", "lengyue_shin_r" },
                { "J_Bip_R_Foot", "lengyue_foot_r" },
                { "J_Bip_R_ToeBase", "lengyue_toe_r" },
                { "J_Bip_L_Thumb1", "lengyue_thumb_01_l" },
                { "J_Bip_L_Thumb2", "lengyue_thumb_02_l" },
                { "J_Bip_L_Thumb3", "lengyue_thumb_03_l" },
                { "J_Bip_L_Index1", "lengyue_index_01_l" },
                { "J_Bip_L_Index2", "lengyue_index_02_l" },
                { "J_Bip_L_Index3", "lengyue_index_03_l" },
                { "J_Bip_L_Middle1", "lengyue_middle_01_l" },
                { "J_Bip_L_Middle2", "lengyue_middle_02_l" },
                { "J_Bip_L_Middle3", "lengyue_middle_03_l" },
                { "J_Bip_L_Ring1", "lengyue_ring_01_l" },
                { "J_Bip_L_Ring2", "lengyue_ring_02_l" },
                { "J_Bip_L_Ring3", "lengyue_ring_03_l" },
                { "J_Bip_L_Little1", "lengyue_little_01_l" },
                { "J_Bip_L_Little2", "lengyue_little_02_l" },
                { "J_Bip_L_Little3", "lengyue_little_03_l" },
                { "J_Bip_R_Thumb1", "lengyue_thumb_01_r" },
                { "J_Bip_R_Thumb2", "lengyue_thumb_02_r" },
                { "J_Bip_R_Thumb3", "lengyue_thumb_03_r" },
                { "J_Bip_R_Index1", "lengyue_index_01_r" },
                { "J_Bip_R_Index2", "lengyue_index_02_r" },
                { "J_Bip_R_Index3", "lengyue_index_03_r" },
                { "J_Bip_R_Middle1", "lengyue_middle_01_r" },
                { "J_Bip_R_Middle2", "lengyue_middle_02_r" },
                { "J_Bip_R_Middle3", "lengyue_middle_03_r" },
                { "J_Bip_R_Ring1", "lengyue_ring_01_r" },
                { "J_Bip_R_Ring2", "lengyue_ring_02_r" },
                { "J_Bip_R_Ring3", "lengyue_ring_03_r" },
                { "J_Bip_R_Little1", "lengyue_little_01_r" },
                { "J_Bip_R_Little2", "lengyue_little_02_r" },
                { "J_Bip_R_Little3", "lengyue_little_03_r" }
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
            public Transform sourceRoot, sourceKnee, sourceFoot, thigh, knee, foot;
            public Vector3 referenceKnee, referenceFoot, restKnee, restFoot;
            public readonly List<Vector3> goals = new List<Vector3>();
            public readonly List<Vector3> poles = new List<Vector3>();
        }

        public static AnimationClip Retarget(string sourcePath, string outputPath)
        {
            AssetDatabase.ImportAsset(sourcePath,
                ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(sourcePath);
            if (importer == null)
                throw new InvalidOperationException("Missing animation source: " + sourcePath);
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.resampleCurves = true;
            importer.optimizeGameObjects = false;
            importer.preserveHierarchy = true;
            importer.SaveAndReimport();

            var sourceClip = AssetDatabase.LoadAllAssetsAtPath(sourcePath)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                .OrderByDescending(clip => AnimationUtility.GetCurveBindings(clip)
                    .Count(binding => binding.type == typeof(Transform)))
                .First();
            var source = Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath));
            var target = Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>(TargetPath));
            source.hideFlags = target.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                DisableBehaviours(source);
                DisableBehaviours(target);
                var sourceTransforms = Transforms(source);
                var targetTransforms = Transforms(target);
                var sourceHips = sourceTransforms["J_Bip_C_Hips"];
                // The armature root is the rigid frame the source gesture is authored in.
                var sourceBasis = sourceHips.parent != null ? sourceHips.parent : sourceHips;
                var targetBasis = target.GetComponentInChildren<Animator>(true).transform;
                var bones = BoneMap.Select(pair =>
                {
                    var destination = targetTransforms[pair.Value];
                    return new Bone
                    {
                        source = sourceTransforms[pair.Key],
                        target = destination,
                        path = AnimationUtility.CalculateTransformPath(destination, targetBasis),
                        referenceRotation = Quaternion.Inverse(sourceBasis.rotation) *
                                            sourceTransforms[pair.Key].rotation,
                        targetRestRotation = Quaternion.Inverse(targetBasis.rotation) *
                                             destination.rotation
                    };
                }).OrderBy(bone => bone.path.Count(c => c == '/')).ToArray();
                var restPosition = targetBasis.InverseTransformPoint(
                    targetTransforms["lengyue_hips"].position);
                var referenceHips = Quaternion.Inverse(sourceBasis.rotation) *
                    (sourceHips.position - sourceBasis.position);
                var legScale = LegLength(targetTransforms,
                                     "lengyue_thigh_l", "lengyue_shin_l", "lengyue_foot_l") /
                               LegLength(sourceTransforms,
                                     "J_Bip_L_UpperLeg", "J_Bip_L_LowerLeg", "J_Bip_L_Foot");
                var legs = new[] { "l", "r" }.Select(side =>
                {
                    var sourceSide = side == "l" ? "L" : "R";
                    var sourceKnee = sourceTransforms["J_Bip_" + sourceSide + "_LowerLeg"];
                    var sourceFoot = sourceTransforms["J_Bip_" + sourceSide + "_Foot"];
                    return new Leg
                    {
                        sourceKnee = sourceKnee,
                        sourceFoot = sourceFoot,
                        thigh = targetTransforms["lengyue_thigh_" + side],
                        knee = targetTransforms["lengyue_shin_" + side],
                        foot = targetTransforms["lengyue_foot_" + side],
                        referenceKnee = RelativeMetres(sourceKnee, sourceBasis),
                        referenceFoot = RelativeMetres(sourceFoot, sourceBasis),
                        restKnee = targetBasis.InverseTransformPoint(
                            targetTransforms["lengyue_shin_" + side].position),
                        restFoot = targetBasis.InverseTransformPoint(
                            targetTransforms["lengyue_foot_" + side].position)
                    };
                }).ToArray();
                // Arms are retargeted as reach targets, exactly like the legs. The
                // gesture lifts them from the T-pose to a chest-level hold, and
                // rotations cannot be carried across that change without inventing
                // a twist on every joint, which reads as a mangled arm. The wrist
                // and elbow are mirrored as offsets from the shoulder - not from
                // the rest wrist, which already sits at full extension, so adding
                // a displacement to it would always exceed the arm's reach.
                var armScale = LegLength(targetTransforms,
                                    "lengyue_upper_arm_l", "lengyue_forearm_l", "lengyue_hand_l") /
                               LegLength(sourceTransforms,
                                    "J_Bip_L_UpperArm", "J_Bip_L_LowerArm", "J_Bip_L_Hand");
                var arms = new[] { "l", "r" }.Select(side =>
                {
                    var sourceSide = side == "l" ? "L" : "R";
                    return new Leg
                    {
                        sourceRoot = sourceTransforms["J_Bip_" + sourceSide + "_UpperArm"],
                        sourceKnee = sourceTransforms["J_Bip_" + sourceSide + "_LowerArm"],
                        sourceFoot = sourceTransforms["J_Bip_" + sourceSide + "_Hand"],
                        thigh = targetTransforms["lengyue_upper_arm_" + side],
                        knee = targetTransforms["lengyue_forearm_" + side],
                        foot = targetTransforms["lengyue_hand_" + side]
                    };
                }).ToArray();
                // Hand orientation and every finger joint are re-based on the
                // gesture's own first frame: each joint is aligned from its
                // production rest direction + palm-plane roll to the authored
                // first-frame direction + roll. All measurements run in world space
                // and the alignment is conjugated into the target basis, so
                // differing armature import rotations (Blender's -90 degrees X, for
                // instance) cannot skew the result. Arm and forearm keep the
                // rotation they get here only as a starting guess; the reach
                // targets solved below replace it.
                sourceClip.SampleAnimation(source, 0);
                var digits = new[]
                {
                    new[] { "Thumb", "thumb" }, new[] { "Index", "index" },
                    new[] { "Middle", "middle" }, new[] { "Ring", "ring" },
                    new[] { "Little", "little" }
                };
                foreach (var side in new[] { "l", "r" })
                {
                    var sourceSide = side == "l" ? "L" : "R";
                    var sourceAcross = sourceTransforms["J_Bip_" + sourceSide + "_Index1"].position -
                        sourceTransforms["J_Bip_" + sourceSide + "_Little1"].position;
                    var targetAcross = targetTransforms["lengyue_index_01_" + side].position -
                        targetTransforms["lengyue_little_01_" + side].position;
                    RebaseArmChainBone(
                        bones.Single(bone => bone.target.name == "lengyue_hand_" + side),
                        sourceTransforms["J_Bip_" + sourceSide + "_Hand"],
                        sourceTransforms["J_Bip_" + sourceSide + "_Index1"],
                        targetTransforms["lengyue_hand_" + side],
                        targetTransforms["lengyue_index_01_" + side],
                        sourceAcross, targetAcross, sourceBasis, targetBasis, false);
                    foreach (var digit in digits)
                        for (var joint = 1; joint <= 3; joint++)
                        {
                            var sourceName = "J_Bip_" + sourceSide + "_" + digit[0] + joint;
                            var targetName = "lengyue_" + digit[1] + "_0" + joint + "_" + side;
                            // The last finger joint has no child in the map; aligning
                            // it on its own offset from the previous joint keeps the
                            // tip parallel to its parent.
                            var sourceNext = sourceTransforms[
                                "J_Bip_" + sourceSide + "_" + digit[0] + (joint < 3 ? joint + 1 : 2)];
                            var targetNext = targetTransforms[
                                "lengyue_" + digit[1] + "_0" + (joint < 3 ? joint + 1 : 2) + "_" + side];
                            RebaseArmChainBone(
                                bones.Single(bone => bone.target.name == targetName),
                                sourceTransforms[sourceName], sourceNext,
                                targetTransforms[targetName], targetNext,
                                sourceAcross, targetAcross, sourceBasis, targetBasis, joint == 3);
                        }
                }

                var frameRate = sourceClip.frameRate > 0 ? sourceClip.frameRate : 30f;
                var frames = Mathf.RoundToInt(sourceClip.length * frameRate);
                var hipsPositions = new List<Vector3>();
                for (var frame = 0; frame <= frames; frame++)
                {
                    sourceClip.SampleAnimation(source, frame / frameRate);
                    foreach (var bone in bones)
                        bone.rotations.Add((Quaternion.Inverse(sourceBasis.rotation) *
                            bone.source.rotation * Quaternion.Inverse(bone.referenceRotation) *
                            bone.targetRestRotation).normalized);
                    var delta = Quaternion.Inverse(sourceBasis.rotation) *
                        (sourceHips.position - sourceBasis.position) - referenceHips;
                    if (delta.magnitude > .3f)
                        throw new InvalidOperationException(
                            "Gesture hips displacement exceeds the rig's metre scale: " +
                            Path.GetFileName(sourcePath));
                    hipsPositions.Add(restPosition + delta * legScale);
                    foreach (var leg in legs)
                    {
                        leg.goals.Add(leg.restFoot + legScale *
                            (RelativeMetres(leg.sourceFoot, sourceBasis) - leg.referenceFoot));
                        leg.poles.Add(leg.restKnee + legScale *
                            (RelativeMetres(leg.sourceKnee, sourceBasis) - leg.referenceKnee));
                    }
                    // Arm goals and poles hold the wrist and elbow offsets from
                    // the shoulder; the shoulder itself only settles once the
                    // torso pose is baked, so these are resolved per frame.
                    foreach (var arm in arms)
                    {
                        var shoulder = RelativeMetres(arm.sourceRoot, sourceBasis);
                        arm.goals.Add(armScale *
                            (RelativeMetres(arm.sourceFoot, sourceBasis) - shoulder));
                        arm.poles.Add(armScale *
                            (RelativeMetres(arm.sourceKnee, sourceBasis) - shoulder));
                    }
                }

                var generated = new AnimationClip
                {
                    name = Path.GetFileNameWithoutExtension(outputPath),
                    frameRate = frameRate
                };
                var localRotations = bones.ToDictionary(bone => bone, _ => new List<Quaternion>());
                var bakedHips = new List<Vector3>();
                var maximumFootRetargetError = 0f;
                for (var frame = 0; frame <= frames; frame++)
                {
                    foreach (var bone in bones)
                        bone.target.rotation = targetBasis.rotation * bone.rotations[frame];
                    var hips = targetTransforms["lengyue_hips"];
                    hips.position = targetBasis.TransformPoint(hipsPositions[frame]);
                    // Clamp the pelvis to the height the IK legs can actually reach,
                    // so crouches cannot stretch or pop the solved legs.
                    var reachableHeight = hips.position.y;
                    foreach (var leg in legs)
                    {
                        var goal = targetBasis.TransformPoint(leg.goals[frame]);
                        var reach = Vector3.Distance(leg.thigh.position, leg.knee.position) +
                                    Vector3.Distance(leg.knee.position, leg.foot.position) - .001f;
                        var horizontal = Vector3.ProjectOnPlane(goal - leg.thigh.position, Vector3.up);
                        var thighOffset = leg.thigh.position.y - hips.position.y;
                        reachableHeight = Mathf.Min(reachableHeight, goal.y +
                            Mathf.Sqrt(Mathf.Max(0, reach * reach - horizontal.sqrMagnitude)) - thighOffset);
                    }
                    hips.position = new Vector3(hips.position.x, reachableHeight, hips.position.z);
                    foreach (var leg in legs)
                    {
                        var goal = targetBasis.TransformPoint(leg.goals[frame]);
                        SolveLeg(leg, goal, targetBasis.TransformPoint(leg.poles[frame]));
                        maximumFootRetargetError = Mathf.Max(maximumFootRetargetError,
                            Vector3.Distance(leg.foot.position, goal));
                    }
                    // The authored wrist can sit further out than the production
                    // arm reaches; the solver clamps to the chain's own length, so
                    // the hand ends up as close as the rig allows instead of
                    // stretching the arm. SolveLeg restores the hand's transferred
                    // orientation, which keeps the palm and fingers authored.
                    foreach (var arm in arms)
                    {
                        var shoulder = arm.thigh.position;
                        SolveLeg(arm,
                            shoulder + targetBasis.rotation * arm.goals[frame],
                            shoulder + targetBasis.rotation * arm.poles[frame]);
                    }
                    foreach (var bone in bones)
                        localRotations[bone].Add(bone.target.localRotation);
                    bakedHips.Add(hips.localPosition);
                }
                if (maximumFootRetargetError > .005f)
                    throw new InvalidOperationException(
                        $"Gesture foot retarget error={maximumFootRetargetError:F6}m: " +
                        Path.GetFileName(sourcePath));
                // Facial animation lives on blend shape curves, not on transforms, so
                // the bone pass above drops it. Both rigs use the VRoid Fcl_* naming,
                // so every authored shape is re-bound by name onto whichever target
                // renderer actually carries it.
                var faceShapes = FaceShapes(target);
                var copiedShapes = 0;
                foreach (var binding in AnimationUtility.GetCurveBindings(sourceClip))
                {
                    if (!binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                        continue;
                    var shape = binding.propertyName.Substring("blendShape.".Length);
                    if (!faceShapes.TryGetValue(shape, out var renderer)) continue;
                    var curve = AnimationUtility.GetEditorCurve(sourceClip, binding);
                    if (curve == null) continue;
                    AnimationUtility.SetEditorCurve(generated, EditorCurveBinding.FloatCurve(
                        AnimationUtility.CalculateTransformPath(renderer.transform, targetBasis),
                        typeof(SkinnedMeshRenderer), binding.propertyName), curve);
                    copiedShapes++;
                }
                Debug.Log($"MATESOUL_ITEM_RETARGET {Path.GetFileName(outputPath)} " +
                    $"frames={frames + 1} seconds={sourceClip.length:F4} " +
                    $"footError={maximumFootRetargetError:F6} faceShapes={copiedShapes}");
                foreach (var bone in bones)
                    SetRotation(generated, bone.path, localRotations[bone], generated.frameRate);
                SetPosition(generated,
                    bones.Single(bone => bone.target.name == "lengyue_hips").path,
                    bakedHips, generated.frameRate);
                generated.EnsureQuaternionContinuity();
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);
                if (existing)
                {
                    EditorUtility.CopySerialized(generated, existing);
                    Object.DestroyImmediate(generated);
                }
                else
                {
                    AssetDatabase.CreateAsset(generated, outputPath);
                    existing = generated;
                }
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return existing;
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(target);
            }
        }

        private static Vector3 RelativeMetres(Transform bone, Transform basis)
            => Quaternion.Inverse(basis.rotation) * (bone.position - basis.position);

        // Builds an orientation from a bone direction plus an across (roll)
        // reference so both direction and twist can be aligned between rigs.
        private static Quaternion Frame(Vector3 forward, Vector3 across)
        {
            var direction = forward.normalized;
            var roll = Vector3.ProjectOnPlane(across, direction);
            if (roll.sqrMagnitude < 1e-6f) roll = Vector3.up;
            return Quaternion.LookRotation(direction, roll.normalized);
        }

        // Aligns one arm-chain joint from its production rest orientation to
        // the authored first-frame orientation (direction + palm-plane roll),
        // then re-bases the retarget reference on the first frame so the
        // authored deltas drive the joint around the corrected base.
        private static void RebaseArmChainBone(Bone bone,
            Transform source, Transform sourceNext, Transform target, Transform targetNext,
            Vector3 sourceAcross, Vector3 targetAcross,
            Transform sourceBasis, Transform targetBasis, bool tip)
        {
            var sourceForward = tip ? source.position - sourceNext.position
                                    : sourceNext.position - source.position;
            var targetForward = tip ? target.position - targetNext.position
                                    : targetNext.position - target.position;
            var alignment = Frame(sourceForward, sourceAcross) *
                            Quaternion.Inverse(Frame(targetForward, targetAcross));
            bone.referenceRotation = Quaternion.Inverse(sourceBasis.rotation) * source.rotation;
            bone.targetRestRotation = Quaternion.Inverse(targetBasis.rotation) * alignment *
                targetBasis.rotation * bone.targetRestRotation;
        }

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

        private static Dictionary<string, Transform> Transforms(GameObject root)
            => root.GetComponentsInChildren<Transform>(true)
                .GroupBy(transform => transform.name)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single());

        // VRoid splits the face across several meshes, so shapes are looked up by name
        // instead of by renderer name.
        private static Dictionary<string, SkinnedMeshRenderer> FaceShapes(GameObject target)
        {
            var result = new Dictionary<string, SkinnedMeshRenderer>();
            foreach (var renderer in target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null) continue;
                for (var index = 0; index < mesh.blendShapeCount; index++)
                {
                    var name = mesh.GetBlendShapeName(index);
                    if (!result.ContainsKey(name)) result[name] = renderer;
                }
            }
            return result;
        }

        private static void DisableBehaviours(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true))
                behaviour.enabled = false;
        }

        private static float LegLength(IReadOnlyDictionary<string, Transform> bones,
            string thigh, string knee, string foot)
            => Vector3.Distance(bones[thigh].position, bones[knee].position) +
               Vector3.Distance(bones[knee].position, bones[foot].position);

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
    }
}
