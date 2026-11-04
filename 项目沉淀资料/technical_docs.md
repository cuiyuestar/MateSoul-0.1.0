# MateSoul 技术文档总览

> 最后更新：2026-10-30

本文档记录 MateSoul 的技术架构、模块边界、接口约定和交接信息。项目已建立 Unity 2022.3 工程、PICO XR 环境、Lengyue 角色资产及自动验证管线；当前正式进入以 Unity Demo 为产品内核的开发阶段。

> **当前唯一生效的平台基线：** Unity 2022.3.38f1 + URP +
> `com.bytedance.pico.xr`（PICO Unity Integration SDK）+ XRI + Animation
> Rigging。Phase D0 之前的 Kotlin、FullStage、Spatial ECS 和
> `com.pico.spatial:bom` 内容均为历史记录，不得作为当前实施步骤。

## Phase 0 技术交接：首版架构设计（历史方案）

### 1. 阶段概述

MateSoul 是一个面向 PICO OS 6 的 XR 虚拟恋爱/陪伴游戏。项目参考 MateFairy 的整体能力，但首版不直接复制 MateFairy 代码，而是采用更清晰的 Avatar Runtime-first 架构。

核心目标：

- 使用 FullStage 作为主空间容器。
- 将主角从飞行精灵改为地面少女。
- 首版即纳入状态机、心情值系统、动画调度和控制权租约。
- action 玩法方案暂定，首版只保留接口边界，不实现伪 action。
- 当前 `girl.glb` 无动画轨道，首版使用 NoOp 动画播放器打通调度链路。

主计划文档：

- `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_首版开发计划.md`

### 2. 当前仓库状态

当前 MateSoul 仓库仅包含：

- `/Users/bytedance/MateSoul/girl.glb`
- `/Users/bytedance/MateSoul/.git/`
- `/Users/bytedance/MateSoul/项目沉淀资料/`

已知资产事实：

- `girl.glb` 不包含 glTF 标准动画轨道。
- 当前无法直接播放 walk / happy / sweet / angry / sad 动画。
- 后续用户会补充行走动画和多类心情表现动画。

### 3. 架构原则

MateSoul 首版采用 Avatar Runtime-first：

```text
用户输入 / LLM / 触碰 / 调试命令
    -> AvatarIntentRouter
        -> AvatarRuntime
            -> AvatarStateMachine
            -> ControlLeaseManager
            -> MoodSystem
            -> MoodTransitionQueue
            -> AnimationScheduler
            -> GroundLocomotionSystem
            -> AvatarEntityBinding
```

原则：

1. 外部模块不直接控制少女身体、动画或心情状态。
2. `AvatarRuntime` 是少女表现控制的唯一入口。
3. 状态机只管理行为状态，不直接播放动画。
4. 动画调度器只处理动画请求，不读取 action 或玩家交互全局状态。
5. 控制权租约是身体控制权的唯一事实源。
6. action 在方案未定前不得进入 LLM 支持列表。

### 4. 计划模块说明

建议包结构：

```text
com.example.matesoul
  platform/
  scene/
  avatar/
  avatar/state/
  avatar/control/
  avatar/mood/
  avatar/animation/
  avatar/locomotion/
  ai/
  orchestrator/
  ui/
```

#### 4.1 `avatar/`

`AvatarRuntime` 是虚拟少女运行时入口，负责接收 `AvatarIntent` 并协调状态机、心情值系统、动画和控制权。

#### 4.2 `avatar/state/`

状态机首版包含：

```kotlin
enum class AvatarState {
    SPAWNING,
    IDLE,
    WANDER_NEAR_USER,
    FOLLOW_USER,
    APPROACH_USER,
    STAY_NEAR_USER,
    LOCKED_REACTION
}
```

首版最小实现可先启用：

- `SPAWNING`
- `IDLE`
- `FOLLOW_USER`
- `STAY_NEAR_USER`
- `LOCKED_REACTION`

#### 4.3 `avatar/control/`

控制权租约用于替代 MateFairy 中分散的 lock / interrupt bus / runtime dependency 状态。

```kotlin
data class ControlLease(
    val owner: ControlOwner,
    val priority: ControlPriority,
    val reason: String,
    val expiresAtMillis: Long?,
    val interruptible: Boolean
)
```

优先级：

```kotlin
enum class ControlPriority(val value: Int) {
    BASE_LOCOMOTION(10),
    POSITIVE_MOOD_REACTION(25),
    NEGATIVE_MOOD_REACTION(35),
    FUTURE_ACTION(50),
    DIRECT_PLAYER_INTERACTION(80),
    DEBUG_OVERRIDE(100)
}
```

#### 4.4 `avatar/mood/`

心情类型首版固定为：

```kotlin
enum class MoodType {
    SADNESS,
    ANGER,
    HAPPY,
    SWEET
}
```

心情值系统需要记录：

- 每类心情值的 `0-100` 数值。
- 当前激活的派生状态。
- 状态回落事件队列。
- 状态 token。
- 持久化时间戳。

`MoodSystem` 不直接移动角色，也不直接播放底层动画；它根据心情值和派生状态向 `AnimationScheduler` 与 `ControlLeaseManager` 产出表现请求。

#### 4.5 `avatar/animation/`

动画系统使用语义键，不以 trackIndex 作为业务入口：

```kotlin
enum class AnimationKey {
    IDLE,
    WALK,
    HAPPY,
    SWEET,
    ANGRY,
    SAD
}
```

当前 `girl.glb` 无动画时，使用 `NoOpAnimationPlayer`。后续补充动画后，只更新 `AnimationCatalog`。

#### 4.6 `avatar/locomotion/`

`GroundLocomotionSystem` 负责地面移动：

- 读取 HMD proxy。
- 根据水平距离驱动 `FOLLOW_USER` / `STAY_NEAR_USER`。
- 使用固定 Stage floor `y = 0` 作为首版地面高度。
- 后续预留 plane / mesh grounding。

### 5. MateFairy 改进点

MateSoul 不直接复刻 MateFairy 的以下设计：

1. 不让动画模块直接读取 action lock 或玩家交互 runtime singleton。
2. 不把移动、动作恢复、语义驻留、携物和动画请求都塞进一个超大行为系统。
3. 不在多个系统中分别维护“当前谁控制角色”的状态。
4. 不用字符串情绪直接映射动画作为完整情绪系统。
5. 不向 LLM 暴露未实现或未接稳的 action。

### 6. PICO Spatial SDK 注意事项

首版应遵守：

- 使用 Stage，因为 HMD tracking 和后续感知能力需要 Full Space。
- 模型加载优先在 `SpatialView(initial = { ... })` 中完成。
- GLB 用 `Entity.loadSuspend("asset://girl.glb")`。
- 加载返回的是根实体，应将根实体挂载到场景。
- 动画 API 必须在主线程调用。
- 交互实体需要 `CollisionComponent + InteractableComponent`。
- 视觉模型和身体/碰撞代理分离，避免缩放、碰撞和动画互相影响。

### 7. 后续实现顺序

1. 基于 FullStage 模板创建 Android 工程。
2. 接入 `girl.glb` 静态加载。
3. 创建 `AvatarEntityBinding`。
4. 实现 `AvatarRuntime`、状态机和控制权租约。
5. 实现地面移动。
6. 实现情绪和 NoOp 动画调度。
7. 接入文本对话和空间气泡。
8. 后续补动画后更新 `AnimationCatalog`。

### 8. 交接约定

后续开发需要遵守：

- 文档和代码注释使用中文。
- 不直接复制 MateFairy 的大文件结构。
- 不把 action 待定能力伪装成已实现能力。
- 不在多个模块中维护重复控制权状态。
- 每完成一个阶段后更新 `项目沉淀资料/work_reports.md` 和 `项目沉淀资料/technical_docs.md`。

## Phase 0.1 技术交接：心情值系统决策

### 1. 阶段概述

本阶段根据用户补充需求，对原“情绪调度”设计进行升级：MateSoul 首版不再只做简单的 `emotion -> animation` 映射，而是建立可扩展的心情值系统。

已确认的核心决策：

- 首版完整实现难过值、生气值、开心值、甜蜜值。
- 心情值运行时事实源采用 ECS `MoodComponent`。
- 心情状态采用“数值 + 派生状态”模型。
- Soul 采用“玩家可见五维双极人格 + 内部数值因子派生”。
- 文本语义识别采用“规则 + LLM 四分分类器”。
- 心情值需要长期持久化，但应用关闭期间不结算状态时间。

### 2. 核心数据模型

心情类型：

```kotlin
enum class MoodType {
    SADNESS,
    ANGER,
    HAPPY,
    SWEET
}
```

正负分组：

```kotlin
enum class MoodValence {
    NEGATIVE,
    POSITIVE
}
```

状态等级：

```kotlin
enum class MoodLevel {
    NONE,
    LIGHT,
    MEDIUM,
    HIGH
}
```

运行时组件：

```kotlin
class MoodComponent(
    val meters: MutableMap<MoodType, MoodMeter>,
    var activeState: ActiveMoodState?,
    val pendingTransitions: MutableList<MoodTransitionSignal>
)
```

数值刻度：

```text
范围：0 - 100
轻度阈值：30
中度阈值：60
高度阈值：85
```

### 3. Soul 双层模型

玩家可见维度：

```text
敏感 - 钝感
温柔 - 强势
独立 - 依恋
坦率 - 克制
自信 - 脆弱
```

内部由 `SoulFactorMapper` 派生：

- 难过增长因子
- 生气增长因子
- 开心增长因子
- 甜蜜增长因子
- 负面表现强度因子
- 正面表现强度因子

后续实现时，不应把内部因子直接暴露给玩家。玩家编辑的是可理解的人格维度，公式使用的是派生因子。

### 4. 语义识别协议

首版使用“规则 + LLM 四分分类器”。

分类器输出：

```kotlin
data class MoodClassificationResult(
    val sadness: MoodSignalScore,
    val anger: MoodSignalScore,
    val happy: MoodSignalScore,
    val sweet: MoodSignalScore
)
```

低置信度策略：

- 低置信度结果不丢弃。
- 低置信度按正常增量的 25% 生效。

### 5. 增长公式

采用分段公式：

```text
delta = baseDelta(severity) * soulFactor(moodType) * confidenceFactor
```

默认建议：

```text
baseDelta(LIGHT) = 8
baseDelta(MEDIUM) = 18
baseDelta(HIGH) = 32
confidenceFactor = 1.0   当 confidence 达到有效阈值
confidenceFactor = 0.25  当 confidence 低于有效阈值
```

### 6. 冲突规则

负面心情：

- 难过与生气同时达到阈值时，难过优先。
- 难过优先时，清空生气值。

正面心情：

- 开心与甜蜜同时达到阈值时，甜蜜优先。
- 甜蜜优先只决定当前表现，不清空开心值。

正负冲突：

- 负面表现优先级高于正面表现。
- 正面状态不会清空负面心情值。
- 负面状态进入表现时，如果当前正处于正面表现状态，则退出当前正面激活态，并清空该正面心情自身数值。
- 负面状态进入表现时，如果当前不处于正面表现状态，则不清空任何正面心情值。
- 任一心情退出自身 active state 时，都必须清空该心情自身数值。
- “不清空”规则只针对其它心情值；退出方自己的心情值始终按退出规则清空。

### 7. 状态回落事件

采用事件信号队列，不使用协程 delay 作为主机制。

负面心情：

```text
高度难过 / 高度生气：40s 后发出转轻度信号
中度难过 / 中度生气：15s 后发出转轻度信号
轻度难过 / 轻度生气：60s 后发出转无状态信号
```

正面心情：

```text
高度开心 / 高度甜蜜：15s 后发出转轻度信号
中度开心 / 中度甜蜜：15s 后发出转轻度信号
轻度开心 / 轻度甜蜜：作为持久状态，没有默认退出时间
```

清空规则：

- 高度/中度转轻度属于同一心情类型内的等级回落，不视为退出该心情，不清空自身数值。
- 轻度负面转无状态属于退出该负面心情，必须清空该负面心情自身数值。
- 轻度正面没有默认退出时间；只有被负面心情挤占时才退出，并清空该正面心情自身数值。
- 被其它心情挤占导致退出 active state 时，被挤占的心情必须清空自身数值。

事件消费规则：

- 每次进入状态时生成 `stateToken`。
- 回落事件携带 `stateToken`。
- 只处理与当前激活状态 token 匹配的事件。
- 如果控制权被占用，事件挂起。
- 控制权释放后重新校验 token；不匹配则丢弃。

### 8. 与控制权租约关系

控制权优先级需要区分正负心情表现：

```kotlin
enum class ControlPriority(val value: Int) {
    BASE_LOCOMOTION(10),
    POSITIVE_MOOD_REACTION(25),
    NEGATIVE_MOOD_REACTION(35),
    FUTURE_ACTION(50),
    DIRECT_PLAYER_INTERACTION(80),
    DEBUG_OVERRIDE(100)
}
```

高度难过状态的特殊规则：

- 少女原地站立不动。
- `GroundLocomotionSystem` 必须让出移动控制。
- 状态回落信号如果被 future action 占用阻塞，需要 token 挂起等待。

### 9. 持久化约定

首版需要长期持久化：

- `MoodMeter` 数值。
- 当前激活态。
- 状态 token。
- 时间戳。

重要约定：

- 应用关闭期间不结算状态时间。
- 重新进入应用后，从保存时的状态继续。
- 后续若体验上负面状态冻结过强，再单独设计离线结算或安全恢复。

## Phase 0.3 技术交接：Trae + pico-cli 工具链固化

### 1. 阶段概述

本阶段将 MateSoul 后续开发主路线调整为 Trae + pico-cli 直跑。Claude Code 已安装并保留，但由于登录入口暂不可用，不作为当前主链路依赖。

当前工具链边界：

```text
Trae：负责代码协作、文件修改、命令调度与项目上下文维护
pico-cli：负责 PICO 工具链、模拟器、设备、应用安装、MCP 与知识库能力
Claude Code：保留安装，后续账号入口可用后再恢复
```

### 2. 新增文件与职责

新增文件：

- `/Users/bytedance/MateSoul/.gitignore`
- `/Users/bytedance/MateSoul/.mcp.json`
- `/Users/bytedance/MateSoul/scripts/pico-env.sh`
- `/Users/bytedance/MateSoul/scripts/pico-run.sh`
- `/Users/bytedance/MateSoul/scripts/pico-doctor.sh`
- `/Users/bytedance/MateSoul/scripts/mcp-spatial-knowledge.sh`
- `/Users/bytedance/MateSoul/scripts/mcp-spatial-editor.sh`
- `/Users/bytedance/MateSoul/项目沉淀资料/Trae_PICO_CLI_工作流.md`
- `/Users/bytedance/MateSoul/.trae/development-log.md`

脚本职责：

```text
pico-env.sh
  统一设置 MATESOUL_ROOT、PICO_HOME、Primer CLI 前缀、JAVA_HOME、npm registry 与 PATH。

pico-run.sh
  在统一环境下执行 pico-cli，避免每次手动 source 和拼接环境变量。

pico-doctor.sh
  一键执行 pico-cli version、doctor、plugin doctor、mcp doctor 与 primer-cli doctor sdk。

mcp-spatial-knowledge.sh
  作为项目级 MCP wrapper，启动 pico-cli mcp spatial-knowledge。

mcp-spatial-editor.sh
  作为项目级 MCP wrapper，启动 pico-cli editor bootstrap。
```

### 3. 本地工具链目录

为规避 Trae 沙箱对 home 目录写入的限制，本项目使用项目内工具目录：

```text
/Users/bytedance/MateSoul/.pico-home
  项目内 PICO_HOME，包含 agent-vault 0.13.3 和 6.0 包。

/Users/bytedance/MateSoul/.pico-cli-workspace
  项目内 pico-cli 插件工作区，包含 spatial-agentic-tools 与 primer-cli。
```

这两个目录属于本地工具链缓存，已在 `.gitignore` 中排除。

### 4. MCP 状态

当前 `pico-cli mcp doctor` 验证结果：

```text
Spatial Knowledge:
  0.13: graph.json OK
  graphify serve: READY

Spatial Editor:
  bootstrap: READY
```

注意：`6.0` agent-vault 包当前没有 `graph.json`，doctor 会自动回落到 `0.13` 中可用的 graph 数据。

### 5. 已知限制

1. `pico-cli editor doctor` 会尝试写入 `~/.pico-cli/editor-state.json.lock`，在 Trae 沙箱下可能被拦截。
2. 当前 `pico-cli` 版本没有 `mcp:unity-cli` 命令，Unity Editor 深度自动化需要后续单独确认 Unity MCP bridge。
3. `pico-cli plugin doctor` 仍提示 Codex、Copilot、TraeCLI 未安装插件，这是非当前主路线宿主警告，不影响 Trae + pico-cli 直跑。

### 6. 标准使用方式

推荐入口：

```bash
cd /Users/bytedance/MateSoul
scripts/pico-doctor.sh
scripts/pico-run.sh mcp doctor
scripts/pico-run.sh emulator doctor
```

后续所有 PICO 工具链命令优先通过 `scripts/pico-run.sh` 执行。

## Phase D0 技术交接：Demo 开发基线

### 1. 阶段概述

本阶段冻结 MateSoul Demo 的产品范围和技术路线。完整方案：

- `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_Demo开发方案.md`

当前生效的技术路线为：

```text
Unity 2022.3.38f1
  + URP
  + PICO XR
  + XR Interaction Toolkit
  + Animation Rigging
```

此前 Kotlin、FullStage、Spatial ECS 和 GLB 静态角色方案不再作为实现基线。旧方案中的 Avatar Runtime-first、心情值系统、语义动画和控制权唯一事实源继续保留，并翻译为 Unity/C# 架构。

### 2. Demo 核心链路

```text
基础 UI
  -> 文字对话与字幕
  -> 角色 Idle / Wander
  -> 虚拟椅子放置
  -> AI / UI / Random 触发坐椅
  -> 玩家抓手
  -> 坐姿被打断
  -> 角色牵手跟随
  -> 松手后恢复自主行为
```

该链路是后续版本的 Golden Path。新增功能必须保持该链路可回归。

### 3. 运行时边界

统一入口：

```text
UI / AI / Random / Player
  -> AvatarIntentRouter
  -> AvatarActionCoordinator
  -> ControlLeaseManager
  -> Animation / Locomotion / IK / Item Adapters
```

控制通道：

- `Locomotion`
- `FullBody`
- `LeftArm`
- `RightArm`
- `Gaze`
- `Face`

优先级：

```text
SystemSafety       100
PlayerHandHold      90
ExplicitCommand     60
RandomInteraction   30
AutonomousMovement  10
```

所有动作必须实现准入、开始、持续更新、取消、完成和清理。UI、AI 和随机行为不得直接操作 Animator、角色 Transform 或骨骼。

### 4. MateFairy 参考边界

保留：

- Application-scope Runtime 集中装配。
- `ILLMProvider` 与 `ConversationOrchestrator` 分层。
- 结构化 AI 响应。
- Action Request、Registry、可取消 Action 生命周期。
- 视觉模型和物理代理分离。

不照搬：

- Kotlin 和 Spatial ECS 平台代码。
- 超大 `HomeStage`、`FairyBehaviorSystem`。
- 多个全局 `RuntimeDependencies`。
- 重复的 ActionRegistry。
- Prompt 中多处硬编码 Action。
- 由 LLM 直接控制角色或物品。

### 5. 牵手 IK 约定

使用 Animation Rigging `TwoBoneIKConstraint`。IK Target 必须是世界空间独立对象，代码只修改 Target 和 IK Weight，不直接修改 Hand 骨骼。

牵手移动采用手臂软范围、硬范围和角色根节点补偿移动：

1. 软范围内只使用 IK。
2. 超出软范围时由 `LocomotionController` 驱动角色步行。
3. 超出硬范围时限制 Target 并提供反馈。
4. 跟踪丢失、瞬移、距离异常和场景销毁时自动释放。

现有 `HeadPoseGuard` 仅作为旧动画兼容策略。正式 Demo 不允许它在 IK 计算后覆盖 Hips、Spine、Chest、Neck 或 Head。

### 6. 虚拟物品约定

虚拟物品由 `VirtualItemDefinition` 描述，并统一注册到 `VirtualItemRegistry`。AI、UI 和随机行为通过同一 Registry 和 Action 使用物品。

Demo 椅子必须提供：

- `ApproachPoint`
- `SeatSocket`
- `ExitPoint`
- `SeatForward`
- `Occupancy`

坐姿流程固定为：

```text
Reserve -> Approach -> Align -> SitDown -> SitIdle -> StandUp -> Release
```

Demo 使用预制 SeatSocket，不在运行时根据任意网格猜测座位点。现实椅子语义识别后续通过新的空间目标适配器接入。

### 7. 开发顺序

```text
D0 基线与工程骨架
D1 UI 与字幕
D2 自主行为
D3 AI 对话
D4 牵手 IK
D5 虚拟物品与坐椅
D6 集成与 PICO 真机验证
```

不得跳过 D0 直接堆叠玩法。每个阶段都必须保持已完成阶段可运行，并补齐对应 EditMode、PlayMode 或设备验证。

### 8. 交接约定

- Demo 主场景和核心 Runtime 直接演进为正式版，不另建平行正式工程。
- 新动作只能通过 `IAvatarAction` 注册。
- 新物品只能通过 Definition 和 Capability 扩展。
- 新输入必须转换成 AvatarIntent。
- 新 AI 能力只能输出注册表允许的结构化 Action。
- 所有关键动作记录 requestId、actionId、targetId、leaseId、状态变化、结果和耗时。
- 每次 PICO 交付记录 Git commit、APK SHA-256、设备型号、系统版本和验证证据。

## Phase Asset-Handoff 技术交接：冷月动画制作包

### 1. 交付结构

```text
Lengyue_AnimatorKit_V001/
  Lengyue_AnimationMaster.blend
  Lengyue_CurrentAppearance_Rig.fbx
  README.md
  Textures/
  Reference/
  Reports/
  SHA256SUMS.txt
```

生成脚本位于：

- `ArtSource/SapphiArt/Release/Lengyue/AnimatorKit/scripts/build_animator_kit.py`
- `ArtSource/SapphiArt/Release/Lengyue/AnimatorKit/scripts/validate_animator_kit.py`
- `MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/V001/Editor/LengyueAnimatorSnapshot.cs`

### 2. 生成流程

1. `LengyueAnimatorSnapshot` 在 Unity Editor 的 Preview Scene 中实例化当前 prefab。
2. 对当前可见 SkinnedMeshRenderer 执行 `BakeMesh`，采集实际可见网格、UV、
   三角形、骨骼权重、材质贴图和 22 个 BlendShape 差值。
3. Blender 构建器打开现有 IK AnimationMaster，保留原始 116 骨 Rest Matrix。
4. 当前头发、服装、NeckExtension 和 SkinCoverage 由 Unity 快照重建。
5. 已验证的源 DCC 脸、眼、牙齿和 Shape Key 被保留，眼睛材质替换为
   `Lengyue_Eye_v002_Albedo.png`。
6. 导出含模型、蒙皮、BlendShape、身体骨和头发参考骨的 Binary FBX 7.4。
7. 验证器重新导入 FBX，核对骨数、网格数、表情数和贴图，再生成 ZIP 与校验码。

### 3. 动画数据边界

- 身体动作：原始 116 根变形骨骼，30 FPS 逐帧烘焙。
- 眼球转动：`Eye_L`、`Eye_R` 骨骼旋转。
- 眨眼：`face_eye_close_L_BS`、`face_eye_close_R_BS`。
- 笑眼/睁大眼：对应 `face_eyeHappy_*`、`face_eye_wide_*` Shape Key。
- 头发：不进入普通身体动画，由 Unity `HairSpringPhysics` 处理。
- 裙骨：骨架层级保留，但身体动画不得包含 `Skirt_*` 曲线。
- 控制器：`CTRL_`、`MCH_`、`DRV_` 仅存在于 Blender 主文件，不直接进入 Unity。

纯骨骼 FBX不能表达 BlendShape 动画。若动作包含表情，动画师必须回传 DCC 源文件，
并额外导出包含脸部网格和 BlendShape 曲线的 FBX。

### 4. 已验证不变量

- 身体变形骨：116；身体总骨（含 IK 辅助）：141。
- 头发参考骨：247。
- 当前外观网格：10。
- 表情通道：22。
- 可移植贴图：4，Blender 内嵌且 FBX round-trip 可识别。
- 眼骨驱动测试：最大顶点位移 `0.0087249102 m`。
- 手部 IK 测试：端点位移 `0.0512272865 m`。
- 原始骨架 Rest Matrix 保持不变。
- `Mech_In_L/R` 与 Unity prefab 的已知参考差异约 5 mm，构建器保留原动画骨架
  Rest Pose，不把 Unity 侧附件偏移反写回骨架。

### 5. 注意事项

1. 原厂 FBX 材质保存了作者电脑上的绝对贴图路径，不能直接作为可移植交付。
2. Blender 主文件是首选入口，因为它保留 IK 控制器并内嵌贴图。
3. Maya/3ds Max 使用通用 FBX；若未自动解包贴图，按 `README.md` 重连。
4. DCC 基础材质与 Unity URP 光照不做像素级一致保证，但模型几何和纹理来源一致。
5. 当前 BodyOnly 清理器会去除眼球和表情曲线，新表情资产需单独设计导入层。

## Phase Rendering 技术交接：无描边 URP Toon P0

### 1. 阶段概述

冷月最初建立独立的无描边 Toon 预览分支，并在 P0.2 阶段将同一材质配置接入
正式角色 Prefab。Toon 资源位于：

`/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/V001/Toon/`

### 2. 核心资源

- `Shaders/MateSoulCharacterToon.shader`
  - 唯一前向 Pass：`ToonForward`
  - 使用二阶漫反射、彩色阴影、硬边高光、弱边缘提亮
  - 复用 URP `ShadowCaster` 和 `DepthOnly`
  - 没有 Outline Pass
- `Editor/LengyueToonPreviewBuilder.cs`
  - `Build()`：创建材质、Prefab、预览场景和验证图
  - `CreateMaterial()`：从正式材质继承 `_BaseMap`、颜色和 UV Transform
  - `CaptureEvidence()`：固定灯光、相机、姿势和分辨率生成前后对照
  - `.dbg/build-toon-preview` 可触发无人值守重建
- `Prefabs/Lengyue_GameCharacter_ToonPreview.prefab`
  - 保留用于固定姿势截图的独立预览变体
- `Validation/ToonPreviewReport.json`
  - 记录 Shader、材质、场景、截图和 `outlineEnabled=false`

### 3. 着色流程

1. 采样当前生产 BaseMap 与 BaseColor。
2. 使用 `N dot L`、阈值和 Softness 生成二阶光照色带。
3. 将主灯 Shadow Attenuation 按 `_ReceiveShadows` 混入色带。
4. 叠加低强度 SH 间接光。
5. 头发/服装可叠加阈值化 Toon Specular。
6. 通过 `_UnlitBlend` 控制脸和眼睛的弱光照比例。
7. 最终执行 URP Fog；不执行任何轮廓扩张或屏幕空间边缘检测。

### 4. 分部参数约定

- Face：`UnlitBlend=0.9`、无 Specular、无 Rim，作为无 Face SDF 时的平涂方案。
- Eye：`UnlitBlend=0.92`，保留少量硬高光。
- Hair：二阶阴影、硬边紫色高光、低强度 Rim。
- Cloth：二阶阴影和很弱的材质高光。
- SkinCoverage：偏暖阴影，兼顾袖口和裙边外露皮肤。

### 5. 自动验证

验证环境固定为：

- 1024 x 1024
- 正交相机
- 灰色背景
- 正前上方单一 Directional Light
- Flat Ambient
- Source 与 Toon 使用相同相机、姿势和灯光

证据输出：

- `Evidence/before_after_front.png`
- `Evidence/toon_detail_views.png`
- `Evidence/after_face.png`
- `Evidence/after_threequarter.png`

### 6. 避坑与边界

1. Directional Light 的 Transform Forward 是光线传播方向；证据灯方向设反会导致
   Simple Lit 正面全暗，形成失真的对比结论。
2. 普通法线 Toon 不适合二次元脸部，会在鼻翼和眼窝形成随光变化的块状阴影。
   没有可靠 Face SDF 时应使用高比例弱光照平涂。
3. 弱 Rim 只是表面边缘提亮，不是描边；Shader 内不得加入反向外壳或屏幕空间
   Outline Pass。
4. P0 只验证渲染方向。Face SDF、Hair Specular Mask、PICO 性能和动画全帧
   稳定性属于后续门禁。

### 7. 运行时接入状态

- `runtimeReady=true`：Shader、材质和 Toon Prefab 都是 Unity 运行时可直接渲染的
  资产，不依赖截图后处理。
- `integratedIntoProductionPrefab=true`：正式
  `Lengyue_GameCharacter_V001.prefab` 的目标 Renderer 已替换为 Toon 材质。
- `LengyueCharacterBuilder` 会在每次重建正式 Prefab 时重新应用 Toon 材质，防止
  回退到 Simple Lit。
- 项目当前所有通过正式 Lengyue Prefab 创建的角色实例默认直接使用 Toon 渲染；
  独立 Toon Preview Prefab 仅用于固定证据采集。

### 8. 衣服拓扑与色阶平滑

衣服实测数据：

- `LavenderCasual_Garments`：42,988 顶点，68,915 三角面。
- `LavenderCasual_SkinCoverage`：8,704 顶点，9,060 三角面。
- 合计：51,692 顶点，77,975 三角面。

该密度已高于“低模服装”，继续整体细分会显著增加 Skinned Mesh 顶点变换成本和
PICO GPU 压力。画面中的多边形感来自硬 Toon 色阶跨过平滑法线场时形成的阶梯边界。

修正方式：

```hlsl
half ndotl = saturate(dot(normalWS, light.direction));
half adaptiveSoftness = max(
    _ShadowSoftness, fwidth(ndotl) * 0.8h);
half band = smoothstep(
    _ShadowThreshold - adaptiveSoftness,
    _ShadowThreshold + adaptiveSoftness,
    ndotl);
```

屏幕导数只平滑色阶边界，不改变模型轮廓、UV、蒙皮权重或动画。若后续仍认为裙摆
轮廓存在折线，应只对轮廓区域在 DCC 中定向加线并重新传递蒙皮，不应整体细分。

### 9. P0.2 布料光照与正式 Prefab 接入

#### 9.1 布料光照

上半身的棱角感主要来自二阶光照在肩袖与胸衣法线场上的硬切换，不是三角面不足。
Shader 最终使用包裹光和屏幕导数共同控制明暗过渡：

```hlsl
half ndotl = saturate(
    (dot(normalWS, light.direction) + _DiffuseWrap)
    / (1.0h + _DiffuseWrap));
half adaptiveSoftness = max(
    _ShadowSoftness, fwidth(ndotl) * 0.8h);
half band = smoothstep(
    _ShadowThreshold - adaptiveSoftness,
    _ShadowThreshold + adaptiveSoftness, ndotl);
```

服装参数：

- `_ShadowThreshold=0.50`
- `_ShadowSoftness=0.085`
- `_DiffuseWrap=0.32`
- `_IndirectStrength=0.20`
- `_UnlitBlend=0.02`

`DiffuseWrap` 让侧向受光区域保留柔和亮度，`fwidth` 仅在屏幕空间色阶边界过窄时
提供最小平滑宽度。两者都不修改网格、蒙皮、UV 或碰撞数据。

#### 9.2 默认材质接入

核心 API：

- `LengyueToonPreviewBuilder.ApplyDefaultToonMaterials(GameObject character)`
  - 按 Renderer 名称应用 Face、Eye、Hair、Cloth、SkinCoverage 五类材质。
- `LengyueToonPreviewBuilder.UsesDefaultToonMaterials(GameObject character)`
  - 验证正式角色是否完整使用 `MateSoul/Character Toon`。
- `LengyueCharacterBuilder.Build()`
  - 在头发和服装组装完成后调用 `ApplyDefaultToonMaterials()`，再保存正式 Prefab。

正式 Prefab 的 10 个门禁 Renderer：

- `Lengyue_Bob_Hair_v002`
- `BottomTeeth`
- `TopTeeth`
- `brow`
- `eyes`
- `face`
- `tongue`
- `LavenderCasual_Garments`
- `LavenderCasual_NeckExtension`
- `LavenderCasual_SkinCoverage`

运行时探针要求这 10 个 Renderer 的所有材质 Shader 均为
`MateSoul/Character Toon`，否则直接抛出异常，不能生成通过报告。

#### 9.3 姿势截图管线

`CaptureEvidence()` 对 Rest/T Pose、Idle、Walk、Run 分别生成：

1. 正面全身。
2. 侧面全身。
3. 背面全身。
4. 脸部近景。

Walk 固定采样 `8/30 s`，Run 固定采样 `5/30 s`。每个姿势先执行
`SkinnedMeshRenderer.BakeMesh`，再创建不可变代理，避免不同截图混用旧蒙皮缓存。
Unity 2022.3 的 BakeMesh 结果已经包含 Renderer Scale，因此代理必须使用
`localScale=Vector3.one`，否则 SkinCoverage 的 `0.01` 缩放会被重复应用。

#### 9.4 运行时门禁

`LengyueRuntimeValidationProbe` 在 PlayMode 第 60 帧检查：

- `defaultToonMaterialsVerified=true`
- `toonRendererCount=10`
- `outlineEnabled=false`
- `hairPhysicsInitialized=true`
- `hairSimulatedJoints=68`
- `skirtPoseGuardInitialized=true`

生成截图前先删除旧 `runtime_default.png`，防止等待逻辑把历史截图误判为本轮产物。
第二次复测于 `2026-09-23 23:04:42` 同时重写截图和 JSON，输出
`LENGYUE_TOON_PLAYMODE_PASS`，随后删除临时请求并自动退出 PlayMode。

#### 9.5 证据与报告

- 16 张原始图及合成图：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/V001/Toon/Evidence/`
- 静态报告：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/V001/Toon/Validation/ToonPreviewReport.json`
- PlayMode 报告：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/V001/Toon/Validation/ToonPlayModeReport.json`
- Walk/Run 全帧报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Release/Lengyue/V001/reports/locomotion_validation.json`

Walk 33 帧与 Run 21 帧均无裙腿三角形相交，且头颈最大表面间隙分别为
`0.00000099985 m` 和 `0.00000103104 m`。这部分验证的是正式 Prefab，而不是
独立预览替身。

## Phase Accessories 技术交接：四芒星胸针 V001

### 1. 几何与源文件

源目录：
`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Accessories/StarBrooch/V001/`

`scripts/build_star_brooch.py` 使用分段 Bézier 四芒星轮廓及深度环生成实体外框和星芯，
保留环状四边面拓扑及平面 UV。源文件 `Lengyue_StarBrooch_Master.blend` 含两个
独立可编辑对象。FBX 不含骨架或动画；Unity 几何 JSON 使用 X 横向、Y 向上、Z 向前，
三角形绕序与原法线保持一致。

外框 3,072 三角面，星芯 1,344 三角面，总计 4,416；两者非流形边均为 0。
游戏中合为一个 Renderer，使用珐琅外框、深紫凹面、珍珠星芯三个材质槽。

### 2. 正式角色挂接

`LengyueStarBroochBuilder` 位于：
`/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/V001/Editor/LengyueStarBroochBuilder.cs`

流程：

1. 读取几何 JSON，保存原点居中的独立 Unity Mesh。
2. 在胸前高度 `1.306 m` 拟合刚性配饰倾角；本轮结果为绕 X 轴 `-46°`。
3. 根据衣服前表面深度确定前后位置，保留最少 `1.5 mm` 深度差。
4. 写入佩戴网格与现有 `Chest` 的 Bind Pose，全顶点单骨权重为 1。
5. 将 `Lengyue_StarBrooch` SkinnedMeshRenderer 加入正式 Prefab。
6. 保存骨架姿态、现有网格不变的断言结果，再执行姿态验证并截图。

`LengyueCharacterBuilder` 调用 `AttachToCharacter()`，确保以后重建仍包含配饰。
不修改 `Assets/SapphiArt`，不向现有动画添加曲线，不添加物理模拟或 LateUpdate 跟随脚本。

### 3. 验证与运行时入口

`StarBroochValidation.json` 检查 Idle 41 帧、Walk 33 帧、Run 21 帧与胸部 ±20°。
将姿态后的衣服和配饰变换回胸部参考坐标系，比较配饰采样点与衣服前表面深度；
同时比较实际蒙皮位置与预期 Chest 刚体变换结果。

最终最大跟随误差约 `2.4e-7 m`；动画采样最小深度间距约 `0.00297 m`。
该指标仅覆盖本次采样的配饰与衣服区域，不声称已执行任意动作的完整三角形相交检查。

新的运行时验证场景直接引用正式 Prefab：
`/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/V001/Accessories/StarBrooch/Validation/Lengyue_StarBrooch_Validation.unity`

`.dbg/start-toon-playmode` 现在打开该场景。运行时探针在第 60 帧要求 11 个目标 Renderer
均使用 Toon Shader，并要求胸针可见、单骨绑定 Chest、三个材质槽。最终报告：
`broochAttachmentVerified=true`，`toonRendererCount=11`。

### 4. 注意事项

- BakeMesh 采图和几何检查沿用已验证的无重复缩放转换，勿再乘服装的 `0.01` 缩放。
- 保存已有 Mesh 时显式清理并写入网格缓冲区，避免 CopySerialized 留下旧缓冲。
- 独立配饰相机使用 `nearClipPlane=0.001`，确保浅浮雕星芯不被裁切。
- 旧 `Lengyue_V001_Validation.unity` 不是正式 Prefab 的同步实例，不应用来证明新增
  配饰已在生产资产生效；采用上述新场景。
- 配饰宽高为独立模型尺寸，佩戴后受胸前倾角影响，正面投影高度会缩短。

### 5. 交付目录

- FBX、独立/绑定网格及材质：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/V001/Accessories/StarBrooch/`
- 多视图与佩戴截图：上述目录 `Evidence/`。
- 姿态和 PlayMode 报告：上述目录 `Validation/`。
- 146 项原始资产保护结果：源目录 `reports/protected_assets_validation.json`。

## Phase Provenance V002 技术交接：独立运行时角色与发布门禁

### 1. 阶段概述

V002 将 V001 的已验证外观和运行时行为转换为普通 Prefab，消除对
`Assets/SapphiArt/` 的直接和传递依赖，同时保留许可来源说明。核心目录：

`/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/V002/`

原始供应商资产与 V001 继续作为只读输入和回滚基线，不允许删除或覆盖。

### 2. 核心类

`LengyueV002ReleaseBuilder`：

1. 从 V001 实例化并执行 `PrefabUnpackMode.Completely`。
2. 删除 24 个禁用旧 Renderer、Inactive `faceFlat` 和
   `SapphiArtChan_AnimManager`。
3. 为保留 Renderer 复制 Mesh、Material、Texture，生成 V002 自有 GUID。
4. 创建 BodyOnly Idle/Walk/Run 和独立 Animator Controller。
5. 保留 `param_toidle`、`param_idletowalk`、`param_idletorunning` 等应用参数。
6. 保存普通 Prefab，检查依赖闭包、骨骼、BlendShape、Renderer 状态和名称。
7. 删除上次生成但本轮已无引用的 Mesh/Material/Texture，保证重复构建稳定。

`LengyueV002RuntimeProbe`：

1. 等待运行时组件初始化。
2. 通过参数驱动 Idle -> Walk -> Idle。
3. 同时采集头发模拟关节数和 substep。
4. 要求 Animator、`SkirtPoseGuard`、`HeadPoseGuard`、`HairSpringPhysics`
   均可用。
5. 将结果写入 `Lengyue_V002_RuntimeReport.json`。

### 3. 发布不变量

- Prefab 类型必须是 `Regular`，不能是 `Variant`。
- `AssetDatabase.GetDependencies()` 结果不得以 `Assets/SapphiArt/` 开头。
- 生产 Renderer 固定为 11 个。
- 身体骨固定为 116，头发骨固定为 247，表情通道固定为 22。
- Animator Avatar 为空，Controller 必须位于 V002 Runtime。
- Idle、Walk、Run 只能包含身体白名单曲线；不得写头、脸、头发或裙骨。
- 原有 Animator 参数名保留，避免破坏 `virtual-friend` 控制脚本契约。
- 所有 Mesh/Material/Texture 必须位于 V002 Runtime 子目录。
- 第三方声明必须存在于 `Assets/StreamingAssets/THIRD_PARTY_NOTICES.txt`。

### 4. 重复构建避坑

1. 不要删除再创建 Animator Controller。已加载 Scene 会保留被销毁的 Controller
   对象并在 PlayMode 表现为 `runtimeAnimatorController=null`。V002 现在原地清理
   State/Transition/Parameter，保留 Controller GUID。
2. 验证场景已打开时不能再创建同路径 Additive Scene 并覆盖保存。构建器检测该
   状态后用 Single Scene 原位重建。
3. `AnimatorStateInfo.IsName()` 在短过渡和低前台帧率下可能只看到旧 Current
   State。运行时探针同时检查 Current 与 Next State 的 `shortNameHash`。
4. 仅遍历 `ShaderUtil` 当前属性不能发现非生产 MeshRenderer 引用。V002 直接删除
   `faceFlat` 整个 GameObject，并以最终依赖闭包作为准出标准。

### 5. APK 审计

普通审计 APK 已确认：

- V002 场景构建成功。
- `assets/THIRD_PARTY_NOTICES.txt` 存在且内容正确。
- 无供应商 Prefab、FBX、材质、纹理、Controller 或动画依赖。
- `global-metadata.dat` 与 `globalgamemanagers` 仍含供应商 Demo 类名；原因是 Unity
  会把 `Assets` 下未引用的 Runtime MonoBehaviour 也编入全局类型元数据。

隔离构建脚本：

`/Users/bytedance/MateSoul/scripts/build_lengyue_v002_clean_player.sh`

它将工程复制到临时目录并排除 `Assets/SapphiArt/`，再调用
`BuildStagedAuditPlayer()`。本机并行 headless Unity 缺少独立许可证，脚本逻辑已
完成但该构建未验证；CI 必须提供有效 Unity headless license。

### 6. 资源与报告

- 正式 Prefab：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/V002/Prefabs/Lengyue_GameCharacter_V002.prefab`
- 静态报告：
  `V002/Validation/Lengyue_V002_ProvenanceReport.json`
- PlayMode 报告：
  `V002/Validation/Lengyue_V002_RuntimeReport.json`
- 完整整改报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Release/Lengyue/V002/reports/copyright_remediation_20260924.md`
- APK 扫描报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Release/Lengyue/V002/reports/player_package_scan.json`

### 7. virtual-friend 边界

`virtual-friend` 当前使用 Built-in Render Pipeline 和 Windows 本地 PICO 包路径；
V002 使用 URP。未经渲染管线迁移、包依赖修复和场景 IK 重绑定验证，不得自动替换
其 Build Scene。当前只加入 `StreamingAssets` 来源声明，原场景保持不变。

## Phase Motion Preview 技术交接：Unity PlayMode 视频录制

### 1. 目标与边界

本管线用于检查角色资产在 Unity 中的实际 Walk/Run 动态，不是 PICO 真机录屏，
不构建 APK。录制输入固定为正式 V001 Prefab 和已验证的 BodyOnly 动画；输出为
1280 × 720、30 FPS、H.264 MP4。

### 2. 核心类

`LengyueMotionVideoCaptureBuilder`：

1. 创建 Additive 临时 Scene，不保存、不覆盖当前打开的 V002 Scene。
2. 实例化 `Lengyue_GameCharacter_V001.prefab`。
3. 建立固定三分之四正面相机和 Toon 验证灯光。
4. 绑定 Walk/Run BodyOnly Clip 和临时输出目录。
5. 进入 PlayMode；退出后依据 Recorder 根对象识别并关闭临时 Scene。

`LengyueMotionVideoRecorder`：

1. `DefaultExecutionOrder=10000`，在 Animator 求值后调用
   `AnimationClip.SampleAnimation()`。
2. `HairSpringPhysics=11000`、`SkirtPoseGuard=12000`、
   `HeadPoseGuard=12100` 随后处理同一帧姿势。
3. 每个动作预热 30 帧，再以 `Camera.Render` 输出 150 张 PNG。
4. 跟踪 `Hand_R` 的世界位置包围范围，低于 `0.02 m` 判定为静态录制并失败。
5. 检查胸针、Toon 材质和三个运行时保护组件，写出机器可读报告。

### 3. 踩坑

- 首轮使用手动 PlayableGraph 时，本项目 Animator Controller 在后续阶段覆盖了
  BodyOnly 姿势，视频只剩物理微抖。仅检查“帧哈希不同”会产生假通过。
- 修正后在 `LateUpdate`、执行序号 10000 直接采样身体动画，使动画先于头发和姿势
  保护组件生效，并以手部实际位移作为业务不变量。
- Additive 临时 Scene 的未保存名称显示为 `Untitled`，不能依赖 Scene 名称清理；
  应通过根对象 `Unity Motion Video Recorder` 识别。
- 按整秒抽帧可能恰好落在循环动画的同相位。动态检查应使用一个循环内的多相位帧，
  或直接使用骨骼位移指标。

### 4. 编码与交付

临时 PNG 使用 ffmpeg 编码：

- `libx264`
- `CRF 18`
- `yuv420p`
- `faststart`

最终两段视频均为 5 秒、150 帧，ffprobe 读取和 ffmpeg 全帧解码通过。临时 PNG
序列及抽帧检查图已删除，交付目录为：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Release/Lengyue/V001/motion_preview/`

## Phase Wardrobe V004 技术交接：参考图双层吊带裙

### 1. 阶段概述

本阶段在不修改原始 `Assets/SapphiArt` 和 V002 的前提下，为冷月建立 V003
服装变体。新版服装包括平直胸口吊带裙、两根肩带、布包纽扣、紫色短袖、
荷叶袖口和浅紫色双层裙摆；保留腰间蝴蝶结，移除参考图中不存在的星形胸针。

V003 继续使用 116 根身体骨、原骨骼层级和 Rest Pose，并保留 V002 的
`HairSpringPhysics`、`SkirtPoseGuard`、`HeadPoseGuard` 及无描边
`MateSoul/Character Toon` 渲染。

### 2. 构建架构

`build_layered_dress.py` 负责 Blender 侧程序化建模和蒙皮：

1. 从只读服装源加载身体骨架、已有服装和腰间蝴蝶结。
2. 构建吊带裙上身、肩带、纽扣、短袖、外露荷叶袖口和双层裙摆。
3. 上下裙层使用相同的前后裙骨权重场，避免奔跑时产生相反扭转。
4. 外露袖口使用连续 UpperArm 权重，避免 Chest/UpperArm 混合牵拉形成尖角。
5. 对原 BowLoop 和 Knot 几何计算 SHA-256，构建结束后再次校验，确保蝴蝶结主体
   未被改写；垂绳只做贴合新裙面的位移。
6. 输出独立 Blender 源文件、FBX、纹理和机器可读构建报告。

`LengyueLayeredDressBuilder` 负责 Unity 侧集成：

1. 从 V002 Prefab 派生 V003，不回写 V002。
2. 导入新服装 FBX，将 116 根身体骨按名称重绑定到 V002 角色骨架。
3. 针对 FBX 导入后的 `Head` -> `Head 1` 名称变化执行显式映射。
4. 重算 bindposes，并验证蒙皮后的参考姿势误差。
5. 保留身体、脸、头发、皮肤覆盖和 NeckExtension，删除星形胸针。
6. 创建正式 V003 Prefab、验证场景、截图和 BakeMesh 几何快照。

### 3. 验证架构

服装网格的第二套 UV `ValidationPart` 用于标记验证区域：

- `1`：上层裙摆
- `2`：下层裙摆
- `3`：T 恤与袖口
- `4`：吊带裙上身

`validate_unity_geometry.py` 读取 Unity 实际 `BakeMesh` 的二进制快照，并通过
Blender `BVHTree.overlap()` 检查 56 个姿势。准出指标只覆盖用户可见且应当分离的
表面：上下裙摆与腿部、上下裙层、外露袖口与手臂。吊带裙与内层 T 恤、隐藏袖根
属于有意搭接，只作为信息指标，不能据此宣称“全部内部表面零相交”。

`LengyueLayeredDressProbe` 在 PlayMode 中通过 Animator 参数进入 Walk/Run，
每个动作采集 90 帧，并验证：

- 动作状态实际到达，手部运动距离大于 `0.02 m`。
- Toon 材质、68 根活动头发关节及 Skirt/Head Guard 均生效。
- 订阅 `Application.logMessageReceived`，任何
  `SkinnedMeshRenderer: Rendering stopped` 都使验收失败。
- 记录 55 帧、14 对头颈边界点的最大间隙。

### 4. Unity Mesh 顶点流避坑

顶点布局发生变化后，不能用 `EditorUtility.CopySerialized()` 将新 Mesh 覆盖到
旧 Mesh 资产。Unity 可能保留旧 GPU vertex stride，导致 `BakeMesh` 正常但实时
`SkinnedMeshRenderer` 停止渲染，形成静态检查假通过。

V003 的处理方式是从 `vertices`、`normals`、`tangents`、`uv`、`uv2`、
`boneWeights` 和各 submesh triangles 重建标准顶点流，再删除旧 Mesh 资产并使用
`AssetDatabase.CreateAsset()` 创建新资产。运行时日志门禁是该问题的最终
Fail-closed 保障，不能删除。

### 5. 关键验证结果

- Blender：56,912 顶点、113,128 三角面，最大权重误差 `4.2841e-8`。
- FBX 回读：最大表面误差 `4.1650e-9 m`。
- Unity：64,574 导入顶点，重绑定最大误差 `7.2151e-7 m`。
- 56 姿势：上下裙摆与腿、上下裙层、外露袖口与手臂相交数均为 0。
- 头颈：55 帧最大间隙 `3.85725e-7 m`。
- PlayMode：Walk/Run 各 90 帧，`meshRenderingErrors=0`。
- 视频：960 × 960、30 FPS、90 帧、3 秒、H.264/yuv420p，全帧解码通过。

### 6. 资源路径

- Blender 源文件：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v004/source/Lengyue_LayeredDress_v004.blend`
- FBX：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v004/exports/Lengyue_LayeredDress_v004.fbx`
- Unity Prefab：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/V003/Prefabs/Lengyue_GameCharacter_V003.prefab`
- Unity 构建器：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/V003/Editor/LengyueLayeredDressBuilder.cs`
- PlayMode 探针：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/V003/Validation/LengyueLayeredDressProbe.cs`
- 验证与视觉证据：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v004/reports/`
  和 `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v004/evidence/`

## Phase Wardrobe V004.1 技术交接：收身版型与连续裙骨权重

### 1. 修正目标

本次针对两个外观问题修正既有 V004，不创建破坏性分支：

- 裙身腰部以下存在额外 `36 mm` 中段 ease，导致轮廓呈气球状。
- 吊带裙上身在腋下叠加侧向余量，侧片没有贴合躯干。

`build_layered_dress.py` 现在将裙身改为贴合腰臀后自然展开的 A 形，并将腋下
设计间隙控制为约 `5.24 mm`。腰围、裙长、双层结构和蝴蝶结位置不变。

### 2. 裙身轮廓

新版 `skirt_profile(t)` 统一生成横向与前后半径：

- 中段侧向比上一版收紧 `39.37 mm`。
- 中段前后比上一版收紧 `35.51 mm`。
- 下摆侧向收紧 `17 mm`，前后收紧 `10 mm`。
- 腰口半径保持不变，避免腰线、蝴蝶结和上身接缝错位。

这些差值写入 `reports/model_build.json` 的 `fitRevision`，后续调整不能只依赖
截图判断。

### 3. 连续裙骨权重

收紧静态网格后，原共享前裙骨会在抬起一条腿时推动整片前摆，并与另一条腿相交。
不能通过整体增强 `SkirtPoseGuard` 旋转解决，否则会在 Walk 中制造新的交叉。

最终 `layer_weights(a, t)` 使用以下连续场：

1. 裙摆正中继续由共享 `Skirt_Front_01/02` 控制，保持中线平滑。
2. 从正中向左右前侧逐渐混入对应的 `Skirt_FrontSide_01/02_L/R`。
3. 到约 57–61° 的腿侧区域时完全交给对应 FrontSide 链。
4. 上下裙层在对应环使用同一权重函数，避免层间独立扭转。
5. 每个顶点仍截断为最多四个影响并重新归一。

该方案同时满足静态收身和动态单腿释放，56 个 Unity BakeMesh 姿势的验收面
交叉重新归零。

### 4. 腋下收身

`bib_surface(a, t)` 将余量拆为基础余量、顶部余量和腋下定向内收三部分：

- 基础余量：`5.5 mm`
- 顶部附加余量：最多 `3.5 mm`
- 腋下定向内收：最多 `2.5 mm`

精确侧向顶部由身体截面计算出的净间隙为 `5.239 mm`。该参数保留足够蒙皮空间，
同时消除原先侧片向外凸出的轮廓。

### 5. 回归结果

- Blender/FBX：56,912 顶点、113,128 三角面，116 骨与 Rest Pose 不变。
- Unity：重绑定最大误差 `7.2151e-7 m`。
- 56 姿势：上下裙摆—腿、上下裙层、外露袖口—手臂交叉全部为 0。
- PlayMode：Walk/Run 各 90 帧，`meshRenderingErrors=0`。
- 视频：960 × 960、30 FPS、90 帧、3 秒，全帧解码通过。

核心实现仍位于：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v004/scripts/build_layered_dress.py`

## Phase Wardrobe V004.2 技术交接：袖口独立粉紫 Atlas

### 1. 颜色设计

荷叶边不再与 T 恤主体共用深紫色 Atlas 第 1 区，而是使用第 7 区。第 7 区以第 1 区
的织物纹理为基底，混入粉紫目标色：

- 袖根粉紫混合比例：`10%`
- 外缘粉紫混合比例：`32%`
- 插值曲线：`10% + 22% * v^1.35`
- 袖口压线继续使用第 1 区深紫色

因此花边整体仍归属于深紫色系，只在外缘形成更暖、更亮的粉紫色差。

### 2. 资源隔离

`build_layered_dress.py` 从 V002 Atlas 只读复制像素，生成：

`/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/V003/Runtime/Textures/LavenderCasual_Atlas_v004.png`

导出副本位于：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v004/exports/LavenderCasual_Atlas.png`

两份 V004 纹理 SHA-256 相同；V002
`Runtime/Textures/LavenderCasual_Atlas.png` 保持原哈希，不允许回写。

### 3. Unity 绑定门禁

`LengyueLayeredDressBuilder` 在创建 V003 Toon 材质时显式加载 V004 Atlas，并将其
赋给 `_BaseMap`。`unity_build.json` 的
`sleeveRuffleAccentTextureAssigned` 必须为 `true`。

`LengyueLayeredDressProbe` 在 PlayMode 中进一步检查运行时材质纹理名称必须为
`LavenderCasual_Atlas_v004`，结果写入
`sleeveRuffleAccentTextureVerified`。这防止 Blender 图片已更新但 Unity 材质仍
引用 V002 旧 Atlas 的假通过。

### 4. 回归结果

- V004 Atlas 构建与 Unity 绑定通过，V002 Atlas 未修改。
- 56 姿势几何验收保持通过。
- Walk/Run 各 90 帧，`meshRenderingErrors=0`。
- H.264/yuv420p 视频均为 960 × 960、30 FPS、3 秒，全帧解码通过。

## Phase Wardrobe V004.3 技术交接：粉紫对比增强

V004.2 的 `10%` 至 `32%` 是首轮轻量粉紫方案。根据后续视觉反馈，当前正式参数
已调整为：

- 粉紫目标色：线性 RGB `(0.74, 0.36, 0.60)`
- 袖根混合比例：`16%`
- 外缘混合比例：`42%`
- 插值曲线：`16% + 26% * v^1.35`

外缘仍以 `58%` 的原深紫纹理为基础，因此只增强荷叶边与袖身的色差，不改变整体
深紫主色关系。最终参数记录在 `reports/model_build.json` 的
`sleeveRuffleColor`。

## Phase Wardrobe V004.4 技术交接：最终粉紫参数

V004.3 参数已被后续视觉调整取代。当前正式袖口参数为：

- 粉紫目标色：线性 RGB `(0.80, 0.34, 0.61)`
- 袖根混合比例：`20%`
- 外缘混合比例：`48%`
- 插值曲线：`20% + 28% * v^1.35`
- 外缘原深紫保留比例：`52%`

以上参数是当前交付基线；构建报告、V004 独立 Atlas 和 Unity 运行时纹理门禁均已
同步更新。

## Phase Wardrobe V004.5 技术交接：综合色差门禁

V004.4 的轻量增幅在最终 PNG 中仍不够明显，已被当前参数取代：

- 粉紫目标色：线性 RGB `(0.88, 0.40, 0.70)`
- 袖根混合比例：`38%`
- 外缘混合比例：`68%`
- 插值曲线：`38% + 30% * v^1.35`
- 深紫分区平均线性 RGB：约 `(0.464, 0.344, 0.574)`
- 粉紫分区平均线性 RGB：约 `(0.675, 0.373, 0.637)`
- 最终综合色距：`0.2224`

`build_layered_dress.py` 现在断言红色通道均值差大于 `0.16` 且综合色距大于
`0.18`。该门禁用于避免“数值改变但 Toon 成品肉眼无差异”的回归。深紫仍由面积
更大的短袖主体和袖口压线承担，粉紫仅用于荷叶边及少量协调细节。

## Phase Wardrobe V004.6 技术交接：多姿态穿模验证

### 1. 验证矩阵

`LengyueLayeredDressBuilder.CaptureAndExport` 现在输出 72 个 Unity 实际蒙皮快照：

- Idle 1、Reference 1、Walk 33、Run 21。
- 16 个确定性压力姿态，覆盖上肢四向运动、屈肘、单腿高抬、弓步、侧向外展、
  交叉步和双腿屈曲组合。

压力姿态使用父骨到子骨的当前世界方向与目标世界方向计算旋转，不再依赖固定
Euler 轴符号。每次构建前会删除旧 `reports/geometry/`，防止过期快照混入统计。

### 2. 相交指标

`validate_unity_geometry.py` 使用 Blender `BVHTree.overlap()` 检查：

- 上/下层裙摆与腿部。
- 上下裙层之间。
- 外露袖口与手臂。
- 衣身、上下裙摆与外露手臂。

短袖覆盖范围内的上臂皮肤不计入外露指标。当前以 Rest Pose
`|X| > 0.26 m` 的皮肤三角面定义外露手臂；完整上臂交叉仍作为信息指标保留。

### 3. 当前结论

- 72 姿态中 63 通过、9 失败；总门禁保持 `passed=false`。
- Walk 33 帧和 Run 21 帧全部通过。
- 全部 72 姿态的上下裙层相交数均为 0。
- 失败姿态见 `reports/multi_pose_summary.json` 和
  `evidence/penetration_findings.png`。
- PlayMode Walk/Run 各 90 帧通过，运行时 Mesh 渲染错误为 0。

### 4. 修复边界

正常 Idle 手扶腰属于直接影响现有动画的高优先级问题。高抬腿和弓步可通过扩展
`SkirtPoseGuard` 的腿部方向响应修复。侧劈、宽距深蹲属于大幅动作，静态扩裙会
破坏已确认的贴身轮廓，优先采用运行时裙骨碰撞或动作幅度约束。

## Phase Wardrobe V004.7 技术交接：肩袖连续蒙皮

### 1. 根因

V004.6 之前的短袖权重在 `|X|=0.130–0.210 m` 内直接从躯干混合到
`UpperArm`。手臂上举时，内侧顶点保持在 `Chest`，外侧顶点跟随上臂，连续网格
被拉成翼状折片。该问题不会产生三角面穿模，因此原几何门禁无法发现。

### 2. 权重迁移

`build_layered_dress.py` 现在执行以下流程：

1. 读取未修改身体网格 `Casual_Skin_arms` 的肩臂蒙皮权重。
2. 为短袖顶点查询同侧最近 8 个身体顶点并进行距离加权插值。
3. 在 Rest Pose `|X|=0.090–0.125 m` 内从躯干权重平滑过渡到身体肩臂权重。
4. 保留权重最高的 4 根骨并重新归一化。

最终使用 `Chest`、`Spine`、`Shoulder_L/R`、`UpperArm_L/R` 和
`UpperArmTwist_L/R`，共迁移 4452 个顶点。最大最近源点距离约 `0.0731 m`，
最终权重误差约 `4.47e-8`。

### 3. 形变门禁

短袖主体在第二套 UV 中使用 `ValidationPart=5`，与荷叶袖口分离统计。
`validate_unity_geometry.py` 对双臂上举姿态计算：

- 肩袖三角形面积变化。
- 肩袖边长变化。
- 肩袖顶点到手臂表面的距离。

结果写入 `reports/shoulder_deformation.json`。当前 `P95` 面积与边长拉伸分别为
`2.154×` 和 `1.962×`，较基线下降 `48.5%` 与 `57.5%`，所有硬门禁通过。

### 4. 避坑

- 权重顶点组不能通过持有可变 `VertexGroupElement` 引用边遍历边删除；应先复制
  group index 整数，否则旧 `Chest` 权重可能残留并导致总权重大于 1。
- 不能简单强推全部肩根权重到 `UpperArm`；这会降低拉伸数值但在侧视图形成腋下
  裂口。当前方案使用身体肩骨权重作为中间层。

## Phase Wardrobe V004.8 技术交接：吊带纽扣尺寸

`build_layered_dress.py` 使用 `strap_button_scale=1.80` 等比缩放两枚布包纽扣的
环形截面、厚度和缝线。最终最大半径为 `0.01134 m`，直径为 `0.02268 m`，
约为 `0.0136 m` 吊带宽度的 `1.67` 倍。
纽扣中心与吊带几何保持不变，尺寸数据写入 `model_build.json.strapButtons`，
便于后续调整和自动校验。

## Phase Wardrobe V004.9 技术交接：统一生产资产

### 1. 唯一入口

业务场景只能引用：

`/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Prefabs/Lengyue_GameCharacter.prefab`

该 Prefab GUID 固定为 `ef0be3f2d0f904d0789c8483c7dd5e6c`。`Runtime` 下统一
管理 `Animations`、`Materials`、`Meshes`、`Models`、`Prefabs`、`Scripts`、
`Shaders` 和 `Textures`。禁止重新引入 V001、V002 或 V003 运行时路径。

### 2. 构建流程

`LengyueLayeredDressBuilder.Build()` 的流程为：

1. 读取统一 Prefab 并完全解包到隔离工作场景。
2. 从 `Runtime/Models/Lengyue_LayeredDress.fbx` 重建标准顶点流和 bindposes。
3. 替换名为 `LavenderCasual_Garments` 的现有服装 Renderer。
4. 保持身体、脸、头发、骨骼层级、Rest Pose、动画和运行时物理组件。
5. 覆盖保存同一个 Prefab，不创建新版本入口。
6. 验证 Prefab 数量为 1，`Lengyue/V0*` 与 `Assets/SapphiArt` 依赖均为 0。

构建器支持有已保存场景和未保存空场景两种编辑器启动状态。空场景状态使用
`NewSceneMode.Single`，完成后恢复干净空场景，避免 Unity 抛出
`Cannot create a new scene additively with an untitled scene unsaved`。

### 3. 归档与更新约定

- 历史生成资产位于
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/legacy_unity_assets/`，
  仅用于追溯，不应移动回 Unity `Assets`。
- Blender 构建脚本直接覆盖统一路径下的 FBX 和衣服 Atlas；对应 `.meta` 保持不变。
- `MateSoul/Lengyue/Build Production Character` 是后续服装更新的唯一 Unity 构建命令。
- `LengyueLayeredDressProbe` 设置 `Application.runInBackground=true`，保证后台
  Walk/Run 录帧不会因编辑器失焦停滞。

### 4. 验证边界

资产合并只改变目录和生产入口，不改变多姿态验收结论。当前 72 姿态仍为 63
通过、9 个已知极端姿态失败；Walk/Run、肩袖上举、头颈连续性和实时渲染门禁
保持原结果。

## Phase Wardrobe V004.10 技术交接：腰间丝带宽度

`build_layered_dress.py` 使用 `waist_ribbon_row_spacing=0.0043` 生成五排腰带环，
外加 `0.002 m` 截面直径，最终可见高度为 `0.0192 m`，相对原 `0.016 m`
提高 `20%`。各排围绕 `Z=1.098 m` 对称分布，确保腰线和原蝴蝶结位置不漂移。
尺寸参数写入 `model_build.json.waistRibbon`；原蝴蝶结几何 SHA-256 继续作为
独立保留门禁。

## Phase Appearance HairLip 技术交接：各向异性卡通头发与下唇贴图

### 1. 运行时材质

修改文件位于
`/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/`：

- `Shaders/MateSoulCharacterToon.shader`：`_HairMode` 默认关闭；
  `EvaluateHairLight` 使用三档主光色阶和单条各向异性高光。
- `Materials/Lengyue_Toon_Hair.mat`：仅此材质设置 `_HairMode=1`。
- `Textures/Lengyue_Face_BaseColor.tga`：在原贴图下唇区域加入淡粉色。

顶点阶段传递蒙皮切线和 handedness；片元阶段把切线投影到法线切平面，
通过 `cross(N,T) * tangent.w` 得到 UV V 发束方向。高光由半角向量在发束方向
和横向上的椭圆瓣计算，再经 smoothstep 压成简洁条带。`fwidth` 保持边缘抗锯齿，
无噪声纹理、环境反射、Fresnel 边缘增强或描边 Pass。

当前粗糙度 `0.10`、各向异性 `0.72`、高光强度 `0.16`、偏移 `0.08`；
三档色阶阈值为 `0.28/0.62`，边界柔度 `0.018`。这些是本 Toon 模型的形状
参数，不是标准 PBR 能量模型。头发模式目前使用主光和低强度 SH 填充，不叠加
额外灯光；其他材质仍走原有着色路径。

### 2. 下唇定位与 Blender 同步

源与脚本：
`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/HairLip/`。

`scripts/tint_lower_lip.py` 从 `source/before/Lengyue_Face_BaseColor.tga`
幂等重建；Unity UV 中上唇中心约为 `(751,815)`，下唇边界约为 `(751,840)`，
不能用覆盖整个嘴部的简单圆形蒙版。当前沿下唇弧线插值，嘴角淡出，粉色
sRGB `(231,148,172)`、最大混合 `0.30`。结果只改 679 像素，
边界 `(713,830)–(788,850)`，最大单通道变化 24/255。

`scripts/sync_blender_lip.py` 将同一贴图打包到现有 V004 Blender 源文件。
`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v004/scripts/build_layered_dress.py`
保存源文件前调用该同步方法，避免重建衣服时唇色退回旧版。Blender Principled
不复刻 Unity 的精确条带 Shader，源场景 metadata 和交付 README 已说明。

### 3. 验证流程与避坑

1. `Editor/LengyueAppearanceReview.cs` 在 PreviewScene 实例化统一 Prefab，
   生成固定相机、固定灯光近景及微笑/张嘴截图，不修改用户工作场景。
2. 自动化请求文件为仓库 `.dbg/appearance-capture`，内容 `after` 截图，
   `play` 在临时验证场景从统一 Prefab 开始实时录制。
3. `LengyueLayeredDressProbe.appearanceCloseup` 只改变验证相机；
   新增 `strandHairVerified` 检查头发模式、切线数及其他材质未启用头发模式。
4. `scripts/package_review.py` 检查资产变化边界、截图局部差异、Animator 状态、
   视频帧数与全帧解码，生成 `reports/appearance_validation.json`。

外部修改素材后必须同步 `AssetDatabase.Refresh`，且确认实际材质 `_HairMode`
生效；仅收到截图成功日志不能证明加载了新资源。当前工具已加入显式导入和
材质门禁。Unity BlendShape 名带前缀，如 `Face_Blendshape.face_smile_BS`；
不要直接沿用 Blender 的短名称。

### 4. 交付与边界

最新证据：
`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/HairLip/evidence/`。
原始材质、贴图与 Blender 备份位于同目录树的 `source/before/`。
Unity Shader 编译及 Walk/Run 各 90 帧验证通过，68 个头发物理关节、540 子步，
渲染停止错误 0。资产校验确认全部 Mesh、Prefab、骨架、权重、动画和衣服未变。
本次未重跑服装 72 姿态门禁，原有包括 Idle 在内的 9 项失败继续保留。

## Phase Appearance HairEye 技术交接：发丝浅暗纹与虹膜提亮

### 1. 生成与生产入口

脚本目录：
`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/HairEye/scripts/`。
`refine_textures.py` 从本轮 `source/before/` 幂等生成两张生产贴图，覆盖统一
Runtime 下的 `Lengyue_Hair_Albedo.png` 和 `Lengyue_Eye_Albedo.png`，保留 `.meta`。

头发依据原 4×4 UV 图集，每 tile 留 2.5% 边距；UV V 为根梢方向。每束添加
三条带微小弧度的暗纹及邻近的浅纹，使用高斯截面和根梢 smoothstep 淡出。
主暗纹最大乘性降低 18%，实际最大单通道变化 17/255。没有随机噪声或新 Shader
采样，仍通过原贴图 mipmap 过滤。

虹膜在下半部使用固定 Hue 的 HSV 变换，Value 向白提升系数 `0.15`，
Saturation 最多降低 `5.5%`。径向遮罩保留瞳孔核心与外圈；主/次高光按原
中心和纵横比分别扩张 `1.30/1.40`，通过 Linear RGB 只补新增亮斑区域。
扩张在外圈前平滑淡出，避免硬裁切造成明显边缘。

### 2. 源文件和验证

`sync_blender_textures.py` 将两张贴图内嵌到现有 V004 Blender，目标对象为
`Lengyue_Bob_Hair_v002` 与 `eyes`；不处理保留的旧版隐藏头发对象。
服装构建脚本保存源文件前，依次同步下唇、头发与眼睛。几何签名方法复用
HairLip 的 `geometry_signature`，确认顶点、面、蒙皮、骨架 Rest Pose 和
BlendShape 不变。本轮完整 Blender 备份在 HairEye/source/before。

`LengyueAppearanceReview` 当前输出指向 HairEye，菜单改为
`MateSoul/Lengyue/Capture Current Appearance`。`.dbg/appearance-capture`
协议不变。Before 复用并核对了上一轮已交付的真实 Unity 截图。

`package_review.py` 检查 Runtime 只有两张贴图发生变化，检查眼部和发丝确实
有像素变化，中央脸部/下唇与服装像素不变；验证虹膜下部色相、PlayMode、
视频帧数和全帧解码。脸部回归 ROI 使用中央区域，避免把下巴两侧的后层头发
误算为皮肤变化。

结果：下部虹膜平均亮度 `80.78 → 91.62/255`，中央下部采样区最大 Hue 差
`0.52°`；Walk/Run 各 90 帧通过，头发 68 关节/540 子步，渲染停止错误 0。
精确 Toon 高光仍以 Unity 为准。旧服装穿模结论未在本轮重测或改变。

## Phase Appearance NaturalHighlights 技术交接：哑光光带与外侧眼高光

### 1. 当前资源和光照

最新工作目录：
`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/NaturalHighlights/`。
生产目录：
`/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/`。

- `Shaders/MateSoulCharacterToon.shader`：保留仅头发的三档柔和色阶，将原硬阈值
  镜面带改成 `_HairHighlightMap` RGB 三层蒙版。每层在原 UV 上随蒙皮变形，
  宽缓各向异性响应只调节强弱，不重新切出一条月牙。
- `Textures/Lengyue_Hair_Highlights.png`：1024²、4×4 tile，每 tile 95% 有效区域；
  RGB 各存一条有差异的柔边光带，必须线性导入并启用 mipmap，保留 RGB swizzle。
- `Materials/Lengyue_Toon_Hair.mat`：粗糙度形状参数 0.32、各向异性 0.58、
  高光强度 0.18、漫反射包裹 0.32、阴影柔度 0.08。这里的粗糙度不等同于 PBR。
  没有额外法线贴图，减弱凹凸感通过降低材质明暗反差实现，未修改网格法线。
- `Textures/Lengyue_Hair_Albedo.png`：逐 tile、逐行将横向偏差保留 65%，微量
  提亮基础颜色，保留纵向细纹；未加入噪声或轮廓描边。

### 2. 眼睛与鼻部生成

`scripts/build_textures.py` 从本轮 `source/before` 重建，重复执行不叠色。
双眼共享镜像 UV，U 大于 0.5 均为远离鼻梁的外侧；主光斑归一化中心由
`(-0.34,0.38)` 改为 `(0.40,0.38)`，次光斑改为 `(0.56,0.10)`。

仅在新旧光斑的联合范围重建虹膜。EyeLab/v001 导出的历史 PNG 实际像素为
`round(sRGBToLinear(解析颜色)×255)`，不能直接使用解析 sRGB 数值替换，
否则局部底色会变亮。局部恢复相同历史编码后，再保留原下部 HSV 提亮并绘制
新光斑。瞳孔核心、外圈与归一化 y≤-0.22 下部虹膜保持字节一致。

用亮度大于 150/255 的亮区面积核对实际缩小效果，避免仅减小公式参数却因混合
空间不同而使视觉亮斑变大。最终 `1932 → 1734 px`，等效直径比 0.94737。

鼻部在 2048² 面部贴图中修改 `(742,732)–(763,776)` 区域，短鼻侧影配柔和
鼻尖底影，颜色 `(153,104,110)`，最大混合 0.1149，只改 549 像素。
原下唇 y830 附近不受影响。

### 3. 源文件同步与验证

1. 执行 `scripts/build_textures.py` 生成三张原贴图和新 RGB 光带图集。
2. V004 Blender 执行 `scripts/sync_blender_textures.py`，使用新加载图像并打包，
   同步头发、光带、眼睛和脸部；Principled 预览粗糙度 0.68，只近似 Unity 光照。
3. `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v004/scripts/build_layered_dress.py`
   保存前调用当前 `apply_current_appearance()`。旧 HairEye 同步函数假设头发
   只有一个图片节点，不应在新增光带节点后继续使用。
4. `LengyueAppearanceReview` 输出指向 NaturalHighlights。`VerifyRibbons` 通过
   GPU 回读验证三通道仍独立、使用线性导入和 mipmap；静态图还检查 Shader 错误。
5. `.dbg/appearance-capture` 内容为 `after` 或 `play`。Editor 脚本变更后先等
   编译/domain reload，再投递请求。旧 delegate 可能在刷新前消费请求，
   因此应核对输出目录和报告时间，不能只看成功日志。
6. PlayMode 完成后 `scripts/package_review.py` 检查资产边界、下唇/脸颊/衣服像素、
   Blender 图像 SHA、光斑缩小、暗部提亮以及两段视频的帧数、时长、全帧解码。

报告均在当前目录 `reports/`。Unity 静态和实际 Walk/Run 验证通过，
68 个头发物理关节、540 子步、渲染停止错误 0。几何、骨架、权重及 BlendShape
签名保持 `4bb25c6851357755c6e57e7713250a2f9b7361459ad023ccd2e338dbbfa5f80c`。
原服装 9 个穿模失败未在本轮复测或修复；精确材质验收以 Unity 渲染为准。

## Phase Appearance ReferenceMatch 技术交接：参考材质与版本化回滚

工作目录：
`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/ReferenceMatch/`。
当前生产头发为 DeepPurple r6，眼鼻保留本轮修正；HairEye、r3 与错误取色 r5
均为独立回滚点。全部验证边界见 `reports/current_status.json`。

1. `scripts/build_face.py` 从封存贴图重建，内侧高光中心 `(-0.34,0.38)`，
   主/次尺寸参数分别 `0.936/0.99`。历史线性字节编码延用 NaturalHighlights 的
   解析函数，避免更改虹膜底色；鼻尖影基于无鼻侧影的 HairEye 底图，仅 87 像素。
2. `scripts/build_hair.py` 生成指定基色与连续主亮部蒙版，保存每轮实际纹理和
   参数。r6 基色 `(54,23,96)` 取自 HairEye 原图集均值
   `(54.27,23.39,95.63)`；强度 `0.36`、半宽 `0.025m`、中心 `1.674m`，
   高光色 `(0.40,0.39,0.43)`，弱上层过渡强度 `0.18`。
   蒙版坐标是静止状态角度/高度，不是旧 4×4 tile 的三条重复光带。
   参考图仅约束高光结构、粗糙度与层次，禁止用其颜色覆盖角色原始配色。
3. `scripts/prepare_mesh.py` 从不可变 HairEye 网格生成 normals/tangents/UV2，
   19520 顶点、25824 三角形；字节验证位置、索引、UV0、蒙皮与其余序列化字段。
   Z 向上、-Y 朝前，UV2 为 `0.5+atan2(x,-(y-.006))/(2π), (z-1.4)/.35`。
   `LengyueReferenceHair.Prepare()` 提供 Unity 内同公式入口；该入口及生成资产
   已编译，静态截图和带 UV2 门禁的 PlayMode 均通过。
4. `scripts/sync_blender_textures.py` 同步当前 Runtime 贴图并保持几何签名。
   检查实际 Shader 是否含 `input.hairUV`：候选使用平滑法线/ReferenceLightUV；
   HairEye 则只从最佳版加载 `Review_Volume_v003_Mesh` 网格数据，保留当前眼鼻。
   不可用 objects 导入恢复头发，否则会带入重复父骨架。服装构建器已接入此函数。
5. `scripts/restore_appearance.py preferred` 完整恢复 HairEye；
   `preferred --hair-only` 只恢复头发；`candidate` 恢复深紫 r6；
   `candidate-r5` 恢复错误取色但保留的灰蓝紫 r5；`candidate-r3` 恢复 r3。
   每次先验证快照 SHA，源同步失败恢复此前资产。Blender 必须传
   `--python-exit-code 1`，否则 Python 异常可能仍以退出码 0 返回。候选参数
   随快照切换，避免 Runtime 与 Blender 预览串版。
6. `scripts/render_preview.py` 只用于 r1/r2 的 Blender 方向研究。最终依据
   r3–r6 的同条件 Unity 对比，见 `evidence/unity_iteration_comparison.png`；
   完整正脸色彩修正见 `evidence/color_correction_full_face.png`。
7. `LengyueAppearanceReview` 已改为输出 ReferenceMatch，兼容最佳版和候选。
   候选恢复后等 Unity 编译完成，再写 `.dbg/appearance-capture` 的标签或 `play`。
   不要在旧 delegate 仍注册时投递请求，否则可能误写历史目录。

原 Unity 进程一度仅写云项目心跳且不自动导入；通过可见编辑器执行
`Assets > Refresh` 后恢复。隔离克隆的 bee_backend 134
（`mono_os_sem_post: semaphore_signal failed with error 15`）未用于最终结论。
r6 在原项目完成 10 视角、换光、表情与 Walk/Run 验证。

HairEye/r3/r5/r6 完整切换与失败恢复均已验证。生产 Runtime 相对 HairEye
只改 Shader、头发 Material、发网格着色属性及四张外观贴图；Prefab GUID 不变。
Walk/Run 各 90 帧，头发 68 关节/540 子步，渲染停止错误 0。Blender 几何签名为
`4bb25c6851357755c6e57e7713250a2f9b7361459ad023ccd2e338dbbfa5f80c`。

## Phase Appearance LayeredBands r7d 技术交接：多尺度发束着色

当前生产版为 LayeredBands r7d。工作目录与 Runtime 路径不变；r6 降级为单带
历史快照，恢复入口为 `candidate-r6`。

### 1. 三尺度材质结构

1. `scripts/build_hair.py` 从不可变 HairEye 底图生成深紫 Albedo。基色仍为
   `(54,23,96)`；保留原始大尺度明暗，并将每行半径 8 像素的局部高频差值按
   `0.65` 追加，强化束内纵纹而不改变整体配色。
2. 高光图 RGB 通道定义为：R=主横带；G=上带、短强调带、下带；B=五组纵向亮脊。
   四条横带半宽分别为 `6.5/2.0/3.0/4.2 mm`，边沿过渡 `1.1 mm`。中心、宽度和
   强度分别使用不同角频率及局部片段包络，不生成等距平行光环。
3. `MateSoulCharacterToon.shader` 对 R/G 取最大值形成横向层次，再与 B 取最大值。
   横纵交叉不相加，因此不会产生 r7a 的网格亮块。常规 Specular 为 0；哑光由
   粗糙各向异性响应实现，而不是模糊高光图边缘。

### 2. 网格着色属性

`scripts/prepare_mesh.py` 始终从 HairEye 网格重建。顶点法线为
`0.58 × 原始发束法线 + 0.42 × 胶囊法线` 后归一化；恢复大束方向差的同时避免
HairEye 的过硬切面。UV2 继续保存静止角度/高度，并按 UV0 的 4×4 tile 施加最大
`3.2 mm` 的预定义高度错位，使横带跟随不同大束上下错开。

位置、索引、UV0、蒙皮、BindPose、BlendShape 与其他序列化字段执行字节级保护。
报告位于
`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/ReferenceMatch/reports/hair_mesh.json`。

### 3. Blender 与版本回滚

`scripts/sync_blender_textures.py` 从 HairEye Blender 快照读取基线发束法线，
同步相同的法线混合、UV2 tile 错位和 RGB 通道 `max` 组合。旧 r3–r6 参数没有
`normal_detail` 或 `band_stagger` 时默认 0，保证历史版本仅头发恢复仍可执行。

回滚命令：

```bash
cd /Users/bytedance/MateSoul
python3 ArtSource/SapphiArt/Appearance/ReferenceMatch/scripts/restore_appearance.py candidate
python3 ArtSource/SapphiArt/Appearance/ReferenceMatch/scripts/restore_appearance.py candidate-r6
```

第一条恢复当前 r7d，第二条恢复 r6。两条路径及 `--hair-only` 均已实际往返验证；
完整快照位于 `rollback/selected_LayeredBands_r7d/`。

### 4. 验证边界

- `reports/r7d_material_metrics.json`：前部角度列为 3–4 个横带，纵向纹理高频 RMS
  `4.000`；不存在宽高斯羽化。
- `reports/r7d_balanced_layers_capture.json`：Unity 10 视角、Shader 错误 0。
- `reports/playmode.json`：Walk/Run 各 90 帧，渲染错误 0。
- `evidence/material_structure_full_face.png`：r6 单带与 r7d 多层完整正脸对比。
- 本阶段不修改服装网格、动画、Prefab GUID、眼鼻贴图或既有穿模结论。

## Phase Appearance AnimeStrands r8d 技术交接：UV0 逐发束动漫高光

当前生产版为 AnimeStrands r8d。r7d 的横向高光设计已废弃但快照保留，恢复入口
为 `candidate-r7d`。

### 1. UV0 高光布局

`scripts/build_hair.py` 不再使用头部角度/高度绘制高光。1024² 高光图按 UV0
拆成 4×4、每格 256² 的发束单元：

1. R 通道：每束独立的纵向主块，顶部为宽肩，下端按三次平滑函数收为尖角。
2. G 通道：位于主块内部的窄亮芯，长度和宽度均更小。
3. B 通道：每束 1–2 个菱形碎光，使用独立冷紫色叠加。

16 组 `center/width/start/end/bend/strength/chips` 参数明确记录在
`iterations/r8d_selected_anime_blocks/parameters.json`。边缘仅保留
`0.006 UV` 的抗锯齿范围，不做高斯羽化。Shader 从 `input.uv` 采样，不再从
`input.hairUV` 生成跨发束光环。

### 2. Shader 与结构阴影

`MateSoulCharacterToon.shader` 对 R 主块乘 `0.72` 后与 G 亮芯取最大值；B 碎光
使用偏亮冷紫色按 `0.62` 独立叠加。各向异性只调节块面强度，不改变轮廓。

材质关键值：

- `_ShadowColor=(0.30,0.22,0.43)`
- `_ReceiveShadows=0.72`
- `_ShadowSoftness=0.022`
- `_DiffuseWrap=0.10`
- `_SpecularIntensity=0`

`scripts/prepare_mesh.py` 使用 76% HairEye 原发束法线与 24% 胶囊法线，强化
大束间明暗切面。UV2仍保留以兼容旧快照，但 r8d 高光不读取 UV2。

### 3. 眼睛与源文件

`scripts/build_face.py` 直接复制 HairEye 眼睛贴图，不再解析重绘光斑。Runtime
眼睛 SHA 与 HairEye 快照一致，亮区为 1932 px，对应一大一小原始光斑。
面部底图仍从 HairEye 重建 87 像素鼻尖影，因此眼睛恢复不会移除鼻影。

`scripts/sync_blender_textures.py` 检测
`highlight_layout=uv0_per_strand_pointed_blocks` 后使用基础 UV 层采样蒙版，并
同步主块/亮芯/碎光组合及 76% 法线混合。旧 r3–r7d 分支继续兼容。

### 4. 回滚与验证

```bash
cd /Users/bytedance/MateSoul
python3 ArtSource/SapphiArt/Appearance/ReferenceMatch/scripts/restore_appearance.py candidate
python3 ArtSource/SapphiArt/Appearance/ReferenceMatch/scripts/restore_appearance.py candidate-r7d
```

第一条恢复 r8d，第二条恢复 r7d。两条路径和 `--hair-only` 已实际往返验证。

- `reports/r8d_material_metrics.json`：头部横带 0，16 个发束单元，眼睛精确匹配。
- `reports/r8d_selected_anime_blocks_capture.json`：Unity 10 视角、Shader 错误 0。
- `reports/playmode.json`：Walk/Run 各 90 帧、渲染错误 0。
- `evidence/highlight_logic_full_face.png`：r7d/r8d 完整正脸逻辑对比。
- 几何、骨架、权重、BlendShape、动画、Prefab GUID 与服装穿模结论不变。

## Phase Animation Yummy 技术交接：Unity 原生曲线映射与运行时物理渲染

### 1. 阶段概述

动画工作区为
`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/`。所有外部 FBX
先复制到 `inputs/` 并设为只读。最终动作与渲染基准是 Unity 2022.3.38f1，
不使用 Blender 预览判断动作是否正确。

骨骼动作范围为 2–174 帧、30 FPS。源 FBX 把身体、头发与控制 Rig 合并为一个
400 骨 Armature；Unity 原生导入为 `Scene` Clip，长度 5.7333 秒。最终
`Lengyue_Yummy_BodyOnly.anim` 使用 Unity 曲线直接求值：旋转保留动画师绝对
Local Rotation，位置和缩放只迁移动画增量到生产 Prefab Rest Pose。

### 2. 构建流程

1. `LengyueYummyUnityReview.ConfigureImporter()` 将 FBX 按 Generic、无 Avatar、
   无压缩、30 FPS 原生导入。
2. `BuildBodyClip()` 使用骨骼末级名称查找源/目标唯一 Transform，逐帧直接
   `Evaluate` Unity 导入曲线。`Head` 映射到生产节点 `Head 1`。
3. 输出 67 根身体骨、670 条曲线。`H_*`、`Hair_*`、`Backhair_*`、`Skirt_*`、
   `OuterSkirt_*`、IK/CTRL/DRV/MCH 与 `Eye_L/Eye_R` 曲线全部剔除。
4. 旋转直接使用源绝对值；位置为
   `targetRestPosition + sourcePosition - sourceDefaultPosition`；缩放按源默认值
   与目标 Rest Scale 的比例映射。不能对旋转做同样的 Rest-delta，因为动画专用
   FBX 的默认旋转就是动作首帧，减去后会得到 T Pose。
5. 构建 macOS Standalone Player，以真实 Player Loop 执行
   `SampleAnimation -> HairSpringPhysics/SkirtPoseGuard LateUpdate ->
   Camera.Render`。输出 PNG 后由 FFmpeg 编码并检查全帧。

主要文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Yummy/Yummy_Action_InPlace.fbx`
- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Yummy/Lengyue_Yummy_BodyOnly.anim`
- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Editor/LengyueYummyUnityReview.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Validation/LengyueYummyCaptureProbe.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Validation/Lengyue_Yummy_Capture.unity`

### 3. 渲染与物理证据

`reports/unity_clip_build.json` 必须满足：

- `mappingMode = UnityAbsoluteRotation_TargetRestTranslation`
- `mappedBones = 67`
- `mappedBindings = 670`
- `missingBones = []`
- `hairCurvesRemoved = true`
- `skirtCurvesRemoved = true`

`reports/unity_playmode.json` 当前结果：

- 10 个 Renderer 全部使用 `MateSoul/Character Toon`。
- Hair Balanced：68 个模拟关节、510 个 90 Hz 子步、最大弯曲 18°。
- 发梢相对头部运动范围 0.0817 m。
- 裙摆最大骨骼偏转 22.84°；左右腿屈曲 29.19°/26.58°。
- 左右手世界运动范围 0.4702 m/0.5813 m。
- 头颈距离比例最大偏差 `0.000002861`。
- `SkinnedMeshRenderer: Rendering stopped` 错误为 0。
- 视频 960×960、30 FPS、173 帧、5.7667 秒。

锁屏下 Unity Editor 的 Player Loop 不推进，Edit Mode GPU 蒙皮也会保留缓存姿态，
因此最终验证必须构建并运行 macOS Standalone Player，不能用 Edit Mode 静态画面
判断动作。
受限 shell 直接启动 Unity 会使 `bee_backend` 因 macOS semaphore 返回 134；
通过 LaunchServices 启动隔离 Unity 可正常编译。隔离 Library 验证后必须清理。

动画师参考视频为 223 帧/7.433 秒，包含 30 帧开场停留。核心动作按
`referenceFrame = unityFrame + 30` 对齐，证据见
`ArtSource/SapphiArt/Animation/Yummy/evidence/Yummy_Reference_vs_Unity_aligned.png`。

失败模式：

- `UnityNativeCurves_PathRemapOnly`：旧 Rig 的绝对 Translation 导致头颈分离。
- `UnitySourceRestDelta_ToProductionRest`：把动作首帧当 Bind Rest，导致 T Pose。
- Edit Mode `Camera.Render`：GPU 蒙皮缓存不刷新，画面与骨 Transform 不一致。
- BakeMesh 代理：分体皮肤网格局部空间不同，不可用于最终渲染。

### 4. Blender 结果边界

早期 Blender Action 拆分与 MP4 被用户确认动作不符合预期。相关文件只可用于
FBX 结构诊断，禁止作为动作正确性、Unity 映射或视觉验收基准。

### 5. BlendShape 文件诊断边界

`Yummy_BlendShape_InPlace.fbx` 的原始结构为 12 个 `Geometry:Mesh`、0 个
`Geometry:Shape`、0 个 `Deformer`、0 个 `BlendShapeChannel`。仅有 6 条曲线
连接 Bob Hair Mesh 的 `Lcl Translation/Rotation`，因此不能从静态顶点或对象
变换推断真实表情。

禁止根据动作语义手工伪造表情曲线。动画师应启用 Morph/BlendShape Deformer
及动画导出，在干净场景重导后确认当前 22 个非 Basis Shape Key 与 2–174 帧权重
曲线均存在。完整名单和 SHA-256 见
`ArtSource/SapphiArt/Animation/Yummy/reports/compatibility_report.md`。

### 6. 当前交付边界

- 身体动作使用 Unity 混合 Rest 映射；头发和裙摆使用 Unity 运行时系统。
- 眉、眼睑、嘴型、脸颊和下颌 Shape Key 保持 0。
- `Eye_L/Eye_R` 曲线暂不应用，避免无表情文件时覆盖生产中性眼神。
- 当前 MP4 是 Unity 三渲二骨骼与物理预览，不是最终表情合成版。
- V004 生产 Blender、两个动画师 FBX 及 AnimeStrands r8d 外观资产未改动。

## Phase Animation Yummy Facial 技术交接：表情重建与分区二级运动

### 1. 面部曲线生成

`LengyueYummyUnityReview.BuildBodyClip()` 在身体重定向后调用
`AddFacialCurves()`，直接向最终 `AnimationClip` 写入
`SkinnedMeshRenderer` 的 `blendShape.<完整名称>` 曲线。目标通过 Renderer 名称
和 Shape Key 后缀唯一解析，缺失或重名立即中止构建。

当前 9 条曲线：

```text
Face_Blendshape.face_eye_close_L_BS
Face_Blendshape.face_eye_close_R_BS
Face_Blendshape.face_eye_wide_L_BS
Face_Blendshape.face_eye_wide_R_BS
Brow_BS.brow_sad_L
Brow_BS.brow_sad_R
Face_Blendshape.face_O_BS
Face_Blendshape.face_sad_BS
Face_Blendshape.face_smile_BS
```

时间轴按 `referenceFrame = unityFrame + 30` 对齐。闭眼主段约为 Unity
44–106 帧，120 帧附近睁大眼；嘴型使用低权重，避免手遮挡时产生夸张失真。
`ClipBuildReport` 用 `facialBindings == 9` 作为硬门禁，并记录
`facialSource=AnimatorReference_ReconstructedBecauseSourceFbxHasNoBlendShapeChannels`。

### 2. 眼球骨骼边界

`IsExcluded()` 不再排除 `Eye_L/Eye_R`，最终映射为 69 骨、690 条源 Transform
曲线。但 Standalone 实测两眼旋转范围均为 `0°`，说明动作 FBX 内的眼球曲线为
常量。运行时继续记录该值，但不把“必须产生旋转”作为通过条件，也不凭参考视频
人工制造不存在的眼球骨数据。

### 3. 头发与裙摆配置

Yummy 场景使用以下二级运动参数：

```text
HairSpringPhysics.quality = Full
HairSpringPhysics.maxBendDegrees = 24
SkirtPoseGuard.responseMultiplier = 1.45
SkirtPoseGuard.maxResponseDegrees = 42
```

`ConfigureEnhancedHair()` 会关闭 `H_Fringe_*`、`H_FringeFine_*`、
`H_Detail_*` 的 Full 标记。这样额前轮廓保持稳定，侧发和后发仍有 108 个运行时
模拟关节。`SkirtPoseGuard.Response()` 先乘响应倍率，再按角度上限钳制；默认值
`1.0/90°` 保持旧场景行为，仅 Yummy 验证场景使用增强参数。

### 4. 运行时验收

`LengyueYummyCaptureProbe` 除骨骼连续性外，还读取 Shape Key 实际权重峰值与
物理配置。当前通过值：

```text
eyeClose L/R       100 / 100
eyeWide L/R         92 / 92
browSad L/R         72 / 72
mouth max                 24
hair joints              108
hair max angle        24.0001 deg
hair tip range         0.3563 m
skirt max angle       32.8541 deg
mesh rendering errors         0
```

最终产物：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_Toon_Facial_EnhancedPhysics.mp4`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/evidence/Yummy_Unity_Facial_EnhancedPhysics_contact.png`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/unity_clip_build.json`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/unity_playmode.json`

## Phase Animation Yummy Hair v3 技术交接：根姿态锥约束

### 1. 双层角度约束

`HairSpringSolver` 现在维护两套旋转：

- `Rotations`：当前模拟链的父子旋转，用于计算骨骼实际连接位置。
- `referenceRotations`：只由 Hair Root 和静止姿态 Local Rotation 递推，不受
  上游模拟偏转影响。

每个 Verlet 步骤先按 `MaxBendDegrees` 限制相邻骨骼局部偏转，再按
`MaxChainBendDegrees` 限制相对根姿态方向的偏转。两次迭代投影用于满足两个
圆锥约束；球体碰撞修正后再次执行相同限制。

原实现只做第一层，因此 6 个活动段各偏转 20° 时会产生很大的累计弧度。第二层
将任意段相对随头部旋转的原始方向限制在 12° 内，保留发束曲线，同时消除整链
甩飞。

### 2. 运行时接口

`HairSpringPhysics` 新增：

```text
maxChainBendDegrees  配置的根姿态相对上限，Yummy 场景为 12°
MaxChainAngle        当前 Advance 内实际达到的最大根相对偏角
```

`Advance()` 每个固定子步把上限传给求解器，并汇总
`LastMaxChainAngleDegrees`。世界重力仍以 `Vector3.down` 传入，因此参考姿态随
头部旋转、重力方向不随头部旋转，头歪时自然形成相对侧倾。

### 3. Yummy 分区参数

```text
per-joint bend       20°
root-relative bend   12°
H_Back stiffness     >= 0.9
H_Back drag          >= 0.58
H_Back gravity       >= 0.22
H_Side gravity       >= 0.12
H_Fringe gravity     >= 0.14
```

### 4. 重力验收

`LengyueYummyCaptureProbe` 在 173 帧录制结束后执行不写入视频的确定性测试：

1. 固定角色姿态并让头发稳定。
2. 绕镜头前向轴分别旋转 Hair Root `+20°/-20°`。
3. 计算倾斜前后 Hair Root 局部重力向量差。
4. 将后发发梢位移投影到该方向，要求双向响应均大于 `0.005 m`。

最终实测：

```text
chain angle maximum       12.0001°
back tip total range       0.0994 m
back lateral range         0.0207 m
positive gravity response  0.0165 m
negative gravity response  0.0162 m
fringe range               0.0174 m
left/right side range      0.0384 / 0.0386 m
```

验证报告：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/unity_playmode.json`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/evidence/Yummy_Unity_Hair_ChainLimit_contact.png`

该表情是动画师预览驱动的近似重建，不等价于缺失的原始 Morph 曲线。有效
BlendShape FBX 到达后，只替换 `AddFacialCurves()` 的重建来源，不修改已验证的
骨骼映射和二级运动边界。

## Phase Animation Yummy Facial v2 技术交接：分区发丝与表情事件门禁

### 1. 表情事件时间轴

`AddFacialCurves()` 当前生成 12 条曲线。新增：

```text
Face_Blendshape.face_Puff_BS
Face_Blendshape.face_jaw_open_BS
Face_Blendshape.face_E_mouth_BS
```

关键事件按 Unity 帧定义：

```text
44-105   闭眼
105-110  闭眼权重降为 0，睁大眼权重升至 92
35-100   鼓腮 72
100-126  鼓腮渐退
148/157/166  三次张嘴峰值
152/161/170  三次闭嘴谷值
```

`LengyueYummyCaptureProbe` 记录 `eyesFullyOpenFrame`，并在 148/157/166 与
152/161/170 帧分别检查下颌张开和闭合。通过条件为完全睁眼帧 108–111、
`cheekPuffMax > 70`、`jawOpenMax > 30`、`mouthEMax > 15`，以及三组开闭采样
全部命中。

### 2. 发丝分区策略

全局 `maxBendDegrees = 20`，具体分区在 `ConfigureEnhancedHair()` 中修改
场景实例的 `HairSpringPhysics.Bone`，不改变生产 Prefab 默认值：

```text
H_Fringe_*:
  仅 _05/_06 启用 Full
  stiffness >= 1.6
  drag >= 0.72
  gravity *= 0.4

H_SideL_*/H_SideR_*:
  full = balanced
  stiffness >= 1.0
  drag >= 0.52

H_FringeFine_*/H_Detail_*:
  full = false
```

只启用刘海叶子骨 `_06` 无效，因为叶子骨旋转不会改变其自身位置；必须同时启用
父级 `_05`，才能产生可见而可控的末端位移。最终刘海、左侧发、右侧发相对头部
运动范围分别为 `0.0257/0.0799/0.0806 m`，总模拟关节为 98。

### 3. 最终验收值

```text
facial bindings       12
eyes fully open frame 110
cheek puff max        72
jaw open max          34
E mouth max           18
speech samples        3 open / 3 closed
hair max angle        20.0001 deg
fringe range          0.0257 m
left/right side range 0.0799 / 0.0806 m
skirt max angle       32.8541 deg
rendering errors      0
```

最终媒体及报告：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_Toon_Facial_EnhancedPhysics.mp4`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/evidence/Yummy_Unity_Facial_Hair_Final_contact.png`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/unity_clip_build.json`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/unity_playmode.json`

## Phase Animation Yummy Hair v4 技术交接：15.6° 整链上限

本阶段只放宽已有根姿态锥约束，不改变求解算法或分区动力学参数：

```text
per-joint bend       20°
root-relative bend   15.6°
```

配置同步点：

- `HairSpringPhysics.maxChainBendDegrees` 默认值为 `15.6f`。
- `LengyueYummyUnityReview.EnhancedHairMaxChainBendDegrees` 为 `15.6f`。
- `LengyueYummyCaptureProbe.EnhancedHairMaxChainBendDegrees` 为 `15.6f`。

`15.6 = 12 × 1.3`。运行时仍先执行单关节圆锥限制，再执行根姿态相对圆锥限制；
碰撞修正后重复约束。世界重力仍为 `Vector3.down`，因此放宽后只增加允许摆幅，
不会取消头部倾斜时的左右垂落。

Unity Standalone 实测：

```text
chain angle maximum       15.6001°
back tip total range       0.1264 m
back lateral range         0.0269 m
positive gravity response  0.0204 m
negative gravity response  0.0200 m
fringe range               0.0214 m
left/right side range      0.0490 / 0.0491 m
rendering errors           0
```

验证报告与最终媒体沿用 Yummy 交付路径：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/unity_playmode.json`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_Toon_Facial_EnhancedPhysics.mp4`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/evidence/Yummy_Unity_Hair_ChainLimit_contact.png`

## Phase Animation Yummy Hair v5 技术交接：三节刘海受控链

`ConfigureEnhancedHair()` 对 `H_Fringe_*` 使用以下配置：

```text
active joints  _04 / _05 / _06
stiffness      >= 1.45
drag           >= 0.66
gravity        >= 0.17
```

增加 `_04` 后，物理作用从发梢扩展到刘海中下段，使正脸视角能观察到完整发束的
惯性变化。高阻尼与较强回正力用于抑制三节链的累积摆动；求解器原有 `20°`
单关节上限和 `15.6°` 根姿态相对上限保持不变。

`LengyueYummyCaptureProbe` 对刘海范围执行双边门禁：

```csharp
report.fringeTipRelativeMovementM > .028f &&
report.fringeTipRelativeMovementM < .06f
```

最终实测刘海范围为 `0.0431 m`，上一版为 `0.0214 m`。总模拟关节为 105；
后发、侧发及重力倾斜指标保持稳定。专项关键帧位于：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/evidence/Yummy_Unity_FrontFringe_contact.png`

## Phase Animation Yummy Hair/Cloth v6 技术交接

### 1. 头发各向异性响应

`HairSpringPhysics.Bone` 与 `HairSpringSolver.Joint` 新增：

```text
lateralResponse
depthResponse
lateralGravityResponse
lateralInertiaResponse
```

`directionReference` 指向 `Head 1`。初始化时记录头骨相对角色根节点的静止旋转，
运行时由当前头骨旋转消除该绑定偏移，得到与角色视觉方向一致且随头部运动的参考
坐标系。刘海使用 `1.6/0.25/4.0/1.8`，侧发使用
`1.5/0.3/5.0/1.6`。

求解顺序：

1. 在校准坐标系中缩放 Verlet 速度和世界重力的横向/纵深分量。
2. 根据头部横向加速度、滚转角速度和重力倾斜生成受限横向驱动力。
3. 执行单关节与根姿态圆锥限制。
4. 相对静止端点增强横向偏移、压缩纵深偏移，再次执行双重硬限制。
5. 执行球形碰撞修正。

验收使用同一校准坐标系拆分 X/Z 范围，不能直接使用导入头骨的 Local X/Z。

### 2. 裙摆连续二级运动

`SkirtPoseGuard` 现在控制 30 根内外层裙摆骨骼。每帧先恢复静止姿态并应用既有
腿部避让，再计算：

```text
effectiveDown = worldGravity - filteredHipAcceleration
target = gravityTilt * gravityResponse
       + inertiaTilt * inertiaResponse
```

目标角经过 `3 Hz`、阻尼比 `0.68` 的 90 Hz 子步弹簧，最大二级角度为 `8°`。
Yummy 配置使用重力响应 `0.45`、惯性响应 `0.35`、横向响应 `1.6` 和最大髋部
加速度 `6 m/s²`。髋骨绑定轴先按静止姿态校准，禁止直接把原始骨骼 Local Axis
当作世界水平轴，否则目标角可能跨越 180° 并持续触顶。

运行时报告同时记录头发 X/Z 分量、裙摆下沿范围、弹簧角、惯性目标以及
`+15°/-15°` 双向重力倾斜响应。最终证据：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/unity_playmode.json`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/evidence/Yummy_Unity_Hair_Lateral_contact.png`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/evidence/Yummy_Unity_Cloth_Dynamics_contact.png`

## Phase Animation Yummy Hair/Cloth v7 技术交接

### 1. 头发：v5 积分路径加方向硬边界

`HairSpringSolver` 不再使用 v6 的 `ScaleAxis` 和
`ShapeDirectionalOffset`。基础运动恢复为 v5 的固定 90 Hz Verlet 路径：

1. 使用上一帧端点速度、阻尼、世界重力、风力和回正力计算候选端点。
2. 叠加由头部横向加速度、滚转和重力倾斜生成的轻量横向惯性。
3. 执行 `20°` 单关节限制和 `15.6°` 根姿态相对限制。
4. 在校准头部坐标系内独立钳制 X/Z 方向偏移，再恢复定长骨段。
5. 每次碰撞投影后重新执行上述限制。

`HairSpringPhysics.Bone` 的方向字段为：

- `lateralInertiaResponse`：只控制额外横向惯性，不缩放原始 Verlet 速度。
- `maxLateralDegrees`：相对静止端点的横向硬限制，`0` 表示关闭。
- `maxDepthDegrees`：相对静止端点的纵深硬限制，`0` 表示关闭。

Yummy 参数：

| 区域 | 活动关节 | 横向惯性 | X 上限 | Z 上限 |
|---|---|---:|---:|---:|
| 前脸刘海 | `_04/_05/_06` | 8.0 | 14° | 5° |
| 两侧头发 | `_05/_06/_07/_08` | 4.0 | 15° | 8° |

后发继续使用 v5 配置，不设置方向硬限制。这样保留原始自然回弹，只对用户明确
要求的刘海与侧发做方向增强。最终实测刘海 X/Z 为
`0.0320/0.0160 m`，侧发 X 为 `0.0531/0.0534 m`。

### 2. 裙摆：内收、释放、避让三阶段

`SkirtPoseGuard` 每帧按以下顺序处理 30 根内外层裙骨：

1. 恢复全部裙骨静止旋转和局部位置，避免多帧累积变形。
2. 在当前身体姿态下记录未处理的下沿半宽和前后半径。
3. 当腿部屈曲较小时，对侧裙施加内收旋转，并把中段裙骨径向收缩 `16%`。
4. 屈曲从 `5°` 到 `14°` 时平滑释放垂坠内收；超过 `14°` 后内收为 0。
5. 腿部避让从 `3°` 到 `10°` 平滑激活；大动作阶段继续使用原
   `1.45x/42°` 防穿模目标。
6. 最后叠加重力与髋部加速度驱动的 90 Hz 二级弹簧。

关键参数：

| 参数 | 值 |
|---|---:|
| 内层侧向垂坠角 | 14° |
| 外层侧向垂坠角 | 17° |
| 中段径向收缩 | 16% |
| 弹簧频率 | 2.6 Hz |
| 阻尼比 | 0.58 |
| 二级运动上限 | 7° |
| 末端响应 | 0.48 |

内收与避让不是同时保持最大值。低屈曲时优先形成贴身垂坠轮廓；腿接近裙摆时，
内收先释放，随后沿用已通过压力姿态测试的避让曲线。因此不能重新引入“常驻安全
距离”，否则裙摆会再次呈伞状外撑。

### 3. 验证指标

`LengyueYummyCaptureProbe` 新增以下门禁：

- 头发逐关节横向/纵深最大偏角。
- 刘海和左右侧发在校准头部坐标系中的 X/Z 范围。
- 同一身体姿态下，裙摆内收前后的下沿半宽和前后半径。
- 动画全过程下沿最小/最大半宽、最大内收权重和最大避让权重。

Unity 2022.3.38f1 Standalone 最终结果：

```text
simulated hair joints       113
fringe X/Z                  0.0320 / 0.0160 m
left/right side X           0.0531 / 0.0534 m
hair lateral/depth limit    15.0000 / 8.0001 deg
hair chain limit            15.6001 deg
skirt drape width           0.1580 -> 0.1116 m
skirt drape depth           0.2573 -> 0.2408 m
skirt hem movement          0.0878 m
skirt secondary angle       3.6683 deg
skirt avoidance weight max  1.0
mesh rendering errors       0
```

### 4. 工程注意事项

- `SkirtPoseGuard.Initialize()` 在重复初始化前必须恢复已保存的局部旋转和位置，
  否则 PlayMode 再绑定会把上一帧垂坠姿态错误地记录成新静止姿态。
- 裙摆宽度必须在角色根坐标系中测量；`Hips` 带导入轴旋转，直接使用
  `hips.InverseTransformPoint` 会把视觉横向映射到错误轴。
- macOS 上若主 Unity 已占用工程，可通过
  `/Users/bytedance/MateSoul/.dbg/yummy-unity-capture` 触发编辑器内回放；构建
  Player 使用 `/Users/bytedance/MateSoul/.dbg/yummy-unity-build-player`。
- 关键实现文件：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Runtime/HairSpringSolver.cs`
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Runtime/HairSpringPhysics.cs`
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Scripts/SkirtPoseGuard.cs`
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Validation/LengyueYummyCaptureProbe.cs`

## Phase Animation Yummy Camera v8 技术交接

### 1. 基准机位标定

动画师视频是 1280×720 的 Blender 界面录屏，真正用于标定的是左侧 3D 视口，
不能把时间轴、Outliner 和系统任务栏计入构图。视口坐标轴显示：

- X 轴保持水平、Z 轴保持垂直。
- 相机沿模型正面轴观察，没有明显偏航或俯仰。
- 画面是正交近景，不是透视全身镜头。

对齐后的 Unity 参数：

| 参数 | 值 |
|---|---:|
| 相机位置 | `(0.18, 1.41, 3.20)` |
| LookAt 目标 | `(0.18, 1.41, 0.00)` |
| 投影 | Orthographic |
| Orthographic Size | `0.50` |
| 画幅 | 16:9 |

相机位置与目标使用相同 X/Y，因此 `(0.18, 1.41)` 只负责平移构图，不会重新
引入偏航或俯仰。Unity 相机朝向实测与 `Vector3.back` 的夹角为 `0°`。

### 2. 构图验收

基准视频第 30 帧与 Unity 第 0 帧对齐后，采用归一化画面坐标比较：

- 主体中心：约 59% 画面宽度。
- 头顶：约 21% 画面高度。
- 腰线：约 80% 画面高度。

该机位会主动裁掉腿部和裙摆下半段，以突出面部、手势、刘海和上身表演。这是
基准视频本身的近景构图，不应再用“全身必须入画”的旧门禁回退相机。

### 3. 高清捕获链路

`LengyueYummyCaptureProbe` 暴露以下捕获参数：

- `captureWidth=1920`
- `captureHeight=1080`
- `captureMsaa=4`

捕获流程：

1. Camera 渲染到 4× MSAA 的 1920×1080 RenderTexture。
2. 使用 `Graphics.Blit` 显式 Resolve 到单采样 RenderTexture。
3. 从 Resolve 结果执行 `ReadPixels`，逐帧写入无损 PNG。
4. 173 帧完成后用 H.264 Main Profile Level 4.0、CRF 15、
   `yuv420p` 编码，并加入 AAC-LC 双声道静音轨。

禁止直接从多重采样 RenderTexture 调用 `ReadPixels`；不同图形后端可能产生
读取失败或未解析的边缘。无音轨 High Profile MP4 在部分内嵌播放器中无法播放，
因此最终交付固定使用 Main Profile + AAC-LC。运行时报告同时门禁分辨率、MSAA、
正交尺寸和正面朝向。

### 4. 最终规格

```text
resolution               1920 x 1080
frame rate               30 FPS
frames                   173
duration                 5.7667 s
capture MSAA             4x
camera orthographic size 0.50
camera forward error     0.0 deg
mesh rendering errors    0
runtime gate             passed
```

关键实现文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Editor/LengyueYummyUnityReview.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Validation/LengyueYummyCaptureProbe.cs`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/unity_playmode.json`

## Phase Animation Yummy Hair Contact v9 技术交接：保持主动态的表面分离

### 1. 根因与职责

旧的 3 个球体和弹簧末端碰撞没有覆盖真实发帽表面，末端残余为零仍可能发生
宽刘海片穿透。另有 `H_Detail_*`、`H_FringeFine_*` 共 12 条细丝的骨骼
被设为非活动，因而只跟随头部刚体变换，没有相邻主发束的重力倾斜。

`LengyueHairSurfaceContact` 的执行顺序为 11500，晚于 `HairSpringPhysics`
的 11000。它不修改骨骼旋转、不向原求解器注入额外速度，也不修改原物理参数。
当前由 Yummy 的两处 `ConfigureEnhancedHair` 配置；并未在所有角色或动画上
自动启用。

### 2. 数据与坐标约定

- 适用网格为冷月生成式短发：源网格 Z 向上、-Y 朝面部，根骨骼 `HairRoot`。
- 原始顶点保存为 `RestVertices`；`target.sharedMesh` 使用私有动态副本。
  禁用组件恢复原网格，销毁时释放副本。
- 通过三角连通性和焊接后的 UV 接缝识别 38 个部件。不能仅凭最强权重是否
  `HairRoot` 分类：部分发束顶部同样是根骨骼权重，但不属于发帽。
- 以发束名和 UV 纵向行确定横截面，整行使用相同接触推出量，避免压扁发束。

### 3. 运行流程

1. 初始化时为每条细丝选择一条相邻主发束；按横截面中心到主发束三角形的
   最近点取得重心坐标，插值主发束权重。细丝顶点位置不变，同一横截面使用
   相同权重，以避免横截面扭曲。绑定覆盖 12 条 / 2952 顶点。
2. 只把独立发帽部件的三角面放入 XZ 空间桶。查询朝前的最小 Y 表面深度，
   以 0.8 mm 间距分离。使用保守包围盒排除不可能接触的候选面。
3. 每帧根据当前骨骼矩阵在 HairRoot 绑定坐标中重建未约束刘海；
   查询顶点、三角面中心和边中点，取得所需分离量。
4. 横截面取最大所需量；相邻行以 0.65 系数向两侧衰减延展，只增加必要分离，
   不用平均降低接触修正。每帧从原始位置重算，避免累计漂移。
5. 仅对源 -Y 深度修正，X/Z 位置不变；通过混合蒙皮矩阵的逆变换写回私有
   网格顶点。相同权重的矩阵只混合、求逆一次。保留原 UV、材质和风格化法线。

### 4. 验证方式与边界

`hair_surface_contact.json` 来自独立 double 精度射线/三角面查询。
每帧检查 3502 个刘海顶点、117012 个三角面采样点，并核对运行时实际网格
编码位置与重建位置。原始求解器源文件逐字一致；同一 PlayMode 环境下主骨骼和
主发束 X/Z 差为零。Standalone 与 Editor 有亚微米位置差，属于运行环境的
浮点差异，报告保留具体数值而不是抹成零。

该层只解决当前发帽与前刘海接触，以及细丝漏随动。其他动画需要重新跑表面
采样；设备端性能、手指接触和发束互碰尚未验证。接触段前后轨迹必然包含几何
推出，不应宣称所有顶点三维轨迹完全保留。

临时诊断位于 `LengyueYummyCaptureProbe` 的 `#region debug-point`：
使用 `reportDirectory` 回溯仓库查找 `.dbg/hair-scalp-contact.env`，兼容
Standalone 的 `Application.dataPath` 位于 `.app` 内部的情况。
正式验收前保留该插桩及调试服务器，验收后依据 debugger 工作流移除。

### 5. 文件与工程注意事项

- 组件：`/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Runtime/LengyueHairSurfaceContact.cs`
- 捕获配置：`/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Editor/LengyueYummyUnityReview.cs`
- 帧验证：`/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Validation/LengyueYummyCaptureProbe.cs`
- 结果：`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/hair_surface_contact.json`
- 视频：`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_HairContact_v9.mp4`

更改脚本字段后必须先完成 Unity Editor 刷新与域重载，再触发 Standalone
构建；在刷新尚未完成时直接构建，可能出现 Editor / Player class layout
不一致。新运行门禁同时检查 `runtimeErrors=0`，不能只检查
`meshRenderingErrors=0`。

## Phase Animation Yummy Sleeve v10 技术交接：衣袖柔性模拟

- 生产衣物为 `LavenderCasual_Garments`，64574 顶点、113128 三角面，
  UV2.x 的 ValidationPart：3=袖口/缝线，5=短袖上衣；两类共14559顶点。
- 原先没有 Sleeve/Cloth 二级动态。基线173帧的 BakeMesh 与线性蒙皮计算差
  小于0.52微米。袖筒半径中位数约53.3mm，需在自由区增加重力垂坠和惯性。
- 源网格坐标 Z 向上、-Y 向前。当前导入后的 UpperArm_L 绑定点位于
  X=-0.11746m，UpperArm_R 位于 X=+0.11746m，应通过绑定位置识别左右。
- 初始化日志采集前 Animator 已执行一次，不能将该时刻 boneWorld 当作 Rest。
  静止骨骼坐标必须从 mesh.bindposes 的逆矩阵恢复。
- 插桩位于 `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Validation/LengyueYummyCaptureProbe.cs`
  的 `#region debug-point sleeve-*`。服务器7779，日志与环境名
  `sleeve-soft-dynamics`，不覆盖7778端口的头发会话。

### 1. 运行时组件与职责

新增
`/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Scripts/LengyueSleeveCloth.cs`。
执行顺序12500，位于身体动画和裙摆骨骼修正之后。组件已经写入生产
`Lengyue_GameCharacter.prefab`；Yummy场景构建发现缺失时也会安装。

1. 每侧构造6圈×20点的模拟面，共240点；通过源坐标 `abs(X)=.125–.256m`
   绑定10623个衣袖顶点。第一圈固定，自由度按长度平滑增加。
2. 肩部、袖身、缝线和荷叶边使用相同双线性位移场。径向松量收缩22%，
   与世界重力共同改变原先撑开的圆筒轮廓。
3. 世界坐标Verlet积分，步长不超过1/120秒；跟随动画目标的弹性恢复、
   阻尼和相邻点抗拉约束产生惯性滞后、下垂与回落。前后步长比修正速度，
   避免不同渲染帧率直接改变惯性。
4. 两侧上臂/前臂使用渐变半径胶囊，胸侧使用随Chest变换的胶囊。
   先检查屈肘是否压迫袖口，按需沿上臂向肩部收拢，最多5cm；
   随后执行接触投影。这样避免袖边相邻点向前臂两侧分裂。
5. 从动画蒙皮后的点加上柔性位移，再通过**当前有效混合蒙皮矩阵的逆**
   写入私有网格。相同权重只混合/求逆一次，降低每帧CPU开销。
   根据变形前后几何法线旋转原有风格化法线，保留原材质/UV。

### 2. 蒙皮精度与生命周期

独立播放器默认质量档只有2个骨骼影响，Editor基线使用4个。
如果求解器始终用4权重逆蒙皮，实际BakeMesh可出现约2cm偏差，即使
内部胶囊指标通过也不代表渲染表面一致。

实测本项目Unity2022.3.38f1的Standalone中，即使renderer显式quality为
Bone4，全局skinWeights=2仍限制实际蒙皮。通过GetVertexBuffer直接读取GPU
顶点验证：2权重重建误差小于0.6微米，4权重重建误差可达约36mm。
因此不能只看Inspector的renderer quality推断实际影响数。

`SkinInfluences` 在初始化时取全局skinWeights、renderer显式quality和4三者的
上限交集，按实际影响数截断并重新归一化权重。保持renderer质量档不变，因此
共用网格上的裙摆继续使用原来的蒙皮行为。验证脚本也需读取日志
`qualitySkinInfluences`，不能把4权重重建结果直接与2权重Player比较。
当前组件按初始化时质量档绑定，运行中切换质量档需重建组件绑定。

`Initialize()` 只创建本实例的动态网格；`ResetSimulation()` 清除速度历史；
`Evaluate(dt)` 支持受控重放。禁用恢复源网格，启用恢复实例网格，
销毁时释放实例。源FBX、源网格、骨架层级/Rest、头发求解器均未修改。

### 3. 参数与验证边界

- `radialEase=.22`，`gravityScale=.65`，`hemFrequency=2.4Hz`，
  `dampingRatio=.38`。肩根恢复频率4.8Hz，向袖口渐变。
- 2mm接触壳层为视觉表面留余量；报告负的壳层间隙需要加回2mm才能得到
  与胶囊本体的间隙。它不等于真实人体网格的逐三角面穿透证明。
- `LengyueYummyCaptureProbe` 除173帧运动外，加入同一固定姿势下重力开/关对照、
  20mm整体平移后滞后检查，以及2秒固定姿势回落检查。
- 临时GPU校核采样第0/85/172帧。当前Metal蒙皮缓冲步长为10个float，
  位置在每顶点前3个float，坐标相对rootBone；不能再乘衣物源网格的Z-up变换。
- Editor阶段：重力下垂约16.17mm，初始滞后19.76mm，回落残差0.00522mm；
  肩部固定误差小于0.5微米。最终以Standalone数值报告为准。
- 这是针对当前服装的低分辨率约束面；没有实现布料自碰撞或严格不可伸长约束。
  尚未验证任意动作集和PICO真机实时预算；桌面袖子组件约20ms/帧，
  不应直接宣称达到头显实时性能要求。
- 最终Standalone门禁通过：173帧，运行/渲染错误均为0。
  袖筒中位半径L 53.285→43.337mm、R 53.358→43.590mm；
  重力下垂16.514mm，20mm平移初始滞后19.858mm，停止2秒残差0.00278mm。
- 实际BakeMesh肩部固定误差≤0.482微米；GPU第0/85/172帧重建误差≤0.557微米。
  每帧67163个顶点/边中点/面心独立采样，胶囊本体最坏残余穿入0.341mm；
  不是“任何动画完全无穿模”的证明。帧内边长拉伸比p99的最坏值为1.174，
  孤立短边最大5.991，不能描述为严格不可伸长布料。
- 正式报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/sleeve_cloth_v10.json`。
- 最终视频：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_SleeveCloth_v10.mp4`。
  对比视频为同目录 `Lengyue_Yummy_SleeveCloth_v10_Comparison.mp4`，左v9、右v10。
  两者均为1920×1080、30fps、173帧、H.264 Main L4.0/yuv420p+AAC，
  带faststart，完整解码验证通过。

## Phase Garment Layer Dynamics 技术交接：基线与设计方向

- 新任务以v10为外观/袖子动态基准，补充衣层互相避让、吊带/内衬/裙身/
  丝带随动，以及肩部到领口的柔性衔接。
- 独立记录：`/Users/bytedance/MateSoul/debug-garment-layer-dynamics.md`。
  新会话服务器端口7780；Probe新增完整网格基线及每帧float32 Base64网格输入、
  世界蒙皮矩阵观察，未改变衣服行为。
- 重点观察v10的按需袖口轴向收拢是否使袖子进入外层裙身，以及
  `abs(X)=.125m`固定边界与领口/肩带之间的刚度差。
  这些是待验证方向，不代表已确定修复方案。
- 保留现有 `sleeve-soft-dynamics` 与 `hair-scalp-contact` 会话，
  不能把用户对袖子动态的认可解释为整套碰撞已验收通过。
- 完整观察数据解析：
  `/Users/bytedance/MateSoul/.dbg/garment-analysis.py`；
  产物 `garment-layer-dynamics/baseline.npz`（173帧Editor 4权重）和
  `components.json`（42连通件）。默认全局质量不能为了简化计算改动。
- 连通件语义：0/4/40/41袖边；3整件上衣；8胸前裙身；7胸前包边；
  11/38吊带；10/12/37/39纽扣及线；21领圈；6/5外裙及裙边；
  2/1下层裙及边；9腰带；22/27蝴蝶结；25结心；
  24/30垂绳，23/35绳端。其余低于0.25m的part0为鞋袜，不应加裙装物理。
- 本任务下一步采用单一动态网格写入者，配合共享接缝位移、世界空间低分辨率
  布料图和层间接触。吊带目标需继承相邻肩部上衣的蒙皮场并保持原静态偏移。
  外围袖子在裙身外、胸前上衣在裙身内，两类约束须区分。
- `.dbg/garment-contact-baseline.py` 用Blender BVHTree逐三角面检查。
  最近表面有向距离包含胸前裙身开口边缘歧义，不可把最小负数直接当作
  真实穿入深度；相交面也需区分自由袖口、袖身和设计上的遮盖/缝合区域。

## Phase Garment Cloth v11 技术交接

> 历史方案：用户于2026-09-28选择恢复v10。下述内容只用于追溯，
> `LengyueGarmentCloth` 及运行时绑定资源已删除，不得按当前架构继续依赖。

### 1. 架构与数据

- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Scripts/LengyueGarmentCloth.cs`
  是无MonoBehaviour的整衣求解器，由 `LengyueSleeveCloth` 持有并在袖子更新前
  执行。二者共用同一输出数组和动态Mesh，保证每帧只有一个网格写入者。
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/scripts/build_garment_cloth_binding.py`
  从运行时捕获的源网格生成绑定；正式资源为
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Resources/LengyueGarmentCloth.json`。
  资源带顶点数门禁，不能跨FBX网格版本复用。
- 当前布料图共1404点、4670条抗拉边，8点紧支撑C2核驱动42024个顶点。
  区域依次为双层裙、胸前裙身、内衬/上衣、左右吊带、左右蝴蝶结、
  左右垂绳。两层裙共享参数场，附属包边/纽扣/绳端复用宿主位移。

### 2. 每帧流程

1. 按renderer实际蒙皮质量截断并归一化权重，计算当前混合蒙皮矩阵。
2. 吊带顶点通过邻近上衣肩部的蒙皮场重建目标位置，保留原始静态偏移；
   领圈使用同一机制，缓解Chest刚性目标与肩袖混合蒙皮之间的突变。
3. 以不超过1/120秒的步长执行世界空间Verlet积分、弹性恢复、重力和抗拉约束。
   裙腰、胸前裙身上下缝边固定；主体中部、裙摆、蝴蝶结和垂绳按区域自由度运动。
4. 用源胸前裙身恢复出的128×29外表面网格构建连续径向接触场。
   袖子控制点和密集顶点均投影到8mm动态安全层之外；中央内衬不执行外推。
5. 将世界空间位移通过当前混合蒙皮矩阵的逆写回私有网格，并统一重算法线。

### 3. 验证与边界

- 最终macOS Standalone使用2个有效骨骼影响。173帧门禁通过，运行/渲染错误为0；
  GPU第0/85/172帧重建误差不超过0.568微米。
- 袖子保持原参数：`radialEase=.22`、`gravityScale=.65`、
  `hemFrequency=2.4Hz`、`dampingRatio=.38`。最终重力下垂14.994mm，
  20mm平移滞后19.841mm，2秒残差0.0061mm。
- 9个整衣区域重力响应分别为10.174、1.786、2.070、1.140、1.140、
  6.327、6.269、4.789、4.791mm；20mm平移滞后19.383mm，
  2秒残差0.0033mm。
- 同为Editor 4权重条件下，第98帧吊带/肩部最大新增间距
  18.862→5.105mm，平均1.720→0.307mm。该指标证明衔接改善，
  不等价于人体表面碰撞验证。
- 重点帧自由袖口/外袖与胸前裙身的保守三角相交对总数下降20.28%。
  缝合边、包边遮盖和故意共面部分必须排除；最近面有向距离在开口处也不能
  直接解释为穿入深度。
- 当前组合求解在桌面Standalone平均约56.7ms/帧，尚未做PICO真机预算。
  进入实时部署前优先优化42024顶点的8点位移插值和4544组混合矩阵逆，
  推荐Burst/Jobs或Compute Shader，不要通过减小袖子摆幅换取性能。

### 4. 交付与复现

- 最终报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/garment_cloth_v11.json`。
- 主成片：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_GarmentCloth_v11.mp4`。
- v10/v11对比：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_GarmentCloth_v11_Comparison.mp4`。
- 两个视频均为1920×1080、30fps、173帧、H.264 Main L4.0、
  yuv420p、AAC静音轨、faststart，并已完整解码。
- 打包与复核脚本：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/scripts/package_garment_cloth_v11.py`。
  该调试会话在v11交付时保持OPEN；用户随后选择恢复v10，现已关闭并转为
  历史审计记录。

## Phase Sleeve v10 ShadowFabric 技术交接

### 1. 当前生产基线

- 当前袖子运行时是已验收v10：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Scripts/LengyueSleeveCloth.cs`。
  其SHA-256为
  `b38485cd4d10faf17536fb65e8440cebf9486b10b5a19d255ed15bd4a22a36e0`，
  与 `.dbg/garment-layer-dynamics/LengyueSleeveCloth_v10.cs` 一致。
- `LengyueGarmentCloth.cs`、对应meta、`LengyueGarmentCloth.json`及对应meta
  已从生产运行时移除。v11成片和报告仅为历史产物。
- 不得重新引入v11整衣耦合，除非用户再次明确选择该方向。

### 2. 手臂阴影根因与修复

手臂使用独立材质
`/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Materials/Lengyue_Toon_SkinCoverage.mat`。
frame 162隔离结果如下：

1. 只对该材质关闭 `_ReceiveShadows`，锯齿仍存在。
2. 将 `_ShadowSoftness` 从`.06`提高到`.25`，明暗分带变为连续过渡。
3. 设置 `_UnlitBlend=1` 后问题完全消失。
4. `_DebugUV=1`确认异常像素来自SkinCoverage，而非Face材质。

因此根因是手臂低面数/分裂法线场被过窄的Toon分带放大，不是实时阴影图。
最终只保留SkinCoverage的 `_ShadowSoftness=.25`；实验性的全局阴影距离和
级联调整已恢复，避免无证据改变场景阴影预算。frame 162手臂内部高频能量
下降12.17%，该值仅是图像空间对比指标。

### 3. 织物微表面

Shader文件：
`/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Shaders/MateSoulCharacterToon.shader`。

新增 `_FabricDetailMap`，通道约定为：

- R：织物高度及底色微调。
- G/B：切线空间法线X/Y。
- A：纤维边缘光权重。

Fragment阶段先以R通道轻微调制原有服装Atlas，再用GB扰动世界空间法线，
最后以A通道控制视角相关的软纤维光。此方案不替换原色图，不改变UV、
服装几何或v10动态；参数为Detail Scale 8、Albedo `.06`、Normal `.10`、
Sheen `.045`、Sheen Power `3.5`。

织物资源：

- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Textures/Lengyue_Fabric_Weave.png`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v004/scripts/build_fabric_detail.py`

纹理为256×256无缝RGBA，SHA-256为
`f45abc6e46f3c15910d1d2d123c0cc8b1e6eefc8da64c7fa68403f4a598ab9cd`。
Importer使用Linear、MipMap、Trilinear、Aniso 8。原
`Lengyue_Clothing_Atlas.png` 使用Trilinear、Aniso 4、压缩质量100。
`LengyueLayeredDressBuilder.cs` 已写入同样绑定和参数，后续重建材质时
不会把本次设置覆盖回旧值。

### 4. 捕获与诊断

`LengyueYummyCaptureProbe.cs` 和 `LengyueYummyUnityReview.cs` 将
`captureSupersample` 设为2。录制先以3840×2160、4×MSAA渲染，再缩小至
1920×1080，主要用于稳定近景边缘和织物微细节。

Probe保留frame 162四种隔离输出和材质设置日志，Shader保留隐藏
`_DebugUV`。这些是当前开放调试会话
`/Users/bytedance/MateSoul/debug-v10-shadow-fabric.md` 的证据，
用户验收后方可删除。网络插桩失败不能阻断正式渲染或验证门禁。

### 5. 验证与交付

- Unity 2022.3.38f1 macOS Standalone：173帧，运行/渲染错误均为0，PASS。
- v10物理：10623驱动顶点、2权重、重力下垂16.514mm、
  平移滞后19.858mm、2秒残差0.00278mm。
- 报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/v10_shadow_fabric.json`
- 主成片：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_SleeveCloth_v10_ShadowFabric.mp4`
- 对比片：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_SleeveCloth_v10_ShadowFabric_Comparison.mp4`
- frame 162对比：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/evidence/Yummy_v10_ShadowFabric_Frame162_Comparison.png`

## Phase Wardrobe v005 技术交接：收窄荷叶衣领

### 1. 资产链路

v005采用非破坏性增量构建：

1. 以
   `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v004/source/Lengyue_LayeredDress_v004.blend`
   为只读输入。
2. `build_narrow_collar.py` 新增独立衣领对象，不修改v004已有服装对象。
3. 保存
   `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v005/source/Lengyue_NarrowCollarDress_v005.blend`。
4. 导出
   `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v005/exports/Lengyue_NarrowCollarDress_v005.fbx`。
5. 同步到Unity
   `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Models/Lengyue_NarrowCollarDress.fbx`。
6. `LengyueLayeredDressBuilder`重绑116骨骼并更新唯一生产Prefab。

v004源文件和FBX均不覆盖。回滚时将Builder的 `Model` 常量恢复为
`Lengyue_LayeredDress.fbx`并重新执行生产角色构建即可。

### 2. 衣领几何与蒙皮

- 对象名：`LayeredDress_NarrowRuffleCollar`。
- 144段环向采样、7行径向采样、18个波瓣。
- 开口宽度84.49mm，相对原约108mm收窄21.77%。
- 厚度1.2mm，边缘使用0.25mm两段Bevel，避免低面数硬折线。
- 外侧连接边100% Chest；内侧最大45% Neck，并沿径向平滑衰减。
  该权重场让外缘稳定连接衬衫，同时让贴颈内缘跟随低头和侧倾。
- Unity中使用独立 `ValidationPart=6`。`LengyueSleeveCloth`只处理
  Part 3/5且 `abs(X)>=.125m`，因此衣领不会进入v10袖子求解器。
- 衣领使用Atlas tile 7，与当前袖口内衬/荷叶边粉紫色完全一致；
  短袖主体继续使用tile 1。统一使用 `Lengyue_LayeredDress_Toon`，
  不增加材质槽和Draw Call。

### 3. 构建门禁

`LengyueLayeredDressBuilder.BuildReport`新增：

- `collarVertices`：必须大于0，当前为4904。
- `narrowCollarImported`：必须为true。
- `sleeveDrivenVertices`：必须严格等于10623。
- `sleeveTargetRebound`：必须为true。Builder替换服装Renderer后显式设置
  `LengyueSleeveCloth.target`，避免重复构建Prefab时保留已销毁引用或null。

Builder同时导出每个姿态的衣领、颈部及旧服装BakeMesh数据。衣领专项脚本：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v005/scripts/validate_collar_geometry.py`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v005/scripts/compare_v004_v005.py`

验证结果：

- 73个Rest/动画/压力状态中衣领与颈部三角相交为0。
- 最小动画采样间隙3.04mm。
- v005旧服装Part 0–5与v004对应顶点在73份姿态文件中最大差值0m。
- Yummy Standalone 173帧通过，实际2权重路径，运行/渲染错误均为0。
- `LengyueSleeveCloth.cs`仍与已验收v10备份同哈希，物理数值未漂移。

### 4. 报告与复现

- Blender构建报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v005/reports/model_build.json`
- Unity构建报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v005/reports/unity_build.json`
- 衣领碰撞报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v005/reports/collar_geometry.json`
- 旧服装等价性报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v005/reports/existing_garment_regression.json`
- 最终汇总：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/v10_narrow_collar.json`
- 打包脚本：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/scripts/package_v10_narrow_collar.py`

全量旧服装压力测试仍会报告v004已经存在的极端裙摆/手臂接触；
不能将这些历史失败归因于衣领，也不能把衣领专项PASS描述为全衣任意姿态无穿模。

## Phase Lavender Chibi Bowl 技术交接

### 1. 阶段概述

本阶段新增可直接用于Unity或PICO Spatial运行时的陶瓷碗资产。模型按
`Ø138 x H70 mm`制作，使用真实米制、闭合实体拓扑、独立内外壁UV和三套
釉面PBR材质；GLB与FBX均完成回读验证。

资产根目录：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Bowl/`

### 2. 几何与UV设计

- `Bowl_Ceramic`由36点闭合剖面绕Z轴旋转128段生成。
- 剖面从底部中心依次经过底部凹槽、58mm底足、外壁、卷边、内壁并回到
  碗内中心，因此不依赖Solidify，导出后仍保持封闭体积。
- `UVMap`按区域采用两种映射：外壁使用连续圆柱映射，碗内使用俯视径向
  映射；卷边和底足使用独立淡紫材质。
- `MicroUV`以约5mm尺度平铺陶瓷微法线，避免和角色图案共用同一纹理密度。
- 原点位于底足接地中心，局部包围盒为
  `[-69, -69, 0] mm`到`[69, 69, 70] mm`。

### 3. 贴图生成流程

`prepare_bowl_textures.py`不依赖Pillow，通过FFmpeg解码用户提供的WebP，
再用NumPy完成以下流程：

1. 从图像四边对近白背景执行连通域泛洪，只移除与外边界连通的背景，
   避免眼白和脸部高光被错误透明化。
2. 输出`character_decal.png`，并按碗面物理纵横比压缩到圆柱UV。
3. 生成2048×1024无缝外壁图，在四个正交方向重复角色并排布花边。
4. 生成1024×1024径向碗内图和512×512淡紫底足图。
5. 生成512×512可平铺切线空间陶瓷微法线。

完整重建命令：

```bash
/Applications/Blender.app/Contents/MacOS/Blender \
  --background \
  --python /Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Bowl/scripts/prepare_bowl_textures.py

/Applications/Blender.app/Contents/MacOS/Blender \
  --background \
  --python /Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Bowl/scripts/build_bowl_asset.py
```

### 4. 材质与运行时约定

- `MAT_Bowl_Exterior_Glaze`：外壁角色、花边与下沿双线。
- `MAT_Bowl_Interior_Glaze`：碗底角色圆章与径向装饰。
- `MAT_Bowl_RimAndFoot_Glaze`：卷边、底足和底部。
- 三套材质均为非金属陶瓷，IOR 1.46、Roughness 0.17–0.19、
  Coat Weight 0.72–0.78，并共享`ceramic_micro_normal.png`。
- PICO Spatial优先使用自包含GLB；Unity可使用FBX和外置PNG。
  `ceramic_micro_normal.png`必须按Linear/Non-Color导入。

### 5. 验证与避坑

- 源网格：4354顶点、4480面、8704三角形、边界边0、非流形边0。
- GLB/FBX回读后均为1个网格、3个材质，尺寸均保持
  `138 x 138 x 70 mm`。
- Blender MCP执行过程中不能调用`bpy.ops.wm.read_factory_settings`，
  该操作会卸载MCP插件并断开连接；脚本使用`clear_scene_data()`只清理场景
  数据块。
- 小型米制模型的Area Light不能沿用人像场景的数百瓦功率。当前展示场景
  使用0.75–6W软光，避免浅色釉面高光剪裁。
- 资产未包含碰撞代理或LOD。动态物理场景应新增简化碰撞体，不能直接使用
  8704三角形碗体作为动态Mesh Collider。

### 6. 核心资源

- 构建脚本：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Bowl/scripts/build_bowl_asset.py`
- 贴图脚本：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Bowl/scripts/prepare_bowl_textures.py`
- Blender源文件：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Bowl/source/LavenderChibiBowl_138mm.blend`
- GLB：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Bowl/exports/LavenderChibiBowl_138mm.glb`
- FBX：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Bowl/exports/LavenderChibiBowl_138mm.fbx`
- 自动校验报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Bowl/reports/bowl_model_report.json`

## Phase Lavender Tableware 技术交接：马克杯与盘子

### 1. 系列化贴图架构

马克杯和盘子复用碗阶段产出的透明角色贴花、淡紫陶瓷配色和
`ceramic_micro_normal.png`，但不直接复用碗的最终UV图：

- 马克杯圆周约251mm、高95mm，使用碗外壁图会把角色拉成长条，因此
  `prepare_mug_textures.py`重新按杯身物理比例排版2048×1024外壁图。
- 盘面为200mm圆形观察面，使用独立2048×2048径向图，避免从1024碗内图
  放大产生采样损失。
- 两个脚本动态加载
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Bowl/scripts/prepare_bowl_textures.py`
  中的无Pillow图像处理函数，保证背景分割、缩放和图形绘制规则一致。

### 2. 马克杯几何与材质

`build_mug_asset.py`包含以下核心流程：

1. 25点闭合剖面绕Z轴旋转128段，生成杯身、底厚、卷边和内壁。
2. 7点Bezier路径在XZ平面生成D形把手，截面半径5.8mm。
3. 曲线转网格后先执行`remove_doubles`，再封闭两个端环并三角化端盖。
4. 把手端点深入杯壁，保证视觉连接连续；杯身与把手保持两个独立网格。
5. 外壁使用圆柱UV，杯内使用径向UV，微法线使用独立`MicroUV`。

材质分工：

- `MAT_Mug_Exterior_Glaze`：人物、上沿花边与下沿双线。
- `MAT_Mug_Interior_Glaze`：杯底角色圆章和纯色杯壁。
- `MAT_Mug_RimFootHandle_Glaze`：杯口和底足。
- `MAT_Mug_Handle_Glaze`：无微法线把手釉面，避免非规则曲面切线拉伸。

源网格总计9372三角形。GLB/FBX回读包围盒均约为
`113.0041 x 80 x 95 mm`。

### 3. 盘子几何与材质

`build_plate_asset.py`使用23点闭合剖面绕Z轴旋转128段：

1. 底部中心向外形成浅凹底面和约156mm底足。
2. 外侧曲面上升到`Z=25 mm`并形成圆润口沿。
3. 内侧从94mm半径缓慢下降到中心，形成可承载食物的连续浅盘面。
4. 顶面采用径向UV，侧壁采用连续圆柱UV，底部采用独立投影UV。

材质分工：

- `MAT_Plate_Top_Glaze`：中心角色与盘面环形装饰。
- `MAT_Plate_Edge_Glaze`：侧壁连续花边。
- `MAT_Plate_Underside_Glaze`：底足和底面。

源网格为5376三角形，GLB/FBX回读尺寸均为`200 x 200 x 25 mm`。

### 4. 完整重建

```bash
/Applications/Blender.app/Contents/MacOS/Blender \
  --background \
  --python /Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Mug/scripts/prepare_mug_textures.py

/Applications/Blender.app/Contents/MacOS/Blender \
  --background \
  --python /Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Mug/scripts/build_mug_asset.py

/Applications/Blender.app/Contents/MacOS/Blender \
  --background \
  --python /Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Plate/scripts/prepare_plate_textures.py

/Applications/Blender.app/Contents/MacOS/Blender \
  --background \
  --python /Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Plate/scripts/build_plate_asset.py
```

四个脚本都会保留真实米制；两个构建脚本还会渲染四视图、导出GLB/FBX并
回读验证网格、材质和包围盒。

### 5. 验证与运行时约定

- 马克杯：杯身与把手边界边0、非流形边0；GLB/FBX均为2网格、4材质。
- 盘子：边界边0、非流形边0；GLB/FBX均为1网格、3材质。
- GLB为PICO Spatial优先格式；FBX和外置PNG用于Unity。
- 两套资产均未绑定Collider。马克杯建议使用杯身凸包加把手胶囊体；
  盘子建议使用简化圆柱或低面数凸包。
- 角色Base Color按sRGB导入，`ceramic_micro_normal.png`按Linear/
  Non-Color导入。

### 6. 核心资源

- 马克杯根目录：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Mug/`
- 马克杯报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Mug/reports/mug_model_report.json`
- 盘子根目录：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Plate/`
- 盘子报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Plate/reports/plate_model_report.json`

## Phase Lavender Chibi Cupcake 技术交接

### 1. 阶段概述

本阶段新增`70 x 70 x 75 mm`纸杯蛋糕资产，交付根目录为：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Cupcake/`

资产由7个闭合网格组成：纸托、蛋糕胚、奶油、粉/紫爱心、粉/紫糖珠。
GLB与FBX均完成回读尺寸、材质和网格数量验证。

### 2. 贴图生成

`prepare_cupcake_textures.py`复用碗阶段的FFmpeg/NumPy图像工具，但针对
纸杯蛋糕生成以下独立资源：

- `cupcake_liner_basecolor.png`：2048×1024无缝纸托图，移除展开图中的
  粘合边、文字和裁切线。
- `paper_normal.png`：纸纤维微法线。
- `cake_basecolor.png`与`cake_normal.png`：金色蛋糕胚和烘焙孔隙。
- `frosting_basecolor.png`与`frosting_normal.png`：暖香草奶油资源；
  当前高模主要依靠几何起伏，Normal保留给LOD材质。
- `character_decal.png`：系列统一角色透明贴花。

纸托圆周与高度的物理比例约为6:1，角色在贴图中预先纵向拉长，映射到
锥形纸托后恢复约22×20mm的正常比例。

### 3. 几何构成

- `Cupcake_Liner`：128段圆周、32道余弦褶皱，顶边同步加入0.85mm波浪高度。
- `Cupcake_Cake`：带双频低幅轮廓扰动的闭合蛋糕胚，顶部轻微拱起。
- `Cupcake_Frosting`：96层×128角向采样的闭合参数曲面。
- `Cupcake_Hearts_*`：28点心形轮廓挤出后按奶油表面法向放置。
- `Cupcake_Pearls_*`：10段×6环低面数球体合并为双色批次。

奶油表面的主要函数为：

```text
baseRadius = 31.25mm * (1 - t)^0.72
phase = 2π * 1.15 * t - angle
ridge = 3.0mm * ((1 + cos(4 * phase)) / 2)^1.8 * (1 - t)^0.42
nozzle = 0.75mm * cos(8 * (angle + 2π * 0.7 * t)) * (1 - t)^0.58
radius = baseRadius + ridge + nozzle
height = 34mm + 41mm * t
```

四向主脊在高度方向形成约4.6圈连续裱花，八向细棱模拟星嘴；该曲面只有一个
底部封口和一个顶点，不存在管线自交形成的圈间切缝。

### 4. 材质与导出

- `MAT_Cupcake_PaperLiner`：纸托Base Color、纸纤维Normal，Roughness 0.48。
- `MAT_Cupcake_Sponge`：蛋糕Base Color、孔隙Normal，Roughness 0.56。
- `MAT_Cupcake_Frosting`：暖香草色、轻微SSS、Roughness 0.28。
- `MAT_Candy_Pink`与`MAT_Candy_Purple`：高饱和釉面糖果。

完整重建命令：

```bash
/Applications/Blender.app/Contents/MacOS/Blender \
  --background \
  --python /Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Cupcake/scripts/prepare_cupcake_textures.py

/Applications/Blender.app/Contents/MacOS/Blender \
  --background \
  --python /Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Cupcake/scripts/build_cupcake_asset.py
```

### 5. 验证与资源

- 总计15666顶点、16576面、31144三角形。
- 7个网格边界边0、非流形边0。
- GLB/FBX回读均为7网格、5材质、`70 x 70 x 75 mm`。
- Blender：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Cupcake/source/LavenderChibiCupcake_70x75mm.blend`
- GLB：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Cupcake/exports/LavenderChibiCupcake_70x75mm.glb`
- FBX：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Cupcake/exports/LavenderChibiCupcake_70x75mm.fbx`
- 报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Cupcake/reports/cupcake_model_report.json`

## Phase Lavender Props 技术交接：淡紫底色统一

### 1. 统一色彩约定

碗、马克杯和纸杯蛋糕纸托的基础底色统一为：

```text
sRGB Base Color = (214, 190, 236)
Hex = #D6BEEC
```

该颜色不是白色材质上的展示灯光Tint，而是直接写入Base Color贴图，因此
Blender、GLB、FBX和Unity/PICO运行时均会保持一致。釉面高光允许接近白色，
但中间调与阴影必须保持可读的淡紫色。

### 2. 修改位置

- 共用陶瓷底色：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Bowl/scripts/prepare_bowl_textures.py`
  的`ceramic_background()`。
- 碗内壁提亮目标：同文件`build_interior()`，目标色
  `RGB(226, 207, 244)`，中心混合强度0.16。
- 马克杯内壁提亮目标：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Mug/scripts/prepare_mug_textures.py`
  的`build_interior()`，中心混合强度0.14。
- 马克杯无贴图把手材质在`build_mug_asset.py`中使用更浓的线性薰衣草色
  `(0.67, 0.52, 0.82)`，避免把手与杯身出现色差。
- 纸托底色：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Cupcake/scripts/prepare_cupcake_textures.py`
  的`build_liner()`。

马克杯外壁、内壁和卷边贴图依赖碗的公共图像函数，因此重建顺序必须是：

1. `prepare_bowl_textures.py`
2. `prepare_mug_textures.py`
3. `prepare_cupcake_textures.py`
4. 分别运行三套`build_*_asset.py`

### 3. 验证约定

- 禁止仅调节Blender世界光或材质Viewport颜色来模拟紫色；必须检查导出的
  PNG Base Color。
- 色彩调整后必须重新导出GLB/FBX，因为GLB嵌入贴图，FBX也使用嵌入纹理。
- 三套资产的尺寸和拓扑门禁不变：碗`138×138×70 mm`、马克杯高95mm、
  纸杯蛋糕`70×70×75 mm`，所有源网格保持闭合流形。
- 最终哈希记录在
  `/Users/bytedance/MateSoul/项目沉淀资料/work_reports.md`
  的“Phase Lavender Props 色彩修正”章节。

## Phase Wardrobe v006 技术交接：浅U衣领与开放颈部覆盖

### 1. 根因模型

v005的 `Review_NeckExtension_v002` 是为旧高领口修复颈部断层而创建的
闭合五行管状网格。其底部有19个封口三角，最后两行使用与衣领不同的
Neck/Chest权重。Yummy抬头阶段，封口和下层侧壁会旋转到衣领及衬衫前方。

预修复运行时证据：

- 173帧中68帧发生衣领/NeckExtension相交。
- 第107帧峰值176对相交三角；第130帧105对。
- 底盖19个三角在173帧中始终存在朝向相机的部分。
- SkinCoverage隔离不改变衣领缺陷；禁用NeckExtension后缺陷消失。

原 `collar_geometry.json` 只覆盖通用73姿态，没有覆盖Yummy的特定头部运动，
因此不能作为“完整动画无穿模”的单独证据。

### 2. v006资产链路

构建脚本：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v006/scripts/build_shallow_u_collar.py`

输入保持为只读v004源文件，输出：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v006/source/Lengyue_ShallowUCollarDress_v006.blend`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v006/exports/Lengyue_ShallowUCollarDress_v006.fbx`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v006/exports/Lengyue_CollarNeckExtension_v006.fbx`
- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Models/Lengyue_ShallowUCollarDress.fbx`
- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Models/Lengyue_CollarNeckExtension.fbx`

完整重建：

```bash
/Applications/Blender.app/Contents/MacOS/Blender \
  --background \
  /Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v004/source/Lengyue_LayeredDress_v004.blend \
  --python /Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v006/scripts/build_shallow_u_collar.py
```

随后在Unity执行 `MateSoul/Lengyue/Build Production Character`。

### 3. 衣领几何

- 内开口半径：X=43mm、前后=45mm，宽度86mm。
- 相对原始约108mm领口仍收窄20.37%。
- 内缘高度使用平滑余弦圆弧，前中部比v005下降6.7mm；中心切线水平，
  不形成V领尖角。
- 内缘可见波瓣振幅为0；18折褶只保留在领片内部。
- 外缘向衬衫内重叠约3mm，避免形成新的覆盖缝隙。
- 衣领继续使用 `ValidationPart=6` 和Atlas tile 7粉紫色。

### 4. NeckExtension

- 新网格为56顶点、84个开放侧壁三角，无底盖。
- 顶部三行保持旧Face边界位置、UV与蒙皮；新底边在衣领内侧收进3mm。
- 底边权重为55% Chest / 45% Neck，与衣领内缘一致。
- `LengyueLayeredDressBuilder`在导入后重绑116骨骼，并将14个顶边法线
  匹配到Face BakeMesh。
- 专用材质：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Materials/Lengyue_Toon_NeckExtension.mat`
  使用同一Face贴图，`_UnlitBlend=1`，BaseColor为
  `(1.03, 1.12, 1.165, 1)`；静态边界实测RGB差低于1。
- 生成Mesh：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Meshes/Lengyue_LavenderCasual_NeckExtension_Open.asset`

Unity生成Mesh必须删除旧`.asset`后重新创建。对不同顶点数的Mesh使用
`EditorUtility.CopySerialized`会保留旧顶点缓冲，只替换部分数据。

### 5. 验证门禁

- `unity_build.json`：要求56个颈部顶点、84个侧壁三角、14个Face法线匹配、
  Neck专用材质、袖子Renderer重绑及10623个v10驱动顶点。
- `collar_geometry.json`：73姿态衣领对NeckExtension和Face均为0相交。
- `existing_garment_regression.json`：Part 0–5对v004最大坐标差0m。
- `collar-coverage-opening`网络插桩：Standalone 173帧逐帧上传衣领、
  NeckExtension与Face BakeMesh，并离线执行BVH三角相交检查。
- 最终Standalone结果：0/173相交，最小间隙1.555mm，运行/渲染错误0。

调试会话当前保持OPEN。用户确认前不得删除：

- `/Users/bytedance/MateSoul/debug-collar-coverage-opening.md`
- `/Users/bytedance/MateSoul/.dbg/trae-debug-log-collar-coverage-opening.ndjson`
- `/Users/bytedance/MateSoul/.dbg/collar-coverage-opening.env`
- `LengyueYummyCaptureProbe.cs`内 `debug-point collar-*` 插桩。

## Phase Wardrobe v10 回滚技术交接：无衣领生产基线

### 1. 当前生产状态

v006章节仅记录历史实现，不再代表当前生产配置。当前唯一生产角色恢复为
加衣领前的v10 ShadowFabric状态：

- 服装模型：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Models/Lengyue_LayeredDress.fbx`
- 生产Prefab：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Prefabs/Lengyue_GameCharacter.prefab`
- 颈部Mesh：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Meshes/Lengyue_LavenderCasual_NeckExtension.asset`
- 颈部材质：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Materials/Lengyue_Toon_Face.mat`

生产模型来自v004，无 `ValidationPart=6`。v10袖子求解器、ShadowFabric织物
材质和手臂阴影软化均保留。

### 2. Builder回滚门禁

`LengyueLayeredDressBuilder`现在固定加载
`Runtime/Models/Lengyue_LayeredDress.fbx`，并执行以下生产不变量：

1. 所有UV2的`ValidationPart`均不得为6。
2. 服装Mesh必须为64574顶点、113128三角形。
3. `LengyueSleeveCloth`驱动顶点必须严格为10623。
4. NeckExtension必须绑定原始70顶点Mesh和Face材质。
5. 替换服装Renderer后必须重新设置`LengyueSleeveCloth.target`。

构建结果写入：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v004/reports/unity_build.json`

当前`passed=true`，`noCollarVertices=true`，
`canonicalNeckRestored=true`，`sleeveTargetRebound=true`。

### 3. 运行时验证

`LengyueYummyCaptureProbe`不再包含衣领BakeMesh上传和候选Renderer隔离逻辑，
无衣领通过条件为：

```csharp
report.sleeveInitialized &&
report.collarVertices == 0 &&
report.sleeveDrivenVertices == 10623
```

最新macOS Standalone以1920×1080、30fps、4×MSAA和2×超采样完成173帧：

- `collarVertices=0`
- `sleeveDrivenVertices=10623`
- `sleeveSkinInfluences=2`
- `sleeveGravityDropM=0.0165139139`
- `sleeveMotionLagM=0.0198577121`
- `sleeveSettledErrorM=0.00000277979`
- `runtimeErrors=0`
- `meshRenderingErrors=0`
- `passed=true`

报告路径：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/unity_playmode.json`

### 4. 资产与调试生命周期

生产 `Runtime` 中已删除零引用的
`Lengyue_NarrowCollarDress.fbx`、`Lengyue_ShallowUCollarDress.fbx`、
`Lengyue_CollarNeckExtension.fbx`、
`Lengyue_LavenderCasual_NeckExtension_Open.asset`和
`Lengyue_Toon_NeckExtension.mat`及其`.meta`。

v005/v006仍保留在：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v005/`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v006/`

它们是历史源资产，不进入生产Prefab。`collar-coverage-opening`会话已关闭，
7782服务及衣领专用临时日志、环境文件和运行时插桩均已清理。

## Phase Yummy Cookie v001 技术交接：动作标定与道具生命周期

### 1. 隔离边界

运行时与编辑器代码、道具副本、材质、角色Prefab和场景全部位于：

`/Users/bytedance/MateSoul/MateSoul/Assets/Experiments/YummyCookie_v001/`

脚本和交付物位于：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/YummyCookie_v001/`

原角色、动画、材质、物理脚本和道具源模型均只读。`baseline_hashes.json`
记录153个原文件，用`scripts/package_demo.py`在交付前校验；删除实验Assets目录
及其`.meta`即可回滚。生产Prefab和EditorBuildSettings不引用本实验。

### 2. 核心实现

- `Editor/YummyCookieBuilder.cs`的`Analyze()`逐帧采样手指/手掌/Head骨骼；
  嘴部标记由`face_O_BS`发生位移的顶点中心生成。`Build()`复制基线场景的
  v10配置，创建道具布局和独立Player，恢复构建前编辑器场景状态。
- `YummyCookieDemo.cs`使用同一帧时钟驱动`AnimationClip.SampleAnimation`和
  道具状态。它在头发、裙子、袖子的LateUpdate之前采样身体动画。
- `Reach`：演示帧0–23，固定原第0帧身体姿态，以手臂双关节CCD做65mm下探，
  并从原第30帧张开的手指局部旋转插值至捏取姿态。
- `Carry`：演示帧24–45，对应原第0–21帧；曲奇局部位置固定为
  `(.085, .010, -.032)m`，父级为`Hand_R`。盘中初始位姿从同一锚点反算，
  保证父级切换连续。没有创建替代手持对象。
- `Chew`：演示帧46起，对应原第22帧起；隐藏被取走的同一个对象。
  `ApplyFrame()`根据绝对时间重建状态，因此回退、循环和跳转不会漏触发。
- 入口阶段张嘴叠加只修改角色实例BlendShape权重，原Clip不增删曲线。

### 3. 资产与运行方式

`scripts/prepare_props.py`从原曲奇Blend只读加载，将材质烘焙到1024×1024
颜色/法线图并导出FBX。Unity合并子网格、居中到曲奇厚度中心，保持
`50 × 49.953 × 11 mm`；盘子保持直径200mm。

盘内为中心1块、两圈各6块、顶部1块。环心半径52mm，曲奇外缘约77mm，
避免进入盘口升高区域。固定桌面位置由初始手掌抓取位姿决定。

打开实验场景即可播放；`Space`暂停动作时间、`R`重播。独立Player参数
`--cookie-capture`输出197帧1080p截图（4×MSAA、2×超采样）及
`reports/replay.json`，结束后自动退出；不依赖原Yummy探针的网络调试服务。

### 4. 验证与交接约定

- 最终构建及运行错误均为0；197帧通过，袖子驱动顶点10623。
- 抓取跳变`1.49e-8m`，携带阶段手掌锚点误差0。
- 盘中真实MeshFilter实例由14减为13；入口后目标隐藏。
- `rewindRestoresServing=true`、`repeatedBiteHidesCookie=true`。
- 嘴部接触使用曲奇圆柱和嘴形中心近似，须结合渲染检查；不可作为精确网格
  碰撞证明或其他动作的通用识别结果。
- Unity初始化为未命名场景时，`RestoreSceneManagerSetup`可能无有效活动场景；
  应保留原Scene句柄并恢复活动状态。
- 修改C#后先发送`.dbg/yummy-cookie-v001/refresh`，等待程序集重载完成，
  再发送`build`，否则可能出现编辑器/Player字段布局不一致。
- `scripts/package_demo.py`封装H.264 Main L4.0/yuv420p视频、做完整解码，
  并输出`reports/delivery.json`包含源文件隔离校验及视频信息。

## Phase Lavender Props 二次色彩验收技术补充

- 最终共享Base Color为sRGB `#D6BEEC`，不再使用过浅的`#E6D8F2`。
- 内壁仅做轻量径向提亮，不能混向纯白；碗与马克杯中心混合强度上限分别为
  0.16和0.14。
- 马克杯把手没有Base Color贴图，必须在
  `build_mug_asset.py::handle_glaze_material()`中同步维护颜色。
- 修改共用陶瓷底色后必须按“碗贴图→马克杯贴图→各自模型”的顺序重建，
  因为马克杯会复制碗的角色、底足和微法线资源。
- 视觉验收同时检查PNG贴图与最终棚拍，避免只看源纹理而忽略AgX与釉面高光
  对饱和度的稀释。

## Phase Lavender Chibi Cutlery 技术交接：参数化餐具资产

### 1. 阶段概述

本阶段新增勺子与筷子两套参数化Blender资产，延续Bowl、Mug、Plate的
薰衣草陶瓷视觉规范。每套资产均由纹理脚本和场景构建脚本完整重建，不依赖
手工编辑后的隐式状态。

### 2. 勺子结构

- 构建脚本：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Spoon/scripts/build_spoon_asset.py`
- `WIDTH_PROFILE`描述正视轮廓，`half_width()`使用椭圆端帽与Catmull-Rom
  插值连接勺头、窄颈和宽柄。
- `surface_z()`生成凹面勺头、等厚背面和柄部轻微拱度，生成后统一标定到
  `25 x 140 x 12 mm`。
- 网格由正面、背面、左右侧壁和勺头底端封口组成，最终为单一闭合流形。

### 3. 筷子结构

- 构建脚本：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Chopsticks/scripts/build_chopsticks_asset.py`
- `cross_section_half()`控制7mm圆角方截面向夹持端渐细，并通过四个高斯凹槽
  生成实体防滑纹。
- 横截面采用24段超椭圆；上端以半椭圆收口，下端以三角扇封口。
- 左右筷子使用独立Mesh数据，单支均为`7 x 230 x 7 mm`，源场景中心距9mm。

### 4. 纹理与UV

1. 两套`prepare_*_textures.py`动态载入
   `Props/Bowl/scripts/prepare_bowl_textures.py`，复用陶瓷底纹、图像编码和
   微法线生成能力。
2. 爱心与四角星使用带描边的隐式曲线栅格化，波浪和圆点由解析方程绘制。
3. 勺子和筷子的纹理长宽比与实体长宽比不同，因此绘制阶段分别应用物理比例
   补偿，禁止直接使用等像素宽高，否则图案会在模型上纵向拉长。
4. 正面/宽面使用装饰材质，背面和窄侧面使用素色材质；`MicroUV`独立平铺
   `ceramic_micro_normal.png`。

### 5. 构建与验证

完整重建顺序：

```bash
/Applications/Blender.app/Contents/MacOS/Blender --background \
  --python ArtSource/SapphiArt/Props/Spoon/scripts/prepare_spoon_textures.py
/Applications/Blender.app/Contents/MacOS/Blender --background \
  --python ArtSource/SapphiArt/Props/Spoon/scripts/build_spoon_asset.py
/Applications/Blender.app/Contents/MacOS/Blender --background \
  --python ArtSource/SapphiArt/Props/Chopsticks/scripts/prepare_chopsticks_textures.py
/Applications/Blender.app/Contents/MacOS/Blender --background \
  --python ArtSource/SapphiArt/Props/Chopsticks/scripts/build_chopsticks_asset.py
```

构建脚本会保存Blend、渲染四视图、导出GLB/FBX，再重新导入两种格式验证
包围盒、网格数和材质数。当前报告中尺寸检查及闭合流形检查均为`true`。

### 6. 资源路径

- 勺子：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Spoon/`
- 筷子：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Chopsticks/`
- 共用风格来源：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Bowl/`

## Phase Lavender Chibi Chopsticks v2 技术交接：纹样对比度

筷子纹样颜色定义位于：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Chopsticks/scripts/prepare_chopsticks_textures.py`

本次将爱心和星形外描边调整为深紫色，填充调整为高饱和玫粉色，并扩大外描边
比例。波浪线厚度由4像素提高到7像素，点状装饰同步扩大。修改只影响
`chopsticks_decorated_basecolor.png`及其内嵌导出材质，不改变模型几何、
UV、尺寸或碰撞建议。

## Phase Lavender Chibi Furniture 技术交接：参数化家具资产

### 1. 共用模块

家具共用逻辑位于：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/FurnitureCommon/furniture_common.py`

核心接口：

- `add_rounded_box()`：生成尺寸稳定的圆角桌面或座面，并按面向分配顶面纹理。
- `create_tapered_member()`：沿任意两点生成带端盖的渐细圆杆，可分配粉色脚套。
- `create_scalloped_apron()`：生成正面装饰、背面素色的闭合荷叶边围板。
- `export_assets()`和`validate_export()`：导出GLB/FBX并重新导入校验。

### 2. 桌子结构

桌子构建脚本：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Table/scripts/build_table_asset.py`

桌面决定最终`600 x 400 x 750 mm`包围盒。四根腿以不同上下端点产生外张角度，
创建后按顶点最小Z值整体抬升，使所有脚套准确接触`Z=0`。前后桌裙为5瓣，
左右桌裙为3瓣，均为独立闭合网格。

### 3. 椅子结构

椅子构建脚本：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Chair/scripts/build_chair_asset.py`

后立柱从地面连续延伸至球形柱头。椅背外轮廓使用超椭圆基础形状叠加双波峰顶部
和三瓣底边；爱心孔为独立内轮廓。正反面通过
`mathutils.geometry.tessellate_polygon`执行带孔三角化，再分别封闭外侧壁和
爱心内壁。禁止恢复布尔镂空或按索引直接连接内外轮廓：

- 布尔方案曾产生16条非流形边。
- 直接环连接会形成自交四边形和放射状法线瑕疵。

### 4. 纹理与验证

桌椅纹理脚本均复用`Props/Bowl/scripts/prepare_bowl_textures.py`，输出独立
桌面、围板、椅背和素色贴图。椅背正面关闭微法线，避免带孔三角化区域的切线
分片；其余部件继续使用`ceramic_micro_normal.png`。

当前自动报告确认：

- 桌子：`600 x 400 x 750 mm`，9个闭合网格，9980三角形。
- 椅子：`360 x 380 x 680 mm`，13个闭合网格，13248三角形。
- 两套GLB/FBX回读尺寸均匹配，边界边和非流形边均为0。

## Phase 美术资产展示包技术交接

### 1. 交付结构

展示包根目录：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Submission/Lengyue_ArtAssets_Showcase_v001/`

目录职责：

- `01_Preview/`：总览图、7个道具多视图和角色真实运行视频。
- `02_GLTF/`：7个道具GLB，作为首选交互交换格式。
- `03_FBX_and_Textures/`：6个道具FBX及同级贴图兜底。
- `04_Character/`：角色骨骼FBX、贴图、参考图和Unity视频。
- `SHA256SUMS.txt`：包内文件校验。

### 2. 白模规避策略

道具评审优先使用GLB。碗、马克杯、盘子、勺子、筷子和纸杯蛋糕的GLB均将
3–5张贴图写入二进制`bufferView`，没有外部图片URI。曲奇使用6套内嵌PBR
材质参数，不依赖外部文件。

FBX不作为单文件预览格式。必须保持以下结构：

```text
AssetName/
├── Model.fbx
└── Textures/
    ├── *_basecolor.png
    └── *_normal.png
```

角色FBX已嵌入贴图，但不同DCC对媒体解包支持不同，因此仍必须保留
`04_Character/Textures/`。角色最终外观以Unity视频为评审基准。

### 3. 提交流程

1. 将`01_Preview/ArtAssets_Overview.png`放在飞书文档或评审页第一屏。
2. 内嵌角色最终Unity MP4。
3. 上传完整ZIP作为模型附件。
4. 正文注明“道具GLB已内嵌贴图；角色FBX需与Textures一起解压”。
5. 接收方导入后必须使用材质预览或渲染预览，不能在实体模式下判断贴图。

### 4. 验证

- 总览图：PNG RGBA，1920×960。
- 角色视频：H.264、1920×1080、30 FPS、yuv420p。
- ZIP：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Submission/Lengyue_ArtAssets_Showcase_v001.zip`
- ZIP大小79849117字节，SHA-256：
  `e9744a6928c65c92e294b554208d40d6f3257d4039cae94b20fdeed195f5494b`。
- `unzip -t`检查无错误。

## Phase 美术资产展示包V002技术纠正

### 1. 有效版本

V001交付已撤销。唯一有效目录与压缩包为：

```text
/Users/bytedance/MateSoul/ArtSource/SapphiArt/Submission/
├── Lengyue_ArtAssets_Showcase_v002/
└── Lengyue_ArtAssets_Showcase_v002.zip
```

最新角色的权威来源：

```text
DCC:
ArtSource/SapphiArt/Wardrobe/v006/source/
  Lengyue_ShallowUCollarDress_v006.blend

Unity:
MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Prefabs/
  Lengyue_GameCharacter.prefab

Video:
ArtSource/SapphiArt/Animation/YummyCookie_v001/render/
  Lengyue_Yummy_CookieEating_v001.mp4
```

### 2. Unity依赖闭包

生产角色不是单一FBX，而是Prefab组装资产。打包时从
`Lengyue_GameCharacter.prefab`递归读取所有32位GUID，在整个Unity
`Assets/`中解析对应`.meta`，最终得到32项依赖：

- Prefab、当前服装/脸/眼睛/牙齿/头发网格
- 5套Toon材质、7张贴图和自定义Toon Shader
- `HeadPoseGuard`、`SkirtPoseGuard`、`LengyueSleeveCloth`
- 外部当前依赖`HairSpringPhysics.cs`
- Idle、Walk、Run和Animator Controller

闭包清单位于：

`04_Character_Latest/UnityProject/DEPENDENCY_CLOSURE.txt`

所有依赖均保留原始`Assets/...`路径和`.meta`，递归复核结果为32/32，
未解析GUID为0。未被生产Prefab引用的旧模型和Yummy旧资源不进入闭包。

### 3. 静态角色GLB

生成脚本：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Submission/scripts/build_latest_character_preview.py`

流程：

1. 打开v006 Blend。
2. 只选当前可见网格，并断言存在浅U领和当前头发、不存在旧头发。
3. 将带形态键和双骨架的可见网格烘焙为静态副本。
4. 显式映射服装、脸、眼睛和头发4套PBR贴图材质。
5. 导出无骨架GLB。
6. 清空Blender后重新导入GLB，检查网格/材质/图片并渲染预览。

最终门禁为50个回读网格、4套材质、4张内嵌图片、0个外部图片URI。
该GLB仅供外观旋转查看，生产运行必须使用Unity Prefab。

### 4. 版本一致性证明

`YummyCookie_v001/reports/baseline_hashes.json`记录了生成视频时使用的生产
Prefab和服装网格哈希。两者均与v002包内文件一致：

```text
Prefab  4daa9e370e831814c2bc03b0c4e0c582eb3e93a7ee85677dfd43dfe9bede6d32
Garment e4bc85598f158944987ef78d3ba4a14b8a99630c353f4de882a0e214f7894d2f
Video   1d2333b70b038c35e99fdf386962f51628a08e282683cd4a3221cd6e9bca03e8
```

### 5. 最终包

- 路径：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Submission/Lengyue_ArtAssets_Showcase_v002.zip`
- 大小：103745487字节
- SHA-256：
  `c0fe2fc8482de5bebd2391ab92325ced59c05a722562816cdbd442e61ae9a312`
- `unzip -t`：通过
- 禁止旧文件扫描：通过

## Phase PlushDoll 技术交接：静态毛绒玩偶（2026-09-29）

资产根目录：
`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/PlushDoll/`。

### 1. 构建

- `scripts/prepare_plush_textures.py`：Python/Pillow/NumPy生成布料底色、
  周期微法线与2048px刺绣五官。布面使用物理尺度MicroUV，
  五官使用头部X/Z平面投影UV，避免独立眼片悬空。
- `scripts/build_plush_doll_asset.py`：Blender 5.2.1参数化闭合几何。
  `hair_panels()`生成19块沿头部外形成形的布片，`shell()`负责双面与边缘闭合；
  `tube()`以显式共用顶点的三角扇封口生成线迹。
- `straps()`通过服装BVH射线确定布带接触点；布扣根据服装采样点拟合外侧支撑
  平面，保持圆面连续，避免逐点跟随相交衣层形成折面。
- `add_pile()`将固定短绒建为闭合四面体，包含在三种格式中；未使用仅Blender
  可见的毛发系统。`normalize()`对真实顶点等比缩放，仅约束总高300mm。
- 布扣、缝线和发片为可编辑独立对象。Cycles材质使用布面Sheen与微法线，
  GLB/FBX接收引擎需自行匹配着色，不保证与Cycles像素一致。

### 2. 独立验收

`scripts/validate_plush_delivery.py`重新打开磁盘上的Blend，并清空场景分别导入
GLB与FBX。检查真实顶点包围盒、面数、贴图可读取性、闭合边、相邻面朝向、
退化面及正体积。导出格式在UV/法线接缝处分裂顶点，验证时在内存副本以
1e-9米阈值合并同位置顶点后检查，不修改交付文件。

最终为82网格、170,630三角形、8张被引用图片；三种格式全部通过。
高度300mm，宽211.5878mm，深201.4783mm。独立结果和SHA-256写入
`reports/plush_doll_model_report.json`。

### 3. 使用边界

- 参考图没有尺寸，300mm是假定成品尺度；宽、深是建模实测结果。
- 无骨骼、布料模拟或Unity/PICO运行时验收；不是融合成一个壳体的3D打印模型。
- 关闭三个`Short_Pile_*`对象可移除62,000个短绒三角面。
- 完整重建命令见该目录`README.md`。`-- --preview`只更新Blend/预览，
  正式交付必须随后运行完整构建和独立验收。

## Phase PlushDoll Face V2 技术交接：单色眼球（2026-09-29）

### 1. 纹理规则

- `scripts/prepare_plush_textures.py`中的`face()`只在眼眶内部绘制统一RGB
  `(111, 58, 158)`紫色，不再绘制眼白、瞳孔、渐变或高光。
- 深色外椭圆负责眼眶，上弧线负责上眼线；三根外侧线迹、眉线继续保留。
- 嘴部线迹不再生成。眼球仅包含模拟刺绣线方向的微观明暗，不包含图形层级。

### 2. 交付与验证

- 更新源文件：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/PlushDoll/source/Lengyue_PlushDoll_300mm.blend`
- 更新GLB、FBX、四视图与三视图合成图；完整结果仍写入
  `reports/plush_doll_model_report.json`。
- 独立回读结果：82网格、170,630三角形、8张贴图；尺寸、面数、闭合拓扑、
  相邻面朝向、正体积和退化面检查在Blend/GLB/FBX中全部通过。

## Phase GLB Collection 技术交接：GLB与贴图合集（2026-09-29）

### 1. 目录与清单

- 合集路径：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/GLB_and_Textures/`
- 所有GLB统一放在`Models/`；实际依赖图片放在`Textures/<模型名>/`。
- `manifest.json`记录扫描范围、18个源GLB、11个独立模型、7个重复副本、
  43张贴图，以及所有交付文件的SHA-256。

### 2. 收集逻辑

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/tools/collect_glb_assets.py`
  扫描项目GLB并排除构建、缓存和生成目录。
- 使用完整文件SHA-256去重；同内容模型优先选择`Props/*/exports/`作为来源，
  角色模型使用提交包中的`Lengyue_CurrentAppearance_v006.glb`。
- 脚本解析GLB 2.0 JSON/BIN块，按`images[].bufferView`提取真正被模型引用的
  图片，因此不会混入预览图、参考图和验证证据。
- GLB已经内嵌贴图，独立`Textures/`用于人工检查、修改或不支持内嵌资源的引擎。
  曲奇不含图片资源，材质由GLB中的常量参数定义。

### 3. 验证约定

- 重建合集会删除并重新生成`GLB_and_Textures/`，但不会修改任何源模型或源贴图。
- 交付前必须检查模型复制哈希、GLB 2.0文件头、提取贴图字节哈希和图片签名。
- 当前验证结果：11个GLB、43张贴图、约64MB，全部通过。

## Phase CharacterPresentation v004 技术交接：参考图姿势扩展（2026-09-29）

### 1. 资源边界

- 只读角色源：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v004/source/Lengyue_LayeredDress_v004.blend`
- 输出目录：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/CharacterPresentation/v004/`
- `v003`展示图及角色源Blend未被覆盖。

### 2. 生成流程

1. `render_reference_poses.py`清除临时动画并重置116根姿势骨骼。
2. 通过世界空间旋转组合生成T字、扭转、侧弯、前倾、宽站、挥手和斜举。
3. 使用`Wardrobe_Front`相机、1200×1200、Cycles 24采样和透明背景输出单图。
4. `compose_pose_sheet.py`复用5张已批准姿势图，将7张新增渲染铺到统一
   RGB `(93, 95, 103)`背景。
5. 最终生成12格中文标签展示图，并在`presentation_report.json`记录来源、
   保留/新增/排除列表及SHA-256。

### 3. 使用约束

- `POSE_ONLY=<slug>`可用于单独重渲染一个新姿势；完整运行会刷新
  `render_report.json`。
- 合成脚本依赖Pillow；渲染脚本必须由Blender打开指定源Blend后执行。
- 最终展示图：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/CharacterPresentation/v004/Lengyue_CommonPoses_Render.png`

## Phase GLB Collection Layout V2 技术交接（2026-09-29）

### 1. 最终结构

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/GLB_and_Textures/Models/`
  集中存放11个去重后的GLB。
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/GLB_and_Textures/Textures/`
  按模型名划分子目录，保存43张从GLB提取的实际图片。
- 模型目录保持扁平；贴图保留模型作用域，防止多个资产的同名贴图覆盖。

### 2. 生成与验证

- `collect_glb_assets.py`每次重建时同步刷新`Models/`、`Textures/`和
  `manifest.json`，不再生成旧版“每模型一个顶层目录”的结构。
- 验证要求：所有`.glb`的直接父目录必须是`Models/`；所有贴图路径必须匹配
  `Textures/<manifest资产名>/`；模型和贴图SHA-256必须与清单一致。
- 当前11个模型、43张贴图全部通过结构和哈希检查。

## Phase Retro Electronics 技术交接（2026-09-29）

### 1. 资产与结构

- 电视机：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Television/`
  尺寸`480 × 450 × 420mm`，32个闭合网格。
- 遥控器：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/RemoteControl/`
  尺寸`50 × 18 × 140mm`，47个闭合网格。
- 共用代码：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/ElectronicsCommon/`

### 2. 核心实现

- `electronics_common.py`提供细纹塑料/CRT屏幕材质、闭合圆柱/球体/圆环、杆件、
  心形挤出和实体线条图标生成。遥控器图标不依赖字体，避免字体转网格产生开放边。
- `prepare_electronics_textures.py`确定性生成`plastic_micro_normal.png`和
  `screen_basecolor.png`；随机种子固定，可重复构建。
- 电视屏幕在圆角基础网格上执行边缘保持的4次幂外凸位移，得到真实CRT曲面；
  内框、屏幕和前面板按亚毫米深度分层，避免遮挡和Z-fighting。
- 遥控器前后图标均被约束在`-9mm`到`+9mm`边界内，使按键凸起后总厚度仍严格为
  18mm。

### 3. 验证与重建

- `validate_electronics_delivery.py`分别重新打开Blend、导入GLB和FBX，焊接UV/法线
  接缝后检查尺寸、三角形数量、贴图数量、边界边、非流形边、法向连续性和退化面。
- 当前结果：电视35,440三角形/2贴图，遥控器10,116三角形/1贴图，三格式全部一致。
- `package_views.py`将正视、侧视、俯视或背视渲染合成为统一三视图交付图。
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/GLB_and_Textures/`已包含电视和
  遥控器，当前总计13个独立GLB和46张提取贴图。

## Phase Retro Electronics Reference V2 技术交接（2026-09-29）

### 1. 替代关系与尺寸

- 用户反馈初版偏离参考，本版替代上一节的构建实现、尺寸解释与验收结论。
- 电视源文件名保留 `LavenderRetroTV_480x450x420mm`；420mm指机身含脚，
  实际整体包围尺寸为480×450×565.7mm。根对象的 `dimension_note`、
  README及JSON报告均记录这一解释。
- 遥控器整体含按键为50×18×140mm。
- 最终统计：电视36网格、112,306三角形、2图片；遥控器32网格、
  32,344三角形、1图片。此统计对应最新源文件和两个导出格式。

### 2. 构建模块和流程

共用代码位于
`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/ElectronicsCommon/`：

1. `prepare_reference_atlas.py`生成2048²基础色/丝印图集和独立屏幕基础色；
   材质通过 Principled BSDF 读取sRGB图片，Blend打包、GLB内嵌、FBX嵌入。
2. `reference_geometry.py`以毫米输入、米制顶点输出；`loft()`沿深度连接截面，
   `plate()`独立定义正视圆角和厚度，`ring_button()`保留真实中心孔，
   `crt()`使用中央顶点和24圈径向网格生成鼓面。
3. `build_reference_electronics.py`组织两套资产。电视前框为闭合环形截面，
   散热槽和遥控器电池盖凹槽使用布尔切除；粉色遥控器侧边使用主壳UV分区，
   禁止再叠加共面外皮。
4. `finish()`在源网格上以1e-9m焊接重复顶点，再三角化、按40°设锐边。
   按钮平面盖面保持平法线，避免半球状假高光。
5. Cycles渲染四个视角并保存Blend；复用FurnitureCommon的选择导出，排除棚灯、
   地面和相机；两个旧资产构建脚本仅保留新版兼容入口。
6. `validate_electronics_delivery.py`独立回读三个格式；`package_views.py`
   使用中文标签合成正式三视图，再运行合集脚本刷新交付。

完整命令记录于：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Television/README.md`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/RemoteControl/README.md`

### 3. 验收和框架避坑

- 包围范围使用 `matrix_world @ vertex.co`，避免旋转对象包围盒高估尺寸。
- 导入器会按UV/法线拆分顶点，拓扑检查前可以焊接接缝，但必须先记录
  `raw_triangles`、`raw_degenerate_faces`。`collapsed_faces_after_weld`
  必须为0，防止“验收时修好坏面”而源文件仍含退化三角形。
- 最新三个格式的原始退化面、焊接后坍塌面、非流形边、边界边、相邻面朝向
  不一致均为0，三角形数与构建报告一致。
- 源Blend执行首命中射线：电视屏幕中心/边缘、电源与散热槽内壁；
  遥控器OK、方向环、电源、菜单、电池盖。此检查针对源场景，
  不能表述成三个导出格式都做过可见面射线。
- Blender 5.2 使用 `scene.compositing_node_group`。Alpha Over必须连接
  `Background`、`Foreground`命名输入，旧数字索引会把渲染图接到Factor。
- 不用材质不断加深来掩盖过曝；先校正棚灯能量和AgX曝光，再判断参考色彩。
- 旧版`electronics_common.py`和`prepare_electronics_textures.py`不参与当前构建。
  旧`plastic_micro_normal.png`未被本版材质引用，也不会进入合集。

### 4. 交付与恢复

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Television/`
  与`RemoteControl/`保留源文件、两种导出、实际贴图、最终渲染和独立验收JSON。
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/GLB_and_Textures/`
  维持`Models/`平铺模型、`Textures/<资产名>/`分放图片；
  13个模型、46张图，已按源文件、GLB内嵌字节和manifest交叉校验。
- 偏差分析：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/ElectronicsCommon/reports/reference_deviations.md`。
- 回退存档：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/ElectronicsCommon/reports/pre_reference_rebuild_v1.zip`。
  保持ZIP形式，避免旧GLB被合集脚本再次扫描收录。
- 本轮没有修改应用代码、角色或其他道具源资产；当前尚未验证Unity/PICO运行效果。

## Phase Lengyue Wardrobe Foundation 技术交接（2026-09-29）

### 1. 架构与职责

- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Scripts/LengyueOutfitDefinition.cs`
  定义服装数据契约。一个定义包含多个SkinnedMeshRenderer部件，部件角色包括
  Garment、SkinCoverage、NeckExtension、Shoes、Accessory和Other。
- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Scripts/LengyueWardrobeController.cs`
  持有唯一角色骨架、初始服装和服装目录；公开`TrySwitchOutfit`与
  `SwitchOutfit`，并在成功后发送`OutfitChanged`。
- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Editor/LengyueOutfitDefinitionBuilder.cs`
  从选中的服装根节点提取Mesh、材质、骨骼名、RootBone、Bounds、局部变换和
  Renderer配置，生成ScriptableObject定义。
- 生产Prefab已挂载控制器，`skeletonRoot`指向`BindJoints`，
  `outfitContainer`指向现有模型容器；`lavender-casual`初始套装由服装、
  皮肤遮挡层和颈部补片三个对象组成。

### 2. 原子切换流程

1. 校验服装ID、部件、SubMesh与材质数量、BindPose与骨骼名数量。
2. 在唯一`BindJoints`层级建立无重名骨骼索引，兼容源`Head`到生产`Head 1`
   的既有映射。
3. 在禁用的临时节点下创建全部Renderer并完成骨骼重绑定；任何缺骨立即终止。
4. 激活新节点，重建袖子运行时Mesh并按能力启停裙摆守卫。
5. 全部成功后才隐藏旧套装并更新当前ID；失败时删除临时节点、重新绑定旧布料，
   当前服装保持可见。
6. 切回初始服装时复用Prefab内原对象，不重复实例化。

### 3. 服装接入约定

1. 新服装必须使用当前116骨静止姿态与骨骼名，只导出服装相关Renderer。
2. 选中导入根节点，执行
   `MateSoul/Lengyue/Wardrobe/Create Definition From Selection`。
3. 将生成的`LengyueOutfitDefinition`加入生产Prefab控制器的`Outfits`数组。
4. 业务层调用`TrySwitchOutfit(outfitId, out error)`；失败信息必须记录，
   不得在失败后手工关闭当前服装。
5. 服装必须原子包含对应SkinCoverage、NeckExtension、鞋和附件，禁止只替换
   Garment造成肤色穿透或接缝暴露。
6. 只有保留UV2部件3/5元数据的服装可启用`enableSleeveCloth`；只有使用当前
   `Skirt_*`与`OuterSkirt_*`骨骼的服装可启用`enableSkirtPoseGuard`。

### 4. 验证和避坑

- 生产构建报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v004/reports/unity_build.json`，
  `wardrobeConfigured=true`且整体`passed=true`。
- 换装事务报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v004/reports/wardrobe_foundation.json`，
  覆盖定义生成、成功切换、骨骼重绑定、缺骨拒绝、失败回滚和初始服装恢复。
- `LengyueSleeveCloth`切换目标前必须恢复旧`sharedMesh`并销毁运行时Mesh；
  仅替换`target`会把旧粒子、BindPose和顶点缓存带入新服装。
- `SkirtPoseGuard`禁用前必须恢复静止旋转和位置；否则裤装切入后仍可能保留
  上一帧裙骨偏转。
- 当前未实现换装UI、玩家选择存档和资源异步加载；服装数量较少时可直接使用
  ScriptableObject目录，后续数量扩大再接Addressables，不提前引入资源层。

## Phase D0.1 技术交接：Room Capture-first 现实场景导航

### 1. 结论

Demo 采用“先获得稳定房间地图，再开始角色活动”的启动策略：

```text
PICO Room Capture
  -> Scene Anchors
  -> RoomMapSnapshot
  -> Runtime NavMesh
  -> GroundLocomotion
```

角色活动期间不持续以实时网格更新驱动全图重建。Spatial Mesh 降级为精细碰撞、
遮挡和扫描质量补偿，不作为首版导航的唯一事实源。

### 2. SDK 事实边界

`Scene Capture` 与 `Spatial Mesh` 是不同数据源：

- Scene Capture 由系统级 Room Capture 应用生成并保存房间语义数据。
- 应用可以查询 Scene Anchors，但不能修改系统 Scene Anchors。
- Floor 和 Ceiling 使用 Polygon。
- Wall、Door、Window、Opening、VirtualWall 使用 Box2D。
- Chair、Table、Sofa、Cabinet 等家具使用 Box3D。
- Spatial Mesh 返回实时三角形、变换和逐顶点语义，主要覆盖 HMD 周围约 5m。
- 完整大范围 Spatial Mesh 需要应用自行缓存，不能假设系统一次返回整间房精细网格。

当前 SDK 已提供：

```text
PXR_MixedReality.StartSenseDataProvider
PXR_MixedReality.QuerySceneAnchorAsync
PXR_MixedReality.StartSceneCaptureAsync
PXR_MixedReality.GetSceneSemanticLabel
PXR_MixedReality.GetScenePolygonData
PXR_MixedReality.GetSceneBox2DData
PXR_MixedReality.GetSceneBox3DData
PXR_MixedReality.LocateAnchor
PXR_MixedReality.StopSenseDataProvider
```

源码：

- `/Users/bytedance/MateSoul/XR/Runtime/Scripts/Features/PXR_MixedReality.cs`
- `/Users/bytedance/MateSoul/XR/Runtime/Scripts/SensePack/PXR_SceneCaptureManager.cs`
- `/Users/bytedance/MateSoul/XR/Runtime/Scripts/SensePack/PXR_SpatialMeshManager.cs`
- `/Users/bytedance/MateSoul/XR/Runtime/Scripts/SensePack/PXR_PlaneDetectionManager.cs`

### 3. 启动状态机

```text
CheckingExistingMap
  -> QuerySceneAnchorAsync
  -> NeedsCapture
  -> StartSceneCaptureAsync
  -> LoadingAnchors
  -> ValidatingRoom
  -> BuildingNavigation
  -> Ready
```

规则：

1. 启动 Scene Capture Provider。
2. 查询现有 Scene Anchors。
3. 已存在且通过质量门禁时直接构建导航。
4. 数据为空或质量不足时启动系统 Room Capture。
5. 扫描返回成功后整批重新查询 Scene Anchors。
6. 构建 `RoomMapSnapshot` 和 Runtime NavMesh。
7. 只有 `Ready` 状态允许角色生成随机移动任务。
8. 完成读取后停止不再需要的 Provider。

### 4. 数据模型

建议由 `PicoRoomCaptureProvider` 将 SDK 数据转换为稳定领域模型：

```text
RoomMapSnapshot
  roomId
  snapshotVersion
  floorPolygons
  walls
  openings
  furnitureBoxes
  anchorUUIDs
  capturedAt
  navigationBounds
```

业务层不得持有 PICO anchor handle。Handle 只在当前 Provider 生命周期有效，
持久化和跨会话引用使用 UUID；每次启动重新 Query 和 Locate。

### 5. 导航构建

推荐增加 Unity AI Navigation，由 `RuntimeNavMeshService` 负责：

1. 三角化主 Floor Polygon，生成可行走源。
2. Floor 边界限制角色不能离开扫描房间。
3. Wall 与 VirtualWall 生成薄碰撞体。
4. Chair、Table、Sofa、Cabinet 等生成 BoxCollider 和导航障碍。
5. 障碍体按角色半径加安全余量膨胀。
6. 一次性构建 Runtime NavMesh。
7. 随机点必须经过 `NavMesh.SamplePosition`。
8. 只接受 `NavMesh.CalculatePath` 的完整路径。

NavMesh 负责全局绕路；CharacterController、Physics.CapsuleCast 和地面 Raycast
负责最终位移与碰撞安全。不得只依赖 NavMesh，也不得只依赖 MeshCollider 滑动。

### 6. 房间质量门禁

最低准入条件：

- 存在包含玩家位置的主 Floor Polygon。
- Floor 面积满足角色活动半径。
- Floor Polygon 无自交、退化边或异常尺度。
- 存在 Wall 或 VirtualWall。
- 关键 Scene Anchors 均可 Locate。
- 构建后的 NavMesh 覆盖玩家附近区域。
- 至少一个候选随机点拥有完整路径。

不满足时返回 `NeedsCapture`，禁止退化为无边界随机移动。

### 7. Spatial Mesh 的保留用途

首版可在启动阶段短暂启用 Low LOD Spatial Mesh：

- 生成墙角和不规则表面的精细 MeshCollider。
- 校验 Scene Capture 代理与实际表面的偏差。
- 为现实遮挡提供几何。
- 记录当前房间网格快照。

如果运行中不需要持续更新，缓存结果后停止 Mesh Provider。若未来支持移动家具或扩展到
大空间，再增加低频更新、防抖重建和局部路径重规划。

### 8. 已知限制

- Scene Capture 是近似语义地图，不是完整高精度三角网格。
- 家具移动后旧 Scene Anchors 可能过期，需要重新扫描。
- Anchor 数量减少时不保证收到删除事件，重新扫描后应整批替换 Snapshot。
- Chair Box3D 只能作为障碍，不能直接作为精确坐姿点。
- 首版只支持单房间、单主地面，不支持楼梯和跨房间导航。
- 动态人物或宠物不能依靠静态 Scene Capture 处理。

### 9. 测试入口

测试分三层：

1. Unity PlayMode 使用固定 `RoomMapSnapshot` 测试导航和碰撞。
2. PICO Emulator 测试 Scene Capture、Spatial Mesh、Plane Detection 和 VST。
3. PICO 真机测试实际扫描质量、坐标稳定、碰撞误差、帧耗和房间变化。

关键日志：

```text
roomSnapshotId
anchorCount
semanticCounts
floorArea
navigationBounds
navMeshBuildMs
pathStatus
blockedCollider
replanReason
```

## Phase Pink Pajamas 技术交接：首套新增服装实装（2026-09-29）

### 资产与构建入口

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/PinkPajamas/v001/scripts/build_pajamas.py`
  从v004服装源读取116骨静止姿态，生成连通袖窿上衣、独立翻领/口袋/纽扣、
  连通裆部长裤、帽冠/帽边/绒球、手部遮挡层和配套室内鞋。
  原文件只读；棋格使用UV纹理，肩袖接缝分别投影以减少拉伸。
- 上衣下摆与裤子臀/大腿权重连续匹配，避免行走时裤腿穿过固定在Hips的衣摆。
  袖子从现有手臂表面转移权重，导出前限制4权重并归一化。
- 导出GLB/FBX共3个网格、144,760三角形；原颈部70顶点补片在Unity内引用，
  不以服装导出覆盖原接缝。`verify_exports.py`独立回读两种格式并检查骨数、
  蒙皮、权重、尺寸及跨格式顶点误差。
- Unity数据目录：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Wardrobe/PinkPajamas/`
  保存FBX、格纹贴图、规范化Mesh、Toon材质和`PinkPajamas.asset`。

### Unity接入与验证

1. `LengyuePajamasBuilder.Build`规范化FBX顶点流，按生产骨架重建BindPose，
   比较绑定前后BakeWorld顶点，偏差上限`2e-5m`。
2. 创建`pink-pajamas`定义，服装/手部/帽子/颈部共4个部件；
   `EnableSleeveCloth=false`、`EnableSkirtPoseGuard=false`。
3. 保留生产Prefab现有目录，加入睡衣引用；不复制角色，不修改Rest Pose。
4. `LengyuePajamasProbe`在PlayMode先检查切换前后Animator状态，
   再使用既有待机/行走/跑步/进食Clip确定性采样并录制330帧。
5. 往返切换16次，检查旧装显隐、骨架引用、帽子局部位置稳定性、
   袖子Mesh释放/恢复、裙摆启停、非法ID拒绝和残留节点。最终运行错误为0。
6. `package_presentation.py`生成中文展示图并从GLB提取配套PNG；
   FFmpeg合成11秒30FPS视频，验收后删除逐帧临时图。

### 已确认的管线陷阱

- **Unity生成Mesh不可直接CopySerialized替换**：顶点数/蒙皮布局变化后，
  旧GPU缓冲可能保留，BakeMesh正常但实际渲染报
  `data size and vertex stride`不一致。Builder重建生成Mesh资产并同步定义引用；
  材质与ScriptableObject仍原位更新。
- **Blender锁定选择导致漏导出**：复制旧鞋袜时需清除`hide_select`和
  `hide_viewport`后再加入合并选择；仅`select_set(True)`并不足够。
- **删除顶点权重时先快照索引**：不能遍历会变动的VertexGroupElement引用删除，
  必须先记录group索引，否则残余权重会导致归一化错误。
- **应用几何修改器后重新归一化**：副本合并前应用非Armature修改器，
  合并后再限制并归一化4权重。GLB回读时排除导入器创建的骨骼显示辅助球，
  不把它计为导出的第四个网格。
- 旧`LengyueSleeveCloth`笼体仅覆盖短袖`x=.125–.256m`，不能直接用于
  本次长袖。睡衣关闭该求解器，切回原装重新初始化。
- 测试场景重复运行会遇到Unity干净但未命名的场景残留；仅在确认其包含本测试
  的角色、相机、灯光且无未保存修改时替换，不覆盖用户其他场景。

### 交付边界

所有显示与动画验收均来自Unity PlayMode，并非PICO真机。
长袖/长裤/睡帽完成蒙皮和动作跟随；睡帽垂尾尚无独立物理摆动。
帽子顶点相对头部误差`2.6783e-7m`，导出和运行报告与文件哈希
位于主目录`reports/`。回退只需移除生产Prefab目录中的`pink-pajamas`引用。

## Phase D0.2 技术交接：PICO Emulator 房间数据能力实测

### 1. 实测环境

2026-09-30 使用 PICO Emulator `6.2.0` 创建并启动：

```text
AVD: MateSoul_PICO_0_13
Device: pico_spaceos_emulator_arm64
Android: 16 / API 36
Build: picosdk_spaceos_arm64-userdebug
ADB: emulator-5554
```

宿主机沙箱内直接启动会被 HVF 拒绝。当前通过
`scripts/start-pico-emulator.command` 交给 macOS LaunchServices 启动，
ADB 和虚拟化均正常；`scripts/pico-run.sh emulator doctor` 也确认模拟器包和
Android Studio 环境可用。

### 2. 虚拟房间与调试能力

模拟器包包含可用的虚拟客厅环境：

```text
.pico-home/6.2/emulator/resources/Toren1BD.obj
.pico-home/6.2/emulator/resources/Toren1BD.mtl
.pico-home/6.2/emulator/resources/Toren1BD_Main.png
.pico-home/6.2/emulator/resources/Toren1BD_Decor.png
```

OBJ 有 `16,253` 个顶点，边界约为
`9.650m × 3.000m × 11.563m`，对象包含沙发、餐桌、餐椅、书架、边桌、
电视和厨房等室内结构。模拟器控制协议定义了
`LIVING_ROOM_DAY`、`LIVING_ROOM_NIGHT`，调试面板包含
`Plane Detection`、`Spatial Mesh` 和 `Spatial Anchor` 开关。

该模型首先是模拟器的人工 VST/Virtual Scene 环境。它可以提供可移动的室内视觉
场景和 MR 调试输入，但不能因为 OBJ 存在就认定系统已经生成了 Room Capture
语义数据。

### 3. 扫描数据实测

新建 AVD 启动后的 `mrservice` 状态为：

```text
Map status: 0
Map uuid: <empty>
Session Anchor Counts: 0
```

同时：

- `/data/misc/pxr/maps` 为空。
- `com.pico.mrservice`、`com.pico.spatial.runtime`、
  `com.bytedance.pico.openmr` 和 `com.bytedance.pico.openmr.spatial`
  服务均已运行。
- Launcher 中没有可直接点击的独立 Room Capture 应用入口。
- Room Capture 应由测试应用通过 `StartSceneCaptureAsync` 触发。

因此当前结论是：

```text
有虚拟房间模型和 Spatial Mesh/Plane/Anchor 调试能力
!=
已有可查询的 Room Capture Scene Anchors
```

新 AVD 不带预生成 Floor、Wall、Door、Furniture 等 Scene Anchor 数据。

### 4. 对 Demo 的影响

模拟器可以用于：

1. 验证 Room Capture 启动状态机、权限、Provider 生命周期和空数据分支。
2. 验证 Spatial Mesh、Plane Detection、坐标转换和基础碰撞逻辑。
3. 在生成或注入有效数据后验证 `RoomMapSnapshot -> Runtime NavMesh`。
4. 使用固定 `RoomMapSnapshot` 回归导航，不依赖每次重新扫描。

模拟器当前不能直接替代：

- 真机首次房间扫描质量验收。
- 真实家具语义准确率和墙体边界误差。
- HMD 周围约 5m Spatial Mesh 的覆盖、抖动和性能。
- Room Capture 数据在真实房间变化后的失效与重扫行为。

下一步应构建最小 `RoomCaptureProbe` APK，开启
`sceneCapture`、`spatialMesh`、`planeDetection`、`spatialAnchor` 能力，
执行：

```text
StartSenseDataProvider
  -> QuerySceneAnchorAsync
  -> anchorCount == 0 时 StartSceneCaptureAsync
  -> 再次 QuerySceneAnchorAsync
  -> 输出 semanticCounts / polygon / Box2D / Box3D
```

如果模拟器 Room Capture 流程仍不能产出 Scene Anchors，则导航开发使用固定
`RoomMapSnapshot` 继续推进，并把真实扫描数据采集和地图质量验收保留为 PICO
真机准出项。

## Phase D1 技术交接：扫描快照驱动的房间随机行走

### 1. 阶段概述

已实现第一条可运行玩法闭环：

```text
PICO Scene Anchors / PICO MockSceneData
  -> RoomMapSnapshot
  -> Floor Polygon + Obstacle Boxes
  -> Runtime NavMesh
  -> Complete Path Selection
  -> Lengyue Random Walk
```

实现位于：

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/RoomNavigation/`

验证场景：

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/RoomNavigation/RoomNavigation_v001.unity`

生产角色 Prefab 未修改。场景实例在地图和 NavMesh 进入 `Ready` 后才激活并挂载
`RoomRandomWalker`。

### 2. 双数据源

`IRoomMapProvider` 隔离扫描数据来源：

- `PicoSceneCaptureRoomMapProvider`：真机启动 Scene Capture Provider，查询已有
  Scene Anchors；为空时调用 `StartSceneCaptureAsync`，再整批查询 Floor
  Polygon、Wall/VirtualWall Box2D 与家具 Box3D。
- `PicoMockRoomMapProvider`：编辑器和 PICO Emulator 读取 PICO SDK 自带
  `MockSceneData.json`。该文件只有四面 Wall 与 Table；Provider 使用墙端点
  Convex Hull 推导主地板 Polygon。

PICO Emulator 通过 `SystemInfo.deviceModel == swan_arm64` 识别，直接使用 Mock
Provider；不先等待模拟器当前无法完成的 Room Capture。

真机不允许回退到 Mock 数据。真实数据缺失或质量不合格时保持 `Failed`，角色不移动。

### 3. 核心类

- `RoomMapSnapshot`：保存 Floors、Obstacles、来源、时间与 Bounds，并检查面积、
  退化和自交。
- `RoomGeometry`：地板凸包、XZ 面积、耳切三角化、点在多边形内、障碍投影。
- `RuntimeRoomNavigation`：生成带厚度地板 Mesh，将墙和家具作为
  `Not Walkable` Box Source，一次性构建 Runtime NavMesh。
- `RoomRandomWalker`：仅接受 `NavMesh.CalculatePath` 的 `PathComplete` 目标；
  使用 `NavMeshAgent` 移动，并驱动现有 `param_idletowalk/param_toidle`。
- `RoomNavigationBootstrap`：负责 `LoadingRoom -> BuildingNavigation -> Ready`
  状态和 fail-closed 准入。
- `RoomNavigationValidationProbe`：逐帧检查地板边界、障碍侵入和 NavMesh 覆盖，
  输出 JSON 报告。

角色导航参数：

```text
agentRadius: 0.25m
agentHeight: 1.65m
agentClimb: 0.20m
voxelSize: 0.08m
obstacleSafetyMargin: 0.08m
```

### 4. 构建与验证入口

Unity 菜单：

```text
MateSoul/Room Navigation/Build Validation Scene
MateSoul/Room Navigation/Open Validation Scene
MateSoul/Room Navigation/Run Validation Scene
MateSoul/Room Navigation/Build Android Validation APK
```

验证报告：

`/Users/bytedance/MateSoul/项目沉淀资料/validation/room_navigation_v001.json`

Android APK：

`/Users/bytedance/MateSoul/Builds/RoomNavigation_v001.apk`

### 5. 已确认的框架问题

1. Unity 默认 NavMesh Agent 半径为 `0.5m`。PICO Mock 数据中的 Table 长
   `4.56m`，默认半径会侵蚀全部剩余通道；改为冷月胶囊半径 `0.25m` 后可保留
   两侧完整路径。
2. 零厚度地板不稳定，地板生成 `0.08m` 厚的闭合 Mesh 后再烘焙。
3. 不能把模拟器 Toren VST 模型当作 Scene Anchors。当前 Mock 扫描快照与
   Toren 可视客厅是两套数据，随机行走逻辑已验证，但二者的坐标/家具不保证重合。
4. 真机必须使用 Room Capture 数据，不能因为模拟器分支存在而静默降级。
5. PICO 空间能力已在 `PXR_ProjectSetting.asset` 中开启：
   `videoSeeThrough`、`sceneCapture`、`spatialMesh`、`planeDetection`、
   `spatialAnchor`。

### 6. 准出结果

Unity Editor 10 秒验证：

```text
floorArea: 25.5258m2
obstacleCount: 5
destinationsSelected: 2
destinationsReached: 1
distanceTravelled: 8.5366m
outsideFloorFrames: 0
obstaclePenetrationFrames: 0
offNavMeshFrames: 0
navMeshBuildMs: 12
```

PICO Emulator APK 10 秒验证：

```text
source: PICO SDK MockSceneData
floorArea: 25.5258m2
obstacleCount: 5
destinationsSelected: 2
destinationsReached: 1
distanceTravelled: 8.3296m
outsideFloorFrames: 0
obstaclePenetrationFrames: 0
offNavMeshFrames: 0
navMeshBuildMs: 20
```

APK 为 Android Development IL2CPP、`arm64-v8a`，实际约 `61MB`，SHA-256：

`a6031586e9b21cdd550e6985f9519b47c880547b5f47ba6e2c8c815bf9ae9678`

下一准出项是在 PICO 4 Ultra 真机完成首次 Room Capture，验证真实
`Floor/Wall/Furniture -> RoomMapSnapshot -> NavMesh`，并将 Toren 模拟器的
Spatial Mesh 注入或对齐快照作为独立模拟测试增强。

## Phase AppleCutSet 技术交接：预切苹果资产与状态替换协议

### 1. 资源与职责

资产根目录：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/AppleCutSet/`

核心脚本：

- `prepare_apple_textures.py`：确定性生成红皮Base Color、果皮Normal和果肉
  Base Color。
- `build_apple_cut_set.py`：由共享母体生成三种状态、打包图片、导出三种格式并
  渲染检查视图；支持`--preview`和`--skip-renders`。
- `validate_apple_cut_set.py`：独立回读Blend/GLB/FBX，执行尺寸、图片、拓扑和
  旋转拼合验收。
- `package_apple_views.py`：生成中文状态总览和三套多视图。

最终源文件：

```text
GameApple_Full_90x90x103mm
GameApple_Half_45x90x103mm
GameApple_Quarter_45x45x84mm
```

文件名中的半边/四分之一宽度表示果体范围。种子会从切面轻微凸出，
因此含细节包围盒比果体宽约1.52mm。

### 2. 共享几何协议

- 果体为90×90×84mm，`Z`向上，局部原点固定在完整苹果中心。
- 横截面仅使用四次和八次谐波形成轻微瓣形，保证90°旋转周期。
- 完整果皮128个环向分段；半边使用连续64段；四分之一使用连续32段。
- 纵向轮廓使用保留控制点的Catmull-Rom细分，消除早期试制的折线轮廓。
- 半边位于`X >= 0`；基础小瓣位于`X >= 0, Y <= 0`。
- 两个半边使用Z旋转`0°/180°`；四个小瓣使用
  `0°/90°/180°/270°`。禁止负缩放镜像。
- `Apple_Stem_Transferable`只存在于半边模型。持梗半边被切割前保持果梗
  世界Transform并转交给一个小瓣，其他小瓣不得复制果梗。

### 3. 独立验收

`validate_apple_cut_set.py`对每种格式分别：

1. 重新打开Blend，或清空场景后导入GLB/FBX。
2. 在世界空间测量果体及整体顶点范围。
3. 先检查原始退化面，再以`1e-7m`焊接导入器产生的UV/切线拆点。
4. 检查边界边、非流形边、面朝向、焊接后坍塌面、网格数、三角形和图片。
5. 收集果皮材质实际使用的顶点，将半边/小瓣绕Z旋转后与完整母体比较。

最终7,042个果皮表面顶点在0.001mm量化下完全匹配：

```text
two_halves_missing_vertices: 0
two_halves_extra_vertices: 0
four_quarters_missing_vertices: 0
four_quarters_extra_vertices: 0
validation_passed: true
```

GLB导入会按UV和切线拆点，不能直接把未焊接边界边当作开放拓扑；
但焊接前的`raw_degenerate_faces`必须仍为0，避免验收过程掩盖源文件坏面。

### 4. Unity/PICO运行时建议

当前工程为Unity 2022.3.38f1、URP 14、本地PICO XR包，依赖锁解析到
XR Interaction Toolkit 2.5.4。建议新增：

```text
KnifeBladeTracker
AppleSliceController
ApplePieceState
CutAppleAction
```

运行链路：

1. `KnifeBladeTracker`在`FixedUpdate`保存刀刃根/尖上一帧和当前帧位置，
   用扫掠面检查跨越，不只依赖单次Trigger。
2. 校验速度阈值、刀刃与允许切面角度、中心有效区域、状态和唯一事务ID。
3. `AppleSliceController`执行`Whole -> 2 Half -> 4 Quarter`唯一合法迁移。
4. 新块先在原苹果世界Transform实例化；下一FixedUpdate再启用独立低面数
   凸碰撞代理和小幅分离冲量。
5. 玩家通过`XRGrabInteractable`持刀；角色自动切割时由手部Socket/IK驱动同一
   刀具检测器，不复制命中逻辑。
6. `CutAppleAction`通过`AvatarActionCoordinator`获取控制Lease和物品占用，
   动画事件只开放切割窗口，不直接生成或销毁模型。

视觉模型、Collider和Rigidbody继续分离；动态物体不得直接使用14k三角形
渲染网格作为MeshCollider。完整流程与测试不变量见：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/AppleCutSet/reports/unity_slicing_implementation.md`

### 5. 合集与旧试制

- `GLB_and_Textures/Models/`已收录三个最终GLB。
- 对应贴图目录使用完整模型名，避免三个状态共用名称时产生覆盖。
- 旧`92×88`试制文件未删除；`collect_glb_assets.py`通过
  `SUPERSEDED_MODEL_NAMES`排除，不参与17模型/56图片的正式合集。
- 当前未创建Unity Prefab、Collider或PlayMode测试，也未执行PICO真机切割；
  不能把美术和文件验收表述为实机玩法已完成。

## Phase AppleCutSet Visual Rework V2 技术交接

### 1. 替代关系

用户否定首版`90×90`视觉表现。本节替代上一节的材质、切面细节和网格统计；
共享原点、四向旋转复用及Unity预切状态机协议保持不变。

被否定版本归档：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/AppleCutSet/reports/pre_visual_rework_v1.zip`

正式Blend、GLB、FBX沿用原文件名并由Visual Rework V2覆盖，根对象
`revision`属性为`Visual Rework V2`。

### 2. 视觉与材质实现

- 纵向控制轮廓将最大半径前移到苹果上半部，下半部连续收窄；
  Catmull-Rom细分和四次/八次横截面谐波保持平滑及90°周期。
- 果皮使用：
  - `apple_skin_basecolor.png`
  - `apple_skin_normal.png`
  - `apple_skin_roughness.png`
- 果肉使用：
  - `apple_flesh_basecolor.png`
  - `apple_flesh_normal.png`
- 果皮降低Coat，粗糙度由独立Non-Color图片控制；果肉使用较高粗糙度、
  低Coat和少量Subsurface。
- `create_ribbon_on_plane()`沿共享切面轮廓生成约0.6mm闭合果皮薄边。
  半苹果为完整闭合环；小瓣只沿两个外皮边生成开放路径带，不覆盖内部中心线。
- 籽腔使用低突出闭合椭圆体，种子厚度由1.6mm降到约0.5mm；
  果核膜与果肉色差降低，避免贴片感。
- 果梗中心线弯曲，并叠加三次/七次截面半径扰动，仍保持闭合流形。

### 3. 最终数据

```text
Full:    2 meshes, 14,364 triangles, 3 images
Half:    8 meshes,  9,888 triangles, 5 images
Quarter: 9 meshes,  6,098 triangles, 5 images
```

果体尺寸不变：

```text
Full body:    90 × 90 × 84 mm
Half body:    45 × 90 × 84 mm
Quarter body: 45 × 45 × 84 mm
```

种子和薄边轻微越过切面，半边/小瓣含可见细节宽度约45.49mm；
碰撞体仍应依据Fruit主体，不依据整体渲染包围盒。

### 4. 验收与交付

- 最新Blend、GLB、FBX的网格数、三角形数、图片数和尺寸一致。
- 原始退化面、焊接后坍塌面、边界边、非流形边和相邻面朝向错误均为0。
- 两半与四瓣重组后的7,042个果皮表面顶点继续完全一致。
- `GLB_and_Textures`当前17个独立模型、61张实际嵌入图片；
  三个苹果模型在合集、源导出和JSON报告中的SHA-256一致。
- 旧`92×88`试制仍由`SUPERSEDED_MODEL_NAMES`排除；V1回退包为ZIP，
  不会被GLB扫描器收录。

## Phase AppleCutSet Visual Rework V3：Round Profile

V3仅替换母体纵向轮廓和横截面起伏，材质、薄皮、籽腔、共享原点及运行时
状态协议沿用V2。根对象`revision`更新为
`Visual Rework V3 Round Profile`。

### 轮廓修正

- V2在`Z≈30mm`仍使用约44mm半径，导致正面和斜视出现近直侧壁。
- V3按接近球形的半径序列重新取样：顶部肩线逐步扩张，最大45mm半径位于
  果体中部，之后连续收窄到底部。
- 四次/八次横截面谐波由`0.012/0.004`降为`0.008/0.002`，
  减弱方形四瓣感，同时保留90°旋转周期。
- 尺寸仍为`90×90×84mm`，不需要修改Unity缩放、Collider中心和替换旋转。

### 最终验证

```text
Full:    2 meshes, 13,596 triangles, 3 images
Half:    8 meshes,  9,450 triangles, 5 images
Quarter: 9 meshes,  5,852 triangles, 5 images
Shared skin vertices: 6,658
Two halves exact match: true
Four quarters exact match: true
```

三种格式的尺寸、网格数、三角形数、图片数和闭合拓扑均通过；
统一合集保持17个模型、61张实际嵌入图片。

## Phase D2 技术交接：PICO Emulator 原生 Spatial Mesh 导航基线

### 1. 阶段概述

正式开发前已完成 PICO Emulator 6.2.0 的原生环境数据闭环，模拟器不再使用
与 Toren 可视房间坐标无关的 `MockSceneData.json` 作为运行时主数据源。

当前三条数据路径严格分离：

```text
Unity Editor -> PICO MockSceneData（离线逻辑回归）
PICO Emulator -> PICO Spatial Mesh（Toren 原生注入网格）
PICO 真机 -> Room Capture Scene Anchors（缺失时引导扫描，失败即关闭）
```

模拟器 Scene Capture Provider 可启动且查询返回 `SUCCESS`，但当前空间
`sceneAnchorCount=0`。Spatial Mesh 查询返回：

```text
mesh blocks: 48
vertices: 75,931
indices: 289,074
triangles: 96,358
semantic floor vertices: 35
```

能力报告：

`/Users/bytedance/MateSoul/项目沉淀资料/validation/room_environment_capability_v001.json`

### 2. 核心架构

实现目录：

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/RoomNavigation/`

- `PicoSpatialMeshRoomMapProvider`：启动 `XRMeshSubsystem`，调用
  `PXR_MixedReality.QueryMeshAnchorAsync`，将每个 block 的局部顶点变换到
  Unity 世界坐标并深拷贝；快照完成后停止 subsystem，避免静态玩法持续扫描。
- `RoomSurfaceMesh`：保存世界坐标顶点和三角索引。`RoomMapSnapshot` 的
  `SurfaceMeshes` 为可选字段，因此 Editor Mock 与真机 Scene Anchor Provider
  无需改变协议。
- `RuntimeRoomNavigation`：当快照包含原生 surface mesh 时，直接把 48 份
  三角网格作为 `NavMeshBuildSource`；仅在非网格路径使用 Floor Polygon 和
  Obstacle Box。这样不会再因粗包围盒覆盖整块房间而误删可行走面。
- `RoomNavigationBootstrap`：Editor 使用 Mock；Emulator 使用原生 Spatial
  Mesh；真机使用 Room Capture。设备端不渲染 D1 调试盒，初始角色位置优先
  取 HMD 水平前方约 `2.2m`，再投影到 NavMesh。
- `RoomNavigationValidationProbe`：原生网格路径记录 block、顶点、三角形、
  地图 Bounds 和角色终点；逐帧检查 Floor Polygon 与 NavMesh 覆盖。

### 3. 坐标与验收数据

最终模拟器报告：

`/Users/bytedance/MateSoul/项目沉淀资料/validation/room_navigation_v001.json`

```text
source: PICO Emulator Spatial Mesh
floor area: 55.8912m2
map bounds: 11.2609 x 3.4318 x 6.3464m
surface meshes: 48
surface vertices: 75,931
surface triangles: 96,358
NavMesh build: 15ms
travelled: 9.5838m
outside floor: 0 frames
off NavMesh: 0 frames
result: PASS
```

原生 Surface Mesh 与 VST Toren 由同一模拟器环境提供，因此导航坐标和可视
房间不再是 D1 的两套数据。语义包围盒仍保留 14 份作为诊断元数据，但不作为
原生网格路径的 NavMesh 裁剪源，也不作为穿透真值。

### 4. SDK 与构建避坑

1. PICO Emulator 首次读取 `SPATIAL_DATA` 会弹系统权限框；必须允许后再采集。
2. `sceneCapture=1` 只代表能力开启，不代表已有 Scene Anchors；本次实测为 0。
3. Spatial Mesh 是实时数据。静态导航只取一次快照并停止 subsystem，避免持续
   处理更新事件。
4. PICO XR 0.13 的 `PLog` 会在 Android 广播线程访问 `Time.frameCount`，
   Unity 2022 IL2CPP 会抛线程安全异常并导致 SIGABRT。已修改：
   - `/Users/bytedance/MateSoul/XR/Runtime/Scripts/PXR_Loader.cs`
   - `/Users/bytedance/MateSoul/XR/Runtime/Scripts/Utils/PXR_Log.cs`
   
   后台线程日志现在降级为 `FrameID=worker`；模拟器持续运行并接收
   `SDKLoglevelChanged` 后进程仍存活。
5. batchmode 的默认 Untitled Scene 不能 Additive 新建场景。
   `RoomNavigationSceneBuilder` 会在没有可恢复场景时使用 `NewSceneMode.Single`。
6. Unity Personal License 需要由 Hub 启动。构建入口
   `/Users/bytedance/MateSoul/scripts/build-room-navigation.command`
   复用 Hub 会话参数后执行无人值守 Android 构建。
7. `adb screencap` 不包含 PICO VST 合成层，黑底截图不能证明或否定 Toren
   可视效果；本阶段以原生网格来源、坐标 Bounds、移动轨迹和 NavMesh 不变量
   作为自动化证据。

### 5. 交付与剩余风险

- APK：`/Users/bytedance/MateSoul/Builds/RoomNavigation_v001.apk`
- SHA-256：
  `a2fff052e6aa910a565761282e71d14116654ab2d6d470703da5678c24493976`
- 场景：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/RoomNavigation/RoomNavigation_v001.unity`

PICO 4 Ultra 真机仍必须验证 Room Capture 的 Floor Polygon、墙体/家具语义、
Tracking Origin 重定位和首次授权流程。正式玩法接入时还需将
`RoomRandomWalker` 纳入 `AvatarActionCoordinator` 的控制 Lease，并把模板包名
替换为正式 MateSoul 包名。

## Phase D2.1 技术交接：Unity 与 Spatial SDK 路线消歧

### 1. 当前平台结论

MateSoul 游戏当前不是 PICO Spatial SDK Android/Kotlin 工程，而是 Unity 工程：

```text
/Users/bytedance/MateSoul/MateSoul/
  Unity 2022.3.38f1
  URP 14.0.11
  com.bytedance.pico.xr -> /Users/bytedance/MateSoul/XR
  Android ARM64 / IL2CPP
```

因此不得执行以下操作：

- 配置 `com.pico.spatial:bom`
- 新建 Kotlin/Compose/FullStage 工程
- 使用 Spatial ECS 替代 Unity GameObject、NavMesh、Animator 或
  Animation Rigging

### 2. 名词边界

`Spatial Mesh`、`Scene Capture`、`Spatial Anchor` 是空间能力名称。它们同时
可能出现在不同 PICO SDK 产品中。本项目调用的是
`ByteDance.PICO.XR.PXR_MixedReality`、`PXR_SpatialMeshManager` 等
**PICO Unity SDK API**，不等于引入 PICO Spatial SDK BOM。

当前正式方案：

`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_Demo开发方案.md`

旧方案：

`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_首版开发计划.md`

旧方案已标记为归档，仅保留产品目标、心情值规则和 Avatar Runtime-first
设计思想；其中 Kotlin、FullStage、BOM 和 Spatial ECS 步骤全部失效。

## Phase Framework F2 技术交接：角色动作与领域内核（2026-10-02）

本阶段仍在开发中。实施计划为
`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_游戏框架实施计划.md`。
用户明确允许尺寸、速度、阈值等使用代码默认值，禁止参数中心和环境变量膨胀。

`Assets/MateSoul/Core` 为不依赖引擎的程序集：

- `ActionCatalog` 显式注册动作定义与工厂；AI 暴露信息从同一目录取得。
- `AvatarActionCoordinator` 负责准入、控制权、退出阶段、结果、超时及释放。
- `ControlChannels.FullBody` 是身体与两臂的资源组合，不会绕过手臂冲突。
- `ControlLeaseManager` 不另建超时钟；Lease 生命周期与动作完全一致。
- `IAvatarAction.Cancel` 请求退出，Tick 继续至完成；Dispose 停止执行器并释放占用。
  尚未启动即被拒绝的动作也会 Dispose，因此实现必须只停止自己已启动的资源。

`Assets/MateSoul/Gameplay` 为纯 C# 物品注册/占用、情绪、对话编排和存档模型。
实例状态与定义分开；晚到的已取消对话不更新情绪或提交动作。

26 项 NUnit 测试已通过。测试在 Unity 自带 Mono 编译运行纯 C#，报告：
`/Users/bytedance/MateSoul/.artifacts/framework/domain-tests.xml`。
官方 NUnit 3.13.3 与 ConsoleRunner 3.20.0 放在忽略的临时构建目录；
运行需 `--inprocess --domain=None`，Unity 的自定义 NUnit DLL 不兼容官方驱动。
Unity batchmode 启动因沙箱禁止许可数据库写入而失败，尚未完成 Unity Test Runner、
真实 PlayMode 或 Android 构建；不将纯逻辑测试当作设备验收。

Unity 适配正在实现，尚未完成：
`AvatarMotor`、`CharacterActions`、Character/Runtime asmdef。
`RuntimeRoomNavigation` 新增共享角色尺寸常量；原生网格路径建立真实 MeshCollider，
不把诊断语义 AABB 当作碰撞体。该变更需场景回归。

## Phase Framework F3 技术交接：运行时装配与编辑器验证（2026-10-02，验收未完成）

### 运行时与内容入口

工程路径仍为 `/Users/bytedance/MateSoul/MateSoul`。以下路径均相对此工程：

| 入口 | 职责 |
| --- | --- |
| `Assets/MateSoul/Runtime/GameBootstrap.cs` | 选择编辑器 Mock / 模拟器原生 Mesh / 真机 Scene Capture；装配会话；处理暂停、销毁 |
| `Runtime/GameSession.cs` | 显式装配目录、角色、物品、情绪、对话、存档；注册 move/stay/visit/outfit |
| `Runtime/AvatarMotor.cs` | CharacterController 位移、朝向、Speed 动画参数；动作持有执行器 |
| `Runtime/CharacterActions.cs` | 移动、短暂停留、访问物品、换装；移动每秒重算路径以响应物品导航切割 |
| `Runtime/AutonomousBehaviour.cs` | 本地默认活动权重；向同一个协调器提交请求 |
| `Runtime/ItemDefinition.cs`、`GameItem.cs` | 只读定义资产、实例视图；可变状态/占用仍归 ItemRegistry |
| `Runtime/SpatialAnchorService.cs` | PICO Unity SpatialAnchor 创建/持久化/查询/定位；异步操作结束后释放 handle/provider |
| `Runtime/JsonGameSaveStore.cs` | 版本验证、临时文件原子替换；保存一个房间锚点与物品相对姿态 |
| `Runtime/HttpLLMProvider.cs` | HTTPS 网关；POST input/history/actions/targets，接收 ConversationReply；不在 APK 内放供应商密钥 |
| `Runtime/GameHud.cs` | 基础状态、输入/字幕、移动/停止、换装、物品访问/移除、保存；编辑器鼠标、设备 XRI 射线入口 |
| `Editor/GameSceneBuilder.cs` | 生成正式场景、角色 Variant、Speed BlendTree、陪伴灯定义与碰撞/导航切割 |

以上短路径的基目录是 `Assets/MateSoul`。生产场景为
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Scenes/MateSoul.unity`；
角色 Variant 为 `Assets/MateSoul/Content/Lengyue_Runtime.prefab`。
保留源角色 Prefab 和原验证场景。Variant 禁用旧 HeadPoseGuard 的上身骨骼覆盖，
改用已有 BodyOnly Idle/Walk；完整视觉回归尚待完成。

### 持久化与接口边界

1. 角色尺寸、速度、活动权重、时间阈值均为局部代码默认值或少量 Inspector 字段。
   无新增游戏环境变量、参数中心、规则引擎或全局服务定位器。
2. AI 仅能提交目录公开动作，模型不能指定控制通道或优先级；未知动作/失效目标明确拒绝。
   未设置 GameBootstrap 的 `conversationEndpoint` 时明确显示未连接。
3. 物品复用同一内容定义，各自持有实例 ID 和状态。访问期间获得 ItemReservation；
   删除目标立即使占用失效，随后由协调器释放动作控制权。
4. `matesoul-v1.json` 位于 `Application.persistentDataPath`。房间锚点无法恢复时，
   保留存档内原物品数据并显示失败，不在未知世界坐标中恢复物品。
   编辑器使用明确的 `editor-mock-room` 锚点，设备不会把它当真实 UUID。
5. 修复了情绪降级后收到其他情绪微小信号会重新升级的问题；暂停/离线不推进时钟。

### 验证工具及已知问题

命令行 Unity 批处理曾卡在启动 ADB/Bee 子进程：
`mono_os_sem_post: semaphore_signal failed with error 15`。进程超过一小时没有
进入测试。本次已结束遗留 PID，移除新增的批处理启动脚本。该证据定位到
Unity/Mono 子进程启动链，尚未定位其更底层成因，不能归因于游戏逻辑死循环。

现在使用 Unity Hub 启动编辑器 → `MateSoul > Game > Run Framework Tests`。
`Tests/Engine/FrameworkTestMenu.cs` 通过官方 TestRunnerApi 执行并输出 XML；
不另建后台测试服务。此入口已经实际跑完多轮。

Unity Test Framework 1.1.33 不接受原来的 `async Task` NUnit 测试签名，
已改成可同步完成的领域用例；PlayMode 进入前不能在 UnitySetUp 重载后重复创建
编辑器场景，场景准备移到测试菜单执行。脚本重编译后曾出现旧测试元数据，
后续完整重载已消除四个旧 async 签名失败。

最新结果：

- 纯 C# NUnit：28/28，通过；`.artifacts/framework/domain-tests.xml`。
- Unity TestRunner：32/33，通过 32 项。剩余
  `ProductionCharacterMovesAnimatesAndStopsWithoutLeavingNavMesh` 在 UnitySetUp
  出现 NullReferenceException，栈指向 `GameRuntimeTests.cs:72`。
  此用例上一轮曾通过；不能据此忽略最新失败，应在稳定程序集状态下复现定位。
- 最新通过的集成用例：暂停/抢占、两物品独立状态及 AI 目标删除释放、
  换装/情绪/物品恢复与未连接 AI 状态、JSON 原子替换。
- 主场景 HUD 视觉和模拟器原生 Mesh + CharacterController 回归尚未完成；
  PICO 锚点代码未进行真机跨启动验收，Android APK 尚未构建。
- 最近一次重跑操作被 Computer Use 明确返回屏幕锁定；没有继续操作锁屏界面。

下一次从解锁后的编辑器测试入口继续，完成剩余用例及
`MateSoul > Game > Build Android APK`。不要重复等待已失败的批处理链路，
不要将当前框架描述为已经全部验收。

## Phase Framework F4 技术交接：测试域重载修复与 APK（2026-10-02）

Unity Test Framework 1.1.33 的 BeforeAfterTestCommandBase 会新建迭代器并恢复
程序计数器。UnitySetUp 在 EnterPlayMode 之前由编译器初始化的捕获变量闭包不会
自动恢复，造成 `var restore = session.RestoreItemsAsync()` 处赋值空引用。
运行时插桩已证实 session/character 非空。将 SetUp 的 WaitUntil lambda 改为
`while (!restore.IsCompleted) yield return null` 后，33 项测试全绿；移除插桩后
再次全绿（08:34:26Z）。避免在跨域重载的测试初始化中捕获局部变量。

Android 编辑器构建已实际通过 Bee、UnityLinker、IL2CPP、ARM64 编译和 Gradle，
产物 `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，SHA-256：
`4744b7eaf8557631870a4517c2ca43810658b649515edf54b6e8a241edb28baa`。
原包名仍为 `com.UnityTechnologies.com.unity.template.urpblank`。
模拟器 ADB 位于
`/Users/bytedance/MateSoul/.pico-home/6.2/emulator/system-images/platform-tools/adb`。
启动器为 `scripts/start-pico-emulator.command`，通过 Terminal/LaunchServices 启动。
2026-10-02 16:48:02.683 设备日志证实新 GameBootstrap 已读取 48 原生 Mesh blocks，
构建 NavMesh 38ms 并输出 MATESOUL_READY。运动回归与交互验收仍待完成。

临时 Editor/FrameworkValidationTrigger.cs 监听
`.artifacts/framework/editor-request.txt` 并执行菜单。必须先写 `.pending`，
再原子 rename 为 `.txt`，避免编辑器抢先消费空文件。该类、meta、HUD 插桩、
debugger 日志/环境/会话记录已在 F5 交付时删除。
不要在 Play Mode 中刷新脚本：热重载不恢复普通 C# GameSession，源角色布料缓存
也不支持运行中脚本重载。验证应先退出 Play、等待编译结束，再启动。

## Phase Framework F5 技术交接：输入验证与开发包运动证据（2026-10-02）

### 1. 框架交付入口

Unity 工程：`/Users/bytedance/MateSoul/MateSoul`。
打开菜单 `MateSoul > Game > Open Game Scene` 后进入 Play。
测试入口为 `MateSoul > Game > Run Framework Tests`；Android 开发包入口为
`MateSoul > Game > Build Android APK`。继续使用编辑器入口，避免已观察到的
batchmode 子进程启动挂起。

最新 Unity 测试在 `2026-10-02T08:57:31Z` 结束，33/33 通过；
其中 28 个领域用例、4 个真实角色/NavMesh 集成用例、1 个 JSON 存储用例。
纯领域 NUnit 的 28/28 与这 33 项重叠，不能相加宣称为 61 个独立用例。
静态编译再次通过。原角色 Prefab 与旧导航验证场景保持独立。

### 2. HUD 验证范围

报告：`/Users/bytedance/MateSoul/项目沉淀资料/validation/framework_hud_20261002.json`。
生产场景中通过合成 Mouse 设备排队发送 MouseState，经过 InputSystem、
XRUIInputModule、GraphicRaycaster 和 Button 正常事件链，验证六次状态变化：
暂停/恢复、自主停止/恢复、字幕关闭/开启。未直接调用 onClick 或业务方法。

测试期间临时选择 InputSystemActions，并在结束后恢复原模式及移除测试设备。
XRI 2.5.4 的 Both 路径会先读新输入，再用旧 Input 状态覆盖；向新系统单独注入
事件不能验收 Both 的实体鼠标输入。桌面工具没有可靠提供 Game 视图原生鼠标输入，
因此不宣称完成实体鼠标及 PICO 控制器交互验收。没有据此增加业务输入兜底。
项目仍保留原 `activeInputHandler=2`，Android 构建会提示 Both 不受支持；
正式发布前应结合旧脚本的输入依赖统一输入模式并做真机验证。

### 3. 开发包只读运动探针

源码：
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/DevelopmentNavigationProbe.cs`。
只在 Development Player 的 GameBootstrap 场景自动附加，编辑器不自动运行，
非 Development 构建不编译该类。它不提交动作、不修改角色或会话状态。
启动后观察 30 秒有效游戏时间，记录原生 Mesh 数量、实际平面位移、移动启动次数、
Animator Speed、地板范围、NavMesh 偏离和房间碰撞侵入。

碰撞检测使用向内收缩 5 cm 的角色胶囊，并抬高底部 6 cm 排除正常地面接触。
这能证明采样范围内没有较深侵入，不能替代连续碰撞分析。
报告写入 `Application.persistentDataPath/framework-navigation.json`，
输出 `MATESOUL_NAVIGATION_PASS/FAIL` 后销毁探针。30 秒观察以外的长期稳定性、
设备帧率、真机 Room Capture 和空间锚点恢复不由该报告证明。

最终 `emulator-5554` 报告：

```text
source: PICO Emulator Spatial Mesh
mesh blocks: 48
floor area: 55.8912 m²
NavMesh build: 34 ms
game time: 30.2207 s
frames sampled: 381
movement starts: 3
distance: 15.9345 m
max Animator Speed: 1.10003
outside floor: 0
off NavMesh: 0
room penetration: 0
result: PASS
```

结构化报告：
`/Users/bytedance/MateSoul/项目沉淀资料/validation/framework_navigation_20261002.json`。
最终 APK 为 79,893,920 字节，SHA-256
`88e7f5628ae185050623acd393013cca4775b0799c84cf0e8a0c2d9ccb0f2538`。
Unity 的 `BuildReport.summary.totalSize=742341569` 表示构建总输出量，
不是 APK 文件长度。

### 4. 外部能力边界

- 无模型服务地址时显示未连接；真实 HTTPS AI 网关、语音和长期记忆尚未验收。
- 真机空间授权/扫描、物品锚点跨启动、控制器射线与头显性能需设备实测。
- 坐姿完整动画、牵手与完整美术 UI 属于后续玩法和内容，不存在伪成功占位动作。
- 根 Git 仓库尚无初始提交；本次分支为 `framework/game-runtime`，
  不将既有大资产、调试目录、设备私钥或 SDK 工作区批量加入版本控制。
- 当前清理发生在桌面锁定期间，Unity GUI 尚未刷新被删除的 Editor 验证桥接；
  当前源码的独立 Runtime/Editor 静态编译已通过，下次解锁后 AssetDatabase
  会同步删除项。

## Phase Framework F6 技术交接：PICO XR VST 合成（2026-10-02）

### 1. 运行链路

MateSoul 继续使用 PICO Unity Integration SDK。设备端初始化顺序为：

1. `GameBootstrap.InitializeAsync` 请求 `PXR_Manager.EnableVideoSeeThrough = true`。
2. 模拟器选择 `PicoSpatialMeshRoomMapProvider`；真机选择
   `PicoSceneCaptureRoomMapProvider`。
3. `RuntimeRoomNavigation` 使用房间数据建立 NavMesh 和 MeshCollider。
4. PICO compositor 将内置卧室 VST 放在底层，Unity 透明 App Layer 在上层渲染
   角色、HUD 和射线。

VST 与房间理解是两个独立通道：VST 只提供系统合成背景，应用不能读取其图像；
Spatial Mesh/Scene Capture 提供导航和碰撞数据。不得把可见卧室纹理当作导航几何。

### 2. 必要渲染配置

- XR Camera：`SolidColor`、背景 `RGBA(0,0,0,0)`、`allowHDR=false`。
- `URP-Performant`、`URP-Balanced`、`URP-HighFidelity`：
  `m_SupportsHDR=0`。
- XR Camera 未启用 URP Post Processing。
- Editor Overview Camera 保持不透明背景和 HDR，仅用于编辑器俯视，不进入设备渲染。

场景生成器路径：
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/RoomNavigation/Editor/RoomNavigationSceneBuilder.cs`。
运行时入口：
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameBootstrap.cs`。

### 3. 验证证据

最终 APK：

```text
path: /Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk
bytes: 79,893,918
SHA-256: 6aea4521a6e1efc152eae3753803753113bfceed7cd6edfae1171a61df1dc2d6
Unity total build output: 742,341,587 bytes
```

模拟器日志确认 `XR_FB_passthrough Version=5`、`SupportColor=1`、
`EnableVideoSeeThrough 1`、`Succeed to create a passthrough layer`，
随后 `SeethroughStateChanged status=3`。最终 2160×2158 画面显示卧室 VST，
角色、World Space HUD、控制器射线均可见，不再是黑色背景。

运动报告：30.27 秒、277 帧、4 次移动启动、15.37 米；
越界、偏离 NavMesh、房间碰撞侵入均为 0。
结构化证据位于：

- `/Users/bytedance/MateSoul/项目沉淀资料/validation/framework_vst_20261002.json`
- `/Users/bytedance/MateSoul/项目沉淀资料/validation/framework_navigation_20261002.json`
- `/Users/bytedance/MateSoul/项目沉淀资料/validation/game_framework_20261002.json`

### 4. 模拟器日志噪声

PICO Emulator 原生 passthrough 层会持续输出
`Passthrough effect data is NULL!`，但基础 VST 可见且状态已启用。
补调 `PXR_MixedReality.EnableVideoSeeThroughEffect(true)` 无法消除该日志，
因此没有保留无效 effect 调用或增加重试状态机。该现象按模拟器原生限制记录，
真机验收时重新观察，不据此判定真实设备存在同类问题。

## Phase Framework F7 技术交接：底部导航与分页 HUD（2026-10-02）

### 1. UI 结构

实现文件：
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs`。

运行时层级：

```text
MateSoul HUD
└── Bottom HUD
    ├── HUD Content
    │   ├── 对话栏目
    │   ├── 互动栏目
    │   ├── 信息栏目
    │   ├── 虚拟物品栏目
    │   └── 设置栏目
    └── Bottom Navigation
        ├── 对话
        ├── 互动
        ├── 信息
        ├── 虚拟物品
        └── 设置
```

底部导航固定为 824×160 UI 单位；五个按钮均为 145.6×140 的近方形功能牌，
按钮间距 18，标签置底，不包含底部高光或选中菱形。背景采用粉紫半透明
九宫格圆角 Sprite；Sprite 由 48×48 RGBA 纹理运行时生成，切片圆角半径 11，
并由 HUD 生命周期释放，不依赖 `UI/Skin/UISprite.psd`。上方内容区为 760×330；默认关闭，
点击页签展开，再次点击当前页签收起。
所有页面预先创建并切换 Active 状态，不在每次点击时重复分配 UI 对象。

五个图标不是字符或字体符号。对话气泡和八齿齿轮由 `CreateIconSprite`
生成 96×96 RGBA 纹理；手掌、信息和背包在 F8 改为直接加载参考图提取资源。
图标色为 `RGB(255,251,226)`；模拟器最终实拍卡片中心色为
`RGB(192,154,187)`。

### 2. 玩家视角定位

- Editor：`ScreenSpaceOverlay`，参考分辨率 1920×1080，锚定底部中央。
- Android/PICO：`WorldSpace`，画布约 0.99 米宽，目标位置为相机前方 1.5 米、
  相机 Up 方向下移 0.70 米。
- `LateUpdate` 使用指数平滑跟随相机位置和旋转；“重置界面”执行立即定位。
- UI 只跟随视角，不写角色 Transform，也不参与 AvatarActionCoordinator 控制权。

### 3. 页签职责

- 对话：回复字幕、文本输入、发送、字幕开关。
- 互动：`一起散步`、`打招呼`、`坐下聊天`、`玩猜拳` 测试活动。
  当前只返回“测试按钮已响应”，后续真实行为必须注册 ActionCatalog 能力。
- 信息：会话/心情/自主状态、随机移动、停留、换装。
- 虚拟物品：当前物品、放置、轮换、访问、移除。
- 设置：自主活动、暂停/继续、重新定位物品、保存、重置界面。

UI、AI 和自主行为继续通过同一个 `GameSession.Submit` 入口提交角色动作。

### 4. 输入与测试

`ProjectSettings.activeInputHandler=1`，即 New Input System。项目正式场景使用
XRI、`XRUIInputModule`、`TrackedDeviceGraphicRaycaster`；未打包实验场景中的
旧 `Input.GetKeyDown` 不再要求 Android 启用 Both。

新增 PlayMode 测试：
`GameRuntimeTests.BottomHudExpandsVerticalInteractionActivities`。
测试通过 `ExecuteEvents.pointerClickHandler` 发送 PointerClick，而不是直接调用
按钮回调，验证导航名称、互动栏目展开、四项纵向顺序、反馈和收起。
最新 Unity TestRunner 为 34/34，通过时间 `2026-10-02T14:32:01Z`。

最终 APK：

```text
path: /Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk
bytes: 79,907,577
SHA-256: a15c89e9feab53bf288a8d7e5b09b7112c4dd47ab0b9c8449c280497325528b5
Unity total build output: 742,907,229 bytes
```

结构化报告：
`/Users/bytedance/MateSoul/项目沉淀资料/validation/framework_ui_20261002.json`。

## Phase Framework F8 技术交接：参考图标资源化（2026-10-02）

### 1. 资源来源与边界

用户提供的最终局部参考图尺寸为 916×186。手掌、信息和背包不再通过
`InsideIcon` 的圆/胶囊组合近似，而是从参考图中各取 80×80 窗口，
按前景色分离透明 alpha 后放大为 192×192 RGBA 正式资源：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/Resources/MateSoulUI/Interaction.png`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/Resources/MateSoulUI/Information.png`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/Resources/MateSoulUI/Items.png`

这些资源只复用用户提供参考图中的 UI 图形，不包含背景、按钮或文字像素。

### 2. 运行时加载

`ReferenceIconPath` 仅为 `Interaction`、`Information`、`Items` 返回
Resources 路径。`CreateIconSprite` 使用 `Resources.Load<Texture2D>` 加载，
设置 Bilinear/Clamp 后建立运行时 Sprite；资源缺失时抛出
`InvalidOperationException`，避免回退到不一致图标。对话气泡和齿轮仍走
96×96、4×4 超采样程序化生成链路。

导航图标 Rect 为 80×80，顶部偏移 9。这个尺寸与参考图的原始取样窗口一致，
卡片本身仍为 145.6×140，因此图标相对卡片的占比也与参考一致。

### 3. 验证

- Unity TestRunner：34/34，通过时间 `2026-10-02T14:32:01Z`。
- 模拟器：2160×2158 全帧与 900×300 局部放大目视通过。
- 导航探针：30.00 秒、1096 帧、5 次移动、15.38 米；
  `outsideFloorFrames=0`、`offNavMeshFrames=0`、`roomPenetrationFrames=0`。
- 最终 APK SHA-256：
  `a15c89e9feab53bf288a8d7e5b09b7112c4dd47ab0b9c8449c280497325528b5`。

## Phase Framework F9 技术交接：虚拟物品放置、聚焦与拾取（2026-10-02）

### 1. 阶段概述

本阶段在现有 `ItemDefinition -> GameSession -> ItemRegistry -> Anchor/Save`
链路上接入桌子和椅子，没有创建第二套物品运行时。UI 只提交期望定义和姿态，
`GameSession` 统一负责地面投影、占用检查、实例化、释放归位和存档。

核心文件：

| 文件 | 职责 |
|---|---|
| `Runtime/ItemDefinition.cs` | 目录可见性、地面占用尺寸、放置高度、聚焦材质 |
| `Runtime/GameSession.cs` | 目录、最近空位搜索、生成、释放归位、锚点与存档 |
| `Runtime/GameItem.cs` | 物品实例、XRI 抓取生命周期、NavMeshObstacle 切换、高亮材质 |
| `Runtime/VirtualItemFocusController.cs` | 从主相机中心执行 SphereCast，只聚焦最近可见物品 |
| `Runtime/GameHud.cs` | 动态目录按钮和视前期望姿态，不执行碰撞规则 |
| `Editor/GameSceneBuilder.cs` | 桌椅 prefab/定义生成及控制器、眼手输入装配 |

### 2. 放置与释放流程

1. HUD 读取 `GameBootstrap.CatalogItems`，只显示 `CatalogVisible=true` 的定义。
2. 玩家点击桌子或椅子后，HUD 用相机水平前向计算 1.45 m 前的期望姿态。
3. `GameSession.TryResolvePlacement` 将候选 Y 固定为
   `INavigationService.FloorHeight`，避免床、桌面等高位水平网格成为放置面。
4. 从期望点开始，以 0.25 m 为一圈，最多 8 圈、每圈 16 个方向依距离搜索。
5. 每个候选先用 `INavigationService.TrySample(..., 0.3m)` 验证主地面 NavMesh，
   再按定义 footprint/height 执行带旋转的 `Physics.OverlapBox`。
6. 首个空候选用于生成；首次生成仍通过现有 `ISpatialAnchorService` 建立房间锚点，
   物品以锚点局部姿态进入 `JsonGameSaveStore`。
7. 拾取时禁用该实例的 `NavMeshObstacle` 并记录稳定姿态；释放时复用同一搜索，
   找不到空位则回退稳定姿态，随后恢复障碍并保存。

`FloorHeight` 是导航服务的明确契约。不要再从头显、物品当前高度或
`NavMesh.SamplePosition` 的任意最近面推断房间地面。

### 3. XR 输入与聚焦

- 手柄：左右 `ActionBasedController.selectAction` 绑定
  `<XRController>{LeftHand|RightHand}/gripPressed`，Value 绑定 `/grip`。
  Trigger 继续只用于 UI Press。
- 眼手：额外的 `Gaze Pinch Ray` 使用 `<XRHMD>/centerEyePosition` 和
  `centerEyeRotation`，Select 绑定
  `<PicoHandInteraction>{RightHand}/pinchTouched`，Value 绑定 `/pinchValue`。
- 项目设置 `handTracking=1`，`handTrackingSupportType=0` 保持控制器与手同时支持。
- `XRGrabInteractable` 使用 Kinematic Movement、Dynamic Attach、关闭旋转跟踪和
  Throw On Detach，符合家具平移放置而非投掷物语义。
- 聚焦不依赖 XRI Hover 材质。`VirtualItemFocusController` 从视野中心做 5.5 cm
  SphereCast，命中最近 `GameItem` 后附加 `MateSoul/Item Focus Highlight`
  Fresnel 透明材质；移出中心、开始拾取或组件禁用时恢复原材质数组。

眼手模式对外说明统一为：**视野中心瞄准，右手拇指食指捏合拾取，松开释放**。

### 4. 内容资源

源资产及 Unity 生成物：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/PropsSource/Table/`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/PropsSource/Chair/`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/Table.prefab`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/Table.asset`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/Chair.prefab`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/Chair.asset`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/ItemFocusHighlight.mat`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/Shaders/ItemFocusHighlight.shader`

桌子定义尺寸为 footprint 0.60×0.40 m、高 0.75 m；椅子为
0.36×0.38 m、高 0.68 m。场景生成器会检查 FBX Renderer Bounds 与这些生产尺寸
误差不超过 5 cm，避免 Unity 导入缩放错误静默进入运行时。

### 5. SDK 与验证避坑

- PICO Emulator 首次启用手追需要
  `com.picovr.permission.HAND_TRACKING`；房间 Mesh 同时需要
  `com.picovr.permission.SPATIAL_DATA`。权限弹窗会暂停 Unity XR Session，
  自动化验收必须先处理权限再读取 `MATESOUL_READY`。
- 模拟器主窗口是无 bundle ID 的裸 `qemu-system-aarch64`，通用 macOS
  Computer Use 无法可靠绑定；ADB Touch 也不会转换为 XRI Tracked Pointer。
  本阶段只用临时 Development Intent 做渲染验收，随后从源码和 APK 删除。
- PICO Spatial Mesh 中床面可能进入 NavMesh。物品放置必须先固定主地面高度并使用
  0.3 m 小范围采样；直接从头显高度进行大半径 Sample 会把家具放到床面。
- `NavMeshObstacle` 在物品跟随玩家期间必须关闭，否则 carving 会随帧移动并扰动
  导航；释放稳定后再恢复。

最终验证：

```text
Unity TestRunner: 35 passed, 0 failed, 0 skipped
PICO runtime: MATESOUL_READY source=PICO Emulator Spatial Mesh items=0 navMs=45
APK bytes: 79,914,170
APK SHA-256: d39a33160ff7d5e7ca2bcac22b1ae6f24be110c5b707bddf1f23c15b4c88fa17
```

结构化报告：
`/Users/bytedance/MateSoul/项目沉淀资料/validation/virtual_items_20261002.json`。

## Phase Framework F10 技术交接：持久化锚点恢复状态机（2026-10-03）

### 1. 阶段概述

本阶段修复“首次放置桌椅后，重启既看不到旧物品，也无法继续放置”的恢复死锁。
锚点仍是跨启动定位的主来源；存档中的房间根姿态只作为经过当前房间合法性校验的
临时回退，不替代 PICO Spatial Anchor。

核心文件：

| 文件 | 改动 |
|---|---|
| `Gameplay/GameSaveData.cs` | 增加 `hasRoomAnchorPose` 和房间根 Pose 字段 |
| `Runtime/JsonGameSaveStore.cs` | 校验房间 Pose 的有限数及单位四元数 |
| `Runtime/SpatialAnchorService.cs` | 有界重试查询全部持久化锚点并匹配 UUID |
| `Runtime/GameSession.cs` | 恢复状态、姿态回退、新布局事务和防空覆盖 |
| `Runtime/GameBootstrap.cs` | 对外暴露恢复失败状态，不再误报完全就绪 |
| `Tests/Engine/GameRuntimeTests.cs` | 跨启动位置、临时失败回退和旧存档重建回归 |

### 2. 恢复状态与流程

`GameSession` 对物品恢复维护以下状态：

| `ItemsReady` | `ItemRestoreFailed` | 含义 | 目录点击 |
|---|---|---|---|
| `false` | `false` | 恢复正在进行 | 暂时拒绝，避免并发修改 |
| `true` | `false` | 物品已恢复或无旧物品 | 正常放置 |
| `false` | `true` | 旧布局不可定位 | 点击后尝试创建新布局 |

启动流程：

1. 读取并校验存档中的定义、锚点 UUID、物品局部 Pose 和可选房间根 Pose。
2. 通过 `PicoSpatialAnchorService` 加载持久化锚点。服务启动后最多查询 6 次，
   每次间隔 400 ms；每次查询全部锚点，再通过 `GetAnchorUuid` 匹配目标。
3. 锚点加载成功时，以其 Pose 设置 `Room Items` 根节点，并更新存档中的房间 Pose。
4. 锚点加载失败时，仅当存档有房间 Pose，且 `CanRestoreAt` 确认每件物品都位于
   当前主地面、位置误差不超过 0.15 m、没有非法重叠时，才按该 Pose 恢复。
5. 两条路径都失败时设置 `ItemRestoreFailed=true`，保留旧存档，不生成旧物品，
   启动状态显示“旧物品定位失败”。

禁止把 `Pose.identity` 当恢复兜底。它会把旧物品静默放到世界原点，既不能证明属于
当前房间，也可能穿墙或覆盖角色。

### 3. 新布局事务

旧布局不可定位后，目录第一次放置按以下顺序执行：

1. 先用统一的 NavMesh 和 `Physics.OverlapBox` 规则求得合法放置点。
2. 深拷贝旧 `GameSaveData`，仅在内存中清除旧锚点、旧物品和回退 Pose。
3. 创建并持久化新 PICO 锚点，设置 `Room Items` 根节点，生成请求的桌子或椅子。
4. 原子写入新存档后提交状态，并提示“已创建新布局”。
5. 任一步失败都恢复旧 `GameSaveData` 和失败状态，使旧布局仍可在未来重试恢复。

`Save()` 只有在 `ItemsReady=true` 时才从当前视图重建 `saved.items`。因此应用暂停、
失焦或退出时，不会把一次锚点加载失败误写成“用户删除了全部物品”。

### 4. PICO SDK 避坑

- `StartSenseDataProvider(SpatialAnchor)` 成功不代表持久化锚点已经立即出现在查询结果中。
  恢复必须覆盖 Provider 数据更新窗口，但重试要有上限，不能阻塞目录交互。
- 按 UUID 只查询一次会把暂时不可见误判为永久丢失。当前实现与 PICO 示例一致，
  查询全部锚点后在 Unity 侧匹配 UUID。
- 查询返回的非目标临时 handle 必须及时 `DestroyAnchor`；命中的 handle 由服务保存，
  在服务 Dispose 且没有进行中操作时统一释放。
- 修复前存档没有回退 Pose，不能凭局部物品坐标推导房间世界原点。锚点永久丢失时，
  正确行为是明确开始新布局，而不是猜测旧位置。
- 从 Trae 沙箱直接启动 QEMU 会被 macOS Hypervisor Framework 拒绝并返回
  `HVF error: HV_DENIED`。应通过
  `/Users/bytedance/MateSoul/scripts/start-pico-emulator.command` 让 Terminal
  在沙箱外启动，再用项目内 Android SDK 的 ADB 连接。
- PICO Emulator 6.2.0 虽在 gRPC `InputEvent` 中声明 `xr_hand_event`，实测发送该
  事件会触发宿主 `UniversalExceptionRaise` 并终止 QEMU。不要把该接口用于自动化
  回归，也不要据此判定 Unity 应用崩溃。

### 5. 验证与产物

```text
Unity TestRunner: 37 passed, 0 failed, 0 skipped
Android ARM64/IL2CPP: PASS
APK bytes: 79,915,653
APK SHA-256: 08b8a55455fdd989ef81eea60b39494ac2c29ad45b3acf323df9dbdb62ca3166
PICO Emulator cold start: PASS (4 persisted items restored through validated room pose)
Placement: PASS (table placed through GameSession, item count 4 -> 5)
Process restart: PASS (5 items restored at identical world positions)
Release APK restart: PASS (MATESOUL_READY items=5 itemState=ready)
Save SHA-256 before/after restart: 28c62ac422c0df2d536e6798eee4c4e1e4be219f2a5cfa002b2fe44058cb6e09
```

测试结果：
`/Users/bytedance/MateSoul/.artifacts/framework/unity-tests.xml`。

结构化报告：
`/Users/bytedance/MateSoul/项目沉淀资料/validation/virtual_items_restart_fix_20261003.json`。

本次设备验证没有清空 AVD 或应用数据。旧存档先恢复 2 张桌子和 2 把椅子；
一次性 Development 探针只负责读取 Intent 并调用与 HUD 相同的
`PlaceItemAsync`，验证完成后已从源码和正式 APK 删除。所有临时截图仅用于目视确认，
结构化结果写入报告后删除，不作为项目资产保留。

## Phase Framework F11 技术交接：现实椅子落座（2026-10-03，验证中）

### 数据与执行路径

1. `RealChairScanner` 使用 PICO Unity `QueryMeshAnchorAsync` 的顶点、索引和语义。
   世界坐标为 `position + rotation * vertex`；Room Capture 的 Chair OBB
   仅作为候选空间范围。`PxrMeshLod.High` 已启用，不能把此配置解释为厘米级精度保证。
2. `SeatGeometry` 按共享网格顶点拆分连通椅面，检查高度、水平度、完整支撑范围、
   靠背方向，再通过三角形与坐姿/腿部空间盒相交检查拒绝障碍物。
   默认只接收有明确靠背、约 0.32–0.54 m 高且尺寸适配角色的椅面。
3. 连续稳定结果注册 `real-seat-*` 与 `sit` 能力；现实椅子不写入虚拟物品存档。
   动作锁定一份座位几何；后续几何变化、缺失或超时会撤销可用性。
   网格失效表示当前不能证明安全落座，不表示椅子一定被人移动。
4. `SitOnChairAction` 通过 `AvatarActionCoordinator` 持有身体、行走和视线控制权，
   先抵达正前方可达点，再按固定座位朝向精确对齐。
5. `AvatarMotor` 仍是角色根节点唯一写入者。落座时暂时停用角色站立胶囊，
   使用已检查的坐姿空间；房间碰撞体保持开启。退出恢复站立点和胶囊。
6. `AvatarSeatingRig` 在 Animator 后、裙摆模拟前执行。Generic 骨架由 Editor
   显式绑定，不使用 `Animator.MatchTarget`；骨盆接触偏移和脚踝高度是角色标定参数。
   `SkirtPoseGuard.SetSeatSupport` 由原裙摆所有者施加约束。

### 验证工具与边界

- Editor 自动化入口：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/SeatingWorkbench.cs`。
  向 `/Users/bytedance/MateSoul/.artifacts/seating/editor-request` 写入
  `analyze`、`preview`、`tests` 或 `build`，由已授权的 Unity Editor 执行。
- 几何测试与动作测试：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/SeatingTests.cs`。
  已完成 48/48 基础与几何回归，追加动作验证后专项 12/12 通过。
  专项 XML 保存在 `/Users/bytedance/MateSoul/.artifacts/seating/seating-tests.xml`；
  最终设备验证结果待补。Unity 的 `EnterPlayMode` 会进行域重载，
  捕获局部变量的测试协程必须在进入 Play Mode 后再创建，否则闭包会丢失。
- `SeatingDeviceProbe` 仅在 Development 构建启用，通过应用私有目录中的文件
  `seating-validation/command` 执行 `sit`、`stand`、`stop`；
  输出实际 SDK 网格与角色状态供测试，不提供网络命令接口，Release 不包含入口。
- 当前尚未完成物理头显验证。不得将拟合残差、骨骼目标误差当作现实椅面定位误差。
  对软垫、透明材质、移动椅子和不完整扫描的效果，需要实际设备测量。
- 本轮 Unity C#/IL2CPP 已编译成功；Gradle 被 stop 命令中断后，复用
  `MateSoul/Library/Bee/Android/Prj/IL2CPP/Gradle`，
  将 `GRADLE_USER_HOME` 指向 `.artifacts/seating/gradle-home`，
  用 Unity 自带 Gradle 7.5.1 执行 `--no-daemon --offline --max-workers=4 assembleDebug`。
  日志 `.artifacts/seating/gradle-build.log` 记录 `BUILD SUCCESSFUL in 57s`，
  产出 `Builds/MateSoul_RealChair_Development.apk`，签名校验通过。

### F11 模拟器数据与构建续记（2026-10-03 19:17）

- `SeatingDeviceProbe` 使用显式 `MeshBlock` DTO 导出 SDK 网格，避免直接序列化
  未标记 Serializable 的 SDK 结构而得到 `{}`。
- 当前模拟器的 Chair 水平候选位于约 `(-3.323, 0.743, 0.699)`，
  主地面 `y≈0.391`，水平范围约 0.309 m，面积 0.0747 m²，无合格靠背。
  当前规则要求面积至少 0.105 m²、宽至少 0.34 m，并需确定靠背朝向，所以不会放行。
  解析脚本：`/Users/bytedance/MateSoul/.artifacts/seating/analyze_mesh.py`。
- 脚本变更可复用 `Library/Bee/artifacts/1300b0aPDevDbg.dag/MateSoul.Runtime.rsp`
  用 Unity 的 Roslyn 编译器编译。该程序集原始输出与原 ILPostProcess 输出哈希一致，
  当前没有额外 IL 后处理；复制到 `Library/Bee/PlayerScriptAssemblies` 后，
  使用 `Library/Bee/Player696e6a9e.dag` 完成裁剪和 IL2CPP 生成。
  本轮 Bee 在后期收尾停住，使用 `.artifacts/seating/finish_native.py` 按生成
  DAG 的原始命令保守重编译对象、链接并复制原生库与匹配的 global-metadata.dat，
  然后运行项目内 Gradle。不要把这条脚本增量路径用于场景/资源结构变更。
- 更新 APK 已安装，签名通过，SHA-256 为
  `00d58d52571ec92485270d7ca113d366a5367eba0ade3670c69426015831b795`。
  设备没有出现落座代码异常；已有物品焦点 HTTP 诊断代码仍有网络策略异常，待隔离。
- 仍待完成明确标注的标准椅测试模型演示与录像；真实头显未连接。

### F11 最终实现与验证补充

- `RealChairScanner.Start()` 不再自行循环调用 `QueryMeshAnchorAsync`，而是订阅
  `PXR_Manager.SpatialMeshDataUpdated`。PICO Manager 是原生网格查询与共享缓存的
  唯一所有者；Scanner 只负责把更新转换成 `SeatTriangle` 并维护座位稳定状态。
- `AvatarSeatingRig` 的脊柱姿态必须每帧从 `neutral[1]` 计算绝对目标。
  不要对上一帧 `spine.rotation` 继续左乘倾角，否则未被 Animator 完整覆盖的骨骼
  会逐帧累积前倾。专项测试会在坐稳后再执行 30 帧并检查旋转漂移小于 0.1°。
- `SeatingDeviceProbe` 只存在于 `DEVELOPMENT_BUILD || UNITY_EDITOR`，支持显式
  标准椅几何、动作命令、状态导出和录制辅助；这些能力不会自动替换 SDK 网格，
  Release 也不包含命令入口。视频中的标准椅只能证明动作、对齐与 IK 链路。
- 普通 Development 包不会启用物品焦点 HTTP 埋点。只有显式定义
  `MATESOUL_ITEM_FOCUS_DIAGNOSTICS` 才会编译该网络诊断，避免设备端
  `Insecure connection not allowed` 干扰物品绑定。
- 手工复用 IL2CPP 生成目录时，即使 C++ 编译和 Gradle 成功，也可能生成与
  `global-metadata.dat` 不一致的启动包。本轮曾表现为
  `GlobalMetadata::GetTypeInfoFromTypeDefinitionIndex` 栈溢出。
  最终 APK 由已授权 Unity Editor 完整执行 `BuildPipeline.BuildPlayer` 生成；
  `.artifacts/seating/direct_build.py` 只能作为诊断工具，不作为交付构建基线。
- 最终专项测试 12/12 通过；设备状态确认坐稳时骨盆目标误差约 1.23 mm，
  双脚高度误差小于 0.2 mm、站立胶囊关闭。起身后胶囊恢复、FullBody 租约释放，
  动作结果为 `Succeeded`。
- 最终 APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_RealChair_Development.apk`，
  SHA-256 `b1fe888e8ebf347c39750d8fbade55fdd9e0f3858c60e14005c4fcd0d721ad96`。
- 视频与报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/real_chair_seating_emulator_20261003.mp4`
  和
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/real_chair_seating_20261003.json`。
  模拟器原生 `screenrecord` 对 XR 主显示重复静止帧，视频改由 32 张连续设备
  `screencap` 按顺序编码；不是物理头显录像，也不构成现实椅识别精度验收。

### F11 原生房间椅识别与落座闭环

上一节的合成标准椅结论只覆盖动作、IK 和控制权链路，已被本节原生房间验证取代。
最终验收没有调用 `SeatingDeviceProbe.fixture`，输入来自
`PXR_Manager.SpatialMeshDataUpdated` 的 48 个 block、86,323 个三角面。

#### 识别策略

1. `SeatGeometry.Triangles` 保留每个三角面的多数语义标签与 mesh `SourceId`。
2. `Chair` 语义仍是高优先候选，但必须存在合格椅面和一致靠背；语义本身不能授权落座。
3. 对 `Unknown` block，只有同时满足面积、尺寸、平面 RMS、完整支撑采样和单侧上部
   结构一致性时才生成 `GeometryInferred=true` 的候选。
4. PICO Emulator 的可见扶手椅最终参数为中心
   `(-3.062346, 0.765189, -0.055161)`、朝向
   `(0.857872, 0, 0.513863)`、宽 `0.801418 m`、深 `0.708128 m`、
   平面 RMS `0.029331 m`、支撑采样 `43/49`。
5. SDK 错标为 `Chair` 的圆形边桌只有约 `0.308 × 0.308 m` 的紧凑水平面且无靠背。
   无靠背紧凑语义面当前统一 fail-closed；不要通过角色当前位置或物体名称补丁放行。

#### 净空所有权

`SeatGeometry.HasClearance` 必须区分目标椅本体与外部障碍：

- 目标 `SourceId` 的网格已用于证明椅面、靠背和支撑结构。角色坐入该体积是预期行为，
  因此不再把同源三角形作为外部扫掠障碍。
- 其他所有 mesh source 仍参与上身与腿部扫掠检查，不因 `Unknown` 标签而忽略。
- 模拟器数据中，修正前的 68 个碰撞三角形全部来自目标椅自身 block，
  没有外部来源；测试 `TargetChairMeshIsNotTreatedAsAnExternalObstacle` 同时断言
  同源表面放行、异源同位置表面拒绝。

#### 最终证据

- 座椅专项测试：
  `/Users/bytedance/MateSoul/.artifacts/framework/unity-tests.xml`，`19/19` 通过。
- APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_RealChair_Development.apk`，
  SHA-256 `79fda5d64f6a1bd4ed9fe2b31d78d1af40f5838ddf392001a7e95c1e13477db4`，
  82,292,943 字节，APK Signature Scheme v2 校验通过。
- 最终冷启动只发布 1 个座位，且为 `GeometryInferred=true` 的扶手椅。
  设备日志完整经过 `walking`、`docking`、`sitting`、`seated`、`standing`。
- 视频：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/matesoul_native_room_chair_complete_20261003.mp4`，
  SHA-256 `401e1ae83ad9705b2378253715381789372bb49cc277e532e55eb9100815a8f3`。
  该文件是最终 APK 的原生 `screenrecord` 裁剪，不含合成帧；48 秒内 48 个逐秒帧
  哈希均不同。模拟器 XR 主显示约 1 fps，因此不能据此评估真机动画帧率。
- 结构化报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/real_chair_seating_20261003.json`。

物理 PICO 头显仍未验证。模拟器原生房间可以证明“SDK 网格输入到角色动作输出”的
闭环，但不能替代实体房间中的几何误差、材质、遮挡和家具移动测试。

## Phase Framework F12 技术交接：DeepSeek 对话与联网 MCP（2026-10-04）

### 1. 阶段概述

本阶段把原有 `ILLMProvider` 扩展点实现为可部署的服务端网关，并完成
Unity Development APK、PICO Emulator、DeepSeek 与 stdio MCP 的真实端到端验证。
供应商密钥和协议不进入 Unity；AI、UI 和自主行为仍共享动作目录与
`AvatarActionCoordinator`，没有按调用来源复制角色执行逻辑。

运行链路：

```text
GameHud / DialogueDeviceProbe
  -> ConversationOrchestrator.SendAsync
  -> HttpLLMProvider
  -> POST /v1/conversations/reply
  -> ConversationService
  -> DeepSeekClient
  -> McpToolRuntime
  -> stdio web-mcp-server (web_search / web_fetch)
  -> ConversationReply
  -> 动作目录校验 / 情绪更新 / AvatarActionCoordinator
  -> GameHud 纯文字字幕
```

### 2. Unity 核心类

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ConversationGatewaySettings.cs`
  是可序列化网关配置，只保存端点、请求超时和 Development 本机 HTTP 开关。
  生产端点必须是 HTTPS；HTTP 仅在 `Debug.isDebugBuild`、显式启用且 URI 为 loopback
  时放行。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/HttpLLMProvider.cs`
  为每个应用会话创建 `session_id`，每次请求创建 `request_id`，并发送历史、当前
  `ExposeToAI` 动作、未占用物品目标和情绪 ID。它不持有 DeepSeek key，也不理解
  角色或物品名称。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Gameplay/Conversation.cs`
  中的 `ConversationOrchestrator` 是对话状态唯一所有者。它负责取消前一请求、
  30 秒会话超时、回复长度校验、动作/目标能力二次校验、情绪信号限幅和历史追加。
  AI 动作通过 `AvatarIntent(IntentSource.AI)` 提交到同一个协调器。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs`
  的“角色字幕”是独立 `Text`，位于 `y=174` 的底部导航上方，没有 `Image`、
  文本框或射线目标。对话面板只负责输入命令；回复不再显示在气泡中。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/DialogueDeviceProbe.cs`
  只在 `DEVELOPMENT_BUILD || UNITY_EDITOR` 编译，并只在 Development Build 由
  `GameBootstrap` 装配。它读取
  `Application.persistentDataPath/dialogue-probe-request.txt`，调用正式
  `ConversationOrchestrator`，原子写出 `dialogue-probe-result.json`。

### 3. 网关与 MCP 职责

网关目录：`/Users/bytedance/MateSoul/services/ai-gateway`。

- `src/app.ts`：Express HTTP 边界、可选 Bearer 认证、请求体上限、健康检查、
  稳定错误响应和不含用户内容的 schema 拒绝日志。
- `src/contracts.ts`：Zod 请求/回复契约。动作与目标必须来自 Unity 当轮目录；
  情绪信号只能使用当轮注册 ID。Unity `JsonUtility` 把空目标能力编码为空串，
  仅在此输入边界将其规范化为 `null`。
- `src/conversation-service.ts`：组装角色提示和动态能力目录；每轮向模型提供 MCP
  工具；执行模型选中的并行工具调用；限制工具轮次；必要时只进行一次 JSON 协议修复。
- `src/deepseek-client.ts`：DeepSeek HTTP、超时合并、`response_format=json_object`、
  `tool_choice=auto` 和响应 schema 校验。API key 仅通过构造参数来自进程环境。
- `src/mcp-runtime.ts`：通过官方 MCP SDK 的 `StdioClientTransport` 启动 Web MCP，
  动态读取工具目录并把 MCP schema 转成模型 function tools；限制返回文本长度。
- `src/mcp/web-server.ts` 与 `src/mcp/web-tools.ts`：提供只读 `web_search`、
  `web_fetch`。网页读取限制协议、DNS 结果、响应字节数、正文字符数和超时；
  loopback、私网、链路本地等地址在请求前拒绝，页面脚本不会执行。

DeepSeek 最终回复协议为 `reply_text + action + mood_signals`。服务端先按当轮目录
验证，Unity 收到后再次验证；任一层不满足都不会直接写 Animator 或 Transform。

### 4. 配置与密钥边界

- `/Users/bytedance/MateSoul/services/ai-gateway/config/gateway.json`：
  服务监听、DeepSeek 模型/超时/工具轮次、历史和回复上限、网页工具边界。
- `/Users/bytedance/MateSoul/services/ai-gateway/config/character.json`：
  冷月身份、表达风格、联网核实和动作陈述约束。
- `/Users/bytedance/MateSoul/services/ai-gateway/.env.local`：
  本机 `DEEPSEEK_API_KEY`，已被 `.gitignore` 排除。不得复制到 C#、Unity Asset、
  JSON 配置、日志或 APK。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/ConversationGateway.asset`：
  当前 Development 指向 `http://127.0.0.1:8787/v1/conversations/reply`。
  PICO Emulator 需执行 `adb reverse tcp:8787 tcp:8787`。
- 正式部署必须替换为 HTTPS 地址，并在
  `server.clientTokenEnvironmentVariable` 配置服务端令牌环境变量；本地健康检查
  不受 Bearer 认证影响。

完整启动和验证命令见
`/Users/bytedance/MateSoul/services/ai-gateway/README.md`。

### 5. 请求流程与状态

1. `ConversationOrchestrator` 取消仍在执行的旧请求，状态切换为 `Thinking`。
2. 从 `ActionCatalog`、`ItemRegistry`、`MoodSystem` 读取当前能力快照。
3. `HttpLLMProvider` 生成请求 ID，通过项目网关发送，不接触供应商凭据。
4. 网关把 `web_search`、`web_fetch` 自动提供给 DeepSeek。模型可以直接回答，
   也可以选择一个或多个工具；每次调用记录请求 ID、工具名、耗时相关结构化事件。
5. 网关校验最终 JSON 及动作、目标、情绪业务不变量后返回。
6. Unity 再次校验回复。合法动作交给 `AvatarActionCoordinator`，情绪交给
   `MoodSystem`，文本写入会话历史并由 HUD 字幕读取。
7. 取消、超时、HTTP 或协议失败分别进入 `Cancelled` 或 `Failed`，不会追加伪历史，
   也不会提交动作。

### 6. 验证与避坑

- 网关测试 `11/11` 通过，`npm audit` 为 0 vulnerabilities。
- Unity TestRunner `57/57` 通过：
  `/Users/bytedance/MateSoul/.artifacts/framework/unity-tests.xml`。
- Development APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  82,304,569 字节，SHA-256
  `9b3a12b1707d03c56baf7028f52559be405e461892f74b078ff14fb7fba2d71c`。
- 最终设备请求 ID `1f80d734802f46fb9a4b69fc377e596a`，网关耗时 5,232 ms，
  3 次模型调用、5,310 tokens，自动调用 `web_search` 和 `web_fetch`；
  Unity 状态为 `Replied`，设备画面确认纯文字位于按钮上方且无背景。
- 结构化报告：
  `/Users/bytedance/MateSoul/.artifacts/framework/dialogue-e2e-validation.json`。

已确认的避坑：

- Android 的 `Application.persistentDataPath` 位于
  `/sdcard/Android/data/<package>/files`，不是 `run-as` 的内部 `files/`。
- `adb install -r` 可能保留旧应用进程。安装新 APK 后必须 force-stop 并冷启动，
  否则验证的仍可能是旧 IL2CPP 代码。
- Unity `JsonUtility` 会把空字符串和缺失能力的线协议表现混在一起；规范化只应在
  HTTP 输入边界完成，不要让空串进入领域层。
- 工具轮次上限是必要的故障闭合边界。模型达到上限时返回失败，不应为了单条问题创建
  站点名、问题文本或角色名特判。
- PICO Emulator 已覆盖真实 DeepSeek/MCP 外网路径；物理头显的弱网、断网、证书和
  正式 HTTPS 网关仍需单独验收。

## Phase Framework F13 技术交接：现实物品与交互能力解耦（2026-10-04）

### 1. 分层模型

现实世界交互现在分为四层：

```text
PICO PxrSemanticLabel
  -> RealitySemanticCatalog（SDK 适配与完整分类）
  -> RoomMapSnapshot.RealityItems（房间物品观测）
  -> RealityItemDirectory（实例目录 + 声明交互）
  -> 专用几何验证器发布 available capability
  -> ItemRegistry / ActionCatalog / AvatarActionCoordinator
```

语义、实例和动作可用性不能合并：

- 语义目录回答“SDK 能识别哪些类别”。
- `RealityItemObservation` 回答“当前房间观测到了什么、在哪里、多大”。
- `RealityInteractionCatalog` 回答“产品上该类别可以对应什么交互”。
- `AvailableInteractions` 回答“当前实例是否已通过该交互的几何和安全验证”。

因此床可以声明 `lie`，但在床面解析器和 `LieOnBedAction` 完成前不会出现在
`ItemRegistry` 的可执行目标中。

### 2. 语义目录

文件：
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/RoomNavigation/Runtime/RealitySemantic.cs`。

`RealitySemanticCatalog` 显式覆盖 PICO SDK 当前 23 个枚举值。静态初始化会比较
项目目录值与 `PxrSemanticLabel`，SDK 新增或删除语义时直接失败，要求开发者明确分类。

当前 `Item` 角色共 15 类：

```text
Door, Window, Table, Sofa, Chair, Curtain, Cabinet, Bed, Plant,
Screen, Refrigerator, WashingMachine, AirConditioner, Lamp, WallArt
```

Floor、Ceiling、Wall、Opening、Stairway 属于结构面；Human 属于人物；
VirtualWall 属于 SDK 生成的虚拟边界；Unknown 不进入现实物品目录。

### 3. 房间观测

`RoomMapSnapshot` 新增：

```csharp
public IReadOnlyList<RealityItemObservation> RealityItems { get; }
```

`RealityItemObservation` 包含 `Id`、`RealitySemantic`、`Center`、`Rotation`、
`Size`。三个 provider 的行为一致：

1. `PicoMockRoomMapProvider` 按目录声明的 `Box2D/Box3D` 读取 Mock 数据。
2. `PicoSpatialMeshRoomMapProvider` 按 mesh block 和顶点语义聚合 AABB。
3. `PicoSceneCaptureRoomMapProvider` 按 Scene Anchor 语义读取 Box2D 或 Box3D。

导航障碍仍使用 `RoomObstacleKind`，现实业务类型使用 `RealitySemantic`。
不要重新把二者合并：同一个床既可以是导航障碍，也可以是现实物品实例，但两者职责不同。

Unity `JsonUtility` 会将缺失的嵌套 Box 字段表现为零值对象，不能只判断 `null`。
Mock provider 必须依据 `RealityGeometryKind` 选择 Box2D 或 Box3D，否则床可能误走
门窗分支并产生零尺寸观测。

### 4. 交互目录与实例目录

文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RealityInteractionCatalog.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RealityItemDirectory.cs`

当前交互映射：

```text
Chair -> sit
Bed   -> lie
```

`RealityItemDirectory.Load` 只加载 SDK 观测，不发布动作 capability。
交互模块完成实例级校验后调用 `PublishInteraction`；撤回时调用
`WithdrawInteraction`。目录会校验：

1. interaction 是否声明支持该语义；
2. ID 与尺寸是否有效；
3. ID 是否撞到非目录所有的 `ItemRegistry` 实例；
4. capability 撤回后，现有预约是否立即失效。

`RealityItemRecord` 同时暴露 `DeclaredInteractions` 和 `AvailableInteractions`。
前者来自产品映射，后者来自运行时验证，不要用前者绕过几何检查。

### 5. 椅子模块迁移

`RealChairScanner` 不再持有或直接修改 `ItemRegistry`，只持有
`RealityItemDirectory`。识别稳定且 `SeatGeometry.HasClearance` 通过后发布：

```text
semantic = Chair
interaction = sit
geometryInferred = true/false
```

网格失效、姿态变化或净空变化时撤回 `sit`，运行中的 `ItemReservation` 随即失效，
`SitOnChairAction` 按原有退出流程站回 approach pose。

`GameSession.ConfigureRealityItems` 是现实交互装配入口：

1. 加载 `RoomMapSnapshot.RealityItems`；
2. 创建当前已实现的 `RealChairScanner`；
3. 从 `RealityInteractionCatalog` 读取 `sit` 定义并注册 `ActionDefinition`；
4. 启动扫描器。

添加床交互时应新增床目标解析器和 `LieOnBedAction`，然后在该入口注册模块；
不要在中央协调器添加物品名称或语义 `switch`。

### 6. 验证

- Unity TestRunner：`60/60` 通过。
- APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_RealChair_Development.apk`，
  通用交付副本 `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  SHA-256 `bff6959e693877386377151e82b9183f39d7a11848fdaded5ff682ed0b0813d4`。
- PICO Emulator：28 个现实物品实例；两张床只声明 `lie`，无可用 capability；
  唯一几何验证椅子发布 `sit`。坐下/起身回归通过。
- 报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/reality_item_modularization_20261004.json`。

物理 PICO 头显和 Scene Capture provider 尚未完成本阶段的设备验证。

## Phase Framework F14 技术交接：角色人设与运行时上下文（2026-10-04）

### 1. 数据分层

本阶段把 AI 角色信息明确拆成三个生命周期：

1. **静态人设**：身份和性格目录，来源为
   `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/LengyuePersona.json`。
2. **玩家选择**：五个性格 option ID，持久化在 `GameSaveData.personality`。
3. **运行时上下文**：衣装、情绪、姿态、活动 Action 与近期事件，只存在于当前
   `GameSession` 的内存快照。

长期对话记忆不属于以上三者。本阶段没有把近期 Action 事件伪装成长期记忆。

### 2. 人设领域模型

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Gameplay/CharacterContext.cs`
定义纯 C# 契约：

- `CharacterPersonaDefinition`：版本、角色 ID、固定身份和五个性格维度。
- `PersonalityTraitDefinition/PersonalityOptionDefinition`：UI 名称与 AI 行为说明。
- `CharacterPersonaState`：校验定义、恢复玩家选择、循环选择、输出持久化数据与
  `CharacterPersonaSnapshot`。
- `CharacterRuntimeSnapshot`：revision、appearance、mood、posture、actions 和
  recent_events。
- `ICharacterContextProvider`：对话层唯一读取接口。

固定身份由领域边界强校验：`schema_version=1`、年龄 16、性别 `female`、身高
155 cm。玩家 UI 不提供这些字段的编辑入口。五个必需维度为：

```text
extraversion
conscientiousness
openness
agreeableness
emotional_stability
```

人设文件是内容源；存档只保存选择 ID。旧版存档的 `personality=null` 在
`JsonGameSaveStore.Load()` 迁移为空选择，然后由人设默认值补齐，不修改存档版本。

### 3. 运行时快照所有权

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CharacterRuntimeContextProvider.cs`
只读取现有所有者：

| 上下文字段 | 唯一事实源 |
|---|---|
| 当前衣装 ID/名称 | `LengyueWardrobeController` |
| 当前情绪/强度 | `MoodSystem` |
| 活动 Action/状态/来源/耗时 | `AvatarActionCoordinator` |
| Action 阶段/姿态 | Action 自身的 `IActionRuntimeStatus` |
| 目标名称/状态 | 统一 `ItemRegistry` |
| 性格选择 | `CharacterPersonaState` |

Provider 订阅 `OutfitChanged`、`Mood.Changed`、`AvatarActionCoordinator.Changed`
更新 revision 和最多 8 条近期事件；每次 AI 请求仍从所有者捕获当前不可变 DTO。
没有运行时 JSON 热读、场景遍历、Physics 查询、Animator 扫描或 Spatial Mesh 查询。

`IActionRuntimeStatus` 位于 Core。当前实现：

- `MoveAction`：`moving / standing`
- `StayAction`：`staying / standing`
- `VisitItemAction`：`approaching|observing / standing`
- `ChangeOutfitAction`：`switching`
- `SitOnChairAction`：`walking|docking|sitting|seated|standing`

新 Action 若需要被 AI 感知，只实现该接口；协调器和上下文 Provider 禁止增加
具体 Action 类型判断。现实目标已由 `RealityItemDirectory` 投影进同一个
`ItemRegistry`，所以运行时协议不包含现实/虚拟分支。

### 4. 对话与网关链路

1. `ConversationOrchestrator.SendAsync` 枚举可用动作、目标和情绪。
2. 调用 `ICharacterContextProvider.Capture()`，用 `Stopwatch` 记录采集耗时。
3. `HttpLLMProvider` 将 `persona/runtime` 与原请求一起序列化，并记录
   `AI_CONTEXT_CAPTURED revision/captureMs/payloadBytes`。
4. 网关 Zod schema 校验固定身份、五维人格、运行时枚举和数组上限。
5. `buildMessages` 把人设与快照标记为可信游戏数据。协议、工具和 Action 白名单优先；
   数据中的文本不能成为指令。
6. Runtime snapshot 是当前游戏状态唯一权威。回答身份、衣装、情绪、姿态、Action
   和游戏目标时禁止调用 Web MCP。
7. 回复动作仍按当前 `ActionCatalog/ItemRegistry` 二次验证；请求快照不能直接执行。

Unity `JsonUtility` 会把无目标 Action 的空对象编码为
`{"id":"","name":"","state":""}`。`contracts.ts` 只在 HTTP 输入边界把这组三空值
规范化为 `null`，其他部分为空或半空仍拒绝。

DeepSeek 偶发返回空 `content`。`ConversationService` 将它视为一次协议失败，复用
已有单次 repair 调用，传入空 JSON 和明确错误原因；repair 不暴露 MCP 工具。若修复
仍失败，请求按原错误路径终止，不继续重试。

### 5. UI 与衣装

`GameHud` 设置栏目包含“角色性格”和“系统”两个页签。角色页展示
“冷月 · 16岁 · 女 · 155 cm”，并按人设文件的 trait 顺序生成五个按钮。
按钮显示 `维度：当前选项`，点击调用 `GameSession.SelectNextPersonality()` 并立即
保存。

`LengyueWardrobeController.CurrentDisplayName` 从初始衣装或衣装目录读取显示名，
不按 outfit ID 写名称分支。当前 AI 可见衣装：

- `lavender-casual`：薰衣草常服
- `pink-pajamas`：粉格睡衣与睡帽

### 6. 性能与验证

未预热 APK 的首次 Capture 为 39.057 ms，随后为 3.180、0.508、0.210 ms，
说明成本来自首次泛型/快照路径初始化。`GameSession` 现在于装配阶段调用一次
`Capture()` 预热。最终 APK 冷启动后的真实结果：

```text
identity turn: capture 0.082 ms, payload 2,389 bytes
active-action turn: capture 0.233 ms, payload 3,082 bytes
budget: capture P95 < 2 ms, payload < 4,096 bytes
```

- Unity TestRunner：63 passed、0 failed、0 skipped。
- 网关测试：13 passed；npm audit 0 vulnerabilities。
- APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  82,330,843 字节，
  SHA-256 `485f5664dfc5896457c318b4724eefcaf31b2dde3233e66b158bc5ec11b44d93`。
- 报告：
  `/Users/bytedance/MateSoul/.artifacts/framework/persona-runtime-context-validation.json`。

后续新增衣装只扩展 Wardrobe 定义；新增行为只注册 Action 并实现可选状态接口；
新增情绪继续扩展 MoodSystem。不得在 Prompt 或上下文 Provider 中增加角色名、
衣装 ID、Action ID 或物品名称分支。

## Phase Framework F15 技术交接：AI 网关开发启动链路（2026-10-04）

### 1. 故障边界

Unity Development 配置使用
`http://127.0.0.1:8787/v1/conversations/reply`。该地址在 Unity Editor 中直接访问
宿主机；在 PICO Emulator 中依赖 ADB reverse。出现
`对话网关请求失败：HTTP 0 / Cannot connect to destination host` 时，依次检查：

1. `curl -fsS http://127.0.0.1:8787/health`；
2. Emulator 场景检查 `adb -s emulator-5554 reverse --list`；
3. 查看 `/Users/bytedance/MateSoul/.dbg/ai-gateway/gateway.log`。

不要把网关不可用改成客户端直连 DeepSeek，也不要把 API key 写入 Unity。

### 2. 网关启动器

`/Users/bytedance/MateSoul/scripts/start-ai-gateway.command` 是本地开发环境的网关
启动入口，执行顺序如下：

1. 调用 `/health`，仅当响应标识
   `service=matesoul-ai-gateway` 时复用现有实例；
2. 若 8787 被其他进程占用则 fail-closed；
3. 只检查 `.env.local` 是否配置 `DEEPSEEK_API_KEY`，不打印值；
4. 执行 `npm run build`；
5. 通过 `nohup node dist/src/index.js` 后台启动并记录 PID；
6. 最多等待 20 秒，健康检查未通过时终止进程并返回失败。

运行状态：

```text
log: /Users/bytedance/MateSoul/.dbg/ai-gateway/gateway.log
pid: /Users/bytedance/MateSoul/.dbg/ai-gateway/gateway.pid
health: http://127.0.0.1:8787/health
```

### 3. Emulator 集成

`/Users/bytedance/MateSoul/scripts/start-pico-emulator.command` 在启动 PICO Emulator
前同步调用网关启动器，然后后台等待 `emulator-5554` 上线并执行：

```bash
/Users/bytedance/MateSoul/.pico-home/6.2/emulator/system-images/platform-tools/adb \
  -s emulator-5554 reverse tcp:8787 tcp:8787
```

因此正常开发只需运行 Emulator 启动脚本。Unity Editor 单独运行时，可只执行
`start-ai-gateway.command`。

### 4. 验证证据

- `bash -n`：两个 `.command` 文件均通过。
- 网关测试：13 passed、0 failed。
- `/health`：返回 `status=ok` 及 `web_search/web_fetch`。
- 真实 DeepSeek + MCP 请求：
  `a5d0cf64-e52d-49d4-9c1e-10c580218b17`，依次完成 `web_search` 和
  `web_fetch`，4.123 秒返回，请求和响应 ID 一致。

## Phase Framework F16 技术交接：现实床目标与躺卧执行（2026-10-04）

### 1. 阶段概述

本阶段将 F13 中仅声明的 `Bed → lie` 产品映射实现为可执行能力。系统只消费 PICO
Unity SDK 提供的原生 Spatial Mesh；床面、床头、完整支撑、床尾接近点和动作扫掠净空
全部通过后，`RealityItemDirectory` 才向统一 `ItemRegistry` 发布 `lie`。

运行时所有权保持不变：

1. PICO XR 提供房间网格和语义。
2. `RealityMeshSource` 唯一持有网格订阅与三角面快照。
3. `RealBedScanner` 决定床目标和 capability 是否可用。
4. `AvatarActionCoordinator` 决定动作调度和 FullBody 控制权。
5. `LieOnBedAction` 编排导航、接触和退出。
6. `AvatarMotor` 唯一写角色根节点，`AvatarLyingRig` 只写骨骼接触姿态。

### 2. 共享网格源

文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RealityMeshSource.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/SeatGeometry.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RealChairScanner.cs`

`RealityMeshSource` 将 `PxrSpatialMeshInfo` 转换为通用 `RealityTriangle`，字段为世界空间
三个顶点、语义 label 和 source ID。椅子与床扫描器订阅其 `Changed` 事件，不得各自
启动或停止 `XRMeshSubsystem`。

Spatial Mesh 的逐顶点语义优先级高于 Scene Capture 包围盒。仅当 Scene Capture
快照没有 SurfaceMesh 时，才允许用 Box2D/Box3D 补充 Unknown。粗 AABB 不能覆盖已有
逐顶点语义，否则家具包围盒重叠会污染相邻物体。

### 3. 床几何解析

文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/BedGeometry.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RealBedScanner.cs`

`BedGeometry.Extract` 的处理顺序：

1. 按 source ID 收集 Bed 三角面，并在 source 内按共享边形成连通组。
2. 过滤近水平、处于合理床面高度且面积足够的承托面。
3. 在 XZ 平面拟合最小面积 OBB，验证宽度、长度、平面误差与网格覆盖率。
4. 将高度、中心和朝向重叠的床架/床品 source 合并为一个目标。
5. 统计两端床面以上的结构面积，以床头板较大一端确定床头；无法明确判向时拒绝。
6. 从所有合并 source 的投影边界计算 `FootEdge`，避免 Dock 落在床架延伸部分。
7. 使用 Triangle-AABB SAT 检查人体占用区及躺下/起身扫掠区，目标自身 source
   证明支撑但不作为外部障碍。

`BedTarget` 的关键数据：

- `Center/Forward/Width/Length`：躺卧平面及床头方向。
- `FootEdge/FloorY`：真实床尾和站立地面。
- `Dock`：床尾外 0.48 m 的站立点。
- `ContactRoot`：床中心向床尾偏移 `Length × 0.42`，床面以上保留 0.035 m。
- `LyingRotation`：角色局部前方朝世界上方，局部上方朝床头。
- `SourceIds/PlaneError/StableSamples/Available`：来源、质量和可用状态。

`RealBedScanner` 连续 3 个样本匹配且 `HasClearance` 通过后发布 `lie`。目标消失、
姿态变化或净空失败时立即撤回 capability；运行中的 `ItemReservation` 随即失效。

### 4. 躺卧动作与接触 Rig

文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/LieOnBedAction.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarLyingRig.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarMotor.cs`

动作阶段为：

```text
walking -> docking -> lying -> resting -> rising
```

`Check` 会锁定床目标副本，验证 capability、占用状态、当前净空、角色尺寸和床尾
NavMesh。采样点与期望 Dock 的水平偏差不得超过 0.12 m，高度偏差不得超过 0.08 m。

`Start` 取得 Item reservation 并执行共享 `MoveAction`。Dock 完成后，
`AvatarMotor.BeginBodyContact` 禁用站立 CharacterController；随后在 2.2 秒内插值根
位置和旋转，`AvatarLyingRig.SetPose` 同步将权重提升到 1。Rig 在 Animator 之后锁定
22 根中性骨骼，并把裙摆支撑交给现有 `SkirtPoseGuard`。

主动 `stay` 或更高优先级动作会触发 `rising`，在 1.8 秒内反向恢复床尾站姿。
床 reservation 或净空失效同样先安全起身，但最终 Action 状态为 `Failed`。
`Dispose` 是唯一清理边界：释放 Rig、恢复 CharacterController 并释放 reservation。

### 5. 装配与扩展约束

`GameSession.ConfigureRealityItems` 创建一个 `RealityMeshSource`，随后装配
`RealChairScanner` 和 `RealBedScanner`，并从 `RealityInteractionCatalog` 注册
`sit`、`lie`。`GameSceneBuilder.BindContactRigs` 在角色 Prefab 上绑定
`AvatarSeatingRig` 与 `AvatarLyingRig`。

UI、AI 和 Development Probe 只能提交：

```text
AvatarIntent("lie", source, bedTargetId)
```

不得直接调用床扫描器驱动角色，不得直接写 Animator/Transform，也不得在协调器中添加
床名称或角色名称分支。新增现实交互应继续采用“语义声明 → 专用几何验证 →
目录发布 capability → 注册 ActionDefinition”的扩展路径。

### 6. 验证与资源

- Unity TestRunner：68 passed、0 failed、0 skipped。
- 原生输入：PICO Emulator Spatial Mesh，86,323 个三角面，无合成床 fixture。
- 唯一床目标：`1.910 × 2.095 m`，平面误差 `0.0016 m`，两个重叠 source 合并。
- 躺稳：Rig 权重 1、CharacterController 关闭、FullBody 租约持有。
- 起身或目标丢失：Rig 权重 0、CharacterController 恢复、FullBody 租约释放。
- APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_RealBed_Development.apk`，
  SHA-256 `936283143940c28d200ed47e7e4729df9ec62b41a9953af72252b53921cc200e`。
- 视频：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/matesoul_native_room_bed_complete_20261004.mp4`。

## Phase Framework F18 技术交接：站立 Dock 后直接切换床面（2026-10-04）

### 1. 当前状态机

F17 的弯腰方案已按产品决定移除，当前唯一有效流程为：

```text
walking -> docking -> mounting -> lying -> resting -> rising
```

- `walking`：执行冻结的 `BedMountPlan` 路径。
- `docking`：精确到达所选床沿并面向床内。
- `mounting`：完整保持一帧站姿，`lyingWeight=0`、CharacterController 启用。
- 下一 Tick：调用 `BeginBodyContact`，一次性写入床面
  `ContactRoot/LyingRotation`，并将 `lyingWeight` 直接设为 1。
- `rising`：一次性回到原 MountPlan Dock；下一 Tick 完成清理并恢复站立碰撞体。

不再存在 `preparing`、`PreparationWeight` 或 0 到 1 的躺卧位置/旋转插值。

### 2. 最近安全床沿

`BedMountPlanner` 仍从 `Left/Right/Foot` 三个候选中选择完整 NavMesh 路径最短者。
候选点位于实际床架外缘之外 0.36 m：

```text
CharacterController radius: 0.30 m
additional clearance:       0.06 m
total edge clearance:       0.36 m
```

`HasMountClearance` 现在只检查竖直站立胶囊周围的外部障碍，不再预留躯干前倾扫掠区。
床面人体区和躺卧净空仍由 `HasClearance` 独立验证。

### 3. 不变量

- 到达 `mounting` 前，角色根节点必须保持竖直，且不得禁用 CharacterController。
- `mounting` 到 `lying` 只能发生一次离散根节点切换。
- 不允许任何 `0 < lyingWeight < 1` 采样。
- 左右床沿、床尾、局部地面、NavMesh 路径和外部净空仍按运行时数据求解。
- UI、AI 和 Probe 继续只提交 `AvatarIntent("lie", source, bedTargetId)`。

### 4. 验证资源

- Unity TestRunner：71 passed、0 failed、0 skipped。
- 原生 PICO Emulator mesh：86,323 triangles。
- 最终床尾 Dock：`(-0.137, 0.447, 1.279)`。
- APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  SHA-256 `756b265ddfdcc92f55f1098b52c510a534d28311c5548cc7bfbef132e2bb8f84`。
- 报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/real_bed_lying_20261004.json`。
- 视频：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/matesoul_native_room_bed_complete_20261004.mp4`。
- 报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/real_bed_lying_20261004.json`。

物理 PICO 头显仍需验证真实床垫接触高度、透视遮挡、运行中移动家具和 Scene Capture
Box3D 朝向。模拟器中的手动 `Camera.main` 位移不会同步房间合成视角，不能用该方式
生成接触高度证据。

## Phase Framework F17 技术交接：BedMountPlan 与离散上床过渡（2026-10-04）

### 1. 修复目标

F16 的床识别与安全检查有效，但动作表现错误：角色从床尾地面到床面期间同时插值位置
和仰卧旋转，导致远看像“躺着漂过去”。本阶段把床目标、接近规划和动作表现拆为三个
独立层次：

1. `BedTarget` 描述稳定床面、方向、高度及床架外缘。
2. `BedMountPlanner` 在动作准入时选择实际可达的上床边。
3. `LieOnBedAction` 只执行冻结后的计划，并使用弯腰加离散位置切换。

### 2. BedTarget 外缘

文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/BedGeometry.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RealBedScanner.cs`

`BedTarget` 现在包含：

- `FootEdge`：所有合并床 source 沿床尾方向的最大投影。
- `LeftEdge/RightEdge`：中央身体区沿床宽方向的实际外缘。
- `FloorY`：局部站立地面。
- `Center/Forward/Width/Length`：床面 OBB。

床头板和一体式床头柜可能明显宽于床垫。左右外缘只统计满足
`abs(longitudinalProjection) <= Length × 0.42` 的顶点；床头端的宽结构不会误判床，
也不会把中段侧边 Dock 推到远处。Emulator 原生床实测：

```text
surface: 1.910 × 2.095 m
foot edge: 1.540 m
left/right central edge: 1.116 / 1.103 m
surface height above floor: 0.429 m
```

### 3. BedMountPlanner

类型：

- `BedMountEdge`：`Left`、`Right`、`Foot`。
- `BedMountCandidate`：理想 Dock、朝床内方向和站立旋转。
- `BedMountPlan`：选中的边、NavMesh 实际 Dock、旋转和路径长度。
- `BedMountPlanner.TryCreate`：动作层唯一规划入口。

每个候选点位于对应床架外缘再向外 0.48 m。Planner 按以下顺序筛选：

1. 在理想点 0.22 m 内采样 NavMesh。
2. 水平误差不超过 0.14 m，采样高度与 `FloorY` 相差不超过 0.12 m。
3. 从角色当前位置到 Dock 必须存在完整路径。
4. `BedGeometry.HasMountClearance` 使用与接近方向对齐的 OBB 检查站立和弯腰空间。
5. 在所有通过项中选择完整路径长度最短的方案。

`RealBedScanner.HasClearance` 只负责床面人体区域；它不依赖 NavMesh，因此仍可稳定发布
床 capability。具体可达边必须在 `LieOnBedAction.Check` 中按角色当时位置计算。
计划一旦通过准入即冻结，移动过程中不切换左右边，避免路径抖动。

### 4. 动作状态机

文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/LieOnBedAction.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarLyingRig.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarMotor.cs`

新阶段：

```text
walking -> docking -> preparing -> mounting -> lying -> resting -> rising
```

执行规则：

1. `walking` 使用共享 `MoveAction` 沿完整 NavMesh 路径移动。
2. `docking` 只做小范围位置校正，并让角色面向床内。
3. `preparing` 保持角色根节点、旋转和 CharacterController 不变，只提高
   `PreparationWeight`。
4. `mounting` 是显式帧屏障。即使设备单帧 `dt` 大于准备时长，也至少完整渲染一帧
   满权重弯腰姿态。
5. 下一 Tick 才调用 `BeginBodyContact`，并通过 `AvatarMotor.SetContactRoot` 一次性
   写入 `ContactRoot/LyingRotation`；没有 Dock 到床面的 `Lerp/Slerp`。
6. `lying/resting` 锁定中性仰卧骨骼和裙摆支撑。
7. `rising` 先一次性回到原 MountPlan 的 Dock 和弯腰姿态，至少保留一帧后解除弯腰；
   `Dispose` 最终恢复 CharacterController 和释放 reservation。

运行时不变量：

- `walking/docking/preparing/mounting`：`posture=standing`、`lyingWeight=0`、
  CharacterController 启用。
- `lying/resting`：`posture=lying`、`lyingWeight=1`、CharacterController 禁用。
- 不允许 `0 < lyingWeight < 1`；该范围表示旧漂移实现回归。
- 角色根节点仍只能由 `AvatarMotor` 写入。

### 5. 高度自适应弯腰

`AvatarLyingRig.SetPreparation` 不绑定某张床或某个 Animator Clip。`LateUpdate` 根据
床面相对局部地面的高度，将总前倾角在 52° 到 26° 之间插值，并按 30%/70% 分配到
hips 和 spine。低床需要更深弯腰，高床使用较小角度。

该姿态是没有爬床动画时的表现降级，不承担碰撞或路径规划。未来接入通用上床动画时，
保持 `BedMountPlan` 和状态阶段不变，只替换 `preparing/mounting` 的 Rig 表现。

### 6. 验证

- Unity TestRunner：71 passed、0 failed、0 skipped。
- 参数覆盖：床面高度 0.42/0.84 m、床宽 0.95/1.90 m、床长 2.0/2.3 m。
- 侧边规划用例证明床尾与另一侧不可用时可以选择剩余侧边，并始终面向床内。
- 远距离 PlayMode 用例逐帧断言到达前保持站姿、存在完整 mounting 帧、无分数
  `lyingWeight`，并验证主动起身和目标丢失恢复。
- PICO Emulator 原生 mesh：86,323 个三角面；实际选择 `Foot`，从 2.66 m 外步行到
  Dock，弯腰后单帧切换至床面。
- APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  82,359,836 字节，SHA-256
  `37461471ec6a0988ece12158f684b72d0145723a1ae166d486ce9c2c7285b90b`。
- 报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/real_bed_lying_20261004.json`。
- 视频：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/matesoul_native_room_bed_complete_20261004.mp4`。

## Phase Framework F19 技术交接：电视交互与视频框架（2026-10-04）

### 1. 当前状态

源码、资产生成和场景目录写入均已完成。电视专项测试 `5/5`、全量框架测试
`76/76` 通过；Android APK 已重建。接入说明见
`/Users/bytedance/MateSoul/项目沉淀资料/Television.md`。

### 2. 组件与所有权

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/TelevisionController.cs`：
  电源、视频解码、空间音源和 RenderTexture 的单一所有者。状态为
  `Off/NoSignal/Preparing/Playing/Paused/Ended/Error`。
  `SetPower` / `TogglePower` 控制电源，`SetVideoClip` / `SetVideoUrl` 负责换源；
  `StateChanged` 用于观察状态，`LastError` 保留最近播放错误。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/TelevisionPowerButton.cs`：
  继承 Unity `Button`，复用 XRUIInputModule 的扳机点击和 Editor 鼠标事件。
  48 mm 透明点击区域固定在机壳前；只有按钮网格下压 3 mm，按下/回弹约为
  55/120 ms。悬停使用 MaterialPropertyBlock 的发光值，无新增描边。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/Shaders/TelevisionScreen.shader`：
  原 CRT 网格 UV 上的 URP unlit 视频显示，含 XR instancing/stereo 宏和开关渐变。
  解码输出为每台电视独立的 1024×738 RT，使用 `FitInside` 保持源比例。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/TelevisionBuilder.cs`：
  导入原电视副本，校验 0.480×0.5657×0.450 m 总尺寸（含天线），
  将前面统一为物品局部 -Z，绑定独立按钮、屏幕、LED、UI 和播放器。
  物品目录只声明现有角色 `visit` 能力；电视开关不改角色动作协调器。

### 3. 生成与视频接入

1. `MateSoul > Game > Build Game Scene` 已完成执行。
2. 构建器已生成
   `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/Television.prefab`、
   `Television.asset` 和 `TelevisionMaterials/`，并注册到游戏场景的物品目录。
3. 在预制体根节点 `TelevisionController` 设置 `Video Clip`，或在 Clip 为空时填写
   `Video Url`。支持直接 HTTP(S) 媒体地址和设备可读的绝对 file URI。
4. 重建场景复用既有预制体，避免覆盖后续配置的视频源。
5. 未提供资源时开机显示 `NO SIGNAL`，不启动解码器或分配视频 RT。

### 4. 生命周期与框架注意事项

- `Prepare` 异步完成后才 `Play`；默认 30 秒准备超时，错误展示 `VIDEO ERROR`。
  不进行自动重试。再次开关机或换源触发新一次准备。
- 关机先切换 `Off` 再停止播放器，忽略迟到回调；同时停止音频、解绑并释放 RT。
  组件停用和物品销毁走同一清理流程。电视电源/播放进度不写入现有布局存档。
- 应用挂起暂停播放；恢复后继续播放。每实例使用独立 MPB 与 RT，避免多台电视串画面。
- `Selectable` 自带 `ExecuteAlways`。派生按钮通过 `Application.IsPlaying` 区分运行实例，
  防止 Editor 中生成/销毁预制体时误写按钮网格位置。
- `MaterialPropertyBlock` 不能在 MonoBehaviour 字段初始化器中创建，必须在 `Awake`
  创建；否则 Editor 构建预制体时触发原生 `CreateImpl` 异常。
- 电视 FBX 保留 Blender Z-up 轴。构建器先在检测到 `size.z > size.y` 时绕 X 轴
  `-90°` 直立，再按按钮深度统一正面为局部 -Z；最终尺寸
  `0.480 × 0.566 × 0.450 m`。
- Unity Object 不能用 C# `??` 判断是否缺失。`GameItem.Bind` 的 Rigidbody 和
  XRGrabInteractable 改为显式 `== null`，否则可能拿到 Unity “伪 null”并在写属性时
  抛出 MissingComponentException。
- UI 图形射线开启 3D 遮挡检查，背面不能点击。机身继续由原 `GameItem` 的
  XRGrabInteractable 与箱形 Collider 管理，扳机与握持操作保持独立。

### 5. 验证与交付

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/TelevisionTests.cs`
  包含五个集成用例：鼠标反馈与开关、XR 遮挡/背面、实例隔离和抓取绑定、
  错误与清空源恢复、临时 H.264/AAC 视频的解码/循环/释放，结果 `5/5`。
- 全量 Unity TestRunner：`76/76` 通过。`GameRuntimeTests` 已断言 HUD 生成
  `物品/television` 按钮。
- TRAE 沙箱内直接启动 Unity/Bee 仍会触发
  `mono_os_sem_post: semaphore_signal failed with error 15`。验证流程通过系统 Terminal
  启动 Unity，场景生成、测试和 Android 构建全部成功；这属于工具运行环境问题。
- APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  82,378,113 字节，v2 签名通过，SHA-256
  `f49e8841f6c691f140c5b1faf0f55e852e53a0124354a82d66dad33456b543ad`。
- 当前无连接中的 PICO 模拟器，未执行安装和设备画面验证。临时诊断视频不进入 Assets
  或 APK，正式视频到位后仍需验证目标设备上的编解码兼容性与音视频同步。

## Phase Framework F20 技术交接：APK 指纹同步与模拟器冷启动（2026-10-04）

### 1. 入口与配置

唯一入口：

```text
/Users/bytedance/MateSoul/scripts/start-pico-emulator.command
```

稳定默认值：

```text
APK_PATH=/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk
DEVICE_ID=emulator-5554
AVD=MateSoul_PICO_0_13
```

临时覆盖只提供两个明确边界：

```bash
MATESOUL_APK_PATH=/absolute/path/app.apk \
MATESOUL_DEVICE_ID=emulator-5554 \
/Users/bytedance/MateSoul/scripts/start-pico-emulator.command
```

不引入配置中心或环境变量矩阵。

### 2. 启动状态流

```text
start-ai-gateway
  -> 已连接且完成开机？
     -> 是：同步 APK 并启动应用
     -> 否：已有同名 QEMU 进程？
        -> 是：等待该实例完成开机，再同步并启动
        -> 否：后台等待设备，前台 exec PICO Emulator
  -> 读取 APK package / launchable activity
  -> 比较宿主 APK 与设备 base.apk 的 SHA-256
     -> 相同：跳过安装
     -> 缺失或不同：adb install -r -g，再次校验
  -> adb reverse tcp:8787 tcp:8787
  -> am force-stop
  -> am start -W
```

`wait_for_emulator` 同时要求 `adb get-state` 成功和
`sys.boot_completed=1`，避免 package manager 尚未就绪时安装。

### 3. 最新版本判据

当前 Unity 工程的 `versionCode` 仍为 `1`，连续构建无法靠版本号区分。脚本采用内容
指纹：

1. 宿主使用 macOS `shasum -a 256`。
2. `pm path <package>` 定位设备安装的 `base.apk`。
3. 优先执行设备 `sha256sum`，不传输 79 MB APK。
4. 设备无 `sha256sum` 时，拉取到 `mktemp` 临时目录后计算并立即删除。
5. 覆盖安装后必须再次读取设备 SHA；不一致则 fail-closed，不启动应用。

APK 元数据由最新 Android build-tools 中的 `aapt dump badging` 动态解析。当前解析结果：

```text
package=com.UnityTechnologies.com.unity.template.urpblank
activity=com.unity3d.player.UnityPlayerActivity
```

### 4. 生命周期与失败边界

- `adb install -r` 可能保留旧进程，因此无论是否安装，都执行 `am force-stop` 后冷启动。
- AI 网关保持幂等；每次应用启动前重建 `tcp:8787` reverse，防止模拟器重启后映射丢失。
- APK 不存在、缺少 aapt、元数据不可解析、设备开机超时、安装失败或安装后 SHA 不一致
  均返回非零，不静默启动未知版本。
- 启动与安装记录写入
  `/Users/bytedance/MateSoul/.dbg/pico-emulator/emulator-host.log`。

### 5. 验证证据

- 首次运行：旧设备 SHA
  `756b265ddfdcc92f55f1098b52c510a534d28311c5548cc7bfbef132e2bb8f84`
  自动更新为
  `f49e8841f6c691f140c5b1faf0f55e852e53a0124354a82d66dad33456b543ad`，
  安装结果 `Success`。
- 设备 `base.apk` 与
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk` 哈希一致。
- 两次重复运行均命中 `APK is already current` 分支，未安装，仍完成冷启动。
- `topResumedActivity` 为 MateSoul 的 UnityPlayerActivity；`adb reverse --list`
  显示 `tcp:8787 tcp:8787`。
- 结构化报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/pico_auto_install_20261004.json`。

## Phase Framework F21 技术交接：多模态指向与统一按钮执行（2026-10-04）

### 1. 模式所有权

文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/PlayerInteractionModeController.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/GameSceneBuilder.cs`

`PlayerInteractionModeController` 是远场输入模式唯一所有者：

```text
Controller
  - LeftHand UI Ray: X / primaryButton
  - RightHand UI Ray: A / primaryButton
  - Grip: XRGrabInteractable 抓取

EyeHand
  - PXR_HMD eye gaze: 指向与悬停
  - Right PicoHandInteraction pinch: UI Press
```

两种模式通过射线根节点 active 状态互斥。物理近场按压不属于远场模式，始终可用。

自动模式流程：

1. 查询具备 `Controller | HeldInHand` 特征的 XR InputDevice。
2. 任一设备 `CommonUsages.isTracked=true` 时立即切到 `Controller`。
3. 全部手柄失去追踪 0.75 秒后切到 `EyeHand`，过滤短暂抖动。
4. 设置页以后可调用 `SetMode(mode)` 锁定人工选择；调用
   `ResumeAutomaticSwitching()` 恢复设备自动切换。

### 2. 输入绑定

手柄：

```text
position/rotation/isTracked/trackingState = <XRController>{Left|RightHand}/...
select/selectValue = gripPressed / grip
uiPress/uiPressValue = primaryButton / primaryButton
```

右手 primaryButton 对应 A，左手对应 X。Trigger 不再触发电视或 HUD 的 UI 点击，
但后续可继续分配给其他动作。

眼手：

```text
position = <PXR_HMD>/eyeGazePosition
rotation = <PXR_HMD>/eyeGazeRotation
isTracked = <PXR_HMD>/eyeGazeIsTracked
trackingState = <PXR_HMD>/eyeGazeTrackingState
uiPress = <PicoHandInteraction>{RightHand}/pinchTouched
uiPressValue = <PicoHandInteraction>{RightHand}/pinchValue
```

Gaze controller 不配置 `selectAction`。这条不变量防止用户捏合电视电源键时，
同一帧又触发电视根节点的 XRGrabInteractable。

`GameSceneBuilder.ConfigureInteractionCapabilities` 每次重建场景时把
`PXR_ProjectSetting.handTracking/eyeTracking` 设为 true，并设置
`ControllersAndHands`。`RoomNavigationSceneBuilder` 同步设置 `PXR_Manager.eyeTracking`。
不可只手改 YAML：已加载的 Unity ScriptableObject 会在退出时写回旧内存值。

### 3. 近场物理按钮

文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/PhysicalPressInteractor.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/TelevisionPhysicalPressSurface.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/TelevisionPowerButton.cs`

电视预制体含 `Physical Press Surface`：

```text
trigger size = 0.032 × 0.032 × 0.016 m
press direction = surface local +Z（朝机身内部）
full press depth = 0.006 m
activate threshold = 70%
re-arm threshold = 20%
```

虚拟手接入步骤：

1. 在食指指尖节点添加 `PhysicalPressInteractor`。
2. `pressPoint` 指向实际指尖接触点；为空时使用组件 Transform。
3. 指尖添加小型 Collider，并在手或指尖层级提供运动学 Rigidbody。
4. 不要从虚拟手直接调用电视开关；触发面计算压入深度并调用
   `TelevisionPowerButton.SetPhysicalPress`。

按钮维护每个指尖的压入值和 latch。射线 Pointer、点击回弹脉冲和物理压入取最大值，
共同驱动同一个按钮视觉；跨过阈值只调用现有 `onClick`，因此电视电源仍只有
`TelevisionController` 一个状态所有者。

`TelevisionPhysicalPressSurface` 统计同一指尖的多 Collider 接触数，并在 Collider 离开、
指尖禁用/销毁或表面禁用时调用 `EndPhysicalPress`。`GameItem.Bind` 只向
XRGrabInteractable 注册根节点非 Trigger Collider，避免按压触发区改变整机抓取。

### 4. 权限与构建

最终 APK Manifest 已验证包含：

```text
com.picovr.permission.EYE_TRACKING
com.picovr.permission.HAND_TRACKING
com.picovr.permission.SPATIAL_DATA
```

模拟器中三项权限均 `granted=true`。APK 为 82,379,655 字节，v2 签名通过，
SHA-256：

```text
feca0a0ce5ffcdb3ce9d7868b44491d84e6ed6f82c1798ce35cdde6e32099a83
```

### 5. 验证边界

- `TelevisionTests`：`6/6`，覆盖射线、遮挡、开关、视频、实例隔离和真实 Physics
  触发的虚拟指尖按压。
- 全量 Unity TestRunner：`77/77`。
- 场景序列化检查确认 controller primaryButton、PXR eye gaze、pinchTouched 和
  `Physical Press Surface` 已写入产物。
- PICO Emulator 自动更新后，设备 APK 哈希与宿主一致，应用在前台运行且无 C# 致命异常。
- 当前未通过真实手柄 A 键、物理眼动和真实手部追踪做人工操作验收；模拟器和自动测试
  不能替代物理头显的眼动校准、捏合舒适度及最终虚拟手碰撞体验。
- 结构化报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/interaction_modes_20261004.json`。

## Phase Lavender Chibi Bowling 技术交接：参数化保龄球资产（2026-10-05）

### 1. 阶段概述

本阶段根据三视图交付保龄球和保龄球瓶两套游戏资产。两套模型统一使用米制、
浅紫釉面材质和圆形冷月角色贴花，具备 Blender 源文件、GLB、FBX、多视图渲染及
结构化验证报告。GLB 内嵌角色纹理，可直接进入 Unity 资产导入流程。

资产根目录：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/BowlingBall/`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/BowlingPin/`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/BowlingCommon/`

### 2. 参数与几何

保龄球：

- 直径 218.3 mm，`1 Blender Unit = 1 m`。
- 两个手指孔直径 20.6 mm，拇指孔直径 24.0 mm。
- 球体使用 96×48 UV Sphere；三个孔使用 Exact Boolean 实际切割，不是黑色贴片。
- 孔沿、内腔和主体分离，方便后续单独调整材质或移除视觉细节。

球瓶：

- 最大直径 121 mm，高度 380 mm。
- 96 段旋转体，纵向轮廓通过分段 Hermite 插值生成。
- 白色颈环位于 252–264 mm 和 275–287 mm 高度区间，直接使用瓶体面材质分配，
  不增加开放式悬浮环。

两套角色贴花均通过
`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/BowlingCommon/bowling_common.py`
中的 `closed_circular_patch` 创建。该方法同时生成外表面、内表面和边墙，形成闭合薄壳，
并通过 `surface_mapper` 分别贴合球面与旋转体曲面。

### 3. 构建与验证流程

贴图预处理入口：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/BowlingCommon/prepare_bowling_textures.py`

模型构建入口：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/BowlingBall/scripts/build_bowling_ball_asset.py`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/BowlingPin/scripts/build_bowling_pin_asset.py`

执行顺序：

1. 使用 Blender 内置 Python 运行贴图预处理脚本，从既有角色透明图生成圆底、白环和
   透明外缘贴花。
2. 分别运行两个模型构建脚本，生成几何、材质、摄影棚和四视图。
3. 保存 `.blend`，导出 `.glb` 与 `.fbx`。
4. 在空场景中重新导入两个导出格式，核对尺寸、网格数和材质数。
5. 使用 BMesh 检查全部网格边界边与非流形边，并写入 JSON 报告。

当前报告：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/BowlingBall/reports/bowling_ball_model_report.json`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/BowlingPin/reports/bowling_pin_model_report.json`

报告中的尺寸、闭合流形、真实指孔、双白环和曲面贴花断言均为 `true`。

### 4. 导出与运行时约定

- GLB 使用 glTF 2.0，贴图以二进制资源内嵌，不依赖外部绝对路径。
- 关闭纯色网格的切线导出，避免无 UV 网格产生无意义警告。
- 保龄球视觉网格约 19.4k 三角面；球瓶视觉网格约 21.3k 三角面。
- Unity 中保龄球应使用 Sphere Collider；不要让指孔细节改变投掷碰撞轮廓。
- 球瓶使用简化 Convex Mesh Collider 或 Capsule 组合体。动态刚体不得直接使用高面数
  非凸展示网格。
- 材质为非金属釉面；运行时若调整紫色，应修改材质 Base Color，不要在角色 PNG 上
  叠加整体色偏。

### 5. 已知边界

- 当前交付只完成模型、材质和格式级验证，尚未创建 Unity Prefab、刚体参数或保龄球玩法。
- FBX 会引用同目录纹理时需由接收方按 DCC/Unity 导入规则处理；首选运行时格式是已内嵌
  纹理的 GLB。
- 物理质量、摩擦、弹性、球瓶重心和 PICO 抓取手感需要在正式玩法场景及物理头显上验收。

## Phase Lavender Chibi Bowling V1.1 技术交接：顶部轮廓收窄（2026-10-05）

### 1. 修订范围

本次只修改球瓶白色颈环以上的头部轮廓。腹部、底座、颈环高度、角色贴花位置、
材质和整体尺寸保持不变。

参数化入口：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/BowlingPin/scripts/build_bowling_pin_asset.py`

关键规格：

- `PIN_HEAD_MAX_DIAMETER = 0.076`
- `HEAD_PROFILE_START_Z = 0.302`
- 302–378 mm 范围内的 `PROFILE_CONTROL` 半径由下至上调整为
  29、34.5、38、32.5、24.5、12.5 mm，380 mm 顶点仍收束为 0。

### 2. 设计说明

- 原顶部最大半径 41 mm，直径约 82 mm；V1.1 最大半径 38 mm，直径 76 mm。
- 收窄比例约 7.3%，属于轻量调整，不改变球瓶主体识别轮廓。
- 继续保留多个顶部控制点和 Hermite 插值，避免直接缩放导致顶部尖锐或在颈部产生折线。

### 3. 验证约定

报告
`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/BowlingPin/reports/bowling_pin_model_report.json`
新增：

- `reference_dimensions_mm.head_maximum_diameter = 76.0`
- `validation.head_maximum_diameter_matches = true`

每次重建仍必须同时通过整体尺寸、闭合流形、双白环、顶部直径和曲面贴花五项断言。
V1.1 沿用原文件名和目录，旧导出已被新轮廓原位替换。

## Phase Physics Foundation 技术交接：公共物理基座（2026-10-05，已完成）

- 设计、参数及接入说明见
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_公共物理基座.md`。
- `PhysicsBody` 统一管理根刚体、子碰撞体共享材质、受力跟随、暂停和净空/支撑查询；
  `PhysicsGrabInteractable` 只使用 XRI 的选择与附着点能力，避免双重运动写入。
- `AvatarMotor` 已从 CharacterController 迁移到 45 kg 动态胶囊；
  坐卧使用始终启用的受控接触代理。退出被占用时保留姿态，可通过起身/移动重试；
  角色上下文读取真实接触姿态。
- `GameSession` 恢复先暂停整批物品，验证穿插与支撑关系，失败撤回并保留存档。
  新增兼容旧版的 `ItemSaveData.needsGravitySettling`，用于恢复被暂停/退出打断的下落。
- 恢复未结束时 `AvatarActionCoordinator.Suspended=true`，因此 UI、AI、自治即使绕过
  `GameSession.Submit` 也不能取得角色控制权。暂停取消恢复后，`GameSession.Tick`
  在旧操作完全退出后重试；恢复家具占据初始出生点时，通过
  `AvatarMotor.TryPlaceAfterRestore` 选择可导航、无穿插且有支撑的安全位置。
- 主地面物品的最终 Y 使用 `INavigationService.FloorHeight`，NavMesh 采样点只负责
  验证 XZ 可达性，避免 NavMesh 代理高度使整套家具悬空。
- 50 Hz 固定步长及 0.06 s 最大补步窗口写入 TimeManager；使用局部 8/2 求解迭代、
  休眠、按需 CCD、非分配查询和合并保存。本轮不做压力测试。
- Unity 2022.3 此环境的直接批处理子进程启动存在已知 Mono 故障；使用 Hub 启动编辑器，
  在项目根 `.artifacts/framework/run-tests.request` 创建请求后执行 Assets → Refresh，
  由 `FrameworkTestMenu` 完成场景生成、空场景切换和测试。最终 XML 位于
  `/Users/bytedance/MateSoul/.artifacts/framework/unity-tests.xml`。
- 最终全量结果为 95/95 通过，失败 0、跳过 0。结果文件：
  `/Users/bytedance/MateSoul/.artifacts/framework/unity-tests.xml`。
- 已确认并修复初始化问题：启用 Rigidbody 插值后，外部设置 Transform 并不保证同帧
  `Rigidbody.position` 同步；装配结束必须通过 `PhysicsBody.Place` 设置刚体姿态。
- 卧姿转移先直立抬升，侧边入口先沿床长轴对齐，再旋转、横移和落位；
  退出使用同一路径的逆向净空验证，避免水平胶囊扫过床沿或床头板。
- Unity Test Framework 1.1.33 不应在共享 `UnitySetUp` 中执行 `EnterPlayMode`：
  域重载可能把上一测试已完成的迭代器 PC 用于下一测试。相关集成测试改为每项测试
  自己进入 Play Mode；`EngineTestFrames` 只向 EditMode runner 返回 `null`，
  在内部等待 `Time.time` / `Time.fixedTime`，确保断言真实执行。
- 当前测试自动化只有 `FrameworkTestMenu` 消费框架请求文件，可写完整测试名
  （多个用分号连接）；先触发现有 `.dbg/yummy-cookie-v001/refresh` 刷新，
  确认脚本 DLL 编译完成，再写测试请求，避免旧程序集与运行中测试混用。
- Android Development APK 已构建为
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，仅包含 `arm64-v8a`，
  使用 IL2CPP，APK v2 签名及压缩包完整性验证通过。

## Phase PICO Emulator Startup Fix 技术交接（2026-10-05）

### 1. 启动依赖

`PicoSpatialMeshRoomMapProvider` 依赖 Unity `XRMeshSubsystem`。PICO Loader 只有在
`PXR_ProjectSetting.spatialMesh=true` 时才创建该子系统；仅调用
`PXR_MixedReality.StartSenseDataProvider(SpatialMesh)` 不能弥补 Loader 配置缺失。

当前双重保障：

1. `/Users/bytedance/MateSoul/MateSoul/Assets/Resources/PXR_ProjectSetting.asset`
   持久化 `spatialMesh: 1`。
2. `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/GameSceneBuilder.cs`
   的 `ConfigureInteractionCapabilities()` 每次构建再次写入 `settings.spatialMesh=true`。

`GameBootstrap` 会在加载房间前隐藏角色。因此若房间初始化抛异常，角色保持隐藏；
排查角色完全消失时应先检查 `MATESOUL_READY`，不能只检查 Activity 是否启动。

### 2. 交互射线

`GameSceneBuilder.CreateInteractionRays()` 当前生成 5 条射线：

- 左右 `XRController` 射线：UI Press 使用
  `<XRController>{LeftHand|RightHand}/triggerPressed`，模拟值使用 `/trigger`。
- 左右 `PicoHandInteraction` 射线：姿态使用 `/pointer/position` 与
  `/pointer/rotation`，UI Press 使用 `/pointerActivated`。
- 一条 PICO 眼动 + 右手捏合射线。

`PlayerInteractionModeController` 使用 Unity XR
`InputDeviceCharacteristics` 检测控制器、手部和眼动设备。控制器丢失时，仅在真实检测到
手部设备后切换到 Gesture/EyeHand；否则继续保留 Controller 模式，避免把全部射线关闭。

### 3. 验证基线

- 定向测试：
  `MateSoul.Tests.GameRuntimeTests.CatalogPlacementGrabAndFocusUseRegisteredDefinitions`
  为 1/1 通过。
- 运行日志必须同时包含：
  `Spatial mesh snapshot blocks=...` 和
  `MATESOUL_READY source=PICO Emulator Spatial Mesh`。
- 当前 APK SHA-256：
  `030ded271ad17073117cca53f948599aa2dd372bcb0badff04df3cd9ded950c4`。
- 宿主 APK 与 `/data/app/.../base.apk` 哈希已确认一致。

### 4. 已知边界

- ADB `input keyevent` 不能代表 PICO Emulator 的原生控制器 Trigger；自动测试只验证
  InputAction 绑定和模式状态，最终点击手感仍需在宿主模拟器或真机人工验收。
- PICO Spatial Mesh 当前同时用于碰撞和 NavMesh 输入，家具水平面可能被识别为可行走面。
  角色渲染与激活已确认正常，但自治移动可能离开首屏。后续应把 Floor 语义导航几何与
  完整碰撞网格分离，不要通过关闭自治或固定角色位置掩盖导航数据问题。

## Phase Mini Game 01 技术交接：双人保龄球（2026-10-05）

### 1. 模块边界

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/MiniGame.cs`：
  小游戏内容基类，只暴露元数据、动作注册和 Begin/End 生命周期。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/MiniGameHost.cs`：
  会话级目录与唯一活动游戏所有者；拒绝并行启动，负责统一清理。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/SystemNotifications.cs`：
  无 Unity UI 依赖的消息状态源；小游戏、会话错误和 UI 命令只向该模块发布消息。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/SystemNotificationPresenter.cs`：
  左上角提示与视野中心结果窗口的唯一表现层。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/BowlingGame.cs`：
  球瓶点和球点放置、回合、计时、计分、复位与胜负状态机。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/BowlingTurnAction.cs`：
  冷月走位、下蹲伸手、滚球和等待结算动作；只由
  `AvatarActionCoordinator` 执行。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/BowlingPinContacts.cs`：
  动态球瓶与 Kinematic 阻挡代理组成的单向物理隔离策略。

### 2. 状态与计分

状态顺序：

```text
PlacingPins -> PlacingSpawn -> Approaching -> PlayerReady
-> PlayerRolling -> PlayerResult
-> CompanionPlaying -> CompanionRolling -> CompanionResult -> Winner
```

十瓶使用标准 1-2-3-4 阵列，相邻瓶中心距 `0.3048 m`。球首次接触任意球瓶时记录
时间，恰在 5 秒后读取全部球瓶状态。球瓶满足任一条件即计 1 分：

1. 本地上轴与世界上轴夹角不小于 45°。
2. 刚体中心比初始站立位置低至少 `0.19 m`。

未命中时以球稳定静止或 12 秒超时结算 0 分，防止状态机永久等待。每次计分后先冻结
本轮物理状态，再把唯一球实例复位到已验证为空的刷新点；玩家回合结束后同时复位球瓶。

### 3. 物理所有权

保龄球和球瓶继续使用公共 `PhysicsBody`。球为 `SphereCollider`、质量 `6.5 kg`；
球瓶为两个 `CapsuleCollider` 加一个头部 `SphereCollider`、质量 `1.55 kg`、重心高度
`0.145 m`。展示模型的指孔和 21k 三角面球瓶网格不参与动态碰撞。

球瓶不能简单设为 Kinematic，否则保龄球也无法击倒。`BowlingPinContacts` 为每个
动态球瓶创建同形状的 Kinematic 阻挡代理：

1. 动态球瓶只保留房间支撑、球瓶连锁碰撞和有效出手后的保龄球碰撞。
2. 手、角色和其他道具忽略动态球瓶，只与同步到球瓶姿态的 Kinematic 代理碰撞。
3. 保龄球出手前由代理阻挡；出手后切换为动态球瓶碰撞并忽略代理。
4. 每 `0.5 s` 扫描一次新加入场景的 Collider，使游戏中途生成的道具也遵守隔离规则。

曾验证过 `Physics.ContactModifyEvent` 的 inverse mass/inertia 缩放方案；回调实际触发
12 次时，普通刚体仍把球瓶推移约 `0.62 m`，因此已删除该方案，不能重新作为兜底。

### 4. 输入与角色控制

- Controller：UI 与放置使用 Trigger；物品抓取使用 Grip。
- EyeHand：眼动负责选中目标，右手 Pinch 负责确认；`HandPinchPose` 作为抓取附着点。
- Gesture：手部 pointer 射线负责指向，Pinch 负责确认/抓取/释放。

`PlayerInteractionModeController` 保证三种模式互斥，也允许在设置页手动循环选择。
放置期间 `PlacementActive=true`，普通物品选择射线暂停，避免同一次按压同时抓物和确认。

冷月回合注册为不暴露给 AI 的 `bowling-turn`，以 `IntentSource.System` 申请
`ControlChannels.All`。`ConversationOrchestrator` 在请求前和模型返回后都检查
Dialogue 通道，防止异步回复在冷月投球期间建立动作。

### 5. 内容构建与许可

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/BowlingContentBuilder.cs`
从以下源资产幂等生成球、瓶和游戏 Prefab：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/BowlingBall/`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/BowlingPin/`

欢呼音效位于
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Audio/ThirdParty/`，
来源与 CC BY 4.0 署名见同目录 `CrowdCheer_LICENSE.md`。

### 6. 验证说明

`/Users/bytedance/MateSoul/scripts/check-game-framework.py` 当前会编译 Runtime、Editor
和 Tests，已通过。Unity 运行回归入口为：

```text
/Users/bytedance/MateSoul/scripts/run-bowling-validation.command
```

LaunchServices 隔离 Unity 已成功执行场景构建并生成球、球瓶、材质和游戏 Prefab。
纯 batchmode 仍会命中本机 Mono semaphore 错误。保龄球专项测试 `4/4` 通过，
全量 Framework Tests 为 `98 passed / 0 failed / 1 skipped`；唯一跳过项是既有电视
可选诊断视频。Android ARM64/IL2CPP 构建通过，APK SHA-256 为
`39ff51ba57a16e3e5d67a22a8a934b66c4d9ac8e96b51160b8375724afe19bd8`。
当前无 ADB 设备在线，仍需在 PICO 真机验收三种输入的主观手感与 MR 空间尺度。

## Phase Mini Game 01.1 技术交接：房间与物品恢复状态解耦（2026-10-06）

`GameSession.ItemsReady` 只表示历史虚拟物品已恢复，不代表 PICO 房间网格或 NavMesh
是否可用。`GameSession` 本身是在 `RuntimeRoomNavigation.Build()` 成功后才创建，因此
`MiniGameHost` 已绑定 Session 时，小游戏所需的房间几何已经有效。

当前门禁使用 `GameSession.IsRestoringItems`：

1. `!ItemsReady && !ItemRestoreFailed`：旧物品仍在恢复，短暂阻止开始以避免布局并发变化。
2. `ItemsReady`：正常开始。
3. `ItemRestoreFailed`：旧锚点/存档不可用，但房间网格有效，允许小游戏开始新会话。

禁止再使用 `ItemsReady` 文案推断“房间定位未完成”。Unity 定向测试覆盖恢复中和恢复失败
两个分支，结果 `2/2` 通过。修复后 APK 已安装到 `emulator-5554`，宿主与设备
SHA-256 均为
`d31c969a85860d41e8bb10cdc5e8d2099a77167711dbdf4fc98b350a781fc698`。
设备日志中的旧物品恢复失败不再等价于房间网格失败。

## Phase Mini Game 01.2 技术交接：运行时 Shader 引用（2026-10-06）

Android 构建会裁剪未被资源直接引用的 Shader。运行时调用
`Shader.Find("Universal Render Pipeline/Unlit")` 不构成构建依赖，可能返回 `null`；
因此禁止用该方式创建保龄球放置标记材质。

`BowlingGame.Begin()` 当前从 `pinPrefab` 的 Renderer 中选择已序列化的不透明材质，
使用 `new Material(source)` 创建实例并设置 `_BaseColor`/`_Color`。球瓶 Prefab 是
游戏 Prefab 的直接依赖，因此其 Shader 与材质必然进入 Android 包。

专项测试验证球瓶 Prefab 至少存在一个有效不透明材质，结果 `4/4` 通过。修复版 APK
已安装并冷启动，SHA-256：
`d2a44beb5ac67e08ec152f8dce343b81cb394dcf421e328e95d3eae05858e289`。

## Phase Mini Game 01.3 技术交接：球专属吸附（2026-10-06）

`PhysicsGrabInteractable` 新增 `snapToInteractor`。保龄球 Prefab 将其设为 `true`：

1. Select 开始时读取 `interactor.GetAttachTransform(this)`。
2. 目标位置可占用时立即 `PhysicsBody.Place(target)`，提供明确的吸手反馈。
3. 随后进入 `BeginFollow(target)`，出手速度仍来自公共动态刚体求解。
4. 控制器射线的附着点是手柄；`HandItemRay` 覆盖附着点为 `HandPinchPose`。

该策略只用于保龄球，不修改桌椅等通用物品的远距抓取。生成的
`BowlingBall.prefab` 必须包含 `snapToInteractor: 1`，对应测试已纳入
`BowlingTests`。当前设备 APK SHA-256：
`b43c3e96429f22fb7319ae08ee6221b74850146997a7e8aa5d4372cbff3ad2a3`。

## Phase Art Playing Cards 技术交接：公制纸牌、图集与空心纸盒（2026-10-06）

资产目录：`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/PlayingCards/`。

1. `scripts/prepare_textures.py` 从包内三张参考图提取插画与角标，统一 54 个牌值，
   生成 `textures/faces/` 的独立 PNG、`cards_atlas_4k.png` 与 `box_atlas_2k.png`。
   角色按等比缩放放入统一背景；普通牌下角标由上角标旋转 180° 复用。
   图集有 16 px 卡片边缘扩展区；所有牌背引用同一个图集区域。
2. `scripts/build_playing_cards.py` 使用 Blender 构建真实厚度的圆角纸牌，
   每张以中心为枢轴、X 为宽、Z 为高、正面朝 −Y。63 × 88 × 0.30 mm 是几何尺寸，
   不依赖纹理轮廓；54 个命名节点独立保留。单位为米，禁止再额外乘以 1000。
3. 纸盒外廓宽/高/深 65/91/18.5 mm，纸板厚 0.35 mm。盒体是封闭纸板体积围成
   的空腔，顶盖、插舌、左右侧耳各为独立闭合网格。`Hinge_TopLid_RotateLocalX`
   打开角 −112°，左右侧耳分别绕自身 Y 轴旋转 −78°/+78°。
4. `scripts/asset_io.py` 提供包内独立导出与回读验证。导出 GLB 和 FBX 后重新导入，
   检查 54 个牌节点、逐张尺寸、整体包围盒、UV/材质/贴图；网格只在验证副本内
   以 1e-8 m 焊接导出产生的同位置接缝，再检查流形和退化三角形。
   `reports/model_validation.json` 记录 8/8 回读通过及约 0.45 mm 最小装盒间隙。
5. `scripts/package_delivery.py` 检查牌面唯一性、上下角标一致性、GLB 贴图内嵌、
   导出哈希，生成 `SHA256SUMS.txt` 和旁侧的 `LavenderChibiPlayingCards_v001.zip`。
   包内排除构建日志、Python 缓存和 Blender 临时备份。

源文件 `source/LavenderChibiPlayingCards_Set_v001.blend` 包含七个场景，默认打开
产品展示场景；主装盒场景名为 `01_Packed_54_Cards_In_Box`，54 张展开场景名为
`02_All_54_Cards_Equal_Size`。再次打开磁盘源文件已验证 54 张牌、七个场景、
两张内嵌图集。图集和独立 PNG 均保留，GLB/FBX 同时内嵌贴图。

当前未创建 Unity Prefab 或接入游戏。后续导入 FBX 时将两种材质映射到项目 URP
材质；单牌使用简化 BoxCollider，盒子采用壁面组合碰撞体保留真实内腔，物理与
抓取继续复用 `PhysicsBody` 等公共基座。原图单牌分辨率有限，不应将提取后
放大的纹理称为新增高清细节；盒背是按已提供牌背纹样补齐的设计面。

## Phase Art Dartboard 技术交接：飞镖盘程序化资产（2026-10-06）

资产根目录：
`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Dartboard/`。

1. `scripts/build_dartboard_asset.py` 从参数化尺寸生成模型。总包围盒固定为
   `400 x 48 x 400 mm`，正面朝 `-Y`；原三视图保存在
   `references/dartboard_orthographic_reference.png`。
2. 靶面从顶部 20 分开始，顺时针编号为
   `20,1,18,4,13,6,10,15,2,17,3,19,7,16,8,11,14,9,12,5`。
   四个径向区域分别为内单倍、三倍、外单倍和双倍。所有扇区均为封闭棱柱；
   径向和角向保留窄缝，露出底层分隔材质。
3. 80 个扇区在导出前合并为 `Dartboard_TargetSegments`，20 个数字合并为
   `Dartboard_Numbers`。合并器先按材质对象建立统一材质索引，再复制世界变换后的
   顶点和面，禁止直接用 `bpy.ops.object.join()`，后者会把本资产的扇区面错误归入
   第一个材质槽。
4. 主壳、扇区、数字、蝴蝶结、翅膀、猫爪、表情、侧面装饰、背板、挂孔和螺钉共
   56 个网格节点、41,190 顶点和 81,772 三角面。10 个 PBR 材质均内嵌于
   Blender/GLB/FBX，不依赖外部贴图。
5. `reports/dartboard_model_report.json` 记录源场景和 GLB/FBX 独立回读结果。
   三种格式尺寸均为 `400 x 48 x 400 mm`（FBX 浮点显示厚度 `48.0001 mm`），
   边界边和非流形边均为 0。
6. Unity 接入时应给主体配置一个简化圆柱或低边数凸碰撞体，装饰件默认不参与碰撞。
   靶面分区已有独立几何与材质，但命中计分应使用轻量的径向/角度数学判定，不应为
   80 个扇区各挂一个复杂 MeshCollider。

完整交付包：
`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/LavenderChibiDartboard_v001.zip`。
本阶段未创建 Unity Prefab、飞镖模型、投掷逻辑或 APK。

## Phase Virtual Item Catalog 技术交接：全量目录与分页（2026-10-06）

### 1. 目录装配

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/GameSceneBuilder.cs` 的
`BuildCatalog(Material)` 是正式目录的唯一装配入口，按稳定顺序返回 19 个
`ItemDefinition`。`GameBootstrap` 将该数组交给 `GameSession`，HUD、AI、自治和存档
继续共享同一注册表，不允许为新增物品建立平行生成链路。

新增通用模型由 `BuildCatalogProp(...)` 构建：

1. 从 `Assets/MateSoul/Content/PropsSource/<Item>/` 实例化 FBX。
2. 校验导入包围盒与美术规格，归一化模型中心和落地点。
3. 生成简化 Box/Sphere/Capsule Collider，并通过 `PhysicsContentBuilder` 配置唯一根 Rigidbody。
4. 按尺寸决定是否创建 `NavMeshObstacle`。
5. 生成 `Assets/MateSoul/Content/Props/<Item>.prefab/.asset`。
6. 小型物品启用 `allowSurfacePlacement`，可优先放到桌面等水平支撑面；扑克牌和
   遥控器在构建时额外旋转为稳定的平放姿态。

目录 ID 固定为：

```text
companion-lamp, table, chair, television, plate, mug, bowl,
playing-cards, spoon, chopsticks, knife, cupcake, cookie, apple,
dartboard, bowling-ball, bowling-pin, plush-doll, remote-control
```

正式目录共 19 项。苹果切片态不属于普通目录；目录中的保龄球和球瓶使用独立的
`ItemDefinition` 与普通物品 Prefab，`BowlingGame` 继续拥有自己的球/瓶实例和生命周期，
两者不共享运行时状态。

### 2. 材质与物理

- `BuildCatalogMaterial(...)` 把 FBX 内嵌材质复制为项目内 URP Lit 材质，保留基础色、
  Base Map、Normal Map、Metallic 和 Smoothness，路径为
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/Materials/`。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/PhysicsContentBuilder.cs`
  新增 Ceramic、Plastic、Paper、Food、Fabric 五种共享 PhysicMaterial。
- 所有新增物品继续由 `GameItem` 在运行时绑定 `PhysicsGrabInteractable`，抓取、暂停、
  稳定姿态存档和删除行为没有新增特例。
- 项目无刀具美术源文件；`BuildKnife()` 生成 ID 为 `knife` 的轻量餐刀 Prefab。
  后续取得正式 FBX 后应只替换其视觉子节点，保持 ID、物理所有权和存档兼容。

### 3. HUD 分页

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs` 的物品页使用固定
6 个按钮槽位，两行三列显示。`RefreshCatalogPage()` 只更新槽位名称、文本和显隐，
`ChangeCatalogPage()` 循环切换页码，避免目录数量增长时创建越界按钮或改变面板尺寸。
底部“下个已放置 / 过去看看 / 删除当前物品”仍操作房间实例，不与目录页索引混用。

### 4. 资源与验证

- Unity 运行时源资产：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/PropsSource/`
- 生成的定义、Prefab 与材质：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/`
- 正式场景：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Scenes/MateSoul.unity`
- `GameRuntimeTests` 增加生产目录完整性、物理组件和四页遍历验证；最终结果
  `24 passed / 0 failed / 0 skipped`。
- Android ARM64/IL2CPP Development APK 构建通过，路径
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，SHA-256
  `0d030d02490bb081064520ffa45c2e505501f1d72eb953a1f0bb88c25d4dab62`。
- 当前没有 ADB 在线设备，尚未完成设备侧安装、近距离材质观感、抓取手感和高面数物品
  同时存在时的性能验收。

## Phase Television Program 01 技术交接：节目目录、循环与中心裁切（2026-10-06）

### 1. 正式节目资源

第一个节目固定为：

```text
/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Video/LengyueCryingProgram.mp4
```

规格：MP4、H.264、AAC、1920×1080、30 fps、19.6 秒；SHA-256
`840f571f0b807a1b35b70bde34f12378a149f4fa883ccd5a1040ba6fd4cd2a12`。
`TelevisionBuilder.FirstProgramPath` 是构建期唯一引用，原桌面文件不参与运行时。

### 2. 节目所有权

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/TelevisionController.cs`
继续作为单台电视电源、解码器、音频和 RenderTexture 的唯一所有者，并新增：

- `programs`：按顺序序列化的正式 `VideoClip` 数组。
- `programIndex`：当前节目；第 0 项是默认节目。
- `ConfigurePrograms(VideoClip[])`：构建期注册节目并选中第 0 项。
- `SelectProgram(int)`：运行时切台；开机时立即停止旧源并准备新节目。

`SetVideoClip` 和 `SetVideoUrl` 继续作为临时外部源接口，调用后退出正式节目索引。
无论正式节目、临时 Clip 还是 URL，`BeginSource()` 都将 `VideoPlayer.isLooping`
强制设为 `true`，不允许单个节目关闭循环。

### 3. 无黑边画面策略

电视玻璃可视尺寸约 358×258 mm，RenderTexture 固定为 1024×738，宽高比一致。
`VideoPlayer.aspectRatio` 在 Prefab 和运行时均设置为 `VideoAspectRatio.FitOutside`：

1. 按原始宽高比等比缩放，禁止 X/Y 独立拉伸。
2. 让视频完全覆盖 RenderTexture。
3. 超出电视宽高比的区域从四周居中裁切。
4. `TelevisionScreen.shader` 继续按完整 UV 采样已裁切的 RenderTexture。

因此 16:9 节目在接近 4:3 的屏幕上会裁掉左右边缘，但不会出现上下黑边，也不会让
人物变宽或变窄。

### 4. 构建与验证

- `TelevisionBuilder` 每次创建/升级 Prefab 都重写正式节目、`FitOutside` 和循环配置。
- `Television.prefab` 已确认节目数组第 0 项引用正式 MP4，`programIndex=0`、
  `m_AspectRatio=4`、`m_Looping=1`。
- 带图形设备的 `TelevisionTests` 为 `7 passed / 0 failed / 0 skipped`；真实节目完成
  解码、非黑像素检查、19.6 秒循环回调和关机释放验证。
- Android ARM64/IL2CPP Development APK 构建通过：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，SHA-256
  `571a42acca9048edcc92f1e9d3a01873cd0099fd53dc95ce7196bfa3c045e742`。
- 当前无 ADB 在线设备，仍需在 PICO 模拟器或真机验收空间音量和近距离屏幕观感。

## Phase UI Overlay 技术交接：World Space UI 顶层渲染（2026-10-06）

### 1. 阶段概述

主 HUD、展开面板、字幕、系统提示和小游戏结果仍使用 World Space Canvas，以保留
XR 射线交互与头部跟随语义；显示层通过统一 Shader 和 Canvas 排序绕过 3D 深度遮挡。
电视机屏幕和实体按钮不属于系统 HUD，继续参与真实世界遮挡。

### 2. 核心实现

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AlwaysOnTopUi.cs`
  是顶层 UI 配置的唯一入口。
- `Configure(Canvas)` 设置 `overrideSorting=true` 和 `sortingOrder=32760`。
- `ConfigureRaycasters(GameObject)` 同时保留 `GraphicRaycaster` 与
  `TrackedDeviceGraphicRaycaster`，并关闭后者的 2D/3D Occlusion 检查，避免前景
  Collider 拦截本应落到 HUD 的 XR 点击。
- `Apply(Transform, Material)` 处理已有子层级 Graphic；泛型 `Apply<T>` 处理运行时
  新建的 `Image` 和 `Text`，防止动态窗口退回默认 UI 材质。
- `CreateMaterial()` 从 `Resources/MateSoulUI/AlwaysOnTop` 加载模板并实例化，实例由
  Presenter 在生命周期结束时销毁，避免共享运行时状态或材质泄漏。

调用流程：

1. Presenter 创建顶层材质。
2. 创建 World Space Canvas，并配置最高安全排序值和 XR Raycaster。
3. 创建全部静态与动态 Graphic，同时绑定同一材质实例。
4. 每帧只更新 Canvas 的头部跟随位置和 `worldCamera`，不修改深度策略。
5. Presenter 销毁时释放实例化材质。

### 3. Shader 与资源

- Shader：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/Shaders/UiAlwaysOnTop.shader`
- 构建引用材质：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/Resources/MateSoulUI/AlwaysOnTop.mat`

Shader 使用 Overlay Queue、`ZWrite Off`、`ZTest Always` 和标准 Alpha Blend，同时保留
Unity UI 的 Stencil、裁剪矩形、Alpha Clip、顶点色、`_TextureSampleAdd` 与 Sprite
Atlas 支持。Resources 材质必须保留：如果只通过 `Shader.Find` 访问，Android Player
依赖分析会裁剪 Shader，运行时将抛出材质缺失异常。

### 4. 相机生命周期

`GameHud.Recenter()` 和 `SystemNotificationPresenter.Follow()` 不得在同一帧重复读取
`Camera.main`。PICO XR 初始化时 Main Camera 会短暂替换，正确流程是先缓存
`activeCamera`，判空后在本帧只使用该引用更新 Transform 和 `canvas.worldCamera`。

### 5. 验证

- `GameRuntimeTests`：`24 passed / 0 failed / 0 skipped`。
- 测试覆盖 Canvas `sortingOrder=32760`、全部 Graphic 的
  `MateSoul/UI Always On Top` Shader，以及 XR Raycaster 的两个遮挡开关。
- Android ARM64/IL2CPP 构建成功。APK 大小 `118,893,297` 字节，SHA-256
  `3ee0f329b1b8c50296d62b2ea4a6797dd337395c4a04e534b1b75d6116cd93af`。
- `emulator-5554` 覆盖安装后回拉 `base.apk`，哈希与宿主一致；冷启动截图确认 HUD
  和系统提示在前景家具上保持可见，日志无 Shader 缺失和 HUD 相机空引用。
- Emulator 的 Android 触摸注入不等价于 XR 射线点击，且 QEMU 窗口没有可供
  Computer Use 定位的 bundle ID；展开窗口的设备自动点击属于当前验证边界。

## Phase UI Icon Sharpness 技术交接：World Space 图标采样策略（2026-10-06）

### 1. 问题与原则

HUD 的互动、信息和虚拟物品图标为 192×192 RGBA 纹理，最终显示区域约 80×80 Canvas
单位。UI 图标不是远景材质，不应生成 Mipmap；否则 World Space Canvas 的缩放导数会
选择较低 mip，导致已经缩小显示的图标再次降采样。小尺寸 UI 纹理也不应使用 Android
有损纹理压缩。

### 2. 资源配置

以下三个资源的 `.png.meta` 使用相同配置：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/Resources/MateSoulUI/Interaction.png`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/Resources/MateSoulUI/Information.png`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/Resources/MateSoulUI/Items.png`

固定要求：

1. `enableMipMap: 0`，始终从原始 192×192 图像采样。
2. Default、Standalone、Android 的 `textureCompression: 0`。
3. `alphaIsTransparency: 1`，让 Unity 对透明像素边缘做颜色扩展，避免 Bilinear
   采样产生暗色或发灰轮廓。
4. 运行时继续使用 `FilterMode.Bilinear`；原图已有抗锯齿，不应改成 Point 造成锯齿。

对话和设置图标由 `GameHud.CreateIconSprite()` 创建 `Texture2D(..., false)`，构造参数
已经禁用 Mipmap，不需要添加资产导入配置。

### 3. 验证

- HUD 专项测试 `BottomHudExpandsVerticalInteractionActivities`：`1 passed / 0 failed`。
- Android ARM64/IL2CPP 增量构建成功，APK SHA-256：
  `5c9d81f18300591ee48ff03aef437c818db7d1ca22b7f97ed37dfdc9675474c1`。
- `emulator-5554` 冷启动截图确认资源图标边缘更清晰，UI 顶层遮挡行为保持不变；
  设备回拉 APK 与宿主构建哈希一致。

## Phase Grip Grab Regression 技术交接：PICO Grip 输入与交付构建隔离（2026-10-06）

### 1. 输入绑定规则

控制器射线由
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/GameSceneBuilder.cs`
的 `CreateControllerRays()` 统一创建。Controller 模式的 Select 必须同时保留：

- `<XRController>{LeftHand|RightHand}/gripPressed`：数字侧键，是真机的标准路径。
- `<XRController>{LeftHand|RightHand}/grip` +
  `Press(pressPoint=0.1)`：模拟量侧键兼容路径。
- `<Keyboard>/e` 和 `<Keyboard>/o`：PICO Emulator 左右手键盘回退。

PICO `DeviceLayouts.cs` 对 `gripPressed` 声明的 `GripButton`/`GripPress` 是 alias，
不是 Input System usage。`/{GripButton}` 会按 usage 查找控件，不能替代
`/gripPressed`。后续升级 PICO SDK 时必须保持该精确路径，并由测试设备布局同时验证
数字和模拟量输入。

### 2. 抓取所有权与防护

- `ActionBasedController` 只将 Grip 转换为 XRI Select。
- `XRRayInteractor` 负责命中、Hover、Select 和附着点。
- `PhysicsGrabInteractable` 只桥接 XRI 选择生命周期与 `PhysicsBody`，不直接写
  Rigidbody 或 Transform。
- `PhysicsBody` 继续作为动态刚体、跟随目标、速度继承和释放后重力的唯一所有者。
- `HandItemRay.GetValidTargets()` 在访问跟踪状态前校验 `mode`、`xrController` 和
  `currentControllerState`。未激活或尚未绑定的手部射线返回空目标集，不能抛异常。

通用物品、保龄球、真机 Grip、模拟器键盘和手势模式不得创建平行抓取实现；新增物品
只注册 Collider、`PhysicsBody` 和 `PhysicsGrabInteractable`。

### 3. 回归测试

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/GripGrabTests.cs`
注册一个同时具有 `grip` 与 `gripPressed` 的测试 XRController，并通过生产
`CreateControllerRays()` 建立真实交互链路。

两项测试分别只注入模拟量和只注入数字按钮，统一验证：

1. 射线将目标 `PhysicsGrabInteractable` 解析为有效目标。
2. Grip 激活 `selectInteractionState` 和 XRI Selection。
3. `PhysicsBody.IsFollowing` 为真，动态刚体保持非 Kinematic。
4. 移动控制器后物体跟随附着点。
5. 松开后取消 Selection/Follow，并在重力下下落。

数字 `ButtonControl` 的 delta state 是 1 字节，测试必须注入 `bool`；向其注入
4 字节 `float` 会在 Input System 边界直接抛出尺寸不匹配，不能代表产品绑定失败。
当前结果为 `GripGrabTests 2/2`、`GameRuntimeTests 24/24`。

### 4. 正式与诊断构建

`GameSceneBuilder.BuildAndroid()` 是正式菜单和自动交付入口，固定使用
`BuildOptions.None`。`BuildAndroidDevelopment()` 才允许生成 Development APK；
`BuildItemFocusDebugOnce` 已显式改用该入口。

该隔离避免其他仍开放的 `#if DEVELOPMENT_BUILD` 调试会话进入正式包并发起明文 HTTP，
同时保留按需诊断能力。正式 APK 必须在安装前验证：

1. 不含 `application-debuggable`。
2. 仅包含 `arm64-v8a`。
3. `pvr.app.type=vr`、`use.pxr.sdk=1`。
4. `enable_anchor`、`mr_map_mgr_auto_start`、`enable_spatial_anchor` 均为 1。
5. 主机 APK 与设备 `base.apk` SHA-256 完全一致。

本阶段正式 APK 为
`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，大小
`108,328,452` 字节，SHA-256
`7a8759771307c8abc770656b03268ff3b4c3e221fcd4516d6ff4d31701403ed2`。
`emulator-5554` 冷启动成功并输出 `MATESOUL_READY`；最终侧键手感仍需用户在空间画面中
完成“放置物品、射线对准、按 Grip/O、移动、松开”的人工验收。

## Phase Television Program Replacement 技术交接：正式节目无断链替换（2026-10-06）

### 1. 当前正式资源

电视第 0 个节目固定为：

```text
/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Video/PixelSpaceGameProgram.mp4
```

资源参数：

- VideoClip GUID：`d46263424b4b94293a866c1f1af847bb`
- SHA-256：`c55bdafdcc691236eccb0c3639b2eb044d487eb6d19f6e7538c5e381879f0079`
- 视频：H.264 High、yuv420p、1280×720、24 fps
- 音频：AAC LC、44.1 kHz、双声道
- 时长：15.071995 秒

旧 `LengyueCryingProgram.mp4` 的 `.meta` 随文件重命名，GUID 不变。这样
`Television.prefab` 中的 `programs[0]` 和 `videoClip` 可继续使用原序列化引用，
无需重新绑定已有电视实例或迁移场景数据。

### 2. 构建期单一入口

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/TelevisionBuilder.cs`
的 `FirstProgramPath` 是正式节目路径的唯一构建期来源。`Build()` 会加载该资源并通过
`ConfigurePrograms(VideoClip[])` 重写 Prefab 节目列表；文件缺失时直接抛错，不能静默
回退到旧节目或空源。

后续换节目应遵循：

1. 把正式 MP4 放入 `Assets/MateSoul/Content/Video/`。
2. 需要保持既有引用时，连同当前 `.meta` 一起重命名并覆盖媒体内容。
3. 更新 `FirstProgramPath` 和媒体元数据测试。
4. 重建场景，运行 `TelevisionTests` 的真实解码用例，再构建 APK。

电视运行时所有权未改变：`TelevisionController` 继续独占 VideoPlayer、AudioSource、
RenderTexture、电源状态和节目索引；输入路径只调用同一个电源命令。

### 3. 验证不变量

本次替换后的不变量为：

1. Prefab 节目数为 1，`ProgramIndex=0`，资源路径等于 `FirstProgramPath`。
2. VideoClip 为 1280×720，时长在 15.072±0.1 秒内。
3. `VideoPlayer.aspectRatio=FitOutside` 且 `isLooping=true`。
4. 播放后状态进入 `Playing`，RenderTexture 存在非黑像素。
5. 完整播放后收到 loop 回调；关机后停止播放并释放目标纹理。
6. APK 与设备 `base.apk` SHA-256 完全一致。

带正式媒体副本的 `TelevisionTests` 为 `7 passed / 0 failed / 0 skipped`；独立全量
回归为 `109 passed / 0 failed / 1 skipped`，其中跳过的是没有临时诊断文件时的同一
可选解码用例。正式 APK 大小 `92,856,132` 字节，SHA-256
`731a6caa1b8abfb03096548d815069b4e4c0493b02bc9b633488e8934e666ed1`，
已安装到 `emulator-5554` 并冷启动成功。

完整结构化结果位于：

```text
/Users/bytedance/MateSoul/项目沉淀资料/validation/television_program_replacement_20261006.json
```

## Phase Hand Hold IK 技术交接：受约束手臂 IK 与牵引控制（2026-10-06）

### 1. 核心结构

牵手链路保持角色控制单一所有权：

```text
HandHoldInteractable
  -> AvatarIntent(hold-left-hand / hold-right-hand)
  -> AvatarActionCoordinator
  -> HandHoldAction
       |-> HandIKRig -> TwoBoneIKConstraint
       `-> AvatarMotor -> PhysicsBody
```

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/HandHoldInteractable.cs`
  只负责 XRI Select 生命周期、Interactor 位姿和震动反馈，不移动 Transform/Rigidbody。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/HandIKRig.cs`
  持有左右手骨骼绑定、Target、Hint、IK 权重、掌心偏移、腕部限位和范围弧线。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/HandHoldAction.cs`
  持有牵手动作状态、跟踪丢失/超距释放、代理力、玩家移动前馈和 NavMesh 方向约束。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/HandHoldMath.cs`
  提供无场景状态的伸展约束、swing-twist 腕部约束和带迟滞的牵引控制器。
- `AvatarMotor.BeginHandHold/SetHandHoldVelocity/EndHandHold` 是牵引位移唯一入口；
  `HandHoldAction` 不直接调用 Rigidbody。

### 2. 动作与资源所有权

左右手分别注册为 `hold-left-hand` 和 `hold-right-hand`，控制通道为：

```text
Left:  Locomotion | LeftArm
Right: Locomotion | RightArm
```

两者都包含 `Locomotion`，因此 Demo 仍只允许单手牵引。Player 优先级可以抢占移动和
全身接触动作；旧动作退出并释放 Lease 后，牵手动作才启动。取消阶段保留 Lease，直到
IK 权重降为零。暂停和会话销毁通过 `Dispose()` 立即清除 Motor、IK 和 XRI Selection。

### 3. IK 与人体工学约束

每侧主链为 `UpperArm -> Forearm -> Hand`，Target 和 Hint 由
`GameSceneBuilder.BindHandHoldRig()` 生成。Target/Hint 不属于骨架链，但必须保持在
Animator 根层级中，不能在运行时 `SetParent(null)`。Animation Rigging 在
Android/Burst 下会为场景 Transform 建立 `TransformStreamHandle`；层级外引用无法解析。

腕部限制使用前臂轴和掌心标志点构造解剖坐标系：

1. 将目标手腕旋转转换到 Forearm 局部空间。
2. 分解为绕前臂轴的 twist 与垂直方向的 swing。
3. twist 限制旋前/旋后；swing 投影到屈伸、尺偏/桡偏组成的非对称椭圆。
4. `ForearmTwist_*` 只承担部分视觉蒙皮扭转，不进入 TwoBone IK 主链。

默认舒适范围为屈腕 `45°`、伸腕 `40°`、尺偏 `25°`、桡偏 `15°`、
旋前/旋后各 `65°`，软限制比例为 `0.78`。这些值是产品调参起点，不是医学极限。

### 4. 牵引动力学

PICO 输入只提供位姿，没有真实力传感器。系统根据超出软伸展范围的位移和相对外拉速度
计算代理力：

```text
extension = max(0, shoulderToTarget - softReach)
proxyForce = spring * extension + damping * outwardSpeed
driveForce = max(0, horizontalProxyForce - movingResistance)
acceleration = driveForce / effectiveMass
```

- 软/硬伸展默认分别为手臂长度的 `0.84`/`0.97`。
- 启动阈值 `18`、停止阈值 `10`，分别持续 `0.12s`/`0.20s`，避免临界抖动。
- 玩家 HMD 与手部水平移动同向时加入速度前馈；只晃手而头部静止时不使用该前馈。
- 牵手最大速度 `1.1 m/s`、最大加速度 `2.2 m/s²`。
- 竖直力和控制器角速度不传入角色根节点；超距、跟踪跳变或路径长期阻塞会自动释放。

### 5. 资源与验证

- Animation Rigging 依赖：
  `/Users/bytedance/MateSoul/MateSoul/Packages/manifest.json`
- 角色运行时 Prefab：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Lengyue_Runtime.prefab`
- 限位显示材质：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/HandHoldLimit.mat`
- 场景构建器：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/GameSceneBuilder.cs`
- 专项测试：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/HandHoldTests.cs`

当前验证结果为专项 `5/5`、全量 `109 passed / 0 failed / 1 skipped`。最终 Android
APK SHA-256 为
`731a6caa1b8abfb03096548d815069b4e4c0493b02bc9b633488e8934e666ed1`；
`emulator-5554` 启动后输出 `MATESOUL_READY`，且无 HandHold Rig 句柄异常。

## Phase UI High Density 技术交接：World Space 动态字体像素密度（2026-10-07）

### 1. 根因

Unity `CanvasScaler` 在 World Space 模式下会把 `dynamicPixelsPerUnit` 写入
`Canvas.scaleFactor`。旧版 `UnityEngine.UI.Text` 对动态字体直接使用该 scale factor
作为 `pixelsPerUnit`。默认值 `1` 会让 20px 字号只生成约 20px 的字形纹理，即使 UI
在空间中具有较大的物理尺寸，字体贴图本身仍然低分辨率。

### 2. 统一配置

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AlwaysOnTopUi.cs`
  定义 `DynamicPixelsPerUnit = 4f`，并由 `ConfigureScaler(CanvasScaler)` 同时设置
  `referencePixelsPerUnit=100` 和动态像素密度。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs`
  创建 CanvasScaler 后立即调用该共享配置。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/SystemNotificationPresenter.cs`
  在 Editor 与 Android 两种模式下都创建 CanvasScaler；Android World Space 不再使用
  Canvas 默认 scale factor。

该配置只提高动态字体生成分辨率。RectTransform 尺寸、World Space `localScale` 和
屏幕中的视觉尺寸保持不变；PNG 图标继续使用无 Mipmap、无压缩配置，UI 顶层 Shader
继续使用 `ZTest Always`。

### 3. 验证与边界

- HUD 专项测试 `BottomHudExpandsVerticalInteractionActivities`：
  `1 passed / 0 failed`。
- Android ARM64/IL2CPP 构建成功，独立 APK SHA-256：
  `39839ed93f8b069e59c45e8e3ac785b2751fa0f834c693e57c724c214e60586c`。
- PICO Emulator 冷启动截图确认系统提示和导航标签笔画更清晰，近景角色/家具不能遮挡
  HUD，目标 UI 异常计数为 0。
- 同包名 APK 会被并行验证任务互相覆盖。设备截图后曾发生另一条部署；待 ADB 空闲后
  已重新安装并立即回拉 `base.apk`，最终设备与宿主 SHA-256 完全一致。后续并发验证
  仍应优先使用独占模拟器或独立包名。

## Phase Hand Hold Proximity 技术交接：物理手位姿与近场准入（2026-10-07）

### 1. 交互不变量

牵手必须同时满足“瞄准掌心、物理手进入近场、握持输入有效”三个条件：

```text
Ray/Gaze 命中 HandHoldInteractable
  + Controller Transform 或 Pinch Attach 距离掌心 <= 0.10 m
  + Grip 或 Pinch 触发 XRI Select
  -> HandHoldRequest
  -> AvatarActionCoordinator
  -> HandHoldAction / HandIKRig
```

射线命中点只负责目标发现与 Hover，不得作为物理手位置。普通物品继续支持原有 8 米
射线抓取；近场限制仅存在于 `HandHoldInteractable.IsSelectableBy()`，不会改变
`PhysicsGrabInteractable`。

### 2. 位姿来源

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/HandHoldPoseProvider.cs`
  定义 `IHandHoldPoseProvider` 和共享的跟踪有效性检查。
- `ControllerItemRay.TryGetHandHoldPose()` 返回其 `ActionBasedController` 所在的
  真实跟踪 Transform。射线末端和命中点均不参与距离计算。
- `HandItemRay.TryGetHandHoldPose()` 返回 `HandPinchPose` 更新的
  `LeftHand/RightHand Pinch Attach`。EyeHand 模式虽然由 gaze ray 提供候选目标，
  最终准入和持续 IK 仍使用手部捏合点。
- 位姿缺少 Position/Rotation tracking flag、对象失活、位置非有限数或四元数无效时
  fail closed，不允许开始或继续牵手。

### 3. 距离滞回

`HandHoldInteractable` 默认参数：

```text
entryDistance = 0.10 m
exitDistance  = 0.16 m
```

未选中时只接受进入半径；已被同一 Interactor 选中后改用退出半径。XRI 在保持选择期间
每帧重新调用 `IsSelectableBy()`，因此物理手超过 `0.16 m` 时会进入统一的 Select Exit，
再由 `HandHoldAction` 淡出 IK 并释放动作通道。两个半径避免手停留在阈值附近时反复
建立和断开牵手。

### 4. 模拟器与真机验证

PICO Emulator 不显示 MateSoul 自定义手柄网格，但运行时仍有
`ActionBasedController.transform`；射线起点就是该跟踪节点的可见线索。当前
PICO Emulator 6.2.0 的 `virtual_input` 服务会反复退出，键盘 `O/E` 可验证 Grip
输入回退，却不能可靠模拟 6DoF 手柄平移，所以不能仅靠当前模拟器完成最终近场手感验收。

推荐分层验证：

1. `HandHoldTests` 验证远距拒绝、Controller Transform、Pinch Pose 和 `0.10/0.16 m`
   滞回，当前为 `8/8` 通过。
2. Unity PlayMode 可导入 XRI 2.5.4 的 XR Device Simulator，通过模拟控制器平移测试
   手臂跟随和超距释放；该 Simulator 只能用于 Editor，不进入 Android 包。
3. PICO 真机完成最终验收：将真实手柄移入角色掌心约 `10 cm`，按住 Grip，移动手柄
   观察手臂 IK 与角色牵引，再移出约 `16 cm` 或松开 Grip 验证释放。

全量测试为 `112 passed / 0 failed / 1 skipped`。Android ARM64/IL2CPP Release APK
SHA-256 为
`97a8c7c1fb82a533a3e5f2f027cebb65b57df7001511437e9ec9e459966ecce4`；
设备安装哈希一致，`emulator-5554` 冷启动输出
`MATESOUL_READY source=PICO Emulator Spatial Mesh items=3 itemState=ready navMs=47`。

## Phase Bowling Pin Reset 技术交接：占位语义与复位物理所有权（2026-10-07）

### 1. 根因与不变量

`BowlingPinContacts` 使用 Kinematic 代理阻挡非保龄球物体，并对真实球瓶与代理调用
`Physics.IgnoreCollision`。旧版 `PhysicsBody.CanOccupy` 仍对该代理执行
`Physics.ComputePenetration`，导致碰撞层认为“不碰撞”，占位层却认为“被占用”。

修复后的共享不变量：

```text
Physics.GetIgnoreCollision(shape, other) == true
=> other 不得阻止 shape 的 CanOccupy
```

这不是保龄球名称特判，适用于所有显式关闭碰撞的 `PhysicsBody` 碰撞对。

### 2. 球瓶复位生命周期

核心文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/BowlingPin.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/BowlingGame.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/PhysicsBody.cs`

复位流程：

1. `BowlingGame.TryResetPins` 先验证全部 Standing Pose 可占用。
2. 每个球瓶只调用一次 `BowlingPin.Rack(StandingPose)`。
3. `Rack` 如有旧 Contact 所有权先释放，再用 `Body.Place` 清零动态速度。
4. 球瓶取得 Contact/Kinematic 所有权，并记录 `Time.time + 1f`。
5. `BowlingPin.FixedUpdate` 到期后释放 Contact，恢复动态刚体。
6. `TryResetPins` 把 `IsPlacementLocked` 视为已完成复位，不能在后续帧重复 Rack。

`BowlingPin` 是复位锁的唯一所有者；`BowlingGame` 不维护第二套解锁计时器。

### 3. 输入与模拟器验证

PICO Emulator 6.2 的桌面 `virtual_input` 服务不稳定，但原始键盘事件可到达 Unity。
控制器输入同时保留：

- 真机 Grip：`gripPressed` 和带 `Press(pressPoint=0.1)` 的 `grip`。
- 模拟器 Grip：右 `O`、左 `E`。
- 真机 UI：`triggerPressed` 和 `trigger`。
- 模拟器 UI：右 `U`、左 `Q`。

Development 设备探针只负责寻找合法球道、调用生产放置校验、等待冷月到位和对准真实
XRI 射线；它不调用 Select/Grab。抓取、移动和释放仍由键盘映射进入
Input System、`ActionBasedController`、XRI 和 `PhysicsBody`。

### 4. 验证与交付

- 自动化：`BowlingTests 8/8`、`PhysicsRuntimeTests 5/5`、
  `GripGrabTests 2/2`、扳机专项 `1/1`。
- 设备：玩家得分后成功进入 `CompanionRolling`，排除复位阻塞；第二次 Rack 的
  10 个球瓶在保护窗口内无位姿和速度偏差。
- 证据文档：
  `/Users/bytedance/MateSoul/debug-bowling-pin-reset-blocked.md`。
- 稳定 Release：
  `/Users/bytedance/MateSoul/.artifacts/item-focus-debug/Builds/MateSoul_Framework.apk`，
  SHA-256
  `41845998d8108d809b5647d6a0dc49a65d4411f13f6512daa8a51578dceaf69b`。
  Release Manifest 不含 `debuggable` 或明文 HTTP 配置，设备哈希一致并输出
  `MATESOUL_READY`。

## Phase Emulator Launch Recovery 技术交接：APK 场景损坏与启动存活校验（2026-10-07）

### 1. 故障判定

`adb shell am start -W` 的 `Status: ok` 只表示 Android 接受并启动了 Activity，不表示
Unity 已加载首场景。故障 APK 的确定性证据为：

```text
Unity: assets/bin/Data/level0 is corrupted
Unity: [Position out of bounds!]
libc: Fatal signal 5 (SIGTRAP), tid Loading.Preload
ActivityManager: APP CRASH(NATIVE)
```

APK ZIP CRC、设备安装哈希和 Manifest 均可正常通过，因此这些检查不能替代 Unity
运行时场景加载验证。本次损坏位于 Unity 序列化的 `level0` 内容，而非传输或安装过程。

### 2. 启动脚本流程

核心文件：

- `/Users/bytedance/MateSoul/scripts/start-pico-emulator.command`
- `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`

当前流程：

1. 校验 APK 存在且包含 PICO VR、SDK 和 Spatial Anchor Manifest 元数据。
2. 启动或复用 AI Gateway。
3. 等待 `emulator-5554` 完成启动。
4. 对比宿主 APK 与设备 `base.apk` SHA-256，只在不一致时覆盖安装。
5. 配置 `adb reverse tcp:8787 tcp:8787`，强制停止旧进程并执行 `am start -W`。
6. 等待 5 秒并通过 `pidof` 验证 Unity 进程仍存活；失败时输出关键崩溃摘要并返回非零。
7. 启动过程同时输出到终端和
   `/Users/bytedance/MateSoul/.dbg/pico-emulator/emulator-host.log`。

### 3. 交付约束

- 当前根目录稳定 APK SHA-256 为
  `41845998d8108d809b5647d6a0dc49a65d4411f13f6512daa8a51578dceaf69b`。
- 新 APK 不能只做 ZIP、签名、Manifest 和安装哈希检查；至少还要执行冷启动存活检查，
  并确认设备日志出现 `MATESOUL_READY`。
- 并行 Unity 构建不得直接共享或覆盖同一
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`。各任务应先写独立产物，
  通过冷启动验证后再晋升为根目录交付包。

## Phase Television Stale APK Fix 技术交接：节目内容准入（2026-10-07）

### 1. 根因

源码和 `Television.prefab` 均正确指向 `PixelSpaceGameProgram.mp4`，但恢复到根目录的
旧稳定 APK 是在节目替换前生成的。直接提取其
`assets/bin/Data/sharedassets0.resource`，可得到旧节目原始 SHA-256
`840f571f0b807a1b35b70bde34f12378a149f4fa883ccd5a1040ba6fd4cd2a12`。
因此“宿主 APK 与设备 APK 哈希一致”只能证明安装一致，不能证明节目内容正确。

### 2. 两层防护

`GameSceneBuilder.BuildAndroid(BuildOptions)` 会给所有正式和 Development 构建追加
`BuildOptions.CleanBuildCache`，避免 Unity/Gradle 变体中间目录保留旧 streamed asset。

`/Users/bytedance/MateSoul/scripts/start-pico-emulator.command` 在读取 Manifest 后执行
节目准入：

1. 从 APK 的 `sharedassets0.resource` 定位 MP4 `ftyp`。
2. 按正式视频字节长度提取内嵌节目。
3. 比较内嵌节目与
   `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Video/PixelSpaceGameProgram.mp4`
   的 SHA-256。
4. 不一致时返回非零，不比较设备哈希、不安装、不启动应用。

该校验同样作用于 `MATESOUL_APK_PATH` 指向的诊断包，避免旧隔离工程再次覆盖设备节目。

### 3. 当前交付

- 正式节目 SHA-256：
  `c55bdafdcc691236eccb0c3639b2eb044d487eb6d19f6e7538c5e381879f0079`
- APK SHA-256：
  `d8edc592f413b7f8b64d38518e4a2ac3f6a1297de8843102e38d989ba22dcb68`
- APK 大小：`117,799,263` 字节
- 设备：`emulator-5554`，安装哈希与宿主一致
- 冷启动：`MATESOUL_READY`，应用保持前台

验证报告：

```text
/Users/bytedance/MateSoul/项目沉淀资料/validation/television_stale_apk_fix_20261007.json
```

## Phase Hand Hold Simulator 技术交接：按需 XR 设备模拟（2026-10-08）

### 1. 组件边界

- `HandHoldSimulatorSetup` 是 Editor-only 入口，负责导入 XRI 官方样例、配置 Prefab
  引用、重建场景和启动测试 PlayMode。
- `XRDeviceSimulatorSettings` 的
  `m_AutomaticallyInstantiateSimulatorPrefab` 必须保持 `false`。Simulator 只在
  `Run Hand Hold Simulator` 触发的 `EnteredPlayMode` 中实例化，避免普通测试多出一套
  `<XRController>` 设备。
- `HandHoldSimulatorIndicator` 位于 Controller 物理跟踪点，只读取
  `HandHoldInteractable.IsWithinHoldRange()` 和实际选择状态：

```text
蓝色/粉色 = 未进入掌心近场
绿色      = 已进入 0.10 m 准入范围
黄色      = 已建立 XRI Select / HandHoldAction
```

- Controller 与 Palm 标记均标记为 `EditorOnly`，没有 Collider，Android 构建会剔除，
  不改变真机交互和射线命中。

### 2. 启动链路

```text
MateSoul/Game/Run Hand Hold Simulator
  -> GameSceneBuilder.BuildScene()
  -> 打开 MateSoul.unity
  -> EditorApplication.isPlaying = true
  -> EnteredPlayMode
  -> 实例化 XR Device Simulator Prefab
  -> XRSimulatedHMD / XRSimulatedController
  -> 既有 ActionBasedController / ControllerItemRay
  -> HandHoldInteractable / HandHoldAction / HandIKRig
```

不应直接打开普通 PlayMode 后期待 Simulator 自动出现；统一使用上述菜单或
`Cmd+Shift+K`。

### 3. 操作映射

- `Y`：切换/锁定右控制器。
- `T`：切换/锁定左控制器。
- `W/S/A/D/Q/E`：移动当前模拟设备。
- 鼠标：调整当前设备朝向。
- `G`：模拟 Controller Grip，驱动生产 `<XRController>/grip` 绑定。
- `Esc`：停止当前设备操控。

右控制器位姿球进入绿色掌心目标约 `0.10 m` 后才允许 Grip 建立牵手；保持状态使用
`0.16 m` 退出半径。射线末端与近场距离无关。

### 4. 验证记录

- `HandHoldTests`：`9/9`。
- `BowlingTests` 隔离回归：`8/8`。
- Editor PlayMode：官方 Simulator UI 正常显示，`Right Controller` 可选，
  `MATESOUL_READY source=PICO SDK MockSceneData items=0 itemState=ready navMs=5`。
- 全量测试存在一个与本功能无关的顺序相关 Bowling 输入用例抖动；该套件隔离复跑通过，
  不作为 Hand Hold Simulator 阻断项。

## Phase Fragile Props 技术交接：组合放置与碰撞破碎（2026-10-08）

### 1. 阶段概述

本阶段把“目录内容”和“运行时物理实体”解耦：

- 目录只显示一个 `chopsticks`，放置后得到可分别抓取和分离的左右两根筷子。
- 易碎能力由 `ItemDefinition` 显式配置，当前只应用于盘子、马克杯和碗。
- 破碎只接受非持有状态下的真实碰撞力；抓取甩动不会直接触发。
- 原物品、碎片、暂停、存档和销毁继续服从 `GameSession/PhysicsBody` 的单一所有权。

### 2. 数据与组件职责

- `BreakableSettings`
  - `enabled`：内容准入开关。
  - `lightForceThreshold/heavyForceThreshold`：轻度和重度碰撞力阈值。
  - `lightFragmentCount/heavyFragmentCount`：两级碎片数量。
  - `debrisLifetime`：非持久化碎片寿命。
  - `Validate()` 强制阈值递增、重度碎片更多且所有数值有限。
- `ItemPlacementPart`
  - 持有成员 `ItemDefinition`、局部位置和局部旋转。
  - 禁止引用自身或另一个组合定义，避免递归展开和循环依赖。
- `ItemDefinition`
  - `Breakable` 决定 `GameItem.Bind` 是否挂载 `BreakableItem`。
  - `PlacementParts` 非空时，该定义是目录组合入口而非运行时实例模板。
- `BreakableItem`
  - 监听 `OnCollisionEnter`，计算碰撞力、执行状态准入并选择破碎等级。
  - 在创建碎片成功后调用 `GameItem.TryBreak`，由会话删除原物品。
- `BreakableDebris`
  - 动态生成凸三棱柱碎片 Mesh、MeshCollider 和 `PhysicsBody`。
  - 继承原速度/角速度并添加碰撞点外扩速度，负责定时回收 Mesh。
- `GameSession`
  - 原子展开组合放置；跟踪碎片并统一暂停、恢复、销毁。
  - 破碎时从 `views`、`ItemRegistry` 和 `GameSaveData.items` 删除原实例。

### 3. 组合筷子放置流程

1. `GameSceneBuilder.BuildCatalog` 生成隐藏的 `chopstick-left/right` 独立定义。
2. `BuildChopsticks` 生成可见的 `chopsticks` 入口，并配置左右各 `4.5 mm` 局部偏移。
3. `GameSession.PlaceItemAsync` 使用组合入口的整体 footprint 做一次空位检查。
4. 创建或复用房间锚点后再次检查占用，随后为每个成员分配独立实例 ID 并调用
   `Spawn`。
5. `Save()` 一次写入两条成员记录；任一异常会销毁已创建成员并恢复原存档和锚点根。
6. 旧存档若直接引用 `chopsticks`，恢复路径仍会实例化入口自带的单根预览 Prefab，
   保证定义升级向后兼容。新放置不会保存组合入口。

成员定义必须注册到会话的 definitions 字典，否则组合放置会 fail-closed，并报告
“组合物品成员未注册”。目录 UI 只筛选 `CatalogVisible=true`，因此隐藏成员不会形成
重复入口。

### 4. 破碎判定流程

1. `BreakableItem.Configure` 设置内容参数，并以 `Time.fixedTime + 0.25 s` 延迟武装。
2. `OnCollisionEnter` 读取 `collision.impulse.magnitude`，除以
   `Time.fixedDeltaTime` 得到本项目使用的平均碰撞力代理。
3. 低于轻度阈值直接返回；达到重度阈值选 `Heavy`，否则选 `Light`。
4. 以下任一条件成立时拒绝破碎：
   `GameItem.IsHeld`、`PhysicsBody.IsFollowing`、`PhysicsBody.IsPaused`、
   `PhysicsBody.IsContact`。
5. 捕获原物品 Pose、线速度、角速度和非高亮材质，创建相应数量的碎片。
6. `GameSession.BreakItem` 删除原物品和存档记录并更新提示；碎片不进入存档。

当前内容参数：

| ID | 轻度阈值 | 重度阈值 | 轻/重碎片 | 寿命 |
|---|---:|---:|---:|---:|
| `plate` | 45 N | 95 N | 4 / 22 | 20 s |
| `mug` | 38 N | 80 N | 6 / 22 | 20 s |
| `bowl` | 35 N | 72 N | 4 / 12 | 20 s |

### 5. 生命周期与设计约束

- 预碎模型按各片网格顶点数分配原物品总质量；程序化回退使用等分质量。两者仍通过
  `PhysicsBody` 受力，不直接操作碎片 Rigidbody 状态。
- `GameSession.SetPaused` 同时暂停物品和所有活动碎片；`Dispose` 回收两者。
- `BreakableDebris.OnDestroy` 注销会话引用，并只销毁程序化回退创建的运行时 Mesh；
  预碎模型共享的资产 Mesh 不由实例销毁。
- 碎片是瞬态结果，不应加入 `ItemRegistry`、目录或存档。
- 盘子和马克杯使用预碎美术 Prefab，碗仍使用程序化回退。新增美术资源不能另建平行的
  破碎会话、暂停或存档所有权。

### 6. SDK 与物理避坑

- `Collision.relativeVelocity` 不能稳定表达质量和接触求解结果；本阶段使用 PhysX 已求解
  冲量除以固定步长。修改 `Time.fixedDeltaTime` 后必须重新校准阈值。
- 只在 `OnCollisionEnter` 判定，避免持续接触每帧累计造成静置物品自发破碎。
- 仅检查 `IsHeld` 不足以覆盖选择退出与物理跟随之间的过渡，因此必须同时检查
  `IsFollowing`；暂停和接触姿态也在同一准入边界拒绝。
- 组合入口的 Prefab 只是目录预览和旧存档兼容载体，不能把它误当成新放置后的第三根
  筷子。
- 构建脚本必须使用正式 `BuildAndroid()`，即 `BuildOptions.None`；Development 构建
  不能晋升为根目录交付 APK。

### 7. 资源与验证

核心文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemDefinition.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameItem.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameSession.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/BreakableItem.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/BreakableDebris.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/GameSceneBuilder.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/GameRuntimeTests.cs`

验证结果：

- `GameRuntimeTests`：26/26。
- `PhysicsRuntimeTests`：5/5。
- `GripGrabTests`：2/2。
- Release APK 大小 `108,933,754` 字节，SHA-256
  `bafc04df36b8827cd6302caee41b8ed5f29f1620c523ab84bf769dce1f685f4a`。
- 宿主与模拟器安装包哈希一致，应用保持前台并输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=2 itemState=ready navMs=34`。

## Phase Idle 01 技术交接：待机动画导入与控制器接入（2026-10-08）

### 1. 阶段概述

新待机沿用角色现有的单一 Animator 和 `Speed` BlendTree，不新增第二个 Animator、
并行动画状态机或运行时 Transform 写入者。最终运行资源为：

- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Idle01/Lengyue_Idle01_Combined.anim`
- 30 FPS，9.1667 秒，循环播放。
- `Speed=0` 播放新待机；接近 `1.1 m/s` 时混合到既有 Walk Clip。

### 2. 导入器职责

`/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Editor/LengyueIdle01Builder.cs`
负责完整导入流程：

1. 将骨骼和表情 FBX 配置为 Generic、No Avatar、关闭动画压缩并保留层级。
2. 按骨骼名称映射到正式 `Lengyue_GameCharacter.prefab`，其中源 `Head` 映射为目标
   `Head 1`。
3. 使用目标 Prefab 的静止局部位置和缩放作为基准，重建旋转、位置和缩放曲线。
4. 排除 `H_*`、`Hair_*`、`Backhair_*`、`Skirt_*`、`OuterSkirt_*`、
   `CTRL_*`、`DRV_*`、`MCH_*` 和 IK/Fulcrum 骨骼，避免覆盖头发、裙摆和手部
   Animation Rigging。
5. 仅接受能够唯一匹配目标 Renderer 与形态键名称的
   `SkinnedMeshRenderer.blendShape.*` 曲线；错误绑定直接失败，不做静默映射。
6. 生成循环 Clip，并同时更新正式 `Avatar.controller` 与源
   `Lengyue_Locomotion.controller`。

重新导入入口为 `MateSoul > Lengyue > Import Idle 01`。
`GameSceneBuilder.BuildAnimator()` 同样固定读取新 Clip，保证场景重建结果一致。

### 3. 运行时切换

运行时切换逻辑没有新增分支：

1. `AvatarMotor` 继续把水平实际速度写入 Animator 的 `Speed` 参数。
2. 行走时 BlendTree 从新 Idle 向既有 Walk 混合。
3. 路径结束、动作停止或牵手结束时，`AvatarMotor.Stop()` / `EndHandHold()` 将
   `Speed` 写回 0。
4. Animator 自动回到新待机；动画不接管角色根节点位移，`applyRootMotion` 仍关闭。

### 4. 表情 FBX 完整性问题

当前
`/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Idle01/Lengyue_Idle01_BlendShape_InPlace.fbx`
不是可用的形态键动画文件。对 FBX 7400 原始节点和 Unity 曲线分别检查后得到：

- `Shape` 节点：0。
- `BlendShape` 节点：0。
- `BlendShapeChannel` 节点：0。
- Unity `SkinnedMeshRenderer.blendShape.*` 曲线：0。
- 唯一动画目标：`Lengyue_Bob_Hair_v002` 的位置与旋转。

因此当前 `Lengyue_Idle01_Combined.anim` 只包含已验证的骨骼待机，表情保持默认值。
不能根据文件名或静态面部网格伪造原始表情时序。

美术重新导出时必须启用 BlendShape/Morph Target 导出，并确认 FBX 内存在
`BlendShapeChannel` 及权重动画曲线。覆盖同名文件后重新执行导入菜单，构建器会自动
合并表情曲线；若 Renderer 或形态键名称不匹配，则会 fail-closed 并指出具体绑定。

### 5. 验证与资源

- 映射骨骼：69。
- 源变换曲线：690。
- 缺失目标骨骼：0。
- 生成 Clip：`m_SampleRate=30`、`m_StopTime=9.166667`、`m_LoopTime=1`。
- Play Mode 正常进入并输出 `MATESOUL_READY`，未发现空引用或资源引用异常。
- 导入报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Idle01/reports/unity_import.json`。

## Phase Fracture Art V2 技术交接：参考图预碎模型（2026-10-08）

### 1. 阶段概述

本阶段只替换碎片几何来源，不改变碰撞力阈值、抓取免碎、原物品删除、暂停或存档语义：

- 盘子轻度/重度分别使用 4/22 个专属碎块。
- 马克杯轻度为 5 个杯体大块加完整把手，重度为 20 个杯体碎块加 2 个把手段。
- 碗和未配置专属资源的未来物品继续使用原程序化楔块。
- 四套美术模型保留原 UV、角色图案、盘沿/杯沿、杯底和把手，新增浅色陶瓷断面材质。

### 2. 建模管线

可重复构建入口：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Breakables/scripts/build_breakable_assets.py`

流程：

1. 从盘子、马克杯正式 `.blend` 追加源网格与材质，不修改原始资产。
2. 盘子轻度使用共享边界的锯齿放射切割；重度使用外圈、内圈、中心三层 Voronoi。
3. 马克杯轻度在俯视平面做五分杯体并保留完整把手；重度按高度分三层切成 20 块，
   把手再分成上下两段。
4. 每个切割单元对原网格做 Boolean Intersect，保留原材质槽、`UVMap` 和 `MicroUV`；
   Boolean 新面映射到 `MAT_Ceramic_Fracture`。
5. 每片原点重置到自身包围盒中心，FBX 仍保留其装配局部位姿。
6. 导出四份 FBX，同时写入 Unity `PropsSource`，生成预览和 JSON 几何报告。

源文件与报告：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Breakables/source/LavenderCeramicFractures.blend`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Breakables/reports/fracture_model_report.json`

几何预算：

| 变体 | 碎片 | 顶点 | 三角形 |
|---|---:|---:|---:|
| `PlateLightFracture` | 4 | 3,458 | 6,900 |
| `PlateHeavyFracture` | 22 | 4,994 | 9,900 |
| `MugLightFracture` | 6 | 5,426 | 10,828 |
| `MugHeavyFracture` | 22 | 6,788 | 13,488 |

四套模型均为闭合流形，非流形边为 0。

### 3. Unity 内容装配

`BreakableSettings` 新增：

- `lightDebrisPrefab`
- `heavyDebrisPrefab`

两个引用必须同时为空或同时配置；只有一个引用时 `Validate()` 直接失败。

`GameSceneBuilder.BuildBreakableSettings` 负责：

1. 读取四份 FBX。
2. 校验 `MeshRenderer` 数量与内容配置一致。
3. 将导入材质映射为 URP Lit，共用现有盘子/马克杯贴图。
4. 为断面材质设置浅紫白 BaseColor、较低光滑度和轻微 Emission。
5. 移除美术资源中可能存在的 Collider/Rigidbody。
6. 生成
   `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/Breakables/`
   下四个正式 Prefab。

### 4. 运行时实例化

`BreakableDebris.Create` 根据等级选择专属 Prefab：

1. 实例化模板并按名称稳定排序所有 `MeshFilter`。
2. 校验实际碎片数、网格和 Renderer。
3. 运行时为每片添加凸 `MeshCollider` 与 `PhysicsBody`。
4. 按 `mesh.vertexCount / totalVertexCount` 分配原物品总质量。
5. 保留原装配位姿，继承原物品线速度/角速度并施加碰撞点外扩速度。
6. 继续注册到 `GameSession`；暂停、寿命、销毁和非持久化语义不变。

未配置 Prefab 时进入 `CreateProceduralFragments`，保留碗和旧内容兼容性。

### 5. 避坑

- Unity `Object` 的“已销毁伪空对象”不会被 C# `??` 识别。动态补组件必须使用
  `if (component == null)`，否则会对已销毁的 `MeshCollider` 壳引用赋值并抛
  `MissingComponentException`。
- 美术 Prefab 不应预挂 Rigidbody/Collider；这些组件由运行时按公共 `BodySettings`
  统一创建，避免重复刚体所有权。
- 高面数视觉网格在本轮 PhysX cooking 中未出现 255 polygon 或凸包失败警告；若后续
  模型复杂度继续增长，应导出独立低面数凸碰撞代理，不要依赖真机运行时降面。
- Unity 空白验证场景没有环境光时，背光陶瓷面会显示为纯黑；材质验收必须设置环境光，
  不能据此误判为贴图丢失。

### 6. 验证

- Blender 四套模型：数量准确，全部闭合流形。
- Unity URP：贴图、断面、盘沿、杯沿与把手显示正常。
- `GameRuntimeTests`：26/26。
- `PhysicsRuntimeTests`：5/5。
- `GripGrabTests`：2/2。
- Release APK：`111,764,624` bytes，SHA-256
  `aa6935cc54fba6f747685277ec51de53879a37666552857d77d64b075399a6ca`。
- APK v2 签名、`arm64-v8a`、PICO VR/Spatial Data 元数据通过。
- 宿主与模拟器安装包哈希一致；冷启动输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=0 itemState=ready navMs=33`。

## Phase Fragile Audio 技术交接：破碎空间音效（2026-10-08）

### 1. 数据契约

`BreakableSettings` 新增 `AudioClip breakSound`。易碎配置启用时该字段必须非空；
`Validate()` 与阈值、碎片数和 Prefab 对一起 fail-closed。

当前盘子、碗、马克杯统一引用：

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Audio/CeramicBreak.mp3`

素材属性：

- MP3，44.1 kHz，源文件双声道。
- Unity 导入后强制单声道，适合作为 3D 点声源。
- PCM、Decompress On Load、Preload Audio Data、关闭 Load In Background。
- Unity Clip 长度 `2.06366 s`；比容器声明的 2 秒略长是 MP3 编码延迟。

### 2. 播放流程

1. `BreakableItem.HandleImpact` 通过原有阈值和抓取状态准入。
2. `BreakableDebris.Create` 先完整创建碎片和物理体。
3. `GameItem.TryBreak` 成功删除原物品后，调用
   `BreakableDebris.PlayBreakSound(settings.breakSound, severity)`。
4. `PlayBreakSound` 在碎片根节点添加唯一 `AudioSource` 并调用 `Play()`。
5. 原物品可以安全销毁；AudioSource 随碎片根节点继续播放并在碎片寿命结束时回收。

事务失败、持有免碎和低于阈值路径不会播放声音，避免“听到破碎但物品仍存在”。

### 3. 空间参数

| 参数 | 轻度 | 重度 |
|---|---:|---:|
| Volume | 0.72 | 0.95 |
| Pitch | 1.04 | 0.94 |
| Spatial Blend | 1.0 | 1.0 |
| Min/Max Distance | 0.35 / 7 m | 0.35 / 7 m |
| Doppler Level | 0.2 | 0.2 |
| Spread | 25° | 25° |

重度使用更低音高和更高音量，轻度使用稍高音高，不复制或随机叠加多个播放器。

### 4. 生命周期

- `BreakableDebris.SetPaused(true)` 同时暂停所有 `PhysicsBody` 和破碎音效；
  恢复时调用 `AudioSource.UnPause()`。
- `GameSession.Dispose` 或 20 秒碎片寿命结束会销毁碎片根节点及 AudioSource。
- 音效不进入存档，不建立独立音频状态机。

### 5. Unity 2022.3 避坑

- 当前版本不存在 `AudioImporter.normalize` 公共属性。
- `AudioImporter.preloadAudioData` 已废弃并在编译时直接报错；必须改用
  `AudioImporter.defaultSampleSettings.preloadAudioData`。
- MP3 导入后的 `AudioClip.length` 包含编码器延迟，测试不能按容器声明时长做过窄的
  精确断言。
- `AudioSource` 不能挂在会立即销毁的原 `GameItem` 上，否则破碎声会被截断。

### 6. 验证与构建

- 破碎播放与轻/重参数：1/1。
- 生产目录共享 Clip、单声道及导入设置：1/1。
- `PhysicsRuntimeTests`：5/5。
- `GripGrabTests`：2/2。
- `GameRuntimeTests`：24/26；音频相关断言全部通过，两个失败均为独立可复现的既有
  角色移动/推墙基线。
- Release APK：`111,935,968` bytes，SHA-256
  `d3a7f25371b255d13ddb85d6a6229c8a9a9ea5480609e84a19edaf9ba0cf6b0b`。
- APK `sharedassets0` 可检索到 `CeramicBreak`，设备与宿主 APK 哈希一致。
- PICO Emulator 冷启动输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=1 itemState=ready navMs=296`，
  无 Unity、FMOD 或 Audio 异常。

## Phase AI Emotion Audio 技术交接：单轮情绪识别与语气音效（2026-10-08）

### 1. 契约边界

AI 网关 `ConversationReply` 新增必填字符串 `response_emotion`：

| Wire value | Unity enum | 语义 |
|---|---|---|
| `normal` | `ResponseEmotion.Normal` | 中性、事实说明、思考或轻微迟疑 |
| `happy` | `ResponseEmotion.Happy` | 开心、被逗笑、感谢或亲近 |
| `sad` | `ResponseEmotion.Sad` | 沮丧、受伤、失望或难过 |
| `angry` | `ResponseEmotion.Angry` | 生气、恼火、被冒犯或不满 |

网关模型必须基于完整历史与当前玩家输入，判断“角色在本轮回复中表达的情绪”，不能
只复述玩家情绪。`ModelReplySchema` 使用 Zod 枚举强校验；缺失或非法值触发现有一次
repair，repair 仍失败则整轮请求失败。

### 2. 长期心情与单轮语气

- `MoodSystem` 是长期状态所有者：保存 meter、活动心情、强度、计时和衰减。
- `ResponseEmotion` 是单轮输出标签：不存档、不衰减、不参与心情优先级。
- `mood_signals` 可以改变长期心情；`response_emotion` 只控制本轮音效。两者允许
  不同，例如角色长期沮丧时仍可能因玩家安慰而用 `happy` 回复。

不得把 `response_emotion` 写回 `MoodSystem`，也不得从 `MoodSystem.ActiveId` 反推
语气音效，否则会失去上下文判定结果并造成跨轮污染。

### 3. Unity 运行流程

1. `HttpLLMProvider` 将网关 JSON 反序列化为 `ConversationReply`。
2. `ConversationOrchestrator` 校验 `reply_text` 后调用
   `ResponseEmotionProtocol.TryParse`；非法值使会话进入 `Failed`。
3. 动作、长期心情信号和历史记录成功落地后，编排器设置
   `LastResponseEmotion`，递增 `ReplySequence`，最后进入 `Replied`。
4. `ResponseEmotionAudio` 订阅 `ConversationOrchestrator.Changed`，只处理尚未消费的
   成功回复序号，因此 `Thinking`、取消、失败和重复通知都不会播放。
5. 播放器从对应池使用 `UnityEngine.Random.Range` 选择一个 Clip，先停止上一条语气音，
   再从 Humanoid 头部骨骼上的专属 `AudioSource` 播放；无 Humanoid 头骨时回退到角色
   根节点。

`ResponseEmotionAudio` 是本功能唯一音频播放所有者。`GameBootstrap` 调用
`ResponseEmotionAudio.Attach(character, Session.Conversation)` 完成资源加载和绑定；
装配失败时会释放已创建的 `GameSession` 与锚点服务。

### 4. 资源映射

资源根目录：

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Resources/ResponseEmotionAudio/`

目录即映射，不依赖角色名或素材文件名分支：

- `Normal/`：2 个常态音效。
- `Happy/`：2 个开心音效。
- `Sad/`：1 个沮丧音效。
- `Angry/`：2 个生气音效。

`Resources.LoadAll<AudioClip>` 在启动装配阶段一次性加载四个池。每个池至少一个 Clip，
空池或空引用直接抛配置异常。项目副本为 44.1 kHz、16-bit 单声道 WAV，Unity 导入
设置为 Vorbis、Decompress On Load、预加载且不后台加载。

空间音源固定参数：

| 参数 | 值 |
|---|---:|
| Spatial Blend | 1.0 |
| Volume | 0.85 |
| Doppler Level | 0 |
| Min Distance | 0.6 m |
| Max Distance | 8 m |
| Rolloff | Logarithmic |

### 5. 扩展约定

- 增加同类音效：放入对应情绪目录，不修改选择代码。
- 增加第五种情绪：必须同步扩展网关 Zod 枚举、Prompt 语义、Unity enum/parser、资源
  目录和契约测试；不能只在一端增加字符串。
- 增加表情或动作：消费同一 `ResponseEmotion`，需要身体控制时提交已注册能力给
  `AvatarActionCoordinator`，不得由音频组件直接写 Animator 或 Transform。
- 切换 TTS：以新的语音播放所有者替换 `ResponseEmotionAudio`，不要让 TTS 与语气
  音效并行抢占同一角色声音。

### 6. 验证

- `services/ai-gateway/npm run check`：14/14。
- `MateSoul.Tests.GameplayTests` 与
  `MateSoul.Tests.ResponseEmotionAudioTests`：23/23。
- 真实 DeepSeek + MCP 请求
  `aab3c4c3-da6d-4db1-9f41-73fd8f461428` 返回合法
  `response_emotion: normal`。
- 覆盖四态映射、非法协议拒绝、回复序号去重、资源池数量、随机选择和 3D 播放参数。
- Unity 2022.3.38f1 已完成脚本编译和 WAV 导入；最终头显响度与衰减仍需真机验收。

## Phase Movement Stall Fix 技术交接：移动进度不变量与 Idle 归零（2026-10-08）

### 1. 失败模型

NavMesh 可达不等于动态刚体一定能前进。未进入 NavMesh 构建源的 Collider 可能在物理
层挡住角色，但 `NavMesh.CalculatePath` 仍持续返回完整路径。旧实现只以“距离当前
拐点小于 `0.12 m`”作为完成条件，每秒重算同一路径，因此物理速度接近零时动作仍会
一直处于 Running。

### 2. 所有权与判定

失速判定属于 `MoveAction`，而不是 Animator、PhysicsBody 或中央协调器：

1. `Start()` 记录当前位置并清零阻塞计时。
2. 每次 `motor.Tick()` 后，只计算水平实际位移。
3. 从上一个有效进度点累计位移达到 `0.03 m` 时，更新进度点并清零计时。
4. 未达到进度阈值时累计活动时间；达到 `1.25 s` 后先调用 `motor.Stop()`，再以
   `Movement is blocked by an obstacle.` 失败退出。
5. `AvatarActionCoordinator` 负责释放租约和调用 `Dispose()`；重复 `Stop()` 幂等。

该计时不在每秒 NavMesh 重规划时重置，否则同一条错误路径会永久延长动作。正常
`1.1 m/s` 行走、推轻物体和爬坡远高于进度阈值，不会因单次接触被误判。

### 3. 动画速度边界

`AvatarMotor.SetAnimationSpeed()` 是 Animator `Speed` 的唯一写入助手：

- 实际水平速度 `< 0.05 m/s` 时写入 `0`。
- 其他速度保持原值，不改变 BlendTree 的 Walk 阈值 `1.1`。
- `Stop()`、`EndHandHold()`、普通路径 Tick 和牵手 FixedUpdate 使用同一入口。

物理运动继续由 `PhysicsBody` 独占；低速死区只改变视觉状态，不篡改 Rigidbody
速度，不影响推物、碰撞、坐卧、牵手或导航。

### 4. Idle 运行时事实

- 生产 Prefab 引用 `Avatar.controller`，其 `Speed=0` 子节点引用
  `Lengyue_Idle01_Combined`。
- 正常到达和障碍阻塞测试均要求该 Clip 权重 `> 0.99`。
- Clip 为 30 FPS、9.1667 秒、循环；根节点无位移。
- 源动画中段头部相对起始约 `24.88°`，右上臂约 `3.71°`；骨盆和双腿接近静止。
- BlendShape FBX 曲线数仍为 0，所以当前待机没有配套表情变化。远景观察时应重点看
  头部和右臂，不能以腿部是否明显移动判断是否播放。

### 5. 验证

- 定向用例：
  `ProductionCharacterMovesAnimatesAndStopsWithoutLeavingNavMesh`、
  `CharacterPushesLightObjectButCannotWalkThroughAnUnmappedWall`，`2/2` 通过。
- 全量框架：117 通过、1 个既有保龄球抓取位置用例失败、1 跳过；移动相关回归均通过。
- APK 与设备 SHA-256：
  `d3a7f25371b255d13ddb85d6a6229c8a9a9ea5480609e84a19edaf9ba0cf6b0b`。
- 冷启动输出 `MATESOUL_READY`，无启动崩溃。
- 数值报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/movement_stall_idle_fix_20261008.json`。

## Phase Fragile Audio Timing 技术交接：播放请求前移（2026-10-09）

### 1. 修复目标

旧流程先实例化全部碎片、创建并 cooking 凸 `MeshCollider`，最后才调用
`AudioSource.Play()`。真机上的碎片构建成本会直接转化为可感知的声音滞后。

本阶段保持 `BreakableDebris` 对碎片和一次性音源的统一所有权，只调整同一同步事务内
的顺序，不增加公共播放器、异步状态机或物品名称分支。

### 2. 当前执行顺序

1. `BreakableItem.HandleImpact` 完成阈值、持有、跟随、暂停和受控接触准入。
2. `BreakableDebris.Create` 创建碎片根节点并配置一次性 3D `AudioSource`。
3. 立即记录 `BreakSoundRequestedAt` 并调用 `AudioSource.Play()`。
4. 实例化轻度或重度预碎 Prefab，为每片配置凸碰撞体、`PhysicsBody`、质量和初速度。
5. 碎片完成后记录 `FragmentsReadyAt`，输出 `BREAK_SOUND_TIMING`，再注册到
   `GameSession`。
6. `GameItem.TryBreak` 删除原物品并提交存档；同一帧结束前不会渲染中间状态。

因此声音请求必然早于碎片就绪。音源仍挂在碎片根节点上，原物品销毁不会截断声音。

### 3. 可观测性

`BreakableDebris` 暴露只读运行态证据：

- `BreakSoundRequestedAt`：调用播放前的 `Time.realtimeSinceStartupAsDouble`。
- `FragmentsReadyAt`：所有碎片和碰撞体创建完成后的时间。
- `BREAK_SOUND_TIMING ... leadMs=...`：两者差值，正值表示播放请求领先。

这些字段用于测试和性能证据，不参与业务判定、存档或暂停恢复。

### 4. 验证结果

- 破碎时序定向用例：1/1。
- `PhysicsRuntimeTests`：5/5。
- `GripGrabTests`：2/2。
- 播放请求领先碎片完成 `0.9-16.0 ms`；覆盖盘子轻/重、真实坠落和马克杯轻/重。
- Release APK：`112,083,919` bytes，SHA-256
  `93220fc7ec7bea1b31dc13324ee83415b4cf9f296896592457e0148722237b3d`。
- 设备与宿主 APK 哈希一致；冷启动输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=3 itemState=ready navMs=66`。

### 5. 平台边界

- `AudioSource.Play()` 记录的是 Unity 播放请求，不是扬声器声压到达时间。最终音画同步
  仍需在 PICO 真机上验收。
- PICO Emulator 本次系统音频 HAL 出现 `pcm_writei` I/O 警告；MateSoul/Unity
  进程内没有 FMOD 或破碎音频异常。不要用该模拟器告警推断真机音频延迟。
- 音频资源仍为
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Audio/CeramicBreak.mp3`，
  导入参数和轻/重空间音频参数均未改变。

## Phase Lavender Retro Radio 技术交接：可交互模型契约（2026-10-09）

### 1. 资产结构

资产根目录：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Radio/`

构建入口：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Radio/scripts/build_radio_asset.py`

主根节点为 `LavenderRetroRadio_240x90x160mm_Root`。建模坐标为米制 Z-up，正面朝
`-Y`。机身尺寸 240×90×160 mm，完整高度 268 mm。

模型使用 47 个闭合网格和 10 个 PBR 材质。中文与频率文字已经转成低细分实体网格，
不依赖运行时字体或外部贴图。GLB 无外部图片 URI。

### 2. 交互所有权

Blender 源文件由根节点上的三个归一化属性驱动：

- `power`：`0` 为关、`1` 为开。
- `tuning`：`0..1` 映射为 −135°..+135°。
- `volume`：`0..1` 映射为 −135°..+135°。

稳定控制节点：

- `Radio_PowerSwitch_Pivot`：开关滑块沿局部 X 从 −4 mm 移动至 +4 mm。
- `Radio_TuningKnob_Pivot`：调台旋钮绕局部 Y 旋转。
- `Radio_VolumeKnob_Pivot`：音量旋钮绕局部 Y 旋转。
- `Radio_DialNeedle_Pivot`：由调台值联动，沿局部 X 从 −60 mm 移动至 +60 mm。

根物品继续拥有拾取和刚体状态；各控件只修改自己的枢轴，不直接改外壳 Transform。
完整机器可被拾取时，运行时应阻止控件拖拽与整机抓取同时取得同一输入。

### 3. 导出契约

交互定义：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Radio/reports/radio_interaction_manifest.json`

GLB 通过节点 extras 保存 `interaction_type`、轴向、范围和默认值；FBX 保留同名 Empty
及 custom properties。Blender Driver 本身不会在 Unity 中执行，因此 Unity 接入层必须：

1. 按稳定节点名绑定三个控件和调谐指针。
2. 把旋钮输入归一化到 `0..1`，再映射到 −135°..+135°。
3. 调台值同时映射到指针局部 X 的 −60..+60 mm。
4. 电源只在释放后的新一次操作中切换，避免按住时重复触发。
5. 音量值最终由收音机状态所有者写入 AudioSource，不允许交互组件直接维护第二份音量状态。

### 4. 验证

报告：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Radio/reports/radio_model_report.json`

当前验证覆盖：

- 机身包络精确为 240×90×160 mm。
- 全部 47 个网格无边界边、无非流形边。
- Blender 三组 Driver 的最小值和最大值实测通过。
- GLB/FBX 回读尺寸与源文件一致，三个控制枢轴均保留。
- GLB extras 与外部图片 URI 检查通过。

当前只交付模型侧交互契约，尚未绑定具体 Unity/PICO 手势、射线、音频源或电台内容。
这些运行时能力应在玩法需求确定后通过单一收音机控制器接入，不应修改或合并上述枢轴。

## Phase Idle Lower-Body Fix 技术交接：待机语义与导入不变量（2026-10-09）

### 1. 证据结论

生产角色仍只有一个 Animator，Controller 为 `Avatar`，Locomotion BlendTree 使用
`Speed` 在 `Lengyue_Idle01_Combined` 与 `Lengyue_Walk_BodyOnly` 之间切换。

墙阻塞后的运行时采样确认：

1. Rigidbody 水平速度约为 0。
2. Animator `Speed=0`。
3. 当前 Clip 为 `Lengyue_Idle01_Combined`，权重 1。
4. 修复前连续 0.5 秒 Idle 采样仍有 `1.74°/5.12°` 左右腿变化。

因此根因位于动画内容而非 Animator 状态残留、第二 Animator 或旧 Controller。

### 2. 导入器约束

修改文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Editor/LengyueIdle01Builder.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Idle01/Lengyue_Idle01_Combined.anim`

`BuildClip` 对 `Leg_L/R`、`Knee_L/R`、`Foot_L/R`、`Toe_L/R` 和 `Mech_*`
骨骼始终采样源动画首帧。其他映射骨骼继续按原时间采样，不缩短新待机，也不回退旧
Idle。这样可同时满足：

- 下肢在 `Speed=0` 时不产生步态；
- 上半身保留新 Idle 的头部、躯干和手臂运动；
- `Speed>0` 时仍由同一 BlendTree 平滑混合到现有 Walk；
- 后续重新执行 `MateSoul > Lengyue > Import Idle 01` 不会重新引入问题。

### 3. 运行时保护

`MoveAction` 连续 1.25 秒水平位移不足 3 cm 时停止动作并报告阻塞。
`AvatarMotor` 把低于 0.05 m/s 的实际水平速度写为 Animator `Speed=0`。两者分别处理
动作生命周期和接触抖动；动画导入器则保证 `Speed=0` 对应的 Clip 内容确实是待机。
三层职责不可互相替代。

### 4. 回归不变量

`GameRuntimeTests.cs` 中的 `LengyueIdleImportTests` 对生成资源执行全周期采样：

- 八个主要下肢骨骼相对首帧最大旋转变化必须 `<0.05°`；
- `Head 1` 最大旋转变化必须 `>5°`，防止把新待机错误冻结为静态 Pose。

运行时另验证正常到达和墙阻塞后均为 `Speed=0`、新 Idle 权重 1。后修复连续静止采样
左右腿变化均为 `0°`。

## Phase Lavender Chibi Clock 技术交接：独立指针枢轴（2026-10-09）

### 1. 资产结构

资产根目录：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Clock/`

参数化构建入口：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Clock/scripts/build_clock_asset.py`

主根节点为 `LavenderChibiClock_160x40mm_Root`。建模坐标为米制 Z-up，正面朝 `-Y`，
尺寸为 160×40×160 mm。模型使用 9 个闭合网格和 6 个 PBR 材质，不依赖外部贴图。

### 2. 指针契约

稳定节点：

- `Clock_HourHand_Pivot`：时针旋转所有者。
- `Clock_MinuteHand_Pivot`：分针旋转所有者。
- `Clock_HourHand`：时针视觉网格，只能作为 Hour Pivot 子节点。
- `Clock_MinuteHand`：分针视觉网格，只能作为 Minute Pivot 子节点。

两个 Pivot 的世界位置均为 `(0, -19, 80) mm`，旋转轴为局部 Y。正面观察时，正角度
表示顺时针。Blender 根节点提供：

- `hour_angle_degrees`
- `minute_angle_degrees`

属性范围均为 −180° 至 +180°。默认参考姿态为时针 −52°、分针 +56°。

### 3. 导出与运行时约定

指针清单：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Clock/reports/clock_hand_manifest.json`

GLB extras 和 FBX custom properties 保存节点、轴向与范围，但 Blender Driver 不会在
Unity 中执行。后续时钟系统应：

1. 只缓存并旋转两个 Pivot。
2. 保持表壳根节点负责整体摆放或刚体状态。
3. 用单一时间状态同时计算时针与分针角度，避免两个组件分别维护时间。
4. 不把指针网格重新合并进表盘。
5. 旋转前先确认 Unity 导入后的局部轴向；不要使用世界轴硬编码。

### 4. 验证

模型报告：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Clock/reports/clock_model_report.json`

报告验证整体尺寸、闭合流形、独立父子关系、共享中心、Driver 极值、GLB extras、
GLB/FBX 节点和导出回读尺寸。当前全部断言为 `true`。

构建脚本在渲染与导出阶段临时静音 Driver并应用参考姿态，随后恢复 Driver；这样预览
与导出默认外观正确，同时 Blender 源文件仍可通过角度属性实时控制。

## Phase Radio & Heavy Impact Audio 技术交接（2026-10-10）

### 1. 阶段概述

本阶段新增可放置/抓取的复古收音机，以及由内容配置驱动的三级重物碰撞音效。两项能力
都依附现有 `ItemDefinition → GameItem → PhysicsBody` 生命周期，不创建平行物品系统，
也不在 `GameSession` 中按物品名称分支。

### 2. 收音机所有权

- `RadioController`：每个收音机实例的唯一电源、台位、音量、交叉淡化和暂停所有者。
- `RadioPowerButton`：复用 XR UI press，只提交 `TogglePower()`，不直接操作音源。
- `RadioKnobControl`：把世界空间指针角度映射为 `0..1`，只旋转模型声明的局部枢轴；
  调台旋钮额外移动 `Radio_DialNeedle_Pivot`。
- `RadioBuilder`：导入 FBX/音频、转 URP 材质、建立稳定 UI 命中面、三音源绑定和物品定义。

整机抓取仍由根节点 `PhysicsGrabInteractable/PhysicsBody` 所有；控件在物品被持有时不可
交互，避免同一输入同时旋钮和移动整机。

### 3. 音频状态机

音乐通道 A/B 均关闭 Unity 原生循环，空间参数为全 3D、Logarithmic、`0.45-9 m`：

1. 开机时当前台从音量 0 在 `0.3 s` 内淡入。
2. 调台值映射到三个离散台位；目标跨越多个台时每次只向相邻台移动一步。
3. 每一步启动空闲音乐通道，旧台使用 `cos(t·π/2)` 淡出，新台使用
   `sin(t·π/2)` 淡入。
4. 独立噪声通道使用 `sin(t·π)` 包络，在换台中段达到峰值并与两台内容交融。
5. 曲目剩余时间进入 `2.4 s` 窗口时，空闲通道从同曲开头播放；尾部与开头使用相同
   等功率交叉淡化，完成后交换活动通道。
6. 会话物理暂停或应用暂停时，三个通道一起 Pause/UnPause；禁用或销毁实例时统一停止。

音量输出使用 `normalized² × 0.9`，低档更易细调。三首长音乐使用
`Streaming + Vorbis + Force To Mono`；调频噪声使用 PCM/Decompress On Load。

### 4. 金属重物碰撞

`ImpactSoundSettings` 是 `ItemDefinition` 的可选内容能力：

| 等级 | 受力阈值 | Volume | Pitch |
|---|---:|---:|---:|
| Light | 120 N | 0.46 | 1.04 |
| Medium | 350 N | 0.72 | 1.00 |
| Heavy | 850 N | 0.95 | 0.94 |

`ImpactSoundEmitter` 使用与易碎品一致的
`collision.impulse.magnitude / Time.fixedDeltaTime` 受力代理。桌子、椅子和复古电视
通过各自定义显式启用并共享同一 Clip。两个已标记重物互撞时按组件实例 ID 只允许一侧
播放，避免同一碰撞双响；`0.18 s` 冷却抑制一次倒地的连续接触脉冲。

持有、抓取跟随、会话暂停、受控接触和创建后的前 `0.25 s` 均禁止播放。该组件只拥有
音频，不修改 Rigidbody、物品存档或角色状态。

### 5. 资源路径

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/Radio.prefab`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/Radio.asset`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/PropsSource/Radio/`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Audio/Radio/`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Audio/MetalHeavyImpact.wav`

调频噪声是固定 seed `241` 的带通粉红噪声，可重复生成；用户提供的四个源文件没有被
原地修改。

### 6. 验证与构建

- `RadioTests` 4/4、`ImpactSoundTests` 3/3、`PhysicsRuntimeTests` 5/5、
  `GripGrabTests` 2/2。
- APK：`128,215,436` bytes，SHA-256
  `b4a4e0273a63281f9d3c4d60e8d13f9029bdc50c04463e0036474fb1e9483336`。
- APK v2 签名、ARM64、PICO VR/Spatial Anchor 元数据和五个新音频资源入包检查通过。
- 模拟器冷启动输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=3 itemState=ready navMs=44`。

当前对话端点为本地 HTTP，所以交付物是 Development APK。不得为生成 Release 包放宽
HTTPS 准入；应先配置正式 HTTPS 网关。模拟器系统音频输出不用于真机响度或延迟结论。

## 2026-10-10：模拟器内存诊断入口（根因待确认）

- 入口：`/Users/bytedance/MateSoul/scripts/diagnose-pico-memory.command`，
  Python 标准库实现，从 macOS Terminal 运行；`--self-check` 只验证内存计数 ABI。
- 使用 `libproc.proc_pid_rusage` / `rusage_info_v0` 的 `ri_phys_footprint`、
  `ri_resident_size`、`ri_wired_size`。不能拿 ps 的 VSZ 与物理内存比较；
  当前工具沙箱下 ps 对其他进程曾返回 0，也不能作为已释放的证据。
- 执行顺序：启动当前 AVD → 确认 boot completed → force-stop 游戏取得
  30 秒空模拟器基线 → 调用现有 `start-pico-emulator.command` 校验同步 APK
  并启动 → 180 秒观察 → force-stop 游戏 → 30 秒回收观察 → 结束本轮模拟器。
- 使用原有 6GB AVD、`angle_indirect` 和已构建 APK，未改变游戏资源、图形 API
  或 AVD 参数。首次采样使用10GiB保护线；后续复测使用25GiB进程硬保护线并
  叠加系统压力保护。采样/命令有延迟，这是尽力保护，不是 OS 硬限制，也不表示
  泄漏已修复。
- 约每 10 秒取得一次 `dumpsys meminfo`，提取 PSS、Java/Native Heap、
  Graphics；阶段边界抓取 vmmap。HTTP 事件聚合到
  `/Users/bytedance/MateSoul/.dbg/trae-debug-log-emulator-memory-growth.ndjson`，
  各轮 host-samples.json / summary.json / 原始输出保存在
  `/Users/bytedance/MateSoul/.dbg/emulator-memory-growth/<timestamp>/`。
- 诊断服务器使用 debugger skill 内置脚本，闲置 1200 秒退出，采样入口可重新启动。
  本轮创建的模拟器和启动器在 finally 回收；Ctrl+C 可中止。
- `HV_DENIED` 是当前自动执行环境不能创建 Hypervisor 虚拟机的阻塞证据；
  本次只完成采样工具实测，未完成游戏复现，不得报告任何修复前后收益。
- 已有 `VK_ERROR_OUT_OF_DEVICE_MEMORY` 的具体信息为 timestamp query buffer
  超出单缓冲区尺寸限制；需要结合 guest PSS、宿主 footprint 和 vmmap
  才能判断是否与用户的总内存增长有关。

### 宿主图形增长与 MoltenVK 候选规避

20261010-134741 运行记录确认，空模拟器 footprint 稳定约 8GiB，游戏运行阶段
到达 10GiB 保护线。`owned unmapped (graphics)` 1.5G→3.8G，
region 数 309→1910，而游戏 PSS 加载后仅约 630MiB。Android Graphics=0
不代表宿主 GPU 没有分配。当前证据确定增长所属层，尚未确定完整分配调用链。

- 实际库：
  `/Users/bytedance/MateSoul/.pico-home/6.2/emulator/lib64/gles_angle/libvulkan.dylib`。
  `vkGetVersionStringsMVK` 返回 MoltenVK 1.2.11 / Vulkan 1.2.304。
- 上游参考：
  `https://github.com/google/gfxstream/blob/main/host/vulkan/vulkan_dispatch.cpp`，
  b/364055067 显式将 `MVK_CONFIG_USE_METAL_ARGUMENT_BUFFERS` 设为 `0`。
  同文件要求 Metal heaps，不能通过盲目禁用 MTLHeap 绕过。
- 本次候选修复在
  `/Users/bytedance/MateSoul/scripts/start-pico-emulator.command`
  驱动加载前设置上述开关；已运行的模拟器不会因为重跑启动器而改变此状态，
  必须关闭后重启。不修改设备端 APK 和 AVD 参数。
- 原生测试确认同库开关有效，descriptor pool 的额外 GPU buffer 从
  1081344 bytes 降为0；但两模式释放均正常，不能以此替代真实游戏验证。
- 对比命令：

```bash
/Users/bytedance/MateSoul/scripts/diagnose-pico-memory.command --post-fix
```

采样器在启动 QEMU 前设置同一开关，runId 为 post-fix，额外输出 run.json，
明确是否触发保护线和是否执行完所有阶段。缺少游戏存活、就绪/渲染证据，或
提前退出，都不得当作内存修复通过。原生测试在
`/Users/bytedance/MateSoul/.dbg/emulator-memory-native/`，仅用于本次诊断。

### 25GiB 采样保护策略

`/Users/bytedance/MateSoul/scripts/diagnose-pico-memory.command` 的后续复测策略：

1. QEMU `ri_phys_footprint` 达到25GiB时记录现场并停止。
2. 每10秒执行一次系统安全采样，记录 `memory_pressure -Q`、`vm.swapusage`
   和根卷可用空间。
3. 任一条件成立时提前停止：内存余量≤5%；根卷余量≤8GiB；或内存余量≤10%
   且交换区余量≤128MiB。
4. 停止前抓取限定规模的 guest meminfo、logcat 和宿主 vmmap。
5. `run.json` 写入 `hardLimitGiB`、`stopReason`、`limitReached` 和
   `completedAllPhases`。只有 `completedAllPhases=true` 才表示完成了
   空模拟器、游戏运行和 force-stop 后回收三阶段。

25GiB是本次诊断上限，不是正常内存预算。它解决10GiB过早截断的问题；系统安全条件
负责在16GiB宿主出现真实资源耗尽风险时优先保全机器和证据。

## Phase Radio Control Timing Fix 技术交接（2026-10-10）

### 1. 碰撞音可听起点

`ImpactSoundEmitter.OnCollisionEnter` 原本已在收到 PhysX 碰撞回调后立即调用
`AudioSource.Play()`，并使用 PCM/Decompress On Load/Preload。实测延迟来自素材自身
开头约 `99.7 ms` 的静音，而不是阈值计算或异步解码。

项目副本从原始用户素材的 `94 ms` 位置重建为 44.1 kHz 单声道 PCM：

- 路径：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Audio/MetalHeavyImpact.wav`
- 时长：`2.906 s`
- 前导静音：约 `5.7 ms`
- SHA-256：
  `e6603cf1d2be92b8d8ac30f508ecbb19c718688a84c4f95412990e79ae27796f`

测试读取 Clip 前 `20 ms` 的 PCM 样本，要求首个超过 `0.002` 的样本出现在 `12 ms`
以内。不要用碰撞预测提前播放完整声音：预测无法获得真实 impulse，容易在未发生接触时
误报等级和声音。

### 2. 旋钮连续映射

`RadioKnobControl` 不再把每个指针位置直接映射为绝对值：

1. `OnPointerDown` 只记录当前指针角，不修改旋钮值。
2. 每次 `OnDrag` 使用 `Mathf.DeltaAngle(previous, current)` 得到局部增量。
3. 增量除以 `270°` 后累加到当前归一化值，并 Clamp 到 `0..1`。
4. `+180°/-180°` 由 `DeltaAngle` 自动展开为小增量，不产生满量程跳变。
5. 正向（顺时针）增量对应从“小”到“大”，`RadioController.OutputVolume` 同步增加。

禁用组件时只清理拖动状态，不重置值，避免对象生命周期切换后视觉与控制器状态分裂。

### 3. 开关和频率针

开关端点由 `RadioBuilder.ConfigurePowerSwitchRange` 计算：

- 方向来自 `Radio_Label_Off → Radio_Label_On`，不依赖导入后的 local X 符号。
- 槽体可用区间使用 `Radio_PowerSwitch_Socket` 投影范围。
- 两端扣除 `Radio_PowerSwitch_Slider` 投影半宽和 `1 mm` 安全边距。
- Builder 先将 Pivot 放到“关”端，再把“开-关”局部偏移序列化给
  `RadioPowerButton`。

频率针由 `RadioBuilder.IndicatorRange` 计算：

- 从 `Radio_Frequency_Window` 左右边界扣除指针半宽和 `2 mm` 边距。
- 把两个世界端点转换到 `Radio_DialNeedle_Pivot` 父节点局部坐标。
- `RadioKnobControl.ApplyVisual` 使用 `Vector3.Lerp(min, max, value)`，不依赖 FBX
  导出的初始针位置。

### 4. 构建准入与验证

`GameSceneBuilder.ConfigurePicoBuildSettings` 在最终 BuildPlayer 前显式恢复 Hand、Eye、
VST、Spatial Anchor 和 Spatial Mesh 开关。这样隔离工程即使漏同步设置资产，也不会
生成被启动脚本拒绝的 `enable_vst=0` APK。

- `RadioTests` 5/5、`ImpactSoundTests` 3/3、`PhysicsRuntimeTests` 5/5、
  `GripGrabTests` 2/2。
- APK：`144,508,187` bytes，SHA-256
  `983a6b287c878f67686a4f30362e44f498ba4d0599c7335703495b9a46544cf6`。
- Manifest：VR、VST、Spatial Anchor 全部为 1；APK v2 签名和 ARM64 通过。
- 模拟器冷启动输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=3 itemState=ready navMs=33`。

设备日志中当前分支仍有独立的 `GameHud.ReportWindowGlyphCoverage` 空参数错误；它不在
本阶段调用链内，后续 HUD 任务应单独处理。

## 2026-10-10：虚拟物品交互内存关联诊断

### 1. 已确认边界

`20261010-141110` 采样表明，游戏运行期间QEMU footprint约
8.56→14.88GiB，而Android应用PSS约600→656MiB。宿主
`owned unmapped (graphics)` 由1.5G/304 regions增至8.8G/7034 regions；
游戏force-stop后大部分占用快速释放。

这证明资源归属于游戏图形生命周期，但不能单靠PSS定位Unity业务对象。用户观察到
空闲约9GiB稳定，生成/抓取/投掷/破碎/收音机操作期间增长，因此需区分：

1. spawn的一次性GPU加载；
2. focus的`sharedMaterials`重绑定；
3. grab/release与动态刚体渲染；
4. debris和impact audio；
5. radio交互；
6. 交互唤醒60FPS后放大的PICO Emulator逐帧图形后端泄漏。

### 2. 结构化事件

Development APK临时上报到
`http://127.0.0.1:7791/event`，session为`emulator-memory-growth`，
runId为`interaction-correlation`：

- `GameBootstrap.Update`：2秒周期的FPS、items、held、awakeBodies、debris、
  fragments、poweredRadios、targetFrameRate。
- `GameSession.Spawn`：definition、renderer/collider/vertex数量。
- `GameItem`：focus、grab、release。
- `BreakableDebris.Create`：severity、fragment数量、资源路径类型。
- `ImpactSoundEmitter`：前20次以及之后每25次播放。
- `RadioController` / `RadioKnobControl`：电源与旋钮拖动起止。

请求完成后显式Dispose。不要提高上报频率，否则诊断网络本身会成为变量。

### 3. 采样入口

```bash
/Users/bytedance/MateSoul/scripts/diagnose-pico-memory.command --interaction-correlation
```

宿主仍按2秒记录footprint，Android PSS约10秒一次，25GiB和系统压力保护不变。
事件与宿主样本使用毫秒时间戳关联。主动终止后数据仍保存在
`.dbg/emulator-memory-growth/<timestamp>/`，但`completedAllPhases=false`。

### 4. 构建与准入

- APK：`144,523,739` bytes。
- SHA-256：
  `d96ceeaea5a2ccc0b1c41b35e29714f87ee8a90c729d58327c6c29751b840115`。
- ARM64、Development、APK v2签名、`enable_vst=1`、
  `enable_spatial_anchor=1`、`enable_mesh_anchor=1`。
- `global-metadata.dat`已确认包含`interaction-correlation`。
- `start-pico-emulator.command` 的布尔Manifest检查必须匹配
  `=(type 0x10)0x1$`，不能搜索宽泛的`0x1`，后者会误命中`type 0x10`。

该APK是诊断产物，尚未包含根因修复；不能将构建成功报告为内存问题解决。

## Phase Idle Facial/Cadence 技术交接：满步态权重与面部兜底（2026-10-10）

### 1. 行走权重

Walk Clip 本身仍为 30 FPS、1.0667 秒循环，播放速度为 1。视觉上的“步频降低”实际
来自 BlendTree 权重：角色稳定速度约 `0.992 m/s`，而原 Walk 阈值为 `1.1`，所以
Walk 只有约 `90.2%`，固定下肢 Idle 仍占约 `9.8%`，缩短了腿部摆幅。

生产规则现在是：

- `Speed=0`：100% `Lengyue_Idle01_Combined`。
- `0<Speed<0.9`：用于起步和减速的 Idle/Walk 平滑混合。
- `Speed>=0.9`：100% `Lengyue_Walk_BodyOnly`。

阈值同时由 `GameSceneBuilder.BuildAnimator()` 和
`LengyueIdle01Builder.UpdateControllers()` 写入，避免场景重建或 Idle 重导后回退。

### 2. 面部源文件边界

`Lengyue_Idle01_BlendShape_InPlace.fbx` 当前仍没有
`SkinnedMeshRenderer.blendShape.*` 曲线。导入报告必须分别记录：

- `sourceFacialBindings`：源 FBX 的真实曲线数；
- `facialBindings`：最终运行 Clip 的曲线数；
- `facialFallbackGenerated`：是否生成兜底。

禁止把兜底曲线计为源文件有效。当前值为 `0 / 5 / true`。

### 3. 自然待机兜底

仅当 `sourceFacialBindings==0` 时，导入器向最终 Clip 写入：

```text
Face_Blendshape.face_eye_close_L_BS
Face_Blendshape.face_eye_close_R_BS
Face_Blendshape.face_eyeHappy_L
Face_Blendshape.face_eyeHappy_R
Face_Blendshape.face_smile_BS
```

9.1667 秒周期包含三次短眨眼；柔和眼神和微笑使用低权重缓慢变化，首尾值一致。目标
Renderer 和 BlendShape 后缀必须唯一匹配，否则导入立即失败。有效 Morph FBX 到位后
直接复制源曲线，并自动跳过兜底。

### 4. 回归

`GameRuntimeTests.cs` 覆盖：

1. 正常稳定移动时 Walk 最大权重 `>0.99`。
2. 阻塞和到达后仍回到 Idle。
3. Idle 全周期下肢变化 `<0.05°`，头部变化 `>5°`。
4. 最终 Clip 恰有上述 5 条面部曲线。
5. 采样帧中眨眼、柔和眼神和微笑权重实际作用到正式 `face` Renderer。

## Phase Lengyue Final Asset Export 技术交接：最终资产包与 VRM 边界（2026-10-10）

### 1. 输出位置

- 资产目录：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Release/Lengyue/FinalGameAsset_20261010/`
- 总包：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Release/Lengyue/Lengyue_FinalGameAsset_20261010.zip`
- 构建脚本：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Release/Lengyue/FinalGameAsset_20261010/scripts/build_final_game_asset.py`
- 清单：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Release/Lengyue/FinalGameAsset_20261010/Reports/final_game_asset_manifest.json`

### 2. 文件职责

- `Exports/Lengyue_FinalGameDefault.vrm`：
  从 rigged GLB 包装 VRM 0.x 扩展，写入 51 个 Humanoid 骨映射。
- `Exports/Lengyue_FinalGameDefault_Rigged.glb`：
  默认日常装的带骨骼交换版，含 2 个 Skin。
- `Exports/Lengyue_FinalGameDefault_Rigged.fbx`：
  DCC/美术软件交换版。
- `Exports/Lengyue_FinalGameDefault_VisualExact.glb`：
  直接复用当前视觉快照 `Lengyue_CurrentAppearance_v006.glb`，适合检查外观，
  但它本身不带 Skin。
- `UnityProduction/Lengyue_GameCharacter_UnityProduction_Runtime.zip`：
  运行时权威资产包，包含 Prefab、材质、Mesh、贴图、换装定义、角色脚本、
  Toon Shader 及 meta 文件。

### 3. 生成策略

1. 从
   `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v006/source/Lengyue_ShallowUCollarDress_v006.blend`
   读取带骨源资产。
2. 排除历史旧发型 `Lengyue_Bob_Hair`，保留 `Lengyue_Bob_Hair_v002`。
3. 选择默认 `lavender-casual` 可见 Mesh、`BindJoints` 和
   `Lengyue_HairRig_v002` 导出 Rigged GLB/FBX。
4. 在 Rigged GLB JSON 中追加 `VRM` 扩展、Meta、Humanoid、FirstPerson、
   BlendShapeMaster、SecondaryAnimation 和 MaterialProperties。
5. 复制现有视觉快照 GLB 作为外观精确参考。
6. 将 Unity 运行时目录压成 zip，不改动生产 Prefab。

### 4. 验证与边界

- VRM 回读：`VRM_EXT=True`、Humanoid 骨 `51`、Skin `2`。
- Blender 导入 `.vrm`：`BindJoints=116`、`Lengyue_HairRig_v002=247`、
  Mesh 数 `53`。
- Zip 内确认包含 `Lengyue_GameCharacter.prefab`、
  `LengyueWardrobeController.cs`、`MateSoulCharacterToon.shader`、
  `PinkPajamas.asset`。
- VRM 不承载 Unity 脚本、Animator Controller、URP Toon Shader 参数、
  运行时换装目录、袖子布料、裙摆守卫或头发物理。游戏内仍必须使用
  `Lengyue_GameCharacter.prefab`。
- 默认 VRM 是日常装；睡衣只在 Unity 运行资产包中作为 `pink-pajamas`
  换装定义存在。

## Phase Dialogue Focus Cancellation 技术交接：对话与应用暂停解耦（2026-10-10）

### 1. 生命周期所有权

`ConversationOrchestrator` 自己拥有单轮请求的 `CancellationTokenSource`。合法取消入口：

1. 用户显式点击“取消”。
2. 小游戏需要独占 Dialogue 控制通道。
3. 单轮请求超过 30 秒。
4. `GameSession.Dispose()` 终止整个 Session。

`GameSession.SetPaused(true)` 不再取消对话。PICO 系统键盘、系统面板和焦点切换都会
短暂触发 `OnApplicationPause` 或 `OnApplicationFocus(false)`；这些事件只应冻结
角色、物理、物品和动作，不代表用户放弃 AI 回复。

### 2. 请求流程

1. `GameHud.SendDialogue()` 在会话非 `Thinking` 时提交一次请求。
2. `ConversationOrchestrator.SendAsync()` 创建 30 秒超时令牌并进入 `Thinking`。
3. `HttpLLMProvider` 通过网关发送请求。
4. 应用短暂暂停时，Unity 主循环可停止，但网络请求令牌保持有效。
5. 应用恢复后继续处理完成结果，写入上下文并进入 `Replied`。
6. `Thinking` 期间再次发送只返回“冷月正在回复，请稍候”，不会替换或取消首个请求。

### 3. 避坑

- 不要把 `OnApplicationFocus(false)` 等同于用户取消。软键盘本身就可能导致失焦。
- 不要在新的发送请求前无条件取消旧请求；UI 应禁用发送，编排器还要保留单飞校验。
- 判断 API 故障时必须关联客户端状态和网关 request ID。网关成功但历史消息始终为
  0，通常说明客户端在提交回复前取消或丢弃了结果。
- `UnityWebRequest` 的网络完成与 Unity 主循环消费结果是两个阶段；短暂暂停可以延迟
  消费，但不应主动中断请求。

### 4. 验证不变量

- 暂停前会话为 `Thinking`。
- 暂停期间请求 Task 未完成且 Provider token 未取消。
- 恢复并返回合法响应后，会话进入 `Replied`。
- 显式取消测试仍必须进入 `Cancelled`，重叠发送测试必须保留首个请求。

## Phase Lengyue Quality Baseline 技术交接（2026-10-10）

- 分析根目录：`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v001/`，工具环境为该目录 `.venv`。
- `scripts/freeze_baseline.py` 一次性生成只读快照，已有 `baseline/manifest.json` 时拒绝覆盖；还生成独立的 `unity_review/` 工程。原生产 Prefab、Mesh、材质、脚本均未修改。
- `scripts/analyze_vrm.py` 支持 accessor 字节步长、归一化整数、Sparse accessor 与列主序矩阵。蒙皮坐标必须为 `sum(weight * jointWorld * inverseBind * POSITION)`，不能再乘 Mesh 节点世界变换。Morph 位移使用同一个线性蒙皮矩阵。
- 规范坐标原点为 Head 世界坐标；Y 向上，Z 由 Head 至虹膜质心的水平向量确定，X 与其正交。记录完整变换矩阵。脸表面 AABB 宽度用于第一轮无量纲分析，不能冒充经标注的颅宽/脸高。
- 同位置 accessor 被多个子网格共享时，顶点总量仅计一次；部位几何统计仅使用该 primitive 索引实际引用的顶点。
- 32 层截面、72 方位径向包络来自三角形与平面/射线相交；四视图轮廓来自三维三角形投影并集，再取 256 等弧长点，不使用截图估计几何。
- 连通组件发束候选、面表面无符号距离、离散曲率均有方法及限制字段。碰撞穿插数、艺术发束分类、平滑主曲率方向和重建拟合在未验证时不填“通过”。
- 当前项目平台仍是 Unity 2022.3.38f1 / URP 14.0.11 / PICO Unity SDK；本次任务不升级 Unity、不引入 Kotlin/Spatial SDK、不移植鬼瓦轮原始资产。

## Phase Lengyue Quality Review 技术交接（2026-10-10）

- 工作根目录：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v001/`。
- `scripts/LengyueBaselineCapture.cs`在隔离的`unity_review/`中创建固定URP场景，读取源Prefab/材质/网格/层级，采集15张图并导出头发、脸、眼睛的BakeMesh数据。行为停用、动作分别恢复原pose后采样；不是实时物理验证。
- BakeMesh坐标必须沿用既有`LengyueAppearanceReview`代理路径：只应用位置/旋转，不再乘源Transform缩放。重复乘0.01会令身体和四肢在截图中消失。该问题已在评审工具中修正，未修改生产资源。
- `scripts/audit_unity.py`使用Unity实际表面做UV、拓扑和导出差异复核。生产头发与VRM外包络近似，但2048点/方向到三角面距离平均约0.359/0.403mm、最大7.664/9.012mm，后续严格身份目标以生产表面为准。
- `scripts/verify_measurements.py`在临时副本中重跑两模型分析，比较22份输出SHA-256；不会覆盖冻结参数。首次13项数值验证存于`measurement_verification.json`；后续额外核对已有报告的哈希。
- `scripts/build_review_boards.py`从实测数据和Unity基线生成PNG与`review/lengyue_target_spec.json`。约束带是二维位移示意，不等价于IoU，也不表示重建完成。前后正交外轮廓可能一致，必须补分区遮挡指标。
- 参考头发白模由`render_reference_hair.py`直接临时导入，替换为中性材质后渲染，不导出参考Mesh/UV/纹理资产。它不是精确MToon表现。
- 生产Shader已有头发三段明暗、UV0高光Mask与Fabric Detail；`hairUV`被传递但片元未使用；没有`_ShadeMap`。新功能应保留旧属性并默认关闭，不能重复建设已有能力。
- 生产源Animator没有Avatar。BodyOnly Transform曲线路径、现有Rest Pose和挂点是实际兼容边界；不要因VRM有Humanoid映射而擅自换Animator工作方式。
- 后续批准前不要进入阶段4。批准后先补语义分束、UV退化用途、安全距离/相交与真实游戏运行基线，再建独立冷月代理；所有未做指标保持null/未知。
- 冻结目录为只读；`delivery_manifest.json`只覆盖核心交付，不纳入Unity缓存、Python环境或启动日志。结束时`production_integrity.json`记录171个生产文件逐项核对结果。

## Phase Lengyue Quality Implementation 启动约定（2026-10-10）

- 最新用户授权覆盖所有后续步骤，前述“批准前不要进入阶段4”门禁已获批准；无需逐阶段再询问。
- 实施分支/工作树：`feature/lengyue-quality-upgrade`，`/Users/bytedance/MateSoul/.artifacts/lengyue-quality-worktree`。产物与进度在`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v002/`。
- 复用`v001/.venv`和`v001/unity_review`作为隔离执行工具环境；不得改写`v001`已封存的71份核心交付和manifest。
- 头发生成可参考项目自身`HairLab/v002/scripts/build_detailed_hair.py`的37条参数曲线与`rig_hair.py`的247骨弧长绑定。后续必须从冷月数据构造独立拓扑，不能复制鬼瓦轮资产。
- 下一执行步骤：Unity精确绑定/表面快照、Blender源结构检查、语义与碰撞补齐，然后实施并迭代头发及其他品质阶段。不要把本启动记录当作任务完成。

## Phase Lengyue Quality Hair 技术交接（2026-10-10）

- 实施真源与继续执行清单：`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v002/IMPLEMENTATION.md`。本阶段不是全部任务结束。
- `LengyueExactSnapshot.cs`从冻结Prefab导出所有Mesh、四权重、完整bindpose/骨路径、两套UV、Normal/Tangent及每个Morph帧；JSON坐标保留Mesh authoring空间，另存BakeMesh世界表面。
- 头发authoring为Z向上、-Y朝脸；DCC到Unity另有X镜像，必须配对反转三角绕序。最终候选骨路径/绑定矩阵沿用生产接口，权重由冷月自身37条弧长曲线重新计算。
- `build_hair.py`生成封闭Sweep、独立40格UV图集、截面/语义元数据。侧壁拆法线，主体用72%宏观场+28%局部法线，头发Tangent采用沿Sweep的显式正交帧，不依赖UV1或切线空间Normal Map。
- 不能逐三角只靠法线猜绕序。应对位置焊接拓扑执行一致朝向求解，再只把翻转映射回保留UV/法线分裂的原索引。环形帽壳的theta必须取模2π，旧曲线的非周期sweep项会让复制缝口不闭合。
- `build_clearance_field.py`在冷月脸表面采样206×145径向包络。耳部使用宽高度邻域保护粗帽壳；额头使用局部包络，避免帽壳盖过外层刘海。闭合帽壳不代表没有耳部穿插，必须双向检查双方三角边。
- `verify_hair.py`测量实际三角投影/截面/径向包络/语义中心线/UV和权重，并做脸、眼、眉双向有限线段-三角形相交。当前38壳体闭合且零静态交叉；最终`passed=false`保留至运行时和视觉验收结束。
- 旧刘海清除额头穿插后，tip最多外移11.4316mm，严格原始地标/Fréchet阈值未全过；报告分开记录`envelope_fit_passed`、`strict_original_geometry_thresholds_passed`、`static_surface_safety_passed`，禁止将包络通过冒充全项通过。
- `LengyueQualityBuilder`在隔离Unity工程生成源Prefab的Variant，旧Prefab不变。当前只接入头发候选和临时材质；正式新Shader、脸眼/服装、游戏包装和VRM仍需继续完成。
- 服装减面必须保留Unity `uv2.x`中的part编号。`LengyueSleeveCloth`按part 3/5和X范围选择驱动顶点，不能跨part插值后四舍五入“碰运气”；原骨路径和整套换装所有权保持不变。

## Phase Lengyue Quality Surfaces 技术交接（2026-10-10）

- 活跃目录仍为`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v002/`。`IMPLEMENTATION.md`记录后续必做项；用户授权要求继续完成，不能在材质阶段结束。
- `build_face_eyes.py`使用共享边中点和1/2/3边分裂保证局部细分无T接缝。原顶点位置不动；任一旧Morph影响的边禁用PN曲率位移，避免把中性形状偏移固化到嘴/眼变形。`quality_parent_triangles`记录每个新三角的原父三角。
- 表情验证比较**同一Morph、同一权重**下新三角与原父三角的方向。只比较中性法线会把正常闭眼翻转误报成破面。当前45项原形变比较均为0个新翻转；新增组合Morph还需视觉/嘴型覆盖回归。
- 面部80%椭球宏观法线+20%原法线，控制范围在正脸，形变法线按相同混合处理。頭发侧壁法线改为92%宏观+8%局部，几何与拟合数值不变。
- `simplify_garment.py`用Blender分part做0.48比例减面，插值UV/权重但重新附加精确part ID；`verify_surfaces.py`按每个part测量双向全部顶点和三角中心到三角面的距离，同时生成完整Tangent。
- `build_textures.py`从冷月自身Face/Cloth贴图分离低频信息，头发和虹膜完全解析生成。`audit_material_regions.py`只在参考UV区域且Base alpha≥0.5的1024 BOX采样上统计RGB/Lab/ΔE/高光，不保存参考像素。
- `complete_hair_signature.py`输出头部规范坐标描述及4视图256²重心深度投影遮挡图，cap记为id37；513条是视图内发束对的投影重叠，不是三维穿插次数。
- `LengyueQualityBuilder.Work`已支持`-qualitySource <v002绝对路径>`，默认指向项目ArtSource；隔离Unity执行必须传此参数。`BuildMaterial`更新新Shader/11张纹理/4张材质/4个网格/Variant并采集。`LengyueExactSnapshot.Output`仍是实验路径，正式工具交付前要修正。
- Android纹理导入采用ASTC6x6、MipMap、Trilinear；Mask/Detail为线性，Base/Shade/Emission按sRGB。仅新材质使用独立`MateSoul/Character Quality`；原Shader及旧材质保持。
- 最新Unity材质图在`review/material/`；数值报告在`reports/`。DCC候选源目前仍是上一阶段头发+旧脸眼/服装，下一阶段必须用本轮生成数据更新，不要将其当作完整新源导出。

## Phase Lengyue Quality Runtime 技术交接（2026-10-10）

- 当前完整实施记录位于`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v002/IMPLEMENTATION.md`最新章。DCC、表情接口、实际游戏场景及VRM已完成一轮闭环，任务继续做压力覆盖和最终交付。
- 已有Unity Mesh必须直接`Clear(false)`/`ClearBlendShapes()`后重新赋顶点、法线、切线、UV、三角、bindpose、四权重和Morph，并`SetDirty`/`SaveAssets`。本项目实测`CopySerialized(newMesh, existingMesh)`即使额外SetDirty仍未保存最新几何；必须使用独立Editor进程导出再次核验，不能只检查内存对象。
- `LengyueFaceControl`控制四个Animator层的参数/权重；原状态机/参数保留。`QualityExpressionRegistration`注册`expression`能力，通过`FacialPoseRequest`提交至既有协调器并持有Face控制通道。当前网络边界不识别该新Payload，因此`exposeToAI=false`，不可把可调用接口描述为已自动驱动AI情绪或实时嘴型。
- 最新角膜峰值为0.20mm且仅位于UV归一半径0.65内；原虹膜边缘和整个眼眶PN位置保持旧几何，避免浅角接缝放大微小位移。`audit_eye_seams.py`比较双向三角交线段（端点+中点），18个实际帧按0.5mm容差通过；密度增加后的相交边计数不能与旧低面数直接比较。严格零交叉仍未满足，属继承的遮挡建模边界，报告应保留。
- `LengyueQualityBuilder`仅在新Variant将三球半径取正、刘海深度/横向角限幅设为1.5°/5°；旧Prefab和Solver代码不改。最新实际运行数据是`playmode_06.log`、`runtime_samples/`与`runtime_verify_04.log`。
- `complete_dcc_candidate.py`必须从原DCC先运行头发生成再完整同步；Unity到Blender世界轴为(-X,-Z,Y)，通过旧对象world逆矩阵回局部，法线用逆转置，`Head 1`映射`Head`。新衣服复用`BindJoints`，旧集合隐藏；Blender5.2渲染枚举使用`BLENDER_EEVEE`。
- VRM直接由`production_snapshot`导出，Unity到glTF反射Z并翻绕序/UV0 V；Tangent XYZ随Z反射，w因坐标与V两次反射抵消而保持；UV1数据通道原值保留。`verify_quality_vrm.py`不复用写入逻辑，重新解析GLB并重算蒙皮，对比Unity BakeMesh，误差≤1.69µm。Spring按HairRoot下一层37独立链分组，正半径检查在导出边界执行。
- 正式工作树`/Users/bytedance/MateSoul/.artifacts/lengyue-quality-worktree`的SDK/角色Scripts被原根忽略规则漏收；已补本机缺失且ignored依赖（报告清单），勿在本任务提交。新QualityUpgrade/Scripts务必强制加入Git。lab工具为支持PlayMode MonoBehaviour已移到`Assets/Review/Tooling`，不要直接用lab构建Android Player。
- 实际Game验证应在启动Unity**之前**将工作树产品名临时设为`MateSoulQualityValidation`，原存档路径可能被Unity缓存，不能在EnterPlaymode前临时改名来假定隔离。GameProbe恢复批处理焦点并重试已有RestoreItemsAsync，验证后工作树已恢复MateSoul。`actual_game_playmode.json`记录14动作/450帧/0错误。
- 剩余执行顺序：确认最新动态数值→补转头/下蹲与表情0/50/100视觉→工作树Android新场景构建→统一交付/清临时产物→更新报告和提交。ADB当前无设备，设备帧率/温度/内存未验证。

## Phase Lengyue Quality Delivery 技术交接（2026-10-11）

- 完整交付入口：`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v002/README.md`。新增Unity真源位于`/Users/bytedance/MateSoul/.artifacts/lengyue-quality-worktree/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/QualityUpgrade/v002/`。主项目旧资源不覆盖。
- **更正前文的眼部IK描述：** 生产Animator无Humanoid Avatar。最终`LengyueFaceControl`只驱动5层Animator（Expression/Mouth/Blink L/Blink R/Gaze）。`LengyueQualityExpressions.AddGaze`生成3×3方向的眼骨Quaternion曲线，以FreeformCartesian2D混合；没有额外LateUpdate写骨骼，没有改成Humanoid。
- `GazeYawLimit=8`、`GazePitchLimit=6`；`SetGaze`接受世界点并将方向转为角色坐标角度。此入口处理一次目标采样，持续跟踪应由Face通道持有者更新目标。`FacialPoseRequest`新增可选Gaze和Blink，原参数调用兼容；动作Dispose清理全部通道输入。
- `LengyueQualitySupplementProbe`只用于隔离工程，按真实帧驱动18类输入和压力姿态。基础Idle固定到同一时间，仅用于排除自动眨眼对强度比较的干扰；上层动画与物理继续实际执行。最大中点线性误差0.0003221，所有强度有效。压力fixture使用真实骨路径，暂时停用自己的HeadPoseGuard，不把测试姿态当作游戏动作能力。
- 最终真实游戏报告为660帧/21动作/0错误；基础矩阵仍为350格、1080帧、16次换装。新增`finalize_delivery.py`统一交付验收与分块SHA-256，原始严格失败和未测设备字段不会被覆盖为通过。
- `LengyueQualityAndroidBuild.Run`只构建新质量场景，临时设置独立包名、ARM64/IL2CPP，finally恢复PlayerSettings；随后仅导出新增目录的Unity增量包。BuildReport.totalSize约899MB是构建统计，实际压缩APK约113.9MB，应使用FileInfo或manifest的大小。
- 最新VRM增加8°/6°look-at限制，独立蒙皮回读误差仍1.69µm；导出不包含Unity Animator/换装/布料solver，也不能无损复现定制Shader。
- 执行监控：`scripts/watch_command.py`每5秒写状态，日志增量读取不跳过未读区；Unity退出可能一次写出超过64KB内存日志，不能只搜最后64KB来判定成功。`--via-open`按唯一日志路径识别Unity PID，只终止本任务PID；TERM最多5秒，再KILL并确认退出。直接二进制启动的bee退出码134改用LaunchServices解决。终端工具偶发返回60002时先检查JSON/完成标记，不能盲目重跑。
- 临时raw截图/BakeMesh/log/Blender备份已清理，保留源资产、交换/构建产物、合并审核板和机器报告。后续复跑会重新生成raw；不要把清理后的raw不存在当作此前未执行。
- 尚未满足的严格标准与设备边界统一见`README.md`及`reports/final_acceptance.json`；这是完成候选实现与已可用环境验证，不代表PICO真机表现已认证。
- 对应提交`81901350c1d647110a1522d107ab174104dd4d6b`（`feature/lengyue-quality-upgrade`），只含278个本任务新增文件；不包括本机补齐的949个原忽略依赖。大型DCC、VRM、APK和Unity增量包在本地ArtSource交付目录，由manifest核对；不要假设它们随Git提交分发。

## Phase Lengyue Visual v003 技术交接（2026-10-11，进行中）

- 最新需求明确改为参考发型的参数级复刻，旧“必须保持原冷月发型轮廓”约束作废。详细实现/剩余任务见`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v003/IMPLEMENTATION.md`。
- `reference_hair.py`按1µm位置焊接识别57独立壳+后发底层；仅用原UV纵坐标识别环，不保存原UV。PCA获得截面真实平面，64点中心线与33组宽深控制驱动独立35×8（外片）/24×3（内片）Sweep。旧径向切面法会扭曲细发尖和发顶宽度。
- `build_hair.py`明确记录全局头型适配（X/Y脸宽比例，Z额外×1.20）与局部皮肤间距投影，不能将适配后归一化误差冒充参考米制外形完全相等。UV2保存strand ID/t，UV0新8×8图集；57×5链及绑定矩阵独立生成。
- Vertex安全不等于Triangle安全。使用局部皮肤径向场检测三角中心/边中点，仅对不足区传播边细分后投影，当前23895面双向face/eyes/brow交叉0。open cap的trimesh法线可能整体朝内，须额外径向校正；否则Unity背面剔除会产生错误露肤条。
- 新`LengyueVisualBuilder`自带Mesh JSON结构，直接更新已有Mesh确保持久化；`ConfigureHair`保持原Renderer路径，停用旧Solver并创建Head/VisualHairRoot下新骨架和单个Solver。当前仅HairPreview，其他身体/衣服/面部导入与注册尚待扩展。
- 验证`verify_hair.py`实做64×144径向、512点轮廓、截面、中心线Chamfer/Fréchet、128²逐束投影深度遮挡；`hair_safety.py`独立三角安全。最新lab Unity渲染已成功，正式新脸/服装制作后必须重跑静态/动态安全，不能复用旧脸检测当最终。
- v003工具在lab `Assets/Review/Tooling`，生产目标新目录`QualityUpgrade/v003`，最终要接入当前主项目可访问场景/A/B，不只留在隐藏工作树。原脸眼局部坐标就是世界米；SkinCoverage局部厘米×.01，旧UV0全部单点；衣服/头发局部Z-up，转换以snapshot localToWorld为准。

## Phase Lengyue Visual v003 完整静态表面交接（2026-10-11）

- 新脚本与真源：`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v003/scripts/`；最新必做事项在同目录上层`IMPLEMENTATION.md`。已完成完整静态候选，尚无v003动态、DCC/VRM和游戏交付验证。
- `build_anatomy.py`在原骨/UV/Morph上进行真实小幅重塑和共享边细分；`source_triangles`逐层映射回原三角，方便同表情同权重朝向验证。眼眶原位保留，2048面虹膜核心0.25mm角膜仅在UV半径.62内变化，避免浅角边界放大位移。
- 原Body厘米坐标，按位置+权重焊接后边界固定柔化，细分并生成5个圆柱图表，拆UV接缝仍保留同位置同权重。衣服`build_garments.py`仅重映射UV0，UV2.x原part0..5不改，袖part3/5选择仍有效；新领片/胸针/腰链nearest权重绑定到原衣服骨，全部归part0。
- `paint_surfaces.py`通过原三角UV重心插值获取世界位置后绘制解剖妆容。不要再按肤色色差阈值做Face SDF Mask，原烘焙亮点会成为白纹；主皮肤UV岛连续遮罩、独立重绘肤色和原少量高频细节更稳定。嘴部中心取原Morph实际Y=1.4879，不是1.47。
- Shader`MateSoul/Character Visual v003`新增Face SDF/Control、左右head世界轴与唇湿润；Body用SkinMode柔和连续漫反射；Base/Shade/Emission为sRGB，Normal/Mask/SDF/Control/Detail为Linear，Cloth_Detail使用Repeat。下一阶段必须在运行时按head骨更新`_FaceRight/_FaceForward`，静态默认轴不能直接当游戏完成。
- `LengyueVisualBuilder.Build()`现在完整导入六Mesh及五材质，`HairPreview()`仅为内部步骤。生产Prefab仍位于隔离lab的新`QualityUpgrade/v003`目录；新的Animator和动作协调器注册尚未配置。不要只调用HairPreview覆盖掉已完成脸/衣服。
- `verify_anatomy.py`六Mesh核心143943面：finite/权重/骨绑定/切线/0退化契约通过；15Morph×3强度新增翻面0；新Hair与新Face/Eyes双向三角交叉0。眼白/虹膜仍保留原薄片相交，交线几何偏差≈1.86e-16m。附属旧牙/眉等未计核心总数，最终报告需Unity生产快照求真实总面数。
- Hair发现116个退化端帽面，已在独立Sweep结束后过滤，再计算法线；最终23779面，重跑发型拟合通过最新计划目标，旧7%径向P95超限仍须披露。不要删除cap径向法线修正或局部不足三角细分。
- 最终本阶段实测`unity_full_final`（16.4秒，确认退出）与四张合并审核板；新`make_review_boards.py`生成原版/v002/v003同灯光同相机图。当前没有v003运行时数据，必须继续全任务，不能拿v002既有1080/2100帧报告冒充本次结果。

## Phase Lengyue Visual v003 运行时与交换源交接（2026-10-11）

- v003命名空间为`MateSoul.CharacterVisual`，避免依赖主工程未安装的v002类。`LengyueVisualExpressions`生成Expression/Mouth/Blink L/Blink R/Gaze五个Generic Animator层；`VisualFaceControl.FaceBasis`由当前head.rotation乘初始化记录的head到角色轴变换计算，注视与面部SDF统一使用该坐标系。五层面部曲线仍只由Animator写入。
- `VisualExpressionRegistration`使用既有ActionCatalog和`FacialPoseRequest`，能力占用`Face | Gaze`，结束时清理输入；`exposeToAI=false`。当前无网络Payload解析或语音音素源，不能写成已实现自动AI表情/实时口型。
- `LengyueVisualGameBuilder`保留原游戏包装的所有业务组件，按Renderer路径替换新网格/材质；复制新的VisualHairRoot并重新绑定286骨，停用旧solver。场景来源始终是当前工程`Assets/MateSoul/Scenes/MateSoul.unity`，输出到新v003目录。菜单`MateSoul/冷月外观 v003`提供游戏/A-B入口。
- **动态发根约束：** 每个五关节链的0/1/2关节固定，3/4关节模拟；`maxBendDegrees=maxChainBendDegrees=1.2`，DirectionalOffset两个分量设0以使用整体圆锥限幅。原方向分量重建在部分近水平发段可超过圆锥角，第一轮曾出现12.6°及大量穿插；只修改新Prefab配置，不改全项目solver。当前15组压力/动作/注视实际BakeMesh双向交叉0。
- `LengyueVisualPlayProbe`采集2850帧及63组表情强度；`verify_playmode.py`验证实际新网格、唯一solver和强度有效性。组合表情采用层权重叠加，中点不是单一线性Morph，不把组合非线性误差当作单参数线性失败。袖子代理最小间距记录-1.161mm、最大位移54.79mm，Editor CPU时延不是设备性能。
- `build_visual_dcc.py`从完整Unity生产快照重新构建8个当前可见Mesh、403骨、57可编辑发束曲线与18贴图，保留UV2、原/新增Morph和权重。`verify_visual_dcc.py`独立重开并求值。Blender5.2的`shape_key_add`默认weight=1，必须显式设0，且`from_mix=False`防止混入其它形变。
- `LengyueVisualExport`输出真实active spring标记；VRM只从每链第一动态关节开始建立Spring，保留固定上段。独立readback误差1.613µm；MToon不支持自定义SDF/唇湿润/布料细节求解/1.2°限幅，Unity仍是最终视觉与运行真源。
- 主工程副本`.artifacts/lengyue-visual-validation`使用独立产品名`MateSoulVisualValidation`隔离存档。复制Assets/Packages/ProjectSettings已完成；巨大Library/PackageCache复制分别120秒限时退出并确认。保留完成的不可变缓存，按相同manifest/lock共享闭合工作树PackageCache，只补DB/ScriptAssemblies，缺失Artifact由Unity重建。隔离副本中PICO依赖须改为`file:/Users/bytedance/MateSoul/XR`，原`file:../../XR`在新深度会指向不存在的`.artifacts/XR`。不要改主项目manifest。
- 本节后仍需当前主版本实际动作回归、Android/Unitypackage、最终合并板/验收/清理/提交。主分支有大量并行改动；只同步新增v003目录，最终任务分支不得把这些无关改动一起提交。

## Phase Lengyue Visual v003 最终构建与复现交接（2026-10-11）

- 真源目录：`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v003`。README包含当前主工程场景/Prefab/四类交付包链接、回退表、原始阈值例外；`final_acceptance.json`区分完成本地实现、严格旧阈值和设备未测，不能将三者混为一个passed。
- `LengyueVisualAndroidBuild.Run`只打包新游戏场景，设置ARM64/IL2CPP与独立包名，finally恢复PlayerSettings；ExportPackage仅导出v003。该包依赖已有MateSoul宿主，不能导入空白工程冒充独立Demo。
- `verify_packages.py`通过aapt、apksigner、ZIP CRC、ARM64 native库与libil2cpp检查实际APK，并确认global-metadata无临时7785探针端点；解开Unitypackage的pathname/asset/asset.meta，逐一比对构建副本SHA。使用压缩文件实际大小，不用BuildReport.totalSize当APK大小。
- 当前宿主`AlwaysOnTopUi`、`SystemNotificationPresenter`、`GameHud`、`HttpLLMProvider`带未受开发开关控制的临时HTTP调试片段，HTTP禁用时会抛异常。`scripts/build_clone_ui.patch`仅在隔离副本移除4个探针，保留真正字体校验、业务与网关准入；`reports/build_source_adjustments.json`记录源/构建SHA。主工程文件未套用该补丁。最终副本HTTP=0下21动作0错误，旧HTTP=2夹具报告另存`host_http_fixture_game_playmode.json`。
- `LengyueVisualGameProbe`将第一个Editor-ready回调单独截图，30个实时帧后记录正式ready再开始21动作；新增`readyWarmupFrames`写入报告。曾观察首回调材质瞬态异常，30帧后恢复，不能将它解释成已完成真机启动验证。
- `sync_delivery.py`只写新v003目录和meta。主工程完整同步最终构建副本；feature同步新Scripts/Integration/Editor及独立A/B场景，保留基点生成的Game.controller、Runtime Prefab与游戏场景。`unity_integration.json`列出这三类派生文件及其配套meta的允许差异，其他核心资源逐文件应相同。
- `cleanup_delivery.py`在全部监督进程结束后保存精简执行历史，删除v003 raw帧、运行时采样、启动日志和blend1；`finalize_delivery.py`重新核对603项保护输入并为art/current/branch三组交付生成SHA。保留production_snapshot中性快照供DCC/VRM复现，不把它当可删除运行时压力采样。
- 复跑顺序：参数/几何/贴图生成 → `LengyueVisualBuilder.Build` → 表情与游戏/A-B入口 → PlayMode及实际BakeMesh检查 → Unity最终中性导出 → Blender重建/回读与VRM导出/回读 → 在当前宿主隔离副本应用必要的临时探针清理 → 游戏回归/Android打包/包体核验 → 同步/清理/manifest。所有长任务仍使用watch_command的5秒心跳及截止时间。
- 尚未证明：PICO设备性能、严格眼沿/布料零交叉、参考原米制发型毫无适配差异、旧径向P95≤7%、VRM Shader/物理无损、AI网络情绪与实时音素。准确数值与可用范围见README与各独立报告。
- 对应提交`141b65a3fabd46c230b47d01455882a7d9427b4c`（294个新增文件）。Unity自动序列化的空字段及unified patch空上下文带尾空格，保持原始格式与包体SHA；人工源文件/文档严格格式检查通过。提交钩子硬编码扫描2秒超时，仅作为未完成的提示性扫描记录。

## Phase 通用物品交互开发准备：架构评审（2026-10-15）

- 正式评审稿：
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_通用物品交互开发准备方案.md`。
- 当前 `AvatarActionCoordinator`、`PhysicsBody`、`ItemDefinition` 和 `ItemRegistry`
  可作为增量建设基础；不另建平行动作协调器或物理写入路径。
- 现有 `ItemReservation` 只有 `object Occupant`，不足以处理玩家/NPC 争抢、旧回调、
  超时、持有转移和异常恢复。规划将 `ItemRegistry` 升级为带 generation、
  InteractionID 和显式 `ItemControlState` 的唯一物品控制事实源。
- 静态交互数据采用 `ItemInteractionProfile` ScriptableObject 与 Prefab
  `ItemInteractionAuthoring` 组件组合；类别规则可复用，抓取锚点和站位槽保留模型局部
  Pose。中央系统不得增加物品名称分支。
- P0 仅承诺苹果、杯/碗、遥控器三类代表物，使用 GroundBendPickup 和 MidReach
  两类 in-place 动作完成预约、接近、IK、安全吸附、持有、放回和统一回滚闭环。
- 临时预约不进入存档。保存、暂停、场景切换和 Owner 丢失均须先走有限回滚；
  最终策略允许物品在最近安全 Pose 恢复动态状态，但不允许永久保持玩家不可操作。
- 当前文件是开发准备评审稿，未替代 `MateSoul_Demo开发方案.md`，未修改运行时代码、
  Animator 或物品资产。

### 多物品 Action 补充

- 产品层 `Action` 定义为完整生活事件；`ItemInteraction` 是单物品执行步骤，`Motion`
  是动画/IK 表现。复杂 Action 仍作为一个父 `IAvatarAction` 进入现有
  `AvatarActionCoordinator`，内部 Step 不嵌套提交角色 Action。
- 参考 MateFairy 的 intent 注册、持续实例、生命周期通知和实体 ID 思想；不移植双注册表、
  全局 Action 锁、`Map<String, Any>`、无语义 `objectIds`、Controller 手写物理恢复和
  `RUNNING/COMPLETED/FAILED` 三态模型。
- 新规划结构包括 `CompositeActionDescriptor`、`ActionRoleSpec`、
  `ICompositeActionPlanBuilder`、`ActionRunContext`、公共 ActionStep 和逆序补偿栈。
- 多物品采用 RoleBinding、按需 HardReservation、`StagedForAction` 三层所有权；
  同阶段的多个资源通过 `TryReserveBundle` 全有或全无提交。
- “切水果”需要 `fruit/cutter/plate/workspace` 四个角色。冷月只有两只手，实际计划必须
  先把盘子和苹果分次放入餐桌工作槽，再取刀切割，不能让三个物品同时附着角色。
- 缺餐桌时返回 `BlockedMissingRole`，不启动 Action、不加锁；只通过现有系统消息通道
  提示“需要先放置餐桌”，不自动打开物品目录，也不保留 pending Action。
- 苹果到苹果块使用结果事务：`CutCommitPoint` 前完整回滚，提交后只做 forward recovery，
  避免整苹果和苹果块重复、半提交存档或永久预约。
- P0 先用内部 `stage-fruit` 验证苹果、盘子、餐桌三物品编排；P1 实现带刀具、切割 Motion
  和产物事务的正式 `cut-fruit`。

## Phase Lavender Chef Knife 技术交接：闭合金属刀身（2026-10-15）

### 1. 资产结构

资产根目录：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Knife/`

参数化构建入口：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Knife/scripts/build_knife_asset.py`

主根节点为 `LavenderChefKnife_330mm_Root`。子节点：

- `Knife_MetalBlade`：刀身与刃口材质区域。
- `Knife_HiddenTang`：伸入握柄的金属刀茎。
- `Knife_DarkBrownHandle`：深棕色握柄。

参考图没有尺寸，当前采用 330 mm 总长、200 mm 刀刃、50 mm 最大宽度、24 mm 最大
厚度的工程默认值。替换尺寸时必须整体重建，不应只缩放刀身而保留旧握柄。

### 2. 刀身几何

刀身沿长度采样 72 段、横向采样 20 段。每个横向截面从 0.22 mm 刃口连续过渡到
3 mm 刀脊，正反两面、刃口侧壁、刀脊和刀根均闭合。

刃口宽度区域约占刀面 20%，与主体共用一个 Mesh，但使用独立抛光材质。该结构保证：

1. 侧视图可见真实刀脊厚度。
2. 刀刃不是悬浮贴片。
3. 刃口材质可以独立调节粗糙度。
4. 后续生成简化碰撞体时仍以整体刀身轮廓为准。

### 3. 金属材质契约

- `Knife_BrushedSteel`：Metallic 0.97，Roughness 0.22。
- `Knife_PolishedCuttingEdge`：Metallic 1.0，Roughness 0.09。
- `Knife_TangSteel`：Metallic 0.95，Roughness 0.27。
- `Knife_DarkBrown_Handle`：非金属，Roughness 0.30。

GLB 验证器直接解析 glTF JSON 的 `pbrMetallicRoughness`。按 glTF 规范，
`metallicFactor` 缺省值是 1，不是 0；验证代码必须使用该默认值，否则会把完全金属材质
误报为非金属。

### 4. 运行时边界

模型报告：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Knife/reports/knife_model_report.json`

当前全部尺寸、流形、楔形刀身、金属参数、刃口粗糙度和导出回读断言为 `true`。

Unity 接入时应创建独立抓取锚点和简化碰撞体。刀具属于通用物品交互规划中的 P1 高风险
物品，不能仅依靠 `XRGrabInteractable` 直接开放切割；切水果 Action 应由统一物品状态、
多角色预约和提交点事务共同控制。

## Phase Lavender Chef Knife V002 技术交接：高精几何与内嵌 PBR（2026-10-15）

### 1. 生效版本

V002 原位替换 V001 文件。有效入口仍为：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Knife/source/LavenderChefKnife_330mm.blend`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Knife/exports/LavenderChefKnife_330mm.glb`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Knife/exports/LavenderChefKnife_330mm.fbx`

报告 `revision` 为 `V002`。前文的 72×20 刀身采样、7,356 总三角面和无纹理材质仅描述
V001，不再是当前资产参数。

### 2. 高精几何

- 刀身：128 个纵向区间、36 个横向区间，正反面增加轻微凸磨厚度。
- 抛光刃带：刀面宽度的 16%，与主刀身保持同一闭合网格。
- 刀柄：64 边超椭圆截面；12 个控制轮廓通过每段 7 次 Catmull-Rom 插值形成约
  78 个纵向环。
- 最终：14,764 顶点、29,516 三角面，全部闭合流形。

### 3. 纹理与材质

纹理目录：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Knife/textures/`

包含：

- `knife_brushed_steel_basecolor.png`
- `knife_brushed_steel_roughness.png`
- `knife_handle_basecolor.png`

三张纹理均为 512×512，由构建脚本确定性生成并打包进 Blend；GLB 通过 BufferView
内嵌三张图片，不产生外部 URI。主刀身 Metallic 0.97，抛光刃口 Metallic 1.0。

钢材 Roughness 由纹理驱动，glTF 中材质 factor 为 1 并乘贴图，不应再用 V001 的固定
0.22 值解释最终像素粗糙度。

### 4. Blender 图像生成顺序

Blender 5.2 生成图像必须遵循：

1. 创建 Image。
2. 设置 `colorspace_settings.name`。
3. 写入 `image.pixels`。
4. 调用 `image.update()`。
5. 设置输出路径并保存。

在步骤 3 之后再切换颜色空间会把生成图像清零，表现为模型与 GLB 纹理全部发黑。

### 5. 当前验证

`knife_model_report.json` 的尺寸、流形、楔形刀身、金属参数、纹理内嵌、外部 URI 和
GLB/FBX 回读断言全部为 `true`。运行时仍必须使用简化碰撞体。

## Phase Lavender Gingham Dining Table 技术交接（2026-10-15）

### 1. 资产与构建入口

有效美术资产：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Table/source/LavenderGinghamDiningTable_1200x700x755mm.blend`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Table/exports/LavenderGinghamDiningTable_1200x700x755mm.glb`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Table/exports/LavenderGinghamDiningTable_1200x700x755mm.fbx`

参数化构建入口：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Table/scripts/build_dining_table_asset.py`

旧 `LavenderChibiTable_600x400x750mm` 文件仅保留历史追溯，不再由 Unity 构建器引用。

### 2. 桌布几何

木桌主体尺寸为 1200×700×750 mm。桌布原始平面在 X/Y 方向分别增加 165/235 mm
物理余量，再由参数化映射生成：

1. 桌沿 24 mm 圆弧转折。
2. 垂边纵向下坠。
3. 沿边双频褶皱与波浪下摆。
4. 双轴外扩区域的角部聚拢。
5. 顶面最大约 2.8 mm 的轻微松弛。
6. Solidify 生成 3 mm 厚度和闭合边缘。

桌布采样为 128×96，应用 Solidify 后为 25,026 顶点、50,048 三角面。该网格是静态
烘焙结果，不在 Unity 挂 Cloth 组件，不参与运行时布料求解。

### 3. 织物 PBR

纹理：

- `tablecloth_gingham_basecolor.png`：1024×1024，浅紫/粉紫格纹和边缘缝线。
- `tablecloth_weave_normal.png`：512×512，双向经纬编织法线。
- `tablecloth_weave_roughness.png`：512×512，织物粗糙度变化。

Blend 与 GLB 使用完整 BaseColor/Normal/Roughness，GLB 通过 BufferView 内嵌三张图片，
无外部 URI。Unity 的 `MAT_Tablecloth_LavenderGingham` 使用 URP Lit，保留 Base Map、
Normal Map 和常量 Smoothness；当前未把 glTF Roughness 另行转为 URP Mask Map。

### 4. 虚拟物品装配

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/GameSceneBuilder.cs` 新增
`BuildDiningTable(Material)`，替代通用 `BuildProp()` 的旧桌子调用。

该方法区分两个尺寸：

- 视觉包络：1.2811×0.7547×0.7712 m，只用于 FBX 导入校验和落地点归一化。
- 物理主体：1.2×0.75×0.7 m，用于占地、顶面/桌腿 Collider 和 NavMeshObstacle。

稳定约定：

- ID：`table`
- 显示名：`餐桌`
- 质量：18 kg
- 目录可见：是
- 表面放置：餐桌自身为否；其他小物品可通过顶面 Collider 放置到桌上

`Table.asset` 和 `Table.prefab` 原路径与 GUID 不变，因此场景引用和旧存档无需迁移。
柔软桌布不能扩大硬碰撞体，不能创建 MeshCollider，也不能单独持有 Rigidbody。

### 5. 验证

结构化报告：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Table/reports/dining_table_model_report.json`
- `/Users/bytedance/MateSoul/项目沉淀资料/validation/dining_table_virtual_item_20261015.json`

Unity 2022.3.38f1 场景重建成功。以下测试 3/3 通过：

- `CatalogPlacementGrabAndFocusUseRegisteredDefinitions`
- `SurfacePlacementAndReversedSupportOrderRestoreAsOneLayout`
- `RemovingSupportThenPausingRestoresTheRemainingItemUnderGravity`

受限 shell 会触发已知 Bee semaphore 134，必须通过系统 Terminal/LaunchServices 启动
Unity 才能获得有效编译与测试结论。

## Phase Art Dining Chair V002 技术交接：参数化木构与软包坐垫（2026-10-15）

资产根目录：
`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/DiningChairV002/`。

### 1. 生成管线

1. `scripts/prepare_dining_chair_textures.py` 使用固定随机种子生成浅暖橡木和浅紫灰
   织物各三张 1024² PBR 贴图，并记录参考图与贴图 SHA-256。
2. `scripts/build_dining_chair_asset.py` 从参数创建木构、坐垫和包边，渲染六个视图，
   导出 GLB/FBX，重新导入并验证尺寸、材质和流形拓扑。
3. `scripts/package_delivery.py` 校验导出哈希、报告、预览和源文件后生成交付 ZIP 与
   `SHA256SUMS.txt`。

模型以米存储，X/Y/Z 分别为宽/深/高，根原点在地面中心。由于原图无尺寸标注，设计
外廓固定为 450×520×850 mm；构建结束使用根节点非破坏缩放归一到该外廓，导出后
再以同一尺寸回读断言。

### 2. 木构几何

`create_swept_member()` 沿多段中心线扫掠圆角截面，用于前腿和后腿/靠背柱。截面指数
0.72，在圆形与圆角矩形之间，保留参考图的软木柱轮廓。后柱包含座面附近的前弯和
靠背段后倾，上端另有闭合半椭球木帽。

`create_curved_back_slat()` 对上下靠背板同时施加：

- X 方向横向包裹弧度；
- 中心轻微向下的垂向弧度；
- 圆角 Y/Z 截面；
- 沿宽度连续的木纹 UV。

四边围板与座板使用倒角闭合盒体。所有 16 个生产网格均为闭合流形，总计 15,504
三角面。

### 3. 软包结构

`create_cushion()` 使用封闭超椭球参数面构造 450×430×56 mm 坐垫：

- 水平指数 0.235，形成平整中心和圆润四角；
- 垂向指数 0.30，表现泡棉上下鼓包与边缘压缩；
- 经纬织纹通过 BaseColor/Normal/Roughness 贴图表现，不依赖 Blender 专有程序节点。

`create_piping()` 沿圆角矩形路径扫掠 1.1 mm 半径圆截面，形成独立 2.2 mm 包边绳；
底面另有独立深色防尘布。织物材质使用高粗糙度和 Sheen。GLB 已确认完整保留织物
BaseColor、Normal、Roughness 并内嵌图片。

### 4. 导出与接入边界

核心文件：

- `source/WarmOakDiningChair_450x520x850mm.blend`
- `exports/WarmOakDiningChair_450x520x850mm.glb`
- `exports/WarmOakDiningChair_450x520x850mm.fbx`
- `reports/dining_chair_model_report.json`

当前 V002 是独立美术资产，未切换 Unity 的旧椅子引用。后续接入时保留稳定目录 ID
和公共 `PhysicsBody` 所有权；建议使用 1 个座框 BoxCollider、4 个腿柱简化碰撞体和
2 个靠背 BoxCollider。不要为坐垫、包边或防尘布创建独立 Rigidbody，也不要使用动态
MeshCollider。质量建议 4.5–5.5 kg，重心位于座框中心略下方。

## Phase Art Dining Chair V003 技术交接：局部比例修正（2026-10-15）

V003 目录：
`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/DiningChairV003/`。

V003 不对 V002 做整体 Z 缩放。修正只发生在座面以上：

1. 后腿/靠背柱在 535 mm 以上重新布置中心线，顶部木帽中心提升至约 909 mm。
2. 下靠背板中心由 608 mm 提升至 665 mm。
3. 上靠背板中心由 754 mm 提升至 825 mm。
4. 构建结束仍统一归一到 450×520×920 mm；座面顶高实测约 475.7 mm，与 V002
   基本一致，因此坐姿、桌椅配高和物理碰撞方案无需改变。

坐垫 `lavender_fabric_basecolor.png` 提高了紫色饱和度；Normal、Roughness 和几何
包边参数不变。预览场景曝光调整为 −1 EV，避免高亮把浅紫色压成白色。该曝光只影响
交付渲染，不影响 GLB/FBX 材质数据。

核心文件已更新为：

- `source/WarmOakDiningChair_450x520x920mm.blend`
- `exports/WarmOakDiningChair_450x520x920mm.glb`
- `exports/WarmOakDiningChair_450x520x920mm.fbx`
- `reports/dining_chair_model_report.json`

V003 GLB/FBX 回读尺寸、闭合流形、6 张贴图内嵌和 PBR 材质通道均通过。Unity 当前
引用仍未切换；接入时应选 V003，不再使用 V002 的 850 mm 高版本。

## Phase Art Cooking Set V001 技术交接：锅具与同构牛排状态（2026-10-15）

资产目录：
`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/CookingSetV001/`。

### 1. 构建与纹理

`scripts/prepare_cooking_set_textures.py` 生成 12 张 PBR 贴图：

- 拉丝不锈钢 BaseColor/Normal/Roughness；
- 不粘层 BaseColor/Normal/Roughness；
- 生牛排图集 BaseColor/Normal/Roughness；
- 熟牛排图集 BaseColor/Normal/Roughness。

牛排图集的 V=0–0.225 为侧壁、V=0.245–1 为顶面。Pillow 图片行坐标和 Blender UV
的 V 方向相反，生成脚本在保存前对图集做垂直翻转，并同步翻转法线贴图绿色通道；
禁止删除该处理，否则顶面和侧壁纹理会互换。

### 2. 锅具几何

`create_pan_body()` 旋转封闭截面，显式创建不锈钢外壁、锅沿和不粘内表面。锅体不是
单层面，外径 280 mm、锅高 56 mm。金属连接件通过 XZ 路径扫掠圆角截面；握柄使用
闭合倒角盒体，挂孔通过构建期布尔应用后保存，不依赖运行时布尔。

锅铲铲头不能使用连续布尔切孔：首轮布尔版本留下 12 条开放边。当前
`create_blade_blank()` 将外轮廓和三个反向孔环交给 `tessellate_polygon()`，
分别生成上下三角面并显式连接外壁/孔壁，因此三个槽孔是真实贯通且网格闭合。

### 3. 生熟牛排同构契约

`create_steak_mesh()` 生成唯一参数化肋眼几何，生牛排直接使用该 Mesh，熟牛排通过
`Mesh.copy()` 复制并仅替换材质。验证要求：

- 尺寸均为 180×135×28 mm；
- 顶点数均为 578；
- 三角面均为 1,152；
- 顶点坐标与面索引 SHA-256 必须一致；
- 最大逐顶点位置差必须为 0 mm。

当前哈希：
`e6b4d38b10d51612ff0c0efbfe7bf670719522603a0dba395c8e1a5152d15156`。
熟牛排煎痕只存在于纹理中，不能通过额外几何、缩放或置换破坏同构契约。

### 4. 导出与 Unity 边界

核心交付：

- `source/StainlessPanSpatula_SteakPair_v001.blend`
- `exports/StainlessFryingPan_280mm.glb/.fbx`
- `exports/SlottedSpatula_370mm.glb/.fbx`
- `exports/RibeyeSteak_Raw_180x135x28mm.glb/.fbx`
- `exports/RibeyeSteak_Cooked_180x135x28mm.glb/.fbx`
- `reports/cooking_set_model_report.json`

当前未接入 Unity。接入时锅体/手柄、铲头/手柄分别使用简化组合碰撞体，槽孔无需参与
碰撞；牛排使用同一 Collider 和占地数据。烹饪状态应由单一业务所有者切换 Mesh/
Material，不得并行创建第二套 Transform 或 Rigidbody 状态机。

## Phase Natural Oak Dining Chair 技术交接：生产餐椅与 Unity 物理装配（2026-10-15）

### 1. 生效资产

本轮正视、侧视、背视参考图对应的生产入口为：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Chair/source/NaturalOakDiningChair_480x505x820mm.blend`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Chair/exports/NaturalOakDiningChair_480x505x820mm.glb`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Chair/exports/NaturalOakDiningChair_480x505x820mm.fbx`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Chair/scripts/build_dining_chair_asset.py`

模型名义尺寸为 480×505×820 mm，实测视觉包络为
481.9661×510.8422×819.9502 mm，座面顶高为 475 mm。X/Y/Z 分别表示宽、深、高，
Unity 导入后对应 X/Z/Y。

同目录中的 `LavenderChibiChair_360x380x680mm` 是历史资产。
`Props/DiningChairV002/` 与 `Props/DiningChairV003/` 是此前隔离的比例实验资产，
均不是当前 `GameSceneBuilder` 引用入口。

### 2. 参数化建模管线

`build_dining_chair_asset.py` 负责完整确定性构建：

1. 生成橡木和坐垫各 BaseColor、Normal、Roughness 三张贴图。
2. 建立座框、外撇腿柱、连续后柱、弧形靠背、软包坐垫、包边和木塞。
3. 打包贴图并保存 Blender 源文件。
4. 渲染主视图、正视图、侧视图、背视图和坐垫近景。
5. 导出 GLB/FBX，再回读验证尺寸、材质数和网格数。
6. 解析 GLB JSON，断言 6 张图片通过 BufferView 内嵌、木材与织物均保留完整 PBR
   通道且没有外部图片 URI。

最终 16 个生产网格全部为闭合流形，共 13,660 顶点、27,260 三角面。坐垫本体为
9,408 三角面，独立包边为 3,840 三角面，二者均不依赖 Blender 专有修改器才能显示。

### 3. Unity 构建职责

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/GameSceneBuilder.cs` 中的
`BuildDiningChair(Material)` 独占餐椅装配职责：

1. 调用 `ConfigureNormalMapTexture()` 将木材和织物法线设为 `NormalMap`，关闭 sRGB。
2. 导入 `NaturalOakDiningChair_480x505x820mm.fbx`，聚合所有 Renderer Bounds。
3. 以 0.482×0.82×0.5109 m 视觉包络和独立容差执行尺寸准入。
4. 将模型底部归一到根节点地面，并通过 `BuildCatalogMaterial("Chair", material)`
   生成 URP Lit 材质。
5. 使用 0.48×0.82×0.505 m 物理主体调用 `FurnitureShapes()` 建立简化碰撞。
6. 配置 5.5 kg 根 Rigidbody、木材 PhysicMaterial 和可雕刻 NavMeshObstacle。
7. 原位保存 `Chair.prefab`，再更新稳定 `Chair.asset`。

稳定约定：

- ID：`chair`
- 显示名：`餐椅`
- ItemDefinition GUID：`22898bdf986214a14883e06d935de66a`
- Prefab GUID：`e0c5b82c8b0db4edc95628e4e213e217`
- 占地：0.48×0.505 m
- 放置高度：0.82 m
- 质量：5.5 kg
- 目录可见：否，由现有产品策略统一控制

Prefab 只能保留一个物理所有者：根 Rigidbody + PhysicsBody。碰撞由 6 个 BoxCollider
组成，包括座框、四腿和靠背；禁止给高精木构、坐垫或包边添加动态 MeshCollider，
也禁止给坐垫添加独立 Rigidbody 或实时 Cloth。

### 4. 材质边界

Blender 与 GLB 完整保留木材和织物的 BaseColor、Normal、Roughness。Unity 当前 URP
材料接入 Base Map 和 Normal Map；木材 Smoothness 为约 0.58，织物为约 0.24。
Roughness PNG 保留在 `PropsSource/Chair/` 作为完整源数据，但当前未额外打包为 URP
Mask Map。后续若统一引入 Mask Map 管线，应在公共材质构建器中转换，不能只为餐椅
增加一套并行材质规则。

### 5. 验证与已知限制

结构化证据：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Chair/reports/dining_chair_model_report.json`
- `/Users/bytedance/MateSoul/项目沉淀资料/validation/dining_chair_virtual_item_20261015.json`
- `/Users/bytedance/MateSoul/.artifacts/framework/unity-tests.xml`

Unity 2022.3.38f1 场景重建退出码为 0，3 项目录/支撑/物理恢复测试全部通过。首轮模型
曾出现后柱穿过靠背正面和木塞 NGON 切线警告，分别通过调整后柱顶部中心/截面和使用
三角扇端盖修复。

本阶段未构建 Android APK，也未完成 PICO 真机 GPU、材质采样或抓取手感验证。受限
shell 仍会触发 Bee semaphore 问题，真实 Unity 编译与测试需从系统
Terminal/LaunchServices 启动。

## Phase Virtual Item Inventory 技术交接：限量库存、回收与特殊物品（2026-10-15）

### 1. 核心所有权

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameSession.cs`
同时拥有场景实例和背包剩余数量，业务层与 UI 不得自行维护数量。

关键职责：

- `inventory`：按目录 `ItemDefinition.Id` 保存剩余数量。
- `inventoryOwners`：把组合成员映射到目录物品，例如左右筷子都归属
  `chopsticks`。
- `GetRemainingQuantity()`：UI 查询的唯一入口，同时检查本地日期补货。
- `PlaceItemAsync()`：完成空位校验和锚点提交后扣减 1 个库存单元。
- `RecycleItem()`：按 `InventoryUnitId` 删除同一组合的全部场景实例并返还 1。
- `BreakItem()`：复用同一实例清理路径，但不返还库存。

`ItemDefinition` 的新字段定义真实内容策略：

- `InventoryLimit`：背包上限，0 表示不进入目录。
- `RestockDaily`：跨本地日期后将剩余数量补充到上限。
- `FixedAfterPlacement`：生成后进入 `PhysicsBody` contact/kinematic 状态。
- `PlacementPreview`：通过地面投影确认精确位置，不搜索附近替代点。

禁止根据 `table`、`apple` 等字符串在 `GameSession` 中增加玩法分支；后续内容应继续
通过上述定义字段扩展。

### 2. 存档模型与迁移

`GameSaveData` 新增：

- `ItemInventorySaveData[] inventory`
- `string dailyInventoryDate`

`ItemSaveData` 新增：

- `inventoryDefinitionId`
- `inventoryUnitId`

目录物品每次放置生成一个库存单元 ID。普通物品包含一个实例；筷子包含左右两个实例，
但二者共享同一 `inventoryDefinitionId=chopsticks` 和 `inventoryUnitId`。

旧存档兼容规则：

1. `inventory == null` 时初始化为空数组。
2. 缺少库存快照时，以 `InventoryLimit - 已放置库存单元数` 计算剩余数量。
3. 缺少所有者时按 `inventoryOwners` 推导；普通物品以旧 `instanceId` 作为单元，
   组合物品按成员定义和空间邻近关系重新归并为同一旧库存单元。
4. 存档中声明的所有者必须存在且与成员定义匹配，否则恢复故障闭合。

每日苹果按设备本地 `DateTime.Now.Date` 比较 `yyyy-MM-dd`。跨日后剩余数量直接补充
为 5，不删除前一天已放置的苹果；这表示“每日新增可取 5 个”，不是“房间内最多
同时存在 5 个”。

### 3. 餐桌放置流程

`ItemPlacementPreviewController` 挂在 `GameBootstrap` 同一对象上：

1. 首次点击餐桌进入预览模式。
2. 控制器把 1.2×0.7 m 投影放在视线水平前方约 1.8 m 的主地面。
3. `GameSession.TryResolveExactPlacement()` 校验 NavMesh 主地面与整块餐桌包围范围；
   只忽略角色自身碰撞，其他现实房间碰撞和虚拟物品都视为遮挡。
4. 可放置为绿色，不可放置为红色。
5. 再次点击且为绿色时，以 `searchNearby=false` 提交，不得自动挪到附近位置。
6. `GameItem.Bind()` 根据 `FixedAfterPlacement` 禁用 `PhysicsGrabInteractable`，
   并让 `PhysicsBody` 进入固定 contact 状态。

离开虚拟物品页或选择其他物品会取消投影。餐桌仍可被瞄准、查看和回收，但不能被
抓取、投掷或通过物理碰撞移动。

### 4. 现实时间时钟

资源入口：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/PropsSource/Clock/LavenderChibiClock_160x40mm.fbx`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/Clock.prefab`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RealTimeClock.cs`

模型契约：

- 时针枢轴：`Clock_HourHand_Pivot`
- 分针枢轴：`Clock_MinuteHand_Pivot`
- 旋转轴：枢轴本地 Y
- 正方向：从表盘正面观察为顺时针
- 模型默认角：时针 -52°、分针 56°

`RealTimeClock` 在 `Awake` 读取 FBX 默认局部旋转作为基准，之后只写两个枢轴：

- 分针角度：`(minute + second / 60) * 6`
- 时针角度：`(hour % 12 + minute / 60 + second / 3600) * 30`

运行时每 0.25 秒采样本地现实时间。不得旋转指针网格、时钟根节点或刚体；编辑器
构建阶段也不得调用 `ApplyTime()`，否则会把构建时刻烘焙进 Prefab 并导致二次叠加。

### 5. 内容入口与验证

高精刀入口：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/PropsSource/Knife/LavenderChefKnife_330mm.fbx`
- 330×50×24 mm，29,516 三角面，显示名为“刀”，库存 1。

目录策略由 `GameSceneBuilder.ConfigureItemPolicy()` 统一落盘。陪伴灯和餐椅仅隐藏，
稳定资产 GUID 仍保留用于旧存档识别。

验证结果：

- Unity Roslyn 四程序集依赖顺序编译通过。
- Bee 全量 `ScriptAssemblies` 构建及 Burst IL 后处理通过，906/906 节点评估完成。
- PlayMode 用例已覆盖库存耗尽/回收/重启、苹果跨午夜、筷子组合回收、目录数量策略、
  餐桌固定属性与时钟 30° 小时增量。

当前 TRAE 受限 shell 中 Unity 以 `bee_backend --ipc` 启动会触发
`mono_os_sem_post: semaphore_signal failed with error 15`。验证时可独立启动
`Unity.ILPP.Runner` 并用 Bee 非 IPC 模式完成全量编译；PlayMode Test Runner 仍需在
正常 Unity Editor 或系统 Terminal 中执行。

## Phase Tableware Size Variants 技术交接：等比资产派生与目录型号化（2026-10-15）

### 1. 型号契约

碗：

- `bowl`：138×138×70 mm，小号，沿用既有 ID/GUID。
- `bowl-medium`：180×180×91.304 mm，倍率 1.30434783。
- `bowl-large`：220×220×111.594 mm，倍率 1.59420290。

盘子：

- `plate`：200×200×25 mm，小号，沿用既有 ID/GUID。
- `plate-medium`：250×250×31.25 mm，倍率 1.25。
- `plate-large`：300×300×37.5 mm，倍率 1.50。

所有型号的 X/Y/Z 在 Blender 中表示宽、深、高；Unity Definition 的 Footprint 使用
X/Z，PlacementHeight 使用 Y。四个新型号均进入虚拟物品栏，库存上限为 2，并允许
放置到桌面等支撑表面。

### 2. 美术生成管线

生成入口：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/build_tableware_size_variants.py`

脚本流程：

1. 分别打开小号碗、盘的生产 Blend。
2. 仅按统一倍率修改生产 Mesh 顶点，不重采样、不改 UV、不改材质槽。
3. 为型号根节点写入 `size_class`、`uniform_scale_from_small`、`source_asset` 元数据。
4. 保存独立 Blend，导出 GLB/FBX。
5. 回读导出文件并验证尺寸、单网格、3 材质、闭合流形和 GLB 图片内嵌。
6. 从 Unity 小号盘破碎 FBX 派生中/大号轻重破碎 FBX，验证 4/22 个碎块和整体倍率。
7. 渲染从左到右为小/中/大的真实比例对比图。

有效入口：

- `Bowl/source/LavenderChibiBowl_180mm.blend`
- `Bowl/source/LavenderChibiBowl_220mm.blend`
- `Plate/source/LavenderChibiPlate_250x31mm.blend`
- `Plate/source/LavenderChibiPlate_300x38mm.blend`
- 对应 `exports/` 下同名 GLB/FBX
- `Bowl/reports/bowl_size_variants_report.json`
- `Plate/reports/plate_size_variants_report.json`

由于 Blender FBX 二进制包含非稳定导出数据，每次重建后 FBX SHA-256 可能变化；
最终交付必须重新同步到 Unity，并以报告中的当前哈希复核，不得用旧哈希判断几何变化。
GLB 在当前脚本中保持稳定哈希。

### 3. Unity 构建扩展

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/GameSceneBuilder.cs` 的
`BuildCatalog()` 负责六个碗盘型号的唯一注册。新增型号使用独立 ID、Prefab 和
ItemDefinition，不能用显示名或运行时缩放推导型号。

`BuildCatalogProp()` 新增两个可选参数：

- `materialGroup`：指定共享材质目录。中/大碗传入 `Bowl`，中/大盘传入 `Plate`，
  因而复用现有 URP 材质，不产生 `BowlMedium`、`PlateLarge` 等冗余目录。
- `blocksNavigation`：覆盖旧的尺寸阈值。中/大盘显式为 `false`，因为可放置餐具
  即使直径达到 0.25 m，也不应雕刻 NavMesh。

`BuildBreakableSettings()` 和 `BuildFracturePrefab()` 同步支持 `materialGroup`。
中/大盘各自引用等比破碎 FBX，但碎片材质继续归属 `Plate`。碗仍使用程序化碎片，
没有额外的预制碎片资产。

质量按统一缩放后的体积倍率确定并适度取整：

- 中/大碗：0.69/1.26 kg。
- 中/大盘：0.82/1.42 kg。

易碎阈值随直径提高，保持 `heavy > light`；所有陶瓷型号继续共享
`CeramicBreak.mp3`。

### 4. 测试契约

`GameRuntimeTests.ProductionCatalogContainsEveryStandaloneProp` 现在把六个碗盘 ID
纳入固定目录顺序，并验证：

- 显示名、Footprint、PlacementHeight、质量、库存和表面放置。
- 所有碗盘均启用易碎策略，非陶瓷目录项不误启用。
- 三个盘子型号均拥有 4/22 块轻重破碎 Prefab。
- 三个碗型号均不引用预制碎片，继续走程序化碎片。
- 全部目录 Prefab 具有 PhysicsBody 与 Collider。

易碎契约提取到普通 `AssertCatalogBreakables()`，避免 UnityTest 迭代器对临时
`BreakableSettings.Validate` 方法组生成不稳定状态机空引用。

正式测试证据：

- `/Users/bytedance/MateSoul/.artifacts/framework/unity-tableware-tests.xml`
- 结果：2/2 通过，0 失败。

HUD 全量分页用例在 `-nographics` 环境会触发既有 HTTP 字体回退限制，不能把该环境
故障解释为目录数据失败。场景 YAML 已直接包含四个新 Definition GUID，目录契约测试
也验证四项 `CatalogVisible=true`。

### 5. 交付与限制

结构化总报告：

- `/Users/bytedance/MateSoul/项目沉淀资料/validation/tableware_size_variants_20261015.json`

本阶段没有修改 PICO XR 能力边界，也没有构建 Android APK。后续真机验证应重点检查
大号盘 1.42 kg 和大号碗 1.26 kg 的抓取惯性、桌面摆放密度及破碎阈值体感。

## Phase Cooking Items 技术交接：组合锅具与无限库存（2026-10-16）

### 1. 内容定义

生产目录入口：

- `frying-pan`：显示“平底锅”，库存 1，组合 Definition。
- `frying-pan-body`：隐藏成员，1.3 kg。
- `spatula`：隐藏成员，0.18 kg。
- `raw-steak`：显示“生牛排”，0.35 kg，无限库存。

对应文件位于：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/FryingPan.asset`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/FryingPanBody.asset`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/Spatula.asset`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/RawSteak.asset`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/PropsSource/CookingSet/`

平底锅组合的两个 `ItemPlacementPart` 使用同一 `InventoryUnitId`。因此锅体和锅铲是
两个独立刚体、可分别抓取，但库存扣减与回收按一组处理。禁止把锅铲直接合并到锅的
渲染 Prefab，否则会破坏独立抓取和后续烹饪动作扩展。

### 2. 无限库存契约

`ItemDefinition` 新增 `UnlimitedInventory`，通过
`ConfigureUnlimitedInventory()` 配置。该策略不是一个极大整数：

1. `CatalogItems` 接纳 `InventoryLimit > 0` 或 `UnlimitedInventory=true`。
2. `GameSession.inventory` 和 `GameSaveData.inventory` 不保存无限物品计数。
3. `GetRemainingQuantity()` 对无限物品返回 `int.MaxValue`，仅供运行时准入判断。
4. 放置无限物品不扣减；回收无限物品不返还。
5. HUD 根据定义显示 `∞`，不能直接显示 `2147483647`。

`∞` 已加入 `UiFontAssetBuilder.WindowCharacters` 并烘焙到静态 Noto CJK SDF 资源，
Unicode 为 8734。

### 3. 锅具构建与定位

`GameSceneBuilder.BuildFryingPan()` 独占锅具装配：

1. 导入平底锅和锅铲 FBX，分别构建隐藏成员 Prefab。
2. 目录平底锅继续使用相同锅体模型作为预览，但运行时实际生成隐藏成员。
3. `ConfigureFryingPanColliders()` 删除通用整体 BoxCollider，生成一个薄锅底、四段
   低矮锅沿和一个手柄包围盒。
4. `CalculateSpatulaOnPanPosition()` 读取 `FryingPan_Body` 与
   `Spatula_SlottedBlade` 的 Renderer Bounds，在 Prefab 本地坐标中对齐锅体中心与
   铲面中心，并把锅铲最低点抬到锅底上方约 64 mm。
5. 锅铲绕 Y 轴偏转 -12°，避免完全重叠并保持手柄朝向接近锅柄。

不能恢复通用整体盒体；它会封死锅腔，使锅铲只能悬浮在平底锅包围盒顶部。

### 4. 材质

材质目录：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/Materials/CookingSet/`

`brushed_stainless_normal.png`、`nonstick_normal.png`、
`steak_raw_atlas_normal.png` 均按 Unity NormalMap、关闭 sRGB 导入。
`BuildCatalogMaterial()` 对 `MAT_Cookware_BrushedStainless` 显式设置
`_Metallic=1.0`，因为 Unity FBX 材质描述没有可靠保留 Blender/GLB 的金属度。

### 5. 验证

专项测试报告：

- `/Users/bytedance/MateSoul/项目沉淀资料/validation/cooking_items_tests_20261016.xml`
- `/Users/bytedance/MateSoul/项目沉淀资料/validation/cooking_items_20261016.json`
- 结果：3/3 通过，0 失败。

覆盖契约：

- 无限库存连续放置、回收后仍不耗尽，且不写库存快照。
- 平底锅一次放置生成锅体+锅铲，锅铲初始高于锅体，两者整组回收并恢复库存。
- 生产目录包含 `frying-pan`、`raw-steak`，隐藏成员不进入目录。
- 平底锅使用 6 个 BoxCollider，拉丝钢 Metallic=1 且 NormalMap 生效。
- 生牛排 NormalMap 与无限库存定义生效。

全量 `GameRuntimeTests` 的新增用例均通过。现存失败来自测试环境和历史断言，不应归因
于本阶段：`-nographics` 下 HTTP 字体回退被 Unity 拒绝、旧存档 JSON 比较未接受
规范化字段、以及既有易碎资产前置数据不完整。

## Phase Water Dispenser 技术交接：接水体积与池化溢水（2026-10-16）

### 1. 生产资产与目录契约

美术入口：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/WaterDispenser/source/CreamFloorWaterDispenser_340x372x1400mm.blend`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/WaterDispenser/exports/CreamFloorWaterDispenser_340x372x1400mm.glb`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/WaterDispenser/exports/CreamFloorWaterDispenser_340x372x1400mm.fbx`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/WaterDispenser/scripts/build_water_dispenser_asset.py`

Unity 入口：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/WaterDispenser.asset`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/WaterDispenser.prefab`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/WaterDispenserBuilder.cs`

稳定约定：

- ID：`water-dispenser`
- 分类：`ItemCategory.Furniture`
- 占地/高度：0.34×0.372 m / 1.4 m
- 质量：18 kg
- 库存：1
- 固定放置：是
- 地面投影：是
- 表面放置：否

Prefab 使用 4 个简化 BoxCollider、唯一根 `PhysicsBody`/Rigidbody 和一个
NavMeshObstacle。高精显示网格不得改为动态 MeshCollider。

### 2. 运行时所有权

`WaterDispenserController` 只拥有一个饮水机实例的出水状态、网格水柱和水迹池：

1. `WaterDispenserButton` 通过世界空间 UI/XR 指针点击调用
   `ToggleDispensing()`。
2. Controller 从 `WaterDispenser_Nozzle_Outlet` 向下执行
   `Physics.RaycastNonAlloc`，最多复用 16 个命中缓存，不产生托管数组。
3. 命中 `WaterReceiver` 时按 0.18 L/s 提交水量。
4. 容器容量不足或未命中容器时，从固定 6 个水迹对象中循环取一个。
5. 水迹在 2.4 s 内扩大、降低 Alpha 并停用；不得运行时 Instantiate/Destroy。
6. 连续出水最多 8 s 自动关闭；饮水机暂停时隐藏水柱并暂停流量。

该系统没有逐滴刚体、粒子碰撞或 Navier-Stokes 流体求解。PICO 移动端上，连续水柱
需要的是视觉连续性和接水判定，不需要高成本液体粒子之间的相互作用。

`WaterDispenserController` 不写饮水机根 Transform，也不写容器 Transform。
饮水机固定/暂停由 `PhysicsBody` 管理，容器抓取仍由 `GameItem`/XRI 管理。

### 3. 接水容器

`WaterReceiver` 挂在生产 Prefab 根节点，每个容器只持有一个无 Collider 的
`Contained Water` 圆柱水面：

- 马克杯：0.32 L。
- 小/中/大碗：0.55 / 1.2 / 2.1 L。
- 小/中/大盘：0.08 / 0.13 / 0.20 L。

接水判定使用本地开口中心、开口半径和最大 38° 倾角；水面高度按
`CurrentLiters / CapacityLiters` 映射。容器满后多余流量进入水迹池，不再静默吞水。

当前水量是场景实例级临时状态，不写 `GameSaveData`。保存布局后重启会保留容器位置，
但水量重置为空；这是本阶段明确边界，避免在核心物品存档中膨胀液体瞬态数据。

### 4. 构建器

`WaterDispenserBuilder.Build()` 负责：

1. 校验 340×1400×372 mm Unity 包络和交互节点。
2. 将 Blender 材质转换为 URP Lit；透明水桶与水体使用透明 Surface。
3. 创建水柱、6 个水迹和世界空间按钮，不给水体添加 Collider/Rigidbody；按钮热区
   必须使用 `WaterDispenser_DropButton` Renderer 中心，不能使用位于模型原点的
   `WaterDispenser_Button_Pivot.position`。
4. 配置固定家具物理、NavMeshObstacle、Definition 和场景目录引用。

`ConfigureReceivers(catalog)` 在所有碗盘和马克杯 Prefab 已生成后统一添加
`WaterReceiver`。因此调用顺序必须是先构建完整 `catalog`，再配置 Receiver，不能在
单个餐具 `BuildCatalogProp()` 内增加 `id` 字符串分支。

### 5. 模型契约

Blender/FBX 必须保留：

- `WaterDispenser_Button_Pivot`
- `WaterDispenser_Nozzle_Outlet`
- `WaterDispenser_DripTrayBase`

模型总包络为 340×372×1400 mm，36 个闭合网格、21,684 三角面。水桶外壳与桶内水体
必须是独立网格，GLB 必须内嵌 3 张 PBR 贴图且无外部 URI。

按钮 V002 契约：

- 深色按钮座：92×54 mm，保持静止。
- 可动按钮：76×44 mm，和水滴图标共同挂在
  `WaterDispenser_Button_Pivot` 下。
- 点击热区：78×52 mm，对齐按钮 Renderer 中心并位于前表面外 8 mm。
- 按下行程：5 mm；按下 45 ms、回弹 110 ms。
- 快速点击触发 120 ms 脉冲，即使 PointerDown/Up 同帧也必须显示完整动作。

### 6. 验证与限制

正式证据：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/WaterDispenser/reports/water_dispenser_model_report.json`
- `/Users/bytedance/MateSoul/.artifacts/framework/unity-water-button-tests.xml`
- `/Users/bytedance/MateSoul/项目沉淀资料/validation/water_dispenser_virtual_item_20261016.json`

按钮修复后的结果为 Unity 3/3 通过、场景重建退出码 0。测试覆盖目录与家具分类、
按钮热区对齐、按下/快速点击/回弹动画、马克杯接水、容量上限、满杯溢出、池化水迹
消失及无液滴刚体。修复前的基础测试 4/4 证据仍保留在
`.artifacts/framework/unity-water-dispenser-tests.xml`。

当前限制：

- 水面保持容器本地平面，不模拟倾倒和晃动。
- 水量不持久化。
- 水迹是短生命周期视觉，不改变地面摩擦。
- 尚未完成 Android APK 和 PICO 真机透明排序/交互手感验证。

## Phase Virtual Item Categories 技术交接：资产分类与 HUD 筛选（2026-10-16）

### 1. 数据契约

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemDefinition.cs` 定义：

- `ItemCategory.Furniture`：家具。
- `ItemCategory.Appliance`：电器。
- `ItemCategory.Tableware`：餐具。
- `ItemCategory.Kitchenware`：厨具。
- `ItemCategory.Food`：食品。
- `ItemCategory.Toy`：玩具。

`ItemDefinition.Category` 是唯一分类来源。`Configure(...)` 的
`ItemCategory itemCategory` 参数为必填项，使新资产在编译期暴露遗漏；不允许根据
`Id`、显示名、材质或物理参数在 UI 层推断分类。`GetDisplayName()` 统一提供中文名称，
避免按钮文案和资产枚举形成两套映射。

### 2. HUD 流程

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs` 保留完整
`catalog`，并为当前分类维护 `categoryCatalog`：

1. 初始化时创建家具、电器、餐具、厨具、食品、玩具六个分类按钮。
2. `SelectCatalogCategory()` 按 `ItemDefinition.Category` 过滤，重置页码并更新选中色。
3. `RefreshCatalogPage()` 只对分类结果分页，每页最多 6 项。
4. `PlaceCatalogItem()` 和 `RefreshCatalogQuantities()` 都按同一分类页索引取 Definition，
   不存在显示集合与执行集合错位。
5. 空分类显示“该分类暂无可用物品”，不会回退到其他分类或全部物品。

页面纵向布局为：标题 10、分类按钮 45、两行物品 88/132、分页 179、当前物品 219、
操作按钮 258；所有控件位于 330 高度内容区内。

### 3. 当前映射

目录可见映射：

- 家具：`table`、`water-dispenser`。
- 电器：`television`、`radio`、`clock`、`remote-control`。
- 餐具：`plate`、`plate-medium`、`plate-large`、`mug`、`bowl`、
  `bowl-medium`、`bowl-large`、`spoon`、`chopsticks`。
- 厨具：`knife`、`frying-pan`。
- 食品：`raw-steak`、`cupcake`、`cookie`、`apple`。
- 玩具：`playing-cards`、`dartboard`、`bowling-ball`、`bowling-pin`、
  `plush-doll`。

隐藏 Definition 仍必须正确分类：`chair` 为家具，`companion-lamp` 为电器，
`chopstick-left/right` 为餐具，`frying-pan-body` 与 `spatula` 为厨具。这样组合物品
拆分生成后仍保留稳定语义。

### 4. 生成器约定

以下生成入口必须显式传递分类：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/GameSceneBuilder.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/TelevisionBuilder.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/RadioBuilder.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/WaterDispenserBuilder.cs`

`ConfigureItemPolicy()` 在重写库存、可见性和放置策略时必须回传原
`definition.Category`，否则策略更新会意外清空分类。新类别属于产品模型变更，不应
通过枚举默认值或静默回退兼容。

### 5. 验证

`GameRuntimeTests` 的分类专项覆盖：

- 六个分类按钮完整且中文显示名唯一。
- 逐分类翻页后收集到的 ID 与该分类 Definition 完全等价。
- 六类结果并集与 26 个生产目录 ID 完全等价。
- 生产资产精确映射，包含刀=厨具、马克杯=餐具、电视=电器。
- 32 个现有 Definition 均有序列化 `category` 字段。

Unity 2022.3.38f1 最终专项结果为 2/2 通过。尚未做 Android/PICO 真机 UI 射线验证。

## Phase Latest Android Consolidation 技术交接：当前 Development APK（2026-10-16）

当前主工程已通过
`MateSoul.Editor.GameSceneBuilder.BuildAndroidDevelopment()` 完成场景重建与 Android
ARM64/IL2CPP 整包构建。交付信息：

- APK：`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- 大小：`135,186,241` 字节
- SHA-256：`f641785746d0eec786c29368edaa1e5d413c1ceb5b6f91a55bbbeba6f798d4f4`
- 构建类型：Development，供 PICO Emulator 和本地 HTTP AI Gateway 使用
- 内置电视节目 SHA-256：
  `c55bdafdcc691236eccb0c3639b2eb044d487eb6d19f6e7538c5e381879f0079`

`start-pico-emulator.command` 已按哈希把设备旧包 `aea65e...` 更新为该版本。安装后
应用保持前台，设备 `base.apk` 哈希一致；运行画面确认饮水机和 HUD 已进入当前版本。
后续新增功能若只完成 Unity 测试但未构建 Android APK，不能把根目录 APK 视为最新
运行版本。

## Phase Emulator Install Recovery 技术交接：ADB 安装回退（2026-10-16）

`/Users/bytedance/MateSoul/scripts/start-pico-emulator.command` 的 APK 同步流程不再只依赖
ADB 流式安装：

1. APK 哈希不一致时，首先执行 `adb install -r -g`。
2. 流式安装失败后，重启项目内 ADB server 并等待 `emulator-5554` 完成启动。
3. 将 APK 推送到 `/data/local/tmp/matesoul-install.apk`。
4. 在设备侧执行 `pm install -r -g`。
5. 无论安装成功或失败都清理设备临时 APK；成功后继续校验设备 `base.apk` SHA-256。

该回退用于处理 PICO Emulator 冷启动期间 ADB 流式传输连接失稳。磁盘不足、签名冲突、
Manifest 错误仍应直接按真实错误处理，不能通过重复安装掩盖。

当前根目录和设备安装包 SHA-256 均为
`4b224fe6734b774b6fd9347158b81f3b89eea3438e9f5628010290428cded232`；默认启动脚本
冷启动验证通过。

## Phase Cut Fruit Runtime 技术交接：预约基座实施中（2026-10-16）

- 继续执行清单：`/Users/bytedance/MateSoul/.artifacts/cut-fruit-action/IMPLEMENTATION.md`；
  本阶段不是系统交付完成。
- 当前 `ItemRegistry` 已加入控制态、Guid 预约、generation、批量预约、心跳与统一释放；
  `GameItem` 的持有态由该状态派生。`PhysicsBody.Interaction.cs` 拥有预约期间受控物理。
- 回收组合物前查询每个成员预约；电视、收音机、饮水机入口、破碎和碰撞声接入预约门禁。
- 新刀为 33 cm 厨刀，新餐桌 1.2×0.7 m 且放置后固定；三种盘子需共同支持接收水果。
  新增库存归属规则必须参与切块事务，不能回收四片得到四份苹果。
- 编译基线通过；定向预约测试因另一个 Unity 实例占用主工程未启动，不能宣称测试通过。
  后续采用隔离副本验证。Action 编排、动画、切割产物、菜单仍待继续完成。

## Phase HUD Visibility 技术交接：统一可见性与输入门禁（2026-10-16）

### 1. 输入占用与键位契约

当前输入实现以实际 XRI 绑定为准：

| 模式 | 已占用输入 | 用途 | UI 切换 |
|---|---|---|---|
| 手柄 | `triggerPressed` / `trigger` | UI 点击、地面放置确认 | 右手 `secondaryButton`，即 B 键 |
| 手柄 | `gripPressed` / `grip` | 虚拟物品抓取 | 不复用 |
| 眼手/手势 | `pinchTouched` / `pinchValue` | 选择、抓取、放置确认 | 不复用 |
| 眼手/手势 | `pointerActivated` | UI 点击 | 不复用 |
| 眼手/手势 | `graspFirm` | 原先未使用 | 左右手同时握拳 0.5 秒 |

不使用 Menu/Home/System：这些键由 PICO 系统或平台导航持有。A 键虽然当前没有正式
应用绑定，但保留为通用确认语义；B 键更符合返回/界面切换语义。

### 2. `GameHud` 状态所有权

文件：
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs`

`GameHud` 是 HUD 可见性的唯一 owner：

1. `CreateHudToggleActions()` 创建并启用右手 B、左手握拳和右手握拳三个
   `InputAction`。
2. `UpdateHudVisibilityInput()` 每帧读取输入，不受 HUD 的 0.2 秒信息刷新节流影响。
3. 手柄按键只在释放后 armed；双手握拳必须连续达到 0.5 秒，并在两手完全松开后才
   重新 armed，防止长按每帧重复切换。
4. `SetHudVisible()` 只切换 `Bottom HUD` 根节点，不重置 `activePage`、不停止小游戏、
   不改变会话状态；因此重新显示时恢复隐藏前页面。
5. 隐藏 HUD 时若 TMP 输入框仍被 EventSystem 选中，会清除该选择，避免不可见输入框
   继续接收文字。
6. `OnDestroy()` 统一 Dispose 三个运行时 InputAction。

`VisibilityHint()` 根据 `PlayerInteractionModeController.CurrentMode` 返回当前模式提示。
底栏保持 824×160，导航按钮高度从 140 调整为 127，提示行位于 y=137；提示和按钮各自
有稳定 RectTransform，不发生覆盖。

### 3. 粉色焦点高亮联动

粉色光边的唯一 owner 是
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/VirtualItemFocusController.cs`。

`SetHudVisible(false)` 禁用该组件。Unity 同步调用其 `OnDisable()`：

1. 对当前 `focused` 物品执行 `SetFocused(false)`。
2. `GameItem` 恢复基础材质数组，移除 `ItemFocusHighlight.mat`。
3. 清空 `focused`，隐藏期间不再执行相机中心 SphereCast。

`SetHudVisible(true)` 重新启用组件，下一帧按当前视线重新解析焦点。不要在
`GameItem`、手柄射线和手势射线中各自增加 UI 可见性判断，否则会形成多套高亮状态。

### 4. 字体与生成资源

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AlwaysOnTopUi.cs` 的
`InitialFontCharacters` 已加入“展示隐藏键双握拳”。静态字体由
`UiFontAssetBuilder.Ensure()` 生成；Editor 脚本重载和 Android 构建都会执行 Ensure。
现有动态 Noto Sans CJK fallback 仍负责未预收集字符，不应为单条提示创建新字体资产。

### 5. 验证边界

- 最终 HUD 两个源码文件已通过 Unity Roslyn 隔离复编译。
- 完整 `Gameplay → Runtime → Engine Tests` 依赖编译曾在并行切水果改动进入未完成
  状态前通过。
- 回归断言位于本地 Engine Test harness，覆盖绑定路径、提示布局、默认显示、
  隐藏/恢复和 `VirtualItemFocusController.enabled` 联动。
- 当前正式 Test Runner 被并行开发中缺失的 `CutFruitAction` 与
  `ReportItemAction` 阻断，不能记录为运行通过；应在该分支恢复可编译后重跑
  `MateSoul.Tests.GameRuntimeTests.BottomHudExpandsVerticalInteractionActivities`。

## Phase Cut Fruit Runtime 技术交接：多物品 Action 与切割事务（2026-10-16）

### 1. 阶段概述

此前“预约基座实施中”的状态已结束。当前主工程已形成可运行的切水果闭环，平台仍是
Unity + PICO Unity SDK，不涉及 Kotlin、Spatial ECS 或 Spatial BOM。

测试入口：HUD“互动 → 切水果”；取消入口：“取消切水果”。所需能力为：

| 角色 | capability | 当前内容 |
|---|---|---|
| 水果 | `cuttable-fruit` | 完整苹果 |
| 刀具 | `cutting-tool` | 33 cm 厨刀 |
| 容器 | `food-receiver` | 小/中/大盘子 |
| 工作台 | `food-workstation` | 餐桌 |

### 2. 核心类与所有权

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemActionRun.cs`
  负责一次多物品 Action 的批量准入、步骤序列、心跳、取消和统一释放；子步骤禁止再次
  提交父协调器。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemTransferStep.cs`
  一次只占用一只手，执行接近、对齐、伸手、抬起、携带、桌面放置和收手。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CutFruitAction.cs`
  固定业务顺序：盘子 → 苹果 → 刀 → 两次垂直切割 → 四块装盘。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Gameplay/ItemRegistry.cs`
  是物品控制权唯一 owner。玩家抓取、角色预约、持有、暂存和使用均共享同一租约。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/PhysicsBody.Interaction.cs`
  是角色操作期间刚体位姿唯一 owner；每个移动段执行保守占位与扫掠检测。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarItemRig.cs`
  在烘焙 Generic 基础动作之后做有界手腕接触修正，不使用纯站姿 IK 硬拉地面物品。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemInteractionProfile.cs`
  保存 grip、carry、blade、receiving height 和工作槽；新增同类物品应扩展 capability 与
  profile，不得在协调器增加物品名分支。

### 3. 执行流程与不变量

1. `Check()` 只查找候选，不加锁；缺物品通过 `SystemNotifications` 返回。
2. `Start()` 使用 `TryReserveBundle()` 全有或全无预约四类物品。
3. 已搬到餐桌的物品进入 `StagedForAction`，保持预约但释放手容量。
4. 切割要求实际刀刃中点从苹果上方穿过中心接触窗，不能只依赖计时器。
5. `CommitCut()` 先创建并验证四块产物，再在无 await/callback 的提交区删除完整苹果。
6. 提交后即使后续刀具归位或保存失败，也保留切块，不复活完整苹果。
7. 取消、暂停、超时和物品移除统一 Dispose 租约；释放回调先恢复物理，再开放玩家控制。

### 4. 动画与物理踩坑

- 冷月右臂实测总长约 `0.421 m`。动作必须按运行时骨骼比例烘焙；地面蹲姿骨盆目标
  `0.17 m`，桌面姿势 `0.74 m / 42°`，切割姿势 `0.64 m / 50°`。
- 携带点必须同时满足手臂可达和角色碰撞净空。当前角色局部坐标为
  `right=.21, y=1.28, forward=.43`。
- `AvatarItemRig.ContactError` 在 `LateUpdate` 更新。Action `Tick` 设置目标后必须等待一个
  求解帧，不能同帧准入。
- 切割基础姿势不能按刀刃下落时间采样；第一刀前完整进入 `Cut`，两刀期间保持末帧。
- 刀具从切割区回槽必须走“上升 → 高位平移/旋转 → 下降”路径，禁止低位斜穿。

### 5. 内容与资源

- 动作构建器：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/ItemActionContentBuilder.cs`
- 运行配置与动作：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/ItemActions/`
- 苹果块 Definition/Prefab：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/AppleQuarter.asset`
  和 `AppleQuarter.prefab`
- 正式模型源：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/PropsSource/Apple/GameApple_Quarter_45x45x84mm.fbx`

### 6. 验证与交付

- 主工程相关 Unity 测试：24/24 通过。
- Android Development ARM64/IL2CPP：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `4b224fe6734b774b6fd9347158b81f3b89eea3438e9f5628010290428cded232`
- PICO Emulator 因宿主 `HV_DENIED` 无法启动，本轮没有设备侧自然度结论；后续必须在可用
  模拟器或真机验证动作观感、桌边净空和取消手感。

## Phase Water Dispenser Button V002 技术交接（2026-10-16）

### 1. 根因

`WaterDispenser_Button_Pivot` 位于模型根原点，只有按钮和图标作为远端子节点。
V001 构建器使用 `buttonPivot.position` 创建世界空间 Canvas，导致点击热区落在饮水机
底部附近，而不是可见按钮上。按钮几何同时与面板近似齐平，缺少明确的机械层次。

### 2. 修复契约

- 点击热区中心必须取 `WaterDispenser_DropButton` 的 `Renderer.bounds.center`。
- 热区尺寸固定为 78×52 mm，并沿按钮前表面法线外移 8 mm。
- 深色 `WaterDispenser_ButtonSocket` 保持静止。
- `WaterDispenser_DropButton` 和 `WaterDispenser_DropIcon` 挂在 Pivot 下共同移动。
- 行程 5 mm；按下 45 ms，回弹 110 ms，快速点击脉冲 120 ms。
- 点击脉冲必须由 `WaterDispenserButton.ToggleWater()` 显式触发，不能只依赖
  PointerDown 持续时间。

### 3. 验证

- 模型 V002：36 个闭合网格、21,684 三角面，包络仍为 340×372×1400 mm。
- Unity Prefab 热区锚点高度 0.978 m、前方 0.194 m；按钮按压序列化位移为 5 mm。
- `/Users/bytedance/MateSoul/.artifacts/framework/unity-water-button-tests.xml`
  为 3/3 通过。
- 测试要求热区距按钮视觉中心小于 25 mm、实际按下位移大于 3.5 mm，且快速点击后
  必须先完成按压脉冲再回到原始 Pivot 位置。

## Phase Water Dispenser Button V003 技术交接（2026-10-16）

V002 水滴图标背面位于按钮正面附近，透明/抗锯齿与深度精度组合下可能出现共面闪烁。
V003 几何契约：

- `WaterDispenser_DropButton` 正面保持原位置。
- `WaterDispenser_DropIcon` 厚度为 1.2 mm。
- 图标背面与按钮正面设计间隙为 0.8 mm。
- 图标与按钮均继续挂在 `WaterDispenser_Button_Pivot` 下，不改变 5 mm 按压行程。
- 机身后部内收 2 mm，资产总深度仍为 372 mm。

`WaterDispenserTests.ProductionAssetsKeepFurnitureAndReceiverContracts` 使用 Unity 导入后
的 Renderer Bounds 判定前后方向，并要求水滴浮雕与按钮表面间隙至少 0.4 mm。该断言
避免后续改模重新引入共面或穿模。

正式证据：

- `/Users/bytedance/MateSoul/.artifacts/framework/unity-water-icon-tests.xml`
- 结果：2/2 通过，场景重建退出码 0。

## Phase Interaction Component Focus 技术交接：声明式交互光圈（2026-10-16）

### 1. 生效规则

本章节替代前文“HUD 隐藏时禁用 `VirtualItemFocusController`”的旧规则。当前规则为：

1. `VirtualItemFocusController` 始终按相机中心对整件 `GameItem` 做 SphereCast。
2. 命中物品后，`GameItem.SetFocused(true)` 同时通知该物品下所有
   `ItemInteractionFocusTarget`。
3. 光圈是否显示不依赖 HUD 展开/隐藏，也不要求指针精确 hover 某个控件。
4. 没有 `ItemInteractionFocusTarget` 的物品不会显示任何焦点光圈。
5. 控件不可用、物品被角色预约或组件禁用时，标记自动移除材质；重新可用且物品仍在
   焦点中时可自动恢复。

### 2. 通用扩展契约

新增文件：
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemInteractionFocusTarget.cs`

该组件持有：

- 可选 `Selectable control`：用于读取控件动态 `IsInteractable()`；纯物理交互可不填。
- `Material focusMaterial`：统一使用 `ItemFocusHighlight.mat`。
- `Renderer[] renderers`：只包含该交互组件的可视网格。
- `requested/applied` 双状态：物品焦点请求与实际可用状态分离。

新增虚拟物品时按以下流程接入：

1. 交互脚本继续拥有业务操作和 pointer hover/按压反馈。
2. 在同一控件节点增加一个 `ItemInteractionFocusTarget`。
3. `Configure(control, material, renderers)` 只登记真实可操作部件，不登记机壳、屏幕、
   标签或整件模型。
4. 不修改 `VirtualItemFocusController`，不在 `GameItem` 增加物品 ID/名称判断。
5. 构建器测试必须断言标记数量、关联控件、材质和 Renderer 非空。

`ItemInteractionFocusTarget` 只在实际状态变化时写 `sharedMaterials`，平时
`LateUpdate()` 只检查请求和控件可用性，不会每帧重新分配材质数组。

### 3. 当前生产映射

| 物品 | 标记数量 | Renderer 范围 |
|---|---:|---|
| 复古电视 | 1 | `TV_Power_Button` |
| 复古收音机 | 3 | 电源滑块、调谐旋钮层级、音量旋钮层级 |
| 饮水机 | 1 | `WaterDispenser_DropButton` |

生产配置入口：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/TelevisionBuilder.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/RadioBuilder.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/WaterDispenserBuilder.cs`

`GameSceneBuilder.BuildCatalog()` 把同一个光圈材质传给三个构建器。电视和收音机
Definition 现在也持久化 `FocusMaterial`，但 `GameItem` 不再读取它并遍历整物
Renderer；实际范围只由标记组件声明。

### 4. HUD 解耦

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs` 的
`SetHudVisible()` 只切换 `Bottom HUD` 并清理不可见输入框焦点，不再修改
`VirtualItemFocusController.enabled`。因此：

- HUD 展开：交互部件光圈正常。
- HUD 隐藏：交互部件光圈仍正常。
- 重新展开：无需重建或重新解析焦点状态。

### 5. 光圈参数

统一材质：
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/ItemFocusHighlight.mat`

- `_OutlineWidth = 0.003`
- `_Intensity = 0.65`
- `_Color = (1.0, 0.16, 0.48, 0.70)`

Shader 默认值与 `GameSceneBuilder.BuildFocusMaterial()` 写入值保持一致。不得只手改
Material，否则下次场景重建会被构建器旧值覆盖。

### 6. 验证

- 场景重建成功，三个生产 Prefab 序列化标记数量为 1/3/1。
- Unity Roslyn Runtime、Editor、Engine Tests 编译通过。
- Bee 全量脚本构建 906/906 成功。
- `/Users/bytedance/MateSoul/.artifacts/interaction-focus/test-results.xml`：
  17 项中 16 通过、0 失败、1 个外部诊断视频可选用例跳过。
- 关键回归包括：中心焦点同时请求全部收音机控件、电视仅按钮追加材质、普通物品零整包
  光圈、HUD 隐藏保持焦点，以及原电视/收音机/饮水机交互链不回归。

## Phase Action Locomotion Recovery 技术交接：规划与物理位移分权（2026-10-16）

### 1. 所有权契约

Action 移动链路固定为：

1. `MoveAction` 和 `RoomNavigationService` 负责目的地、全局完整路径与失败语义。
2. `AvatarMotor` 内的 `NavMeshAgent` 只负责局部避障和期望速度。
3. `AvatarMotor.FixedUpdate()` 把期望速度提交给 `PhysicsBody.AddForce()`。
4. `PhysicsBody`、Rigidbody 是角色位置的唯一写入者，Animator 不应用 Root Motion。

`NavMeshAgent` 必须保持 `updatePosition=false`、`updateRotation=false`、
`updateUpAxis=false`，每帧用真实刚体位置同步 `nextPosition`。禁止恢复 Agent 对
Transform 的直接写入，否则会和碰撞、重力、推物及角色姿态形成双 owner。

### 2. 局部避障与阻塞准入

- Agent 使用高质量局部避障、自动重规划和 0.12 m stopping distance；
  `desiredVelocity` 非零时优先于静态路径拐点方向。
- `MoveAction` 的进展定义是“当前目标水平剩余距离相对历史最优值至少改善 0.015 m”，
  不能使用任意方向位移，否则碰撞回弹和横向抖动会永久重置阻塞计时。
- 目标水平漂移超过 0.1 m 时重新建立进展基线。停滞期间每 0.75 秒重新计算路径，
  连续 3 秒没有有效改善才抛出 `Movement is blocked by an obstacle.`。
- 恢复机制不能取消最终 fail-closed：未进入 NavMesh 的硬墙仍由真实碰撞阻挡，并在
  3 秒窗口后失败，不能穿透或无限等待。

### 3. 回归契约

- `CharacterLocallyAvoidsNonCarvingNavObstacle`：非 carving 动态障碍必须成功绕行，
  横向偏移大于 0.45 m，并到达目标。
- `CharacterPushesLightObjectButCannotWalkThroughAnUnmappedWall`：轻物体可被推动，
  未映射硬墙不可穿透，最终 Action 必须失败并停止动画与运动。
- `RealCharacterCollectsCutsAndServesWithoutDuplicatingInventory`：局部避障改动不得破坏
  完整切水果、物品预约或库存事务。
- 完整相关回归结果：
  `/Users/bytedance/MateSoul/.artifacts/cut-fruit-action/movement-regression.xml`，
  27/27 通过。

### 4. 构建与 Unity 生命周期

- `MaterialPropertyBlock` 等 Unity native wrapper 禁止在 `MonoBehaviour` 构造器或字段
  初始化器中创建。Editor 构建器通过 `AddComponent()` 创建组件时也会触发该限制；
  应在 `Awake()` 或首次使用点按需创建。
- 当前 Android Development ARM64/IL2CPP 交付物：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  SHA-256 `10b88beed6b7029c62f095c98b535a809740c33686451e359d3e638b21cf3af6`。
- `movement-blocked` 调试会话仍为 `[OPEN]`。在 PICO 真机确认修复前保留网络插桩和
  调试服务；确认后按调试流程一次性删除插桩、日志、env 和调试记录。

## Phase Cooking System V001 技术交接：分层传热与可烹饪标记（2026-10-16）

### 1. 组件职责

- `HeatingBaseController`：电源、40–100℃温度、显示屏、锅底候选扫描和接触面积判定。
- `HeatingBaseButton`：Power/Decrease/Increase XR 点击、2.5 mm 压下和自动回弹。
- `CookwareHeatReceiver`：锅底热源绑定、锅内低频空间查询和温度传递。
- `CookableFood`：不可见熟度、50/70/100 阶段、`>100` 煮糊标记、状态持久化和
  生熟材质切换。
- `CookingSettings`：`ItemDefinition` 上的内容标记。底座和锅具不得根据食材名称
  判断是否可加热。

### 2. 熟度与状态

每次传热使用：

```text
gain = max(0, temperatureCelsius - 20) * durationSeconds * donenessPerDegreeSecond
```

当前生牛排 `donenessPerDegreeSecond=0.025`。阈值：

- `<50`：`Raw`
- `50–<70`：`Medium`
- `70–<100`：`MediumWell`
- `100`：`WellDone`
- `>100`：`Burned`

熟度钳制到 120。物品状态分别写入 `cooking.raw`、`cooking.medium`、
`cooking.medium-well`、`cooking.well-done`、`cooking.burned`。保存恢复按阶段阈值
恢复，不向 HUD 展示连续熟度。

### 3. 接触与性能

`HeatingBaseController.CircleOverlapArea()` 使用两圆相交解析公式。锅具准入同时要求：

1. 锅底法线与底座法线夹角不超过 12°；
2. 垂直间隙处于 −4 至 14 mm；
3. 加热盘与锅底面积比至少 0.55。

底座与锅具均以 0.2 秒间隔调用固定数组版本 `Physics.OverlapBoxNonAlloc`；锅内判定
使用锅局部坐标的中心距离、食材包围半径和高度区间。查询数组容量为 24，并在同一
采样内去重 `CookableFood`，没有每帧 MeshCollider 面积计算或动态集合分配。

### 4. 内容扩展

新增可烹饪食材时：

1. 为 `ItemDefinition` 调用 `ConfigureCooking(new CookingSettings(response))`；
2. Prefab 保留普通 Collider/Rigidbody，由 `GameItem.Bind()` 自动绑定
   `CookableFood`；
3. 如需熟后外观，调用 `CookableFood.ConfigureVisual(rawRoot, cookedRoot)` 配置两个
   同父节点、同局部位姿的完整模型根节点；
4. 不可烹饪食品必须保持 `CookingSettings.enabled=false`。

新增锅具时只挂 `CookwareHeatReceiver` 并配置锅底中心、接触半径、内部半径和内部
高度，不修改底座逻辑。

### 5. 资源与验证

- 模型：`Assets/MateSoul/Content/PropsSource/CookingSet/ElectricHeatingBase_330x300x55mm.fbx`
- Prefab：`Assets/MateSoul/Content/Props/HeatingBase.prefab`
- 定义：`Assets/MateSoul/Content/Props/HeatingBase.asset`
- 构建器：`Assets/MateSoul/Editor/HeatingBaseBuilder.cs`
- 测试：`Assets/MateSoul/Tests/Engine/CookingSystemTests.cs`
- 验证报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/HeatingBaseV001/reports/unity_validation_report.json`

定向测试 4/4、场景重建和 Android ARM64/IL2CPP Development 构建通过。全量测试在
既有 PlayMode 域重载阶段挂起，未产生汇总结果；后续需要拆分长时套件或定位 Test
Runner 生命周期问题，不能把该次全量运行记录为通过。

## Phase Coffee Bean Jar V001 技术交接：单刚体盖子与有限豆簇（2026-10-16）

### 1. 组件职责

- `CoffeeBeanJarLid`：保存闭合姿态、受力阈值、目标开合状态和 108° 盖子动画。
- `CoffeeBeanJarLidInteractable`：提供 XR Trigger 选择入口和轻触觉反馈，只提交开合
  请求，不拥有物理状态。
- `CoffeeBeanJarController`：判定盖子开启、罐体倾斜和释放节拍，扣减有限容量并同步
  罐内豆层高度。
- `CoffeeBeanCluster`：管理一簇 28 颗合并可视豆的短时刚体运动、稳定冻结和延迟清理。
- `CoffeeBeanJarBuilder`：导入模型、转换 URP Lit 材质、配置碰撞体、运行时绑定、
  Prefab、ItemDefinition 和两瓶库存。

### 2. 物理所有权与盖子交互

`PhysicsBody.Configure()` 禁止子 Rigidbody，因此木盖不能建立第二套刚体状态。生产
Prefab 保持以下契约：

1. 罐根节点持有唯一 `PhysicsBody` 和 Rigidbody。
2. 木盖网格节点持有非 Trigger `BoxCollider`，用于识别只作用于盖子的碰撞冲量。
3. 木盖子节点持有 Trigger `SphereCollider` 和 `CoffeeBeanJarLidInteractable`，用于
   XR 直接选择。
4. 根控制器的 `OnCollisionEnter()` 只接受 `thisCollider` 等于木盖 Collider 的接触，
   冲量达到 0.12 才提交打开请求。
5. 盖子 Pivot 由 `CoffeeBeanJarLid` 唯一写入；业务层不得直接改盖子 Transform。

Imported Model Prefab 内部 Transform 不能直接重挂到运行时容量 Pivot。构建器必须先
使用 `PrefabUtility.UnpackPrefabInstance(..., PrefabUnpackMode.Completely, ...)`
解包，再把 `CoffeeBeanJar_InteriorBeans` 挂到容量 Pivot；否则 Unity 会拒绝父级修改。

### 3. 有限容量与视觉等价

- 罐内可视豆固定为 336 颗并合并成一个 Renderer。
- 运行时容量固定为 12 簇，每簇模型包含 28 颗可视豆并合并成一个 Renderer。
- `12×28=336`，因此完整倾倒量与满罐可视量保持一一等价，而不是无限粒子发射。
- 只有盖子开启比例至少 0.85，且 `dot(jar.up, world.up) <= 0.52` 时才积累释放时间。
- 释放间隔为 0.11 秒；`RemainingClusterCount` 归零后控制器立即停止，不能回收后补豆。
- `FillPivot` 的 Y 缩放等于剩余簇比例；归零时关闭节点，避免空罐残留豆层。

容量是罐实例的运行时状态，目前不写入存档。若未来需要持久化，必须存储已释放簇数或
剩余簇数，不能在加载时通过场景中豆簇数量反推。

### 4. 性能边界

每个豆簇只包含一个 Renderer、一个 Rigidbody 和一个 SphereCollider，不为单颗豆创建
物理组件。豆簇速度稳定 0.45 秒或运动满 4 秒后转为 Kinematic，并在生成 45 秒后清理。
单瓶最多 12 个活动豆簇，两瓶库存上限对应最坏 24 个活动刚体。后续提高视觉豆数时应
修改合并网格，不得线性增加 Rigidbody 或 Collider。

### 5. 内容与验证

- 美术源文件：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/CoffeeBeanJar/`
- 生产 Prefab：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/CoffeeBeanJar.prefab`
- 豆簇 Prefab：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/CoffeeBeanCluster.prefab`
- 物品定义：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/CoffeeBeanJar.asset`
- 运行时脚本：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CoffeeBeanJarController.cs`
- 测试：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/CoffeeBeanJarTests.cs`
- 验证报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/coffee_bean_jar_virtual_item_20261016.json`

Unity 场景重建成功，专项测试 3/3 通过。PICO 真机上的受力阈值、倾倒手感、透明材质
排序和双瓶并发性能仍需设备验收。

## Phase Dialogue UI And Social Attention V001 技术交接：可逆 UI 过渡与社交注视 Action（2026-10-17）

### 1. 组件职责

- `GameHud.BuildDialogueBar()`：构建独立于通用内容面板的 824×112 底部输入条。背景由
  68% Alpha 浅紫玻璃层、14% Alpha 顶部雾面高光和阴影组成。
- `GameHud.AnimateDialogue()`：以 `CanvasGroup`、`anchoredPosition` 和中心 Pivot
  横向缩放驱动可逆过渡。展开 0.62 秒，关闭 0.58 秒；关闭横向目标比例为 0.04。
- `GameSession.UpdateSocialAttention()`：合并“对话展开”和“玩家靠近”两个请求源，
  维护距离滞回并向唯一 Action 协调器提交或取消动作。
- `SocialAttentionAction`：拥有 `Locomotion | Gaze`，停止角色移动并以 48°/s 更新身体
  朝向。它是 UI 级优先级，可抢占 `Autonomous`，但会让位于 Player/System 动作。
- `AvatarSocialAttentionRig`：在执行顺序 12000 的 `LateUpdate()` 中为 Generic Rig
  追加颈部、头部和双眼旋转，不修改动画片段、Animator Controller 或角色根 Transform。

### 2. UI 状态与对话生命周期

1. 点击“对话”先把通用内容页切为 `None`，设置
   `GameSession.SetDialogueAttention(true)`。
2. 导航栏在 0.34 秒内渐隐并下沉 14 px；0.1 秒后对话条从 X=0.08、Y=0.88 展开到 1。
3. “发送”只调用 `Conversation.SendAsync()`；请求被接受后清空输入框，但不改变
   `dialogueOpen`。
4. “关闭”只提交 `SetDialogueAttention(false)` 并播放挤压退出，不调用
   `Conversation.Cancel()`。系统键盘引起的暂停/失焦也不取消在途请求。
5. 导航使用 `EaseOutBack` 从下方向原位回弹；动画期间两个 `CanvasGroup` 均关闭
   Raycast，完成后只允许当前可见组交互。

动画从当前 Alpha、位置和缩放取起点，因此快速反向切换不会跳帧。B 键或双手握拳只
隐藏 `hudRoot`，不重置对话页状态和输入状态。

### 3. 社交注视准入与滞回

- 玩家水平距离进入阈值为 1.55 m，退出阈值为 1.90 m。高度不参与近距离判定，避免
  HMD 高度差导致角色在玩家身边反复进入/退出。
- 请求表达式为 `dialogueAttentionRequested || playerNearby`。两个来源任一存在即保持
  同一 `social-attention` Action，不建立平行控制器。
- 请求刚出现时允许按 UI 优先级提交，因此可安全抢占自主漫游。若已有 UI、AI、Player
  或 System Action，协调器拒绝新动作；会话只在角色重新 Idle 后重试。
- 请求消失时通过 `Avatar.Cancel()` 退出，头眼权重按指数响应平滑归零。若因更高优先级
  Action 抢占则立即释放头眼控制，避免退出残留与新动作叠加。
- `AvatarMotor.FaceAtSpeed()` 只改变现有 Rigidbody 朝向目标及转速；最终旋转仍由
  `PhysicsBody.RotateTowards()` 执行，未引入第二个 Transform owner。

### 4. Generic Rig 限幅

运行时在角色组装边界绑定 `Neck`、`Head 1`、`Eye_L`、`Eye_R`。缺少任一骨骼时
fail-closed，启动报错，不静默退化。

- 头部水平限幅：±48°。
- 头部俯仰限幅：向上 26°、向下 20°。
- 眼球剩余补偿：水平 ±12°、俯仰 ±8°。
- 颈部承担 28% 水平/22% 俯仰，头部承担其余部分。
- 权重响应：进入 5.5、退出 4.0 的指数平滑。

身体每帧缓慢朝玩家转动，所以玩家绕行使局部头部角度达到限幅后，身体会继续跟进并把
头部拉回舒适区。不要把该逻辑改为 UI 直接旋转骨骼，也不要恢复 Humanoid
`Animator.SetLookAt*`；当前生产角色是 Generic Rig。

### 5. 文件与验证

- UI：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs`
- 社交注视：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarSocialAttention.cs`
- 会话仲裁：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameSession.cs`
- 运行时组装：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameBootstrap.cs`
- 身体低速朝向：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarMotor.cs`
- 回归测试：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/GameRuntimeTests.cs`
- 结果：
  `/Users/bytedance/MateSoul/.artifacts/dialogue-ui/regression.xml`

三程序集静态编译通过，Unity 相关回归 5/5 通过。当前没有 Android/PICO 视觉验收结论；
雾透 Alpha、世界空间动画速度和头眼转幅必须在目标头显上最终确认。

## Phase Turn In Place V001 技术交接：任意角度转身求解与二维步态（2026-10-17）

### 1. 所有权与组件职责

- `TurnInPlaceMotion` 是无场景依赖的角度求解器。输入剩余有符号角、最大角速度和
  `deltaTime`，输出本帧角位移、实际角速度、剩余角和归一化 `TurnRate`。
- `AvatarMotor` 仍是朝向请求的唯一入口。它判断当前是否适合原地转身，并把求解器输出
  提交给 `PhysicsBody.RotateTowards()`；业务 Action、Animator 和动画片段都不直接写
  根 Transform。
- `AvatarLocomotionBuilder` 维护生产 Animator 的 Base Layer。它只更新
  `Speed`/`TurnRate` Locomotion BlendTree，不修改 `ItemInteraction` Layer。
- `Avatar.controller` 是生成结果；`GameSceneBuilder` 在新建和更新已有角色 Prefab 时
  都调用同一配置入口，防止后续场景重建退回一维 Idle/Walk。

### 2. 转身状态流程

1. `Face()` 或 `FaceAtSpeed()` 只更新目标 `facing` 和允许的最大角速度。
2. 角色水平速度低于 0.12 m/s 且没有位移请求时，`FixedUpdate()` 计算当前 Forward 到
   `facing` 的有符号水平角。
3. 剩余角达到 12°时进入转身，低于 4°时退出；角速度以 360°/s²加速、540°/s²减速。
4. 最大转速由调用者限制；标准动作上限仍为 240°/s，社交注视维持 48°/s。归一化
   Animator 参数以 120°/s 为参考并夹紧到 `[-1, 1]`。
5. 165°以上视为反向模糊区，保持首次选择的左右方向；离开该区域且反向误差达到 35°
   才允许换向。
6. 小于 12°且未进入步态时，以 30°/s做微转，保证 Dock 等精确对齐不会停在死区边缘。
7. 路径急转使用 70°进入、30°退出的滞回。角色先把线速度降为零并转身，再恢复
   NavMeshAgent 给出的移动速度。

`Mathf.Sign(0)` 在 Unity 中返回 1，不得用它单独判断“角速度是否仍有方向”。当前实现
先要求绝对角速度大于 0.5°/s，再做符号比较；否则进入微转分支。

### 3. Animator 契约

Base Layer 使用 `FreeformCartesian2D`：

- `(Speed=0, TurnRate=0)`：`Lengyue_Idle01_Combined`
- `(Speed=0.9, TurnRate=0)`：`Lengyue_Walk_BodyOnly`
- `(Speed=0, TurnRate=-1)`：左转步态，Walk Body-Only，速度 0.78
- `(Speed=0, TurnRate=1)`：右转步态，Walk Body-Only，速度 0.78、周期偏移 0.5

`Animator.applyRootMotion` 必须保持关闭。动画只表达步态，真实角度由 Rigidbody 完成，
所以目标角可以连续变化，也不需要为每个角度制作片段。后续替换专用左右转身 Clip 时，
保持四个 BlendTree 坐标和 `TurnRate` 参数不变即可。

### 4. 文件与验证

- 求解器：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/TurnInPlaceMotion.cs`
- 物理与 Animator 参数接入：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarMotor.cs`
- Animator 构建器：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/AvatarLocomotionBuilder.cs`
- 生成控制器：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Avatar.controller`
- 专项测试：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/TurnInPlaceTests.cs`

Unity 2022.3.38f1 专项 6/6、共享动作定向回归 7/7 通过。TRAE 受限 shell 直接启动
Unity 仍会触发 Bee semaphore error 15，真实编译和测试通过 macOS LaunchServices
启动。当前没有 PICO 真机脚步观感结论；专用转身 Clip、脚掌 Plant 曲线和 IK 锁定只能
在目标头显观察现有步态后按实际滑脚量决定，不能在运行时再增加第二个根旋转所有者。

## Phase Action Locomotion And Simplified Carry V002 技术交接：角色碰撞与持有物解耦（2026-10-17）

### 1. 生效边界

运行时必须区分角色本体和角色持有物：

1. 冷月本体遵守场景碰撞、执行局部避障，无法绕开的硬墙不可穿透。
2. 尚未拾取的物品保留自身物理状态，可以成为角色遇到的普通 Collider。
3. `HeldByCharacter` 物品关闭重力和碰撞，作为手部附属品跟随，不执行任何穿模准入。
4. 放下物品时恢复碰撞；结束预约或取消 Action 时恢复拾取前的 Rigidbody 配置。

不得把角色本体的硬墙测试扩展为持有物碰撞测试，也不得恢复“推动轻物体才算通过”的
断言。持有物允许穿过场景几何是降低运行时复杂度的明确设计选择。

### 2. Action 寻路

- `MoveAction.Start()` 提交一次完整路径并记录目标和历史最短剩余距离。
- 目标水平移动超过 0.1 m 时才重新调用 `TryPath()`；静态目标不做周期性全局重算。
- `AvatarMotor` 内的 `NavMeshAgent` 使用高质量局部避障，但
  `updatePosition/updateRotation/updateUpAxis` 全部关闭。Agent 只输出期望速度，
  `PhysicsBody`/Rigidbody 仍是位置和旋转唯一 owner。
- 未注册到 NavMesh 的普通 Collider 由 `OnCollisionStay()` 触发固定侧切向恢复。接触
  0.3 秒后激活，每次保持 1.25 秒；同一次移动不重新选择左右侧。
- 物理恢复累计 8 秒后判定无法绕行；其他无进展情况由 12 秒 Action 兜底处理。

### 3. 拾取与携带生命周期

1. `ItemTransferStep` 完成 FloorReach 或 TableReach，并只要求手部误差不超过 0.15 m。
2. `GameItem.Stabilize()` 取得交互所有权，`PhysicsBody.BeginInteraction()` 保存
   Kinematic、重力、碰撞、约束、检测模式和睡眠状态。
3. 物品立即转为 Kinematic、关闭重力和 `detectCollisions`，清空原线速度与角速度。
4. `ItemInteractionProfile.ItemFromGrip()` 将物品抓取点对齐
   `AvatarItemRig.HandGrip`，预约状态切为 `HeldByCharacter`。
5. 抬起、携带、放置和刀具动作只通过 `TryMoveInteraction(owner, pose)` 更新 Pose。
   该方法仅验证 owner、暂停状态和 Pose 数值，不检查空间占用。
6. 进入 Release 时先恢复碰撞，再切换为 `StagedForAction` 并记录工作位姿。
7. `EndInteraction()` 恢复原物理配置，但不会重放长时间预约前的旧动量。

以下持有期逻辑已明确删除，不得重新引入：

- 每厘米平移或每两度旋转的离散路径采样。
- `BoxCast`、Collider Sweep、`OverlapBox`。
- 用 `CanOccupy()` 或瞬时 `HasSupport()` 作为第二套 Action 准入。
- 让持有物参与冷月本体的阻塞计时或硬墙断言。

### 4. 组件职责

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CharacterActions.cs`：
  持久目标、进展计时和最终阻塞准入。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarMotor.cs`：
  Agent 期望速度、Rigidbody 驱动和普通 Collider 切向恢复。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/PhysicsBody.Interaction.cs`：
  交互物理快照、无物理 Pose 跟随和生命周期恢复。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemTransferStep.cs`：
  15 cm 近手吸附、持有状态切换、携带和释放。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CutFruitAction.cs`：
  刀具使用同一无物理持有约定，不维护独立碰撞规则。

### 5. 验证与待确认项

- `CharacterLocallyAvoidsUnregisteredPhysicsCollider`：通过。
- `CharacterCannotWalkThroughAnUnmappedWall`：通过，仅验证角色本体。
- `NearHandPickupSnapsAndDisablesPhysicsUntilRelease`：通过，覆盖 8 cm 近失配吸附、
  Kinematic、重力关闭、碰撞关闭和取消恢复。
- `RealCharacterCollectsCutsAndServesWithoutDuplicatingInventory`：通过。
- 相关回归结果：
  `/Users/bytedance/MateSoul/.artifacts/cut-fruit-action/simplified-runtime-regression.xml`，
  27/27 通过。
- Android Development ARM64/IL2CPP APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，SHA-256
  `aa6880a99d6f26cbec4187493c6d3c0e7794a31083efc8b4aabd1872742f63b5`，
  ZIP 完整性和 APK Signature Scheme v2 验证通过。

PICO 真机确认前，两份调试记录和网络插桩保持 `[OPEN]`。设备验收只检查角色绕障、
近手吸附、持有物稳定跟手和释放恢复，不检查持有物是否穿过墙体或其他场景 Collider。

## Phase Cooking Spontaneous Reply V002 技术交接：事件驱动 AI 回复隔离层（2026-10-17）

### 1. 阶段概述

本阶段完成两项独立改进：

1. 加热底座使用标准电源图标和低亮暗红显示屏，温度数字保持暖橙高对比度。
2. 牛排在熟度达到 50 时切换完整熟牛排模型，并通过独立 AI 路径生成冷月的环境事件
   回复。

“系统性自发 AI 回复”是可复用基础能力，不属于玩家对话。任何后续环境感知、物品状态、
时间或玩法事件，只要需要冷月主动说一句话，都应接入该能力，不能伪造成玩家消息。

### 2. 运行时职责

- `SpontaneousReplyCoordinator`：
  维护事件队列、`trigger_id` 去重、串行请求、短时字幕、情绪结果和生命周期取消。
- `ISpontaneousReplyProvider`：
  系统回复的独立传输接口，不复用 `ILLMProvider.CompleteAsync()` 的会话请求模型。
- `HttpLLMProvider.CompleteSpontaneousAsync()`：
  从普通对话 Gateway URI 派生 `/v1/system-replies/generate` 并发送严格请求。
- `CookingSpontaneousReplyTrigger`：
  烹饪领域适配器，只监听 `CookableFood.DonenessChanged`，首次达到
  `FoodDoneness.Medium` 后向通用协调器提交结构化事件。
- `GameHud`：
  在系统回复可见期内优先展示 `SpontaneousReplies.VisibleText`。
- `ResponseEmotionAudio`：
  同时监听普通会话和系统回复的递增序号，每条成功回复只播放一次对应情绪片段。

`GameSession` 拥有 `SpontaneousReplyCoordinator`，负责 Tick 与 Dispose。系统回复不进入
`SessionConversationContext`，因此不会污染玩家对话历史，也不会成为后续普通对话的记忆。

### 3. Gateway 协议与隔离不变量

端点：

- `POST /v1/system-replies/generate`

请求字段：

- `request_id`
- `trigger_id`
- `event_description`
- `content_requirements[]`
- `rules[]`
- `persona`

响应字段：

- `status: "ok"`
- `reply_text`
- `response_emotion: normal | happy | sad | angry`

必须长期保持以下不变量：

1. 请求没有 `history`、`session_id`、`runtime`、`actions`、`targets` 或 `moods`。
2. `SpontaneousReplyService` 调用模型时 tools 数组恒为空，不连接 MCP。
3. Gateway 只组合角色 `systemPrompt`、当前人格快照、事件、内容要求和规则。
4. 模型只能返回文本和情绪，不能返回动作或心情增量。
5. 无效 JSON 最多执行一次无工具 repair，仍失败则向 Unity 返回 502。
6. `trigger_id` 在当前 `GameSession` 内去重，领域适配器应把物品实例或事件实例 ID
   编入 trigger，避免同一事件重复说话。

这些约束由
`/Users/bytedance/MateSoul/services/ai-gateway/tests/spontaneous-reply-service.test.ts`
验证。后续修改普通对话服务时，不得顺带把历史、Web 工具或 Action 能力引入此端点。

### 4. 牛排事件配置

`GameSceneBuilder.BuildRawSteak()` 在生产 Prefab 中写入：

- 生牛排完整模型根节点 `Model`。
- 熟牛排完整模型根节点 `Cooked Model`，初始 inactive。
- 两个根节点完全相同的局部位置、局部旋转和局部缩放。
- `CookingSpontaneousReplyTrigger` 的事件描述、内容要求和规则。

熟度阶段达到或跨过 50 后，`CookableFood` 关闭生模型、开启熟模型。最终台词不存放在
Prefab；Prefab 只要求“闻到很香的气味、询问玩家是否在煎牛排、只说一句自然中文”，
并限制模型不得断言、不得提及 AI、系统提示、熟度数值、工具或内部机制。

恢复存档时 `CookableFood.Configure()` 使用 `publish=false` 应用已有阶段，因此加载一块
已经熟制的牛排不会再次触发主动回复。

### 5. 后续事件接入流程

1. 在事件所有者或窄领域适配器中确定一次性触发边界。
2. 生成稳定且带实例区分的 `trigger_id`。
3. 提供事实性的 `event_description`，不得在其中伪造玩家记忆。
4. 把必须表达的语义拆为 `content_requirements`，把禁止项和长度要求写入 `rules`。
5. 调用 `GameSession.SpontaneousReplies.Enqueue()`。
6. 增加 Provider 捕获测试，断言人格、事件、要求与规则正确，且重复 trigger 被拒绝。
7. 增加 Gateway 测试，断言 messages 不含历史且 tools 恒为空。

除非业务明确要把事件变成共同对话经历，否则不得调用
`ConversationOrchestrator.SendAsync()`，也不得手工写入 `SessionConversationContext`。

### 6. 视觉、音频与资源

- 电源图标：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/HeatingBasePowerIcon.png`
- 加热底座 Prefab：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/HeatingBase.prefab`
- 牛排 Prefab：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/RawSteak.prefab`
- 通用协调器：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Gameplay/SpontaneousReplies.cs`
- Gateway 服务：
  `/Users/bytedance/MateSoul/services/ai-gateway/src/spontaneous-reply-service.ts`

当前项目没有任意文本 TTS。`reply_text` 只作为字幕显示，角色发声仍是
`ResponseEmotionAudio` 的预录情绪片段。未来接入 TTS 时应监听协调器的成功回复，不得
另建一条带历史的 AI 请求路径。

### 7. 构建与验证

- AI Gateway：17/17 通过。
- Unity 烹饪与系统回复定向测试：5/5 通过。
- Android Development ARM64/IL2CPP 构建成功。
- APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `5f64e2b7484b79f5bd158ce3d339226a096bb32fe05a3ffff0eea6293a189518`

Release 构建继续要求 HTTPS Gateway。本机 HTTP 仅允许 Development APK；不得为方便
测试而降低正式包的 Gateway 校验规则。

## Phase Ergonomic Grip Follow V003 技术交接：最终手骨驱动与关节约束（2026-10-17）

### 1. 所有权不变量

角色持有物必须遵守以下单向数据流：

1. Action 只更新手部目标。
2. Animator 先应用身体动作。
3. `AvatarItemRig.LateUpdate()` 求解肩、肘、前臂和手腕。
4. 同一个 `LateUpdate()` 用最终 `HandGrip` 和固定附件 Pose 更新物品。
5. 物品不得反向成为手部追逐目标，也不得维护独立的跟随插值器。

吸附后任何一帧都只能有一个持有物 Pose owner。禁止恢复
`ItemTransferStep -> Rigidbody -> AvatarItemRig` 的循环依赖。

### 2. 人体工学 IK

`AvatarItemRig` 的位置与旋转职责已经分离：

- 普通 Reach、Lift、Carry 和 Place 只求解抓取点位置，保留动画手腕朝向。
- 肘关节通过两段臂长和余弦定理限制在 35°–175°。
- 明确需要朝向的刀具动作先让前臂承担最多 75°旋前/旋后，再允许手腕承担最多 55°
  剩余修正。
- 目标超出可达范围时保持关节边界，不以手腕 180°翻转补偿位置或朝向。
- `ContactError` 只表示抓取点位置误差；不得把物品 Euler 完全匹配重新作为普通拾取
  准入。

55°上限只用于切刀等有明确姿态要求的动作。普通搬运的 `matchRotation=false`，腕部
主动修正应为 0。

### 3. 吸附与跟手流程

1. 冷月走到物品旁，播放 FloorReach 或 TableReach。
2. IK 连续两个求解帧后检查距离；上限 25 cm 只用于拒绝真正未靠近的物品。
3. 保持物品世界旋转，仅把 authored grip position 平移到实际 `HandGrip.position`。
4. `AttachHeld()` 保存 `itemFromGrip` 相对 Pose，并立即进行一次同步。
5. Lift/Carry 平滑移动的是 HandGrip 目标；持有物不执行 `MoveTowards`。
6. 最终 IK 后 `SyncHeldBody()` 计算 `HandGrip * itemFromGrip`，通过
   `PhysicsBody.TryMoveInteraction()` 同步 Rigidbody 和 Transform。
7. Release 时解除附件、对齐 authored 工作位、恢复碰撞并写入稳定检查点。

`PhysicsBody.BeginInteraction()` 会保存原 `RigidbodyInterpolation` 并设置为 `None`；
`EndInteraction()` 恢复原插值、重力、碰撞、约束和检测模式。

### 4. 取消与事务回滚

`GameItem.Stabilize()` 注册的租约释放回调必须先把物品移回 `stablePose`，再调用
`EndInteraction()`。`stablePose` 的含义是最近一次已提交检查点：

- 尚未放置成功时是 Action 开始前位置。
- 已放到桌面后由 `CheckpointInteraction()` 更新为桌面位置。
- 取消时不会从手部、角色胶囊或地板内部恢复动态物理。
- 成功结束时回滚目标等于最终检查点，因此不会产生额外移动。

不得直接恢复拾取前速度；长时间持有后的旧动量仍应清零。

### 5. 切刀适配

- 刀具搬运不再使用额外 180° Carry 翻转。
- 进入切割前先完整混合 Cut 身体姿势，再抬刀离开桌面。
- 每一刀在下降前根据实际刀刃中点执行有界水平校正。
- 动作到位容差是 1 cm / 8°，但提交仍要求实际刀刃从苹果上方穿过 4.5 cm 高度门槛，
  且水平中心误差小于 2 cm。
- 两次垂直切割均成功后才允许原子替换完整苹果。

### 6. 文件与验证

- 人体工学 IK 与附件所有权：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarItemRig.cs`
- 搬运状态机：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemTransferStep.cs`
- 交互物理快照：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/PhysicsBody.Interaction.cs`
- 取消回滚：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameItem.cs`
- 切刀轨迹：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CutFruitAction.cs`
- 回归测试：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/ItemActionRuntimeTests.cs`
- 结果：
  `/Users/bytedance/MateSoul/.artifacts/cut-fruit-action/wrist-grip-regression-3.xml`，
  27/27 通过。
- Android Development ARM64/IL2CPP APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，`139,644,816` 字节，
  SHA-256 `8cbc1fcdd288c66c632275c5928ffa330e9fb04d0095277eb2f77333a56a8eb1`；
  ZIP 完整性和 APK Signature Scheme v2 验证通过。

真机确认前，`debug-wrist-grip-follow.md` 保持 `[OPEN]`。验收重点是拾取瞬间腕姿、
Lift/Carry 全程相对 Pose、刀具旋转和取消后物品回位。

## Phase Turn In Place V002 技术交接：相位化脚锁与紧凑转身 Clip（2026-10-17）

### 1. 运行链路

当前转身数据流为：

1. Action 只通过 `AvatarMotor.Face()/FaceAtSpeed()` 提交目标朝向。
2. `TurnInPlaceMotion` 计算带加减速、死区和 180°方向锁的本帧角位移。
3. `PhysicsBody.RotateTowards()` 写入唯一 Rigidbody 根旋转。
4. Animator 使用 `Speed × TurnRate` 二维 BlendTree 选择 Idle、Walk 或左右专用
   Turn Body-Only Clip。
5. `AvatarTurnFootPlantRig.LateUpdate()` 读取 Clip 写入的 Plant 权重，只修正当前支撑腿。
6. 坐卧、物品 IK、裙摆和头发按既有执行顺序继续处理。

业务层、Animator Clip 和脚锁组件都不得直接旋转角色根 Transform。

### 2. 专用动画生成

`AvatarLocomotionBuilder` 从生产 Idle 与 Walk 确定性生成：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Locomotion/Lengyue_TurnLeft_BodyOnly.anim`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Locomotion/Lengyue_TurnRight_BodyOnly.anim`

生成规则：

- 手臂/前臂保留 22% Walk 动幅，避免原地转身仍大幅摆臂。
- 髋、脊柱和胸部保留 34%，并增加 1.5°–4.5°方向性预转。
- 大腿保留 56%、膝盖 68%、脚 52%，摆动腿增加最多约 9°腿部和 7°脚部朝向。
- Hips 位移只保留 32%，减少“向前走但根不前进”的视觉矛盾。
- Clip 周期保持 1.0667 秒并开启 Loop Blend，不包含 Root Motion、头发或裙摆曲线。
- `leftPlantWeight` 在前半周期为主，`rightPlantWeight` 在后半周期为主，0.40–0.48
  和 0.92–1.00 为换脚过渡区。

生产 Controller 的 `(0,-1)` 与 `(0,1)` 节点必须引用上述专用 Clip，不得重新指回
`Lengyue_Walk_BodyOnly`。

### 3. 支撑脚 IK

`AvatarTurnFootPlantRig` 使用项目已有的 Generic 两骨求解器，不注册新的
Animation Rigging Layer：

- Plant 权重首次达到 0.12 时缓存脚骨世界位置和旋转。
- 权重降到 0.04 以下时释放该脚。
- 每帧从 Animator 姿态求解到缓存脚位，再按 Plant 权重混合上腿、膝和脚旋转。
- 两脚同时承担权重时，`AvatarMotor` 将根角位移缩放到最低 55%，降低换脚瞬间滑移。
- 组件执行顺序为 10900，早于 `AvatarSeatingRig` 11000、
  `AvatarLyingRig` 11010 和 `AvatarItemRig` 11100；后续全身动作可覆盖腿部修正。

曾尝试向现有 `RigBuilder` 插入双腿 `TwoBoneIKConstraint`。即使约束权重为 0，新 Layer
仍改变 Animator 图并使物品接触动作不稳定，因此已彻底移除。不要恢复该方案。

### 4. 角速度与准入

- 原地转身最大角速度：135°/s。
- 转身动画启动水平速度：0.12 m/s。
- 进入/退出角度：12°/4°。
- 路径急转滞回：70°进入、30°退出。
- 180°方向锁区：165°以上。
- 小角度微转：30°/s。

120°/s 虽能把右/左支撑脚滑移进一步降至约 2.0/14.6 mm，但动态物理障碍绕行在
12 秒回归窗口内未完成；135°/s 是当前移动时限与视觉稳定性的已验证折中。

### 5. 文件与证据

- 角度求解：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/TurnInPlaceMotion.cs`
- 根旋转与速度准入：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarMotor.cs`
- 支撑脚求解：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarTurnFootPlantRig.cs`
- 内容生成：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/AvatarLocomotionBuilder.cs`
- Prefab 绑定：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/GameSceneBuilder.cs`
- 回归测试：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/TurnInPlaceTests.cs`

最终专项 9/9、共享动作 6/6、切水果独立 1/1 通过。135°/s 下右转 73°最大支撑脚滑移
约 4.47 mm，左转 -112°约 17.43 mm。测试结果位于
`/Users/bytedance/MateSoul/.artifacts/turn-in-place-v2/`。Android Development 构建、
APK ZIP 完整性和 v2 签名均通过。PICO 真机仍需检查裙摆遮挡下的脚掌观感与连续反向转身。

## 2026-10-17：MoltenVK argument-buffer 回滚

### 1. 故障证据

PICO Emulator本轮启动MateSoul后，宿主日志连续出现：

```text
VK_ERROR_INVALID_SHADER_NV: Vertex shader requires view range buffer,
but there is no free slot to pass it.
Shader library compile failed
cannot reserve 'buffer' resource location at index 11
```

随后QEMU main loop与CPU0–5线程均多次超过15秒无响应。用户观察到此时模拟器
达到约40GB；进程在检查前已退出，因此没有本轮vmmap分项。

### 2. 根因与修复

此前诊断阶段在启动器中强制
`MVK_CONFIG_USE_METAL_ARGUMENT_BUFFERS=0`。该配置虽然来自通用gfxstream兼容思路，
但不适用于当前PICO Emulator 6.2捆绑的ANGLE/MoltenVK shader资源布局，会与
multiview的`spvViewMask` buffer槽发生冲突，并导致pipeline反复编译失败。

修复规则：

1. `start-pico-emulator.command` 启动前
   `unset MVK_CONFIG_USE_METAL_ARGUMENT_BUFFERS`。
2. `diagnose-pico-memory.command` 删除所有0/1覆盖，只记录
   `metalArgumentBuffers=driver-default`。
3. 不修改Vulkan、Metal heap、ANGLE、AVD RAM或游戏画质。

### 3. 边界

此修复只处理“启动后快速40GB并QEMU挂死”。虚拟物品交互导致的宿主
`owned unmapped (graphics)`增长仍需在driver-default下重新采样。不得再把
argument-buffer开关作为内存优化手段，也不得将本次静态回滚直接宣称为全部内存问题
已经解决。

## Phase Turn In Place V003 技术交接：阈值迈步与 Social Attention

### 1. 转身状态机

`TurnInPlaceMotion.EnterAngle` 为 18°，`ExitAngle` 为 3°。未达到进入阈值时只输出
30°/s 的小角度根旋转，`NormalizedRate` 保持 0，因此 Animator 不会播放脚步。达到
阈值后，`AvatarMotor.UpdateTurnStepAnimation()` 根据方向触发一次
`TurnStepLeft` 或 `TurnStepRight`；Animator 返回基础状态后，若偏差仍大于阈值才会
再次触发。

Controller 基础层包含四个状态：

1. `Locomotion`：仅含 Idle/Walk 的 1D BlendTree。
2. `SocialAttention`：播放专用 Body-Only Idle。
3. `TurnStepLeft`：非循环左迈步。
4. `TurnStepRight`：非循环右迈步。

两个 TurnStep 均可从 Locomotion 或 SocialAttention 进入，并按
`SocialAttention` bool 返回正确基础状态。不要恢复将左右转身作为连续 2D BlendTree
采样点的 V002 方案，否则小角度持续混合会再次造成“脚动不明显但一直滑”的观感。

### 2. 支撑脚约束

`AvatarTurnFootPlantRig.ApplyFoot()` 仍在 Plant 权重 0.12 时捕获世界锚点、0.04 时
释放，但实际 IK 权重通过 `SmoothStep(InverseLerp(0.12, 0.65, weight))` 重映射。
因此抬脚和落脚保留渐变，权重进入 0.65 的稳定支撑区后脚掌完全锁定。根旋转仍只由
`PhysicsBody` 所有；Foot Plant 只能修改腿骨，禁止写角色 Transform。

### 3. 社交注视流程与仲裁

1. `GameSession.UpdateSocialAttention()` 只提交 `IntentSource.Ambient` Intent。
2. `SocialAttentionAction` 占用 `ControlChannels.All`，启动时停止移动、锁定当前朝向、
   设置 Animator `SocialAttention=true`，然后启动头眼 Rig。
3. `AvatarSocialAttentionRig` 在 LateUpdate 中让 Neck、`Head 1`、Eye_L、Eye_R
   跟随玩家，并记录实际 `CurrentHeadYaw`。
4. 只有 `CurrentHeadYaw` 达到 32°，Action 才调用 `AvatarMotor.FaceAtSpeed()`；
   身体偏差降到 12° 后 `HoldFacing()`，形成头先身后的滞回。
5. Autonomous、AI、UI、Player、System 优先级均高于 Ambient，且任一通道都会与
   Social Attention 的 All 声明冲突，因此正式 Action 会抢占并关闭专用 Idle。
   动作结束且玩家仍在范围内时，GameSession 才重新提交 Ambient Intent。

未来生气、开心、难过等特殊动画必须注册为 Action 并至少占用 Body 通道；不要在
Animator 外部直接播放，否则无法参与统一仲裁。

### 4. 动画导入与资源

- 原始 FBX：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/SocialAttention/idle_03_InPlace.fbx`
- 清洗后动画：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/SocialAttention/Lengyue_SocialAttention_Idle_BodyOnly.anim`
- 导入器：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Editor/LengyueIdle01Builder.cs`
- Controller 生成器：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/AvatarLocomotionBuilder.cs`
- 运行时：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarMotor.cs`、
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarSocialAttention.cs`
- 测试：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/TurnInPlaceTests.cs`

`LengyueIdle01Builder.ImportSocialAttention()` 使用 Generic、无 Avatar、无材质导入，
将源 `Head` 重定向到生产 `Head 1`，并过滤 Hair、Skirt、IK、CTRL、DRV、MCH。
本次导入映射 69 根骨骼且无缺失。新增社交动画必须继续走该清洗流程，不能把含 400
根控制、裙摆和旧发骨的源 FBX 直接挂进生产 Controller。

### 5. 构建与验证边界

专项结果为 Action 仲裁 19/19、转身 11/11、社交 1/1。证据目录：
`/Users/bytedance/MateSoul/.artifacts/turn-in-place-v3/`。Development APK 位于
`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`。当前网关为本地 HTTP，
Release 构建会按设计失败；只有 HTTPS 网关可生成 Release APK。

## Phase Cooking Effects V003 技术交接：受热音频与白烟生命周期（2026-10-17）

### 1. 所有权与触发边界

烹饪表现必须服从现有传热判定，不能根据物品名称或普通碰撞事件独立启动：

1. `HeatingBaseController` 判断电源、锅底接触面积和暂停状态，拥有按键滴声与电磁炉
   运转循环声。
2. `CookwareHeatReceiver` 继续按 0.2 秒执行 `OverlapBoxNonAlloc`，维护最多 24 个
   当前实际受热的 `CookableFood`。
3. `CookableFood.SetHeating()` 只接收锅具给出的受热事实，不自行做空间查询。
4. `CookingEffects` 属于单个食材实例，拥有滋滋 AudioSource、白烟 ParticleSystem
   及其启停生命周期。

成立条件为：

`底座通电 && 锅底接触比例达标 && 食材带可烹饪标记 && 食材位于锅内`

只有全部成立时，食材才增加熟度、播放滋滋声并冒白烟。任一条件失效后，锅具在下一次
0.2 秒扫描内调用 `SetHeating(false)`；底座断电、锅具禁用和暂停会立即清理全部已跟踪
食材效果。

### 2. 底座音频

`HeatingBaseController` 使用两个独立 3D AudioSource：

- `buttonAudio`：非循环，任何通过 `CanUse()` 的底座按钮点击播放一次滴声。
- `operatingAudio`：循环，仅当 `IsOperating` 为真时播放。`IsOperating` 要求已通电、
  已找到有效 `CookwareHeatReceiver` 且底座未暂停。

运转声不会在仅通电但无锅时播放。温度从 40℃ 到 100℃时，运转声 Pitch 从 0.94
平滑映射到 1.06。两个音源均为全 3D 空间声，关闭 Doppler，最大听距为 4–5 m。

### 3. 食材音频与白烟

`CookingEffects` 在运行时创建：

- 一个循环 3D `AudioSource`，音量随温度从 0.34 增至 0.62，Pitch 从 0.92 增至 1.08；
- 一个名为 `Cooking Steam` 的世界空间粒子系统；
- Box 发射区域按食材 Collider 的 X/Z 包围尺寸设置；
- 5–13 粒子/秒、最多 36 粒子，寿命 1.4–2.3 秒；
- 向上速度、低强度 Noise、逐渐放大与 Alpha 淡入淡出。

停止受热时，粒子使用 `StopEmitting` 保留已生成烟雾的自然消散；禁用物体时使用
`StopEmittingAndClear` 立即清理。音频目标音量降至零后停止播放，避免频繁进出锅边界
产生硬切。

`GameItem.Bind()` 为所有可烹饪食材配置 `CookableFood`；`CookableFood.Configure()`
会确保存在 `CookingEffects`。因此新增食材不需要修改底座或锅具，只需正确设置
`CookingSettings.enabled=true`。

### 4. 确定性资源生成

`CookingEffectsAssetBuilder` 生成以下项目自有资源：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Resources/CookingEffects/ButtonBeep.wav`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Resources/CookingEffects/InductionHum.wav`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Resources/CookingEffects/FoodSizzle.wav`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Resources/CookingEffects/SmokeSoft.png`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Resources/CookingEffects/Smoke.mat`

WAV 为 44.1 kHz、16-bit、单声道 PCM：

- 滴声：0.13 秒双频衰减电子音。
- 电磁运转声：4 秒周期谐波低频循环。
- 滋滋声：6 秒确定性高通噪声与随机爆裂包络。

Builder 仅在字节内容变化时重写文件。AudioImporter 使用
`DecompressOnLoad + PCM + PreserveSampleRate`；`preloadAudioData` 必须设置在
`AudioImporterSampleSettings`，Unity 2022.3 已禁止直接写旧的
`AudioImporter.preloadAudioData`。

烟雾材质使用 `Universal Render Pipeline/Particles/Unlit` 和透明 Surface。软粒子贴图
包含有效 Alpha，不依赖外部版权素材。

### 5. Unity 粒子与 UI 避坑

- `gameObject.AddComponent<ParticleSystem>()` 会按默认 `playOnAwake` 立即进入播放态。
  设置 `main.duration` 前必须先调用 `StopEmittingAndClear`，否则 Unity Test Runner
  会抛出“system is still playing”断言。
- World Space Canvas 的 UI 正面是 `-RectTransform.forward`。加热底座显示器位于模型
  哪一侧，就必须让该方向与 `-forward` 同向；此前按 `forward` 判断导致数字从背面看而
  镜像。测试通过显示面中心到物体中心的外向向量验证 Dot > 0.99。
- 烟雾只使用低频受热集合更新，不得为视觉效果新增 `Update()` 中的 Collider 查询。

### 6. 文件与验证

- 食材效果：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CookingEffects.cs`
- 锅内受热集合：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CookwareHeatReceiver.cs`
- 底座音频：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/HeatingBaseController.cs`
- 资源生成：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/CookingEffectsAssetBuilder.cs`
- 定向测试：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/CookingSystemTests.cs`

Unity 定向测试 4/4 通过；Android Development ARM64/IL2CPP 构建、APK ZIP 完整性和
APK Signature Scheme v2 验证通过。APK SHA-256：
`4f10135caf64e25582a7146fab752487384e15f45b20ec8622099a3786a86dc5`。

## Phase Turn In Place V004 技术交接：TurnStep 频率与幅度闭环

### 1. 运行时准入

`TurnInPlaceMotion` 当前参数：

- `EnterAngle = 32°`：达到该偏差才允许播放调整步。
- `ExitAngle = 7°`：进入调整后收敛到该角度再退出。
- `MicroTurnAngularSpeed = 18°/s`：32°以内只做缓慢根旋转。
- `ReverseAngle = 42°`：目标反向抖动不会轻易切换迈步方向。

社交注视在 `CurrentHeadYaw >= 40°` 后才启动身体跟随，身体偏差降到 14° 后停止。
因此 26° 等轻微朝向调整不会触发脚步，头部可先承担更大的注视范围。

### 2. Trigger 生命周期

`AvatarMotor.UpdateTurnStepAnimation()` 使用三个状态：

1. `turnStepRequested=true`：Trigger 已发送，等待 Animator 消费。
2. `turnStepWasObserved=true`：当前或 Next State 已实际进入 TurnStep。
3. TurnStep 退出：清除请求并启动 `TurnStepRecoverySeconds=0.6s`。

在 Trigger 已发送但 Animator 尚未进入状态时，不得清除请求或再次 `SetTrigger()`。
这是防止同一渲染帧多个 FixedUpdate 留下重复 Trigger 的关键不变量。恢复窗口结束前，
即使剩余角度仍大于阈值也不触发下一步。

### 3. 单步动画生成

`AvatarLocomotionBuilder.BuildTurnClip()` 不再直接保留 Walk 的完整位移：

1. 每帧使用 `sin(phase * PI)` 作为 0→1→0 包络。
2. Walk 骨骼旋转按包络和骨骼比例混合到 Idle。
3. 除 Hips 外，参与转身的骨骼位置曲线固定为 Idle 位置。
4. Hips 只保留 18% 的 Walk 位移并乘同一包络。
5. TurnYaw 同样乘包络，确保 Clip 首尾严格回到 Idle。

当前旋转比例为：手臂 15%、躯干 25%、腿 45%、膝 55%、脚 50%。第一阶段左脚摆动、
右脚 Plant；第二阶段右脚摆动、左脚 Plant。不得恢复 V003 中与摆动脚同侧 Plant 的
反向相位。

### 4. 验证不变量

`TurnInPlaceTests` 同时验证：

- 小角度不进入 TurnStep；
- 慢速大角度的步数上限和最小间隔；
- Clip 在静止根节点下的脚轨迹为 2.5–16 cm；
- 运行时根水平漂移小于 2 cm；
- 支撑脚世界滑移小于 2.5 cm；
- 左右动作最终收敛且无过冲。

当前实测：左/右 Clip 脚轨迹 10.8/14.7 cm，右/左运行时支撑脚滑移
9.8/13.2 mm。测试报告：
`/Users/bytedance/MateSoul/.artifacts/turn-in-place-v4/turn-tests.xml`。

### 5. 关键文件

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/TurnInPlaceMotion.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarMotor.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarSocialAttention.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/AvatarLocomotionBuilder.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/TurnInPlaceTests.cs`

## Phase Classified Grip V001 技术交接：物品模板主导抓握（2026-10-17）

### 1. 架构边界

抓握姿态由物品模板决定，角色手部不再强行适配物品当前世界朝向：

1. `ItemInteractionProfile` 描述物品局部抓握契约。
2. `ItemTransferStep` 只提交主手/副手目标和动作阶段。
3. `AvatarItemRig` 独占手臂、手腕、手指解算以及持有物跟随。
4. `PhysicsBody` 独占 Rigidbody 状态切换和最终位姿写入。
5. Action、UI 和 AI 不得直接写 Animator、手骨或持有物 Transform。

Profile 分类：

- `Generic`：苹果、玩偶、碗等掌托物。
- `Handle`：刀、勺、锅铲、平底锅等细长手柄。
- `LoopHandle`：杯子、收音机等环形把手。
- `ThinEdge`：盘子、书卡、遥控器、薄食材等侧捏物。
- `LargeTwoHand`：电视、桌子、椅子、饮水机、加热底座。

新增物品必须通过 `ItemActionContentBuilder.ConfigureCatalog()` 生成或更新 Profile，
不得在协调器中新增物品名称分支。

### 2. Profile 数据与生成

`ItemInteractionProfile` 的核心字段：

- `gripPosition/gripEuler`：主抓握锚点。
- `secondaryGripPosition/secondaryGripEuler`：双手物品副锚点。
- `gripSize`：握持区域三轴尺寸。
- `fingerClearance/fingerCurl`：指尖安全间隙与目标弯曲度。
- `handPose`：`Support`、`Power`、`Hook`、`TwoHandSupport` 或 `Pinch`。

Builder 优先读取名称包含 `Handle` 或 `Grip` 的 Renderer Bounds；无命名握持部位时，
按整体 Bounds 和分类规则生成稳定锚点。双手物品在同一侧边生成左右对称锚点；
薄物选择最长边端点；普通物选择上表面中心。所有长度单位为米。

主资源路径：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemInteractionProfile.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/ItemActionContentBuilder.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/ItemActions/`

### 3. 吸附、跟随与人体工学

拾取流程：

1. 角色走到物品附近并按 Profile 的世界抓握点伸手。
2. 手部进入 25 cm 吸附范围后，物品通过 `ItemFromGrip()` 或 `ItemFromHands()`
   直接对齐标准手掌位姿，忽略原摆放旋转。
3. `GameItem.Stabilize()` 取得交互所有权，关闭重力、碰撞和 Rigidbody 插值。
4. `AvatarItemRig.AttachHeld()` 保存手到物品关系。
5. 每帧 `LateUpdate` 先完成臂部与手指求解，再由 `SyncHeldBody()` 唯一写入物品位姿。
6. 放置或取消时由拥有者恢复原始物理状态。

手指使用五条三关节骨链。`Handle/LoopHandle` 按半径求包握目标，`ThinEdge` 将拇指和
食指放到薄片法线两侧，双手物品对左右链镜像求解。`AimFinger()` 使用局部基准旋转和
固定次数 CCD，不在逐帧路径创建 LINQ 临时数组。

人体工学硬限制保持为：肘角 35°–175°、前臂滚转最大 75°、手腕修正最大 55°。
普通搬运只匹配位置；刀具工作姿态才使用受限旋转修正。

### 4. 双手与切割特殊流程

`ItemTransferStep` 对 `RequiresTwoHands` 的 Profile 在所有运动阶段调用
`ReachSecondary()`，吸附时使用左右实际手位姿计算 `ItemFromHands()`。进入持有状态后，
物品由两只最终解算手共同确定，不能退回单手插值或物理 Sweep。

切水果仍使用同一抓握系统，但刀刃交叉是独立业务不变量：

- 移动准入比较实际握点，不比较偏离手柄约 10 cm 的刀具根节点。
- 下刀时对实际刀刃中点做水平和垂直闭环修正。
- 水平误差大于 15 mm 时暂停下降；有效交叉半径仍为 20 mm。
- 第二刀不原地硬扭手腕，而是通过 `MoveAction` 绕苹果移动到正交工作位。

### 5. 构建与验证

- Profile：`ItemGripProfileTests` 3/3。
- 动作：`ItemActionRuntimeTests` 4/4。
- 共享物品、Action、导航：27/27。
- 完整切割：两刀 `crossed=true`，交叉水平误差 4.71/4.21 mm。

Development 构建必须带 `BuildOptions.CleanBuildCache`。Unity 2022.3 在测试、内容重建
和 Android 增量构建连续执行后，Bee 可能保留已删除的
`Temp/StagingArea/Data/Managed/MethodsToPreserve.xml` 依赖；仅重跑相同增量图会再次在
UnityLinker 失败。干净缓存构建可消除该陈旧依赖。

当前 APK：
`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，SHA-256
`5aed1df3893dac413010996af71fd226bed3d7b00e989edad39e8ec4c111f1f0`。
仅包含 `arm64-v8a`，使用 APK Signature Scheme v2 调试签名。

## Phase Turn In Place V005 技术交接：根旋转与接触相位同步

### 1. 近身社交边界

`GameSession` 使用水平距离判定：

- `SocialAttentionEnterDistance = 0.8f`
- `SocialAttentionExitDistance = 1.05f`

进入和退出保留 25 cm 滞回。对话请求仍可绕过距离主动申请 Social Attention；距离只
控制普通近身触发。测试固定验证 1.0 m 不进入、0.72 m 进入、0.95 m 保持、1.15 m 退出。

### 2. 动画设计基线

本阶段参考 Epic Lyra Turn-in-Place 的 Root Yaw 补偿思路和 Pose/Stride Warping 的
有限变形原则：角色根旋转需要与动画接触相位匹配，不能依赖过高播放速率或过度拉伸
姿态掩盖差异。

生成流程：

1. `stepEnvelope = sin²(phase * PI)`，首尾位置和角速度均平滑回到 Idle。
2. 右转使用原 Walk 相位，左转偏移半周期，交换先摆动腿。
3. 右转首段右脚支撑、左脚摆动；左转首段左脚支撑、右脚摆动。
4. 0.40–0.68 周期为双支撑交接区，降低重心切换的突兀感。
5. Animator 在 0.14 s 内进入 TurnStep，播放到 96% 后以 0.16 s 退出。

### 3. 根旋转同步

`AvatarMotor` 先读取 Current/Next Animator State，再决定是否应用
`TurnMotionSample.DeltaAngle`：

- 尚未进入 TurnStep：大角度根旋转为 0，等待腿部动画。
- TurnStep 播放中：按最高 78°/s 推进 Rigidbody 根旋转。
- 完成一步且剩余偏差小于 48°：不重复迈步，以 28°/s 收尾。
- 完成一步且剩余偏差仍大于等于 48°：经过 0.6 s 恢复窗口后允许下一步。
- 从未迈步且偏差小于 32°：继续使用 18°/s 轻微根旋转。

`TurnRate` 只在 TurnStep 实际播放时写入非零值，因此
`AvatarTurnFootPlantRig` 不会在等待或恢复阶段错误捕获脚锚点。

### 4. 验证不变量

- 左右 Plant 曲线首段支撑脚必须与方向匹配。
- Clip 结束时左右脚位置误差小于 2 mm、旋转误差小于 1°。
- Clip 静态脚轨迹保持在 2.5–16 cm。
- 运行时支撑脚滑移小于 25 mm、根水平漂移小于 20 mm。
- 26°偏差不得进入 TurnStep；慢速 120°转向步数不得超过两步且间隔大于 1.2 s。
- 生产移动与 NavMesh 停止行为必须独立通过。

当前测试报告：

- `/Users/bytedance/MateSoul/.artifacts/turn-in-place-v5/turn-tests.xml`
- `/Users/bytedance/MateSoul/.artifacts/turn-in-place-v5/social-attention-test.xml`
- `/Users/bytedance/MateSoul/.artifacts/turn-in-place-v5/movement-test.xml`

### 5. 关键文件

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameSession.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarMotor.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/AvatarLocomotionBuilder.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarTurnFootPlantRig.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/TurnInPlaceTests.cs`

## Phase Cooking Reliability V004 技术交接：可见表面坐标与面积重叠准入（2026-10-17）

### 1. 加热面坐标

不得使用导入 FBX 子节点的 Transform Pivot 代表物理接触面。模型作者可以把 Pivot
放在物体中心、世界原点或任意建模基准点，它不保证位于可见网格表面。

`HeatingBaseBuilder` 当前执行：

1. 查找 `HeatingBase_HeatingSurface` Renderer。
2. 使用 `renderer.bounds.center.x/z` 与 `renderer.bounds.max.y` 创建
   `Heating Surface Contact`。
3. 把该显式 Transform 绑定到 `HeatingBaseController.heatingSurface`。
4. `EvaluateContactRatio()` 和 `OverlapBoxNonAlloc` 均以该标记为基准。

生产测试要求标记 Y 与 Renderer 顶面误差不超过 0.1 mm。后续替换底座模型时，只需
重建内容即可重新计算，不得手工猜测 Pivot 偏移。

### 2. 食材在锅内的准入

旧规则为：

`centerDistance + foodRadius <= interiorRadius`

该规则要求食材近似圆完整包含于锅内，对 180×135 mm 牛排只允许约 5.75 cm 的中心偏移，
与玩家自然放置不符。

当前规则使用 `HeatingBaseController.CircleOverlapArea()` 计算锅内圆与食材近似圆的
解析相交面积：

`overlapArea / foodArea >= 0.55`

同时继续要求食材中心高度位于 `[-6 mm, interiorHeight]`，并要求
`CookingSettings.enabled=true`。这允许大部分食材位于锅内、边缘少量越界的真实摆放，
但不会让只擦到锅边的食材受热。

锅具仍使用 55% 锅底接触面积、12°倾角和 -4 至 14 mm 垂直间隙，不降低底座与锅之间
的受热要求。

### 3. UI 贴面与朝向

`HeatingBaseBuilder` 不再使用模型总包围盒前方 8 mm 作为所有 UI 的公共 Z：

- 温度 UI 使用 `HeatingBase_TemperatureDisplay` 表面。
- 电源 UI 使用 `HeatingBase_PowerButton_Cap` 表面。
- 加减 UI 分别使用对应按钮帽表面。
- 每个 UI 沿外法线只增加 0.6 mm，避免 Z-fighting。

World Space Canvas 正面仍按 `-RectTransform.forward` 判断。温度文本协议为
`$"{temperature}°C"`，默认显示 `60°C`。

### 4. 统一表现链路

以下四个结果必须来自同一个受热判定，不允许分别触发：

1. `CookableFood.Doneness` 增长。
2. 熟度达到 50 后生模型切换为熟模型。
3. `CookingEffects` 播放滋滋循环声。
4. `CookingEffects` 发射白烟。

如果四项全部未发生，优先排查：

1. `HeatingBaseController.ActiveReceiver` 是否为空。
2. `CurrentContactRatio` 是否达到 0.55。
3. 锅底圆心相对 `Heating Surface Contact` 的垂直间隙。
4. `CookwareHeatReceiver.Contains(food)` 的高度和重叠比例。

不得绕过 `CookwareHeatReceiver` 为烟雾或模型切换新增 Collider 事件。

### 5. 验证与产物

- 定向测试：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/CookingSystemTests.cs`
- 测试结果：4/4 通过。
- Development APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- APK SHA-256：
  `be5958b75b79bcfaa57aefc110433ad0a8ecaaf04f3e249e5de2a5f2c11a19f0`

Android ARM64/IL2CPP、APK ZIP 完整性和 APK Signature Scheme v2 均验证通过。

## Phase System Notifications V002 技术交接：并发消息栈与独立退场（2026-10-17）

### 1. 状态所有权

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/SystemNotifications.cs`
仍是系统消息的唯一状态所有者。左上角提示由单一字符串改为只读 `Hints` 集合：

- `ShowHint(text)` 创建带稳定递增 ID 的独立消息，插入集合首位。
- 默认有效期为 5 秒；显式传入 `seconds <= 0` 时保持原有永久提示语义。
- `Tick()` 对每条消息独立计时并删除到期项，不再由后来的消息覆盖前一条。
- `Hint` 兼容属性继续返回最新一条消息，现有只读调用方无需迁移。
- `Result` 仍是居中显示的独立回合结果，不参与左上角消息栈。

业务系统不得直接创建提示面板，也不得自行维护第二套倒计时。

### 2. 展示与动画

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/SystemNotificationPresenter.cs`
按消息 ID 维护独立 `HintView`：

1. 最新消息固定在顶部，旧消息按 155 px 高度和 14 px 间距向下排列。
2. 新面板从目标位置上方 38 px 进入，0.42 秒内同时使用 SmoothStep 淡入和
   EaseOutCubic 下移动画。
3. 新消息插入时，已有面板在 0.36 秒内平滑移动到新的列表位置。
4. 消息到期后，面板保留 0.34 秒执行 SmoothStep 渐隐，再销毁 GameObject。
5. 透明度时间轴与列表位移时间轴互相独立，连续快速插入不会中断旧消息淡入。

动画使用 `Time.unscaledDeltaTime`，菜单暂停或时间缩放不会冻结 UI 动效。运行时动态创建
的 Image 和 TMP 文本会重新应用 `AlwaysOnTopUi`，保持与原系统提示一致的遮挡策略。

### 3. 回归边界

- `BowlingTests` 覆盖新消息顺序、独立过期、默认 5 秒和永久提示。
- `GameRuntimeTests.BottomHudExpandsVerticalInteractionActivities` 覆盖双面板并存、
  最新消息在上、CanvasGroup 淡入及 Always-On-Top 材质。
- 通知模块生产源码及其直接依赖已使用 Unity 2022.3.38f1 的 Roslyn 引用集编译通过。
- 当前 TRAE 沙箱会使 Unity `bee_backend` 以
  `mono_os_sem_post: semaphore_signal failed with error 15` 退出，因此本阶段未得到
  Test Runner XML；应在非沙箱 Unity Editor 中补跑上述两个测试入口。

## Phase Electric Kettle V001 技术交接：套装物品与烧水状态机（2026-10-17）

### 1. 资产与目录结构

电热水壶采用一个可见目录条目、两个隐藏运行部件：

- `electric-kettle`：物品栏可见，库存上限 1，只负责成组放置和库存归属。
- `electric-kettle-body`：可拿取壶体，持有 `WaterReceiver`、烧水控制器和红色开关。
- `electric-kettle-base`：放置后固定，持有 `ElectricKettleBase` 接触标记。

`ItemPlacementPart` 先放底座，再把壶体放在底座上方 34 mm。三个实例共享
`inventoryDefinitionId=electric-kettle` 与同一个 `inventoryUnitId`，回收任一部件时按现有
组合物品规则回收整套，不额外维护库存表。

`GameSceneBuilder` 的通用目录默认值会把所有定义重置为非固定，因此必须在目录策略阶段
再次对 `electric-kettle-base` 调用 `ConfigureItemPolicy(..., fixedAfterPlacement: true)`。
只在 `ElectricKettleBuilder` 中设置一次不足以抵抗后续目录归一化。

### 2. 运行时状态所有权

`ElectricKettleController` 是电源、有效加热、累计时间、蒸汽与两路音频的唯一所有者：

1. 开关点击先播放 `CookingEffects/ButtonBeep`。
2. `CurrentLiters <= 0.001` 时保持关闭并调用系统提示“请先给壶里加水”。
3. 有水时进入 `powered=true`，但只有壶底与 `ElectricKettleBase.ContactPoint` 对齐后
   才进入 `heating=true`。
4. 有效接触要求上下方向夹角不超过 12°、垂直差不超过 18 mm、水平差不超过 35 mm。
5. 只有 `heating=true` 才累计时间、发射蒸汽并播放 `InductionHum`；离开底座暂停，
   不重置进度。
6. 累计 60 秒后水温固定为 100°C，自动关闭开关、停止蒸汽和循环声，再播放一次滴声并
   提示“水已烧开”。

`ElectricKettlePowerButton` 只负责输入和红色开关位移动画。通电时开关保持按下，
自动烧开后随 `IsPoweredOn=false` 回弹；不得在按钮类内复制烧水计时。

### 3. 水量与温度

`WaterReceiver` 新增 `CurrentTemperatureCelsius` 和 `SetTemperature()`：

- 空容器默认 20°C。
- 第一份接水按 20°C 进入。
- 向已有热水加入常温水时按体积做加权混合。
- `Empty()` 同时清零水量并恢复 20°C。
- 烧水控制器将 20–100°C 映射到 0–60 秒有效加热进度。

当前水量仍由饮水机统一写入，热水壶没有第二套注水接口。后续若增加倒水，应扩展
`WaterReceiver` 的出水能力，不得直接改其私有水量字段。

### 4. 视觉、物理与音频

- Blender 源文件：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/ElectricKettle/source/ElectricKettle_Set_220x245x245mm.blend`
- Unity FBX：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/PropsSource/ElectricKettle/`
- 壶体使用 Capsule + 壶嘴 Box + 把手 Box 复合碰撞体，避免使用高开销动态 MeshCollider。
- 底座使用 185×28×185 mm BoxCollider，并作为固定部件留在放置表面。
- 蒸汽使用最多 48 粒子的世界空间 ParticleSystem，只有有效加热时发射。
- 滴声与电流声引用平底锅系统现有资源，不复制 WAV，不建立新的音效配置中心。

### 5. Builder 与验证

`ElectricKettleBuilder.BuildAsset()` 可独立重建模型材质、两个 Prefab 和三个
ItemDefinition，不依赖全量 `BuildScene`。这是当前其他目录资产构建失败时的最小恢复入口。

专项测试位于：

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/ElectricKettleTests.cs`

测试 2/2 通过：

- `ProductionAssetsExposeOneKettleSetInventoryUnit`
- `EmptyKettleIsRejectedAndValidContactBoilsInSixtySeconds`

结果 XML：
`/Users/bytedance/MateSoul/.artifacts/electric-kettle/test-results.xml`。

## Phase Coffee Grinding V002 技术交接：实例化落豆与手动研磨（2026-10-17）

### 1. 咖啡豆表示层级

本阶段明确区分“满罐静态展示”和“倾倒动态物理”：

1. `CoffeeBeanVolumeRenderer` 使用单颗闭合豆网格和确定性局部矩阵，以
   `Graphics.DrawMeshInstanced` 绘制 720 颗独立豆实例。
2. `CoffeeBeanJarController` 只维护 336 颗有限可倾倒容量，每 0.06 秒释放 8 颗。
3. 每颗落豆是独立 `CoffeeBean`、Rigidbody、Collider 和 Renderer，不再使用共享刚体
   的豆簇。
4. 每瓶同时动态模拟上限为 96；旧豆稳定或超过 1.1 秒后转为 Kinematic。
5. 普通表面接触后延迟 1.5 秒，再在 6 秒内 SmoothStep 缩小并销毁。

满罐实例数大于物理落豆数是有意的视觉/模拟分层：透明罐需要高密度外观，但 PICO 不应
同时承担数百个持续活动刚体。容量扣减仍以 336 颗独立豆为硬上限，倒空后不会补充。

### 2. 研磨套装与库存所有权

目录定义 `coffee-grinder` 是唯一可见、库存上限为 1 的 `Kitchenware` 条目。它通过
`ItemPlacementPart` 生成两个隐藏成员：

- `coffee-grinder-mortar`：研磨容器、豆接收区、研磨状态和粉末效果的 owner。
- `coffee-grinder-pestle`：独立可抓取研磨棒，使用 `CoffeePestle` 暴露研磨尖端。

两个成员共享 `inventoryDefinitionId=coffee-grinder` 和同一 `inventoryUnitId`。
`GameSession.TryRemoveInventoryUnit()` 因此会在回收任一成员时删除整套并返还一个库存。
不得在两个部件上另建同步删除逻辑。

### 3. 接收与研磨状态

`CoffeeGrinderReceiver` 是唯一咖啡豆接收入口：

1. Trigger 命中 `CoffeeBean` 后调用 `TryAcceptBean()`。
2. 物理豆停止模拟并销毁，研磨钵内的 `CoffeeBeanVolumeRenderer` 增加一个独立实例。
3. 被接收的豆没有地面消失计时；普通碗、杯子和咖啡豆罐不挂接收器。
4. Receiver 在 `OnTriggerStay()` 中读取 `CoffeePestle.GrindingTip`。
5. 尖端位于半径 61 mm、高 78 mm 的有效区，且速度至少 0.035 m/s 时才累计研磨时间。
6. 有效搅拌累计 4 秒后，豆实例归零，显示咖啡粉模型并播放 72 粒粉尘 Burst。
7. `CoffeeGrinderController` 通过 `GameItem.Session.Notifications` 发布
   “咖啡粉磨制完成”，不直接创建 UI。

静止放置研磨棒不累计进度；暂时移出容器不会清空已累计时间。新豆可以在已有咖啡粉后
进入下一轮研磨，粉末量按累计已研磨豆数增加。

### 4. 资源与构建入口

- 咖啡豆源资产：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/CoffeeBeanJar/`
- 研磨套装源资产：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/CoffeeGrinder/`
- 咖啡豆构建器：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/CoffeeBeanJarBuilder.cs`
- 研磨套装构建器：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/CoffeeGrinderBuilder.cs`
- 运行时核心：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CoffeeGrinderController.cs`
- 结构化报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/coffee_grinding_system_20261017.json`

Unity 场景重建成功，专项测试 5/5 通过。测试覆盖物理容量、实例化满罐、落地消失、
唯一接收器、静止不计时、4 秒研磨、完成提示和组合库存事务。PICO 真机性能与交互手感
仍需设备验收。

## Phase Virtual Items V003 技术交接：固定物品、墙面投影与回收模式（2026-10-17）

### 1. 定义与权限边界

`ItemDefinition` 的放置策略由三个字段共同描述：

- `FixedAfterPlacement`：玩家不可抓取，冷月的 `ItemTransferStep` 不可搬运。
- `PlacementPreview`：放置前必须经过红绿投影确认。
- `PlacementSurface`：`Ground` 使用水平地面，`Wall` 只接受法线近似水平的竖直墙面。

固定物品不是静态物体。`GameItem.Bind()` 保持 Rigidbody 为动态状态，不调用
`PhysicsBody.SetContact(true)`。地面家具保留重力；墙挂物关闭重力以维持初始挂墙位置，
但继续响应碰撞、`AddForce` 和 `AddTorque`。

当前策略：

- 地面：`table`、`water-dispenser`、`chair`、`television`
- 墙面：`clock`、`dartboard`

### 2. 墙面放置流程

1. `ItemPlacementPreviewController` 从相机中心发射射线。
2. `GameSession.TryResolveWallPlacement()` 只接受竖直表面，并按墙法线、物体厚度和高度
   计算根节点位姿。
3. 投影使用物体宽高形成贴墙薄片；候选空间无占用时为绿色，否则为红色。
4. `PlaceItemAsync(..., searchNearby: false)` 提交前再次调用 `TryGetWallSupport()`，
   防止确认期间墙面状态变化。
5. 恢复存档时，墙挂物使用墙面支撑而不是 `PhysicsBody.HasSupport()` 的向下支撑判断；
   碰撞检查仅忽略实际承载墙，其他重叠仍失败关闭。

### 3. 回收状态机

`VirtualItemFocusController` 是回收选择状态的唯一 owner：

1. `BeginRecycleSelection()` 进入回收状态。
2. 视野中心球形射线选择最近的未持有、未预约 `GameItem`。
3. `GameItem.SetRecycleFocused(true)` 将现有 `ItemFocusHighlight` 材质追加到整物体所有
   Renderer；普通交互控件高亮在此期间关闭。
4. 第二次点击“确认回收”调用 `GameSession.RecycleItem()`；成功后退出回收状态并清理
   高亮。
5. 离开虚拟物品页、开始放置其他物品或销毁 HUD 时均取消回收状态。

`GameSession.RecycleItem()` 返回布尔结果，HUD 仅在事务成功后退出模式。冷月持有或预约
中的物品继续拒绝回收，不绕过既有控制权租约。

### 4. 资源与验证

- 策略生成入口：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/GameSceneBuilder.cs`
- 回归用例：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/GameRuntimeTests.cs`
- 相关隔离回归 8/8 通过。
- Android ARM64 / IL2CPP Development APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `c63cb4c8147dd8f382cc3222deac09162b1f7abd95d4905d61777491dab3af01`

### 5. 水平承载面投影补充（2026-10-18）

固定且 `AllowSurfacePlacement=true` 的物品不再使用只落主地面的预览路径：

1. `ItemPlacementPreviewController` 先将视野中心射线交给
   `GameSession.TryResolveSurfacePlacement()`。
2. 命中法线满足 `normal.y >= 0.6` 时，将桌面、架板、床面或地面作为承载面。
3. 物体保持世界竖直，仅使用视线水平方向确定朝向；根节点位于命中点上方 6 mm。
4. `TryResolveExactPlacement()` 和提交前检查会重新寻找承载面，避免预览后环境变化。
5. 占用检测忽略当前承载 Collider，但不忽略其他 Collider；可放表面物品使用 5 mm
   安全余量，普通地面家具继续使用 30 mm。
6. 存档恢复同样识别水平承载 Collider，允许此前贴近桌面或床面的合法布局恢复。

当前直接使用该固定投影策略的典型资源是
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/Television.asset`。
专项测试 `FixedSurfacePreviewAcceptsSmallTablesShelvesAndBeds` 覆盖小桌、架子、床面和
竖直墙面拒绝，相关放置回归 3/3 通过。

本次 Android Development APK 的 SHA-256 为
`61c7382d9417c380230b73b742ae2a373577c195d7a4b972fff9bd546e87cba9`。

## Phase MR Occlusion V001 技术交接：环境深度与空间网格混合遮挡（2026-10-17）

### 1. 架构边界

现实遮挡由两个互补数据源组成：

1. `PXR_EnvironmentDepthManager` 获取逐帧双目环境深度，负责手、人体和移动现实物体。
2. `RealityOcclusionMeshRenderer` 消费 Spatial Mesh，负责墙、地面和家具等稳定表面。
3. Environment Depth 不修改角色或道具材质。`RealityEnvironmentOcclusionFeature` 在
   URP `AfterRenderingPostProcessing` 阶段读取 `_CameraDepthTexture`，将被现实深度
   判定遮挡的目标像素 Alpha 乘为零，由 PICO passthrough 合成显示现实画面。
4. Spatial Mesh 使用 `Hidden/MateSoul/Reality Spatial Mesh Depth`，只写深度、不写
   颜色且不创建 Collider，因此不会改变导航和物理状态。

遮挡数据只属于渲染层。`RuntimeRoomNavigation`、`RealityMeshSource` 继续分别拥有
NavMesh 与现实交互扫描数据，不能把 Depth-Only Renderer 当作碰撞源。

### 2. 启动流程

1. `GameBootstrap.Start()` 在设备 Player 中调用
   `RealityOcclusionMeshRenderer.Install(Camera.main)`。
2. 安装器在 XR Origin 上挂载唯一的 `PXR_EnvironmentDepthManager` 和
   `RealityOcclusionMeshRenderer`；编辑器模式不启动真实深度 Provider。
3. 房间快照加载后调用 `ApplySnapshot()`。有 `SurfaceMeshes` 时直接构建精确遮挡网格；
   没有时使用楼面和 Scene Capture 障碍体作为近似。
4. `RealityMeshSource` 启动 XR Mesh Subsystem 后，遮挡器同时监听
   `PXR_Manager.SpatialMeshDataUpdated`，按 UUID 增量添加、替换和删除 GPU Mesh。
5. 组件禁用或销毁时解除事件订阅并释放运行时 Mesh 与 Material。

### 3. 构建配置

`PXR_ProjectSetting.asset` 必须保持：

```yaml
videoSeeThrough: 1
spatialMesh: 1
environmentDepth: 1
```

三个 URP Pipeline Asset 必须启用 `m_RequireDepthTexture`，对应 Renderer Asset 必须包含
`RealityEnvironmentOcclusionFeature`。`RealityOcclusionSetup.Configure()` 是统一修复
入口，`GameSceneBuilder.BuildScene()` 每次重建场景前都会调用，禁止只修改某一个质量
档位。

### 4. Shader 与合成约束

- 环境深度合成 Shader：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/Resources/RealityOcclusion/EnvironmentDepthComposite.shader`
- 空间网格深度 Shader：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/Resources/RealityOcclusion/SpatialMeshDepth.shader`
- XR Camera 必须继续使用 `SolidColor` 且背景 Alpha 为 0。
- 全屏通道使用 `Blend Zero SrcAlpha` 与 `ColorMask A`，只衰减合成 Alpha，不覆盖现有
  RGB，也不改变冷月的 NPR Shader。
- `_EnvironmentDepthBias` 默认 0.01。仅在真机证实边缘穿插后小幅调整；过大会让虚拟物
  在现实物体边缘出现明显缺口。
- 透明、反光、细杆和深度相机视野外区域可能没有稳定环境深度。此类区域由 Spatial
  Mesh 尽量兜底，但不能宣称像素级完美遮挡。

### 5. 验证状态

`RealityOcclusionTests` 覆盖快照网格构建、精确网格优先、无物理 Collider 和全部质量
档位配置。Runtime、Editor、测试程序集已通过 Unity Roslyn 编译。当前沙箱禁止独立的
Unity Test Runner 进程写入全局 `CoreBusinessMetrics.db`，因此专项用例尚未实际执行。

Android ARM64 / IL2CPP Development Player 已完整构建成功，两个新增 Shader 均通过
Player 编译。产物为
`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，SHA-256 为
`61c7382d9417c380230b73b742ae2a373577c195d7a4b972fff9bd546e87cba9`。APK Manifest
已确认包含 `enable_environment_depth=1`、`enable_mesh_anchor=1`、
`enable_vst=1` 和 `com.picovr.permission.SPATIAL_DATA`。最终视觉质量仍必须在支持
Environment Depth 的 PICO 真机上验收。

## Phase Electric Kettle V002 技术交接：显式停靠与拉丝金属资产（2026-10-18）

### 1. 模型坐标与资源

电热水壶统一采用壶嘴 -X、提手 +X 的主轴：

- 正面从 -X 看，壶嘴位于中央，提手被壶身遮挡。
- 侧面从 -Z 看，壶嘴在左、提手在右。
- 顶面可检查壶嘴、壶盖、红色开关和提手是否共线。

建模源文件：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/ElectricKettle/source/ElectricKettle_Set_220x245x245mm.blend`

同目录保留 `ElectricKettle_Set_front.png`、`ElectricKettle_Set_side.png`、
`ElectricKettle_Set_top.png` 三张正交复核图。Unity 源资源位于：

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/PropsSource/ElectricKettle/`

### 2. URP 金属材质

FBX 不负责携带 Blender 程序拉丝节点。`ElectricKettleBuilder` 显式导入并绑定：

- `kettle_brushed_steel_basecolor.png`
- `kettle_brushed_steel_normal.png`

`MAT_Kettle_BrushedStainless` 使用 URP Lit，固定参数为：

- `_Metallic = 0.94`
- `_Smoothness = 0.72`
- `_BumpScale = 0.42`
- `_NORMALMAP` 开启

后续替换 FBX 时必须继续保留 `MAT_Kettle_BrushedStainless` 材质名，否则 Builder 无法
应用上述定标。黑色提手、红色开关、接触件和蓝色指示灯按材质名分别定标，不与金属共用。

### 3. Dock 状态所有权

`PhysicsBody` 新增 `IsDocked`、`Dock(Pose)` 和 `Undock()`。Dock 是与 Follow、Contact、
Interaction 并列的刚体状态，不允许业务层直接写 Rigidbody 标志：

1. `ElectricKettleController` 每 0.1 秒在壶底触点周围 0.16 m 查找最近
   `ElectricKettleBase`。
2. 检测到后，以底座旋转作为目标旋转，并根据壶底触点局部坐标反算壶体根位置。
3. `PhysicsBody.Dock()` 一次性设置目标 Pose、清零速度/角速度、关闭重力并切换为
   Kinematic。
4. 停靠状态不执行 FixedUpdate 动力学，不再经历下落、弹开或倾倒过程。
5. `PhysicsGrabInteractable.Grab()` 调用 `BeginFollow()` 时，PhysicsBody 先自动
   `Undock()`，恢复重力和动态刚体，再进入原 Follow 流程。
6. 角色业务调用 `BeginInteraction()` 时同样先解除停靠，避免 AI 与停靠状态争夺刚体。

水壶控制器只计算目标 Pose 并请求 Dock，实际 Rigidbody 状态仍由 `PhysicsBody` 唯一拥有。
禁止在 `ElectricKettleController.Update()` 中持续写 Transform，也禁止使用 Parent/Unparent
模拟停靠。

### 4. 碰撞与组合放置

- 壶身：一个 CapsuleCollider。
- 壶嘴：一个独立 BoxCollider。
- 提手：上、后、下三段 BoxCollider，不填充握持内孔。
- 底座：185×28×185 mm BoxCollider，固定放置。

套装初始放置仍使用 `ItemPlacementPart` 生成两个独立物品。即使初始高度或接触顺序有小幅
误差，壶体也会在首次扫描时直接吸附到底座，不依赖两刚体碰撞自稳定。

### 5. 回归边界

`ElectricKettleTests` 覆盖：

- 生产材质已绑定 BaseMap、NormalMap，Metallic/Smoothness 达到定标值。
- 带任意倾斜、线速度和角速度的壶体进入 16 cm 范围后完成单次吸附。
- 停靠后触点误差小于 1 mm、旋转与底座一致、速度归零、无重力且为 Kinematic。
- `BeginFollow()` 自动解除停靠，恢复动态刚体和重力。
- 原有空壶拒绝、60 秒烧水、100°C 和自动关机继续通过。

测试结果 2/2 通过：

`/Users/bytedance/MateSoul/.artifacts/electric-kettle-v2/test-results.xml`

Android Development APK：

`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`

SHA-256：

`9c2a6f621c57f7f30df0fe644c929389c6f6312d65fc19f038aa6fe8568345b6`

## Phase Social Attention V006 技术交接：距离传感与意图准入分离

### 1. 状态与参数

近距离只产生传感状态，不直接提交 Action：

- 进入距离：0.8 m。
- 退出距离：1.05 m，需连续越界 0.25 秒。
- 接近证据窗口：1.2 秒指数衰减。
- 玩家准入：朝向冷月累计接近至少 0.15 m，并比冷月接近贡献高 0.08 m。
- Candidate 停留：连续位于 0.8 m 内 0.7 秒。
- 角色稳定：协调器空闲、Motor 非移动、水平速度低于 0.05 m/s，持续 0.25 秒。
- Ambient 最长时长：5.5 秒。
- 超时冷却：5 秒；重新武装必须先确认离开 1.05 m。

Candidate 有效期间不调用 `Autonomy.Tick()`，避免刚满足玩家意图时又启动新的随机行为。
已有正式 Action 不会被 Ambient 抢占；Candidate 会等待其结束并重新站稳。

### 2. 接近来源算法

`SampleApproachEvidence()` 每次记录玩家与角色的水平位置。以上一帧双方连线为轴：

1. 玩家位移投影到“朝向冷月”方向，仅累计正向分量。
2. 冷月位移投影到“朝向玩家”方向，仅累计正向分量。
3. 两类证据按 1.2 秒窗口指数衰减。
4. 玩家证据满足阈值并领先时建立 Candidate。
5. Candidate 建立后允许玩家停下；只有冷月证据反超时才撤销。

因此玩家走近后停住不会丢失意图，而冷月随机走到静止玩家身边不会误触发。

### 3. 动画准入阶段

`SocialAttentionAction` 分为：

1. `noticing-player`：开始头眼 Rig，但不设置 Social Attention Animator bool。
2. 0.45 秒后进入 `attending-player`，再 CrossFade 到专用 Social Idle。
3. 头偏转达到既有 40°阈值后，身体才启动 TurnStep。

退出时 `AvatarSocialAttentionRig` 使用缓存的 `lastRawYaw/lastRawPitch` 继续按权重渐出；
不能在 `target=null` 后继续直接读取 Transform，也不能用立即归零替代渐出。

### 4. 生命周期规则

- 距离退出：取消 Ambient Action，确认离开后重新武装。
- Ambient 超时：取消 Action、锁定 proximity attention，并启动冷却。
- 玩家仍站在近处：距离不能重新触发。
- 玩家离开再进入：重新采集玩家接近证据。
- 对话请求：绕过距离、接近来源和冷却，但仍等待角色稳定并执行 Notice。
- 正式 Action：继续通过 `ControlChannels.All` 抢占 Ambient。

### 5. 关键文件与验证

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameSession.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarSocialAttention.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/GameRuntimeTests.cs`

测试报告：

- `/Users/bytedance/MateSoul/.artifacts/social-attention-v6/player-approach-and-notice.xml`
- `/Users/bytedance/MateSoul/.artifacts/social-attention-v6/avatar-approach-suppression.xml`
- `/Users/bytedance/MateSoul/.artifacts/social-attention-v6/boundary-timeout.xml`

## Phase MR Occlusion V002 技术交接：Environment Depth 运行时准入

### 1. 能力边界

`PXR_ProjectSetting.environmentDepth`、APK Manifest 的
`enable_environment_depth=1` 和 `SPATIAL_DATA` 权限必须始终保留。它们声明真机能力，
不能为了兼容模拟器从构建配置中删除。

运行时由 `RealityOcclusionMeshRenderer.EnvironmentDepthAvailable` 决定是否创建
`PXR_EnvironmentDepthManager` 和提交 `RealityEnvironmentOcclusionFeature`：

1. Unity Editor：不启用原生 Environment Depth。
2. Android Build identity 的 fingerprint、product、model 或 Unity device identity
   明确包含 `emulator`：不启用原生 Environment Depth。
3. 其他 PICO Android 设备：启用 Environment Depth。

检测结果只计算一次。不得使用 `swan` 或 `swan_arm64` 作为模拟器条件，因为它可能是
真实 Swan 硬件产品标识。

### 2. 模拟器与真机效果

- 模拟器：继续订阅 PICO Spatial Mesh，并使用
  `Hidden/MateSoul/Reality Spatial Mesh Depth` 写入静态现实几何深度；不支持手、人体、
  移动物体等动态 Environment Depth 遮挡。
- 真机：创建 `PXR_EnvironmentDepthManager`，URP 在后处理后执行
  `Hidden/MateSoul/Reality Environment Depth Composite`，保留动态现实纵深遮挡。

模拟器仍可能在 OpenXR 初始化日志中打印 Environment Depth 扩展不支持；只要没有
`GetEnvironmentDepthTextureId` 调用和 `SIGSEGV`，该声明日志不影响运行。

### 3. 启动保护

`/Users/bytedance/MateSoul/scripts/start-pico-emulator.command` 会：

1. 拒绝缺少 `enable_mesh_anchor=1` 或 `enable_environment_depth=1` 的 APK。
2. 启动后等待 12 秒再确认进程存活。
3. 失败时摘要输出 `EnvironmentDepth`、`SIGSEGV` 和其他启动崩溃证据。

### 4. 验证基线

- Reality Occlusion EditMode：6/6 通过。
- APK SHA-256：
  `61f74a019509fb8aa0b6fd9f3be26ddad2f11e2e2dbd3247b60ef8a3ee800093`。
- 模拟器运行证据：`MATESOUL_READY source=PICO Emulator Spatial Mesh`，角色、房间和
  HUD 正常，持续运行无原生崩溃。

## Phase Electric Kettle V003 技术交接：封闭几何与吸附阈值（2026-10-18）

### 1. 几何修复

`build_electric_kettle.py` 为原有开放式旋转体补充三个独立封面：

- `Kettle_BottomPlate`：水壶实体底面。
- `KettleBase_BottomPlate`：底座实体底面。
- `KettleBase_Deck`：底座上表面。

这些节点由带端盖的 Cylinder 生成并进入 FBX。不要通过关闭背面剔除掩盖缺面；
物品可能从任意方向被玩家拿起观察，生产模型必须保持可见外壳闭合。

### 2. 吸附规则

`ElectricKettleController.baseDetectionRadius` 的生产默认值和 Prefab 序列化值均为
0.12 m。检测仍以水壶 `contactPoint` 为球心，并选择球内最近的
`ElectricKettleBase.ContactPoint`。

吸附行为本身不变：

1. 玩家或角色停止控制水壶后执行邻近检测。
2. 触点距离小于 0.12 m 时调用共享 `PhysicsBody.Dock()`。
3. 水壶位置和旋转瞬时归正，刚体速度归零并关闭重力。
4. 再次抓取、Follow 或角色交互时由 `PhysicsBody` 所有者解除停靠。

不得扩大 Collider 代替吸附阈值，也不得新增并行 Transform 写入路径。

### 3. 关键文件

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/ElectricKettle/source/build_electric_kettle.py`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ElectricKettleController.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/ElectricKettleTests.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/ElectricKettleBody.prefab`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/ElectricKettleBase.prefab`

### 4. 验证基线

- `ElectricKettleTests`：2/2 通过。
- 吸附半径断言：0.12 m。
- 底部资产检查图：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/ElectricKettle/source/ElectricKettle_Set_bottom.png`。
- APK SHA-256：
  `d0a495627d5c008fcc1efc703afd547ded694715db82173352ec73fec43c9c97`。
- APK ZIP 完整性和 APK Signature Scheme v2 验证通过。

## Phase Electric Kettle V004 技术交接：生成贴图内容校验（2026-10-18）

### 1. 故障机制

`MAT_Kettle_BrushedStainless` 始终正确绑定 URP Lit、Base Map、Normal Map，
Metallic 0.94 和 Smoothness 0.72。外观变黑源于 Blender 5.2 在后台模式下将
程序生成的 `Image` 经 `Image.save()` 输出为全零 RGB PNG；API 未抛出异常。

因此“材质引用不为空”不是有效资产不变量。必须验证引用目标的实际像素内容。

### 2. 生成规则

`build_electric_kettle.py` 的 `save_texture()` 直接写入 PNG：

1. 将浮点 RGBA 按 8 bit 量化。
2. 每行写入 PNG filter 0。
3. 使用 zlib 压缩 IDAT。
4. 为 IHDR、IDAT、IEND 写入长度和 CRC。

该路径不依赖 Blender 图像缓存、色彩空间状态或后台保存实现。银灰 Base Map 的
实测 Y 范围为 131–155，平均 142.77；法线蓝通道必须保持高值。

### 3. 构建准入

`ElectricKettleBuilder.ValidateGeneratedTextures()` 在配置 TextureImporter 前：

- 使用 `ImageConversion.LoadImage()` 解码源 PNG。
- Base Map RGB 平均值必须大于 0.35。
- Normal Map 蓝通道平均值必须大于 0.75。
- 任一条件失败立即中止资产构建，不允许黑图进入 Prefab 或 APK。

`ElectricKettleTests.ProductionAssetsExposeOneKettleSetInventoryUnit()` 使用同样的
原始 PNG 解码方式覆盖该不变量，不依赖 TextureImporter 的 Read/Write 设置。

### 4. 关键文件与基线

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/ElectricKettle/source/build_electric_kettle.py`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/ElectricKettleBuilder.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/ElectricKettleTests.cs`
- Base Map SHA-256：
  `adec44b68876c21d041cd0d3a842c849ce7bead8139ab24365a3394a11cc547c`
- APK SHA-256：
  `e7307de4c23fb7887250cf279f4652ba5d749d016308da703264914f21b94c4a`
- 专项测试：2/2 通过；APK ZIP 完整性与 v2 签名通过。

## Phase Coffee Bean Jar V002 技术交接：有界颗粒物理与可停靠盖子

### 1. 设计边界

咖啡豆罐采用“逻辑库存、静态体积、动态代表”三层模型：

1. `CoffeeBeanJarController` 维护 336 颗权威逻辑库存。
2. `CoffeeBeanVolumeRenderer` 用 GPU Instancing 表示罐内剩余体积。
3. `CoffeeBean` 对象池只提供少量可碰撞、可进入研磨杯的动态代表豆。

不得让视觉豆数量反向成为库存真值，也不得按 336 容量创建等量刚体。当前生产参数为：

- 每批逻辑释放：8。
- 每批物理代表：2。
- 物理实例池上限：24。
- 释放间隔：0.06 秒。

### 2. 对象池生命周期

`CoffeeBeanJarController.PrewarmPool()` 在运行时创建罐体同级的 Pool Root，并一次性实例化
24 个 `CoffeeBean`。Pool Root 不能放在罐体子层级，否则根 `PhysicsBody.Initialize()`
可能把豆刚体识别为非法的嵌套复合刚体。

释放流程：

1. `ReleaseBatch()` 先扣减逻辑库存，再按代表数量从队列取豆。
2. `CoffeeBean.Launch()` 恢复 Collider、重力和动态刚体状态。
3. 池为空时，最早的活动代表豆先回收再复用，场上物理数量不会突破 24。
4. 落地缩小结束或 `ConsumeIntoGrinder()` 成功后调用 `ReturnBean()`。
5. 罐体销毁时统一销毁 Pool Root，活动豆与空闲豆共享同一生命周期边界。

### 3. 独立盖子状态机

`CoffeeBeanJarLid` 是独立 `PhysicsBody`，生产层级由 `CoffeeBeanJarBuilder` 生成：

- `Coffee Bean Jar Lid Socket`：保留在罐体模型内，提供唯一闭合 Pose。
- `CoffeeBeanJar_Lid_Pivot`：木盖物理根，包含木盖、密封圈、Collider 和
  `CoffeeBeanJarLidInteractable`。
- 木盖质量：0.085 kg；物理材质：Wood。

运行时 `CoffeeBeanJarLid.Awake()` 先把盖子移到罐体同级，再初始化自身刚体。该类使用
`DefaultExecutionOrder(-1000)`，确保罐体根 `PhysicsBody` 初始化前已移除嵌套刚体。

状态转换：

1. 初始调用 `PhysicsBody.Dock(socketPose)`，盖子为运动学且关闭重力。
2. 冲击达到 0.08 N·s 或 `CoffeeBeanJarLidInteractable.Grab()` 被调用时 `Undock()`。
3. 脱离状态下保持完整刚体物理；抓取运动继续由 `PhysicsGrabInteractable` 提交给
   `PhysicsBody`。
4. 松手后仅当距离不超过 0.018 m、旋转误差不超过 14°、速度满足阈值且冷却达到
   0.35 秒时重新 `Dock()`。
5. 归位是短距离瞬时对齐，不使用可见的远距离插值动画。

### 4. 关键文件

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CoffeeBeanJarController.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CoffeeBean.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CoffeeBeanJarLid.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CoffeeBeanJarLidInteractable.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/CoffeeBeanJarBuilder.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/CoffeeBeanJar.prefab`

### 5. 验证与避坑

- `CoffeeBeanJarTests`：2/2 通过。
- 测试明确验证 336 逻辑容量最终只存在 24 个咖啡豆刚体/Collider。
- 在 kinematic 刚体上写 `velocity/angularVelocity` 会产生 Unity 原生警告；回池时必须
  先判断 `Rigidbody.isKinematic`。
- TRAE 命令沙箱内直接启动 Unity Bee 可能因 macOS 信号量限制以 134 退出；项目现有
  `.command` 外部终端方式可用于批处理验证。该现象不是 C# 编译错误。

## Phase Virtual Items V004 技术交接：投影准入与不可移动策略（2026-10-18）

### 1. 投影准入

`PlacementPreview` 不再依赖 `FixedAfterPlacement`。所有
`CatalogVisible=true` 的最终目录定义都必须启用投影，约束在
`GameSceneBuilder.BuildCatalog()` 和 `ItemActionContentBuilder.ConfigureCatalog()`
两个资产生成边界中执行，防止后处理 Builder 覆盖配置。

水平表面流程：

1. `ItemPlacementPreviewController` 对 `AllowSurfacePlacement=true` 的物品优先调用
   `GameSession.TryResolveSurfacePlacement()`。
2. 从相机中心沿视线发射 8 m Raycast，接受 `normal.y >= 0.6` 的承载面。
3. 命中面可以来自现实重建 Collider 或其他 `GameItem`，因此现实/虚拟桌子、架板和
   床面共用一条逻辑。
4. 候选物体保持世界竖直，按视线水平方向朝向玩家，根节点位于命中点上方 6 mm。
5. 可放表面物品使用 5 mm 占用余量，并忽略当前承载 Collider；其他碰撞仍拒绝。
6. 未命中水平面时继续尝试主地面，墙挂物继续使用独立墙面求解。

### 2. 不可移动标记

`ItemDefinition.ImmovableAfterPlacement` 是独立于
`FixedAfterPlacement` 的物理策略：

- `FixedAfterPlacement`：禁止玩家抓取和冷月搬运。
- `ImmovableAfterPlacement`：在上述限制之外，`GameItem.Bind()` 调用
  `PhysicsBody.SetContact(true)`，令 Rigidbody 保持 Kinematic，不接受外力。

当前只有 `table` 与 `water-dispenser` 启用不可移动标记。其他固定物品不得因为
`FixedAfterPlacement=true` 自动转为 Kinematic。

### 3. 验证

- `FixedSurfacePreviewAcceptsSmallTablesShelvesAndBeds`
- `SurfacePlacementAndReversedSupportOrderRestoreAsOneLayout`
- `ImmovableFurnitureIgnoresExternalForces`
- `FixedMovableFurnitureCannotBeGrabbedButRemainsDynamic`
- 墙挂与生产资产契约等相关专项测试合计 7/7 通过。
- Android Development APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `a79103dfb1d7a414fc55a8a4b5032f41965481f35a7e389742f397bb755cbf3d`

## Phase Virtual Items V005 技术交接：投影与不可移动标记强绑定（2026-10-18）

> 本节是最终规则，覆盖 Phase Virtual Items V004 的“所有目录物品启用投影”方案。

### 1. 强制不变量

`ItemDefinition.ConfigurePlacement()` 在配置边界强制：

```csharp
PlacementPreview == ImmovableAfterPlacement
```

因此：

- 不可移动物品必须显示放置投影。
- 普通可移动物品不得显示放置投影。
- `AllowSurfacePlacement` 只决定候选位置能否落在桌面、架板、床面等水平承载面，不决定
  是否进入投影模式。
- `FixedAfterPlacement` 只表达禁止玩家和冷月直接搬运，不能单独开启投影。

### 2. 当前资源策略

- 不可移动并显示投影：`table`、`water-dispenser`、`chair`、`television`、
  `clock`、`dartboard`。
- 明确保持普通放置：`cupcake`、`frying-pan`、`heating-base`，三者均为
  `placementPreview=0`、`immovableAfterPlacement=0`。
- `heating-base` 仍保留既有 `FixedAfterPlacement=true`，用于禁止直接搬运，但不会
  获取 Contact 物理锁，也不会进入投影状态。

资产生成时，`GameSceneBuilder` 先将所有定义重置为无投影，再仅通过
`ConfigureItemPolicy()` 为不可移动物品同时写入投影和物理锁标记。独立的咖啡豆罐、
研磨套装、电热水壶和收音机 Builder 也保持无投影配置。

### 3. 验证

- 资源扫描确认投影集合与不可移动集合完全一致，共 6 项。
- 专项测试 8/8 通过，覆盖投影准入、地面/水平面/墙面求解、不可移动物理锁、普通固定
  物品动态物理及加热底座无投影契约。
- Android Development APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `43199713d0791421c0fa5091e88360ef5b3f7a75fbae133ad3cc4ce0a0641d56`

## Phase Virtual Items V006 技术交接：数据化放置朝向（2026-10-19）

### 1. 模型正面约定

通用放置旋转以“物品本地 `+Z` 指向玩家”为默认约定。少数资源的建模正面轴不同，必须
通过 `ItemDefinition.PlacementFacingYaw` 修正，不得在 `GameSession` 或 `GameHud`
按物品名称/ID 分支。

当前例外：

- `television`：模型正面为本地 `-Z`，`PlacementFacingYaw=180`。
- `radio`：模型正面为本地 `-Z`，`PlacementFacingYaw=180`。

### 2. 统一旋转入口

`ItemDefinition.ResolvePlacementRotation(Vector3 facesPlayer)`：

1. 将目标方向投影到水平面。
2. 用 `Quaternion.LookRotation()` 建立默认朝向。
3. 右乘 `Quaternion.Euler(0, PlacementFacingYaw, 0)` 应用资源朝向偏移。

以下路径必须调用该入口：

- `ItemPlacementPreviewController` 的主地面投影。
- `GameSession.TryResolveSurfacePlacement()` 的桌面、架子、床面投影。
- `GameHud.PlaceAsync()` 的普通直接放置。

墙挂物仍以墙法线确定贴墙姿态，暂不应用水平正面偏移。

### 3. 验证

- 电视与收音机定义 YAML 已写入 `placementFacingYaw: 180`。
- 测试新增本地 `-Z` 指向玩家的几何断言及生产定义配置断言。
- Unity 许可证当前未激活，返回 `No valid Unity Editor license found`；尚未完成运行测试
  与 APK 构建。必须在许可证恢复后重建场景并执行专项测试，才能生成新的交付 APK。

## Phase Classified Grip V002 技术交接：体积表面与受限手指求解（2026-10-18）

### 1. Profile 几何语义

`gripPosition/gripEuler` 是物品的虚拟掌心锚点，不再等同于物体中心或手指接触点。
`gripShapeOffset` 表示从该锚点到真实握持体积中心的偏移：

```csharp
public Vector3 WorldGripShapeCenter(Pose grip)
    => grip.position + grip.rotation * gripShapeOffset;
```

Builder 按分类生成锚点：

1. `Generic`：体积中心上方 `半高 + fingerClearance + 18 mm`。
2. `Handle/LoopHandle`：轴心上方 `半径 + fingerClearance + 18 mm`。
3. `ThinEdge`：虚拟掌锚点位于物体边缘后方 70 mm，使边缘落到指尖区域。
4. `LargeTwoHand`：左右锚点位于外表面之外，体积中心偏移沿掌面法线。

Generic 的 `gripSize` 必须保留 Prefab Bounds 的真实尺寸，不得再次用 90/110 mm
上限截断；否则掌间隙和关节闭合量会基于错误体积计算。

### 2. 表面目标

手指目标不使用 Collider 或逐帧 Physics 查询：

- 普通物和大物件：将每根手指的基础指尖方向投影到椭球最近表面。
- 手柄与环柄：投影到圆柱截面，并把轴向位置限制在握持长度内。
- 薄物：拇指和食指分别投影到薄片法线两侧，其他三指保持基础手型。

表面间隙由 `fingerClearance` 提供。运行时同时计算：

- `PalmClearance`：握持体积到腕骨—中指根部掌段胶囊的距离；
- `MinimumFingerClearance`：活动指尖到解析表面的最小有符号距离；
- `FingerPoseError`：指尖与目标表面点的距离。

这些指标用于测试和调试，不参与放宽动作准入。

### 3. 人体工学关节约束

动作开始时缓存五指三关节基础局部旋转，放下物品时恢复。每帧流程：

1. 由物品直径、指根到目标距离和 Profile `fingerCurl` 计算闭合系数。
2. MCP 绕掌面法线执行最多 8° 外展；拇指鞍关节最多 22°对掌。
3. 每节屈曲轴由当前指节方向与掌面法线叉乘得到。
4. 固定执行三轮受限 CCD；MCP/PIP/DIP 分别受各自最大屈曲角约束。
5. 关节只能沿选定屈曲方向累计，不允许自由三维 `FromToRotation`。

非拇指上限为 58°/92°/58°，拇指为 48°/62°/42°，再乘实时闭合系数。
Pinch 使用完整侧捏活动范围，但到达解析表面后停止，不会继续压入。

### 4. 性能不变量

- 每手最多 `5 × (1 次外展 + 3 轮 × 3 关节) = 50` 次固定 Transform 运算。
- 不使用 Raycast、Overlap、Sweep、`ComputePenetration` 或运行时 Collider 遍历。
- 指骨数组和基础旋转数组只在 `Begin()` 时创建；Player 的逐帧路径不使用 LINQ、
  `ToArray()`、字符串拼接或容器分配。
- 4 Hz 几何日志位于 `#if UNITY_EDITOR` 调试区，不进入 APK。

### 5. 验证基线

- Apple：Palm `+9.50 mm`，Finger `+2.24 mm`。
- Knife：Palm `+2.00 mm`，Finger `+2.15 mm`。
- Plate：Palm `+3.07 mm`，Finger 最小 `+0.75 mm`。
- Profile 4/4、Item Action 4/4、共享回归 27/27。

关键文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemInteractionProfile.cs`
- `/Users/bytedance/MateSoul/MateSoul/Runtime/AvatarItemRig.cs`
- `/Users/bytedance/MateSoul/MateSoul/Editor/ItemActionContentBuilder.cs`
- `/Users/bytedance/MateSoul/MateSoul/Tests/Editor/ItemGripProfileTests.cs`
- `/Users/bytedance/MateSoul/MateSoul/Tests/Engine/ItemActionRuntimeTests.cs`

## Phase Full-body Reach V003 技术交接：连续全身可达性求解（2026-10-18）

### 1. 状态与所有权

物品动作层只保留：

- `Reach`：拾取、放置和回收；
- `Carry`：移动中的稳定持有；
- `Cut`：切割手势。

业务动作仍只调用共享 `AvatarItemRig`，不得直接写 Animator、骨骼或物品 Transform。
`ItemActionContentBuilder.ConfigureCharacter()` 负责显式绑定双臂、双手和十个姿态骨骼；
运行时禁止按名称重新查找生产骨骼。

### 2. 连续姿态流程

`AvatarItemRig.LateUpdate()` 在手臂 IK 之前执行 `ApplyFullBodyPosture()`：

1. 读取目标相对角色根节点的高度和水平距离。
2. 高位由手臂主导；中位逐步增加髋折叠与膝髋下沉；极低位增加深蹲和受限前倾。
3. 仅对 0.45–0.98 m 的中位目标计算手臂剩余可达量，并把最多 0.18 m 分配给髋部，
   避免按物品名称配置姿态。
4. 脊柱前倾后，颈部补偿 45%、头部补偿 37%，避免头随躯干贴向桌面。
5. 姿态修改前缓存双脚 Pose，修改后用 `AvatarSeatingRig.Solve()` 重解双腿并恢复脚旋转。
6. 最后执行原有双臂、腕部和手指受限求解，再由 Rig 同步手持物。

运行时公开 `PostureLeanDegrees`、`PostureCrouchDepth`、`PostureHeadPitch`、双膝角和
`MaximumFootPlantError`，只用于测试与诊断。Player 路径不执行 Physics 查询，也不
创建逐帧容器。

### 3. 动画资产规则

Reach/Cut Clip 只提供上肢动作基线，不得再烘焙固定 Hips、Spine、Leg、Knee 或 Foot
曲线。构建器每次重建时删除旧 `FloorReach.anim`、`TableReach.anim` 及控制器状态，
防止离散姿态旁路重新进入生产。

### 4. 单段切水果

`KnifeCutStep` 保留拾刀、禁用碰撞、手持跟随和归还控制权，但切割语义改为：

1. 以抓稳后的当前可达刀具 Pose 为手势基准。
2. 播放一次 12 cm 抬刀和下切动作，不要求刀刃穿过苹果几何中心。
3. 手势计时完成后调用一次 `CommitCut()`，原子生成四块苹果。
4. 播放回收动作，在释放边界将刀具精确对齐进入动作前的桌面 Pose，再恢复碰撞。

不得重新增加第二刀、第二 Dock、角色绕苹果转向或 `crossed` 几何门槛。

### 5. 验证与已知边界

- 姿态采样 1/1、Item Action 5/5 通过。
- 地面样本约 37° 前倾、0.54 m 下蹲、双膝保持受限，脚底误差低于 0.005 m；
  高位样本约 0.84° 前倾且无下蹲。
- 共享批次 31/33。两项导航失败可在不运行物品动作时独立复现，归属当前并行
  `AvatarMotor` NavMeshAgent/物理绕行改动，不得通过放宽抓取或姿态限制规避。
- APK：`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `564dd240c1431b7db959fcd35c91dc3e4058371c8894c57a55c802fa5b37d40b`

关键文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarItemRig.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemTransferStep.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CutFruitAction.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/ItemActionContentBuilder.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/ItemActionRuntimeTests.cs`

## Phase Reach Reliability V004 技术交接：分层拾取与固定工作台物理边界（2026-10-18）

### 1. 低位全身姿态

`AvatarItemRig.ApplyFullBodyPosture()` 仍由目标相对高度与水平距离连续求解，不增加
物品名称或离散动作分支。极低位改为：

1. `leanDemand` 与水平延伸量生成基础前倾。
2. `sqrt(floorDemand) * 21°` 提前增加低位髋折叠，总前倾限制为 57°。
3. 下蹲使用 `0.42 m` 主需求与 `0.06 m` 地面增量，避免 0.6 m 以上深蹲遮蔽弯腰。
4. 保留颈 45%、头 37% 反向补偿和双腿脚锁。
5. `PostureTorsoLeanDegrees` 记录最终头髋轴相对角色 Up 的实际角度，测试不能只检查
   请求的 Spine 旋转。

当前极低位证据为请求前倾 `51.45°`、实际躯干轴 `35.01°`、下蹲 `0.414 m`。

### 2. 双层吸附准入

`ItemTransferStep` 定义两个稳定边界：

- `MaximumPickupSnapDistance = 0.25f`：主吸附范围；
- `FallbackPickupSnapDistance = 0.45f`：主范围失败后的兜底范围。

`CanSnapPickup(primaryError, secondaryError)` 使用双手最大误差进行统一判定。普通
`ItemTransferStep` 和 `KnifeCutStep` 必须调用该方法；不得分别复制常量，也不得按
苹果、刀、盘子等名称配置特殊范围。超过 0.45 m 必须拒绝，不能继续扩大。

### 3. 固定工作台与手持物理策略

`GameItem.Stabilize()` 只适用于即将由角色移动的物品。它调用
`PhysicsBody.BeginInteraction()` 后会切换为 Kinematic、关闭重力和碰撞，并由
interaction owner 独占写 Pose。

固定餐桌只需要 `ItemReservation` 防止并发使用，不能调用 `Stabilize()`。否则餐桌
compound collider 被关闭，而桌上尚未拿取的动态物仍启用重力，必然失去支撑穿桌。
因此 `CutFruitAction.BuildSteps()` 只读取 table binding/profile，不取得餐桌
interaction ownership。回归必须断言 Action 启动后：

- `table.Body.Rigidbody.detectCollisions == true`；
- `table.Body.IsInteractionControlled == false`。

### 4. 验证与交付

- `ItemActionRuntimeTests`：6/6。
- 共享回归：31/34；本阶段模块全部通过，失败位于两项导航/避障和一项 HUD 断言。
- Development APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `3db0cc41286bfbd64ca1de867b0ae44c146e96ce6298e72ec8cf444e07e76eca`
- APK 仅包含 `arm64-v8a`，ZIP 与 v2 签名验证通过。

关键文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarItemRig.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemTransferStep.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CutFruitAction.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/ItemActionRuntimeTests.cs`

## Phase Navigation Stall V005 技术交接：转身门控与切向避障生命周期（2026-10-18）

### 1. 根因模型

`AvatarMotor` 的动态障碍绕行包含两个阶段：

1. `OnCollisionStay()` 检测角色沿期望速度压向侧面碰撞法线，持续 0.3 秒后生成切向
   `physicalAvoidanceNormal/sign`。
2. `Tick()` 使用 `PhysicalAvoidanceVelocity()` 请求切向移动，并先通过
   `GateMovementForTurn()` 完成大角度原地转向。

旧实现无论是否仍在转向，每 Tick 都递减 `physicalAvoidanceSeconds`。转身门控把
切向速度清零后，角色尚未到达 30°退出角，1.25 秒绕行窗口已经耗尽，于是重新沿
Agent 前向速度撞向同一障碍。

### 2. 修复不变量

当物理避障处于激活状态时：

- `physicalAvoidanceTotalSeconds` 始终累加，8 秒后仍可故障闭合；
- `waitingForFacing=true` 时暂停 `physicalAvoidanceSeconds`；
- 朝向进入 30°退出角后恢复倒计时，并实际执行 1.25 秒切向移动；
- 不修改 Agent 路径、角色速度、碰撞体尺寸或 MoveAction 的 12 秒阻塞上限。

该规则同时适用于床角、未注册 Collider 和 non-carving NavMeshObstacle，不允许增加
床名称或场景对象特判。

### 3. 证据与验证

- Pre-fix：Agent 路径为 `PathComplete` 且持续请求 `1.1 m/s`，角色却固定在
  `x=1.05`，横向位移近似 0。
- Post-fix：避障计时在转身阶段保持 `1.229 s`；随后角色以约 `0.99 m/s` 横移至
  `z=-1.20`，再沿 Agent 速度回到目标。
- 两类局部绕行 2/2；局部绕行与 `ItemActionRuntimeTests` 联合回归 8/8。
- 无限墙仍被阻塞状态机拒绝。剩余 Idle Clip 权重断言属于动画过渡边界。
- APK SHA-256：
  `b26c46ff2ba642c6a3549bcbe4f5c7a291ab90d4f897af959b9992bba2bcec9f`

关键文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarMotor.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CharacterActions.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/GameRuntimeTests.cs`

## Phase Handle Grip V006 技术交接：圆柱手柄接触模型与通知准入（2026-10-18）

### 1. 系统通知准入

`ItemActionRun.Start()` 在整组预约成功、Rig 所有权建立后发送唯一动作提示
“冷月正在切水果。”。`Advance()` 只推进内部步骤，不拥有用户通知。

预约物品提示遵循真实交互准入：

1. `GameItem.SetFocused()` 和 `OnHoverEntered()` 只管理视觉状态，不发送占用提示。
2. `PhysicsGrabInteractable.ProcessInteractable()` 仅在 Dynamic 阶段检查当前 hover
   interactor。
3. 只有 `IXRSelectInteractor.isSelectActive=true` 且物品仍为角色预约状态时，才调用
   `GameItem.ReportUnavailable()`。
4. `ReportUnavailable()` 保留 1 秒节流，避免持续按住选择键造成消息风暴。

禁止重新在 focus/hover 回调中发送“冷月正在使用 xxx”，因为凝视和射线经过不是
玩家操作意图。

### 2. Handle 圆柱接触模型

`AvatarItemRig.HandleContactLocal()` 是所有 `ItemGripKind.Handle` 的共享接触模型：

- 圆柱局部 Z 为手柄长轴，接触点 Z 来自各指指根并限制在实际手柄半长内；
- 食指、中指、无名指和小指根据指根横向位置分布到圆柱下半面，径向距离恒等于
  `GripRadius + fingerClearance`；
- 拇指落到其指根横向位置的对侧上半面，形成 opposition，而不是复用四指投影；
- 算法只执行固定数量向量运算，不分配集合、不做物理查询，继续满足每只手最多
  50 次关节操作和逐帧零托管分配约束；
- `LoopHandle` 继续使用现有钩握投影，不受 Power Grip 修改影响。

旧实现从“当前指尖到圆柱中心”归一化目标，目标随求解后姿态自反馈。刀具运行时证据
显示无名指极端屈曲而小指远离手柄，说明仅靠净距通过不能代表人体工学正确。

### 3. 回归不变量

- 刀具和平底锅必须使用同一接触函数，在各自半径下保持接触点径向距离正确。
- 四指接触点局部 Y 必须位于圆柱下半面；拇指必须位于上半面且局部 X 与拇指指根
  相反。
- 刀具资产的 `gripPosition`、`gripEuler`、刀刃朝向和掌心 2 mm 净距保持不变；
  不允许通过刀具名称特判掩盖共享算法问题。
- 切水果开始后系统提示列表只有动作开始消息；被预约物品的 passive focus 不得新增
  消息，显式交互尝试才可新增占用提示。

### 4. 验证与资源

- Runtime 全量 Roslyn、通知回归源码和共享手柄几何回归源码编译通过。
- Test Runner 在受限 shell 中仍受 Bee IPC semaphore 限制；真实 Development
  ARM64/IL2CPP 构建通过。
- APK SHA-256：
  `cd7872fa68a4bacdfe51c50d3361a87d22c8788fcc1af4c0be4fc9b41b3619c4`

关键文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarItemRig.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemActionRun.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameItem.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/PhysicsGrabInteractable.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/ItemActionRuntimeTests.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Editor/ItemGripProfileTests.cs`

## Phase Item Action V007 技术交接：稳定手势、动作注视与 Pose 连续性（2026-10-18）

### 1. 稳定手势求解

`AvatarItemRig` 对 `Handle/Power` 和 `ThinEdge/Pinch` 使用以下生命周期：

1. `Begin()` 保存角色五指基准局部旋转。
2. `AttachHeld()` 清空本次持物的姿态缓存。
3. 第一个 `LateUpdate()` 从基准姿态计算稳定接触目标，在受限外展和
   MCP/PIP/DIP 屈曲范围内求解。
4. 将结果保存为 `rightHeldFingerRotations`；后续帧直接恢复缓存，不读取上一帧
   求解结果，不产生目标自反馈。
5. `DetachHeld()` 清空缓存并恢复基准姿态。

Handle 的四指仍落在圆柱下半面，拇指位于对掌上半面；Pinch 的拇指和食指共享盘缘
局部接触点并位于相对两侧。缓存只在每次 Attach 时创建一次，后续帧没有托管分配或
物理查询。

### 2. 放置连续性不变量

`ItemTransferStep` 在 Place 阶段同时驱动手部 Reach 和 held item Pose：

- 先移动到目标上方 0.18 m，再下降到最终 Pose；
- 物品位置和旋转均使用连续 MoveTowards/RotateTowards；
- 只有位置误差不超过 25 mm、旋转误差不超过 8°且手部接触稳定后才进入 Release；
- Release 写入相同目标 Pose，因此不得产生可见跳变。

`KnifeCutStep.MoveKnife()` 同样调用 `SetHeldPoseOverride()`。切割结束归位不再按固定
时长释放，而是等待刀具距离原位不超过 5 mm且旋转误差不超过 2°。这是独立于普通
搬运步骤的必要门控，不能只修 `ItemTransferStep`。

### 3. 已就位判定与停步注视

`CutFruitAction.IsAtWorkspace()` 的准入条件为：

- 物品存在有效支撑；
- 与目标水平距离不超过 0.12 m；
- 垂直距离不超过 0.08 m；
- 当前上方向与目标上方向点积至少为 0.85。

满足条件的盘子、苹果或刀不生成 `ItemTransferStep`。中央协调器仍统一预约全部输入，
因此跳过搬运不会放弃 Action 期间的所有权。

`GameSession.SetItemActionAttention()` 复用 `AvatarSocialAttentionRig` 和
`AvatarMotor.SetSocialAttention()`：停止移动的 Dock/Reach/Lift/Place/Release/
Retract 与切割阶段启用 Idle03 body-only Animator 状态，并追踪当前物品；Approach、
Carry 和 Action Dispose 关闭。业务系统仍不直接写 Animator 或头眼 Transform。

### 4. 回归与交付

- `ItemActionRuntimeTests` 9/9：包含真实角色完整链路、已就位跳过、
  Idle03/物品目标、Handle/Pinch 稳定性和 Held→Staged 小于 2 mm 连续性断言。
- `ItemGripProfileTests` 5/5：共享 Handle 接触、刀具朝向、掌心净距和双手锚点通过。
- Development APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `b6ea6f1e0c05c9b2d60074efbd7816a9a9899a6924ceaacd31492262a9d0f2da`
- APK 仅包含 `arm64-v8a`，ZIP、debuggable、IL2CPP/Unity native libraries 和 v2
  签名验证通过。

关键文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarItemRig.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarSocialAttention.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameSession.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemTransferStep.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CutFruitAction.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/ItemActionRuntimeTests.cs`

## Phase Held Attachment V008 技术交接：手物附件不变量（2026-10-18）

> 本章节覆盖 V007 中关于 `SetHeldPoseOverride()` 的设计。该接口已删除，不得恢复。

### 1. 核心不变量

当物品状态为 `HeldByCharacter` 时：

1. `AvatarItemRig.SyncHeldBody()` 是唯一 Pose 写入者。
2. 单手物品 Pose 只能由 `HandGrip + itemFromGrip` 计算。
3. 双手物品 Pose 只能由 `ItemInteractionProfile.ItemFromHands()` 计算。
4. Action 不得直接写 held item 的世界 Pose。
5. 每帧 `WorldGrip(item.Pose)` 与 `HandGrip` 的位置误差必须小于 5 mm。

违反第 4 条会在 IK 达到可达边界时让物品继续移动、手臂停止，形成 14–41 cm 的悬空。
`HeldAlignmentError=0` 只能证明物品命中了 Action 强制目标，不能证明物品仍在手里；
必须直接测量 `actualGripToHand`。

### 2. 位置驱动与释放对齐

`AvatarItemRig.GripForHeldPosition(itemPosition)` 保持当前手腕旋转，通过已捕获的
`itemFromGrip.position` 反求能将物品中心送到目标的 HandGrip 位置。

普通搬运流程：

1. Lift/Carry/Place 始终 Reach 该 HandGrip，物品由 `SyncHeldBody()` 跟手。
2. 物品中心距离目标不超过 5 mm 后进入 Release。
3. 先 `DetachHeld()`，并将租约状态从 `HeldByCharacter` 转为 `StagedForAction`。
4. 碰撞仍关闭，由 Action 在原位置以 `180°/s` 平滑转到最终朝向。
5. 位置误差不超过 2 mm、旋转误差不超过 2°后恢复碰撞并保存检查点。

`KnifeCutStep` 的切割轨迹和归位采用相同原则：持刀期间只驱动 HandGrip；归位中心
到达后松手，再平滑对齐原始朝向。禁止在闭合抓握期间让刀具独立于手骨移动。

### 3. 证据与回归

- Pre-fix：normal follow 最大 `3.053829e-7 m`；override 分支最大
  `0.4075509 m`。
- Post-fix：885 个 Held 样本最大 `3.998401e-7 m`，覆盖盘子、苹果、刀和苹果块。
- Profile 往返、Rigidbody/Transform 和 owner 写入误差均约为浮点噪声。
- `ItemActionRuntimeTests` 9/9，真实角色完整链路通过。
- Development APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `e03295cce5a933c04f59f4231749ca13e90dc0d293a1002ad48bdbfc2ece476c`
- 仅 `arm64-v8a`，ZIP、debuggable、IL2CPP/Unity native libraries 和 v2 签名通过。

关键文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarItemRig.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemTransferStep.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CutFruitAction.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/ItemActionRuntimeTests.cs`

## Phase Table Dual-Side V009 技术交接：餐桌工作区选侧（2026-10-19）

### 1. 坐标约定

餐桌长边位于餐桌局部 `Z` 的正负两侧。`ItemActionContentBuilder` 中已有工作区坐标
继续以 `-Z` 侧为 authored baseline：

- `workPosition`
- `platePosition`
- `toolPosition`
- `approachPosition`

当选择 `+Z` 对侧时，所有局部工作点执行 `(x, y, z) -> (-x, y, -z)`，目标旋转额外
乘 `Quaternion.Euler(0, 180, 0)`。必须同时镜像位置和朝向，不能只把角色移动到对侧。

### 2. 选侧准入

`CutFruitAction.TrySelectWorkspaceSide()` 对 `side=-1/+1` 分别验证：

1. 由对应工作点生成长边 Dock；对侧 Dock 同样做 180°镜像。
2. `INavigationService.TrySample(..., 0.08 m)` 必须成功且采样偏差不超过 0.08 m。
3. 从当前角色位置到 Dock 的 `TryPath()` 必须返回完整路径。
4. 角色朝向该工作点时，`Motor.Body.CanOccupy()` 必须通过。
5. 盘子、苹果和刀的三个 Dock 必须在同一侧全部通过。
6. 两侧均有效时，按三条路径长度之和选择更短侧；平局保持 authored `-Z` 侧。

`workspaceSide` 在 `CheckBindings()` 中确定，随后整次 Action 固定使用该值。不得在每个
子步骤重新独立选侧，否则角色可能在搬运过程中来回绕桌。

### 3. 测试与当前验证边界

- 新增测试导航装饰器 `FilteredNavigation`，可只屏蔽默认长边的 Dock 带。
- `BlockedDefaultLongEdgeUsesOppositeTableSide` 将输入物品放在对侧镜像工作区，
  断言 Action 成功启动且首阶段为“切开苹果”。
- Runtime 与测试源码通过 Roslyn 编译。
- 当前 Unity 2022.3.38f1 bundled Licensing Client 1.15.5 无法读取现有 entitlement，
  Test Runner 在执行前因 `com.unity.editor.headless` 授权失败退出；未生成 V009 APK。

关键文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CutFruitAction.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/ItemActionRuntimeTests.cs`

## Phase Handle Grip V010 技术交接：局部骨轴 Power Grip（2026-10-19）

### 1. 通用根因

旧 `PoseFingerAnatomically()` 逐关节在世界空间根据指尖目标重算弯曲轴，只优化最终
指尖距离。它不保证三节指骨单平面屈曲、四指顺序闭合或每根手指都接触圆柱。
`rightHeldFingerRotations` 又会缓存首次求解，因此错误局部最优会贯穿整个持物周期。

刀具实测：

- `axisAlignment≈1.0`、`palmClearance≈0.002 m`；
- 食指/中指/无名指/小指 clearance 约
  `18.6/19.0/9.0/20.9 mm`；
- `fingerPoseError≈27.5 mm`。

因此该问题不是 Knife asset 特例，而是 Handle + Power 的共享缺陷。

### 2. 新姿态模型

`PowerGripFlexion(diameter, curl, finger)` 生成 MCP/PIP/DIP 的人体工学偏好角；
`PosePowerGripFinger()` 再以圆柱接触点为约束选择实际姿态：

1. 手柄越细，闭合角越大；直径按 12–55 mm 映射。
2. 从食指到小指 MCP/PIP/DIP 小幅递增，形成连续环握。
3. PIP/DIP 只沿骨骼局部 Z 轴旋转；MCP 允许 `±24°` 的有限双轴内收/外展，以补偿
   食指到小指的横向指根间距，但不允许远端指节侧向扭曲。
4. 在受限角度空间内按“指尖到目标距离 + 偏离偏好姿态的小权重”选择确定解。
5. Thumb 使用独立对掌目标；轴向接触至少位于 handle half-length 的 55%。
6. 首帧结果仍可缓存，但缓存的是约束求解结果，不是逐帧 CCD 的局部最优。

Blender 对原始 FBX 的离线检查确认 Index/Middle/Ring/Pinky 共 12 个关节的局部 Z
方向一致，是正确 flexion axis；Thumb 局部轴为复合方向，不可共用四指公式。

### 3. 内容分类与不变量

- Knife、FryingPan、FryingPanBody、Spatula：`Handle + Power`。
- Spoon、Chopsticks、ChopstickLeft、ChopstickRight：保留 `Handle` 圆柱几何，
  使用 `Pinch`，并采用更低 curl/clearance。
- `MaximumFingerClearance` 是 Handle 视觉准入指标；只检查
  `MinimumFingerClearance` 会让“只有拇指接触”错误通过。
- 完整刀具动作要求最大指尖净距小于 6 mm，并继续检查掌心净距、指尖穿透和手物附件。

### 4. 验证边界

- 固定局部 Z 角方案：`fingerPoseError=41.998 mm`、
  `maximumFingerClearance=25.88 mm`；反向旋转恶化至
  `105.533 mm / 59.21 mm`，因此不能仅靠统一角度或翻转符号。
- 最终约束求解：`fingerPoseError=10.06 mm`、
  `maximumFingerClearance=4.73 mm`、`minimumFingerClearance=-0.12 mm`。
- `ItemGripProfileTests` 6/6，真实角色完整切水果 1/1，
  `ItemActionRuntimeTests` 全量 11/11 通过。
- Unity 2022.3.38 自带 Licensing Client 1.15.5 无法接受旧 MAC binding；由
  LaunchServices 启动 Unity Hub 1.18.3 授权客户端后，测试和构建恢复。测试程序集
  受 `includePlatforms: Editor` 约束，必须通过 EditMode Runner 执行，其中
  `EnterPlayMode` 负责切换运行态；直接指定 PlayMode 会得到 0 项。
- Development ARM64/IL2CPP APK 构建成功：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  SHA-256 `f262e8aea8b30b0c12096f7c460e6f2a4fdff161a4b0a07fca700860b4709b34`。
  ZIP、仅 ARM64、debuggable 和 v2 签名验证通过。
- 离线四视角确认四指已闭合包覆；当前 ADB 无设备，PICO 真机视觉验收仍是关闭
  `knife-handle-grip` 调试会话的唯一剩余条件。

关键文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarItemRig.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/ItemActionContentBuilder.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/ItemActions/*.asset`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Editor/ItemGripProfileTests.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/ItemActionRuntimeTests.cs`

## Phase Virtual Items V008 技术交接：双层高亮与时钟量化更新（2026-10-19）

### 1. 高亮职责

高亮分为两条严格隔离的路径：

1. 普通视角焦点：`GameItem.SetFocused()` 只转发到
   `ItemInteractionFocusTarget.SetFocused()`，因此只亮声明的按钮、旋钮、盖子等组件。
2. 回收选择：`GameItem.SetRecycleFocused()` 遍历整物体 Renderer，追加独立克隆的
   `RecycleFocusMaterial`。

不得让普通焦点复用整物体回收高亮。回收材质从 `ItemDefinition.FocusMaterial` 克隆，
但运行时覆盖：

- `_OutlineWidth = 0.007`
- `_Intensity = 1.25`
- `_Color.a = 0.96`

普通材质继续保持 `_OutlineWidth=0.003`、`_Intensity=0.65`，确保已有按钮/旋钮反馈
不被放大。回收模式退出时必须从 `recycleBaseMaterials` 恢复原材质数组；`GameItem`
销毁时释放运行时克隆材质。

### 2. 时钟表盘资源

`GameSceneBuilder.ConfigureClockNumbers()` 在 Clock Prefab 根下生成 `Clock Numbers`，
并清理旧节点后重建四个 `TextMeshPro`：

- 12：上方
- 3：右侧
- 6：下方
- 9：左侧

不得生成其余数字。文字使用现有 LiberationSans SDF 资产，不新增字体资源或 Canvas。

### 3. 离散时间算法

`RealTimeClock.ApplyTime()` 使用：

```text
minuteAngle = minute * 6°
hourAngle = (hour mod 12) * 30° + floor(minute / 5) * 2.5°
```

结果：

- 同一分钟内秒数变化不会移动任何指针。
- 跨分钟时分针跳 6°。
- 时针只在分钟跨越 0、5、10……边界时跳 2.5°。

### 4. 验证

- 场景重建成功；Clock Prefab 仅包含 12、3、6、9。
- 专项测试 3/3 通过。
- Android Development APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `52560816681b325953f58001aab0af089210a12389e9ba0fe07f3c5a11087e2f`

## Phase Item Attention V011 技术交接：连续物品注视求解（2026-10-19）

### 1. 根因与不变量

`AvatarSocialAttentionRig` 原先将每帧 `Atan2` 得到的 signed yaw 直接限制到
`[-48°, 48°]`。目标位于正后方时，几何方向连续，但角度表示会在
`+180°/-180°` 之间换符号，进而让受限结果从 `+48°` 跳到 `-48°`。

物品注视必须满足：

1. 后半球目标不能因为角度表示换符号而切换转头侧。
2. 目标启用、移动或切换时，Head yaw/pitch 都必须受角速度限制。
3. `AvatarItemRig` 继续负责拾取姿态和颈头俯仰补偿；
   `AvatarSocialAttentionRig` 只在其后叠加注视，不接管物品动作状态。
4. 身体 Dock 转向造成的世界空间 Head 变化不能误判为局部甩头；回归准入应检查
   `CurrentHeadYaw` 的连续性。

### 2. 求解流程

`AvatarSocialAttentionRig.LateUpdate()` 的处理顺序：

1. 从双眼中点到目标计算角色局部 raw yaw/pitch。
2. 当 `abs(rawYaw) > 90°` 时进入后半球滞回，使用当前 Head 侧别或首次 raw yaw
   符号确定 `rearYawSign`；目标返回前半球后才解除滞回。
3. 将连续 yaw 限制到 `±48°`，pitch 限制到上 `26°`、下 `20°`。
4. 结合注视权重生成目标偏移，再用 `Mathf.MoveTowards` 将实际 Head yaw/pitch
   分别限制为 `90°/s` 和 `70°/s`。
5. Neck/Head 按原有 `28/72` 与 `22/78` 比例分配旋转；眼睛使用同一连续 yaw，
   避免头部稳定后眼睛仍在后方边界左右翻转。

### 3. 验证与交接

- pre-fix：完整动作在 raw yaw `179.85° -> -179.98°` 时产生 `95.13°` 单帧
  Head 旋转，反向跨越时达到 `95.31°`。
- post-fix：36 个后方边界样本保持同侧；最大相邻求解 Head yaw 步长 `5.30°`。
- `RearTargetYawWrapDoesNotFlipHeadSide` 专项 1/1；
  `ItemActionRuntimeTests` 全量 12/12。
- APK：`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `ec2462e69230d4600bc5d2ad3cb230b63437fe70570fb3269cb5bed1cd9f821f`

关键文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarSocialAttention.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarItemRig.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemTransferStep.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/ItemActionRuntimeTests.cs`

## Phase Room Navigation V012 技术交接：Spatial Mesh 地面导航（2026-10-20）

### 1. 导航几何所有权

PICO Spatial Mesh 与角色全局导航必须分工：

1. `RoomMapSnapshot.Floors` 提供全局可走地面轮廓。
2. 可靠、接地且尺寸受控的语义家具包围盒提供 Not Walkable 障碍。
3. `RoomMapSnapshot.SurfaceMeshes` 只创建 `MeshCollider`，用于物理碰撞、遮挡和
   交互几何，不直接作为 area 0 可走面。
4. Unity `NavMesh` 负责角色路径；PICO XR 继续负责空间网格采集。

禁止恢复“把全部原始空间网格三角形烘成可走面”的实现。家具顶面会形成离散导航岛，
即使起点和终点都能 `SamplePosition`，`CalculatePath` 仍可能返回 `PathPartial`。

### 2. 障碍筛选与回退

`RuntimeRoomNavigation.IsReliableSpatialObstacle()` 只接受满足以下条件的障碍：

- 底部接近主地面；
- 顶部至少高出地面 0.25 m；
- X/Z 尺寸不超过 3.5 m；
- 语义不是 Wall、VirtualWall、Lamp、AirConditioner 或 Screen。

这些排除项源于 PICO Emulator 实际房间数据：部分语义块是跨墙面或跨房间的粗聚合
包围盒，直接 carving 会移除完整地面。完整构建没有可采样地面时，Spatial Mesh 模式
必须显式警告并回退到 floor-only；原始 MeshCollider 仍保留物理阻挡。

### 3. 安全出生点

`RoomNavigationBootstrap.FindStandableSpawn()` 从相机前方期望点开始，以 0.45 m
递增半径和每圈 16 个方向搜索。候选点必须同时满足：

1. 投影到主地面 NavMesh，水平误差不超过 0.3 m；
2. 高度与主地面相差不超过 0.1 m；
3. 角色 Capsule 在候选 Pose 不与房间碰撞体重叠。

占位检测只忽略 `capsule.transform` 的子层级。不能使用角色与房间共同父节点的
`transform.root`，否则床、桌等同级环境碰撞体也会被错误忽略。

### 4. 回归准入

关键测试：

- `FurnitureSurfaceMeshDoesNotReplaceWalkableFloor`
- `SpatialMeshUsesGroundedFurnitureAsNavigationObstacle`
- `SpawnProbeDoesNotIgnoreSiblingRoomGeometry`
- `BlockedDefaultLongEdgeUsesOppositeTableSide`
- `WalkSitStandAndTargetLossReleaseBodyAndReservation`

PICO 设备验收必须同时验证：

1. 角色出生在地面，不被床或桌顶起；
2. 坐下路径为 `PathComplete`，落座后可正常起身；
3. 切水果至少一侧的盘子位、工作位、刀位均可采样、可完整寻路且可占位；
4. 家具仍有物理碰撞，角色不能穿过床、柜体和餐桌。

关键文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/RoomNavigation/Runtime/RuntimeRoomNavigation.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/RoomNavigation/Runtime/RoomNavigationBootstrap.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/RoomNavigationSurfaceMeshTests.cs`
- `/Users/bytedance/MateSoul/debug-item-seat-navigation-fail.md`

当前 Development APK：

- 路径：`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `8f07a4b5b1f69aa85ddee28d510f490d255bd3bec9664f20bd44e46eac9db9c9`

## Phase Virtual Items V013 技术交接：旧布局恢复隔离（2026-10-20）

### 1. 恢复不变量

旧布局恢复必须满足：

1. `场景有效实例 + 背包剩余库存 = 库存上限`。
2. 当前现实房间变化只淘汰发生冲突的库存单元，不能连带隐藏其他有效物品。
3. 一个库存单元可由多个 Prefab 组成；共享 `inventoryUnitId` 的部件必须整组保留
   或整组回收。
4. 虚拟物品彼此穿插或存档结构非法仍整批失败，避免自动修复掩盖损坏存档。
5. 恢复事务只有在碰撞、支撑图、角色出生和写盘全部成功后才提交。

### 2. 恢复流程

`GameSession.RestoreItemsAsync()`：

1. 保留加载时原始 `GameSaveData` 和库存快照。
2. 验证定义、ID 和 Pose 后生成全部暂停状态的 Runtime View。
3. 对每个 View 执行 `PhysicsBody.CanOccupy()`；回调返回首个实际阻挡 Collider。
4. 若阻挡者属于其他 `GameItem`，判定为损坏存档并整批失败。
5. 若阻挡者属于当前现实房间，则按
   `inventoryDefinitionId + inventoryUnitId` 标记冲突库存单元。
6. 销毁冲突单元的全部部件，调用 `ReturnSavedInventory()`，从 `saved.items` 移除。
7. 对剩余物品执行支撑图和角色安全出生验证。
8. 仅在有冲突单元回收时写入更新后的存档与库存；任何后续异常或取消均恢复快照。

`PhysicsBody.CanOccupy()` 新增可选 `blocked` 回调，只暴露阻挡 Collider 与穿透深度，
不改变原有布尔判定和所有既有调用方行为。

### 3. 实际故障证据

- 原存档：9 个物品，Runtime View 为 0。
- 冲突物品：`coffee-bean-jar`。
- 阻挡者：`Plant_mesh-20-Plant`，不是自身 Collider，也不是其他虚拟物品。
- 穿透深度：`0.113540322 m`；容差：`0.012 m`。
- pre-fix：销毁 9/9 View，9 条存档仍保留。
- post-fix：8/8 有效物品恢复；咖啡豆罐回背包；存档 9 -> 8；可用库存 1 -> 2。

### 4. 回归准入

关键测试：

- `ConflictingSavedUnitReturnsToInventoryWithoutHidingValidItems`
- `ValidatedRoomPoseRestoresItemsWhenAnchorIsTemporarilyUnavailable`
- `SurfacePlacementAndReversedSupportOrderRestoreAsOneLayout`
- `LegacyChopstickPairMigratesToOneInventoryUnit`
- `LateAnchorCompletionAndStorageFailureDoNotLeaveGhostItems`

关键文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameSession.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/PhysicsBody.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/GameRuntimeTests.cs`
- `/Users/bytedance/MateSoul/debug-saved-items-restore-loss.md`

当前 Development APK：

- 路径：`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `9b829804f6f9265bf4356428c65c3b7d70d9ecbdddf13843834515559f682073`

## Phase Item Interaction V014 技术交接：Power Grip 与自然释放（2026-10-20）

### 1. Power Grip 视觉不变量

Handle + Power 不能只最小化指尖到圆柱接触点的距离。必须同时满足：

1. 食指到小指 MCP 保持可见闭合，实际最小屈曲大于 `40°`。
2. PIP/DIP 围绕人体工学偏好角屈曲，不用关节极值代偿。
3. MCP 侧向展开不超过 `10°`，禁止双轴大角度扭转追逐接触点。
4. 四指姿态稳定缓存；持有期间不切换多解。
5. 指尖间隙与目标误差只作辅助指标，不能替代手势拓扑检查。

`PowerGripFlexion()` 按手柄直径、Profile curl 和手指序号生成偏好角。实际求解范围：

- MCP：偏好角 `±8°`
- PIP：偏好角 `±10°`
- DIP：偏好角 `±8°`
- MCP spread：每轴 `±10°`

旧实现允许 MCP 从 `15°` 到 `105°` 全域搜索，距离目标会压倒偏好角惩罚；刀具最终
选择约 15° MCP，因此数值接触正确但视觉错误。

### 2. 刀具自然释放

刀具不再使用通用“脱手后精确旋转对齐”：

1. 持有状态读取当前 Collider 世界包围盒最低点。
2. 保持当前世界旋转，将最低点移动到 authored 支撑面上方约 5 mm。
3. `DetachHeld()` 后不再调用 `TryMoveInteraction()` 写 Pose。
4. `GameItem.ReleaseInteractionToPhysics()` 保存检查点并调用
   `PhysicsBody.EndInteraction()`，立即恢复原碰撞、重力和动态刚体状态。
5. 物体从 5 mm 高度自然落到桌面，姿态由物理决定。

仅刀具搬运和切割后归刀使用 `naturalRelease=true`。盘子、苹果块等有精确构图要求的
内容继续确定性落位。不要把自然释放无差别扩展到所有物品，否则苹果块会在盘中散开。

同一个 `ItemReservation` 可在动作内多次 Begin/End 物理控制；
`GameItem` 用 `interactionReleaseOwner` 保证 lease 清理回调只注册一次。

### 3. 回归准入

- `ItemGripProfileTests`：6/6。
- `ItemActionRuntimeTests`：12/12。
- Release 前后位置差必须小于 `0.1 mm`，旋转差小于 `0.01°`。
- 自然释放后必须满足 `controlled=false`、`collisions=true`。

关键文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarItemRig.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemTransferStep.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CutFruitAction.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameItem.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/ItemActionRuntimeTests.cs`

当前 Development APK：

- 路径：`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `22d2ff51039a32016de2748715edbc220be8b5cd31de0f2816352f958465911f`

## Phase Avatar Animation V015 技术交接：组合待机与随机轮换（2026-10-21）

### 1. 阶段概述

Idle02 已按现有 Lengyue Generic 骨架接入生产 Animator。正式运行资产不是直接引用
FBX 子 Clip，而是由 Editor Builder 生成一个同时包含身体 Transform 曲线与
SkinnedMeshRenderer BlendShape 曲线的组合 `.anim`，避免身体和表情使用两套并行
Animator。

核心资产：

- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Idle02/Lengyue_Idle02_Body_InPlace.fbx`
- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Idle02/Lengyue_Idle02_BlendShape_InPlace.fbx`
- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Idle02/Lengyue_Idle02_Combined.anim`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Avatar.controller`

### 2. 导入与曲线绑定

`LengyueIdle01Builder.ImportIdle02()` 的处理顺序：

1. 将两份 FBX 配置为 Generic、NoAvatar、无动画压缩、保留层级并同步重新导入。
2. 检查两份源 Clip 的 `SkinnedMeshRenderer/blendShape.*` 绑定。
3. 将身体骨曲线按骨名重定向到正式 Prefab；`Head` 映射到运行时的 `Head 1`。
4. 排除头发、裙摆、IK、MCH、DRV 和控制骨曲线。
5. BlendShape 名称优先精确匹配；供应文件省略
   `Face_Blendshape.` 前缀时，按唯一后缀匹配正式 Mesh 中的完整名称。
6. 设置循环并生成组合 Clip；导入或表情采样失败时直接抛错，不为 Idle02 生成
   伪表情兜底。

本批源文件存在命名与内容不一致：

- `Lengyue_Idle02_BlendShape_InPlace.fbx` 没有 BlendShape 曲线。
- `Lengyue_Idle02_Body_InPlace.fbx` 同时包含身体动作和 6 条有效表情曲线。

因此当前组合 Clip 的身体与表情都从 Body FBX 提取。此纠正记录在
`ArtSource/SapphiArt/Animation/Idle02/reports/unity_import.json`，后续不要仅根据
文件名强制切回损坏的专用表情文件。

### 3. Animator 与运行时职责

`AvatarLocomotionBuilder` 维护以下 Base Layer 状态：

- `Locomotion`：Idle01 与 Walk 的 Speed BlendTree。
- `Idle02`：新组合待机。
- `SocialAttention`、`TurnStepLeft`、`TurnStepRight`：保持既有行为。

`IdleVariant == 1 && Speed < 0.05 && !SocialAttention` 时，从 Locomotion 过渡到
Idle02；IdleVariant 变化或 Speed 增大时回到 Locomotion。Idle02 同样可以直接进入
社交注意和左右转身，避免随机待机阻塞高优先级姿态。

`AvatarIdleVariation` 是 `IdleVariant` 的唯一运行时写入者：

1. `GameBootstrap` 在 `AvatarMotor.Bind()` 后调用 `Attach()`。
2. 当前状态必须是 Locomotion 或 Idle02，且 Base Layer 不在过渡。
3. 角色不能处于移动、转身、坐/躺接触、SocialAttention 或有权重的上层动作。
4. 以当前主导 Clip 长度为单位等待 1 至 2 个循环。
5. 从其他注册待机中随机选择一个不同变体并写入 `IdleVariant`。

稳定数值保留在组件字段中；新增待机内容应扩展注册状态，不应在动作协调器中增加
Clip 名称分支。

### 4. 回归与验收

`Idle02PlaybackValidation.Run()` 会实例化正式 Lengyue Prefab 并挂生产 Controller，
通过 `Animator.Update()` 执行实际状态播放，而不是只检查序列化曲线。

当前准入结果：

- Clip 6.7 秒、30 FPS。
- 69 根骨骼、690 条身体绑定、6 条表情绑定。
- 面部通道：左右闭眼、左右睁眼、O 口、微笑。
- Animator 全时段最大面部权重变化 `100.000`。
- 关键身体骨最大姿态变化 `13.111°`。
- Idle01 -> Idle02、移动退出、移动阻断随机切换、SocialAttention 阻断随机切换全部
  通过。

验证入口：

- Unity 菜单：`MateSoul/Lengyue/Import Idle 02`
- Unity 菜单：`MateSoul/Lengyue/Validate Random Idle Playback`
- BatchMode：
  `-executeMethod MateSoul.Editor.LengyueIdle01Builder.ImportIdle02`
- BatchMode：
  `-executeMethod MateSoul.Editor.Idle02PlaybackValidation.Run`

数值报告：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Idle02/reports/unity_import.json`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Idle02/reports/playback_validation.json`

## Phase Avatar Animation V016 技术交接：三待机状态机（2026-10-21）

### 1. IdleVariant 注册表

生产 Animator 的待机参数固定为：

- `0`：`Locomotion` 中的 Idle01。
- `1`：`Idle02`，身体与 6 条 BlendShape 组合 Clip。
- `2`：`Idle03`，BodyOnly Clip。

Idle03 资源路径：

- 源 FBX：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/SocialAttention/idle_03_InPlace.fbx`
- 运行 Clip：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/SocialAttention/Lengyue_SocialAttention_Idle_BodyOnly.anim`

该 Clip 同时被 `Idle03` 和 `SocialAttention` 状态引用。这是资源复用，不是状态合并：
`Idle03` 由 `IdleVariant=2` 进入，`SocialAttention` 仍由布尔参数和注视系统控制。

### 2. 转换规则

`AvatarLocomotionBuilder` 为三个待机建立显式转换：

1. Locomotion 根据 `IdleVariant=1/2` 进入 Idle02/Idle03。
2. Idle02 与 Idle03 可根据目标 Variant 直接互转。
3. 两个扩展待机在 `IdleVariant=0` 时返回 Locomotion。
4. `Speed > 0.05`、SocialAttention 和左右 TurnStep 可从任一待机抢占。
5. 所有待机互转要求 `Speed < 0.05 && !SocialAttention`，交叉淡化为 0.2 秒。

`AvatarIdleVariation` 使用固定 `VariantCount=3`，不从 Inspector 接受与 Controller
可能不一致的数量。随机选择始终排除当前 Variant，等待时长仍为当前主导 Clip 的
1 至 2 个循环。

### 3. BodyOnly 面部复位

Idle03 不包含 BlendShape 曲线。因为所有 Base Layer 待机状态保持
`writeDefaultValues=true`，从 Idle02 完整过渡到 Idle03 后，Idle02 写入的闭眼、
睁眼、O 口和微笑权重必须恢复到角色默认值，不能冻结最后一帧表情。

`Idle02PlaybackValidation.Run()` 现已升级为三待机验证：

- 先播放完整 Idle02，确认 6 条形态键和身体姿态变化。
- 人工设置相关形态键权重后，通过状态机进入 Idle03。
- 推进完整交叉淡化并确认 6 条权重残留小于 `0.1`。
- 播放完整 Idle03，确认关键身体骨变化。
- 固定种子运行随机调度，确认 Variant 1 和 2 都能到达。
- 复验移动和 SocialAttention 门禁。

当前结果：

- Idle03：3.708 秒、24 FPS、0 条 BlendShape 曲线。
- Idle03 最大关键骨姿态变化：`10.350°`。
- Idle02 -> Idle03 面部残留：`0.000`。
- 三待机随机与门禁测试：全部通过。

### 4. 设备验证前置条件

Editor 中修改 Animator、`.anim` 或 `AvatarIdleVariation` 不会热更新已安装 APK。
验证随机待机前必须确认 APK 构建时间晚于这些资产，并校验设备端 `base.apk` 与本地
SHA-256 一致。2026-10-21 已安装的 Development APK 哈希为：

`d1cb9eb39de8978da6d947cef931b2c82421bc1f10e9aa641d16cd90f6715214`

该包已安装到 PICO Emulator `emulator-5554` 并冷启动成功。若设备仍显示旧行为，应先
检查 `pm path` 对应文件哈希，不要仅根据应用版本号判断；当前 `versionCode=1` 和
`versionName=0.1.0` 没有随开发构建递增。

## Phase Coffee Brewing V001 技术交接：守恒转移与冲泡状态（2026-10-21）

### 1. 状态所有权

- `WaterReceiver`：唯一持有液体体积和温度。
- `CoffeeGrinderController`：唯一持有尚未转移的 `groundBeanCount`。
- `CoffeeBrewReceiver`：唯一持有杯内粉量和 `IsCoffee`。
- `LiquidPourer`、`CoffeePowderPourer`：只负责发现目标和发起转移，不缓存第二份
  内容状态。

`WaterReceiver.TransferTo()` 先让目标按容量接收，再从源扣除相同体积；
`CoffeeGrinderController.TransferGroundCoffeeTo()` 使用同一事务顺序。不得先从源扣除
 再尝试接收，否则满杯、错误角度或不支持冲泡的目标会造成内容丢失。

### 2. 液体转移

`WaterReceiver.Receive(liters, temperature, point)` 保留原有开口和倾斜校验，并按
 体积对已有液体与新液体做加权温度混合。原两参数接口继续表示 20°C 常温水，因此
 饮水机行为不变。

水壶 Prefab 的 `LiquidPourer` 参数：

- 流速：0.24 L/s。
- 最小倾倒角：52°。
- 最大检测距离：0.65 m。
- 出口：`Kettle Pour Origin`。
- 表现：`Kettle Water Stream`，无 Collider 和 Rigidbody。

运行时从出口沿重力方向执行无分配射线检测，只接受射线位于目标开口半径内的
`WaterReceiver`。冲泡依据实际水温，不依赖 `ElectricKettleController.IsHeating`；
水壶离开底座后停止加热，但已加热的水仍保持温度。

### 3. 咖啡粉转移

`CoffeePowderPourer` 参数：

- 转移速率：24 单位/s。
- 最小倾倒角：55°。
- 最大检测距离：0.55 m。
- 出口：`Coffee Powder Pour Origin`。
- 表现：`Coffee Powder Stream` 循环粒子，无物理粉末刚体。

目标必须显式挂载 `CoffeeBrewReceiver`。目标实际接收后才减少
`CoffeeGrinderController.GroundBeanCount`，并同步刷新研磨钵粉末高度和显隐。

### 4. 冲泡状态

马克杯的 `CoffeeBrewReceiver` 使用稳定默认值：

- 最少水量：0.08 L。
- 最低水温：80°C。
- 单次粉量：12 单位。
- 最大待接收粉量：48 单位。

水量/温度变化和粉量变化都会调用同一个 `EvaluateBrew()`。三项条件同时满足后：

1. 消耗 12 单位粉。
2. 将 `IsCoffee` 置为 true。
3. 用 `MaterialPropertyBlock` 将该杯液面改为深棕色，不修改共享水材质。
4. 发布“咖啡冲泡完成”提示和 `BrewCompleted` 事件。

状态转换与到达顺序无关。`WaterReceiver.Empty()` 会通过 `Emptied` 事件清空杯内粉量、
咖啡状态和材质覆盖。

### 5. Builder 与验证

`GameSceneBuilder.BuildCatalog()` 先调用
`WaterDispenserBuilder.ConfigureReceivers(catalog)` 建立接水能力，再调用
`CoffeeGrinderBuilder.ConfigureBrewingReceivers(catalog)` 给马克杯注册冲泡能力。
普通碗和盘子只保留接水能力。

关键文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/WaterReceiver.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/LiquidPourer.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CoffeePowderPourer.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CoffeeBrewReceiver.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/CoffeeBrewingTests.cs`

相关回归 10/10 通过，包括真实倾斜、射线命中、100°C 热水转移、12 单位粉转移和
咖啡生成。APK SHA-256：
`3aa08a19bb696c56be05ddb8b7f79f9f6503c0a56a9fd38ff6196b4bfa9b1600`。

## Phase Coffee Brewing V002 技术交接：提示门禁与整豆感应（2026-10-21）

### 1. 提示判定顺序

`CoffeeBrewReceiver.EvaluateContents()` 是三类提示和冲泡转换的唯一判定入口：

1. 杯内无水：不提示，并重新武装冷水和整豆提示。
2. 杯内存在整豆：提示“请先将咖啡豆研磨成粉再冲泡”并停止本轮后续判定。
3. 无整豆且满足水量、温度、粉量阈值：完成冲泡并提示“咖啡冲泡完成”。
4. 未冲泡、存在咖啡粉且水温低于 80°C：提示
   “请用加热后的水冲泡咖啡”。

整豆提示优先，防止整豆、咖啡粉和冷水同时存在时连续发布两条消息。

### 2. 去重与重新武装

`coldWaterWarningShown` 和 `wholeBeanWarningShown` 是条件锁存，不是计时器：

- 条件连续成立期间只提示一次。
- 连续注水触发多次 `WaterReceiver.ContentsChanged` 不会刷屏。
- 温度逐帧变化不会重复整豆提示。
- 水被清空、咖啡粉条件解除或最后一颗整豆离开后，对应提示重新武装。
- 冲泡成功会清除冷水提示锁存。

不要把提示去重放到 `SystemNotifications`；消息总线不应理解咖啡业务状态。

### 3. 整豆感应组件

`CoffeeBrewIngredientSensor` 挂在马克杯的 `Coffee Ingredient Receiver` 子节点：

- `SphereCollider.radius = 0.0323 m`。
- `isTrigger = true`。
- `OnTriggerEnter/Stay` 注册有效 `CoffeeBean`。
- `OnTriggerExit/OnDisable` 注销整豆。
- Sensor 自己维护已观察集合，`CoffeeBrewReceiver` 维护冲泡判定所需的整豆集合。

整豆不会被消费或转成粉；只有 `CoffeeGrinderController.TryAcceptBean()` 可以把物理豆
转为研磨器内部豆量。不得把杯子挂成 `CoffeeGrinderReceiver`。

### 4. Builder 与回归

`CoffeeGrinderBuilder.ConfigureBrewingReceivers()` 每次重建时先删除旧
`Coffee Ingredient Receiver`，再按 `WaterReceiver.OpeningCenter` 和
`OpeningRadius * 0.95` 创建感应区，避免重复子节点。

关键新增文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CoffeeBrewIngredientSensor.cs`

相关回归 11/11 通过，结果：

- `/Users/bytedance/MateSoul/.artifacts/coffee-brewing-feedback/test-results.xml`

APK SHA-256：

`2195c0b7eba45d938c62cc6ca3f9200cf53cb0ba718231420e73b4a5e6e6f795`

## Phase Runtime Cutting V001 技术交接：可配置运行时切割（2026-10-21）

### 1. 系统边界

运行时切割沿用物品系统的单一所有者约束：

1. `RuntimeCuttingTool` 只从玩家持有的刀具采集刀刃轨迹并提交世界空间切平面。
2. `GameSession.TryCutItem()` 独占控制权租约、物品注册表变更、原子替换和存档触发。
3. `RuntimeCuttable` 独占单个切块的网格、碰撞体、体积比例和切割历史。
4. `MeshPlaneSlicer` 是无场景状态的几何算法，不读物品名称、库存或交互状态。

不得让刀具、HUD、AI 或其他业务入口直接替换 Mesh、Collider 或 Registry 状态。

### 2. 内容配置

`ItemDefinition.Cuttable` 使用 `CuttableSettings` 注册实际可切能力：

- `interiorMaterial`：切面使用的第二材质。
- `collisionProxy`：可读、闭合、凸且低于动态凸网格三角形上限的碰撞代理。
- `origin` / `axis` / `length` / `maximumRadius` / `radiusProfile`：定义食材对象空间
  轴线和半径轮廓，用于稳定生成横截面与纵向 UV。
- `maximumCutDepth`：单个分支允许的最大切割历史，胡萝卜为 3。
- `minimumPieceVolumeRatio`：相对原始碰撞体积的最小碎片比例，胡萝卜为 0.03。

胡萝卜由
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/CarrotBuilder.cs`
确定性生成：

- 视觉根体：32 段 x 12 环。
- 碰撞代理：12 段 x 6 环。
- 质量：0.12 kg。
- 库存：5 个，每日补充。
- 能力：`visit`、`runtime-cuttable`。

### 3. 刀路判定

`RuntimeCuttingTool.FixedUpdate()` 只在刀具实例为 `PlayerHeld` 且物理未暂停时运行：

1. 将 `ItemInteractionProfile.bladeStart` / `bladeEnd` 转为世界坐标。
2. 要求刀刃中点位移至少 0.008 m、速度至少 0.28 m/s。
3. 用 `cross(blade, movement)` 生成切平面法线。
4. 先以扫掠 AABB 获取候选，再沿刀刃 5 个采样点对候选实际 Collider 做端点
   `ClosestPoint` 和运动射线命中确认。
5. 命中后提交经过前后帧刀刃中点的世界空间平面，并设置 0.14 s 冷却。

仅 AABB 重叠不能触发切割。当前算法面向有平移分量的挥刀，纯绕刀柄旋转且刀刃中点
几乎不移动时不会切割，这是防误触的有意约束。

### 4. 网格切片与切面材质

`MeshPlaneSlicer.Slice()` 对每个三角形分别裁剪正负半空间，插值位置、法线、切线和
现有 UV，再将交线端点焊接、按切平面角度排序并三角扇封口。结果固定为两个 submesh：

- submesh 0：原始外表面。
- submesh 1：新生成内部切面。

切面顶点通道合同：

- UV0：基于食材径向截面的 `CrossSectionUv`。
- UV1：基于主轴和径向距离的 `LongitudinalUv`。
- UV2.x（Unity `uv3`）：`abs(dot(cutNormal, foodAxis))`。

`CarrotInterior.shader` 使用该连续方向值混合横截面与纵向细节。因此 45° 斜切自然
产生椭圆/拉长芯，不使用“齐平贴图、否则纯色”的硬阈值，也不会在阈值附近跳变。

### 5. 原子替换与物理

`GameSession.TryCutItem()` 的提交顺序：

1. 为目标获取 `PlayerUsing` 租约。
2. 将世界切平面转换到目标对象空间并生成正负两侧视觉/碰撞网格。
3. 实例化两个候选物品，应用各自网格和体积比例。
4. 忽略两个新碎片彼此，检查它们是否会占用其他物体空间。
5. 从 Registry 和视图表移除原物品，再保留两个新物品。
6. 继承原刚体线速度和角速度，并沿切面法线施加相反的 0.035 m/s 分离速度。
7. 触发保存；提交前任一步失败都会回收已准备碎片并保留原物品。

两个切块沿用相同 `inventoryDefinitionId` 和 `inventoryUnitId`，所以库存计数不因切割
翻倍，回收该库存单元时也能清理全部切块。每块刚体质量按原始质量乘当前体积比例。

### 6. 存档合同

`ItemSaveData.cutState` 保存版本 1 的对象空间平面历史：

- 法线 `nx/ny/nz`。
- 平面距离 `distance`。
- 当前切块保留的 `positiveSide`。

加载时从生产 Prefab 的 `originalSurface` 和 `collisionProxy` 按顺序重放。验证边界：

- 最多 4 个通用历史操作，且不得超过物品自身 `maximumCutDepth`。
- 法线、距离必须有限，法线长度必须约为 1。
- `null`、`planes == null` 和空数组均按未切物品处理，兼容旧存档。
- 碰撞网格每次重放后仍必须为正体积且不超过 255 个三角形。

### 7. 资源与验证

核心文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RuntimeCuttingTool.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/MeshPlaneSlicer.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RuntimeCuttable.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameSession.RuntimeCutting.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/Shaders/CarrotExterior.shader`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/Shaders/CarrotInterior.shader`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/CarrotCuttingTests.cs`

纹理位于
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/PropsSource/Carrot/Textures`。
4 张运行时图均为 512 x 512，但来源是用户提供的 360 x 360 AI PNG；不得将其描述为
原生 512/1024 细节。

专项测试 7/7、生产切割集成 1/1 通过。最终 APK：

- 路径：`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- 大小：141,227,418 字节。
- SHA-256：
  `3fb315c7da2d511bdbf25fba8f48ed2dcee38bfba5a9c8e1e0f9e6d84cb99ce6`
- ZIP 完整性通过，APK Signature Scheme v2 通过。

## 2026-10-22 GitHub 发布边界与依赖校验（准备阶段）

- 发布通过独立 Git 工作树完成；原工作树的未提交开发成果与开发资料继续保留。
- 根目录脚本忽略必须使用 `/scripts/`；Unity 生成目录应限定到项目根，不能通过未锚定的 `Obj/` 排除模型资源目录。角色和 PICO SDK 内部的 `Scripts/` 属于真实依赖。
- 资源剔除依据主场景、角色源 Prefab、动态 Resources、运行代码、构建代码和工程设置的依赖闭包；序列化 GUID 也需校验，避免 Library 缓存掩盖遗漏。
- 角色使用 Generic 动画时不强制要求 Humanoid Avatar；仅 Human Motion 需要有效的 Humanoid Avatar。
- `GameHud.CloseDialogue` 曾位于调试 region 内，移除调试上报时必须保留关闭对话的实际行为。
- 饼干资源迁至 `Assets/MateSoul/Content/PropsSource/Cookie`，保持 `.meta` GUID；构建入口不再读取实验路径或强制读取 `ArtSource`。
- 本地网关默认 loopback HTTP，仅用于 Development Build 和 ADB reverse；正式 APK 保留 HTTPS 校验，服务端密钥通过用户自己的 `.env.local` 配置。
- 最新模型的 Texture2D 文本序列化可超过 GitHub 单文件限制。最终发布前需验证资产体积、像素数据和资源引用；当前仅完成准备，提交号和完整 APK 验证结果待补充。

## Phase Bowling Animation：动画触球帧与手球同步（2026-10-22）

资源目录为
`/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Bowling/`。
原始 FBX 保留不变，`Lengyue_Bowling.anim` 为映射到生产骨骼路径的 Generic Clip，
保留完整身体、手指和可用面部曲线，排除参考球、指孔模型及控制骨架容器。
`BowlingMotion.asset` 保存 Clip、20/21/90 事件帧和参考球校准得到的手部局部位姿。

1. `BowlingTurnAction` 通过现有 MoveAction 接近球，使用 `AvatarMotor.Dock` 对齐站位和
   朝向。站位由动画触球点的水平位置反算，不再硬编码球与角色的固定间距。
2. 专用 `Bowling.Roll` Override 层播放完整动作，状态速度为 0，由 Action 时钟提交
   normalizedTime；上层权重为 1 时现有随机待机门禁阻止轮换。
3. `AvatarBowlingRig` 第 20 帧开始检测球中心与校准持球中心的距离。8 cm 内立即吸附；
   第 21 帧仍未吸附则采用 45 cm 范围；超出则无视距离吸附。距离无法达到不再抛出
   “够不着”异常；物理控制权失效仍应报错。
4. 吸附复用物品系统的 `PhysicsBody` Interaction 所有权，持球期间 Kinematic、
   无重力/碰撞、无 Rigidbody 插值。`LateUpdate` 执行序 13000 读取最终右手骨骼位姿，
   调用 `TryMoveInteraction` 同步球的位置和旋转，不再用 IK 追逐预设球轨迹。
5. 第 90 帧以同一手部位姿解除持有，再按球道方向施加速度和 `speed / radius` 角速度。
   动画完成后清零 Bowling 层；Dispose/OnDisable 恢复物理所有权。

代码路径：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/BowlingMotion.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarBowlingRig.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/BowlingTurnAction.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Editor/BowlingAnimationBuilder.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/BowlingAnimationValidation.cs`

验证覆盖 15/30/72/120 FPS、正常/扩大/无距离吸附、提前吸附门禁、整个持球段姿态、
出手一次性和取消物理恢复，共 12 条通过。报告为
`/Users/bytedance/MateSoul/项目沉淀资料/validation/bowling_animation_20261022.json`；
参考球相对偏移最大差值 2.693 mm，生产 Animator 姿态误差 0°，持球同步误差 0。
物理速度断言必须先执行 Script 模式的物理步，不能在 AddForce 后同帧立即读取速度。

最终包已完成 PICO Emulator 完整回合：玩家计分和球瓶复位后，冷月完成拾球、滚球、
击中球瓶并得到 10 分，进入 Winner 结算。首个命中为出手后 0.385 游戏秒，
计分为出手后 5.428 游戏秒，无吸附失败或动作超时。设备数值证据位于
`/Users/bytedance/MateSoul/项目沉淀资料/validation/bowling_animation_device_20261022.json`。
已安装 APK 哈希为 `450b10d2772f42a9435385627fc86707f9115c86dc4893da104cd2ecd256e8e1`。
后续真机验收关注手指与球面接触及不同真实地面高度的触球效果。

PICO 真机仍需验收 6DoF 挥刀命中、连续切块抓取稳定性和近距离切面清晰度。

## Phase Avatar Animation V017 技术交接：随机待机时钟与准入边界（2026-10-22）

### 1. 时间合同

`AvatarIdleVariation` 的等待时间是表现策略，不应受低帧率下
`Maximum Allowed Timestep` 对 `Time.deltaTime` 的裁剪影响。运行时必须使用
`Time.unscaledDeltaTime` 计算待机轮换；暂停菜单通过组件门禁控制，不通过缩放时间
间接延长轮换。

### 2. 中断合同

移动、转身、社交注意、坐/躺和上层动作只暂停当前倒计时。恢复到可播放待机状态后
继续使用剩余时间，不重新抽取 1 至 2 个循环。只有完成一次 Variant 选择后才创建
下一轮计划。

### 3. 可见状态所有权

随机待机准入依据：

1. Animator 已启用且 Base Layer 不在过渡。
2. `Speed <= 0.05` 且 `SocialAttention=false`。
3. 没有坐/躺接触姿态。
4. 所有上层 Animator Layer 权重不超过 0.01。
5. 当前状态是 Locomotion、Idle02 或 Idle03。

不得再使用 `AvatarMotor.IsMoving/IsTurning` 作为待机门禁。它们表示路径/转身意图，
可能在角色视觉上已经静止时仍保持 true；实际 Walk 由 Speed 拦截，TurnStep 由状态
白名单拦截。

### 4. 设备证据

- pre-fix：56.425 秒墙钟仅累计 5.629906 秒，Variant 始终为 0。
- post-fix：约 10.4 秒完成 Variant 0 -> 1，并完成 Locomotion -> Idle02 转换。
- 自主移动开始后 Variant 1 保持，下一轮剩余时间暂停而不清零。

最终 APK：

- 路径：`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- 大小：141,691,096 字节。
- SHA-256：
  `450b10d2772f42a9435385627fc86707f9115c86dc4893da104cd2ecd256e8e1`
- ZIP 完整性通过，APK Signature Scheme v2 通过。

## Phase Virtual Items V014 技术交接：水平虚拟支撑与覆盖率合同（2026-10-22）

### 1. 水平支撑准入

非墙面物品满足以下任一条件时可探测水平支撑：

1. `AllowSurfacePlacement=true`，用于普通可放到桌面的物品。
2. `ImmovableAfterPlacement=true`，用于带放置投影的固定大件。

`ItemPlacementPreviewController` 对所有非墙面不可移动物品先执行
`TryResolveSurfacePlacement`。若视线未命中有效水平支撑，再回退到主地面精确放置。
不得重新用 `AllowSurfacePlacement` 单独限制不可移动物品，否则投影会再次无法落到
虚拟桌面、架子等表面。

### 2. 支撑覆盖率

`ItemDefinition.MinimumSurfaceSupportRatio` 是内容数据，不在 `GameSession` 中添加
物品 ID 分支：

- 默认值 1：采样旋转后占地范围的四个内缩角，适用于桌子、椅子、饮水机等大件。
- 小于 1：在缩放后的占地椭圆上采样 8 个方向，适用于允许边缘少量悬空的物品。
- 复古电视由 `TelevisionBuilder` 配置为 0.85。该值允许支撑面略小于
  0.48 m x 0.45 m 投影，同时保持电视正面朝向玩家。
- 覆盖采样必须命中同一支撑物、法线 `y >= 0.6`，且各采样点表面高度差不超过
  3.5 cm。只命中中心但边缘明显悬空时仍应拒绝。

### 3. 虚拟复合支撑

水平射线命中的 Collider 若属于 `GameItem`，支撑身份按该 `GameItem.transform`
判定，而不是按单个 Collider 判定。用途有三处：

1. 覆盖采样可命中同一虚拟物品下的不同子 Collider。
2. `IsPlacementFree` 忽略该支撑物自身的复合 Collider，但不忽略其他物品。
3. 存档恢复时 `PhysicsBody.CanOccupy` 使用独立的 `ignoredColliderRoot` 忽略同一
   支撑根，随后仍由支撑图确认上下层关系。

预览、`TryResolveExactPlacement`、`PlaceItemAsync` 最终提交和
`RestoreItemsAsync` 必须共用上述规则，避免出现投影绿色但提交失败，或首次放置成功
但重启后恢复失败。

### 4. 验证基线

放置专项回归 6/6 通过，覆盖略小电视支撑、明显过小支撑拒绝、不可移动椅子放到带
复合 Collider 的虚拟桌面、墙挂物规则和上下层反序恢复。

最终 APK：

- 路径：`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- 大小：141,692,728 字节。
- SHA-256：
  `28284f4de24d07085bd289ad51b3372d5c421efffd5037a89c3dd51bdbe48e86`
- ZIP 完整性通过，APK Signature Scheme v2 通过。

## GitHub main 发布边界与构建基线（2026-10-22）

发布提交为 `3981a436c89b2b922446706a630dcfaf2065a2fe`，GitHub main 已回查确认。
工作树采用独立发布副本，原开发目录和其他美术分支未切换。

### 保留范围

- `MateSoul/Assets`、`MateSoul/Packages`、`MateSoul/ProjectSettings`。
- `XR/`：`com.bytedance.pico.xr` 本地 Unity SDK。
- `Dependencies/Lengyue/UniVRM-0.131.3/{UniGLTF,VRM}`：新版角色实际使用的依赖。
- `services/ai-gateway`：源码、配置示例、锁文件与启动说明。
- 根 README、忽略规则及第三方许可证。

依赖收集同时覆盖场景、角色、物品、Resources 动态加载、渲染/XR 设置、
ProjectSettings 引用和编辑器内容重建输入。URP 全局设置
`Assets/UniversalRenderPipelineGlobalSettings.asset` 必须随项目保留。
`Study/` 和 `LengyueIntegration/` 中存在当前角色实际引用的网格、材质、
纹理与动态脚本，不能根据目录名称一概排除。

### 序列化与忽略规则

四张 4096² 原生 Texture2D 的文本版本超过 GitHub 单文件限制，发布树采用
Unity 二进制资源与 Mixed 序列化。每张保留 67,108,864 字节原始像素，
文件大小为 67,112,416 字节；像素 SHA-256、颜色空间、过滤/环绕设置、
GUID 与 fileID `2800000` 在转换前后保持一致。

根目录临时脚本和构建对象的忽略规则必须锚定，避免 `scripts/` 或 `Obj/`
误伤 Unity 的真实 `Scripts/`、`OBJ/` 资源目录。SDK Samples、项目测试、
独立调试工具、验证媒体、ArtSource、APK、缓存及报告不属于发布内容。

### 验证与使用

最终发布树为 4,554 个文件；1,050 项依赖检查、Android ARM64 / IL2CPP 构建、
移除临时验证脚本后的编译、APK ZIP CRC 和 v2 签名均通过。
验证 APK 为 179,735,124 字节，SHA-256 为
`34c8008bde54f4173de81bfe2da91ccdb6d0345cbd87cf98b8ee77840bbda3da`。

AI 网关验证包括 TypeScript 构建、健康检查、`web_search` / `web_fetch`
注册和非法请求 400；真实模型请求数量为 0。默认 loopback HTTP 地址用于
Development Build 与 `adb reverse`，正式构建必须使用实际 HTTPS 网关；
密钥只在服务端 `.env.local` 配置。设备运行仍应按实际 PICO 硬件能力验收。

发布验证用工作树、APK、日志、下载缓存及辅助脚本在远端确认后清理；
本节与工作报告仅在本地沉淀，不进入 GitHub 发布树。

## Phase Lengyue Production Merge 技术交接：角色资产与主工程接入（2026-10-22）

### 1. 当前运行入口与资产版本

Unity 2022.3.38f1 主工程仍为 `/Users/bytedance/MateSoul/MateSoul`，启动场景为
`Assets/MateSoul/Scenes/MateSoul.unity`，角色为
`Assets/MateSoul/Content/Lengyue_Runtime.prefab`。当前模型组合为 Direct49 头部/头发、
SoftFit01 上身与袜子、LimbRefinement01 手肘与小腿。不要退回 Direct41，也不要把
只有诊断产物的更高编号目录直接作为完整模型来源。

新版网格、材质与校正 ShapeKey 位于 `Assets/LengyueIntegration`，源资产依赖闭包
位于 `Assets/Study`。`com.vrmc.gltf` 与 `com.vrmc.univrm` 使用工作区内
`Dependencies/Lengyue/UniVRM-0.131.3`，原始许可证保留。PICO 包路径仍为
`file:../../XR`，平台仍为 PICO Unity SDK / Android ARM64 / IL2CPP。

### 2. 动画与状态所有权

13 条当前主工程 Clip 已按 Humanoid Pose 重定向并烘焙到新版骨架路径，保留 GUID
与事件；实际运行沿用已烘焙曲线，当前 Animator 没有运行时 Humanoid Avatar。
主工程动作协调、待机、社交、坐躺、物品接触、保龄球与换装所有权保持原有职责。

`AvatarVisualDynamics` 在执行顺序 14000 的 LateUpdate 中更新新增视觉组件：
裙摆支撑、手动 Spring Bone、坐姿大腿净空 ShapeKey 和睫毛表情同步。
`StudySkirtPose` 只驱动插入的支撑 Transform，其子骨由 Spring Bone 管理。
这些视觉更新不驱动角色根 Transform 或业务 Animator 状态。

`AvatarTurnFootPlantRig` 将原来的“完整腿部 IK 后混合关节旋转”改为“混合脚部
目标位置后执行一次 IK”，脚部旋转仍按权重混合，避免近直腿旋转插值引起的脚部
弧线滑移。

### 3. 场景重建不变量

1. `AvatarLocomotionBuilder.BuildTurnClips` 优先复用已重定向的左右转身 Clip；
   新骨架接入后不得从旧角色 Prefab 自动覆盖这些 Clip。
2. `GameSceneBuilder.BuildCharacter` 在内容采样前保存全部 Transform 的
   localPosition/localRotation/localScale 及左右脚接触权重。
3. 内容与接触绑定完成后恢复上述状态，再保存 Prefab，防止临时采样姿态污染
   默认姿态。左右脚默认世界 Z 为约 -0.018681 m。
4. `ProductionCharacterValidation.Run` 检查单 Animator、61 个面部形态键、
   Direct49 头发 Shader、SoftFit01 材质、全部骨骼/脚本引用及正常场景网格一致性。

### 4. 合并与验证边界

当前主工程只迁入确认的资产及所需代码；其他并行玩法源码未覆盖。正常
`BuildScene` 重存的物品/配置写入 `normal_build_asset_changes.json`，不包含在本次
角色提交。`merge_manifest.json` 保存资产前后哈希，`BeforeMerge` 保存可回滚文件。

22 项编辑器运行验证与 9 项既有回归覆盖待机、转身、社交、换装、坐躺、双手接触、
拾球、持球与动画出手；不代表真机追踪或性能验收。袜口局部几何验证覆盖
0°、+15°、-15° 俯仰，未发现负向穿入，不代表全动画碰撞验收。

最终主工程 APK 位于 `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
182,880,466 字节，SHA-256 为
`146a7fcf7d765412b702f74aa34bfb12c72286b42d0a35b3f8e3c3385dea7501`。
首次封包磁盘不足时保留已成功的 ARM64/IL2CPP 编译结果，恢复空间后只重跑
Gradle `assembleDebug`；最终 ZIP CRC、APK v2 签名、ABI 和关键载荷哈希通过。
Manifest 保留空间数据权限与环境深度能力，未执行设备安装或运行验收。

## Phase AI World Context 技术交接：有界现实与虚拟世界摘要（2026-10-22）

### 1. 阶段概述

本阶段在既有 persona/runtime/action catalog 之外新增 `runtime.world`。目标是让对话
理解玩家和房间当前状态，同时保持采集成本与 Prompt 长度有明确上限。原始空间网格、
逐顶点数据、全量坐标和全量虚拟实例不会进入每轮请求。

主要文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Gameplay/CharacterContext.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameSession.AIContext.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RuntimeWorldFacts.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CharacterRuntimeContextProvider.cs`
- `/Users/bytedance/MateSoul/services/ai-gateway/src/contracts.ts`
- `/Users/bytedance/MateSoul/services/ai-gateway/src/conversation-service.ts`

### 2. 数据结构与所有权

`RuntimeWorldSnapshot` 由 `GameSession` 独占生成，包含：

- `player`：玩家是否接近、距离档位、对话是否打开、输入模式和当前关注物。
- `held_items`：玩家或冷月实际持有/使用/预约的物品，最多 8 条。
- `activity`：当前小游戏的 ID、阶段和分数；没有小游戏时为空。
- `active_facts`：设备、烹饪和咖啡流程的有效状态，最多 12 条。
- `inventory_alerts`：有限库存低于 20% 或归零时输出，最多 8 条。
- `environment`：启动时缓存的房间面积、尺寸、形状、开放度、语义数量、区域和少量
  空间关系。
- `virtual_world`：全部已放置虚拟物品的分组计数与最多 12 个显著精确实例。

`CharacterRuntimeContextProvider` 只接受 `Func<RuntimeWorldSnapshot>` 并在 Capture
时调用，不拥有房间、物品或小游戏状态。`world` 保持可选，以兼容尚未完成启动配置的
Unity 客户端和旧请求。

### 3. 采集流程

1. `GameBootstrap` 完成 `ConfigureRealityItems` 和 `MiniGames.Bind`。
2. `ConfigureAIWorldContext` 从 `RoomMapSnapshot` 生成一次环境缓存。
3. 玩家发送消息时，`CharacterRuntimeContextProvider.Capture` 调用
   `CaptureAIWorldContext`。
4. 动态采集只遍历当前 `GameItem`，生成持有物、事实、库存告警、分组计数和显著实例。
5. `HttpLLMProvider` 随原 runtime 一起序列化 `world`。
6. Gateway 先用严格 Zod schema 校验所有字段和数组上限，再构建模型 Prompt。
7. Gateway 保留原始 targets 只做回复动作校验；Prompt 使用 compact target summary。

World revision 仅在序列化后的动态摘要变化时递增。环境缓存单独使用 revision 1；
重复对话且世界状态不变时不会伪造新版本。

### 4. 现实环境精度规则

- `PICO Room Capture` 且存在 RealityItems：`count_quality=observed`，允许生成最多
  8 条近邻关系。
- `PICO Emulator Spatial Mesh`、网格聚合或障碍物回退：
  `count_quality=estimated`。语义包围盒可用于区域推断，但不能宣称逐件准确，也不
  生成精确物体关系。
- `shape` 只分 `rectangular/irregular/multi-area/unknown`；`open_space` 由地板面积
  与非墙障碍物投影面积估算为 `open/balanced/dense`。
- 环境分析不在每轮重复执行。房间重新 Capture 时必须显式再次调用
  `ConfigureAIWorldContext`，刷新环境 revision 与动态摘要。

### 5. 扩展约定

设备状态通过 `IRuntimeWorldFactSource.TryCaptureRuntimeWorldFact` 暴露。组件仅在
状态对对话有价值时返回一条短事实；关闭且无内容的设备返回 false。`GameItem.Bind`
缓存子层级 contributor，采集时不做反射扫描。

当前 contributor：

- `RadioController`、`TelevisionController`
- `ElectricKettleController`、`HeatingBaseController`
- `WaterDispenserController`、`CookableFood`
- `CoffeeBeanJarController`、`CoffeeGrinderController`
- `CoffeeBrewReceiver`

新增设备应在自身组件实现接口，事实值使用短键值信息。禁止在中央采集器按
ItemDefinition ID、显示名称或具体设备类型分支。新小游戏应覆盖
`MiniGame.CaptureRuntimeContext`；保龄球已输出 phase、player_score 和
companion_score。

### 6. Prompt 与协议边界

Gateway 的 WorldContext 上限与 Unity 一致：持有物 8、事实 12、库存告警 8、
语义计数 24、关系 8、虚拟物品组 24、显著实例 12。越界请求直接 400，不截断后
继续推理。

target compact 规则：

1. 按 `name + state + sorted capabilities` 分组。
2. 每组只暴露最多 3 个可执行 sample ID，最多 32 组。
3. WorldContext 中显著且仍可用的目标额外作为 exact targets，最多 12 个。
4. 完整 request.targets 不进入 Prompt，但继续用于最终 `action.target_id` 能力校验。

这保证物品数量增加时 Prompt 主要随“状态种类”而不是“实例数量”增长。模型只能选择
compact summary 中出现的 ID；被省略实例仍保留在世界统计中，但本轮不直接作为动作
目标。

### 7. 验证与已知限制

- AI Gateway：TypeScript build 成功，19/19 测试通过；覆盖旧客户端无 world、
  WorldContext 越界拒绝、重复 targets 压缩和既有动作协议。
- Unity 2022.3.38f1：Runtime/Test Assemblies 编译成功，无 C# 编译错误。
- TRAE 沙箱禁止 Unity 写全局 `CoreBusinessMetrics.db`，导致定向测试未进入执行并且
  未产出 XML。不要把该结果记录成测试通过。
- 尚未执行真机对话。下一轮设备验收必须记录 payloadBytes、worldRevision、
  Gateway totalTokens、请求耗时，并确认 Room Capture 与 Spatial Mesh 的
  observed/estimated 标记符合实际来源。

## Phase Turn Idle 技术交接：连续转向与 Animator 退出（2026-10-23）

### 1. 所有权与流程

`AvatarMotor` 是原地朝向和转向动画参数的唯一所有者。新增
`IsTurnStepActive` 属性和 Animator Bool `TurnStepActive` 表示真实转向过程；
它与 `TurnInPlaceMotion.IsTurning` 不同：求解器进入小角度收尾后可能已停止
大角度转向，动画仍须保持至朝向误差 ≤1°。

1. 累积误差 ≥32° 后，电机设 `TurnStepActive=true` 并发左右触发器。
2. 播放至归一化时间 .96 前，按现有速度和脚部 RotationScale 调整根朝向。
3. 尾部阶段保留片段尾姿、.6s recovery；≥48° 再补一步，<48° 以 28°/s 收尾。
4. 补步以 .14s 直接进入同侧/另一侧 TurnStep，允许受电机触发约束的 self transition。
5. 尾部角度 ≤1°且求解器停止后，电机释放所有权；Animator 用 .16s 回到
   Locomotion 或 SocialAttention。显式取消和移动也走 `ResetTurnMotion`。

### 2. Animator 与随机待机约定

- TurnStep 出口使用 `TurnStepActive IfNot` 和 SocialAttention 条件；
  `hasExitTime=false`。完成片段检查在电机中，避免在非循环状态里因归一化时间
  超过 1 后等待下一次 exitTime 判定，或忽略已经发生的完成事件。
- `AvatarLocomotionBuilder` 与保存的 `Assets/MateSoul/Content/Avatar.controller`
  必须同步。更新脚本而未更新控制器会缺参数，不能作为完成交付。
- `AvatarIdleVariation` 在真实 `IsTurnStepActive` 时暂停计时。不要重新增加
  `IsMoving` 或存留 NavMesh 路径作为随机待机准入条件，避免恢复之前修复的饥饿问题。
- Bind、HoldFacing、移动、接触姿态等生命周期统一清理所有权和触发器；
  业务系统继续通过 action coordinator 提交动作。

### 3. 资源与验证

修改文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarMotor.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarIdleVariation.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/AvatarLocomotionBuilder.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Avatar.controller`

左右非循环转身片段、正式 Prefab 与模型保持；脚部 Rig 本身未修改。
9 组手动时钟回放和 4 组正常 Play Mode 通过，提前 Idle 间隙 0s，Play Mode 最大
支撑脚位移 4.81–6.07mm。取消/走路打断、随机 Idle 恢复由手动回放单独验证。
详细证据与边界：
`/Users/bytedance/MateSoul/项目沉淀资料/Lengyue_转向Idle衔接分析_20261023.md`。

当前 Animator 片段尾部本身带收回姿态，所以头显仍需验收补步恢复的自然度；
此修复不等于连续作者转身动画已完成。未构建 APK，未产出 NUnit XML。
`Assets/MateSoul/Tests/` 被 `.gitignore` 排除，此次本地测试检查修改需要在未来
整理测试版本管理时一并处理。

## Phase Startup Optimization 技术交接：预取锚点与启动计时（2026-10-23）

### 1. 初始化流程与所有权

1. `GameBootstrap.InitializeAsync` 读取 `JsonGameSaveStore`，由存储边界验证存档。
2. 创建唯一锚点服务，使用 Bootstrap 生命周期 token 发起 `PrepareRoomAnchorAsync`；
   立即开始房间加载，不等待锚点任务完成。
3. 房间、导航和角色绑定完成后创建 `GameSession`，传入可选的 `initialSave` 与
   `preparedRoomAnchor`。没有预读参数的既有调用者继续从 store 读取。
4. `RestoreItemsAsync` 通过 `LoadRoomAnchorAsync` 消费预取任务。暂停取消只结束
   恢复等待，尚未完成的预取任务保留；再次恢复使用原结果，不重复请求 Provider。
5. 已完成的预取任务被消费后清除。定位失败仍走既有存档房间姿态验证、回退和回滚；
   后续显式重试可重新请求锚点服务。
6. 启动失败取消 Bootstrap token；成功组装后 Session 拥有锚点服务的 Dispose。
   Bootstrap 观察早期任务异常，避免房间加载失败时遗留未观察的任务异常。

不要将锚点定位搬到 `Task.Run`，不要让业务系统绕开 action coordinator 操作角色。
`preparedRoomAnchor` 的定位操作取消由 Bootstrap 生命周期负责；Session 的恢复操作
token 控制本次等待及恢复事务。

### 2. PICO 锚点事件

`PicoSpatialAnchorService.LoadAsync` 在启动 SpatialAnchor Provider 前订阅
`PXR_Manager.SpatialAnchorDataUpdated`，以版本计数和 TaskCompletionSource 避免查询
期间遗漏更新。空结果等待下一次更新或约 2 秒数据等待到期，再做最终查询。

- 空查询或一次空数据更新不代表 UUID 永久失效，不删除存档或跳过姿态验证。
- 约 2 秒限制针对 Provider 启动后的空数据重试等待；不是整个 SDK 操作的硬实时上限。
- 原生查询沿用 SDK cancellation token；事件在 finally 中退订，操作结束后由所有者
  清理锚点句柄和 Provider。
- 保留匹配句柄，释放本次查询中未采用的句柄；不销毁服务已经持有的句柄。
- 本轮模拟器实际触发一次空数据更新，新包 3 次空查询，旧包 6 次。
  真机有效 UUID 与非空数据更新还需验证。

### 3. 非关键启动工作

`ResponseEmotionAudio.Attach` 只绑定 Conversation / SpontaneousReplies；
`EnsureLoaded` 在首次使用某种情绪时通过 Resources 加载并缓存该池。
显式 `Configure` 仍校验音频池，`ClipCount` 会触发该池加载。
首次播放延迟需要后续验收。

Seating/Dialogue 探针和 `RealityMeshSource` 的网格 JSON 导出由
`MATESOUL_DEVICE_PROBES` 编译符号显式开启，并仍要求 Development。
默认移除探针工作，实际空间网格、现实交互和遮挡不受影响。

### 4. 时间口径和构建入口

- `MATESOUL_STARTUP stage=... elapsedMs=...` 是从 Bootstrap.Start 开始的阶段时钟。
  SDK Provider 支持可选 `Action<string>` 回调，记录子系统、查询与快照完成边界。
- `ReportStartupReady` 在恢复尚未结束时不发 READY；恢复结束或明确失败后报告一次。
- `first_main_camera_render` 来自 URP `endCameraRendering`，不能代表头显实际显示。
- 业务总耗时仍采用设备单调日志的 Activity START → `MATESOUL_READY`。
- 后续使用 `MateSoul.Editor.GameSceneBuilder.BuildCurrentSceneAndroidDevelopment`
  或 `BuildCurrentSceneAndroidRelease`，输出
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`。
  新入口直接构建已保存的 `Assets/MateSoul/Scenes/MateSoul.unity`；
  原 `BuildAndroid` 入口仍会重建场景，角色合入后不应混用。

### 5. 验证和交付

- Android Development ARM64 / IL2CPP 编译、APK CRC、9 项 Unity 回归和 8 条预取
  边界断言通过。边界脚本位于 `/tmp`，不是新增仓库 NUnit 用例。
- 同一 12 件存档的两包各 5 次正式模拟器冷启：
  READY 中位数 15.138 → 10.296s，网格后等待 3.601 → 0.306s。
  两包包含其他既有差异，不能把整体收益全部归因于本阶段。
- 存档、偏好、ADB 身份已恢复；模拟器退出、正式 Prefab / Animator Controller /
  Scene 三项哈希保持均已确认。默认 APK 是已验证的新包，旧包备份保留。
- 剩余重点为约 4.859s 的引擎标记到 Bootstrap 区间、1.099–6.015s 的房间数据等待、
  真机 Release 和 Unity 对象级内存快照。短暂尾部 PSS 降低不等于峰值或泄漏已解决。
- 报告绝对路径：
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_冷启动优化与复测_20261023.md`。
  数值、精简阶段日志、回归 XML 与恢复记录：
  `/Users/bytedance/MateSoul/项目沉淀资料/性能测试/20261023-startup-optimization/`。

## 启动前生命周期计时与二进制 Profiler 交接（2026-10-24）

### 1. 边界与职责

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/StartupTimeline.cs`
只在 Development Player 执行，`BeforeSplashScreen` 启动 Stopwatch；
`MATESOUL_TIMELINE` 的 `sinceBeforeSplashMs` 统一覆盖场景前后、Bootstrap Awake /
Start、HUD 字体加载与角色激活。原 `MATESOUL_STARTUP.elapsedMs` 的起点仍为
Bootstrap.Start，旧数据解析器无需换口径。

启动时注册一个 PlayerLoop 观察回调及 sceneLoaded / onBeforeRender 事件；
业务 READY 后两帧注销，应用退出也注销。它不执行玩法或驱动角色。
所有 Unity API 均在主线程调用。Unity 2022.3 的 `SplashScreen.isFinished`
绑定未声明线程安全，不能移到 Task.Run 或后台线程强行提高采样精度。
观测到 false 后第一次 true 时输出 `splash_finished_observed` 和
`lastUnfinishedMs`，两者只能给出结束时间区间；若首次回调已经为 true，
不能把那个时刻当成真正结束时刻。BeforeSplashScreen 不等于实际画面开始。

### 2. 独立 Profiler 采样

Development 包检测
`Application.persistentDataPath/startup-profile.enabled` 时启用二进制录制，
输出同目录 `startup-profile.raw`，READY 后两帧或 30 秒停止。常规计时需移除
开关文件，避免把录制开销混进性能样本。

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/StartupProfileExport.cs`
提供批处理方法 `MateSoul.Editor.StartupProfileExport.Export`。
参数为 `-startupProfile <raw绝对路径>` 和
`-startupProfileOutput <json绝对路径>`，通过 ProfilerDriver 加载原始录制并导出
帧、线程、样本名称、父索引及历时。前四帧导出全部样本，其余导出至少 1ms 的
样本；历时包含嵌套子项，不能直接相加为 CPU 总成本。
Unity 2022 默认只导入最后 300 帧，本工具通过反射调用
`ProfilerDriver.SetMaxFrameHistoryLength(2000)`，仅设置本次批处理进程的原生容量，
不写 EditorPrefs。部分原生样本有元数据值却没有描述数组，须允许描述为 null，
保留名称与历时。完整录制已成功导出 0–404，不能用最初的 105–404 导出做前置归因。

### 3. 验证状态与续测限制

- Android Development 构建、ZIP CRC、Editor 编译均通过，正式 Scene / Prefab /
  Controller / ProjectSettings 哈希未变；计时包已完成 5 次正式启动、
  1 次预热及 1 次独立录制，7 次完整恢复并通过 READY 后存活检查。
- 独立包：
  `/Users/bytedance/MateSoul/Builds/MateSoul_StartupTimeline_20261024.apk`，
  SHA-256 `5042d715237c6a02db5e5c8f035d4483f6bd62018f5971934d7fefccb26b07cf`。
  基线结束时默认 Framework 恢复此前验证包，SHA-256：
  `86a9d240f933d60f7df47aa548eed310692663d11f09fe4def3e3471b56a9892`；
  后续已更新为下一节的重叠验证包。
- 直接启动 QEMU 的 `HV_DENIED` 已通过 LaunchServices 解决：
  `open /Users/bytedance/MateSoul/scripts/start-pico-emulator.command`。
  不应把启动器返回 0 当作设备启动成功，须核实 QEMU 和 ADB。
  本轮结束存档、偏好、默认安装包、shell 身份已恢复，QEMU 退出确认。
- 引擎到 Bootstrap 5.030s 仍是墙钟。单独录制的首帧资源锁 1526.859ms、
  水印 GPUProgram 等待 708.113ms；后台预加载 1760.316ms 与等锁重叠，
  不得重复计入。未映射到具体 FBX / 贴图，不猜测角色资产的独立成本。
- 启动重叠已完成 Android 验证，最终通过 `GameBootstrap.cs.meta` 导入顺序 -100
  和通知 UI 的 Start 生命周期实现；不保留重复的代码属性声明。
  基线 HUD 为 124ms，但最终确认的房间请求前等待减少为 85ms，不能直接报 124ms
  整体启动收益。物品准入与恢复合同保持原逻辑，详见下一节。
- 本次工具与备份在 `/tmp/matesoul-startup-phases-20261024/`。
  `probe_phases.py` 支持 backup / run / restore，限制为 emulator-5554，
  每次输入恢复做哈希校验、检查真实 READY、READY 后存活 12 秒及物品身份保留。
  运行工具前仍须先安装独立计时 APK。

## 房间读取与 UI 初始化重叠交接（2026-10-24）

### 1. 当前实现

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameBootstrap.cs.meta`
  的 MonoImporter.executionOrder 为 -100，Bootstrap.Start 先于默认顺序的 UI 组件。
  这是脚本的 Unity 执行顺序设置，亦影响该脚本的其他生命周期方法。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/SystemNotificationPresenter.cs`
  的 Bind 只绑定 SystemNotifications；Start 加载共享字体、创建 UI、订阅 Changed
  并 Refresh。启动前产生的消息保留在数据源，由 Refresh 展示。
- Bootstrap 仍统一创建会话、读取房间、构建导航、恢复物品并报告 READY。
  提前请求后再执行 HUD / 通知的同步字体加载；它们仍运行在 Unity 主线程。
  没有另建启动执行器或改变角色动作协调器的所有权。
- 新增观测点为 notifications_start、notifications_font_load_start/end、
  notifications_ready；仍复用 StartupTimeline 的单一 Stopwatch。

### 2. 包内顺序与缓存验证

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/GameSceneBuilder.cs`
的 BuildCurrentSceneAndroidDevelopment 现在使用 Development | CleanBuildCache。
该入口只构建已保存场景，不调用资产生成器；本次干净构建耗时约 527 秒。

本轮两个增量包的 APK 整体哈希已变化，但包内 MonoScript.m_ExecutionOrder 仍是 0。
必须核验实际序列化数据，不能仅查看源码属性、.meta 或编译成功标记。
用 UnityPy 合并 APK 的 globalgamemanagers.assets.split0…split5 后，
读取 GameBootstrap 的 MonoScript；干净构建后的包内 order=-100，
HUD 与通知组件仍为 0，运行日志亦确认读取先于两类 UI。
UnityPy 安装在任务独立环境：
`/Users/bytedance/MateSoul/.unity-tmp/startup-inspection-env/`。

构建前关闭模拟器，长命令需超时和进度检查。中止构建可能留下 XR 构建回调临时加入的
ProjectSettings.preloadedAssets 条目；本次已对照构建前副本，只恢复该差异，
主场景、Prefab、控制器及 ProjectSettings 最终哈希均一致。
不要从 Git 整体覆盖用户的已有设置或运行角色资产生成器。

### 3. 实测口径与限制

- 同存档预热 1、正式 3、独立录制 1；5 次恢复 12 件物品且身份保持，
  READY 后存活 12 秒。普通 4 次读取顺序与恢复准入检查全部通过。
- 两端均为托管标记时，使用 sinceBeforeSplashMs 作差。
  AfterSceneLoad → room_load_start 基线中位数 138ms，最终 53ms（33–54ms），
  减少 85ms。正式组 READY 11.709s（11.495–12.626s）是观测值，
  跨干净构建和环境波动，不能认定总差值为这一修改的收益。
- 正式 HUD / 字体为 338 / 269ms，中位数 339ms 的 UI 历时落在房间等待区间内。
  字体本身没有被优化。额外录制 HUD 为 134.131ms、ReadObject 为 93.184ms，
  三个不重叠 File.Read 合计 91.380ms；未重现正式高值，尚未解释全部波动。
  嵌套历时不重复相加，读操作的墙钟不等于 CPU 开销。
- UI 回归在 GameRuntimeTests.cs:2435 材质断言失败，新旧通知初始化实现都相同。
  前面的消息数量、顺序和透明度断言通过，整个用例仍未通过。
  TMP 子网格与该材质断言的适配另行处理。

### 4. 当前交付与恢复

默认 `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk` 与独立
`/Users/bytedance/MateSoul/Builds/MateSoul_StartupOverlap_20261024.apk`
及设备安装包哈希一致：
`08ba06dcba77c678e398da6e5fad91d4f3c0be63094a447b2c08de4d76103f69`，
182999478 bytes。Android ARM64 / IL2CPP、ZIP CRC、包内顺序及保护资产核验通过。
存档、偏好恢复，ADB uid=2000，设备 Profiler 文件移除；QEMU PID 71092 与 Unity 退出确认。

报告为 `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_冷启动优化与复测_20261023.md`
第 10.5 节；数值、缓存证据、失败对照 XML、Profiler 摘要及默认包更新校验在
`/Users/bytedance/MateSoul/项目沉淀资料/性能测试/20261024-startup-phases/early-start/`。
完整录制及中间文件仍位于 `/tmp/matesoul-startup-phases-20261024/early-start-final/`。

## 真机房间标定能力与设备识别交接（2026-10-24）

### 1. 能力配置与构建入口

`/Users/bytedance/MateSoul/MateSoul/Assets/Resources/PXR_ProjectSetting.asset`
的 `sceneCapture`、`planeDetection` 已设置为 1；既有 `videoSeeThrough`、
`spatialAnchor`、`spatialMesh`、`environmentDepth`、`handTracking`、
`eyeTracking` 保持启用。能力开关只声明应用能力，首次使用仍需用户授予空间数据权限。

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/GameSceneBuilder.cs`
的 `ConfigureInteractionCapabilities` 与 `ConfigurePicoBuildSettings` 设置房间标定、
空间网格、平面检测等能力。`BuildCurrentSceneAndroid` 在构建前调用前者，
使保存场景的打包入口也具备对应配置。

真机首次功能验收使用
`MateSoul/Game/Build Current Scene Android Development APK`。该入口只构建已保存的
`Assets/MateSoul/Scenes/MateSoul.unity`，不调用场景、角色或动画生成器。
默认生成 `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`。
当前对话网关为 `http://127.0.0.1:8787/v1/conversations/reply`，
Release 校验仍要求 HTTPS，不为此次房间验收放宽网络策略。

### 2. 统一设备识别

新类路径：
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/RoomNavigation/Runtime/PicoDeviceEnvironment.cs`。

`PicoDeviceEnvironment.IsEmulator` 在首次访问时读取 Android Build.MODEL、
PRODUCT、FINGERPRINT，并结合 Unity 的 deviceModel / deviceName，只检查明确的
`emulator` 标识；结果在进程内缓存。Android 身份读取失败时只检查已有 Unity 身份，
不会将 `swan_arm64` 硬件名称作为模拟器证据。

同一结果用于以下三处：

- `GameBootstrap.InitializeRoomAsync`：模拟器选择 Spatial Mesh Provider，
  真机选择 `PicoSceneCaptureRoomMapProvider(true, TraceStartup)`。
- `RoomNavigationBootstrap.InitializeAsync`：独立导航场景使用相同识别。
- `RealityOcclusionMeshRenderer.EnvironmentDepthAvailable`：
  编辑器和明确识别的模拟器不启动原生环境深度，真机保留该路径。

首次判断输出 `MATESOUL_DEVICE kind=... model=... product=... unityModel=...
name=... fingerprint=...`。此日志用于核对实际设备分支，不等价于 SDK 能力探测。
SDK 启动、权限和地图质量失败仍沿既有启动错误路径处理，不回退 Mock 房间。

### 3. 真机操作与验收

1. 在头显开启 USB 调试，连接 Mac 并允许电脑调试，确认 ADB 状态为 `device`。
2. 使用设备序列号指定 ADB 目标，安装本次 APK；先读取 MODEL、PRODUCT、
   FINGERPRINT 核对设备身份，不要求用户维护机型白名单。
3. 戴上头显，在实际房间启动应用并允许空间数据访问。查询成功且 Scene Anchors
   为空时，应用发起系统 Room Capture；已有数据时直接复用。
4. 按系统引导扫视和走动，覆盖地面、墙体与家具，完成保存后返回游戏。
5. 确认 `MATESOUL_DEVICE kind=headset` 与
   `MATESOUL_READY source=PICO Room Capture`，检查地面高度、家具绕行、
   物品落地和跨启动锚点恢复。人工扫描耗时与自动启动处理分别记录。

Room Capture 生成的是系统房间语义标定数据，不需要把 OBJ/JSON 手工导入游戏。
真机启动快照仍由 Floor Polygon、Wall Box2D 和家具 Box3D 组成；
原生 Spatial Mesh 遮挡和椅床表面探测不等价于全房间精细 MeshCollider 已接入。
现有代码只在查询为空时主动发起标定，没有游戏内强制重新标定按钮。

### 4. 验证证据

- 8 项 Android 标识模拟回归通过，覆盖共享 Swan 硬件名、真机与模拟器身份、
  fingerprint 标识、JNI 失败、空字段和缓存一致性；工具在临时目录，
  不改项目既有测试文件，亦不声称完成物理头显验证。
- Android Development ARM64 / IL2CPP 干净构建通过，进程退出 0，
  总进程历时 489.52 秒。新包仅含 arm64-v8a；签名、ZIP CRC 和 IL2CPP
  metadata 内的 `PicoDeviceEnvironment`、`MATESOUL_DEVICE` 标记核验通过。
- APK Manifest 中 `enable_vst`、`enable_anchor`、`enable_spatial_anchor`、
  `enable_scene_anchor`、`enable_mesh_anchor`、`enable_plane_detection`、
  `enable_environment_depth` 均为 1，`SPATIAL_DATA`、`HAND_TRACKING`、
  `EYE_TRACKING` 权限均已声明。
- 主场景、`Lengyue_Runtime.prefab`、`Avatar.controller` 构建后哈希与基线一致，
  ProjectSettings 和 EditorBuildSettings 字节内容亦未改变；原有工作区变更保留。
- 独立包 `/Users/bytedance/MateSoul/Builds/MateSoul_RealDevice_20261024.apk`
  与默认 Framework APK 哈希相同，183001762 bytes，SHA-256：
  `8bc2ccdaf4aa9f6fd5b2368faf300a945295673ae6dbcf0b390466ca79c3d56c`。
- 包体与资产核验记录：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/real_device_room_capture_20261024.json`。
- 构建后 ADB 列表仍为空，真实设备型号、系统版本、Room Capture 数据质量
  和跨启动锚点恢复仍须设备连接后核对。

## 真机启动版权校验与包名交接（2026-10-24）

### 1. 实际故障边界

USB 授权后真实头显识别为 B3110 / swan / Android 16。
`MateSoul_RealDevice_20261024.apk` 安装成功，设备 base.apk SHA-256
与交付一致。但使用 Unity 模板包名启动时，系统 Matrix 拦截正常 Activity 启动，
显示“版权校验不通过：签名非法”，并调用 force-stop 结束应用。

日志证据属于 `AppStartInterceptManager`、`com.bytedance.pico.matrix` 与 PromptSDK；
不属于 Unity GameBootstrap、Scene Capture 或游戏网关。Android apksigner 的 v2
签名验证通过只证明 APK 文件签名有效，不能证明 PICO 平台版权校验必然通过。

`PXR_PlatformSetting.asset` 的 `startTimeEntitlementCheck=0`、appID 为空；
旧 APK Manifest 无 `pvr.app.id`。不要凭系统提示重复切换应用内版权校验、
伪造 appID、修改头显 Matrix 组件或关闭系统校验。

### 2. 新应用标识

`/Users/bytedance/MateSoul/MateSoul/ProjectSettings/ProjectSettings.asset`
的 Android applicationIdentifier 现在为 `com.matesoul.game`。
保留原签名证书，使用正确的独立包名进行正常安装和 Launcher 启动验收。
`/Users/bytedance/MateSoul/scripts/diagnose-pico-memory.command` 的 PACKAGE 同步更新。

旧包名仍仅用于历史证据：
`com.UnityTechnologies.com.unity.template.urpblank`。
不同包名对应不同 Android UID、私有目录、外部应用目录和应用锚点身份，
旧存档不会自动进入新应用。未卸载旧包、未清除用户数据，
后续若需迁移必须显式核对存档、锚点所属应用与定位结果。

### 3. 构建和复验

继续使用 `GameSceneBuilder.BuildCurrentSceneAndroidDevelopment`，
只打包已保存主场景，保留用户的锐化开关和资产现状。核验新包 Manifest、
Android 签名证书一致、ZIP CRC、空间能力声明和主资产哈希，
然后安装至实际头显，通过正常 Launcher Activity 启动读取日志。

若独立包名仍被系统校验拒绝，应使用 PICO 开发者平台匹配包名、签名和组织帐号，
不能把签名轮换、关闭系统 Matrix 或伪造授权作为整改方案。
本次正常启动已未再出现签名非法，并进入新应用权限提示；原始设备日志保存在系统临时目录。

### 4. 实际复验结果与后续操作

- 新包：
  `/Users/bytedance/MateSoul/Builds/MateSoul_RealDevice_PackageFix_20261024.apk`，
  包名 `com.matesoul.game`，183001250 bytes，SHA-256：
  `53a8586b316556723b88926694f8b292d2f70b3cbfd77237746b8229e7c84a78`。
  默认 Framework APK 已更新，新包不覆盖旧包名私有数据。
- Android 签名证书 SHA-256：
  `0799ed95a5c84fcc7d5eb21dfa5e587dbc8650b1ac54dc50201de4540711ea97`，
  与旧包相同；APK CRC、空间能力声明及保护资产校验通过。
- 真机 `adb install -r` 成功，设备 APK 校验与本地相同；
  使用 MAIN / LAUNCHER 正常启动入口返回 ok，应用 PID 23107 存在。
  初次观察期间头显休眠，唤醒后收到新包视线追踪权限提示；
  本轮没有 Matrix 签名非法提示，没有关闭系统校验服务。
- 启动观察未到业务 READY，头显自动休眠，新包的空间和追踪权限仍未授予。
  必须戴上头显，选择允许应用访问所需权限后再验收 Room Capture，
  不把安装成功、Activity 创建或权限提示当成房间就绪。
- 主场景、角色 Prefab、动画控制器哈希保持一致，PXR 配置与任务开始时相同。
  构建回调临时加入的 preloadedAssets 已由 Unity 正常恢复，
  ProjectSettings 最终只保留 Android package 的本次差异。

## 真机 HUD 和离线启动取证交接（2026-10-24）

- `GameHud.Recenter` 只从 HMD 水平朝向确定目标，面板默认 0.65m、
  眼下 0.12m； yaw 30°/位移 0.2m 才更新目标。交互期间冻结目标，
  防止玩家低头或手指靠近时按钮继续逃离。现有页面、UI toggle 和游戏状态沿用。
- `AlwaysOnTopUi` 为兼容现有调用保留类名，但当前材料不再置顶：
  UI shader 用 Transparent + `unity_GUIZTestMode`，
  SDF 材质改为 `MateSoul/Spatial UI SDF`，文字 `isOverlay=false`。
  背景和文字均采样 SDK Environment Depth，不能仅依赖屏幕合成中的不透明深度，
  因为 Canvas 不写场景深度。关闭环境深度时仍按 Unity 几何深度渲染。
- PICO SDK 中 `PicoHandInteraction`/`PXR_HandSubSystem` 受 `XR_HANDS` 控制；
  开启 PXR 的 handTracking 开关不能替代 `com.unity.xr.hands` 依赖。
  项目安装 1.4.3，适用于当前 Unity 2022.3，未升级 Unity/XRI。
- `PlayerInteractionModeController` 读取 SDK 手部设备状态并安装左右 `PicoHandPoke`；
  poke 使用 XRI UI 输入模型，不另写按钮事件或手势业务状态机。SDK 当前
  `WriteOptionalPose` 只写 pose position/rotation，没有填充嵌套 pose tracking 字段，
  因此点按读取 device 的追踪状态，并检查姿态旋转已初始化。
- `PicoHandInputSetup` 是 SDK 布尔布局的项目兼容边界。SDK 使用 `format=BYTE`
  且队列发送 bool 原始字节 0/1，Input System BYTE 轴会将 1 归一化为 1/255，
  `isPressed` 不成立。布局为布尔控件增加 `scale(factor=255)` 数值恢复处理，
  保留单字节事件大小和既有布局；不修改共享 `XR/` 目录。
  禁止只把模拟输入写为 255 后宣称 SDK 事件正常。
- `DeviceStartupLog` 只在 Android 工作，自动轮转两份启动记录，每份 128KiB 上限。
  路径为 Unity persistentDataPath 下 `diagnostics/startup-{current,previous}.log`。
  记录 `MATESOUL_*`、`[RoomNavigation]` 和异常，不记录聊天消息或相机图像。
- 精确异常 `Room obstacles removed the complete walkable surface.` 表示
  障碍烘焙没有可行走区域而 floor-only 烘焙成功；发生在角色启用和 session restore 前。
  没有真实房间证据时不要忽略所有障碍或删除存档掩盖失败。
- 本地验证和真机待验收项详见
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_UI与房间启动问题_20261024.md`。
- 交付 APK：`/Users/bytedance/MateSoul/Builds/MateSoul_SpatialUI_20261024.apk`；
  Development ARM64/IL2CPP，保留 `com.matesoul.game` 与既有签名证书，
  可通过正常 `adb install -r` 覆盖同包名旧版本并保留存档。默认 Framework APK 同步。
  构建/CRC/签名/受保护资产验证通过；真实手遮挡和房间故障仍需设备证据。

### 真机导航失败的证据边界

- SpatialUI APK 已通过正常 `adb install -r` 安装和 Launcher 启动，设备包哈希一致，PID 29505。应用进程和 Activity 就绪不能等同于 `MATESOUL_READY`；本轮房间初始化仍失败。
- 旧日志保留 16:53:55 的 `MATESOUL_NAVIGATION_FAIL`。启动失败后 `Room.Clear()` 和探针未初始化会造成 `source=""`、floorArea/navBuild/frame 等为零，不用这些零值推断权限或地板缺失。
- 新日志为 `PICO Room Capture` / floorArea=5.48 / obstacleSources=18 / vertices=0，floor-only 采样成功的异常分支再次运行，故障位于障碍导航烘焙。
- 本轮日志没有 anchor_start，因此旧房间锚点准备不是这次复现的必要条件。
- 所有家具旋转为零。SDK 的 SceneCapture 示例不使用 Box3D 返回的局部旋转，注释为 currently rotation not support；provider 却将其直接乘入 anchorRotation。下一次修复必须记录原始 anchor/local quaternion 并验证适配，不能按猜测交换尺寸轴或忽略全体障碍。
- `DeviceStartupLog` 已在真机落盘，文件位于 `/sdcard/Android/data/com.matesoul.game/files/diagnostics/startup-current.log`。用户测试期间只读取日志，保留运行实例和存档。
- 数值记录：`/Users/bytedance/MateSoul/项目沉淀资料/validation/headset_ui_room_device_20261024.json`。

### HUD 跟随修订与导航诊断进行中

- HUD 以可见panel的rect.center定位而非Canvas中心，默认向下18°、距离0.65m。
  HMD elevation乘0.25后限幅±8°，目标角为-18°加该跟随角。head-down35°时
  面板自身只下移8°，用户仍能低头靠近视野中心查看。
- 只有pointer select或近距离poke悬停才暂停follow，避免EyeHand gaze悬停让面板永远不跟随。
- 展开页锚定底部y=200，高330；字幕y=540，高64，导航y=0、高160，
  均在920×620 Canvas内。可见展开页与导航分别按自身中心定位。
- 房间provider增加MATESOUL_ROOM_FLOOR和MATESOUL_ROOM_BOX，记录原始anchor/local姿态，
  用于确定无效局部四元数、虚拟墙或安全半径是否造成全部导航消失。当前未修改导航业务策略。
- 原始几何诊断包已安装，PID31592；头显Asleep时进程存在但不会进行Unity/XR启动，
  不能把60秒无日志当成修复或新崩溃。需佩戴唤醒后继续。

## Phase H1 技术交接：直接手部输入（2026-10-24）

- 真机默认输入为 `PlayerInteractionMode.Gesture`，枚举旧数值保留用于已有
  序列化与 AI context；设备不再自动切换 Controller/EyeHand。
  `PlayerInteractionModeController.Awake` 禁用旧射线对象，在同一 tracking
  origin 下创建两套官方手模型、掌心交互器和指尖 poke。
- `PicoTrackedHand` 执行顺序 -200，早于 XRI manager 的 -105。
  `GetJointLocations` 同时校验成功返回、isActive、PositionValid、
  OrientationValid、PositionTracked 与有限坐标/旋转。
  掌心与指尖转换到世界坐标；`graspValue` 采用握紧 .75 / 松开 .35。
  `PicoHandInputSetup` 的 BYTE 布尔值兼容处理必须保留。
- `PicoDirectHandInteractor` 通过 7 cm 邻域、表面距离与实体阻挡取得最近目标。
  只向 XRI 提供选择意图，不自行操作 Rigidbody。要求新张手→握紧；
  保持选中对象有效，每手最多一个物品；失追踪、进入放置后清空握取准备状态。
  实现 `IHandHoldPoseProvider`，继续接入角色牵手的动作协调器。
- `PhysicsGrabInteractable` 对直接手禁用射线 snap，使用实际掌心作为初始
  相对 attach。`PhysicsBody.EndFollow(discardVelocity)` 仍是统一释放边界：
  正常松手保持真实物理速度，跟踪丢失不沿用抛出速度。
- `PicoHandPoke` 使用已验证指尖位置和末节指骨朝向，深度 2.5 cm。
  持物/握紧关闭该手 UI；另一手仍可操作。无需为手指添加会推飞物体的动态刚体。
  UI 准入使用 `!Closed`，不要求 graspValue 接近零，以允许伸出食指时其他手指自然弯曲。
- 家具确认事件 `ItemPlacementPreviewController.ConfirmRequested` 交给
  GameHud 原有 PlaceAsync；固定使用同一只手，中途失追踪不切换另一只手。
  水平面手掌向下投影最多2m；墙面限制掌心附近；预览关闭释放 PlacementActive。
  `Cancel` 只释放已激活的物品预览；HUD 切页不能清除小游戏的放置状态。
  新物品预览和 MiniGameHost 启动均在输入边界阻止与已有放置任务重叠。
- 官方手仅在实例上移除 RayPose；资源位于 SDK `Resources/Prefabs/HandLeft|HandRight`。
  项目手部 shader 位于
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/Resources/MateSoulHands/TrackedHand.shader`。
  正常深度测试和写入，无描边，不会移除 passthrough 真实手。现有环境深度
  后处理仍可能遮挡手模型，必须在真机检查而非全局关闭遮挡。
- `DirectHandValidation.Run` 在单独 Unity batch 进程运行，驱动真实 XRI manager
  和 PhysicsBody，验证选择、竞争、偏移和释放；`ValidateAndBuild` 追加构建独立
  `Builds/MateSoul_DirectHands_20261024.apk`。不重新生成主场景或角色资产。
- SDK 初始化 `PXR_Utils.SetDefaultDefineSymbols` 可在资源导入期间调用
  `PXR_ProjectSetting.GetProjectConfig`，误创建默认设置，导致 handTracking/MR
  标记归零。构建前必须调用项目既有 ConfigureInteractionCapabilities，
  复核 HandsOnly 和环境感知配置。不要仅修改 YAML 后假定构建产物正确。
- 完整取舍、验证证据与真机清单见
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_直接手部交互改良_20261024.md`。
- 最终23项回归与Android构建通过，产物签名/CRC/ARM64/IL2CPP/HandsOnly均已
  实检；数值与受保护资产哈希记录在
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/direct_hands_20261024.json`。
  当前未安装新包，自动注入输入的回归不能作为真实手势或视觉效果已通过的证据。

## Phase 技术交接：真实Scene Capture导航修复（2026-10-24）

- `PicoSceneCaptureRoomMapProvider.BuildSnapshot`负责SDK姿态组合边界。
  当前SDK可返回缺失的Box3D局部旋转`(0,0,0,0)`：模长平方大于0.0001时归一化，
  否则使用Quaternion.identity，再与anchorRotation相乘。
  同一修正姿态同时用于RoomObstacle和RealityItemObservation，不改SDK尺寸轴。
- `RuntimeRoomNavigation.Build`直接使用obstacle.Size参与烘焙，物理碰撞与导航保持
  同一几何；NavMesh的agentRadius已经承担角色体积侵蚀，删除原额外0.08m边距。
  agentRadius=.25m、height=1.65m、climb=.2m、slope=45°、minRegionArea=.25不变，
  overrideVoxelSize开启、voxelSize=.02m。旧场景残留序列化字段无效，
  无需为清理该字段重新生成已审核场景。
- 真机捕获数据回放必须保留原全部18个障碍。只修旋转、只降体素或只删除虚拟墙均
  不能通过；删除重复膨胀与2cm体素组合通过。最终生产代码验证8个导航顶点和4个
  无碰撞站立样本。有效导航中心面积0.01724㎡，该值不代表角色实体占地面积，
  也不代表房间具有足够散步空间。
- HUD最终以可见导航栏或展开页rect.center定位，目标下方18°、距离.65m；
  头部elevation乘.25后限幅±8°。只有select或近距poke暂停跟随，普通注视不暂停。
  保留最新PicoTrackedHand/PicoDirectHandInteractor与GameHud放置确认订阅。
- 联合包为`/Users/bytedance/MateSoul/Builds/MateSoul_RoomFixed_UIFollow_20261024.apk`，
  已安装并验证设备哈希、签名、CRC、ARM64/IL2CPP与存档保持。
  PICO HandsOnly在实际Manifest中表现为handtracking=1且没有controller声明，
  Scene Capture对应enable_scene_anchor=1，不能用查找字符串“HandsOnly”或
  不存在的“enable_scene_capture”键判断构建失败。
- Activity启动或PID存在不能代替业务READY。此次PID5111在Unity初始化前头显再次
  Asleep，故导航与avatar验证INCONCLUSIVE；拉取的离线startup文件可能仍是上一版，
  必须核对时间和本次日志阶段。继续时先保持头显佩戴唤醒，再检查navmesh_built、
  avatar_bound和MATESOUL_READY，随后保留save/anchor执行第二次冷启动。
- 调试原始几何插桩、临时Unity回放及debug server待实际修复验证后清理，
  不删除其他并发任务的DirectHandValidation或资产文件。
  数值和证据索引：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/headset_ui_room_final_20261024.json`。

### 出生点采样覆盖与连续启动验收

- 真实设备PID8378已通过`RuntimeRoomNavigation.Build`，随后在`GameBootstrap`
  玩家相对出生点采样失败；因此原NavMesh空网格修复已验证，冷月启用仍未完成。
- `GameBootstrap.InitializeRoomAsync`的首选点为玩家前方2.2m，搜索距离必须以该点
  计算：`Vector3.Distance(spawn, snapshot.Bounds.center) + snapshot.Bounds.extents.magnitude`。
  原半对角线只适用于以房间中心为原点的覆盖，不能直接复用给外部目标。
  新算法仍只执行一次Room.TrySamplePosition，不忽略障碍，不新增备用出生点状态。
- 临时`MATESOUL_SPAWN_SEARCH`打印目标、bounds.center和新旧距离；
  `MATESOUL_SPAWN_FOUND`打印采样成功点。真机闭环后移除这两个region。
  临时SpawnCoverageValidation用实际生产导航和已有捕获数据验证15个组合，
  旧搜索13个失败、新搜索全部通过，再调用已审核场景的构建入口。
- 最新包`/Users/bytedance/MateSoul/Builds/MateSoul_RoomSpawnFixed_20261024.apk`
  已构建安装，SHA-256为
  `8e9a935971fb86dfc19dc4398ca2a20f90ef1a0f404ed044fe3f46ffa85102ce`。
  原用户直接手部与HUD修改保留。构建和设备包哈希、存档保持检查通过。
- 当前保存数据没有roomAnchorId和物品。连续两次READY仅能证明当前房间/存档下的
  重复启动；旧物品恢复需另用实际有锚点的存档验收。
  新包PID9594尚未Unity初始化即再次休眠，不能使用拉回的旧startup文件证明通过。

## Phase 技术交接：HUD 专用眼手输入（2026-10-24）

- `GameHud.UpdateWorldPose` 是设备与回归共用的定位方法：可见页中心0.45m、下方30°，
  头部俯仰×0.25并限幅±8°。`hudInteractionActive` 时提前返回，停止残余插值。
- `HudEyeHandInteractor` 仅实现 `IUIInteractor`，注册于 `XRUIInputModule`；
  `MateSoulHUD` 位于TagManager第8层，所有HUD子节点和下拉模板继承该层。
  新输入只检测此层，不向XRInteractionManager注册，也不实现物品选择接口。
- `PicoTrackedHand` 执行顺序为-300，早于XRI UI的-200；读取PICO `pinchValue`，
  以0.75/0.35迟滞输出IsPinching。Grip/Closed、官方虚拟手和原握取通路保持。
- XRI物品manager为-105，晚于UI。UI调用 `PicoDirectHandInteractor.RefreshProximity`
  获取当前帧接近状态，避免把刚靠近物品时的捏合误认为菜单点击。
- 使用PICO `gazeStatus`、`gazeTrackingState`、`gazePosition`、`gazeRotation`；
  有效状态包含位置和旋转，姿态经输入根节点转换为世界坐标。无头朝向替代眼动。
- 持物/附近物品/握拳抑制同手眼手输入，任一可触碰食指距HUD平面5cm内优先poke。
  按下后锁定发起手，取消时以select=false和空命中发送释放，避免失跟踪被当作点击。
- 构建配置同时保持HandsOnly、eyeTracking=true和房间/环境深度能力。核对最终APK
  `handtracking=1`、无controller声明、眼睛权限及`picovr.software.eye_tracking=1`。
- 临时HUD集成验证在 `/tmp/matesoul-hud-eyehand-20261024`，通过临时Editor链接运行；
  对批处理创建的XRI UI组件显式执行生命周期，测试真实UI射线筛选和点击回调。
  保留原 `DirectHandValidation.cs`，只清理本次自己的临时链接。
- 最终45项检查通过。批处理Canvas需通过URP `SubmitRenderRequest`实际渲染一次，
  才会生成可供XRI检测的Graphic.depth；Editor临时链接修改后必须更新时间戳以触发重导入。
  验证脚本调用生产UpdateWorldPose与UpdateModel，不复制待测算法。
- 最终HUD包与稳定导航包哈希一致，已用Roslyn原始Android PDB的文档SHA-256核对
  5个运行时文件，并对照APK和Gradle的libil2cpp.so。可复用这次联合构建，无需重打包。
  设备已安装且本轮哈希复验通过；PID14107记录HUD距离0.45m与HUD眼手启用。
  数值记录：`/Users/bytedance/MateSoul/项目沉淀资料/validation/hud_eyehand_20261024.json`。

## Phase 技术交接：导航小区域保留与碰撞安全出生点

- `RuntimeRoomNavigation.Build`最终使用minRegionArea=0、voxelSize=.02m。
  NavMesh已经按.25m角色半径侵蚀，`.25㎡`再次剪枝作用于中心可移动面积，
  会删除仍能站立的小岛。Recast的tile边界规则可令小岛偶尔保留，因此仅验证
  单次原点/朝向不足以证明跨启动稳定。两份真实数据各6种旋转/平移验证全部通过。
  旧文档中的minRegionArea=.25保留约定已被本轮证据修订。
- `TryFindSpawnPosition(desired,out spawn)`属于RoomNavigation的出生点选择能力：
  先同步房间碰撞体，取得覆盖完整房间的最近NavMesh点，再考察各导航三角形中心；
  要求位于MainFloor内，并以.25m半径、1.65m高度、.03m起身偏移的胶囊
  验证不与GeneratedRoomGeometry相交，选择离目标最近的合格点。
  完整胶囊校验针对输入装配阶段，只执行一次，不建立运行时恢复状态机。
- GameBootstrap在character.SetActive(true)前使用此能力；姿态初置仍属于Session
  装配，业务移动/动作继续由共享协调器与PhysicsBody拥有。
  不通过缩小角色半径、忽略家具或用floor-only导航掩盖真机障碍问题。
- 最终真机证据在startup-current.log中完整落盘。PID13697部分logcat行丢失，
  应使用匹配本次时间的离线日志补证；从旧进程拉回的日志不能冒充新进程。
  两次成功包含navmesh_built、spawn_found、avatar_enable_end、avatar_bound与READY；
  PID13697/PID14107分别20/19ms烘焙，原存档不变。
- 最新已安装APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_StableRoomStartup_20261024.apk`，
  SHA-256 `bb97c3719f59f1520464351799f56139d5c2cd3cfa6fbf8f3ce167753c398999`。
  保存当前运行实例PID14107。当前源与包保留最新HUD/眼手输入变化。
- 验收边界：持续Awake下连续两次冷启动通过，非全部启动生命周期通过。
  恢复睡眠后QuerySceneAnchor runtime failure、第一次查询空锚点/native capture后
  无地板仍待定位；当前save无锚点/旧物品，未验收旧物品恢复。
  不在没有证据时增加固定延迟、伪造地板或多层SDK重试。
- 本轮临时StableNavigationCheck Editor链接已清理；原始日志/诊断保留用于未解决
  的SDK就绪问题，不清理另一个HUD验证任务。最新记录：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/headset_stable_startup_20261024.json`。

## Phase 技术交接：语义框不可见与实时测量表面碰撞

- 数据职责：Scene Capture 的地板/语义 Box3D 用于初始化导航和保守碰撞，
  不能创建深度遮挡渲染器。PICO Unity SDK 的 Spatial Mesh 提供可用于
  `MeshCollider` 的顶点/索引与位姿；Environment Depth 提供每眼视觉深度纹理。
  运行时精细碰撞不再仅依靠启动时的粗房间箱体。
- `RealityOcclusionMeshRenderer` 保留现有安装入口，统一拥有每个原生 UUID 的
  Unity Mesh、MeshFilter、MeshRenderer 和静态非凸 MeshCollider。通过
  `RoomSurfaceCollider.SourceId = uuid.GetHashCode()` 与家具扫描的来源身份保持一致。
  原生 Added/Updated 更新渲染与物理，Removed 立即停用旧对象再释放网格；
  Unchanged 已存在块不重新创建或烘焙。
- 原生网格仅在环境深度不可用时写视觉深度，物理碰撞始终独立工作。
  只有实际测量得到的 SurfaceMeshes 可作遮挡后备；Scene Capture 语义框及
  地板代理不再进入遮挡渲染。保守语义箱体不会因局部扫描出现而自动删除。
- `RuntimeRoomNavigation.SetLiveSurfaceSources` 只停用与实时来源对应的旧快照
  MeshCollider，移除原生来源时恢复旧测量快照；避免新旧几何双重接触。
  `TryFindSpawnPosition` 同时检查 GeneratedRoomGeometry 与标记的实时表面。
- `GameBootstrap` 装配顺序为 `Room.Build` → `ApplySnapshot(snapshot, Room)` →
  安全出生点搜索 → 启用角色。导航仍以房间地板/语义障碍为基线，
  没有增加每帧导航重烘焙或第二套动作执行器。
- 全屏环境遮挡必须在 `BeforeRenderingTransparents` 执行。
  `_CameraDepthTexture` 不包含默认不写深度的 HUD；如果透明 UI 已画完再按
  背景地板深度乘整帧 alpha，会错误擦除前景菜单。HUD 背景和 SDF 文字随后
  在自己的世界位置采样环境深度，保留真实手部遮挡。
- 新增 `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/Shaders/RealityEnvironmentDepth.hlsl`，
  在 PICO 遮挡函数外检查深度投影正前方、每眼 UV 和 near/far 范围，
  未覆盖位置保持可见。UI 两个 Shader 与 EnvironmentDepthComposite 共用此边界。
  PICO Unity SDK 原始源码保持不变。
- High LOD 在 `GameSceneBuilder.ConfigureInteractionCapabilities` 明确赋值
  `PxrMeshLod.High`，资源 YAML 对应 `meshLod: 2`。修改文件后已有
  ScriptableObject 实例仍可能持有 Low 并在构建时保存，所以必须验证构建入口
  实际加载的配置，不能只检查磁盘 YAML。
- 20 项生产组件检查通过，包含真实 PhysX 落物、位姿/网格生命周期、三套 Shader
  导入、High 配置和真实房间的完整障碍出生点回放。
  批处理 Editor 的 MonoBehaviour 生命周期需在验证中显式触发；
  临时脚本/日志均位于 `/tmp/matesoul-reality-20261024`，交付保留数值报告。
- 真机视觉与接触精度仍需佩戴验收；High 网格依然是系统重建数据，
  不能视为逐帧环境深度图的等精度物理替代，也不能保证未扫描/薄边/反光区域无穿入。

## Phase 技术交接：持续UI指针与物品按钮轻触（2026-10-24）

- 本节替代前述“HUD平面5cm范围优先poke”和“捏合0.75/0.35”的输入约定。
  现在只有实际可用HUD控件5mm接触才抑制眼手；接触已按下时保留15mm退出迟滞。
  非接触悬停和空白区域不抑制。捏合进入/退出为0.75/0.55，握拳判定不变。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/PicoHandPoke.cs`
  现在实现 `MonoBehaviour,IUIInteractor`，不再继承XRPokeInteractor。
  `Configure`接共享手部样本，`Bind`在GameHud取得EventSystem后注册；
  `UpdateUIModel`使用食指附近的短段和真实Graphic Raycaster检测控件，
  5mm按下、15mm退手释放，失跟踪/持物/组件禁用取消。原select状态保留到UI处理，
  不因追踪变化反复注册或Reset。`IsTouching`供HUD仲裁和位置冻结使用。
- 接触按下后使用原控件表面法线投影命中段，保证快速后退也能在原控件投递释放，
  同时保留按下、拖拽和松手链路。横移出控件或过深穿透取消；相邻控件需退出后重按。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/PhysicalUiButton.cs`
  通过TrackedDeviceEventData识别PicoHandPoke，物品按钮在OnPointerDown执行onClick，
  OnPointerClick不重复执行。普通鼠标保留Button原行为，原业务IsInteractable边界不变。
  现有5类按钮仅变更继承关系，新增按钮使用此能力，无需名称分支或修改Prefab GUID。
- GameHud先Bind左右Poke，再注册HudEyeHandInteractor，确保当前帧接触先于眼手仲裁。
  LateUpdate只对本HUD实际命中且按下/接触的指针冻结位置；物品控件不会冻结HUD。
- XRI2.5.4的UIInputModule.Process是延后处理入口，真正处理位于Update/DoProcess。
  集成回归必须跑EventSystem.Update与XRUIInputModule.Update，不能只强制调用单个
  ProcessTrackedDevice。批处理Canvas仍需URP SubmitRenderRequest生成有效Graphic.depth。
- TrackedDeviceModel.raycastPoints setter只检查点数。修改复用列表前先设置空列表，
  再写入新点位，确保静止眼睛/移动菜单也刷新；不能调用Reset丢失select释放变化。
- 当前验证63项UI/Prefab + 原23项抓取通过，含100次交替眼手点选及7个真实控件。
  验证样本是注入姿态。临时脚本位于`/tmp/matesoul-input-fix-20261024`，
  仅移除本轮Editor符号链接/meta，原DirectHandValidation文件和导航/角色资产不变。
- APK为`/Users/bytedance/MateSoul/Builds/MateSoul_InputResponsive_20261024.apk`，
  SHA-256 `c97b7aa8474a6e1ee9c62dd40c35f0313a47e7e76d8d5db8ad6430e0b68417b5`；
  已安装并核对设备哈希。流式ADB空错误不能直接视为安装失败，应先查看设备
  PackageInstaller记录和实际包哈希。本轮实际已成功，无需重复安装。
- 原存档存在且时间戳早于安装，首次安装前哈希未落盘；复验期间未改变。
  新进程PID24677在Unity初始化前休眠，因此运行输入/真人手感仍待验证，
  不把自动测试或旧进程日志冒充本次真机通过。
- 数值交付：`/Users/bytedance/MateSoul/项目沉淀资料/validation/input_responsiveness_20261024.json`。

## Phase 技术交接补记：最终现实网格包与联合输入代码

- 最终有效部署为 `/Users/bytedance/MateSoul/Builds/MateSoul_RealityCollision_20261024.apk`，
  SHA-256 `7b9e6b33dabc4ba6d56383d09b1b1bbed97ac25a8cf32f6460fedf2de132a11e`，
  225.1s构建通过，183925615 bytes。上一节 InputResponsive 包是此前部署记录。
- 该包保留输入修复：HudEyeHandInteractor、PicoHandPoke、PicoTrackedHand、
  PhysicalUiButton、GameHud 均与最终 Player Android PDB 的源码 SHA-256 一致；
  四个现实网格/启动/导航关键源码同样一致，APK libil2cpp 与构建原生库匹配。
- 从 APK 中 `assets/bin/Data/dcc1ee21fb96e47e5a35a62b97c9a681` 的实际
  PXR_ProjectSetting 对象复验 `meshLod=2`、`spatialMesh=1`、`environmentDepth=1`、
  `handTrackingSupportType=1` 与 `eyeTracking=1`。自定义类型树裁剪时按本地SDK
  45个标量字段的声明顺序读取，并严格核对对象长度和布尔范围；不是仅检查 YAML。
- 最新设备包哈希已复验，存档在安装前后哈希相同；主场景、冷月Prefab、Animator
  保护哈希保持不变。只清理本轮 RealityGeometryCheck 的 Editor 链接和meta。
- PID30706 为 Awake，但首次房间数据为空后进入 Scene Capture 系统采集；
  本轮尚未取得 room_loaded / READY / liveMeshColliders 证据。保持此前SDK生命周期
  验收边界，不能把已安装与20项自动回归等同于真人视觉及现实接触验收。
- 结果文档和数值：
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_现实遮挡与实时网格碰撞_20261024.md`，
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/reality_collision_20261024.json`。

## Phase 技术交接：SceneCapture定位等待与启动重试（2026-10-24）

- `PicoSceneCaptureRoomMapProvider(Action<string> trace = null)`负责读取已有标定；
  已删除bool自动采集参数和StartSceneCaptureAsync分支。
  主游戏GameBootstrap和独立RoomNavigationBootstrap都使用此读取合同。
- 流程：调用前检查取消→订阅SceneAnchorDataUpdated→启动provider→立即查询→
  快照验证有效才返回→空/暂态ERROR_RUNTIME_FAILURE/无效地板等待事件或2秒重查询。
  事件版本计数覆盖查询中的更新；linked token设置25秒期限。
  finally解除订阅、取消等待、停止已成功启动的provider。
- 权限等其它原生错误直接报告；调用者取消保持OperationCanceledException。
  自身期限到达转为带“重试定位”指引的TimeoutException。
  本地PICO SDK的MR权限询问循环不使用token，首次弹窗仍需用户回应；
  不把25秒描述为可以替代系统权限或安全边界步骤。
- `GameBootstrap.RetryLocalizationAsync`只保留一个startupTask；
  未完成时返回同一Task，Session为空时重新初始化，已有Session时恢复物品。
  清理失败会话、角色可见性、Animator引用与已创建的物品根后才允许下一次启动。
- GameHud设置按钮走专用RetryLocalization入口，绕过要求Session已存在的
  普通业务RunAsync准入；避免房间启动失败后重试按钮没有反应。
- 最终生产类原文在隔离Unity工程用SDK桩回放14项，通过取消、事件竞争和25秒期限；
  没有新增正式资源或重建角色、动画、场景。当前主项目仍为Unity/PICO Unity SDK。
- 真机诊断PID2022：系统UID1000在GameBootstrap前启动安全边界界面，
  native state=VPS_LOCALIZATION_STATE_QUERY_WAITING_6DOF_STEADY，随后休眠。
  该观测与旧进程游戏主动StartSceneCapture是两个边界，不能互相冒充验证结果。
  下一轮需佩戴唤醒并环视，检查room_loaded、MATESOUL_READY与连续启动；
  不清除标定、不关闭系统安全边界来强行得到通过。
- 记录：
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_启动误入房间标定修复_20261024.md`，
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/room_relocation_20261024.json`。
- 最终有效包为`/Users/bytedance/MateSoul/Builds/MateSoul_RoomRelocation_20261024.apk`，
  SHA-256 `39c2650539cc582f3909f45324dca150aadd4cf0e9a2fefba66001026026b150`，
  183925623 bytes，BuildPipeline 249.097秒。诊断APK只是此前观测样本。
  11个当前房间/输入/现实网格源码SHA-256均与Android PDB相符，包内libil2cpp与
  构建库一致，签名/CRC/ARM64通过，主场景、冷月Prefab、Animator保护哈希不变。
- 安装成功且设备包哈希一致；前/后安装及运行后存档SHA-256完全一致。
  最终PID3524未出现MATESOUL_STARTUP或MATESOUL_READY，系统UID1000先打开安全边界，
  等待稳定6DoF，10.6秒时休眠。不要在后续交接中写为“真机房间恢复已通过”。
- 本轮HTTP诊断代码、临时构建类/meta与debug记录/env/ndjson已清理，collector已退出。
  后续佩戴验收可继续用ADB原生日志与正常StartupTimeline采集，无需恢复旧临时脚本。

## Phase 技术交接：眼手历史输入边界与故障采样（2026-10-24，未修复）

- 历史HUD眼手：GameSceneBuilder创建Gaze Pinch Ray，ActionBasedController绑定
  PXR_HMD眼动controls与右手pinchTouched，XRI管理按下/松开；
  旧PicoHandPoke使用SDK pokePose、root isTracked和trackingState。
- 当前HUD：HudEyeHandInteractor以PicoTrackedHand.IsPinching为点击状态，
  进入0.75/保持0.55，并通过Closed、Tracked、nearItem、持物和touchingHud准入。
  Closed进入0.75/保持0.35，PicoDirectHandInteractor.CanPoke也拒绝Closed。
  原生松开、自定义保持、共享握拳准入是不同边界，尚待真人日志确认是哪一个失效。
- 六样本SDK原始状态回放在空Play模式场景执行真实SDK私有UpdateHand，
  包含既有BYTE scale(factor=255)、原生InputAction和ActionBasedController。
  0.9→0.7时historicalUiSelect从1到0，graspValue=0.6不阻止再次眼手点击。
  Editor模式的InputSystem.Update不能直接作为运行时action回放，已废弃两次无效采样；
  不通过注入已经处理的布尔值宣称真人眼手行为通过。
- 本轮只新增带eyehand-pointer-stall标记的日志区域，未改变现有交互执行。
  GameHud.UIEvent记录pointer down/up/click与target；
  PicoTrackedHand记录rawPinch/rawGrasp/nativeTracked/nativePinch/Closed/关节状态；
  HudEyeHandInteractor记录eyeTracked/eyeSelect/owner/touchingHud与两手poke状态。
  GameHud.Update记录focus、CanvasGroup、页面过渡、sessionReady。
- 采集地址http://127.0.0.1:7789/event，PID17660保留；ADB reverse7789与8080已设置。
  HTTP失败后仅用已有DeviceStartupLog记录MATESOUL_INPUT_TRACE。
  该输入collector与上一阶段已清理的房间collector相互独立。
- 当前设备包为诊断CF14（完整SHA见数值记录），保留最新房间生产修复；
  12个相关当前源码匹配Android PDB，包内libil2cpp匹配构建原生库。
  当前HUD45cm/30°、场景近距手部、轻触按钮、High mesh LOD=2均保留。
- 下一步须佩戴头显重现：眼手点故障按钮→失灵时尝试眼手/食指→保持应用约10秒→
  张手移离UI及物品。日志到达后确认A-D假设、最小修复、同一复现前后对比，
  通过后清理本轮runtime插桩与collector。当前状态awaiting_user_reproduction。
- 不重建用户删除的旧InputFlowCheck.cs、launch_check.py、collect_result.py、
  deploy_checked_apk.py；保留既有DirectHandValidation。临时输入历史探针放在
  `/tmp/matesoul-input-history-20261024/`，不加入正式运行时工具类。
- 记录：
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_眼手冲突历史回查_20261024.md`，
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/eyehand_history_20261024.json`。

## Phase 技术交接：HUD原生捏合与轻触单击合同（2026-10-24）

- HUD眼手与世界手势已拆开取值：世界抓取继续使用PicoTrackedHand的Closed/Grip和
  关节姿态；HUD使用NativeTracked/NativePinching，对应PICO原生isTracked和
  pinchTouched。不要再用自定义捏合迟滞或Closed决定HUD pointer select。
- HudEyeHandInteractor的CanPinch只负责HUD/世界所有权边界：
  原生手已跟踪、未持物、手掌不在可抓取物附近。触碰HUD时仍由touchingHud使眼手让位。
  原生pinchTouched=false必须在当次UI更新释放，避免视线漂移期间延迟pointer-up。
- PicoHandPoke采用“首次接触单击”合同：进入5mm阈值发送down，相邻UI帧通过捕获控件
  中心发送up/click；保持接触不连点，退出15mm后重新武装。按压开始后即使Closed升高，
  只要食指仍跟踪且没有持物，也必须完成已捕获的release。
- PicoDirectHandInteractor.RefreshProximity以Time.frameCount去重，避免EventSystem和
  XRInteractionManager同帧重复查询。非凸MeshCollider不允许调用Collider.ClosestPoint；
  当前使用bounds.ClosestPoint作保守距离，OverlapSphere仍负责实际候选初筛。
- 真机前证据：眼手10次down/10次up/8次click，食指4/4/2；原生已松开但自定义保持
  17个样本；一次明确Closed取消；触碰占用、焦点、CanvasGroup和眼动假设已排除。
  logcat的ClosestPoint警告27,529条，修复后隔离回归明确断言不再产生该警告。
- 运行时日志仍保留`eyehand-pointer-stall`，新构建使用`runId=post-fix`。
  HTTP不可用时继续复用DeviceStartupLog的MATESOUL_INPUT_TRACE，不新增并行输入系统。
- 自动回归6项通过；临时UiConflictValidation/UiConflictBuild链接及meta已清理，
  不进入正式项目。最终设备包SHA-256为
  `96afed789cafd793c357b7d02983831e2b52ad4a2c7e4efc430432272b9491fa`。
- 下一轮真人验收必须连续点击互动/信息/虚拟物品，再用食指轻触对话关闭；
  保持应用10秒后回捞post-fix日志。通过前状态为awaiting_post_fix_human。
- 记录：
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_UI交互冲突根因与修复_20261024.md`，
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/ui_interaction_conflict_20261024.json`。

## Phase 技术交接：SceneCapture持续等待合同（2026-10-24）

- 本节替代前述`LocalizationTimeout=25s`合同。真机证据表明PICO可在多轮失败后
  识别候选历史地图，但VPS结果导入与SceneCapture当前mapUuid激活不是同一时刻。
  固定deadline会在系统状态传播完成前主动停止provider。
- `PicoSceneCaptureRoomMapProvider.LoadAsync`现在以调用者CancellationToken为唯一
  生命周期边界：订阅SceneAnchorDataUpdated→启动provider→查询→验证快照；
  空结果、暂态ERROR_RUNTIME_FAILURE或暂时无效地板继续等待事件/2秒周期查询。
  有效快照返回、非暂态错误或调用者取消时统一解除订阅并停止已启动provider。
- GameBootstrap仍只有一个startupTask。启动未完成时RetryLocalizationAsync返回
  同一Task，不会创建第二套恢复状态机；应用OnDestroy取消lifetime后provider退出。
- 真机导航不允许Spatial Mesh静默降级。Scene Capture负责真实房间地板、语义家具
  和世界一致性；运行时Spatial Mesh只补充高精度碰撞与遮挡。没有伪造地板、
  旧坐标缓存或自动StartSceneCapture。
- 角色在真实快照和NavMesh构建完成前保持inactive；GameSession、Room Items和
  RestoreItemsAsync随后创建。因此看到角色和物品同时缺失时先查room_loaded，
  不要分别重建角色Prefab或物品目录。
- 最终包：
  `/Users/bytedance/MateSoul/Builds/MateSoul_RoomPersistentWait_20261024.apk`，
  SHA-256 `0b54e7d0aeb0b83715f5863e717b990d7b65e1cf62e79c081447bd9732a24205`，
  183394838 bytes，BuildPipeline 803.979秒。签名v2、1564项CRC、ARM64与
  8个当前相关源码Android PDB校验通过，三项角色/场景保护资产不变。
- 设备覆盖安装及本地包SHA一致，存档前后SHA一致。当前UI原生捏合/轻触修复源码
  已纳入同一包；输入collector PID17660继续保留，不能因本阶段清理而停止。
- PID16134无人值守运行只到first_before_render，随后系统安全边界暂停Unity并休眠，
  所以仍需佩戴验收。通过标准是同一进程出现room_loaded、navmesh_built、
  avatar_bound、items_restored/business_ready和MATESOUL_READY；仅“不再报25秒超时”
  不足以证明PICO已激活房间。
- 数值：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/room_persistent_wait_20261024.json`。

## Phase 技术交接：PICO MR安全策略与活动房间启动边界（2026-10-24）

- MateSoul是透视现实房间应用，PXR_ProjectSetting.mrSafeguard必须为true。
  GameSceneBuilder.ConfigureInteractionCapabilities在现有构建能力配置中设置；
  由SDK PXR_BuildProcessor生成Android metadata `enable_mr_safeguard=1`。
  SDK PXR_ManagerEditor:315说明未启用时默认VR safety policies，不能只开VST/
  SceneCapture而忽略该项。此项不等于SecureMR，也不等于关闭安全边界。
- 诊断必须分清：持久RoomCapture文件存在→识别候选地图→当前6DoF/map激活→
  SceneCapture获取有效锚点→NavMesh→角色/Session/物品。任何前一环节成功
  都不能直接证明下一环节成功；地图为空时无需重建冷月或物品资源。
- 旧原生证据22:47:48.663为safeMode=1、same6dof=false、
  dataAlgorithm=null、VR_NO_JUMP，系统抢占在GameBootstrap之前。
  另一个旧查询窗口当前mapUuid空、scene anchor0。SDK与旧APK Manifest共同
  证实配置缺陷，不能将“房间已有标定”解释为应用可立即读取有效快照。
- 新包仍保留单SceneCapture provider生命周期等待，固定2秒周期与更新事件。
  真实有效RoomMapSnapshot是角色/物品启动的准入条件，不引入伪造地板或
  SpatialMesh静默降级。当前角色和玩法所有权规则不变。
- 新RuntimeColliderRefiner中Unity2022 Physics.BakeMesh为void；
  调用完成后设置已有baked标志，异常时维持原处理。不要从其他Unity版本
  假设该API返回bool。本次修正已经由Android脚本编译验证。
- 构建必须选择已保存GameSceneBuilder.ScenePath，不运行BuildScene重新写角色。
  最终包202950656 bytes、BuildPipeline139.513秒，MR metadata=1；
  签名v2、1564项CRC、ARM64、10个当前源码Android PDB、IL2CPP原生/metadata通过。
  设备APK SHA为
  `f3314afb7744c2b45f45ee9a3bd137ae1e6e7b788348eda5f21fa4951c2ae596`。
  三项场景/角色/Animator保护资产及前后/运行后存档哈希一致。
- 新PID19672已经bootstrap_start与scene_provider_started；
  本次未捕获旧BoundaryJumpManager/executeSkip抢占。首次query SUCCESS/0锚点、
  3352ms、updates2、focus1；随后头显休眠，第10.2秒起所有probe均Asleep。
  尚无room_loaded、avatar_bound、READY，状态必须为
  awaiting_post_fix_human_room_verification。不能将配置修复写为全启动验收通过。
- HTTP直连事件0、后续reverse为空；post-fix实际查询从DeviceStartupLog回捞，
  导入时source=adb-offline-device-startup-log并保留行号与时间。
  本轮RoomMapProviders诊断/7792采集端留待佩戴验收，READY通过后再清理；
  输入7789/PID17660保持独立。不要恢复已删除的旧房间构建/回放工具。
- 报告、数值和APK：
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_MR安全策略与房间启动修复_20261024.md`，
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/room_mr_safety_20261024.json`，
  `/Users/bytedance/MateSoul/Builds/MateSoul_MRSafety_20261024.apk`。

## Phase 技术交接：HUD捏合与世界抓取状态隔离（2026-10-24）

- `NativePinching`只表示HUD眼手捏合；`NativeGrasping`绑定PICO原生graspFirm，
  只表示世界抓取的firm gesture。禁止再用同一0.75/0.35模拟迟滞同时驱动两者。
- PicoTrackedHand在NativePinching期间设置`suppressGraspUntilOpen`：
  即使graspFirm同时为1，Closed仍为0；捏合结束后直到graspFirm实际归0才清抑制。
  随后的新graspFirm才能进入Closed，避免眼手尾部误抓物体。
- PicoDirectHandInteractor在`!Closed`时直接armed=true。firm grasp在远处仍会
  清armed，继续防止握拳移动进范围自动吸取；新firm transition只有在候选附近且
  `HasGripContact`通过时才选择。Play Mode按frameCount去重近物查询，
  Edit Mode验证不缓存。
- CanPoke合同改为“食指TipTracked且未持物”。Closed不再禁止触碰；
  实际持物仍由hasSelection阻断。post-fix-2真机日志新增nativeGrasp与
  graspSuppressed，验收时应看到眼手期间graspSuppressed=1、closed=0。
- post-fix-1真机证据：眼手7/7、Poke3/3均成功，pointer/capture假设已排除；
  右手20个Closed样本全部held=0/nearItem=0，17个发生在原生捏合已释放后。
  直接手恢复与Grip降到0.35以下严格同帧，确认共享Closed为根因。
- 自动回归：抓取隔离7项、DirectHandValidation 23项通过。
  覆盖眼手抑制、张手后新firm、立即重武装、未持物触碰、近距拾取、
  远处不吸取、跟踪丢失安全释放、双手争抢、放置和物理阻挡。
- Unity2022兼容：RuntimeColliderRefiner调用void返回的Physics.BakeMesh，
  以异常作为失败边界；AvatarPhysicalInteraction通过第一个ContactPoint的
  thisCollider/otherCollider识别手段与角色部位，不使用新版Collision.otherCollider。
- 最终包SHA-256
  `8ae8b3b7db01d6c1c8415ddbeb1c3a5292ef89271eec6a06ee1f66f40430bb02`，
  202949723 bytes。9个手部物理当前源码和11个关联源码匹配Android PDB，
  libil2cpp与同批构建一致；Manifest保持enable_mr_safeguard=1及手/眼/空间权限。
- 安装时ADB首次offline，按“确认旧包哈希→reconnect offline→单次有界重试”
  恢复，避免重复未知安装。设备包与存档哈希最终通过。
- 真人验收：眼手点击后无需刻意完全摊手，立即食指触碰，再张手/firm grasp拾物；
  连续3轮。通过后回捞post-fix-2并清理输入诊断区域与7789 collector。
- 报告/数值：
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_眼手后直接手失效根因与修复_20261024.md`，
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/hand_after_eye_conflict_20261024.json`。

## Phase 技术交接：同包不同定位结果与旧物品支撑证据（2026-10-24）

- 当前实际installed0x8ae8包包含MR safeguard1、完整当前主场景与最新直接手修复。
  判断安装必须每次从pm path获取base.apk并设备sha256sum，不能依赖上次安装记录。
- 23:13用户失败时10次房间query=SUCCESS/0锚点/focus1，持续46s；
  23:18同包主动启动时当前地图b371...有效，22锚点/313ms，
  NavMesh24ms、avatar_bound、READY。前者仍说明地图激活/数据传播阶段存在失败，
  一次后者不能满足全场景稳定性或清理room-map-activation完成门槛。
- READY分为物品ready/recovery。当前已确认恢复支撑图失败导致10件全部回滚，
  Notice为“旧物品缺少可靠支撑，已保留原存档”，旧房间锚点已located=True。
  不要仅看到READY就认定旧物品完整恢复。
- GameSession支撑图前后新增R6-R8诊断：每件Body.Shapes数、bounds底部、
  itemRoot/世界pose、向下3m射线命中名称/距离/法线/rigidbody。只诊断，不改规则。
  runId=support-pre-fix，HTTP7792+已有DeviceStartupLog/MATESOUL_RESTORE_TRACE。
- 静态当前RuntimeRoomNavigation.CreateFloorGeometry在非debug模式不建floor collider；
  RealityOcclusionMeshRenderer.WaitForPhysicalSurfaceAsync只要求任意native mesh存在。
  物品支撑覆盖未必成立；必须按实际逐件命中确认，勿盲目放宽支撑阈值。
- 后续实时存档里一块raw-steak y<-51000且needsGravitySettling，仍在下坠。
  这是运行时地板/物理边界独立证据；不要用修改或覆盖存档掩盖故障。
- 当前正在等待support诊断包构建与设备采样，临时工具路径
  `/tmp/matesoul-room-map-activation-20261024/latest-user-failure`；
  不重建被删除旧构建工具。记录：
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_安装核对与旧物品恢复故障_20261024.md`。

## Phase 技术交接：不可见地板碰撞、库存边界与启动锚点预取（2026-10-24）

- RuntimeRoomNavigation.CreateFloorGeometry必须始终创建RealitySurface
  MeshCollider；showDebugGeometry只能控制MeshRenderer/材质。当前实时网格只
  覆盖局部，任意mesh就绪不代表整块地板具备碰撞。地板不可写depth。
- RoomNavigationService.ContainsFloorPoint使用所有标定Floor多边形检测，
  不用NavMesh判断物品边界：现实桌面和家具区域可能不在可走NavMesh上。
- GameSession.RestoreItemsAsync在占位检查前筛掉低于floor1m的物品，或
  floor外且没有PhysicsBody支撑的物品。按InventoryUnitKey一起回收组装件，
  复用ReturnSavedInventory保全有限库存/无限库存合同，成功后持久化；
  仍保留室内无支撑、空ID、非法旋转、虚拟重叠的严格拒绝。
- GameSession.Tick复用RecycleItem处理未持有/未占用且低于floor1m的物品；
  清理和库存更新由现有InventoryUnit owner执行，没有新物理控制器。
- 原10件真机数据隔离回放通过：3正常恢复、7异常单位回收，跌落回收、
  库存守恒，floor Collider1/Renderer0。现有13项回归12通过；missing定义
  构造失败是修复前已存在失败，勿写全通过。
- 房间预取锚点LoadAsync只有短等待；定位可能74s后才完成。若prepare task
  早已fault/cancel，恢复阶段必须重查一次当前活动地图下的同一UUID。
  历史world pose和当前地图不是同一可靠坐标系：本轮floorY0.603而历史rootY0.024，
  直接fallback导致全部支撑无hit。GameSession.LoadRoomAnchorAsync已改，
  已成功或仍进行中的prefetch与暂停合同保持，设备验证进行中。
- 临时回放只能在空未保存场景/MemoryStore执行；不要把旧存档写回设备。
  Unity Android设备扫描会终止别SDK的ADB server，安装与Unity批处理应串行。

## Phase 交付状态：锚点重查隔离验收与实际安装（2026-10-24）

- `GameSession.LoadRoomAnchorAsync`对failed/canceled准备任务进行一次当前活动地图
  重查已通过隔离回放；故意设置历史worldY=-5，物品仍正确恢复到当前锚点。
  原10件数据3恢复/7单位回收、库存守恒和runtime跌落回收保持通过。
- 当前交付`/Users/bytedance/MateSoul/Builds/MateSoul_RoomAnchorAndFloor_20261024.apk`，
  最终设备SHA `c4b3ba300acaf243ae4fbcdf01b087d6817bdd70571c3c14f672aa5ccc007096`。
  MR1/10源码PDB/签名CRC通过，场景/prefab/controller和存档hash保护通过。
- 最新60秒设备采样全部休眠，最终包真人恢复仍INCONCLUSIVE。不要把此前
  awake时角色绑定或隔离回放写作最终全链路真机通过。
- 临时Editor链接已清理，room-map-activation保持OPEN等待佩戴采样。
  数值：`/Users/bytedance/MateSoul/项目沉淀资料/validation/room_install_restore_20261024.json`。

## Phase 技术交接：宽松抓取、有限接触推力与角色柔性反馈（2026-10-24）

### 控制路径

1. `PicoTrackedHand.UpdateWorldGrip`读取原生握持值及中/无名/小指弯曲度；
   两根指头形成闭合即可侧握。WorldGrip是场景抓取信号，既有Closed与HUD
   原生捏合释放合同独立保留。关节数据不足时掌心6.5cm表面近距容差仍可选择。
2. `PicoDirectHandInteractor.UpdateSelection`使用0.42取得和0.22明确张手阈值。
   一次新闭合只开放0.22s近距取得窗口；已选中后保留，0.16s持续张手才释放。
   丢追踪0.28s内保留最后掌心姿态，之后释放并由PhysicsBody清理速度。
3. `TrackedHandPhysics`保留掌心及16条手指/腕段，但均为Trigger；通过
   ComputePenetration取得真实方向和深度。单手对单物品/单角色合并最强接触，
   每FixedUpdate只提交一次。追踪跳变、持有物、固定/停靠物和其他动作占用物
   不提交推力。手部不会作为无限质量实体挤飞刚体。
4. `PhysicsBody.ApplyHandPush`将接近速度映射为有限目标速度（≤0.55m/s），
   单轴接触加速度≤2.5m/s²，角力矩≤mass×0.035。约0.15m/s轻推映射
   到0.048m/s物品速度。只限制手部推动，避免全局限速破坏保龄球或抛掷。
5. `AvatarPhysicalResponse.ReceiveContact`请求已注册的隐藏角色能力，
   不在AI/UI/autonomy之外再开执行器。GameBootstrap创建Session后BindSession。

### 角色碰撞与所有权

- 在生产Body/Face的BakeMesh顶点和骨骼权重上拟合69个内收细分面；身体接触
  不使用宽袖、裙摆和头发范围。Head1是真正头骨，Face只是根部Renderer。
  当前接触offset2mm；手指也分段。一次性拟合编辑器实测67ms，设备耗时未验收。
- `HandHoldInteractable.Configure`恢复左右原Sphere选择面并启用，进入10cm/
  退出16cm。移除将选择面替换为物理身体面的方法；Sphere是Trigger不传递推力。
  `HandIKRig`、`HandHoldAction`和原牵引控制器继续负责手臂目标及跟随。
- `physical_body`、`physical_left_arm`、`physical_right_arm`分别占用Body、
  LeftArm、RightArm，待机ambient注意力让出通道，显式动作及牵手臂不让出。
  自由臂受力也可轻带Chest，但不会抢选中手臂的IK通道。
- 当前实现使用有限角度阻尼弹簧。肘膝使用关节平面单轴和伸直边界，肩腕/
  躯干按受力和邻接权重联动。此实现不是全身ragdoll或完整生物力学模型。
- 仅Torso速度≥0.65m/s、真实压入≥12mm、连续≥0.14s触发
  `physical_balance_step`。Locomotion没有其他所有者时请求4–9cm偏移，
  `AvatarMotor.BeginPhysicalStep`负责0.62s运动与碰撞，1.1s冷却。
  Body反馈同时使用两段程序足部IK转移支撑，抬脚2.5cm；不新增或重建动画Clip。

### 验证与交接

- 生产Prefab和真实PhysX专项26通过，既有HandHold/GripGrab/PhysicsRuntime/
  RealityOcclusion/RoomNavigationSurfaceMesh共25通过。轻触根位移约0，
  较强接触2.475cm小步且两脚有抬升；固定物和同手接触聚合均通过。
- 隔离编辑器场景必须Initialize Wardrobe、RestoreItemsAsync解除Avatar.Suspended；
  XRI在EditMode没有正常Awake发现Collider，手工设置列表后需重新注册。
  验证专用临时入口不保存场景，也不调用GameSceneBuilder.BuildScene或全资产构建。
- APK从已保存主场景增量打包。受保护主场景、角色Prefab、Controller及Clip meta
  必须核对哈希；同时用Android Player PDB源码校验和确认本轮修改确实进入编译。
  与其他任务共享Unity时批处理串行，不能结束其他窗口的进程。
- 现有实时High Spatial Mesh、宽松导航、不可见地板和房间锚点重查继续保留。
  未来调参优先调整输入门槛、单接触推力和部位弹簧，不向中央协调器加入角色名分支。
- 真人接触表面贴合、握持疲劳、衣物穿插、动画合成及头显性能尚待验收。
  完整说明与数值：
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_抓取舒适度与冷月柔性反馈_20261024.md`，
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/hand_comfort_20261024.json`。
- 本轮APK：`/Users/bytedance/MateSoul/Builds/MateSoul_HandComfort_20261024.apk`，
  SHA `a7dd75abf3b34a63e1c04f12816cffb3b492f930f4aa911deb2d72c37c85f5de`。
  签名/CRC/ARM64/IL2CPP原生库/8源码PDB/25保护资产校验通过。此包未安装，
  与另一个窗口之后的实时房间几何修改分属不同编译时点；后续合并构建需
  保留本轮8个交互源码，重新核对房间源码校验和。临时Editor入口已删除。

## Phase D37 技术交接：实时测量覆盖、增量导航与深度生命周期

- `RuntimeRoomNavigation.UpdateLiveSurface/RemoveLiveSurface`接收变更UUID；
  `LiveRoomSurface`仅投影地板附近覆盖与0.12m至角色身高范围的障碍。
  16cm格、行合并、稳定源顺序、250ms合并；内部NavMeshData保留，
  UpdateNavMeshDataAsync用完整源哈希增量更新，Clear先Cancel再释放资源。
  2cm体素和64体素tile保证房间更新颗粒度，桌面不作为walkable源。
- 标定家具footprint所有格有测量覆盖后才能撤销旧障碍，固定墙/边界保留。
  不能以单个mesh存在或Removed事件推断全房间家具消失。
- `RealityOcclusionMeshRenderer`是扫描生命周期和测量物理唯一owner；
  UUID队列合并每帧2ms软预算，单块cook不可中断。先创建新Collider、
  更新导航投影再释放旧Mesh资源；Unchanged没有重复cook。
- `RealityMeshSource`是语义几何缓存消费者，不再自行启动/停止扫描。
  GameSession.Tick驱动250ms合并发布，现有chair/bed resolver拥有动作目标。
  运行时Unknown不得被旧Scene Capture AABB改写为Chair/Bed。
- `PhysicsBody`动态默认ContinuousSpeculative；快速基本形状ContinuousDynamic，
  含凸MeshCollider继续Speculative。坐卧/暂停等kinematic状态仍由原owner控制。
- 本地SDK `XR/Runtime/Scripts/SensePack/PXR_EnvironmentDepthManager.cs`
  增加SetTrackingOrigin，reset清prevTextureId/global纹理/开关；
  抓取失败清不可用，先帧参数再每帧预处理，恢复RenderTexture.active。
  深度纹理ID可复用，绝不能把“ID不变”视为图像没更新。
  SDK升级时复核此局部patch；没有引入第二个深度provider或Spatial ECS。
- 验证：真实地板受控快速下落、受控深度reset、动态障碍移动/移除/不变全部通过；
  现有27定向回归通过。编辑器墙钟不等于头显帧耗时，真人验收仍需持续佩戴。
- PICO文档：`https://developer.picoxr.com/document/unity/spatial-mesh/`。
  用户视野内变化可触发网格更新；视野外移动、反光/透明/薄边仍无可靠承诺。
  报告：`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_实时房间几何与穿地遮挡修复_20261025.md`。

### D37 最终设备交付

- 包：`/Users/bytedance/MateSoul/Builds/MateSoul_LiveRoomGeometry_20261025.apk`，
  SHA `5dfdefee9577bd43a82079a542e847d85c0306ca334687e71e1d424d9deaeb05`，
  已安装且最后设备复查一致；13源码Player PDB包含当前手部与房间修改。
- PID14459新日志已有10网格、512深度原始/预处理纹理、开关1；
  50秒采样后续都Asleep，没有房间READY。真人验收与头显耗时还未取得，
  不得把该链路启动信号或编辑器回放记作所有症状已修复。
- LiveRoomGeometryReplay临时入口已移除。Runtime诊断与debug记录保持OPEN，
  持续佩戴READY后再采集；验收通过后按debugger清理本会话插桩/记录/采集端。
  数值：`/Users/bytedance/MateSoul/项目沉淀资料/validation/live_room_geometry_20261025.json`。

## Phase 交互研究交接：抓取手感方案（2026-10-25，未修改运行时）

- 用户反馈160ms释放驻留影响手感，Trigger-only手缺实体感，拾取仍不自然。
  当前Pinch/Palm没有记录取得模式，拇食指释放可能仍被其他指头的WorldGrip
  阻挡。应按实际参与取得的指组释放；真实输入与合成显示骨骼保持单向关系，
  不读取包覆后的指节来判断抓握，否则会形成自锁。
- PXR_Hand在onBeforeRender更新可见骨骼，PhysicsBody每20ms按位置误差
  跟随并插值。宽松准入又保留原掌心偏移，故手物可能同时存在空间和时间差。
  推荐抓握面/正确锚点、持物可见手绑定实际物品显示姿态、有限速度前馈。
- 新手部约束只能控制最终可见骨骼/接触代理，PhysicsBody仍单点写物品运动；
  手势选择仍归XRI，物品预约和冷月牵手/局部反馈仍归已有owner。
  不能简单把17个追踪运动学Trigger改成实体来获得无限质量推力。
- ItemInteractionProfile可复用抓握几何，但冷月手部姿态不能直接套PICO玩家手。
  没有专用内容的物品保留自由抓取；按能力及几何扩展，不加物品名称分支。
- 30–50ms模糊输入防抖、80–120ms追踪宽限仅是推荐起点；清晰释放建议
  当帧结束选择。所有新方案均未实施或真机验证，最新已安装基线仍为D37。
- 官方来源、机制及验收方案：
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_手部抓取手感工程分析_20261025.md`。

## D37 真人失败诊断：房间准入与遮挡的证据边界

- 最新5dfdefee...eaeb05设备包仍有后续启动阻断：SceneCapture当前UUID为空，
  全零map导入、exist map0，8/11次36.5/51.5秒空查询。GameBootstrap顺序
  是Room.LoadAsync→Build→Spawn→Bind→Session→RestoreItems，故本次不显示
  的直接原因在第一步；有效Spatial Anchor与Mesh/Depth不能证明它完成。
- 启动文件和native ring证实focused1窗口，不能用稍后回捞Asleep解释该失败。
  status7没有找到Unity业务枚举，不自行命名或推断系统地图被删除。
- 遮挡512纹理与global1只代表资源；当前global1关掉所有网格depth renderer，
  后续必须测全屏pass/重投影/最终alpha。保留alpha配置0仅为待实测风险，
  HDR关闭时不能直接推断RenderTarget无alpha。
- 首次已就绪PID16015后半段缺spawn与原floor pose，墙外位置不能定因。
  运行期地图变为空而旧snapshot继续被使用是线索，需要验证坐标/边界合同。
- DeviceStartupLog达到128KiB后停止写入，input报告37/40s填满，两次旋转
  丢掉首次出生记录。日志保留策略必须保证关键位置与定位事件可追溯。
- 本轮只回捞、分析与文档更新，没有更改运行时或安装新包。报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_墙外角色与后续启动阻断日志分析_20261025.md`。

## 自然抓取实现交接（2026-10-25）

- `PicoTrackedHand`真实关节缓存是手势判定唯一来源。CaptureGripFingers/
  TryGetGripStrength记录实际参与掌握的指组；有效弯曲数据优先于陈旧firm标志。
  TryGetGraspPose为捏取提供指尖中点，为侧握提供掌心/虎口组合点。
  MotionValid限制异常位移/角度、重获追踪后的速度，提供有限线/角速度前馈。
- `PicoDirectHandInteractor`保持XRI选择所有权，选择时记录Pinch或Palm；
  NativePinching退出结束捏取，掌握指组≤0.12直接释放、<0.26持续40ms释放。
  模糊追踪100ms宽限，异常跳变退出且不继承速度；明确释放与跟踪失败分开。
  准入保持0.42及220ms新动作窗口，重复捏取单独武装，不因余指弯曲锁死。
- 附件点投影到实际Collider、保留物品初始世界姿态。物品仍由PhysicsBody驱动，
  新SetFollowTarget(Pose,velocity,angularVelocity)前馈叠加原误差控制；
  一参数入口保持原调用合同。最近80ms释放样本来自真实刚体而非穿墙手位移。
- 新`PicoHandContactVisual`订阅onBeforeRender，BeforeRenderOrder100在PXR_Hand
  恢复原始姿态后只写最终显示骨骼。握持手按物品实际显示位姿有界对齐；
  掌心接触法向最多修正35mm，持物对齐最多75mm/35°。指节30Hz拟合，
  6段弯曲扫描+5次细化停在首次接触；深嵌且无安全起始姿态时保留原始姿态。
  不支持真实触觉，也不能把它描述为全物理手或任意物体精确包覆。
- 接触查询复用TrackedHandPhysics.PalmCollider/FingerCollider的已启用形状。
  禁用的辅助Collider曾使ComputePenetration返回false；不额外创建第二套
  刚体执行器，也不把运动学Trigger直接变为无限质量接触。True输入不读显示骨。
- 手材质Opaque/ZWrite/ZTest保持，改明暗/阴影/弱高光，无描边；抓放音效短且
  低音量。所有查询/音频资源由视觉组件生命周期清理，禁用不保留Held引用。
- 32集成专项+28既有回归通过；图像对照使用BakeMesh固定当前姿态后渲染，
  避免编辑器SkinnedMesh缓存使两张图相同。临时图像不进入最终交付包。
  新代码没有改主场景/生产Prefab/动画Clip。真人自遮挡、侧握贴合与疲劳待验收。
- 当前房间改动继续合并，最终APK核验应包含PhysicsBody、手部6文件、房间
  RuntimeRoomNavigation/LiveRoomSurface、GameBootstrap及SDK深度文件。
- 实现和数值：
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_自然抓取与手部接触实现_20261025.md`，
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/natural_grasp_20261025.json`。

## Phase D38 技术交接：有界房间准入、凹地板与双路遮挡

### 启动所有权与回退流程

1. `GameBootstrap.LoadRoomMapAsync`是真机房间准入的唯一所有者。编辑器继续
   `PicoMockRoomMapProvider`，模拟器直接使用Spatial Mesh，真实设备先尝试
   Scene Capture。
2. 真实设备创建linked CTS并启动`PicoSceneCaptureRoomMapProvider`，
   同时等待独立12秒`Task.Delay`。外层deadline必须独立于SDK任务，因为
   PICO query在系统定位/地图异常时可能不响应managed取消。
3. Scene任务先完成则使用标定房间；deadline先完成则取消旧任务、观察其异步
   fault并记录`scene_fallback_spatial_mesh`，然后加载PICO Spatial Mesh。
4. `PicoSpatialMeshRoomMapProvider`先读取PXR subsystem现有缓存，缓存不足才
   启动扫描；只停止由自身启动的subsystem，不能破坏遮挡/实时导航owner。
5. 后续仍保持单一路径：snapshot→Room.Build→occlusion→spawn→avatar bind
   →session→item restore→READY。AI/UI/autonomy没有新增旁路执行器。

### 实测地板与出生合同

- Spatial Mesh地板从向上法线且接近主地板高度的Floor三角形建立16cm栅格。
  选择面积至少4m²的最大连通组件；并列时优先相机最近组件。轮廓按栅格边界
  生成并去除共线点，因此保留L形等凹形，不得恢复为全点ConvexHull。
- snapshot仍保留原始SurfaceMesh用于碰撞/遮挡，凹轮廓只定义主可行走区域。
  这避免低成本区域推导篡改高精度网格。
- `TryFindSpawnPosition`遍历最近NavMesh点和三角形中心，候选必须在
  `MainFloor`多边形内且角色胶囊不与现实代理相交。
- 一旦`liveSurfaces.Count>0`，候选还必须从上方射线命中
  `RoomSurfaceCollider`，法线Y≥0.7，实测高度与候选不超过15cm。
  使用hit高度作为最终spawn Y。没有实测支撑即拒绝，不静默退回标定高度。
- GameBootstrap的搜索距离为首选点到房间中心距离加bounds extents，
  防止玩家面墙时2.2m首选点在房间外且搜索半径覆盖不到室内。

### 遮挡双路合同

- `RealityEnvironmentOcclusionFeature`继续在透明虚拟物前运行全屏环境深度
  Pass；Player `preserveFramebufferAlpha=1`，Renderer目标首帧记录实际格式。
- `RealityOcclusionMeshRenderer`的native measured mesh depth-only renderer
  不再因Environment Depth有效而关闭。环境深度负责视野内高频细节，
  实测网格提供静态几何后备；两者都只写深度/alpha合同，不创建第二套碰撞owner。
- `MATESOUL_OCCLUSION_PASS alphaPreserved=True format=R8G8B8A8_SRGB`
  证明Pass与目标格式进入真机，不等于最终双眼像素已经正确遮挡。
  最终结论仍需佩戴视角观察或GPU读回证据。

### 日志与SDK避坑

- `DeviceStartupLog`必须优先保留`MATESOUL_STARTUP`、ROOM、SPAWN、GEOMETRY、
  OCCLUSION、READY和异常；不要让眼手输入高频日志占满128KiB。
- Spatial Anchor定位成功只证明存档世界锚点可定位，不证明Scene Capture当前
  map UUID和anchors有效；Spatial Mesh/Depth有数据也不能替代这一判断。
- Activity通过ADB启动后，未佩戴PICO头显约3秒会触发onPause/onStop。
  Unity PlayerLoop暂停时managed 12秒deadline也不会推进；此场景只能记为
  “设备休眠导致后验不完整”，不能判定fallback成功或失败。
- 构建产物必须同时核对APK完整SHA、Player PDB源码名、保护文件哈希和存档哈希。
  早于最终源码变更的中间APK不得以相似文件名交付。

### 验证与资源

- 专项12/12通过；组合33/35，本轮新增房间和遮挡测试全部通过。
  两项既有Seating时序失败不属于本轮回归通过项。
- 最终包：
  `/Users/bytedance/MateSoul/Builds/MateSoul_RoomFallbackOcclusion_Final_20261025.apk`，
  SHA
  `4f24c35e3b287c4ce9abd2713d0e07ac2935384db2a8ae65a34e0d683d154f8f`。
- 正式分析：
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_墙外角色与后续启动阻断日志分析_20261025.md`。
- 数值：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/room_fallback_occlusion_20261025.json`。
- 调试会话保持OPEN，待持续佩戴启动取得fallback/READY、SPAWN_FOUND/
  SPAWN_BOUND、GEOMETRY meshDepth+depthAvailable及真人视觉后再清理插桩。

### 自然抓取合并包交付

- 当前已安装：`/Users/bytedance/MateSoul/Builds/MateSoul_NaturalGrasp_20261025.apk`，
  SHA `e2335100b7793251079306ea47ee1f2c10a50522816efb2db6aeb5946e6b8ef9`。
  本轮15源码Player PDB匹配，包含当前房间fallback、遮挡和自然抓取。
  不能再用此前4f24包作为最后安装身份；现场回捞先核对设备APK。
- 32专项+28回归通过，主场景/角色/Controller/Clip meta共25项哈希不变，
  签名CRC/ARM64/IL2CPP原生库一致性通过。设备存档安装前后不变。
- 安装未自动启动，手部舒服程度、握取包覆、房间READY及双眼遮挡仍需
  持续佩戴验收。专项数值为编辑器受控输入，不能代替真人数据。
- NaturalGraspAcceptance临时Editor链接及meta已清理；实际代码保留，
  文档与数值：
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_自然抓取与手部接触实现_20261025.md`，
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/natural_grasp_20261025.json`。

## Phase D39 技术交接：Spatial Mesh无Floor语义时的安全降级

### 现场合同缺口

- `QueryMeshAnchorAsync`返回SUCCESS和非空mesh只证明几何存在，不保证
  `PxrSemanticLabel.Floor`存在。当前真机8块mesh连续23次查询成功，
  PICO online mesh仍返回`floor_status=-1/floor_height=inf`。
- 旧`BuildSnapshot`只把多数顶点标签为Floor的水平三角形加入地板，
  `RoomMapSnapshot.Validate()`还要求4m²。因此Scene Capture回退虽成功，
  业务仍会在Spatial Mesh provider中失败。

### 新地板选择顺序

1. 先收集PICO Floor语义且`|normal.y|>=0.75`的三角形。
2. 语义地板缺失或面积小于1m²时，读取XR主相机根节点Y作为tracking floor。
3. 只合并Floor或Unknown语义、中心高度距tracking floor不超过0.30m的
   近水平实测三角形。Table/Chair/Bed等已知非地板语义不得参与。
4. 以16cm栅格计算四邻接连通组件，选满足1m²的相机最近组件，再生成
   轴对齐凹轮廓。没有合格组件则继续等待，不创建虚构平面。
5. `RuntimeRoomNavigation.Build`只对source=`PICO Spatial Mesh`使用1m²门槛；
   Scene Capture、mock和其他房间源仍使用4m²通用验证。
6. `TryFindSpawnPosition`继续要求真实`RoomSurfaceCollider`射线支撑和15cm
   高度一致性。房间准入与角色最终支撑是两道独立门。

### 安全边界

- 1m²是“允许角色在实测站立岛出现”的最低面积，不表示完整房间扫描完成。
  后续live mesh仍通过现有增量导航扩展/更新障碍。
- tracking-floor几何推断不使用ConvexHull，不扩大扫描范围，不把抬高平面
  下投到地面，也不在无实际三角形时生成默认方形。
- `trackingFloorYOverride`仅提供确定性测试注入；生产调用不传值，自动读取
  XR主相机根节点。不要把测试参数外部化为运行时配置。
- 快照拒绝日志输出blocks、floors、floorArea、语义/几何三角形数、
  expectedFloorY、mode和reason，后续不再只看到笼统navigation-ready异常。

### 验证与交付

- 新增“Unknown tracking-floor接受”和“Unknown桌面高度拒绝”测试。
  RoomNavigation 7/7、房间+遮挡组合14/14通过。
- 当前安装包：
  `/Users/bytedance/MateSoul/Builds/MateSoul_MeasuredFloorFallback_20261025.apk`
- SHA：
  `d5d0a9a9777fe6f432f1bb99863ee482fb4fcee329acb2988c69878d61f44237`
- 包含最新自然抓取/PhysicsBody与本轮房间修复；保护文件构建前后hash一致。
- 数值：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/measured_floor_fallback_20261025.json`。

## Phase D40 技术交接：HUD深度隔离与单一视觉遮挡所有权

> 2026-10-25 真人复验已否决本节“关闭实测网格、只用Environment Depth”的
> 方案：该方案导致现实对虚拟物品/角色的遮挡消失。下文保留为历史记录；
> HUD深度隔离和下移保留，当前运行合同以D41为准。

### 阶段概述

本阶段处理D39真人启动成功后暴露的两个显示问题：低头时不可见深度面会裁掉
HUD和虚拟物，HUD整体位置仍偏高。碰撞、导航和现实遮挡能力继续保留，只调整
最终视觉深度的所有权和HUD姿态。

### HUD渲染合同

- `Assets/MateSoul/Runtime/Shaders/UiAlwaysOnTop.shader`与
  `SpatialUiSdf.shader`统一使用`ZTest Always`、`ZWrite Off`。
- HUD shader不得include PICO Environment Occlusion或
  `RealityEnvironmentDepth.hlsl`，不得使用
  `MateSoulEnvironmentOcclusion`改变UI alpha。
- World Space Canvas、`TrackedDeviceGraphicRaycaster`及HUD专用物理层继续
  负责空间交互。UI“永远可见”只影响视觉深度，不绕过按钮交互准入。
- `AlwaysOnTopUi`仍是图形与TMP材质的统一配置入口。新增HUD元素必须继续
  通过该入口取得材质，不能自行换回会参与场景深度的默认UI shader。

### 现实遮挡所有权

1. `PXR_EnvironmentDepthManager.IsDepthAvailable == true`时，
   Environment Depth是唯一视觉遮挡owner。
2. 此时`RealityOcclusionMeshRenderer`关闭所有native measured mesh的
   `MeshRenderer`，避免粗网格或近场错误面再次写不可见深度。
3. `MeshCollider`、`RoomSurfaceCollider`和live navigation surface不随
   renderer关闭；物理支撑、碰撞与增量导航继续使用同一实测网格。
4. Environment Depth不可用或manager禁用时，native measured mesh renderer
   自动恢复，作为静态视觉遮挡后备。
5. Scene Capture的SurfaceMesh/语义盒只允许作为碰撞或导航代理，始终禁止
   写视觉深度。其坐标和几何精度不足以作为最终像素遮挡来源。

关键入口：

- `RealityOcclusionMeshRenderer.UpdateDepthRendering`
- `RealityOcclusionMeshRenderer.ApplyDepthRendering`
- `RealityOcclusionMeshRenderer.ShouldRenderMeasuredMesh`
- `RealityOcclusionMeshRenderer.EnvironmentDepthOwnsVisualOcclusion`

开发包每2秒输出`environmentDepthOwnsVisuals`与`meshDepth`。正常深度可用
状态应为前者`true`、后者`false`；深度失效的后备状态应相反。

### HUD姿态

- `GameHud.CalculateVisibleCenter`在原45cm观看距离和30°向下角基础上增加
  `HudVerticalOffset=-0.10m`。
- 偏移使用世界Y轴，不改变HUD朝向、交互距离、面板中心补偿和跟随迟滞。
  页面展开、对话条切换仍以当前可见面板中心对齐。
- 后续调高/调低只修改这一稳定常量；不要同时改距离、俯角和Canvas缩放，
  否则无法区分高度、视角和可点击面积的影响。

### SDK与构建避坑

- Environment Depth纹理有效并不意味着应该同时保留Spatial Mesh视觉深度。
  两者叠加会让粗网格成为不可见遮罩，尤其在低头观察近场地面时明显。
- UI专用shader即使名称为Always On Top，只要仍使用
  `unity_GUIZTestMode`或环境深度alpha乘法，就不满足HUD深度隔离合同。
- Development Player会编译`DEVELOPMENT_BUILD && !UNITY_EDITOR`诊断块，
  EditMode测试可能不会发现其中的插值字符串语法错误。本阶段修复了
  `HandHoldInteractable`两处多余JSON右花括号；逻辑未改变。
- 构建必须在当前源码完成后启动。以IL2CPP生成文件中的新方法名核对，可避免
  把较早构建误作当前HUD/遮挡修复证据。

### 验证与资源

- `MateSoul.Tests.RealityOcclusionTests`：11/11通过。
- 真机状态：
  `nativeMeshes=9, meshDepth=false, environmentDepthOwnsVisuals=true,
  depthAvailable=true, rawWidth=512, processedWidth=512`。
- APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_OcclusionUiLowered_20261025.apk`
- SHA-256：
  `6115c646a86f1a33f39374d684ff91d9690a18d4c53f33fe9d440892bf7b3c17`
- 自动证据：
  `/Users/bytedance/MateSoul/.artifacts/occlusion-ui-lower-20261025`
- 数值：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/occlusion_ui_lowered_20261025.json`
- 真人双眼确认低头遮罩消失和HUD高度合适之前，视觉验收仍为pending。

## Phase D41 技术交接：撤销关闭实测网格造成的遮挡回归

- D40只验证了纹理可用、renderer被关和配置测试，未验证现实遮挡最终像素。
  `IsDepthAvailable=true`表示深度数据到达，不能作为关闭实测网格的准入条件。
- 真人反馈遮挡消失，设备完整SHA为`6115c646...7b3c17`；
  保全日志持续`nativeMeshes=9~10/meshDepth=false/depthAvailable=true`，
  证明网格源存在却被D40逻辑持续禁用。
- `RealityOcclusionMeshRenderer.UpdateDepthRendering`恢复：
  native mesh存在就保持renderer启用；只有native mesh尚未到达时才显示
  Spatial Mesh快照中的实测SurfaceMeshes。Scene Capture地板多边形和语义盒
  不作为视觉网格生成。快照SurfaceMesh在当前provider中来自实测世界顶点。
- 删除D40新增的Environment Depth单owner字段、判定方法和公开属性，
  防止再次因纹理到达关闭可靠的世界遮挡。Environment Depth合成继续保留，
  实测mesh负责现实固定几何，二者属于同一现实遮挡链路。
- HUD图形/TMP专用shader仍使用`ZTest Always`、`ZWrite Off`且不采样
  Environment Depth；GameHud `HudVerticalOffset=-0.10m`不变。
  世界遮挡与HUD可见性是两项不同的渲染合同。
- 新回归验证包括manager `IsDepthAvailable=true`且global开关1时mesh仍启用，
  以及实际URP相机在Metal上的GPU像素读回：无遮挡RGBA=(1,0,0,1)，
  实测网格遮挡后=(0,0,0,0)，HUD覆盖后=(0,1,0,1)。专项12/12通过。
- 首次CommandBuffer测试连对照物都未画出，已弃用；最终采用
  `Camera.Render`并关闭测试期间shader异步编译。失败尝试不列入通过证据。
- GPU测试证明生产深度shader与HUD隔离兼容，不能代替头显双眼/真实家具验收。
  交付和现场状态见`validation/occlusion_restored_20261025.json`。
- 当前恢复包已安装：
  `/Users/bytedance/MateSoul/Builds/MateSoul_OcclusionRestored_HudLowered_20261025.apk`，
  SHA `1b0c0f2fe4fd09c452f420b407b4a19e74c6b4ba967f0726fa8b82f546e7eac7`。
  构建3分13.55秒成功，8项保护hash一致、CRC/签名/ABI通过；PID10647在
  Environment Depth有效时确认10块native mesh仍`meshDepth=true`。
- 从此真实世界遮挡回归测试必须同时验证世界像素隐藏和HUD像素可见。
  不能以texture width、`IsDepthAvailable`、测试总数或renderer.enabled
  单项通过代替整个渲染行为。

## Phase D42 技术交接：出生准入等待完整网格批次与实测支撑

- `RealityOcclusionMeshRenderer.ApplyPendingMeshes`按2ms软预算分帧cook网格。
  旧liveCollisionReady在首块mesh安装后立即完成，使GameBootstrap在首块
  只有墙面、地板仍pending时进行唯一一次spawn搜索并永久失败。
- 删除mesh侧首块TCS/WaitForPhysicalSurfaceAsync。新增只读
  `HasPendingMeshes`，保留HasLiveCollision，网格安装、碰撞、视觉遮挡仍由
  RealityOcclusionMeshRenderer管理。
- `GameBootstrap.WaitForCharacterSpawnAsync`是当前出生等待唯一入口：
  使用独立Stopwatch计时8秒；有实测网格且pending为空才调用
  Room.TryFindSpawnPosition，失败每50ms等待后重试，成功返回出生坐标。
  CancellationToken取消时立即停止任务；截止无结果仍明确拒绝启动。
- 角色依然在成功spawn之后启用/绑定；AI/UI/autonomy动作所有权不改变。
  无地面时不沿用标定Y冒充实测支撑，不忽略墙面/床/桌碰撞。
- RuntimeRoomNavigation保留法线Y≥0.7、高度差≤15cm的实测support合同、
  主地板边界和角色胶囊Overlap检查。新诊断统计noSupport/outside/overlap/
  candidateCount，每秒最多一次失败报告；成功立即报告。
- 验证包括真实NavMesh+MeshCollider墙先到/地板后到、无网格截止、取消，
  与现有房间及GPU遮挡合计21项通过；回放记录首墙33/33无支撑，地板后成功。
- Unity可留下已退出PID的`Library/EditorInstance.json`和Temp锁文件；
  先核对PID和系统锁再移除残留，不能只因工具看不到进程就终止其他编辑器。
- 本轮保留最新并行手部修改和已恢复的实测视觉遮挡；交付证据：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/spawn_mesh_readiness_20261025.json`。
- 当前设备包：
  `/Users/bytedance/MateSoul/Builds/MateSoul_MeasuredSpawnReady_20261025.apk`，
  SHA `942af4ccf99b2894a7ec31dedaad2caf60f12f2aa5913cd0802c99d0f4dd2e84`。
  构建3分06.49秒成功，最新手部与出生代码Player PDB源码SHA均一致；
  不得用此前未包含后续手部更新的中间Framework包覆盖它。
- PID14849后验因未佩戴onPause/onStop停在Scene Capture等待，未进入spawn；
  真机出生验收pending。若后续仍报错，读取MATESOUL_SPAWN_PROBE确认
  是实测支撑、边界、胶囊碰撞还是无NavMesh候选，再作定向修改。

## Phase D43 技术交接：稳定握把与玩家操作优先（2026-10-25）

- `IntentSource.Contact`位于枚举末尾，保留旧来源枚举序号。优先级70，低于
  显式Player 90。身体反馈不再与牵手使用相同优先级，交接仍由
  AvatarActionCoordinator取消、Tick退出、释放lease、启动新动作完成。
- `PicoDirectHandInteractor`在HandHoldInteractable的10cm范围或
  ItemGripAnchor范围内，接受0.32抓握与闭合手进入；普通物面抓取保持0.42。
  松手只读取原始追踪，20ms模糊防抖与50ms丢失宽限。
- 新`ItemGripAnchor`读取ItemInteractionProfile的Handle/LoopHandle能力，
  抓握容差8cm。GameItem.Bind按能力安装，运行时没有物品名分支；
  现有角色用握点资源保持不变。
- 掌心稳定框架来自Wrist→MiddleProximal与Index/Little近端骨；抓取时固定
  它相对SDK掌心的旋转。PhysicsGrabInteractable使用预设Grip作为localAttach，
  避免弯指改变握点位置和刀刃方向。
- PhysicsBody.BeginFollow的stableGrip/cuttingTool参数选择真实替代行为。
  稳定持握kinematic、关闭重力和求解器碰撞、取消插值，逐帧目标通过
  2.5cm/10°采样检查CanOccupy。不是另设Transform写入者。
- cutting-tool允许穿过Cuttable.enabled食材，RuntimeCuttingTool仍按刀刃扫掠、
  速度与距离门槛提交切割；生产胡萝卜实测切开通过。苹果当前通用切割未启用，
  沿用既有分块动作。不要用不可切割苹果证明通用扫掠成功。
- 释放恢复动态、重力、碰撞与插值；投掷从实际受约束位姿短窗计算，不回放
  被墙阻挡的目标速度。暂停恢复必须保留stableFollow的kinematic状态。
- PicoHandContactVisual末端显示对齐腕部，手指在对齐结束后锁定合适的姿态；
  不写PicoTrackedHand缓存。左右官方手模型均实测通过。
- GameHud可移动物品前方0.55m生成、水平同向；GameSession搜索最多0.20m，
  复用支撑覆盖与占用检查。没有附近空位时直接拒绝，不迁移到远处。
- RoomNavigationService.TryRandom最低距离0.35m，同Unity夹具中旧1.25m
  拒绝0.545m完整路径，新入口通过。完整导航与障碍合同不变。
- Unity专项28项、既有回归59/59通过。真机跟随精度/手感与当前房间短路线
  尚待佩戴验收；当前debug会话保持OPEN，未把Unity夹具当成真机闭环。
- 说明与参考来源：
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_稳定握把交互与灵敏度优化_20261025.md`。
- 最新本轮交付为`Builds/MateSoul_StableGrip_20261025.apk`，
  SHA `2777e0f1f63575d1976f670637a5f52aec6a979d9e3a36e3bfc4f31d2765067a`；
  本地/设备一致，安装前后存档一致，14个Player PDB源码SHA匹配。
  新包包含并行出生等待与已恢复的实测mesh遮挡。
- 自动启动PID15922收到10mesh/depth512，未佩戴时onPause；未取得新的
  牵手/移动后验。下一轮从`post-fix`埋点直接读证据，不重跑已通过的28项
  专项和59项回归，除非新增改动或失败需要。
- 数值：`/Users/bytedance/MateSoul/项目沉淀资料/validation/stable_grip_20261025.json`。

## Phase D44 开发交接（进行中，未验证）

- 分支`fix/fluid-handoff-mesh-cache-20261025`。不要覆盖并行出生与遮挡修改。
- PhysicsGrabInteractable预约owner改为自身，使两只PicoDirectHandInteractor共享
  一个PlayerHeld预约；仅drivingHand提交物理目标。OnSelectExiting在还有选择
  时重建localAttach，无Drop/EndFollow；最后松手才归还预约。GameItem设置
  selectMode=Multiple。必须验证退出与取消清理及手型一致性。
- PicoDirectHandInteractor已去掉open/lostTracking计时器，松手以当前真实
  捏合/参与手指强度响应；reach=.13、握把allowance=.12。
- 掌心support为额外kinematic实心Box，水平且速度<.8m/s时启用；其他17段
  Sensor仍负责限幅推力。持有物品与手的碰撞对忽略。
- MeasuredMeshRetention保留Removed直到有效新网格覆盖其包围范围，Renderer
  OnDisable仅禁用缓存物体，OnDestroy释放；RoomSnapshot.Id改变清缓存。
- FluidStream每源128个parcel、单mesh，保存世界位置/速度/体积/温度/颜色，
  20ms重力积分、SphereCast与WaterReceiver开口穿越，溅水终结并累计。
  新shader`Resources/MateSoulWater/Fluid.shader`，无屏幕流体buffer/逐滴刚体。
- WaterReceiver聚合容器容量、水质量和温度，自动寻找vessel renderer中心
  修正液体XZ偏移，杯碗倾倒进入流体；LiquidPourer水壶共用流体。
  CoffeeBrewReceiver设置流出颜色，ElectricKettleController温升基于当前
  水温，避免加入冷水后直接覆盖混合温度。
- GameSession.TryResolveNearPlayerPlacement检索前方0.25–0.77m、±30°表面，
  惩罚地板，优先玩家高度；新validatedNearSurface参数防止旧水平重采样。
- 下一步必须编译并验证上述改动，再交付。设备存档已按用户授权清空；
  备份在`项目沉淀资料/备份/InventoryBeforeReset_20261025.json`。

## Phase 技术交接：放置朝向与支撑射线分离（2026-10-25）

- `GameSession.TryResolveSurfacePlacement(definition, aim, ignoredItem, out pose,
  viewerPosition = null)`：aim只负责选择水平支撑面；viewerPosition负责放置朝向。
  方向为`viewerPosition - surface.point`，由现有ResolvePlacementRotation投影到
  水平面并应用placementFacingYaw。未显式指定viewerPosition时沿用aim.origin，
  保持原有视线放置调用语义。
- ItemPlacementPreviewController.Refresh将已校验的Camera.main位置传给
  RefreshDirectHand；向下掌心射线不再决定物品yaw。旋转必须在
  HasRequiredSurfaceCoverage与IsPlacementFree之前确定，不能在验证之后单独
  旋转预览或成品，否则旋转后的占地可能越出台面或进入障碍。
- 电视前面位于本地-Z，使用placementFacingYaw=180；饮水机按钮位于本地+Z，
  使用0。WaterDispenserBuilder通过negativeZFace显式生成180/0补偿。
  新模型应配置其真实正面轴，禁止在中央放置流程添加物品名称判断。
- 挂墙物品仍以墙法线定位；已有存档读取保存的Pose，不在恢复时自动朝向玩家。
  本次不改TryResolveNearPlayerPlacement的移动物品朝向合同。
- 32组Physics检查通过：两模型、地板/0.8m桌面、八个方向，正面点积最低
  0.9999999，头部射线与掌心射线一致；正式Android构建与安装通过。
  临时PlacementFacingCheck入口/meta已清理，数值产物保留。
- APK：`/Users/bytedance/MateSoul/Builds/MateSoul_PlacementFacing_20261025.apk`；
  数值：`/Users/bytedance/MateSoul/项目沉淀资料/validation/placement_facing_20261025.json`。

## Phase D45 技术交接：玩家AI配置与HUD放置显隐（2026-10-25）

- `GameBootstrap.AIConfiguration`持有玩家配置；`SaveAIConfiguration`先持久化，
  成功再替换内存对象。`HttpLLMProvider`通过函数每轮读取该对象，普通与自发回复
  均使用同一密钥/模型/网关令牌，保存无需重建Session或重启应用。
- `PlayerAIConfiguration.Validate`只接受HTTPS根地址或游戏对话路由，拒绝URL
  密码、query、fragment与错误路由；开发显式开启时仅允许loopback HTTP。
  输入字段边界验证Header字符和长度，默认模型`deepseek-v4-pro`。
- `Assets/Plugins/Android/AISecretStore.java`采用非导出Keystore AES密钥和AES-GCM，
  SharedPreferences只保存密文与IV；无法解密时要求重新配置。Editor只存地址与模型。
  玩家秘密没有写入游戏存档、ScriptableObject或APK。Java/Dex构建已确认，实际
  头显保存/重新加载仍待用户佩戴验收，独立app_process探针被Android终止。
- 网关`player-credentials.ts`校验`X-AI-API-Key`/`X-AI-Model`，`index.ts`按请求
  创建DeepSeekClient和服务对象，复用MCP。可选Authorization仍由网关统一验证。
  服务端默认DEEPSEEK_API_KEY改为可选；未配置可启动，调用缺密钥返回401。
- `POST /v1/configuration/check`真实调用一次模型，15秒上限。`AIConnectionError`
  将密钥/模型/余额/限流分别映射到401/400/402/429，供应商错误正文不透传或记录。
- `GameHud`新增AI配置/教程页、缺密钥提示及设置入口；测试连接独立于业务Session
  就绪/暂停限制，测试期间锁定配置输入并由HUD生命周期取消请求。
- `ItemPlacementPreviewController.ActiveChanged`是放置显隐唯一信号。HUD计算
  `IsVisible = hudVisible && !placementPreview.IsActive`，保留页/草稿/手动显隐状态，
  取消当帧禁止B键再切HUD。放置成功仍由现有库存提交完成后Cancel恢复。
- 放置控制器动态添加时，RequireComponent的启动组件可能尚未挂入对象，
  不在Awake读取GameBootstrap，而在Begin读取，已通过实际UI回归验证。
- `HudVerticalOffset`从-.10改为-.20；页面定位与HUD深度隔离保持不变。
  `ConversationGateway.asset`默认地址空，build validator接受玩家首次配置。
- Node21/21、Unity19/19通过；真实配置/对话/自发回复均200。当前已安装包
  `Builds/MateSoul_AiConfig_LowerHud_Placement_20261025.apk`，SHA
  `183cf00823f0f8eb7b479392fd45ea07001ad3abe95a7843ebcf04d5d74840cc`。
  APK/设备一致，安装前后存档一致，7个关键源码SHA在Player PDB匹配。
- 本机launchd标签`com.matesoul.ai-gateway`持续运行，头显ADB reverse 8787已建立；
  Development地址`http://127.0.0.1:8787`。该桥接不等于公网部署，正式玩家
  需要HTTPS网关。详细操作在`services/ai-gateway/README.md`。
- 本轮不编辑现实遮挡、角色、场景与URP。并行任务在构建后继续修改
  `RealityOcclusionMeshRenderer.cs`，不要把当前磁盘源码当作本APK快照。
  并行D44功能是否验收由其任务判断，不从此处UI/网关回归推断。
- 详细记录：`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_AI配置与HUD放置交互_20261025.md`。
  数值：`/Users/bytedance/MateSoul/项目沉淀资料/validation/ai_hud_placement_20261025.json`。

## APK内AI迁移工作边界（2026-10-25，进行中）

- 用户明确采用MateFairy式本地AI编排，不再依赖MateSoul网关；Demo方案10.3
  已同步。平台保持Unity/C#，不能复制Kotlin/Spatial SDK运行时。
- HttpLLMProvider应直接实现供应商Chat Completions协议，普通与自发回复
  共用请求客户端；从PlayerAIConfiguration按请求读取密钥、模型与供应商地址。
- 迁移conversation-service的上下文提示词、目标分组、最多4轮工具调用，
  一次回复修复，以及spontaneous-reply-service的独立事件提示词和格式校验。
- 联网搜索/网页读取作为注册的进程内工具，无Node stdio子进程。
  保留公开URL、重定向、超时、正文大小边界及取消传播。
- 继续使用ConversationOrchestrator与AvatarActionCoordinator的现有状态和资源
  所有权；模型输出必须经过动作/目标/情绪注册表校验，不直接写动画或Transform。
- 配置页取消网关令牌，默认供应商HTTPS地址和模型；旧网关配置按记录版本
  迁移，保留玩家密钥，不能将旧网关地址误当供应商接口。
- 尚未完成代码、测试或构建，不计为通过；后续交付需独立真实模型验证、
  Unity回归与新APK指纹/存档核验。

## Phase D44 已验证运行时合同（2026-10-25）

- `PhysicsGrabInteractable`持有单一PlayerHeld预约。两只直接手可同时选中，
  只有drivingHand提交目标；原手退出后按当前世界姿态重算localAttach，
  更新stableGrip，最后手退出才Drop。已选的两手均保持IsSelectableBy=true。
- `PicoDirectHandInteractor`没有open/lostTracking计时器。当前捏合/手指强度
  决定选择，跟踪无效立即退出；reach=.13、握把范围=.12。稳定物品关闭
  detectCollisions后，补充搜索XRI注册表中的已选物品，仍检查距离和遮挡。
- `TrackedHandPhysics`增加实体Palm Support（75×22×85mm），使用
  ContinuousSpeculative、摩擦.8/.65。连续、接近水平、速度<.8m/s时承托，
  快动作继续Sensor限幅；承托不叠加同方向推力，Held碰撞对由手生命周期恢复。
- `FluidStream`每源固定128单元、单mesh/renderer，1536顶点/1536三角形；
  double累积20ms固定步长，空闲不更新，索引仅初始化一次。体积、温度、
  颜色与世界速度随水单元运动；只跨有效开口Receive，墙面/桌面命中洒落。
- 凸杯体封住视觉开口，因此只在开口范围的向下轨迹允许绕过容器凸体，
  仍须实际跨口才接收，障碍和侧壁不可穿透加水。容器液面XZ对齐实际杯身；
  CoffeeBrewIngredientSensor同步开口，水质量加到干重、混温按体积加权。
- 饮水机、水壶LiquidPourer、杯碗倾倒共用流体。旧水柱关闭。池满时不从源
  容器扣水；水壶基于当前混温继续加热，保留常温60秒/空壶拒绝合同。
- `MeasuredMeshRetention`保留Removed直到同UUID刷新或有效新UUID覆盖旧测量；
  Renderer/RealityMeshSource及增量导航一致，暂停沿用，新房间清缓存/导航。
  缓存仅本会话、部分覆盖保守保留；家具视野外移动仍需重新观察。
- `TryResolveNearPlayerPlacement`搜索0.25–0.77m、±30°，桌面优先地板，
  允许小斜面、逐角覆盖与碰撞/可视检查；锚点await之后重新检查近表面。
  `validatedNearSurface`避免旧水平准入拒绝已选斜面，实际生成/库存提交已通过。
- 临时入口在/tmp，构建前移除Editor副本及meta。专项44/44通过；
  批量65/66，整豆首帧触发测试单独1/1通过，记录为时序不稳定。
- 真机已放置物品0，库存31行/49件。最终Player源码、APK、安装与佩戴结果
  以`validation/fluid_handoff_mesh_cache_20261025.json`为准，当前构建进行中。

## APK内AI系统实现与验收（2026-10-26，最终构建进行中）

- 当前HttpLLMProvider直接调用玩家配置的chat/completions，Authorization为玩家
  Bearer Key；不再使用X-AI头、网关路由和服务端配置检查。
- ModelReplyProtocol组织权威persona/runtime、历史和有界目标分组，
  最终JSON限制600字符、四种情绪、当轮开放动作/目标能力、16项注册心情信号。
  拒绝额外/重复字段和错误类型，只允许一次无工具格式修正。
- AIWebTools在APK内执行web_search/web_fetch；Bing RSS搜索、HTTPS网页文本读取。
  每跳校验公网URL和DNS、最多4次重定向、1MiB下载、20k字符提取，取消继续传播。
  AIBoundedDownload限制模型响应256KiB；整轮最多25秒、4轮工具、每轮8次。
- ModelConnection.asset和ModelConnectionSettings保留旧资源GUID。
  GameBootstrap.modelConnection使用FormerlySerializedAs迁移旧场景字段。
  PlayerAIConfiguration版本2迁移旧记录，保留玩家密钥，丢弃旧网关令牌。
- Android密钥仍由AISecretStore/Keystore加密保存，Editor只持久化公开字段；
  真实供应商验收密钥仅放在进程环境，不进入资源或APK。
- 真实模型/专项28项全部通过，普通对话1499ms，工具回灌后4368ms。
  GPU遮挡和既有对话动作回归仍在验证，专属APK与安装结果待补齐。
- Unity后台submit任务默认持续运行会在退出后重新等锁；使用KeepAlive=false的
  一次性任务，并检查日志推进、磁盘空间及真实锁持有者。不能无限重启或移除活锁。
- 文档：`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_APK内AI系统迁移_20261025.md`。

## HUD 高度与俯仰回正合同（2026-10-26）

- 生产实现：`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs`，
  `UpdateWorldPose` 是 HUD 世界定位的单一入口。
- 非立即更新且 `hudInteractionActive` 时保持定位；其余更新先计算水平 yaw。
  头部接近垂直时沿用已有 yaw，不再提前返回。
- 水平朝向差超过 30° 或水平位移超过 20cm 才刷新水平跟随原点；
  `hudFollowOrigin.y` 每次非冻结更新都取当前头显高度，避免缓存弯腰偏差。
- 俯仰目标为基础 −30° 加上 `8 × tanh(headElevation × 0.25 / 8)`。
  有界平滑映射消除硬限幅饱和区，深低头开始抬起时目标也随之回升。
- 最后按当前可见的导航/对话/展开页面中心补偿 Canvas 原点，
  用 `1-exp(-7*deltaTime)` 插值位置与旋转，保留点按和触碰时的稳定性。
- 22 项生产方法隔离回放全部通过；最大回正误差 `1.46759419e-6m`。
  不以合成回放代替 PICO 真机体验验收。
- 空间恢复后，共享工程用生产 `BuildCurrentSceneAndroidIncrementalDevelopment`
  构建已保存场景，BuildPipeline 约 122.9 秒、退出 0。本轮复用同批新产物，
  不重建已被用户删除的临时验收脚本。
- `/Users/bytedance/MateSoul/Builds/MateSoul_HudReturn_20261026.apk`：
  218,746,149 字节，SHA-256
  `1b7252ef8d26210e708aad85d8b1d24c5a2615f8865c29f5ef47ffdf9884d880`。
  CRC 1,569 项、签名、ARM64 及同批原生库匹配通过；GameHud 等 5 份交互源码
  在 Player 和 Android stripped PDB 均匹配。场景/角色/Animator 保护哈希一致。
- APK 校验在新构建结束后立即进行；其后并行角色合并任务启动，
  后续 Library 和角色修改不属于本包验证范围。
- 数值与源码指纹：`/Users/bytedance/MateSoul/项目沉淀资料/validation/hud_return_20261026.json`。
  当前无 ADB 设备连接，安装与真人体验验收尚未完成。
- 已在共享 Unity 释放后移除本轮 `HudReturnAcceptance.cs` Editor 链接与 meta；
  原 22 项验收数值保留，用户删除的临时脚本未恢复或执行。
- 交接说明：`/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_HUD低头回正修复_20261026.md`。

## APK内AI最终构建与后续安装合同（2026-10-26）

- 当前生产基线是APK内模型协议与工具编排；旧ConversationGateway类/资源/令牌
  已迁为ModelConnectionSettings/ModelConnection.asset/玩家Bearer Key。
  不应按旧文档恢复服务器网关、X-AI头或ADB reverse 8787。
- 最终APK：`/Users/bytedance/MateSoul/Builds/MateSoul_APKLocalAI_20261026.apk`，
  SHA256 `1b7252ef8d26210e708aad85d8b1d24c5a2615f8865c29f5ef47ffdf9884d880`。
  为当前场景ARM64/IL2CPP Development构建，完整保留当前HUD和并行交互/遮挡源码。
- Player PDB中13个关键文件的校验和匹配当前源码；APK原生库匹配本轮构建，
  1569项CRC、签名、zipalign和PICO能力标记通过。Android SecretStore已编入，
  开发者密钥未被打入包内。保护资产的构建前后哈希相同。
- 真实供应商28/28；GPU/对话回归43/45加两项更新后复测2/2。
  衣装显示名以Wardrobe.CurrentDisplayName为准；角色占用物品拒绝玩家回收，
  外部目标丢失仍终止动作并释放资源。未修改这些现有运行时合同。
- 当前没有ADB设备，安装状态为false。下一步连接目标头显后覆盖安装，
  核对设备APK哈希和安装前后房间存档，再验收密钥保存后重启读取与实际对话。
  不重跑authoring builder，不替换场景/角色，也不再启动旧网关。
- 完整数值记录：`/Users/bytedance/MateSoul/项目沉淀资料/validation/apk_local_ai_20261026.json`。

## Phase D44 最终交接与设备待验（2026-10-26）

- `RuntimeRoomNavigation.TryFindSpawnPosition`仍要求有效实测地面支撑，
  将高度设为max(measuredY, Snapshot.MainFloor.Centroid.y)。稳定地板
  始终保留，不能因实时测量低6cm让出生胶囊穿进稳定地板。
  新回放证明旧路径碰撞、修正后成功，同时真实障碍继续阻挡。
- FluidStream、WaterReceiver、LiquidPourer、手部交接、掌心承托及缓存合同
  保持D44记录；最新47项控制回放、21项房间与GPU遮挡回归通过。
- `Builds/MateSoul_FluidHandoffMeshCache_20261026.apk`是最新修正版，
  SHA256 `1b7252ef8d26210e708aad85d8b1d24c5a2615f8865c29f5ef47ffdf9884d880`。
  19份Player源码含RuntimeRoomNavigation修正匹配；APK原生库、CRC、
  签名、zipalign及构建时场景/prefab/controller保护指纹通过。
- 共享构建日志：`/tmp/matesoul-ai-finish-20261026/build.log`，
  时长2分02.899秒。只在证实其开始晚于当前修改、PDB和原生库完全匹配后复用。
- 临时Editor专项入口/meta已移除，脚本与原始日志位于
  `/tmp/matesoul-fluid-handoff-20261025`，不是生产资源。
- 设备当前未连接，最新修正版installed=false。上一包启动未达READY：
  首轮mesh晚到超时，次轮房间成功但稳定地板挡住全部出生候选。
  新修正仅Unity回放证明，不宣称真机通过；连接后覆盖安装并确认业务READY，
  再测即时释放/两手交换/承托/接水/转头缓存；此前库存重置保持有效。
- 最终数值：`项目沉淀资料/validation/fluid_handoff_mesh_cache_20261026.json`；
  原设备安装与失败记录保存在20261025版本报告，勿混用包指纹。

## 冷月 Direct55 生产资源与合入合同（2026-10-26）

- 当前入口为
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Lengyue_Runtime.prefab`；
  `Assets/MateSoul/Scenes/MateSoul.unity` 保持原有预制体连接，禁止整体覆盖旧独立工程。
- 新资源位于 `Assets/LengyueIntegration/Direct55/`：
  `Face_Direct54_GameExpressions.asset`、`Body_Direct54_LimbRefinement.asset`、
  `SupplementalArms_Direct54_LimbRefinement.asset`、`Hair_Direct55.asset`、
  `HairStrandLines_Direct55.asset`，另有来源 Direct51 的发束线材质/Shader。
- 面部保留游戏网格的 61 个表情帧，仅更新静止顶点和法线；来源的拓扑、UV、
  权重及表情增量逐项比对。身体使用 `game + (Direct54 - sourceBefore54)`，
  保留已验收的肘部、腿部和脚部修正与绑定矩阵。补臂使用原始补臂顶点计算
  相同 4% 瘦身包络，再把位移叠加到已修正的游戏补臂上，避免重复缩减肘部修正。
- 没有改变骨架和旧动画，无需重定向。服装三档与坐姿形变、AvatarVisualDynamics、
  睫毛同步、VRM 弹簧及动作协调器沿用原实现。发束线与头发使用同一 bones/rootBone
  和 bindposes，增加一个蒙皮 Renderer，不增加运行时控制器。
- `Assets/LengyueIntegration/Editor/ProductionCharacterValidation.cs` 已更新为新基线：
  检查 Direct55 网格路径/发束骨骼、SoftFit01、61 个表情、脚部 Z、缺失脚本
  以及正式场景与预制体一致性。一次性合入与运行循环脚本已从 Assets 清理。
- 集成 604 项、运行循环 32 项、正式场景检查通过；范围为 Unity Editor。
  184 个既有动画/服装依赖和 8 个来源文件哈希保持一致，数值报告与唯一对比图
  位于 `ArtSource/LocalStudyOnly/Lengyue_ProductionMerge_20261026/`。
- 使用保存场景的 `BuildCurrentSceneAndroidIncrementalDevelopment` 构建，耗时
  76.711 秒；`Builds/MateSoul_Direct55_20261026.apk` 为本阶段包，SHA256
  `b4ba021b5380813bdd0fc41cd4ad49bf4416ab5ee221d9086c8ef6a0984b0611`。
- Android sharedassets 被分成 1183 个分块。包内验证需按编号从 APK 自身重建后
  解析 Renderer/Mesh 指针，禁止让解析器回退到本地 Library 同名资源来证明包内容。
  五个新网格绑定、面部 61 个表情、CRC/签名/ARM64 与构建指纹均通过。
- 无连接设备，当前安装状态为未安装。早前独立命名的 AI/流体包不含本次角色更新；
  头显需安装 Direct55 包后再验收渲染与交互，不能复用旧包的验收结论。

## 统一最新APK与安装状态（2026-10-26）

- `Builds/MateSoul_AllLatest_20261026.apk`为当前统一交付入口，
  SHA256 `b4ba021b5380813bdd0fc41cd4ad49bf4416ab5ee221d9086c8ef6a0984b0611`。
  与Direct55阶段包字节一致；已经包含AI、HUD、流体、两手交换、实测缓存、
  遮挡与出生修正，不能再拿更早1b7252ef或4fe4777a包代替该完整资源基线。
- 本轮从9份Player程序集PDB枚举嵌套类型的序列点，273份工作区源码校验和
  全部匹配；角色资源integrity指纹匹配。包内sharedassets仅由APK重建，
  五个Renderer/Mesh绑定与61个表情再验通过，没有使用本地Unity资源回退。
- 原生IL2CPP库与构建产物一致，1573项CRC、v2签名、zipalign、
  PICO VST/网格/深度/锚点能力标记通过。
- 重新编译尝试没有成功：Unity许可证机器绑定不一致且无缓存登录令牌。
  没有修改或绕过许可证；交付策略是验证当前源码/资源与已有成功构建一致后复用。
  需要新的编译时，应通过Unity Hub正常恢复许可证。
- 当前USB ADB与mDNS均没有发现目标PB3A10MGL9220170B，installed=false。
  连接后以`adb install --no-streaming -r`覆盖安装，核对设备base.apk SHA、
  安装前后matesoul-v1.json一致，随后启动并观察READY与实际佩戴效果。
- 记录：`项目沉淀资料/validation/all_latest_20261026.json`，
  包内解析中间目录已清理；其余临时证据仅在`/tmp/matesoul-integrated-20261026`。

## 整合包设备后验：安装已完成、实测支撑尚未通过（2026-10-27）

- `MateSoul_AllLatest_20261026.apk`已覆盖安装到PB3A10MGL9220170B。
  设备包SHA256=b4ba021b...b0611与本地一致；存档安装前后完全相同，
  SHA256=1d70e575...6eb0c。本轮无生产源码修改，未恢复已删除的临时脚本。
- `am start -W`成功，Activity initial display为236ms；此值不代表业务就绪。
  PID11936/Awake，Scene Capture返回房间，floorY=0.640，NavMesh构建完成。
  8块实时网格和512×512深度已经启用，现实遮挡不缺失。
- `TryFindSpawnPosition`连续报告13个候选全部缺实测支撑：
  noSupport=13、outside=0、overlap=0。8秒等待后启动异常，avatarBound=false、
  businessReady=false。此前max(measuredY,stableFloorY)修正不绕过实测支撑准入；
  当前失败在该高度调整之前，不能用放宽碰撞或删除地板来解释/掩盖。
- 继续处理启动时需核对标定地面与实时网格坐标/高度、真正可见地面支撑。
  本轮仅安装及读日志，未改房间几何准入，不能宣称真机角色或交互已验收。
- 完整记录：`项目沉淀资料/validation/all_latest_install_20261027.json`；
  原始后验：`/tmp/matesoul-install-20261027/startup.log`。

## HUD 展开页面的导航定位基准（2026-10-27）

- 当前实现：`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs`，
  `UpdateWorldPose` 在普通页面展开/收起时始终补偿 `navigationRoot` 中心；
  对话模式继续使用 `dialogueRoot` 中心。
- 内容面板沿用 `BuildPages` 的本地位置 `(0,200)` 和尺寸 `760×330`，
  导航为 `824×160`，面板在导航上方展开，平面边缘间隔为40单位/2.64cm。
  展开页面中心不再引起整个HUD按285单位向下重定位。
- 仅当 `contentRoot.gameObject.activeSelf` 时，从目标世界中心减去
  `Vector3.up * HudExpandedNavigationDrop`；默认值为 `.02f` 米。
  现有指数插值负责2cm平滑下移，收起后回到默认导航位置。
- 默认45cm距离、−20cm高度偏移、有界tanh俯仰、连续头显高度跟随、
  20cm水平停留区和交互冻结继续沿用。此规则替代旧“展开页面中心定位”合同。
- 生产源码几何核算：旧按钮默认下移16.2899cm，新下移2cm；
  面板与导航中心间距沿Canvas平面为18.81cm。几何核算不是Unity运行时姿态回放。
- C#编译通过；首轮Unity在Burst child domain的Mono泛型实例化中崩溃，
  未产生新APK。同步重试前另一个启动诊断构建占用工程；后续复用其新成功构建，
  BuildPipeline耗时104.376秒，当前GameHud SHA-256
  `4b7f59f6b6a7091800716709ed02690cc3e19740cb63723fa17f062e2f4b5ebd`
  在Player和Android stripped PDB均匹配。
- 交付APK为 `/Users/bytedance/MateSoul/Builds/MateSoul_HudPanelUpward_20261027.apk`，
  SHA-256 `19b8a6c90ecbeed71803d1df9adaa3c4bd5e850e430f5632ba3dd4a5f464cc63`。
  CRC、签名、zipalign、ARM64/IL2CPP及本轮保护资产哈希均通过。
- 设备PB3A10MGL9220170B已安装相同包，本轮核对base.apk哈希后复用该安装；
  核验期间存档SHA-256保持`1d70e575...6eb0c`。ADB服务恢复后保留已有7781
  诊断映射，不再次安装/启动应用；实际佩戴效果仍待人工验收。
- 本包包含并行启动诊断日志，用于开发验收。HUD定位改动不处理实测地面支撑
  缺失，不能把本次包校验或几何计算当作房间启动READY的证据。
- 临时数据在 `/tmp/matesoul-hud-panel-upward-20261027`；
  最终验证记录为
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/hud_panel_upward_20261027.json`。

## 启动房间与实测出生点一致性（2026-10-27）

- `GameBootstrap.InitializeRoomAsync` 在角色启用、动作协调器和Session创建前
  完成房间/出生准入。`BuildRoomAndFindSpawnAsync` 负责Room.Build、
  ApplySnapshot、玩家前方首选点和8秒实测等待；等待返回可空Vector3，
  取消继续抛出。捕获来源没有出生点时只调用一次现有Spatial Mesh加载路径，
  再次失败仍报错。有效捕获房间继续使用原来源。
- `LoadSpatialMeshRoomAsync` 统一原Scene查询超时回退与新的实测支撑失败回退，
  记录具体原因；两次场景构建使用最终snapshot配置现实物品和AI世界上下文。
  不另建恢复状态机，不提前启用角色，不改变运行中的动作/动画所有权。
- `RealityOcclusionMeshRenderer.ApplySnapshot` 在换房间清理native/pending和
  旧导航表面后调用RefreshCachedNativeMeshes，重新烹调并注册SDK缓存；
  SDK仅发Unchanged或头显静止时也能恢复当前网格支撑。
- 不改变RaycastNonAlloc长度0.7m、起点上移0.35m、最大高度差0.15m、
  normal.y≥0.7与角色胶囊重叠检查。原0.640m标定与约1.16m实测不匹配
  时必须拒绝原候选；替换为实测snapshot后重新验证。
- 本轮22项Unity回归覆盖倾斜实测地面与52cm高度差、重建与有效capture、
  无网格和取消、真实GPU深度遮挡及HUD。最终真机自然启动使用Scene超时
  回退，10块网格、3个候选无拒绝并到READY；不能写成新分支自然真机触发。
  绝对floorY随tracking定位变化，本轮最终0.73256m，禁止硬编码诊断高度。
- Android实际存档在`/sdcard/Android/data/com.matesoul.game/files/matesoul-v1.json`，
  不在run-as内部files目录。安装前停止现有会话，读实际存档，覆盖安装后再比对。
- UnityLinker生成的PDB可能HashAlgorithm=None但Hash仍32字节；
  使用32字节SHA256/20字节SHA1校验并拒绝未知长度，不能把None一律当SHA1。
  Mono.Cecil需选Unity自带GAC的0.11.1，`lib/mono/unity`下旧版本不支持此接口。
- 最终APK与5份关键源码在Player/Android裁剪PDB、同批IL2CPP库一致。
  1,184项sharedassets与已解析Direct55整合包完全一致，61个表情保留。
  交付`Builds/MateSoul_SpawnFix_20261027.apk`已安装，SHA256=15da3544...de1720；
  本轮3处HTTP探针已经删除。报告与数值路径见角色出生点修复验证文档。

## 虚拟手透明渲染约定（2026-10-27）

- 资源：`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/Resources/MateSoulHands/TrackedHand.shader`。
  `PicoTrackedHand.Configure`为左右官方手模型分别创建该Shader的材质，
  默认 `_BaseColor` 为 `(0.77, 0.83, 0.85, 0.5)`，无需修改SDK预制体。
- 使用Transparent渲染类型/队列，片元输出 `_BaseColor.a`；
  RGB混合为 `SrcAlpha / OneMinusSrcAlpha`，Alpha通道混合为
  `One / OneMinusSrcAlpha`，保留MR合成需要的覆盖Alpha。
- `ZWrite Off`，移除原Lit的DepthOnly/DepthNormals引用，避免透明手
  以完整轮廓写入不透明深度；仍使用 `ZTest LEqual` 和 `Cull Back`。
  原有颜色、光照、骨骼、尺寸、追踪、物理及交互参数保持不变。
- 本轮仅验证源码修改范围和差异格式；Unity编译、目标PICO上的外观及
  透明物体叠加效果尚未验收，后续构建才能进入安装包。

## UI 面板样式升级边界（2026-10-27，进行中）

- 实现入口为 `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs`。
  用户明确保留底部导航按钮外观与现有色调，范围为内容页面及其交互动画。
- 计划将页面按标题/说明、分组卡片和底部操作区重排；新面板按钮只管理
  自身颜色与文字反馈，沿用Unity Button的onClick，不移动根点击矩形。
- 面板与页面淡入/淡出由一个生命周期入口管理，快速切换继续当前视觉状态；
  继续使用unscaled时间、既有HUD回正和展开2cm下移规则。
- 当前AlwaysOnTopUi生产设置为sortingOrder=10、TMP isOverlay=false。
  旧GameRuntimeTests中的32760/true与已删除握拳字段断言过期，
  不能据此回退生产渲染/输入实现。
- 现阶段仅完成探索，尚未实施；后续验证在隔离场景与内存存档中进行，
  不拍摄头显现实相机、不写用户游戏存档，不恢复已删除的临时脚本。

## UI 面板内容升级实现（2026-10-27）

- `HudPanelButton`继承Unity Button并沿用onClick，是内部按钮表面与文字颜色
  的唯一所有者；只缩放Label，根RectTransform、raycast矩形与业务回调不变。
  分类与设置标签通过SetSelected设置状态，避免外部Image颜色写入与动画竞争。
- `GameHud.AnimatePanel`统一控制内容根与各页CanvasGroup。快速切换停止旧协程，
  从当前透明度与位置继续；过渡期间关闭内容交互，结束只保留目标页可点选。
  保持activePage为页面逻辑状态的唯一入口。
- 内容760×448，HUD高度760，字幕位于y=668。导航尺寸与世界位置补偿方法
  保持原实现，展开下移仍为0.02m。新128像素圆角Sprite独立于原导航Sprite，
  与材质一样由GameHud销毁生命周期释放。
- 验收脚本在 `/tmp/matesoul-ui-panel-style-20261027`，通过临时测试入口调用
  既有GameRuntimeTests的内存场景；实际URP渲染请求用于绘制UI。
  测试保护并恢复PlayerPrefs，不读取或保存用户实际游戏场景存档。
- Unity TestRunner跨EnterPlayMode不能可靠恢复迭代器的编译器闭包。
  本轮IL证据表明捕获interactionButton的Any表达式使闭包在切换前分配，
  切换后闭包为空；改用foreach避免捕获。ExitPlayMode需由清理外层迭代器yield，
  不能直接从嵌套清理协程yield。此问题属于验收脚本，非UI运行时缺陷。
- Unity实例识别使用ps的comm列，避免正则误匹配shell包装命令或漏掉版本号
  中的f1。构建前保存保护资产快照，区别并行任务在本轮期间已授权的更新。
  最新完整验收和APK交付仍待完成。

## UI 面板升级验收合同与交付状态（2026-10-27）

- 本轮完整验收已通过。用空场景、内存存档和实际URP相机渲染9个页面状态，
  检查Graphic边界与TMP文字截断；实际GraphicRaycaster命中动画按钮后再执行
  点击，验证根点击区域保持不变。另验证快速开关/切页后的唯一可交互页面、
  分类与系统开关回调、隐藏恢复和AI配置返回对话的草稿保留。
- 验收通过后的GameHud/HudPanelButton源码SHA-256与Player构建一致。
  GameHud、HudPanelButton、GameBootstrap、RealityOcclusionMeshRenderer
  在PlayerScriptAssemblies及ManagedStripped中均通过源码校验；APK内
  libil2cpp.so与构建输出字节一致，剥离后的程序集和包内均无NUnit/TestRunner
  或MateSoul测试程序集。
- APK使用BuildCurrentSceneAndroidIncrementalDevelopment构建保存的主场景，
  不执行角色或场景重建器。保护文件按构建开始时快照验证，记录构建前并行
  更新；sharedassets有5个分片变化，但五个生产角色Mesh原始数据与SpawnFix
  包一致，Face_Direct54_GameExpressions仍有61个形态。
- 构建阻断的UGUI问题来自失效的生成缓存，清理PackageManager的ProjectCache
  与错误UGUI缓存链接后重新解析，未修改依赖源码或manifest。地面支撑方法
  曾插入到CanReplaceCalibration声明与方法体之间；仅重排声明并移除重复括号，
  保持两个方法原有逻辑。用户统计/SDK缓存的沙盒写入拒绝没有获得绕过授权，
  本轮通过BuildReport、编译源码、原生库、CRC和签名确认已生成APK有效。
- 交付包为`Builds/MateSoul_UIPanels_20261027.apk`（Development、ARM64/IL2CPP）。
  完整数值证据位于`项目沉淀资料/validation/ui_panel_style_20261027.json`。
  头显未连接，installed=false、runtimeReady=null；覆盖安装后的存档保持、
  READY及实际VR观感/手势体验尚未验证。编辑器验收未采集现实相机画面。

## 手部释放、有限推力与房间地面支撑合同（2026-10-27）

- 入口位于`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/`：
  PicoTrackedHand、PicoDirectHandInteractor、PhysicsGrabInteractable、
  PhysicsBody、TrackedHandPhysics；地面所有者位于
  `Assets/MateSoul/RoomNavigation/Runtime/RuntimeRoomNavigation.cs`。
- 多指抓握按捕获指组的第二强弯曲值维持；单指残留不延迟松手。
  原生pinch逻辑独立。统一IsContinuousMotion按采样间隔验证：
  位移≤0.02+6×dt米，转角≤10+20×Rad2Deg×dt度，dt在0.0001～0.1秒。
  有效速度输出仍限制为4m/s、12rad/s；追踪失效清除运动连续性。
- XRI退出选择早于本帧持有物更新，释放必须读取当帧手样本并换算到
  物品质心；不能重新加入旧物体速度均值。双手交接保留现有选择/控制权，
  最后一只手松开才结束跟随。近墙/桌释放通过Rigidbody.SweepTestAll
  限制进入碰撞面的速度，保持当前物品姿态并配置CCD。
- ApplyHandPush使用接触点相对速度：closingSpeed=Clamp(
  handNormalSpeed-bodyPointNormalSpeed-0.035,0,4)，有效手质量0.35kg，
  impulse=(0.35×itemMass/(0.35+itemMass))×closingSpeed，使用Impulse模式。
  接触偏心扭矩上限为impulse×0.15。穿透仅证明接触，不生成持续静态推力。
  ObserveContact必须保留手速法线符号；提前截为零会在双方分离时产生
  反向冲量。已有多指合并与持有/暂停/业务控制权边界继续生效。
- SpatialMesh导航地面是局部可见连通区域，不能作为所有掉落物品的唯一
  支撑。Measured Room Ground Support使用实测主地面高度、0.1m厚度及
  有限测量范围，无Renderer、不进入NavMesh、不作为真实测量出生点证据。
  UpdateLiveSurface在导航覆盖相等的提前返回前扩展基础支撑，使用世界
  变换后的8个边界角。块替换保留已观测范围；Clear/重建清除所有者字段。
- 30项Unity回归与31组专项数据通过，包含1m/13.89ms异常瞬移拒绝、
  30FPS快速释放、甩离无刹车、墙边释放、普通/凸网格落地、新扫描覆盖、
  控制器抓取、出生点与现实遮挡。包内7份关键源码在Player及Android裁剪
  PDB均匹配，原生库与构建输出一致。Mono源码校验使用Cecil0.11.1，
  需定位Unity的arm64 libMonoPosixHelper；无需重建APK处理工具加载问题。
- 最终APK与报告路径见工作汇报。本轮HTTP探针、诊断服务和项目内临时入口
  已清理；数值归档后清除诊断记录/NDJSON/env。设备未连接，installed=false、
  runtimeReady=null，不能把Unity回放描述为真人手势验收。

## 稳定握点持有阶段的网格穿越修复（2026-10-27）

- 原地面扫描缺口属于独立问题；不能用它单独解释玩家手持后的高落穿概率。
  ItemGripAnchor的Handle/LoopHandle路径在持有时关闭自动碰撞，旧
  ApplyStableFollow按25mm/10°采样并容许12mm穿透，薄物品可先进入网格
  背面/内部再穿到下方。实际生产地面网格即使生成80mm厚度也会复现。
- 修复在`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/PhysicsBody.cs`。
  ApplyStableFollow从当前姿态沿平移方向SweepTestAll，过滤离开接触面的
  命中与既有stableCutting可切割对象例外，限制行程并保留2mm间隙。
  每个旋转/占位样本的平移进度不得超过该行程；仅此路径使用1mm穿透容差。
- 保留人工持有的自动碰撞关闭策略，避免无限质量手持物甩飞周围动态物品。
  EndFollow恢复碰撞/重力/动态刚体；不能把本次失败归为松手漏恢复，因为
  对照中释放状态全部正常，越界发生在释放前。
- 72组对照、48组有支撑场景全部正常，含3mm薄物品、偏移初始高度、沿面
  滑动和抬离；最终8项Physics/Grip/Boundary回归通过。扩大回归的两个
  角色质量断言在原逻辑下同样失败，旧逻辑源码与前版APK哈希完全一致。
  Reach角旧/新同为53.3893929°，Grip误差旧.107554436/新.107552461m；
  本次未修改角色资产，也未放宽测试阈值。
- 新包`Builds/MateSoul_HeldReleaseBoundary_20261027.apk`含本补充修复，
  原HandThrowPhysics包保留为历史交付；两包不可混用验证该分支。
  JSON与报告路径见工作汇报。真实PICO发生概率未由引擎回放统计；设备未连接。

## 生气反应的动作所有权与音频时钟（2026-10-27）

- 实现为`ResponseEmotionAudio.SynchronizedAngryAction`，注册ID
  `response-emotion-angry`，不暴露为AI可调用动作，情绪回包自行提交。
  AI优先级，控制Locomotion|FullBody|Gaze|Face；不新增独立动作执行器。
  `AvatarActionCoordinator.HasBlockingClaim`使用协调器既有优先级判断，
  避免靠错误字符串轮询提交。暂停/恢复边界继续由GameSession拥有。
- 对话ReplySequence/主动回复ReplySequence去重后进入情绪队列。
  无同级或更高优先级冲突时提交；动作Start设置Emotion权重1、Angry
  normalizedTime=0并播放音效。状态speed=0，Tick以timeSamples/frequency
  换算原动画秒数；到1.6333333秒后保持末姿直到2.5秒音效结束。
  Cancel立即停止两者，Dispose释放层；生命周期停止不会自动重播。
- `AngryEmotionBuilder.Import`调用既有Generic骨架转换并单独映射旧面部
  语义，只添加Emotion层，不重建其他层/转身片段。55骨骼、550身体曲线，
  输出GUID=`74856d42c549b41f188c5ce491b8072f`。原FBX静态闭眼在Unity
  AnimationClip中被省略，需补齐恒定100权重；三条变化嘴形曲线必须存在，
  遇到额外有效且未映射的曲线直接报错。
- 原sad嘴形→Fcl_MTH_Angry×0.65和Fcl_BRW_Angry×0.35；
  Puff→Fcl_MTH_Small×0.35和Fcl_MTH_U×0.12；
  extra_01→Fcl_MTH_Down×0.25。这是现有生产拓扑上的表情适配，
  不是同拓扑形态复制。其余面部曲线归零以隔离底层表情，睫毛继续由
  AvatarVisualDynamics复制权重；61形态网格、角色Prefab与场景保持原值。
- 编辑器菜单：`MateSoul/Lengyue/Import Angry Emotion`；
  核心源文件为`Assets/Characters/SapphiArtCustom/Lengyue/Editor/AngryEmotionBuilder.cs`，
  资源在同角色`Runtime/Animations/Emotion/Angry`目录。启动通过
  `ResponseEmotionAudio.Attach(character, Session, Session.Conversation,
  Session.SpontaneousReplies)`绑定。未绑定Session的旧音效工具路径保留。
- 两项专项回归与两项既有音效回归通过：检查AI入口、播放时钟、声音与
  Animator状态、等待、暂停、seek、待机接替、玩家抢占与表情恢复。
  真机显示/音频设备的输出延迟不等于AudioSource内部采样进度，不能将
  本次数值误差描述为设备层零延迟；本轮未打包安装。报告与JSON见工作汇报。

## 高处释放故障的证据范围与未决项（2026-10-27）

- 用户实际操作中释放前物品始终在地面上方。稳定手持目标强制越界测试只验证
  ApplyStableFollow 的边界约束，不能作为高处释放故障的根因或修复验收。
- 生产 GameItem.Bind/RuntimeColliderRefiner、真实 PicoDirectHandInteractor/XRI
  退出路径和相同释放姿态/速度的未抓取对照共 380 组均落地，释放状态全部正常。
  碰撞体覆盖 Box/Mesh/Sphere；最小释放下边界 0.13378787m。没有证据支持
  在这些受控样本中漏恢复碰撞、手掌接通导致落穿或既有地面高速漏碰。
- 用生产 RealityOcclusionMeshRenderer 做每步地面替换：5 个物品各 150 次，
  没有落穿。本地受控排除与真机排除必须分开，真机仍 INCONCLUSIVE。
- 独立物理测试的 session=null 会跳过真实碎裂，因此另用完整 GameSession
  和内存存档验证杯/盘。6 个原物品不穿地，4 个经 BreakItem 合法退役；
  1.8m 马杯的一个重度碎片落穿。不能把碎片和原物品身份混为同一故障。
- 本轮只添加 Editor 且 MATESOUL_ELEVATED_RUN 标记下的状态探针，未改业务
  逻辑，未打新包。PhysicsBody 去除本会话探针的哈希仍为
  fa5454a74c0107fd4202494c2f67c51b545e61b483fc85b241e9f397e05d3c49。
  角色、场景、Animator、手 Shader 的哈希不变；临时测试入口已移除。
- 诊断会话 OPEN；外部目录 `/tmp/matesoul-elevated-hand-release`、记录
  `/tmp/debug-elevated-hand-release.md`。当前需要接入头显并确认实际物品，
  再采集释放状态、手掌冲量、落地姿态、支撑几何和碎裂身份转换。
  报告与关键事件行号见高处释放复核 JSON，不得将其标记为真机修复完成。

## 2026-10-27：行走动画的距离时钟与实际速度同步

生产行走由 `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/AvatarWalk01Builder.cs` 导入，输出到现有 `Lengyue_Walk_BodyOnly.anim`，其 GUID 维持 `ba4f556c58f4c464eaa45be56dcd387e`。

1. 从原始 `Lengyue_LayeredDress.fbx` 读取参考骨架的世界旋转，把动画旋转转换到当前生产骨架。位置使用旋转相对的世界米制差值；不能用 `PelvisRoot.InverseTransformPoint` 抵消 FBX 的厘米缩放，否则可能产生百倍位置误差。
2. 保持当前骨长，将源脚部位置增量按腿长比例映射到当前静止脚位，用双骨 IK 求解膝盖和踝部。两条腿的可达高度共同约束骨盆，限制最大适配为 5 cm；当前片段实测最大 2.5021 cm。
3. 对完整循环分配首尾残差，再用支撑脚的向后位移作为距离时钟，保持循环长度、以 120 Hz 重采样身体曲线。运行时不再需要额外的行走 IK 执行器。
4. `AvatarMotor` 每个渲染帧读取实际水平速度，设置 `Speed` 和 `WalkPlaybackRate`。参考速度为 `0.6221811f`，默认速度为 `0.75f`；Locomotion 启用该速度倍率参数，完整行走混合阈值为 `0.12f`。

零行走速度时倍率恢复 1，维持正常待机时钟；转身和其他动作状态不使用该参数。暂停与坐卧接触不播放行走。PhysicsBody 继续拥有角色根节点，Animator 仅驱动骨骼，Root Motion 关闭。

`AvatarLocomotionBuilder` 和 `LengyueIdle01Builder` 使用统一的完整行走阈值，避免重建控制器或导入待机后退回 0.9 m/s 的旧混合方式。`AvatarWalk01Builder.ConfigureWalkPlayback` 仅修改现有行走状态，避免将行走导入耦合到尚在制作的其他动作资产。

验证使用保留生产 305 个 Transform 的骨架和生产控制器，并在最小 Rigidbody 夹具中执行生产速度更新方法。60 个支撑阶段最大漂移为 1.055 mm；报告范围为骨架/Animator 回放。来源、标定值、工况和检查项见 `/Users/bytedance/MateSoul/项目沉淀资料/validation/walk01_stride_20261027.json`。

动画验证工程使用 Unity 内置模块即可，不需要完整 PICO/URP 启动。验证副本不得共享可写包缓存；UPM 外层退出状态、Unity 核心成功日志和实际产物需要分别核对，避免将后台 metrics 写入限制误判为动画失败。

本轮最后在主工程完成全部生产脚本编译及资产导入，Tundra 成功、Unity 退出码 0；生产片段哈希和控制器参数与验收结果一致。生产代码保留导入器，主工程临时验收入口已移除。

## 2026-10-27：保存主场景 Release 构建与实体头显交付

可复用启动入口为`/Users/bytedance/MateSoul/scripts/run-headset.py`，
由`.trae/environments/run.json`执行。使用现有
`MateSoul.Editor.GameSceneBuilder.BuildCurrentSceneAndroidRelease`；
不可替换为会重新生成场景/角色的`BuildAndroid`入口。

流程为检查Unity进程/锁与实体设备，保存源码、动画、控制器、Prefab和
项目设置快照，构建保存场景，核对399项输入未变，生成带时间戳APK，
检查CRC和ARM64运行库，覆盖安装，核对真实存档与设备APK哈希后打开Activity。
40分钟总时限和10分钟日志无进展时限只终止本脚本拥有的进程组。
构建日志与快照保存在系统临时目录。

本次交付`Builds/MateSoul_AllLatest_20261027-214501.apk`为Release，
实际164,918,629字节。Player与ManagedStripped共36项源码证明通过，
原生库与Release staging字节一致，测试程序集排除。Unity的BuildReport
totalSize包含未压缩资源，不可当作实际APK大小。

包内资源检查需同时重组`sharedassets*.assets.splitN`与
`globalgamemanagers.assets.splitN`，并读取独立的32位GUID资源文件；
半透明手Shader位于`27a146a06e2274d189f122e94ac4c1e0`，默认Alpha=0.5。
新版行走和生气片段长度分别为1.066666722与1.633333325秒；
五个Direct54/55生产网格与旧SpawnFix包相同，面部61形态保持。

编译证明完成后并行Editor诊断修改了PhysicsBody；应保留已完成证明并
与构建开始快照校验，不能把随后源码哈希差异伪装成编译通过。
当前新增内容受`UNITY_EDITOR`保护，没有新增Android生产执行逻辑。
后续新生产修复仍需新构建。

Unity核心退出0与外层TRAE沙盒拒绝统计/用户级缓存写入可能同时出现；
分别核验退出、fresh APK、设备包哈希与安装结果。实体头显无人佩戴时
Activity启动成功仍可能自动休眠；本次仅验证Unity/UI/输入初始化和
PID存活，未出现`MATESOUL_READY`，不得宣称房间和真人交互验收通过。
详见`项目沉淀资料/MateSoul_最新整合包安装_20261027.md`。

## 持刀挥舞的主线程工作量与切割重试（2026-10-27）

- 持刀稳定握点由PhysicsBody.ApplyStableFollow在Dynamic阶段求解。
  N=Clamp(Ceil(Max(逐帧目标位移/0.025m,逐帧目标旋转/10°)),1,60)；
  生产刀三个BoxCollider，每个样本各执行OverlapSphereNonAlloc和候选处理。
  大幅动作的总幅度不能直接代入N，必须使用单帧目标差距。
- 刀柄范围球半径约0.333m，实际柄长约0.133m；子形状中心偏移扩大宽相。
  12个不与刀实体接触的邻近网格，在极端1.2m/90°目标差距下产生144个
  范围查询与1152次穿透判定，Mac均耗2.241ms；平移扫掠约0.0023ms，
  主要工作量来自重复占位/候选处理。不能用Mac数据代表PICO帧耗时。
- RuntimeCuttingTool.nextCutTime仅在session.TryCutItem成功后设置0.14s。
  GameSession.RuntimeCutting先Slice、SpawnCutPiece、BakeMesh、SyncTransforms，
  后检查新切块空间；空间失败仍支付上述成本，回滚后释放源物品预约。
  受控失败调用中位7.609ms、P95 14.201ms，值得先限制失败重试并前移
  可提前完成的准入检查。本轮没有修改业务流程。
- 独立空中搜索约0.0015ms，完整切割3.70–9.21ms；内存存档不包含真实
  磁盘延迟。连续20次拒绝调用是压力测试，不表示设备每帧会重试20次。
- GC.GetAllocatedBytesForCurrentThread在当前Unity运行时未通过32KB校准，
  归档中分配数值标记无效；不得依据0值排除GC。实际卡顿帧及房间网格
  更新时间未采得，真机归因保持INCONCLUSIVE。
- 三项诊断测试通过；仅本会话计时计数探针已移除，其他会话探针保留。
  报告与完整证据事件见持刀挥舞分析及knife_swing_profile_20261027.json。

## 稳定握持候选复用与切割尝试限频（2026-10-27，后续实现）

此节是同日分析后的生产实现，更新了上节描述的稳定跟随和切割冷却路径。

`PhysicsBody.StableFollow.cs` 是 PhysicsBody 内部的 partial 实现；仍由共享
物理所有者执行握持运动。BeginFollow首次稳定握持分配复用数组，每次更新快照
子碰撞体的根相对姿态与当前世界AABB，对全部25mm/10°采样姿态的原球形
查询范围求AABB并集。一次OverlapBoxNonAlloc取得最多256项障碍候选，快照
障碍姿态、包围盒、稳定切割例外和忽略碰撞对；各段先AABB剔除再ComputePenetration。
各段形状剔除范围旋转的是当前世界AABB，覆盖偏心子物体及非均匀缩放，
不能只旋转局部尺寸。Rigidbody插值会令Physics查询的包围盒和可见Transform
姿态不同；障碍剔除范围须同时保留物理包围盒和按刚体姿态差变换到可见姿态的
包围盒。ComputePenetration仍使用Transform姿态，与公共CanOccupy保持一致。

平移SweepTestAll、2mm间隙、60段上限、1mm穿透容差保持。缓冲区饱和时
保持当前姿态，不把截断候选当成安全空间。RefreshColliders重建形状和配对缓存，
EndFollow清除碰撞体引用。公共CanOccupy继续服务放置、存档恢复和其他占位检查；
普通跟随与脱手动量算法没有改写。不要把复用数组解释为零分配：
SweepTestAll仍返回数组，切网格、碰撞烘焙和保存仍有同步成本。

RuntimeCuttingTool在首个真正接触的目标尝试前设置nextCutTime=fixedTime+0.14s，
成功与失败都消费尝试；每次刀划最多处理一个目标。候选去重前移到刀刃接触检查前，
空中挥舞不消费冷却。Configure清零冷却，禁用时释放候选引用。
GameSession.TryCutItem在预约前检查切割深度和切面与CollisionMesh.bounds的
投影交集。原物体穿透深度不是切开后两块的等价安全判定，不应直接代替后置检查；
切块占位验证、提交保存及回滚继续由GameSession所有。

同方法各180次Mac Editor计时：12个邻近网格下0.30m/45°均值1.619→0.060ms，
极端单帧1.20m/90°为4.699→0.197ms。修正后的源码29项独立回归全部通过，
包含480组旧求解器比对、平移和偏心旋转插值障碍各40帧回放，以及生产刀真实
XRI选择/释放流程；受控手包不等于真机真人动作。真实头显卡顿归因和GC停顿
保持未确认，不能把跟随CPU耗时降低比例解释为头显帧率增益。
保存场景Release包为`Builds/MateSoul_KnifeMotion_20261027.apk`；654项受跟踪
构建输入一致，签名、对齐、ARM64原生库及插值修正生成代码核验通过，未安装头显。
报告与数值见持刀挥舞性能优化和knife_motion_optimization_20261027.json。

## 2026-10-27：拾取动画时间线与高度混合

资源目录为 `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/ItemActions/Pickup/`。
`PickupAnimationBuilder.Import` 只导入这三段素材并绑定当前 Prefab/控制器。
源 FBX 的首帧不是静止骨架；使用 `Lengyue_LayeredDress.fbx` 的 PelvisRoot
参考坐标、世界旋转差和目标骨长，避免直接复制源局部旋转导致腿/躯干扭错。
仅映射 53 个身体/手指骨骼；髋部平移保留并按腿长缩放，静止骨长不变。

控制器增加 Pickup 层、PickupHeight 参数、PickupHeightBlend 状态；
`PickupAnimations.asset` 按高度升序保存 clip、contactTime、contactGrip。
三个稳定接触段重采样到归一化 0.5，因此相邻片段按高度混合时抓取不会抢先。
`PickupPerformance` 提供组合时长和接触位置；之后添加 High 应遵循同样的
接触相位、标定和内容注册流程，不能在协调器增加物品名分支。

`ItemTransferStep` 反算接近站位，Reach 到接触帧等待 LateUpdate 后执行吸附，
Lift 正向播放剩余片段，PickupBlend 0.3s 后交回 Carry。社交注意力在全身
拾取期间关闭，避免头部状态覆盖作者表演。`AvatarItemRig` 在此期间跳过旧
程序化全身姿态，只执行有限手部接触、脚底支撑、手势和物品跟随。
取消/结束由现有动作生命周期归还所有权，ClearPickup 清除层权重及接触偏移。

ThinEdge 首次吸附保留物品原朝向，防止低位盘子远边被强制旋转到地面以下。
持物手势依据 itemFromGrip 重建物品实际抓握坐标，薄片捏持与其他持物共用同一
跟随所有者。脚底下沉修正仅抬高脚踝目标至 Prefab 标定值，不挪动角色根节点。

259 项几何、持物、释放、连续高度和脚底检查通过；2 项完整流程/取消回放通过。
原始握刀距离检查仍失败：相同骨架基准下旧 Reach 与新 Pickup 误差相同，
专项回放仅将此作为已有基线，并保留原始测试。不要宣称手势问题已修复。
Release 构建输出 `Builds/MateSoul_PickupAnimations_20261027.apk`；154 项构建输入
前后一致，CRC、ARM64 IL2CPP 和签名通过。APK 实际 165,581,061 字节；不要将
Unity BuildReport 的未压缩总量 2,334,665,721 字节写成 APK 大小。
本轮未执行安装；真机验收与构建结果以三档拾取接入报告为准。

## 2026-10-27：面向实际物品实例的拾取入口

`GameHud.HudPage.PickupItems` 使用现有 HUD 页面切换动画。列表从
`GameSession.ItemViews` 中筛选当前有效、可移动、单手抓握的物品，按
ItemDefinition 汇总并分页；数量只计算未占用实例。实际点击时重新检查实例，
按角色距离排序后提交 `AvatarIntent("pickup-item", UI, instance.Id)`，
因此回收和占用变化不会令一次过期选择直接获得物品控制权。

`PickupItemAction : ItemActionRun` 使用可选的 `ItemActionRequirement.InstanceId`
约束目标，沿用既有资源预约和动作生命周期。ActionName/StartNotice 允许子动作
提供正确业务提示，原切水果默认值保留。新动作注册身体/移动/视线/双臂资源，
不向 AI 公开。无需新增 Animator 执行器或物理所有者。

`ItemTransferStep` 无目的地的构造重载表示仅拾取；复用 Approach/Dock/Reach/
Lift/PickupBlend 后进入 Hold。Hold 保持 Carry 和既有手部跟随，不触发搬运放下
流程的 35 秒阶段时限；协调器保留 3600 秒整体期限。点击放下/取消调用
Avatar.Cancel，下一次协调器 Tick 经原有 Dispose 恢复碰撞、重力及预约，
并请求物理存档。关闭 HUD 页面只改变 UI，不清理动作所有权。

`GameSession.Submit` 仅用未完成的切水果/拾取动作更新 observedItemAction，
避免被拒绝的新请求覆盖现有取消句柄。结束提示按动作类型报告。
HUD 仅在拾取页可见时刷新列表，且运行期间禁止再提交第二次拾取。

7 项相关回归最终通过，包含真实 UI 点击、三个支撑高度、放下/暂停、过高/占用/
丢失目标、7 种物品分页与库存保持、原切水果和对话状态。临时验收脚本存放在
系统临时目录，打包前移除主工程入口；报告和结果见物品拾取测试面板文档。
保存主场景 Release 输出 `Builds/MateSoul_PickupButton_20261027.apk`；
272 项受跟踪输入前后一致，CRC、ARM64 库、PickupItemAction/BuildPickupPage/
PickUpItem 的 IL2CPP 元数据及 v2 签名核验通过。实际包大小 165,610,361 字节。
构建期间其他任务修改源码时应重新构建或明确版本边界，不能把旧产物宣称为
新修改的验证；本次发现并行手部代码变化后重新构建，最终输入一致。

## 2026-10-27：扫描网格放置容差与不依赖床头的躺卧

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameSession.cs`
是本轮放置判定唯一所有者。放宽条件为不可移动物品且碰撞体带有
`RoomSurfaceCollider`；不要通过物品名称或任意静态碰撞体启用放宽。
`HasRequiredSurfaceCoverage` 保留物品的 MinimumSurfaceSupportRatio，
在中心已命中的前提下采样四个角与四个边中点，允许两个漏扫点；高度差上限
为 0.08m，局部法线 y 下限为 0.5。不同 RoomSurfaceCollider 可共同提供支撑。
普通表面的四角/八点全通过及 0.035m 高差规则继续生效。

`IsPlacementFree` 对扫描网格再查询物品底部 0.08m 以上的完整占位盒：
仅出现在底部容差带中的网格不阻塞，高于容差带的网格仍阻塞，
包括同一网格分块内的墙体。其他实体使用原完整占位盒。
固定水平物品恢复调用同一判定，并传回实际阻塞碰撞体以保持原库存单元隔离规则。
此逻辑不改变公共 PhysicsBody 的抓取、投掷和连续碰撞检测。

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/BedGeometry.cs`
新增 BedHeadCue（Default / Pillow / Structure）和 HeadEdge。
枕头提示来自 Bed / Unknown 三角形：位于床面端部，最高不超过床面 0.20m，
顶面中心高于床面 0.025m，连通顶面面积 0.04–0.6m²，
横向宽度下限 0.18m、上限 max(0.85m, 床宽×0.9)，纵向长度 0.12–0.60m；
一端有效面积超过另一端的 1.5 倍才认定方向。它是几何启发式，
不是新增相机视觉模型或可靠的物体语义识别。
只有床头结构同样明确时才使用结构方向，否则以稳定的床长轴方向继续。
人体净空检查允许该头端区域内的低矮枕头几何，不忽略整块 Unknown 网格。

床边横轴统一通过 Cross(up, Forward) 计算，边缘距离与左右上床点使用同一方向。
`RealBedScanner` 在新的候选无明确头向时对齐已有方向，同时交换头尾/左右边缘，
避免方向翻转破坏稳定采样和动作。近似方形床不再要求长宽至少相差 0.12m；
长短轴因扫描误差互换时，可以使用与已有床向更接近的轴，同时换算尺寸和四边距离。
床面识别失败、空间被挡或路径不可达仍报告原因；
方向无法区分自身不会阻止发布 lie 能力。共享动作所有权和退出清理保持原路径。

本轮修改还包括：
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/AvatarMotor.cs`、
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RealBedScanner.cs`、
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/LieOnBedAction.cs`、
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs`。
运行时和编辑器静态编译已通过；最终专项 Unity 回归 19 项通过，0 失败/跳过。
测试进程退出码 0，耗时 100.077 秒，136 个受跟踪输入前后一致。
覆盖跨分块/6cm起伏、两漏点允许/三漏点及15cm台阶拒绝、真实障碍、
保存原实例恢复、枕头两端与双枕、无头向及近方床方向稳定、三种完整躺下/
取消起身场景。六个本轮生产源码打包前与验收哈希一致，临时测试入口已移除。
通过结论限定上述专项；较早扩大回归的两项未复验断言已在专项报告记录。

`AvatarMotor.BeginLyingContact` 的躺卧胶囊半径仍为 0.14m，高度改为 1.4m、
中心 (0, 0.725, 0.145)。相比原高度 1.5m、中心 y=0.775m，脚端不移动，
头端减少 0.1m，躯干代理不再覆盖靠枕头的颈部区域。它仍用于检查完整上床/
离床转移路线，未禁用碰撞或忽略整个枕头网格；退出仍恢复站立胶囊。
验收回放使用 0.02s 捕获步进，避免无渲染批处理的渲染帧数与物理步数不一致。
报告及数值：`MateSoul_放置容差与躺床方向_20261027.md`、
`validation/placement_bed_20261027.json`。
本轮保存主场景 Release 构建因 ShaderCompiler IPC 初始化失败退出 -6；
工具同时报告 Unity 配置/metrics 和 Android SDK 缓存写入被沙盒拦截。
未产出 `MateSoul_PlacementBed_20261027.apk`。构建进程与锁已释放，
六个生产源码失败后仍匹配验收；不能用旧 APK 宣称本次修改已打包验证。
恢复构建使用报告中的脚本或保存场景菜单，继续保护主角色与场景资产。

## 2026-10-27：开放锅腔复合碰撞与近距握把准入

`FryingPanColliderBuilder.Configure` 是两个生产锅体碰撞的编辑器生成入口；
`GameSceneBuilder.ConfigureFryingPanColliders` 直接转调。圆锅底半径.112m、厚.005m，
48边凸网格；圆周24段，各在y=.012m处分成上下凸壁，保留内壁折角。
原模型外壁剖面为(.112,0)、(.126,.004)、(.137,.025)、(.14,.052)、
(.14,.056)、(.136,.054)；内壁由(.112,.005)经(.126,.012)到(.136,.054)。
壁段两端各延伸.05°，避免接缝浮点漏射线。禁止用整锅凸包封住锅腔。

52个凸MeshCollider包含1个锅底、48个壁段和3个原模型手柄/握把/连接环凸网格。
碰撞节点独立于原模型实例，手柄按原节点世界位姿和缩放匹配。
`Content/Props/FryingPanCollision.asset` 保存3个共享生成网格；重建通过
CopySerialized更新已存网格，保留GUID和子资源标识。加热绑定与组合库存保持。

`ItemGripAnchor.acquisitionAllowance` 从.12改为.025m，距离从已有握持盒边界算起。
`PicoDirectHandInteractor.FindCandidate` 在普通重叠候选和已选中工具注册表路径
都用实际碰撞面最近距离排序；把手不再减.025，原候选从减.012改为减.001。
这是1mm平局偏好，不能宣称任意微小距离差都强制切换。
XRI管理选择，PhysicsBody管理运动和最终脱手；原跟随、释放与交接生命周期不增加所有者。
直接选择和PhysicsBody占位缓冲扩到128，HeatingBaseController/CookwareHeatReceiver
接触缓冲扩到128，保留原查询饱和边界。

最终29项专项/相关回归通过，两个锅体各4320条射线最大误差小于1.5mm；
真实锅铲六方向及锅底阻挡、牛排空腔、四类生产握把、邻近物品优先、密集重叠、
已持目标注册表、双手交接及控制器/烹饪/组合库存通过。临时测试入口已移除。
Mac无渲染单次.30m/45°跟随P95：锅体.6983/.7514ms，锅铲.0559/.0566ms
（空场景/邻近桌面）。每组预热20次、采样120次，仅计SetFollowTarget；
不含动态物理求解总成本，不是PICO帧率结论。
保存主场景Release快照已交付为 `Builds/MateSoul_PanGrip_20261027.apk`；
658项构建输入前后一致，23项修复验收输入匹配，进程退出0。
Gradle缓存APK没有新时间戳；通过1541个Unity staging文件逐项相等、Gradle APK/
原生release库相等及最终副本CRC、17个ARM64 ELF、v2签名、4KB对齐确认身份。
包SHA-256为 `9b64904c6a2c1515c05574af350bbf1e1fbcaccba53cdcd65a62697baa883945`。
构建范围为2026-10-28 01:02:44–01:03:53（北京时间）；之后的7个并行源码变化
及后续Unity构建不能使用本轮29项结果宣称通过。共享staging被替换后，
从同哈希保留副本完成独立交付，不修改其他任务源码或终止其Unity实例。
本轮没有安装头显，真人验收未执行。
详见 `MateSoul_锅体碰撞与抓取吸附修复_20261027.md` 与
`validation/pan_grip_20261027.json`。

## 2026-10-28：保存场景 Release 打包与覆盖安装

本轮复用 `GameSceneBuilder.BuildCurrentSceneAndroid`，通过系统临时目录中的
Editor 入口传入 `BuildOptions.CleanBuildCache`；不调用内容或场景重建器。
首次增量 BuildPipeline 成功，Gradle 却保留旧输出和时间戳；需区分 Unity
构建结果、实际 APK 文件状态和工具层沙盒提示，不能仅根据其中一项断言交付完成。

安装目标为实体 PICO B3110，包名 `com.matesoul.game`。覆盖安装前停止应用，
比较外部持久目录 `matesoul-v1.json` 的安装前后 SHA-256；
设备 `pm path` 指向的 base.apk 哈希必须与本地通过签名、CRC、ARM64 和对齐检查
的 APK 相同。安装后解析实际 Activity、唤醒并启动，分别记录进程存活与
`MATESOUL_READY`，业务就绪和真人交互不能用 Activity 启动耗时代替。

完整本地代码编译结束后，20 分钟总时限在 Gradle 阶段触发；自有进程已退出，
确认无 Unity/lsof 锁持有者后移除了陈旧锁及本轮临时 Editor 入口。
继续构建使用项目现有 `scripts/run-headset.py`，复用编译缓存；使用其既定
40 分钟总时限和 10 分钟无进展限额。最终安装结果待完成后补记。
报告：`MateSoul_放置与躺床打包安装_20261028.md`；
数值：`validation/placement_bed_install_20261028.json`。

## 2026-10-28：Release 构建输入差异与安装回执闭环

本轮保存场景 Release 构建已成功，BuildPipeline 耗时 87.1628243 秒，
真实 APK 为 165,796,681 字节。`report.summary.totalSize` 不能代替 APK
文件大小；交付记录采用实际文件 stat 和 SHA-256。
交付包 `/Users/bytedance/MateSoul/Builds/MateSoul_PlacementBed_20261028.apk`
的 SHA-256 为 `9b64904c6a2c1515c05574af350bbf1e1fbcaccba53cdcd65a62697baa883945`。

`scripts/run-headset.py` 的严格输入检查捕获到 408 个输入中唯一的
`ProjectSettings/ProjectSettings.asset` 差异，在安装前主动停止。
构建前预加载列表包含 `ce2724fcd8e494aa9b0dc418bf84b6c0` 和
`5833520fd0bfe4424b19feec78b2a744` 两个引用，构建后为空列表。
按该差异重建完整文件后，两次 SHA-256 分别匹配原快照
`b3891e01a5bfe389715410d3a04bfe4ee4976b47cc4bc606193095c752505b4a` 与
`fe9ef46054f0de009f58446f83374180848e0a609a3497ebd2b7833fa3bf9177`。
本轮通过独立核验完成安装，没有放宽脚本检查或手动重写项目设置。
其他 407 个受跟踪输入前后一致；六个相关生产源码在安装前仍匹配专项版本。

安装流程先固定已验证 APK 副本，再检查 CRC、v2 签名、4 字节对齐、
仅 ARM64 和 IL2CPP 库；使用 Android Studio JBR 运行 apksigner。
本包未开启 Android debuggable，签名证书仍为现有 Android Debug 证书。
01:05 覆盖安装返回 Success，设备 base.apk SHA-256 等于本地产物。
外部存档 `matesoul-v1.json` 安装前后均为
`02f988f4be743ab00371ed1acb63047b6bf4d054a9fe00ac075e72e5dd163164`。

`UnityPlayerActivity` 冷启动 Status ok、Android TotalTime 147ms，PID 28041
存活，focused Activity 属于 MateSoul。日志含 `MATESOUL_OCCLUSION_PASS`、
`MATESOUL_INPUT`、`MATESOUL_UI` 及 PICO XR/左右手初始化；最近 2500 条
缓冲日志按当前 PID 取样，未发现致命异常。设备随后为 Asleep，未见业务
`MATESOUL_READY`，不能据此宣称房间就绪或真人交互通过。
另一窗口的构建进程及其资源没有被停止或修改。

成功构建日志、源码快照、`recovery-result.json`、`startup-result.json`
和启动文本证据在系统临时目录
`/var/folders/mp/bbv_ln_177q9ss7dlmf6trmh0000gn/T/matesoul-headset-20261028-004342-y85aesoz/`。
最终报告与数值记录沿用上一章节路径；原 19 项专项的两项未复验断言继续保留。

## 2026-10-28：High 拾取内容与四档高度混合

`PickupAnimationBuilder.ImportHigh` 只生成/更新 High 片段与标定，然后按抓握点
高度排序写入现有 PickupAnimations.asset，更新现有 PickupHeightBlend。
角色对该数据资产的引用保留，因此 UI 和业务动作继续调用既有
PickupAnimationSet.Select，不新增 Animator 或 Transform 执行路径。

原上传 High 为 30FPS、2–61 帧、1.966667s，稳定抓取段约 1.0s；重采样对齐到
归一化 0.5 后为 0.983333s。生产抓握点为 (0.049273,1.229810,0.242873)m，
Clip GUID 为 a62c780d6e8d84172a21c234de5a46da。完整内容重建入口包含四段，
避免后续重建时丢失 High；超高错误提示不再限定前三种放置高度。

Table/High 混合区间最大原始接触差约 6.34cm，沿用上限 15cm 的有限 IK 修正，
最终接触门槛仍为 2.5cm。高度准入上限约 1.38m，仍受真实站位和接触可达性约束。
旧三段片段及 meta、生产角色 Prefab、主场景保持逐字节一致。

48 项专项和 3 项 Unity PlayMode 实际动作回归通过，包含四种支撑高度、High
参数实际使用、完整回收/持物、暂停/放下和边界拒绝。临时测试入口已移除；
数字证据和构建信息见 High 接入报告。本地回放不等于真机真人验收。

## 2026-10-28：手部追踪断流的选择与物理所有权

生产修改位于Unity项目 `Assets/MateSoul/Runtime/` 下的 `PicoTrackedHand`、
`PicoDirectHandInteractor`、`TrackedHandPhysics`、`PhysicsBody`、
`PhysicsGrabInteractable` 和 `DeviceStartupLog`；同时更新
`Assets/Resources/PXR_ProjectSetting.asset` 及 `GameSceneBuilder` 的两个配置入口。
不引入新的运动所有者或角色动作路径。

1. SDK关节缓存维护位置有效与完整位姿有效两种掩码。位置必须具有
   `PositionValid|PositionTracked` 且有限；旋转另需 `OrientationValid` 和有效
   四元数。`TryGetJointPosition` 服务弯曲/接触/抓握中心，`TryGetJoint` 保留完整
   位姿合同，手掌追踪不会因位置有效而放宽。
2. 已选PhysicsGrab物品遇到无追踪、无握持信号或位姿异常时，选择最长保留150ms。
   `IsTrackingSuspended` 阻止附着点更新，物理交互向 `HoldFollowPose` 提交当前
   物体位姿与零速度；动态物品保持动态刚体，固定步跟随继续抵消重力。
3. 恢复需 `MotionValid`，即原始采样恢复后的连续运动证据；第一恢复样本继续
   暂停。相对最后抓握位姿的12cm/45°上限和最初失效时间保持不变，异常采样不能
   延长宽限。原始 `Tracked`、UI指尖和角色牵手不会伪装成仍有追踪。
4. 超时或跳变触发原XRI退出，`PhysicsBody.EndFollow(true)` 清除旧抛掷速度并
   恢复原物理状态。`requireOpenAfterTrackingLoss` 防止握把辅助立即重抓；
   张手才重新准入。可靠的当前松手信号优先于恢复等待；只有当前运动有效的正常
   松手才保持抛掷动量。双手交接继续使用现有驱动者转移，不重启物理跟随。
5. 未选中时，仅对失效前正在握/捏且已有候选的尝试保留350ms。恢复必须同目标、
   当前接触和有效运动信息；张手/换目标/超时清除。该窗口不能恢复已超时掉落的物品。
6. `TrackedHandPhysics.FixedUpdate` 在原始失效或直接交互暂停时关闭代理和旧
   接触，避免恢复首帧的旧手部数据推动物体。日志沿用已有128KiB诊断文件上限，
   新增追踪原因、持续时间、头部俯仰和抓取恢复阶段。

本地PICO SDK的 `PXR_ManagerEditor` 明确将高频开关标注为60Hz，并提示功耗增加；
`PXR_BuildProcessor` 将其写为Android manifest的
`Hand_Tracking_HighFrequency=1`。配置开启不等于实际采样率已实测，更不能补齐
低头时因遮挡或视场产生的原始数据缺失。

最终48项Unity/XRI/PhysX回归通过，包括32项专项及16项既有控制器、物理和牵手
回归。完整进程耗时179.33秒，Unity退出0，相关8个输入前后一致，临时测试入口
及meta已移除。测试需使用PlayMode时钟并执行PhysicsBody固定步后再手动
PhysX模拟；同步注入多个样本时需刷新测试夹具的帧缓存，否则会误测捏取模式。
用户级Unity metrics写入被工具沙盒拒绝，但测试XML、退出码及源码哈希通过。
保存场景Release与覆盖安装已完成，流程275.14秒，1815个受跟踪输入一致，相关
8个输入仍匹配通过回归的版本。交付APK为
`/Users/bytedance/MateSoul/Builds/MateSoul_HandTrackingRecovery_20261028.apk`，
165,769,469字节，SHA-256
`4718e2f8854787c980ad7693719d83ac9c58e25f3446242934aa93787f7158c3`。
CRC、v2签名、zipalign、ARM64和IL2CPP验证通过，包内manifest高频开关为1；
生成C++包含位置分离、保持跟随和超时后重新张手准入，设备base.apk哈希一致。
安装前后存档均为
`403dc8bba63db134df73fc4bd0b787157465932d6c87679086c17512af4844ea`。
启动Status ok，唤醒后前置既有顶部实例，PID29985存活；当前PID日志取样含
UI/输入/遮挡初始化，未发现所列致命或运行时异常。头显随后再次休眠，未出现
本轮手部追踪转移或业务READY，不能将进程存活视为真人低头交互通过。
构建工具层仍有统计和SDK缓存写入限制；以成功构建回执、独立APK内容/签名、
设备包和存档哈希及启动证据分别闭环，不重复打包。数字记录及真人限制见专项报告。
保存主场景 Release 包为 `Builds/MateSoul_PickupHigh_20261028.apk`；
274 项受跟踪输入一致，实际包 165,797,457 字节，CRC、ARM64 库、IL2CPP
拾取元数据、场景中的 Pickup_High 和 v2 签名通过。APK 与 Gradle 输出哈希一致。
Unity 增量缓存可保留 APK 旧时间戳，不能只按 mtime 判断是否打包了新资源；
应结合构建成功、输入哈希、Gradle 输出身份和包内场景数据核验。本轮未安装头显。

## 2026-10-28：最新整合包安装与增量产物身份核验

最新交付`Builds/MateSoul_AllLatest_20261028-011707.apk`为保存主场景Release，
165,769,469字节，SHA256
`4718e2f8854787c980ad7693719d83ac9c58e25f3446242934aa93787f7158c3`。
25份当前生产源码在Player与ManagedStripped共50项证明通过，1815项输入稳定；
四档Pickup、行走、生气、锅体墙碰撞、高频手manifest和透明手Alpha0.5均进入包。
角色五网格与上一整合包一致，面部61形态保持。

`scripts/run-headset.py`快照扩展到生产数据asset、mat、prefab、meta及PICO设置。
严格输入比较保留；成功构建后比对Unity APK与Gradle Release输出哈希，
检查APK中的libil2cpp与当前Release stripped_native_libs一致，避免将Gradle
缓存保留的mtime误判成旧资源。签名、对齐前置安装，再核对设备base.apk和存档。

同一主工程其他窗口已启动等价构建时，确认入口、cwd、构建前输入快照以及
专项验收哈希后复用实际产物；不终止其他窗口或开启冲突Unity实例。
此次构建包含手部恢复48项通过的实现及High/放置/锅体/持刀最新内容。

安装后PID29985存活，设备/UI/左右手输入初始化，60秒取样未见致命异常；
头显自动休眠，未取得房间READY，仍需佩戴验证实际交互。
原存档哈希安装前后为
`403dc8bba63db134df73fc4bd0b787157465932d6c87679086c17512af4844ea`。
完整证据见`MateSoul_最新整合包安装_20261028.md`和
`validation/all_latest_20261028.json`。

## 2026-10-28：拾取兜底与接近站位容差

`ItemTransferStep.CanSnapPickup` 现在以主、副手误差最大值不超过0.65m为边界，
作者四档拾取和双手搬运都使用同一检查。原先作者四档在Reach绕过兜底、
只接受0.025m残差，导致扩大兜底仍不生效。常规范围标记0.25m保持。
`PickupAnimationSet.HeightTolerance=0.35m` 用于高度准入，
`MaximumCorrection=0.15m` 继续限制作者动画的手部修正；不能用高度余量
扩大手臂的实际几何修正。

接近流程为：

1. 依据动画抓握位置搜索站位，7圈×16方向、圈间距0.075m、最大外移0.45m；
   导航采样和采样误差最多0.18m，完整导航路线为必要条件。
2. 对该候选调用 `PhysicsBody.CanOccupy(pose, 0.04f)` 接受浅重叠；
   不改全局碰撞体与PhysX响应。记录物品当前原点，物品实际移动超过0.15m
   才重新搜索；不能再用目标离扩大站位的距离作为移动判定。
3. 仅物品动作给 `MoveAction` 传 `arrivalDistance=0.18m`。
   进入该范围后先Stop再成功，避免已经接近时被阻塞窗口拒绝；
   默认参数0保留普通移动的motor完成条件，Check及重规划仍要求完整路线。
4. 物品源与目标Dock传0.15m位置容差，默认Dock仍为0.015m；
   接受位置后目标速度必须清零，朝向要求继续为小于1°。
5. `AvatarMotor.dockFacing` 只表示当前Dock的朝向收尾，Stop清除。
   小于普通转身进入阈值且没有正在播放的转身步时，按现有28°/s收尾速度
   纠正残余yaw；大角度及正在播放的转身步继续沿原流程完成。
6. 作者动画到接触后允许物品在0.65m边界内贴合当前握点，再沿原持物/放置/
   取消流程处理；没有第二套动作执行器或物理所有者。

Unity高架复现的最终站位误差0.1198183m、朝向误差0°，在最初0.10m对齐
试验参数下持续停在Dock。以实际误差确定0.15m最终容差后，四档均成功。
最终6项回归通过，核心6项源/场景/Prefab输入哈希一致。测试覆盖残差
0.55m接受与0.651m拒绝、主副手边界、0.03m浅重叠接受与0.06m拒绝、
0.12m站位误差接受后清零速度、5°朝向收尾、普通移动与不完整路线拒绝、
扩大搜索不重复重启、四档实际动画层及暂停/取消所有权恢复。

`EnterPlayMode` 的域重载可能丢失协程捕获局部变量；跨该边界的测试状态使用
实例字段，含捕获局部变量的断言放在普通辅助方法。一次编辑器Burst缓存编译
线程原生崩溃后，验证启动使用 `--burst-disable-compilation`；发布构建不使用
该验证参数。必须移除旧测试XML并要求本轮完成回执，不能将LaunchServices外层
退出0与旧XML组合成新测试证据。临时Engine入口及meta退出Unity后移除。
历史NearHandPickup测试中的46cm副手拒绝断言属于旧45cm合同，本轮未修改该测试；
新主副手边界以专项65cm/65.1cm断言为准，不将选择的6项回归宣称为全库通过。

代码为 `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/` 下
`ItemTransferStep.cs`、`CharacterActions.cs`、`AvatarMotor.cs`、
`PickupAnimationSet.cs`。保存主场景Release目标：
`/Users/bytedance/MateSoul/Builds/MateSoul_PickupTolerance_20261028.apk`。
最终包体与编译参数核验见 `MateSoul_拾取吸附与接近容差_20261028.md` 及
`validation/pickup_tolerance_20261028.json`。

本轮保存主场景Release最终流程149.8664秒、1815项输入稳定，包165,770,689字节，
SHA-256 `a13745965d3c49e04dd6c0dcd154e78b960490a173cb2ce03965ad933aa6acc0`。
CRC、APK v2签名、zipalign、18项ARM64库、四档动画、拾取元数据及当前
Gradle/stripped原生库身份通过；生成C++中8个函数证明当前距离参数、近点Stop、
Dock清零速度与小角度收尾进入发布运行时。
首轮虽构建成功，但并行床面源码变化使输入一致性校验拒绝交付；最终在最新
`BedGeometry.cs`/`RealBedScanner.cs` 上重建，拾取核心6项验证输入仍一致。
床面新功能的专项验收由对应并行工作记录，本轮未安装头显。

## 2026-10-28：床面细节容差与接触高度（验证中）

`BedGeometry` 初步调整局部法线阈值为0.75、拟合高度 RMS 阈值为0.06m、
覆盖采样为75%。`BedTarget.ContactLift` 是相对既有接触高度的额外抬升；
在中央躯干范围搜索10cm内的 Bed/Unknown 细节最高点，超过床面3cm的部分
用于抬升，使原本离床面4cm的胶囊底部保持高于细节约1cm。
已确认的枕头提示继续使用原颈部避让规则，不用枕头顶面整体抬高躯干。

`HasClearance` 对低矮扫描细节放宽，不再按整个床 SourceId 无条件排除高障碍；
普通已知其他语义物体、较高杂物、床边路径和实际 PhysX 转移检查保留。
`RealBedScanner.Observe` 复制 ContactLift；SamePose 将其3.5cm以上的变化
视为接触姿态变化。没有修改通用 PhysicsBody 或 AvatarMotor 的碰撞规则。
原来的床头兜底与实时 Bed 语义入口保持。

新增行为尚待专项验证，尤其需要真实 MeshCollider 下的起伏/杂物完整躺下起身、
较高障碍及同源扫描分块障碍拒绝、漏扫边界和原枕头方向回归。
临时源码副本位于
`/var/folders/mp/bbv_ln_177q9ss7dlmf6trmh0000gn/T/matesoul-bed-surface-20261028-gw370vzv`。

## 2026-10-28：床面容差最终合同与真机交付

本章为前述“验证中”章节的最终回执。`BedGeometry`使用面积加权高度RMS≤0.06m、
局部法线`abs(y)>0.75`、7×9中央采样至少48点覆盖；整体尺寸、面积、实时Bed语义、
稳定观察与床边路线合同不变。RMS是整体误差，不是每点最高6cm。

`BedTarget.ContactLift`与原`.035m`接触偏移相加。中央躯干范围内最高的允许
Bed/Unknown细节超过床面`.03m`的部分作为额外抬升，躺卧碰撞胶囊底面在细节
上方保留约1cm。已确认枕头沿颈部避让处理，不用枕头顶面抬高整个躯干。
Scanner复制抬升值，SamePose增加抬升差≤`.035m`；高于容差的同床SourceId
障碍也逐三角形检查，不能重新恢复整块SourceId无条件净空豁免。通用PhysicsBody/
AvatarMotor的CanOccupy及上床/起床转移路径没有改动。

本轮22项验证（14项专项+8项既有回归）通过，真实PhysX三轮覆盖8cm连续起伏与
6cm/9cm低矮杂物，均完成接触、取消或目标失效退出、站立碰撞恢复和控制权释放。
8cm起伏RMS`.02675m`，抬升`.00759m`；6cm/9cm杂物抬升`.03m`/`.06m`。
大缺口、明显变形、较高障碍、同源高障碍、已知其他语义和高悬空物仍拒绝。
此回执不是全库测试通过，旧测试范围不作为新增行为的证据。

验收使用临时Engine链接及`--burst-disable-compilation`，生产Release不使用
该测试参数。退出Unity后清理本轮链接及meta；macOS清理比较须将链接目标和
临时目录同时resolve，避免`/var`与`/private/var`导致自有链接漏清理。

保存主场景Release入口完成1815项输入前后哈希一致，两个床源码与验收输入相同；
APK、Gradle Release及stripped-native IL2CPP哈希合同、CRC、签名、对齐全部核验。
已`install -r`至PICO `PB3A10MGL9220170B`，设备base.apk与交付一致，存档安装前后
哈希相同。路径：
`/Users/bytedance/MateSoul/Builds/MateSoul_AllLatest_20261028-020752.apk`。

初次Activity回执在头显Asleep时为UNKNOWN且持久化startup日志仍旧，不能代表新包
READY。唤醒恢复已有Activity后，新启动UTC为`2026-10-27T18:09:40.3415350Z`，
`18:10:32.6173680Z`出现`MATESOUL_READY source=PICO Spatial Mesh items=0 itemState=ready navMs=3`。
Scene Capture超时后既有实时网格路径完成初始化；真实床铺效果仍需佩戴体验，
不能把该无人佩戴的等待用作性能结论。

沙盒外层failed可能来自Unity metrics/config与SDK缓存写入，但当轮实际测试退出0、
XML、构建PASS、签名APK、安装后hash、存档hash及新启动日志成功时，以这些具体
证据分别报告结果，不凭外层failed重跑已经成功的流程。

完整报告和数值：`MateSoul_床面起伏与杂物容差_20261028.md`、
`validation/bed_surface_tolerance_20261028.json`。本轮未提交或推送，保留所有其他窗口
既有改动。临时数值/日志保留在前述工作目录，无新增验证截图或视频。

## 2026-10-28：三层吸附的正确执行合同

用户要求65cm仅用于第三层兜底，不能替换前两层。当前
`ItemTransferStep.SelectPickupSnapLayer` 返回 `Contact`、`Primary`、
`Fallback` 或拒绝 `None`，按主副手最大误差依次判断：
精确接触≤0.025m → 常规吸附≤0.25m → 最后兜底≤0.65m。
`PickupAnimationSet.MaximumCorrection=0.15m` 继续限制作者动画的手部IK修正。

Reach先沿原作者动画/IK流程完成并等待接触位姿，随后按上述顺序判断。
Contact/Primary直接贴合；Fallback第一次只设置当前步骤的 `fallbackPending`
并返回，下一次动作更新重新选层。前两层恢复时优先执行，只有仍落在Fallback
范围内才启用第三层。`CanSnapPickup` 仅保留为距离资格查询，不能替代执行时
的分层选择。物品贴合、附着、预约与取消仍由原所有者处理，没有第二套吸附器。

12项专项已通过，覆盖前两层边界保持、前两层完成不增加兜底等待、兜底在首次
失败后暂不附着、复查后55cm兜底、复查中2cm精确接触恢复优先、65.1cm拒绝、
物理恢复与控制权释放。测试39.0076秒，除并行变更的三个HUD/手部UI源码外，
1812项输入一致；该夹具不执行这三个UI组件，完整变化清单保存在数值JSON中。
此结果不是全库或HUD验收。本次源码修正未重新打包或安装，不能复用首轮APK
作为新分层合同的发布证明。临时Engine链接及meta已清理。

源码：`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemTransferStep.cs`；
说明见 `MateSoul_拾取吸附与接近容差_20261028.md` 末尾，
数值见 `validation/pickup_layer_priority_20261028.json`。此前“统一65cm门槛”
的章节为历史实现，本章优先。

## 2026-10-28：第二轮床面与固定物品宽松判定

当前床合同升级为面积加权高度RMS≤0.10m、局部法线abs(y)>0.6、中央35/63点
覆盖（55%）、面积/包围投影≥45%。`Extract`按所有Bed三角形的连通性合并
扫描分块，不再各SourceId独立拟合；SamePose以实际姿态比较，中心变化≤6cm，
角度≤6°、宽度≤10cm、长度和边界≤12cm，ContactLift变化仍≤3.5cm。
Scanner两次稳定观察发布能力。低矮Bed/Unknown扫描细节上限20cm，躯干
接触高度继续保持高于最高细节约1cm，枕头识别和实际上床/起床路径保持。

固定物品规则统一放在`GameSession.HasRequiredSurfaceCoverage`等既有入口。
按ImmovableAfterPlacement将采样宽长各限制为min(配置比例,0.65)，中心已命中
后至少3/8周边点。扫描支撑高度差20cm、采样射线半长24cm、法线y≥0.35；
非扫描固定支撑高度差8cm、半长12cm、法线y≥0.5。精确支撑探测从固定物品
位置上方28cm向下56cm，支持扫描面高度变化。可移动物品仍用原配置与全覆盖。

`IsPlacementFree`对固定物品统一5mm外扩，只对现实扫描网格豁免底部20cm，
上部扫描占位和普通实体仍拒绝。物品高度小于豁免带时直接跳过该网格的上部盒，
避免负尺寸。固定壁挂法线abs(y)≤0.5、朝向点积≥0.7、支撑探测前后余量16cm。
所有判断基于内容标记，未增加具体物品名称分支或独立配置中心。

恢复固定物品时共用宽松水平支撑判定；支撑为虚拟PhysicsBody时必须已在
accepted集合，否则继续原支撑图检查。静态房间支撑可作为链根，避免把未受
静态房间支撑的虚拟物品循环互认。18cm扫描面变化后固定电视实例及存档局部
坐标保留、逆序固定餐桌/电视堆叠恢复均已实测通过。

本轮31项全部通过（20项专项+11项既有回归），进程92.34秒、输入哈希一致。
包括4种水平固定目录物品18cm台阶、小虚拟台、三点支撑、倾斜扫描墙面时钟，
约41%床漏扫、20/24cm起伏与18cm杂物、真实PhysX接触与退出、较大障碍拒绝，
以及无效库存单元隔离。覆盖结论仅限这31项，不代表全库通过。临时目录：
`/private/tmp/matesoul-broad-tolerance-20261028-xDkmX8`；入口及meta已清理。

本轮保存主场景Release构建/覆盖安装执行中，最终交付回执见
`MateSoul_躺床与固定物品大幅放宽_20261028.md`及
`validation/broad_tolerance_20261028.json`。真机床面扫描与视觉贴合仍需佩戴体验。

### 最终Release与输入一致性说明

本轮保存主场景Release已生成：
`/Users/bytedance/MateSoul/Builds/MateSoul_BroadTolerance_20261028-025648.apk`。
流程149.50秒、1754项生产输入前后一致，本轮三个源码与31项验收哈希一致。
CRC、签名、zipalign、Gradle APK及stripped-native IL2CPP一致性全部通过。
APK SHA-256为`ecb71cc1cab7d8fa68d3d015561b5aeec19291858f6be256ab7e83ecb5d7eee3`。

原始全量快照包含Engine临时测试，会因其他窗口新增测试而拒绝交付。该程序集
`includePlatforms=["Editor"]`、TestAssemblies且构建为BuildOptions.None，
不会进入Android Player。最终临时启动包装单独校验asmdef哈希及Player程序集缺席，
生产源码/资源/配置继续要求前后哈希一致。较早包构建后HUD生产源码有变化，
因此实际重新构建当前最新输入，没有放行生产变更或安装旧包。

无持有者的残留UnityLockfile仅在lsof和Unity进程证据均为空后清理。设备后来
断开，最终ADB列表为空，故当前交付只到“代码/专项/Release APK完成”，本轮
未覆盖安装、未改设备存档、未验新包真机READY。重新连接时使用上述最终包
执行install-r，并核对安装前后存档、设备base.apk与交付包哈希。
完整数值：`validation/broad_tolerance_20261028.json`。

## 2026-10-28：PICO 原生键盘与 HUD 输入焦点边界

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs`新增
`IsTextInputFocused`：从EventSystem当前选中对象读取TMP_InputField.isFocused。
该状态直到TMP消费键盘终态文本并结束编辑后才释放；不依赖Application.isFocused，
因此系统键盘短暂夺取应用焦点时不会提前把HUD手势重新放开。API配置三项输入和
对话框沿用同一个状态所有者，没有额外键盘恢复循环或重复ActivateInputField。

`HudEyeHandInteractor.UpdateModel`将编辑状态纳入available，取消已有捏合，
以空射线和select=false让XRI发送无点击的释放；armed只在available且未捏合时
设为true。`PicoHandPoke.UpdateUIModel`编辑时清空接触、捕获控件和armed，
同样输出空射线；编辑结束后先退离触碰区域再重新点击。保持XR模块运行以消费
这些取消事件，不通过禁用整个模块冻结旧指针状态。

GameHud在Android配置XRUIInputModule.enableMouseInput=false、
enableTouchInput=false。设备HUD由已注册的追踪手势输入，原生键盘触摸事件不再
作为Unity的平面指针清空选中控件；编辑器仍可用鼠标切换控件。隐藏菜单检测
在编辑期间清除待触发手势并返回，避免输入时双手姿态隐藏整个HUD。

框架机制已通过实际GUI事件链复现：XRI空命中按下清空选中对象，TMP.OnDeselect
调用DeactivateInputField；原生路径会将m_SoftKeyboard.active设为false并移除引用。
TMP.LateUpdate在!isFocused时停止键盘文本同步，所以不能依靠下一帧自动重新选中
输入框来补救首字符丢失。GameSession.SetPaused继续保留Conversation自身取消生命周期。

本轮29项专项检查与5项既有PlayerAIHudTests全部通过。专项是编辑器实际TMP／XRI
事件测试，不能替代PICO原生IME验收。Release构建1815项输入哈希一致；生成C++
确认三处焦点检查及两种Android输入来源关闭，APKmetadata含get_IsTextInputFocused，
CRC／v2签名／对齐／Gradle与原生库身份通过。

安装包：`/Users/bytedance/MateSoul/Builds/MateSoul_KeyboardFocusFix_20261028.apk`。
报告及数值：`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_APIKey键盘焦点修复_20261028.md`、
`/Users/bytedance/MateSoul/项目沉淀资料/validation/keyboard_focus_20261028.json`。
临时专项入口及meta已清理，本轮设备未连接，未安装或完成原生键盘佩戴验收。

## 2026-10-28：运行时切割的累计刀路与刀刃采样

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RuntimeCuttingTool.cs`
在 `previousStart/previousEnd` 之外增加 `strokeStart/strokeEnd`，分别记录
上一物理步的实际刀刃与当前连续刀路的起点。8 毫米门槛比较刀路起止中点
的净位移；0.08 米/秒门槛仍使用上一物理步的实际速度，避免把静止时长或
所有往复路径的总长度当作有效切割。

流程为读取刀刃、识别换向、计算累计位移和切平面法线、检查运动门槛、
执行候选查询及实际碰撞体扫掠，最后更新历史。反向运动重新起刀；速度
不足、冷却期间或累计行程达到门槛后重置起点。无持有权、暂停和禁用时
清除历史有效位。候选 AABB、采样轨迹和提交切平面都必须采用同一累计
起止刀刃，不能只放宽速度而继续使用前后单帧轨迹。

`SweptBladeTouches` 的采样数改为最长刀刃长度除以 0.004 米、向上取整
后加一，至少两个端点。生产刀刃约 0.16 米，固定五点曾留下约 0.04 米
的空隙；新采样对窄块保留实际 Collider.ClosestPoint 和 Collider.Raycast
检查。仍为有限采样，不表示无限精度的连续曲面求交。只在通过运动门槛
且宽相命中可切割物后执行窄相，沿用去重和失败冷却。

本轮仅改变刀路采集层；网格所有权、切块质量、库存单元、切割历史、
占位验证、保存及失败回滚仍归 `RuntimeCuttable` 和 `GameSession`。
当前目标仍要求未被持有或预约，没有引入对他人持有物的抢占切割。

20 项回放及相关回归通过，源码/场景/角色/控制器验证期间哈希一致。
回放是编辑器受控刀路，PICO 手部追踪、真实挥刀手感需佩戴验收。
临时入口已清理；Release 构建与校验 157.06 秒，1,815 项输入前后一致。
切割源码在测试、构建和交付时的哈希相同；生成 C++ 含 `strokeStart`。
APK 与本次 Gradle 输出、包内原生库与本次编译产物身份一致，
CRC、v2 签名及对齐通过。工具包装层因用户级 Unity metrics/config 写入
被沙盒拒绝而回报失败，须与 Unity 自身退出码及实际交付验证区分。
本轮 Unity 自身成功退出，进程和锁已释放，无需重用旧 APK。
交付 `/Users/bytedance/MateSoul/Builds/MateSoul_CarrotCutFix_20261028.apk`，
设备未连接，未安装。详见
`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_刀切胡萝卜修复_20261028.md`。

## 2026-10-28：现实椅面的跨块提取、靠背法线与拒绝阶段

`SeatGeometry.Extract(triangles, floorY, out SeatScanDiagnostics)` 保留严格 Chair
路径，再用 Chair 与有来源编号的 Unknown 面做几何回退。SourceId 不再作为
候选家具分组依据；水平面用共享的 8 毫米量化顶点跨块连通，靠背以位置关联。
几何回退宽深范围为 0.36–0.90 / 0.34–0.85 米，高度范围 0.32–0.60 米，
椅面面积 0.12–0.90 平方米，高度 RMS ≤0.05 米。

背部要求 abs(normal.y)≤0.40，在椅面上方 0.12–0.75 米、水平距离
0.12–0.65 米。翻转水平法线使其朝椅面外侧，其平面到椅面投影边缘距离
≤0.10 米，横向范围不越出椅面投影超过 0.08 米。面积加权外侧法线给出
背部方向；总面积≥0.035 平方米、方向一致性≥0.65。
不能再用径向向量筛掉靠背偏侧片段，也不能用桌上水平物体代替靠背。
内侧 7×7 采样仍需≥42点，中央3×3全部支持，避免用凸包跨越真实缺口。

`SeatScanDiagnostics` 在实际提取阶段记录水平三角面、连通组及面积、
平面、靠背、尺寸和覆盖拒绝计数。`RealChairScanner.Observe` 根据提取结果
区分拒绝、确认稳定与坐起空间阻挡，Editor/Development 每两秒记录
`MATESOUL_SEAT scan`。Release 使用状态提示；没有新增调试服务。
SourceId 仍用于稳定与接触碰撞排除，不能把所有参与关联的块一起忽略。
`AvatarSeatingRig.Check` 的最高54厘米、动作协调器/导航/预约合同继续生效。

受控原始规则回放4项重现预期拒绝；最终36项验收35通过、1失败。
16项新识别专项、18项既有几何/扫描回归及1项实际接触扫掠通过。
完整坐起回放左脚踝目标误差约5.28毫米，超出原5毫米容差；完整坐姿复查
仍失败，此回放的严格Chair提取路径未改。完整起身/取消/目标丢失后的释放
未完成验收，后续追加运行遇另一Unity实例占用工程。旧XML不能算该次证据。
补齐NavMesh、等待真实物理步及有限等待上限均只发生在外部隔离副本。
新增临时项目入口和meta已清理，原SeatingTests.cs、角色、场景、动作代码未改。

这不是现场根因闭环：ADB为空，原始设备网格缺失，旧20261003记录仅为模拟器。
高LOD不是完整对象识别，肉眼可见不等于SDK已生成可用面。后续优先获取
mesh输入量、floorY、拒绝阶段、稳定次数及实际障碍/导航数据；断裂面和歧义
家具可考虑人工确认椅面/朝向。虚拟Chair.asset只有visit，需独立sit资产绑定。
本轮未打包或安装APK；生产输入哈希与验收记录一致。

源码：
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/SeatGeometry.cs`、
`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RealChairScanner.cs`。
报告：
`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_现实椅子识别诊断与修正_20261028.md`；
数值：
`/Users/bytedance/MateSoul/项目沉淀资料/validation/chair_recognition_20261028.json`。

## 2026-10-28：PICO 全局环境光适配

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RealityAmbientLighting.cs`
由 GameBootstrap 在房间启动和角色绑定之后安装，只在真实头显启用。
控制器持有环境光 Provider、GPU 资源和当前照明参数；不同时安装 SDK 的
PXR_LightEstimationManager，避免两个组件争用采集和场景照明。

处理顺序为等待原生更新、查询环境 Cubemap、GPU 均匀采样 64 个方向、
压缩为 1×1 RGBAFloat、异步回读平均 RGB 与有效覆盖、映射照明目标、
按 unscaledDeltaTime 平滑应用。最多每 0.5 秒处理一次更新；平滑时间常数
1.25 秒，亮度乘数 0.4～1.25，色调权重 0.3。参考亮度 0.18 为线性视觉
参考，不是物理照度标定；真实硬件数值需佩戴后核对。

控制器保存原 AmbientMode、SphericalHarmonicsL2 和主光颜色/强度。
适配时按 RGB 权重缩放已有球谐系数，主光仅改变颜色和强度，保留角度。
因此现有脸、头发、服装 Shader 的 GetMainLight 和 SampleSH 路径直接受益，
其他共享场景照明的虚拟物体也会适配。没有从 Cubemap 推断主光方向。

有效样本覆盖至少 75%，拒绝非有限数据和未初始化透明黑；有有效 alpha 的
真实黑暗仍可进入最小照明。没有新样本 5 秒后向默认照明平滑回退。
暂停/恢复丢弃旧样本，禁用立即恢复原参数。重新启用等待旧采集任务退出，
原生图像在 GPU 回读完成后释放，最终统一销毁材质、贴图和 Provider。
空间数据权限由房间启动取得；照明边界只检查已有权限。

生产配置 `Assets/Resources/PXR_ProjectSetting.asset` 启用光照并指定 128×128；
ConfigureInteractionCapabilities 和 ConfigurePicoBuildSettings 保存同一设置。
SDK 早期 InitializeOnLoad 曾在索引未就绪时创建默认配置并清空能力开关；
最终编辑器配置直接用 AssetDatabase 加载，PXR_Utils 默认宏初始化和
PXR_PortalEditor 首次读取都延后到 EditorApplication.delayCall。
仅直接加载而不延后初始化不足以修复资产导入时序。

本轮 16 项照明专项加 15 项既有遮挡回归全部通过，实际 Metal HDR Shader
及 AsyncGPUReadback 已执行；Unity 退出 0，进程 36.39 秒，输入哈希一致。
测试使用受控 Cubemap，不能代替 PICO Provider 的真实精度、延迟或性能验收。
临时入口和日志在 `/private/tmp/matesoul-ambient-lighting-20261028`，
主工程临时验收脚本及 meta 已移除。保存场景 Release 编译 210.13 秒，
APK 165,648,273 字节，CRC/v2 签名/对齐及当前 Gradle/原生库身份通过。
实际包内已核对照明控制器、HDR Shader 和启用光照的 Manifest 声明。
构建期间变动仅 6 项 Editor-only 验收文件；其 asmdef、生成 C++ 和包内
metadata 共同确认测试未进入播放器，生产输入一致。
交付 `/Users/bytedance/MateSoul/Builds/MateSoul_EnvironmentLighting_20261028.apk`，
当前未安装或完成真机视觉验收。Release 最终回执见
`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_现实环境光适配_20261028.md`。

## Phase 技术交接：收音机旋钮手部捕获与持续拖动（2026-10-28）

本节替代此前“旋钮仅支持近距触碰拖动”的能力描述。两个旋钮已经支持
捏住后转腕；现有鼠标调台绕圈、音量沿标签连线拖动的路径继续保留。

1. `PicoHandPoke` 用 `IDragHandler` 区分持续控件与按钮。
   持续控件沿原表面法线投影当前指尖，5mm 进入、15mm 释放、
   最深 25mm；移出原控件或失去准入时取消。按钮仍以单次接触执行。
2. 捏合使用 `PicoTrackedHand.NativePinching`、拇指/食指中点及稳定掌心坐标系。
   `RadioKnobControl` 通过启用/禁用注册活动控件，选择 4.5cm 内最近的可用旋钮，
   拒绝机壳背面及外部障碍后的捕获。没有物品名称分支或每帧场景全量搜索。
3. `RadioKnobControl` 持有唯一操作手与前一掌心旋转；提取相邻四元数绕
   `transform.forward` 的 twist，忽略侧摆，以 1.5 倍角度增量更新 `0..1`。
   270° 视觉行程、频率指针和 RadioController 音量/台位映射沿用生产资产。
   捏合不依赖 UI 拖动阈值或二维屏幕位移，头部移动不会改变值。
4. `PicoHandPoke.HasControlCapture` 使同手 `PicoDirectHandInteractor` 的候选和
   获取过程让位，并消费已发生的捏合边沿。UI 拖动还锁定 pointerId，
   防止另一手的 pointer-up 释放当前操作。
5. 捕获后 9cm 之外、跟踪丢失、持物、编辑文字或禁用都会释放。
   异常取消必须松开后重新捏合；仍保持的捏合不能退化为另一条旋钮指尖拖动。
   正常松开保持当前值，控件生命周期清理只释放所有权，不重置音量或台位。

调台指尖按在中心时允许开始拖动；在半径 4mm 的角度无定义区域暂停角度采样，
移到外圈后重新建立基准，避免拒绝按下或产生跳值。真实手指拖动取消像素门槛，
普通鼠标保留原阈值。核心修改仅为以上三个 Runtime 文件，不修改 Radio.prefab、
动画、角色资源或 PICO SDK。

19 项集成验收全部通过，Unity 退出 0、耗时 51.24 秒，输入和保护资产哈希一致。
测试包含真实生产旋钮视觉/业务映射、端点反向、四元数跨界、双手独占、
跟踪和禁用取消、实体遮挡、中心起拖、连续多帧触碰及按钮去重。
临时测试文件在 `/private/tmp/matesoul-radio-knobs-20261028`，
主工程测试入口及 meta 已移除。注入手部姿态不代表 PICO 真机追踪精度与主观手感。

保存主场景 Release BuildPipeline 耗时 152.32 秒，构建加核验 194.75 秒。
1,820 项输入全部未变；当前 Gradle APK 与交付 APK 哈希一致，
包内 ARM64 libil2cpp.so 与本次 stripped native library 一致；
生成 C++ 包含 RadioKnobControl.BeginHandTurn/UpdateHandTurn、
PicoHandPoke.UpdateHandTurn/HasControlCapture。APK CRC、v2 签名和对齐检查通过。
交付 `/Users/bytedance/MateSoul/Builds/MateSoul_RadioKnobs_20261028.apk`，
165,650,833 字节，头显未连接、未安装。报告与完整数值记录分别位于
`项目沉淀资料/MateSoul_收音机旋钮交互修复_20261028.md`、
`项目沉淀资料/validation/radio_knobs_20261028.json`。

本机沙盒仍拦截 Unity 用户级 metrics/config 与 SDK 包登记写入，
因此 Shell 后台回执显示权限失败；实际 Unity 验收退出 0，
BuildPipeline、当前 APK 和独立签名/原生库校验均已完成。
判断结果应读取数值报告及新产物身份，不仅依据外层工具状态。

## Phase 技术交接：盐罐倒置出料与软瓶挤酱（2026-10-28）

三件物品的 ID 为 `salt-shaker`、`black-pepper-sauce`、`salad-dressing`，
生产 `.prefab/.asset` 为 Props 目录下的 `SaltShaker`、`BlackPepperSauce`、
`SaladDressing`。全部是可单手抓取的厨具，库存各 1，允许水平面放置，
不使用固定设施的投影。完整参数与路径见
`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_盐罐与挤压酱瓶_20261028.md`。

1. `CondimentBuilder.Build` 配置 FBX 导入、共享材质、基础碰撞体、
   物理设置、握持 Profile、ParticleSystem 与出料绑定。
   `BuildAndRegister` 只打开保存主场景并追加目录引用；
   GameSceneBuilder.BuildCatalog 同时注册三个定义。
2. `SeasoningDispenser` 是唯一出料视觉所有者。盐模式根据 `outlet.up`
   与世界向上的点积，以 160°/150° 迟滞控制；酱模式从抓取所有者读取压力。
   压力同时控制发射率、喷射速度和两层 `Squeeze`，不写瓶子 Transform。
3. `PhysicsGrabInteractable.DrivingHand` 沿用现有换手状态。
   `PicoDirectHandInteractor.TryGetSqueezeStrength` 只接受有效非捏合抓取，
   使用捕获抓取时选定的原手指。每次输入所有者变化或失跟踪重新取基准，
   增量 ≥0.12 开始挤酱、<0.06 停止；手指在仍持握时放松可降低基准。
4. 手柄没有另加 Activate action，因为生产 CreateControllerRays 已把扳机
   绑定到 UI Press。读取 `uiPressInteractionState.value`，必须先松开到 ≤0.1
   才允许挤压。新增交互应优先复用已有有效输入，避免只读未绑定的字段。
5. 粒子世界空间、盐 768/酱 160 硬上限、最多 16 碰撞形状，
   碰撞后消亡，无逐粒 Rigidbody/碰撞回调；3 米外停止和离屏暂停。
   不累计永久盐堆或酱汁残留。软瓶 `updateWhenOffscreen=false`；
   导入的局部包围盒从 sharedMesh.bounds 设置。
6. 玻璃和软瓶使用 `CondimentGlass` 的双目实例化透明 Pass。
   酱汁流使用 `SauceParticle` 的圆形裁切和拉伸粒子，高光来自程序着色，
   无额外纹理或液体网格。内层酱汁降低镜面高光，避免深棕色被瓶壳冲淡。

Blender 重复节点会自动出现 `.001/.002` 后缀，喷口绑定匹配 `Outlet` 及其后缀。
FBX 空节点保留 Blender 轴，因此绑定阶段明确把喷口世界向上轴对齐到瓶身 +Y，
再将粒子局部 +Z 旋转到喷口 +Y；验收实际检查喷口位置和方向。
两种软瓶均有内外薄壁、两层形变，贴图 Mipmap + ASTC 6×6 / 512 / 不可读。
母版已按物品隔离并内嵌贴图，不需要打开 Unity 取回编辑素材。

14/14 专项通过，Unity 退出 0、24.88 秒。EditMode 夹具需要显式执行
XRGrabInteractable.Awake 初始化 TeleportationMonitor，以及禁用的生命周期回调；
遗漏会造成真实 SelectEnter 入口的夹具空引用，不应因此修改生产抓取代码。
既有播放模式回归的帧推进停滞不计入完成验收；临时脚本和日志放在
`/private/tmp/matesoul-condiments/`，主工程验收入口及 meta 已移除。
当前测试使用注入输入，真机追踪精度、挤压舒适度和 GPU 性能仍需佩戴验证。

保存主场景 Release BuildPipeline 通过（222.76 秒），构建与包核验共 256.46 秒，
1,882 项输入保持一致。APK 167,604,969 字节，CRC/v2/zipalign 通过，
生成出料 C++、包内当前 libil2cpp.so、三件定义和两个 Shader 均已检出。
交付 `/Users/bytedance/MateSoul/Builds/MateSoul_Condiments_20261028.apk`，
SHA-256 `26b3afec437e45cb59394fb3d991464de34d5d452b441e22e8a6e4e3106ad3b2`；
详见 `项目沉淀资料/validation/condiments_20261028.json`，当前未安装。
Unity 用户级统计及 SDK 登记仍有沙盒写入拒绝，外层回执不是实际构建结果；
本轮 Unity 退出 0、当前包身份和签名已独立验证。

## Phase 技术交接：作者持物动画与自主白名单（2026-10-28）

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemDefinition.cs`
提供 `UseAnimation` / `AllowAutonomousUse` / `ConfigureUse`。
默认不允许自主使用。`AutonomousBehaviour` 从会话注入的物品谓词过滤候选，
`GameSession.CreateVisitAction` 同时检查自主请求，防止直接调用协调器绕过白名单。
物品注册表与其他 action 的预约入口不加白名单过滤。

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/UseHeldItemAction.cs`
沿用 `ItemActionRun` 的 RunId、预约与生命周期，依次拿杯、运行
`HeldItemUseStep`、从 Held 状态放回。`AvatarItemRig.UseMotion` 驱动独立
`ItemUse` 层，持物和指形仍由原 Rig 更新；结束清掉该层，不引入第二个跟随器。
取消、暂停、抢占与目标失效由顶层 Dispose 统一释放状态和物理。

`ItemTransferStep` 的 `finishAfterPickup` 允许把持物状态交给后续步骤，
`fromHeld` 避免重复拾取。返回时按原位重新搜索站位，忽略当前已持物品；
抓握目标低于 45cm 使用作者拾取蹲身段，正常手臂范围复用原有放置/躯干伸手。
低处接触允许 4cm 完成误差，随后通过原释放对齐流程到达精确位置；
这是放回接触容差，三层拾取吸附仍为 2.5cm / 25cm / 最后 65cm，
拾取 IK 修正上限仍为 15cm。

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/ItemUseAnimationBuilder.cs`
使用 `PickupAnimationBuilder.RetargetAuthoredItem` 的世界 rest delta 适配，
新动画按源时间采样，不套用拾取的接触重定时。喝水右臂使用生产 Face 的
`Fcl_MTH_U` 区域标定嘴部，并按杯体 Renderer 中心计算实际开口。
`WaterReceiver.Awake` 会重新居中开口，不能直接把包含把手偏移的序列化
openingCenter 当最终坐标，否则约有 13mm 接触误差。
接触调整烘焙进新 Clip，不修改源 FBX 或生产角色网格。

内容目录为 `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/ItemActions/Use/`。
`Drink.asset` 绑定 `Drink_01.anim`，`Props/Mug.asset` 绑定该内容并允许自主使用。
未来单手物品先创建使用动画资产，再调用 `BindController` 注册
`ItemUse.<clip.name>`、启用 `allowAutonomousUse`。固定设施/双手行为应先
接入对应动作能力；不要在中央协调器添加物品名分支。
`ItemActionContentBuilder` 重建时保留使用动画层绑定。

最终 11/11 通过，Unity 退出 0、89.91 秒；包含实际 Animator、导航、物理、
预约和 XRI 路径。主体动作已做 Metal 预览，最终回归因 ShaderCompiler IPC
启动问题采用无图形模式。标定点误差约 0.023mm，根节点采样漂移 0；
这些指标不代表设备跟踪精度。本轮未生成 APK 或执行真机验收。
临时入口/meta 和预览媒体已清理，数值证据位于
`/Users/bytedance/MateSoul/项目沉淀资料/validation/drink_use_20261028.json`。

## 低头偏头裁剪与深度有效性门控（2026-10-28）

环境深度入口仍由
`/Users/bytedance/MateSoul/XR/Runtime/Scripts/SensePack/PXR_EnvironmentDepthManager.cs`
单独拥有。`LastUpdatedFrame` 表示成功获取及预处理的 Unity 帧，
`IsDepthFrameCurrent` 同时要求组件启用、可用状态和当前帧号。
原生返回值、有限位置、近单位旋转、合理 FOV、有限近远平面与纹理数组
结构在入口校验。纹理 ID 复用不表示内容不更新；仍每帧预处理。

`RealityEnvironmentOcclusionFeature` 通过 XR 相机的父 manager 检查当前发布帧，
仅在有效时加入 BeforeRenderingTransparents 的 alpha 合成。
动态失败或停更不影响原有 `RealityOcclusionMeshRenderer` /
`MeasuredMeshRetention` 的实测网格与 Collider 生命周期。
Removed 仍视为丢失观测，直到同空间的明确覆盖替换才退出；
房间切换继续清理旧房间资源。世界网格使用实时 HMD 位姿渲染。

SDK `DepthPreprocessing.shader` 在 16 样本 Gather 发现 0、1 或 NaN 时返回
未测量标记 `(0,0,0,-1)`。应用 `RealityEnvironmentDepth.hlsl` 扩大原始深度
约 3 像素的有效边界，并逐一检查参与双线性插值的四个统计 texel。
不能只检查已经插值的标记，否则无效邻域可能产生正数并错误通过。
仅在明确现实前景时应用软遮挡；均匀深度处于误差带时保留完整可见，
实际深度边缘仍软过渡。最终 GPU 输入的有效边缘 visibility 为 0.5。

`SpatialMeshDepth.shader` 在视点 8cm 内 clip 视觉片元，Collider 仍保留。
主场景 XR 相机与 `RoomNavigationSceneBuilder.CreateCameraRig` 的近裁剪为 2cm；
编辑器预览相机仍为原配置。此修改没有扩大角色 bounds 或替换角色资产。

最终 48/48 Metal GPU / 引擎专项通过；源码哈希在测试和 Release 构建之间一致。
Release 首次 Burst 1.8.16 哈希缓存入口出现 Encoding.GetEncoding 空引用，
独立重建进程设置 `UNITY_BURST_DISABLE_INCREMENTAL_PLAYER_BUILDS=1`
并使用 UTF-8 locale 后通过；生产 AOT 编译开启，98 个方法编译进原生库。
该环境选项只用于本次构建进程，未新增产品配置系统。

交付 `/Users/bytedance/MateSoul/Builds/MateSoul_DepthFallback_20261028.apk`，
实际 168,074,571 字节。CRC、v2 签名、对齐、ARM64/IL2CPP 与构建原生库
哈希一致性通过，生成 C++ 已检出 descriptor 和渲染门控；9 个相关输入
在最终测试与构建间一致。没有安装到头显。

SDK 升级需迁移上述入口验证、发布帧门控、预处理尺寸重建和无效样本标记。
原生 `EnvironmentDepthFrameDesc` 没有采集时间戳；持续返回成功但图像内部
冻结不能用 `LastUpdatedFrame` 检测。正常距离的错误扫描网格仍需真机证据；
新增四 texel 检查的 GPU 帧耗、2cm 近面的深度精度和现场低头表现尚未验收。

报告：
`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_低头偏头裁剪与深度兜底_20261028.md`。
数值回执：
`/Users/bytedance/MateSoul/项目沉淀资料/validation/depth_fallback_20261028.json`。

## 最新整合APK与离线构建交接（2026-10-28，20:16快照）

`scripts/run-headset.py --build-only`允许未连接头显时先完成保存场景Release
构建和CRC/签名/对齐/原生库/Gradle身份核验，写入`result.json`中的
`installed=false`。默认无参数入口仍要求连接设备并覆盖安装，Run JSON未更改。
生产快照扩展到本地XR SDK和HLSL，本次3362项输入前后一致。

构建进程沿用已验证的`UNITY_BURST_DISABLE_INCREMENTAL_PLAYER_BUILDS=1`
和UTF-8 locale，避免Burst1.8.16增量哈希读取空引用，保留AOT编译。
环境参数只约束构建进程，不进入游戏配置；用户级metrics写入受限不能掩盖
Unity退出0与已核验产物，也不能据此声称设备安装完成。

最新包`Builds/MateSoul_AllLatest_20261028-201655.apk`实际168,074,571字节，
SHA256=`536eb440aa82e95895121b4cd764f5adc236d6a94f10c6fdcd9f38121c138855`。
36份生产/SDK文件在Player与ManagedStripped共72项源码证明通过。
包内七段动作Clip含Drink_01（3.3秒），调味物品和环境光/酱粒Shader检出；
角色五网格与上一整合包相同，面部61形态，主XR近裁剪0.02m。

目标PB3A10MGL9220170B未连接，安装、存档核验和READY待设备恢复。
固定APK安装脚本位于本轮临时目录`install-ready.py`，再次执行即可在目标
连接时覆盖安装并比对存档和base.apk；缺少连接时只记录INSTALL_PENDING。
此前椅子完整坐起回归失败按原报告保留，不属于本次打包解决的范围。

详见`MateSoul_最新整合包待安装_20261028_201655.md`及
`validation/all_latest_20261028_201655.json`。

## 已验证安装回执与新进程 READY（2026-10-28，20:34）

上述168,074,571字节整合包已直接通过ADB覆盖安装到PB3A10MGL9220170B。
安装前后真实存档哈希一致，设备base.apk等于本地已验包的
`536eb440aa82e95895121b4cd764f5adc236d6a94f10c6fdcd9f38121c138855`。
不依据APKmtime或旧设备日志判断结果。

UnityPlayerActivity冷启动Status ok，Android显示TotalTime140ms；
本次PID9575日志与`startup-current.log`中的本轮启动UTC时间匹配。
20:34:40.822出现`MATESOUL_READY source=PICO Room Capture items=2
itemState=ready navMs=9`，证明房间和保存物品恢复就绪。
当前PID取样未见致命异常。Activity显示时间不能代替业务READY，
业务READY也不等于所有真人交互功能已验收。

安装回执补入`validation/all_latest_20261028_201655.json`；
原始当前PID日志为本轮系统临时目录`device-startup-installed-2034.log`。

## 物品接近站位误判与动作内导航投影（2026-10-28）

`ItemTransferStep.FindApproach()` 原先只接受理想动画站位 18cm 内的导航
落点。真机盘子 34 个采样命中全部超出该距离，尚未进入路径/占位检查就
被拒绝。`RuntimeRoomNavigation.TrySamplePosition()` 公共合同为
`Max(0.75m, maxDistance)` 查询范围，所以采样成功不能直接代表上层站位
准入成功。不要通过全局缩小导航采样范围解决动作准入问题。

新流程先完整执行原候选；全部失败后，单手拾取追加 65cm 附近落点搜索，
从实际落点面向抓取点，并减去动画 `contactGrip` 的横向夹角。预估误差
扣除 `PickupAnimationSet.MaximumCorrection` 的现有 15cm 后须 ≤65cm。
最终 IK 接触误差仍由 2.5cm →25cm →65cm 三层检查；第三层等待下一帧
已求解姿态并复查前两层的规则不变。预估修正信用不能替代实际抓取检查。

候选仍须经过完整路径及 `PhysicsBody.CanOccupy()` 的 4cm
穿入容差。放回 `fromHeld` 与双手动作保持原搜索；动作控制权仍归协调器。
新增内容不通过物品名称分支特判。喝水、自主白名单、切水果和保龄球
均维持原入口。

一次性 A-D 探针用于同设备前后对照，交付生产包前必须移除探针、启动
marker 与临时 HTTP build wrapper。生产只保留接近失败的单次统计日志，
通过 `DeviceStartupLog` 的 `MATESOUL_ITEM_APPROACH` 前缀落盘。
原本协调器捕获并展示的 HUD 错误不一定会出现在异常日志中，后续应优先
检查持久诊断记录，避免仅凭无异常 stacktrace 判定动作正常。

最终诊断逻辑 29/29 编辑器专项与回归通过，Unity 退出 0；覆盖导航投影、
原站位优先、断路/实体障碍拒绝、三层吸附、喝水白名单及动作控制权。
正式产物、真机站位结果与清理回执见
`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_物品接近站位误判修复_20261028.md`
及 `/Users/bytedance/MateSoul/项目沉淀资料/validation/item_approach_20261028.json`。

正式 Release 清理探针后编译退出 0，3362 项输入保持一致，168,092,347
字节 APK 已于 23:24 覆盖安装到 PB3A10MGL9220170B。设备 APK 与本地
`4451eccb…d11d3cd` 一致，安装前后真实存档 SHA256 一致。临时服务器、
marker、USB reverse 与常亮设置均已收尾，设备保留正式版本。

最终诊断包的站位探测未执行：该次 Scene Capture 超时，Spatial Mesh
返回 `ERROR_RUNTIME_FAILURE`；电源查询显示 Asleep。禁止将这次环境启动
失败解释为新的站位算法失效，也禁止声称现场盘子已验证可达。
需要头显唤醒后的新 READY 与真人盘子/马克杯交互证据。

正式版本启动 UTC `2026-10-28T15:24:32.1079080Z`，23:25:01 同样记录
上述 Spatial Mesh 错误；本轮 `freshProductionReady=false`，不引用先前
已成功启动的旧日志来证明当前包就绪。

## 床标定辅助识别交接（2026-10-28）

`RealBedScanner`只从`Source == "PICO Room Capture"`的快照提取有效 Bed
标定，调用`BedGeometry.Extract(triangles, floorY, calibratedBeds)`。原两参数
实时 Bed 提取保持优先。每个已定位床盒扩大15cm，局部接纳Bed/Unknown面；
明确其他语义不改标签。新增`BedTarget.CalibrationId`与派生
`CalibrationAssisted`仅标识本次候选依据，动作仍使用现有Lie capability。

局部水平面必须处在地面以上35–90cm；按面积找跨度20cm内的主导高度带，
将实测均值作为床面高度，避免床头最高点影响。床长轴取标定长轴，
中心与宽长由当前顶点求得。实测宽/长至少覆盖标定宽/长的65%/70%，再经过
原面积、尺寸、7×9采样55%覆盖、10cm RMS检查。无需片段共享顶点，
仍不能用无网格标定盒或零碎中间片段直接制造床面。

枕头/结构/默认头向、20cm低细节与ContactLift、两次稳定确认、人体净空、
目标撤销和动作退出保持统一。`SamePose`比较几何，原生/辅助切换不会单因
字段或块ID改变而重建交互身份。辅助床上床净空使用
`IsCalibratedFrame`局部三角面检查，不能直接忽略同一Unknown块上的外物。
`RealityMeshSource`仍将实时Unknown原样保留，不把旧标定灌回全局语义。

状态区分：标定已定位但网格不足、测量成立正在确认、较高障碍阻挡、可用。
Editor/Development每两秒至多一条`MATESOUL_BED_SCAN`记录calibrated、
measured、assisted、blocked、available和triangles。

真实Unity验证30/30（21专项+7既有回归+2PhysX动作），原生退出0，
142项输入一致。动作测试仅在外部临时镜像改用FixedStep与有限帧预算，
生产动作未作测试适配；临时入口已清理。外层metrics数据库权限错误不覆盖
唯一XML和实际进程证据。本轮未生成APK或安装，现场定位与躺卧效果待真机。

报告`MateSoul_床标定辅助实时精细识别_20261028.md`，
数值`validation/bed_calibration_20261028.json`。仅改床，不将本路径
自动用于椅子等移动家具。

## 生气与沮丧共用音频时钟表演（2026-10-29）

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ResponseEmotionAudio.cs`
在 `BindRuntime` 注册 Angry / Sad 的情绪、动作 ID、动画和状态绑定。
两种动作共用 `SynchronizedEmotionAction`，不在中心协调器添加情绪分支，
也不创建第二套动作执行器。既有 Angry 接口保留，新增 Sad 动画和进度接口。

1. AI 对话或主动回复的情绪进入同一队列，选择对应音效池。
2. 无阻塞占用且会话可用时，通过 AvatarActionCoordinator 提交动作。
3. 动作同帧将 Animator 状态和 AudioSource 归零，立即求值起始姿态并播放声音。
4. 每帧用 `timeSamples / frequency / animation.length` 设置状态进度；
   Emotion 状态 `speed = 0`，结束段姿态保持到声音结束。
5. 完成、抢占、暂停、禁用均停止声音并清零层权重。动作的 `started` 标记
   保证拒绝准入或冲突检查时的 Dispose 不会清理另一条正在运行的表演。

`AvatarLocomotionBuilder.ConfigureEmotionState` 复用既有 Emotion 层及状态；
生产控制器重建同时挂载 Angry、Sad，不替换主控制器或既有片段。
`SadEmotionBuilder` 通过现有预定义编辑程序集的通用导入器重定向身体，
将原动态闭眼、张嘴、悲伤与下齿曲线适配到生产 Fcl/Game 表情词汇。

资源位于
`/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Emotion/Sad/`。
原动作 3.266667 秒，复用音效 3.28 秒，不拉伸动作或修改音高。
55 骨骼、550 身体绑定、61 面部绑定、9 动态形变；生产形体未替换。

真实 Unity 检查 5/5：沮丧 301 音频位置采样 + 2,174 连续播放帧，
时钟误差 5.96046448e-8，Animator 状态进度误差 0，旋转采样误差 0°，
面部权重误差 0.000106812。生气连续 1,566 帧验证原同步行为保持。
高优先级交互等待/抢占、无效准入、暂停与禁用的同步清理均已验证。

本轮环境问题来自 Library/PackageCache 中指向已清理临时目录的包链接，
恢复同版本完整缓存后编译通过。NUnit 与测试框架已保存为标准生成缓存目录。
编辑器临时源码链接修改后可能未触发重新编译；验证时变更链接资源路径，
确认新版字段出现在数值报告，避免复用旧检查程序集。临时入口及 meta 已移除。
清理后再次验证生产工程编译，原生退出码 0，耗时 31.025 秒。

报告：
`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_沮丧动画与音效同步_20261029.md`；
数值：
`/Users/bytedance/MateSoul/项目沉淀资料/validation/sad_emotion_20261029.json`。
本轮未重新打包或安装，最终头显听画同步仍待真机验收。

## 时钟导入姿态校准与现实时间同步（2026-10-29）

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RealTimeClock.cs`
保留单一时间采样入口 `Synchronize`，只写两个指针 Pivot 的局部旋转。
本次修复优先于历史章节中“减去 Blender authoredAngle”的实现说明。

1. 生产 FBX 导入后，Pivot 的局部 Y 轴在钟面坐标中为 −Z，正角度仍沿表盘
   顺时针旋转。Blender 默认时针 −52°、分针 +56°在 Unity 中变为
   表盘 +52°、−56°，角度符号翻转。
2. `authoredHourAngle` / `authoredMinuteAngle` 保留 Blender 源角度的原语义；
   `Awake` 仍缓存未运行的 FBX 默认旋转。运行时将目标角度与源角度相加，
   抵消导入姿态后设置显示，不修改模型导入配置或重新保存指针姿态。
3. 分针目标角为 `Minute * 6`；时针目标角为
   `(Hour % 12) * 30 + Floor(Minute / 5) * 2.5`。忽略秒与毫秒。
4. `OnEnable`、`OnApplicationPause(false)`、`OnApplicationFocus(true)`
   立即调用 `Synchronize`；正常 `Update` 按 `Time.unscaledTime` 每
   0.25 秒采样设备本地 `DateTime.Now`。游戏暂停不冻结现实时间。

旧逻辑在 00:00 显示 104° / 248°，全天始终带相同固定偏差；仅比较相邻
Quaternion 的角度增量会漏检。后续时钟资源变更应验证实际网格相对表心的
方向，覆盖 12 点及其他已知刻度，不能直接将 Blender 轴向/角度当作导入结果。

正式 Prefab 主工程 Play Mode 验证 12 项通过，全天 1,440 分钟 × 3 个秒位置
共 4,320 次采样，最大误差 0.0003967285°。任意摆放、时间回拨、
5 分钟/午夜/跨年边界、启用/恢复回调及冻结游戏时间后的真实 Update 均通过。
恢复回调属于桌面模拟，PICO OS 前后台与设备时间仍需现场验收。
源 FBX、Clock Prefab、角色、控制器与主场景哈希一致，临时检查入口已移除。

报告：
`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_时钟现实时间修复_20261029.md`。

数值：
`/Users/bytedance/MateSoul/项目沉淀资料/validation/clock_time_20261029.json`。
通过 `BuildCurrentSceneAndroidRelease` 构建正式已保存场景，3,366 项源输入
哈希一致。IL2CPP 生成代码已验证两根指针使用目标角加源角，
`Synchronize` 使用 `DateTime.Now`；APK 包内 ARM64 库与当前 Gradle 产物一致，
CRC、签名、zipalign 均通过。修复包为
`/Users/bytedance/MateSoul/Builds/MateSoul_ClockTimeFix_20261029.apk`，
168,237,103 字节，构建及校验 230.43 秒；当前未连接头显，未安装实测。

## 冷月 Direct59 累计资产绑定交接（2026-10-29）

生产角色不直接替换为 Direct59 独立 Prefab。主工程继续以
`Assets/MateSoul/Content/Lengyue_Runtime.prefab` 为唯一运行时角色，
只将最新版视觉资源接到既有 SkinnedMeshRenderer：

1. `Body` 的 mesh 继续使用
   `Direct55/Body_Direct54_LimbRefinement.asset`，材质槽 0 更新为
   `Direct59/BodySkin_Direct56.mat`。
2. `Face` 的 mesh 继续使用
   `Direct55/Face_Direct54_GameExpressions.asset`，保留 61 个游戏 Morph；
   材质槽 2 更新为 `Direct59/FaceExpression_Direct57.mat`。
3. `Hair` 更新为 `Direct59/Hair_Direct59.asset`。
4. 发束线对象更名为 `HairStrandLines_Direct59`，mesh 更新为
   `Direct59/HairStrandLines_Direct59.asset`；继续复用原 Direct51 发束线材质、
   头发骨骼数组和 bind pose 合同。
5. 主场景继续通过 GUID `b62edb46b12e444e5953050e8431e291`
   实例化生产 Prefab，不保存第二套角色。

Direct59 来源只修改 Hair 分束 84 的 79 个顶点，继承 Direct58 的累计几何；
无三角形翻转或新增退化面。Direct56/57 是 Unity 侧贴图与材质变化，不能用
Direct59 Blend 的旧贴图覆盖。生产集成因此必须拆分为“保留游戏网格 +
更新材质/头发”的组合，不能整包覆盖现有 Prefab。

`ProductionCharacterValidation.Run()` 已扩展检查：

- Direct54 游戏面网格和 61 Morph；
- Direct59 头发与同骨骼、同 bind pose 的发束线；
- Direct56 身体材质与 Direct57 面部表情材质；
- SoftFit01 服装、脚部静止位置、缺失脚本/材质/骨骼；
- 主场景实例与生产 Prefab 的 mesh、材质一致性。

该源码已用 Unity 2022.3.38f1 自带 Mono `csc`、UnityEngine/UnityEditor 模块和
当前 `MateSoul.Editor.dll`、`MateSoul.Runtime.dll` 独立编译通过。此结果证明
校验器 API 绑定有效，但不替代 AssetDatabase 导入和场景运行。

合入前后验证采用反向替换不变量：把 Direct59 的对象名、两个 mesh GUID 和两个
material GUID 恢复为旧值后，Prefab 必须与合入前备份逐字节一致。该方法能保护
当前工作区中已有的移动速度、拾取动画和其他并行功能改动。

当前 batchmode 在加载项目之前因 `com.unity.editor.headless` entitlement 失效，
不能据此判断资源编译结果。授权恢复后的执行顺序为：

1. 运行 `LengyueIntegration.Editor.ProductionCharacterValidation.Run`。
2. 运行角色动作/表情/发束物理回归。
3. 从当前保存场景生成新的 Android ARM64 / IL2CPP Release。
4. 校验 APK CRC、签名、zipalign、原生库和包内角色绑定。
5. 在 PICO 真机检查眉色、拇指、右侧耳旁发缝和动态发束。

资源与报告：

- `/Users/bytedance/MateSoul/MateSoul/Assets/LengyueIntegration/Direct59/`
- `/Users/bytedance/MateSoul/ArtSource/LocalStudyOnly/Lengyue_ProductionMerge_20261029/`
- `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_Direct59冷月主工程合入_20261029.md`
- `/Users/bytedance/MateSoul/项目沉淀资料/validation/lengyue_direct59_20261029.json`

## Codebase 仓库发布与跨设备开发交接（2026-10-30）

正式仓库为 `https://github.com/picoxr/VibeCodingSampleProject_MateSoul`。当前完整快照位于
`import/matesoul-snapshot-20261030`，MR `!1` 合入受保护 `main` 前需要
1 名 Developer 及以上成员批准，Auto merge 已启用。

### 克隆与打开

新设备必须先安装 Git LFS，再克隆仓库：

```bash
git lfs install
git clone https://github.com/picoxr/VibeCodingSampleProject_MateSoul.git
```

Unity Hub 中应打开克隆目录下的 `MateSoul/` 子目录，而不是仓库根目录。
编辑器版本固定为 Unity `2022.3.38f1`，并安装 Android Build Support、
SDK、NDK、OpenJDK。首次打开由 Unity 重建 `Library/` 并恢复注册表包。

### 仓库依赖边界

`/Users/bytedance/MateSoul/MateSoul/Packages/manifest.json` 中的本地包引用
全部使用相对路径：

1. `com.bytedance.pico.xr` 指向仓库根目录 `XR/`。
2. `com.vrmc.gltf` 指向
   `Dependencies/Lengyue/UniVRM-0.131.3/UniGLTF`。
3. `com.vrmc.univrm` 指向
   `Dependencies/Lengyue/UniVRM-0.131.3/VRM`。

这些目录必须与 `MateSoul/` 一起克隆，不能只复制 Unity 子目录。
`Library/Temp/Obj/Builds/Logs/UserSettings`、本机密钥和验证产物不属于
可移植源码，继续由根 `.gitignore` 排除。

### Git LFS 合同

以下 4 个 Unity 序列化纹理必须保持由目标仓库 `.gitattributes` 精确追踪：

- `MateSoul/Assets/Study/A_ImportedResources/032_Texture2D.asset`
- `MateSoul/Assets/Study/A_ImportedResources/037_Texture2D.asset`
- `MateSoul/Assets/Study/A_ImportedResources/042_Texture2D.asset`
- `MateSoul/Assets/Study/A_ImportedResources/044_Texture2D.asset`

它们各为 134,218,768 字节，不能改回普通 Git blob。提交大资源前先检查
`git lfs ls-files`；新设备克隆后执行 `git lfs fsck`，并确认上述文件不是
约 134 字节的 LFS 指针文本。

### 已验证不变量

- Codebase 分支最新提交：`9379e3f24d0aab0420cfe3230cf28b0c819bdc58`。
- 版本化文件 5,271 个，Unity `.meta` 0 缺失、0 孤立。
- 4 个 LFS 对象均可从 Codebase 下载，大小和 SHA-256 与发布源一致。
- 普通 Git 最大 blob 67,281,324 字节，低于 Codebase 100 MiB 限制。
- 无大小写不敏感路径冲突、仓库符号链接和被禁止的本机/构建产物。
- 可选 `services/ai-gateway` 在全新克隆中 TypeScript 编译通过，但依赖审计
  仍有 3 个 moderate、3 个 high 漏洞；当前 Unity 运行时不依赖该服务。

任何后续提交若新增超过 100 MiB 的必要资产，应先评估是否确实属于生产依赖，
确认后再增加精确 LFS 路径；不要用宽泛的 `*.asset` 规则把所有 Unity YAML
资产都迁入 LFS。

## 仓库范围与美术源边界 — 2026-10-30

仓库定位已从"可运行发布快照"调整为"可继续开发的工作副本"，因此 `.gitignore`
改为白名单式放行，新增内容如下：

- `项目沉淀资料/`：整体入库，但排除 `资产交付/`。交付 zip/FBX 属大体积二进制
  交付物，需要时在本地重新生成，不随仓库分发。
- `ArtSource/`：不再整体忽略，只放行当前冷月生产链。Git 忽略规则无法在父目录
  被排除时重新纳入子文件，因此使用逐级 `!` 链：
  `/ArtSource/*` → `!/ArtSource/LocalStudyOnly/` → `.../Onigawara_Lengyue_20261012/`
  → `.../eye_shape_reference_v12_dcc_real/` → `.../Fix5[4-9]/`，并在全局
  `*.blend` 规则之后追加 `!.../Fix5[4-9]/*.blend` 以覆盖该行。
- 全局 `*.log`、`*.zip`、`**/renders/`、`*.blend1` 规则继续生效，所以美术链里的
  工作渲染、构建日志和 Blender 备份不会被带入。

外观升级的两条路径：

- Unity 侧改材质、shader、贴图、网格 asset 与 SpringBone 参数，仓库已具备
  全部资产（生产预制体 `Assets/MateSoul/Content/Lengyue_Runtime.prefab`、
  `Assets/LengyueIntegration/Direct59/`、`SapphiArtCustom/Lengyue/QualityUpgrade/v003/`）。
- DCC 侧重制几何与烘焙贴图，需要 `Fix54`–`Fix59` 的 `.blend` 与构建脚本；
  这些已入库。衣柜历史（`Wardrobe/v00x`）、EyeLab 实验源与旧 `Unity6Study`
  工程不在仓库内，若任务需要必须先在本机补齐。

新增超过 100 MiB 的必要资产仍应先评估是否属于生产依赖，再用精确路径加入 LFS；
不要用宽泛的 `*.asset` 或 `*.blend` 规则整体迁移。

## 开发资产与 SDK 位置 — 2026-10-30

结论与约束：

- `XR/` 位于仓库根而非 `MateSoul/Packages/`。`com.bytedance.pico.xr` 以
  `file:../../XR` 相对解析，整仓 clone 后打开 `MateSoul/` 即可；任何"只拷 Unity
  工程目录"的做法都会让该依赖失效。若要改为自包含，需整体迁移并同步重写
  `.gitignore` 中 `XR/**` 规则，属独立变更。
- 测试、验证场景、探针、Editor 调试构建器、XRI 设备模拟器、PICO SDK 的 Debugger
  与样例、UniVRM 包内样例与测试，均已入库。它们与运行时代码同一仓库，改动需同样
  遵守"不破坏生产组件/Action Coordinator/动画事件/GUID"的约束。
- 同名 Editor 类存在两份：`Assets/MateSoul/Editor/`（`MateSoul.Editor` 程序集）
  与 `Assets/Characters/SapphiArtCustom/Lengyue/Editor/`（Assembly-CSharp-Editor）。
  跨程序集同名不冲突，但修改行为时须确认调用方所属程序集。
- 仍不入库：APK/AAB、`*.log` 运行日志、`**/renders/`、`*.blend1`、`XR/**/*.pdb`
  （45 MB 调试符号）、`项目沉淀资料/资产交付/`、`ArtSource` 的历史源与旧工程。
- 两次发布都出现过"重新暂存把 LFS 实体写回索引"的问题：重建索引后必须核对
  `git ls-tree -l` 中 4 个 `Study/A_ImportedResources/*_Texture2D.asset` 仍为
  LFS 指针，否则会被 100 MiB 限制拒绝。
