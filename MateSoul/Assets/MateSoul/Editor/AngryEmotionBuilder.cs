using System;
using System.Linq;
using MateSoul.Runtime;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public static class AngryEmotionBuilder
    {
        public const string DirectoryPath =
            "Assets/Characters/LengyueCharacter/Lengyue/Runtime/Animations/Emotion/Angry";
        public const string SourcePath = DirectoryPath + "/Mad_01_InPlace.fbx";
        public const string ClipPath = DirectoryPath + "/lengyue_clip_angry01.anim";

        [MenuItem("MateSoul/Lengyue/Import Angry Emotion")]
        public static void Import()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(
                GameSceneBuilder.CharacterPath);
            if (!character)
                throw new InvalidOperationException(
                    "Production character prefab is missing.");
            // The mad source uses the re-rigged display-name skeleton; retarget
            // the body through the same world-space pipeline as the item gestures
            // (drink, hug toy, radio).
            var clip = AvatarItemAnimationRetarget.Retarget(
                SourcePath, ClipPath, AvatarItemAnimationRetarget.DisplayRig);
            // Character authoring tools belong to Unity's predefined editor
            // assembly, which cannot be referenced by this asmdef.
            var importer = Type.GetType(
                "MateSoul.Editor.LengyueIdle01Builder, Assembly-CSharp-Editor", true);
            // The re-rigged source uses face_* shapes; map them onto the production
            // Fcl_*/Game_* face and add the angry brow/eye/mouth presets.
            importer.GetMethod("MapAuthoredEmotionFace")
                .Invoke(null, new object[] { character, clip, SourcePath, true });
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                AvatarLocomotionBuilder.ControllerPath);
            if (!controller)
                throw new InvalidOperationException(
                    "Production avatar controller is missing.");
            AvatarLocomotionBuilder.ConfigureProductionController(controller);
            Validate(character, controller, clip);
            EditorUtility.SetDirty(clip);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"MATESOUL_ANGRY_EMOTION_IMPORTED seconds={clip.length:F3} " +
                $"fps={clip.frameRate:F1} body={TransformBindingCount(clip)} " +
                $"face={FacialBindingCount(clip)}");
        }

        private static void Validate(GameObject character,
            AnimatorController controller, AnimationClip clip)
        {
            if (clip.length < 1.5f || clip.length > 1.8f)
                throw new InvalidOperationException(
                    $"Angry animation length is unexpected: {clip.length:F3}s.");
            if (AnimationUtility.GetAnimationClipSettings(clip).loopTime)
                throw new InvalidOperationException(
                    "Angry animation must be a one-shot clip.");
            if (TransformBindingCount(clip) < 100)
                throw new InvalidOperationException(
                    "Angry animation has too few body bindings.");
            if (FacialBindingCount(clip) == 0)
                throw new InvalidOperationException(
                    "Angry animation has no production facial bindings.");

            var layer = controller.layers.FirstOrDefault(
                value => value.name == ResponseEmotionAudio.EmotionLayerName);
            if (layer == null)
                throw new InvalidOperationException(
                    "Animator Emotion layer is missing.");
            var state = layer.stateMachine.states
                .Select(value => value.state)
                .SingleOrDefault(value =>
                    value.name == ResponseEmotionAudio.AngryStateName);
            if (!state || state.motion != clip || Mathf.Abs(state.speed) > .0001f)
                throw new InvalidOperationException(
                    "Animator Angry state is not configured for synchronized sampling.");

            var angryAudio = Resources.LoadAll<AudioClip>(
                    "ResponseEmotionAudio/Angry")
                .OrderByDescending(value => value.length)
                .FirstOrDefault();
            if (!angryAudio || angryAudio.length + .01f < clip.length)
                throw new InvalidOperationException(
                    "Long angry audio must cover the complete animation.");

            var instance = Object.Instantiate(character);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var hips = instance.GetComponentsInChildren<Transform>(true)
                    .Single(value => value.name == "lengyue_hips");
                clip.SampleAnimation(instance, 0);
                var start = hips.position;
                clip.SampleAnimation(instance, clip.length);
                var drift = Vector3.ProjectOnPlane(
                    hips.position - start, Vector3.up).magnitude;
                if (drift > .02f)
                    throw new InvalidOperationException(
                        $"Angry animation is not in-place: {drift:F4}m.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static int TransformBindingCount(AnimationClip clip)
            => AnimationUtility.GetCurveBindings(clip).Count(binding =>
                binding.type == typeof(Transform));

        private static int FacialBindingCount(AnimationClip clip)
            => AnimationUtility.GetCurveBindings(clip).Count(binding =>
                binding.type == typeof(SkinnedMeshRenderer) &&
                binding.propertyName.StartsWith(
                    "blendShape.", StringComparison.Ordinal));
    }
}
