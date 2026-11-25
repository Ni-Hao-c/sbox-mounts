# Charge Rifle 与 L-STAR 状态表现

数据来自本地零售 `client.dll`、WeaponData、Squirrel 脚本及 POV MDL，提取记录在 `artifacts/retail-runtime-research-20261125/charge-regen/`。本次表现层消费机械状态，不接收客户端提供的充能进度或射击许可。

Charge Rifle 的 windup/winddown 保留原 1P/3P Miles event，并复用已有多层音频、随机选择和衰减。阶段切换才启停声音，重复收到同阶段状态不会重新播放。IDA 汇编确认 windup seek 是 `fraction * charge_time`；winddown 是 `(1-fraction) * cooldown_time`，不是机械衰减使用的 `charge_cooldown_time`。普通 Defender 的前者为 1.3 秒、后者默认 0；机械衰减仍为 1 秒。详情见 `charge-presentation-native.txt`，反编译伪代码遗漏了 XMM3 中的 seek 参数。

`charge_effect_show_during_drain` 原生描述符默认 true，因此释放扳机后保留充能粒子，进度归零才清理；满充停止 windup，收枪/禁用销毁声音与粒子。粒子使用原名称和 muzzle attachment，不用通用发光球代替。重新开始充能保留机械进度，同时 seek 新声音。

原 Defender `charge_seq` 是 105 帧、30 fps 的循环动作，完整循环约 3.467 秒，并非 1.3 秒的机械进度动画。视模以原速循环，不能按 fraction 强行压缩它。当前使用站姿 charge 主序列；没有宣称恢复全部 crouch/slide/one-handed 动作选择。

L-STAR partial vent 保留 `LSTAR_VentCooldown` / `_3p`、SWAY_ROTATE 烟雾，以及 `cooldown_seq` 作者的 hip/ADS 双 cell。原 MDL 粒子事件进一步在 vent_spinner、vent_cover_L/R 创建对应效果。empty reload milestone 0 的四个 `xo_spark_med` attachment 与 `LSTAR_LensBurnout` 独立由宿主通知；中途恢复不重播 burnout。

视模 importer 增加原 `ACT_VM_RELOADEMPTY_LATE1..4` 及粒子 create/stop/control-point 事件。full/late1/late2/late3/late4 的帧数分别为 181/141/105/75/43；缺失的 late5 不伪造。L_HAND 控制点在动画期间持续更新。视模按宿主 reload snapshot 的 milestone、duration、elapsed 选择对应原序列。

专项原生验证入口是 `CheckWeaponChargeHeatPresentation`：检查预热完成、实际声句柄与 seek、真实粒子及纹理、充能骨骼变化、winddown 粒子实例保留与清理、cooldown hip/ADS 骨差及 late 阶段实际动画，另存 charge/vent/late1 截图。该 fixture 为表现观察注入已知权威状态；实际扳机、弹药与网络校验由独立机械 fixture 检查。运行结果由主代理统一记录，源码与 fixture 完成不等同于音画或真实多人验收。

首轮专项在真实预热阶段拦截了缺失的定向 PCF 映射。已按原 snapshot 中的实际定义补齐 charge/defender impact、L-STAR cooldown/overheat/impact、四 attachment spark 及 smokepuff 的来源。`charge-heat-pcf-definitions.json` 保存 9 个源 PCF 的 SHA256 与 275 个定义；各文件内所有 child 引用均闭合，无需全局扫描，也没有删掉失败的预热请求或放宽 Ready。`ChargeHeatParticleCatalogTests` 用 14 个实际根验证同一边界。

第二轮发现 `gen_def_CH_refrac_1` 被通用 sprite 预热路径错误要求 albedo。原 `particle/refract/simple_ring_wide_nml` VMT 实为 Refract，使用独立 normal/opacity 纹理。现增加 mount 内 `Titanfall2ParticleRefractionRenderer` 与 `titanfall2_particle_refraction.shader`，读取实际场景颜色、按法线偏移并以 opacity/粒子 alpha 衰减；没有将法线画成彩色贴片。预热把这两张纹理作为数据纹理校验。world 与 GameOverlay 均只在 `Translucent` 颜色 pass 绘制，通过 `WantsFrameBufferCopy` 请求当前 pass 的场景拷贝；不在无颜色的 refraction stencil pass 抓图，也不向持久 object attributes 缓存该纹理。amount/blur 的适配单位是渲染像素，不宣称原版着色器量纲或像素一致。

新增小型同进程 GPU 证明：原 PCF 单层通过真实 factory 建立，检查 normal/opacity、实际 draw 和粒子数，在纹理背景上比较折射开/关，要求 alpha=0 完全恢复背景。`Paused` 只冻结年龄，仍会执行 appearance 和 post-step；因此独立材质测试关闭曲线更新器并注入固定单粒子，额外断言跨帧 alpha/size 不变。真实 charge 验证在该测试之前运行，保留原始 PCF 生命周期。该证明由主代理统一编译 shader 和执行，实际运行结果另存报告。

2026-11-25 17:31:46 至 17:34:51（本地时间）Steam 切换并更新至 Borealis 分支，BuildID 26888677。旧报告使用 `Sandbox.SceneRenderer`，此后重建使用 `Sandbox.Rendering` 与 Borealis；两组结果不能当作同一渲染环境。新 `ContentFiles.FindGame()` 从 `AppContext.BaseDirectory` 向父级寻找 `core/shaders`，不读取工作目录或 `FACEPUNCH_ENGINE`，因此专项程序应位于隔离运行环境的子目录。fixture 改用 `Graphics.FlushGPU()` 等待当前绘制设备；Borealis 分支中的旧 Source 2 `g_pRenderDevice` 调用不能证明新 GPU 队列已完成。新引擎的编译和渲染结果需单独记录。

本批交付状态：更新后的官方包构建成功，31 个挂载 shader 按新 ABI 4 编译成功。换弹和投掷实际输入各 7 项通过。Charge 167/167 预热、原动画骨骼变化、音效 seek 与释放清理通过；清理检查按引擎帧末 Destroy 语义执行。完整表现专项尚未通过：Borealis 下自定义背景未绘出，充能截图未清晰显示枪身，折射像素结果不可验收；后续运行又检出 L-STAR 23 个预热失败项（纹理、定向 PCF、精确音效名），未放宽 Ready 判定。失败原始报告保存在 `weapon-presentation-borealis*.json`。用户要求加快并自行测试，因此本批部署已通过输入检查的实现，不再追加验证；不宣称画面、全部预热或真人多人已通过。
