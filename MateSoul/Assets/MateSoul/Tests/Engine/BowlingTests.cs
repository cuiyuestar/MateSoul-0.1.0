using System.Collections;
using System.Linq;
using MateSoul.Editor;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class BowlingTests
    {
        private GameObject root;

        [TearDown]
        public void TearDownImmediate()
        {
            if (!Application.isPlaying && root != null) Object.DestroyImmediate(root);
        }

        [UnityTearDown]
        public IEnumerator TearDownPlayMode()
        {
            if (root != null) Object.Destroy(root);
            yield return null;
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        [Test]
        public void RackUsesRegulationTriangleAndFortyFiveDegreeDownThreshold()
        {
            var positions = Enumerable.Range(0, 10).Select(BowlingGame.PinOffset).ToArray();
            Assert.That(positions.Distinct().Count(), Is.EqualTo(10));
            Assert.That(positions.Count(point => Mathf.Abs(point.z) < .001f), Is.EqualTo(1));
            Assert.That(positions.Count(point =>
                Mathf.Abs(point.z - BowlingGame.PinSpacing * Mathf.Sqrt(3) * .5f) < .001f), Is.EqualTo(2));
            Assert.That(positions.Count(point =>
                Mathf.Abs(point.z - BowlingGame.PinSpacing * Mathf.Sqrt(3)) < .001f), Is.EqualTo(3));
            Assert.That(positions.Count(point =>
                Mathf.Abs(point.z - BowlingGame.PinSpacing * Mathf.Sqrt(3) * 1.5f) < .001f), Is.EqualTo(4));

            root = new GameObject("Pin threshold", typeof(CapsuleCollider));
            var material = new PhysicsMaterial("Pin test");
            var body = root.AddComponent<PhysicsBody>();
            body.Configure(new BodySettings(1.55f, material));
            var pin = root.AddComponent<BowlingPin>();
            pin.Rack(new Pose(Vector3.zero, Quaternion.identity));
            body.Place(new Pose(Vector3.zero, Quaternion.Euler(44, 0, 0)));
            Assert.That(pin.IsDown, Is.False);
            body.Place(new Pose(Vector3.zero, Quaternion.Euler(46, 0, 0)));
            Assert.That(pin.IsDown, Is.True);
            Object.DestroyImmediate(material);
        }

        [Test]
        public void CompanionApproachCandidatesUseTheValidatedBehindAndSideOffsets()
        {
            var spawn = new Pose(new Vector3(2, .4f, -1), Quaternion.identity);
            var candidates = BowlingGame.CompanionApproachCandidates(
                spawn, spawn.position + Vector3.forward * 3);

            Assert.That(candidates, Has.Length.EqualTo(2));
            Assert.That(Vector3.Distance(candidates[0], spawn.position - Vector3.forward * .4f -
                Vector3.right * .7f), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(candidates[1], spawn.position - Vector3.forward * .4f +
                Vector3.right * .7f), Is.LessThan(.0001f));
        }

        [Test]
        public void BuiltContentUsesSimplifiedCollidersAndLicensedCheer()
        {
            var game = AssetDatabase.LoadAssetAtPath<GameObject>(BowlingContentBuilder.GamePath);
            var ball = AssetDatabase.LoadAssetAtPath<GameObject>(BowlingContentBuilder.BallPath);
            var pin = AssetDatabase.LoadAssetAtPath<GameObject>(BowlingContentBuilder.PinPath);
            Assert.That(game, Is.Not.Null);
            Assert.That(game.GetComponent<BowlingGame>(), Is.Not.Null);
            Assert.That(ball.GetComponent<SphereCollider>().radius,
                Is.EqualTo(BowlingGame.BallRadius).Within(.0001f));
            Assert.That(ball.GetComponent<PhysicsBody>().Rigidbody.mass, Is.EqualTo(6.5f).Within(.01f));
            Assert.That(ball.GetComponent<PhysicsGrabInteractable>().SnapToInteractor, Is.True);
            Assert.That(pin.GetComponentsInChildren<Collider>(), Has.Length.EqualTo(3));
            Assert.That(pin.GetComponentsInChildren<MeshCollider>(), Is.Empty);
            Assert.That(pin.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .Any(material => material != null && material.shader != null && material.renderQueue < 3000),
                Is.True, "Placement markers must clone a material referenced by the pin prefab.");
            Assert.That(pin.GetComponent<Rigidbody>().centerOfMass.y, Is.EqualTo(.145f).Within(.001f));
            var source = AssetDatabase.GetDependencies(BowlingContentBuilder.GamePath);
            Assert.That(source.Any(path => path.EndsWith("lengyue_audio_crowd_cheer.mp3")),
                Is.True);
        }

        [Test]
        public void CanOccupyHonorsIgnoredColliderPairs()
        {
            root = new GameObject("Ignored collision occupancy");
            var material = new PhysicsMaterial("Ignored collision occupancy");
            var bodyObject = new GameObject("lengyue_body_root", typeof(BoxCollider));
            bodyObject.transform.SetParent(root.transform);
            var body = bodyObject.AddComponent<PhysicsBody>();
            body.Configure(new BodySettings(1, material));
            var pose = new Pose(new Vector3(200, 200, 200), Quaternion.identity);
            body.Place(pose);
            var blocker = new GameObject("Ignored blocker", typeof(BoxCollider));
            blocker.transform.SetParent(root.transform);
            blocker.transform.position = pose.position;
            Physics.SyncTransforms();

            Assert.That(body.CanOccupy(pose), Is.False);
            Physics.IgnoreCollision(bodyObject.GetComponent<Collider>(), blocker.GetComponent<Collider>(), true);
            Assert.That(body.CanOccupy(pose), Is.True);
            Object.DestroyImmediate(material);
        }

        [UnityTest]
        public IEnumerator RackLocksPinPhysicsForOneSecond()
        {
            yield return new EnterPlayMode();
            root = new GameObject("Pin placement lock", typeof(CapsuleCollider));
            var material = new PhysicsMaterial("Pin placement lock");
            var body = root.AddComponent<PhysicsBody>();
            body.Configure(new BodySettings(1.55f, material));
            var pin = root.AddComponent<BowlingPin>();

            pin.Rack(new Pose(Vector3.zero, Quaternion.identity));
            Assert.That(pin.IsPlacementLocked, Is.True);
            Assert.That(body.IsContact, Is.True);
            Assert.That(body.Rigidbody.isKinematic, Is.True);
            yield return EngineTestFrames.Seconds(.8f);
            Assert.That(pin.IsPlacementLocked, Is.True);
            Assert.That(body.Rigidbody.isKinematic, Is.True);
            yield return EngineTestFrames.Seconds(.3f);
            Assert.That(pin.IsPlacementLocked, Is.False);
            Assert.That(body.IsContact, Is.False);
            Assert.That(body.Rigidbody.isKinematic, Is.False);
            Object.Destroy(material);
        }

        [UnityTest]
        public IEnumerator ProductionRayInputSnapsBowlingBallToHandAndReleasesIt()
        {
            yield return new EnterPlayMode();
            yield return ExerciseRayGrab();
        }

        private IEnumerator ExerciseRayGrab()
        {
            root = new GameObject("Bowling ray input integration");
            UnityEngine.InputSystem.InputSystem.RegisterLayout(@"{
                ""name"":""BowlingSnapController"",""extend"":""XRController"",
                ""controls"":[
                    {""name"":""grip"",""layout"":""Axis""},
                    {""name"":""gripPressed"",""layout"":""Button"",""aliases"":[""GripButton"",""GripPress""]}
                ]}");
            var device = UnityEngine.InputSystem.InputSystem.AddDevice("BowlingSnapController");
            UnityEngine.InputSystem.InputSystem.SetDeviceUsage(device, "RightHand");
            typeof(GameSceneBuilder).GetMethod("CreateControllerRays",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, new object[] { root.transform });
            root.GetComponent<PlayerInteractionModeController>().SetMode(PlayerInteractionMode.Controller);
            var ray = root.transform.Find("RightHand UI Ray")
                .GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>();
            var controller = ray.GetComponent<UnityEngine.XR.Interaction.Toolkit.ActionBasedController>();
            controller.enableInputTracking = false;
            ray.transform.position = new Vector3(0, 1, 0);
            var ball = Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>(BowlingContentBuilder.BallPath), root.transform)
                .GetComponent<BowlingBall>();
            ball.Body.Place(new Pose(new Vector3(0, 1, 2), Quaternion.identity));
            ball.Body.Rigidbody.useGravity = false;
            ball.AllowPlayer(true);
            var grip = device.GetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("gripPressed");
            try
            {
                yield return null;
                Physics.SyncTransforms();
                yield return null;
                Assert.That(ray.interactablesHovered.Contains(ball.Grab), Is.True,
                    "The production ray must actually hover the distant ball.");
                UnityEngine.InputSystem.InputSystem.QueueDeltaStateEvent(grip, true);
                yield return null;
                yield return null;
                var selected = ball.Grab.isSelected;
                var distance = Vector3.Distance(ball.Body.Pose.position, ray.transform.position);
                var selectAction = controller.selectAction.action;
                var controls = string.Join(";", selectAction.controls.Select(control => control.path));
                var inputState = $"enabled={selectAction.enabled},phase={selectAction.phase}," +
                                 $"value={selectAction.ReadValue<float>()},grip={grip.isPressed}," +
                                 $"controllerActive={controller.selectInteractionState.active}," +
                                 $"controllerValue={controller.selectInteractionState.value},controls={controls}";
#region debug-point D:ray-integration
                var payload = $"{{\"sessionId\":\"bowling-controller-grab\",\"runId\":\"ray-integration\"," +
                              $"\"hypothesisId\":\"D\",\"location\":\"BowlingTests.ExerciseRayGrab\"," +
                              $"\"msg\":\"[DEBUG] production ray input and snap\",\"data\":{{" +
                              $"\"selected\":{selected.ToString().ToLowerInvariant()}," +
                              $"\"distanceToHand\":{distance.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                              $"\"forceGrab\":{ray.useForceGrab.ToString().ToLowerInvariant()}," +
                              $"\"input\":\"{inputState.Replace("\"", "'")}\"" + "}}";
                var request = UnityEngine.Networking.UnityWebRequest.Post(
                    "http://127.0.0.1:7782/event", payload, "application/json");
                request.SendWebRequest().completed += _ => request.Dispose();
#endregion
                Assert.That(selected, Is.True,
                    "InputAction -> ActionBasedController -> XRI must select the ball. " + inputState);
                Assert.That(distance, Is.LessThan(.03f), "Snap must reach the tracked hand, not the ray hit point.");
                ray.transform.position += Vector3.right * .3f;
                yield return EngineTestFrames.Seconds(.6f);
                Assert.That(Vector3.Distance(ball.Body.Pose.position, ray.transform.position), Is.LessThan(.05f));
                var released = 0;
                ball.Released += () => released++;
                UnityEngine.InputSystem.InputSystem.QueueDeltaStateEvent(grip, false);
                yield return null;
                yield return null;
                Assert.That(ball.Grab.isSelected, Is.False);
                Assert.That(ball.Body.IsFollowing, Is.False);
                Assert.That(released, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.InputSystem.InputSystem.RemoveDevice(device);
                UnityEngine.InputSystem.InputSystem.RemoveLayout("BowlingSnapController");
            }
        }

        [UnityTest]
        public IEnumerator ForeignBodyIsBlockedWithoutMovingPinButReleasedBallCanKnockItDown()
        {
            yield return new EnterPlayMode();
            root = new GameObject("Bowling collision test");
            var material = new PhysicsMaterial("Bowling collision test")
            { staticFriction = .5f, dynamicFriction = .35f, bounciness = .05f };
            var floor = new GameObject("Floor", typeof(BoxCollider));
            floor.transform.SetParent(root.transform);
            floor.transform.position = new Vector3(0, -.05f, 0);
            floor.GetComponent<BoxCollider>().size = new Vector3(6, .1f, 6);
            floor.GetComponent<BoxCollider>().sharedMaterial = material;

            var pinObject = Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>(BowlingContentBuilder.PinPath), root.transform);
            var pin = pinObject.GetComponent<BowlingPin>();
            pin.Rack(new Pose(Vector3.zero, Quaternion.identity));
            var ballObject = Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>(BowlingContentBuilder.BallPath), root.transform);
            var ball = ballObject.GetComponent<BowlingBall>();
            ball.Body.Place(new Pose(new Vector3(0, BowlingGame.BallRadius, -1), Quaternion.identity));
            ball.AllowPlayer(false);
            var foreign = new GameObject("Foreign object", typeof(SphereCollider));
            foreign.transform.SetParent(root.transform);
            foreign.GetComponent<SphereCollider>().radius = .12f;
            var foreignBody = foreign.AddComponent<PhysicsBody>();
            foreignBody.Configure(new BodySettings(2, material));
            foreignBody.Place(new Pose(new Vector3(0, .12f, -.8f), Quaternion.identity));
            using (var filter = new BowlingPinContacts(new[] { pin }, ball,
                       new[] { floor.GetComponent<Collider>() }))
            {
                foreignBody.AddForce(Vector3.forward * 5, ForceMode.VelocityChange);
                yield return EngineTestFrames.Seconds(.45f);
                Assert.That(filter.ProtectedColliderCount, Is.GreaterThan(0));
                Assert.That(Vector3.Distance(pin.Body.Pose.position, Vector3.zero), Is.LessThan(.025f),
                    $"Pin moved to {pin.Body.Pose.position:F3} despite its collision shield.");
                Assert.That(Quaternion.Angle(pin.Body.Pose.rotation, Quaternion.identity), Is.LessThan(5));

                Object.Destroy(foreign);
                pin.Rack(new Pose(Vector3.zero, Quaternion.identity));
                yield return EngineTestFrames.Seconds(1.05f);
                Assert.That(pin.IsPlacementLocked, Is.False);
                ball.Body.Place(new Pose(new Vector3(0, BowlingGame.BallRadius, -.8f), Quaternion.identity));
                filter.BallReleased = true;
                ball.Body.AddForce(Vector3.forward * 5, ForceMode.VelocityChange);
                for (var elapsed = 0f; elapsed < .7f; elapsed += Time.fixedDeltaTime)
                {
                    filter.Sync();
                    yield return EngineTestFrames.FixedStep();
                }
                Assert.That(pin.IsDown || Vector3.Distance(pin.Body.Pose.position, Vector3.zero) > .12f,
                    Is.True, "A released bowling ball must transfer force to the pin.");
            }
            Object.Destroy(material);
        }

        [Test]
        public void SystemNotificationsExpireWithoutOwningPresentation()
        {
            var messages = new SystemNotifications();
            messages.ShowHint("较早提示", .5f);
            messages.ShowHint("最新提示", 1f);
            messages.ShowResult("结果", 1f);
            messages.Tick(.6f);
            Assert.That(messages.Hints.Select(hint => hint.Text),
                Is.EqualTo(new[] { "最新提示" }));
            Assert.That(messages.Hint, Is.EqualTo("最新提示"));
            Assert.That(messages.Result, Is.EqualTo("结果"));
            messages.Tick(.5f);
            Assert.That(messages.Hints, Is.Empty);
            Assert.That(messages.Result, Is.Null);
        }

        [Test]
        public void SystemNotificationsUseFiveSecondDefaultAndKeepPermanentHints()
        {
            var messages = new SystemNotifications();
            messages.ShowHint("永久提示", 0);
            messages.ShowHint("默认提示");

            Assert.That(messages.Hints.Select(hint => hint.Text),
                Is.EqualTo(new[] { "默认提示", "永久提示" }));
            Assert.That(messages.Hints.Select(hint => hint.Id).Distinct().Count(), Is.EqualTo(2));

            messages.Tick(4.99f);
            Assert.That(messages.Hints, Has.Count.EqualTo(2));
            messages.Tick(.02f);
            Assert.That(messages.Hints.Select(hint => hint.Text),
                Is.EqualTo(new[] { "永久提示" }));
        }
    }
}
