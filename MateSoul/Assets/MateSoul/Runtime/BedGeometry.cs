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
        public RealitySemantic Semantic = RealitySemantic.Bed;
        public string CalibrationId;
        public bool CalibrationFallback;
        // True when Width had to be borrowed from the calibration box height
        // because the horizontal footprint was degenerate. Such a width is not
        // a horizontal measurement and must not steer the lying direction.
        public bool WidthFromHeight;
        public int[] SourceIds;
        public float ObservedAt;
        public int StableSamples;
        // Consecutive scans that disagreed with the cached seat. Used to ride out
        // the constant mesh refinement instead of dropping the target on the
        // first outlier.
        public int MissedScans;
        // 连续多少次扫描判定“上方净空不足”。和 MissedScans 同理，单次净空抖动
        // 不应该立刻撤掉已经稳定的躺卧入口。
        public int BlockedScans;
        public bool Available;

        public Quaternion Rotation => Quaternion.LookRotation(Forward, Vector3.up);
        public bool IsSofa => Semantic == RealitySemantic.Sofa;
        // A bed has a head and a foot, so the body lies along Forward. A sofa has
        // neither, and its longest horizontal side runs parallel to the back, so
        // the body lies along that side instead of into the backrest. A width
        // borrowed from the box height is not a horizontal measurement, so it
        // must never flip the body off the sofa's real long side.
        public bool LiesAcross => IsSofa && !WidthFromHeight && Width > Length;
        public Vector3 LyingAxis => LiesAcross ? Right : Forward;
        public Quaternion LyingRotation => Quaternion.LookRotation(Vector3.up, -LyingAxis);
        public Vector3 Right => Vector3.Cross(Vector3.up, Forward);
        public Vector3 Dock => BedGeometry.MountCandidates(this)
            .First(candidate => candidate.Edge == BedMountEdge.Foot).IdealDock;
        // .725 is the authored torso offset inside the lying capsule, so a sofa
        // is centred on its seat instead of shifted towards one arm. A bed keeps
        // its authored head-to-foot placement.
        public Vector3 ContactRoot => Center +
                                      (IsSofa ? LyingAxis * .725f : Forward * (Length * .42f)) +
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

    // Field diagnosis for the on-device scan: how many surfaces carried the
    // bed/sofa label at all, how many survived the height band, and which rule
    // rejected each connected candidate.
    public sealed class BedScanDiagnostics
    {
        private const int CandidateLimit = 6;
        private readonly Dictionary<string, int> rejected = new Dictionary<string, int>();
        public int Semantic, InBand, Groups;
        public string Edges = "none";
        public readonly List<string> Candidates = new List<string>();

        public void Clear()
        {
            Semantic = InBand = Groups = 0;
            Edges = "none";
            rejected.Clear();
            Candidates.Clear();
        }

        public void Reject(string reason, float width, float length, float coverage)
        {
            rejected[reason] = rejected.TryGetValue(reason, out var count) ? count + 1 : 1;
            if (Candidates.Count < CandidateLimit)
                Candidates.Add($"w={width:F2} l={length:F2} cov={coverage:F2} {reason}");
        }

        public void Accept(float width, float length, float coverage)
        {
            if (Candidates.Count >= CandidateLimit) return;
            Candidates.Add($"w={width:F2} l={length:F2} cov={coverage:F2} accepted");
        }

        public void Fallback(float width, float length, float height)
        {
            if (Candidates.Count >= CandidateLimit) return;
            Candidates.Add($"w={width:F2} l={length:F2} h={height:F2} calibrationFallback");
        }

        public string Summary()
        {
            return rejected.Count == 0
                ? "none"
                : string.Join(",", rejected.OrderBy(pair => pair.Key)
                    .Select(pair => $"{pair.Key}:{pair.Value}"));
        }
    }

    public static class BedGeometry
    {
        // The dock only has to be far enough that the avatar does not start the
        // stand-to-lie transfer inside the seat. A short stand-off keeps the dock
        // reachable in narrow rooms; the transfer itself tolerates a light clip.
        private const float MountClearance = .16f;
        private const float SurfacePlaneTolerance = .10f;
        private const float SurfaceDetailTolerance = .20f;
        // Low tatami-style seats and deep three-seat sofas sit outside the
        // original mattress envelope.
        private const float MinimumSurfaceHeight = .25f;
        private const float MaximumSurfaceHeight = .9f;
        private const float SofaMaximumWidth = 2.5f;
        private const float BedMaximumWidth = 2.3f;
        // Anything smaller cannot hold a body. Scattered mesh blocks otherwise
        // leave centimetre-sized patches that claim a seat, put the dock inside
        // the furniture and hide the calibration fallback that does work.
        private const float MinimumSeatWidth = .45f;
        private const float MinimumSeatLength = .6f;
        // A calibration fallback lands a couple of centimetres inside the
        // measured surface instead of hovering over an approximated cushion.
        private const float FallbackContactLift = -.02f;

        private static RealitySemantic SemanticOf(IEnumerable<RealityTriangle> group)
            => group.Any(triangle => triangle.HasSemantic(PxrSemanticLabel.Bed))
                ? RealitySemantic.Bed
                : RealitySemantic.Sofa;

        private static bool Matches(RealityTriangle triangle, RealitySemantic semantic)
            => semantic == RealitySemantic.Sofa
                ? triangle.HasSemantic(PxrSemanticLabel.Sofa)
                : triangle.HasSemantic(PxrSemanticLabel.Bed);

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

        public static List<BedTarget> Extract(IReadOnlyList<RealityTriangle> triangles, float floorY,
            BedScanDiagnostics diagnostics = null)
        {
            diagnostics?.Clear();
            // A mattress can span several spatial-mesh blocks. Its connected
            // geometry, rather than a block identifier, defines the surface.
            var semantic = triangles.Where(triangle =>
                    triangle.HasSemantic(PxrSemanticLabel.Bed) ||
                    triangle.HasSemantic(PxrSemanticLabel.Sofa))
                .ToArray();
            if (diagnostics != null) diagnostics.Semantic = semantic.Length;
            var horizontal = semantic.Where(triangle =>
                    Mathf.Abs(triangle.Normal.y) > .6f &&
                    triangle.Center.y - floorY >= MinimumSurfaceHeight &&
                    triangle.Center.y - floorY <= MaximumSurfaceHeight)
                .ToArray();
            if (diagnostics != null) diagnostics.InBand = horizontal.Length;
            // Every structure query inside TryCreate only accepts bed/sofa (or,
            // when calibrated, unknown) triangles, so filter once here instead of
            // walking the whole room for each connected surface.
            var furniture = triangles.Where(triangle =>
                    triangle.HasSemantic(PxrSemanticLabel.Bed) ||
                    triangle.HasSemantic(PxrSemanticLabel.Sofa) ||
                    triangle.HasSemantic(PxrSemanticLabel.Unknown))
                .ToArray();
            var candidates = new List<BedTarget>();
            foreach (var group in Connected(horizontal))
            {
                if (diagnostics != null) diagnostics.Groups++;
                if (TryCreate(group, furniture, floorY, SemanticOf(group), out var target,
                        null, diagnostics))
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

        public static List<BedTarget> Extract(IReadOnlyList<RealityTriangle> triangles,
            float floorY, IReadOnlyList<RealityItemObservation> calibratedBeds,
            BedScanDiagnostics diagnostics = null)
        {
            var result = Extract(triangles, floorY, diagnostics);
            foreach (var bed in calibratedBeds)
            {
                if (!IsUsableCalibration(bed)) continue;
                // A measured seat that already sits inside the calibration box is
                // that piece of furniture. Adopt the calibration identity instead
                // of skipping the box: the assisted flag is what keeps a scanned
                // sofa usable when the mesh grazes the standing envelope.
                var measured = result.FirstOrDefault(target => InCalibration(target.Center, bed));
                if (measured != null)
                {
                    if (string.IsNullOrEmpty(measured.CalibrationId))
                        measured.CalibrationId = bed.Id;
                    continue;
                }

                // Calibration supplies identity and a search region, never a
                // synthetic mattress. Unknown is accepted only inside that region.
                var local = triangles.Where(triangle =>
                    (Matches(triangle, bed.Semantic) ||
                     triangle.HasSemantic(PxrSemanticLabel.Unknown)) &&
                    InCalibration(triangle.A, bed) &&
                    InCalibration(triangle.B, bed) &&
                    InCalibration(triangle.C, bed)).ToArray();
                var horizontal = local.Where(triangle =>
                        Mathf.Abs(triangle.Normal.y) > .6f &&
                        triangle.Center.y - floorY >= MinimumSurfaceHeight &&
                        triangle.Center.y - floorY <= MaximumSurfaceHeight)
                    .OrderBy(triangle => triangle.Center.y).ToArray();
                if (horizontal.Length == 0)
                {
                    AddCalibrationFallback(result, bed, triangles, floorY, diagnostics);
                    continue;
                }

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
                if (!TryCreate(surface, local, floorY, bed.Semantic, out var candidate, bed,
                        diagnostics))
                {
                    AddCalibrationFallback(result, bed, triangles, floorY, diagnostics);
                    continue;
                }
                if (result.Any(target =>
                    Vector3.ProjectOnPlane(target.Center - candidate.Center, Vector3.up).magnitude <
                    Mathf.Min(target.Length, candidate.Length) * .45f)) continue;
                candidate.CalibrationId = bed.Id;
                candidate.ContactLift = GetSurfaceContactLift(candidate, triangles);
                result.Add(candidate);
            }
            return result;
        }

        private static void AddCalibrationFallback(List<BedTarget> result,
            RealityItemObservation bed, IReadOnlyList<RealityTriangle> triangles,
            float floorY, BedScanDiagnostics diagnostics)
        {
            var fallback = CreateCalibrationFallback(bed, triangles, floorY);
            if (result.Any(target => InCalibration(target.Center, bed))) return;
            result.Add(fallback);
            diagnostics?.Fallback(fallback.Width, fallback.Length,
                fallback.Center.y - fallback.FloorY);
        }

        // Room calibration keeps the furniture identity even when the scanned
        // mesh carries no usable seat label. The footprint comes from the box and
        // the height from the live mesh, so the avatar still lands on the real
        // surface instead of a synthetic plane. No size or length check may stop
        // this path: a calibration box that reached this point is used as is.
        private static BedTarget CreateCalibrationFallback(RealityItemObservation bed,
            IReadOnlyList<RealityTriangle> triangles, float floorY)
        {
            var axis = HorizontalAxis(bed);
            Footprint(bed, out var length, out var width, out var widthFromHeight);
            var sampled = SampleSeatHeight(bed, triangles, floorY);
            var boxTop = bed.Center.y + Mathf.Abs(bed.Size.y) * .5f - floorY;
            // Without a measured sample, prefer a seat-height plane over the top
            // of the box so the avatar embeds slightly instead of hovering.
            var height = sampled > 0
                ? Mathf.Clamp(sampled - floorY, MinimumSurfaceHeight, MaximumSurfaceHeight)
                : Mathf.Clamp(boxTop > .1f ? Mathf.Min(boxTop, .5f) : .4f,
                    MinimumSurfaceHeight, MaximumSurfaceHeight);
            var towardHead = axis;
            if (axis.z < -.001f || Mathf.Abs(axis.z) <= .001f && axis.x < 0)
                towardHead = -axis;
            return new BedTarget
            {
                Center = new Vector3(bed.Center.x, floorY + height, bed.Center.z),
                Forward = -towardHead,
                Width = Mathf.Clamp(width, .45f,
                    bed.Semantic == RealitySemantic.Sofa ? SofaMaximumWidth : BedMaximumWidth),
                Length = Mathf.Max(length, .6f),
                HeadEdge = length * .5f,
                FootEdge = length * .5f,
                LeftEdge = width * .5f,
                RightEdge = width * .5f,
                FloorY = floorY,
                HeadCue = BedHeadCue.Default,
                Semantic = bed.Semantic,
                CalibrationId = bed.Id,
                CalibrationFallback = true,
                WidthFromHeight = widthFromHeight,
                SourceIds = Array.Empty<int>(),
                ContactLift = FallbackContactLift
            };
        }

        // Longest horizontal side of a calibration box, plus the perpendicular
        // one. A 2D sofa box stores its depth in the vertical extent, so a
        // degenerate horizontal side borrows that value instead of collapsing.
        private static void Footprint(RealityItemObservation bed,
            out float length, out float width, out bool widthFromHeight)
        {
            length = Mathf.Max(Mathf.Abs(bed.Size.x), Mathf.Abs(bed.Size.z));
            width = Mathf.Min(Mathf.Abs(bed.Size.x), Mathf.Abs(bed.Size.z));
            widthFromHeight = width < .4f;
            if (widthFromHeight) width = Mathf.Abs(bed.Size.y);
            length = Mathf.Max(length, .6f);
            width = Mathf.Max(width, .45f);
        }

        // Room Capture can hand back a box whose long axis points up or whose
        // frame is not yaw-only. Any horizontal direction is better than
        // dropping the calibrated furniture entirely.
        private static Vector3 HorizontalAxis(RealityItemObservation bed)
        {
            var vertical = Mathf.Abs(bed.Size.z) >= Mathf.Abs(bed.Size.x);
            var axis = Vector3.ProjectOnPlane(
                bed.Rotation * (vertical ? Vector3.forward : Vector3.right), Vector3.up);
            if (axis.sqrMagnitude > .04f) return axis.normalized;
            axis = Vector3.ProjectOnPlane(
                bed.Rotation * (vertical ? Vector3.right : Vector3.forward), Vector3.up);
            if (axis.sqrMagnitude > .04f) return axis.normalized;
            return vertical ? Vector3.forward : Vector3.right;
        }

        // The seat is the horizontal band carrying the most measured area inside
        // the calibration box. Using a single band, instead of a fitted plane,
        // tolerates a cushion that is not perfectly flat. The floor under the
        // furniture is excluded by the same height band as the scan.
        private static float SampleSeatHeight(RealityItemObservation bed,
            IReadOnlyList<RealityTriangle> triangles, float floorY)
        {
            const float binSize = .04f;
            var bins = new Dictionary<int, float>();
            var bestBin = 0;
            var bestArea = 0f;
            foreach (var triangle in triangles)
            {
                if (!IsSeatSample(triangle, bed, floorY)) continue;
                var bin = Mathf.RoundToInt(triangle.Center.y / binSize);
                bins.TryGetValue(bin, out var area);
                area += triangle.Area;
                bins[bin] = area;
                if (area <= bestArea) continue;
                bestArea = area;
                bestBin = bin;
            }
            if (bestArea < .15f) return 0;
            var weighted = 0f;
            var total = 0f;
            foreach (var triangle in triangles)
            {
                if (!IsSeatSample(triangle, bed, floorY)) continue;
                if (Mathf.Abs(triangle.Center.y - bestBin * binSize) > binSize * 1.5f) continue;
                weighted += triangle.Center.y * triangle.Area;
                total += triangle.Area;
            }
            return total > .01f ? weighted / total : 0;
        }

        private static bool IsSeatSample(RealityTriangle triangle,
            RealityItemObservation bed, float floorY)
        {
            if (Mathf.Abs(triangle.Normal.y) < .5f) return false;
            if (triangle.Center.y - floorY < MinimumSurfaceHeight - .02f ||
                triangle.Center.y - floorY > MaximumSurfaceHeight) return false;
            return InSeatFootprint(triangle, bed);
        }

        // A circular footprint around the calibration box tolerates a rotated or
        // flat (2D) box. The height band is already enforced by IsSeatSample, so
        // this only has to decide whether the sample belongs to the furniture.
        private static bool InSeatFootprint(RealityTriangle triangle,
            RealityItemObservation bed)
        {
            Footprint(bed, out var length, out var width, out _);
            var radius = Mathf.Max(length, width) * .5f + .25f;
            var delta = Vector3.ProjectOnPlane(triangle.Center - bed.Center, Vector3.up);
            return delta.sqrMagnitude <= radius * radius;
        }

        public static bool IsUsableCalibration(RealityItemObservation bed)
        {
            if (bed == null || (bed.Semantic != RealitySemantic.Bed &&
                bed.Semantic != RealitySemantic.Sofa)) return false;
            bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
            var center = bed.Center;
            var size = bed.Size;
            var rotation = bed.Rotation;
            if (!Finite(center.x) || !Finite(center.y) || !Finite(center.z) ||
                !Finite(size.x) || !Finite(size.y) || !Finite(size.z) ||
                !Finite(rotation.x) || !Finite(rotation.y) ||
                !Finite(rotation.z) || !Finite(rotation.w)) return false;
            var norm = Quaternion.Dot(rotation, rotation);
            // Room Capture may report a sofa as a flat 2D box (one axis is a
            // wall thickness) or with an unusual frame, so only the values
            // themselves are validated here. Size and orientation are handled
            // when the fallback is built.
            return norm > .99f && norm < 1.01f &&
                   Mathf.Abs(size.x) > .01f && Mathf.Abs(size.y) > .01f &&
                   Mathf.Abs(size.z) > .01f;
        }

        // Region test for calibration-guided extraction. A circle around the box
        // works for every reported frame, including a flat 2D sofa box or a box
        // tilted by Room Capture, where a rotated axis-aligned test would reject
        // the real mesh underneath.
        private static bool InCalibration(Vector3 point, RealityItemObservation bed)
        {
            Footprint(bed, out var length, out var width, out _);
            var radius = Mathf.Max(length, width) * .5f + .2f;
            var delta = point - bed.Center;
            var horizontal = new Vector2(delta.x, delta.z).sqrMagnitude;
            var halfHeight = Mathf.Max(Mathf.Abs(bed.Size.y), .2f) * .5f + .35f;
            return horizontal <= radius * radius && Mathf.Abs(delta.y) <= halfHeight;
        }

        private static bool TryCreate(IReadOnlyList<RealityTriangle> group,
            IReadOnlyList<RealityTriangle> all, float floorY, RealitySemantic semantic,
            out BedTarget target, RealityItemObservation calibration = null,
            BedScanDiagnostics diagnostics = null)
        {
            target = null;
            // Size, length and coverage are deliberately not gated here: a
            // partially scanned sofa or a table-height cushion still has to be
            // reachable. Ranking and overlap suppression happen in Extract.
            var area = Mathf.Max(group.Sum(triangle => triangle.Area), .0001f);
            var surfaceY = group.Sum(triangle => triangle.Center.y * triangle.Area) / area;
            var error = Mathf.Sqrt(group.Sum(triangle =>
                Mathf.Pow(triangle.Center.y - surfaceY, 2) * triangle.Area) / area);
            if (semantic == RealitySemantic.Sofa)
            {
                // A sofa is not one flat plane: its arms and the top of the
                // backrest are horizontal too and sit above the cushions.
                // Fitting all of them lifts the whole surface, and the body then
                // floats at armrest height. Cushions cover most of the area and
                // stay below the group mean, so the mean is the split line.
                var seat = group.Where(triangle =>
                    triangle.Center.y <= surfaceY + .08f).ToArray();
                if (seat.Length > 0)
                {
                    group = seat;
                    area = Mathf.Max(seat.Sum(triangle => triangle.Area), .0001f);
                    surfaceY = seat.Sum(triangle => triangle.Center.y * triangle.Area) / area;
                    error = Mathf.Sqrt(seat.Sum(triangle =>
                        Mathf.Pow(triangle.Center.y - surfaceY, 2) * triangle.Area) / area);
                }
            }
            // A calibration box already pins the furniture down, so an uneven
            // cushion is accepted there; the flat-plane fit only guards the scan.
            if (error > SurfacePlaneTolerance && calibration == null)
            {
                diagnostics?.Reject("planeError>0.10", 0, 0, area);
                return false;
            }

            var points = group.SelectMany(triangle =>
                new[] { triangle.A, triangle.B, triangle.C }).ToArray();
            Vector3 center, right, axis;
            float width, length;
            if (calibration == null)
                FitOrientedBounds(points, surfaceY, out center,
                    out right, out axis, out width, out length);
            else
            {
                axis = HorizontalAxis(calibration);
                right = Vector3.Cross(Vector3.up, axis);
                var xs = points.Select(point => Vector3.Dot(point - calibration.Center, right)).ToArray();
                var zs = points.Select(point => Vector3.Dot(point - calibration.Center, axis)).ToArray();
                width = xs.Max() - xs.Min();
                length = zs.Max() - zs.Min();
                center = calibration.Center + right * ((xs.Max() + xs.Min()) * .5f) +
                         axis * ((zs.Max() + zs.Min()) * .5f);
                center.y = surfaceY;
            }
            var coverage = area / Mathf.Max(width * length, .0001f);

            if (width < MinimumSeatWidth || length < MinimumSeatLength)
            {
                diagnostics?.Reject("fragment", width, length, coverage);
                return false;
            }

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
            if (supported < acrossSamples * lengthSamples * .55f)
            {
                diagnostics?.Reject($"support<{supported / (float)(acrossSamples * lengthSamples):F2}",
                    width, length, coverage);
                return false;
            }

            var sourceIds = all.Where(triangle =>
                {
                    if (!Matches(triangle, semantic) &&
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
            // requiring a distinct headboard in the scanned mesh. A sofa has no
            // head end, so its direction is left deliberately unspecified.
            var headCue = BedHeadCue.Default;
            var towardHead = axis;
            if (semantic == RealitySemantic.Bed)
            {
                var pillowNegative = PillowTopArea(all, center, right, -axis,
                    width, length, surfaceY);
                var pillowPositive = PillowTopArea(all, center, right, axis,
                    width, length, surfaceY);
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
            }
            if (headCue == BedHeadCue.Default &&
                (axis.z < -.001f || Mathf.Abs(axis.z) <= .001f && axis.x < 0))
                towardHead = -axis;

            var forward = -towardHead;
            right = Vector3.Cross(Vector3.up, forward);
            // A spatial-mesh block can also carry floor or wall triangles metres
            // across whose centroid sits inside the seat box while the vertices
            // reach far outside it. Only vertices that actually belong to the
            // seat may shape the edges, or a single oversized triangle discards
            // a sofa that was scanned completely.
            var mergedPoints = all.Where(triangle => sourceIds.Contains(triangle.SourceId))
                .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
                .Where(point => IsSeatPoint(point, center, right, forward, width, length, surfaceY))
                .ToArray();
            if (mergedPoints.Length == 0)
            {
                diagnostics?.Reject("seatPointsEmpty", width, length, coverage);
                return false;
            }
            // An asymmetric cushion or a block that also holds an armrest shifts
            // the measured edge. Clamp it into the band a dock can use instead of
            // throwing away an otherwise usable scanned sofa.
            var footEdge = Mathf.Clamp(
                mergedPoints.Max(point => Vector3.Dot(point - center, forward)),
                length * .4f, length * 1.1f);
            var headEdge = -mergedPoints.Min(point => Vector3.Dot(point - center, forward));
            // Headboards and integrated bedside furniture can be wider than the
            // mattress. Side entry uses the central body lane instead of those
            // end-only protrusions.
            var sidePoints = mergedPoints.Where(point =>
                Mathf.Abs(Vector3.Dot(point - center, forward)) <= length * .42f).ToArray();
            if (sidePoints.Length == 0)
            {
                diagnostics?.Reject("sidePointsEmpty", width, length, coverage);
                return false;
            }
            var leftEdge = Mathf.Clamp(
                -sidePoints.Min(point => Vector3.Dot(point - center, right)),
                width * .4f, width * .85f);
            var rightEdge = Mathf.Clamp(
                sidePoints.Max(point => Vector3.Dot(point - center, right)),
                width * .4f, width * .85f);
            if (diagnostics != null)
                diagnostics.Edges =
                    $"foot={footEdge:F2} head={headEdge:F2} left={leftEdge:F2} right={rightEdge:F2}";

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
                Semantic = semantic,
                SourceIds = sourceIds
            };
            target.ContactLift = GetSurfaceContactLift(target, all);
            diagnostics?.Accept(width, length, coverage);
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
            if (previous.StableSamples == 0) return;
            if (candidate.HeadCue == BedHeadCue.Default &&
                candidate.Width >= 1.65f &&
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
            // The extraction can report the same seat with either sign of Forward
            // (the pillow cue does not settle it), and a 180° flip between two
            // scans breaks the pose comparison. Keep the direction the target was
            // already committed to; a real turn is adopted once it persists.
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

        // Vertices that actually sit over the seat surface. Large planar
        // triangles from the spatial mesh keep their centroid inside the target
        // box while their corners reach metres away, so a plain bounding pass
        // over those triangles would report an absurd seat edge.
        private static bool IsSeatPoint(Vector3 point, Vector3 center, Vector3 right,
            Vector3 forward, float width, float length, float surfaceY)
        {
            var delta = point - center;
            return Mathf.Abs(Vector3.Dot(delta, right)) <= width * .6f &&
                   Mathf.Abs(Vector3.Dot(delta, forward)) <= length * .6f &&
                   point.y >= surfaceY - .4f && point.y <= surfaceY + .45f;
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
            // Cheap world-space reject before the per-vertex rotation: only the
            // body envelope matters, not the rest of the room.
            var reach = bed.Length * .42f + .70f;
            foreach (var triangle in triangles)
            {
                if ((triangle.Center - bed.Center).sqrMagnitude > reach * reach) continue;
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
            if (!Matches(triangle, bed.Semantic) &&
                !triangle.HasSemantic(PxrSemanticLabel.Unknown)) return false;
            var ceiling = bed.Center.y + SurfaceDetailTolerance;
            return triangle.A.y <= ceiling && triangle.B.y <= ceiling &&
                   triangle.C.y <= ceiling;
        }

        private static float GetSurfaceContactLift(BedTarget bed,
            IReadOnlyList<RealityTriangle> triangles)
        {
            var inverse = Quaternion.Inverse(bed.Rotation);
            // The probe stands for the body itself, so it may never be longer than
            // the lying capsule. Deriving it from Length used to sweep the whole
            // sofa, arms included, and lift the body to armrest height.
            var halfLength = Mathf.Min(bed.Length * .42f, .78f);
            var extent = new Vector3(Mathf.Min(.30f, bed.Width * .3f),
                SurfaceDetailTolerance, halfLength);
            var highest = bed.Center.y;
            var reach = halfLength + .70f;
            foreach (var triangle in triangles)
            {
                if ((triangle.Center - bed.Center).sqrMagnitude > reach * reach) continue;
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
            // Only the standing torso has to fit. Keep the envelope tight so a
            // dock that grazes the cushion or an armrest is still accepted.
            var extent = new Vector3(.30f, .80f, .27f);
            foreach (var triangle in triangles)
            {
                if (ignored.Contains(triangle.SourceId)) continue;
                // The furniture's own seat, back and arms must never veto the
                // dock: the avatar is meant to end up against, or slightly
                // inside, that surface.
                if (IsOwnFurniture(triangle, bed)) continue;
                var a = inverse * (triangle.A - center);
                var b = inverse * (triangle.B - center);
                var c = inverse * (triangle.C - center);
                if (Intersects(a, b, c, Vector3.zero, extent)) return false;
            }
            return true;
        }

        // Footprint test for the target's own shell. Includes unknown-labelled
        // triangles because a calibrated sofa is often scanned without a label.
        private static bool IsOwnFurniture(RealityTriangle triangle, BedTarget bed)
        {
            if (!Matches(triangle, bed.Semantic) &&
                !triangle.HasSemantic(PxrSemanticLabel.Unknown)) return false;
            var delta = triangle.Center - bed.Center;
            return Mathf.Abs(Vector3.Dot(delta, bed.Right)) <= bed.Width * .5f + .12f &&
                   Mathf.Abs(Vector3.Dot(delta, bed.Forward)) <= bed.Length * .5f + .12f &&
                   triangle.Center.y >= bed.FloorY - .05f &&
                   triangle.Center.y <= bed.Center.y + .60f;
        }

        // Mesh blocks that make up the target's own seat, back and arms. Planning
        // already skips them via IsOwnFurniture; the stand-to-lie transfer has to
        // use the same set or the furniture vetoes its own landing.
        public static void CollectOwnSurfaceSources(BedTarget bed,
            IReadOnlyList<RealityTriangle> triangles, ISet<int> destination)
        {
            if (destination == null) return;
            if (bed.SourceIds != null)
                foreach (var source in bed.SourceIds) destination.Add(source);
            if (triangles == null) return;
            foreach (var triangle in triangles)
                if (IsOwnFurniture(triangle, bed)) destination.Add(triangle.SourceId);
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
        // Extra stand-off tried when the closest dock is not on the navigation
        // mesh. Walking a step further out is always preferable to refusing.
        private static readonly float[] SearchOffsets = { 0f, .18f, .36f, .60f };

        public static bool TryCreate(BedTarget bed, Vector3 origin,
            INavigationService navigation, IReadOnlyList<RealityTriangle> geometry,
            out BedMountPlan plan)
        {
            plan = null;
            BedMountPlan clipped = null;
            var trace = new List<string>();
            foreach (var candidate in BedGeometry.MountCandidates(bed))
            {
                var outward = -candidate.Inward;
                foreach (var offset in SearchOffsets)
                {
                    var wanted = candidate.IdealDock + outward * offset;
                    if (!navigation.TrySample(wanted, .45f, out var dock))
                    {
                        trace.Add($"{candidate.Edge}@{offset:F2}=sample");
                        continue;
                    }
                    var drift = Vector3.ProjectOnPlane(dock - wanted, Vector3.up).magnitude;
                    if (drift > .40f || Mathf.Abs(dock.y - bed.FloorY) > .25f)
                    {
                        trace.Add($"{candidate.Edge}@{offset:F2}=drift{drift:F2}");
                        continue;
                    }
                    if (!navigation.TryPath(origin, dock, out var corners))
                    {
                        trace.Add($"{candidate.Edge}@{offset:F2}=path");
                        continue;
                    }

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
                    if (!BedGeometry.HasMountClearance(bed, geometry, candidatePlan))
                    {
                        // Remember the route anyway: a light overlap with the
                        // furniture is acceptable, an unreachable seat is not.
                        trace.Add($"{candidate.Edge}@{offset:F2}=clip");
                        if (clipped == null || candidatePlan.PathLength < clipped.PathLength)
                            clipped = candidatePlan;
                        continue;
                    }
                    if (plan == null || candidatePlan.PathLength < plan.PathLength)
                        plan = candidatePlan;
                    break;
                }
            }

            if (plan == null)
            {
                plan = clipped;
                if (plan != null)
                    Debug.Log($"MATESOUL_BED_PLAN lenient edge={plan.Edge} " +
                              $"dock={plan.Dock:F2} path={plan.PathLength:F2}");
            }
            if (plan == null)
                Debug.Log($"MATESOUL_BED_PLAN failed no reachable dock " +
                          $"bed={bed.Semantic} w={bed.Width:F2} l={bed.Length:F2} " +
                          $"center={bed.Center:F2} foot={bed.FootEdge:F2} " +
                          $"left={bed.LeftEdge:F2} right={bed.RightEdge:F2} " +
                          $"origin={origin:F2} tried=[{string.Join(" ", trace)}]");
            return plan != null;
        }
    }
}
