using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace MateSoul.Tests
{
    // Uses Unity's supported in-process runner, including across PlayMode reloads.
    [InitializeOnLoad]
    public static class FrameworkTestMenu
    {
        private const string ActiveRun = "MateSoul.FrameworkTests.Active";
        private static readonly TestRunnerApi Runner;
        private static double nextPoll;
        private static string OutputDirectory => Path.GetFullPath(
            Path.Combine(Application.dataPath, "../../.artifacts/framework"));
        private static string RequestedRun => Path.Combine(OutputDirectory, "run-tests.request");

        static FrameworkTestMenu()
        {
            Runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            Runner.RegisterCallbacks(new Results());
            EditorApplication.update += RunRequested;
        }

        [MenuItem("MateSoul/Game/Run Framework Tests")]
        public static void Run() => RunFilter(null);

        [MenuItem("MateSoul/Game/Run Seating Tests")]
        public static void RunSeating() => RunFilter("MateSoul.Tests.SeatingTests");

        private static void RunRequested()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode ||
                SessionState.GetBool(ActiveRun, false)) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            if (!File.Exists(RequestedRun)) return;
            var requested = File.ReadAllText(RequestedRun).Trim();
            File.Delete(RequestedRun);
            RunFilter(requested.StartsWith("MateSoul.Tests.", System.StringComparison.Ordinal)
                ? requested : null);
        }

        private static void RunFilter(string suite)
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            try { MateSoul.Editor.GameSceneBuilder.BuildScene(); }
            catch (Exception error)
            {
                Directory.CreateDirectory(OutputDirectory);
                File.WriteAllText(Path.Combine(OutputDirectory, "editor-test-progress.log"),
                    "Scene build failed\n" + error);
                SessionState.SetBool(ActiveRun, false);
                Debug.LogException(error);
                if (Environment.GetEnvironmentVariable("MATESOUL_EXIT_AFTER_TESTS") == "1")
                    EditorApplication.Exit(2);
                return;
            }
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            Directory.CreateDirectory(OutputDirectory);
            File.WriteAllText(Path.Combine(OutputDirectory, "editor-test-progress.log"), "Started\n");
            SessionState.SetBool(ActiveRun, true);
            Runner.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                testNames = suite?.Split(';'),
                assemblyNames = new[] { "MateSoul.Tests.EditMode", "MateSoul.Tests.Engine" }
            }));
        }

        private sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test)
            {
                if (SessionState.GetBool(ActiveRun, false) && !test.IsSuite)
                    File.AppendAllText(Path.Combine(OutputDirectory, "editor-test-progress.log"), test.FullName + "\n");
            }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                if (!SessionState.GetBool(ActiveRun, false)) return;
                SessionState.SetBool(ActiveRun, false);
                File.WriteAllText(Path.Combine(OutputDirectory, "unity-tests.xml"), result.ToXml().OuterXml);
                Debug.Log($"MATESOUL_TESTS_FINISHED passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
                if (Environment.GetEnvironmentVariable("MATESOUL_EXIT_AFTER_TESTS") == "1")
                    EditorApplication.Exit(result.FailCount == 0 ? 0 : 1);
            }
        }
    }
}
