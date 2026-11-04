using System;
using System.Collections.Generic;
using System.Linq;
using ByteDance.PICO.XR;
using MateSoul.RoomNavigation;
using UnityEngine;
using UnityEngine.XR;

namespace MateSoul.Runtime
{
    public readonly struct RealityTriangle
    {
        public readonly Vector3 A, B, C;
        public readonly int Label;
        public readonly int SourceId;
        public Vector3 Center => (A + B + C) / 3f;
        public Vector3 Normal => Vector3.Cross(B - A, C - A).normalized;
        public float Area => Vector3.Cross(B - A, C - A).magnitude * .5f;

        public RealityTriangle(Vector3 a, Vector3 b, Vector3 c, bool chair, int sourceId = -1)
            : this(a, b, c,
                (int)(chair ? PxrSemanticLabel.Chair : PxrSemanticLabel.Unknown), sourceId)
        {
        }

        public RealityTriangle(Vector3 a, Vector3 b, Vector3 c, int label, int sourceId)
        {
            A = a;
            B = b;
            C = c;
            Label = label;
            SourceId = sourceId;
        }

        public bool HasSemantic(PxrSemanticLabel semantic) => Label == (int)semantic;
    }

    public sealed class RealityMeshSource : IDisposable
    {
        private readonly Dictionary<Guid, List<RealityTriangle>> blockGeometry =
            new Dictionary<Guid, List<RealityTriangle>>();
        private List<RealityTriangle> geometry = new List<RealityTriangle>();
        private readonly MeasuredMeshRetention retention = new MeasuredMeshRetention();
        private bool disposed;
        private bool dirty;
        private float nextPublish;

        public RealityMeshSource(RoomMapSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        }

        public IReadOnlyList<RealityTriangle> Geometry => geometry;
        public event Action<List<RealityTriangle>, float> Updated;
        public void Tick(float now)
        {
            if (!dirty || disposed || now < nextPublish) return;
            geometry = blockGeometry.Values.SelectMany(value => value).ToList();
            dirty = false;
            nextPublish = now + .25f;
            Updated?.Invoke(geometry, now);
        }

        public void Start()
        {
            if (Application.isEditor) return;
            PXR_Manager.SpatialMeshDataUpdated += OnMeshUpdated;
            // RealityOcclusionMeshRenderer is the single scanning lifecycle owner.
            RefreshCached();
        }

        public void RefreshCached()
        {
            if (Application.isEditor || disposed) return;
            var cached = PXR_Plugin.MixedReality.SpatialMeshData.Values
                .Where(mesh => mesh.state != MeshChangeState.Removed).ToList();
            if (cached.Count > 0) OnMeshUpdated(cached);
        }

        private void OnMeshUpdated(List<PxrSpatialMeshInfo> blocks)
        {
            try
            {
                var changed = false;
                foreach (var block in blocks)
                {
                    if (block.state == MeshChangeState.Removed)
                    {
                        retention.MarkUnobserved(block.uuid);
                        continue;
                    }
                    if (block.state == MeshChangeState.Unchanged &&
                        blockGeometry.ContainsKey(block.uuid)) continue;
                    // Old calibration must not turn newly observed Unknown surfaces
                    // into the furniture that used to occupy this position.
                    if (block.vertices == null || block.indices == null) continue;
                    foreach (var obsolete in retention.Observe(block)) blockGeometry.Remove(obsolete);
                    blockGeometry[block.uuid] = Convert(new[] { block },
                        Array.Empty<RealityItemObservation>());
                    changed = true;
                }
                if (!changed) return;
                dirty = true;
#if MATESOUL_DEVICE_PROBES && DEVELOPMENT_BUILD
                SeatingDeviceProbe.WriteMeshOnce(blocks);
#endif
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }

        public static List<RealityTriangle> Convert(IEnumerable<PxrSpatialMeshInfo> blocks,
            IReadOnlyList<RealityItemObservation> observations)
        {
            var result = new List<RealityTriangle>();
            var semanticVolumes = observations == null
                ? Array.Empty<RealityItemObservation>()
                : observations.OrderBy(item => item.Size.x * item.Size.y * item.Size.z).ToArray();
            foreach (var block in blocks)
            {
                if (block.state == MeshChangeState.Removed ||
                    block.vertices == null || block.indices == null) continue;
                var world = block.vertices.Select(vertex =>
                    block.position + block.rotation * vertex).ToArray();
                for (var index = 0; index + 2 < block.indices.Length; index += 3)
                {
                    var a = block.indices[index];
                    var b = block.indices[index + 1];
                    var c = block.indices[index + 2];
                    if (a >= world.Length || b >= world.Length || c >= world.Length) continue;
                    var label = TriangleLabel(block, a, b, c);
                    if (label == (int)PxrSemanticLabel.Unknown)
                        label = SemanticAt((world[a] + world[b] + world[c]) / 3f,
                            semanticVolumes, label);
                    var triangle = new RealityTriangle(world[a], world[b], world[c], label,
                        block.uuid.GetHashCode());
                    if (triangle.Area > .00001f) result.Add(triangle);
                }
            }
            return result;
        }

        private static int SemanticAt(Vector3 point,
            IReadOnlyList<RealityItemObservation> observations, int fallback)
        {
            if (observations == null) return fallback;
            foreach (var observation in observations)
            {
                var local = Quaternion.Inverse(observation.Rotation) *
                            (point - observation.Center);
                var half = observation.Size * .5f + Vector3.one * .025f;
                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y &&
                    Mathf.Abs(local.z) <= half.z)
                    return (int)observation.Semantic;
            }
            return fallback;
        }

        private static int TriangleLabel(PxrSpatialMeshInfo block, int a, int b, int c)
        {
            var first = VertexLabel(block, a);
            var second = VertexLabel(block, b);
            var third = VertexLabel(block, c);
            if (first == second || first == third) return first;
            return second == third ? second : (int)PxrSemanticLabel.Unknown;
        }

        private static int VertexLabel(PxrSpatialMeshInfo block, int index)
            => block.labels != null && index < block.labels.Length
                ? (int)block.labels[index]
                : (int)PxrSemanticLabel.Unknown;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            PXR_Manager.SpatialMeshDataUpdated -= OnMeshUpdated;
            blockGeometry.Clear();
            retention.Clear();
            geometry.Clear();
        }
    }
}
