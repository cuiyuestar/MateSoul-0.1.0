# 游戏开发历史产物清理记录

日期：2026-10-29  
范围：`/Users/bytedance/MateSoul`

## 清理结果

- 删除文件：12,315 个
- 删除文件逻辑大小：9.18 GiB
- 文件分配空间统计：9.22 GiB
- 磁盘可用空间实测增加：2.01 GiB
- 清理后可用空间：约 30 GiB

逻辑大小与实际释放量差异主要来自 APFS 克隆文件和共享数据块。大量历史 APK 虽然表现为独立文件，但底层可能共享相同构建数据。

## 已删除

| 类型 | 文件数量 | 逻辑大小 |
|---|---:|---:|
| 10 月 16 日独立验证工程 Unity `Library` 缓存 | 11,791 | 1.35 GiB |
| 10 月 24–26 日已被后续版本替代的测试 APK | 37 | 6.89 GiB |
| 旧验证目录 APK | 6 | 0.78 GiB |
| 历史调试与构建日志 | 464 | 0.15 GiB |
| 历史截图与自动录屏 | 17 | 0.02 GiB |

已完整删除：

`/.artifacts/cut-fruit-action/validation/MateSoul/Library`

该目录属于独立历史验证工程的可重建缓存。对应工程的 `Assets`、`Packages` 和 `ProjectSettings` 均保留。

## 保留范围

- 10 月 27–29 日的所有 APK、日志和验证材料
- 当前 `Builds/MateSoul_Framework.apk`
- `MateSoul_ClockTimeFix_20261029.apk`
- 近期键盘、深度、拾取、动画和交互验证 APK
- `MateSoul_Framework_Baseline_20261022.apk`
- `MateSoul_StartupOptimized.apk` 及其他启动性能基线包
- `项目沉淀资料/性能测试` 全部数据
- 生产 Unity 工程 `MateSoul/Library`
- PICO 模拟器磁盘、SDK 和当前工具链
- 所有源码、模型、动画、材质、Prefab、测试脚本和渲染脚本
- `ArtSource` 中的原始资产、检查点和正式交付包

## 完整性验证

- 清理前后 Git 工作区状态完全一致。
- 候选文件与 Git 跟踪文件交集为 0。
- 清理开始和结束时均无 Unity、Blender、模拟器或 Gradle 任务运行。
- 当前框架 APK、今日构建包和近期关键验证包均存在。
- 启动性能归因 CSV 与基线 APK 均存在。
- 生产工程、当前源码和近期测试证据未被修改。

结论：本次清理仅作用于已被后续版本替代的历史构建产物、旧日志媒体和独立验证工程缓存，不影响当前开发和测试工作。
