# MateSoul 项目内部空间清理记录

日期：2026-10-29  
范围：`/Users/bytedance/MateSoul`

## 为什么之前接近 90 GiB

MateSoul 不是只有游戏源码，目录内同时包含：

- Unity 生产工程及 Android/IL2CPP 构建缓存
- 完整 PICO 模拟器、系统镜像和 AVD 数据盘
- ArtSource 原始模型、多个历史版本、检查点和独立验证工程
- 历史 `.artifacts`、`.dbg`、`.unity-tmp`
- 多个 APK 与正式交付包

清理前主要大项为：

| 目录或内容 | 约占用 |
|---|---:|
| `ArtSource` | 29.7 GiB |
| `.pico-home` | 20.4 GiB |
| 生产 Unity 工程 | 19.4 GiB |
| `.artifacts` | 6.4 GiB |
| 旧独立角色集成工程 | 4.2 GiB |
| APK、文档和其他依赖 | 其余空间 |

## 本轮项目内部清理

- 项目目录清理前：77.44 GiB
- 项目目录清理后：46.67 GiB
- 项目目录减少：30.77 GiB
- 磁盘可用空间实测增加：15.29 GiB
- 清理后整盘可用空间：约 105 GiB
- 删除文件：75,631 个

逻辑大小与实际释放量不同，是因为 APFS 克隆块和历史 APK/Unity 缓存存在共享存储。

## 删除内容

| 类型 | 逻辑大小 |
|---|---:|
| Unity Android/Bee 构建缓存 | 13.81 GiB |
| Unity PlayerDataCache | 1.27 GiB |
| Burst、Shader 与 BuildPlayerData 缓存 | 0.41 GiB |
| 历史 `.artifacts` | 6.33 GiB |
| 历史 `.dbg` | 0.35 GiB |
| 历史 `.unity-tmp` | 0.08 GiB |
| 已被 Direct59 生产合并替代的 Direct41 独立工程 | 4.19 GiB |
| 旧美术 review Unity 缓存 | 1.54 GiB |
| 旧 checkpoint `replay.tar.gz` | 2.63 GiB |

## 保留内容

- 当前 `MateSoul/Assets`、`Packages`、`ProjectSettings`
- Unity `Library/Artifacts` 与 `Library/PackageCache`
- 当前 APK 与近期关键验证 APK
- 完整 PICO 模拟器、系统镜像和 AVD 数据盘
- `Unity6Study` 与全部 Fix 源目录
- `Lengyue_ProductionMerge_20261022`
- `Lengyue_ProductionMerge_20261026`
- `Lengyue_ProductionMerge_20261029`
- 当前 Direct59/Direct56/Direct57/Direct54 生产角色资产
- 原始 Blender、VRM、FBX、贴图和正式交付 ZIP
- 性能测试、真机验收和项目技术文档

## 影响说明

- 没有新增受版本控制文件修改或删除。
- Git 状态差异仅为未跟踪的 `.unity-tmp/` 被清理。
- 当前源码、模型、动画、Prefab 和测试逻辑未修改。
- 下一次 Android 构建需要重新生成 Bee、Burst、Shader 和 PlayerData 缓存，首次构建会变慢。
- 不应为了验证清理结果立即执行完整 Unity 构建，否则会重新占用已释放空间。

## 清理后主要剩余项

| 目录 | 约占用 | 保留原因 |
|---|---:|---|
| `ArtSource` | 20.7 GiB | 原始模型、Fix 历史与生产合并备份 |
| `.pico-home` | 17.3 GiB | 当前模拟器、系统镜像与 AVD |
| 生产 Unity 工程 | 3.9 GiB | 当前源码、导入资产缓存和包缓存 |
| `Builds` | 2.3 GiB | 当前与关键回归 APK |
| `项目沉淀资料` | 1.1 GiB | 正式交付与验证资料 |

进一步显著缩减只能删除 PICO 模拟器/AVD 或原始美术历史，这会直接影响当前测试能力或资产回滚，因此未继续处理。
