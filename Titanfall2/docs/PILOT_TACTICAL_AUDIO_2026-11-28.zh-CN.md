# 铁御技能音频与 s&box 生命周期适配（2026-11-28）

此次按用户明确要求保留 s&box 运动/技能逻辑适配，复用 Titanfall 2 的音频与屏幕资源，不继续以逆向完整原生运动作为前提。

## 兴奋剂

- 激活：武器脚本的 `pilot_stimpack_activate_1P/3P`。
- 持续：`sh_stim.gnut` 的 `pilot_stimpack_loop_1P/3P`，使用可主动停止的 `Titanfall2SoundEventEmitter` sustain voice。
- 尾音：`sh_stim.gnut` 在 `duration - 2.0` 播放 `pilot_stimpack_deactivate_1P/3P`，对应本实现剩余 2 秒触发一次。若提前取消且尚未播放尾音，则取消时播放一次。
- 自然结束、换装、禁用、销毁均停止循环声音。切换第一/第三人称会重建正确声部，但保留本次激活的尾音已播放状态，避免重复。
- 3P 身体效果仍使用 `P_pilot_stim_hld`；第一人称屏幕粒子由独立屏幕效果组件生命周期管理，避免在声音 presenter 中重复创建 `P_heal`。

## 抓钩

`general.mbnk` 确认的精确事件：

| 阶段 | 本地第一人称 | 第三人称 |
| --- | --- | --- |
| 发射尝试 | `pilot_grapple_fire` | `pilot_grapple_fire_3p` |
| 已附着移动 | `pilot_grapple_traverse_1p` | `pilot_grapple_traverse_3p` |
| 回收 | `pilot_grapple_retract_1p` | `pilot_grapple_retract_3p` |
| 复位 | `pilot_grapple_reset` | `pilot_grapple_reset_3p` |

有资格的发射尝试即播放 fire，打空不再静音。失败不扣技能充能、不启动 traverse；一次失败尝试后有 0.2 秒请求节流，避免按键连发导致音频堆积。成功结束、主动取消或换装回收时，停止 sustain、播放 retract，并在 0.12 秒后播放 reset。**0.2 秒节流和 0.12 秒复位排程是 s&box 适配时序，不宣称原生时序。**

挂接、碰到不可挂表面、拔出分别读取挂载资源的：

- `scripts/impacts/grapple.txt`
- `scripts/impacts/grapple_disallowed.txt`
- `scripts/impacts/grapple_extract.txt`

使用场景碰撞表面 `gamematerial`，并优先识别人体、机器人和泰坦材质。第一人称选择 `Sound_attacker`，其他视角选择 `Sound`。这些表中 `Concrete.Grapple.Impact_1P_vs_3P` 等旧点号事件与 Miles 的下划线名称对应；播放前仍要求归一化后的**精确事件**存在，缺失则输出诊断，绝不搜索近似名称或随意换声。

抓钩绳及运动仍是适配逻辑；音频改造不会把其声称为已移植的原版原生实现。

## 验证入口

`Tests/PilotTacticalResourceTests.cs`：

- `RetailTacticalAudioUsesExactAudibleEvents` 检查事件和非静音音量层，包括 reset。
- `GrappleImpactTablesResolveExactMilesEventsForAllMaterials` 从实际安装 VPK 读取三张材质表，验证对应精确 Miles 事件。
- `StimAndGrappleEventsDecodeToNonSilentRetailPcm` 检查 18 个代表事件，去重读取其可听层的真实 BCF 并解码非零 PCM。

这些测试需要 `TF2_GAMEPLAY_TEST_GAME` 指向游戏安装；资源与 PCM 检查不能替代运行时声部停止、3P 切换、听感和屏幕效果验收。测试运行结果以本轮最终验证报告为准。
