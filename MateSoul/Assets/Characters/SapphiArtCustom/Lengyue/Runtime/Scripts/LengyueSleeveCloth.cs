using System;
using System.Collections.Generic;
using UnityEngine;

namespace MateSoul.Cloth
{
    /// <summary>
    /// A small world-space cloth cage drives the shirt and its cuff together.
    /// The imported mesh, skeleton and animation remain immutable.
    /// </summary>
    [DefaultExecutionOrder(12500)]
    public sealed class LengyueSleeveCloth : MonoBehaviour
    {
        public SkinnedMeshRenderer target;
        [Range(0, .4f)] public float radialEase = .22f;
        [Range(0, 1)] public float gravityScale = .65f;
        [Range(1, 6)] public float hemFrequency = 2.4f;
        [Range(.1f, 1)] public float dampingRatio = .38f;
        public bool IsInitialized => runtimeMesh != null;
        public Mesh SourceMesh => originalMesh;
        public int DrivenVertexCount => bindings.Count;
        public int SkinInfluences { get; private set; }
        public Vector3[] ParticlePositions => positions;
        public Vector3[] AnimatedPositions => animated;
        public float MaximumOffset { get; private set; }
        public float MinimumClearance { get; private set; }
        public int ContactCount { get; private set; }
        public double LastUpdateMilliseconds { get; private set; }

        const int Rings = 6, Sectors = 20, SideCount = Rings * Sectors;
        const float StartX = .125f, EndX = .256f;
        const float CenterY = .005f, CenterZ = 1.355f;
        const float Thickness = .002f;

        struct Binding
        {
            public int vertex, a, b, c, d, skinIndex;
            public float u, v, freedom;
        }

        struct Edge
        {
            public int a, b;
            public float strength;
        }

        struct Capsule
        {
            public Vector3 a, b;
            public float ra, rb;
        }

        Mesh originalMesh, runtimeMesh;
        Vector3[] rest, output, restNormals, normals, restSurfaceNormals, surfaceNormals;
        Vector3[] cageRest, insetRest, positions, previous, animated, aims, lastAims;
        float[] freedom;
        BoneWeight[] vertexWeights, cageWeights;
        Matrix4x4[] bindposes, skin;
        Matrix4x4[] mixedSkin, inverseSkin;
        readonly List<BoneWeight> uniqueWeights = new List<BoneWeight>();
        Transform[] bones;
        readonly List<Binding> bindings = new List<Binding>();
        readonly List<Edge> edges = new List<Edge>();
        readonly List<int> clothTriangles = new List<int>();
        readonly List<int> normalVertices = new List<int>();
        readonly Capsule[] capsules = new Capsule[5];
        readonly int[] upper = new int[2], elbow = new int[2], hand = new int[2];
        int chest;
        bool reset = true;
        float previousStep;

        void Start() => Initialize();

        public void ConfigureForOutfit(
            SkinnedMeshRenderer garment,
            bool simulateSleeves)
        {
            if (!simulateSleeves)
            {
                enabled = false;
                DisposeSimulation();
                target = null;
                return;
            }
            if (!garment)
                throw new InvalidOperationException(
                    "Sleeve cloth outfit has no garment renderer");
            if (target == garment && IsInitialized)
            {
                enabled = true;
                ResetSimulation();
                return;
            }
            DisposeSimulation();
            target = garment;
            enabled = true;
            Initialize();
        }

        public void DisposeSimulation()
        {
            if (target && runtimeMesh &&
                target.sharedMesh == runtimeMesh)
                target.sharedMesh = originalMesh;
            Mesh obsolete = runtimeMesh;
            runtimeMesh = null;
            originalMesh = null;
            rest = output = restNormals = normals = null;
            restSurfaceNormals = surfaceNormals = null;
            cageRest = insetRest = positions = previous = null;
            animated = aims = lastAims = null;
            freedom = null;
            vertexWeights = cageWeights = null;
            bindposes = skin = mixedSkin = inverseSkin = null;
            bones = null;
            bindings.Clear();
            edges.Clear();
            clothTriangles.Clear();
            normalVertices.Clear();
            uniqueWeights.Clear();
            SkinInfluences = 0;
            MaximumOffset = 0;
            MinimumClearance = 0;
            ContactCount = 0;
            LastUpdateMilliseconds = 0;
            reset = true;
            previousStep = 0;
            if (obsolete)
            {
                if (Application.isPlaying) Destroy(obsolete);
                else DestroyImmediate(obsolete);
            }
        }

        public void Initialize()
        {
            if (IsInitialized) return;
            if (!target)
                foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (renderer.name == "LavenderCasual_Garments") target = renderer;
            if (!target) throw new InvalidOperationException("Sleeve garment renderer missing");
            originalMesh = target.sharedMesh;
            rest = originalMesh.vertices;
            output = (Vector3[])rest.Clone();
            restNormals = originalMesh.normals;
            normals = (Vector3[])restNormals.Clone();
            vertexWeights = originalMesh.boneWeights;
            SkinInfluences = Mathf.Min(4, (int)QualitySettings.skinWeights,
                target.quality == SkinQuality.Auto ? 4 : (int)target.quality);
            // Match the renderer's active weight limit before computing either
            // the animated target or inverse skinning. Do not change quality
            // on the shared garment: that would also alter the accepted skirt.
            for (int i = 0; i < vertexWeights.Length; i++)
            {
                BoneWeight w = vertexWeights[i];
                if (SkinInfluences < 4) w.weight2 = w.weight3 = 0;
                if (SkinInfluences == 1) w.weight1 = 0;
                float sum = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                w.weight0 /= sum; w.weight1 /= sum;
                w.weight2 /= sum; w.weight3 /= sum;
                vertexWeights[i] = w;
            }
            bindposes = originalMesh.bindposes;
            bones = target.bones;
            skin = new Matrix4x4[bones.Length];
            for (int side = 0; side < 2; side++)
            {
                string suffix = side == 0 ? "_L" : "_R";
                upper[side] = FindBone("UpperArm" + suffix);
                elbow[side] = FindBone("Forearm" + suffix);
                hand[side] = FindBone("Hand" + suffix);
            }
            // Imported bone suffixes have negative/positive X respectively.
            if (bindposes[upper[0]].inverse.m03 > 0)
            {
                Array.Reverse(upper);
                Array.Reverse(elbow);
                Array.Reverse(hand);
            }
            chest = FindBone("Chest");
            var parts = originalMesh.uv2;
            var candidates = new List<int>();
            for (int i = 0; i < rest.Length; i++)
            {
                int part = Mathf.RoundToInt(parts[i].x);
                if ((part == 3 || part == 5) && Mathf.Abs(rest[i].x) >= StartX)
                    candidates.Add(i);
            }
            int count = 2 * SideCount;
            cageRest = new Vector3[count];
            insetRest = new Vector3[count];
            positions = new Vector3[count];
            previous = new Vector3[count];
            animated = new Vector3[count];
            aims = new Vector3[count];
            lastAims = new Vector3[count];
            cageWeights = new BoneWeight[count];
            freedom = new float[count];
            for (int side = 0; side < 2; side++)
                for (int ring = 0; ring < Rings; ring++)
                    for (int sector = 0; sector < Sectors; sector++)
                    {
                        int n = Node(side, ring, sector);
                        float x = Mathf.Lerp(StartX, EndX, ring / (float)(Rings - 1));
                        float angle = sector * 2 * Mathf.PI / Sectors;
                        Vector3 radial = new Vector3(0, Mathf.Cos(angle), Mathf.Sin(angle));
                        Vector3 center = new Vector3(side == 0 ? -x : x, CenterY, CenterZ);
                        Vector3 sample = center + radial * .052f;
                        int nearest = candidates[0];
                        float best = float.MaxValue;
                        foreach (int id in candidates)
                        {
                            float distance = (rest[id] - sample).sqrMagnitude;
                            if (distance < best) { best = distance; nearest = id; }
                        }
                        float radius = Vector3.Dot(rest[nearest] - center, radial);
                        cageRest[n] = center + radial * radius;
                        cageWeights[n] = vertexWeights[nearest];
                        float t = ring / (float)(Rings - 1);
                        freedom[n] = t * t * (3 - 2 * t);
                        insetRest[n] = cageRest[n] - radial * radius * radialEase * freedom[n];
                        edges.Add(new Edge { a = n, b = Node(side, ring, sector + 1), strength = .45f });
                        if (ring > 0)
                        {
                            edges.Add(new Edge { a = n, b = Node(side, ring - 1, sector), strength = .65f });
                            edges.Add(new Edge { a = n, b = Node(side, ring - 1, sector + 1), strength = .15f });
                            edges.Add(new Edge { a = n, b = Node(side, ring - 1, sector - 1), strength = .15f });
                        }
                    }
            var weightLookup = new Dictionary<BoneWeight, int>();
            foreach (int id in candidates)
            {
                BoneWeight weight = vertexWeights[id];
                if (!weightLookup.TryGetValue(weight, out int skinIndex))
                {
                    skinIndex = uniqueWeights.Count;
                    uniqueWeights.Add(weight);
                    weightLookup.Add(weight, skinIndex);
                }
                float t = Mathf.InverseLerp(StartX, EndX, Mathf.Abs(rest[id].x));
                float axial = t * (Rings - 1);
                int ring = Mathf.Min(Rings - 2, Mathf.FloorToInt(axial));
                float angle = Mathf.Atan2(rest[id].z - CenterZ, rest[id].y - CenterY);
                float angular = Mathf.Repeat(angle / (2 * Mathf.PI), 1) * Sectors;
                int sector = Mathf.FloorToInt(angular);
                int side = rest[id].x < 0 ? 0 : 1;
                bindings.Add(new Binding {
                    vertex = id, a = Node(side, ring, sector), b = Node(side, ring, sector + 1),
                    c = Node(side, ring + 1, sector), d = Node(side, ring + 1, sector + 1),
                    u = axial - ring, v = angular - sector, freedom = t * t * (3 - 2 * t),
                    skinIndex = skinIndex
                });
            }
            mixedSkin = new Matrix4x4[uniqueWeights.Count];
            inverseSkin = new Matrix4x4[uniqueWeights.Count];
            var tris = originalMesh.triangles;
            var usedNormals = new bool[rest.Length];
            for (int i = 0; i < tris.Length; i += 3)
            {
                int part = Mathf.RoundToInt(parts[tris[i]].x);
                if (part != 3 && part != 5) continue;
                for (int k = 0; k < 3; k++)
                {
                    clothTriangles.Add(tris[i + k]);
                    usedNormals[tris[i + k]] = true;
                }
            }
            for (int i = 0; i < rest.Length; i++)
                if (usedNormals[i]) normalVertices.Add(i);
            restSurfaceNormals = new Vector3[rest.Length];
            surfaceNormals = new Vector3[rest.Length];
            AccumulateNormals(rest, restSurfaceNormals);
            runtimeMesh = Instantiate(originalMesh);
            runtimeMesh.name = originalMesh.name + "_SleeveClothInstance";
            runtimeMesh.MarkDynamic();
            target.sharedMesh = runtimeMesh;
            reset = true;
        }

        int FindBone(string name)
        {
            int index = Array.FindIndex(bones, b => b && b.name == name);
            if (index < 0) throw new InvalidOperationException("Sleeve bone missing: " + name);
            return index;
        }

        static int Node(int side, int ring, int sector) =>
            side * SideCount + ring * Sectors + (sector + Sectors) % Sectors;

        Matrix4x4 Skin(BoneWeight w)
        {
            if (w.weight0 > .999999f) return skin[w.boneIndex0];
            Matrix4x4 a = skin[w.boneIndex0], b = skin[w.boneIndex1];
            Matrix4x4 c = skin[w.boneIndex2], d = skin[w.boneIndex3];
            var result = new Matrix4x4();
            for (int j = 0; j < 16; j++)
                result[j] = a[j] * w.weight0 + b[j] * w.weight1 +
                    c[j] * w.weight2 + d[j] * w.weight3;
            return result;
        }

        public void ResetSimulation() => reset = true;

        void LateUpdate() => Evaluate(Time.deltaTime);

        public void Evaluate(float deltaTime)
        {
            Initialize();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            float dt = Mathf.Clamp(deltaTime, 1f / 240f, .05f);
            for (int i = 0; i < skin.Length; i++)
                skin[i] = bones[i].localToWorldMatrix * bindposes[i];
            for (int i = 0; i < mixedSkin.Length; i++)
            {
                mixedSkin[i] = Skin(uniqueWeights[i]);
                inverseSkin[i] = mixedSkin[i].inverse;
            }
            UpdateColliders();
            for (int i = 0; i < positions.Length; i++)
            {
                Matrix4x4 matrix = Skin(cageWeights[i]);
                animated[i] = matrix.MultiplyPoint3x4(cageRest[i]);
                aims[i] = matrix.MultiplyPoint3x4(insetRest[i]);
            }
            // A deeply bent forearm compresses the sleeve toward the shoulder.
            // Gather the whole fabric field before contact, instead of letting
            // adjacent ruffle vertices escape on opposite sides of the forearm.
            for (int side = 0; side < 2; side++)
            {
                Vector3 axis = (capsules[side * 2].b - capsules[side * 2].a).normalized;
                float gather = 0;
                for (int attempt = 0; attempt < 25; attempt++)
                {
                    bool blocked = false;
                    for (int j = side * SideCount + Sectors; j < (side + 1) * SideCount; j++)
                        if (Distance(capsules[side * 2 + 1],
                            aims[j] - axis * gather * freedom[j], out _) < .006f)
                        { blocked = true; break; }
                    if (!blocked) break;
                    gather += .002f;
                }
                for (int j = side * SideCount; j < (side + 1) * SideCount; j++)
                    aims[j] -= axis * gather * freedom[j];
            }
            if (reset || (aims[0] - lastAims[0]).sqrMagnitude > .25f)
            {
                Array.Copy(aims, positions, aims.Length);
                Array.Copy(aims, previous, aims.Length);
                Array.Copy(aims, lastAims, aims.Length);
                reset = false;
                previousStep = 0;
            }
            int steps = Mathf.CeilToInt(dt * 120);
            float h = dt / steps;
            ContactCount = 0;
            for (int step = 0; step < steps; step++)
            {
                float alpha = (step + 1f) / steps;
                for (int i = 0; i < positions.Length; i++)
                {
                    Vector3 aim = Vector3.Lerp(lastAims[i], aims[i], alpha);
                    if (freedom[i] == 0) { positions[i] = previous[i] = aim; continue; }
                    float omega = 2 * Mathf.PI * Mathf.Lerp(4.8f, hemFrequency, freedom[i]);
                    Vector3 velocity = (positions[i] - previous[i]) *
                        (previousStep > 0 ? h / previousStep : 1) *
                        Mathf.Exp(-2 * dampingRatio * omega * h);
                    previous[i] = positions[i];
                    positions[i] += velocity + ((aim - positions[i]) * (omega * omega) +
                        Physics.gravity * gravityScale * freedom[i]) * (h * h);
                }
                for (int iteration = 0; iteration < 5; iteration++)
                {
                    // Fabric resists extension but can compress and fold.
                    foreach (Edge edge in edges)
                    {
                        Vector3 difference = positions[edge.b] - positions[edge.a];
                        float length = difference.magnitude;
                        float limit = Vector3.Distance(aims[edge.a], aims[edge.b]) * 1.08f;
                        float weight = freedom[edge.a] + freedom[edge.b];
                        if (length <= limit || weight == 0) continue;
                        Vector3 correction = difference * ((length - limit) / length * edge.strength / weight);
                        positions[edge.a] += correction * freedom[edge.a];
                        positions[edge.b] -= correction * freedom[edge.b];
                    }
                    for (int i = 0; i < positions.Length; i++)
                    {
                        if (freedom[i] == 0) continue;
                        Vector3 aim = Vector3.Lerp(lastAims[i], aims[i], alpha);
                        positions[i] = aim + Vector3.ClampMagnitude(positions[i] - aim, .045f * freedom[i]);
                        Vector3 before = positions[i];
                        positions[i] = ResolveContacts(positions[i]);
                        if ((positions[i] - before).sqrMagnitude > 1e-12f)
                        {
                            // Remove inward velocity; retain tangential cloth sliding.
                            Vector3 normal = (positions[i] - before).normalized;
                            Vector3 velocity = positions[i] - previous[i];
                            previous[i] = positions[i] - Vector3.ProjectOnPlane(velocity, normal) * .96f;
                            ContactCount++;
                        }
                    }
                }
                previousStep = h;
            }
            Array.Copy(aims, lastAims, aims.Length);
            MaximumOffset = 0;
            MinimumClearance = float.MaxValue;
            foreach (Binding binding in bindings)
            {
                Vector3 offset = Vector3.Lerp(
                    Vector3.Lerp(positions[binding.a] - animated[binding.a],
                        positions[binding.b] - animated[binding.b], binding.v),
                    Vector3.Lerp(positions[binding.c] - animated[binding.c],
                        positions[binding.d] - animated[binding.d], binding.v), binding.u);
                Matrix4x4 matrix = mixedSkin[binding.skinIndex];
                Vector3 baseline = matrix.MultiplyPoint3x4(rest[binding.vertex]);
                Vector3 point = baseline + offset;
                // Dense surface projection closes gaps between cage samples and
                // keeps the ruffle/seam thickness outside the arm.
                if (binding.freedom > .15f) point = ResolveContacts(point);
                MaximumOffset = Mathf.Max(MaximumOffset, (point - baseline).magnitude);
                if (binding.freedom > .5f)
                    MinimumClearance = Mathf.Min(MinimumClearance, Clearance(point));
                output[binding.vertex] = inverseSkin[binding.skinIndex].MultiplyPoint3x4(point);
            }
            Array.Clear(surfaceNormals, 0, surfaceNormals.Length);
            AccumulateNormals(output, surfaceNormals);
            foreach (int i in normalVertices)
                if (surfaceNormals[i].sqrMagnitude > 1e-16f && restSurfaceNormals[i].sqrMagnitude > 1e-16f)
                    normals[i] = Quaternion.FromToRotation(restSurfaceNormals[i], surfaceNormals[i]) * restNormals[i];
            runtimeMesh.vertices = output;
            runtimeMesh.normals = normals;
            // Existing renderer bounds already enclose the body and full skirt.
            watch.Stop();
            LastUpdateMilliseconds = watch.Elapsed.TotalMilliseconds;
        }

        void AccumulateNormals(Vector3[] vertices, Vector3[] result)
        {
            for (int i = 0; i < clothTriangles.Count; i += 3)
            {
                int a = clothTriangles[i], b = clothTriangles[i + 1], c = clothTriangles[i + 2];
                Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                result[a] += normal; result[b] += normal; result[c] += normal;
            }
        }

        void UpdateColliders()
        {
            float scale = target.transform.lossyScale.x;
            for (int side = 0; side < 2; side++)
            {
                capsules[side * 2] = new Capsule { a = bones[upper[side]].position,
                    b = bones[elbow[side]].position, ra = .038f * scale, rb = .029f * scale };
                capsules[side * 2 + 1] = new Capsule { a = bones[elbow[side]].position,
                    b = bones[hand[side]].position, ra = .029f * scale, rb = .020f * scale };
            }
            // A torso-side capsule follows Chest in bind coordinates.
            Matrix4x4 chestSkin = skin[chest];
            capsules[4] = new Capsule {
                a = chestSkin.MultiplyPoint3x4(new Vector3(0, -.015f, 1.20f)),
                b = chestSkin.MultiplyPoint3x4(new Vector3(0, -.015f, 1.32f)),
                ra = .085f * scale, rb = .082f * scale
            };
        }

        float Distance(Capsule capsule, Vector3 point, out Vector3 direction)
        {
            Vector3 axis = capsule.b - capsule.a;
            float t = Mathf.Clamp01(Vector3.Dot(point - capsule.a, axis) / Mathf.Max(axis.sqrMagnitude, 1e-8f));
            Vector3 radial = point - Vector3.Lerp(capsule.a, capsule.b, t);
            float length = radial.magnitude;
            direction = length > 1e-6f ? radial / length : Vector3.up;
            return length - Mathf.Lerp(capsule.ra, capsule.rb, t) - Thickness;
        }

        Vector3 ResolveContacts(Vector3 point)
        {
            for (int iteration = 0; iteration < 48; iteration++)
            {
                float correction = 0;
                foreach (Capsule capsule in capsules)
                {
                    float distance = Distance(capsule, point, out Vector3 normal);
                    if (distance < 0)
                    {
                        point -= normal * distance;
                        correction = Mathf.Max(correction, -distance);
                    }
                }
                if (correction < .00001f) break;
            }
            return point;
        }

        float Clearance(Vector3 point)
        {
            float distance = float.MaxValue;
            foreach (Capsule capsule in capsules)
                distance = Mathf.Min(distance, Distance(capsule, point, out _));
            return distance;
        }

        void OnDisable()
        {
            if (target && originalMesh) target.sharedMesh = originalMesh;
            reset = true;
        }

        void OnEnable()
        {
            if (target && runtimeMesh) target.sharedMesh = runtimeMesh;
            reset = true;
        }

        void OnDestroy()
        {
            DisposeSimulation();
        }
    }
}
