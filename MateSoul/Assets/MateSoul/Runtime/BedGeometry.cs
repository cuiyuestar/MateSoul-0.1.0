using System;
using System.Collections.Generic;
using System.Linq;
using ByteDance.PICO.XR;
using MateSoul.RoomNavigation;
using UnityEngine;

namespace MateSoul.Runtime
{
    public enum BedHeadCue
    {
        Default,
        Pillow,
        Structure
    }

    [Serializable]
    public sealed class BedTarget
    {
        public string Id;
        public Vector3 Center, Forward;
        public float Width, Length, HeadEdge, FootEdge, LeftEdge, RightEdge, FloorY, PlaneError;
        public float ContactLift;
        public BedHeadCue HeadCue;
        public string CalibrationId;
        public int[] SourceIds;
        public float ObservedAt;
        public int StableSamples;
        public bool Available;

        public Quaternion Rotation => Quaternion.LookRotation(Forward, Vector3.up);
        public Quaternion LyingRotation => Quaternion.LookRotation(Vector3.up, -Forward);
        public Vector3 Right => Vector3.Cross(Vector3.up, Forward);
        public Vector3 Dock => BedGeometry.MountCandidates(this)
            .First(candidate => candidate.Edge == BedMountEdge.Foot).IdealDock;
        public Vector3 ContactRoot => Center + Forward * (Length * .42f) +
                                      Vector3.up * (.035f + ContactLift);
        public bool IsFresh => Available;
        public bool CalibrationAssisted => !string.IsNullOrEmpty(CalibrationId);
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
        private const float SurfacePlaneTolerance = .10f;
        private const float SurfaceDetailTolerance = .20f;

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
            // A mattress can span several spatial-mesh blocks. Its connected
            // geometry, rather than a block identifier, defines the surface.
            var horizontal = triangles.Where(triangle =>
                    triangle.HasSemantic(PxrSemanticLabel.Bed) &&
                    Mathf.Abs(triangle.Normal.y) > .6f &&
                    triangle.Center.y - floorY >= .35f &&
                    triangle.Center.y - floorY <= .9f)
                .ToArray();
            var candidates = new List<BedTarget>();
            foreach (var group in Connected(horizontal))
                if (TryCreate(group, triangles, floorY, out var target))
                    candidates.Add(target);

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

        public static List<BedTarget> Extract(IReadOnlyList<RealityTriangle> triangles,
            float floorY, IReadOnlyList<RealityItemObservation> calibratedBeds)
        {
            var result = Extract(triangles, floorY);
            foreach (var bed in calibratedBeds)
            {
                if (!IsUsableCalibration(bed) ||
                    result.Any(target => InCalibration(target.Center, bed))) continue;

                // Calibration supplies identity and a search region, never a
                // synthetic mattress. Unknown is accepted only inside that region.
                var local = triangles.Where(triangle =>
                    (triangle.HasSemantic(PxrSemanticLabel.Bed) ||
                     triangle.HasSemantic(PxrSemanticLabel.Unknown)) &&
                    InCalibration(triangle.A, bed) &&
                    InCalibration(triangle.B, bed) &&
                    InCalibration(triangle.C, bed)).ToArray();
                var horizontal = local.Where(triangle =>
                        Mathf.Abs(triangle.Normal.y) > .6f &&
                        triangle.Center.y - floorY >= .35f &&
                        triangle.Center.y - floorY <= .9f)
                    .OrderBy(triangle => triangle.Center.y).ToArray();
                if (horizontal.Length == 0) continue;

                // A tall bed box can include its headboard. Find the height band
                // with most measured surface area rather than using the box top.
                var start = 0;
                var bestStart = 0;
                var bestEnd = 0;
                var area = 0f;
                var bestArea = 0f;
                for (var end = 0; end < horizontal.Length; end++)
                {
                    area += horizontal[end].Area;
                    while (horizontal[end].Center.y - horizontal[start].Center.y >
                           SurfacePlaneTolerance * 2f)
                        area -= horizontal[start++].Area;
                    if (area <= bestArea) continue;
                    bestArea = area;
                    bestStart = start;
                    bestEnd = end + 1;
                }
                var surface = horizontal.Skip(bestStart).Take(bestEnd - bestStart).ToArray();
                if (!TryCreate(surface, local, floorY, out var candidate, bed)) continue;
                if (result.Any(target =>
                    Vector3.ProjectOnPlane(target.Center - candidate.Center, Vector3.up).magnitude <
                    Mathf.Min(target.Length, candidate.Length) * .45f)) continue;
                candidate.CalibrationId = bed.Id;
                candidate.ContactLift = GetSurfaceContactLift(candidate, triangles);
                result.Add(candidate);
            }
            return result;
        }

        public static bool IsUsableCalibration(RealityItemObservation bed)
        {
            if (bed == null || bed.Semantic != RealitySemantic.Bed) return false;
            bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
            var center = bed.Center;
            var size = bed.Size;
            var rotation = bed.Rotation;
            if (!Finite(center.x) || !Finite(center.y) || !Finite(center.z) ||
                !Finite(size.x) || !Finite(size.y) || !Finite(size.z) ||
                !Finite(rotation.x) || !Finite(rotation.y) ||
                !Finite(rotation.z) || !Finite(rotation.w)) return false;
            var norm = Quaternion.Dot(rotation, rotation);
            return norm > .99f && norm < 1.01f &&
                   Vector3.Dot(rotation * Vector3.up, Vector3.up) > .9f &&
                   Mathf.Min(size.x, size.z) >= .75f &&
                   Mathf.Max(size.x, size.z) >= 1.65f &&
                   Mathf.Max(size.x, size.z) <= 3.5f && size.y > 0;
        }

        private static bool InCalibration(Vector3 point, RealityItemObservation bed)
        {
            var local = Quaternion.Inverse(bed.Rotation) * (point - bed.Center);
            var half = bed.Size * .5f + Vector3.one * .15f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y &&
                   Mathf.Abs(local.z) <= half.z;
        }

        private static bool TryCreate(IReadOnlyList<RealityTriangle> group,
            IReadOnlyList<RealityTriangle> all, float floorY, out BedTarget target,
            RealityItemObservation calibration = null)
        {
            target = null;
            var area = group.Sum(triangle => triangle.Area);
            if (area < .8f || area > 8f) return false;
            var surfaceY = group.Sum(triangle => triangle.Center.y * triangle.Area) / area;
            var error = Mathf.Sqrt(group.Sum(triangle =>
                Mathf.Pow(triangle.Center.y - surfaceY, 2) * triangle.Area) / area);
            if (error > SurfacePlaneTolerance) return false;

            var points = group.SelectMany(triangle =>
                new[] { triangle.A, triangle.B, triangle.C }).ToArray();
            Vector3 center, right, axis;
            float width, length;
            if (calibration == null)
                FitOrientedBounds(points, surfaceY, out center,
                    out right, out axis, out width, out length);
            else
            {
                axis = Vector3.ProjectOnPlane(calibration.Rotation *
                    (calibration.Size.z >= calibration.Size.x ? Vector3.forward : Vector3.right),
                    Vector3.up).normalized;
                right = Vector3.Cross(Vector3.up, axis);
                var xs = points.Select(point => Vector3.Dot(point - calibration.Center, right)).ToArray();
                var zs = points.Select(point => Vector3.Dot(point - calibration.Center, axis)).ToArray();
                width = xs.Max() - xs.Min();
                length = zs.Max() - zs.Min();
                center = calibration.Center + right * ((xs.Max() + xs.Min()) * .5f) +
                         axis * ((zs.Max() + zs.Min()) * .5f);
                center.y = surfaceY;
                if (width < Mathf.Min(calibration.Size.x, calibration.Size.z) * .65f ||
                    length < Mathf.Max(calibration.Size.x, calibration.Size.z) * .7f) return false;
            }
            if (width < .75f || width > 2.3f || length < 1.65f || length > 2.65f ||
                area / (width * length) < .45f)
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
            if (supported < acrossSamples * lengthSamples * .55f) return false;

            var sourceIds = all.Where(triangle =>
                {
                    if (!triangle.HasSemantic(PxrSemanticLabel.Bed) &&
                        !(calibration != null && triangle.HasSemantic(PxrSemanticLabel.Unknown)))
                        return false;
                    var delta = triangle.Center - center;
                    return Mathf.Abs(Vector3.Dot(delta, right)) <= width * .65f &&
                           Mathf.Abs(Vector3.Dot(delta, axis)) <= length * .65f &&
                           triangle.Center.y >= surfaceY - .4f &&
                           triangle.Center.y <= surfaceY + .45f;
                })
                .Select(triangle => triangle.SourceId).Distinct().OrderBy(id => id).ToArray();

            // Pillow-shaped low horizontal patches are a better head cue than
            // requiring a distinct headboard in the scanned mesh.
            var pillowNegative = PillowTopArea(all, center, right, -axis,
                width, length, surfaceY);
            var pillowPositive = PillowTopArea(all, center, right, axis,
                width, length, surfaceY);
            var headCue = BedHeadCue.Default;
            var towardHead = axis;
            if (Mathf.Max(pillowNegative, pillowPositive) >= .04f &&
                Mathf.Max(pillowNegative, pillowPositive) >
                Mathf.Min(pillowNegative, pillowPositive) * 1.5f)
            {
                towardHead = pillowPositive > pillowNegative ? axis : -axis;
                headCue = BedHeadCue.Pillow;
            }
            var negative = UpperStructureArea(all, sourceIds, center, right, axis,
                width, length, surfaceY, -1);
            var positive = UpperStructureArea(all, sourceIds, center, right, axis,
                width, length, surfaceY, 1);
            var strongest = Mathf.Max(negative, positive);
            var weakest = Mathf.Min(negative, positive);
            if (headCue == BedHeadCue.Default && strongest >= .12f &&
                strongest >= weakest * 2f)
            {
                towardHead = positive > negative ? axis : -axis;
                headCue = BedHeadCue.Structure;
            }
            if (headCue == BedHeadCue.Default &&
                (axis.z < -.001f || Mathf.Abs(axis.z) <= .001f && axis.x < 0))
                towardHead = -axis;

            var forward = -towardHead;
            right = Vector3.Cross(Vector3.up, forward);
            var mergedPoints = all.Where(triangle => sourceIds.Contains(triangle.SourceId))
                .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
                .ToArray();
            var footEdge = mergedPoints.Max(point => Vector3.Dot(point - center, forward));
            var headEdge = -mergedPoints.Min(point => Vector3.Dot(point - center, forward));
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
                HeadEdge = headEdge,
                FootEdge = footEdge,
                LeftEdge = leftEdge,
                RightEdge = rightEdge,
                FloorY = floorY,
                PlaneError = error,
                HeadCue = headCue,
                SourceIds = sourceIds
            };
            target.ContactLift = GetSurfaceContactLift(target, all);
            return true;
        }

        private static bool IsPillowGeometry(RealityTriangle triangle, Vector3 center,
            Vector3 right, Vector3 towardHead, float width, float length, float surfaceY)
        {
            if (!triangle.HasSemantic(PxrSemanticLabel.Bed) &&
                !triangle.HasSemantic(PxrSemanticLabel.Unknown)) return false;
            bool Inside(Vector3 point)
            {
                var delta = point - center;
                var along = Vector3.Dot(delta, towardHead);
                return point.y >= surfaceY - .01f && point.y <= surfaceY + .20f &&
                       Mathf.Abs(Vector3.Dot(delta, right)) <= width * .5f &&
                       along >= length * .20f && along <= length * .50f;
            }
            return Inside(triangle.A) && Inside(triangle.B) && Inside(triangle.C);
        }

        private static float PillowTopArea(IReadOnlyList<RealityTriangle> all,
            Vector3 center, Vector3 right, Vector3 towardHead,
            float width, float length, float surfaceY)
        {
            var tops = all.Where(triangle =>
                Mathf.Abs(triangle.Normal.y) >= .75f &&
                triangle.Center.y >= surfaceY + .025f &&
                IsPillowGeometry(triangle, center, right, towardHead,
                    width, length, surfaceY)).ToArray();
            var area = 0f;
            foreach (var patch in Connected(tops))
            {
                var patchArea = patch.Sum(triangle => triangle.Area);
                if (patchArea < .04f || patchArea > .6f) continue;
                var points = patch.SelectMany(triangle =>
                    new[] { triangle.A, triangle.B, triangle.C }).ToArray();
                var xs = points.Select(point => Vector3.Dot(point - center, right)).ToArray();
                var zs = points.Select(point => Vector3.Dot(point - center, towardHead)).ToArray();
                var patchWidth = xs.Max() - xs.Min();
                var patchLength = zs.Max() - zs.Min();
                // Adjacent pillows can become one connected patch in a coarse scan.
                if (patchWidth >= .18f && patchWidth <= Mathf.Max(.85f, width * .9f) &&
                    patchLength >= .12f && patchLength <= .60f)
                    area += patchArea;
            }
            return area;
        }

        public static void AlignUncertainDirection(BedTarget candidate, BedTarget previous)
        {
            if (candidate.HeadCue != BedHeadCue.Default || previous.StableSamples == 0) return;
            if (candidate.Width >= 1.65f &&
                Mathf.Abs(candidate.Length - candidate.Width) < .12f &&
                Mathf.Abs(Vector3.Dot(candidate.Right, previous.Forward)) >
                Mathf.Abs(Vector3.Dot(candidate.Forward, previous.Forward)))
            {
                candidate.Forward = candidate.Right;
                var dimension = candidate.Width;
                candidate.Width = candidate.Length;
                candidate.Length = dimension;
                var head = candidate.HeadEdge;
                var foot = candidate.FootEdge;
                candidate.HeadEdge = candidate.LeftEdge;
                candidate.FootEdge = candidate.RightEdge;
                candidate.LeftEdge = foot;
                candidate.RightEdge = head;
            }
            if (Vector3.Dot(candidate.Forward, previous.Forward) >= 0) return;
            candidate.Forward = -candidate.Forward;
            var edge = candidate.HeadEdge;
            candidate.HeadEdge = candidate.FootEdge;
            candidate.FootEdge = edge;
            edge = candidate.LeftEdge;
            candidate.LeftEdge = candidate.RightEdge;
            candidate.RightEdge = edge;
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
            => Vector3.Distance(first.Center, second.Center) <= .06f &&
               Mathf.Abs(first.ContactLift - second.ContactLift) <= .035f &&
               Vector3.Angle(first.Forward, second.Forward) <= 6f &&
               Mathf.Abs(first.Width - second.Width) <= .10f &&
               Mathf.Abs(first.Length - second.Length) <= .12f &&
               Mathf.Abs(first.FootEdge - second.FootEdge) <= .12f &&
               Mathf.Abs(first.LeftEdge - second.LeftEdge) <= .12f &&
               Mathf.Abs(first.RightEdge - second.RightEdge) <= .12f;

        public static bool HasClearance(BedTarget bed,
            IReadOnlyList<RealityTriangle> triangles)
        {
            var inverse = Quaternion.Inverse(bed.Rotation);
            foreach (var triangle in triangles)
            {
                if (IsLowSurfaceDetail(triangle, bed)) continue;
                if (bed.HeadCue == BedHeadCue.Pillow &&
                    IsPillowGeometry(triangle, bed.Center, bed.Right, -bed.Forward,
                        bed.Width, bed.Length, bed.Center.y)) continue;
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

        private static bool IsLowSurfaceDetail(RealityTriangle triangle, BedTarget bed)
        {
            if (!triangle.HasSemantic(PxrSemanticLabel.Bed) &&
                !triangle.HasSemantic(PxrSemanticLabel.Unknown)) return false;
            var ceiling = bed.Center.y + SurfaceDetailTolerance;
            return triangle.A.y <= ceiling && triangle.B.y <= ceiling &&
                   triangle.C.y <= ceiling;
        }

        private static float GetSurfaceContactLift(BedTarget bed,
            IReadOnlyList<RealityTriangle> triangles)
        {
            var inverse = Quaternion.Inverse(bed.Rotation);
            var extent = new Vector3(Mathf.Min(.30f, bed.Width * .3f),
                SurfaceDetailTolerance, bed.Length * .42f);
            var highest = bed.Center.y;
            foreach (var triangle in triangles)
            {
                if (!IsLowSurfaceDetail(triangle, bed)) continue;
                if (bed.HeadCue == BedHeadCue.Pillow &&
                    IsPillowGeometry(triangle, bed.Center, bed.Right, -bed.Forward,
                        bed.Width, bed.Length, bed.Center.y)) continue;
                var a = inverse * (triangle.A - bed.Center);
                var b = inverse * (triangle.B - bed.Center);
                var c = inverse * (triangle.C - bed.Center);
                if (Intersects(a, b, c, Vector3.zero, extent))
                    highest = Mathf.Max(highest,
                        Mathf.Max(triangle.A.y, Mathf.Max(triangle.B.y, triangle.C.y)));
            }
            // The lying torso proxy already clears the fitted plane by 4cm.
            // Keep another 1cm above tolerated detail instead of ignoring its collider.
            return Mathf.Max(0, highest - bed.Center.y - .03f);
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
                if (ignored.Contains(triangle.SourceId) &&
                    (!bed.CalibrationAssisted || IsCalibratedFrame(triangle, bed))) continue;
                var a = inverse * (triangle.A - center);
                var b = inverse * (triangle.B - center);
                var c = inverse * (triangle.C - center);
                if (Intersects(a, b, c, Vector3.zero, extent)) return false;
            }
            return true;
        }

        private static bool IsCalibratedFrame(RealityTriangle triangle, BedTarget bed)
        {
            if (!triangle.HasSemantic(PxrSemanticLabel.Bed) &&
                !triangle.HasSemantic(PxrSemanticLabel.Unknown)) return false;
            bool Inside(Vector3 point)
            {
                var delta = point - bed.Center;
                return Mathf.Abs(Vector3.Dot(delta, bed.Right)) <= bed.Width * .65f &&
                       Mathf.Abs(Vector3.Dot(delta, bed.Forward)) <= bed.Length * .65f &&
                       point.y >= bed.FloorY - .05f && point.y <= bed.Center.y + .45f;
            }
            return Inside(triangle.A) && Inside(triangle.B) && Inside(triangle.C);
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
