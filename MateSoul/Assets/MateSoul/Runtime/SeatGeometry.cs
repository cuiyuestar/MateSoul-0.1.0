using System;
using System.Collections.Generic;
using System.Linq;
using ByteDance.PICO.XR;
using UnityEngine;

namespace MateSoul.Runtime
{
    [Serializable]
    public sealed class SeatTarget
    {
        public string Id;
        public Vector3 Center, Forward;
        public float Width, Depth, FloorY, PlaneError;
        public bool Backless;
        public bool GeometryInferred;
        public int SourceId;
        public float ObservedAt;
        public int StableSamples;
        public bool Available;
        public Quaternion Rotation => Quaternion.LookRotation(Forward, Vector3.up);
        public Vector3 Right => Vector3.Cross(Vector3.up, Forward);
        public Vector3 Dock => new Vector3(Center.x, FloorY, Center.z) + Forward * (Depth * .5f + .48f);
        public bool IsFresh => Available;
        public SeatTarget Copy() => (SeatTarget)MemberwiseClone();
    }

    public sealed class SeatScanDiagnostics
    {
        public int HorizontalTriangles, SurfaceGroups;
        public int AreaRejected, PlaneRejected, BackrestRejected, SizeRejected, SupportRejected;

        public string NoTargetStatus(int triangleCount)
        {
            if (triangleCount == 0) return "尚未收到房间扫描数据，请先扫描椅子附近";
            if (HorizontalTriangles == 0) return "未找到合适高度的椅面，请观察椅面与地面";
            if (SupportRejected > 0) return "椅面扫描覆盖不足，请继续观察椅面";
            if (SizeRejected > 0) return "候选椅面尺寸不足或过大，无法落座";
            if (BackrestRejected > 0) return "已找到候选椅面，请继续观察靠背";
            if (PlaneRejected > 0) return "候选椅面起伏过大，尚不能确认落座位置";
            return "椅面扫描面积不足或尚未连成可用表面";
        }
    }

    // Pure geometry: semantic labels select candidates, never determine a contact pose.
    public static class SeatGeometry
    {
        public static List<SeatTarget> Extract(IReadOnlyList<RealityTriangle> triangles, float floorY)
            => Extract(triangles, floorY, out _);

        public static List<SeatTarget> Extract(IReadOnlyList<RealityTriangle> triangles, float floorY,
            out SeatScanDiagnostics diagnostics)
        {
            diagnostics = new SeatScanDiagnostics();
            var horizontal = triangles.Where(t => t.HasSemantic(PxrSemanticLabel.Chair) &&
                Mathf.Abs(t.Normal.y) > .985f &&
                t.Center.y - floorY >= .32f && t.Center.y - floorY <= .60f).ToArray();
            // Connected surfaces, including shared vertices across mesh block boundaries.
            // A category or a mesh UUID is not a chair instance.
            var parents = Enumerable.Range(0, horizontal.Length).ToArray();
            var vertices = new Dictionary<Vector3Int, int>();
            for (var i = 0; i < horizontal.Length; i++)
                foreach (var v in new[] { horizontal[i].A, horizontal[i].B, horizontal[i].C })
                {
                    var key = Vector3Int.RoundToInt(v / .008f);
                    if (vertices.TryGetValue(key, out var other))
                        parents[Root(parents, i)] = Root(parents, other);
                    else vertices.Add(key, i);
                }
            var groups = new Dictionary<int, List<RealityTriangle>>();
            for (var i = 0; i < horizontal.Length; i++)
            {
                var key = Root(parents, i);
                if (!groups.TryGetValue(key, out var group)) groups[key] = group = new List<RealityTriangle>();
                group.Add(horizontal[i]);
            }
            var seats = new List<SeatTarget>();
            foreach (var group in groups.Values)
            {
                var area = group.Sum(t => t.Area);
                if (area < .055f || area > .65f) continue;
                var center = group.Aggregate(Vector3.zero, (p, t) => p + t.Center * t.Area) / area;
                var points = group.SelectMany(t => new[] { t.A, t.B, t.C }).ToArray();
                var error = points.Max(v => Mathf.Abs(v.y - center.y));
                if (error > .018f) continue;

                // A vertical surface above one edge provides backrest direction.
                var back = Vector3.zero;
                var backArea = 0f;
                foreach (var t in triangles)
                {
                    var delta = t.Center - center;
                    if (!t.HasSemantic(PxrSemanticLabel.Chair) ||
                        Mathf.Abs(t.Normal.y) > .40f ||
                        delta.y < .12f || delta.y > .58f) continue;
                    delta.y = 0;
                    if (delta.magnitude < .12f || delta.magnitude > .43f) continue;
                    var normal = Vector3.ProjectOnPlane(t.Normal, Vector3.up).normalized;
                    if (Mathf.Abs(Vector3.Dot(normal, delta.normalized)) < .8f) continue;
                    back += delta.normalized * t.Area;
                    backArea += t.Area;
                }
                var hasBack = backArea >= .035f && back.magnitude / backArea >= .80f;
                // A semantic label cannot distinguish the emulator's compact side
                // table from a stool. Until another cue can prove a safe facing
                // direction, backless patches fail closed.
                if (!hasBack) continue;
                if (area < .105f) continue;
                var forward = -back.normalized;
                if (!TryCreateTarget(group, center, points, forward, floorY, error, false, out var seat))
                    continue;
                seats.Add(seat);
            }
            ExtractGeometricChairs(triangles, floorY, seats, diagnostics);
            return seats;
        }

        private static void ExtractGeometricChairs(IReadOnlyList<RealityTriangle> triangles,
            float floorY, ICollection<SeatTarget> seats, SeatScanDiagnostics diagnostics)
        {
            // SDK source IDs identify spatial mesh blocks, not furniture instances.
            // A seat may cross a block seam, and its back may carry a different label.
            var geometry = triangles.Where(t => t.HasSemantic(PxrSemanticLabel.Chair) ||
                t.Label == (int)PxrSemanticLabel.Unknown && t.SourceId != -1).ToArray();
            var horizontal = geometry.Where(t => Mathf.Abs(t.Normal.y) > .7f &&
                t.Center.y - floorY >= .32f && t.Center.y - floorY <= .60f).ToArray();
            diagnostics.HorizontalTriangles = horizontal.Length;
            foreach (var group in Connected(horizontal))
            {
                diagnostics.SurfaceGroups++;
                if (!TryCreateGeometricTarget(group, geometry, floorY, diagnostics, out var target) ||
                    seats.Any(existing => Vector3.Distance(existing.Center, target.Center) < .25f))
                    continue;
                seats.Add(target);
            }
        }

        private static bool TryCreateGeometricTarget(IReadOnlyList<RealityTriangle> group,
            IReadOnlyList<RealityTriangle> source, float floorY,
            SeatScanDiagnostics diagnostics, out SeatTarget target)
        {
            target = null;
            var area = group.Sum(t => t.Area);
            if (area < .12f || area > .90f) { diagnostics.AreaRejected++; return false; }
            var center = group.Aggregate(Vector3.zero, (sum, triangle) =>
                sum + triangle.Center * triangle.Area) / area;
            var planeError = Mathf.Sqrt(group.Sum(t =>
                Mathf.Pow(t.Center.y - center.y, 2) * t.Area) / area);
            if (planeError > .05f) { diagnostics.PlaneRejected++; return false; }
            var points = group.SelectMany(t => new[] { t.A, t.B, t.C }).ToArray();

            var upper = Vector3.zero;
            var upperArea = 0f;
            foreach (var triangle in source)
            {
                if (Mathf.Abs(triangle.Normal.y) > .40f) continue;
                var delta = triangle.Center - center;
                if (delta.y < .12f || delta.y > .75f) continue;
                delta.y = 0;
                if (delta.magnitude < .12f || delta.magnitude > .65f) continue;
                var normal = Vector3.ProjectOnPlane(triangle.Normal, Vector3.up).normalized;
                if (Mathf.Abs(Vector3.Dot(normal, delta)) < .12f) continue;
                var outward = Vector3.Dot(normal, delta) >= 0 ? normal : -normal;
                var edge = points.Max(point => Vector3.Dot(point - center, outward));
                // A neighbouring wall or an object above a table cannot stand in
                // for a backrest. Only vertical structure at the seat edge counts.
                if (Mathf.Abs(Vector3.Dot(delta, outward) - edge) > .10f) continue;
                var side = Vector3.Cross(Vector3.up, outward);
                var lateral = Vector3.Dot(delta, side);
                if (lateral < points.Min(point => Vector3.Dot(point - center, side)) - .08f ||
                    lateral > points.Max(point => Vector3.Dot(point - center, side)) + .08f) continue;
                // Surface normals preserve facing when only one side of a back
                // is scanned; radial vectors would skew it toward that fragment.
                upper += outward * triangle.Area;
                upperArea += triangle.Area;
            }
            // Sparse/slatted backs provide direction without a solid 0.18m² panel.
            if (upperArea < .035f || upper.magnitude / upperArea < .65f)
            { diagnostics.BackrestRejected++; return false; }

            var forward = -upper.normalized;
            var right = Vector3.Cross(Vector3.up, forward);
            var xs = points.Select(point => Vector3.Dot(point - center, right)).ToArray();
            var zs = points.Select(point => Vector3.Dot(point - center, forward)).ToArray();
            var width = xs.Max() - xs.Min();
            var depth = zs.Max() - zs.Min();
            if (width < .36f || width > .90f || depth < .34f || depth > .85f)
            { diagnostics.SizeRejected++; return false; }
            center += right * (xs.Max() + xs.Min()) * .5f +
                      forward * (zs.Max() + zs.Min()) * .5f;

            var supported = 0;
            for (var x = -3; x <= 3; x++)
                for (var z = -3; z <= 3; z++)
                {
                    var point = center + right * (x / 3f * (width * .5f - .045f)) +
                                forward * (z / 3f * (depth * .5f - .045f));
                    var hasSupport = group.Any(triangle => ContainsXZ(triangle, point));
                    if (hasSupport) supported++;
                    else if (Mathf.Abs(x) <= 1 && Mathf.Abs(z) <= 1)
                    { diagnostics.SupportRejected++; return false; }
                }
            if (supported < 42) { diagnostics.SupportRejected++; return false; }

            target = new SeatTarget
            {
                Center = center, Forward = forward, Width = width, Depth = depth,
                FloorY = floorY, PlaneError = planeError, Backless = false,
                GeometryInferred = true, SourceId = group[0].SourceId
            };
            return true;
        }

        private static IEnumerable<List<RealityTriangle>> Connected(IReadOnlyList<RealityTriangle> triangles)
        {
            var parents = Enumerable.Range(0, triangles.Count).ToArray();
            var vertices = new Dictionary<Vector3Int, int>();
            for (var i = 0; i < triangles.Count; i++)
                foreach (var vertex in new[] { triangles[i].A, triangles[i].B, triangles[i].C })
                {
                    var key = Vector3Int.RoundToInt(vertex / .008f);
                    if (vertices.TryGetValue(key, out var other))
                        parents[Root(parents, i)] = Root(parents, other);
                    else vertices.Add(key, i);
                }
            var groups = new Dictionary<int, List<RealityTriangle>>();
            for (var i = 0; i < triangles.Count; i++)
            {
                var key = Root(parents, i);
                if (!groups.TryGetValue(key, out var group))
                    groups[key] = group = new List<RealityTriangle>();
                group.Add(triangles[i]);
            }
            return groups.Values;
        }

        private static bool TryCompactStool(IReadOnlyList<RealityTriangle> group,
            IReadOnlyList<RealityTriangle> geometry, Vector3 center, Vector3[] points,
            float floorY, float error, out SeatTarget target)
        {
            target = null;
            var bestArea = float.MaxValue;
            var forward = Vector3.forward;
            for (var angle = 0f; angle < 90f; angle += .25f)
            {
                var candidate = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                var right = Vector3.Cross(Vector3.up, candidate);
                var xs = points.Select(v => Vector3.Dot(v - center, right)).ToArray();
                var zs = points.Select(v => Vector3.Dot(v - center, candidate)).ToArray();
                var width = xs.Max() - xs.Min();
                var depth = zs.Max() - zs.Min();
                var boundsArea = width * depth;
                if (boundsArea < bestArea) { bestArea = boundsArea; forward = candidate; }
            }
            if (!TryCreateTarget(group, center, points, forward, floorY, error, true, out target))
                return false;
            return TryOrientBackless(target, geometry, target.Center + target.Forward);
        }

        private static bool TryCreateTarget(IReadOnlyList<RealityTriangle> group, Vector3 center,
            Vector3[] points, Vector3 forward, float floorY, float error, bool backless,
            out SeatTarget target)
        {
            target = null;
            var right = Vector3.Cross(Vector3.up, forward);
            var xs = points.Select(v => Vector3.Dot(v - center, right)).ToArray();
            var zs = points.Select(v => Vector3.Dot(v - center, forward)).ToArray();
            var width = xs.Max() - xs.Min();
            var depth = zs.Max() - zs.Min();
            if (backless)
            {
                if (width < .28f || width > .42f || depth < .28f || depth > .42f) return false;
            }
            else if (width < .34f || width > .80f || depth < .32f || depth > .72f) return false;
            center += right * (xs.Max() + xs.Min()) * .5f + forward * (zs.Max() + zs.Min()) * .5f;
            // The complete inner contact rectangle must be supported. A convex hull
            // alone would silently bridge a missing patch or an open chair frame.
            for (var x = -3; x <= 3; x++)
                for (var z = -3; z <= 3; z++)
                {
                    var p = center + right * (x / 3f * (width * .5f - .045f)) +
                            forward * (z / 3f * (depth * .5f - .045f));
                    if (!group.Any(t => ContainsXZ(t, p))) return false;
                }
            target = new SeatTarget
            {
                Center = center, Forward = forward, Width = width, Depth = depth,
                FloorY = floorY, PlaneError = error, Backless = backless,
                SourceId = group[0].SourceId
            };
            return true;
        }

        public static bool TryOrientBackless(SeatTarget seat, IReadOnlyList<RealityTriangle> geometry,
            Vector3 approachFrom)
        {
            if (!seat.Backless) return HasClearance(seat, geometry);
            var originalForward = seat.Forward;
            var originalRight = seat.Right;
            var originalWidth = seat.Width;
            var originalDepth = seat.Depth;
            var toward = Vector3.ProjectOnPlane(approachFrom - seat.Center, Vector3.up);
            if (toward.sqrMagnitude < .0001f) toward = originalForward;
            toward.Normalize();
            var directions = new[] { originalForward, -originalForward, originalRight, -originalRight };
            var order = Enumerable.Range(0, directions.Length)
                .OrderByDescending(i => Vector3.Dot(directions[i], toward));
            foreach (var index in order)
            {
                seat.Forward = directions[index];
                seat.Width = index < 2 ? originalWidth : originalDepth;
                seat.Depth = index < 2 ? originalDepth : originalWidth;
                if (HasClearance(seat, geometry)) return true;
            }
            seat.Forward = originalForward;
            seat.Width = originalWidth;
            seat.Depth = originalDepth;
            return false;
        }

        private static int Root(int[] parents, int i)
        {
            while (parents[i] != i) { parents[i] = parents[parents[i]]; i = parents[i]; }
            return i;
        }

        private static bool ContainsXZ(RealityTriangle t, Vector3 p)
        {
            float Cross(Vector3 a, Vector3 b) => a.x * b.z - a.z * b.x;
            var a = Cross(t.B - t.A, p - t.A);
            var b = Cross(t.C - t.B, p - t.B);
            var c = Cross(t.A - t.C, p - t.C);
            return (a >= -.0001f && b >= -.0001f && c >= -.0001f) ||
                   (a <= .0001f && b <= .0001f && c <= .0001f);
        }

        public static bool SamePose(SeatTarget a, SeatTarget b)
        {
            if (a.Backless != b.Backless || a.GeometryInferred != b.GeometryInferred ||
                a.SourceId != b.SourceId) return false;
            var dimensionsMatch = Mathf.Abs(a.Width - b.Width) <= .04f &&
                                  Mathf.Abs(a.Depth - b.Depth) <= .04f;
            if (a.Backless && b.Backless)
                dimensionsMatch |= Mathf.Abs(a.Width - b.Depth) <= .04f &&
                                   Mathf.Abs(a.Depth - b.Width) <= .04f;
            return Vector3.Distance(a.Center, b.Center) <= .025f &&
                   (a.Backless && b.Backless || Vector3.Angle(a.Forward, b.Forward) <= 5f) &&
                   dimensionsMatch;
        }

        // Test an upper-body envelope along the complete sit/stand sweep.
        // All mesh triangles participate, including other chairs and unlabeled objects.
        public static bool HasClearance(SeatTarget seat, IReadOnlyList<RealityTriangle> triangles)
        {
            var inverse = Quaternion.Inverse(seat.Rotation);
            var front = seat.Depth * .5f + .66f;
            var center = new Vector3(0, .815f, (front - .10f) * .5f);
            var extent = new Vector3(.20f, .735f, (front + .10f) * .5f);
            foreach (var t in triangles)
            {
                // The avatar intentionally enters the target chair's own volume.
                // Meshes from every other source remain hard obstacles.
                if (seat.SourceId != -1 && t.SourceId == seat.SourceId) continue;
                var a = inverse * (t.A - seat.Center);
                var b = inverse * (t.B - seat.Center);
                var c = inverse * (t.C - seat.Center);
                if (BoundsOverlap(a, b, c, center, extent) &&
                    TriangleBox(a - center, b - center, c - center, extent)) return false;
                var footMin = seat.FloorY - seat.Center.y + .04f;
                var legCenter = new Vector3(0, (footMin + .08f) * .5f, .46f);
                var legExtent = new Vector3(.19f, (.08f - footMin) * .5f, .16f);
                if (BoundsOverlap(a, b, c, legCenter, legExtent) &&
                    TriangleBox(a - legCenter, b - legCenter, c - legCenter, legExtent)) return false;
            }
            return true;
        }

        private static bool BoundsOverlap(Vector3 a, Vector3 b, Vector3 c,
            Vector3 center, Vector3 extent)
            => Mathf.Min(a.x, Mathf.Min(b.x, c.x)) <= center.x + extent.x &&
               Mathf.Max(a.x, Mathf.Max(b.x, c.x)) >= center.x - extent.x &&
               Mathf.Min(a.y, Mathf.Min(b.y, c.y)) <= center.y + extent.y &&
               Mathf.Max(a.y, Mathf.Max(b.y, c.y)) >= center.y - extent.y &&
               Mathf.Min(a.z, Mathf.Min(b.z, c.z)) <= center.z + extent.z &&
               Mathf.Max(a.z, Mathf.Max(b.z, c.z)) >= center.z - extent.z;

        private static bool TriangleBox(Vector3 a, Vector3 b, Vector3 c, Vector3 extent)
        {
            bool Separates(Vector3 axis)
            {
                var x = Vector3.Dot(a, axis); var y = Vector3.Dot(b, axis); var z = Vector3.Dot(c, axis);
                var radius = extent.x * Mathf.Abs(axis.x) + extent.y * Mathf.Abs(axis.y) + extent.z * Mathf.Abs(axis.z);
                return Mathf.Min(x, Mathf.Min(y, z)) > radius || Mathf.Max(x, Mathf.Max(y, z)) < -radius;
            }
            var edges = new[] { b - a, c - b, a - c };
            if (Separates(Vector3.right) || Separates(Vector3.up) || Separates(Vector3.forward) ||
                Separates(Vector3.Cross(edges[0], edges[1]))) return false;
            foreach (var edge in edges)
                if (Separates(Vector3.Cross(edge, Vector3.right)) || Separates(Vector3.Cross(edge, Vector3.up)) ||
                    Separates(Vector3.Cross(edge, Vector3.forward))) return false;
            return true;
        }
    }
}
