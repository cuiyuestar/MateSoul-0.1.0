using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MateSoul.Runtime
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class CoffeeBeanVolumeRenderer : MonoBehaviour
    {
        private const int MaximumBatchSize = 1023;

        [SerializeField] private Mesh beanMesh;
        [SerializeField] private Material beanMaterial;
        [SerializeField, Min(1)] private int capacity = 720;
        [SerializeField, Min(.005f)] private float radius = .043f;
        [SerializeField, Min(.005f)] private float height = .148f;
        [SerializeField] private float bottom = .018f;
        [SerializeField] private int seed = 20261017;
        [SerializeField] private bool castsShadows = true;
        [SerializeField, HideInInspector] private int visibleCount;
        [SerializeField, HideInInspector] private bool initialized;

        private Matrix4x4[] localMatrices = Array.Empty<Matrix4x4>();
        private readonly Matrix4x4[] drawBatch = new Matrix4x4[MaximumBatchSize];

        public int Capacity => capacity;
        public int VisibleCount => visibleCount;
        public Mesh BeanMesh => beanMesh;
        public Material BeanMaterial => beanMaterial;
        public bool UsesGpuInstancing => beanMaterial != null &&
                                         beanMaterial.enableInstancing;

        public void Configure(Mesh mesh, Material material, int instanceCapacity,
            float fillRadius, float fillHeight, float fillBottom, int randomSeed,
            bool shadows = true)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (instanceCapacity < 1 || !PhysicsBody.IsFinite(fillRadius) ||
                !PhysicsBody.IsFinite(fillHeight) || !PhysicsBody.IsFinite(fillBottom) ||
                fillRadius <= 0 || fillHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(instanceCapacity));
            beanMesh = mesh;
            beanMaterial = material;
            beanMaterial.enableInstancing = true;
            capacity = instanceCapacity;
            radius = fillRadius;
            height = fillHeight;
            bottom = fillBottom;
            seed = randomSeed;
            castsShadows = shadows;
            initialized = true;
            RebuildLayout();
            SetVisibleCount(capacity);
        }

        private void OnEnable()
        {
            if (localMatrices.Length != capacity) RebuildLayout();
            if (!initialized)
            {
                visibleCount = capacity;
                initialized = true;
            }
        }

        private void OnValidate()
        {
            capacity = Mathf.Max(1, capacity);
            radius = Mathf.Max(.005f, radius);
            height = Mathf.Max(.005f, height);
            if (beanMaterial != null) beanMaterial.enableInstancing = true;
            RebuildLayout();
            visibleCount = Mathf.Clamp(visibleCount, 0, capacity);
        }

        public void SetVisibleRatio(float ratio)
            => SetVisibleCount(Mathf.RoundToInt(capacity * Mathf.Clamp01(ratio)));

        public void SetVisibleCount(int count)
            => visibleCount = Mathf.Clamp(count, 0, capacity);

        public void RebuildLayout()
        {
            if (capacity < 1)
            {
                localMatrices = Array.Empty<Matrix4x4>();
                return;
            }
            localMatrices = new Matrix4x4[capacity];
            var random = new System.Random(seed);
            var beanHeight = Mathf.Max(.0047f, height /
                Mathf.Max(1, Mathf.CeilToInt(height / .0054f)));
            var layerCount = Mathf.Max(1, Mathf.CeilToInt(height / beanHeight));
            var perLayer = Mathf.CeilToInt(capacity / (float)layerCount);
            for (var index = 0; index < capacity; index++)
            {
                var layer = index / perLayer;
                var slot = index % perLayer;
                var spiral = (slot + .5f) / perLayer;
                var angle = slot * 2.39996323f + layer * .47f +
                            Range(random, -.12f, .12f);
                var radial = Mathf.Sqrt(spiral) * radius * .93f;
                var position = new Vector3(
                    Mathf.Cos(angle) * radial + Range(random, -.0012f, .0012f),
                    bottom + (layer + .5f) * height / layerCount +
                    Range(random, -.0011f, .0011f),
                    Mathf.Sin(angle) * radial + Range(random, -.0012f, .0012f));
                var rotation = Quaternion.Euler(
                    Range(random, -28f, 28f),
                    Range(random, 0f, 360f),
                    Range(random, -20f, 20f));
                var scale = Vector3.one * Range(random, .90f, 1.08f);
                localMatrices[index] = Matrix4x4.TRS(position, rotation, scale);
            }
        }

        private void LateUpdate()
        {
            if (beanMesh == null || beanMaterial == null || visibleCount <= 0 ||
                localMatrices.Length != capacity)
                return;
            var root = transform.localToWorldMatrix;
            var offset = 0;
            while (offset < visibleCount)
            {
                var count = Mathf.Min(MaximumBatchSize, visibleCount - offset);
                for (var index = 0; index < count; index++)
                    drawBatch[index] = root * localMatrices[offset + index];
                Graphics.DrawMeshInstanced(
                    beanMesh,
                    0,
                    beanMaterial,
                    drawBatch,
                    count,
                    null,
                    castsShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    true,
                    gameObject.layer);
                offset += count;
            }
        }

        private static float Range(System.Random random, float minimum, float maximum)
            => minimum + (float)random.NextDouble() * (maximum - minimum);
    }
}
