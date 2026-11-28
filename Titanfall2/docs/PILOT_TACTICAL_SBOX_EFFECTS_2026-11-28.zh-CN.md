# 铁御技能：s&box 逻辑适配与原版声音、屏幕资源

日期：2026-11-28。本轮遵循用户补充要求：抓钩运动和兴奋剂逻辑适配 s&box，优先复用游戏音效与滤镜资源；不以恢复 Respawn 原生函数为前提。本轮没有增加隐身、全息影像或 A-Wall。

## 抓钩

- 保留现有 s&box `PlayerController` / `Rigidbody` 拉拽、摆荡和脱离实现。技能的主机计时使用实际帧时间，运动子步不重复累加主机计时；挂接目标销毁或退出铁御移动模式后会脱离。
- 自动超时跨过长帧时，剩余空闲时间计入技能充能，不因卡顿丢掉冷却进度。
- 发射失败也播放原版发射/复位音，不扣充能、不启动移动循环。成功挂接、拒绝表面和拔出按原版 `grapple`、`grapple_disallowed`、`grapple_extract` 材质表选择事件。
- 断开、换装和停用会停止移动循环，并播放回收/复位。0.2 秒尝试节流与 0.12 秒延后复位属于本项目的 s&box 排程。
- 原脚本没有抓钩专属调色表，因此不额外添加自制颜色遮罩冒充原版滤镜。继续使用现有 FOV 和绳索表现。

## 兴奋剂

- 复用原版启动、循环和收尾音；在真实本地第一人称播放 1P，第三人称/自由相机及远端播放对应 3P 空间声音。切换视角不会重复触发收尾音。
- `D:\northstar\scripts\vscripts\weapons\sh_stim.gnut` 定义的屏幕资源是 `P_heal`，不是独立 `.raw` LUT。沿用其速度 0–360 映射到控制点 1 的 X=5–200，Y=999，Z=0；第三人称身体使用 `P_pilot_stim_hld`。
- 视觉/回血持续时间仍来自挂载脚本。速度状态遵循脚本的额外 0.5 秒尾段和末尾 0.25 秒渐退，倍率应用在 s&box 移动系统中。
- 长帧会正常消耗技能时间；回血仅累计该帧处于有效持续时间内的部分，不能跨过结束点继续回血。速度尾段不继续回血。换装/卸装清空尾段。
- 当前速度倍率、回血速率和 FOV 为明确的 s&box 适配参数，不宣称是原生函数逐帧恢复。
- 修复 PCF RGB/Alpha 转换：原版 `P_heal` 及两个子系统的颜色第四字节为 0，透明度由独立 Alpha 参数控制。转换器现在把 RGB tint 的 alpha 设为 1，保留独立透明度曲线；否则 s&box 的 `Color.a × Alpha` 会把有效粒子完全隐藏。

## 相位与屏幕效果隔离

- 复用 `P_phase_shift_screen`、`P_phase_shift_screen_start`、`P_phase_shift_player` 和原版 `materials/correction/mp_ability_shifter.raw`。
- `.raw` 通过现有挂载资源加载为完整 32³ 纹理；复用 `titanfall2_color_correction.shader` 进行显示 RGB 查表，不把该纹理错误塞进布局不同的原生 `ColorGrading` LUT 属性。
- 仅给本地第一人称当前相机注册独立 `CommandList`。切换相机/第三人称/自由相机、技能结束、替换、停用或销毁时移除本组件的命令；保留宿主已有后处理、地图调色和命令列表。共享挂载 LUT 不由技能销毁。
- 屏幕 PCF 使用专属视角标签，其它相机排除该标签。自定义 PCF Refraction/Ramp/Rope 渲染器实时同步对象标签，保证相机过滤生效。只移除本组件实际添加的标签。

## 主要代码

- `Code/Gameplay/Pilot/Tacticals/PilotTactical.cs`：主机计时、回血、抓钩触发/断开与清理。
- `PilotTacticalRules.cs`：速度尾段渐退规则。
- `PilotTactical.Presentation.cs`：声音与世界粒子。
- `PilotTactical.ScreenEffects.cs`：本地屏幕 PCF、相位 LUT 与相机隔离。
- `Validation/PilotTacticalPresentation.cs`：实际音频句柄、相机命令、原版纹理、屏幕截图及生命周期验证。

声音资源与材质表依据见 [音频记录](PILOT_TACTICAL_AUDIO_2026-11-28.zh-CN.md)。验证输出目录：`artifacts/pilot-tactical-effects-20261128/`。构建、资源读取、实际原生渲染与真人听感/多人实战分别记录，不互相替代。

## 适配边界

原版 PCF 的部分屏幕空间/viewmodel 标记及球面发射器平面约束尚未完整映射到 s&box。本轮复用实际 PCF 图、材质、声音和相位 LUT，并验证实际渲染；不宣称屏幕粒子布局或抓钩手感逐帧等同原版。真人听感、多人网络和实际地图中的最终体验仍需单独验收。

## 本轮构建与验证

- 官方 `MenuBuild.exe` 白名单编译通过；验证程序编译为 0 警告、0 错误。
- 52 项定向测试全部通过、无跳过，包含 PilotParkour、PilotTactical、ColorCorrection 和 ParticleColorChannel；资源测试使用实际 Titanfall 2 安装，包含 18 个代表声音事件的非静音 PCM 解码。
- `--pilot-tactical-presentation` 原生图形/音频专项通过：实际 Miles 句柄、第一/第三人称声路、延迟启动、结束停止；固定相机下的原版 Stim 粒子和 Phase LUT 像素变化；视角/相机切换、替换、停用、卸装以及宿主后处理和隐藏状态保留。
- 同一最终 DLL 的 `--pilot-tactical` 6 组行为验证全部通过：拾取/按键保留、Stim、抓钩拉动、相位碰撞/退出、脉冲侦测、重生持久化与卸装清理。报告：`tactical-native/runtime-results.json`。
- 长帧实测：Stim 回血 105.00006，理论预算 105；抓钩剩余 0.1 秒时跨过 1 秒帧，退出后充能从 77.36105 到 84.86105，与剩余 0.9 秒充能一致。
- 最终包：`artifacts/pilot-tactical-effects-20261128/package/package.local.titanfall2.dll`。
- SHA256：`2AF569C3963B29513E10B9E40728A1B0E4795EB86A8DB3BC973570BF08FBD249`；MVID：`292cae85-520b-422a-bfbf-033434b40f48`。
- 图形/音频报告与截图：`presentation-native/runtime-results.json`、`presentation-native/pilot-tactical-presentation-*.png`；测试日志：`tests-final.log`；白名单构建日志：`menubuild.log`，均位于本轮验证输出目录。

使用独立 native 测试 runtime，未部署到 Steam s&box，也未把这些专项结果当作真人地图或多人验收。
