# Trae + pico-cli 工作流

> 最后更新：2026-07-14

本文记录 MateSoul 当前采用的 Trae + pico-cli 开发路线。Claude Code 已保留安装，但由于登录入口暂不可用，当前不作为主工作流依赖。

## 1. 当前结论

当前主路线：

```text
Trae 负责 AI 协作与代码修改
pico-cli 负责 PICO 工具链、模拟器、设备、应用安装和知识库能力
项目内 .pico-home 与 .pico-cli-workspace 负责规避 Trae 沙箱对 home 目录写入的限制
```

已验证可用能力：

- `pico-cli doctor`
- `pico-cli plugin doctor`
- `pico-cli mcp doctor`
- Spatial Knowledge MCP 数据：READY
- Spatial Editor MCP bootstrap：READY
- Codebase SSH：已通过
- Primer CLI：已安装在项目内 `.pico-cli-workspace/primer`
- agent-vault `0.13.3`：已安装在项目内 `.pico-home/0.13/agent-vault`

## 2. 环境入口

在 Trae 或普通终端中使用 pico-cli 前，优先执行：

```bash
cd /Users/bytedance/MateSoul
source scripts/pico-env.sh
```

也可以直接通过包装器执行：

```bash
scripts/pico-run.sh doctor
scripts/pico-run.sh mcp doctor
scripts/pico-run.sh plugin doctor
```

一键体检：

```bash
scripts/pico-doctor.sh
```

## 3. 关键目录

```text
/Users/bytedance/MateSoul/.pico-home
  项目内 PICO_HOME，存放 agent-vault 等本地工具资产

/Users/bytedance/MateSoul/.pico-cli-workspace
  项目内 pico-cli 插件工作区，存放 spatial-agentic-tools 与 primer-cli

/Users/bytedance/MateSoul/scripts
  Trae + pico-cli 直跑脚本
```

这些目录已被 `.gitignore` 忽略，不应提交到仓库。

## 4. 常用命令

### 环境检查

```bash
scripts/pico-doctor.sh
```

### Spatial SDK 项目创建

```bash
scripts/pico-run.sh project create \
  --template stage \
  --dir /Users/bytedance/MateSoul/apps/mate-spatial-demo \
  --name MateSpatialDemo \
  --package com.matesoul.spatial.demo
```

模板选择：

```text
planar      2D 面板或普通 UI 优先
volumetric  3D 展示盒子或模型查看器
stage       沉浸式、MR、手势、空间感知
```

### 模拟器与设备

```bash
scripts/pico-run.sh emulator doctor
scripts/pico-run.sh emulator start
scripts/pico-run.sh device list
scripts/pico-run.sh device info
```

### 应用安装与日志

```bash
scripts/pico-run.sh app install /path/to/app.apk
scripts/pico-run.sh app launch com.example.package
scripts/pico-run.sh app logcat
scripts/pico-run.sh app watch-crash com.example.package
```

## 5. MCP 配置

项目根目录已提供 `.mcp.json`：

```text
pico-spatial-knowledge -> scripts/mcp-spatial-knowledge.sh
pico-spatial-editor    -> scripts/mcp-spatial-editor.sh
```

注意：`pico-cli mcp doctor` 显示 READY 代表底层能力可用，但 Trae 是否自动挂载项目级 MCP 取决于 Trae 当前版本和 MCP 配置加载机制。当前主路线不依赖自动挂载，必要时直接调用 pico-cli 或读取本地 agent-vault。

## 6. 已知卡点与规避方式

### Trae 沙箱拦截 home 目录写入

已遇到的受限路径包括：

```text
~/.pico-cli/cache
~/.pico/6.0
~/.claude/plugins/*.tmp
~/.unity/bin/unity
```

规避策略：

- 使用项目内 `.pico-cli-workspace`
- 使用项目内 `.pico-home`
- 尽量通过 `scripts/pico-run.sh` 和 `scripts/pico-env.sh` 执行命令

### Claude Code 暂不作为主链路

Claude Code 已安装并保留，但登录入口暂不可用。后续若获得可用 Claude/Bedrock/Vertex/企业入口，可重新启用 Claude Code，不需要删除当前配置。

### Unity CLI 未完全接入

当前 `pico-cli` 版本没有 `mcp:unity-cli` 命令；Unity Editor MCP 相关能力先以 `pico-cli mcp doctor` 的 Spatial Editor READY 状态作为基础验证。后续需要 Unity Editor 深度自动化时，应单独确认 Unity MCP bridge 是否在项目中启用。

## 7. 后续建议

1. 后续 PICO 工具链操作优先走 `scripts/pico-run.sh`。
2. 新建 Spatial SDK 项目前先跑 `scripts/pico-doctor.sh`。
3. 若需要更新 spatial-agentic-tools，先确认 Trae 沙箱是否允许相关 home 路径写入；否则继续使用项目内 workspace。
4. 若切换到 Unity 实现路线，优先把 Unity 项目、PICO Unity SDK、XRI 与 Animation Rigging 的状态单独纳入 doctor 文档。
