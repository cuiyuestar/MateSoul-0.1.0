using System;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace MateSoul.Runtime
{
    public static class AlwaysOnTopUi
    {
        public const int SortingOrder = 32760;
        public const float DynamicPixelsPerUnit = 8f;
        public const string TextShaderName = "TextMeshPro/Mobile/Distance Field Overlay";
        private const string MaterialResourcePath = "MateSoulUI/AlwaysOnTop";
        private const string FontResourcePath = "MateSoulUI/NotoSansCJKsc-SDF";
        private const string DynamicFontResourcePath = "MateSoulUI/NotoSansCJKsc-Dynamic-SDF";
        public const string InitialFontCharacters =
            "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_" +
            " ，。！？：；、·…/+-%()（）" +
            "一上下个中主二互交仍休伴位保信停先入共关内再冷准击切初删前功动势化去反发取可合名" +
            "向启品响回图在坐基备天女始子字存定容对小尚尾局岁左已幕平并床应座开式当心态性息情" +
            "想戏房手拟择按换提放散文新无明暂月有服束来果柄标栏格椅槽模正步水没活测消游点物状玩" +
            "用界留的目看眼瞄码础示称系级结统继续置聊能自色落虚装角设试话说请走起身躺输过返还" +
            "这送选里重钮闭间除陪静面页项题馈齐龄球与轮流滚每人倒瓶得分展示隐藏键双握拳";
        private static TMP_FontAsset textFont;
        private static Material textMaterial;

        public static Material CreateMaterial()
        {
            var template = Resources.Load<Material>(MaterialResourcePath);
            if (template == null || template.shader == null)
                throw new InvalidOperationException($"{MaterialResourcePath} material is missing.");
            return new Material(template)
            {
                name = "MateSoul UI Always On Top",
                hideFlags = HideFlags.DontSave
            };
        }

        public static void Configure(Canvas canvas)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = SortingOrder;
        }

        public static void ConfigureScaler(CanvasScaler scaler)
        {
            scaler.referencePixelsPerUnit = 100;
            scaler.dynamicPixelsPerUnit = DynamicPixelsPerUnit;
            var canvas = scaler.GetComponent<Canvas>();
            if (canvas != null) canvas.scaleFactor = DynamicPixelsPerUnit;
        }

        public static TMP_FontAsset TextFont
        {
            get
            {
                if (textFont != null) return textFont;
                var template = Resources.Load<TMP_FontAsset>(FontResourcePath);
                if (template == null)
                    throw new InvalidOperationException($"{FontResourcePath} font asset is missing.");
                var dynamicFallback = Resources.Load<TMP_FontAsset>(DynamicFontResourcePath);
                if (dynamicFallback == null ||
                    dynamicFallback.atlasPopulationMode != AtlasPopulationMode.Dynamic ||
                    dynamicFallback.sourceFontFile == null)
                    throw new InvalidOperationException(
                        $"{DynamicFontResourcePath} dynamic fallback font is invalid.");
                if (template.fallbackFontAssetTable == null ||
                    !template.fallbackFontAssetTable.Contains(dynamicFallback) ||
                    TMP_Settings.defaultFontAsset != template ||
                    TMP_Settings.fallbackFontAssets == null ||
                    !TMP_Settings.fallbackFontAssets.Contains(dynamicFallback))
                    throw new InvalidOperationException(
                        "TMP global default and fallback fonts are not configured for MateSoul.");
                textFont = template;
                textFont.name = "MateSoul UI SDF";
                return textFont;
            }
        }

        private static Material TextMaterial
        {
            get
            {
                if (textMaterial != null) return textMaterial;
                var font = TextFont;
                if (font.atlasTextures == null || font.atlasTextures.Length == 0 ||
                    font.atlasTextures[0] == null)
                    throw new InvalidOperationException("The UI SDF atlas is missing.");
                textMaterial = new Material(font.material)
                {
                    name = "MateSoul UI SDF Material",
                    mainTexture = font.atlasTextures[0],
                    hideFlags = HideFlags.DontSave
                };
                return textMaterial;
            }
        }

        public static T ConfigureText<T>(T text) where T : TMP_Text
        {
            text.font = TextFont;
            text.fontSharedMaterial = TextMaterial;
            text.isOverlay = true;
            text.raycastTarget = false;
            return text;
        }

        public static void ConfigureRaycasters(GameObject root)
        {
            var graphic = root.GetComponent<GraphicRaycaster>() ?? root.AddComponent<GraphicRaycaster>();
            graphic.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            var tracked = root.GetComponent<TrackedDeviceGraphicRaycaster>() ??
                          root.AddComponent<TrackedDeviceGraphicRaycaster>();
            tracked.checkFor2DOcclusion = false;
            tracked.checkFor3DOcclusion = false;
        }

        public static void Apply(Transform root, Material material)
        {
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic is TMP_Text text) ConfigureText(text);
                else graphic.material = material;
            }
        }

        public static T Apply<T>(T graphic, Material material) where T : Graphic
        {
            if (graphic is TMP_Text text) ConfigureText(text);
            else graphic.material = material;
            return graphic;
        }
    }
}
