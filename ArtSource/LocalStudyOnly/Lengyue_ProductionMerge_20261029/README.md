# 冷月 Direct59 主工程合入（2026-10-29）

主工程生产角色已累计接入 Direct56 至 Direct59 的最新外观：

- 身体仍使用 `Body_Direct54_LimbRefinement.asset`，仅将皮肤材质更新为
  `BodySkin_Direct56.mat`，去除拇指根部固定暗纹。
- 面部仍使用带 61 个游戏形态键的
  `Face_Direct54_GameExpressions.asset`，仅将表情材质更新为
  `FaceExpression_Direct57.mat`，接入柔和深紫眉。
- 头发和发束线更新为 `Hair_Direct59.asset` 与
  `HairStrandLines_Direct59.asset`，累计包含 Direct58/59 的后侧和侧耳发缝修正。
- SoftFit01 服装、LimbRefinement、补臂、骨架、Animator、动作协调器、
  动画事件和既有 Clip GUID 均未替换。

生产资源位于：

`/Users/bytedance/MateSoul/MateSoul/Assets/LengyueIntegration/Direct59/`

生产 Prefab 位于：

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Lengyue_Runtime.prefab`

主场景仍通过原 GUID 引用该 Prefab，没有复制第二套角色。

## 静态验证

- Direct59 源 `geometry_gate`、`independent_blend_gate`、`unity_gate`、
  `package_gate` 及两个 ROI 闭合检查通过。
- 8 个复制到生产目录的资源与已验收源文件逐字节一致。
- 8 个新增资源 GUID 在主工程中均唯一，两个新材质的全部依赖均可解析。
- 生产面网格仍包含 61 个形态键。
- 将 5 个 Direct59 绑定反向替换后，生产 Prefab 与合入前备份逐字节一致，
  证明本轮没有改写其他玩法组件或参数。
- 主场景文件时间保持为 2026-10-28 18:13:43，本轮未写入场景；场景继续引用
  原生产 Prefab GUID `b62edb46b12e444e5953050e8431e291`。
- 更新后的 `ProductionCharacterValidation.cs` 使用 Unity 2022.3.38f1 自带
  Mono 编译器和当前项目程序集独立编译通过。

数值回执：

`/Users/bytedance/MateSoul/项目沉淀资料/validation/lengyue_direct59_20261029.json`

## 当前验证边界

`ProductionCharacterValidation` 已更新为检查 Direct59 网格、Direct56 身体材质、
Direct57 面部表情材质和场景继承关系。当前 Unity 2022.3.38f1 batchmode 在进入
项目导入前因 `com.unity.editor.headless` 授权失效退出，因此本轮没有新的 Unity
运行循环、Android APK 或 PICO 真机证据。该授权失败不能解释为资产编译失败。

合入前 Prefab 与校验器保存在 `BeforeMerge/`，Direct55 生产资源也未删除，可按
原绑定回退。
