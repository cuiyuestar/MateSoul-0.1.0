# MateSoul MR 安全策略与房间启动修复

最后更新：2026-10-24

状态：配置修复、构建、安装已完成；新启动已进入 Scene Capture。
头显随后休眠，真实房间加载与冷月恢复尚未通过佩戴验收。

## 已确认的问题边界

1. 用户启动的应用包名仍为 `com.matesoul.game`，已保存的主场景、冷月
   Prefab 与 Animator 文件哈希与先前审阅基线一致。
2. 旧包的 Android Manifest 中 `enable_mr_safeguard=0`，同时
   `enable_vst=1`、`enable_scene_anchor=1`。PICO SDK 编辑器说明明确指出：
   未选择 MR Safeguard 时，应用默认使用 VR 安全策略。
3. 22:47:48.663 的原生日志显示 `same6dof=false`、`dataAlgorithm=null`、
   `executeSkip branch=VR_NO_JUMP`；系统 UID1000 打开
   `com.pvr.seethrough.setting`，游戏在进入 Unity 业务启动前被暂停。
4. 另一个已进入业务的进程 PID17594 在 22:43 查询时，
   `current map uuid` 为空，Scene Capture 读取
   `00000000-0000-0000-0000-000000000000`，返回 0 个场景锚点。
5. 系统房间设置能读取持久房间
   `b371603e-5c9e-482e-98f5-036d8e1e2958` 及其 RoomCapture 资产。
   保存数据存在、系统识别候选地图、当前坐标地图激活是不同边界。
   单纯延长游戏查询时间没有证明地图已激活。

原生来源：
`/tmp/matesoul-room-map-activation-20261024/snapshot-native.log`。
按原始 LF 分割的关键行号：524990、525020、681374、681380、681381。
这些是已存在日志的回捞证据，不能作为新修复包的运行记录。

## 修复内容

- `/Users/bytedance/MateSoul/MateSoul/Assets/Resources/PXR_ProjectSetting.asset`：
  将 `mrSafeguard` 设置为 1。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/GameSceneBuilder.cs`：
  在现有 `ConfigureInteractionCapabilities()` 中设置
  `settings.mrSafeguard = true`，使正式构建入口保持相同配置。
- 保留 Scene Capture 单 provider 的生命周期等待；仅真实有效房间快照可以
  进入导航、角色绑定和物品恢复。没有清除标定、存档或关闭系统安全边界。
- 修复本次构建发现的 Unity 2022 API 兼容错误：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RuntimeColliderRefiner.cs`
  中 `Physics.BakeMesh` 返回 `void`，不能赋给 `bool`。改为调用完成后更新
  既有 `baked` 标志，保留异常时原有处理。

SDK 依据：
`/Users/bytedance/MateSoul/XR/Editor/PXR_ManagerEditor.cs:315`；
`/Users/bytedance/MateSoul/XR/Editor/PXR_BuildProcessor.cs:567`。

## 验证标准

- 签名、ZIP CRC、ARM64、IL2CPP 输出和当前相关源码 Android PDB 校验。
- APK 最终 Manifest 中 `enable_mr_safeguard=1`。
- 覆盖安装后设备 APK 哈希匹配；安装前后存档 SHA-256 一致。
- 已保存主场景、角色 Prefab、Animator 哈希保持不变。
- 同一新启动进程中确认安全策略分支、当前地图及场景锚点；
  必须取得 `room_loaded`、`navmesh_built`、`avatar_bound`、
  物品恢复及 `MATESOUL_READY`，才能认定完整启动问题已解决。

MR 配置错误已由源码、包 Manifest 和原生日志确认；改变配置是否足以恢复
本次房间激活，仍以新包设备证据为准。

## 构建与安装结果

- APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_MRSafety_20261024.apk`。
- 文件大小：202950656 bytes；SHA-256：
  `f3314afb7744c2b45f45ee9a3bd137ae1e6e7b788348eda5f21fa4951c2ae596`。
- Unity BuildPipeline 耗时 139.513 秒，进程正常退出。签名 v2、
  1564 项 ZIP CRC、Android ARM64 校验通过；APK 的 IL2CPP metadata
  和原生库与本次构建输出一致，10 个当前相关源码匹配 Android PDB。
- 最终 APK Manifest 确认 `enable_mr_safeguard=1`；使用明确指定的已保存
  `Assets/MateSoul/Scenes/MateSoul.unity`，没有运行重建场景/角色的 authoring 入口。
- 已覆盖安装，设备实际包 SHA-256 与上述 APK 完全一致。
- 安装前、安装后、运行后存档 SHA-256 均为：
  `c32fb27b536e8eba8cf427f27cca2ba6056de9704cf8ab8108ce4756dbd84114`。
- 主场景、冷月 Prefab、Animator 三项保护哈希不变；当前 HUD 原生捏合、
  食指轻触和近距物品交互修复源码已包含在本次包中。

Unity 遥测数据库等工作区外写入被沙箱拒绝；该限制没有阻止脚本编译、
BuildPipeline 成功及 APK 输出。成功依据是构建日志、实际 APK 和设备校验，
不以工具包装层退出状态代替构建结果。

## 新包设备观测

- 23:00:11 唤醒并启动，新进程 PID19672 的新日志已确认，排除了旧启动文件。
- 23:00:16.859 进入 `bootstrap_start`；
  23:00:17.366 到达 `scene_provider_started`，开始实际 Scene Capture 查询。
  本次 62.2 秒观测未捕获旧版 `SeeThroughSetting` 的
  `BoundaryJumpManager/executeSkip` 抢占流程。
  这证明本次已越过先前捕获的业务启动前阻断，但不是所有启动场景的稳定性结论。
- 首次查询耗时 3352ms，结果 `SUCCESS`、0 个锚点、2 次 provider 更新、
  应用焦点为 1。原生 `LoadSceneAnchors` 当前 map 字段仍为空。
- 10.2 秒的第一次设备状态检查已经 `Asleep`，此后到 62.2 秒均保持休眠。
  最新启动阶段停在 `scene_localization_wait`，没有
  `room_loaded`、`navmesh_built`、`avatar_bound` 或 `MATESOUL_READY`。
  因此不能写“房间恢复/冷月加载已通过真机验收”。
- HTTP 没有直接收到设备事件，后续查询 ADB reverse 列表为空。
  已从既有 `DeviceStartupLog` 回捞真实 post-fix 查询，并明确标记
  `source=adb-offline-device-startup-log` 导入本轮采集端；没有伪造在线采样。

设备证据：
`/tmp/matesoul-room-map-activation-20261024/post-fix-device.log`、
`post-fix-startup-current.log`、`post-fix-device-result.json`。
数值记录：
`/Users/bytedance/MateSoul/项目沉淀资料/validation/room_mr_safety_20261024.json`。

## 下一次佩戴验收

戴上并唤醒头显，在当前已保存房间数据对应的现实房间内打开应用列表中的
MateSoul，保持佩戴至少 1 分钟。检查是否进入含冷月、导航菜单和物品的游戏场景。
如果仍停在“正在找回房间”，也保持唤醒，以便回捞持续查询与原生地图激活证据。
无需重新扫描或删除系统房间。

本轮房间诊断插桩与采集端暂时保留，待取得真实有效房间与 READY 证据后清理。
另一个输入排障的 PID17660/7789 采集端保持独立，不随本轮清理停止。
