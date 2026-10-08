using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MateSoul.Gameplay;
using UnityEngine;
using UnityEngine.Networking;

namespace MateSoul.Runtime
{
    // The application talks to a project-controlled gateway. Provider credentials
    // and vendor-specific protocols stay on that server, outside the APK.
    public sealed class HttpLLMProvider : ILLMProvider
    {
        private readonly string endpoint;
        private readonly int timeoutSeconds;
        private readonly bool allowLocalHttpInDevelopment;
        private readonly string sessionId = Guid.NewGuid().ToString("N");

        public HttpLLMProvider(ConversationGatewaySettings settings)
        {
            endpoint = settings == null ? null : settings.Endpoint;
            timeoutSeconds = settings == null ? 60 : settings.TimeoutSeconds;
            allowLocalHttpInDevelopment = settings != null && settings.AllowLocalHttpInDevelopment;
        }

        [Serializable] private sealed class ActionInfo
        {
            public string id, description, target_capability;
        }
        [Serializable] private sealed class TargetInfo
        {
            public string id, name, state;
            public string[] capabilities;
        }
        [Serializable] private sealed class RequestData
        {
            public string session_id;
            public string request_id;
            public string input;
            public DialogueMessage[] history;
            public ActionInfo[] actions;
            public TargetInfo[] targets;
            public string[] moods;
            public CharacterPersonaSnapshot persona;
            public CharacterRuntimeSnapshot runtime;
        }
        [Serializable] private sealed class ErrorInfo
        {
            public string code, message, request_id;
        }
        [Serializable] private sealed class ErrorData
        {
            public ErrorInfo error;
        }

        public async Task<ConversationReply> CompleteAsync(ConversationRequest request, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new InvalidOperationException("AI 服务未连接。请在 GameBootstrap 中设置对话网关地址。");
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                !IsAllowedEndpoint(uri))
                throw new InvalidOperationException(
                    "对话网关地址必须使用 HTTPS；开发包仅允许显式配置的本机 HTTP 地址。");
            var requestId = Guid.NewGuid().ToString("N");
            var data = new RequestData
            {
                session_id = sessionId,
                request_id = requestId,
                input = request.Input, history = request.History.ToArray(),
                actions = request.Actions.Select(a => new ActionInfo
                {
                    id = a.Id, description = a.Description, target_capability = a.TargetCapability
                }).ToArray(),
                targets = request.Targets.Select(t => new TargetInfo
                {
                    id = t.Id, name = t.DisplayName, state = t.StateId, capabilities = t.Capabilities.ToArray()
                }).ToArray(),
                moods = request.Moods.ToArray(),
                persona = request.CharacterContext.persona,
                runtime = request.CharacterContext.runtime
            };
            var payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(data));
            UnityEngine.Debug.Log(
                $"AI_CONTEXT_CAPTURED revision={data.runtime.revision} " +
                $"captureMs={request.ContextCaptureMilliseconds:F3} payloadBytes={payload.Length}");
            var elapsed = Stopwatch.StartNew();
            using (var web = new UnityWebRequest(endpoint, "POST"))
            {
                web.uploadHandler = new UploadHandlerRaw(payload);
                web.downloadHandler = new DownloadHandlerBuffer();
                web.SetRequestHeader("Content-Type", "application/json");
                web.SetRequestHeader("X-Request-ID", requestId);
                web.timeout = timeoutSeconds;
                token.ThrowIfCancellationRequested();
                var operation = web.SendWebRequest();
                while (!operation.isDone)
                {
                    if (token.IsCancellationRequested) { web.Abort(); token.ThrowIfCancellationRequested(); }
                    await Task.Yield();
                }
                token.ThrowIfCancellationRequested();
                if (web.result != UnityWebRequest.Result.Success)
                {
                    var gatewayError = string.IsNullOrEmpty(web.downloadHandler.text)
                        ? null : JsonUtility.FromJson<ErrorData>(web.downloadHandler.text);
                    var detail = gatewayError?.error?.message ?? web.error;
                    throw new InvalidOperationException(
                        $"对话网关请求失败：HTTP {web.responseCode} / {detail}");
                }
                var reply = JsonUtility.FromJson<ConversationReply>(web.downloadHandler.text);
                UnityEngine.Debug.Log($"AI_REQUEST_COMPLETED sessionId={sessionId} requestId={requestId} " +
                                      $"durationMs={elapsed.ElapsedMilliseconds}");
                return reply;
            }
        }

        private bool IsAllowedEndpoint(Uri uri)
        {
            if (uri.Scheme == Uri.UriSchemeHttps) return true;
            return allowLocalHttpInDevelopment && UnityEngine.Debug.isDebugBuild &&
                   uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
        }
    }
}
