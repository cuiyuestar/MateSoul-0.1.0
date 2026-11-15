using System.Linq;
using MateSoul.Editor;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class HandHoldTests
    {
        [Test]
        public void ReachUsesSoftTensionAndNeverExceedsHardLength()
        {
            var sample = HandHoldMath.ConstrainReach(Vector3.zero,
                Vector3.right * 2, 1, .8f, .95f);

            Assert.That(sample.Position.x, Is.EqualTo(.95f).Within(.0001f));
            Assert.That(sample.Extension, Is.EqualTo(1.2f).Within(.0001f));
            Assert.That(sample.Separation, Is.EqualTo(1.05f).Within(.0001f));
            Assert.That(sample.Utilization, Is.EqualTo(1));
        }

        [Test]
        public void WristConstraintProjectsCombinedSwingIntoErgonomicEnvelope()
        {
            var limits = WristRotationLimits.Comfortable;
            var desired = Quaternion.AngleAxis(100, Vector3.right) *
                          Quaternion.AngleAxis(80, Vector3.up);
            var result = HandHoldMath.ConstrainWristDelta(
                desired, Vector3.right, Vector3.up, limits);

            Assert.That(result.Utilization, Is.GreaterThan(1));
            Assert.That(result.FlexionDegrees, Is.InRange(
                -limits.extensionDegrees, limits.flexionDegrees));
            Assert.That(result.DeviationDegrees, Is.InRange(
                -limits.radialDeviationDegrees, limits.ulnarDeviationDegrees));
            Assert.That(result.TwistDegrees, Is.InRange(
                -limits.supinationDegrees, limits.pronationDegrees));
            Assert.That(Quaternion.Angle(Quaternion.identity, result.Rotation),
                Is.LessThanOrEqualTo(120));
        }

        [Test]
        public void TractionUsesDwellAndLowerReleaseThreshold()
        {
            var tuning = HandHoldTuning.Default;
            tuning.forceFilterHz = 30;
            var controller = new HandHoldTractionController();

            controller.Tick(Vector3.right * 30, Vector3.zero, Vector3.zero, .05f, tuning);
            controller.Tick(Vector3.right * 30, Vector3.zero, Vector3.zero, .05f, tuning);
            Assert.That(controller.Engaged, Is.False);
            controller.Tick(Vector3.right * 30, Vector3.zero, Vector3.zero, .05f, tuning);
            Assert.That(controller.Engaged, Is.True);
            Assert.That(controller.CommandVelocity.x, Is.GreaterThan(0));

            controller.Tick(Vector3.zero, Vector3.zero, Vector3.zero, .1f, tuning);
            Assert.That(controller.Engaged, Is.True);
            controller.Tick(Vector3.zero, Vector3.zero, Vector3.zero, .15f, tuning);
            Assert.That(controller.Engaged, Is.False);
        }

        [Test]
        public void TractionNeverExceedsConfiguredSpeedOrAcceleration()
        {
            var tuning = HandHoldTuning.Default;
            tuning.forceFilterHz = 30;
            var controller = new HandHoldTractionController();
            var previous = Vector3.zero;
            const float step = .02f;

            for (var index = 0; index < 200; index++)
            {
                var velocity = controller.Tick(Vector3.forward * 10000,
                    Vector3.forward * 5, Vector3.forward * 5, step, tuning);
                Assert.That(velocity.magnitude,
                    Is.LessThanOrEqualTo(tuning.maximumSpeed + .0001f));
                Assert.That(Vector3.Distance(previous, velocity) / step,
                    Is.LessThanOrEqualTo(tuning.maximumAcceleration + .0001f));
                previous = velocity;
            }
        }

        [Test]
        public void LeadFollowsSustainedPlayerMovementAndStopsAfterwards()
        {
            var tuning = HandHoldTuning.Default;
            var controller = new HandHoldLeadController();

            for (var index = 0; index < 10; index++)
            {
                var idle = controller.Tick(Vector3.zero, Vector3.zero, .05f, tuning);
                Assert.That(controller.Engaged, Is.False);
                Assert.That(idle.sqrMagnitude, Is.LessThan(.0001f));
            }

            var velocity = Vector3.zero;
            for (var index = 0; index < 40; index++)
            {
                velocity = controller.Tick(Vector3.forward * .6f,
                    Vector3.forward * .6f, .05f, tuning);
                Assert.That(velocity.magnitude,
                    Is.LessThanOrEqualTo(tuning.maximumSpeed + .0001f));
            }
            Assert.That(controller.Engaged, Is.True);
            Assert.That(velocity.z, Is.GreaterThan(.1f));
            // The follow command rides the player's head speed instead of ramping
            // towards it on its own acceleration budget.
            Assert.That(velocity.z, Is.EqualTo(.6f).Within(.02f));

            for (var index = 0; index < 60; index++)
                velocity = controller.Tick(Vector3.zero, Vector3.zero, .05f, tuning);
            Assert.That(controller.Engaged, Is.False);
            Assert.That(velocity.sqrMagnitude, Is.LessThan(.0001f));
        }

        [Test]
        public void LeadIgnoresHeadTurnsAndHandGestures()
        {
            var tuning = HandHoldTuning.Default;
            var controller = new HandHoldLeadController();
            Vector3 velocity = default;

            for (var index = 0; index < 40; index++)
                velocity = controller.Tick(Vector3.right * .4f,
                    Vector3.forward * .4f, .05f, tuning);
            Assert.That(controller.Engaged, Is.False);
            Assert.That(velocity.sqrMagnitude, Is.LessThan(.0001f));

            // A head turn alone must not drag the avatar along: the held hand stays
            // where it is while the head swings to the side.
            for (var index = 0; index < 40; index++)
                velocity = controller.Tick(Vector3.right * .4f,
                    Vector3.zero, .05f, tuning);
            Assert.That(controller.Engaged, Is.False);
            Assert.That(velocity.sqrMagnitude, Is.LessThan(.0001f));

            for (var index = 0; index < 40; index++)
                velocity = controller.Tick(Vector3.zero,
                    Vector3.right * .4f, .05f, tuning);
            Assert.That(controller.Engaged, Is.False);
            Assert.That(velocity.sqrMagnitude, Is.LessThan(.0001f));
        }

        [Test]
        public void LeadEngagesOnSlowWalkWithAStabilisedHand()
        {
            var tuning = HandHoldTuning.Default;
            var controller = new HandHoldLeadController();
            Vector3 velocity = default;

            // Device logs measured 0.15-0.32 m/s walking while the held hand lagged
            // behind at roughly a third of the head speed.
            for (var index = 0; index < 40; index++)
                velocity = controller.Tick(Vector3.forward * .18f,
                    Vector3.forward * .07f, .05f, tuning);
            Assert.That(controller.Engaged, Is.True);
            Assert.That(velocity.z, Is.GreaterThan(.05f));
        }

        [Test]
        public void ProductionPrefabContainsTwoConstrainedSelectableHands()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameSceneBuilder.CharacterPath);
            Assert.That(prefab, Is.Not.Null);
            var rig = prefab.GetComponent<HandIKRig>();
            Assert.That(rig, Is.Not.Null);
            Assert.That(rig.IsConfigured, Is.True);
            Assert.That(prefab.GetComponentsInChildren<HandHoldInteractable>(true).Length,
                Is.EqualTo(2));
            var animator = prefab.GetComponentInChildren<Animator>(true);
            var constraints = prefab.GetComponentsInChildren<TwoBoneIKConstraint>(true);
            Assert.That(constraints.Length, Is.EqualTo(2));
            foreach (var constraint in constraints)
            {
                Assert.That(constraint.data.target.IsChildOf(animator.transform), Is.True);
                Assert.That(constraint.data.hint.IsChildOf(animator.transform), Is.True);
            }
            foreach (var socket in prefab.GetComponentsInChildren<HandHoldInteractable>(true))
            {
                Assert.That(socket.GetComponent<SphereCollider>().isTrigger, Is.True);
                Assert.That(socket.EntryDistance,
                    Is.EqualTo(HandHoldInteractable.DefaultEntryDistance));
                Assert.That(socket.ExitDistance,
                    Is.EqualTo(HandHoldInteractable.DefaultExitDistance));
                var marker = socket.transform.Cast<Transform>()
                    .Single(child => child.name.EndsWith("Palm Target (Editor Only)"));
                Assert.That(marker.CompareTag("EditorOnly"), Is.True);
                Assert.That(marker.GetComponent<Renderer>(), Is.Not.Null);
                Assert.That(marker.GetComponent<Collider>(), Is.Null);
            }
            Assert.That(animator.GetComponent<RigBuilder>(), Is.Not.Null);
        }

        [Test]
        public void DeviceSimulatorIsEnabledOnlyForEditorPlayMode()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                HandHoldSimulatorSetup.SimulatorPrefabPath);
            var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                HandHoldSimulatorSetup.SettingsPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<XRDeviceSimulator>(), Is.Not.Null);
            Assert.That(settings, Is.Not.Null);

            var serialized = new SerializedObject(settings);
            Assert.That(serialized.FindProperty(
                "m_AutomaticallyInstantiateSimulatorPrefab").boolValue, Is.False,
                "The simulator must not register extra controllers in unrelated PlayMode tests.");
            Assert.That(serialized.FindProperty(
                "m_AutomaticallyInstantiateInEditorOnly").boolValue, Is.True);
            Assert.That(serialized.FindProperty("m_SimulatorPrefab").objectReferenceValue,
                Is.SameAs(prefab));
        }

        [Test]
        public void ControllerHoldRangeUsesTrackedControllerInsteadOfRayReach()
        {
            var root = new GameObject("Controller proximity test");
            try
            {
                root.AddComponent<XRInteractionManager>();
                var controllerObject = new GameObject("Controller");
                controllerObject.transform.SetParent(root.transform);
                var controller = controllerObject.AddComponent<ActionBasedController>();
                controller.enableInputTracking = false;
                var ray = controllerObject.AddComponent<ControllerItemRay>();
                ray.xrController = controller;
                var socket = CreateSocket(root.transform);

                controllerObject.transform.position = socket.transform.position - Vector3.forward * 2;
                controllerObject.transform.LookAt(socket.transform);
                Assert.That(socket.IsWithinHoldRange(ray), Is.False,
                    "A distant ray hit must not count as physical hand contact.");

                controllerObject.transform.position =
                    socket.transform.position - Vector3.forward * .09f;
                Assert.That(socket.IsWithinHoldRange(ray), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EyeHandHoldRangeUsesPinchPoseInsteadOfAimOrigin()
        {
            var root = new GameObject("Pinch proximity test");
            try
            {
                root.AddComponent<XRInteractionManager>();
                var aimObject = new GameObject("Hand Aim");
                aimObject.transform.SetParent(root.transform);
                var controller = aimObject.AddComponent<ActionBasedController>();
                controller.enableInputTracking = false;
                var ray = aimObject.AddComponent<HandItemRay>();
                ray.xrController = controller;
                var pinch = new GameObject("Pinch Attach").transform;
                pinch.SetParent(root.transform);
                ray.Configure(null, null, pinch);
                var socket = CreateSocket(root.transform);

                aimObject.transform.position = socket.transform.position;
                pinch.position = socket.transform.position + Vector3.right * .5f;
                Assert.That(socket.IsWithinHoldRange(ray), Is.False,
                    "The hand aim or gaze origin must not impersonate the physical pinch pose.");

                aimObject.transform.position = socket.transform.position - Vector3.forward * 2;
                pinch.position = socket.transform.position + Vector3.right * .09f;
                Assert.That(socket.IsWithinHoldRange(ray), Is.True);
                Assert.That(ray.TryGetHandHoldPose(out var pose), Is.True);
                Assert.That(pose.position, Is.EqualTo(pinch.position));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SelectedHandUsesLargerExitRange()
        {
            var root = new GameObject("Hold hysteresis test");
            try
            {
                var manager = root.AddComponent<XRInteractionManager>();
                var controllerObject = new GameObject("Controller");
                controllerObject.transform.SetParent(root.transform);
                var controller = controllerObject.AddComponent<ActionBasedController>();
                controller.enableInputTracking = false;
                var ray = controllerObject.AddComponent<ControllerItemRay>();
                ray.xrController = controller;
                ray.interactionManager = manager;
                var socket = CreateSocket(root.transform);
                socket.interactionManager = manager;
                controllerObject.transform.position =
                    socket.transform.position + Vector3.right * .09f;

                Assert.That(socket.IsWithinHoldRange(ray), Is.True);
                manager.SelectEnter((IXRSelectInteractor)ray, socket);
                Assert.That(socket.isSelected, Is.True);

                // XRI re-validates the selection every frame, so the held radius has
                // to cover the arm lagging behind the walking player; otherwise the
                // hold is cancelled before the character has a chance to follow.
                controllerObject.transform.position =
                    socket.transform.position + Vector3.right * .13f;
                Assert.That(socket.IsWithinHoldRange(ray), Is.True,
                    "Selection should survive movement between the entry and exit radii.");
                controllerObject.transform.position =
                    socket.transform.position + Vector3.right * .35f;
                Assert.That(socket.IsWithinHoldRange(ray), Is.True,
                    "A walking hold must outlast the arm lag instead of ending on the " +
                    "acquisition radius.");
                controllerObject.transform.position =
                    socket.transform.position + Vector3.right * .75f;
                Assert.That(socket.IsWithinHoldRange(ray), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static HandHoldInteractable CreateSocket(Transform parent)
        {
            var socketObject = new GameObject("Hand Socket", typeof(SphereCollider));
            socketObject.transform.SetParent(parent);
            socketObject.transform.position = new Vector3(0, 1, 1);
            var socket = socketObject.AddComponent<HandHoldInteractable>();
            socket.Configure(AvatarHandSide.Right, null);
            return socket;
        }
    }
}
