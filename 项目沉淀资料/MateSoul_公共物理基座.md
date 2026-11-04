# MateSoul 公共物理基座

最后更新：2026-10-10

## 范围与所有权

运行平台为 Unity 2022.3.38f1 / PhysX / PICO Unity Integration SDK。
角色、桌子、椅子、复古电视和陪伴灯共用 Unity 物理世界；PICO 房间网格、地面和
现实障碍物提供静态碰撞几何。保龄球和球瓶目前仍为离线美术资产，尚未加入运行时物品目录。

- `PhysicsBody`：唯一刚体状态入口，处理质量、材质、受力、抓取跟随、暂停、碰撞查询和受控姿态。
- `PhysicsGrabInteractable`：保留 XRI 选择与动态射线附着点，将目标姿态交给 `PhysicsBody`；
  不执行 XRI 自带的 Transform/刚体运动。抓取不改变物体质量，不关闭碰撞。
- `AvatarMotor`：接收动作协调器的导航意图，在 FixedUpdate 中提交有限加速度；
  NavMesh 规划路线，刚体胶囊处理重力、接触、推物和障碍物。
- `GameSession`：物品注册、创建、恢复事务、暂停/恢复、删除、合并存档与销毁。
- `GameItem`：物品状态、抓取事件、稳定姿态检查点、静止后的导航障碍与存档通知。
- `PhysicsContentBuilder`：生成共享物理材质、碰撞代理和项目物理时间配置；
  具体模型差异只存在于内容构建阶段。

业务系统仍通过 `AvatarActionCoordinator` 提交角色动作；不要直接写角色 Transform、
Animator 或 Rigidbody。物品从 `ItemDefinition` 注册并通过 `GameSession` 创建，
避免另建不受会话暂停、存档和销毁管理的物理物品。

## 当前内容参数

尺寸单位为米，质量单位为千克。参数是基于生活经验的产品默认值，不是具体实物测量数据。

| 内容 | 外形尺寸 | 质量 | 单一物理材质 | 静摩擦 / 动摩擦 / 弹性 |
|---|---|---:|---|---|
| 冷月 | 站立胶囊高 1.65、半径 0.25 | 45 | Rubber，近似鞋底接触 | 0.80 / 0.65 / 0.10 |
| 桌子 | 0.60 × 0.40 × 0.75 | 8 | Wood | 0.55 / 0.40 / 0.05 |
| 椅子 | 0.36 × 0.38 × 0.68 | 4.5 | Wood | 0.55 / 0.40 / 0.05 |
| 复古电视 | 机身 0.48 × 0.45 × 0.42，含天线高约 0.566 | 18 | Metal | 0.50 / 0.35 / 0.08 |
| 陪伴灯 | 放置包围尺寸 0.42 × 0.42 × 0.66 | 1.2 | Metal | 0.50 / 0.35 / 0.08 |
| 未单独指定的房间碰撞体 | 采集几何 | 静态 | Room | 0.60 / 0.45 / 0.02 |

每个物品的所有非 Trigger 碰撞体共享同一 `PhysicMaterial`，翻转后不更换材质。
两接触面的摩擦和弹性使用 Average 合并。物品线性阻尼默认 0.02、角阻尼 0.05；
重心和惯性张量由根刚体及组合碰撞体计算。

桌子为桌面加四腿，椅子为座面、椅背加四腿；电视使用机身 BoxCollider，天线不参与
实体碰撞；陪伴灯沿用简化组合碰撞体。视觉网格不直接充当动态高面数碰撞网格。

## 运动与交互

自由物品使用动态刚体，支持重力、冲量、力矩、摩擦、弹跳、碰撞传力和倾倒。
抓取通过有限加速度追随位置与旋转，允许物体被障碍物挡住；松手保留实际求解后的
线速度和角速度，不重新寻地、扶正或瞬移。跟踪丢失、交互器失活、删除和暂停会退出选择。

角色站立/行走为动态刚体，锁定整体翻滚，保持动画角色直立；这不是逐骨骼布娃娃。
坐姿使用较短的躯干胶囊，卧姿使用水平躯干胶囊。这些受控接触姿态使用运动学刚体，
碰撞体持续启用，进入/退出执行分段净空校验。躺床保留原有产品要求的姿态切换；
转移路径按抬升、平移、落位校验，不播放新增上床动画。

起身需要站立空间、地面支撑和退出路径。原出口被挡时检查附近有限候选点；
全部不可用时保持当前接触姿态并报告失败，移开障碍后可通过“起身”或移动请求重试。
角色上下文从真实接触状态读取坐/卧姿态，不因动作失败误报站立。

## 放置、存档与生命周期

1. 桌椅目录放置限定主地面，并使用房间地面高度而不是 NavMesh 的代理高度；
   电视、陪伴灯可放在水平支撑面上。
2. 初次放置会检查占用；异步锚点创建完成后再次检查，避免等待期间区域已被占用。
3. 松手直接恢复自由物理。稳定且有支撑后更新姿态检查点并合并保存；
   导航障碍仅在静止时启用。
4. 持有中的物品保存最后稳定检查点；自由物品保存实际姿态，不保存跨启动的动量。
   `needsGravitySettling` 表示保存时失去支撑的物品，例如刚删除桌子后正在下落的电视。
5. 恢复先创建暂停的整批物品，再验证 ID、定义、有限姿态、穿插和支撑关系。
   支撑顺序与存档排列无关；记录中的下落物品必须能在已知几何上落地，才允许恢复重力。
   未标记的悬空旧姿态、房间外无落点、穿插、重复 ID 或未知定义会撤回整批恢复。
6. 恢复失败保留原存档；放置失败及存储失败撤回本次创建与锚点根状态。
7. 暂停先取消抓取和动作，再冻结角色及物品刚体；恢复还原暂停前速度，不补跑离线时间。
8. 删除立即取消选择并禁用对象，随后释放对象；会话销毁统一回收所有物品。
9. 存档恢复完成前，动作协调器统一拒绝 UI、AI 和自治动作。暂停取消恢复后，
   会话恢复运行时会自动重试，不会永久停留在 `ItemsReady=false`。
10. 恢复旧家具时先忽略尚未安置的角色碰撞体完成布局校验，再把角色放到邻近可导航、
    无穿插且有地面支撑的位置；找不到安全出生点时整批回滚。

新增存档字段兼容 version 1 旧数据，缺省为 false。

## 新物品接入

1. 制作以底部中心为放置原点的米制 Prefab，使用 Box/Sphere/Capsule 或简化凸包组成
   碰撞代理；只允许一个根 Rigidbody，不要在子节点再挂独立 Rigidbody。
2. 创建 `ItemDefinition`，配置实际尺寸、能力、质量及一个共享物理材质。
   使用 `ConfigurePhysics(new BodySettings(kilograms, material), onSurfaces)` 写入物理内容。
3. 在构建或装配时使用 `PhysicsContentBuilder.ConfigureBody` 配置 Prefab；
   将定义加入现有目录。运行时创建、抓取、暂停、存档自动走公共路径。
4. 外部玩法用 `PhysicsBody.AddForce` / `AddTorque` 提交物理作用；普通运动不使用
   `Place`。`Place` 仅用于初始化和已验证的受控姿态切换。

复用位置：

- 运行时代码：`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/`
- 内容构建器：`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Editor/PhysicsContentBuilder.cs`
- 共享材质：`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Physics/`
- 测试：`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Tests/Engine/PhysicsRuntimeTests.cs`

## PICO 性能取舍与验证边界

[PICO 官方设备介绍](https://developer.picoxr.com/document/discover/xr-devices/)公开了
Space Pro 双芯片方案，以及 CPU/GPU 相对 XR2 Gen 2 的性能提升说明；这些数据不能
换算成 Unity 物理毫秒预算。12 ms MR 链路数据也不是应用侧 PhysX 预算。

本版默认 50 Hz 物理步长，单帧最多约三个补步；常态使用离散碰撞，抓取或高速运动才启用
连续碰撞检测。外部冲量在首个物理步前即可启用 CCD。使用休眠、简单组合形体、
局部 8/2 求解迭代、固定容量非分配查询、稳定后合并存档；查询饱和时拒绝未经完整验证的位置。

本轮做基础行为、生命周期、边界和原功能回归，不做压测。
尚未测得 Space Pro 真机 PhysX CPU 耗时、帧率、温度或最大活动刚体数量。
房间碰撞网格仍随现有 `RuntimeRoomNavigation.Build` 装配，本次未新增实时增量重建。
真机跟踪噪声、手感和复杂扫描网格需要后续设备验收。

## 基本验收结果

2026-10-05 完成 Unity 自动化全量回归，95 项全部通过，失败 0、跳过 0。
覆盖重力、质量、摩擦、冲量、力矩、CCD、抓取释放、暂停、存档恢复、支撑关系、
角色推物/墙体阻挡/坡面移动、坐卧接触与退出、HUD 删除和电视实体按钮回归。

Android Development APK 已完成构建：

- 路径：`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- 大小：82,433,205 bytes
- ABI：`arm64-v8a`
- 签名：APK Signature Scheme v2 验证通过
- SHA-256：`76254650afdb2f5d628ddacb774b14f295a20379c338d3a04aa8313d80d4b1f3`

详细证据见
`/Users/bytedance/MateSoul/项目沉淀资料/MateSoul_公共物理基座验证报告.md`。

## 组合物品：独立筷子

目录中的“筷子”是放置入口，不是运行时实体。`ItemDefinition.PlacementParts` 将一次
放置展开为 `chopstick-left` 和 `chopstick-right` 两个隐藏成员定义：

1. `GameSession.PlaceItemAsync` 先按组合整体的占地尺寸寻找空位。
2. 锚点创建和二次占用检查通过后，按局部偏移一次性创建左右两根筷子。
3. 两根筷子各自拥有唯一实例 ID、根 `Rigidbody`、`PhysicsBody`、
   `PhysicsGrabInteractable`、碰撞体和存档记录，可以分别抓取、移动和删除。
4. 任一成员创建或保存失败时，整次组合放置回滚，不留下半套筷子。
5. 旧存档中的 `definitionId=chopsticks` 仍使用组合定义自带的单根预览 Prefab 恢复，
   不因定义升级导致整批存档失败；新放置只会保存左右成员 ID。

组合入口及成员定义位于：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/Chopsticks.asset`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/ChopstickLeft.asset`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/ChopstickRight.asset`

新增组合物品时必须把成员定义一并注册到 `GameSession`，但成员保持
`CatalogVisible=false`。不要给组合入口 Prefab 增加多个刚体，也不要在运行时把两个
独立物品重新挂到同一刚体层级。

## 易碎品碰撞破碎

`ItemDefinition.Breakable` 是内容级开关；未启用的物品完全不挂载破碎行为。当前参数：

| 物品 | 轻度阈值 | 重度阈值 | 轻度碎片 | 重度碎片 | 碎片寿命 |
|---|---:|---:|---:|---:|---:|
| 盘子 | 45 N | 95 N | 4 | 22 | 20 s |
| 马克杯 | 38 N | 80 N | 6 | 22 | 20 s |
| 碗 | 35 N | 72 N | 4 | 12 | 20 s |

判定只发生在 `OnCollisionEnter`，碰撞力代理为
`collision.impulse.magnitude / Time.fixedDeltaTime`。达到轻度阈值但未达到重度阈值时
生成少量大碎片；达到重度阈值时生成更多小碎片。创建后有 `0.25 s` 装配保护期，避免
放置瞬间接触被误判为撞击。

以下状态一律禁止破碎：

- `GameItem.IsHeld`：玩家仍在抓取；
- `PhysicsBody.IsFollowing`：抓取跟随尚未结束；
- `PhysicsBody.IsPaused`：会话暂停；
- `PhysicsBody.IsContact`：受控接触姿态。

因此玩家抓住易碎品甩动不会直接触发破碎；松手后撞击地面、家具或其他碰撞体才会进入
判定。破碎成功后，原物品立即从 `ItemRegistry`、运行时视图和存档中删除。碎片由
`BreakableDebris` 从内容配置的预碎 Prefab 实例化；没有专属资源时才生成凸三棱柱
回退网格。每片仍使用 `PhysicsBody`，继承原速度和角速度，并获得朝碰撞点外侧的
扩散速度。

碎片由 `GameSession` 统一跟踪，参与暂停、恢复和会话销毁；碎片不写入存档，并在寿命
结束后销毁，同时释放运行时 Mesh。不要给碎片另建保存系统或独立暂停状态机。

## 2026-10-08 增量验收

- `GameRuntimeTests`：26/26，通过组合放置、独立刚体、持有免碎、轻/重碎片数量和
  3 米真实坠落碰撞测试。
- `PhysicsRuntimeTests`：5/5。
- `GripGrabTests`：2/2。
- Release APK：
  `/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- 大小：108,933,754 bytes
- SHA-256：
  `bafc04df36b8827cd6302caee41b8ed5f29f1620c523ab84bf769dce1f685f4a`
- APK v2 签名、`arm64-v8a`、PICO VR 与 Spatial Anchor 元数据通过；宿主和设备
  `base.apk` 哈希一致。
- PICO Emulator 冷启动保持前台并输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=2 itemState=ready navMs=34`。

盘子与马克杯已替换为正式预碎模型；碗仍使用程序化回退。三组阈值仍需在 PICO 真机上
按实际抓取释放速度、地面材质和音画反馈校准。

## 预碎美术资源规范

盘子和马克杯使用参考图驱动的专属模型：

| 变体 | 碎片 | 三角形 | 视觉结构 |
|---|---:|---:|---|
| 盘子轻度 | 4 | 6,900 | 四个不规则大块，保留盘沿与中心图案 |
| 盘子重度 | 22 | 9,900 | 外圈、内圈和中心 Voronoi 碎块 |
| 马克杯轻度 | 6 | 10,828 | 五个杯体大块和一个完整把手 |
| 马克杯重度 | 22 | 13,488 | 20 个分层杯壁/杯底碎块和两个把手段 |

四套模型均为闭合流形，保留原 `UVMap`、角色图案、陶瓷釉面和独立浅色断面材质。
Blender 源文件、可重复构建脚本与数值报告位于：

- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Breakables/source/LavenderCeramicFractures.blend`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Breakables/scripts/build_breakable_assets.py`
- `/Users/bytedance/MateSoul/ArtSource/SapphiArt/Props/Breakables/reports/fracture_model_report.json`

Unity 运行资源位于：

- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/Breakables/`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/PropsSource/Plate/`
- `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/PropsSource/Mug/`

`BreakableSettings.lightDebrisPrefab/heavyDebrisPrefab` 必须成对配置。运行时按网格顶点数
分配碎片质量，每片动态添加凸 `MeshCollider` 和 `PhysicsBody`；未配置 Prefab 的内容
继续走通用回退。不要把碰撞体或 Rigidbody 烘焙进美术 Prefab，也不要让碎片进入存档。

## 2026-10-08 预碎模型验收

- Blender：四套模型碎片数准确，全部闭合流形，非流形边为 0。
- Unity URP：盘子和马克杯轻/重预览均保留原贴图、杯沿/盘沿、把手和断面材质。
- `GameRuntimeTests`：26/26；新增盘子与马克杯预碎网格、凸碰撞体及两级碎片数验证。
- `PhysicsRuntimeTests`：5/5。
- `GripGrabTests`：2/2。
- Release APK：`111,764,624` bytes；SHA-256
  `aa6935cc54fba6f747685277ec51de53879a37666552857d77d64b075399a6ca`。
- 宿主与模拟器 `base.apk` 哈希一致；冷启动输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=0 itemState=ready navMs=33`。

## 易碎品音频反馈

所有启用 `BreakableSettings` 的物品必须配置 `breakSound`。当前盘子、碗、马克杯共享：

`/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Audio/CeramicBreak.mp3`

音频由碎片根节点播放，不挂在即将销毁的原物品上。`AudioSource.Play()` 必须先于
预碎 Prefab 实例化和凸 `MeshCollider` cooking：

- 轻度：Volume `0.72`、Pitch `1.04`。
- 重度：Volume `0.95`、Pitch `0.94`。
- 3D Spatial Blend `1.0`，Logarithmic Rolloff，范围 `0.35-7 m`。
- 会话暂停时与碎片物理一起暂停，恢复时继续；碎片销毁时自动回收。

素材导入为单声道 PCM、Decompress On Load、预加载，约 180 KB 内存。新易碎材质应通过
内容配置复用或替换 Clip，不要在协调器中增加物品名称分支，也不要创建常驻公共播放器。

2026-10-08 验收：

- 盘子、碗、马克杯引用同一 Clip。
- 持有免碎与低于阈值时不创建 AudioSource；碎片构建失败会销毁碎片根节点和音源。
- 轻/重播放参数、空间衰减和暂停生命周期自动化通过。
- APK 包含 `CeramicBreak`，模拟器冷启动无 Unity、FMOD 或 Audio 异常。
- 正式 APK SHA-256：
  `d3a7f25371b255d13ddb85d6a6229c8a9a9ea5480609e84a19edaf9ba0cf6b0b`。

## 2026-10-09 破碎音效时序验收

- `BreakSoundRequestedAt <= FragmentsReadyAt` 已成为运行时测试不变量。
- 盘子轻度/重度/真实坠落的播放请求分别领先碎片完成 `6.7/5.2/0.9 ms`。
- 马克杯轻度/重度的播放请求分别领先碎片完成 `16.0/7.2 ms`。
- 定向破碎时序用例 1/1、`PhysicsRuntimeTests` 5/5、`GripGrabTests` 2/2。
- Release APK：`112,083,919` bytes；SHA-256
  `93220fc7ec7bea1b31dc13324ee83415b4cf9f296896592457e0148722237b3d`。
- 宿主与模拟器设备 APK 哈希一致；应用冷启动后保持前台并输出
  `MATESOUL_READY source=PICO Emulator Spatial Mesh items=3 itemState=ready navMs=66`。

自动化只证明 Unity 播放请求早于碎片就绪。扬声器输出还包含系统混音与硬件缓冲延迟，
最终主观同步必须在 PICO 真机上复核。

## 金属重物碰撞音频

桌子、椅子和复古电视通过 `ItemDefinition.ImpactSound` 显式启用三级碰撞音频。中央
会话不按物品名称判断，新增内容必须在定义资产中注册该能力。

受力代理与易碎品一致：

`collision.impulse.magnitude / Time.fixedDeltaTime`

| 等级 | 阈值 | Volume | Pitch |
|---|---:|---:|---:|
| Light | 120 N | 0.46 | 1.04 |
| Medium | 350 N | 0.72 | 1.00 |
| Heavy | 850 N | 0.95 | 0.94 |

以下状态禁止播放：

- `GameItem.IsHeld`
- `PhysicsBody.IsFollowing`
- `PhysicsBody.IsPaused`
- `PhysicsBody.IsContact`
- 物品创建后的前 `0.25 s`

`ImpactSoundEmitter` 使用 `0.18 s` 冷却合并一次倒地中的连续接触脉冲；两个已标记重物
互撞时只由一侧播放，避免双响。音源使用与易碎品一致的 3D Logarithmic 衰减和
`0.35-7 m` 范围，跟随物品暂停和销毁。

2026-10-10 验收：

- `ImpactSoundTests` 3/3，覆盖生产定义、轻/中/重音量和真实椅子坠落。
- `PhysicsRuntimeTests` 5/5，`GripGrabTests` 2/2。
- APK SHA-256：
  `b4a4e0273a63281f9d3c4d60e8d13f9029bdc50c04463e0036474fb1e9483336`。
- 宿主与模拟器设备哈希一致；冷启动 `MATESOUL_READY`，应用音频异常为 0。

## 2026-10-10 金属撞击时序修复

金属撞击原始素材开头包含约 `99.7 ms` 静音。运行时的 `OnCollisionEnter → Play()`
顺序没有额外等待，但该静音会造成“受力结束后才发声”的感知。项目副本已裁掉
`94 ms`，保留约 `5.7 ms` 前导，时长变为 `2.906 s`。

`ImpactSoundTests` 现在直接读取 Clip 开头 PCM，要求播放请求后 `12 ms` 内出现超过
`0.002` 的有效样本。不要用预测碰撞替代真实 impulse 判级；预测只能估计速度，可能在
没有发生接触时误播或选错轻/中/重等级。

本阶段 APK SHA-256：
`983a6b287c878f67686a4f30362e44f498ba4d0599c7335703495b9a46544cf6`。
