#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    // Deterministic device validation for development builds. The request still
    // travels through the production conversation, gateway, and MCP path.
    public sealed class DialogueDeviceProbe : MonoBehaviour
    {
        private const string RequestFileName = "dialogue-probe-request.txt";
        private const string ResultFileName = "dialogue-probe-result.json";
        private GameBootstrap game;
        private CancellationTokenSource lifetime;
        private bool processing;
        private float nextPoll;

        [Serializable]
        private sealed class Result
        {
            public string input;
            public string state;
            public string reply;
            public string action;
            public string actionState;
            public string actionError;
            public string mood;
            public string error;
            public float durationSeconds;
        }

        private string RequestPath => Path.Combine(Application.persistentDataPath, RequestFileName);
        private string ResultPath => Path.Combine(Application.persistentDataPath, ResultFileName);

        public void Configure(GameBootstrap bootstrap)
        {
            game = bootstrap;
            lifetime = new CancellationTokenSource();
        }

        private void Update()
        {
            if (processing || game?.Session == null || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + .25f;
            if (!File.Exists(RequestPath)) return;

            string input;
            try
            {
                input = File.ReadAllText(RequestPath).Trim();
                File.Delete(RequestPath);
            }
            catch (Exception exception)
            {
                WriteResult(new Result { state = ConversationState.Failed.ToString(), error = exception.Message });
                return;
            }

            processing = true;
            ExecuteAsync(input);
        }

        private async void ExecuteAsync(string input)
        {
            var startedAt = Time.realtimeSinceStartup;
            var result = new Result { input = input };
            try
            {
                await game.Session.Conversation.SendAsync(input, lifetime.Token);
                var conversation = game.Session.Conversation;
                result.state = conversation.State.ToString();
                result.reply = conversation.Text;
                result.action = conversation.LastAction?.Intent.ActionId;
                result.actionState = conversation.LastAction?.State.ToString();
                result.actionError = conversation.ActionError;
                result.mood = game.Session.Mood.ActiveId;
            }
            catch (Exception exception)
            {
                result.state = ConversationState.Failed.ToString();
                result.error = exception.Message;
            }
            finally
            {
                result.durationSeconds = Time.realtimeSinceStartup - startedAt;
                if (lifetime != null && !lifetime.IsCancellationRequested) WriteResult(result);
                processing = false;
            }
        }

        private void WriteResult(Result result)
        {
            var temporaryPath = ResultPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(result, true));
            if (File.Exists(ResultPath)) File.Delete(ResultPath);
            File.Move(temporaryPath, ResultPath);
        }

        private void OnDestroy()
        {
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = null;
        }
    }
}
#endif
