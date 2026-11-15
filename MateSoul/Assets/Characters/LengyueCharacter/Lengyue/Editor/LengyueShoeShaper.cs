using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MateSoul.Editor
{
    /// <summary>
    /// Slims the Lavender Casual platform shoes and seals their open soles.
    /// The source FBX is never modified; the generated runtime mesh is shaped
    /// in place so skinning, bind poses and part metadata stay untouched.
    /// </summary>
    public static class LengyueShoeShaper
    {
        public const string MeshPath = LengyueLayeredDressBuilder.Root +
            "/Runtime/Meshes/lengyue_mesh_layereddress_garments.asset";

        // Sole slab: the original platform is 28 mm thick. Keep its top and
        // raise its bottom by 8 mm so the upper and sock stay in place.
        const float SoleZ = .004f;
        const float SoleLift = .008f;
        const float SoleTaperEndZ = .040f;
        const float SoleBottomZ = SoleZ + SoleLift;
        const float SoleEdgeZ = SoleBottomZ + .002f;

        [MenuItem("MateSoul/Lengyue/Shoe/Thin And Seal Sole")]
        public static void ApplyToAsset()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (!mesh)
            {
                Debug.LogError("Missing generated shoe mesh: " + MeshPath);
                return;
            }
            if (!Apply(mesh))
            {
                Debug.Log("LENGYUE_SHOE_SKIP already sealed mesh=" +
                    mesh.name + " v=" + mesh.vertexCount +
                    " tris=" + (mesh.triangles.Length / 3));
                return;
            }
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(
                MeshPath, ImportAssetOptions.ForceUpdate);
            Validate(mesh);
            float minZ = float.MaxValue;
            foreach (Vector3 vertex in mesh.vertices)
                minZ = Mathf.Min(minZ, vertex.z);
            Debug.Log("MATESOUL_SHOE size mesh=" + mesh.name +
                " v=" + mesh.vertexCount +
                " tris=" + (mesh.triangles.Length / 3) +
                " minZ=" + minZ.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " lift=" + SoleLift.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " soleBottom=" + SoleBottomZ.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " soleEdge=" + SoleEdgeZ.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " capFaces=" + CountDownwardSoleTriangles(
                    mesh.triangles, mesh.vertices) +
                " path=" + MeshPath);
            Debug.Log("LENGYUE_SHOE_PASS " + MeshPath);
        }

        public static void ApplyBatch()
        {
            try
            {
                ApplyToAsset();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Applies the shoe shaping to an in-memory generated mesh.
        /// Returns false when the mesh was already sealed.
        /// </summary>
        public static bool Apply(Mesh mesh)
        {
            if (!mesh || mesh.subMeshCount != 1)
                throw new InvalidOperationException(
                    "The shoe shaper expects the single-submesh garment mesh.");
            int[] triangles = mesh.triangles;
            Vector3[] vertices = mesh.vertices;
            int original = vertices.Length;
            if (triangles.Length == 0 || vertices.Length == 0)
                throw new InvalidOperationException(
                    "The garment mesh is empty.");
            int existingCaps = CountDownwardSoleTriangles(
                triangles, vertices);
            if (existingCaps > 0) return false;

            Vector3[] normals = mesh.normals;
            bool hasNormals = normals != null && normals.Length == original;
            LiftSole(vertices, hasNormals ? normals : null);            var loops = BuildSoleLoops(triangles, vertices);
            if (loops.Count == 0)
                throw new InvalidOperationException(
                    "No sole boundary loops were found.");

            var extra = new List<Vector3>();
            var extraNormals = new List<Vector3>();
            var extraUv = new List<Vector2>();
            var extraUv2 = new List<Vector2>();
            var extraTangents = new List<Vector4>();
            var extraWeights = new List<BoneWeight>();
            var extraColors = new List<Color32>();
            Vector2[] uv = mesh.uv;
            Vector2[] uv2 = mesh.uv2;
            Vector4[] tangents = mesh.tangents;
            BoneWeight[] weights = mesh.boneWeights;
            Color32[] colors = mesh.colors32;
            bool hasUv = uv != null && uv.Length == original;
            bool hasUv2 = uv2 != null && uv2.Length == original;
            bool hasTangents = tangents != null && tangents.Length == original;
            bool hasWeights = weights != null && weights.Length == original;
            bool hasColors = colors != null && colors.Length == original;
            var capTriangles = new List<int>();
            foreach (var loop in loops)
                CapLoop(loop, vertices, original, extra,
                    extraNormals, extraUv, extraUv2, extraTangents,
                    extraWeights, extraColors, uv, uv2, tangents, weights,
                    colors, hasColors, capTriangles);
            if (mesh.indexFormat == IndexFormat.UInt16 &&
                original + extra.Count > 65535)
                throw new InvalidOperationException(
                    "Sole cap would exceed the 16-bit index budget.");

            Array.Resize(ref vertices, original + extra.Count);
            extra.CopyTo(vertices, original);
            if (hasNormals)
            {
                Array.Resize(ref normals, vertices.Length);
                extraNormals.CopyTo(normals, original);
            }
            if (hasUv)
            {
                Array.Resize(ref uv, vertices.Length);
                extraUv.CopyTo(uv, original);
            }
            if (hasUv2)
            {
                Array.Resize(ref uv2, vertices.Length);
                extraUv2.CopyTo(uv2, original);
            }
            if (hasTangents)
            {
                Array.Resize(ref tangents, vertices.Length);
                extraTangents.CopyTo(tangents, original);
            }
            if (hasWeights)
            {
                Array.Resize(ref weights, vertices.Length);
                extraWeights.CopyTo(weights, original);
            }
            if (hasColors)
            {
                Array.Resize(ref colors, vertices.Length);
                extraColors.CopyTo(colors, original);
            }

            int[] combined = new int[triangles.Length + capTriangles.Count];
            triangles.CopyTo(combined, 0);
            capTriangles.CopyTo(combined, triangles.Length);

            var indexFormat = mesh.indexFormat;
            Matrix4x4[] bindposes = mesh.bindposes;
            mesh.Clear();
            mesh.indexFormat = indexFormat;
            mesh.vertices = vertices;
            if (hasNormals) mesh.normals = normals;
            if (hasUv) mesh.uv = uv;
            if (hasUv2) mesh.uv2 = uv2;
            if (hasTangents) mesh.tangents = tangents;
            if (hasWeights) mesh.boneWeights = weights;
            if (hasColors) mesh.colors32 = colors;
            if (bindposes != null && bindposes.Length > 0)
                mesh.bindposes = bindposes;
            mesh.subMeshCount = 1;
            mesh.SetTriangles(combined, 0);
            mesh.RecalculateBounds();

            int capTrianglesAdded = capTriangles.Count / 3;
            int openAfter = CountDownwardSoleTriangles(
                mesh.triangles, mesh.vertices);
            if (openAfter != capTrianglesAdded)
                throw new InvalidOperationException(
                    "Sole cap validation failed: " + openAfter +
                    " downward faces, expected " + capTrianglesAdded + ".");
            int unmatched = CountUnmatchedBottomEdges(
                mesh.triangles, mesh.vertices, SoleEdgeZ);
            if (unmatched != 0)
                throw new InvalidOperationException(
                    "Sole cap left " + unmatched +
                    " unmatched rim edges.");
            return true;
        }

        static void LiftSole(Vector3[] vertices, Vector3[] normals)
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                float z = vertices[i].z;
                if (z >= SoleTaperEndZ) continue;
                float slope;
                if (z <= SoleZ)
                {
                    vertices[i].z = z + SoleLift;
                    slope = 1f;
                }
                else
                {
                    float t = (z - SoleZ) / (SoleTaperEndZ - SoleZ);
                    float smooth = t * t * (3f - 2f * t);
                    vertices[i].z = z + SoleLift * (1f - smooth);
                    slope = 1f - SoleLift * (6f * t * (1f - t) /
                        (SoleTaperEndZ - SoleZ));
                }
                if (normals == null || normals.Length != vertices.Length)
                    continue;
                Vector3 normal = normals[i];
                normals[i] = new Vector3(
                    normal.x, normal.y, normal.z / slope).normalized;
            }
        }

        sealed class SoleLoop
        {
            public readonly List<int> vertices = new List<int>();
            public readonly List<Vector3> points = new List<Vector3>();
        }

        static List<SoleLoop> BuildSoleLoops(
            int[] triangles, Vector3[] vertices)
        {
            var edges = new Dictionary<long, int>();
            AddEdges(triangles, edges);
            var weld = new Dictionary<Vector3Int, int>();
            var nodeVertices = new List<int>();
            var nodePoints = new List<Vector3>();
            var adjacency = new Dictionary<int, List<int>>();
            foreach (var pair in edges)
            {
                if (pair.Value != 1) continue;
                int a = (int)(pair.Key >> 32);
                int b = (int)(pair.Key & 0xffffffff);
                if (vertices[a].z > SoleEdgeZ ||
                    vertices[b].z > SoleEdgeZ) continue;
                int na = Weld(a, vertices, weld, nodeVertices, nodePoints);
                int nb = Weld(b, vertices, weld, nodeVertices, nodePoints);
                if (na == nb) continue;
                AddNeighbour(adjacency, na, nb);
                AddNeighbour(adjacency, nb, na);
            }
            var loops = new List<SoleLoop>();
            var used = new HashSet<long>();
            foreach (int start in adjacency.Keys)
            {
                foreach (int first in adjacency[start])
                {
                    if (used.Contains(EdgeKey(start, first))) continue;
                    var loop = Trace(start, adjacency, used);
                    if (loop.Count < 3) continue;
                    var shape = new SoleLoop();
                    foreach (int node in loop)
                    {
                        shape.vertices.Add(nodeVertices[node]);
                        shape.points.Add(nodePoints[node]);
                    }
                    loops.Add(shape);
                }
            }
            return loops;
        }

        static void CapLoop(
            SoleLoop loop, Vector3[] vertices, int capVertexOffset,
            List<Vector3> extra, List<Vector3> extraNormals,
            List<Vector2> extraUv, List<Vector2> extraUv2,
            List<Vector4> extraTangents, List<BoneWeight> extraWeights,
            List<Color32> extraColors,
            Vector2[] uv, Vector2[] uv2, Vector4[] tangents,
            BoneWeight[] weights, Color32[] colors, bool hasColors,
            List<int> capTriangles)
        {
            var polygon = EarClip(loop.points);
            if (polygon.Count == 0)
                throw new InvalidOperationException(
                    "Could not triangulate a sole loop.");
            var remap = new int[loop.points.Count];
            for (int i = 0; i < loop.vertices.Count; i++)
            {
                int source = loop.vertices[i];
                remap[i] = capVertexOffset + extra.Count;
                if (Mathf.Abs(loop.points[i].z - SoleBottomZ) > 1e-4f)
                    throw new InvalidOperationException(
                        "Sole cap is not on the lifted sole plane.");
                extra.Add(new Vector3(
                    loop.points[i].x, loop.points[i].y, loop.points[i].z));
                extraNormals.Add(Vector3.down);
                extraUv.Add(uv != null && uv.Length == vertices.Length ?
                    uv[source] : Vector2.zero);
                extraUv2.Add(uv2 != null && uv2.Length == vertices.Length ?
                    uv2[source] : Vector2.zero);
                extraTangents.Add(
                    tangents != null && tangents.Length == vertices.Length ?
                        tangents[source] : new Vector4(1, 0, 0, -1));
                extraWeights.Add(
                    weights != null && weights.Length == vertices.Length ?
                        weights[source] : new BoneWeight());
                if (hasColors) extraColors.Add(colors[source]);
            }
            for (int i = 0; i + 2 < polygon.Count; i += 3)
            {
                int a = polygon[i], b = polygon[i + 1], c = polygon[i + 2];
                Vector3 pa = loop.points[a], pb = loop.points[b],
                    pc = loop.points[c];
                float nz = Vector3.Cross(pb - pa, pc - pa).z;
                if (nz > 0)
                {
                    int swap = b; b = c; c = swap;
                }
                capTriangles.Add(remap[a]);
                capTriangles.Add(remap[b]);
                capTriangles.Add(remap[c]);
            }
        }

        static int Weld(
            int vertex, Vector3[] vertices,
            Dictionary<Vector3Int, int> weld,
            List<int> nodeVertices, List<Vector3> nodePoints)
        {
            Vector3 point = vertices[vertex];
            var key = new Vector3Int(
                Mathf.RoundToInt(point.x * 100000f),
                Mathf.RoundToInt(point.y * 100000f),
                Mathf.RoundToInt(point.z * 100000f));
            int node;
            if (!weld.TryGetValue(key, out node))
            {
                node = nodeVertices.Count;
                weld[key] = node;
                nodeVertices.Add(vertex);
                nodePoints.Add(point);
            }
            return node;
        }

        static void AddNeighbour(
            Dictionary<int, List<int>> adjacency, int a, int b)
        {
            List<int> list;
            if (!adjacency.TryGetValue(a, out list))
            {
                list = new List<int>();
                adjacency[a] = list;
            }
            if (!list.Contains(b)) list.Add(b);
        }

        static List<int> Trace(
            int start, Dictionary<int, List<int>> adjacency,
            HashSet<long> used)
        {
            var loop = new List<int> { start };
            int current = start;
            while (true)
            {
                int next = -1;
                foreach (int neighbour in adjacency[current])
                {
                    if (used.Contains(EdgeKey(current, neighbour)))
                        continue;
                    next = neighbour;
                    break;
                }
                if (next < 0) break;
                used.Add(EdgeKey(current, next));
                if (next == start) break;
                loop.Add(next);
                current = next;
            }
            return loop;
        }

        // Ear clipping on the footprint polygon.
        static List<int> EarClip(List<Vector3> points)
        {
            int count = points.Count;
            var polygon = new List<int>();
            for (int i = 0; i < count; i++) polygon.Add(i);
            double area = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 a = points[i], b = points[(i + 1) % count];
                area += a.x * b.y - b.x * a.y;
            }
            bool counterClockwise = area > 0;
            var result = new List<int>();
            int guard = 0;
            while (polygon.Count > 3 && guard++ < count * count)
            {
                bool clipped = false;
                for (int i = 0; i < polygon.Count; i++)
                {
                    int previousIndex =
                        polygon[(i + polygon.Count - 1) % polygon.Count];
                    int currentIndex = polygon[i];
                    int nextIndex =
                        polygon[(i + 1) % polygon.Count];
                    if (!IsConvex(points, previousIndex, currentIndex,
                        nextIndex, counterClockwise)) continue;
                    bool blocked = false;
                    foreach (int index in polygon)
                    {
                        if (index == previousIndex ||
                            index == currentIndex || index == nextIndex)
                            continue;
                        if (Inside(points, index, previousIndex,
                            currentIndex, nextIndex))
                        {
                            blocked = true;
                            break;
                        }
                    }
                    if (blocked) continue;
                    result.Add(previousIndex);
                    result.Add(currentIndex);
                    result.Add(nextIndex);
                    polygon.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) return new List<int>();
            }
            if (polygon.Count != 3) return new List<int>();
            result.Add(polygon[0]);
            result.Add(polygon[1]);
            result.Add(polygon[2]);
            return result;
        }

        static bool IsConvex(
            List<Vector3> points, int a, int b, int c, bool ccw)
        {
            Vector3 pa = points[a], pb = points[b], pc = points[c];
            float cross = (pb.x - pa.x) * (pc.y - pb.y) -
                (pb.y - pa.y) * (pc.x - pb.x);
            return ccw ? cross > 1e-10f : cross < -1e-10f;
        }

        static bool Inside(
            List<Vector3> points, int point, int a, int b, int c)
        {
            Vector3 pa = points[a], pb = points[b], pc = points[c],
                pp = points[point];
            float d1 = Sign(pp, pa, pb);
            float d2 = Sign(pp, pb, pc);
            float d3 = Sign(pp, pc, pa);
            bool negative = d1 < -1e-9f || d2 < -1e-9f || d3 < -1e-9f;
            bool positive = d1 > 1e-9f || d2 > 1e-9f || d3 > 1e-9f;
            return !(negative && positive);
        }

        static float Sign(Vector3 p, Vector3 a, Vector3 b)
        {
            return (p.x - b.x) * (a.y - b.y) -
                (a.x - b.x) * (p.y - b.y);
        }

        /// <summary>
        /// Counts downward-facing faces whose vertices all sit on the sole
        /// plane. The open source shoes have none; a sealed sole has one or
        /// more cap fans per foot.
        /// </summary>
        static int CountDownwardSoleTriangles(
            int[] triangles, Vector3[] vertices)
        {
            int count = 0;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                if (a.z > SoleEdgeZ || b.z > SoleEdgeZ ||
                    c.z > SoleEdgeZ) continue;
                if (Vector3.Cross(b - a, c - a).z < 0) count++;
            }
            return count;
        }

        /// <summary>
        /// Welds the bottom rim by position and reports edges that still have
        /// a single user. A correctly capped sole pairs every shoe rim edge
        /// with a coincident cap edge, so the result is zero.
        /// </summary>
        static int CountUnmatchedBottomEdges(
            int[] triangles, Vector3[] vertices, float maxZ)
        {
            var weld = new Dictionary<Vector3Int, int>();
            var positions = new List<Vector3>();
            var edges = new Dictionary<long, int>();
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int[] corners =
                {
                    triangles[i], triangles[i + 1], triangles[i + 2]
                };
                for (int k = 0; k < 3; k++)
                {
                    int a = corners[k], b = corners[(k + 1) % 3];
                    if (vertices[a].z > maxZ || vertices[b].z > maxZ)
                        continue;
                    AddEdge(edges,
                        WeldNode(a, vertices, weld, positions),
                        WeldNode(b, vertices, weld, positions));
                }
            }
            int count = 0;
            foreach (var pair in edges)
                if (pair.Value == 1) count++;
            return count;
        }

        static int WeldNode(
            int vertex, Vector3[] vertices,
            Dictionary<Vector3Int, int> weld, List<Vector3> positions)
        {
            Vector3 point = vertices[vertex];
            var key = new Vector3Int(
                Mathf.RoundToInt(point.x * 100000f),
                Mathf.RoundToInt(point.y * 100000f),
                Mathf.RoundToInt(point.z * 100000f));
            int node;
            if (!weld.TryGetValue(key, out node))
            {
                node = positions.Count;
                weld[key] = node;
                positions.Add(point);
            }
            return node;
        }

        static void AddEdges(
            int[] triangles, Dictionary<long, int> edges)
        {
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                AddEdge(edges, triangles[i], triangles[i + 1]);
                AddEdge(edges, triangles[i + 1], triangles[i + 2]);
                AddEdge(edges, triangles[i + 2], triangles[i]);
            }
        }

        static void AddEdge(
            Dictionary<long, int> edges, int a, int b)
        {
            long key = EdgeKey(a, b);
            int count;
            edges.TryGetValue(key, out count);
            edges[key] = count + 1;
        }

        static long EdgeKey(int a, int b)
        {
            int low = Math.Min(a, b), high = Math.Max(a, b);
            return ((long)low << 32) | (uint)high;
        }

        static void Validate(Mesh mesh)
        {
            int[] triangles = mesh.triangles;
            Vector3[] vertices = mesh.vertices;
            float minimumZ = float.MaxValue;
            foreach (Vector3 vertex in vertices)
                minimumZ = Mathf.Min(minimumZ, vertex.z);
            if (Mathf.Abs(minimumZ - SoleBottomZ) > 1e-3f)
                throw new InvalidOperationException(
                    "Shoe validation failed: sole bottom is at " +
                    minimumZ + ", expected " + SoleBottomZ + ".");
            int downward = CountDownwardSoleTriangles(
                triangles, vertices);
            if (downward < 60)
                throw new InvalidOperationException(
                    "Shoe validation failed: sole cap is missing.");
            int unmatched = CountUnmatchedBottomEdges(
                triangles, vertices, SoleEdgeZ);
            if (unmatched != 0)
                throw new InvalidOperationException(
                    "Shoe validation failed: " + unmatched +
                    " unmatched sole rim edges remain.");
        }
    }
}
