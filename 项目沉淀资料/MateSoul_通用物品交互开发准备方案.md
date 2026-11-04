# MateSoul 通用物品交互开发准备方案

> 日期：2026-10-15
> 状态：开发准备评审稿，尚未替代 `MateSoul_Demo开发方案.md`
> 平台：Unity 2022.3.38f1 + PICO Unity Integration SDK + XRI + Animation Rigging + PhysX
> 参数标记：**已确定规则**表示应作为实现不变量；**现有工程值**表示代码当前值但仍可按验证结果调整；**设计初值**表示尚未经过 PICO 真机校准的起始值。

## 1. 方案结论

### 1.1 总体判断

整体方向合理，但不能直接在现有 `ItemReservation` 上增加几个布尔字段后开工。MateSoul
已有足够的动作、物理和物品基础，可以增量建设通用交互；缺口集中在物品控制权、
交互内容数据、通用动作族和失败回滚四个方面。

建议保留并扩展以下现有所有权边界：

1. `AvatarActionCoordinator` 继续作为角色动作生命周期、优先级、超时和打断的唯一入口。
2. `PhysicsBody` 继续作为 Rigidbody、受控移动和碰撞查询的唯一写入者。
3. `ItemRegistry` 升级为物品控制状态和原子租约的唯一事实源。
4. `ItemDefinition` 继续承载物品内容定义，但只引用可复用交互 Profile；局部抓取锚点和
   站位槽放在 Prefab 组件中。
5. Animator 提供动作语义、重心和整体姿态；Animation Rigging 只做有界末端修正。
6. UI、AI、自治和玩家 XRI 都必须通过同一个物品控制接口，不能各自判断 `IsHeld`。
7. 产品层 `Action` 表示一项完整生活事件，可以涉及多个物品和多个步骤；它仍作为一个
   `IAvatarAction` 进入现有 Coordinator，不另建平行角色控制器。

以下目标不能作为 P0 承诺：

- 一套站立动画通过 IK 覆盖地面、书架和任意朝向。
- 滚动、下落中的任意物品都能被自然截停并拾取。
- 电视、桌椅等大型物品与苹果、遥控器共享完全相同的动作和吸附参数。
- 在当前仅有 Idle/Walk 的生产 Animator 上直接得到完整自然取物效果。

P0 应先完成“稳定物品 -> 原子预约 -> 接近 -> 两类基础动作 -> 有界 IK -> 安全吸附
-> 持有/放回 -> 任意失败可恢复”的闭环，再扩大动作和物品覆盖。

### 1.2 工程事实与缺口

| 领域 | MateSoul 当前事实 | 结论 |
|---|---|---|
| 动作仲裁 | `AvatarActionCoordinator` 已支持通道租约、优先级、超时、取消和统一 `Dispose` | 直接扩展，不另建交互总状态机 |
| 物品登记 | `ItemRegistry` 可按实例 ID 查询，并有最小 `Occupant` 占用 | 必须升级；当前无 InteractionID、状态转移、超时和所有者语义 |
| 物品内容 | `ItemDefinition` 已有能力字符串、质量、尺寸、物理材质和单一 `approachOffset` | 可继续使用，但不足以描述多锚点、多动作和接近扇区 |
| 物品物理 | `PhysicsBody` 已支持复合碰撞体占位检查、路径离散采样、暂停和受限跟随 | 可复用；吸附还需形体 Sweep 和显式控制权 |
| 玩家抓取 | `PhysicsGrabInteractable` 通过 `PlayerGrabbable` 和 `PhysicsBody` 工作 | 必须在 XRI 选择前原子申请同一物品状态 |
| 删除/破碎 | `GameSession.TryRemoveItem` 当前会直接移除注册项；破碎也走该入口 | 必须统一查询物品控制状态，预约中默认拒绝 |
| 使用入口 | 电视、收音机按钮只检查组件可用和 `GameItem.IsHeld` | 必须改为查询统一控制状态 |
| 保存读档 | 临时预约未序列化；持有时保存最后稳定姿态 | 方向正确；保存前必须取消并完成回滚，读档永不恢复临时预约 |
| 动画 | 生产 `Avatar.controller` 当前只有 Idle/Walk，`applyRootMotion=false` | P0 必须新增正式取物 Clip 和交互层 |
| IK | `HandIKRig` 已证明 TwoBone IK、世界目标和权重平滑可用 | 抽取通用手臂求解能力，不能直接复用“牵手”业务状态 |
| 专用动作 | 坐、躺、保龄球已有站位、碰撞和分阶段 Action | 可参考生命周期；不得复制为每个物品一个专用状态机 |

### 1.3 数据管理形式

采用“ScriptableObject Profile + Prefab Authoring 组件 + 运行时权威记录”的组合，不选单一形式。

#### `ItemDefinition`：物品级静态事实

保留现有 ID、Prefab、质量、尺寸和物理配置，新增：

```text
interactionProfile
interactionAuthoringRequired
controlPolicy
```

`ItemDefinition` 不直接塞入大量每物品动作参数，避免苹果、杯子、碗重复保存相同规则。

#### `ItemInteractionProfile`：类别级可复用规则

ScriptableObject，按真实交互类别复用，例如：

```text
SmallRoundFood
HandledVessel
FlatDish
SlenderUtensil
RemoteDevice
SoftToy
TwoHandCarry
LargeFurniture
```

内容包括：

- 可供性列表：PickUp、PutDown、Eat、Use、Throw、Toggle、Carry。
- 单手/双手、首选手、握持姿势 ID。
- 允许的动作族、质量和尺寸门槛。
- 是否允许 IK、Root Warp、吸附、运动学稳定。
- 类别级吸附距离、旋转容差、净空膨胀量。
- 允许的物品控制状态和使用后状态。

#### `ItemInteractionAuthoring`：Prefab 局部几何

MonoBehaviour，保存必须随模型局部坐标变化的数据：

- 一个或多个 `GripAnchor`：局部 Pose、手别、握持姿势、允许的可供性。
- `ApproachSector`：物品局部方向扇区，不是单一世界点。
- 可选 `InteractionSlot`：精确站位、朝向、适用动作族和优先级。
- `UseAnchor`、`PlaceAnchor` 和物品操作轴。
- 交互净空代理：优先复用物理 Collider；必要时提供简化的 authored proxy。

抓取锚点的正向约定为“物品握持后手掌朝向”，上向约定为“掌背上向”。刀具的锚点必须
在刀柄，不能依赖包围盒中心推断。

#### `CharacterInteractionMotionSet`：角色动作内容

ScriptableObject，按角色和动作族配置：

- AnimationClip / Animator state ID。
- 动作族、手别、单/双手、兼容握持姿势。
- 角色局部目标高度和水平距离包络。
- 预期手部接触 Pose。
- 归一化接触窗口、IK 窗口、Root Warp 窗口。
- Root Motion 策略、退出姿势和取消安全点。

#### 接口边界

不为纯数据再造接口。只在确有替换点处使用：

```text
IItemAffordanceHandler
  CanPlan(context)
  BuildPlan(context)

IInteractionPathValidator
  CanMoveBody(body, from, to, exclusions, out failure)
```

`PickUp`、`Use`、`PutDown` 等 Handler 注册到现有 Action/Catalog 能力体系；中央协调器不得
出现按苹果、刀、杯子名称分支。

### 1.4 批量配置与调试

新增 Editor 工具，而不是运行时自动猜测全部锚点：

1. Profile 批量分配：按定义列表一次给同类物品分配 Profile。
2. 锚点模板：从 Collider bounds 生成候选锚点，仅作为待确认草稿。
3. Scene Gizmo：显示抓取坐标轴、接近扇区、站位槽、动作高度包络和吸附球。
4. 内容审计：扫描所有 `ItemDefinition`，报告缺 Profile、能力无 Handler、锚点在碰撞体外、
   刀柄方向错误、双手间距不合法、站位落不到 NavMesh 等问题。
5. 动画预览：用冷月 Prefab + 指定 Motion + IK 权重采样显示接触误差。
6. 批量报告：输出定义 ID、Profile、锚点数、动作覆盖和失败原因，不自动修改已人工确认数据。

运行时调试视图只在 Development Build 启用，显示 InteractionID、物品控制态、选中动作族、
手到锚点误差和最后失败码。

### 1.5 完整主流程修订

推荐主流程：

```text
空间索引查询
-> 便宜候选过滤
-> 目标与能力有效性检查
-> 目标稳定性/支撑面/粗净空检查
-> 生成少量站位候选并做完整 NavMesh 路径检查
-> 原子取得预约
-> 保存物理快照并稳定目标
-> 重新计算精确站位和支撑相对坐标
-> 寻路；移动支撑存在时持续更新目标
-> 最终站位、朝向、遮挡和净空复核
-> 选择动作族与 Motion
-> 计算自然接触误差
-> 计算有界 IK；必要时规划有界 Root Warp
-> 播放动作并在接触窗口前平滑应用 IK/Warp
-> 接触窗口内复核自然/IK/Warp 实际结果
-> 仅在仍失败且满足全部门禁时执行安全吸附
-> 原子转入 HeldByCharacter / InUseByCharacter / Completed
-> 更新物理、碰撞、父子关系和持有控制权
-> 正常结束或保持持有会话
-> 统一清理 InteractionContext
```

路径检查必须发生在预约前，但只对最多几个站位候选做完整路径计算。预约后仍需复核，因为
玩家和动态环境可能已改变目标周围空间。

## 2. 多物品 Action 编排

### 2.1 Action 的正式定义

本文后续使用以下术语，避免把产品 Action、动画和一次拾取混为一谈：

| 术语 | 定义 | 示例 |
|---|---|---|
| `Action` | 玩家可理解的一项完整交互事件 | 切水果、打开收音机、踢球 |
| `ActionRun` | 某次 Action 的运行实例，拥有唯一 `ActionRunID` | 本次“切苹果” |
| `ActionRole` | Action 需要的语义角色，不直接等同物品 ID | `fruit`、`cutter`、`plate`、`workspace` |
| `RoleBinding` | 本次运行中角色到具体物品实例的绑定 | `fruit -> apple-instance-17` |
| `ActionPlan` | 根据当前世界状态生成的可执行步骤 | 先送盘子，再送苹果，最后取刀 |
| `ActionStep` | 可复用、可取消、可补偿的原子步骤 | PickUp、PlaceAtSlot、UseTool |
| `ItemInteraction` | 单个步骤里的角色-物品操作 | 右手抓刀、把苹果放到桌面 |
| `Motion` | 一个步骤使用的动画/IK/Warp 内容 | MidReach、GroundBendPickup |

`Action` 是业务编排层，`ItemInteraction` 是执行层，`Motion` 是表现层。上层只能调用下层
公开能力，不能越层直接写 Animator、Transform 或 Rigidbody。

### 2.2 从 MateFairy 继承什么

MateFairy 提供了有价值的产品语义参考：

- 对话层只输出 action intent，不直接操作场景对象。
- Action 通过注册表扩展，一个 Action 对应独立运行实例。
- 运行实例可以持续多帧推进，而不是一次函数调用。
- 常驻行为在 Action 运行时让出控制权。
- Action 有开始、结束、中断和生命周期通知。
- 场景对象通过稳定 ID/索引解析，不让 LLM 持有引擎实体引用。

参考实现：

```text
/Users/bytedance/MateFairy/app/src/main/java/com/example/matefairy01/action/ActionRegistry.kt
/Users/bytedance/MateFairy/app/src/main/java/com/example/matefairy01/interaction/InteractionActionCore.kt
/Users/bytedance/MateFairy/app/src/main/java/com/example/matefairy01/interaction/InteractionActionSystem.kt
/Users/bytedance/MateFairy/app/src/main/java/com/example/matefairy01/interaction/ObjectInteractionActionControllers.kt
/Users/bytedance/MateFairy/app/src/main/java/com/example/matefairy01/interaction/PlayFootballActionController.kt
```

MateSoul 不复制以下 MateFairy 结构：

1. 对话 `ActionRegistry` 和场景 `InteractionActionRegistry` 两套注册表串联，容易接线遗漏。
2. 一个全局 `InteractionActionLockState` 锁死所有 Action，不能表达角色通道和多 NPC。
3. `objectIds: List<String>` 没有语义角色，无法知道哪个是刀、盘子或工作台。
4. `Map<String, Any>` 参数依靠运行时转换和默认值，错误不能在边界一次发现。
5. Action Controller 自己切刚体、写 Transform、恢复碰撞并清速度，恢复逻辑大量重复。
6. 携带对象只有 FollowComponent，没有物品级原子预约，玩家与 Action 可以竞争写状态。
7. `RUNNING/COMPLETED/FAILED` 状态过少，没有 Blocked、Cancelling、Compensating 和提交点。
8. 单个 Controller 手写大段 `when(state)`，复用 PickUp/Place/Use 时只能复制代码。
9. 失败没有补偿日志，Action 中途失败后只能依靠专用 cleanup 猜测恢复。
10. 缺少可测试的纯计划/状态层，许多问题只能进入 ECS/设备后发现。

这些问题解释了 MateFairy 后期反复增加 ActionRecoveryGrace、放下保护、行为归零和专用
恢复补丁的原因。MateSoul 必须先建立统一所有权、可补偿步骤和结构化准入，再增加具体 Action。

### 2.3 Action 架构

```text
UI / AI / Autonomy
        |
        v
ActionCatalog
  - ActionDescriptor
  - ICompositeActionPlanBuilder
        |
        v
ActionAdmissionService
  - Resolve Roles
  - Validate Requirements
  - Build Plan
        |
        v
AvatarActionCoordinator
        |
        v
PlannedInteractionAction : IAvatarAction
  - ActionRunContext
  - ActionStepRunner
  - Compensation Stack
  - Item Reservation Bundle
        |
        +--> AvatarMotor / Interaction Motion / IK
        +--> ItemRegistry / PhysicsBody
        +--> ActionResultTransaction
```

`ActionAdmissionService` 和 PlanBuilder 不写世界状态。`PlannedInteractionAction` 是唯一进入
Coordinator 的父 Action；内部 Step 不再次提交嵌套 `IAvatarAction`，否则会与父 Action
持有的 Locomotion/Body/Arm 通道死锁。

### 2.4 Action 定义形式

使用“ScriptableObject 描述 + C# PlanBuilder”混合形式，不建立任意节点图或通用规则语言。

`CompositeActionDescriptor` 保存内容和产品元数据：

```text
actionId
displayName
description
requiredRoles[]
optionalRoles[]
requiredChannels
timeout
interruptionPolicy
exposeToAI
uiIcon
```

`ActionRoleSpec`：

```text
roleId
requiredCapability
cardinality
selectionPolicy
reservationPolicy
handRequirement
distinctFromRoles[]
missingSystemMessage
```

`ICompositeActionPlanBuilder` 是代码扩展点：

```text
CanBuild(ActionPlanningContext, RoleBindings)
Build(ActionPlanningContext, RoleBindings) -> ActionPlan
```

每个真实复杂 Action 有一个 PlanBuilder，但只负责组合公共 Step，不自行实现导航、抓取、
IK、吸附和物理恢复。

公共 Step 首批包括：

```text
MoveToItem
PickUpItem
CarryItem
PlaceItemAtSlot
MoveToWorkspace
StabilizeItemWithHand
PlayInteractionMotion
UseToolOnItem
InvokeItemFunction
TransformItem
ReleaseItem
```

单物品“拿起苹果”同样可编译为短 ActionPlan，因此不会形成“简单物品一套执行器、复杂
Action 另一套执行器”。

### 2.5 ActionRun 上下文

```text
ActionRunContext
  actionRunId
  requestId
  actorId
  source
  descriptor
  bindings: roleId -> ItemHandle
  plan
  currentStepIndex
  activeStep
  itemLeases
  handOccupancy
  workspaceBinding
  compensationStack
  commitRevision
  startedAt / heartbeatAt / deadline
  result
```

`ItemHandle` 必须包含 itemId + generation；不能只缓存 GameObject 引用。`ActionRunID` 与单物品
交互使用的 InteractionID 统一：一次 ActionRun 的所有租约共享 Owner ID，每个 Step 另有
递增 `stepSequence` 用于拒绝旧回调。

### 2.6 角色解析与准入

Action 提交后先解析所有 required roles，再获得角色通道和物品硬预约：

1. 按能力查询每个角色的候选物品。
2. 过滤已删除、已持有、不稳定、不可达、缺少锚点或不兼容的实例。
3. 先解析约束最强的角色，再解析候选较多的角色。
4. 保证 `distinctFromRoles` 不绑定到同一实例。
5. 选择总代价最低的组合：路径、预计交互误差、当前占用和工作台槽位。
6. 生成完整计划并验证手容量、工作台容量和每一步的前后条件。
7. 只有计划可执行时才进入 Coordinator 和硬预约。

准入结果必须结构化：

```text
Ready
BlockedMissingRole
BlockedUnavailable
BlockedUnreachable
BlockedNoWorkspaceSlot
BlockedNoFeasiblePlan
```

例如没有餐桌：

```text
status = BlockedMissingRole
actionId = cut-fruit
missingRole = workspace
requiredCapability = preparation-surface
message = 需要先放置餐桌，冷月才能切水果。
```

此时不创建运行 Action、不取得任何预约，也不进入等待玩家的锁定状态。运行时只通过现有
`SystemNotificationPresenter` 弹出对应系统消息，本次请求随即结束。玩家补齐物品后重新
触发 Action；系统不自动打开物品目录，也不保留 pending request。

同理应区分：

- 没有刀：提示放置或寻找餐刀。
- 有刀但玩家正拿着：提示当前不可用，不误报“没有刀”。
- 有餐桌但没有空工作槽：提示清理桌面或重新摆放。
- 物品齐全但没有可达路径：提示重新摆放，而不是生成物品。

### 2.7 多物品预约策略

多物品 Action 不能从开始到结束无差别锁死房间所有相关物品，也不能完全按需单个预约后再
赌后续物品仍可用。采用三层策略：

1. `RoleBinding`：计划阶段绑定候选 ID，但不禁止玩家操作。
2. `HardReservation`：当前即将操作的物品在 Step 开始前原子预约。
3. `StagedForAction`：物品已运到工作台后继续由 ActionRun 持有，玩家不可拿走。

需要同一阶段同时保持不变的资源使用原子批量接口：

```text
TryReserveBundle(actionRunId, requests[], out bundle, out failure)
ReleaseBundle(actionRunId, reason)
```

批量预约规则：

- 主线程内先验证全部记录，再一次性提交；任何一个失败都不留下部分租约。
- 内部按 itemId + generation 排序，日志和释放顺序稳定，避免未来多线程/多 NPC 死锁。
- 不允许“拿着 A 的锁等待 B”；批量失败先释放本 Step 临时资源，再重规划。
- 同一个 ActionRun 可把 `Reserved -> Held -> StagedForAction` 转移，不释放给玩家后再预约。
- 已 Staged 的物品不因当前不在手中而释放。
- P0 工作台先预约整张餐桌；P1 可把桌面交互槽拆为独立 reservable slot，允许桌子其他区域
  继续被使用，但桌子本体移动仍会使 Action 取消或重规划。

### 2.8 手容量与工作台槽位

冷月默认只有：

```text
LeftHand: 0 或 1 个握持对象
RightHand: 0 或 1 个握持对象
```

双手物品同时占两个槽。刀具只能占兼容手的 `ToolGrip`。计划验证器必须拒绝超容量计划，
并自动插入 `PlaceItemAtSlot` 释放手，而不是让三个物品同时附着角色。

桌面 `ItemInteractionAuthoring` 增加可复用工作槽：

```text
PreparationWorkspace
  CuttingSlot
  PlateSlot
  ToolSafeSlot
  ActorStandSlot
  requiredClearance
```

这些是桌子局部坐标，桌子移动时可重新计算世界 Pose。槽位被真实障碍占据时不可使用。

### 2.9 “切水果”Action 实例

#### 角色需求

| Role | 能力 | 数量 | 约束 |
|---|---|---:|---|
| `fruit` | `cuttable-fruit` | 1 | 稳定、未破碎、未被持有 |
| `cutter` | `cutting-tool` | 1 | 必须有刀柄 GripAnchor |
| `plate` | `food-receiver` | 1 | 有足够输出槽/容量 |
| `workspace` | `preparation-surface` | 1 | 有 Cutting/Plate/Tool/Stand 槽及 NavMesh 路径 |

当前 MateSoul 苹果、刀、盘子、桌子资产都只有 `visit` 能力，因此**现状不能执行该
Action**；必须先补 Profile、锚点、桌面槽、切割 Motion 和苹果块输出资产。

#### 可执行计划

用户描述的是目标语义，不应逐字翻译为三件物品同时拿在手中。推荐计划：

```text
Resolve apple + knife + plate + table workspace
-> Validate complete plan and reserve workspace
-> Reserve plate
-> Approach / PickUp plate
-> Carry plate to table
-> Place plate at PlateSlot
-> Transfer plate to StagedForAction
-> Reserve apple
-> Approach / PickUp apple
-> Carry apple to table
-> Place apple at CuttingSlot
-> Transfer apple to StagedForAction
-> Reserve knife
-> Approach / PickUp knife by handle with right hand
-> Carry knife to table
-> Final bundle validation: table + plate + apple + knife
-> Align at ActorStandSlot
-> Left hand stabilizes apple; right hand holds knife
-> Play cutting Motion; validate each contact window
-> Commit apple -> apple pieces transaction
-> Move/place pieces into plate output slots
-> Put knife at ToolSafeSlot
-> Release table and knife
-> Transfer plate with pieces to AvailableSettled
-> Complete Action
```

如果后续加入托盘，可由 PlanBuilder 选择“一次搬运托盘”的成本更低变体；中央执行器无需修改。

### 2.10 结果事务与不可逆提交点

“切苹果”不仅移动物体，还会把一个物品转换成多个结果物。使用
`ActionResultTransaction`：

1. 切割动画开始前保持原苹果实体完整。
2. 在隐藏状态预创建苹果块，校验资源、碰撞体、PlateSlot 容量和有限 Pose。
3. 到达 `CutCommitPoint` 时原子更新权威状态：
   - 原苹果转为 `ConsumedByAction`。
   - 苹果块转为 `ProducedForAction`，Owner 仍为同一 ActionRun。
   - 视觉上切换原苹果与苹果块。
4. 苹果块全部放入盘子并通过占位检查后，提交结果并触发存档。
5. 提交失败时隐藏/销毁未提交苹果块，恢复完整苹果。

补偿边界：

- `CutCommitPoint` 前：可以完全回滚，恢复苹果、刀、盘子原始快照。
- `CutCommitPoint` 后：不再复活完整苹果；采用 forward recovery，保证已生成苹果块安全落盘、
  刀具放下和所有租约释放。
- 保存发生在提交前：先取消并回滚，再保存。
- 保存发生在提交后：先完成 forward recovery，再保存结果，不能写入半个事务。

### 2.11 ActionStep 契约与补偿

```text
Check(context)                 无副作用前置检查
Enter(context)                 取得本 Step 资源
Tick(context, dt)              推进
Commit(context)                发布本 Step 结果
Cancel(context, reason)        请求安全退出
Compensate(context)            撤销已提交的可逆副作用
Dispose()                      释放本 Step 局部资源
```

每个成功 Step 向补偿栈写入一条结构化补偿操作，例如：

```text
RestoreItemSnapshot(plate)
ReleaseItemLease(apple)
RestoreWorkspaceSlot(table, plate-slot)
DetachHeldItem(knife)
DestroyUncommittedOutputs(apple-pieces)
```

取消时按逆序执行。补偿操作幂等，并校验 ActionRunID、generation 和 stepSequence。
复杂 Action 的 Controller 不允许再写一个专用大 cleanup 方法尝试恢复所有状态。

### 2.12 Action 与角色通道

一个 `PlannedInteractionAction` 作为整体进入 `AvatarActionCoordinator`：

- `cut-fruit` 持有 Locomotion + FullBody + LeftArm + RightArm。
- Face、Gaze、Dialogue 是否可并行由 Descriptor 明确；切割接触窗口可临时要求 Gaze。
- Wander、随机拾物和其他身体 Action 不能插入。
- 玩家牵手等高优先级 Action 可以打断，父 Action 统一进入 Compensating。

不使用 MateFairy 的全局 Action lock。多个 NPC 可以各自运行 Action，但对共享物品仍通过
`ItemRegistry` 原子竞争。

### 2.13 AI 与 UI 边界

LLM 只选择已注册 Action ID 和可选的角色偏好，不生成执行步骤：

```json
{
  "id": "cut-fruit",
  "role_preferences": [
    { "role_id": "fruit", "definition_id": "apple" }
  ]
}
```

运行时决定具体实例、路径、顺序、手别和回滚。`role_id` 与 `definition_id` 必须来自
ActionCatalog/ItemCatalog 白名单，禁止恢复 MateFairy 的自由 `Map<String, Any>`。

展示回复前执行准入：

```text
LLM 提议 cut-fruit
-> Runtime TryPlan
-> Ready: 回复“好，我来切苹果”并提交 Action
-> Blocked: 回复/提示具体缺失项，不承诺已经开始
```

UI 订阅结构化 `ActionRunSnapshot`：

```text
actionId / actionRunId
state / currentStep
boundRoles
progress
blockReason
recovering
```

UI 不直接推进 Step，也不释放物品。

### 2.14 Action 观测指标

除单物品接触指标外，记录：

```text
action_admission_result
missing_role
plan_build_ms
role_candidate_count
plan_step_count
bundle_reservation_conflicts
role_rebind_count
step_duration_ms
step_retry_count
compensation_count/result
commit_revision
action_total_duration_ms
action_terminal_state
```

日志以 ActionRunID 串联，但核心存档只保存最终物品世界状态和已提交产品，不保存完整运行 trace。

## 3. 权威状态机

### 3.1 权威边界

系统包含两个层次，但不形成两个互相竞争的所有者：

- `ItemControlState`：由 `ItemRegistry` 唯一维护，回答“谁能控制这个物品”。
- `ItemInteractionPhase`：属于当前 `IAvatarAction` 的 `InteractionContext`，回答“这次动作走到哪一步”。

Action 只能通过 InteractionID 请求物品状态转移；不能直接写 `GameItem.IsHeld`、
`grab.enabled`、Rigidbody 或父子关系。

### 3.2 预约记录

建议运行时记录：

```text
ItemControlRecord
  itemId
  generation
  state
  ownerKind          Player / Character / System
  ownerId            玩家、NPC 或系统稳定 ID
  interactionId      Guid
  capabilityId
  priority
  acquiredFrame
  acquiredAt
  heartbeatAt
  expiresAt
  transitionVersion
```

`generation` 在物品删除和同 ID 重建后递增，防止旧租约复活。`transitionVersion` 每次状态
改变递增，便于拒绝乱序回调。物理快照不塞入该记录，而由当前 `InteractionContext` 持有：

```text
ItemPhysicsSnapshot
  worldPose
  parent / siblingIndex / localPose
  linearVelocity / angularVelocity / sleeping
  isKinematic / useGravity / constraints / collisionDetection
  collider enabled/isTrigger/layer values
  NavMeshObstacle enabled
  playerGrab enabled
  supportId / poseRelativeToSupport / supportGeneration
  collisionIgnorePairsAddedByThisInteraction
```

只保存本交互实际会修改的字段；质量、材质等不变字段不重复保存。

### 3.3 原子接口

```text
TryReserveItem(itemId, ownerId, interactionId, capabilityId, priority, out lease, out failure)
TryTransitionItem(itemId, interactionId, expectedState, nextState, out failure)
HeartbeatReservation(itemId, interactionId)
ReleaseReservation(itemId, interactionId, reason)
TransferReservation(itemId, interactionId, expectedState, nextState, newOwner)
CanPlayerOperate(itemId, operation, out denial)
```

规则：

1. 所有接口只在 Unity Session 主线程调用，一次调用内完成检查和写入。
2. 同帧竞争采用 first successful commit wins；失败方拿到明确 `AlreadyOwned`，不得覆盖。
3. NPC 调度器在提交前按行为优先级、路径成本、距离和稳定 tie-breaker 排序；活动租约不因另一个
   NPC 优先级更高而抢占，避免来回抖动。
4. 玩家抓取也先申请 `PlayerHeld`；一旦 NPC 已取得预约，玩家不能绕过。
5. `ReleaseReservation` 幂等；InteractionID 或 generation 不匹配时记录 `StaleCallback` 并无操作。
6. 删除、破碎、收纳、按钮、旋钮、XRI 抓取都查询该状态。

### 3.4 物品控制状态

| 状态 | 进入条件 | 允许退出 | 超时/打断 | 恢复动作 |
|---|---|---|---|---|
| `AvailableDynamic` | 自由刚体未稳定、滚动或下落 | 稳定后到 `AvailableSettled`；玩家原子抓取到 `PlayerHeld` | 无预约超时 | 保持真实物理 |
| `AvailableSettled` | 有支撑并持续稳定 | 玩家到 `PlayerHeld`；NPC 到 `ReservedByCharacter`；合法删除到 `Removed` | 无 | 无 |
| `PlayerHeld` | XRI 选择前原子取得 | 玩家释放到 `AvailableDynamic`；暂停/跟踪丢失取消抓取 | 跟踪丢失、暂停、删除请求拒绝 | 按现有 PhysicsBody 释放真实速度 |
| `ReservedByCharacter` | 预检查和路径通过，原子预约成功 | `StabilizedForCharacter`；失败到原可用态 | Action 取消、预约过期、目标异常 | 未改物理时直接释放 |
| `StabilizedForCharacter` | 物理快照完成，物品相对静态或相对支撑稳定 | `SnappingToCharacter`、`HeldByCharacter`、`InUseByCharacter`、`StagedForAction`、完成或回滚 | 导航/站位/遮挡失败、支持体变化过大 | 按快照恢复 |
| `SnappingToCharacter` | 接触窗口开放且吸附门禁通过 | `HeldByCharacter` 或回滚 | 路径中途阻塞、窗口关闭、旧回调 | 沿已验证反向路径回退到最近安全姿态 |
| `HeldByCharacter` | 同一 InteractionID 原子提交并绑定手部 | `InUseByCharacter`、`StagedForAction`、放置后可用态、丢弃后动态态 | 受击、动作切换、手部失效、场景切换 | 优先安全放置；否则回到最后安全姿态后释放 |
| `StagedForAction` | 已放到当前 Action 的有效工作槽，仍由 ActionRun 拥有 | 再次 Held、InUse、Consumed、完成后 Available | 工作台移动、槽被破坏、Action 取消 | 恢复原快照或最近安全支撑 Pose |
| `InUseByCharacter` | Held 或 Stabilized 状态按能力转入 | Held、完成态或回滚 | 功能对象失效、动画取消、超时 | 先撤销尚未提交的功能副作用，再恢复持有/原位 |
| `ConsumedByAction` | 结果事务已到不可逆提交点，源物品不再可见/可操作 | `Removed` | 只允许 forward recovery | 保证输出物完成并删除源记录 |
| `ProducedForAction` | 结果物已预创建或提交，Owner 为当前 ActionRun | Staged、Held、Available | 提交前失败可销毁；提交后只 forward recovery | 放入目标容器或最近安全 Pose |
| `Completing` | 放置或功能结果已提交，正在恢复物理 | `AvailableSettled`、`AvailableDynamic` 或 `Removed` | 只允许 SystemSafety | 完成最小一致性清理 |
| `Removed` | 仅 Available，或 SystemSafety 强制清理成功 | 无 | 无 | 终态；generation 失效 |

滚动或下落物品默认不能从 `AvailableDynamic` 进入 NPC 预约。P1 若增加截停能力，应作为单独
Affordance 和动作族，不能悄悄放宽普通拾取门禁。

### 3.5 交互动作阶段

| Phase | 进入条件 | 成功退出条件 | 建议超时 | 可打断 | 失败原因与恢复 |
|---|---|---|---:|---|---|
| `Searching` | 行为请求已获角色通道准入 | 找到候选 | 1.0 s | 全部 | 无候选 -> Replan/Failed，无物品状态变更 |
| `Preflight` | 候选存在 | 能力、稳定、支撑、粗净空通过 | 0.25 s | 全部 | Unsupported/Unstable/NoClearance -> 下个候选 |
| `PathCheck` | 有站位候选 | 至少一条完整路径 | 0.5 s | 全部 | NoStandSlot/NoPath -> 下个候选 |
| `Acquiring` | 确认准备执行 | 原子预约成功 | 1 frame | 全部 | AlreadyOwned/StaleTarget -> 下个候选或失败 |
| `Stabilizing` | 持有有效租约 | 快照完成且稳定模式生效 | 0.35 s | Safety、玩家高优先动作 | 物理异常 -> 回滚并释放 |
| `Planning` | 物品已稳定 | 精确站位、动作候选生成 | 0.25 s | 是 | TargetMoved/SupportInvalid -> 回滚或重规划 |
| `Navigating` | 完整路径有效 | 到达站位邻域 | 8 s | 是 | NoProgress/PathInvalid/TargetMoved -> 最多重规划一次，否则回滚 |
| `Aligning` | 到达邻域 | 位置、朝向、脚底和净空满足 | 1.5 s | 是 | AlignmentBlocked -> 回滚 |
| `SelectingMotion` | 最终目标 Pose 有效 | 选中自然/IK/Warp 可解的 Motion | 0.1 s | 是 | NoMotion/IKOutOfRange/WarpOutOfRange -> 回滚 |
| `Anticipation` | Animator 进入正确状态且 token 匹配 | 到达 Reach 窗口 | 由 Clip 定义 | 是，走取消安全点 | AnimatorMismatch/EventTimeout -> 回滚 |
| `Reaching` | 基础姿态开始接近 | 到达接触窗口 | 由 Clip 定义 | 仅 Safety/高优先级 | 遮挡或净空丢失 -> 退出动画并回滚 |
| `ContactWindow` | 当前 InteractionID 的窗口开放 | 自然、IK、Warp 或吸附成功 | 0.12-0.30 s | 仅 Safety | ContactMiss/SnapBlocked -> 退出并回滚 |
| `Snapping` | 全部吸附门禁通过 | 物品达到手部锚点 | 0.06-0.25 s | Safety | PathBlocked/CommitRejected -> 反向回滚 |
| `Committing` | 最终 Pose 安全 | 原子转为 Held/Using/Completed，物理绑定成功 | 1 frame | 仅 Safety | 转移失败 -> 补偿到 Reserved 并回滚 |
| `HoldingOrUsing` | 控制权已转移 | 使用完成、放置或丢弃 | 由语义动作定义 | 是 | 由持有/使用策略安全退出，不释放成玩家可用中间态 |
| `Cancelling` | 收到取消或失败 | 动画到安全退出点 | 现有默认 3 s | Safety 可强制 | 超时 -> 强制进入 RollingBack |
| `RollingBack` | 本次改过物理或状态 | 物理、碰撞、父子关系恢复且租约释放 | 1.0 s | 仅 Safety | 原位被占 -> 使用最近已验证安全 Pose 后释放 |
| `Succeeded/Failed` | 清理完成 | 终态 | 无 | 无 | 只发布结果，不再持有资源 |

### 3.6 失败原因

使用枚举和结构化上下文，不依赖自由文本：

```text
TargetMissing, UnsupportedAffordance, AlreadyOwned, StaleGeneration,
UnstableTarget, NoSupport, NoApproachSlot, NoCompletePath, NavigationBlocked,
TargetMoved, SupportMoved, ClearanceLost, Occluded, NoCompatibleMotion,
IKOutOfRange, WarpOutOfRange, ContactMiss, SnapNotAllowed, SnapPathBlocked,
ContactWindowClosed, AnimatorMismatch, CommitRejected, Interrupted, Timeout,
OwnerLost, SceneChanging, SaveRequested, PhysicsRestoreFailed, StaleCallback,
MissingRequiredRole, RoleUnavailable, RoleBindingStale, NoFeasiblePlan,
HandCapacityExceeded, WorkspaceSlotUnavailable, BundleAcquireFailed,
StagedItemLost, ResultPrepareFailed, ResultCommitFailed, CompensationFailed
```

每个失败结果至少记录 ActionRunID/InteractionID、action、role、item/profile、当前 Step、
动作族、失败码和是否使用吸附。

### 3.7 超时、心跳和看门狗

- Action 的业务超时继续由 `AvatarActionCoordinator` 管理。
- 预约 `expiresAt` 是防永久锁死的安全后门，不替代 Action 超时。
- Running/Cancelling Action 每 `heartbeatInterval` 更新预约。
- `ItemRegistry` 看门狗低频检查：Owner 不存在、InteractionID 不在活动 Action/持有控制器中、
  或超过绝对截止时间时，进入统一恢复。
- `HeldByCharacter` 不按普通预约时长自动掉落，但必须有活动 `CharacterHeldItemController`
  Owner；Owner 丢失时执行安全放置或释放。
- 场景销毁时先停止 Action，再恢复/释放物品，最后销毁 Registry。

### 3.8 多物品 Action 状态

| 状态 | 进入条件 | 退出条件 | 失败处理 |
|---|---|---|---|
| `Requested` | UI/AI/自治提交 Action ID | 进入 Resolving | 未知 Action 直接 Rejected |
| `Resolving` | Descriptor 存在 | Required Roles 全部绑定 | 缺角色进入 Blocked，不加锁 |
| `Blocked` | 缺物品、工作台、槽位或路径 | 用户修复后重新提交 | 终态；弹出对应系统消息 |
| `Planning` | Roles 已绑定 | 手容量、步骤、路径和补偿计划全部有效 | 无可行计划进入 Rejected |
| `Queued` | Plan 有效，等待角色通道 | Coordinator 获得通道 | 可取消，无物品硬预约 |
| `Acquiring` | 父 Action 已启动 | 当前 Step/Bundle 原子预约成功 | 失败可重绑一次，否则 Compensating |
| `Executing` | 当前 Step 已 Enter | Step Commit，推进下一 Step | 可重试、重规划或 Compensating |
| `CommittingResult` | 到达不可逆语义提交点 | 输入/输出状态原子提交 | 提交前完整回滚；提交后 forward recovery |
| `Cancelling` | 被请求、抢占、超时或目标丢失 | 当前 Motion 到安全退出点 | 到期强制 Compensating |
| `Compensating` | 有已提交可逆副作用 | 补偿栈清空、租约释放 | 单项异常继续其余补偿，最终不得保留锁 |
| `ForwardRecovering` | 不可逆提交后发生异常 | 输出物和刀具进入安全状态 | 不恢复已消费源物品 |
| `Succeeded` | 全部步骤和清理完成 | 终态 | 发布结果并保存 |
| `Failed/Cancelled` | 补偿和清理完成 | 终态 | 发布结构化原因 |

### 3.9 Action 角色生命周期

```text
Unbound
-> Bound                 只有计划绑定，不阻止玩家
-> Reserved              即将操作，拥有硬预约
-> Held                  在冷月手中
-> Staged                位于工作槽，仍归 ActionRun
-> Consumed / Produced   结果事务状态
-> Released              恢复普通物品控制
```

同一角色可以在 Held 与 Staged 间多次转移，但必须始终由同一 ActionRunID 授权。

## 7. 动画与物理方案

### 7.1 基础动作族

按“身体策略”组织，不按物品名组织：

| 动作族 | 主要高度范围 | 身体语义 | P0/P1/P2 |
|---|---|---|---|
| `GroundSquatPickup` | 地面至小腿下部 | 下蹲、重心下降、保持背部自然 | P1 |
| `GroundBendPickup` | 地面至膝下 | 弯腰屈膝、单手取物 | P0 |
| `LowReach` | 膝至腰下 | 小幅屈膝前伸 | P1 |
| `MidReach` | 腰至胸 | 站立单手前伸 | P0 |
| `HighReach` | 胸至头顶附近 | 抬臂并轻微伸展躯干 | P1 |
| `TwoHandPickup` | 地面/台面 | 对称或主辅手拿取 | P1 |
| `LargeCarry` | 由专用槽定义 | 双手、重心后移、限制移动 | P2 |

每族可有左/右、近/远和握持姿势变体。动作选择先按手数、质量、净空和目标高度排除，
再按预计自然接触误差、IK 使用量、Root Warp 使用量排序。

复杂 Action 不制作一条覆盖全流程的超长动画。`cut-fruit` 组合：

```text
通用 PickUp / Carry / Place
-> StabilizeFruit 左手稳定动作
-> CutWithKnife 右手切割动作，可按允许次数循环
-> TransferFoodToPlate
-> PlaceToolSafely
```

ActionPlan 决定播放顺序；MotionSet 只描述单段身体语义和接触窗口。同一个
`CutWithKnife` 可复用到兼容尺寸与锚点的水果，不按苹果名称绑定。

### 7.2 动画接触事件

不把 Unity `AnimationEvent` 字符串回调作为权威提交点。`CharacterInteractionMotionSet`
保存归一化窗口，运行时 `InteractionAnimationEventRelay` 根据 Animator state hash、
normalizedTime、循环序号和当前 InteractionID 发出一次性事件：

```text
AnticipationBegin
IKBlendIn
WarpBegin
ContactWindowOpen
GripCommitPoint
ContactWindowClose
IKBlendOut
ReleaseCommitPoint
ToolContactPulse
SemanticCommitPoint
MotionComplete
```

所有事件携带当前 InteractionID 和单调 sequence；重复、乱序、旧 ID 直接忽略。
AnimationEvent 可供动画师预览标记，但运行时仍由 Motion 元数据复核。
`ToolContactPulse` 可在一个切割 Motion 中出现多次；只有 ActionPlan 指定的
`SemanticCommitPoint` 能触发结果事务，普通刀具接触不能删除苹果或生成产物。

### 7.3 自然接触、IK 与 Warp

执行顺序不是在接触瞬间才逐级尝试，而是在播放前完成可解性规划：

1. 用 Motion 的 authored contact pose 预测自然手部误差。
2. 误差在自然阈值内：IK 目标权重保持 0 或极低。
3. 超出自然阈值但在舒适 IK 包络内：计算所需 IK 权重并在接触前渐入。
4. IK 仍不足但站位微调可解：规划有限 Root Warp，在 anticipation/reach 前半段完成。
5. 接触窗口内用实际骨骼 Pose 复核；仍未接触时才评估吸附。

IK 使用 Animation Rigging 的 TwoBone IK。约束：

- 目标为世界空间 Transform，不挂在骨架层级下。
- 权重使用 SmoothStep 渐入渐出。
- 手腕旋转和肘部 Hint 有舒适限制。
- 超过硬伸展比时 Motion 直接判为不可解，不让肩膀和手臂被拉长。
- 双手物品以主手解算物品 Pose，辅手解算到第二锚点，不能两只手各自拉动物品。

### 7.4 Root Motion / Motion Warping

当前 `AvatarMotor` 明确关闭 Root Motion。建议分阶段：

- P0：使用 in-place 交互动画；`AvatarMotor.Dock` 完成播放前定位，不启用 Root Motion。
- P1：增加 `InteractionRootDriver`，由它读取 `Animator.deltaPosition/deltaRotation`，
  再通过 `AvatarMotor/PhysicsBody` 提交碰撞受限的根运动。
- Warp 只修正动作开始到接触前的水平位移和 yaw；不直接写 Transform，不修正垂直根高度，
  不在接触后追赶移动目标。
- Warp 超出上限时取消或重规划，不能隐式扩大到“看起来能碰到”为止。

### 7.5 物理生命周期

| 时点 | Rigidbody/PhysicsBody | 碰撞 | XRI/功能入口 |
|---|---|---|---|
| 候选与预检查 | 保持真实动态状态 | 不变 | 不变 |
| 预约成功 | 尚不改物理，先完成快照 | 不变 | 统一状态立即拒绝玩家操作 |
| 稳定普通静止物品 | 进入由 InteractionID 拥有的 Kinematic Stabilized 模式，速度清零但保留快照 | 保持实体碰撞 | XRI、使用、删除、破碎拒绝 |
| 稳定移动支撑上的物品 | 记录支撑 ID 和局部 Pose；FixedUpdate 用 MovePosition/MoveRotation 跟随支撑 | 保持实体碰撞并持续占位检查 | 同上 |
| 吸附 | 运动学受控；每步只提交下一个已验证 Pose | 保持查询形体；只忽略本物品与角色手臂的指定碰撞对 | 同上 |
| 附着/持有 | 转入 `HeldByCharacter` 后绑定 hand grip；Rigidbody 运动学 | 忽略角色自身碰撞；保留环境检测和必要 Collider | 玩家操作仍拒绝 |
| 工作台暂存 | 转入 `StagedForAction`，按工作槽局部 Pose 运动学稳定 | 保持实体碰撞并验证槽位 | 玩家操作拒绝；同 Action 可再次拿取 |
| 结果预创建 | 输出物禁用或不参与碰撞，源物保持有效 | 不进入玩家查询 | 仅结果事务可见 |
| 结果提交 | 源物转 Consumed，输出物转 Produced/Staged | 输出物启用目标碰撞配置 | 仍由 ActionRun 持有直到落盘 |
| 放置 | 先验证目标 Pose、支撑和脱离路径，再解除父子/跟随 | 恢复交互新增的 IgnoreCollision 对 | 最后原子转为 Available |
| 丢弃 | 恢复动态刚体并施加动作定义的有限释放速度 | 恢复碰撞 | 转为 `AvailableDynamic` |
| 取消 | 按快照或最近安全 Pose 恢复 | 精确恢复本交互改过的字段和碰撞对 | 最后释放租约 |

不要直接复用全局 `SetPaused` 作为预约稳定，因为暂停还承担应用生命周期速度保存。应给
`PhysicsBody` 增加带 InteractionID 的受控模式，拒绝 Follow、Contact、Pause 和
Interaction 同时占有。

### 7.6 移动支撑物

1. 支撑检测返回静态房间表面或具体 `PhysicsBody`。
2. 快照记录物品相对支撑 Pose，不记录一次性的世界 Pose 作为唯一目标。
3. 寻路阶段持续从相对 Pose 重算抓取锚点和站位槽；支撑移动超过重规划阈值时重算路径。
4. 进入最终对齐后，若支撑仍持续快速移动，则取消；不让冷月追着桌面上的杯子播放取物动画。
5. 稳定物品通过运动学跟随支撑，不把物品 Transform 直接设为支撑子节点。
6. 回滚时优先回到支撑当前 Pose 下的原局部位置；若被占用，则退到记录的最近安全 Pose。
7. 支撑本身被删除、预约或失去有效 generation 时，目标交互立即取消。

### 7.7 抵近吸附

#### 类别初值

以下均为**设计初值**，距离指实际手部抓取锚点与物品 GripAnchor 的直线距离：

| 物品类别 | 示例 | 建议距离 | 合法范围 | 最大初始角误差 | 说明 |
|---|---|---:|---:|---:|---|
| 精细/危险细长 | 刀、筷子 | 0.035 m | 0.020-0.060 m | 8° | 只吸刀柄/握持端，路径和旋转最严格 |
| 普通细长/设备 | 勺、遥控器 | 0.050 m | 0.025-0.080 m | 12° | 需要明确长轴方向 |
| 小型圆润物 | 苹果、曲奇 | 0.070 m | 0.035-0.110 m | 20° | 可容忍较大旋转，但不能穿过支撑面 |
| 杯盘容器 | 杯、碗、盘 | 0.050 m | 0.025-0.080 m | 10° | 姿态敏感，盘面和杯口方向必须保持 |
| 柔软物 | 玩偶 | 0.100 m | 0.050-0.150 m | 25° | 可放宽视觉误差，仍使用刚体代理检查 |
| 中型双手物 | 小箱体等 | 0.100 m | 0.050-0.150 m | 12° | 两只手都须在各自门槛内 |
| 大型物品 | 桌、电视、椅子 | P0 禁止吸附 | 0-0.100 m | 6° | P2 专用搬运动作验证后再开放 |

#### 时长和插值

- 基础时长设计初值 `0.12 s`，合法范围 `0.06-0.25 s`。
- 实际时长取基础时长、`distance/maxLinearSpeed`、`angle/maxAngularSpeed` 三者最大值。
- 位置用三次 Hermite SmoothStep，旋转用同一进度的 Quaternion Slerp，确保起止速度为零。
- 位置和旋转必须同一时刻完成，不能先把刀柄移到手里再突然翻转刀刃。

#### 路径安全

不能使用单根射线。`IInteractionPathValidator` 执行：

1. 对 Sphere/Box/Capsule Collider 使用对应 NonAlloc ShapeCast。
2. 复合碰撞体逐形体检查，并排除自身、角色允许忽略的手臂碰撞和当前支撑接触面。
3. 旋转段按最大角步长采样 `CanOccupy/ComputePenetration`。
4. 凸 MeshCollider 使用 authored 简化代理；没有代理时使用保守 oriented bounds，不允许退化为射线。
5. 开始前验证整条路径；吸附中每个 FixedUpdate 再验证下一段。
6. 查询缓冲区饱和视为失败，不截断结果后继续。

#### 中途失败和回滚

- 保存每个已通过验证的吸附 Pose。
- 路径中途阻塞时停在最后安全 Pose，不继续穿透。
- 优先沿已验证 Pose 反向平滑返回原支撑相对 Pose。
- 原位被新障碍占用时，停在最近安全 Pose，恢复动态刚体并释放；不得为“回到原位”穿墙。
- 任何恢复异常最终都必须清除预约和玩家禁用状态；允许物品落下，不允许永久锁死。

### 7.8 吸附质量统计

每次进入接触窗口只记录一种最终模式：

```text
Natural
IK
WarpAndIK
Snap
Failed
CancelledBeforeContact
```

聚合维度：

- Item Profile、Motion Family、左右手。
- 目标高度桶、水平距离桶、接近方向桶。
- 支撑类型、是否移动支撑、是否遮挡。
- 最终接触误差、IK 权重峰值、Warp 位移/角度、Snap 距离/时长。
- 失败码和阶段。

核心指标：

```text
natural_contact_rate
ik_assist_rate
warp_assist_rate
snap_rate
snap_path_block_rate
contact_failure_rate
rollback_failure_rate
reservation_expiry_count
orphan_recovery_count
```

吸附率不能和“接触窗口前取消”混在同一分母。开发期按 Profile + Motion 聚合，不在核心存档
保存逐帧轨迹。设计告警初值：同一桶样本不少于 50 次且 `snap_rate > 15%`，优先检查动画
接触 Pose、站位槽和 IK 包络；这是调参信号，不是最终质量标准。

## 8. 参数表

### 8.1 候选、稳定与导航

| 参数 | 单位 | 状态/初值 | 合法范围 | 调高影响 | 调低影响 | 校准信号 |
|---|---:|---:|---:|---|---|---|
| `candidateRadius` | m | 设计初值 4.0 | 1.0-8.0 | 候选更多、查询和路径成本增加 | 可能忽略可见目标 | 目标漏选率、候选数 |
| `candidateRefreshInterval` | s | 设计初值 0.50 | 0.20-2.00 | 更新更慢、成本低 | 响应快、成本高 | 目标移动后的发现延迟 |
| `maxCandidatesAfterCheapFilter` | 个 | 设计初值 8 | 3-24 | 覆盖高、路径预算高 | 可能错过次优目标 | 无路径后回退次数 |
| `maxPathCandidates` | 个 | 设计初值 3 | 1-6 | 可达率高、NavMesh 成本高 | 失败更快 | 每次行为路径计算数 |
| `settledLinearSpeed` | m/s | 设计初值 0.05 | 0.01-0.20 | 更容易把滑动物品当稳定 | 等待更久 | 预约后目标位移 |
| `settledAngularSpeed` | °/s | 设计初值 10 | 2-30 | 更容易接收旋转物 | 等待更久 | 稳定后角度漂移 |
| `settledDwell` | s | 设计初值 0.25 | 0.10-0.80 | 误判少、响应慢 | 响应快、误判多 | 预约后立即回滚率 |
| `supportProbeDistance` | m | 现有工程值 0.13 | 0.05-0.25 | 容易把悬空物判为有支撑 | 薄间隙可能漏判 | 下落物误预约/支撑漏判 |
| `standPositionTolerance` | m | 设计初值 0.03 | 0.01-0.08 | 更易进入动画、接触误差增大 | 对齐时间增加 | Warp/Snap 率 |
| `standYawTolerance` | ° | 设计初值 3 | 1-10 | 进入动作快、横向误差增大 | 对齐更慢 | 侧向 IK、脚滑 |
| `alignTimeout` | s | 设计初值 1.5 | 0.5-3.0 | 容忍阻滞但等待长 | 更快失败 | AlignmentBlocked 率 |
| `repathInterval` | s | 现有工程值 1.0 | 0.25-2.0 | 路径更新慢、成本低 | 动态响应快、成本高 | 路径失效率、NavMesh 耗时 |
| `navigationTimeout` | s | 设计初值 8 | 3-20 | 可走更远但锁更久 | 远目标易失败 | 路径长度/超时分布 |
| `targetMoveReplanDistance` | m | 设计初值 0.08 | 0.03-0.20 | 少重规划、末端误差大 | 频繁重规划 | 重规划次数、Snap 率 |
| `targetMoveReplanAngle` | ° | 设计初值 6 | 2-20 | 少重规划、握持方向误差大 | 频繁重规划 | 旋转误差、重规划次数 |

### 8.2 预约与恢复

| 参数 | 单位 | 状态/初值 | 合法范围 | 调高影响 | 调低影响 | 校准信号 |
|---|---:|---:|---:|---|---|---|
| `actionTimeout` | s | 现有默认 30 | 5-60 | 锁持有更久 | 长路径/动作易误杀 | Action timeout 分布 |
| `exitTimeout` | s | 现有默认 3 | 0.5-5 | 允许自然退出 | 更快强制回滚 | 退出超时率 |
| `reservationHeartbeat` | s | 设计初值 0.5 | 0.1-1.0 | 失主发现慢、调用少 | 调用多、发现快 | orphan 恢复延迟 |
| `reservationStaleGrace` | s | 设计初值 2.0 | 1.0-5.0 | 容忍卡顿、解锁慢 | 暂时掉帧可能误释放 | 看门狗误触发 |
| `watchdogInterval` | s | 设计初值 1.0 | 0.25-2.0 | 成本低、恢复慢 | 恢复快、扫描多 | orphan 解锁时间 |
| `absoluteReservationDeadline` | s | 已确定规则：Action timeout + exit timeout + 2 | 由 Action 派生 | 不单独调 | 不单独调 | 永久锁计数必须为 0 |
| `rollbackTimeout` | s | 设计初值 1.0 | 0.25-2.0 | 更努力回原位 | 更快在最近安全点释放 | 回滚失败率 |
| `maxReplansPerInteraction` | 次 | 设计初值 1 | 0-3 | 成功率高、锁更久 | 更快失败 | 平均重规划次数 |
| `lockedHintCooldown` | s | 设计初值 1.0 | 0.3-3.0 | 提示不扰人 | 玩家更快获得反馈 | 连点提示频率 |

### 8.3 动画、IK 与 Warp

| 参数 | 单位 | 状态/初值 | 合法范围 | 调高影响 | 调低影响 | 校准信号 |
|---|---:|---:|---:|---|---|---|
| `naturalContactPositionTolerance` | m | 设计初值 0.025 | 0.010-0.050 | 更多算自然接触 | 更多启用 IK | 手掌可见悬空 |
| `naturalContactAngleTolerance` | ° | 设计初值 8 | 3-15 | 更多算自然接触 | 姿态更严格 | 杯口/刀柄方向 |
| `ikSoftReachRatio` | 臂长比例 | 设计初值 0.84，来自现有牵手值 | 0.65-0.92 | 允许更伸直 | 更早换站位/Warp | 肘锁死、肩抬升 |
| `ikHardReachRatio` | 臂长比例 | 设计初值 0.97，来自现有牵手值 | 0.85-0.985 | 覆盖更远但更僵硬 | 失败/重规划更多 | 手臂拉直帧占比 |
| `ikBlendIn` | s | 设计初值 0.15，现有牵手值 | 0.08-0.35 | 更慢更柔和 | 更快但可能抽动 | 接触前误差、关节速度 |
| `ikBlendOut` | s | 设计初值 0.22，现有牵手值 | 0.10-0.40 | 回姿慢 | 弹回明显 | 手臂回弹观感 |
| `maxWarpDistance` | m | 设计初值 0.08 | 0.03-0.15 | 覆盖更强、脚滑风险高 | 重规划/失败更多 | 脚滑和 Snap 率 |
| `maxWarpYaw` | ° | 设计初值 8 | 3-15 | 朝向适配强 | 站位要求高 | 足部旋转滑动 |
| `warpCompleteBeforeContact` | 归一化时间 | 已确定规则：是 | 0-1 | 不适用 | 不适用 | 接触后根追赶必须为 0 |
| `contactWindowDuration` | s | 由每个 Motion 配置，初值 0.18 | 0.10-0.35 | 更易命中但动作停滞感强 | 时序更严格 | ContactWindowClosed 率 |

### 8.4 吸附与碰撞

| 参数 | 单位 | 状态/初值 | 合法范围 | 调高影响 | 调低影响 | 校准信号 |
|---|---:|---:|---:|---|---|---|
| `snapBaseDuration` | s | 设计初值 0.12 | 0.06-0.25 | 移动更可见但柔和 | 更像瞬移 | 峰值屏幕速度 |
| `snapMaxLinearSpeed` | m/s | 设计初值 0.8 | 0.3-1.2 | 更快、更突兀 | 可能错过接触窗口 | 实际吸附时长 |
| `snapMaxAngularSpeed` | °/s | 设计初值 360 | 120-540 | 方向修正快 | 动作拖长 | 刀/杯旋转观感 |
| `snapTranslationStep` | m | 设计初值 0.01 | 0.005-0.02 | 此项调高会减少查询但增大漏检风险 | 更安全、查询更多 | Sweep 漏检/耗时 |
| `snapRotationStep` | ° | 设计初值 2 | 1-5 | 查询少、旋转漏检风险高 | 查询更多 | 旋转穿模 |
| `clearanceInflation` | m | 设计初值 0.015 | 0.005-0.04 | 更保守、失败多 | 擦碰/穿模风险高 | SnapBlocked 与穿模 |
| `maxPenetrationTolerance` | m | 现有占位默认 0.012；吸附初值 0.005 | 0.001-0.012 | 容忍穿插 | 误报阻塞 | 可见穿插、假阻塞 |
| `snapRateWarning` | % | 设计初值 15 | 5-30 | 告警更宽松 | 更早要求改动画 | 每桶样本 >= 50 |
| `minimumMetricSamples` | 次 | 设计初值 50 | 20-200 | 结论稳定但反馈慢 | 易受偶然样本影响 | 置信区间 |

### 8.5 物品与动作分类

| 参数 | 单位 | 状态/初值 | 合法范围 | 调高影响 | 调低影响 | 校准信号 |
|---|---:|---:|---:|---|---|---|
| `oneHandMassLimit` | kg | 设计初值 1.5 | 0.5-3.0 | 更多物品可单手但不自然 | 更早要求双手 | 手腕/身体姿态观感 |
| `twoHandMassLimit` | kg | 设计初值 8.0 | 3.0-12.0 | 可搬更重物 | 更多物品拒绝 | 移动稳定和重心 |
| `largeItemMinExtent` | m | 设计初值 0.45 | 0.30-0.80 | 更少物品判大型 | 更多进入大型策略 | 碰撞净空和双手需求 |
| `groundBandMaxHeight` | m | 设计初值 0.22 | 0.12-0.35 | 更多走地面动作 | 更多走低位动作 | 膝/背姿态 |
| `bendBandMaxHeight` | m | 设计初值 0.42 | 0.30-0.60 | 弯腰覆盖高 | 更早切 LowReach | 动作族切换突变 |
| `lowBandMaxHeight` | m | 设计初值 0.70 | 0.55-0.90 | 低位动作覆盖高 | MidReach 增多 | 手臂自然度 |
| `midBandMaxHeight` | m | 设计初值 1.20 | 1.00-1.40 | MidReach 过度抬臂 | HighReach 增多 | 肩部抬升 |
| `highBandMaxHeight` | m | 设计初值 1.65 | 1.40-1.85 | 可尝试更高目标 | 高架目标拒绝 | 踮脚/拉伸观感 |

高度以冷月脚底所在支撑面为零点。各区间允许重叠，最终按 Motion 实际包络和预计修正量选择，
不能只用硬阈值跳变。

### 8.6 多物品 Action

| 参数 | 单位 | 状态/初值 | 合法范围 | 调高影响 | 调低影响 | 校准信号 |
|---|---:|---:|---:|---|---|---|
| `maxRoleCandidates` | 个/角色 | 设计初值 4 | 1-8 | 计划覆盖高、组合数增加 | 容易错过可行组合 | RoleUnavailable、计划耗时 |
| `maxPlanCombinations` | 组合 | 设计初值 64 | 8-256 | 更可能找到全局较优解 | 更快但可能次优/失败 | plan_build_ms、NoFeasiblePlan |
| `planBuildTimeout` | s | 设计初值 0.50 | 0.10-2.00 | 容忍复杂计划 | 更快失败 | 计划超时率 |
| `maxRolesPerAction` | 个 | 设计初值 8 | 2-16 | 支持更复杂配方 | 限制内容规模 | 每 Action 角色数分布 |
| `maxStepsPerAction` | 个 | 设计初值 32 | 4-64 | 支持长流程 | 复杂 Action 被拒绝 | Step 数、总时长 |
| `compositeActionTimeout` | s | 设计初值 90 | 20-180 | 长流程成功率高、占用更久 | 多趟搬运易超时 | Action 总时长 P95 |
| `defaultStepTimeout` | s | 设计初值 15 | 3-30 | 单步容错高 | 阻塞更快失败 | Step timeout 分布 |
| `maxRoleRebinds` | 次/角色 | 设计初值 1 | 0-2 | 抵抗玩家移动，行为更长 | 更快失败 | rebind 成功率 |
| `hardReservationLookahead` | Step | 设计初值 1 | 1-3 | 后续资源更稳定、锁更多 | 玩家干扰概率增加 | 冲突率、平均锁时长 |
| `workspaceMoveTolerance` | m | 设计初值 0.03 | 0.01-0.10 | 容忍桌子移动但接触误差高 | Action 更易取消 | WorkspaceMoved 率 |
| `workspaceRotateTolerance` | ° | 设计初值 3 | 1-10 | 容忍旋转 | 切割对齐更严格 | Warp/Snap、取消率 |
| `workspaceSlotPositionTolerance` | m | 设计初值 0.02 | 0.005-0.05 | 更易放入槽 | 苹果/盘子摆放不准 | 槽位占用和接触误差 |
| `maxCompensationAttempts` | 次/操作 | 设计初值 2 | 1-3 | 恢复成功率高 | 更快走最近安全点 | CompensationFailed |
| `actorHandCapacity` | 个 | 已确定规则：2 | 2 | 不可调 | 不可调 | 超容量计划必须为 0 |

`hardReservationLookahead` 不是一次把未来所有角色都锁住；`StagedForAction` 和结果提交所需
角色例外，因为它们已经进入当前 Action 的一致性边界。

## 9. 异常与边界场景

| 场景 | 检测与裁决 | 恢复/反馈 |
|---|---|---|
| 玩家与冷月同帧争抢 | 玩家 XRI 和 NPC 都调用同一原子状态转移；first commit wins，记录 same-frame contention | 失败方不改物理；玩家失败时显示锁图标和“冷月正在使用”提示 |
| 目标正在滚动或下落 | `AvailableDynamic`、速度/角速度/支撑门禁不通过 | 不预约；等待稳定或换目标。P0 不瞬间冻结空中物品 |
| 目标位于移动桌面/平台 | 记录 support ID、generation 和局部 Pose；持续重算世界目标 | 慢速移动可跟随和重规划；最终对齐阶段过快则回滚 |
| 寻路途中角色被打断 | Coordinator 进入 Cancelling，预约继续由旧 Action 持有直到退出完成 | 停止 Motor，恢复物品快照，释放租约，再让高优先动作取得通道 |
| 动画中物品被销毁 | 普通删除入口因预约拒绝；SystemSafety 强制删除先通知 Interaction | Action 取消，清理 IK/Animator token；对象已失效时跳过物理恢复但释放记录 |
| 动画中角色被销毁 | Owner 生命周期事件触发 | 看门狗或 Session shutdown 恢复物品并释放；不得等待 Animator 回调 |
| 目标突然被遮挡 | 接触前 Capsule/Shape cast 和视线复核 | 动画走最近安全退出点，回滚；最多重规划一次 |
| 吸附路径中途阻塞 | 每 FixedUpdate 验证下一小段 | 停在最后安全 Pose，沿已验证路径反向回退；失败则就地安全释放 |
| 已附着但状态切换失败 | `Committing` 使用 expected state + InteractionID；绑定动作包在补偿事务中 | 解绑，状态补偿回 Reserved，按快照回滚；绝不直接开放玩家操作 |
| 物品状态已转 Held 但手绑定失败 | 捕获异常，只有相同 InteractionID 可补偿 | Held -> Reserved -> Rollback；记录 CommitRejected |
| 保存 | 先暂停新行为，取消活动交互并等待有限回滚时间 | 不保存预约；持有物保存最后稳定 Pose。强制超时时在最近安全 Pose 释放后保存 |
| 读档 | 临时控制态不反序列化，所有 generation 重建 | 所有物品从 Available 状态恢复；旧 InteractionID 永久失效 |
| 切换场景 | `StopAll(SessionEnded)` 在 Registry 销毁前执行 | 恢复/释放后销毁；新场景不继承临时预约 |
| 预约超时 | Action 超时或安全截止时间到达 | 统一 Cancel -> Rollback -> Release；发出 `ReservationExpired` 指标 |
| 所有者丢失 | 看门狗找不到活动 Action/持有控制器 | 恢复快照或最近安全 Pose；清除玩家禁用和 UI 状态 |
| 回调乱序/重复 | 校验 generation、InteractionID、transitionVersion 和 event sequence | 旧回调无操作，只计 `StaleCallback`；释放接口幂等 |
| 玩家持续点击锁定物品 | `CanPlayerOperate` 在 Hover/Select/Use/Delete 入口统一拒绝 | 锁图标、轻量拒绝音/震动；文字提示按 cooldown 限流，不刷日志 |
| 其他物体碰撞/爆炸推动 | Stabilized 状态为运动学并保持实体碰撞，破碎/冲量入口检查状态 | 忽略对目标施力；支撑体异常移动则取消 |
| 预约时支撑物被玩家抓走 | support generation/速度变化触发 | 取消并按支撑当前 Pose 计算回滚；无法回原位时最近安全释放 |
| 目标破碎 | 普通破碎在预约/持有状态拒绝；SystemSafety 强制破碎先撤销交互 | 清理原物品记录后生成碎片；旧 InteractionID 失效 |
| 应用暂停/失焦 | 现有 `GameSession.SetPaused` 先停止 Action | 交互回滚后再冻结全局物理；恢复不复活临时预约 |
| 回滚原位被占 | 对原 Pose、支撑相对 Pose 和记录安全 Pose 依次检查 | 使用最近安全 Pose 恢复动态物理；宁可自然落下，不锁死 |
| 看门狗自身异常 | 单个物品恢复包裹异常边界，不中止整个 Registry 扫描 | 强制启用 XRI/Collider，清除租约；记录 `PhysicsRestoreFailed` 供修复 |
| 缺少餐桌等 required role | 准入阶段得到 `BlockedMissingRole` | 不启动、不预约；通过系统消息提示缺失物品 |
| 物品存在但正被玩家持有 | 区分 `RoleUnavailable` 与 Missing | 不抢玩家物品；提示等待/放下或选择同类替代物 |
| 多个同类苹果 | 按稳定性、完整路径和计划总代价选择，绑定 itemId + generation | 绑定后实例稳定；硬预约前失效可按策略重绑一次 |
| 两个角色错误绑定同一实例 | `distinctFromRoles` 和计划验证阻止 | 计划阶段失败，不进入 Coordinator |
| 批量预约第二个物品失败 | `TryReserveBundle` 先验证全部再提交 | 任何物品都不被本批次锁定；重绑或 Blocked |
| 玩家移动尚未硬预约的未来物品 | RoleBinding revision 失效 | 当前手中/已 Staged 物品保持；只重算剩余计划，超过次数则补偿 |
| 玩家点击已 Staged 物品 | Staged 仍由 ActionRunID 拥有 | 统一拒绝并提示冷月正在准备；不能因不在手里而开放 |
| 计划需要三只手 | HandOccupancy 验证失败 | 自动插入工作台放置 Step；无槽位则 `NoFeasiblePlan` |
| 餐桌工作槽被占 | authored slot 的占位检查失败 | 换槽/换桌；均失败时 UI 提示清理桌面 |
| 餐桌在切割前被移动 | workspace local Pose/revision 超阈值 | 提交前取消并完整回滚，或仅对剩余步骤重规划 |
| 餐桌在切割提交后被移动 | 已越过不可逆提交点 | ForwardRecovering：保护苹果块、放下刀并在最近安全支撑完成 |
| 已 Staged 的盘子被强制删除 | 普通删除拒绝；SystemSafety 删除使 role generation 失效 | 提交前回滚其余物品；提交后为输出寻找备用安全支撑 |
| 切割 Motion 中断 | 检查是否越过 `CutCommitPoint` | 提交前恢复完整苹果；提交后绝不复活苹果，转 forward recovery |
| 苹果块预创建失败 | `ActionResultTransaction.Prepare` 未提交 | 销毁临时输出，完整苹果保持；Action 失败并补偿 |
| 苹果块提交后存档失败 | 世界事务已提交但持久化失败 | 保持当前一致世界状态并重试保存；不回滚成整苹果制造重复 |
| 盘子容量不足 | 结果 Prepare 计算输出槽失败 | 切割前 Blocked；提示更换/清空盘子 |
| 补偿中某个物品原位被占 | 每个补偿操作独立验证 | 继续补偿其他角色；该物品落到最近安全 Pose，最终释放全部租约 |

锁定视觉反馈不新增描边。现有 Focus outline 用于指向选择；预约状态建议降低少量饱和度并在
玩家指向时显示小锁图标和短提示，避免两种状态共用同一种视觉语义。

## 10. 性能与扩展性

### 10.1 空间查询

- `ItemRegistry` 保留 ID 字典用于显式目标 O(1) 查询。
- 自治搜索增加 1 m 设计初值网格的空间哈希；物品跨格或稳定状态变化时事件更新。
- 搜索先按距离、能力、控制态、稳定状态、质量和高度做无 Physics 查询过滤。
- 只对最多 8 个候选做粗净空，只对最多 3 个候选计算 NavMesh 完整路径。
- 玩家或 AI 已给出 target ID 时不扫描全场。
- 多角色 Action 为每个 Role 只保留 top-K 候选；先处理候选最少的 Role，并以有上限的
  回溯搜索分配不同实例，不能计算无限笛卡尔积。

### 10.2 检测频率与距离分层

| 层级 | 设计初值 | 行为 |
|---|---|---|
| Active Interaction | 当前唯一目标 | 每 Tick 检查 lease/目标版本；Sweep 只在接触和吸附阶段按 FixedUpdate |
| Near | <= 2 m | 动态目标 5 Hz 更新索引和稳定状态，不做持续 Sweep |
| Warm | 2-5 m | 1 Hz 更新索引；候选搜索时再做几何检查 |
| Far | > 5 m | 只响应注册、移除、跨格和显式目标事件 |

这些频率是设计初值，必须用 PICO 真机 Profiler 调整，当前没有设备侧毫秒预算证据。

### 10.3 事件驱动与轮询边界

事件驱动：

- 注册/移除、控制态变化、稳定/动态变化、跨空间格、支撑变化、Owner 销毁。
- 玩家 Select/Use/Delete 尝试。
- Animator state 进入/退出和 Interaction 终态。

有限轮询：

- 活动交互目标的遮挡、距离、支撑和心跳。
- 看门狗 1 Hz。
- 自治候选刷新 2 Hz 设计初值。

不要给每个物品增加独立 Update 扫描附近角色。现有 `GameItem.Update` 的稳定检查后续应改为
低频/事件化稳定通知，或由共享物理观察器分帧处理。

### 10.4 Sweep 与 IK 时机

- Sweep 不用于候选列表全量扫描。
- 路径预检查只对最终目标和站位执行。
- 吸附前验证完整路径；吸附中只验证下一步。
- IK Rig 在非交互时权重为 0，不更新目标和 Hint。
- 双手 IK、Root Warp、手指 Pose 只在对应 Motion 窗口激活。
- 使用 NonAlloc Physics API；缓冲区满按失败处理并计数，不能静默漏碰撞。

### 10.5 缓存与失效

可缓存：

- Item Profile 到运行时只读数据。
- Prefab 锚点局部 Pose。
- Motion 兼容索引：动作族 × 手别 × 握持姿势。
- 稳定物品的支撑 ID、空间格和 bounds。
- 站位候选的物品/支撑局部 Pose。

失效条件：

- item/support generation 改变。
- 物品移动超过 0.05 m 或旋转超过 5°的设计初值。
- 控制态、能力、支撑、Collider 或房间导航版本改变。
- MotionSet 或角色比例在 Editor 重新导入。

不缓存跨帧 Physics 命中对象引用作为安全结论；环境改变后必须复核。

### 10.6 多 NPC 扩展

- 预约记录已包含 ownerId、priority、InteractionID，可直接支持多 NPC。
- 候选评分由各 NPC 计算，最终所有权仍由 ItemRegistry 原子提交。
- 不抢占活动预约；高优先级只影响尚未提交的候选排序。
- 需要抢险类 SystemSafety 时，先取消原 Action 并等待/强制完成回滚，再转移，不直接改 owner。
- 多 NPC 共抬是 P2 独立的 multi-owner lease，不应把单 owner 字段改成随意列表。

### 10.7 可观测性

保留聚合指标和最近有限条结构化记录，不把逐帧 trace 写入物品存档：

```text
interaction_started/completed/failed/cancelled
reservation_acquired/rejected/expired/orphan_recovered
contact_mode
phase_duration_ms
failure_code
position_error_m / angle_error_deg
ik_peak / warp_distance_m / snap_distance_m
rollback_result
```

开发面板可按 Item Profile、Motion Family 和失败码排序。吸附率高的桶进入动画或站位修复，
而不是继续放宽吸附距离。

### 10.8 Action 计划与执行成本

- ActionDescriptor 和 RoleSpec 在加载时编译为只读索引：capability -> item candidates。
- ItemRegistry revision 未变化时可复用便宜角色候选；路径、占位和控制态仍在提交前复核。
- PlanBuilder 只在 Action 提交、角色失效或剩余计划受影响时运行，不逐帧重新生成计划。
- 批量预约复杂度与角色数线性相关；一次 Action 设计上限 8 个 required roles。
- 补偿栈只保存 Step 级结构化操作和必要快照引用，不复制整场景。
- Apple pieces 等输出使用对象池或预制批量创建；未到 Prepare 阶段不提前常驻实例。
- 同一角色当前只运行一个身体复合 Action；Face/Dialogue 并行仍由现有通道租约决定。
- 性能报告分开记录 planning、navigation、physics validation、animation 和 compensation，
  不能只给一个 Action 总耗时。

## 13. 开发计划

### P0：最小闭环

目标：证明不会锁死、不会绕过权威状态、失败可恢复，并获得首批真实接触数据。

#### 范围

1. 升级 `ItemRegistry`：ItemControlState、InteractionID、generation、原子转移、幂等释放、
   Action 关联和看门狗。
2. 玩家抓取、删除、破碎、电视/收音机使用入口统一接入物品控制状态。
3. 为 `PhysicsBody` 增加 InteractionID 所有的 Stabilized/Snap/Held 控制模式和快照恢复。
4. 新增 `ItemInteractionProfile`、`ItemInteractionAuthoring`、`CharacterInteractionMotionSet`
   及 Editor 审计/Gizmo。
5. 新增 `CompositeActionDescriptor`、`ICompositeActionPlanBuilder`、`ActionRunContext`、
   `ActionStepRunner`、补偿栈和结构化 AdmissionResult。
6. 新增原子 `TryReserveBundle`、`StagedForAction`、RoleBinding、HandOccupancy 和工作台槽。
7. 代表物：苹果、马克杯或碗、遥控器。刀具暂不进入 P0 对外成功清单。
8. 动作族：`GroundBendPickup`、`MidReach`，左右手至少完成一侧生产链；补一个安全放回动作。
9. in-place 动画 + TwoBone IK；Root Motion/Warp 暂不开启。
10. 安全吸附：复合形体 Sweep、旋转占位采样、反向回滚、类别参数。
11. 完成 PickUp -> HeldByCharacter -> PutDown -> Available 的完整状态转移。
12. 增加内部 `stage-fruit` 验证 Action：苹果 + 盘子 + 餐桌，两趟搬运后均进入
    `StagedForAction`，再按补偿计划恢复。它只验证多物品编排，不做切割。
13. 缺餐桌、无空槽等情况通过 `SystemNotificationPresenter` 弹出对应系统消息，
    不打开目录、不保留 pending request、不创建锁定 Action。
14. 保存/暂停/场景切换前统一取消并回滚；读档不恢复预约。
15. 开发指标：Natural/IK/Snap/Failed、Action admission、Step、bundle、补偿和失败码。

#### 测试

- EditMode：同帧竞争、重复预约、旧 ID、同 ID 重建、重复释放、超时、Owner 丢失。
- Engine/PlayMode：XRI 与 NPC 争抢、删除/使用拒绝、Sweep 阻塞、吸附中途加障碍、
  动画事件乱序、附着提交失败、暂停/保存/读档、200 次强制取消循环。
- Action 编排：缺餐桌、角色错误复用同一实例、三只手计划、批量预约冲突、未来角色移动、
  Staged 物品争抢、第二趟中断和逆序补偿。
- 场景矩阵：地面、固定桌面、沙发高度代理、不同朝向和轻度遮挡。
- PICO：控制器和手势分别验证提示、自然度、穿模和恢复；记录 Profiler 数据。

#### P0 准出

- 所有操作入口服从同一控制状态。
- 自动化强制取消后无残留租约、禁用 Collider、禁用 XRI 或错误父节点。
- 吸附路径不存在可复现穿墙/穿桌板。
- 成功后先转 Held，再由 PutDown 释放；不存在 Reserved 直接变 Available 的中间窗口。
- 能按 Profile/Motion 查看吸附率和失败码。
- `stage-fruit` 任一 Step 失败后，苹果、盘子、餐桌和角色手占用全部恢复，无部分 bundle。
- 缺餐桌时不播放“开始切水果”的成功回复，只弹出“需要先放置餐桌”等对应系统消息。

### P1：自然度与覆盖率

1. 增加 `GroundSquatPickup`、`LowReach`、`HighReach`、双手轻物动作。
2. 增加有限 `InteractionRootDriver` 和 Root Warp，所有根运动仍经 PhysicsBody。
3. 增加持有会话 continuation：Use/Eat/Place 请求由同一个 Coordinator Action 和
   InteractionID 延续，不通过临时释放再预约。
4. 正式实现 `cut-fruit`：苹果、刀、盘子、餐桌四角色、桌面工作槽、左右手协同、
   切割 Motion、`CutCommitPoint` 和苹果块结果事务。
5. 覆盖盘、勺、玩偶、筷子和刀；刀具启用更严格锚点、旋转和路径策略。
6. 支持稳定的低速移动支撑，相对坐标寻路和有限重规划。
7. 增加手指握持 Pose、主辅手求解和放置槽。
8. 根据 P0 指标修正 Motion 接触 Pose、站位和 IK，不优先放宽 Snap。
9. 增加内容批量配置、动作预览和 Action Role/Step 覆盖率报告。
10. 覆盖切割提交前完整回滚、提交后 forward recovery、输出预创建失败和存档失败。

P1 准出：主要小物件在地面、桌面、沙发和中高架位置都有明确动作或明确拒绝；移动支撑取消
不会遗留锁；高频桶的 Snap 率经过动画/站位优化后下降，并保留前后数据。`cut-fruit`
缺任一角色时给出准确 UI 指引，成功时盘中只有一次有效输出，任意中断无整苹果/苹果块重复。

### P2：高级表现与复杂物理

1. 大型物品双手搬运、移动速度限制、转身净空和放置协作。
2. 两 NPC 竞争策略、队列和公平性；如确有玩法需求，再设计 multi-owner 共抬租约。
3. 更复杂的移动平台、支撑链、拥挤空间和动态障碍重规划。
4. 动态目标截停作为独立 Affordance，不放宽普通拾取。
5. 更精细全身 IK、脚锁、姿态匹配和动画变体选择。
6. 多阶段使用动作，例如拿遥控器 -> 对准电视 -> 按键 -> 放回。
7. Action 计划变体选择，例如托盘一次搬运、多个餐桌成本比较、可替换水果/刀具。
8. 可组合配方内容工具；只有出现多个真实 Action 共用需求后才抽取，不提前建设任意规则引擎。
9. 真机性能压测后确定最大活动刚体、候选物品数、Sweep、IK 和 PlanBuilder 预算。

### 实施顺序结论

先实现权威状态、ActionRun 和补偿，再制作批量动画；先用 `stage-fruit` 证明多物品事务，
再实现有不可逆结果的 `cut-fruit`。任何阶段都不得以“动画还没好”为理由绕过预约状态，
也不得以“预约已取得”为理由省略路径和碰撞复核。
