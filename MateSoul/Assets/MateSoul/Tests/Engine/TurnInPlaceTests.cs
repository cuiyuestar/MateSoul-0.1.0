using System;
using System.Collections;
using System.Linq;
using MateSoul.Editor;
using MateSoul.RoomNavigation;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

namespace MateSoul.Tests.Engine
{
    public sealed class TurnInPlaceTests
    {
        [TestCase(37f)]
        [TestCase(-83f)]
        [TestCase(146f)]
        public void SolverConvergesForArbitraryAnglesWithoutOvershoot(float target)
        {
            var motion = new TurnInPlaceMotion();
            var current = 0f;
            var maximumRate = 0f;
            for (var index = 0; index < 500; index++)
            {
                var remaining = Mathf.DeltaAngle(current, target);
                var sample = motion.Tick(remaining, 180f, .02f);
                current += sample.DeltaAngle;
                maximumRate = Mathf.Max(maximumRate, Mathf.Abs(sample.NormalizedRate));
            }

            Assert.That(Mathf.Abs(Mathf.DeltaAngle(current, target)), Is.LessThan(.01f));
            Assert.That(maximumRate, Is.GreaterThan(.2f));
            Assert.That(motion.IsTurning, Is.False);
        }

        [Test]
        public void SolverKeepsDirectionWhenTargetJittersAcrossOppositeHeading()
        {
            var motion = new TurnInPlaceMotion();
            var first = motion.Tick(179f, 180f, .02f);
            var second = motion.Tick(-179f, 180f, .02f);

            Assert.That(first.DeltaAngle, Is.GreaterThan(0));
            Assert.That(second.DeltaAngle, Is.GreaterThan(0));
        }

        [Test]
        public void SmallYawSlidesButThresholdStartsCorrectiveStep()
        {
            var motion = new TurnInPlaceMotion();
            var small = motion.Tick(TurnInPlaceMotion.EnterAngle - 1f, 135f, .02f);
            Assert.That(small.DeltaAngle, Is.GreaterThan(0));
            Assert.That(small.IsTurning, Is.False);
            Assert.That(small.NormalizedRate, Is.Zero);

            var step = motion.Tick(TurnInPlaceMotion.EnterAngle + 1f, 135f, .02f);
            Assert.That(step.IsTurning, Is.True);
            Assert.That(step.NormalizedRate, Is.GreaterThan(0));
        }

        [Test]
        public void ProductionControllerContainsSocialIdleAndOneShotTurnSteps()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                AvatarLocomotionBuilder.ControllerPath);
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.parameters.Any(parameter =>
                parameter.name == "Speed" &&
                parameter.type == AnimatorControllerParameterType.Float), Is.True);
            Assert.That(controller.parameters.Any(parameter =>
                parameter.name == "TurnRate" &&
                parameter.type == AnimatorControllerParameterType.Float), Is.True);
            Assert.That(controller.parameters.Any(parameter =>
                parameter.name == "SocialAttention" &&
                parameter.type == AnimatorControllerParameterType.Bool), Is.True);
            Assert.That(controller.parameters.Any(parameter =>
                parameter.name == "TurnStepLeft" &&
                parameter.type == AnimatorControllerParameterType.Trigger), Is.True);
            Assert.That(controller.parameters.Any(parameter =>
                parameter.name == "TurnStepRight" &&
                parameter.type == AnimatorControllerParameterType.Trigger), Is.True);
            Assert.That(controller.parameters.Any(parameter =>
                parameter.name == "TurnStepActive" &&
                parameter.type == AnimatorControllerParameterType.Bool), Is.True);

            var states = controller.layers[0].stateMachine.states
                .Select(child => child.state).ToDictionary(state => state.name);
            var blend = states["Locomotion"].motion as BlendTree;
            Assert.That(blend, Is.Not.Null);
            Assert.That(blend.blendType, Is.EqualTo(BlendTreeType.Simple1D));
            Assert.That(blend.blendParameter, Is.EqualTo("Speed"));
            Assert.That(blend.children.Select(child => child.threshold),
                Is.EquivalentTo(new[] { 0f, .9f }));
            Assert.That(states["SocialAttention"].motion.name,
                Is.EqualTo("lengyue_clip_socialattention_idle_bodyonly"));
            Assert.That(states["TurnStepLeft"].motion.name,
                Is.EqualTo("lengyue_clip_turn_left_bodyonly"));
            Assert.That(states["TurnStepRight"].motion.name,
                Is.EqualTo("lengyue_clip_turn_right_bodyonly"));
            Assert.That(AnimationUtility.GetAnimationClipSettings(
                (AnimationClip)states["TurnStepLeft"].motion).loopTime, Is.False);
            Assert.That(AnimationUtility.GetAnimationClipSettings(
                (AnimationClip)states["TurnStepRight"].motion).loopTime, Is.False);
            foreach (var name in new[] { "TurnStepLeft", "TurnStepRight" })
            {
                var exits = states[name].transitions.Where(transition =>
                    transition.destinationState == states["Locomotion"] ||
                    transition.destinationState == states["SocialAttention"]).ToArray();
                Assert.That(exits.Length, Is.EqualTo(2));
                Assert.That(exits.All(transition => !transition.hasExitTime &&
                    transition.conditions.Any(condition =>
                        condition.parameter == "TurnStepActive" &&
                        condition.mode == AnimatorConditionMode.IfNot)), Is.True);
            }
        }

        [TestCase(AvatarLocomotionBuilder.TurnLeftPath)]
        [TestCase(AvatarLocomotionBuilder.TurnRightPath)]
        public void TurnClipsContainExplicitFootPlantCurves(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            Assert.That(clip, Is.Not.Null);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            var leftBinding = bindings.Single(binding =>
                binding.type == typeof(AvatarTurnFootPlantRig) &&
                binding.propertyName == "leftPlantWeight");
            var rightBinding = bindings.Single(binding =>
                binding.type == typeof(AvatarTurnFootPlantRig) &&
                binding.propertyName == "rightPlantWeight");
            var leftWeight = AnimationUtility.GetEditorCurve(
                clip, leftBinding).Evaluate(clip.length * .25f);
            var rightWeight = AnimationUtility.GetEditorCurve(
                clip, rightBinding).Evaluate(clip.length * .25f);
            if (path == AvatarLocomotionBuilder.TurnLeftPath)
                Assert.That(leftWeight, Is.GreaterThan(rightWeight));
            else
                Assert.That(rightWeight, Is.GreaterThan(leftWeight));
        }

        [TestCase(AvatarLocomotionBuilder.TurnLeftPath)]
        [TestCase(AvatarLocomotionBuilder.TurnRightPath)]
        public void TurnClipFootSwingStaysModerate(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                GameSceneBuilder.CharacterPath);
            var character = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var animator = character.GetComponentInChildren<Animator>();
                var transforms = animator.GetComponentsInChildren<Transform>(true);
                var leftFoot = transforms.Single(value => value.name == "lengyue_foot_l");
                var rightFoot = transforms.Single(value => value.name == "lengyue_foot_r");
                clip.SampleAnimation(animator.gameObject, 0);
                var leftStart = animator.transform.InverseTransformPoint(
                    leftFoot.position);
                var rightStart = animator.transform.InverseTransformPoint(
                    rightFoot.position);
                var leftStartRotation = leftFoot.rotation;
                var rightStartRotation = rightFoot.rotation;
                var maximumSwing = 0f;
                for (var frame = 0; frame <= Mathf.CeilToInt(clip.length * 60); frame++)
                {
                    clip.SampleAnimation(animator.gameObject,
                        Mathf.Min(clip.length, frame / 60f));
                    maximumSwing = Mathf.Max(maximumSwing,
                        Vector3.Distance(leftStart,
                            animator.transform.InverseTransformPoint(leftFoot.position)),
                        Vector3.Distance(rightStart,
                            animator.transform.InverseTransformPoint(rightFoot.position)));
                }
                TestContext.WriteLine($"{clip.name} footSwing={maximumSwing:F4}m");
                Assert.That(maximumSwing, Is.InRange(.025f, .16f));
                Assert.That(Vector3.Distance(leftStart,
                    animator.transform.InverseTransformPoint(leftFoot.position)),
                    Is.LessThan(.002f));
                Assert.That(Vector3.Distance(rightStart,
                    animator.transform.InverseTransformPoint(rightFoot.position)),
                    Is.LessThan(.002f));
                Assert.That(Quaternion.Angle(leftStartRotation, leftFoot.rotation),
                    Is.LessThan(1f));
                Assert.That(Quaternion.Angle(rightStartRotation, rightFoot.rotation),
                    Is.LessThan(1f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(character);
            }
        }

        [Test]
        public void SocialIdleContainsOnlyProductionBodyCurves()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                AvatarLocomotionBuilder.SocialAttentionPath);
            Assert.That(clip, Is.Not.Null);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.That(bindings.Any(binding =>
                binding.type == typeof(Transform) &&
                binding.path.EndsWith("/lengyue_head_01", StringComparison.Ordinal)), Is.True);
            Assert.That(bindings.Any(binding =>
                binding.type != typeof(Transform)), Is.False);
            Assert.That(bindings.Any(binding =>
            {
                var leaf = binding.path.Split('/').Last();
                return leaf.StartsWith("lengyue_hair_", StringComparison.Ordinal) ||
                       leaf.StartsWith("lengyue_skirt_", StringComparison.Ordinal) ||
                       leaf.StartsWith("lengyue_outer_skirt_", StringComparison.Ordinal);
            }), Is.False);
        }

        [UnityTest]
        public IEnumerator ProductionAvatarTurnsInPlaceWithoutLinearDrift()
        {
            yield return new EnterPlayMode();
            yield return ExerciseProductionAvatarTurn(73f);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator ProductionAvatarTurnsLeftWithPlantedFeet()
        {
            yield return new EnterPlayMode();
            yield return ExerciseProductionAvatarTurn(-112f);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator SmallYawDoesNotTriggerFootStep()
        {
            yield return new EnterPlayMode();
            yield return ExerciseStepFrequency(26f, 48f, 2.2f, 0);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator SlowLargeTurnUsesSeparatedCorrectiveSteps()
        {
            yield return new EnterPlayMode();
            yield return ExerciseStepFrequency(120f, 48f, 4f, 2);
            yield return new ExitPlayMode();
        }

        private static IEnumerator ExerciseProductionAvatarTurn(float targetAngle)
        {
            var roomObject = new GameObject("Turn test room");
            roomObject.AddComponent<RuntimeRoomNavigation>().Build(
                new RoomMapSnapshot("turn-test", "turn-test", new[]
                {
                    new RoomFloorRegion("floor", new[]
                    {
                        new Vector3(-5, 0, -5), new Vector3(-5, 0, 5),
                        new Vector3(5, 0, 5), new Vector3(5, 0, -5)
                    })
                }, Array.Empty<RoomObstacle>()), false);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameSceneBuilder.CharacterPath);
            var character = UnityEngine.Object.Instantiate(prefab);
            character.transform.position = Vector3.up * .03f;
            var animator = character.GetComponentInChildren<Animator>();
            var motor = character.GetComponent<AvatarMotor>();
            var footRig = character.GetComponent<AvatarTurnFootPlantRig>();
            motor.Bind(animator);
            Assert.That(footRig, Is.Not.Null);
            Assert.That(footRig.IsConfigured, Is.True);
            Physics.SyncTransforms();
            yield return null;

            var start = motor.Position;
            var targetDirection = Quaternion.Euler(0, targetAngle, 0) * Vector3.forward;
            motor.FaceAtSpeed(start + targetDirection * 2f, 150f);
            var peakTurnRate = 0f;
            var peakStepWeight = 0f;
            var peakPlantWeight = 0f;
            var maximumPlantSlip = 0f;
            var trackLeft = false;
            var trackRight = false;
            var leftAnchor = Vector3.zero;
            var rightAnchor = Vector3.zero;
            var initialLeftFoot = character.transform.InverseTransformPoint(
                footRig.LeftFoot.position);
            var initialRightFoot = character.transform.InverseTransformPoint(
                footRig.RightFoot.position);
            var maximumFootStep = 0f;
            var unfinishedIdleSeconds = 0f;
            var observedStep = false;
            for (var elapsed = 0f; elapsed < 5f; elapsed += Time.deltaTime)
            {
                yield return null;
                var inStep = IsTurnStep(animator.GetCurrentAnimatorStateInfo(0));
                if (animator.IsInTransition(0))
                    inStep |= IsTurnStep(animator.GetNextAnimatorStateInfo(0));
                observedStep |= inStep;
                var remaining = Vector3.Angle(
                    motor.Body.Pose.rotation * Vector3.forward, targetDirection);
                if (observedStep && !inStep &&
                    remaining > TurnInPlaceMotion.ExitAngle)
                    unfinishedIdleSeconds += Time.deltaTime;
                peakTurnRate = Mathf.Max(peakTurnRate,
                    Mathf.Abs(animator.GetFloat("TurnRate")));
                foreach (var clip in animator.GetCurrentAnimatorClipInfo(0))
                    if (clip.clip != null && clip.clip.name == "lengyue_clip_walk_bodyonly")
                        peakStepWeight = Mathf.Max(peakStepWeight, clip.weight);
                    else if (clip.clip != null &&
                             clip.clip.name.IndexOf(
                                 "Turn", StringComparison.Ordinal) >= 0)
                        peakStepWeight = Mathf.Max(peakStepWeight, clip.weight);
                peakPlantWeight = Mathf.Max(peakPlantWeight,
                    Mathf.Max(footRig.LeftWeight, footRig.RightWeight));
                MeasurePlantSlip(footRig.LeftFoot, footRig.LeftWeight,
                    ref trackLeft, ref leftAnchor, ref maximumPlantSlip);
                MeasurePlantSlip(footRig.RightFoot, footRig.RightWeight,
                    ref trackRight, ref rightAnchor, ref maximumPlantSlip);
                maximumFootStep = Mathf.Max(maximumFootStep,
                    Vector3.Distance(initialLeftFoot,
                        character.transform.InverseTransformPoint(
                            footRig.LeftFoot.position)),
                    Vector3.Distance(initialRightFoot,
                        character.transform.InverseTransformPoint(
                            footRig.RightFoot.position)));
            }

            var finalDirection = Vector3.ProjectOnPlane(
                motor.Body.Pose.rotation * Vector3.forward, Vector3.up);
            Assert.That(Vector3.Angle(finalDirection, targetDirection), Is.LessThan(1f));
            Assert.That(Vector3.ProjectOnPlane(motor.Position - start, Vector3.up).magnitude,
                Is.LessThan(.02f));
            Assert.That(peakTurnRate, Is.GreaterThan(.25f));
            Assert.That(peakStepWeight, Is.GreaterThan(.25f));
            Assert.That(maximumFootStep, Is.GreaterThan(.025f),
                "Corrective turn must contain a visible foot step.");
            Assert.That(peakPlantWeight, Is.GreaterThan(.65f));
            Assert.That(maximumPlantSlip, Is.LessThan(.025f));
            Assert.That(unfinishedIdleSeconds, Is.Zero,
                "Idle must not take over an unfinished facing episode.");
            TestContext.WriteLine(
                $"target={targetAngle:F0} maxPlantSlip={maximumPlantSlip:F5}");
            Assert.That(animator.GetFloat("TurnRate"), Is.EqualTo(0).Within(.01f));
            Assert.That(motor.IsTurning, Is.False);

            UnityEngine.Object.Destroy(character);
            UnityEngine.Object.Destroy(roomObject);
            yield return null;
        }

        private static IEnumerator ExerciseStepFrequency(float targetAngle,
            float speed, float duration, int maximumSteps)
        {
            var roomObject = new GameObject("Turn frequency test room");
            roomObject.AddComponent<RuntimeRoomNavigation>().Build(
                new RoomMapSnapshot("turn-frequency", "turn-frequency", new[]
                {
                    new RoomFloorRegion("floor", new[]
                    {
                        new Vector3(-5, 0, -5), new Vector3(-5, 0, 5),
                        new Vector3(5, 0, 5), new Vector3(5, 0, -5)
                    })
                }, Array.Empty<RoomObstacle>()), false);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                GameSceneBuilder.CharacterPath);
            var character = UnityEngine.Object.Instantiate(prefab);
            character.transform.position = Vector3.up * .03f;
            var animator = character.GetComponentInChildren<Animator>();
            var motor = character.GetComponent<AvatarMotor>();
            motor.Bind(animator);
            Physics.SyncTransforms();
            yield return null;

            var direction = Quaternion.Euler(0, targetAngle, 0) * Vector3.forward;
            motor.FaceAtSpeed(motor.Position + direction * 2f, speed);
            var stepCount = 0;
            var wasInStep = false;
            var wasCurrentInStep = false;
            var previousState = 0;
            var previousNormalizedTime = 0f;
            var previousStepTime = float.NegativeInfinity;
            var minimumInterval = float.PositiveInfinity;
            for (var elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                yield return null;
                var current = animator.GetCurrentAnimatorStateInfo(0);
                var inStep = IsTurnStep(current);
                if (animator.IsInTransition(0))
                    inStep |= IsTurnStep(animator.GetNextAnimatorStateInfo(0));
                var continuedStep = IsTurnStep(current) &&
                    wasCurrentInStep &&
                    (current.shortNameHash != previousState ||
                     current.normalizedTime < previousNormalizedTime - .2f);
                if (inStep && (!wasInStep || continuedStep))
                {
                    stepCount++;
                    minimumInterval = Mathf.Min(
                        minimumInterval, elapsed - previousStepTime);
                    previousStepTime = elapsed;
                }
                wasInStep = inStep;
                wasCurrentInStep = IsTurnStep(current);
                previousState = current.shortNameHash;
                previousNormalizedTime = current.normalizedTime;
            }

            Assert.That(stepCount, Is.LessThanOrEqualTo(maximumSteps));
            if (stepCount > 1)
                Assert.That(minimumInterval, Is.GreaterThan(1.2f));
            if (maximumSteps > 0)
                Assert.That(stepCount, Is.GreaterThanOrEqualTo(1));

            UnityEngine.Object.Destroy(character);
            UnityEngine.Object.Destroy(roomObject);
            yield return null;
        }

        private static bool IsTurnStep(AnimatorStateInfo state)
            => state.shortNameHash == Animator.StringToHash("TurnStepLeft") ||
               state.shortNameHash == Animator.StringToHash("TurnStepRight");

        private static void MeasurePlantSlip(Transform foot, float weight,
            ref bool tracking, ref Vector3 anchor, ref float maximum)
        {
            if (weight < .65f)
            {
                tracking = false;
                return;
            }
            if (!tracking)
            {
                anchor = foot.position;
                tracking = true;
                return;
            }
            maximum = Mathf.Max(maximum, Vector3.Distance(anchor, foot.position));
        }
    }
}
