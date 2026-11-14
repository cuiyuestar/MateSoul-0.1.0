using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ByteDance.PICO.XR;
using MateSoul.RoomNavigation;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed class RealChairScanner : IDisposable
    {
        // Extraction walks the room mesh; it is pointless to repeat it faster than
        // the mesh source republishes geometry.
        private const float MinimumScanInterval = .5f;
        // Standing next to the chair hides it from the headset's own sensors. Two
        // missed publishes (~1s) keep the prompt alive; the third revokes it.
        private const int MissedScanGrace = 2;
        private readonly RealityItemDirectory realityItems;
        private readonly RoomMapSnapshot room;
        private readonly RealityMeshSource meshSource;
        private readonly float floorY;
        private readonly List<SeatTarget> targets = new List<SeatTarget>();
        private readonly object scanGate = new object();
        private List<RealityTriangle> triangles = new List<RealityTriangle>();
        private float nextCachedConfirmation;
        // Unconditional on purpose: Release builds compile out the development
        // probes, and a blocked seat must still be diagnosable from the device log.
        private float nextScanLog;
        private string lastScanSignature;
        private List<RealityTriangle> pending;
        private float nextScan;
        private int scanBusy;
        private BackgroundScan completed;
        private bool disposed;

        private sealed class BackgroundScan
        {
            public List<RealityTriangle> Geometry;
            public float Now;
            public List<SeatTarget> Observed;
            public SeatScanDiagnostics Diagnostics;
            public long ElapsedMs;
        }
        public IEnumerable<SeatTarget> Targets => targets.Where(t => t.IsFresh);
        public IReadOnlyList<RealityTriangle> Geometry => triangles;
        public string Status { get; private set; } = "请扫描椅面、靠背和椅子前方";
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        public bool UseDevelopmentGeometry { get; set; }
#endif

        public RealChairScanner(RealityItemDirectory directory, RoomMapSnapshot snapshot,
            RealityMeshSource source = null)
        {
            realityItems = directory;
            room = snapshot;
            meshSource = source;
            floorY = snapshot.MainFloor.Centroid.y;
            if (meshSource != null) meshSource.Updated += OnMeshUpdated;
        }

        public void RefreshCached()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (UseDevelopmentGeometry) return;
#endif
            meshSource?.RefreshCached();
        }

        public void Tick(float now)
        {
            ApplyCompleted();
            Pump(now);
            if (now < nextCachedConfirmation ||
                !targets.Any(target => !target.Available && target.StableSamples > 0)) return;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (UseDevelopmentGeometry) return;
#endif
            nextCachedConfirmation = now + .5f;
            RequestScan(triangles, now);
        }

        private void OnMeshUpdated(List<RealityTriangle> geometry, float now)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (UseDevelopmentGeometry) return;
#endif
            try
            {
                RequestScan(geometry, now);
            }
            catch (Exception error)
            {
                Invalidate();
                Status = "椅子扫描失败：" + error.Message;
                Debug.LogException(error);
            }
        }

        // Seat extraction is O(room triangles x surface groups). Running it inside
        // the mesh-publish callback stalled the main thread for ~0.6s per publish
        // on device, so device builds hand the snapshot to a worker thread and
        // apply the result on a later frame. The editor stays synchronous: it has
        // no spatial mesh, and the edit-mode tests assert on the immediate result.
        private void RequestScan(List<RealityTriangle> geometry, float now)
        {
            if (disposed || geometry == null) return;
            triangles = geometry;
            if (now < nextScan)
            {
                pending = geometry;
                return;
            }
            nextScan = now + MinimumScanInterval;
            if (Application.isEditor)
            {
                pending = null;
                Observe(geometry, now);
                return;
            }
            if (Interlocked.CompareExchange(ref scanBusy, 1, 0) != 0)
            {
                pending = geometry;
                return;
            }
            pending = null;
            ThreadPool.QueueUserWorkItem(_ => ExtractInBackground(geometry, now));
        }

        // A scan that finished while another snapshot was waiting must not be
        // dropped just because no new geometry arrived.
        private void Pump(float now)
        {
            if (disposed || pending == null || now < nextScan) return;
            var geometry = pending;
            RequestScan(geometry, now);
        }

        private void ExtractInBackground(List<RealityTriangle> geometry, float now)
        {
            var scan = new BackgroundScan { Geometry = geometry, Now = now };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                scan.Observed = SeatGeometry.Extract(geometry, floorY, out var diagnostics);
                scan.Diagnostics = diagnostics;
            }
            catch (Exception error)
            {
                scan.Diagnostics = null;
                Debug.LogException(error);
            }
            watch.Stop();
            scan.ElapsedMs = watch.ElapsedMilliseconds;
            lock (scanGate) completed = scan;
            Interlocked.Exchange(ref scanBusy, 0);
        }

        private void ApplyCompleted()
        {
            BackgroundScan scan;
            lock (scanGate)
            {
                scan = completed;
                completed = null;
            }
            if (scan == null || disposed) return;
            if (scan.Observed == null)
            {
                Invalidate();
                Status = "椅子扫描失败";
                return;
            }
            if (scan.ElapsedMs >= 50)
                Debug.Log($"MATESOUL_PERF_SEAT_SCAN ms={scan.ElapsedMs} tris={scan.Geometry.Count} " +
                          $"groups={scan.Diagnostics.SurfaceGroups}");
            Apply(scan.Geometry, scan.Now, scan.Observed, scan.Diagnostics);
        }

        public void Observe(List<RealityTriangle> geometry, float now)
        {
            triangles = geometry;
            var observed = SeatGeometry.Extract(geometry, floorY, out var diagnostics);
            Apply(geometry, now, observed, diagnostics);
        }

        private void Apply(List<RealityTriangle> geometry, float now,
            List<SeatTarget> observed, SeatScanDiagnostics diagnostics)
        {
            using var perf = PerfMonitor.Sample("Room.SeatApply");
            var matched = new HashSet<SeatTarget>();
            foreach (var candidate in observed)
            {
                var target = targets.Where(t => !matched.Contains(t) &&
                    Vector3.Distance(t.Center, candidate.Center) < .18f)
                    .OrderBy(t => Vector3.Distance(t.Center, candidate.Center)).FirstOrDefault();
                if (target == null)
                {
                    target = candidate;
                    target.Id = "real-seat-" + Guid.NewGuid().ToString("N");
                    targets.Add(target);
                }
                var stable = SeatGeometry.SamePose(target, candidate) &&
                             (target.Available || now - target.ObservedAt < 2f);
#if DEVELOPMENT_BUILD
                if (target.Available && !stable)
                    Debug.LogWarning($"MATESOUL_SEAT geometry_changed id={target.Id} " +
                                     $"centerDelta={Vector3.Distance(target.Center, candidate.Center):F4} " +
                                     $"oldSize={target.Width:F4}x{target.Depth:F4} " +
                                     $"newSize={candidate.Width:F4}x{candidate.Depth:F4} " +
                                     $"sampleGap={now - target.ObservedAt:F3}");
#endif
                target.StableSamples = stable ? target.StableSamples + 1 : 1;
                // No drifting average: a running action locks a copy and checks it each tick.
                target.Center = candidate.Center; target.Forward = candidate.Forward;
                target.Width = candidate.Width; target.Depth = candidate.Depth;
                target.Backless = candidate.Backless;
                target.GeometryInferred = candidate.GeometryInferred;
                target.SourceId = candidate.SourceId;
                target.FloorY = candidate.FloorY;
                target.PlaneError = candidate.PlaneError;
                target.ObservedAt = now;
                target.MissedScans = 0;
                target.Available = target.StableSamples >= 3 && SeatGeometry.HasClearance(target, geometry);
                matched.Add(target);
                if (target.Available)
                    realityItems.PublishInteraction(target.Id, RealitySemantic.Chair,
                        target.Center, target.Rotation,
                        new Vector3(target.Width,
                            Mathf.Max(.5f, target.Center.y - target.FloorY + .45f), target.Depth),
                        target.GeometryInferred, RealityInteractionIds.Sit);
                else realityItems.WithdrawInteraction(target.Id, RealityInteractionIds.Sit);
            }
            if (matched.Any(target => !target.Available))
                nextCachedConfirmation = now + .5f;
            foreach (var target in targets.Where(t => !matched.Contains(t)).ToArray())
            {
                // The chair the player is walking up to is inside the headset's own
                // blind spot, so an empty publish does not mean the seat is gone.
                if (++target.MissedScans <= MissedScanGrace) continue;
                target.Available = false;
                target.StableSamples = 0;
                realityItems.WithdrawInteraction(target.Id, RealityInteractionIds.Sit);
                if (now - target.ObservedAt > 15f) targets.Remove(target);
            }
            var count = Targets.Count();
            Status = count > 0 ? $"已识别 {count} 把可落座椅子" :
                matched.Any(target => target.StableSamples >= 3) ? "已找到椅子，但坐下或起身空间被阻挡" :
                observed.Count > 0 ? "正在确认椅面稳定性" : diagnostics.NoTargetStatus(geometry.Count);
            var signature = Status + "|" + count;
            if (signature != lastScanSignature || now >= nextScanLog)
            {
                lastScanSignature = signature;
                nextScanLog = now + (count > 0 ? 15f : 5f);
                LogScan(geometry, floorY, diagnostics, observed, count);
            }
        }

        private void LogScan(List<RealityTriangle> geometry, float floorY,
            SeatScanDiagnostics diagnostics, List<SeatTarget> observed, int available)
        {
            var chairTriangles = 0;
            var unknownTriangles = 0;
            var otherTriangles = 0;
            var blocks = new HashSet<int>();
            foreach (var triangle in geometry)
            {
                if (triangle.HasSemantic(PxrSemanticLabel.Chair)) chairTriangles++;
                else if (triangle.Label == (int)PxrSemanticLabel.Unknown) unknownTriangles++;
                else otherTriangles++;
                blocks.Add(triangle.SourceId);
            }
            var volumes = room.RealityItems?.Count ?? 0;
            var chairVolumes = room.RealityItems == null ? 0 :
                room.RealityItems.Count(item => item.Semantic == RealitySemantic.Chair);
            Debug.Log(
                $"MATESOUL_SEAT scan tris={geometry.Count} chairTris={chairTriangles} " +
                $"unknownTris={unknownTriangles} otherTris={otherTriangles} blocks={blocks.Count} " +
                $"volumes={volumes} chairVolumes={chairVolumes} floorY={floorY:F3} " +
                $"horizAll={diagnostics.HorizontalAll} inBand={diagnostics.InHeightBand} " +
                $"semHoriz={diagnostics.SemanticHorizontalTriangles} " +
                $"semSurf={diagnostics.SemanticSurfaces} semArea={diagnostics.SemanticAreaRejected} " +
                $"semPlane={diagnostics.SemanticPlaneRejected} " +
                $"semBack={diagnostics.SemanticBackrestRejected} " +
                $"semFit={diagnostics.SemanticFitRejected} " +
                $"geoHoriz={diagnostics.HorizontalTriangles} " +
                $"geoSurf={diagnostics.SurfaceGroups} geoArea={diagnostics.AreaRejected} " +
                $"geoPlane={diagnostics.PlaneRejected} geoBack={diagnostics.BackrestRejected} " +
                $"geoSize={diagnostics.SizeRejected} geoSupport={diagnostics.SupportRejected} " +
                $"candidates={observed.Count} available={available} status={Status}");
            foreach (var reject in diagnostics.Rejects)
                Debug.Log($"MATESOUL_SEAT reject reason={reject.Reason} " +
                          $"center={reject.Center:F2} h={reject.Height:F2} area={reject.Area:F3} " +
                          $"plane={reject.PlaneError:F3} back={reject.BackArea:F3}");
            foreach (var target in observed)
            {
                // Naming the triangle that vetoed the seat separates a desk edge from
                // the chair's own backrest without another device run.
                var open = SeatGeometry.HasClearance(target, geometry, out var blocker);
                Debug.Log($"MATESOUL_SEAT target center={target.Center:F2} " +
                          $"h={target.Center.y - target.FloorY:F2} " +
                          $"size={target.Width:F2}x{target.Depth:F2} " +
                          $"samples={target.StableSamples} available={target.Available} " +
                          $"clearance={open} " +
                          $"inferred={target.GeometryInferred} backless={target.Backless}" +
                          (open ? string.Empty :
                              $" blocker={blocker.Center:F2} src={blocker.SourceId} " +
                              $"normal={blocker.Normal:F2} area={blocker.Area:F3} " +
                              $"forward={target.Forward:F2}"));
            }
        }

        public SeatTarget Resolve(string id) => targets.FirstOrDefault(t => t.Id == id && t.IsFresh);
        public SeatTarget Nearest(Vector3 point) => Targets
            .OrderBy(t => Vector3.Distance(t.Center, point) + (t.Backless ? .5f : 0f))
            .FirstOrDefault();
        public bool IsSafe(SeatTarget locked)
        {
            var current = Resolve(locked.Id);
            if (current == null || !SeatGeometry.SamePose(current, locked)) return false;
            if (current.ObservedAt <= locked.ObservedAt) return true;
            locked.ObservedAt = current.ObservedAt;
            return SeatGeometry.HasClearance(locked, triangles);
        }

        public void Invalidate()
        {
            foreach (var target in targets)
            {
                target.Available = false;
                target.StableSamples = 0;
                realityItems.WithdrawInteraction(target.Id, RealityInteractionIds.Sit);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            pending = null;
            if (meshSource != null) meshSource.Updated -= OnMeshUpdated;
            Invalidate();
        }
    }
}
