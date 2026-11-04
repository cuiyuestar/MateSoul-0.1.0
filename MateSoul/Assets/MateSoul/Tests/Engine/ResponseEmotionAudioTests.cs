using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MateSoul.Core;
using MateSoul.Gameplay;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class ResponseEmotionAudioTests
    {
        private sealed class QueueProvider : ILLMProvider
        {
            private readonly Queue<ConversationReply> replies;

            public QueueProvider(IEnumerable<ConversationReply> values)
            {
                replies = new Queue<ConversationReply>(values);
            }

            public Task<ConversationReply> CompleteAsync(
                ConversationRequest request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(replies.Dequeue());
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
                    schema_version = 1,
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

        [UnityTest]
        public IEnumerator CompletedRepliesPlayOnlyFromTheirEmotionPool()
        {
            yield return new EnterPlayMode();
            var character = new GameObject("Response Emotion Audio Test");
            var catalog = new ActionCatalog();
            using (var avatar = new AvatarActionCoordinator(catalog))
            using (var items = new ItemRegistry())
            using (var dialogue = new ConversationOrchestrator(
                new QueueProvider(new[]
                {
                    Reply("normal"), Reply("happy"), Reply("sad"), Reply("angry")
                }),
                new SessionConversationContext(), catalog, avatar, items,
                new MoodSystem(), new StaticCharacterContext()))
            {
                var player = ResponseEmotionAudio.Attach(character, dialogue);
                Assert.That(player.ClipCount(ResponseEmotion.Normal), Is.EqualTo(2));
                Assert.That(player.ClipCount(ResponseEmotion.Happy), Is.EqualTo(2));
                Assert.That(player.ClipCount(ResponseEmotion.Sad), Is.EqualTo(1));
                Assert.That(player.ClipCount(ResponseEmotion.Angry), Is.EqualTo(2));

                foreach (var emotion in new[]
                         {
                             ResponseEmotion.Normal, ResponseEmotion.Happy,
                             ResponseEmotion.Sad, ResponseEmotion.Angry
                         })
                {
                    var pending = dialogue.SendAsync(emotion.ToString());
                    while (!pending.IsCompleted) yield return null;
                    pending.GetAwaiter().GetResult();
                    Assert.That(player.LastPlayedEmotion, Is.EqualTo(emotion));
                    Assert.That(player.Source.clip, Is.SameAs(player.LastPlayedClip));
                    Assert.That(ResourcePool(emotion), Does.Contain(player.LastPlayedClip));
                }
                Assert.That(dialogue.ReplySequence, Is.EqualTo(4));
                Assert.That(player.Source.spatialBlend, Is.EqualTo(1));
                Assert.That(player.Source.loop, Is.False);
            }
            Object.Destroy(character);
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator MultiClipEmotionUsesRandomizedSelection()
        {
            yield return new EnterPlayMode();
            var character = new GameObject("Response Emotion Random Test");
            var player = character.AddComponent<ResponseEmotionAudio>();
            player.Configure(
                ResourcePool(ResponseEmotion.Normal),
                ResourcePool(ResponseEmotion.Happy),
                ResourcePool(ResponseEmotion.Sad),
                ResourcePool(ResponseEmotion.Angry));
            var previousState = Random.state;
            Random.InitState(31415);
            var selected = new HashSet<AudioClip>();
            for (var index = 0; index < 16; index++)
                selected.Add(player.Play(ResponseEmotion.Normal));
            Random.state = previousState;
            Assert.That(selected, Is.EquivalentTo(ResourcePool(ResponseEmotion.Normal)));
            Object.Destroy(character);
            yield return null;
            yield return new ExitPlayMode();
        }

        private static ConversationReply Reply(string emotion) => new ConversationReply
        {
            status = "ok",
            reply_text = "reply",
            response_emotion = emotion,
            action = new DialogueAction { id = "none" },
            mood_signals = System.Array.Empty<MoodSignal>()
        };

        private static AudioClip[] ResourcePool(ResponseEmotion emotion)
            => Resources.LoadAll<AudioClip>(
                "ResponseEmotionAudio/" + emotion.ToString());
    }
}
