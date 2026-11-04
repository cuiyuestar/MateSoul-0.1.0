# 电视交互与视频接入

最后更新：2026-10-06

## 当前交付状态

电视预制体、ItemDefinition、URP 材质、节目目录和场景绑定均已生成。
正式第一个节目为“Q版角色太空横版像素游戏”，H.264/AAC、1280×720、24 fps、
15.072 秒。电视专项测试 `7/7` 通过，覆盖该正式视频的导入元数据、真实解码、
屏幕像素、自然结束循环和资源释放。Android APK 已重建并安装到 PICO Emulator。

后续重新生成场景可执行 `MateSoul > Game > Build Game Scene`；
只生成电视资产可用 `MateSoul > Game > Build Television Asset`。

## 使用

1. 打开 `Assets/MateSoul/Scenes/MateSoul.unity`。
2. 在「虚拟物品」中选择「复古电视」放置。
3. 手柄射线指向模型右下角电源键时，按钮局部发光；按右手 A / 左手 X 点击，
   按钮下压并回弹。
   Unity Editor 中可以使用鼠标点击。握持键仍用于移动整台电视。
4. 点击一次开机后自动循环播放第一个节目；再次点击关机。未配置节目时显示 `NO SIGNAL`。
   电源状态只保留在当前实例生命周期中；重新加载布局后默认关机。

## 输入模式

### 手柄模式

- 右手射线对准电源键后按钮高亮，按 A 键点击。
- 左手射线同样可使用 X 键点击。
- Grip 只负责抓取和移动整台电视，不作为 UI 点击键。

### 眼手模式

- 使用 PICO eye gaze 对准电源键，按钮高亮。
- 右手捏合触发点击。视线射线不绑定抓取，不会在开关电视时移动整台电视。
- 追踪到手柄时默认使用手柄模式；手柄追踪丢失 0.75 秒后自动切换眼手模式。

### 虚拟手物理按压

电视已经包含物理按压面。后续虚拟手的食指节点添加 `PhysicalPressInteractor`、
Collider 和运动学 Rigidbody 后，指尖向机身内部压入约 6 mm 即可触发按钮。
该近场方式在手柄和眼手模式下都可用。

## 节目目录

在 `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Props/Television.prefab`
的根节点找到 `TelevisionController`：

| Inspector 字段 | 配置方式 |
|---|---|
| Programs | 正式节目顺序；数组第 0 项是开机默认节目 |
| Program Index | 当前节目索引，默认 0 |
| Video Clip | 当前活动节目，由节目选择自动同步，也可用于临时调试 |
| Video Url | 不配置 Clip 时，填入绝对 `https://` 视频地址或 `file://` 本地文件 URI |
| Volume | 默认 0.65，作用于电视自身的空间音源 |
| Prepare Timeout | 默认 30 秒；超时显示 `VIDEO ERROR`，不无限重试 |

所有节目强制循环播放。Clip 和 URL 同时填写时，Clip 优先。建议节目使用
MP4 / H.264 / AAC、720p–1080p、24–30 fps；最终音频效果仍需用正式文件在目标
PICO 设备验证。
URL 是直接媒体地址，不能使用视频网站的页面链接；正式网络资源优先使用 HTTPS。
`file://` 指向运行设备可读的文件；电脑上的路径不会自动复制到头显。

正式节目由 `ConfigurePrograms(VideoClip[])` 注册，运行时使用 `SelectProgram(int)`
切换。临时换源仍可使用 `SetVideoClip(VideoClip)` 或 `SetVideoUrl(string)`。
开机时换源会停止旧解码、释放旧纹理并准备新视频；关机时只更新配置。
清空视频源后，开机状态保持为 `NoSignal`。
`TelevisionBuilder` 每次重建都会把正式节目列表重新写入 Prefab，节目顺序由构建器
统一维护。

当前第一个节目资源：

```text
/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Content/Video/PixelSpaceGameProgram.mp4
VideoClip GUID: d46263424b4b94293a866c1f1af847bb
SHA-256: c55bdafdcc691236eccb0c3639b2eb044d487eb6d19f6e7538c5e381879f0079
```

## 实现边界

- `TelevisionPowerButton`：沿用 XR UI / Unity EventSystem。透明的 48 mm 点击区域固定在
  电源键前方；只移动 `TV_Power_Button` 可视网格，行程 3 mm。
  用发光材质属性提示悬停，不新增轮廓描边。
- `TelevisionController`：单一电源、VideoPlayer、AudioSource 和 RenderTexture 所有者。
  不参与角色的 Animator、Transform 或动作协调器。
- `TelevisionScreen.shader`：在原有 `TV_CRT_ConvexGlass` 弧形网格和 UV 上采样视频，
  支持 URP 与 XR 单通道立体渲染；视频画面不受场景灯光变暗。
- 视频由 Unity 原生 `VideoPlayer` 解码到每实例独立的 1024×738 RenderTexture，
  对应原屏幕 358×258 mm。`FitOutside` 保持视频原始比例并居中裁切超出区域，
  填满屏幕且不产生黑边，不使用非等比拉伸。
- `TelevisionBuilder`：从 ArtSource 复制 FBX 和两张贴图到 Unity，创建 URP 材质、
  预制体和 ItemDefinition。原始 Blender/FBX 资产保持不变。
- 加入现有 ItemRegistry 和物品目录，复用放置、握持、删除与布局存档。
  当前角色能力只声明已有的 `visit`，不把“看电视”等尚未实现的角色行为暴露给 AI。

状态路径：

```text
Off → NoSignal                         未配置源
Off → Preparing → Playing              有效节目
Playing ↔ Paused                       应用挂起 / 恢复
Playing → Playing                      每个节目自然结束后循环
Preparing / Playing → Error            加载超时 / 解码错误
任意开机状态 → Off                      再次点击电源
```

组件停用或物品删除时停止声音与视频、释放纹理；再次启用时保持关机。
点击关机可以取消尚未完成的准备，不会因迟到回调再次开机。
播放器错误记录在 `LastError`，通过 `StateChanged` 订阅状态变化；
再次关闭后开启可显式重试，不引入后台重试状态机。

## 验证

`Assets/MateSoul/Tests/Engine/TelevisionTests.cs` 覆盖真实预制体的鼠标和 XR 射线、
遮挡/背面拦截、悬停发光、按压回弹、开关状态、实例隔离、抓取绑定、换源恢复与资源释放。

实际视频解码用例使用 `.artifacts/television/diagnostic.mp4`。本轮验证将正式节目临时
复制到该位置，验证了非黑画面、15.072 秒结束后的循环回调与关机资源释放。验证后会
删除诊断副本和截图。也可生成短诊断片：

```bash
mkdir -p .artifacts/television
ffmpeg -hide_banner -loglevel error \
  -f lavfi -i 'testsrc2=size=640x360:rate=24' \
  -f lavfi -i 'sine=frequency=440:sample_rate=48000' \
  -t 2 -c:v libx264 -pix_fmt yuv420p -c:a aac -movflags +faststart \
  .artifacts/television/diagnostic.mp4
```

在 Unity Test Runner 运行 `MateSoul.Tests.TelevisionTests`。
没有临时诊断文件时，解码用例明确标为跳过，其余用例正常运行。
诊断片和验证截图位于 `.artifacts/television/`，不进入 Assets 或 APK，验证完成后可删除。
