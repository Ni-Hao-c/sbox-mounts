# 泰坦 NPC 技能与屋顶跨越改造

本轮将研究方案落实到独立 `sbox-mounts` 项目，使用 Steam 官方 s&box 的公开接口与沙箱编译。未修改引擎、原游戏文件或 IDA 数据库，未部署 Steam mount，也未提交 Git。工作区原有的武器、Pilot、渲染等改动保留。

## 已接入的行为

- **统一技能决策与动作生命周期**：TitanKit 负责自主技能及核心选择，动作有准备、单次提交、恢复、取消阶段；同步动作编号、动画角色、进度和决策原因。目标、视线、资源、模式、地面及动作权限在提交时复核。
- **技能配合**：Ion 能量、Vortex 防御/返弹与肩炮；Ronin 剑挡、相位、ArcWave、剑核心追击及连击间歇；Legion 射程切换、PowerShot 弹药、枪盾和 SmartCore；Northstar 起飞净空、悬停、降落及接管；Monarch 再装填、Tone 锁定/Sonar、Scorch 热盾消耗与地面技能条件。
- **运动与中断**：冲刺连续碰撞推进并消费次数；技能移动所有权阻止普通战术循环重新发出移动。死亡、禁用、处决、驾驶接管取消待释放动作。手动核心保留打断普通近战的既有规则。
- **零售配置**：读取 AISettings/WeaponScripts 的继承、SP/MP、AutoTitan/Enhanced 模式块，按内容快照缓存并保留来源。主枪按实际发射数执行 burst/rest。阵营不自动代表玩家归属或 AutoTitan 模式。
- **动作表现**：真实 MDL 活动映射为技能准备/主动作/恢复角色；有 `AE_NPC_WEAPON_FIRE` / `DIRECTED_FIRE` 时用同一时钟提交效果及采样动画。无对应事件的活动使用明确标记的结束时刻回退；缺失活动、近战和部分核心仍保留计时回退。持续防御保持对应姿态，允许的主枪并发保留。
- **屋顶跨越**：Spectre/Reaper 等按兵种能力接受跳跃、下降、攀爬连接；执行对齐、完整 hull 预检、扫掠推进、落地恢复和继续路线。保留零售根运动曲线，避免角色世界位置与模型根骨重复移动。普通 Titan 不获得步兵屋顶跳跃能力，Northstar 飞行有单独控制器。
- **感知修复**：直接目视的时间与队友共享情报分离；失去视线后不读取敌人的实时朝向/速度来驱动侧袭。

## 使用及调试入口

NPC 属性 `AiMode`、`UseRetailAiSettings`、`AiSettingsName`、`AiWeaponScript` 控制配置；`TraversalFamily` 和 `AllowTraversal` 控制跨越能力。普通友方/敌方默认使用 `EnemyMp` 的战斗配置语义，阵营关系独立；明确需要限制技能时再选择 AutoTitan 模式。

`Titanfall2TraversalLink` 提供作者指定的屋顶连接：设置局部起终点、Jump/Climb 类型、方向和连接半径。起终点必须有导航及承托碰撞，整个飞行 hull 必须可通过。`Assets/navigation` 的两个自定义 `.navlink` 资源需随项目内容一起编译/加载；只复制 DLL 不足以交付这两个资源。

局部自动发现是有界适配：仅查看已请求路径终点或已知接敌位置，每个场景最多保留 64 条，扫描间隔 0.5 秒。它不恢复原版 AIN，也不意味着任意屋顶都能自动到达。

独立原生场景已验证自动发现：清空全部显式连接后，仅下达屋顶移动目标，生成一条连接并完成落地/路线续行。双 Spectre 共用显式连接时峰值占用为 1，两者都到达屋顶。

运行时可检查 `AiAction/AiPhase/AiActionSerial/AiAnimationRole/AiDecisionReason/AiTacticalState`，以及 `TraversalPhase/TraversalCycle/TraversalSerial/LastTraversalFailure`。

## 验证与证据

本轮冻结产物结果：官方 `MenuBuild.exe` 沙箱编译成功；相关单元/零售数据测试 **78/78，通过且无跳过**；6 组原生回归全部通过，使用同一 MVID `c9d56622-09ef-472f-8c3a-3c492a767d53`。

| 原生套件 | 结果 | 报告记录数 |
| --- | --- | ---: |
| `--titan-ai` | 通过；技能资源/锁、策略选择、Ronin/Ion 时序、Northstar 落地/接管 | 11 |
| `--npc-traversal` | 通过；真实模型及导航、自动发现、双 NPC 占用、取消与恢复 | 16 |
| `--cores` | 通过；核心、近战打断、濒死/死亡清理及伤害约束 | 10 |
| `--npc-ordnance` | 通过；手雷相关既有回归 | 5 |
| `--titan-ion` | 通过；Ion 能量与执行器既有回归 | 4 |
| `--titan-fidelity` | 通过；泰坦武器/技能既有回归 | 10 |

记录数不是独立行为测试数量，尤其屋顶套件含 6 条资源注册诊断及状态记录。Ronin 释放时动画 cycle 为 `0.18918896`，对应零售事件 `0.189189`；目标提交时刻 1.5 秒，实际 1.516667 秒（一个 60 Hz 更新步）。Ion 无对应 fire 事件，验证的是明确的活动结束回退及 prepare/main/recover 顺序。Northstar 测得上升 185 单位并正常落地。

验证产物目录：`artifacts/npc-ai-implementation-20261128/`。最终构建、测试和运行报告以本目录的 `verification-summary.json` 为准；`native/*-results.json` 保存具体断言结果和程序集 MVID。`baseline/` 与 `baseline-hashes.json` 保存改造开始时的相关源码，便于与已有未提交工作区区分。

原生验证使用独立运行目录，二进制链接到同一 Steam 官方引擎，配置/日志/数据隔离。`.navlink` 是需预注册的 GameResource；TestAppSystem 无游戏包资源加载阶段，因此屋顶夹具从实际资源 JSON 重现包注册，然后通过真实 NavMesh/MoveTo 测试跨越，没有反射调用跨越执行入口。

最终核心回归使用 `validation-source-snapshot/` 中的验证源码快照：另一个会话正在修改 `PilotTacticalPresentation.cs` 并引入新的生产 API，因此该快照仅排除这一无关夹具及其 dispatcher 项。NPC/Titan 夹具、断言和受测生产 DLL 保持一致；未修改另一会话的源码。

收尾时另一个会话继续修改了 `ActorPrefabLoader.cs`、`Model.Animations.cs`、`Npc.Animation.cs`，加入骑乘组件及配对交互动画接口/所有权检查。三份文件的前后哈希见 `concurrent-source-changes.json`。上表运行结果严格对应冻结 DLL，不能归到这些后续修改；当前共享工作区的额外编译结果单独记入 `integration-latest-menubuild.log` 和验证汇总。

该最新共享工作区也已通过官方沙箱编译，产物在 `integration-latest/`。未把先前冻结版本的原生测试结果移记到这个新产物。

真实 Ronin/Ion 时间线夹具通过反射选定一次动作以隔离策略选择，后续使用真实场景更新、模型元数据和技能执行器；它证明执行时序，不证明完整原版战术选择器。

运行回归过程中额外修复了全身技能被普通寻路打断、剑核心绕过 AI 近战策略、持续防御姿态提前结束、运行中模式切换未取消准备动作，以及导航取消后提前一帧视为已释放连接的问题。旧核心夹具补充了濒死血池阶段断言后，再验证真正死亡取消核心，未取消原有死亡断言。

## 范围边界

这是基于已核实零售数据和执行规则的适配实现。尚未完整解释原版 selector 顺序、Boss 关卡阶段或全部 Frontier Defense 策略；AutoTitan 模式支持配置和技能限制，不代表完整主人跟随/守点系统。跨越使用根曲线水平进度和经碰撞验证的适配轨迹，Northstar 也不宣称与原版物理逐帧一致。真实地图自动屋顶发现、网络客户端表现和逐帧视觉对照需单独验收。

`Tests/`、`Validation/` 及 `artifacts/` 当前被仓库忽略规则覆盖。本轮新增测试仍保存在本地；后续制作提交或交付包时需要一并纳入，不能只依赖普通 `git diff`。
