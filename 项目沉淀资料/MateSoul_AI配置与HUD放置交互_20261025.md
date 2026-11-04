# MateSoul AI配置与HUD放置交互

日期：2026-10-25。平台：Unity 2022.3.38f1、URP、PICO Unity SDK。

## 玩家入口与行为

- HUD可见区域在上一版基础上再下移10cm，`HudVerticalOffset`从`-.10`改为`-.20`，按钮和展开面板使用同一定位方法。
- 无玩家API Key或网关地址时，打开对话框，在输入框上方显示“先配置ai才能激活对话功能”和紫色“点此配置ai”按钮；输入与发送保持不可用。
- 点击提示按钮打开“AI配置”，也可经“设置 → 系统 → AI配置”进入。配置项为网关地址、模型名称、API Key、可选网关访问令牌。两种秘密输入均以密码模式显示。
- “测试连接”会实际调用模型，验证网关、密钥与模型；“保存配置”即时生效。从对话入口保存后回到原对话框并保留草稿；设置入口保存后停留在配置页。
- “如何配置AI”打开内置教程，介绍DeepSeek账户与密钥、游戏网关地址、令牌、测试和保存。模型供应商地址不能直接代替MateSoul网关。
- 固定物品预览开始时隐藏整个HUD，保留原页面、草稿及手动显隐选择；放置提交成功、取消或组件停用时恢复原显隐状态。
- 手部模式绿色预览时张手、握紧后松开确认；双手握拳1秒取消。手柄模式右手扳机确认、B键取消。取消当帧不会同时触发HUD显隐切换。

## 运行时与网关

`PlayerAIConfiguration`由`GameBootstrap`持有；`HttpLLMProvider`每次请求读取当前配置。普通对话、自发回复和配置测试均使用：

| 请求字段 | 用途 |
| --- | --- |
| `X-AI-API-Key` | 玩家供应商密钥 |
| `X-AI-Model` | 玩家模型名称 |
| `Authorization: Bearer …` | 可选网关访问令牌，与供应商密钥独立 |

网关每个请求创建独立模型客户端，复用现有MCP运行时与回复契约。缺少服务端`DEEPSEEK_API_KEY`仍可启动，等待玩家提供密钥；原服务端默认密钥路径保持可用。

`POST /v1/configuration/check`最长15秒真实调用模型；成功返回`{"status":"ok"}`。无密钥/无权限返回401，余额不足402，无效模型400，限流429。供应商错误正文不写入日志或转发给玩家，避免其中可能包含的秘密。

地址边界只接受HTTPS根地址或`/v1/conversations/reply`，拒绝URL账号密码、查询参数、片段与其他路由。Development包可显式允许本机HTTP。生产`ConversationGateway.asset`默认地址为空，构建允许玩家首次配置，不嵌入密钥。

Android使用`Assets/Plugins/Android/AISecretStore.java`：非导出Android Keystore AES密钥、AES-GCM认证加密、私有SharedPreferences保存密文。读取失败要求重新配置。Unity Editor只在本次会话保留秘密，持久化仅包含地址与模型。

本机调试网关由当前用户的launchd任务`com.matesoul.ai-gateway`运行，端口8787，头显已建立`adb reverse tcp:8787 tcp:8787`。开发包填`http://127.0.0.1:8787`并使用自己的密钥即可；向其他玩家发布时提供HTTPS网关。该本机桥接不是公网部署。

## 验证与交付

- Node/HTTP测试21/21：契约、MCP边界、无/无效密钥、并发玩家隔离、网关令牌与配置检查。
- Unity界面与现实遮挡回归19/19：提示文案、密码字段、设置与教程入口、即时保存、草稿保留、放置显隐及手动隐藏恢复、真实GPU遮挡与地面等待。
- 真实DeepSeek调用：配置检查HTTP200/2058ms，普通对话HTTP200/2189ms，自发回复HTTP200/1643ms。
- Android ARM64/IL2CPP构建成功；ZIP CRC、APK签名通过，`AISecretStore`在DEX中，7份关键运行时代码SHA256出现在Player PDB中。
- 已覆盖安装到`PB3A10MGL9220170B`，设备APK与本地产物一致；安装前后房间存档哈希均为`ea6aad38001a4e54046e096b723c39a81ce4912c2518078c22fadf4b7b2d13be`。
- 头显休眠，尚未完成佩戴后的UI视野位置、输入键盘、放置手势和密钥保存/重启读取验收。独立`app_process`密钥探针被Android终止，不作为加密运行验收；仅确认Java编译和DEX收录。

APK：
`/Users/bytedance/MateSoul/Builds/MateSoul_AiConfig_LowerHud_Placement_20261025.apk`

大小：215392670字节。

SHA256：`183cf00823f0f8eb7b479392fd45ea07001ad3abe95a7843ebcf04d5d74840cc`。

数值记录：
`/Users/bytedance/MateSoul/项目沉淀资料/validation/ai_hud_placement_20261025.json`。

本任务没有修改现实遮挡代码、场景、角色或URP资源。构建时保留共享工作区的并行改动。构建结束时受保护文件哈希一致；之后另一个任务继续修改`RealityOcclusionMeshRenderer.cs`，后续磁盘版本不能据此宣称已包含在本APK中。APK验证的是Player源码指纹所记录的构建快照；并行流体/双手交接的验收结论由其所属任务负责。
