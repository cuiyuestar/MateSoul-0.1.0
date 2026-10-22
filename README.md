# MateSoul

PICO 混合现实陪伴游戏。仓库包含 Unity 游戏源码、运行资源、本地 PICO Unity SDK
和 AI 对话网关；克隆时需要保留整个目录结构。

## 环境与打开方式

- Unity **2022.3.38f1**，安装 Android Build Support、SDK/NDK 和 OpenJDK。
- Android **ARM64 / IL2CPP**；URP **14.0.11**。
- Node.js **20.18.1 或更新版本**，用于启动 AI 网关。

```bash
git clone git@github.com:cuiyuestar/MateSoul.git
cd MateSoul
```

在 Unity Hub 中添加 `MateSoul/` 子目录，打开
`Assets/MateSoul/Scenes/MateSoul.unity`。Unity 会按 `Packages/manifest.json` 和
`packages-lock.json` 恢复依赖；`com.bytedance.pico.xr` 从仓库根目录的 `XR/` 加载。
新版冷月的 UniVRM / UniGLTF 包从 `Dependencies/Lengyue/UniVRM-0.131.3/` 加载。
角色、衣装、动画、物品、字体、音效和电视节目视频均随仓库提供。

## 本地网关与设备运行

```bash
cd services/ai-gateway
npm ci
cp .env.example .env.local
```

在 `.env.local` 中填入自己的 `DEEPSEEK_API_KEY`，然后运行：

```bash
npm run build
npm start
```

另一个终端检查健康状态，并为通过 USB 连接的 PICO 设备建立端口转发：

```bash
curl -fsS http://127.0.0.1:8787/health
adb reverse tcp:8787 tcp:8787
```

Unity 菜单选择 `MateSoul → Game → Build Android Development APK`。
生成的 APK 位于仓库根目录 `Builds/MateSoul_Framework.apk`：

```bash
adb install -r Builds/MateSoul_Framework.apk
```

默认 `ConversationGateway.asset` 使用 `http://127.0.0.1:8787`，配合 Development
Build 和上述端口转发工作。编辑器使用同一个本机网关。使用多个设备时给 `adb`
命令添加 `-s <设备序列号>`。

## 正式构建

先部署 `services/ai-gateway`，通过 HTTPS 反向代理提供设备可访问的网关地址；
将 `Assets/MateSoul/Content/ConversationGateway.asset` 的 `endpoint` 改为真实的
`https://<网关域名>/v1/conversations/reply`。随后选择
`MateSoul → Game → Build Android APK`。正式构建会拒绝 HTTP 网关地址。

设备需要支持项目使用的 PICO MR、Scene Capture / Spatial Mesh 和 Environment
Depth 能力，并授权空间数据访问。模拟器不提供与真机等价的动态环境深度。

## 仓库边界

保留运行、资源导入和 Android 构建所需内容，以及第三方许可证。美术制作母版、
实验场景、调试探针、验证图片/视频、一次性脚本、缓存、APK 和开发报告留在本地。
游戏播放的视频和声音属于运行资源。

AI 密钥仅在服务端配置，`.env.local` 不进入版本控制或 APK。
