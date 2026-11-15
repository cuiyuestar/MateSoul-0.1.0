using System;
using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.Rendering;

namespace MateSoul.Runtime
{
    /// <summary>
    /// 环境深度遮挡的专用诊断。把 SDK 发布的前处理深度、重投影矩阵，与角色头部/胸口/脚部
    /// 采样点在同一时刻的场景深度并排记下来，用来区分两种可能：
    /// 真实表面确实比角色更近，或重投影/阈值把角色误判成被遮挡。
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class EnvironmentDepthDiagnostics : MonoBehaviour
    {
        private static readonly int DepthTextureId = Shader.PropertyToID("_EnvironmentDepthTexture");
        private static readonly int PreprocessedTextureId =
            Shader.PropertyToID("_PreprocessedEnvironmentDepthTexture");
        private static readonly int ZBufferParamsId =
            Shader.PropertyToID("_EnvironmentDepthZBufferParams");
        private static readonly int ReprojectionMatricesId =
            Shader.PropertyToID("_EnvironmentDepthReprojectionMatrices");
        private static readonly int OcclusionEnabledId =
            Shader.PropertyToID("_EnvironmentOcclusionEnabled");
        private const string Prefix = "MATESOUL_OCCLUSION_ENV";
        // 与 RealityEnvironmentDepth.hlsl 的前置判断保持一致，便于日志里直接复算结论。
        private const float RelativeErrorScale = 0.015f;
        private const float ProbeBorder = 0.01f;
        private const float HeadInset = 0.10f;
        private const float ChestHeight = 0.65f;
        private const float FeetInset = 0.03f;
        private const float ProbeViewDistance = 2f;
        private const float StateInterval = 1f;
        private const float RelaxedStateInterval = 5f;
        private const float MatrixInterval = 10f;
        private const float ReadbackInterval = 2f;
        private const float AlphaReadbackInterval = 2f;
        private const float RendererRefreshInterval = 5f;
        private const string AlphaPrefix = "MATESOUL_OCCLUSION_ALPHA";

        private readonly Vector3[] probeWorlds = new Vector3[4];
        private readonly bool[] probeReady = new bool[4];
        private readonly Vector2[] probeUvs = new Vector2[4];
        private readonly float[] probeSceneDepths = new float[4];
        private readonly string[] probeNames = { "view", "head", "chest", "feet" };
        private Camera viewCamera;
        private Transform character;
        private Renderer[] characterRenderers = Array.Empty<Renderer>();
        private float nextState, nextMatrix, nextReadback, nextRendererRefresh;
        private bool readbackPending, readbackErrorLogged, readbackDataErrorLogged;
        private int readbackCount;
        // 合成只改 eye buffer 的 alpha：把角色屏幕区域读回来，才能区分
        // 「遮罩没写进画面」与「写进去了但合成器没采用」。
        private float nextAlphaReadback;
        private bool alphaPending, alphaErrorLogged, alphaTargetMissingLogged,
            alphaRectMissingLogged, alphaDescriptorLogged;
        private RenderTexture alphaScratch;
        private int alphaCount;
        private int lastProbedCount, lastOccludedCount;

        public static void Install(Camera camera, Transform character)
        {
            if (Application.isEditor || camera == null) return;
            var diagnostics = camera.GetComponent<EnvironmentDepthDiagnostics>() ??
                              camera.gameObject.AddComponent<EnvironmentDepthDiagnostics>();
            diagnostics.viewCamera = camera;
            diagnostics.character = character;
            Debug.Log($"{Prefix} install camera={Format(camera.transform.position)} " +
                      $"character={(character != null ? character.name : "none")}");
        }

        private void LateUpdate()
        {
            var now = Time.unscaledTime;
            var active = RealityOcclusionMeshRenderer.EnvironmentOcclusionAllowed;
            if (now >= nextState)
            {
                nextState = now + (active ? StateInterval : RelaxedStateInterval);
                ReportState(active);
                if (active)
                {
                    CollectProbes();
                    ReportProbes();
                }
            }
            if (now >= nextMatrix)
            {
                nextMatrix = now + MatrixInterval;
                ReportMatrices();
            }
            if (active && now >= nextReadback)
            {
                nextReadback = now + ReadbackInterval;
                RequestReadback();
            }
            if (active && now >= nextAlphaReadback)
            {
                nextAlphaReadback = now + AlphaReadbackInterval;
                RequestAlphaReadback();
            }
        }

        private void ReportState(bool active)
        {
            var depth = Shader.GetGlobalTexture(DepthTextureId);
            var preprocessed = Shader.GetGlobalTexture(PreprocessedTextureId);
            var zbuffer = Shader.GetGlobalVector(ZBufferParamsId);
            // 深度流是否当前帧、环境合成最近一次执行距今多少帧，是「同一场景
            // 时好时坏」最先要排除的两环；原来这里只有纹理格式，无法判断。
            var manager = viewCamera != null
                ? viewCamera.GetComponentInParent<PXR_EnvironmentDepthManager>()
                : null;
            var compositeAge = RealityEnvironmentOcclusionFeature.CompositeAgeFrames;
            var gate = RealityOcclusionMeshRenderer.CurrentCoverageGated;
            Debug.Log($"{Prefix} state envOcclusion={(active ? 1 : 0)} " +
                      $"enabledFlag={Shader.GetGlobalFloat(OcclusionEnabledId):F0} " +
                      $"depth={Describe(depth)} preprocessed={Describe(preprocessed)} " +
                      $"depthAvailable={(manager != null && manager.IsDepthAvailable ? 1 : 0)} " +
                      $"depthCurrent={(manager != null && manager.IsDepthAvailable ? 1 : 0)} " +
                      $"depthLastFrame={(manager != null && manager.IsDepthAvailable ? Time.frameCount : -2)} " +
                      $"compositeAge={compositeAge} " +
                      $"compositeFrames={RealityEnvironmentOcclusionFeature.CompositeFrames} " +
                      $"gate={gate} meshDepth={(RealityOcclusionMeshRenderer.MeshDepthActive ? 1 : 0)} " +
                      $"nativeMeshes={RealityOcclusionMeshRenderer.ActiveNativeMeshCount} " +
                      $"zbuf=({zbuffer.x:F4},{zbuffer.y:F4}) " +
                      $"camera={Format(viewCamera.transform.position)} " +
                      $"frame={Time.frameCount}");
        }

        private void ReportMatrices()
        {
            var matrices = Shader.GetGlobalMatrixArray(ReprojectionMatricesId);
            if (matrices == null) return;
            for (var eye = 0; eye < matrices.Length && eye < 2; eye++)
            {
                var m = matrices[eye];
                Debug.Log($"{Prefix} matrix eye={eye} " +
                          $"m00={m.m00:F4} m02={m.m02:F4} m11={m.m11:F4} m12={m.m12:F4} " +
                          $"m22={m.m22:F4} m23={m.m23:F4} " +
                          $"t=({m.m03:F3},{m.m13:F3},{m.m23:F3}) " +
                          $"w=({m.m30:F4},{m.m31:F4},{m.m32:F4},{m.m33:F4})");
            }
        }

        private void CollectProbes()
        {
            for (var i = 0; i < probeReady.Length; i++) probeReady[i] = false;
            var cameraTransform = viewCamera.transform;
            probeWorlds[0] = cameraTransform.position + cameraTransform.forward * ProbeViewDistance;
            probeReady[0] = true;
            if (!TryGetCharacterBounds(out var bounds)) return;
            var center = bounds.center;
            var size = bounds.size;
            probeWorlds[1] = new Vector3(center.x, bounds.max.y - size.y * HeadInset, center.z);
            probeWorlds[2] = new Vector3(center.x, bounds.min.y + size.y * ChestHeight, center.z);
            probeWorlds[3] = new Vector3(center.x, bounds.min.y + size.y * FeetInset, center.z);
            for (var i = 1; i < probeReady.Length; i++) probeReady[i] = true;
        }

        private bool TryGetCharacterBounds(out Bounds bounds)
        {
            bounds = default;
            if (character == null) return false;
            if (characterRenderers.Length == 0 || Time.unscaledTime >= nextRendererRefresh)
            {
                nextRendererRefresh = Time.unscaledTime + RendererRefreshInterval;
                characterRenderers = character.GetComponentsInChildren<Renderer>();
            }
            var any = false;
            foreach (var renderer in characterRenderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return any;
        }

        private void ReportProbes()
        {
            var matrices = Shader.GetGlobalMatrixArray(ReprojectionMatricesId);
            if (matrices == null || matrices.Length == 0) return;
            var zbuffer = Shader.GetGlobalVector(ZBufferParamsId);
            for (var eye = 0; eye < matrices.Length && eye < 2; eye++)
            {
                for (var i = 0; i < probeWorlds.Length; i++)
                {
                    if (!probeReady[i]) continue;
                    var world = probeWorlds[i];
                    var screen = viewCamera.WorldToViewportPoint(world);
                    if (!Project(world, matrices[eye], zbuffer,
                            out var uv, out var inBounds, out var sceneDepth))
                    {
                        Debug.Log($"{Prefix} probe eye={eye} name={probeNames[i]} invalid=1");
                        continue;
                    }
                    if (eye == 0)
                    {
                        probeUvs[i] = uv;
                        probeSceneDepths[i] = sceneDepth;
                    }
                    Debug.Log($"{Prefix} probe eye={eye} name={probeNames[i]} " +
                              $"world={Format(world)} screen=({screen.x:F3},{screen.y:F3}) " +
                              $"uv=({uv.x:F3},{uv.y:F3}) inBounds={(inBounds ? 1 : 0)} " +
                              $"duv=({uv.x - screen.x:F3},{uv.y - screen.y:F3}) " +
                              $"sceneDist={sceneDepth:F2}");
                }
            }
        }

        private static bool Project(Vector3 world, Matrix4x4 matrix, Vector4 zbuffer,
            out Vector2 uv, out bool inBounds, out float sceneDepth)
        {
            uv = default;
            inBounds = false;
            sceneDepth = 0f;
            var clip = matrix * new Vector4(world.x, world.y, world.z, 1f);
            if (Mathf.Abs(clip.w) < 1e-5f) return false;
            var x = clip.x / clip.w;
            var y = clip.y / clip.w;
            var z = clip.z / clip.w;
            uv = new Vector2((x + 1f) * .5f, (y + 1f) * .5f);
            var denominator = z + zbuffer.y;
            if (Mathf.Abs(denominator) < 1e-6f) return false;
            sceneDepth = zbuffer.x / denominator;
            inBounds = z >= -1f && z <= 1f &&
                       uv.x >= ProbeBorder && uv.x <= 1f - ProbeBorder &&
                       uv.y >= ProbeBorder && uv.y <= 1f - ProbeBorder;
            return true;
        }

        private void RequestReadback()
        {
            if (readbackPending || !SystemInfo.supportsAsyncGPUReadback) return;
            if (!(Shader.GetGlobalTexture(PreprocessedTextureId) is RenderTexture texture) ||
                !texture.IsCreated() || texture.dimension != TextureDimension.Tex2DArray ||
                texture.volumeDepth < 1 || texture.width < 2 || texture.height < 2)
                return;
            readbackPending = true;
            var width = texture.width;
            var height = texture.height;
            const int eye = 0;
            // 采样点在请求时刻快照，避免读回回调到达时角色已经移动导致 uv 与深度错配。
            var uvs = (Vector2[])probeUvs.Clone();
            var sceneDepths = (float[])probeSceneDepths.Clone();
            var ready = (bool[])probeReady.Clone();
            AsyncGPUReadback.Request(texture, 0, 0, width, 0, height, eye, 1,
                TextureFormat.RGBAFloat, request =>
            {
                readbackPending = false;
                if (request.hasError)
                {
                    if (!readbackErrorLogged)
                    {
                        readbackErrorLogged = true;
                        Debug.Log($"{Prefix} readback error=1 size={width}x{height} eye={eye}");
                    }
                    return;
                }
                try
                {
                    readbackCount++;
                    ReportReadback(request.GetData<Vector4>(), width, height, eye,
                        uvs, sceneDepths, ready);
                }
                catch (Exception error)
                {
                    if (!readbackDataErrorLogged)
                    {
                        readbackDataErrorLogged = true;
                        Debug.Log($"{Prefix} readback data error={error.GetType().Name} " +
                                  $"message={error.Message}");
                    }
                }
            });
        }

        private void ReportReadback(Unity.Collections.NativeArray<Vector4> samples,
            int width, int height, int eye,
            Vector2[] uvs, float[] sceneDepths, bool[] ready)
        {
            var zbuffer = Shader.GetGlobalVector(ZBufferParamsId);
            var valid = 0;
            var minimum = float.MaxValue;
            var maximum = float.MinValue;
            foreach (var sample in samples)
            {
                if (!TryLinearize(sample, zbuffer, out var near, out var far)) continue;
                valid++;
                minimum = Mathf.Min(minimum, near);
                maximum = Mathf.Max(maximum, far);
            }
            var total = Mathf.Max(1, samples.Length);
            Debug.Log($"{Prefix} readback eye={eye} size={width}x{height} " +
                      $"samples={readbackCount} valid={valid / (float)total:F3} " +
                      $"depthMin={(valid > 0 ? minimum : 0f):F2} " +
                      $"depthMax={(valid > 0 ? maximum : 0f):F2} " +
                      $"camera={Format(viewCamera.transform.position)}");
            var probedCount = 0;
            var occludedProbes = 0;
            for (var i = 0; i < uvs.Length; i++)
            {
                if (!ready[i]) continue;
                var uv = uvs[i];
                var sceneDepth = sceneDepths[i];
                if (!TrySampleBlock(samples, width, height, uv, zbuffer,
                        out var nearDepth, out var farDepth, out var rawNear, out var rawFar))
                {
                    Debug.Log($"{Prefix} envprobe eye={eye} name={probeNames[i]} " +
                              $"uv=({uv.x:F3},{uv.y:F3}) invalidBlock=1");
                    continue;
                }
                var threshold = sceneDepth * (1f - RelativeErrorScale);
                var occluded = nearDepth < threshold;
                if (occluded) occludedProbes++;
                probedCount++;
                Debug.Log($"{Prefix} envprobe eye={eye} name={probeNames[i]} " +
                          $"uv=({uv.x:F3},{uv.y:F3}) raw=({rawNear:F3},{rawFar:F3}) " +
                          $"envNear={nearDepth:F2} envFar={farDepth:F2} " +
                          $"sceneDist={sceneDepth:F2} margin={sceneDepth - nearDepth:F2} " +
                          $"verdict={(occluded ? "occluded" : "visible")}");
            }
            ReportVerdict(probedCount, occludedProbes);
        }

        // 合成 pass 写在 eye buffer 的 alpha 里；这里把角色所在屏幕矩形读回来，
        // 直接看 alpha 有没有被写成 0。
        private void RequestAlphaReadback()
        {
            if (alphaPending || !SystemInfo.supportsAsyncGPUReadback) return;
            var target = RealityEnvironmentOcclusionFeature.LastColorTarget;
            var age = RealityEnvironmentOcclusionFeature.CompositeAgeFrames;
            if (target == null || !target.IsCreated() ||
                target.width < 8 || target.height < 8)
            {
                if (!alphaTargetMissingLogged)
                {
                    alphaTargetMissingLogged = true;
                    Debug.Log($"{AlphaPrefix} note=no-color-target age={age} " +
                              $"target={(target != null && target.IsCreated() ? $"{target.width}x{target.height}" : "null")}");
                }
                return;
            }
            if (!alphaDescriptorLogged)
            {
                alphaDescriptorLogged = true;
                Debug.Log($"{AlphaPrefix} target size={target.width}x{target.height} " +
                          $"dim={target.dimension} volume={target.volumeDepth} " +
                          $"msaa={target.antiAliasing} format={target.graphicsFormat} " +
                          $"preserveAlpha={(Graphics.preserveFramebufferAlpha ? 1 : 0)}");
            }
            if (!TryProjectCharacterRect(target.width, target.height,
                    out var x, out var y, out var rectWidth, out var rectHeight))
            {
                if (!alphaRectMissingLogged)
                {
                    alphaRectMissingLogged = true;
                    Debug.Log($"{AlphaPrefix} note=no-rect age={age} " +
                              $"target={target.width}x{target.height}");
                }
                return;
            }
            var scratchWidth = Mathf.Clamp(rectWidth, 8, 256);
            var scratchHeight = Mathf.Clamp(rectHeight, 8, 256);
            var arraySlices = target.dimension == TextureDimension.Tex2DArray
                ? Mathf.Max(1, target.volumeDepth)
                : 1;
            EnsureAlphaScratch(scratchWidth, scratchHeight, arraySlices);
            Graphics.Blit(target, alphaScratch,
                new Vector2(rectWidth / (float)target.width, rectHeight / (float)target.height),
                new Vector2(x / (float)target.width, y / (float)target.height));
            alphaPending = true;
            var probed = lastProbedCount;
            var occluded = lastOccludedCount;
            var gate = RealityOcclusionMeshRenderer.CurrentCoverageGated;
            var scratch = alphaScratch;
            if (arraySlices > 1)
            {
                AsyncGPUReadback.Request(scratch, 0, 0, scratchWidth, 0, scratchHeight,
                    0, 1, TextureFormat.RGBA32, request => HandleAlpha(request, x, y,
                        rectWidth, rectHeight, probed, occluded, gate, age));
            }
            else
            {
                AsyncGPUReadback.Request(scratch, 0, TextureFormat.RGBA32,
                    request => HandleAlpha(request, x, y, rectWidth, rectHeight,
                        probed, occluded, gate, age));
            }
        }

        private void EnsureAlphaScratch(int width, int height, int slices)
        {
            if (alphaScratch != null && alphaScratch.IsCreated() &&
                alphaScratch.width == width && alphaScratch.height == height &&
                alphaScratch.volumeDepth == slices) return;
            if (alphaScratch != null)
            {
                alphaScratch.Release();
                Destroy(alphaScratch);
            }
            alphaScratch = new RenderTexture(width, height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "MateSoul Alpha Probe",
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                dimension = slices > 1 ? TextureDimension.Tex2DArray : TextureDimension.Tex2D,
                volumeDepth = slices,
                antiAliasing = 1
            };
            alphaScratch.Create();
        }

        private void HandleAlpha(AsyncGPUReadbackRequest request,
            int x, int y, int rectWidth, int rectHeight,
            int probed, int occluded, int gate, int age)
        {
            alphaPending = false;
            if (request.hasError)
            {
                if (!alphaErrorLogged)
                {
                    alphaErrorLogged = true;
                    Debug.Log($"{AlphaPrefix} note=readback-error " +
                              $"rect=({x},{y},{rectWidth},{rectHeight}) age={age}");
                }
                return;
            }
            try
            {
                alphaCount++;
                ReportAlpha(request.GetData<Color32>(), x, y, rectWidth, rectHeight,
                    probed, occluded, gate, age);
            }
            catch (Exception error)
            {
                if (!alphaErrorLogged)
                {
                    alphaErrorLogged = true;
                    Debug.Log($"{AlphaPrefix} readback data error={error.GetType().Name} " +
                              $"message={error.Message}");
                }
            }
        }

        private void ReportAlpha(Unity.Collections.NativeArray<Color32> pixels,
            int x, int y, int rectWidth, int rectHeight,
            int probed, int occluded, int gate, int age)
        {
            var total = Mathf.Max(1, pixels.Length);
            long alphaSum = 0, rgbSum = 0;
            var zero = 0;
            var full = 0;
            foreach (var pixel in pixels)
            {
                alphaSum += pixel.a;
                rgbSum += (pixel.r + pixel.g + pixel.b) / 3;
                if (pixel.a < 32) zero++;
                else if (pixel.a > 223) full++;
            }
            var zeroRatio = zero / (float)total;
            var fullRatio = full / (float)total;
            var mean = alphaSum / (float)total / 255f;
            string note;
            if (zeroRatio >= .15f) note = "alpha-mask-present";
            else if (occluded >= 2 && zeroRatio < .05f) note = "alpha-mask-missing";
            else note = "alpha-mask-weak";
            Debug.Log($"{AlphaPrefix} rect=({x},{y},{rectWidth},{rectHeight}) px={total} " +
                      $"samples={alphaCount} mean={mean:F3} zero={zeroRatio:F3} " +
                      $"full={fullRatio:F3} rgbMean={rgbSum / (float)total / 255f:F3} " +
                      $"occludedProbes={occluded}/{probed} gate={gate} envPassAge={age} " +
                      $"preserveAlpha={(Graphics.preserveFramebufferAlpha ? 1 : 0)} " +
                      $"note={note} camera={Format(viewCamera.transform.position)} " +
                      $"frame={Time.frameCount}");
        }

        // 角色包围盒 8 个角点投到视口，得到需要读回的最小屏幕矩形。
        private bool TryProjectCharacterRect(int width, int height,
            out int x, out int y, out int rectWidth, out int rectHeight)
        {
            x = 0;
            y = 0;
            rectWidth = 0;
            rectHeight = 0;
            if (!TryGetCharacterBounds(out var bounds)) return false;
            var minX = float.MaxValue;
            var minY = float.MaxValue;
            var maxX = float.MinValue;
            var maxY = float.MinValue;
            var any = false;
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                var viewport = viewCamera.WorldToViewportPoint(corner);
                if (viewport.z <= .05f) continue;
                any = true;
                minX = Mathf.Min(minX, viewport.x);
                minY = Mathf.Min(minY, viewport.y);
                maxX = Mathf.Max(maxX, viewport.x);
                maxY = Mathf.Max(maxY, viewport.y);
            }
            if (!any) return false;
            minX = Mathf.Clamp01(minX);
            minY = Mathf.Clamp01(minY);
            maxX = Mathf.Clamp01(maxX);
            maxY = Mathf.Clamp01(maxY);
            if (maxX <= minX || maxY <= minY) return false;
            x = Mathf.Clamp(Mathf.FloorToInt(minX * width), 0, width - 1);
            y = Mathf.Clamp(Mathf.FloorToInt(minY * height), 0, height - 1);
            rectWidth = Mathf.Clamp(Mathf.CeilToInt(maxX * width) - x, 1, width - x);
            rectHeight = Mathf.Clamp(Mathf.CeilToInt(maxY * height) - y, 1, height - y);
            return rectWidth >= 8 && rectHeight >= 8;
        }

        private void OnDestroy()
        {
            if (alphaScratch == null) return;
            alphaScratch.Release();
            Destroy(alphaScratch);
            alphaScratch = null;
        }

        // 单行结论：把「开关 / 门控 / 合成是否在跑 / 探针判定」放在同一时刻，
        // 这是本次排查最缺的一环。
        private void ReportVerdict(int probed, int occluded)
        {
            lastProbedCount = probed;
            lastOccludedCount = occluded;
            var envAllowed = RealityOcclusionMeshRenderer.EnvironmentOcclusionAllowed;
            var gate = RealityOcclusionMeshRenderer.CurrentCoverageGated;
            var meshDepth = RealityOcclusionMeshRenderer.MeshDepthActive;
            var age = RealityEnvironmentOcclusionFeature.CompositeAgeFrames;
            var manager = viewCamera != null
                ? viewCamera.GetComponentInParent<PXR_EnvironmentDepthManager>()
                : null;
            string note;
            if (!envAllowed) note = "env-flag-off";
            else if (gate == 1 && meshDepth) note = "env-suppressed-by-coverage-gate";
            else if (age < 0 || age > 4) note = "env-pass-not-running";
            else note = "env-pass-running";
            Debug.Log($"MATESOUL_OCCLUSION_VERDICT envAllowed={(envAllowed ? 1 : 0)} " +
                      $"gate={gate} meshDepth={(meshDepth ? 1 : 0)} " +
                      $"envPassAge={age} " +
                      $"depthCurrent={(manager != null && manager.IsDepthAvailable ? 1 : 0)} " +
                      $"nativeMeshes={RealityOcclusionMeshRenderer.ActiveNativeMeshCount} " +
                      $"occludedProbes={occluded}/{probed} note={note} " +
                      $"camera={Format(viewCamera.transform.position)} " +
                      $"frame={Time.frameCount}");
        }

        // 与 DepthPreprocessing.shader 一致：前处理纹理里 x=1-minDepth、y=1-maxDepth，
        // 合成时取相邻四个 texel 的最小/最大编码深度。
        private static bool TrySampleBlock(Unity.Collections.NativeArray<Vector4> samples,
            int width, int height, Vector2 uv, Vector4 zbuffer,
            out float nearDepth, out float farDepth, out float rawNear, out float rawFar)
        {
            nearDepth = 0f;
            farDepth = 0f;
            rawNear = 0f;
            rawFar = 0f;
            if (!(uv.x > 0f && uv.x < 1f && uv.y > 0f && uv.y < 1f)) return false;
            var x = Mathf.Clamp(Mathf.FloorToInt(uv.x * width - 1f), 0, width - 2);
            var y = Mathf.Clamp(Mathf.FloorToInt(uv.y * height - 1f), 0, height - 2);
            var minimum = float.MaxValue;
            var maximum = float.MinValue;
            for (var dy = 0; dy < 2; dy++)
            for (var dx = 0; dx < 2; dx++)
            {
                var sample = samples[(y + dy) * width + x + dx];
                if (!IsSampleValid(sample)) return false;
                minimum = Mathf.Min(minimum, 1f - sample.x);
                maximum = Mathf.Max(maximum, 1f - sample.y);
            }
            rawNear = minimum;
            rawFar = maximum;
            var nearDenominator = minimum * 2f - 1f + zbuffer.y;
            var farDenominator = maximum * 2f - 1f + zbuffer.y;
            if (Mathf.Abs(nearDenominator) < 1e-6f || Mathf.Abs(farDenominator) < 1e-6f)
                return false;
            nearDepth = zbuffer.x / nearDenominator;
            farDepth = zbuffer.x / farDenominator;
            return true;
        }

        private static bool TryLinearize(Vector4 sample, Vector4 zbuffer,
            out float nearDepth, out float farDepth)
        {
            nearDepth = 0f;
            farDepth = 0f;
            if (!IsSampleValid(sample)) return false;
            var near = 1f - sample.x;
            var far = 1f - sample.y;
            var nearDenominator = near * 2f - 1f + zbuffer.y;
            var farDenominator = far * 2f - 1f + zbuffer.y;
            if (Mathf.Abs(nearDenominator) < 1e-6f || Mathf.Abs(farDenominator) < 1e-6f)
                return false;
            nearDepth = zbuffer.x / nearDenominator;
            farDepth = zbuffer.x / farDenominator;
            return true;
        }

        // 前处理 pass 用 float4(0,0,0,-1) 标记未测量区域。
        private static bool IsSampleValid(Vector4 sample) =>
            sample.z > -0.5f && sample.x > 0f && sample.x < 1f &&
            sample.y > 0f && sample.y < 1f;

        private static string Describe(Texture texture)
        {
            if (texture == null) return "null";
            if (texture is RenderTexture renderTexture)
                return $"{renderTexture.width}x{renderTexture.height}x{renderTexture.volumeDepth}:" +
                       $"{renderTexture.graphicsFormat}";
            return $"{texture.width}x{texture.height}:{texture.graphicsFormat}";
        }

        private static string Format(Vector3 value) =>
            $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }
}
