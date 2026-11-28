# 武器 ADS 开火动画与 sway/bob（2026-11-27）

CAR 机瞄开火时的大幅枪身摇摆来自导入器一直取 `attack_seq` 的第一个动画格：该格是腰射动画，原版另有幅度很小的 ADS 动画。现按序列实际 `ads_blend` 轴选择接近 1 的格，并在动画图中随瞄准比例混合腰射和 ADS 开火增量。每发共用一个播放周期，发射时确定时长，避免中途切换 ADS 导致相位跳变。没有 ADS 轴的序列保留原格；没有按枪名硬编码选择。

原版 CAR `attack_seq` 是 2×1：`attack_anim` 28 帧，`attack_ads_anim` 20 帧，均为 additive。原始动画解码中，枪身 `ja_c_propGun` 的最大旋转约为 2.088° / 0.0396°、位移约为 1.168 / 0.1963；ADS 枪栓 `def_c_bolt` 仍移动约 3.7423。因此应使用原版 ADS 动画，不能统一压低整副骨架的运动。

实现涉及 `Code/Resource/Model.PilotView.cs`、`Model.PilotView.Resources.cs` 的 `pilot_fire_ads_delta`，以及 `Code/Gameplay/Shared/Animation/PilotAnimationPlayer.cs`。保留 `idle_weight = 1 - Zoom`，使腰射 idle 不再叠加到完整 ADS。

## 171 个 sway/bob 字段

`Code/Gameplay/Weapons/Data/WeaponData.Sway.cs` 读取腰射 85 项、`_zoomed` 85 项及 `sway_enable_zoomed`。每组包括 10 个移动/转向方向的 60 个平移与旋转系数、12 个上下限、2 个增益、2 个附件轴心字段、9 个 bob 字段。字段经武器 prefab 和配件覆盖传给 `Runtime/PilotViewModel.Motion.cs`；原来的通用转向弹簧已由这些原版数据替代。

依据当前 retail `client.dll` 的只读 IDA 结果：初始化 `0x180592810`，更新 `0x1805939C0`，附件过渡 `0x180110010`，轴心变换 `0x180592680`。反编译中缺失的 xmm 参数已结合指令核对。完整摘录在 `D:\sbox\_codex-build\car-ads-20261127\viewmotion-retail-evidence.txt`。

| 缺省字段 | 腰射 | ADS |
| --- | ---: | ---: |
| sway 平移/旋转 gain | 12 | 24 |
| bob gain | 12 | 12 |
| bob min/max speed | 0.1 / 0.2 | 0.1 / 0.2 |
| 其余浮点字段 | 0 | 0 |

`sway_enable_zoomed` 缺省为 true。ADS 字段有独立缺省值，未填写时不继承腰射；方向系数保留负数。

- 移动输入为视角局部速度乘帧时间；转向输入为最近五次角速度均值乘 tick interval，适配为 `Scene.FixedDelta`。使用玩家输入视角，避免把已经叠加的后坐力再次送入 sway。
- ADS 启用且 zoom > 0 时选用 ADS 参数；最终角度上下限用 `clamp(zoom × 20)` 过渡。附件轴心按目标参数块的独立时间过渡，应用 `P × R × inverse(P)` 后再加局部平移。
- bob 根据速度上下限推进周期：竖直一个周期、水平两个周期；pitch 使用竖直波，yaw/roll 使用水平波。空中和滑铲停止驱动并平滑衰减。
- 同一时间的多次相机组合不会重复推进状态；重新装备清零。跑酷姿态原有 ADS 抑制保留，与武器自身 sway/bob 分开应用。

## 验证与边界

`Tests/PilotAdsFireTests.cs` 从实际注册表枚举全部 `.pov` 武器，检查 ADS 轴选择、原版 CAR 幅度、additive 解码及挂载动画子资源。`Tests/WeaponViewMotionTests.cs` 覆盖缺省值、带符号参数、移动方向、五次转向采样、bob、ADS 限幅/轴心、重复采样、MP/配件覆盖及四把原版武器数据。

原生动画图验证 `Validation/CarAdsMotion.cs` 已通过；结果文件为 `D:\sbox\_codex-build\weapon-presentation-20261127\native-draft\car-results.json`（MVID `112693ca-91e0-43f7-bcc3-2708a3fb62c3`）。这是图内采样，数值与原始完整动画解码的峰值略有不同：

| 原生 CAR 图采样 | 腰射 | 半 ADS | 完整 ADS |
| --- | ---: | ---: | ---: |
| 枪身最大旋转（度） | 1.9479563 | 0.9723608 | 0.03956468 |
| 枪身最大位移（模型单位） | 1.1633143 | — | 0.18647781 |

`Validation/WeaponViewMotion.cs` 在实际挂载 prefab 和 viewmodel `Place` 路径验证了 CAR、R-101（`rspn101`）、Alternator、Wingman，四项均通过。它检查配置序列化、实际转向 sway、重复相机调用幂等和 ADS 角度限制。结果为 `D:\sbox\_codex-build\weapon-presentation-20261127\native-official\view-motion-results.json`（`success: true`，MVID `f7d8edae-0093-4a28-851d-05c7c9d340d1`）。固定输入下四把枪均记录腰射旋转 `0,-0.3266,0.5226`、ADS `0,-0.04,0.3777`，腰射/ADS bob cycle 均为 0.4 秒；这些数值仅描述该测试输入。

本次未部署到正在使用的游戏安装，也未进行物理鼠标输入、持续射击与移动组合的手感验收。原生验证证明资源和运行时调用链生效，不替代原版与 s&box 的人工游玩对照。
