using System;
using System.Collections.Generic;
using System.Linq;
using ByteDance.PICO.XR;
using UnityEngine;

namespace MateSoul.Runtime
{
    [Serializable]
    public sealed class BedTarget
    {
        public string Id;
        public Vector3 Center, Forward;
        public float Width, Length, FootEdge, LeftEdge, RightEdge, FloorY, PlaneError;
        public int[] SourceIds;
        public float ObservedAt;
        public int StableSamples;
        public bool Available;

        public Quaternion Rotation => Quaternion.LookRotation(Forward, Vector3.up);
        public Quaternion LyingRotation => Quaternion.LookRotation(Vector3.up, -Forward);
        public Vector3 Right => Vector3.Cross(Vector3.up, Forward);
        public Vector3 Dock => BedGeometry.MountCandidates(this)
            .First(candidate => candidate.Edge == BedMountEdge.Foot).IdealDock;
        public Vector3 ContactRoot => Center + Forward * (Length * .42f) + Vector3.up * .035f;
        public bool IsFresh => Available;
        public BedTarget Copy()
        {
            var copy = (BedTarget)MemberwiseClone();
            copy.SourceIds = SourceIds?.ToArray();
            return copy;
        }
    }

    public enum BedMountEdge
    {
        Left,
        Right,
        Foot
    }

    public sealed class BedMountCandidate
    {
        public BedMountEdge Edge;
        public Vector3 IdealDock;
        public Vector3 Inward;
        public Quaternion Rotation => Quaternion.LookRotation(Inward, Vector3.up);
    }

    public sealed class BedMountPlan
    {
        public BedMountEdge Edge;
        public Vector3 Dock;
        public Quaternion Rotation;
        public float PathLength;
    }

    public static class BedGeometry
    {
        private const float MountClearance = .36f;

        public static IEnumerable<BedMountCandidate> MountCandidates(BedTarget bed)
        {
            var floorCenter = new Vector3(bed.Center.x, bed.FloorY, bed.Center.z);
            var right = bed.Right;
            yield return new BedMountCandidate
            {
                Edge = BedMountEdge.Left,
                IdealDock = floorCenter - right * (bed.LeftEdge + MountClearance),
                Inward = right
            };
            yield return new BedMountCandidate
            {
                Edge = BedMountEdge.Right,
                IdealDock = floorCenter + right * (bed.RightEdge + MountClearance),
                Inward = -right
            };
            yield return new BedMountCandidate
            {
                Edge = BedMountEdge.Foot,
                IdealDock = floorCenter + bed.Forward * (bed.FootEdge + MountClearance),
                Inward = -bed.Forward
            };
        }

        public static List<BedTarget> Extract(IReadOnlyList<RealityTriangle> triangles, float floorY)
        {
            var beds = triangles.Where(triangle =>
                    triangle.HasSemantic(PxrSemanticLabel.Bed))
                .GroupBy(triangle => triangle.SourceId);
            var candidates = new List<BedTarget>();
            foreach (var source in beds)
            {
                var horizontal = source.Where(triangle =>
                        Mathf.Abs(triangle.Normal.y) > .9f &&
                        triangle.Center.y - floorY >= .35f &&
                        triangle.Center.y - floorY <= .9f)
                    .ToArray();
                foreach (var group in Connected(horizontal))
                    if (TryCreate(group, triangles, floorY, out var target))
                        candidates.Add(target);
            }

            var result = new List<BedTarget>();
            foreach (var candidate in candidates.OrderByDescending(item => item.Width * item.Length))
            {
                if (result.Any(existing =>
                        Vector3.ProjectOnPlane(existing.Center - candidate.Center, Vector3.up).magnitude <
                        Mathf.Min(existing.Length, candidate.Length) * .45f &&
                        Mathf.Abs(existing.Center.y - candidate.Center.y) < .12f))
                    continue;
                result.Add(candidate);
            }
            return result;
        }

        private static bool TryCreate(IReadOnlyList<RealityTriangle> group,
            IReadOnlyList<RealityTriangle> all, float floorY, out BedTarget target)
        {
            target = null;
            var area = group.Sum(triangle => triangle.Area);
            if (area < .8f || area > 6f) return false;
            var surfaceY = group.Sum(triangle => triangle.Center.y * triangle.Area) / area;
            var error = Mathf.Sqrt(group.Sum(triangle =>
                Mathf.Pow(triangle.Center.y - surfaceY, 2) * triangle.Area) / area);
            if (error > .025f) return false;

            var points = group.SelectMany(triangle =>
                new[] { triangle.A, triangle.B, triangle.C }).ToArray();
            FitOrientedBounds(points, surfaceY, out var center,
                out var right, out var axis, out var width, out var length);
            if (width < .75f || width > 2.3f || length < 1.65f || length > 2.65f ||
                length - width < .12f || area / (width * length) < .62f)
                return false;

            var supported = 0;
            const int acrossSamples = 7;
            const int lengthSamples = 9;
            for (var x = 0; x < acrossSamples; x++)
                for (var z = 0; z < lengthSamples; z++)
                {
                    var point = center +
                                right * Mathf.Lerp(-width * .42f, width * .42f,
                                    x / (acrossSamples - 1f)) +
                                axis * Mathf.Lerp(-length * .42f, length * .42f,
                                    z / (lengthSamples - 1f));
                    if (group.Any(triangle => ContainsXZ(triangle, point))) supported++;
                }
            if (supported < acrossSamples * lengthSamples * .9f) return false;

            var sourceIds = all.Where(triangle =>
                {
                    if (!triangle.HasSemantic(PxrSemanticLabel.Bed)) return false;
                    var delta = triangle.Center - center;
                    return Mathf.Abs(Vector3.Dot(delta, right)) <= width * .65f &&
                           Mathf.Abs(Vector3.Dot(delta, axis)) <= length * .65f &&
                           triangle.Center.y >= surfaceY - .4f &&
                           triangle.Center.y <= surfaceY + .45f;
                })
                .Select(triangle => triangle.SourceId).Distinct().OrderBy(id => id).ToArray();

            var negative = UpperStructureArea(all, sourceIds, center, right, axis,
                width, length, surfaceY, -1);
            var positive = UpperStructureArea(all, sourceIds, center, right, axis,
                width, length, surfaceY, 1);
            var strongest = Mathf.Max(negative, positive);
            var weakest = Mathf.Min(negative, positive);
            if (strongest < .12f || strongest < weakest * 2f) return false;
            var towardHead = positive > negative ? axis : -axis;

            var forward = -towardHead;
            var mergedPoints = all.Where(triangle => sourceIds.Contains(triangle.SourceId))
                .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
                .ToArray();
            var footEdge = mergedPoints.Max(point => Vector3.Dot(point - center, forward));
            // Headboards and integrated bedside furniture can be wider than the
            // mattress. Side entry uses the central body lane instead of those
            // end-only protrusions.
            var sidePoints = mergedPoints.Where(point =>
                Mathf.Abs(Vector3.Dot(point - center, forward)) <= length * .42f).ToArray();
            if (sidePoints.Length == 0) return false;
            var leftEdge = -sidePoints.Min(point => Vector3.Dot(point - center, right));
            var rightEdge = sidePoints.Max(point => Vector3.Dot(point - center, right));
            if (footEdge < length * .4f || footEdge > length * 1.1f) return false;
            if (leftEdge < width * .4f || leftEdge > width * .85f ||
                rightEdge < width * .4f || rightEdge > width * .85f) return false;

            target = new BedTarget
            {
                Center = center,
                Forward = forward,
                Width = width,
                Length = length,
                FootEdge = footEdge,
                LeftEdge = leftEdge,
                RightEdge = rightEdge,
                FloorY = floorY,
                PlaneError = error,
                SourceIds = sourceIds
            };
            return true;
        }

        private static float UpperStructureArea(IReadOnlyList<RealityTriangle> all,
            IReadOnlyCollection<int> sourceIds, Vector3 center, Vector3 right, Vector3 axis,
            float width, float length, float surfaceY, int side)
        {
            var area = 0f;
            foreach (var triangle in all)
            {
                if (!sourceIds.Contains(triangle.SourceId) ||
                    triangle.Center.y <= surfaceY + .05f) continue;
                var delta = triangle.Center - center;
                var x = Vector3.Dot(delta, right);
                var z = Vector3.Dot(delta, axis) * side;
                if (Mathf.Abs(x) <= width * .65f &&
                    z >= length * .2f && z <= length * .75f)
                    area += triangle.Area;
            }
            return area;
        }

        private static void FitOrientedBounds(Vector3[] points, float y, out Vector3 center,
            out Vector3 right, out Vector3 forward, out float width, out float length)
        {
            var average = points.Aggregate(Vector3.zero, (sum, point) => sum + point) /
                          points.Length;
            var bestArea = float.MaxValue;
            right = Vector3.right;
            forward = Vector3.forward;
            width = length = 0;
            var xOffset = 0f;
            var zOffset = 0f;
            for (var angle = 0f; angle < 180f; angle += .5f)
            {
                var candidateForward = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                var candidateRight = Vector3.Cross(Vector3.up, candidateForward);
                var xs = points.Select(point => Vector3.Dot(point - average, candidateRight)).ToArray();
                var zs = points.Select(point => Vector3.Dot(point - average, candidateForward)).ToArray();
                var candidateWidth = xs.Max() - xs.Min();
                var candidateLength = zs.Max() - zs.Min();
                var boundsArea = candidateWidth * candidateLength;
                if (boundsArea >= bestArea) continue;
                bestArea = boundsArea;
                right = candidateRight;
                forward = candidateForward;
                width = candidateWidth;
                length = candidateLength;
                xOffset = (xs.Max() + xs.Min()) * .5f;
                zOffset = (zs.Max() + zs.Min()) * .5f;
            }
            if (width > length)
            {
                var dimension = width;
                width = length;
                length = dimension;
                var direction = right;
                right = forward;
                forward = direction;
                var offset = xOffset;
                xOffset = zOffset;
                zOffset = offset;
            }
            center = new Vector3(average.x, y, average.z) +
                     right * xOffset + forward * zOffset;
        }

        private static IEnumerable<List<RealityTriangle>> Connected(
            IReadOnlyList<RealityTriangle> triangles)
        {
            var parents = Enumerable.Range(0, triangles.Count).ToArray();
            var vertices = new Dictionary<Vector3Int, int>();
            for (var index = 0; index < triangles.Count; index++)
                foreach (var vertex in new[]
                         {
                             triangles[index].A, triangles[index].B, triangles[index].C
                         })
                {
                    var key = Vector3Int.RoundToInt(vertex / .008f);
                    if (vertices.TryGetValue(key, out var other))
                        parents[Root(parents, index)] = Root(parents, other);
                    else vertices.Add(key, index);
                }
            var groups = new Dictionary<int, List<RealityTriangle>>();
            for (var index = 0; index < triangles.Count; index++)
            {
                var root = Root(parents, index);
                if (!groups.TryGetValue(root, out var group))
                    groups[root] = group = new List<RealityTriangle>();
                group.Add(triangles[index]);
            }
            return groups.Values;
        }

        private static int Root(int[] parents, int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }
            return index;
        }

        private static bool ContainsXZ(RealityTriangle triangle, Vector3 point)
        {
            float Cross(Vector3 a, Vector3 b) => a.x * b.z - a.z * b.x;
            var a = Cross(triangle.B - triangle.A, point - triangle.A);
            var b = Cross(triangle.C - triangle.B, point - triangle.B);
            var c = Cross(triangle.A - triangle.C, point - triangle.C);
            return a >= -.0001f && b >= -.0001f && c >= -.0001f ||
                   a <= .0001f && b <= .0001f && c <= .0001f;
        }

        public static bool SamePose(BedTarget first, BedTarget second)
            => first.SourceIds.SequenceEqual(second.SourceIds) &&
               Vector3.Distance(first.Center, second.Center) <= .035f &&
               Vector3.Angle(first.Forward, second.Forward) <= 4f &&
               Mathf.Abs(first.Width - second.Width) <= .05f &&
               Mathf.Abs(first.Length - second.Length) <= .06f &&
               Mathf.Abs(first.FootEdge - second.FootEdge) <= .06f &&
               Mathf.Abs(first.LeftEdge - second.LeftEdge) <= .06f &&
               Mathf.Abs(first.RightEdge - second.RightEdge) <= .06f;

        public static bool HasClearance(BedTarget bed,
            IReadOnlyList<RealityTriangle> triangles)
        {
            var ignored = new HashSet<int>(bed.SourceIds);
            var inverse = Quaternion.Inverse(bed.Rotation);
            foreach (var triangle in triangles)
            {
                if (ignored.Contains(triangle.SourceId)) continue;
                var a = inverse * (triangle.A - bed.Center);
                var b = inverse * (triangle.B - bed.Center);
                var c = inverse * (triangle.C - bed.Center);
                var bodyCenter = new Vector3(0, .30f, 0);
                var bodyExtent = new Vector3(Mathf.Min(.30f, bed.Width * .3f),
                    .30f, bed.Length * .42f);
                if (Intersects(a, b, c, bodyCenter, bodyExtent)) return false;
            }
            return true;
        }

        public static bool HasMountClearance(BedTarget bed,
            IReadOnlyList<RealityTriangle> triangles, BedMountPlan plan)
        {
            var ignored = new HashSet<int>(bed.SourceIds);
            var inverse = Quaternion.Inverse(plan.Rotation);
            var center = plan.Dock + Vector3.up * .84f;
            var extent = new Vector3(.34f, .81f, .31f);
            foreach (var triangle in triangles)
            {
                if (ignored.Contains(triangle.SourceId)) continue;
                var a = inverse * (triangle.A - center);
                var b = inverse * (triangle.B - center);
                var c = inverse * (triangle.C - center);
                if (Intersects(a, b, c, Vector3.zero, extent)) return false;
            }
            return true;
        }

        private static bool Intersects(Vector3 a, Vector3 b, Vector3 c,
            Vector3 center, Vector3 extent)
        {
            if (Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > center.x + extent.x ||
                Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < center.x - extent.x ||
                Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > center.y + extent.y ||
                Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < center.y - extent.y ||
                Mathf.Min(a.z, Mathf.Min(b.z, c.z)) > center.z + extent.z ||
                Mathf.Max(a.z, Mathf.Max(b.z, c.z)) < center.z - extent.z)
                return false;
            a -= center;
            b -= center;
            c -= center;
            bool Separates(Vector3 axis)
            {
                var x = Vector3.Dot(a, axis);
                var y = Vector3.Dot(b, axis);
                var z = Vector3.Dot(c, axis);
                var radius = extent.x * Mathf.Abs(axis.x) +
                             extent.y * Mathf.Abs(axis.y) +
                             extent.z * Mathf.Abs(axis.z);
                return Mathf.Min(x, Mathf.Min(y, z)) > radius ||
                       Mathf.Max(x, Mathf.Max(y, z)) < -radius;
            }
            var edges = new[] { b - a, c - b, a - c };
            if (Separates(Vector3.right) || Separates(Vector3.up) ||
                Separates(Vector3.forward) ||
                Separates(Vector3.Cross(edges[0], edges[1]))) return false;
            foreach (var edge in edges)
                if (Separates(Vector3.Cross(edge, Vector3.right)) ||
                    Separates(Vector3.Cross(edge, Vector3.up)) ||
                    Separates(Vector3.Cross(edge, Vector3.forward))) return false;
            return true;
        }
    }

    public static class BedMountPlanner
    {
        public static bool TryCreate(BedTarget bed, Vector3 origin,
            INavigationService navigation, IReadOnlyList<RealityTriangle> geometry,
            out BedMountPlan plan)
        {
            plan = null;
            foreach (var candidate in BedGeometry.MountCandidates(bed))
            {
                if (!navigation.TrySample(candidate.IdealDock, .22f, out var dock) ||
                    Vector3.ProjectOnPlane(dock - candidate.IdealDock, Vector3.up).magnitude > .14f ||
                    Mathf.Abs(dock.y - bed.FloorY) > .12f ||
                    !navigation.TryPath(origin, dock, out var corners))
                    continue;

                var pathLength = 0f;
                var previous = origin;
                foreach (var corner in corners)
                {
                    pathLength += Vector3.ProjectOnPlane(corner - previous, Vector3.up).magnitude;
                    previous = corner;
                }
                var candidatePlan = new BedMountPlan
                {
                    Edge = candidate.Edge,
                    Dock = dock,
                    Rotation = candidate.Rotation,
                    PathLength = pathLength
                };
                if (!BedGeometry.HasMountClearance(bed, geometry, candidatePlan)) continue;
                if (plan == null || candidatePlan.PathLength < plan.PathLength)
                    plan = candidatePlan;
            }
            return plan != null;
        }
    }
}
