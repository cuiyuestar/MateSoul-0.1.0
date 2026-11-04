# MateSoul 工作汇报总览

> 最后更新：2026-10-30

本文档记录 MateSoul 各阶段工作进展、完成内容、验证情况、技术债务和后续建议。

> **当前唯一生效的平台路线：** Unity 2022.3.38f1 + URP +
> `com.bytedance.pico.xr`（PICO Unity Integration SDK）+ XRI + Animation
> Rigging。Phase D0 之前的 Kotlin、FullStage、Spatial ECS 和 BOM 内容是
> 历史决策记录，不是当前待办。

## Phase 0 工作汇报：首版开发计划与架构设计（历史方案）

### 一、工作完成情况

本阶段完成了 MateSoul 首版开发计划和初始技术文档编写。

新增文档：

- `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_首版开发计划.md`
- `/Users/bytedance/MateSoul/项目沉淀资料/technical_docs.md`
- `/Users/bytedance/MateSoul/项目沉淀资料/work_reports.md`

核心设计决策：

1. MateSoul 不直接复制 MateFairy 工程，而是从 FullStage 模板重新搭建。
2. 首版范围纳入状态机、情绪调度、动画调度和控制权租约。
3. 首版采用 Avatar Runtime-first 架构，让 `AvatarRuntime` 成为少女身体与表现控制的唯一入口。
4. 用 `ControlLeaseManager` 替代 MateFairy 中分散的 action lock、player interaction state 和动画 owner 状态。
5. 动画系统以 `AnimationKey` 语义键驱动，避免直接绑定 trackIndex。
6. 当前 `girl.glb` 无动画轨道，因此首版先使用 `NoOpAnimationPlayer` 跑通调度逻辑。
7. action 方案暂定，首版只保留接口，不向 LLM 暴露未实现 action。

### 二、参考与分析

本阶段参考了 MateFairy 的以下内容：

- README 架构说明
- `HomeStage.kt`
- `FairyBehaviorSystem.kt`
- `AnimationModule.kt`
- `EmotionEngine.kt`
- `InteractionActionCore.kt`
- `PlayerFairyInteractionCore.kt`
- 项目沉淀资料中的技术文档和工作汇报

识别出的 MateFairy 主要可改进点：

- 动画调度器直接读取 action 和玩家交互全局状态，业务穿透较重。
- 行为系统承担过多职责，后续维护成本高。
- 控制权状态分散在多个 runtime singleton 中。
- 情绪系统过薄，仅能做字符串去重和动画映射。
- action 支持列表中混入了部分未完全接稳的能力，容易形成体验断裂。

### 三、验证情况

本阶段未进行代码编译或真机验证，因为尚未创建 Android 工程。

已完成的验证：

- 确认 MateSoul 当前仓库仅包含 `girl.glb` 和 git 元数据。
- 确认项目文档目录已创建。
- 确认首版开发计划、技术文档、工作汇报已写入。

### 四、技术债务与风险

当前主要风险：

1. `girl.glb` 无动画轨道，首版无法验证真实走路和情绪动画效果。
2. MateSoul 尚未创建 Android / Gradle / PICO Spatial SDK 工程。
3. action 玩法方案待定，需要后续单独进行交互设计。
4. 地面高度首版计划使用 Stage floor，真实房间地面/mesh grounding 需要后续验证。

缓解策略：

- 首版用 NoOp 动画播放器保证架构可测。
- 先搭 FullStage 最小工程，再逐步迁移 MateFairy 中成熟的可复用思想。
- action 在未确认前不进入 LLM 可用能力列表。
- 后续补动画后，只更新 `AnimationCatalog`，不重构状态机和情绪链路。

### 五、下一阶段建议（历史建议，已废弃）

当时建议下一阶段进入 Phase 1：FullStage 工程骨架搭建；该建议现已废弃。

Phase 1 目标：

- ~~基于 FullStage 模板创建 MateSoul Android 工程。~~
- ~~配置 PICO Spatial SDK BOM、Kotlin、Compose、Gradle。~~
- 配置 assets noCompress。
- 将 `girl.glb` 纳入 app assets。
- 完成首次 `compileDebugKotlin` 或等价构建验证。

进入 Phase 1 前建议确认：

- 是否严格按 FullStage 模板起步。
- 是否同意首版只做文本输入。
- 是否同意首版 action 仅保留待定接口。
- 是否创建 `.trae/development-log.md` 记录后续跨会话开发日志。

## Phase 0.1 工作汇报：心情值系统架构决策

### 一、工作完成情况

本阶段根据用户补充需求，对 MateSoul 首版开发计划中的“情绪调度”部分进行了架构升级。

更新文档：

- `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_首版开发计划.md`
- `/Users/bytedance/MateSoul/项目沉淀资料/technical_docs.md`
- `/Users/bytedance/MateSoul/项目沉淀资料/work_reports.md`

新增核心决策：

1. 首版完整实现心情值系统基础设施，不只做简单情绪动画触发。
2. 首版心情类型包括难过、生气、开心、甜蜜。
3. 难过和生气属于负面心情；开心和甜蜜属于正面心情。
4. 心情值使用 `0-100` 刻度，默认阈值为轻度 `30`、中度 `60`、高度 `85`。
5. 心情值运行时事实源采用 ECS `MoodComponent`。
6. 心情状态采用“数值 + 派生状态”模型。
7. Soul 采用“玩家可见五维双极人格 + 内部数值因子派生”。
8. 文本语义识别采用“规则 + LLM 四分分类器”。
9. 低置信度分类结果按正常增量的 `25%` 生效。
10. 状态回落采用事件信号队列，并通过 `stateToken` 防止旧信号误触发。
11. 心情值需要长期持久化，但应用关闭期间不结算状态时间。

### 二、关键规则沉淀

负面心情冲突：

- 难过与生气同时达到阈值时，难过优先。
- 难过优先时，清空生气值。

正面心情冲突：

- 开心与甜蜜同时达到阈值时，甜蜜优先。
- 甜蜜优先只决定当前表现，不清空开心值。

正负心情冲突：

- 负面表现优先级高于正面表现。
- 正面状态不会清空负面心情值。
- 负面状态进入表现时，如果当前正处于正面表现状态，则清理当前正面激活态，并清空该正面心情自身数值。
- 负面状态进入表现时，如果当前不处于正面表现状态，则不清空正面心情值。
- 任一心情退出自身 active state 时，都必须清空该心情自身数值。

状态回落：

- 高度负面状态 `40s` 后发出转轻度信号。
- 中度负面状态 `15s` 后发出转轻度信号。
- 轻度负面状态 `60s` 后发出转无状态信号。
- 高度/中度正面状态 `15s` 后发出转轻度信号。
- 轻度正面状态是持久状态，没有默认退出时间。
- 高度/中度转轻度属于同一心情类型内等级回落，不清空自身数值。
- 轻度负面转无状态、轻度正面被负面挤占、任一心情被其它心情挤占时，退出方必须清空自身数值。

## Phase 0.2 工作汇报：心情值退出与清空规则调整

### 一、工作完成情况

本阶段根据用户补充要求，调整了心情值系统中正面心情被负面心情挤占时的清空边界，以及所有心情退出自身状态时的数值清空规则。

更新文档：

- `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_首版开发计划.md`
- `/Users/bytedance/MateSoul/项目沉淀资料/technical_docs.md`
- `/Users/bytedance/MateSoul/项目沉淀资料/work_reports.md`

### 二、规则调整

正面心情：

- 轻度正面心情仍作为持久状态，没有默认退出时间。
- 高度/中度正面心情统一在 `15s` 后转轻度。
- 高度/中度转轻度是同一心情类型内的等级回落，不视为退出，不清空自身数值。

负面挤占正面：

- 执行负面心情表现时，如果当前正处于正面表现状态，则退出当前正面激活态，并清空该正面心情自身数值。
- 执行负面心情表现时，如果当前不处于正面表现状态，则不清空正面心情值。

通用退出规则：

- 任一心情退出自身 active state 时，都必须清空该心情自身数值。
- 轻度负面转无状态属于退出自身状态，必须清空该负面心情自身数值。
- 轻度正面被负面心情挤占属于退出自身状态，必须清空该正面心情自身数值。

### 三、验证情况

本阶段为文档规则更新，未进行代码实现、编译或真机验证。

已完成验证：

- 主开发计划已更新清空边界。
- 技术交接文档已同步更新。
- 工作汇报已补充 Phase 0.2 记录。

### 四、后续开发建议

后续实现 `MoodSystem` 时，需要单测覆盖以下场景：

- 正面轻度状态在没有负面挤占时长期保持。
- 负面进入时，如果当前 active state 为正面，则清空该正面 MoodMeter。
- 负面进入时，如果当前 active state 非正面，则不清空正面 MoodMeter。
- 高/中等级回落到轻度时不清空自身 MoodMeter。
- 退出到无状态或被其它心情挤占时清空自身 MoodMeter。

### 三、验证情况

本阶段为文档与架构决策更新，未进行代码实现、编译或真机验证。

已完成验证：

- 用户通过选项 UI 完成关键方案准出。
- 主开发计划已更新心情值系统章节。
- 技术文档已追加 Phase 0.1 技术交接。
- 工作汇报已记录本阶段决策。

### 四、技术债务与风险

当前风险：

1. 心情值系统较首版原计划更重，需要在 Phase 1/2 之后尽快建立纯 Kotlin 单测。
2. 长期持久化但不做离线结算，可能导致用户重进应用时仍处于负面状态。
3. 规则 + LLM 四分分类器的误判会直接影响角色心情值。
4. Soul 五维双极到内部因子的映射需要单独设计调参表。

缓解策略：

- 先实现 `MoodSystem` 纯业务逻辑和单元测试，再接入 PICO ECS。
- 低置信度只按 25% 增量生效，降低误判伤害。
- 状态回落事件必须携带 token，避免旧信号误触发。
- 持久化策略保持可替换，后续可增加离线结算或安全恢复。

### 五、后续开发建议

当时的 Phase 1 仍建议先搭建 FullStage 工程骨架；该建议现已废弃。

心情值系统建议作为 Phase 3/4 的核心业务模块提前实现纯 Kotlin 测试：

- `MoodSystemTest`
- `MoodTransitionQueueTest`
- `SoulFactorMapperTest`
- `MoodSemanticClassifierTest`
- `MoodPersistenceStoreTest`

## Phase 0.3 工作汇报：Trae + pico-cli 开发工作流固化

### 一、工作完成情况

本阶段将 MateSoul 后续开发主路线正式切换为 Trae + pico-cli 直跑。Claude Code 已保留安装，但不再作为当前开发阻塞项。

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

核心决策：

1. 当前主路线为 Trae 负责 AI 协作，pico-cli 负责 PICO 工具链能力。
2. 不删除 Claude Code，后续有可用登录方式后可恢复使用。
3. 使用项目内 `.pico-home` 作为 PICO_HOME。
4. 使用项目内 `.pico-cli-workspace` 作为插件与 primer-cli 工作区。
5. 后续 PICO CLI 命令优先通过 `scripts/pico-run.sh` 执行。

### 二、验证情况

已执行并通过：

```bash
scripts/pico-run.sh --version
scripts/pico-run.sh mcp doctor
scripts/pico-run.sh plugin doctor
scripts/pico-doctor.sh
```

验证结果：

- `pico-cli/0.4.0` 可用。
- Node.js `v22.23.1` 满足要求。
- Primer doctor 配置正常，Java 17+ 可用。
- Spatial Knowledge MCP READY。
- Spatial Editor MCP READY。
- `agent-vault 0.13.3` 中 `graph.json` 可用。

保留警告：

- Codex、Copilot、TraeCLI 未安装 `pico-spatial-agentic-tools`，这是非当前主路线宿主警告。
- `6.0` agent-vault 包没有 `graph.json`，doctor 会自动使用 `0.13` 的 graph 数据。

### 三、技术债务与风险

当前风险：

1. Trae 是否自动加载项目级 `.mcp.json` 取决于 Trae 当前版本；当前主路线不依赖自动挂载。
2. `pico-cli editor doctor` 在 Trae 沙箱下可能被 `~/.pico-cli/editor-state.json.lock` 写入限制拦截。
3. Unity Editor 深度自动化尚未接入，因为当前 `pico-cli` 没有 `mcp:unity-cli` 命令。
4. Claude Code 登录仍未解决，后续若接入 Claude Code，需要重新验证插件和 MCP 自动注入链路。

缓解策略：

- 日常工具链操作统一走 `scripts/pico-run.sh`。
- 环境体检统一走 `scripts/pico-doctor.sh`。
- 需要 MCP 能力时先看 `scripts/pico-run.sh mcp doctor`。
- Unity 路线进入实装阶段后，再单独验证 Unity MCP bridge。

### 四、后续开发建议

下一步可继续 Phase 1：创建首个可运行工程骨架。

历史备选：PICO Spatial SDK Kotlin 路线（已废弃，不执行）：

```bash
scripts/pico-run.sh project create \
  --template stage \
  --dir /Users/bytedance/MateSoul/apps/mate-spatial-demo \
  --name MateSpatialDemo \
  --package com.matesoul.spatial.demo
```

当前生效：Unity 路线：

- 先确认 Unity 项目目录与 Unity 2022.3 LTS 安装状态。
- 再接入 PICO Unity SDK OS6-Preview、XRI、Animation Rigging。
- 使用 `scripts/pico-run.sh emulator doctor` 和设备命令完成运行验证。

## Phase D0 工作汇报：Demo 开发方案冻结

### 一、工作完成情况

本阶段完成 MateSoul 首个可运行 Demo 的产品范围、技术路线、核心架构、开发阶段和验收标准设计。

新增文档：

- `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_Demo开发方案.md`

同步更新：

- `/Users/bytedance/MateSoul/项目沉淀资料/technical_docs.md`
- `/Users/bytedance/MateSoul/项目沉淀资料/work_reports.md`

Demo 必做功能：

1. 玩家视野下方的五入口基础工具栏。
2. 角色 Idle、随机行走和随机虚拟物品互动。
3. AI 文本对话和视野下方字幕。
4. 单手牵手 IK 与玩家移动跟随。
5. 虚拟物品空间放置。
6. 冷月接近虚拟椅子并坐下、驻留和起身。

### 二、关键设计决策

1. 当前正式实现路线统一为 Unity 2022.3、URP、PICO XR、XRI 和 Animation Rigging。
2. Demo 是后续正式版本的产品内核，不创建一次性 Demo Runtime。
3. MateFairy 只复用 Runtime 装配、ConversationOrchestrator、结构化 AI 协议和可取消 Action 等思想，不复制 Kotlin/Spatial ECS 平台代码。
4. UI、AI、随机调度和玩家抓取统一提交 AvatarIntent。
5. 角色动作由 `AvatarActionCoordinator` 和 `ControlLeaseManager` 统一仲裁。
6. 控制权拆分为 Locomotion、FullBody、LeftArm、RightArm、Gaze 和 Face 通道。
7. Random、AI、UI 共用同一套物品 Action，不维护分叉实现。
8. 虚拟椅子使用预制 ApproachPoint、SeatSocket 和 ExitPoint，避免运行时猜测任意网格座位。
9. 牵手使用 TwoBoneIKConstraint；代码只控制 Target 和 IK Weight，不直接写手部骨骼。
10. 现有 `HeadPoseGuard` 只作为旧动画兼容策略，不允许覆盖正式 Demo 的上身 IK 和注视。

### 三、参考工程结论

MateFairy 中值得保留：

- Application-scope Runtime。
- Provider / Orchestrator / Port 分层。
- Action Request、Registry、可取消实例和来源优先级。
- 视觉模型与物理代理分离。

需要规避：

- 超大 HomeStage 和 BehaviorSystem。
- 多个全局 RuntimeDependencies。
- 两套 ActionRegistry。
- Prompt 内重复维护 Action 映射。
- UI、AI 或随机系统直接控制动画和位置。

`virtual-friend` 中值得保留：

- Animation Rigging TwoBone IK。
- 世界空间独立 Hand Target。
- IK Weight 平滑切换。
- 单一骨骼控制源。

需要重构：

- 抓取、根节点跟随、Animator 参数和头部注视集中在单个组件。
- 按初始 HMD Offset 直接 Lerp 角色。
- 硬编码供应商 Animator 参数。
- 缺少控制权仲裁、伸展边界和异常释放。

### 四、验证情况

本阶段仅完成架构设计和文档沉淀，未修改 Unity 运行代码。

已完成：

- 读取 MateFairy 对话、Runtime、行为、Action、椅子交互和 UI 实现。
- 读取 `virtual-friend` 牵手 IK 实现及迭代记录。
- 核对当前 MateSoul Unity、PICO XR、角色资产和运行时约束。
- 明确 Demo Golden Path、阶段准出条件和后续扩展规则。

未执行：

- Unity 编译。
- PlayMode 测试。
- APK 构建。
- PICO 真机验证。

### 五、技术债务与风险

1. 当前项目未引入 Animation Rigging。
2. 当前 Build Settings 仍只有 SampleScene。
3. 现有 Animator Override 只清理 Walk 和 Run，其余供应商动画尚未进入安全白名单。
4. Demo 需要的 Idle、SitDown、SitIdle 和 StandUp 尚未形成正式动画交付。
5. `HeadPoseGuard` 与上身 IK、注视和未来情绪动画存在写入冲突。
6. 头发 247 根蒙皮骨和高面数服装需要 PICO 真机性能门禁。
7. 当前没有 Unity Test assembly 和持续集成门禁。
8. PICO 真机尚未连接，无 APK 验证结果。

### 六、后续开发建议

下一阶段执行 D0：

1. 修复版本管理和角色发布基线。
2. 建立 `Assets/MateSoul` 模块目录及 asmdef。
3. 创建 Bootstrap、Demo_Main、Character_Validation、Performance_Benchmark 场景。
4. 创建 Lengyue Demo Prefab Variant。
5. 建立新的生产 Animator Controller。
6. 引入 Animation Rigging。
7. 完成最小 APK 构建与 PICO 角色显示性能冒烟测试。

在 D0 准出前，不开始 AI、牵手或虚拟物品业务实现。

## Phase Asset-Handoff 工作汇报：冷月当前外观动画制作包

### 一、工作完成情况

完成可直接交付动画师的冷月制作包：

- `ArtSource/SapphiArt/Release/Lengyue/AnimatorKit/Lengyue_AnimatorKit_V001.zip`
- Blender 主工作文件：`Lengyue_AnimationMaster.blend`
- 通用交换文件：`Lengyue_CurrentAppearance_Rig.fbx`
- 4 张独立贴图、4 张外观参考图、中文 `README.md`、JSON 报告及 SHA-256 清单

制作包不再依赖原 FBX 中失效的作者电脑绝对贴图路径。外观取自当前 Unity
`Lengyue_GameCharacter_V001.prefab`：紫色短发、紫色虹膜、LavenderCasual
服装、NeckExtension 以及已修复穿袖的 SkinCoverage。

### 二、关键设计决策

1. 保留原始 116 根身体变形骨骼的名称、层级和 Rest Matrix。
2. Blender 主文件保留 25 根非变形 IK/机制辅助骨；通用 FBX 仅导出变形骨架。
3. 眼球转动使用 `Eye_L/Eye_R` 骨骼，眨眼、眉毛和嘴型使用 22 个 Shape Key。
4. 头发 247 骨仅供构图参考，常规身体动画仍由 Unity 头发物理驱动。
5. Unity 当前可见服装、头发、颈部和袖内修复网格由 prefab 评估结果提取；
   脸、眼、牙齿沿用已验证的源 DCC 表情拓扑并替换当前眼部贴图。
6. Unity 运行时 `HairSpringPhysics`、`SkirtPoseGuard`、`HeadPoseGuard` 不伪装成
   DCC 功能，最终动作必须回 Unity 复验。

### 三、编译与测试情况

- Unity 快照：116 根身体骨、8 个当前可见 SkinnedMeshRenderer。
- Blender 主文件：116 根变形骨、141 根身体总骨、247 根头发骨、22 个表情。
- 通用 FBX round-trip：10 个网格、116 根身体骨、247 根头发骨、22 个表情、
  4 张贴图全部保留。
- 眼球骨骼测试位移：`8.7249 mm`。
- 手部 IK 测试位移：`51.2273 mm`。
- 睁眼、闭眼、视线及全身参考图人工复核通过。
- ZIP 解压测试和包内 SHA-256 校验通过。

### 四、风险与后续建议

1. Maya/3ds Max 未在本机实测；若材质未自动恢复，按 `README.md` 手动重连
   `Textures/`。
2. 身体纯骨骼 FBX 无法承载眨眼等 BlendShape 曲线，表情必须同时回传源文件
   或独立的“脸部网格 + BlendShape 动画”FBX。
3. 现有 BodyOnly 清理器会剔除眼球和表情曲线；新视线/表情需建立独立导入通道。
4. 大幅转头、弯腰和坐姿仍需在 Unity 中重新执行头颈与穿模验证。

## Phase Rendering 工作汇报：冷月无描边 Toon 渲染试验

### 一、工作完成情况

完成无描边 URP Toon 试验，并在 P0.2 阶段接入正式角色：

- 新增 `MateSoul/Character Toon` Shader。
- 新增脸部、眼睛、头发、服装和 SkinCoverage 五类 Toon 材质。
- 新增 `Lengyue_GameCharacter_ToonPreview.prefab` 独立预览变体。
- 新增 `Lengyue_ToonPreview.unity` 预览场景。
- 新增自动构建器 `LengyueToonPreviewBuilder`，可重复生成材质、Prefab、场景、
  前后对比图、脸部近景和三分之四视图。

原始 `Assets/SapphiArt` 未修改；正式
`Lengyue_GameCharacter_V001.prefab` 已在 P0.2 阶段切换为默认 Toon 材质。

### 二、渲染方案

1. 明确取消描边，仅保留一个 `UniversalForward` Toon Pass。
2. 身体和服装使用带彩色阴影的二阶直接光。
3. 头发增加硬边高光和低强度边缘提亮，但不生成轮廓线。
4. 眼睛使用 92% 弱光照混合，避免虹膜随环境光变黑。
5. 在没有 Face SDF 的 P0 阶段，脸部使用 90% 弱光照平涂，避免普通法线
   在鼻翼和眼窝形成不自然色块。
6. 实时阴影、间接光、附加灯和雾仍遵守 URP 管线。

### 三、编译与验证

- Unity 2022.3.38f1 C# 编译通过。
- Shader 在 Metal/URP 渲染路径成功出图，无 Shader 编译错误。
- 自动化日志输出 `LENGYUE_TOON_PREVIEW_PASS`。
- `ToonPreviewReport.json` 明确记录 `outlineEnabled: false`。
- 在同一正前上方主光、同一相机和同一 T Pose 下生成 Simple Lit/Toon 对照图。
- 人工复核正面、脸部近景和三分之四视角：无缺材质、无黑模、无描边、
  眼部贴图正常、头颈连续。

### 四、技术债务与后续建议

1. 当前为 P0 无描边 Toon，不是假定完成的“原神式脸部”。
2. P1 需要美术制作 Face SDF；在资产到位前不应伪造方向性面部阴影。
3. 头发硬边高光后续应增加 Hair Specular Mask，避免完全由模型法线决定。
4. 用户确认视觉方向后，才应把 Toon 材质并入正式角色 Prefab。
5. 上 PICO 前需在 Balanced/Performant 两级 URP 配置下做 GPU 与双眼视觉验证。

### 五、P0.1 衣服低面感复核

- 实测 `LavenderCasual_Garments` 为 68,915 个三角面。
- `LavenderCasual_SkinCoverage` 为 9,060 个三角面。
- 衣服合计约 77,975 个三角面，已不是低模；未执行盲目细分。
- 低面感根因是 Toon 阴影阈值过硬，而非轮廓拓扑不足。
- Shader 已加入基于 `fwidth(N dot L)` 的屏幕导数自适应 Softness。
- Cloth Softness 最终调整为 `0.085`，并增加 `DiffuseWrap=0.32`；
  SkinCoverage Softness 调整为 `0.055`。
- 新截图中裙身明暗边界更平滑，同时保留二阶卡通结构。
- 验证报告最终记录 `runtimeReady=true`、
  `integratedIntoProductionPrefab=true` 和衣服真实三角面统计。

当前 Toon 方案是真实可运行的 Shader/Material/Prefab，不是后处理截图模拟；正式
Prefab 已切换为该风格，所有通过该 Prefab 创建的角色实例默认直接使用 Toon 渲染。

## Phase Rendering P0.2 工作汇报：布料优化与默认 Toon 正式接入

### 一、工作完成情况

1. 在 `MateSoulCharacterToon.shader` 中加入 `_DiffuseWrap`，并保留
   `fwidth(N dot L)` 自适应色阶平滑，降低肩袖和胸衣的硬折面感。
2. 服装最终参数为 `ShadowSoftness=0.085`、`DiffuseWrap=0.32`；未增加模型面数，
   避免扩大 PICO Skinned Mesh 顶点处理成本。
3. `LengyueCharacterBuilder` 在正式 Prefab 构建时调用
   `ApplyDefaultToonMaterials()`，以后重建角色也不会退回 Simple Lit。
4. 正式 `Lengyue_GameCharacter_V001.prefab` 的 10 个目标 Renderer 已全部使用
   `MateSoul/Character Toon`，报告记录 `integratedIntoProductionPrefab=true`。
5. 自动生成 Rest/T Pose、Idle、Walk、Run 四种姿势的正面、侧面、背面和脸部
   近景，共 16 张原始截图；另生成四张四宫格和一张四姿势正面总览。

### 二、编译与测试情况

- Unity 2022.3.38f1 构建日志：`LENGYUE_V001_BUILD_PASS`。
- Toon 预览日志：`LENGYUE_TOON_PREVIEW_PASS`。
- PlayMode 第 60 帧：`LENGYUE_TOON_PLAYMODE_PASS`。
- 运行时门禁：`defaultToonMaterialsVerified=true`、
  `toonRendererCount=10`、`outlineEnabled=false`。
- 头发物理：初始化成功，最多模拟 68 根活动骨；本轮累计 15 个 substep。
- Walk：30 FPS、33 帧全采样，裙腿三角形相交帧数为 0。
- Run：30 FPS、21 帧全采样，裙腿三角形相交帧数为 0。
- 头颈最大表面间隙：Walk `0.00000099985 m`，Run `0.00000103104 m`。
- 第二次 PlayMode 复测先删除旧运行时截图，再于 `2026-09-23 23:04:42`
  生成新截图和报告；请求标记已自动清理并退出 PlayMode。

### 三、交付证据

- 四姿势四宫格：
  `MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/V001/Toon/Evidence/`
  下的 `rest_contact.png`、`idle_contact.png`、`walk_contact.png`、
  `run_contact.png`。
- 四姿势正面总览：`all_poses_front.png`。
- 启动运行时画面：`runtime_default.png`。
- 静态报告：`Toon/Validation/ToonPreviewReport.json`。
- 运行时报告：`Toon/Validation/ToonPlayModeReport.json`。
- 动画全帧报告：
  `ArtSource/SapphiArt/Release/Lengyue/V001/reports/locomotion_validation.json`。

### 四、技术债务与后续建议

1. 当前脸部仍是无 Face SDF 的 P0 平涂方案；P1 应由美术提供 Face SDF。
2. 头发高光仍由法线和统一参数控制，后续可增加 Hair Specular Mask。
3. T Pose 袖口保留原始服装的直线轮廓；如需修改实际轮廓，应在 DCC 中只对袖口
   定向加线并重新传递蒙皮，不应整体细分服装。
4. 进入 PICO 交付前仍需在目标设备执行双眼视觉、GPU 时间和热稳定性验证。

## Phase Accessories 工作汇报：参考图四芒星胸针

### 一、完成内容

- 按参考图制作紫色四芒星胸针：圆润外框、深紫内凹面、浅色凸起星芯及薄型背面。
- 独立尺寸 70 × 92 × 10.4 mm；2,212 顶点、4,416 三角面。
- 提供可编辑 Blender 主文件、FBX、Unity 网格与三类 Toon 材质。
- 配饰位于胸前 V 领中央，100% 绑定现有 `Chest`，无新增身体骨骼和运行时跟随脚本。
- 正式 `Lengyue_GameCharacter_V001.prefab` 已挂接；`LengyueCharacterBuilder`
  重建时调用 `LengyueStarBroochBuilder.AttachToCharacter()` 保留配饰。

### 二、验证结果

- Blender 外框和星芯均为闭合网格，非流形边为 0。
- Idle 41 帧、Walk 33 帧、Run 21 帧，以及胸部 ±20° 两姿态均通过采样间距与刚性跟随检查。
- 静态最小前表面深度间距约 1.5 mm；动画采样最小间距约 2.97 mm。
- 现有骨架 Rest Pose、已有网格引用均保留；146 个供应商/服装/动画文件哈希无变化。
- PlayMode 第 60 帧通过：`broochAttachmentVerified=true`，
  `defaultToonMaterialsVerified=true`，`toonRendererCount=11`，无描边。
- 配饰六视图、四种姿势全身与佩戴近景、运行时截图已生成。

### 三、资产路径与踩坑

- Blender 源资产：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Accessories/StarBrooch/V001/`
- Unity 资产与证据：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/V001/Accessories/StarBrooch/`
- 独立产品相机需设置较小 near clip，否则相机距配饰 0.3 m 时凸起星芯可能被裁切。
- 重写 Unity Mesh 时显式更新顶点、法线、索引、权重和 Bind Pose，避免旧网格缓存。
- 原验证场景存储的是旧角色副本；新增验证场景直接引用正式 Prefab，运行时门禁曾正确
  拦截未包含胸针的旧场景。当前启动复测入口已改为新场景。
- 任意新动作仍需复验；本轮间距指标是指定帧的采样深度差，不代表所有姿态的完整碰撞证明。

## Phase Provenance V002 工作汇报：冷月版权与发布依赖整改

### 一、工作完成情况

1. 新增普通 Prefab
   `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/V002/Prefabs/Lengyue_GameCharacter_V002.prefab`，
   不再继承供应商 Prefab。
2. 将 11 个生产 Renderer 的网格、材质和纹理复制为 V002 自有 GUID；删除
   24 个禁用旧衣服/头发 Renderer、Inactive `faceFlat` 和供应商 Demo 脚本。
3. 新建只含 BodyOnly Idle/Walk/Run 的 `Lengyue_Locomotion_V002.controller`，
   保留现有应用使用的 Animator 参数名。
4. 新增 `LengyueV002ReleaseBuilder`，自动构建、依赖审计、重复构建清理和 Android
   审计包构建。
5. 新增 `LengyueV002RuntimeProbe`，验证 Idle/Walk 状态、裙摆、头部与头发物理。
6. 在 MateSoul 与 `virtual-friend` 的 `StreamingAssets` 中加入第三方来源声明；
   V001 Delivery 与 AnimatorKit 增加 Internal-only 禁止再分发标识。
7. 原 `Assets/SapphiArt` 与 V001 均未修改或删除。

### 二、编译与测试情况

- Unity 2022.3.38f1 C# 编译通过。
- 静态来源报告：`passed=true`、普通 Prefab、32 项依赖、
  `vendorDependencies=[]`。
- 保留 11 个 Renderer、116 根身体骨、247 根头发骨、22 个表情通道。
- PlayMode：Animator 已赋值，Idle -> Walk -> Idle 通过，裙摆/头部保护初始化通过，
  头发平衡档实际模拟 68 根骨。
- Android 审计 APK 构建成功；声明文件已位于
  `assets/THIRD_PARTY_NOTICES.txt`。
- APK 无供应商 Prefab/FBX/材质/纹理/动画 payload；两个 Unity 全局元数据文件仍含
  未引用供应商 MonoBehaviour 类名，因为该源脚本仍位于工程 `Assets`。
- 审计 APK 与临时解包目录已清理，只保留 JSON/Markdown 报告。

### 三、技术债务与后续建议

1. Asset Store 获取记录、席位和最终法务审查无法自动化，发布主体仍需归档证据。
2. 不得把 V001/V002、FBX、Blend 或 AnimatorKit 作为可复用资产包公开分发。
3. `virtual-friend` 为 Built-in Render Pipeline，V002 为 URP；为避免粉色材质和 XR
   编译风险，本轮未替换其 Build Scene，只加入随包声明。
4. `scripts/build_lengyue_v002_clean_player.sh` 可在 CI 中创建排除
   `Assets/SapphiArt` 的隔离工程；本机第二个 headless Unity 无独立许可证，未完成
   该变体构建验证。
5. 正式 Demo 应改用 V002，并在真实游戏场景完成 PICO 真机性能与 APK/AAB 复审。

## Phase Motion Preview 工作汇报：Unity Walk/Run 动态视频

### 一、完成内容

- 新增 `LengyueMotionVideoRecorder`，在 Unity PlayMode 中以 30 FPS 直接驱动
  `Lengyue_Walk_BodyOnly` 和 `Lengyue_Run_BodyOnly`，每段录制 5 秒。
- 新增 `LengyueMotionVideoCaptureBuilder`，临时创建独立录制 Scene，引用正式
  `Lengyue_GameCharacter_V001.prefab`，不会覆盖当前打开的 V002 Scene。
- 固定 1280 × 720 三分之四正面机位，保留无描边 Toon、胸针、头发物理、
  `SkirtPoseGuard` 与 `HeadPoseGuard` 的实时效果。
- PNG 序列编码为 H.264/yuv420p MP4 后已清理，仅保留视频、JSON 报告和 SHA-256。

### 二、验证结果

- Walk：150 帧、5 秒、30 FPS，跟踪手部位移范围 `0.09522 m`。
- Run：150 帧、5 秒、30 FPS，跟踪手部位移范围 `0.25811 m`。
- 两段视频均为 1280 × 720，ffmpeg 全帧解码无错误。
- PlayMode 门禁：头发物理、裙摆保护、头颈保护、胸针、Toon 材质均存在。
- 首轮静态录制被位移门禁拦截；修正动画执行顺序后重录并通过。
- 误生成的 Android APK、ADB 脚本和 DeviceCapture 资产均已清理，本交付不涉及
  PICO 真机或 Android 构建。

### 三、交付路径

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Release/Lengyue/V001/motion_preview/Lengyue_Walk_UnityPlayMode_30fps.mp4`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Release/Lengyue/V001/motion_preview/Lengyue_Run_UnityPlayMode_30fps.mp4`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Release/Lengyue/V001/motion_preview/MotionCaptureReport.json`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Release/Lengyue/V001/motion_preview/SHA256SUMS.txt`

## Phase Wardrobe V004 工作汇报：参考图双层吊带裙改造

### 一、完成内容

- 基于用户参考图建立非破坏性 `LayeredDress v004`，原 V002、116 骨身体骨架、
  Rest Pose、头颈修复、头发和动画均未修改。
- 上身改为平直胸口吊带裙，新增两根实体肩带、布包纽扣和短袖荷叶边。
- 裙摆改为真实上下两层网格；两层共享对应环的蒙皮权重，避免运动时反向扭转。
- 按用户补充要求保留原腰间蝴蝶结和结心；垂绳仅向外贴合新裙面，防止被遮挡。
- 参考图不含胸针，因此 V003 服装变体移除 `Lengyue_StarBrooch`。
- 服装继续使用无描边 `MateSoul/Character Toon`，提高阴影柔度和
  `DiffuseWrap`，降低布料棱角感。
- 新增 Blender 构建脚本、Unity V003 Prefab、验证场景、运行时 Probe、截图和
  Walk/Run 视频。

### 二、编译与测试情况

- Blender：56,912 顶点、113,128 三角面；最大权重归一误差
  `4.2841e-8`，FBX 回读最大表面误差 `4.1650e-9 m`。
- Unity：64,574 导入顶点、113,128 三角面；重绑定最大误差
  `7.2151e-7 m`，116 根身体骨和 Rest Pose 保持不变。
- 指定表面几何门禁共 56 帧：
  - 上层裙摆—腿部：0
  - 下层裙摆—腿部：0
  - 上下裙层：0
  - 外露袖口—手臂：0
- 头颈 55 帧、14 对边界点，最大间隙 `3.8573e-7 m`
  （约 `0.000386 mm`）。
- PlayMode：Walk/Run 各 90 帧；Toon、头发物理 68 根活动关节、
  Skirt/Head Guard 均通过，`meshRenderingErrors=0`。
- 两段 MP4 均为 960 × 960、30 FPS、90 帧、3 秒，H.264 全帧解码通过。

### 三、问题与修复

- 初版双层裙在大幅奔跑时层间扭转：改为上下层共用前/后裙骨权重场。
- 初版袖口受 Chest 与 UpperArm 混合牵拉产生尖角：外露袖口统一改为上臂连续权重。
- 初次 PlayMode 录制中服装未渲染：根因是对顶点布局已变化的 Mesh 资产执行
  `CopySerialized`，旧 GPU vertex stride 未刷新。修复为重建标准顶点流并删除后
  重新创建 Mesh 资产，同时新增运行时日志门禁。
- `bodiceShirtIntersections` 和隐蔽袖根搭接是裙装覆盖 T 恤的设计层叠，不作为
  “全内部表面零交叉”结论；报告明确区分验收面和信息面。

### 四、交付路径

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v004/`
- `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/V003/Prefabs/Lengyue_GameCharacter_V003.prefab`

### 五、后续建议

1. 当前完成 Unity Editor PlayMode 验证，尚未进行 PICO 真机 GPU/双眼检查。
2. 新服装为 113,128 三角面，接入正式空间场景前应做整角色 GPU 和 draw-call 预算。
3. V003 是 V002 的服装变体，正式替换启动角色前仍需在目标游戏场景复测相机、
   IK、灯光和交互。

## Phase Wardrobe V004.1 工作汇报：裙身与腋下收身修正

### 一、完成内容

- 根据视觉反馈移除裙身中段过量鼓包，腰围保持不变，中段侧向收紧
  `39.37 mm`、前后收紧 `35.51 mm`，下摆侧向/前后分别收紧
  `17/10 mm`。
- 吊带裙腋下侧片由额外外扩改为定向内收，最终设计间隙约 `5.24 mm`。
- 保持平直胸口、肩带纽扣、双层裙摆、腰间蝴蝶结、116 骨和 Rest Pose 不变。
- 裙摆蒙皮改为前中共享骨到左右 FrontSide 骨的连续权重过渡，使收身轮廓在
  单腿抬起时只释放对应侧面，避免整片裙摆推动另一条腿。
- Blender、FBX、Unity V003 Prefab、静态证据图和 Walk/Run 视频均已覆盖更新。

### 二、编译与测试情况

- Blender 构建、116 骨校验、权重归一、蝴蝶结几何 SHA-256 和 FBX 回读通过。
- Unity 重新导入及 V003 重绑定通过，最大误差 `7.2151e-7 m`。
- 56 个姿势中，上下裙摆—腿部、上下裙层、外露袖口—手臂相交均为 0。
- PlayMode Walk/Run 各 90 帧，动作状态到达，68 根头发关节正常模拟，
  `meshRenderingErrors=0`。
- 两段 960 × 960、30 FPS、3 秒 MP4 全帧解码通过。

### 三、踩坑与后续建议

1. 对统一前裙骨整体增加旋转会把裙摆推向另一条腿，虽然降低奔跑峰值，却会引入
   新的步行交叉，因此该实验已撤回。
2. 收身裙需要“静态版型收紧”和“动态局部释放”同时成立；最终采用连续权重过渡，
   不能退回硬切左右分区或只扩大裙身。
3. 当前仍建议在 PICO 真机完成双眼视觉和 GPU 预算复测，但 Unity 资产准出门禁已通过。

## Phase Wardrobe V004.2 工作汇报：袖口粉紫强调色

### 一、完成内容

- 保留短袖主体与袖口压线的深紫色，仅将外露荷叶边切换到独立粉紫 Atlas 分区。
- 粉紫混合比例从袖根 `10%` 平滑增加到外缘 `32%`，以深紫为主并通过轻微色差
  强调花边层次。
- 新建 V004 独立纹理
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/V003/Runtime/Textures/LavenderCasual_Atlas_v004.png`，
  V002 原 Atlas 未修改。
- Unity 构建器显式绑定 V004 Atlas，PlayMode 探针新增纹理名称门禁。

### 二、验证结果

- Blender/FBX 构建、Unity 重绑定和 V004 纹理绑定均通过。
- 56 姿势服装几何门禁保持通过，指定外露表面相交数均为 0。
- Walk/Run 各 90 帧，Toon、头发物理和姿势保护正常，`meshRenderingErrors=0`。
- 两段 MP4 重新生成并通过全帧解码。

### 三、设计约束

粉紫只作为荷叶边的层次色，不能替换 T 恤主体深紫。当前复用 Atlas 第 7 区以维持
单材质和单 draw call；布包纽扣与垂绳端点会获得同一组轻量协调色。

## Phase Wardrobe V004.3 工作汇报：袖口粉紫对比增强

- 根据二次视觉反馈，将荷叶边粉紫混合比例由袖根 `10%`、外缘 `32%` 调整为
  袖根 `16%`、外缘 `42%`。
- 同时轻微提高目标色红色分量，增加与深紫袖身的视觉区分；外缘仍保留
  `58%` 深紫基底。
- V004 独立 Atlas、Blender、FBX、Unity V003 Prefab、截图和 Walk/Run 视频均已更新。
- 56 姿势几何门禁、PlayMode 纹理绑定、实时渲染与视频全帧解码继续通过。

## Phase Wardrobe V004.4 工作汇报：袖口粉紫二次增强

- 根据最终视觉反馈，将荷叶边粉紫混合比例进一步调整为袖根 `20%`、外缘 `48%`。
- 粉紫目标色调整为线性 RGB `(0.80, 0.34, 0.61)`，外缘仍保留 `52%` 深紫基底。
- 已重新生成 V004 Atlas、Blender、FBX、Unity V003、截图与 Walk/Run 视频。
- 56 姿势几何、运行时纹理绑定、实时渲染和视频解码全部通过。

## Phase Wardrobe V004.5 工作汇报：可感知袖口色差

- 确认上一版成品中深紫与粉紫 Atlas 的平均 RGB 仅约相差 `(27, 0, 3)`，
  Toon 渲染后视觉变化不足。
- 将荷叶边调整为明确的玫粉紫：袖根混合 `38%`、外缘混合 `68%`，短袖主体和
  袖口压线继续保持深紫。
- 新增构建门禁：粉紫分区相对深紫分区的红色均值差必须大于 `0.16`，综合色距
  必须大于 `0.18`。
- 最终线性综合色距为 `0.2224`；几何、PlayMode、视频解码均通过。

## Phase Wardrobe V004.6 工作汇报：72 姿态穿模压力测试

### 一、测试扩展

- 将 Unity `BakeMesh` 样本从 56 个扩展至 72 个：Idle 1、Reference 1、
  Walk 33、Run 21、人工压力姿态 16。
- 压力姿态覆盖举臂、前后伸臂、垂臂、屈肘、左右高抬腿、左右弓步、侧劈、
  交叉步、屈膝和宽距深蹲。
- 改用骨段目标方向生成压力姿态，避免角色根节点朝向导致测试名称与实际姿态错位。
- 新增衣身/裙摆与外露手臂交叉指标，并排除短袖内部被遮挡的上臂皮肤误报。

### 二、测试结论

- 总计 72 姿态：63 通过、9 失败，严格门禁总状态为 `passed=false`。
- Walk `33/33`、Run `21/21` 全部通过；双层裙摆在全部姿态中互穿数为 0。
- 失败集中于 Idle 手扶腰、垂臂贴裙、深屈肘、右高抬腿、左右弓步、侧劈和
  宽距深蹲；右高抬腿在单姿态与组合姿态中重复出现。
- Unity PlayMode 的 Walk/Run 各 90 帧继续通过，`meshRenderingErrors=0`。

### 三、交付与后续

- 新增 `reports/multi_pose_summary.json` 和 `evidence/penetration_findings.png`。
- 后续修复应优先处理正常 Idle 手扶腰，再处理高抬腿/弓步；侧劈与宽距深蹲
  建议通过裙摆运行时碰撞或动作限幅解决，不建议单纯扩大静态裙体。

## Phase Wardrobe V004.7 工作汇报：高举手臂肩袖形变优化

### 一、问题与修复

- 原肩袖只在 `Chest` 与 `UpperArm` 之间跨约 8 cm 混合，上举时产生翼状折片。
- 将 4452 个短袖肩区顶点迁移到身体 `Casual_Skin_arms` 的连续权重场，引入
  `Shoulder` 和 `UpperArmTwist`，过渡范围为 Rest Pose `|X|=0.090–0.125 m`。
- 保留 116 骨、Rest Pose、短袖几何和粉紫荷叶边；没有增加运行时修正骨。
- 放弃了过度偏向上臂的第二版权重，因为其侧视图会产生腋下开口。

### 二、量化结果

- 上举肩袖 `P95` 面积拉伸：`4.186× → 2.154×`，下降 `48.5%`。
- 上举肩袖 `P95` 边长拉伸：`4.618× → 1.962×`，下降 `57.5%`。
- 最大边长拉伸：`14.132× → 5.169×`。
- 上举姿态外露袖口—手臂穿模为 0，肩袖独立门禁通过。
- Walk/Run PlayMode 各 90 帧通过，`meshRenderingErrors=0`。
- 72 姿态既有结论保持 63 通过、9 失败；未新增失败姿态。

### 三、交付

- 新增 `reports/shoulder_deformation.json`。
- 新增 `evidence/shoulder_overhead_views.png` 正、侧、背和细节证据。

## Phase Wardrobe V004.8 工作汇报：吊带纽扣比例优化

- 两枚布包纽扣最终等比放大 `80%`，直径由 `12.6 mm` 调整为 `22.68 mm`；
  中间版 `14.49 mm` 和 `16.38 mm` 均已被覆盖。
- 最终直径约为吊带宽度的 `1.67` 倍，形成明确的连接处装饰焦点。
- 纽扣中心、吊带间距和胸口轮廓未改变；缝线同步等比放大。
- Blender/FBX 回读、Unity V003 导入、72 姿态、肩袖形变门禁与 PlayMode
  Walk/Run 均完成回归，未新增失败姿态或运行时渲染错误。

## Phase Wardrobe V004.9 工作汇报：冷月生产资产统一

### 一、资产收敛

- 唯一生产入口调整为：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Prefabs/Lengyue_GameCharacter.prefab`。
- 将新衣服、116 骨角色、动画、身体与头发网格、Toon 材质、贴图及 Pose Guard
  归并到同一 `Lengyue/Runtime` 树；Unity `Assets` 内只保留一个冷月 Prefab。
- 新 Prefab 沿用 V003 已验证 GUID `ef0be3f2d0f904d0789c8483c7dd5e6c`。
- V001/V002/V003 旧 Unity 目录已移出 `Assets`，保存在
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/legacy_unity_assets/`，
  不再参与 Unity 导入或运行时选型。

### 二、构建链路

- `LengyueLayeredDressBuilder` 改为从统一 Prefab 自身幂等重建，不会重新生成旧服装。
- 构建门禁新增：生产 Prefab 数量必须为 1，旧版本依赖与原厂目录依赖必须为 0。
- 修复空白未保存场景下无法创建 Additive 工作场景的问题。
- PlayMode 探针启用后台运行，避免编辑器失焦后录帧停滞。

### 三、验证结果

- Unity 构建报告通过：116 骨、Rest Pose 保持、星形胸针移除、旧路径依赖为 0。
- Walk/Run 各 90 帧通过，头发物理 68 关节、540 子步，渲染错误为 0。
- 72 姿态回归保持 63 通过、9 个已知极端姿态失败，迁移未新增穿模。

## Phase Wardrobe V004.10 工作汇报：腰间丝带比例优化

- 腰间横向褶皱丝带的可见高度由约 `16 mm` 增加到 `19.2 mm`，增幅 `20%`。
- 以原 `1.098 m` 中心线向上下均匀扩展，没有改变腰线位置。
- 蝴蝶结环、结心和垂带几何保持不变。
- 统一 Prefab 重建通过；72 姿态结论未变化，肩袖门禁、Walk/Run 和实时渲染
  回归通过。

## Phase Appearance HairLip 工作汇报：头发条带高光与下唇淡粉色

### 一、完成内容

- 在统一 `MateSoul/Character Toon` Shader 中增加仅头发材质启用的各向异性条带
  高光及三档明暗。使用蒙皮后的 UV 切线方向，保持原紫色发型与无描边风格。
- 高光最终形状参数为粗糙度 `0.10`、各向异性 `0.72`、强度 `0.16`；
  关闭头发原有轮廓光与普通圆形高光，避免油亮感。
- 下唇按 Unity 面部网格实际 UV 弧线混入淡粉色，最大混合 `30%`，只改
  `679` 个像素；保持上唇、嘴线和周围皮肤。Blender 内嵌贴图同步完成。
- 生产入口与 GUID 沿用统一 `Lengyue_GameCharacter.prefab`，材质直接生效。
- 新增隔离场景截图工具 `LengyueAppearanceReview`，扩展现有 PlayMode 探针的
  头发模式门禁与近景录制。交付位于
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/HairLip/`。

### 二、验证

- Unity 2022.3.38f1 / Metal 编译通过，5 个相机方向、3 个主光方向、微笑与
  张嘴近景检查完成。
- 实际 Walk/Run 各 90 帧，Animator 状态到达；手部位移 `0.0952/0.2581 m`，
  头发物理 68 关节、540 子步，渲染停止错误为 0。
- 两段视频均为 960×960、30 FPS、3 秒，全帧解码通过。
- SHA-256 比较确认 Runtime 只改 Shader、头发 Material、面部 Texture 三项；
  Prefab、Mesh、骨架、权重、动画与服装未变。固定相机的衣服与眼部区域像素一致。
- Blender 几何、骨架、权重和 BlendShape 签名保持一致。

### 三、边界与交接

- 新效果的正面、侧面、表情和前后对比以 HairLip 目录为准，旧 Wardrobe 图像仍
  记录当时的服装验收。
- 本轮不重跑几何穿模测试，也未修复原有 9 项失败；其中包含 Idle 手扶腰，
  不能笼统称为全部“极端姿态”。
- Blender 可看到同步后的唇色；头发的精确卡通高光需在 Unity 查看。
- `doc-maintainer` 指定的旧 MateFairy01 路径不存在，继续维护实际 MateSoul 文档。

## Phase Appearance HairEye 工作汇报：发丝条纹与眼睛透亮感

- 根据用户反馈，在上一轮横向高光基础上增加沿发束方向的浅暗细纹；采用原有
  4×4 UV 图集，每束三条连续微弯纹理，根部和梢部淡出，避免细碎噪点。
- 保持眼睛整体紫色和形态，提亮虹膜下部。下部采样区平均亮度由约
  `80.78` 增至 `91.62/255`；主/次高光斑尺寸参数分别放大 `30%/40%`，
  高光中心、纵横比、瞳孔核心与外圈保持不变。
- Runtime 仅更新 `Lengyue_Hair_Albedo.png`、`Lengyue_Eye_Albedo.png`；
  统一 Prefab、几何、动画、Shader、Material 及此前下唇改动均保留。
- Blender 内嵌贴图同步完成，服装构建器已接入相同同步方法，后续重建不会丢失。
- Unity 10 张静态验证图完成；真实 Walk/Run 各 90 帧通过，头发物理
  68 关节、540 子步，渲染停止错误 0；视频帧数、时长与全帧解码通过。
- 同相机中央脸部、下唇与衣服像素保持一致；Blender 几何、骨架、权重、
  BlendShape 签名一致。未重新执行服装 72 姿态检测，原有失败记录保留。
- 最新证据与生成脚本位于
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/HairEye/`。
  原 HairLip 目录作为上一轮记录保留，当前截图菜单改为
  `MateSoul/Lengyue/Capture Current Appearance`。

## Phase Appearance NaturalHighlights 工作汇报：哑光丝绸头发与面部细节

- 根据最新反馈，删除硬阈值月牙形高光，使用三通道 4×4 图集制作三层位置、
  宽度、曲线、倾斜和亮度各异的柔边光带。原发丝纹理保留，横向明暗反差降低
  35%；阴影、中间调和漫反射包裹一起调整，使表面凹凸明暗更柔和。
- 没有叠加法线贴图或镜面环境反射。头发暗部固定区域平均亮度
  `14.48 → 28.46/255`，保持紫色层次。
- 双眼光斑移到眼角外侧，亮区面积 `1932 → 1734 px`，等效直径缩小约 5.3%。
  下部虹膜、瞳孔核心、外圈保持原像素。鼻侧与鼻尖下方补淡阴影，最大混合约
  11.5%，只改面部贴图 549 像素，下唇保持。
- Unity 2022.3.38f1 / Metal：10 张静态图、RGB 高光通道实际导入检查通过；
  Walk/Run 各 90 帧通过，68 个头发物理关节、540 子步，渲染停止错误 0。
  两段 960×960、30 FPS、3 秒视频通过帧数、时长及全帧解码检查。
- SHA-256 确认 Runtime 只改 Shader、头发 Material、三张原贴图，新增高光图集
  及其 meta。Prefab、网格、骨架、权重、BlendShape、动画、衣服保持；
  固定相机下嘴唇、脸颊与衣服像素不变。
- V004 Blender 同步四张内嵌图和哑光近似预览；几何签名保持一致。
  服装构建器已切换到统一 `apply_current_appearance()`。
- 最新结果：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/NaturalHighlights/`。
  HairEye/HairLip 保留历史证据，当前截图入口输出到 NaturalHighlights。
- 技术注意：眼睛 UV 在双眼都为 U 增大朝外，不能简单镜像整个虹膜；原始眼纹理
  存在历史线性编码，需要按实际字节重建局部底色。修改截图工具后应等 domain
  reload 完成再投递请求，避免旧工具把新截图写入旧目录。
- 本轮未重跑服装 72 姿态穿模检测，原有包括 Idle 手扶腰在内的 9 项失败继续保留。
  后续如调整材质，以本轮固定光照对比及实际 PlayMode 结果为基线。

## Phase Appearance ReferenceMatch 工作汇报：参考匹配与最终验收

- 用户明确否定 NaturalHighlights，指定其前一版 HairEye 为当前最佳回滚目标。
  本轮取得并实际查看了原始参考图；连续宽亮带、浅分界及窄明度范围是材质方向，
  不能继续逐束重复三条光带；参考图发色不属于目标配色。
- 已封存完整 HairEye 与 NaturalHighlights 的 Runtime、Blender 源文件和原截图，
  全部有 SHA-256 清单。回滚工具同时支持完整恢复和仅头发恢复；两种路径均实际
  执行验证，源文件同步失败会恢复操作前的资产。
- 双眼亮斑移回内侧并再缩小 10%；亮区 1734→1393 px，等效直径比 0.8963。
  鼻影改为 87 像素的小范围深色鼻尖影，移除长鼻侧影，保留嘴唇和下半虹膜。
- 头发完成 r1–r6 六轮调整，其中 r3–r6 使用同一 Unity 相机和灯光复核。
  用户明确纠正：参考图只用于材质结构与风格，不得改变原角色发色。r6 因此完整
  保留 r5 的连续柔边主亮带、弱上层过渡、浅发丝、平滑胶囊法线和角度/高度 UV2，
  仅把基色从错误的灰蓝紫恢复为 HairEye 原始深紫 RGB `54/23/96`。
- 原 Unity 进程最初未导入文件；显式执行 Assets Refresh 后恢复，无需沿用隔离实例
  的 bee_backend 134 失败。r5 完成 10 张静态图，Shader 错误 0；真实 Walk/Run
  各 90 帧通过，头发 68 关节、540 子步，渲染停止错误 0。
- **当前生产状态是 DeepPurple r6 + ReferenceMatch 材质风格 + 本轮眼鼻修正**。
  Unity 暗部采样 `59.62/28.41/92.20`，接近 HairEye `56.17/21.76/90.18`；
  参考图颜色不再参与发色生成。网格仅改 normals、
  tangents、UV2；位置、索引、UV0、骨架、权重、BlendShape、动画和 Prefab GUID
  保持。HairEye、r3、错误取色 r5 与 r6 均可独立恢复。
- 新入口：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/ReferenceMatch/README.md`。
  两段 960×960、30 FPS、3 秒视频通过帧数和全帧解码；180 张临时帧及隔离工程
  已清理。既有包括 Idle 手扶腰在内的 9 项服装失败未重测。
- 按 doc-maintainer 维护本项目实际文档；其固定指向的 MateFairy01 路径仍不存在。

## Phase Appearance LayeredBands r7d 工作汇报：多层高光与发束立体感修复

- 用户否定 r6 的单一羽化光带与过度平滑发束。重新拆分为四个层次：粗细不同的
  主带、短强调带、上带和下带；边缘过渡固定为 `1.1 mm`，不使用高斯羽化。
- 新增五组纵向亮脊。Shader 以 `max` 而非相加合成横纵高光，避免交叉位置形成
  规则网格或过曝方块；传统镜面仍为 0，保持哑光而非塑料高光。
- 底图恢复 HairEye 原始明暗幅度，并只增强横向高频差值。深紫均值保持
  `53.999/23.031/95.994`；纵向纹理高频 RMS 为 `4.000`，高于 HairEye 的
  `2.666` 和 r6 的 `1.069`，中景仍可辨识束内细纹。
- 法线改为 58% 原始发束法线与 42% 胶囊法线混合；实时阴影强度恢复到 `0.58`，
  阴影软度降至 `0.035`。大束之间重新出现方向性明暗，但未恢复尖锐镜面。
- 完成 r7a–r7d 四轮固定条件 Unity 对比，r7d 前部采样列可分辨 3–4 个独立横带。
  最终 10 个静态视角、两种换光、表情、Walk/Run 各 90 帧通过；Shader/渲染错误
  均为 0，头发物理 68 关节、540 子步。
- Runtime、V004 Blender 源文件、参数、10 视角与生成脚本封存于
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/ReferenceMatch/rollback/selected_LayeredBands_r7d/`，
  SHA-256 清单通过。r6/r7d 完整恢复与仅头发恢复均实际往返验证。
- 当前生产状态为 **LayeredBands r7d + 原始深紫 + 既有眼鼻修正**。参考图只约束
  材质结构，不约束发色。原有 9 项服装穿模失败未重测、未修改。

## Phase Appearance AnimeStrands r8d 工作汇报：逐发束高光逻辑重构

- 用户指出 r7d 的根因仍是“光滑头盔反光”：横向高光与发丝方向垂直。r8d
  完全删除头部空间横带，改用 UV0 4×4 图集逐发束生成高光。
- 16 个发束分区各自拥有长度、宽度、偏移、弯曲和强度不同的纵向尖角主块，
  G 通道为窄亮芯，B 通道为 1–2 个小菱形碎光。Shader 让碎光使用独立冷紫色
  叠加，避免被主块吞没；常规镜面仍为 0。
- 暗部颜色改为 `(0.30,0.22,0.43)`，阴影接收提高到 `0.72`，软度降至
  `0.022`；原发束法线权重提高到 76%。底图保持 HairEye 深紫均值
  `54.010/23.028/96.001`，纵向纹理高频 RMS 为 `4.210`。
- 眼睛贴图直接恢复
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/ReferenceMatch/rollback/preferred_HairEye/Runtime/Textures/Lengyue_Eye_Albedo.png`，
  SHA 对比逐字节一致；一大一小高光的尺寸、位置和虹膜全部匹配 HairEye。
  当前 87 像素深色鼻尖影继续保留。
- r8a–r8d 固定条件 Unity 对比完成。最终 10 个静态视角、两种换光、Walk/Run
  各 90 帧通过；Shader/渲染错误均为 0，头发物理 68 关节、540 子步。
- Runtime、V004 Blender、参数、生成脚本和 10 视角封存于
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/ReferenceMatch/rollback/selected_AnimeStrands_r8d/`。
  r7d/r8d 完整恢复与仅头发恢复均实际往返验证。
- 当前生产状态为 **AnimeStrands r8d + HairEye 原始眼睛 + 紧凑鼻尖影**。
  r7d 降级为历史回滚；原有 9 项服装穿模失败未重测、未修改。

## Phase Animation Yummy 工作汇报：骨骼动作应用与表情 FBX 诊断

- 将动画师提供的两个 FBX 复制到项目内只读输入目录并记录 SHA-256；微信原文件、
  V004 生产 Blender 源文件均未修改。
- `Yummy_Action_InPlace.fbx` 为 30 FPS、2–174 帧、399 根动画骨骼和 3990 条
  FCurve 的骨骼动作。早期 Blender 拆分与预览结果被用户确认不符合预期，
  **不得作为动作正确性基准**，仅保留为失败诊断历史。
- 动画师补充 223 帧/7.433 秒实际预览后确认旧 Unity 视频严重错误。根因是把
  早期 Rig 的绝对 Local Translation 直接写入当前骨架，导致头颈分离；而全量
  Rest-delta 又会把“动画首帧默认姿态”错误清零为 T Pose。
- 最终映射为 `UnityAbsoluteRotation_TargetRestTranslation`：保留动画师 Unity
  曲线的绝对 Local Rotation，位置和缩放只迁移动画增量到生产 Rest Pose。
  `Lengyue_Yummy_BodyOnly.anim` 包含 67 根目标身体骨、670 条曲线、无缺失骨。
  头发、裙摆、IK/CTRL/DRV/MCH 与眼球曲线剔除，避免覆盖运行时物理和缺失表情。
- `Yummy_BlendShape_InPlace.fbx` 经 Blender 导入及原始 FBX 节点解析双重确认：
  只有 12 个静态 Mesh 和 Bob Hair 对象的 6 条位移/旋转曲线，`Shape`、
  `Deformer`、`BlendShapeChannel` 均为 0，无法恢复任何眉眼或嘴型动画。
- Unity 使用生产 Prefab 与 `MateSoul/Character Toon` 渲染；头发由
  `HairSpringPhysics` Balanced 模式接管，实际模拟 68 个关节/510 子步，
  最大弯曲 18°、发梢相对头部位移 0.0817 m；裙摆由 `SkirtPoseGuard` 接管，
  最大骨骼偏转 22.84°，左右腿最大屈曲 29.19°/26.58°，渲染停止错误 0。
- 最终使用 macOS Unity Standalone Player 的真实 Player Loop 验证，左右手移动
  0.4702 m/0.5813 m，头颈距离最大比例偏差仅 `0.000002861`，杜绝 T Pose 和
  头部脱离的假通过。动画师参考帧 30/60/90/... 与 Unity 帧 0/30/60/... 对齐，
  对照证据为 `evidence/Yummy_Reference_vs_Unity_aligned.png`。
- 最终视频为
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_Toon_RuntimePhysics.mp4`：
  H.264、960×960、30 FPS、173 帧、5.7667 秒，全帧解码及 173 个独立帧通过。
- 主工程保留 Unity FBX、生成 Clip、验证场景与自动录制脚本。锁屏下 Editor
  Player Loop 不推进，因此构建 macOS Standalone 验证程序完成真实运行时录制；
  Standalone 构建、隔离项目及 173 张临时帧在验证后清理。
- 动画师重导要求、目标 Shape Key 名单和底层证据已写入
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/compatibility_report.md`。
  收到包含 22 个非 Basis Morph Target 及权重曲线的新 FBX 后，再合成正式表情版。

## Phase Animation Yummy Facial 工作汇报：参考表情重建与物理增强

### 一、功能完成情况

- `Yummy_BlendShape_InPlace.fbx` 仍确认不含任何 Shape、Deformer 或
  BlendShapeChannel；本阶段没有伪称恢复原曲线，而是以动画师预览为基准，在生产
  角色现有 Shape Key 上重建 9 条可追溯的眉眼嘴曲线。
- `Lengyue_Yummy_BodyOnly.anim` 现包含双眼闭合、睁大、悲伤眉和轻量嘴型，
  同时恢复 `Eye_L/Eye_R` 骨骼绑定。源眼球旋转实测为常量，因此不额外伪造视线。
- 身体映射由 67 骨/670 曲线增至 69 骨/690 曲线，头发、裙摆和控制 Rig 曲线
  仍被排除，避免覆盖运行时二级运动。
- Yummy 场景头发改为 `Full`、最大弯曲 `24°`；额前 `H_Fringe_*`、
  `H_FringeFine_*`、`H_Detail_*` 固定，防止快速抬头时刘海卷曲。侧发和后发
  实际模拟 108 个关节。
- `SkirtPoseGuard` 新增响应倍率和响应角度上限；Yummy 场景使用 `1.45x/42°`，
  生产 Prefab 默认仍为 `1.0x/90°`，不改变其他动画。

### 二、编译与验证

- Unity 2022.3.38f1 macOS Standalone 构建成功：0 errors、11 warnings。
- 173 帧真实 Player Loop 验证通过，10 个 Renderer 全部使用
  `MateSoul/Character Toon`，渲染停止错误为 0。
- 面部峰值：闭眼 `100/100`、睁大眼 `92/92`、悲伤眉 `72/72`、嘴型 `24`。
- 物理峰值：头发最大弯曲 `24.0001°`、发梢相对位移 `0.3563 m`；裙摆最大
  骨角 `32.8541°`，较旧版 `22.8441°` 明显提高。
- 左右手运动 `0.4702/0.5813 m`，头颈距离比例偏差
  `0.0000028613`，未回归 T Pose 或头部脱离。
- 最终 H.264 视频为 960×960、30 FPS、173 帧、5.7667 秒，173 个解码帧
  均独立；SHA-256 为
  `dce2ff2741025dfce4f252bda6770c182695b9863dffcfecbae040b9c520e4e1`。

### 三、交付与边界

- 最终视频：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_Toon_Facial_EnhancedPhysics.mp4`
- 表情接触表：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/evidence/Yummy_Unity_Facial_EnhancedPhysics_contact.png`
- 运行时报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/unity_playmode.json`
- 当前表情是参考视频重建版。若动画师重新导出有效 Morph 曲线，应以原始曲线替换
  这 9 条重建曲线；骨骼重定向和物理配置可以继续复用。

## Phase Animation Yummy Facial v2 工作汇报：表情时序与头发分区调优

### 一、问题修复

- 逐 5 帧重查动画师参考视频，确认参考帧 135 仍闭眼、140 已睁眼。按
  `referenceFrame = unityFrame + 30` 将完全睁眼时机从 Unity 114 帧提前到
  110 帧，与身体抖动同步。
- 新增 `face_Puff_BS`，在进食后建立 35–126 帧的鼓腮与渐退曲线，运行时峰值
  为 72。
- 新增 `face_jaw_open_BS` 和 `face_E_mouth_BS`，在鼓掌阶段建立三组张嘴/闭嘴
  脉冲。运行时验证为 3 个张嘴采样、3 个闭嘴采样，峰值分别为 34/18。
- 面部重建曲线由 9 条增至 12 条，`unity_clip_build.json` 门禁同步改为
  `facialBindings == 12`。

### 二、头发分区调优

- 全局弯曲上限由 24° 降至 20°。
- 每束 `H_Fringe_*` 仅开放最后两节，并使用 `stiffness >= 1.6`、
  `drag >= 0.72` 和 40% 重力；刘海相对位移为 0.0257 m，可见但不卷起。
- `H_SideL_*`/`H_SideR_*` 只保留 Balanced 末端关节，并提高阻尼与回正力；
  左右侧发相对位移为 0.0799/0.0806 m，关键帧未再穿脸。
- 总模拟关节由 108 收敛至 98；后发仍保留 Full 动态，裙摆维持
  `1.45x/42°`。

### 三、验证与交付

- Unity 2022.3.38f1 macOS Standalone：0 errors、11 warnings。
- 173 帧真实 Player Loop 验证通过，10 个 Toon Renderer、0 个渲染错误，
  头颈距离比例偏差 `0.0000028613`。
- 最终视频继续写入
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_Toon_Facial_EnhancedPhysics.mp4`，
  SHA-256 为
  `1737b37b68b17cb2af3b3b4b4043f4ec7dc066a83684836c4bdc944fea51b16c`。
- 新版关键帧接触表：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/evidence/Yummy_Unity_Facial_Hair_Final_contact.png`。

## Phase Animation Yummy Hair v3 工作汇报：整链限幅与重力侧倾

### 一、问题与实现

- 定位后发过摆根因：原求解器的 `maxBendDegrees` 只限制相邻骨骼，长发链可逐节
  累积偏转，无法约束整条发束。
- `HairSpringSolver` 新增随 Hair Root 旋转的参考姿态，并对每个活动段同时执行
  `20°` 局部限制和 `12°` 根姿态相对限制，碰撞修正后也重新套用双重限制。
- `HairSpringPhysics` 新增 `maxChainBendDegrees`、`MaxChainAngle`，将整链上限暴露
  给场景配置和运行时验收。
- 后发参数调整为 `stiffness >= 0.9`、`drag >= 0.58`、
  `gravity >= 0.22`；侧发重力为 `0.12`，刘海重力为 `0.14`。重力始终使用
  世界向下方向，头部倾斜后会产生相对头部的侧向下垂。

### 二、验证结果

- 后发发梢运动范围由 `0.3361 m` 收敛至 `0.0994 m`。
- 实测整链最大偏角 `12.0001°`，未突破 `12°` 配置上限。
- 动画内后发左右运动范围 `0.0207 m`；固定姿态向左右各倾斜 `20°` 时，沿新增
  局部重力方向的响应分别为 `0.0165/0.0162 m`。
- 刘海范围 `0.0174 m`；左右侧发范围 `0.0384/0.0386 m`，保持可见轻摆且关键
  帧无穿脸。
- Unity 2022.3.38f1 Standalone 构建成功，173 帧 Player Loop、10 个 Toon
  Renderer、0 个渲染错误，最终门禁通过。

### 三、交付

- 最终视频：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_Toon_Facial_EnhancedPhysics.mp4`
- 头发关键帧：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/evidence/Yummy_Unity_Hair_ChainLimit_contact.png`
- 视频 SHA-256：
  `4ea98e319c1f8834e537e310e823669e5ad40656b484eb10398d34a14fac314a`

## Phase Animation Yummy Hair v4 工作汇报：整链上限提高 30%

- 按用户要求将头发根姿态相对整链上限从 `12°` 提高 30% 至 `15.6°`；单关节
  `20°` 上限、分区阻尼、回正力和世界重力参数保持不变。
- 同步修改 `HairSpringPhysics` 默认值、Yummy 构建配置和运行时验收常量，
  避免新建组件、场景重建和 Player 门禁之间出现配置偏差。
- Unity 2022.3.38f1 Standalone 构建成功。173 帧真实 Player Loop 中整链峰值
  `15.6001°`，后发总范围 `0.1264 m`、左右范围 `0.0269 m`；左右重力倾斜响应
  `0.0204/0.0200 m`，运行时门禁通过且渲染错误为 0。
- 刘海范围为 `0.0214 m`，左右侧发范围为 `0.0490/0.0491 m`。最终视频保持
  960×960、30 FPS、173 帧，173 个解码帧均独立。
- 最终视频 SHA-256：
  `a5d33205ea94297cb5a8a5308c633eb852d9f94bf85e17d20537f3d0172f61c5`

## Phase Animation Yummy Hair v5 工作汇报：前脸刘海动态增强

- 前脸刘海由每束仅 `_05/_06` 两节参与模拟，调整为 `_04/_05/_06` 三节参与；
  模拟关节总数由 98 增至 105，侧发、后发和裙摆配置保持不变。
- 刘海局部参数调整为 `stiffness >= 1.45`、`drag >= 0.66`、
  `gravity >= 0.17`，继续受单关节 `20°` 和整链 `15.6°` 双重限制。
- 运行时门禁将刘海最低相对位移提高至 `0.028 m`，并增加 `0.06 m` 上限，
  防止为了可见性重新引入卷曲、遮眼和穿额头。
- Unity Standalone 173 帧验证中，刘海范围由 `0.0214 m` 提高到 `0.0431 m`；
  后发 `0.1264 m`、左右侧发 `0.0490/0.0491 m` 均未变化。关键帧复核无卷曲、
  遮眼或发束断层，运行时门禁通过且渲染错误为 0。
- 最终视频 SHA-256：
  `4d83ef7ded5634c37f749484829c3d3b95fcf1d6c05724a25a1a90fded0c3b4d`

## Phase Animation Yummy Hair/Cloth v6 工作汇报：横向惯性与裙摆二级运动

### 一、头发方向修正

- 原求解器只控制三维总摆幅，无法保证视觉运动主要发生在左右方向。本轮为每个
  发丝关节新增横向惯性、横向重力和纵深响应系数，并使用头骨相对静止姿态校准
  角色方向，避免导入骨骼绑定轴与画面方向不一致。
- 圆锥限幅前新增方向整形：增强参考坐标 X 偏移、压缩 Z 偏移，再重新执行
  `20°/15.6°` 双重硬限制和碰撞修正。
- 刘海横向/前后范围为 `0.0399/0.0134 m`；左右侧发横向分别为
  `0.0460/0.0446 m`，前后仅 `0.0112/0.0132 m`。横向成为明确主运动方向，
  且发束总范围未失控。

### 二、裙摆物理升级

- `SkirtPoseGuard` 从仅按腿部屈曲设置固定角度，升级为“腿部避让目标 +
  世界重力目标 + 髋部加速度惯性 + 90 Hz 欠阻尼弹簧”。
- 控制范围由 16 根 `Skirt_*` 扩展到 16 根内层和 14 根 `OuterSkirt_*`，
  外层裙摆不再保持刚性。
- 动画内裙摆弹簧峰值 `2.6763°`、横向弹簧峰值 `0.5317°`、下沿范围
  `0.0360 m`、惯性目标峰值 `4.1368°`。角色左右倾斜 `15°` 时，双向重力
  下垂响应均为 `7.9978°`。

### 三、验证与交付

- Unity 2022.3.38f1 Standalone 173 帧门禁通过，10 个 Toon Renderer、
  0 个渲染错误，173 个解码帧均独立。
- 最终视频：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_Toon_Facial_EnhancedPhysics.mp4`
- 头发/衣服证据：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/evidence/Yummy_Unity_Hair_Lateral_contact.png`
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/evidence/Yummy_Unity_Cloth_Dynamics_contact.png`
- 视频 SHA-256：
  `90177db9ff1e5aee991de745eb634672641da355f748783da95643d652a5faea`

## Phase Animation Yummy Hair/Cloth v7 工作汇报：自然垂坠与受控横摆

### 一、完成内容

- 将头发求解器恢复为 v5 的 Verlet 速度、世界重力和回正力路径，删除 v6 对速度、
  重力和端点偏移的逐步方向缩放，解决发束运动机械、动态效果退化的问题。
- 在 v5 基线上保留头部横向惯性输入，并增加逐关节横向/纵深硬限制。刘海使用
  `14°/5°`，侧发使用 `15°/8°`；侧发额外启用 `_05`，总模拟关节由 105
  增至 113。
- `SkirtPoseGuard` 新增低屈曲静止垂坠姿态：内外侧裙骨分别内收 `14°/17°`，
  中段骨骼径向收缩 `16%`。腿部屈曲达到 `14°` 前平滑释放内收，随后完整恢复
  原防穿模姿态，不以永久外撑换安全距离。
- 裙摆二级运动调整为 `2.6 Hz`、阻尼比 `0.58`、最大 `7°`，末端响应提高到
  `0.48`，保留重力和髋部惯性，同时增加裙片之间的差异化响应。

### 二、验证结果

- Unity 2022.3.38f1 macOS Standalone 构建成功：`0 errors`。
- Standalone Player 完成 173 帧真实回放，10 个 Toon Renderer，
  `meshRenderingErrors=0`，运行时门禁 `passed=true`。
- 刘海总范围 `0.0367 m`，横向/前后范围 `0.0320/0.0160 m`。
- 左右侧发总范围 `0.0670/0.0675 m`，横向范围 `0.0531/0.0534 m`；
  单关节横向上限实测 `15.0000°`，整链上限实测 `15.6001°`。
- 裙摆下沿运动范围 `0.0878 m`，二级弹簧峰值 `3.6683°`。同一动画姿态下，
  高内收状态的横向半径由 `0.1580 m` 降至 `0.1116 m`，前后半径由
  `0.2573 m` 降至 `0.2408 m`；最大避让权重仍达到 `1.0`。
- 12 个全身关键帧复核未见腿部穿出、裙层翻折、刘海遮眼或侧发过摆。
- 最终视频为 960×960、30 FPS、173 帧，全部解码帧唯一；SHA-256：
  `9e195c419edd0d9c074e24e7932c5ce3fc25d6986bf7d415eb41eea82abe9b92`。

### 三、交付与注意事项

- 最终视频：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_Toon_Facial_EnhancedPhysics.mp4`
- 运行时报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/unity_playmode.json`
- 关键帧接触表：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/evidence/Yummy_Unity_Hair_Lateral_contact.png`
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/evidence/Yummy_Unity_Cloth_Dynamics_contact.png`
- 当前“无穿模”结论覆盖 Yummy 173 帧和既有 121 帧压力姿态集；新动画仍需运行
  同类逐帧门禁，不能把有限动作集外推为任意姿态的数学保证。

## Phase Animation Yummy Camera v8 工作汇报：动画师基准机位与 1080p 录制

### 一、完成内容

- 读取动画师基准视频
  `/Users/bytedance/Library/Containers/com.tencent.xinWeChat/Data/Documents/xwechat_files/wxid_0x464dim3jo722_81dc/msg/video/2026-09/7ae61c9d685aec687f09e8c33b8f9e73.mp4`，
  从 Blender 视口坐标轴和关键帧标定相机方向、缩放与构图。
- 将原 `(.55, .96, 3.2)`、约 10° 侧偏的全身机位改为正面正交近景：
  相机位置 `(0.18, 1.41, 3.20)`，目标 `(0.18, 1.41, 0)`，
  `orthographicSize=0.50`。
- 构图对齐后，主体中心约位于画面横向 59%，头顶约 21%，腰线约 80%，对应
  基准视频中 3D 视口的主体位置。
- 捕获从 960×960 升级为 1920×1080；RenderTexture 使用 4× MSAA，
  经显式 Resolve 后读取，避免直接读取多重采样纹理。
- 最终视频使用 H.264 Main Profile Level 4.0、`yuv420p`、CRF 15 编码，
  并加入 AAC-LC 双声道静音轨，兼容要求音轨或不支持 High Profile 的播放器。

### 二、验证结果

- Unity 2022.3.38f1 macOS Standalone 构建成功：`0 errors`。
- Standalone Player 完成 173 帧回放，运行时报告 `passed=true`，
  `meshRenderingErrors=0`。
- 输出规格：1920×1080、30 FPS、173 帧、5.7667 秒；完整解码通过。
- 最终视频 SHA-256：
  `9de381255eabdeec648a736a48214ef7dae53e96f7d378f26907446c2612ef21`。

### 三、交付路径

- 视频：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_Toon_Facial_EnhancedPhysics.mp4`
- 运行时报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/unity_playmode.json`
- 相机和分辨率已写入报告，后续不得再根据窗口尺寸隐式推导构图。

## Phase Animation Yummy Hair Contact v9 工作汇报：刘海表面碰撞与细丝随动

### 一、完成内容

- 新增 `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Runtime/LengyueHairSurfaceContact.cs`。
  从发帽真实三角面构建碰撞代理，在原弹簧求解之后执行刘海表面分离；
  按发束横截面整体推出并沿长度平滑衔接，保持薄片厚度。
- 刘海只沿头部局部前后方向做必要的接触修正。没有降低重力、惯性、摆角、
  横向驱动力或现有运动上限；`HairSpringSolver.cs`、`HairSpringPhysics.cs`
  与修复前副本逐字一致。
- 找到 12 条细丝、60 根非活动骨骼造成的漏随动。运行时将 2952 个细丝顶点
  绑定到相邻主发束的插值蒙皮权重，保留细丝原始形状和偏移。
- 修改 `LengyueYummyUnityReview` 与 `LengyueYummyCaptureProbe`，让编辑器和
  Standalone 捕获都配置相同组件。门禁新增普通运行异常计数、细丝绑定数量和
  173 帧接触求解检查，避免只有“渲染错误为零”却漏掉脚本异常。

### 二、验证与交付

- PlayMode 同环境对照：主骨骼矩阵和主发束横向/竖向位置差均为 0。
- Standalone 完成 173 帧；刘海横摆仍为 3.1959 cm，两侧仍为
  5.3093 / 5.3424 cm。运行错误、蒙皮渲染错误均为 0。
- 每帧独立检查 3502 个刘海顶点及 117012 个三角面采样点。
  本次 Yummy 动作集没有检出刘海进入发帽；第 60 帧原有 377 个穿透顶点降至 0。
- 12 条细丝均从头部局部静止恢复随主发束运动。接触修改写入私有网格实例，
  原 FBX、原始网格、材质和动画文件没有被写入接触形变。
- 最终独立播放器视频：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_HairContact_v9.mp4`。
  保持参考机位、1920×1080、30 FPS、4× MSAA，使用 H.264 Main + AAC。
- 数值报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/hair_surface_contact.json`，
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/unity_playmode.json`。

### 三、适用范围与后续

- 验证覆盖当前 Yummy 173 帧。该碰撞层针对冷月短发的发帽与前刘海，
  不代表已经验证任意动画、手部与头发碰撞、发束互碰或头显实时预算。
- 接触区前后位移会因表面分离而改变，这是消除穿透所需；未接触区的主骨骼运动
  保持原样，不能把“保留物理效果”描述为“碰撞点三维坐标完全不变”。
- 调试会话 `hair-scalp-contact` 保留至用户验收后再清理；原先独立的历史
  调试会话不在本次清理范围内。

## Phase Animation Yummy Sleeve v10 工作汇报：衣袖柔性动态

- 用户要求衣袖更贴近身体，随肢体产生有重力、惯性和轻柔感的布料运动。
- 已建立 `sleeve-soft-dynamics` 独立诊断会话，在
  `LengyueYummyCaptureProbe.cs` 添加只读插桩并完成 173 帧基线。
- 14559 个上衣/袖口顶点的最大额外运动残差仅 0.0000005185 m；
  当前没有衣袖物理组件，只有普通骨骼蒙皮。现有防穿袖处理位于隐藏的
  SkinCoverage，并非运行时外撑衣袖。
- 基线位于 `/Users/bytedance/MateSoul/.dbg/sleeve-soft-dynamics/`。
- 新增 `LengyueSleeveCloth`，240个世界坐标模拟点驱动10623个衣袖顶点；
  肩部固定，袖身和荷叶边共享位移。实现重力、惯性滞后、阻尼回落、
  手臂/胸侧接触，以及屈肘时沿上臂向肩部压褶收拢。
- 动态网格为运行时私有副本，原始服装、骨架和动画不改；组件已接入生产Prefab。
  保持原有头发、裙摆、脸部和1920×1080参考机位。
- 第一轮接触修正存在袖边局部拉扯，通过整片收拢解决。
  Editor单帧边长拉伸比p99的最坏值从3.91降至1.183；
  该算法仍不是严格不可伸长或带自碰撞的完整布料系统。
- 同姿势重力开关验证：袖口平均下垂16.17mm；
  20mm整体平移产生19.76mm初始滞后，停止2秒后残差0.00522mm。
- 袖筒中位半径约53mm收至43mm，肩缝固定误差低于0.5微米（Editor）。
- 独立播放器复核额外发现其默认2骨骼蒙皮与Editor 4骨骼影响数不同；
  已让求解器按实际renderer质量档截断并归一化权重，避免渲染表面与碰撞计算
  不一致，同时不改变共用网格上已认可的裙摆蒙皮。
- 173帧Editor和Standalone门禁均通过，运行/渲染错误为0。
  最终Standalone重力下垂16.514mm、初始滞后19.858mm、2秒回落残差0.00278mm；
  GPU实际顶点与2权重重建误差≤0.557微米。桌面CPU袖子组件平均20.21ms/帧，
  PICO设备预算尚未验证。
- 每帧67163个表面点独立检查，胶囊本体最坏残余穿入0.341mm；
  本次验证不宣称真实人体网格或任意动作完全无穿模。
- 高清成片：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_SleeveCloth_v10.mp4`。
- 同步左右对比（左v9、右v10）：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_SleeveCloth_v10_Comparison.mp4`。
- 两份视频均为1920×1080/30fps/173帧，H.264 Main + AAC，完整解码通过。
  数值与文件校验信息见
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/sleeve_cloth_v10.json`。
- 调试插桩保留至视觉验收；v9对照视频保留。

## Phase Garment Layer Dynamics 工作汇报：整套服装柔性与衣层接触（基线诊断）

- 用户认可v10衣袖物理，但指出衣袖穿进其他衣层，以及吊带、连衣裙、
  内衬和丝带仍僵硬；要求在当前外观基础上升级整套服装的布料动态。
- 用户附图为双手靠脸、头部倾斜的姿势，并指出肩部异常下沉/扭曲。
  内衬、领口和吊带缺少协调随动是待验证假设，不直接当作根因。
- 已建立独立 `garment-layer-dynamics` 诊断记录和4个可证伪假设。
  已完成只读插桩与Editor 173帧基线，尚未修改衣物行为。
- 完整服装共64574个顶点、42个焊接连通部分；两条吊带、领圈、
  纽扣和胸前包边均100%绑定Chest，邻近肩袖却使用肩/上臂混合权重。
  第98帧对应肩带/肩部点相对静态位置最多额外分离18.862mm。
- 外层裙身、下层裙摆、吊带及丝带的网格相对已有骨骼运动残差为0；
  原有裙摆骨骼摆动不等于布片本身已有柔性模拟。
- 三角面检查确认袖子/上衣与裙身相交，但纯蒙皮也有相交，
  不能简单归因于v10轴向收拢。第30帧相交对数806/664（v10/纯蒙皮），
  第98帧508/932；后续按袖口、袖身、缝合区域分别验证。
- 基线数据和v10代码副本位于
  `/Users/bytedance/MateSoul/.dbg/garment-layer-dynamics/`。
  新增整套布料及层间接触仍待实现，最终需Standalone验证。

## Phase Garment Cloth v11 工作汇报：整衣布料与动态衣层接触

> 历史方案：用户于2026-09-28明确要求恢复v10。v11运行时求解器及绑定资源
> 已移除，视频和报告仅保留为历史证据，不代表当前生产状态。

### 一、功能完成情况

- 新增 `LengyueGarmentCloth`，由现有 `LengyueSleeveCloth` 统一驱动同一份
  私有运行时网格，避免多个组件互相覆盖顶点结果。
- 1404个世界空间控制点驱动42024个衣物顶点，覆盖双层裙摆、胸前裙身、
  内衬/上衣中央、左右吊带、蝴蝶结和两条垂绳；包边、纽扣、绳端跟随
  对应主布片，裙身上下缝边固定，中部保留柔性。
- 吊带不再只绑定Chest：运行时继承相邻肩部上衣的混合蒙皮场并保留原始
  建模间距。第98帧肩带相对肩部的最大新增间距由18.862mm降至5.105mm，
  平均新增间距由1.720mm降至0.307mm。
- 保留v10袖子全部重力、惯性、阻尼和屈肘收拢参数；新增128×29真实动态
  胸前裙身外表面和8mm衣层安全层。重点帧60/90/98/104的自由袖口与外袖
  三角相交对总数由1504降至1199（下降20.28%）；缝合覆盖区不混入该指标。

### 二、编译与验证

- Unity Editor与macOS Standalone均完成173帧回放；最终Player使用2个有效
  骨骼影响，运行错误和蒙皮渲染错误均为0，门禁通过。
- 袖子重力下垂14.994mm，20mm平移初始滞后19.841mm，停止2秒后残差
  0.0061mm；未通过减小摆幅规避穿模。
- 9个衣物区域重力响应范围1.140–10.174mm；整衣20mm平移初始滞后
  19.383mm，停止2秒后残差0.0033mm。整衣最大局部柔性位移35.0mm。
- GPU第0/85/172帧与2权重CPU重建最大误差0.568微米。
- 最终视频均为1920×1080、30fps、173帧、H.264 Main L4.0/yuv420p+AAC，
  完整解码通过：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_GarmentCloth_v11.mp4`；
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_GarmentCloth_v11_Comparison.mp4`。
- 数值报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/garment_cloth_v11.json`。

### 三、技术债务与后续

- 三角相交对是保守接触计数，不等于穿入深度；上衣与胸前裙身的设计缝合/
  遮盖区域仍有重叠，不能作为自由布片穿模统计。
- 当前桌面Standalone中袖子与整衣组合求解平均约56.7ms/帧；本阶段优先完成
  视觉升级和证据闭环，尚未通过PICO真机帧预算。后续若进入实时产品链路，
  应将高密度顶点回写和混合蒙皮逆变换迁移至Burst/Jobs或GPU。
- `garment-layer-dynamics` 调试会话在v11交付时保持OPEN；用户随后选择
  恢复v10，该会话现已关闭，历史截图、NPZ和视频仅用于审计。

## Phase Sleeve v10 ShadowFabric 工作汇报：手臂阴影与服装质感优化

### 一、功能完成情况

- 按用户要求回退v11整衣方案。删除 `LengyueGarmentCloth` 运行时脚本、
  JSON绑定资源及其与袖子组件的耦合；当前 `LengyueSleeveCloth.cs` 与
  已验收v10备份逐字节一致，SHA-256均为
  `b38485cd4d10faf17536fb65e8440cebf9486b10b5a19d255ed15bd4a22a36e0`。
- 通过frame 162四组隔离帧确认：关闭手臂材质实时阴影后锯齿不变，
  提高Toon分带柔化后显著改善，Unlit时完全消失。根因是
  `Lengyue_Toon_SkinCoverage` 的低面数/分裂法线场被过窄Toon分带放大，
  不是实时阴影贴图分辨率。
- 仅将手臂SkinCoverage材质 `_ShadowSoftness` 从`.06`调整为`.25`；
  不修改脸部、头发、袖子物理或全局URP阴影预算。frame 162手臂内部
  高频能量较v10基线下降12.17%。
- 为服装Shader增加可平铺织物高度、切线空间法线和纤维边缘光响应。
  新增256×256无缝RGBA织物图，在保持原服装色图、配色和几何不变的前提下，
  增加细密经纬纹理、轻微表面起伏和柔和纤维高光。
- 最终服装参数为Detail Scale 8、Albedo `.06`、Normal `.10`、
  Sheen `.045`、Sheen Power `3.5`；原服装Atlas改为Trilinear和Aniso 4。
- 离线录制保持4×MSAA并增加2×离屏超采样，再缩小到1920×1080，
  改善近景轮廓及材质采样稳定性。

### 二、编译与验证

- Unity 2022.3.38f1 macOS Standalone完成173帧回放，运行错误0、
  渲染错误0，门禁PASS。
- v10袖子指标保持：10623个驱动顶点、2个有效蒙皮影响；
  重力下垂16.514mm、20mm平移初始滞后19.858mm、
  停止2秒残差0.00278mm、最大位移83.239mm。
- 两个视频均为1920×1080、30fps、173帧、H.264 Main L4.0、
  yuv420p+AAC，并通过完整解码。
- 数值报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/v10_shadow_fabric.json`。

### 三、交付物

- 主成片：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_SleeveCloth_v10_ShadowFabric.mp4`
- v10原版/优化版对比：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_SleeveCloth_v10_ShadowFabric_Comparison.mp4`
- frame 162静帧对比：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/evidence/Yummy_v10_ShadowFabric_Frame162_Comparison.png`

### 四、技术债务与后续

- 当前修复通过材质分带平滑低面数法线导致的阶梯感，没有重建手臂拓扑或法线；
  若后续要求更细腻的明暗体积，需评估离线重算法线或增加网格细分。
- 织物效果为通用平铺微表面，不是扫描级PBR材质；当前强度刻意受控，
  避免破坏原有二次元风格和浅紫配色。
- `_DebugUV`、frame 162隔离代码及调试证据保留至用户视觉验收；
  验收后再清理，不提前删除证据。

## Phase Wardrobe v005 工作汇报：收窄粉紫荷叶衣领

### 一、功能完成情况

- 基于当前v10 ShadowFabric角色新增贴颈小荷叶领，仅参考用户图片中的衣领，
  未采用图片中的旧版袖子颜色或修改其他服装造型。
- 衣领使用18个柔和波瓣，开口宽度从原领口约108mm收至84.49mm，
  横向收窄21.77%；Blender Rest Pose最小颈部间隙5.35mm。
- 衣领复用袖口内衬/荷叶边的Atlas tile 7粉紫色及现有织物Shader，
  与用户指定区域同色；短袖主体仍使用tile 1，不新增材质和Draw Call。
- 衣领外侧100%跟随Chest，内侧平滑过渡至45% Neck权重，
  在贴颈感与低头/侧倾稳定性之间取得平衡。
- 新建非破坏性v005源文件和FBX；v004 Blend/FBX保留为回滚基线。
  Unity生产Builder已切换到v005模型，并继续复用唯一生产Prefab。

### 二、验证情况

- Blender构建门禁通过：116根骨骼及Rest Pose不变，v004已有服装网格不变，
  衣领/颈部静态三角相交为0，FBX往返最大误差4.17nm。
- Unity导入后衣领为4904顶点、7277三角形，标记为
  `ValidationPart=6`；旧服装区域0–5在73份Rest/姿态数据中逐顶点差值为0。
- 生产Builder显式重绑 `LengyueSleeveCloth.target`，防止重复重建Prefab后
  Renderer引用失效。
- 73个Unity BakeMesh状态的衣领/颈部三角相交均为0，最小采样距离3.04mm。
- Yummy macOS Standalone完成173帧，2权重路径下运行错误0、渲染错误0。
  v10袖子仍驱动10623顶点，重力下垂16.514mm、平移滞后19.858mm、
  2秒回落残差0.00278mm，源码哈希与已验收v10完全一致。
- 主成片和对比片均为1920×1080、30fps、173帧、H.264 Main L4.0，
  yuv420p+AAC，并通过完整解码。

### 三、交付物

- Blender源文件：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v005/source/Lengyue_NarrowCollarDress_v005.blend`
- FBX：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v005/exports/Lengyue_NarrowCollarDress_v005.fbx`
- 主成片：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_SleeveCloth_v10_NarrowCollar.mp4`
- 无衣领/新衣领对比：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_SleeveCloth_v10_NarrowCollar_Comparison.mp4`
- 数值报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/v10_narrow_collar.json`

### 四、边界与后续

- 本阶段只验收衣领及“不改变旧服装”的回归条件。全量v004压力姿态报告中
  原有裙摆/手臂极端姿态失败仍然存在，不属于本次衣领改动。
- 衣领调试会话保持OPEN，待用户确认造型、开口和颜色后再清理临时接触表。

## Phase Lavender Chibi Bowl 工作汇报：游戏陶瓷碗建模与贴图

### 一、功能完成情况

- 根据用户提供的三视图建立真实尺寸陶瓷碗，成品尺寸为
  `138 x 138 x 70 mm`，底足外径为`58 mm`，原点位于碗底中心。
- 采用128段旋转剖面生成单一闭合实体，包含内壁、3.5mm标称口沿壁厚、
  圆润卷边、碗底厚度和底足；成品为4354顶点、4480面、8704三角形。
- 从角色原图提取透明贴花，并结合素材板重建外壁连续花边、爱心、星点、
  碗底圆形装饰、淡紫陶瓷底纹和可平铺微法线。
- 建立外壁圆柱UV、内壁俯视径向UV和独立`MicroUV`，外壁角色在四个正交
  方向重复，正视与侧视均能看到完整图案。
- 配置外壁、内壁、卷边/底足三套釉面PBR材质，并提供可复现构建脚本。

### 二、交付物

- Blender源文件：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Bowl/source/LavenderChibiBowl_138mm.blend`
- PBR自包含GLB：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Bowl/exports/LavenderChibiBowl_138mm.glb`
- Unity兼容FBX：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Bowl/exports/LavenderChibiBowl_138mm.fbx`
- 贴图与角色透明贴花：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Bowl/textures/`
- 主视、正视、侧视、俯视渲染：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Bowl/renders/`
- 数值报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Bowl/reports/bowl_model_report.json`

### 三、验证情况

- 源网格边界边为0、非流形边为0，尺寸、材质分区和双UV层门禁均通过。
- GLB和FBX分别回读为1个网格、3个材质，包围盒均为
  `138 x 138 x 70 mm`，无单位或轴向缩放漂移。
- GLB大小1507496字节，SHA-256为
  `a9d1d295c08e146ff2327843e7953e37d539129b1429e9a6757a2b5016c862a5`。
- FBX大小2062396字节，SHA-256为
  `4a046ee1bb5e4e41d7ab197a738db4298b14b052114e94f267fff6047977d248`。
- 四张1024×1024最终渲染已人工检查，图案、内腔、卷边和底足均可见。

### 四、踩坑与后续建议

- Blender MCP会在`read_factory_settings`后断开；构建脚本改为只清理场景
  数据块，不再重置应用级插件状态。
- 真实米制小物件对Area Light能量敏感，展示灯光使用0.75–6W范围，
  避免釉面高光剪裁和角色贴图冲白。
- 当前未附带物理碰撞代理和LOD。进入可拾取玩法前应增加简化碰撞体；
  真机批量摆放时再根据场景预算评估LOD与纹理ASTC压缩。

## Phase Lavender Tableware 工作汇报：马克杯与盘子建模

### 一、马克杯完成情况

- 按三视图完成杯口`Ø80 mm`、高`95 mm`、底径`60 mm`的陶瓷马克杯；
  含把手总包围盒约`113.0041 x 80 x 95 mm`，原点位于杯底中心。
- 杯身与把手使用独立闭合网格。杯身为5888三角形，把手为3484三角形，
  两者边界边和非流形边均为0。
- 杯身沿用碗的角色与花边风格，但按杯身周长/高度重新排版外壁UV，
  避免直接拉伸碗贴图；角色在正反两面各出现一次。
- 杯内使用专用径向图，只在杯底保留角色圆章，杯壁保持淡紫釉面。
- 把手使用独立无微法线釉面材质，避免曲面UV拉伸造成端部暗带。

交付目录：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Mug/`

GLB与FBX均回读为2个网格、4个材质，高度保持95mm。最终哈希：

- GLB：
  `7e6dde052ef0316f233482072bbe5cb04b44c3b418f6c10567dd306adcdfb481`
- FBX：
  `64a29abe9a63e37c76f8e647ef710e3e8fccfa2e4922a6f5684cf8bb3d858cfa`

### 二、盘子完成情况

- 按三视图完成`Ø200 x H25 mm`浅盘，包含盘面、圆润口沿、侧壁与底足。
- 单一闭合实体为2690顶点、2816面、5376三角形，边界边和非流形边均为0。
- 盘面使用独立2048×2048径向贴图，角色位于中心，外圈为同系列波浪、
  爱心和点状装饰；侧壁使用连续环绕花边。
- 盘面、外侧花边和底面使用3套釉面PBR材质，并共享碗的陶瓷微法线风格。

交付目录：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Plate/`

GLB与FBX均回读为1个网格、3个材质，尺寸保持
`200 x 200 x 25 mm`。最终哈希：

- GLB：
  `0ef362e04b9e8f72680f6b11e5b352ffe480fc6b421d345c51099119757978bd`
- FBX：
  `525abbaec2509cce70c7c1f274c6f9566257d2a0b5df7935f8c3ec1d8ceafb58`

### 三、验证与踩坑

- 两套资产均输出Blender、GLB、FBX、独立贴图、四张1024×1024渲染和
  JSON数值报告；模型尺寸、材质数、流形与导出回读门禁全部通过。
- Blender曲线转换的马克杯把手默认保留两个未焊接端环；通过
  `remove_doubles`后封孔并三角化端盖，边界边由64降为0。
- 深杯内部与浅盘顶面需要不同展示光比：马克杯增加低功率顶光，盘子降低
  顶直射并增加前向柔光，避免图案冲白或侧壁压黑。
- 当前仍未生成运行时碰撞代理与LOD；进入拾取/堆叠玩法前应按各自形状建立
  简化复合碰撞体。

## Phase Lavender Chibi Cupcake 工作汇报：纸杯蛋糕建模

### 一、功能完成情况

- 按用户三视图完成`70 x 70 x 75 mm`纸杯蛋糕，原点位于纸托底部中心。
- 模型拆分为纸托、蛋糕胚、连续奶油、双色爱心糖片和双色糖珠，共7个闭合
  网格、5个材质、31144三角形。
- 纸托使用32道真实几何褶皱和波浪顶边；从用户贴图素材中提炼角色、爱心、
  星点、上下边线和淡紫配色，生成无文字、无粘合边的2048×1024无缝环绕图。
- 蛋糕胚和奶油分别生成独立Base Color与微法线资源；糖果使用高光釉面材质。
- 输出Blender、GLB、FBX、四张1024×1024渲染、原始参考图、贴图和JSON报告。

### 二、奶油结构决策

- 首轮使用收尖的单管螺旋模拟裱花；相邻管线自交后在每圈交叠处出现平切缝，
  视觉上像分层截断，不符合参考图。
- 最终改为单一闭合连续表面：96层×128角向采样，以4.6圈纵向起伏、
  四向旋转主脊和八向星嘴细棱共同塑形。
- 最终奶油没有圈间开口或相交切面，爱心和糖珠按表面法向半嵌入，顶点精确
  收束到`Z=75 mm`。

### 三、验证与交付

- 7个网格的边界边和非流形边均为0；总包围盒为
  `70 x 70 x 75 mm`。
- GLB回读为7网格、5材质，SHA-256：
  `208730c4a5cc981b6a708f218c66e769a5d7dc0787966a4440ddde31795cb801`。
- FBX回读为7网格、5材质，SHA-256：
  `5f18bcaa4b31f6f6bf35dc386433e08a3d6d818760bc0355ef78caa2e4b1c0eb`。
- 资产根目录：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Cupcake/`
- 数值报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Cupcake/reports/cupcake_model_report.json`

### 四、后续建议

- 当前为近景质量，总计31144三角形；批量摆放前建议增加约8k三角形LOD1。
- 当前未附带Collider。可拾取玩法建议使用单一圆柱或低面数凸包，不要直接
  将奶油和糖珠高模设为动态Mesh Collider。

## Phase Lavender Props 色彩修正：统一淡紫底色

### 一、修正内容

- 用户确认首版淡紫仍过浅后，将碗、马克杯和纸托的最终底色加深为
  `RGB(214, 190, 236)`（`#D6BEEC`），在AgX和釉面高光下仍可明确辨识。
- 将碗与马克杯内壁提亮目标改为`RGB(226, 207, 244)`，混合强度分别降至
  0.16和0.14，保留内腔明度但不再洗白紫色。
- 马克杯把手无贴图材质同步改为更浓的薰衣草紫；奶油、蛋糕和糖果材质不变。
- 重建三套贴图、Blender源文件、四视图、GLB、FBX和JSON验证报告。

### 二、验证结果

- 碗仍为`138 x 138 x 70 mm`，单网格闭合流形，3材质。
- 马克杯仍为杯口`Ø80 mm`、高`95 mm`，杯身与把手均为闭合流形。
- 纸杯蛋糕仍为`70 x 70 x 75 mm`，7个网格全部闭合流形。
- 三张主视图人工检查通过：高光区域接近白色，中间调和阴影可稳定辨识
  淡紫色，角色、花边与纸托褶皱未被覆盖。

### 三、最终导出哈希

- 碗GLB：
  `a9d1d295c08e146ff2327843e7953e37d539129b1429e9a6757a2b5016c862a5`
- 碗FBX：
  `4a046ee1bb5e4e41d7ab197a738db4298b14b052114e94f267fff6047977d248`
- 马克杯GLB：
  `7e6dde052ef0316f233482072bbe5cb04b44c3b418f6c10567dd306adcdfb481`
- 马克杯FBX：
  `64a29abe9a63e37c76f8e647ef710e3e8fccfa2e4922a6f5684cf8bb3d858cfa`
- 纸杯蛋糕GLB：
  `208730c4a5cc981b6a708f218c66e769a5d7dc0787966a4440ddde31795cb801`
- 纸杯蛋糕FBX：
  `5f18bcaa4b31f6f6bf35dc386433e08a3d6d818760bc0355ef78caa2e4b1c0eb`

## Phase Wardrobe v006 工作汇报：衣领全动画防穿模与浅U开口

### 一、问题与根因

- 用户指出v005衣领在Yummy运动中会露出衣领下方的不规则皮肤块，且衣领
  视觉像等高圆环，缺少圆领应有的轻微向下开口。
- `collar-coverage-opening`运行时插桩完整记录173帧衣领、Face、
  SkinCoverage、NeckExtension和衬衫的BakeMesh。
- 预修复证据显示：68/173帧发生衣领/NeckExtension相交，峰值为第107帧
  176对三角形，第130帧为105对。
- 隔离渲染确认露肤来源是旧 `NeckExtension`，而非SkinCoverage或Face。
  旧网格包含19个底部封口三角和不稳定下层侧壁，受不同Neck/Chest权重
  驱动后会翻到衣领与衬衫前方。

### 二、实现

- 新建非破坏性v006服装资产，保留v004/v005回滚基线。
- 将衣领开口改成平滑浅U：取消内缘18波瓣，前中部下移6.7mm；
  最终开口宽86mm，仍比原始约108mm领口窄20.37%。
- 保留内部18折褶响应和袖口内衬同款Atlas tile 7粉紫色。
- 新建56顶点/84三角形开放式NeckExtension：删除19三角底盖及旧下层侧壁，
  新底边向衣领内缩3mm，并使用与衣领内缘一致的55% Chest/45% Neck权重。
- 匹配14个Face边界法线；新增独立校准材质，修复浅U开口暴露出的历史颈部
  UV/明暗接缝。
- Unity Builder继续显式重绑 `LengyueSleeveCloth.target`，且对开放颈部
  顶点数、三角形数、法线匹配和材质加入构建门禁。

### 三、验证

- 73个Rest/动画/压力姿态：衣领与NeckExtension、Face相交均为0，
  最小间隙2.439mm。
- Yummy macOS Standalone实际2权重路径：173/173帧相交为0，
  最小衣领到皮肤距离1.555mm，第130帧1.892mm。
- 运行错误0、渲染错误0；1920×1080、30fps、4×MSAA、2×超采样。
- v10袖子代码哈希保持不变，驱动顶点10623；重力下垂16.514mm、
  平移滞后19.858mm、回落残差0.00278mm。
- 旧服装Part 0–5在73份对应姿态中与v004逐顶点差值为0。
- 两段H.264 Main L4.0视频均为173帧并通过完整解码。

### 四、交付物

- v006 Blender：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v006/source/Lengyue_ShallowUCollarDress_v006.blend`
- 主成片：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_Unity_SleeveCloth_v10_ShallowUCollar.mp4`
- v005/v006对比片：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/render/Lengyue_Yummy_SleeveCloth_v10_ShallowUCollar_Comparison.mp4`
- 第130帧对比：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/evidence/Yummy_v10_ShallowUCollar_Frame130_Comparison.png`
- 汇总报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/v10_shallow_u_collar.json`

### 五、当前状态

- 调试会话仍为 `[OPEN - POST-FIX AWAITING USER VERIFICATION]`。
- 用户视觉确认前保留网络插桩、NDJSON日志、环境文件和调试服务；
  确认后按TRAE-debugger流程统一清理。

## Phase Wardrobe v10 回滚工作汇报：恢复加衣领前生产版本

### 一、功能完成情况

- 按用户要求将唯一生产Prefab从v006浅U衣领版本恢复到加衣领前的
  v10 ShadowFabric基线。
- `LengyueLayeredDressBuilder`重新使用v004
  `Lengyue_LayeredDress.fbx`，并恢复原始70顶点闭合
  `Lengyue_LavenderCasual_NeckExtension.asset`及
  `Lengyue_Toon_Face.mat`。
- Builder新增无衣领门禁：UV2 `ValidationPart=6`顶点必须为0；
  同时继续要求v10袖子驱动顶点严格为10623，并显式重绑
  `LengyueSleeveCloth.target`。
- 保留已验收的v10袖子重力、惯性和回落参数，以及ShadowFabric阶段的
  手臂阴影软化、织物高度/法线/纤维光纹理、4×MSAA和2×超采样。
- 删除生产 `Runtime` 下5个零引用衣领专用资产及其`.meta`。
  v005/v006源文件、导出物、报告和视频仍保留在`ArtSource`作为历史记录。
- 删除 `LengyueYummyCaptureProbe` 的衣领网络插桩，关闭7782调试服务，
  清理对应环境文件、NDJSON和逐帧隔离产物。调试记录已标记
  `[CLOSED - ROLLED BACK]`。

### 二、编译与测试

- Unity生产构建报告通过：
  `garmentVertices=64574`、`garmentTriangles=113128`、
  `noCollarVertices=true`、`canonicalNeckRestored=true`、
  `sleeveTargetRebound=true`，最大重绑误差`0.722 um`。
- macOS Player构建成功：错误0，产物大小186021367字节。
- 无调试依赖Standalone完成173帧回放：
  `collarVertices=0`、`sleeveDrivenVertices=10623`、
  `sleeveSkinInfluences=2`、运行错误0、渲染错误0。
- 袖子物理基准未漂移：重力下垂16.514mm、运动滞后19.858mm、
  回落残差0.00278mm。
- 无衣领静态近景：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v004/evidence/idle_detail.png`

### 三、交付与后续

- 当前生产模型：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Models/Lengyue_LayeredDress.fbx`
- 当前生产Prefab：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Prefabs/Lengyue_GameCharacter.prefab`
- 构建报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v004/reports/unity_build.json`
- Standalone报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Yummy/reports/unity_playmode.json`
- 后续服装修改必须以该无衣领生产基线为输入；除非用户重新确认衣领需求，
  不得将v005/v006资产重新接回生产Prefab。

## Phase Yummy Cookie v001 工作汇报：隔离的盘中取饼进食演示

### 一、完成内容

- 新建分支`experiment/yummy-cookie-v001`和独立实验目录
  `/Users/bytedance/MateSoul/MateSoul/Assets/Experiments/YummyCookie_v001/`。
  原无衣领v10生产角色、原AnimationClip、盘子/曲奇源文件及项目设置均保持原样。
- 逐帧采样173帧手掌、指骨及嘴形顶点，确认角色右手第0帧已是捏取手势，
  第20–23帧最接近嘴部，之后手指张开并进入鼓腮咀嚼。
- 为使抓取过程可见，实验版前接24帧（0.8秒）下探抓取动作，再完整播放
  原173帧身体动作。仅在实验角色实例的入口阶段叠加轻微张嘴。
- 使用原200mm盘子盛放14块50mm曲奇，以两层环形和一块顶层曲奇排列。
  抓取时同一个顶层对象绑定右手，原动画第22帧隐藏，盘内实际剩余13块。
- 曲奇程序化材质烘焙为1024px颜色/法线图，合并为单网格单材质；
  新增独立场景、角色Prefab、桌面、macOS Player、验证和打包脚本。

### 二、验证与问题处理

- 最终Player构建成功，构建错误0；197帧Standalone运行错误0。
- 抓取位移跳变约0.000015mm，手掌锚点误差0；入口前可见、入口后隐藏；
  回退重播恢复14块、再次入口保持隐藏均通过。
- v10头发和袖子初始化成功，袖子仍驱动10623个顶点；
  原版153个受保护文件的SHA-256全部一致。
- 首轮方阵边角触及盘沿，改为外缘半径77mm的环形堆叠。
- Unity未命名场景不能直接恢复SceneManagerSetup，已保留并恢复原活动场景。
  脚本热重载与构建必须分开，避免编辑器和Player程序集字段布局不一致。

### 三、交付与回滚

- 成片：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/YummyCookie_v001/render/Lengyue_Yummy_CookieEating_v001.mp4`
- 报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/YummyCookie_v001/reports/replay.json`
- 版本说明：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Experiments/YummyCookie_v001/README.md`
- 删除`Assets/Experiments/YummyCookie_v001/`及同名`.meta`即可撤销实验，
  无需重建原版。该场景未写入项目构建场景列表。
- 当前是针对Yummy动作标定的演示；后续复用于其他动作时需重新采样抓取/入口
  时序及手掌局部位姿。嘴部距离使用几何近似，不声称任意姿态精确防穿模。

## Phase Lavender Props 二次色彩验收：提升紫色浓度

- 用户反馈首版`#E6D8F2`在成片中仍偏白，最终基础色调整为
  `#D6BEEC`（`RGB(214, 190, 236)`）。
- 碗与马克杯内壁提亮量进一步收敛；马克杯把手同步加深，避免无贴图把手
  与贴图杯身存在色差。
- 碗、马克杯、纸杯蛋糕纸托的贴图、Blend、四视图、GLB和FBX全部重建。
- 三套资产尺寸、网格数量、材质数量和闭合流形门禁均保持通过。
- 主视图确认高光仍接近白色，中间调为明确浅薰衣草紫，纸托褶皱和陶瓷
  曲面阴影不再丢失紫色。

## Phase Lavender Chibi Cutlery 工作汇报：勺子与筷子建模

### 一、完成内容

- 在`ArtSource/SapphiArt/Props/Spoon/`新增140mm陶瓷勺完整资产包。
  勺体为单一闭合壳体，包含凹面勺头、连续柄部曲率、圆角尾端和双材质UV。
- 在`ArtSource/SapphiArt/Props/Chopsticks/`新增230mm陶瓷筷子完整资产包。
  两支筷子为独立闭合网格，采用圆角方截面、渐细夹持端和四道实体防滑浅槽。
- 复用`Props/Bowl`的陶瓷微法线和程序化纹理基础函数，重新生成爱心、波浪、
  圆点与四角星。纹样按模型实际长宽比补偿，避免窄长UV造成纵向拉伸。
- 两套资产均提供可重复执行的纹理生成和Blender构建脚本、源文件、GLB、FBX、
  四视图渲染及JSON校验报告。

### 二、验证结果

- 勺子尺寸：`25 x 140 x 12 mm`；`11200`三角形；边界边0、非流形边0。
- 筷子单支尺寸：`7 x 230 x 7 mm`；每支`7920`三角形；
  边界边0、非流形边0。双支场景总宽16mm。
- 两套GLB和FBX均完成重新导入回读，网格数、材质数和尺寸全部通过。
- 筷子底端改为三角扇封口并按面向生成UV，消除了GLB切线计算警告。
- Blender 5.2.1 LTS后台构建、Eevee渲染和导出均成功。

### 三、交付路径

- 勺子：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Spoon/`
- 筷子：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Chopsticks/`
- 勺子报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Spoon/reports/spoon_model_report.json`
- 筷子报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Chopsticks/reports/chopsticks_model_report.json`

### 四、后续建议

- Unity/PICO运行时优先导入GLB；若使用FBX，需从`textures/`重新绑定颜色图与
  `ceramic_micro_normal.png`。
- 可拾取交互应使用单独的低面数凸包或组合碰撞体，不直接使用交付网格作为动态
  Mesh Collider。

## Phase Lavender Chibi Chopsticks v2 工作汇报：纹样对比度增强

### 一、完成内容

- 加深筷子爱心与四角星的深紫描边和玫粉填充色。
- 加粗波浪线与点状装饰，保持原布局、UV比例和筷身底色不变。
- 重新生成贴图、Blend、GLB、FBX及四视图渲染。

### 二、验证结果

- 近景与完整正视图确认纹样辨识度明显提升。
- 单支尺寸仍为`7 x 230 x 7 mm`，两支均保持闭合流形。
- GLB/FBX回读尺寸检查继续通过，导出过程无切线警告。

## Phase Lavender Chibi Furniture 工作汇报：桌子与椅子建模

### 一、完成内容

- 新增`Props/FurnitureCommon/furniture_common.py`，统一圆角板件、渐细斜腿、
  荷叶边围板、家具材质、摄影棚、导出和回读验证逻辑。
- 完成`600 x 400 x 750 mm`桌子：圆角桌面、四面五/三瓣桌裙、四根外张腿、
  粉色脚套及桌面/桌裙爱心、波浪、圆点、四角星纹样。
- 完成`360 x 380 x 680 mm`椅子：圆角座面、前/侧椅裙、四腿、侧撑、
  连续后立柱、球形柱头和真实爱心镂空椅背。
- 纹理复用Bowl的陶瓷底纹与微法线生成能力，并针对家具重新生成高对比纹样。

### 二、问题处理与验证

- 桌腿斜截面首轮使地面以下多出0.90mm；改为按实际包围盒抬升各腿后，
  总高恢复为精确750mm。
- 椅背首轮布尔镂空产生16条非流形边；最终改为带孔多边形三角化，并显式封闭
  外侧壁和爱心内壁，非流形边降为0。
- 椅背中间版本按序连接内外轮廓产生自交四边形和放射线；改用Blender
  `tessellate_polygon`后视觉瑕疵消失。
- 桌子为9个闭合网格、9980三角形；椅子为13个闭合网格、13248三角形。
- 两套Blend及GLB/FBX回读尺寸、材质和闭合流形检查全部通过，导出无切线警告。

### 三、交付路径

- 桌子：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Table/`
- 椅子：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Chair/`
- 桌子报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Table/reports/table_model_report.json`
- 椅子报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Chair/reports/chair_model_report.json`

### 四、后续建议

- 运行时建议为桌面、座面和腿分别配置简化Box/Capsule Collider。
- 椅背镂空网格适合渲染，但交互碰撞不应直接使用高精度Mesh Collider。

## Phase 美术资产展示包工作汇报

### 一、交付内容

- 为马克杯、碗、盘子、勺子、筷子、角色、曲奇和纸杯蛋糕建立统一展示包：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Submission/Lengyue_ArtAssets_Showcase_v001/`
- 生成1920×960八资产总览图，并汇总各道具的真实材质多视图。
- 汇总7个道具GLB、6套道具FBX与贴图目录、角色骨骼FBX、4张角色贴图、
  角色参考图和3段Unity运行视频。
- 提供`00_README.md`，明确评审顺序、提交方式、白模规避规则和资产清单。

### 二、提交策略

- 非技术评审首先查看总览图、各道具多视图PNG和角色Unity视频。
- 可交互道具统一优先使用GLB；GLB将网格、PBR材质和贴图封装在单文件中。
- 技术验收附FBX与同级`Textures/`，禁止只提交单独FBX。
- 角色完整提交`04_Character/`，其FBX同时保留嵌入贴图和独立贴图兜底。
- 默认不提交`.blend`源文件；只有对方明确要求可编辑源文件时再单独提供。

### 三、验证结果

- 6个贴图型道具GLB全部无外部图片路径，嵌入图片数量为3–5张。
- 曲奇GLB使用6套内嵌PBR材质参数，无外部资源路径。
- 角色展示视频为1920×1080、H.264、yuv420p、30 FPS。
- ZIP通过`unzip -t`完整性检查，无压缩错误。
- 最终文件：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Submission/Lengyue_ArtAssets_Showcase_v001.zip`
- ZIP大小79849117字节，SHA-256：
  `e9744a6928c65c92e294b554208d40d6f3257d4039cae94b20fdeed195f5494b`。

### 四、后续建议

- 飞书提交时将总览图和角色视频直接插入正文，ZIP作为完整模型附件。
- 若评审平台限制附件大小，可另拆分为“预览包”和“模型包”，但不要拆散
  FBX与其Textures目录。

## Phase 美术资产展示包V002纠正：替换为最新角色

### 一、问题与纠正

- V001展示包错误引用了9月23日的`AnimatorKit_V001`角色FBX、旧v11服装
  视频和V001走跑视频，已撤销并删除。
- 当前唯一有效包为：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Submission/Lengyue_ArtAssets_Showcase_v002.zip`
- 最新角色源改为9月28日生成的
  `Wardrobe/v006/source/Lengyue_ShallowUCollarDress_v006.blend`。
- 生产版本改为Unity的`Lengyue_GameCharacter.prefab`，最新展示视频改为
  `Lengyue_Yummy_CookieEating_v001.mp4`。

### 二、交付结构

- `01_Preview/`：v006角色多视图、最新CookieEating视频和新版八资产总览。
- `02_GLTF/`：7个道具GLB及v006角色静态展示GLB。
- `03_FBX_and_Textures/`：道具FBX及贴图。
- `04_Character_Latest/DCC/`：v006完整Blend源文件、服装FBX和验证报告。
- `04_Character_Latest/UnityProject/`：从生产Prefab递归解析得到的32项Unity
  真实依赖闭包，包含全部`.meta`。

### 三、版本证据

- v006 Blend SHA-256：
  `03789b3be673b9e9255ab4b1daadbcc6fae4c61fe50977e94f664447c1be1b54`
- 生产Prefab SHA-256：
  `4daa9e370e831814c2bc03b0c4e0c582eb3e93a7ee85677dfd43dfe9bede6d32`
- v006服装网格 SHA-256：
  `e4bc85598f158944987ef78d3ba4a14b8a99630c353f4de882a0e214f7894d2f`
- 最新视频 SHA-256：
  `1d2333b70b038c35e99fdf386962f51628a08e282683cd4a3221cd6e9bca03e8`
- 最新视频生成时记录的Prefab与服装网格哈希，和当前生产文件完全一致。

### 四、验证结果

- 禁止旧文件扫描通过：包内不存在AnimatorKit V001、旧角色FBX、v11视频或
  V001走跑视频。
- Unity依赖闭包32/32匹配，无未解析GUID；补齐外部
  `HairSpringPhysics.cs`依赖。
- 角色静态GLB由v006当前可见网格烘焙生成，回读50网格、4材质、4张内嵌
  贴图，包含浅U领与当前头发，不包含旧头发。
- 最新视频为H.264、1920×1080、30 FPS、197帧，完整解码通过。
- ZIP通过`unzip -t`，大小103745487字节，SHA-256：
  `c0fe2fc8482de5bebd2391ab92325ced59c05a722562816cdbd442e61ae9a312`。

### 五、使用约束

- 角色GLB仅用于静态外观旋转查看；生产级骨骼、Toon Shader、布料与动画
  必须使用`04_Character_Latest/UnityProject/`中的生产Prefab依赖闭包。
- `DCC/FBX/`是服装和颈部扩展，不是完整角色；完整DCC源为v006 Blend。

## Phase PlushDoll 工作汇报：冷月坐姿毛绒玩偶（2026-09-29）

### 一、交付

- 根据用户正、侧、后三视图新建坐姿玩偶，路径：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/PlushDoll/`。
- 原图无尺寸；只假定总高300mm并等比缩放，实测宽211.5878mm、深201.4783mm。
- 完成19块紫色齐刘海/短发布片、曲面刺绣脸、背带裙、袖口、前后实心粉色
  布扣、手部/鞋底/裙身缝线及15,500根固定短绒几何。
- 交付打包贴图的Blend、GLB、FBX、1200px四视图、三视图合成图、重建脚本与JSON报告。

### 二、纠正与验证

- 首轮球体拼接模型存在遮眼刘海、悬空五官/背带、曲线端口开口；已替换。
- 发片沿连续头部外形构建，脸部五官投影在头网格上，背带通过射线贴合服装。
- 线迹采用显式管壁和共用端点封口；布扣使用服装采样拟合支撑平面，
  避免逐点跟随裙腰交界导致圆扣折弯。
- 独立重新打开Blend、清空场景后分别导入GLB/FBX：
  三者均82网格、170,630三角形、8张可读取贴图；
  尺寸/面数一致，闭合、面向一致性、正体积与退化面检查全部通过。
- 渲染使用Cycles，未执行Unity/PICO运行时验收。

### 三、使用说明

- 这是静态无骨骼摆件；短绒为三个独立`Short_Pile_*`对象，可关闭以降低面数。
- 参考图仅决定比例与外观，300mm不是用户提供的制造尺寸。
- 完整检验记录：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/PlushDoll/reports/plush_doll_model_report.json`。

## Phase PlushDoll Face V2 工作汇报：单色眼球修订（2026-09-29）

### 一、修改内容

- 根据用户反馈移除嘴部刺绣。
- 眼球改为同一紫色的连续色块，移除眼白、瞳孔、渐变和高光。
- 保留深色眼眶、上眼线、睫毛与眉毛，以维持面部识别度和萌系神态。
- 更新`prepare_plush_textures.py`、Blender源文件、GLB、FBX及四视图渲染。

### 二、验证

- 独立回读Blend、GLB和FBX，三者均为82网格、170,630三角形。
- 实测尺寸约211.5553 × 201.4846 × 300mm；尺寸和三角形数量跨格式一致。
- 8张贴图均可读取，闭合拓扑、相邻面朝向、正体积和退化面检查全部通过。
- 当前仍为静态无骨骼资产，未执行Unity/PICO运行时验收。

## Phase GLB Collection 工作汇报：模型与贴图统一整理（2026-09-29）

### 一、交付

- 新建统一交付目录：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/GLB_and_Textures/`。
- 扫描项目中18个GLB文件，按SHA-256去除7个提交包重复副本，收录11个独立模型。
- 所有GLB集中到`Models/`，贴图集中到`Textures/<模型名>/`，避免同名贴图覆盖。
- 共提取43张实际嵌入贴图；曲奇未使用图片贴图，目录内保留说明文件。
- 新增`manifest.json`记录原始路径、重复来源、文件大小及模型/贴图SHA-256。
- 新增可重复执行脚本：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/tools/collect_glb_assets.py`。

### 二、验证

- 逐文件校验复制前后模型SHA-256一致。
- 校验全部GLB为有效2.0文件头且声明长度与实际长度一致。
- 校验43张提取贴图的字节数、SHA-256和PNG文件签名。
- 7个被去重副本均与保留文件SHA-256一致；原始项目资产未移动或删除。
- 最终合集约64MB。

## Phase CharacterPresentation v004 工作汇报：参考图姿势扩展（2026-09-29）

### 一、交付内容

- 在不修改`v003`和服装源Blend的前提下，新建
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/CharacterPresentation/v004/`。
- 保留自然站立、行走、双臂上举、双臂前伸、屈肘举手5个动作。
- 新增T字姿势、躯干扭转、侧弯、前倾、宽站姿、单手挥手、双臂斜举7个动作。
- 最终输出为2514×2110 PNG，全部使用中文动作名；不包含下蹲、交叉步、
  高抬腿或弓步。

### 二、实现与验证

- `render_reference_poses.py`以v004服装Blend为只读输入，通过骨骼世界空间
  旋转生成7个新姿势并以Cycles透明背景渲染。
- `compose_pose_sheet.py`将保留姿势与新增姿势铺到统一灰色背景，生成12格展示图，
  同时记录每个输入和最终图的SHA-256。
- 最终图已人工检查中文标签、构图、背景一致性和动作可读性。
- 最终图SHA-256：
  `bf5981c8f1190da5de7dfd9a08faa9aff65a027ec746aa55ec2f622b795b58c1`。

## Phase GLB Collection Layout V2 工作汇报：模型与贴图分目录（2026-09-29）

- 按用户要求将合集改为`Models/`与`Textures/`两个顶层目录。
- `Models/`集中存放11个GLB；`Textures/`保存43张贴图，并按模型名分子目录，
  避免`ceramic_micro_normal.png`等同名文件互相覆盖。
- `manifest.json`中的模型和贴图相对路径已同步刷新。
- 精确检查所有GLB的父目录、模型/贴图数量及SHA-256，全部通过。
- 原始资产未移动、未删除。

## Phase Retro Electronics 工作汇报：电视机与遥控器建模（2026-09-29）

### 一、交付

- 新建复古CRT电视机，实测尺寸`480 × 450 × 420mm`；包含圆角深机身、真实
  外凸屏幕、双杆天线、控制面板、扬声器、侧散热栅格及四个脚垫。
- 新建电视遥控器，实测尺寸`50 × 18 × 140mm`；包含方向环、OK键、爱心键、
  电源键、音量/频道/静音键、快捷键、侧饰条及背部电池仓。
- 两套资产均交付Blender源文件、GLB、FBX、主效果图、三视图及JSON报告。
- 新增电子道具共用几何/塑料材质模块、贴图生成脚本、三视图合成脚本和独立
  Blend/GLB/FBX验收脚本。

### 二、验证

- 电视机：32个闭合网格，独立回读35,440三角形，2张贴图。
- 遥控器：47个闭合网格，独立回读10,116三角形，1张贴图。
- Blend、GLB、FBX的尺寸、三角形数量和贴图数量分别一致；所有网格边界边、
  非流形边、法向不连续边和退化面均为0。
- 统一`GLB_and_Textures`合集已刷新为13个独立模型、46张贴图；全部通过
  GLB文件头及SHA-256校验。
- 清理了本阶段产生的`.blend1`和`__pycache__`临时文件。

## Phase Retro Electronics Reference V2 工作汇报：参考还原重建（2026-09-29）

### 一、纠正初版结论

- 用户明确否定初版外观。上一章节的技术检查不能证明参考还原度；
  其中关于 CRT 曲面、无互遮和电视整体高度的结论，以本章节和最新报告为准。
- 先记录屏幕过宽、薄板圆角不足、实心框遮挡、后壳形状、按钮布局、丝印及
  材质冲白等偏差，再重建几何，经过正视、侧视、俯视/背视和斜视复核。
- 将参考图 420mm 标注解释为机身含脚高度；电视含天线总高为 565.7mm。
  这是本版的尺寸解释，不能把文件名中的 420mm 当作整体包围高度。

### 二、完成内容

- 电视：实际开孔的厚凹框、358×258mm 鼓面屏、12mm 中央前凸、收窄后壳、
  三列各18排凹入散热槽、双节窄V天线、贴合屋顶的底座和连接机身的脚垫。
- 遥控器：大圆角滚边壳体、真正方向环及独立 OK、重新排布的扁平按键、
  图集丝印、嵌入式电池盖；粉色侧边合入同一壳体，消除重叠黑线。
- 新增共用模块：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/ElectronicsCommon/reference_geometry.py`、
  `build_reference_electronics.py`、`prepare_reference_atlas.py`；
  两个原构建入口转发到新版，更新独立验收和中文三视图合成。
- 正式 Blend / GLB / FBX、四视图、中文三视图和 README 均已刷新；
  旧版模型和脚本保存在 `ElectronicsCommon/reports/pre_reference_rebuild_v1.zip`。

### 三、最终验收

| 资产 | 实测整体尺寸/mm | 网格数 | 三角形 | 实际图片数 |
|---|---|---:|---:|---:|
| 电视机 | 480×450×565.7 | 36 | 112,306 | 2 |
| 遥控器 | 50×18×140 | 32 | 32,344 | 1 |

- Blender 5.2.1 LTS 构建、Cycles 渲染、导出及独立回读成功。
- 两套资产在 Blend、GLB、FBX 中尺寸、面数和图片数一致；
  边界边、非流形边、面朝向不一致、原始退化面和焊接后坍塌面均为0。
- 源场景的屏幕、电源、散热槽、OK、方向环、菜单和电池盖首命中射线均通过。
- 合集刷新为13个 GLB、46张图片、7个去重副本；
  电视和遥控器的合集 GLB 与最新验收报告中的哈希一致。

### 四、经验与边界

- 验收焊接曾掩盖圆头槽/脚垫中的重复顶点。现改为导出前清理，并增加
  `raw_degenerate_faces`、`collapsed_faces_after_weld`，直接约束原始文件。
- 通过命名端口修复 Blender 5.2 合成节点接线；通过40°锐边修复孔边异常阴影。
- 当前为静态高精度美术资产，未接入交互、视频播放，也未进行 Unity/PICO
  运行时验收；进入运行场景前可按预算另做 LOD 和碰撞体。
- 本轮完成视觉复核与技术验收，尚未获得用户对修正版外观的最终确认。

## Phase Lengyue Wardrobe Foundation 工作汇报：轻量换装基座（2026-09-29）

### 一、完成内容

- 新增`LengyueOutfitDefinition`，结构化记录整套服装的Mesh、材质、骨骼名、
  部件角色、局部变换以及袖子/裙摆能力，不为服装复制角色骨架或Animator。
- 新增`LengyueWardrobeController`，支持按ID切换服装、运行时骨骼重绑定、
  完整预校验、隐藏节点预构建和原子提交；缺骨、缺材质或配置错误时保持旧服装。
- 新增Unity菜单
  `MateSoul/Lengyue/Wardrobe/Create Definition From Selection`，可从服装专用
  FBX/Prefab自动生成定义，不需要手填骨骼数组。
- `LengyueSleeveCloth`支持释放旧运行时Mesh并对新服装重新初始化；
  `SkirtPoseGuard`支持按服装启停，关闭时恢复全部裙骨静止姿态。
- 生产Builder会自动为`Lengyue_GameCharacter.prefab`挂载换装控制器，并把
  Garments、SkinCoverage、NeckExtension注册为`lavender-casual`初始套装。

### 二、编译与验证

- Unity 2022.3.38f1脚本编译成功，无本次新增C#错误或警告。
- 生产角色重建通过：116骨、64,574服装顶点、113,128三角形，
  最大重绑定误差`7.2151477e-7m`，`wardrobeConfigured=true`。
- 自动事务验证全部通过：定义自动生成、正常服装切换、目标骨骼重绑定、
  缺骨服装拒绝、失败后保留当前服装、切回初始套装。
- 验证报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/v004/reports/wardrobe_foundation.json`。

### 三、边界与后续

- 当前目录仅注册初始`lavender-casual`，尚未制作第二套正式服装，也未开发换装UI
  或存档；这些应复用本阶段控制器，不要复制角色Prefab。
- 生成定义时必须选择“服装专用根节点”；其下所有SkinnedMeshRenderer都会进入
  同一套装。每套服装必须包含自己的遮挡层、颈部补片、鞋和附件。
- 袖子布料仍要求现有UV2部件元数据，非兼容服装应关闭该能力；裤装或无裙骨服装
  应关闭裙摆守卫。

## Phase Pink Pajamas 工作汇报：睡衣睡帽建模与真实换装（2026-09-29）

### 完成内容

- 根据用户三视图制作粉白格纹睡衣、翻领滚边、四颗纽扣、胸袋、袖口、
  长裤裤脚、弯垂睡帽与绒球，补充适配原脚型的室内鞋。
- 服装按冷月原体型拟合，保留116骨静止姿态。交付Blend、FBX、内嵌贴图GLB，
  三组中文展示图及11秒Unity换装回放。
- 新增`LengyuePajamasBuilder`与`LengyuePajamasProbe`，生产目录注册
  `pink-pajamas`，默认仍为`lavender-casual`。
- 生产Prefab与任务前备份逐行比较，仅`outfits`增加一个定义引用；
  原日常装、身体骨架、Animator和旧服装源Blend未更改。

### 验证结果

- Blender 5.2.1独立导入FBX/GLB：3个导出网格、116骨、144,760三角形；
  GLB内嵌2张PNG。最大权重和误差`1.3411e-7`，
  两格式顶点最大偏差`2.3806e-6m`。
- Unity 2022.3.38f1接入4个Renderer（服装、手部、帽子、原颈部补片）；
  重绑定最大偏差`1.0306e-6m`。
- 实际PlayMode通过16次成功切换、330帧待机/行走/跑步/进食；
  运行/渲染错误为0。Animator、骨架引用保持，帽子跟随头部，
  原服装正确隐藏，切回后短袖布料和裙摆恢复。
- 非法ID被拒绝且保持当前服装；反复切换后无残留服装节点。
- 复核实拍帧修正了肩部格纹拉伸、衣摆与裤腿相交、鞋部漏导出。
  测试期间Unity旧网格GPU缓冲残留导致的渲染错误通过重建生成Mesh解决，
  最终重新录制并验证通过。

### 交付与边界

- 主目录：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Wardrobe/PinkPajamas/v001/`
- 定义：
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Wardrobe/PinkPajamas/PinkPajamas.asset`
- 报告：主目录`reports/unity_build.json`、`unity_playmode.json`、
  `export_validation.json`、`delivery_manifest.json`。
- 长袖长裤使用身体蒙皮，睡帽整体跟随头骨；未实现新增长袖/睡帽自由布料模拟，
  未进行PICO真机测试。当前闭环覆盖Unity编辑器PlayMode。

## Phase D0.2 工作汇报：PICO Emulator 虚拟房间与扫描数据验证

### 一、完成内容

- 下载并创建 PICO Emulator `6.2.0` AVD：`MateSoul_PICO_0_13`。
- 通过宿主机 LaunchServices 成功启动模拟器，规避 TRAE 沙箱内
  `HVF error: HV_DENIED`。
- 新增可复用启动入口：
  `/Users/bytedance/MateSoul/scripts/start-pico-emulator.command`。
- 通过 ADB、系统服务、持久化目录、模拟器资源和控制协议交叉检查虚拟房间与
  Room Capture 数据状态。

### 二、运行验证

模拟器运行状态：

```text
ADB serial: emulator-5554
Device: pico_spaceos_emulator_arm64
Android: 16 / API 36
Build type: userdebug
sys.boot_completed: 1
```

已确认运行中的核心服务：

- `com.pico.mrservice`
- `com.pico.spatial.runtime`
- `com.bytedance.pico.openmr`
- `com.bytedance.pico.openmr.spatial`

`scripts/pico-run.sh emulator doctor` 通过，PICO Emulator binary、Android
Studio 2025.1.x、Android SDK 35 和 PICO Spatial Plugin 均可识别。

### 三、虚拟房间结论

模拟器内置 `Toren1BD` 客厅/公寓模型，包含沙发、餐桌、餐椅、书架、边桌、
电视和厨房等结构。OBJ 共 `16,253` 个顶点，空间边界约
`9.650m × 3.000m × 11.563m`。

模拟器控制协议提供 `LIVING_ROOM_DAY` 和 `LIVING_ROOM_NIGHT` 环境，调试界面
包含 `Plane Detection`、`Spatial Mesh` 和 `Spatial Anchor` 能力开关。

因此已有可供 MR 视觉、移动输入和空间能力联调的虚拟房间。

### 四、扫描数据结论

新 AVD 没有预生成 Room Capture 数据：

```text
mrservice Map status: 0
mrservice Map uuid: <empty>
Session Anchor Counts: 0
/data/misc/pxr/maps: empty
```

内置 OBJ 只是 Virtual Scene/VST 环境，不等于 Floor、Wall、Door、Furniture
等 Scene Anchors。Launcher 中也没有独立 Room Capture 入口，后续必须由测试
应用调用 `StartSceneCaptureAsync`，再查询是否能在模拟器中生成 Scene Anchors。

### 五、后续建议

下一阶段创建最小 `RoomCaptureProbe`：

1. 开启 `sceneCapture`、`spatialMesh`、`planeDetection` 和
   `spatialAnchor`。
2. 查询已有 Scene Anchors，验证空数据分支。
3. 调用 `StartSceneCaptureAsync`，完成后整批重新查询。
4. 输出语义计数、Floor Polygon、Wall Box2D 和 Furniture Box3D。
5. 若模拟器不能产出数据，使用固定 `RoomMapSnapshot` 继续导航开发，
   把真实扫描质量验收保留到 PICO 真机。

## Phase D1 工作汇报：扫描房间内随机行走首功能

### 一、完成内容

- 新增 `Assets/MateSoul/RoomNavigation` 独立模块和验证场景。
- 实现 `IRoomMapProvider`、`RoomMapSnapshot`、PICO Scene Capture Provider
  与 PICO SDK Mock Provider。
- 将 Floor Polygon 三角化为地面，将 Wall/VirtualWall 和家具 Box3D
  转成导航障碍，一次性构建 Runtime NavMesh。
- 角色只选择完整 NavMesh 路径，接入现有待机/走路 Animator 参数。
- 地图为空、几何无效、NavMesh 不覆盖地板时 fail-closed，不创建随机移动。
- 开启 PICO VST、Scene Capture、Spatial Mesh、Plane Detection 和
  Spatial Anchor 工程能力。

### 二、模拟测试策略

PICO Emulator 的 Toren 客厅只有 VST 模型，没有预生成 Scene Anchors。当前：

- 编辑器与 Emulator 使用 PICO SDK 官方 `MockSceneData.json`。
- Mock 文件提供四面墙和一张桌子；根据墙端点推导约 `25.53m2` 地板。
- PICO 真机严格使用 Room Capture；不允许回退到 Mock 数据。

该策略验证的是完整的数据转换和寻路行为，不把硬编码 Transform 直接交给角色。
但 Mock 快照与 Toren 可视客厅不是同一份空间数据，暂不声称两者家具坐标一致。

### 三、编译与测试

Unity 2022.3.38f1 编译通过，无 D1 脚本错误或警告。

Editor PlayMode 10 秒：

```text
NavMesh build: 12ms
Random targets: 2
Reached targets: 1
Travelled: 8.5366m
Outside floor: 0 frames
Obstacle penetration: 0 frames
Off NavMesh: 0 frames
```

Android IL2CPP 构建通过：

- APK：`/Users/bytedance/MateSoul/Builds/RoomNavigation_v001.apk`
- ABI：`arm64-v8a`
- 大小：约 `61MB`
- SHA-256：
  `a6031586e9b21cdd550e6985f9519b47c880547b5f47ba6e2c8c815bf9ae9678`

APK 已安装并运行于 `MateSoul_PICO_0_13`：

```text
NavMesh build: 20ms
Random targets: 2
Reached targets: 1
Travelled: 8.3296m
Outside floor: 0 frames
Obstacle penetration: 0 frames
Off NavMesh: 0 frames
```

报告：

`/Users/bytedance/MateSoul/项目沉淀资料/validation/room_navigation_v001.json`

### 四、关键问题与修复

1. 首次构建没有 NavMesh：将零厚度地面改为 `0.08m` 厚闭合 Mesh。
2. 加入桌子后通道被完全侵蚀：Unity 默认 Agent 半径 `0.5m` 与角色不符，
   改为冷月实际 `0.25m`。
3. 单纯目视无法证明避障：增加逐帧地板 Polygon、家具投影和 NavMesh 检查。
4. 模拟器曾离线导致 ADB 安装失败；重新使用
   `scripts/start-pico-emulator.command` 启动后完成闭环。

### 五、后续建议

1. 在 PICO 4 Ultra 真机执行首次 Room Capture，验证真实 Floor、Wall 与家具。
2. 确认 PICO Emulator Spatial Mesh 调试开关能否把 Toren 几何注入应用；
   若不能，则为 Toren OBJ 生成坐标对齐的专用 Snapshot。
3. 将当前模板包名
   `com.UnityTechnologies.com.unity.template.urpblank` 改为正式 MateSoul 包名。
4. 后续把 `RoomRandomWalker` 接入统一 ActionCoordinator，不让自治移动成为
   第二套角色控制入口。

## Phase AppleCutSet 工作汇报：苹果预切模型与实机切割契约（2026-09-30）

### 一、完成内容

- 新建独立资产目录：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/AppleCutSet/`。
- 从同一个四向对称参数化母体生成完整苹果、半苹果和四分之一瓣，
  不是分别目测建模；切面包含果肉、果核膜和种子。
- 交付三套Blend、GLB、FBX、三张PBR贴图、12张正式渲染图、4张中文审核图、
  JSON验收报告和Unity切水果实现建议。
- 所有状态保留完整苹果中心为同一局部原点：
  完整苹果可用`0°/180°`两个半边替换；四瓣可用同一小瓣的
  `0°/90°/180°/270°`旋转生成，不使用负缩放。
- 半苹果提供独立`Apple_Stem_Transferable`；四分之一瓣不复制果梗，
  运行时只把现有果梗转交给唯一一瓣。

### 二、规格与验证

| 状态 | 果体尺寸/mm | 网格 | 三角形 | 图片 |
|---|---|---:|---:|---:|
| 完整 | 90×90×84 | 2 | 14,364 | 2 |
| 半边 | 45×90×84 | 5 | 8,680 | 3 |
| 四分之一 | 45×45×84 | 5 | 4,882 | 3 |

- 参考图没有尺寸标注；采用常见90mm直径苹果，果梗使完整/半边总高达到103mm。
- Blend、GLB、FBX独立回读全部通过：尺寸、网格数、三角形数和贴图数一致；
  原始退化面、焊接后坍塌面、边界边、非流形边和相邻面朝向错误均为0。
- 旋转拼合验证覆盖7,042个果皮表面顶点：
  两半和四瓣重组后缺失/多余顶点均为0，量化容差为0.001mm。
- 统一合集刷新为17个独立GLB、56张图片、7个同内容副本去重；
  新增的三种苹果模型已分别存入`Models/`和对应`Textures/`目录。

### 三、实现决策与后续

- 实机不建议做运行时布尔切割；采用预切模型状态机，
  避免Android/PICO上的拓扑、UV、GC和CPU峰值不确定性。
- 当前只交付静态美术资产，没有修改Unity生产场景或角色资产，也没有附带
  Collider、Rigidbody和刀具脚本。
- 后续建议先完成“玩家持刀 + 预切替换”的PlayMode闭环，再通过
  `CutAppleAction`接入`AvatarActionCoordinator`和角色Animation Rigging。
- 旧尺寸试制文件因资产保护策略未删除；合集脚本通过
  `SUPERSEDED_MODEL_NAMES`明确排除，不进入正式统一交付。

## Phase AppleCutSet Visual Rework V2 工作汇报：外观重建（2026-09-30）

### 一、重建原因与修改

- 用户明确否定首版外观。首版虽然通过尺寸、拓扑和拼合检查，但轮廓偏方、
  果皮偏塑料、果肉层次弱，果核和种子有贴片感；技术PASS不等于美术通过。
- 将纵向轮廓改为上肩饱满、下半收窄，保留四向对称和预切拼合协议。
- 重做深红果皮Base Color，新增独立粗糙度贴图，降低Coat与微法线强度。
- 果肉提亮并新增纤维法线；切面增加约0.6mm实际果皮薄边。
- 果核膜降低色差；每颗种子增加浅凹籽腔并压低突出高度。
- 果梗改为非均匀截面和弯曲中心线，不再是直圆锥。
- 被否定版本保存为
  `AppleCutSet/reports/pre_visual_rework_v1.zip`，未破坏回退路径。

### 二、最终统计与验收

| 状态 | 网格 | 三角形 | 实际图片 |
|---|---:|---:|---:|
| 完整 | 2 | 14,364 | 3 |
| 半边 | 8 | 9,888 | 5 |
| 四分之一 | 9 | 6,098 | 5 |

- Blend、GLB、FBX回读全部通过；尺寸、网格、三角形和图片数一致。
- 原始退化面、焊接后坍塌面、边界边、非流形边和面向错误均为0。
- 7,042个共享果皮顶点的两半/四瓣旋转拼合仍为完全匹配，缺失和多余均为0。
- 统一合集保持17个独立GLB，实际图片由56张更新为61张；
  三个苹果GLB均与最新验收报告哈希一致。
- 更新正式效果图、三套多视图、README、重建脚本和贴图生成脚本。

### 三、边界

- 本轮修复的是美术资产和交付质量，用户尚未对Visual Rework V2给出最终确认。
- Unity Collider、Rigidbody、刀具命中、Prefab和PICO真机切割仍未实现。

## Phase AppleCutSet Visual Rework V3：圆形轮廓修正（2026-09-30）

- 用户反馈V2苹果仍不够圆。根因为`Z≈30mm`的半径接近最大值，形成近直侧壁。
- 将母体改为接近球形的连续径向曲线：最大宽度回到中部，顶部、侧面和底部
  连续收拢；四次/八次横截面扰动由`1.2%/0.4%`降到`0.8%/0.2%`。
- 完整、半边和四分之一瓣由同一新母体重建，果体尺寸仍为
  `90×90×84mm`，运行时旋转替换协议不变。
- 最终统计：完整13,596三角形，半边9,450三角形，小瓣5,852三角形。
- Blend、GLB、FBX全部回读通过；6,658个果皮表面顶点在两半和四瓣重组后
  缺失/多余均为0。统一合集仍为17个模型、61张实际图片。

## Phase D2 工作汇报：正式开发前 Emulator 原生空间基线

### 一、完成内容

- 新增 `PicoSpatialMeshRoomMapProvider`，PICO Emulator 运行时改用 Toren
  原生 Spatial Mesh，不再使用坐标不一致的 PICO MockSceneData。
- 新增 `RoomSurfaceMesh` 数据结构；Runtime NavMesh 直接消费 48 个原生
  三角网格块，Floor 语义顶点仅负责生成移动采样边界。
- Editor Mock、Emulator Spatial Mesh、真机 Room Capture 三条数据路径分离，
  真机和模拟器均不允许静默降级到 Mock。
- 设备端关闭粗调试盒；角色初始位置优先放到 HMD 正前方约 `2.2m`。
- 增强 JSON 报告，记录 surface block、顶点、三角形、地图 Bounds 和角色终点。
- 新增 `/Users/bytedance/MateSoul/scripts/build-room-navigation.command`，
  复用 Unity Hub License 会话执行可重复 Android 构建。
- 修复 PICO XR 0.13 后台日志线程访问 `Time.frameCount` 导致的 IL2CPP
  SIGABRT；后台日志降级为无 Unity API 的格式。

### 二、原生能力结论

PICO Emulator 6.2.0 实测：

```text
Scene Capture provider start: SUCCESS
Scene Anchor query: SUCCESS
Scene Anchor count: 0
XRMeshSubsystem running: true
Spatial Mesh query: SUCCESS
Mesh blocks: 48
Vertices: 75,931
Indices: 289,074
```

结论：模拟器支持并实际注入 Toren Spatial Mesh；无需生成 Toren OBJ 静态
Snapshot 回退。Scene Capture 当前没有预生成 Anchor，不能作为模拟器导航源。

能力报告：

`/Users/bytedance/MateSoul/项目沉淀资料/validation/room_environment_capability_v001.json`

### 三、编译与运行验证

Unity 2022.3.38f1 Android IL2CPP 构建成功：

- APK：`/Users/bytedance/MateSoul/Builds/RoomNavigation_v001.apk`
- 大小：`79,791,453 bytes`
- SHA-256：
  `a2fff052e6aa910a565761282e71d14116654ab2d6d470703da5678c24493976`

最终模拟器 10 秒验证：

```text
source: PICO Emulator Spatial Mesh
floor area: 55.8912m2
map bounds: 11.2609 x 3.4318 x 6.3464m
surface meshes: 48
surface vertices: 75,931
surface triangles: 96,358
NavMesh build: 15ms
random targets: 2
reached targets: 1
travelled: 9.5838m
outside floor: 0 frames
obstacle penetration: 0 frames
off NavMesh: 0 frames
result: PASS
```

测试结束后应用 PID 仍存活；系统两次下发 `SDKLoglevelChanged`，未再次崩溃。

最终导航报告：

`/Users/bytedance/MateSoul/项目沉淀资料/validation/room_navigation_v001.json`

### 四、关键问题与处理

1. 首次原生转换把语义点压成 AABB，14 个粗包围盒覆盖整块地面。
   改为直接烘焙原始三角网格，语义盒只保留为诊断元数据。
2. Unity batchmode 默认 Untitled Scene，Additive 新建场景会失败。
   构建器按是否存在可恢复场景选择 Single/Additive。
3. Trae 沙箱不能写 Unity License 数据库。构建脚本从 Hub 启动的 Editor
   读取当前会话参数，再切换到 batchmode。
4. PICO SDK 的后台日志回调访问 Unity `Time.frameCount`，导致 IL2CPP
   线程安全异常和 SIGABRT。日志封装增加线程安全降级后闭环通过。
5. `adb screencap` 不包含 VST 合成层，截图为黑底，不能用作视觉验收；
   临时截图已清理，仅保留结构化数值报告。

### 五、后续建议

1. 在 PICO 4 Ultra 真机执行 Room Capture，校验真实 Floor、墙体、家具语义和
   Tracking Origin 重定位。
2. 将 `RoomRandomWalker` 接入 `AvatarActionCoordinator` 控制 Lease，
   保持“安全 > 玩家交互 > 指令 > 自治活动”的唯一控制入口。
3. 将模板包名 `com.UnityTechnologies.com.unity.template.urpblank`
   改为正式 MateSoul 包名。
4. 正式场景增加用户可见的空间数据授权/扫描状态，不把初始化失败只留在日志中。

## Phase D2.1 工作汇报：SDK 路线口径纠正

### 一、问题

旧文档 `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_首版开发计划.md`
仍把 “配置 PICO Spatial SDK BOM、Kotlin、Compose、FullStage” 写成执行步骤，
与 Phase D0 后已经生效的 Unity 路线冲突。

### 二、纠正结果

- 旧首版计划已标记为归档，BOM/FullStage 步骤改为禁止执行的历史内容。
- `MateSoul_Demo开发方案.md` 增加平台硬约束：
  当前只使用 Unity 包 `com.bytedance.pico.xr`。
- `technical_docs.md` 和 `work_reports.md` 顶部增加当前唯一生效路线声明。
- 道具文档中的运行时建议统一改为 PICO Unity SDK / Unity 导入说明。
- 新增 `/Users/bytedance/MateSoul/AGENTS.md`，阻止后续 Agent 误配置
  `com.pico.spatial:bom` 或创建 Kotlin/Compose 平行工程。

### 三、当前工程证据

```text
Unity: 2022.3.38f1
Unity package: com.bytedance.pico.xr
Package source: file:/Users/bytedance/MateSoul/XR
Build target: Android ARM64 / IL2CPP
```

本次仅修正文档和工程规则，不修改已通过验证的 Unity Runtime、APK 或导航报告。

## Phase Framework F2 工作汇报：内核实现与首批测试（2026-10-02）

工作仍在进行。已建立 `framework/game-runtime` 分支；根仓库原本没有任何
commit，未批量加入现有资产或本地工具数据。

已将“稳定参数可写代码默认值，克制配置/环境变量/保护层”的要求加入 AGENTS
和当前开发方案，新增框架实施计划。

已实现纯 C# 动作协调、控制资源、物品注册/占用、情绪时钟、可取消对话编排、
存档 DTO。26 项 NUnit 测试通过，包括等待旧动作退出后接管、原子资源冲突、
启动/更新/清理异常、目标移除、情绪退出和过期 AI 回复。

纯 C# 编译和测试通过；Unity 首次 batchmode 测试被用户目录许可数据库的
沙箱权限阻断。Unity 场景、UI、存档适配和设备构建尚在后续执行范围，
不能将本阶段描述为完整框架交付。

## Phase Framework F3 工作汇报：完成基础接入，验证入口切换（2026-10-02）

### 已完成

- 新增 GameSession/GameBootstrap，完成角色移动与动画、基础自主活动、
  物品定义/实例/占用、换装、情绪、对话网关、HUD、JSON 存档接入。
- 生成 `Assets/MateSoul/Scenes/MateSoul.unity`、独立角色 Variant、
  Idle/Walk BlendTree、陪伴灯内容资产。没有覆盖原角色资产。
- 接入 PICO Unity Spatial Anchor 服务，物品保存相对房间锚点的位置；
  定位失败保留原存档内容。该设备能力仍需真机验证。
- 所有来源通过共享动作目录/协调器提交行为；默认速度、权重和阈值留在局部代码，
  没有增加游戏环境变量或通用配置中心。

### 验证与卡死处理

- 运行时和 Editor 代码静态编译通过，Unity 编辑器实际编译通过。
- 纯 C# 领域测试最新 28/28 通过。
- 命令行 Unity 在启动 ADB/Bee 子进程时出现
  `mono_os_sem_post: semaphore_signal failed with error 15`，遗留进程持续挂起。
  已结束该进程并移除本次新增的批处理启动脚本。
- 改用 Unity Hub + 编辑器内置 TestRunnerApi，已实际完成测试运行。
  最新 33 项中 32 项通过；剩余一项是首个 PlayMode 用例 SetUp 空引用，
  不是整体验证通过。
- 后续重跑遇到桌面锁屏，界面工具明确拒绝操作。没有继续尝试锁屏操作。

### 未完成与下一步

继续定位剩余测试初始化失败，完成生产场景 UI/动画视觉回归、Android IL2CPP APK
构建、模拟器原生 Mesh 导航回归。真机 Room Capture、锚点跨启动及性能继续以设备
实测为准。当前分支仍为 `framework/game-runtime`；未将无初始提交的整个仓库批量
加入 Git。技术交接见本日 F3 章节及
`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_游戏框架实施计划.md`。

## Phase Framework F4 工作汇报：测试全绿与 Android 构建（2026-10-02，交互验证进行中）

- Unity 测试 33/33 通过，最后无插桩运行结束时间 `08:34:26Z`。
  首个 UnitySetUp 的问题已确认：EnterPlayMode 恢复协程 PC 时跳过了 lambda
  闭包初始化。只将该处 WaitUntil 改为 while/yield；无业务兜底改动。
- 主场景正常显示生产角色、中文 HUD 和 Mock 房间，自主行走可见。
- 编辑器内 Android ARM64 / IL2CPP 构建成功，输出
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`。当时记录的
  742,307,345 是 Unity 构建总输出量，不是 APK 文件长度。
  SHA-256：`4744b7eaf8557631870a4517c2ca43810658b649515edf54b6e8a241edb28baa`。
- 模拟器安装/启动完成，设备日志 `16:48:02.683` 输出 MATESOUL_READY，
  数据源 PICO Emulator Spatial Mesh，48 blocks，NavMesh 38ms。
- 菜单自动化不稳定时使用临时 `Editor/FrameworkValidationTrigger.cs`；
  请求文件必须先写 `.pending` 再原子 rename 为 `.txt`，避免读取空文件。
  此临时桥接和调试插桩已在 F5 交付清理中删除。
- 尚未完成：HUD 鼠标输入确认、模拟器新 motor 运动不变量回归。
  构建保留原 Both 输入设置，Unity 有 Android 不支持该组合的提示。
- 编辑器曾在 Play 中发生源文件热重载，导致会话字段丢失和旧布料脚本索引异常；
  已退出 Play。后续必须等待程序集稳定后重新进入，不能把这一轮作为有效运行验收。
- 真机 Room Capture、空间锚点跨启动、真实模型网关仍需对应设备/服务验证。

## Phase Framework F5 工作汇报：框架交付与模拟器闭环（2026-10-02）

### 完成交付

- 正式场景由 `GameBootstrap` 显式装配动作协调、角色执行器、房间导航、自主行为、
  物品、情绪、对话、存档和 HUD；UI、AI 与自主活动不再各自执行角色动作。
- 稳定尺寸、速度、权重和阈值保留为局部默认值或 Inspector 字段；没有引入参数中心、
  环境变量矩阵、规则语言、服务定位器或并行恢复状态机。
- 最终 Android ARM64 / IL2CPP 开发包：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  79,893,920 字节，SHA-256
  `88e7f5628ae185050623acd393013cca4775b0799c84cf0e8a0c2d9ccb0f2538`。
  Unity 构建总输出量为 742,341,569 字节，不能与 APK 文件大小混用。

### 验证结果

- 纯 C# NUnit 28/28 通过；Unity TestRunner 最新 33/33 通过，
  结束时间 `2026-10-02T08:57:31Z`。两组包含重叠领域用例，不相加计数。
- HUD 通过 MouseState → InputSystem → XRUIInputModule → GraphicRaycaster →
  Button 的完整事件链验证暂停/恢复、自主停止/恢复、字幕关闭/开启，共 6 项通过；
  未直接调用按钮回调或业务方法。
- 最终 APK 在 `emulator-5554` 读取 PICO Emulator Spatial Mesh：
  48 blocks、地面 55.89 m²、NavMesh 34 ms。
- 新 `AvatarMotor` 的 30.22 秒只读回归通过：381 帧、3 次移动启动、
  累计 15.93 米、Animator Speed 峰值 1.10；越界、偏离 NavMesh、
  房间碰撞侵入均为 0。
- 结构化证据：
  `validation/game_framework_20261002.json`、
  `validation/framework_hud_20261002.json`、
  `validation/framework_navigation_20261002.json`。

### 清理与边界

- 已删除临时 `FrameworkValidationTrigger`、本次 debugger 日志/环境/会话记录和
  菜单请求文件，仅保留运行时代码、APK 与数值报告。
- 当前屏幕锁定，Unity GUI 未在清理后再次刷新；当前源码的独立 Runtime/Editor
  静态编译通过。下次解锁编辑器时 AssetDatabase 会同步删除项。
- 仍需真机验证 Room Capture、空间锚点跨启动、控制器射线和设备性能；
  真实模型网关需服务地址可用后验证。模拟器低帧率不作为真机性能结论。
- 项目仍使用模板包名且保留 Both 输入设置。Android 已警告 Both 不受支持；
  正式发布前需结合旧脚本依赖统一输入模式并回归真机输入。
- 根仓库无初始 commit；未批量提交既有大资产、SDK、设备私钥或本地工具目录。

## Phase Framework F6 工作汇报：模拟器 VST 房间接入（2026-10-02）

### 功能改动

- `GameBootstrap` 在所有 Android/PICO 运行路径统一请求
  `PXR_Manager.EnableVideoSeeThrough = true`。模拟器继续使用原生 Spatial Mesh
  做导航，真机继续使用 Scene Capture；没有引入 Spatial SDK/Kotlin 平行实现。
- `RoomNavigationSceneBuilder` 将 XR Camera 固定为透明黑背景并关闭 Camera HDR。
- `URP-HighFidelity.asset`、`URP-Balanced.asset` 关闭 HDR；
  `URP-Performant.asset` 原本已关闭。后处理保持关闭，满足 PICO VST 合成要求。
- Spatial Mesh 仍只承担 NavMesh 与碰撞，不额外渲染调试网格。

### 验证结果

- 静态 Runtime/Editor 编译通过；Unity TestRunner 最新 33/33 通过，
  结束时间 `2026-10-02T10:43:29Z`。
- PICO Emulator 日志确认 `XR_FB_passthrough Version=5`、彩色 passthrough 支持、
  `EnableVideoSeeThrough 1`、passthrough layer 创建成功并进入启用状态。
- 2160×2158 设备画面实际显示模拟器内置卧室，黑色背景消失；角色、World Space
  HUD 和控制器射线正常叠加。临时截图完成目视检查后删除。
- VST 开启下 30.27 秒运动回归再次通过：277 帧、4 次移动启动、15.37 米，
  越界、偏离 NavMesh、房间碰撞侵入均为 0。
- 最终 APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  79,893,918 字节，SHA-256
  `6aea4521a6e1efc152eae3753803753113bfceed7cd6edfae1171a61df1dc2d6`。
- 结构化报告：
  `validation/framework_vst_20261002.json`、
  `validation/framework_navigation_20261002.json`、
  `validation/game_framework_20261002.json`。

### 已知边界

- PICO Emulator 原生运行时在基础 VST 正常可见时仍高频输出
  `Passthrough effect data is NULL!`。额外启用 VST effect API 没有消除日志，
  已撤回该无效调用，不增加业务兜底。
- 该日志来自模拟器原生 passthrough 层；当前未观察到应用异常或视觉失败。
  真机仍需验证真实透视、Room Capture、锚点恢复、控制器交互及设备性能。

## Phase Framework F7 工作汇报：视角底部主导航 UI（2026-10-02）

### 功能完成

- 将原 620×590 大面板重构为视角底部五段式导航：
  `对话`、`互动`、`信息`、`虚拟物品`、`设置`。
- 按 1672×940 参考图完成视觉校准：824×160 深色半透明托盘承载五张
  145.6×140 近方形粉紫功能牌，间距 18；标签置底，不保留参考图中不存在的
  底部高光或选中菱形。
- 对话气泡和齿轮使用 96×96、4 倍超采样的程序化实心图标；手掌、信息和背包
  后续在 F8 按用户提供的局部参考图直接提取像素轮廓。所有按钮均不再用字符模拟。
- 模拟器最终实拍卡片中心色为 `RGB(192,154,187)`，图标为
  `RGB(255,251,226)`；导航底边完整且贴近画面底部。
- 设备端使用 World Space Canvas，并以平滑跟随保持在玩家视角下方；
  编辑器使用相同尺寸约束的 Screen Space Overlay。
- 点击页签时在导航上方展开统一内容区；再次点击当前页签可收起。
- 互动栏目提供 `一起散步`、`打招呼`、`坐下聊天`、`玩猜拳`
  四个纵向测试选项，点击后显示明确反馈，不伪装成已实现玩法。
- 原有对话、字幕、角色状态、移动/停留、换装、虚拟物品、暂停、定位及保存能力
  已迁移到对应页签，没有创建第二套动作执行入口。
- Android `activeInputHandler` 从 Both 改为 New Input System；
  正式场景和 PICO/XRI 均使用新输入，消除了 Android 构建确认框及警告。

### 验证结果

- 静态 Runtime/Editor 编译通过。
- Unity TestRunner 34/34 通过，新增
  `BottomHudExpandsVerticalInteractionActivities`，覆盖五个导航按钮、
  PointerClick 展开、四项纵向顺序、活动反馈与再次点击收起。
- 2160×2158 模拟器画面确认导航栏位于视角底部中央，文字完整，
  未遮挡角色主体，VST 卧室仍正常显示；临时截图已删除。
- 最终 APK：79,905,841 字节，SHA-256
  `5d20b6ca0da97f1e87360bac559e0c0bbab3fa86fffe3881712eccbec7d54174`。
- 同一 APK 最终 30.07 秒导航回归通过：540 帧、3 次移动、14.47 米，
  越界、偏离 NavMesh、房间侵入均为 0。
- 结构化报告：
  `validation/framework_ui_20261002.json` 和
  `validation/game_framework_20261002.json`。

### 已知边界

- 互动选项目前仅验证 UI 交互反馈，后续需通过 ActionCatalog 注册真实活动，
  不能在 HUD 内直接驱动 Animator 或 Transform。
- 模拟器随机路径在不同视觉迭代中曾出现 24 帧和 5 帧 NavMesh 采样偏离；
  最终同一 APK 清理报告后重启，回归为 0。后续导航改动仍需保留多轮随机路径测试。

## Phase Framework F8 工作汇报：导航图标像素级回收（2026-10-02）

### 功能完成

- 从用户本轮提供的 916×186 局部参考 PNG 中直接提取 `互动`、`信息`、
  `虚拟物品` 三枚图标的 80×80 原始窗口，不再用程序化几何逼近。
- 新增透明资源：
  `Assets/MateSoul/Runtime/Resources/MateSoulUI/Interaction.png`、
  `Information.png`、`Items.png`。正式资源为 192×192 RGBA，
  保留参考图的抗锯齿轮廓，并统一为 `RGB(255,251,226)`。
- `GameHud.CreateIconSprite` 对三枚图标使用 `Resources.Load<Texture2D>`；
  对话气泡和齿轮继续使用已有程序化生成。资源缺失时直接报错，不静默降级。
- 主导航图标框从 70×70 校准为参考取样对应的 80×80，顶部偏移 9；
  手掌圆弧掌心、衬线信息符号、背包提手/侧袋/前袋线均与参考像素一致。

### 验证结果

- Runtime/Editor 静态编译通过；Unity TestRunner 34/34 通过，
  结束时间 `2026-10-02T14:32:01Z`。
- PICO Emulator 2160×2158 实拍和 900×300 局部放大确认三枚图标均正确加载，
  轮廓与参考图一致；临时参考副本和截图在收尾时删除。
- 最终 APK：79,907,577 字节，SHA-256
  `a15c89e9feab53bf288a8d7e5b09b7112c4dd47ab0b9c8449c280497325528b5`。
- 同一 APK 第二轮 30.00 秒导航回归通过：1096 帧、5 次移动、15.38 米，
  越界、偏离 NavMesh、房间侵入均为 0。首轮 6 帧随机偏离不作为最终证据。

## Phase Framework F9 工作汇报：虚拟桌椅、避障放置与 XR 拾取（2026-10-02）

### 功能完成

- 将 Lavender Chibi 桌子和椅子接入正式 Unity 内容目录，生成带
  `BoxCollider`、`NavMeshObstacle` 和准确尺寸的 prefab/`ItemDefinition`。
  桌子尺寸为 0.60×0.40×0.75 m，椅子为 0.36×0.38×0.68 m。
- 虚拟物品栏目改为从注册目录动态生成 `桌子`、`椅子` 按钮；点击后按当前视角
  请求前方放置。陪伴灯仅保留旧存档兼容，不再显示在目录中。
- `GameSession` 成为放置规则唯一所有者：先投影到房间主地面，再以 0.25 m
  径向步长、16 个方向搜索 2 m 内最近候选，并通过 NavMesh 与定向
  `Physics.OverlapBox` 双重检查墙壁、角色和已有物品。
- 同一放置规则用于首次生成和拾取释放，释放失败时回到上一个稳定姿态并立即存档，
  没有为 UI、手柄和眼手模式建立平行动作链。
- `GameItem` 运行时装配 kinematic `Rigidbody` 和 `XRGrabInteractable`；
  左右手柄侧握键 `gripPressed` 均可拾取，松开释放。拾取期间关闭
  `NavMeshObstacle`，防止导航切割跟随物体移动。
- 眼手模式采用“头显视野中心瞄准 + 右手拇指食指捏合拾取，松开释放”。
  `PXR_ProjectSetting.handTracking` 已开启，未占用 A/B/X/Y 按键。
- 新增中心 SphereCast 聚焦控制器和 URP Fresnel 透明叠加材质；只轻微增强边缘，
  不使用硬描边或复制膨胀网格。

### 验证结果

- Unity TestRunner 最终 35/35 通过，失败和跳过均为 0。新增用例覆盖桌椅目录、
  隐藏旧灯、头显高度请求回落主地面、阻挡点径向偏移、抓取组件和高亮材质恢复。
- PICO Emulator 6.2.0 实际加载 `PICO Emulator Spatial Mesh`。验证中发现并修复
  头显高度会让 `NavMesh.SamplePosition` 选中床面的缺陷；修复后桌椅落在主地面，
  Lavender 纹理、爱心镂空和边缘提示正常。
- 临时 Development Intent 放置桥仅用于模拟器无法自动点击裸 qemu 窗口时的
  渲染验收，已从源码和最终 APK 删除。最终普通启动输出
  `MATESOUL_READY ... items=0 navMs=45`，无 FATAL、空引用或启动失败。
- 最终 APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  79,914,170 字节，SHA-256
  `d39a33160ff7d5e7ca2bcac22b1ae6f24be110c5b707bddf1f23c15b4c88fa17`。
- 结构化报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/virtual_items_20261002.json`。

### 已知边界与后续建议

- 模拟器已验证物品运行时、材质、VST 合成和干净启动；真实手柄 Grip、真手捏合、
  空间锚点跨启动和真机性能仍需 PICO 设备实测。
- 当前角色初始 NavMesh 采样仍可能选择床面等可行走水平网格，这属于角色导航面
  语义过滤问题；物品系统已独立固定使用主地面，不受该问题影响。
- 后续新增虚拟物品应继续扩展 `ItemDefinition` 注册内容，不在 HUD 或协调器中增加
  物品名称分支。

## Phase Framework F10 工作汇报：虚拟物品跨启动恢复修复（2026-10-03）

### 问题根因

- 旧实现把空间锚点加载成功作为物品系统唯一的就绪条件。PICO 锚点查询在启动阶段
  暂时失败或锚点已经失效时，`itemsRestored` 会永久保持 `false`。
- `PlaceItemAsync` 在该状态下只返回“仍在恢复”，导致桌子、椅子按钮在之后每次点击
  都无效；启动日志仍报告房间就绪，掩盖了物品恢复失败。
- 旧存档只保存锚点 UUID 和物品局部姿态，没有房间根姿态；锚点不可用后无法安全地
  把旧物品放回当前房间。

### 修复内容

- `PicoSpatialAnchorService.LoadAsync` 改为按 PICO 示例查询全部持久化锚点并匹配
  UUID，最多查询 6 次、间隔 400 ms，覆盖 Provider 刚启动时的数据更新窗口。
- `GameSaveData` 新增房间锚点位置和旋转快照；`JsonGameSaveStore` 校验有限数和
  单位四元数。锚点暂时不可用时，只有在全部物品仍落在当前主地面且占用合法时，
  才使用该姿态恢复。
- `GameSession` 明确区分恢复中、恢复成功和不可恢复三种状态。不可恢复时目录按钮
  不再永久锁死，首次成功点击会创建新锚点和新布局。
- 新布局创建采用事务式替换：新锚点、生成和存档全部成功后才提交；失败时恢复原存档，
  不会提前覆盖仍可能恢复的旧布局。
- `Save()` 在物品未恢复时不再用空列表覆盖旧物品；`GameBootstrap` 的状态和
  `MATESOUL_READY itemState` 会明确暴露恢复失败。

### 编译与测试

- Unity TestRunner：37/37 通过，0 failed、0 skipped。新增用例覆盖锚点暂时不可用时
  的房间姿态恢复，以及旧存档锚点失效后由目录点击创建新布局；原跨启动测试增加
  世界坐标一致性断言。
- Unity 2022.3.38f1 Android ARM64/IL2CPP 构建通过；移除一次性构建触发器后再次
  干净启动，无 C# 编译错误。
- 最终 APK：`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  79,915,653 字节，SHA-256
  `08b8a55455fdd989ef81eea60b39494ac2c29ad45b3acf323df9dbdb62ca3166`。
- 结构化报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/virtual_items_restart_fix_20261003.json`。

### 兼容边界

- 修复后产生的存档同时具备持久化锚点和经校验的房间姿态回退，可抵抗锚点查询的
  暂时失败。
- 修复前已经产生的存档没有房间姿态。其旧物品只能在原 PICO 锚点仍存在时恢复；
  若锚点确已失效，第一次点击桌子或椅子会明确创建新布局，旧物品不会被错误放到
  世界原点。

### PICO Emulator 真实重启验证

- 通过 macOS Terminal 在 Trae 沙箱外启动原有 `MateSoul_PICO_0_13` AVD，
  `emulator-5554` 正常上线；没有清空 AVD、应用数据或外部存档。
- 修复版首次冷启动读取到原有 4 件物品，日志为
  `items=4 itemState=ready`，锚点暂不可用时按已校验房间姿态恢复；
  画面确认 2 张桌子和 2 把椅子均可见。
- 一次性 Development Intent 探针调用与 HUD 相同的
  `GameSession.PlaceItemAsync("table", pose)`，物品数从 4 增至 5，
  新桌子世界坐标为 `(0.096, 0.432, 0.747)`。
- 强制停止应用后无参数冷启动，5 件物品逐项恢复到相同坐标；
  重启前后 `matesoul-v1.json` SHA-256 均为
  `28c62ac422c0df2d536e6798eee4c4e1e4be219f2a5cfa002b2fe44058cb6e09`。
- 最后覆盖安装无探针的正式 APK 并再次冷启动，日志为
  `MATESOUL_READY ... items=5 itemState=ready navMs=42`，画面仍显示全部 5 件物品。
  验证探针、验证 APK、临时截图和 gRPC 工具均在收尾时删除。
- 真机仍需验证 Room Capture 坐标稳定性和原生持久化锚点成功路径；模拟器本轮实际
  覆盖的是“原生锚点暂不可用时，通过已校验房间姿态恢复”的路径。

## Phase Framework F11 工作汇报：现实椅子落座（2026-10-03，设备验证进行中）

- 已实现 `SeatGeometry`、`RealChairScanner`、`SitOnChairAction` 和
  `AvatarSeatingRig`。语义只用于筛选椅子网格；椅面、朝向、尺寸和空隙来自实际三角面。
  不能从 Chair 包围盒中心直接推导臀部接触点。
- `sit` 注册到原有动作目录，UI 与 AI 共享执行器和椅子占用；支持走近、精确对齐、
  坐下驻留、起身、抢占退出和目标失效退出。菜单“互动”新增可执行的“坐下聊天”和“起身”。
- 运行时角色 Prefab 增加 Generic 骨骼绑定、骨盆与双腿接触姿态、手掌姿态；
  原有 `SkirtPoseGuard` 作为裙摆骨骼唯一所有者增加椅面支撑约束。
- 原 37 项回归和新增 11 项几何用例合计 48/48 通过；追加动作回归后，
  座椅专项 12/12 通过，覆盖真实 Unity Animator/CharacterController 执行、
  骨盆位置、双脚、朝向、正常起身和目标失效后的控制权释放。
  Unity C# 与 IL2CPP 编译完成；最后 Gradle 打包收到外部 stop 命令而中断，
  改用项目内独立 Gradle 用户目录后打包成功，`apksigner verify` 通过。
  APK：`/Users/bytedance/MateSoul/Builds/MateSoul_RealChair_Development.apk`，
  66,549,695 字节，SHA-256：
  `882bb97c196e2a2d6bdf01fb303d1607b38e2604d0207d98e92f13f9bd52eecd`。
  安装和视频尚未完成。
- 回归发现既有物品交互 HTTP 调试埋点在 Editor 抛异常并中断物品绑定；
  已将这些埋点限制为非 Editor 的 Development 构建。
- 仅连接到 `emulator-5554`（PICO Emulator）。后续视频必须明确标注模拟器；
  不得声称完成真实头显的椅子扫描精度验收。网格密度不等于定位精度，
  SDK 网格查询也不等同于对移动家具的实时物体跟踪。

### F11 设备验证进展（2026-10-03 19:17）

- 已覆盖安装并启动更新后的落座 Development APK，82,272,567 字节，
  SHA-256 `00d58d52571ec92485270d7ca113d366a5367eba0ade3670c69426015831b795`，
  签名校验通过。上述 66 MB 哈希对应第一轮包。
- 修正诊断网格序列化：SDK 的 `PxrSpatialMeshInfo` 没有可用于 JsonUtility 的
  Serializable 标记，改为显式可序列化 DTO；修正 HUD 后续动作失败的反馈。
- 模拟器 SDK 实际返回 48 个网格块、86,323 个有效三角面，其中 640 个 Chair
  三角面。当前高度范围内的水平连通面只有一个，约 0.309 × 0.309 m、
  0.0747 m²，且没有满足条件的靠背。拒绝落座符合当前保守检查规则。
  原始数据与解析结果在 `.artifacts/seating/device/mesh.json`、
  `.artifacts/seating/mesh-analysis.json`。这不能证明实体设备的识别精度。
- 原生库经项目内 C# / UnityLinker / IL2CPP 生成工程重编译与链接，
  Gradle 记录 `BUILD SUCCESSFUL in 42s`。直接启动 Unity 会被沙箱拒绝写入
  用户目录 CoreBusinessMetrics 缓存；没有修改系统权限。
- 设备日志仍发现既有物品焦点 HTTP 埋点抛出 `Insecure connection not allowed`，
  需将其限制到显式启用的诊断构建。标准椅测试模型、完整动作录制尚未完成。

### F11 完成记录（2026-10-03）

- 椅子扫描改为消费 `PXR_Manager.SpatialMeshDataUpdated`，由 PICO Manager 唯一持有
  原生网格查询；移除第二条轮询链路，避免模拟器出现 `ERROR_LIMIT_REACHED`。
- 既有物品焦点 HTTP 埋点增加显式 `MATESOUL_ITEM_FOCUS_DIAGNOSTICS` 编译开关，
  普通 Development 包不再触发 Android 明文 HTTP 限制。
- 设备连拍发现坐姿脊柱角度被逐帧累加。`AvatarSeatingRig` 已改为相对中立骨架
  计算绝对脊柱目标；新增坐稳后连续 30 帧不漂移断言，座椅专项测试 12/12 通过。
- PICO Emulator 上使用明确标注的 0.48 × 0.44 m、椅面高 0.45 m 标准椅几何模型，
  完成走近、对齐、坐下保持和起身。坐稳时骨盆目标误差约 1.23 mm，
  双脚高度误差均小于 0.2 mm；起身后角色胶囊与 FullBody 控制权正常恢复。
- 最终 APK 为 82,267,836 字节，SHA-256
  `b1fe888e8ebf347c39750d8fbade55fdd9e0f3858c60e14005c4fcd0d721ad96`，
  Unity 完整构建与签名校验通过。
- 演示视频：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/real_chair_seating_emulator_20261003.mp4`。
  视频为 32 张连续模拟器设备渲染帧编码，画面已明确标注“非真实椅扫描”；
  原因是模拟器 Android `screenrecord` 对 XR 主显示只重复静止帧。
- 结构化报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/real_chair_seating_20261003.json`。
  当前没有物理 PICO 头显连接，现实椅识别精度与物理接触仍待真机验收。

### F11 原生房间椅闭环修正（2026-10-04）

- 用户复核指出，上一版合成标准椅只能证明动作链路，不能证明 PICO 房间感知链路。
  本轮废弃该视频作为验收证据，改为全程使用 PICO Emulator 原生房间网格。
- 实际网格共 48 个 block、86,323 个三角面。SDK 的 `Chair` 标签落在带杯子的
  圆形边桌上，而可见编织扶手椅位于 `Unknown` block。新增严格几何兜底：
  椅面尺寸、平面 RMS、43/49 支撑采样、上部结构面积和方向一致性必须同时成立，
  并在 `SeatTarget` 标记 `GeometryInferred` 和 `SourceId`。
- 净空诊断发现扶手椅原先被自身网格拒绝：68 个碰撞三角形全部属于目标椅 block，
  外部来源为 0。现在目标椅自身网格负责证明椅形，但不作为“进入该椅”的外部障碍；
  其他 mesh source 仍全部参与坐下/起身扫掠检查。
- 为避免再次选择圆桌，当前功能对无靠背紧凑语义面 fail-closed。最终冷启动只发布
  1 个候选：中心 `(-3.062, 0.765, -0.055)`、尺寸 `0.801 × 0.708 m`、
  `GeometryInferred=true` 的原生扶手椅。
- 座椅专项回归 `19/19` 通过。最终 APK 为 82,292,943 字节，APK v2 签名通过，
  SHA-256 `79fda5d64f6a1bd4ed9fe2b31d78d1af40f5838ddf392001a7e95c1e13477db4`。
  最终包在模拟器中从房间另一侧完成 `walking → docking → sitting → seated → standing`；
  坐稳时 Rig 权重 1、站立胶囊关闭、FullBody 租约持有，起身后均恢复。
- 原生录屏：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/matesoul_native_room_chair_complete_20261003.mp4`。
  视频由最终 APK 直接通过 PICO Emulator `screenrecord` 采集，48 秒、1080×1080、
  1 fps，48 个逐秒帧哈希均不同；画面同时保留被拒绝的圆桌，便于确认实际落座目标。
- 结构化报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/real_chair_seating_20261003.json`。
  本结论仅覆盖模拟器原生房间；物理头显上的软材质、遮挡、透明反光和家具移动仍待验收。

## Phase Framework F12 工作汇报：DeepSeek 对话与联网 MCP（2026-10-04）

### 一、完成内容

- 新增 `/Users/bytedance/MateSoul/services/ai-gateway` TypeScript 服务，统一封装
  DeepSeek `chat/completions`、角色提示、请求/响应校验、结构化日志和生命周期。
  API key 只从服务端 `.env.local` 读取，该文件被版本控制忽略，不进入 Unity 或 APK。
- 网关通过官方 MCP SDK 启动 stdio Web MCP，向每轮模型请求自动暴露
  `web_search` 和 `web_fetch`。工具是否调用由 DeepSeek 根据问题自动决定；
  搜索/网页超时、结果大小和工具轮次均受配置约束。
- `web_fetch` 在请求前解析 DNS 并拒绝 loopback、私网、本地链路等地址；网页内容按
  不可信参考数据处理，不执行脚本。模型最终 JSON 会按 Unity 当轮提交的动作、目标能力
  和情绪目录再次校验。
- 新增 `ConversationGatewaySettings` 与 `HttpLLMProvider`。Unity 每轮动态提交历史、
  AI 可见动作、未占用目标和注册情绪，不包含供应商 key。AI 动作继续经
  `ConversationOrchestrator -> AvatarActionCoordinator` 执行，没有第二套角色控制路径。
- HUD 回复从气泡改为底部导航上方的无背景纯文字字幕；保留对话输入、发送、取消和字幕
  开关。世界空间画布继续跟随玩家视角，字幕与按钮不重叠。
- 新增仅 Development Build 装配的 `DialogueDeviceProbe`，通过
  `Application.persistentDataPath` 一次性读取请求并调用正式对话编排器，原子写出结果。
  Release 不包含该命令入口。
- 增加网关运行说明
  `/Users/bytedance/MateSoul/services/ai-gateway/README.md`，并补充 schema 拒绝日志。
  Unity `JsonUtility` 对空目标能力输出空串的问题在网关输入边界规范化为 `null`，
  内部领域语义不接收空串。

### 二、编译与测试

- 网关自动化测试 `11/11` 通过；覆盖 HTTP 契约、客户端认证、Unity 空串规范化、
  动态动作/目标/情绪、协议修复、MCP 自动调用、网页 SSRF 拒绝和结果格式。
- `npm audit --audit-level=moderate`：0 vulnerabilities。
- Unity TestRunner：`57/57` 通过，0 failed、0 skipped；结果：
  `/Users/bytedance/MateSoul/.artifacts/framework/unity-tests.xml`。
- Unity 2022.3.38f1 Android ARM64/IL2CPP Development 构建通过。APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，82,304,569 字节，
  SHA-256 `9b3a12b1707d03c56baf7028f52559be405e461892f74b078ff14fb7fba2d71c`。
- PICO Emulator 真实闭环通过。请求“请联网搜索今天北京的天气，并用一句中文告诉我。”
  经 Unity 发往网关，DeepSeek 自动调用 `web_search` 与 `web_fetch`；网关 3 次模型调用、
  5,232 ms、5,310 tokens，Unity 记录 5,921 ms，最终状态 `Replied`。
  请求 ID：`1f80d734802f46fb9a4b69fc377e596a`。
- 设备画面确认回复为按钮上方无背景纯文字。临时截图和设备探针文件已删除，结构化结果
  保留在 `/Users/bytedance/MateSoul/.artifacts/framework/dialogue-e2e-validation.json`。

### 三、问题与边界

- 首次查询 PICO 规格时，模型连续请求网页达到 `maxToolRounds=4`，网关按上限终止并
  返回 502；该限制用于防止无界工具循环，没有为单次样例放宽。随后实时天气请求在
  `web_search + web_fetch` 两轮工具链内正常完成。
- 本地 Development 使用 `127.0.0.1:8787` 与 `adb reverse`。正式部署仍需 HTTPS、
  进程级密钥管理，并通过 `clientTokenEnvironmentVariable` 启用客户端认证。
- 本轮验证覆盖 PICO Emulator 与真实 DeepSeek/MCP 外网调用；物理 PICO 头显的网络
  可达性、弱网和断网恢复仍需独立验收。语音输入/语音合成不在本阶段范围内。

## Phase Framework F13 工作汇报：现实物品目录与交互模块化（2026-10-04）

### 一、完成内容

- 新增
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/RoomNavigation/Runtime/RealitySemantic.cs`，
  用项目领域枚举完整覆盖当前 PICO SDK 的 23 个 `PxrSemanticLabel`，并区分
  `Surface`、`Item`、`Person`、`SyntheticBoundary` 及对应几何载体。
  当前现实物品清单包含门、窗、桌、沙发、椅子、窗帘、柜子、床、植物、屏幕、
  冰箱、洗衣机、空调、灯和墙面装饰，共 15 类。
- `RoomMapSnapshot` 新增 `RealityItemObservation` 与 `RealityItems`。Mock Scene、
  Spatial Mesh、Scene Capture 三类 provider 现在统一产出稳定 ID、语义、中心、
  朝向和尺寸，不再用导航层的 `RoomObstacleKind` 充当业务物品类型。
- 新增 `RealityInteractionCatalog`，明确维护 `Chair → sit`、`Bed → lie` 映射。
  映射表示产品语义，不等于动作已经可执行；未实现床姿态解析和躺下执行器前，
  床不会向 `ItemRegistry` 发布 `lie` capability。
- 新增 `RealityItemDirectory`，作为现实物品实例唯一目录。SDK 观测先进入目录；
  只有专用交互模块验证几何和净空后，才把 capability 投影到现有 `ItemRegistry`。
  虚拟物品、现实物品和 AI/UI 仍共用原有 `ActionCatalog` 与
  `AvatarActionCoordinator`，没有复制动作执行系统。
- `RealChairScanner` 只负责椅面、靠背、稳定性和净空，不再直接创建
  `ItemInstance`；可用状态通过目录发布或撤回 `sit`。`GameSession` 改为
  `ConfigureRealityItems`，统一加载现实物品目录并装配当前已实现的椅子模块。
- `ItemRegistry` 支持动态增加/撤回 capability；预约会在 capability 被撤回后立即
  失效。目录还拒绝现实物品 ID 与其他物品所有者冲突，避免误改虚拟物品能力。

### 二、编译与测试

- Unity TestRunner 完整回归 `60/60` 通过，0 failed、0 skipped；新增用例覆盖
  SDK 语义目录完整性、椅子/床交互映射、观测与可执行目标隔离、capability 撤回、
  ID 冲突以及 Mock provider 的 Bed/Lamp/Door 输出。
- Unity 2022.3.38f1 Android ARM64/IL2CPP Development Build 成功。APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_RealChair_Development.apk`，
  同步交付名 `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  82,312,193 字节，APK Signature Scheme v2 校验通过，SHA-256
  `bff6959e693877386377151e82b9183f39d7a11848fdaded5ff682ed0b0813d4`。
- PICO Emulator 原生 86,323 三角面网格输出 28 个现实物品实例：
  Bed 2、Cabinet 3、Chair 2、Curtain 3、Door 1、Lamp 8、Plant 6、
  WallArt 1、Window 2。
- 两张床均为 `declaredInteractions=[lie]`、`availableInteractions=[]`；
  唯一通过椅形与净空验证的几何推断扶手椅为 `availableInteractions=[sit]`。
  模块化后坐下和起身再次通过，胶囊与 FullBody 控制权均正确恢复。
- 结构化报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/reality_item_modularization_20261004.json`。

### 三、边界与下一步

- 本阶段没有伪造 `LieOnBedAction`。下一步需实现床面/床头朝向解析、安全接近位、
  躺姿目标与起身路径，再由床模块发布 `lie` capability。
- SDK 语义只证明“SDK 将该网格标成某类物品”，不能直接授权交互。每种交互仍需
  独立几何验证器，沿用椅子模块的 fail-closed 原则。
- 当前设备结论覆盖 PICO Emulator Spatial Mesh；Scene Capture 与物理头显上的
  Box2D/Box3D 尺寸、朝向和家具移动仍需真机验证。

## Phase Framework F14 工作汇报：角色人设与运行时 AI 上下文（2026-10-04）

### 一、完成内容

- 新增结构化人设文件
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/LengyuePersona.json`。
  身份固定为冷月、16 岁、女、155 cm；定义外向性、尽责性、开放性、亲和性和
  情绪稳定性五个维度，每个维度四个玩家可选档位。
- 新增 `CharacterPersonaState`。玩家存档只保存五个选项 ID，不复制人设描述；
  旧 `version=1` 存档没有 `personality` 字段时迁移为默认选项，身份字段和选项目录
  仍由版本化人设文件唯一提供。
- 设置页拆分为“角色性格/系统”两页。角色页只读展示基础身份，并从人设文件动态生成
  五个性格按钮；点击循环选择并立即写入现有 `GameSaveData`。
- 新增 `CharacterRuntimeContextProvider`，从 Wardrobe、MoodSystem、
  AvatarActionCoordinator、ActionCatalog 和统一 ItemRegistry 捕获不可变内存快照。
  正式对话不读取或解析运行时文件。
- `IActionRuntimeStatus` 让每种 Action 自己报告 `phase/posture`。协调器只发布通用
  Action 快照，不增加 `SitOnChairAction/CutAppleAction` 类型分支；并行执行、退出和
  排队动作以数组形式传给 AI。
- 现实物体与虚拟物体继续使用同一个 `ItemRegistry` 和 Action 协议。AI 只读取
  `action + phase + target`，不需要选择现实/虚拟两套执行系统。
- Unity 每轮向网关新增 `persona/runtime`。网关严格校验固定身份、五维人格、衣装、
  情绪、姿态、Action 状态/阶段和最多 8 条近期事件，再作为事实数据注入 Prompt。
- 增加 `AI_CONTEXT_CAPTURED` 日志，记录 revision、采集耗时和请求字节数；网关日志
  记录衣装、情绪、姿态及活动 Action，但不记录 API key 或完整用户输入。

### 二、问题修复

- Unity `JsonUtility` 会把无目标 Action 的 `null` 目标编码为三个空字符串。
  网关只在 HTTP 输入边界将“全空目标”规范化为 `null`，领域层仍不接受空目标。
- 首次未预热的设备采集为 39.057 ms，随后稳态为 3.180、0.508、0.210 ms。
  `GameSession` 现于装配阶段预热一次快照；最终冷启动首轮降至 0.082 ms。
- 模型曾对“当前在做什么”错误调用 `web_search`。Prompt 现明确以 runtime snapshot
  作为游戏状态唯一权威，身份、衣装、情绪、姿态、Action 和游戏目标禁止联网查询。
- DeepSeek 实测会偶发返回空 `content`。复用原有单次协议修复处理空回复，不增加
  第二套重试状态机；工具在修复调用中关闭。

### 三、编译与验证

- Unity TestRunner：`63/63` 通过，0 failed、0 skipped。覆盖固定身份、人设解析、
  性格跨启动恢复、旧存档迁移、衣装/情绪/活动 Action 快照和五个 HUD 选择控件。
- 网关测试：`13/13` 通过；`npm audit --audit-level=moderate` 为 0 vulnerabilities。
- Android ARM64/IL2CPP Development APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，82,330,843 字节，
  SHA-256 `485f5664dfc5896457c318b4724eefcaf31b2dde3233e66b158bc5ec11b44d93`。
- PICO Emulator 冷启动首轮请求正确回答“冷月、16 岁、155 cm、薰衣草常服”；
  请求 ID `d7d9bdccbb4349b499ed4b544eb26856`，上下文采集 0.082 ms、
  2,389 字节，网关 2,327 ms，Unity 3,010 ms。
- 活动 Action 请求 ID `5f23c51bfb4545e68c353d5f3bec3036`。网关收到
  `move:cancelling` 与 `stay:queued`、姿态 `standing`，没有调用 MCP；
  上下文采集 0.233 ms、3,082 字节，网关 1,751 ms、Unity 2,171 ms，
  单次模型调用后正确回答角色正停止导航。
- 结构化报告：
  `/Users/bytedance/MateSoul/.artifacts/framework/persona-runtime-context-validation.json`。

### 四、边界

- 近期事件仅为当前会话内最多 8 条，不是长期记忆；后续长期记忆不得直接复用该队列。
- 当前衣装以完整 Outfit 为单位。未来若出现可独立切换的首饰或手持物，应增加明确
  appearance slot，而不是解析 Renderer 名称。
- 本阶段验证覆盖 PICO Emulator。物理头显只影响网络与设备性能验证，不改变人设和
  Action 上下文的数据所有权。

## Phase Framework F15 工作汇报：AI 网关启动链路修复（2026-10-04）

### 一、问题与根因

- Unity 点击发送后提示“对话网关请求失败”。运行时配置仍正确指向
  `http://127.0.0.1:8787/v1/conversations/reply`，但本机 8787 端口没有监听进程，
  `/health` 返回 connection refused。
- 原实现只有网关源码和手工 `npm start` 操作说明；PICO Emulator 启动脚本没有
  启动网关，也没有自动建立 ADB reverse。功能实现完成不等于开发依赖已运行。

### 二、修复内容

- 新增 `/Users/bytedance/MateSoul/scripts/start-ai-gateway.command`：
  - 验证 `.env.local` 中存在服务端 DeepSeek 凭据，但不输出凭据；
  - 拒绝复用占用 8787 端口的非 MateSoul 服务；
  - 构建 TypeScript 后后台启动网关；
  - 轮询 `/health` 并校验 `service=matesoul-ai-gateway`；
  - 重复运行直接复用健康实例；
  - 日志和 PID 写入项目 `.dbg/ai-gateway/`。
- 修改 `/Users/bytedance/MateSoul/scripts/start-pico-emulator.command`。启动 Emulator
  前先保证网关健康，设备上线后自动执行 `reverse tcp:8787 tcp:8787`。
- 更新网关 README，把幂等脚本设为默认开发启动入口。

### 三、验证

- 两个 shell 脚本均通过 `bash -n`，且具备执行权限。
- 网关启动后 `/health` 返回 `status=ok`，重复启动没有产生第二个监听进程。
- 网关自动化测试 `13/13` 通过。
- 真实 DeepSeek + MCP 请求成功，请求 ID
  `a5d0cf64-e52d-49d4-9c1e-10c580218b17`，依次完成 `web_search` 和
  `web_fetch`，4.123 秒返回，响应 request ID 完全一致。
- 网关保持运行，Unity Editor 可继续使用当前
  `ConversationGateway.asset` 地址发送对话。

## Phase Framework F16 工作汇报：现实床躺卧交互（2026-10-04）

### 一、完成内容

- 新增共享网格源
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RealityMeshSource.cs`。
  PICO Spatial Mesh 的订阅、生命周期和世界空间三角面转换只有一个所有者；
  `RealChairScanner` 与新床扫描器消费同一不可变几何快照。
- 新增 `BedGeometry` 与 `RealBedScanner`。算法从同 source 连通面提取水平承托面，
  拟合最小面积 OBB，检查支撑覆盖率，合并重叠床架/床品 source，并依据床头上部结构
  判定朝向。人体区域或起身扫掠区域存在外部障碍时不发布 `lie`。
- Emulator 原生房间中的两个 Bed mesh block 被确认是同一张床的床架与床品，而非
  两张床。合并后唯一目标尺寸为 `1.910 × 2.095 m`，平面误差 `0.0016 m`；
  `FootEdge=1.540 m` 来自完整床架边界，避免将床尾 Dock 放到床架上。
- 新增 `LieOnBedAction`，实现
  `walking → docking → lying → resting → rising`。动作通过原有
  `AvatarActionCoordinator`、`ItemRegistry` 和控制权租约执行；运行中床 capability
  失效时反向起身并以明确错误结束。
- 新增 `AvatarLyingRig`。Animator 之后锁定 22 根角色骨骼的中性局部姿态，并复用
  `SkirtPoseGuard` 提供床面支撑。`AvatarMotor` 的椅子专用接触 API 已泛化为
  `BeginBodyContact/SetContactRoot/EndBodyContact`，坐下回归保持不变。
- `GameSession`、`GameBootstrap`、`GameSceneBuilder`、`GameHud` 与
  `SeatingDeviceProbe` 已接入床模块。互动菜单新增“躺床休息”，UI 与 AI 均通过注册的
  `lie` ActionDefinition 进入同一执行链路。

### 二、编译与测试

- Unity TestRunner：`68/68` 通过，0 failed、0 skipped，耗时 20.854 秒。
  床专项 5 项覆盖重叠 source 合并、无床头拒绝、净空拒绝、稳定发布唯一 capability、
  两轮躺卧/起身/目标丢失恢复。
- Android ARM64/IL2CPP Development APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_RealBed_Development.apk`；
  通用副本 `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`。
  两者均为 82,353,519 字节，APK v2 签名通过，SHA-256
  `936283143940c28d200ed47e7e4729df9ec62b41a9953af72252b53921cc200e`。
- PICO Emulator 使用 86,323 个原生房间网格三角面稳定识别 1 张可躺卧床和
  1 把可落座椅子，全程未创建床 fixture。躺稳时 Rig 权重 1、CharacterController
  关闭、FullBody 租约持有；主动起身后权重归零、碰撞体恢复、租约释放。
- 强制撤回床目标后，角色安全回到床尾，动作最终为 `Failed`，错误为
  “床面几何或可用空间已变化，已退出躺卧。”，碰撞体与控制权均恢复。
- 原生房间完整录像：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/matesoul_native_room_bed_complete_20261004.mp4`。
  时长 25.896 秒、2160×2158、约 1 fps，SHA-256
  `ca96643c0936e9c7ef6184070da8eda76bf1437b043373b15f54ee23bd941f27`。
- 结构化报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/real_bed_lying_20261004.json`。

### 三、踩坑与技术边界

- 仅用床面 OBB 的短边作为床尾会把 Dock 放到向外延伸的床架上。接近点必须依据所有
  合并床 source 的真实床尾边界计算，再通过 NavMesh 高度与水平偏差双重校验。
- Spatial Mesh 已携带逐顶点语义时，不能再用 Scene Capture 粗 AABB 覆盖 Unknown；
  否则与 Cabinet 包围盒重叠的真实扶手椅会被误标并导致椅子 capability 回归消失。
- 手动移动 Unity `Camera.main` 不会同步 PICO Emulator 的房间合成视角，所得截图不能
  用于判断角色与现实床接触高度。最终视觉证据必须冷启动并使用同一 HMD 追踪视角。
- 当前结论只覆盖 PICO Emulator。物理头显仍需验证真实床垫高度、透视遮挡、家具移动
  后 capability 撤回，以及 Scene Capture provider 的 Box3D 朝向。

## Phase Framework F17 工作汇报：泛化上床规划与防漂移修复（2026-10-04）

### 一、问题与根因

- 旧 `LieOnBedAction` 到达床尾后，在 2.2 秒内同时插值地面 Dock、床面
  `ContactRoot` 和仰卧旋转。导航阶段本身存在，但视觉结果是角色提前仰躺，再漂移到
  床上。
- 旧逻辑只尝试床尾一个接近点。床尾存在脚凳、床架延伸或墙体时，即使左右床沿可达，
  动作也会被拒绝；该策略不能覆盖靠墙床、单人床、双人床和不同高度床。

### 二、修复内容

- `BedTarget` 新增 `LeftEdge/RightEdge`。床尾继续使用全部合并床 source 的真实外缘；
  左右边缘只使用床中央身体区，避免宽床头板或一体式床头柜把侧边 Dock 推得过远。
- 新增 `BedMountCandidate`、`BedMountPlan` 和 `BedMountPlanner`。每张床生成左侧、
  右侧、床尾三条候选边，逐一验证：
  - 按实际床架外缘向外留出 0.48 m；
  - NavMesh 采样与局部地面高度；
  - 从角色当前位置出发的完整路径；
  - 站立及弯腰空间的 Triangle-AABB SAT 净空。
  最终选择路径最短的可用方案，而不是按床名称或固定世界坐标分支。
- `LieOnBedAction` 状态机改为
  `walking → docking → preparing → mounting → lying → resting → rising`。
  `walking/docking/preparing/mounting` 全程保持站立根姿态和 CharacterController；
  `preparing` 只增加弯腰权重，`mounting` 至少完整渲染一帧后，根节点才一次性从 Dock
  切换到床面，不再对躺姿位置或旋转做连续插值。
- `AvatarLyingRig` 新增 `PreparationWeight`。前倾角依据
  `bed.Center.y - bed.FloorY` 计算：低床弯腰更多，高床弯腰更少。主动起身先瞬时回到
  同一接近点的弯腰姿态，再解除弯腰并恢复站立胶囊，避免反向漂移。
- `SeatingDeviceProbe` 增加 `actionPhase/posture/lyingPreparation`，使设备验证可以
  区分导航、弯腰、瞬时上床和稳定躺卧。

### 三、测试与设备验证

- Unity TestRunner：`71/71` 通过，0 failed、0 skipped。新增用例覆盖低床、高床、
  窄床、宽床、仅侧边可达、远距离保持站姿、完整弯腰帧以及禁止分数躺卧权重。
- PICO Emulator 继续使用 86,323 个原生房间网格三角面，无床 fixture。原生床尺寸
  `1.910 × 2.095 m`，中央左右真实外缘 `1.116 / 1.103 m`。
- 设备从 `(-2.400, 0.411, 0.800)` 远距离启动：连续 5 个 walking 采样均为
  `posture=standing`、`lyingWeight=0`、胶囊启用；随后 2 个 preparing 采样和
  1 个 `PreparationWeight=1` 的 mounting 帧。
- mounting 到 resting 之间，根节点从
  `(-0.137, 0.419, 1.159)` 一次切换为
  `(-0.137, 0.855, 2.300)`；不存在 `0 < lyingWeight < 1` 的设备采样。
  录像确认角色先走到床边、面对床弯腰，下一帧才出现在床面。
- Android ARM64/IL2CPP Development APK 为 82,359,836 字节，APK v2 签名通过，
  SHA-256 `37461471ec6a0988ece12158f684b72d0145723a1ae166d486ce9c2c7285b90b`。
- 更新后的原生房间录像：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/matesoul_native_room_bed_complete_20261004.mp4`，
  29.937 秒、2160×2158，SHA-256
  `146419a9b199fe36082008930cce52872cf5da2c5d126213140eaa35dcb35e07`。

### 四、后续建议

- 当前弯腰加瞬移是无爬床动画时的明确降级方案。获得通用上床动画后，应只替换
  `preparing/mounting` 的表现，不修改 BedMountPlanner、capability 或协调器。
- 物理头显需继续验证软床垫实际接触高度、左右床沿靠墙时的候选淘汰、床边柜移动后的
  动态净空，以及 Scene Capture Box3D 的边缘精度。

## Phase Framework F18 工作汇报：移除弯腰并直接上床（2026-10-04）

### 一、需求调整

- 按最新产品决定，取消 F17 的程序化弯腰表现。角色只需走到离床最近的安全位置，
  保持站姿，然后直接切换到床面仰卧姿态。
- 泛化的左侧、右侧、床尾候选规划继续保留；本次不回退到模拟器床专用坐标。

### 二、实现

- `LieOnBedAction` 删除 `preparing` 阶段，最终状态机为
  `walking → docking → mounting → lying → resting → rising`。
- `mounting` 保留为一个完整站立帧屏障。下一 Tick 才一次性写入
  `ContactRoot/LyingRotation`，因此既无弯腰，也无仰躺漂移。
- `AvatarLyingRig` 删除 `PreparationWeight`、`SetPreparation` 和 hips/spine 前倾逻辑；
  Development Probe 同步删除 `lyingPreparation`。
- 床架外缘安全距离从 0.48 m 收紧到 0.36 m：角色胶囊半径为 0.30 m，额外保留
  0.06 m。站立净空盒同步移除面向床内的弯腰扫掠空间。
- 起身采用对称离散切换：从床面直接回到原 MountPlan 的站立 Dock，下一帧恢复胶囊和
  释放控制权。

### 三、验证

- Unity TestRunner：`71/71` 通过，0 failed、0 skipped，耗时 13.652 秒。
- PICO Emulator 原生房间仍识别 1 张床和 1 把椅子，输入为 86,323 个原生网格三角面。
- 实际选择床尾方案，Dock 从 `z=1.159` 收紧到 `z=1.279`；设备录像显示角色保持站姿
  走到床前，下一帧直接出现在床面，没有弯腰和中间漂移。
- 主动起身后根节点回到 `(-0.137, 0.447, 1.279)`，`lyingWeight=0`、
  CharacterController 恢复且 FullBody 租约释放。
- APK 为 82,360,293 字节，v2 签名通过，SHA-256
  `756b265ddfdcc92f55f1098b52c510a534d28311c5548cc7bfbef132e2bb8f84`。
- 最终录像更新为 26.062 秒、2160×2158，SHA-256
  `0d46ec1f07684ba511868d6a43744e11b72aceb554bee8d4c240402d42389376`。

## Phase Framework F19 工作汇报：电视按钮与视频播放框架（2026-10-04）

### 一、源码完成情况

- 新增 `TelevisionController`，统一拥有电视开关、VideoPlayer、空间 AudioSource 和
  每实例视频纹理。支持 VideoClip / URL、循环、等比留边、准备超时、错误状态、
  应用挂起暂停以及关机/删除时资源释放；没有视频时显示 `NO SIGNAL`。
- 新增 `TelevisionPowerButton`，复用现有 Unity/XR UI 点击通道，通过材质发光提示悬停，
  对独立 `TV_Power_Button` 网格执行 3 mm 下压与回弹；模型主体保持握持操作。
- 新增 URP/XR 屏幕 shader，使用原 `TV_CRT_ConvexGlass` 弧面与 UV。
- 新增 `TelevisionBuilder`，负责导入复制资产、创建材质、绑定按钮/视频组件、
  生成预制体与 ItemDefinition；`GameSceneBuilder` 已接入构建调用。
  已有电视预制体会被复用，以保留后续配置的视频源。
- 原电视 ArtSource 保持不变，FBX 与两张贴图已复制至
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/PropsSource/Television/`。
- 新增五个 `TelevisionTests` 集成用例，以及接入说明
  `/Users/bytedance/MateSoul/项目沉淀资料/Television.md`。

### 二、集成修复

- 初次看不到电视的直接原因是 `Television.prefab`、`Television.asset` 和场景目录引用
  尚未生成，`MateSoul.unity` 的 `items` 数组仍只有灯、桌子和椅子。
- 修正电视 FBX 的 Z-up 导入轴：先绕 X 轴直立，再统一正面为物品局部 -Z。
  最终校验尺寸为 `0.480 × 0.566 × 0.450 m`，按钮和 CRT 屏幕仍使用原独立网格。
- `MaterialPropertyBlock` 从 MonoBehaviour 字段初始化器移入 `Awake`，消除预制体导入时
  `CreateImpl is not allowed` 和后续 Selectable 空引用。
- 修复共享 `GameItem.Bind` 对 Unity Object 使用 `??` 的错误。改为显式 `== null`
  后再添加 Rigidbody / XRGrabInteractable，避免“伪 null”产生 MissingComponentException。
- 已生成 `Television.prefab`、`Television.asset`、六个 URP 材质，并将电视作为
  `catalogVisible` 的第 3 个可见目录物品写入 `MateSoul.unity`。

### 三、验证与交付

- 静态编译通过；电视专项测试 `5/5` 通过，覆盖鼠标/XR 射线、遮挡、按钮反馈、
  无信号、错误恢复、实例隔离、MP4 解码循环和关机释放。
- 最终状态下全量 Unity TestRunner：`76/76` 通过，0 failed、0 skipped。
  HUD 测试明确断言存在 `物品/television`。
- TRAE 沙箱直接启动 Unity 时仍会触发
  `mono_os_sem_post: semaphore_signal failed with error 15`；改由系统 Terminal
  启动同一 Unity 命令后，场景生成、测试和 Android 构建均成功。
- 新 APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  82,378,113 字节，APK v2 签名通过，SHA-256
  `f49e8841f6c691f140c5b1faf0f55e852e53a0124354a82d66dad33456b543ad`。
- 当前没有连接中的 PICO 模拟器，因此未自动安装；Unity Editor 已重新打开包含电视引用的
  `MateSoul` 游戏场景。正式视频资源的设备解码、音视频同步和长时间播放仍待资源到位后验证。

## Phase Framework F20 工作汇报：PICO 模拟器按需安装与自动启动（2026-10-04）

### 一、实现

- 扩展 `/Users/bytedance/MateSoul/scripts/start-pico-emulator.command`，把原先仅启动
  模拟器和配置网关反向端口的流程改为：
  `等待完整开机 → 解析 APK → 判断是否最新 → 按需安装 → force-stop → 冷启动`。
- 使用 Android `aapt dump badging` 动态读取 package 和 launchable activity，
  不在脚本中重复硬编码应用 ID。
- “最新版”以 APK 内容 SHA-256 为唯一判据。优先通过设备 `sha256sum` 计算已安装
  `base.apk`；设备不支持时回退 `adb pull` 后本地计算。由于当前 APK 的
  `versionCode=1` 长期不变，不能用版本号可靠判断。
- 已安装 SHA 与宿主一致时跳过 `adb install`；缺失或不一致时执行
  `adb install -r -g`，并在安装后再次校验 SHA。
- 模拟器已经运行时脚本只同步并启动应用；模拟器正在启动但 ADB 未 ready 时等待现有
  QEMU 进程，不重复启动同名 AVD。
- 保留 AI 网关幂等启动和 `adb reverse tcp:8787 tcp:8787`，每次启动应用前执行
  `am force-stop`，避免继续运行旧 IL2CPP 进程。

### 二、验证

- Bash 语法检查通过。
- 首次执行检测到设备旧 SHA
  `756b265ddfdcc92f55f1098b52c510a534d28311c5548cc7bfbef132e2bb8f84`，
  自动更新到
  `f49e8841f6c691f140c5b1faf0f55e852e53a0124354a82d66dad33456b543ad`；
  `adb install` 返回 `Success`。
- 设备 `base.apk` 与宿主 APK 的 SHA-256 完全一致。
- 第二、第三次执行均输出 `APK is already current`，未重复安装，并成功冷启动
  `com.UnityTechnologies.com.unity.template.urpblank/com.unity3d.player.UnityPlayerActivity`。
- `topResumedActivity` 和应用 PID 均确认 MateSoul 位于前台；网关反向端口为
  `tcp:8787 → tcp:8787`。

### 三、后续

- 双击脚本即可完成模拟器与应用启动，不再需要手工安装 APK。
- 可通过 `MATESOUL_APK_PATH` 指定其他 APK，通过 `MATESOUL_DEVICE_ID` 指定其他
  ADB 设备。物理设备仍需单独确认系统授权和安装策略。

## Phase Framework F21 工作汇报：手柄、眼手与近场物理点击（2026-10-04）

### 一、交互模式

- 新增 `PlayerInteractionModeController`，将远场输入明确分为互斥的
  `Controller` 和 `EyeHand`，避免手柄射线与视线射线同时争抢按钮高亮。
- 默认自动模式根据 `Controller | HeldInHand` 设备的实际追踪状态选择：
  发现追踪手柄立即使用手柄模式；手柄丢失 0.75 秒后切换眼手模式。
  同时提供 `SetMode` 和 `ResumeAutomaticSwitching`，供后续设置页手动选择。
- 手柄模式保留 Grip 抓取虚拟物品，将 UI 点击从 Trigger 改为 `primaryButton`：
  右手 A 键、左手 X 键。
- 眼手模式使用 PICO Unity 输入
  `<PXR_HMD>/eyeGazePosition|eyeGazeRotation|eyeGazeIsTracked` 驱动真实眼动射线，
  右手 `pinchTouched/pinchValue` 提交 UI 点击。Gaze 不绑定 XRI Select，因此捏合不会
  同时抓走被注视的电视。
- 场景构建器统一开启 PICO hand tracking 和 eye tracking；
  最终 APK 已包含并授予 `HAND_TRACKING`、`EYE_TRACKING`、`SPATIAL_DATA` 权限。

### 二、虚拟手物理点击接口

- 新增 `PhysicalPressInteractor`。后续虚拟手只需把它、Collider 和运动学 Rigidbody
  挂在指尖，即可参与世界控件按压。
- 新增 `TelevisionPhysicalPressSurface`，电视预制体自带 32×32×16 mm 触发区，
  指尖沿按钮内法线压入 6 mm 达到激活阈值。
- `TelevisionPowerButton` 统一合并射线 Pointer 和物理压入量，仍由同一个
  `onClick → TelevisionController.TogglePower` 执行开关。
- 每个指尖一次压入只触发一次；降到 20% 或完全离开后重新武装。指尖在触发区内被
  禁用/销毁时会清理接触并释放按钮，避免卡住。
- `GameItem` 显式只把物品根节点非 Trigger Collider 交给 XRGrabInteractable，
  物理按钮触发区不会被误当成整机抓取面。

### 三、验证与交付

- 电视专项测试 `6/6` 通过；新增用例使用真实 Rigidbody/Collider 虚拟指尖验证
  压入、保持不重复触发、释放、二次按压及中途禁用清理。
- 全量 Unity TestRunner `77/77` 通过。射线测试断言 A/X primaryButton、PICO eye gaze、
  右手 pinch UI Press、模式互斥和 gaze 不绑定抓取 Select。
- 最终 APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  82,379,655 字节，APK v2 签名通过，SHA-256
  `feca0a0ce5ffcdb3ce9d7868b44491d84e6ed6f82c1798ce35cdde6e32099a83`。
- 自动安装脚本已更新 PICO Emulator；设备 APK 哈希一致，MateSoul 位于前台，
  运行日志无 Fatal、NullReferenceException 或 MissingComponentException。
- 自动测试证明绑定和状态不变量；A 键、真实眼动捏合及未来虚拟手模型的主观操作手感
  仍需在物理头显/最终手模型上验收。

## Phase Lavender Chibi Bowling 工作汇报：保龄球与球瓶建模（2026-10-05）

### 一、工作完成情况

- 新建参数化公共脚本
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/BowlingCommon/bowling_common.py`，
  统一材质、闭合曲面贴花、摄影棚、渲染、导出和回读验证能力。
- 从现有碗角色素材重建 1024×1024 RGBA 圆形贴花，并分别写入保龄球和球瓶的
  `textures/bowling_character_decal.png`；两份文件 SHA-256 一致。
- 完成直径 218.3 mm 的保龄球。模型包含三个通过 Exact Boolean 实际切出的指孔、
  独立圆润孔沿、低反射内腔及贴合球面的角色贴花。
- 完成 121×121×380 mm 的保龄球瓶。瓶体使用 Hermite 插值轮廓旋转成型，
  包含圆润顶部、收颈、宽腹、两条白色颈环和贴合曲面的角色贴花。
- 两套资产均交付 Blender 源文件、内嵌贴图 GLB、FBX、四张 1024×1024 多视图渲染、
  参数化重建脚本、README 和 JSON 验证报告。

### 二、验证结果

- 保龄球：8 个闭合网格、9,714 顶点、19,408 三角面，边界边和非流形边均为 0；
  实测尺寸 218.3001×218.3797×218.3 mm。
- 球瓶：2 个闭合网格、10,660 顶点、21,312 三角面，边界边和非流形边均为 0；
  实测尺寸 121.0×121.08×380.0 mm。
- GLB 和 FBX 均完成重新导入，尺寸、网格数和材质数与源文件一致。
- 两个 GLB 均为 glTF 2.0，角色 PNG 内嵌，无外部图片 URI。
- 保龄球 GLB SHA-256：
  `946d5d0e78a6081f9028a45cc0d359ac9b97ba3b9dcab73a6bd7f63e926d4647`；
  球瓶 GLB SHA-256：
  `8e43eeac874b953e32a71f6634961e375b6ef734b5726ebd5717495c531b8768`。

### 三、问题与处理

- 系统 Python 不包含 NumPy，贴图脚本改由 Blender 内置 Python 执行，避免增加项目环境依赖。
- 首轮保龄球受高线性底色与釉面高光影响接近白色，最终降低底色明度并收敛镜面参数，
  保持与现有浅紫道具一致且在白色摄影棚中清晰可读。
- 首轮指孔内腔受反射和遮挡影响发白，调整内腔位置、覆盖半径和粗糙度后恢复稳定深紫。
- 球瓶顶部首轮轮廓偏尖，通过增加顶部控制点形成连续圆顶。
- 纯色网格不需要切线，GLB 导出关闭 `export_tangents`，消除无 UV 网格警告。

### 四、后续建议

- Unity 运行时为保龄球使用 Sphere Collider，不用三指孔视觉几何参与物理碰撞。
- 球瓶使用简化 Convex Mesh Collider 或 Capsule 组合碰撞体，避免将 21.3k 三角面的
  展示网格直接作为动态碰撞体。
- 当前已完成离线资产级验收；正式接入玩法后仍需在 Unity/PICO 中验证抓取手感、
  刚体质量、摩擦、重心和多球瓶碰撞稳定性。

## Phase Lavender Chibi Bowling V1.1 工作汇报：球瓶顶部收窄（2026-10-05）

### 一、调整内容

- 按视觉反馈调整
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/BowlingPin/scripts/build_bowling_pin_asset.py`
  的 302–378 mm 高度段轮廓控制点。
- 球瓶顶部最大直径由约 82 mm 收窄至 76 mm，缩减约 7.3%；保持圆顶连续，
  未改变颈环、腹部、贴花、121 mm 最大外径和 380 mm 总高度。
- README 和 JSON 报告新增 76 mm 顶部直径规格与自动断言。

### 二、重新生成与验证

- 已重新生成 Blender、GLB、FBX 和 hero/front/side/back 四张 1024×1024 渲染图。
- GLB/FBX 回读尺寸仍为 121.0×121.08×380.0 mm。
- 2 个网格、21,312 三角面，边界边与非流形边均为 0。
- 尺寸、闭合流形、双白环、76 mm 顶部直径和曲面贴花断言全部为 `true`。
- V1.1 GLB SHA-256：
  `f6b4e38aec5c02bef0ab20a9fc9593be29a315593e6b8d03945b2617ab7952e1`。
- V1.1 FBX SHA-256：
  `94d269a2d71dd4aaaba4c7ceafad434edd427ca41faa72d53b5c77cc2341a82f`。

### 三、交付说明

- 原路径中的球瓶源文件和导出已原位替换为 V1.1，不保留旧比例副本。
- 保龄球资产未发生变化。

## Phase Physics Foundation 工作汇报：公共物理基座（2026-10-05，已完成）

- 已实现 `PhysicsBody` / `PhysicsGrabInteractable`，接入角色和当前四种运行时物品。
  桌椅使用简单组合碰撞体；物品使用单一经验材质，释放保留求解后的速度和旋转。
- 已接入角色动态刚体、坐卧碰撞代理、受阻退出、全体刚体暂停、批量恢复事务、
  稳定后合并保存，以及删除支撑后中断下落的恢复。
- 初始编译的 Unity CLI 遇到本机既有 Mono `semaphore_signal error 15`；
  已通过 Unity Hub 启动编辑器，使用 FrameworkTestMenu 内置测试入口完成实际编译与回归。
- 修复角色/物品装配时 Transform 与 Rigidbody 首帧不同步、床铺转移时水平胶囊扫入床沿、
  暂停取消恢复后不重试、旧家具与初始角色出生点冲突，以及主地面放置多出 NavMesh
  代理高度的问题。
- 恢复期间由动作协调器统一拒绝 UI、AI 和自治动作；恢复成功或确认失败后才解除角色冻结。
- 基于真实执行的 Unity 全量结果为 **95/95 通过，失败 0、跳过 0**。测试覆盖物理行为、
  抓取、暂停、恢复事务、角色移动与坐卧、HUD、电视和原有领域逻辑。
- 接入与性能取舍说明：
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_公共物理基座.md`。
- 本轮按用户要求不做压测，也未进行 PICO Space Pro 真机物理性能与手感验收。
- 已生成 `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`（82,433,205 bytes），
  ZIP 完整性、v2 签名和 `arm64-v8a` ABI 校验通过。
- 最终测试 XML 与哈希、边界用例清单见
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_公共物理基座验证报告.md`。
- 已合并重复的测试请求消费者，`FrameworkTestMenu` 独占请求轮询并支持分号分隔测试名；
  可借现有 `.dbg/yummy-cookie-v001/refresh` 文件入口刷新已启动的编辑器，不依赖桌面解锁。
- Unity Test Framework 1.1.33 在 `UnitySetUp` 内进入 Play Mode 时会错误复用上一协程 PC，
  造成测试主体被跳过；已将相关测试改为在每个测试外层进入 Play Mode，并用只返回
  `null` 的等待辅助器驱动物理帧，最终结果不包含伪通过。

## Phase PICO Emulator Startup Fix 工作汇报：角色与 UI 失效修复（2026-10-05）

### 一、问题与修复

- 设备日志确认 `GameBootstrap.InitializeAsync` 因
  `PICO XR mesh subsystem is unavailable` 中断；角色此前已隐藏，因此无法重新激活。
- 根因是 `/Users/bytedance/MateSoul/MateSoul/Assets/Resources/PXR_ProjectSetting.asset`
  的 `spatialMesh` 为关闭状态。现已启用，并由 `GameSceneBuilder` 在每次场景/Android
  构建时强制保持开启。
- 控制器 UI Press 从 A/X `primaryButton` 改为标准扳机
  `triggerPressed`/`trigger`。
- 场景新增左右手 `PicoHandInteraction` 射线；自动模式只在检测到手或眼动设备时切换，
  无可用追踪时保留控制器射线，避免所有射线同时关闭。
- 已重新生成并同步 `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Scenes/MateSoul.unity`。

### 二、验证与交付

- 定向 Unity 测试
  `GameRuntimeTests.CatalogPlacementGrabAndFocusUseRegisteredDefinitions` 为 **1/1 通过**；
  覆盖 5 条射线、扳机绑定、手势绑定和模式互斥。
- Android IL2CPP 构建通过，日志包含 `MATESOUL_BUILD_PASS`；正式 APK 为
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，SHA-256
  `030ded271ad17073117cca53f948599aa2dd372bcb0badff04df3cd9ded950c4`。
- 设备 APK 哈希与宿主一致。运行日志包含
  `Spatial mesh snapshot blocks=48` 与
  `MATESOUL_READY source=PICO Emulator Spatial Mesh`，原启动异常消失。
- 模拟器右手柄连接成功且射线可见；运行时状态与截图确认角色已激活并正常渲染。

### 三、后续建议

- PICO Emulator 的宿主鼠标/控制器扳机需人工完成一次主观点击验收；ADB Android
  KeyEvent 不等价于 PICO 原生控制器 Trigger。
- 当前空间网格 NavMesh 会包含家具顶面，角色自治可能走出初始视野；应另立任务将
  Floor 语义用于导航烘焙、完整 Mesh 仅用于碰撞与现实物体识别。

## Phase Mini Game 01 工作汇报：双人保龄球（2026-10-05）

### 一、功能开发

- 在互动栏新增“小游戏”入口和独立二级页面，首个注册玩法为保龄球；启动后自动关闭
  HUD 页面，支持结束与重新开始。
- 新增 `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/MiniGame.cs`
  与 `MiniGameHost.cs`，小游戏通过目录注册，不把玩法分支写入公共协调器。
- 新增解耦的 `SystemNotifications` 与 `SystemNotificationPresenter`。游戏逻辑只发布
  消息，表现层统一在左上角显示浅紫圆角、白色粗体提示，在视野中心显示计分/胜负大窗。
- 完成球瓶阵列、球点放置、玩家回合、冷月走近与投球、5 秒延迟计分、比分/胜负展示、
  4 秒欢呼声和唯一球实例复位。倒瓶标准为瓶身上轴偏离世界竖直至少 45°。
- 保龄球为 6.5 kg 球形碰撞体，球瓶为 1.55 kg 三段复合碰撞体；尺寸继续使用现有
  218.3 mm 保龄球与 121×121×380 mm 球瓶 FBX。
- `BowlingPinContacts` 为每个动态球瓶维护同步的 Kinematic 碰撞代理：动态球瓶忽略
  手、角色和其他道具，这些外物只与代理碰撞，因此仍会被阻挡或反弹但不能推动球瓶；
  有效出手后的球及球瓶连锁碰撞保持正常。
- 冷月回合通过 `AvatarActionCoordinator` 的 System 动作占用全部控制通道，
  对话、UI、AI 和自主行为不能建立平行动作；结束/暂停统一取消并清理。

### 二、三种交互与资源

- 手柄：扳机确认 UI/地面位置，Grip 抓取与松开保龄球。
- 眼手：眼动射线选中，食指与拇指捏合确认；抓球后使用手部 Pinch Pose 跟随。
- 手势：手部射线选中，食指与拇指捏合确认、抓取和释放。
- 欢呼声来自 Gregor Quendel 的 `Strong cheering - II - Short`，采用可商用的
  CC BY 4.0，已裁成 4 秒并保留许可、来源和 SHA-256：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Audio/ThirdParty/`。

### 三、验证与当前边界

- `scripts/check-game-framework.py` 已扩展为同时编译 Runtime、Editor 和 Tests；
  当前静态编译通过，仅有既有未使用字段警告。
- 新增 `BowlingTests.cs`，覆盖标准 1-2-3-4 三角阵列、45° 倒瓶阈值、Prefab 物理配置、
  系统提示生命周期，以及“其他物体不推动球瓶、保龄球可击倒”的 PhysX 回归。
- LaunchServices 隔离 Unity 已完成场景和三套保龄球 Prefab 的真实构建。首次碰撞回归
  证明 ContactModify 质量缩放不满足边界（球瓶位移约 0.62 m），现已替换为双碰撞体
  隔离方案；保龄球专项 `4/4` 通过。
- 全量 Unity TestRunner 为 `98 passed / 0 failed / 1 skipped`；唯一跳过项是既有电视
  可选诊断视频。Android ARM64/IL2CPP 构建通过，APK 为
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，文件大小
  `82,440,847` 字节，ZIP 完整、仅含 `arm64-v8a`，APK v2 签名有效，SHA-256
  `39ff51ba57a16e3e5d67a22a8a934b66c4d9ac8e96b51160b8375724afe19bd8`。
- 当前无 ADB 设备在线，因此未执行 PICO 安装、实机输入和主观物理手感验收。

## Phase Mini Game 01.1 工作汇报：小游戏房间门禁修复（2026-10-06）

- 修复 `MiniGameHost.TryStart()` 错把 `GameSession.ItemsReady`（历史虚拟物品恢复完成）
  当作房间网格就绪的问题。
- 新增 `GameSession.IsRestoringItems`。仅在历史物品仍异步恢复时短暂阻止小游戏，并提示
  “房间网格已就绪，正在恢复历史虚拟物品”；旧锚点恢复失败时直接允许小游戏继续使用
  已构建的模拟器网格和 NavMesh。
- 静态 Runtime/Editor/Tests 编译通过；Unity 定向回归 `2/2` 通过，覆盖恢复中阻止及
  恢复失败后放行，避免再次显示“请先完成房间定位”的错误归因。
- 已重新完成 Android ARM64/IL2CPP 构建并安装到 `emulator-5554`。宿主与设备 APK
  SHA-256 均为
  `d31c969a85860d41e8bb10cdc5e8d2099a77167711dbdf4fc98b350a781fc698`；
  冷启动日志包含 `MATESOUL_READY source=PICO Emulator Spatial Mesh`。当前旧物品因
  与模拟房间相交而恢复失败，但新门禁会继续允许小游戏使用有效网格。

## Phase Mini Game 01.2 工作汇报：Android 放置标记 Shader 修复（2026-10-06）

- 修复保龄球启动时 `Shader.Find("Universal Render Pipeline/Unlit")` 在 Android
  Shader 裁剪后返回空，继而触发 `Value cannot be null. Parameter name: shader`。
- 放置标记改为克隆球瓶 Prefab 已引用的不透明材质，再覆盖颜色；不再依赖运行时按名称
  搜索 Shader，材质依赖会随 Prefab 稳定进入包体。
- 保龄球专项回归 `4/4` 通过。主工程 Android ARM64/IL2CPP 增量构建通过并重新安装到
  `emulator-5554`；宿主与设备 SHA-256 均为
  `d2a44beb5ac67e08ec152f8dce343b81cb394dcf421e328e95d3eae05858e289`。
- 冷启动日志包含 `MATESOUL_READY source=PICO Emulator Spatial Mesh`，未再出现 Shader
  空引用；最终点击与放置流程由当前模拟器继续人工验收。

## Phase Mini Game 01.3 工作汇报：保龄球抓取吸附（2026-10-06）

- 修复控制器 Grip 选中后保龄球缺少即时视觉反馈的问题。球专属
  `PhysicsGrabInteractable.snapToInteractor=true`，选中时在目标位置无碰撞占用的前提下
  立即移动到交互器附着点，再由 `PhysicsBody` 连续跟随。
- 手柄模式吸附到控制器；眼手模式通过 `HandItemRay.GetAttachTransform()` 吸附到真实
  Pinch Pose。家具等普通物品继续保持 `useForceGrab=false`，不改变既有远距抓取语义。
- Prefab 构建器显式标记抓取组件 Dirty，生成资产已确认包含
  `snapToInteractor: 1`。保龄球专项回归 `4/4` 通过。
- 修复版 APK 已安装并冷启动于 `emulator-5554`，宿主与设备 SHA-256 均为
  `b43c3e96429f22fb7319ae08ee6221b74850146997a7e8aa5d4372cbff3ad2a3`；
  设备日志为 `MATESOUL_READY ... itemState=ready`，未出现 Shader 或启动异常。
- `/Users/bytedance/MateSoul/scripts/run-bowling-validation.command` 提供非沙箱隔离验证入口；
  后续需在可交互桌面执行，并在通过后重建场景与 Android APK。

## Phase Art Playing Cards 工作汇报：54 张扑克牌与配套纸盒（2026-10-06）

- 新资产位于 `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/PlayingCards/`。
  从用户本次提供的 `扑克牌new.png`、`扑克牌背面.png`、`扑克牌盒.png` 提取图案，
  完成 52 张普通牌和红/黑 Joker，所有牌统一为 63 × 88 × 0.30 mm、圆角半径 3 mm。
- 人物图案保留原比例，通过延展背景和角标重排统一尺寸；Joker 改为竖版；
  下角标按本张牌上角标旋转生成，修正原图部分 K 下角被画为 J 的错误。
- 牌盒外尺寸 65 × 91 × 18.5 mm，壁厚 0.35 mm、内腔 64.3 × 90.3 × 17.8 mm。
  包含空心盒体、可独立旋转的顶盖/插舌/两侧耳和真实拇指缺口；54 张牌叠厚
  16.2 mm，计入插舌及侧耳后的最小装配间隙约 0.45 mm。
- 交付 Blender 源文件、整副牌/闭合盒/打开盒/装盒组合四组 GLB 和 FBX、54 张
  独立牌面 PNG、共用牌背、两张图集、预览图及 JSON 验证报告。源文件重新打开
  验证通过，包含七个场景、54 张牌及两张打包贴图。
- 源网格 59/59 闭合流形；54 张牌合计 15,336 三角面，牌盒 964 三角面。
  GLB/FBX 共 8/8 回读通过，检查了单张与整体尺寸、唯一节点、UV、材质、
  可读取贴图和焊接导出接缝后的闭合拓扑。
- 参考牌盒实际为一张展示三个面的透视图，三个可见面分别校正提取；未展示盒背
  复用牌背纹样。牌面原图分辨率有限，扩大 PNG/图集不会增加原图细节。
- 本阶段仅交付美术资产；没有修改 Unity 场景、物品注册、游戏逻辑或设备 APK，
  后续若接入运行时，需通过现有公共物理基座配置牌片和空心盒碰撞体。

## Phase Art Dartboard 工作汇报：飞镖盘三视图建模（2026-10-06）

- 根据用户提供的正视、侧视、后视参考图完成粉紫卡通飞镖盘建模，整体尺寸严格为
  `400 x 48 x 400 mm`，坐标约定为 X 宽、Y 厚、Z 高，正面朝 `-Y`。
- 正面包含标准 20 区编号顺序、独立单倍/双倍/三倍环、爱心靶心、蝴蝶结、翅膀、
  猫爪和表情；侧面包含心形与星形装饰；背面包含圆形检修盖、挂孔和三颗紧固件。
- 计分扇区采用真实封闭挤出几何，不是平面贴图；80 个扇区合并为一个保留三种材质的
  网格，20 个数字合并为一个网格，将总节点控制为 56。
- Blender 源文件、GLB、FBX、四张独立渲染、三视图拼图、原参考图、可重建脚本、
  JSON 验证报告和 SHA-256 清单均已交付至
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Dartboard/`。
- 源文件及 GLB/FBX 回读尺寸均通过；共 41,190 顶点、81,772 三角面、10 个 PBR
  材质，源文件与两个导出格式的边界边和非流形边均为 0。
- 完整压缩包为
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/LavenderChibiDartboard_v001.zip`，
  ZIP 完整性和内部文件哈希校验通过。
- 本阶段仅完成独立美术资产，未创建 Unity Prefab、飞镖碰撞计分逻辑或更新 APK。
  后续接入时主壳使用简化圆柱碰撞体，装饰件默认不参与物理。

## Phase Virtual Item Catalog 工作汇报：全量虚拟物品接入（2026-10-06）

### 一、功能开发

- 虚拟物品目录由原有桌子、椅子、复古电视扩展为 19 项：
  陪伴灯、桌子、椅子、复古电视、盘子、马克杯、碗、扑克牌、勺子、筷子、
  餐刀、纸杯蛋糕、曲奇、苹果、飞镖盘、保龄球、保龄球瓶、冷月玩偶、遥控器。
- 将 `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/` 下的正式 FBX 与必要贴图
  非破坏性复制到
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/PropsSource/`，
  原始美术交付保持不变。
- `GameSceneBuilder.BuildCatalog()` 成为场景目录的统一装配入口；新增成品统一生成
  `ItemDefinition`、Prefab、简化碰撞体、`PhysicsBody` 和表面放置配置。
- 仓库没有可用刀具模型，已有苹果资产说明也明确不含刀具；本轮补充了轻量餐刀
  Prefab，使用 URP Lit 金属刀身和紫色手柄，后续可直接替换视觉模型而不改目录 ID。
- FBX 内嵌材质在构建时统一转换为项目内 URP Lit 材质，避免 Android Shader 裁剪后
  出现粉色材质。
- 虚拟物品页改为每页 6 项的两行三列目录，支持循环翻页和页码/总数显示；
  已放置物品的切换、访问和删除操作保持不变。

### 二、验证与交付

- Unity 场景构建成功，`MateSoul.unity` 的 `GameBootstrap.items` 已序列化 19 个定义。
- 19 个 Prefab 均包含非空模型引用、简化 Collider、Rigidbody 和共享 `PhysicsBody`；
  54 个转换材质均引用 URP Lit。
- `GameRuntimeTests` 全量定向回归为 **24/24 通过**，覆盖目录完整性、四页遍历、
  放置、抓取、选中、删除、存档恢复及既有角色行为。
- Android ARM64/IL2CPP Development 构建通过。APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，SHA-256
  `0d030d02490bb081064520ffa45c2e505501f1d72eb953a1f0bb88c25d4dab62`。
- 当前无 ADB 设备在线，因此未执行本次 APK 的模拟器安装和头显内目录点击验收。

### 三、后续边界

- 苹果半块/四分之一块是切割玩法内部状态，不在普通物品目录重复展示。目录中的保龄球
  和球瓶使用独立普通物品 Prefab，不复用也不干扰 `BowlingGame` 的受控实例。
- 飞镖盘当前按落地摆件处理，尚未实现墙面吸附；扑克牌以整副装盒资产放置，尚未提供
  单张抽取玩法。
- 飞镖盘、玩偶和扑克牌源模型面数较高。当前按“用户主动选择后才实例化”控制开销；
  若后续允许批量摆放，应先补 LOD/网格合并和设备侧性能预算验证。

## Phase Television Program 01 工作汇报：冷月哭泣节目接入（2026-10-06）

### 一、功能开发

- 按用户最终指定素材接入
  `/Users/bytedance/Desktop/冷月哭泣节目/冷月哭泣节目.mp4`；此前提供的视频未复制、
  未引用。正式 Unity 资源为
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Video/LengyueCryingProgram.mp4`。
- 视频规格为 H.264/AAC、1920×1080、30 fps、19.6 秒，SHA-256
  `840f571f0b807a1b35b70bde34f12378a149f4fa883ccd5a1040ba6fd4cd2a12`。
- `TelevisionController` 新增正式节目数组、当前节目索引和 `SelectProgram(int)`；
  第 0 项作为开机默认节目。临时 Clip/URL 换源接口继续保留。
- 所有节目强制循环播放。VideoPlayer 使用 `FitOutside`，把宽屏节目等比放大后居中裁切，
  填满 358×258 mm 电视玻璃，不留黑边，也不进行非等比拉伸。
- `TelevisionBuilder` 在创建和升级 Prefab 时统一写入节目数组、循环和裁切配置；
  `Television.prefab` 已序列化节目索引 0、`m_Looping=1` 和 `FitOutside`。

### 二、验证与交付

- 带图形设备的 Unity 电视专项回归 **7/7 通过**。正式视频完成真实解码，屏幕像素
  非黑，播放至 19.6 秒后触发循环回调并继续保持 Playing。
- 电视截图人工检查确认画面覆盖完整 CRT 区域、无黑边，冷月人物比例正常，裁切保留
  视觉中心。
- Android ARM64/IL2CPP Development 构建通过。APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，大小 `120,783,553` 字节，
  SHA-256 `571a42acca9048edcc92f1e9d3a01873cd0099fd53dc95ce7196bfa3c045e742`。
- 当前无 ADB 设备在线，未执行本次 APK 的模拟器安装与头显内空间音频验收。

## Phase UI Overlay 工作汇报：HUD 与系统窗口始终置顶（2026-10-06）

### 一、功能开发

- 新增 `AlwaysOnTopUi`，统一配置主 HUD、展开窗口、字幕、系统提示和小游戏结果窗口：
  Canvas 使用最高安全排序值 `32760`，XR Raycaster 关闭 2D/3D 遮挡检测。
- 新增 `MateSoul/UI Always On Top` UI Shader，使用 `ZTest Always`、透明混合、
  UI Stencil、RectMask、Sprite Atlas 和动态字体兼容路径，使 UI 不再被角色、家具或
  空间网格写入的深度遮挡。
- 新增 `Resources/MateSoulUI/AlwaysOnTop.mat` 显式引用 Shader，避免 Android Player
  构建时因仅使用 `Shader.Find` 而被裁剪。
- `GameHud` 与 `SystemNotificationPresenter` 统一复用该配置；运行时动态创建的
  `Image`、`Text` 也立即绑定顶层材质。电视机本体上的实体屏幕与按钮仍保留真实遮挡。
- 修复 XR 启动阶段 Main Camera 替换时 `GameHud.Recenter` 和系统提示跟随逻辑的
  空引用风险，单帧只使用一次缓存后的活动相机。

### 二、验证与交付

- `GameRuntimeTests` 全量回归 **24/24 通过**；专项断言覆盖 Canvas 排序值、所有
  Graphic 的顶层 Shader 以及 `TrackedDeviceGraphicRaycaster` 遮挡开关。
- Android ARM64/IL2CPP 构建成功，构建日志包含
  `Build Finished, Result: Success` 和 `MATESOUL_BUILD_PASS`。
- APK 路径：
  `/Users/bytedance/MateSoul/.artifacts/ui-overlay-debug/Builds/MateSoul_Framework.apk`，
  大小 `118,893,297` 字节，SHA-256
  `3ee0f329b1b8c50296d62b2ea4a6797dd337395c4a04e534b1b75d6116cd93af`。
- APK 已覆盖安装到 `emulator-5554`；从设备回拉的 `base.apk` 与宿主构建哈希一致。
  冷启动状态为 `COLD`，应用保持前台运行，截图确认主 HUD 和系统提示完整显示在
  前景床体/家具画面之上，角色进入近景后 HUD 仍持续可见。
- 设备日志未再出现顶层 Shader 缺失或 `GameHud.Recenter` 空引用。

### 三、后续边界

- PICO Emulator 的 QEMU 窗口没有可供 Computer Use 定位的 bundle ID，ADB
  `input tap` 也不会等价触发 XR 射线点击，因此展开窗口的设备侧自动点击未完成；
  其材质与射线配置已由同一路径的运行时测试覆盖。
- 冷启动日志仍可见既有 `AvatarMotor.FixedUpdate` 向非 HTTPS 调试端点发送请求产生的
  `Insecure connection not allowed`，与本阶段 UI 修改无关，后续应单独清理该诊断上报。

## Phase UI Icon Sharpness 工作汇报：导航图标清晰度修复（2026-10-06）

- 确认互动、信息和虚拟物品三张 192×192 UI 图标错误启用了 Mipmap 与普通纹理压缩。
  World Space Canvas 缩放后会采样低分辨率 mip，再叠加 Android 压缩，导致轮廓明显发软。
- 将三张图标的 Default、Standalone 和 Android 导入设置统一改为无 Mipmap、无压缩，
  并启用 `alphaIsTransparency`，消除透明边缘的暗色插值。对话和设置图标由运行时
  直接生成，本来就没有 Mipmap，因此未修改其造型。
- 顶层 UI Shader、Canvas 排序和 XR 遮挡配置保持不变，清晰度修复不会重新引入角色或
  家具遮挡 HUD 的问题。
- HUD 专项回归 `1/1` 通过，日志确认三张图标已重新导入；Android ARM64/IL2CPP
  增量构建成功。
- 修复版 APK 大小 `118,961,138` 字节，SHA-256
  `5c9d81f18300591ee48ff03aef437c818db7d1ca22b7f97ed37dfdc9675474c1`。
  已安装至 `emulator-5554`，设备回拉 APK 哈希与宿主一致。
- 冷启动截图确认三张资源图标边缘较旧版更清晰，近景椅背经过 HUD 区域时 UI 仍保持
  顶层；目标 UI Shader/相机异常计数为 0。

## Phase Grip Grab Regression 工作汇报：虚拟物品侧键抓取修复（2026-10-06）

### 一、根因与修复

- 对比公共物理基座接入前后的控制器配置，确认数字 Grip 绑定从精确
  `<XRController>{Hand}/gripPressed` 退化为 `{GripButton}` usage。
  PICO Input System 布局只把 `GripButton` 声明为 `gripPressed` 的 alias，usage
  选择器无法替代真实控件路径。
- `GameSceneBuilder.CreateControllerRays()` 恢复 `/gripPressed`，保留模拟量
  `/grip + Press(pressPoint=0.1)`；右手 `O`、左手 `E` 作为 PICO Emulator
  键盘回退。真机与模拟器继续共用同一个 XRI Select 和 `PhysicsBody` 跟随路径。
- 设备此前误装了 `.artifacts/item-focus-debug` 诊断 APK。该包每帧遍历未激活
  `HandItemRay`，持续触发空引用并将帧率拖至 1–3 FPS。已删除焦点控制器和交互模式
  中的遗留网络诊断遍历，并为 `HandItemRay` 增加未绑定 Controller 状态保护。
- 正式 APK 改为 `BuildOptions.None`，避免开放中的 `DEVELOPMENT_BUILD` HTTP
  诊断进入交付包；显式 `BuildAndroidDevelopment()` 继续服务诊断构建。

### 二、验证与交付

- 新增 `GripGrabTests` 两项端到端回归，分别覆盖模拟量 `grip` 和纯数字
  `gripPressed`。两项均通过真实 XRI Ray 命中、Select、动态刚体跟随、移动、释放和
  重力恢复，结果为 **2/2 通过**。
- 当前源码的 `GameRuntimeTests` 为 **24/24 通过**，覆盖目录物品抓取、暂停/删除
  取消选择、控制器跟踪丢失释放，以及精确 `/gripPressed` 绑定断言。
- Release Android ARM64/IL2CPP APK 已通过 Manifest 校验：无
  `application-debuggable`，`pvr.app.type=vr`、`use.pxr.sdk=1`、
  `enable_anchor=1`、`mr_map_mgr_auto_start=1`、`enable_spatial_anchor=1`。
- APK 已写入 `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，大小
  `108,328,452` 字节，SHA-256
  `7a8759771307c8abc770656b03268ff3b4c3e221fcd4516d6ff4d31701403ed2`。
- 已覆盖安装到 `emulator-5554`，设备 `base.apk` 哈希与宿主一致；冷启动成功并输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=0 itemState=ready navMs=130`。
  Unity 日志未再出现 `NullReferenceException` 或 `Insecure connection not allowed`。

### 三、剩余验收

- 自动化已覆盖输入到物理释放的完整链路，但 PICO Emulator 当前没有已放置物品，
  且外部工具不能可靠模拟“射线对准物品后按侧键”的空间操作。需用户在已启动的
  模拟器中放置任一物品、用右手射线指向并按 `O`/Grip，确认物品可抓起和释放。
- `debug-grip-grab-regression` 保持 `[OPEN]`；用户确认修复后再删除本会话插桩、
  Debug Server、环境文件和调试记录。

## Phase Television Program Replacement 工作汇报：像素太空游戏节目替换（2026-10-06）

### 一、功能开发

- 将用户提供的
  `/Users/bytedance/Downloads/Q版角色太空横版像素游戏视频生成.mp4`
  复制为正式 Unity 资源
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Video/PixelSpaceGameProgram.mp4`。
  两者 SHA-256 均为
  `c55bdafdcc691236eccb0c3639b2eb044d487eb6d19f6e7538c5e381879f0079`。
- 删除运行资产中的旧节目文件名，但保留原 VideoClip GUID
  `d46263424b4b94293a866c1f1af847bb`，因此电视 Prefab 和既有序列化引用没有断链。
- `TelevisionBuilder.FirstProgramPath` 已切换到新资源；`TelevisionTests` 的媒体断言
  更新为 1280×720、24 fps、15.072 秒。电视继续使用 `FitOutside` 居中裁切和循环播放。
- 新视频规格为 H.264 High / yuv420p、AAC LC 双声道 44.1 kHz，文件大小
  `3,427,040` 字节。

### 二、验证与交付

- Unity 场景重建成功，`Television.prefab` 的节目数组和当前 VideoClip 均保持指向
  GUID `d46263424b4b94293a866c1f1af847bb`。
- 使用同一份新 MP4 执行真实解码专项，`TelevisionTests` 为 **7/7 通过**：
  播放器进入 `Playing`、RenderTexture 非黑、完整节目结束后触发循环、关机释放资源。
- 独立全量 Unity TestRunner 为 **109 通过 / 0 失败 / 1 跳过**；跳过项仅是未提供
  临时诊断副本时的可选视频用例，随后专项已用正式节目补跑通过。
- Release Android ARM64/IL2CPP APK 为
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，大小
  `92,856,132` 字节，SHA-256
  `731a6caa1b8abfb03096548d815069b4e4c0493b02bc9b633488e8934e666ed1`；
  APK Signature Scheme v2 校验通过。
- APK 已覆盖安装到 `emulator-5554`，设备 `base.apk` 与宿主哈希完全一致。应用冷启动
  后保持前台，眼动、手势和空间数据权限均为 granted，并输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=4 itemState=ready navMs=82`。

### 三、交付边界

- 电视默认关机，因此冷启动不会主动创建设备端视频解码器；启动日志无
  `TELEVISION_VIDEO_ERROR`、VideoPlayer 或 MediaCodec 异常。正式节目的解码、画面、
  循环和释放已由带图形设备的 Unity 测试覆盖。
- 结构化证据保存在
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/television_program_replacement_20261006.json`。
  临时诊断视频、截图、测试 XML 和构建日志在验证完成后删除，不进入交付资产。

## Phase Hand Hold IK 工作汇报：双手牵引与人体工学约束（2026-10-06）

### 一、功能开发

- 引入 `com.unity.animation.rigging` `1.3.1`，在
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Lengyue_Runtime.prefab`
  中为左右手生成独立 `TwoBoneIKConstraint`、Target、Elbow Hint 和掌心
  `HandHoldInteractable`。
- 新增 `HandIKRig`、`HandHoldAction`、`HandHoldInteractable` 和
  `HandHoldMath`。玩家选择手掌后只驱动 IK Target，不把控制器位置直接写入骨骼或
  角色 Rigidbody。
- 手腕约束采用 swing-twist 分解，分别限制屈伸、尺偏/桡偏和前臂旋前/旋后；持握时
  显示三组腕部活动范围弧线，接近限制时由绿转黄/红并发送限位震动。
- 牵引使用弹簧阻尼代理力、启停迟滞和 HMD/手部同向移动前馈。软范围内仅响应手臂 IK，
  超过阈值后才向 `AvatarMotor` 提交水平速度；根节点不接受腕部扭矩或竖直拉力。
- `AvatarMotor` 增加牵手驱动模式，最大速度沿用 `1.1 m/s`，加速度独立限制为
  `2.2 m/s²`。移动继续由共享 `PhysicsBody`、NavMesh 和碰撞代理执行。
- 左右手动作分别占用 `Locomotion + LeftArm/RightArm`，通过
  `AvatarActionCoordinator` 抢占移动、坐卧和随机动作；释放、跟踪丢失、目标跳变、
  超距、路径阻塞和暂停均执行统一 Cleanup。

### 二、编译与验证

- Unity Runtime、Editor 和测试程序集编译通过。
- `HandHoldTests` 专项测试：`5 passed / 0 failed`。
- 全量框架回归：`109 passed / 0 failed / 1 skipped`；跳过项为仓库既有的可选电视
  诊断视频用例。
- Android ARM64/IL2CPP Release 构建成功，Animation Rigging 已进入 IL2CPP 与裁剪链路。
- 最终 APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，SHA-256
  `731a6caa1b8abfb03096548d815069b4e4c0493b02bc9b633488e8934e666ed1`。
- APK 已覆盖安装至 `emulator-5554`，设备 `base.apk` 哈希一致；冷启动进程保持运行并
  输出 `MATESOUL_READY source=PICO Emulator Spatial Mesh items=4 itemState=ready navMs=82`。
  设备日志不再出现 HandHold Target 解析或 `TransformStreamHandle` 异常。

### 三、踩坑与后续验收

- Animation Rigging 的 Target/Hint 可以位于角色骨架链之外，但在 Android/Burst 下
  必须仍属于 Animator 根层级；运行时解除父子关系会导致流句柄无法解析并中止应用。
- 掌心交互代理不能直接添加到导入角色的嵌套 Prefab 骨节点。当前将 Socket 放在新增
  Rig 层并在 `LateUpdate` 跟随手掌，从而保持原始角色资产只读。
- PICO Emulator 的原生虚拟输入服务仍不能可靠模拟空间 Grip 操作。自动化已覆盖数学、
  Prefab、动作仲裁和 Android 启动，最终牵手手感、手腕范围和拉动阈值仍需在模拟器或
  真机中人工调校。

## Phase UI High Density 工作汇报：HUD 与窗口文字清晰度修复（2026-10-07）

### 一、问题与修复

- 上一阶段仅处理了图标纹理 Mipmap 和压缩，未解决整个 World Space Canvas 的动态
  字体分辨率问题。HUD 及系统提示仍使用 `CanvasScaler.dynamicPixelsPerUnit=1`，
  20px 左右的中文字形被直接投射到接近 1 米宽的空间窗口上，导致笔画和窗口内容发虚。
- 在 `AlwaysOnTopUi` 中新增统一的 CanvasScaler 配置，将
  `dynamicPixelsPerUnit` 提升到 `4`。20px 文字现在按约 80px 字形生成，再保持原物理
  尺寸显示。
- `GameHud` 与 `SystemNotificationPresenter` 均使用该配置；系统提示的 World Space
  Canvas 此前没有 CanvasScaler，本次已补齐。
- UI 物理尺寸、布局、图标造型、顶层 Shader、Canvas 排序和 XR 点击遮挡策略均保持
  不变。

### 二、验证

- HUD 专项回归 `1/1` 通过。
- Android ARM64/IL2CPP 构建成功，构建日志包含
  `Build Finished, Result: Success` 与 `MATESOUL_BUILD_PASS`。
- 独立修复包：
  `/Users/bytedance/MateSoul/.artifacts/ui-overlay-debug/Builds/MateSoul_Framework.apk`，
  SHA-256
  `39839ed93f8b069e59c45e8e3ac785b2751fa0f834c693e57c724c214e60586c`。
- PICO Emulator 冷启动截图确认系统提示与底部导航中文字笔画明显变清晰，角色和家具
  经过 UI 区域时仍不能遮挡 UI；目标 UI Shader/相机异常计数为 0。
- 截图后曾发生另一个并行验证流程覆盖同包名 APK；待 ADB 空闲后已重新安装独立修复包，
  并立即回拉 `base.apk` 校验，设备与宿主 SHA-256 最终完全一致。

## Phase Hand Hold Proximity 工作汇报：牵手近场准入（2026-10-07）

### 一、功能开发

- 新增
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/HandHoldPoseProvider.cs`，
  用统一的 `IHandHoldPoseProvider` 区分“射线瞄准位置”和“玩家物理手位姿”。
- Controller 模式改用 `ControllerItemRay`，牵手距离读取
  `ActionBasedController.transform`；8 米射线仍可用于 Hover 和普通物品抓取，但远端
  命中角色掌心不能建立牵手。
- EyeHand/Gesture 模式继续用 `HandItemRay` 查找目标，牵手距离与 IK Target 只读取
  `HandPinchPose` 驱动的 `Pinch Attach`，不读取 gaze ray 或 hand aim ray 的命中点。
- `HandHoldInteractable` 增加双半径判定：进入距离 `0.10 m`，已握住后的退出距离
  `0.16 m`。XRI 每帧重查 `IsSelectableBy()`，超出退出半径或跟踪失效时沿既有
  Select Exit 和 Action Cleanup 链路释放。

### 二、编译与验证

- Unity Runtime、Editor 和测试程序集编译通过。
- `HandHoldTests`：`8/8` 通过，新增覆盖远距射线拒绝、Pinch Pose 准入和双半径防抖。
- `GripGrabTests + GameRuntimeTests`：`26/26` 通过，确认普通物品远距抓取与交互模式
  切换未回归。
- 全量 Unity TestRunner：`112 passed / 0 failed / 1 skipped`；跳过项仍为未提供临时
  诊断视频时的可选电视测试。
- Android ARM64/IL2CPP Release 构建成功。APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，大小
  `92,858,480` 字节，SHA-256
  `97a8c7c1fb82a533a3e5f2f027cebb65b57df7001511437e9ec9e459966ecce4`。
- APK 已覆盖安装到 `emulator-5554`，设备 `base.apk` 哈希与宿主一致；冷启动进程
  保持运行并输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=3 itemState=ready navMs=47`。

### 三、模拟器验收边界

- PICO Emulator 当前只渲染射线，不渲染 MateSoul 自己的手柄模型；这不等于没有
  Controller Transform，运行日志已确认右手控制器 Pose Action 为 active。
- 但当前模拟器的 `virtual_input` 服务会反复退出，宿主键盘 `O/E` 只能稳定验证
  Grip 按钮回退，不能可靠地产生可控的 6DoF 手柄平移。因此它不适合作为本功能的最终
  手感验收环境。
- 近场准入和 IK 数学由自动测试覆盖；可视化动态验收应使用 Unity PlayMode 的
  XR Device Simulator，或直接使用 PICO 真机。真机验收必须确认手柄进入掌心约
  `10 cm` 后按 Grip 才成立，离开约 `16 cm` 后释放。

## Phase Bowling Pin Reset 工作汇报：球瓶复位误报与放置稳定性修复（2026-10-07）

### 一、问题与修复

- 玩家击球计分后错误提示“请移开球瓶原位置上的物品”。运行时证据确认，占位检测把
  `BowlingPinContacts` 为球瓶创建的 `Foreign Collision Shield` 当成外部障碍。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/PhysicsBody.cs`
  的 `CanOccupy` 现在排除已通过 `Physics.IgnoreCollision` 禁用的碰撞对。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/BowlingPin.cs`
  的 `Rack` 现在清零动态速度并锁定为 Kinematic，1 个游戏秒后由球瓶自身恢复动态。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/BowlingGame.cs`
  只触发一次 Rack，并把 `IsPlacementLocked` 视为已完成复位，避免逐帧重置锁定计时。
- PICO Emulator 的扳机回退补齐为右手 `U`、左手 `Q`；真机
  `triggerPressed/trigger` 绑定保持不变。

### 二、验证

- `BowlingTests`：`8/8` 通过。
- `PhysicsRuntimeTests`：`5/5` 通过。
- `GripGrabTests`：`2/2` 通过。
- 控制器扳机绑定专项：`1/1` 通过。
- PICO Emulator 真实输入链路完成
  `PlayerReady → PlayerRolling → CompanionRolling`；玩家得分日志为
  `BOWLING_SCORE player=True score=1 hit=1.601 elapsed=6.604`。
- 玩家计分后的 10 个球瓶复位样本均为角度/高度偏差/速度/角速度 `0`，锁定期间保持
  `isKinematic=true`、`contact=true`；没有新的占位阻塞事件。
- 最终稳定 Release：
  `/Users/bytedance/MateSoul/.artifacts/item-focus-debug/Builds/MateSoul_Framework.apk`，
  SHA-256
  `41845998d8108d809b5647d6a0dc49a65d4411f13f6512daa8a51578dceaf69b`。
  设备 `base.apk` 哈希一致，冷启动输出 `MATESOUL_READY`。

### 三、踩坑与后续

- 当前主工程并行 HandHold 场景曾生成缺少 IK 配置的 Release，冷启动会报
  `角色缺少牵手 IK 配置`。该包已废弃；本次交付使用已完成真实保龄球闭环的稳定场景
  快照构建。
- 完整根因与前后证据记录在
  `/Users/bytedance/MateSoul/debug-bowling-pin-reset-blocked.md`。

## Phase Emulator Launch Recovery 工作汇报：启动崩溃诊断与稳定包恢复（2026-10-07）

### 一、根因与修复

- `scripts/start-pico-emulator.command` 能正常启动模拟器、安装 APK 并执行
  `am start`；真正失败的是根目录 APK
  `97a8c7c1fb82a533a3e5f2f027cebb65b57df7001511437e9ec9e459966ecce4`。
- 设备连续复现 `APP CRASH(NATIVE)`。Unity 加载线程在约 1 秒后报告
  `assets/bin/Data/level0 is corrupted` 和 `Position out of bounds`，随后主动触发
  `SIGTRAP`；对应 tombstone 栈全部位于 `libunity.so` 的 `Loading.Preload` 路径。
- 将已完成真实冷启动验证的稳定 Release
  `41845998d8108d809b5647d6a0dc49a65d4411f13f6512daa8a51578dceaf69b`
  恢复为 `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`。
- 启动脚本现在会在 `am start -W` 后等待 5 秒并检查应用 PID。若应用已退出，会输出
  `level0`、`Position out of bounds` 或 `Fatal signal` 摘要并返回非零状态。
- 模拟器等待、安装和启动输出改为同时写入终端与
  `/Users/bytedance/MateSoul/.dbg/pico-emulator/emulator-host.log`，避免故障只落盘而
  用户看不到；同时删除了脚本末尾重复的重定向。

### 二、验证

- `bash -n scripts/start-pico-emulator.command` 通过。
- 不使用环境覆盖执行默认脚本成功，APK 宿主与设备 SHA-256 均为 `418459...`。
- `am start -W` 返回 `Status: ok`，5 秒存活检查得到 PID `4910`。
- 20 秒后进程仍在运行，并输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=0 itemState=recovery navMs=35`。
- 设备截图确认角色、MR 房间、系统提示和底部 HUD 正常渲染；诊断截图与 tombstone
  临时副本已清理。

## Phase Television Stale APK Fix 工作汇报：旧节目回退防护（2026-10-07）

- 旧节目回退不是 Prefab 或存档问题。上一阶段为规避损坏场景，恢复了 SHA-256 为
  `41845998...` 的旧稳定 APK；该包内 `sharedassets0.resource` 实际仍包含旧节目
  `840f571f...`（1920×1080、30 fps、19.6 秒）。
- `GameSceneBuilder.BuildAndroid` 现统一使用 `BuildOptions.CleanBuildCache`，防止
  Gradle debug/release 变体继续复用旧视频流。
- `start-pico-emulator.command` 新增 fail-closed 内容校验：安装前从 APK 中提取节目，
  与正式 `PixelSpaceGameProgram.mp4` 做 SHA-256 对比。坏包已验证会被拒绝。
- Clean Build 成功；最终 APK 大小 `117,799,263` 字节，SHA-256
  `d8edc592f413b7f8b64d38518e4a2ac3f6a1297de8843102e38d989ba22dcb68`。
  包内节目与正式资源哈希均为
  `c55bdafdcc691236eccb0c3639b2eb044d487eb6d19f6e7538c5e381879f0079`。
- 新包已覆盖安装到 `emulator-5554`，宿主与设备 APK 哈希一致，应用保持前台并输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=0 itemState=ready navMs=34`。
- 构建时一并修复后续 UI 测试引入的可见性编译错误：`AlwaysOnTopUi` 改为 public。
  结构化报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/television_stale_apk_fix_20261007.json`。

## Phase Hand Hold Simulator 工作汇报：编辑器内 IK 实机替代验证（2026-10-08）

### 一、可操作环境

- 导入 XRI 2.5.4 官方 `XR Device Simulator` 样例，共 `111` 个文件、约 `1.3 MB`。
- 新增 `MateSoul/Game/Run Hand Hold Simulator`，快捷键为 `Cmd+Shift+K`。该入口会重建
  MateSoul 场景、打开场景、进入 PlayMode，并只在本次运行中实例化 Simulator。
- Simulator 全局自动实例化保持关闭，避免额外的 `XRSimulatedController` 污染普通
  PlayMode 测试；Android 包不会包含运行中的 Simulator 对象。
- 左右 Controller Transform 增加仅编辑器可见的物理位姿球，角色双掌增加绿色目标球。
  控制器进入 `10 cm` 准入范围时变绿，建立牵手后变黄。所有标记均使用
  `EditorOnly` 标签且无 Collider。

### 二、验证

- Unity Runtime、Editor、Engine Test 程序集编译通过。
- `HandHoldTests`：`9/9` 通过。
- `BowlingTests`：隔离复跑 `8/8` 通过。
- PlayMode 实测已显示官方 Device Simulator 面板，可切换到 `Right Controller`；
  MockScene 初始化完成并输出
  `MATESOUL_READY source=PICO SDK MockSceneData items=0 itemState=ready navMs=5`。
- 本轮进入 PlayMode 后未新增 `UnityException`、`NullReferenceException`。
- 全量 117 项存在一个既有顺序相关输入测试抖动：
  `ProductionRayInputSnapsBowlingBallToHandAndReleasesIt` 在全量中偶发失败，隔离复跑
  `8/8` 通过；与按需 Simulator 实例化链路无关。

### 三、操作

1. 在 Unity 执行 `MateSoul > Game > Run Hand Hold Simulator`。
2. 点击 Game 窗口，按 `Y` 选择右控制器；按住 `W/S/A/D/Q/E` 平移，鼠标调整朝向。
3. 将粉色控制器位姿球移到绿色掌心球附近；控制器球变绿后按住 `G`。
4. 移动控制器观察手臂 IK 与角色牵引；松开 `G` 或离开约 `16 cm` 验证释放。

## Phase Fragile Props 工作汇报：独立筷子与易碎品破碎（2026-10-08）

### 一、功能完成情况

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemDefinition.cs`
  新增 `BreakableSettings`、`BreakSeverity` 和 `ItemPlacementPart`。目录定义现在可以声明
  易碎参数或一次放置展开的成员物品。
- “筷子”仍只占一个目录入口，但一次放置会原子创建隐藏的
  `chopstick-left`、`chopstick-right` 两个独立 `GameItem`。两根筷子各自拥有刚体、
  碰撞体、抓取状态、实例 ID 和存档记录，可以完全分离。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/BreakableItem.cs`
  使用 `collision.impulse.magnitude / Time.fixedDeltaTime` 估算平均碰撞力。盘子、
  马克杯和碗分别配置轻度/重度阈值；抓取、跟随、暂停和受控接触期间拒绝破碎。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/BreakableDebris.cs`
  生成 4 片轻度碎片或 12 片重度碎片。碎片继承原物体速度和角速度，由
  `GameSession` 统一暂停、恢复、销毁，20 秒后自动回收且不写入存档。
- `GameSession.PlaceItemAsync` 对组合成员实行同一事务：全部生成并保存才提交，失败时
  整组回滚。旧 `chopsticks` 存档继续按单根兼容预览恢复，不会阻断整批物品恢复。
- `GameSceneBuilder` 已重新生成盘、碗、马克杯、左右筷子、组合入口和正式场景资源。

### 二、参数与验证

- 盘子：轻度 `45 N`、重度 `95 N`。
- 马克杯：轻度 `38 N`、重度 `80 N`。
- 碗：轻度 `35 N`、重度 `72 N`。
- `GameRuntimeTests`：26/26；覆盖一次放置两根独立筷子、移动一根不影响另一根、
  两条持久化记录、持有免碎、轻/重碎片数量及 3 米真实坠落。
- `PhysicsRuntimeTests`：5/5。
- `GripGrabTests`：2/2。
- 正式 Release：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，大小
  `108,933,754` 字节，SHA-256
  `bafc04df36b8827cd6302caee41b8ed5f29f1620c523ab84bf769dce1f685f4a`。
- APK ZIP、v2 签名、`arm64-v8a`、PICO VR、Spatial Anchor 元数据均通过。
  设备 `base.apk` 哈希一致；冷启动保持前台并输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=2 itemState=ready navMs=34`。

### 三、技术债务与后续

- 当前碎片是程序化凸三棱柱，用于闭环验证物理和生命周期；最终表现可替换为美术预碎
  Prefab，但必须继续由 `BreakableDebris/GameSession` 管理。
- 当前阈值按 Unity 物理碰撞力代理设置，需要在 PICO 真机上按不同地面材质和实际释放
  速度校准，并补充碎裂音效与粒子反馈。
- 易碎品在玩家持有期间严格免碎，这是产品约束；如以后需要“持物敲击后破碎”，应新增
  明确的玩法模式，而不是移除公共持有保护。

## Phase Idle 01 工作汇报：新待机骨骼动画接入（2026-10-08）

### 一、功能完成情况

- 将两份新 FBX 非破坏性复制到
  `/Users/bytedance/MateSoul/MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Idle01/`。
- 新增 `LengyueIdle01Builder`，按正式 Lengyue Prefab 的骨骼路径重映射 Generic 动画，
  生成 30 FPS、9.1667 秒且可循环的 `Lengyue_Idle01_Combined.anim`。
- 新 Clip 共映射 69 根角色骨骼、690 条源变换曲线；头发、裙摆、控制器和 IK 辅助骨骼
  被排除，继续服从既有二次运动与 Animation Rigging 所有权。
- `Avatar.controller` 的 `Speed=0` 节点和源角色 `Lengyue_Locomotion.controller`
  的 `idle` 状态均已切换到新 Clip。角色移动时仍由原 Walk Clip 驱动，
  `AvatarMotor.Stop()` 将 `Speed` 归零后自动回到新待机。
- `GameSceneBuilder.BuildAnimator()` 已改为引用新 Clip，后续重建场景或运行时角色不会
  回退到旧待机。

### 二、验证

- Unity Editor 程序集编译通过；额外使用 Unity Roslyn 响应文件复编译通过。
- Unity Play Mode 正常进入，输出
  `MATESOUL_READY source=PICO SDK MockSceneData items=0 itemState=ready navMs=42`，
  未出现 `NullReferenceException` 或 `MissingReferenceException`。
- 生成资源校验：`m_StopTime=9.166667`、`m_SampleRate=30`、`m_LoopTime=1`。
- `Avatar.controller` 的 Idle 子节点和源 Locomotion Controller 的 idle 状态均引用
  `Lengyue_Idle01_Combined.anim`。
- 结构化报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Idle01/reports/unity_import.json`。

### 三、阻塞项与后续

- 用户提供的 `idle_01_BlendShape_InPlace(1).fbx` 本身不含任何 `Shape`、
  `BlendShape`、`BlendShapeChannel` 或 `SkinnedMeshRenderer.blendShape.*` 曲线；
  文件中唯一动画连接是 `Lengyue_Bob_Hair_v002` 的 Transform 曲线。因此本轮无法
  还原原作者设计的表情动画，当前新待机保持角色默认表情。
- 构建器已经实现真实 BlendShape 曲线的名称/Renderer 校验与合并逻辑。美术重新导出
  包含实际 BlendShape Deformer 和权重曲线的 FBX 后，覆盖同名文件并执行
  `MateSoul > Lengyue > Import Idle 01` 即可完成表情接入，无需修改运行时代码。

## Phase Fracture Art V2 工作汇报：盘子与马克杯预碎模型（2026-10-08）

### 一、功能完成情况

- 根据用户参考图重新制作四套预碎模型：
  - 盘子轻度：4 个不规则大块。
  - 盘子重度：22 个外圈、内圈和中心碎块。
  - 马克杯轻度：5 个杯体大块和 1 个完整把手。
  - 马克杯重度：20 个分层杯体碎块和 2 个把手段。
- 模型保留原有角色图案、UV、盘沿、杯沿、杯底和把手；切割面增加独立浅色陶瓷材质。
- 新增可重复建模脚本：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Breakables/scripts/build_breakable_assets.py`。
- 新增 Blender 源文件与几何报告：
  - `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Breakables/source/LavenderCeramicFractures.blend`
  - `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Breakables/reports/fracture_model_report.json`
- `BreakableSettings` 新增成对的轻度/重度 Prefab 引用。
- `BreakableDebris` 支持专属预碎 Prefab，同时保留碗使用的程序化回退；每片继续由
  `PhysicsBody/GameSession` 管理。
- `GameSceneBuilder` 自动校验碎片数、转换 URP 材质、生成四个正式 Prefab，并拒绝
  Collider/Rigidbody 混入美术资源。

### 二、资源与预算

| 变体 | 碎片 | 三角形 |
|---|---:|---:|
| 盘子轻度 | 4 | 6,900 |
| 盘子重度 | 22 | 9,900 |
| 马克杯轻度 | 6 | 10,828 |
| 马克杯重度 | 22 | 13,488 |

四套模型均为闭合流形，非流形边为 0。最坏同时产生 22 个瞬态刚体，20 秒后统一回收。

### 三、验证

- Blender 预览确认轻/重碎片密度、盘沿/杯沿、角色图案和把手结构符合参考方向。
- Unity URP 实际捕获确认贴图、断面材质和模型朝向正常；验证截图与一次性捕获脚本已
  清理，仅保留正式资源和数值报告。
- `GameRuntimeTests`：26/26；覆盖盘子和马克杯两级预碎模型、凸碰撞体、质量分配、
  持有免碎与真实坠落。
- `PhysicsRuntimeTests`：5/5。
- `GripGrabTests`：2/2。
- Release APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- 大小：`111,764,624` bytes。
- SHA-256：
  `aa6935cc54fba6f747685277ec51de53879a37666552857d77d64b075399a6ca`。
- APK ZIP、v2 签名、`arm64-v8a`、PICO VR 与 Spatial Data 元数据通过。
- 模拟器冷启动、进程存活、宿主/设备哈希一致，并输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=0 itemState=ready navMs=33`。

### 四、问题与后续

- 首轮运行时遇到 Unity `Object` 伪空引用：`GetComponent() ?? AddComponent()` 无法
  识别已销毁组件。已改为显式 `== null`，避免 `MissingComponentException`。
- 当前没有破碎音效、粒子和地面材质差异反馈；后续可在不改变破碎所有权的前提下补充。
- 22 个凸 MeshCollider 在自动化和模拟器中无 cooking 警告，但仍需在 PICO 真机测量
  破碎瞬间 CPU 峰值；若超预算，优先增加低面数碰撞代理，不降低视觉网格质量。

## Phase Fragile Audio 工作汇报：全易碎品陶瓷破碎音效（2026-10-08）

### 一、功能完成情况

- 将用户提供的
  `/Users/bytedance/Desktop/陶瓷破碎音效.mp3`
  非破坏性复制为
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Audio/CeramicBreak.mp3`。
- `BreakableSettings` 新增必填 `breakSound`；盘子、碗、马克杯共用同一 `AudioClip`，
  新易碎品漏配音效时 `Validate()` 直接失败。
- `BreakableItem` 只在 `GameItem.TryBreak` 成功后播放声音；持有免碎、低于阈值或
  破碎事务回滚时不会误播。
- `BreakableDebris` 在碎片根节点创建一次性 3D `AudioSource`，使原物品销毁后声音仍
  能播放完整，并跟随会话暂停、恢复和碎片生命周期销毁。
- 轻度破碎使用音量 `0.72`、音高 `1.04`；重度破碎使用音量 `0.95`、音高 `0.94`。
  两者均为全空间混合，距离范围 `0.35-7 m`。
- `GameSceneBuilder` 将素材导入为单声道、PCM、Decompress On Load、预加载且不后台
  加载，避免第一次破碎时临时解码。

### 二、验证

- 源文件与项目副本 SHA-256 一致：
  `a3a2aeda54a7294ef419ac368ca09cf41468301e8872e0dbe49fe195bd297270`。
- 音频：44.1 kHz，Unity 单声道，Clip 长度 `2.06366 s`（包含 MP3 编码延迟）。
- 破碎音效运行时用例：1/1。
- 生产目录共享音效与导入参数用例：1/1。
- `PhysicsRuntimeTests`：5/5。
- `GripGrabTests`：2/2。
- `GameRuntimeTests`：24/26；本次新增音频断言全部通过。失败项为当前独立可复现的
  `ProductionCharacterMovesAnimatesAndStopsWithoutLeavingNavMesh` 和
  `CharacterPushesLightObjectButCannotWalkThroughAnUnmappedWall`，两者隔离复跑仍失败，
  未触及本轮音频代码。
- Release APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- 大小：`111,935,968` bytes。
- SHA-256：
  `d3a7f25371b255d13ddb85d6a6229c8a9a9ea5480609e84a19edaf9ba0cf6b0b`。
- APK 内 `sharedassets0` 包含 `CeramicBreak`；ZIP、v2 签名、`arm64-v8a`、PICO VR 与
  Spatial Data 元数据通过。
- 模拟器冷启动后应用保持前台，输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=1 itemState=ready navMs=296`，
  未发现 Unity、FMOD 或 Audio 异常。

### 三、后续

- 当前所有陶瓷易碎品共享同一素材。后续若增加玻璃、木材等材质，应在
  `BreakableSettings` 中配置对应声音，不在中央协调器按物品名称分支。
- PICO Emulator 只能验证导入、播放调用和音频子系统无异常；最终响度、空间衰减和
  轻/重音高差异需要在头显扬声器或耳机上验收。

## Phase AI Emotion Audio 工作汇报：AI 四态语气音效（2026-10-08）

### 一、功能完成情况

- 对话网关最终响应新增必填 `response_emotion`，仅接受
  `normal`、`happy`、`sad`、`angry`。模型按完整上下文理解玩家本轮话语后，判定
  角色实际回复所表达的情绪；字段缺失或越界会进入现有一次协议修复，不静默回退。
- 长期 `MoodSystem` 与单轮 `response_emotion` 保持独立：前者继续负责可持久化心情
  数值、强度和衰减，后者只决定当前回复播放哪类语气音，避免历史心情污染本轮声音。
- `ConversationOrchestrator` 在动作和情绪信号落地前校验四态协议，成功回复后发布
  `LastResponseEmotion` 和单调递增的 `ReplySequence`；取消、超时、失败和非法响应
  不触发播放。
- 新增 `ResponseEmotionAudio`，由 `GameBootstrap` 在角色会话装配完成后绑定。组件
  订阅成功回复，只从对应资源池随机选择一个 Clip，停止上一条语气音后从角色头部的
  3D `AudioSource` 播放。
- 从用户素材目录非破坏性导入七个音效副本：常态 2、开心 2、沮丧 1、生气 2。
  项目副本统一转换为 44.1 kHz、16-bit 单声道 PCM 源，Unity 使用
  `DecompressOnLoad + Vorbis + Preload Audio Data`。
- 资源位于
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Resources/ResponseEmotionAudio/`，
  以情绪目录作为数据驱动映射；新增同类素材只需放入对应目录。

### 二、验证

- AI 网关 `npm run check`：14/14 通过，覆盖四态枚举、缺失字段、非法字段、repair、
  MCP 和 HTTP 稳定契约。
- Unity 定向测试：23/23 通过，覆盖四态发布、非法值故障闭合、四个资源池数量、
  多音效随机选择、回复完成时播放和 3D 音源配置。
- 真实 DeepSeek + MCP 请求成功，Request ID
  `aab3c4c3-da6d-4db1-9f41-73fd8f461428`，响应包含
  `response_emotion: normal`，请求头与响应 Request ID 一致。
- Unity 2022.3.38f1 完成脚本编译和七个 WAV 导入，无 C# 编译错误。
- 七个项目音效总大小约 1.3 MB；本阶段未重新构建 APK。

### 三、问题与后续

- Unity 2022 的 Bee 在无桌面 CLI 会话中会因 `Cannot create window: no screens
  available` 退出；本次通过 macOS LaunchServices 启动 Editor 后运行项目内测试请求。
- 当前语气音效只承担“配音占位”，字幕仍展示完整 `reply_text`。后续增加面部表情或
  情绪动作时，应消费同一 `ResponseEmotion`，并通过既有控制通道协调，不另建并行
  情绪状态机。
- 音量、最小/最大距离和不同素材间响度仍需在 PICO 头显上主观验收。

## Phase Movement Stall Fix 工作汇报：障碍阻塞停走与新待机验收（2026-10-08）

### 一、修复内容

- `MoveAction` 新增实际位移进度看门狗。角色连续 `1.25 s` 水平位移不足 `3 cm`
  时，判定物理障碍阻塞，立即停止 `AvatarMotor` 并让动作失败退出；每秒 NavMesh
  重规划不会重置该计时。
- `AvatarMotor` 新增 `0.05 m/s` 动画低速死区。墙面接触抖动或微小滑动不再持续混入
  Walk；正常移动、牵手速度和物理受力参数未改变。
- “未映射墙”测试现在同时校验：角色不穿墙、动作失败、Motor 停止、Animator
  `Speed=0`、新待机 Clip 权重为 100%。
- 正常移动测试新增明确断言：到达后当前 Clip 必须是
  `Lengyue_Idle01_Combined`，不再只检查“有动画”。

### 二、待机动画结论

- 新待机此前已经正确挂到生产 `Avatar.controller`；正常到达和障碍阻塞两个运行时
  用例均确认停止后 `Lengyue_Idle01_Combined` 权重大于 `0.99`。
- 用户感知不明显有两项原因：原 Bug 使墙前 `Speed` 无法稳定归零；源动画本身主要是
  头部和右臂轻微动作，骨盆/腿部基本静止，且表情 FBX 不含 BlendShape 曲线。
- 源动画数值采样：中段头部相对起始约 `24.88°`，右上臂约 `3.71°`，
  根节点位移为 `0`。因此动画存在，但远景下比走路动作克制。

### 三、验证与部署

- Runtime、Editor、Engine Tests 使用 Unity Roslyn 响应文件编译通过。
- 定向运行时回归：`2/2` 通过。
- 全量框架：`117 passed / 1 failed / 1 skipped`。唯一失败为既有
  `ProductionRayInputSnapsBowlingBallToHandAndReleasesIt` 球离手位置偏差，与本次
  `MoveAction/AvatarMotor` 无调用关系；本轮新增和移动相关用例均通过。
- Release APK 大小 `111,935,968` bytes，SHA-256
  `d3a7f25371b255d13ddb85d6a6229c8a9a9ea5480609e84a19edaf9ba0cf6b0b`。
- 宿主与模拟器设备 APK 哈希一致；冷启动成功并输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=1 itemState=ready navMs=41`。
- 结构化报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/movement_stall_idle_fix_20261008.json`。

## Phase Fragile Audio Timing 工作汇报：破碎音画同步修复（2026-10-09）

### 一、修复内容

- 将 `BreakableDebris` 的 `AudioSource.Play()` 从破碎事务末尾前移到碎片实例化与凸
  `MeshCollider` cooking 之前，避免盘子和马克杯先显示碎片、后请求播放破碎声。
- 新增 `BreakSoundRequestedAt`、`FragmentsReadyAt` 和 `BREAK_SOUND_TIMING` 结构化
  日志，直接度量播放请求相对碎片完成的领先量。
- `GameRuntimeTests.AssertBreakSound` 新增时序不变量：播放请求时间不得晚于碎片完成
  时间；原有 Clip、轻/重音量音高、空间衰减和暂停生命周期断言继续保留。

### 二、验证

- 定向破碎时序用例：1/1。
- `PhysicsRuntimeTests`：5/5。
- `GripGrabTests`：2/2。
- 实测播放请求领先碎片完成：
  - 盘子轻度 `6.7 ms`，盘子重度 `5.2 ms`，盘子真实坠落重度 `0.9 ms`。
  - 马克杯轻度 `16.0 ms`，马克杯重度 `7.2 ms`。
- Release APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- 大小：`112,083,919` bytes。
- SHA-256：
  `93220fc7ec7bea1b31dc13324ee83415b4cf9f296896592457e0148722237b3d`。
- APK ZIP、v2 签名、`arm64-v8a`、PICO VR/Spatial Data 元数据及
  `CeramicBreak` 包内资源检查通过。
- 模拟器设备与宿主 APK 哈希一致，冷启动后应用保持前台并输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=3 itemState=ready navMs=66`。

### 三、边界与后续

- 自动化证明的是 `AudioSource.Play()` 请求顺序，不等同于扬声器实际出声时间；PICO
  真机仍需复核主观同步效果。
- PICO Emulator 本次出现系统音频 HAL `pcm_writei` I/O 警告，但 MateSoul/Unity
  进程内没有 FMOD 或破碎音频异常。该平台告警不用于判断真机音频延迟。

## Phase Lavender Retro Radio 工作汇报：可交互收音机建模（2026-10-09）

### 一、工作完成情况

- 根据三视图完成浅紫复古收音机建模，机身包络严格为 240×90×160 mm；提手和伸缩
  天线按参考图位于机身高度标注之外，完整模型高度 268 mm。
- 完成圆角 ABS 外壳、前框、FM/AM 频率窗、红色调谐指针、扬声器与格栅、提手、
  两段式天线、脚垫、中文控制标识及爱心/星形装饰。
- 电源拨动开关、调台旋钮和音量旋钮均为独立网格与独立枢轴，不与外壳合并。
- Blender 根节点提供 `power`、`tuning`、`volume` 三个 `0..1` 属性，可实时驱动开关、
  两个旋钮和调谐指针。
- GLB extras、FBX custom properties 和独立
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Radio/reports/radio_interaction_manifest.json`
  共同记录控件轴向、范围、默认值及联动关系。

### 二、交付与验证

- Blender：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Radio/source/LavenderRetroRadio_240x90x160mm.blend`
- GLB：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Radio/exports/LavenderRetroRadio_240x90x160mm.glb`
- FBX：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Radio/exports/LavenderRetroRadio_240x90x160mm.fbx`
- 共 47 个网格、15,642 顶点、30,780 三角面；边界边与非流形边均为 0。
- GLB/FBX 回读尺寸均为 240×90×268 mm，三个控制枢轴均保留。
- GLB 节点 extras 验证通过，且无外部图片 URI。
- Blender 驱动实测通过：开关中心 X 为 81–89 mm，两个旋钮为 −135° 至 +135°，
  调谐指针 X 为 −87–33 mm。
- GLB SHA-256：
  `f817e6eb3237f534917dfe7ec4f1b54d7948f5f6a72a6927393dc2335e15e91a`。
- FBX SHA-256：
  `7d81a55289ab0766534caa935aa026ad7e09878b59212cd8fba1446f79609fb9`。

### 三、问题与边界

- 首轮上下控制面板存在 7 mm 共面重叠，渲染表现为错误深色条；已将上下区间分离，
  消除深度冲突。
- 通过降低字体曲线分辨率和格栅细分，将三角面由约 9.9 万降至 3.08 万。
- Blender 驱动可直接用于源文件预览；GLB/FBX 不执行 Blender Driver，运行时需按
  `radio_interaction_manifest.json` 将输入绑定到保留的控制枢轴。
- 本阶段不包含电台音频内容或 Unity/PICO 输入适配，避免在交互方式和节目源未确定时
  引入错误的业务实现。

## Phase Idle Lower-Body Fix 工作汇报：静止角色持续步态修复（2026-10-09）

### 一、根因与修复

- 运行时证据确认：角色被墙阻塞后刚体速度约为 0，Animator `Speed=0`，
  `Lengyue_Idle01_Combined` 权重为 1；问题不是 Walk 状态残留。
- 修复前同一 Idle Clip 的连续静止采样中，左右腿仍变化约 `1.74°/5.12°`，新导入
  待机自身的下肢曲线造成了“原地行走”观感。
- `LengyueIdle01Builder` 现在将腿、膝、脚、脚趾及膝部机械骨骼固定到新待机首帧；
  骨盆、脊柱、头部和手臂继续播放完整 9.1667 秒新待机。
- 保留 `MoveAction` 的 1.25 秒阻塞看门狗和 `AvatarMotor` 的 0.05 m/s 动画死区，
  未改变导航、牵手、坐卧、保龄球或动作协调所有权。

### 二、验证

- Idle 导入回归：1/1。完整周期下肢旋转变化 `<0.05°`，头部变化 `>5°`。
- 正常到达与未映射墙阻塞：2/2。
- 后修复墙前连续 Idle 采样：刚体速度约 0、`Speed=0`、Idle 权重 1，左右腿变化均
  为 `0°`。
- Release APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- 大小：`112,894,567` bytes。
- SHA-256：
  `0e584f6dff5662ef8abe6f1cd1d5fa046af8889febb4d852786b6d395ad8553a`。
- APK ZIP、v2 签名、`arm64-v8a`、PICO VR 与 Spatial Anchor 元数据校验通过。

### 三、环境说明

- PICO Emulator 本轮启动被 macOS `HV_DENIED` 拒绝，未执行设备侧画面复核；问题修复
  由生产 Prefab 的 Play Mode 状态、逐帧骨骼数值和生成 Clip 回归共同验证。
- 先前“新 Idle 已正确播放，因此只是不明显”的结论不完整；正确结论是 Animator
  切换正常，但新 Idle 的下肢内容本身不满足静止语义。

## Phase Lavender Chibi Clock 工作汇报：可转动指针时钟建模（2026-10-09）

### 一、工作完成情况

- 根据三视图完成直径 160 mm、厚度 40 mm 的浅紫极简时钟，包含圆角后壳、前框、
  暖白表盘、12 个立体刻度、中心盖、时针和分针。
- 时针与分针均为独立闭合网格，分别挂载到
  `Clock_HourHand_Pivot` 与 `Clock_MinuteHand_Pivot`；两个枢轴共享精确表心，
  但可独立绕局部 Y 轴旋转。
- Blender 根节点提供 `hour_angle_degrees` 和 `minute_angle_degrees` 属性，
  可直接预览 −180° 至 +180° 指针姿态。
- GLB extras、FBX custom properties 与
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Clock/reports/clock_hand_manifest.json`
  共同记录指针节点、旋转轴和范围。
- 已交付 Blender、GLB、FBX、五张 1024×1024 渲染图、三视图总览、构建脚本、
  README 和结构化模型报告。

### 二、验证结果

- 实测尺寸严格为 160×40×160 mm。
- 9 个闭合网格、6,084 顶点、12,096 三角面；边界边与非流形边均为 0。
- Blender 两组指针 Driver 的 −180° 与 +180° 极值实测通过。
- GLB/FBX 回读尺寸一致，两个指针枢轴均保留。
- GLB extras 检查通过，且无外部图片 URI。
- GLB SHA-256：
  `5c654b36e5fb7039e46faec2acc651975a538b3f411c1ab74b6a984c001d009a`。
- FBX SHA-256：
  `f142adb3be1ea0bc7f6fec63716487590a781f3ef79e70a57495cbe91d033c94`。

### 三、问题与边界

- 首轮后盖参数前后顺序错误，导致厚度为 40.4 mm；调整后盖区间为 17–20 mm 后恢复
  40 mm 精确包络。
- 新建 Driver 在同一 Blender 后台进程的首轮渲染前未求值，曾导致两根指针暂时重叠
  在 12 点。最终流程在渲染和导出时显式应用参考姿态，Blend 中仍保持 Driver 启用。
- 当前只交付可转动模型结构，不包含 Unity/PICO 时钟逻辑。后续应只旋转两个 Pivot，
  不直接修改指针网格或表壳 Transform。

## Phase Radio & Heavy Impact Audio 工作汇报：收音机与金属重物音效（2026-10-10）

### 一、功能完成情况

- 将 `LavenderRetroRadio_240x90x160mm.fbx` 接入虚拟物品目录，运行时包络为
  `0.24×0.268×0.09 m`，质量 `1.8 kg`，支持地面/家具表面放置和整体抓取。
- `RadioPowerButton` 使用现有 XR UI 点击通道切换电源；`RadioKnobControl` 将世界空间
  指针拖动映射到模型独立旋钮枢轴，调台旋钮同步驱动红色频率指针，音量旋钮连续调节。
- `RadioController` 独占三个空间音源：两个音乐声道负责等功率交叉淡化，一个噪声
  声道负责换台沙沙声。跨多台调节时按相邻台逐级切换，不直接跳过中间台。
- 三首曲目按用户顺序配置为 `BlueDiscoDance`、`BlueHour`、`SunsetGlow`。每首不直接
  `loop`，而是在结束前用双声道将尾部淡出、开头淡入，默认循环交叉淡化 `2.4 s`。
- 音乐导入为单声道、Streaming、Vorbis；短调频噪声为 PCM/Decompress On Load。
  音量使用平方曲线映射，电源开关带 `0.3 s` 淡入淡出，避免突发点击声。
- `ItemDefinition` 新增 `ImpactSoundSettings`。桌子、椅子、复古电视显式标记为金属
  重物，使用 `120/350/850 N` 轻/中/重三级阈值，对应音量
  `0.46/0.72/0.95`，并保留一致的 3D 衰减、音高分级和暂停生命周期。
- `ImpactSoundEmitter` 在持有、跟随、暂停和受控接触期间不播放，初始放置后延迟
  `0.25 s` 才启用，并用 `0.18 s` 冷却避免倒地时多碰撞体连续爆音。

### 二、资源与代码

- 音乐：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Audio/Radio/`
- 金属撞击：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Audio/MetalHeavyImpact.wav`
- 收音机 Prefab：
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/Radio.prefab`
- 核心类：`RadioController`、`RadioPowerButton`、`RadioKnobControl`、
  `ImpactSoundEmitter`、`RadioBuilder`。

### 三、验证与部署

- `RadioTests`：4/4，通过电源、音量、换台三声道交融、相邻台顺序和循环首尾淡化。
- `ImpactSoundTests`：3/3，通过生产配置、三级音量和真实椅子坠落。
- `PhysicsRuntimeTests`：5/5。
- `GripGrabTests`：2/2。
- Android Development APK：`128,215,436` bytes，SHA-256
  `b4a4e0273a63281f9d3c4d60e8d13f9029bdc50c04463e0036474fb1e9483336`。
- APK ZIP、v2 签名、`arm64-v8a`、PICO VR/Spatial Data 元数据通过；Build Report
  确认三首音乐、调频噪声和金属撞击资源全部入包。
- 宿主与模拟器设备 APK 哈希一致；冷启动后应用保持前台并输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=3 itemState=ready navMs=44`，
  应用进程内音频/FMOD/空引用异常为 0。

### 四、边界与后续

- 当前 APK 使用本地 HTTP AI 网关，因此按项目准入规则构建为 Development 包；Release
  包仍要求将对话端点切换为 HTTPS，未为本任务放宽安全策略。
- PICO Emulator 可验证状态机和播放调用，不能替代头显扬声器上的空间响度、调台手感
  和碰撞阈值主观验收。真机应重点校准旋钮拖动灵敏度、沙沙声峰值和三类家具阈值。

## 2026-10-10：模拟器 20–40GB 内存异常排查（进行中）

- 用户报告游戏启动时模拟器占用 20–40GB。当前未获得该现场的进程采样，尚未确认
  泄漏所有者，不能标记已修复。工作分支为 `fix/emulator-memory-growth`，
  保留其他任务已有未提交改动。
- 已核实宿主 16GB 物理内存、AVD RAM 6144MB、Unity 使用 Vulkan，经
  `angle_indirect` / MoltenVK 在宿主渲染。已有 Vulkan 时间戳缓冲区报错为
  65536B 超过 32768B 限制，不能据此推断总内存耗尽。
- 自动冷启动被 `HVF error: HV_DENIED` 阻止，游戏没有启动。需要用户在 macOS
  Terminal 执行一次 `/Users/bytedance/MateSoul/scripts/diagnose-pico-memory.command`。
- 新采样入口自动完成空模拟器 30 秒、游戏 180 秒、退出游戏 30 秒，采集宿主
  physical footprint、Android PSS、vmmap 和 logcat；在宿主模拟器达到 10GiB
  时停止本轮，避免继续复现到 20–40GB。该保护线不是修复或正常内存指标。
- 已通过 Python/Shell 语法与 macOS 内存计数器实测：预留 1GiB 虚拟地址不改变
  footprint，实际触碰 16MiB 才增加约 16MiB。完整三阶段验证尚待运行。
- 记录：`/Users/bytedance/MateSoul/debug-emulator-memory-growth.md`；
  证据目录：`/Users/bytedance/MateSoul/.dbg/emulator-memory-growth/`。
- 下一步：先用宿主/guest 差值定位资源所属层，再实施修复与相同条件下的对比验证。

### 第二轮：采样定位与候选修复（待游戏复测）

- 用户完成 `20261010-134741` 采样：空模拟器稳定约 8.03GiB；游戏运行约
  115 秒后达到 10.001GiB 并触发保护，没有进入退出游戏观察阶段。
- `vmmap` 显示宿主 `owned unmapped (graphics)` 从 1.5G / 309 regions
  增至 3.8G / 1910 regions；malloc 仅从 534.6M 增至 554.0M。
  加载稳定后游戏 PSS 在约82秒内仅从 642515KiB 增至 649622KiB。
  主要增长位于宿主图形侧，但仍不能排除应用发起 GPU 分配。
- 确认实际动态库为 MoltenVK 1.2.11 / Vulkan 1.2.304。参考上游 gfxstream
  b/364055067 兼容处理，在 `scripts/start-pico-emulator.command` 的驱动加载前
  设置 `MVK_CONFIG_USE_METAL_ARGUMENT_BUFFERS=0`，保留 Vulkan、Metal heaps、
  6GB AVD、画质和当前 APK。此变更是候选规避，尚未确认它就是完整根因。
- 同库原生测试：120轮 × 2048 descriptor sets，确认配置 1→0；单池额外
  Metal allocation 从 1081344 bytes 降至 0。两种模式释放后均回到
  458752 bytes，独立池未复现泄漏，不能将这组数字报告为游戏内存改善。
- Shell 语法、采样脚本执行和 macOS 内存计数器自检通过。新增 `--post-fix`
  模式和游戏存活检查，保护终止时补取 guest meminfo 与限定条数 logcat。
- 下一步从 macOS Terminal 执行
  `/Users/bytedance/MateSoul/scripts/diagnose-pico-memory.command --post-fix`，
  比较启动、稳定运行、退出后的 footprint、graphics 和 PSS。当前自动执行环境
  的 `HV_DENIED` 未解除，不能代替这次完整游戏验证。

### 第三轮：采样保护线扩展

- 首次测试的空模拟器基线约8.03GiB，10GiB保护线只留下约2GiB增长窗口，
  的确可能在20–40GB症状充分出现前结束；该阈值不再用于后续复测。
- `scripts/diagnose-pico-memory.command` 的进程硬保护线已调整为25GiB，
  仍保持每2秒宿主采样和约每10秒 Android PSS 采样。
- 由于宿主只有16GiB物理内存，不能仅依赖25GiB进程线。新增每10秒系统安全检查：
  系统内存余量≤5%、根卷余量≤8GiB，或内存余量≤10%且交换区余量≤128MiB
  时先抓取 vmmap、guest meminfo、logcat，再停止本轮。
- `run.json` 新增 `hardLimitGiB` 与 `stopReason`，用于区分25GiB进程上限、
  系统压力提前停止和完整三阶段完成。
- 调整后本机预检为内存余量37%、swap余量1427.38MiB、根卷余量约39.5GiB，
  没有命中提前停止条件。Python AST、25GiB常量、系统指标解析及内存计数器
  自检通过；完整 post-fix 游戏复测仍需从 macOS Terminal 执行。

## Phase Radio Control Timing Fix 工作汇报：收音机控件与碰撞声修复（2026-10-10）

### 一、问题与修复

- 金属撞击素材实测前导静音约 `99.7 ms`，这才是碰撞后延迟感的主要来源。项目副本
  裁掉 `94 ms`，保留约 `5.7 ms` 安全前导；仍为单声道 44.1 kHz PCM 和预加载，
  不引入预测碰撞的误报风险。
- 音量旋钮旧实现按下即按绝对指针角赋值，且 `+180°/-180°` 存在断点。新实现按下只
  锁定当前角度，拖动使用相邻采样的 `Mathf.DeltaAngle` 累加，跨断点保持连续；顺时针
  从“小”到“大”时归一化值和实际输出严格单调增加。
- 电源滑块不再假设 `local +X` 是“开”。`RadioBuilder` 读取“关/开”标签方向，并根据
  滑块与槽体真实包围盒计算两个绝对端点；开机动画向“开”移动且完整留在槽内。
- 红色频率针不再以导入初始位置加减固定 `0.12 m`。Builder 从频率窗边界扣除指针半宽
  后生成最小/最大局部位置，旋钮 `0..1` 在该区间绝对插值，端点不会飞出窗口。
- Android 最终构建准入再次显式写入 Hand/Eye/VST/Spatial Anchor/Spatial Mesh，
  防止隔离工程漏同步 `PXR_ProjectSetting.asset` 时生成 `enable_vst=0` 的无效 APK。

### 二、验证

- `RadioTests`：5/5；新增覆盖按下不跳值、跨 `179°→-179°` 连续、顺时针音量单调、
  开关朝“开”且不出槽、红针最小/中点/最大映射。
- `ImpactSoundTests`：3/3；新增要求播放请求后 `12 ms` 内出现可听瞬态，实际素材前导
  为约 `5.7 ms`。
- `PhysicsRuntimeTests`：5/5；`GripGrabTests`：2/2。
- Android Development APK：`144,508,187` bytes，SHA-256
  `983a6b287c878f67686a4f30362e44f498ba4d0599c7335703495b9a46544cf6`。
- APK ZIP、v2 签名、ARM64、`pvr.app.type=vr`、`enable_vst=1`、
  `enable_spatial_anchor=1` 通过。
- 宿主与模拟器设备 APK 哈希一致；冷启动后应用保持前台并输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=3 itemState=ready navMs=33`。

### 三、边界

- 模拟器不能代替真机扬声器输出和控制器拖动手感验收，最终仍需复核碰撞瞬态、旋钮
  灵敏度及滑块/频率针视觉。
- 当前分支另有 `GameHud.ReportWindowGlyphCoverage` 空参数日志，与本阶段音频和收音机
  调用链无关，本阶段未覆盖该并行 HUD 调试改动。

## 2026-10-10：模拟器内存与虚拟物品交互关联诊断（进行中）

- 25GiB post-fix 采样未触发硬上限或系统安全条件。游戏运行约180秒，
  QEMU footprint 8.56→14.88GiB，结束前15.01GiB；OLS约36.37MiB/s，
  即2.13GiB/min。
- Android游戏PSS仅614007→671628KiB。`vmmap`确认宿主
  `owned unmapped (graphics)` 从1.5G/304 regions升至8.8G/7034 regions，
  malloc仅约增加44MiB。force-stop游戏后QEMU在约12秒由15.10降至6.63GiB。
- 用户补充：无操作时约9GiB稳定；创建桌椅、碗、收音机并抓取、投掷、破碎、
  操作收音机后才快速升至15GiB，历史20–40GB也伴随高频物品交互。因此当前
  不能认定是静态VST自行增长，也不能仅凭现象认定为虚拟物品对象堆泄漏。
- 静态检查未见收音机/碰撞声每帧创建资源：AudioSource、MaterialPropertyBlock
  均复用，破碎只创建有限碎片并有回收。待验证边界是焦点材质重绑定、物理/破碎
  事件，以及交互把模拟器从10FPS唤醒到60FPS后放大的逐帧图形泄漏。
- 已在Development路径增加低频结构化事件：每2秒FPS/物品/刚体/碎片/收音机状态，
  以及spawn、focus、grab/release、break、抽样impact audio、radio power/knob。
  每个请求完成后显式Dispose，不记录每帧或每个原始碰撞。
- Unity脚本编译、Tundra、ILPP与Android Development构建成功。诊断APK：
  `144,523,739` bytes，SHA-256
  `d96ceeaea5a2ccc0b1c41b35e29714f87ee8a90c729d58327c6c29751b840115`；
  ARM64、Development、v2签名、VST/Spatial Anchor/Spatial Mesh声明通过。
- 启动器Manifest布尔值检查改为精确匹配值`0x1`；旧逻辑会将类型标记`0x10`
  误判成启用。采样器新增 `--interaction-correlation` 运行标识。
- 下一步执行分段复现，把宿主footprint与事件时间线关联后再实施根因修复。

## Phase Idle Facial/Cadence 工作汇报：行走混合与待机神态修复（2026-10-10）

### 一、修复内容

- 运行时数据确认 Walk Clip 周期仍为 1.0667 秒，没有被减速；稳定移动速度约
  `0.992 m/s` 时，旧 `1.1` 阈值只产生约 `90.2%` Walk 权重，其余为固定下肢
  Idle，导致步幅缩小并加重滑步观感。
- 将生产 `Avatar.controller` 和场景重建器的满 Walk 阈值改为 `0.9 m/s`。低速启动
  仍平滑混合，正常速度下 Walk 权重达到 100%，未修改角色移动速度。
- 再次确认 `Lengyue_Idle01_BlendShape_InPlace.fbx` 源文件没有
  `SkinnedMeshRenderer.blendShape.*` 曲线。Idle 导入器仅在源曲线数为 0 时生成
  5 条自然神态兜底：左右眨眼、左右柔和眼神和轻微微笑。
- 若后续替换为有效 Morph FBX，导入器优先复制源曲线，不叠加兜底。

### 二、验证

- 定向回归 `3/3`：稳定行走 Walk 权重 `>99%`、墙阻塞正确回 Idle、Idle 下肢静止。
- 最终 Idle Clip 含 5 条面部曲线；眨眼峰值 `>99`，柔和眼神 `>5`，微笑 `>8`。
- 导入报告：`sourceFacialBindings=0`、`facialBindings=5`、
  `facialFallbackGenerated=true`，明确区分无效源文件和运行时兜底。
- Android Development APK：`144,523,552` bytes，SHA-256
  `803da0a465c69e1710559d0939c07374c1e169bdb135f9fcb51cfc9bfe9d4d59`。
- APK ZIP、v2 签名、ARM64、PICO VR 和 Spatial Anchor 元数据通过。

### 三、边界

- 当前无在线模拟器/真机，因此未执行设备侧主观步幅和表情强度验收。
- 当前表情是明确标记的自然待机兜底，不等价于动画师原始神态；要恢复原作，仍需
  重新导出包含 `BlendShapeChannel` 和权重动画的 FBX。

## Phase Lengyue Final Asset Export 工作汇报：最终游戏资产交付（2026-10-10）

### 一、交付内容

- 创建最终资产包：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Release/Lengyue/FinalGameAsset_20261010/`。
- 输出总包：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Release/Lengyue/Lengyue_FinalGameAsset_20261010.zip`。
- 交付文件包含：
  - `Lengyue_FinalGameDefault.vrm`：带 VRM 0.x 元数据和 Humanoid 映射的外部交换版。
  - `Lengyue_FinalGameDefault_Rigged.glb` / `.fbx`：带 116 骨身体骨架和 247 骨发型骨架的交换文件。
  - `Lengyue_FinalGameDefault_VisualExact.glb`：当前外观视觉快照，适合外部快速查看。
  - `Lengyue_GameCharacter_UnityProduction_Runtime.zip`：游戏内运行时资产、脚本、材质、贴图、Prefab 与 meta 文件。

### 二、验证结果

- VRM 文件回读确认：存在 `VRM` 扩展、51 个 Humanoid 骨映射、2 个 Skin。
- Blender glTF 导入确认：身体骨架 116 骨，发型骨架 247 骨，导入 Mesh 数 53。
- Unity 生产资产 zip 内确认包含 `Lengyue_GameCharacter.prefab`、
  `LengyueWardrobeController.cs`、`MateSoulCharacterToon.shader` 和
  已注册睡衣 `PinkPajamas.asset`。
- 总包大小 `58,695,335` bytes，SHA-256：
  `50b4d0e018999cfc592935f2bb09037eb3475706c3d35b8f5eb2332870d394c3`。

### 三、边界说明

- Unity Prefab 仍是游戏内唯一权威资产；VRM 不能表达
  `LengyueWardrobeController`、Animator Controller、URP Toon Shader、
  袖子/裙摆/头发运行时求解等逻辑。
- `.vrm` 是外部查看和交换格式，不应作为 MateSoul 运行时替代资产。
- 默认 VRM/GLB 为 `lavender-casual` 外观；`pink-pajamas` 保留在 Unity 运行资产包中作为已注册换装。

## Phase Dialogue Focus Cancellation 工作汇报：系统键盘失焦取消修复（2026-10-10）

### 一、问题与根因

- 真实 HUD 点击发送后频繁显示“对话已取消”，但网关日志中的对应请求均在
  `1.45-2.40s` 内成功完成，排除 AI API、网关和 30 秒超时。
- 运行时调用来源日志确认：PICO 系统键盘或应用切换触发
  `OnApplicationPause/OnApplicationFocus`，随后 `GameSession.SetPaused(true)`
  在请求处于 `Thinking` 时调用 `Conversation.Cancel()`。
- 同一帧会话变为 `Cancelled`，Provider 随后观察到请求令牌取消。显式取消按钮和
  Session 销毁均未触发。

### 二、修复

- 暂停仍冻结角色、物理、物品、动作和存档，但不再取消独立拥有生命周期的 AI 请求。
- 显式“取消”、小游戏接管、30 秒请求超时和 Session 销毁仍可终止请求。
- 保留“请求进行中拒绝重复发送”和发送按钮禁用，避免并发请求覆盖。
- 参考 MateFairy 的生命周期边界：输入 UI/短暂失焦不拥有主对话取消权。

### 三、验证

- 定向引擎回归 `TransientPauseDoesNotCancelInFlightConversation`：`1/1` 通过。
- Gameplay 会话回归：`22/22` 通过。
- 修复前失焦复现：`Thinking -> SetPaused(true) -> Cancelled -> tokenCancelled=true`。
- 修复后同场景：暂停和恢复期间保持 `Thinking`，HTTP 200 后进入 `Replied`；
  联网工具请求端到端耗时 `9.97s`。
- 最终 Development APK：`144,535,776` bytes，SHA-256
  `aea65e371e0939192c5fbca6f0fb794b79de71770079c3adb782d34be6951620`；
  设备端安装包哈希一致。

### 四、后续

- 等待真实 HUD 点击确认；确认后删除 `dialogue-font-tofu` 调试插桩、日志、环境文件
  和调试记录，只保留业务修复与回归测试。

## Phase Lengyue Quality Baseline：冷月品质升级前置审计（2026-10-10）

- 本轮授权范围为阶段 0–3；未获确认不得进入阶段 4 重建或修改生产资产。
- 最新设计约束优先：外观以冷月为准，鬼瓦轮仅用于质量/制作方法分析，不将其轮廓相似度当作冷月验收目标。
- 工作目录：`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v001/`。
- `baseline/production_snapshot.zip` 冻结 171 个文件及冷月 VRM，配套 SHA-256 清单、GUID 依赖图和 Prefab 序列化清单；GUID 扫描未发现未解析项。包含现有未提交版本，不覆盖并行游戏开发变更。
- `scripts/analyze_vrm.py` 已读取两份 VRM 的 accessor、索引、骨骼、逆绑定矩阵、法线、UV、权重、Morph 和材质，输出两套机器可读统计。面数核实为冷月 150,906、参考 49,389。
- 发现并修正分析器坐标陷阱：冷月身体网格节点带 0.01 变换，但蒙皮逆绑定抵消该变换，必须按 glTF 完整蒙皮公式求世界坐标。仅乘节点矩阵会把约 20.2cm 的脸宽误算为 2mm。
- `unity_review/` 是无生产目录链接的独立 Unity 2022.3.38f1 / URP14 工程；`scripts/LengyueBaselineCapture.cs` 负责固定灯光采集。首轮批处理启动缺少活跃许可证，正在通过本机已登录 Unity Hub 恢复正常启动。尚未宣称 Unity 基线或视觉验证通过。
- 发束曲线目前是连通组件与高度切片候选，不等同于已确认的艺术主束；未经重建的 `fit_report.json` 明确使用 null/未适用状态，不伪造拟合成绩。

## Phase Lengyue Quality Review：首轮评审材料交付（2026-10-10）

- 已补齐上述基线阶段的Unity采集：隔离工程编译通过，15张固定基线覆盖四视图、闭眼/微笑/张嘴、Idle/Walk/Run采样与强侧光。Missing Script为0，5种绑定材质Shader错误为0；编辑器采样不等同于游戏/物理验收。
- 双模型22份统计重复生成逐字节一致，13项数值不变量通过。新增`audit_unity.py`核实生产源Prefab为8个中性激活Renderer、150764面、94508顶点。
- 生产头发19520顶点、25824面，Tangent/UV0/UV1齐全；不能根据无Tangent的VRM断言生产缺失。UV0有6736个面积退化三角形，后续先区分可见面与恒色封口。
- 生产Animator的Avatar为空，现有BodyOnly动画使用Transform曲线。VRM Humanoid元数据不代表运行时已验证Humanoid Avatar；外观任务须保留既有动画接口。
- 交付评审文档、目标JSON、3张PNG评审板及后续检查清单，位于：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v001/review/`。
- 目标改为冷月自身身份保持：包络±3%、四视图身份IoU≥0.95为待批准草案。参考白模只解释工艺，没有制作换色参考或优化后模型。
- 尚缺语义分束、真正头部地标、分区/动态间距、区域纹理统计及PICO运行验证；报告明确列为未完成，不视为全阶段验收。按原提示阶段2/3确认边界，先等待方向确认，再补门禁后建立独立代理。
- 本轮结束核对171个冻结生产文件无哈希变化。未修改生产模型/Shader/Prefab/脚本，也未切换当前工作分支。

## Phase Lengyue Quality Implementation：全部后续阶段已授权（2026-10-10）

- 用户明确要求：“很好，直接完成后续所有阶段的步骤工作”。这取代前一轮逐阶段等待确认；继续完成前置缺项及阶段4–12，不再反复确认。
- 在`/Users/bytedance/MateSoul/.artifacts/lengyue-quality-worktree`创建`feature/lengyue-quality-upgrade`分支，基点`91ccc17a397271cc5cc39fb261a40e73655a23c8`。主工作区`fix/emulator-memory-growth`及其未提交工作保持原样。
- 实施状态与工具索引位于`ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v002/IMPLEMENTATION.md`。
- 已定位既有37发束参数曲线、247骨弧长权重生成器、Volume_v002/v003帽壳与截面修订及最终服装DCC源。下一步导出Unity精确绑定数据，独立重建头发并逐项完成材质/脸眼/服装/运行时/导出和验证。
- 当前仅完成实施环境准备，新网格和最终升级资产尚未生成；任务仍在执行中。

## Phase Lengyue Quality Hair：独立头发候选与静态验证（2026-10-10）

- 已导出生产模型精确网格/绑定/表情，独立重建37发束及帽壳，18476面（原25824）、17347渲染顶点，保留247骨接口，UV退化清零。
- 38个连通壳体均闭合、面朝向一致；脸/眼/眉双向三角形交叉均为0，四视图轮廓IoU为0.9779–0.9910。检查的是静态候选，不是动态或设备验收。
- 为清除旧刘海与额头的相交，局部tip最多径向外移11.4316mm；root/tip RMS为3.2547mm，超过原3.0325mm草案。报告明确保留原阈值失败状态，后续披露安全修正的局部容差调整，不能虚报全项通过。
- 独立Unity工程已编译并生成新Prefab Variant及8张白模对照；候选Blender源含37条可编辑曲线。尚未用正式新材质替换，也未合入主生产工程。
- 产物：`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v002/`；当前细节及继续执行项见`IMPLEMENTATION.md`。
- 正在继续已授权的材质/Shader、脸眼、服装、表情物理、换装、VRM和游戏/设备验证，不在本阶段结束任务。

## Phase Lengyue Quality Surfaces：正式材质与脸眼/服装候选（2026-10-10）

- `v002/scripts/MateSoulCharacterQuality.shader`新增独立Shade、眼部Emission及8种调试输出；保留旧属性名，Shade默认关闭。旧生产Shader未改动。
- 生成11张独立贴图，Hair/Face/Cloth采用Base+Shade；头发按40格UV制作非白色短高光，仅7条主束采用强高光，眼睛升级到1K与独立高光贴图。
- 脸部由2552增至7986面，保留1548个旧顶点的位置和15个旧Morph名称/顺序，增加A/I/U/E/O、大笑、害羞和惊讶组合Morph；眼睛由32增至512面，边界固定、中央弧面抬升至多0.35mm。
- 服装按UV2的6个part隔离减面，从113128降到54296面；全部顶点+三角中心的双向表面误差最大0.265412mm。骨路径/bindpose、权重和袖子part3/5保持。
- Unity隔离工程完成两轮编译与四视图头部/全身对照及8种调试渲染。首轮面部碎片阴影通过80%宏观椭球法线场修正；发束侧壁黑色虚线通过92%宏观法线场减轻。仍需多光照动态验收。
- `surface_validation.json`通过中性网格契约；原15种Morph的0/50/100%共45项同姿态父三角比较中，新引入翻转为0。相对中性姿态法线反向不能直接解释成破面，眨眼本身可能旋转超过90度。
- 补齐`hair_signature.json`、513组视图内投影遮挡关系和诊断板，完成参考47个UV区域与冷月37发束的材质统计；参考原始数据仅留在内存。
- 新候选已复制到独立工作树的`MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/QualityUpgrade/v002/`，尚未提交。仍需完整DCC源更新、表情物理/换装回归、VRM、游戏与设备验证，不构成最终验收。

## Phase Lengyue Quality Runtime：DCC、表情与游戏/VRM验证（2026-10-10）

- 完整Blender候选已同步新脸眼、服装、UV、法线、骨权重和23个Face Shape Key；原源文件未覆盖。生成面部16层截面、曲率/法线诊断及69项Morph 0/50/100位移热图。地标为10个明确标注的几何极值代理，不冒充完整人工解剖标记。
- 新`LengyueFaceControl`保留Animator为面部曲线唯一写入者，增加六种情绪、五个嘴型、左右眨眼和Animator眼部注视入口。`QualityExpressionRegistration`把表情能力接入共享动作协调器的Face通道；网络AI Payload适配及实时音素输入未声称完成。
- 新建游戏包装Variant与独立`MateSoul_Quality.unity`。完整游戏场景实测450帧，导航、6表情、5嘴型、2次换装共14个动作全部Succeeded，错误0；每个表情中途核验实际网格权重并截图。
- 独立角色场景实际运行1080帧、16次换装；350张固定状态/灯光/相机A/B图及36帧/角色的BakeMesh用于数值验证。新Variant修正原负半径碰撞球并限制刘海向深度方向摆动。眼部保留原虹膜嵌入边界，用三角交线几何比较代替直接对比交叉边数量；18帧容差检测通过，严格“零交叉”仍为false。
- 修复了验证工具的真实资产保存问题：对已有Mesh的`CopySerialized`未可靠落盘，已改为直接清空/赋值原Mesh数据并保存，独立Unity进程回读四Mesh误差≤5.97e-8。只有`playmode_06.log`对应最终正确网格，之前实验图不能当作最终验收。
- 新VRM由Unity生产快照生成：90498面、15个表情组、37组Spring、3碰撞球，完整Tangent/UV/Normal。独立GLB解析/蒙皮回读通过，最大位置误差1.69µm；MToon与Unity自定义Shader/物理的降级项明确记录。
- 独立工作树因根`scripts/`忽略规则缺SDK和角色源码，已补入本机忽略依赖用于构建，没有更改主项目；误带入其他并行功能的测试源已隔离，不纳入提交。实际游戏验证使用独立测试存档名称，测试后恢复工作树产品名。
- 尚待完成：转头/下蹲压力、组合表情中间权重视觉覆盖、Android ARM64/IL2CPP构建、最终数值/审核板/回退说明与分支提交。当前ADB设备列表为空，不得声称真机性能已测。详见`ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v002/IMPLEMENTATION.md`最新章节。

## Phase Lengyue Quality Delivery：候选交付与执行监控（2026-10-11）

- 后续覆盖已完成：补充PlayMode 2,100帧，18类表情/嘴型/眨眼/注视×3强度×新旧，共108项；头部实测35.9825°、髋部高度范围180mm。12组压力/注视采样头发与脸/眼交叉0，继承眼沿接缝差在0.5mm内；强度中点误差最大0.0003221，补充验收通过。
- 修复原先注视入口不生效的问题：生产Animator没有Humanoid Avatar，改用Generic Animator骨骼曲线与二维BlendTree，保留现有动画接口。水平注视限制±8°、垂直±6°，避免薄片虹膜在大角度被眼白过度遮挡。
- `FacialPoseRequest`加入Blink/Gaze，由共享动作协调器Face通道持有和清理。实际游戏回归最终660帧、21动作全部Succeeded、0错误。
- 最终Android ARM64/IL2CPP非Development APK已成功构建，87.81秒、0错误；113,898,264 bytes，独立包名`com.matesoul.quality.v002`。Unity增量包10,636,106 bytes；依赖既有MateSoul工程，旧场景不替换。
- 完整Blender源、VRM、APK、Unity增量包、审核板、数值报告及迁移/回退表位于`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v002/`，入口`README.md`，最终校验`reports/final_acceptance.json`与`delivery_manifest.json`。
- 原生产目录保留，新增资源和代码在`feature/lengyue-quality-upgrade`独立工作树；本轮测试对3个编辑器指示器材质的副作用和临时产品名已恢复，旧工作树文件无diff。
- 用户指出长命令卡死后，加入5秒心跳、截止时间、日志停滞/异常监测、TERM/KILL后确认退出。直接启动Unity发生bee信号量错误，改用`--via-open`后成功；最终游戏、数学验证和APK监督均确认退出。已清理984个临时图/采样/备份，约413MB，并清理73个启动日志/临时监控文件。
- **交付边界：** 严格原始地标/Fréchet阈值仍有已披露超限，继承眼沿不是零相交，地标只是几何代理，网络AI Payload/实时音素未接入，无连接设备所以PICO性能未测。不得将可回退候选交付写成严格全项验收通过。
- 已提交`81901350c1d647110a1522d107ab174104dd4d6b`至本地`feature/lengyue-quality-upgrade`，278个文件全部为本任务新增，工作树干净。171个原生产文件及冻结评审包最终哈希核对全部未变。

## Phase Lengyue Visual v003：视觉目标纠正与参考发型重建（2026-10-11，进行中）

- 用户否定v002的可见变化，明确新要求：鬼瓦轮发型参数级完整复刻并贴合冷月头型；妆容/服装全面升级，面部与肢体提高面数。已授权“严格按照你的计划，完成整个升级任务，中途不得停止”，不再逐阶段等确认。v002只能视为技术底座，不能视为美术达标。
- 新分支`feature/lengyue-visual-v003`，继续使用隔离工作树；实施真源`ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v003/IMPLEMENTATION.md`。主分支并行修改不动。
- 已从参考提取57个独立发束壳体（含9刘海）+后发底层，经PCA截面、64点中心线、独立Sweep和冷月头型适配生成新头发：13,870顶点、23,895面、独立UV/4张2K贴图/新57×5链骨架。
- 独立重建IoU约.997；适配后显式撤销全局XYZ登记变换后的四视图IoU为.996657/.971702/.980975/.996656；截面mean0.67%/P951.98%，中心线mean Chamfer1.40mm，遮挡顺序一致97.79%。头型登记额外Z缩放20%及局部内层修正量明确披露；径向P95约7.99%，不能声称原7%阈值通过。
- 独立双向三角检查对旧脸（含头皮/耳）、眼、眉均0交叉；新增帽壳朝外法线和局部不足三角传播细分，修正了露肤和耳部跨面穿插。
- Unity新头发Prefab/渲染已完成，但脸、眼、妆容、身体、服装仍待制作；新脸与真实动态回归、当前游戏集成、最终构建交付仍须继续。不得在该头发里程碑结束任务。

## Phase Lengyue Visual v003：面部身体和服装静态升级（2026-10-11，继续执行）

- 新Face16796面、Eyes2048面、Body28400面、Neck496面；原Face2552面/Body9060面确实增密，原面部顶点重塑约1.28mm，原15个Morph及骨绑定保留，增加8个组合Morph。身体重做5图表UV、关节/手指细化和≤.7mm柔化。
- 新服装72424面，以v002粗底进行裙/袖造型细化，增加双层弧形领片、银月牙胸针、腰链/垂链。新7图表UV、18张全角色贴图包含紫灰眼妆/冷粉腮红/玫紫唇妆、虹膜纤维、银紫滚边、裙摆藤蔓刺绣及凸绣法线。
- 新Shader增加独立Face SDF/Control和身体软阴影；修正首轮色差遮罩造成的面部白纹、错误唇部位置。实际Jaw/Smile Morph定位嘴部Y≈1.4879，眼眶边界保持原位，不能再用粗略猜测坐标。
- 六核心网格合计143943面，退化面0、权重/骨/切线契约通过；45项原表情同姿态比较新增翻面0，新头发与新Face/Eyes双向交叉0。头发额外清除116个零面积端帽面后为23779面，拟合指标复测结论不变。眼白/虹膜继承交线几何误差接近浮点精度，不是严格零相交。
- Unity完整导入与原版/v002/v003三方、三灯光、头部四向/全身/服装特写采集成功并确认退出；四张静态合并对照板在`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v003/review/`。脸、衣服已有可见变化，当前还不能称动态或设备验收通过。
- **必须继续**：新表情控制器/材质head轴更新、PlayMode表情/转头/布料/换装、完整DCC/VRM、当前主项目可访问入口、APK/Unity包、设备可用性检查、最终文档/清理/提交。主工程与feature工作树尚未导入本轮新资源，不能让用户误以为已替换游戏角色。

## Phase Lengyue Visual v003：运行接入与交换资产（2026-10-11，构建交付继续中）

- 已添加独立`MateSoul.CharacterVisual`运行模块。`VisualFaceControl`只提交五层Generic Animator参数，头部最终世界轴通过MaterialPropertyBlock更新Face SDF；`VisualExpressionRegistration`在共享协调器注册Face/Gaze通道，仍不宣称网络AI情绪/音素自动接入。
- v003实际PlayMode完成2850帧、21类输入×0/50/100、8次整套换装，运行错误0。头部±35°、髋部下移180mm压力有效；15组实际BakeMesh检查新头发与脸/眼双向交叉全部0。眼睛在±8°/±6°注视下正面虹膜可见面积约87.2%–88.8%，保留原薄片眼沿结构。
- 第一轮动态发现发根摆动使贴合头皮的发片穿插；已固定每链前三关节，只让末两关节在1.2°上限内模拟，保持57链/286骨、114动态关节和唯一Solver。第二轮同夹具通过，不能再使用第一轮错误动态图。
- 默认可见总三角面由Unity快照确认为143987（核心143943+眉/下牙44）。新完整Blender源包含403绑定骨、57可编辑中心线、18新贴图和全部Morph；独立重开/求值误差≤0.415µm。Blender5.2新ShapeKey默认weight=1，已显式清零再保存。
- VRM导出143987面、15表情组、57末梢Spring组、2碰撞球；独立解析蒙皮误差≤1.613µm。面部SDF、唇湿润、布料与Unity物理限幅等无法无损表达的项已列入导出报告。
- feature隔离分支游戏实测21动作全部Succeeded、错误0；当前主工程已新增`QualityUpgrade/v003`的角色、A/B场景和菜单入口。另建`.artifacts/lengyue-visual-validation`验证主分支当前未提交功能的兼容性，不覆盖它们。
- 当前ADB无连接设备，PICO性能仍未测。当前主工程兼容性回归、APK/Unity包、最终清理/验收报告与分支提交仍在执行，不能把本节写成整个任务已完成。

## Phase Lengyue Visual v003：完整本地交付（2026-10-11）

- 头发、妆容、面部/肢体拓扑与默认裙装美术升级完成；默认可见143987面。新57束参数发型适配冷月头型，18张新贴图、403绑定骨/57可编辑曲线的完整Blender源和VRM均独立回读通过。
- 本地交付入口：`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Appearance/QualityUpgrade_20261010/v003/README.md`。主工程可直接打开`MateSoul/Assets/Characters/SapphiArtCustom/Lengyue/QualityUpgrade/v003/Scenes/MateSoul_Visual_v003.unity`或同目录`Lengyue_Visual_AB_v003.unity`；旧游戏场景未替换。
- 2850帧/63强度样本/8次换装、15组头发与脸眼双向交叉检查通过；当前游戏最终660帧、21动作全部Succeeded、0错误。30帧实时预热后才拍正式ready图，另记录首回调截图，避免将Editor渲染尚未稳定的一帧当作常态。
- 发现当前宿主4处未受开发开关保护的临时UI/网关HTTP探针，会在HTTP禁用时抛异常。仅在隔离构建副本删除这些探针，HTTP保持0后回归通过；主工程并行代码未覆盖，因此主工程原探针问题仍须其所属任务清理。可复现差异在`v003/scripts/build_clone_ui.patch`。
- Android ARM64/IL2CPP非Development APK及Unity增量资源包已构建，包名`com.matesoul.visual.v003`。签名、ZIP完整性、native库、发布元数据不含临时HTTP端点和资源包逐资产匹配均验证；精确大小/时间/警告数见`reports/android_build.json`，SHA见`delivery_manifest.json`。
- `reports/final_integrity.json`核对603项原生产、参考、v001/v002冻结交付及本轮输入，全部未变。交付清理原始截图、BakeMesh、启动日志与Blender备份，仅保留核心资产、数值报告和合并审核板。
- 分支`feature/lengyue-visual-v003`保留8190135基点的游戏派生资源，主工程使用当前宿主派生资源；核心外观相同。只提交v003专属资源、脚本、参数、报告、审核板及文档更新摘录；大型blend/VRM/APK/Unity包与中性快照按既有惯例本地交付，未推送远端。
- **边界保留：** 头型深度额外×1.20，径向P95为7.986%未过旧7%；继承眼沿非零相交，袖代理最小间距-1.161mm，头发1.2°限幅。无连接PICO/模拟器，设备帧率/显存/温度未测；不得称全原始阈值或真机认证通过。网络AI表情解析/实时音素和睡衣美术重做不在本轮交付内。
- 最终本地提交`141b65a3fabd46c230b47d01455882a7d9427b4c`，294个本任务新增文件，feature工作树干净。提交钩子的提示性硬编码扫描因2秒时限停止，不能声称该扫描通过；提交本身成功。代码/文档格式检查及前述Unity编译、运行、包体验证已完成。

## Phase 通用物品交互：开发准备评审（2026-10-15）

### 一、完成内容

- 基于当前 Unity 工程核对了动作协调器、物品定义和注册、XRI 抓取、公共物理基座、
  保存读档、坐卧与保龄专用动作、生产 Animator 和现有自动化测试。
- 新增
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_通用物品交互开发准备方案.md`。
- 方案给出物品权威控制状态机、交互 Action 阶段、原子预约结构、物理快照与回滚、
  动画/IK/Root Warp 边界、分类吸附参数、异常矩阵、性能策略和 P0/P1/P2 计划。

### 二、关键结论

- 整体方向可行，但当前 `ItemReservation` 不足以作为正式 NPC 取物锁。
- `ItemRegistry` 应升级为唯一物品控制事实源，玩家抓取、使用、删除、破碎和 NPC 行为
  必须查询同一个 InteractionID 状态。
- P0 先完成三类小物件、两类基础动作和安全回滚闭环；刀具、双手、大型物品、复杂移动
  支撑和多 NPC 协作后移。
- 吸附只作为接触窗口内最后兜底，必须使用物品形体 Sweep 与旋转占位检查，并记录
  Natural/IK/Warp/Snap 比率反向治理动画和站位质量。

### 三、验证与边界

- 已执行文档结构、关键需求覆盖和 `git diff --check` 检查。
- 本阶段未修改或编译运行时代码，未制作交互动画，未执行 Unity 自动化和 PICO 真机验证。
- 方案参数均标记为现有工程值、已确定规则或待校准设计初值；吸附距离和性能频率不作为
  已验证行业标准。

### 四、下一步

- 评审确认后从 P0 的 ItemControlState、原子预约和所有玩家操作入口统一门禁开始实施。
- 状态与回滚测试通过后，再接入 Profile/锚点、两类动画、IK 和安全吸附。

### 五、多物品 Action 需求补充

- 复核 MateFairy 的 `ActionRegistry`、`InteractionActionSystem`、足球、音响和小黄鸭
  Controller，确认其注册式 intent 和持续 Action 实例可作为背景参考，但全局锁、双注册表、
  自由参数、无物品预约及专用恢复代码不能迁移。
- 评审稿新增多物品 Action 编排：ActionRole/RoleBinding、ActionPlan、公共 ActionStep、
  ActionRunID、原子预约 Bundle、工作台 Staged 状态、手容量和逆序补偿。
- 完整展开 `cut-fruit`：苹果、刀、盘子、餐桌四角色，多趟搬运，桌面工作槽，左右手协同，
  切割提交点和苹果块结果事务。
- 缺餐桌等前置条件在准入阶段返回结构化 Blocked 结果，只弹出对应系统消息；
  不打开物品目录、不保留 pending Action，也不创建锁定 Action。
- 实施阶段调整为 P0 用 `stage-fruit` 验证多物品事务，P1 再交付正式 `cut-fruit`。
- 本次仍只更新开发准备文档，未修改 Unity 运行时代码、Animator 或物品资产。

## Phase Lavender Chef Knife 工作汇报：金属厨师刀建模（2026-10-15）

### 一、工作完成情况

- 根据三视图完成厨师刀建模。参考图未标注尺寸，采用常见 8 英寸厨师刀比例：
  总长 330 mm、刀刃 200 mm、最大刀宽 50 mm、握柄厚 24 mm。
- 刀身不是平面挤出，而是从 3 mm 刀脊连续收薄至 0.22 mm 刃口的闭合楔形网格。
- 主刀身与抛光刃口位于同一网格的不同材质区域；刀柄为深棕色圆润超椭圆握柄，
  另包含伸入握柄内部的独立金属刀茎。
- 已交付 Blender、GLB、FBX、五张 1024×1024 渲染图、三视图总览、README、
  参数化构建脚本和结构化验证报告。

### 二、验证结果

- 实测包络为 49.994×24×330 mm，满足 50×24×330 mm 目标容差。
- 3 个闭合网格、3,684 顶点、7,356 三角面；边界边与非流形边均为 0。
- GLB 主刀身 `metallicFactor=0.97`、粗糙度 0.22；抛光刃口
  `metallicFactor=1.0`、粗糙度 0.09。
- GLB/FBX 回读尺寸一致，`Knife_MetalBlade` 节点保留，GLB 无外部图片 URI。
- GLB SHA-256：
  `4e904f4ab70269703b6a9141fdf2f0c5e16c7ec6d49252fc4abe2d6283e94315`。
- FBX SHA-256：
  `2ef04728a86b71ce8d1e3f8815ab8dadd335f50a88b7bd55f7cea6f2d57e3244`。

### 三、问题与边界

- glTF 规范中 `metallicFactor=1` 可省略字段；首轮验证器把省略错误解释为 0，
  导致抛光刃口误报非金属。已按 glTF 默认值修正，模型材质无需回退。
- 当前交付仅为模型资产，尚未建立 Unity Prefab、抓取锚点、碰撞体或切水果 Action。
- 后续运行时不得使用 7.3k 三角面的展示网格直接参与动态碰撞；应使用简化刀身盒体与
  握柄胶囊/凸包组合，并由物品交互系统统一管理持有和危险动作状态。

## Phase Lavender Chef Knife V002 工作汇报：高精重制（2026-10-15）

### 一、重制内容

- 用户指出 V001 低模且不够精致，本轮原位替换 Blender、GLB、FBX 和全部渲染，
  V001 只保留在历史记录中，不再作为有效交付。
- 刀身采样由 72×20 提升至 128×36，并增加轻微凸磨曲面；刀身三角面由 6,048
  提升至 18,944。
- 刀柄由 40 边、9 个粗轮廓环重建为 64 边、约 78 个 Catmull-Rom 纵向环；
  刀柄三角面由 720 提升至 9,984，顶部、侧面和握柄收腰不再出现明显折面。
- 新增 3 张 512×512 PBR 纹理：拉丝钢底色、方向性粗糙度和深棕握柄底色，
  全部内嵌 GLB，无外部图片 URI。
- 抛光刃带占比由 24% 收窄至 16%，避免高光区域过宽。

### 二、验证结果

- 最终 3 个闭合网格、14,764 顶点、29,516 三角面；边界边与非流形边均为 0。
- 实测包络 49.9997×24×330 mm，GLB/FBX 回读一致。
- 主刀身 Metallic 0.97，抛光刃口 Metallic 1.0；GLB 内嵌图片数量为 3。
- GLB SHA-256：
  `e31caf974fbe35ed5dd08d9f206cb7ece141242fff7f2f920c032f47deb259f2`。
- FBX SHA-256：
  `fe0754b35ce3e8fb91bd573ff0cf32ad748f2734d08131907d4430d74e58eb6c`。

### 三、踩坑

- Blender 5.2 对新建 Image 在写入像素后再修改 `colorspace_settings.name` 会清空像素
  缓冲，首轮 V002 因此生成全黑纹理。最终顺序改为先设置颜色空间，再写像素、
  `image.update()` 并保存。
- 运行时碰撞体仍须使用简化几何；高精重制不改变刀具作为 P1 高风险物品的交互边界。

## Phase Lavender Gingham Dining Table 工作汇报：高精餐桌与虚拟目录接入（2026-10-15）

### 一、资产开发

- 根据三视图将旧的 600×400 mm 小桌升级为 1200×700×750 mm 标准餐桌。
- 木桌包含圆角桌板、四条围板和四根外撇锥形桌腿；桌布使用烘焙静态高密网格，
  包含桌沿弯折、垂边褶皱、波浪下摆、角部聚拢和顶面轻微松弛。
- 桌布为 3 mm 闭合实体，单独包含 25,026 顶点和 50,048 三角面；完整模型共
  26,898 顶点、53,756 三角面，边界边和非流形边均为 0。
- 新增 1024² 格纹 BaseColor、512² 织物 Normal 与 512² Roughness；三张纹理均内嵌
  GLB，Unity URP 材质保留 Base Map 和 Normal Map。
- 最终视觉包络为 1281.0323×771.1353×754.6436 mm。

### 二、虚拟物品接入

- 未新增重复目录项，而是保持稳定 ID `table` 和原 `Table.asset` GUID，显示名更新为
  “餐桌”，旧存档和 `MateSoul.unity` 场景引用继续有效。
- `GameSceneBuilder.BuildDiningTable()` 独立校验桌布视觉包络；占地、组合碰撞体与
  NavMeshObstacle 只使用 1200×700×750 mm 木桌主体。
- `ItemDefinition` 占地更新为 1.2×0.7 m，放置高度 0.7547 m，质量由 8 kg 更新为
  18 kg。桌布不创建 Cloth、Rigidbody 或 MeshCollider。
- `Table.prefab` 保留一个顶面 BoxCollider、四个桌腿 BoxCollider、唯一根 Rigidbody
  和 NavMeshObstacle。

### 三、验证

- Blender 尺寸、闭合流形、高密桌布、GLB 三纹理内嵌及 GLB/FBX 回读断言全部通过。
- GLB SHA-256：
  `97c6ca3f53bc101915e0e429d21b13b149e3eaa6b102d098ccde9db69e4aa2be`。
- FBX SHA-256：
  `4eb4cb0f6ba3f089127ff630f76ff0c63402cba941e86781df9dbaddabc5efcc`。
- Unity 2022.3.38f1 场景重建成功，退出码 0。
- 餐桌专项 PlayMode 测试 3/3 通过：目录放置与聚焦、桌面支撑恢复、移除支撑后的物理
  恢复。
- 验证报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/dining_table_virtual_item_20261015.json`。

### 四、边界与踩坑

- TRAE 受限 shell 直接启动 Unity 仍会导致 `bee_backend` semaphore error 15；最终通过
  系统 Terminal/LaunchServices 完成真实编译、场景构建和测试。
- 本次未构建 APK，也未在 PICO 模拟器或真机检查桌布纹理采样和 53.8k 三角面的实际
  GPU 开销。

## Phase Art Dining Chair V002 工作汇报：参考图木质餐椅重制（2026-10-15）

### 一、资产开发

- 新资产隔离在
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/DiningChairV002/`，
  未覆盖旧版 `Props/Chair/`、Unity `Chair.asset` 或现有 Prefab。
- 参考图无尺寸标注，按成人餐椅比例确定为 450×520×850 mm，实际座面顶高约
  476 mm；Blender 以米存储，原点位于地面中心。
- 完成前腿轻微外撇、后腿/靠背柱一体弯曲、圆木柱端头、上下双弧形靠背板、木质
  四边座框和座板。最终为 16 个独立闭合网格、7,782 顶点、15,504 三角面。
- 坐垫使用封闭超椭球软包轮廓，而非圆角方块；包含 450×430×56 mm 泡棉鼓包、
  2.2 mm 独立包边绳及深色底部防尘布。
- 浅暖橡木和浅紫灰坐垫分别使用 BaseColor/Normal/Roughness 三张 1024² 贴图。
  木纹保留顺纹、细孔和局部色差；织物加入经纬法线、高粗糙度与 Sheen。

### 二、交付与验证

- 交付 Blender 源文件、GLB、FBX、6 张 PBR 贴图、参考图、六视图预览、纹理报告、
  模型报告和 SHA-256 清单。
- 源网格边界边 0、非流形边 0；GLB 回读 450×520×850 mm，FBX 回读
  450×520.0002×850.0001 mm。
- GLB 含 16 个网格、3 个材质并内嵌全部 6 张 PBR 贴图，无外部图片 URI；木材与
  织物均确认保留 Normal 和 Roughness 纹理。
- `.blend` 复开验证包含 16 个生产网格和 6 张已打包贴图。导出回读产生的 12 份
  无引用贴图数据块已在最终保存前清理。
- 本阶段没有修改 Unity 目录、游戏逻辑或 APK。后续接入时继续复用公共物理基座，
  使用座框/腿柱/靠背组合简化碰撞体，不能把 15.5k 三角面的渲染网格直接设为动态
  MeshCollider。

## Phase Art Dining Chair V003 工作汇报：靠背比例与坐垫颜色修正（2026-10-15）

- 根据用户反馈保留 V002 作为回退，在
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/DiningChairV003/`
  建立隔离修正版；V003 是当前建议使用版本。
- 宽度 450 mm、深度 520 mm 和座面顶高约 475.7 mm 均保持不变，仅延长座面以上的
  后腿/靠背柱并上移两块弧形背板。整体高度由 850 mm 调整为 920 mm，靠背有效高度
  增加约 70 mm，比例更接近参考三视图。
- 坐垫由接近白色的淡紫灰改为明确可见的浅紫色。保留 1024² BaseColor、Normal、
  Roughness、织物 Sheen、超椭球泡棉轮廓和 2.2 mm 独立包边绳。
- 16 个生产网格、7,782 顶点和 15,504 三角面未增加；边界边与非流形边继续为 0。
- GLB 回读 450×520×919.9999 mm，FBX 回读 450×520.0001×919.9997 mm；
  6 张木材/织物 PBR 贴图全部内嵌 GLB，无外部图片 URI。
- 本次仍未修改 Unity `Chair.asset`、Prefab 或场景；V003 仅作为修正后的独立美术资产
  交付。

## Phase Art Cooking Set V001 工作汇报：平底锅、锅铲与生熟牛排（2026-10-15）

### 一、资产开发

- 新资产位于
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/CookingSetV001/`，
  根据任务中的两张三视图制作平底锅、锅铲、生牛排和熟牛排四个独立公制资产。
- 平底锅外径 280 mm，含手柄总包络约 553.2×280×102.6 mm。锅体包含封闭锅底、
  有厚度的外壁/锅沿、深色不粘内层、弯折金属连接件、套环、两枚内侧铆钉和带真实
  挂孔的黑色耐热握柄。
- 锅铲总长约 370 mm，黑色耐热铲头包含三个真实贯通槽孔；连接杆和主手柄为拉丝
  不锈钢，握柄末端同样包含实际贯通挂孔。
- 生熟牛排均为 180×135×28 mm，并由完全相同的 578 顶点、1,152 三角面构成。
  两份几何 SHA-256 均为
  `e6b4d38b10d51612ff0c0efbfe7bf670719522603a0dba395c8e1a5152d15156`，
  最大顶点位置差为 0 mm。
- 生熟状态只替换 PBR 贴图：生牛排使用深红肌理、随机分叉脂肪纹和湿润粗糙度；
  熟牛排使用深褐焦化表面、平行煎痕和收缩脂肪纹，不通过煎痕几何改变轮廓。

### 二、验证与交付

- 完整场景包含 12 个生产网格、5,666 顶点和 11,302 三角面；边界边与非流形边均
  为 0。
- 四组 GLB/FBX 共 8 个导出文件全部重新导入并通过尺寸回读；四个 GLB 均内嵌自身
  使用的 PBR 贴图且没有外部图片 URI。
- 自动断言覆盖不锈钢金属度、锅内不粘层 Normal/Roughness、锅铲真实槽孔、生熟牛排
  尺寸与几何哈希一致性。
- 交付 Blender 源文件、四组 GLB/FBX、12 张 PBR 贴图、9 张预览、模型/纹理/交付
  JSON 报告和 SHA-256 清单。
- 本阶段未修改 Unity 物品目录、Prefab、场景或 APK；后续烹饪玩法中生熟切换应只
  替换状态/材质，不重新缩放牛排或重建碰撞体。

## Phase Natural Oak Dining Chair 工作汇报：参考图餐椅建模与 Unity 接入（2026-10-15）

### 一、资产开发

- 按本轮正视、侧视、背视参考图，在
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Chair/` 原位建立当前生产餐椅；
  名义尺寸为 480×505×820 mm，座面顶高 475 mm。
- 木构包含外撇前腿、连续弯曲后腿/靠背柱、横向弧形靠背、四边座框及四枚深色木塞；
  最终视觉包络为 481.9661×510.8422×819.9502 mm。
- 坐垫使用 9,408 三角面的闭合高密网格表现泡棉鼓包、边缘压缩和圆润四角，并使用
  独立 3,840 三角面包边绳表现缝制结构。
- 天然橡木与织物分别提供 BaseColor、Normal、Roughness，共 6 张确定性生成的 PBR
  贴图；GLB 全部内嵌，无外部图片 URI。
- 完整模型为 16 个闭合网格、13,660 顶点、27,260 三角面；边界边和非流形边均为 0。

### 二、Unity 接入

- `GameSceneBuilder` 新增 `BuildDiningChair()`，Unity 引用切换到
  `NaturalOakDiningChair_480x505x820mm.fbx`，并在构建时校验视觉包络。
- 保留稳定 ID `chair`、原 `Chair.asset` 与 `Chair.prefab` GUID，显示名更新为“餐椅”；
  占地为 0.48×0.505 m，放置高度 0.82 m，质量为 5.5 kg。
- 当前产品策略继续通过 `ConfigureItemPolicy(catalog, "chair", 0, visible: false)`
  隐藏餐椅，因此本轮是生产资产升级，不改变目录可见性。
- Prefab 使用 1 个座框、4 个椅腿和 1 个靠背 BoxCollider，根节点只有 1 个 Rigidbody
  和 1 个 NavMeshObstacle；高精显示网格不使用动态 MeshCollider。
- 木材和织物 Base Map、Normal Map 已接入 URP Lit；新增法线导入配置，确保两张法线
  贴图按 `NormalMap` 且关闭 sRGB 导入。

### 三、交付与验证

- 已交付 Blender 源文件、GLB、FBX、6 张 PBR 贴图、五张独立渲染、三视图总览、
  参数化构建脚本、README 和结构化模型报告。
- GLB SHA-256：
  `016857032790473ddfc7aa3c945d019ee1cde50c4c29731bea4d558d67a36331`。
- FBX SHA-256：
  `53bff6f26704d19c2f19cde16181b31c99922474823e9802483a9bea7199dd48`。
- Unity 2022.3.38f1 场景重建成功，退出码 0；目录放置/聚焦、支撑恢复和移除支撑后的
  物理恢复专项测试 3/3 通过。
- 验证报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/dining_chair_virtual_item_20261015.json`。

### 四、问题与边界

- 首轮后立柱上端穿过靠背正面，已将顶部中心向后调整并缩小顶端截面；木塞端盖由
  NGON 改为三角扇，消除 FBX 切线导出警告。
- 本轮未构建 Android APK，也未在 PICO 真机验证 27.3k 三角面的实际 GPU 成本。
- 旧 `LavenderChibiChair_360x380x680mm` 文件仅保留历史兼容，不再由 Unity 构建器引用。

## Phase Virtual Item Inventory 工作汇报：限量库存、回收、时钟与餐桌放置（2026-10-15）

### 一、功能完成情况

- `ItemDefinition` 新增库存上限、每日补货、固定放置和投影预览内容策略；
  `GameSession` 成为剩余数量与场景实例的唯一事实源。
- 盘子、碗、勺子、筷子和马克杯上限设为 2；苹果每日本地时间 0:00 补充 5 个；
  扑克牌、收音机、玩偶、飞镖盘、电视机、刀、时钟和餐桌上限设为 1。
- 陪伴灯和餐椅从目录隐藏且库存为 0；原小桌已由新餐桌取代。刀显示名统一为“刀”，
  接入 29,516 三角面的高精厨师刀 FBX。
- “删除当前物品”改为“回收当前物品”。回收恢复对应库存；破碎物品不返还。筷子
  左右两根共享一个库存单元，回收任一根会整双回收并恢复 1 份。
- 虚拟物品按钮右下角新增剩余数量；库存为 0 时按钮不可用。
- 餐桌使用 1.2×0.7 m 地面投影，空闲时为绿色、被占用时为红色；再次点击餐桌确认
  当前绿色位置。餐桌落地后进入固定物理状态，不能被玩家抓取或移动。
- 新增 `RealTimeClock`，只旋转模型内独立的时针/分针枢轴，并按设备本地现实时间
  每 0.25 秒更新。

### 二、存档与兼容

- `GameSaveData` 新增库存快照、每日库存日期、库存所有者和库存单元 ID。
- 旧存档缺少新字段时，会按物品定义和已放置实例反推剩余库存；组合筷子通过共享
  单元 ID 防止按两根重复扣减或返还。
- 苹果日期使用 `yyyy-MM-dd` 本地日期，不依赖在线服务；跨过本地午夜后的首次
  `Tick` 或库存查询会完成补货并立即持久化。

### 三、验证情况

- Unity 2022.3.38f1 Roslyn 依赖顺序编译通过：
  `MateSoul.Gameplay -> MateSoul.Runtime -> MateSoul.Editor -> MateSoul.Tests.Engine`。
- 手动 Bee 全量构建与 Burst IL 后处理通过：`906` 节点评估，
  `Tundra build success`。
- 场景和内容重建已生成 `Clock.asset/prefab`、高精刀 Prefab、库存策略字段及场景
  引用；静态差异检查无本轮新增空白错误。
- 新增库存持久化、回收、每日补货、组合筷子和时钟角度回归用例。受 TRAE 沙箱内
  Unity `bee_backend --ipc` 的 `mono_os_sem_post error 15` 限制，PlayMode Test Runner
  未能在本会话实际执行；该问题不影响非 IPC Bee 全量编译结果。

### 四、后续建议

- 下一步在 PICO 真机验证餐桌红绿投影的可读性、1.8 m 默认距离、固定碰撞和回收手感。
- 在系统 Terminal 或正常 Unity Editor 中运行 `GameRuntimeTests`，补齐 PlayMode
  执行证据；再验证设备本地时区切换和跨午夜补货。

## Phase Tableware Size Variants 工作汇报：碗盘中大型号与虚拟物品栏接入（2026-10-15）

### 一、型号资产

- 保留现有小号碗 `138×138×70 mm` 与小号盘 `200×200×25 mm`，稳定 ID `bowl`、
  `plate` 不变，显示名更新为“小号碗”“小号盘子”。
- 新增中号碗 `180×180×91.304 mm`、大号碗 `220×220×111.594 mm`，缩放倍率分别
  为 `1.30434783` 和 `1.59420290`。
- 新增中号盘 `250×250×31.25 mm`、大号盘 `300×300×37.5 mm`，缩放倍率分别为
  `1.25` 和 `1.50`。
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/build_tableware_size_variants.py`
  直接按倍率放大生产网格顶点，输出独立 Blend/GLB/FBX；拓扑、UV、材质槽和贴图不变，
  不依赖 Unity Transform 临时缩放。
- 四个新型号 GLB 均为单网格、3 材质、4 张内嵌图片且无外部 URI；碗保持 8,704
  三角面，盘子保持 5,376 三角面，边界边和非流形边均为 0。
- 新增小/中/大真实比例对比渲染：
  `Bowl/renders/bowl_size_comparison.png` 和
  `Plate/renders/plate_size_comparison.png`。

### 二、Unity 虚拟物品栏

- 新增稳定 ID：`bowl-medium`、`bowl-large`、`plate-medium`、`plate-large`；四项均
  `catalogVisible=true`、可放置到表面、库存上限为 2。
- 质量按等比体积增长后取两位有效值：中/大碗 0.69/1.26 kg，中/大盘
  0.82/1.42 kg。
- 新型号复用既有 `Bowl`/`Plate` URP 材质资产，没有生成平行材质目录。
- `BuildCatalogProp()` 新增可选 `materialGroup` 和 `blocksNavigation` 参数；25/30 cm
  盘子明确不创建 NavMeshObstacle，避免餐桌上的餐具切割角色导航网格。
- 中/大盘轻度 4 块、重度 22 块的破碎 FBX 同步按型号倍率生成，并分别建立
  `PlateMedium*Fracture.prefab`、`PlateLarge*Fracture.prefab`，打碎后尺寸不会回退
  到小号。
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Scenes/MateSoul.unity`
  已写入四个新 ItemDefinition GUID，虚拟物品栏可直接枚举。

### 三、验证

- Blender 5.2.1 LTS 对四个 Blend、GLB、FBX 和四个破碎 FBX 完成回读尺寸验证；
  两份型号报告中全部 validation 字段为 `true`。
- 美术最终 FBX 与 Unity `PropsSource` 副本 SHA-256 逐一一致。
- Unity 2022.3.38f1 场景重建两轮均退出码 0，最终轮使用最终导出文件。
- 正式测试 2/2 通过：
  `ProductionCatalogContainsEveryStandaloneProp` 验证目录顺序、可见性、尺寸、质量、
  库存和破碎资源；`CatalogPlacementGrabAndFocusUseRegisteredDefinitions` 验证通用
  放置、抓取与聚焦链路。
- 结构化证据：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/tableware_size_variants_20261015.json`。

### 四、问题与边界

- Blender 5.2 的 Eevee 枚举为 `BLENDER_EEVEE`；首次对比渲染使用
  `BLENDER_EEVEE_NEXT` 失败，修正后完整重跑且未保留半成品报告。
- HUD 全量分页测试在 `-nographics` 下因项目既有 HTTP 字体回退触发
  `Insecure connection not allowed`，与本次目录数据无关；目录 Definition、场景引用
  和通用放置链路已有独立通过证据。
- 本轮未构建 Android APK，也未在 PICO 真机验证不同尺寸餐具的抓取手感。

## Phase Cooking Items 工作汇报：平底锅组合与无限生牛排（2026-10-16）

### 一、虚拟物品栏

- 新增目录项 `frying-pan`（平底锅），库存上限 1。点击放置会同时生成隐藏成员
  `frying-pan-body` 和 `spatula`，二者共享同一库存单元；回收任一成员会整组回收并
  恢复 1 个平底锅库存。
- 锅铲的组合姿态由构建器读取锅体中心、铲面中心和模型最低点自动计算，铲面位于锅内、
  手柄与锅柄同向轻微偏转，初始高度略高于锅沿后由物理系统自然落下。
- 新增目录项 `raw-steak`（生牛排），使用显式 `UnlimitedInventory` 策略，不写伪上限、
  放置不扣减、回收不增加，HUD 数量角标显示 `∞`。
- `UiFontAssetBuilder` 已把 `∞` 加入静态 Noto CJK SDF 字形集。

### 二、模型与物理

- 复用 `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/CookingSetV001/` 的已验证
  生产 FBX 和 PBR 贴图，复制到 Unity `PropsSource/CookingSet/`。
- 平底锅尺寸 553.192×102.617×280 mm，质量 1.3 kg；锅铲尺寸
  369.953×51.475×99.987 mm，质量 0.18 kg；生牛排尺寸 180×28×135 mm，质量
  0.35 kg。
- 平底锅由锅底、四段低矮锅沿和手柄共 6 个 BoxCollider 构成，锅腔保持开放，避免
  通用整体盒体让锅铲悬浮。
- URP 材质接入 BaseColor/Normal；拉丝不锈钢 Metallic 显式恢复为 1.0，避免 FBX
  导入后退化为灰色非金属材质。

### 三、验证

- Unity 2022.3.38f1 场景连续重建成功，最终日志包含
  `MATESOUL_SCENE_BUILT path=Assets/MateSoul/Scenes/MateSoul.unity`。
- Roslyn 编译和 Bee/Burst IL 后处理通过，Bee 全量构建为 906/906 节点成功。
- 专项正式测试 3/3 通过：
  `UnlimitedInventoryNeverDepletesOrPersistsAStockCounter`、
  `FryingPanPlacementSpawnsSpatulaOnTopAsOneInventoryUnit`、
  `ProductionCatalogContainsEveryStandaloneProp`。
- 结构化证据：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/cooking_items_20261016.json`。
- 全量 `GameRuntimeTests` 为 26/32 通过；6 项失败均是既有测试环境或旧断言问题，
  包括 HTTP 字体回退安全策略、旧存档 JSON 字段规范化和易碎资产前置数据，本次新增
  功能用例没有失败。

### 四、后续建议

- PICO 真机重点验证锅铲在锅内的稳定落位、组合回收手感和连续生成大量生牛排时的
  房间物理负载。
- 后续烹饪玩法应继续让 `raw-steak`/熟牛排共享几何与碰撞，只由单一业务状态切换
  材质或 Prefab，不新增并行 Transform 状态机。

## Phase Water Dispenser 工作汇报：高精饮水机与接水系统（2026-10-16）

### 一、美术资产

- 根据正视、侧视和俯视参考图建立落地式饮水机，现实尺寸为
  340×372×1400 mm；包含奶油白塑料机身、独立柜门和把手、深色接水腔、不锈钢
  出水嘴、滴水盘、侧面检修板、半透明 PET 水桶及独立桶内水体。
- 模型最终为 36 个闭合网格、10,906 顶点、21,684 三角面；边界边和非流形边均为 0。
- 材质覆盖塑料、透明 PET、水体、金属、橡胶、按钮、滴水盘和接水腔；奶油白塑料
  使用 BaseColor、Roughness、Normal 三张确定性生成的 PBR 贴图。
- 保留稳定交互节点 `WaterDispenser_Button_Pivot` 和
  `WaterDispenser_Nozzle_Outlet`，供 Unity 绑定按钮与出水口。
- 交付 Blender、GLB、FBX、五张 1024² 渲染图、参数化脚本、README 和模型报告；
  GLB 内嵌 3 张贴图，无外部图片 URI。

### 二、水系统设计与实现

- 没有使用逐滴 Rigidbody 或通用流体模拟。`WaterDispenserController` 只维护一根
  连续网格水柱，通过非分配 Raycast 确定容器或滴水盘终点。
- `WaterReceiver` 接入马克杯、三种碗和三种盘子，每个容器只有一个水面网格；
  水量以升记录，水面按容量比例抬升，容器倾斜超过 38° 时拒绝接水。
- 流量为 0.18 L/s，单次最长连续出水 8 s。容器已满或没有容器承接的水会进入固定
  6 个水迹对象池。
- 水迹以 0.36 s 的最小间隔复用，约 2.4 s 扩散、淡出并关闭，不产生持续增长的
  GameObject、Collider 或 Rigidbody。
- `WaterDispenserButton` 复用项目世界空间 UI/XR 指针模式；点击切换出水，按钮有
  5 mm 物理按压反馈、快速点击脉冲和出水状态发光。
- 饮水机本体物理由公共 `PhysicsBody` 持有；水系统不写饮水机或容器 Transform。

### 三、虚拟物品栏

- 新增稳定 ID `water-dispenser`，显示名“饮水机”，分类为“家具”。
- 目录可见、库存上限 1、质量 18 kg、占地 0.34×0.372 m、放置高度 1.4 m。
- 使用固定放置和地面投影确认；放置后不能被抓取移动，并通过
  0.34×1.4×0.372 m NavMeshObstacle 雕刻角色导航。
- Prefab 使用 4 个简化 BoxCollider 和唯一根 Rigidbody，不使用 21.3k 三角面的
  显示网格参与动态碰撞。

### 四、验证与问题记录

- Blender、GLB、FBX 回读尺寸、闭合流形、贴图内嵌和交互节点全部通过。
- Unity 2022.3.38f1 场景重建退出码 0，场景包含饮水机 Definition GUID。
- 初始正式测试 4/4 通过；按钮可见性与点击修复后，增强专项测试 3/3 通过，新增验证
  点击热区与按钮 Renderer 中心对齐、按下位移超过 3.5 mm、快速点击仍完整播放按压
  脉冲并回弹归位。
- 首轮 Unity 编译曾把 `InverseTransformPoint` 调在 GameObject 上，修正为
  `root.transform`；第二轮按钮 Pivot 位于根中心导致推导行程为零，改为按前表面方向
  固定 5 mm 行程。
- 用户反馈按钮不明显且无法点击后，模型增加 92×54 mm 深色底座，按钮本体扩大到
  76×44 mm 并明确前凸，水滴图标扩大为约 34 mm 高的高对比蓝色；Unity 热区从错误的
  模型原点改为 `WaterDispenser_DropButton` Renderer 中心，尺寸为 78×52 mm，并放在
  前表面外 8 mm，避免被机身 Collider 遮挡。
- 结构化证据：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/water_dispenser_virtual_item_20261016.json`。
- 本轮未构建 Android APK，也未在 PICO 真机验证透明水材质、射线按钮手感和持续出水
  的实际 GPU 成本。

## Phase Virtual Item Categories 工作汇报：六分类目录与筛选（2026-10-16）

### 一、功能完成情况

- `ItemDefinition` 新增持久化 `ItemCategory` 字段，固定为家具、电器、餐具、厨具、
  食品、玩具六类，并提供统一中文显示名。
- `GameHud` 的虚拟物品页新增六个分类按钮。切换分类时重置到第一页，只展示该分类
  物品；每类继续沿用每页 6 项、库存角标、上一页/下一页和放置逻辑。
- 分类是资产数据，不按 ID 或显示名在 HUD 中临时推断。`GameSceneBuilder`、
  `TelevisionBuilder`、`RadioBuilder` 和饮水机构建器生成的 Definition 都会持久化
  分类，场景重建不会丢失。
- 当前 32 个 Definition 均已写入分类，其中 26 个目录可见、6 个是隐藏组合部件或
  停用目录项。

### 二、归类结果

- 家具：餐桌、饮水机；隐藏餐椅同属家具。
- 电器：复古电视、复古收音机、时钟、遥控器；隐藏陪伴灯同属电器。
- 餐具：三种盘子、马克杯、三种碗、勺子、筷子；左右独立筷子同属餐具。
- 厨具：刀、平底锅；隐藏锅体和锅铲同属厨具。
- 食品：生牛排、纸杯蛋糕、曲奇、苹果。
- 玩具：扑克牌、飞镖盘、保龄球、保龄球瓶、冷月玩偶。

用户指定的边界已固定：刀为厨具、马克杯为餐具、电视机为电器。

### 三、编译与测试

- Unity 2022.3.38f1 成功编译 `MateSoul.Runtime`、`MateSoul.Editor` 和
  `MateSoul.Tests.Engine`，场景生成后 32/32 Definition 均包含分类字段。
- 专项回归 2/2 通过：
  `BottomHudExpandsVerticalInteractionActivities` 覆盖六分类按钮、分类内分页和
  26 个可见物品全集；`ProductionCatalogContainsEveryStandaloneProp` 覆盖生产资产
  的精确分类映射。
- 首次直接在 Trae 沙箱中启动 Unity 时，Bee 因 macOS semaphore 限制退出；改由
  macOS LaunchServices 启动同一 Unity 版本后，编译和测试正常完成。

### 四、后续建议

- 新增虚拟物品时必须在构建器调用 `ItemDefinition.Configure(...)` 时显式传入分类，
  不得在 HUD 增加物品名判断。
- 尚未执行 Android APK 和 PICO 真机交互验证；真机侧重点检查六个分类按钮的射线
  命中面积、文字清晰度和快速切换手感。

## Phase Latest Android Consolidation 工作汇报：当前功能整包构建（2026-10-16）

- 确认此前默认启动包仍是 2026-10-10 构建，未包含 10 月 16 日完成的餐具尺寸变体、
  平底锅组合、生牛排、饮水机、六分类目录和最新字体资源。
- 使用当前主工程重新生成 `MateSoul.unity`，随后执行
  `GameSceneBuilder.BuildAndroidDevelopment()`；Unity Android ARM64/IL2CPP 构建成功，
  日志包含 `Build Finished, Result: Success` 和 `MATESOUL_BUILD_PASS`。
- 最新 APK 为 `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，大小
  `135,186,241` 字节，SHA-256
  `f641785746d0eec786c29368edaa1e5d413c1ceb5b6f91a55bbbeba6f798d4f4`。
- 启动脚本校验电视节目资源一致，检测到设备旧包 `aea65e...` 后完成覆盖安装；设备
  `base.apk` 与宿主哈希一致。
- 冷启动后应用保持前台，PID `3230`；设备截图已确认最新饮水机模型进入运行场景，
  HUD 正常显示，未出现 `level0` 损坏或原生启动崩溃。

## Phase Emulator Install Recovery 工作汇报：ADB 流式安装自动回退（2026-10-16）

- 根目录 Development APK 更新为 15:36 构建，SHA-256
  `4b224fe6734b774b6fd9347158b81f3b89eea3438e9f5628010290428cded232`。
- 复现启动失败发生在 `adb install -r -g` 的 `Performing Streamed Install` 阶段；
  模拟器已正常启动、`/data` 仍有 6.5 GB 可用空间，APK 的 PICO/VST/Spatial Anchor
  Manifest 与电视节目资源校验均通过。
- 重启项目内 ADB 服务后，采用 `adb push` 到 `/data/local/tmp`，再由设备执行
  `pm install -r -g`，安装成功。
- `start-pico-emulator.command` 新增自动回退：流式安装失败时重启 ADB，等待设备恢复，
  执行两阶段安装并清理设备临时 APK；原有安装后 SHA-256 校验继续生效。
- 默认脚本回归通过，应用冷启动后 PID `7944` 持续运行并保持前台，宿主与设备 APK
  哈希一致。

## Phase Cut Fruit Runtime 工作汇报：继续开发中（2026-10-16）

- 已重新扫描最新刀、餐桌、库存与盘碗尺寸变体；沿用 `feature/cut-fruit-action` 分支，
  本轮前代码与内容快照保存在 `.artifacts/cut-fruit-action/baseline-20261016/`。
- 已实施物品权威控制态、批量预约、幂等释放、受控物理与玩家操作门禁；新增 5 条预约
  业务不变量测试。编译基线通过，测试启动因主工程被另一 Unity 实例占用而未完成。
- 剩余：通用步骤、内容配置与动画、切水果事务、HUD 入口、隔离运行验证和 Android 构建。
  当前不构成可测试完整功能，需继续执行；详细进度见 `.artifacts/cut-fruit-action/IMPLEMENTATION.md`。

## Phase HUD Visibility 工作汇报：手柄与手势收起/唤出（2026-10-16）

### 一、输入分析与分配

- 手柄现有占用为：扳机负责 UI 点击及放置确认，握把负责虚拟物品抓取；应用未占用
  A/B/X/Y，但 Menu/Home 属于系统语义，不作为游戏切换键。
- 手势现有占用为：`pinchTouched` 负责选择、抓取和放置确认，
  `pointerActivated` 负责 UI 点击；`graspFirm` 尚未占用。
- UI 切换最终分配为右手 B 键，以及双手同时握拳保持 0.5 秒。两种输入都必须释放后
  才能再次触发，收起和唤出共用同一切换动作。

### 二、功能完成情况

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs` 新增 HUD
  可见性状态和输入动作。游戏启动默认显示；隐藏后保留当前页面和业务状态，重新唤出
  时原样恢复。
- HUD 隐藏时同步禁用 `VirtualItemFocusController`，其 `OnDisable()` 会立即移除当前
  虚拟物品的粉色焦点材质；唤出后恢复焦点检测。
- 五个底部导航按钮压缩内部留白，在按钮栏最下方新增动态提示：
  “展示/隐藏UI：右手 B 键”或“展示/隐藏UI：双手握拳”。
- 新提示字符已加入 `AlwaysOnTopUi.InitialFontCharacters`；正常 Editor 重载或 Android
  构建执行 `UiFontAssetBuilder.Ensure()` 时会刷新静态 SDF 字库。

### 三、验证与限制

- Unity Roslyn 按依赖顺序编译 `Gameplay → Runtime → Engine Tests` 通过；并对最终
  `GameHud.cs`、`AlwaysOnTopUi.cs` 做隔离复编译，退出码 0。
- 本地回归断言覆盖三个输入绑定、提示文本不与按钮重叠、默认显示、隐藏/恢复，以及
  隐藏时关闭粉色焦点控制器。
- 正常 Terminal 环境已绕过 Trae 沙箱的 Bee 信号量限制，但正式 Test Runner 被并行
  开发中的未完成 `CutFruitAction`/`ReportItemAction` 引用阻断，未生成结果 XML；
  该阻断不来自 HUD 变更。
- 尚未执行 Android/PICO 真机验证。后续应重点检查 B 键边沿触发、双手握拳误触率和
  提示行在头显中的清晰度。

## Phase Cut Fruit Runtime 工作汇报：多物品切水果闭环（2026-10-16）

### 一、功能完成情况

- 新增统一的多物品 Action 执行链：候选解析、苹果/刀/盘子/餐桌批量预约、逐件取物、
  搬运、桌面编排、两次刀刃切割、四块苹果装盘和取消恢复均由
  `AvatarActionCoordinator` 单入口驱动。
- `ItemRegistry` 现在统一仲裁玩家与角色控制权；`PhysicsBody.Interaction.cs` 是预约期
  间物品位姿唯一写入者。缺少任一物品时只发送系统消息，不锁物品、不自动续跑。
- 新增 `ItemInteractionProfile` 内容配置、`AvatarItemRig` 和四组 Generic 动作：
  `FloorReach`、`TableReach`、`Carry`、`Cut`。刀柄和刀刃基于 33 cm 正式刀模型实测，
  餐桌工作槽基于 1.2×0.7×0.755 m 正式餐桌配置。
- 正式四分之一苹果模型已接入。完整苹果以原子事务替换为四块隐藏消耗品；切块不能补回
  完整苹果库存，避免“一份苹果切成四份库存”。
- “互动”页面新增“切水果”和“取消切水果”；动作阶段和最终结果使用现有系统通知。
  静态 UI 字库已加入“水”，不再产生 fallback 文本子网格。

### 二、关键问题与解决

- 初始地面动作按错误身体比例制作。运行时证据确认冷月单臂总长约 0.421 m，随后按实测
  比例重烘焙蹲姿、桌面伸手和切割姿势。
- 手持盘子距角色仅约 0.31 m，运动学碰撞把角色向反方向推出。携带点改为胸前
  `(0.21, 1.28, 0.43)`，保留真实碰撞而非关闭角色/物品碰撞。
- `Tick` 读取 `LateUpdate` 手部误差会得到上一帧数据；到位后增加一个动画求解帧再准入。
- 刀具归位改为先抬到安全高度、水平回到刀槽上方，再垂直落下，避免扫过切块和桌面。

### 三、验证结果

- 主工程相关测试 24/24 通过：动作协调、物品控制、缺物品、取消/暂停恢复、真实角色完整
  切割装盘、HUD 和生产目录。
- 完整真实帧测试验证两次刀刃均穿过苹果中心，四块切块全部落入盘子；完整苹果库存只减少
  1。临时视觉截图检查后已删除。
- Android Development ARM64/IL2CPP 构建成功：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  `137,014,092` 字节，SHA-256
  `4b224fe6734b774b6fd9347158b81f3b89eea3438e9f5628010290428cded232`。
- 本轮 PICO Emulator 启动在 HVF 初始化阶段被宿主拒绝：`HV_DENIED`。APK 未能在模拟器
  内安装启动；后台模拟器和等待进程均已终止。真机自然度和交互手感仍需设备验收。

## Phase Water Dispenser Button V002 工作汇报：按钮可见性与点击修复（2026-10-16）

- 针对“按钮不明显、水滴图标不清晰、无法点击且没有按压动画”的反馈完成原位修复。
- 模型增加 92×54 mm 深色按钮座；按钮扩大为 76×44 mm 并明确前凸，水滴图标扩大为
  约 34 mm 高的高对比蓝色。总包络仍为 340×372×1400 mm。
- Unity 点击热区不再使用位于模型原点的 Pivot，改为绑定
  `WaterDispenser_DropButton` Renderer 中心；热区为 78×52 mm，位于机身前表面外
  8 mm，不受机身 Collider 遮挡。
- 按钮行程由 4 mm 增至 5 mm；增加 120 ms 快速点击脉冲，按下约 45 ms、回弹约
  110 ms，即使 PointerDown/Up 同帧也能完整显示动作。
- V002 模型为 36 个闭合网格、10,906 顶点、21,684 三角面；GLB/FBX 回读与流形校验
  全部通过。
- Unity 2022.3.38f1 场景重建成功；增强专项测试 3/3 通过，覆盖热区对齐、按下位移、
  快速点击脉冲、回弹归位、目录契约和接水功能。

## Phase Water Dispenser Button V003 工作汇报：水滴图标穿模修复（2026-10-16）

- 用户反馈水滴图标若隐若现且展示不完整。定位为图标背面与按钮正面近似共面，存在
  深度竞争和圆角表面交叠风险。
- 将水滴图标改为 1.2 mm 厚的独立浅浮雕，整体前移并与按钮正面保留约 0.8 mm
  空隙；同步内收机身后表面，总包络继续保持 340×372×1400 mm。
- Blender/GLB/FBX 尺寸、36 个闭合网格和 21,684 三角面回读验证全部通过。
- Unity 场景重建退出码 0；专项测试 2/2 通过。新增 Renderer Bounds 断言要求图标与
  按钮表面至少分离 0.4 mm，并继续验证按钮动画、出水和接水功能。

## Phase Interaction Component Focus 工作汇报：可交互部件光圈（2026-10-16）

### 一、功能完成情况

- 取消 `GameItem` 对整件虚拟物品全部 Renderer 追加粉色光圈材质的旧实现。普通碗盘、
  家具、食物等没有声明独立控件时，视线对准后不再整件被光圈包围。
- 新增通用 `ItemInteractionFocusTarget`。每个物品内容只需登记交互控件、对应 Renderer
  和统一光圈材质；中央焦点系统不包含电视、收音机、饮水机等物品名分支。
- 焦点仍按整件物品判定：只要物品进入视线中心，`GameItem.SetFocused(true)` 会一次
  广播给该物品全部标记，不需要将射线逐个对准按钮或旋钮。
- 电视登记电源按钮 1 项；收音机登记开关、调谐旋钮、音量旋钮 3 项；饮水机登记出水
  按钮 1 项。场景和三个生产 Prefab 已重新构建。
- 光圈与 HUD 状态解耦。B 键/双手握拳隐藏 HUD 后，
  `VirtualItemFocusController` 继续运行，可交互部件仍然显示光圈。

### 二、视觉调整

- 小组件光圈外扩宽度由 `0.012 m` 降至 `0.003 m`。
- 发光强度由 `1.8` 降至 `0.65`，Alpha 由 `0.95` 降至 `0.70`。
- 颜色保留粉色语义，但改为较柔和的 `(1.0, 0.16, 0.48)`，避免遮挡按钮图标和旋钮
  刻度。组件本身的 pointer hover/按压反馈继续保留。

### 三、验证

- Unity 场景重建成功，日志包含
  `MATESOUL_SCENE_BUILT path=Assets/MateSoul/Scenes/MateSoul.unity`。
- Runtime、Editor、Engine Tests 的 Unity Roslyn 编译通过；Bee 为 906/906 成功。
- 正式相关回归共 17 项：16 通过、0 失败、1 跳过。跳过项是需要外部诊断视频的既有
  电视解码可选测试，与本功能无关。
- 已覆盖：物品中心广播全部收音机控件、电视仅按钮描边、饮水机仅按钮标记、普通物品
  无整包描边、HUD 隐藏焦点保持、弱光材质参数，以及三类设备原交互功能无回归。
- 尚未构建包含本功能的新 Android APK，也未在 PICO 真机验收 3 mm 光圈的实际观感。

## Phase Action Locomotion Recovery 工作汇报：局部避障与阻塞判定修复（2026-10-16）

### 一、根因与修复

- 运行时证据确认用户判断成立：旧随机行走依赖 `NavMeshAgent` 局部避障，而生产 Action
  移动只沿静态 NavMesh 拐点驱动 Rigidbody，因此遇到未 carving 的动态障碍时不会绕行。
- `AvatarMotor` 恢复 `NavMeshAgent`，但设置 `updatePosition=false`、
  `updateRotation=false`、`updateUpAxis=false`。Agent 只输出 `desiredVelocity`，
  `PhysicsBody` 和 Rigidbody 仍是角色位移唯一 owner，不形成第二套 Transform 写入。
- `MoveAction` 不再把碰撞回弹或横向抖动当作进展，而是记录到目标的历史最短水平距离。
  每改善 1.5 cm 重置阻塞计时；停滞期间每 0.75 秒重新求路，连续 3 秒无有效进展后才
  fail-closed 报错。

### 二、验证与交付

- 动态非 carving 障碍专项测试成功绕行，最大横向偏移约 0.65 m；未映射硬墙仍在约
  3.02 秒后失败，角色没有穿墙。
- 相关 Unity 回归 27/27 通过，覆盖物品控制、Action 协调、HUD/目录、生产移动、局部
  避障、硬墙拒绝和完整切水果流程。权威结果为
  `/Users/bytedance/MateSoul/.artifacts/cut-fruit-action/movement-regression.xml`。
- Android Development ARM64/IL2CPP 构建成功并更新正式 APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，`136,804,291` 字节，
  SHA-256 `10b88beed6b7029c62f095c98b535a809740c33686451e359d3e638b21cf3af6`。
- 打包期间同时修正新烹饪组件在 `MonoBehaviour` 字段初始化器中创建
  `MaterialPropertyBlock` 的 Unity 生命周期错误，改为首次使用时创建，未改变烹饪业务。
- PICO 模拟器仍受宿主 `HV_DENIED` 限制，真机验收前
  `debug-movement-blocked.md`、运行时网络插桩和调试服务保持 `[OPEN]`，不得提前清理。

## Phase Cooking System V001 工作汇报：加热底座与通用烹饪链路（2026-10-16）

### 一、模型与内容接入

- 新增加热底座资产
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/HeatingBaseV001/`。主体设计尺寸
  330×300×55 mm，总包络 330×313.5×60.15 mm；230 mm 加热面适配平底锅约
  224 mm 有效锅底。
- 模型包含独立加热面、电源/减号/加号按钮枢轴和温度屏幕；28 个生产网格、
  19,428 三角面，边界边和非流形边均为 0，GLB/FBX 回读尺寸一致。
- `HeatingBaseBuilder` 已生成生产 `HeatingBase.prefab/.asset`，三个 World Space XR
  按钮均有 2.5 mm 压下和自动回弹；屏幕关机熄灭，开机默认显示 60°。
- 现有平底锅 Prefab 已增加 `CookwareHeatReceiver`；生牛排已增加 `CookableFood`
  和熟牛排材质映射。只有 `RawSteak.asset` 的 `CookingSettings.enabled=1`，其他食品
  默认关闭。

### 二、烹饪逻辑

- 底座温度范围 40–100℃，步长 10℃。熟度公式为
  `max(0, 锅温-20) × 烹饪秒数 × 0.025`，熟度上限 120。
- 50/70/100 分别对应五分熟、七分熟、全熟；超过 100 标记 `Burned`，并写入可持久化
  物品状态。
- 锅底和加热盘使用圆相交面积公式，接触率至少 55%，同时校验 14 mm 垂直间隙和
  12° 倾角。底座每 0.2 秒做一次 `OverlapBoxNonAlloc` 广相位。
- 锅具每 0.2 秒检查内部食材；仅中心、高度和食材半径均位于锅内且带可烹饪标记的
  物品受热。没有逐帧渲染网格接触面积计算。

### 三、验证结果

- `CookingSystemTests` 定向回归 4/4 通过，覆盖按钮回弹、温度上下限、熟度公式、
  接触面积、锅内判定、标记准入和生熟材质切换。
- 全量 EditMode/Engine 运行约 8 分钟后停在既有 PlayMode 域重载且 CPU 为 0，
  未生成汇总 XML；该次运行被终止，不能计为通过。
- Android Development ARM64/IL2CPP 构建成功，APK 为
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，138,369,867 字节，
  SHA-256 `63ddc9011981704ed05aa97340616c958354cd3166f9a4d26c0aff4b41c04b5d`，
  v2 签名验证通过。
- 美术与验证交付包：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/ElectricHeatingBase_CookingSystem_v001.zip`，
  SHA-256 `05d22b35c3773b83dd3d464a4efe10204b8bb83c075f0cfc633090883d0b77c7`。

## Phase Coffee Bean Jar V001 工作汇报：有限容量咖啡豆罐（2026-10-16）

### 一、模型与内容接入

- 新增
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/CoffeeBeanJar/`，包含 Blender
  源文件、GLB/FBX、4 张 PBR 贴图、5 张多视图渲染和参数化构建脚本。
- 罐体真实尺寸为 120×120×194 mm，共 5 个闭合网格、32,264 顶点和 61,824
  三角面；边界边和非流形边均为 0。罐内 336 颗咖啡豆合并为一个 Renderer。
- Unity 新增 `CoffeeBeanJar.prefab/.asset` 和 `CoffeeBeanCluster.prefab`。物品 ID 为
  `coffee-bean-jar`，分类为厨房用品，支持桌面放置，虚拟物品栏库存上限为 2。

### 二、交互与容量

- 木盖使用独立 Pivot、物理 Collider 和 XR Trigger，但罐体与木盖只保留一个根
  `PhysicsBody`/Rigidbody，符合项目物理所有权约束。
- 木盖受力冲量达到 0.12 后打开，也可通过 XR 直接选择切换开合；开启角度为 108°。
- 木盖开启超过 85% 且罐体倾斜后，每 0.11 秒释放一簇。总容量固定为 12 簇，每簇
  28 颗合并可视豆，视觉总量 `12×28=336` 与罐内豆数一致。
- 倒空后不生成第 13 簇。罐内豆层随剩余容量缩放，释放 5 簇后为 7/12，倒空后隐藏。
- 每簇只有一个 Renderer、一个 Rigidbody 和一个 Collider；静止 0.45 秒或运动满
  4 秒后转为 Kinematic，45 秒后清理。两瓶同时倾倒最多产生 24 个活动豆簇刚体。

### 三、验证与后续

- Unity 2022.3.38f1 场景重建退出码为 0。
- 专项测试 3/3 通过，覆盖单根刚体、盖子物理/XR Collider、受力阈值、12 簇容量、
  7/12 豆层缩放、倒空隐藏、禁止无限生成、豆簇冻结和两瓶库存。
- 测试结果：
  `/Users/bytedance/MateSoul/.artifacts/framework/unity-coffee-bean-jar-tests.xml`。
- 结构化验证报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/coffee_bean_jar_virtual_item_20261016.json`。
- 尚未进行 PICO 真机验收。后续需检查盖子受力手感、倾倒角度、透明材质，以及两瓶
  同时倾倒时的设备帧率。

## Phase Dialogue UI And Social Attention V001 工作汇报：底部对话条与玩家注视（2026-10-17）

### 一、对话 UI

- `GameHud` 不再为“对话”打开 760×330 的通用大面板，改为在原底部导航位置显示
  824×112 的浅紫雾透玻璃输入条；输入框右侧依次为“发送”和最右侧“关闭”。
- 展开时导航在 0.34 秒内渐隐下沉，对话条延迟 0.1 秒后以横向展开、轻微上浮和
  `EaseOutQuint` 入场；关闭时对话条在 0.42 秒内由两侧向中心挤压，导航使用
  `EaseOutBack` 向上回弹。反向操作从当前视觉状态继续，不重置到动画起点。
- 发送只清空已成功提交的文本，对话条保持展开；关闭只收起 UI 和释放对话注视请求，
  不取消正在等待的 AI 回复。B 键/双手握拳隐藏 HUD 时仍保留当前对话展开状态。

### 二、角色社交注视

- 新增 `AvatarSocialAttentionRig` 和 `SocialAttentionAction`。动作统一占用
  `Locomotion | Gaze`，通过 `AvatarActionCoordinator` 运行，不由 UI 直接写角色根节点。
- 打开对话条或玩家进入 1.55 m 水平距离会请求注视；离开采用 1.90 m 阈值，避免边界
  抖动。该 UI 级 Action 可抢占自主漫游，但不会抢占同级或更高优先级正式动作；正式
  动作结束且请求仍存在时自动恢复注视。
- 冷月身体以 48°/s 缓慢转向玩家；Generic Rig 的 `Neck / Head 1 / Eye_L / Eye_R`
  在 Animator 后执行平滑加权。头部水平限幅 48°、俯仰上 26°/下 20°，眼球只补偿
  剩余小角度；玩家绕到侧后方时身体持续跟进，避免头部异常扭曲。

### 三、验证与后续

- `scripts/check-game-framework.py` 已补齐 TextMeshPro 和 Animation Rigging 程序集引用，
  Runtime、Editor、Tests 三程序集静态编译通过。
- Unity 相关回归 5/5 通过，覆盖 HUD 展开/发送/关闭、对话请求、近距离滞回、自动漫游
  抢占、正式 Action 等待与恢复、生产移动和系统键盘暂停期间对话保持。结果：
  `/Users/bytedance/MateSoul/.artifacts/dialogue-ui/regression.xml`。
- 尚未构建新 Android APK，也未在 PICO 真机验收雾透材质、世界空间 UI 动画观感及
  头眼跟随幅度。真机验收应重点观察 48° 头部限幅、快速绕行时身体接管和按钮射线手感。

## Phase Turn In Place V001 工作汇报：冷月任意角度自然转身（2026-10-17）

### 一、运行时转身模组

- 新增 `TurnInPlaceMotion`，把任意目标朝向转换为带加速、减速、进入/退出死区和
  180°方向锁的角位移。37°、-83°、146°等角度不再依赖固定角度动画。
- `AvatarMotor` 新增原地转身状态输出。角色静止后由求解器生成角位移，并继续通过
  `PhysicsBody.RotateTowards()` 驱动 Rigidbody；没有新增 Transform 或 Animator Root
  Motion 所有者。
- 路径方向与身体夹角达到 70°时先减速原地转身，缩小到 30°后再开始移动，避免角色
  面向侧后方时横滑。小于 12°的调整使用低速微转，仍可精确完成 Dock 对齐。
- 接近 180°时锁定已选择的左右方向，只有离开反向模糊区后才允许换向，避免目标轻微
  抖动导致左右脚反复切换。

### 二、Animator 内容

- 生产 `Avatar.controller` 新增 `TurnRate` Float 参数，Base Layer 改为
  `Speed × TurnRate` 的二维 Freeform Cartesian BlendTree。
- 左右转身第一版复用现有 `Lengyue_Walk_BodyOnly`，播放速度为 0.78；右转使用 0.5
  周期偏移，使左右方向由不同脚起步。Idle、Walk 和物品交互 Layer 保持原结构。
- 新增 `AvatarLocomotionBuilder` 及菜单
  `MateSoul/Game/Rebuild Avatar Locomotion`。新建角色和已有角色的场景重建路径都会
  重新应用同一 Locomotion 配置，避免生成资产与构建代码漂移。

### 三、验证与边界

- Unity 2022.3.38f1 通过 LaunchServices 完成真实编译和 Controller 重建，日志包含
  `MATESOUL_LOCOMOTION_BUILT` 并正常退出。
- 转身专项测试 6/6 通过，覆盖任意角度收敛、180°方向稳定、Controller 结构、真实角色
  73°原地转身、无水平漂移、步态权重和参数归零。
- 共享动作定向回归 7/7 通过，覆盖生产移动、两类局部避障、社交注视、坐下/起身、
  躺下/起身及切水果多阶段动作。
- 测试中发现 Unity 的 `Mathf.Sign(0)` 返回 1，会使正向微转停在约 2.45°；现已在符号
  判断前增加角速度阈值。TRAE 受限 shell 的 Bee 仍会报 semaphore error 15，真实
  Unity 验证继续使用系统 LaunchServices。
- 当前尚未进行 PICO 真机视觉验收。第一版使用行走步态表达转身，后续可替换为独立
  左右转身 Body-Only Clip，并根据真机脚滑观感决定是否增加脚掌 Plant 曲线和 IK 锁定。

## Phase Action Locomotion And Simplified Carry V002 工作汇报：持久寻路与无物理跟手（2026-10-17）

### 一、寻路修复

- `MoveAction` 已改为与随机行走一致的持久目标生命周期：路径只提交一次，仅当目标实际
  移动超过 0.1 m 时重新求路，不再每秒重复提交同一条全局路径。
- `NavMeshAgent` 继续负责 NavMesh 规划和已注册障碍的局部避让，Rigidbody 仍是角色
  位移唯一 owner。普通 Collider 阻挡时，角色在接触 0.3 秒后沿固定侧切向恢复，避免
  左右方向反复翻转。
- 物理绕行最多允许 8 秒，Action 总无进展兜底为 12 秒。真正不可绕开的硬墙仍
  fail-closed，但“必须推动轻物体”的旧测试要求已删除。

### 二、拾取与携带简化

- 手部接近准入由 3 cm 调整为 15 cm 上限。完成伸手动作且物品进入该范围后，物品抓取点
  直接吸附到实际手心，不再要求手指精确接触。
- 进入 `HeldByCharacter` 时，物品 Rigidbody 转为 Kinematic、关闭重力并设置
  `detectCollisions=false`。抬起、携带和刀具动作期间只按手部或 Action Pose 跟随。
- 已删除持有期的分段路径检查、`BoxCast`、Collider Sweep 和 `OverlapBox`。冷月本体的
  避障与硬墙规则不作用于已拾取物品。
- 放下前恢复碰撞；预约结束时恢复原 Rigidbody 配置，且不重放拾取前的旧速度。

### 三、验证与交付

- Unity 相关回归 27/27 通过，覆盖未注册 Collider 绕行、不可绕硬墙拒绝、8 cm 近手
  吸附、持有期物理关闭与取消恢复，以及完整切水果和库存防复制。结果：
  `/Users/bytedance/MateSoul/.artifacts/cut-fruit-action/simplified-runtime-regression.xml`。
- Android Development ARM64/IL2CPP 构建成功。APK ZIP 完整性和 v2 签名验证通过：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，`151,521,814` 字节，
  SHA-256 `aa6880a99d6f26cbec4187493c6d3c0e7794a31083efc8b4aabd1872742f63b5`。
- PICO 真机仍需复验普通障碍绕行、切水果拾取和持有物稳定跟手。在用户确认前，
  `debug-movement-blocked.md`、`debug-item-reach-layer.md` 及对应运行时插桩保持
  `[OPEN]`，不得清理。

## Phase Cooking Spontaneous Reply V002 工作汇报：牛排模型切换与系统性自发回复（2026-10-17）

### 一、底座视觉修复

- 电源按钮删除错误的 `I/O` 文本，改为透明 Sprite 标准电源符号。图标由
  `HeatingBaseBuilder` 确定性生成，不依赖 TMP 字体是否包含 `U+23FB`。
- 温度数字改为暖橙色；显示屏发光由高亮红色降为低亮暗红背光，避免背景吞掉数字。
- `HeatingBase.prefab` 已重建，三个按钮继续保留 XRI 点击、压下和自动回弹行为。

### 二、生熟牛排切换

- `CookableFood` 从单 Renderer 材质替换改为生、熟两个完整模型根节点切换。
- `GameSceneBuilder` 把熟牛排 FBX 写入 `RawSteak.prefab`，并强制复制生牛排模型的
  `localPosition`、`localRotation` 和 `localScale`。熟度首次达到或跨过 50 时关闭
  生模型并开启熟模型；煮糊色只作用于熟模型 Renderer。
- 专项测试验证两个模型初始位姿完全一致，且达到 50 后确实发生 GameObject 激活切换。

### 三、系统性自发 AI 回复

- 新增 `SpontaneousReplyCoordinator`、`ISpontaneousReplyProvider` 和独立 Gateway
  端点 `/v1/system-replies/generate`。
- 请求仅包含角色人格快照、事件描述、内容要求和规则；不携带普通对话历史、玩家记忆、
  Runtime Action、物品目标、心情信号或 MCP 工具，也不写回普通会话上下文。
- `CookingSpontaneousReplyTrigger` 在牛排首次进入可见熟制阶段时提交事件化要求：
  冷月闻到香味并询问玩家是否在煎牛排。最终台词由 AI 生成，没有写死在客户端。
- 回复当前通过短时字幕和既有情绪音频片段呈现。项目尚无任意文本 TTS，因此不能把
  情绪音频描述为生成台词的真实语音。

### 四、验证与交付

- AI Gateway TypeScript 构建与测试：17/17 通过。
- Unity `CookingSystemTests` 与 `SpontaneousReplyTests`：5/5 通过。
- Unity 生产场景和 Prefab 重建成功，Android Development ARM64/IL2CPP 构建成功。
- APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  `139,644,827` 字节，SHA-256
  `5f64e2b7484b79f5bd158ce3d339226a096bb32fe05a3ffff0eea6293a189518`。
- APK ZIP 完整性无错误，APK Signature Scheme v2 验证通过。
- Release 构建仍按既有安全策略要求 HTTPS 网关；本次本机 HTTP 配置只生成
  Development APK，没有放宽发布准入。

### 五、后续建议

- 后续环境事件应复用 `SpontaneousReplyCoordinator.Enqueue()`，新增事件适配器，
  不得调用 `ConversationOrchestrator.SendAsync()` 或把系统事件伪装成玩家输入。
- 真机复验电源图标尺寸、温度数字对比度、生熟模型切换瞬间是否闪烁，以及字幕和情绪
  音频的同步观感。
- 若需要冷月真正朗读任意生成台词，应单独接入 TTS，并由同一协调器的成功回复事件驱动。

## Phase Ergonomic Grip Follow V003 工作汇报：人体工学手臂与零偏移跟手（2026-10-17）

### 一、问题根因

- 运行时证据确认，旧 IK 会把物品完整 Grip 旋转直接赋给手骨，单帧腕部旋转峰值达到
  179.947°；所有物品默认 `gripEuler=(0,0,180)`，刀具搬运还叠加 180°翻转。
- 旧搬运逻辑在 Action Tick 中以 0.7/1.6 m/s 单独插值物品，随后
  `AvatarItemRig.LateUpdate()` 再让手追物品。Lift 阶段 Grip 与手心最大分离
  19.7 cm，角色抬手与物品移动不是同一个 Pose 所有者。
- Rigidbody 保持 Interpolate，给非物理持有物额外引入渲染延迟。

### 二、运行时修复

- 接近阶段只求解抓取点位置，保持动画给出的自然手腕朝向；进入吸附时保持物品当前
  朝向，只平移抓取点到手心。
- `AvatarItemRig` 保存一次手到物品的相对 Pose。最终 IK 完成后，由 `HandGrip` 唯一
  写入持有物 Pose；`ItemTransferStep` 不再独立移动 Rigidbody。
- 持有期间关闭 Rigidbody 插值、重力和碰撞，并同步 Rigidbody/Transform；释放时恢复
  原配置。取消或租约回收先回滚最近稳定检查点，避免物品从地板内部恢复后坠落。
- 人体工学边界：肘角 35°–175°、前臂旋转最多 75°、腕部修正最多 55°。普通搬运不
  请求腕部旋转；只有切刀等明确需要朝向的动作使用受限旋转。
- 切刀先进入 Cut 身体姿势，再抬刀并执行水平中心校正；仍以刀刃实际穿过苹果中心作为
  唯一切割提交条件。

### 三、验证结果

- 物品 Action 套件 4/4 通过，覆盖取消/暂停回滚、近手吸附、零偏移跟手、完整切割与
  四块装盘：
  `/Users/bytedance/MateSoul/.artifacts/cut-fruit-action/wrist-grip-item-suite-2.xml`。
- 共享回归 27/27 通过：
  `/Users/bytedance/MateSoul/.artifacts/cut-fruit-action/wrist-grip-regression-3.xml`。
- post-fix Grip 与手心最大偏差约 `2.43e-7 m`，持有 Pose 同步误差约 `1.19e-7`；
  实测肘角 37.95°–172.11°，腕部修正不超过 55°。
- 两次刀刃轨迹均记录 `crossed=true`；第一刀中心误差约 6.3 mm，第二刀低于 1 μm。
- Android Development ARM64/IL2CPP 构建成功，APK ZIP 完整性与 v2 签名验证通过：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，`139,644,816` 字节，
  SHA-256 `8cbc1fcdd288c66c632275c5928ffa330e9fb04d0095277eb2f77333a56a8eb1`。
- PICO 真机确认前，`debug-wrist-grip-follow.md`、网络插桩、日志和调试服务保持
  `[OPEN]`，不得清理。

## Phase Turn In Place V002 工作汇报：专用转身步态与支撑脚锁定（2026-10-17）

### 一、V001 不足与本轮修复

- V001 的左右转身仍直接复用前进 Walk。原动画脚踝前后轨迹约 40 cm，而 Rigidbody
  围绕身体中心连续旋转，脚步相位、支撑脚和角速度没有同步，因此滑步属于结构性问题，
  不能继续靠调整 BlendTree 阈值解决。
- 新增确定性生成的 `Lengyue_TurnLeft_BodyOnly` 和
  `Lengyue_TurnRight_BodyOnly`。转身 Clip 将手臂摆幅降至原 Walk 的 22%，躯干降至
  34%，腿部降至 52%–68%，并为髋、脊柱、腿和脚增加左右方向性旋转。
- 两条 Clip 写入显式 `leftPlantWeight/rightPlantWeight` 曲线：前半周期锁左脚，后半
  周期锁右脚，换脚窗口平滑交接。

### 二、运行时脚锁与节奏

- 新增 `AvatarTurnFootPlantRig`，执行顺序为 10900，在 Animator 后、坐卧与物品动作
  前运行。它只在 `motor.IsTurning && Speed < 0.12` 时启用。
- 支撑脚进入 Plant 区间时缓存世界 Pose，再复用项目已验证的
  `AvatarSeatingRig.Solve()` 两骨 IK 修正对应腿链。未向 `RigBuilder` 增加腿部 Layer，
  因此非转身状态不会改变手臂、物品交互或坐卧动画图。
- 原地转身最大角速度由 150°/s 校准为 135°/s；换脚双支撑阶段继续降低根旋转速度。
  该值兼顾步态周期和动态障碍绕行，120°/s 会使物理障碍回归超过 12 秒时限。
- Rigidbody 仍是根位移与旋转的唯一所有者；脚锁只写腿骨，不写角色 Transform。

### 三、验证与交付

- 转身专项 `9/9` 通过，覆盖左右专用 Clip、Plant 曲线、37°/-83°/146°求解、
  73°右转和 -112°左转。
- 支撑脚高权重阶段最大世界滑移：右转约 4.47 mm，左转约 17.43 mm；验收上限为
  25 mm。角色根水平漂移低于 20 mm。
- 共享移动、两类避障、社交朝向和坐卧回归 `6/6` 通过；完整切水果与四块装盘独立
  回归 `1/1` 通过。
- 最终测试证据保存在
  `/Users/bytedance/MateSoul/.artifacts/turn-in-place-v2/turn-tests.xml`、
  `shared-regression.xml` 和 `item-action-regression.xml`。
- 生产场景重建成功。Android Development ARM64/IL2CPP 构建成功，APK 为
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，139,644,816 字节，
  SHA-256 `8cbc1fcdd288c66c632275c5928ffa330e9fb04d0095277eb2f77333a56a8eb1`；
  ZIP 完整性通过，APK Signature Scheme v2 验证有效。
- 尚未完成 PICO 真机主观观感验收。后续重点观察左转 112°时约 17 mm 的残余滑移、
  裙摆与膝盖交叉，以及快速连续改向时的换脚节奏；不得为了消除剩余滑移引入第二个
  根节点 Pose owner。

## 2026-10-17：PICO Emulator 启动后40GB与QEMU挂死修复

- 用户报告游戏启动后短时间内模拟器达到约40GB。检查时QEMU和ADB已经退出，
  无法补采现场vmmap，因此没有伪造该次进程精确分项。
- 本轮宿主日志确认MateSoul VkDevice创建后连续出现
  `VK_ERROR_INVALID_SHADER_NV`、`Shader library compile failed` 和
  `cannot reserve 'buffer' resource location at index 11`。最后700行共有53条
  shader/挂起信号，随后QEMU main loop和CPU0–5多次超过15秒无响应。
- 根因变量是此前内存诊断候选规避强制设置
  `MVK_CONFIG_USE_METAL_ARGUMENT_BUFFERS=0`。该设置已在前一轮证明不能降低
  图形内存增长，本轮进一步证明会破坏PICO Emulator/MoltenVK的shader资源绑定。
- 已从普通启动器和内存采样器移除0/1覆盖；启动前清除继承值，恢复MoltenVK
  driver-default。25GiB进程保护、系统压力保护、VST和虚拟物品事件保持不变。
- Shell/Python语法、环境回滚静态检查通过。当前系统内存余量恢复到39%，QEMU未运行。
- 该修复针对“启动即40GB并挂死”。历史“虚拟物品高频交互后宿主graphics缓慢增长”
  是另一条尚未闭环的路径，必须在driver-default下重新执行interaction-correlation，
  不能把两种症状合并报告为已完全解决。

## Phase Turn In Place V003 工作汇报：阈值迈步与低优先级社交注视（2026-10-17）

### 一、功能完成情况

- 原地转身改为阈值式调整：小于约 18° 的偏差允许轻微根旋转，不混入转身步态；
  达到阈值后触发一次左/右 `TurnStep`，动作结束仍有较大偏差才重复迈步。
- 左右转身动画由循环 BlendTree 改为一次性 Animator State；腿、膝和脚动作幅度提高
  到 90%–100%，解决 V002 脚步不明显的问题。
- 支撑脚 IK 权重改为非线性重映射，进入稳定支撑区后完全锁定，保留抬脚和落脚渐变。
- 指定 `idle_03_InPlace.fbx` 已清洗为 3.708 秒、24 FPS 的 Body-Only 社交 Idle。
  共映射 69 根生产骨骼，排除 Hair、Skirt、IK、CTRL、DRV、MCH 曲线，无缺失骨骼。
- 社交注视进入独立 `SocialAttention` 状态：先由头、颈、眼跟随玩家；头部实际偏转
  达到 32° 后身体才迈步转向，身体偏差回落到 12° 后停止。
- 新增最低优先级 `IntentSource.Ambient`，社交注视声明 `ControlChannels.All`。
  Autonomous、AI、UI、Player、System 任一正式 Action 或后续全身特殊动画均可抢占。

### 二、验证与交付

- Action 仲裁回归 `19/19`、转身专项 `11/11`、社交注视专项 `1/1` 通过。
- 完整 `GameRuntimeTests` 为 `29/35`。本次社交用例通过；其余 6 个失败位于未映射墙体、
  临时碰撞绕行、每日库存、易碎物、旧布局回滚和 AI 目标移除，属于当前并行功能改动。
- 测试证据位于 `/Users/bytedance/MateSoul/.artifacts/turn-in-place-v3/`。
- Android Development ARM64/IL2CPP 构建成功：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  141,170,493 字节，SHA-256
  `792d70d5e175472f71f1109d8ae1d1b7ddbcdcd82105fb6dcf76e91dfe39f74f`。
  ZIP 完整性与 APK Signature Scheme v2 验证通过。

### 三、遗留事项

- Release 构建仍按安全规则拒绝当前本地 HTTP 对话网关；本次为 Debug 签名的
  Development APK。发布前必须配置 HTTPS 网关并走 Release 构建。
- 仍需 PICO 真机主观验收 18° 临界点、连续大角度重复迈步、裙摆遮挡和头身衔接。
- 完整运行时回归的 6 个并行失败需由对应物品、存档和导航任务分别收敛。

## Phase Cooking Effects V003 工作汇报：烹饪音效、白烟与显示器镜像修复（2026-10-17）

### 一、音效资产与底座反馈

- 新增 `CookingEffectsAssetBuilder`，确定性生成三份 44.1 kHz 单声道 PCM WAV：
  130 ms 电子滴声、4 秒电磁炉低频运转循环和 6 秒食材煎制滋滋循环。
- 加热底座三个有效按键点击都会播放空间化滴声；无效或不可交互点击不播放。
- 电磁炉运转声只在底座已通电且锅底接触面积通过准入时循环播放。断电、锅体移开或
  系统暂停后立即停止，避免“有声音但没有实际传热”的状态分裂。
- 音量检测确认三份 WAV 非静音：滴声平均 -14.3 dB、滋滋声 -17.5 dB、
  电磁运转声 -11.2 dB。

### 二、食材滋滋声与白烟

- 新增 `CookingEffects`，由每个带 `CookingSettings.enabled=true` 的食材实例拥有。
- `CookwareHeatReceiver` 在原 0.2 秒固定数组扫描中同时维护实际受热食材集合。
  食材进入有效受热锅内时开启滋滋循环和白烟；离锅、失去热源、暂停或禁用时关闭。
- 白烟使用 URP Particles/Unlit、64×64 软 Alpha 贴图和最多 36 个粒子。发射率、
  滋滋音量和音高随 40–100℃变化，不新增逐帧物理查询。
- 该机制不绑定牛排名称。未来任何正确配置 `CookingSettings` 的食材都会自动获得
  同一受热表现。

### 三、显示器镜像修复

- 修正 `HeatingBaseBuilder.MakePanel()` 的世界空间 Canvas 朝向。
- UI 正面现在朝向显示器所在的底座外侧，不再从 Canvas 背面读取温度数字；电源、
  加减按钮的射线交互与压下回弹保持不变。

### 四、验证与交付

- 生产场景、`HeatingBase.prefab` 和 `RawSteak.prefab` 重建成功。
- `CookingSystemTests` 定向测试 4/4 通过，覆盖：
  - 三类音频与烟雾资源绑定；
  - 按键滴声只触发一次；
  - 无锅时不播放电磁运转声；
  - 有效锅接触后运转声启动；
  - 食材入锅后滋滋声与白烟启动，离锅后停止；
  - 显示器 Canvas 正面朝向外侧。
- 主工程被并发回归占用时，定向测试在临时无 Library 副本中完成；验证后临时副本已删除。
- Android Development ARM64/IL2CPP 构建成功。APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  `157,683,529` 字节，SHA-256
  `4f10135caf64e25582a7146fab752487384e15f45b20ec8622099a3786a86dc5`。
- APK ZIP 完整性无错误，APK Signature Scheme v2 验证通过。

### 五、后续建议

- PICO 真机重点确认三类音效在 0.5–2 m 距离的相对音量、滋滋循环接缝、白烟透明度和
  高温发射密度；主观听感参数只在真机证据基础上调整。
- 当前运转声严格绑定有效锅接触。如果产品后续需要“无锅报警”，应新增独立短提示音，
  不得让运转循环在无锅状态播放。

## Phase Turn In Place V004 工作汇报：低频中幅调整步（2026-10-17）

### 一、用户反馈与根因

- V003 在 18° 偏差即播放完整 TurnStep，腿部幅度又达到 90%–100%，导致轻微身体
  转动也出现频繁大步。
- FixedUpdate 可能在 Animator 消费 Trigger 前重复清除并设置 Trigger，残留请求会在
  返回基础状态后立即触发下一步。
- 转身 Clip 继承了 Walk 的腿脚位置曲线，且首尾没有回到 Idle；右转静态脚轨迹峰值
  达 38.3 cm。
- Plant 曲线锁定了正在摆动的脚，支撑相位反向，进一步放大脚部拉扯和滑移。

### 二、修复

- 迈步准入从 18° 提高到 32°，小角度根旋转降到 18°/s；社交身体跟随改为头部实际
  偏转达到 40° 后接管，14° 时退出。
- TurnStep 请求拆分为“已请求、已观察到状态、已退出恢复”三阶段，动作退出后增加
  0.6 秒恢复窗口，禁止同帧重复 Trigger 和无缝连踩。
- 生成动画加入 0→1→0 单步包络，首尾严格回到 Idle；清除腿脚 Walk 位置位移，仅保留
  受控关节旋转和 18% 髋部位移。
- 左右 Plant 相位交换为第一阶段右脚支撑、第二阶段左脚支撑。
- 腿、膝、脚旋转混合收敛至 45%/55%/50%，手臂 15%、躯干 25%。

### 三、验证与交付

- 转身专项 `15/15` 通过，包括：
  - 26° 偏差全程不触发脚步；
  - 120°、48°/s 慢速大转身最多两步，步间隔大于 1.2 秒；
  - 左右 Clip 静态脚轨迹分别 10.8 cm、14.7 cm；
  - 73°右转和 -112°左转支撑脚最大滑移约 9.8 mm、13.2 mm。
- 社交头身与 Action 抢占专项 `1/1` 通过。
- 证据位于 `/Users/bytedance/MateSoul/.artifacts/turn-in-place-v4/`。
- Android Development APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  143,011,277 字节，SHA-256
  `577f61e6f575481eaffc691870029ea05fa5456b65238cf193518aebf4c341c3`。
  ZIP 完整性与 APK Signature Scheme v2 验证通过。

### 四、真机复测重点

- 观察 20°–31° 范围的轻微滑步是否可接受，以及 32° 临界点是否仍有突兀感。
- 观察玩家持续绕角色移动时，0.6 秒恢复窗口是否提供足够停顿。
- 当前数值已有自动化上下界保护；后续若调参，应优先修改阈值和恢复时间，不得重新引入
  Walk 腿脚位置曲线或取消单步首尾 Idle 包络。

## Phase Classified Grip V001 工作汇报：全物品模板抓握与切水果闭环（2026-10-17）

### 一、功能完成情况

- 抓握配置统一为 `Generic`、`Handle`、`LoopHandle`、`LargeTwoHand`、`ThinEdge`
  五类。每个物品 Profile 保存主/副抓握锚点、局部旋转、尺寸、手指间隙、弯曲度和手势。
- `ItemActionContentBuilder` 根据实际 Prefab Bounds 和命名握持部位生成全部目录 Profile；
  主工程构建日志覆盖 35 个 Profile，包括 `companion-lamp`。
- `AvatarItemRig` 完成左右臂绑定、五指三关节 CCD、薄片侧捏、尺寸驱动包握和双手支撑。
  手腕修正仍限制为 55°，肘角限制为 35°–175°，没有为通过测试放宽人体工学边界。
- 物品接近手后按模板吸附并忽略原摆放朝向；持有期间关闭重力、碰撞和 Rigidbody 插值，
  由最终手部解算位姿单点驱动物品，实现零独立插值跟随。
- `ItemTransferStep` 已接通大物件第二抓握目标，在接近、吸附、搬运和放置阶段同时驱动
  左右手；普通物品继续走单手路径。
- 切水果动作改为按实际握点判断到达，切割阶段闭环修正刀刃中点。第二刀通过共享
  `MoveAction` 绕苹果移动 90°，完成正交切割后回到原工作位并归还刀具。

### 二、验证与构建

- 完整切水果专项：
  `/Users/bytedance/MateSoul/.artifacts/cut-fruit-action/grip-cut-flow-20.xml`，1/1 通过。
  两次实际刀刃交叉均为 `crossed=true`，水平误差分别 4.71 mm 和 4.21 mm。
- `ItemActionRuntimeTests` 4/4 通过；主工程 `ItemGripProfileTests` 3/3 通过。
- 共享物品、Action 与导航回归 27/27 通过：
  `/Users/bytedance/MateSoul/.artifacts/cut-fruit-action/wrist-grip-regression-current.xml`。
- Android Development ARM64/IL2CPP 构建成功：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，约 133 MiB，
  SHA-256 `5aed1df3893dac413010996af71fd226bed3d7b00e989edad39e8ec4c111f1f0`。
  ZIP 完整性、仅 `arm64-v8a` 和 APK Signature Scheme v2 均验证通过。

### 三、踩坑与后续

- Unity Development 增量构建曾复用陈旧 Bee 依赖，导致 UnityLinker 找不到
  `MethodsToPreserve.xml`。`BuildAndroidDevelopment` 现显式使用 `CleanBuildCache`；
  干净重试 4 分 35 秒成功。
- 直接双手搬运集成测试在隔离工程的资产 GUID 重载阶段不稳定，未保留该测试夹具。
  双锚点数学测试、完整内容校验及现有动作回归均通过；电视、桌子、椅子、饮水机和
  加热底座的双手视觉效果仍需 PICO 真机验收。
- 调试会话 `wrist-grip-follow` 保持 `[OPEN]`。真机确认前不得删除插桩、日志或停止
  7786 调试服务。

## Phase Turn In Place V005 工作汇报：近身触发与相位同步优化（2026-10-17）

### 一、功能调整

- 社交注视由 1.55 m 进入、1.9 m 退出大幅收紧为 0.8 m 进入、1.05 m 退出。
  1 m 不再自动进入该状态，只有玩家真正靠近角色才触发。
- TurnStep 使用 `sin²` 零速度首尾包络，降低起步和收步的速度突变。
- 左右转身使用不同 Walk 采样相位和不同支撑脚：左转首段左脚支撑，右转首段右脚
  支撑；换脚阶段保留双支撑重叠。
- Animator 进入 TurnStep 的过渡由 0.08 s 增至 0.14 s，退出点延后至 96%，退出混合
  增至 0.16 s，避免动作未完成就切回 Idle。
- 大角度 Rigidbody 根旋转只在 Animator 实际进入 TurnStep 后推进；等待 Trigger、
  换步恢复期间不再出现身体先转而腿尚未启动的错相。
- 单步最大根速为 78°/s。第一步后剩余偏差不足 48°时不再播放第二个完整步，而以
  28°/s 小幅收尾；仅剩余偏差仍超过 48°才允许重复迈步。

### 二、验证

- `TurnInPlaceTests` 15/15 通过：
  - 26°轻微偏差不触发脚步；
  - 120°慢速转向满足步数和步间隔上限；
  - 左右支撑相位正确，Clip 首尾脚位和脚旋转回到 Idle；
  - 左右脚轨迹约 9.4 cm/14.7 cm；
  - 73°右转与 -112°左转支撑脚滑移约 17.7 mm/17.2 mm，低于 25 mm 上限。
- 社交专项 1/1 通过，覆盖 1.0 m 不进入、0.72 m 进入、0.95 m 保持和 1.15 m 退出。
- 生产移动专项 1/1 通过，NavMesh 行走、动画和停止未受原地转身降速影响。
- 测试证据位于 `/Users/bytedance/MateSoul/.artifacts/turn-in-place-v5/`。

### 三、构建

- Android Development ARM64/IL2CPP 构建成功：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  139,547,185 字节，SHA-256
  `63dde74e7bf03ad3ea3da960e186fb61716f8c571fb0ae9b55b0ea57f0925426`。
- APK ZIP 完整性与 APK Signature Scheme v2 验证通过。

## 2026-10-22 GitHub 主分支发布准备（进行中）

- 按用户要求在独立发布工作树整理当前版本，保留运行、Unity 导入和 Android 构建依赖；原开发目录继续保留开发资料。
- 修正未锚定的 `scripts/`、`Obj/` 忽略规则对 Unity 角色和 SDK 源码的误排除；补齐被忽略的角色程序集、SDK 脚本及资源。
- 清除一次性验证脚本、调试上报、演示场景和桌面模拟器残留引用；饼干运行资源保留原 GUID，迁入正式 Content 目录。
- 保龄球和电视构建使用已纳入仓库的导入资源，不再要求本地 `ArtSource` 目录。
- 网关 TypeScript 编译、进程启动、MCP 工具注册和非法请求校验通过。初始快照的 Unity 编译及场景/角色/资源引用校验通过。
- 发布前检测到并同步了另一开发窗口刚更新的角色、衣装、动画和绑定代码。该最新快照的完整验证及 Android 构建尚待完成，尚未推送主分支。
- 最新角色存在 128 MiB 的 Texture2D 文本资产，需在保持像素、GUID 和引用不变的前提下解决 GitHub 单文件 100 MiB 限制。

### 四、后续

- PICO 真机重点检查 0.8 m 边界是否符合近身交互预期，以及快速绕行玩家时头、髋、
  支撑脚的视觉连续性。
- 后续只允许在测试边界内微调进入距离、根速和过渡时间；不得恢复两侧共用支撑相位，
  也不得让根旋转在 TurnStep 尚未播放时提前推进。

## Phase Cooking Reliability V004 工作汇报：贴面 UI 与真实受热准入修复（2026-10-17）

### 一、问题与根因

- 温度、加减号和电源图标此前统一放在模型总包围盒外 8 mm，没有使用各自屏幕或按钮帽
  的真实表面，导致 UI 明显悬空。
- 温度文本只显示 `°`，缺少摄氏单位 `C`。
- 加热判定直接使用 `HeatingBase_HeatingSurface` 的 FBX Transform Pivot。原测试又把
  锅底圆心人工对齐这个 Pivot，未覆盖真实锅体落在可见加热盘顶面的情况。
- 食材准入要求整个近似圆完全位于锅内；牛排稍微偏心就完全不受热，进而同时不变熟、
  不播放滋滋声且不冒烟。

### 二、修复

- 每个 World Space UI 按各自 Renderer 的外表面定位，只保留 0.6 mm 防止 Z-fighting；
  生产 Prefab 的 UI Z 坐标由 0.16475 m 回贴至约 0.15735 m。
- 温度显示改为 `40°C`–`100°C`，默认 `60°C`。
- `HeatingBaseBuilder` 在可见加热盘 Renderer 顶面创建
  `Heating Surface Contact` 标记，运行时不再依赖 FBX Pivot。
- `CookwareHeatReceiver.Contains()` 改为食材投影与锅内圆的解析重叠面积判定，达到
  55% 即视为在锅内；仍保留高度区间和可烹饪标记准入。

### 三、验证与交付

- `CookingSystemTests` 4/4 通过。测试新增以下真实约束：
  - 接触标记与可见加热盘顶面误差低于 0.1 mm；
  - 四个 UI 与各自模型表面间距为 0.6 mm；
  - 通电后显示 `60°C`；
  - 牛排偏离锅中心 7.5 cm 仍可按面积重叠受热；
  - 熟牛排模型、滋滋声和白烟同步启动，离锅后停止。
- Android Development ARM64/IL2CPP 构建成功。APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  `139,549,309` 字节，SHA-256
  `be5958b75b79bcfaa57aefc110433ad0a8ecaaf04f3e249e5de2a5f2c11a19f0`。
- APK ZIP 完整性无错误，APK Signature Scheme v2 验证通过。

### 四、后续

- PICO 真机复测时应把锅自然放到可见加热盘上，并把牛排放在锅内偏心位置，确认无需
  像旧测试一样精确对齐 Pivot。
- 若仍出现不受热，应优先记录 `CurrentContactRatio`、锅底垂直间隙和食材重叠比例，
  不得分别为模型切换、音效和烟雾增加平行触发条件。

## Phase System Notifications V002 工作汇报：多消息并发展示（2026-10-17）

### 一、功能完成情况

- 将左上角系统提示从“后消息覆盖前消息”改为独立消息列表，支持短时间内多条消息同时
  展示，最新消息位于最上方。
- 每条消息拥有独立 ID 和生命周期；默认展示 5 秒，到期后自动渐隐销毁。
- 新消息以“渐变 + 从上向下滑入”方式出现，已有消息同步向下平滑让位。
- 将淡入与位置重排拆为两个动画时间轴，连续快速触发消息时不会发生透明度跳变。
- 居中的小游戏回合结果保持原逻辑，不受本次左上角消息栈改造影响。

修改文件：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/SystemNotifications.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/SystemNotificationPresenter.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/BowlingTests.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/GameRuntimeTests.cs`

### 二、编译与测试

- 使用 Unity 2022.3.38f1 的 Roslyn 引用集编译通知模块生产源码及其直接依赖，
  结果通过。
- 新增状态测试覆盖新旧顺序、独立过期、默认 5 秒及永久消息；运行期测试覆盖双面板
  布局、最新消息置顶、淡入 CanvasGroup 和 Always-On-Top 材质。
- Unity Test Runner 在当前 TRAE 沙箱中启动时，`bee_backend` 因系统信号量权限报错
  `mono_os_sem_post: semaphore_signal failed with error 15`，未生成测试 XML。已终止并
  确认测试进程退出；需要在非沙箱 Editor 中补跑测试。

### 三、后续验收

- PICO 真机连续触发 3 至 5 条提示，检查 World Space Canvas 中的阅读间距、滑移动效和
  5 秒后渐隐是否符合舒适度预期。
- 如需限制极端消息风暴，应在 `SystemNotifications` 增加明确的最大可见条数策略；
  不应由 presenter 静默丢弃业务消息。

## Phase Electric Kettle V001 工作汇报：电热水壶建模与烧水交互（2026-10-17）

### 一、功能完成情况

- 按参考三视图完成不锈钢电热水壶与独立圆形底座建模，交付 Blender 源文件、壶体 FBX、
  底座 FBX 和预览图；红色开关、壶嘴蒸汽点、壶底触点均为独立命名节点。
- 新增 `ElectricKettleController`、`ElectricKettlePowerButton` 和
  `ElectricKettleBase`，实现空壶拒绝、底座接触门控、累计有效加热 60 秒、白色蒸汽、
  水温增长、自动关机与提示。
- 开关滴声直接复用 `CookingEffects/ButtonBeep`，有效加热循环声直接复用
  `CookingEffects/InductionHum`；离开底座时暂停计时、蒸汽和电流声，放回后继续。
- 扩展 `WaterReceiver` 保存水温；新接入的常温水按体积混合温度，倒空后恢复 20°C。
- 虚拟物品栏新增一个“电热水壶”套装条目，库存上限为 1。放置时生成独立的固定底座与
  可拿取壶体，两个隐藏部件共享同一个库存单元。
- `MateSoul.unity` 的 `GameBootstrap.items` 已注册
  `electric-kettle-body`、`electric-kettle-base` 和 `electric-kettle`。

### 二、关键文件

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/ElectricKettle/source/ElectricKettle_Set_220x245x245mm.blend`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/ElectricKettleBuilder.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ElectricKettleController.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ElectricKettlePowerButton.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ElectricKettleBase.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/ElectricKettleTests.cs`

### 三、编译与测试

- Blender 5.2.1 无界面生成 `.blend`、两个 FBX 和 1280×720 预览图成功。
- Unity Runtime 全量 Roslyn 编译通过；`ElectricKettleBuilder` 与包含当前目录改动的
  `GameSceneBuilder` 编译通过。
- 热水壶单资产构建成功，Prefab、材质和三个 ItemDefinition 均已生成。
- `ElectricKettleTests` 2/2 通过，覆盖套装库存结构、音效绑定、空壶拒绝、底座接触、
  60 秒烧水、100°C 水温和自动关机。
- 结构化报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/electric_kettle_20261017.json`。

### 四、问题与后续

- 全量 `BuildScene` 在热水壶资产完成后被当前咖啡研磨机的
  `CoffeeGroundPowder_126mm` 高度校验中断；该问题与热水壶无关。热水壶已通过独立
  Builder 完成构建与场景注册，本阶段未覆盖新的 Android APK。
- PICO 真机需复核红色开关射线命中、壶体放回底座的接触容差、蒸汽尺度和 60 秒音频
  连续性；不得通过扩大到无条件加热来修正接触手感。

## Phase Coffee Grinding V002 工作汇报：独立咖啡豆与研磨套装（2026-10-17）

### 一、咖啡豆表现修正

- 废弃 `CoffeeBeanCluster`、28 颗合并网格和 12 簇倾倒方案。单颗咖啡豆现在是
  12×7.1×5.28 mm 的独立闭合模型，具有沿长轴延伸的真实凹槽。
- Blender 罐体模型包含 720 个独立豆对象，覆盖整个有效内腔；Unity 使用同一单豆网格
  的 720 个 GPU 实例显示满罐效果，避免 720 个 GameObject 和 Renderer。
- 倾倒容量固定为 336 颗独立物理豆，每次释放 8 颗。每颗豆有独立模型、Rigidbody 和
  Collider，同时动态模拟上限为 96；普通表面落地后延迟 1.5 秒并用 6 秒缓慢缩小消失。

### 二、研磨套装

- 新增 `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/CoffeeGrinder/`，包含
  Blender 源文件、研磨钵/研磨棒/咖啡粉 GLB 与 FBX、6 张 PBR 贴图和 5 张渲染图。
- 研磨钵主体为 180 mm 直径、140 mm 高暖白陶瓷，包含深色研磨内衬、木底座和
  “咖啡豆研磨”中文浮雕铭牌；研磨棒长 255 mm，使用木柄和深色石质研磨端。
- 目录新增 `coffee-grinder`，分类为厨房用品、库存上限 1。隐藏成员
  `coffee-grinder-mortar` 与 `coffee-grinder-pestle` 共享同一库存单元，放置和回收
  均作为一套处理。

### 三、运行时与验证

- 只有 `CoffeeGrinderReceiver` 能接收落豆；豆进入研磨钵后转为容器内独立 GPU
  实例，不参与地面消失流程。
- 研磨棒尖端必须位于容器内部且移动速度达到 0.035 m/s，累计有效搅拌 4 秒后删除
  豆实例、显示咖啡粉隆起层、播放 72 粒粉尘效果，并提示“咖啡粉磨制完成”。
- 已修正咖啡粉高度契约，前序电热水壶记录中的 `BuildScene` 阻断已解除；当前
  Unity 2022.3.38f1 场景重建退出码为 0。
- 专项回归 5/5 通过，覆盖有限 336 颗容量、独立豆物理、缓慢消失、唯一接收容器、
  静止不计时、4 秒研磨、UI 提示，以及套装共同放置和共同回收。
- 结果文件：
  `/Users/bytedance/MateSoul/.artifacts/framework/unity-coffee-grinder-tests.xml`。
- 尚未进行 PICO 真机验收；需重点检查透明玻璃内的实例排序、两瓶同时倾倒的帧率、
  研磨棒移动阈值和粉尘粒子可见度。

## Phase Virtual Items V003 工作汇报：固定放置与视野回收（2026-10-17）

### 一、功能完成情况

- `ItemDefinition` 新增 `ItemPlacementSurface`，支持 `Ground` 与 `Wall` 两类投影。
- 餐桌、饮水机、复古电视、餐椅配置为地面固定放置；时钟和飞镖盘配置为墙面固定放置。
  餐椅已重新加入虚拟物品栏，库存上限为 1。
- 固定放置只禁止玩家抓取和冷月搬运，不再通过 `SetContact(true)` 将 Rigidbody 设为
  Kinematic；物品仍保留碰撞、质量、受力和旋转响应。墙挂物关闭重力，但仍是动态刚体。
- 回收改为两阶段操作：首次点击“回收当前物品”进入回收状态，视野中心物品显示整物体
  高亮；再次点击“确认回收”才执行库存返还与持久化。
- 墙挂物恢复时按墙面支撑校验，避免沿用地面向下探测而误判存档无效。

### 二、关键文件

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemDefinition.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/ItemPlacementPreviewController.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/VirtualItemFocusController.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameItem.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameSession.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs`

### 三、编译与测试

- 本次功能与直接相关既有回归共 8/8 通过，覆盖动态刚体、禁止抓取、墙面限定、
  墙挂存档恢复、整物体高亮、二次确认回收、库存返还、组合物品回收和表面放置。
- 完整 `GameRuntimeTests` 为 30/38；8 个失败来自该测试类既有的顺序依赖或无图形模式
  环境问题，包括角色寻路、脆弱物、每日库存、AI 占用回收和 UI 材质断言。本次相关用例
  隔离运行均通过。
- Android ARM64 / IL2CPP Development APK 构建成功：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- APK SHA-256：
  `c63cb4c8147dd8f382cc3222deac09162b1f7abd95d4905d61777491dab3af01`

### 四、后续验收

- PICO 真机检查墙面投影的深度、朝向和红绿反馈。
- 验证固定家具受碰撞时的质量手感，以及回收模式下远距离小物体的瞄准稳定性。

### 五、水平承载面放置补充（2026-10-18）

- 修复固定物品启用投影后只认主地面的问题。`AllowSurfacePlacement` 物品现在优先使用
  视野射线命中的水平承载面，因此复古电视可放在现实小桌、架子和床面。
- 水平面法线阈值放宽至 `normal.y >= 0.6`，兼容重建网格的小幅噪声和软质床面。
- 可放表面物品的占用检测余量由 30 mm 收敛至 5 mm，并仅忽略当前承载面的碰撞；
  周围其他物体仍会阻止重叠放置。
- 新增 `FixedSurfacePreviewAcceptsSmallTablesShelvesAndBeds`，与原表面放置、墙挂物测试
  合计 3/3 通过。
- Android Development APK 重建成功，路径为
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`。
- APK SHA-256：
  `61c7382d9417c380230b73b742ae2a373577c195d7a4b972fff9bd546e87cba9`

## Phase MR Occlusion V001 工作汇报：现实纵深遮挡（2026-10-17）

### 一、功能完成情况

- 启用 PICO Unity Integration SDK 0.13.1 的 `Environment Depth`，构建时自动写入
  `enable_environment_depth=1` 和 `SPATIAL_DATA` 权限。
- 新增 URP 全屏环境深度合成通道。它在后处理完成后比较 Unity 场景深度与 PICO 双目
  环境深度，将被现实物体挡住的虚拟像素 Alpha 清零，不要求逐个替换角色或道具材质。
- 新增空间网格 Depth-Only 遮挡器。原生 Spatial Mesh 更新时只更新不可见网格及深度，
  不创建 Collider，不与现有导航、角色避障和物品物理争夺所有权。
- 当房间只有 Scene Capture 快照时，使用楼面与障碍体作为稳定遮挡近似；存在原生
  Surface Mesh 时优先使用精确表面，不叠加粗粒度房间体。
- 三个 URP 质量档位均启用 Camera Depth Texture，并登记统一环境深度 Renderer
  Feature；`GameSceneBuilder` 会在后续重建场景时复核并补齐配置。

### 二、关键文件

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RealityEnvironmentOcclusionFeature.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RealityOcclusionMeshRenderer.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/Resources/RealityOcclusion/`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/RealityOcclusionSetup.cs`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/RealityOcclusionTests.cs`

### 三、编译与测试

- `MateSoul.Runtime`、`MateSoul.Editor`、`MateSoul.Tests.Engine` 已使用 Unity
  2022.3.38f1 生成的 Roslyn 响应文件分别编译通过。
- 新增 3 个专项用例，覆盖 Scene Capture 粗网格、Spatial Mesh 优先级、无 Collider
  约束和三个质量档位的 Renderer Feature 配置。
- Unity Test Runner 无法在当前 TRAE 沙箱内启动：Unity 写入
  `~/Library/Application Support/Unity/CoreBusinessMetrics.db` 被阻止，Bee 子进程
  随后以退出码 134 终止。已终止并确认相关批处理 Unity 进程退出。
- 随后的 Android ARM64 / IL2CPP Development 构建成功，两个新 Shader 均进入实际
  Player 编译。APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`；SHA-256：
  `61c7382d9417c380230b73b742ae2a373577c195d7a4b972fff9bd546e87cba9`。
- APK Manifest 已核验 `enable_vst=1`、`enable_mesh_anchor=1`、
  `enable_environment_depth=1` 和 `com.picovr.permission.SPATIAL_DATA`。

### 四、后续验收

- 必须在支持 Environment Depth 的 PICO 真机上验证手、人体、移动实物与冷月/道具的
  前后遮挡；透明、反光和细薄现实物体的边缘仍受深度传感器质量限制。
- 真机重点观察遮挡边缘抖动、头部快速移动时的延迟和空间网格更新成本，再决定是否调整
  `_EnvironmentDepthBias`；不要通过逐材质复制 Shader 处理局部问题。

## Phase Electric Kettle V002 工作汇报：三视图重建与近底座吸附（2026-10-18）

### 一、问题与根因

- V001 模型把壶嘴放在 -Y、提手放在 +X，二者没有沿同一轴线前后对置；壶嘴是方盒楔块，
  提手是圆管，正面、侧面和顶面轮廓均与参考三视图不一致。
- Blender 程序材质无法随 FBX 完整进入 Unity，旧 Builder 又没有绑定金属底色和法线贴图，
  因而生产 Prefab 几乎没有拉丝金属表现。
- 套装放置后壶体仍作为普通动态刚体出生，仅依赖重力和碰撞落到底座，在首帧接触偏心时
  会被底座顶开并倾倒。

### 二、修复

- 重建壶体为旋压收口轮廓，壶嘴与提手改为 -X/+X 对置；壶嘴使用渐缩侧轮廓实体，
  提手使用带内孔的扁平 D 形实体，壶盖改为低矮旋压曲面。
- 新增正面、侧面、顶面三张正交复核图，并生成 512×512 拉丝钢底色与法线贴图。
- URP Lit 金属参数定标为 Metallic 0.94、Smoothness 0.72、Normal Scale 0.42；
  黑色耐热提手、红色开关和底座分别使用独立材质。
- 提手碰撞由覆盖整个内孔的大盒子改成上、后、下三段复合 BoxCollider，壶身继续使用
  CapsuleCollider，壶嘴使用独立 BoxCollider。
- `PhysicsBody` 新增显式 Dock/Undock 状态。水壶触点进入底座 16 cm 范围后，立即选择
  最近底座、归正旋转、重合触点、清零速度并关闭重力；玩家抓取或角色交互前自动解除停靠。

### 三、验证与交付

- Runtime、Builder 和测试源码编译通过。
- `ElectricKettleTests` 2/2 通过，新增覆盖：
  - 倾斜并带线速度/角速度的水壶靠近底座后瞬时吸附；
  - 壶体与底座触点误差小于 1 mm；
  - 停靠后运动学、无重力、速度归零；
  - 开始 Follow 后恢复动态刚体和重力；
  - 金属材质贴图、Metallic 和 Smoothness 已写入生产 Prefab。
- Android Development APK 构建成功：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  139,803,909 字节，SHA-256
  `9c2a6f621c57f7f30df0fe644c929389c6f6312d65fc19f038aa6fe8568345b6`。
- APK ZIP 完整性和 APK Signature Scheme v2 验证通过。
- 结构化报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/electric_kettle_v2_20261018.json`。

### 四、后续验收

- PICO 真机重点检查三视图轮廓、拉丝方向、提手黑度、红色开关命中和 16 cm 吸附手感。
- 吸附阈值只允许在专项测试约束内微调；不得恢复自由落体对位，也不得在水壶控制器之外
  增加第二套 Transform 强制写入逻辑。

## Phase Social Attention V006 工作汇报：接近意图与平滑准入（2026-10-18）

### 一、问题修复

- 取消“进入 0.8 m 就等于希望互动”的直接映射。`GameSession` 现在在约 1.2 秒窗口内
  分别累计玩家和冷月朝向对方的水平位移贡献。
- 玩家接近贡献达到 15 cm 且比冷月高至少 8 cm 时，才建立 Ambient 候选；若候选期间
  冷月贡献反超，则撤销候选。冷月随机行走到静止玩家旁边不会进入社交状态。
- 候选需在 0.8 m 内连续保持 0.7 秒，且角色 Action 空闲、移动停止、水平速度稳定
  0.25 秒后才提交 Social Attention。候选期间暂停创建新的自主行为。
- 退出边界 1.05 m 增加 0.25 秒时间去抖，避免边界噪声反复取消。
- Ambient 注视最多保持 5.5 秒；无对话或交互时自动退出并进入 5 秒冷却。玩家不离开
  退出范围就不能自动重新武装，显式对话仍可绕过距离与冷却。

### 二、动画过渡

- `SocialAttentionAction` 新增 `noticing-player` 阶段。Action 开始后先只渐入头、颈和
  眼睛跟随 0.45 秒，之后才设置 Animator `SocialAttention=true` 并 CrossFade 到
  专用 Idle，避免从上一动作瞬间切换。
- 退出时缓存最后一次头部目标角并随 Rig 权重渐出，修复目标清空后空引用，同时保留
  自然回正过程。
- 正式 Action 与情绪动画的优先级和 All 通道抢占规则保持不变。

### 三、验证与交付

- 玩家主动靠近、Notice、身体后跟、对话抢占与恢复：1/1 通过。
- 冷月自身靠近静止玩家不触发：1/1 通过。
- 0.79/0.82 m 边界抖动、Ambient 超时和静止不重入：1/1 通过。
- 测试证据：
  `/Users/bytedance/MateSoul/.artifacts/social-attention-v6/`。
- Android Development APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  139,803,909 字节，SHA-256
  `9c2a6f621c57f7f30df0fe644c929389c6f6312d65fc19f038aa6fe8568345b6`。
  ZIP 完整性与 APK Signature Scheme v2 验证通过。

### 四、后续验收

- PICO 真机观察玩家自然走近时约 0.7 秒候选加 0.45 秒 Notice 的总体响应是否偏慢。
- 若需微调，只调整候选、Notice 和 Ambient 时长；不得重新用距离直接提交 Action，
  也不得取消超时后的离开重武装规则。

## Phase MR Occlusion V002 工作汇报：模拟器崩溃隔离与真机能力保留（2026-10-18）

### 一、问题与根因

- 最新包在 PICO Emulator 启动后长期停留在黑色加载界面，约 8 秒后 Unity 进程退出。
- Tombstone 确认是 `libPxrPlatform.so` 的
  `METAEnvironmentDepth::GetEnvironmentDepthTextureId()` 空指针 `SIGSEGV`。
- 模拟器 OpenXR Runtime 返回 `XR_ERROR_FUNCTION_UNSUPPORTED`，但
  `PXR_EnvironmentDepthManager.Update()` 仍继续读取 Environment Depth 纹理。

### 二、修复

- `RealityOcclusionMeshRenderer` 只在 Android Build identity 明确包含 `emulator` 时禁用
  `PXR_EnvironmentDepthManager`；删除可能误伤 Swan 真机的 `swan_arm64` 判定。
- `RealityEnvironmentOcclusionFeature` 在模拟器不提交 Environment Depth 合成 Pass。
- 模拟器继续保留 Spatial Mesh / Scene Capture Depth-Only 遮挡；支持能力的 PICO 真机
  继续启用动态 Environment Depth。
- 修复 Development 诊断中 `TMP_FontAsset.HasCharacters()` 返回空 missing 数组时的
  `ArgumentNullException`。
- 启动脚本新增 Environment Depth、Spatial Mesh Manifest 强校验，并把启动存活观察
  窗口从 5 秒延长到 12 秒。

### 三、验证与交付

- Reality Occlusion 专项测试 6/6 通过；`swan_arm64` 不判为模拟器，两类明确 emulator
  标识均正确命中。
- Android Development APK 构建成功，ZIP 完整性通过，Manifest 保留
  `enable_environment_depth=1`、`enable_mesh_anchor=1` 和 `SPATIAL_DATA` 权限。
- APK：`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`，
  139,805,097 字节，SHA-256
  `61f74a019509fb8aa0b6fd9f3be26ddad2f11e2e2dbd3247b60ef8a3ee800093`。
- 模拟器冷启动输出 `MATESOUL_READY`，角色、房间和 HUD 正常显示；PID `3227`
  连续运行超过 40 秒，无 `GetEnvironmentDepthTextureId`、`SIGSEGV` 或字体诊断异常。

## Phase Electric Kettle V003 工作汇报：底面封闭与吸附范围收敛（2026-10-18）

### 一、问题修复

- 水壶壶底新增实体封面 `Kettle_BottomPlate`，修复从下方观察时因开放网格导致的透明。
- 加热底座新增底面 `KettleBase_BottomPlate` 和顶面 `KettleBase_Deck`，补齐底座缺面。
- 封面使用实际 Cylinder 网格，不依赖双面材质，确保 Unity 背面剔除下仍正确显示。
- 水壶自动吸附半径由 0.16 m 缩短为 0.12 m，减少离底座较远时提前跳转。

### 二、资产与测试

- 重建 Blender、FBX 和 Unity Prefab，底部检查图：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/ElectricKettle/source/ElectricKettle_Set_bottom.png`。
- `ElectricKettleTests` 2/2 通过，生产资产断言同步约束 0.12 m 吸附半径。
- 结构化验证：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/electric_kettle_v3_20261018.json`。

### 三、Android 交付

- Android Development APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- 文件大小：139,819,389 字节。
- SHA-256：
  `d0a495627d5c008fcc1efc703afd547ded694715db82173352ec73fec43c9c97`。
- APK ZIP 完整性和 APK Signature Scheme v2 验证通过。

### 四、后续验收

- PICO 设备上从低角度检查水壶和底座，不应再看到透明孔洞。
- 实际抓放时确认 12 cm 吸附既不会过早触发，也能覆盖正常放置误差。

## Phase Electric Kettle V004 工作汇报：金属材质黑图修复（2026-10-18）

### 一、根因

- Prefab Renderer、URP Lit Shader、Metallic 0.94 和 Smoothness 0.72 均未丢失。
- `kettle_brushed_steel_basecolor.png` 与法线 PNG 被 Blender 5.2
  `Image.save()` 静默写成全黑图，白色材质与黑色 Base Map 相乘后导致壶体呈黑色。
- 旧测试只断言贴图引用非空，无法发现“文件存在但像素全黑”的内容损坏。

### 二、修复

- `build_electric_kettle.py` 改为直接编码标准 RGBA PNG 数据块，不再依赖 Blender
  生成图像保存 API。
- 重建银灰拉丝底色与法线贴图；底色 Y 平均亮度由 16 恢复为 142.77。
- `ElectricKettleBuilder` 在资产构建前解码 PNG，强制校验底色平均亮度和法线蓝通道。
- `ElectricKettleTests` 同步加入实际像素校验，黑图不能再通过专项测试。

### 三、验证与交付

- 电热水壶专项测试 2/2 通过。
- Android Development APK：139,947,609 字节。
- SHA-256：
  `e7307de4c23fb7887250cf279f4652ba5d749d016308da703264914f21b94c4a`。
- APK ZIP 完整性和 APK Signature Scheme v2 验证通过。
- 结构化报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/electric_kettle_v4_20261018.json`。

### 四、后续验收

- 当前 PICO Emulator 未连接，需启动后覆盖安装最新 APK，再从物品栏生成水壶检查反射。
- 以后程序生成的材质贴图必须校验实际像素统计，不能只验证文件或材质引用存在。

## Phase Coffee Bean Jar V002 工作汇报：物理预算与分体盖交互（2026-10-18）

### 一、性能优化

- 咖啡豆库存与场上物理解耦：罐体仍保持 336 颗逻辑容量和 GPU Instancing 填充表现，
  每次释放 8 颗逻辑豆时只生成 2 颗代表性物理豆。
- 新增 24 实例固定对象池。倾倒全过程不再持续 `Instantiate/Destroy`，超过预算时循环
  复用最早的代表豆，因此刚体、碰撞体、Renderer 和逐帧组件数量均有确定上限。
- 对象池位于罐体同级生命周期节点，避免预热实例被根 `PhysicsBody` 误判为复合刚体。
- 咖啡豆落地消失或进入研磨杯后回池；清理 kinematic 刚体时不再写入速度，专项日志中
  相关 Unity 警告为 0。

### 二、分体式盖子

- 木盖、密封圈、Collider、`Rigidbody`、`PhysicsBody` 和抓取组件组成独立物理件。
- 盖子受超过 0.08 N·s 的冲击或被玩家抓取时解除停靠；脱离后不再跟随罐体。
- 玩家松手后，盖子仅在距罐口 1.8 cm 内、姿态误差不超过 14°、线速度和角速度足够低
  且经过 0.35 秒冷却时瞬时归位，避免远距离或运动中的明显吸附。
- 独立盖子与罐体 Collider 互相忽略，防止停靠重叠产生自碰撞抖动。

### 三、资产与验证

- `CoffeeBeanJarBuilder` 已重建生产 Prefab，Prefab 包含两个独立 Rigidbody、罐口 Socket、
  24 物理实例上限和每批 2 个代表豆参数。
- Unity 2022.3.38f1 编译通过。
- `CoffeeBeanJarTests` 2/2 通过：
  - `ProductionAssetKeepsFiniteCapacityAndSingleBodyContracts`
  - `LidDetachesSnapsAndPouringUsesBoundedPhysicalPool`
- 测试覆盖 336 逻辑容量耗尽、24 实例硬上限、冲击阈值、独立刚体层级和 1.8 cm 回吸附。

### 四、后续建议

- 在 PICO 模拟器和真机用 Profiler 对比连续倾倒前后的 Physics、Scripts、GC Alloc 和帧时；
  当前已验证资源上限与无运行时实例化，尚未形成设备侧数值性能报告。
- 真机仅允许小范围微调 1.8 cm 吸附距离、14°姿态阈值和 0.35 秒冷却，不得恢复远距离
  拉拽动画，也不得重新为 336 颗库存创建独立刚体。

## Phase Virtual Items V004 工作汇报：全平面投影与不可移动标记（2026-10-18）

### 一、功能完成情况

- 所有虚拟物品栏中的可见物品统一启用放置投影，不再只有固定家具显示投影。
- `AllowSurfacePlacement=true` 的物品优先使用视野中心命中的水平表面，可放在现实或
  虚拟桌子、架板、床面等 Collider 上；没有命中合法表面时仍可回退到主地面。
- 水平承载面允许 `normal.y >= 0.6` 的扫描误差，占用检测余量从 30 mm 放宽至 5 mm，
  并仅忽略实际承载 Collider，其他物体重叠仍会显示红色并拒绝放置。
- `ItemDefinition` 新增 `ImmovableAfterPlacement` 标记。餐桌和饮水机率先启用，放置后
  由 `PhysicsBody` 的 Contact 状态锁定为 Kinematic，不响应碰撞、外力或力矩。
- `FixedAfterPlacement` 与“不可移动”拆分：前者只限制玩家/冷月直接搬运，复古电视、
  椅子、时钟、飞镖盘仍可保留动态物理。

### 二、验证与交付

- 场景资源重建成功；所有可见目录物品均为 `placementPreview=1`。
- 全项目仅 `table`、`water-dispenser` 为 `immovableAfterPlacement=1`。
- 专项测试 7/7 通过，覆盖现实小桌、架子、床面、虚拟餐桌、墙面排除、动态固定物与
  不可移动物的物理差异。
- Android ARM64 / IL2CPP Development APK 构建成功：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- APK SHA-256：
  `a79103dfb1d7a414fc55a8a4b5032f41965481f35a7e389742f397bb755cbf3d`

## Phase Virtual Items V005 工作汇报：投影范围纠正（2026-10-18）

> 本节覆盖 Phase Virtual Items V004 中“所有可见物品均启用投影”的旧描述。

### 一、规则纠正

- 放置投影只属于 `ImmovableAfterPlacement=true` 的不可移动物品，不再由目录可见性
  或普通表面放置能力触发。
- 纸杯蛋糕、平底锅、平底锅加热底座及其他普通小物件均不打不可移动标记，也不显示
  放置投影，继续沿用直接放置流程。
- 当前不可移动并显示投影的物品为餐桌、饮水机、时钟、飞镖盘、复古电视和餐椅；
  其中时钟、飞镖盘使用墙面投影，其余使用地面或已允许的水平承载面。
- `ItemDefinition.ConfigurePlacement()` 强制
  `PlacementPreview == ImmovableAfterPlacement`，从数据入口阻止错误组合。

### 二、验证与交付

- 场景重建后检查确认：投影资源集合与不可移动资源集合完全一致，共 6 项。
- 明确检查纸杯蛋糕、平底锅、平底锅加热底座：
  `placementPreview=0`、`immovableAfterPlacement=0`。
- 专项测试 8/8 通过。
- Android Development APK 构建成功：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- APK SHA-256：
  `43199713d0791421c0fa5091e88360ef5b3f7a75fbae133ad3cc4ce0a0641d56`

## Phase Virtual Items V006 工作汇报：电视与收音机正面朝向（2026-10-19）

### 一、功能修正

- 确认复古电视与复古收音机资源均以本地 `-Z` 为模型正面，而通用放置逻辑默认让本地
  `+Z` 朝向玩家，导致放置后背面朝向玩家。
- `ItemDefinition` 新增 `PlacementFacingYaw` 数据配置以及统一的
  `ResolvePlacementRotation()`，不在放置协调器中按物品 ID 写分支。
- 电视、收音机配置 `placementFacingYaw=180`；地面投影、水平承载面投影和普通直接
  放置三条路径均复用该旋转解析。
- 其他物品默认偏移为 0，不改变既有朝向。

### 二、验证状态

- 代码与 YAML 静态一致性检查通过；电视、收音机资产均已写入 180° 朝向偏移。
- 新增测试断言：电视在小桌、架子、床面放置后，本地 `-Z` 必须指向玩家；电视与
  收音机生产定义均要求 `PlacementFacingYaw=180`。
- Unity 当前返回 `No valid Unity Editor license found`，场景重建、专项测试和 Android
  APK 构建尚未执行。本阶段不得将上一版 APK 视为包含该修复。

## Phase Classified Grip V002 工作汇报：掌心穿模与人体工学手指修复（2026-10-18）

### 一、真机反馈与根因

- PICO 复测确认 V001 仍有严重掌心穿模，苹果模型中心几乎与掌心中心重合；不同类型物品
  的手指关节仍存在明显多轴扭曲和吸附感。
- 运行时证据确认苹果体积中心位于 80.95 mm 掌段内部的 44.52 mm 处，距掌轴约 0，
  保守估算掌体侵入至少 63 mm。
- 旧手指 CCD 围绕错误的掌内锚点求表面，`fingerCurl` 未进入关节公式；每节骨骼使用
  自由三维 `FromToRotation`，中节和末节出现几十度侧摆与扭转。

### 二、修复内容

- `ItemInteractionProfile` 新增 `gripShapeOffset`，把虚拟掌心锚点和真实握持体积中心拆开。
- `ItemActionContentBuilder` 使用 Prefab 实际 Bounds 写入尺寸。苹果当前为
  `90×103×90 mm`，掌锚点 Y 为 124 mm，体积中心偏移 Y 为 -72.5 mm。
- 普通物、手柄、环柄和双手物均按支撑半径、Profile 间隙及 18 mm 掌垫厚度把物体移到
  掌外；薄物边缘前移 70 mm 到拇指/食指区域。
- 手指目标使用椭球、圆柱和薄片表面的解析投影。闭合量由物品尺寸、目标距离和
  `fingerCurl` 联合计算。
- MCP 只允许受限外展；MCP/PIP/DIP 只围绕生理屈曲平面旋转。每手最多 50 次固定关节
  运算，不执行 Physics 查询，Player 路径无逐帧分配。

### 三、数值验证

- 苹果：掌间隙 `+9.50 mm`，最小指尖间隙 `+2.24 mm`，表面目标误差从
  `65.83 mm` 降到 `12.20 mm`。
- 刀柄：掌间隙 `+2.00 mm`，最小指尖间隙 `+2.15 mm`。
- 薄盘：掌间隙 `+3.07 mm`，最小指尖间隙 `+0.75 mm`，不再穿入。
- 主工程 Profile `4/4`、主工程 Plate 抓握 `1/1`、隔离 Item Action `4/4`、
  共享回归 `27/27` 通过。
- 主工程内容构建覆盖当前 41 个 Profile。

### 四、构建与遗留

- Android Development ARM64/IL2CPP 构建成功：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `43199713d0791421c0fa5091e88360ef5b3f7a75fbae133ad3cc4ce0a0641d56`
- ZIP 完整性、仅 `arm64-v8a` 和 APK Signature Scheme v2 验证通过。
- 主工程完整 Item Action 还暴露并行物理改动的独立问题：取消后的盘子曾坠落到
  Y=-125.787 m，另有一次切割后返回路径阻挡。抓握专项、隔离完整动作与共享回归均
  通过，因此本次没有放宽 25 cm 吸附准入，也没有混入物理恢复修复。
- 调试会话继续保持 `[OPEN]`，等待 PICO 真机近距离检查苹果、刀柄和盘子的掌指关系。

## Phase Full-body Reach V003 工作汇报：连续姿态与单段切水果（2026-10-18）

### 一、问题与修复

- 删除 `FloorReach/TableReach` 的 0.35 m 硬切换，统一为 `Reach/Carry/Cut` 三态。
- `AvatarItemRig` 新增髋、脊柱、颈、头和双腿十骨绑定，根据目标高度、水平距离及手臂
  剩余可达量连续分配前倾、下蹲和重心前移；双脚取样后通过腿部 IK 回钉。
- 头颈反向补偿躯干前倾。地面目标使用深蹲加髋折叠，中位目标使用受限膝髋协同，
  高位目标保持手臂主导，不再对桌面目标应用固定 42° 弓腰。
- `ItemActionContentBuilder` 不再给 Reach/Cut 动画烘焙固定髋、脊柱和腿曲线，并清除
  旧 `FloorReach/TableReach` 控制器状态和动画资产。
- 切水果改为同一站位的一次 12 cm 抬刀/下切手势，手势结束后原子提交四块苹果。
  已删除真实刀刃穿越门槛、第二刀、90° 绕行和返回路径；刀具在释放边界精确回原位。

### 二、数值与测试

- 生产角色四档目标测试通过：地面前倾约 37°、下蹲约 0.54 m，头部剩余俯仰约 6.7°；
  高位前倾约 0.84°且不下蹲；脚底漂移低于 0.005 m。
- 地面、低位、桌面和高位目标均保持在 25 cm 抓取准入范围内。
- `ItemActionRuntimeTests` 5/5 通过，完整覆盖取消恢复、抓握、连续姿态、单次切割、
  四块装盘与库存清理。
- 共享批次 31/33 通过。失败的两项为当前并行 `AvatarMotor` NavMeshAgent/物理绕行
  改动导致的导航用例，隔离复跑 0/2，未混入本阶段修复。

### 三、构建与交付

- Android Development ARM64/IL2CPP 干净构建成功：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `564dd240c1431b7db959fcd35c91dc3e4058371c8894c57a55c802fa5b37d40b`
- ZIP 完整性、仅 `arm64-v8a` 和 APK Signature Scheme v2 均验证通过。
- `item-posture-cut` 与 `wrist-grip-follow` 调试会话、插桩和日志继续保持，等待 PICO
  真机确认桌面头部间距、地面深蹲观感及单次切割节奏。

## Phase Reach Reliability V004 工作汇报：低位弯腰、双层吸附与餐桌穿透修复（2026-10-18）

### 一、问题根因与修复

- 极低位拾取并非没有脊柱前倾，而是旧参数最多下蹲约 0.63 m，净躯干轴仅
  25–29°，深蹲在视觉上压过了弯腰。现将极低位姿态重新分配为约 0.41–0.47 m
  下蹲和 51–57°请求前倾，并继续保留头颈补偿、双腿求解与脚底锁定。
- 拾取准入由单一 0.25 m 门槛改为双层范围：0.25 m 内为主吸附，
  0.25–0.45 m 为兜底吸附，超过 0.45 m 仍故障闭合拒绝。普通搬运和切割拿刀共用
  `ItemTransferStep.CanSnapPickup()`，没有物品名称分支。
- 切水果启动时穿桌的根因是 `CutFruitAction.BuildSteps()` 对固定餐桌调用
  `GameItem.Stabilize()`；该方法进入手持物 `PhysicsBody.BeginInteraction()`，
  会把餐桌 `detectCollisions` 设为 false，导致桌上动态盘子、苹果和刀同时失去支撑。
  现删除餐桌 Stabilize，仅保留预约锁；餐桌不再进入手持物理状态。

### 二、运行时证据与测试

- 低位样本：请求前倾 `51.45°`、净躯干轴 `35.01°`、下蹲 `0.414 m`。
- 运行时成功接受 `0.34 m` 和 `0.364 m` 误差并分类为 fallback；大于 `0.45 m`
  的输入仍拒绝。
- Action 预约前后餐桌均为 `detectCollisions=true`、`controlled=false`；完整切水果
  链路中工作台持续提供支撑。
- `ItemActionRuntimeTests` 6/6 通过。
- 共享回归 31/34；三项失败均位于未修改的 `GameRuntimeTests`：两项
  `AvatarMotor` 导航/避障用例和一项 HUD 展开断言。本阶段未扩展修改范围。

### 三、构建与交付

- Android Development ARM64/IL2CPP 构建成功，耗时约 4 分 59 秒：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `3db0cc41286bfbd64ca1de867b0ae44c146e96ce6298e72ec8cf444e07e76eca`
- ZIP 完整性、仅 `arm64-v8a`、`libil2cpp.so`/`libunity.so` 存在及 APK
  Signature Scheme v2 验证通过。
- `low-reach-table-fall` 调试会话保持 `[OPEN]`，等待 PICO 真机确认低位姿态、
  兜底拾取成功率和切水果启动时桌面道具稳定性。

## Phase Navigation Stall V005 工作汇报：床角/物理障碍卡死修复（2026-10-18）

### 一、事实与根因

- `AvatarMotor` 当前工作树相对仓库基线存在 NavMeshAgent、原地转身门控和物理切向
  避障重写；用户反馈角色疑似卡在床角后，两项导航回归可稳定复现异常。
- 运行时证据排除了路径无效和 Agent 停止：路径持续为 `PathComplete`，Agent
  `isStopped=false`，并持续输出约 `1.1 m/s` 期望速度。
- 根因是物理避障请求 90°切向移动后，`GateMovementForTurn()` 在朝向未完成时将
  `desiredVelocity` 清零，但 1.25 秒避障保持时间仍继续消耗。角色尚未转到绕行方向，
  避障就失效并恢复前推，形成反复撞角、转身、归零的循环。

### 二、最小修复与验证

- 仅在 `physicalAvoidanceSeconds > 0 && waitingForFacing` 时暂停避障保持时间消耗；
  角色转到退出角后再开始计算 1.25 秒横向绕行窗口。
- 8 秒物理避障总失败预算继续累加，保留故障闭合，避免无限恢复。
- 修复前纯物理障碍用例停在 `x=1.05`，横向位移近似 0，动作 12 秒后仍为 Running。
- 修复后角色以约 `0.99 m/s` 横移到 `z=-1.20`，脱离障碍后回到 Agent 路径并到达目标。
- NavMeshObstacle 与纯 Collider 绕行专项 2/2 通过；与 Item Action 联合回归 8/8
  通过。
- 无限墙仍正确判定移动失败；其最后一帧 Idle 动画权重断言仍失败，属于独立动画
  收尾问题，不是路径或碰撞卡死。

### 三、构建交付

- Android Development ARM64/IL2CPP 构建成功：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `b26c46ff2ba642c6a3549bcbe4f5c7a291ab90d4f897af959b9992bba2bcec9f`
- ZIP 完整性、仅 `arm64-v8a` 和 APK Signature Scheme v2 验证通过。
- `avatar-bed-corner-stall` 调试会话保持 `[OPEN]`，等待 PICO 场景复测床角绕行。

## Phase Handle Grip V006 工作汇报：切水果提示收敛与手柄环握修复（2026-10-18）

### 一、通知边界

- 切水果成功取得整组物品预约后只播报一次“冷月正在切水果。”，不再为搬盘子、搬苹果、
  拿刀和装盘等内部步骤逐条发送系统提示。
- 注视和悬停预约物品不再播报“冷月正在使用 xxx”；仅当 XRI 选择输入已经激活、玩家
  实际尝试抓取该预约物品时提示。玩家主动回收预约物品时的反馈继续保留。

### 二、手柄抓握根因与修复

- 运行时刀具样本中，刀柄半径约 12 mm、掌心净距约 2.00 mm，排除刀具中心或轴向
  配置错误。异常集中在共享五指目标：无名指 PIP/DIP 约 103°/91°，小指净距约
  22.50 mm 且仅约 32°/21°，没有形成环握。
- 根因是共享 `Handle` 分支使用当前指尖到圆柱中心的径向向量反推目标，形成姿态
  自反馈，并且没有区分四指包覆与拇指对掌。
- 新算法按各指指根确定手柄轴向位置；食指到小指分布在圆柱下半面，拇指落在对侧
  上半面。刀、平底锅、锅铲等所有 `Handle` 共用该模型，不增加物品名称特判；
  `LoopHandle` 钩握保持原逻辑。

### 三、验证与交付

- `MateSoul.Runtime` 全量 Roslyn 编译通过；通知 PlayMode 回归与刀/平底锅共享手柄
  几何回归源码编译通过。
- 当前 TRAE shell 直接运行 Test Runner 时，Bee IPC 仍因
  `mono_os_sem_post error 15` 退出；通过 LaunchServices 完成真实 Editor 编译、
  ARM64 IL2CPP 和 Gradle Development 构建。
- APK：`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `cd7872fa68a4bacdfe51c50d3361a87d22c8788fcc1af4c0be4fc9b41b3619c4`
- ZIP 完整、仅 `arm64-v8a`、`application-debuggable` 且 APK Signature Scheme v2
  验证通过。
- `knife-handle-grip` 调试会话保持 `[OPEN]`，等待 PICO 复测刀具四指环握、拇指对掌
  和手腕方向后再清理插桩与日志。

## Phase Item Action V007 工作汇报：抓握稳定、目标注视与连续放置（2026-10-18）

### 一、功能与根因修复

- 刀柄抓握不再使用与角色骨轴不匹配的固定角度。Handle 现在根据圆柱稳定接触点执行
  受限外展和 MCP/PIP/DIP 屈曲，并缓存首帧局部关节姿态；刀具指尖最大误差由约
  45.4 mm 降至约 27.4 mm，后续帧不再重复求解或分配内存。
- 盘子抽动不是 Handle 修改的副作用，而是 ThinEdge/Pinch 目标自反馈产生多解切换。
  Pinch 改为共享接触点、受限求解和首帧缓存，连续关节样本保持稳定，同时通过
  指尖误差小于 40 mm、穿透不超过 5 mm 的既有门槛。
- 普通搬运 Place 阶段持续驱动物品 Pose，在位置误差 25 mm、旋转误差 8°以内才允许
  Release；切割后归刀路径也接入 held pose override，并以 5 mm / 2°作为最终释放门控。
- Action 停步操作时启用 Idle03 body-only 状态，并由头颈和双眼追踪当前盘子、苹果、
  刀或切割目标；行走和 Action 结束时关闭。
- Action 构建前检查盘子、苹果和刀是否已在餐桌工作位且有支撑；满足水平 12 cm、
  垂直 8 cm 和朝向约束时跳过对应搬运步骤。

### 二、证据与回归

- Pre-fix Release 跳变：盘子约 207.95 mm、刀约 182.54 mm；post-fix 普通搬运
  盘子、苹果、刀和 4 个苹果块均为 0 mm。
- 连续性测试额外发现切割后归刀曾跳变 157.37 mm；修复后完整链路
  Held→Staged 最大位移小于 2 mm。
- 新增已就位跳过、Idle03/目标注视、刀柄姿态稳定、盘子姿态稳定和释放连续性断言。
- `ItemActionRuntimeTests` 全量 9/9 通过；真实角色完整切水果链路通过。
- `ItemGripProfileTests` 5/5 通过，覆盖刀刃方向、掌心净距、共享 Handle 接触模型和
  双手锚点。

### 三、构建与交付

- Android Development ARM64/IL2CPP 构建成功，耗时约 4 分 14 秒：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `b6ea6f1e0c05c9b2d60074efbd7816a9a9899a6924ceaacd31492262a9d0f2da`
- ZIP 完整，仅 `arm64-v8a`，包含 `libil2cpp.so` 与 `libunity.so`；
  `application-debuggable` 和 APK Signature Scheme v2 验证通过。
- `item-action-jitter` 与 `knife-handle-grip` 调试会话继续保持 `[OPEN]`，等待 PICO
  真机确认手势外观、盘子稳定性、Idle03 注视和放置连续性后再清理。

## Phase Held Attachment V008 工作汇报：物品离手悬空根治（2026-10-18）

### 一、事实与根因

- Pre-fix 运行时共采集 647 条事件。正常手部派生跟随的 320 个样本中，物品抓握点
  到手部最大仅 `3.053829e-7 m`；进入 `held pose override` 后，盘子最大偏离
  `0.1612654 m`、苹果 `0.1405476 m`、苹果块 `0.1743947 m`、刀
  `0.4075509 m`。
- Profile 往返位置误差不超过 `1.49e-8 m`，Rigidbody 与 Transform 误差约
  `1.2e-7 m`，排除资产配置和物理双写。
- 根因是 V007 为消除 Release 跳变而让 Action 直接驱动物品世界 Pose。手臂 IK
  到达人体工学边界后，物品仍继续移动，因此出现闭合手势留在原处、物品悬空。

### 二、修复

- 删除 `AvatarItemRig.SetHeldPoseOverride()` 及对应状态；HeldByCharacter 期间物品
  只能由真实 `HandGrip/LeftHandGrip` 派生。
- 新增 `GripForHeldPosition()`：放置和切割只驱动物品中心目标，保持当前可达手腕
  朝向，不再强迫圆盘/苹果匹配无意义的绝对世界旋转。
- 物品中心到位后先松手并进入 `StagedForAction`，再以受控 Pose 在原地平滑转正，
  最后恢复碰撞；刀具归位采用相同生命周期。
- 新增完整链路不变量：所有 `HeldByCharacter` 物品的 WorldGrip 与 HandGrip 距离
  必须持续小于 5 mm。

### 三、验证与交付

- Post-fix 885 个 Held 样本覆盖盘子、苹果、刀和苹果块，最大手物间距
  `3.998401e-7 m`；比修复前最大 40.76 cm 降低约六个数量级。
- 普通搬运 Release 位置跳变均为 0；真实角色完整切水果链路通过。
- `ItemActionRuntimeTests` 全量 9/9 通过。
- Android Development ARM64/IL2CPP 构建成功，耗时约 8 分钟：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `e03295cce5a933c04f59f4231749ca13e90dc0d293a1002ad48bdbfc2ece476c`
- ZIP 完整、仅 `arm64-v8a`、包含 `libil2cpp.so`/`libunity.so`、
  `application-debuggable` 且 APK Signature Scheme v2 验证通过。
- `held-item-hand-gap` 调试会话保持 `[OPEN]`，等待 PICO 实机确认后清理。

## Phase Table Dual-Side V009 工作汇报：餐桌双长边动态选侧（2026-10-19）

### 一、功能实现

- `CutFruitAction` 不再固定使用餐桌局部 Z 负方向长边。Action 准入阶段同时评估
  `-Z/+Z` 两条长边。
- 每个候选侧必须让盘子位、苹果工作位和刀具位三个 Dock 全部满足 NavMesh 采样、
  完整路径及角色胶囊占位；两侧均可用时选择三条路径总长度更短的一侧。
- 选定对侧后，盘子位、苹果位、刀位、切割 Dock 和物品目标朝向统一绕餐桌中心
  旋转 180°。选侧结果保存在本次 Action 内，执行中不会因角色移动反复换边。
- 两侧均不可用时明确拒绝并提示“餐桌两条长边都无法使用”，不再只报告固定侧失败。

### 二、回归覆盖

- 新增 `BlockedDefaultLongEdgeUsesOppositeTableSide`：测试导航层封锁默认长边 Dock，
  将盘子、苹果和刀预置在镜像工作位，Action 必须选择对侧并直接进入“切开苹果”。
- Runtime 与新增测试源码均通过 Unity Roslyn 编译。
- Unity Test Runner 在加载测试前被 Personal License IPC 握手阻塞：
  `No ULF license found / Token not found in cache`。该失败发生在脚本编译和测试执行前，
  因此本阶段不能声明 PlayMode 用例已运行通过。

### 三、交付状态

- 本阶段代码已完成，但尚未生成包含 V009 的新 APK；现有
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk` 仍是 V008 产物，不得
  用于验证餐桌双侧选路。
- 授权恢复后应优先运行 `ItemActionRuntimeTests` 全量，再构建 Development
  ARM64/IL2CPP APK 并更新哈希。

## Phase Handle Grip V010 工作汇报：Power Grip 骨轴重构（2026-10-19）

### 一、问题与影响面

- PICO 截图确认刀具仍被拇指/食指捏持，中指、无名指和小指没有形成整掌包覆。
- 运行时样本中刀柄轴/手掌轴点积约 1、掌心净距约 2 mm，排除刀具位置和轴向配置。
  但食指、中指、小指离刀柄表面分别约 18.6、19.0、20.9 mm，仅拇指轻微穿入，
  证明旧指标用最小净距掩盖了其余手指悬空。
- 缺陷位于共享 `Handle` 姿态生成器，因此 Knife、FryingPan、FryingPanBody、
  Spatula 都受影响。Spoon 与 Chopsticks 也曾误用 Handle + Power。

### 二、修复

- 四指不再使用世界空间指尖 CCD。新求解从 bind localRotation 出发，PIP/DIP 只沿
  真实局部 Z 屈曲轴旋转；MCP 除局部 Z 屈曲外，只允许 `±24°` 的有限内收/外展。
- 求解器在受限关节角范围内选择最接近圆柱接触点的确定姿态，兼顾不同指长、柄径和
  四指横向间距；首帧求解后缓存，持握期间不逐帧抖动。
- Blender 5.2 离线读取 `Lengyue_LayeredDress.fbx`，确认四指 12 个关节的局部 Z
  均为一致屈曲轴；Thumb 为复合轴，继续使用独立对掌求解。
- 拇指接触位置从手柄轴向 15% 调整到至少 55%，移到食指/中指区域，避免护手旁捏圈。
- Spoon/Chopsticks 保留细长 Handle 几何，但改用 Pinch；Pinch 分支优先于
  Handle Power 分支。
- 新增 `MaximumFingerClearance`，刀具完整动作要求所有手指最大净距小于 6 mm。

### 三、验证状态

- 固定角度方案在真实角色上得到 `41.998 mm` 指尖误差和 `25.88 mm` 最大离柄；
  单纯反转屈曲符号恶化到 `105.533 mm / 59.21 mm`。加入 MCP 有限内收/外展后，
  最终稳定为 `10.06 mm / 4.73 mm`，且最小净距仅 `-0.12 mm`。
- Unity `ItemGripProfileTests` 6/6、完整切水果单用例 1/1、
  `ItemActionRuntimeTests` 全量 11/11 通过。全量同时覆盖 V009 餐桌对侧选路。
- 四个离线近景视角确认旧的拇指/食指捏圈和三指伸直已消失，四指形成连续包覆；
  仍需 PICO 真机按用户原观察角度进行最终验收。
- Development ARM64/IL2CPP 构建成功：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `f262e8aea8b30b0c12096f7c460e6f2a4fdff161a4b0a07fca700860b4709b34`
- APK ZIP 完整、仅 `arm64-v8a`、包含 IL2CPP/Unity native libraries、
  `application-debuggable` 且 APK Signature Scheme v2 验证通过。
- 当前 ADB 无设备；`knife-handle-grip` 保持 `[OPEN]`，等待真机确认后清理诊断插桩
  和临时视觉证据。

## Phase Virtual Items V008 工作汇报：回收高亮与离散时钟（2026-10-19）

### 一、高亮边界

- 普通视角选中逻辑保持原样：只高亮按钮、旋钮、盖子等通过
  `ItemInteractionFocusTarget` 声明的可交互组件，不对整物体加轮廓。
- 回收模式使用独立运行时材质，才对被选中的整件物品高亮。
- 普通交互材质保持原参数：轮廓宽度 0.003 m、强度 0.65、透明度 0.7。
- 回收高亮参数提升为：轮廓宽度 0.007 m、强度 1.25、透明度 0.96；退出回收模式后
  恢复每个 Renderer 原材质，不污染普通交互状态。

### 二、时钟改造

- Clock Prefab 新增且仅新增 `12`、`3`、`6`、`9` 四个 3D 表盘数字。
- 分针按整数分钟离散更新，每跨一分钟旋转 6°；秒和毫秒不参与计算。
- 时针按每 5 分钟一个时间桶更新，每次旋转 2.5°，不再跟随分钟连续小角度移动。
- 指针仍由 `DateTime.Now` 提供现实本地时间。

### 三、验证与交付

- 场景资源重建成功，Clock Prefab 检查确认只有四个数字。
- 专项测试 3/3 通过，覆盖普通组件高亮边界、回收整物高亮强度、时钟数字和离散跳动。
- Android ARM64 / IL2CPP Development APK 构建成功：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- APK SHA-256：
  `52560816681b325953f58001aab0af089210a12389e9ba0fe07f3c5a11087e2f`

## Phase Item Attention V011 工作汇报：物品注视甩头修复（2026-10-19）

### 一、问题与根因

- 冷月在拾取物品的 Reach 阶段会出现瞬时异常偏头。
- 运行时证据确认：目标接近角色正后方时，局部 yaw 在 `+180°/-180°` 表示边界
  换符号；旧逻辑逐帧直接限制到 `±48°`，导致期望 Head yaw 从 `+48°` 瞬间变为
  `-48°`。
- 完整动作中记录到两次 `95.13°/95.31°` 的单帧最终 Head 旋转。物品全身姿态对
  Head 的最大贡献为 `10.26°`，会叠加俯身，但不是甩头根因。

### 二、修复

- `AvatarSocialAttentionRig` 为后方目标增加左右侧滞回：目标停留在后半球时保持首次
  选定的转头侧，不再受 `±180°` 数值换符号影响。
- 实际 Head yaw/pitch 分别加入 `90°/s`、`70°/s` 角速度上限；启停权重继续控制
  淡入淡出，但任何目标切换都不能直接造成大角度跳变。
- 新增 `RearTargetYawWrapDoesNotFlipHeadSide`，让目标连续跨越后方边界并验证侧别与
  单帧步长。

### 三、验证与交付

- 后方边界专项回归 1/1 通过；32 个样本零侧别翻转。
- 完整真实角色切水果链路 1/1 通过；修复后最大相邻求解 Head yaw 步长为 `5.30°`，
  pre-fix 的 `95.31°` 单帧反转消失。
- `ItemActionRuntimeTests` 全量 12/12 通过。
- Development ARM64/IL2CPP APK 构建成功并通过 v2 签名验证：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `ec2462e69230d4600bc5d2ad3cb230b63437fe70570fb3269cb5bed1cd9f821f`
- `item-attention-head-snap` 调试会话保持 `[OPEN]`，等待 PICO 真机按拾取盘子、苹果、
  刀具和放置流程确认视觉效果后再清理插桩与日志。

## Phase Room Navigation V012 工作汇报：空间网格孤岛导航修复（2026-10-20）

### 一、问题与根因

- 用户点击“切水果”时提示“餐桌两条长边都无法使用”，点击“坐下”时提示
  `No complete route to the target.`。
- PICO Emulator 原存档复现显示：角色起点和座椅 Dock 都能投影到 NavMesh，但
  `CalculatePath` 返回 `PathPartial`，路径只覆盖约 0.10 m。
- 根因是 `RuntimeRoomNavigation` 把全部原始 Spatial Mesh 三角形作为可走区域。
  桌面、床面等家具顶面因此形成与地面互不连通的 NavMesh 孤岛。
- 单纯改成地面轮廓后又暴露两个生产数据问题：粗粒度 Wall/灯具/柜体包围盒可能抹掉
  整块地面；若出生点落在床下，物理会把角色顶到床面。

### 二、修复内容

- Spatial Mesh 模式只使用语义地面轮廓生成全局可走面；原始网格继续负责碰撞、
  遮挡和交互几何，不再直接参与可走面烘焙。
- 只将接地、尺寸受控且有阻挡高度的可靠语义家具加入 Not Walkable 障碍；
  Wall、VirtualWall、Lamp、AirConditioner、Screen 等粗聚合块不参与全局 carving。
- 若过滤后的障碍仍抹掉完整地面，记录明确警告并回退到 floor-only NavMesh。
- `RoomNavigationBootstrap` 在地面采样点周围搜索无碰撞出生位，并在占位检测前同步
  Transform；自碰撞排除仅忽略角色胶囊自身层级，不再误忽略同级房间碰撞体。
- 新增 `RoomNavigationSurfaceMeshTests`，覆盖家具顶面、可靠家具绕行和出生占位边界。

### 三、验证与交付

- `RoomNavigationSurfaceMeshTests` 3/3 通过。
- `ItemActionRuntimeTests` 12/12 通过，包含餐桌默认侧阻塞后选择对侧。
- 目标坐下回归通过；完整 `SeatingTests` 19/20，唯一失败是未创建 NavMesh 的既有
  测试夹具问题，与本次变更无关。
- PICO Emulator 同一 48-block 空间网格中，角色出生高度从错误的 `0.866 m` 回到
  地面 `0.391 m`；座椅路径由 `PathPartial` 变为 `PathComplete`。角色到达座椅后
  坐姿权重达到 `1.0`，起身后恢复 `0`。
- 模拟器端“切水果”最终回放被独立的空间锚点物品恢复门禁阻断；原存档已按哈希完整
  恢复。餐桌双侧选择由全量动作测试覆盖，仍需 PICO 真机补充 UI 入口闭环。
- Development ARM64/IL2CPP APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `8f07a4b5b1f69aa85ddee28d510f490d255bd3bec9664f20bd44e46eac9db9c9`
- APK Signature Scheme v2 验证通过。`item-seat-navigation-fail` 调试会话保持
  `[OPEN]`，真机确认前不清理导航插桩。

## Phase Virtual Items V013 工作汇报：旧布局部分恢复与库存守恒（2026-10-20）

### 一、问题与运行时根因

- 启动时提示“旧物品位置与当前房间或其他物品相交”后，多个旧物品在场景中全部
  消失，背包数量也没有恢复。
- PICO Emulator 原存档包含 9 个物品。运行时证据确认咖啡豆罐与当前房间的
  `Plant_mesh-20-Plant` 穿插 `0.11354 m`；刀、盘子、收音机、餐桌等其余物品
  均通过占位检测。
- 旧实现采用整批事务：任一物品失败即销毁 9/9 Runtime View，但保留 9 条
  `saved.items`，库存初始化继续把这些单元视为已摆放，因此形成“场景无物品、
  背包也无库存”的不守恒状态。

### 二、修复内容

- 恢复流程按 `inventoryDefinitionId + inventoryUnitId` 隔离冲突单元。
- 与当前现实房间碰撞体冲突的单元从旧布局移除并退回背包；其余有效物品继续恢复。
- 平底锅/锅铲、筷子等共享库存单元的组合物品按整组回收，不产生残缺组合。
- 旧虚拟物品彼此穿插、未知定义、重复 ID、非法 Pose 等损坏存档仍按 Fail-closed
  整批失败，不用自动回收掩盖数据错误。
- 后续支撑图、角色出生或写盘失败时恢复原存档和库存，保证事务一致。
- 系统提示改为明确说明回收了多少个冲突物品，其余物品已恢复。

### 三、验证与交付

- 新增 `ConflictingSavedUnitReturnsToInventoryWithoutHidingValidItems`，专项 1/1 通过。
- 恢复关键回归 5/5 通过：冲突回收、锚点回退、组合库存迁移、承载顺序、
  异步锚点与写盘失败回滚。
- 同一真实存档 post-fix 回放：8/8 有效物品通过支撑图；仅咖啡豆罐回背包；
  存档从 9 条变为 8 条，咖啡豆罐可用库存从 1 变为 2，Session 无恢复错误。
- 验证结束后设备原始 9 物品存档已按 SHA-256 完整恢复，应用保持停止，下一次启动
  将由修复逻辑正式迁移。
- Development ARM64/IL2CPP APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `9b829804f6f9265bf4356428c65c3b7d70d9ecbdddf13843834515559f682073`
- APK Signature Scheme v2 验证通过。`saved-items-restore-loss` 保持 `[OPEN]`，
  等待用户启动确认后再清理诊断插桩。

## Phase Item Interaction V014 工作汇报：握刀拓扑与自然放刀（2026-10-20）

### 一、握刀手势纠正

- 用户 PICO 复测否定上一版握刀结果。复查确认上一轮所谓“四视角正确”包含空背景和
  裙摆误拍，不能作为视觉证据。
- 有效截图与骨骼数据表明：旧求解器为追逐指尖接触点，把四指 MCP 压到约 `15°`，
  再用最高 `±24°` 双轴扭转补偿；虽然指尖离刀柄小于 6 mm，视觉仍是夹持而非环握。
- Power Grip 改为围绕人体工学偏好角受限求解；MCP/PIP/DIP 仅作小范围残差修正，
  MCP 横向展开不超过 `10°`。
- 新姿态 MCP 约 `52°–59°`，形成闭合拳型。回归准入新增 MCP 最小屈曲和最大侧向
  扭转，不再以单一指尖距离作为视觉正确性的代理。

### 二、自然放刀

- 旧逻辑在 `DetachHeld()` 后仍以 `180°/s` 主动旋转刀具到工作位方向。其初衷是
  恢复碰撞前避免穿桌并保证工作位朝向，但把对齐放在脱手后导致刀具自行旋转。
- 刀具现在保持当前世界旋转，根据当前 Collider 最低点下降到支撑面上方 5 mm，
  然后解除附件并立即恢复碰撞和重力；Release 阶段不再写刀具 Pose。
- 切割后归刀使用同一自然释放。盘子与苹果块仍保留确定性摆盘，避免物理散落。
- 同一预约支持“拿起、放下、再次拿起”，物理清理回调只注册一次。

### 三、验证与交付

- `ItemGripProfileTests` 6/6 通过。
- `ItemActionRuntimeTests` 12/12 通过。
- 刀具 Release 结构化证据：
  `snapDistance=0`、`rotationDelta=0`、`controlled=false`、`collisions=true`。
- Development ARM64/IL2CPP APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `22d2ff51039a32016de2748715edbc220be8b5cd31de0f2816352f958465911f`
- APK Signature Scheme v2 验证通过；用户当前模拟器存档已按哈希恢复。

## Phase Avatar Animation V015 工作汇报：Idle02 导入与随机待机（2026-10-21）

### 一、功能完成情况

- 将两份 Idle02 FBX 非破坏复制到
  `Assets/Characters/SapphiArtCustom/Lengyue/Runtime/Animations/Idle02/`。
- `LengyueIdle01Builder` 新增 Idle02 导入入口，沿用正式角色骨名映射，排除头发、
  裙摆、IK 和控制骨，生成 `Lengyue_Idle02_Combined.anim`。
- `Avatar.controller` 新增 `Idle02` 状态与整型参数 `IdleVariant`；移动、转身和
  SocialAttention 继续保留现有优先级。
- 新增 `AvatarIdleVariation`。角色静止且没有社交注意、转身、接触姿态或上层动作时，
  按当前 Clip 长度随机等待 1 至 2 个循环，再选择不同待机；移动开始后立即回到
  Locomotion。
- `GameBootstrap` 在 `AvatarMotor.Bind()` 后统一初始化随机待机组件，没有新增第二套
  Animator 或 Transform 控制链。

### 二、源资产纠正

- 文件名为 `idle_02_blendshape_inplace(1).fbx` 的资源实际没有 BlendShape 曲线，
  只包含头发网格 Transform 动画。
- `idle_02_InPlace.fbx` 实际同时包含 69 根可映射身体骨骼的动作和 6 条有效面部曲线。
- 导入器按实际曲线选择 `idle_02_InPlace.fbx` 作为身体与表情源，并在 JSON 报告中
  记录 `facialSourceCorrected=true`；没有生成或使用兜底表情。

### 三、编译与测试

- Unity 2022.3.38f1 BatchMode 编译和导入成功。
- 组合 Clip：6.7 秒、30 FPS、69 根正式骨骼、690 条身体绑定、6 条面部绑定。
- 有效面部通道：左右闭眼、左右睁眼、O 口和微笑。
- 直接 Clip 采样：身体姿态变化 `7.373°`，面部权重变化 `100`。
- 生产 Prefab + 生产 Controller 的 Animator 全时段播放验证通过：
  面部权重变化 `100.000`，关键身体骨姿态变化 `13.111°`。
- 固定种子验证通过：Idle01 -> Idle02、移动返回 Locomotion、移动期间不切换、
  SocialAttention 期间不切换。
- 报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Idle02/reports/`
- 本阶段未重建 Android APK；PICO 真机视觉验收仍需随下一次集成构建完成。

### 四、后续建议

- 动画供应侧应修正 BlendShape FBX 导出预设，确保专用表情文件确实包含 Shape Keys
  和动画曲线；当前运行结果不依赖该损坏文件。
- 新增 Idle03 及更多待机时，应扩展 Controller 状态和 `variantCount` 的注册内容，
  不在随机组件中添加角色名或 Clip 名分支。
- 真机重点观察随机切换的 0.2 秒过渡、眨眼/微笑幅度，以及 SocialAttention 和物品
  上层动作结束后的待机恢复。

## Phase Avatar Animation V016 工作汇报：Idle03 加入随机待机（2026-10-21）

### 一、功能完成情况

- 将已有 `idle_03_InPlace.fbx` 对应的 BodyOnly Clip 注册为 `Idle03` 待机状态。
- `IdleVariant` 约定扩展为：`0=Idle01`、`1=Idle02`、`2=Idle03`。
- `AvatarIdleVariation` 的随机集合扩展为三个已注册待机，并允许在
  `Locomotion/Idle02/Idle03` 三个状态中继续轮换。
- Idle03 与 SocialAttention 复用同一个身体 Motion，但使用两个独立 Animator 状态；
  SocialAttention 的进入条件与注视 Rig 行为未改变。
- Idle02 和 Idle03 可直接交叉淡化，移动、左右转身和 SocialAttention 仍可从任一
  待机立即抢占。

### 二、资产检查

- `idle_03_InPlace.fbx` 为 3.708 秒、24 FPS 的纯骨骼动画。
- 源 FBX 没有面部网格和 BlendShape 曲线；69 根正式身体骨骼映射正常。
- 不复制第二份 27 MB `.anim`，随机 Idle03 直接复用
  `Lengyue_SocialAttention_Idle_BodyOnly.anim`。

### 三、验证结果

- Unity 2022.3.38f1 Controller 重建成功，`Idle03` 状态引用正确。
- 生产 Prefab + 生产 Controller 实播验证通过：
  - Idle03 关键身体骨最大姿态变化：`10.350°`。
  - 固定随机种子回放覆盖 Idle02 和 Idle03。
  - Idle02 -> Idle03 完整过渡后，Idle02 的 6 条形态键残留：`0.000`。
  - 移动返回 Locomotion、移动期间不随机切换、SocialAttention 期间不随机切换均通过。
- 更新后的数值报告：
  `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/Idle02/reports/playback_validation.json`
- 本阶段未重建 Android APK，仍需随下一次集成构建做 PICO 真机视觉验收。

### 四、设备包纠正

- 用户在 PICO Emulator 中观察到只播放单一待机。核对确认设备使用的是
  2026-10-20 20:07 构建的旧 APK，早于随机待机运行时和三待机 Controller。
- 已于 2026-10-21 16:40 重新构建 ARM64/IL2CPP Development APK，并覆盖安装到
  `emulator-5554`；设备端 APK 哈希与本地产物一致。
- APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- SHA-256：
  `d1cb9eb39de8978da6d947cef931b2c82421bc1f10e9aa641d16cd90f6715214`
- 应用冷启动成功且进程保持运行。当前模拟器头显视角没有包含角色，因此本次不把设备
  截图作为动画视觉验收证据；随机状态覆盖仍以生产 Prefab 的 Animator 自动验证为准。

## Phase Coffee Brewing V001 工作汇报：热水与咖啡粉冲泡链路（2026-10-21）

### 一、功能完成情况

- `WaterReceiver` 新增带温度接收、排出和容器间转移；目标实际接收多少，源容器才扣除
  多少，水量和温度仍由一个组件统一持有。
- 新增 `LiquidPourer`。水壶倾斜超过 52° 后，从壶嘴向下检测接水容器，并以
  0.24 L/s 转移当前温度的水；水流只负责表现，不参与物理碰撞。
- `CoffeeGrinderController` 新增事务式粉量转移；新增 `CoffeePowderPourer`，研磨钵
  倾斜超过 55° 后，以 24 单位/s 向已注册的冲泡容器转移咖啡粉。
- 新增 `CoffeeBrewReceiver` 并注册到马克杯。杯内达到 0.08 L、80°C 和 12 单位
  咖啡粉后立即完成一次性冲泡，液面通过 `MaterialPropertyBlock` 变为咖啡色并提示
  “咖啡冲泡完成”。
- 咖啡粉先进入杯子或热水先进入杯子均可冲泡；冷水不会误触发，清空容器会复位粉量
  和咖啡状态。

### 二、架构约束

- 不使用水面、咖啡粉模型或粒子的 Collider 判定内容接触。
- 不在 `GameSession` 或中央协调器中添加 `mug`、水壶或研磨器名称分支。
- Builder 只负责给生产 Prefab 注册能力；运行时通过 `WaterReceiver`、
  `CoffeeBrewReceiver` 等组件发现目标。
- 水量、温度、未转移粉量和杯内冲泡状态各自只有一个 owner，不存在并行补偿状态。

### 三、验证与交付

- `BuildScene` 成功，水壶、研磨器、马克杯生产 Prefab 均写入新能力。
- 咖啡冲泡、水壶、研磨器和饮水机相关回归 10/10 通过。
- 端到端用例实际旋转水壶和研磨钵，通过射线命中马克杯，转入 100°C 热水和
  12 单位粉并生成咖啡。
- Android Development APK：140,676,310 字节。
- SHA-256：
  `3aa08a19bb696c56be05ddb8b7f79f9f6503c0a56a9fd38ff6196b4bfa9b1600`。
- APK ZIP 完整性和 APK Signature Scheme v2 验证通过。
- 结构化报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/coffee_brewing_20261021.json`。

### 四、后续验收

- PICO 真机重点检查 52°/55° 倾倒阈值、水流与粉流方向、杯口命中容错和咖啡液面
  颜色透明度。
- 当前只给马克杯注册冲泡能力；如需让碗或其他杯具冲泡，应由 Builder 显式注册
  `CoffeeBrewReceiver`，不得扩大为所有 `WaterReceiver` 自动可冲泡。

## Phase Coffee Brewing V002 工作汇报：冲泡错误提示与整豆识别（2026-10-21）

### 一、提示行为

- 咖啡粉与低于 80°C 的水首次共存时提示“请用加热后的水冲泡咖啡”。
- 冲泡条件满足并转为咖啡时继续提示“咖啡冲泡完成”。
- 整颗 `CoffeeBean` 与任意温度水共存时提示
  “请先将咖啡豆研磨成粉再冲泡”。
- 干燥杯中的整豆不提示；只有水与整豆同时存在才提示。
- 连续注水、温度逐帧变化和 Trigger Stay 不会重复刷相同提示；条件解除后才重新武装。
- 整豆错误优先于冷水咖啡粉提示，避免同一帧连续发布两条相互竞争的消息。

### 二、整豆感应

- 新增 `CoffeeBrewIngredientSensor`，由马克杯子节点
  `Coffee Ingredient Receiver` 持有。
- 感应区为半径 32.3 mm 的 Sphere Trigger，只识别 `CoffeeBean`，不复用
  `CoffeeGrinderReceiver`，不会消费、隐藏或回收整豆。
- Sensor 通过进入、停留和离开事件维护杯内整豆集合；水量变化仍由
  `CoffeeBrewReceiver` 统一评估，因此支持“先放豆后加水”和“先加水后放豆”。

### 三、验证与交付

- `BuildScene` 成功，生产马克杯 Prefab 已注册整豆 Trigger。
- 咖啡冲泡、水壶、研磨器和饮水机相关回归 11/11 通过。
- 覆盖冷水加粉、连续冷水去重、冲泡成功、整豆加冷水、整豆加热水和真实 Trigger
  命中。
- Android Development APK：141,226,906 字节。
- SHA-256：
  `2195c0b7eba45d938c62cc6ca3f9200cf53cb0ba718231420e73b4a5e6e6f795`。
- APK ZIP 完整性和 APK Signature Scheme v2 验证通过。
- 结构化报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/coffee_brewing_v2_20261021.json`。

### 四、后续验收

- PICO 真机检查整豆靠近杯口时的感应范围，确保不会因杯外擦边经过而误提示。
- 若调整杯口 Collider，只修改 Builder 的显式感应尺寸，不在运行时增加物品名称判断
  或全场景整豆扫描。

## Phase Runtime Cutting V001 工作汇报：胡萝卜建模与运行时切割（2026-10-21）

### 一、资产与材质

- 新增生产物品 `Carrot.asset` / `Carrot.prefab`，归类为食品，质量 0.12 kg，
  每日库存 5 个。
- `CarrotBuilder` 生成 32 段、12 环的闭合视觉网格、低面数凸碰撞代理和独立体积叶冠；
  原始资产不依赖外部 DCC 文件即可重建。
- 胡萝卜外皮使用程序化环纹；切面材质使用对象空间 UV，依据切面法线与胡萝卜主轴
  的夹角连续混合横截面和纵向内部细节。横切显示圆芯，斜切显示拉长芯，纵切显示
  纵向纤维，不采用角度阈值硬切纯色。
- 用户提供的 4 张 360 x 360 AI PNG 已升采样为 512 x 512 运行时纹理。升采样未创造
  原生高分辨率细节，来源哈希记录在
  `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/PropsSource/Carrot/SOURCES.md`。

### 二、运行时切割

- 新增 `RuntimeCuttingTool`：仅玩家持刀且刀具未暂停时采样刀刃；要求至少 8 mm 位移、
  0.28 m/s 速度，并以刀刃 5 点扫掠射线确认真实 Collider 命中，避免只因包围盒相交
  而误切。
- 新增 `MeshPlaneSlicer`：在对象空间裁剪三角面，封闭正负两侧切口并写入独立内部
  submesh；切口顶点同时写入横截面 UV、纵向 UV 和方向对齐值。
- 新增 `RuntimeCuttable`：胡萝卜最多连续切 3 次，拒绝小于原始碰撞体积 3% 的碎片，
  并保证每个动态凸 `MeshCollider` 不超过 255 个三角形。
- `GameSession.TryCutItem` 在 `PlayerUsing` 租约内预生成两侧碎片、检查空间占用后
  原子替换原物品；两侧继承线速度、角速度和同一库存单元，质量按体积比例分配。
- 存档仅记录对象空间切平面历史和保留侧，加载时从原始网格确定性重放；空切割历史
  与旧存档兼容。删除任一库存单元时仍能统一回收其全部切块。
- 刀具能力和胡萝卜能力通过 `ItemInteractionProfile` / `CuttableSettings` 注册，
  中央协调器没有胡萝卜名称分支。

### 三、验证与交付

- 胡萝卜专项 EditMode 测试 7/7 通过：生产资产、闭合切片、体积守恒、斜切方向数据、
  连续切割上限、存档重放、非法平面拒绝和旧空状态兼容。
- 生产 `GameSession` 切割集成测试 1/1 通过：原物品替换为两个独立凸碰撞刚体，
  质量守恒、切割历史落盘且共享库存单元。
- 最终完整 `GameRuntimeTests` 为 37/44；胡萝卜集成用例通过。剩余 7 项是当前脏工作树
  中既有的 HUD 材质、墙/坡导航、每日库存夹具、易碎物夹具、无效布局 owner 和
  AI 目标移除回归，不在本阶段扩张修复。
- Android Development Clean Build 成功，IL2CPP 构建图 1848 项完成，APK 实际大小
  141,227,418 字节。
- APK SHA-256：
  `3fb315c7da2d511bdbf25fba8f48ed2dcee38bfba5a9c8e1e0f9e6d84cb99ce6`。
- APK ZIP 完整性和 APK Signature Scheme v2 验证通过。
- 结构化报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/carrot_runtime_cutting_20261021.json`。

### 四、后续验收

- 必须在 PICO 真机验证玩家挥刀速度阈值、刀刃命中容错、连续切块抓取稳定性和切面
  在实际 URP 光照下的观感；自动测试不能替代 6DoF 手感验收。
- 若需要更近距离观察切面，应重新生成原生 1024 或更高分辨率纹理，而不是继续放大
  当前 360 来源图。
- 后续新增可切食材应注册 `CuttableSettings`、可读闭合视觉网格和低面数凸代理，
  不得在 `GameSession` 添加物品名称判断。

## Phase Avatar Animation V017 工作汇报：随机待机设备运行修复（2026-10-22）

### 一、运行时根因

- PICO Emulator 实测约 2.36 FPS；项目 `Maximum Allowed Timestep=0.06s`，导致
  `Time.deltaTime` 相对真实时间推进约慢 10 倍。
- 旧逻辑要求连续播放 1 至 2 个完整 Idle 循环才切换，并在移动、转身或交互开始时
  清零倒计时；自主 Wander 约 3 秒即抢占，因此 Variant 长期停留为 0。
- 设备证据还确认 `AvatarMotor.IsMoving/IsTurning` 可能在可见 Animator 已回到
  Locomotion 且 `Speed=0` 时继续为 true。用动作意图作为动画待机门禁会误判。

### 二、修复

- `AvatarIdleVariation.Update()` 改用 `Time.unscaledDeltaTime`，待机轮换按真实时间推进。
- 移动、转身、社交和上层动作阻断时暂停已有倒计时，不再丢弃累计进度。
- 待机准入以实际 Animator 状态白名单与 `Speed <= 0.05` 为准；坐/躺接触仍阻断，
  实际 Walk 和 TurnStep 状态仍不会切换待机。

### 三、验证与交付

- pre-fix：56.425 秒真实时间只减少 5.629906 秒倒计时；Wander 后计划清零，
  `IdleVariant` 始终为 0。
- post-fix：启动后约 10.4 秒 `IdleVariant` 从 0 切到 1，Animator 完成
  Locomotion -> Idle02；后续自主移动没有重置 Variant 或下一轮计划。
- 所有临时运行时上报、NDJSON、环境文件和调试记录均已删除。
- 最终 ARM64/IL2CPP Development APK 已覆盖安装到 PICO Emulator，设备端哈希与
  本地产物一致，冷启动成功。
- APK：`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- 大小：141,691,096 字节。
- SHA-256：
  `450b10d2772f42a9435385627fc86707f9115c86dc4893da104cd2ecd256e8e1`
- APK ZIP 完整性和 APK Signature Scheme v2 验证通过。

## Phase Bowling Animation：冷月拾取与滚球动画接入（2026-10-22）

- 导入用户交付的 `pick_up_bowlingball_InPlace.fbx`，生成 30 FPS、4.067 秒的非循环
  `Lengyue_Bowling.anim`，含 690 条骨骼曲线与 5 条面部曲线。
- 新增 `BowlingMotion`、`BowlingAnimationBuilder`、`BowlingAnimationValidation`。
  `BowlingTurnAction` 按动画触球点校准站位与朝向，再播放完整拾球和滚球动作；
  `AvatarBowlingRig` 在第 20 帧开始 8 cm 近距吸附，第 21 帧使用 45 cm 扩大范围，
  再远则无条件吸附，避免因距离导致动作失败。
- 持球期间复用 `PhysicsBody.BeginInteraction/TryMoveInteraction/EndInteraction`。
  动画驱动手，球在最终骨骼更新后跟随手；第 90 帧出手并恢复重力、碰撞、动态刚体。
- 已更新生产 Animator Controller、角色 Prefab、场景及场景构建绑定。
- Unity Animator 与物理步验证 12/12 通过，覆盖 15/30/72/120 FPS 和三层距离。
  持球误差 0、骨骼姿态误差 0°，与参考球偏移最大差值 2.693 mm；
  出手回调恰好一次，取消能恢复物理与动画层。
- 最终包 PICO 模拟器完整回合通过：玩家出手、计分、球瓶复位、冷月动画拾球/滚球、
  命中/计分及 Winner 结算。冷月得分 10，首个命中发生于出手后 0.385 游戏秒，
  在出手后 5.428 游戏秒计分；全过程未出现吸附失败或 Action timeout。
- 报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/bowling_animation_20261022.json`、
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/bowling_animation_device_20261022.json`。
- 已安装 Development APK，设备哈希一致：
  `450b10d2772f42a9435385627fc86707f9115c86dc4893da104cd2ecd256e8e1`。
  工作分支 `feature/bowling-authored-animation`；其他并行任务的未提交改动保留。
- 复制工程验证时 `.anim` 文件中的 Unity 绑定缓存可能重建，曲线和 Clip 设置一致；
  Unity Animator 播放验证是有效性依据。真机后续关注手指接触视觉和真实地面高度。

## Phase Virtual Items V014 工作汇报：虚拟支撑面放置与电视容差（2026-10-22）

### 一、问题与根因

- 桌子、椅子、饮水机等不可移动物品虽然启用了放置投影，但水平表面探测仍受
  `AllowSurfacePlacement` 限制，因此视线命中虚拟物品顶面时不会生成有效投影。
- 最终占位校验只忽略射线命中的单个 Collider；虚拟物品使用复合碰撞体时，同一
  `GameItem` 的其他 Collider 会被误判为障碍物。
- 电视没有独立支撑容差，投影尺寸稍大于桌面或支架时会被严格拒绝。

### 二、修复

- 非墙面的不可移动物品现可使用水平支撑面；预览、精确提交、锚点等待后的最终校验
  和存档恢复统一使用同一判定。
- 虚拟支撑按 `GameItem` 根识别，允许同一支撑物的复合 Collider 共同组成顶面；
  其他虚拟物品仍保持阻挡。
- `ItemDefinition` 新增 `MinimumSurfaceSupportRatio` 数据字段。默认值 1 使用严格
  四角支撑；复古电视显式配置为 0.85，使用 8 点环形采样，允许支撑面略小于投影且
  电视仍面向玩家。明显过小的支架继续判为不可放置。

### 三、验证与交付

- 放置相关回归 6/6 通过：固定可移动家具、不可移动家具受力锁定、墙挂物、电视小
  支撑/过小支撑、不可移动椅子放到复合碰撞体虚拟桌面、电视叠放后的反序存档恢复。
- Android ARM64/IL2CPP Development Clean Build 成功，Unity 记录
  `MATESOUL_BUILD_PASS`，耗时 9 分 23 秒。
- APK：`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- 大小：141,692,728 字节。
- SHA-256：
  `28284f4de24d07085bd289ad51b3372d5c421efffd5037a89c3dd51bdbe48e86`
- APK ZIP 完整性与 APK Signature Scheme v2 验证通过。

## 2026-10-22 — GitHub main 最新运行版本发布

### 发布结果

- GitHub：`https://github.com/cuiyuestar/MateSoul/tree/main`。
- 提交：`3981a436c89b2b922446706a630dcfaf2065a2fe`。
- 从 `91ccc17` 正常快进推送；`git ls-remote` 已确认远端 main 与提交一致。
- 本地 main 同步快进；原开发工作树与其他美术工作树保留。
- 最终树包含 4,554 个文件，源文件总大小 1,047,347,156 字节。

### 内容整理

- 保留 Unity Assets / Packages / ProjectSettings、本地 PICO Unity SDK、
  UniVRM 0.131.3 / UniGLTF 依赖与 AI 网关源码。
- 同步最新冷月模型、服装动态、动画及物品；保留游戏视频、音效、字体和
  资源导入、场景重建所需的源模型与贴图。
- 修正忽略规则中 `scripts/`、`Obj/` 的宽泛匹配，避免漏传实际运行代码及模型。
- 排除美术制作中间产物、实验场景、调试探针、验证媒体、一次性脚本、
  SDK 调试面板、构建缓存、APK 和开发报告。
- 四张必要的 4096² Texture2D 转为 Unity 原生二进制，每张 67,112,416 字节；
  转换前后像素 SHA-256、纹理设置、GUID 和 fileID 全部一致。
- 移除截图验证方法和临时发布验证脚本后，最终目录重新编译通过。

### 验证

- 1,050 项依赖检查通过，无缺失场景脚本、角色脚本、材质、骨骼或资源引用。
- Android ARM64 / IL2CPP Development Build 成功：0 errors，5 warnings。
- 验证 APK：179,735,124 字节；1,553 个 ZIP 条目、16 个 ARM64 原生库；
  IL2CPP ELF machine 为 183。ZIP CRC 与 APK v2 签名验证通过。
- APK SHA-256：
  `34c8008bde54f4173de81bfe2da91ccdb6d0345cbd87cf98b8ee77840bbda3da`。
- Manifest 保留空间数据权限与 `enable_environment_depth`；
  AI 网关构建、启动、MCP 工具注册和非法请求 400 检查通过。
- 本次未发起真实模型请求或执行真机运行验收。AI 使用需按仓库 README
  配置服务端密钥；正式构建需配置实际 HTTPS 网关地址。
- 发布临时工作树、验证 APK、缓存、日志与辅助脚本已清理。

## Phase Lengyue Production Merge 工作汇报：新版冷月合入主工程（2026-10-22）

### 一、完成内容

- 用户授权将独立验证后的新版冷月合入 `/Users/bytedance/MateSoul/MateSoul`。
  正常 `MateSoul.unity` 场景和 `Lengyue_Runtime.prefab` 已使用 Direct49 头部/侧发、
  SoftFit01 服装及 LimbRefinement01 手肘、小腿优化。
- 合并三个 LOD 的袜子优化，并对脚踝局部收缩量保留皮肤间隙。Fix50 当时只有
  诊断图与报告，没有完整模型资产，本次采用完整可集成的 Direct49。
- 将主工程当前 13 条动画重定向到新版骨架，保留 Clip GUID、动画事件、待机、
  社交、坐躺、换装、物品交互和保龄球组件。
- 修复重建场景时重新生成旧骨架转身动画、接触动画采样姿态被保存为默认姿态
  的问题；脚部 IK 改为先混合目标位置再求解，减少近直腿的接触滑移。
- 当前分支为 `feature/lengyue-current-model-merge`；其他窗口的玩法源码未覆盖。
  正常 Builder 会重新序列化既有物品和项目配置，这些变化另行记录并排除在本次
  角色合并提交外。共享 Unity6Study 和 Fix 源目录未修改。

### 二、验证与 APK

- 独立快照资产检查 4,124 项通过；编辑器运行循环检查 22/22、既有回归 9/9 通过。
  肢体检查 182,317 项通过。主工程最终复核：15 个蒙皮网格、41 个脚本无缺失，
  主场景各网格/材质与 Prefab 一致，左右脚默认位置正确。
- 袜口 0°、+15°、-15° 俯仰筛查最小间隙分别为 2.238、1.149、1.978 mm；
  未发现局部负向穿入。该检查不代表全部动画的完整碰撞验收。
- 主工程 ARM64/IL2CPP 编译成功。首次 Gradle 封包因磁盘空间耗尽失败，清理本次
  临时缓存后复用编译结果，`assembleDebug` 成功，耗时 155.333 秒。
- 最终 APK 已替换 `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`：
  182,880,466 字节，ZIP CRC、v2 签名、ARM64 架构验证通过。包内关键原生库、
  元数据与资源哈希均匹配本次主工程构建，保留 `SPATIAL_DATA` 和
  `enable_environment_depth`。
- SHA-256：
  `146a7fcf7d765412b702f74aa34bfb12c72286b42d0a35b3f8e3c3385dea7501`。
- 当前未连接设备，头显外观、真实追踪、遮挡及帧率仍需安装后实测。

### 三、证据与回滚

- 交付目录：
  `/Users/bytedance/MateSoul/ArtSource/LocalStudyOnly/Lengyue_ProductionMerge_20261022`。
- `README.md`、`merge_manifest.json`、各数值验证报告与 `merged_three_views.png`
  记录本次组合版本；`BeforeMerge` 保存替换前主工程文件及旧 APK。
- 下一次角色合并继续采用局部资产与动画重定向，不用旧独立工程覆盖整套主工程。
- 本次角色合并已提交 `2de87be2144f45b8a959c9948fcca97175bae1b3`；
  `GameSceneBuilder` 仅提交本次默认姿态恢复补丁，其他并行未提交改动继续保留。

## Phase AI World Context 工作汇报：有界环境与物品上下文（2026-10-22）

### 一、完成内容

- `CharacterRuntimeSnapshot` 新增可选 `world`，覆盖玩家接近/对话/输入模式、关注物、
  玩家与冷月持有物、小游戏、活跃设备事实、库存提醒、现实环境摘要和虚拟世界摘要。
- 新增 `GameSession.AIContext.cs`。现实房间只在启动配置时分析一次；每轮对话仅采集
  有界动态数据。虚拟物品按定义、状态和控制权聚合，最多 24 组，并只保留最多 12 个
  关注、持有、占用或距离最近的精确实例。
- Room Capture 的逐件语义计数标记为 `observed`；Spatial Mesh 和回退障碍物聚合
  标记为 `estimated`，不从聚合包围盒生成伪精确物体关系。
- 新增 `IRuntimeWorldFactSource`。收音机、电视、水壶、加热底座、饮水机、可烹饪
  食物、咖啡豆罐、研磨器和冲泡容器自行输出事实，中央采集器没有物品名称分支。
- 保龄球通过 `MiniGame.CaptureRuntimeContext()` 输出阶段和双方得分；后续小游戏
  继续覆盖该扩展点。
- AI Gateway 增加严格的 WorldContext Zod 契约和日志字段。完整 targets 仍用于
  回复协议校验，模型 Prompt 改为按名称、状态、能力分组，只提供显著目标和每组最多
  3 个可选 ID，避免物品数量线性放大 Prompt。

### 二、验证结果

- `services/ai-gateway` TypeScript 构建成功，19/19 测试通过。
- Unity 2022.3.38f1 两次完成 Runtime 与 Test Assemblies 编译，Tundra 构建成功，
  未发现 C# 编译错误。
- Unity 定向测试执行被 TRAE 沙箱阻止写入
  `~/Library/Application Support/Unity/CoreBusinessMetrics.db`，未生成结果 XML；
  该限制发生在测试启动阶段，不是源码编译失败。
- 本次未构建 APK，也未执行 PICO 真机对话与实际 token/延迟测量。

### 三、边界与后续建议

- 当前硬上限：持有物 8、活跃事实 12、库存提醒 8、现实语义 24、虚拟物品组 24、
  显著实例 12；网关对同样上限执行 fail-closed 校验。
- 后续新增设备状态应实现 `IRuntimeWorldFactSource`，不要向
  `GameSession.AIContext` 增加物品 ID 或组件类型 switch。
- 真机验收时记录 `AI_CONTEXT_CAPTURED payloadBytes`、网关 `worldRevision`、
  总 token 和首包延迟；若典型场景超过约 1200 context tokens，再按事实优先级缩减，
  不传原始网格、全量坐标或全量实例明细。

## Phase Turn Idle 工作汇报：连续转向期间的动画所有权（2026-10-23）

### 一、完成内容

- 基于正式 `Lengyue_Runtime.prefab` 复现并修复 TurnStep/Idle 反复切换。
  根因是片段固定 .96 退出、.6s recovery 与转向尚未收敛之间缺少共同完成条件；
  随机 Idle、脚部锁定是放大因素。
- `AvatarMotor` 增加 `TurnStepActive` 连续转向所有权。尾部保持与补步恢复仍存在，
  下一步可直接进入相同/反向 TurnStep；角度收敛或动作取消后统一释放。
- `AvatarLocomotionBuilder` 与正式 `Avatar.controller` 增加连续补步与受所有权约束的
  退出。`AvatarIdleVariation` 暂停真实转向期间的随机切换，完成后继续原有计时。
- 正式角色、非循环左右转身片段逐字节保持。没有重建角色或用旧骨架生成动画。

### 二、验证结果

- 固定时钟正式 Prefab 回放 9 组：转向未完成时的 Idle 间隙均为 0s，
  最终角度均收敛为 0°，随机待机和 movement/social 状态恢复通过。
- 正常 Play Mode 另跑固定 120°、左 112°、移动目标、反向 4 组，全部通过；
  最大支撑脚位移 4.81–6.07mm，补步间隔约 1.66s。
- HoldFacing 取消、走路打断均断言在 .24s 内退出 TurnStep。
- Unity Runtime/Editor/Test assemblies 编译成功；未运行 NUnit XML 测试，
  不将回放报告冒充单测结果。
- 未构建 APK，未执行头显主观观感验收。沙箱拦截 Unity 全局分析/配置写入，
  子进程仍正常退出，回放报告完整。

### 三、交接与清理

- 详见 `/Users/bytedance/MateSoul/项目沉淀资料/Lengyue_转向Idle衔接分析_20261023.md`
  与同目录数值 JSON。采集插桩、HTTP 服务与临时输出已清理。
- 既有本地 `TurnInPlaceTests.cs` 补充提前 Idle、连续补步计数与控制器契约检查；
  该目录被 `.gitignore` 排除，交付时需留意测试文件的版本管理。
- 后续头显验收关注补步间的尾部保持是否自然。现有片段的躯干姿态会回到尾部姿态，
  此次修复只保证朝向过程不再反复交给普通 Idle，未替换动画内容。

## Phase Startup Optimization 工作汇报：冷启动并行化与复测（2026-10-23）

### 一、完成内容

- `GameBootstrap` 提前读取并验证存档，启动旧锚点定位任务后立即加载房间；
  `GameSession` 消费预读存档和预取任务，保留暂停取消、恢复复用、姿态回退与回滚。
- `PicoSpatialAnchorService` 在 Provider 启动前订阅数据更新，用事件唤醒和约 2 秒
  有界数据等待替换固定 6 次、400ms 间隔重试；空数据更新不代表永久失效。
- `ResponseEmotionAudio.Attach` 仅绑定事件，按需加载、缓存实际使用的情绪池。
- Seating/Dialogue 探针与网格导出改由 `MATESOUL_DEVICE_PROBES` 显式启用。
  房间加载、网格查询、角色绑定、会话、音效和恢复增加 Development 阶段日志。
- READY 等到物品恢复完成或明确进入失败恢复状态再报告，避免暂停期间提前报就绪。
- `GameSceneBuilder` 新增当前已保存场景的 Android 构建入口，不重建场景或角色。
  默认 APK 更新，旧包保留为 `Builds/MateSoul_Framework_Baseline_20261022.apk`。

### 二、验证结果

- Android Development ARM64 / IL2CPP 构建成功，APK ZIP CRC 和库检查通过；
  9/9 Unity 回归通过，另有 8 条已编译程序集预取锚点边界断言通过。
- PICO 模拟器两包各预热 1 次、正式各 5 次，交替安装顺序，每次恢复同一份
  12 件物品存档。正式 10 次均完整恢复并存活至 READY 后约 12 秒。
- 本组业务 READY 中位数：旧包 15.138s、新包 10.296s；网格后等待中位数：
  3.601s → 0.306s。旧包还有此前代码差异，总差值不能全部归因于本次优化。
- 新包各轮实际经历 1 次空数据更新事件、3 次空锚点查询；旧包为 6 次。
  默认新包 0/5 检测到探针文件更新，旧包为 5/5。
- 短暂稳定尾部 PSS 组中位数 1019.43 → 920.14MiB，但采样全局峰值
  1340.71 → 1371.12MiB；本次不是泄漏或内存归因验收。
- 存档及偏好完全恢复，存档哈希一致；ADB 恢复 shell 身份，模拟器退出确认。
  正式角色 Prefab、动画控制器和主场景哈希保持不变。

### 三、限制与后续

- 新包引擎初始化标记到 Bootstrap 仍约 4.859s；房间加载 1.099–6.015s。
  下一步用真机 Release、Unity 加载/CPU 时间线和 Memory Profiler 拆分。
- `endCameraRendering` 只证明 Unity 渲染回调，不代替头显 Compositor 显示首帧。
  有效真机锚点和非空数据更新仍需验收；本轮未清缓存或跑交互循环。
- Unity 工具结束时报告全局遥测/SDK 元数据写入受到沙箱限制，但实际进程退出 0，
  XML、构建成功标记与 APK 均已复核。Android `run-as` 无法访问外部存档，
  改用已授权的 userdebug root 真实媒体目录，并在结束后恢复身份。
- 报告：`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_冷启动优化与复测_20261023.md`。
  数据：`/Users/bytedance/MateSoul/项目沉淀资料/性能测试/20261023-startup-optimization/`。
  中间脚本、完整调试日志、缓存和存档备份留在 `/tmp`，未加入项目交付目录。

## Phase 工作汇报：启动前生命周期计时与 Profiler 导出（2026-10-24）

### 一、已完成

- 新增 `StartupTimeline.cs`：Development 包记录 BeforeSplashScreen、
  Before/AfterSceneLoad、sceneLoaded、首个 PlayerLoop、启动画面状态和主线程
  回调边界；与 Bootstrap 的原有阶段使用同一前置单调时钟。
- `GameBootstrap.cs` 增加 Awake、角色激活边界，`GameHud.cs` 增加 Start、
  字体实际加载和 HUD 完成边界。计时包未修改业务执行顺序；
  后续的启动顺序与通知 UI 重叠已完成 Android 复测，最终实现见下一节。
- 新增 Editor 工具 `StartupProfileExport.cs`，批量导出 Unity 二进制录制的帧、
  线程和包含子项的样本历时。录制需要单次显式文件开关，普通计时不启用录制。
- 独立 APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_StartupTimeline_20261024.apk`。
  基线结束时默认 Framework 恢复上一轮验证包；后续已更新为下一节的验证版本。

### 二、验证与实测

- Android Development ARM64 / IL2CPP 构建与 ZIP CRC 校验通过；
  后续 Editor 编译亦成功，没有 C# 编译错误。
- 主场景、角色 Prefab、动画控制器和项目设置哈希保持不变。
- 构建日志确认当前为非 Pro 许可证；没有修改 Unity Splash / Logo 配置。
- 最初直接启动 QEMU 报 `HV_DENIED`，按既有技术文档通过 LaunchServices 打开
  启动脚本后解决。完成预热 1 次、正式 5 次及独立 Profiler 1 次；
  7 次均恢复 12 件物品、身份保持，READY 后存活 12 秒。
- 引擎到 Bootstrap 中位数 5.030s，细分为：引擎到 BeforeSplash 293ms、
  到 XR 焦点 2189ms、焦点到 BeforeScene 2356ms、场景生命周期 55ms、
  场景生命周期后到 Bootstrap 127ms。各区间中位数不能相加。
- 单独录制首帧 2568.107ms 内，资源管理锁等待 1526.859ms、
  WatermarkRender 着色器 GPUProgram 等待约 708.113ms。
  后台预加载 1760.316ms 与等锁重叠，不重复计入墙钟。
- HUD 构建中位数 124ms、字体载入 92ms，5 轮都挡在房间读取前。
  后续通过脚本导入顺序、通知 UI 生命周期调整及干净构建完成读取提前验证。
- 存档、偏好、默认安装包与 shell 身份均已恢复，存档哈希仍为
  `3663b55a…f3c5`，QEMU PID 55917 退出确认。

### 三、交接

- 后续 Startup 重叠版本已完成同存档复测并更新默认 APK，结果见下一节。
  使用 `open /Users/bytedance/MateSoul/scripts/start-pico-emulator.command` 启动模拟器，
  不再重复直接启动 QEMU。
- 说明已追加至
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_冷启动优化与复测_20261023.md` 第 10 节。
- 中间工具、原版本备份、完整构建与失败启动日志位于
  `/tmp/matesoul-startup-phases-20261024/`。

## Phase 工作汇报：房间读取与 UI 初始化重叠验收（2026-10-24）

### 一、实现与实测

- `GameBootstrap.cs.meta` 设置导入执行顺序 `-100`；`SystemNotificationPresenter`
  的 Bind 只绑定数据，字体及 UI 在 Start 创建，避免共享字体再次挡在房间请求前。
- `GameSceneBuilder.BuildCurrentSceneAndroidDevelopment` 加入 CleanBuildCache。
  两个增量 APK 的包内脚本顺序实际仍为 0；干净构建后为 -100，并由运行日志验证。
  只构建保存的场景，主场景、角色 Prefab、控制器和项目设置哈希保持一致。
- 预热 1 次、正式 3 次及独立 Profiler 1 次，5 次均恢复 12 件物品、保持身份，
  READY 后存活 12 秒。普通 4 次逐轮通过读取先于两类 UI 及恢复准入顺序检查。
- AfterSceneLoad → room_load_start 中位数由 138ms 降至 **53ms**，减少 **85ms**。
  最终组 READY 中位数 11.709s；跨干净构建和环境波动，不报告总差值为因果收益。

### 二、边界与验证状态

- 本组 HUD / 字体中位数为 338 / 269ms，历时已与房间等待重叠，但自身没有加快。
  追加录制 HUD 为 134.131ms，资源读 93.184ms，三个互不重叠 File.Read
  合计 91.380ms；未重现正式字体高值，不能认定为固定 CPU 成本或持续退化。
- 现有 UI 回归在 `GameRuntimeTests.cs:2435` 材质断言失败；旧同步 Bind 对照也在
  同一行失败。通知数量、顺序及透明度检查已通过，整体用例仍未通过。
  最终源码已恢复并与构建副本核验一致，测试文件没有改动。
- Android ARM64 / IL2CPP、ZIP CRC、包内顺序、保护资产及安装包哈希验证通过。
  默认与安装 APK 均为 `08ba06dcba77c678e398da6e5fad91d4f3c0be63094a447b2c08de4d76103f69`，
  182999478 bytes；独立包为 `/Users/bytedance/MateSoul/Builds/MateSoul_StartupOverlap_20261024.apk`。
- 存档、偏好恢复，ADB uid=2000；QEMU PID 71092 与 Unity 退出确认，
  设备 Profiler 开关、录制文件已清理。

### 三、后续与资料

- 后续重点仍为引擎/XR 前置等待、同包同缓存条件下的字体读取，以及真机 Release。
  UI 材质断言与 TMP 子网格的适配单独处理，不能把既有测试失败记为本轮通过。
- 报告：`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_冷启动优化与复测_20261023.md` 第 10.5 节。
- 数值、缓存证据、UI 失败对照 XML、额外录制摘要及恢复校验：
  `/Users/bytedance/MateSoul/项目沉淀资料/性能测试/20261024-startup-phases/early-start/`。

## Phase 工作汇报：真机房间标定能力与设备识别修正（2026-10-24）

### 一、实现

- 分支：`feature/real-device-room-capture-20261024`。基于当前工作区修改，
  保留其他任务已有的未提交变更。
- `PXR_ProjectSetting.asset` 开启 `sceneCapture`、`planeDetection`；
  视频透视、空间网格、空间锚点、环境深度与既有手眼跟踪配置保持启用。
  `GameSceneBuilder` 的能力配置与保存场景打包入口同步设置所需开关。
- 新增 `PicoDeviceEnvironment.cs`，统一读取 Android MODEL、PRODUCT、
  FINGERPRINT 和 Unity 设备身份；仅使用明确的 `emulator` 标识识别模拟器。
  `swan_arm64` 是可能与真机共用的硬件名称，不再作为模拟器判据。
- `GameBootstrap`、`RoomNavigationBootstrap` 和 `RealityOcclusionMeshRenderer`
  复用同一缓存结果。真机走 Room Capture，模拟器走 Spatial Mesh；
  环境深度不会再因真机使用 Swan 名称被关闭。
- 新增一次性 `MATESOUL_DEVICE` 身份日志，便于真机连接后核对实际型号与分支。
  房间查询为空时仍使用既有 `StartSceneCaptureAsync` 触发系统房间标定。

### 二、验证状态

- 8 项 Android 身份模拟回归通过，覆盖 Swan 真机标识、PICO 4 Ultra 标识、
  共用硬件名称的模拟器、仅 fingerprint 含模拟器标识、JNI 读取失败、
  缺失标识及跨消费者缓存一致性。这些是标识回归，不代表物理头显实测。
- 首轮编译发现遮挡模块仍需 `UnityEngine.XR.MeshChangeState` 的命名空间引用，
  已恢复引用；临时回归工具使用 Unity 随附 Mono 编译器兼容语法。
- Android Development ARM64 / IL2CPP 干净构建通过，Unity 进程退出 0，
  总进程历时 489.52 秒。APK 签名、ZIP CRC、七项空间能力声明、空间数据及
  手眼跟踪权限、设备识别类与日志标记入包核验通过。
- 独立交付：
  `/Users/bytedance/MateSoul/Builds/MateSoul_RealDevice_20261024.apk`，
  183001762 bytes，SHA-256：
  `8bc2ccdaf4aa9f6fd5b2368faf300a945295673ae6dbcf0b390466ca79c3d56c`。
  默认 Framework APK 已更新为相同包。
- 数值与包体验收记录：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/real_device_room_capture_20261024.json`。
- 构建后 ADB 仍未连接设备；尚未进行物理头显安装、房间扫描和坐标验收。
- 构建使用 `BuildCurrentSceneAndroidDevelopment`，不运行角色或场景生成器。
  主场景、角色 Prefab、动画控制器 SHA-256 与构建前一致；
  ProjectSettings 和 EditorBuildSettings 构建后亦保持原字节内容。

### 三、交接

- USB 连接头显、允许 USB 调试后，读取 `adb devices -l` 和 Android 系统身份。
  正常真机日志应为 `MATESOUL_DEVICE kind=headset`，就绪源为
  `MATESOUL_READY source=PICO Room Capture`。
- 首次进入允许空间数据权限，按系统房间标定引导扫描地面、墙体和家具并保存。
  系统数据由应用直接查询，无需导出 OBJ 后再导入 Unity。
- 此包用于功能验收；当前对话网关为本地 HTTP，正式 Release 构建仍要求 HTTPS。
  真机对话联调可在电脑网关运行后设置 `adb reverse tcp:8787 tcp:8787`。
- 真机导航仍使用地板 Polygon 和家具标定体；完整三角网格的全房间精细碰撞、
  强制重新标定入口和家具移动后的地图重建不由本次配置修改完成。
- 完整构建日志、标识回归工具和资产基线保存在任务临时目录，
  不加入项目交付包。

## Phase 工作汇报：真机启动签名校验与应用包名修正（2026-10-24）

### 一、故障定位

- 物理头显已授权 ADB，设备型号 B3110、产品名 swan、Android 16，
  旧真机测试 APK 安装成功且设备包 SHA-256 与交付一致。
- 启动日志显示 `AppStartInterceptManager` 被 `com.bytedance.pico.matrix`
  拦截，Matrix 展示“版权校验不通过：签名非法”，并强制停止模板包名应用。
  这是系统启动版权校验，不是 Room Capture 或游戏启动逻辑的错误。
- APK 的 Android v2 签名本身验证通过；PXR_PlatformSetting 的
  startTimeEntitlementCheck=0、appID 为空，APK 未带 pvr.app.id。
  因此不通过修改应用内 entitlement 开关处理系统拦截。

### 二、整改

- Android applicationIdentifier 从
  `com.UnityTechnologies.com.unity.template.urpblank` 改为 `com.matesoul.game`。
  Unity 模板默认包名不是 MateSoul 的正式应用身份，新包保留原有签名证书，
  验证独立包名是否能正常通过系统启动校验。
- `scripts/diagnose-pico-memory.command` 的目标包名同步更新。
- 保留用户当前锐化与 PXR 配置，不重新运行场景、角色或动画生成器。
  新包与旧包拥有独立应用目录，旧包存档不自动迁入，亦未删除旧包。

### 三、验证状态

- 保存场景 Development ARM64 / IL2CPP 干净构建通过，Unity 退出 0，
  进程历时 577.52 秒。APK v2 签名、ZIP CRC、七项空间能力声明和资产哈希通过。
  证书 SHA-256 与旧包一致，未轮换签名。
- 新包已安装至 B3110 真机，base.apk 与交付包 SHA-256 一致；
  正常 MAIN / LAUNCHER Activity 启动返回 ok，PID 23107 持续存在。
  初次观察头显处于休眠；唤醒后出现新包的视线追踪权限提示，
  本轮未再出现 Matrix 的“签名非法”。
- 完整 Unity 初始化与房间 READY 尚未验证：头显自动休眠，
  新包 SPATIAL_DATA、HAND_TRACKING、EYE_TRACKING 仍待用户授权。
  没有通过 adb grant 自动接受隐私权限或绕过系统校验。
- 独立交付：
  `/Users/bytedance/MateSoul/Builds/MateSoul_RealDevice_PackageFix_20261024.apk`，
  183001250 bytes，SHA-256：
  `53a8586b316556723b88926694f8b292d2f70b3cbfd77237746b8229e7c84a78`。
  默认 Framework APK 同步为此新包；先前 RealDevice 包仍为旧包名历史版本。
- 主场景、角色 Prefab、动画控制器未改变；PXR 配置保持用户当前内容，
  ProjectSettings 最终只增加本次 Android 包名差异。
- 临时日志仅保存在系统临时目录，诊断输出过滤无关云空间账号与地图记录。
  Android 全量 logcat 中存在非 UTF-8 字节，读取时按 bytes 保留并替换解码，
  不把解码错误当成设备或应用运行故障。

## Phase 工作汇报：真机 UI 空间关系、手部输入与二次启动诊断（2026-10-24）

- 用户反馈低头时 HUD 下滑、UI 覆盖手且不能交互，以及第二次启动的房间障碍提示。
  本次 ADB 无设备，先进行了本地 Unity 运行验证，没有宣称已取到真实头显日志。
- 旧 HUD 使用完整摄像机姿态，每帧随俯仰移动；低头 35° 时中心下移 0.734m。
  改为约 0.65m 的空间面板，忽略俯仰，通过水平转向/位移阈值平滑跟随，
  指向 UI 时暂停跟随。修复后同样俯仰的面板位移为 0。
- UI 与 TMP 文字原先均为 Overlay、跳过深度测试；改为 Transparent 和空间深度，
  背景/文字直接采样 PICO 环境深度。真实手遮挡仍需设备验收。
- 缺少 XR Hands 导致 SDK 手部设备类型未编译。新增 XR Hands 1.4.3、
  SDK Input System 状态判定和 `PicoHandPoke` 标准 XRI 手指点按。
  增加 `PicoHandInputSetup` 修正 SDK BYTE 布尔控件的归一化，保留 SDK 的 bool 事件布局。
- 精确提示为 `Room obstacles removed the complete walkable surface.`；
  合成房间复现了地板被障碍盒清空导航的路径，真实障碍尺寸和跨启动差异仍未知。
  保留导航准入并增加几何诊断；增加 Android 最近两次离线启动日志，支持断开 USB 后取证。
- 工作分支为 `fix/headset-ui-room-20261024`，未重建主场景、角色或动画。
  C# 编译已通过，完整回归/构建进度见
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_UI与房间启动问题_20261024.md`。
- 最终 Android Development ARM64/IL2CPP 包构建通过，Unity 退出 0，475.5秒；
  签名、ZIP CRC、包名与三项主资产哈希通过。交付
  `/Users/bytedance/MateSoul/Builds/MateSoul_SpatialUI_20261024.apk`，
  183346980 bytes。实际 SDK bool 事件读取为 1，pointer/pinch 按下验证通过。
  数值证据为 `项目沉淀资料/validation/headset_ui_room_20261024.json`。
  当前无 ADB 设备，尚未安装或完成头显交互和二次启动验收。

### 真机安装与历史日志复核

- 用户连接 B3110 / swan 后，`adb install -r` 安装 SpatialUI 新包成功，设备包哈希与交付一致，保留存档。Launcher 返回 ok，PID 29505 持续运行，用户自行测试 UI，助手未再次重启。
- 回捞到旧进程 27357 在 16:53:55.824 的 `MATESOUL_NAVIGATION_FAIL`，精确异常与用户记忆吻合。旧日志没有保留完整几何，探针中的零值不代表地板查询失败。
- 新版在 18:08:45.596 复现同一导航故障：Room Capture 查询成功、地板 5.48㎡、18 个障碍、完整导航顶点 0，角色尚未启用；本轮未进入旧锚点准备阶段。
- 左右手子系统和追踪在真机正常启动，自动模式为 EyeHand；点击与遮挡体验仍由用户验收。离线日志成功落盘并拉取。
- 全部家具障碍旋转为零，SDK 示例不支持 Box3D 局部旋转而 provider 使用该返回值；该姿态转换是后续验证重点，尚未证实唯一根因。本轮未修改导航策略。
- 设备验证记录：`项目沉淀资料/validation/headset_ui_room_device_20261024.json`；
  原始日志在任务临时目录，结论已追加到真机问题报告。

### HUD 轻微俯仰跟随与房间原始姿态采集

- 根据用户反馈将 HUD 从完全忽略俯仰改为 25% 俯仰跟随、上下最多 8°，
  以当前可见导航栏/展开页的中心定位到下方 18°。点击和 poke 时固定，普通注视不固定。
- 展开页移到导航上方，字幕移到展开页上方；实际 Recenter 逻辑本地验证通过：
  导航和页面正视均 -18°，低头35°后 -26°，面板移动0.09068m，按下时不移动。
- 在 SceneCapture provider 增加原始地板顶点和 anchor/local quaternion 日志，
  尚未修改导航策略；下一步对实际房间回放定位无效局部旋转与障碍侵蚀问题。
- 诊断 APK 构建通过，443.18s，保护资产哈希一致；安装成功且设备hash一致，
  Activity start ok，PID31592。60秒观察中头显Asleep，无Unity/XR初始化数据；
  需要用户佩戴并保持唤醒才能采集原始几何。导航修复仍未完成。
- 临时证据：`/tmp/matesoul-room-ui-follow-20261024`；
  诊断包：`/Users/bytedance/MateSoul/Builds/MateSoul_RoomPoseDiagnostic_20261024.apk`。

## Phase H1 工作汇报：直接手部握取与官方虚拟手（2026-10-24）

- 用户要求真机放弃手柄/射线，采用伸手握取与直接触碰。已新增
  `PicoTrackedHand`、`PicoDirectHandInteractor` 和不透明 URP 手部材质，
  实例化 SDK 官方左右手骨骼模型；跟踪失效隐藏模型并安全释放物品。
- `PlayerInteractionModeController` 在旧场景启动时安装双手并关闭原射线；
  场景构造器和构建配置改用 HandsOnly，编辑器继续保留手柄模拟。
- 掌心距物品碰撞体表面 7 cm 内，新的张手→握紧触发拾取，0.75/0.35
  阈值迟滞；保持相对握取偏移。物品仍由原 XRI/ItemRegistry/PhysicsBody
  控制，未新增并行物理或角色动作执行逻辑。
- 手指 UI 点按、持物时的点按抑制、菜单双拳冲突保护、手边回收、
  家具和保龄球手掌下方放置已接入。旋钮保留近距触碰拖动，
  手指包覆 IK 和捏住旋钮扭腕不在本次已完成功能中。
- 初轮 C# 编译和 20 项生产组件行为检查通过；SDK 导入回调出现
  `GetProjectConfig` 在导入期间重建默认设置的问题，最后的 HandsOnly
  配置检查失败。已恢复 MR/追踪配置，并在复验及构建入口显式配置。
- 最终代码的 23 项自动检查已通过，新增覆盖自然弯指时食指点按以及
  未激活物品预览不释放小游戏的放置状态；完整 Android 包验证结果记录在专题报告中。
- 独立 APK `Builds/MateSoul_DirectHands_20261024.apk` 最终构建成功，
  Unity 退出0、257.42s、202838211 bytes，ARM64/IL2CPP。
  签名与原房间诊断包一致，1564项ZIP CRC通过；Manifest确认HandsOnly，
  主场景/角色prefab/Animator控制器哈希不变。结果：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/direct_hands_20261024.json`。
  尚未安装到设备或进行真人手势/遮挡验收。
- 完整结果见
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_直接手部交互改良_20261024.md`。
  真机手势识别、遮挡重影和物体接触手感须单独验收，不以自动注入姿态测试代替。
  不覆盖另一个任务正在使用的房间诊断包。

## Phase 工作汇报：真机房间导航修复与联合包安装（2026-10-24）

- 原始真机数据确认6个家具Box3D局部旋转为全零四元数，原先相乘丢失锚点朝向；
  `PicoSceneCaptureRoomMapProvider`在SDK输入边界归一化有效局部旋转，
  缺失时使用单位旋转，保留SDK尺寸和全部家具/墙体。
- 真实房间回放进一步确认：只修复旋转仍无导航，额外障碍膨胀与8cm粗体素共同
  清空狭小通路。`RuntimeRoomNavigation`删除重复安全膨胀，体素改2cm，
  角色半径0.25m、高度1.65m及最小区域阈值不变。
- 生产代码回放保留18个障碍，通过4个无碰撞胶囊站立采样点，生成8个导航顶点，
  烘焙29ms。剩余可导航中心面积仅0.01724㎡，不能认为已具备自由散步条件。
- HUD采用可见页面中心下方18°、25%俯仰跟随和±8°限幅；低头35°时移动约
  0.09068m，按下/近距poke时固定。保留用户最新直接手部输入、握取与放置代码。
- 最终联合APK构建通过，Unity退出0，112.62s，202838211 bytes；
  签名、1564项CRC、包名、ARM64/IL2CPP、HandsOnly及房间/深度Manifest通过。
  主场景、冷月Prefab与Animator控制器哈希不变。
- 已通过`adb install -r`安装至PB3A10MGL9220170B，6.87s，设备APK哈希匹配，
  既有存档安装前后哈希一致，安装前保存日志且未清空logcat。
- Launcher于19:04:01启动成功，PID5111；头显约11s再次休眠，63.76s观察无Unity/XR
  初始化。最终导航、冷月加载和连续两次启动仍待重新佩戴并保持唤醒后验证；
  UI实际视觉与点按体验同样未宣称通过。临时取证保留到实际验证结束。
- 交付：`/Users/bytedance/MateSoul/Builds/MateSoul_RoomFixed_UIFollow_20261024.apk`；
  数值：`/Users/bytedance/MateSoul/项目沉淀资料/validation/headset_ui_room_final_20261024.json`；
  根因、对照与验收状态见真机问题报告。未commit/push。

### 冷月加载复核与出生点搜索补修

- 用户佩戴启动后的PID8378在19:15:00.861成功生成NavMesh，18个障碍保留，
  真实设备证明前一轮障碍烘焙修复通过。随后出现“房间内没有可用的角色出生点”，
  冷月尚未启用；不能把NavMesh通过等同于游戏启动成功。
- `GameBootstrap`以玩家前方2.2m作为目标，却使用房间自身半对角线作为搜索距离。
  已改为目标到bounds.center的距离加bounds.extents.magnitude，单次搜索覆盖房间。
  15个生产导航回归全部通过，旧搜索漏掉13个；导航与物理障碍维持原样。
- `Builds/MateSoul_RoomSpawnFixed_20261024.apk`构建通过，291.87s，
  202841762 bytes；CRC/签名/包名/ARM64/IL2CPP/HandsOnly验证通过，
  受保护主场景、冷月Prefab、Animator控制器哈希不变。
- 已覆盖安装至真机，6.7s，设备APK哈希匹配，原存档未变化且日志未清空。
  当前存档为0物品、无房间锚点，后续连续启动不能声称覆盖旧锚点恢复。
- 新包自动启动PID9594在约11s再次Asleep，尚无Unity/XR初始化；
  保持佩戴后仍需验证出生点、冷月启用和两次MATESOUL_READY。
  当前不宣称冷月加载或连续启动验收通过。

## Phase 工作汇报：HUD 眼手点选与舒适距离（2026-10-24）

- 按用户最新反馈，HUD 可见面板中心从 65cm/下方18°改为45cm/下方30°；
  保留25%俯仰跟随与±8°限幅，点按期间完全冻结位置和朝向。
- 新增 `HudEyeHandInteractor`，只通过 `IUIInteractor` 操作专用 `MateSoulHUD`
  层。HUD同时支持眼手点选、食指触碰；虚拟物品、角色、电器面板保持近距离手部交互。
- `PicoTrackedHand` 增加捏合采样并提前到UI之前更新；当前帧物品接近、
  持有、握拳、触屏优先级、丢失跟踪取消和发起手锁定均在UI边界仲裁。
- 23项直接手部回归与22项HUD集成检查全部通过，共45项。交付包
  `Builds/MateSoul_HudEyeHand_20261024.apk` 与稳定导航包同一二进制；
  5个运行时源码的Android编译校验值、打包原生库、签名/CRC/ABI和眼手Manifest均已验证。
- 已确认设备安装哈希一致，跳过重复安装，保持PID14107运行；
  真机日志确认HUD距离0.45m、眼手与触碰输入初始化、冷月绑定和READY。
  真人眼动点选与舒适度待佩戴验收。临时Editor链接已移除，保留既有直接手部回归。
- 结果详见 `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_HUD眼手与舒适距离_20261024.md`
  和 `项目沉淀资料/validation/hud_eyehand_20261024.json`。

## Phase 工作汇报：冷月加载和连续冷启动真机验收（2026-10-24）

- 用户再次打开游戏后，休眠恢复PID9594仍遇到Scene Anchor查询runtime failure；
  立即完整冷启动PID10901又复现空导航，说明此前2cm体素/删除膨胀的修复仍不稳定。
- 72组几何回放定位到`.25㎡ minRegionArea`删除狭小导航区域，tile边界保留规则导致
  相同房间在不同朝向/平移后偶尔通过。最终将minRegionArea设0，保留2cm体素和
  全部18个障碍；2份捕获×6种朝向由1/12通过提高至12/12。
- 胶囊验收发现最近NavMesh边缘点可能碰到倾斜柜体。新增统一TryFindSpawnPosition，
  从最近导航点和三角形中心中选择地板内、完整角色胶囊不碰撞的最近点。
  12种生产代码回放和独立完整胶囊检查全部通过，未改角色半径/高度或删除障碍。
- 最新包`Builds/MateSoul_StableRoomStartup_20261024.apk`构建通过，406.32s，
  202848091 bytes；签名/1564项CRC/包名/ARM64/IL2CPP与保护资产哈希通过。
  已覆盖安装，6.81s，设备包哈希一致；原存档及标定保留，未清空日志。
- **两次连续唤醒状态完整冷启动通过**：PID13697于19:43:59.013 READY，
  PID14107于19:45:04.632 READY；冷月启用/绑定均完成，导航烘焙20/19ms。
  Bootstrap到READY为919/836ms（不是Android冷启动全耗时）。
  当前保留PID14107供用户测试，未再主动重启。
- 当前存档0物品、无roomAnchorId，本轮没有覆盖旧物品/锚点恢复。
  首次新包PID12938曾返回0锚点并进入native capture，随后没有地板；该问题及
  休眠恢复runtime failure仍独立未解决，不能把两次成功写成所有启动路径稳定。
- 临时Editor验证链接已移除，SDK就绪问题所需日志/诊断暂保留；
  当前HUD45cm/下30°及眼手输入代码未回退。未commit/push。
  最新验收边界见`项目沉淀资料/validation/headset_stable_startup_20261024.json`
  和真机UI/房间启动问题报告。

## Phase 工作汇报：透明遮挡移除与实时网格碰撞（2026-10-24）

- 确认语义包围框被额外创建为 `ColorMask 0 / ZWrite On` 的深度立方体：
  颜色不可见但会裁掉虚拟内容。移除 Scene Capture 地板代理和语义框的深度渲染，
  保留原房间碰撞箱、导航障碍和语义信息。
- 确认全屏环境遮挡在透明 UI 之后使用背景不透明深度裁剪画面。
  `RealityEnvironmentOcclusionFeature` 改在透明物绘制前执行；HUD 面板和文字各自
  使用环境深度。新增统一深度视野检查，防止视野外像素错误采样深度图。
- `RealityOcclusionMeshRenderer` 为运行时 Spatial Mesh 同步生成静态
  `MeshCollider`，与渲染共享测量网格；按 UUID 更新、移除并释放旧网格，
  跳过 Unchanged 块的重复物理烘焙。Environment Depth 可用时关闭粗网格深度渲染，
  物理碰撞保持工作；同源旧快照碰撞面由实时网格接替。
- `RuntimeRoomNavigation` 的安全出生点检查覆盖实时碰撞面。`GameBootstrap`
  在房间构建后绑定网格所有权，再搜索出生点；动作和物品仍由现有控制资源所有者执行。
- 网格精度从 Low 提升为 High，并固定在 `GameSceneBuilder` 构建入口。
  仅修改 YAML 曾被已加载的配置实例写回 Low，生产配置已改由构建入口明确设置。
- 20 项检查全部通过：真实落物在 0.8m 实时桌面停稳、网格更新到 1.1m、
  移除恢复、旧网格释放、无颜色遮挡框、深度与碰撞独立开关、三个着色器导入，
  以及实际 5.48㎡房间保留全部 18 个障碍下的安全出生点。
- 最终 Android APK 构建通过，225.1s，183925615 bytes；签名与1564项CRC、
  ARM64/IL2CPP、关键编译源码及打包原生库一致性检查通过。
  已覆盖安装且原存档哈希保留；佩戴视觉与真实接触验收尚未完成。
  主场景、冷月 Prefab 和 Animator 控制器均保持保护哈希；临时验证在
  `/tmp/matesoul-reality-20261024`，不覆盖其他窗口的输入改动。
- 精度边界：实时网格补齐包围框外的真实表面，语义框继续提供保守碰撞；
  Environment Depth 是视觉深度纹理，Spatial Mesh 才提供物理三角形。
  High 不代表毫米精度，未扫描区域、薄边与反光表面仍需真机验收。

## Phase 工作汇报：眼手卡住与物品按钮轻触修复（2026-10-24）

- 取消整张 HUD 前后5cm对眼手的屏蔽，改为实际可用控件接触仲裁；食指先于眼手处理。
  `PicoHandPoke` 改为持续注册的 UI 指针，5mm进入、15mm释放，
  不依赖进入速度；保留正常松手与失跟踪/持物/禁用时的取消事件。
- 新增 `PhysicalUiButton`，电视、收音机、饮水机、水壶和加热底座
  5个按钮类共7个按钮继承，接触当帧响应且退出不重复点击。HUD保持松手点击，
  世界保持近距直接手部交互；HUD45cm/下30°和操作姿态冻结保持。
- 捏合迟滞改为0.75/0.55。修复复用raycastPoints列表导致静止眼姿态下菜单命中不刷新的问题。
- 原23项直接手部回归与新63项完整UI/真实Prefab检查全部通过；
  其中包含左右手交替100次眼手点击、恢复/取消、持物、实体遮挡和标准拖拽。
  样本为注入姿态，不能作为真人眼手验收。
- APK `Builds/MateSoul_InputResponsive_20261024.apk` 构建退出0，353.34s，
  183925623 bytes；签名/1564项CRC/ABI/眼手及房间深度Manifest通过，
  主场景、冷月Prefab、Animator、既有直接手部测试及受保护导航源码哈希不变。
- 已安装至PB3A10MGL9220170B，设备APK哈希匹配。首个ADB客户端返回空错误，
  设备确认Session installed，因此仅复验没有重复安装。原存档存在且修改时间早于安装；
  首次安装前哈希未落盘，不声称完整前后哈希对比。
- Launcher启动PID24677后约10s休眠，16.94s内无Unity/HUD/READY；
  真人连续点选和轻触手感待佩戴验证。仅清理本轮Editor链接/meta，
  保留既有DirectHandValidation；未commit/push。
- 报告：`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_眼手卡住与按钮轻触修复_20261024.md`。
  数值：`/Users/bytedance/MateSoul/项目沉淀资料/validation/input_responsiveness_20261024.json`。

## Phase 工作汇报补记：现实碰撞修复的最终部署（2026-10-24）

- 当前最终安装包为 `Builds/MateSoul_RealityCollision_20261024.apk`，
  SHA-256 `7b9e6b33dabc4ba6d56383d09b1b1bbed97ac25a8cf32f6460fedf2de132a11e`。
  它已替代上一段 InputResponsive 部署，保留其眼手/按钮轻触修改。
  五个输入/HUD源码与最终 Android 编译文档哈希逐一一致。
- APK 内直接核对 High LOD=2、Spatial Mesh、环境深度、HandsOnly 和眼动设置。
  三个角色/场景保护资产哈希不变；设备包哈希匹配，安装前后已有存档哈希相同。
- 进程 PID30706 观察时为 Awake，但 PICO 查询返回空房间并进入系统采集，
  未出现 room_loaded / READY 或实时碰撞块日志。保留进程等待采集，不重复重启；
  20项回归、编译、安装均通过，真机低头视觉及现实接触仍待验收。
- 修复说明：`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_现实遮挡与实时网格碰撞_20261024.md`。
  数值记录：`/Users/bytedance/MateSoul/项目沉淀资料/validation/reality_collision_20261024.json`。

## Phase 工作汇报：修复启动空查询误入房间标定（2026-10-24）

- 本次原生日志确认：PID32169在3.503秒时查询0锚点后调用StartSceneCapture，
  1.325秒后系统才报告非零地图；没有标定数据删除的证据。
- RoomMapProviders改为先订阅SceneAnchorDataUpdated、立即查询、等待更新并每2秒
  有限重查询，25秒定位窗口；主游戏和独立导航均取消自动房间采集。
- GameBootstrap以单一startupTask串行启动/重试，失败时清理未完成会话；
  GameHud“设置→系统→重试定位”在Session为空时也可用。
- 去除本轮HTTP插桩后的生产类14项Unity回放通过，覆盖事件竞争、空查询、
  地板位姿延迟、错误、调用者取消、超时和再次读取；25.003秒空查询超时，
  25.054秒取消查询future。SDK输入为注入，不能当作头显定位验收。
- 诊断APK已构建/签名/CRC/ARM64验证并安装，存档前后哈希相同。
  诊断PID2022在业务启动前被系统安全边界界面暂停，系统等待稳定6DoF，
  10.2秒时头显休眠；房间恢复、冷月显示与连续启动仍待佩戴验证。
- 正式结果：
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_启动误入房间标定修复_20261024.md`，
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/room_relocation_20261024.json`。
- 最终部署：`Builds/MateSoul_RoomRelocation_20261024.apk`，183925623 bytes，
  SHA-256 `39c2650539cc582f3909f45324dca150aadd4cf0e9a2fefba66001026026b150`，
  BuildPipeline耗时249.097秒。签名/1564项CRC/ARM64/原生库/11个当前源码PDB校验通过，
  角色与场景三项保护资产哈希不变；已安装，设备包哈希匹配，安装前后与运行后
  存档哈希均为`c32fb27b536e8eba8cf427f27cca2ba6056de9704cf8ab8108ce4756dbd84114`。
- 最终冷启动PID3524同样在GameBootstrap前被系统6DoF/安全边界与10.6秒休眠阻断。
  房间就绪/冷月显示仍未取得真机证据，下一轮需要佩戴唤醒再确认。
  本轮HTTP插桩、Editor构建类/meta、debug记录/env/ndjson已删除，collector退出已确认。

## Phase 工作汇报：眼手冲突历史回查与故障采集（2026-10-24，待真人复现）

- 找到取消眼手前的原始会话代码：HUD使用原生`pinchTouched`和ActionBasedController，
  食指使用原生pokePose；当前增加自定义捏合/握拳迟滞、关节准入和触碰仲裁。
  这些差异已确认，真人卡死根因仍未确认；不把旧源码直接当作旧版绝对稳定的证据。
- 空Play模式场景回放实际PICO SDK UpdateHand→BYTE处理器→InputAction→
  ActionBasedController，6个原始模拟样本通过。捏合0.9→0.7时原生UI状态1→0，
  grasp保持0.6时仍可再次按下/松开。它验证历史输入语义，不能代替真人传感器验收。
  两次Editor模式采样因输入更新模式不代表运行时而废弃，均未据此修改业务逻辑。
- GameHud、PicoTrackedHand、HudEyeHandInteractor仅加入本轮输入日志：
  原生/处理后手势、触碰占用、UI down/up/click、焦点与CanvasGroup。
  所有根因假设仍为INCONCLUSIVE，尚未执行新的交互修复。
- 当前实际安装包已复验为
  `/tmp/matesoul-input-history-20261024/MateSoul_EyeHandHistory_PreFix_20261024.apk`，
  SHA-256 `cf14cdf1c92ce476ac48cefb78c17b2c5f414b3870a3d8d8d5a760e7b2f6273b`，
  183925623 bytes，148.1秒。它接续上一阶段RoomRelocation包，保留其最终生产逻辑。
  12个当前房间/输入/现实源码匹配Android PDB，APK原生库匹配构建库；
  签名/1564项CRC/ARM64/关键权限通过；角色/场景三项保护哈希与存档哈希不变。
- 头显部署时Asleep，真人眼手故障尚未捕获。保留本轮7789/PID17660采集服务与
  runtime插桩等待佩戴复现，HTTP失败时复用DeviceStartupLog的MATESOUL_INPUT_TRACE。
  此服务属于输入排障，不是上一阶段已清理的房间collector。
- 记录：
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_眼手冲突历史回查_20261024.md`，
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/eyehand_history_20261024.json`。

## Phase 工作汇报：UI眼手/触碰冲突修复（2026-10-24，待真人复验）

- 回捞最新真机运行得到269条结构化输入日志。眼手10次down/10次up只有8次click；
  食指4次down/4次up只有2次click。失败窗口内眼动、应用焦点和HUD CanvasGroup正常，
  48个仲裁样本没有触碰占用残留。
- 根因确认：PICO原生pinchTouched松开后，自定义IsPinching仍保持约734ms；
  第二次失败又被Closed门槛主动取消。食指旧状态机在按深/目标变化时只发up不发click。
  同时，非凸MeshCollider近物查询产生27,529条ClosestPoint警告，约130条/秒。
- PicoTrackedHand新增NativeTracked/NativePinching；HudEyeHandInteractor恢复原生
  捏合按下/松开语义，不再以Closed或完整手掌关节作为HUD准入。
  PicoHandPoke改为首次轻触后相邻UI帧完成一次click，退出15mm后重新武装。
  PicoDirectHandInteractor对非凸网格改用bounds最近点，并限制每手每帧刷新一次。
- 空Play Mode + 实际XRUIInputModule回归6项通过，覆盖Closed隔离、原生当帧释放、
  单次轻触、接触去重、退出重入和非凸网格无告警。正式Android构建耗时4分03秒。
- 最终包`Builds/MateSoul_UIConflictFixed_20261024.apk`，202921796 bytes，
  SHA-256 `96afed789cafd793c357b7d02983831e2b52ad4a2c7e4efc430432272b9491fa`。
  签名/1564项CRC/ARM64/IL2CPP/当前源码PDB通过，角色与场景保护哈希不变。
  已安装且设备包哈希一致；存档安装前后哈希不变。
- 头显安装时Asleep，仍需佩戴后连续眼手点击和食指轻触，回捞`runId=post-fix`
  作为最终真人验收。自动验证通过，但当前不写“真人验收通过”。
- 记录：
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_UI交互冲突根因与修复_20261024.md`，
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/ui_interaction_conflict_20261024.json`。

## Phase 工作汇报：房间定位改为生命周期持续等待（2026-10-24，待佩戴验收）

- PID5177真机证据确认：PICO第8次云定位识别到历史地图
  `4d8d4423-76ea-4b0f-a009-961f54978966`，score 0.322153、
  1613个inlier；但MR引擎仍为`status=7, mapUuid=[]`，SceneCapture保持0锚点。
  旧版25秒后停止provider，导致角色、Session和物品恢复均未开始。
- `PicoSceneCaptureRoomMapProvider`删除固定25秒deadline。现在由GameBootstrap
  生命周期持有单一provider，事件更新或每2秒查询直到真实房间有效；
  应用退出/销毁时取消并清理。权限等非暂态错误仍立即失败，不自动进入标定。
- 未使用Spatial Mesh伪造真机房间；它继续用于实时精细碰撞。
  角色保持在Room.Build/NavMesh完成后启用，物品仍在Session创建后恢复。
- Development构建通过，BuildPipeline 803.979秒。最终包
  `Builds/MateSoul_RoomPersistentWait_20261024.apk`，183394838 bytes，
  SHA-256 `0b54e7d0aeb0b83715f5863e717b990d7b65e1cf62e79c081447bd9732a24205`。
  签名v2/1564项CRC/ARM64/8个当前相关源码PDB通过，三项保护资产哈希不变。
- 已覆盖安装，设备包哈希一致；存档安装前后SHA-256均为
  `c32fb27b536e8eba8cf427f27cca2ba6056de9704cf8ab8108ce4756dbd84114`。
  当前UI眼手冲突修复源码随包保留，输入collector PID17660健康。
- 无人值守PID16134在GameBootstrap前被系统安全边界接管并约10秒后休眠，
  未取得房间READY证据。需佩戴保持唤醒环视；当前状态为
  `awaiting_human_room_recovery`，不能写为真机恢复通过。
- 记录：
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_启动误入房间标定修复_20261024.md`，
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/room_persistent_wait_20261024.json`。

## Phase 工作汇报：MR安全策略配置修复与新启动取证（2026-10-24，待佩戴验收）

- 确认旧APK `enable_mr_safeguard=0`，与真实房间MR应用用途不匹配。
  SDK说明未选该项默认VR安全策略；原生捕获safeMode=1、
  same6dof=false、dataAlgorithm=null及VR_NO_JUMP，系统UID1000在Unity业务前
  打开房间/边界页面。系统持久房间存在，而当前查询map仍空，二者不能混同。
- `PXR_ProjectSetting.asset`改为mrSafeguard=1；GameSceneBuilder现有能力配置
  固定该值。本轮首个业务diff先加入RoomMapProviders三处诊断，再依据原生/
  SDK/Manifest证据修复。不清除标定/存档，不关闭安全边界，不重建角色。
- 本次编译同时发现最新RuntimeColliderRefiner把Unity2022返回void的
  Physics.BakeMesh赋给bool；改为调用完成后设置已有标志，脚本编译通过。
- 使用已保存主场景增量Development构建，BuildPipeline139.513秒。
  APK `Builds/MateSoul_MRSafety_20261024.apk`，202950656 bytes，
  SHA-256 `f3314afb7744c2b45f45ee9a3bd137ae1e6e7b788348eda5f21fa4951c2ae596`。
  最终MR标志=1，签名v2/1564项CRC/ARM64/10个当前源码PDB以及IL2CPP产物校验通过。
  Unity遥测目录写入被沙箱拒绝，但实际构建成功与APK校验已独立确认。
- 已覆盖安装，设备实际包哈希匹配；场景、冷月Prefab、Animator三项保护资产
  哈希不变。安装前、后和运行后存档均为
  `c32fb27b536e8eba8cf427f27cca2ba6056de9704cf8ab8108ce4756dbd84114`。
  当前输入与现实碰撞修复源码完整保留。
- PID19672已取得新的bootstrap_start、scene_provider_started；
  本次62.2秒观测没有捕获旧系统接管分支，确认本次越过业务启动前阻断。
  首次真实query SUCCESS/0锚点/2次更新/3352ms/focused=1；
  第10.2秒已Asleep，后续均休眠，没有room_loaded或READY。
  配置修复通过不等于完整房间恢复通过，仍需要佩戴保持唤醒验收。
- 本轮HTTP未直接收到设备事件、后续ADB reverse为空；
  已从正常DeviceStartupLog导入真实post-fix查询，明确标记离线来源。
  RoomMapProviders诊断区域及7792/PID45626保留等待真人验证；
  输入7789/PID17660是其他窗口采集端，继续保留。本轮构建辅助链接移除。
- 报告/数值：
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_MR安全策略与房间启动修复_20261024.md`，
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/room_mr_safety_20261024.json`。

## Phase 工作汇报：眼手后直接手失效修复（2026-10-24，待真人复验）

- 最新真机post-fix日志证明上一轮HUD修复有效：眼手7/7 click、食指3/3 click，
  canceled=0且没有触碰占用残留；ClosestPoint告警也为0。
- 新根因是共享Closed污染直接手：右手65个样本中20个Closed，17个发生在
  原生捏合结束后，且20个均held=0/nearItem=0。最后一次眼手点击后Closed持续
  约6.36秒，grasp降至0.250才退出，132ms后又因0.820重新进入。
- PicoTrackedHand改用PICO原生graspFirm；眼手捏合期间及其尾部抑制Closed，
  直到原生graspFirm释放。PicoDirectHandInteractor在任意非firm状态立即重新武装，
  CanPoke不再被Closed误屏蔽，持物抑制UI和远处不吸取规则保持。
- 抓取隔离专项7项、既有直接手23项全部通过。同步兼容并行手部物理代码：
  Unity2022的Physics.BakeMesh按void调用；Collision双方Collider从ContactPoint读取。
- 最终包`Builds/MateSoul_HandAfterEyeFixed_20261024.apk`，202949723 bytes，
  SHA-256 `8ae8b3b7db01d6c1c8415ddbeb1c3a5292ef89271eec6a06ee1f66f40430bb02`。
  同批构建184.527秒；签名/1564项CRC/ARM64/IL2CPP/9个手部物理源码及
  11个关联源码PDB校验通过，MR safeguard=1和最新房间代码均保留。
- 首次安装因USB ADB转offline失败；确认旧包未替换后有界重连并再次安装成功。
  设备APK哈希匹配，安装前后存档哈希不变，角色/场景保护资产未变。
- 新包使用`runId=post-fix-2`。下一轮需眼手点击后立即触碰HUD和近距拾物，
  连续3轮并保留10秒日志；通过前不写真人直接手验收通过。
- 报告/数值：
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_眼手后直接手失效根因与修复_20261024.md`，
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/hand_after_eye_conflict_20261024.json`。

## Phase 工作汇报：实际安装核对、房间恢复与旧物品支撑故障（2026-10-24，排查中）

- 用户再次反馈启动仍异常后，直接读取设备base.apk计算完整SHA：
  `8ae8b3b7db01d6c1c8415ddbeb1c3a5292ef89271eec6a06ee1f66f40430bb02`。
  与HandAfterEyeFixed包一致，23:13:20安装；包含MR/房间修复及最新手部修复。
  当前实际包已由另一窗口从MRSafety更新，不能继续仅报先前f331包。
- 23:13失败新日志持续46秒、10次SUCCESS/anchors0/updates2/focus1，
  无room_loaded。该失败不能再归因于无人值守约10秒休眠。
- 不重新安装、只主动打开同一个8ae8包，23:18 PID25763取得真实地图b371...、
  22锚点/313ms，room_loaded/navmesh_built/avatar_bound/business_ready齐全；
  导航24ms，15秒probe Awake。证明包正确且地图有效时能启动，
  不证明所有定位状态已稳定恢复。
- 独立物品故障已确认：旧房间锚点located=True，但10件物品因
  “旧物品缺少可靠支撑”全部回滚；READY items0/recovery，原存档保留。
  后续新运行的实时存档变为4件，牛排needsGravitySettling且y持续降至-51041，
  这属于新物理故障证据，不冒充原10件布局失败的原因。
- GameSession仅新增support-pre-fix支撑诊断，没有改恢复规则。诊断包构建正在完成；
  等待并保持其他窗口Unity回归进程不被中断后再构建。所有脚本/证据在/tmp，
  Editor只有本轮临时链接。下一步逐件支撑命中数据→最小修复→设备对比。
- 报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_安装核对与旧物品恢复故障_20261024.md`。

## Phase 工作汇报：地板碰撞与异常物品回收通过，锚点重查验证中（2026-10-24）

- 逐件真机诊断确认6件坠到地板下4万至24.5万米、1件出地板无支撑，
  导致正常3件一起回滚。RuntimeRoomNavigation在非debug时跳过地板Collider
  是物理漏底原因；现在始终建不可见RealitySurface地板Collider。
- GameSession按既有库存单位回收落出房间的旧物品；运行期复用RecycleItem
  回收地板下1m、未持有且未占用的物品，避免继续污染存档。
  真实虚拟重叠与室内无支撑仍拒绝。
- 隔离Unity回放通过：原10件/原房间地板，3件恢复、7单位回收；
  runtime跌落回收和库存守恒通过，Collider1/Renderer0，不写用户存档。
- 13项既有回归12通过，InvalidLayouts的missing定义构造失败与修复前相同。
  floor-recovery-tests.xml和support-replay-result.json保留外部临时目录证据。
- fd8df1ff...0760e包安装验证通过，MR safeguard1，当前主场景/角色/controller/
  手部源码保护SHA通过；安装前后存档b9563447...42f21fa一致。
  Unity设备扫描重启ADB曾打断安装，核对旧hash后串行重装才确认成功。
- 单次wake后同进程跨休眠取得22锚点、角色绑定；4旧物品仍恢复失败。
  新证据为prefetch在3.35s失败但房间74.50s才激活，旧世界rootY0.024、
  当前floorY0.603，空间原点已变。GameSession对失败/取消prefetch增加
  房间就绪后的单次锚点重查；此修改构建/设备复验进行中，不能声称全流程通过。
- 报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_安装核对与旧物品恢复故障_20261024.md`。

## Phase 最终交付核对：房间地板与锚点重查合并包（2026-10-24，真人验收待完成）

- 已安装`Builds/MateSoul_RoomAnchorAndFloor_20261024.apk`；
  本地/设备/最终复查SHA一致：
  `c4b3ba300acaf243ae4fbcdf01b087d6817bdd70571c3c14f672aa5ccc007096`。
  Build225.24s，MR1、签名、CRC、ARM64/IL2CPP、10源码PDB通过；
  场景/角色/controller保护及安装前后存档hash通过。
- 合并了并行最新手部物理/握持修改；修正其TrackedHandPhysics if/else语句
  位置和临时HandComfortValidation缺少Wardrobe命名空间的编译错误。
  构建途中源码变化的产物没有交付，最终通过全部当前源码一致性检查。
- 锚点重查专项隔离回放通过：faulted prefetch+历史worldY=-5时重新加载
  活动锚点，3件恢复、7单位回收、runtime跌落回收和库存守恒仍通过。
- 最终设备60秒全Asleep，只到scene_provider_started，未取得READY；
  需要用户佩戴保持唤醒启动约1分钟才能完成真机验证，不宣称全修复。
- 临时Editor回放/构建链接及meta已清理；房间诊断/7792保持，其他窗口
  输入诊断/Unity不动。数值`项目沉淀资料/validation/room_install_restore_20261024.json`。

## Phase 工作汇报：宽松抓取与冷月局部柔性反馈（2026-10-24，真机手感待验收）

- PicoTrackedHand / PicoDirectHandInteractor 以独立 WorldGrip 支持宽松侧握，
  0.42进入、0.22明确张手、0.16s释放驻留、0.28s跟踪宽限。选择后保持，
  不再按每帧指尖接触反复释放；原生眼手/HUD Closed机制保持分离。
- TrackedHandPhysics 的17段变为真实压入传感器，单手/单目标/单物理帧
  合并接触；PhysicsBody.ApplyHandPush 限制手部驱动0.55m/s、2.5m/s²，
  消除运动学手的无限质量冲量及多指重复施力。固定物和持有物不受同手推力。
- AvatarPhysicalResponse 从生产蒙皮拟合69个内收细分碰撞面，contactOffset2mm，
  头部正确随Head1。恢复原左右牵手点及既有IK、腕部限制、牵引和跟随动作。
- 局部阻尼弹簧带动受力关节及邻近骨骼，肘膝限制轴向和伸直。仅较强躯干接触
  持续0.14s才请求一次平衡步，AvatarMotor负责根节点，程序脚部IK交替抬脚。
  Body/LeftArm/RightArm/Locomotion分别通过共享动作协调器持有控制权。
- 生产角色+真实PhysX+受控样本专项26/26通过，既有牵手/抓取/物理/
  遮挡/房间导航回归25/25通过。轻推骨骼1.456°、根位移约0；
  较强推力位移2.475cm，双脚各抬约2.497cm；轻推物品速度0.04798m/s。
- 编辑器验证必须完成Wardrobe初始化与RestoreItemsAsync，后者解除启动门禁；
  XRI EditMode需要在指定Collider集合后重新注册。一次Burst退出崩溃后，
  验证命令关闭Burst编译，真实PhysX照常运行。旧版物理断言不冒充全仓通过。
- 实时网格、不可见地板、锚点重查及最新UI修复均沿用共享工作树；
  真人握取手感、动态衣物穿插、动画叠加和头显性能仍待验证。
- 专项说明：
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_抓取舒适度与冷月柔性反馈_20261024.md`；
  数值：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/hand_comfort_20261024.json`。
- 已生成`Builds/MateSoul_HandComfort_20261024.apk`，215490357字节，
  SHA-256 `a7dd75abf3b34a63e1c04f12816cffb3b492f930f4aa911deb2d72c37c85f5de`；
  BuildPipeline303.44s、签名v2/1564项CRC/ARM64/IL2CPP原生库一致性通过，
  MR1、本轮8源码PDB匹配、25项保护资产哈希不变。临时Editor入口已清理，
  原始数值/既有测试XML保留项目validation目录，复验脚本保留/tmp。
- 此包未安装；并行窗口构建期间更新的GameSession/RealityMeshSource/
  RuntimeRoomNavigation实时房间实验晚于本包编译，不能宣称包含其最终代码。

## Phase D37 工作汇报：实时房间几何、穿地与环境深度（2026-10-25）

- 重新核对设备旧包SHA为c4b3ba...007096，原生ring和当前/上次启动日志已读取；
  当前PID9285采样时Asleep，不能据此声称真人故障消失。
- 真标定地板受控PhysX回放复现Discrete快速漏检：4cm球10m/s下降，
  y=-7.524663；默认ContinuousSpeculative后y=.040924，无穿地。
  速度是受控实验值，非用户现场测量。
- 本地PICO Unity SDK深度生命周期实验复现reset保留旧ID42；
  修复清ID/纹理/可用开关、每帧预处理复用swapchain图像、绑定tracking空间。
  回放确认reset不再留旧ID，双眼真机视觉仍待验证。
- 实时mesh变更以UUID合并、先cook新碰撞再撤旧面；2ms软预算。
  16cm障碍格、250ms合并、UpdateNavMeshDataAsync增量更新稳定地板导航。
  已测量覆盖的旧家具障碍由实时数据替换，固定墙/未测量区域继续保守保留。
- RealityMeshSource缓存变化块，删除旧标定语义强制投射；现有Chair/Bed
  scanner和动作协调器继续验证目标、撤销能力与reservation。
- 受控导航回放通过：移1.28m旧孔释放/新孔阻挡、Removed释放、
  相同投影跳过；编辑器移动/移除异步2.86/2.16ms，不冒充头显性能。
- 现有27项定向回归全通过。主场景增量APK正在构建/核验，
  保留最新手部舒适度修改，不覆盖其他窗口dirty文件。
- 正式记录：
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_实时房间几何与穿地遮挡修复_20261025.md`。
  PICO保证视野内变化网格更新，不能承诺视野外移动立即发现或High为毫米精度。

## Phase 安装核对：手部舒适度交付包（2026-10-25）

- 按用户要求覆盖安装`Builds/MateSoul_HandComfort_20261024.apk`到
  `PB3A10MGL9220170B`，ADB返回Success。设备APK完整SHA与本地一致：
  `a7dd75abf3b34a63e1c04f12816cffb3b492f930f4aa911deb2d72c37c85f5de`。
- 安装前后存档SHA均为`b956344702cc90fbd79e1678045d3dce4c60d60cb722dffe05ece88f042f21fa`，
  内容不变。安装在共享Unity构建结束后执行，未自动启动；真人手感待验收。
- 本次安装的是已交付手部舒适度包，不代表安装了另一窗口后续实时房间几何包。
  核对记录：`项目沉淀资料/validation/hand_comfort_20261024.json`。

## Phase D37 安装与运行证据补充（2026-10-25）

- 最新完整主场景包已安装：
  `/Users/bytedance/MateSoul/Builds/MateSoul_LiveRoomGeometry_20261025.apk`。
  215489136字节，SHA设备最终复查一致
  `5dfdefee9577bd43a82079a542e847d85c0306ca334687e71e1d424d9deaeb05`。
  包含手部舒适度与本轮实时房间最终源码；13源码PDB、9保护文件、
  ARM64/IL2CPP原生库、签名、1563项CRC通过，安装前后存档不变。
- PID14459新日志确认10网格/有效环境深度/raw512/processed512；
  随后50s全程Asleep，房间READY与真人移动椅子/穿地/双眼视觉仍待验收。
  不应再安装只含此前手部编译时点的包覆盖这一交付。
- 本轮临时Editor实验入口与meta已删除，未修改生产角色/场景/动画Clip。
  数值：`项目沉淀资料/validation/live_room_geometry_20261025.json`。

## Phase 交互研究：释放黏手、实体感与拾取方式（2026-10-25，方案未实施）

- 对照Unity XRI、Meta Hand Grab/First Steps、Ultraleap Physical Hands/
  Grab Detector共5份官方资料，确认当前160ms固定释放等待、捏取/握取共用
  释放语义、Trigger-only接触、提前抓取偏移保留及手物更新时间不同。
- 推荐清晰释放直接响应、模糊输入短防抖；按参与抓取的手指判断释放。
  接着补正确抓握面/附件点、手指包覆和实际物品姿态下的可见手，再优化
  PhysicsBody速度前馈与释放历史。不把运动学手段直接改成实体碰撞来增强推力。
- 本轮只形成源码分析和工程建议，没有修改运行时、构建或安装。30–50ms
  模糊输入防抖及80–120ms追踪宽限是待真人验证起点，不是业界标准或实测值。
  手部3源码与最新LiveRoomGeometry交付记录一致；后续基于该合并版本实现。
- 分析报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_手部抓取手感工程分析_20261025.md`。

## Phase D37 真人失败回捞：墙外角色、遮挡与后续启动（2026-10-25）

- 用户实际反馈墙外角色/无遮挡及后续无业务加载，D37真人验收失败仍OPEN。
  回捞设备SHA仍与5dfdefee...eaeb05交付一致，本次未改业务或重新安装。
- 后续PID17225/17599共8/11次空SceneCapture查询，36.5/51.5s focused1；
  原生当前mapUuid为空，LoadSceneAnchors全零UUID、exist map0、3s超时。
  因房间provider未返回，角色绑定和物品恢复完全没进入；不是资源损坏。
- 存档锚点locatedTrue、实时网格11块、深度纹理512有效，但不能代表标定
  当前房间可用或遮挡生效。用户最终视觉失败推翻“纹理到达即可验收”的判断。
- 首次PID16015后半段sessionReady1/floorCollider1；最初出生pose记录已覆盖。
  墙外具体原因和GPU遮挡根因仍INCONCLUSIVE；没有把猜测打包成新修复。
- startup128KiB上限被输入诊断37/40s内填满，只保留两次启动。需要修正
  诊断留存及房间准入，验证实际地面/墙边界与GPU最终像素。
- 报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_墙外角色与后续启动阻断日志分析_20261025.md`。

## Phase 自然抓取：快速释放与显示手接触（2026-10-25，真人手感待验收）

- 完成PicoTrackedHand/PicoDirectHandInteractor按捏取、掌握记录参与指组。
  清晰释放当次更新退出；模糊区间40ms防抖、追踪失效100ms安全释放。
  重复捏取无需全手张开，远处握拳不吸取；HUD原生输入合同保留。
- 取得锚点投影到邻近物面，复用合适的ItemInteractionProfile抓握几何，
  保留物品初始姿态。候选先过滤接触兼容性、再评分并保留持续候选偏好。
- 新PicoHandContactVisual在官方追踪手之后做有界显示修正，复用原17段
  Trigger查询掌心/指节接触，闭合路径首次接触停止。显示骨骼不写回真实
  手势缓存，保持物品PhysicsBody和XRI选择各自单点所有权。
- PhysicsBody增加跟随线/角速度前馈；PhysicsGrabInteractable保留最近80ms
  实际刚体速度用于运动释放，追踪失真丢弃速度。推力上限和固定物合同保持。
  手Shader增强立体明暗/阴影/弱高光，抓放提供轻音提示。
- 官方双手+真实XRI/PhysX专项32通过，既有牵手/抓取/物理/遮挡/导航回归
  28通过，共60通过。受控跟随误差21.26→9.99mm，墙前仍停；掌心限位、
  指节第一接触、输入隔离及渲染对照均验证。编辑器结果不冒充头显手感。
- 遇到Unity包解析中止后复用本机同版本包缓存；没有升级/降级依赖。
  临时Editor入口不保存场景，不重建角色/Clip。并行房间修复按当前文件
  合并构建，批处理串行，交付前核对手部及房间源码PDB校验和。
- 说明：
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_自然抓取与手部接触实现_20261025.md`，
  数值：`/Users/bytedance/MateSoul/项目沉淀资料/validation/natural_grasp_20261025.json`。

## Phase D38 工作汇报：房间安全回退、实测出生点与遮挡后备（2026-10-25）

### 功能完成情况

- `GameBootstrap`为真机Scene Capture增加12秒外层硬截止。空房间或SDK查询
  挂起时取消旧任务并切换PICO Spatial Mesh，避免角色绑定和物品恢复无限等待。
- `RoomMapProviders`优先使用PXR缓存网格，只停止自己启动的mesh subsystem；
  由16cm地板栅格最大连通组件生成凹轮廓，不再用全局凸包跨越L形凹区。
- `RuntimeRoomNavigation`在实时表面存在时要求spawn命中实测水平
  `RoomSurfaceCollider`且高度误差不超过15cm，并覆盖整个房间搜索候选。
- 遮挡保留Environment Depth全屏Pass和实测网格depth-only后备；
  Player/场景构建固定`preserveFramebufferAlpha=1`，首帧记录格式和alpha状态。
- `DeviceStartupLog`过滤高频输入日志，保留房间、spawn、遮挡、角色和物品关键链路。
  未恢复用户已删除的`HandComfortValidation.cs`和`LiveRoomGeometryReplay.cs`。

### 编译与测试

- 房间/遮挡专项12/12通过。组合回归33/35；本轮新增测试全部通过，
  两项失败均为既有`SeatingTests`无图形/NavMesh时序用例。
- Android ARM64/IL2CPP增量构建exit0，100.07秒；保护的场景、角色、
  Controller及并行手部源码哈希均未变化。
- 最终包：
  `/Users/bytedance/MateSoul/Builds/MateSoul_RoomFallbackOcclusion_Final_20261025.apk`，
  202884057字节，SHA
  `4f24c35e3b287c4ce9abd2713d0e07ac2935384db2a8ae65a34e0d683d154f8f`。
  本地/设备SHA一致，v2签名、1563项CRC、ARM64/IL2CPP和空间权限通过。
- 覆盖安装保留存档；安装前、安装后、单次启动后SHA均为
  `d583d595759ecedaf70b2cdd68700a07113d99577a486df3ebfde99c98463540`。

### 真机后验与剩余验收

- 最终包PID16900冷启动无崩溃，frame1确认遮挡Pass运行：
  `alphaPreserved=True format=R8G8B8A8_SRGB`。
- 未佩戴头显约3秒后触发onPause/onStop，Unity在`GameBootstrap`前暂停；
  因此本次没有执行12秒fallback、spawn、物品恢复和READY，不能冒充真机通过。
- 后续只需持续佩戴启动一次并环视，验证fallback/READY、墙内spawn、
  旧物品可见和最终双眼遮挡。调试会话与关键插桩在此之前保持OPEN。
- 正式报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_墙外角色与后续启动阻断日志分析_20261025.md`；
  数值：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/room_fallback_occlusion_20261025.json`。

## 自然抓取最终交付与安装（2026-10-25）

- `Builds/MateSoul_NaturalGrasp_20261025.apk`已安装，设备与本地SHA一致：
  `e2335100b7793251079306ea47ee1f2c10a50522816efb2db6aeb5946e6b8ef9`。
  包大小195593241字节，增量构建46.38s，签名v2/1564项CRC/ARM64/
  IL2CPP及原生库一致性通过；当前15个手部、HUD、房间及SDK源码PDB通过。
- 包含当前房间fallback/遮挡修复和本轮自然抓取修改；替换前设备包为
  `4f24c35e...54f8f`，不回退到旧HandComfort版本。25保护资产哈希不变。
- 安装前后存档均为`d583d595759ecedaf70b2cdd68700a07113d99577a486df3ebfde99c98463540`；
  没有自动启动。32专项+28回归、渲染对照通过；真机手感和房间READY仍待佩戴验收。
- 本轮临时Editor验证入口及meta已移除。复验脚本/中间图像留在/tmp，
  数值和原始自动结果留项目validation，最终交付未包含临时媒体。
- 说明：`项目沉淀资料/MateSoul_自然抓取与手部接触实现_20261025.md`，
  数值：`项目沉淀资料/validation/natural_grasp_20261025.json`。

## Phase D39 工作汇报：无地板语义的实时网格准入修复（2026-10-25）

- 用户佩戴复验仍只有UI。PID19751日志确认前一版12秒Scene Capture deadline
  正常触发，8块实时网格、mesh depth和512环境深度均可用。
- 回退后23次Mesh查询全部SUCCESS并完成snapshot，但没有`room_loaded`，
  最终抛出navigation-ready异常；PICO原生日志同步返回
  `online_mesh floor_height=inf floor_status=-1`。
- 根因是旧provider只接纳Floor语义三角形并要求4m²；当前网格有实测几何，
  但系统未输出Floor语义，因此角色和物品仍被房间准入阻断。
- 修复保留语义Floor优先；不足时只使用XR Floor高度±30cm、近水平、
  Unknown语义的真实三角形。专用最小连通面积1m²，仍保留16cm凹轮廓；
  桌面高度未知面明确拒绝，spawn仍要求实测Collider支撑。
- RoomNavigation 7/7、房间+遮挡组合14/14通过。共享源码增量IL2CPP构建
  3分11.27秒成功，场景/角色/Controller和最新自然抓取/PhysicsBody保护hash不变。
- 已覆盖安装
  `Builds/MateSoul_MeasuredFloorFallback_20261025.apk`，215323018字节，
  SHA `d5d0a9a9777fe6f432f1bb99863ee482fb4fcee329acb2988c69878d61f44237`；
  本地/设备一致，签名v2、1564项CRC、ARM64/IL2CPP通过。
- 安装前后存档SHA均为`d583d595...463540`。安装后设备Asleep，需用户持续
  佩戴再启动，验收room_loaded、spawn、角色/物品和最终遮挡。
- 数值：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/measured_floor_fallback_20261025.json`。

## Phase D40 工作汇报：低头透明遮罩与HUD下移（2026-10-25）

> 本阶段关闭实测网格的方案已被真人复验否决：现实对虚拟物品/角色的遮挡
> 消失。11项配置测试不等于视觉通过；恢复方案和像素验证见D41。

### 功能完成情况

- 用户佩戴复验确认D39包已正常加载冷月和虚拟物品，房间准入阻断已解除；
  随后确认低头时有不可见形状裁掉UI和虚拟物，且HUD仍偏高。
- 现场日志同时满足`nativeMeshes=8~9`、`meshDepth=true`、
  `EnvironmentDepth=true/raw512/processed512`。HUD两个专用shader还在做
  场景ZTest和Environment Depth alpha裁切，因此存在两套视觉遮挡源及HUD
  误参与现实遮挡。
- `UiAlwaysOnTop.shader`和`SpatialUiSdf.shader`改为`ZTest Always`，
  移除Environment Depth include、世界坐标和alpha乘法。HUD交互raycaster、
  Canvas排序和世界空间交互距离保持不变。
- `GameHud`的可视面中心增加`-0.10m`固定世界Y偏移，不通过改变面板尺寸或
  跟随距离实现下移。
- `RealityOcclusionMeshRenderer`改为单视觉owner：Environment Depth有效时
  关闭native measured mesh renderer；深度不可用时再启用实测网格后备。
  MeshCollider与增量导航始终保留；Scene Capture代理永不写视觉深度。

### 编译、测试与交付

- `RealityOcclusionTests` 11/11通过，覆盖HUD shader合同、10cm下移、
  Environment Depth接管时renderer关闭但collider继续启用，以及深度失效时
  measured mesh后备恢复。
- Android ARM64/IL2CPP增量构建成功，耗时2分37.30秒；IL2CPP C++中可见
  `CalculateVisibleCenter`、`ApplyDepthRendering`和
  `ShouldRenderMeasuredMesh`。场景、角色prefab、Controller、PhysicsBody和
  TrackedHandPhysics保护hash未变化。
- 并行牵手诊断代码的两个插值JSON字符串各多一个右花括号，Development
  Player编译报`CS8086`；仅删除多余括号后构建通过，未改牵手状态机。
- 最终包：
  `/Users/bytedance/MateSoul/Builds/MateSoul_OcclusionUiLowered_20261025.apk`，
  215331012字节，SHA
  `6115c646a86f1a33f39374d684ff91d9690a18d4c53f33fe9d440892bf7b3c17`。
  本地/设备完整SHA一致，v2签名、1564项CRC、zipalign、ARM64/IL2CPP通过。
- 覆盖安装前后和启动后存档SHA均为
  `6952fb70e18fa32a3c25f3ed30e55c2836cd492a3994d6ebd970134e656bd3e3`。

### 真机后验与后续

- 新包运行日志已确认9块native mesh存在时
  `meshDepth=false/environmentDepthOwnsVisuals=true/depthAvailable=true`，
  raw/processed depth均512；说明新的视觉owner切换已在真机生效。
- 无人佩戴观察期间Activity被系统切到后台，未完成本次业务READY；未见
  FATAL/ANR。透明遮罩是否消失及HUD高度是否合适仍须用户佩戴复验，
  调试会话保持OPEN。
- 数值：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/occlusion_ui_lowered_20261025.json`。

## Phase D41 工作汇报：恢复现实对物品与角色的遮挡（2026-10-25）

- 用户确认D40包使现实遮挡消失。核对设备仍为SHA `6115c646...7b3c17`，
  实际佩戴日志已到READY，持续9～10块native mesh存在且meshDepth=false，
  Environment Depth纹理512有效。回归来自本窗口关闭实测网格的错误改动。
- 撤销Environment Depth有效时关闭mesh的判断和对应字段/方法。native mesh
  始终提供视觉深度，暂未到达时使用Spatial Mesh实测快照；不渲染语义盒。
- 保留D40 HUD两个shader的深度隔离和10cm下移，场景、角色prefab、
  Controller与并行PhysicsBody/TrackedHandPhysics均无变化。
- GPU/URP相机像素测试通过：无遮挡物品=(1,0,0,1)，遮挡后=(0,0,0,0)，
  HUD=(0,1,0,1)。专项12/12通过；不再以shader字符串或renderer开关代替
  视觉遮挡验证。此前关于“mesh双开导致透明块”的具体根因未证实，撤销结论。
- 构建/设备交付及存档保护见：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/occlusion_restored_20261025.json`。
- 主场景ARM64/IL2CPP构建成功3分13.55秒；已覆盖安装
  `/Users/bytedance/MateSoul/Builds/MateSoul_OcclusionRestored_HudLowered_20261025.apk`，
  215331582字节、1564项CRC、签名v2、zipalign均通过。本地/设备完整SHA：
  `1b0c0f2fe4fd09c452f420b407b4a19e74c6b4ba967f0726fa8b82f546e7eac7`。
- PID10647后验确认`nativeMeshes=10/meshDepth=true/depthAvailable=true`，
  raw/processed512；安装前后及启动后存档SHA均为
  `80017be952f12bcfdbd2e222efae2c37c934501c98efebd2a09a9dd634bd07e5`。
- 自动采样未完成业务READY，未见fatal signal；实测mesh开启已验证，头显
  双眼真实家具遮挡和原低头透明块仍需佩戴观察，不能把状态日志当作最终验收。

## Phase D43 工作汇报：稳定握把、灵敏牵手与近距离生成（2026-10-25）

- 分支：`fix/stable-grip-handhold-20261025`。本轮真机pre-fix日志5585条，
  4次牵手选择3次被同优先级physical_*拒绝；成功样本IK权重1且目标跟随正常。
- 增加Contact来源（70），显式Player保持90；被动反馈经协调器退出后交接
  手臂/移动资源。牵手保留10/16cm迟滞，接受已握手进入和0.32抓握。
- 模糊松手40→20ms，跟踪丢失100→50ms；明确释放不等待。
- `ItemGripAnchor`读取既有Handle/LoopHandle握点内容，复用于刀柄、锅柄、
  锅铲、杯耳等。PhysicsBody保持唯一运动所有权：稳定持握逐帧对齐，
  手型仅在显示层锁定，2.5cm/10°扫掠保留墙面阻挡，松手恢复动态物理。
- 小物品前方生成1.45→0.55m，水平同向，附近搜索最多0.20m；验证桌面/
  地面支撑覆盖与占用，不再在远处找空位。
- Unity狭小区域复现有0.545m完整路线却被1.25m最小随机距离拒绝；
  改为0.35m后同夹具准入通过，实际房间后验仍待验证。
- Unity专项28项通过，既有回归59/59通过。官方左右手型、释放/暂停、
  薄墙、物品控制、真实物理、切割、遮挡均覆盖；生产刀具实际扫掠切开胡萝卜。
- 苹果未启用通用扫掠切割，沿用既有分块动作。稳定模式降低物理反馈，
  真实手与受约束显示手的舒适度须佩戴验收。
- 未改场景、角色Prefab、Controller和遮挡代码，5项hash一致。
  构建安装进行中，临时Editor专项脚本已在构建前移出主项目。
- 说明：
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_稳定握把交互与灵敏度优化_20261025.md`。
- 本轮最终包已安装 `Builds/MateSoul_StableGrip_20261025.apk`，215346246字节，
  SHA `2777e0f1f63575d1976f670637a5f52aec6a979d9e3a36e3bfc4f31d2765067a`，
  设备/本地一致。BuildReport42.23秒成功，14项Player PDB源码匹配、
  原生库/元数据、1564项CRC、v2签名、zipalign通过。
- 存档安装前后均`6233a5dc...e6ae6fe0`。PID15922收到10mesh、meshDepth=true、
  depth512，未佩戴后onPause，未见业务READY；手感与移动真机后验pending。
  数值：`/Users/bytedance/MateSoul/项目沉淀资料/validation/stable_grip_20261025.json`。

## Phase D42 工作汇报：角色出生等待实测地面就绪（2026-10-25）

- 回捞用户失败PID12609，设备仍为D41 `1b0c0f2f...46e7eac7`。Scene Capture
  22anchors、房间和NavMesh已加载，首块mesh加入即触发collision-ready；
  spawn在另外九块mesh陆续加入前执行并报错。
- 根因是“有任意实测碰撞网格”被当成“角色地面支撑可用”。网格按2ms预算
  分帧安装，首块可能只有墙面，不能释放角色启动。
- GameBootstrap现在在原8秒等待窗口内，等待pending批次完成并持续寻找
  有实测支撑且无碰撞的spawn；成功才绑定角色。移除mesh侧首块TCS准入，
  由现有启动owner承担有界等待；超时和取消保持明确失败。
- 不改变地面支撑/墙体碰撞阈值，不生成虚构平面；保留实测视觉遮挡、HUD
  深度隔离与10cm下移。候选诊断输出noSupport/outside/overlap且限频。
- 引擎回放：首块墙面33/33候选缺支撑，等待地面加入后成功spawn；
  超时/取消和既有地板、真实GPU遮挡合计21/21通过。
- 首次回放被并行手部接口临时不匹配挡住，接口补齐后重跑通过；不回退手部。
  陈旧EditorInstance和UnityLockfile只在确认原PID退出、系统锁未占用后清除。
- 构建与真机后验记录：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/spawn_mesh_readiness_20261025.json`。
- 最终共享源码构建3分06.49秒成功，Player源码校验确认包含最新并行手部
  修改和出生等待；保护资产构建前后不变。
- 已覆盖安装
  `/Users/bytedance/MateSoul/Builds/MateSoul_MeasuredSpawnReady_20261025.apk`，
  215346246字节，设备/本地SHA一致：
  `942af4ccf99b2894a7ec31dedaad2caf60f12f2aa5913cd0802c99d0f4dd2e84`。
  CRC1564项、v2签名、zipalign、ARM64/IL2CPP通过。
- 安装前后和启动后存档均`6233a5dc...e6ae6fe0`。PID14849自动后验有10mesh/
  meshDepth=true/depth512，但头显onPause/onStop并Asleep，未进入出生搜索；
  需要佩戴启动复验实际出生与READY，不将控制回放当作真机完成。

## Phase D44 进行中：流体、双手交接、托举与实测缓存（2026-10-25）

- 用户授权清空已放置物品与重置库存。已force-stop后备份存档至
  `项目沉淀资料/备份/InventoryBeforeReset_20261025.json`，清空6个物品、
  inventory与物品anchor（人格/心情保留），设备下次会按目录配置补满库存。
- 最新旧会话日志4394条：牵手12次全部启动、移动53/494样本，先前
  冲突拒绝问题已改善。新问题可静态确认，不启动新泛用debug会话。
- 正在实现：删除松手计时器，扩大近距13cm与握把12cm；物品允许两只直接手
  同时选择，单一PhysicsGrabInteractable持有预约，原手松开后交接驱动。
- TrackedHandPhysics增加平缓水平掌心承托碰撞，手指推力保留Trigger。
- MeasuredMeshRetention保留Removed观察直到收到覆盖旧范围的有效替换；
  视觉/物理/导航与RealityMeshSource同步沿用缓存，房间改变/销毁时清理。
- FluidStream固定128个流体parcel，无逐滴刚体，20ms重力轨迹+SphereCast、
  开口跨越准入、体积/温度接收与溅水；饮水机/水壶/容器倾倒共用。
- GameSession新增视线前方25个探点，优先高于地面的近高度表面并检查
  斜面支撑；HUD背包显示可用/已放数量。
- 以上生产代码已编辑，尚未编译或实测，不计为通过。接下来专项验证双手
  交接、承托、体积温度、缓存及桌面放置，之后构建安装与补齐文档。

## Phase D45 工作汇报：AI配置、HUD再下移与固定物品放置显隐（2026-10-25）

- HUD按钮及展开页面在上一版基础上再下移10cm，`HudVerticalOffset=-.20`。
- 无玩家密钥时对话框显示“先配置ai才能激活对话功能”与“点此配置ai”紫色按钮；
  设置系统页增加“AI配置”，含地址、模型、API Key、可选令牌、保存、真实连接测试
  和“如何配置AI”内置教程。配置保存立即用于普通对话和自发回复，保留输入草稿。
- 新增`PlayerAIConfiguration`及Android Java `AISecretStore`，Android Keystore/
  AES-GCM保存玩家秘密；Editor仅保留本次会话秘密。网关接受请求级玩家凭据，
  不共享不同玩家的API Key，服务端未配置默认密钥也可启动。
- 固定物品进入预览立即隐藏HUD，成功提交或取消后恢复原页面及手动显隐状态；
  手部张手/握紧/松开确认、双手握拳1秒取消，手柄扳机确认/B取消。
- 网关21/21、Unity UI与现实遮挡19/19通过；真实模型配置检查2058ms、普通回复
  2189ms、自发回复1643ms均HTTP200。Android ARM64/IL2CPP构建、签名、CRC、
  Java DEX与7份Player源码指纹校验通过。
- 已覆盖安装`Builds/MateSoul_AiConfig_LowerHud_Placement_20261025.apk`，
  SHA256 `183cf00823f0f8eb7b479392fd45ea07001ad3abe95a7843ebcf04d5d74840cc`。
  安装前后房间存档均`ea6aad38...b2d13be`，设备APK与本地一致。
- 本机网关由launchd `com.matesoul.ai-gateway`持续运行，已桥接头显8787端口；
  Development配置地址`http://127.0.0.1:8787`，其他玩家使用部署者HTTPS地址。
- 没有修改场景/角色/现实遮挡/URP资源。构建后并行任务继续修改实测缓存，
  本包只对应记录的构建快照，不宣称验证并行D44功能。头显休眠，佩戴视野、
  键盘/手势及密钥保存后重启读取仍待验收；独立app_process探针未通过。
- 交接说明：`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_AI配置与HUD放置交互_20261025.md`。
- 数值记录：`/Users/bytedance/MateSoul/项目沉淀资料/validation/ai_hud_placement_20261025.json`。

## Phase 工作汇报：不可移动物品放置正面朝向玩家（2026-10-25）

- 根因：手部放置的竖直向下射线只包含支撑面方向，水平分量为0，原逻辑退回
  世界+Z，导致电视、饮水机随着玩家站位变化而侧对玩家。
- GameSession.TryResolveSurfacePlacement增加可选viewerPosition，在旋转后的支撑
  覆盖与碰撞检查之前按“命中点→玩家”的水平向量计算朝向。
  ItemPlacementPreviewController传入头显位置，投影与实际放置共用返回Pose。
- 沿用ItemDefinition模型正面补偿：电视正面-Z/yaw180°，饮水机正面+Z/yaw0°。
  WaterDispenserBuilder根据按钮所在前面显式设置yaw，避免重建后丢失配置。
- 两模型×地面/桌面×八个玩家方位共32组实际Physics放置检查通过，
  正面对齐点积最低0.9999999；手部与视线投影旋转一致，保持竖直与6mm支撑间隙。
- Android构建退出0，198.9秒；签名、1566项CRC、ARM64、IL2CPP与运行时PDB校验通过。
  APK已安装并核对设备哈希；安装前后存档一致，场景/角色/Animator保护资产未变。
- APK：`/Users/bytedance/MateSoul/Builds/MateSoul_PlacementFacing_20261025.apk`，
  215392670字节，SHA-256
  `ae4731c2fcbc8e39483eadb286fb66af695b6499eaca23bedb726d0396b237fa`。
- 旧存档保留已保存Pose；朝向修复作用于新放置/重新放置。真人视觉验收待复测，
  不以32组控制验证替代头显实际观察。临时验证入口及meta已清理。
- 数值：`/Users/bytedance/MateSoul/项目沉淀资料/validation/placement_facing_20261025.json`。

## APK内AI迁移进行中（2026-10-25）

- 用户明确授权“开始改造迁移”：参照MateFairy，将提示词、模型协议、工具调度、
  联网工具、回复校验打包进Unity APK，玩家自带密钥，取消游戏网关依赖。
- 已核对MateFairy的DeepSeekLLMProvider、本地WebSearchTool及Keystore设置。
  MateSoul已有ConversationOrchestrator与动作准入，直接复用，不新增执行入口。
- 已将Demo方案10.3中的服务端代理/禁止正式直连规定改为用户授权的新基线。
- 接下来实现供应商直连、请求内工具循环、公开网页边界与输入配置迁移，
  验证取消/无效回复/能力校验和真实模型调用，然后构建并覆盖安装。
- 当前仅完成探索与方案基线更新，尚未交付直连代码或新APK。保留并行物理、
  放置朝向及现实遮挡修改；不能把已有网关版包当成迁移验证。

## Phase D44 验证进展：流体、交接、承托与网格缓存（2026-10-25）

- 最新专项44/44通过：XRI二手加入、唯一预约、无跳位交接、当前样本即时
  松手/跟踪无效释放、自由旋转生产盘子承托、水量温度和斜桌面实际库存提交。
- 生产盘子PhysX2秒后y=1.011m、速度0；水壶0.20L倒入杯中0.12L、
  壶内0.07999991L、杯内100°C；发射0.51L=接收0.32L+洒落0.190000013L。
- 实测Removed不会删除最新网格，暂停/恢复沿用缓存；同UUID和覆盖范围的
  新UUID可替换，碰撞、depth、增量导航与语义数据同步。
- 稳定握把关闭刚体碰撞会使PhysX搜索看不到物品，近距搜索现通过XRI注册表
  补充这类已选物品。杯子凸碰撞封闭内腔，水只允许从有效口部进入。
- 既有批量回归65/66；整豆登记测试首yield后偶尔未运行物理回调，
  单独1/1通过。保持原断言，报告其首帧不稳定，不宣称整批66/66。
- Unity默认包缓存写入被沙盒拒绝引起NUnit丢失，已改用/tmp可写UPM缓存，
  只停止本轮异常进程。并行新AI测试缺少Newtonsoft引用，补齐Editor测试
  assembly预编译依赖；AIWebTools的Formatting歧义用明确别名修正。
- 设备确认items=0、inventory=31行/49件可用，用户授权重置已生效。
- 专用Android包正在构建；构建、签名、源码指纹与安装结果待补充。
- 完整说明：`项目沉淀资料/MateSoul_流体双手交接与网格缓存_20261025.md`。

## APK内AI迁移：代码与真实联网验收完成（2026-10-26，最终构建进行中）

- HttpLLMProvider改为供应商HTTPS直连；提示词、能力目录、工具循环和严格回复校验
  已迁入Unity。普通对话与自发回复共用客户端，继续使用唯一动作协调器。
- PlayerAIConfiguration版本2保留旧玩家密钥，旧网关地址迁为默认DeepSeek地址，
  配置页只需API地址、模型、API Key。移除网关令牌与8787启动/桥接依赖。
- ModelConnectionSettings与对应资源由旧网关资源改名，保留GUID及场景序列化兼容；
  未重建场景/角色，保持并行HUD与流体、遮挡改动。
- 真实模型与专项28/28通过：普通对话1499ms、自发回复约1633ms，
  搜索与网页读取回灌后回复4368ms。网关及子进程已停止。
- 磁盘满导致上一轮执行中断；空间恢复后继续GPU遮挡/对话动作回归和最终构建。
  执行器改为不自动重启，15秒进度、180秒无日志终止、5GiB剩余空间边界。
- APK构建、源码指纹和安装尚未完成；当前ADB没有连接设备。
- 详情：`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_APK内AI系统迁移_20261025.md`。

## HUD 低头后回正修复（2026-10-26）

- 修改 `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs`
  的 `UpdateWorldPose`：高度连续更新，20cm 位移停留区仅约束水平；俯仰硬限幅
  改为有界 tanh，近垂直低头保留水平朝向但继续更新高度。
- 保持 45cm 距离、30° 基础俯角、−20cm 垂直偏移、±8° 俯仰上限、
  可见面板中心补偿和交互冻结，解除点按/触碰后正常恢复跟随。
- 生产方法隔离姿态回放 22/22，通过五轮弯腰站直、深低头抬起、近垂直、
  冻结解除与 30/72/90fps 回正；最大误差 `1.46759419e-6m`。
  这是合成姿态验证，头显实际体验仍待验收。
- 空间恢复后复用共享工程的新生产构建，BuildPipeline 约 122.9 秒、退出 0；
  已生成 `/Users/bytedance/MateSoul/Builds/MateSoul_HudReturn_20261026.apk`。
  1,569 项 CRC、签名、ARM64、5 份 Player/Android PDB 源码指纹及原生库通过。
  构建前后主场景、角色 Prefab、Animator 一致。
- APK 实际 218,746,149 字节，SHA-256
  `1b7252ef8d26210e708aad85d8b1d24c5a2615f8865c29f5ef47ffdf9884d880`。
- 校验后并行角色合并任务启动；本包对应已记录的构建快照。
  数值：`/Users/bytedance/MateSoul/项目沉淀资料/validation/hud_return_20261026.json`。
- 已在共享 Unity 释放后清理本轮 HUD 临时 Editor 链接及 meta；
  当前无 ADB 设备连接，尚未安装。用户删除的临时验证脚本不恢复、不执行。
- 说明：`/Users/bytedance/MateSoul/项目沉淀资料/真机验收/MateSoul_HUD低头回正修复_20261026.md`。

## Phase D44 交付：流体、双手交接、实测缓存与出生高度（2026-10-26）

- 七项需求均已实现；上一轮设备已清空6个已放物品并按目录补满库存，
  验证items=0、inventory=31行/49件可用，原存档有独立备份。
- 真机上一包暴露稳定地板高于实测地面，11个出生候选因脚底重叠全被拒绝。
  出生高度改为两层支撑较高值，保留实测地面准入、障碍和现实遮挡。
- 最新专项47/47、房间导航/GPU遮挡21/21通过。此前批量65/66，整豆首帧
  触发测试单独1/1通过，仍报告该时序不稳定，不写成整批通过。
- 复用全部最新修正后启动的共享Unity构建；耗时2分02.899秒。
  19份Player源码校验和、APK原生库、1569项CRC、签名、zipalign、
  ARM64/IL2CPP通过，构建期间场景/角色/Animator保护资产不变。
- 最终APK：`/Users/bytedance/MateSoul/Builds/MateSoul_FluidHandoffMeshCache_20261026.apk`，
  218746149字节，SHA256：
  `1b7252ef8d26210e708aad85d8b1d24c5a2615f8865c29f5ef47ffdf9884d880`。
- 当前ADB无设备，最新包尚未安装，报告installed=false；原包已安装的
  事实不能替代出生修正版真机验收。恢复连接后应覆盖安装、READY及手感复测。
- 临时FluidHandoffAcceptance.cs和meta已从Editor清理；证据脚本/日志留在/tmp。
- 说明：`项目沉淀资料/MateSoul_流体双手交接与网格缓存_20261025.md`；
  数值：`项目沉淀资料/validation/fluid_handoff_mesh_cache_20261026.json`。

## APK内AI迁移交付（2026-10-26，等待设备安装）

- AI编排、供应商直连、联网工具、单次格式修正与严格能力准入均已进入APK。
  玩家填写API地址、模型和自己的API Key，不需要MateSoul网关或8787桥接。
- 真实供应商专项28/28通过；GPU遮挡与游戏合同共45项完成验证。
  首批43/45、两项过期fixture更新后2/2通过，保留完整原始结果。
- 当前保存场景Android构建成功，耗时2分02.899秒；13份Player源码指纹、
  1569项CRC、签名、zipalign、ARM64/IL2CPP及PICO能力配置通过。
  场景/角色预制体/Animator哈希未变；APK不包含验证用开发者密钥。
- APK：`/Users/bytedance/MateSoul/Builds/MateSoul_APKLocalAI_20261026.apk`，
  218746149字节，SHA256
  `1b7252ef8d26210e708aad85d8b1d24c5a2615f8865c29f5ef47ffdf9884d880`。
- ADB当前没有连接头显，尚未覆盖安装。安装后存档、Keystore重启读取和实际对话
  需在设备连接后验证；不把Editor/GPU结果写成真机验收。
- 报告：`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_APK内AI系统迁移_20261025.md`；
  数值：`/Users/bytedance/MateSoul/项目沉淀资料/validation/apk_local_ai_20261026.json`。

## 冷月 Direct55 主工程合入（2026-10-26）

- 已更新主工程 `MateSoul/Assets/MateSoul/Content/Lengyue_Runtime.prefab`：
  Direct55 头发/发束线、Direct54 面部/身体；保存的 MateSoul 场景继承该预制体。
- Direct54 身体顶点增量叠加在 LimbRefinement01 上，补臂同步径向瘦身；
  保留肘部、脚踝、SoftFit01 三档服装与坐姿修正、61 个表情及动画 GUID/事件。
  未覆盖独立工程、共享 Unity6Study/Fix 来源或其他任务的游戏逻辑。
- 604 项集成检查、32 项实际 Unity 运行循环检查、正式场景绑定检查通过。
  换装、表情/睫毛、发束弹簧、转身、坐卧、双手接触及保龄球释放正常；
  184 个已有动画/服装依赖哈希保持一致。
- 保存场景的 Android Development 构建成功，BuildPipeline 耗时 76.711 秒。
  APK 内 1183 个 sharedassets 分块直接解析，确认五个更新网格的真实 Renderer
  绑定及 61 个面部表情；1573 项 CRC、签名、ARM64/IL2CPP 与资源指纹通过。
- 交付包：`/Users/bytedance/MateSoul/Builds/MateSoul_Direct55_20261026.apk`，
  218746517 字节，SHA256
  `b4ba021b5380813bdd0fc41cd4ad49bf4416ab5ee221d9086c8ef6a0984b0611`。
- 临时 Editor 脚本、导入副本和单独截图已移除，只保留一张同机位对比图、
  数值报告与合入前备份。证据目录：
  `/Users/bytedance/MateSoul/ArtSource/LocalStudyOnly/Lengyue_ProductionMerge_20261026`。
- 当前 ADB 无设备，尚未安装该包；不能将 Editor 检查写成 PICO 佩戴或性能验收。
  后续需安装本次 Direct55 包，观察发束在头显抗锯齿下的表现并验收角色交互。

## 全部最新改动整合交付（2026-10-26）

- 整合角色Direct55/Direct54与61个表情、APK内AI直连/联网工具、HUD回正、
  实体水流、即时释放/双手交接、掌心承托、拾取范围、实测缓存/遮挡、
  近玩家平台生成及出生高度修正，保持当前主场景与生产资源。
- 新启动的Unity构建因许可证机器绑定不匹配/无headless entitlement失败。
  421份输入在该尝试中未变；没有伪造、修改许可证或更改用户机器配置。
- 随后逐一核对9个生产程序集的273份源码，全部匹配当前磁盘代码；
  Direct55资源指纹也与最新成功构建一致。因此复用当前有效的Direct55构建，
  生成统一交付文件，不将该复用写成新编译成功。
- 从交付APK自身重建sharedassets并解析实际Renderer，确认五个最新网格与
  61个表情；1573项CRC、v2签名、zipalign、ARM64/IL2CPP原生库及PICO能力通过。
- 整合包：`Builds/MateSoul_AllLatest_20261026.apk`，218746517字节，
  SHA256 `b4ba021b5380813bdd0fc41cd4ad49bf4416ab5ee221d9086c8ef6a0984b0611`。
- USB ADB及mDNS多次未发现目标头显，installed=false，未执行设备安装或
  再次修改存档。头显恢复连接后直接覆盖安装并核对包哈希/存档及启动状态。
- 新的重新编译需要在Unity Hub恢复有效许可证；当前交付包已匹配全部生产改动。
- 数值：`项目沉淀资料/validation/all_latest_20261026.json`；
  本轮中间脚本、包解析目录与日志全部位于`/tmp/matesoul-integrated-20261026`。

## 最新整合包头显安装与启动检查（2026-10-27）

- 目标头显PB3A10MGL9220170B/B3110重新连接；当前273份生产源码和Direct55
  资源仍与整合包一致，直接覆盖安装既有最新包，没有重建或恢复临时脚本。
- 安装用时10.44秒，设备base.apk与本地SHA256一致：
  `b4ba021b5380813bdd0fc41cd4ad49bf4416ab5ee221d9086c8ef6a0984b0611`。
  安装前后存档完全一致，SHA256：
  `1d70e575b16e72be4ba512e2484f4a0ef38d868e296ec10dcc05f1257456eb0c`。
- 已启动应用PID11936，头显Awake，房间Scene Capture与NavMesh已加载，
  收到8块实时网格，实测遮挡开启，环境深度512×512。
- 启动仍未达READY：13个候选全部noSupport=13、outside=0、overlap=0，
  8秒后报“房间内没有可用的角色出生点”。当前是实测地面支撑缺失，
  不能归为此前稳定地板重叠，也不能写成角色/交互真机验收通过。
- 安装任务完成，启动问题及真人交互仍待处理。未修改房间准入或源码，
  未清空设备存档，也未停止其他会话进程。
- 安装记录：`项目沉淀资料/validation/all_latest_install_20261027.json`；
  统一包报告已同步installed=true与本轮启动状态。原始日志：
  `/tmp/matesoul-install-20261027/startup.log`。

## HUD 面板向上展开与按钮轻微下移（2026-10-27）

- 用户要求面板展开时保持底部按钮易操作。原定位切换到内容面板中心，
  以 0.00066m/UI单位、导航中心80和面板中心365计算，默认俯角下按钮下移约16.3cm。
- 修改 `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs`：
  页面展开继续以底部导航为定位基准，面板在按钮上方显示；
  `HudExpandedNavigationDrop=.02f` 使按钮仅平滑下移2cm，收起后恢复基准。
- 页面与导航边缘沿Canvas平面保持2.64cm间隔；继续沿用高度回正、俯仰跟随、
  指数插值与点按冻结。所有内容页面共用同一布局规则。
- C#编译通过。首轮Unity在Burst后台编译线程发生native crash，退出-11；
  同步重试前共享工程被另一启动诊断构建占用，随后复用其成功的新构建，
  BuildPipeline耗时104.376秒，确认新Player/Android PDB包含本轮HUD源码。
- APK：`/Users/bytedance/MateSoul/Builds/MateSoul_HudPanelUpward_20261027.apk`，
  218,746,517字节，SHA-256
  `19b8a6c90ecbeed71803d1df9adaa3c4bd5e850e430f5632ba3dd4a5f464cc63`。
  1,573项CRC、签名、zipalign、ARM64与同批原生库校验通过；
  本轮场景/角色/Animator保护资产一致。
- 新包已由并行启动诊断任务安装到PB3A10MGL9220170B，本轮复用该安装并核对
  设备包哈希；存档核验期间均为`1d70e575...6eb0c`，没有再次安装或重启应用。
  ADB服务查询阻塞后恢复服务，并恢复已有7781诊断映射。
- 实际佩戴布局尚未人工验收；本轮几何核算及包版本核验不代表启动READY或
  角色出生问题已经修复。启动诊断仍由对应任务处理。
- 中间备份、日志与几何计算位于 `/tmp/matesoul-hud-panel-upward-20261027`；
  最终数值记录路径为
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/hud_panel_upward_20261027.json`。

## 角色出生点实测支撑不匹配修复（2026-10-27）

- 原 Scene Capture 地面平均0.640m，13个候选全部缺实测支撑；未改业务的
  正向控制取得实测1.163m地面并正常出生，射线和碰撞正常，确认两路地面不兼容。
- `GameBootstrap` 将现有 Spatial Mesh 回退扩展到捕获返回后8秒内没有出生点；
  统一重建、等待与一次重试，最终缺支撑仍失败。`RealityOcclusionMeshRenderer`
  切换房间时重新入队SDK缓存。没有放宽支撑/碰撞准入或固定地面高度。
- 22项Unity引擎回归通过，覆盖高度差重放、有效capture保留、无网格/取消、
  GPU现实遮挡与HUD。5份关键源码在Player和Android裁剪PDB均匹配；
  UnityLinker可能保留SHA256摘要但将算法标记置空，校验需按摘要长度识别。
- 本轮3处HTTP探针移除后重新构建、安装最终包；1,184个sharedassets与
  Direct55整合包逐项相同，14个Direct55文件未变，61个表情保留。
- 最终APK：`Builds/MateSoul_SpawnFix_20261027.apk`，218,746,517字节，
  SHA256=`15da354470db9acb0d20a4d7c33d5ce9dc9fc1219a71352736877c8393de1720`。
  CRC/签名/zipalign/ARM64原生库通过，设备PB3A10MGL9220170B包哈希相同。
- 覆盖安装前后外部应用存档一致，SHA256=8b68381b...41b1dd。
  最终PID21117，10块网格，3个候选无拒绝，avatar_bound与READY均通过，
  navMs=19。真机自然运行走Scene查询超时回退；新不匹配分支由Unity重放覆盖。
- 结论：启动失败已修复并在最终包验证。两路SDK偏移的内部原因未确定；
  全部真人交互不属于本轮已验收范围。证据见
  `项目沉淀资料/MateSoul_角色出生点修复验证_20261027.md` 与
  `项目沉淀资料/validation/spawn_measured_support_fix_20261027.json`。

## 虚拟手半透明外观（2026-10-27）

- 左右虚拟手的共用 `TrackedHand.shader` 默认不透明度改为0.5，
  保留原RGB、光照计算、顶点变换和双眼渲染宏。
- 启用透明队列与Alpha混合，关闭透明手的深度写入，移除原不透明深度
  Pass；深度测试及背面剔除保持原设置。
- 修改前后322份运行时/SDK资源的哈希比对中，仅手部着色器发生变化。
  手部模型、追踪、碰撞、抓取、触碰及接触姿态逻辑未修改。
- 差异格式检查通过。本轮未执行Unity编译、真机外观验收或APK打包；
  已安装的上一版APK不包含本次修改。

## UI 面板内容样式升级（2026-10-27，进行中）

- 用户要求保留底部导航按钮样式与现有粉紫色调，升级面板内的按钮分布、
  按钮层次、内容排版和交互动画。
- 已完成现有页面与输入逻辑核对：内容面板高度330单位，互动反馈位于
  y=326且高度28，存在超出面板的问题；大部分页面使用连续长按钮，
  需要重新组织标题、分组、内容卡片和底部操作区。
- 接下来仅修改GameHud面板构建与面板按钮表现，保留导航方法、业务回调、
  近手/眼手输入及2cm展开下移合同；按钮点击矩形保持稳定。
- 当前尚未修改面板实现、编译或验收。后续需真实Unity页面渲染检查、
  页面切换/点击验证及包含最新角色、出生修复和手部资源的APK交付。

## UI 面板升级实现与渲染检查（2026-10-27）

- 已修改 `GameHud.cs`，新增 `HudPanelButton.cs`：面板高度448，
  互动与设置采用双列布局，物品采用分类标签和带数量区的网格，
  信息、AI配置与教程分出标题、内容和操作区。
- 使用当前粉紫色系，增加深浅按钮层次、抗锯齿圆角、文字悬停/按下反馈，
  面板以0.26秒展开、0.18秒关闭，页面交叉淡入0.18秒。
  按钮根点击矩形保持固定。
- 底部导航、图标、圆角、原按钮颜色配置、对话栏及HUD世界跟随方法与
  修改前逐项一致；仍向上展开并仅下移底栏2cm。
- 隔离空场景和内存存档中的七个页面及系统设置渲染检查已通过，
  无文字截断或越界；修正了AI教程两行说明的高度。
  完整点击验收尚未通过，最新测试代码已修正PlayMode切换导致的闭包丢失，
  正在等待共享Unity任务释放；不得把旧失败XML作为通过证据。
- 编译阻断修复：新 `AngryEmotionBuilder.cs` 及meta移入
  `Assets/Characters/SapphiArtCustom/Lengyue/Editor`，与其调用的导入工具
  同属默认Editor程序集，代码与GUID保持不变。
- 临时脚本、备份与检查结果仅在 `/tmp/matesoul-ui-panel-style-20261027`；
  本轮新APK尚未构建/安装。后续需完成点击验收、保存场景构建、包与源码
  校验、覆盖安装并核对真实存档。并行动画/手部改动需按构建开始时快照核验。

## UI 面板升级验收与 APK 交付（2026-10-27）

- 面板内容升级已完成。底部五个导航按钮、粉紫色系与向上展开/底栏下移2cm
  合同保留；内部采用分组卡片、双列操作、物品分类与数量区，加入平滑切页和
  悬停/按下反馈，点击矩形不随反馈缩放。
- 最新Unity整体验收通过：9个页面状态无越界或文字截断，6项检查覆盖导航、
  系统业务开关、实际GraphicRaycaster点击、快速切页与分类、隐藏恢复，以及
  AI配置/教程/保存后返回对话并保留草稿。旧失败XML已由本次通过结果替代。
- 从保存的主场景成功构建Android ARM64/IL2CPP Development包：
  `Builds/MateSoul_UIPanels_20261027.apk`，实际文件218,783,588字节。
  SHA-256为`0b990d84cbaf00ed742d32f38546a26a2cdcf20316a0fa031b5936f37d6c5d5e`。
- 编辑器、Player与ManagedStripped程序集的UI源码校验一致；4份运行时源码的
  双程序集校验、ZIP CRC、原生库字节、签名与zipalign检查均通过，测试程序集
  未进入安装包。8个受保护HUD方法与修改前一致。
- 主场景、角色Prefab、Animator、手部Shader等保护文件与构建开始快照一致；
  GameBootstrap的并行更新发生在构建前并已纳入快照。包内五个Direct54/55
  角色网格与SpawnFix包字节一致，生产面部仍为61个形态。
- 编译期间清理了失效的UGUI生成缓存；同时修正RuntimeRoomNavigation中
  方法声明错位与重复括号，保留已有地面支撑逻辑。Unity进程构建退出码为0；
  外层沙盒拒绝了用户统计目录与SDK缓存写入，后续仅校验已生成的工程内APK。
- 头显当前USB/ADB均未连接，未执行覆盖安装，未读取或修改头显存档。
  READY、真实VR视觉与手势体验仍待真机验收。临时测试入口、meta及本轮截图
  已清理；数值证据保留于`项目沉淀资料/validation/ui_panel_style_20261027.json`。

## 投掷、地面支撑与手部推力修复（2026-10-27）

- `PicoTrackedHand` 与 `PicoDirectHandInteractor` 修复尾随手指导致的松手
  延迟，统一使用随采样间隔变化的运动连续性边界。30FPS、4m/s甩手原本
  因13.33cm单帧位移被误判，现在正确释放；追踪瞬移仍拒绝抛掷。
- `PhysicsGrabInteractable` 使用松手帧的手部线/角速度；
  `PhysicsBody` 在碰撞边界投影释放速度并立即配置高速CCD。
  3.5m/s手速原本仅释放0.1m/s，现在释放3.5m/s，姿态无跳变。
- `RuntimeRoomNavigation` 为SpatialMesh房间创建有限、无Renderer的
  实测高度基础支撑，范围随实时测量扩大，房间重建时清除。复现发现可见
  地面小块外没有碰撞支撑；同房间对照跌至-10.9809m，修复后普通/凸网格
  四种抛落和新增扫描区域全部正常落地，NavMesh与视觉遮挡未扩展。
- 手推按接触相对速度与有限手部有效质量计算冲量和受限扭矩。
  150g物品轻推得到0.0455m/s、重推2.0755m/s，4kg重推0.23856m/s；
  静止接触不施力。保留手速法线符号，修复甩离残留重叠造成1→0.48038m/s
  反向刹车；最终保持1m/s。双手交接无掉落、跳位。
- 无诊断探针生产代码通过30项Unity回归，专项保存31组数值结果。
  当前保存场景重新构建成功，ZIP CRC/签名/zipalign/ARM64库及7份源码
  在Player与ManagedStripped中的一致性检查通过。
- APK为`Builds/MateSoul_HandThrowPhysics_20261027.apk`，218,783,588字节，
  SHA256=`ca248b58a522f10259177cb8d4176866a459bd1691bec3aea3d783d13f302481`。
  Direct55的14个文件未变，1,184项sharedassets与最新UI验收包一致。
- 头显未连接，未安装或修改存档；真实跟踪延迟、轻重推手感和抛落需连接后
  验收。报告为`项目沉淀资料/MateSoul_投掷与手部物理修复_20261027.md`，
  数值为`项目沉淀资料/validation/hand_throw_physics_20261027.json`。

## 手持后落穿根因复核与补充修复（2026-10-27）

- 用户指出未拿取物品很少落穿，手拿过后概率显著升高；原扫描缺口归因不完整。
  36组对照确认：生产地面MeshCollider存在时，20/6/3mm物品在未抓取及
  普通跟随中落地，稳定握点手持却在松手前到y=-0.2m；碰撞恢复正常。
- 根因为持有时关闭自动碰撞后，25mm离散占位采样加12mm穿透容差可跳过
  薄物品与网格的接触。松手只是让已经处于地面下方的物品继续下落。
- PhysicsBody.ApplyStableFollow增加连续平移扫掠及2mm间隙，手持占位容差
  收紧到1mm，保留旋转分段检查和可切割对象例外。72组对照中的48组有支撑
  场景全部落地，24组无支撑对照仍落穿；沿面滑动与抬离通过。
- 最终无探针8项物理/抓取回归通过。扩大20项回归18过/2失败，两个角色
  姿态/握持误差断言在旧逻辑对照中同样失败，未改资源或放宽断言。
- 新APK为`Builds/MateSoul_HeldReleaseBoundary_20261027.apk`，218,783,588字节，
  SHA256=`407720eb5e846c99fd46eaa007ff192509f602bb1e2aa90a0f900d951f7f783c`。
  CRC/签名/zipalign/ARM64库与7份源码的双程序集证明通过，角色/UI资源未变。
- 报告与数值分别为`项目沉淀资料/MateSoul_手持后落穿根因复核_20261027.md`
  和`项目沉淀资料/validation/held_release_boundary_20261027.json`。
  头显未连接，尚未安装；本轮探针、服务、临时入口和验证媒体已清理。

## 高处释放落穿症状复核与归因更正（2026-10-27）

- 用户明确补充：正常举起、高举或轻轻向上抛后才松手，物品始终在地面上方。
  上一节主动将手持目标移至地下的回放只证明稳定握点越界漏洞，不能确认这一
  实际症状的根因；已更正其独立报告的开头与适用范围。
- 用生产物品初始化和实际 XRI 选择/退出测试马杯、刀、勺子、盘子、保龄球；
  360 组高度/速度/朝向/地面对照加 20 组手掌支撑对照，全部原物品落地。
  380 次释放均恢复碰撞、动态刚体和重力。受控结果不能代表真机发生概率。
- 实际 RealityOcclusionMeshRenderer 地面每步替换的 5 组回放未落穿；
  完整 GameSession 的 6 组杯/盘高处释放中原物品未穿地，4 个按碎裂规则退役。
  观察到 1.8m 马杯的一个重度碎片落穿，尚不足以解释用户原物品故障。
- 主矩阵完成后有脚本清理错误，逐组证据保留；手掌专项最终 NUnit 通过，
  Unity 退出码 0，工具层另拒绝非必要 metrics 数据库写入。不能笼统称所有
  脚本/碎片断言通过。源码探针外的生产逻辑与本轮前完全一致。
- 当前真机根因仍为 INCONCLUSIVE，未新增修复 APK。头显未连接，需要具体
  物品和真实释放/接触日志。诊断会话 OPEN，项目内临时测试入口已移除。
- 报告：`项目沉淀资料/MateSoul_高处释放落穿复核_20261027.md`；
  数值：`项目沉淀资料/validation/elevated_hand_release_20261027.json`。

## AI 生气动画与“哼”音效同步（2026-10-27）

- `ResponseEmotionAudio` 将 AI 对话/主动回复的 angry 情绪注册为共享动作，
  占用移动、身体、注视与面部；忙碌时两者共同等待，可接替待机注意力。
  `GameBootstrap` 绑定当前 GameSession，公开 Play 在绑定后也走该动作路径。
- 导入用户的 `Mad_01_InPlace.fbx` 为 `Lengyue_Angry01.anim`：
  30fps、1.6333333秒、55骨骼、550身体曲线及61生产面部曲线。
  补齐Unity省略的恒定闭眼权重，旧嘴形按原时序适配Direct54/55；
  鼓腮通过现有Small/U嘴形近似，未更换或扩展生产面部网格。
- 新增默认权重0、状态速度0的Emotion层；动作Start同时开启动画和2.5秒
  `Angry_Long.wav`，音频采样位置驱动动画，末姿保持到音效完成。
  完成/暂停/抢占/禁用同步停止，取消后不重播；其余情绪维持原音效池。
- Unity导入、编译与4项回归通过。最终1,929次采样最大归一化进度差
  `5.96046448e-8`；抢占后闭眼权重恢复0。Hips水平首尾差0.003932m。
  本功能未修改主Prefab、场景与左右转身动画；保留并行行走任务的0.75m/s
  调整，并用最新Prefab、AvatarMotor及行走片段重跑同步专项。
  控制器45对象与测试资产等价。
- 验证报告：`项目沉淀资料/MateSoul_生气动画与音效同步_20261027.md`；
  数值：`项目沉淀资料/validation/angry_emotion_sync_20261027.json`。
  未重建APK或安装；真实头显输出延迟待验收。导入/测试完成后外层沙盒
  报Unity用户目录metrics/config写入受限，产物及测试结果已独立核对。
  本轮临时验证工程、脚本与测试入口清理后仅保留资产、实现和数值报告。

## 2026-10-27：Walk_01 行走替换与步幅同步

- 将 `Walk_01.fbx` 重定向到当前生产骨架，替换 `Lengyue_Walk_BodyOnly.anim`，保留片段 GUID 和动画事件。当前角色骨架的 305 个 Transform 保持原值；左右转身片段未重建。
- 新增 `AvatarWalk01Builder`：转换骨轴与单位，按腿长比例 0.914593 适配，用双骨 IK 保留落脚轨迹，骨盆最大降低 25.021 mm，并按步行距离烘焙动画时序。
- 循环 1.066667 s、每循环 0.663660 m、参考步速 0.622181 m/s。默认移动速度为 0.75 m/s；`AvatarMotor` 每帧用实际 Rigidbody 水平速度驱动 Locomotion 播放倍率，完整行走混合阈值为 0.12 m/s。
- 最小 Unity 工程回放验证通过：5 个恒速工况与启停/挡停恢复共 60 个支撑阶段，最大水平漂移 1.055 mm；三种待机、转身时钟、GUID、根位移及绑定检查均通过。范围为骨架/Animator 回放，真实房间碰撞和头显视觉待验收。
- 早期完整副本的 UPM 和权限处理消耗过多时间；改为仅包含骨架与动画的最小工程后完成验收。后续同类任务优先缩小验证范围，验证副本使用独立包缓存。
- 报告：`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_新版行走动画与步幅标定_20261027.md`；数值：`/Users/bytedance/MateSoul/项目沉淀资料/validation/walk01_stride_20261027.json`。
- 最终主工程全量脚本编译、资产导入通过，Unity 退出码 0；片段哈希与验收产物一致，控制器的倍率参数和阈值完成复核。临时主工程测试入口已移除；未打 APK 或安装头显。

## 2026-10-27：全部已完成改动整合、Release 构建与头显安装

- 从保存的主场景重新构建 Release ARM64/IL2CPP，整合新版行走/步幅同步、
  生气动画音效、AI场景摘要、UI内容升级、半透明手、投掷/手推、手持边界、
  房间出生回退及既有物品改动。构建前后399项输入一致，主资产未重建。
- APK：`Builds/MateSoul_AllLatest_20261027-214501.apk`，164,918,629字节，
  SHA256=`ec688c81ede38991ddde33fe259d998065289806da78aef579f36755d115af85`。
  Unity退出码0；CRC/签名/对齐、36项双程序集源码、原生库与角色网格核验通过。
- 覆盖安装到PB3A10MGL9220170B，设备包哈希一致，真实存档安装前后一致，
  Activity启动成功，PID15651。唤醒后Unity/UI/输入初始化，但头显自动休眠，
  未取得房间READY；真人视觉、对话与物理交互仍待佩戴验收。
- 新增`run-headset.py`和Run配置，复用现有保存场景Release入口，包含有限
  构建时间、进度心跳、源码快照、存档与安装包核验。
- 外层沙盒拒绝Unity统计/SDK缓存写入，实际构建安装结果独立核对成功。
  验包后并行窗口只向PhysicsBody添加Editor诊断，未混入新生产修复。
- 报告：`项目沉淀资料/MateSoul_最新整合包安装_20261027.md`；
  数值：`项目沉淀资料/validation/all_latest_20261027.json`。

## 2026-10-27：持刀大幅挥舞卡顿性能分析

- 本轮只分析，没有修改生产业务逻辑或安装 APK。实际 XRI 握持生产刀进入
  稳定跟随，确认三个 BoxCollider；每帧按位移/25mm、旋转/10°分段检查。
- 20 组场景/目标变化组合各测 120 次。12 个邻近网格工况下，单帧目标变化
  2cm/5°均耗 0.052ms；极端 1.2m/90°执行48段、144次范围查询和1152次
  穿透判断，均耗2.241ms。数字来自Mac CPU，1.2m是单帧压力工况，不能
  当成正常挥舞总幅度或头显实测。单个网格面数增长没有一致的时间增长。
- 空中切割搜索约0.0015ms；胡萝卜完整切割提交3.70–9.21ms。失败切块准备
  同样切网格、实例化、烘焙和回滚；20次空间阻塞压力尝试中位7.609ms、
  P95 14.201ms、最大38.523ms。成功后才更新冷却，失败允许后续再尝试。
- 分配字节计数器校准失败：分配32KB仍返0，因此GC归因保留INCONCLUSIVE。
  三项专项测试均通过，探针/临时入口/服务清理，物理业务源码与前基线一致。
- 设备包哈希匹配AllLatest Release；已有日志只有启动、短暂恢复和暂停，
  没有实际挥刀或房间READY。实际真机卡顿根因仍INCONCLUSIVE，不能将
  已确认的源码开销放大机制直接宣称为该次故障的唯一根因。
- 报告：`项目沉淀资料/MateSoul_持刀挥舞卡顿分析_20261027.md`；
  数值：`项目沉淀资料/validation/knife_swing_profile_20261027.json`。

## 2026-10-27：持刀挥舞性能优化与回归

- 用户在分析后授权开始优化。新增 `PhysicsBody.StableFollow.cs`，在同一物理
  所有者内快照每次更新的形状、障碍姿态和忽略碰撞对；对原有各段球形查询
  范围的AABB求并集，一次查询候选，各段先按保守包围盒剔除，再判断穿透。
- 移动障碍回放发现初版缓存对Rigidbody插值姿态漏检。已修正候选查询范围，
  障碍剔除范围同时包含物理包围盒和可见插值姿态包围盒，再完整复测。
- 保留平移扫掠、25mm / 10° 采样、60段上限及1mm容差。复用数组只在首次
  稳定握持时创建，碰撞体刷新时更新，松手清除引用；256项候选饱和时不移动。
- 切割成功和失败均消费0.14s冷却，每次有效刀划最多尝试一个物品；空中挥刀
  不消费冷却。切割深度和明显不穿过碰撞网格的切面在预约、切网格之前拒绝；
  保留切块空间检查、提交保存和回滚。
- 同场景各180次计时，12个邻近网格下，单次0.30m/45°从1.619ms降至
  0.060ms（降低96.3%），极端单帧1.20m/90°从4.699ms降至0.197ms。
  这是Mac无渲染CPU结果，不能当作头显帧率增益。该场景和先前分析不同，
  只能使用本轮同方法的优化前后数字比较。
- 修正后的同一版源码29项独立检查全部通过：480组位姿与旧求解器一致，
  平移插值障碍、带偏心子碰撞体的旋转插值障碍各40帧与公共占位检查一致；
  薄网格地面、纯旋转、偏心复合/非均匀缩放、忽略碰撞对、刷新、饱和、
  失败冷却和帧末清理均通过。
  生产刀通过真实XRI选择流程验证稳定握持、新鲜速度脱手和取消释放；
  输入为受控模拟手包，真人头显挥刀尚待验收。
- 原有物理、Grip、切割与切块保存回归通过，测试前后四个生产源码及受保护
  场景/角色/控制器哈希一致。四个临时测试入口及meta已移除；
  临时脚本、日志和产物在/tmp。
- Release APK：`Builds/MateSoul_KnifeMotion_20261027.apk`，165,580,237字节。
  构建219.9秒、Unity退出码0；654项受跟踪输入前后一致，ZIP CRC、17个
  ARM64原生库、v2签名、4KB对齐及插值修正的Android生成代码核验通过。
  未安装头显；SHA-256完整值见报告和数值归档。
- 报告：`项目沉淀资料/MateSoul_持刀挥舞性能优化_20261027.md`；
  数值：`项目沉淀资料/validation/knife_motion_optimization_20261027.json`。

## 2026-10-27：Floor / Low / Table 拾取动画接入

- 将用户提供的三个 FBX 非破坏接入当前 Unity 生产角色，新增
  `PickupAnimationBuilder.cs`、`PickupAnimationSet.cs`、三段 `.anim` 和内容绑定；
  修改 `AvatarItemRig`、`ItemTransferStep` 和内容重建器。
- 根据标准参考骨架映射关节旋转；保留生产骨长、网格、服装和其他动作 GUID。
  三段抓取相位统一为 0.5，支持相邻高度混合，避免高度档位之间无动作可用。
- 用标定抓握点反算站位；接触校正最大 15cm、吸附门槛 2.5cm；吸附后继续
  播放原动画起身段，再过渡到 Carry。Table 调整前伸；脚底补偿混合下沉。
- 薄片物品保留初始吸附朝向，修复盘子贴地取消后盘缘穿地的问题。双手大件和
  专用切割准备保留原有路径。Pickup_High 待用户提供。
- 最终 259 项动作数值检查、2 项生产流程回放通过。10 个高度的最大接触残差
  0.071mm，脚底高度误差小于 1mm；临时测试入口移除。
- 已有握刀严格阈值仍未通过。旧 Reach 与新 Pickup 实测手指目标误差分别为
  0.1106564m / 0.1106563m，指节旋转一致；本次只证明未劣化，未改原测试。
- 说明及数值：`项目沉淀资料/MateSoul_三档拾取动画接入_20261027.md`、
  `项目沉淀资料/validation/pickup_animations_20261027.json`。
- Release APK：`Builds/MateSoul_PickupAnimations_20261027.apk`，165,581,061 字节，
  SHA-256 `c568aa858a2e0c378a116d744a2af4075bb8cd8a9aa3618a65b216b347104e39`。
  154 项构建输入快照一致，ZIP CRC、ARM64 IL2CPP 库及 APK v2 签名通过。
  构建耗时 3 分 9 秒；未安装头显，真人交互仍待验收。

## 2026-10-27：互动页物品拾取测试入口

- “互动”新增“物品拾取”按钮，展开独立二级面板，显示房间内已放置的可移动
  单手物品名称和可拾取数量。每页 6 种，支持翻页、返回互动和去放置物品。
- 点击名称选择最近且空闲的实际实例；拾取后保持持物，“放下 / 取消”负责
  结束。关面板不结束动作，暂停和会话退出清理物品和角色控制权。
- 新增 `PickupItemAction.cs`；复用 `ItemActionRun` 预约/心跳/清理和
  `ItemTransferStep` 全部动画流程，只增加目标实例约束与持物结束边界。
  固定、大件双手、过高、占用和不存在物品有明确过滤或拒绝。
- 无效新请求不再覆盖仍在运行的物品动作取消句柄。UI 不生成物品、不消耗库存、
  不触发切水果，动作不对 AI 公开。
- 7 项相关回归最终通过（主回放 6 项加专项分页 1 项）：真实点击和最近目标、
  地面/低台/桌面拾取、持物/放下/暂停、边界拒绝、翻页/数量更新，以及原有
  切水果和对话/物品页面。三档苹果持物误差最大 0.000001m 以内。
- 临时测试入口已移除。使用 `doc-maintainer` 将实现与验收交接追加到本项目
  文档。报告：`项目沉淀资料/MateSoul_物品拾取测试面板_20261027.md`；
  数值：`项目沉淀资料/validation/pickup_button_20261027.json`。
- Release APK：`Builds/MateSoul_PickupButton_20261027.apk`，165,610,361 字节，
  SHA-256 `9146d3f4fb7519c0cf445230ea27fcbf0d96033edb85ab683b40226e012685ef`。
  最终构建 145.49 秒，272 项受跟踪输入一致；CRC、18 项 ARM64 库、新页面/
  动作的 IL2CPP 元数据与 v2 签名核验通过。初次构建发现并行源码变化后重新打包。
  未进行头显安装或真人验收。

## 2026-10-27：不可移动物品放置容差与躺床方向

- `GameSession` 对标有 `RoomSurfaceCollider` 的扫描网格放宽不可移动物品的
  支撑判定：高度差允许 8cm，支撑点可跨网格分块；中心必须命中，八个周边点
  至少六个有效。普通虚拟表面继续使用原来的支撑面积和高度合同。
- 实际占位检查只容忍扫描网格在物品底部 8cm 内的起伏；同一网格中的墙体、
  高凸起和其他实体仍阻止放置。预览、提交和固定物品存档恢复共用此判定，
  避免放置后再启动被相同扫描噪声退回库存。
- `BedGeometry` 优先用床面端部、低矮且横向连通的枕头形状推断头向；
  无枕头时参考明显床头结构，仍无法区分时沿床长轴选择固定方向。
  `RealBedScanner` 连续扫描在头向不明时沿用已有方向，不把方向识别作为准入门禁。
- 移除近似方形床的长宽差门槛；扫描长短轴交换时同步交换尺寸和四边距离，
  沿用已有床向。相邻枕头被扫描成一整片时也可以作为头向提示。
- 实际回放确认旧躺卧胶囊与枕头相撞；`AvatarMotor` 将躺卧躯干代理缩短至
  1.4m、中心 y=0.725m，保持脚端范围，避免用颈部代理阻止枕头接触。
  起身恢复原站立胶囊，转移路径检查继续生效。
- 继续检查床面完整性、人体净空、床边可达性及上床转移路径；所有 UI / AI
  请求仍由原 `LieOnBedAction` 和动作协调器执行。HUD 文案更新为“走向床边”。
- 运行时和编辑器源码静态编译通过；最终专项 Unity 回归 19 项通过、0 失败、
  0 跳过，进程退出码 0，耗时 100.077 秒，136 个受跟踪输入前后一致。
  原静态验证脚本的 URP / NUnit 引用已过时，本轮仅在临时副本补足引用。
- 回放使用 0.02s 捕获步进以保持协调器与物理时钟一致。并行手部任务曾修改
  非本轮源码，已重跑取得上述输入一致的专项结果；六个本轮生产源码打包前
  再次与验收哈希核对一致。临时测试入口及 meta 已移除。
- 报告：`项目沉淀资料/MateSoul_放置容差与躺床方向_20261027.md`；
  数值：`项目沉淀资料/validation/placement_bed_20261027.json`。
  本结论只覆盖最终 19 项专项，较早扩大回归有两项断言未复验，细节见报告。
- 保存主场景 Release 打包失败，Unity 退出码 -6，ShaderCompiler IPC 初始化报
  0x80000006；工具同时报告 Unity 配置/metrics 与 SDK 缓存写入被沙盒拒绝。
  本轮 APK 未生成；自有构建进程已退出、项目锁释放，生产源码仍匹配验收。
  恢复所需权限和现有构建脚本已在报告中给出。
- 本轮分支：`fix/placement-bed-tolerance-20261028`。未创建提交或安装头显。

## 2026-10-27：平底锅穿模与抓取吸附范围修复

- 新增 `FryingPanColliderBuilder.cs`，更新两个生产锅体预制体：圆锅底、24段上下
  凸壁及3个贴合原模型的手柄碰撞，共52个凸MeshCollider，保留开放锅腔。
  内容重建器复用同一生成器，三个生成网格共享并保留资源标识。
- 握持区域外吸附余量从12cm收紧到2.5cm；取消把手2.5cm评分加成，按实际
  碰撞面距离排序，原候选仅保留1mm平局偏好；注册表中的双手交接目标同样处理。
- 选择/公共占位查询缓冲从64到128，热接触从24到128，容纳复合锅体。
  原模型实例块、组合库存、烹饪和XRI/PhysicsBody控制权保留。
- 最终29项专项与相关回归全部通过，Unity退出0；两个锅体各4320条内壁射线通过，
  最大径向误差约1.17mm。真实锅铲六方向阻挡、锅底、牛排空腔、四类握把、邻近
  目标、超过64个重叠、双手交接/释放及原有烹饪/库存/控制器抓取通过。
- Mac无渲染120次采样，单次0.30m/45°跟随：锅体P95空场景0.6983ms、邻近桌面
  0.7514ms；锅铲0.0559/0.0566ms。计时不含动态接触求解总成本，不能当作真机帧率。
- 临时测试入口及meta已移除；交付已验收快照
  `Builds/MateSoul_PanGrip_20261027.apk`，165,796,681字节；
  SHA-256 `9b64904c6a2c1515c05574af350bbf1e1fbcaccba53cdcd65a62697baa883945`。
  稳定构建68.546秒、Unity退出0，658项构建输入一致，23项验收输入匹配。
  Gradle复用签名包并保留时间戳；1541个Unity数据、原生release staging和Gradle
  APK当时匹配，最终独立副本CRC、17个ARM64库、v2签名与4KB对齐通过。
  构建后的7个并行源码修改及新的共享staging不属于本轮29项回归结论；
  当前源码仍保留锅体生成器、2.5cm余量与新排序。详细版本边界见报告。
  本轮未安装头显，真人手感尚待验收。
- 报告：`项目沉淀资料/MateSoul_锅体碰撞与抓取吸附修复_20261027.md`；
  数值：`项目沉淀资料/validation/pan_grip_20261027.json`。

## 2026-10-28：放置与躺床改动重新打包安装

- 用户授权重新构建并安装到已连接的 PICO B3110。六个相关生产源码与此前
  19 项通过的专项验收一致；本轮未修改生产逻辑、场景或角色资源。
- 首次增量构建 Unity 成功、524 个输入一致，但 Gradle 保留旧 APK 缓存时间戳；
  已改用临时 Editor 入口调用保存场景构建方法，使用 CleanBuildCache 打包 Release。
- 清理缓存后的本地代码编译完成，整轮在 Gradle 阶段触及 20 分钟限额退出，
  未生成新 APK；自有进程和陈旧锁已清理，临时 Editor 入口已移除。
- 已转用项目现有 `scripts/run-headset.py`，使用已完成的缓存继续打包、覆盖安装、
  启动及存档/设备 APK 哈希核对。流程使用项目既定 40 分钟和 10 分钟停滞限额；
  最终包及安装身份待结果完成后补记。
- 报告：`项目沉淀资料/MateSoul_放置与躺床打包安装_20261028.md`；
  数值：`项目沉淀资料/validation/placement_bed_install_20261028.json`。

## 2026-10-28：放置与躺床 APK 覆盖安装完成

- 已完成上一章节的待办：保存主场景 Release 构建成功，新 APK 为
  `Builds/MateSoul_PlacementBed_20261028.apk`，165,796,681 字节，
  SHA-256 `9b64904c6a2c1515c05574af350bbf1e1fbcaccba53cdcd65a62697baa883945`。
- 自动流程因项目设置哈希变化在安装前停止。408 个输入中，仅预加载资源列表
  被 Unity 从两个引用清为空列表；逐字节重建两个快照哈希确认差异。
  其他 407 个输入前后一致，六个相关源码在安装前仍匹配此前 19 项通过版本。
  本轮没有修改运行脚本的校验规则或手动修改项目设置。
- 独立通过 CRC、APK v2 签名、zipalign 和 ARM64 IL2CPP 检查，01:05
  `adb install -r` 返回 Success。设备 base.apk 哈希与交付包一致，
  `matesoul-v1.json` 安装前后 SHA-256 均为
  `02f988f4be743ab00371ed1acb63047b6bf4d054a9fe00ac075e72e5dd163164`。
- 唤醒后 Activity 冷启动成功，Android TotalTime 147ms。PID 28041 存活，
  XR 渲染、UI 与左右手设备初始化日志可见；取样未见致命异常。
  设备随后休眠，未见 `MATESOUL_READY`，真人放置/躺床验收仍待佩戴操作。
- 较早扩大回归的两项未复验断言仍保留，详见原专项报告。本轮未创建提交、
  推送或采集截图/相机帧。当前完整结果见打包安装报告及其数值记录。

## 2026-10-28：Pickup_High 接入四档拾取

- 接入用户提供的 `Pickup_High_01_InPlace.fbx`，完整复制保留原文件；只导入 High，
  未重建旧 Floor/Low/Table 片段、生产角色或主场景，保护哈希前后一致。
- High 为 30FPS、1.966667s；原始接触约 1.0s，对齐后接触为 0.983333s，
  当前角色抓握点标定高度 1.229810m。53 个身体/手指骨骼使用原参考骨架适配。
- `PickupAnimations.asset` 和现有 PickupHeightBlend 增加第四档；原“物品拾取”
  按钮自动支持 High 与 Table/High 之间的混合，保留有限接触修正及所有权清理。
- 48 项动画/绑定/混合/接触/脚底/取消检查和 3 项实际 UI/四档拾取回归通过。
  在 1.105810m 高架上的苹果，实际抓握点为 1.229810m，完成拾取和持物；
  放下、暂停、超高/占用/重复等边界均通过。临时工程测试入口已移除。
- 报告：`项目沉淀资料/MateSoul_High拾取动画接入_20261028.md`；
  数值：`项目沉淀资料/validation/pickup_high_20261028.json`。
- Release APK：`Builds/MateSoul_PickupHigh_20261028.apk`，165,797,457 字节，
  SHA-256 `224e0b30aad39b73c16c62d6901e4553b38b557d370d855b519dfca72a7a1980`。
  最终构建 140.86 秒，274 项受跟踪输入一致；Gradle 输出与交付包哈希一致，
  CRC、18 项 ARM64 库、High 场景动画数据、拾取 IL2CPP 元数据和 v2 签名通过。
  临时截图已清理。本轮未安装头显或进行真人验收。

## 2026-10-28：低头手部追踪断流恢复优化

- 在 `fix/hand-tracking-recovery-20261028` 完成应用层补偿。PICO 手部高频追踪
  设置开启，内容重建及 Android 构建入口均保留设置；SDK 标注 60Hz，实际设备
  频率和功耗仍需佩戴验证。
- `PicoTrackedHand` 分开验证关节位置与旋转。手指弯曲、接触和抓握中心可使用
  当前有效位置；手掌仍需完整位姿，位置未追踪及非有限数据仍拒绝。
- `PicoDirectHandInteractor` 为已握持物品增加最多150ms宽限。断流冻结附着点，
  `PhysicsGrabInteractable` 向唯一物理所有者 `PhysicsBody.HoldFollowPose` 提交
  保持请求，清零速度；恢复要求有效运动信息、12cm位置差及45°旋转差限制。
  超时或跳变安全释放，掉落工具必须先张手再抓，避免握把辅助自动吸回。
- 正在尝试的抓取最多350ms保留意图，只恢复同目标的有效接触；张手、换目标和
  超时清除。原始追踪保持真实，UI及手部推碰不使用失效位姿，角色牵手不做物品宽限。
- 48项实际Unity回归全部通过，其中32项专项、16项原控制器/物理/牵手回归；
  Unity退出0，进程耗时179.33秒，8个相关输入一致。复核发现并修复了超时工具
  自动吸回问题；测试回放修正为PlayMode时钟与实际物理步进。
- Unity退出后工具层仍报告用户级metrics数据库受沙盒限制；XML、进程退出及
  哈希证据均通过。临时测试入口及meta已移除，主场景和冷月Prefab哈希一致。
- 保存主场景Release构建和覆盖安装已完成，流程275.14秒、1815个输入一致。
  交付为 `Builds/MateSoul_HandTrackingRecovery_20261028.apk`，165,769,469字节，
  SHA-256 `4718e2f8854787c980ad7693719d83ac9c58e25f3446242934aa93787f7158c3`。
  CRC/v2签名/zipalign/ARM64/IL2CPP及包内高频开关通过；设备base.apk哈希一致。
  存档安装前后均为 `403dc8bba63db134df73fc4bd0b787157465932d6c87679086c17512af4844ea`。
  Activity启动成功，PID29985存活，取样含UI/输入/遮挡初始化且未见所列异常；
  头显随后再次休眠，业务READY与真人低头效果、实际频率和功耗仍待佩戴验收。
  工具层统计/SDK缓存写入限制不影响上述独立核验结果；本轮未创建提交或推送。
- 报告：`项目沉淀资料/MateSoul_低头手部追踪恢复优化_20261028.md`；
  数值：`项目沉淀资料/validation/hand_tracking_recovery_20261028.json`。

## 2026-10-28：再次整合与头显覆盖安装

- 整合四档拾取High、手部追踪恢复/高频配置、锅体碰撞/2.5cm抓取余量、
  持刀性能优化、放置起伏容差和躺床方向；此前行走、情绪、AI/UI等改动保留。
- 复用并行窗口同一保存主场景Release构建，1815项输入前后一致；
  手部恢复8项、放置/躺床6项及锅体/持刀核心源资源匹配已通过专项。
- APK：`Builds/MateSoul_AllLatest_20261028-011707.apk`，165,769,469字节，
  SHA256=`4718e2f8854787c980ad7693719d83ac9c58e25f3446242934aa93787f7158c3`。
  Unity退出0，50项双程序集源码证明、CRC/v2签名/对齐/原生库及包内资源通过。
- PICO PB3A10MGL9220170B覆盖安装成功，设备包哈希与本地一致，原存档保持。
  Activity启动成功，PID29985。唤醒后Unity/UI/左右手输入就绪，取样无致命异常；
  头显自动休眠，房间READY和真人交互仍未验收。
- `run-headset.py`扩展数据asset/材质/meta/PICO设置快照，安装前签名/对齐检查，
  以Gradle输出和原生库哈希验证增量包身份，替代仅mtime判断。
- 报告：`项目沉淀资料/MateSoul_最新整合包安装_20261028.md`；
  数值：`项目沉淀资料/validation/all_latest_20261028.json`。

## 2026-10-28：冷月拾取兜底吸附与接近容差

- 将兜底吸附从45cm扩大为65cm，四档作者拾取动画也使用该边界；
  原先四档仅接受2.5cm残差而绕过旧兜底的问题已修正。主、副手统一检查，
  作者动画的手部几何修正上限仍为15cm。
- 接近站位允许4cm浅重叠，导航采样及偏移容差18cm；搜索扩大为7圈、
  每圈16方向、最大外移45cm。物品移动超过15cm才重新搜索，避免静止目标
  因扩大搜索而不断重启路线。物品动作近点到达18cm，最终站位容差15cm。
- 高架在Unity复现中停在Dock，位置误差11.98183cm、朝向误差0°；
  将最初10cm试验容差调整为15cm后，高架拾取完成。对齐接受后目标速度清零，
  Dock拥有的小角度朝向收尾继续收敛至1°以内，Stop清理状态。
- 修改 `ItemTransferStep.cs`、`CharacterActions.cs`、`AvatarMotor.cs`、
  `PickupAnimationSet.cs`；普通移动保持默认完成条件，完整导航路线仍为前提，
  没有全局缩小角色碰撞体或取消实体墙碰撞。
- 最终6项Unity回归通过，进程43.3426秒、6项核心输入一致。四档抓握高度
  0.124/0.474/0.904/1.229810m，拾取6.117/2.939/2.564/3.553秒；
  55cm残差接受、65.1cm拒绝，3cm浅重叠接受/6cm拒绝，暂停与取消释放控制权。
  编辑器Burst即时编译线程一次原生崩溃后，使用关闭即时编译参数验证实际
  Animator/NavMesh/PhysX；旧XML每轮移除，临时测试入口及meta已清理。
- 历史NearHandPickup测试仍硬编码副手46cm拒绝，本轮未修改；专项验证了新
  65cm主副手边界。“6项通过”仅指本轮选择的5项专项和1项取消回归。
- 保存主场景Release交付路径为 `Builds/MateSoul_PickupTolerance_20261028.apk`。
  最终流程149.8664秒，1815项输入一致；实际包165,770,689字节，SHA-256
  `a13745965d3c49e04dd6c0dcd154e78b960490a173cb2ce03965ad933aa6acc0`。
  CRC/v2签名/zipalign/18项ARM64库、当前Gradle与原生库身份、四档动画及8个
  生成C++函数通过。首轮因并行修改两个躺床源码被输入校验拒绝交付，最终已
  纳入最新床面源码重建；本轮未执行头显安装、提交或推送。
- 报告：`项目沉淀资料/MateSoul_拾取吸附与接近容差_20261028.md`；
  数值：`项目沉淀资料/validation/pickup_tolerance_20261028.json`。

## 2026-10-28：床面起伏与少量杂物容差调整（验证中）

- 根据真机反馈放宽 `BedGeometry`：床面高度 RMS 上限从2.5cm调为6cm，
  局部法线 y 门槛从0.9调为0.75，床面覆盖采样从90%调为75%。
  原床面尺寸、面积、稳定观察、床语义、床边可达性和转移路线检查保留。
- 允许拟合床面上10cm内的 Bed/Unknown 扫描细节；新增 `BedTarget.ContactLift`，
  根据躯干范围实际最高细节抬高躺卧目标，保持碰撞代理在细节上方。
  `RealBedScanner` 更新该值，并将明显的接触高度变化纳入姿态稳定比较。
- 高障碍不再仅因和床共用扫描分块就跳过人体净空检查；实际物理碰撞检查
  继续生效。枕头优先、床头结构和稳定默认方向逻辑保持。
- 分支 `fix/bed-surface-tolerance-20261028`；本轮仅修改两个床相关源码。
  原始副本和后续临时验收工作目录：
  `/var/folders/mp/bbv_ln_177q9ss7dlmf6trmh0000gn/T/matesoul-bed-surface-20261028-gw370vzv`。
  此时尚未完成新专项验收或新包安装，不能使用旧19项结果证明新增容差已通过。

## 2026-10-28：床面起伏与杂物容差专项通过并安装

- 上述验证中改动已完成：床面高度RMS上限6cm、局部法线竖直分量0.75、
  中央扫描覆盖75%；躯干范围10cm内的Bed/Unknown细节允许并抬高躺卧目标。
  `BedGeometry.cs`与`RealBedScanner.cs`为本轮源码修改范围，动作仍由共享
  协调器执行，床语义入口、枕头方向、床边可达性及真实碰撞检查继续生效。
- 本轮14项专项和8项既有回归共22项全通过，Unity退出0，进程40.32秒，
  当轮XML与日志有效、源码输入哈希一致。6cm/8cm连续起伏、约19%漏扫接受；
  8cm起伏实测RMS2.675cm，确实超出旧2.5cm门槛。
- 真实MeshCollider下，8cm起伏、6cm/9cm杂物床面均完成躺下/起身；
  目标额外抬升0.759cm/3cm/6cm，目标位置误差日志值均0m。
  较高障碍、同床分块高障碍、已知其他语义、高悬空物和大缺口按预期拒绝。
  临时Engine测试入口及meta已清理，未将旧19项或全库测试冒充本轮结果。
- 保存主场景Release构建与安装流程73.99秒，1815项输入前后不变；
  CRC、签名、对齐、Gradle APK及native IL2CPP一致性通过。交付：
  `Builds/MateSoul_AllLatest_20261028-020752.apk`，165,770,689字节，
  SHA-256 `a13745965d3c49e04dd6c0dcd154e78b960490a173cb2ce03965ad933aa6acc0`。
  已覆盖安装PICO `PB3A10MGL9220170B`，设备APK哈希相同；安装前后存档哈希
  均`09c8f03cb1b44bbd0aa67f388ad55057a2c18a542b47fda2cae7149a55219f20`。
- 头显睡眠时旧startup日志不能作为本轮证据；唤醒后新启动记录在UTC
  18:10:32出现`MATESOUL_READY source=PICO Spatial Mesh items=0 itemState=ready`。
  Scene Capture超时后既有实时网格路径完成初始化。实际床铺扫描与视觉贴合
  仍待佩戴体验，不将无人佩戴等待当作性能验收。
- 工具外层的failed来自Unity用户级metrics/config等写入被沙盒拦截；
  本轮真实测试、构建、安装和启动回执全部核对成功，没有重复构建安装。
  分支`fix/bed-surface-tolerance-20261028`，未提交或推送，保留其他窗口改动。
- 专项报告：`MateSoul_床面起伏与杂物容差_20261028.md`；
  数值：`validation/bed_surface_tolerance_20261028.json`。

## 2026-10-28：拾取吸附三层优先级纠正

- 用户明确：仅扩大第三层兜底，前两层范围保持原值。首轮将65cm作为统一
  准入门槛未落实该优先关系，本章及专项报告末尾补充为当前合同。
- `ItemTransferStep.SelectPickupSnapLayer` 按精确接触2.5cm、常规吸附25cm、
  第三层兜底65cm依次选择；作者动画手部IK修正上限15cm保持。
  前两层成功即完成；第三层首次失败候选只等待，下一次动作更新重新检查
  前两层，仍需兜底才执行。保留同一物品预约、Rig和物理所有者。
- 12项专项通过（8项距离/优先级边界、4项实际Unity流程），39.0076秒。
  覆盖正常吸附不等待兜底、55cm仅在复查后兜底、复查中精确接触恢复优先、
  65.1cm拒绝以及取消后的物理/控制权恢复。
- 测试期间并行修改三个HUD/手部UI脚本，其余1812项输入保持一致；
  相关拾取实现与资源稳定，未将此结果作为HUD或全库验收。
  临时入口和meta已清理。本次未重新打包、安装、提交或推送。
- 更新专项报告 `MateSoul_拾取吸附与接近容差_20261028.md`，
  将首轮“统一65cm”的实现与包体标为历史记录；
  数值为 `validation/pickup_layer_priority_20261028.json`。

## 2026-10-28：继续放宽躺床与全部固定物品放置

- `BedGeometry`床面整体高度RMS提高到10cm，中央扫描覆盖降为55%
  （35/63点）、面积比例45%、局部法线绝对竖直分量>0.6。允许20cm内的
  Bed/Unknown扫描细节并按最高躯干细节抬高接触；跨SourceId合并连通床面，
  编号变化不重置稳定状态。`RealBedScanner`两次稳定观察即可发布躺卧能力。
- `GameSession`按ImmovableAfterPlacement共用放置规则：中心及至少3/8周边
  支撑点，采样宽长各不超过物品65%；扫描支撑起伏/底部占位容差20cm，
  非扫描固定支撑起伏8cm。固定物品占位外扩统一5mm，壁挂角度及支撑距离
  同步放宽。普通可移动物品覆盖合同、上部障碍与普通实体碰撞继续检查。
- 固定物品存档恢复沿用同一支撑判定，并保持虚拟堆叠支撑链最终到静态房间。
  修改范围仅BedGeometry.cs、RealBedScanner.cs、GameSession.cs；
  本轮未改场景或角色资源，未提交或推送。
- 本轮20项专项+11项既有回归共31项全部通过，无跳过，Unity退出0、
  进程92.34秒，输入前后哈希一致。餐椅、餐桌、复古电视、饮水机全部通过
  18cm扫描台阶放置；三个周边支撑接受、两点拒绝，小虚拟支撑台对固定电视
  接受、对可移动电视拒绝。倾斜扫描墙面时钟、18cm扫描高度变化后原实例恢复、
  逆序固定堆叠、无效库存单元隔离全部通过。
- 床面约41%漏扫接受；20cm/24cm连续起伏实测RMS6.660cm/7.975cm；
  18cm杂物抬升15cm。24cm起伏与18cm杂物两轮真实PhysX躺下/起身通过，
  目标位置误差日志值0m，碰撞形状及控制权释放正常。
- 临时专项入口和meta已清理。工具外层failed来自Unity metrics数据库写入
  沙盒拦截，实际当轮验证成功。Release构建及安装此时执行中，最终回执
  追加于本轮报告：`MateSoul_躺床与固定物品大幅放宽_20261028.md`。

### 本轮最终打包回执

保存主场景Release APK已完成，流程149.50秒、1754项生产输入前后不变；
本轮3个源码与31项验收哈希一致，CRC、签名、对齐、Gradle及native IL2CPP
一致性通过。最终包为`Builds/MateSoul_BroadTolerance_20261028-025648.apk`，
165,772,917字节，SHA-256：
`ecb71cc1cab7d8fa68d3d015561b5aeec19291858f6be256ab7e83ecb5d7eee3`。

首次成功编译之后其他窗口新增Editor测试且更新HUD生产代码，因此未安装较早包；
最终包包含最新HUD输入，Editor-only Engine测试单独核验并排除出播放器输入。
期间无持有者的残留Unity锁经lsof/进程确认后清理。最终ADB设备列表为空，
本轮**未覆盖安装、未验证新包真机READY**。设备连接后需安装上述最终APK；
本轮没有改写设备存档。数值回执：`validation/broad_tolerance_20261028.json`。

## 2026-10-28：API Key 系统键盘首次按键消失修复

- 在实际 Unity TMP／XRI GUI 事件链复现输入框编辑时的外部按下清除焦点。
  TMP 失焦会关闭原生键盘并停止同步文本。修复键盘手势与游戏 UI 并行消费：
  `GameHud.IsTextInputFocused`统一读取当前 TMP 编辑状态，视线捏合、指尖触碰
  和隐藏菜单手势在编辑期间停止触发。API 地址、模型名称与对话输入同样受益。
- Android XRUIInputModule 关闭鼠标和触摸来源，继续使用追踪手势驱动 HUD。
  已有按下通过空射线取消并正常释放；结束编辑后需松开捏合或退离触碰区域
  再触发，避免最后一次键盘手势误触保存。修改仅涉及 GameHud.cs、
  HudEyeHandInteractor.cs、PicoHandPoke.cs。
- 29项 GUI 专项检查全部通过，进程22.67秒；既有 PlayerAIHudTests 5项全部
  通过，0失败、0跳过，进程22.03秒。首次／连续输入、退格、取消、禁用、
  已有点击取消和关闭后的手势恢复通过，三个修复源码输入哈希一致。
- 保存主场景 Release 构建进程120.76秒，1815项输入前后不变。
  CRC、v2签名、zipalign、当前Gradle APK与原生库身份通过；Android生成代码
  含三处焦点检查及鼠标／触摸关闭，包内metadata含get_IsTextInputFocused。
  交付`Builds/MateSoul_KeyboardFocusFix_20261028.apk`，165,772,917字节，
  SHA-256 `ecb71cc1cab7d8fa68d3d015561b5aeec19291858f6be256ab7e83ecb5d7eee3`。
- 临时Unity验收入口及meta已移除。设备查询未发现连接的PICO，本轮未覆盖安装，
  原生键盘的按键／粘贴／完成体验仍需头显验收。系统键盘短暂暂停不会取消
  Conversation的既有约定保留。
- 报告：`MateSoul_APIKey键盘焦点修复_20261028.md`；
  数值：`validation/keyboard_focus_20261028.json`。

## 2026-10-28：刀切胡萝卜的连续慢速刀路修复

- 使用生产刀具和胡萝卜回放复现：快速横切成功，但每物理步 2.5 毫米的
  慢速横切和 3 毫米的桌面下刀均不生成切块。原因是旧逻辑要求每帧位移
  至少 8 毫米，连续低速运动被逐帧丢弃。
- 修改 `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RuntimeCuttingTool.cs`：
  累计连续刀路的 8 毫米净位移，速度门槛降至 0.08 米/秒；换向、低速、
  冷却及结束持有清除累计状态。候选、扫掠和切平面均采用累计起止边。
  刀刃采样间隔从约 4 厘米缩至最长 4 毫米，覆盖窄切块。
- 20 项专项和相关回归全部通过，Unity 退出码 0，进程 83.11 秒。
  包括快速/慢速刀路、桌面切割、窄切块再切、小幅抖动不切、切块质量、
  历史恢复、XRI 抓取释放、薄墙阻挡和暂停。验证输入哈希一致，临时
  回放入口及 `.meta` 已移除。共享切割事务、占位验证和 0.14 秒冷却保留。
- 保存场景 Release 包已交付：
  `/Users/bytedance/MateSoul/Builds/MateSoul_CarrotCutFix_20261028.apk`，
  165,619,187 字节，构建与校验 157.06 秒，1,815 项输入前后一致。
  CRC、v2 签名、对齐、当前 Gradle APK 和原生库身份通过，生成 C++ 包含
  新增刀路状态。SHA-256：
  `edbba2790726d99e2915caac565905e9eb98fe8c5f8ae4bad1dec362821f3e69`。
  Unity 用户级 metrics/config 写入被沙盒拦截，工具包装层回报失败；
  Unity 本身成功退出，测试和安装包已独立校验。进程与项目锁已释放。
- 最终设备列表为空，尚未安装或完成佩戴验收。
  报告：`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_刀切胡萝卜修复_20261028.md`；
  数值：`/Users/bytedance/MateSoul/项目沉淀资料/validation/carrot_knife_cut_20261028.json`。

## 2026-10-28：现实椅子识别拒绝条件诊断与修正

- 用户反馈明显椅面/靠背也零识别。基于真实 Unity 的受控三角面回放，
  确认原 Unknown 路径的同 SourceId 假设、42 厘米宽度下限及
  0.18 平方米背部面积门槛分别拒绝跨块椅子、40 厘米椅面、条状靠背。
  SDK SourceId 是空间块，不是家具实例；高 LOD 不保证完整物体拓扑。
- `SeatGeometry` 几何回退跨块合并椅面，关联 Chair/Unknown 背部，
  最小宽深对齐角色 36×34 厘米，背部面积下限 0.035 平方米。
  使用竖直表面法线与椅面边缘关系估计朝向，限制邻墙/邻椅靠背借用；
  保留覆盖、中央支撑、稳定、碰撞和导航检查。
  `RealChairScanner` 添加分阶段状态及 Editor/Development 扫描计数日志。
- 原始规则回放 4/4 重现预期；最终 36 项验收为 35 通过、1 失败：
  16 项识别专项、18 项既有几何/扫描回归及 1 项 PhysX 接触扫掠通过。
  完整坐起回放左脚踝误差约 5.28 毫米，超过既有 5 毫米门槛；
  完整坐姿复查仍失败，没有放宽验收标准。完整动作回归不能称通过。
  最后追加起身验证被另一 Unity 实例的有效项目锁阻挡，未运行。
- 生产脚本编译通过；验收期间 10 项生产/场景/测试输入哈希一致。
  本轮只修改 SeatGeometry.cs、RealChairScanner.cs，临时入口及 meta 清理。
  未生成或安装 APK；ADB 无设备，用户现场失败原因仍待原始网格与日志确认。
  库存虚拟 Chair 当前仍只有 visit，需要独立 sit 绑定。
- 报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_现实椅子识别诊断与修正_20261028.md`；
  数值：`/Users/bytedance/MateSoul/项目沉淀资料/validation/chair_recognition_20261028.json`。

## 2026-10-28：现实环境光明暗与冷暖适配

- 新增 `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RealityAmbientLighting.cs`
  和 `Runtime/Resources/RealityLighting/AmbientReduction.shader`。角色绑定后安装唯一
  照明控制器，使用 PICO 原生 128×128 环境 Cubemap；GPU 全球采样压缩成一个
  HDR 像素，AsyncGPUReadback 上限 2Hz，每帧平滑调整既有主光和环境球谐照明。
- 强度限制为默认值的 40%～125%，受限色调混合 30%，平滑时间常数 1.25 秒。
  保留主光朝向和现有 Toon 材质；5 秒没有有效数据时平滑恢复默认打光，
  暂停/禁用时释放照明控制权。原生图像只在 GPU 消费完成后释放。
- 配置与两种构建入口均启用 Light Estimation。修正 SDK 编辑器提前读取配置
  导致默认资产覆盖的实际问题：编辑器直接加载配置，宏和 Portal 初始化延后
  至资产导入完成后的 delayCall。涉及 XR 的 PXR_ProjectSetting、PXR_Utils、
  PXR_PortalEditor；设备端原有光照接口保持不变。
- 16 项照明专项和 15 项既有遮挡回归共 31 项通过，0 失败、0 跳过，
  Unity 退出码 0、进程 36.39 秒。HDR GPU 采样与异步回读实际执行；
  代码、配置、角色、场景哈希一致，光照和追踪开关在重载后保持启用。
- 保存主场景 Release 已完成，BuildPipeline 耗时 210.13 秒，包体 165,648,273
  字节。CRC、v2 签名、zipalign、Gradle/原生库身份及包内光照开关/Shader 通过。
  1,829 项输入中仅 6 项其他窗口 Editor-only 验收文件变化，播放器输入一致，
  已核对这些类型不在包内。交付
  `/Users/bytedance/MateSoul/Builds/MateSoul_EnvironmentLighting_20261028.apk`，
  SHA-256 `5900f92f6caa3a6615fc0df5994b0c9c2b3df533c2cf2d9aa4280bc645938f24`。
  最终回执沉淀于
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_现实环境光适配_20261028.md`。
  当前未连接头显，现实 Cubemap 数值、更新延迟和融合效果仍需佩戴验收。
  0.18 为视觉映射参考亮度，不是标定 lux；本阶段只做明暗和冷暖适配。

## Phase 工作汇报：收音机旋钮持续拖动与捏合转腕修复（2026-10-28）

- 修改 `Runtime/PicoHandPoke.cs`、`Runtime/RadioKnobControl.cs`、
  `Runtime/PicoDirectHandInteractor.cs`。按钮继续接触即点、下一次 UI 更新释放；
  有拖动能力的控件保持按下，命中位置跟随指尖，不再跳回控件中心。
- 收音机两个旋钮支持靠近捏合后原地转腕：4.5cm 内选择最近旋钮，
  捕获后允许 9cm 活动距离，手腕绕实际轴的角度按 1.5 倍映射到 270° 行程。
  不使用物理扭矩；按下不跳值，松开不回弹，到端点反向立即有效。
- 捕获期间同手整机抓取让位，另一手不能夺取正在操作的旋钮。
  失跟踪、控件禁用、退离或文字编辑取消后要求重新松开并捏合；
  被拒绝或取消的捏合不转入指尖拖动，避免相邻旋钮串路。
- 13 项专项与 6 项既有收音机集成回归全部通过，0 失败、0 跳过。
  Unity 退出码 0，验证进程耗时 51.24 秒；使用真实 XRUIInputModule、
  Metal Canvas 命中和生产 Radio.prefab，手部样本为注入姿态。
  源码、收音机预制体、主场景、冷月预制体与 Animator 哈希一致。
- 验证器在 EditMode 中显式激活当前 EventSystem，并清理跨用例注册，
  避免旧 EventSystem 导致 UIInputModule.Update 根本不执行的假失败。
  临时验证入口及 meta 已移除，脚本与日志仅留在
  `/private/tmp/matesoul-radio-knobs-20261028`。真机未连接，手感仍待佩戴验收。
- 保存主场景 Release BuildPipeline 通过，耗时 152.32 秒；
  构建与核验总计 194.75 秒，APK 为 165,650,833 字节。1,820 项构建输入
  全部保持一致，CRC、v2 签名、zipalign、Gradle APK 与原生库身份检查通过；
  已确认转腕和捕获实现进入生成 C++ 与当前包内 libil2cpp.so。
  交付 `/Users/bytedance/MateSoul/Builds/MateSoul_RadioKnobs_20261028.apk`，
  SHA-256 `fc4ae1ba4c070c0e2c5fa4055eb15856f86f48711be7ab7d8964506b5aee53e1`。
  头显未连接，未安装；修复报告为
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_收音机旋钮交互修复_20261028.md`，
  数值记录为 `项目沉淀资料/validation/radio_knobs_20261028.json`。

## Phase 工作汇报：三视图盐罐与挤压酱瓶（2026-10-28）

- 制作盐罐、黑椒酱、沙拉酱 Blender 母版、FBX、贴图和生产 Prefab。
  盐罐为 58 × 58 × 130 mm、9,736 三角面，金属盖有 14 个真实孔；
  两种软瓶各 6,592 三角面，包含白色翻盖喷嘴与瓶身/酱汁双 Squeeze 形变。
- 新增 `Runtime/SeasoningDispenser.cs`、`Editor/CondimentBuilder.cs`、
  `Runtime/Shaders/CondimentGlass.shader` 和 `SauceParticle.shader`。
  `PhysicsGrabInteractable` 提供当前驱动手，
  `PicoDirectHandInteractor` 复用已持握手指的有效弯曲量。
  三件物品已加入保存主场景的厨具目录，场景构建器同步注册。
- 盐罐倾角 160° 开始撒盐，恢复至 150° 以下停止；单罐最多 768 粒。
  酱瓶持握后继续挤压出料，手柄沿用已绑定扳机；单瓶最多 160 粒。
  粒子碰撞不创建独立刚体，短生命周期、距离/离屏控制、无粒子阴影，
  贴图 ASTC 6×6 / 512 / Mipmap，松手与追踪丢失停喷、暂停清空。
- 14 项有界 Unity 集成专项全部通过，退出码 0、24.88 秒。
  验证含真实 XRI 选择/释放、注入的控制器状态与手部弯曲、
  迟滞、跟踪恢复/换手、软瓶复原、粒子上限、生命周期与实际 Metal URP 渲染。
  既有播放模式回归发生帧推进停滞，已停止拥有的进程；
  不将这次未完成回归计入通过结果。主工程临时验收入口已移除。
- 母版位于 `/Users/bytedance/MateSoul/ArtSource/Condiments/`，
  专项报告为 `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_盐罐与挤压酱瓶_20261028.md`。
  保存主场景 Android Release 已通过，BuildPipeline 222.76 秒、编译与核验
  总计 256.46 秒，实际 APK 167,604,969 字节。1,882 项输入未变，
  CRC、v2 签名、对齐、Gradle APK/原生库一致性检查及包内物品/Shader 检出通过。
  交付 `/Users/bytedance/MateSoul/Builds/MateSoul_Condiments_20261028.apk`；
  数值回执为 `项目沉淀资料/validation/condiments_20261028.json`。
  临时预览和自动模型备份已清理；未安装到头显，真机手感与 GPU 帧耗尚未验收。

## Phase 工作汇报：喝水动画与自主物品白名单（2026-10-28）

- 接入用户提供的 `Drink_01_InPlace.fbx`，30fps、3.3 秒，重定向生产骨架的
  53 个身体/手指节点，烘焙实际杯口到嘴部的接触适配。
  新资源位于 `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/ItemActions/Use/`，
  原 FBX 不变，生产角色、面部形态和既有拾取/转身 Clip 不替换。
- `ItemDefinition` 新增 `useAnimation` 与默认关闭的 `allowAutonomousUse`；
  自主随机交互初始只允许马克杯。白名单只限制自主 `visit`，
  切水果、保龄球和显式拾取的物品预约不受影响。
- 新增 `UseHeldItemAction`，复用 `ItemActionRun` / `ItemTransferStep` /
  `AvatarItemRig` 完成接近、拿杯、喝水和放回。低处使用拾取蹲身段反向放下，
  桌面使用原有放置姿态。返回重新求站位并忽略已持杯子的自阻挡。
  三层吸附维持 2.5cm、25cm、最后 65cm 兜底。
- 最终 11/11 专项和回归通过，Unity 退出 0、89.91 秒。
  验证包括地面 UI / 桌面自主完整流程、白名单越权、占用过滤、
  取消/暂停/抢占/目标移除、切水果预约/工作台碰撞、保龄球真实 XRI 抓取。
  杯口标定点误差约 0.023mm，根节点采样漂移 0；这是编辑器数值验证。
- ShaderCompiler IPC 初始化失败时改用 `-nographics` 完成最终流程回归；
  此前已检查主体动作 Metal 预览。临时验收入口及预览媒体已清理。
  本轮未打包、安装或执行真机验收。
- 报告为 `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_喝水动画与自主交互白名单_20261028.md`，
  数值回执为 `/Users/bytedance/MateSoul/项目沉淀资料/validation/drink_use_20261028.json`。
  后续单手物品先绑定使用动画和控制器状态，再启用白名单标记。

## Phase 工作汇报：低头偏头裁剪与动态深度兜底（2026-10-28）

- 加固 PICO Unity 环境深度入口：检查原生 descriptor 返回值、姿态、FOV、
  近远平面与双眼纹理结构；公开成功发布的 Unity 帧号，渲染只使用当前发布帧。
  深度失败/停更时停止动态 alpha 合成，继续使用原有实测网格和碰撞。
- 预处理拒绝 0、1、NaN 等邻域样本；像素端检查完整滤波边界及四个统计 texel，
  防止无效标记被双线性插值变成可信深度。均匀不确定区域保持完整可见，
  真实遮挡边缘仍保留软过渡。视点 8cm 内的实测片元不写视觉深度。
- 主 XR 相机及构建器近裁剪由 5cm 改为 2cm；生产角色与动画资源未修改。
  最终 Metal GPU / 引擎专项 48/48，通过停更恢复、缓存碰撞/替换、HUD、
  70°低头和 45°偏头场景；Unity 测试退出 0、36.895 秒。
- Android Release 重建成功，Unity 退出 0、119.427 秒，BuildPipeline
  94.531 秒。首次 Burst 增量哈希缓存读取触发 Encoding 空引用；
  重建通过独立编译进程绕过增量缓存，保留 Burst AOT 的 98 个方法。
  CRC、v2 签名、对齐、ARM64/IL2CPP 与当前原生库一致性核验通过。
  交付 `/Users/bytedance/MateSoul/Builds/MateSoul_DepthFallback_20261028.apk`，
  实际大小 168,074,571 字节；9 个相关输入在测试及构建期间一致。
- 原生 SDK 没有采集时间戳，成功返回但内容冻结仍无法从 Unity 帧号识别；
  正常距离的错误实测网格、设备 GPU 帧耗和低头现场效果需要真机验收。
  当前头显未连接，本轮尚未安装或验收现场症状。
- 专项报告：
  `/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_低头偏头裁剪与深度兜底_20261028.md`。
  数值回执：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/depth_fallback_20261028.json`。

## 最新全部改动整合打包，等待头显连接（2026-10-28，20:16构建快照）

- 保存主场景Release构建完成，包含喝水/自主白名单、盐罐酱瓶、环境光、
  收音机旋钮、刀切修复、椅子识别和深度兜底，以及此前全部生产改动。
- APK：`Builds/MateSoul_AllLatest_20261028-201655.apk`，168,074,571字节，
  SHA256=`536eb440aa82e95895121b4cd764f5adc236d6a94f10c6fdcd9f38121c138855`。
  Unity退出0；3362项输入稳定，72项双程序集源码、CRC/v2签名/对齐/原生库通过。
- 包内Drink及四档拾取、三种调味物品、环境光/粒子Shader、高频手与2cm近裁剪
  检出；五个角色网格和61面部形态保持。深度9项、照明6项匹配专项验收。
- 头显ADB/USB/mDNS未连接，未安装或修改存档。已经准备固定APK安装脚本，
  连接并允许USB调试后可直接覆盖安装，不需重打包。
- `run-headset.py`增加离线`--build-only`、SDK/HLSL快照，以及已验证的独立
  Burst编译方式。外层统计写入限制与成功Unity/包验证结果分别记录。
- 既有椅子坐起回归脚踝误差失败仍未解决，本轮仅整合打包。
- 报告：`项目沉淀资料/MateSoul_最新整合包待安装_20261028_201655.md`；
  数值：`项目沉淀资料/validation/all_latest_20261028_201655.json`。

## 最新整合包覆盖安装与房间 READY（2026-10-28，20:34）

- 头显PB3A10MGL9220170B恢复USB连接后，直接安装20:16快照APK，未重建或修改
  生产资源。覆盖安装Success，设备base.apk与本地536eb440…138855哈希一致。
- `matesoul-v1.json`安装前后SHA256均为
  `ddcd0a215a3fe9435c8ff653d214bb2517af5a446aa936992cbf0a859095b8cc`。
- Activity冷启动Status ok，Android TotalTime140ms。新进程9575于20:34:40.822
  报告`MATESOUL_READY source=PICO Room Capture items=2 itemState=ready navMs=9`。
  持久启动日志和当前PID日志一致，房间就绪，2件物品恢复，启动取样无致命异常。
- 安装待办已完成，具体真人交互与环境光/深度效果仍保留各专项边界。
  已更新原报告及`validation/all_latest_20261028_201655.json`，未提交或推送。

## Phase 工作汇报：物品接近站位误判修复（2026-10-28）

- 回捞 PICO 实际运行日志并加入一次性原逻辑探测：盘子 34 个导航命中
  全部被距理想动画站位 18cm 门槛拦下，路径与占位检查均未执行。
  底层导航至少在 75cm 内采样，原动作却没有利用附近可走落点和既有
  65cm 最后吸附能力。根因是动作站位准入与现有可达能力脱节。
- `ItemTransferStep.FindApproach()` 保留所有原候选，失败后才为单手拾取
  追加 65cm 导航投影，依据实际落点修正朝向，计入已有 15cm IK 修正能力。
  保留完整路径、4cm 角色占位容差及实际 2.5cm/25cm/最后 65cm 三层吸附；
  放回与双手动作维持原搜索。生产仅在失败时落盘单次筛选统计。
- 29/29 专项和回归通过，Unity 退出 0、进程 171.31 秒；涵盖原候选
  优先、断路/实体障碍拒绝、三层吸附、喝水白名单、切水果和保龄球。
  清理探针后 Release 重编译退出 0、220.23 秒，3362 项输入保持一致。
- 正式 APK `Builds/MateSoul_ItemApproachFix_20261028.apk`，168,092,347
  字节，CRC/v2签名/对齐/ARM64及当前原生库核验通过。23:24 已覆盖安装；
  设备 APK 与本地 `4451eccb…d11d3cd` 一致，安装前后存档哈希一致。
- 最终诊断包的现场探测因头显 Asleep、Scene Capture 超时及 Spatial Mesh
  `ERROR_RUNTIME_FAILURE` 未执行，尚无修正后盘子现场路径成功证据。
  已换回正式包并删除 marker、清空本轮 USB 转发、恢复常亮设置原值 0。
  新启动 READY 与真人完整拾取需保持头显唤醒后验收，不以 Activity 时间
  或编辑器投影回归代替现场结论。
- 正式版 23:24:32 新启动已核验；23:25:01 同样出现 Spatial Mesh
  `ERROR_RUNTIME_FAILURE`，没有 READY。已停止有界等待，未继续重启或
  放宽 SDK 识别规则，需要戴上头显保持唤醒后重新定位。
- 报告：`项目沉淀资料/MateSoul_物品接近站位误判修复_20261028.md`；
  数值：`项目沉淀资料/validation/item_approach_20261028.json`。
  没有提交、推送或覆盖角色资源。

## 床房间标定辅助与实时网格精细识别（2026-10-28，22:55验证）

- 修改 `BedGeometry.cs`、`RealBedScanner.cs`。实时 Bed 识别优先；不足时复用
  Room Capture 中 Bed 身份/区域/方向，关联当前 Bed/Unknown 网格测量床面。
  高床头盒不直接决定高度，按实测面积主导高度带细化；可合并不连续床面片段。
- 保留现有 10cm 起伏、55% 覆盖、20cm 低细节容差、头向线索与接触高度调整。
  当前网格不足时明确提示已定位的床；障碍提示与稳定确认提示分别显示。
  没有实测支撑不会发布 Lie，没有给 Unknown 全局补旧标定语义。
- 同块 Unknown 的外部障碍按局部三角面重新检查，避免将整块网格当床框忽略。
  椅子、角色、主场景、动作资源与协调器本轮未修改。
- Unity 本轮独立验证 30/30，包含 21 项辅助专项、7 项原床回归和
  2 项真实 PhysX 躺下/起身/失效恢复/侧向进出回放。Unity 退出 0，
  全流程 69.903 秒，142 项输入哈希一致。临时测试及 meta 已清理。
- 外层工具对 Unity 用户级 metrics 数据库写入报告沙盒限制，与上述实际
  原生退出及唯一 XML 分开记录。本轮未重新打包或安装，没有把20:16旧包
  当作本次床辅助识别的部署证据。标定定位精度和现场躺床仍需真机验收。
- 报告：`项目沉淀资料/MateSoul_床标定辅助实时精细识别_20261028.md`；
  数值：`项目沉淀资料/validation/bed_calibration_20261028.json`。

## 沮丧动画与音效同步接入（2026-10-29）

- 接入 `Cry_01_InPlace.fbx`，生成 `Lengyue_Sad01.anim`，3.266667 秒 /
  30 fps / 55 骨骼 / 550 身体曲线 / 61 面部曲线，其中 9 条动态表情曲线。
  复用 3.28 秒 `Sad_Sigh`，原速播放，不修改音频。
- 新增 `SadEmotionBuilder` 和 Emotion.Sad 状态；`ResponseEmotionAudio`
  将生气与沮丧合并为一个音频时钟播放器，通过动作协调器统一申请移动、
  全身、视线、面部控制。无效动作清理只作用于实际启动的动作实例。
- 5/5 引擎检查通过，包含 AI 回复、声音进度与真实骨骼/表情采样、
  连续播放、交互等待、抢占、暂停、禁用，以及生气与音效池回归。
  沮丧 301 个采样点 + 2,174 连续帧，最大时钟误差 5.96046448e-8，
  状态进度误差 0，骨骼旋转误差 0°。
- 角色 prefab、主场景和转身片段哈希一致。控制器仅增加 Sad 状态及其
  Emotion 状态机引用。临时检查入口已移除。
  清理后生产工程再次编译通过，原生退出码 0，耗时 31.025 秒。
- 恢复过期临时目录指向的同版本 Unity 包缓存，移除已失效的旧验证源码链接。
  外层工具的用户级 metrics 沙盒报告与引擎通过结果分别记录。
- 报告：`项目沉淀资料/MateSoul_沮丧动画与音效同步_20261029.md`；
  数值：`项目沉淀资料/validation/sad_emotion_20261029.json`。
  本轮未生成 APK；PICO 真机听画效果待验收。

## 时钟现实时间指向与恢复同步修复（2026-10-29）

- 修改 `RealTimeClock.cs`。复现 Unity 导入后的默认指向为时针 +52°、
  分针 −56°，与 Blender 配置符号相反；旧公式重复叠加默认偏移，
  00:00 实际指向 104° / 248°。改为抵消导入姿态后显示真实时间。
- 保留设备本地系统时间、每分钟分针 6°、每 5 分钟时针 2.5°、
  忽略秒以及 12/3/6/9 数字。启用、重新显示、恢复和获得焦点立即同步，
  常规更新继续使用不受游戏暂停影响的时间。
- 正式 Clock Prefab 真实 Play Mode 检查 12/12，包含全天 1,440 分钟、
  4,320 次秒位置采样、任意摆放姿态、跨午夜/跨年/时间回拨、
  启用/恢复回调和冻结游戏时间后的实际更新。最大指向误差 0.0003967285°。
- Unity 原生退出 0，约 40 秒；外层用户级 metrics 写入限制单独记录。
  时钟 Prefab/FBX、角色 Prefab、控制器、主场景哈希一致，
  临时验证入口及 meta 已移除。旧相邻角度增量检查不足以证明刻度指向正确。
- 报告：`项目沉淀资料/MateSoul_时钟现实时间修复_20261029.md`。
  数值：`项目沉淀资料/validation/clock_time_20261029.json`。
- 已生成 `Builds/MateSoul_ClockTimeFix_20261029.apk`，ARM64 / IL2CPP Release，
  168,237,103 字节，构建及校验 230.43 秒。CRC、签名、zipalign、
  原生库与当前 Gradle 输出一致性均通过，3,366 项源输入哈希一致。
  当前没有连接 PICO 设备，尚未安装，真机前后台与时间验收待完成。

## 冷月 Direct59 主工程合入（2026-10-29）

- 将 Direct56 拇指暗纹修复、Direct57 柔和深紫眉及 Direct58/59 发缝几何
  累计接入生产角色。新增资源位于
  `MateSoul/Assets/LengyueIntegration/Direct59/`。
- 生产角色仍使用 Direct54 游戏面网格的 61 个 Morph、Direct54 +
  LimbRefinement01 身体、原补臂和 SoftFit01 服装；骨架、Animator、动作协调器、
  动画事件和 Clip GUID 均未替换。主场景继续引用原生产 Prefab。
- Direct59 源闸门、8 个资源字节一致性、GUID 唯一性、材质依赖、61 Morph 和
  Prefab 精确反向替换检查通过。相对合入前，Prefab 仅有 5 个预期绑定变化。
- `ProductionCharacterValidation` 已更新为检查 Direct59 头发/发束线、
  Direct56 身体材质、Direct57 面部表情材质及主场景继承关系，并使用 Unity
  2022.3.38f1 Mono 编译器和当前项目程序集独立编译通过。
- Unity 2022.3.38f1 batchmode 在项目导入前因
  `com.unity.editor.headless` 授权失效退出；本轮没有新的运行循环、APK 或
  PICO 真机结论。通用框架脚本还因现有 URP Renderer Feature 未纳入其 Roslyn
  引用而中止，两项均不解释为冷月资产失败。
- 报告：
  `项目沉淀资料/MateSoul_Direct59冷月主工程合入_20261029.md`；
  数值：
  `项目沉淀资料/validation/lengyue_direct59_20261029.json`；
  合入前备份和证据：
  `ArtSource/LocalStudyOnly/Lengyue_ProductionMerge_20261029/`。

## Codebase 可移植项目快照发布（2026-10-30）

- 将当前生产 Unity 工程整理并上传到
  `git@github.com:picoxr/VibeCodingSampleProject_MateSoul.git`。目标分支为
  `import/matesoul-snapshot-20261030`，最新提交为 `9379e3f`；
  MR 为 `MR !1`。
- `main` 是受保护分支，当前默认规则要求 1 名 Developer 及以上成员批准。
  MR 的 4 项服务端检查全部通过，Auto merge 已启用；批准后会自动合入。
- 发布快照包含 5,271 个文件：Unity `Assets/Packages/ProjectSettings`、
  仓库内 PICO Unity SDK `XR/`、UniVRM 依赖 `Dependencies/` 和可选历史
  AI 网关。缓存、APK、日志、验证媒体、本机设置、`.env` 和 adb 私钥均排除。
- 4 个实际被 Study 材质引用的 134,218,768 字节纹理超过 Codebase
  100 MiB 普通 blob 限制，已通过 `.gitattributes` 精确纳入 Git LFS。
  LFS 上传 4/4、537 MB 完成；普通 Git 最大 blob 降为 67,281,324 字节。
- 从 Codebase 分支执行全新克隆，完成 5,271 个文件 checkout 和
  4/4 LFS 对象还原。`git fsck --full`、`git lfs fsck`、4 个 SHA-256、
  Unity `.meta` 配对（0 缺失、0 孤立）、相对包路径和大小写路径冲突检查通过。
- 克隆中的历史 AI 网关执行 `npm ci && npm run build` 成功。`npm audit`
  报告 3 个 moderate、3 个 high 依赖漏洞；该网关不参与当前 Unity APK
  运行，后续若重新启用必须先单独治理依赖。
- 当前机器未安装可调用的 Unity 2022.3.38f1 Editor，因此本次没有在全新克隆
  上重跑 Unity 导入/Android 构建。合入后仍需从 `main` 再克隆一次并用对应
  Unity 版本打开 `/Users/bytedance/MateSoul/MateSoul` 等价路径下的工程目录。

## 仓库范围调整：从"可运行快照"扩到"可继续开发副本" — 2026-10-30

用户要求仓库不仅能发布，还要支持另一台设备克隆后直接继续开发，因此重新划定
入库边界，并新建 MR `!2`。

- 新入库：`项目沉淀资料/`（277 个文件，16 MB，含方案、技术文档、专项报告、
  `maintenance/`、`真机验收/`）与冷月生产链 DCC 源
  `ArtSource/LocalStudyOnly/Onigawara_Lengyue_20261012/eye_shape_reference_v12_dcc_real/Fix54`–`Fix59`
  （259 个文件，171 MB，含 4 个 `.blend`、构建脚本、导出缓冲和 gate 报告），
  另加 `Lengyue_ProductionMerge_20261029` 合入证据。
- 仍排除：`项目沉淀资料/资产交付/`（1.0 GB 交付 zip 与 FBX）、`ArtSource/SapphiArt`
  衣柜与 EyeLab 历史源（4.4 GB）、`ArtSource/LocalStudyOnly/Onigawara_Lengyue_20261012/Unity6Study`
  （6.3 GB 旧独立工程）、APK、运行日志和美术工作渲染图。
- 入库后仓库 5,807 个文件、1,751 MB，最大普通 blob 仍低于 100 MiB；
  `Fix54`–`Fix59` 的 `.blend` 为 34,684,232 字节，未触发 LFS 限制。
- 远端分支 `import/matesoul-docs-20261030`，提交 `2c7ee3e`；MR
  `MR !2`，Auto merge 已启用，
  `main` 仍为受保护分支需 1 名 Developer 批准。
- 过滤克隆复验：5,807 个文件、277 个文档、259 个美术源文件、仓库内 `.meta`
  仅 1 处例外（`Dependencies/.../UniGLTF/Runtime/UniGLTF/.gitignore` 本身不是资产）、
  0 孤立 `.meta`、LFS 指针未变、无符号链接与大小写冲突。

## 开发资产全部入库 — 2026-10-30

用户要求"全部开发资产都入库"，因此撤回此前的开发资产排除策略，MR `!2` 追加提交
`95476b5`。

- 新增入库 792 个文件：`Assets/MateSoul/Tests`（75）、`Assets/Samples/XR Interaction
  Toolkit` 设备模拟器（113）、`Assets/Characters/SapphiArtCustom/Lengyue/Validation`
  与全部探针脚本、`XR/Assets|Runtime|Editor/Debugger`（126）、`XR` 各 Samples
  （114）、`Dependencies/Lengyue/UniVRM-0.131.3` 包内样例与测试（268）、
  `services/ai-gateway` 的 scripts 与 tests（8）。
- 撤回的忽略规则：测试与验证内容、探针清单、SDK 样例/调试器、依赖包内部样例与
  测试、以及两份"陈旧 Editor 副本"。后者属 `MateSoul.Editor` 程序集，与
  `Lengyue/Editor`（Assembly-CSharp-Editor）内的同名类不在同一程序集，不会冲突，
  且 `GameSceneBuilder` 所在的程序集只解析自己那份。
- 同时删除本地孤立元数据 `Lengyue/Runtime/Resources.meta`（仓库已在 `9379e3f`
  移除该目录，本地残留会导致重新入库）。
- 入库后 6,599 个文件、1,788 MB，无超过 100 MiB 的普通 blob；4 个 LFS 纹理仍为
  指针，未因重新暂存被还原成实体。
- 官方 SDK 位置保持仓库根 `XR/`（未搬入 `Packages/`）：`file:../../XR` 相对
  `MateSoul/Packages/` 解析，完整 clone 已验证可用；仅取 `MateSoul/` 子目录会失效，
  README 已写明必须整仓 clone。
- 克隆复验：`.meta` 缺失仅 5 处且均非资产（`.gitignore`、两个 `.sample.json`、
  两个本地未生成 `.meta` 的 Editor 脚本），0 孤立 `.meta`；此前两处断链
  （`RoomNavigation_v001.unity` 的探针脚本、`XRDeviceSimulatorSettings.asset`
  的模拟器预制体）已恢复解析。
