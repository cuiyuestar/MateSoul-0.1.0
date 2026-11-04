# MateSoul Direct59 冷月主工程合入

> 日期：2026-10-29
> 状态：已合入主工程，静态验证通过；Unity 运行、APK 与真机验证待授权恢复

## 合入结果

生产角色 `Lengyue_Runtime.prefab` 已累计接入 Direct56 至 Direct59：

- Direct56：身体材质和两张皮肤贴图，去除拇指根部固定暗纹。
- Direct57：面部表情材质和两张面部贴图，接入柔和深紫眉。
- Direct58/59：头发与发束线网格，收窄后侧发缝并闭合右侧耳旁露肤缝。

游戏专用资产保持原样：

- 面部仍为 Direct54 游戏网格，保留 61 个 Morph。
- 身体仍为 Direct54 + LimbRefinement01。
- 补臂、SoftFit01 服装、骨架、Animator、动作协调器、动画事件和 Clip GUID
  均未替换。
- 主场景继续引用同一个生产 Prefab，没有创建平行角色实例。

## 关键路径

- 最新生产资源：
  `/Users/bytedance/MateSoul/MateSoul/Assets/LengyueIntegration/Direct59/`
- 生产角色：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Lengyue_Runtime.prefab`
- 生产校验器：
  `/Users/bytedance/MateSoul/MateSoul/Assets/LengyueIntegration/Editor/ProductionCharacterValidation.cs`
- 合入证据：
  `/Users/bytedance/MateSoul/ArtSource/LocalStudyOnly/Lengyue_ProductionMerge_20261029/`
- 数值回执：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/lengyue_direct59_20261029.json`

## 验证结果

静态验证通过：

1. Direct59 五类源验证闸门通过，目标 ROI 露肤像素归零。
2. 8 个生产资源与已验收源文件逐字节一致。
3. 8 个新增 GUID 唯一，新材质全部依赖均存在。
4. 生产面网格仍为 61 个形态键。
5. Prefab 相对合入前只发生 5 个预期绑定替换。
6. 主场景文件本轮未写入，仍引用原生产 Prefab GUID。
7. 更新后的生产校验器使用 Unity 2022.3.38f1 Mono 编译器及当前项目程序集
   独立编译通过。

当前 Unity batchmode 在项目导入前因 headless entitlement 失效退出，因此没有把
本轮写成 Unity 编译、运行循环、Android 构建或 PICO 真机通过。校验器已更新，
授权恢复后应先运行该校验，再构建新的 Direct59 APK。

## 回退

合入前 Prefab 和校验器保存在证据目录的 `BeforeMerge/`。Direct55 目录没有删除，
回退只需恢复旧 Prefab 绑定，无需覆盖源资产或重建骨架、动画。
