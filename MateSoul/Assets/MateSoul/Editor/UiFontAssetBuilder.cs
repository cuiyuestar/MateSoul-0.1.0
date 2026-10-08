using System;
using System.Collections.Generic;
using System.Linq;
using MateSoul.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace MateSoul.Editor
{
    public static class UiFontAssetBuilder
    {
        private const string SourcePath =
            "Assets/MateSoul/Runtime/Resources/MateSoulUI/NotoSansCJKsc-Regular.otf";
        private const string AssetPath =
            "Assets/MateSoul/Runtime/Resources/MateSoulUI/NotoSansCJKsc-SDF.asset";
        private const string ContentRoot = "Assets/MateSoul/Content";
        private const string WindowCharacters = "‹›“”「」→～";

        [InitializeOnLoadMethod]
        private static void EnsureAfterScriptReload()
        {
            EditorApplication.delayCall += Ensure;
        }

        [MenuItem("MateSoul/Game/Generate UI Font Asset #&u")]
        public static void Generate()
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>(SourcePath);
            if (source == null) throw new InvalidOperationException($"{SourcePath} is missing.");

            AssetDatabase.DeleteAsset(AssetPath);
            var fontAsset = TMP_FontAsset.CreateFontAsset(source, 96, 10,
                GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            if (fontAsset == null)
                throw new InvalidOperationException("Unable to create the UI SDF font asset.");
            fontAsset.name = "NotoSansCJKsc-Regular SDF";
            fontAsset.material.name = "NotoSansCJKsc-Regular SDF Material";
            var overlayShader = Shader.Find(AlwaysOnTopUi.TextShaderName);
            if (overlayShader == null)
                throw new InvalidOperationException(
                    $"{AlwaysOnTopUi.TextShaderName} shader is missing.");
            fontAsset.material.shader = overlayShader;
            fontAsset.isMultiAtlasTexturesEnabled = true;
            fontAsset.ClearFontAssetData(false);
            if (!fontAsset.TryAddCharacters(CollectRequiredCharacters(),
                    out var missingCharacters) && !string.IsNullOrEmpty(missingCharacters))
                throw new InvalidOperationException(
                    $"The UI font is missing required characters: {missingCharacters}");

            var alphaAtlases = fontAsset.atlasTextures;
            var rgbaAtlases = new Texture2D[alphaAtlases.Length];
            for (var index = 0; index < alphaAtlases.Length; index++)
            {
                var alphaAtlas = alphaAtlases[index];
                var pixels = alphaAtlas.GetPixels32();
                for (var pixel = 0; pixel < pixels.Length; pixel++)
                    pixels[pixel] = new Color32(255, 255, 255, pixels[pixel].a);
                var rgbaAtlas = new Texture2D(alphaAtlas.width, alphaAtlas.height,
                    TextureFormat.RGBA32, false, true)
                {
                    name = $"NotoSansCJKsc-Regular SDF Atlas {index}",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                rgbaAtlas.SetPixels32(pixels);
                rgbaAtlas.Apply(false, false);
                rgbaAtlases[index] = rgbaAtlas;
            }

            fontAsset.atlasTextures = rgbaAtlases;
            var atlasCache = typeof(TMP_FontAsset).GetField("m_AtlasTexture",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            if (atlasCache == null)
                throw new InvalidOperationException("TMP atlas cache field is unavailable.");
            atlasCache.SetValue(fontAsset, rgbaAtlases[0]);
            fontAsset.material.mainTexture = rgbaAtlases[0];
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;

            AssetDatabase.CreateAsset(fontAsset, AssetPath);
            EditorUtility.SetDirty(fontAsset);
            for (var index = 0; index < rgbaAtlases.Length; index++)
            {
                AssetDatabase.AddObjectToAsset(rgbaAtlases[index], fontAsset);
                EditorUtility.SetDirty(rgbaAtlases[index]);
            }
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            EditorUtility.SetDirty(fontAsset.material);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        public static string CollectRequiredCharacters()
        {
            var characters = new SortedSet<char>(
                AlwaysOnTopUi.InitialFontCharacters.Concat(WindowCharacters));
            foreach (var guid in AssetDatabase.FindAssets(
                         "t:ItemDefinition", new[] { ContentRoot }).OrderBy(value => value))
            {
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                AddCharacters(characters, definition?.DisplayName);
            }

            var personaAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(
                GameSceneBuilder.PersonaPath);
            if (personaAsset == null)
                throw new InvalidOperationException(
                    $"{GameSceneBuilder.PersonaPath} is missing.");
            var persona = CharacterPersonaLoader.Load(personaAsset);
            AddCharacters(characters, persona.identity?.name);
            foreach (var trait in persona.personality_traits ??
                                  Array.Empty<MateSoul.Gameplay.PersonalityTraitDefinition>())
            {
                if (trait == null) continue;
                AddCharacters(characters, trait.display_name);
                foreach (var option in trait.options ??
                                       Array.Empty<MateSoul.Gameplay.PersonalityOptionDefinition>())
                    AddCharacters(characters, option?.display_name);
            }
            return new string(characters.ToArray());
        }

        private static void AddCharacters(ISet<char> characters, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            foreach (var character in value)
                characters.Add(character);
        }

        public static void Ensure()
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
            if (fontAsset == null || fontAsset.atlasTexture == null ||
                fontAsset.atlasTexture.width != 2048 || fontAsset.atlasTexture.height != 2048 ||
                fontAsset.atlasTexture.format != TextureFormat.RGBA32 ||
                fontAsset.atlasPopulationMode != AtlasPopulationMode.Static ||
                fontAsset.material.shader.name != AlwaysOnTopUi.TextShaderName ||
                !fontAsset.HasCharacters(CollectRequiredCharacters(), out _, false, false))
                Generate();
        }
    }
}
