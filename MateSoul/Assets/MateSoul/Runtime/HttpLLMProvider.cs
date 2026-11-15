using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MateSoul.Gameplay;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace MateSoul.Runtime
{
    // Model protocol, tools and validation run in the APK. Read credentials once per turn.
    public sealed class HttpLLMProvider : ILLMProvider, ISpontaneousReplyProvider
    {
        private readonly ModelConnectionSettings settings;
        private readonly Func<PlayerAIConfiguration> configuration;
        private readonly AIWebTools webTools = new AIWebTools();
        private int TimeoutSeconds => Math.Min(25, settings == null ? 25 : settings.TimeoutSeconds);

        public HttpLLMProvider(ModelConnectionSettings settings,
            Func<PlayerAIConfiguration> playerConfiguration = null)
        {
            this.settings = settings;
            configuration = playerConfiguration;
        }

        private PlayerAIConfiguration ReadConfiguration()
        {
            var player = configuration?.Invoke();
            if (player == null || !player.IsConfigured)
                throw new InvalidOperationException("先配置ai才能激活对话功能");
            return PlayerAIConfiguration.Validate(player.endpoint, player.model, player.apiKey,
                settings != null && settings.AllowLocalHttpInDevelopment);
        }

        public async Task<ConversationReply> CompleteAsync(ConversationRequest request, CancellationToken token)
        {
            var player = ReadConfiguration();
            var messages = ModelReplyProtocol.ConversationMessages(request);
            var requestId = Guid.NewGuid().ToString("N");
            var elapsed = Stopwatch.StartNew();
            UnityEngine.Debug.Log($"AI_CONTEXT_CAPTURED revision={request.CharacterContext.runtime.revision} " +
                $"captureMs={request.ContextCaptureMilliseconds:F3} promptCharacters={messages.ToString(Formatting.None).Length}");
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
                for (var round = 0; round <= 4; round++)
                {
                    var completion = await SendModelAsync(player, messages, AIWebTools.Definitions, deadline.Token);
                    var calls = completion["tool_calls"] as JArray;
                    if (calls == null || calls.Count == 0)
                    {
                        var reply = await ValidatedReplyAsync(player, messages, completion,
                            text => ModelReplyProtocol.Conversation(text, request), deadline.Token);
                        deadline.Token.ThrowIfCancellationRequested();
                        UnityEngine.Debug.Log($"AI_REQUEST_COMPLETED requestId={requestId} route=direct " +
                            $"durationMs={elapsed.ElapsedMilliseconds} toolRounds={round}");
                        return reply;
                    }
                    if (round == 4 || calls.Count > 8)
                        throw new InvalidOperationException("AI 联网查询超过本轮次数限制，请缩小问题范围。");
                    messages.Add(completion);
                    foreach (var call in calls)
                    {
                        if (call["id"]?.Type != JTokenType.String || call["function"]?["name"]?.Type != JTokenType.String ||
                            call["function"]?["arguments"]?.Type != JTokenType.String)
                            throw new InvalidOperationException("模型返回了无效的工具请求。");
                        var name = (string)call["function"]["name"];
                        var result = await webTools.CallAsync(name, (string)call["function"]["arguments"], deadline.Token);
                        messages.Add(new JObject { ["role"] = "tool", ["tool_call_id"] = (string)call["id"], ["content"] = result });
                        UnityEngine.Debug.Log($"AI_WEB_TOOL_COMPLETED requestId={requestId} " +
                            $"tool={(name == "web_search" || name == "web_fetch" ? name : "unknown")} resultCharacters={result.Length}");
                    }
                }
            }
            throw new InvalidOperationException("AI 未返回最终回复。");
        }

        public async Task<SpontaneousReplyResult> CompleteSpontaneousAsync(SpontaneousReplyRequest request, CancellationToken token)
        {
            var player = ReadConfiguration();
            var messages = ModelReplyProtocol.SpontaneousMessages(request);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
                var completion = await SendModelAsync(player, messages, null, deadline.Token);
                return await ValidatedReplyAsync(player, messages, completion, ModelReplyProtocol.Spontaneous, deadline.Token);
            }
        }

        private static string Shorten(string value, int limit = 240)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var flat = value.Replace('\n', ' ').Replace('\r', ' ');
            return flat.Length <= limit ? flat : flat.Substring(0, limit) + "…";
        }

        private static async Task<T> ValidatedReplyAsync<T>(PlayerAIConfiguration player, JArray messages,
            JObject completion, Func<string, T> parse, CancellationToken token)
        {
            var content = completion["content"]?.Type == JTokenType.String ? (string)completion["content"] : "{}";
            try
            {
                if ((completion["tool_calls"] as JArray)?.Count > 0) throw new FormatException("Tools are not allowed in the final reply.");
                return parse(content);
            }
            catch (Exception error) when (error is JsonException || error is FormatException)
            {
                UnityEngine.Debug.Log($"AI_REPLY_REPAIR reason={error.Message} " +
                    $"raw={Shorten(content)}");
                messages.Add(ModelReplyProtocol.Message("assistant", content));
                messages.Add(ModelReplyProtocol.Message("user",
                    "The previous response failed JSON protocol validation: " + error.Message +
                    " Return one corrected JSON object only. Do not use tools."));
                UnityEngine.Debug.Log("AI_REPLY_REPAIR_REQUESTED");
                var repaired = await SendModelAsync(player, messages, null, token);
                if ((repaired["tool_calls"] as JArray)?.Count > 0)
                    throw new InvalidOperationException("模型未返回有效的修正回复。");
                var repairedContent = repaired["content"]?.Type == JTokenType.String
                    ? (string)repaired["content"] : "{}";
                try { return parse(repairedContent); }
                catch (Exception invalid) when (invalid is JsonException || invalid is FormatException)
                {
                    UnityEngine.Debug.Log($"AI_REPLY_FAILED reason={invalid.Message} " +
                        $"raw={Shorten(repairedContent)}");
                    throw new InvalidOperationException("模型回复格式无效，请重试。");
                }
            }
        }

        private static async Task<JObject> SendModelAsync(PlayerAIConfiguration player, JArray messages,
            JArray tools, CancellationToken token, int maxTokens = 2048)
        {
            var body = new JObject
            {
                ["model"] = player.model, ["messages"] = messages.DeepClone(),
                ["stream"] = false, ["max_tokens"] = maxTokens, ["temperature"] = .7,
                ["response_format"] = new JObject { ["type"] = "json_object" }
            };
            // DeepSeek reasoning is unnecessary for short in-world remarks and consumes the turn budget.
            if (new Uri(player.endpoint).Host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase))
                body["thinking"] = new JObject { ["type"] = "disabled" };
            if (tools != null && tools.Count > 0)
            { body["tools"] = tools; body["tool_choice"] = "auto"; }
            using (var request = new UnityWebRequest(player.endpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Formatting.None)));
                request.downloadHandler = new AIBoundedDownload(262144);
                request.redirectLimit = 0; // Credentials must never follow a provider redirect.
                request.timeout = 25;
                request.SetRequestHeader("Content-Type", "application/json");
                player.ApplyHeaders(request);
                await AIBoundedDownload.SendAsync(request, token);
                var download = (AIBoundedDownload)request.downloadHandler;
                if (download.TooLarge) throw new InvalidOperationException("AI 回复超过接收大小限制。");
                if (request.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException(ModelError(request.responseCode));
                try
                {
                    var envelope = ModelReplyProtocol.ParseObject(download.Text);
                    var choice = envelope["choices"]?[0] as JObject;
                    if (!(choice?["message"] is JObject message) ||
                        (string)choice["finish_reason"] == "length")
                        throw new FormatException("Missing or truncated model response.");
                    // Only retain fields supported by the next chat-completions request.
                    return new JObject
                    {
                        ["role"] = "assistant", ["content"] = message["content"]?.DeepClone(),
                        ["tool_calls"] = message["tool_calls"]?.DeepClone()
                    };
                }
                catch (Exception error) when (error is JsonException || error is FormatException ||
                                              error is ArgumentException || error is InvalidOperationException)
                { throw new InvalidOperationException("供应商未返回有效的模型回复，请检查 API 地址与模型名称。"); }
            }
        }

        private static string ModelError(long code)
        {
            if (code == 401 || code == 403) return "API Key 无效或无访问权限，请检查 AI 配置。";
            if (code == 402) return "AI 账户余额不足，请充值后重试。";
            if (code == 429) return "AI 请求过于频繁或额度不足，请稍后重试。";
            if (code == 400 || code == 404 || code == 422) return $"AI 地址、模型或协议不匹配（HTTP {code}），请检查配置。";
            return $"无法连接 AI 供应商（HTTP {code}），请检查网络或稍后重试。";
        }

        public static async Task CheckConfigurationAsync(PlayerAIConfiguration player, CancellationToken token)
        {
            player = PlayerAIConfiguration.Validate(player.endpoint, player.model, player.apiKey);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(20));
                var result = await SendModelAsync(player, new JArray(ModelReplyProtocol.Message("user",
                    "Return exactly this JSON: {\"status\":\"ok\"}")), null, deadline.Token, 64);
                try
                {
                    if ((string)ModelReplyProtocol.ParseObject((string)result["content"])["status"] != "ok")
                        throw new FormatException();
                }
                catch (Exception error) when (error is JsonException || error is FormatException)
                { throw new InvalidOperationException("模型未返回有效的连接测试结果。"); }
            }
        }
    }
}
