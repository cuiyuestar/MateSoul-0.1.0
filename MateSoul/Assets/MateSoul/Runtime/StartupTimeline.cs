using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace MateSoul.Runtime
{
    // Development-only observations. All Unity APIs stay on the main thread.
    internal static class StartupTimeline
    {
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        private static readonly Stopwatch Clock = new Stopwatch();
        private static bool firstLoop, firstBeforeRender, sawUnfinishedSplash, observedFinishedSplash;
        private static long lastUnfinishedMs;
        private static bool recording;
        private static int stopProfileFrame = int.MaxValue;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void BeforeSplashScreen()
        {
            Clock.Restart();
            Mark("before_splash_screen");
            SceneManager.sceneLoaded += SceneLoaded;
            Application.onBeforeRender += BeforeRender;
            Application.quitting += Cleanup;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var children = loop.subSystemList;
            var updated = new PlayerLoopSystem[children.Length + 1];
            updated[0] = new PlayerLoopSystem { type = typeof(StartupTimeline), updateDelegate = Tick };
            Array.Copy(children, 0, updated, 1, children.Length);
            loop.subSystemList = updated;
            PlayerLoop.SetPlayerLoop(loop);

            // Opt in for a single diagnostic run; regular measurements only emit boundary logs.
            if (!File.Exists(Path.Combine(Application.persistentDataPath, "startup-profile.enabled")))
                return;
            Profiler.logFile = Path.Combine(Application.persistentDataPath, "startup-profile.raw");
            Profiler.enableBinaryLog = true;
            Profiler.enabled = true;
            recording = true;
            Mark("profiler_started");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BeforeSceneLoad() => Mark("before_scene_load");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad() => Mark("after_scene_load");

        private static void SceneLoaded(Scene scene, LoadSceneMode mode)
            => Mark("scene_loaded");

        private static void BeforeRender()
        {
            if (firstBeforeRender) return;
            firstBeforeRender = true;
            Mark("first_before_render_callback");
            Application.onBeforeRender -= BeforeRender;
        }

        private static void Tick()
        {
            if (!firstLoop)
            {
                firstLoop = true;
                Mark("first_player_loop");
            }
            ObserveSplash(SplashScreen.isFinished);
            if (recording && (Time.frameCount >= stopProfileFrame || Clock.ElapsedMilliseconds >= 30000))
            {
                Profiler.enabled = false;
                Profiler.enableBinaryLog = false;
                recording = false;
                Mark("profiler_stopped");
            }
            if (Time.frameCount >= stopProfileFrame) Cleanup();
        }

        private static void ObserveSplash(bool finished)
        {
            if (observedFinishedSplash) return;
            if (!finished)
            {
                sawUnfinishedSplash = true;
                lastUnfinishedMs = Clock.ElapsedMilliseconds;
            }
            else if (sawUnfinishedSplash)
            {
                observedFinishedSplash = true;
                Debug.Log($"MATESOUL_TIMELINE stage=splash_finished_observed " +
                          $"sinceBeforeSplashMs={Clock.ElapsedMilliseconds} " +
                          $"lastUnfinishedMs={lastUnfinishedMs} frame={Time.frameCount}");
            }
        }
#endif

        [Conditional("DEVELOPMENT_BUILD")]
        public static void Mark(string stage)
        {
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            var finished = SplashScreen.isFinished;
            Debug.Log($"MATESOUL_TIMELINE stage={stage} sinceBeforeSplashMs={Clock.ElapsedMilliseconds} " +
                      $"frame={Time.frameCount} splashFinished={finished}");
            ObserveSplash(finished);
            if (stage == "business_ready") stopProfileFrame = Time.frameCount + 2;
#endif
        }

#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        private static void Cleanup()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            Application.onBeforeRender -= BeforeRender;
            Application.quitting -= Cleanup;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            loop.subSystemList = Array.FindAll(loop.subSystemList,
                child => child.type != typeof(StartupTimeline));
            PlayerLoop.SetPlayerLoop(loop);
            if (recording)
            {
                Profiler.enabled = false;
                Profiler.enableBinaryLog = false;
                recording = false;
            }
        }
#endif
    }
}
