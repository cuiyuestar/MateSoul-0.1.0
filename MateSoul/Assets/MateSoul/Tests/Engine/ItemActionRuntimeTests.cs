using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MateSoul.Core;
using MateSoul.Editor;
using MateSoul.Gameplay;
using MateSoul.RoomNavigation;
using MateSoul.Runtime;
using MateSoul.Wardrobe;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class ItemActionRuntimeTests
    {
        private GameObject root, character;
        private GameSession session;
        private AvatarSocialAttentionRig socialAttention;
        private Store store;
        private sealed class Store : IGameSaveStore
        {
            public GameSaveData Data = new GameSaveData();
            public GameSaveData Load() => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(Data));
            public void Save(GameSaveData value)
                => Data = JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(value));
        }
        private sealed class Anchors : ISpatialAnchorService
        {
            public Pose Pose;
            public Task<string> CreateAsync(Pose pose, CancellationToken token)
            { Pose = pose; return Task.FromResult("test-room"); }
            public Task<Pose> LoadAsync(string id, CancellationToken token) => Task.FromResult(Pose);
            public void Dispose() { }
        }

        private sealed class FilteredNavigation : INavigationService
        {
            private readonly INavigationService inner;
            private readonly Func<Vector3, bool> allowed;
            public FilteredNavigation(
                INavigationService inner,
                Func<Vector3, bool> allowed)
            {
                this.inner = inner;
                this.allowed = allowed;
            }
            public float FloorHeight => inner.FloorHeight;
            public bool TryPath(
                Vector3 origin, Vector3 destination,
                out Vector3[] corners)
            {
                if (allowed(destination))
                    return inner.TryPath(
                        origin, destination, out corners);
                corners = null;
                return false;
            }
            public bool TrySample(
                Vector3 point, float distance, out Vector3 result)
            {
                if (allowed(point))
                    return inner.TrySample(
                        point, distance, out result);
                result = default;
                return false;
            }
            public bool TryRandom(
                Vector3 origin, System.Random random,
                out Vector3 result)
                => inner.TryRandom(origin, random, out result);
        }

        private IEnumerator SetUp(
            bool placeItems,
            Func<INavigationService, INavigationService> navigation = null)
        {
            Time.timeScale = 2;
            root = new GameObject("Item action test room");
            var room = root.AddComponent<RuntimeRoomNavigation>();
            room.Build(new RoomMapSnapshot("item-actions", "item-actions", new[]
            {
                new RoomFloorRegion("floor", new[]
                {
                    new Vector3(-5,0,-5), new Vector3(-5,0,5),
                    new Vector3(5,0,5), new Vector3(5,0,-5)
                })
            }, Array.Empty<RoomObstacle>()), false);
            character = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GameSceneBuilder.CharacterPath));
            var motor = character.GetComponent<AvatarMotor>();
            motor.Bind(character.GetComponentInChildren<Animator>());
            var catalog = new[] { "Apple", "Knife", "Plate", "Table", "AppleQuarter" }
                .Select(name => AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    "Assets/MateSoul/Content/Props/" + name + ".asset")).ToArray();
            Assert.That(catalog, Has.None.Null, "Build the action content before testing.");
            store = new Store();
            var itemRoot = new GameObject("Items").transform;
            itemRoot.SetParent(root.transform);
            INavigationService navigationService =
                new RoomNavigationService(room);
            if (navigation != null)
                navigationService = navigation(navigationService);
            session = new GameSession(motor, navigationService,
                character.GetComponentInChildren<LengyueWardrobeController>(), catalog, itemRoot,
                store, new Anchors(), new HttpLLMProvider(null),
                CharacterPersonaLoader.Load(AssetDatabase.LoadAssetAtPath<TextAsset>(GameSceneBuilder.PersonaPath)));
            var animator = character.GetComponentInChildren<Animator>();
            socialAttention = AvatarSocialAttentionRig.Attach(
                character, animator);
            var playerHead = new GameObject("Test player head").transform;
            playerHead.SetParent(root.transform);
            playerHead.position = new Vector3(100, 1.7f, 100);
            session.ConfigureSocialAttention(
                socialAttention, playerHead);
            session.Autonomy.Enabled = false;
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;
            Assert.That(session.ItemsReady, Is.True);
            if (!placeItems) yield break;
            var ids = new[] { "table", "plate", "apple", "knife" };
            var positions = new[] { new Vector3(0, 0, 1.6f), new Vector3(-.8f, 0, .3f),
                new Vector3(.8f, 0, .3f), new Vector3(-.7f, 0, -.5f) };
            for (var i = 0; i < ids.Length; i++)
            {
                var place = session.PlaceItemAsync(ids[i], new Pose(positions[i], Quaternion.identity));
                while (!place.IsCompleted) yield return null;
                Assert.That(session.ItemViews.Any(view => view.Definition.Id == ids[i]), Is.True, session.Notice);
            }
            for (var elapsed = 0f; elapsed < .5f; elapsed += Time.deltaTime) yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            session?.Dispose();
            Object.Destroy(character);
            Object.Destroy(root);
            Time.timeScale = 1;
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator MissingItemsOnlyReportSystemHintAndAcquireNoControl()
        {
            yield return new EnterPlayMode();
            yield return SetUp(false);
            var action = session.Submit(new AvatarIntent(CutFruitAction.Id, IntentSource.UI));
            Assert.That(action.State, Is.EqualTo(ActionState.Rejected));
            Assert.That(session.Notice, Does.Contain("苹果").And.Contain("刀").And.Contain("盘子").And.Contain("餐桌"));
            Assert.That(session.Avatar.Leases.Count, Is.Zero);
            Assert.That(session.Items.Items.Any(item => item.IsOccupied), Is.False);
        }

        [UnityTest]
        public IEnumerator CutActionAnnouncesOnceAndPassiveFocusStaysSilent()
        {
            yield return new EnterPlayMode();
            yield return SetUp(true);
            session.Notifications.Clear();

            var action = session.Submit(
                new AvatarIntent(CutFruitAction.Id, IntentSource.UI));
            Assert.That(action.State, Is.EqualTo(ActionState.Running), action.Error);
            Assert.That(session.Notifications.Hints, Has.Count.EqualTo(1));
            Assert.That(session.Notifications.Hint,
                Is.EqualTo("冷月正在切水果。"));

            var reserved = session.ItemViews.First(view => view.IsReserved);
            reserved.SetFocused(true);
            Assert.That(session.Notifications.Hints, Has.Count.EqualTo(1),
                "Passive focus must not announce character ownership.");

            reserved.ReportUnavailable();
            Assert.That(session.Notifications.Hints, Has.Count.EqualTo(2));
            Assert.That(session.Notifications.Hint,
                Is.EqualTo($"冷月正在使用{reserved.Definition.DisplayName}。"));

            session.CancelItemAction();
            session.Tick(.02f);
        }

        [UnityTest]
        public IEnumerator CancellationAndPauseReleaseEveryItemAndRestorePhysics()
        {
            yield return new EnterPlayMode();
            yield return SetUp(true);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var action = session.Submit(new AvatarIntent(CutFruitAction.Id, IntentSource.UI));
                Assert.That(action.State, Is.EqualTo(ActionState.Running), action.Error);
                var elapsed = 0f;
                while (!session.ItemViews.Any(view => view.IsHeld) && !action.IsFinished && elapsed < 20)
                {
                    session.Tick(Time.deltaTime); elapsed += Time.deltaTime; yield return null;
                }
                Assert.That(action.IsFinished, Is.False, action.Error);
                if (attempt == 0) { session.CancelItemAction(); session.Tick(.02f); }
                else { session.SetPaused(true); session.SetPaused(false); }
                Assert.That(action.State, Is.EqualTo(ActionState.Cancelled));
                Assert.That(session.Items.Items.All(item => !item.IsOccupied), Is.True);
                Assert.That(session.ItemViews.All(view => !view.Body.IsInteractionControlled), Is.True);
                Assert.That(session.ItemViews.Where(view => !view.IsFixedAfterPlacement)
                    .All(view => !view.Body.Rigidbody.isKinematic && view.Body.Rigidbody.useGravity), Is.True);
                Assert.That(session.Avatar.Leases.Count, Is.Zero);
                for (var settling = 0f; settling < 1; settling += Time.deltaTime) yield return null;
            }
        }

        [UnityTest]
        public IEnumerator NearHandPickupSnapsAndDisablesPhysicsUntilRelease()
        {
            yield return new EnterPlayMode();
            yield return SetUp(true);
            var action = session.Submit(new AvatarIntent(CutFruitAction.Id, IntentSource.UI));
            Assert.That(action.State, Is.EqualTo(ActionState.Running), action.Error);
            var rig = character.GetComponent<AvatarItemRig>();
            var contactError = typeof(AvatarItemRig).GetField(
                "<ContactError>k__BackingField",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assert.That(contactError, Is.Not.Null);

            GameItem held = null;
            for (var elapsed = 0f; elapsed < 20 && held == null && !action.IsFinished;
                 elapsed += Time.deltaTime)
            {
                contactError.SetValue(rig, .34f);
                session.Tick(Time.deltaTime);
                held = session.ItemViews.SingleOrDefault(view =>
                    view.Instance.ControlState == ItemControlState.HeldByCharacter);
                yield return null;
            }

            Assert.That(action.State, Is.EqualTo(ActionState.Running), action.Error);
            Assert.That(held, Is.Not.Null, action.Error);
            Assert.That(.34f, Is.GreaterThan(
                ItemTransferStep.MaximumPickupSnapDistance));
            Assert.That(.34f, Is.LessThan(
                ItemTransferStep.FallbackPickupSnapDistance));
            Assert.That(ItemTransferStep.CanSnapPickup(
                ItemTransferStep.FallbackPickupSnapDistance + .001f), Is.False);
            Assert.That(ItemTransferStep.CanSnapPickup(.2f, .46f), Is.False);
            Assert.That(held.Body.IsInteractionControlled, Is.True);
            Assert.That(held.Body.Rigidbody.isKinematic, Is.True);
            Assert.That(held.Body.Rigidbody.useGravity, Is.False);
            Assert.That(held.Body.Rigidbody.detectCollisions, Is.False);
            Assert.That(held.Body.Rigidbody.interpolation,
                Is.EqualTo(RigidbodyInterpolation.None));
            Assert.That(held.Definition.Interaction.gripKind,
                Is.EqualTo(ItemGripKind.ThinEdge));

            var followSamples = 0;
            var maximumGripError = 0f;
            var maximumFingerError = 0f;
            var minimumFingerClearance = float.PositiveInfinity;
            for (var elapsed = 0f; elapsed < 1.2f &&
                 held.Instance.ControlState == ItemControlState.HeldByCharacter;
                 elapsed += Time.deltaTime)
            {
                session.Tick(Time.deltaTime);
                yield return null;
                var itemGrip = held.Definition.Interaction.WorldGrip(held.Body.Pose);
                maximumGripError = Mathf.Max(maximumGripError,
                    Vector3.Distance(itemGrip.position, rig.HandGrip.position));
                maximumFingerError = Mathf.Max(
                    maximumFingerError, rig.FingerPoseError);
                minimumFingerClearance = Mathf.Min(
                    minimumFingerClearance, rig.MinimumFingerClearance);
                Assert.That(rig.ElbowAngle,
                    Is.InRange(34.9f, 175.1f));
                Assert.That(rig.WristCorrection,
                    Is.LessThanOrEqualTo(55.01f));
                Assert.That(rig.HeldAlignmentError,
                    Is.LessThan(.0001f));
                Assert.That(Vector3.Distance(
                    held.transform.position, held.Body.Rigidbody.position),
                    Is.LessThan(.0001f));
                followSamples++;
            }
            Assert.That(followSamples, Is.GreaterThan(3));
            Assert.That(maximumGripError, Is.LessThan(.001f));
            Assert.That(maximumFingerError, Is.LessThan(.04f));
            Assert.That(minimumFingerClearance, Is.GreaterThan(-.005f));

            session.CancelItemAction();
            session.Tick(.02f);
            Assert.That(action.State, Is.EqualTo(ActionState.Cancelled));
            Assert.That(held.Body.IsInteractionControlled, Is.False);
            Assert.That(held.Body.Rigidbody.isKinematic, Is.False);
            Assert.That(held.Body.Rigidbody.useGravity, Is.True);
            Assert.That(held.Body.Rigidbody.detectCollisions, Is.True);
            Assert.That(held.Body.Rigidbody.interpolation,
                Is.EqualTo(RigidbodyInterpolation.Interpolate));
        }

        [UnityTest]
        public IEnumerator StartingCutActionKeepsFixedWorkspaceCollisionEnabled()
        {
            yield return new EnterPlayMode();
            yield return SetUp(true);
            var table = session.ItemViews.Single(view =>
                view.Definition.Id == "table");
            Assert.That(table.Body.Rigidbody.detectCollisions, Is.True);

            var action = session.Submit(
                new AvatarIntent(CutFruitAction.Id, IntentSource.UI));
            Assert.That(action.State, Is.EqualTo(ActionState.Running), action.Error);

            Assert.That(table.Body.Rigidbody.detectCollisions, Is.True,
                "Reserving a fixed workspace must not apply held-item collision policy.");
            Assert.That(table.Body.IsInteractionControlled, Is.False);
            session.CancelItemAction();
            session.Tick(.02f);
        }

        [UnityTest]
        public IEnumerator StagedInputsSkipTransfersAndStartWithKnifeWork()
        {
            yield return new EnterPlayMode();
            yield return SetUp(true);
            var table = session.ItemViews.Single(view =>
                view.Definition.Id == "table");
            var workspace = table.Definition.Interaction;
            Pose At(Vector3 local) => new Pose(
                table.transform.TransformPoint(local),
                table.Body.Pose.rotation);
            var plate = session.ItemViews.Single(view =>
                view.Definition.Id == "plate");
            var apple = session.ItemViews.Single(view =>
                view.Definition.Id == "apple");
            var knife = session.ItemViews.Single(view =>
                view.Definition.Id == "knife");
            SetBodyPose(plate, At(workspace.platePosition));
            SetBodyPose(apple, At(workspace.workPosition));
            SetBodyPose(knife, At(workspace.toolPosition));
            Physics.SyncTransforms();

            var action = session.Submit(
                new AvatarIntent(CutFruitAction.Id, IntentSource.UI));
            Assert.That(action.State, Is.EqualTo(ActionState.Running),
                action.Error);
            var phase = session.Avatar.Capture().Single(snapshot =>
                snapshot.action_id == CutFruitAction.Id).phase;
            Assert.That(phase, Is.EqualTo("切开苹果"));
            session.CancelItemAction();
            session.Tick(.02f);
        }

#region debug-point F:power-grip-visual
        [UnityTest]
        public IEnumerator CaptureKnifePowerGripVisualEvidence()
        {
            yield return new EnterPlayMode();
            yield return SetUp(true);
            var table = session.ItemViews.Single(view =>
                view.Definition.Id == "table");
            var workspace = table.Definition.Interaction;
            Pose At(Vector3 local) => new Pose(
                table.transform.TransformPoint(local),
                table.Body.Pose.rotation);
            SetBodyPose(session.ItemViews.Single(view =>
                view.Definition.Id == "plate"),
                At(workspace.platePosition));
            SetBodyPose(session.ItemViews.Single(view =>
                view.Definition.Id == "apple"),
                At(workspace.workPosition));
            SetBodyPose(session.ItemViews.Single(view =>
                view.Definition.Id == "knife"),
                At(workspace.toolPosition));
            Physics.SyncTransforms();

            var action = session.Submit(
                new AvatarIntent(CutFruitAction.Id, IntentSource.UI));
            var rig = character.GetComponent<AvatarItemRig>();
            var heldFrames = 0;
            for (var elapsed = 0f; elapsed < 30f && heldFrames < 3;
                 elapsed += Time.deltaTime)
            {
                session.Tick(Time.deltaTime);
                yield return null;
                var held = session.ItemViews.FirstOrDefault(view =>
                    view.Definition.Id == "knife" &&
                    view.Instance.ControlState ==
                    ItemControlState.HeldByCharacter);
                if (held == null) continue;
                heldFrames++;
                if (heldFrames == 3)
                    CaptureGripEvidence(rig, held);
            }
            Assert.That(heldFrames, Is.EqualTo(3));
            session.CancelItemAction();
            session.Tick(.02f);
        }
#endregion

        [UnityTest]
        public IEnumerator BlockedDefaultLongEdgeUsesOppositeTableSide()
        {
            yield return new EnterPlayMode();
            yield return SetUp(true, inner =>
                new FilteredNavigation(
                    inner,
                    point => point.z < .85f || point.z > 1.1f));
            var table = session.ItemViews.Single(view =>
                view.Definition.Id == "table");
            var workspace = table.Definition.Interaction;
            Pose Opposite(Vector3 authored)
            {
                var mirrored = new Vector3(
                    -authored.x, authored.y, -authored.z);
                return new Pose(
                    table.transform.TransformPoint(mirrored),
                    table.Body.Pose.rotation *
                    Quaternion.Euler(0, 180, 0));
            }
            SetBodyPose(
                session.ItemViews.Single(view =>
                    view.Definition.Id == "plate"),
                Opposite(workspace.platePosition));
            SetBodyPose(
                session.ItemViews.Single(view =>
                    view.Definition.Id == "apple"),
                Opposite(workspace.workPosition));
            SetBodyPose(
                session.ItemViews.Single(view =>
                    view.Definition.Id == "knife"),
                Opposite(workspace.toolPosition));
            Physics.SyncTransforms();

            var action = session.Submit(
                new AvatarIntent(CutFruitAction.Id, IntentSource.UI));
            Assert.That(action.State, Is.EqualTo(ActionState.Running),
                action.Error);
            var phase = session.Avatar.Capture().Single(snapshot =>
                snapshot.action_id == CutFruitAction.Id).phase;
            Assert.That(phase, Is.EqualTo("切开苹果"),
                "The opposite edge must use its mirrored workspace.");
            session.CancelItemAction();
            session.Tick(.02f);
        }

        [UnityTest]
        public IEnumerator StationaryItemWorkUsesIdle03AndTracksCurrentItem()
        {
            yield return new EnterPlayMode();
            yield return SetUp(true);
            var action = session.Submit(
                new AvatarIntent(CutFruitAction.Id, IntentSource.UI));
            Assert.That(action.State, Is.EqualTo(ActionState.Running),
                action.Error);
            var motor = (AvatarMotor)session.Motor;
            var animator = character.GetComponentInChildren<Animator>();
            var sawWalkingWithoutAttention = false;
            var sawTrackedItem = false;
            var sawIdle03State = false;
            for (var elapsed = 0f; elapsed < 20 &&
                 !sawIdle03State && !action.IsFinished;
                 elapsed += Time.deltaTime)
            {
                session.Tick(Time.deltaTime);
                yield return null;
                if (motor.IsMoving && !motor.SocialAttentionEnabled)
                    sawWalkingWithoutAttention = true;
                if (!motor.SocialAttentionEnabled) continue;
                Assert.That(socialAttention.IsTracking, Is.True);
                Assert.That(socialAttention.Target, Is.Not.Null);
                sawTrackedItem = session.ItemViews.Any(view =>
                    view.transform == socialAttention.Target);
                sawIdle03State |= animator.GetCurrentAnimatorStateInfo(0)
                    .IsName("SocialAttention");
            }
            Assert.That(sawWalkingWithoutAttention, Is.True);
            Assert.That(sawTrackedItem, Is.True);
            Assert.That(sawIdle03State, Is.True,
                "Stopped item work must use the Idle03 body-only state.");

            session.CancelItemAction();
            session.Tick(.02f);
            Assert.That(motor.SocialAttentionEnabled, Is.False);
            Assert.That(socialAttention.IsTracking, Is.False);
        }

        [UnityTest]
        public IEnumerator RearTargetYawWrapDoesNotFlipHeadSide()
        {
            yield return new EnterPlayMode();
            yield return SetUp(false);
            var animator = character.GetComponentInChildren<Animator>();
            var bones = animator.GetComponentsInChildren<Transform>(true);
            var leftEye = bones.Single(value => value.name == "Eye_L");
            var rightEye = bones.Single(value => value.name == "Eye_R");
            var target = new GameObject("Rear seam attention target").transform;
            target.SetParent(root.transform);

            Vector3 RearTarget(float side)
            {
                var origin = (leftEye.position + rightEye.position) * .5f;
                return origin + character.transform.TransformDirection(
                    new Vector3(side, 0, -1));
            }

            target.position = RearTarget(.01f);
            socialAttention.Begin(target);
            for (var frame = 0; frame < 20; frame++)
                yield return null;

            var settledSign = Mathf.Sign(socialAttention.CurrentHeadYaw);
            Assert.That(Mathf.Abs(socialAttention.CurrentHeadYaw),
                Is.GreaterThan(20f));
            var maximumYawStep = 0f;
            for (var frame = 0; frame < 12; frame++)
            {
                target.position = RearTarget(
                    frame % 2 == 0 ? -.01f : .01f);
                var before = socialAttention.CurrentHeadYaw;
                yield return null;
                maximumYawStep = Mathf.Max(maximumYawStep,
                    Mathf.Abs(socialAttention.CurrentHeadYaw - before));
                Assert.That(Mathf.Sign(socialAttention.CurrentHeadYaw),
                    Is.EqualTo(settledSign),
                    "A target crossing the rear yaw seam must keep the selected head side.");
            }
            Assert.That(maximumYawStep, Is.LessThanOrEqualTo(8f),
                "Head yaw must remain angular-rate limited.");
            socialAttention.End(true);
        }

        [UnityTest]
        public IEnumerator ReachPostureSamplesFloorLowTableAndHighTargets()
        {
            yield return new EnterPlayMode();
            yield return SetUp(false);
            var rig = character.GetComponent<AvatarItemRig>();
            var heights = new[] { .12f, .48f, .82f, 1.12f };
            var leans = new float[heights.Length];
            var torsoLeans = new float[heights.Length];
            var crouches = new float[heights.Length];
            var headPitch = new float[heights.Length];
            var leftKnees = new float[heights.Length];
            var rightKnees = new float[heights.Length];
            var footErrors = new float[heights.Length];
            var contactErrors = new float[heights.Length];
            for (var sample = 0; sample < heights.Length; sample++)
            {
                var run = Guid.NewGuid();
                rig.Begin(run);
                var height = heights[sample];
                var target = new Pose(
                    session.Motor.Position +
                    character.transform.rotation *
                    new Vector3(.2f, height, .4f),
                    character.transform.rotation);
                for (var frame = 0; frame < 8; frame++)
                {
                    rig.Motion(ItemMotion.Reach, 1);
                    rig.Reach(target, 1);
                    yield return null;
                }
                leans[sample] = rig.PostureLeanDegrees;
                torsoLeans[sample] = rig.PostureTorsoLeanDegrees;
                crouches[sample] = rig.PostureCrouchDepth;
                headPitch[sample] = rig.PostureHeadPitch;
                leftKnees[sample] = rig.LeftKneeAngle;
                rightKnees[sample] = rig.RightKneeAngle;
                footErrors[sample] = rig.MaximumFootPlantError;
                contactErrors[sample] = rig.ContactError;
                rig.End(run);
                yield return null;
            }
            Assert.That(leans[0], Is.GreaterThan(leans[1]));
            Assert.That(leans[1], Is.GreaterThan(leans[2]));
            Assert.That(leans[2], Is.GreaterThan(leans[3]));
            Assert.That(crouches[0], Is.GreaterThanOrEqualTo(crouches[1] - .01f));
            Assert.That(crouches[1], Is.GreaterThan(crouches[2]));
            Assert.That(crouches[2], Is.GreaterThanOrEqualTo(crouches[3]));
            Assert.That(leans[3], Is.LessThanOrEqualTo(3.1f),
                "High targets should remain arm-dominant.");
            Assert.That(crouches[0], Is.InRange(.40f, .48f));
            Assert.That(leans[0], Is.InRange(48f, 53f));
            Assert.That(torsoLeans[0], Is.GreaterThanOrEqualTo(34f),
                "Floor pickup must visibly combine waist flexion with the crouch.");
            Assert.That(headPitch.All(value => value <= 11f), Is.True);
            Assert.That(leftKnees[0], Is.InRange(25f, 150f));
            Assert.That(rightKnees[0], Is.InRange(25f, 150f));
            Assert.That(footErrors.All(value => value < .005f), Is.True);
            Assert.That(contactErrors.All(value =>
                value <= ItemTransferStep.FallbackPickupSnapDistance), Is.True);
        }

        [UnityTest]
        public IEnumerator RealCharacterCollectsCutsAndServesWithoutDuplicatingInventory()
        {
            yield return new EnterPlayMode();
            yield return SetUp(true);
            var action = session.Submit(new AvatarIntent(CutFruitAction.Id, IntentSource.UI));
            Assert.That(action.State, Is.EqualTo(ActionState.Running), action.Error);
            var elapsed = 0f;
            var report = 0f;
            var itemRig = character.GetComponent<AvatarItemRig>();
            var appleGripSamples = 0;
            var minimumApplePalmClearance = float.PositiveInfinity;
            var minimumAppleFingerClearance = float.PositiveInfinity;
            var knifeGripSamples = 0;
            var minimumKnifeFingerError = float.PositiveInfinity;
            var maximumKnifeFingerError = 0f;
            var maximumKnifeFingerClearance = float.NegativeInfinity;
            var minimumKnifeMcpFlexion = float.PositiveInfinity;
            var maximumKnifeMcpSpread = 0f;
            var plateGripSamples = 0;
            var minimumPlateFingerError = float.PositiveInfinity;
            var maximumPlateFingerError = 0f;
            var releaseSamples = 0;
            var maximumReleaseSnap = 0f;
            var maximumReleaseRotation = 0f;
            var maximumHeldGripError = 0f;
            GameItem previouslyHeld = null;
            while (!action.IsFinished && elapsed < 230)
            {
                session.Tick(Time.deltaTime);
                elapsed += Time.deltaTime;
                yield return null;
                if (previouslyHeld != null &&
                    previouslyHeld.Instance.ControlState ==
                    ItemControlState.StagedForAction)
                {
                    maximumReleaseSnap = Mathf.Max(
                        maximumReleaseSnap,
                        previouslyHeld.LastInteractionReleasePositionDelta);
                    maximumReleaseRotation = Mathf.Max(
                        maximumReleaseRotation,
                        previouslyHeld.LastInteractionReleaseRotationDelta);
                    releaseSamples++;
                }
                Assert.That(session.ItemViews.Count(view => view.Instance.ControlState ==
                    ItemControlState.HeldByCharacter), Is.LessThanOrEqualTo(1));
                var held = session.ItemViews.SingleOrDefault(view =>
                    view.Instance.ControlState ==
                    ItemControlState.HeldByCharacter);
                if (held != null)
                    maximumHeldGripError = Mathf.Max(
                        maximumHeldGripError,
                        Vector3.Distance(
                            held.Definition.Interaction
                                .WorldGrip(held.Body.Pose).position,
                            itemRig.HandGrip.position));
                if (held != null && held.Definition.Id == "apple")
                {
                    minimumApplePalmClearance = Mathf.Min(
                        minimumApplePalmClearance,
                        itemRig.PalmClearance);
                    minimumAppleFingerClearance = Mathf.Min(
                        minimumAppleFingerClearance,
                        itemRig.MinimumFingerClearance);
                    appleGripSamples++;
                }
                if (held != null && held.Definition.Id == "knife")
                {
                    minimumKnifeFingerError = Mathf.Min(
                        minimumKnifeFingerError,
                        itemRig.FingerPoseError);
                    maximumKnifeFingerError = Mathf.Max(
                        maximumKnifeFingerError,
                        itemRig.FingerPoseError);
                    maximumKnifeFingerClearance = Mathf.Max(
                        maximumKnifeFingerClearance,
                        itemRig.MaximumFingerClearance);
                    minimumKnifeMcpFlexion = Mathf.Min(
                        minimumKnifeMcpFlexion,
                        itemRig.MinimumPowerGripMcpFlexion);
                    maximumKnifeMcpSpread = Mathf.Max(
                        maximumKnifeMcpSpread,
                        itemRig.MaximumPowerGripMcpSpread);
                    knifeGripSamples++;
#region debug-point F:power-grip-visual
                    if (knifeGripSamples == 3)
                        CaptureGripEvidence(itemRig, held);
#endregion
                }
                if (held != null && held.Definition.Id == "plate")
                {
                    minimumPlateFingerError = Mathf.Min(
                        minimumPlateFingerError,
                        itemRig.FingerPoseError);
                    maximumPlateFingerError = Mathf.Max(
                        maximumPlateFingerError,
                        itemRig.FingerPoseError);
                    plateGripSamples++;
                }
                if (elapsed >= report)
                {
                    report += 3;
                    Debug.Log($"[ItemActionTest] t={elapsed:F1} " +
                        $"phase={session.Avatar.Capture().FirstOrDefault()?.phase} " +
                        $"root={session.Motor.Position:F3} " +
                        $"contact={itemRig.ContactError:F3}");
                }
                previouslyHeld = held;
            }
            if (action.State != ActionState.Succeeded) CaptureEvidence();
            Assert.That(action.State, Is.EqualTo(ActionState.Succeeded), action.Error);
            Assert.That(appleGripSamples, Is.GreaterThan(3));
            Assert.That(minimumApplePalmClearance,
                Is.GreaterThanOrEqualTo(-.001f));
            Assert.That(minimumAppleFingerClearance,
                Is.GreaterThanOrEqualTo(-.002f));
            Assert.That(knifeGripSamples, Is.GreaterThan(3));
            Assert.That(maximumKnifeFingerError, Is.LessThan(.045f));
            Assert.That(maximumKnifeFingerClearance, Is.LessThan(.025f));
            Assert.That(minimumKnifeMcpFlexion, Is.GreaterThan(40f),
                "Power grip MCP joints must visibly close around the handle.");
            Assert.That(maximumKnifeMcpSpread, Is.LessThanOrEqualTo(10.01f),
                "Power grip MCP joints must not twist sideways to chase a fingertip target.");
            Assert.That(maximumKnifeFingerError - minimumKnifeFingerError,
                Is.LessThan(.001f),
                "Knife power-grip joints must not switch solutions.");
            Assert.That(plateGripSamples, Is.GreaterThan(3));
            Assert.That(maximumPlateFingerError - minimumPlateFingerError,
                Is.LessThan(.001f),
                "Plate pinch joints must remain stable while held.");
            Assert.That(releaseSamples, Is.GreaterThanOrEqualTo(3));
            Assert.That(maximumReleaseSnap, Is.LessThan(.0001f),
                "Release must preserve position while restoring physics.");
            Assert.That(maximumReleaseRotation, Is.LessThan(.01f),
                "Release must not actively rotate an item after detaching it.");
            Assert.That(maximumHeldGripError, Is.LessThan(.005f),
                "A held item must remain attached to the character hand.");
            Assert.That(session.ItemViews.Count(view => view.Definition.Id == "apple"), Is.Zero);
            var pieces = session.ItemViews.Where(view => view.Definition.Id == "apple-quarter").ToArray();
            Assert.That(pieces, Has.Length.EqualTo(4));
            var plate = session.ItemViews.Single(view => view.Definition.Id == "plate");
            foreach (var piece in pieces)
            {
                var delta = piece.Body.Pose.position - plate.Body.Pose.position;
                Assert.That(Vector3.ProjectOnPlane(delta, Vector3.up).magnitude, Is.LessThan(.09f));
                Assert.That(piece.Body.HasSupport(), Is.True);
                session.RecycleItem(piece.Instance.Id);
            }
            Assert.That(session.GetRemainingQuantity("apple"), Is.EqualTo(4));
            Assert.That(store.Data.items.Any(item => item.definitionId == "apple" ||
                item.definitionId == "apple-quarter"), Is.False);
            Assert.That(session.Items.Items.All(item => !item.IsOccupied), Is.True);
            Assert.That(session.Avatar.Leases.Count, Is.Zero);
        }

        private void CaptureEvidence()
        {
            var cameraObject = new GameObject("Action Evidence Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = session.Motor.Position + new Vector3(-1.4f, 1.7f, -1.8f);
            camera.transform.LookAt(session.Motor.Position + Vector3.up * .7f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.gray;
            var light = new GameObject("Evidence light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(45, -30, 0);
            var target = new RenderTexture(768, 768, 24);
            camera.targetTexture = target;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(768, 768, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 768, 768), 0, 0);
            image.Apply();
            Directory.CreateDirectory("../evidence");
            File.WriteAllBytes("../evidence/failure.png", image.EncodeToPNG());
            RenderTexture.active = previous;
            camera.targetTexture = null;
            Object.Destroy(image); Object.Destroy(target);
            Object.Destroy(light.gameObject); Object.Destroy(cameraObject);
        }

#region debug-point F:power-grip-visual
        private static void CaptureGripEvidence(
            AvatarItemRig rig, GameItem item)
        {
            var grip = rig.HandGrip;
            var center = item.Definition.Interaction
                .WorldGripShapeCenter(grip) + grip.rotation *
                new Vector3(0, 0, .012f);
            var cameraObject = new GameObject("Grip Evidence Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = .12f;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 2f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.18f, .2f, .22f);
            var light = new GameObject("Grip Evidence Light")
                .AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.transform.rotation = Quaternion.LookRotation(
                -camera.transform.forward + camera.transform.up * .4f);
            var target = new RenderTexture(1024, 1024, 24);
            camera.targetTexture = target;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(
                1024, 1024, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
            image.Apply();
            var directory = Path.GetFullPath(
                "../.unity-tmp/knife-handle-grip");
            Directory.CreateDirectory(directory);
            var directions = new[] {
                (center - (rig.transform.position + Vector3.up * .8f))
                    .normalized,
                ((center - (rig.transform.position + Vector3.up * .8f))
                    .normalized + rig.transform.forward * .7f).normalized,
                ((center - (rig.transform.position + Vector3.up * .8f))
                    .normalized - rig.transform.forward * .7f).normalized,
                ((center - (rig.transform.position + Vector3.up * .8f))
                    .normalized + Vector3.up * .55f).normalized
            };
            var names = new[] { "outside", "outside-front",
                "outside-back", "outside-high" };
            for (var view = 0; view < directions.Length; view++)
            {
                camera.transform.position = center +
                    directions[view] * .45f + Vector3.up * .03f;
                camera.transform.LookAt(center, Vector3.up);
                camera.Render();
                image.ReadPixels(
                    new Rect(0, 0, 1024, 1024), 0, 0);
                image.Apply();
                File.WriteAllBytes(
                    Path.Combine(directory,
                        $"power-grip-post-fix-{names[view]}.png"),
                    image.EncodeToPNG());
            }
            RenderTexture.active = previous;
            camera.targetTexture = null;
            Object.Destroy(image);
            Object.Destroy(target);
            Object.Destroy(light.gameObject);
            Object.Destroy(cameraObject);
        }
#endregion

        private static void SetBodyPose(GameItem item, Pose pose)
        {
            item.Body.Rigidbody.position = pose.position;
            item.Body.Rigidbody.rotation = pose.rotation;
            item.Body.Rigidbody.velocity = Vector3.zero;
            item.Body.Rigidbody.angularVelocity = Vector3.zero;
        }
    }
}
