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
    public static class Idle02PlaybackValidation
    {
        private const string CharacterPrefabPath =
            "Assets/Characters/LengyueCharacter/Lengyue/Runtime/Prefabs/" +
            "lengyue_character.prefab";

        [Serializable]
        private sealed class ValidationReport
        {
            public string clip;
            public float clipLengthSeconds;
            public string idle03Clip;
            public float idle03ClipLengthSeconds;
            public int facialCurveCount;
            public string[] animatedFacialShapes;
            public float maximumFacialWeightDelta;
            public float maximumPoseRotationDegrees;
            public int idle03FacialCurveCount;
            public float idle03MaximumPoseRotationDegrees;
            public float idle03MaximumFacialResidual;
            public bool idle01Played;
            public bool randomSwitchedToIdle02;
            public bool randomSwitchedToIdle03;
            public bool idle02Played;
            public bool idle03Played;
            public bool movementReturnedToLocomotion;
            public bool movementBlockedRandomSwitch;
            public bool socialAttentionBlockedRandomSwitch;
            public bool passed;
        }

        private sealed class FacialTarget
        {
            public string path;
            public string name;
            public SkinnedMeshRenderer renderer;
            public int index;
        }

        [MenuItem("MateSoul/Lengyue/Validate Random Idle Playback")]
        public static void Run()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                CharacterPrefabPath);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                AvatarLocomotionBuilder.ControllerPath);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                AvatarLocomotionBuilder.Idle02Path);
            var idle03 = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                AvatarLocomotionBuilder.Idle03Path);
            if (!prefab || !controller || !clip || !idle03)
                throw new InvalidOperationException(
                    "Random idle playback validation content is incomplete.");

            var instance = Object.Instantiate(prefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var animator = instance.GetComponentInChildren<Animator>(true);
                if (!animator || animator.transform != instance.transform)
                    throw new InvalidOperationException(
                        "Canonical Lengyue Animator must be on the prefab root.");
                animator.runtimeAnimatorController = controller;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.enabled = true;
                instance.SetActive(true);
                animator.Rebind();
                animator.Update(0);

                var report = new ValidationReport
                {
                    clip = AvatarLocomotionBuilder.Idle02Path,
                    clipLengthSeconds = clip.length,
                    idle03Clip = AvatarLocomotionBuilder.Idle03Path,
                    idle03ClipLengthSeconds = idle03.length
                };
                ValidateDirectPlayback(animator, instance, clip, report);
                ValidateIdle03Playback(animator, instance, clip, idle03, report);
                ValidateRandomSelection(animator, instance, report);
                report.passed = report.facialCurveCount >= 1 &&
                                report.maximumFacialWeightDelta > .1f &&
                                report.maximumPoseRotationDegrees > .1f &&
                                report.idle03FacialCurveCount == 0 &&
                                report.idle03MaximumPoseRotationDegrees > .1f &&
                                report.idle03MaximumFacialResidual < .1f &&
                                report.idle01Played &&
                                report.randomSwitchedToIdle02 &&
                                report.randomSwitchedToIdle03 &&
                                report.idle02Played &&
                                report.idle03Played &&
                                report.movementReturnedToLocomotion &&
                                report.movementBlockedRandomSwitch &&
                                report.socialAttentionBlockedRandomSwitch;
                WriteReport(report);
                if (!report.passed)
                    throw new InvalidOperationException(
                        "Random idle runtime playback validation failed.");
                Debug.Log(
                    $"LENGYUE_RANDOM_IDLE_PLAYBACK_VALIDATED faceCurves=" +
                    $"{report.facialCurveCount} faceDelta=" +
                    $"{report.maximumFacialWeightDelta:F3} idle02PoseDelta=" +
                    $"{report.maximumPoseRotationDegrees:F3} idle03PoseDelta=" +
                    $"{report.idle03MaximumPoseRotationDegrees:F3} " +
                    $"idle03FaceResidual={report.idle03MaximumFacialResidual:F3}");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void ValidateDirectPlayback(
            Animator animator, GameObject instance, AnimationClip clip,
            ValidationReport report)
        {
            var shapes = ResolveFacialTargets(clip, instance);
            report.facialCurveCount = shapes.Length;
            report.animatedFacialShapes = shapes
                .Select(shape => shape.path + "/" + shape.name)
                .ToArray();

            var bones = new[] { "lengyue_hips", "lengyue_spine", "lengyue_chest", "lengyue_head_01",
                    "lengyue_upper_arm_l", "lengyue_upper_arm_r", "lengyue_thigh_l", "lengyue_thigh_r" }
                .Select(name => instance.GetComponentsInChildren<Transform>(true)
                    .Single(transform => transform.name == name))
                .ToArray();
            var minimum = Enumerable.Repeat(
                float.PositiveInfinity, shapes.Length).ToArray();
            var maximum = Enumerable.Repeat(
                float.NegativeInfinity, shapes.Length).ToArray();
            var start = new Quaternion[bones.Length];

            animator.SetFloat("Speed", 0);
            animator.SetBool("SocialAttention", false);
            animator.SetInteger("IdleVariant", 1);
            animator.Play("Idle02", 0, 0);
            animator.Update(0);
            for (int index = 0; index < bones.Length; index++)
                start[index] = bones[index].localRotation;

            int frames = Mathf.CeilToInt(clip.length * clip.frameRate);
            for (int frame = 0; frame <= frames; frame++)
            {
                animator.Update(frame == 0 ? 0 : 1f / clip.frameRate);
                for (int index = 0; index < shapes.Length; index++)
                {
                    float weight = shapes[index].renderer.GetBlendShapeWeight(
                        shapes[index].index);
                    minimum[index] = Mathf.Min(minimum[index], weight);
                    maximum[index] = Mathf.Max(maximum[index], weight);
                }
                report.maximumPoseRotationDegrees = Mathf.Max(
                    report.maximumPoseRotationDegrees,
                    bones.Select((bone, index) =>
                        Quaternion.Angle(start[index], bone.localRotation)).Max());
            }
            report.maximumFacialWeightDelta = Enumerable.Range(0, shapes.Length)
                .Max(index => maximum[index] - minimum[index]);
            report.idle02Played =
                animator.GetCurrentAnimatorStateInfo(0).shortNameHash ==
                Animator.StringToHash("Idle02");
        }

        private static FacialTarget[] ResolveFacialTargets(
            AnimationClip clip, GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .ToDictionary(renderer => AnimationUtility.CalculateTransformPath(
                    renderer.transform, instance.transform));
            return AnimationUtility.GetCurveBindings(clip)
                .Where(binding => binding.type == typeof(SkinnedMeshRenderer) &&
                                  binding.propertyName.StartsWith(
                                      "blendShape.", StringComparison.Ordinal))
                .Select(binding =>
                {
                    if (!renderers.TryGetValue(binding.path, out var renderer))
                        throw new InvalidOperationException(
                            "Runtime facial renderer is missing: " + binding.path);
                    string name = binding.propertyName.Substring(
                        "blendShape.".Length);
                    int index = renderer.sharedMesh.GetBlendShapeIndex(name);
                    if (index < 0)
                        throw new InvalidOperationException(
                            "Runtime facial shape is missing: " + name);
                    return new FacialTarget
                    {
                        path = binding.path,
                        name = name,
                        renderer = renderer,
                        index = index
                    };
                })
                .ToArray();
        }

        private static void ValidateIdle03Playback(
            Animator animator, GameObject instance, AnimationClip idle02,
            AnimationClip idle03, ValidationReport report)
        {
            var facialTargets = ResolveFacialTargets(idle02, instance);
            foreach (var target in facialTargets)
                target.renderer.SetBlendShapeWeight(target.index, 73);

            animator.SetInteger("IdleVariant", 2);
            for (int step = 0; step < 6; step++) animator.Update(.1f);
            report.idle03Played =
                animator.GetCurrentAnimatorStateInfo(0).shortNameHash ==
                Animator.StringToHash("Idle03");
            report.idle03MaximumFacialResidual = facialTargets.Length == 0
                ? 0
                : facialTargets.Max(target => Mathf.Abs(
                    target.renderer.GetBlendShapeWeight(target.index)));
            report.idle03FacialCurveCount =
                AnimationUtility.GetCurveBindings(idle03).Count(binding =>
                    binding.type == typeof(SkinnedMeshRenderer) &&
                    binding.propertyName.StartsWith(
                        "blendShape.", StringComparison.Ordinal));

            var bones = new[] { "lengyue_hips", "lengyue_spine", "lengyue_chest", "lengyue_head_01",
                    "lengyue_upper_arm_l", "lengyue_upper_arm_r", "lengyue_thigh_l", "lengyue_thigh_r" }
                .Select(name => instance.GetComponentsInChildren<Transform>(true)
                    .Single(transform => transform.name == name))
                .ToArray();
            animator.Play("Idle03", 0, 0);
            animator.Update(0);
            var start = bones.Select(bone => bone.localRotation).ToArray();
            int frames = Mathf.CeilToInt(idle03.length * idle03.frameRate);
            for (int frame = 0; frame <= frames; frame++)
            {
                animator.Update(frame == 0 ? 0 : 1f / idle03.frameRate);
                report.idle03MaximumPoseRotationDegrees = Mathf.Max(
                    report.idle03MaximumPoseRotationDegrees,
                    bones.Select((bone, index) =>
                        Quaternion.Angle(start[index], bone.localRotation)).Max());
            }
        }

        private static void ValidateRandomSelection(
            Animator animator, GameObject instance, ValidationReport report)
        {
            animator.SetFloat("Speed", 0);
            animator.SetBool("SocialAttention", false);
            animator.SetInteger("IdleVariant", 0);
            animator.Play("Locomotion", 0, 0);
            animator.Update(0);
            report.idle01Played =
                animator.GetCurrentAnimatorStateInfo(0).shortNameHash ==
                Animator.StringToHash("Locomotion");

            var variation = instance.GetComponent<AvatarIdleVariation>();
            if (!variation) variation = instance.AddComponent<AvatarIdleVariation>();
            variation.Bind(animator, 1207);
            for (int cycle = 0; cycle < 24 &&
                                (!report.randomSwitchedToIdle02 ||
                                 !report.randomSwitchedToIdle03); cycle++)
            {
                variation.Tick(0);
                variation.Tick(1000);
                animator.Update(.25f);
                int variant = animator.GetInteger("IdleVariant");
                report.randomSwitchedToIdle02 |= variant == 1;
                report.randomSwitchedToIdle03 |= variant == 2;
            }

            int beforeMovement = animator.GetInteger("IdleVariant");
            animator.SetFloat("Speed", 1);
            for (int step = 0; step < 5; step++) animator.Update(.1f);
            variation.Tick(0);
            variation.Tick(1000);
            report.movementBlockedRandomSwitch =
                animator.GetInteger("IdleVariant") == beforeMovement;
            report.movementReturnedToLocomotion =
                animator.GetCurrentAnimatorStateInfo(0).shortNameHash ==
                Animator.StringToHash("Locomotion");

            animator.SetFloat("Speed", 0);
            animator.SetBool("SocialAttention", true);
            animator.Update(.25f);
            int beforeAttention = animator.GetInteger("IdleVariant");
            variation.Tick(0);
            variation.Tick(1000);
            report.socialAttentionBlockedRandomSwitch =
                animator.GetInteger("IdleVariant") == beforeAttention;
        }

        private static void WriteReport(ValidationReport report)
        {
            string directory = Path.GetFullPath(
                Application.dataPath +
                "/../../ArtSource/Lengyue/Animation/Idle02/reports");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                directory + "/playback_validation.json",
                JsonUtility.ToJson(report, true));
        }
    }
}
