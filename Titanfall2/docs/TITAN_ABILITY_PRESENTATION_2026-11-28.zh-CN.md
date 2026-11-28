# 泰坦技能表现排查与修复（2026-11-28）

## 范围与证据

脚本以 `D:\northstar\scripts` 为准，不把 `D:\northstar\mods` 下的自定义覆盖当成原版逻辑。模型、PCF 和 Miles 事件以本机 Steam Titanfall 2 的 VPK、`r2/sound/general.mbnk` 实测为准。脚本里写了名称、资源能解码、s&box 实际渲染/播放，是三种不同的验证层级。

本轮改表现接线，不改技能伤害、位置判定、弹药返还、冷却、冲刺、AI 出招时机或玩家装备机制。没有覆盖 Steam 游戏目录。

## 帝王：重备武装

- `D:\northstar\scripts\weapons\mp_titanability_rearm.txt:20,43–62,109–113` 指向 `atpov_titan_monarch_pods.mdl`，启用副武器起止/充能动作，常规充能 0.5s。`D:\northstar\scripts\vscripts\weapons\mp_titanability_rearm.nut:12–33` 刷新副武器以及玩家推进能量，明确跳过实际开火动作。这不是升级核心，也不是 XO-16 的换弹动作。
- 第一人称实物是 `models/weapons/titan_bt_rocket_pod/atpov_titan_monarch_pods.mdl`，包含 `titan_l_pod` 和 `titan_r_pod` 两组网格；使用自身 `CAMERA_BASE`。播放 `draw_seq → charge_seq → holster_seq`，保留主武器，不执行 `attack_seq` 上的枪口或抛壳回调。还原 `rp_monarch_pullout` / `rp_monarch_holster`，从武器脚本取充能/激活提示，不再误播 Monarch Upgrade Core。
- 第三人称序列来自 Vanguard include `models/titans/medium/titan_medium_sp_core.mdl`：`mt_rearm_out → mt_rearm_charge → mt_rearm_in`。左右 `def_l_missile_root` / `def_r_missile_root` **以旋转**展开，不能只测骨头平移。机械声用 MDL `AE_CL_PLAYSOUND` 对应的 deploy/retract Start、Servo、Stop 3P 事件；本地驾驶员不会同时听见这套旁观者混音。
- 原生检查发现原来的角色动画图把上半身技能复制进了全身状态机；每个角色动作双槽，帝王因此触发 s&box `StateMachine supports at most 64 states` 并回落到旧序列。现在有上半身蒙版的模型由技能层独占这类动作，不再重复占用全身槽位。
- 手动触发使用独立的复制时间戳；AI 保留原有准备/提交/恢复时间线。离舱、角色失效或技能结束要回收临时肩架。现有挂载仍在技能接受时立即恢复资源，本轮只补表现；未移植 `rapid_rearm` 的 0.2s 变体、原版拒绝/冷却就绪提示或整套逐帧武器状态机。

## 七机体审计矩阵

| 机体 | 本轮实际接入 | 已确认而仍未完整接入 |
| --- | --- | --- |
| 帝王 | 重备武装双侧肩架、两种视角的机械/激活音效；火箭齐射发射和飞行声；能量虹吸射击音效；升级核心驾驶员/世界激活声 | 齐射与虹吸独立肩部开火动画、肩部光效；升级核心驾驶舱 `P_xo_shield_up`、原版 `states` 切换 |
| 离子 | Laser Shot 正确的 1P/3P 发射声；Vortex 能量颜色、开启/循环/空放或反射、拦截提示；Laser Core 充能/发射/循环/停止声 | 手动 Laser Shot 原版充能时序和肩架；Vortex 驾驶舱专用模型和捕获弹可视化；Laser Core 充能挂点、FP 光束和命中端光晕 |
| 强力（Tone） | 修复追踪火箭串用帝王齐射声；索敌声呐发射/到达声以及 `P_ar_sonar_CP`（CP1 为持续时间和缩放）；Salvo Core 1P/3P 音效 | 追踪火箭与声呐各自的肩架动作、锁定失败提示；Salvo Core 双发射口模型和火花；粒子墙保持现有实现 |
| 烈焰 | Firewall 使用自身起手声而非主武器声；燃烧陷阱独立视角音效；Thermal Shield 起止/持续/拦截反馈；Flame Core 补驾驶员起手声 | Firewall 拳部动作与 `P_wpn_meteor_wall` 的地面传播；Flame Core 分轨发声和相邻控制点连接；Thermal Shield FP 专用视模 |
| 北极星 | 集束导弹两视角发射声；VTOL 起飞声、悬停循环和四处推进器挂点的原版大/小喷焰；Flight Core 起飞和逐枚火箭发射声 | 集束弹持续爆点、肩架动作；Flight Core 本身的驾驶舱挂载、喷口和下降生命周期未完整接线；VTOL 落地不能以计时结束冒充；绊线陷阱保持现有实现 |
| 浪人 | Arc Wave 两视角发射声；剑格挡拔剑/实际拦截反馈（无伪造环形护盾）；Phase Dash 进入和离开声；Sword Core 去除双重激活并分配近战混音 | Arc Wave 的独立拔剑/弧刃与落地电效；完整剑格挡视模及动画；本机快照未找到脚本声明的 `P_phase_shift_main_XO`，保留现有裂隙效果；`Stryder.Dash` 也未通过本机 Miles 查询，不用其他冲刺声替代 |
| 军团 | 近/远切换分别使用 `weapon_predator_rangeswitch_toshort_*` / `...tolong_*`；两种 Power Shot 发射声；Gun Shield 血量着色、弹种命中与破盾；Smart Core 补驾驶员激活声 | Power Shot 预转姿态/枪口火花、枪盾独立 FP 模型；Smart Core 锁定/丢锁提示、持续声、准星和枪口光效 |

离子的激光绊雷另外接回原版 pylons 模型、`P_wpn_lasertrip_base` 与拆除声/爆炸 PCF。挂载仍是一个接近触发的模拟体，不等于零售版多桩射线网络；不添加虚假光束。火箭技能的原始 `.mdl` 路径现在转为挂载 `.visual.vmdl`，否则引擎会拒绝加载该模型。

## 代表性原始入口

- `D:\northstar\scripts\vscripts\weapons\mp_titanability_ammo_swap.nut:120–135`：军团切换前后的两套音效。
- `D:\northstar\scripts\weapons\mp_titanweapon_tracker_rockets.txt`：Tone 显式的 burst-start 音效优先于从 `#base` 继承的单发音效。
- `D:\northstar\scripts\vscripts\weapons\mp_titanability_laser_trip.nut:14–25,273–277`：模型、基础 PCF、销毁声和爆炸。
- `D:\northstar\scripts\vscripts\weapons\sh_sonar.gnut:8–42`：声呐 PCF 以及 CP1 用法。
- `D:\northstar\scripts\vscripts\weapons\mp_titanability_hover.nut:20–41,54–123,161–195`：起飞、悬停、推进器和原版下降/着地分段。
- `D:\northstar\scripts\vscripts\weapons\mp_titancore_lasercannon.nut:15–25,200–281`：Laser Core 的视角和生命周期音效；`mp_titancore_flight_core.nut:104–158` 对应 Flight Core。
- 手持防御技能的脚本逐项映射、音效/PCF 判定见同目录 `TITAN_DEFENSE_PRESENTATION_2026-11-28.md`。

## 残余与验证边界

- 尚未支持所有原版 PCF operator、RUI 程序或 Miles 动作。例如部分 `core_ability_land_*`、浪人 Sword Core 结束声的事件存在但当前 Miles 解码器不接受；没有用无关提示音冒充。
- 座舱/世界与 AI 的脚本、数据和原生夹具验证不等于真实多客户端联机，也不等于扬声器听测。进地图后仍应人工验收左右肩架的材质和视野比例、防御音效持续停止，以及声呐/焰流的最终画面。

## 本轮验证结果

- 官方 `MenuBuild.exe` 沙盒编译通过；[构建包](D:/sbox/sbox-mounts/artifacts/titan-ability-presentation-20261128/package/package.local.titanfall2.dll) SHA-256 `B3A354DC2A59F9276C8680DC91B226C6DBD8F9DBBD3F6B6466DBB1982E6EA5A6`。
- `FullyQualifiedName~Titan` 筛选 **79 通过、0 失败、0 跳过**；零售数据检查开启。跟踪证据包含重备武装 MDL、实际 Miles 音源、四组 PCF 资源、炮弹挂载路径。
- 原生 [`--titan-cockpit`](D:/sbox/sbox-mounts/artifacts/native-9bfbdcf9d1514f009eb57b1065b64833/runtime-results.json) 通过：驾驶舱 `draw_seq → charge_seq → holster_seq`、离开表现窗口销毁肩架、第三人称左右导弹架各旋转 **132.18°**。原生 [`--titan-ion`](D:/sbox/sbox-mounts/artifacts/native-b22df58e575e42f89c12c2cb3fd29156/runtime-results.json) 通过。
- 两个更广的原生套件**未全过**。[`--cores`](D:/sbox/sbox-mounts/artifacts/native-e68e2144a57f4089b4fc10dd9222af68/runtime-results.json) 的离子核心子项通过；在军团 Smart Core 的主炮测试中，夹具直接调用驾驶员射击，未建立当前 Predator 预转就绪条件。[`--titan-ai`](D:/sbox/sbox-mounts/artifacts/native-7651c57d4c02429dbb6001c52df25cb4/runtime-results.json) 的 AI 帝王重备武装、军团切换与北极星正常升降子项通过；最后用敌方北极星直接 `TryEmbark`，未通过现有友方认领限制。这两处没有通过放宽判定来绕过。
- 没有对 Steam 安装执行替换；没有声称做过双端联机或扬声器人工试听。
