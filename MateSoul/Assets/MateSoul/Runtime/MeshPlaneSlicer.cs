using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MateSoul.Runtime
{
    public sealed class MeshSliceHalf
    {
        public Mesh Mesh { get; }
        public float Volume { get; }

        internal MeshSliceHalf(Mesh mesh)
        {
            Mesh = mesh;
            Volume = MeshPlaneSlicer.CalculateVolume(mesh);
        }
    }

    public sealed class MeshSliceResult
    {
        public MeshSliceHalf Positive { get; }
        public MeshSliceHalf Negative { get; }

        internal MeshSliceResult(MeshSliceHalf positive, MeshSliceHalf negative)
        {
            Positive = positive;
            Negative = negative;
        }
    }

    public static class MeshPlaneSlicer
    {
        private const float PlaneEpsilon = .00001f;
        private const float WeldEpsilonSquared = .00000001f;

        private struct Vertex
        {
            public Vector3 position;
            public Vector3 normal;
            public Vector4 tangent;
            public Vector2 uv0;
            public Vector2 uv1;
            public Vector2 uv2;

            public static Vertex Lerp(Vertex first, Vertex second, float amount)
            {
                var normal = Vector3.Lerp(first.normal, second.normal, amount);
                if (normal.sqrMagnitude > .000001f) normal.Normalize();
                var tangent = Vector4.Lerp(first.tangent, second.tangent, amount);
                var tangentDirection = new Vector3(tangent.x, tangent.y, tangent.z);
                if (tangentDirection.sqrMagnitude > .000001f)
                {
                    tangentDirection.Normalize();
                    tangent.x = tangentDirection.x;
                    tangent.y = tangentDirection.y;
                    tangent.z = tangentDirection.z;
                }
                return new Vertex
                {
                    position = Vector3.Lerp(first.position, second.position, amount),
                    normal = normal,
                    tangent = tangent,
                    uv0 = Vector2.Lerp(first.uv0, second.uv0, amount),
                    uv1 = Vector2.Lerp(first.uv1, second.uv1, amount),
                    uv2 = Vector2.Lerp(first.uv2, second.uv2, amount)
                };
            }
        }

        private sealed class Builder
        {
            private readonly List<Vector3> positions = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector4> tangents = new List<Vector4>();
            private readonly List<Vector2> uv0 = new List<Vector2>();
            private readonly List<Vector2> uv1 = new List<Vector2>();
            private readonly List<Vector2> uv2 = new List<Vector2>();
            private readonly List<int>[] triangles =
            {
                new List<int>(),
                new List<int>()
            };

            public int TriangleCount => (triangles[0].Count + triangles[1].Count) / 3;

            public void AddTriangle(Vertex first, Vertex second, Vertex third, int submesh)
            {
                submesh = Mathf.Clamp(submesh, 0, 1);
                var start = positions.Count;
                Add(first);
                Add(second);
                Add(third);
                triangles[submesh].Add(start);
                triangles[submesh].Add(start + 1);
                triangles[submesh].Add(start + 2);
            }

            private void Add(Vertex vertex)
            {
                positions.Add(vertex.position);
                normals.Add(vertex.normal);
                tangents.Add(vertex.tangent);
                uv0.Add(vertex.uv0);
                uv1.Add(vertex.uv1);
                uv2.Add(vertex.uv2);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                if (positions.Count > ushort.MaxValue)
                    mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(positions);
                mesh.SetNormals(normals);
                mesh.SetTangents(tangents);
                mesh.SetUVs(0, uv0);
                mesh.SetUVs(1, uv1);
                mesh.SetUVs(2, uv2);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(triangles[0], 0, false);
                mesh.SetTriangles(triangles[1], 1, false);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        public static MeshSliceResult Slice(Mesh source, Plane localPlane,
            CuttableSettings settings, string name = "Runtime Slice")
        {
            if (source == null || !source.isReadable)
                throw new ArgumentException("Runtime slicing requires a readable source mesh.");
            if (source.subMeshCount < 1 || source.subMeshCount > 2)
                throw new ArgumentException(
                    "Runtime slicing supports one exterior and one interior submesh.");
            settings.Validate();
            var normal = localPlane.normal.normalized;
            if (!PhysicsBody.IsFinite(normal) || normal.sqrMagnitude < .99f)
                throw new ArgumentException("Cut plane normal must be finite and normalized.");

            var sourceVertices = source.vertices;
            var sourceNormals = source.normals;
            var sourceTangents = source.tangents;
            var sourceUv0 = source.uv;
            var sourceUv1 = source.uv2;
            var sourceUv2 = source.uv3;
            var positive = new Builder();
            var negative = new Builder();
            var capPoints = new List<Vector3>();

            for (var submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                var indices = source.GetTriangles(submesh);
                for (var index = 0; index < indices.Length; index += 3)
                {
                    var first = Read(indices[index], sourceVertices, sourceNormals,
                        sourceTangents, sourceUv0, sourceUv1, sourceUv2);
                    var second = Read(indices[index + 1], sourceVertices, sourceNormals,
                        sourceTangents, sourceUv0, sourceUv1, sourceUv2);
                    var third = Read(indices[index + 2], sourceVertices, sourceNormals,
                        sourceTangents, sourceUv0, sourceUv1, sourceUv2);
                    var triangle = new[] { first, second, third };
                    AddClipped(positive, triangle, localPlane, true, submesh);
                    AddClipped(negative, triangle, localPlane, false, submesh);
                    CollectIntersections(triangle, localPlane, capPoints);
                }
            }

            var uniqueCap = Weld(capPoints);
            if (uniqueCap.Count < 3 || positive.TriangleCount == 0 ||
                negative.TriangleCount == 0)
                return null;

            AddCap(positive, uniqueCap, normal, false, settings);
            AddCap(negative, uniqueCap, normal, true, settings);
            var positiveMesh = positive.Build(name + " Positive");
            var negativeMesh = negative.Build(name + " Negative");
            var positiveHalf = new MeshSliceHalf(positiveMesh);
            var negativeHalf = new MeshSliceHalf(negativeMesh);
            if (positiveHalf.Volume <= .000000001f ||
                negativeHalf.Volume <= .000000001f)
            {
                UnityEngine.Object.DestroyImmediate(positiveMesh);
                UnityEngine.Object.DestroyImmediate(negativeMesh);
                return null;
            }
            return new MeshSliceResult(positiveHalf, negativeHalf);
        }

        private static Vertex Read(int index, Vector3[] positions, Vector3[] normals,
            Vector4[] tangents, Vector2[] uv0, Vector2[] uv1, Vector2[] uv2)
        {
            return new Vertex
            {
                position = positions[index],
                normal = normals.Length == positions.Length ? normals[index] : Vector3.up,
                tangent = tangents.Length == positions.Length
                    ? tangents[index]
                    : new Vector4(1, 0, 0, 1),
                uv0 = uv0.Length == positions.Length ? uv0[index] : Vector2.zero,
                uv1 = uv1.Length == positions.Length ? uv1[index] : Vector2.zero,
                uv2 = uv2.Length == positions.Length ? uv2[index] : Vector2.zero
            };
        }

        private static void AddClipped(Builder output, Vertex[] triangle, Plane plane,
            bool keepPositive, int submesh)
        {
            var input = new List<Vertex>(triangle);
            var clipped = new List<Vertex>(4);
            for (var index = 0; index < input.Count; index++)
            {
                var current = input[index];
                var next = input[(index + 1) % input.Count];
                var currentDistance = plane.GetDistanceToPoint(current.position);
                var nextDistance = plane.GetDistanceToPoint(next.position);
                var currentInside = keepPositive
                    ? currentDistance >= -PlaneEpsilon
                    : currentDistance <= PlaneEpsilon;
                var nextInside = keepPositive
                    ? nextDistance >= -PlaneEpsilon
                    : nextDistance <= PlaneEpsilon;
                if (currentInside) clipped.Add(current);
                if (currentInside == nextInside) continue;
                var denominator = currentDistance - nextDistance;
                if (Mathf.Abs(denominator) < PlaneEpsilon) continue;
                var amount = Mathf.Clamp01(currentDistance / denominator);
                clipped.Add(Vertex.Lerp(current, next, amount));
            }
            if (clipped.Count < 3) return;
            for (var index = 1; index < clipped.Count - 1; index++)
                output.AddTriangle(clipped[0], clipped[index], clipped[index + 1],
                    Mathf.Min(submesh, 1));
        }

        private static void CollectIntersections(Vertex[] triangle, Plane plane,
            List<Vector3> output)
        {
            var trianglePoints = new List<Vector3>(3);
            for (var index = 0; index < triangle.Length; index++)
            {
                var current = triangle[index];
                var next = triangle[(index + 1) % triangle.Length];
                var currentDistance = plane.GetDistanceToPoint(current.position);
                var nextDistance = plane.GetDistanceToPoint(next.position);
                if (Mathf.Abs(currentDistance) <= PlaneEpsilon)
                    AddUnique(trianglePoints, current.position);
                if (currentDistance * nextDistance >=
                    -(PlaneEpsilon * PlaneEpsilon)) continue;
                var amount = currentDistance / (currentDistance - nextDistance);
                AddUnique(trianglePoints,
                    Vector3.Lerp(current.position, next.position, amount));
            }
            if (trianglePoints.Count != 2) return;
            output.Add(trianglePoints[0]);
            output.Add(trianglePoints[1]);
        }

        private static List<Vector3> Weld(List<Vector3> points)
        {
            var result = new List<Vector3>();
            foreach (var point in points) AddUnique(result, point);
            return result;
        }

        private static void AddUnique(List<Vector3> points, Vector3 point)
        {
            foreach (var existing in points)
                if ((existing - point).sqrMagnitude <= WeldEpsilonSquared)
                    return;
            points.Add(point);
        }

        private static void AddCap(Builder output, List<Vector3> points,
            Vector3 planeNormal, bool facePlaneNormal, CuttableSettings settings)
        {
            var center = Vector3.zero;
            foreach (var point in points) center += point;
            center /= points.Count;

            var reference = Mathf.Abs(Vector3.Dot(planeNormal, Vector3.up)) < .9f
                ? Vector3.up
                : Vector3.right;
            var firstAxis = Vector3.Cross(planeNormal, reference).normalized;
            var secondAxis = Vector3.Cross(planeNormal, firstAxis).normalized;
            points.Sort((left, right) =>
            {
                var leftOffset = left - center;
                var rightOffset = right - center;
                var leftAngle = Mathf.Atan2(Vector3.Dot(leftOffset, secondAxis),
                    Vector3.Dot(leftOffset, firstAxis));
                var rightAngle = Mathf.Atan2(Vector3.Dot(rightOffset, secondAxis),
                    Vector3.Dot(rightOffset, firstAxis));
                return leftAngle.CompareTo(rightAngle);
            });

            var outward = facePlaneNormal ? planeNormal : -planeNormal;
            var tangent = new Vector4(firstAxis.x, firstAxis.y, firstAxis.z, 1);
            var alignment = Mathf.Abs(Vector3.Dot(planeNormal, settings.Axis));
            var centerVertex = CapVertex(center, outward, tangent, alignment, settings);
            for (var index = 0; index < points.Count; index++)
            {
                var current = CapVertex(points[index], outward, tangent, alignment, settings);
                var next = CapVertex(points[(index + 1) % points.Count], outward, tangent,
                    alignment, settings);
                if (facePlaneNormal)
                    output.AddTriangle(centerVertex, current, next, 1);
                else
                    output.AddTriangle(centerVertex, next, current, 1);
            }
        }

        private static Vertex CapVertex(Vector3 position, Vector3 normal, Vector4 tangent,
            float alignment, CuttableSettings settings)
        {
            return new Vertex
            {
                position = position,
                normal = normal,
                tangent = tangent,
                uv0 = settings.CrossSectionUv(position),
                uv1 = settings.LongitudinalUv(position),
                uv2 = new Vector2(alignment, 0)
            };
        }

        public static float CalculateVolume(Mesh mesh)
        {
            if (mesh == null || !mesh.isReadable) return 0;
            var vertices = mesh.vertices;
            double volume = 0;
            for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                var triangles = mesh.GetTriangles(submesh);
                for (var index = 0; index < triangles.Length; index += 3)
                {
                    var first = vertices[triangles[index]];
                    var second = vertices[triangles[index + 1]];
                    var third = vertices[triangles[index + 2]];
                    volume += Vector3.Dot(first, Vector3.Cross(second, third)) / 6d;
                }
            }
            return Mathf.Abs((float)volume);
        }
    }
}
