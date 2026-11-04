using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.XR;

namespace MateSoul.RoomNavigation
{
    [DefaultExecutionOrder(-900)]
    [DisallowMultipleComponent]
    public sealed class RoomEnvironmentCapabilityProbe : MonoBehaviour
    {
        [SerializeField, Min(2f)]
        private float timeoutSeconds = 8f;

        private readonly List<XRMeshSubsystem> meshSubsystems = new List<XRMeshSubsystem>();
        private CancellationTokenSource lifetimeCancellation;

#if UNITY_ANDROID && !UNITY_EDITOR
        private async void Start()
        {
            if (!IsPicoEmulator())
            {
                return;
            }

            lifetimeCancellation = new CancellationTokenSource();
            try
            {
                await ProbeAsync(lifetimeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogError($"ROOM_ENVIRONMENT_CAPABILITY_PROBE_ERROR {exception}");
            }
        }
#endif

        private async Task ProbeAsync(CancellationToken lifetimeToken)
        {
            var report = new CapabilityReport
            {
                deviceModel = SystemInfo.deviceModel,
                deviceName = SystemInfo.deviceName,
                unityVersion = Application.unityVersion,
                sceneProviderStartResult = "NotRun",
                sceneQueryResult = "NotRun",
                meshQueryResult = "NotRun"
            };

            using (var sceneTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken))
            {
                sceneTimeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                try
                {
                    await ProbeSceneAnchorsAsync(report, sceneTimeout.Token);
                }
                catch (OperationCanceledException)
                {
                    report.sceneQueryResult = "Timeout";
                }
            }

            using (var meshTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken))
            {
                meshTimeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                try
                {
                    await ProbeSpatialMeshAsync(report, meshTimeout.Token);
                }
                catch (OperationCanceledException)
                {
                    report.meshQueryResult = "Timeout";
                }
            }

            report.nativeEnvironmentDataUsable =
                report.sceneAnchorCount > 0 ||
                (report.meshBlockCount > 0 &&
                 report.meshVertexCount > 0 &&
                 report.meshIndexCount > 0);
            report.recommendedSource = report.nativeEnvironmentDataUsable
                ? "PICO Emulator native environment data"
                : "Toren-aligned fixture fallback";

            var json = JsonUtility.ToJson(report, true);
            var reportPath = Path.Combine(
                Application.persistentDataPath,
                "room_environment_capability_v001.json");
            File.WriteAllText(reportPath, json);
            Debug.Log($"ROOM_ENVIRONMENT_CAPABILITY_PROBE\n{json}");
        }

        private static async Task ProbeSceneAnchorsAsync(
            CapabilityReport report,
            CancellationToken cancellationToken)
        {
            var providerStarted = false;
            try
            {
                var startResult = await PXR_MixedReality.StartSenseDataProvider(
                    PxrSenseDataProviderType.SceneCapture,
                    cancellationToken);
                report.sceneProviderStartResult = startResult.ToString();
                if (startResult != PxrResult.SUCCESS)
                {
                    return;
                }

                providerStarted = true;
                var queryResult = await PXR_MixedReality.QuerySceneAnchorAsync(cancellationToken);
                report.sceneQueryResult = queryResult.result.ToString();
                report.sceneAnchorCount = queryResult.anchorDictionary?.Count ?? 0;
                if (queryResult.anchorDictionary == null)
                {
                    return;
                }

                foreach (var pair in queryResult.anchorDictionary)
                {
                    if (PXR_MixedReality.GetSceneSemanticLabel(pair.Key, out var label) ==
                        PxrResult.SUCCESS)
                    {
                        AddSemanticCount(report.sceneSemantics, label.ToString());
                    }
                }
            }
            finally
            {
                if (providerStarted)
                {
                    PXR_MixedReality.StopSenseDataProvider(
                        PxrSenseDataProviderType.SceneCapture);
                }
            }
        }

        private async Task ProbeSpatialMeshAsync(
            CapabilityReport report,
            CancellationToken cancellationToken)
        {
            SubsystemManager.GetSubsystems(meshSubsystems);
            report.meshSubsystemCount = meshSubsystems.Count;
            foreach (var subsystem in meshSubsystems)
            {
                if (!subsystem.running)
                {
                    subsystem.Start();
                }

                report.meshSubsystemRunning |= subsystem.running;
            }

            do
            {
                var queryResult = await PXR_MixedReality.QueryMeshAnchorAsync(cancellationToken);
                report.meshQueryResult = queryResult.result.ToString();
                AccumulateSpatialMesh(report, queryResult.meshInfos);
                if (report.meshBlockCount > 0)
                {
                    return;
                }

                await Task.Delay(500, cancellationToken);
            }
            while (!cancellationToken.IsCancellationRequested);
        }

        private static void AccumulateSpatialMesh(
            CapabilityReport report,
            IReadOnlyList<PxrSpatialMeshInfo> meshInfos)
        {
            report.meshBlockCount = 0;
            report.meshVertexCount = 0;
            report.meshIndexCount = 0;
            report.meshSemantics.Clear();
            if (meshInfos == null)
            {
                return;
            }

            foreach (var meshInfo in meshInfos)
            {
                if (meshInfo.state == MeshChangeState.Removed)
                {
                    continue;
                }

                report.meshBlockCount++;
                report.meshVertexCount += meshInfo.vertices?.Length ?? 0;
                report.meshIndexCount += meshInfo.indices?.Length ?? 0;
                if (meshInfo.labels == null)
                {
                    continue;
                }

                foreach (var label in meshInfo.labels)
                {
                    AddSemanticCount(report.meshSemantics, label.ToString());
                }
            }
        }

        private static void AddSemanticCount(List<SemanticCount> counts, string label)
        {
            foreach (var item in counts)
            {
                if (string.Equals(item.label, label, StringComparison.Ordinal))
                {
                    item.count++;
                    return;
                }
            }

            counts.Add(new SemanticCount
            {
                label = label,
                count = 1
            });
        }

        private static bool IsPicoEmulator()
        {
            return ContainsIgnoreCase(SystemInfo.deviceModel, "swan_arm64") ||
                   ContainsIgnoreCase(SystemInfo.deviceName, "pico_spaceos_emulator");
        }

        private static bool ContainsIgnoreCase(string value, string expected)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnDestroy()
        {
            if (lifetimeCancellation == null)
            {
                return;
            }

            lifetimeCancellation.Cancel();
            lifetimeCancellation.Dispose();
            lifetimeCancellation = null;
        }

        [Serializable]
        private sealed class CapabilityReport
        {
            public string deviceModel;
            public string deviceName;
            public string unityVersion;
            public string sceneProviderStartResult;
            public string sceneQueryResult;
            public int sceneAnchorCount;
            public List<SemanticCount> sceneSemantics = new List<SemanticCount>();
            public int meshSubsystemCount;
            public bool meshSubsystemRunning;
            public string meshQueryResult;
            public int meshBlockCount;
            public int meshVertexCount;
            public int meshIndexCount;
            public List<SemanticCount> meshSemantics = new List<SemanticCount>();
            public bool nativeEnvironmentDataUsable;
            public string recommendedSource;
        }

        [Serializable]
        private sealed class SemanticCount
        {
            public string label;
            public int count;
        }
    }
}
