using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace MateSoul.Hair
{
    /// <summary>
    /// Surface contact for the generated Lengyue bob (Z up, -Y toward face).
    /// Runs after the spring solver without changing any bone or spring state.
    /// A private mesh keeps the imported asset and its rest shape intact.
    /// </summary>
    [DefaultExecutionOrder(11500)]
    public sealed class LengyueHairSurfaceContact : MonoBehaviour
    {
        public SkinnedMeshRenderer target;
        [Min(0)] public float clearance = .0008f;
        public int BoundFineStrands { get; private set; }
        public int BoundFineVertices { get; private set; }
        public int ContactVertices { get; private set; }
        public float MaximumCorrection { get; private set; }
        public double LastUpdateMilliseconds { get; private set; }
        public Vector3[] RestVertices => rest;
        public float[] DepthOffsets => depths;

        const float CellSize = .006f;
        sealed class Row
        {
            public readonly List<int> vertices = new List<int>();
            public Vector3 center;
            public float uv, depth;
        }
        sealed class Strand
        {
            public string name;
            public bool fine, fringe;
            public readonly List<Row> rows = new List<Row>();
            public readonly List<int> triangles = new List<int>();
        }
        struct CapTriangle
        {
            public Vector3 a, b, c, min, max;
            public float determinant;
        }

        Mesh source, instance;
        Vector3[] rest, output, posed;
        float[] depths;
        BoneWeight[] weights;
        Matrix4x4[] bind, skin, blended, inverseBlended;
        BoneWeight[] contactWeights;
        int[] blendIndices;
        Transform[] bones;
        int[] triangles;
        int rootBone;
        Row[] vertexRows;
        Strand[] vertexStrands;
        readonly List<Strand> strands = new List<Strand>();
        readonly List<int> contact = new List<int>();
        readonly List<CapTriangle> cap = new List<CapTriangle>();
        readonly Dictionary<Vector2Int, List<int>> cells =
            new Dictionary<Vector2Int, List<int>>();

        void Start() => Initialize();
        void OnEnable()
        {
            if (instance && target) target.sharedMesh = instance;
        }
        void OnDisable()
        {
            if (source && target && target.sharedMesh == instance)
                target.sharedMesh = source;
        }
        void OnDestroy()
        {
            if (!instance) return;
            if (Application.isPlaying) Destroy(instance);
            else DestroyImmediate(instance);
        }
        void LateUpdate() => Solve();

        public void Initialize()
        {
            if (instance) return;
            if (!target || !target.sharedMesh)
                throw new InvalidOperationException("Hair contact requires a skinned hair mesh");
            source = target.sharedMesh;
            rest = source.vertices;
            output = (Vector3[])rest.Clone();
            posed = new Vector3[rest.Length];
            depths = new float[rest.Length];
            weights = source.boneWeights;
            bind = source.bindposes;
            bones = target.bones;
            triangles = source.triangles;
            rootBone = Array.FindIndex(bones, b => b.name == "HairRoot");
            if (rootBone < 0 || source.blendShapeCount != 0)
                throw new InvalidOperationException("Unexpected Lengyue hair topology");
            skin = new Matrix4x4[bones.Length];
            vertexRows = new Row[rest.Length];
            vertexStrands = new Strand[rest.Length];
            BuildStrands(source.uv);
            BindFineStrands();
            CacheContactWeights();
            BuildCap();
            instance = Instantiate(source);
            instance.name = source.name + " (surface contact)";
            instance.MarkDynamic();
            instance.boneWeights = weights;
            target.sharedMesh = instance;
        }

        static bool Fine(string name) =>
            name.StartsWith("H_Detail_", StringComparison.Ordinal) ||
            name.StartsWith("H_FringeFine_", StringComparison.Ordinal);

        void BuildStrands(Vector2[] uv)
        {
            // Root-pinned vertices still belong to their moving strip. Resolve
            // connected parts across UV seams before classifying by bone names.
            var parent = new int[rest.Length];
            var welded = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < rest.Length; i++)
            {
                parent[i] = i;
                var key = new Vector3Int(Mathf.RoundToInt(rest[i].x * 1e7f),
                    Mathf.RoundToInt(rest[i].y * 1e7f), Mathf.RoundToInt(rest[i].z * 1e7f));
                if (welded.TryGetValue(key, out int other)) Union(i, other);
                else welded.Add(key, i);
            }
            for (int t = 0; t < triangles.Length; t += 3)
            {
                Union(triangles[t], triangles[t + 1]);
                Union(triangles[t], triangles[t + 2]);
            }
            var names = new Dictionary<int, string>();
            for (int i = 0; i < rest.Length; i++)
            {
                string bone = bones[weights[i].boneIndex0].name;
                if (bone != "HairRoot")
                    names[Root(i)] = bone.Substring(0, bone.LastIndexOf('_'));
            }
            var byName = new Dictionary<string, Strand>();
            for (int i = 0; i < rest.Length; i++)
            {
                if (!names.TryGetValue(Root(i), out string name)) continue;
                if (!byName.TryGetValue(name, out Strand strand))
                {
                    strand = new Strand {
                        name = name, fine = Fine(name),
                        fringe = name.StartsWith("H_Fringe", StringComparison.Ordinal)
                    };
                    byName.Add(name, strand);
                    strands.Add(strand);
                }
                Row row = strand.rows.Find(r => Mathf.Abs(r.uv - uv[i].y) < .00001f);
                if (row == null)
                {
                    row = new Row { uv = uv[i].y };
                    strand.rows.Add(row);
                }
                row.vertices.Add(i);
                row.center += rest[i];
                vertexRows[i] = row;
                vertexStrands[i] = strand;
                if (strand.fringe) contact.Add(i);
            }
            foreach (Strand strand in strands)
            {
                strand.rows.Sort((a, b) => b.uv.CompareTo(a.uv));
                foreach (Row row in strand.rows) row.center /= row.vertices.Count;
            }
            for (int t = 0; t < triangles.Length; t += 3)
            {
                Strand strand = vertexStrands[triangles[t]];
                if (strand != null) strand.triangles.Add(t);
            }
            int Root(int vertex)
            {
                while (parent[vertex] != vertex)
                {
                    parent[vertex] = parent[parent[vertex]];
                    vertex = parent[vertex];
                }
                return vertex;
            }
            void Union(int a, int b) => parent[Root(a)] = Root(b);
        }

        void BindFineStrands()
        {
            foreach (Strand fine in strands)
            {
                if (!fine.fine) continue;
                // Pick a single neighboring lock for the whole fine strand.
                // All vertices of a cross section share weights to avoid twisting.
                Strand neighbor = null;
                float best = float.PositiveInfinity;
                foreach (Strand candidate in strands)
                {
                    if (candidate.fine || candidate.fringe != fine.fringe) continue;
                    float distance = 0;
                    foreach (Row row in fine.rows)
                        distance += Closest(candidate, row.center, out _, out _);
                    if (distance < best) { best = distance; neighbor = candidate; }
                }
                if (neighbor == null)
                    throw new InvalidOperationException("No guide strand for " + fine.name);
                foreach (Row row in fine.rows)
                {
                    Closest(neighbor, row.center, out int t, out Vector3 bary);
                    BoneWeight weight = Interpolate(
                        weights[triangles[t]], weights[triangles[t + 1]],
                        weights[triangles[t + 2]], bary);
                    foreach (int i in row.vertices)
                    {
                        weights[i] = weight;
                        BoundFineVertices++;
                    }
                }
                BoundFineStrands++;
            }
        }

        float Closest(Strand strand, Vector3 point, out int triangle, out Vector3 bary)
        {
            float best = float.PositiveInfinity;
            triangle = -1;
            bary = Vector3.zero;
            foreach (int t in strand.triangles)
            {
                Vector3 a = rest[triangles[t]], b = rest[triangles[t + 1]],
                    c = rest[triangles[t + 2]];
                Vector3 candidate = ClosestBarycentric(point, a, b, c);
                float distance = (a * candidate.x + b * candidate.y +
                    c * candidate.z - point).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                triangle = t;
                bary = candidate;
            }
            return best;
        }

        static Vector3 ClosestBarycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return new Vector3(1, 0, 0);
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return new Vector3(0, 1, 0);
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0)
            {
                float v = d1 / (d1 - d3);
                return new Vector3(1 - v, v, 0);
            }
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return new Vector3(0, 0, 1);
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0)
            {
                float w = d2 / (d2 - d6);
                return new Vector3(1 - w, 0, w);
            }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 >= d3 && d5 >= d6)
            {
                float w = (d4 - d3) / (d4 - d3 + d5 - d6);
                return new Vector3(0, 1 - w, w);
            }
            float sum = va + vb + vc;
            return sum > 1e-20f ? new Vector3(va, vb, vc) / sum : Vector3.right;
        }

        static BoneWeight Interpolate(BoneWeight a, BoneWeight b, BoneWeight c, Vector3 bary)
        {
            var values = new Dictionary<int, float>();
            Add(a, bary.x); Add(b, bary.y); Add(c, bary.z);
            var sorted = new List<KeyValuePair<int, float>>(values);
            sorted.Sort((x, y) => y.Value.CompareTo(x.Value));
            while (sorted.Count < 4) sorted.Add(new KeyValuePair<int, float>(0, 0));
            float sum = sorted[0].Value + sorted[1].Value + sorted[2].Value + sorted[3].Value;
            return new BoneWeight {
                boneIndex0 = sorted[0].Key, weight0 = sorted[0].Value / sum,
                boneIndex1 = sorted[1].Key, weight1 = sorted[1].Value / sum,
                boneIndex2 = sorted[2].Key, weight2 = sorted[2].Value / sum,
                boneIndex3 = sorted[3].Key, weight3 = sorted[3].Value / sum
            };
            void Add(BoneWeight weight, float scale)
            {
                Accumulate(weight.boneIndex0, weight.weight0 * scale);
                Accumulate(weight.boneIndex1, weight.weight1 * scale);
                Accumulate(weight.boneIndex2, weight.weight2 * scale);
                Accumulate(weight.boneIndex3, weight.weight3 * scale);
            }
            void Accumulate(int index, float value)
            {
                if (value <= 0) return;
                values.TryGetValue(index, out float old);
                values[index] = old + value;
            }
        }

        static Vector2Int Cell(Vector3 p) => new Vector2Int(
            Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));

        void CacheContactWeights()
        {
            var unique = new Dictionary<BoneWeight, int>();
            var ordered = new List<BoneWeight>();
            blendIndices = new int[rest.Length];
            foreach (int i in contact)
            {
                if (!unique.TryGetValue(weights[i], out int index))
                {
                    index = ordered.Count;
                    unique.Add(weights[i], index);
                    ordered.Add(weights[i]);
                }
                blendIndices[i] = index;
            }
            contactWeights = ordered.ToArray();
            blended = new Matrix4x4[ordered.Count];
            inverseBlended = new Matrix4x4[ordered.Count];
        }

        void BuildCap()
        {
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int ia = triangles[t], ib = triangles[t + 1], ic = triangles[t + 2];
                if (vertexStrands[ia] != null || vertexStrands[ib] != null ||
                    vertexStrands[ic] != null) continue;
                Vector3 a = rest[ia], b = rest[ib], c = rest[ic];
                float determinant = (b.x - a.x) * (c.z - a.z) -
                    (b.z - a.z) * (c.x - a.x);
                if (Mathf.Abs(determinant) < 1e-12f) continue;
                int index = cap.Count;
                cap.Add(new CapTriangle {
                    a = a, b = b, c = c, determinant = determinant,
                    min = Vector3.Min(a, Vector3.Min(b, c)),
                    max = Vector3.Max(a, Vector3.Max(b, c))
                });
                Vector2Int min = Cell(Vector3.Min(a, Vector3.Min(b, c)));
                Vector2Int max = Cell(Vector3.Max(a, Vector3.Max(b, c)));
                for (int x = min.x; x <= max.x; x++)
                    for (int z = min.y; z <= max.y; z++)
                    {
                        var key = new Vector2Int(x, z);
                        if (!cells.TryGetValue(key, out List<int> list))
                            cells.Add(key, list = new List<int>());
                        list.Add(index);
                    }
            }
            if (cap.Count == 0) throw new InvalidOperationException("Hair cap surface is missing");
        }

        float RequiredDepth(Vector3 point)
        {
            if (!cells.TryGetValue(Cell(point), out List<int> list)) return 0;
            float front = float.PositiveInfinity;
            foreach (int index in list)
            {
                CapTriangle t = cap[index];
                // Most bucket candidates lie behind or beside this sample.
                // Reject by conservative bounds before ray/triangle arithmetic.
                if (point.y + clearance < t.min.y || t.min.y > front ||
                    point.x < t.min.x - .000001f || point.x > t.max.x + .000001f ||
                    point.z < t.min.z - .000001f || point.z > t.max.z + .000001f)
                    continue;
                float x = point.x - t.a.x, z = point.z - t.a.z;
                float u = (x * (t.c.z - t.a.z) - z * (t.c.x - t.a.x)) / t.determinant;
                float v = ((t.b.x - t.a.x) * z - (t.b.z - t.a.z) * x) / t.determinant;
                if (u < -.00001f || v < -.00001f || u + v > 1.00001f) continue;
                front = Mathf.Min(front, t.a.y + (t.b.y - t.a.y) * u + (t.c.y - t.a.y) * v);
            }
            return Mathf.Max(0, point.y - front + clearance);
        }

        public void Solve()
        {
            Initialize();
            long start = Stopwatch.GetTimestamp();
            Matrix4x4 worldToRoot =
                (bones[rootBone].localToWorldMatrix * bind[rootBone]).inverse;
            for (int b = 0; b < bones.Length; b++)
                skin[b] = worldToRoot * bones[b].localToWorldMatrix * bind[b];
            foreach (Strand strand in strands)
                foreach (Row row in strand.rows) row.depth = 0;
            // Cross sections share skin weights, so blend/invert once per
            // distinct weight set instead of once per seam-duplicated vertex.
            for (int i = 0; i < contactWeights.Length; i++)
            {
                BoneWeight w = contactWeights[i];
                Matrix4x4 matrix = new Matrix4x4();
                for (int column = 0; column < 4; column++)
                    matrix.SetColumn(column,
                        skin[w.boneIndex0].GetColumn(column) * w.weight0 +
                        skin[w.boneIndex1].GetColumn(column) * w.weight1 +
                        skin[w.boneIndex2].GetColumn(column) * w.weight2 +
                        skin[w.boneIndex3].GetColumn(column) * w.weight3);
                blended[i] = matrix;
                inverseBlended[i] = matrix.inverse;
            }
            foreach (int i in contact)
            {
                posed[i] = blended[blendIndices[i]].MultiplyPoint3x4(rest[i]);
                vertexRows[i].depth = Mathf.Max(vertexRows[i].depth, RequiredDepth(posed[i]));
            }
            // Include triangle interiors/edges: endpoint-only colliders miss broad strips.
            foreach (Strand strand in strands)
            {
                if (!strand.fringe) continue;
                foreach (int t in strand.triangles)
                {
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    float depth = RequiredDepth((posed[a] + posed[b] + posed[c]) / 3);
                    depth = Mathf.Max(depth, RequiredDepth((posed[a] + posed[b]) * .5f));
                    depth = Mathf.Max(depth, RequiredDepth((posed[b] + posed[c]) * .5f));
                    depth = Mathf.Max(depth, RequiredDepth((posed[c] + posed[a]) * .5f));
                    vertexRows[a].depth = Mathf.Max(vertexRows[a].depth, depth);
                    vertexRows[b].depth = Mathf.Max(vertexRows[b].depth, depth);
                    vertexRows[c].depth = Mathf.Max(vertexRows[c].depth, depth);
                }
                // Extend separation smoothly along the strand; never average it down.
                for (int r = 1; r < strand.rows.Count; r++)
                    strand.rows[r].depth = Mathf.Max(strand.rows[r].depth, strand.rows[r - 1].depth * .65f);
                for (int r = strand.rows.Count - 2; r >= 0; r--)
                    strand.rows[r].depth = Mathf.Max(strand.rows[r].depth, strand.rows[r + 1].depth * .65f);
            }
            ContactVertices = 0;
            MaximumCorrection = 0;
            foreach (int i in contact)
            {
                float depth = vertexRows[i].depth;
                depths[i] = depth;
                if (depth > .00001f) ContactVertices++;
                MaximumCorrection = Mathf.Max(MaximumCorrection, depth);
                // Correct source -Y only. Lateral/up trajectories are exactly preserved.
                output[i] = rest[i] + inverseBlended[blendIndices[i]].MultiplyVector(new Vector3(0, -depth, 0));
            }
            instance.vertices = output;
            instance.RecalculateBounds();
            LastUpdateMilliseconds =
                (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        }
    }
}
