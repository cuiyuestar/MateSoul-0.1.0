using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MateSoul.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public static class BowlingAnimationValidation
    {
        [Serializable]
        private sealed class SampleResult
        {
            public int fps;
            public float startingError, pickupTime, releaseTime, maximumHeldError;
            public float maximumPoseErrorDegrees, handTravel;
            public Vector3 launchVelocity, launchAngularVelocity;
            public string tier;
            public int releases;
            public bool restoredPhysics, cancelledRestoresPhysics;
        }

        [Serializable]
        private sealed class Report
        {
            public bool passed;
            public float animationFps, length, contactHeight, sourceReferenceError;
            public int bodyCurves, faceCurves;
            public List<SampleResult> cases = new List<SampleResult>();
        }

        [MenuItem("MateSoul/Lengyue/Validate Bowling Animation")]
        public static void Run()
        {
            if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(GameSceneBuilder.ScenePath);
            var motion = AssetDatabase.LoadAssetAtPath<BowlingMotion>(
                "Assets/Characters/LengyueCharacter/Lengyue/Runtime/Animations/Bowling/lengyue_motion_bowling.asset");
            motion.Validate();
            var report = new Report
            {
                animationFps = motion.clip.frameRate, length = motion.clip.length,
                contactHeight = motion.pickupCenter.y
            };
            var bindings = AnimationUtility.GetCurveBindings(motion.clip);
            report.bodyCurves = bindings.Count(binding => binding.type == typeof(Transform));
            report.faceCurves = bindings.Count(binding => binding.type == typeof(SkinnedMeshRenderer));
            Require(!AnimationUtility.GetAnimationClipSettings(motion.clip).loopTime,
                "Roll clip must not loop.");
            foreach (var fps in new[] { 15, 30, 72, 120 })
            foreach (var error in new[] { 0f, .22f, 1.2f })
                report.cases.Add(ValidateRun(motion, fps, error));
            report.sourceReferenceError = ValidateReference(motion);
            report.passed = true;
            var path = Path.GetFullPath(Application.dataPath +
                "/../../项目沉淀资料/validation/bowling_animation_20261022.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log($"BOWLING_ANIMATION_VALIDATED cases={report.cases.Count} " +
                      $"referenceError={report.sourceReferenceError:F6} curves={report.bodyCurves}");
        }

        private static SampleResult ValidateRun(BowlingMotion motion, int fps, float error)
        {
            var character = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                GameSceneBuilder.CharacterPath));
            var ballObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                BowlingContentBuilder.BallPath));
            var reference = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                GameSceneBuilder.CharacterPath));
            var physicsScene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Additive);
            SceneManager.MoveGameObjectToScene(ballObject, physicsScene);
            foreach (var collider in character.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (var collider in reference.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            character.hideFlags = ballObject.hideFlags = reference.hideFlags = HideFlags.HideAndDontSave;
            var body = ballObject.GetComponent<PhysicsBody>();
            body.Initialize();
            var animator = character.GetComponentInChildren<Animator>(true);
            var rig = character.GetComponent<AvatarBowlingRig>();
            var boneNames = new[] { "lengyue_hips", "lengyue_spine", "lengyue_upper_arm_r", "lengyue_forearm_r", "lengyue_hand_r", "lengyue_thigh_r" };
            var actualBones = boneNames.Select(name => character.GetComponentsInChildren<Transform>(true)
                .Single(bone => bone.name == name)).ToArray();
            var expectedBones = boneNames.Select(name => reference.GetComponentsInChildren<Transform>(true)
                .Single(bone => bone.name == name)).ToArray();
            var run = Guid.NewGuid();
            var result = new SampleResult { fps = fps, startingError = error, pickupTime = -1, releaseTime = -1 };
            try
            {
                // Baseline is intentionally an active random-idle variant.
                animator.enabled = true;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.SetInteger("IdleVariant", 1);
                animator.Play("Idle02", 0, .3f);
                animator.Update(0);
                motion.clip.SampleAnimation(animator.gameObject, motion.PickupTime);
                var source = rig.BallAtHand;
                source.position += Vector3.right * error;
                body.Place(source);
                rig.Begin(run, body, Vector3.forward, 3, () => result.releases++);
                var initial = body.Pose;
                var expectedTier = error < AvatarBowlingRig.NearPickupDistance ? "Near" :
                    error <= AvatarBowlingRig.FallbackPickupDistance ? "Expanded" : "Unconditional";
                var step = 1f / fps;
                Vector3? firstHand = null;
                for (var time = 0f; time <= motion.clip.length + step; time += step)
                {
                    rig.Sample(time);
                    animator.Update(0);
                    rig.SyncAfterAnimation();
                    Require(animator.GetCurrentAnimatorStateInfo(animator.GetLayerIndex("Bowling")).fullPathHash ==
                        Animator.StringToHash("Bowling.Roll"), "Bowling Animator state was not evaluated.");
                    motion.clip.SampleAnimation(reference, Mathf.Min(time, motion.clip.length));
                    for (var bone = 0; bone < actualBones.Length; bone++)
                        result.maximumPoseErrorDegrees = Mathf.Max(result.maximumPoseErrorDegrees,
                            Quaternion.Angle(actualBones[bone].localRotation, expectedBones[bone].localRotation));
                    var handPosition = actualBones[4].position;
                    if (!firstHand.HasValue) firstHand = handPosition;
                    result.handTravel = Mathf.Max(result.handTravel,
                        Vector3.Distance(firstHand.Value, handPosition));
                    if (time + .00001f < motion.PickupTime)
                    {
                        Require(!body.IsInteractionControlled, "Ball acquired before pickup frame.");
                        Require(Vector3.Distance(body.Pose.position, initial.position) < .00001f,
                            "Ball moved before pickup.");
                    }
                    if (rig.LastPickupTier != AvatarBowlingRig.PickupTier.None && result.pickupTime < 0)
                    {
                        result.pickupTime = time;
                        result.tier = rig.LastPickupTier.ToString();
                        Require(body.Rigidbody.isKinematic && !body.Rigidbody.useGravity &&
                                !body.Rigidbody.detectCollisions, "Held ball must be non-physical.");
                    }
                    if (body.IsInteractionControlled)
                        result.maximumHeldError = Mathf.Max(result.maximumHeldError,
                            Vector3.Distance(body.Pose.position, rig.BallAtHand.position));
                    if (rig.DidRelease && result.releaseTime < 0) result.releaseTime = time;
                }
                Require(result.tier == expectedTier, $"Wrong pickup tier: {result.tier}, expected {expectedTier}.");
                Require(result.pickupTime >= motion.PickupTime - .00001f &&
                        result.pickupTime <= motion.FallbackTime + step + .00001f, "Pickup event skipped.");
                Require(result.releaseTime >= motion.ReleaseTime - .00001f &&
                        result.releaseTime <= motion.ReleaseTime + step + .00001f, "Release event skipped.");
                Require(result.maximumHeldError < .0001f, "Hand and ball drifted during animation.");
                Require(result.maximumPoseErrorDegrees < .1f && result.handTravel > .4f,
                    $"Authored pose was not played: error={result.maximumPoseErrorDegrees}, travel={result.handTravel}.");
                result.restoredPhysics = !body.IsInteractionControlled && !body.Rigidbody.isKinematic &&
                    body.Rigidbody.useGravity && body.Rigidbody.detectCollisions;
                Require(result.restoredPhysics && result.releases == 1, "Release must restore physics exactly once.");
                var simulation = Physics.simulationMode;
                try
                {
                    Physics.simulationMode = SimulationMode.Script;
                    Physics.SyncTransforms();
                    physicsScene.GetPhysicsScene().Simulate(.02f);
                }
                finally { Physics.simulationMode = simulation; }
                result.launchVelocity = body.Rigidbody.velocity;
                result.launchAngularVelocity = body.Rigidbody.angularVelocity;
                Require(body.Rigidbody.velocity.z > 2.9f && body.Rigidbody.angularVelocity.x > 20,
                    $"Rolling launch momentum not applied: v={body.Rigidbody.velocity}, spin={body.Rigidbody.angularVelocity}.");
                rig.End(run);
                // Cancellation releases ownership without inventing a launch.
                rig.Begin(run, body, Vector3.forward, 3, () => result.releases++);
                rig.Sample(motion.FallbackTime);
                animator.Update(0);
                rig.SyncAfterAnimation();
                rig.End(run);
                result.cancelledRestoresPhysics = !body.IsInteractionControlled &&
                    !body.Rigidbody.isKinematic && body.Rigidbody.useGravity &&
                    body.Rigidbody.detectCollisions && animator.GetLayerWeight(animator.GetLayerIndex("Bowling")) == 0;
                Require(result.cancelledRestoresPhysics && result.releases == 1, "Cancellation leaked state.");
                return result;
            }
            finally
            {
                rig.End(run);
                Object.DestroyImmediate(character);
                Object.DestroyImmediate(ballObject);
                Object.DestroyImmediate(reference);
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(physicsScene, true);
            }
        }

        private static float ValidateReference(BowlingMotion motion)
        {
            const string path = "Assets/Characters/LengyueCharacter/Lengyue/Runtime/Animations/Bowling/pick_up_bowlingball_InPlace.fbx";
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            var target = AssetDatabase.LoadAssetAtPath<GameObject>(GameSceneBuilder.CharacterPath)
                .GetComponentsInChildren<Transform>(true).Single(value => value.name == "lengyue_hand_r");
            var heldOffset = Vector3.Scale(motion.ballInHand, target.lossyScale);
            root.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .Where(value => !value.name.StartsWith("__preview__"))
                    .OrderByDescending(value => value.length).First();
                var hand = root.GetComponentsInChildren<Transform>(true).Single(value => value.name == "lengyue_hand_r");
                var ball = root.GetComponentsInChildren<MeshFilter>(true).Single(value => value.name == "BowlingBall_Body");
                var max = 0f;
                for (var frame = 24; frame <= 87; frame++)
                {
                    clip.SampleAnimation(root, frame / clip.frameRate);
                    var center = ball.transform.TransformPoint(ball.sharedMesh.bounds.center);
                    // Compare authored attachment in world metres, independent of FBX armature scale.
                    var offset = Quaternion.Inverse(hand.rotation) * (center - hand.position);
                    max = Mathf.Max(max, Vector3.Distance(offset, heldOffset));
                }
                Require(max < .012f, $"Calibrated hand offset differs from reference by {max}m.");
                return max;
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void Require(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException(message);
        }
    }
}
