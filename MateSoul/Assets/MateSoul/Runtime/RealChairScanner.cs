using System;
using System.Collections.Generic;
using System.Linq;
using MateSoul.RoomNavigation;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed class RealChairScanner : IDisposable
    {
        private readonly RealityItemDirectory realityItems;
        private readonly RoomMapSnapshot room;
        private readonly RealityMeshSource meshSource;
        private readonly List<SeatTarget> targets = new List<SeatTarget>();
        private List<RealityTriangle> triangles = new List<RealityTriangle>();
        private float nextCachedConfirmation;
        private bool disposed;
        public IEnumerable<SeatTarget> Targets => targets.Where(t => t.IsFresh);
        public IReadOnlyList<RealityTriangle> Geometry => triangles;
        public string Status { get; private set; } = "请观察椅面、靠背和椅子前方";
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        public bool UseDevelopmentGeometry { get; set; }
#endif

        public RealChairScanner(RealityItemDirectory directory, RoomMapSnapshot snapshot,
            RealityMeshSource source = null)
        {
            realityItems = directory;
            room = snapshot;
            meshSource = source;
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
            if (now < nextCachedConfirmation ||
                !targets.Any(target => !target.Available && target.StableSamples > 0)) return;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (UseDevelopmentGeometry) return;
#endif
            nextCachedConfirmation = now + .5f;
            Observe(triangles, now);
        }

        private void OnMeshUpdated(List<RealityTriangle> geometry, float now)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (UseDevelopmentGeometry) return;
#endif
            try
            {
                Observe(geometry, now);
            }
            catch (Exception error)
            {
                Invalidate();
                Status = "椅子扫描失败：" + error.Message;
                Debug.LogException(error);
            }
        }

        public void Observe(List<RealityTriangle> geometry, float now)
        {
            triangles = geometry;
            var observed = SeatGeometry.Extract(geometry, room.MainFloor.Centroid.y);
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
                target.Available = false;
                target.StableSamples = 0;
                realityItems.WithdrawInteraction(target.Id, RealityInteractionIds.Sit);
                if (now - target.ObservedAt > 15f) targets.Remove(target);
            }
            var count = Targets.Count();
            Status = count > 0 ? $"已识别 {count} 把可落座椅子" :
                observed.Count > 0 ? "正在确认椅面稳定性与前方空间" : "尚未找到完整椅面和靠背，请换角度观察";
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
            if (meshSource != null) meshSource.Updated -= OnMeshUpdated;
            Invalidate();
        }
    }
}
