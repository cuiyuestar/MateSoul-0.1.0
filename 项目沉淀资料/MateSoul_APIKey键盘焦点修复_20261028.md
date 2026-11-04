# MateSoul：API Key 系统键盘焦点修复

日期：2026-10-28

点击 API Key 输入框后，首次按键导致键盘消失的问题已修复到 Unity 主项目。API 地址、模型名称与对话输入框使用同一处理方式。

## 原因与实现

Unity 的实际 XRI 事件链已复现：输入框处于编辑状态时，一个未命中控件的指针按下事件，会清空 EventSystem 当前选中对象并触发 TMP_InputField.OnDeselect。TMP 随即停止编辑；在 Android 原生键盘路径中，该方法还会关闭 TouchScreenKeyboard。

此前系统键盘手势与游戏视线捏合、指尖触碰同时处理，Android 触摸与鼠标来源也默认开启。此次修复建立统一的输入焦点边界：

- `GameHud.IsTextInputFocused` 读取当前选中的 TMP 输入框及其 `isFocused`，直接使用 TMP 的编辑生命周期。
- 输入期间，`HudEyeHandInteractor` 与 `PicoHandPoke` 输出空射线并取消已有按下，继续由 XRI 正常处理释放事件。
- 视线捏合需在编辑结束后松开再重新捏合；指尖需离开触碰区域再重新接触，防止最后一次键盘手势误触保存按钮。
- 输入期间暂停 HUD 隐藏手势，并清除其待触发状态。
- Android 的 XRUIInputModule 关闭鼠标与触摸事件来源；PICO HUD 继续由追踪手势驱动，编辑器鼠标操作保持可用。

修改文件：

1. `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/GameHud.cs`
2. `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/HudEyeHandInteractor.cs`
3. `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul/Runtime/PicoHandPoke.cs`

原有 GameSession 对系统键盘短暂失焦／暂停的处理继续保留：对话请求由自身超时和显式取消控制。

## 验证

- Unity 2022.3.38f1 实际 TMP／XRI GUI 事件链专项：29 个检查通过，进程耗时 22.67 秒，三个修复源码的输入哈希前后相同。
- 覆盖外部点击失焦机制、首次与连续输入、退格、已有点击取消、隐藏菜单抑制、结束／取消／禁用时释放焦点，以及关闭后的捏合与触碰恢复。
- 既有 `PlayerAIHudTests`：5 项全部通过，0 失败、0 跳过，进程耗时 22.03 秒。覆盖 AI 配置、密码输入类型、教程、保存、对话草稿与 HUD 状态保持。
- 专项使用编辑器输入事件；PICO 原生键盘的按键、粘贴、完成和关闭体验仍需佩戴头显验收。

## 安装包与记录

Release 安装包路径：`/Users/bytedance/MateSoul/Builds/MateSoul_KeyboardFocusFix_20261028.apk`。

包体 165,772,917 字节，SHA-256：
`ecb71cc1cab7d8fa68d3d015561b5aeec19291858f6be256ab7e83ecb5d7eee3`。

构建进程耗时 120.76 秒，1815 项输入前后哈希一致。APK CRC、v2 签名、
zipalign、当前 Gradle Release APK 与原生 IL2CPP 库身份核对通过。
Android 生成代码已确认三处焦点判定调用，以及鼠标／触摸来源关闭；
包内 metadata 包含 `get_IsTextInputFocused`。

构建回执、包体大小、SHA-256、签名、CRC、对齐及源码身份记录见：
`/Users/bytedance/MateSoul/项目沉淀资料/validation/keyboard_focus_20261028.json`。

本轮设备查询未发现已连接的 PICO，未进行覆盖安装或原生键盘真机验收。
