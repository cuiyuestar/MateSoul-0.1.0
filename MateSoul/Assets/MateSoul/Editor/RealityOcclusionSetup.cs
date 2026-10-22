using System;
using System.Linq;
using MateSoul.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MateSoul.Editor
{
    public static class RealityOcclusionSetup
    {
        public const string CompositeShaderPath =
            "Assets/MateSoul/Runtime/Resources/RealityOcclusion/EnvironmentDepthComposite.shader";

        private static readonly string[] PipelinePaths =
        {
            "Assets/Settings/URP-Performant.asset",
            "Assets/Settings/URP-Balanced.asset",
            "Assets/Settings/URP-HighFidelity.asset"
        };

        private static readonly string[] RendererPaths =
        {
            "Assets/Settings/URP-Performant-Renderer.asset",
            "Assets/Settings/URP-Balanced-Renderer.asset",
            "Assets/Settings/URP-HighFidelity-Renderer.asset"
        };

        [MenuItem("MateSoul/Game/Configure Reality Occlusion")]
        public static void Configure()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(CompositeShaderPath);
            if (shader == null)
                throw new InvalidOperationException(
                    $"Environment depth composite shader is missing: {CompositeShaderPath}");

            foreach (var path in PipelinePaths)
            {
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (pipeline == null)
                    throw new InvalidOperationException($"URP pipeline asset is missing: {path}");
                pipeline.supportsCameraDepthTexture = true;
                EditorUtility.SetDirty(pipeline);
            }

            foreach (var path in RendererPaths)
                ConfigureRenderer(path, shader);

            AssetDatabase.SaveAssets();
        }

        public static bool HasFeature(ScriptableRendererData rendererData)
            => rendererData != null &&
               rendererData.rendererFeatures.Any(
                   feature => feature is RealityEnvironmentOcclusionFeature);

        private static void ConfigureRenderer(string path, Shader shader)
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            if (rendererData == null)
                throw new InvalidOperationException($"URP renderer asset is missing: {path}");

            var feature = rendererData.rendererFeatures
                .OfType<RealityEnvironmentOcclusionFeature>()
                .FirstOrDefault();
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<RealityEnvironmentOcclusionFeature>();
                feature.name = "MateSoul Reality Environment Occlusion";
                AssetDatabase.AddObjectToAsset(feature, rendererData);
                rendererData.rendererFeatures.Add(feature);

                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        feature,
                        out _,
                        out long localId))
                    throw new InvalidOperationException(
                        $"Could not persist reality occlusion feature in {path}.");

                var serializedRenderer = new SerializedObject(rendererData);
                var featureMap = serializedRenderer.FindProperty("m_RendererFeatureMap");
                featureMap.InsertArrayElementAtIndex(featureMap.arraySize);
                featureMap.GetArrayElementAtIndex(featureMap.arraySize - 1).longValue = localId;
                serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
            }

            feature.Configure(shader);
            EditorUtility.SetDirty(feature);
            rendererData.SetDirty();
            EditorUtility.SetDirty(rendererData);
        }
    }
}
