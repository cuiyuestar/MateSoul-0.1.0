﻿﻿﻿﻿﻿using System;
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
            "Assets/Characters/LengyueCharacter/Lengyue/Runtime/Animations/Walk02/Walk_02_InPlace.fbx";
        private const string TargetPath = "Assets/MateSoul/Content/lengyue_avatar_runtime.prefab";
        private const string ReportPath =
            "/tmp/matesoul_walk02_measurements.json";

        // The authored source exports the VRoid/VRM skeleton (J_Bip_*), while the
        // production avatar uses the lengyue_* rig. Retarget happens bone by bone.
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
            public float minimumGoalHeight, maximumHorizontalReach;
            public float hipsRestHeight, footRestHeight;
            public float[] sourceFootZMin, sourceFootZMax;
            public float[] pelvisCorrections;
            public bool distanceTimingBaked;
            public float[] supportSpeeds;
            public Vector3[] leftFeet, rightFeet, leftToes, rightToes, leftHands, rightHands;
            public string[] paths;
        }

        [MenuItem("MateSoul/Lengyue/Import Walk 02")]
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
            var target = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TargetPath));
            source.hideFlags = target.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                DisableBehaviours(source);
                DisableBehaviours(target);
                var sourceTransforms = Transforms(source);
                var targetTransforms = Transforms(target);
                var sourceHips = sourceTransforms["J_Bip_C_Hips"];
                // The armature root is the rigid frame the source cycle is authored in.
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
                var restPosition = targetBasis.InverseTransformPoint(targetTransforms["lengyue_hips"].position);
                var referenceHips = Quaternion.Inverse(sourceBasis.rotation) *
                    (sourceHips.position - sourceBasis.position);
                var legScale = LegLength(targetTransforms, "lengyue_thigh_l", "lengyue_shin_l", "lengyue_foot_l") /
                               LegLength(sourceTransforms, "J_Bip_L_UpperLeg", "J_Bip_L_LowerLeg", "J_Bip_L_Foot");
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
                        restKnee = targetBasis.InverseTransformPoint(targetTransforms["lengyue_shin_" + side].position),
                        restFoot = targetBasis.InverseTransformPoint(targetTransforms["lengyue_foot_" + side].position)
                    };
                }).ToArray();
                // A prefab can be saved in an asymmetric stance. Keeping each
                // foot's own rest Z then shifts one foot half a stride behind the
                // other, which reads as a limp (the left foot never steps out).
                // Both legs share one fore/aft datum so the cycle stays symmetric.
                var footDatum = (legs[0].restFoot.z + legs[1].restFoot.z) * .5f;
                var kneeDatum = (legs[0].restKnee.z + legs[1].restKnee.z) * .5f;
                foreach (var leg in legs)
                {
                    leg.restFoot = new Vector3(leg.restFoot.x, leg.restFoot.y, footDatum);
                    leg.restKnee = new Vector3(leg.restKnee.x, leg.restKnee.y, kneeDatum);
                }
                // The VRoid source rests in a T-pose but authors the walk with
                // hanging arms, so rest-based references pin the production
                // rig's arms at its own T-pose rest. Re-base the entire arm
                // chain - arm, forearm, hand and every finger joint - on the
                // cycle's own first frame: each joint is aligned from its
                // production rest direction + palm-plane roll to the authored
                // first-frame direction + roll. All measurements run in world
                // space and the alignment is conjugated into the target basis,
                // so differing armature import rotations (Blender's -90 degrees
                // X, for instance) cannot skew the result. Joints are aligned
                // as world rotations and the local pose is re-derived below,
                // so nothing double rotates.
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
                        bones.Single(bone => bone.target.name == "lengyue_upper_arm_" + side),
                        sourceTransforms["J_Bip_" + sourceSide + "_UpperArm"],
                        sourceTransforms["J_Bip_" + sourceSide + "_LowerArm"],
                        targetTransforms["lengyue_upper_arm_" + side],
                        targetTransforms["lengyue_forearm_" + side],
                        sourceAcross, targetAcross, sourceBasis, targetBasis, false);
                    RebaseArmChainBone(
                        bones.Single(bone => bone.target.name == "lengyue_forearm_" + side),
                        sourceTransforms["J_Bip_" + sourceSide + "_LowerArm"],
                        sourceTransforms["J_Bip_" + sourceSide + "_Hand"],
                        targetTransforms["lengyue_forearm_" + side],
                        targetTransforms["lengyue_hand_" + side],
                        sourceAcross, targetAcross, sourceBasis, targetBasis, false);
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
                var frames = Mathf.RoundToInt(sourceClip.length * sourceClip.frameRate);
                // Neither the imported rest pose nor the first frame is a
                // neutral datum: the rest can be an authored stance, and the
                // first frame sits at the fully split stride instant. Referencing
                // either one leaves one foot half a stride behind the other,
                // which reads as a limp. Average the whole cycle so both legs
                // share one datum and each foot swings symmetrically.
                var footSums = new Vector3[legs.Length];
                var kneeSums = new Vector3[legs.Length];
                var hipsSum = Vector3.zero;
                for (var frame = 0; frame <= frames; frame++)
                {
                    sourceClip.SampleAnimation(source, frame / sourceClip.frameRate);
                    for (var index = 0; index < legs.Length; index++)
                    {
                        footSums[index] += RelativeMetres(legs[index].sourceFoot, sourceBasis);
                        kneeSums[index] += RelativeMetres(legs[index].sourceKnee, sourceBasis);
                    }
                    hipsSum += Quaternion.Inverse(sourceBasis.rotation) *
                               (sourceHips.position - sourceBasis.position);
                }
                for (var index = 0; index < legs.Length; index++)
                {
                    legs[index].referenceFoot = footSums[index] / (frames + 1);
                    legs[index].referenceKnee = kneeSums[index] / (frames + 1);
                }
                referenceHips = hipsSum / (frames + 1);
                var hipsPositions = new List<Vector3>();
                var footZMin = new[] { float.MaxValue, float.MaxValue };
                var footZMax = new[] { float.MinValue, float.MinValue };
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
                        (sourceHips.position - sourceBasis.position) - referenceHips;
                    if (delta.magnitude > .3f)
                        throw new InvalidOperationException("Walk hips displacement exceeds the rig's metre scale.");
                    hipsPositions.Add(restPosition + delta * legScale);
                    for (var index = 0; index < legs.Length; index++)
                    {
                        var leg = legs[index];
                        var relativeFoot = RelativeMetres(leg.sourceFoot, sourceBasis);
                        footZMin[index] = Mathf.Min(footZMin[index], relativeFoot.z);
                        footZMax[index] = Mathf.Max(footZMax[index], relativeFoot.z);
                        leg.goals.Add(leg.restFoot + legScale * (relativeFoot - leg.referenceFoot));
                        leg.poles.Add(leg.restKnee + legScale *
                            (RelativeMetres(leg.sourceKnee, sourceBasis) - leg.referenceKnee));
                    }
                }

                var generated = new AnimationClip
                {
                    name = "lengyue_clip_walk_bodyonly",
                    frameRate = sourceClip.frameRate,
                    wrapMode = WrapMode.Loop
                };
                var localRotations = bones.ToDictionary(bone => bone, _ => new List<Quaternion>());
                var report = new Report
                {
                    lengthSeconds = sourceClip.length, frameRate = sourceClip.frameRate,
                    legScale = legScale, mappedBones = bones.Length,
                    sourceFootZMin = footZMin, sourceFootZMax = footZMax,
                    paths = bones.Select(bone => bone.path).ToArray()
                };
                // Remove the small endpoint residual across the cycle; keep the authored
                // motion and make the final pose identical to the first pose.
                var hipResidual = hipsPositions[frames] - hipsPositions[0];
                var bakedHips = new List<Vector3>();
                var pelvisCorrections = new List<float>();
                var minimumGoalHeight = float.MaxValue;
                var maximumHorizontalReach = 0f;
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
                    targetTransforms["lengyue_hips"].position = targetBasis.TransformPoint(
                        hipsPositions[frame] - hipResidual * phase);
                    var hips = targetTransforms["lengyue_hips"];
                    var authoredHeight = hips.position.y;
                    var reachableHeight = authoredHeight;
                    foreach (var leg in legs)
                    {
                        var goalResidual = leg.goals[frames] - leg.goals[0];
                        var goal = targetBasis.TransformPoint(leg.goals[frame] - goalResidual * phase);
                        var reach = Vector3.Distance(leg.thigh.position, leg.knee.position) +
                                    Vector3.Distance(leg.knee.position, leg.foot.position) - .001f;
                        var horizontal = Vector3.ProjectOnPlane(goal - leg.thigh.position, Vector3.up);
                        minimumGoalHeight = Mathf.Min(minimumGoalHeight, goal.y);
                        maximumHorizontalReach = Mathf.Max(maximumHorizontalReach, horizontal.magnitude);
                        var thighOffset = leg.thigh.position.y - hips.position.y;
                        reachableHeight = Mathf.Min(reachableHeight, goal.y +
                            Mathf.Sqrt(Mathf.Max(0, reach * reach - horizontal.sqrMagnitude)) - thighOffset);
                    }
                    var pelvisCorrection = authoredHeight - reachableHeight;
                    pelvisCorrections.Add(pelvisCorrection);
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
                report.pelvisCorrections = pelvisCorrections.ToArray();
                report.minimumGoalHeight = minimumGoalHeight;
                report.maximumHorizontalReach = maximumHorizontalReach;
                report.hipsRestHeight = restPosition.y;
                report.footRestHeight = legs[0].restFoot.y;
                foreach (var bone in bones)
                    SetRotation(generated, bone.path, localRotations[bone], generated.frameRate);
                SetPosition(generated,
                    bones.Single(bone => bone.target.name == "lengyue_hips").path,
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
                Debug.Log($"MATESOUL_WALK02_IMPORTED bones={report.mappedBones} " +
                          $"seconds={report.lengthSeconds:F4} referenceSpeed={report.naturalSpeed:F5} " +
                          $"stride={report.strideMeters:F5}");
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

        // Use distance as the animation clock during authoring. Runtime then only
        // needs actual metres per second / calibrated metres per second.
        private static void BakeDistanceTiming(AnimationClip clip, GameObject target,
            string[] paths, Report report)
        {
            const float sampleRate = 120;
            var count = Mathf.RoundToInt(clip.length * sampleRate) + 1;
            var feet = new[] { target.transform.Find(paths.Single(path => path.EndsWith("/lengyue_foot_l"))),
                target.transform.Find(paths.Single(path => path.EndsWith("/lengyue_foot_r"))) };
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
            var hipsIndex = Array.FindIndex(transforms, bone => bone.name == "lengyue_hips");
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

        private static void Measure(AnimationClip clip, GameObject target, Report report)
        {
            var transforms = Transforms(target);
            var count = Mathf.RoundToInt(clip.length * clip.frameRate) + 1;
            var feet = new[] { "lengyue_foot_l", "lengyue_foot_r", "lengyue_toe_l", "lengyue_toe_r",
                "lengyue_hand_l", "lengyue_hand_r" };
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
            report.leftHands = points[4];
            report.rightHands = points[5];
            report.maximumLoopPositionError = points.Max(p => Vector3.Distance(p[0], p[count - 1]));
            report.maximumLoopRotationError = transforms.Values.Select((bone, index) =>
                Quaternion.Angle(startRotations[index], bone.localRotation)).Max();
            report.bindingCount = AnimationUtility.GetCurveBindings(clip).Length;
            report.leftFootExcursion = points[0].Max(p => p.z) - points[0].Min(p => p.z);
            report.rightFootExcursion = points[1].Max(p => p.z) - points[1].Min(p => p.z);
            var speeds = new List<float>();
            foreach (var track in points.Take(4))
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
