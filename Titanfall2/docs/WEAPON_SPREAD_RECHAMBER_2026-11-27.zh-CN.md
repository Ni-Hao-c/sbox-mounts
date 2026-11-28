# 连续扩散与 Kraber 上膛

## 原版证据

IDA 当前 `client.dll` 基址为 `0x180000000`。

- `spread_time_to_max` 描述表 `0x1809459A0`：`+16` 浮点缺省为 **0**，`+30` 参数偏移为 `0x570`。脚本缺省和显式 0 都采用逐发扩散，不推测非零值。
- `0x1805BD7A0`：连续扩散开启时跳过逐发增量及 decay timestamp；否则 hip 仅在 zoom < 0.5 时增加，ADS 始终增加。
- `0x1805BD5B0 / 0x1805BD8C0`：第一发记下开始时刻与现有 kick/max 比例；后续按 `clamp(elapsed/timeToMax + initialFraction,0,1) * 当前姿态max` 更新，连续开火不重置计时。ADS 时 hip 不增加，仍可衰减。
- `0x1805BD520 / 0x1805B3F80`：停止只清 firing run，保留 kick；连续模式未刷新逐发 decay timestamp，停止后的残余通常立即衰减。
- fire_mode 表 `0x180B33728` 依次为 `auto`、`semi-auto`、`offhand`。`0x1805B48C0` 仅在 mode=1 且 `rechamber_time>0` 设置 needs-rechamber。
- `0x1805B48C0 / 0x1805B3C10`：先经过正常发射间隔，再启动 activity 309，上膛的 next-primary = 当前时刻 + rechamber_time；不是取两者最大值。
- `0x1805A55…` 事件 77 清除 needs-rechamber；事件 76 调 `0x18059CEE0`，重设 next-primary 至正常 rate 允许的最早时刻。因此 READY 不能作为额外的更晚门槛。
- `0x1805AA000` 的 activity `0x135`（309）返回 `rechamber_time`；`0x1805AA7A0` 返回 `自然片长 / 目标时长`，上限 12，作为动画播放速率。不能把 MDL 的自然片长直接当游戏中上膛时长。

`D:/northstar/scripts/weapons/mp_weapon_sniper.txt` 定义 fire_rate=1.85、rechamber_time=1.60，缺省 spread_time_to_max 未显式覆盖。

原版 `models/weapons/at_rifle/ptpov_at_rifle.mdl` 审计：

- `rechamber_seq` / `ACT_VM_RECHAMBER`，hip animation 47、ads_blend 对应 animation 48。
- 64 帧、30fps、delta，原始片长 2.1 秒。
- `AE_WPN_RECHAMBER` = 28/63 cycle，即 0.93333 秒。
- `AE_WPN_READYTOFIRE` = 50/63 cycle，即 1.66667 秒。
- 原始音效与 shell_eject 事件随上膛片段保留。

以上是源 MDL 时间。Kraber 的播放速率实际为 `2.1 / 1.6 = 1.3125`，完整上膛播放 1.6 秒；bolt 为 `1.6 * 28/63 = 0.711111` 秒，READY 为 `1.6 * 50/63 = 1.269841` 秒。

实物审计日志：`D:/sbox/_codex-build/rechamber-audit-20261127/audit.txt`。

## 实现边界

开火门槛、bolt 状态和动画片长分别保存：Kraber 的默认 next-primary 在上膛开始后 1.6 秒，但实际 READY 在 1.269841 秒提前解除它。完整动画仍可播放至 1.6 秒；新的合法开火直接替换剩余上膛动画。bolt 事件前收枪会保留待上膛状态，重新持枪重做；bolt 事件后收枪保留剩余射击门槛，不重做 bolt。取消装填不清除待上膛状态，完整空仓装填清除它。

hitscan 和 projectile 均在 host 扣弹/伤害前检查状态。连续扩散只在确认的发射后启动，释放输入结束 run，排队 burst 保持 run；额外逐发 kick 不会叠加到连续 ramp。

测试位于 `Tests/WeaponContinuousSpreadTests.cs`、`Tests/WeaponRechamberTests.cs`；完整编译、官方沙盒校验与 native acceptance 由整合任务统一执行。
