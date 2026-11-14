using System;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using ByteDance.PICO.Platform.CLIB;
using ByteDance.PICO.Platform.Framework;
using ByteDance.PICO.Platform.Models;
using ByteDance.PICO.Platform.Service;
#endif

namespace MateSoul.Runtime
{
    // 使用 PICO 平台内置语音转文字：点击开始，检测到停止说话后自动结束并回调文本。
    // 编辑器不加载 PICO 原生库，只返回不可用提示；界面仍可在编辑器里检查布局。
    public sealed class SpeechToTextRecorder : IDisposable
    {
        public enum Phase
        {
            Idle,
            Preparing,
            Listening,
            Finishing
        }

        private const float PrepareTimeoutSeconds = 20f;
        private const float PermissionTimeoutSeconds = 15f;
        private const float NoSpeechTimeoutSeconds = 12f;
        private const float FinishAfterSilenceSeconds = 5f;
        private const int MaxRecognitionSeconds = 60;
        private const float FinishGraceSeconds = 1f;

        public event Action<string> PartialText;
        public event Action<string> Completed;
        public event Action<string> Failed;

        public Phase State { get; private set; }
        public bool IsCapturing => State != Phase.Idle;

        public void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (IsCapturing)
            {
                Stop();
                return;
            }
            if (platformInitFailed)
            {
                Failed?.Invoke("PICO 平台服务初始化失败，请检查网络与 App ID 后重启应用。");
                return;
            }
            recognized = string.Empty;
            State = Phase.Preparing;
            active = this;
            platformInitializing = false;
            permissionRequested = false;
            engineStarted = false;
            phaseDeadline = Time.realtimeSinceStartup + PrepareTimeoutSeconds;
            if (!CoreService.Initialized)
            {
                var appId = ReadAppId();
                if (string.IsNullOrWhiteSpace(appId))
                {
                    Fail("语音输入需要 PICO App ID：请在 Unity 菜单 PICO > Platform > Platform Settings 中填写后重新打包。");
                    return;
                }
                platformInitializing = true;
                try
                {
                    CoreService.AsyncInitialize(appId).OnComplete(OnPlatformInitialized);
                }
                catch (Exception error)
                {
                    platformInitializing = false;
                    Fail("PICO 平台服务初始化失败：" + error.Message);
                    return;
                }
            }
            ContinuePreparing();
#else
            Failed?.Invoke("语音输入仅在 PICO 真机上可用。");
#endif
        }

        // 手动结束：停止采集，等待最后一包识别结果；超时后用已识别文本结束。
        public void Stop()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (State == Phase.Listening)
            {
                State = Phase.Finishing;
                phaseDeadline = Time.realtimeSinceStartup + FinishGraceSeconds;
                StopEngineQuietly();
            }
            else if (State == Phase.Preparing)
            {
                Cancel();
            }
#endif
        }

        public void Cancel()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var started = engineStarted;
            Reset();
            if (started) StopEngineQuietly();
#else
            State = Phase.Idle;
#endif
        }

        public void Tick()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            switch (State)
            {
                case Phase.Preparing:
                    if (!platformInitializing) ContinuePreparing();
                    if (State == Phase.Preparing && Time.realtimeSinceStartup >= phaseDeadline)
                        Fail(permissionRequested
                            ? "未获得麦克风权限，请允许录音后重试。"
                            : "语音服务准备超时，请检查网络后重试。");
                    break;
                case Phase.Listening:
                    if (recognized.Length == 0 &&
                        Time.realtimeSinceStartup - lastVoiceAt >= NoSpeechTimeoutSeconds)
                        Finish();
                    else if (recognized.Length > 0 &&
                        Time.realtimeSinceStartup - lastVoiceAt >= FinishAfterSilenceSeconds)
                        Finish();
                    else if (Time.realtimeSinceStartup - listenStartedAt >= MaxRecognitionSeconds + 2f)
                        Finish();
                    break;
                case Phase.Finishing:
                    if (Time.realtimeSinceStartup >= phaseDeadline) Complete();
                    break;
            }
#endif
        }

        public void Dispose() => Cancel();

#if UNITY_ANDROID && !UNITY_EDITOR
        private static SpeechToTextRecorder active;
        private static bool callbacksRegistered;
        private static bool engineReady;
        private static bool platformInitFailed;
        private bool platformInitializing;
        private bool permissionRequested;
        private bool engineStarted;
        private string recognized = string.Empty;
        private float phaseDeadline;
        private float listenStartedAt;
        private float lastVoiceAt;

        private static string ReadAppId()
        {
            try
            {
                var setting = PXR_PlatformSetting.Instance;
                return setting == null ? null : setting.appID;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void OnPlatformInitialized(Message<PlatformInitializeResult> message)
        {
            platformInitializing = false;
            if (!ReferenceEquals(active, this) || State != Phase.Preparing) return;
            if (message == null || message.IsError ||
                message.Data != PlatformInitializeResult.Success &&
                message.Data != PlatformInitializeResult.AlreadyInitialized)
            {
                platformInitFailed = true;
                Fail("PICO 平台服务初始化失败，请检查网络与 App ID 后重启应用。");
                return;
            }
            platformInitFailed = false;
            ContinuePreparing();
        }

        private void ContinuePreparing()
        {
            if (State != Phase.Preparing || platformInitializing || platformInitFailed) return;
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.Microphone))
            {
                if (!permissionRequested)
                {
                    permissionRequested = true;
                    phaseDeadline = Time.realtimeSinceStartup + PermissionTimeoutSeconds;
                    UnityEngine.Android.Permission.RequestUserPermission(
                        UnityEngine.Android.Permission.Microphone);
                }
                return;
            }
            if (!EnsureEngine()) return;
            RegisterCallbacks();
            int result;
            try
            {
                result = SpeechService.StartAsr(true, true, MaxRecognitionSeconds);
            }
            catch (Exception error)
            {
                Fail("语音识别启动失败：" + error.Message);
                return;
            }
            if (result != 0)
            {
                Fail(ErrorMessage(result));
                return;
            }
            engineStarted = true;
            State = Phase.Listening;
            listenStartedAt = Time.realtimeSinceStartup;
            lastVoiceAt = listenStartedAt;
        }

        private bool EnsureEngine()
        {
            if (engineReady) return true;
            AsrEngineInitResult result;
            try
            {
                result = SpeechService.InitAsrEngine();
            }
            catch (Exception error)
            {
                Fail("语音识别初始化失败：" + error.Message);
                return false;
            }
            if (result == AsrEngineInitResult.Success || result == AsrEngineInitResult.AlreadyInitialized)
            {
                engineReady = true;
                return true;
            }
            Fail(result == AsrEngineInitResult.Arch32BitNotSupported
                ? "语音识别仅支持 64 位应用。"
                : "语音识别初始化失败，请检查 PICO 平台配置。");
            return false;
        }

        private static void RegisterCallbacks()
        {
            if (callbacksRegistered) return;
            callbacksRegistered = true;
            SpeechService.SetOnAsrResultCallback(OnAsrResult);
            SpeechService.SetOnSpeechErrorCallback(OnSpeechError);
        }

        private static void OnAsrResult(Message<AsrResult> message)
        {
            var recorder = active;
            if (recorder != null) recorder.HandleAsrResult(message);
        }

        private static void OnSpeechError(Message<SpeechError> message)
        {
            var recorder = active;
            if (recorder != null) recorder.HandleSpeechError(message);
        }

        private void HandleAsrResult(Message<AsrResult> message)
        {
            if (State != Phase.Listening && State != Phase.Finishing) return;
            if (message == null || message.IsError || message.Data == null) return;
            var text = (message.Data.Text ?? string.Empty).Trim();
            if (text.Length > 0)
            {
                recognized = text;
                lastVoiceAt = Time.realtimeSinceStartup;
                PartialText?.Invoke(recognized);
            }
            if (message.Data.IsFinalResult) Complete();
        }

        private void HandleSpeechError(Message<SpeechError> message)
        {
            if (!IsCapturing) return;
            var code = message != null && message.IsError
                ? message.Error.Code
                : message?.Data?.Code ?? -1;
            Fail(ErrorMessage(code));
        }

        private void Finish()
        {
            if (State != Phase.Listening) return;
            State = Phase.Finishing;
            phaseDeadline = Time.realtimeSinceStartup + FinishGraceSeconds;
            StopEngineQuietly();
        }

        private void Complete()
        {
            var value = recognized.Trim();
            var started = engineStarted;
            Reset();
            if (started) StopEngineQuietly();
            if (value.Length == 0) Failed?.Invoke("没有听清，请再说一次。");
            else Completed?.Invoke(value);
        }

        private void Fail(string error)
        {
            var started = engineStarted;
            Reset();
            if (started) StopEngineQuietly();
            Failed?.Invoke(error);
        }

        private void Reset()
        {
            State = Phase.Idle;
            recognized = string.Empty;
            platformInitializing = false;
            permissionRequested = false;
            engineStarted = false;
            if (ReferenceEquals(active, this)) active = null;
        }

        private static void StopEngineQuietly()
        {
            try
            {
                SpeechService.StopAsr();
            }
            catch (Exception)
            {
                // 退出或引擎已释放时停止失败不影响玩家可见流程。
            }
        }

        private static string ErrorMessage(int code)
        {
            if (code == -402) return "麦克风不可用：请允许录音权限后重试。";
            if (code == -700) return "麦克风被其他应用占用，请关闭后重试。";
            if (code == -1100 || code == -1101) return "语音服务鉴权失败，请检查 PICO 账号登录与 App ID。";
            if (code == 1013) return "没有识别到有效的语音，请再说一次。";
            if (code == 1014) return "等待语音超时，请再说一次。";
            if (code == 1015) return "说话时间过长，请分段说。";
            if (code == 4001 || code == 4003 || code >= -103 && code <= -100)
                return "语音服务网络异常，请检查网络后重试。";
            if (code == -1000) return "语音识别状态异常，请重试。";
            return $"语音识别失败（{code}），请重试。";
        }
#endif
    }
}
