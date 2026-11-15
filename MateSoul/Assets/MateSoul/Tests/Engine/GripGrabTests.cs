using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MateSoul.Editor;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class GripGrabTests
    {
        private GameObject root;
        private InputDevice device;
        private PhysicsMaterial material;

        [UnityTest]
        public IEnumerator ControllerGripDrivesSelectionFollowAndReleaseThroughRealRay()
        {
            yield return new EnterPlayMode();
            yield return ExerciseController(false);
        }

        [UnityTest]
        public IEnumerator DigitalGripWithNoAnalogValueStillGrabsAndReleases()
        {
            yield return new EnterPlayMode();
            yield return ExerciseController(true);
        }

        private IEnumerator ExerciseController(bool digitalGrip)
        {
            root = new GameObject("Grip regression");
            material = new PhysicsMaterial("Grip support");
            var floor = new GameObject("Floor", typeof(BoxCollider));
            floor.transform.SetParent(root.transform);
            floor.transform.position = Vector3.down * .1f;
            floor.GetComponent<BoxCollider>().size = new Vector3(20, .2f, 20);
            InputSystem.RegisterLayout(@"{
                ""name"":""GripRegressionController"",""extend"":""XRController"",
                ""controls"":[
                    {""name"":""grip"",""layout"":""Axis""},
                    {""name"":""gripPressed"",""layout"":""Button"",""aliases"":[""GripButton"",""GripPress""]},
                    {""name"":""trigger"",""layout"":""Axis""},
                    {""name"":""triggerPressed"",""layout"":""Button""}
                ]}");
            device = InputSystem.AddDevice("GripRegressionController");
            InputSystem.SetDeviceUsage(device, "RightHand");
            typeof(GameSceneBuilder).GetMethod("CreateControllerRays",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, new object[] { root.transform });
            root.GetComponent<PlayerInteractionModeController>().SetMode(PlayerInteractionMode.Controller);
            var controller = root.GetComponentsInChildren<ActionBasedController>()
                .Single(value => value.name == "RightHand UI Ray");
            controller.enableInputTracking = false;
            controller.transform.position = new Vector3(0, .15f, 0);
            var ray = controller.GetComponent<XRRayInteractor>();
            var grip = device.GetChildControl<AxisControl>(digitalGrip ? "gripPressed" : "grip");
            var item = new GameObject("Catalog-like body", typeof(SphereCollider));
            item.transform.SetParent(root.transform);
            item.GetComponent<SphereCollider>().radius = .15f;
            var body = item.AddComponent<PhysicsBody>();
            body.Configure(new BodySettings(2, material));
            body.Place(new Pose(new Vector3(0, .15f, 2), Quaternion.identity));
            var grab = item.AddComponent<PhysicsGrabInteractable>();
            grab.useDynamicAttach = true;
            grab.throwOnDetach = false;
            grab.interactionManager = ray.interactionManager;
            Physics.SyncTransforms();
            yield return EngineTestFrames.Seconds(.15f);
            var targets = new List<IXRInteractable>();
            ray.GetValidTargets(targets);
            Assert.That(targets, Does.Contain(grab), "The physical ray must resolve the item's registered collider.");
            QueueGrip(grip, true);
            yield return null;
            yield return null;
#region debug-point A:grip-grab-regression
            try { using (var client = new System.Net.WebClient()) client.UploadString("http://127.0.0.1:7790/event", "{\"sessionId\":\"grip-grab-regression\",\"runId\":\"post-fix\",\"hypothesisId\":\"A\",\"msg\":\"[DEBUG] input-to-selection\",\"data\":{\"digital\":" + digitalGrip.ToString().ToLowerInvariant() + ",\"raw\":" + grip.ReadValue() + ",\"select\":" + controller.selectInteractionState.active.ToString().ToLowerInvariant() + ",\"selected\":" + grab.isSelected.ToString().ToLowerInvariant() + "}}"); } catch (System.Net.WebException) { }
#endregion
            Assert.That(controller.selectInteractionState.active, Is.True,
                "Side grip must activate Select even when this device only reports a button value.");
            Assert.That(grab.isSelected, Is.True, "Selection must survive the next Dynamic pass.");
            Assert.That(body.IsFollowing, Is.True);
            var start = body.Pose.position;
            controller.transform.position += Vector3.up * .5f;
            yield return EngineTestFrames.Seconds(.4f);
            Assert.That(grab.isSelected, Is.True);
            Assert.That(body.Pose.position.y, Is.GreaterThan(start.y + .3f));
            Assert.That(body.Rigidbody.isKinematic, Is.False);
            QueueGrip(grip, false);
            yield return null;
            yield return null;
            Assert.That(grab.isSelected || body.IsFollowing, Is.False);
            var released = body.Pose.position;
            yield return EngineTestFrames.Seconds(.3f);
            Assert.That(body.Pose.position.y, Is.LessThan(released.y));
        }

        private static void QueueGrip(AxisControl grip, bool pressed)
        {
            if (grip is ButtonControl)
                InputSystem.QueueDeltaStateEvent(grip, pressed);
            else
                InputSystem.QueueDeltaStateEvent(grip, pressed ? 1f : 0f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(root);
            Object.Destroy(material);
            if (device != null) InputSystem.RemoveDevice(device);
            InputSystem.RemoveLayout("GripRegressionController");
            yield return null;
            yield return new ExitPlayMode();
        }
    }
}
