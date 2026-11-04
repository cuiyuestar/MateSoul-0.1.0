using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MateSoul.Gameplay;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MateSoul.Tests
{
    // Opt-in acceptance against a real provider; credentials are process-only, never assets.
    public sealed class DirectAICloudValidationTests
    {
        private static HttpLLMProvider Provider()
        {
            var key = Environment.GetEnvironmentVariable("MATESOUL_VALIDATION_AI_KEY");
            if (string.IsNullOrEmpty(key)) Assert.Ignore("No cloud acceptance credential supplied.");
            var configuration = PlayerAIConfiguration.Validate(PlayerAIConfiguration.DefaultEndpoint, "deepseek-v4-pro", key);
            return new HttpLLMProvider(null, () => configuration);
        }

        private static IEnumerator Wait(Task task, string scenario)
        {
            var watch = Stopwatch.StartNew();
            while (!task.IsCompleted && watch.Elapsed.TotalSeconds < 35) yield return null;
            Assert.That(task.IsCompleted, Is.True);
            task.GetAwaiter().GetResult();
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "matesoul-direct-ai-cloud-timing.txt"),
                scenario + " durationMs=" + watch.ElapsedMilliseconds + Environment.NewLine);
        }

        [UnityTest]
        public IEnumerator RealConfigurationCheckAndConversation()
        {
            var provider = Provider();
            var key = Environment.GetEnvironmentVariable("MATESOUL_VALIDATION_AI_KEY");
            var configuration = PlayerAIConfiguration.Validate(PlayerAIConfiguration.DefaultEndpoint, "deepseek-v4-pro", key);
            yield return Wait(HttpLLMProvider.CheckConfigurationAsync(configuration, CancellationToken.None), "configuration");
            var reply = provider.CompleteAsync(DirectAIIntegrationTests.Request("冷月，简单介绍一下你自己。"), CancellationToken.None);
            yield return Wait(reply, "conversation");
            Assert.That(reply.Result.reply_text, Does.Contain("冷月"));
        }

        [UnityTest]
        public IEnumerator RealSpontaneousRemark()
        {
            var reply = Provider().CompleteSpontaneousAsync(new SpontaneousReplyRequest
            {
                persona = DirectAIIntegrationTests.Request().CharacterContext.persona,
                event_description = "玩家正在平底锅里煎牛排，空气中传来香气。",
                content_requirements = new[] { "自然地问玩家是不是在煎牛排。" },
                rules = new[] { "只说一句简短中文，不请求动作。" }
            }, CancellationToken.None);
            yield return Wait(reply, "spontaneous");
            Assert.That(reply.Result.reply_text, Does.Contain("牛排"));
        }

        [UnityTest]
        public IEnumerator RealSearchAndPageReadThenModelReply()
        {
            var tools = new AIWebTools();
            var search = tools.CallAsync("web_search", "{\"query\":\"Unity 2022.3 LTS documentation\",\"limit\":3}", CancellationToken.None);
            yield return Wait(search, "web_search");
            Assert.That(search.Result, Does.Contain("\"results\":[{").And.Not.StartsWith("Tool error:"));
            var fetch = tools.CallAsync("web_fetch",
                "{\"url\":\"https://docs.unity3d.com/2022.3/Documentation/Manual/index.html\",\"max_characters\":2000}", CancellationToken.None);
            yield return Wait(fetch, "web_fetch");
            Assert.That(fetch.Result, Does.Contain("Unity").And.Not.StartsWith("Tool error:"));
            var reply = Provider().CompleteAsync(DirectAIIntegrationTests.Request(
                "请先调用 web_search 搜索 Unity 2022.3 LTS 文档，再调用 web_fetch 读取 " +
                "https://docs.unity3d.com/2022.3/Documentation/Manual/index.html，用一句话说明这个页面是什么并附链接。"),
                CancellationToken.None);
            yield return Wait(reply, "model_with_web_tools");
            Assert.That(reply.Result.reply_text, Does.Contain("Unity"));
        }
    }
}
