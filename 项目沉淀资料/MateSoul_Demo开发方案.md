# MateSoul Demo 开发方案

> 最后更新：2026-10-02  
> 状态：正式开发基线  
> 技术路线：Unity 2022.3.38f1 + URP + PICO XR + XR Interaction Toolkit  
> 角色基线：Lengyue Game Character  

## 0. 文档效力

本方案定义 MateSoul 首个可运行 Demo 的产品范围、技术架构、模块边界、开发顺序和验收标准。

该 Demo 不是一次性展示工程，而是 MateSoul 后续正式版本的产品内核。后续新增情绪、记忆、语音、虚拟物品、现实语义和角色动作时，必须扩展本方案定义的运行时和接口，不得另建平行状态机、动作系统或角色控制链路。

此前的 `MateSoul_首版开发计划.md` 继续保留产品目标、心情值规则和 Avatar Runtime-first 思想，但其中 Kotlin、FullStage、Spatial ECS 和 GLB 静态角色等技术假设不再作为当前实现基线。当前实现统一使用现有 Unity 工程和角色资产管线。

2026-10-02 开发要求补充：角色尺寸、移动速度、自主活动权重、阈值和时间等稳定
参数允许先采用代码默认值或 Inspector 字段。“无硬编码”针对把具体角色、物品和
玩法分支写进公共内核，不要求所有数值配置化。禁止过度设计环境变量、参数中心、
通用规则引擎和重复保护逻辑。接口与内容定义只为真实替换/复用需求存在。
本轮框架实施顺序、范围与验收见 `MateSoul_游戏框架实施计划.md`。

技术路线硬约束：

- 当前使用 PICO Unity Integration SDK：Unity 包
  `com.bytedance.pico.xr`，不是 Android/Kotlin PICO Spatial SDK。
- 不配置 `com.pico.spatial:bom`，不引入 Compose、Kotlin、FullStage 或
  Spatial ECS。
- 文档中的 “Spatial Mesh”“Scene Capture”“Spatial Anchor” 指
  PICO Unity SDK 提供的 MR 功能 API，不代表项目切换到了 PICO Spatial SDK。
- 除非用户明确重新发起平台迁移，否则任何后续开发都必须保持 Unity/C# 路线。

## 1. Demo 目标

Demo 需要在同一个 PICO MR 运行进程中完成以下核心体验：

1. 玩家视野下方存在固定的基础 UI 工具栏。
2. 冷月能够待机、随机行走和随机选择虚拟物品进行简单互动。
3. 玩家能够输入文字与冷月对话，回复以视野下方字幕显示。
4. 玩家能够抓住冷月的一只手自由移动，冷月在牵手状态下跟随玩家行走。
5. 玩家能够在现实空间中放置虚拟物品。
6. 冷月能够接近并使用虚拟物品，例如走到虚拟椅子前并坐下。
7. 上述功能共享同一套动作仲裁和角色控制机制，不发生多个系统同时写角色位置或骨骼的问题。

## 2. Demo Golden Path

Demo 的核心演示路径固定如下：

```text
启动应用
  -> 进入 PICO MR Full Space
  -> 冷月在玩家侧前方落地生成
  -> 下方工具栏显示
  -> 冷月执行 Idle / Wander
  -> 玩家通过“虚拟物品”放置椅子
  -> 玩家通过“对话”要求冷月坐下
  -> AI 返回文本和 sit Action
  -> 冷月接近椅子并坐下
  -> 玩家抓住冷月的手
  -> 坐姿被安全打断，冷月起身并进入牵手跟随
  -> 玩家松手
  -> 冷月恢复 Idle / Wander
```

后续开发必须持续保证这条链路可运行。任何新功能都不能破坏该链路。

## 3. Demo 范围

### 3.1 必做范围

- PICO MR Full Space 启动与透视环境。
- 首次进入房间时完成系统 Room Capture；后续启动优先复用已保存的房间语义数据。
- 基于房间地面、墙体和家具语义一次性构建 Runtime NavMesh。
- 冷月角色加载、落地定位和运行时组件初始化。
- 基础 UI 工具栏及五个功能入口。
- 文本输入、AI 请求、结构化回复和字幕显示。
- Idle、随机行走和随机虚拟物品互动。
- 单手牵手、手臂 IK、角色跟随和安全释放。
- 虚拟椅子放置、接近、坐下、驻留和起身。
- 统一动作请求、控制权仲裁、取消和恢复机制。
- EditMode、PlayMode 和 PICO 真机验证。

### 3.2 暂不进入 Demo

- 语音输入和语音合成。
- RAG、长期记忆、Dream 蒸馏和 MCP 工具调用。
- 多角色和多人共享空间。
- 现实椅子语义识别。
- 任意网格自动推断坐姿点。
- 双手同时牵引角色。
- 跨房间寻路、楼梯通行和动态家具实时重规划。
- 大规模虚拟物品商店和经济系统。
- 开放式 LLM Action。

这些能力后续必须通过已预留接口接入，不能直接修改角色底层控制逻辑。

## 4. 技术基线

### 4.1 引擎与平台

- Unity：`2022.3.38f1`
- 渲染管线：URP `14.0.11`
- PICO：现有 `com.bytedance.pico.xr`
- 交互：XR Interaction Toolkit `2.5.4`
- IK：新增 Animation Rigging `1.3.1`
- 寻路：新增 Unity AI Navigation，并在运行时构建 NavMesh
- 目标平台：Android ARM64 / IL2CPP
- 最低系统：PICO OS 6 目标设备

### 4.2 场景

正式创建以下场景：

```text
Assets/MateSoul/Scenes/Bootstrap.unity
Assets/MateSoul/Scenes/Demo_Main.unity
Assets/MateSoul/Scenes/Character_Validation.unity
Assets/MateSoul/Scenes/Performance_Benchmark.unity
```

现有 `Lengyue_V001_Validation.unity` 继续作为角色资产验证场景，不直接承载产品玩法。

### 4.3 角色资产

Demo 使用当前 Lengyue Prefab 的新 Variant，不修改：

```text
Assets/SapphiArt/
Assets/Characters/SapphiArtCustom/Lengyue/V001/
```

建议新增：

```text
Assets/MateSoul/Content/Characters/Lengyue_Demo.prefab
```

该 Variant 负责挂载 Demo Runtime、IK Rig、碰撞代理和交互锚点，原角色发布资产保持只读。

## 5. 总体架构

```text
UI / AI / 随机调度 / 玩家抓取
          |
          v
AvatarIntentRouter
          |
          v
AvatarActionCoordinator
  |-- ControlLeaseManager
  |-- AvatarStateMachine
  |-- AnimationScheduler
  |-- LocomotionController
  |-- HandHoldController
  |-- VirtualItemInteractionService
  `-- AvatarPresentation
          |
          v
Animator / Animation Rigging / XRI / PICO XR
```

### 5.1 核心原则

1. UI、AI、随机系统和玩家交互只提交 Intent，不直接控制 Animator、Transform 或骨骼。
2. `AvatarActionCoordinator` 是角色动作生命周期唯一事实源。
3. `ControlLeaseManager` 是角色控制权唯一事实源。
4. 每个动作都必须可取消，并在成功、失败和异常路径执行 Cleanup。
5. 随机活动、AI 指令和 UI 指令使用同一种 Action，不复制执行逻辑。
6. 角色视觉、碰撞代理、移动根节点和交互锚点相互分离。

## 6. 工程模块

建议目录：

```text
Assets/MateSoul/
  Core/
    Intent/
    Actions/
    Control/
    State/
  Avatar/
    Animation/
    Locomotion/
    IK/
    Presentation/
  AI/
    Providers/
    Protocol/
    Orchestration/
  UI/
    HUD/
    Dialogue/
    Panels/
  Items/
    Definitions/
    Placement/
    Interactions/
  Pico/
    Tracking/
    SpatialMesh/
    Anchors/
  Content/
    Characters/
    Items/
    Animations/
  Tests/
    EditMode/
    PlayMode/
```

每个 Runtime 模块建立独立 asmdef，Editor 和 Tests 不进入运行时程序集。

## 7. 基础 UI 面板

### 7.1 布局

使用阻尼跟随 HMD 的 World Space Canvas：

- 工具栏位于视线下方约 `15°`。
- 与玩家距离约 `1.0-1.2m`。
- 工具栏保持固定宽高，不因选中、文本或图标变化产生位移。
- 不直接作为摄像机子节点硬锁定，使用平滑位置和朝向跟随降低抖动。
- 使用 XR UI Input Module 支持控制器射线和后续手势输入。

五个按钮从左到右：

```text
对话 | 互动 | 信息 | 虚拟物品 | 设置
```

按钮使用图标和文字。一次只允许展开一个二级面板。

### 7.2 功能定义

- `对话`：打开文字输入面板，提交后关闭输入并进入等待状态。
- `互动`：提供过来、停下、坐下、起身等显式动作。
- `信息`：展示角色名称、当前状态、心情值和牵手状态。
- `虚拟物品`：打开物品目录并进入放置模式。
- `设置`：字幕、音量、调试状态等基础设置。

### 7.3 字幕

角色回复不再显示为角色头顶气泡。

- 字幕位于工具栏上方。
- 最多两行，超长文本截断或分页。
- 支持 `思考中`、正常回复、错误和取消状态。
- 新回复替换旧回复，显示一段时间后淡出。
- 角色移动、牵手或坐下时，字幕位置仍相对玩家稳定。

## 8. 统一动作模型

动作请求至少包含：

```text
requestId
source
actionId
targetId
requiredChannels
priority
timeout
parameters
```

动作来源：

```text
Random
AI
UI
Player
System
```

### 8.1 控制通道

```text
Locomotion
FullBody
LeftArm
RightArm
Gaze
Face
```

通道允许安全并行。例如牵手占用 `LeftArm + Locomotion`，对话表现可以继续占用 `Face + Gaze`。

### 8.2 优先级

```text
SystemSafety       100
PlayerHandHold      90
ExplicitCommand     60
RandomInteraction   30
AutonomousMovement  10
```

规则：

- 玩家牵手可以打断随机活动、AI 物品动作和坐姿。
- AI/UI 显式动作可以打断随机物品互动和随机行走。
- 随机动作不能打断任何显式动作。
- Safety 可以中止全部动作并恢复角色到稳定状态。
- Dialogue 本身不占用 Locomotion，可与行走或牵手并行。

### 8.3 Action 生命周期

每个 Action 必须实现：

```text
CanExecute
Start
Tick
Cancel
Complete
Cleanup
```

任何退出路径都必须释放 Lease、停止动画、解除物品占用并恢复角色状态。

## 9. 角色随机活动

基础状态：

```text
Idle
Wander
ObjectInteraction
HandHeld
Seated
Recovering
```

自动状态流：

```text
Idle -> Wander -> Idle
Idle -> ObjectInteraction -> Idle
任意自动状态 -> HandHeld -> Recovering -> Idle
ObjectInteraction -> Seated -> StandUp -> Idle
```

### 9.1 Random Activity Director

随机调度器只决定提交什么 Action，不直接移动角色。

约束：

- 每次 Idle 后等待随机冷却时间。
- 仅从当前 `CanExecute` 的行为中按权重选择。
- 只选择已注册且未被占用的虚拟物品。
- 玩家正在对话、牵手或操作物品时暂停随机调度。
- 同一物品交互有独立冷却，避免角色频繁坐下。

### 9.2 移动

Demo 使用简单地面移动：

- Kinematic Capsule 作为身体代理。
- Runtime NavMesh 负责全局路径规划。
- 水平速度与转向速度限制。
- 向下射线保证脚底贴地。
- CapsuleCast 检测空间网格和虚拟物品障碍。
- 随机点限制在玩家周围安全半径内。
- 不瞬移，Safety Reset 除外。

`INavigationService` 隔离 Unity AI Navigation。寻路输出只作为期望路径，实际位移仍由
`GroundLocomotionController` 通过 CharacterController 和 CapsuleCast 执行。

### 9.3 Room Capture-first 导航地图

Demo 不在角色活动期间边扫描房间、边修改地图、边规划路线。游戏开始前必须先获得有效的房间语义数据并完成导航构建。

PICO 三类环境能力的职责：

```text
Scene Capture
  系统级房间语义快照；提供 Floor Polygon、Wall Box2D、
  Chair/Table/Sofa 等 Box3D；作为导航主数据源。

Spatial Mesh
  HMD 周围实时三角网格和逐顶点语义；仅用于可选的精细碰撞、
  遮挡和扫描质量补偿，不作为首版导航唯一来源。

Plane Detection
  运行时平面与边界；用于地面校验和虚拟物品放置。
```

启动流程：

```text
CheckingExistingMap
  -> QuerySceneAnchorAsync
  -> 已有数据：LoadingAnchors
  -> 无数据或质量不足：StartSceneCaptureAsync
  -> LoadingAnchors
  -> ValidatingRoom
  -> BuildingNavigation
  -> Ready
```

只有进入 `Ready` 后才允许创建角色自主行为和随机移动目标。

`RoomMapSnapshot` 至少记录：

```text
roomId
snapshotVersion
floorPolygons
walls
doors
openings
furnitureBoxes
anchorUUIDs
capturedAt
navigationBounds
```

导航构建规则：

1. 使用 `Floor` Scene Anchor 的 Polygon 生成主地面网格。
2. Floor Polygon 边界作为角色不可越界边界。
3. Wall 与 VirtualWall 生成薄碰撞体。
4. Chair、Table、Sofa、Cabinet 等 Box3D 生成障碍体。
5. 障碍体按角色半径和安全距离向外膨胀。
6. 一次性构建 Runtime NavMesh。
7. 随机点必须通过 `NavMesh.SamplePosition` 和完整路径校验。
8. CharacterController/CapsuleCast 保留为最终碰撞安全门。

房间数据有效性门禁：

- 存在包含玩家位置的主 Floor Polygon。
- Floor 面积达到最低活动面积。
- Polygon 无自交和退化边。
- 存在 Wall 或 VirtualWall。
- 所有关键 Scene Anchor 可成功定位。
- Runtime NavMesh 包含玩家附近区域。
- 至少一个随机候选点可得到完整路径。

不满足时必须重新进入 Room Capture，不允许退化为无边界随机移动。

Scene Capture 数据属于静态环境快照。家具移动、空间改造或重新扫描后，整批重新查询
Scene Anchors 并重建 `RoomMapSnapshot`；不依赖增量删除事件推断当前完整房间。

首版限制为单房间、单主地面，不支持楼梯和跨房间门洞寻路。

## 10. AI 对话

### 10.1 参考与重构

复用 MateFairy 的以下思想：

- `ILLMProvider` 抽象。
- Application-scope Runtime 统一装配。
- `ConversationOrchestrator` 编排输入、上下文、模型和动作分发。
- 结构化响应协议。
- Mock Provider 优先打通链路。

不复用以下实现：

- 不复制 Kotlin/Spatial ECS 平台代码。
- 不把记忆、Dream、MCP 全部放入 Demo 首版。
- 不使用多个全局 `RuntimeDependencies` 单例。
- 不维护两套 ActionRegistry。
- 不在 Prompt 多处硬编码 Action 列表。
- 不由 LLM 直接执行动作。
- 不在 APK 内嵌开发者共享 API Key；玩家自己的密钥可在本机 Keystore 加密保存。

### 10.2 响应协议

```json
{
  "status": "ok",
  "reply_text": "好呀，我去椅子上坐一会儿。",
  "response_emotion": "happy",
  "mood_signals": [{"id": "happy", "delta": 4}],
  "action": {
    "id": "sit",
    "target_id": "chair_01"
  }
}
```

规则：

- `reply_text` 必须非空且不超过600字符；`response_emotion`为normal/happy/sad/angry。
- `action.id` 必须来自运行时注册表。
- 不支持或目标不匹配的动作拒绝整条模型回复，不能通过部分接受掩盖协议错误。
- 解析或准入失败只允许一次格式修正，之后明确报错，不执行动作或更新对话历史。
- 请求支持取消、超时和场景销毁时的资源释放。

### 10.3 部署

- 开发阶段先接 `MockLLMProvider`。
- 联调阶段接真实 Provider。
- 2026-10-25 用户授权迁移为 APK 内 AI 系统：提示词组织、供应商协议、
  工具调用循环、联网工具和回复校验均由 Unity 运行时提供。
- 玩家填写自己的 API Key，设备通过 HTTPS 直连模型供应商；密钥在本机
  使用 Android Keystore 加密保存，不打包开发者共享密钥。
- 游戏运行不依赖 MateSoul 后端、Node.js 进程、网关地址或 ADB 端口桥接。
- 模型输出仍需经过运行时能力注册表校验，并由共享动作协调器提交执行。
- 长期记忆后续通过 `IConversationContextProvider` 接入。

## 11. 游戏内牵手 IK

### 11.1 参考结论

参考 `virtual-friend` 中验证成功的部分：

- XRGrabInteractable 驱动世界空间 IK Target。
- TwoBoneIKConstraint 驱动完整手臂链。
- IK Weight 平滑进入和退出。
- IK Target 不放在角色骨架层级下。
- 不直接写 Hand 骨骼位置。

需要重构的部分：

- 不使用单个组件同时处理抓取、根节点跟随、Animator 参数和头部注视。
- 不按初始 HMD Offset 直接 Lerp 整个角色。
- 不硬编码供应商 Animator 参数。
- 不直接写 Head 骨骼。
- 不允许牵手逻辑绕过 ActionCoordinator。

### 11.2 组件

```text
HandHoldInteractable
HandIKRig
HandReachSolver
HandHoldAction
HandHoldLocomotionPolicy
HandHoldSafetyMonitor
```

左右手分别配置：

```text
UpperArm -> Forearm -> Hand
TwoBoneIKConstraint
World-space Target
Elbow Hint
```

### 11.3 行为

1. 玩家抓住手部交互点。
2. 提交 `HandHoldAction` 并申请对应手臂与 Locomotion Lease。
3. IK Weight 平滑升到 1。
4. 手在软伸展范围内时，只由 IK 响应。
5. 超过软范围时，角色向玩家侧后方舒适目标步行。
6. 超过硬范围时限制 IK Target，并提供手柄震动反馈。
7. 松手后 IK Weight 平滑降到 0。
8. 角色进入短暂 Recovering，再回到 Idle。

以下情况自动释放：

- 控制器跟踪丢失。
- 玩家或角色瞬移。
- 距离超过硬安全阈值。
- 角色进入异常姿态。
- 场景销毁或应用暂停。

Demo 只允许单手牵引。第二只手抓取时返回不可用反馈。

## 12. 虚拟物品系统

### 12.1 数据定义

每个物品由 `VirtualItemDefinition` 描述：

```text
itemId
displayName
prefab
placementRule
collisionProfile
interactionCapabilities
approachPoint
interactionSockets
anchorPolicy
```

所有运行时实例进入 `VirtualItemRegistry`，随机行为、AI 和 UI 使用同一个 Registry 查询。

### 12.2 放置流程

```text
打开虚拟物品面板
  -> 选择椅子
  -> 控制器射线命中空间网格或水平平面
  -> 显示 Ghost Preview
  -> 校验坡度、面积和碰撞
  -> 玩家确认
  -> 创建物品实例
  -> 注册 Registry
  -> 创建或关联 PICO Spatial Anchor
```

Demo 首先保证当前会话内稳定放置。重启后的锚点恢复通过 `IAnchorService` 接口预留，并作为后续增强项。

### 12.3 椅子交互

虚拟椅子必须预制：

```text
ApproachPoint
SeatSocket
ExitPoint
SeatForward
Occupancy
```

坐椅 Action：

```text
Reserve
  -> Approach
  -> Align
  -> SitDown
  -> SitIdle
  -> StandUp
  -> Release
```

Demo 不通过运行时网格猜测座位位置。后续识别现实椅子时，仅替换目标发现和座位解析适配器。

物品被移动、删除或失去锚点时，角色动作必须取消并进入安全恢复。

## 13. 动画与角色运行时约束

Demo 至少需要：

```text
Idle_BodyOnly
Walk_BodyOnly
SitDown
SitIdle
StandUp
```

每条正式动画必须满足：

- 使用现有 116 根身体骨架。
- 不修改骨名、层级、Rest Pose 或缩放。
- 不包含头发骨曲线。
- 不包含 `Skirt_*` 或 `OuterSkirt_*` 曲线。
- Root Motion 策略明确。
- 头、眼和面部曲线必须按通道契约明确归属。
- 运行头颈连续、裙腿相交、有限值和多视角回归。

现有 `HeadPoseGuard` 不能直接作为 Demo 的全局上身控制器。它会恢复 Hips、Spine、Chest、Neck 和 Head，可能覆盖上身动画、注视和手臂 IK。应将其保留为旧动画兼容策略，正式 Demo 使用清理后的 Animator Controller 和明确的 Rig 执行顺序。

推荐执行顺序：

```text
Animator
  -> 角色姿态连续性
  -> Animation Rigging IK
  -> SkirtPoseGuard
  -> HairSpringPhysics
  -> Validation Probe
```

## 14. 开发阶段

### D0：基线与工程骨架

交付：

- 修正版本管理和资产基线。
- 建立 `Assets/MateSoul` 目录和 asmdef。
- 创建 Bootstrap、Demo_Main、Validation、Benchmark 场景。
- 创建 Lengyue Demo Prefab Variant。
- 创建新的生产 Animator Controller。

准出：

- Unity 编译通过。
- Demo_Main 可进入 PlayMode。
- 角色不修改原始 SapphiArt 资产。

### D1：UI 与字幕

交付：

- 五入口工具栏。
- 二级面板互斥。
- 文本输入与字幕。
- XR 控制器射线交互。

准出：

- UI 在移动头部时稳定、无明显抖动。
- 文本不超出容器。
- 字幕不遮挡工具栏和主要角色区域。

### D2：自主行为

交付：

- ActionCoordinator、LeaseManager。
- Idle、Wander、Recovering。
- 安全活动区、贴地和避障。
- RandomActivityDirector。

准出：

- 随机行为不穿墙、不瞬移、不离玩家过远。
- 显式动作可以稳定打断随机行为。

### D3：AI 对话

交付：

- Mock Provider。
- 真实 Provider。
- ConversationOrchestrator。
- 结构化协议、超时、取消和日志。
- AI Action 接入统一 ActionCoordinator。

准出：

- 对话失败不阻塞角色运行。
- 非法 Action 不会进入角色控制链路。
- 回复以字幕呈现，不生成角色头顶气泡。

### D4：牵手 IK

交付：

- 左右手交互点。
- 单手 TwoBone IK。
- 牵手跟随和安全释放。
- 与行走、坐姿和随机行为的抢占规则。

准出：

- 手臂无脱节、拉丝和明显抖动。
- 松手后平滑回到动画姿态。
- 玩家移动时角色步行跟随而非被直接拖拽。

### D5：虚拟物品与椅子

交付：

- VirtualItemDefinition 和 Registry。
- 空间放置、Ghost Preview 和碰撞校验。
- Chair Socket。
- Sit、SitIdle、StandUp Action。
- Random / AI / UI 三种来源共享同一坐椅 Action。

准出：

- 椅子可稳定放置在水平表面。
- 角色能够接近、对齐、坐下和起身。
- 牵手能够安全打断坐姿。

### D6：集成与真机

交付：

- 完整 Golden Path。
- PICO APK。
- 真机性能、稳定性和交互报告。
- 运行截图、关键日志和 SHA-256。

准出：

- 目标设备稳定运行至少 10 分钟。
- 72Hz 模式满足帧预算；同步记录 90Hz 测试结果。
- 稳态无持续 GC。
- 所有 Action 在取消后无残留 Lease、IK 或物品占用。

## 15. 测试策略

### 15.1 EditMode

- Action 优先级和通道冲突。
- Lease 超时、释放和异常清理。
- Random Scheduler 冷却和候选过滤。
- AI JSON 解析和 Action 白名单。
- VirtualItem Registry、占用和销毁。
- Hand Reach Solver 的软硬边界。

### 15.2 PlayMode

- UI 面板互斥和字幕生命周期。
- Idle / Wander / Action 状态转换。
- 抓手、IK 权重、步行跟随和松手恢复。
- 椅子放置、坐下、起身和打断。
- Animator、SkirtPoseGuard、HairSpringPhysics 的执行顺序。
- 新动画的头颈与裙腿回归。

### 15.3 PICO 真机

- 控制器射线和 Direct Grab。
- UI 可读性和舒适视角。
- 空间网格命中和物品放置。
- 牵手时的移动舒适性。
- 角色、头发、裙摆和物品同时运行的帧耗。
- 暂停、恢复、跟踪丢失和场景重载。

## 16. 可观测性

关键日志统一记录：

```text
sessionId
requestId
source
actionId
targetId
leaseId
previousState
nextState
result
cancelReason
durationMs
llmLatencyMs
```

核心事件：

```text
ACTION_REQUESTED
ACTION_STARTED
ACTION_REJECTED
ACTION_CANCELLED
ACTION_COMPLETED
LEASE_ACQUIRED
LEASE_RELEASED
HAND_HOLD_STARTED
HAND_HOLD_RELEASED
ITEM_PLACED
ITEM_INTERACTION_STARTED
AI_REQUEST_COMPLETED
```

每次真机验证记录 APK SHA-256、Git commit、设备型号、系统版本和运行时间。

## 17. 后续开发治理

Demo 完成后，后续功能按以下方式扩展：

- 新动作：新增 `IAvatarAction` 实现并注册。
- 新物品：新增 `VirtualItemDefinition` 和 Capability。
- 新 AI 能力：扩展 Action Schema 和 Registry，不直接调用场景对象。
- 新情绪：接入 MoodSystem，通过 Face、Gaze、BodyPose 通道表现。
- 新记忆：实现 `IConversationContextProvider`，不侵入 UI 和 Action。
- 新输入：语音、手势统一转换为 AvatarIntent。
- 现实语义：实现新的 `ISpatialTargetProvider`。
- 新寻路：替换 `INavigationService`。

禁止：

- UI 直接调用 Animator。
- AI Provider 直接查找或移动 GameObject。
- 随机行为直接写角色 Transform。
- 物品脚本直接抢占角色骨骼。
- 为单个新玩法复制第二套 ActionRegistry、状态机或控制锁。

## 18. Demo Definition of Done

Demo 完成必须同时满足：

1. 五入口工具栏和字幕稳定可用。
2. 冷月能够 Idle、随机行走和随机使用椅子。
3. AI 对话能够返回文本并触发白名单动作。
4. 玩家能够抓住任意一只手并带领角色移动。
5. 玩家能够放置虚拟椅子，角色能够坐下和起身。
6. 牵手、对话、随机活动和坐椅子之间的抢占符合优先级。
7. 所有取消路径均无残留控制权。
8. 原始 SapphiArt 资产未被修改。
9. 动画和角色资产回归通过。
10. PICO 真机运行、性能报告、截图和 SHA-256 齐全。
