using System;
using System.Collections.Generic;
using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace MateSoul.Runtime
{
    // 把设备侧的实时空间网格以半透明语义色画出来，让玩家/开发者直接看到
    // 扫描覆盖范围和每个 block 的语义标签。显隐由 HUD 开关控制。
    [DisallowMultipleComponent]
    public sealed class SpatialMeshVisualizer : MonoBehaviour
    {
        private const string OverlayShaderResource = "RealityOcclusion/SpatialMeshOverlay";
        // 顶点 alpha 区分 block 状态：刚更新的 block 更亮，能看出网格在长。
        private const float AddedAlpha = .5f;
        private const float UpdatedAlpha = .68f;
        private const float UnchangedAlpha = .3f;

        private static SpatialMeshVisualizer instance;
        private readonly Dictionary<Guid, Entry> entries = new Dictionary<Guid, Entry>();
        private Material overlayMaterial;

        public int BlockCount => entries.Count;

        public static SpatialMeshVisualizer Install(Transform parent)
        {
            if (Application.isEditor) return null;
            if (instance != null) return instance;
            var host = new GameObject("Spatial Mesh Visualizer");
            if (parent != null) host.transform.SetParent(parent, false);
            instance = host.AddComponent<SpatialMeshVisualizer>();
            return instance;
        }

        // 显隐开关变化后调用：打开时从 SDK 缓存重建一遍，关闭时只停渲染。
        public static void RefreshVisibility()
        {
            if (instance == null) return;
            instance.ApplyVisibility();
        }

        private void Awake()
        {
            instance = this;
            var shader = Resources.Load<Shader>(OverlayShaderResource);
            if (shader == null)
            {
                Debug.LogWarning($"MATESOUL_MESH_VIZ shader missing: {OverlayShaderResource}");
                enabled = false;
                return;
            }
            overlayMaterial = new Material(shader)
            {
                name = "Reality Spatial Mesh Overlay (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
            overlayMaterial.SetFloat("_Alpha", 1f);
            PXR_Manager.SpatialMeshDataUpdated += OnSpatialMeshDataUpdated;
            if (RealityOcclusionMeshRenderer.MeshVisualizationAllowed)
                ApplyVisibility();
        }

        private void OnDestroy()
        {
            PXR_Manager.SpatialMeshDataUpdated -= OnSpatialMeshDataUpdated;
            ClearEntries();
            if (overlayMaterial != null) Destroy(overlayMaterial);
            if (instance == this) instance = null;
        }

        private void ApplyVisibility()
        {
            if (overlayMaterial == null) return;
            var visible = RealityOcclusionMeshRenderer.MeshVisualizationAllowed;
            // 隐藏期间不跟踪增量，重新打开时整体重建才能对上当前房间。
            if (visible) RebuildFromCache();
            else
                foreach (var entry in entries.Values)
                    if (entry.Renderer != null) entry.Renderer.enabled = false;
            Debug.Log($"MATESOUL_MESH_VIZ visible={(visible ? 1 : 0)} blocks={entries.Count}");
        }

        // 开关打开前 SDK 已经发过的 block 不会再发 Added，直接从缓存补一遍。
        private void RebuildFromCache()
        {
            ClearEntries();
            foreach (var block in PXR_Plugin.MixedReality.SpatialMeshData.Values)
                if (block.state != MeshChangeState.Removed)
                    Build(block);
            Debug.Log($"MATESOUL_MESH_VIZ rebuild blocks={entries.Count}");
        }

        private void OnSpatialMeshDataUpdated(List<PxrSpatialMeshInfo> blocks)
        {
            if (blocks == null || overlayMaterial == null) return;
            if (!RealityOcclusionMeshRenderer.MeshVisualizationAllowed) return;
            foreach (var block in blocks)
            {
                if (block.state == MeshChangeState.Removed)
                {
                    Remove(block.uuid);
                    continue;
                }
                if (block.state == MeshChangeState.Unchanged && entries.ContainsKey(block.uuid))
                    continue;
                Build(block);
            }
        }

        private void Build(PxrSpatialMeshInfo block)
        {
            if (block.vertices == null || block.indices == null ||
                block.vertices.Length < 3 || block.indices.Length < 3)
                return;

            if (!entries.TryGetValue(block.uuid, out var entry))
            {
                var host = new GameObject($"Spatial Mesh {block.uuid:N}");
                host.transform.SetParent(transform, false);
                var filter = host.AddComponent<MeshFilter>();
                var renderer = host.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = overlayMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.allowOcclusionWhenDynamic = false;
                entry = new Entry(host, new Mesh { name = host.name }, filter, renderer);
                filter.sharedMesh = entry.Mesh;
                entries[block.uuid] = entry;
            }

            var mesh = entry.Mesh;
            mesh.Clear();
            mesh.indexFormat = block.vertices.Length > ushort.MaxValue
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;
            mesh.SetVertices(block.vertices);
            mesh.SetColors(BuildColors(block));
            mesh.SetTriangles(Array.ConvertAll(block.indices, value => (int)value), 0);
            mesh.RecalculateBounds();

            entry.Host.transform.SetPositionAndRotation(block.position, block.rotation);
            entry.Renderer.enabled = RealityOcclusionMeshRenderer.MeshVisualizationAllowed;
        }

        private static Color[] BuildColors(PxrSpatialMeshInfo block)
        {
            var alpha = block.state switch
            {
                MeshChangeState.Added => AddedAlpha,
                MeshChangeState.Updated => UpdatedAlpha,
                _ => UnchangedAlpha
            };
            var colors = new Color[block.vertices.Length];
            var labels = block.labels;
            for (var index = 0; index < colors.Length; index++)
            {
                var label = labels != null && index < labels.Length
                    ? labels[index]
                    : PxrSemanticLabel.Unknown;
                var color = SemanticColor(label);
                color.a = alpha;
                colors[index] = color;
            }
            return colors;
        }

        private static Color SemanticColor(PxrSemanticLabel label)
        {
            return label switch
            {
                PxrSemanticLabel.Floor => new Color(.72f, .72f, .72f),
                PxrSemanticLabel.Ceiling => new Color(.55f, .55f, .55f),
                PxrSemanticLabel.Wall => new Color(.20f, .55f, 1f),
                PxrSemanticLabel.Door => new Color(0f, .85f, .9f),
                PxrSemanticLabel.Window => new Color(1f, .25f, .9f),
                PxrSemanticLabel.Opening => new Color(1f, .85f, .1f),
                PxrSemanticLabel.Table => new Color(1f, .25f, .15f),
                PxrSemanticLabel.Sofa => new Color(.2f, .9f, .3f),
                PxrSemanticLabel.Chair => new Color(.55f, .1f, .1f),
                PxrSemanticLabel.Human => new Color(.1f, .6f, .1f),
                PxrSemanticLabel.Curtain => new Color(.1f, .1f, .65f),
                PxrSemanticLabel.Cabinet => new Color(1f, .55f, .1f),
                PxrSemanticLabel.Bed => new Color(1f, .75f, .8f),
                PxrSemanticLabel.Plant => new Color(.6f, .2f, .6f),
                PxrSemanticLabel.Screen => new Color(.55f, .3f, .1f),
                PxrSemanticLabel.Refrigerator => new Color(.6f, .6f, .1f),
                PxrSemanticLabel.WashingMachine => new Color(1f, .85f, .1f),
                PxrSemanticLabel.AirConditioner => new Color(.8f, .8f, .8f),
                PxrSemanticLabel.Lamp => new Color(.55f, 1f, .55f),
                PxrSemanticLabel.WallArt => new Color(.55f, .1f, .3f),
                PxrSemanticLabel.Stairway => new Color(.35f, .1f, .35f),
                _ => Color.white
            };
        }

        private void Remove(Guid id)
        {
            if (!entries.TryGetValue(id, out var entry)) return;
            entries.Remove(id);
            DestroyEntry(entry);
        }

        private void ClearEntries()
        {
            foreach (var entry in entries.Values) DestroyEntry(entry);
            entries.Clear();
        }

        private static void DestroyEntry(Entry entry)
        {
            if (entry.Host != null) Destroy(entry.Host);
            if (entry.Mesh != null) Destroy(entry.Mesh);
        }

        private sealed class Entry
        {
            public Entry(GameObject host, Mesh mesh, MeshFilter filter, MeshRenderer renderer)
            {
                Host = host;
                Mesh = mesh;
                Filter = filter;
                Renderer = renderer;
            }

            public GameObject Host { get; }
            public Mesh Mesh { get; }
            public MeshFilter Filter { get; }
            public MeshRenderer Renderer { get; }
        }
    }
}
