using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using MateSoul.Gameplay;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MateSoul.Tests
{
    public sealed class SpontaneousReplyTests
    {
        private sealed class CapturingProvider : ISpontaneousReplyProvider
        {
            public SpontaneousReplyRequest Request { get; private set; }

            public Task<SpontaneousReplyResult> CompleteSpontaneousAsync(
                SpontaneousReplyRequest request, CancellationToken token)
            {
                Request = request;
                return Task.FromResult(new SpontaneousReplyResult
                {
                    status = "ok",
                    reply_text = "好香呀，你是在煎牛排吗？",
                    response_emotion = "happy"
                });
            }
        }

        [UnityTest]
        public IEnumerator CoordinatorDeduplicatesTriggersAndPublishesTemporaryReply()
        {
            var provider = new CapturingProvider();
            var coordinator = new SpontaneousReplyCoordinator(
                provider, new CharacterPersonaState(Persona()));
            try
            {
                Assert.That(coordinator.Enqueue(
                    "cooking:item-1",
                    "空气中出现了煎牛排香气。",
                    new[] { "询问玩家是否在煎牛排。" },
                    new[] { "只说一句中文。" }), Is.True);
                Assert.That(coordinator.Enqueue(
                    "cooking:item-1",
                    "重复事件",
                    new[] { "不得执行。" },
                    new[] { "不得执行。" }), Is.False);

                var frames = 0;
                while (coordinator.ReplySequence == 0 && frames++ < 10)
                    yield return null;

                Assert.That(coordinator.State,
                    Is.EqualTo(SpontaneousReplyState.Replied));
                Assert.That(coordinator.VisibleText,
                    Is.EqualTo("好香呀，你是在煎牛排吗？"));
                Assert.That(coordinator.LastResponseEmotion,
                    Is.EqualTo(ResponseEmotion.Happy));
                Assert.That(provider.Request.persona.character_id,
                    Is.EqualTo("lengyue"));
                Assert.That(provider.Request.event_description,
                    Does.Contain("煎牛排香气"));
                coordinator.Tick(9f);
                Assert.That(coordinator.VisibleText, Is.Null);
            }
            finally
            {
                coordinator.Dispose();
            }
        }

        private static CharacterPersonaDefinition Persona()
        {
            return new CharacterPersonaDefinition
            {
                schema_version = 1,
                character_id = "lengyue",
                identity = new CharacterIdentityDefinition
                {
                    name = "冷月",
                    age = 16,
                    gender = "female",
                    height_cm = 155
                },
                personality_traits = new[]
                {
                    Trait("extraversion"),
                    Trait("conscientiousness"),
                    Trait("openness"),
                    Trait("agreeableness"),
                    Trait("emotional_stability")
                }
            };
        }

        private static PersonalityTraitDefinition Trait(string id)
        {
            return new PersonalityTraitDefinition
            {
                id = id,
                display_name = id,
                default_option_id = "balanced",
                options = new[]
                {
                    new PersonalityOptionDefinition
                    {
                        id = "balanced",
                        display_name = "平衡",
                        description = "自然表达。"
                    }
                }
            };
        }
    }
}
