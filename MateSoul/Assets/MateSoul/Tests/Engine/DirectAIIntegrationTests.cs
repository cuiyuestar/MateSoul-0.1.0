using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MateSoul.Core;
using MateSoul.Gameplay;
using MateSoul.Runtime;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class DirectAIIntegrationTests
    {
        private const string GoodReply =
            "{\"status\":\"ok\",\"reply_text\":\"我在这里。\",\"response_emotion\":\"normal\"," +
            "\"action\":{\"id\":\"none\",\"target_id\":null},\"mood_signals\":[]}";

        private sealed class ModelServer : IDisposable
        {
            private readonly HttpListener listener = new HttpListener();
            private readonly Func<int, JObject, Task<string>> respond;
            public readonly List<JObject> Requests = new List<JObject>();
            public readonly List<string> Authorizations = new List<string>();
            public string Endpoint { get; }
            public int StatusCode = 200;
            public Task Loop { get; }
            public ModelServer(Func<int, JObject, Task<string>> respond)
            {
                this.respond = respond;
                var portProbe = new TcpListener(IPAddress.Loopback, 0);
                portProbe.Start();
                var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
                portProbe.Stop();
                Endpoint = $"http://127.0.0.1:{port}/chat/completions";
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                listener.Start();
                Loop = RunAsync();
            }
            private async Task RunAsync()
            {
                try
                {
                    while (listener.IsListening)
                    {
                        var context = await listener.GetContextAsync();
                        Assert.That(context.Request.Url.AbsolutePath, Is.EqualTo("/chat/completions"));
                        Assert.That(context.Request.Headers["X-AI-API-Key"], Is.Null);
                        using (var reader = new StreamReader(context.Request.InputStream))
                        {
                            var body = JObject.Parse(await reader.ReadToEndAsync());
                            Requests.Add(body);
                            Authorizations.Add(context.Request.Headers["Authorization"]);
                            var reply = await respond(Requests.Count - 1, body);
                            var bytes = Encoding.UTF8.GetBytes(reply);
                            context.Response.StatusCode = StatusCode;
                            context.Response.ContentType = "application/json";
                            context.Response.ContentLength64 = bytes.Length;
                            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                            context.Response.Close();
                        }
                    }
                }
                catch (Exception) when (!listener.IsListening) { }
            }
            public void Dispose() { listener.Stop(); listener.Close(); }
        }

        public static ConversationRequest Request(string input = "你在哪里？")
            => new ConversationRequest(input, Array.Empty<DialogueMessage>(), Array.Empty<ActionDefinition>(),
                Array.Empty<ItemInstance>(), new[] { "happy" }, new CharacterContextSnapshot
                {
                    persona = new CharacterPersonaSnapshot
                    { schema_version = 1, character_id = "lengyue", name = "冷月", age = 16, gender = "female", height_cm = 155 },
                    runtime = new CharacterRuntimeSnapshot { revision = 37, posture = "standing" }
                }, .5f);

        private static string Completion(string content)
            => new JObject { ["choices"] = new JArray(new JObject
                { ["finish_reason"] = "stop", ["message"] = ModelReplyProtocol.Message("assistant", content) }) }.ToString();
        private static string ToolCompletion()
            => new JObject { ["choices"] = new JArray(new JObject
            {
                ["finish_reason"] = "tool_calls", ["message"] = new JObject
                {
                    ["role"] = "assistant", ["content"] = null,
                    ["tool_calls"] = new JArray(new JObject
                    {
                        ["id"] = "call-1", ["type"] = "function", ["function"] = new JObject
                        { ["name"] = "web_fetch", ["arguments"] = "{\"url\":\"https://127.0.0.1/private\"}" }
                    })
                }
            }) }.ToString();

        private static HttpLLMProvider Provider(ModelServer server, out ModelConnectionSettings settings)
        {
            settings = ScriptableObject.CreateInstance<ModelConnectionSettings>();
            settings.Configure(server.Endpoint, 25, true);
            var config = PlayerAIConfiguration.Validate(server.Endpoint, "fixture-model", "fixture-key", true);
            return new HttpLLMProvider(settings, () => config);
        }
        private static IEnumerator Wait(Task task)
        {
            var until = DateTime.UtcNow.AddSeconds(30);
            while (!task.IsCompleted && DateTime.UtcNow < until) yield return null;
            Assert.That(task.IsCompleted, Is.True, "AI operation did not finish within its budget.");
        }

        [UnityTest]
        public IEnumerator DirectRequestUsesBearerAndRuntimeContext()
        {
            using (var server = new ModelServer((_, __) => Task.FromResult(Completion(GoodReply))))
            {
                var provider = Provider(server, out var settings);
                try
                {
                    var task = provider.CompleteAsync(Request(), CancellationToken.None);
                    yield return Wait(task);
                    Assert.That(task.GetAwaiter().GetResult().reply_text, Is.EqualTo("我在这里。"));
                    Assert.That(server.Requests.Count, Is.EqualTo(1));
                    Assert.That(server.Authorizations[0], Is.EqualTo("Bearer fixture-key"));
                    Assert.That((string)server.Requests[0]["model"], Is.EqualTo("fixture-model"));
                    Assert.That((string)server.Requests[0]["messages"][0]["content"], Does.Contain("\"revision\":37"));
                    Assert.That(server.Requests[0]["tools"].Count(), Is.EqualTo(2));
                }
                finally { Object.DestroyImmediate(settings); }
            }
        }

        [UnityTest]
        public IEnumerator ToolResultIsReturnedWithMatchingIdAndIllegalActionIsRepairedOnce()
        {
            using (var server = new ModelServer((round, _) => Task.FromResult(round == 0 ? ToolCompletion() :
                Completion(round == 1 ? GoodReply.Replace("\"none\"", "\"unregistered-action\"") : GoodReply))))
            {
                var provider = Provider(server, out var settings);
                try
                {
                    var task = provider.CompleteAsync(Request(), CancellationToken.None);
                    yield return Wait(task);
                    Assert.That(task.GetAwaiter().GetResult().action.id, Is.EqualTo("none"));
                    Assert.That(server.Requests.Count, Is.EqualTo(3));
                    var tool = server.Requests[1]["messages"][3];
                    Assert.That((string)tool["role"], Is.EqualTo("tool"));
                    Assert.That((string)tool["tool_call_id"], Is.EqualTo("call-1"));
                    Assert.That((string)tool["content"], Does.StartWith("Tool error:"));
                    Assert.That(server.Requests[2]["tools"], Is.Null, "A repair cannot request more tools.");
                    Assert.That((string)server.Requests[2]["messages"].Last["content"], Does.Contain("Unregistered action"));
                }
                finally { Object.DestroyImmediate(settings); }
            }
        }

        [UnityTest]
        public IEnumerator RepeatedInvalidReplyFailsAfterSingleRepair()
        {
            using (var server = new ModelServer((_, __) => Task.FromResult(Completion("{}"))))
            {
                var provider = Provider(server, out var settings);
                try
                {
                    var task = provider.CompleteAsync(Request(), CancellationToken.None);
                    yield return Wait(task);
                    Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
                    Assert.That(server.Requests.Count, Is.EqualTo(2));
                }
                finally { Object.DestroyImmediate(settings); }
            }
        }

        [UnityTest]
        public IEnumerator AuthenticationErrorDoesNotEchoProviderBodyOrRetry()
        {
            using (var server = new ModelServer((_, __) => Task.FromResult("{\"error\":\"fixture-key private detail\"}")))
            {
                server.StatusCode = 401;
                var provider = Provider(server, out var settings);
                try
                {
                    var task = provider.CompleteAsync(Request(), CancellationToken.None);
                    yield return Wait(task);
                    var error = Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
                    Assert.That(error.Message, Does.Contain("API Key").And.Not.Contains("fixture-key"));
                    Assert.That(server.Requests.Count, Is.EqualTo(1));
                }
                finally { Object.DestroyImmediate(settings); }
            }
        }

        [UnityTest]
        public IEnumerator CancelledRequestCannotPublishLateReply()
        {
            var release = new TaskCompletionSource<string>();
            using (var server = new ModelServer((_, __) => release.Task))
            using (var cancellation = new CancellationTokenSource())
            {
                var provider = Provider(server, out var settings);
                try
                {
                    var task = provider.CompleteAsync(Request(), cancellation.Token);
                    var until = DateTime.UtcNow.AddSeconds(5);
                    while (server.Requests.Count == 0 && DateTime.UtcNow < until) yield return null;
                    cancellation.Cancel();
                    yield return Wait(task);
                    Assert.That(task.IsCanceled, Is.True);
                }
                finally { release.TrySetResult(Completion(GoodReply)); Object.DestroyImmediate(settings); }
            }
        }

        [UnityTest]
        public IEnumerator SpontaneousReplySharesProviderButHasNoHistoryActionsOrTools()
        {
            using (var server = new ModelServer((_, __) => Task.FromResult(Completion(
                "{\"reply_text\":\"好香呀！\",\"response_emotion\":\"happy\"}"))))
            {
                var provider = Provider(server, out var settings);
                try
                {
                    var task = provider.CompleteSpontaneousAsync(new SpontaneousReplyRequest
                    {
                        persona = Request().CharacterContext.persona, event_description = "空气中有食物香气。",
                        content_requirements = new[] { "说一句话。" }, rules = new[] { "不请求动作。" }
                    }, CancellationToken.None);
                    yield return Wait(task);
                    Assert.That(task.GetAwaiter().GetResult().response_emotion, Is.EqualTo("happy"));
                    Assert.That(server.Requests[0]["messages"].Count(), Is.EqualTo(1));
                    Assert.That(server.Requests[0]["tools"], Is.Null);
                }
                finally { Object.DestroyImmediate(settings); }
            }
        }

        [Test]
        public void GatewayRecordMigratesWithoutLosingKeyAndNewProviderRecordRemains()
        {
            var old = JsonUtility.FromJson<PlayerAIConfiguration>(
                "{\"endpoint\":\"http://127.0.0.1:8787/v1/conversations/reply\",\"apiKey\":\"saved-key\",\"gatewayToken\":\"obsolete\"}");
            old.Migrate();
            Assert.That(old.endpoint, Is.EqualTo(PlayerAIConfiguration.DefaultEndpoint));
            Assert.That(old.apiKey, Is.EqualTo("saved-key"));
            Assert.That(old.version, Is.EqualTo(2));
            var current = PlayerAIConfiguration.Validate("https://provider.example/v1", "model", "key");
            current.Migrate();
            Assert.That(current.endpoint, Is.EqualTo("https://provider.example/v1/chat/completions"));
            Assert.That(JsonUtility.ToJson(current), Does.Not.Contain("gatewayToken"));
        }

        [TestCase("{\"reply_text\":\"ok\",\"response_emotion\":\"normal\",\"extra\":\"injected\"}")]
        [TestCase("{\"reply_text\":\"ok\",\"response_emotion\":\"happy\",\"mood_signals\":[{\"id\":\"happy\",\"delta\":33}]}")]
        [TestCase("{\"reply_text\":\"ok\",\"response_emotion\":\"angry\",\"action\":{\"id\":\"none\",\"target_id\":\"item\"}}")]
        [TestCase("{\"reply_text\":\"ok\",\"response_emotion\":\"normal\",\"reply_text\":\"duplicate\"}")]
        public void InvalidReplyCannotReachGameState(string content)
            => Assert.That(() => ModelReplyProtocol.Conversation(content, Request()), Throws.Exception);

        [TestCase("127.0.0.1")]
        [TestCase("172.16.0.1")]
        [TestCase("169.254.169.254")]
        [TestCase("192.168.1.1")]
        [TestCase("100.100.100.100")]
        [TestCase("::1")]
        [TestCase("fc00::1")]
        [TestCase("::ffff:192.168.1.1")]
        public void WebToolsRejectPrivateNetworks(string address)
            => Assert.That(AIWebTools.IsPublicAddress(IPAddress.Parse(address)), Is.False);
    }
}
