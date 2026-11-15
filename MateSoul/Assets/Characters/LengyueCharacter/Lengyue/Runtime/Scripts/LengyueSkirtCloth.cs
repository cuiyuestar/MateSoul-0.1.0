using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace MateSoul.Cloth
{
    /// <summary>
    /// World-space skirt cloth layered on top of SkirtPoseGuard and the
    /// shared garment mesh owned by LengyueSleeveCloth. The cage is a radial
    /// ring grid from the waist to the hem; thigh and shin capsules push the
    /// lower skirt outward so it wraps around the legs during a crouch.
    /// </summary>
    [DefaultExecutionOrder(12600)]
    public sealed class LengyueSkirtCloth : MonoBehaviour
    {
        const string LogPrefix = "MATESOUL_SKIRT_CLOTH";
        [SerializeField] SkinnedMeshRenderer target;
        [SerializeField] LengyueSleeveCloth sleeve;
        [Range(0f, 1f)] public float gravityScale = .32f;
        [Range(1f, 8f)] public float hemFrequency = 2.2f;
        [Range(.1f, 1f)] public float dampingRatio = .46f;
        [Range(.01f, .12f)] public float maxOffset = .075f;
        [Range(1f, 6f)] public int substeps = 2;
        public bool verboseLogging = true;
        [Min(.25f)] public float logInterval = 1f;

        const int Rings = 6, Sectors = 16;
        const int NodeCount = Rings * Sectors;
        const float TopZ = 1.02f, BottomZ = .664f;
        const float InnerSimulationZ = .90f;
        const float Thickness = .004f;

        public bool IsInitialized => runtimeMesh != null && bindings.Count > 0;
        public int VertexCount => bindings.Count;
        public int CageNodeCount => cageRest != null ? cageRest.Length : 0;
        public int SimulatedVertexCount => bindings.Count;
        public int LastWrapNodes { get; private set; }
        public float LastMaxPush { get; private set; }
        public float MaximumOffset { get; private set; }
        public float MinimumClearance { get; private set; }
        public int ContactCount { get; private set; }
        public double LastUpdateMilliseconds { get; private set; }

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

        Mesh source, runtimeMesh;
        Vector3[] rest, output, restNormals, normals;
        Vector3[] restSurfaceNormals, surfaceNormals;
        Vector3[] cageRest, positions, previous, animated, aims, lastAims;
        float[] freedom;
        BoneWeight[] vertexWeights, cageWeights;
        Matrix4x4[] bindposes, skin, mixedSkin, inverseSkin;
        Transform[] bones;
        readonly List<Binding> bindings = new List<Binding>();
        readonly List<Edge> edges = new List<Edge>();
        readonly List<int> clothTriangles = new List<int>();
        readonly List<int> normalVertices = new List<int>();
        readonly List<BoneWeight> uniqueWeights = new List<BoneWeight>();
        readonly Capsule[] capsules = new Capsule[5];
        readonly int[] thigh = new int[2], shin = new int[2], foot = new int[2];
        int hips;
        int skinInfluences;
        bool active, reset = true, initializeFailed;
        bool loggedInit, loggedWaiting, loggedLying;
        float previousStep;
        float logTimer;
        int simulatedOuter, simulatedInner;
        SkirtPoseGuard guard;
        Vector3 center;

        public void ConfigureForOutfit(
            SkinnedMeshRenderer garment, LengyueSleeveCloth cloth)
        {
            Log("configure garment=" + Name(garment) +
                " sleeve=" + Name(cloth) +
                " sameTarget=" + (target == garment && sleeve == cloth) +
                " activeBefore=" + active);
            if (!garment || !cloth)
            {
                active = false;
                DisposeState();
                target = null;
                sleeve = null;
                return;
            }
            if (target == garment && sleeve == cloth &&
                runtimeMesh != null)
            {
                active = true;
                reset = true;
                loggedInit = false;
                return;
            }
            DisposeState();
            target = garment;
            sleeve = cloth;
            active = true;
            reset = true;
            initializeFailed = false;
            loggedInit = false;
            loggedWaiting = false;
            loggedLying = false;
        }

        static string Name(UnityEngine.Object value) =>
            value ? value.name : "<null>";

        void Log(string message)
        {
            if (!verboseLogging) return;
            Debug.Log(LogPrefix + " " + message);
        }

        void DisposeState()
        {
            source = null;
            runtimeMesh = null;
            rest = output = restNormals = normals = null;
            restSurfaceNormals = surfaceNormals = null;
            cageRest = positions = previous = animated = aims = lastAims = null;
            freedom = null;
            vertexWeights = cageWeights = null;
            bindposes = skin = mixedSkin = inverseSkin = null;
            bones = null;
            bindings.Clear();
            edges.Clear();
            clothTriangles.Clear();
            normalVertices.Clear();
            uniqueWeights.Clear();
            MaximumOffset = 0;
            MinimumClearance = 0;
            ContactCount = 0;
            LastUpdateMilliseconds = 0;
            previousStep = 0;
            reset = true;
        }

        void LateUpdate() => Evaluate(Time.deltaTime);

        public void Evaluate(float deltaTime)
        {
            if (!active || !target) return;
            if (Vector3.Dot(target.transform.up, Vector3.up) < .7f)
            {
                // Lying poses are owned by the pose guard; the skirt cloth
                // only resolves leg contacts while the character is upright.
                if (!loggedLying)
                {
                    loggedLying = true;
                    Log("skip upright=false dot=" +
                        Vector3.Dot(target.transform.up, Vector3.up)
                            .ToString("F3", CultureInfo.InvariantCulture));
                }
                reset = true;
                return;
            }
            loggedLying = false;
            if (!sleeve) sleeve = GetComponent<LengyueSleeveCloth>();
            if (!sleeve || !sleeve.IsInitialized)
            {
                if (!loggedWaiting)
                {
                    loggedWaiting = true;
                    Debug.LogWarning(LogPrefix +
                        " waiting for sleeve cloth initialized=" +
                        (sleeve && sleeve.IsInitialized) +
                        " target=" + Name(target));
                }
                return;
            }
            if (initializeFailed)
            {
                if (!loggedInit)
                {
                    loggedInit = true;
                    Debug.LogError(LogPrefix +
                        " disabled after initialization failure target=" +
                        Name(target));
                }
                return;
            }
            if (runtimeMesh && sleeve.RuntimeMesh != runtimeMesh)
            {
                DisposeState();
                reset = true;
                initializeFailed = false;
            }
            if (!IsInitialized) Initialize();
            if (!IsInitialized) return;
            SimulateAndWrite(deltaTime);
        }

        void Fail(string reason)
        {
            initializeFailed = true;
            Debug.LogError(LogPrefix + " init failed: " + reason +
                " target=" + Name(target));
        }

        void Initialize()
        {
            if (initializeFailed) return;
            if (!guard) guard = GetComponent<SkirtPoseGuard>();
            source = sleeve.SourceMesh;
            runtimeMesh = sleeve.RuntimeMesh;
            if (!source || !runtimeMesh || !target)
            {
                Fail("missing source/runtime/target");
                return;
            }
            if (runtimeMesh.vertexCount != source.vertexCount)
            {
                Fail("vertex count mismatch source=" + source.vertexCount +
                    " runtime=" + runtimeMesh.vertexCount);
                return;
            }
            rest = source.vertices;
            output = new Vector3[rest.Length];
            restNormals = source.normals;
            if (restNormals == null || restNormals.Length != rest.Length)
            {
                Fail("missing normals");
                return;
            }
            normals = new Vector3[restNormals.Length];
            vertexWeights = source.boneWeights;
            bindposes = source.bindposes;
            bones = target.bones;
            if (vertexWeights.Length != rest.Length ||
                bindposes.Length != bones.Length)
            {
                Fail("weight/bindpose mismatch weights=" + vertexWeights.Length +
                    " bindposes=" + bindposes.Length + " bones=" + bones.Length);
                return;
            }
            skinInfluences = Mathf.Min(4, (int)QualitySettings.skinWeights,
                target.quality == SkinQuality.Auto ? 4 : (int)target.quality);
            for (int i = 0; i < vertexWeights.Length; i++)
            {
                BoneWeight weight = vertexWeights[i];
                if (skinInfluences < 4) { weight.weight2 = weight.weight3 = 0; }
                if (skinInfluences == 1) weight.weight1 = 0;
                float sum = weight.weight0 + weight.weight1 +
                    weight.weight2 + weight.weight3;
                if (sum <= 1e-8f) { weight.weight0 = 1; sum = 1; }
                weight.weight0 /= sum; weight.weight1 /= sum;
                weight.weight2 /= sum; weight.weight3 /= sum;
                vertexWeights[i] = weight;
            }
            skin = new Matrix4x4[bones.Length];
            for (int side = 0; side < 2; side++)
            {
                string suffix = side == 0 ? "_l" : "_r";
                thigh[side] = FindBone("lengyue_thigh" + suffix);
                shin[side] = FindBone("lengyue_shin" + suffix);
                foot[side] = FindBone("lengyue_foot" + suffix);
            }
            hips = FindBone("lengyue_hips");

            // Candidate vertices: inner and outer skirt parts below the waist.
            var parts = source.uv2;
            var candidates = new List<int>();
            var candidateZ = new List<float>();
            var candidateAngle = new List<float>();
            var candidateRadius = new List<float>();
            var simulated = new List<int>();
            var simulatedVertex = new bool[rest.Length];
            if (parts == null || parts.Length != rest.Length)
            {
                Fail("uv2 missing");
                return;
            }
            center = Vector3.zero;
            simulatedOuter = simulatedInner = 0;
            int waistCount = 0;
            for (int i = 0; i < rest.Length; i++)
            {
                int part = Mathf.RoundToInt(parts[i].x);
                if (part != 1 && part != 2) continue;
                if (rest[i].z > TopZ + .001f) continue;
                candidates.Add(i);
                candidateZ.Add(rest[i].z);
                if (rest[i].z > TopZ - .03f)
                {
                    center.x += rest[i].x;
                    center.y += rest[i].y;
                    waistCount++;
                }
                // The outer hem and the lower inner skirt are simulated; the
                // upper inner skirt stays with the pose guard.
                if (part == 2 ||
                    (part == 1 && rest[i].z < InnerSimulationZ))
                {
                    simulated.Add(i);
                    simulatedVertex[i] = true;
                    if (part == 2) simulatedOuter++;
                    else simulatedInner++;
                }
            }
            if (waistCount > 0)
            {
                center.x /= waistCount;
                center.y /= waistCount;
            }
            for (int i = 0; i < candidates.Count; i++)
            {
                int vertex = candidates[i];
                float dx = rest[vertex].x - center.x;
                float dy = rest[vertex].y - center.y;
                candidateAngle.Add(Mathf.Atan2(dy, dx));
                candidateRadius.Add(Mathf.Sqrt(dx * dx + dy * dy));
            }
            if (candidates.Count == 0)
            {
                Fail("no skirt vertices below z=" + TopZ);
                return;
            }

            // Build the radial cage from the measured hem radii.
            cageRest = new Vector3[NodeCount];
            positions = new Vector3[NodeCount];
            previous = new Vector3[NodeCount];
            animated = new Vector3[NodeCount];
            aims = new Vector3[NodeCount];
            lastAims = new Vector3[NodeCount];
            cageWeights = new BoneWeight[NodeCount];
            freedom = new float[NodeCount];
            var radius = new float[NodeCount];
            var count = new int[NodeCount];
            for (int i = 0; i < candidates.Count; i++)
            {
                float axial = (TopZ - candidateZ[i]) / (TopZ - BottomZ);
                int ring = Mathf.Clamp(
                    Mathf.RoundToInt(axial * (Rings - 1)), 0, Rings - 1);
                float angle = candidateAngle[i] / (2f * Mathf.PI);
                if (angle < 0) angle += 1f;
                int sector = Mathf.Clamp(
                    Mathf.RoundToInt(angle * Sectors) % Sectors, 0, Sectors - 1);
                int node = Node(ring, sector);
                radius[node] = Mathf.Max(radius[node], candidateRadius[i]);
                count[node]++;
            }
            for (int ring = 0; ring < Rings; ring++)
            {
                for (int sector = 0; sector < Sectors; sector++)
                {
                    int node = Node(ring, sector);
                    if (count[node] == 0)
                    {
                        float left = radius[Node(ring, sector - 1)];
                        float right = radius[Node(ring, sector + 1)];
                        radius[node] = left > 0 && right > 0
                            ? (left + right) * .5f
                            : Mathf.Max(left, right);
                    }
                    if (radius[node] <= 0)
                        radius[node] = .14f;
                    float t = ring / (float)(Rings - 1);
                    float angle = sector * 2f * Mathf.PI / Sectors;
                    float z = Mathf.Lerp(TopZ, BottomZ, t);
                    cageRest[node] = new Vector3(
                        center.x + Mathf.Cos(angle) * radius[node],
                        center.y + Mathf.Sin(angle) * radius[node], z);
                    freedom[node] = t * t;
                    edges.Add(new Edge {
                        a = node, b = Node(ring, sector + 1), strength = .5f });
                    if (ring > 0)
                    {
                        edges.Add(new Edge {
                            a = node, b = Node(ring - 1, sector), strength = .7f });
                        edges.Add(new Edge {
                            a = node, b = Node(ring - 1, sector + 1), strength = .18f });
                        edges.Add(new Edge {
                            a = node, b = Node(ring - 1, sector - 1), strength = .18f });
                    }
                }
            }
            var ringInfo = new StringBuilder();
            for (int ring = 0; ring < Rings; ring++)
            {
                float minimum = float.MaxValue, maximum = 0f, sum = 0f;
                for (int sector = 0; sector < Sectors; sector++)
                {
                    float value = radius[Node(ring, sector)];
                    minimum = Mathf.Min(minimum, value);
                    maximum = Mathf.Max(maximum, value);
                    sum += value;
                }
                ringInfo.AppendFormat(CultureInfo.InvariantCulture,
                    " r{0}=[{1:F3},{2:F3}]/{3:F3}",
                    ring, minimum, maximum, sum / Sectors);
            }
            for (int ring = 0; ring < Rings; ring++)
                for (int sector = 0; sector < Sectors; sector++)
                {
                    int node = Node(ring, sector);
                    Vector3 point = cageRest[node];                    int best = -1;
                    float bestDistance = float.MaxValue;
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        float distance = (rest[candidates[i]] - point).sqrMagnitude;
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            best = candidates[i];
                        }
                    }
                    if (best >= 0)
                    {
                        cageWeights[node] = vertexWeights[best];
                    }
                }

            var weightLookup = new Dictionary<BoneWeight, int>();
            for (int i = 0; i < simulated.Count; i++)
            {
                int vertex = simulated[i];
                BoneWeight weight = vertexWeights[vertex];
                int skinIndex;
                if (!weightLookup.TryGetValue(weight, out skinIndex))
                {
                    skinIndex = uniqueWeights.Count;
                    uniqueWeights.Add(weight);
                    weightLookup.Add(weight, skinIndex);
                }
                float axial = (TopZ - rest[vertex].z) / (TopZ - BottomZ);
                axial = Mathf.Clamp(axial, 0f, 1f) * (Rings - 1);
                int ring = Mathf.Min(Rings - 2, Mathf.FloorToInt(axial));
                float u = Mathf.Clamp01(axial - ring);
                float angle = Mathf.Atan2(
                    rest[vertex].y - center.y,
                    rest[vertex].x - center.x) / (2f * Mathf.PI);
                if (angle < 0) angle += 1f;
                float angular = angle * Sectors;
                int sector = Mathf.FloorToInt(angular) % Sectors;
                float v = angular - Mathf.Floor(angular);
                float t = Mathf.Clamp01(axial / (Rings - 1));
                bindings.Add(new Binding {
                    vertex = vertex,
                    a = Node(ring, sector),
                    b = Node(ring, sector + 1),
                    c = Node(ring + 1, sector),
                    d = Node(ring + 1, sector + 1),
                    u = u, v = v, freedom = t * t,
                    skinIndex = skinIndex
                });
            }

            mixedSkin = new Matrix4x4[uniqueWeights.Count];
            inverseSkin = new Matrix4x4[uniqueWeights.Count];
            var tris = source.triangles;
            var usedNormals = new bool[rest.Length];
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                if (!simulatedVertex[tris[i]] ||
                    !simulatedVertex[tris[i + 1]] ||
                    !simulatedVertex[tris[i + 2]]) continue;
                clothTriangles.Add(tris[i]);
                clothTriangles.Add(tris[i + 1]);
                clothTriangles.Add(tris[i + 2]);
                usedNormals[tris[i]] = true;
                usedNormals[tris[i + 1]] = true;
                usedNormals[tris[i + 2]] = true;
            }
            for (int i = 0; i < rest.Length; i++)
                if (usedNormals[i]) normalVertices.Add(i);
            restSurfaceNormals = new Vector3[rest.Length];
            surfaceNormals = new Vector3[rest.Length];
            AccumulateNormals(rest, restSurfaceNormals);
            output = new Vector3[rest.Length];
            normals = new Vector3[restNormals.Length];
            // The cloth can wrap beyond the source skirt cone around a bent
            // thigh; widen the shared culling bounds so the hem is not clipped.
            Bounds bounds = source.bounds;
            bounds.Expand(.7f);
            target.localBounds = bounds;
            reset = true;
            loggedInit = true;
            Log("init mesh=" + source.name +
                " runtime=" + runtimeMesh.name +
                " v=" + source.vertexCount +
                " tris=" + (source.triangles.Length / 3) +
                " candidates=" + candidates.Count +
                " simulated=" + simulated.Count +
                " outer=" + simulatedOuter +
                " inner=" + simulatedInner +
                " bindings=" + bindings.Count +
                " weights=" + uniqueWeights.Count +
                " cage=" + NodeCount +
                " edges=" + edges.Count +
                " influences=" + skinInfluences +
                " quality=" + target.quality +
                " center=(" + center.x.ToString("F3", CultureInfo.InvariantCulture) +
                "," + center.y.ToString("F3", CultureInfo.InvariantCulture) + ")" +
                " rings:" + ringInfo +
                " legs=hips" + hips + ",L" + thigh[0] + "/" + shin[0] + "/" + foot[0] +
                ",R" + thigh[1] + "/" + shin[1] + "/" + foot[1] +
                " capsuleRadii=hips.115/.105 thigh.075/.060 shin.055/.042" +
                " boundsCenter=" + bounds.center.ToString("F3") +
                " boundsSize=" + bounds.size.ToString("F3") +
                " guard=" + (guard ? guard.name : "<null>") +
                " sleeveSource=" + sleeve.SourceMesh.name);
        }

        int FindBone(string name)
        {
            int index = Array.FindIndex(bones, b => b && b.name == name);
            if (index < 0) throw new InvalidOperationException("Skirt bone missing: " + name);
            return index;
        }

        static int Node(int ring, int sector) =>
            ring * Sectors + (sector + Sectors) % Sectors;

        Matrix4x4 Skin(BoneWeight weight)
        {
            if (weight.weight0 > .999999f) return skin[weight.boneIndex0];
            Matrix4x4 a = skin[weight.boneIndex0], b = skin[weight.boneIndex1];
            Matrix4x4 c = skin[weight.boneIndex2], d = skin[weight.boneIndex3];
            var result = new Matrix4x4();
            for (int j = 0; j < 16; j++)
                result[j] = a[j] * weight.weight0 + b[j] * weight.weight1 +
                    c[j] * weight.weight2 + d[j] * weight.weight3;
            return result;
        }

        void SimulateAndWrite(float deltaTime)
        {
            if (target.sharedMesh != runtimeMesh)
            {
                Fail("runtime mesh is not assigned to the renderer");
                return;
            }
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
            int wrapNodes = 0;
            float maxPush = 0f;
            for (int i = 0; i < NodeCount; i++)
            {
                Matrix4x4 matrix = Skin(cageWeights[i]);
                animated[i] = matrix.MultiplyPoint3x4(cageRest[i]);
                aims[i] = freedom[i] > .05f
                    ? WrapLegs(animated[i])
                    : animated[i];
                float push = Vector3.Distance(aims[i], animated[i]);
                if (push > .001f)
                {
                    wrapNodes++;
                    maxPush = Mathf.Max(maxPush, push);
                }
            }
            LastWrapNodes = wrapNodes;
            LastMaxPush = maxPush;
            if (reset || (aims[0] - lastAims[0]).sqrMagnitude > .25f)
            {
                Array.Copy(aims, positions, aims.Length);
                Array.Copy(aims, previous, aims.Length);
                Array.Copy(aims, lastAims, aims.Length);
                reset = false;
                previousStep = 0;
            }
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 90f * substeps));
            float h = dt / steps;
            ContactCount = 0;
            for (int step = 0; step < steps; step++)
            {
                float alpha = (step + 1f) / steps;
                for (int i = 0; i < NodeCount; i++)
                {
                    Vector3 aim = Vector3.Lerp(lastAims[i], aims[i], alpha);
                    if (freedom[i] <= 0)
                    {
                        positions[i] = previous[i] = aim;
                        continue;
                    }
                    float omega = 2f * Mathf.PI *
                        Mathf.Lerp(4.2f, hemFrequency, freedom[i]);
                    Vector3 velocity = (positions[i] - previous[i]) *
                        (previousStep > 0 ? h / previousStep : 1) *
                        Mathf.Exp(-2f * dampingRatio * omega * h);
                    previous[i] = positions[i];
                    positions[i] += velocity +
                        ((aim - positions[i]) * (omega * omega) +
                         Physics.gravity * gravityScale * freedom[i]) * (h * h);
                }
                for (int iteration = 0; iteration < 4; iteration++)
                {
                    foreach (Edge edge in edges)
                    {
                        Vector3 difference = positions[edge.b] - positions[edge.a];
                        float length = difference.magnitude;
                        float limit = Vector3.Distance(
                            aims[edge.a], aims[edge.b]) * 1.08f;
                        float weight = freedom[edge.a] + freedom[edge.b];
                        if (length <= limit || weight == 0) continue;
                        Vector3 correction = difference *
                            ((length - limit) / length * edge.strength / weight);
                        positions[edge.a] += correction * freedom[edge.a];
                        positions[edge.b] -= correction * freedom[edge.b];
                    }
                    for (int i = 0; i < NodeCount; i++)
                    {
                        if (freedom[i] <= 0) continue;
                        Vector3 aim = Vector3.Lerp(lastAims[i], aims[i], alpha);
                        positions[i] = aim + Vector3.ClampMagnitude(
                            positions[i] - aim, maxOffset * freedom[i]);
                        Vector3 before = positions[i];
                        positions[i] = ResolveContacts(positions[i]);
                        if ((positions[i] - before).sqrMagnitude > 1e-12f)
                        {
                            Vector3 normal = (positions[i] - before).normalized;
                            Vector3 velocity = positions[i] - previous[i];
                            previous[i] = positions[i] -
                                Vector3.ProjectOnPlane(velocity, normal) * .96f;
                            ContactCount++;
                        }
                    }
                }
                previousStep = h;
            }
            Array.Copy(aims, lastAims, aims.Length);

            Vector3[] current = runtimeMesh.vertices;
            if (output == null || output.Length != current.Length)
                output = new Vector3[current.Length];
            Array.Copy(current, output, current.Length);
            Vector3[] currentNormals = runtimeMesh.normals;
            bool writeNormals = currentNormals != null &&
                currentNormals.Length == restNormals.Length;
            if (writeNormals)
            {
                if (normals == null || normals.Length != currentNormals.Length)
                    normals = new Vector3[currentNormals.Length];
                Array.Copy(currentNormals, normals, currentNormals.Length);
            }

            MaximumOffset = 0;
            MinimumClearance = float.MaxValue;
            foreach (Binding binding in bindings)
            {
                if (binding.freedom <= .001f) continue;
                Vector3 offset = Vector3.Lerp(
                    Vector3.Lerp(positions[binding.a] - animated[binding.a],
                        positions[binding.b] - animated[binding.b], binding.v),
                    Vector3.Lerp(positions[binding.c] - animated[binding.c],
                        positions[binding.d] - animated[binding.d], binding.v),
                    binding.u);
                Matrix4x4 matrix = mixedSkin[binding.skinIndex];
                Vector3 baseline = matrix.MultiplyPoint3x4(rest[binding.vertex]);
                Vector3 point = baseline + offset;
                if (binding.freedom > .15f)
                {
                    point = WrapLegs(point);
                    point = ResolveContacts(point);
                }
                MaximumOffset = Mathf.Max(MaximumOffset,
                    (point - baseline).magnitude);
                if (binding.freedom > .5f)
                    MinimumClearance = Mathf.Min(MinimumClearance, Clearance(point));
                output[binding.vertex] =
                    inverseSkin[binding.skinIndex].MultiplyPoint3x4(point);
            }
            Array.Clear(surfaceNormals, 0, surfaceNormals.Length);
            AccumulateNormals(output, surfaceNormals);
            if (writeNormals)
                foreach (int i in normalVertices)
                    if (surfaceNormals[i].sqrMagnitude > 1e-16f &&
                        restSurfaceNormals[i].sqrMagnitude > 1e-16f)
                        normals[i] = Quaternion.FromToRotation(
                            restSurfaceNormals[i], surfaceNormals[i]) * restNormals[i];
            runtimeMesh.vertices = output;
            if (writeNormals) runtimeMesh.normals = normals;
            watch.Stop();
            LastUpdateMilliseconds = watch.Elapsed.TotalMilliseconds;
            logTimer += deltaTime;
            if (!verboseLogging || logTimer < logInterval) return;
            logTimer = 0f;
            float flexLeft = guard ? guard.LastLeftFlex : 0f;
            float flexRight = guard ? guard.LastRightFlex : 0f;
            float avoidance = guard ? guard.LastAvoidanceWeight : 0f;
            float drape = guard ? guard.LastDrapeWeight : 0f;
            float hemWidth = guard ? guard.CurrentHemHalfWidth : 0f;
            float hemDepth = guard ? guard.CurrentHemDepthRadius : 0f;
            string clearance = MinimumClearance == float.MaxValue
                ? "none"
                : MinimumClearance.ToString("F4", CultureInfo.InvariantCulture);
            Log("stats t=" + Time.unscaledTime.ToString("F2",
                    CultureInfo.InvariantCulture) +
                " frame=" + Time.frameCount +
                " ms=" + LastUpdateMilliseconds.ToString("F3",
                    CultureInfo.InvariantCulture) +
                " bindings=" + bindings.Count +
                " contacts=" + ContactCount +
                " wrapNodes=" + LastWrapNodes +
                " maxPush=" + LastMaxPush.ToString("F3", CultureInfo.InvariantCulture) +
                " maxOffset=" + MaximumOffset.ToString("F3", CultureInfo.InvariantCulture) +
                " clearance=" + clearance +
                " hipsY=" + bones[hips].position.y.ToString("F3", CultureInfo.InvariantCulture) +
                " flexL=" + flexLeft.ToString("F1", CultureInfo.InvariantCulture) +
                " flexR=" + flexRight.ToString("F1", CultureInfo.InvariantCulture) +
                " avoid=" + avoidance.ToString("F2", CultureInfo.InvariantCulture) +
                " drape=" + drape.ToString("F2", CultureInfo.InvariantCulture) +
                " hemW=" + hemWidth.ToString("F3", CultureInfo.InvariantCulture) +
                " hemD=" + hemDepth.ToString("F3", CultureInfo.InvariantCulture));
            if (Mathf.Max(Mathf.Abs(flexLeft), Mathf.Abs(flexRight)) > 30f &&
                LastWrapNodes == 0 && ContactCount == 0)
                Debug.LogWarning(LogPrefix +
                    " crouch without skirt contact/wrap t=" +
                    Time.unscaledTime.ToString("F2", CultureInfo.InvariantCulture) +
                    " frame=" + Time.frameCount +
                    " flexL=" + flexLeft.ToString("F1", CultureInfo.InvariantCulture) +
                    " flexR=" + flexRight.ToString("F1", CultureInfo.InvariantCulture) +
                    " target=" + Name(target));
        }

        void AccumulateNormals(Vector3[] vertices, Vector3[] result)
        {
            for (int i = 0; i + 2 < clothTriangles.Count; i += 3)
            {
                int a = clothTriangles[i], b = clothTriangles[i + 1],
                    c = clothTriangles[i + 2];
                Vector3 normal = Vector3.Cross(
                    vertices[b] - vertices[a], vertices[c] - vertices[a]);
                result[a] += normal;
                result[b] += normal;
                result[c] += normal;
            }
        }

        void UpdateColliders()
        {
            float scale = target.transform.lossyScale.x;
            Transform hipsBone = bones[hips];
            capsules[0] = new Capsule {
                a = hipsBone.position - hipsBone.up * .03f,
                b = hipsBone.position + hipsBone.up * .10f,
                ra = .115f * scale, rb = .105f * scale
            };
            for (int side = 0; side < 2; side++)
            {
                capsules[1 + side * 2] = new Capsule {
                    a = bones[thigh[side]].position,
                    b = bones[shin[side]].position,
                    ra = .075f * scale, rb = .060f * scale
                };
                capsules[2 + side * 2] = new Capsule {
                    a = bones[shin[side]].position,
                    b = bones[foot[side]].position,
                    ra = .055f * scale, rb = .042f * scale
                };
            }
        }

        float Distance(Capsule capsule, Vector3 point, out Vector3 direction)
        {
            Vector3 axis = capsule.b - capsule.a;
            float t = Mathf.Clamp01(Vector3.Dot(
                point - capsule.a, axis) / Mathf.Max(axis.sqrMagnitude, 1e-8f));
            Vector3 radial = point - Vector3.Lerp(capsule.a, capsule.b, t);
            float length = radial.magnitude;
            direction = length > 1e-6f ? radial / length : Vector3.up;
            return length - Mathf.Lerp(capsule.ra, capsule.rb, t) - Thickness;
        }

        /// <summary>
        /// When a bent thigh leaves the skirt cone, the cloth must move to the
        /// outside of the leg instead of staying behind it. Points in the leg's
        /// angular sector are extended radially until they cover the capsule.
        /// </summary>
        Vector3 WrapLegs(Vector3 point)
        {
            Vector3 up = Vector3.up;
            Vector3 axis = bones[hips].position;
            Vector3 radial = Vector3.ProjectOnPlane(point - axis, up);
            float radius = radial.magnitude;
            if (radius < 1e-4f) return point;
            Vector3 direction = radial / radius;
            float targetRadius = radius;
            for (int i = 1; i < capsules.Length; i++)
            {
                Capsule capsule = capsules[i];
                Vector3 segment = capsule.b - capsule.a;
                float t = Mathf.Clamp01(Vector3.Dot(
                    point - capsule.a, segment) /
                    Mathf.Max(segment.sqrMagnitude, 1e-8f));
                Vector3 closest = Vector3.Lerp(capsule.a, capsule.b, t);
                Vector3 legRadial = Vector3.ProjectOnPlane(closest - axis, up);
                float legDistance = legRadial.magnitude;
                if (legDistance < .02f) continue;
                float cosine = Vector3.Dot(direction, legRadial / legDistance);
                float weight = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(.34f, .94f, cosine));
                if (weight <= 0f) continue;
                float required = legDistance +
                    Mathf.Lerp(capsule.ra, capsule.rb, t) + .01f;
                if (required <= radius) continue;
                targetRadius = Mathf.Max(targetRadius,
                    Mathf.Lerp(radius, required, weight));
            }
            if (targetRadius <= radius + 1e-5f) return point;
            return axis + up * Vector3.Dot(point - axis, up) +
                direction * targetRadius;
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
            // The shared runtime mesh stays owned by LengyueSleeveCloth.
            reset = true;
        }
    }
}
