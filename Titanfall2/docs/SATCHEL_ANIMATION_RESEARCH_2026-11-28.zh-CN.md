# 炸药包左右键动画调查（2026-11-28）

调查结论：修复前，左键投掷映射到了原版按引爆器的 `attack_seq`；右键执行爆炸逻辑，却没有触发该引爆动画。原版模型包含完整、可解码的投掷与引爆两组动作。

2026-11-28 后续修复已经完成：左键依次接入 `TossPullout`、`TossPrep`、`TossHold` 和 `Toss`，右键接入独立 `Detonate`，模型事件声音随对应动作播放。投掷和引爆仍由宿主处理；同一套原版活动序列用于导入动画与读取出手、引爆事件。收枪或更换持有者会清掉待执行的引爆。未改动 `[G]` 副手逻辑或原版单手、过顶变体选择；旧储存的挂载武器缺少模型引爆时机时保留原有的右键行为。本文其余内容记录的是修复前的调查。

## 原版资源与动作含义

直接读取本机 `D:\Game\steams\steamapps\common\Titanfall2`，模型为 `models/weapons/clacker_detonator/ptpov_clacker_detonator.mdl`。当前源码编译后调用真实 `ModelLoader.ResolvePilotViewSequences`，并解码全部 37 个有动画索引的序列。

模型 SHA256：`BF5F074006B0CBEF0823EC939FA37AA622B82F7131539919F9F30685667F9798`。

| 用途 | 原版序列 / Activity | 模型时长与关键事件 |
| --- | --- | --- |
| 取出炸药包 | `toss_prep_pullout_seq` / `ACT_VM_TOSS_PREP_PULLOUT` | 0.1333 秒 |
| 准备姿态 | `toss_prep_seq` / `ACT_VM_TOSS_PREP` | 0.0333 秒 |
| 持有炸药包 | `toss_hold_seq` / `ACT_VM_TOSS_HOLD` | 6.3333 秒循环素材 |
| 投掷炸药包 | `toss_seq` / `ACT_VM_TOSS` | 1.1333 秒；起始 `Weapon_R1_Satchel.Throw`；0.3000 秒 `AE_WPN_TOSS_RELEASE`；0.8000 秒 `AE_WPN_READYTOFIRE` |
| 按下引爆器 | `attack_seq` / `ACT_VM_PRIMARYATTACK` | 0.9667 秒；起始 `Weapon_R1_Clacker.TriggerPull`；约 0.3333 秒 `AE_WPN_PRIMARYATTACK` |
| 拿出引爆器 | `draw_seq` / `ACT_VM_DRAW` | 0.6667 秒；起始 `Weapon_R1_Clacker.Raise`；0.3000 秒 ready |
| 普通待机 / 奔跑 | `idle_seq` / `sprint_seq` | 与投掷持有 `toss_hold_seq` / `toss_hold_sprinting_seq` 分开 |

模型还含单手和 overhead 投掷变体。本次未判断原版选择这些变体的全部条件。

原版 `scripts/weapons/mp_weapon_satchel.txt` 声明 `fire_mode = offhand_hybrid`、`offhand_toss = 1`、`no_ammo_used_on_primary_attack = 1`。对应脚本 `mp_weapon_satchel.nut` 中：

- `OnWeaponPrimaryAttackAnimEvent_weapon_satchel` 调用 `Player_DetonateSatchels`。
- `OnWeaponTossReleaseAnimEvent_weapon_satchel` 调用 `FireWeaponGrenade`，返回一次投掷的弹药成本。

因此，原版动画中的 “PrimaryAttack” 是引爆语义，不能直接按挂载的“鼠标左键”语义解释。模型事件也分别使用引爆器扳机声和炸药包投掷声，交叉确认了这一区别。

## 修复前的调用链与错误

1. `Code/Gameplay/Weapons/Runtime/Weapon.cs` 的 `PrimaryAttack()` 对投掷物调用 `_ordnance.BeginAttack(this)`。
2. `Mechanics/Weapon.Ordnance.cs` 在松开左键后安排 toss；到 `_tossAt` 调用 `BroadcastOrdnanceToss()`，到 `_launchAt` 创建投射物。
3. `Mechanics/Weapon.Mechanics.cs:114` 的 `PlayOrdnanceToss()` 实际调用通用 `WeaponModel.OnAttack()`。
4. `Runtime/PilotViewModel.cs:311` 的 `OnAttack()` 播放 `pilot_fp_fire`。
5. `Code/Resource/Model.PilotView.cs:22` 将 Fire 候选设为 `["attack_seq", "toss_seq"]`，优先使用 `attack_seq`。炸药包两者均有，因此不会选择投掷。

探针实测映射：`Idle=idle_seq`，`Draw=draw_seq`，`Fire=attack_seq`，`FireActivity=ACT_VM_PRIMARYATTACK`，`Sprint=sprint_seq`，`Reload=draw_seq`。

由此，左键在投掷时间点播放的是引爆器动作及其 `Weapon_R1_Clacker.TriggerPull` 声音；投射物仍按真正 `toss_seq` 的事件时间生成，画面与玩法使用了不同动作的时间轴。

右键的 `Mechanics/Weapon.Specials.cs:8` 只进入宿主 `DetonateCharges()`，遍历投射物并调用 `DetonateOwned()`，没有播放第一人称动作，也没有按原版 `AE_WPN_PRIMARYATTACK` 延迟起爆。

还有两个关联缺口：

- 准备阶段只调用 `BroadcastOrdnanceSound(false)`，没有播放 `toss_prep_pullout_seq` / `toss_prep_seq`，按住时也不会切换到 `toss_hold_seq`。本机解析出的 Satchel prep/throw sound 字段均为空；原版对应声音来自模型事件。
- `PilotViewModel.SetLocomotionState()` 的动作锁不含投掷角色。后续修复需要避免奔跑/单手姿态切换覆盖正在播放的投掷或引爆动作。

## 修复建议

保持挂载目前的“左键投掷、右键引爆”布局，将动画语义拆开：

1. 显式导入 `TossPullout`、`TossHold`、`Toss` 和 `Detonate` 角色，分别绑定实际原版 Activity/序列；装备 `Draw` 保留引爆器拿出动作。
2. 左键按下进入准备，按住进入持有，松开后播放 `Toss`；将动画播放时长与 `OrdnanceProfile` 的投掷时长一致化。原版默认投掷开始 0.30 秒时出手，0.80 秒时允许后续操作。
3. 右键播放 `Detonate`，在宿主按原版模型事件时间安排起爆（默认动画开始约 0.3333 秒）。不要依赖客户端渲染事件执行权威伤害；同时处理收枪、死亡和重复输入的取消/去重。
4. 同步更新模型内动画、独立 `pilot/*.anim` 子资源、事件映射与运行时播放状态，保留正常枪械的 Fire/ADS 行为。不能只全局交换 `attack_seq` / `toss_seq` 优先级，这会继续丢失引爆的独立入口和准备/持有状态。
5. 增加使用真实第一人称模型的左右键回归：左右键选中不同序列；投掷在 release 点生成且只消耗一次弹药；引爆在 primary event 点执行且不消耗投掷弹药；覆盖按住、连续第二包、奔跑、取消、无弹药但仍有已部署炸药包。

## 验证范围与复现

本次探针构建并运行成功，实际断言当前 resolver 选择 `attack_seq` 且原版同时存在 `toss_seq`。模型全部序列解码成功。它证明资源和映射冲突，不代替原生画面、多客户端或正式沙箱构建验收。

现有 `Validation/WeaponOrdnanceInput.cs:31` 主动将 `WorldModelPath`、`ViewModelPath`、`ViewArmsModelPath` 清空；其 Satchel 检查断言投掷时间、消耗和右键爆炸，没有检查手部动作。`Tests/PilotViewModelTests.cs` 现有原版资源测试检查序列可解析、帧数和骨骼数量，也没有断言 Satchel Fire 的动作语义。这解释了为何已有逻辑测试可以通过。

证据与探针位于仓库根 `artifacts/satchel-animation-20261128/`：

- `satchel-animation-audit.json`：原版文件哈希、当前编译程序集 MVID、实际选择结果、所有序列和事件、投掷时序。
- `mp_weapon_satchel.txt`、`mp_weapon_satchel.nut`：本次从本机安装读取的原版定义。
- `probe/Program.cs`、`probe/probe.csproj`：复现程序。

```powershell
dotnet run --project artifacts/satchel-animation-20261128/probe/probe.csproj `
  '-p:SboxEngineRoot=D:\Game\steams\steamapps\common\sbox' -- `
  'D:\Game\steams\steamapps\common\Titanfall2' `
  'D:\sbox\sbox-mounts\artifacts\satchel-animation-20261128'
```
