using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace MateSoul.Editor
{
    // Batch export for Unity's binary startup recordings; durations are inclusive.
    public static class StartupProfileExport
    {
        [Serializable]
        private sealed class Recording
        {
            public int firstFrame, lastFrame;
            public List<Frame> frames = new List<Frame>();
        }

        [Serializable]
        private sealed class Frame
        {
            public int index, threadIndex, sampleCount;
            public string thread;
            public double startMs;
            public float durationMs;
            public List<Sample> samples = new List<Sample>();
        }

        [Serializable]
        private sealed class Sample
        {
            public int index, parentIndex, childCount;
            public string name;
            public string[] metadata;
            public double startMs;
            public float durationMs;
        }

        public static void Export()
        {
            var args = Environment.GetCommandLineArgs();
            var inputIndex = Array.IndexOf(args, "-startupProfile");
            var outputIndex = Array.IndexOf(args, "-startupProfileOutput");
            if (inputIndex < 0 || outputIndex < 0 ||
                inputIndex + 1 >= args.Length || outputIndex + 1 >= args.Length)
                throw new ArgumentException("Supply -startupProfile and -startupProfileOutput paths.");
            // Unity 2022 defaults to retaining only the last 300 imported frames.
            // Use the native history limit without changing the user's EditorPrefs.
            var historyLimit = typeof(ProfilerDriver).GetMethod("SetMaxFrameHistoryLength",
                BindingFlags.Static | BindingFlags.NonPublic) ??
                throw new InvalidOperationException("This Unity version lacks the expected profiler history API.");
            historyLimit.Invoke(null, new object[] { 2000 });
            ProfilerDriver.LoadProfile(args[inputIndex + 1], false);
            var recording = new Recording
            {
                firstFrame = ProfilerDriver.firstFrameIndex,
                lastFrame = ProfilerDriver.lastFrameIndex
            };
            if (recording.firstFrame < 0)
                throw new InvalidOperationException("The recording contains no readable frames.");
            for (var index = recording.firstFrame; index <= recording.lastFrame; index++)
            {
                for (var threadIndex = 0; threadIndex < 128; threadIndex++)
                {
                    using (var raw = ProfilerDriver.GetRawFrameDataView(index, threadIndex))
                    {
                        if (!raw.valid) break;
                        var frame = new Frame
                        {
                            index = index, threadIndex = threadIndex,
                            thread = raw.threadGroupName + "/" + raw.threadName,
                            startMs = raw.frameStartTimeMs, durationMs = raw.frameTimeMs,
                            sampleCount = raw.sampleCount
                        };
                        var parents = new Stack<KeyValuePair<int, int>>();
                        for (var sampleIndex = 0; sampleIndex < raw.sampleCount; sampleIndex++)
                        {
                            while (parents.Count > 0 && sampleIndex >= parents.Peek().Value)
                                parents.Pop();
                            var duration = raw.GetSampleTimeMs(sampleIndex);
                            var children = raw.GetSampleChildrenCountRecursive(sampleIndex);
                            if (duration >= 1 || index < recording.firstFrame + 4)
                                frame.samples.Add(new Sample
                                {
                                    index = sampleIndex,
                                    parentIndex = parents.Count > 0 ? parents.Peek().Key : -1,
                                    childCount = raw.GetSampleChildrenCount(sampleIndex),
                                    name = raw.GetSampleName(sampleIndex),
                                    metadata = ReadStringMetadata(raw, sampleIndex),
                                    startMs = raw.GetSampleStartTimeMs(sampleIndex), durationMs = duration
                                });
                            if (children > 0)
                                parents.Push(new KeyValuePair<int, int>(
                                    sampleIndex, sampleIndex + children + 1));
                        }
                        recording.frames.Add(frame);
                    }
                }
            }
            File.WriteAllText(args[outputIndex + 1], JsonUtility.ToJson(recording, true));
            Debug.Log($"MATESOUL_PROFILE_EXPORT_PASS frames={recording.firstFrame}-{recording.lastFrame} " +
                      $"threadFrames={recording.frames.Count} path={args[outputIndex + 1]}");
        }

        private static string[] ReadStringMetadata(RawFrameDataView raw, int sampleIndex)
        {
            var metadataCount = raw.GetSampleMetadataCount(sampleIndex);
            if (metadataCount == 0) return Array.Empty<string>();
            var values = new List<string>();
            var descriptions = raw.GetMarkerMetadataInfo(raw.GetSampleMarkerId(sampleIndex));
            // Some runtime markers carry values without exporting a metadata schema.
            if (descriptions == null) return Array.Empty<string>();
            var count = Math.Min(metadataCount, descriptions.Length);
            for (var index = 0; index < count; index++)
                if (descriptions[index].type.ToString().StartsWith("String", StringComparison.Ordinal))
                    values.Add(descriptions[index].name + "=" +
                               raw.GetSampleMetadataAsString(sampleIndex, index));
            return values.ToArray();
        }
    }
}
