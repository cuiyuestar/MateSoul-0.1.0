# MateSoul 首版开发计划（已归档）

> 最后更新：2026-10-01
> 状态：历史方案，Kotlin / FullStage / PICO Spatial SDK 路线已废弃  
> 参考项目：`/Users/bytedance/MateFairy`  
> 当前资产：`/Users/bytedance/MateSoul/girl.glb`

> **禁止作为当前实施基线。** MateSoul 当前唯一生效的游戏技术路线是
> Unity 2022.3.38f1 + URP + `com.bytedance.pico.xr`（PICO Unity Integration
> SDK）+ XR Interaction Toolkit + Animation Rigging。不得配置
> `com.pico.spatial:bom`、Kotlin、Compose、FullStage 或 Spatial ECS。
> 当前方案见：
> `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_Demo开发方案.md`。

## 0. 文档目的

本文档用于定义 MateSoul 首版的产品边界、技术架构、模块职责、开发阶段、验证方式和主要风险。

MateSoul 的整体体验参考 MateFairy，但不直接照搬 MateFairy 的实现。MateFairy 是一个已能运行的 XR 虚拟陪伴项目，证明了 PICO Spatial SDK + Kotlin + Compose + ECS + LLM + 记忆系统的可行性；但其动画、情绪、动作锁和行为系统在连续迭代后出现了较明显的控制权分散、补丁式状态恢复和全局依赖穿透。MateSoul 首版需要把这些问题前置解决。

本计划采用 Vibe Coding 的“架构先行、最小最优、验证闭环”策略：首版不追求对象玩法丰富度，而是先打稳虚拟少女的身体控制、地面移动、心情值系统、动画调度和对话闭环。

## 1. 需求理解

### 1.1 项目目标

MateSoul 是一个面向 PICO OS 6 的 XR 虚拟恋爱/陪伴游戏。核心角色从 MateFairy 中飞在空中的精灵，变为走在地上的少女。

首版目标是交付一个可运行、可演进的基础版本：

- 少女模型能在 Full Space 中加载并稳定显示。
- 少女以“地面角色”的方式站立、待机、靠近玩家、跟随玩家。
- 用户可以通过文本输入与少女对话。
- LLM 输出可以驱动回复文本、心情语义识别和未来预留的 action 意图。
- 心情值系统、动画调度、状态机和控制权租约机制在首版即成为核心架构。
- 首版完整实现难过值、生气值、开心值、甜蜜值的基础逻辑，并支持未来扩展更多心情类型。
- 当前 `girl.glb` 没有动画轨道时，调度系统仍可通过 NoOp 播放器完整跑通；后续补充 `walk / happy / sweet / angry / sad` 动画时不需要重构核心链路。

### 1.2 首版非目标

以下能力不进入首版必做范围：

- 对象玩法，例如足球、音响、小黄鸭、拾取、投掷、坐椅子等。
- 完整 MCP 工具调用和联网搜索。
- 完整 L2/L3/L4 长期记忆、RAG 召回和 Dream 蒸馏。
- 复杂语音输入、手势输入和多模态交互。
- 复杂现实语义感知，例如自动识别椅子、桌面并执行语义动作。
- 动作 action 的完整业务方案。首版只保留 action 接口和协议占位，不对 LLM 暴露未实现 action。

### 1.3 成功标准

首版完成时应满足：

1. 项目可以编译、安装并在 PICO OS 6 Full Space 中启动。
2. 少女实体可加载，并位于玩家前方合理地面位置。
3. HMD 追踪可驱动少女地面跟随状态机。
4. 文本输入可触发 LLM 或 Mock LLM 回复，并显示空间对话气泡。
5. 文本语义识别结果可以驱动 `happy / sweet / anger / sadness` 四类心情值变化。
6. 心情值达到阈值后可以派生轻度、中度、高度状态，并驱动对应表现请求。
7. 动画调度器支持 `idle / walk / happy / sweet / angry / sad` 的语义动画请求，即使当前资源不存在也能返回明确的 NoOp 结果。
8. 控制权租约机制能保证同一时间只有一个高优先级模块控制少女身体。
9. action 处于明确待定状态：接口存在，但不会产生伪功能或状态分叉。

## 2. 现有输入与约束

### 2.1 MateSoul 当前仓库状态

当前 MateSoul 仓库基本为空，仅包含：

- `.git/`
- `girl.glb`

这意味着 MateSoul 可以从更干净的基础上设计架构，不需要兼容旧代码。

### 2.2 当前少女资产状态

已知事实：

- `/Users/bytedance/MateSoul/girl.glb` 不包含 glTF 标准动画轨道。
- 当前无法直接播放骨骼动画或变换动画。
- 该资产可以用于首版静态加载、空间定位、碰撞代理和状态机联调。

后续计划：

- 用户后续会补充行走动画。
- 用户后续会补充开心、甜蜜、生气、伤心心情表现动画。

工程策略：

- 首版动画系统必须用“语义动画名”驱动，而不是用硬编码 trackIndex 驱动。
- 当前使用 `NoOpAnimationPlayer` 保持调度链路可测。
- 补充动画后，只修改 `AnimationCatalog` 或资源映射，不改状态机、情绪控制器和控制权租约。

### 2.3 PICO Spatial SDK 关键约束（历史资料，当前不执行）

MateSoul 首版应采用 Stage：

- 地面少女需要 HMD 追踪和后续可能的手势/现实感知能力。
- Tracking 和 Sense 相关能力要求 Full Space。
- Stage 同一应用内只能有一个。
- Stage 坐标系为右手系，单位为米，原点在用户脚下，`+Z` 朝向用户。

资源加载约束：

- glTF/GLB 支持，但 USD / Spatial Editor bundle 在可控资产管线中更稳定。
- `Entity.loadSuspend("asset://girl.glb")` 是推荐异步加载方式。
- 在 `SpatialView(initial = { ... })` 中一次性加载并 `content.addEntity(root)`。
- 动画 API 必须在主线程调用。
- `SpatialView` 退出时需要显式清理实体和播放控制器。

交互约束：

- 程序化 raycast / convexCast 需要 `CollisionComponent`。
- 用户交互需要 `CollisionComponent + InteractableComponent`。
- 首版少女至少需要一个简化 capsule 碰撞代理，视觉模型和交互/移动代理分离。

## 3. MateFairy 参考结论

### 3.1 应复用的设计资产

MateFairy 中以下思路值得继承：

- 使用 FullStage 作为主体验容器。
- 使用 application-scope runtime 统一装配核心服务，避免 Compose 重组重复创建 LLM / memory / mcp。
- 使用 `ConversationOrchestrator` 管理“用户输入 -> prompt -> LLM -> 结构化回复 -> 情绪/动作分发”。
- 使用结构化 LLM JSON 协议，降低自然语言直接控制游戏逻辑的风险。
- 使用空间对话气泡承载 AI 回复。
- 使用 ECS System 驱动连续空间行为。
- 使用 model visual entity 与 physical/control proxy entity 分离，避免模型缩放、动画和碰撞互相污染。

### 3.2 MateFairy 中应规避的问题

以下是 MateSoul 需要主动改善的结构问题：

1. 动画层知道太多业务状态  
   MateFairy 的 `AnimationModule` 直接读取 action lock、player interaction runtime dependencies。动画调度器因此不再是纯粹的播放/仲裁模块，而是掺入了动作系统和玩家交互系统的状态判断。

2. 行为系统职责过重  
   MateFairy 的 `FairyBehaviorSystem` 同时处理随机移动、跟随、动作恢复、携物、语义驻留、mesh 约束、动画请求和面向策略。文件体量超过 1000 行，后续维护成本高。

3. 锁机制分散  
   MateFairy 中普通 action、玩家触碰 action、动画 owner 和行为让权分别维护自己的状态，依赖 interrupt bus 和全局 RuntimeDependencies 协调。它能运行，但容易形成“多处事实源”。

4. 情绪模型过薄  
   MateFairy 的 `EmotionEngine` 主要是字符串去重和动画映射，没有情绪强度、持续时间、衰减、冷却、打断策略，也没有与状态机形成清晰关系。

5. 动画资源映射过于绑定 trackIndex  
   MateFairy 使用硬编码轨道索引，例如 `MAD_ACTION(26, ...)`。这对固定机器人资源可接受，但不适合 MateSoul 当前“动画稍后补齐”的资产阶段。

6. 暴露未接稳的 action 能力  
   MateFairy 的 supported action 中存在一些未来能力或局部实现能力。MateSoul 首版应避免向 LLM 暴露未实现 action，防止玩家看到“系统声称能做但实际不能做”的行为断裂。

## 4. 架构决策：Avatar Runtime-first

MateSoul 首版采用 Avatar Runtime-first 架构。

核心思想：外部模块不直接控制少女动画、情绪或移动，只提交 `AvatarIntent`。`AvatarRuntime` 是虚拟少女身体和表现的唯一入口。

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

这种设计的目的：

- 让少女的“身体控制权”只有一个事实源。
- 让心情状态、动作、移动都通过统一优先级仲裁。
- 让动画调度器只处理动画请求，不直接依赖 action 系统。
- 让 action 在方案未定时可以安全占位，而不污染首版主链路。
- 让当前没有动画轨道的资产也能运行完整逻辑。

## 5. 模块划分

建议包结构如下：

```text
com.example.matesoul
  platform/
    SpatialApplication.kt
    LaunchActivity.kt
  scene/
    HomeStage.kt
    AvatarSceneInstaller.kt
    SpatialAttachmentInstaller.kt
  avatar/
    AvatarRuntime.kt
    AvatarIntent.kt
    AvatarEntityBinding.kt
    AvatarRuntimeFactory.kt
  avatar/state/
    AvatarState.kt
    AvatarStateMachine.kt
    AvatarStateTransition.kt
  avatar/control/
    ControlLease.kt
    ControlLeaseManager.kt
    ControlPriority.kt
  avatar/mood/
    MoodType.kt
    MoodValence.kt
    MoodLevel.kt
    MoodMeter.kt
    MoodState.kt
    MoodComponent.kt
    MoodSystem.kt
    MoodTransitionQueue.kt
    SoulFactorMapper.kt
    MoodPersistenceStore.kt
  avatar/animation/
    AnimationKey.kt
    AnimationCatalog.kt
    AnimationScheduler.kt
    AnimationPlayer.kt
    PicoAnimationPlayer.kt
    NoOpAnimationPlayer.kt
  avatar/locomotion/
    GroundLocomotionComponent.kt
    GroundLocomotionSystem.kt
    GroundTargetPolicy.kt
  ai/
    ChatMessage.kt
    AIResponse.kt
    ILLMProvider.kt
    MockLLMProvider.kt
    DeepSeekLLMProvider.kt
  orchestrator/
    ConversationOrchestrator.kt
    AvatarCommandPort.kt
  ui/
    DialogueBubbleUI.kt
    TextInputPanel.kt
```

首版优先实现 `avatar/*`、`scene/*`、`ai/*`、`orchestrator/*` 的最小闭环。

## 6. 核心运行链路

### 6.1 对话到角色表现

```text
用户文本输入
  -> ConversationOrchestrator.processUserInput()
  -> LLM 返回 AIResponse(replyText, actionIntent)
  -> MoodSemanticClassifier 结合上下文输出四类心情增量候选
  -> UI 显示 replyText
  -> AvatarCommandPort.submit(AvatarIntent.MoodImpact(...))
  -> actionIntent 若为 none 则忽略；若非 none 但未注册，返回明确 unsupported
  -> AvatarRuntime 内部完成心情值、动画和状态仲裁
```

### 6.2 地面移动到动画

```text
GroundLocomotionSystem 每帧读取 HMD proxy 和 avatar proxy
  -> 根据距离与状态机更新 AvatarState
  -> 申请 BASE_LOCOMOTION 控制权
  -> 移动代理实体位置与朝向
  -> 向 AnimationScheduler 请求 AnimationKey.WALK 或 AnimationKey.IDLE
```

### 6.3 心情值到动画

```text
LLM / 玩家触碰 / 调试命令产生 MoodImpact
  -> MoodSystem 更新 MoodMeter
  -> MoodSystem 根据阈值派生 ActiveMoodState
  -> 若状态表现需要独占身体，申请 MOOD_REACTION 控制权
  -> AnimationScheduler 请求 HAPPY / SWEET / ANGRY / SAD 对应表现
  -> 状态转移事件到期后进入 MoodTransitionQueue
  -> 若控制权被占用，事件按 token 挂起，释放后再消费
  -> 状态机回到 IDLE / FOLLOW_USER / STAY_NEAR_USER
```

## 7. AvatarIntent 设计

所有外部输入统一转成 `AvatarIntent`：

```kotlin
sealed interface AvatarIntent {
    data class DialogueReply(
        val text: String,
        val actionIntent: String?
    ) : AvatarIntent

    data class MoodImpact(
        val impacts: List<MoodImpactItem>,
        val source: IntentSource
    ) : AvatarIntent

    data class PlayerTouched(
        val source: TouchSource
    ) : AvatarIntent

    data class ActionRequested(
        val actionId: String,
        val params: Map<String, String>
    ) : AvatarIntent
}
```

首版实现策略：

- `DialogueReply`：显示文本由 UI 处理，action 只做待定协议处理。
- `MoodImpact`：进入心情值系统，更新 MoodMeter 并派生表现状态。
- `PlayerTouched`：首版可先保留调试入口，未来转成 `MoodImpact`。
- `ActionRequested`：只做协议占位。未注册 action 返回 `UnsupportedAction`，不进入身体控制。

## 8. 状态机设计

MateSoul 建议采用轻量分层状态机。

### 8.1 主状态

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

状态职责：

- `SPAWNING`：模型刚进入场景，完成初始定位和可选入场动画。
- `IDLE`：站立待机，玩家在舒适距离内，无主动移动。
- `WANDER_NEAR_USER`：在玩家附近小范围地面游走，首版可先不启用随机游走。
- `FOLLOW_USER`：玩家远离时，少女向玩家前方/侧前方移动。
- `APPROACH_USER`：对话或未来 action 要求靠近玩家时使用。
- `STAY_NEAR_USER`：少女到达玩家舒适距离后停留并面向玩家。
- `LOCKED_REACTION`：心情表现、玩家触碰或未来 action 临时占用身体控制。

说明：`LOCKED_REACTION` 是状态机层的通用占用态，后续实现时应理解为“心情表现、玩家触碰或 future action 临时占用身体控制权”，不要再实现单独的旧式情绪 lock。

### 8.2 关键转换

```text
SPAWNING -> IDLE
IDLE -> FOLLOW_USER                    当水平距离 > followStartDistance
FOLLOW_USER -> STAY_NEAR_USER          当水平距离 <= followStopDistance
STAY_NEAR_USER -> FOLLOW_USER          当玩家再次远离
IDLE / STAY_NEAR_USER -> LOCKED_REACTION  当高优先级心情表现或触碰租约生效
LOCKED_REACTION -> IDLE / FOLLOW_USER  租约释放后按当前位置重新评估
```

### 8.3 设计边界

状态机只决定“角色当前应该处于什么行为态”，不直接播放动画，不直接调用 LLM，不直接处理 action 业务。

`LOCKED_REACTION` 是通用占用态，后续实现时统一表示心情表现、玩家触碰或 future action 临时占用身体控制权，不再实现单独的旧式情绪 lock。

动画由 `AnimationScheduler` 根据状态和心情表现请求决定。

移动由 `GroundLocomotionSystem` 根据状态执行。

控制权由 `ControlLeaseManager` 判定。

## 9. 控制权租约机制

MateSoul 不使用 MateFairy 式分散 lock。首版引入 `ControlLeaseManager`，作为身体控制权唯一事实源。

### 9.1 数据模型

```kotlin
data class ControlLease(
    val owner: ControlOwner,
    val priority: ControlPriority,
    val reason: String,
    val expiresAtMillis: Long?,
    val interruptible: Boolean
)
```

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

### 9.2 仲裁规则

- 没有租约时，`BASE_LOCOMOTION` 可持续控制移动。
- 高优先级租约可以打断低优先级租约。
- 同优先级默认不打断，除非旧租约 `interruptible = true`。
- 租约超时必须自动释放。
- 每次释放后，状态机必须基于当前位置重新评估，不恢复旧的随机目标。

### 9.3 为什么用 Lease 而不是 Lock

Lock 只表达“不能进入”，但不能表达：

- 谁占用了控制权。
- 占用多久。
- 能否被打断。
- 释放后进入什么状态。
- 为什么被拒绝。

Lease 更适合游戏角色控制，可以把心情状态、未来 action、玩家触碰统一放到同一个仲裁框架下。

## 10. 心情值系统设计

### 10.1 设计目标

首版不做复杂的连续衰减、冷却抑制和打断策略，但需要把心情值系统的基础设施完整搭好。它不是简单的“情绪字符串 -> 动画”映射，而是一个可长期扩展的数值系统。

首版完整支持：

- 难过值
- 生气值
- 开心值
- 甜蜜值

未来可以继续扩展更多心情类型。

### 10.2 心情类型与正负分组

```kotlin
enum class MoodType {
    SADNESS,
    ANGER,
    HAPPY,
    SWEET
}

enum class MoodValence {
    NEGATIVE,
    POSITIVE
}
```

分组规则：

- `SADNESS`、`ANGER` 属于负面心情。
- `HAPPY`、`SWEET` 属于正面心情。
- 负面心情表现优先级高于正面心情表现。
- 负面心情进入表现状态时，如果当前正处于正面表现状态，则退出当前正面激活态，并清空该正面心情自身的 `MoodMeter` 数值。
- 负面心情进入表现状态时，如果当前不处于正面表现状态，则不清空任何正面心情值。
- 正面心情结束或切换时，不清空负面心情数值。
- 正面心情内部不会互相清空心情值。
- 任一心情真正退出自身 active state 时，必须清空该心情自身的 `MoodMeter` 数值。
- 上述“不清空”规则只针对其它心情值；退出方自己的心情值始终按退出规则清空。

补充约束：所有“不清空”规则只针对其它心情值；退出方自己的心情值始终按退出规则清空。

### 10.3 数据模型

心情值以 ECS Component 作为运行时事实源：

```kotlin
class MoodComponent(
    val meters: MutableMap<MoodType, MoodMeter>,
    var activeState: ActiveMoodState?,
    val pendingTransitions: MutableList<MoodTransitionSignal>
)

data class MoodMeter(
    val type: MoodType,
    var value: Float,
    var updatedAtMillis: Long
)

data class ActiveMoodState(
    val type: MoodType,
    val level: MoodLevel,
    val token: Long,
    val enteredAtMillis: Long
)

enum class MoodLevel {
    NONE,
    LIGHT,
    MEDIUM,
    HIGH
}
```

数值刻度：

```text
范围：0 - 100
轻度阈值：30
中度阈值：60
高度阈值：85
```

状态采用“数值 + 派生状态”模型：

- `MoodMeter` 只保存数值。
- `ActiveMoodState` 由数值、阈值和优先级派生。
- 避免每个心情值都保存一套独立状态导致多事实源。

### 10.4 Soul 双层模型

玩家看到的是可理解的人格维度，内部保存可参与公式计算的数值因子。

玩家可见五维双极：

```text
敏感 - 钝感
温柔 - 强势
独立 - 依恋
坦率 - 克制
自信 - 脆弱
```

内部派生因子由 `SoulFactorMapper` 生成。首版不把内部因子直接暴露给玩家，避免设置界面变成数值表。

内部因子至少需要覆盖：

- 难过增长因子
- 生气增长因子
- 开心增长因子
- 甜蜜增长因子
- 负面表现强度因子
- 正面表现强度因子

### 10.5 语义识别

首版采用“规则 + LLM 四分分类器”：

```kotlin
data class MoodClassificationResult(
    val sadness: MoodSignalScore,
    val anger: MoodSignalScore,
    val happy: MoodSignalScore,
    val sweet: MoodSignalScore
)

data class MoodSignalScore(
    val severity: MoodSeverity,
    val confidence: Float,
    val reason: String
)

enum class MoodSeverity {
    NONE,
    LIGHT,
    MEDIUM,
    HIGH
}
```

识别规则：

- 规则层先识别明显辱骂、数落、夸奖、亲密表达等信号。
- LLM 结合上下文进行四分分类，输出四类分数、语义强度和置信度。
- 低置信度不丢弃，而是按正常增量的 25% 生效。

### 10.6 增长公式

采用分段公式：

```text
delta = baseDelta(severity) * soulFactor(moodType) * confidenceFactor
```

建议默认：

```text
baseDelta(LIGHT) = 8
baseDelta(MEDIUM) = 18
baseDelta(HIGH) = 32
confidenceFactor = 1.0       当 confidence 达到有效阈值
confidenceFactor = 0.25      当 confidence 低于有效阈值
```

说明：

- 具体数值后续可配置化。
- 生气与难过公式使用不同的 `soulFactor`。
- 开心与甜蜜公式同样使用从 Soul 派生的正面因子。
- 玩家交互动作暂未开发，但未来也应转化为同一种 `MoodImpact`。

### 10.7 状态派生与冲突规则

负面冲突：

- 难过与生气同时达到阈值时，难过优先。
- 难过优先时，清空生气值。

正面冲突：

- 开心与甜蜜同时达到阈值时，甜蜜优先。
- 甜蜜优先只决定当前表现，不清空开心值。

正负冲突：

- 负面表现优先级高于正面表现。
- 正面状态不会清空负面心情值。
- 负面状态进入表现时，如果当前正处于正面表现状态，则清理当前正面激活态，并清空该正面心情自身数值。
- 负面状态进入表现时，如果当前不处于正面表现状态，则不清空正面心情值。
- 任一心情退出自身 active state 时，都必须清空该心情自身数值。
- “不清空”只表示不清空其它心情值，不改变退出方必须清空自身数值的规则。

### 10.8 状态回落事件

采用事件信号队列，不使用协程 delay 作为状态回落主机制。

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

事件处理规则：

- 每次进入状态时生成 `stateToken`。
- 状态回落事件携带 `stateToken`。
- 只响应与当前 `ActiveMoodState.token` 匹配的事件。
- 如果事件到期时控制权被占用，则事件挂起。
- 控制权释放后再消费挂起事件；如果 token 已不匹配，则丢弃。
- 轻度状态期间如果升级到中度或高度，则旧轻度退出信号无效。
- 中度状态期间如果升级到高度，则旧中度回落信号无效。

### 10.9 持久化

心情值需要长期持久化。

首版规则：

- 持久化 `MoodMeter` 数值、当前激活态、状态 token 和时间戳。
- 应用关闭期间不结算状态时间。
- 重新进入应用后，从保存时的状态继续。
- 后续如果发现负面状态冻结影响体验，再引入离线结算或安全恢复策略。

### 10.10 与控制权租约的关系

心情值系统不直接移动角色，也不直接操作模型。它只产出表现请求：

```text
MoodSystem -> ActiveMoodState -> AnimationScheduler / ControlLeaseManager
```

当进入高度难过状态：

- 少女原地站立不动。
- `MoodSystem` 申请 `NEGATIVE_MOOD_REACTION` 控制权。
- `GroundLocomotionSystem` 让出移动控制。

当状态回落信号到达但控制权被未来 action 占用：

- 信号进入挂起队列。
- action 结束后按 token 校验再切换状态。

### 10.11 与状态机关系

心情状态不是 `AvatarState` 的平行替代，而是影响状态机的上层表现态。

示例：

```text
AvatarState = STAY_NEAR_USER
ActiveMoodState = SADNESS_HIGH
结果：少女维持原地站立，不继续跟随，播放高度难过表现
```

这样可以避免状态爆炸成：

```text
IDLE_SADNESS_HIGH
FOLLOW_SADNESS_MEDIUM
STAY_HAPPY_LIGHT
...
```

## 11. 动画调度设计


### 11.1 动画语义键

首版不使用 trackIndex 作为业务入口，而使用语义键：

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

后续可扩展：

```text
TALK_SOFT
TURN_TO_USER
APPROACH
TOUCH_REACT
ACTION_GENERIC
```

### 11.2 AnimationCatalog

`AnimationCatalog` 负责把语义键映射到真实资源：

```kotlin
data class AnimationClipSpec(
    val key: AnimationKey,
    val trackName: String?,
    val trackIndex: Int?,
    val durationMs: Long,
    val loop: Boolean
)
```

规则：

- 优先按 trackName 匹配。
- trackName 不可用时才使用 trackIndex。
- 当前资源缺失时返回 `MissingClip`，由 scheduler 转为 NoOp。
- 资源映射集中在配置层，不散落在状态机和心情值系统中。

### 11.3 AnimationScheduler 分层

首版三层：

- `BaseLayer`：`IDLE / WALK`，由状态机和移动系统驱动。
- `MoodLayer`：`HAPPY / SWEET / ANGRY / SAD`，由心情值系统驱动。
- `ReservedActionLayer`：保留 action 层，不实现具体业务。

层级优先级：

```text
ReservedActionLayer > MoodLayer > BaseLayer
```

当前没有动画轨道时：

- `NoOpAnimationPlayer` 记录请求和结果。
- `AnimationScheduler` 仍返回 `AnimationPlayResult.NoOpMissingClip`。
- 单元测试可以验证调度逻辑。

## 12. 地面移动设计

### 12.1 与 MateFairy 飞行行为的差异

MateFairy 的主角在空中飞行，Y 轴跟 HMD 高度相关。MateSoul 的少女是地面角色，Y 轴应由地面规则决定。

首版地面高度策略：

1. 优先使用固定 Stage floor：`y = 0`。
2. 预留 Plane / Mesh grounding 接口。
3. 如果未来启用 spatial mesh，使用向下 raycast 或 plane anchor 纠正脚底高度。

### 12.2 跟随距离

建议初始参数：

```text
followStartDistance = 2.2m
followStopDistance = 1.2m
preferredDistance = 1.4m
sideOffset = 0.25m
maxWalkSpeed = 0.9m/s
turnSpeed = 120deg/s
```

说明：

- 距离过近会破坏舒适感。
- 地面少女不应像精灵一样贴脸悬浮。
- 初始版本使用侧前方跟随点，避免挡住玩家视野中央。

### 12.3 移动舒适性

移动规则：

- 不瞬移，除非首次 spawn 或 debug reset。
- 水平移动速度限制在舒适范围。
- 角色转向使用平滑插值。
- 角色始终保持脚底贴地，不做飞行式上下浮动。
- 后续补步行动画后，`WALK` 动画与实际速度绑定。

## 13. Action 待定策略

用户已明确 action 方案待定。因此首版只做接口设计，不做业务玩法。

首版 action 原则：

- 不向 LLM 暴露未实现 action。
- `AIResponse.action_intent` 首版允许 `none`，其他 action 默认返回 unsupported。
- 不实现伪 action，不为了演示而写空 handler。
- 保留 `ReservedActionLayer` 和 `ControlPriority.FUTURE_ACTION`，后续扩展不破坏现有控制权模型。

未来 action 候选方向可以单独评审：

- 对话驱动动作：靠近、挥手、坐下、转身。
- 亲密互动动作：牵手、躲闪、生气后退。
- 场景对象动作：拿起物体、放下物体、坐椅子。
- 现实语义动作：走到桌边、坐到椅子上。

这些不进入首版实现。

## 14. AI 回复协议

首版沿用结构化 JSON，但收敛字段：

```json
{
  "status": "ok",
  "reply_text": "回复给玩家的自然语言",
  "action_intent": "none"
}
```

约束：

- `action_intent` 首版只允许 `none`。
- 若模型输出未知 action，返回 unsupported，但不进入 AvatarRuntime 身体控制。
- 回复文本建议控制在 1 到 2 句，符合空间气泡阅读节奏。
- 心情变化不再由 `emotion` 字段直接驱动，而由 `MoodSemanticClassifier` 结合用户输入、上下文和规则层输出四分分类结果。

## 15. 开发阶段计划

### Phase 0：文档与工程边界确认

交付物：

- `MateSoul_首版开发计划.md`
- `technical_docs.md`
- `work_reports.md`

验证：

- 文档路径存在。
- 首版范围、非目标、架构决策和风险明确。

### Phase 1：FullStage 工程骨架（已废弃，禁止执行）

以下内容仅记录 2026-07-08 的旧路线，不是当前待办：

- ~~基于 FullStage 模板创建 MateSoul Android 工程。~~
- ~~配置 PICO Spatial SDK BOM、Compose、Kotlin、Gradle。~~
- ~~配置 Spatial SDK Android 工程的 `noCompress`。~~

旧验证命令（不再执行）：

- ~~`./gradlew :app:compileDebugKotlin`~~
- 当前应通过 Unity Android IL2CPP 构建和 PICO Emulator / 真机验证。

### Phase 2：少女模型加载与实体绑定

目标：

- 将 `girl.glb` 放入 app assets。
- 使用 `Entity.loadSuspend("asset://girl.glb")` 加载。
- 创建 `AvatarEntityBinding`：
  - visualRoot
  - bodyProxy
  - dialogueAnchor
  - collisionProxy

验证：

- 模型在 Stage 中可见。
- 代理实体和视觉实体可同步位置。
- 没有动画轨道时不崩溃。

### Phase 3：AvatarRuntime 与状态机

目标：

- 实现 `AvatarRuntime`。
- 实现 `AvatarStateMachine`。
- 实现 `ControlLeaseManager`。
- 支持 `IDLE / FOLLOW_USER / STAY_NEAR_USER / LOCKED_REACTION`。

验证：

- 单元测试覆盖状态转换。
- 租约优先级和超时释放可测。
- HMD 远离时进入 follow，接近后进入 stay。

### Phase 4：地面移动系统

目标：

- 实现 `GroundLocomotionSystem`。
- HMD proxy 同步头显位置。
- 少女以地面规则跟随玩家。

验证：

- 角色不飞行、不绑定 HMD 高度。
- 水平距离阈值正确。
- 速度、转向、停靠点稳定。

### Phase 5：心情值系统与动画调度

目标：

- 实现 `MoodComponent`。
- 实现 `MoodSystem`。
- 实现 `MoodTransitionQueue`。
- 实现 `SoulFactorMapper`。
- 实现 `MoodPersistenceStore`。
- 实现 `MoodSemanticClassifier`。
- 实现 `AnimationScheduler`。
- 实现 `NoOpAnimationPlayer`。
- 预留 `PicoAnimationPlayer`。
- 支持 `IDLE / WALK / HAPPY / SWEET / ANGRY / SAD` 语义请求。

验证：

- 当前无动画时请求返回 NoOp，不影响状态机。
- 心情值达到阈值后可派生轻度/中度/高度状态。
- 状态回落事件 token 校验正确。
- 正负情绪冲突和难过/生气、甜蜜/开心冲突符合规则。
- base 动画不会打断心情表现动画。

### Phase 6：对话闭环与空间 UI

目标：

- 文本输入面板。
- 对话气泡。
- Mock LLM 优先跑通。
- DeepSeek Provider 后接入。
- 文本语义分类结果接入 AvatarRuntime。

验证：

- 用户输入后出现回复。
- `sadness / anger / happy / sweet` 可进入心情值系统。
- 未实现 action 不会触发身体控制。

### Phase 7：动画资产补齐后的接线

目标：

- 用户补充动画后，更新 `AnimationCatalog`。
- 支持 idle、walk、happy、sweet、angry、sad 播放。

验证：

- 各语义动画可播放。
- 缺失单个动画不会影响整体运行。
- 动画结束后状态恢复正确。

## 16. 测试策略

### 16.1 单元测试

优先覆盖纯 Kotlin 模块：

- `AvatarStateMachineTest`
- `ControlLeaseManagerTest`
- `MoodSystemTest`
- `MoodTransitionQueueTest`
- `SoulFactorMapperTest`
- `MoodSemanticClassifierTest`
- `AnimationSchedulerTest`
- `ConversationOrchestratorTest`

重点验证：

- 状态转换是否可预测。
- 控制权优先级是否正确。
- 租约超时是否释放。
- 心情值阈值、冲突规则和状态回落事件是否正确。
- 低置信度 25% 增量是否正确。
- 长期持久化恢复是否正确。
- 动画缺失是否 NoOp。

### 16.2 集成验证

Android/PICO 相关验证：

- 模型加载。
- Stage 启动。
- HMD tracking。
- SpatialView attachment。
- PICO animation playback。

### 16.3 真机验证

真机验证内容：

- 舒适距离。
- 地面跟随是否自然。
- 少女是否遮挡视野。
- 空间气泡是否可读。
- 心情表现是否过于频繁或打断感过强。

## 17. 风险与缓解

| 风险 | 影响 | 缓解策略 |
| --- | --- | --- |
| `girl.glb` 无动画轨道 | 情绪和行走无法视觉验证 | 使用 NoOp 调度器先验证架构；补动画后只改 Catalog |
| GLB 尺寸/朝向不稳定 | 少女显示过大、过小或朝向错误 | 使用 visualRoot/bodyProxy 分离；记录初始旋转；必要时导入 Spatial Editor |
| 地面高度不准确 | 少女悬空或穿地 | 首版使用 Stage floor；后续接 plane/mesh grounding |
| 控制权设计过重 | 首版复杂度失控 | 只实现 Lease 核心字段和四级优先级，不做完整 ability system |
| LLM 输出未知 action | 角色行为断裂 | 首版 action 只允许 none；未知 action 明确 unsupported |
| 心情系统规则复杂 | 首版实现成本上升 | 先实现四类心情值和固定阈值，未来再扩展配置表 |
| 负面心情长期持久化 | 玩家重进应用仍处于负面状态 | 首版按用户决策不做离线结算；后续根据体验再加安全恢复 |
| 语义分类误判 | 心情值被错误调整 | 规则 + LLM 四分分类，低置信度只按 25% 增量生效 |
| PICO 动画 API 主线程约束 | 播放崩溃或无效 | AnimationPlayer 内部统一保证主线程调用 |

## 18. 决策项

以下决策项已经被后续 Unity Demo 方案替代：

1. ~~首版工程是否从 FullStage 模板初始化。~~ 当前使用既有 Unity 工程。
2. 首版是否只支持文本输入，不做语音/手势输入。
3. 首版 action 是否严格限制为 `none`，直到另开文档讨论 action 方案。
4. `girl.glb` 是否作为首版静态模型继续使用。
5. 是否接受首版使用 `NoOpAnimationPlayer` 先打通调度系统。
6. 是否创建 `.trae/development-log.md` 记录后续跨会话开发日志。

## 19. 初始 Definition of Done

首版完成定义：

- MateSoul 工程存在并可构建。
- Stage 启动后少女可见。
- 少女有地面状态机和跟随逻辑。
- 心情值系统、动画调度、控制权租约可测试。
- 当前无动画轨道时系统不崩溃。
- 对话回复和空间气泡可用。
- action 未定状态被明确隔离。
- 文档同步更新。

## 20. 参考资料

- MateFairy README：`/Users/bytedance/MateFairy/README.md`
- MateFairy 主场景：`/Users/bytedance/MateFairy/app/src/main/java/com/example/matefairy01/content/HomeStage.kt`
- MateFairy 行为系统：`/Users/bytedance/MateFairy/app/src/main/java/com/example/matefairy01/behavior/FairyBehaviorSystem.kt`
- MateFairy 动画模块：`/Users/bytedance/MateFairy/app/src/main/java/com/example/matefairy01/animation/AnimationModule.kt`
- MateFairy 动作锁：`/Users/bytedance/MateFairy/app/src/main/java/com/example/matefairy01/interaction/InteractionActionCore.kt`
- PICO Spatial SDK 文档目录：`/Users/bytedance/MateFairy/documentation/`
  （仅用于 MateFairy 历史路线，不是 MateSoul 当前 SDK 参考）
