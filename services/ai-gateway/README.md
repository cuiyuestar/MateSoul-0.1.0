# MateSoul AI Gateway

MateSoul 的服务端对话网关。Unity 只发送会话上下文与运行时能力目录；DeepSeek
凭据、供应商协议和联网 MCP 均留在服务端，不进入 APK。

## 架构

```text
Unity HttpLLMProvider
  -> POST /v1/conversations/reply
  -> ConversationService
  -> DeepSeek chat/completions (tool_choice=auto)
  -> stdio MCP: web_search / web_fetch
  -> 严格校验后的 ConversationReply
```

- 动作、可用目标和情绪 ID 每轮由 Unity 运行时提供，不在网关写角色或物品名称分支。
- Unity 每轮同时提交结构化 `persona` 与 `runtime` 快照。人设包含固定身份和当前五维
  性格选择；运行时包含衣装、情绪、姿态、活动 Action 阶段和有界近期事件。
- MCP 工具在每轮模型请求中自动可用；是否调用由模型根据问题决定。
- 最终回复必须满足固定 JSON 契约，动作和目标会再次按运行时目录校验。
- `web_fetch` 拒绝 loopback、私网和本地网络地址，不执行页面脚本。

## 本地运行

要求 Node.js `>=20.18.1`。

```bash
cd /Users/bytedance/MateSoul/services/ai-gateway
npm install
cp .env.example .env.local
```

在 `.env.local` 中配置服务端凭据：

```dotenv
DEEPSEEK_API_KEY=<server-side-key>
```

开发环境推荐使用项目启动脚本。脚本会校验服务端密钥、构建 TypeScript、后台启动
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
`MateSoul/Assets/MateSoul/Content/ConversationGateway.asset`。HTTP loopback 只允许
Development Build；正式环境应配置 HTTPS 网关地址。

## 配置

- `config/gateway.json`：监听地址、DeepSeek 模型、超时、MCP 轮次和网页读取上限。
- `config/character.json`：角色身份与对话提示。
- `MateSoul/Assets/MateSoul/Content/LengyuePersona.json`：Unity 使用的人设内容源，
  固定冷月身份并定义五个性格维度及玩家可选档位。
- `DEEPSEEK_API_KEY`：必填，仅从进程环境读取。
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
`AI_REQUEST_COMPLETED`。`AI_REQUEST_STARTED` 会记录上下文 revision、衣装、姿态和
活动 Action 的 ID/状态/阶段，不记录 API key 或完整用户输入。Unity 侧
`AI_CONTEXT_CAPTURED` 记录快照采集毫秒数与最终请求字节数。
