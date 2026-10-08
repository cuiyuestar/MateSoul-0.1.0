using System;
using System.Collections.Generic;
using UnityEngine;

namespace MateSoul.RoomNavigation
{
    public enum RoomObstacleKind
    {
        Wall,
        VirtualWall,
        Table,
        Sofa,
        Chair,
        Cabinet,
        Bed,
        Plant,
        Screen,
        Refrigerator,
        WashingMachine,
        AirConditioner,
        Lamp,
        Other
    }

    [Serializable]
    public sealed class RoomFloorRegion
    {
        public RoomFloorRegion(string id, IReadOnlyList<Vector3> vertices)
        {
            Id = id;
            Vertices = new List<Vector3>(vertices ?? Array.Empty<Vector3>());
        }

        public string Id { get; }
        public IReadOnlyList<Vector3> Vertices { get; }
        public float Area => RoomGeometry.CalculateAreaXZ(Vertices);
        public Vector3 Centroid => RoomGeometry.CalculateCentroid(Vertices);
    }

    [Serializable]
    public sealed class RoomObstacle
    {
        public RoomObstacle(
            string id,
            RoomObstacleKind kind,
            Vector3 center,
            Quaternion rotation,
            Vector3 size)
        {
            Id = id;
            Kind = kind;
            Center = center;
            Rotation = rotation;
            Size = size;
        }

        public string Id { get; }
        public RoomObstacleKind Kind { get; }
        public Vector3 Center { get; }
        public Quaternion Rotation { get; }
        public Vector3 Size { get; }
    }

    [Serializable]
    public sealed class RealityItemObservation
    {
        public RealityItemObservation(string id, RealitySemantic semantic, Vector3 center,
            Quaternion rotation, Vector3 size)
        {
            if (string.IsNullOrWhiteSpace(id) ||
                !RealitySemanticCatalog.Get(semantic).IsItem ||
                size.x <= 0 || size.y <= 0 || size.z <= 0)
                throw new ArgumentException("A reality item requires an item semantic, ID, and positive size.");
            Id = id;
            Semantic = semantic;
            Center = center;
            Rotation = rotation;
            Size = size;
        }

        public string Id { get; }
        public RealitySemantic Semantic { get; }
        public Vector3 Center { get; }
        public Quaternion Rotation { get; }
        public Vector3 Size { get; }
    }

    public sealed class RoomSurfaceMesh
    {
        public RoomSurfaceMesh(
            string id,
            IReadOnlyList<Vector3> vertices,
            IReadOnlyList<int> indices,
            int sourceId = -1)
        {
            Id = id;
            Vertices = new List<Vector3>(vertices ?? Array.Empty<Vector3>());
            Indices = new List<int>(indices ?? Array.Empty<int>());
            SourceId = sourceId;
        }

        public string Id { get; }
        public IReadOnlyList<Vector3> Vertices { get; }
        public IReadOnlyList<int> Indices { get; }
        public int SourceId { get; }
    }

    public sealed class RoomMapSnapshot
    {
        public RoomMapSnapshot(
            string id,
            string source,
            IReadOnlyList<RoomFloorRegion> floors,
            IReadOnlyList<RoomObstacle> obstacles,
            IReadOnlyList<RoomSurfaceMesh> surfaceMeshes = null,
            IReadOnlyList<RealityItemObservation> realityItems = null)
        {
            Id = id;
            Source = source;
            CapturedAtUtc = DateTime.UtcNow;
            Floors = new List<RoomFloorRegion>(floors ?? Array.Empty<RoomFloorRegion>());
            Obstacles = new List<RoomObstacle>(obstacles ?? Array.Empty<RoomObstacle>());
            SurfaceMeshes = new List<RoomSurfaceMesh>(
                surfaceMeshes ?? Array.Empty<RoomSurfaceMesh>());
            RealityItems = new List<RealityItemObservation>(
                realityItems ?? Array.Empty<RealityItemObservation>());
            Bounds = CalculateBounds(Floors, Obstacles, SurfaceMeshes, RealityItems);
        }

        public string Id { get; }
        public string Source { get; }
        public DateTime CapturedAtUtc { get; }
        public IReadOnlyList<RoomFloorRegion> Floors { get; }
        public IReadOnlyList<RoomObstacle> Obstacles { get; }
        public IReadOnlyList<RoomSurfaceMesh> SurfaceMeshes { get; }
        public IReadOnlyList<RealityItemObservation> RealityItems { get; }
        public Bounds Bounds { get; }

        public RoomFloorRegion MainFloor
        {
            get
            {
                RoomFloorRegion largest = null;
                var largestArea = 0f;
                foreach (var floor in Floors)
                {
                    if (floor.Area <= largestArea)
                    {
                        continue;
                    }

                    largest = floor;
                    largestArea = floor.Area;
                }

                return largest;
            }
        }

        public RoomMapValidationResult Validate(float minimumFloorArea = 4f)
        {
            var floor = MainFloor;
            if (floor == null)
            {
                return RoomMapValidationResult.Invalid("No floor polygon was provided.");
            }

            if (floor.Vertices.Count < 3)
            {
                return RoomMapValidationResult.Invalid("The main floor polygon has fewer than three vertices.");
            }

            if (floor.Area < minimumFloorArea)
            {
                return RoomMapValidationResult.Invalid(
                    $"The main floor area is {floor.Area:F2} m2; minimum is {minimumFloorArea:F2} m2.");
            }

            if (RoomGeometry.HasSelfIntersectionXZ(floor.Vertices))
            {
                return RoomMapValidationResult.Invalid("The main floor polygon self-intersects.");
            }

            return RoomMapValidationResult.Valid(floor.Area);
        }

        private static Bounds CalculateBounds(
            IReadOnlyList<RoomFloorRegion> floors,
            IReadOnlyList<RoomObstacle> obstacles,
            IReadOnlyList<RoomSurfaceMesh> surfaceMeshes,
            IReadOnlyList<RealityItemObservation> realityItems)
        {
            var hasPoint = false;
            var bounds = new Bounds(Vector3.zero, Vector3.zero);

            foreach (var floor in floors)
            {
                foreach (var vertex in floor.Vertices)
                {
                    if (!hasPoint)
                    {
                        bounds = new Bounds(vertex, Vector3.zero);
                        hasPoint = true;
                    }
                    else
                    {
                        bounds.Encapsulate(vertex);
                    }
                }
            }

            foreach (var obstacle in obstacles)
            {
                var half = obstacle.Size * 0.5f;
                var matrix = Matrix4x4.TRS(obstacle.Center, obstacle.Rotation, Vector3.one);
                for (var x = -1; x <= 1; x += 2)
                {
                    for (var y = -1; y <= 1; y += 2)
                    {
                        for (var z = -1; z <= 1; z += 2)
                        {
                            var corner = matrix.MultiplyPoint3x4(
                                new Vector3(half.x * x, half.y * y, half.z * z));
                            if (!hasPoint)
                            {
                                bounds = new Bounds(corner, Vector3.zero);
                                hasPoint = true;
                            }
                            else
                            {
                                bounds.Encapsulate(corner);
                            }
                        }
                    }
                }
            }

            foreach (var surface in surfaceMeshes)
            {
                foreach (var vertex in surface.Vertices)
                {
                    if (!hasPoint)
                    {
                        bounds = new Bounds(vertex, Vector3.zero);
                        hasPoint = true;
                    }
                    else
                    {
                        bounds.Encapsulate(vertex);
                    }
                }
            }

            foreach (var item in realityItems)
            {
                var itemBounds = new Bounds(item.Center, item.Size);
                if (!hasPoint)
                {
                    bounds = itemBounds;
                    hasPoint = true;
                }
                else
                {
                    bounds.Encapsulate(itemBounds);
                }
            }

            return bounds;
        }
    }

    public readonly struct RoomMapValidationResult
    {
        private RoomMapValidationResult(bool isValid, string error, float floorArea)
        {
            IsValid = isValid;
            Error = error;
            FloorArea = floorArea;
        }

        public bool IsValid { get; }
        public string Error { get; }
        public float FloorArea { get; }

        public static RoomMapValidationResult Valid(float floorArea)
        {
            return new RoomMapValidationResult(true, string.Empty, floorArea);
        }

        public static RoomMapValidationResult Invalid(string error)
        {
            return new RoomMapValidationResult(false, error, 0f);
        }
    }

    public static class RoomGeometry
    {
        private const float Epsilon = 0.0001f;

        public static float CalculateAreaXZ(IReadOnlyList<Vector3> vertices)
        {
            return Mathf.Abs(CalculateSignedAreaXZ(vertices));
        }

        public static Vector3 CalculateCentroid(IReadOnlyList<Vector3> vertices)
        {
            if (vertices == null || vertices.Count == 0)
            {
                return Vector3.zero;
            }

            var sum = Vector3.zero;
            foreach (var vertex in vertices)
            {
                sum += vertex;
            }

            return sum / vertices.Count;
        }

        public static List<Vector3> ConvexHullXZ(IReadOnlyList<Vector3> points, float y)
        {
            var sorted = new List<Vector2>();
            foreach (var point in points)
            {
                var candidate = new Vector2(point.x, point.z);
                var duplicate = false;
                foreach (var existing in sorted)
                {
                    if ((existing - candidate).sqrMagnitude <= Epsilon * Epsilon)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    sorted.Add(candidate);
                }
            }

            sorted.Sort((a, b) =>
            {
                var xComparison = a.x.CompareTo(b.x);
                return xComparison != 0 ? xComparison : a.y.CompareTo(b.y);
            });

            if (sorted.Count < 3)
            {
                return new List<Vector3>();
            }

            var lower = new List<Vector2>();
            foreach (var point in sorted)
            {
                while (lower.Count >= 2 &&
                       Cross(lower[lower.Count - 2], lower[lower.Count - 1], point) <= 0f)
                {
                    lower.RemoveAt(lower.Count - 1);
                }

                lower.Add(point);
            }

            var upper = new List<Vector2>();
            for (var i = sorted.Count - 1; i >= 0; i--)
            {
                var point = sorted[i];
                while (upper.Count >= 2 &&
                       Cross(upper[upper.Count - 2], upper[upper.Count - 1], point) <= 0f)
                {
                    upper.RemoveAt(upper.Count - 1);
                }

                upper.Add(point);
            }

            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);

            var result = new List<Vector3>(lower.Count);
            foreach (var point in lower)
            {
                result.Add(new Vector3(point.x, y, point.y));
            }

            return result;
        }

        public static Mesh CreateHorizontalPolygonMesh(
            IReadOnlyList<Vector3> vertices,
            string meshName)
        {
            const float thickness = 0.08f;
            var topTriangles = TriangulateXZ(vertices);
            var meshVertices = new Vector3[vertices.Count * 2];
            for (var i = 0; i < vertices.Count; i++)
            {
                meshVertices[i] = vertices[i];
                meshVertices[i + vertices.Count] = vertices[i] - Vector3.up * thickness;
            }

            var triangles = new List<int>(topTriangles.Length * 2 + vertices.Count * 6);
            triangles.AddRange(topTriangles);
            for (var i = 0; i < topTriangles.Length; i += 3)
            {
                triangles.Add(topTriangles[i] + vertices.Count);
                triangles.Add(topTriangles[i + 2] + vertices.Count);
                triangles.Add(topTriangles[i + 1] + vertices.Count);
            }

            for (var i = 0; i < vertices.Count; i++)
            {
                var next = (i + 1) % vertices.Count;
                var bottom = i + vertices.Count;
                var bottomNext = next + vertices.Count;
                triangles.Add(i);
                triangles.Add(bottom);
                triangles.Add(bottomNext);
                triangles.Add(i);
                triangles.Add(bottomNext);
                triangles.Add(next);
            }

            var mesh = new Mesh
            {
                name = meshName,
                vertices = meshVertices,
                triangles = triangles.ToArray()
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static bool HasSelfIntersectionXZ(IReadOnlyList<Vector3> vertices)
        {
            if (vertices == null || vertices.Count < 4)
            {
                return false;
            }

            for (var i = 0; i < vertices.Count; i++)
            {
                var a1 = ToXZ(vertices[i]);
                var a2 = ToXZ(vertices[(i + 1) % vertices.Count]);
                for (var j = i + 1; j < vertices.Count; j++)
                {
                    if (j == i || (j + 1) % vertices.Count == i || (i + 1) % vertices.Count == j)
                    {
                        continue;
                    }

                    var b1 = ToXZ(vertices[j]);
                    var b2 = ToXZ(vertices[(j + 1) % vertices.Count]);
                    if (SegmentsIntersect(a1, a2, b1, b2))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool ContainsPointXZ(IReadOnlyList<Vector3> polygon, Vector3 point)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return false;
            }

            var inside = false;
            var point2D = ToXZ(point);
            for (int i = 0, previous = polygon.Count - 1; i < polygon.Count; previous = i++)
            {
                var currentPoint = ToXZ(polygon[i]);
                var previousPoint = ToXZ(polygon[previous]);
                if (DistanceToSegmentSquared(point2D, previousPoint, currentPoint) <=
                    Epsilon * Epsilon)
                {
                    return true;
                }

                var crossesRay =
                    (currentPoint.y > point2D.y) != (previousPoint.y > point2D.y) &&
                    point2D.x <
                    (previousPoint.x - currentPoint.x) *
                    (point2D.y - currentPoint.y) /
                    (previousPoint.y - currentPoint.y) +
                    currentPoint.x;
                if (crossesRay)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        public static List<Vector3> CreateObstacleFootprintXZ(RoomObstacle obstacle)
        {
            var points = new List<Vector3>(8);
            var half = obstacle.Size * 0.5f;
            var matrix = Matrix4x4.TRS(obstacle.Center, obstacle.Rotation, Vector3.one);
            for (var x = -1; x <= 1; x += 2)
            {
                for (var y = -1; y <= 1; y += 2)
                {
                    for (var z = -1; z <= 1; z += 2)
                    {
                        points.Add(matrix.MultiplyPoint3x4(
                            new Vector3(half.x * x, half.y * y, half.z * z)));
                    }
                }
            }

            return ConvexHullXZ(points, obstacle.Center.y);
        }

        private static int[] TriangulateXZ(IReadOnlyList<Vector3> vertices)
        {
            if (vertices == null || vertices.Count < 3)
            {
                throw new ArgumentException("A floor polygon needs at least three vertices.", nameof(vertices));
            }

            var remaining = new List<int>(vertices.Count);
            var isCounterClockwise = CalculateSignedAreaXZ(vertices) > 0f;
            if (isCounterClockwise)
            {
                for (var i = 0; i < vertices.Count; i++)
                {
                    remaining.Add(i);
                }
            }
            else
            {
                for (var i = vertices.Count - 1; i >= 0; i--)
                {
                    remaining.Add(i);
                }
            }

            var triangles = new List<int>((vertices.Count - 2) * 3);
            var guard = vertices.Count * vertices.Count;
            while (remaining.Count > 2 && guard-- > 0)
            {
                var earFound = false;
                for (var i = 0; i < remaining.Count; i++)
                {
                    var previous = remaining[(i - 1 + remaining.Count) % remaining.Count];
                    var current = remaining[i];
                    var next = remaining[(i + 1) % remaining.Count];

                    var a = ToXZ(vertices[previous]);
                    var b = ToXZ(vertices[current]);
                    var c = ToXZ(vertices[next]);
                    if (Cross(a, b, c) <= Epsilon || ContainsAnyPoint(a, b, c, vertices, remaining, previous, current, next))
                    {
                        continue;
                    }

                    AddUpwardFacingTriangle(triangles, vertices, previous, current, next);
                    remaining.RemoveAt(i);
                    earFound = true;
                    break;
                }

                if (!earFound)
                {
                    throw new InvalidOperationException("The floor polygon could not be triangulated.");
                }
            }

            return triangles.ToArray();
        }

        private static bool ContainsAnyPoint(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            IReadOnlyList<Vector3> vertices,
            IReadOnlyList<int> remaining,
            int previous,
            int current,
            int next)
        {
            foreach (var index in remaining)
            {
                if (index == previous || index == current || index == next)
                {
                    continue;
                }

                if (PointInTriangle(ToXZ(vertices[index]), a, b, c))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddUpwardFacingTriangle(
            ICollection<int> triangles,
            IReadOnlyList<Vector3> vertices,
            int a,
            int b,
            int c)
        {
            var normalY = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).y;
            triangles.Add(a);
            if (normalY >= 0f)
            {
                triangles.Add(b);
                triangles.Add(c);
            }
            else
            {
                triangles.Add(c);
                triangles.Add(b);
            }
        }

        private static float CalculateSignedAreaXZ(IReadOnlyList<Vector3> vertices)
        {
            if (vertices == null || vertices.Count < 3)
            {
                return 0f;
            }

            var twiceArea = 0f;
            for (var i = 0; i < vertices.Count; i++)
            {
                var current = vertices[i];
                var next = vertices[(i + 1) % vertices.Count];
                twiceArea += current.x * next.z - next.x * current.z;
            }

            return twiceArea * 0.5f;
        }

        private static Vector2 ToXZ(Vector3 point)
        {
            return new Vector2(point.x, point.z);
        }

        private static float Cross(Vector2 origin, Vector2 a, Vector2 b)
        {
            return (a.x - origin.x) * (b.y - origin.y) -
                   (a.y - origin.y) * (b.x - origin.x);
        }

        private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            var c1 = Cross(a, b, point);
            var c2 = Cross(b, c, point);
            var c3 = Cross(c, a, point);
            return c1 >= -Epsilon && c2 >= -Epsilon && c3 >= -Epsilon;
        }

        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            var abC = Cross(a, b, c);
            var abD = Cross(a, b, d);
            var cdA = Cross(c, d, a);
            var cdB = Cross(c, d, b);
            return abC * abD < -Epsilon && cdA * cdB < -Epsilon;
        }

        private static float DistanceToSegmentSquared(Vector2 point, Vector2 start, Vector2 end)
        {
            var segment = end - start;
            var lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Epsilon * Epsilon)
            {
                return (point - start).sqrMagnitude;
            }

            var t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            var projection = start + segment * t;
            return (point - projection).sqrMagnitude;
        }
    }
}
