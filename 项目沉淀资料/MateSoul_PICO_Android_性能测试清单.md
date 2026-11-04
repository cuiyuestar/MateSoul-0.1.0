# MateSoul · PICO / Android 性能测试清单

编制日期：2026-10-22。适用工程：`/Users/bytedance/MateSoul/MateSoul`。

本文是待执行的测试清单，所有用例均未执行；不构成已经发现泄漏、达到性能目标或通过设备适配的结论。工程事实来自当前配置与源码，官方机制见文末来源；测试次数、时长与验收目标是项目建议值，需结合目标设备规格确定。

## 1. 测试目标与判断原则

目标是回答三个问题：默认画质能否在目标设备持续稳定运行；日常玩法的安全容量是多少；反复使用与长时间运行是否会不断积累资源。

高性能手机级芯片可以作为候选硬件，但不能直接推出 MR 游戏性能合格。双目眼纹理、系统透视与追踪、环境深度合成、渲染分辨率和设备散热都影响可用预算。应以目标终端的 Release APK、实际刷新率和至少一小时的持续运行结果作为主要依据。Unity 建议在最终发布平台分析性能；PICO 监测工具提供眼纹理、帧率、温度、频率、PSS 等指标。<a href="#source-1">[cite:1]</a><a href="#source-2">[cite:2]</a>

需要分开得出以下结论：

| 结论范围 | 需要的证据 | 不能代替它的证据 |
|---|---|---|
| 设备适配 | 目标真机 Release、真实双目/MR 功能、持续负载 | 模拟器 FPS、桌面 Unity 播放结果 |
| 启动体验 | 首帧、可交互、扫描/授权、恢复阶段分别计时 | 只看启动页或 `am start -W` 返回 |
| 内存是否够用 | 进程 PSS/RSS、系统压力、启动/玩法峰值 | APK 大小、C# 堆、Java heap 上限 |
| 是否存在泄漏 | 相同状态重复循环、资源净增、引用链或分配栈 | 一次操作后 PSS 未立即下降 |
| 性能优化有效 | 相同设备、内容、状态、温度与工具，单变量对比 | 同时减画质、物品数量与功能后的 FPS |

## 2. 当前工程中应优先覆盖的风险点

下列内容是静态核查发现的测试重点，不是已确认的性能缺陷。

| 编号 | 已核查的工程事实 | 对应测试重点 |
|---|---|---|
| E01 | Unity 2022.3.38f1、URP 14.0.11、Android ARM64 / IL2CPP，增量 GC 开启；本地 PICO Unity 包 `com.bytedance.pico.xr` 的版本字段为 0.13.1 | 使用 Unity / PICO XR 路线；工具/API 支持以当前包和实际系统为准，不能套用 Kotlin Spatial SDK 的资源预算 |
| E02 | `GameBootstrap.InitializeAsync` 按房间读取 → 遮挡快照 → `Room.Build` → 角色与会话装配 → `RestoreItemsAsync` 执行；完成时输出 `MATESOUL_READY` | 逐阶段启动耗时、短时峰值内存、存档内容对启动时间的影响 |
| E03 | `PicoSceneCaptureRoomMapProvider` 先查询 Scene Anchor，无数据才发起 Room Capture；NavMesh 每次启动由 `BuildNavMeshData` 构建，未在此路径发现持久化 NavMesh 读取 | 区分“系统房间数据复用”和“Unity 导航重新构建”，不能假定二次启动全部命中缓存 |
| E04 | `PXR_Settings.asset` 的 `stereoRenderingModeAndroid: 0` 在当前枚举中是 MultiPass；显示频率选择 Default，App SpaceWarp 关闭 | 核查 APK 实际渲染模式与刷新率；测试兼容的 Multiview 对照版本，并检查双眼正确性 |
| E05 | 主场景序列化值为 `foveationLevel: -1`、`eyeFoveationLevel: -1`、`adaptiveResolution: 0` | 当前配置没有启用这些降负载能力；真机确认生效值后再做分辨率/注视点渲染对照 |
| E06 | 有 Performant、Balanced、High Fidelity 三档 URP；High Fidelity 配置 4× MSAA、主光阴影图 4096、4 级联、阴影距离 150，并支持附加光阴影；`QualitySettings` 当前档索引为 2，Android 默认映射为空 | 先确认运行时实际档位；高档阴影和双目渲染可能成为 GPU/带宽重点，不能把编辑器当前档等同于设备实际档 |
| E07 | 三档 URP 均开启深度纹理；动态环境深度有全屏合成 Pass，静态空间遮挡另外生成 Mesh；导航/碰撞和遮挡可能各持有同一来源几何的副本 | 分别测原生深度、URP 合成、静态网格上传与网格副本的成本 |
| E08 | `RealityOcclusionMeshRenderer` 在模拟器禁用原生 Environment Depth，保留静态遮挡；其判断使用 Android 身份中的 emulator 标记；`GameBootstrap` 选择房间 Provider 仍有 `swan_arm64` 分支 | 在真机确认实际 Provider 与深度能力均正确；模拟器与真机负载不同，设备名不能替代能力验证 |
| E09 | 冷月基线为 Direct49 头发/头部、SoftFit01 服装及肢体优化；合并验证记录有 15 个 SkinnedMeshRenderer，但不是同屏活跃数量；`AvatarVisualDynamics.LateUpdate` 执行 VRM 弹簧骨骼、裙摆及 BlendShape 同步 | 实测活跃 LOD、材质/Pass、Animator、IK、Spring Bone、BlendShape 的 CPU/GPU 成本 |
| E10 | `RuntimeCuttable` 生成视觉与碰撞网格，并调用 `Physics.BakeMesh`；碎片有运行时 Mesh；生成/销毁路径均需覆盖 | 切割尖峰、碰撞烘焙、失败回滚、恢复切割历史、动态网格释放 |
| E11 | 咖啡罐运行代码默认有 720 颗实例化渲染的可见豆、336 逻辑容量、24 个物理豆池上限；Prefab 可覆盖配置 | 避免将视觉豆数误算为刚体数；核查实际池数量、多罐总成本与回收后的池生命周期 |
| E12 | 电视创建 1024×738 ARGB32 RenderTexture，并使用 VideoPlayer；收音机使用音源交叉淡化 | 首次解码、换台与循环、GPU 缓冲、音频解码缓存、反复开关/回收释放 |
| E13 | UI 主 SDF 字体构建为 2048×2048 RGBA32 图集；fallback 为动态多图集字体 | 字体首次使用、非常用汉字生成、多图集增长与 CPU 可读副本；单张 RGBA32 图像像素负载为 16 MiB，实际驻留须测 |
| E14 | 存档路径为 `Application.persistentDataPath/matesoul-v1.json`；读写 JSON 和临时文件替换是同步操作；AI 通过 HTTP 网关请求，历史保留最多 16 条消息 | 大存档序列化/磁盘尖峰；区分手机端 HTTP/文本成本与服务端 LLM 延迟；历史有界仍需测大响应和请求释放 |
| E15 | 存在 Development 探针和部分直接 HTTP 调试上报；一些历史插桩位置未统一限定为 Development | 建立无一次性调试上报的发布基线；不存在的调试服务、频繁日志与请求本身不能污染性能结论 |

工程依据的具体文件链接见第 15 节。

## 3. 设备、构建与测试矩阵

### 3.1 必须记录的环境

- [ ] 终端型号、SoC、物理 RAM、PICO OS 与 Android 版本、系统完整 build fingerprint、GPU 与驱动版本。
- [ ] APK SHA-256、源码版本/工作区状态、安装方式、包名、Activity、Release/Development、SDK 版本、IL2CPP 与 ARM64。
- [ ] 实际图形 API、实际画质档、双目模式、眼纹理宽高、render scale、MSAA、阴影、注视点渲染、自适应分辨率、SpaceWarp、系统刷新率。
- [ ] 房间数据来源与大小、空间 Mesh 块数/三角形数、物品类型和数量、切块/碎片/物理豆数量、存档字节数。
- [ ] 电量、是否充电、室温、设备起始温度、网络、系统驻留应用、是否录屏/投屏/开 Profiler。
- [ ] 场景操作脚本、轮数、起止时间、采样频率、所有计量单位及测试中途 PID 是否变化。

### 3.2 最小测试组合

| 维度 | 必测组合 | 执行规则 |
|---|---|---|
| 设备 | 最终目标真机；如有多型号，再测最低支持配置 | 邻近规格 PICO 真机可先做筛查，但不能替代最终硬件签核 |
| 构建 | Release 无工具基线；Release 系统监测；Development Unity 分析 | 分开报告，不能直接混合数值；Development 内容和画质应与 Release 相同 |
| 内容 | 干净存档、常用存档、最大支持容量、切割历史较多的存档 | 保存固定样本与操作步骤；真实房间需固定，另测复杂房间 |
| 时段 | 冷机、运行 15/30/60 分钟、长稳 2 小时；需要常驻时再测 8 小时 | 连续运行中按相同窗口比较，不能只测开机前 5 分钟 |
| 电源 | 日常电量不充电；低电量；充电运行独立测试 | USB 充电改变温度/功耗条件；条件允许时用无线 ADB 测不充电场景 |
| 网络 | 正常网络、断网、慢响应/失败响应 | 通过授权测试网关控制响应；启动就绪不应被 AI 网络请求无意阻塞 |

默认先测一种真实默认画质。只有发现瓶颈后，再对另两档、分辨率或功能做单变量对照，避免一开始展开所有组合。

## 4. 指标口径与建议门槛

以下是项目初始目标，不是 PICO Store 官方认证标准。缺少目标设备 RAM、刷新率和厂商预算时，不给出“所有设备通用”的固定内存上限。

### 4.1 启动与响应

Android TTID 是首帧显示时间；TTFD 需要应用报告完全就绪。系统首帧可能只代表 Unity 启动页，不代表房间和物品已可操作。Google Android vitals 的 5 秒冷启动、2 秒温启动、1.5 秒热启动告警针对 TTID，不能直接用作含 MR 扫描的 TTFD 门槛。<a href="#source-3">[cite:3]</a>

| 指标 | 定义 | 初始建议目标 |
|---|---|---|
| 首帧时间 TTID | 启动 Intent → Android 首个应用画面 | 常规冷启动 P95 < 5 秒；同时记录真正的 Unity/XR 首个内容帧 |
| 游戏可交互时间 | 启动 Intent → 房间、角色、HUD、物品恢复完成，并在后续内容帧通过一次交互 | 已有房间/常用存档：P95 ≤ 8 秒；可向 ≤ 5 秒优化 |
| 恢复前台 | 应用保持进程/Activity → 前台恢复正确画面与输入 | 常用状态 P95 ≤ 1 秒；追踪重新定位等待单独记录 |
| 首次权限/扫描 | 自动初始化、权限界面、人工扫描、扫描后处理分别计时 | 人工操作不纳入自动初始化门槛；扫描确认结束后的处理 P95 ≤ 5 秒为初始目标 |
| UI/交互反馈 | 点击/抓取 → 首个可见反馈 | 常用 UI 建议 ≤ 100 ms；抓取需同时验证跟手，避免只看按钮反馈 |

如果复杂存档无法达到上述目标，应输出耗时与内容容量的关系，并决定优化加载或明确容量限制，不能把超时调整成验收通过。

### 4.2 渲染与持续负载

| 实际刷新率 | 理论每帧间隔 | 建议活跃 CPU/GPU 工作量 P95 上限，按约 20% 余量 |
|---|---|---|
| 72 Hz | 13.89 ms | 11.1 ms |
| 90 Hz | 11.11 ms | 8.9 ms |
| 120 Hz | 8.33 ms | 6.7 ms |

门槛应用于关键 CPU 路径与 GPU 工作时长，不包括为帧节奏而等待的时间。CPU/GPU 常有重叠，不能简单将两者相加，也不能把 `WaitForPresent` 一律判成 GPU 耗时。还要结合实际提交节奏、掉帧与合成器数据判断。

- 默认与常用负载：真实应用提交帧应跟上选定刷新率；建议漏帧比例 ≤ 1%，P99 活跃工作时长不超过理论间隔。
- 常用交互：建议不出现应用导致的 > 50 ms 连续画面停顿；保留超过两倍帧间隔、50 ms、100 ms 的尖峰数量与持续时间。首次加载独立统计。
- 开机与 60 分钟后使用相同内容、画质和分辨率比较：建议活跃帧时间 P95 退化 ≤ 10%，同时仍满足原定帧预算。
- 自适应分辨率或重投影保持的表面 FPS 不能掩盖真实降级；同时记录眼纹理、实际应用帧率、重投影/丢帧率与画面质量。

### 4.3 内存与泄漏

PSS 按比例计入共享页，RSS 计入全部驻留共享页；二者不是同一个指标。Unity C# 管理堆、Unity 原生对象、Android Java 堆、图形与 SDK 分配需要分别观察；不能将 `getMemoryClass` 当作 IL2CPP/原生/GPU 总进程内存上限。<a href="#source-2">[cite:2]</a><a href="#source-4">[cite:4]</a><a href="#source-5">[cite:5]</a>

| 指标 | 建议规则 |
|---|---|
| 进程预算 B | 由厂商应用预算、真机系统/MR 常驻负载和内存压力实验确定应用可接受的峰值 PSS 预算；无 B 时不能签核容量 |
| 常用稳定内存 | 建议稳定 PSS ≤ 0.8 B，给瞬时加载和玩法分配留余量；同时检查系统 `MemAvailable`、交换/zRAM 与低内存事件 |
| 峰值 | 启动、换装、切割、视频准备、存档恢复均不得突破 B；1 秒甚至 5 秒采样可能漏峰值，需短窗高频计数或跟踪补充 |
| 同状态残留 | 全功能预热后，反复创建/使用/回收，动态 Mesh/RT/临时 Material、事件监听和请求数量不应随轮次持续净增 |
| 泄漏排查触发 | 建议同状态 60 分钟净增超过 `max(30 MiB, 预热后基线的 5%)`，或持续增长超过 1 MiB/分钟，触发定位；小但稳定的每轮净增同样需要定位 |
| GC | 常驻核心脚本以无持续每帧分配为目标；事件型分配允许，但必须有界并且不造成超预算停顿 |
| 资源生命周期 | 关闭功能后允许保留可复用有界缓存；回收所属实例/退出会话后，其私有运行时资源应释放，不应靠杀进程掩盖 |

“净增触发值”用于筛查，不是泄漏证明，也不是其以下全部合格。Unity 堆扩张后可能保留未使用空间，GC 也不负责释放所有原生资源；需要结合对象数量和引用关系判断。<a href="#source-5">[cite:5]</a>

## 5. 冷启动、首次启动与生命周期清单

每类启动先做 5 次摸底；正式冷/热启动建议各 30 次，报告 P50/P95/最大值与成功率；条件允许时增至 50 次。清数据/首次扫描至少 5 次，保留每次原始时间，不把小样本 P95 当稳定结论。区分过程状态：进程不存在的启动、进程存在但 Activity 重建、进程和 Activity 都仍存在的前台恢复。<a href="#source-3">[cite:3]</a>

| ID / 优先级 | 测试操作 | 必须记录 | 判定重点 |
|---|---|---|---|
| ST01 / P0 | 已有房间、固定常用存档；force-stop 后重新启动，确认旧 PID 已消失 | 新 PID、TTID、XR 首帧、READY、首个可交互帧、分段耗时、峰值 PSS | 常规冷启动达标；不能称为“磁盘缓存已清空” |
| ST02 / P0 | 设备重启后等待系统稳定，再启动相同存档；与 ST01 配对比较 | 开机后等待时间、系统负载、首次/后续启动耗时 | 判断 OS 页缓存、系统服务初始化带来的差异；重启也不是所有持久化缓存清空 |
| ST03 / P0 | 首次安装或清应用数据，保留已经扫描的系统房间；首次启动并授权 | 版本、权限初始状态、空存档、各阶段时间、新增文件及大小 | 隔离应用首次使用成本；系统房间数据不一定随应用清数据被删除 |
| ST04 / P0 | 使用明确无房间数据的测试空间/系统状态，执行首次 Room Capture | 授权等待、人工扫描、确认完成 → READY 的处理、状态变化、峰值 | 人工时间独立；不存在无限等候、黑屏或阻断提示缺失 |
| ST05 / P0 | 同一个房间分别恢复 0、10、30、50 个合法可放置物品；再测候选支持上限 | 存档字节数、锚点加载、Instantiate、碰撞/切块重建、总恢复时间 | 得到容量与启动耗时曲线；物品种类配比固定，不能只堆最便宜物品 |
| ST06 / P1 | 权限拒绝、扫描取消、定位失败、存档损坏、恢复期间退出 | 结束状态、错误提示、重复请求数、退出后资源 | 清楚结束或可恢复；失败不应永久占用资源，不以错误状态 READY 冒充成功 |
| ST07 / P0 | Home → 应用前台，短暂离开和离开 5 分钟分别重复 | PID/Activity 是否保留、恢复时间、追踪、音视频、输入、内存 | 热恢复无重复装配、无额外 Mesh/RT、状态正确 |
| ST08 / P0 | 对话进行时开关系统键盘、系统菜单；摘戴头显、休眠/唤醒，共 30 轮 | focus/pause 顺序、请求终态、恢复帧时间、SDK/媒体实例数 | 键盘失焦不误取消对话；恢复无多个 Manager/输入监听/音源重复 |
| ST09 / P0 | 应用进入后台后发生系统内存回收，再从图标进入；另测覆盖升级保留存档 | 退出原因、PID 改变、存档正确性、冷启动耗时、旧缓存版本 | 实际重建路径可恢复；force-stop 与真实系统回收分开记录 |

`MATESOUL_READY` 可作业务就绪近似标记，但其输出不保证对应内容帧已经显示，也不等于 Android 已调用 `reportFullyDrawn()`。当前所查业务代码没有发现该调用；正式 TTFD 可在就绪内容帧之后接入 Android 报告，或采用明确命名的自定义可交互指标。

## 6. 首次使用与缓存清单

“缓存”至少分四层：系统页缓存、应用磁盘缓存、进程内已加载资源/对象池、GPU/驱动 Shader 与 Pipeline 缓存。清应用数据、杀进程、设备重启、清 Unity Library 的影响各不相同。Unity Library 属于开发机资产导入/构建缓存，不是玩家设备启动缓存。

| ID / 优先级 | 测试操作 | 必须记录 | 判定重点 |
|---|---|---|---|
| CA01 / P0 | 记录首次启动前后应用私有目录、cache/code_cache、persistentDataPath 的文件清单与大小；连续启动 10 次再比 | 文件大小、mtime、创建次数、读写字节与耗时；系统房间/锚点另记 | 哪些缓存真实存在、何时生成、是否被复用；不能推测存在未实现的缓存 |
| CA02 / P0 | 新进程首次显示冷月、每种服装、全部物品类别与高亮材质；再做同样第二轮 | 首次/二次尖峰、资源加载、Shader/Pipeline 建立、Mesh/Texture 上传、GPU 等待 | 无隐藏的首次卡顿；预热成本和常驻内存分别计入，不用盲目预热全部资源 |
| CA03 / P0 | 第一次开电视、第一次开收音机、第一次切换频道、第一次水壶/烹饪蒸汽 | 解码准备、首个音视频帧、首个粒子、临时缓冲与尖峰 | 首次延迟与后续延迟分别达标；缺文件/弱网进入失败路径时也释放资源 |
| CA04 / P0 | 常用汉字 → 固定非常用汉字集合 → 同一集合重复显示；再使用新字集合 | TMP 图集数量、格式/字节、字形生成 CPU、Texture2D 数量、峰值 | 相同字集合不反复扩图集；新字增长是内容缓存，必须形成明确上限或产品容量策略 |
| CA05 / P1 | 清应用 cache、保留存档与权限的测试变体；再测清全部应用数据变体 | 清除范围、启动/首次功能耗时、重新生成文件 | 两种操作不能混写成“无缓存”；系统/驱动缓存无法确认清除时注明 |
| CA06 / P1 | 覆盖升级应用；有旧存档/旧缓存及剩余存储不足的独立场景 | 缓存失效/迁移、重建峰值、写入失败、残留 `.tmp` | 无无限重建/重复下载/旧资源引用；存档不被当可删除缓存 |

字体字形和对象池等缓存可能只在进程内复用，重启后再生成并不自动表示缓存失效。首先确认设计，再判断行为。

## 7. 内存占用清单

内存检查使用三个视角：Android 进程与系统，Unity 对象/管理堆，GPU/媒体/SDK 资源。三套数值有覆盖与遗漏，不能直接相加为“实际总内存”。Graphics 一栏可能无法完整反映共享图形缓冲；需要设备工具与全系统压力交叉判断。<a href="#source-2">[cite:2]</a><a href="#source-4">[cite:4]</a>

| ID / 优先级 | 测试操作 | 必须记录 | 判定重点 |
|---|---|---|---|
| ME01 / P0 | 同一真机记录系统空闲 → 启动 → READY → 全功能预热 → 常用游玩 → 空场景 | PSS、RSS、Private Dirty、SwapPss/zRAM、MemAvailable；Unity used/reserved、管理堆、原生对象 | 建立统一基线和 B；仅看 Mono/GC 不足以判断 Android 能否承受 |
| ME02 / P0 | 对首次启动、角色启用、换装、最大存档恢复、视频 Prepare、密集切割抓短窗 | 各阶段峰值及持续时间、同时在场资源、副本数 | 峰值不突破 B；低频 PSS 采样不能当作峰值无异常证据 |
| ME03 / P0 | Unity Memory Profiler 抓预热后、最大负载、全部回收后快照，比较 Top 20 | Mesh、Texture2D、RenderTexture、Material、AnimationClip、AudioClip、对象/引用链 | 区分大资源常驻、重复加载、私有副本与未释放；快照产生的停顿排除出帧率基线 |
| ME04 / P0 | 角色近景/远景、换装与关闭不需要的服装，检查 LOD 和资产常驻 | 活跃 renderer/LOD、顶点/三角形、骨骼/ShapeKey、所有 LOD 内存、贴图真实格式、可读副本 | LOD 降低绘制不保证释放未显示资源；静态 RGBA 图集和大纹理优先核查 |
| ME05 / P0 | 静态 Mesh 与动态深度运行、电视播放、收音机交叉淡化单独与组合测 | Unity 内/外原生分配、图形缓冲、解码缓存、PSS/系统差值 | 找出 Unity 统计之外的 SDK/驱动/媒体成本；功能关闭与所属实例销毁分别测 |
| ME06 / P0 | 在测试机逐步引入已知驻留应用/受控内存压力，覆盖前台与后台 | 系统低内存/trim、LMKD/退出原因、恢复状态、持续帧时间 | 不发生前台内存崩溃；后台被系统回收后的重启正确；`send-trim-memory` 仅测通知，不等于真实压力 |

不要为“看起来内存下降”而在正常游玩中循环调用 `GC.Collect` 或 `Resources.UnloadUnusedAssets`。可在独立诊断实验使用一次受控回收判断引用是否仍存活，但要另列其停顿，不能用它替代正常生命周期。<a href="#source-5">[cite:5]</a>

## 8. 内存泄漏与资源残留清单

统一循环方法：固定内容全功能预热 → 返回共同基线 → 做一批操作 → 全部回收/结束 → 等待粒子消散、碎片生命周期、网络终态和延迟销毁 → 回到同一基线采样。每批建议 10 轮，共 10 批；至少完整重复两次。每轮内容与最终存档一致，PID 全程保持不变。

共同基线可以保留约定的常驻角色、UI 和共享目录资源，不能要求每次返回到刚进程启动时的占用。也不能将玩家主动保存更多物品、未回收碎片或新增字形误判成泄漏。

| ID / 优先级 | 重复操作 | 重点计数 / 证据 | 通过条件 |
|---|---|---|---|
| LK01 / P0 | 同一组物品放置 → 使用 → 回收，100 轮 | Registry、GameObject、Collider/Rigidbody、临时 Material、PSS/原生对象 | 回归同状态后动态对象不持续净增；静态 Prefab 资源允许常驻 |
| LK02 / P0 | 苹果/胡萝卜切到合法深度 → 回收全部切块，包含失败与取消 | 视觉 Mesh、碰撞 Mesh、`Physics.BakeMesh` 相关原生分配、候选对象、引用链 | 成功/失败/回滚路径都释放私有 Mesh，无每刀/每轮净增 |
| LK03 / P0 | 摔碎物品 → 等待完整生命周期 → 回收残留；每批一致 | Debris、Fragment、生成 Mesh、音源、碰撞体 | 生命周期结束后恢复基线；自然仍存活的碎片不计成泄漏 |
| LK04 / P0 | 咖啡罐放置 → 倒豆/研磨/冲泡 → 回收罐、盖、豆与杯 | 池 Root、物理豆数量、所属关系、实例化数组/对象 | 同一个罐的池达到配置上限后稳定；回收罐释放所属池，多罐总池数量可解释 |
| LK05 / P0 | 电视开关/换台 100 次，再回收电视；收音机同样测试 | VideoPlayer、MediaCodec/缓冲、RT、AudioSource、AudioClip、事件 | 关闭后的有界缓存可解释；销毁后私有 RT 与媒体实例不持续残留 |
| LK06 / P0 | 换装、动作抢占/取消、牵手、坐/躺/起身、小游戏进出 100 轮 | 私有 Mesh/Material、Rig/Action、事件监听、协程/Task、控制资源 | 结束后临时对象与引用恢复；无旧动作/Rig/事件持续执行 |
| LK07 / P0 | 100 次对话，覆盖成功、失败、超时、主动取消、键盘失焦；含长回复与固定字集合 | 活跃请求/handler、CTS、Task、历史条数、字符串、图集、内存 | 请求终态后释放；历史保持 16 条上限；相同字集合趋稳；不能只检查 HTTP 200 |
| LK08 / P0 | 已支持的房间更新/重建路径、空间 Mesh 同 UUID 更新/移除、暂停/恢复；在诊断版本控制输入 | Mesh UUID 与块数量、渲染/碰撞副本、NavMeshData、事件订阅、原生分配 | 旧 Mesh/导航数据释放；同 UUID 更新不无限积累；没有产品重建入口时走诊断路径并标明 |
| LK09 / P0 | 失焦/前后台/摘戴/休眠循环；2 小时固定负载，再回到共同基线 | PSS 时间线、动态对象净增、Manager、音视频/追踪实例、FD/线程 | 不随循环增加；若仅模拟器宿主增长，需分层定位，不能据此宣布 APK 泄漏 |

### 8.1 泄漏结论的证据要求

1. 画出原始 PSS/原生 used/对象数量时间线，标出每批操作与清理结束；提供每批“同状态”的数值，不能只用运行时最大值。
2. 对比预热后与第 5/10 批清理后的快照，找出持续增加的资源类型及持有者。管理对象看引用链；Unity 对象看运行时私有资源的拥有者；SDK/驱动分配需要原生/厂商工具补充。
3. 对固定轮数计算 MiB/轮；对固定负载计算 MiB/分钟。报告趋势是否重现及可解释缓存项；热变化、共享页变化、真实内容增长单列。
4. 对已经缩小的链路禁用一个候选功能或修复一个生命周期，再按原步骤复测。资源计数、内存趋势、峰值和帧时间同时闭环。
5. 杀进程后下降只能说明属于该进程/关联资源，不能区分合法缓存与泄漏。看不到增长只说明当前时长/路径未观察到泄漏，不能证明所有路径永远无泄漏。

Android 10+ 的 Perfetto heapprofd 可分析采样到的原生分配与未释放栈；生产 user 系统通常要求应用 debuggable 或 profileable。它不是全部 GPU/SDK 分配的总账，也不能仅凭 Java heap dump 分析 IL2CPP C# 对象。<a href="#source-6">[cite:6]</a>

## 9. CPU、GPU、帧节奏与 MR 清单

每个固定场景先预热，再采集至少 3 分钟，重复 3 次；常用与最大支持容量各测一次冷机及热稳态。短时 Unity Profiler 用于定位，Release 长稳用于验收。

| ID / 优先级 | 测试操作 | 必须记录 | 判定重点 |
|---|---|---|---|
| FR01 / P0 | 角色空闲 → 行走/跑步 → 连续转身 → 近景对话/注视 → 持物/坐躺/换装 | Main/Render thread 活跃时间、Animator/IK、Spring Bone、BlendShape、GC、GPU | 拆出角色本身成本；保持脸部、服装与动作质量 |
| FR02 / P0 | 同视角、同物品分布分别测空场、常用场、最大支持场 | 应用帧时间 P50/P95/P99、提交/掉帧、draw call、SetPass、可见面数、CPU/GPU | 性能容量由真实热点决定，不使用单一通用三角形预算签核 |
| FR03 / P0 | 当前 MultiPass 基线与兼容的 Multiview 对照 APK | CPU/GPU、眼纹理、实际模式、左右眼的 Toon/UI/深度/透明/实例化正确性 | 必须双眼图像与 MR 功能无回归；失败不能算性能优化 |
| FR04 / P0 | 同内容比较实际默认档、Balanced、Performant；再逐项测 MSAA/阴影/分辨率 | 实际档位、阴影 Pass、RT、GPU 活跃时间、带宽相关计数、画质 | 每次只改一项；URP 的有效配置优先于传统 Quality 面板数值 |
| FR05 / P0 | 原生环境深度、URP 深度合成、静态空间 Mesh 分别有/无的诊断对照 | 每个 Pass GPU、网格数/上传时间、系统 SDK 成本、遮挡延迟、CPU 分配 | 最终默认版本保留所需功能；禁用只是归因，不作为真机功能无损结论 |
| FR06 / P0 | 人/手/现实物体在前景移动，头部快转；进入复杂扫描区域 | 静态 Mesh 更新尖峰、主线程上传、深度重投影/合成、掉帧与错位 | 同时满足帧预算与遮挡体验；模拟器禁用动态深度路径，必须真机执行 |
| FR07 / P0 | UI 展开/隐藏、目录翻页、回收整物高亮、长字幕、新字形、粒子近景、电视近景 | Canvas/TMP 重建、GC、透明 overdraw、画面覆盖率、GPU/CPU | 正常局部高亮与回收模式整物高亮分别测；近景透明覆盖也可能成为瓶颈 |
| FR08 / P1 | 在设备支持下比较注视点渲染/自适应分辨率与固定基线，覆盖眼/手/控制器模式 | 真实刷新率、应用帧、眼纹理最低值、清晰度、Tracking、重投影 | 记录降分辨率幅度；不把合成器重复旧帧当应用达到目标 FPS |

普通 Android `dumpsys gfxinfo framestats` 主要面向 View UI；Unity/XR 的独立渲染 Surface 与合成链路可能未被它完整反映。它可以辅助观察 Android 界面，游戏帧率主要由 PICO/Unity/GPU 工具确认，不以“gfxinfo 无 jank”签核。

## 10. 玩法容量与短时尖峰清单

容量测试按 0 → 10 → 30 → 50 → 更高候选上限推进；每档包含固定种类配比，停止条件为超帧预算、突破内存预算或明显交互恶化。库存有限的物品保持合法数量，不能借压测破坏产品库存合同。每档记录“可见物品、活跃刚体、睡眠刚体、碰撞体、碎片、Mesh 块数”。

| ID / 优先级 | 操作 | 重点指标 | 判定重点 |
|---|---|---|---|
| PO01 / P0 | 多家具静止、移动家具、角色绕行；比较刚体清醒与睡眠状态 | Physics.Simulate、接触对、FixedUpdate 次数、NavMesh/carving/路径耗时 | 静止物品不持续制造昂贵接触；卡顿后的物理补帧不能形成恶性循环 |
| PO02 / P0 | 最大合法咖啡罐数同时倾倒、落桌/地、接收与研磨 | 每罐实际物理豆上限、全部池/清醒豆、实例化 draw、碰撞与分配 | 用实际物理豆而不是 720 视觉豆估算；多池聚合仍在预算内 |
| PO03 / P0 | 多批切割、最大合法切割深度、失败切割、连续回收 | 每刀时长、BakeMesh、三角形/切块数、GC/原生峰值、存档耗时 | 尖峰不影响正常跟头/跟手；达到内容上限有明确停止条件 |
| PO04 / P0 | 密集摔碎/散落物品，然后等待清理 | 每次碎裂、最大活跃碎片、接触对、音效并发、清理后资源 | 明确碎片容量/生命周期；无长时间不眠物理堆积 |
| PO05 / P0 | 保龄球开局、击球、重置/复位、退出，100 回合 | 碰撞高峰、复位瞬时分配、重复启动/复位、结束后球/瓶/代理数 | 复位物理锁定与自碰撞过滤保持正确；不能为性能省略必要逻辑 |
| PO06 / P0 | 平底锅烹饪、开水壶、饮水、倒液/粉、蒸汽，合法并发负载 | 每控制器 CPU、射线/碰撞、粒子/透明、池/水迹、事件频率 | 视效随停止/回收正确消散，无逐滴刚体或事件重复导致增长 |
| PO07 / P0 | 电视播放 + 收音机 + 烹饪 + 角色互动 + MR 深度 + 常用物品 | 帧预算、PSS 峰值、温度、频率、媒体卡顿、请求时的尖峰 | 综合常用场景达标；单功能通过不能代替并发 |
| PO08 / P0 | 大存档下移动/放置/回收触发 Save；正常、慢、失败与长响应 AI | JSON/文件替换时长与次数、上下文采集、UTF-8 字节、解析/UI 更新、请求终态 | 存档不反复写满磁盘；本地工作不阻断帧；服务端 LLM 耗时独立报告 |

AI 的上下文摘要有条目上限，但物品遍历、目标列表和序列化成本仍需按容量测。上下文采集耗时不必覆盖后续 JSON/HTTP 编码，必须完整拆段；`payloadBytes` 等日志也需核对是否真的按 UTF-8 字节计算。

## 11. 热稳定、功耗与系统压力清单

| ID / 优先级 | 操作 | 记录 | 判定重点 |
|---|---|---|---|
| SO01 / P0 | 固定综合场景连续 60 分钟，重复固定操作脚本 | 0/15/30/60 分钟帧时间、PSS、CPU/GPU 频率、温度、thermal 状态、画质 | 热稳态仍在帧预算内；不能依靠持续最高频率假定设备总能维持 |
| SO02 / P0 | 同内容连续 2 小时；常驻陪伴需求另测 8 小时 | 内存残留、对象数、GC、媒体循环、存档写入、崩溃/ANR/退出原因 | 无随时长持续增长、冻结或资源耗尽；8 小时是加测，不替代 2 小时门禁 |
| SO03 / P0 | 不充电、电量 20% 左右和充电三种条件独立测试 | 温度/thermal、频率、帧时间、电流/电量变化、充电状态 | 低电量和充电行为仍可接受；不把充电电流当纯应用功耗 |
| SO04 / P1 | 相同温度/亮度/刷新率下，比较系统 MR 基线和游戏综合场景 | 电量消耗 %/小时、条件允许时平均功率、电压/电流、系统工作 | 得出续航预算与额外负载；系统传感器读数能用时才计算功率，不虚构测量 |
| SO05 / P0 | 后台驻留、其他系统界面占用、真实低内存状态与恢复 | 系统 MemAvailable/zRAM/回收、进程退出原因、追踪/深度恢复 | 不将后台保活当硬要求；重点是资源收敛与重启/恢复正确 |

温度阈值按目标设备厂商 thermal policy 与安全规范确定，不设一个适用于所有头显的统一摄氏度。ADB/PDC 连线、投屏、录屏和 Profiler 对发热与功耗有影响，正式基线需记录或关闭。

## 12. 安装包、资源与磁盘清单

| ID / 优先级 | 操作 | 记录 | 判定重点 |
|---|---|---|---|
| PK01 / P1 | 用 Build Report/APK 分析包内资源与安装后占用 | APK/安装大小、Top 20 资源、首启后数据、视频/音频、Resources/预加载引用 | 只看实际构建依赖，不以仓库 ArtSource 大小推断手机内存；剔除不影响运行的开发冗余 |
| PK02 / P0 | 核对运行时实际纹理/网格导入设置 | Android 压缩格式、最大尺寸、Mipmap、Read/Write、重复贴图/模型、全部 LOD 驻留 | ASTC 等选择以设备/画质支持为准；必须测解码/驻留，不把 PNG 文件大小当 RAM |
| PK03 / P1 | 核对音频导入与电视节目文件加载方式 | Streaming/压缩/解压、预加载、并发音源、媒体临时文件、关闭后驻留 | 长音频与交叉淡化的成本有界；打包与外部文件供应方式分别可用 |
| PK04 / P0 | 剩余存储不足、保存中断、升级后首次启动与多次开关 | 数据/cache/code_cache/`.tmp` 大小曲线、写入失败、读写尖峰 | 不覆盖有效存档、不无限增长缓存；失败明确处理，不形成重试洪泛 |

## 13. 采集工具与可以直接复用的命令

### 13.1 工具分工

| 工具 | 使用场景 | 边界 |
|---|---|---|
| ADB / dumpsys / Logcat | Release 进程内存、系统压力、启动、生命周期、错误 | 不直接给出全部 Unity 对象/图形资源归属；各 OS 的服务和字段可能不同 |
| PICO Developer Center / Metrics HUD | 实际 FPS、眼纹理、PSS、频率、温度、刷新率、系统性能 | 官方页面覆盖的是其对应 SDK/工具版本；当前终端只采集实际可提供的字段，不照抄旧设备 CPU 核分配 |
| Unity CPU/Rendering/Physics/Audio Profiler | Development 真机短窗定位、GC 分配与慢函数 | 有工具开销；不开全程 Deep Profiling；Unity GPU 计时是否可用依图形 API/设备而定 |
| Unity Memory Profiler | 对象快照、比较、重复资源、管理引用链 | 当前 manifest 未声明此包；测试阶段选兼容 Unity 2022.3 的版本按需安装；快照开销另测 |
| Perfetto | 启动、线程调度、频率、IO、内存趋势；支持时用 heapprofd | PICO OS 的数据源/权限先核查；原生栈需匹配 APK 的符号/Build ID |
| PICO Graphics Probe / 设备兼容 GPU 工具 | GPU Pass、overdraw、带宽、着色与渲染热点 | 先核对目标芯片、驱动、系统与图形 API 支持；不能保证所有计数器可用 |

官方工具文档见来源 1–7。测试授权范围内按需配置 profileable/诊断 APK，不在正式版本中为了拿数据随意改变后端、核心功能或设备限制。

### 13.2 设置 ADB 与目标设备

以下命令是执行时参考，本清单编制期间没有运行设备测试。先运行 `devices -l`，再把 `PERF_SERIAL` 换成目标真机序列号。当前工程包名是模板名；每次换 APK 必须用 Manifest/包信息重新核查，不仅凭文件名判断版本。

```bash
PERF_ADB='/Users/bytedance/MateSoul/.pico-home/6.2/emulator/system-images/platform-tools/adb'
"$PERF_ADB" devices -l

PERF_SERIAL='TARGET_DEVICE_SERIAL'
PERF_PKG='com.UnityTechnologies.com.unity.template.urpblank'
PERF_COMPONENT="$("$PERF_ADB" -s "$PERF_SERIAL" shell cmd package resolve-activity --brief \
  -a android.intent.action.MAIN -c android.intent.category.LAUNCHER "$PERF_PKG" | tr -d '\r' | tail -n 1)"
printf '%s\n' "$PERF_COMPONENT"
```

必须确认输出是 `包名/Activity`，然后再执行启动。如果本地 bundled ADB 不存在，使用已安装的 Android SDK `platform-tools/adb`。命令所在的包可能改变，不能仅凭上方当前默认值处理其他应用。

### 13.3 设备与构建信息

```bash
"$PERF_ADB" -s "$PERF_SERIAL" shell getprop ro.product.model
"$PERF_ADB" -s "$PERF_SERIAL" shell getprop ro.soc.model
"$PERF_ADB" -s "$PERF_SERIAL" shell getprop ro.build.version.release
"$PERF_ADB" -s "$PERF_SERIAL" shell getprop ro.build.fingerprint
"$PERF_ADB" -s "$PERF_SERIAL" shell cat /proc/meminfo
"$PERF_ADB" -s "$PERF_SERIAL" shell dumpsys package "$PERF_PKG"
shasum -a 256 '/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk'
```

### 13.4 常规冷启动

先确认旧进程退出；启动过程中保留完整系统和 Unity 日志。

```bash
"$PERF_ADB" -s "$PERF_SERIAL" shell am force-stop "$PERF_PKG"
"$PERF_ADB" -s "$PERF_SERIAL" shell pidof "$PERF_PKG"
"$PERF_ADB" -s "$PERF_SERIAL" shell am start -W \
  -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -n "$PERF_COMPONENT"
"$PERF_ADB" -s "$PERF_SERIAL" logcat -d -v monotonic \
  ActivityTaskManager:I ActivityManager:I Unity:I '*:S'
```

`ThisTime/TotalTime/WaitTime` 分别保留，主要辅助 TTID，不能当 `MATESOUL_READY` 或可交互时间。采集启动 Intent、业务标记与首个内容帧时，优先统一设备单调时钟；不同主机/设备壁钟日志不能直接相减。

不要在轮次中无意清权限、存档、系统房间数据。需要干净应用状态时，下面命令只在专用测试设备、已备份的状态下使用；会删除应用数据和存档，且不保证清掉 PICO 系统房间/驱动缓存：

```bash
"$PERF_ADB" -s "$PERF_SERIAL" shell pm clear "$PERF_PKG"
```

### 13.5 进程内存与系统状态

```bash
"$PERF_ADB" -s "$PERF_SERIAL" shell dumpsys -t 5 meminfo "$PERF_PKG"
"$PERF_ADB" -s "$PERF_SERIAL" shell dumpsys -t 5 meminfo
"$PERF_ADB" -s "$PERF_SERIAL" shell cat /proc/meminfo
"$PERF_ADB" -s "$PERF_SERIAL" shell dumpsys -t 5 thermalservice
"$PERF_ADB" -s "$PERF_SERIAL" shell dumpsys -t 5 battery
"$PERF_ADB" -s "$PERF_SERIAL" shell df -h /data
```

长稳建议进程 PSS 每 5–10 秒一次、系统/thermal 每 30 秒一次；采样工具本身开销需做开/关对照。`dumpsys meminfo` 较重，不建议靠每帧调用测峰值。高频短窗使用 Unity 计数器、Perfetto/厂商监测，或受权限允许的轻量 RSS；不能把 RSS 误标成 PSS。

支持的 Android 版本可补充退出原因：

```bash
"$PERF_ADB" -s "$PERF_SERIAL" shell dumpsys activity exit-info "$PERF_PKG"
"$PERF_ADB" -s "$PERF_SERIAL" logcat -d -b crash -v threadtime
```

如果字段不存在或权限拒绝，记录工具缺口，改用厂商日志/PDC/bugreport，不能把空输出解释成没有崩溃或退出。

### 13.6 缓存与数据目录

`run-as` 通常要求应用 debuggable；Release 若被拒绝，使用诊断 APK 的相同存档/内容或应用内诊断导出，明确这种测量的构建差异。

```bash
"$PERF_ADB" -s "$PERF_SERIAL" shell run-as "$PERF_PKG" du -k cache code_cache files
"$PERF_ADB" -s "$PERF_SERIAL" shell run-as "$PERF_PKG" ls -lR cache code_cache files
"$PERF_ADB" -s "$PERF_SERIAL" shell du -k "/sdcard/Android/data/$PERF_PKG/files"
"$PERF_ADB" -s "$PERF_SERIAL" shell ls -lR "/sdcard/Android/data/$PERF_PKG/files"
```

这些目录只是候选位置，`Application.persistentDataPath` 和媒体加载目录以运行时实际值为准；Android scoped storage 也可能限制 ADB 可见范围。扫描文件内容没有必要时仅记录路径、大小与时间。

### 13.7 长任务执行纪律

- 每次采集有固定截止时间，启动命令和 ADB 子命令设超时，长稳采集每 30–60 秒报告一次存活/样本进度。
- 超时后先保留部分数据，确认目标采集进程已停止；只允许有限重试，不反复重跑同一失败命令。
- 游戏崩溃导致 PID 变化时终止该轮泄漏趋势分析，记录退出原因，再开始新轮；不能将重启后的低占用混入连续曲线。
- 对长稳性能基线不全程录屏；只有定位问题时抓短片，并保持工具条件可比。

## 14. 执行顺序、记录与最终验收

### 14.1 建议执行顺序

1. **建立可比基线**：确认目标设备、无一次性调试上报的 Release、实际画质/双目模式/刷新率，以及固定房间和存档。
2. **第一轮风险筛查**：ST01/03/05、ME01/02、FR01/02/05、PO07 和 SO01。先回答常用玩法在热稳态下能否维持预算。
3. **完整启动与缓存**：覆盖第 5、6 节，区分首次人工扫描、二次自动读取、资产首次使用和存档恢复。
4. **资源生命周期**：第 8 节 100 轮操作与快照对比；如发现增长，按系统 → Unity → 媒体/GPU/SDK 层归因。
5. **容量与低内存**：定义支持上限和 B，完成压力、后台回收与复杂存档启动。
6. **持续验收**：修复后使用相同场景重测，完成 2 小时长稳及必要的 8 小时常驻实验。

无需先覆盖全部 55 项再处理明显问题；P0 中有不可运行或持续超帧预算的问题时，先定位，保留其他独立用例的执行结果。

### 14.2 每条用例的记录字段

| 字段组 | 必填内容 |
|---|---|
| 身份 | case_id、run_id、设备/fingerprint、APK SHA-256、源码版本、构建类型 |
| 条件 | 房间/存档 ID、物品配比/数量、渲染配置、电量、温度、网络、工具状态 |
| 时间 | start/end、采样时钟与单位、PID、每个关键阶段、操作轮次 |
| 结果 | 具体指标、P50/P95/P99/最大值、峰值/基线、净增 MiB/轮、成功/失败/阻塞 |
| 证据 | 日志/trace/快照文件路径、问题复现步骤、错误与退出原因 |
| 判断 | 未执行 / 通过 / 失败 / 阻塞、不适用的理由、问题归属与复测版本 |

建议保存原始样本为 CSV，保留必要 trace、快照与数值报告；大型抓取/分析缓存放测试临时目录，收尾只保留能复现与解释结论的证据。当前文档没有伪造执行结果。

### 14.3 设备适配门禁

- [ ] 目标真机 Release 的常用场景及承诺容量满足实际刷新率帧预算；双眼/MR 画面无回归。
- [ ] 60 分钟后仍满足帧预算，记录所有分辨率、画质或重投影变化。
- [ ] 已有房间/常用存档的自动启动与前台恢复达标；首次授权/人工扫描独立计时。
- [ ] 启动和玩法峰值不超 B，系统仍有足够余量；内存压力/后台回收后可恢复。
- [ ] 固定状态循环无无法解释的资源净增；2 小时内无崩溃、ANR、冻结、FD/线程耗尽。
- [ ] 物品、碎片、物理豆、切块、房间 Mesh 与字体字形的实际容量边界明确。
- [ ] PICO 动态深度、静态遮挡、导航、交互、AI 生命周期与音视频功能完整。

结果应归为：**默认配置可运行 / 在明确配置与容量限制下可运行 / 需要优化后复测 / 目标硬件与功能需求存在冲突**。缺少目标真机数据时保留“尚未验证”，不能仅由工程体量推断不适配。

## 15. 工程证据与官方来源

### 15.1 当前工程证据

| 证据 | 文件 |
|---|---|
| 平台与运行计划 | [AGENTS.md](file:///Users/bytedance/MateSoul/AGENTS.md)、[Demo 开发方案](file:///Users/bytedance/MateSoul/项目沉淀资料/MateSoul_Demo开发方案.md) |
| 包与 Android 配置 | [manifest.json](file:///Users/bytedance/MateSoul/MateSoul/Packages/manifest.json)、[PICO package.json](file:///Users/bytedance/MateSoul/XR/package.json)、[ProjectSettings.asset](file:///Users/bytedance/MateSoul/MateSoul/ProjectSettings/ProjectSettings.asset) |
| XR 与画质 | [PXR_Settings.asset](file:///Users/bytedance/MateSoul/MateSoul/Assets/XR/Settings/PXR_Settings.asset)、[枚举定义](file:///Users/bytedance/MateSoul/XR/Runtime/Scripts/PXR_Settings.cs)、[QualitySettings.asset](file:///Users/bytedance/MateSoul/MateSoul/ProjectSettings/QualitySettings.asset)、[High Fidelity URP](file:///Users/bytedance/MateSoul/MateSoul/Assets/Settings/URP-HighFidelity.asset)、[主场景](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Scenes/MateSoul.unity) |
| 启动与房间 | [GameBootstrap.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameBootstrap.cs)、[RoomMapProviders.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/RoomNavigation/Runtime/RoomMapProviders.cs)、[RuntimeRoomNavigation.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/RoomNavigation/Runtime/RuntimeRoomNavigation.cs) |
| 遮挡与空间网格 | [RealityOcclusionMeshRenderer.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RealityOcclusionMeshRenderer.cs)、[RealityEnvironmentOcclusionFeature.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RealityEnvironmentOcclusionFeature.cs)、[RealityMeshSource.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RealityMeshSource.cs) |
| 角色 | [AvatarVisualDynamics.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/LengyueIntegration/AvatarVisualDynamics.cs)、[角色合并验证记录](file:///Users/bytedance/MateSoul/ArtSource/LocalStudyOnly/Lengyue_ProductionMerge_20261022/production_validation.json) |
| 切割、碎片、物理豆 | [RuntimeCuttable.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RuntimeCuttable.cs)、[BreakableDebris.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/BreakableDebris.cs)、[CoffeeBeanJarController.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CoffeeBeanJarController.cs)、[CoffeeBeanVolumeRenderer.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/CoffeeBeanVolumeRenderer.cs) |
| 媒体与字体 | [TelevisionController.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/TelevisionController.cs)、[RadioController.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/RadioController.cs)、[UiFontAssetBuilder.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/UiFontAssetBuilder.cs)、[GameHud.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs) |
| 存档、AI 与发布 | [GameSession.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameSession.cs)、[JsonGameSaveStore.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/JsonGameSaveStore.cs)、[Conversation.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Gameplay/Conversation.cs)、[HttpLLMProvider.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/HttpLLMProvider.cs)、[GameSession.AIContext.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameSession.AIContext.cs)、[GameSceneBuilder.cs](file:///Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/GameSceneBuilder.cs) |

### 15.2 官方资料

<a id="source-1"></a>
[cite:1] Unity 2022.3 — Profiling your application。真机分析、Development Profiler、Editor 结果限制。
https://docs.unity3d.com/2022.3/Documentation/Manual/profiler-profiling-applications.html

<a id="source-2"></a>
[cite:2] PICO Unity Integration SDK — Monitor device performance。PDC、Metrics HUD 及 FPS、眼纹理、PSS/RSS、温度/频率等字段。页面所示 SDK 版本与本地包版本不同，具体可用字段以设备为准。
https://developer.picoxr.com/document/unity/monitor-device-performance/

<a id="source-3"></a>
[cite:3] Android Developers — App startup time。冷/温/热启动、TTID、TTFD、`reportFullyDrawn` 和 Android vitals 阈值。
https://developer.android.google.cn/topic/performance/vitals/launch-time?hl=en

<a id="source-4"></a>
[cite:4] Android Developers — Overview of memory management。共享页、PSS、Java heap、后台进程回收。
https://developer.android.google.cn/topic/performance/memory-overview?hl=en

<a id="source-5"></a>
[cite:5] Unity 2022.3 — Managed memory。IL2CPP 管理内存、堆保留/碎片、GC 与原生资源、引用残留。
https://docs.unity3d.com/2022.3/Documentation/Manual/performance-managed-memory.html

<a id="source-6"></a>
[cite:6] Perfetto — Memory: Callstack-based Allocation Profiling。heapprofd、Android 版本/权限、原生采样和符号化限制。
https://perfetto.dev/docs/data-sources/native-heap-profiler

<a id="source-7"></a>
[cite:7] Android Developers — dumpsys。meminfo、系统服务、采集命令与 timeout。
https://developer.android.google.cn/tools/dumpsys?hl=en

<a id="source-8"></a>
[cite:8] Unity 2022.3 — Profiler overview。CPU、渲染、内存、Physics、Audio 与扩展分析工具。
https://docs.unity3d.com/2022.3/Documentation/Manual/Profiler.html

所有外部资料于编制日期核查。工具输出示例和阈值均不代表本项目已经测得的数值。
