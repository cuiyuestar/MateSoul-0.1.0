# MateSoul AI Gateway

2026-10-25 起，此目录保留为历史实现参考。当前 Unity APK 自带提示词、供应商协议、
联网工具循环和严格回复校验，直接向玩家配置的供应商 HTTPS API 请求。
玩家填写 API 地址、模型和自己的 API Key；Android Keystore 加密保存密钥。
当前游戏和模拟器启动脚本均不依赖本服务，也不需要 ADB reverse 8787。

以下文档描述历史网关的运行方式。

## 架构

```text
Unity HttpLLMProvider
  -> POST /v1/conversations/reply
  -> ConversationService
  -> DeepSeek chat/completions (tool_choice=auto)
  -> stdio MCP: web_search / web_fetch
  -> 严格校验后的 ConversationReply

Unity HttpLLMProvider
  -> POST /v1/system-replies/generate
  -> SpontaneousReplyService
  -> DeepSeek chat/completions (tools=[])
  -> 严格校验后的 SpontaneousReply
```

- 动作、可用目标和情绪 ID 每轮由 Unity 运行时提供，不在网关写角色或物品名称分支。
- Unity 每轮同时提交结构化 `persona` 与 `runtime` 快照。人设包含固定身份和当前五维
  性格选择；运行时包含衣装、情绪、姿态、活动 Action 阶段和有界近期事件。
- MCP 工具在每轮模型请求中自动可用；是否调用由模型根据问题决定。
- 最终回复必须满足固定 JSON 契约，动作和目标会再次按运行时目录校验。
- 最终回复必须包含 `response_emotion`，且只能是
  `normal | happy | sad | angry`；Unity 用它选择本轮角色语气音效。
- `web_fetch` 拒绝 loopback、私网和本地网络地址，不执行页面脚本。
- 系统性自发回复只接收人格、事件、内容要求和规则，不接收会话历史、运行时动作、
  物品目标或心情信号，不开放 MCP，也不写回普通对话历史。

## 本地运行

要求 Node.js `>=20.18.1`。

```bash
cd /Users/bytedance/MateSoul/services/ai-gateway
npm install
cp .env.example .env.local
```

如需提供服务端默认密钥，可在 `.env.local` 中配置：

```dotenv
DEEPSEEK_API_KEY=<server-side-key>
```

开发环境推荐使用项目启动脚本。脚本会构建 TypeScript、后台启动
网关并等待健康检查通过；重复执行会复用现有健康实例：

```bash
/Users/bytedance/MateSoul/scripts/start-ai-gateway.command
```

健康检查：

```bash
curl -fsS http://127.0.0.1:8787/health
```

`/Users/bytedance/MateSoul/scripts/start-pico-emulator.command` 会先启动网关，并在
`emulator-5554` 上线后自动建立以下 ADB reverse：

```bash
/Users/bytedance/MateSoul/.pico-home/6.2/emulator/system-images/platform-tools/adb \
  -s emulator-5554 reverse tcp:8787 tcp:8787
```

网关运行日志和 PID 分别位于 `.dbg/ai-gateway/gateway.log` 与
`.dbg/ai-gateway/gateway.pid`。直接调试网关源码时仍可在本目录执行
`npm run dev`，但该前台进程需要保持运行。

Unity 本地 Development 配置位于
`MateSoul/Assets/MateSoul/Content/ConversationGateway.asset`，生产资产默认留空，玩家通过
“设置 → 系统 → AI配置”填写地址、模型、API Key 和可选网关令牌。HTTP loopback
只允许 Development Build；正式环境使用 HTTPS。

## 配置

- `config/gateway.json`：监听地址、DeepSeek 模型、超时、MCP 轮次和网页读取上限。
- `config/character.json`：角色身份与对话提示。
- `MateSoul/Assets/MateSoul/Content/LengyuePersona.json`：Unity 使用的人设内容源，
  固定冷月身份并定义五个性格维度及玩家可选档位。
- `DEEPSEEK_API_KEY`：可选服务端默认密钥；未配置时网关仍可启动，等待玩家提交自己的密钥。

## 玩家 AI 配置

- 普通对话、自发回复和配置检查均接受 `X-AI-API-Key` 与 `X-AI-Model`。
- 每个请求建立独立模型客户端，不保存玩家密钥，不修改其他请求的凭据。
- 如网关启用了访问令牌，客户端通过 `Authorization: Bearer <token>` 提交；这个令牌与
  模型供应商的 API Key 是两个独立字段。
- `POST /v1/configuration/check` 会真实调用一次模型，最长 15 秒，成功返回
  `{"status":"ok"}`。无密钥/无效密钥返回 401，余额不足 402，模型无效 400，限流 429。
- 输入页底部的“如何配置AI”说明获取 DeepSeek 密钥、填入游戏网关、测试与保存步骤。
  模型供应商的 `/chat/completions` 地址不能直接作为游戏网关。
- 本机头显开发：建立 `adb reverse tcp:8787 tcp:8787` 后，在 Development 包中填写
  `http://127.0.0.1:8787`；面向其他玩家部署时提供 HTTPS 网关域名。
- Android 使用非导出 AES-GCM Keystore 密钥持久化；Unity Editor 只在本次会话中保留秘密，
  下次进入 Play Mode 需要重新填写。日志不包含 API Key 或访问令牌。
- `server.clientTokenEnvironmentVariable`：部署环境可指定 Unity 客户端令牌变量名。

`.env.local` 已被版本控制忽略。不要把 API key 写入 Unity Asset、C# 源码、网关
JSON 配置或 APK。

## 验证

```bash
npm test
npm audit --audit-level=moderate
```

真实 DeepSeek 和 MCP 调用会输出结构化事件：
`AI_REQUEST_STARTED`、`MCP_TOOL_REQUESTED`、`MCP_TOOL_COMPLETED`、
`AI_REQUEST_COMPLETED`。系统性自发回复使用
`SYSTEM_REPLY_REQUEST_STARTED`、`SYSTEM_REPLY_REQUEST_COMPLETED` 和
`SYSTEM_REPLY_REQUEST_FAILED`。`AI_REQUEST_STARTED` 会记录上下文 revision、衣装、
姿态和活动 Action 的 ID/状态/阶段，不记录 API key 或完整用户输入。Unity 侧
`AI_CONTEXT_CAPTURED` 记录快照采集毫秒数与最终请求字节数。
