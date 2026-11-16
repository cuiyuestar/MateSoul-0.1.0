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
        public const string SourcePath =
            "Assets/MateSoul/Runtime/Resources/MateSoulUI/NotoSansCJKsc-Regular.otf";
        public const string AssetPath =
            "Assets/MateSoul/Runtime/Resources/MateSoulUI/NotoSansCJKsc-SDF.asset";
        public const string DynamicAssetPath =
            "Assets/MateSoul/Runtime/Resources/MateSoulUI/NotoSansCJKsc-Dynamic-SDF.asset";
        private const string TmpSettingsPath =
            "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        private const string ContentRoot = "Assets/MateSoul/Content";
        private const string WindowCharacters = "‹›“”「」→～∞";

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
            AssetDatabase.DeleteAsset(DynamicAssetPath);
            var fontAsset = TMP_FontAsset.CreateFontAsset(source, 96, 10,
                GlyphRenderMode.SDFAA, 4096, 4096, AtlasPopulationMode.Dynamic, true);
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
            var requiredCharacters = CollectRequiredCharacters();
            if (!fontAsset.TryAddCharacters(requiredCharacters,
                    out var missingCharacters) && !string.IsNullOrEmpty(missingCharacters))
                throw new InvalidOperationException(
                    $"The UI font is missing required characters: {missingCharacters}");
            UnityEngine.Debug.Log(
                $"MATESOUL_FONT baked={requiredCharacters.Length} " +
                $"atlases={fontAsset.atlasTextures.Length} " +
                $"missing={missingCharacters?.Length ?? 0}");

            var alphaAtlases = fontAsset.atlasTextures;
            var rgbaAtlases = new Texture2D[alphaAtlases.Length];
            for (var index = 0; index < alphaAtlases.Length; index++)
            {
                var alphaAtlas = alphaAtlases[index];
                if (alphaAtlas == null)
                {
                    var blank = new Texture2D(4096, 4096, TextureFormat.RGBA32, false, true)
                    {
                        name = $"NotoSansCJKsc-Regular SDF Atlas {index}",
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp
                    };
                    blank.SetPixels32(new Color32[4096 * 4096]);
                    blank.Apply(false, false);
                    rgbaAtlases[index] = blank;
                    continue;
                }
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

            var dynamicFontAsset = TMP_FontAsset.CreateFontAsset(source, 72, 9,
                GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            if (dynamicFontAsset == null)
                throw new InvalidOperationException("Unable to create the dynamic UI fallback font asset.");
            dynamicFontAsset.name = "NotoSansCJKsc-Regular Dynamic SDF";
            dynamicFontAsset.material.name = "NotoSansCJKsc-Regular Dynamic SDF Material";
            dynamicFontAsset.material.shader = overlayShader;
            dynamicFontAsset.isMultiAtlasTexturesEnabled = true;
            dynamicFontAsset.atlasTextures[0].name =
                "NotoSansCJKsc-Regular Dynamic SDF Atlas 0";

            AssetDatabase.CreateAsset(fontAsset, AssetPath);
            AssetDatabase.CreateAsset(dynamicFontAsset, DynamicAssetPath);
            fontAsset.fallbackFontAssetTable = new List<TMP_FontAsset> { dynamicFontAsset };
            for (var index = 0; index < rgbaAtlases.Length; index++)
            {
                AssetDatabase.AddObjectToAsset(rgbaAtlases[index], fontAsset);
                EditorUtility.SetDirty(rgbaAtlases[index]);
            }
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            foreach (var atlas in dynamicFontAsset.atlasTextures)
            {
                AssetDatabase.AddObjectToAsset(atlas, dynamicFontAsset);
                EditorUtility.SetDirty(atlas);
            }
            AssetDatabase.AddObjectToAsset(dynamicFontAsset.material, dynamicFontAsset);
            ConfigureTmpSettings(fontAsset, dynamicFontAsset);
            EditorUtility.SetDirty(fontAsset);
            EditorUtility.SetDirty(fontAsset.material);
            EditorUtility.SetDirty(dynamicFontAsset);
            EditorUtility.SetDirty(dynamicFontAsset.material);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(DynamicAssetPath, ImportAssetOptions.ForceSynchronousImport);
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

            // Bake every non-ASCII character used in C# string literals so fixed hints and
            // labels never depend on runtime glyph addition from the dynamic fallback.
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }))
            {
                var scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!scriptPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                AddLiteralCharacters(characters, System.IO.File.ReadAllText(scriptPath));
            }
            return new string(characters.ToArray());
        }

        private static void AddLiteralCharacters(ISet<char> characters, string source)
        {
            foreach (var line in source.Split('\n'))
            {
                var inString = false;
                foreach (var character in line)
                {
                    if (character == '"') inString = !inString;
                    else if (inString && character > 0x7E) characters.Add(character);
                }
            }
        }

        private static void AddCharacters(ISet<char> characters, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            foreach (var character in value)
                characters.Add(character);
        }

        private static void ConfigureTmpSettings(
            TMP_FontAsset primary, TMP_FontAsset dynamicFallback)
        {
            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
            if (settings == null)
                throw new InvalidOperationException($"{TmpSettingsPath} is missing.");
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("m_defaultFontAsset").objectReferenceValue = primary;
            var fallbacks = serialized.FindProperty("m_fallbackFontAssets");
            fallbacks.arraySize = 1;
            fallbacks.GetArrayElementAtIndex(0).objectReferenceValue = dynamicFallback;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
        }

        private static bool TmpSettingsAreConfigured(
            TMP_FontAsset primary, TMP_FontAsset dynamicFallback)
        {
            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
            if (settings == null) return false;
            var serialized = new SerializedObject(settings);
            var defaultFont = serialized.FindProperty("m_defaultFontAsset");
            var fallbacks = serialized.FindProperty("m_fallbackFontAssets");
            return defaultFont.objectReferenceValue == primary &&
                   fallbacks.arraySize == 1 &&
                   fallbacks.GetArrayElementAtIndex(0).objectReferenceValue == dynamicFallback;
        }

        public static void Ensure()
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
            var dynamicFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicAssetPath);
            var source = AssetDatabase.LoadAssetAtPath<Font>(SourcePath);
            if (fontAsset == null || fontAsset.atlasTexture == null ||
                fontAsset.atlasTexture.width != 4096 || fontAsset.atlasTexture.height != 4096 ||
                fontAsset.atlasTexture.format != TextureFormat.RGBA32 ||
                fontAsset.atlasPopulationMode != AtlasPopulationMode.Static ||
                fontAsset.material.shader.name != AlwaysOnTopUi.TextShaderName ||
                !fontAsset.HasCharacters(CollectRequiredCharacters(), out _, false, false) ||
                dynamicFontAsset == null || dynamicFontAsset.sourceFontFile != source ||
                dynamicFontAsset.atlasPopulationMode != AtlasPopulationMode.Dynamic ||
                !dynamicFontAsset.isMultiAtlasTexturesEnabled ||
                dynamicFontAsset.material.shader.name != AlwaysOnTopUi.TextShaderName ||
                fontAsset.fallbackFontAssetTable == null ||
                fontAsset.fallbackFontAssetTable.Count != 1 ||
                fontAsset.fallbackFontAssetTable[0] != dynamicFontAsset ||
                !TmpSettingsAreConfigured(fontAsset, dynamicFontAsset))
                Generate();
        }
    }
}
