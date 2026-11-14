using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.XR;

namespace MateSoul.Hair
{
    /// <summary>Opt-in A/B capture. Attach to the benchmark scene, not production prefabs.</summary>
    [DefaultExecutionOrder(12000)]
    public sealed class HairPerformanceProbe : MonoBehaviour
    {
        public HairSpringPhysics physics;
        public Renderer hairRenderer;
        public Transform head;
        public float warmupSeconds = 10;
        public float sampleSeconds = 30;
        public bool runOnStart = true;
        public float displayTargetHz = 90;
        public bool Finished { get; private set; }
        public string LastReportPath { get; private set; }
        readonly string[] labels = { "NoHair", "SkinnedOnly", "PhysicsBalanced", "PhysicsFull" };
        int phase = -1;
        float elapsed;
        readonly List<Sample> samples = new List<Sample>(16000);
        readonly List<Phase> phases = new List<Phase>(4);
        Quaternion headRest;
        ProfilerRecorder mainThread, gc, draws, triangles;
        readonly FrameTiming[] timing = new FrameTiming[1];

        [Serializable]
        struct Sample
        {
            public string phase;
            public float elapsed, frameMs, physicsMs;
            public double cpuMainMs, gpuMs;
            public long gcBytes, draws, triangles;
            public int joints, substeps, resets;
        }
        [Serializable]
        class Phase
        {
            public string name;
            public int frames;
            public double frameP50Ms, frameP95Ms, frameP99Ms, physicsP95Ms, mainP95Ms, gpuP95Ms;
            public float missedBudgetFraction;
        }
        [Serializable]
        class Report
        {
            public string unity, device, processor, gpu, graphicsApi;
            public bool editor, development, xrActive;
            public int width, height, eyeWidth, eyeHeight, qualityLevel;
            public float targetHz, reportedRefreshHz;
            public bool mainThreadAvailable, gcAvailable, drawsAvailable, trianglesAvailable;
            public Phase[] phases;
            public Sample[] samples;
            public string warning = "Standalone/Editor results do not certify PICO. GPU=-1 means unavailable.";
        }

        void Start()
        {
            if (runOnStart) Begin();
        }

        [ContextMenu("Begin Performance Capture")]
        public void Begin()
        {
            if (!Application.isPlaying) return;
            if (!physics || !hairRenderer || !head) throw new InvalidOperationException("Probe is not configured");
            StopRecorders();
            samples.Clear();
            phases.Clear();
            headRest = head.localRotation;
            mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1);
            triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1);
            phase = -1;
            Finished = false;
            NextPhase();
        }

        void NextPhase()
        {
            phase++;
            elapsed = 0;
            if (phase >= labels.Length) { Finish(); return; }
            hairRenderer.enabled = phase != 0;
            physics.quality = phase < 2 ? HairSpringSolver.Quality.Off :
                phase == 2 ? HairSpringSolver.Quality.Balanced : HairSpringSolver.Quality.Full;
            head.localRotation = headRest;
            physics.ResetSimulation();
            Debug.Log("HAIR_PERF_PHASE " + labels[phase]);
        }

        void LateUpdate()
        {
            if (phase < 0 || Finished) return;
            elapsed += Time.unscaledDeltaTime;
            FrameTimingManager.CaptureFrameTimings();
            uint count = FrameTimingManager.GetLatestTimings(1, timing);
            if (elapsed >= warmupSeconds)
            {
                samples.Add(new Sample {
                    phase = labels[phase], elapsed = elapsed, frameMs = Time.unscaledDeltaTime * 1000,
                    physicsMs = (float)physics.LastUpdateMilliseconds,
                    cpuMainMs = mainThread.Valid ? mainThread.LastValue / 1000000.0 : -1,
                    gpuMs = count > 0 && timing[0].gpuFrameTime > 0 ? timing[0].gpuFrameTime : -1,
                    gcBytes = gc.Valid ? gc.LastValue : -1, draws = draws.Valid ? draws.LastValue : -1,
                    triangles = triangles.Valid ? triangles.LastValue : -1,
                    joints = physics.LastSimulatedJoints, substeps = physics.LastSubsteps, resets = physics.ResetCount
                });
            }
            if (elapsed >= warmupSeconds + sampleSeconds) NextPhase();
        }

        // Drive in Update, before the spring component's LateUpdate; same trajectory per phase.
        void Update()
        {
            if (phase < 0 || Finished) return;
            head.localRotation = headRest * Quaternion.Euler(
                12 * Mathf.Sin(elapsed * 1.9f), 32 * Mathf.Sin(elapsed * 2.8f), 0);
        }

        static double P(IEnumerable<double> source, double p)
        {
            var sorted = source.Where(x => x >= 0).OrderBy(x => x).ToArray();
            return sorted.Length == 0 ? -1 : sorted[(int)((sorted.Length - 1) * p)];
        }

        void Finish()
        {
            foreach (string label in labels)
            {
                var data = samples.Where(s => s.phase == label).ToArray();
                phases.Add(new Phase {
                    name = label, frames = data.Length,
                    frameP50Ms = P(data.Select(s => (double)s.frameMs), .5),
                    frameP95Ms = P(data.Select(s => (double)s.frameMs), .95),
                    frameP99Ms = P(data.Select(s => (double)s.frameMs), .99),
                    physicsP95Ms = P(data.Select(s => (double)s.physicsMs), .95),
                    mainP95Ms = P(data.Select(s => s.cpuMainMs), .95),
                    gpuP95Ms = P(data.Select(s => s.gpuMs), .95),
                    missedBudgetFraction = data.Length == 0 ? -1 :
                        (float)data.Count(s => s.frameMs > 1000f / displayTargetHz) / data.Length
                });
            }
            float refresh = -1;
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetInstances(displays);
            foreach (var display in displays)
                if (display.running && display.TryGetDisplayRefreshRate(out float hz)) refresh = hz;
            var report = new Report {
                unity = Application.unityVersion, device = SystemInfo.deviceModel,
                processor = SystemInfo.processorType, gpu = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                editor = Application.isEditor, development = Debug.isDebugBuild, xrActive = XRSettings.isDeviceActive,
                width = Screen.width, height = Screen.height, eyeWidth = XRSettings.eyeTextureWidth,
                eyeHeight = XRSettings.eyeTextureHeight, qualityLevel = QualitySettings.GetQualityLevel(),
                targetHz = displayTargetHz, reportedRefreshHz = refresh,
                mainThreadAvailable = mainThread.Valid, gcAvailable = gc.Valid,
                drawsAvailable = draws.Valid, trianglesAvailable = triangles.Valid,
                phases = phases.ToArray(), samples = samples.ToArray()
            };
            LastReportPath = Path.Combine(Application.persistentDataPath,
                "hair-perf-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
            File.WriteAllText(LastReportPath, JsonUtility.ToJson(report, true));
            Debug.Log("HAIR_PERF_COMPLETE " + LastReportPath);
            Finished = true;
            head.localRotation = headRest;
            hairRenderer.enabled = true;
            physics.quality = HairSpringSolver.Quality.Balanced;
            physics.ResetSimulation();
            StopRecorders();
        }

        void StopRecorders()
        {
            mainThread.Dispose(); gc.Dispose(); draws.Dispose(); triangles.Dispose();
        }
        void OnDestroy() => StopRecorders();
    }
}
