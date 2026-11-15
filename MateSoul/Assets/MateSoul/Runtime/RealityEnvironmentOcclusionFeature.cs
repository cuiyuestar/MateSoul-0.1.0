using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MateSoul.Runtime
{
    public sealed class RealityEnvironmentOcclusionFeature : ScriptableRendererFeature
    {
        private static bool reportedExecution;
        // 合成趟的实际执行证据：门控日志只能说明「为什么跳过」，没有这里就无法
        // 区分「跳过」与「执行了但没效果」。
        private static int compositeFrames;
        private static int lastCompositeFrame = -1;
        // 合成只改 alpha；把实际写入的 eye buffer 暴露给诊断，才能验证
        // 「alpha 已经写进画面」还是「写进去了但合成器没采用」。
        private static RenderTexture lastColorTarget;
        private static int lastColorTargetFrame = -1;
        private static string lastDecision = "init";
        private static float nextDecisionLog;
        private const float DecisionLogInterval = 1f;
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
            var skip = ResolveSkipReason(renderingData);
            ReportDecision(skip, renderingData);
            if (skip != null) return;
            renderer.EnqueuePass(pass);
        }

        public override void SetupRenderPasses(
            ScriptableRenderer renderer,
            in RenderingData renderingData)
        {
            if (ResolveSkipReason(renderingData) != null) return;
            pass.SetTarget(renderer.cameraColorTargetHandle);
        }

        internal static int CompositeFrames => compositeFrames;
        internal static int LastCompositeFrame => lastCompositeFrame;
        internal static RenderTexture LastColorTarget => lastColorTarget;
        internal static int LastColorTargetFrame => lastColorTargetFrame;
        internal static int CompositeAgeFrames => lastCompositeFrame < 0
            ? -1
            : Time.frameCount - lastCompositeFrame;

        // 返回 null 表示本帧会执行环境深度合成；否则返回被跳过的那一环。
        // 原实现把六个条件写进一个 if，设备日志无法区分是深度过期、相机不对
        // 还是覆盖度门控把整趟合成关掉了。
        private string ResolveSkipReason(in RenderingData renderingData)
        {
            if (pass == null) return "no-material";
            if (!RealityOcclusionMeshRenderer.EnvironmentDepthAvailable) return "sdk-unavailable";
            if (!RealityOcclusionMeshRenderer.EnvironmentOcclusionAllowed) return "flag-disabled";
            var cameraData = renderingData.cameraData;
            if (cameraData.cameraType != CameraType.Game) return "not-game-camera";
            if (cameraData.camera != Camera.main) return "not-main-camera";
            var manager = cameraData.camera.GetComponentInParent<PXR_EnvironmentDepthManager>();
            if (manager == null) return "no-depth-manager";
            if (!manager.IsDepthAvailable) return "depth-stale";
            if (RealityOcclusionMeshRenderer.EnvironmentCoverageSufficient(cameraData.camera))
                return "coverage-gate";
            return null;
        }

        private void ReportDecision(string skip,
            in RenderingData renderingData)
        {
            var reason = skip ?? "composite";
            if (reason == lastDecision && Time.unscaledTime < nextDecisionLog) return;
            nextDecisionLog = Time.unscaledTime + DecisionLogInterval;
            lastDecision = reason;
            var camera = renderingData.cameraData.camera;
            var manager = camera != null
                ? camera.GetComponentInParent<PXR_EnvironmentDepthManager>()
                : null;
            Debug.Log($"MATESOUL_OCCLUSION_FEATURE skipped={(skip != null ? 1 : 0)} " +
                      $"reason={reason} " +
                      $"envAllowed={(RealityOcclusionMeshRenderer.EnvironmentOcclusionAllowed ? 1 : 0)} " +
                      $"gateAllowed={(RealityOcclusionMeshRenderer.CoverageGateAllowed ? 1 : 0)} " +
                      $"gate={RealityOcclusionMeshRenderer.CurrentCoverageGated} " +
                      $"meshDepth={(RealityOcclusionMeshRenderer.MeshDepthActive ? 1 : 0)} " +
                      $"depthAvailable={(manager != null && manager.IsDepthAvailable ? 1 : 0)} " +
                      $"depthCurrent={(manager != null && manager.IsDepthAvailable ? 1 : 0)} " +
                      $"depthLastFrame={(manager != null && manager.IsDepthAvailable ? Time.frameCount : -2)} " +
                      $"compositeFrames={compositeFrames} compositeAge={CompositeAgeFrames} " +
                      $"nativeMeshes={RealityOcclusionMeshRenderer.ActiveNativeMeshCount} " +
                      $"frame={Time.frameCount} camera={Describe(camera)}");
        }

        private static string Describe(Camera camera) => camera == null
            ? "none"
            : $"({camera.transform.position.x:F2},{camera.transform.position.y:F2}," +
              $"{camera.transform.position.z:F2})";

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
                // Composite opaque virtual geometry before transparent HUD rendering.
                // HUD shaders deliberately ignore scene and environment depth.
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
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
                if (!reportedExecution)
                {
                    reportedExecution = true;
                    Debug.Log($"MATESOUL_OCCLUSION_PASS frame={Time.frameCount} " +
                              $"alphaPreserved={Graphics.preserveFramebufferAlpha} " +
                              $"format={renderingData.cameraData.cameraTargetDescriptor.graphicsFormat}");
                }
                compositeFrames++;
                lastCompositeFrame = Time.frameCount;
                var target = colorTarget != null ? colorTarget.rt : null;
                if (target != null && target.IsCreated())
                {
                    lastColorTarget = target;
                    lastColorTargetFrame = Time.frameCount;
                }
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
