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
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class GameRuntimeTests
    {
        private static readonly string[] ProductionCatalogIds =
        {
            "table", "water-dispenser", "chair", "television", "radio",
            "plate", "plate-medium", "plate-large",
            "mug", "bowl", "bowl-medium", "bowl-large",
            "playing-cards", "spoon", "chopsticks", "knife", "clock",
            "frying-pan", "coffee-bean-jar", "coffee-grinder", "raw-steak", "cupcake",
            "cookie", "apple", "carrot", "dartboard", "bowling-ball", "bowling-pin", "plush-doll",
            "remote-control"
        };
        private GameObject root, character;
        private GameSession session;
        private Animator animator;
        private MemoryStore store;
        private AnchorMemory anchorMemory;
        private ItemDefinition lamp, table, chair, television;
        private Transform itemRoot;

        private sealed class MemoryStore : IGameSaveStore
        {
            public GameSaveData Data = new GameSaveData();
            public bool FailWrites;
            public GameSaveData Load() => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(Data));
            public void Save(GameSaveData data)
            {
                if (FailWrites) throw new IOException("Storage unavailable.");
                Data = JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(data));
            }
        }

        private sealed class ReplyProvider : ILLMProvider
        {
            public string Target;
            public Task<ConversationReply> CompleteAsync(ConversationRequest request, CancellationToken token)
                => Task.FromResult(new ConversationReply
                {
                    reply_text = "去看看这盏灯。",
                    response_emotion = "normal",
                    action = new DialogueAction { id = "visit", target_id = Target }
                });
        }

        private sealed class DeferredReplyProvider : ILLMProvider
        {
            public readonly TaskCompletionSource<ConversationReply> Completion =
                new TaskCompletionSource<ConversationReply>();
            public CancellationToken Token { get; private set; }
            public Task<ConversationReply> CompleteAsync(ConversationRequest request, CancellationToken token)
            {
                Token = token;
                return Completion.Task;
            }
        }

        private sealed class AnchorMemory
        {
            public readonly System.Collections.Generic.Dictionary<string, Pose> Poses =
                new System.Collections.Generic.Dictionary<string, Pose>();
        }

        private sealed class MemoryAnchorService : ISpatialAnchorService
        {
            private readonly AnchorMemory memory;
            public MemoryAnchorService(AnchorMemory memory) { this.memory = memory; }
            public Task<string> CreateAsync(Pose pose, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                var id = Guid.NewGuid().ToString();
                memory.Poses.Add(id, pose);
                return Task.FromResult(id);
            }
            public Task<Pose> LoadAsync(string id, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (!memory.Poses.TryGetValue(id, out var pose))
                    throw new InvalidOperationException("Anchor is unavailable.");
                return Task.FromResult(pose);
            }
            public void Dispose() { }
        }

        private sealed class UnavailableAnchorService : ISpatialAnchorService
        {
            public Task<string> CreateAsync(Pose pose, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(Guid.NewGuid().ToString());
            }
            public Task<Pose> LoadAsync(string id, CancellationToken token)
                => Task.FromException<Pose>(new InvalidOperationException("Anchor is unavailable."));
            public void Dispose() { }
        }

        private sealed class DeferredAnchorService : ISpatialAnchorService
        {
            public readonly TaskCompletionSource<string> Created = new TaskCompletionSource<string>();
            public Task<string> CreateAsync(Pose pose, CancellationToken token) => Created.Task;
            public Task<Pose> LoadAsync(string id, CancellationToken token)
                => Task.FromResult(new Pose(Vector3.zero, Quaternion.identity));
            public void Dispose() { }
        }

        private sealed class DeferredLoadAnchorService : ISpatialAnchorService
        {
            public readonly TaskCompletionSource<Pose> Loaded = new TaskCompletionSource<Pose>();
            public int LoadCalls;
            public Task<string> CreateAsync(Pose pose, CancellationToken token)
                => Task.FromResult("created");
            public Task<Pose> LoadAsync(string id, CancellationToken token)
                => ++LoadCalls == 1 ? Loaded.Task : Task.FromResult(new Pose(Vector3.zero, Quaternion.identity));
            public void Dispose() { }
        }

        // EnterPlayMode belongs to each test's outer iterator. In UTF 1.1.33 a reload
        // in UnitySetUp can restore the previous test's completed PC and skip the body.
        private IEnumerator SetUp()
        {
            // Isolated room and save; never read/write the user's gameplay save.
            root = new GameObject("Framework Test Room");
            var room = root.AddComponent<RuntimeRoomNavigation>();
            room.Build(new RoomMapSnapshot("test", "test", new[]
            {
                new RoomFloorRegion("floor", new[]
                {
                    new Vector3(-5,0,-5), new Vector3(-5,0,5),
                    new Vector3(5,0,5), new Vector3(5,0,-5)
                })
            }, Array.Empty<RoomObstacle>()), false);
            character = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GameSceneBuilder.CharacterPath));
            character.transform.position = Vector3.up * .03f;
            animator = character.GetComponentInChildren<Animator>();
            character.GetComponent<AvatarMotor>().Bind(animator);
            itemRoot = new GameObject("Test Items").transform;
            itemRoot.SetParent(root.transform);
            lamp = TestDefinition("Assets/MateSoul/Content/CompanionLamp.asset");
            table = TestDefinition("Assets/MateSoul/Content/Props/Table.asset");
            chair = TestDefinition("Assets/MateSoul/Content/Props/Chair.asset");
            television = TestDefinition("Assets/MateSoul/Content/Props/Television.asset");
            store = new MemoryStore();
            anchorMemory = new AnchorMemory();
            session = CreateSession(new HttpLLMProvider(null));
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;
            Assert.That(restore.IsFaulted, Is.False);
            yield return null;
        }

        private static ItemDefinition TestDefinition(string path)
        {
            var source = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            var clone = Object.Instantiate(source);
            clone.Configure(clone.Id, clone.DisplayName, clone.Prefab, clone.Capabilities,
                true, clone.Footprint, clone.PlacementHeight, clone.FocusMaterial,
                clone.Category);
            clone.ConfigureInventory(100);
            clone.ConfigurePlacement(false);
            return clone;
        }

        private GameSession CreateSession(ILLMProvider provider, ISpatialAnchorService anchorService = null,
            ItemDefinition[] itemDefinitions = null, Func<DateTime> localNow = null)
        {
            var created = new GameSession(character.GetComponent<AvatarMotor>(),
                new RoomNavigationService(root.GetComponent<RuntimeRoomNavigation>()),
                character.GetComponentInChildren<LengyueWardrobeController>(),
                itemDefinitions ?? new[] { lamp, table, chair, television }, itemRoot,
                store, anchorService ?? new MemoryAnchorService(anchorMemory), provider,
                CharacterPersonaLoader.Load(
                    AssetDatabase.LoadAssetAtPath<TextAsset>(GameSceneBuilder.PersonaPath)),
                localNow: localNow);
            created.Autonomy.Enabled = false;
            return created;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            session?.Dispose();
            Object.Destroy(character);
            Object.Destroy(lamp);
            Object.Destroy(table);
            Object.Destroy(chair);
            Object.Destroy(television);
            Object.Destroy(root);
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator ProductionCharacterMovesAnimatesAndStopsWithoutLeavingNavMesh()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseMovement();
        }

        private IEnumerator ExerciseMovement()
        {
            var start = session.Motor.Position;
            var target = new Vector3(2, 0, 1);
            var handle = session.Submit(new AvatarIntent("move", IntentSource.UI, payload: target));
            var maxSpeed = 0f;
            var maxWalkWeight = 0f;
            var elapsed = 0f;
            while (!handle.IsFinished && elapsed < 8)
            {
                session.Tick(Time.deltaTime);
                maxSpeed = Mathf.Max(maxSpeed, animator.GetFloat("Speed"));
                foreach (var info in animator.GetCurrentAnimatorClipInfo(0))
                    if (info.clip != null &&
                        info.clip.name == "lengyue_clip_walk_bodyonly")
                        maxWalkWeight = Mathf.Max(maxWalkWeight, info.weight);
                Assert.That(UnityEngine.AI.NavMesh.SamplePosition(session.Motor.Position,
                    out var hit, .2f, UnityEngine.AI.NavMesh.AllAreas), Is.True);
                Assert.That(Vector3.ProjectOnPlane(hit.position - session.Motor.Position, Vector3.up).magnitude, Is.LessThan(.08f));
                elapsed += Time.deltaTime;
                yield return null;
            }
            Assert.That(handle.State, Is.EqualTo(ActionState.Succeeded), handle.Error);
            Assert.That(Vector3.Distance(start, session.Motor.Position), Is.GreaterThan(1.8f));
            Assert.That(maxSpeed, Is.GreaterThan(.7f));
            Assert.That(maxWalkWeight, Is.GreaterThan(.99f));
            Assert.That(animator.GetCurrentAnimatorClipInfo(0).Length, Is.GreaterThan(0));
            Assert.That(animator.GetFloat("Speed"), Is.Zero);
            Assert.That(animator.GetCurrentAnimatorClipInfo(0).Any(info =>
                info.clip != null && info.clip.name == "lengyue_clip_idle01_combined" &&
                info.weight > .99f), Is.True);
            Assert.That(session.Avatar.Leases.Count, Is.Zero);
            Assert.That(character.GetComponent<RoomRandomWalker>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator UserPreemptsAutonomyAndPauseFreezesDomainClock()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExercisePreemption();
        }

        private IEnumerator ExercisePreemption()
        {
            var auto = session.Submit(new AvatarIntent("move", IntentSource.Autonomous, payload: new Vector3(3,0,0)));
            session.Tick(.1f);
            yield return null;
            var user = session.Submit(new AvatarIntent("stay", IntentSource.Player));
            session.Tick(.1f);
            Assert.That(auto.State, Is.EqualTo(ActionState.Cancelled));
            Assert.That(user.State, Is.EqualTo(ActionState.Running));
            session.Mood.Apply("anger", 90);
            var remaining = session.Mood.RemainingSeconds;
            var position = session.Motor.Position;
            session.SetPaused(true);
            session.Tick(100);
            yield return null;
            Assert.That(session.Motor.Position, Is.EqualTo(position));
            Assert.That(session.Mood.RemainingSeconds, Is.EqualTo(remaining));
            Assert.That(session.Avatar.Leases.Count, Is.Zero);
            session.SetPaused(false);
            session.Tick(1);
            Assert.That(session.Mood.RemainingSeconds, Is.EqualTo(remaining - 1));
        }

        [UnityTest]
        public IEnumerator DialogueAndProximityUseCoordinatedSocialAttention()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseSocialAttention();
        }

        private IEnumerator ExerciseSocialAttention()
        {
            var player = new GameObject("Social Attention Player").transform;
            player.SetParent(root.transform);
            var attentionRig = AvatarSocialAttentionRig.Attach(character, animator);
            session.ConfigureSocialAttention(attentionRig, player);
            player.position = session.Motor.Position + Vector3.forward * 3f + Vector3.up * 1.55f;
            session.Tick(.1f);
            Assert.That(session.PlayerNearby, Is.False);
            Assert.That(session.SocialAttentionActive, Is.False);

            var autonomous = session.Submit(new AvatarIntent(
                "move", IntentSource.Autonomous, payload: new Vector3(3, 0, 0)));
            session.Tick(.1f);
            player.position = session.Motor.Position + Vector3.right * 1f +
                              Vector3.up * 1.55f;
            session.Tick(.1f);
            Assert.That(session.PlayerNearby, Is.False,
                "One meter must remain outside the reduced attention entry range.");
            player.position = session.Motor.Position + Vector3.right * .72f +
                              Vector3.up * 1.55f;
            session.Tick(.1f);
            Assert.That(autonomous.State, Is.EqualTo(ActionState.Running),
                "Ambient attention must not interrupt autonomous actions.");
            Assert.That(session.PlayerNearby, Is.True);
            Assert.That(session.SocialAttentionActive, Is.False);
            session.Avatar.Cancel(autonomous);
            session.Tick(.1f);
            Assert.That(autonomous.State, Is.EqualTo(ActionState.Cancelled));
            Assert.That(session.PlayerNearby, Is.True);
            for (var elapsed = 0f;
                 !session.SocialAttentionActive && elapsed < 2f;
                 elapsed += Time.deltaTime)
            {
                session.Tick(Time.deltaTime);
                yield return null;
            }
            Assert.That(session.SocialAttentionActive, Is.True);
            Assert.That(session.Avatar.Capture().Single().action_id,
                Is.EqualTo(SocialAttentionAction.Id));
            Assert.That(character.GetComponent<AvatarMotor>().IsMoving, Is.False);
            Assert.That(character.GetComponent<AvatarMotor>().SocialAttentionEnabled,
                Is.False, "Notice must lead the dedicated social idle.");

            var initialRotation = character.transform.rotation;
            for (var elapsed = 0f; elapsed < .25f; elapsed += Time.deltaTime)
            {
                session.Tick(Time.deltaTime);
                yield return null;
            }
            Assert.That(Quaternion.Angle(initialRotation, character.transform.rotation),
                Is.LessThan(2f), "Head tracking must lead body rotation.");
            Assert.That(Mathf.Abs(attentionRig.CurrentHeadYaw), Is.GreaterThan(2f));
            Assert.That(character.GetComponent<AvatarMotor>().SocialAttentionEnabled,
                Is.False);

            for (var elapsed = 0f; elapsed < 1.25f; elapsed += Time.deltaTime)
            {
                session.Tick(Time.deltaTime);
                yield return null;
            }
            Assert.That(character.GetComponent<AvatarMotor>().SocialAttentionEnabled, Is.True);
            var turn = Quaternion.Angle(initialRotation, character.transform.rotation);
            Assert.That(turn, Is.GreaterThan(12f));
            Assert.That(turn, Is.LessThan(60f));
            Assert.That(attentionRig.Weight, Is.GreaterThan(.8f));
            Assert.That(Mathf.Abs(attentionRig.CurrentHeadYaw), Is.LessThanOrEqualTo(48.01f));
            Assert.That(Mathf.Abs(attentionRig.CurrentHeadYaw), Is.GreaterThan(2f));

            player.position = session.Motor.Position + Vector3.forward * .95f +
                              Vector3.up * 1.55f;
            session.Tick(.1f);
            Assert.That(session.PlayerNearby, Is.True,
                "Exit hysteresis should retain attention inside 1.05 meters.");
            player.position = session.Motor.Position + Vector3.forward * 1.15f +
                              Vector3.up * 1.55f;
            for (var elapsed = 0f; elapsed < .4f; elapsed += .1f)
                session.Tick(.1f);
            Assert.That(session.PlayerNearby, Is.False);
            Assert.That(session.SocialAttentionActive, Is.False);

            session.SetDialogueAttention(true);
            for (var elapsed = 0f;
                 !session.SocialAttentionActive && elapsed < 1f;
                 elapsed += .1f)
                session.Tick(.1f);
            Assert.That(session.SocialAttentionActive, Is.True);
            Assert.That(session.Avatar.HasClaim(ControlChannels.Dialogue), Is.False,
                "Dialogue attention must not claim the dialogue channel, or sends are rejected.");

            var explicitAction = session.Submit(
                new AvatarIntent("stay", IntentSource.Player));
            session.Tick(.1f);
            Assert.That(explicitAction.State, Is.EqualTo(ActionState.Running));
            Assert.That(session.SocialAttentionActive, Is.False);
            for (var elapsed = 0f; elapsed < 3.2f; elapsed += .1f)
                session.Tick(.1f);
            Assert.That(explicitAction.State, Is.EqualTo(ActionState.Succeeded));
            session.Tick(.2f);
            Assert.That(session.SocialAttentionActive, Is.True,
                "Dialogue attention should resume after the explicit action completes.");

            session.SetDialogueAttention(false);
            session.Tick(.1f);
            session.Tick(.1f);
            Assert.That(session.SocialAttentionActive, Is.False);
            for (var elapsed = 0f; elapsed < .8f; elapsed += Time.deltaTime)
                yield return null;
            Assert.That(attentionRig.Weight, Is.LessThan(.1f));
        }

        [UnityTest]
        public IEnumerator AvatarApproachDoesNotTriggerAmbientAttention()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            var player = new GameObject("Stationary Player").transform;
            player.SetParent(root.transform);
            var attentionRig = AvatarSocialAttentionRig.Attach(character, animator);
            session.ConfigureSocialAttention(attentionRig, player);
            var motor = character.GetComponent<AvatarMotor>();
            var start = motor.Position;
            player.position = start + Vector3.right * 2f + Vector3.up * 1.55f;
            session.Tick(.1f);

            for (var step = 1; step <= 8; step++)
            {
                var position = Vector3.Lerp(
                    start, start + Vector3.right * 1.3f, step / 8f);
                motor.Body.Place(new Pose(position, motor.Body.Pose.rotation));
                session.Tick(.1f);
                yield return null;
            }
            Assert.That(session.PlayerNearby, Is.True);
            for (var elapsed = 0f; elapsed < 2f; elapsed += .1f)
            {
                session.Tick(.1f);
                yield return null;
            }

            Assert.That(session.SocialAttentionCandidate, Is.False);
            Assert.That(session.SocialAttentionActive, Is.False,
                "Avatar-driven proximity must not be treated as player intent.");
        }

        [UnityTest]
        public IEnumerator BoundaryDebounceAndAmbientTimeoutPreventAttentionLoop()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            var player = new GameObject("Boundary Player").transform;
            player.SetParent(root.transform);
            var attentionRig = AvatarSocialAttentionRig.Attach(character, animator);
            session.ConfigureSocialAttention(attentionRig, player);
            var origin = session.Motor.Position;
            player.position = origin + Vector3.right * 2f + Vector3.up * 1.55f;
            session.Tick(.1f);

            for (var sample = 0; sample < 8; sample++)
            {
                var distance = sample % 2 == 0 ? .79f : .82f;
                player.position = origin + Vector3.right * distance +
                                  Vector3.up * 1.55f;
                session.Tick(.15f);
                yield return null;
            }
            Assert.That(session.SocialAttentionActive, Is.False,
                "Crossing the entry boundary must restart candidate dwell.");

            player.position = origin + Vector3.right * .72f +
                              Vector3.up * 1.55f;
            for (var elapsed = 0f;
                 !session.SocialAttentionActive && elapsed < 2f;
                 elapsed += .1f)
            {
                session.Tick(.1f);
                yield return null;
            }
            Assert.That(session.SocialAttentionActive, Is.True);

            for (var elapsed = 0f; elapsed < 6f; elapsed += .1f)
            {
                session.Tick(.1f);
                yield return null;
            }
            session.Tick(.1f);
            Assert.That(session.SocialAttentionActive, Is.False);
            for (var elapsed = 0f; elapsed < 1.5f; elapsed += .1f)
            {
                session.Tick(.1f);
                yield return null;
            }
            Assert.That(session.SocialAttentionCandidate, Is.False);
            Assert.That(session.SocialAttentionActive, Is.False,
                "Stationary proximity must not re-arm after ambient timeout.");
        }

        [UnityTest]
        public IEnumerator TransientPauseDoesNotCancelInFlightConversation()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            var provider = new DeferredReplyProvider();
            session.Dispose();
            session = CreateSession(provider);

            var pending = session.Conversation.SendAsync("你好");
            Assert.That(session.Conversation.State, Is.EqualTo(ConversationState.Thinking));
            session.SetPaused(true);
            yield return null;

            Assert.That(pending.IsCompleted, Is.False);
            Assert.That(provider.Token.IsCancellationRequested, Is.False);
            Assert.That(session.Conversation.State, Is.EqualTo(ConversationState.Thinking));

            session.SetPaused(false);
            provider.Completion.SetResult(new ConversationReply
            {
                reply_text = "我在。",
                response_emotion = "normal",
                action = new DialogueAction { id = "none" }
            });
            while (!pending.IsCompleted) yield return null;

            Assert.That(pending.IsFaulted, Is.False);
            Assert.That(session.Conversation.State, Is.EqualTo(ConversationState.Replied));
            Assert.That(session.Conversation.Text, Is.EqualTo("我在。"));
        }

        [UnityTest]
        public IEnumerator TwoItemsSaveIndependentlyAndRemovingAITargetReleasesControl()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseAITargetRemoval();
        }

        private IEnumerator ExerciseAITargetRemoval()
        {
            var first = session.PlaceItemAsync(lamp.Id, new Pose(new Vector3(2,0,2), Quaternion.identity));
            while (!first.IsCompleted) yield return null;
            var second = session.PlaceItemAsync(lamp.Id, new Pose(new Vector3(-2,0,2), Quaternion.identity));
            while (!second.IsCompleted) yield return null;
            Assert.That(session.Items.Count, Is.EqualTo(2), session.Notice);
            var items = session.ItemViews.ToArray();
            items[0].Instance.SetState("visited");
            session.Save();
            Assert.That(store.Data.items.Select(i => i.instanceId).Distinct().Count(), Is.EqualTo(2));
            Assert.That(store.Data.items.Count(i => i.stateId == "visited"), Is.EqualTo(1));
            var provider = new ReplyProvider { Target = items[0].Instance.Id };
            using (var conversation = new ConversationOrchestrator(provider, new SessionConversationContext(),
                session.Catalog, session.Avatar, session.Items, session.Mood,
                session.CharacterContext))
            {
                var reply = conversation.SendAsync("看看灯");
                while (!reply.IsCompleted) yield return null;
                Assert.That(conversation.LastAction.State, Is.EqualTo(ActionState.Running), conversation.ActionError);
                Assert.That(items[0].Instance.IsOccupied, Is.True);
                var removedId = items[0].Instance.Id;
                Assert.That(session.RecycleItem(removedId), Is.False,
                    "Player recycling must preserve a character's active item reservation.");
                Assert.That(session.Items.Count, Is.EqualTo(2));
                // External target loss still invalidates the action and releases its channels.
                session.Items.Remove(removedId);
                session.Tick(.1f);
                Assert.That(conversation.LastAction.State, Is.EqualTo(ActionState.Failed));
                Assert.That(session.Avatar.Leases.Count, Is.Zero);
                Assert.That(session.RecycleItem(removedId), Is.True);
                Assert.That(session.Items.Count, Is.EqualTo(1));
                Assert.That(store.Data.items.Select(item => item.instanceId), Does.Not.Contain(removedId));
                Assert.That(session.Notice, Does.Contain("已回收到背包"));
            }
        }

        [UnityTest]
        public IEnumerator RuntimeCarrotCutCommitsTwoPhysicalSavedPieces()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            session.Dispose();
            var carrot = TestDefinition(CarrotBuilder.DefinitionPath);
            try
            {
                session = CreateSession(new HttpLLMProvider(null),
                    itemDefinitions: new[] { carrot });
                var restore = session.RestoreItemsAsync();
                while (!restore.IsCompleted) yield return null;
                Assert.That(restore.IsFaulted, Is.False);

                var placement = session.PlaceItemAsync(carrot.Id,
                    new Pose(new Vector3(0, 0, 2), Quaternion.identity));
                while (!placement.IsCompleted) yield return null;
                Assert.That(placement.IsFaulted, Is.False, session.Notice);
                var source = session.ItemViews.Single();
                var originalMass = source.Body.Rigidbody.mass;
                var cutPoint = source.transform.TransformPoint(
                    Vector3.up * .12f);

                Assert.That(session.TryCutItem(source,
                    new Plane(source.transform.up, cutPoint), this), Is.True,
                    session.Notice);
                var pieces = session.ItemViews.ToArray();
                Assert.That(pieces, Has.Length.EqualTo(2));
                Assert.That(pieces.All(piece =>
                    piece.Definition.Id == "carrot"), Is.True);
                Assert.That(pieces.All(piece =>
                    piece.Cuttable != null &&
                    piece.Cuttable.CutDepth == 1), Is.True);
                Assert.That(pieces.All(piece =>
                    piece.Body.Shapes.Count == 1 &&
                    piece.Body.Shapes[0] is MeshCollider mesh &&
                    mesh.convex), Is.True);
                Assert.That(pieces.Sum(piece => piece.Body.Rigidbody.mass),
                    Is.EqualTo(originalMass).Within(.001f));
                Assert.That(store.Data.items, Has.Length.EqualTo(2));
                Assert.That(store.Data.items.All(item =>
                    item.definitionId == "carrot" &&
                    item.cutState != null &&
                    item.cutState.planes.Length == 1), Is.True);
                Assert.That(store.Data.items.Select(item =>
                    item.inventoryUnitId).Distinct().Count(), Is.EqualTo(1));
            }
            finally
            {
                Object.Destroy(carrot);
            }
        }

        [UnityTest]
        public IEnumerator LimitedInventoryPersistsAndRecycleRestoresOneUnit()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            session.Dispose();
            lamp.ConfigureInventory(2);
            session = CreateSession(new HttpLLMProvider(null));
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;

            Assert.That(session.GetRemainingQuantity(lamp.Id), Is.EqualTo(2));
            foreach (var position in new[] { new Vector3(2, 0, 2), new Vector3(-2, 0, 2) })
            {
                var place = session.PlaceItemAsync(
                    lamp.Id, new Pose(position, Quaternion.identity));
                while (!place.IsCompleted) yield return null;
            }
            Assert.That(session.GetRemainingQuantity(lamp.Id), Is.Zero);
            var rejected = session.PlaceItemAsync(
                lamp.Id, new Pose(new Vector3(0, 0, -2), Quaternion.identity));
            while (!rejected.IsCompleted) yield return null;
            Assert.That(session.ItemViews.Count(), Is.EqualTo(2));
            Assert.That(session.Notice, Does.Contain("已全部放置"));

            session.RecycleItem(session.ItemViews.First().Instance.Id);
            Assert.That(session.ItemViews.Count(), Is.EqualTo(1));
            Assert.That(session.GetRemainingQuantity(lamp.Id), Is.EqualTo(1));
            Assert.That(session.Notice, Does.Contain("已回收到背包"));

            session.Dispose();
            yield return null;
            session = CreateSession(new HttpLLMProvider(null));
            restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;
            Assert.That(session.ItemViews.Count(), Is.EqualTo(1));
            Assert.That(session.GetRemainingQuantity(lamp.Id), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator RefreshAllInventoryRestoresEveryLimitedItemAndSaves()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            session.Dispose();
            lamp.ConfigureInventory(3);
            session = CreateSession(new HttpLLMProvider(null));
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;

            var place = session.PlaceItemAsync(
                lamp.Id, new Pose(new Vector3(2, 0, 2), Quaternion.identity));
            while (!place.IsCompleted) yield return null;
            Assert.That(session.GetRemainingQuantity(lamp.Id), Is.EqualTo(2));

            session.RefreshAllInventory();

            Assert.That(session.GetRemainingQuantity(lamp.Id), Is.EqualTo(3));
            Assert.That(store.Data.inventory
                .Single(item => item.definitionId == lamp.Id).remaining, Is.EqualTo(3));
            Assert.That(session.Notice, Does.Contain("库存已刷新"));
        }

        [UnityTest]
        public IEnumerator DailyInventoryRefreshesAtLocalMidnight()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            session.Dispose();
            lamp.ConfigureInventory(5, true);
            var now = new DateTime(2026, 10, 15, 23, 59, 50);
            session = CreateSession(new HttpLLMProvider(null), localNow: () => now);
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;

            foreach (var position in new[] { new Vector3(2, 0, 2), new Vector3(-2, 0, 2) })
            {
                var place = session.PlaceItemAsync(
                    lamp.Id, new Pose(position, Quaternion.identity));
                while (!place.IsCompleted) yield return null;
            }
            Assert.That(session.GetRemainingQuantity(lamp.Id), Is.EqualTo(3));
            now = new DateTime(2026, 10, 16, 0, 0, 1);
            session.Tick(.1f);
            Assert.That(session.GetRemainingQuantity(lamp.Id), Is.EqualTo(5));
            Assert.That(store.Data.dailyInventoryDate, Is.EqualTo("2026-10-16"));
        }

        [UnityTest]
        public IEnumerator UnlimitedInventoryNeverDepletesOrPersistsAStockCounter()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            session.Dispose();
            lamp.ConfigureUnlimitedInventory();
            session = CreateSession(new HttpLLMProvider(null));
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;

            foreach (var position in new[]
                     {
                         new Vector3(2, 0, 2),
                         new Vector3(-2, 0, 2),
                         new Vector3(0, 0, -2)
                     })
            {
                var place = session.PlaceItemAsync(
                    lamp.Id, new Pose(position, Quaternion.identity));
                while (!place.IsCompleted) yield return null;
            }
            Assert.That(session.ItemViews.Count(), Is.EqualTo(3), session.Notice);
            Assert.That(session.GetRemainingQuantity(lamp.Id), Is.EqualTo(int.MaxValue));
            Assert.That(store.Data.inventory.Select(item => item.definitionId),
                Does.Not.Contain(lamp.Id));

            session.RecycleItem(session.ItemViews.First().Instance.Id);
            Assert.That(session.ItemViews.Count(), Is.EqualTo(2));
            Assert.That(session.GetRemainingQuantity(lamp.Id), Is.EqualTo(int.MaxValue));
        }

        [UnityTest]
        public IEnumerator ChopsticksPlacementCreatesTwoIndependentPersistentItems()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            var group = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Chopsticks.asset");
            var left = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/ChopstickLeft.asset");
            var right = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/ChopstickRight.asset");
            Assert.That(group, Is.Not.Null);
            Assert.That(left, Is.Not.Null);
            Assert.That(right, Is.Not.Null);

            session.Dispose();
            foreach (Transform child in itemRoot) Object.Destroy(child.gameObject);
            yield return null;
            session = CreateSession(new HttpLLMProvider(null),
                itemDefinitions: new[] { group, left, right });
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;

            var place = session.PlaceItemAsync(group.Id,
                new Pose(new Vector3(2, 0, 2), Quaternion.identity));
            while (!place.IsCompleted) yield return null;
            var placed = session.ItemViews.OrderBy(item => item.Definition.Id).ToArray();
            Assert.That(placed, Has.Length.EqualTo(2), session.Notice);
            Assert.That(placed.Select(item => item.Definition.Id),
                Is.EqualTo(new[] { "chopstick-left", "chopstick-right" }));
            Assert.That(placed.Select(item => item.Instance.Id).Distinct().Count(), Is.EqualTo(2));
            Assert.That(store.Data.items, Has.Length.EqualTo(2));
            Assert.That(store.Data.items.Select(item => item.definitionId),
                Is.EquivalentTo(new[] { "chopstick-left", "chopstick-right" }));
            Assert.That(session.GetRemainingQuantity(group.Id), Is.EqualTo(1));

            var leftItem = placed[0];
            var rightItem = placed[1];
            var rightPose = rightItem.Body.Pose;
            leftItem.Body.Place(new Pose(
                leftItem.Body.Pose.position + Vector3.left * .5f,
                leftItem.Body.Pose.rotation));
            Physics.SyncTransforms();
            Assert.That(Vector3.Distance(leftItem.Body.Pose.position, rightItem.Body.Pose.position),
                Is.GreaterThan(.45f));
            Assert.That(Vector3.Distance(rightItem.Body.Pose.position, rightPose.position),
                Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(rightItem.Body.Pose.rotation, rightPose.rotation),
                Is.LessThan(.01f));
            session.RecycleItem(leftItem.Instance.Id);
            Assert.That(session.ItemViews, Is.Empty);
            Assert.That(session.GetRemainingQuantity(group.Id), Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator LegacyChopstickPairMigratesToOneInventoryUnit()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            var group = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Chopsticks.asset");
            var left = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/ChopstickLeft.asset");
            var right = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/ChopstickRight.asset");

            session.Dispose();
            anchorMemory.Poses["legacy-chopsticks"] =
                new Pose(Vector3.zero, Quaternion.identity);
            store.Data = new GameSaveData
            {
                roomAnchorId = "legacy-chopsticks",
                items = new[]
                {
                    new ItemSaveData
                    {
                        instanceId = "legacy-left", definitionId = left.Id,
                        stateId = "default", x = -.0045f, z = 2f, qw = 1
                    },
                    new ItemSaveData
                    {
                        instanceId = "legacy-right", definitionId = right.Id,
                        stateId = "default", x = .0045f, z = 2f, qw = 1
                    }
                }
            };
            session = CreateSession(new HttpLLMProvider(null),
                itemDefinitions: new[] { group, left, right });
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;

            Assert.That(session.ItemViews.Count(), Is.EqualTo(2), session.Notice);
            Assert.That(session.GetRemainingQuantity(group.Id), Is.EqualTo(1));
            session.RecycleItem(session.ItemViews.First().Instance.Id);
            Assert.That(session.ItemViews, Is.Empty);
            Assert.That(session.GetRemainingQuantity(group.Id), Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator FryingPanPlacementSpawnsSpatulaOnTopAsOneInventoryUnit()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            var group = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/FryingPan.asset");
            var pan = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/FryingPanBody.asset");
            var spatula = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Spatula.asset");

            session.Dispose();
            foreach (Transform child in itemRoot) Object.Destroy(child.gameObject);
            yield return null;
            session = CreateSession(new HttpLLMProvider(null),
                itemDefinitions: new[] { group, pan, spatula });
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;
            var place = session.PlaceItemAsync(group.Id,
                new Pose(new Vector3(2, 0, 2), Quaternion.identity));
            while (!place.IsCompleted) yield return null;

            var placed = session.ItemViews.ToArray();
            Assert.That(placed.Select(item => item.Definition.Id),
                Is.EquivalentTo(new[] { "frying-pan-body", "spatula" }), session.Notice);
            Assert.That(session.GetRemainingQuantity(group.Id), Is.Zero);
            Assert.That(placed.Single(item => item.Definition.Id == "spatula")
                    .Body.Pose.position.y,
                Is.GreaterThan(placed.Single(item => item.Definition.Id == "frying-pan-body")
                    .Body.Pose.position.y + .05f));

            session.RecycleItem(placed.Single(
                item => item.Definition.Id == "spatula").Instance.Id);
            Assert.That(session.ItemViews, Is.Empty);
            Assert.That(session.GetRemainingQuantity(group.Id), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CoffeeGrinderPlacementAndRecycleOperateAsOneInventoryUnit()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            var group = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                CoffeeGrinderBuilder.GroupDefinitionPath);
            var mortar = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                CoffeeGrinderBuilder.MortarDefinitionPath);
            var pestle = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                CoffeeGrinderBuilder.PestleDefinitionPath);

            session.Dispose();
            foreach (Transform child in itemRoot) Object.Destroy(child.gameObject);
            yield return null;
            session = CreateSession(new HttpLLMProvider(null),
                itemDefinitions: new[] { group, mortar, pestle });
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;
            var place = session.PlaceItemAsync(group.Id,
                new Pose(new Vector3(2, 0, 2), Quaternion.identity));
            while (!place.IsCompleted) yield return null;

            var placed = session.ItemViews.ToArray();
            Assert.That(placed.Select(item => item.Definition.Id),
                Is.EquivalentTo(new[]
                {
                    "coffee-grinder-mortar", "coffee-grinder-pestle"
                }), session.Notice);
            Assert.That(session.GetRemainingQuantity(group.Id), Is.Zero);
            session.RecycleItem(placed.Single(
                item => item.Definition.Id == "coffee-grinder-pestle").Instance.Id);
            Assert.That(session.ItemViews, Is.Empty);
            Assert.That(session.GetRemainingQuantity(group.Id), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SaveRestoresOutfitMoodAndItemsWithNoOfflineDecay()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseSaveRestore();
        }

        private IEnumerator ExerciseSaveRestore()
        {
            session.Mood.Apply("anger", 90);
            session.Tick(7);
            var outfits = session.Wardrobe.Outfits;
            Assert.That(outfits.Count, Is.GreaterThan(0), "Production wardrobe catalog must have an alternate outfit.");
            var outfit = outfits[0].OutfitId;
            var change = session.Submit(new AvatarIntent("outfit", IntentSource.UI, payload: outfit));
            session.Tick(0);
            Assert.That(change.State, Is.EqualTo(ActionState.Succeeded), change.Error);
            var place = session.PlaceItemAsync(lamp.Id, new Pose(new Vector3(2,0,2), Quaternion.identity));
            while (!place.IsCompleted) yield return null;
            session.Save();
            var id = session.ItemViews.Single().Instance.Id;
            var savedPosition = session.ItemViews.Single().transform.position;
            session.Dispose();
            foreach (Transform child in itemRoot) Object.Destroy(child.gameObject);
            yield return null;
            session = CreateSession(new HttpLLMProvider(null));
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;
            Assert.That(session.Items.TryGet(id, out _), Is.True, session.Notice);
            Assert.That(Vector3.Distance(session.ItemViews.Single().transform.position, savedPosition),
                Is.LessThan(.02f), "Restart must restore the item at its persisted anchor pose.");
            Assert.That(session.Mood.RemainingSeconds, Is.EqualTo(33));
            Assert.That(session.Wardrobe.CurrentOutfitId, Is.EqualTo(outfit));
            var dialogue = session.Conversation.SendAsync("你好");
            while (!dialogue.IsCompleted) yield return null;
            Assert.That(session.Conversation.State, Is.EqualTo(ConversationState.Failed));
            Assert.That(session.Conversation.Text, Does.Contain("先配置ai"));
        }

        [UnityTest]
        public IEnumerator PersonaIdentityIsFixedAndPlayerSelectionsPersistAcrossRestart()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExercisePersona();
        }

        private IEnumerator ExercisePersona()
        {
            Assert.That(session.Persona.Identity.name, Is.EqualTo("冷月"));
            Assert.That(session.Persona.Identity.age, Is.EqualTo(16));
            Assert.That(session.Persona.Identity.gender, Is.EqualTo("female"));
            Assert.That(session.Persona.Identity.height_cm, Is.EqualTo(155));
            Assert.That(session.Persona.Traits.Count, Is.EqualTo(5));
            Assert.That(session.Persona.SelectedOption("extraversion"), Is.EqualTo("balanced"));

            session.SelectNextPersonality("extraversion");
            Assert.That(session.Persona.SelectedOption("extraversion"), Is.EqualTo("outgoing"));
            Assert.That(store.Data.personality.selections.Single(
                value => value.trait_id == "extraversion").option_id, Is.EqualTo("outgoing"));

            session.Dispose();
            session = CreateSession(new HttpLLMProvider(null));
            Assert.That(session.Persona.SelectedOption("extraversion"), Is.EqualTo("outgoing"));
            var context = session.CharacterContext.Capture();
            Assert.That(context.persona.personality.Single(
                value => value.id == "extraversion").option_id, Is.EqualTo("outgoing"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RuntimeContextReadsCurrentOutfitMoodAndActiveAction()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseContext();
        }

        private IEnumerator ExerciseContext()
        {
            session.ConfigureAIWorldContext(new RoomMapSnapshot(
                "ai-context-room",
                "PICO Room Capture",
                new[]
                {
                    new RoomFloorRegion("floor", new[]
                    {
                        new Vector3(-2, 0, -2),
                        new Vector3(-2, 0, 2),
                        new Vector3(2, 0, 2),
                        new Vector3(2, 0, -2)
                    })
                },
                new[]
                {
                    new RoomObstacle(
                        "table-obstacle", RoomObstacleKind.Table,
                        new Vector3(1, .4f, 1), Quaternion.identity,
                        new Vector3(1.2f, .8f, .7f))
                },
                realityItems: new[]
                {
                    new RealityItemObservation(
                        "real-table", RealitySemantic.Table,
                        new Vector3(1, .4f, 1), Quaternion.identity,
                        new Vector3(1.2f, .8f, .7f))
                }), null, null);
            session.Mood.Apply("happy", 65);
            var place = session.PlaceItemAsync(lamp.Id,
                new Pose(new Vector3(2, 0, 2), Quaternion.identity));
            while (!place.IsCompleted) yield return null;
            var item = session.ItemViews.Single();
            var visit = session.Submit(new AvatarIntent(
                "visit", IntentSource.UI, item.Instance.Id));
            session.Tick(.1f);

            var active = session.CharacterContext.Capture().runtime;
            Assert.That(active.appearance.outfit_id, Is.EqualTo("lavender-casual"));
            Assert.That(active.appearance.display_name, Is.EqualTo(session.Wardrobe.CurrentDisplayName));
            Assert.That(active.mood.id, Is.EqualTo("happy"));
            Assert.That(active.mood.level, Is.EqualTo("medium"));
            Assert.That(active.posture, Is.EqualTo("standing"));
            Assert.That(active.actions.Single().id, Is.EqualTo("visit"));
            Assert.That(active.actions.Single().phase, Is.EqualTo("approaching"));
            Assert.That(active.actions.Single().target.id, Is.EqualTo(item.Instance.Id));
            Assert.That(active.actions.Single().target.name, Is.EqualTo("陪伴灯"));
            Assert.That(active.world, Is.Not.Null);
            Assert.That(active.world.environment.source, Is.EqualTo("PICO Room Capture"));
            Assert.That(active.world.environment.floor_area_m2, Is.EqualTo(16f));
            Assert.That(active.world.environment.semantic_counts.Single().id,
                Is.EqualTo("table"));
            Assert.That(active.world.environment.semantic_counts.Single().estimated,
                Is.False);
            Assert.That(active.world.virtual_world.total_count, Is.EqualTo(1));
            Assert.That(active.world.virtual_world.item_groups.Single().count,
                Is.EqualTo(1));
            Assert.That(active.world.virtual_world.salient_items.Single().id,
                Is.EqualTo(item.Instance.Id));
            Assert.That(active.world.active_facts, Is.Empty);

            session.Avatar.StopAll(ActionStopReason.Requested);
            Assert.That(visit.State, Is.EqualTo(ActionState.Cancelled));
            var outfit = session.Wardrobe.Outfits[0];
            var change = session.Submit(new AvatarIntent(
                "outfit", IntentSource.UI, payload: outfit.OutfitId));
            session.Tick(0);
            Assert.That(change.State, Is.EqualTo(ActionState.Succeeded), change.Error);
            var changed = session.CharacterContext.Capture().runtime;
            Assert.That(changed.appearance.outfit_id, Is.EqualTo("pink-pajamas"));
            Assert.That(changed.appearance.display_name, Is.EqualTo("粉格睡衣与睡帽"));
            Assert.That(changed.recent_events.Any(value =>
                value.type == "outfit_changed" && value.value == "pink-pajamas"), Is.True);
            Assert.That(changed.recent_events.Any(value =>
                value.type == "action_finished" && value.action_id == "visit"), Is.True);
        }

        [UnityTest]
        public IEnumerator ValidatedRoomPoseRestoresItemsWhenAnchorIsTemporarilyUnavailable()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseUnavailableAnchor();
        }

        private IEnumerator ExerciseUnavailableAnchor()
        {
            var place = session.PlaceItemAsync(table.Id,
                new Pose(new Vector3(-2, 0, 2), Quaternion.identity));
            while (!place.IsCompleted) yield return null;
            session.Save();
            var expected = session.ItemViews.Single().transform.position;
            Assert.That(store.Data.hasRoomAnchorPose, Is.True);

            session.Dispose();
            foreach (Transform child in itemRoot) Object.Destroy(child.gameObject);
            yield return null;
            session = CreateSession(new HttpLLMProvider(null), new UnavailableAnchorService());
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;

            Assert.That(session.ItemsReady, Is.True, session.Notice);
            Assert.That(session.ItemRestoreFailed, Is.False);
            Assert.That(session.ItemViews.Count(), Is.EqualTo(1));
            Assert.That(Vector3.Distance(session.ItemViews.Single().transform.position, expected),
                Is.LessThan(.02f));
            Assert.That(session.Notice, Does.Contain("当前房间恢复"));
        }

        [UnityTest]
        public IEnumerator LegacyUnavailableAnchorStartsNewLayoutOnCatalogClick()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseLegacyLayout();
        }

        private IEnumerator ExerciseLegacyLayout()
        {
            session.Dispose();
            store.Data = new GameSaveData
            {
                roomAnchorId = Guid.NewGuid().ToString(),
                items = new[]
                {
                    new ItemSaveData
                    {
                        instanceId = "legacy-table", definitionId = table.Id,
                        stateId = "default", qw = 1
                    }
                }
            };
            session = CreateSession(new HttpLLMProvider(null), new UnavailableAnchorService());
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;
            Assert.That(session.ItemRestoreFailed, Is.True);
            Assert.That(session.Items.Count, Is.Zero);
            Assert.That(session.IsRestoringItems, Is.False);
            var miniGames = root.AddComponent<MiniGameHost>();
            miniGames.Configure(Array.Empty<MiniGame>());
            miniGames.Bind(session);
            Assert.That(miniGames.TryStart("missing"), Is.False);
            Assert.That(session.Notifications.Hint, Is.EqualTo("该小游戏尚未安装。"),
                "A failed legacy item restore must not masquerade as unfinished room mapping.");

            var place = session.PlaceItemAsync(chair.Id,
                new Pose(new Vector3(2, 0, -2), Quaternion.identity));
            while (!place.IsCompleted) yield return null;

            Assert.That(session.ItemsReady, Is.True, session.Notice);
            Assert.That(session.ItemRestoreFailed, Is.False);
            Assert.That(session.ItemViews.Single().Instance.DefinitionId, Is.EqualTo(chair.Id));
            Assert.That(store.Data.items.Single().definitionId, Is.EqualTo(chair.Id));
            Assert.That(store.Data.roomAnchorId, Is.Not.EqualTo(string.Empty));
            Assert.That(session.Notice, Does.Contain("已创建新布局"));
        }

        [UnityTest]
        public IEnumerator CatalogPlacementGrabAndFocusUseRegisteredDefinitions()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseCatalog();
        }

        private IEnumerator ExerciseCatalog()
        {
            Assert.That(session.CatalogItems.Select(item => item.Id),
                Is.EqualTo(new[] { "companion-lamp", "table", "chair", "television" }));

            var blocker = new GameObject("Placement Blocker", typeof(BoxCollider));
            blocker.transform.SetParent(root.transform);
            blocker.layer = LayerMask.NameToLayer("Ignore Raycast");
            blocker.transform.position = new Vector3(1.5f, .4f, 0);
            blocker.transform.localScale = new Vector3(.8f, .8f, .8f);
            Physics.SyncTransforms();

            var desired = new Vector3(1.5f, 1.6f, 0);
            var place = session.PlaceItemAsync(table.Id, new Pose(desired, Quaternion.identity));
            while (!place.IsCompleted) yield return null;

            var item = session.ItemViews.Single();
            Assert.That(item.transform.position.y, Is.EqualTo(0).Within(.02f),
                "Placement must use the room's main floor instead of the nearest elevated surface.");
            Assert.That(Vector3.Distance(
                Vector3.ProjectOnPlane(item.transform.position, Vector3.up),
                Vector3.ProjectOnPlane(desired, Vector3.up)), Is.GreaterThan(.35f),
                "A blocked requested pose must shift to a nearby free position.");
            Assert.That(item.GetComponent<XRGrabInteractable>(), Is.Not.Null);
            Assert.That(item.GetComponent<Rigidbody>(), Is.Not.Null);
            Assert.That(table.FocusMaterial.shader.name, Is.EqualTo("MateSoul/Item Focus Highlight"));
            Assert.That(table.FocusMaterial.GetFloat("_OutlineWidth"), Is.EqualTo(.003f).Within(.0001f));
            Assert.That(table.FocusMaterial.GetFloat("_Intensity"), Is.EqualTo(.65f).Within(.0001f));
            var focusColor = table.FocusMaterial.GetColor("_Color");
            Assert.That(focusColor.r, Is.EqualTo(1f).Within(.001f));
            Assert.That(focusColor.g, Is.EqualTo(.16f).Within(.001f));
            Assert.That(focusColor.b, Is.EqualTo(.48f).Within(.001f));
            Assert.That(focusColor.a, Is.EqualTo(.7f).Within(.001f));

            item.SetFocused(true);
            Assert.That(item.GetComponentsInChildren<Renderer>()
                .All(renderer => !renderer.sharedMaterials.Contains(table.FocusMaterial)), Is.True,
                "Ordinary focus must only highlight declared interactive components.");
            item.SetFocused(false);
            Assert.That(item.GetComponentsInChildren<Renderer>()
                .All(renderer => !renderer.sharedMaterials.Contains(table.FocusMaterial)), Is.True);

            character.GetComponent<PhysicsBody>().Place(
                new Pose(new Vector3(0, 0, 1), Quaternion.identity));
            item.Body.Place(new Pose(new Vector3(0, 0, 2), Quaternion.identity));
            var aim = new GameObject("Focus Aim");
            aim.transform.SetParent(root.transform);
            aim.transform.position = new Vector3(0, .73f, 0);
            aim.transform.rotation = Quaternion.identity;
            var focus = root.AddComponent<VirtualItemFocusController>();
            Physics.SyncTransforms();
            var resolve = typeof(VirtualItemFocusController).GetMethod("ResolveFocusTarget",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(resolve, Is.Not.Null);
            Assert.That(resolve.Invoke(focus, new object[] { aim.transform }), Is.SameAs(item),
                "The avatar collider must not hide a centered virtual item.");

            var rayRoot = new GameObject("Configured Rays");
            rayRoot.transform.SetParent(root.transform);
            var createRays = typeof(GameSceneBuilder).GetMethod("CreateControllerRays",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(createRays, Is.Not.Null);
            createRays.Invoke(null, new object[] { rayRoot.transform });
            var rays = rayRoot.GetComponentsInChildren<XRRayInteractor>(true);
            Assert.That(rays.Length, Is.EqualTo(5));
            Assert.That(rays.All(ray => !ray.useForceGrab), Is.True,
                "Furniture must remain at its ray hit distance instead of snapping to the hand.");
            var mode = rayRoot.GetComponent<PlayerInteractionModeController>();
            Assert.That(mode, Is.Not.Null);
            Assert.That(mode.CurrentMode, Is.EqualTo(PlayerInteractionMode.Controller));
            var inputs = rayRoot.GetComponentsInChildren<ActionBasedController>(true);
            var controllerInputs = inputs.Where(input => input.name.EndsWith("UI Ray")).ToArray();
            Assert.That(controllerInputs, Has.Length.EqualTo(2));
            Assert.That(controllerInputs.All(input =>
                    input.GetComponent<ControllerItemRay>() != null),
                Is.True, "Controller rays must expose their tracked controller pose for hand holding.");
            foreach (var input in controllerInputs)
            {
                var hand = input.name.StartsWith("RightHand") ? "RightHand" : "LeftHand";
                var marker = input.transform.Find(
                    hand + " Physical Pose (Editor Only)");
                Assert.That(marker, Is.Not.Null);
                Assert.That(marker.CompareTag("EditorOnly"), Is.True);
                Assert.That(marker.GetComponent<Renderer>(), Is.Not.Null);
                Assert.That(marker.GetComponent<Collider>(), Is.Null);
                Assert.That(marker.GetComponent<HandHoldSimulatorIndicator>(), Is.Not.Null);
            }
            Assert.That(controllerInputs.All(input =>
                    input.selectAction.action.bindings.Any(binding =>
                        binding.path.EndsWith("/gripPressed"))),
                Is.True, "Physical PICO controllers must bind the digital gripPressed control.");
            Assert.That(controllerInputs.All(input =>
                    input.selectAction.action.bindings.Any(binding =>
                        binding.path.EndsWith("/grip") &&
                        binding.interactions.Contains("pressPoint=0.1"))),
                Is.True);
            Assert.That(controllerInputs.All(input =>
                    input.selectActionValue.action.bindings.Single().path.EndsWith("/grip")),
                Is.True);
            Assert.That(controllerInputs.Single(input => input.name.StartsWith("RightHand"))
                .selectAction.action.bindings.Any(binding => binding.path == "<Keyboard>/o"), Is.True);
            Assert.That(controllerInputs.Single(input => input.name.StartsWith("LeftHand"))
                .selectAction.action.bindings.Any(binding => binding.path == "<Keyboard>/e"), Is.True);
            Assert.That(controllerInputs.All(input =>
                    input.uiPressAction.action.bindings.Any(binding =>
                        binding.path.EndsWith("/triggerPressed"))),
                Is.True, "Controller rays must click UI with the trigger.");
            Assert.That(controllerInputs.Single(input => input.name.StartsWith("RightHand"))
                .uiPressAction.action.bindings.Any(binding => binding.path == "<Keyboard>/u"), Is.True);
            Assert.That(controllerInputs.Single(input => input.name.StartsWith("LeftHand"))
                .uiPressAction.action.bindings.Any(binding => binding.path == "<Keyboard>/q"), Is.True);
            Assert.That(controllerInputs.All(input =>
                    input.uiPressActionValue.action.bindings.Single().path.EndsWith("/trigger")),
                Is.True);
            var gazeInput = inputs.Single(input => input.name == "Gaze Pinch Ray");
            Assert.That(gazeInput.positionAction.action.bindings.Single().path,
                Is.EqualTo("<PXR_HMD>/eyeGazePosition"));
            Assert.That(gazeInput.rotationAction.action.bindings.Single().path,
                Is.EqualTo("<PXR_HMD>/eyeGazeRotation"));
            Assert.That(gazeInput.isTrackedAction.action.bindings.Single().path,
                Is.EqualTo("<PXR_HMD>/eyeGazeIsTracked"));
            Assert.That(gazeInput.uiPressAction.action.bindings.Single().path,
                Does.EndWith("/pinchTouched"));
            Assert.That(gazeInput.uiPressActionValue.action.bindings.Single().path,
                Does.EndWith("/pinchValue"));
            Assert.That(gazeInput.selectAction.action.bindings, Is.Empty,
                "Gaze pinch must click UI without grabbing the aimed furniture.");
            Assert.That(gazeInput.GetComponent<XRRayInteractor>().enableUIInteraction, Is.True);
            var handInputs = inputs.Where(input => input.name.EndsWith("Hand Ray")).ToArray();
            Assert.That(handInputs, Has.Length.EqualTo(2));
            Assert.That(handInputs.All(input => input.GetComponent<HandItemRay>() != null),
                Is.True, "Hand rays must expose the physical pinch pose for hand holding.");
            Assert.That(handInputs.All(input =>
                    input.positionAction.action.bindings.Single().path.EndsWith("/pointer/position")),
                Is.True);
            Assert.That(handInputs.All(input =>
                    input.uiPressAction.action.bindings.Single().path.EndsWith("/pointerActivated")),
                Is.True);
            mode.SetMode(PlayerInteractionMode.EyeHand);
            Assert.That(controllerInputs.All(input => !input.gameObject.activeSelf), Is.True);
            Assert.That(gazeInput.gameObject.activeSelf, Is.True);
            Assert.That(handInputs.All(input => input.gameObject.activeSelf), Is.True);
            mode.SetMode(PlayerInteractionMode.Gesture);
            Assert.That(gazeInput.gameObject.activeSelf, Is.False);
            Assert.That(handInputs.All(input => input.gameObject.activeSelf), Is.True);
            mode.SetMode(PlayerInteractionMode.Controller);
            Assert.That(controllerInputs.All(input => input.gameObject.activeSelf), Is.True);
            Assert.That(gazeInput.gameObject.activeSelf, Is.False);
            Assert.That(handInputs.All(input => !input.gameObject.activeSelf), Is.True);
        }

        [UnityTest]
        public IEnumerator FixedMovableFurnitureCannotBeGrabbedButRemainsDynamic()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            television.ConfigurePlacement(true);
            var placement = session.PlaceItemAsync(television.Id,
                new Pose(new Vector3(2, 0, 2), Quaternion.identity));
            while (!placement.IsCompleted) yield return null;

            var item = session.ItemViews.Single();
            var grab = item.GetComponent<PhysicsGrabInteractable>();
            Assert.That(item.IsFixedAfterPlacement, Is.True);
            Assert.That(grab.PlayerGrabbable, Is.False);
            Assert.That(item.Body.IsContact, Is.False);
            Assert.That(item.Body.Rigidbody.isKinematic, Is.False);
            Assert.That(item.Body.Rigidbody.useGravity, Is.True);

            var position = item.Body.Rigidbody.position;
            item.Body.AddForce(Vector3.up * 20f, ForceMode.Impulse);
            yield return EngineTestFrames.FixedStep();
            Assert.That(item.Body.Rigidbody.position.y, Is.GreaterThan(position.y + .005f),
                "Fixed furniture must still respond to external physics forces.");
        }

        [UnityTest]
        public IEnumerator ImmovableFurnitureIgnoresExternalForces()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            table.ConfigurePlacement(
                true, true, ItemPlacementSurface.Ground, true);
            var placement = session.PlaceItemAsync(table.Id,
                new Pose(new Vector3(2, 0, 2), Quaternion.identity));
            while (!placement.IsCompleted) yield return null;

            var item = session.ItemViews.Single();
            var pose = item.Body.Pose;
            Assert.That(item.IsImmovableAfterPlacement, Is.True);
            Assert.That(item.Body.IsContact, Is.True);
            Assert.That(item.Body.Rigidbody.isKinematic, Is.True);
            Assert.That(item.GetComponent<PhysicsGrabInteractable>().PlayerGrabbable, Is.False);

            item.Body.AddForce(Vector3.up * 100f, ForceMode.Impulse);
            item.Body.AddTorque(Vector3.one * 100f, ForceMode.Impulse);
            yield return EngineTestFrames.FixedStep();
            Assert.That(item.Body.Pose.position, Is.EqualTo(pose.position));
            Assert.That(item.Body.Pose.rotation, Is.EqualTo(pose.rotation));
        }

        [UnityTest]
        public IEnumerator WallItemsRequireVerticalSurfaceAndRemainLocked()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            session.Dispose();
            var source = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Clock.asset");
            var clock = Object.Instantiate(source);
            clock.Configure(clock.Id, clock.DisplayName, clock.Prefab, clock.Capabilities,
                true, clock.Footprint, clock.PlacementHeight, clock.FocusMaterial,
                clock.Category);
            clock.ConfigureInventory(1);
            clock.ConfigurePlacement(
                true, true, ItemPlacementSurface.Wall, true);
            session = CreateSession(new HttpLLMProvider(null), itemDefinitions: new[] { clock });
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;

            var wall = new GameObject("Placement Wall", typeof(BoxCollider));
            wall.transform.SetParent(root.transform);
            wall.transform.position = new Vector3(0, 1.5f, 2f);
            wall.transform.localScale = new Vector3(4f, 3f, .1f);
            Physics.SyncTransforms();

            Assert.That(session.TryResolveWallPlacement(clock,
                new Ray(new Vector3(0, 1.5f, 0), Vector3.forward), null, out var pose), Is.True);
            Assert.That(Vector3.Dot(pose.rotation * Vector3.forward, Vector3.back),
                Is.GreaterThan(.99f));
            Assert.That(session.TryResolveWallPlacement(clock,
                new Ray(new Vector3(0, 1.5f, 0), Vector3.down), null, out _), Is.False);

            var placement = session.PlaceItemAsync(clock.Id, pose, false);
            while (!placement.IsCompleted) yield return null;
            var item = session.ItemViews.Single();
            Assert.That(item.IsImmovableAfterPlacement, Is.True);
            Assert.That(item.Body.IsContact, Is.True);
            Assert.That(item.Body.Rigidbody.isKinematic, Is.True);
            Assert.That(item.Body.Rigidbody.useGravity, Is.False);
            Assert.That(item.GetComponent<PhysicsGrabInteractable>().PlayerGrabbable, Is.False);

            var id = item.Instance.Id;
            session.Save();
            session.Dispose();
            yield return null;
            session = CreateSession(new HttpLLMProvider(null), itemDefinitions: new[] { clock });
            restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;
            Assert.That(session.ItemsReady, Is.True, session.Notice);
            Assert.That(session.ItemViews.Single().Instance.Id, Is.EqualTo(id));
        }

        [UnityTest]
        public IEnumerator FixedSurfacePreviewAcceptsSmallTablesShelvesAndBeds()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            television.ConfigurePlacement(
                true, true, ItemPlacementSurface.Ground, true);
            var cameraPosition = new Vector3(0, 1.6f, 0);
            var supports = new[]
            {
                (Name: "Small Table", Center: new Vector3(-1.5f, .35f, 2f),
                    Size: new Vector3(.42f, .7f, .39f)),
                (Name: "Shelf", Center: new Vector3(0, 1.05f, 2f),
                    Size: new Vector3(.52f, .12f, .5f)),
                (Name: "Bed", Center: new Vector3(1.5f, .25f, 2f),
                    Size: new Vector3(1.8f, .5f, 2f))
            };
            foreach (var expected in supports)
            {
                var support = new GameObject(expected.Name, typeof(BoxCollider));
                support.transform.SetParent(root.transform);
                support.transform.position = expected.Center;
                support.transform.localScale = expected.Size;
                Physics.SyncTransforms();
                var top = expected.Center + Vector3.up * (expected.Size.y * .5f);
                Assert.That(session.TryResolveSurfacePlacement(television,
                    new Ray(cameraPosition, top - cameraPosition), null, out var placement),
                    Is.True, expected.Name);
                Assert.That(placement.position.y, Is.EqualTo(top.y + .006f).Within(.002f),
                    expected.Name);
                var towardPlayer = Vector3.ProjectOnPlane(
                    cameraPosition - placement.position, Vector3.up).normalized;
                Assert.That(Vector3.Dot(
                        placement.rotation * Vector3.back, towardPlayer),
                    Is.GreaterThan(.99f), expected.Name + " front");
                Assert.That(session.TryResolveExactPlacement(
                    television, placement, null, out _), Is.True, expected.Name);
            }

            var undersized = new GameObject("Undersized Stand", typeof(BoxCollider));
            undersized.transform.SetParent(root.transform);
            undersized.transform.position = new Vector3(3f, .35f, 2f);
            undersized.transform.localScale = new Vector3(.36f, .7f, .32f);
            Physics.SyncTransforms();
            var undersizedTop = undersized.transform.position + Vector3.up * .35f;
            Assert.That(session.TryResolveSurfacePlacement(television,
                new Ray(cameraPosition, undersizedTop - cameraPosition), null, out _),
                Is.False, "Clearly undersized supports must remain invalid.");

            var wall = new GameObject("Vertical Wall", typeof(BoxCollider));
            wall.transform.SetParent(root.transform);
            wall.transform.position = new Vector3(0, 1.5f, 4f);
            wall.transform.localScale = new Vector3(2f, 3f, .1f);
            Physics.SyncTransforms();
            Assert.That(session.TryResolveSurfacePlacement(television,
                new Ray(new Vector3(0, 1.5f, 3f), Vector3.forward), null, out _), Is.False);
        }

        [UnityTest]
        public IEnumerator ImmovablePreviewPlacesOnCompoundVirtualItemSurface()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            var supportPlacement = session.PlaceItemAsync(table.Id,
                new Pose(new Vector3(2, 0, 2), Quaternion.identity));
            while (!supportPlacement.IsCompleted) yield return null;
            var support = session.ItemViews.Single();
            var compoundSurface = new GameObject(
                "Compound Virtual Support", typeof(BoxCollider));
            compoundSurface.transform.SetParent(support.transform, false);
            compoundSurface.transform.localPosition =
                Vector3.up * (table.PlacementHeight - .02f);
            compoundSurface.GetComponent<BoxCollider>().size =
                new Vector3(.9f, .04f, .6f);
            Physics.SyncTransforms();

            chair.ConfigurePlacement(
                true, true, ItemPlacementSurface.Ground, true);
            Assert.That(chair.AllowSurfacePlacement, Is.False);
            var cameraPosition = new Vector3(2, 1.6f, 0);
            var surfacePoint = new Vector3(2, table.PlacementHeight, 2);
            Assert.That(session.TryResolveSurfacePlacement(chair,
                new Ray(cameraPosition, surfacePoint - cameraPosition),
                null, out var placement), Is.True);
            Assert.That(placement.position.y,
                Is.EqualTo(table.PlacementHeight + .006f).Within(.004f));

            var placeChair = session.PlaceItemAsync(chair.Id, placement, false);
            while (!placeChair.IsCompleted) yield return null;
            Assert.That(session.Items.Count, Is.EqualTo(2), session.Notice);
            Assert.That(session.ItemViews.Single(item => item.Definition.Id == chair.Id)
                .transform.position.y,
                Is.EqualTo(table.PlacementHeight + .006f).Within(.01f));
        }

        [UnityTest]
        public IEnumerator SurfacePlacementAndReversedSupportOrderRestoreAsOneLayout()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseSupportOrder();
        }

        private IEnumerator ExerciseSupportOrder()
        {
            var first = session.PlaceItemAsync(table.Id, new Pose(new Vector3(2, 0, 2), Quaternion.identity));
            while (!first.IsCompleted) yield return null;
            Physics.SyncTransforms();
            var tableTop = new Vector3(2, .75f, 2);
            Assert.That(session.TryResolveSurfacePlacement(television,
                new Ray(new Vector3(2, 1.7f, 0), tableTop - new Vector3(2, 1.7f, 0)),
                null, out var televisionPlacement), Is.True);
            var second = session.PlaceItemAsync(
                television.Id, televisionPlacement, false);
            while (!second.IsCompleted) yield return null;
            Assert.That(session.Items.Count, Is.EqualTo(2), session.Notice);
            var tv = session.ItemViews.Single(item => item.Definition.Id == television.Id);
            Assert.That(tv.transform.position.y, Is.EqualTo(.75f).Within(.025f));
            Assert.That(tv.Body.Rigidbody.mass, Is.EqualTo(18));
            Assert.That(tv.Body.Shapes.All(shape => shape.sharedMaterial == television.Physics.material), Is.True);
            for (var time = 0f; time < 1.5f; time += Time.deltaTime)
            {
                session.Tick(Time.deltaTime);
                yield return null;
            }
            session.Save();
            store.Data.items = store.Data.items.Reverse().ToArray();
            var ids = store.Data.items.Select(item => item.instanceId).ToArray();
            session.Dispose();
            yield return null;
            session = CreateSession(new HttpLLMProvider(null), new UnavailableAnchorService());
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;
            Assert.That(session.ItemsReady, Is.True, session.Notice);
            Assert.That(session.ItemViews.Select(item => item.Instance.Id), Is.EquivalentTo(ids));
            Assert.That(session.ItemViews.Single(item => item.Definition.Id == television.Id)
                .transform.position.y, Is.EqualTo(.75f).Within(.04f));
        }

        [UnityTest]
        public IEnumerator ConflictingSavedUnitReturnsToInventoryWithoutHidingValidItems()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseConflictingSavedUnitRecovery();
        }

        private IEnumerator ExerciseConflictingSavedUnitRecovery()
        {
            session.Dispose();
            yield return null;
            store.Data = new GameSaveData
            {
                items = new[]
                {
                    new ItemSaveData
                    {
                        instanceId = "valid-table",
                        definitionId = table.Id,
                        inventoryDefinitionId = table.Id,
                        inventoryUnitId = "table-unit",
                        x = -2,
                        z = 2,
                        qw = 1,
                        stateId = "default"
                    },
                    new ItemSaveData
                    {
                        instanceId = "blocked-chair",
                        definitionId = chair.Id,
                        inventoryDefinitionId = chair.Id,
                        inventoryUnitId = "chair-unit",
                        x = 2,
                        z = 2,
                        qw = 1,
                        stateId = "default"
                    }
                }
            };
            var blocker = new GameObject(
                "Changed room obstacle", typeof(BoxCollider));
            blocker.transform.SetParent(root.transform);
            blocker.transform.position = new Vector3(2, .5f, 2);
            blocker.GetComponent<BoxCollider>().size = Vector3.one;
            Physics.SyncTransforms();

            session = CreateSession(new HttpLLMProvider(null));
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;

            Assert.That(session.ItemsReady, Is.True, session.Notice);
            Assert.That(session.ItemRestoreFailed, Is.False);
            Assert.That(session.ItemViews.Select(item => item.Definition.Id),
                Is.EqualTo(new[] { table.Id }));
            Assert.That(store.Data.items.Select(item => item.definitionId),
                Is.EqualTo(new[] { table.Id }));
            Assert.That(session.GetRemainingQuantity(chair.Id), Is.EqualTo(100));
            Assert.That(session.GetRemainingQuantity(table.Id), Is.EqualTo(99));
            Assert.That(session.Notice, Does.Contain("已回收到背包"));
        }

        [UnityTest]
        public IEnumerator InvalidLayoutsRollBackEveryBodyAndPreserveOriginalSave()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseInvalidLayouts();
        }

        private IEnumerator ExerciseInvalidLayouts()
        {
            foreach (var fault in new[] { "overlap", "unsupported", "duplicate", "unknown", "rotation" })
            {
                session.Dispose();
                yield return null;
                var first = new ItemSaveData
                {
                    instanceId = "one", definitionId = table.Id, x = -2, z = 2, qw = 1,
                    stateId = "default"
                };
                var second = new ItemSaveData
                {
                    instanceId = "two", definitionId = chair.Id, x = 2, z = 2, qw = 1,
                    stateId = "default"
                };
                if (fault == "overlap")
                {
                    second.definitionId = table.Id;
                    second.x = -2;
                    second.z = 2;
                }
                if (fault == "unsupported") second.y = 2;
                if (fault == "duplicate") second.instanceId = first.instanceId;
                if (fault == "unknown") second.definitionId = "missing";
                if (fault == "rotation") second.qw = 0;
                itemRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                store.Data = new GameSaveData { items = new[] { first, second } };
                var original = JsonUtility.ToJson(new GameSaveData { items = store.Data.items });
                session = CreateSession(new HttpLLMProvider(null));
                var restore = session.RestoreItemsAsync();
                while (!restore.IsCompleted) yield return null;
                Assert.That(session.ItemRestoreFailed, Is.True, fault);
                Assert.That(session.Items.Count, Is.Zero, fault);
                Assert.That(session.ItemViews, Is.Empty, fault);
                Assert.That(itemRoot.GetComponentsInChildren<Collider>(), Is.Empty, fault);
                session.Save();
                Assert.That(JsonUtility.ToJson(new GameSaveData { items = store.Data.items }),
                    Is.EqualTo(original), fault);
            }
        }

        [UnityTest]
        public IEnumerator PauseAndRemovalCancelSelectionAndNeverSaveAnAirborneHandPose()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseSelectionLifecycle();
        }

        private IEnumerator ExerciseSelectionLifecycle()
        {
            var place = session.PlaceItemAsync(chair.Id, new Pose(new Vector3(2, 0, 2), Quaternion.identity));
            while (!place.IsCompleted) yield return null;
            var item = session.ItemViews.Single();
            var manager = new GameObject("Physics XR manager").AddComponent<XRInteractionManager>();
            manager.transform.SetParent(root.transform);
            var ray = new GameObject("Physics XR ray").AddComponent<XRRayInteractor>();
            ray.transform.SetParent(root.transform);
            ray.interactionManager = manager;
            ray.transform.position = item.transform.position + Vector3.up;
            var grab = item.GetComponent<PhysicsGrabInteractable>();
            grab.interactionManager = manager;
            manager.SelectEnter((IXRSelectInteractor)ray, grab);
            Assert.That(item.IsHeld && item.Body.IsFollowing, Is.True);
            item.Body.Place(new Pose(item.transform.position + Vector3.up, Quaternion.Euler(0, 30, 0)));
            session.Save();
            Assert.That(store.Data.items.Single().y, Is.EqualTo(0).Within(.02f));
            session.SetPaused(true);
            Assert.That(item.IsHeld || grab.isSelected || item.Body.IsFollowing, Is.False);
            var pose = item.Body.Pose;
            var avatarPose = character.GetComponent<PhysicsBody>().Pose;
            yield return EngineTestFrames.Seconds(.15f);
            Assert.That(item.Body.Pose.position, Is.EqualTo(pose.position));
            Assert.That(character.GetComponent<PhysicsBody>().Pose.position, Is.EqualTo(avatarPose.position));
            session.SetPaused(false);
            manager.SelectEnter((IXRSelectInteractor)ray, grab);
            var itemId = item.Instance.Id;
            session.RemoveItem(itemId);
            Assert.That(grab.isSelected, Is.False);
            Assert.That(item.gameObject.activeSelf, Is.False);
            Assert.That(store.Data.items, Is.Empty);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LostControllerTrackingReleasesBodyWithoutTeleport()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseLostTracking();
        }

        private IEnumerator ExerciseLostTracking()
        {
            var place = session.PlaceItemAsync(chair.Id, new Pose(new Vector3(2, 0, 2), Quaternion.identity));
            while (!place.IsCompleted) yield return null;
            var item = session.ItemViews.Single();
            var manager = new GameObject("Tracking XR manager").AddComponent<XRInteractionManager>();
            manager.transform.SetParent(root.transform);
            var controller = new GameObject("Untracked controller").AddComponent<ActionBasedController>();
            controller.transform.SetParent(root.transform);
            var ray = controller.gameObject.AddComponent<XRRayInteractor>();
            ray.interactionManager = manager;
            var grab = item.GetComponent<PhysicsGrabInteractable>();
            grab.interactionManager = manager;
            manager.SelectEnter((IXRSelectInteractor)ray, grab);
            var before = item.Body.Pose;
            grab.ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase.Dynamic);
            Assert.That(item.IsHeld || grab.isSelected || item.Body.IsFollowing, Is.False);
            Assert.That(item.Body.Pose.position, Is.EqualTo(before.position));
            Assert.That(item.Body.Rigidbody.isKinematic, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FragileItemsIgnoreHeldImpactAndCreateSeveritySpecificDebris()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseBreakableItems();
        }

        private IEnumerator ExerciseBreakableItems()
        {
            var plate = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Plate.asset");
            Assert.That(plate.Breakable.enabled, Is.True);
            session.Dispose();
            foreach (Transform child in itemRoot) Object.Destroy(child.gameObject);
            yield return null;
            session = CreateSession(new HttpLLMProvider(null),
                itemDefinitions: new[] { plate });
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;

            var lightPlacement = session.PlaceItemAsync(plate.Id,
                new Pose(new Vector3(2, 0, 2), Quaternion.identity));
            while (!lightPlacement.IsCompleted) yield return null;
            yield return EngineTestFrames.Seconds(.3f);
            var item = session.ItemViews.Single();
            var breakable = item.GetComponent<BreakableItem>();
            Assert.That(breakable, Is.Not.Null);

            var manager = new GameObject("Breakable XR manager").AddComponent<XRInteractionManager>();
            manager.transform.SetParent(root.transform);
            var ray = new GameObject("Breakable XR ray").AddComponent<XRRayInteractor>();
            ray.transform.SetParent(root.transform);
            ray.interactionManager = manager;
            var grab = item.GetComponent<PhysicsGrabInteractable>();
            grab.interactionManager = manager;
            manager.SelectEnter((IXRSelectInteractor)ray, grab);
            breakable.ApplyImpactForTesting(plate.Breakable.heavyForceThreshold + 10);
            Assert.That(session.ItemViews.Single(), Is.SameAs(item),
                "A held fragile item must ignore even a heavy impact.");
            Assert.That(session.Debris, Is.Empty);

            manager.SelectCancel((IXRSelectInteractor)ray, grab);
            breakable.ApplyImpactForTesting(plate.Breakable.lightForceThreshold + 1);
            Assert.That(session.ItemViews, Is.Empty);
            Assert.That(store.Data.items, Is.Empty,
                "A broken item must be removed from persistent layout state.");
            var lightDebris = session.Debris.Single();
            Assert.That(lightDebris.FragmentCount, Is.EqualTo(plate.Breakable.lightFragmentCount));
            AssertBreakSound(lightDebris, plate.Breakable.breakSound,
                expectedVolume: .72f, expectedPitch: 1.04f);
            Assert.That(lightDebris.Bodies.All(body => body.GetComponent<PhysicsBody>() != null), Is.True);
            Assert.That(lightDebris.Bodies.All(body =>
                body.GetComponent<MeshFilter>()?.sharedMesh.vertexCount > 6), Is.True,
                "The plate must use authored ceramic meshes rather than six-vertex wedges.");
            Assert.That(lightDebris.Bodies.All(body =>
                body.GetComponent<MeshCollider>()?.convex == true), Is.True);
            Assert.That(lightDebris.Bodies.SelectMany(body =>
                    body.GetComponent<MeshRenderer>().sharedMaterials)
                .Any(material => material.name.Contains("Ceramic_Fracture")), Is.True);
            session.SetPaused(true);
            Assert.That(lightDebris.Bodies.All(body => body.IsPaused), Is.True);
            session.SetPaused(false);
            Assert.That(lightDebris.Bodies.All(body => !body.IsPaused), Is.True);

            var heavyPlacement = session.PlaceItemAsync(plate.Id,
                new Pose(new Vector3(-2, 0, 2), Quaternion.identity));
            while (!heavyPlacement.IsCompleted) yield return null;
            yield return EngineTestFrames.Seconds(.3f);
            var heavyItem = session.ItemViews.Single();
            heavyItem.GetComponent<BreakableItem>()
                .ApplyImpactForTesting(plate.Breakable.heavyForceThreshold + 1);
            var debris = session.Debris.ToArray();
            Assert.That(debris, Has.Length.EqualTo(2));
            Assert.That(debris.Count(value =>
                    value.FragmentCount == plate.Breakable.heavyFragmentCount),
                Is.EqualTo(1));
            AssertBreakSound(debris.Single(value =>
                    value.FragmentCount == plate.Breakable.heavyFragmentCount),
                plate.Breakable.breakSound, expectedVolume: .95f, expectedPitch: .94f);
            Assert.That(session.Notice, Does.Contain("许多小碎片"));

            var fallingPlacement = session.PlaceItemAsync(plate.Id,
                new Pose(new Vector3(0, 0, -2), Quaternion.identity));
            while (!fallingPlacement.IsCompleted) yield return null;
            yield return EngineTestFrames.Seconds(.3f);
            var fallingItem = session.ItemViews.Single();
            fallingItem.Body.Place(new Pose(
                fallingItem.Body.Pose.position + Vector3.up * 3f,
                fallingItem.Body.Pose.rotation));
            for (var elapsed = 0f; elapsed < 2f && session.ItemViews.Any();
                 elapsed += Time.deltaTime)
                yield return null;
            Assert.That(session.ItemViews, Is.Empty,
                "A three-meter ceramic drop must break through the real collision callback.");
            Assert.That(session.Debris.Count(value =>
                    value.FragmentCount == plate.Breakable.heavyFragmentCount),
                Is.EqualTo(2));

            var mug = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                "Assets/MateSoul/Content/Props/Mug.asset");
            session.Dispose();
            foreach (Transform child in itemRoot) Object.Destroy(child.gameObject);
            yield return null;
            session = CreateSession(new HttpLLMProvider(null),
                itemDefinitions: new[] { mug });
            restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;

            var mugLightPlacement = session.PlaceItemAsync(mug.Id,
                new Pose(new Vector3(2, 0, -2), Quaternion.identity));
            while (!mugLightPlacement.IsCompleted) yield return null;
            yield return EngineTestFrames.Seconds(.3f);
            session.ItemViews.Single().GetComponent<BreakableItem>()
                .ApplyImpactForTesting(mug.Breakable.lightForceThreshold + 1);
            var mugLightDebris = session.Debris.Single();
            Assert.That(mugLightDebris.FragmentCount,
                Is.EqualTo(mug.Breakable.lightFragmentCount));
            AssertBreakSound(mugLightDebris, mug.Breakable.breakSound,
                expectedVolume: .72f, expectedPitch: 1.04f);
            Assert.That(mugLightDebris.Bodies.All(body =>
                body.GetComponent<MeshFilter>()?.sharedMesh.vertexCount > 6), Is.True);

            var mugHeavyPlacement = session.PlaceItemAsync(mug.Id,
                new Pose(new Vector3(-2, 0, -2), Quaternion.identity));
            while (!mugHeavyPlacement.IsCompleted) yield return null;
            yield return EngineTestFrames.Seconds(.3f);
            session.ItemViews.Single().GetComponent<BreakableItem>()
                .ApplyImpactForTesting(mug.Breakable.heavyForceThreshold + 1);
            Assert.That(session.Debris.Count(value =>
                    value.FragmentCount == mug.Breakable.heavyFragmentCount),
                Is.EqualTo(1));
        }

        private static void AssertBreakSound(BreakableDebris debris, AudioClip expectedClip,
            float expectedVolume, float expectedPitch)
        {
            var source = debris.BreakSoundSource;
            Assert.That(source, Is.Not.Null);
            Assert.That(source.clip, Is.SameAs(expectedClip));
            Assert.That(source.playOnAwake, Is.False);
            Assert.That(source.loop, Is.False);
            Assert.That(source.spatialBlend, Is.EqualTo(1f));
            Assert.That(source.minDistance, Is.EqualTo(.35f));
            Assert.That(source.maxDistance, Is.EqualTo(7f));
            Assert.That(source.volume, Is.EqualTo(expectedVolume).Within(.001f));
            Assert.That(source.pitch, Is.EqualTo(expectedPitch).Within(.001f));
            Assert.That(debris.BreakSoundRequestedAt, Is.GreaterThanOrEqualTo(0));
            Assert.That(debris.FragmentsReadyAt,
                Is.GreaterThanOrEqualTo(debris.BreakSoundRequestedAt),
                "The sound must be requested before fragment instantiation and collider cooking finish.");
        }

        [UnityTest]
        public IEnumerator LateAnchorCompletionAndStorageFailureDoNotLeaveGhostItems()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseLateCreation();
        }

        private IEnumerator ExerciseLateCreation()
        {
            session.Dispose();
            var anchors = new DeferredAnchorService();
            session = CreateSession(new HttpLLMProvider(null), anchors);
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;
            var pending = session.PlaceItemAsync(table.Id,
                new Pose(new Vector3(2, 0, 2), Quaternion.identity));
            Assert.That(pending.IsCompleted, Is.False);
            session.SetPaused(true);
            anchors.Created.SetResult("late-anchor");
            while (!pending.IsCompleted) yield return null;
            Assert.That(session.Items.Count, Is.Zero);
            Assert.That(store.Data.items, Is.Empty);
            Assert.That(session.ItemsReady, Is.True);
            session.SetPaused(false);
            store.FailWrites = true;
            var failed = session.PlaceItemAsync(table.Id,
                new Pose(new Vector3(2, 0, 2), Quaternion.identity));
            while (!failed.IsCompleted) yield return null;
            Assert.That(session.Items.Count, Is.Zero);
            Assert.That(session.Notice, Does.Contain("Storage unavailable"));
            store.FailWrites = false;
            session.Save();
            Assert.That(store.Data.items, Is.Empty);
            Assert.That(string.IsNullOrEmpty(store.Data.roomAnchorId), Is.True);
        }

        [UnityTest]
        public IEnumerator PausedRestoreRetriesRegardlessOfLateAnchorCompletionOrder()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExercisePausedRestore();
        }

        private IEnumerator ExercisePausedRestore()
        {
            foreach (var completeWhilePaused in new[] { true, false })
            {
                session.Dispose();
                yield return null;
                itemRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                store.Data = new GameSaveData
                {
                    roomAnchorId = "deferred",
                    items = new[] { new ItemSaveData
                        { instanceId = "saved-chair", definitionId = chair.Id, x = 2, z = 2, qw = 1 } }
                };
                var anchors = new DeferredLoadAnchorService();
                session = CreateSession(new HttpLLMProvider(null), anchors);
                var restore = session.RestoreItemsAsync();
                Assert.That(restore.IsCompleted, Is.False);
                session.SetPaused(true);
                if (!completeWhilePaused) session.SetPaused(false);
                anchors.Loaded.SetResult(new Pose(Vector3.zero, Quaternion.identity));
                while (!restore.IsCompleted) yield return null;
                if (completeWhilePaused)
                {
                    Assert.That(session.Items.Count, Is.Zero);
                    session.SetPaused(false);
                }
                for (var frame = 0; frame < 10 && !session.ItemsReady; frame++)
                {
                    session.Tick(.02f);
                    yield return null;
                }
                Assert.That(session.ItemsReady, Is.True, "completion order: " + completeWhilePaused);
                Assert.That(anchors.LoadCalls, Is.EqualTo(2));
                Assert.That(session.ItemViews.Count(), Is.EqualTo(1));
                Assert.That(character.GetComponent<AvatarMotor>().Body.IsPaused, Is.False);
            }
        }

        [UnityTest]
        public IEnumerator RestoringFurnitureAtInitialSpawnRelocatesAvatarWithoutRejectingLayout()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseSpawnRelocation();
        }

        private IEnumerator ExerciseSpawnRelocation()
        {
            session.Dispose();
            yield return null;
            itemRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            store.Data = new GameSaveData
            {
                items = new[] { new ItemSaveData
                    { instanceId = "spawn-table", definitionId = table.Id, qw = 1 } }
            };
            session = CreateSession(new HttpLLMProvider(null));
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;
            Assert.That(session.ItemsReady, Is.True, session.Notice);
            Assert.That(session.ItemViews.Single().Instance.Id, Is.EqualTo("spawn-table"));
            var motor = character.GetComponent<AvatarMotor>();
            Assert.That(Vector3.ProjectOnPlane(motor.Position, Vector3.up).magnitude, Is.GreaterThan(.4f));
            Assert.That(motor.Body.CanOccupy(motor.Body.Pose), Is.True);
            Assert.That(motor.Body.HasSupport(), Is.True);
            Assert.That(motor.Body.IsPaused, Is.False);
        }

        [UnityTest]
        public IEnumerator PendingRestoreRejectsDirectAndConversationCoordinatorActions()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExercisePendingRestoreAdmission();
        }

        private IEnumerator ExercisePendingRestoreAdmission()
        {
            session.Dispose();
            store.Data.roomAnchorId = "deferred";
            var anchors = new DeferredLoadAnchorService();
            session = CreateSession(new HttpLLMProvider(null), anchors);
            var restore = session.RestoreItemsAsync();
            Assert.That(session.IsRestoringItems, Is.True);
            var miniGames = root.AddComponent<MiniGameHost>();
            miniGames.Configure(Array.Empty<MiniGame>());
            miniGames.Bind(session);
            Assert.That(miniGames.TryStart("missing"), Is.False);
            Assert.That(session.Notifications.Hint, Does.Contain("房间网格已就绪"));
            var direct = session.Submit(new AvatarIntent("move", IntentSource.UI, payload: Vector3.right));
            var ai = session.Avatar.Submit(new AvatarIntent("stay", IntentSource.AI));
            Assert.That(direct.State, Is.EqualTo(ActionState.Rejected));
            Assert.That(ai.State, Is.EqualTo(ActionState.Rejected));
            Assert.That(character.GetComponent<AvatarMotor>().Body.IsPaused, Is.True);
            anchors.Loaded.SetResult(new Pose(Vector3.zero, Quaternion.identity));
            while (!restore.IsCompleted) yield return null;
            Assert.That(session.Submit(new AvatarIntent("stay", IntentSource.UI)).State,
                Is.EqualTo(ActionState.Running));
        }

        [UnityTest]
        public IEnumerator ContactExitUsesClearSupportedSpaceAndRetainsPoseWhenEveryExitIsBlocked()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseBlockedExit();
        }

        private IEnumerator ExerciseBlockedExit()
        {
            Assert.That(Application.isPlaying, Is.True);
            var motor = character.GetComponent<AvatarMotor>();
            var waitUntil = Time.time + .15f;
            while (Time.time < waitUntil) yield return null;
            motor.BeginBodyContact(Vector3.zero, .45f);
            motor.SetContactRoot(new Vector3(2, .01f, 0), Quaternion.identity);
            var fixedUntil = Time.fixedTime + Time.fixedDeltaTime * 2;
            while (Time.fixedTime < fixedUntil) yield return null;
            var blocker = new GameObject("Blocked exit").AddComponent<BoxCollider>();
            blocker.transform.SetParent(root.transform);
            blocker.transform.position = new Vector3(0, .9f, 0);
            blocker.size = new Vector3(3, 1.8f, 3);
            Physics.SyncTransforms();
            var before = motor.Body.Pose;
            Assert.Throws<InvalidOperationException>(() => motor.EndBodyContact());
            Assert.That(motor.Body.IsContact, Is.True);
            Assert.That(session.CharacterContext.Capture().runtime.posture, Is.EqualTo("seated"));
            Assert.That(motor.Body.Pose.position, Is.EqualTo(before.position));
            Assert.That(character.GetComponent<CapsuleCollider>().enabled, Is.True);
            blocker.size = new Vector3(.5f, 1.8f, .5f);
            Physics.SyncTransforms();
            session.Submit(new AvatarIntent("stay", IntentSource.UI));
            session.Tick(.02f);
            Assert.That(motor.Body.IsContact, Is.False);
            Assert.That(motor.Body.CanOccupy(motor.Body.Pose), Is.True);
            Assert.That(motor.Body.HasSupport(), Is.True);
            Assert.That(Vector3.ProjectOnPlane(motor.Position, Vector3.up).magnitude, Is.GreaterThan(.49f));
            Assert.That(motor.Body.Rigidbody.isKinematic, Is.False);
        }

        [UnityTest]
        public IEnumerator RemovingSupportThenPausingRestoresTheRemainingItemUnderGravity()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseRemovedSupport();
        }

        private IEnumerator ExerciseRemovedSupport()
        {
            var place = session.PlaceItemAsync(table.Id, new Pose(new Vector3(2, 0, 2), Quaternion.identity));
            while (!place.IsCompleted) yield return null;
            place = session.PlaceItemAsync(television.Id, new Pose(new Vector3(2, .8f, 2), Quaternion.identity));
            while (!place.IsCompleted) yield return null;
            var support = session.ItemViews.Single(item => item.Definition.Id == table.Id);
            session.RemoveItem(support.Instance.Id);
            session.SetPaused(true);
            Assert.That(store.Data.items.Single().needsGravitySettling, Is.True);
            session.Dispose();
            yield return null;
            session = CreateSession(new HttpLLMProvider(null));
            var restore = session.RestoreItemsAsync();
            while (!restore.IsCompleted) yield return null;
            Assert.That(session.ItemsReady, Is.True, session.Notice);
            var remaining = session.ItemViews.Single();
            yield return EngineTestFrames.Seconds(1);
            Assert.That(remaining.Body.Pose.position.y, Is.EqualTo(0).Within(.03f));
            Assert.That(remaining.Body.HasSupport(), Is.True);
        }

        [UnityTest]
        public IEnumerator CharacterClimbsShallowSlopeUnderGravity()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseSlope();
        }

        private IEnumerator ExerciseSlope()
        {
            var ramp = new GameObject("Slope", typeof(BoxCollider));
            ramp.transform.SetParent(root.transform);
            ramp.transform.position = new Vector3(2, .23f, 0);
            ramp.transform.rotation = Quaternion.Euler(0, 0, 12);
            ramp.GetComponent<BoxCollider>().size = new Vector3(3, .08f, 2);
            Physics.SyncTransforms();
            var motor = character.GetComponent<AvatarMotor>();
            motor.Begin(new[] { motor.Position, new Vector3(3, 0, 0) });
            for (var time = 0f; time < 4 && motor.IsMoving; time += Time.deltaTime)
            {
                motor.Tick(Time.deltaTime);
                yield return null;
            }
            Assert.That(motor.Position.x, Is.GreaterThan(2.6f));
            Assert.That(motor.Position.y, Is.GreaterThan(.3f));
            Assert.That(Vector3.Angle(character.transform.up, Vector3.up), Is.LessThan(1));
        }

        [UnityTest]
        public IEnumerator CharacterCannotWalkThroughAnUnmappedWall()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseUnmappedWall();
        }

        private IEnumerator ExerciseUnmappedWall()
        {
            var extendedFloor = new GameObject("Extended hard-wall test floor")
                .AddComponent<BoxCollider>();
            extendedFloor.transform.SetParent(root.transform);
            extendedFloor.transform.position = new Vector3(0, -.05f, 0);
            extendedFloor.size = new Vector3(100, .1f, 100);
            var wall = new GameObject("Unmapped wall").AddComponent<BoxCollider>();
            wall.transform.SetParent(root.transform);
            wall.transform.position = new Vector3(1.7f, 1, 0);
            wall.size = new Vector3(.08f, 2, 40);
            Physics.SyncTransforms();
            var handle = session.Submit(
                new AvatarIntent("move", IntentSource.UI, payload: new Vector3(3, 0, 0)));
            for (var time = 0f; time < 16 && !handle.IsFinished; time += Time.deltaTime)
            {
                session.Tick(Time.deltaTime);
                yield return null;
            }
            yield return null;
            var motor = character.GetComponent<AvatarMotor>();
            Assert.That(session.Motor.Position.x, Is.LessThan(1.47f));
            Assert.That(handle.State, Is.EqualTo(ActionState.Failed));
            Assert.That(handle.Error, Does.Contain("blocked by an obstacle"));
            Assert.That(motor.IsMoving, Is.False);
            Assert.That(animator.GetFloat("Speed"), Is.Zero);
            Assert.That(animator.GetCurrentAnimatorClipInfo(0).Any(info =>
                info.clip != null && info.clip.name == "lengyue_clip_idle01_combined" &&
                info.weight > .99f), Is.True);
            Assert.That(character.GetComponent<PhysicsBody>().Rigidbody.isKinematic, Is.False);
            Assert.That(character.GetComponent<CharacterController>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator CharacterLocallyAvoidsNonCarvingNavObstacle()
        {
            yield return new EnterPlayMode();
            yield return SetUp();

            var obstacleObject = new GameObject("Dynamic local obstacle");
            obstacleObject.transform.SetParent(root.transform);
            obstacleObject.transform.position = new Vector3(1.5f, 1f, 0);
            var collider = obstacleObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(.4f, 2f, .8f);
            var obstacle = obstacleObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            obstacle.size = collider.size;
            obstacle.carving = false;
            Physics.SyncTransforms();
            yield return null;

            var handle = session.Submit(
                new AvatarIntent("move", IntentSource.UI, payload: new Vector3(3, 0, 0)));
            var maximumLateralDeviation = 0f;
            for (var time = 0f; time < 8 && !handle.IsFinished; time += Time.deltaTime)
            {
                session.Tick(Time.deltaTime);
                maximumLateralDeviation = Mathf.Max(maximumLateralDeviation,
                    Mathf.Abs(session.Motor.Position.z));
                yield return null;
            }

            Assert.That(handle.State, Is.EqualTo(ActionState.Succeeded), handle.Error);
            Assert.That(maximumLateralDeviation, Is.GreaterThan(.45f));
            Assert.That(session.Motor.Position.x, Is.GreaterThan(2.8f));
        }

        [UnityTest]
        public IEnumerator CharacterLocallyAvoidsUnregisteredPhysicsCollider()
        {
            yield return new EnterPlayMode();
            yield return SetUp();

            var obstacle = new GameObject("Physics-only local obstacle");
            obstacle.transform.SetParent(root.transform);
            obstacle.transform.position = new Vector3(1.5f, 1f, 0);
            var collider = obstacle.AddComponent<BoxCollider>();
            collider.size = new Vector3(.4f, 2f, .8f);
            Physics.SyncTransforms();

            var handle = session.Submit(
                new AvatarIntent("move", IntentSource.UI, payload: new Vector3(3, 0, 0)));
            var maximumLateralDeviation = 0f;
            for (var time = 0f; time < 12 && !handle.IsFinished; time += Time.deltaTime)
            {
                session.Tick(Time.deltaTime);
                maximumLateralDeviation = Mathf.Max(maximumLateralDeviation,
                    Mathf.Abs(session.Motor.Position.z));
                yield return null;
            }

            Assert.That(handle.State, Is.EqualTo(ActionState.Succeeded), handle.Error);
            Assert.That(maximumLateralDeviation, Is.GreaterThan(.45f));
            Assert.That(session.Motor.Position.x, Is.GreaterThan(2.8f));
        }

        [UnityTest]
        public IEnumerator HudDeletesFocusedItemAndPersistsRemainingItems()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseHudDeletion();
        }

        private IEnumerator ExerciseHudDeletion()
        {
            var firstPlacement = session.PlaceItemAsync(table.Id,
                new Pose(new Vector3(-2, 0, 2), Quaternion.identity));
            while (!firstPlacement.IsCompleted) yield return null;
            var secondPlacement = session.PlaceItemAsync(chair.Id,
                new Pose(new Vector3(2, 0, 2), Quaternion.identity));
            while (!secondPlacement.IsCompleted) yield return null;
            var items = session.ItemViews.ToArray();
            Assert.That(items.Length, Is.EqualTo(2), session.Notice);
            var focused = items[1];
            var focusedId = focused.Instance.Id;
            var retainedId = items[0].Instance.Id;

            var host = new GameObject("HUD Delete Test");
            host.transform.SetParent(root.transform);
            var game = host.AddComponent<GameBootstrap>();
            game.enabled = false;
            game.Configure(null, null, new[] { lamp, table, chair });
            typeof(GameBootstrap).GetProperty(nameof(GameBootstrap.Session))
                ?.SetValue(game, session);
            var focus = host.AddComponent<VirtualItemFocusController>();
            // Supply a stable focus input for this HUD test; actual ray resolution is
            // exercised by CatalogPlacementGrabAndFocusUseRegisteredDefinitions.
            focus.enabled = false;
            typeof(VirtualItemFocusController).GetField("focused",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(focus, focused);
            var hud = host.AddComponent<GameHud>();
            hud.Configure(game);
            yield return null;
            typeof(VirtualItemFocusController).GetField("focused",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(focus, focused);
            typeof(GameHud).GetField("focusController",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(hud, focus);

            typeof(GameHud).GetMethod("RemoveSelectedItem",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(hud, null);
            yield return null;

            Assert.That(focus.RecycleSelectionActive, Is.True);
            Assert.That(focused.IsRecycleFocused, Is.True);
            Assert.That(focused.RecycleFocusMaterial, Is.Not.Null);
            Assert.That(focused.RecycleFocusMaterial.GetFloat("_OutlineWidth"),
                Is.EqualTo(.007f).Within(.0001f));
            Assert.That(focused.RecycleFocusMaterial.GetFloat("_Intensity"),
                Is.EqualTo(1.25f).Within(.001f));
            Assert.That(focused.GetComponentsInChildren<Renderer>(true)
                .All(renderer => renderer.sharedMaterials.Contains(
                    focused.RecycleFocusMaterial)), Is.True);
            Assert.That(session.Items.TryGet(focusedId, out _), Is.True);

            typeof(GameHud).GetMethod("RemoveSelectedItem",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(hud, null);
            yield return null;

            Assert.That(session.Items.TryGet(focusedId, out _), Is.False);
            Assert.That(session.Items.TryGet(retainedId, out _), Is.True);
            Assert.That(store.Data.items.Select(item => item.instanceId),
                Is.EqualTo(new[] { retainedId }));
            Assert.That(focus.RecycleSelectionActive, Is.False);
        }

        [UnityTest]
        public IEnumerator BottomHudExpandsVerticalInteractionActivities()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            yield return ExerciseHud();
        }

        private IEnumerator ExerciseHud()
        {
            var host = new GameObject("HUD Layout Test");
            var game = host.AddComponent<GameBootstrap>();
            game.enabled = false;
            var productionCatalog = LoadProductionCatalog();
            game.Configure(null, null, productionCatalog);
            typeof(GameBootstrap).GetProperty(nameof(GameBootstrap.Session))
                ?.SetValue(game, session);
            var hud = host.AddComponent<GameHud>();
            hud.enabled = false;
            hud.Configure(game);
            var insecureHttpOption = PlayerSettings.insecureHttpOption;
            try
            {
                PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
                typeof(GameHud).GetMethod("Start",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(hud, null);
                typeof(GameHud).GetMethod("EnsurePersonalityControls",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(hud, new object[] { session });
            }
            finally
            {
                PlayerSettings.insecureHttpOption = insecureHttpOption;
            }
            yield return null;

            var hudCanvas = host.GetComponentsInChildren<Canvas>(true)
                .Single(value => value.name == "MateSoul HUD");
            Assert.That(hudCanvas.sortingOrder, Is.EqualTo(32760));
            var nonOverlayGraphics = hudCanvas.GetComponentsInChildren<Graphic>(true)
                .Where(graphic => !(graphic is TMP_Text) && !(graphic is TMP_SubMeshUI))
                .Where(graphic => graphic.material.shader.name != "MateSoul/UI Always On Top")
                .Select(graphic => $"{graphic.name}:{graphic.material.shader.name}").ToArray();
            Assert.That(nonOverlayGraphics, Is.Empty,
                "HUD graphics must all render through the always-on-top shader.");
            var hudText = hudCanvas.GetComponentsInChildren<TMP_Text>(true);
            Assert.That(hudText, Is.Not.Empty);
            Assert.That(hudText.All(text =>
                text.font.name == "MateSoul UI SDF" &&
                text.font.atlasPopulationMode == AtlasPopulationMode.Static &&
                text.font.atlasWidth == 2048 &&
                text.font.atlasHeight == 2048 &&
                text.isOverlay &&
                text.fontSharedMaterial.mainTexture.width == 2048 &&
                text.fontSharedMaterial.mainTexture.height == 2048 &&
                text.materialForRendering.shader.name ==
                AlwaysOnTopUi.TextShaderName), Is.True);
            var atlas = hudText[0].font.atlasTextures[0];
            Assert.That(atlas.width, Is.EqualTo(2048));
            Assert.That(atlas.height, Is.EqualTo(2048));
            Assert.That(hudText[0].font.characterTable.Any(character =>
                character.unicode == '对'), Is.True);
            Assert.That(hudText[0].font.characterTable.Any(character =>
                character.unicode == '话'), Is.True);
            Assert.That(hudText[0].font.HasCharacters("龄球与轮流滚每人倒瓶得分"), Is.True);
            Assert.That(hudText[0].font.HasCharacters(
                UiFontAssetBuilder.CollectRequiredCharacters(), out _, false, false), Is.True);
            Assert.That(productionCatalog.All(definition =>
                hudText[0].font.HasCharacters(
                    definition.DisplayName, out _, false, false)), Is.True);
            Assert.That(session.Persona.Traits.All(trait =>
                hudText[0].font.HasCharacters(
                    trait.display_name, out _, false, false) &&
                trait.options.All(option =>
                    hudText[0].font.HasCharacters(
                        option.display_name, out _, false, false))), Is.True);
            Assert.That(hudText[0].font.HasCharacters(
                "‹›“”「」→～∞", out _, false, false), Is.True);
            Assert.That(hudText[0].font.HasCharacters(
                "展示隐藏键双握拳", out _, false, false), Is.True);
            var dynamicFallback = hudText[0].font.fallbackFontAssetTable.Single();
            Assert.That(dynamicFallback.atlasPopulationMode,
                Is.EqualTo(AtlasPopulationMode.Dynamic));
            Assert.That(dynamicFallback.sourceFontFile, Is.Not.Null);
            Assert.That(dynamicFallback.isMultiAtlasTexturesEnabled, Is.True);
            Assert.That(TMP_Settings.defaultFontAsset, Is.SameAs(hudText[0].font));
            Assert.That(TMP_Settings.fallbackFontAssets, Does.Contain(dynamicFallback));
            Assert.That(hudText[0].font.HasCharacters(
                "龘曌喆锚恢复HTTPS", out _, true, true), Is.True);
            var navigationText = hudText.Where(text => text.name == "功能名称").ToArray();
            Assert.That(navigationText, Has.Length.EqualTo(5));
            foreach (var text in navigationText)
            {
                text.ForceMeshUpdate(true, true);
                Assert.That(text.textInfo.characterCount, Is.EqualTo(text.text.Length),
                    $"{text.text} did not fit its TMP text container.");
            }
            var visibilityHint = hudText.Single(text => text.name == "展示隐藏UI提示");
            Assert.That(visibilityHint.text, Does.StartWith("展示/隐藏UI："));
            Assert.That(visibilityHint.text, Does.Contain("B 键"));
            visibilityHint.ForceMeshUpdate(true, true);
            Assert.That(visibilityHint.textInfo.characterCount,
                Is.EqualTo(visibilityHint.text.Length));
            const System.Reflection.BindingFlags privateInstance =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic;
            var controllerToggle = (UnityEngine.InputSystem.InputAction)typeof(GameHud)
                .GetField("controllerHudToggle", privateInstance)?.GetValue(hud);
            var leftFist = (UnityEngine.InputSystem.InputAction)typeof(GameHud)
                .GetField("leftHandFist", privateInstance)?.GetValue(hud);
            var rightFist = (UnityEngine.InputSystem.InputAction)typeof(GameHud)
                .GetField("rightHandFist", privateInstance)?.GetValue(hud);
            Assert.That(controllerToggle, Is.Not.Null);
            Assert.That(controllerToggle.bindings.Single().path,
                Is.EqualTo("<XRController>{RightHand}/secondaryButton"));
            Assert.That(leftFist, Is.Not.Null);
            Assert.That(leftFist.bindings.Single().path,
                Is.EqualTo("<PicoHandInteraction>{LeftHand}/graspFirm"));
            Assert.That(rightFist, Is.Not.Null);
            Assert.That(rightFist.bindings.Single().path,
                Is.EqualTo("<PicoHandInteraction>{RightHand}/graspFirm"));
            var hudScaler = hudCanvas.GetComponent<CanvasScaler>();
            Assert.That(hudScaler.dynamicPixelsPerUnit, Is.EqualTo(8f));
            var trackedRaycaster = hudCanvas.GetComponent<TrackedDeviceGraphicRaycaster>();
            Assert.That(trackedRaycaster, Is.Not.Null);
            Assert.That(trackedRaycaster.checkFor2DOcclusion, Is.False);
            Assert.That(trackedRaycaster.checkFor3DOcclusion, Is.False);

            var notificationSource = new SystemNotifications();
            var notifications = host.AddComponent<SystemNotificationPresenter>();
            notifications.Bind(notificationSource);
            notificationSource.ShowHint("较早消息", 0);
            notificationSource.ShowHint("最新消息", 0);
            yield return null;
            var notificationCanvas = host.GetComponentsInChildren<Canvas>(true)
                .Single(value => value.name == "System Notifications");
            Assert.That(notificationCanvas.sortingOrder, Is.EqualTo(32760));
            var hintPanels = notificationCanvas.GetComponentsInChildren<RectTransform>(true)
                .Where(value => value.name.StartsWith("系统提示/", StringComparison.Ordinal))
                .OrderByDescending(value => value.anchoredPosition.y)
                .ToArray();
            Assert.That(hintPanels, Has.Length.EqualTo(2));
            Assert.That(hintPanels[0].GetComponentInChildren<TMP_Text>().text,
                Is.EqualTo("最新消息"));
            Assert.That(hintPanels[1].GetComponentInChildren<TMP_Text>().text,
                Is.EqualTo("较早消息"));
            Assert.That(hintPanels[0].GetComponent<CanvasGroup>().alpha,
                Is.GreaterThan(0).And.LessThanOrEqualTo(1));
            Assert.That(notificationCanvas.GetComponentsInChildren<Graphic>(true)
                .Where(graphic => !(graphic is TMP_Text))
                .All(graphic => graphic.material.shader.name == "MateSoul/UI Always On Top"), Is.True);
            Assert.That(notificationCanvas.GetComponentsInChildren<TMP_Text>(true)
                .All(text => text.font.name == "MateSoul UI SDF" && text.isOverlay), Is.True);

            var buttons = host.GetComponentsInChildren<Button>(true);
            foreach (var title in new[] { "对话", "互动", "信息", "虚拟物品", "设置" })
                Assert.That(buttons.Count(button => button.name == title), Is.EqualTo(1), title);
            var categories = new[]
            {
                ItemCategory.Furniture,
                ItemCategory.Appliance,
                ItemCategory.Tableware,
                ItemCategory.Kitchenware,
                ItemCategory.Food,
                ItemCategory.Toy
            };
            Assert.That(buttons.Where(button =>
                    button.name.StartsWith("物品分类/", StringComparison.Ordinal))
                .Select(button => button.name),
                Is.EquivalentTo(categories.Select(category =>
                    "物品分类/" + category.GetDisplayName())));
            var catalogIds = new System.Collections.Generic.HashSet<string>();
            foreach (var category in categories)
            {
                buttons.Single(button =>
                    button.name == "物品分类/" + category.GetDisplayName()).onClick.Invoke();
                var expectedIds = productionCatalog.Where(item => item.Category == category)
                    .Select(item => item.Id).ToArray();
                var categoryIds = new System.Collections.Generic.HashSet<string>();
                var pageCount = Mathf.Max(1, (expectedIds.Length + 5) / 6);
                for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    foreach (var button in buttons.Where(button =>
                                 button.gameObject.activeSelf &&
                                 button.name.StartsWith("物品/", StringComparison.Ordinal) &&
                                 button.name != "物品/上一页" && button.name != "物品/下一页"))
                        categoryIds.Add(button.name.Substring("物品/".Length));
                    buttons.Single(button => button.name == "物品/下一页").onClick.Invoke();
                }
                Assert.That(categoryIds, Is.EquivalentTo(expectedIds), category.GetDisplayName());
                catalogIds.UnionWith(categoryIds);
            }
            Assert.That(catalogIds, Is.EquivalentTo(ProductionCatalogIds));
            Assert.That(host.GetComponentsInChildren<TMP_Text>(true)
                .Count(text => text.name == "剩余数量"), Is.EqualTo(6));
            Assert.That(buttons.Count(button => button.name == "回收当前物品"), Is.EqualTo(1));
            Assert.That(buttons.Count(button =>
                button.name.StartsWith("性格/", StringComparison.Ordinal)), Is.EqualTo(5));
            var oldExtraversion = session.Persona.SelectedOption("extraversion");
            buttons.Single(button => button.name == "性格/extraversion").onClick.Invoke();
            Assert.That(session.Persona.SelectedOption("extraversion"), Is.Not.EqualTo(oldExtraversion));
            var subtitle = host.GetComponentsInChildren<Transform>(true)
                .Single(value => value.name == "角色字幕");
            Assert.That(subtitle.GetComponent<TMP_Text>(), Is.Not.Null);
            Assert.That(subtitle.GetComponent<Image>(), Is.Null,
                "Character speech must be direct text without a bubble or text-box background.");
            var navigation = host.GetComponentsInChildren<Transform>(true)
                .Single(value => value.name == "Bottom Navigation").GetComponent<RectTransform>();
            var navigationButtons = new[] { "对话", "互动", "信息", "虚拟物品", "设置" }
                .Select(title => navigation.Find(title).GetComponent<RectTransform>()).ToArray();
            Assert.That(navigationButtons.All(button =>
                visibilityHint.rectTransform.anchoredPosition.y <
                button.anchoredPosition.y - button.sizeDelta.y), Is.True,
                "The HUD visibility hint must remain below the navigation buttons.");
            var subtitleRect = subtitle.GetComponent<RectTransform>();
            var content = host.GetComponentsInChildren<Transform>(true)
                .Single(value => value.name == "HUD Content").GetComponent<RectTransform>();
            var navigationTop = navigation.anchoredPosition.y +
                                navigation.sizeDelta.y * (1 - navigation.pivot.y);
            var subtitleBottom = subtitleRect.anchoredPosition.y -
                                 subtitleRect.sizeDelta.y * subtitleRect.pivot.y;
            var subtitleTop = subtitleRect.anchoredPosition.y +
                              subtitleRect.sizeDelta.y * (1 - subtitleRect.pivot.y);
            var contentBottom = content.anchoredPosition.y -
                                content.sizeDelta.y * content.pivot.y;
            Assert.That(subtitleBottom, Is.GreaterThan(navigationTop));
            Assert.That(contentBottom, Is.GreaterThan(subtitleTop));

            var focusController = host.GetComponent<VirtualItemFocusController>();
            var hudRoot = host.GetComponentsInChildren<Transform>(true)
                .Single(value => value.name == "Bottom HUD").gameObject;
            var setHudVisible = typeof(GameHud).GetMethod(
                "SetHudVisible", privateInstance);
            Assert.That(setHudVisible, Is.Not.Null);
            Assert.That(hud.IsVisible, Is.True);
            Assert.That(hudRoot.activeSelf, Is.True);
            Assert.That(focusController.enabled, Is.True);
            setHudVisible.Invoke(hud, new object[] { false });
            Assert.That(hud.IsVisible, Is.False);
            Assert.That(hudRoot.activeSelf, Is.False);
            Assert.That(focusController.enabled, Is.True,
                "Interactive-component focus must remain available while the HUD is hidden.");
            setHudVisible.Invoke(hud, new object[] { true });
            Assert.That(hud.IsVisible, Is.True);
            Assert.That(hudRoot.activeSelf, Is.True);
            Assert.That(focusController.enabled, Is.True);

            Assert.That(host.GetComponentsInChildren<Transform>(true)
                .Any(value => value.name == "对话栏目"), Is.False,
                "Dialogue must not reuse the large HUD content panel.");
            var dialogueBar = host.GetComponentsInChildren<Transform>(true)
                .Single(value => value.name == "Dialogue Bar").GetComponent<RectTransform>();
            var dialogueGroup = dialogueBar.GetComponent<CanvasGroup>();
            Assert.That(dialogueBar.gameObject.activeSelf, Is.False);
            var dialogueButton = buttons.Single(button => button.name == "对话");
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left
            };
            ExecuteEvents.Execute(dialogueButton.gameObject, pointer,
                ExecuteEvents.pointerClickHandler);
            for (var elapsed = 0f; elapsed < .7f; elapsed += Time.unscaledDeltaTime)
                yield return null;
            yield return null;
            Assert.That(hud.IsDialogueOpen, Is.True);
            Assert.That(session.DialogueAttentionRequested, Is.True);
            Assert.That(dialogueBar.gameObject.activeSelf, Is.True);
            Assert.That(dialogueGroup.alpha, Is.EqualTo(1).Within(.001f));
            Assert.That(dialogueBar.localScale.x, Is.EqualTo(1).Within(.001f));
            Assert.That(navigation.gameObject.activeSelf, Is.False);
            var glass = dialogueBar.GetComponent<Image>().color;
            Assert.That(glass.r, Is.EqualTo(.72f).Within(.01f));
            Assert.That(glass.b, Is.EqualTo(.86f).Within(.01f));
            Assert.That(glass.a, Is.EqualTo(.68f).Within(.01f));
            var send = buttons.Single(button => button.name == "发送");
            var close = buttons.Single(button => button.name == "关闭");
            Assert.That(((RectTransform)close.transform).anchoredPosition.x,
                Is.GreaterThan(((RectTransform)send.transform).anchoredPosition.x));

            send.onClick.Invoke();
            yield return null;
            Assert.That(hud.IsDialogueOpen, Is.True,
                "Sending must not dismiss the dialogue bar.");
            close.onClick.Invoke();
            for (var elapsed = 0f; elapsed < .65f; elapsed += Time.unscaledDeltaTime)
                yield return null;
            yield return null;
            Assert.That(hud.IsDialogueOpen, Is.False);
            Assert.That(session.DialogueAttentionRequested, Is.False);
            Assert.That(dialogueBar.gameObject.activeSelf, Is.False);
            Assert.That(navigation.gameObject.activeSelf, Is.True);
            Assert.That(navigation.GetComponent<CanvasGroup>().alpha,
                Is.EqualTo(1).Within(.001f));

            var interaction = buttons.Single(button => button.name == "互动");
            ExecuteEvents.Execute(interaction.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            yield return null;

            var page = host.GetComponentsInChildren<Transform>(true)
                .Single(value => value.name == "互动栏目");
            Assert.That(page.gameObject.activeInHierarchy, Is.True);
            var activities = page.GetComponentsInChildren<Button>(true)
                .Where(button => button.name.StartsWith("活动/", StringComparison.Ordinal)).ToArray();
            Assert.That(activities.Select(button => button.name), Is.EqualTo(new[]
            {
                "活动/一起散步", "活动/坐下聊天", "活动/躺下休息", "活动/起身",
                "活动/切水果", "活动/小游戏"
            }));
            Assert.That(page.GetComponentsInChildren<Button>(true)
                .Count(button => button.name == "取消切水果"), Is.EqualTo(1));
            for (var i = 1; i < activities.Length; i++)
                Assert.That(((RectTransform)activities[i].transform).anchoredPosition.y,
                    Is.LessThan(((RectTransform)activities[i - 1].transform).anchoredPosition.y));

            ExecuteEvents.Execute(activities[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
            var feedback = page.GetComponentsInChildren<TMP_Text>(true)
                .Single(value => value.name == "互动反馈");
            Assert.That(feedback.text, Does.Contain("测试按钮已响应"));

            ExecuteEvents.Execute(interaction.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Assert.That(page.gameObject.activeInHierarchy, Is.False);
            Object.Destroy(host);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ProductionCatalogContainsEveryStandaloneProp()
        {
            yield return new EnterPlayMode();
            yield return SetUp();
            var catalog = LoadProductionCatalog();
            Assert.That(catalog.Select(item => item.Id), Is.EqualTo(ProductionCatalogIds));
            Assert.That(catalog.All(item => item.CatalogVisible), Is.True);
            Assert.That(catalog.All(item => item.Prefab != null), Is.True);
            Assert.That(catalog.All(item =>
                    item.PlacementPreview == item.ImmovableAfterPlacement), Is.True,
                "Only immovable catalog items may expose a confirmation projection.");
            Assert.That(catalog.All(item => item.Prefab.GetComponent<PhysicsBody>() != null), Is.True);
            Assert.That(catalog.All(item => item.Prefab.GetComponentsInChildren<Collider>(true).Length > 0),
                Is.True);
            AssertCategory(catalog, ItemCategory.Furniture, "table", "water-dispenser", "chair");
            AssertCategory(catalog, ItemCategory.Appliance,
                "television", "radio", "clock", "remote-control");
            AssertCategory(catalog, ItemCategory.Tableware,
                "plate", "plate-medium", "plate-large", "mug",
                "bowl", "bowl-medium", "bowl-large", "spoon", "chopsticks");
            AssertCategory(catalog, ItemCategory.Kitchenware,
                "knife", "frying-pan", "coffee-bean-jar", "coffee-grinder");
            AssertCategory(catalog, ItemCategory.Food,
                "raw-steak", "cupcake", "cookie", "apple", "carrot");
            AssertCategory(catalog, ItemCategory.Toy,
                "playing-cards", "dartboard", "bowling-ball", "bowling-pin", "plush-doll");
            Assert.That(catalog.Single(item => item.Id == "knife").DisplayName, Is.EqualTo("刀"));
            Assert.That(catalog.Single(item => item.Id == "table").InventoryLimit, Is.EqualTo(1));
            Assert.That(catalog.Single(item => item.Id == "table").FixedAfterPlacement, Is.True);
            Assert.That(catalog.Single(item => item.Id == "table").ImmovableAfterPlacement,
                Is.True);
            Assert.That(catalog.Single(item => item.Id == "table").PlacementPreview, Is.True);
            Assert.That(catalog.Single(item => item.Id == "table").AllowSurfacePlacement, Is.False);
            var waterDispenser = catalog.Single(item => item.Id == "water-dispenser");
            Assert.That(waterDispenser.InventoryLimit, Is.EqualTo(1));
            Assert.That(waterDispenser.FixedAfterPlacement, Is.True);
            Assert.That(waterDispenser.ImmovableAfterPlacement, Is.True);
            Assert.That(waterDispenser.PlacementPreview, Is.True);
            Assert.That(waterDispenser.AllowSurfacePlacement, Is.False);
            foreach (var id in new[]
                     {
                         "table", "water-dispenser", "chair", "television",
                         "clock", "dartboard"
                     })
            {
                var fixedItem = catalog.Single(item => item.Id == id);
                Assert.That(fixedItem.InventoryLimit, Is.EqualTo(1), id);
                Assert.That(fixedItem.FixedAfterPlacement, Is.True, id);
                Assert.That(fixedItem.ImmovableAfterPlacement, Is.True, id);
                Assert.That(fixedItem.PlacementPreview, Is.True, id);
            }
            foreach (var id in new[] { "clock", "dartboard" })
                Assert.That(catalog.Single(item => item.Id == id).PlacementSurface,
                    Is.EqualTo(ItemPlacementSurface.Wall), id);
            foreach (var id in new[] { "television", "radio" })
            {
                var frontFacing = catalog.Single(item => item.Id == id);
                Assert.That(frontFacing.PlacementFacingYaw, Is.EqualTo(180f), id);
                Assert.That(Vector3.Dot(
                        frontFacing.ResolvePlacementRotation(Vector3.back) * Vector3.back,
                        Vector3.back),
                    Is.GreaterThan(.99f), id);
            }
            foreach (var id in new[] { "cupcake", "frying-pan", "apple", "carrot", "plate" })
            {
                var movable = catalog.Single(item => item.Id == id);
                Assert.That(movable.ImmovableAfterPlacement, Is.False, id);
                Assert.That(movable.PlacementPreview, Is.False, id);
            }
            foreach (var id in new[]
                     {
                         "plate", "plate-medium", "plate-large",
                         "bowl", "bowl-medium", "bowl-large",
                         "spoon", "chopsticks", "mug"
                     })
                Assert.That(catalog.Single(item => item.Id == id).InventoryLimit, Is.EqualTo(2), id);
            Assert.That(catalog.Single(item => item.Id == "coffee-bean-jar")
                .InventoryLimit, Is.EqualTo(2));
            var tablewareSizes = new[]
            {
                new
                {
                    Id = "plate", Name = "小号盘子", Footprint = .2f,
                    Height = .025f, Mass = .42f
                },
                new
                {
                    Id = "plate-medium", Name = "中号盘子", Footprint = .25f,
                    Height = .03125f, Mass = .82f
                },
                new
                {
                    Id = "plate-large", Name = "大号盘子", Footprint = .3f,
                    Height = .0375f, Mass = 1.42f
                },
                new
                {
                    Id = "bowl", Name = "小号碗", Footprint = .138f,
                    Height = .07f, Mass = .31f
                },
                new
                {
                    Id = "bowl-medium", Name = "中号碗", Footprint = .18f,
                    Height = .091304f, Mass = .69f
                },
                new
                {
                    Id = "bowl-large", Name = "大号碗", Footprint = .22f,
                    Height = .111594f, Mass = 1.26f
                }
            };
            foreach (var expected in tablewareSizes)
            {
                var definition = catalog.Single(item => item.Id == expected.Id);
                Assert.That(definition.DisplayName, Is.EqualTo(expected.Name), expected.Id);
                Assert.That(definition.Footprint.x,
                    Is.EqualTo(expected.Footprint).Within(.00001f), expected.Id);
                Assert.That(definition.Footprint.y,
                    Is.EqualTo(expected.Footprint).Within(.00001f), expected.Id);
                Assert.That(definition.PlacementHeight,
                    Is.EqualTo(expected.Height).Within(.00001f), expected.Id);
                Assert.That(definition.Physics.mass,
                    Is.EqualTo(expected.Mass).Within(.00001f), expected.Id);
                Assert.That(definition.AllowSurfacePlacement, Is.True, expected.Id);
            }
            Assert.That(catalog.Single(item => item.Id == "apple").InventoryLimit, Is.EqualTo(5));
            Assert.That(catalog.Single(item => item.Id == "apple").RestockDaily, Is.True);
            Assert.That(catalog.Single(item => item.Id == "carrot").InventoryLimit, Is.EqualTo(5));
            Assert.That(catalog.Single(item => item.Id == "carrot").RestockDaily, Is.True);
            var rawSteak = catalog.Single(item => item.Id == "raw-steak");
            Assert.That(rawSteak.UnlimitedInventory, Is.True);
            Assert.That(rawSteak.InventoryLimit, Is.Zero);
            foreach (var id in new[]
                     {
                         "playing-cards", "radio", "plush-doll", "dartboard", "television",
                         "knife", "clock", "table", "water-dispenser"
                     })
                Assert.That(catalog.Single(item => item.Id == id).InventoryLimit, Is.EqualTo(1), id);
            foreach (var path in new[] { "Assets/MateSoul/Content/CompanionLamp.asset" })
            {
                var removed = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                Assert.That(removed, Is.Not.Null, path);
                Assert.That(removed.CatalogVisible, Is.False);
                Assert.That(removed.InventoryLimit, Is.Zero);
            }
            Assert.That(AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    "Assets/MateSoul/Content/CompanionLamp.asset").Category,
                Is.EqualTo(ItemCategory.Appliance));
            Assert.That(AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    "Assets/MateSoul/Content/Props/Chair.asset").Category,
                Is.EqualTo(ItemCategory.Furniture));
            AssertCatalogBreakables(catalog);

            var chopsticks = catalog.Single(item => item.Id == "chopsticks");
            Assert.That(chopsticks.IsPlacementGroup, Is.True);
            Assert.That(chopsticks.PlacementParts, Has.Length.EqualTo(2));
            var chopstickParts = chopsticks.PlacementParts
                .Select(part => part.Definition).ToArray();
            Assert.That(chopstickParts, Has.None.Null);
            Assert.That(chopstickParts.Select(item => item.Id),
                Is.EquivalentTo(new[] { "chopstick-left", "chopstick-right" }));
            Assert.That(chopstickParts.All(item => !item.CatalogVisible), Is.True);
            Assert.That(chopstickParts.All(item => item.Physics.mass == .02f), Is.True);
            Assert.That(chopstickParts.All(item =>
                item.Prefab.GetComponentsInChildren<Rigidbody>(true).Length == 1), Is.True);
            Assert.That(chopstickParts.All(item =>
                item.Prefab.GetComponentsInChildren<Renderer>(true).Length == 1), Is.True);
            Assert.That(chopstickParts.Select(item =>
                    item.Prefab.GetComponentInChildren<Renderer>(true).name),
                Is.EquivalentTo(new[] { "Chopstick_Left", "Chopstick_Right" }));
            Assert.That(chopsticks.Prefab.GetComponentsInChildren<Renderer>(true),
                Has.Length.EqualTo(1),
                "The group prefab remains a single-stick preview for legacy saves.");

            var fryingPan = catalog.Single(item => item.Id == "frying-pan");
            Assert.That(fryingPan.InventoryLimit, Is.EqualTo(1));
            Assert.That(fryingPan.IsPlacementGroup, Is.True);
            Assert.That(fryingPan.PlacementParts, Has.Length.EqualTo(2));
            Assert.That(fryingPan.PlacementParts.Select(part => part.Definition.Id),
                Is.EquivalentTo(new[] { "frying-pan-body", "spatula" }));
            Assert.That(fryingPan.PlacementParts.All(
                part => !part.Definition.CatalogVisible), Is.True);
            Assert.That(fryingPan.PlacementParts.Single(
                    part => part.Definition.Id == "spatula").LocalPosition.y,
                Is.GreaterThan(.055f));
            Assert.That(fryingPan.PlacementParts.Single(
                    part => part.Definition.Id == "frying-pan-body")
                .Definition.Prefab.GetComponentsInChildren<BoxCollider>(true),
                Has.Length.EqualTo(6));
            var panMaterials = fryingPan.Prefab.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials).Distinct().ToArray();
            Assert.That(panMaterials.Single(material =>
                    material.name == "MAT_Cookware_BrushedStainless")
                .GetFloat("_Metallic"), Is.EqualTo(1f));
            Assert.That(panMaterials.Single(material =>
                    material.name == "MAT_Cookware_BrushedStainless")
                .IsKeywordEnabled("_NORMALMAP"), Is.True);
            Assert.That(rawSteak.Prefab.GetComponentInChildren<Renderer>(true)
                .sharedMaterial.IsKeywordEnabled("_NORMALMAP"), Is.True);

            var clockDefinition = catalog.Single(item => item.Id == "clock");
            var clockObject = Object.Instantiate(clockDefinition.Prefab);
            var clock = clockObject.GetComponent<RealTimeClock>();
            Assert.That(clock, Is.Not.Null);
            var hour = clockObject.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == "Clock_HourHand_Pivot");
            var minute = clockObject.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == "Clock_MinuteHand_Pivot");
            var numerals = clockObject.GetComponentsInChildren<TextMeshPro>(true)
                .Where(text => text.name.StartsWith(
                    "Clock Number ", StringComparison.Ordinal)).ToArray();
            Assert.That(numerals.Select(text => text.text),
                Is.EquivalentTo(new[] { "12", "3", "6", "9" }));
            Assert.That(numerals, Has.Length.EqualTo(4));
            clock.ApplyTime(new DateTime(2026, 10, 15, 3, 15, 0));
            var firstHour = hour.localRotation;
            var firstMinute = minute.localRotation;
            clock.ApplyTime(new DateTime(2026, 10, 15, 3, 15, 59));
            Assert.That(Quaternion.Angle(firstHour, hour.localRotation), Is.LessThan(.1f));
            Assert.That(Quaternion.Angle(firstMinute, minute.localRotation), Is.LessThan(.1f));
            clock.ApplyTime(new DateTime(2026, 10, 15, 3, 16, 0));
            Assert.That(Quaternion.Angle(firstHour, hour.localRotation), Is.LessThan(.1f));
            Assert.That(Quaternion.Angle(firstMinute, minute.localRotation),
                Is.EqualTo(6f).Within(.1f));
            clock.ApplyTime(new DateTime(2026, 10, 15, 3, 20, 0));
            Assert.That(Quaternion.Angle(firstHour, hour.localRotation),
                Is.EqualTo(2.5f).Within(.1f));
            clock.ApplyTime(new DateTime(2026, 10, 15, 4, 15, 0));
            Assert.That(Quaternion.Angle(firstHour, hour.localRotation),
                Is.EqualTo(30f).Within(.1f));
            Assert.That(Quaternion.Angle(firstMinute, minute.localRotation),
                Is.LessThan(.1f));
            Object.Destroy(clockObject);
        }

        private static void AssertCatalogBreakables(ItemDefinition[] catalog)
        {
            var fragileIds = new[]
            {
                "plate", "plate-medium", "plate-large", "mug",
                "bowl", "bowl-medium", "bowl-large"
            };
            foreach (var id in fragileIds)
            {
                var fragile = catalog.SingleOrDefault(item => item.Id == id);
                Assert.That(fragile, Is.Not.Null, id);
                Assert.That(fragile.Breakable.enabled, Is.True, id);
            }
            foreach (var nonFragile in catalog.Where(item => !fragileIds.Contains(item.Id)))
                Assert.That(nonFragile.Breakable.enabled, Is.False, nonFragile.Id);
            foreach (var fragile in catalog.Where(item => item.Breakable.enabled))
            {
                Assert.DoesNotThrow(fragile.Breakable.Validate);
                Assert.That(fragile.Breakable.heavyForceThreshold,
                    Is.GreaterThan(fragile.Breakable.lightForceThreshold));
                Assert.That(fragile.Breakable.heavyFragmentCount,
                    Is.GreaterThan(fragile.Breakable.lightFragmentCount));
            }
            var breakSounds = catalog.Where(item => item.Breakable.enabled)
                .Select(item => item.Breakable.breakSound).Distinct().ToArray();
            Assert.That(breakSounds, Has.Length.EqualTo(1));
            Assert.That(breakSounds[0], Is.Not.Null);
            Assert.That(breakSounds[0].name, Is.EqualTo("CeramicBreak"));
            Assert.That(breakSounds[0].channels, Is.EqualTo(1));
            Assert.That(breakSounds[0].length, Is.EqualTo(2f).Within(.1f));
            var audioImporter = (AudioImporter)AssetImporter.GetAtPath(
                AssetDatabase.GetAssetPath(breakSounds[0]));
            Assert.That(audioImporter, Is.Not.Null);
            Assert.That(audioImporter.forceToMono, Is.True);
            Assert.That(audioImporter.loadInBackground, Is.False);
            Assert.That(audioImporter.defaultSampleSettings.preloadAudioData, Is.True);
            Assert.That(audioImporter.defaultSampleSettings.loadType,
                Is.EqualTo(AudioClipLoadType.DecompressOnLoad));
            Assert.That(audioImporter.defaultSampleSettings.compressionFormat,
                Is.EqualTo(AudioCompressionFormat.PCM));
            foreach (var authored in catalog.Where(item =>
                         item.Id.StartsWith("plate", StringComparison.Ordinal) ||
                         item.Id == "mug"))
            {
                Assert.That(authored.Breakable.lightDebrisPrefab, Is.Not.Null);
                Assert.That(authored.Breakable.heavyDebrisPrefab, Is.Not.Null);
                Assert.That(authored.Breakable.lightDebrisPrefab
                        .GetComponentsInChildren<MeshRenderer>(true),
                    Has.Length.EqualTo(authored.Breakable.lightFragmentCount));
                Assert.That(authored.Breakable.heavyDebrisPrefab
                        .GetComponentsInChildren<MeshRenderer>(true),
                    Has.Length.EqualTo(authored.Breakable.heavyFragmentCount));
                Assert.That(authored.Breakable.lightDebrisPrefab
                    .GetComponentsInChildren<Rigidbody>(true), Is.Empty);
                Assert.That(authored.Breakable.heavyDebrisPrefab
                    .GetComponentsInChildren<Collider>(true), Is.Empty);
            }
            foreach (var bowl in catalog.Where(item =>
                         item.Id.StartsWith("bowl", StringComparison.Ordinal)))
            {
                Assert.That(bowl.Breakable.lightDebrisPrefab, Is.Null, bowl.Id);
                Assert.That(bowl.Breakable.heavyDebrisPrefab, Is.Null, bowl.Id);
            }
        }

        private static void AssertCategory(ItemDefinition[] catalog, ItemCategory category,
            params string[] expectedIds)
        {
            Assert.That(catalog.Where(item => item.Category == category).Select(item => item.Id),
                Is.EquivalentTo(expectedIds), category.GetDisplayName());
        }

        private static ItemDefinition[] LoadProductionCatalog()
        {
            var definitions = AssetDatabase.FindAssets("t:ItemDefinition",
                    new[] { "Assets/MateSoul/Content" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ItemDefinition>)
                .Where(item => item != null && ProductionCatalogIds.Contains(item.Id))
                .ToArray();
            Assert.That(definitions.Select(item => item.Id),
                Is.EquivalentTo(ProductionCatalogIds));
            return ProductionCatalogIds.Select(id => definitions.Single(item => item.Id == id)).ToArray();
        }
    }

    public sealed class LengyueIdleImportTests
    {
        [Test]
        public void ImportedIdleKeepsLegsStationaryWhileUpperBodyRemainsAnimated()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/Characters/LengyueCharacter/Lengyue/Runtime/Animations/" +
                "Idle01/lengyue_clip_idle01_combined.anim");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                GameSceneBuilder.CharacterPath);
            Assert.That(clip, Is.Not.Null);
            Assert.That(prefab, Is.Not.Null);
            var facialBindings = AnimationUtility.GetCurveBindings(clip)
                .Where(binding => binding.type == typeof(SkinnedMeshRenderer) &&
                                  binding.propertyName.StartsWith(
                                      "blendShape.", StringComparison.Ordinal))
                .Select(binding => binding.propertyName)
                .ToArray();
            Assert.That(facialBindings, Has.Length.EqualTo(5));
            Assert.That(facialBindings, Does.Contain(
                "blendShape.Face_Blendshape.face_eye_close_L_BS"));
            Assert.That(facialBindings, Does.Contain(
                "blendShape.Face_Blendshape.face_eye_close_R_BS"));
            Assert.That(facialBindings, Does.Contain(
                "blendShape.Face_Blendshape.face_eyeHappy_L"));
            Assert.That(facialBindings, Does.Contain(
                "blendShape.Face_Blendshape.face_eyeHappy_R"));
            Assert.That(facialBindings, Does.Contain(
                "blendShape.Face_Blendshape.face_smile_BS"));

            var instance = Object.Instantiate(prefab);
            try
            {
                var transforms = instance.GetComponentsInChildren<Transform>(true)
                    .GroupBy(item => item.name)
                    .ToDictionary(group => group.Key, group => group.Single());
                var lowerBodyNames = new[]
                {
                    "lengyue_thigh_l", "lengyue_thigh_r", "lengyue_shin_l", "lengyue_shin_r",
                    "lengyue_foot_l", "lengyue_foot_r", "lengyue_toe_l", "lengyue_toe_r"
                };
                clip.SampleAnimation(instance, 0);
                var initialLowerBody = lowerBodyNames.ToDictionary(
                    name => name, name => transforms[name].localRotation);
                var initialHead = transforms["lengyue_head_01"].localRotation;
                var maximumLowerBodyDelta = 0f;
                var maximumHeadDelta = 0f;

                for (var time = .25f; time <= clip.length; time += .25f)
                {
                    clip.SampleAnimation(instance, Mathf.Min(time, clip.length));
                    foreach (var name in lowerBodyNames)
                        maximumLowerBodyDelta = Mathf.Max(maximumLowerBodyDelta,
                            Quaternion.Angle(initialLowerBody[name],
                                transforms[name].localRotation));
                    maximumHeadDelta = Mathf.Max(maximumHeadDelta,
                        Quaternion.Angle(initialHead,
                            transforms["lengyue_head_01"].localRotation));
                }

                Assert.That(maximumLowerBodyDelta, Is.LessThan(.05f));
                Assert.That(maximumHeadDelta, Is.GreaterThan(5f));

                var face = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Single(item => item.name == "lengyue_face_root");
                int Shape(string suffix) => Enumerable.Range(
                        0, face.sharedMesh.blendShapeCount)
                    .Single(index => face.sharedMesh.GetBlendShapeName(index)
                        .EndsWith(suffix, StringComparison.Ordinal));
                clip.SampleAnimation(instance, 61f / 30f);
                Assert.That(face.GetBlendShapeWeight(
                    Shape("face_eye_close_L_BS")), Is.GreaterThan(99f));
                Assert.That(face.GetBlendShapeWeight(
                    Shape("face_eye_close_R_BS")), Is.GreaterThan(99f));
                clip.SampleAnimation(instance, 135f / 30f);
                Assert.That(face.GetBlendShapeWeight(
                    Shape("face_eyeHappy_L")), Is.GreaterThan(5f));
                Assert.That(face.GetBlendShapeWeight(
                    Shape("face_eyeHappy_R")), Is.GreaterThan(5f));
                Assert.That(face.GetBlendShapeWeight(
                    Shape("face_smile_BS")), Is.GreaterThan(8f));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }

    public sealed class JsonGameSaveTests
    {
        [Test]
        public void VersionOneSaveWithoutPersonaMigratesToDefaultSelections()
        {
            var directory = Path.GetFullPath(Path.Combine(
                Application.dataPath, "../../.artifacts/framework/save-test"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, Guid.NewGuid() + ".json");
            try
            {
                File.WriteAllText(path,
                    "{\"version\":1,\"mood\":{\"meters\":[],\"level\":0," +
                    "\"remainingSeconds\":0},\"items\":[]}");
                var loaded = new JsonGameSaveStore(path).Load();
                Assert.That(loaded.personality, Is.Not.Null);
                Assert.That(loaded.personality.selections, Is.Empty);
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void AtomicReplacementRoundTripsAndInvalidVersionDoesNotOverwrite()
        {
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.artifacts/framework/save-test"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, Guid.NewGuid() + ".json");
            try
            {
                var store = new JsonGameSaveStore(path);
                Assert.That(store.Load().version, Is.EqualTo(1));
                store.Save(new GameSaveData { outfitId = "first" });
                store.Save(new GameSaveData { outfitId = "second" });
                Assert.That(store.Load().outfitId, Is.EqualTo("second"));
                Assert.Throws<InvalidDataException>(() => store.Save(new GameSaveData { version = 99 }));
                Assert.That(store.Load().outfitId, Is.EqualTo("second"));
            }
            finally { File.Delete(path); }
        }
    }
}
