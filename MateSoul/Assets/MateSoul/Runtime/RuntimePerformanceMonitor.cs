using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.XR;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MateSoul.Runtime
{
    /// <summary>
    /// Release-build frame telemetry. Emits MATESOUL_PERF_* lines (persisted by DeviceStartupLog)
    /// so frame drops can be diagnosed without a Development build or a PC attached.
    /// Cost is a handful of float operations per frame plus one Log line every 10 s.
    /// </summary>
    [DefaultExecutionOrder(30000)]
    public sealed class RuntimePerformanceMonitor : MonoBehaviour
    {
        private const string Prefix = "MATESOUL_PERF";
        private const int WindowSeconds = 10;
        private const int MaxFrameSamples = 2048;
        private const int MaxTimingSamples = 512;
        private const float SpikeLogInterval = 5f;

        private readonly float[] frameSamples = new float[MaxFrameSamples];
        private readonly float[] sortedFrames = new float[MaxFrameSamples];
        private readonly float[] cpuMain = new float[MaxTimingSamples];
        private readonly float[] cpuRender = new float[MaxTimingSamples];
        private readonly float[] gpu = new float[MaxTimingSamples];
        private readonly float[] presentWait = new float[MaxTimingSamples];
        private readonly FrameTiming[] timings = new FrameTiming[1];

        private int frameSampleCount;
        private int timingSampleCount;
        private int windowFrames;
        private int missedFrames;
        private int severeFrames;
        private int suppressedSpikeLogs;
        private float worstFrameMs;
        private float windowStartTime;
        private float lastSpikeLogTime = -999f;
        private float refreshRate;
        private float budgetMs;
        private long windowGcBytes;
        private int windowGc0;
        private int windowGc1;
        private int windowGc2;
        private bool paused;
        private bool contextLogged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Object.FindObjectOfType<RuntimePerformanceMonitor>() != null) return;
            var host = new GameObject("[RuntimePerformanceMonitor]");
            DontDestroyOnLoad(host);
            host.AddComponent<RuntimePerformanceMonitor>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            refreshRate = ResolveRefreshRate();
            budgetMs = 1000f / (refreshRate > 0f ? refreshRate : 72f);
            ResetWindow(Time.unscaledTime);
        }

        private void OnApplicationPause(bool value)
        {
            paused = value;
            // Background time is not rendered time; restart the window so the summary
            // never mixes paused seconds into FPS.
            if (!value) ResetWindow(Time.unscaledTime);
        }

        private void LateUpdate()
        {
            if (paused) return;
            var now = Time.unscaledTime;
            var dtMs = Time.unscaledDeltaTime * 1000f;
            if (dtMs <= 0f) return;

            if (refreshRate <= 0f)
            {
                refreshRate = ResolveRefreshRate();
                if (refreshRate > 0f) budgetMs = 1000f / refreshRate;
            }

            RecordFrame(dtMs);
            var spiked = dtMs >= budgetMs * 2.5f;
            if (spiked) LogSpike(dtMs, now);

            if (now - windowStartTime >= WindowSeconds)
            {
                LogSummary(now, dtMs, spiked);
                ResetWindow(now);
            }

            if (!contextLogged && now >= 2f)
            {
                contextLogged = true;
                LogContext();
            }
        }

        private void RecordFrame(float dtMs)
        {
            windowFrames++;
            if (dtMs > worstFrameMs) worstFrameMs = dtMs;
            if (dtMs >= budgetMs * 1.05f) missedFrames++;
            if (dtMs >= budgetMs * 2.5f) severeFrames++;
            if (frameSampleCount < frameSamples.Length) frameSamples[frameSampleCount++] = dtMs;

            FrameTimingManager.CaptureFrameTimings();
            var count = FrameTimingManager.GetLatestTimings(1, timings);
            if (count > 0 && timingSampleCount < MaxTimingSamples)
            {
                var timing = timings[0];
                cpuMain[timingSampleCount] = (float)timing.cpuMainThreadFrameTime;
                cpuRender[timingSampleCount] = (float)timing.cpuRenderThreadFrameTime;
                gpu[timingSampleCount] = (float)timing.gpuFrameTime;
                presentWait[timingSampleCount] = (float)timing.cpuMainThreadPresentWaitTime;
                timingSampleCount++;
            }
        }

        private void LogSpike(float dtMs, float now)
        {
            if (now - lastSpikeLogTime < SpikeLogInterval)
            {
                suppressedSpikeLogs++;
                return;
            }
            lastSpikeLogTime = now;
            Debug.Log($"{Prefix}_SPIKE frame={Time.frameCount} dt={dtMs:F1}ms " +
                      $"budget={budgetMs:F1}ms over={dtMs / budgetMs:F1}x " +
                      $"scopes={PerfMonitor.DescribeFrameScopes(3)} " +
                      $"events={PerfMonitor.DescribeWindowCounts(3)} suppressed={suppressedSpikeLogs}");
            suppressedSpikeLogs = 0;
        }

        private void LogSummary(float now, float lastFrameMs, bool lastFrameSpiked)
        {
            var elapsed = Mathf.Max(.001f, now - windowStartTime);
            var fps = windowFrames / elapsed;
            var missedPercent = windowFrames == 0 ? 0f : 100f * missedFrames / windowFrames;

            Array.Copy(frameSamples, sortedFrames, frameSampleCount);
            Array.Sort(sortedFrames, 0, frameSampleCount);

            var gcBytes = GC.GetTotalMemory(false);
            var gcKbPerSecond = Mathf.Max(0f, gcBytes - windowGcBytes) / 1024f / elapsed;
            var gc0 = GC.CollectionCount(0) - windowGc0;
            var gc1 = GC.CollectionCount(1) - windowGc1;
            var gc2 = GC.CollectionCount(2) - windowGc2;

            Debug.Log($"{Prefix}_SUMMARY win={elapsed:F1}s frames={windowFrames} fps={fps:F1} " +
                      $"budget={budgetMs:F1}ms missed={missedFrames}({missedPercent:F1}%) " +
                      $"severe={severeFrames} p50={Percentile(50):F1} p95={Percentile(95):F1} " +
                      $"p99={Percentile(99):F1} max={worstFrameMs:F1} " +
                      $"gpuP95={PercentileFrom(gpu, timingSampleCount, 95):F1} " +
                      $"cpuMainP95={PercentileFrom(cpuMain, timingSampleCount, 95):F1} " +
                      $"cpuRenderP95={PercentileFrom(cpuRender, timingSampleCount, 95):F1} " +
                      $"presentP95={PercentileFrom(presentWait, timingSampleCount, 95):F1} " +
                      $"gcKBps={gcKbPerSecond:F0} gc={gc0}/{gc1}/{gc2} " +
                      $"scopes={PerfMonitor.DescribeWindowScopes(6)} " +
                      $"events={PerfMonitor.DescribeWindowCounts(6)} last={lastFrameMs:F1}ms" +
                      (lastFrameSpiked ? " *" : string.Empty));

            windowGcBytes = GC.GetTotalMemory(false);
            windowGc0 = GC.CollectionCount(0);
            windowGc1 = GC.CollectionCount(1);
            windowGc2 = GC.CollectionCount(2);
            PerfMonitor.ResetWindow();
        }

        private void ResetWindow(float now)
        {
            windowStartTime = now;
            windowFrames = 0;
            missedFrames = 0;
            severeFrames = 0;
            worstFrameMs = 0f;
            frameSampleCount = 0;
            timingSampleCount = 0;
            windowGcBytes = GC.GetTotalMemory(false);
            windowGc0 = GC.CollectionCount(0);
            windowGc1 = GC.CollectionCount(1);
            windowGc2 = GC.CollectionCount(2);
            PerfMonitor.ResetWindow();
        }

        private float Percentile(int percentile)
        {
            if (frameSampleCount == 0) return 0f;
            var index = Mathf.Clamp(
                Mathf.CeilToInt(percentile / 100f * frameSampleCount) - 1, 0, frameSampleCount - 1);
            return sortedFrames[index];
        }

        private static float PercentileFrom(float[] values, int count, int percentile)
        {
            if (count == 0) return 0f;
            // Window is short enough (512 samples) that an in-place sort is cheaper
            // than maintaining a second array; callers reset the count afterwards.
            Array.Sort(values, 0, count);
            var index = Mathf.Clamp(Mathf.CeilToInt(percentile / 100f * count) - 1, 0, count - 1);
            return values[index];
        }

        private void LogContext()
        {
            var pico = Object.FindObjectOfType<PXR_Manager>();
            var eyeWidth = XRSettings.eyeTextureWidth;
            var eyeHeight = XRSettings.eyeTextureHeight;
            if (eyeWidth <= 0 || eyeHeight <= 0)
            {
                eyeWidth = XRSettings.eyeTextureResolutionScale > 0f
                    ? Mathf.RoundToInt(Screen.width * XRSettings.eyeTextureResolutionScale)
                    : Screen.width;
                eyeHeight = Mathf.RoundToInt(Screen.height * XRSettings.eyeTextureResolutionScale);
            }
            var quality = QualitySettings.GetQualityLevel();
            var qualityName = quality >= 0 && quality < QualitySettings.names.Length
                ? QualitySettings.names[quality] : "?";
            Debug.Log($"{Prefix}_CONTEXT device={SystemInfo.deviceModel} " +
                      $"chip={SystemInfo.processorType} gpu={SystemInfo.graphicsDeviceName} " +
                      $"api={SystemInfo.graphicsDeviceType} eye={eyeWidth}x{eyeHeight} " +
                      $"eyeScale={XRSettings.eyeTextureResolutionScale:F2} " +
                      $"screenHz={Screen.currentResolution.refreshRate} xrHz={refreshRate:F1} " +
                      $"quality={qualityName}({quality}) msaa={QualitySettings.antiAliasing} " +
                      $"vSync={QualitySettings.vSyncCount} targetFps={Application.targetFrameRate} " +
                      $"color={QualitySettings.activeColorSpace} " +
                      $"foveation={(pico != null ? pico.foveationLevel.ToString() : "?")} " +
                      $"adaptiveRes={(pico != null ? pico.adaptiveResolution.ToString() : "?")} " +
                      $"sharpening={(pico != null ? pico.sharpeningMode.ToString() : "?")} " +
                      $"frameTiming={FrameTimingManager.IsFeatureEnabled()}");
        }

        private static float ResolveRefreshRate()
        {
            var rate = 0f;
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
            {
                if (!display.running) continue;
                if (display.TryGetDisplayRefreshRate(out var value) && value > 0f)
                    rate = Mathf.Max(rate, value);
            }
            if (rate <= 0f) rate = Screen.currentResolution.refreshRate;
            return rate;
        }
    }

    /// <summary>Allocation-free (steady state) per-subsystem timers surfaced by the summary.</summary>
    public static class PerfMonitor
    {
        private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

        internal struct Slot
        {
            public string Name;
            public int FrameStamp;
            public double FrameMs;
            public double WindowMs;
            public double WindowMax;
            public long WindowCalls;
        }

        public readonly struct Scope : IDisposable
        {
            private readonly int index;
            private readonly long start;

            internal Scope(int index)
            {
                this.index = index;
                start = Stopwatch.GetTimestamp();
            }

            public void Dispose() =>
                Record(index, (Stopwatch.GetTimestamp() - start) * MsPerTick);
        }

        private static readonly Dictionary<string, int> Indices = new Dictionary<string, int>();
        private static readonly Dictionary<string, long> Counts = new Dictionary<string, long>();
        private static readonly Dictionary<string, long> WindowCounts = new Dictionary<string, long>();
        private static Slot[] slots = new Slot[16];
        private static int slotCount;

        public static Scope Sample(string name)
        {
            if (!Indices.TryGetValue(name, out var index))
            {
                if (slotCount == slots.Length) Array.Resize(ref slots, slots.Length * 2);
                index = slotCount++;
                slots[index] = new Slot { Name = name, FrameStamp = -1 };
                Indices.Add(name, index);
            }
            return new Scope(index);
        }

        public static void Count(string name, int amount = 1)
        {
            Counts.TryGetValue(name, out var total);
            Counts[name] = total + amount;
            WindowCounts.TryGetValue(name, out var window);
            WindowCounts[name] = window + amount;
        }

        private static void Record(int index, double ms)
        {
            ref var slot = ref slots[index];
            var frame = Time.frameCount;
            if (slot.FrameStamp != frame)
            {
                slot.FrameStamp = frame;
                slot.FrameMs = 0;
            }
            slot.FrameMs += ms;
            slot.WindowMs += ms;
            if (ms > slot.WindowMax) slot.WindowMax = ms;
            slot.WindowCalls++;
        }

        internal static void ResetWindow()
        {
            for (var index = 0; index < slotCount; index++)
            {
                slots[index].WindowMs = 0;
                slots[index].WindowMax = 0;
                slots[index].WindowCalls = 0;
            }
            WindowCounts.Clear();
        }

        internal static string DescribeFrameScopes(int limit)
        {
            var builder = new StringBuilder(160);
            var used = 0;
            ClearPicked();
            limit = Mathf.Min(limit, Picked.Length);
            while (used < limit)
            {
                var best = -1;
                var bestMs = 0.0;
                for (var index = 0; index < slotCount; index++)
                {
                    if (slots[index].FrameStamp != Time.frameCount) continue;
                    if (IsPicked(index)) continue;
                    if (slots[index].FrameMs <= bestMs) continue;
                    bestMs = slots[index].FrameMs;
                    best = index;
                }
                if (best < 0) break;
                Picked[used] = best;
                if (used > 0) builder.Append(',');
                builder.Append(slots[best].Name).Append('=').Append(bestMs.ToString("F1"));
                used++;
            }
            return used == 0 ? "none" : builder.ToString();
        }

        private static readonly int[] Picked = new int[8];
        private static readonly string[] PickedKeys = new string[8];

        internal static string DescribeWindowScopes(int limit)
        {
            var builder = new StringBuilder(320);
            var used = 0;
            ClearPicked();
            limit = Mathf.Min(limit, Picked.Length);
            while (used < limit)
            {
                var best = -1;
                var bestMs = 0.0;
                for (var index = 0; index < slotCount; index++)
                {
                    if (slots[index].WindowCalls == 0) continue;
                    if (IsPicked(index)) continue;
                    if (slots[index].WindowMax <= bestMs) continue;
                    bestMs = slots[index].WindowMax;
                    best = index;
                }
                if (best < 0) break;
                Picked[used] = best;
                if (used > 0) builder.Append('|');
                var avg = slots[best].WindowMs / slots[best].WindowCalls;
                builder.Append(slots[best].Name).Append(":n=").Append(slots[best].WindowCalls)
                    .Append(",avg=").Append(avg.ToString("F2"))
                    .Append(",max=").Append(bestMs.ToString("F1"));
                used++;
            }
            return used == 0 ? "none" : builder.ToString();
        }

        internal static string DescribeWindowCounts(int limit)
        {
            if (WindowCounts.Count == 0) return "none";
            var builder = new StringBuilder(160);
            var used = 0;
            ClearPicked();
            limit = Mathf.Min(limit, PickedKeys.Length);
            while (used < limit)
            {
                var bestKey = string.Empty;
                var bestValue = 0L;
                foreach (var pair in WindowCounts)
                {
                    if (pair.Value <= bestValue) continue;
                    if (IsPickedKey(pair.Key)) continue;
                    bestValue = pair.Value;
                    bestKey = pair.Key;
                }
                if (bestKey.Length == 0) break;
                PickedKeys[used] = bestKey;
                if (used > 0) builder.Append(',');
                builder.Append(bestKey).Append('=').Append(bestValue);
                used++;
            }
            return used == 0 ? "none" : builder.ToString();
        }

        private static void ClearPicked()
        {
            for (var i = 0; i < Picked.Length; i++)
            {
                Picked[i] = -1;
                PickedKeys[i] = null;
            }
        }

        private static bool IsPicked(int index)
        {
            for (var i = 0; i < Picked.Length; i++)
            {
                if (Picked[i] == index) return true;
            }
            return false;
        }

        private static bool IsPickedKey(string key)
        {
            for (var i = 0; i < PickedKeys.Length; i++)
            {
                if (PickedKeys[i] == key) return true;
            }
            return false;
        }
    }
}
