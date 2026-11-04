using System;
using System.Threading;
using System.Threading.Tasks;
using ByteDance.PICO.XR;
using MateSoul.RoomNavigation;
using UnityEngine;
using UnityEngine.Rendering;

namespace MateSoul.Runtime
{
    /// <summary>Adapts existing scene illumination without changing its light direction or materials.</summary>
    [DisallowMultipleComponent]
    public sealed class RealityAmbientLighting : MonoBehaviour
    {
        private const string ReductionShader = "RealityLighting/AmbientReduction";
        private const int CubemapSize = 128;
        private const float SampleInterval = .5f;
        private const float StaleAfter = 5f;
        private const float AdaptationSeconds = 1.25f;
        private const float ReferenceLuminance = .18f;
        private static readonly int EnvironmentCube = Shader.PropertyToID("_EnvironmentCube");

        private Light mainLight;
        private Color originalLightColor;
        private float originalLightIntensity;
        private AmbientMode originalAmbientMode;
        private SphericalHarmonicsL2 originalAmbientProbe;
        private bool capturedLighting, appliedLighting, paused, sampleRequested;
        private float lastSampleTime = float.NegativeInfinity;
        private float brightness = 1f, targetBrightness = 1f;
        private Color tint = Color.white, targetTint = Color.white;
        private CancellationTokenSource lifetime;
        private Task acquisition = Task.CompletedTask;

        public bool IsAdapting { get; private set; }
        public float EnvironmentLuminance { get; private set; }

        public static RealityAmbientLighting Install(GameObject host)
        {
            if (Application.isEditor || PicoDeviceEnvironment.IsEmulator) return null;
            return host.GetComponent<RealityAmbientLighting>() ??
                   host.AddComponent<RealityAmbientLighting>();
        }

        private void OnEnable()
        {
            CaptureLighting();
            if (Application.isEditor || PicoDeviceEnvironment.IsEmulator) return;
            var previous = acquisition;
            lifetime = new CancellationTokenSource();
            sampleRequested = true;
            PXR_Manager.LightEstimationUpdated += OnLightEstimationUpdated;
            acquisition = AcquireAsync(previous, lifetime);
        }

        private void CaptureLighting()
        {
            if (capturedLighting) return;
            capturedLighting = true;
            originalAmbientMode = RenderSettings.ambientMode;
            originalAmbientProbe = RenderSettings.ambientProbe;
            mainLight = RenderSettings.sun;
            if (mainLight == null)
                foreach (var light in FindObjectsOfType<Light>())
                    if (light.isActiveAndEnabled && light.type == LightType.Directional &&
                        (mainLight == null || light.intensity > mainLight.intensity))
                        mainLight = light;
            if (mainLight != null)
            {
                originalLightColor = mainLight.color;
                originalLightIntensity = mainLight.intensity;
            }
        }

        private void OnLightEstimationUpdated() => sampleRequested = true;

        private async Task AcquireAsync(Task previous, CancellationTokenSource source)
        {
            var token = source.Token;
            Material reduction = null;
            RenderTexture output = null;
            Cubemap cube = null;
            var providerStarted = false;
            try
            {
                // A re-enabled component waits for its previous native/GPU work to release ownership.
                await previous;
                token.ThrowIfCancellationRequested();
#if UNITY_ANDROID && !UNITY_EDITOR
                // Room startup owns the permission prompt; optional lighting must not open another one.
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                        "com.picovr.permission.SPATIAL_DATA"))
                    throw new InvalidOperationException("Spatial data permission has not been granted.");
#endif
                var settings = PXR_ProjectSetting.GetProjectConfig();
                if (!settings.lightEstimation ||
                    settings.lightEstimationTextureResolution != PxrEnvironmentTextureResolution.Size128x128 ||
                    PXR_Plugin.MixedReality.UPxr_GetSenseDataProviderHandle(
                        PxrSenseDataProviderType.LightEstimation) == 0)
                    throw new InvalidOperationException("PICO light estimation provider is not configured.");
                var useVulkan = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan;
                if (!useVulkan && SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES3)
                    throw new NotSupportedException("PICO environment lighting requires Vulkan or OpenGLES3.");
                if (!SystemInfo.supportsAsyncGPUReadback ||
                    !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))
                    throw new NotSupportedException("Asynchronous HDR lighting readback is unavailable.");
                var shader = Resources.Load<Shader>(ReductionShader);
                if (shader == null || !shader.isSupported)
                    throw new InvalidOperationException("Environment lighting reduction shader is unavailable.");
                reduction = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                output = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat,
                    RenderTextureReadWrite.Linear)
                {
                    name = "Reality Ambient Lighting Sample",
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (!output.Create())
                    throw new InvalidOperationException("Environment lighting sample texture could not be created.");

                using (var startTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    startTimeout.CancelAfter(TimeSpan.FromSeconds(8));
                    var result = await PXR_MixedReality.StartSenseDataProvider(
                        PxrSenseDataProviderType.LightEstimation, startTimeout.Token);
                    if (result != PxrResult.SUCCESS)
                        throw new InvalidOperationException($"PICO light estimation start failed: {result}.");
                    providerStarted = true;
                }
                Debug.Log("MATESOUL_AMBIENT_LIGHT provider=started mode=brightness-and-tint", this);
                var nextSample = 0f;
                while (!token.IsCancellationRequested)
                {
                    if (!paused && sampleRequested && Time.realtimeSinceStartup >= nextSample)
                    {
                        sampleRequested = false;
                        nextSample = Time.realtimeSinceStartup + SampleInterval;
                        PxrLightEstimationData native = default;
                        try
                        {
                            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                            {
                                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                                var queried = await PXR_MixedReality.QueryLightEstimationDataAsync(
                                    useVulkan, timeout.Token);
                                native = queried.lightData;
                                if (queried.result != PxrResult.SUCCESS)
                                    throw new InvalidOperationException(
                                        $"PICO light estimation query failed: {queried.result}.");
                            }
                            token.ThrowIfCancellationRequested();
                            if (useVulkan)
                            {
                                if (native.vkImage == IntPtr.Zero) continue;
                                if (cube == null)
                                    cube = new Cubemap(CubemapSize, TextureFormat.RGBAHalf, false);
                                var copied = PXR_Plugin.MixedReality.UPxr_CopyLightEstimationVulkanImageToUnityTexture(
                                    cube.GetNativeTexturePtr(), (ulong)native.vkImage, CubemapSize, 1);
                                if (copied != PxrResult.SUCCESS)
                                    throw new InvalidOperationException($"PICO lighting texture copy failed: {copied}.");
                            }
                            else
                            {
                                if (native.cubemap == 0) continue;
                                if (cube == null)
                                    cube = Cubemap.CreateExternalTexture(CubemapSize, TextureFormat.RGBAHalf,
                                        false, (IntPtr)native.cubemap);
                                else cube.UpdateExternalTexture((IntPtr)native.cubemap);
                            }
                            reduction.SetTexture(EnvironmentCube, cube);
                            var previousTarget = RenderTexture.active;
                            try { Graphics.Blit(null, output, reduction); }
                            finally { RenderTexture.active = previousTarget; }
                            var readback = AsyncGPUReadback.Request(output, 0, TextureFormat.RGBAFloat);
                            // Native textures remain acquired until all GPU consumers have completed.
                            while (!readback.done) await Task.Yield();
                            token.ThrowIfCancellationRequested();
                            if (readback.hasError)
                                throw new InvalidOperationException("Environment lighting GPU readback failed.");
                            if (!paused)
                            {
                                var pixels = readback.GetData<Color>();
                                AcceptSample(pixels[0], Time.realtimeSinceStartup);
                            }
                        }
                        finally
                        {
                            if (native.vkImage != IntPtr.Zero)
                                PXR_Plugin.MixedReality.UPxr_ReleaseEnvironmentTextureImageVulkan(
                                    (ulong)native.vkImage);
                            else if (native.cubemap != 0)
                                PXR_Plugin.MixedReality.UPxr_ReleaseEnvironmentTextureImage(native.cubemap);
                        }
                    }
                    await Task.Delay(50, token);
                }
            }
            catch (OperationCanceledException)
            {
                if (!token.IsCancellationRequested)
                    Debug.LogWarning("MATESOUL_AMBIENT_LIGHT unavailable=provider-timeout", this);
            }
            catch (Exception error)
            {
                Debug.LogWarning($"MATESOUL_AMBIENT_LIGHT unavailable={error.Message}", this);
            }
            finally
            {
                if (providerStarted)
                    PXR_MixedReality.StopSenseDataProvider(PxrSenseDataProviderType.LightEstimation);
                if (cube != null) Destroy(cube);
                if (output != null) { output.Release(); Destroy(output); }
                if (reduction != null) Destroy(reduction);
                source.Dispose();
                if (ReferenceEquals(lifetime, source)) lifetime = null;
            }
        }

        private bool AcceptSample(Color sample, float now)
        {
            if (!IsFinite(sample.r) || !IsFinite(sample.g) || !IsFinite(sample.b) ||
                !IsFinite(sample.a) || sample.a < .75f ||
                sample.r < 0f || sample.g < 0f || sample.b < 0f) return false;
            EnvironmentLuminance = .2126f * sample.r + .7152f * sample.g + .0722f * sample.b;
            targetBrightness = Mathf.Clamp(
                Mathf.Pow(EnvironmentLuminance / ReferenceLuminance, .25f), .4f, 1.25f);
            var divisor = Mathf.Max(EnvironmentLuminance, .001f);
            targetTint = Color.Lerp(Color.white, new Color(
                Mathf.Clamp(sample.r / divisor, .6f, 1.4f),
                Mathf.Clamp(sample.g / divisor, .6f, 1.4f),
                Mathf.Clamp(sample.b / divisor, .6f, 1.4f), 1f), .3f);
            if (EnvironmentLuminance < .001f) targetTint = Color.white;
            lastSampleTime = now;
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void Update() => UpdateLighting(Time.unscaledDeltaTime, Time.realtimeSinceStartup);

        private void UpdateLighting(float deltaTime, float now)
        {
            if (!capturedLighting || float.IsNegativeInfinity(lastSampleTime)) return;
            var available = !paused && now - lastSampleTime <= StaleAfter;
            if (available != IsAdapting)
                Debug.Log($"MATESOUL_AMBIENT_LIGHT adapting={available} " +
                          $"luminance={EnvironmentLuminance:F4} brightness={targetBrightness:F3} " +
                          $"tint={targetTint}", this);
            IsAdapting = available;
            var blend = 1f - Mathf.Exp(-Mathf.Min(deltaTime, .25f) / AdaptationSeconds);
            brightness = Mathf.Lerp(brightness, available ? targetBrightness : 1f, blend);
            tint = Color.Lerp(tint, available ? targetTint : Color.white, blend);
            if (!available && Mathf.Abs(brightness - 1f) < .001f &&
                Mathf.Abs(tint.r - 1f) + Mathf.Abs(tint.g - 1f) + Mathf.Abs(tint.b - 1f) < .001f)
            {
                RestoreLighting();
                return;
            }
            var probe = originalAmbientProbe;
            for (var channel = 0; channel < 3; channel++)
                for (var coefficient = 0; coefficient < 9; coefficient++)
                    probe[channel, coefficient] *= brightness * tint[channel];
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = probe;
            if (mainLight != null)
            {
                var original = originalLightColor.linear;
                mainLight.color = new Color(original.r * tint.r, original.g * tint.g,
                    original.b * tint.b, original.a).gamma;
                mainLight.intensity = originalLightIntensity * brightness;
            }
            appliedLighting = true;
        }

        private void OnApplicationPause(bool value)
        {
            paused = value;
            lastSampleTime = float.NegativeInfinity;
            RestoreLighting();
            if (!value) sampleRequested = true;
        }

        private void RestoreLighting()
        {
            if (appliedLighting)
            {
                RenderSettings.ambientMode = originalAmbientMode;
                RenderSettings.ambientProbe = originalAmbientProbe;
                if (mainLight != null)
                {
                    mainLight.color = originalLightColor;
                    mainLight.intensity = originalLightIntensity;
                }
                appliedLighting = false;
            }
            IsAdapting = false;
            brightness = 1f;
            tint = Color.white;
        }

        private void OnDisable()
        {
            PXR_Manager.LightEstimationUpdated -= OnLightEstimationUpdated;
            lifetime?.Cancel();
            RestoreLighting();
            capturedLighting = false;
            lastSampleTime = float.NegativeInfinity;
        }
    }
}
