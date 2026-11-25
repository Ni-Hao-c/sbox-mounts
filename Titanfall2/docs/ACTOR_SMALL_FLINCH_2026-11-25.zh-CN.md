# NPC 小受击动作：原始 additive gesture

原有 actor graph 已有移动主体、fire、reload 三条独立通道及已证实的 aim autolayer，但没有受击通道。模型角色筛选还会无条件排除 delta 动画，因此原资源中的小受击 gesture 根本无法进入 graph。

本次只接 `ACT_GESTURE_SMALL_FLINCH`。本地零售数据可确认：

| 原模型 | 原 sequence | sequence/animation flags | 帧数 | autolayer |
|---|---|---|---|---|
| `models/humans/pete/pete_reactions.mdl` | `gesture_flinch_add1` / `add2` / `add3` | 20 / 4 | 20 / 21 / 21 | 无 |
| `models/titans/medium/titan_medium_MP_reactions.mdl` | `AI_Flinch_head` / `chest` / `right` / `left` / `right_arm` / `left_arm` | 20 / 4 | 31 / 39 / 46 / 49 / 46 / 49 | 无 |

这些序列都是单 cell、非循环 delta，作者淡入淡出均为 0.2 秒。步兵的非零权重集中在 57 根骨骼，腿部不在该 mask 中；medium Titan 保留自己的更广骨骼权重，不能强套步兵 upper-body mask。原始提取摘要见 `artifacts/retail-runtime-research-20261125/small-flinch-sequences.json`，其来源是同目录 `animation-retail-data.json` 的原 MDL metadata 导出。

Importer 给该活动分配 `tf2_small_flinch` 角色，并额外要求 sequence 与 animation 都是 delta、单 cell、非循环且无 autolayer。AG2 clip 使用已有真实 additive 导入路径：缺失骨骼为零位移、单位旋转与零 additive scale，原 scale 减一，保留该 sequence 的作者 mask。该角色不会加入主体 selector。新的最后一层使用 additive 混合，移动、fire、reload 仍由各自时钟控制。

宿主只在原生 damage listeners 接受且扣到生命、角色存活时发送 `HostOnly` cue；护盾完全吸收、取消、零伤害不触发。每个 cue 允许完整原 clip 结束，连续命中不会把受击动作钉在第一帧。死亡沿用释放 actor graph 的原路径。

范围限制：当前每模型沿用角色候选排序选择一个真实小受击 clip，没有恢复原加权活动随机选择、全身 stagger、受击方向决策或 AI flinch schedule。未知 4096/8192 autolayer flags 仍只保存 metadata，未猜测执行。没有改变伤害、导航、武器射击/换弹规则。

新增 `ActorFlinchTests` 检查活动分类和 delta 约束。专属原生验证入口 `CheckActorFlinch` 检查宿主真实伤害 cue、护盾/零伤害排除、重复命中不重启，并以相同时钟的无 flinch 对照 renderer 检查移动+fire、移动+reload 下的实际骨骼变化及步兵腿部 mask。该验证另存截图，须由统一 build/native 运行后记录结果；本文件不把源码检查当作原生或多人验收。

2026-11-25 原生复核通过：`artifacts/behavior-fidelity-20261125/flinch-debug/report3.json`，实际加载 MVID `ca31ca8c-872e-4860-a4e6-72264bbab2ed`。步兵的 fire+hurt / reload+hurt 分别改变 26 / 24 根骨骼，腿部均为 0；Monarch 为 42 / 42 根。两个角色宿主有效 cue 均为 1，护盾吸收、零伤、连续命中不重启、动作结束及重新触发断言通过。仍未做真实多人传输验收。

初版 fixture 误把已先运行并受过伤的 actor graph 与后来创建的 control graph 比较。新增受击前 baseline 后证实，尚未开启受击层就有差异；仅同位置和更精确旋转比较仍未解决。现在先独立验证宿主 cue，再为骨骼比较同时重建两套 graph，要求 baseline 骨骼变化数为 0，随后测试叠加。位置 0.001、旋转 0.02 度和步兵腿部零变化断言均保留；没有调整原动画 mask 或 production。逐骨数据同时记录 render-local 与 native parent-space 差值，失败两轮证据也保存在同目录。
