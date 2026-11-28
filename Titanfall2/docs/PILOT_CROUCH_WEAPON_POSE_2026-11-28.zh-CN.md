# 普通蹲姿的第一人称持枪表现（2026-11-28）

泰坦陨落 2 有带 `crouch` 的第一人称动画选择，但对本次核对的 Hemlok、CAR、R-101、R-97、EVA-8、L-STAR、DMR、Wingman、Arc Launcher 和 Satchel，普通站立与蹲姿的待机序列使用同一组动画索引，无额外姿态 autolayer。Hemlok 的 `idle_seq` / `idle_seq_crouch` 都用 `[34,35]`（`idle_anim` / `idle_ads_anim`）。静蹲腰射不会凭空改成滑铲时的斜握。枪械确有其他 `crouch` 动作变体：例如 Hemlok 的 `reload_crouch_seq`、`draw_crouch_seq`，以及底层素材名带 `slide` 的 `ads_in_crouch_seq`；本次没有把这些动作变体等同于静蹲待机，也没有改动装弹、开镜或滑铲的序列选择。

实际缺失的是视模型随蹲姿相对镜头轻微下降。`scripts/players/mp/pilot_base.set` 的 `viewmodelDuckOffset` 是 `-0.75` 个模型单位。原版 `client.dll`（SHA256 `002B36487FEC7C98882929FBCCDB506D0146BBF3270CBB964A4D21C5EDF7EEBC`）玩家设定条目 `0x180B09BD0` 绑定该字段到 `+0x90`；视模型函数 `0x1800D2020` 取该值，并按下式沿视角上轴加到视模型位置：

```text
视角上轴 × viewmodelDuckOffset × sin(π/2 × 蹲姿完成度) × (1 − 有效 ADS 完成度)
```

`0x1802C33D0` 作为原版蹲姿曲线来源：使用三次 smoothstep，全蹲为 1、站立为 0；反编译中蹲下常规窗口 400 ms、起身 200 ms，并存在快速下蹲的 200 ms 分支。挂载用 `PlayerController.IsDucking` 的实际蹲姿结果触发纯表现插值：未接管蹲键、碰撞体或蹲姿网络同步；如果顶到天花板仍不能起身，枪也保持蹲姿偏移。切换武器时从当前结果初始化，多个相机 pass 同一时刻不重复推进。现有准确的持枪 ADS 动画继续沿用；现有滑铲角度保持独立。挂载不复制原版移动控制状态机的快速分支、进度和第三人称手臂动画选择，因此过渡起点是 s&box 已确认状态切换时刻。

本地数据探针和反汇编摘录见 `artifacts/crouch-pose-20261128/retail-poses.txt` 和 `artifacts/crouch-pose-20261128/duck-offset-verified.asm`。`PilotViewStanceTests` 验证从腰射到 ADS 的偏移、进度反转与重复采样；`PilotParkourTests` 增加原版配置读取和继承覆盖检查。
