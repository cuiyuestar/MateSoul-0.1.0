# 游戏开发历史产物清理记录

日期：2026-10-25  
范围：`/Users/bytedance/MateSoul`

## 清理结果

- 删除文件：140,562 个
- 删除文件逻辑大小：23.16 GiB
- 删除文件分配空间统计：23.53 GiB
- 清理开始前可用空间：约 2.47 GiB
- 清理完成后可用空间：约 15.68 GiB
- 可用空间实测净增加：约 13.20 GiB

APFS 共享块、构建期间的新文件和系统并发活动会影响实际可用空间，因此逻辑大小不等同于最终物理释放量。

## 已删除

仅清理了 10 月 14–18 日的独立历史验证工程 Unity `Library` 缓存：

| 缓存目录 | 处理结果 |
|---|---|
| `.artifacts/item-focus-debug/build-project/Library` | 已完整删除 |
| `.artifacts/cut-fruit-action/movement-build/MateSoul/Library` | 已完整删除 |
| `.artifacts/cut-fruit-action/validation/MateSoul/Library` | 部分清理，剩余约 1.38 GiB |

第三个缓存清理期间检测到新的 Unity 回归任务启动，清理程序按保护规则立即停止。为避免与当前开发和测试争用磁盘，未继续删除剩余缓存。

## 保留范围

- 生产 Unity 工程 `MateSoul/Library`
- `Assets`、`Packages`、`ProjectSettings` 及所有源码、模型、动画和脚本
- `项目沉淀资料/性能测试` 下的启动性能与归因数据
- 当前 `Builds/MateSoul_Framework.apk`
- 10 月 20 日后的 APK、截图、视频、日志和真机验证材料
- 旧项目的 `Assets`、构建脚本和测试脚本
- PICO 模拟器磁盘、SDK 与当前开发工具链

本次没有执行原计划中的旧 APK、截图、录屏和日志删除，因为当前连续构建与回归测试优先级更高。

## 完整性验证

- 清理前后 Git 工作区状态完全一致。
- 当前性能归因文件 `startup-attribution.csv` 存在。
- 当前 APK 存在。
- 清理完成后的增量 Android 构建成功，`exitCode=0`。
- 构建保护清单中的文件变化数为 0。
- 专项回归测试 28/28 通过。
- 渲染验收正常退出。

结论：本次清理仅作用于旧独立验证工程的可重建缓存，没有影响当前生产工程、性能测试数据或正在进行的开发验证。
