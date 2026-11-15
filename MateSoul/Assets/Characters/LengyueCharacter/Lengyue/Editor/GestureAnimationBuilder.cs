using System;
using System.Linq;
using MateSoul.Runtime;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MateSoul.Editor
{
    // Imports the AI-driven full-body gestures with the same retarget pipeline as
    // the item gestures and emotions (world-space torso deltas, IK limbs, finger
    // rebase). Dance / yawn / no-energy live on the Gesture animator layer.
    public static class GestureAnimationBuilder
    {
        private const string Root =
            "Assets/Characters/LengyueCharacter/Lengyue/Runtime/Animations/Gesture";

        private sealed class Config
        {
            public string Folder;
            public string Source;
            public string Clip;
            public string State;
        }

        private static readonly Config[] Configs =
        {
            new Config
            {
                Folder = "NoEnergy", Source = "NoEnergy_01_InPlace.fbx",
                Clip = "lengyue_clip_noenergy01",
                State = GesturePerformance.NoEnergyStateName
            },
            new Config
            {
                Folder = "Dance", Source = "Dance_01_InPlace.fbx",
                Clip = "lengyue_clip_dance01",
                State = GesturePerformance.DanceStateName
            },
            new Config
            {
                Folder = "Yawn", Source = "Yawn_01_InPlace.fbx",
                Clip = "lengyue_clip_yawn01",
                State = GesturePerformance.YawnStateName
            }
        };

        [MenuItem("MateSoul/Lengyue/Import NoEnergy Gesture")]
        public static void ImportNoEnergy() => Import(Configs[0]);

        [MenuItem("MateSoul/Lengyue/Import Dance Gesture")]
        public static void ImportDance() => Import(Configs[1]);

        [MenuItem("MateSoul/Lengyue/Import Yawn Gesture")]
        public static void ImportYawn() => Import(Configs[2]);

        private static void Import(Config config)
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(
                GameSceneBuilder.CharacterPath);
            if (!character)
                throw new InvalidOperationException(
                    "Production character prefab is missing.");
            var source = Root + "/" + config.Folder + "/" + config.Source;
            var output = Root + "/" + config.Folder + "/" + config.Clip + ".anim";
            var clip = AvatarItemAnimationRetarget.Retarget(
                source, output, AvatarItemAnimationRetarget.DisplayRig);
            // Gestures often carry no authored face; requireFace=false keeps the
            // base expression instead of asserting a neutral face on the layer.
            LengyueIdle01Builder.MapAuthoredEmotionFace(
                character, clip, source, false, false);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                AvatarLocomotionBuilder.ControllerPath);
            if (!controller)
                throw new InvalidOperationException(
                    "Production avatar controller is missing.");
            AvatarLocomotionBuilder.ConfigureGestureState(
                controller, config.State, clip);
            Validate(clip);
            EditorUtility.SetDirty(clip);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"MATESOUL_GESTURE_IMPORTED state={config.State} " +
                $"seconds={clip.length:F3} fps={clip.frameRate:F1} " +
                $"body={BindingCount(clip, typeof(Transform))} " +
                $"face={BindingCount(clip, typeof(SkinnedMeshRenderer))}");
        }

        private static void Validate(AnimationClip clip)
        {
            if (clip.length < .5f || clip.length > 30f)
                throw new InvalidOperationException(
                    "Gesture length is unexpected: " + clip.length.ToString("F3"));
            if (AnimationUtility.GetAnimationClipSettings(clip).loopTime)
                throw new InvalidOperationException(
                    "Gesture must be a one-shot clip.");
            if (BindingCount(clip, typeof(Transform)) < 40)
                throw new InvalidOperationException(
                    "Gesture body bindings are incomplete.");
        }

        private static int BindingCount(AnimationClip clip, Type type)
            => AnimationUtility.GetCurveBindings(clip)
                .Count(binding => binding.type == type);
    }
}
