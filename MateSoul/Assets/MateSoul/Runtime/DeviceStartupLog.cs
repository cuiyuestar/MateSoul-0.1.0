using System;
using System.IO;
using UnityEngine;

namespace MateSoul.Runtime
{
    /// <summary>Keeps the current and previous Android startup evidence when USB is disconnected.</summary>
    internal static class DeviceStartupLog
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private const long MaxBytes = 128 * 1024;
        private static readonly object Gate = new object();
        private static readonly string[] StartupPrefixes =
        {
            "MATESOUL_DEVICE",
            "MATESOUL_TIMELINE",
            "MATESOUL_STARTUP",
            "MATESOUL_ROOM_",
            "MATESOUL_SPAWN_",
            "MATESOUL_GEOMETRY_",
            "MATESOUL_OCCLUSION_",
            "MATESOUL_HAND_TRACKING",
            "MATESOUL_GRAB_TRACKING",
            "MATESOUL_ITEM_APPROACH",
            "MATESOUL_LIVE_NAV",
            "MATESOUL_ANCHOR",
            "MATESOUL_RESTORE_",
            "MATESOUL_NAVIGATION_",
            "MATESOUL_READY"
        };
        private static StreamWriter writer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Install()
        {
            var directory = Path.Combine(Application.persistentDataPath, "diagnostics");
            try
            {
                Directory.CreateDirectory(directory);
                var current = Path.Combine(directory, "startup-current.log");
                var previous = Path.Combine(directory, "startup-previous.log");
                if (File.Exists(current))
                {
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(current, previous);
                }
                writer = new StreamWriter(current, false) { AutoFlush = true };
                writer.WriteLine($"MATESOUL_START utc={DateTime.UtcNow:O} version={Application.version}");
                Application.logMessageReceivedThreaded += Capture;
                Application.quitting += Close;
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
        }

        private static void Capture(string message, string stack, LogType type)
        {
            if (!IsStartupEvidence(message) && type != LogType.Exception)
                return;
            lock (Gate)
            {
                if (writer == null || writer.BaseStream.Position >= MaxBytes) return;
                try
                {
                    writer.WriteLine($"{DateTime.UtcNow:O} {type} {message}");
                    if (type == LogType.Exception) writer.WriteLine(stack);
                }
                catch (IOException)
                {
                    writer.Dispose();
                    writer = null;
                }
            }
        }

        private static bool IsStartupEvidence(string message)
        {
            if (message.StartsWith("[RoomNavigation]", StringComparison.Ordinal))
                return true;
            foreach (var prefix in StartupPrefixes)
                if (message.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            return false;
        }

        private static void Close()
        {
            Application.logMessageReceivedThreaded -= Capture;
            Application.quitting -= Close;
            lock (Gate)
            {
                writer?.Dispose();
                writer = null;
            }
        }
#endif
    }
}
