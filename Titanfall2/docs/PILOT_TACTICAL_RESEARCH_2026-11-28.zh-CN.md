# 铁御战术技能：原版资源与单槽拾取研究

研究日期：2026-11-28。目标工程：`D:\sbox\sbox-mounts\Titanfall2`。脚本参考：`D:\northstar\scripts`。

后续用户明确采用 s&box 逻辑适配并补充声音/滤镜复用；该轮的最新实现与验证见 [s&box 技能效果记录](PILOT_TACTICAL_SBOX_EFFECTS_2026-11-28.zh-CN.md)。本文下方构建结果保留首轮单槽拾取改造的记录。

## 范围与证据边界

现有挂载实现的四种技能是兴奋剂、钩爪、相位移动、脉冲刀。本次交互改造针对这四种：单个技能槽、世界物品拾取替换、T 激活、同类拾取不能刷新冷却、移除右下角多卡片 HUD，在左下角血量旁显示已装备技能图标。

原版另外三种战术技能为隐身、全息影像和 A-Wall；目前并未因为这次 HUD/物品改造而自动实现。七种技能的脚本和美术资源可作为后续实现依据。所有数值均指本机提取脚本中的 `MP_BASE` 或明确常量，不能当作所有补丁/服务器的最终平衡值；运行时应优先读取实际挂载内容中的武器脚本。`D:\northstar` 只用于研究，不是游戏运行时依赖。

## 七种技能与可复用资源

下表文件名省略共同前缀 `D:\northstar\scripts\weapons\`；图标共同前缀为 `rui/pilot_loadout/tactical/`。每个文件的第 9–10 行直接定义 `menu_icon` / `hud_icon`。

| 技能 | 武器脚本 | 原版图标后缀 | 脚本能证明的行为/资源 |
| --- | --- | --- | --- |
| 兴奋剂 | `mp_ability_heal.txt` | `pilot_tactical_stim` | `MP_BASE` 3 秒、200 单位消耗、8 单位/秒恢复；粒子 `P_heal`、`P_pilot_stim_hld`，Miles 声音 `pilot_stimpack_loop_1P/3P`、`pilot_stimpack_deactivate_1P/3P`。 |
| 钩爪 | `mp_ability_grapple.txt` | `pilot_tactical_grapple` | 容量 200，`GetSuitGrapplePower() >= 100` 才能触发；Squirrel 最终调用原生 `owner.Grapple( attackParams.dir )`。 |
| 相位移动 | `mp_ability_shifter.txt` | `pilot_tactical_phase_shift` | `MP_BASE` 2 秒、200 单位消耗、13.333 单位/秒恢复；`P_phase_shift_main`、`P_phase_shift_screen`、`P_phase_shift_player`、`P_phase_shift_screen_start`、`P_warpjump_FP`；调色文件 `materials/correction/mp_ability_shifter.raw`。 |
| 脉冲刀 | `mp_weapon_grenade_sonar.txt` | `pilot_tactical_pulse_blade` | 第一人称 `models/weapons/kunai/ptpov_kunai.mdl`、世界 `models/weapons/kunai/w_kunai.mdl`、投射物 `models/weapons/kunai/w_kunai_projectile.mdl`；命中粒子 `wpn_grenade_sonar_impact`。 |
| 隐身 | `mp_ability_cloak.txt` | `pilot_tactical_cloak` | `MP_BASE` 15 秒、200 单位消耗、10 单位/秒恢复；脚本入口调用 `EnableCloak( ownerPlayer, duration )`，不是普通透明度动画。 |
| 全息影像 | `mp_ability_holopilot.txt` | `pilot_tactical_holo_pilot` | 容量 200，每次 100，8 单位/秒恢复；原生 `CreatePlayerDecoy`，10 秒存活、50 生命值；粒子 `P_ar_holopilot_trail`，声音 `holopilot_deploy_1p/3p`、`holopilot_loop`、`holopilot_loop_enemy`、`holopilot_end_1P/3P`。 |
| A-Wall | `mp_weapon_deployable_cover.txt` | `pilot_tactical_hardcover` | 15 秒；粒子 `P_pilot_amped_shield`，碰撞模型 `models/fx/pilot_shield_wall_amped.mdl`；投掷物 `models/weapons/sentry_shield/sentry_shield_proj.mdl`。不是 `mp_titanability_amped_wall`。 |

详细源码定位：

- 兴奋剂：`scripts/weapons/mp_ability_heal.txt:35–47`；`scripts/vscripts/weapons/sh_stim.gnut:10–16,46–49,71–103`。速度增益 severity 为 `0.4`，脚本添加 `duration + 0.5` 的速度状态，并使用 `0.25` 的衰减阶段；不能把一个固定倍速计时器称作完全等同原版状态系统。
- 钩爪：`scripts/vscripts/weapons/mp_ability_grapple.nut:52–107`。脚本提供门控/调用，拉力、绳索约束、碰撞、充能回退主要在原生代码；现有 C# 摆荡公式属于适配实现。
- 相位：`scripts/vscripts/weapons/sh_phase_shift.gnut:16–38,107–143,247`。`PhaseShiftBegin`、相位实体查询、专用渲染可见性是原生功能。脚本包含相位返回位置的 telefrag 检测；原生 `GetEntityAtPhaseShiftExitPosition` 需要用 s&box 碰撞查询适配。
- 脉冲刀：`scripts/weapons/mp_weapon_grenade_sonar.txt:30–32,120–145`；`scripts/vscripts/weapons/mp_weapon_grenade_sonar.nut:23,97–100,140–170`；`scripts/vscripts/sh_consts.gnut:1023` 的 `SONAR_GRENADE_RADIUS = 1250.0`。原版扫描包含队伍、相位排除与客户端声呐显示，而非只播放圆形粒子。
- 隐身：`scripts/weapons/mp_ability_cloak.txt:36–47`；`scripts/vscripts/weapons/mp_ability_cloak.nut:20–23`。还需实现不同观察者的隐身可见性、开火破隐与 AI 判定，不能仅隐藏模型。
- 全息：`scripts/vscripts/weapons/mp_ability_holopilot.nut:5,136–156,182–201,246`；`scripts/vscripts/weapons/_weapon_utility.nut:233`。诱饵的移动、动画、AI 可攻击性以及友敌高亮均是玩法的一部分。
- A-Wall：`scripts/vscripts/weapons/mp_weapon_deployable_cover.nut:14–25,35–37,124–128,173–248`。普通 Cover 分支的 850 血不是 A-Wall 的血量；Amped 分支设置 1000 血、`CONTENTS_WINDOW`、`PTF_ADDS_MODS | PTF_NO_DMG_ON_PASS_THROUGH`、单向 `-0.55` 穿透门限和 `pass_through_amps_weapon`。需接入子弹/投射物穿越与增伤，不是一个双面挡子弹的墙体。

## 世界拾取外观

原版战术技能原本是装备栏能力，并非已有四个统一外观的地面拾取物。兴奋剂、钩爪和相位的 `playermodel` 是 `models/weapons/empty_handed/w_empty_handed_human.mdl`（分别位于对应武器脚本第 25、24、31 行），不能作为可见的地面物品。相位的 `ptpov_phaseshift_device.mdl` 是第一人称模型，也不能直接当作正确的世界拾取模型。

因此前三种技能使用现有原版背包作为可见容器、通过技能名称区分；脉冲刀可使用原版 `w_kunai.mdl`。这是本项目的拾取玩法适配，不宣称原版存在相同拾取外观。

## 挂载资源复用路径

- UI 图标：`Code/Resource/UiAtlas.cs:98–113` 将 RPAK atlas 的 `image.Name` 注册为 `ResourceType.Texture`；现有 UI 可从 `mount://titanfall2/rui/pilot_loadout/tactical/<图标名>` 加载原图。`rui_full/` 路径是保留原始 canvas 的另一注册方式；仅调用材质或把 RUI 文件当图片并不能替代 atlas 读取。
- PCF：`Code/Resource/Particle.cs:4–44` 将可发现的粒子定义暴露成 prefab，并创建 `Titanfall2ParticleResource` / `Titanfall2ParticleControlPoints`。使用前必须解析完整子粒子图、对应材质及控制点。成功找到名称不等于最终效果已经等同原引擎。
- 音频：复用项目现有 Miles 事件播放路径，按本地/远端选择脚本中的 1P/3P 别名；循环、停播和替换清理必须显式管理。
- 模型与动作：挂载可转换 MDL，但第一人称设备需要对应模型、骨架、动画事件、持枪切换时序配合。复用模型并不自动运行 Squirrel 或 Respawn 原生动作逻辑。

本工程没有自动执行 Respawn Squirrel 的运行时。资源文件可以复用，脚本中的原生方法需要用 s&box 实现相同行为；不能仅通过解包脚本承诺七种技能与原游戏完全一致。

## 本次效果改造与仍存适配

实现已接入兴奋剂和相位的挂载 PCF、循环/收尾音频；脉冲刀使用原版模型、轨迹/命中粒子与激活/扫描音，移除原先借用的 Tone 声音和自制 Burst。钩爪改用在 `general.mbnk` 中确认存在的 `pilot_grapple_fire`、`pilot_grapple_traverse`、`pilot_grapple_retract` 事件。使用这些资源仍不意味着全套原引擎效果已经复刻。

脉冲刀的基础参数改为原脚本的 3500 发射速度、2 倍重力、4.5 秒扫描、1250 范围、对泰坦 100 伤害和 25 秒恢复。兴奋剂基础持续/恢复为 3/25 秒；相位取消了无脚本依据的额外 1.15 倍移动速度。

仍存适配：兴奋剂速度、回血、FOV 和额外 `duration + 0.5` 速度尾段并未全部复刻原生状态系统；钩爪运动与绳索仍是 s&box 适配；相位屏幕 PCF 需要最终画面验收；脉冲刀检测使用周期 NPC 扫描，而不是原版连续触发器和完整多人声呐系统。隐身、全息影像与 A-Wall 本轮仅研究，没有增加到可用技能列表。

## 验收清单

`Validation/PilotParkour.cs` 的战术段已改为通过挂载物品 prefab 和 `Component.IPressable.Event`/`Press` 测试装备，而非旧的 T 循环切换。它是原生运行时接口测试，不是物理键盘 E 的人工验收。

1. 无 Jump Kit 拾取失败且物品保留；只有 Jump Kit 时技能槽为空，T/M3 保留宿主行为。
2. 四种 prefab 都按 `item` 注册；拾取成功只产生一个 tactical controller，物品被消费。
3. T 激活所装备技能且不改变种类，M3、Q、C 保留；相同技能拾取失败，不刷新充能/持续时间。
4. 活跃 Stim 换 Grapple 停止增益；活跃 Grapple 换 Phase 解除旧拉力/绳索；换装不能保留旧技能同时生效。
5. Stim FOV、Grapple 实际拉动、Phase 武器锁定/碰撞隔离/退出 telefrag、Pulse 声呐目标标记继续受测。
   已增加拾取 Pulse 后死亡恢复单个 Pulse 槽，以及卸下 Jump Kit 清空技能和持久状态的回归断言。
6. 单元测试、官方 `MenuBuild.exe` 白名单编译、Validation 工程编译分别记录；运行原生场景和截图验收不能被编译成功代替。
7. 仍需实际画面验证原版图标、血条旁布局、不同分辨率、粒子完整性、1P/3P 声音循环，以及多人下拾取竞争、装备同步与远端显示。

## 原生行为验证结果

新增独立入口 `--pilot-tactical`（`Validation/PilotTactical.cs`，`Program.cs` 注册），避免四技能验收依赖无关泰坦战斗前置。

- `artifacts/pilot-tactical-20261128/tactical-native/runtime-results.json`：`success: true`，6 组记录；模块 MVID `6e2a85e1-3e39-4a0a-ac66-f8f5b832e8d3`。
- 验证内容包括无 Jump Kit 拒绝、空槽、同类不刷新、T 激活/M3 保留、四种物品注册与消费、活跃技能替换、四种技能行为、复活恢复和卸装清空。钩爪测试实际位移为 X=66.3、Z=18；Phase 验证返回碰撞规则 `Ignore` 并成功 telefrag；Pulse 成功标记目标。
- 独立 Validation 编译为 0 错误、0 警告。由于另一任务正在修改 `NpcTraversalAi.cs`，本次从顶层验证源码复制出隔离构建目录，仅排除该无关文件及对应注册；没有改动它的源文件。
- 完整 `--pilot-parkour` 通过前 25 组后，在既有 Monarch Rearm 冷却断言失败，尚未到战术技能段；不能报告完整大套件通过。原始失败证据保留在 `artifacts/pilot-tactical-20261128/pilot-native/`。
- 使用与官方引擎相同 `bin` 的独立 runtime `D:\sbox\_codex-build\pilot-tactical-20261128\runtime`，没有部署到 Steam mounts，也没有修改正式游戏场景。

这些结果是原生接口/场景断言，不代表多人实战或四种技能的最终视觉效果已全部人工验收。HUD 截图验证由独立 `--pilot-tactical-hud` 入口记录。

## 构建、资源与 HUD 验证

- 定向单元测试：`PilotParkourTests`、`PilotTacticalResourceTests`、`DataDrivenCatalogTests` 合计 **46 通过、0 失败、0 跳过**。启用实际 Titanfall 2 安装内容检查，涵盖 8 个 PCF 图的冷加载、26 个 Miles 声音事件及 MP 参数解析。日志：`artifacts/pilot-tactical-20261128/tests.log`。
- 官方 `MenuBuild.exe` 沙盒白名单编译成功，日志：`artifacts/pilot-tactical-20261128/menubuild.log`。构建目录为 `artifacts/pilot-tactical-20261128/package/`，含 DLL 与挂载 Assets；DLL SHA-256 为 `C5FE7041A45FE5C99C2A3E5E59686C2ADC73751770FF036446D50793DC652F8B`，与原生验证加载的 DLL 一致。
- `hud-native/runtime-results.json`：**11 条结果记录通过**。四个原版图标均为 128×128 透明纹理；实际检查了 8 张就绪/冷却截图，T 角标、抓钩充能数字、冷却百分比及细条可见。
- HUD 的 1024×768、1920×1080、2560×1080 各有/无护甲组合中，技能槽与模拟健康区域间距为 11–12 像素。测试根面板 `ScaleToScreen=1`，这些结果不能替代真实宿主自动缩放、超大 HUD 设置和多人游戏画面验收。截图中的健康区域是明确标注的模拟布局，技能面板是实际产品组件。
- HUD 图像：`artifacts/pilot-tactical-20261128/hud-native/pilot-tactical-*-{ready,cooldown}.png`。本轮未部署、未提交或推送；仓库原有其他修改保持原状。`Tests/` 与 `Validation/` 受现有忽略规则影响，提交时需显式检查是否纳入对应测试源码。

## 使用方式

加载本轮构建后，在 Titanfall 2 实体目录生成并穿戴 `Pilot Jump Kit`，再生成 `Pilot Tactical` 的兴奋剂、抓钩、相位或脉冲刀物品，按 E 拾取、按 T 使用。拾取另一种会消费新物品并替换旧槽；重复同种会保留地面物品，不补充充能。旧的中键释放和 T 切换已经移除，`tf2_tactical` 命令现在只触发已拾取的技能。

已发射的脉冲刀保留自身剩余寿命；替换装备槽不会撤销已经投出的投射物。前三种技能使用原版背包作为世界拾取容器，并非原游戏原有的独立掉落道具。

IDA 的 `engine.dll` 模块核对另见 [模块研究记录](PILOT_TACTICAL_IDA_MODULE_AUDIT_2026-11-28.zh-CN.md)。当前数据库证明了 client/server 接口边界，尚未提供抓钩公式或 Stim 原生状态计算的还原依据。
