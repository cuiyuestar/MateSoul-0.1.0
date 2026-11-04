using System;
using System.Threading;
using System.Threading.Tasks;
using MateSoul.Core;
using MateSoul.Gameplay;
using NUnit.Framework;

namespace MateSoul.Tests
{
    public sealed class GameplayTests
    {
        [Test]
        public void SameDefinitionHasIndependentInstancesAndReservations()
        {
            using (var registry = new ItemRegistry())
            {
                var a = new ItemInstance("a", "chair", "Chair A", new[] { "seat" });
                var b = new ItemInstance("b", "chair", "Chair B", new[] { "seat" });
                registry.Register(a); registry.Register(b);
                using (var first = registry.Reserve("a", "seat", this))
                {
                    Assert.That(first, Is.Not.Null);
                    Assert.That(registry.Reserve("a", "seat", new object()), Is.Null);
                    using (var second = registry.Reserve("b", "seat", this))
                        Assert.That(second.IsValid, Is.True);
                    a.SetState("used");
                    Assert.That(b.StateId, Is.EqualTo("default"));
                }
                Assert.That(a.IsOccupied, Is.False);
            }
        }

        [Test]
        public void DeletingAndReusingAnIdCannotReviveAnOldReservation()
        {
            using (var registry = new ItemRegistry())
            {
                registry.Register(new ItemInstance("a", "chair", "Chair", new[] { "seat" }));
                var old = registry.Reserve("a", "seat", this);
                registry.Remove("a");
                registry.Register(new ItemInstance("a", "chair", "Other Chair", new[] { "seat" }));
                using (var next = registry.Reserve("a", "seat", new object()))
                {
                    Assert.That(old.IsValid, Is.False);
                    old.Dispose();
                    Assert.That(next.IsValid, Is.True);
                }
            }
        }

        [Test]
        public void UnsupportedCapabilityDoesNotOccupyAnItem()
        {
            using (var registry = new ItemRegistry())
            {
                var item = new ItemInstance("a", "cup", "Cup", new[] { "drink" });
                registry.Register(item);
                Assert.That(registry.Reserve("a", "seat", this), Is.Null);
                Assert.That(item.IsOccupied, Is.False);
            }
        }

        [Test]
        public void NegativeMoodReplacesPositiveAndClearsOnlyTheExitingMeter()
        {
            var mood = new MoodSystem();
            mood.Apply("happy", 40);
            mood.Apply("sweet", 20);
            mood.Apply("anger", 90);
            Assert.That(mood.ActiveId, Is.EqualTo("anger"));
            Assert.That(mood.GetValue("happy"), Is.Zero);
            Assert.That(mood.GetValue("sweet"), Is.EqualTo(20));
            mood.Apply("sadness", 90);
            Assert.That(mood.ActiveId, Is.EqualTo("sadness"));
            Assert.That(mood.GetValue("anger"), Is.Zero);
        }

        [Test]
        public void TimedDowngradeKeepsMeterButFinalExitClearsIt()
        {
            var mood = new MoodSystem();
            mood.Apply("sadness", 90);
            mood.Tick(40, false);
            Assert.That(mood.Level, Is.EqualTo(MoodLevel.Light));
            Assert.That(mood.GetValue("sadness"), Is.EqualTo(90));
            mood.Tick(60, false);
            Assert.That(mood.Level, Is.EqualTo(MoodLevel.None));
            Assert.That(mood.GetValue("sadness"), Is.Zero);
        }

        [Test]
        public void DueTransitionWaitsForBodyControlAndOldTimerDoesNotAffectNewMood()
        {
            var mood = new MoodSystem();
            mood.Apply("happy", 90);
            mood.Tick(15, true);
            Assert.That(mood.Level, Is.EqualTo(MoodLevel.High));
            mood.Apply("anger", 90);
            Assert.That(mood.RemainingSeconds, Is.EqualTo(40));
            mood.Tick(.1f, false);
            Assert.That(mood.Level, Is.EqualTo(MoodLevel.High));
        }

        [Test]
        public void ChangingMoodAtSameLevelStartsItsOwnClock()
        {
            var mood = new MoodSystem();
            mood.Apply("anger", 90);
            mood.Tick(35, false);
            mood.Apply("sadness", 90);
            Assert.That(mood.RemainingSeconds, Is.EqualTo(40));
        }

        [Test]
        public void UnrelatedSignalDoesNotRestoreAnAlreadyDowngradedMood()
        {
            var mood = new MoodSystem();
            mood.Apply("anger", 90);
            mood.Tick(40, false);
            mood.Tick(5, false);
            mood.Apply("happy", 10);
            Assert.That(mood.Level, Is.EqualTo(MoodLevel.Light));
            Assert.That(mood.RemainingSeconds, Is.EqualTo(55));
        }

        [Test]
        public void InvalidSavedTimerCannotPoisonMoodClock()
        {
            var mood = new MoodSystem();
            mood.Apply("happy", 40);
            var saved = mood.Capture();
            saved.remainingSeconds = float.NaN;
            Assert.Throws<ArgumentException>(() => mood.Restore(saved));
            Assert.That(mood.ActiveId, Is.EqualTo("happy"));
        }

        [Test]
        public void SaveRestoreContinuesRemainingActiveTimeAndSupportsRegisteredMoodExtension()
        {
            var mood = new MoodSystem(new[] { new MoodDefinition("curious", 1, false) });
            mood.Apply("curious", 65);
            mood.Tick(10, false);
            var restored = new MoodSystem(new[] { new MoodDefinition("curious", 1, false) });
            restored.Restore(mood.Capture());
            Assert.That(restored.RemainingSeconds, Is.EqualTo(5));
            restored.Tick(5, false);
            Assert.That(restored.Level, Is.EqualTo(MoodLevel.Light));
            restored.Tick(10000, false);
            Assert.That(restored.ActiveId, Is.EqualTo("curious"));
        }

        private sealed class DelayedProvider : ILLMProvider
        {
            public readonly TaskCompletionSource<ConversationReply> Completion = new TaskCompletionSource<ConversationReply>();
            public ConversationRequest LastRequest { get; private set; }
            public Task<ConversationReply> CompleteAsync(ConversationRequest request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Completion.Task;
            }
        }

        private sealed class StaticCharacterContext : ICharacterContextProvider
        {
            public CharacterContextSnapshot Capture() => new CharacterContextSnapshot
            {
                persona = new CharacterPersonaSnapshot
                {
                    schema_version = 1,
                    character_id = "test",
                    name = "Test",
                    age = 16,
                    gender = "female",
                    height_cm = 155
                },
                runtime = new CharacterRuntimeSnapshot
                {
                    revision = 1,
                    appearance = new RuntimeAppearanceSnapshot
                    {
                        outfit_id = "test-outfit",
                        display_name = "Test Outfit"
                    },
                    mood = new RuntimeMoodSnapshot { id = "neutral", level = "none" },
                    posture = "standing"
                }
            };
        }

        private sealed class CompletedAction : IAvatarAction
        {
            public string Check() => null;
            public void Start() { }
            public ActionProgress Tick(float deltaSeconds) => ActionProgress.Succeeded;
            public void Cancel(ActionStopReason reason) { }
            public void Dispose() { }
        }

        [Test]
        public void CancelledLateReplyCannotChangeMoodOrStartAnAction()
        {
            var provider = new DelayedProvider();
            var catalog = new ActionCatalog();
            catalog.Register(new ActionDefinition("wave", "Wave", ControlChannels.RightArm, _ => new CompletedAction()));
            var mood = new MoodSystem();
            using (var avatar = new AvatarActionCoordinator(catalog))
            using (var items = new ItemRegistry())
            using (var dialogue = new ConversationOrchestrator(provider, new SessionConversationContext(),
                catalog, avatar, items, mood, new StaticCharacterContext()))
            {
                var pending = dialogue.SendAsync("hello");
                dialogue.Cancel();
                provider.Completion.SetResult(new ConversationReply
                {
                    reply_text = "hello", response_emotion = "happy",
                    action = new DialogueAction { id = "wave" },
                    mood_signals = new[] { new MoodSignal { id = "happy", delta = 32 } }
                });
                Assert.That(pending.IsCompleted, Is.True);
                pending.GetAwaiter().GetResult();
                Assert.That(avatar.Count, Is.Zero);
                Assert.That(mood.GetValue("happy"), Is.Zero);
                Assert.That(dialogue.State, Is.EqualTo(ConversationState.Cancelled));
            }
        }

        [Test]
        public void OverlappingSendDoesNotCancelTheInFlightReply()
        {
            var provider = new DelayedProvider();
            var catalog = new ActionCatalog();
            using (var avatar = new AvatarActionCoordinator(catalog))
            using (var items = new ItemRegistry())
            using (var dialogue = new ConversationOrchestrator(provider,
                new SessionConversationContext(), catalog, avatar, items,
                new MoodSystem(), new StaticCharacterContext()))
            {
                var first = dialogue.SendAsync("first");
                var overlapping = dialogue.SendAsync("second");
                var error = Assert.Throws<InvalidOperationException>(
                    () => overlapping.GetAwaiter().GetResult());
                Assert.That(error.Message, Is.EqualTo("冷月正在回复，请稍候。"));
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(dialogue.State, Is.EqualTo(ConversationState.Thinking));

                provider.Completion.SetResult(new ConversationReply
                {
                    reply_text = "reply",
                    response_emotion = "normal",
                    action = new DialogueAction { id = "none" }
                });
                first.GetAwaiter().GetResult();
                Assert.That(dialogue.State, Is.EqualTo(ConversationState.Replied));
                Assert.That(dialogue.Text, Is.EqualTo("reply"));
            }
        }

        [TestCase("unknown", false)]
        [TestCase("internal", false)]
        [TestCase("wave", true)]
        public void DialogueUsesTheSameCatalogAndDoesNotExposeInternalActions(string id, bool accepted)
        {
            var provider = new DelayedProvider();
            var catalog = new ActionCatalog();
            catalog.Register(new ActionDefinition("wave", "Wave", ControlChannels.RightArm, _ => new CompletedAction()));
            catalog.Register(new ActionDefinition("internal", "Private", ControlChannels.Body, _ => new CompletedAction(),
                exposeToAI: false));
            using (var avatar = new AvatarActionCoordinator(catalog))
            using (var items = new ItemRegistry())
            using (var dialogue = new ConversationOrchestrator(provider, new SessionConversationContext(),
                catalog, avatar, items, new MoodSystem(), new StaticCharacterContext()))
            {
                provider.Completion.SetResult(new ConversationReply
                {
                    reply_text = "reply",
                    response_emotion = "normal",
                    action = new DialogueAction { id = id }
                });
                var pending = dialogue.SendAsync("wave");
                Assert.That(pending.IsCompleted, Is.True);
                pending.GetAwaiter().GetResult();
                Assert.That(dialogue.State, Is.EqualTo(ConversationState.Replied));
                Assert.That(dialogue.LastAction != null, Is.EqualTo(accepted));
                Assert.That(dialogue.ActionError == null, Is.EqualTo(accepted));
                Assert.That(provider.LastRequest.Moods,
                    Is.EquivalentTo(new[] { "sadness", "anger", "sweet", "happy" }));
                Assert.That(provider.LastRequest.CharacterContext.persona.age, Is.EqualTo(16));
                Assert.That(provider.LastRequest.CharacterContext.runtime.posture, Is.EqualTo("standing"));
                Assert.That(provider.LastRequest.ContextCaptureMilliseconds, Is.GreaterThanOrEqualTo(0));
                Assert.That(dialogue.LastResponseEmotion, Is.EqualTo(ResponseEmotion.Normal));
                Assert.That(dialogue.ReplySequence, Is.EqualTo(1));
            }
        }

        [TestCase("normal", ResponseEmotion.Normal)]
        [TestCase("happy", ResponseEmotion.Happy)]
        [TestCase("sad", ResponseEmotion.Sad)]
        [TestCase("angry", ResponseEmotion.Angry)]
        public void DialoguePublishesOneValidatedEmotionPerReply(
            string wireValue, ResponseEmotion expected)
        {
            var provider = new DelayedProvider();
            provider.Completion.SetResult(new ConversationReply
            {
                reply_text = "reply",
                response_emotion = wireValue,
                action = new DialogueAction { id = "none" }
            });
            var catalog = new ActionCatalog();
            using (var avatar = new AvatarActionCoordinator(catalog))
            using (var items = new ItemRegistry())
            using (var dialogue = new ConversationOrchestrator(provider,
                new SessionConversationContext(), catalog, avatar, items,
                new MoodSystem(), new StaticCharacterContext()))
            {
                dialogue.SendAsync("hello").GetAwaiter().GetResult();
                Assert.That(dialogue.State, Is.EqualTo(ConversationState.Replied));
                Assert.That(dialogue.LastResponseEmotion, Is.EqualTo(expected));
                Assert.That(dialogue.ReplySequence, Is.EqualTo(1));
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("surprised")]
        public void DialogueRejectsMissingOrUnsupportedResponseEmotion(string wireValue)
        {
            var provider = new DelayedProvider();
            provider.Completion.SetResult(new ConversationReply
            {
                reply_text = "reply",
                response_emotion = wireValue,
                action = new DialogueAction { id = "none" }
            });
            var catalog = new ActionCatalog();
            using (var avatar = new AvatarActionCoordinator(catalog))
            using (var items = new ItemRegistry())
            using (var dialogue = new ConversationOrchestrator(provider,
                new SessionConversationContext(), catalog, avatar, items,
                new MoodSystem(), new StaticCharacterContext()))
            {
                dialogue.SendAsync("hello").GetAwaiter().GetResult();
                Assert.That(dialogue.State, Is.EqualTo(ConversationState.Failed));
                Assert.That(dialogue.ReplySequence, Is.Zero);
                Assert.That(dialogue.LastResponseEmotion, Is.Null);
            }
        }
    }
}
