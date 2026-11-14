using System;
using System.Collections.Generic;
using System.Linq;
using MateSoul.Runtime;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public static class SadEmotionBuilder
    {
        public const string DirectoryPath =
            "Assets/Characters/LengyueCharacter/Lengyue/Runtime/Animations/Emotion/Sad";
        public const string SourcePath = DirectoryPath + "/Cry_01_InPlace.fbx";
        public const string ClipPath = DirectoryPath + "/lengyue_clip_sad01.anim";

        [MenuItem("MateSoul/Lengyue/Import Sad Emotion")]
        public static void Import()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(
                GameSceneBuilder.CharacterPath);
            if (!character)
                throw new InvalidOperationException("Production character prefab is missing.");
            // Reuse the canonical transform remapping from the predefined editor assembly.
            var importer = Type.GetType(
                "MateSoul.Editor.LengyueIdle01Builder, Assembly-CSharp-Editor", true);
            var excludedSourceBones = new[]
            {
                "lengyue_pelvis_root", "lengyue_head_tip",
                "lengyue_breast_l", "lengyue_breast_r", "lengyue_breast_tip_l", "lengyue_breast_tip_r",
                "lengyue_mech_cover_l", "lengyue_mech_cover_r", "lengyue_mech_in_l", "lengyue_mech_in_r",
                "lengyue_mech_out_l", "lengyue_mech_out_r", "lengyue_upper_arm_twist_l", "lengyue_upper_arm_twist_r"
            };
            var clip = (AnimationClip)importer.GetMethod("ImportAuthoredActionTracks")
                .Invoke(null, new object[]
                {
                    SourcePath, ClipPath, ResponseEmotionAudio.SadClipName,
                    character, excludedSourceBones, false
                });
            MapFacialPerformance(character, clip);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                AvatarLocomotionBuilder.ControllerPath);
            if (!controller)
                throw new InvalidOperationException("Production avatar controller is missing.");
            AvatarLocomotionBuilder.ConfigureEmotionState(
                controller, ResponseEmotionAudio.SadStateName, clip);
            Validate(character, controller, clip);
            EditorUtility.SetDirty(clip);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"MATESOUL_SAD_EMOTION_IMPORTED seconds={clip.length:F6} " +
                $"fps={clip.frameRate:F1} body={BindingCount(clip, typeof(Transform))} " +
                $"face={BindingCount(clip, typeof(SkinnedMeshRenderer))}");
        }

        private static void MapFacialPerformance(GameObject character, AnimationClip clip)
        {
            var source = AssetDatabase.LoadAllAssetsAtPath(SourcePath)
                .OfType<AnimationClip>()
                .Where(value => !value.name.StartsWith("__preview__", StringComparison.Ordinal))
                .OrderByDescending(value => BindingCount(value, typeof(SkinnedMeshRenderer)))
                .First();
            var face = character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Single(value => value.name == "lengyue_face_root");
            string path = AnimationUtility.CalculateTransformPath(
                face.transform, character.transform);
            for (int index = 0; index < face.sharedMesh.blendShapeCount; index++)
                SetCurve(face.sharedMesh.GetBlendShapeName(index),
                    AnimationCurve.Constant(0, clip.length, 0));

            var requiredTracks = new HashSet<string>
            {
                "face_eye_close_L_BS", "face_eye_close_R_BS", "face_jaw_open_BS",
                "face_sad_BS", "face_extra_01_BS", "BottomTeeth_BS"
            };
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                if (binding.type != typeof(SkinnedMeshRenderer) ||
                    !binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                    continue;
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (!curve.keys.Any(key => Mathf.Abs(key.value) > .001f)) continue;
                string shape = binding.propertyName.Substring("blendShape.".Length);
                switch (shape)
                {
                    case "face_eye_close_L_BS": Copy("Fcl_EYE_Close_L", 1); break;
                    case "face_eye_close_R_BS": Copy("Fcl_EYE_Close_R", 1); break;
                    case "face_jaw_open_BS": Copy("Fcl_MTH_A", 1); break;
                    case "face_sad_BS":
                        Copy("Fcl_MTH_Sorrow", .55f);
                        Copy("Game_BrowSad_L", .65f);
                        Copy("Game_BrowSad_R", .65f);
                        Copy("Fcl_EYE_Sorrow", .3f);
                        break;
                    case "face_extra_01_BS": Copy("Fcl_MTH_Down", .25f); break;
                    // The production face includes its teeth in the mouth morphs.
                    // Adapt the separate lower-teeth motion as a subtle rounded cry mouth.
                    case "BottomTeeth_BS": Copy("Fcl_MTH_O", .15f); break;
                    default:
                        throw new InvalidOperationException(
                            "Unmapped active sad facial curve: " + binding.path + "/" + shape);
                }
                requiredTracks.Remove(shape);

                void Copy(string target, float strength)
                {
                    int frames = Mathf.RoundToInt(clip.length * clip.frameRate) + 1;
                    var mapped = new AnimationCurve();
                    for (int frame = 0; frame < frames; frame++)
                    {
                        float time = Mathf.Min(clip.length, frame / clip.frameRate);
                        mapped.AddKey(time, Mathf.Clamp(curve.Evaluate(time) * strength, 0, 100));
                    }
                    for (int key = 0; key < mapped.length; key++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(
                            mapped, key, AnimationUtility.TangentMode.Linear);
                        AnimationUtility.SetKeyRightTangentMode(
                            mapped, key, AnimationUtility.TangentMode.Linear);
                    }
                    SetCurve(target, mapped);
                }
            }
            if (requiredTracks.Count > 0)
                throw new InvalidOperationException(
                    "Missing authored sad facial tracks: " +
                    string.Join(", ", requiredTracks.OrderBy(value => value)));

            void SetCurve(string shape, AnimationCurve curve)
            {
                if (face.sharedMesh.GetBlendShapeIndex(shape) < 0)
                    throw new InvalidOperationException("Missing production facial morph: " + shape);
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer),
                        "blendShape." + shape), curve);
            }
        }

        private static void Validate(GameObject character,
            AnimatorController controller, AnimationClip clip)
        {
            if (clip.length < 3.2f || clip.length > 3.4f ||
                AnimationUtility.GetAnimationClipSettings(clip).loopTime)
                throw new InvalidOperationException("Sad animation must be the complete one-shot.");
            if (BindingCount(clip, typeof(Transform)) < 100 ||
                BindingCount(clip, typeof(SkinnedMeshRenderer)) != 61)
                throw new InvalidOperationException("Sad animation body or facial bindings are incomplete.");
            var layer = controller.layers.Single(value =>
                value.name == ResponseEmotionAudio.EmotionLayerName);
            var state = layer.stateMachine.states.Select(value => value.state)
                .Single(value => value.name == ResponseEmotionAudio.SadStateName);
            if (state.motion != clip || Mathf.Abs(state.speed) > .0001f)
                throw new InvalidOperationException("Sad state must use audio-clock sampling.");
            var audio = Resources.LoadAll<AudioClip>("ResponseEmotionAudio/Sad")
                .OrderByDescending(value => value.length).FirstOrDefault();
            if (!audio || audio.length + .01f < clip.length)
                throw new InvalidOperationException("Sad audio must cover the complete animation.");

            var instance = Object.Instantiate(character);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var hips = instance.GetComponentsInChildren<Transform>(true)
                    .Single(value => value.name == "lengyue_hips");
                clip.SampleAnimation(instance, 0);
                var origin = hips.position;
                int frames = Mathf.RoundToInt(clip.length * clip.frameRate) + 1;
                for (int frame = 0; frame < frames; frame++)
                {
                    clip.SampleAnimation(instance, Mathf.Min(clip.length, frame / clip.frameRate));
                    if (Vector3.ProjectOnPlane(hips.position - origin, Vector3.up).magnitude > .02f)
                        throw new InvalidOperationException("Sad animation must remain in place.");
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }

        private static int BindingCount(AnimationClip clip, Type type)
            => AnimationUtility.GetCurveBindings(clip).Count(binding => binding.type == type);
    }
}
