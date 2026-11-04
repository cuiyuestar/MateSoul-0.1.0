using System;
using System.IO;
using System.Linq;
using MateSoul.Runtime;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public static class BowlingAnimationBuilder
    {
        public const string DirectoryPath =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Bowling";
        public const string SourcePath = DirectoryPath + "/pick_up_bowlingball_InPlace.fbx";
        public const string ClipPath = DirectoryPath + "/Lengyue_Bowling.anim";
        public const string MotionPath = DirectoryPath + "/BowlingMotion.asset";

        [MenuItem("MateSoul/Lengyue/Import Bowling Animation")]
        public static void Import()
        {
            var canonical = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Prefabs/Lengyue_GameCharacter.prefab");
            var clip = LengyueIdle01Builder.ImportAuthoredAction(
                SourcePath, ClipPath, "Lengyue_Bowling", canonical, new[]
                {
                    "骨骼", "BowlingBall_Body", "BowlingBall_CharacterDecal",
                    "LeftFinger_DarkInterior", "LeftFinger_RoundedRim",
                    "RightFinger_DarkInterior", "RightFinger_RoundedRim",
                    "Thumb_DarkInterior", "Thumb_RoundedRim"
                });
            var motion = AssetDatabase.LoadAssetAtPath<BowlingMotion>(MotionPath);
            if (!motion)
            {
                motion = ScriptableObject.CreateInstance<BowlingMotion>();
                AssetDatabase.CreateAsset(motion, MotionPath);
            }
            motion.clip = clip;
            motion.pickupFrame = 20;
            motion.fallbackFrame = 21;
            motion.releaseFrame = 90;
            Calibrate(motion, canonical);
            motion.Validate();
            EditorUtility.SetDirty(motion);
            var contents = PrefabUtility.LoadPrefabContents(GameSceneBuilder.CharacterPath);
            try
            {
                ConfigureCharacter(contents);
                PrefabUtility.SaveAsPrefabAsset(contents, GameSceneBuilder.CharacterPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            // Refresh the scene instance while retaining the user's authored room layout.
            var scene = EditorSceneManager.OpenScene(GameSceneBuilder.ScenePath);
            foreach (var root in scene.GetRootGameObjects())
            foreach (var rig in root.GetComponentsInChildren<AvatarBowlingRig>(true))
                ConfigureCharacter(rig.gameObject);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"BOWLING_ANIMATION_IMPORTED fps={clip.frameRate} seconds={clip.length} " +
                      $"pickup={motion.pickupFrame} release={motion.releaseFrame} " +
                      $"handOffset={motion.ballInHand:F4} contact={motion.pickupCenter:F4}");
        }

        private static void Calibrate(BowlingMotion motion, GameObject canonical)
        {
            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            var source = Object.Instantiate(sourcePrefab);
            var target = Object.Instantiate(canonical);
            source.hideFlags = target.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var sourceClip = AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<AnimationClip>()
                    .Where(clip => !clip.name.StartsWith("__preview__"))
                    .OrderByDescending(clip => clip.length).First();
                var sourceHand = source.GetComponentsInChildren<Transform>(true)
                    .Single(bone => bone.name == "Hand_R");
                var targetHand = target.GetComponentsInChildren<Transform>(true)
                    .Single(bone => bone.name == "Hand_R");
                var reference = source.GetComponentsInChildren<MeshFilter>(true)
                    .Single(mesh => mesh.name == "BowlingBall_Body");
                // Source starts at authored frame 1; Unity's clip starts at time zero.
                // Frame 22 is fully held and avoids the pickup interpolation frame.
                sourceClip.SampleAnimation(source, 21f / sourceClip.frameRate);
                motion.clip.SampleAnimation(target, 21f / motion.clip.frameRate);
                var center = reference.transform.TransformPoint(reference.sharedMesh.bounds.center);
                var offset = Quaternion.Inverse(sourceHand.rotation) * (center - sourceHand.position);
                motion.ballInHand = targetHand.InverseTransformVector(targetHand.rotation * offset);
                var localRotation = Quaternion.Inverse(sourceHand.rotation) * reference.transform.rotation;
                motion.ballEulerInHand = localRotation.eulerAngles;
                motion.clip.SampleAnimation(target, motion.PickupTime);
                motion.pickupCenter = target.transform.InverseTransformPoint(
                    targetHand.TransformPoint(motion.ballInHand));
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(target); }
        }

        public static void ConfigureCharacter(GameObject character)
            => GameSceneBuilder.ConfigureBowlingCharacter(character);
    }
}
