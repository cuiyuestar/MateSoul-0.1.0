# MateSoul HUD 低头回正修复（2026-10-26）

## 修复范围

用户反馈连续低头时 HUD 下移，抬头后没有完全回升。先完成只读分析，再按用户授权修改 `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs` 的 `UpdateWorldPose`。

## 原因与处理

1. 原跟随原点使用 20cm 的三维位移停留区。弯腰刷新了较低的原点后，站直的最后一段位移可能不足阈值，因此保留高度偏差。现仅水平位移使用停留区，高度在每次非交互冻结更新中取当前头显高度。
2. 原俯仰使用硬限幅，低头超过约 32° 后目标饱和；从 −60° 抬到 −40° 时目标不变。现使用 `8 × tanh(headElevation × 0.25 / 8)`，始终保持 ±8° 的舒适上限，并随抬头开始回升。
3. 原头部接近垂直时提前返回，连高度更新也停止。现保留已有水平朝向，同时继续计算高度与俯仰。

保留距离 45cm、基础俯角 30°、垂直偏移 −20cm、水平停留区 20cm、朝向停留区 30°、指数插值及当前可见面板中心补偿。眼手点按或食指接触期间继续冻结定位，解除后恢复跟随。

## 已完成验证

- 之前通过反射直接运行生产 `GameHud.UpdateWorldPose` 的隔离姿态回放：22/22。
- 覆盖小高度变化、连续五轮弯腰/站直、深低头抬起、全俯仰范围、近垂直、交互冻结与解除、水平停留区、30/72/90fps 和展开面板中心。
- 最大回正误差：`1.46759419e-6m`，约 `0.0015mm`。
- 原始证据来源：`/tmp/matesoul-hud-return-20261026/acceptance.json`；
  已归档到 `/Users/bytedance/MateSoul/项目沉淀资料/validation/hud_return_20261026.json`。
  这些结果属于合成姿态回放。
- 已在共享 Unity 释放后清理本轮 `HudReturnAcceptance.cs` Editor 链接和 meta。
  用户删除的临时验收脚本不恢复、不执行；生产修复保留。

## 打包与设备状态

上轮 Android 构建因磁盘耗尽失败，未生成本轮修复包。2026-10-26 空间恢复后，共享工程使用 `MateSoul.Editor.GameSceneBuilder.BuildCurrentSceneAndroidIncrementalDevelopment` 成功构建当前保存场景；BuildPipeline 耗时约 122.9 秒，Unity 退出码 0。本轮复用这一新构建，已停止自己的等锁任务，避免重复打包。

| 项目 | 结果 |
| --- | --- |
| APK | `/Users/bytedance/MateSoul/Builds/MateSoul_HudReturn_20261026.apk` |
| 实际文件大小 | 218,746,149 字节 |
| SHA-256 | `1b7252ef8d26210e708aad85d8b1d24c5a2615f8865c29f5ef47ffdf9884d880` |
| ZIP CRC / 签名 | 1,569 项 CRC 通过；apksigner 校验通过 |
| 包名 / ABI | `com.matesoul.game` / 仅 `arm64-v8a` |
| Player 与 Android stripped PDB | GameHud、HudEyeHandInteractor、PicoHandPoke、PicoTrackedHand、PicoDirectHandInteractor 共 5 份当前源码 SHA-256 均匹配 |
| 原生库 | APK 内 `libil2cpp.so` 与本批 Gradle 输入完全一致 |
| 场景与角色保护 | 构建前后主场景、Lengyue Prefab、Avatar.controller 一致；观察期间其 meta/GUID 也一致 |

HUD 源码 SHA-256：`7a405d00597b99d788b0403dfcf45343a3d2200366404724d76e1fa6d9ff1c3e`。数值与源码指纹已保存到 `/Users/bytedance/MateSoul/项目沉淀资料/validation/hud_return_20261026.json`，包含原 22 项回放结果。

校验在本批构建完成后立即进行。之后另一个角色合并任务已启动，不能把后续角色资源或 Library 中间产物当作本包的构建快照。

当前 ADB 无设备连接，尚未安装。头显实际低头/抬头、弯腰/站直及解除点按后的恢复仍待真人验收。
