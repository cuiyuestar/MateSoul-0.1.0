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
    public static class AngryEmotionBuilder
    {
        public const string DirectoryPath =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Emotion/Angry";
        public const string SourcePath = DirectoryPath + "/Mad_01_InPlace.fbx";
        public const string ClipPath = DirectoryPath + "/Lengyue_Angry01.anim";

        [MenuItem("MateSoul/Lengyue/Import Angry Emotion")]
        public static void Import()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(
                GameSceneBuilder.CharacterPath);
            if (!character)
                throw new InvalidOperationException(
                    "Production character prefab is missing.");
            // Character authoring tools belong to Unity's predefined editor
            // assembly, which cannot be referenced by this asmdef.
            var importer = Type.GetType(
                "MateSoul.Editor.LengyueIdle01Builder, Assembly-CSharp-Editor", true);
            var excludedSourceBones = new[]
            {
                "PelvisRoot", "Head_End",
                "Breast_L", "Breast_R",
                "Breast_Tip_L", "Breast_Tip_R",
                "Mech_cover_L", "Mech_cover_R",
                "Mech_In_L", "Mech_In_R",
                "Mech_Out_L", "Mech_Out_R",
                "UpperArmTwist_L", "UpperArmTwist_R"
            };
            var clip = (AnimationClip)importer.GetMethod("ImportAuthoredActionTracks")
                .Invoke(null, new object[] { SourcePath, ClipPath,
                    ResponseEmotionAudio.AngryClipName, character,
                    excludedSourceBones, false });
            MapFacialPerformance(character, clip);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                AvatarLocomotionBuilder.ControllerPath);
            if (!controller)
                throw new InvalidOperationException(
                    "Production avatar controller is missing.");
            AvatarLocomotionBuilder.ConfigureEmotionLayer(controller, clip);
            Validate(character, controller, clip);
            EditorUtility.SetDirty(clip);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"MATESOUL_ANGRY_EMOTION_IMPORTED seconds={clip.length:F3} " +
                $"fps={clip.frameRate:F1} body={TransformBindingCount(clip)} " +
                $"face={FacialBindingCount(clip)}");
        }

        private static void MapFacialPerformance(GameObject character, AnimationClip clip)
        {
            var source = AssetDatabase.LoadAllAssetsAtPath(SourcePath)
                .OfType<AnimationClip>()
                .Where(value => !value.name.StartsWith("__preview__", StringComparison.Ordinal))
                .OrderByDescending(FacialBindingCount)
                .First();
            var face = character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Single(value => value.name == "Face");
            var path = AnimationUtility.CalculateTransformPath(
                face.transform, character.transform);
            // The production face has a different topology and morph vocabulary.
            // Closed eyes transfer directly; the authored pout and cheek-puff
            // timing is adapted with existing mouth morphs (no mesh replacement).
            for (int index = 0; index < face.sharedMesh.blendShapeCount; index++)
                SetFacialCurve(clip, face, path, face.sharedMesh.GetBlendShapeName(index),
                    AnimationCurve.Constant(0, clip.length, 0));
            // Both eyes are held closed throughout the FBX. Unity omits these
            // static shape weights from the imported animation curves.
            SetFacialCurve(clip, face, path, "Fcl_EYE_Close_L",
                AnimationCurve.Constant(0, clip.length, 100));
            SetFacialCurve(clip, face, path, "Fcl_EYE_Close_R",
                AnimationCurve.Constant(0, clip.length, 100));

            var requiredTracks = new HashSet<string>
                { "face_sad_BS", "face_Puff_BS", "face_extra_01_BS" };
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
                    case "face_eye_close_L_BS":
                        Copy("Fcl_EYE_Close_L", 1);
                        break;
                    case "face_eye_close_R_BS":
                        Copy("Fcl_EYE_Close_R", 1);
                        break;
                    case "face_sad_BS":
                        Copy("Fcl_MTH_Angry", .65f);
                        Copy("Fcl_BRW_Angry", .35f);
                        break;
                    case "face_Puff_BS":
                        Copy("Fcl_MTH_Small", .35f);
                        Copy("Fcl_MTH_U", .12f);
                        break;
                    case "face_extra_01_BS":
                        Copy("Fcl_MTH_Down", .25f);
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unmapped active angry facial curve: " + binding.path + "/" + shape);
                }
                requiredTracks.Remove(shape);

                void Copy(string targetShape, float strength)
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
                        AnimationUtility.SetKeyLeftTangentMode(mapped, key,
                            AnimationUtility.TangentMode.Linear);
                        AnimationUtility.SetKeyRightTangentMode(mapped, key,
                            AnimationUtility.TangentMode.Linear);
                    }
                    SetFacialCurve(clip, face, path, targetShape, mapped);
                }
            }
            if (requiredTracks.Count > 0)
                throw new InvalidOperationException(
                    "Missing authored angry facial tracks: " +
                    string.Join(", ", requiredTracks.OrderBy(value => value)));
        }

        private static void SetFacialCurve(AnimationClip clip, SkinnedMeshRenderer face,
            string path, string shape, AnimationCurve curve)
        {
            if (face.sharedMesh.GetBlendShapeIndex(shape) < 0)
                throw new InvalidOperationException("Missing production facial morph: " + shape);
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer),
                    "blendShape." + shape), curve);
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
                    .Single(value => value.name == "Hips");
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
