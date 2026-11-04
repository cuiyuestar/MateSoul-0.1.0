# Lengyue Production Asset

Use this Prefab in all gameplay scenes:

`Runtime/Prefabs/Lengyue_GameCharacter.prefab`

This is the only production Prefab in this Lengyue directory. It contains the
layered dress, fitted ruffle collar, enlarged cloth buttons, toon materials,
locomotion controller, hair physics, skirt pose guard, and head/neck correction.

Do not restore or reference V001, V002, or V003 paths. Historical generated
assets are stored outside Unity under:

`ArtSource/SapphiArt/Wardrobe/legacy_unity_assets/`

The editor command `MateSoul/Lengyue/Build Production Character` updates the
same Prefab in place and preserves its GUID.

The current production garment is the no-collar v004 baseline:

`ArtSource/SapphiArt/Wardrobe/v004/source/Lengyue_LayeredDress_v004.blend`

Production uses garment `ValidationPart 0-5`, the canonical 70-vertex
NeckExtension and the original Face material. The v005/v006 collar revisions
remain under `ArtSource/SapphiArt/Wardrobe/` as non-production history.

Hair and lower-lip appearance was updated on 2026-09-26. The hair material
enables strand-oriented toon highlights and three-tone shadows; the face atlas
contains a very pale lower-lip tint. Both are assigned through the existing
production materials, with no startup switching script.

Latest Unity closeups, before/after comparisons, motion recordings and numeric
validation are in `ArtSource/SapphiArt/Appearance/NaturalHighlights/` at repository root.
Hair now uses three broad, irregular soft ribbons, shallower strand divisions
and lifted shadows for a matte silk appearance. Catchlights are slightly smaller
and placed toward both outer eye corners; a faint nose shadow improves readability.
The brighter lower iris and lower-lip tint are preserved. Hair, ribbon mask, eye
and face maps are packed into the V004 Blender source with a matte preview.
Use `MateSoul/Lengyue/Capture Current Appearance` for current closeups.

## Runtime Wardrobe

The production prefab contains `LengyueWardrobeController`. The current
`LavenderCasual_Garments`, `LavenderCasual_SkinCoverage`, and
`LavenderCasual_NeckExtension` objects form the atomic `lavender-casual`
initial outfit.

To add another outfit:

1. Export a clothing-only FBX using the canonical Lengyue rest pose and bone
   names. Include every outfit-specific coverage mesh, neck patch, shoe, and
   accessory in the same export.
2. Select the imported outfit root in Unity.
3. Run
   `MateSoul/Lengyue/Wardrobe/Create Definition From Selection`.
4. Add the generated `LengyueOutfitDefinition` asset to the production
   prefab controller's `Outfits` catalog.
5. Switch at runtime:

```csharp
if (!wardrobe.TrySwitchOutfit("outfit-id", out string error))
    Debug.LogError(error);
```

The controller validates every mesh, material, root bone, and bone mapping
before changing visibility. A failed switch keeps the current outfit active.
Sleeve cloth is rebuilt for compatible garments, while the skirt pose guard is
enabled only for definitions that contain the canonical skirt bones.

Do not add a second character skeleton or Animator for an outfit. Outfit
definitions contain renderer data only and bind to the existing 116-bone
production skeleton at runtime.

### 粉格睡衣与睡帽

生产换装目录已注册`pink-pajamas`，定义位于
`Runtime/Wardrobe/PinkPajamas/PinkPajamas.asset`。
上衣、长裤、帽子和服装遮挡层使用原身体骨架；不启用旧短袖/裙摆模拟。
切回`lavender-casual`会自动恢复原布料。

- 重建菜单：`MateSoul/Lengyue/Wardrobe/Build Pink Pajamas`
- PlayMode验证：`MateSoul/Lengyue/Wardrobe/Test Pink Pajamas In Play Mode`
- 模型、视频及报告：仓库根目录
  `ArtSource/SapphiArt/Wardrobe/PinkPajamas/v001/`
- 已验证16次往返切换、330帧待机/行走/跑步/进食，运行错误为0。
