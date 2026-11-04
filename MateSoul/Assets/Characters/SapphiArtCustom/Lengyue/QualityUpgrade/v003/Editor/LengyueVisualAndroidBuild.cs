using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Builds only the independent quality scene; never regenerates the production scene.</summary>
public static class LengyueVisualAndroidBuild
{
    [Serializable] sealed class Result
    {
        public string unity, target, backend, architecture, result, scene, apk;
        public ulong bytes, buildReportTotalBytes;
        public double seconds;
        public int errors, warnings;
        public bool deviceTested;
    }

    public static void Run()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            throw new InvalidOperationException("Launch Unity with -buildTarget Android.");
        var group = BuildTargetGroup.Android;
        var backend = PlayerSettings.GetScriptingBackend(group);
        var architecture = PlayerSettings.Android.targetArchitectures;
        var identifier = PlayerSettings.GetApplicationIdentifier(group);
        var product = PlayerSettings.productName;
        var path = LengyueVisualBuilder.Work + "/exports/MateSoul_Visual_v003.apk";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        try
        {
            PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetApplicationIdentifier(group, "com.matesoul.visual.v003");
            PlayerSettings.productName = "MateSoul Visual v003";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { LengyueVisualGameBuilder.ScenePath },
                locationPathName = path, target = BuildTarget.Android, options = BuildOptions.None
            });
            var summary = report.summary;
            File.WriteAllText(LengyueVisualBuilder.Work + "/reports/android_build.json",
                JsonUtility.ToJson(new Result {
                    unity=Application.unityVersion, target="Android", backend="IL2CPP", architecture="ARM64",
                    result=summary.result.ToString(), scene=LengyueVisualGameBuilder.ScenePath,
                    apk=Path.GetFileName(path), bytes=File.Exists(path)?(ulong)new FileInfo(path).Length:0,
                    buildReportTotalBytes=summary.totalSize, seconds=summary.totalTime.TotalSeconds,
                    errors=summary.totalErrors, warnings=summary.totalWarnings, deviceTested=false
                }, true));
            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Visual APK build failed: " + summary.result);
            AssetDatabase.ExportPackage(LengyueVisualBuilder.Root,
                LengyueVisualBuilder.Work+"/exports/Lengyue_Visual_v003.unitypackage",ExportPackageOptions.Recurse);
            Debug.Log("VISUAL_ANDROID_BUILD_COMPLETE bytes=" + new FileInfo(path).Length);
        }
        finally
        {
            PlayerSettings.SetScriptingBackend(group, backend);
            PlayerSettings.Android.targetArchitectures = architecture;
            PlayerSettings.SetApplicationIdentifier(group, identifier);
            PlayerSettings.productName = product;
            AssetDatabase.SaveAssets();
        }
    }
}
