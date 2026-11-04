using System;
using System.Collections.Generic;
using System.Linq;
using MateSoul.RoomNavigation;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed class RealBedScanner : IDisposable
    {
        private readonly RealityItemDirectory realityItems;
        private readonly RoomMapSnapshot room;
        private readonly RealityMeshSource meshSource;
        private readonly RealityItemObservation[] calibratedBeds;
        private readonly List<BedTarget> targets = new List<BedTarget>();
        private List<RealityTriangle> triangles = new List<RealityTriangle>();
        private float nextCachedConfirmation;
        private float nextDiagnostics;
        private bool disposed;

        public RealBedScanner(RealityItemDirectory directory, RoomMapSnapshot snapshot,
            RealityMeshSource source)
        {
            realityItems = directory;
            room = snapshot;
            meshSource = source;
            calibratedBeds = room.Source == "PICO Room Capture"
                ? room.RealityItems.Where(BedGeometry.IsUsableCalibration).ToArray()
                : Array.Empty<RealityItemObservation>();
            if (calibratedBeds.Length > 0)
                Status = $"房间标定已定位 {calibratedBeds.Length} 张床，请观察床面以细化高度";
            meshSource.Updated += OnMeshUpdated;
        }

        public IEnumerable<BedTarget> Targets => targets.Where(target => target.IsFresh);
        public IReadOnlyList<RealityTriangle> Geometry => triangles;
        public int CalibratedBedCount => calibratedBeds.Length;
        public string Status { get; private set; } = "请观察床面，枕头可帮助判断躺卧方向";

        public void Tick(float now)
        {
            if (now < nextCachedConfirmation ||
                !targets.Any(target => !target.Available && target.StableSamples > 0)) return;
            nextCachedConfirmation = now + .5f;
            Observe(triangles, now);
        }

        private void OnMeshUpdated(List<RealityTriangle> geometry, float now)
        {
            try
            {
                Observe(geometry, now);
            }
            catch (Exception error)
            {
                Invalidate();
                Status = "床铺扫描失败：" + error.Message;
                Debug.LogException(error);
            }
        }

        public void Observe(List<RealityTriangle> geometry, float now)
        {
            triangles = geometry;
            var observed = BedGeometry.Extract(geometry, room.MainFloor.Centroid.y, calibratedBeds);
            var matched = new HashSet<BedTarget>();
            var blocked = 0;
            foreach (var candidate in observed)
            {
                var target = targets.Where(item => !matched.Contains(item) &&
                        Vector3.Distance(item.Center, candidate.Center) < .25f)
                    .OrderBy(item => Vector3.Distance(item.Center, candidate.Center))
                    .FirstOrDefault();
                if (target == null)
                {
                    target = candidate;
                    target.Id = "real-bed-" + Guid.NewGuid().ToString("N");
                    targets.Add(target);
                }

                BedGeometry.AlignUncertainDirection(candidate, target);
                var stable = BedGeometry.SamePose(target, candidate) &&
                             (target.Available || now - target.ObservedAt < 2f);
                target.StableSamples = stable ? target.StableSamples + 1 : 1;
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
                target.CalibrationId = candidate.CalibrationId;
                target.SourceIds = candidate.SourceIds;
                target.ObservedAt = now;
                var clear = BedGeometry.HasClearance(target, geometry);
                if (!clear) blocked++;
                target.Available = target.StableSamples >= 2 && clear;
                matched.Add(target);

                if (target.Available)
                    realityItems.PublishInteraction(target.Id, RealitySemantic.Bed,
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
                target.Available = false;
                target.StableSamples = 0;
                realityItems.WithdrawInteraction(target.Id, RealityInteractionIds.Lie);
                if (now - target.ObservedAt > 15f) targets.Remove(target);
            }

            var count = Targets.Count();
            Status = count > 0 ? $"已识别 {count} 张可躺卧床铺" :
                blocked > 0 ? "已测到床面，但躺卧区域有较高障碍，请清理或换角度观察" :
                observed.Count > 0 ? "正在确认床面稳定性" :
                calibratedBeds.Length > 0 ?
                    $"房间标定已定位 {calibratedBeds.Length} 张床，当前床面网格不足，请换角度观察" :
                "尚未找到足够完整的床面，请换角度观察";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (now >= nextDiagnostics)
            {
                nextDiagnostics = now + 2f;
                Debug.Log($"MATESOUL_BED_SCAN calibrated={calibratedBeds.Length} " +
                          $"measured={observed.Count} assisted={observed.Count(item => item.CalibrationAssisted)} " +
                          $"blocked={blocked} available={count} triangles={geometry.Count}");
            }
#endif
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
            return BedGeometry.HasClearance(locked, triangles);
        }

        public bool IsMountSafe(BedTarget locked, BedMountPlan plan)
        {
            var current = Resolve(locked.Id);
            return current != null &&
                   BedGeometry.SamePose(current, locked) &&
                   BedGeometry.HasMountClearance(locked, triangles, plan);
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
            meshSource.Updated -= OnMeshUpdated;
            Invalidate();
        }
    }
}
