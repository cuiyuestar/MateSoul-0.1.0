# MateSoul APK 内 AI 系统迁移

最后更新：2026-10-26

## 交付范围

用户授权参照 MateFairy，把提示词、模型协议、function calling 循环、联网工具与
回复校验迁入 Unity APK。玩家在游戏内填写供应商 API 地址、模型与自己的 API Key。
默认地址为 `https://api.deepseek.com/chat/completions`，默认模型为 `deepseek-v4-pro`。
保存配置立即生效，游戏无需 MateSoul 服务器、Node.js 网关或 ADB 端口桥接。

平台维持 Unity 2022.3.38f1 / URP / PICO Unity SDK / Android ARM64 / IL2CPP。
迁移不引入 Kotlin、Spatial ECS、Compose 或另一个动作执行系统。

## 运行流程

```text
GameHud
  -> ConversationOrchestrator 捕获人格/运行状态/能力目录/会话历史
  -> HttpLLMProvider 直接发送供应商 chat/completions
  -> 模型请求 web_search / web_fetch 时，APK 执行并回灌工具结果
  -> ModelReplyProtocol 校验文本/情绪/动作/目标/心情信号
  -> ConversationOrchestrator 更新会话，并向 AvatarActionCoordinator 提交动作
```

自发回复共用 HTTP 客户端，但只发送人格、事件、内容要求和规则；
不开放工具、不请求动作、不读取或追加普通会话历史。

## 代码与资源

- `Runtime/HttpLLMProvider.cs`：供应商协议、工具循环、单次格式修正和错误映射。
- `Runtime/ModelReplyProtocol.cs`：人格提示词、权威运行上下文、目标压缩及严格 JSON 校验。
- `Runtime/AIWebTools.cs`：进程内 `web_search` 和 `web_fetch`。
- `Runtime/AIBoundedDownload.cs`：流式字节上限和 UnityWebRequest 取消。
- `Runtime/PlayerAIConfiguration.cs`：版本2配置、供应商地址规范化、Bearer Key 与本机存储。
- `Runtime/ModelConnectionSettings.cs`、`Content/ModelConnection.asset`：
  默认连接策略；由旧 ConversationGateway 文件改名，保留两个原 GUID。
- `GameBootstrap.modelConnection` 使用 `FormerlySerializedAs("conversationGateway")`
  保留场景既有资源引用，无需重建场景或角色。
- `Packages/manifest.json`：Unity 官方 Newtonsoft JSON 3.2.1，供动态工具消息和严格解析使用。

路径均相对 `/Users/bytedance/MateSoul/MateSoul/Assets/MateSoul`；
Packages 路径相对 Unity 项目根目录。

## 配置和凭据

配置页保留 API 地址、模型名称、API Key、测试连接、保存和教程；删除网关访问令牌。
支持供应商根地址、`/v1` 或以 `/chat/completions` 结尾的完整接口。
生产请求只允许 HTTPS，地址不能携带用户凭据、查询串或 fragment。
API Key 仅发送在供应商请求的 `Authorization: Bearer ...`，不发送给网页工具。
供应商请求禁止自动重定向，错误提示不回显供应商响应正文或密钥。

旧版本0记录中的网关地址替换为默认供应商地址，保留 API Key 与模型。
新版本2记录保留玩家选择的供应商。旧 gatewayToken 不再序列化或使用。
Android 沿用 AES-GCM / Android Keystore 存储；Editor 只持久化公开配置，
密钥只在当前播放会话中保留。APK 不包含验证用密钥。

## 有界执行合同

- 每轮总预算最多25秒，处于现有 ConversationOrchestrator 的30秒预算之内。
- 最多4轮工具调用，每轮最多8次；最终回复格式/能力准入失败只修正一次。
- 修正请求不开放工具，连续无效结果直接报错，不执行动作或更新历史。
- 文本最多600字符，情绪仅 normal/happy/sad/angry。
- 动作必须来自当轮开放目录；目标必须支持所需能力。
- 无目标动作不能携带目标；心情信号最多16项，仅注册 ID、delta 在[-32,32]。
- JSON 拒绝重复字段、额外字段、错误类型和多个对象。
- 模型响应最多256KiB，网页响应最多1MiB；网页提取最多20,000字符。
- 网页只允许公网 HTTPS 默认端口，检查 DNS 地址与每一跳重定向，
  禁止本机、私网、链路本地、保留地址；最多4次重定向。
- 搜索采用 Bing RSS，XML 禁止 DTD；网页不执行脚本，移除 script/style/noscript，
  返回的内容在提示词中明确为不可信参考数据。
- 网页请求10秒预算；取消传递到工具和 HTTP，不吞掉整轮取消。
- DeepSeek 请求关闭额外 thinking，避免短对话消耗推理预算。

此处迁移的是原网关已注册的两个联网能力。任意远端 MCP 服务器管理、
长期记忆、RAG 和 Dream 不属于本次交付范围。

## 旧网关退役

`services/ai-gateway` 留作历史实现参考，README 已标明当前 APK 不依赖它。
`scripts/start-pico-emulator.command` 不再启动网关、建立8787桥接或因本地网关
要求 Development 包。本次创建的 `com.matesoul.ai-gateway` launchd 任务已停止，
确认网关主进程和网页子进程退出，8787无监听。

## 验证进度

首轮无图形模式39项检查中38项通过。唯一失败是需要 GPU 的现实遮挡渲染用例，
因此必须以随后有图形模式的结果作为最终遮挡验证证据。

真实供应商与专项检查已28/28通过，Unity退出0。包含供应商连接检查、普通对话、
自发回复、搜索/网页读取、真实模型工具回灌、Bearer协议、取消、认证错误脱敏、
单次格式修正、旧配置迁移和HUD状态保持。验证时旧网关及子进程已经停止。

- 真实普通对话：1499ms。
- 真实自发回复用例：约1633ms。
- 真实模型调用搜索与网页读取后回复：4368ms，1轮工具调用。
- 搜索回灌1198字符，网页读取回灌4535字符。
- 原始结果：`/tmp/matesoul-direct-ai/cloud-results.xml`；
  日志：`/tmp/matesoul-direct-ai/cloud.log`。

构建过程曾因磁盘满（ENOSPC）与并行Unity锁等待中断；空间恢复后继续验收。
新的执行任务使用KeepAlive=false，15秒心跳、180秒日志停滞终止，并在剩余空间
低于5GiB时主动停止。陈旧锁只有在lsof无持有者且没有Unity进程时才移除。
GPU遮挡14项、直连19项、HUD5项、自发回复1项和游戏对话合同6项，
共45项已完成验证。首批43/45通过；两项旧用例分别假设固定中文衣装名和允许
回收角色正在使用的物品。更新用例后复测2/2通过：衣装快照对应当前权威显示名；
占用物品拒绝回收，外部目标丢失仍使动作失败、释放资源，再完成库存清理。
这两项仅更新测试，没有修改当前游戏的回收或衣装行为。

原始记录：`/tmp/matesoul-ai-finish-20261026/before-fixture-update-regression.xml`，
复测：`/tmp/matesoul-ai-finish-20261026/regression.xml`。

## APK 交付结果

- APK：`/Users/bytedance/MateSoul/Builds/MateSoul_APKLocalAI_20261026.apk`。
- 文件大小：218,746,149字节（约208.6MiB）。
- SHA-256：`1b7252ef8d26210e708aad85d8b1d24c5a2615f8865c29f5ef47ffdf9884d880`。
- 构建：Android ARM64 / IL2CPP / Development，耗时2分02.899秒，退出0。
- 1569项ZIP CRC、APK v2签名、zipalign、APK原生库与本轮构建产物一致性通过。
- 13份关键运行时代码的Player PDB校验和与当前源码一致，包含AI直连、
  当前HUD、抓取、流体和现实遮挡代码。
- PICO SDK、VST、Spatial Mesh、Environment Depth、Spatial Anchor和网络权限保留。
- Android AISecretStore已编入DEX；扫描所有解压包内容未发现验证用开发者密钥。
- 构建期间场景、Lengyue_Runtime.prefab及Avatar.controller的SHA-256保持一致。
- 最终数值记录：
  `/Users/bytedance/MateSoul/项目沉淀资料/validation/apk_local_ai_20261026.json`。

当前ADB未连接头显，未尝试安装，数值报告明确记录`installed=false`。
覆盖安装后的存档一致性、Keystore重启读取、实际头显对话和佩戴视觉仍待设备连接后验收。
已有Editor真实供应商/GPU结果不能替代这些真机结论。
