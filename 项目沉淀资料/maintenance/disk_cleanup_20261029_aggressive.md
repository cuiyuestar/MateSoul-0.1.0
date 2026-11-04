# 扩展磁盘清理记录

日期：2026-10-29  
范围：MateSoul 历史产物、开发缓存、桌面录屏和下载目录安装包

## 清理结果

- 审计记录删除文件：79,477 个
- 审计内文件逻辑大小：约 29.1 GiB
- 磁盘可用空间实测增加：约 29.0 GiB
- 清理后可用空间：约 65 GiB
- 另有 19 个已替代项目 APK 和 4 个旧美术验证 APK 最终不再占用目录空间

本轮以实际磁盘可用空间变化为准。部分 APK 使用 APFS 克隆块，删除后的物理释放量可能小于文件逻辑大小；开发缓存和录屏通常可以完整释放。

## 已清理内容

| 类型 | 数量或范围 | 大小 |
|---|---:|---:|
| 早期桌面录屏与截图 | 191 个 | 约 9.93 GiB |
| 旧软件安装 DMG | 16 个 | 约 6.45 GiB |
| PICO CLI 下载缓存 | 1 个旧模拟器 ZIP | 约 3.09 GiB |
| Hugging Face、Puppeteer、npm 等可重建缓存 | 多目录 | 约 6 GiB |
| Go、Python、Playwright、pip、pnpm 等编译/包缓存 | 多目录 | 约 3 GiB |
| 已安装或已替代的 SDK/项目压缩包 | 5 个 | 约 0.62 GiB |
| 早期游戏展示视频及目录 | 12 个文件 | 约 0.52 GiB |
| 已被当前框架替代的 APK | 23 个 | 约 3.64 GiB 逻辑大小 |

## 保留内容

- 当前 `Builds/MateSoul_Framework.apk`
- `MateSoul_ClockTimeFix_20261029.apk`
- 近期键盘焦点、深度兜底、物品接近和当前综合回滚 APK
- 投掷/掉落边界专项包
- 启动性能基线、优化包和性能归因数据
- 当前生产 Unity `Library`
- PICO SDK、模拟器系统镜像和当前 AVD 数据盘
- 原始模型、动画、材质、Prefab、Blender 文件和正式交付 ZIP
- ArtSource 中刚被外部任务更新的两个 Unity `Library`
- 内容不同的两个动画师交付文件夹
- 正在被 Python 使用的 `uv` 缓存
- 正在运行的 TRAE、Lark、Chrome、微信、Unity Hub 和豆包缓存
- `Movies` 下的 MateFairy 宣传片与剪辑工程

## 完整性验证

- 清理前后 Git 工作区状态完全一致。
- 当前框架 APK、近期关键 APK 和性能测试材料均保留。
- 候选文件与 Git 跟踪文件交集为 0。
- 清理过程未发现 Unity、Blender、模拟器或 Gradle 任务。
- 没有删除源码、测试脚本、渲染脚本或生产资产。

## 剩余大项说明

当前剩余主要占用来自：

- `~/Library/Application Support` 中的活跃应用数据
- 当前 PICO SDK 与模拟器
- MateSoul 生产 Unity 缓存
- ArtSource 原始资产与检查点
- 正在运行应用的缓存

这些目录继续清理会造成重新下载、构建变慢、会话状态丢失或当前测试风险，因此本轮停止在可重建且不影响当前开发的边界。
