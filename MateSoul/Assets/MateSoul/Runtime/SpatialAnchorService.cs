using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ByteDance.PICO.XR;
using UnityEngine;

namespace MateSoul.Runtime
{
    public interface ISpatialAnchorService : IDisposable
    {
        Task<string> CreateAsync(Pose pose, CancellationToken token);
        Task<Pose> LoadAsync(string id, CancellationToken token);
    }

    // Editor mock room has a fixed origin. These IDs are never accepted on a headset.
    public sealed class EditorRoomAnchorService : ISpatialAnchorService
    {
        public Task<string> CreateAsync(Pose pose, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult("editor-mock-room");
        }
        public Task<Pose> LoadAsync(string id, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (id != "editor-mock-room") throw new InvalidOperationException("此存档不属于编辑器房间。");
            return Task.FromResult(Pose.identity);
        }
        public void Dispose() { }
    }

    public sealed class PicoSpatialAnchorService : ISpatialAnchorService
    {
        private bool started, disposed;
        private int operations;
        private readonly HashSet<ulong> handles = new HashSet<ulong>();

        private async Task StartAsync(CancellationToken token)
        {
            if (disposed) throw new ObjectDisposedException(nameof(PicoSpatialAnchorService));
            if (!started)
            {
                Check(await PXR_MixedReality.StartSenseDataProvider(
                    PxrSenseDataProviderType.SpatialAnchor, token));
                started = true;
            }
            token.ThrowIfCancellationRequested();
        }

        public async Task<string> CreateAsync(Pose pose, CancellationToken token)
        {
            operations++;
            try
            {
                await StartAsync(token);
                var created = await PXR_MixedReality.CreateSpatialAnchorAsync(pose.position, pose.rotation, token);
                Check(created.result);
                handles.Add(created.anchorHandle);
                Check(await PXR_MixedReality.PersistSpatialAnchorAsync(
                    created.anchorHandle, token));
                token.ThrowIfCancellationRequested();
                return created.uuid.ToString();
            }
            finally { operations--; ReleaseIfDisposed(); }
        }

        public async Task<Pose> LoadAsync(string id, CancellationToken token)
        {
            var uuid = Guid.Parse(id);
            operations++;
            var updateVersion = 0;
            var dataUpdated = new TaskCompletionSource<bool>();
            Action onDataUpdated = () =>
            {
                updateVersion++;
                dataUpdated.TrySetResult(true);
            };
            // Subscribe before provider startup so an early data update cannot be missed.
            PXR_Manager.SpatialAnchorDataUpdated += onDataUpdated;
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await StartAsync(token);
                var deadline = elapsed.Elapsed + TimeSpan.FromSeconds(2);
                var lastFailure = "未查询到对应 UUID";
                var queryCount = 0;
                while (true)
                {
                    dataUpdated = new TaskCompletionSource<bool>();
                    var queriedVersion = updateVersion;
                    // PICO's own sample reloads all persisted anchors after the provider data update.
                    // Querying all also avoids an emulator race in the UUID-filtered query path.
                    var query = await PXR_MixedReality.QuerySpatialAnchorAsync(null, token);
                    queryCount++;
                    var located = false;
                    var pose = Pose.identity;
                    if (query.result == PxrResult.SUCCESS)
                    {
                        lastFailure = "未查询到对应 UUID";
                        foreach (var handle in query.anchorHandleList)
                        {
                            var uuidResult = PXR_MixedReality.GetAnchorUuid(handle, out var found);
                            if (!located && uuidResult == PxrResult.SUCCESS && found == uuid)
                            {
                                var locateResult = PXR_MixedReality.LocateAnchor(
                                    handle, out var position, out var rotation);
                                if (locateResult == PxrResult.SUCCESS)
                                {
                                    handles.Add(handle);
                                    pose = new Pose(position, rotation);
                                    located = true;
                                    continue;
                                }
                                lastFailure = locateResult.ToString();
                            }
                            if (!handles.Contains(handle)) PXR_MixedReality.DestroyAnchor(handle);
                        }
                    }
                    else lastFailure = query.result.ToString();
                    token.ThrowIfCancellationRequested();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    Debug.Log($"MATESOUL_ANCHOR query={queryCount} elapsedMs={elapsed.ElapsedMilliseconds} " +
                              $"result={query.result} located={located} updates={updateVersion}");
#endif
                    if (located) return pose;
                    var remaining = deadline - elapsed.Elapsed;
                    if (remaining <= TimeSpan.Zero) break;
                    if (queriedVersion != updateVersion) continue;
                    // Empty results may mean the provider has not loaded persisted data yet.
                    // Wake on a data update, with one final query when the bounded wait expires.
                    await Task.WhenAny(dataUpdated.Task, Task.Delay(remaining, token));
                    token.ThrowIfCancellationRequested();
                }
                throw new InvalidOperationException($"房间锚点尚未定位：{lastFailure}");
            }
            finally
            {
                PXR_Manager.SpatialAnchorDataUpdated -= onDataUpdated;
                operations--;
                ReleaseIfDisposed();
            }
        }

        private static void Check(PxrResult result)
        {
            if (result != PxrResult.SUCCESS) throw new InvalidOperationException($"Spatial Anchor: {result}");
        }

        public void Dispose() { disposed = true; ReleaseIfDisposed(); }
        private void ReleaseIfDisposed()
        {
            if (!disposed || operations != 0) return;
            foreach (var handle in handles) PXR_MixedReality.DestroyAnchor(handle);
            handles.Clear();
            if (started) PXR_MixedReality.StopSenseDataProvider(PxrSenseDataProviderType.SpatialAnchor);
            started = false;
        }
    }
}
