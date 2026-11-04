# MateSoul 真机 UI 与二次启动问题（2026-10-24）

## 已确认的原因

1. **低头看 UI 时按钮继续滑走**：`GameHud.Recenter` 每帧使用摄像机完整 forward/up 定位。相同头部位置低头 35°，旧面板中心高度从 1.000m 降为 0.266m，总位移 0.996m。这个行为符合用户描述，与追踪平滑系数无关。
2. **UI 覆盖手、像贴纸**：背景使用 Overlay 渲染队列和 `ZTest Always`，文字使用 TMP Overlay Shader 并设置 `isOverlay=true`。虽然 Canvas 为 WorldSpace，像素仍跳过空间深度关系。
3. **手部交互缺失**：项目没有安装 `com.unity.xr.hands`，SDK 的 `PicoHandInteraction` 和手部子系统受 `XR_HANDS` 条件编译控制，原工程中这些类型不存在，场景中的 `<PicoHandInteraction>` 绑定没有输入源。原模式控制器也只检查旧 Unity XR 设备列表，未读取 SDK 的 Input System 手部状态。
4. **二次启动提示原文**：`Room obstacles removed the complete walkable surface.`。这是 `RuntimeRoomNavigation.Build` 抛出的异常：地板单独可以烘焙，但加入房间障碍盒后没有任何可用导航点。用 6×6m 地板和覆盖地板的障碍盒本地复现了完全相同的异常。

## 首轮本地排查时的证据边界

- ADB 在本次排查时没有连接设备，未获取用户当时的真实启动日志或房间几何。
- 尚不能确定真机上是哪件障碍物、尺寸/姿态异常还是标定数据覆盖了通路；不能把合成障碍盒当成真实房间证据。
- 报错发生在 `Room.Build`，早于角色启用和 GameSession 恢复。恢复后的角色位置不会直接触发它；并行锚点定位和 Scene Capture 数据读取是否存在时序影响仍待真机日志确认。
- UI 的真实手遮挡、近距离点按、左右手切换和连续两次启动，仍须真机验收。

## 本次改动

- 新分支：`fix/headset-ui-room-20261024`。
- HUD 默认约 0.65m 距离、中心低于眼睛 0.12m；忽略头部俯仰，水平转头超过 30° 或位置移动超过 0.2m 后平滑重定位。正在指向 UI 时暂停跟随，避免按钮移动。
- 保持 Unity/XRI WorldSpace Canvas；背景与 SDF 文字恢复深度测试，并直接用 UI 像素的世界位置采样 PICO Environment Depth，支持真实手遮挡。
- 安装 XR Hands 1.4.3；从 SDK 手部设备状态切换输入模式，PICO 当前活动输入为手部时不让静置手柄占用输入。
- `PicoHandInputSetup` 在 Input System 边界为 SDK 布尔控件增加数值恢复处理，
  保持 SDK `bool` 事件的单字节布局；修正 BYTE 轴把原始 `1` 归一化为 `1/255`
  而达不到按下/追踪阈值的问题，不修改共享 SDK 源文件。
- 增加 `PicoHandPoke`，将 SDK 的手指姿态提交给 XRI 标准点按流程。保留既有射线、抓取和角色动作所有权。
- 增加 `DeviceStartupLog`：Android 自动保留 `startup-current.log` 与 `startup-previous.log`，每份约 128KiB 上限，记录游戏启动、房间导航、输入模式和异常。没有连接 USB 时同样记录。
- 导航失败保留原有准入，不忽略障碍物。Development 包补充障碍 ID、语义、尺寸、旋转、房间来源、地板高度和面积。
- 主场景、冷月 Prefab 和动画控制器未重新生成。

## 验证状态

- 修复前：低头 35°，面板下移约 0.734m；UI 队列 4000，TMP Overlay 为 true；SDK 手部类型不存在。
- 修复后：同样低头 35°，面板高度不变，位移 0；UI 队列 3000，TMP Overlay 为 false；SDK 手部类型已存在。
- Unity C# 编译与本地回归通过：模拟 SDK 手部追踪后输入模式为 Gesture；
  SDK 原始 bool 事件在布局修正后读取值为 1，pointerActivated/pinchTouched
  均通过按下验证；两个 UI Shader 的编辑器导入无 Shader 错误。
  Android Development ARM64 / IL2CPP 构建通过，Unity 退出 0，耗时 475.5 秒；
  无新增 C# 或 Shader 编译错误。
- APK 签名、ZIP CRC、`com.matesoul.game` 包名、ARM64 架构校验通过，
  签名证书与上一真机包一致。主场景、冷月 Prefab、动画控制器哈希保持一致。
- 新测试包：`/Users/bytedance/MateSoul/Builds/MateSoul_SpatialUI_20261024.apk`，
  183346980 bytes，SHA-256：
  `1d098d1c00659474077e4cc9701a67aa7b794fbfb4375b9e5f0d2c0202c1f66e`。
  默认 `MateSoul_Framework.apk` 同步更新。未连接设备，未安装新包。
- 数值与构建证据：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/headset_ui_room_20261024.json`。
- 真实头显验收：尚未执行。

## 连接设备后的操作

先读取现有日志，不清除日志或存档：

```bash
/Users/bytedance/Library/Android/sdk/platform-tools/adb devices -l
/Users/bytedance/Library/Android/sdk/platform-tools/adb logcat -d -v threadtime Unity:I '*:S'
```

新版的离线日志位于：

```text
/sdcard/Android/data/com.matesoul.game/files/diagnostics/startup-current.log
/sdcard/Android/data/com.matesoul.game/files/diagnostics/startup-previous.log
```

验收顺序：保持 USB 连接并戴上头显 → 启动 → 低头查看 UI → 手伸到面板前方观察遮挡 → 左右手点按和射线捏合 → 退出并再次启动 → 拉取日志定位房间故障。保留原有存档与系统标定，避免清空数据后掩盖二次启动问题。

## 真机安装、历史回捞与现场复现

- 用户连接 B3110 / swan 真机后，使用 `adb install -r` 安装新 APK 成功，耗时 8.83 秒，未清空应用数据。设备安装包 SHA-256 与交付包一致。
- MAIN/LAUNCHER 启动返回 `Status: ok`，应用 PID 为 29505，Activity 已处于 resumed；最后检查进程仍存在。这证明应用可启动，**不代表房间初始化成功**。
- 左右 `PicoHandInteraction` 设备均已创建，SDK 报告手部交互受支持，左右手追踪活跃，应用自动进入 `EyeHand` 模式。UI 点按、低头体验和遮挡质量由用户自行验收，本轮没有自动重启游戏。
- 新版离线日志已创建并拉取，路径为 `/sdcard/Android/data/com.matesoul.game/files/diagnostics/startup-current.log`，本次拉取 23401 bytes。

### 上一版发生了什么

安装前保存了设备已有日志缓冲区；即使当时没有连接 USB，旧日志仍存在。回捞到旧进程 27357 在 **16:53:55.824** 输出：

```text
MATESOUL_NAVIGATION_FAIL
"error": "启动失败：Room obstacles removed the complete walkable surface."
```

旧启动的详细房间数据行已不在缓冲区；诊断报告中的 `source=""`、`floorAreaM2=0`、`frames=0` 是启动失败后未填充的探针字段，不能解读为“SDK 没读到地板”。

### 新包复现提供的具体证据

**18:08:45.596** 再次出现同一个异常，但这次拿到了失败前的几何信息：

- Scene Capture 启动和查询成功，房间快照成功建立；
- 地板来源 1，地板面积 **5.48㎡**；
- 障碍物来源 **18**，SurfaceMesh 来源 0；
- 加入障碍后的导航网格顶点 **0**；代码随后用 floor-only 构建成功采样，再抛出该异常；
- 角色尚未启用、GameSession 尚未恢复，所以冷月未出现；
- 本轮没有 `anchor_start` 阶段，说明此次复现未启动旧房间锚点恢复，不能继续把旧锚点作为该异常的必要原因。

**已确认的故障边界是房间障碍物参与导航烘焙后清空了可行走区域。** 权限拒绝、查询不到地板均与这次日志不符。

家具方向是后续优先验证点：6 个家具障碍的 Euler 旋转均为 `(0,0,0)`，例如 Bed 的局部尺寸记录为 `(1.12,1.99,0.62)`。项目 provider 将 `anchorRotation * localRotation` 用作障碍旋转，而本地 SDK 示例 `PXR_SceneCaptureManager.cs` 标明 `//currently,rotation not support`，示例仅使用 anchorRotation。这支持“局部旋转返回值使家具姿态失真”的假设，但当前日志没有原始两个四元数，**尚不能认定它是唯一根因，也不能直接交换尺寸轴**。

数值记录：`/Users/bytedance/MateSoul/项目沉淀资料/validation/headset_ui_room_device_20261024.json`。原始日志保留在 `/tmp/matesoul-headset-ui-room/device-20261024`，项目文档只沉淀本应用相关结论。本轮只安装一次、启动一次，随后只读取证据，保留用户正在测试的运行实例。

## 原始姿态回放与实际修复

用户佩戴唤醒后，诊断包在 18:36:05 读取完整房间数据：9 个地板顶点、18 个障碍物，地板面积约 5.49㎡。6 个家具的 `localRotation` 均为 `(0,0,0,0)`，`anchorRotation` 为有效四元数。`PicoSceneCaptureRoomMapProvider` 原先直接相乘，丢失了家具的锚点朝向。现已将缺失局部旋转视为单位旋转，有效局部旋转先归一化，坐标轴与尺寸保持 SDK 原值。

实际房间回放表明，**旋转缺陷与重复避障间距、粗体素共同导致失败**，单独修复旋转不足以生成导航：

| 回放配置 | 保留障碍物 | 导航结果 |
|---|---:|---|
| 原实现 | 18 | 0 顶点，启动异常 |
| 只修复局部旋转 | 18 | 0 顶点 |
| 修复旋转，移除虚拟墙 | 10 | 0 顶点 |
| 修复旋转，仅将体素降至 2cm | 18 | 0 顶点 |
| 修复旋转，去掉额外膨胀，4cm 体素 | 18 | 0 顶点 |
| 修复旋转，去掉额外膨胀，2cm 体素 | 18 | 8 顶点，约 0.01724㎡有效导航 |

`RuntimeRoomNavigation` 原先先扩大障碍盒，再由 NavMesh 按 0.25m 角色半径侵蚀可行走区域。现在导航使用与物理碰撞相同的障碍盒，移除重复安全膨胀，并将体素从 8cm 改为 2cm。角色半径 0.25m、高度 1.65m、最小区域阈值保持原值；墙体与家具全部保留，未启用忽略障碍物的启动兜底。

将生产版本导航代码直接用于相同房间回放后，通过完整 0.25m 半径/1.65m 高度胶囊检查，找到 **4 个不与家具碰撞的站立采样点**；可用出生点为约 `(-0.02029, 0.00761, -1.70507)`，烘焙约 29ms。可用空间很小，不能据此认为房间具备自由散步条件。

UI 根据用户反馈改为：当前可见页面或导航栏中心位于视野下方 **18°**，俯仰跟随比例 **25%**、上下限幅 **8°**。低头35°时面板下移约0.09068m，点击/近距离点按时固定。展开页放在导航上方，字幕放在展开页上方，避免按整个画布中心定位造成页面偏高。原有直接手势、物品放置与菜单切换的最新改动保留。

本地回放日志位于 `/tmp/matesoul-room-repair-20261024`，最终 Android 包及真机 READY 验证正在进行。

## 联合包构建、安装与当前验收边界

最终联合包已构建并安装：
`/Users/bytedance/MateSoul/Builds/MateSoul_RoomFixed_UIFollow_20261024.apk`，
202838211 bytes，SHA-256：
`fe561531f21cd1fa9186d674a0b42b34a11b92adc01cacc7dfd8a252a8a4b210`。
默认 Framework APK 同步更新。Unity 实际退出0，构建进程耗时112.62s；
1564项ZIP CRC、签名、包名与ARM64/IL2CPP验证通过。Manifest确认
`handtracking=1`、无controller声明、Scene Anchor和Environment Depth开启。
本次包保留最新直接手部交互实现。

生成的IL2CPP代码确认包含：HUD下方18°、25%俯仰跟随与±8°限幅，
Box3D局部四元数归一化/缺失时单位旋转，以及2cm导航体素。
主场景、冷月Prefab和Animator控制器与构建前哈希一致。
工具外层曾报告Unity遥测数据库受沙箱限制，但目标构建日志有
`MATESOUL_BUILD_PASS`，实际APK完整性和设备安装均独立通过。

使用`adb install -r`安装，耗时6.87s，设备base.apk哈希与交付包一致。
安装前保存现有logcat缓冲；未清空日志。设备原有存档存在，
安装前后存档SHA-256一致，系统房间标定保持原有状态。

19:04:01启动Activity成功，PID5111；唤醒命令只能维持短暂Awake，
约11s后设备重新Asleep。63.76s观察没有Unity/XR初始化或新的
`MATESOUL_READY`，因此**尚不能判定最终包真机导航通过、冷月已出现，
也未执行第二次成功冷启动**。未出现原错误只表示本次尚未运行到导航，
不能用作修复验证。拉取的startup日志可能仍属于旧启动，不能冒充本次证据。

需要用户重新佩戴头显、打开MateSoul并保持唤醒约1分钟，随后继续采集
`navmesh_built`、`avatar_bound`与`MATESOUL_READY`并完成第二次冷启动。
UI位置、真实手遮挡和点按手感同样仍待头显内验收。
临时原始几何插桩与调试环境保留到实际修复验证结束。

最新数值记录：
`/Users/bytedance/MateSoul/项目沉淀资料/validation/headset_ui_room_final_20261024.json`。

## 佩戴后启动复核：导航通过，另发现出生点搜索缺陷

用户重新佩戴并打开游戏后，安装包哈希仍与联合包一致。PID8378的真实日志在
19:15:00.861输出`navmesh_built`，18个障碍全部保留，原“障碍物移除了完整
可行走区域”异常已不再发生。但19:15:00.878随后抛出：
`房间内没有可用的角色出生点。`，没有`avatar_enable_end`和业务READY，
因此冷月仍未加载。离线startup-current.log的第67–69行独立确认同一顺序。

原因是`Room.Build`以地板中心成功采样，`GameBootstrap`随后以玩家前方2.2m作为
首选出生点，却仍使用`roomBounds.extents.magnitude`作为采样距离。当玩家朝向墙外
或房间较小，首选点的采样球没有覆盖房间中的剩余导航区域。
本轮将单次搜索距离改为：
`Distance(desiredSpawn, roomBounds.center) + roomBounds.extents.magnitude`。
该距离以三角不等式覆盖房间，无需第二套出生点兜底；不改变障碍、角色半径或动作所有权。

生产导航回归覆盖15个方向/距离组合，旧范围漏掉13个，新范围15个全部通过，
返回点均在原房间地板内，18个障碍保留。
`MATESOUL_SPAWN_COVERAGE_PASS tested=15 oldMisses=13 obstacles=18 vertices=8`。
本轮暂时增加`MATESOUL_SPAWN_SEARCH`和`MATESOUL_SPAWN_FOUND`用于真机范围验证。

新包`/Users/bytedance/MateSoul/Builds/MateSoul_RoomSpawnFixed_20261024.apk`
构建成功，Unity退出0，291.87s，202841762 bytes，SHA-256：
`8e9a935971fb86dfc19dc4398ca2a20f90ef1a0f404ed044fe3f46ffa85102ce`。
签名/CRC/包名/ARM64/IL2CPP通过；Manifest继续为HandsOnly并开启Scene Anchor和
Environment Depth。主场景、冷月Prefab与Animator控制器哈希保持不变。
`adb install -r`安装6.7s，设备APK哈希匹配，安装前后存档不变，
日志缓冲未清空。当前存档物品数0，roomAnchorId为空，因此后续两次启动
只能证明连续启动稳定性，不能冒充旧物品锚点恢复已验证。

新包安装后的自动启动PID9594再次受设备休眠影响：短暂Awake后约11s变为Asleep，
尚未执行Unity/XR初始化。出生点补修的真机验证和两次业务READY仍待保持头显佩戴。
本轮未把Activity启动成功、未出现异常或旧离线日志当作验收通过。
原始证据和当前结果保存在`/tmp/matesoul-room-repair-20261024`。

## 最新验收：冷月加载与连续两次唤醒状态冷启动通过

**冷月已完成启用、绑定和业务初始化，连续两次完整冷启动通过。** 本轮每次先
`am force-stop`并确认进程退出，再以Launcher启动；应用数据、房间标定和日志缓冲保留。
成功实例为PID13697和PID14107，当前保留PID14107供用户继续测试。

| 成功启动 | NavMesh | 出生点 | 冷月启用/绑定 | 业务READY | 导航烘焙 |
|---|---|---|---|---|---:|
| PID13697 | 通过 | `(0.32,-0.05282,-0.68)` | 通过 | 19:43:59.013 | 20ms |
| PID14107 | 通过 | `(0.32,-0.05282,-0.68)` | 通过 | 通过 | 19ms |

从GameBootstrap开始到业务READY分别919ms、836ms，**这不是Android完整冷启动耗时**。
两次保留18个障碍物，无原空导航或出生点异常；READY后继续观察8s未记录Unity异常。
PID13697的部分logcat启动行丢失，但离线startup-current.log第69–89行完整记录
navmesh_built、spawn、avatar_enable_end、avatar_bound和MATESOUL_READY，按时间/PID
对应使用了本次启动文件，没有以旧离线文件补充通过证据。

### 为何前一轮“导航通过”仍会在下一次失败

新的真机重定位姿态下，`.25㎡ minRegionArea`会删除侵蚀后仅约`.03–.05㎡`的导航小岛；
位于体素tile边界时小区域可能保留，导致相同房间换朝向/原点后结果不同。
72组回放对比中，2cm体素加原区域阈值只通过1/12种姿态，关闭区域删除后通过12/12；
改房间坐标框架但保持原阈值为0/12，说明单纯换坐标框架不能解决。
最终仅将minRegionArea设为0，保留2cm体素、.25m半径、1.65m高度及全部障碍。

完整胶囊检查还发现最近导航边缘点可能碰到倾斜柜体。新增
`RuntimeRoomNavigation.TryFindSpawnPosition`，统一在最近导航点和导航三角形中心中，
选择距首选目标最近、位于地板内且不与房间碰撞体相交的站立点；
GameBootstrap在启用角色前使用该能力。12种生产导航回放及独立完整胶囊检查均通过。

### 交付与明确未覆盖项

最新包：`/Users/bytedance/MateSoul/Builds/MateSoul_StableRoomStartup_20261024.apk`，
202848091 bytes，SHA-256：
`bb97c3719f59f1520464351799f56139d5c2cd3cfa6fbf8f3ce167753c398999`。
构建Unity退出0、406.32s；1564项CRC、签名、包名、ARM64/IL2CPP通过。
已覆盖安装，6.81s，设备APK哈希一致。存档安装前后及两次冷启动期间保持一致；
主场景、冷月Prefab和Animator控制器未改变。包包含当前HUD45cm/下方30°和眼手输入。

**仍存在独立SDK就绪问题，不能据两次成功宣称所有启动场景稳定：**

- 休眠后的延迟初始化PID9594返回`Scene Anchor query failed: ERROR_RUNTIME_FAILURE.`；
- 新包首次启动PID12938初次查询0锚点，进入native capture后快照没有地板，
  返回`No floor polygon was provided.`；随后两次重新冷启动才取得完整房间并成功。
  此处没有根据缺失数据推断根因，也没有加入猜测性SDK重试或伪造地板。
- 当前存档0物品、无roomAnchorId，本轮没有测试旧物品锚点恢复；
  很小的剩余导航空间也不代表能够自由散步。
- 日志证明冷月对象启用并绑定，不替代用户对实际视觉/UI手感的验收。

本轮临时Editor验证链接已删除。针对尚未解决的SDK数据就绪问题，保留原始日志与
已有诊断点供后续定位，不删除其他HUD任务的文件。
最新数值和验收边界：
`/Users/bytedance/MateSoul/项目沉淀资料/validation/headset_stable_startup_20261024.json`。
