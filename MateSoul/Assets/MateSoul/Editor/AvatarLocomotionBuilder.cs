using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MateSoul.Runtime;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MateSoul.Editor
{
    public static class AvatarLocomotionBuilder
    {
        public const string ControllerPath = "Assets/MateSoul/Content/Avatar.controller";
        public const string IdlePath =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/" +
            "Idle01/Lengyue_Idle01_Combined.anim";
        public const string Idle02Path =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/" +
            "Idle02/Lengyue_Idle02_Combined.anim";
        public const string Idle03Path =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/" +
            "SocialAttention/Lengyue_SocialAttention_Idle_BodyOnly.anim";
        public const string WalkPath =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/" +
            "Lengyue_Walk_BodyOnly.anim";
        public const string TurnLeftPath =
            "Assets/MateSoul/Content/Locomotion/Lengyue_TurnLeft_BodyOnly.anim";
        public const string TurnRightPath =
            "Assets/MateSoul/Content/Locomotion/Lengyue_TurnRight_BodyOnly.anim";
        public const string SocialAttentionPath =
            Idle03Path;
        private const string CharacterPrefabPath =
            "Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Prefabs/" +
            "Lengyue_GameCharacter.prefab";

        [MenuItem("MateSoul/Game/Rebuild Avatar Locomotion")]
        public static void BuildProductionController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                throw new InvalidOperationException("Production avatar controller is missing.");
            ConfigureProductionController(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            Debug.Log("MATESOUL_LOCOMOTION_BUILT controller=" + ControllerPath);
        }

        public static void ConfigureProductionController(AnimatorController controller)
        {
            var idle = LoadClip(IdlePath);
            var idle02 = LoadClip(Idle02Path);
            var idle03 = LoadClip(Idle03Path);
            var walk = LoadClip(WalkPath);
            var socialIdle = LoadClip(SocialAttentionPath);
            BuildTurnClips(idle, walk, out var left, out var right);
            Configure(controller, idle, idle02, idle03, walk,
                left, right, socialIdle);
        }

        private static void Configure(AnimatorController controller,
            AnimationClip idle, AnimationClip idle02, AnimationClip idle03,
            AnimationClip walk, AnimationClip leftTurn,
            AnimationClip rightTurn, AnimationClip socialIdle)
        {
            if (controller == null || idle == null || idle02 == null ||
                idle03 == null || walk == null || leftTurn == null ||
                rightTurn == null || socialIdle == null)
                throw new ArgumentNullException("Locomotion content is incomplete.");
            EnsureFloatParameter(controller, "Speed");
            EnsureFloatParameter(controller, "TurnRate");
            EnsureParameter(controller, "IdleVariant",
                AnimatorControllerParameterType.Int);
            EnsureParameter(controller, "SocialAttention",
                AnimatorControllerParameterType.Bool);
            EnsureParameter(controller, "TurnStepLeft",
                AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "TurnStepRight",
                AnimatorControllerParameterType.Trigger);

            var machine = controller.layers[0].stateMachine;
            var locomotion = machine.states.Select(child => child.state)
                            .FirstOrDefault(value => value.name == "Locomotion") ??
                        machine.AddState("Locomotion");
            var blend = locomotion.motion as BlendTree;
            if (blend == null)
            {
                blend = new BlendTree();
                AssetDatabase.AddObjectToAsset(blend, controller);
            }
            blend.name = "Idle Walk";
            blend.blendType = BlendTreeType.Simple1D;
            blend.blendParameter = "Speed";
            blend.useAutomaticThresholds = false;
            blend.children = new[]
            {
                Child(idle, 0),
                Child(walk, .9f)
            };
            locomotion.motion = blend;
            locomotion.writeDefaultValues = true;

            var attention = GetOrAddState(machine, "SocialAttention");
            attention.motion = socialIdle;
            attention.writeDefaultValues = true;
            var alternateIdle = GetOrAddState(machine, "Idle02");
            alternateIdle.motion = idle02;
            alternateIdle.writeDefaultValues = true;
            var thirdIdle = GetOrAddState(machine, "Idle03");
            thirdIdle.motion = idle03;
            thirdIdle.writeDefaultValues = true;
            var turnLeft = GetOrAddState(machine, "TurnStepLeft");
            turnLeft.motion = leftTurn;
            turnLeft.writeDefaultValues = true;
            var turnRight = GetOrAddState(machine, "TurnStepRight");
            turnRight.motion = rightTurn;
            turnRight.writeDefaultValues = true;

            ClearTransitions(locomotion);
            ClearTransitions(attention);
            ClearTransitions(alternateIdle);
            ClearTransitions(thirdIdle);
            ClearTransitions(turnLeft);
            ClearTransitions(turnRight);
            AddConditionTransition(locomotion, attention,
                "SocialAttention", AnimatorConditionMode.If, .2f);
            AddConditionTransition(attention, locomotion,
                "SocialAttention", AnimatorConditionMode.IfNot, .2f);
            AddConditionTransition(locomotion, turnLeft,
                "TurnStepLeft", AnimatorConditionMode.If, .14f);
            AddConditionTransition(locomotion, turnRight,
                "TurnStepRight", AnimatorConditionMode.If, .14f);
            AddIdleVariantTransition(locomotion, alternateIdle, 1);
            AddIdleVariantTransition(locomotion, thirdIdle, 2);
            ConfigureAlternateIdle(alternateIdle, locomotion, thirdIdle,
                attention, turnLeft, turnRight, 0, 2);
            ConfigureAlternateIdle(thirdIdle, locomotion, alternateIdle,
                attention, turnLeft, turnRight, 0, 1);
            AddConditionTransition(attention, turnLeft,
                "TurnStepLeft", AnimatorConditionMode.If, .14f);
            AddConditionTransition(attention, turnRight,
                "TurnStepRight", AnimatorConditionMode.If, .14f);
            AddExitTransition(turnLeft, attention, true);
            AddExitTransition(turnLeft, locomotion, false);
            AddExitTransition(turnRight, attention, true);
            AddExitTransition(turnRight, locomotion, false);

            machine.defaultState = locomotion;
            EditorUtility.SetDirty(blend);
            EditorUtility.SetDirty(locomotion);
            EditorUtility.SetDirty(attention);
            EditorUtility.SetDirty(alternateIdle);
            EditorUtility.SetDirty(thirdIdle);
            EditorUtility.SetDirty(turnLeft);
            EditorUtility.SetDirty(turnRight);
            EditorUtility.SetDirty(controller);
        }

        private static void BuildTurnClips(AnimationClip idle, AnimationClip walk,
            out AnimationClip left, out AnimationClip right)
        {
            Directory.CreateDirectory("Assets/MateSoul/Content/Locomotion");
            // Reviewed turn clips already carry the current runtime skeleton paths.
            // Preserve them when rebuilding a scene; explicit authoring can regenerate
            // legacy clips separately before a new skeleton migration.
            left = AssetDatabase.LoadAssetAtPath<AnimationClip>(TurnLeftPath);
            right = AssetDatabase.LoadAssetAtPath<AnimationClip>(TurnRightPath);
            if (left != null && right != null) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefabPath);
            if (prefab == null)
                throw new InvalidOperationException("Production character source is missing.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var animator = instance.GetComponentInChildren<Animator>(true);
                var transforms = animator.GetComponentsInChildren<Transform>(true);
                var bones = new[]
                {
                    "Hips", "Spine", "Chest",
                    "Leg_L", "Knee_L", "Foot_L",
                    "Leg_R", "Knee_R", "Foot_R",
                    "UpperArm_L", "Forearm_L", "UpperArm_R", "Forearm_R"
                }.ToDictionary(name => name,
                    name => transforms.Single(value => value.name == name));
                idle.SampleAnimation(animator.gameObject, 0);
                var idleRotations = bones.ToDictionary(
                    pair => pair.Key, pair => pair.Value.localRotation);
                var idlePositions = bones.ToDictionary(
                    pair => pair.Key, pair => pair.Value.localPosition);

                left = BuildTurnClip(TurnLeftPath, "Lengyue_TurnLeft_BodyOnly",
                    -1, animator, bones, idleRotations, idlePositions, walk);
                right = BuildTurnClip(TurnRightPath, "Lengyue_TurnRight_BodyOnly",
                    1, animator, bones, idleRotations, idlePositions, walk);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static AnimationClip BuildTurnClip(string path, string name, float direction,
            Animator animator, IReadOnlyDictionary<string, Transform> bones,
            IReadOnlyDictionary<string, Quaternion> idleRotations,
            IReadOnlyDictionary<string, Vector3> idlePositions, AnimationClip walk)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = UnityEngine.Object.Instantiate(walk);
                clip.name = name;
                AssetDatabase.CreateAsset(clip, path);
            }
            else
            {
                EditorUtility.CopySerialized(walk, clip);
                clip.name = name;
            }

            const float frameRate = 30f;
            var frameCount = Mathf.CeilToInt(walk.length * frameRate);
            var rotationCurves = bones.ToDictionary(
                pair => pair.Key, _ => new[]
                    { new AnimationCurve(), new AnimationCurve(),
                        new AnimationCurve(), new AnimationCurve() });
            var hipsPositionCurves = new[]
                { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            var previous = new Dictionary<string, Quaternion>();

            for (var frame = 0; frame <= frameCount; frame++)
            {
                var time = Mathf.Min(frame / frameRate, walk.length);
                var phase = walk.length > 0 ? time / walk.length : 0;
                var sine = Mathf.Sin(phase * Mathf.PI);
                var stepEnvelope = sine * sine;
                var motionPhase = direction > 0
                    ? phase : Mathf.Repeat(phase + .5f, 1f);
                walk.SampleAnimation(animator.gameObject,
                    motionPhase * walk.length);
                foreach (var pair in bones)
                {
                    var rotation = Quaternion.Slerp(idleRotations[pair.Key],
                        pair.Value.localRotation,
                        MotionAmount(pair.Key) * stepEnvelope);
                    rotation = Quaternion.AngleAxis(
                        TurnYaw(pair.Key, motionPhase, direction) * stepEnvelope,
                        Vector3.up) * rotation;
                    if (previous.TryGetValue(pair.Key, out var prior) &&
                        Quaternion.Dot(prior, rotation) < 0)
                        rotation = new Quaternion(
                            -rotation.x, -rotation.y, -rotation.z, -rotation.w);
                    previous[pair.Key] = rotation;
                    AddRotationKey(rotationCurves[pair.Key], time, rotation);
                }

                var hipsPosition = Vector3.Lerp(
                    idlePositions["Hips"], bones["Hips"].localPosition,
                    .18f * stepEnvelope);
                hipsPositionCurves[0].AddKey(time, hipsPosition.x);
                hipsPositionCurves[1].AddKey(time, hipsPosition.y);
                hipsPositionCurves[2].AddKey(time, hipsPosition.z);
            }

            foreach (var pair in bones)
            {
                var bonePath = AnimationUtility.CalculateTransformPath(
                    pair.Value, animator.transform);
                SetTransformCurves(clip, bonePath, "m_LocalRotation",
                    rotationCurves[pair.Key]);
                if (pair.Key != "Hips")
                    SetConstantPositionCurves(
                        clip, bonePath, idlePositions[pair.Key], walk.length);
            }
            var hipsPath = AnimationUtility.CalculateTransformPath(
                bones["Hips"], animator.transform);
            SetTransformCurves(clip, hipsPath, "m_LocalPosition", hipsPositionCurves);
            SetPlantCurves(clip, walk.length, direction);
            clip.frameRate = frameRate;
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            return clip;
        }

        private static float MotionAmount(string bone)
        {
            if (bone.StartsWith("UpperArm", StringComparison.Ordinal) ||
                bone.StartsWith("Forearm", StringComparison.Ordinal)) return .15f;
            if (bone == "Hips" || bone == "Spine" || bone == "Chest") return .25f;
            if (bone.StartsWith("Leg", StringComparison.Ordinal)) return .45f;
            if (bone.StartsWith("Knee", StringComparison.Ordinal)) return .55f;
            if (bone.StartsWith("Foot", StringComparison.Ordinal)) return .5f;
            return .4f;
        }

        private static float TurnYaw(string bone, float phase, float direction)
        {
            var wave = Mathf.Sin(phase * Mathf.PI * 2);
            var leftSwing = .5f - .5f * Mathf.Cos(phase * Mathf.PI * 2);
            var rightSwing = 1 - leftSwing;
            if (bone == "Hips") return direction * (3.5f + wave);
            if (bone == "Spine") return direction * (2.5f - wave * .5f);
            if (bone == "Chest") return direction * 1.5f;
            if (bone == "Leg_L") return direction * Mathf.Lerp(-1, 4.5f, leftSwing);
            if (bone == "Leg_R") return direction * Mathf.Lerp(-1, 4.5f, rightSwing);
            if (bone == "Foot_L") return direction * leftSwing * 3.5f;
            if (bone == "Foot_R") return direction * rightSwing * 3.5f;
            return 0;
        }

        private static void AddRotationKey(
            IReadOnlyList<AnimationCurve> curves, float time, Quaternion rotation)
        {
            curves[0].AddKey(time, rotation.x);
            curves[1].AddKey(time, rotation.y);
            curves[2].AddKey(time, rotation.z);
            curves[3].AddKey(time, rotation.w);
        }

        private static void SetTransformCurves(AnimationClip clip, string path,
            string property, IReadOnlyList<AnimationCurve> curves)
        {
            var axes = curves.Count == 4
                ? new[] { "x", "y", "z", "w" } : new[] { "x", "y", "z" };
            for (var index = 0; index < curves.Count; index++)
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(path, typeof(Transform),
                        property + "." + axes[index]), curves[index]);
        }

        private static void SetConstantPositionCurves(AnimationClip clip,
            string path, Vector3 position, float duration)
        {
            SetTransformCurves(clip, path, "m_LocalPosition", new[]
            {
                AnimationCurve.Linear(0, position.x, duration, position.x),
                AnimationCurve.Linear(0, position.y, duration, position.y),
                AnimationCurve.Linear(0, position.z, duration, position.z)
            });
        }

        private static void SetPlantCurves(
            AnimationClip clip, float duration, float direction)
        {
            var firstSwing = new AnimationCurve(
                new Keyframe(0, 0), new Keyframe(duration * .40f, 0),
                new Keyframe(duration * .58f, 1), new Keyframe(duration * .9f, 1),
                new Keyframe(duration, 0));
            var firstSupport = new AnimationCurve(
                new Keyframe(0, 1), new Keyframe(duration * .52f, 1),
                new Keyframe(duration * .68f, 0), new Keyframe(duration * .92f, 0),
                new Keyframe(duration, 1));
            var left = direction > 0 ? firstSwing : firstSupport;
            var right = direction > 0 ? firstSupport : firstSwing;
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("", typeof(AvatarTurnFootPlantRig),
                    "leftPlantWeight"), left);
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("", typeof(AvatarTurnFootPlantRig),
                    "rightPlantWeight"), right);
        }

        private static ChildMotion Child(Motion motion, float threshold)
            => new ChildMotion
            {
                motion = motion,
                threshold = threshold,
                timeScale = 1,
                directBlendParameter = "Blend"
            };

        private static AnimatorState GetOrAddState(
            AnimatorStateMachine machine, string name)
            => machine.states.Select(child => child.state)
                   .FirstOrDefault(state => state.name == name) ??
               machine.AddState(name);

        private static void ClearTransitions(AnimatorState state)
        {
            foreach (var transition in state.transitions.ToArray())
                state.RemoveTransition(transition);
        }

        private static void AddConditionTransition(AnimatorState source,
            AnimatorState destination, string parameter,
            AnimatorConditionMode mode, float duration, float threshold = 0)
        {
            var transition = source.AddTransition(destination);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.canTransitionToSelf = false;
            transition.AddCondition(mode, threshold, parameter);
        }

        private static void ConfigureAlternateIdle(
            AnimatorState state, AnimatorState locomotion,
            AnimatorState otherIdle, AnimatorState attention,
            AnimatorState turnLeft, AnimatorState turnRight,
            int locomotionVariant, int otherVariant)
        {
            AddConditionTransition(state, attention,
                "SocialAttention", AnimatorConditionMode.If, .2f);
            AddConditionTransition(state, turnLeft,
                "TurnStepLeft", AnimatorConditionMode.If, .14f);
            AddConditionTransition(state, turnRight,
                "TurnStepRight", AnimatorConditionMode.If, .14f);
            AddConditionTransition(state, locomotion,
                "Speed", AnimatorConditionMode.Greater, .16f, .05f);
            AddIdleVariantTransition(state, locomotion, locomotionVariant);
            AddIdleVariantTransition(state, otherIdle, otherVariant);
        }

        private static void AddIdleVariantTransition(
            AnimatorState source, AnimatorState destination, int variant)
        {
            var transition = source.AddTransition(destination);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = .2f;
            transition.canTransitionToSelf = false;
            transition.AddCondition(
                AnimatorConditionMode.Equals, variant, "IdleVariant");
            transition.AddCondition(
                AnimatorConditionMode.Less, .05f, "Speed");
            transition.AddCondition(
                AnimatorConditionMode.IfNot, 0, "SocialAttention");
        }

        private static void AddExitTransition(AnimatorState source,
            AnimatorState destination, bool socialAttention)
        {
            var transition = source.AddTransition(destination);
            transition.hasExitTime = true;
            transition.exitTime = .96f;
            transition.hasFixedDuration = true;
            transition.duration = .16f;
            transition.canTransitionToSelf = false;
            transition.AddCondition(socialAttention
                    ? AnimatorConditionMode.If
                    : AnimatorConditionMode.IfNot,
                0, "SocialAttention");
        }

        private static void EnsureFloatParameter(
            AnimatorController controller, string name)
            => EnsureParameter(controller, name,
                AnimatorControllerParameterType.Float);

        private static void EnsureParameter(AnimatorController controller,
            string name, AnimatorControllerParameterType type)
        {
            var parameter = controller.parameters.FirstOrDefault(value => value.name == name);
            if (parameter == null)
            {
                controller.AddParameter(name, type);
                return;
            }
            if (parameter.type != type)
                throw new InvalidOperationException(
                    $"Animator parameter '{name}' must be {type}.");
        }

        private static AnimationClip LoadClip(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
                throw new InvalidOperationException("Animation clip is missing: " + path);
            return clip;
        }
    }
}
