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
    public static class HappyEmotionBuilder
    {
        public const string DirectoryPath =
            "Assets/Characters/LengyueCharacter/Lengyue/Runtime/Animations/Emotion/Happy";
        public const string SourcePath = DirectoryPath + "/Happy_01_InPlace.fbx";
        public const string ClipPath = DirectoryPath + "/lengyue_clip_happy01.anim";

        // Joy shapes that must actually carry weight in the generated clip. If they
        // stay at zero the face silently falls back to neutral at runtime.
        private static readonly string[] RequiredJoyShapes =
        {
            "Fcl_EYE_Joy_L", "Fcl_EYE_Joy_R", "Fcl_MTH_Joy"
        };

        [MenuItem("MateSoul/Lengyue/Import Happy Emotion")]
        public static void Import()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(
                GameSceneBuilder.CharacterPath);
            if (!character)
                throw new InvalidOperationException("Production character prefab is missing.");
            // The laugh source uses the re-rigged display-name skeleton; retarget
            // the body through the same world-space pipeline as the item gestures
            // (drink, hug toy, radio), then map its face_* shapes onto the
            // production Fcl_*/Game_* face.
            var clip = AvatarItemAnimationRetarget.Retarget(
                SourcePath, ClipPath, AvatarItemAnimationRetarget.DisplayRig);
            MapFacialPerformance(character, clip);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                AvatarLocomotionBuilder.ControllerPath);
            if (!controller)
                throw new InvalidOperationException("Production avatar controller is missing.");
            AvatarLocomotionBuilder.ConfigureEmotionState(
                controller, ResponseEmotionAudio.HappyStateName, clip);
            Validate(character, controller, clip);
            EditorUtility.SetDirty(clip);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"MATESOUL_HAPPY_EMOTION_IMPORTED seconds={clip.length:F6} " +
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
            int frames = Mathf.RoundToInt(clip.length * clip.frameRate) + 1;
            var targets = new Dictionary<string, float[]>(StringComparer.Ordinal);

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
                    case "face_eyeHappy_L": Map("Fcl_EYE_Joy_L", 1f); break;
                    case "face_eyeHappy_R": Map("Fcl_EYE_Joy_R", 1f); break;
                    case "face_smile_BS": Map("Fcl_MTH_Joy", 1f); break;
                    case "face_E_mouth_BS": Map("Fcl_MTH_E", 1f); break;
                    case "face_O_BS": Map("Fcl_MTH_O", 1f); break;
                    case "face_jaw_open_BS": Map("Fcl_MTH_A", 1f); break;
                    case "face_eye_close_L_BS": Map("Fcl_EYE_Close_L", 1f); break;
                    case "face_eye_close_R_BS": Map("Fcl_EYE_Close_R", 1f); break;
                    case "face_eye_wide_L_BS": Map("Game_EyeWide_L", 1f); break;
                    case "face_eye_wide_R_BS": Map("Game_EyeWide_R", 1f); break;
                    case "face_extra_01_BS": Map("Fcl_MTH_Up", .3f); break;
                    case "face_extra_02_BS": Map("Fcl_MTH_Joy", .25f); break;
                    case "face_extra_03_BS": Map("Fcl_BRW_Joy", .3f); break;
                    case "face_Puff_BS": Map("Fcl_MTH_SkinFung", .25f); break;
                    case "face_sad_BS":
                        Map("Fcl_MTH_Sorrow", .55f);
                        Map("Game_BrowSad_L", .65f);
                        Map("Game_BrowSad_R", .65f);
                        Map("Fcl_EYE_Sorrow", .3f);
                        break;
                    // The production face includes its teeth in the mouth morphs.
                    case "BottomTeeth_BS": Map("Fcl_MTH_O", .15f); break;
                    default:
                        throw new InvalidOperationException(
                            "Unmapped active happy facial curve: " + binding.path + "/" + shape);
                }

                void Map(string target, float strength)
                {
                    if (!targets.TryGetValue(target, out var values))
                    {
                        values = new float[frames];
                        targets.Add(target, values);
                    }
                    for (int frame = 0; frame < frames; frame++)
                    {
                        float time = Mathf.Min(clip.length, frame / clip.frameRate);
                        values[frame] = Mathf.Max(values[frame],
                            Mathf.Clamp(curve.Evaluate(time) * strength, 0, 100));
                    }
                }
            }

            // Write every production morph, not only the animated ones. The emotion
            // layer is an override layer, so the face must be fully asserted by this
            // clip or it keeps whatever the locomotion layer last wrote.
            for (int index = 0; index < face.sharedMesh.blendShapeCount; index++)
            {
                string shape = face.sharedMesh.GetBlendShapeName(index);
                var curve = new AnimationCurve();
                if (targets.TryGetValue(shape, out var values))
                {
                    for (int frame = 0; frame < frames; frame++)
                        curve.AddKey(Mathf.Min(clip.length, frame / clip.frameRate), values[frame]);
                }
                else
                {
                    curve = AnimationCurve.Constant(0, clip.length, 0);
                }
                for (int key = 0; key < curve.length; key++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(
                        curve, key, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(
                        curve, key, AnimationUtility.TangentMode.Linear);
                }
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer),
                        "blendShape." + shape), curve);
            }
        }

        private static void Validate(GameObject character,
            AnimatorController controller, AnimationClip clip)
        {
            if (clip.length < 1f || clip.length > 5f ||
                AnimationUtility.GetAnimationClipSettings(clip).loopTime)
                throw new InvalidOperationException("Happy animation must be a complete one-shot.");
            if (BindingCount(clip, typeof(Transform)) < 100)
                throw new InvalidOperationException(
                    "Happy animation has too few body bindings.");
            var face = character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Single(value => value.name == "lengyue_face_root");
            if (BindingCount(clip, typeof(SkinnedMeshRenderer)) !=
                face.sharedMesh.blendShapeCount)
                throw new InvalidOperationException(
                    "Happy animation must bind every production facial morph.");

            var layer = controller.layers.Single(value =>
                value.name == ResponseEmotionAudio.EmotionLayerName);
            var state = layer.stateMachine.states.Select(value => value.state)
                .SingleOrDefault(value => value.name == ResponseEmotionAudio.HappyStateName);
            if (!state || state.motion != clip || Mathf.Abs(state.speed) > .0001f)
                throw new InvalidOperationException(
                    "Happy state must use audio-clock sampling.");

            var peaks = new Dictionary<string, float>(StringComparer.Ordinal);
            string path = AnimationUtility.CalculateTransformPath(
                face.transform, character.transform);
            for (int index = 0; index < face.sharedMesh.blendShapeCount; index++)
            {
                string shape = face.sharedMesh.GetBlendShapeName(index);
                var curve = AnimationUtility.GetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(path,
                        typeof(SkinnedMeshRenderer), "blendShape." + shape));
                if (curve != null)
                    peaks[shape] = curve.keys.Max(key => key.value);
            }
            var top = peaks.OrderByDescending(pair => pair.Value).Take(10).ToArray();
            Debug.Log("MATESOUL_HAPPY_FACE peaks=" + string.Join(", ",
                top.Select(pair => pair.Key + ":" + pair.Value.ToString("F1"))));
            foreach (var shape in RequiredJoyShapes)
            {
                if (!peaks.TryGetValue(shape, out float peak))
                    throw new InvalidOperationException(
                        "Happy animation is missing the facial curve: " + shape);
                if (peak < 5f)
                    throw new InvalidOperationException(
                        $"Happy facial curve stays neutral: {shape}={peak:F1}; " +
                        "morph playback would not be visible.");
            }

            var audio = Resources.LoadAll<AudioClip>("ResponseEmotionAudio/Happy")
                .OrderByDescending(value => value.length).FirstOrDefault();
            if (!audio)
                throw new InvalidOperationException("Happy response audio is missing.");
            if (audio.length + .01f < clip.length)
                Debug.LogWarning(
                    $"MATESOUL_HAPPY_AUDIO_SHORT audio={audio.length:F3}s " +
                    $"animation={clip.length:F3}s; the performance will finish on its own clock.");

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
                    clip.SampleAnimation(instance,
                        Mathf.Min(clip.length, frame / clip.frameRate));
                    if (Vector3.ProjectOnPlane(hips.position - origin, Vector3.up).magnitude > .02f)
                        throw new InvalidOperationException(
                            "Happy animation must remain in place.");
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }

        private static int BindingCount(AnimationClip clip, Type type)
            => AnimationUtility.GetCurveBindings(clip).Count(binding => binding.type == type);
    }
}
