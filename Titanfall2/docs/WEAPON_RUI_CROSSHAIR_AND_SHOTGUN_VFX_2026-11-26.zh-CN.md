# Titanfall 2 武器 RUI、枪身弹药显示、准星与霰弹枪特效调研文档

> 调研来源：本机 Retail 版 Titanfall 2（`englishclient_mp_common.bsp.pak000_dir.vpk`、`ui*.rpak`、`client.dll` IDA 逆向、`engine.dll` RUI 运行时分析）。  
> 用途：固化泰坦陨落 2 武器系统的静态权威数据与格式结论，供后续开发与排错直接查阅，无需每次重新解析二进制或反编译。

---

## 1. 霰弹枪与弹道粒子（PCF / DMX v5）逆向结论

### 1.1 `client.dll` 粒子算子默认值（IDA 实证）

| PCF 算子名称 | `client.dll` 类 / 字段描述符地址 | 关键默认值 / 行为 | 修复前 Bug |
|---|---|---|---|
| `Graph Scalar` | `C_OP_GraphScalar` (`0x180661320`) | `output field` 默认 **`3` (Radius)**，`output op` 默认 `0`，`graph time is in lifespans` 默认 `true` | 误设默认 `7` (Alpha)，导致霰弹枪枪口烟雾半径不膨胀且 Alpha 异常 |
| `Remap Noise to Scalar` | `C_INIT_CreationNoise` (`0x1806D7C20`) | `output field` 默认 **`3` (Radius)**，`output minimum=0`，`output maximum=1` | 未解析该初始化器，导致 `mflash_shotgun_base_FP` 半径保持 `1..1` 而非 `10..20` |
| `Move Particles Between 2 Control Points` | `C_INIT_MoveBetweenPoints` (`0x1806DCCF0`) | `end control point` 默认 `1`，`minimum/maximum speed` 决定 CP0→CP1 发射速度，按 `travelDistance / speed` 裁剪寿命 | 未实现，导致 `weapon_tracers_shotgun`（速度 6500..8500）无弹道位移 |
| `Alpha Fade and Decay for Tracers` | `C_OP_FadeAndKillForTracers` (`0x18065EDF0`) | `start_fade_in_time=0`，`end_fade_in_time=0.5`，`start_fade_out_time=0.5`，`end_fade_out_time=1.0`，且属于正常寿命衰减（非 EndCap-only） | 未识别，导致弹道粒子误判为 `EndCapLifetime=true` 且无淡入淡出 |

### 1.2 霰弹枪特效专项缺陷清单

1. **EVA-8 / 通用霰弹枪枪口焰 (`particles/wpn_muzzleflash_shotgun.pcf :: mflash_shotgun_fp_FULL`)**
   - `mflash_shotgun_base_FP`：通过 `Remap Noise to Scalar` 设置半径 `10..20`，并启用 `animation_fit_lifetime=True, animation rate=0`（需按帧数 / 平均寿命反推 `PlaybackSpeed`）。
   - `mflash_shotgun_core_FP`：使用 `$RAMPTEXTURE` 走 `ParticleRampRenderer`，`Position Modify Offset Random` 前移 `70` 单位、`render_sprite_trail` 设 `min/max length = 80, ignore delta time = True`。拖尾锚点必须钉在头部 `(0.5, 0)`，否则核心火焰会悬浮脱节。
   - `mflash_shotgun_sparks_FP`：本地初速上限 `9500`；原 `ParticleStreamer.MaximumVelocity = 2048f` 截断高速火花，已提高至 `12000f`。
   - `mflash_shotgun_smoke_FP`：含两个 `Graph Scalar`，第二个省略 `output field`（默认 Radius=3，倍率最大 3x）。
2. **Mastiff 弹丸特效 (`particles/wpn_dragonsbreath.pcf :: P_mastiff_proj`)**
   - `children` 中 `mstf_proj_smk_rope` 出现两次（首次 `mute=True`），`mstf_proj_sparks_1` 也是 `mute=True`；`Titanfall2PcfReader` 原未跳过 `mute=True` 的子系统。
   - `mstf_proj_sparksheet`（`0.3s`）与 `mstf_proj_sparks_end`（`0.4s`）在 `emit over distance` 中配置了 `emission start time`，原未读取该延迟。
3. **EVA-8 弹道 (`particles/tracer_fx.pcf :: weapon_tracers_shotgun`)**
   - 使用 `move particles between 2 control points`（6500..8500）+ `Alpha Fade and Decay for Tracers`。

### 1.3 三把霰弹枪弹道脚本核对

| 武器 | 脚本 | 弹丸数 / 模式 | 散射公式 / 偏移 | 弹速 / 重力 / 寿命 |
|---|---|---|---|---|
| **EVA-8 Auto** (`shotgun`) | `mp_weapon_shotgun.nut` | 8 颗 Hitscan (`DF_SHOTGUN`) | `ShotgunBlast(..., 8)` | Hitscan |
| **Mastiff** (`mastiff`) | `mp_weapon_mastiff.nut` | 8 颗水平扇形弹丸 | `right=[0.15, 0.3, 0.6, 1.2, -0.3, -0.6, -1.2, -0.15]`；`spread = Graph(zoom, 0, 1, 0.05, 0.025)` | `speed=3000`, `gravity=0.09`, `life=Rand(0.65..0.85)*(zoom>0.8?1.25:1)` |
| **SA-3 Mozambique** (`shotgun_pistol`) | `mp_weapon_shotgun_pistol.nut` | 3 颗三角形弹丸 | `(up, right) = [(-0.2,-0.4), (-0.2,+0.4), (0,0)]`；`spread = Graph(zoom, 0, 1, 0.05, 0.025)` | `speed=3000`, `gravity=0.09`, `life=Rand(0.50..0.75)*(zoom>0.8?1.25:1)` |

---

## 2. MDL53 枪身 RUI 网格拓扑结构（`mstudioruimesh_t`）

### 2.1 二进制布局实测验证

每个 RUI 网格表项为 8 字节（`entry + 4` 为相对 `entry` 的网格偏移），网格头 32 字节：
- `+0x00`: `int32 parents`（绑定骨骼数，1..32）
- `+0x04`: `int32 vertices`（顶点数）
- `+0x08`: `int32 faces`（面数）
- `+0x0C`: `int32 parentOffset`（`int16[parents]` 骨骼索引表，位于名字字符串之后）
- `+0x10`: `int32 vertexOffset`（`vertices * 16B`，每项 `{ int32 parentIdx, float x, y, z }`）
- `+0x14`: `int32 mapOffset`（`faces * 6B`，每面 `int16[3]` 仿射基准顶点索引）
- `+0x18`: `int32 faceOffset`（`faces * 32B`，每面 `float u[4], float v[4]`）
- `+0x1C`: `int32 faceFlags`（非零常见于多面/三角化/曲面网格，不影响仿射求解）

### 2.2 原实现遗漏大量武器屏幕的根因

1. `Model.UI.cs` 原强硬限制 `parents == 1 && vertices == 4 && faces == 1 && u28 == 0` 且 `|dot(right, down)| <= 0.002`。
2. 实测 31 把武器中绝大多数枪身屏幕属于以下三类：
   - **单面微斜四边形**：如 `rspn101_og` (`r101_rui_lower`, `skew=0.0248`)、`sa3_rui_lower` (`v=6, f=1, skew=0`)。
   - **单平面多片拼接**：如 `alternator_rui_lower` (`f=9`)、`volt_rui_lower` (`f=2`)、`hemlok_esaw_rui_upper` (`f=2`)、`b3wing_rui_lower` (`f=2`)、`w1128_rui_lower` (`f=2`)、`doubletake_rui_lower` (`f=9`)、`softball_rui_upper` (`f=3`)、`pulse_lmg_rui_lower` (`f=13`)。
   - **多平面/双面独立子屏**：如 `p2011_rui_lower`（后屏 `f[0]` + 顶数字条 `f[1]`）、`epg_rui_upper`（上槽 `f[0]` + 下屏 `f[1]`）、`lstar_rui_lower`（后主屏 `f[3..4]` + 侧热条 `f[0..2]`）、`hemlok_bfr_rui_lower`（后主屏 `f[0]` + 底条）、`vinson_rui_lower`（多段条）、`smr_rui_upper`（前后双面）、`arc_launcher_rui_upper`、`pulse_lmg_rui_upper`。
3. **解法**：逐面求解仿射基 `(UvOrigin, Right, Down, Normal, PlaneD)`，对轻微梯形屏幕正交化 `Down`，按共面且 UV 基一致聚类合并，输出每个独立平面子屏。实测 **28/28 具备原生枪身弹药屏的武器全部通过（0 失败）**。

---

## 3. 全 32 个武器弹药 / 过热 / 充能 RUI 静态清单

> 对应实现：`Titanfall2/Code/Formats/Rpak/RpakWeaponUiLayout.cs` (`Titanfall2WeaponUiCatalog`)。  
> 原实现失败的三大原因：① `WeaponData.Ui.cs` 仅放行含 `"ammo_counter"` 的名称，漏掉 `vinson_bar`、`lstar_screens`、`pulse_lmg_panel1/2`、`arc_launcher_reticle`、`hcog_lower`；② `RpakWeaponUiLayoutReader` 仅检查第 0 条绘制记录且要求为 `TEXT`，导致 18 个纯进度条/分段格屏幕与非首项文本屏幕全部解析失败；③ 仅解码字节码 opcode `4/7`，未跳过 `11`。

| RUI 资产路径 | 适用武器 / 配件 | 画布 | 类型 | 字体 / 字号 / 锚点 | 进度条 / 分段格区域（画布像素） |
|---|---|---|---|---|---|
| `ui/r101_ammo_counter` | R-201, R-101 | 256×256 | NumericWithBar | f=9, sz=120, (69.1, 56.3) | (10.2, 117.8, 233×7.7) 连续横条 |
| `ui/car_smg_ammo_counter` | CAR（下屏） | 256×256 | Numeric | f=9, sz=120, (128, 64) | — |
| `ui/car_smg_ammo_counter_small` | CAR（上屏） | 100×100 | DualBar | — | (2, 52, 46×44) + (52, 4, 44×24) |
| `ui/r97_ammo_counter` | R-97 | 160×240 | SegmentedBar | — | 4 行分段格 (5, 40, 150×80) + 顶条 |
| `ui/vinson_bar` | V-47 Flatline | 256×256 | ContinuousBar | — | (4, 16, 120×96) 连续横条 |
| `ui/g2a4_ammo_counter` | G2A5 | 256×256 | NumericWithBar | f=9, sz=145, (192, 64) | 左侧 4 行分段格 (6, 16, 110×102) |
| `ui/p2011_ammo_counter` | Hammond P2016 | 256×256 | NumericWithBar | f=9, sz=40, (209.9, 10.2) | 后屏竖向 12 格 (15.4, 12.8, 133.1×243.2) |
| `ui/re45_ammo_counter` | RE-45 Auto | 256×256 | SegmentedBar | — | 竖向 20 格 (25.6, 12.8, 204.8×236) |
| `ui/alternator_ammo_counter` | Alternator | 256×256 | DualBar | — | 双枪管左右竖条 (12,8,76×116) & (168,8,76×116) |
| `ui/volt_ammo_counter` | Volt | 256×256 | Numeric | f=9, sz=60, (64, 25.6) | — |
| `ui/spitfire_ammo_counter` | Spitfire | 256×256 | SegmentedBar | — | 5 行 80 格 (6, 50, 244×200) + 顶条 |
| `ui/hemlok_esaw_ammo_counter` | Devotion | 256×256 | NumericWithBar | f=2, sz=120, (56.3, 46.1) | 底条 (0, 89.6, 256×12.8) |
| `ui/rspn101_dmr_ammo_counter` | Longbow-DMR | 256×256 | SegmentedBar | — | 6 格横条 (12.8, 6, 230.4×88) |
| `ui/b3wing_ammo_counter` | B3 Wingman, Wingman Elite | 256×256 | Numeric | f=19, sz=150, (128, 69.1) | — |
| `ui/w1128_ammo_counter` | EVA-8（下屏） | 256×256 | SegmentedBar | — | 8 发霰弹格 (4, 25.6, 248×64) |
| `ui/w1128_upper_ammo_counter` | EVA-8（上屏） | 256×256 | ContinuousBar | — | 竖向满屏条 (8, 8, 240×240) |
| `ui/hemlok_bfr_ammo_counter` | Hemlok BF-R | 256×256 | NumericWithBar | f=9, sz=190, (150, 86) | 8 格条 (75,156,116×20) + 底条 (0,228,256×28) |
| `ui/mk6_ammo_counter` | Smart Pistol MK6 | 256×256 | NumericWithBar | f=9, sz=145, (192, 71.7) | 左侧 2 行 12 格 (4, 51, 112×85) |
| `ui/epg_ammo_counter` | EPG-1 | 256×256 | DualBar | — | 上 4 格 (12.8,25.6,232×64) + 下 4 格 (12,140,104×104) |
| `ui/lstar_screens` | L-STAR | 100×100 | HeatScreen | f=9, sz=28, (50, 44) | 过热进度条 (12,22,76×56) + 侧热条 (2,87,96×6) |
| `ui/kraber_ammo_counter` | Kraber-AP | 256×256 | SegmentedBar | — | 4 发弹格 (8, 10, 240×165) |
| `ui/smr_ammo_counter` | Sidewinder SMR | 256×256 | ContinuousBar | — | 连续横条 (8, 16, 236×224) |
| `ui/doubletake_ammo_counter` | D-2 Double Take | 512×512 | DualNumeric | f=2, sz=256, (384,128) & (128,128) | — |
| `ui/softball_ammo_counter` | R-6P Softball | 256×256 | SegmentedBar | — | 6 发榴弹格 (6, 25.6, 240×115.2) |
| `ui/pulse_lmg_panel1` | EM-4 Cold War（上屏） | 256×256 | ChargeStack | — | 9 格充能柱 (5.1,10.2,102.4×236) + 4 格弹药柱 |
| `ui/pulse_lmg_panel2` | EM-4 Cold War（下屏） | 256×256 | NumericWithBar | f=9, sz=220, (128, 140.8) | 顶部 4 连发条 (24, 25, 208×28) |
| `ui/mastiff_ammo_counter` | Mastiff | 256×256 | SegmentedBar | — | 4 发弹格 (8, 12, 240×228) |
| `ui/sa3_ammo_counter` | SA-3 Mozambique | 256×256 | SegmentedBar | — | 4 发弹格 (12.8, 25.6, 102.4×76.8) |
| `ui/arc_launcher_reticle` | LG-97 Thunderbolt | 256×256 | NumericWithBar | f=9, sz=15, (61.4, 115.2) | 3 格电弧弹条 (128, 12, 120×96) |
| `ui/hcog_lower` | HCOG 瞄具配件 | 256×256 | NumericWithBar | f=9, sz=20, (192, 120.3) | 右侧弹药条 (134, 16, 116×84) |
| `ui/aog_ammo_counter` | AOG 瞄具配件 | 256×256 | ContinuousBar | — | (12, 32, 232×192) |
| `ui/aog_ammo_counter_lstar` | AOG (L-STAR) | 256×256 | HeatScreen | — | (12, 32, 232×192) |

> 注：Charge Rifle (`defender`)、MGL (`mgl`)、Archer (`rocket_launcher`) 在原版游戏中无枪身弹药屏，仅有 `pro_screen_panel`。

---

## 4. 全 15 种武器准星（`RUI_CrosshairData`）与 ADS 瞄具清单

### 4.1 15 种原生准星与武器映射

> 注意：`mp_weapon_vinson.txt` 使用 `Crosshair_0` 而非 `Crosshair_1`，解析时需同时兼容两者。

| 序号 | RUI 准星资产 | 对应武器 | 视觉形态与动态特性 |
|---|---|---|---|
| 1 | `ui/crosshair_tri` | R-201, CAR, R-97, G2A5, R-101, Hemlok | 三向倒 Y / 左右+下三臂 + 中心点，随散布张合 |
| 2 | `ui/crosshair_plus` | P2016, RE-45, Spitfire, Devotion | 标准四向十字 + 中心点 |
| 3 | `ui/crosshair_alternator` | Alternator, Volt, Flatline | 宽水平左右 T 形臂 + 中心点（无上臂） |
| 4 | `ui/crosshair_sniper_amped` | Longbow-DMR, Kraber, Double Take, Archer, Thunderbolt | 细精准十字 / 四角括号 + 细中心点（腰射淡显、开镜切全屏镜/瞄具） |
| 5 | `ui/crosshair_charge_rifle` | Charge Rifle, Cold War | 中心菱形 + 左右充能弧/条，实时随 `ChargeFraction` 填充变色 |
| 6 | `ui/crosshair_shotgun` | EVA-8 Auto | 四段圆弧组成的动态散布大圆环 + 中心点 |
| 7 | `ui/crosshair_mastiff` | Mastiff | 宽水平括号 `[  •  ]`，匹配 8 弹丸水平扇面，开镜收窄 50% |
| 8 | `ui/crosshair_mozambique` | SA-3 Mozambique | 三点三角形准星（上中 + 左下 + 右下），匹配 3 弹丸三角弹着点 |
| 9 | `ui/crosshair_wingman` | B3 Wingman | 手枪紧凑左右括号 + 下刻度 + 中心点 |
| 10 | `ui/crosshair_wingman_n` | Wingman Elite | 极细精准点 + 紧致外菱形刻度（腰射精准特性） |
| 11 | `ui/crosshair_smart_pistol` | Smart Pistol MK6 | 大锁定框（按 `smart_ammo_search_angle` 映射四角括号）+ 实时目标追踪菱形/锁定数/引导线 |
| 12 | `ui/crosshair_circle2` | EPG-1 | 能量圆环 + 中心点 + 左右提前量短刻线 |
| 13 | `ui/crosshair_lstar` | L-STAR | 弧形左右括号 + 实时过热条（青→橙→红） |
| 14 | `ui/crosshair_smr` | Sidewinder SMR | 火箭筒方形四角括号 + 中心点 |
| 15 | `ui/crosshair_grenade_launcher` / `2` | MGL, Softball | 榴弹抛物线测距梯（中心准星下方多级渐缩水平刻线） |

### 4.2 Smart Pistol 锁定 RUI 与音效参数

来自 `scripts/weapons/mp_weapon_smart_pistol.txt` 与 `mp_weapon_smart_pistol.nut`：
- 锁定搜索范围：`smart_ammo_search_angle = 35°`，`smart_ammo_search_distance = 1250`，`smart_ammo_max_targeted_burst = 12`
- 锁定中循环音：`smart_ammo_looping_sound_acquiring = Weapon_SmartPistol_TargetLockingLoop`
- 锁定完成提示音：`smart_ammo_target_confirmed_sound = Weapon_SmartPistol_PilotLocked`
- 视觉特性：`smart_ammo_draw_acquisition_lines = 1`、`smart_ammo_hud_lock_style = pilot_pistol`、`P_smartpistol_lockon_FP`

### 4.3 ADS 光学瞄具与电子机瞄映射

| 瞄具 Mod / 原生 RUI | 视觉样式 |
|---|---|
| `hcog` (`ui/hcog_upper`) | 经典 HCOG 红色/青色锐利 V 形/点 + 侧框 |
| `holosight` (`ui/holo_scope`) | 全息圆环 + 中心红点 + 水平风偏刻线 |
| `redline_sight` (`ui/acgs_redline`) | ACGS 红色三角尖 + 水平红线风偏刻度 + 竖直下坠刻度 |
| `threat_scope` (`ui/cro_threat_front`) | 威胁镜数字方框 + 四角刻度 + 中心高亮红点 |
| `aog` (`ui/aog_multi_reticles`) | AOG 精准圆环 + 十字风偏线 + 中心点 |
| `scope_4x` (`ui/variable_zoom_crosshair`) | 狙击变倍全屏密位十字镜 |
| `ui/mastiff_horseshoe` | Mastiff 电子马蹄形准星 |
| `ui/doubletake_reticle_doublev` | Double Take 双 V 并联准星 |
| `ui/diamond_reticle_rear` | Hemlok / MK6 菱形电子机瞄 |
| `ui/p2011_green_sights` | P2016 绿色荧光点 |
| `ui/r101_sights` / `ui/vinson_sights` / `ui/volt_sights` / `ui/alternator_reticle` / `ui/g2a4_sights` / `ui/sa3_crosshair` | 原生青/绿电子机瞄点与侧翼刻线 |
