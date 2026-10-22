using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MateSoul.Runtime
{
    public sealed class RealityEnvironmentOcclusionFeature : ScriptableRendererFeature
    {
        [SerializeField] private Shader occlusionShader;

        private Material material;
        private EnvironmentOcclusionPass pass;

        public void Configure(Shader shader)
        {
            occlusionShader = shader;
            Create();
        }

        public override void Create()
        {
            CoreUtils.Destroy(material);
            material = occlusionShader != null
                ? CoreUtils.CreateEngineMaterial(occlusionShader)
                : null;
            pass = material != null ? new EnvironmentOcclusionPass(material) : null;
        }

        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            if (!RealityOcclusionMeshRenderer.EnvironmentDepthAvailable ||
                pass == null ||
                renderingData.cameraData.cameraType != CameraType.Game ||
                renderingData.cameraData.camera != Camera.main)
                return;

            renderer.EnqueuePass(pass);
        }

        public override void SetupRenderPasses(
            ScriptableRenderer renderer,
            in RenderingData renderingData)
        {
            if (!RealityOcclusionMeshRenderer.EnvironmentDepthAvailable ||
                pass == null ||
                renderingData.cameraData.cameraType != CameraType.Game ||
                renderingData.cameraData.camera != Camera.main)
                return;

            pass.SetTarget(renderer.cameraColorTargetHandle);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
            pass = null;
        }

        private sealed class EnvironmentOcclusionPass : ScriptableRenderPass
        {
            private readonly Material material;
            private RTHandle colorTarget;

            public EnvironmentOcclusionPass(Material material)
            {
                this.material = material;
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public void SetTarget(RTHandle target)
            {
                colorTarget = target;
            }

            public override void OnCameraSetup(
                CommandBuffer commandBuffer,
                ref RenderingData renderingData)
            {
                ConfigureTarget(colorTarget);
            }

            public override void Execute(
                ScriptableRenderContext context,
                ref RenderingData renderingData)
            {
                var commandBuffer = CommandBufferPool.Get("MateSoul Reality Occlusion");
                commandBuffer.DrawProcedural(
                    Matrix4x4.identity,
                    material,
                    0,
                    MeshTopology.Triangles,
                    3,
                    1);
                context.ExecuteCommandBuffer(commandBuffer);
                CommandBufferPool.Release(commandBuffer);
            }
        }
    }
}
