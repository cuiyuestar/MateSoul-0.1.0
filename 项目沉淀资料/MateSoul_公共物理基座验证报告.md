# MateSoul 公共物理基座验证报告

最后更新：2026-10-05

## 验证结论

- Unity 版本：2022.3.38f1。
- 自动化回归：95/95 通过，失败 0，跳过 0。
- Android Development APK：构建成功，ZIP 完整性校验通过，APK Signature Scheme v2 校验通过。
- ABI：仅 `arm64-v8a`；脚本后端为 IL2CPP。
- 本轮按要求未执行压力测试，也未在 PICO Space Pro 真机上验证性能和手感。

## 覆盖范围

| 测试组 | 数量 | 结果 |
|---|---:|---|
| ActionCoordinatorTests | 14 | 通过 |
| BedInteractionTests | 9 | 通过 |
| GameRuntimeTests | 23 | 通过 |
| GameplayTests | 14 | 通过 |
| JsonGameSaveTests | 2 | 通过 |
| PhysicsRuntimeTests | 5 | 通过 |
| RealityItemTests | 3 | 通过 |
| SeatingTests | 19 | 通过 |
| TelevisionTests | 6 | 通过 |
| 合计 | 95 | 全部通过 |

已验证的物理与生命周期边界包括：

- 不同质量在相同冲量下的速度差、重力、摩擦、休眠和高速落地防穿透。
- 组合碰撞体共享单一物理材质，拒绝子级重复 Rigidbody 和非法物理参数。
- 抓取跟随受碰撞约束，释放继承求解后的线速度和角速度；跟踪丢失、暂停和删除会取消抓取。
- 角色为动态刚体，能够推开轻物体、通过浅坡，且不能穿过未录入 NavMesh 的实体墙。
- 床尾及左右侧进入/退出、床头板规避、净空不足拒绝、出口阻塞后保持原姿态并支持重试。
- 批量恢复验证未知定义、重复 ID、非法旋转、真实穿插、无支撑、反向支撑顺序和中断下落。
- 暂停取消锚点恢复后自动重试；恢复期间 UI、AI 和自治动作统一拒绝。
- 存档家具占据默认出生点时，先恢复布局，再把角色放到邻近可导航、安全且有支撑的位置。
- 主地面物品使用房间地面高度，不使用 NavMesh 表面偏移；桌上物体按真实桌面高度放置。
- HUD 删除、电视鼠标/XR 射线与实体按钮按压回归通过。

## 产物

- 测试结果：`/Users/bytedance/MateSoul/.artifacts/framework/unity-tests.xml`
- 测试结果 SHA-256：
  `736aebe1f5fd6a172fb8b4618dc217d0498b13ce1c08f249cc7d039990a5796a`
- APK：`/Users/bytedance/MateSoul/Builds/MateSoul_Framework.apk`
- APK 文件大小：82,433,205 bytes
- APK SHA-256：
  `76254650afdb2f5d628ddacb774b14f295a20379c338d3a04aa8313d80d4b1f3`

## 未覆盖项

- PICO Space Pro 真机帧率、PhysX CPU 时间、温度、功耗和最大活动刚体数量。
- 真机手部跟踪噪声、控制器抓取手感、复杂空间网格和真实空间锚点漂移。
- 长时间运行及大规模刚体压力测试。
