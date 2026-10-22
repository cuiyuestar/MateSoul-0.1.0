using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MateSoul.Editor
{
    public sealed class CookingEffectAssets
    {
        public AudioClip ButtonBeep { get; }
        public AudioClip InductionHum { get; }
        public AudioClip FoodSizzle { get; }
        public Material SmokeMaterial { get; }

        public CookingEffectAssets(AudioClip buttonBeep, AudioClip inductionHum,
            AudioClip foodSizzle, Material smokeMaterial)
        {
            ButtonBeep = buttonBeep;
            InductionHum = inductionHum;
            FoodSizzle = foodSizzle;
            SmokeMaterial = smokeMaterial;
        }
    }

    public static class CookingEffectsAssetBuilder
    {
        public const string Root =
            "Assets/MateSoul/Content/Resources/CookingEffects";
        public const string ButtonBeepPath = Root + "/ButtonBeep.wav";
        public const string InductionHumPath = Root + "/InductionHum.wav";
        public const string FoodSizzlePath = Root + "/FoodSizzle.wav";
        public const string SmokeTexturePath = Root + "/SmokeSoft.png";
        public const string SmokeMaterialPath = Root + "/Smoke.mat";
        private const int SampleRate = 44100;

        public static CookingEffectAssets Build()
        {
            Directory.CreateDirectory(Root);
            WriteIfChanged(ButtonBeepPath, EncodeWave(GenerateButtonBeep()));
            WriteIfChanged(InductionHumPath, EncodeWave(GenerateInductionHum()));
            WriteIfChanged(FoodSizzlePath, EncodeWave(GenerateFoodSizzle()));
            ImportAudio(ButtonBeepPath);
            ImportAudio(InductionHumPath);
            ImportAudio(FoodSizzlePath);
            var smokeMaterial = BuildSmokeMaterial();
            var result = new CookingEffectAssets(
                AssetDatabase.LoadAssetAtPath<AudioClip>(ButtonBeepPath),
                AssetDatabase.LoadAssetAtPath<AudioClip>(InductionHumPath),
                AssetDatabase.LoadAssetAtPath<AudioClip>(FoodSizzlePath),
                smokeMaterial);
            if (result.ButtonBeep == null || result.InductionHum == null ||
                result.FoodSizzle == null || result.SmokeMaterial == null)
                throw new InvalidOperationException(
                    "Cooking effect assets failed to import.");
            return result;
        }

        private static float[] GenerateButtonBeep()
        {
            const float duration = .13f;
            var samples = new float[Mathf.RoundToInt(SampleRate * duration)];
            for (var index = 0; index < samples.Length; index++)
            {
                var time = index / (float)SampleRate;
                var normalized = time / duration;
                var attack = Mathf.Clamp01(normalized / .035f);
                var envelope = attack * Mathf.Pow(1f - normalized, 2.4f);
                samples[index] = envelope * (
                    Mathf.Sin(2f * Mathf.PI * 1320f * time) * .78f +
                    Mathf.Sin(2f * Mathf.PI * 1760f * time) * .22f);
            }
            return Normalize(samples, .78f);
        }

        private static float[] GenerateInductionHum()
        {
            const float duration = 4f;
            var samples = new float[Mathf.RoundToInt(SampleRate * duration)];
            for (var index = 0; index < samples.Length; index++)
            {
                var time = index / (float)SampleRate;
                var modulation = .88f +
                                 .12f * Mathf.Sin(2f * Mathf.PI * 2f * time);
                samples[index] = modulation * (
                    Mathf.Sin(2f * Mathf.PI * 50f * time) * .34f +
                    Mathf.Sin(2f * Mathf.PI * 100f * time) * .22f +
                    Mathf.Sin(2f * Mathf.PI * 200f * time) * .13f +
                    Mathf.Sin(2f * Mathf.PI * 400f * time) * .07f +
                    Mathf.Sin(2f * Mathf.PI * 800f * time) * .035f);
            }
            return Normalize(samples, .52f);
        }

        private static float[] GenerateFoodSizzle()
        {
            const float duration = 6f;
            var samples = new float[Mathf.RoundToInt(SampleRate * duration)];
            uint state = 0x6d2b79f5;
            var lowPass = 0f;
            var crackle = 0f;
            for (var index = 0; index < samples.Length; index++)
            {
                state = state * 1664525u + 1013904223u;
                var noise = ((state >> 8) / 16777215f) * 2f - 1f;
                lowPass += (noise - lowPass) * .06f;
                var highPass = noise - lowPass;
                state = state * 1664525u + 1013904223u;
                if ((state & 0xffffu) < 78u)
                    crackle = .65f + ((state >> 16) / 65535f) * .35f;
                crackle *= .982f;
                var time = index / (float)SampleRate;
                var movement = .82f +
                               .18f * Mathf.Sin(2f * Mathf.PI * .67f * time);
                samples[index] = highPass * .38f * movement +
                                 highPass * crackle * .62f;
            }
            const int seam = 768;
            for (var index = 0; index < seam; index++)
            {
                var fade = index / (float)(seam - 1);
                samples[index] *= fade;
                samples[samples.Length - 1 - index] *= fade;
            }
            return Normalize(samples, .68f);
        }

        private static float[] Normalize(float[] samples, float peak)
        {
            var maximum = samples.Max(value => Mathf.Abs(value));
            if (maximum <= .000001f) return samples;
            var scale = peak / maximum;
            for (var index = 0; index < samples.Length; index++)
                samples[index] *= scale;
            return samples;
        }

        private static byte[] EncodeWave(float[] samples)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                var dataLength = samples.Length * sizeof(short);
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataLength);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });
                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(SampleRate);
                writer.Write(SampleRate * sizeof(short));
                writer.Write((short)sizeof(short));
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataLength);
                foreach (var sample in samples)
                    writer.Write((short)Mathf.RoundToInt(
                        Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static void ImportAudio(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
                throw new InvalidOperationException(
                    $"Cooking audio failed to import: {path}");
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = true;
            settings.preloadAudioData = true;
            importer.forceToMono = true;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        private static Material BuildSmokeMaterial()
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            texture.name = "CookingSmokeSoft";
            var pixels = new Color32[64 * 64];
            for (var y = 0; y < 64; y++)
                for (var x = 0; x < 64; x++)
                {
                    var point = new Vector2(
                        (x + .5f) / 64f * 2f - 1f,
                        (y + .5f) / 64f * 2f - 1f);
                    var alpha = Mathf.Pow(
                        Mathf.Clamp01(1f - point.sqrMagnitude), 2.2f);
                    pixels[y * 64 + x] = new Color32(
                        255, 255, 255,
                        (byte)Mathf.RoundToInt(alpha * 255f));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            WriteIfChanged(SmokeTexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(
                SmokeTexturePath, ImportAssetOptions.ForceUpdate);
            var textureImporter =
                AssetImporter.GetAtPath(SmokeTexturePath) as TextureImporter;
            if (textureImporter == null)
                throw new InvalidOperationException(
                    "Cooking smoke texture failed to import.");
            textureImporter.textureType = TextureImporterType.Default;
            textureImporter.alphaIsTransparency = true;
            textureImporter.mipmapEnabled = false;
            textureImporter.wrapMode = TextureWrapMode.Clamp;
            textureImporter.filterMode = FilterMode.Bilinear;
            textureImporter.SaveAndReimport();

            var shader = Shader.Find(
                "Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                throw new InvalidOperationException(
                    "URP particle shader is missing.");
            var material =
                AssetDatabase.LoadAssetAtPath<Material>(SmokeMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "Cooking Smoke" };
                AssetDatabase.CreateAsset(material, SmokeMaterialPath);
            }
            else material.shader = shader;
            var smokeTexture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(SmokeTexturePath);
            material.SetTexture("_BaseMap", smokeTexture);
            material.SetTexture("_MainTex", smokeTexture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", 5f);
            material.SetFloat("_DstBlend", 10f);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static void WriteIfChanged(string path, byte[] data)
        {
            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(data))
                return;
            File.WriteAllBytes(path, data);
        }
    }
}
