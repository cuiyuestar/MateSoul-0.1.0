#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor.Android;

namespace MateSoul.Editor
{
    // PICO 语音转文字需要录音权限，Unity 默认清单不会声明，构建时补入。
    public sealed class VoiceInputBuildStep : IPostGenerateGradleAndroidProject
    {
        private const string RecordAudio = "android.permission.RECORD_AUDIO";

        public int callbackOrder => 10001;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath)) return;
            var manifest = File.ReadAllText(manifestPath);
            if (manifest.Contains(RecordAudio)) return;
            var manifestStart = manifest.IndexOf("<manifest", StringComparison.Ordinal);
            if (manifestStart < 0) return;
            var insertAt = manifest.IndexOf('>', manifestStart);
            if (insertAt < 0) return;
            manifest = manifest.Insert(insertAt + 1,
                "\n    <uses-permission android:name=\"" + RecordAudio + "\" />");
            File.WriteAllText(manifestPath, manifest);
        }
    }
}
#endif
