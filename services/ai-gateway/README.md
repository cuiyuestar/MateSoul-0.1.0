# MateSoul AI Gateway

游戏的服务端对话网关，负责 DeepSeek 调用、联网 MCP、动作与目标校验和情绪回复。
Unity 发送会话上下文、人格与运行时能力目录；密钥和供应商协议保留在服务端。

## 启动

要求 Node.js >=20.18.1。在本目录执行：

```bash
npm ci
cp .env.example .env.local
```

在 `.env.local` 中设置 `DEEPSEEK_API_KEY`，然后：

```bash
npm run build
npm start
```

```bash
curl -fsS http://127.0.0.1:8787/health
```

`npm run dev` 可以直接运行 TypeScript 源码。正式部署使用 `npm run build` 与
`npm start`，由 HTTPS 反向代理对外提供服务。

## 接口与配置

- `POST /v1/conversations/reply`：普通对话、可用动作与目标、运行时上下文及情绪。
- `POST /v1/system-replies/generate`：事件触发的自发回复；不修改普通对话历史。
- `GET /health`：健康检查。
- `config/gateway.json`：监听地址、DeepSeek 模型和超时、MCP 轮次、网页读取上限。
- `config/character.json`：服务端角色提示。
- `DEEPSEEK_API_KEY`：服务端凭据。
- `server.clientTokenEnvironmentVariable`：可指定客户端令牌的环境变量名称。

Unity 配置位于 `MateSoul/Assets/MateSoul/Content/ConversationGateway.asset`。
本地 Development Build 通过 `adb reverse tcp:8787 tcp:8787` 连接本机网关；
正式 APK 必须使用设备可访问的 HTTPS 地址。

角色/物品不使用名称分支；返回的动作、目标和 `response_emotion`
按运行时目录和契约验证。`web_fetch` 拒绝私网、loopback 和本地地址。

`.env.local`、依赖安装目录、编译结果、一次性脚本和测试产物不进入仓库。
