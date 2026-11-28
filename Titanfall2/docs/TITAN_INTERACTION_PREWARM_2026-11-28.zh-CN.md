# NPC 泰坦交互资源预热与去重

2026-11-28。接续登离舱、敌方训牛术、友方搭乘实现。本轮改动保留于源码和本地构建包，未部署至 Steam 安装。

## 行为

- 可交互 NPC 泰坦在 `OnStart` 注册预热请求，在后续更新中通过现有 mount 预热队列逐项处理。仅准备当前机架用到的站立登离舱、六类 Rodeo 入口、拔电池/投雷/补电池/友方idle，以及铁御身体、1P手臂、骨架、电池和手雷模型。BT不加载未支持的Rodeo资源。
- 音效包含原脚本确认的启动声音、Rodeo的1P/3P入口和电池/投雷别名、跳离/电池UI声音，以及实际选中MDL序列的 `AE_CL_PLAYSOUND` 依赖。音频事件展开后复用既有单任务读取/解码队列和 `SoundFile` 缓存。预热不会生成声音播放实例。
- 同型号和模型的 NPC 共享加载中/已完成的请求。跨机型按资源种类和规范化路径共享不可变 Model/Skeleton/Clip；交互播放也读取同一缓存。若玩家在预热完成前交互，按需加载后的结果仍被后续队列复用。
- 每个铁御仍有独立的renderer、Graph、相机及动作时间。只共享资源，不共享可变播放状态。缓存不持有Scene/GameObject，随挂载重置清空；缺失资源也保留诊断，避免每台NPC重复加载。专服/headless不进行视觉/音频预热。
- `Titanfall2Npc.InteractionPrewarmStatus`和内部 `InteractionPrewarmErrors`可检查完成情况。`Finished`与`Ready`不同；存在真实缺失时保留 `Failed`和`Ready=false`。

## 代码

- `Code/Gameplay/Npc/Npc.Prewarm.cs`：生成时登记与更新调度。
- `Code/Gameplay/Titans/Interaction/TitanInteractionAssets.cs`：与Rodeo播放共用的资源清单及声音别名。
- `Code/Gameplay/Shared/Prewarm/AnimationResourceCache.cs`：挂载生命周期共享缓存。
- `Code/Gameplay/Shared/Prewarm/WeaponPrewarm*.cs`：复用现有预算、音频解码、缺失声音缓存及MDL声音依赖展开。
- `TitanInteractionPresentation.cs`：播放读取同一模型、骨架和片段缓存。

## 验证

官方 `MenuBuild.exe` 沙箱编译及原生验证工程编译通过；11项相关规则/去重单测通过，无跳过。

同一构建的完整 `--titan-interactions` 原生回归也通过：`D:\sbox\sbox-mounts\artifacts\native-b496c7e8580a4ebeab1233690b020be7\runtime-results.json`。实际登离舱、拔电池、投雷、友方补给搭乘与生命周期清理均保留；程序集MVID `8de775f9-5645-408e-bd68-13c637d52f4c`。

原生 `--titan-interaction-prewarm` 报告：
`D:\sbox\sbox-mounts\artifacts\native-e7a1f1ea8e8047208c067d0027c4bec6\runtime-results.json`

| 检查 | 结果 |
|---|---|
| 同时生成友方/敌方君主 | 共享同一个未完成请求 |
| 君主资源 | 37个不可变图形/动画资源，57个音频样本已准备 |
| 再次生成君主 | 共享已完成请求；图形/动画加载次数、音频解码加载次数均不增加 |
| 实际登机 | 成功，额外资源加载0 |
| 新增烈焰机型 | 恰好新增31个专属资源；通用资源不重复加载；55个音频样本准备完毕 |
| 预热过程 | 未创建声音播放实例 |

已知缺失：原始泰坦下机片段引用 `tone_disembark_stand`，当前挂载音频包未提供该事件/样本。君主请求因此记录1项Failed、Ready=false；其余资源正常加载。测试仅允许这一明确的已知缺失，其他错误仍使测试失败；没有制造替代声音或把缺失当作成功。

性能边界：这是资源预加载和复用，不是零卡顿保证。初次冷加载铁御模型的单个主线程步骤在此机器约7.7秒，现有2ms帧预算无法抢占一次native模型构建；报告中的后续约5.2秒等待不包含此前这一步。预热将首次成本移到生成后，重复泰坦和实际交互不再重复该资源工作；GPU首次绘制管线仍未认证为已预热。

音频证据：`D:\northstar\scripts\vscripts\rodeo\sh_rodeo_titan_anim.nut:200–219`、`rodeo/sh_rodeo_titan.gnut:134–180`及`players/mp/titan_*.set`。`sh_titan_embark.gnut:96–99,902–912,955`的旧登舱别名注册/播放已注释，本轮未将其伪装成当前有效事件。

构建包：`D:\sbox\sbox-mounts\artifacts\titan-interaction-prewarm-20261128\package`。本地测试、验证和artifacts目录保持仓库原有Git忽略策略。
