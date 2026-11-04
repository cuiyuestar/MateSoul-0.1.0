# MateSoul 最小运行资产清理记录

日期：2026-10-29  
范围：删除不影响现有 APK 正常运行的项目中间产物

## 最终结果

- 本阶段清理前：46.67 GiB
- 本阶段清理后：19.19 GiB
- 本阶段项目目录减少：27.48 GiB
- 删除文件：35,930 个
- 当前磁盘可用空间：约 116 GiB
- 当前保留 APK：`Builds/MateSoul_Framework.apk`
- 当前 APK SHA-256：
  `bc99b96466d628ee2607fc9b6cdfa0c8743d1498c22a169b4c719ed5cc2a511a`

从最初接近 90 GiB 到当前约 19 GiB，MateSoul 项目目录已减少约 70 GiB。

## 为什么 APK 只有一百多 MiB

APK 只包含设备运行所需的 IL2CPP 二进制、压缩贴图、模型、音频和配置。开发目录还会包含：

- 未压缩源资源
- Unity 导入数据库与构建缓存
- Android/IL2CPP 中间文件
- PICO 模拟器、系统镜像和虚拟数据盘
- 美术源文件、历史版本和回滚工程
- 多个测试 APK
- 日志、截图、录屏和验证产物

这些内容不会全部进入 APK，因此开发目录远大于最终安装包。

## 本阶段删除内容

| 类型 | 逻辑大小 |
|---|---:|
| 本地 PICO 模拟器、系统镜像和 AVD | 约 17.3 GiB |
| Unity 项目 `Library` | 约 2.2 GiB |
| 历史 HairLab 工程 | 约 2.1 GiB |
| 旧角色 work/review/log/checkpoint | 约 0.8 GiB |
| 旧 eye/chin/sleeve/body/appearance 中间版本 | 约 2.0 GiB |
| 旧 QualityUpgrade 与 appearance rollback | 约 1.5 GiB |
| 除当前框架包外的 APK | 约 2.1 GiB |
| PICO CLI workspace 与隔离 Unity home | 少量 |

## 保留内容

- `MateSoul/Assets`
- `MateSoul/Packages`
- `MateSoul/ProjectSettings`
- 当前框架 APK
- `Unity6Study`
- `eye_shape_reference_v12_dcc_real` 及全部 Fix
- 原始 `inputs`
- 正式 `deliverables`
- `Lengyue_ProductionMerge_20261022`
- `Lengyue_ProductionMerge_20261026`
- `Lengyue_ProductionMerge_20261029`
- 当前 Direct59/Direct56/Direct57/Direct54 生产角色资产
- SapphiArt 当前道具、服装和动画源文件
- XR、Dependencies 与服务源码
- Git 历史
- 正式技术文档、性能数据和资产交付包

## 影响说明

- 现有 APK 的安装和运行不受影响。
- Git 状态与清理前完全一致。
- 下次用 Unity 打开项目时会完整重建 `Library`，首次导入时间较长。
- PICO 模拟器已删除；如需模拟器测试，必须重新下载并创建 AVD。
- 旧专项 APK 已删除，如需对应版本只能重新构建或从版本归档恢复。
- 未执行 Unity 构建验证，因为构建会立即重新生成大量已删除缓存。

## 当前剩余构成

| 目录 | 约占用 |
|---|---:|
| `ArtSource` | 15.0 GiB |
| 生产 Unity 源工程 | 1.7 GiB |
| 正式项目资料 | 1.1 GiB |
| Git 历史 | 0.8 GiB |
| XR 与依赖 | 约 0.4 GiB |
| 当前 APK 和服务源码 | 约 0.2 GiB |

当前约 19 GiB 已接近保留完整原始资产、Fix 历史、生产源码和正式交付资料时的合理下限。
