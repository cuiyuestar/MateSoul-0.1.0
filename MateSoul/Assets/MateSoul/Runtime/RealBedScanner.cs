using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MateSoul.RoomNavigation;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed class RealBedScanner : IDisposable
    {
        // Extraction walks the room mesh; it is pointless to repeat it faster than
        // the mesh source republishes geometry.
        private const float MinimumScanInterval = .5f;
        // Scans that may disagree with the cached seat before it is dropped.
        private const int AvailabilityGraceScans = 3;
        private readonly RealityItemDirectory realityItems;
        private readonly RoomMapSnapshot room;
        private readonly RealityMeshSource meshSource;
        private readonly RealityItemObservation[] calibratedBeds;
        private readonly float floorY;
        private readonly List<BedTarget> targets = new List<BedTarget>();
        private readonly object scanGate = new object();
        private List<RealityTriangle> triangles = new List<RealityTriangle>();
        private float nextCachedConfirmation;
        private float nextDiagnostics;
        private List<RealityTriangle> pending;
        private float nextScan;
        private int scanBusy;
        private BackgroundScan completed;
        private bool disposed;

        private sealed class BackgroundScan
        {
            public List<RealityTriangle> Geometry;
            public float Now;
            public List<BedTarget> Observed;
            public BedScanDiagnostics Diagnostics;
            public long ElapsedMs;
        }

        public RealBedScanner(RealityItemDirectory directory, RoomMapSnapshot snapshot,
            RealityMeshSource source)
        {
            realityItems = directory;
            room = snapshot;
            meshSource = source;
            floorY = snapshot.MainFloor.Centroid.y;
            calibratedBeds = room.Source == "PICO Room Capture"
                ? room.RealityItems.Where(BedGeometry.IsUsableCalibration).ToArray()
                : Array.Empty<RealityItemObservation>();
            if (calibratedBeds.Length > 0)
                Status = $"房间标定已定位 {calibratedBeds.Length} 处可躺卧家具，请观察床面或沙发面以细化高度";
            Debug.Log($"MATESOUL_BED_CALIB source={room.Source} " +
                      $"items={room.RealityItems.Count} usable={calibratedBeds.Length} detail=[" +
                      string.Join(" | ", room.RealityItems
                          .Where(item => item.Semantic == RealitySemantic.Bed ||
                                         item.Semantic == RealitySemantic.Sofa)
                          .Select(item => $"{item.Semantic} center={item.Center:F2} " +
                                          $"size={item.Size:F2} usable=" +
                                          $"{(BedGeometry.IsUsableCalibration(item) ? 1 : 0)}")) + "]");
            meshSource.Updated += OnMeshUpdated;
        }

        public IEnumerable<BedTarget> Targets => targets.Where(target => target.IsFresh);
        public IReadOnlyList<RealityTriangle> Geometry => triangles;
        public int CalibratedBedCount => calibratedBeds.Length;
        public string Status { get; private set; } = "请观察床面或沙发面，枕头可帮助判断躺卧方向";

        public void Tick(float now)
        {
            ApplyCompleted();
            Pump(now);
            if (now < nextCachedConfirmation ||
                !targets.Any(target => !target.Available && target.StableSamples > 0)) return;
            nextCachedConfirmation = now + .5f;
            RequestScan(triangles, now);
        }

        private void OnMeshUpdated(List<RealityTriangle> geometry, float now)
        {
            try
            {
                RequestScan(geometry, now);
            }
            catch (Exception error)
            {
                Invalidate();
                Status = "可躺卧面扫描失败：" + error.Message;
                Debug.LogException(error);
            }
        }

        // Bed extraction walks every surface group against the whole room mesh.
        // On device it runs on a worker thread so the mesh-publish callback never
        // blocks the main thread; the editor stays synchronous for determinism.
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

        private void Pump(float now)
        {
            if (disposed || pending == null || now < nextScan) return;
            RequestScan(pending, now);
        }

        private void ExtractInBackground(List<RealityTriangle> geometry, float now)
        {
            var scan = new BackgroundScan { Geometry = geometry, Now = now };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                scan.Diagnostics = new BedScanDiagnostics();
                scan.Observed = BedGeometry.Extract(geometry, floorY, calibratedBeds, scan.Diagnostics);
            }
            catch (Exception error)
            {
                scan.Observed = null;
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
                Status = "可躺卧面扫描失败";
                return;
            }
            if (scan.ElapsedMs >= 50)
                Debug.Log($"MATESOUL_PERF_BED_SCAN ms={scan.ElapsedMs} tris={scan.Geometry.Count} " +
                          $"groups={scan.Diagnostics.Groups}");
            Apply(scan.Geometry, scan.Now, scan.Observed, scan.Diagnostics);
        }

        public void Observe(List<RealityTriangle> geometry, float now)
        {
            triangles = geometry;
            var diagnostics = new BedScanDiagnostics();
            var observed = BedGeometry.Extract(geometry, floorY, calibratedBeds,
                diagnostics);
            Apply(geometry, now, observed, diagnostics);
        }

        private void Apply(List<RealityTriangle> geometry, float now,
            List<BedTarget> observed, BedScanDiagnostics diagnostics)
        {
            using var perf = PerfMonitor.Sample("Room.BedApply");
            var matched = new HashSet<BedTarget>();
            var logNow = now >= nextDiagnostics;
            var report = logNow ? new List<string>() : null;
            var blocked = 0;
            foreach (var candidate in observed)
            {
                var target = targets.Where(item => !matched.Contains(item) &&
                        item.Semantic == candidate.Semantic &&
                        Vector3.Distance(item.Center, candidate.Center) < .25f)
                    .OrderBy(item => Vector3.Distance(item.Center, candidate.Center))
                    .FirstOrDefault();
                if (target == null)
                {
                    target = candidate;
                    target.Id = (candidate.Semantic == RealitySemantic.Sofa
                        ? "real-sofa-" : "real-bed-") + Guid.NewGuid().ToString("N");
                    targets.Add(target);
                }

                BedGeometry.AlignUncertainDirection(candidate, target);
                var samePose = BedGeometry.SamePose(target, candidate);
                target.MissedScans = samePose ? 0 : target.MissedScans + 1;
                // The room mesh is refined continuously, so a scan that lands a
                // few centimetres away is normal. Replacing the seat on every
                // outlier is what made the prompt, and an in-progress lie, drop
                // out. A cached seat is only replaced once the disagreement lasts.
                var adopt = samePose || !target.Available ||
                            target.MissedScans >= AvailabilityGraceScans;
                if (adopt)
                {
                    target.Center = candidate.Center;
                    target.Forward = candidate.Forward;
                    target.Width = candidate.Width;
                    target.Length = candidate.Length;
                    target.HeadEdge = candidate.HeadEdge;
                    target.FootEdge = candidate.FootEdge;
                    target.LeftEdge = candidate.LeftEdge;
                    target.RightEdge = candidate.RightEdge;
                    target.FloorY = candidate.FloorY;
                    target.PlaneError = candidate.PlaneError;
                    target.ContactLift = candidate.ContactLift;
                    target.HeadCue = candidate.HeadCue;
                    target.Semantic = candidate.Semantic;
                    target.CalibrationId = candidate.CalibrationId;
                    target.CalibrationFallback = candidate.CalibrationFallback;
                    target.SourceIds = candidate.SourceIds;
                    target.ObservedAt = now;
                    target.StableSamples = samePose
                        ? Mathf.Min(target.StableSamples + 1, 64) : 1;
                }
                // A target anchored to the room calibration already has a known
                // seat; refusing it because the scanned mesh grazes the avatar's
                // standing envelope is what made the prompt flicker away.
                var clear = target.CalibrationAssisted ||
                            BedGeometry.HasClearance(target, geometry);
                target.BlockedScans = clear ? 0 : target.BlockedScans + 1;
                if (!clear) blocked++;
                // 净空判定对网格细微变化很敏感，已经可用的入口允许短暂抖动，
                // 只有连续多次被判为阻塞才真正撤掉。
                target.Available = target.StableSamples >= 2 &&
                                   (clear || (target.Available &&
                                              target.BlockedScans < AvailabilityGraceScans));
                matched.Add(target);
                if (report != null)
                    report.Add($"{target.Semantic} c={target.Center:F2} " +
                               $"w={target.Width:F2} l={target.Length:F2} " +
                               $"h={target.Center.y - target.FloorY:F2} clear={(clear ? 1 : 0)} " +
                               $"stable={target.StableSamples} avail={(target.Available ? 1 : 0)} " +
                               $"lift={target.ContactLift:F2}" +
                               (target.CalibrationFallback ? " fallback" :
                                   target.CalibrationAssisted ? " assisted" : ""));

                if (target.Available)
                    realityItems.PublishInteraction(target.Id, target.Semantic,
                        target.Center, target.Rotation,
                        new Vector3(target.Width,
                            Mathf.Max(.3f, target.Center.y - target.FloorY), target.Length),
                        false, RealityInteractionIds.Lie);
                else realityItems.WithdrawInteraction(target.Id, RealityInteractionIds.Lie);
            }

            if (matched.Any(target => !target.Available))
                nextCachedConfirmation = now + .5f;
            foreach (var target in targets.Where(item => !matched.Contains(item)).ToArray())
            {
                target.MissedScans++;
                // One scan can miss a seat the mesh is still streaming in; keep the
                // seat (and its interaction) until the miss is sustained.
                if (target.Available &&
                    target.MissedScans < AvailabilityGraceScans &&
                    now - target.ObservedAt < 15f) continue;
                target.Available = false;
                target.StableSamples = 0;
                realityItems.WithdrawInteraction(target.Id, RealityInteractionIds.Lie);
                if (now - target.ObservedAt > 15f) targets.Remove(target);
            }

            var count = Targets.Count();
            var fallbackCount = Targets.Count(target => target.CalibrationFallback);
            Status = count > 0
                ? fallbackCount > 0
                    ? $"已按房间标定定位 {count} 处可躺卧位置，正按实测表面贴合"
                    : $"已识别 {count} 处可躺卧位置"
                : blocked > 0 ? "已测到床面或沙发面，但躺卧区域有较高障碍，请清理或换角度观察" :
                observed.Count > 0 ? "正在确认躺卧面稳定性" :
                calibratedBeds.Length > 0 ?
                    $"房间标定已定位 {calibratedBeds.Length} 处可躺卧家具，当前床面或沙发面网格不足，请换角度观察" :
                "尚未找到足够完整的床面或沙发面，请换角度观察";
            if (logNow)
            {
                nextDiagnostics = now + 2f;
                Debug.Log($"MATESOUL_BED_SCAN calibrated={calibratedBeds.Length} " +
                          $"semSurf={diagnostics.Semantic} inBand={diagnostics.InBand} " +
                          $"groups={diagnostics.Groups} measured={observed.Count} " +
                          $"assisted={observed.Count(item => item.CalibrationAssisted)} " +
                          $"fallback={observed.Count(item => item.CalibrationFallback)} " +
                          $"blocked={blocked} available={count} triangles={geometry.Count} " +
                          $"cand=[{string.Join(" | ", diagnostics.Candidates)}] " +
                          $"reject=[{diagnostics.Summary()}] " +
                          $"edges=[{diagnostics.Edges}] " +
                          $"targets=[{string.Join(" | ", report)}] status={Status}");
            }
        }

        public BedTarget Resolve(string id)
            => targets.FirstOrDefault(target => target.Id == id && target.IsFresh);

        public BedTarget Nearest(Vector3 point) => Targets
            .OrderBy(target => Vector3.Distance(target.Center, point)).FirstOrDefault();

        public bool IsSafe(BedTarget locked)
        {
            var current = Resolve(locked.Id);
            if (current == null || !BedGeometry.SamePose(current, locked)) return false;
            if (current.ObservedAt <= locked.ObservedAt) return true;
            locked.ObservedAt = current.ObservedAt;
            return locked.CalibrationAssisted || BedGeometry.HasClearance(locked, triangles);
        }

        public bool IsMountSafe(BedTarget locked, BedMountPlan plan)
        {
            var current = Resolve(locked.Id);
            return current != null &&
                   BedGeometry.SamePose(current, locked) &&
                   (locked.CalibrationAssisted ||
                    BedGeometry.HasMountClearance(locked, triangles, plan));
        }

        public void Invalidate()
        {
            foreach (var target in targets)
            {
                target.Available = false;
                target.StableSamples = 0;
                realityItems.WithdrawInteraction(target.Id, RealityInteractionIds.Lie);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            pending = null;
            meshSource.Updated -= OnMeshUpdated;
            Invalidate();
        }
    }
}
