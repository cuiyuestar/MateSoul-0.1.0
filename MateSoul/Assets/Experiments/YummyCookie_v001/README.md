# Yummy 曲奇进食 v001（隔离实验）

以无衣领 v10 ShadowFabric 为只读基线。原角色、原动画、服装、头发/衣袖物理脚本及盘子和曲奇的源模型均不覆盖。

## 使用

打开本目录的 `YummyCookie_v001.unity` 并进入 Play Mode。
角色从盘中抓取一块曲奇、送入口中，曲奇在入口时消失，随后保留原来的咀嚼动作。
初始盘中 14 块，取走后剩 13 块。

- `Space`：暂停/继续动作时间。
- `R`：重播并恢复盘中曲奇。
- `YummyCookieDemo.loop`：控制是否自动循环；默认循环。

## 动作时序

以 30 fps、从 0 开始计帧：

| 演示帧 | 原动画帧 | 行为 |
|---|---|---|
| 0–23 | 保持 0 | 0.8 秒下探抓取衔接，手指由张开变为捏取 |
| 24 | 0 | 同一个曲奇对象从盘中转为右手子物体，无位置跳变 |
| 25–45 | 1–21 | 固定手掌局部位姿跟随，逐渐接近嘴部 |
| 46 | 22 | 手指开始松开时隐藏曲奇 |
| 47–196 | 23–172 | 继续原版咀嚼和后续动作 |

原 173 帧身体动画按原时序播放。入口阶段仅在该场景的角色实例上补充轻微张嘴，
不改写原 AnimationClip。原动画第 0 帧已处于捏取手势，因此新增前导动作让抓取过程可见。

## 文件职责

- `YummyCookieDemo.cs`：唯一动作时钟、前导手臂衔接、曲奇父级切换/隐藏、循环恢复与截图验证。
- `Editor/YummyCookieBuilder.cs`：读取动画关节/嘴部坐标、创建独立场景与 Prefab、构建独立 Player。
- `Props/`：原始道具的运行时副本；曲奇将程序化材质烘焙为 BaseColor/Normal，
  合并为一个渲染网格，仍保持 50 mm 直径。盘子保持 200 mm 直径。
- `Lengyue_YummyCookie_v001.prefab`：该演示的角色副本，引用原版只读网格与材质。

完整分析、准备脚本、成片和报告位于：

`/Users/bytedance/MateSoul/ArtSource/SapphiArt/Animation/YummyCookie_v001/`

## 构建与验证

Unity 菜单：`MateSoul > Experiments > Yummy Cookie v001 > Build Isolated Demo Player`。
Player 输出到上述产物目录的 `build/YummyCookie_v001.app`。

普通启动为循环演示；加 `--cookie-capture` 参数时输出 197 帧截图和
`reports/replay.json`，完成后自动退出。

更改 C# 后，应先完成 Unity 脚本重载再构建。命令文件接口位于项目根目录
`.dbg/yummy-cookie-v001/`：`refresh` 只刷新；编译完成后使用 `analyze` 或 `build`。
不要在脚本尚未重载时直接发出构建请求。

`reports/replay.json` 检查抓取连续性、手掌跟随、盘内实际实例数量、入口隐藏、
回退重播恢复以及头发/袖子初始化。嘴部接触距离使用曲奇圆柱近似和面部嘴形
顶点中心，不代表任意姿态的精确网格碰撞证明。

## 回滚

删除本目录 `Assets/Experiments/YummyCookie_v001/` 及同名 `.meta` 即可移除实验。
原生产 Prefab、场景和动画仍在原路径，不需要重新构建原版。
`ArtSource/.../YummyCookie_v001/` 只包含这个实验的输出，可以保留归档或一起删除。
项目构建场景列表未追加本场景。
