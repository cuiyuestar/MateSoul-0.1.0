# MateSoul UI 交互冲突根因与修复

最后更新：2026-10-24

progress_status: awaiting_post_fix_human

## 结论

本次真机日志足以确认 UI 无响应不是单一“卡死”，而是三条输入状态机冲突，
再叠加一条每帧物理告警：

1. HUD 眼手点击使用自定义 `IsPinching` 迟滞，而旧版使用 PICO 原生
   `pinchTouched`。原生松开后，自定义状态最长又保持约 734 ms，
   造成 pointer-up 延迟并丢失 click。
2. 自定义 `Closed` 同时参与眼手和食指触碰准入。正常捏合时 grasp 也可能升高，
   因此一个握拳估计会取消眼手 press，并同时禁止食指触碰。
3. 旧食指触碰必须先 down，再等待手指精确退出；按深或目标轻微变化会取消，
   日志中两次都只有 down/up，没有 click。
4. 近物检测对非凸 MeshCollider 调用不支持的 `Collider.ClosestPoint`，
   logcat 中产生 27,529 条警告，约 130 条/秒，持续放大主线程和日志压力。

HUD、CanvasGroup、应用焦点及眼动在两次失败点击窗口内均有效；触碰占用没有残留。

## 真机证据

设备：`PB3A10MGL9220170B`。运行日志由已有 `DeviceStartupLog` 回退通道记录，
当前运行共取得 269 条 `MATESOUL_INPUT_TRACE`。HTTP collector 没收到事件，
但不影响设备文件证据。

### 眼手失败一：松开被迟滞

`startup-current.log`：

- L212：frame 1504，原生和自定义捏合均为 1，产生 down。
- L215：132 ms 后原生 `nativePinch=0`，但 `customPinch=1`。
- L219-L221：再过约 734 ms，自定义状态才归零，只产生 up，没有 click。
- 同期 `eyeTracked=1`、`focus=1`、HUD 可交互。

### 眼手失败二：握拳门槛取消

- L299：frame 2431 正常 down。
- L302：132 ms 后原生已松开，自定义仍保持。
- L306：约 700 ms 后 `customPinch=1` 且 `closed=1`。
- L307-L308：`canceled=1`，只产生 up，没有 click。

当前运行中：

- 右手 68 个状态样本里有 17 个“原生已松开、自定义仍按住”。
- 16 个样本处于 `Closed`；最低记录 grasp 为 0.527，说明迟滞保持时间明显。
- 眼手 10 次 down、10 次 up、只有 8 次 click。

### 食指失败

- L388-L390：第一次“关闭”触碰只有 down/up。
- L393-L394：第二次仍只有 down/up。
- 食指共 4 次 down、4 次 up、只有 2 次 click。

### 已排除项

| 假设 | 证据 | 结论 |
|---|---|---|
| A 原生松开与自定义迟滞不同步 | 17个真实样本；两次失败均先原生松开 | CONFIRMED |
| B Closed/关节门槛误拒绝 HUD | 第二次失败明确 `closed=1`、`canceled=1` | CONFIRMED |
| C 触碰占用残留 | 48个仲裁样本中 `touchingHud=1` 为0 | REJECTED |
| D UI过渡、焦点或眼动失效 | 失败窗口 focus/eye/UI 均有效；阻塞样本为0 | REJECTED |
| E 食指取消状态机丢 click | 4次down/up只有2次click，与取消合同一致 | CONFIRMED |
| F 非凸 ClosestPoint 告警风暴 | logcat 27,529条，调用栈指向 FindCandidate | CONFIRMED |

## 修复

- `PicoTrackedHand` 独立暴露 `NativeTracked` / `NativePinching`。
- `HudEyeHandInteractor` 恢复旧版原生 `pinchTouched` 按下/松开语义；
  不再用 `Closed` 或完整手掌关节状态决定 HUD 捏合。
- 世界交互仍保持近距离直接手势；持物或手掌靠近物品时，HUD 眼手仍让位。
- `PicoHandPoke` 改为首次接触后相邻 UI 帧完成一次稳定点击，
  无需精确回撤；离开 15 mm 释放距离后才重新允许下一次点击。
- `PicoDirectHandInteractor` 每手每帧只刷新一次近物状态；
  非凸 MeshCollider 使用 bounds 最近点，停止非法 `ClosestPoint`。
- HUD 仍为 45 cm / 30°；房间定位重试、现实网格、High Mesh LOD=2、
  HandsOnly、角色与存档均保持。

## 自动验证

空 Play Mode 场景使用真实 XRI `XRUIInputModule` 回放，6项通过：

1. `Closed=1` 不再屏蔽原生 HUD 捏合。
2. 原生松开当帧释放，不等待自定义迟滞。
3. 食指首次轻触产生一次完整 click。
4. 手指未退出释放距离前不重复点击。
5. 退出后再次轻触可重新点击。
6. 非凸网格近物检测不再产生 `Physics.ClosestPoint` 警告。

正式 APK：

- 路径：`/Users/bytedance/MateSoul/Builds/MateSoul_UIConflictFixed_20261024.apk`
- SHA-256：`96afed789cafd793c357b7d02983831e2b52ad4a2c7e4efc430432272b9491fa`
- 大小：202,921,796 bytes；BuildPipeline 4分03秒。
- 签名、1564项 ZIP CRC、ARM64、包名和 libil2cpp 一致性通过。
- 5个修改源码及11个关联源码均与 Android PDB 匹配。
- 主场景、冷月 Prefab、Animator 哈希未变化。
- 已安装；设备 APK 哈希匹配。
- 存档安装前后 SHA-256 均为
  `c32fb27b536e8eba8cf427f27cca2ba6056de9704cf8ab8108ce4756dbd84114`。

## 待真人复验

安装时头显为 Asleep，无法自动生成真实眼动和食指接触。下一次佩戴后需要：

1. 连续眼手点击“互动 / 信息 / 虚拟物品”。
2. 打开对话页，用食指轻触“关闭”。
3. 观察是否每次只触发一次且不会进入两种输入同时失效。
4. 保持应用打开约10秒，以便回捞 `runId=post-fix` 日志。

在取得该样本前，自动验证已通过，但不写“真人验收通过”。
