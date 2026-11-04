using System.IO;
using UnityEditor;
using UnityEngine;

namespace MateSoul.Editor
{
    [InitializeOnLoad]
    internal static class BuildItemFocusDebugOnce
    {
        private const string Key = "MateSoul.ItemFocusDebugBuildStarted";
        private static string RequestedBuild => Path.GetFullPath(
            Path.Combine(Application.dataPath, "../../.artifacts/item-focus-debug/build.request"));
        static BuildItemFocusDebugOnce()
        {
            if (!File.Exists(RequestedBuild) || SessionState.GetBool(Key, false)) return;
            File.Delete(RequestedBuild);
            SessionState.SetBool(Key, true);
            EditorApplication.update += BuildWhenReady;
        }

        private static void BuildWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= BuildWhenReady;
            var previous = PlayerSettings.insecureHttpOption;
            try
            {
                PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
                GameSceneBuilder.BuildAndroidDevelopment();
            }
            finally
            {
                PlayerSettings.insecureHttpOption = previous;
            }
        }
    }
}
