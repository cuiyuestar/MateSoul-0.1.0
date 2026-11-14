using System;
using System.IO;
using UnityEngine;

namespace MateSoul.Runtime
{
    /// <summary>
    /// Keeps the current and previous Android session evidence when USB is disconnected.
    /// A session must stay readable from start to finish, so the writer also emits a
    /// heartbeat: an empty stretch then means the main thread stalled or the process was
    /// killed, never "nothing was logged".
    /// </summary>
    internal static class DeviceStartupLog
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private const long MaxBytesPerFile = 4 * 1024 * 1024;
        // 8 segments cap the on-device footprint at ~32MB. A heartbeat-only hour is
        // roughly 100KB, so this only ever trips on a pathological log storm.
        private const int MaxSegments = 8;
        // 只保留 current/previous 会让「几小时前的现场」在再启动两次后永久丢失；
        // 额外归档最近若干次会话，回捞时按时间挑文件即可。
        private const int MaxArchivedSessions = 8;
        private const string ArchiveFolder = "sessions";
        private const float HeartbeatInterval = 5f;
        // A gap this much larger than the heartbeat interval means the main thread
        // (and therefore rendering) was not ticking.
        private const float HeartbeatStallMs = 250f;
        private const long RetryOpenTicks = 2000 * TimeSpan.TicksPerMillisecond;
        private static readonly object Gate = new object();
        private static StreamWriter writer;
        private static string directory;
        private static int segment = 1;
        private static bool closed;
        private static bool limitReached;
        private static long nextOpenAttempt;
        private static float lastHeartbeat = -1f;
        private static HeartbeatDriver heartbeat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Install()
        {
            try
            {
                directory = Path.Combine(Application.persistentDataPath, "diagnostics");
                Directory.CreateDirectory(directory);
                RotatePreviousSession();
                OpenSegment(1,
                    $"MATESOUL_START utc={DateTime.UtcNow:O} version={Application.version}");
                Application.logMessageReceivedThreaded += Capture;
            }
            catch (IOException error)
            {
                Close();
                Debug.LogWarning($"MATESOUL_DIAGNOSTICS unavailable: {error.Message}");
            }
            catch (UnauthorizedAccessException error)
            {
                Close();
                Debug.LogWarning($"MATESOUL_DIAGNOSTICS unavailable: {error.Message}");
            }
            // Start the heartbeat as early as possible so a stall during loading is also
            // visible; AfterSceneLoad retries in case the splash-time host did not stick.
            InstallHeartbeatHost();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallHeartbeat()
        {
            InstallHeartbeatHost();
        }

        // Survives scene loads without depending on any gameplay object being alive.
        private static void InstallHeartbeatHost()
        {
            if (heartbeat != null || writer == null || closed || directory == null) return;
            try
            {
                var host = new GameObject("[DeviceStartupLog]");
                UnityEngine.Object.DontDestroyOnLoad(host);
                heartbeat = host.AddComponent<HeartbeatDriver>();
                WriteMarker("heartbeat", 1);
            }
            catch (Exception error)
            {
                // A missing heartbeat must not take the log down with it.
                Debug.LogWarning($"MATESOUL_DIAGNOSTICS heartbeat unavailable: " +
                                 $"{error.Message}");
            }
        }

        private static void Capture(string message, string stack, LogType type)
        {
            if (!ShouldCapture(message, type)) return;
            lock (Gate)
            {
                if (!EnsureWritable()) return;
                WriteLine($"{DateTime.UtcNow:O} {type} {message}");
                if (type == LogType.Exception && !string.IsNullOrEmpty(stack))
                    WriteLine(stack);
            }
        }

        // Errors and exceptions are evidence even without a MateSoul prefix; a run that
        // dies silently is exactly what this log exists to prevent.
        private static bool ShouldCapture(string message, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                return true;
            return IsStartupEvidence(message);
        }

        // Any MateSoul marker is named deliberately for this log; a prefix whitelist
        // silently dropped new markers (the item use gesture, for one), so a run
        // could look like it never happened. Match the namespace instead.
        private static bool IsStartupEvidence(string message)
        {
            if (message.StartsWith("[RoomNavigation]", StringComparison.Ordinal))
                return true;
            return message.StartsWith("MATESOUL_", StringComparison.Ordinal);
        }

        private static void WriteHeartbeat(float now, int frame, float fps, float frameMs,
            bool paused, bool focused)
        {
            if (lastHeartbeat >= 0f && now - lastHeartbeat < HeartbeatInterval) return;
            var stall = 0f;
            if (lastHeartbeat >= 0f)
            {
                stall = (now - lastHeartbeat) * 1000f - HeartbeatInterval * 1000f;
                if (stall < HeartbeatStallMs) stall = 0f;
            }
            lastHeartbeat = now;
            lock (Gate)
            {
                if (!EnsureWritable()) return;
                WriteLine($"{DateTime.UtcNow:O} Log MATESOUL_HEARTBEAT frame={frame} " +
                          $"uptime={now:F1}s fps={fps:F1} frameMs={frameMs:F1} " +
                          $"stallMs={stall:F0} paused={(paused ? 1 : 0)} " +
                          $"focus={(focused ? 1 : 0)} " +
                          $"heapMb={GC.GetTotalMemory(false) >> 20} segment={segment}");
            }
        }

        private static void WriteMarker(string kind, int value)
        {
            lock (Gate)
            {
                if (!EnsureWritable()) return;
                WriteLine($"{DateTime.UtcNow:O} Log MATESOUL_DIAGNOSTICS " +
                          $"{kind}={value} segment={segment} " +
                          $"uptime={Time.realtimeSinceStartup:F1}s");
            }
        }

        // Rolls to the next segment instead of going silent at the byte cap, and
        // re-opens after a transient write failure so one bad write cannot end the run
        // log. The caller must hold Gate.
        private static bool EnsureWritable()
        {
            if (closed || limitReached) return false;
            if (writer == null)
            {
                // Retry at most every RetryOpenTicks so a broken volume cannot turn
                // every captured message into a failed file open.
                if (DateTime.UtcNow.Ticks < nextOpenAttempt) return false;
                nextOpenAttempt = DateTime.UtcNow.Ticks + RetryOpenTicks;
                if (!TryOpen(segment, true,
                        $"MATESOUL_DIAGNOSTICS reopen segment={segment} " +
                        $"utc={DateTime.UtcNow:O}"))
                    return false;
            }
            if (writer.BaseStream.Position < MaxBytesPerFile) return true;
            if (segment >= MaxSegments)
            {
                limitReached = true;
                WriteLine($"{DateTime.UtcNow:O} Log MATESOUL_DIAGNOSTICS limit " +
                          $"segments={segment} bytes={writer.BaseStream.Position}");
                DropWriter();
                return false;
            }
            var next = segment + 1;
            if (!TryOpen(next, false,
                    $"MATESOUL_DIAGNOSTICS segment={next} previous={SegmentPath(segment)} " +
                    $"utc={DateTime.UtcNow:O}"))
                return false;
            return writer != null;
        }

        private static bool TryOpen(int index, bool append, string header)
        {
            try
            {
                OpenSegment(index, header, append);
                return true;
            }
            catch (IOException)
            {
                DropWriter();
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                DropWriter();
                return false;
            }
        }

        private static void OpenSegment(int index, string header, bool append = false)
        {
            writer?.Dispose();
            writer = null;
            segment = index;
            writer = new StreamWriter(SegmentPath(index), append) { AutoFlush = true };
            if (!string.IsNullOrEmpty(header)) writer.WriteLine(header);
        }

        // Every line is flushed immediately (AutoFlush) so a killed process keeps the
        // evidence up to its last write. The caller must hold Gate.
        private static void WriteLine(string line)
        {
            if (writer == null) return;
            try
            {
                writer.WriteLine(line);
            }
            catch (IOException)
            {
                DropWriter();
            }
            catch (UnauthorizedAccessException)
            {
                DropWriter();
            }
        }

        private static void DropWriter()
        {
            try
            {
                writer?.Dispose();
            }
            catch (Exception)
            {
                // A failed flush must not mask the original write failure.
            }
            writer = null;
        }

        private static string SegmentPath(int index) => index <= 1
            ? Path.Combine(directory, "startup-current.log")
            : Path.Combine(directory, $"startup-current-part{index}.log");

        private static string PreviousSegmentPath(int index) => index <= 1
            ? Path.Combine(directory, "startup-previous.log")
            : Path.Combine(directory, $"startup-previous-part{index}.log");

        // Keeps exactly one previous session on disk, including its segments.
        private static void RotatePreviousSession()
        {
            ArchivePreviousSession();
            var current = SegmentPath(1);
            var previous = PreviousSegmentPath(1);
            if (File.Exists(current))
            {
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(current, previous);
            }
            for (var index = 2; index <= MaxSegments; index++)
            {
                var part = SegmentPath(index);
                if (!File.Exists(part)) continue;
                var kept = PreviousSegmentPath(index);
                if (File.Exists(kept)) File.Delete(kept);
                File.Move(part, kept);
            }
        }

        // 把上一次会话整体移到 sessions/，文件名带会话起始 UTC 时间，便于回捞。
        private static void ArchivePreviousSession()
        {
            try
            {
                var first = PreviousSegmentPath(1);
                if (!File.Exists(first)) return;
                var archiveDirectory = Path.Combine(directory, ArchiveFolder);
                Directory.CreateDirectory(archiveDirectory);
                var tag = ReadSessionTag(first) ??
                          File.GetLastWriteTimeUtc(first).ToString("yyyyMMdd-HHmmss");
                for (var index = 1; index <= MaxSegments; index++)
                {
                    var source = PreviousSegmentPath(index);
                    if (!File.Exists(source)) continue;
                    var name = index <= 1
                        ? $"startup-{tag}.log"
                        : $"startup-{tag}-part{index}.log";
                    var destination = Path.Combine(archiveDirectory, name);
                    if (File.Exists(destination)) File.Delete(destination);
                    File.Move(source, destination);
                }
                PruneArchives(archiveDirectory);
            }
            catch (IOException)
            {
                // 归档失败不能阻断本次启动日志。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string ReadSessionTag(string path)
        {
            try
            {
                using (var reader = new StreamReader(path))
                {
                    var line = reader.ReadLine();
                    if (string.IsNullOrEmpty(line)) return null;
                    const string marker = "utc=";
                    var start = line.IndexOf(marker, StringComparison.Ordinal);
                    if (start < 0) return null;
                    start += marker.Length;
                    if (line.Length < start + 19) return null;
                    return $"{line.Substring(start, 4)}{line.Substring(start + 5, 2)}" +
                           $"{line.Substring(start + 8, 2)}-" +
                           $"{line.Substring(start + 11, 2)}" +
                           $"{line.Substring(start + 14, 2)}" +
                           $"{line.Substring(start + 17, 2)}";
                }
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static void PruneArchives(string archiveDirectory)
        {
            var tags = new System.Collections.Generic.List<string>();
            foreach (var path in Directory.GetFiles(archiveDirectory, "startup-*.log"))
            {
                var name = Path.GetFileName(path);
                if (name.Length < 23) continue;
                var tag = name.Substring(8, 15);
                if (tag.Length != 15 || tag[8] != '-') continue;
                if (!tags.Contains(tag)) tags.Add(tag);
            }
            tags.Sort(StringComparer.Ordinal);
            while (tags.Count > MaxArchivedSessions)
            {
                var tag = tags[0];
                tags.RemoveAt(0);
                foreach (var path in Directory.GetFiles(archiveDirectory,
                             $"startup-{tag}*.log"))
                    File.Delete(path);
            }
        }

        private static void Close()
        {
            closed = true;
            Application.logMessageReceivedThreaded -= Capture;
            lock (Gate)
            {
                DropWriter();
            }
        }

        private sealed class HeartbeatDriver : MonoBehaviour
        {
            private float lastTick = -1f;
            private int lastFrame;
            private bool paused;
            private bool focused = true;

            private void Update()
            {
                var now = Time.realtimeSinceStartup;
                var delta = lastTick < 0f ? 0f : now - lastTick;
                var frames = lastFrame <= 0 ? 0 : Time.frameCount - lastFrame;
                var fps = delta > .0001f && frames > 0 ? frames / delta : 0f;
                var frameMs = frames > 0 ? delta * 1000f / frames : 0f;
                lastTick = now;
                lastFrame = Time.frameCount;
                WriteHeartbeat(now, Time.frameCount, fps, frameMs, paused, focused);
            }

            private void OnApplicationPause(bool value)
            {
                paused = value;
                WriteMarker("pause", value ? 1 : 0);
            }

            private void OnApplicationFocus(bool value)
            {
                focused = value;
                WriteMarker("focus", value ? 1 : 0);
            }

            private void OnApplicationQuit()
            {
                WriteMarker("quit", 1);
                Close();
            }
        }
#endif
    }
}
