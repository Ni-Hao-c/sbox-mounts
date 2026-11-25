# Sandbox 挂载与原版 Titanfall 2：运行逻辑差距研究

日期：2026-11-25。范围由用户确认：**Sandbox 中的挂载内容**，不把独立 `ttf2` 项目的能力混入。比较源码基线为 `5dd849d`。本轮由三个子智能体分别研究动画、武器、泰坦脚本，主代理复核证据并检查部署基线与 native movement；没有修改生产代码、原版游戏进程或 IDA 数据库，也没有部署。

## 结论

当前挂载已经能加载资源、执行部分玩法，并利用原版配置。但多处仍在用通用的计时器、全身片段、统一遮罩和末端减伤代替原版运行语义。**只增加模型、动画片段或修复引擎接口，不能补上这些差异。**

上一轮“230项测试通过”“AG2骨骼发生变化”是功能与回归检查，不是原版还原度验收。例如，只要腿部在跑，变化骨骼计数就可能通过，并不能证明上半身瞄准、换弹遮罩或事件时点正确。这轮将两种验收明确分开。

另外，Steam安装目录里的挂载DLL仍为11月24日的版本，哈希与 `tf2-lmg-flash-20261124/package` 一致；11月25日新AG2分层包没有部署。检查时没有正在运行的sbox进程，因此只能确认磁盘安装，不能声称已检查某次游戏会话的加载模块。即使部署新包，下述源码差距仍然存在。

## 直接影响体验的差距

| 项目 | 已证实的原版证据 | 当前挂载 | 影响与处理 |
| --- | --- | --- | --- |
| 每枪后坐力 | `client.dll 0x18059C350 → 0x1802CFC50` 生成并写入soft/hard两组状态；`0x1805A7990`处理首发/连续倍率和zoom混合 | 有spread kick与fire clip，没有消费原版`viewkick_*`数据的对应通路 | 枪械手感主线。先恢复R-201的完整时序与spring，再扩展武器 |
| 空仓换弹 | 默认未改装R-201：普通2.2秒、空仓2.92秒 | `WeaponData.cs:33`合并成单一`ReloadSeconds`；两种动画使用同一ReloadTime | 空仓配置完成点早0.72秒。先拆normal/empty时钟，这是最容易验证的一项 |
| ADS与视模 | 原版每枪有独立sway/bob、hip/zoomed参数和旋转挂点 | 镜头/散布用连续zoom；视模接收bool aiming，中途开火选全ADS姿态；惯性为固定弹簧 | 需要统一zoom时钟与层混合，不能再增加几个互斥整段片段解决 |
| 第三人称瞄准 | BT idle/walk/run的autolayer指向3×3 aim grid；native SetPoseParameter链实际归一化、环绕并写入参数 | role导入拒绝单帧delta；Actor graph没有aim grid/pose参数 | 导入的数据在到达图之前已丢失语义，应先补metadata，再补求值 |
| 动作事件与遮罩 | 原版序列带reload、脚步等cycle事件和作者weightlist；泰坦reload部分hip/spine权重是0.1 | Actor主要靠倒计时，遮罩按骨名生成0/1；固定0.1秒过渡，换向未传相位 | 枪、手、腿、弹药和音效容易错拍。第一人称已有部分事件处理，缺口不能泛化成“全挂载没有事件” |
| 泰坦防御 | 零售脚本明确实现Ion捕获/记录/返弹；Legion枪盾是带独立health的吸收实体 | 多数防御在命中后按朝向乘伤害系数 | 属于交战规则差异，调减伤数值无法解决。可先做盾体积与真实弹丸交互 |
| 登乘与处决 | 原版有双参与者、reference attachment、第一/第三人称序列、事件、终止信号和控制形态交接 | 登乘默认0.8秒后SitMoveMode+SetParent；近战为固定前摇扫掠 | 需要共享的配对动作会话与事件清理协议，单放一段“处决动画”不够 |
| 泰坦生命/核心 | 原版有Doomed、弹射、持久soul状态、Monarch三层分支及技能互斥 | 普通泰坦致死后直接KillActor；只有Nuke特例；Monarch为固定路线 | 先建立normal/doomed/ejecting/executing/dead状态，再接动作/归属，不宜继续堆布尔量 |

散布的终值也不代表整个过程一致：原版 `0x1805BD8C0` 包含连续姿态混合和 `spread_lerp_speed` 的有状态限速，而挂载更多使用当前布尔姿态即时选值。另一方面，虽然native含time-to-max分支，本轮155份配置未见显式启用项，不把它夸大成所有武器的首要问题。

## Sandbox 与铁驭运动的边界

挂载的 `Weapon.ReadPilotParkourState` 只尝试从其它组件读取`WallrunSide`、`MantleProgress`，没有为默认Sandbox玩家安装原版跑酷控制器。拿到原版枪、进入原版地图，不会自动获得铁驭的运动反馈。

本轮已定位到 `GameMovement001` 注册、wallrun门控/候选检查、gravity-ramp实际读取路径，以及带 `CGameMovement::TraversalMove` 原版文本锚点的运动/第一人称动作耦合函数。它们足以证明不能用固定视模倾角替代原版运动，但还不是一套完整恢复的movement算法。

若继续做这部分，建议提供**明确启用的Sandbox铁驭模式/控制器**，保留普通Sandbox使用方式。先验证固定路线的速度与相机轨迹，再扩展跑墙、滑铲、攀越和网络预测。独立`ttf2`工程已经有PilotMotor，但这不证明挂载中存在该功能。

## 建议的实施顺序

下面是基于用户当前Sandbox体验的工程排序，不是已完成的动态A/B评分，也不虚构工期。

1. **锁定对照版本。** 确认部署包身份，给测试场景记录模块哈希、武器配置、机体、动作与相机；原版和挂载用同一套动作脚本录制。
2. **做两把标杆枪，而非同时扩所有武器。** R-201先拆普通/空仓换弹，再恢复viewkick、ADS同一时钟与每枪sway/bob；用Kraber验证另一类后坐力/瞄准行为。继续保留已修过的粒子局部速度与单tick多相机积分保护。
3. **补动画运行数据契约。** `SequenceRuntimeData`保留pose/grid/autolayer/weightlist/event/fade信息，先完成BT和士兵的aim+事件+作者mask；再处理相位连续性与一组手部IK。不要把Source1 helper直接当作MDL53数据兼容层。
4. **单独交付真实防御交互。** Ion先实现一种hitscan与一种导弹的捕获返弹；Legion实现独立枪盾体积和health。拦截发生在命中Actor之前，沿用宿主可信伤害来源。
5. **建立持久Titan状态与配对动作。** 先Doomed/弹射和驾驶关系，再用一个机体完成登乘/离舱的配对会话；处决复用该会话，按动画事件结算击杀/电池。随后再扩Monarch分支、Northstar核心和Mortar AI。

## 验收应改成什么

| 对象 | 要记录的结果 |
| --- | --- |
| 换弹 | 剩1发与0发的弹药提交点、首次允许开火时间、取消/续接、事件次数；默认R-201分别校核2.2/2.92秒配置路径 |
| Viewkick/ADS | 单发与连发的aim、camera、weapon recoil分别记录；zoom=0/0.1/0.3/0.6/1开火时检查枪口/FOV/姿态连续性 |
| NPC/泰坦动画 | 同一base phase下采样九个aim端点和中间点；比较每骨transform、枪口方向、作者mask、脚步接触与事件cycle |
| 盾 | 前后、边缘、命中扫描、实体弹、爆炸分别检查；记录捕获/能量/盾血/返弹归属和重复伤害 |
| 配对动作 | 双方共同起点与phase、控制权交接、灯光/击杀/电池事件；死亡、离线、销毁和取消都必须清理锁定状态 |

本轮没有启动原版进程做动态捕获。因此，代码/资源差异是证实项，“哪项对个人感受影响最大”与误差阈值仍需对照录像/采样确认。材质BRDF、曝光、反射、Miles DSP、完整IK求解与全网预测不在本轮完整恢复范围，不能据此宣称整款游戏的运行逻辑已经还原。

## 可复核的证据

研究根目录：`artifacts/retail-runtime-research-20261125/`（原始游戏提取件保留在本地忽略目录，不提交到仓库）。

- `baseline.json`：源码提交、安装包/新构建包/原版模块的SHA256与检查时进程状态。
- `animation-findings.md`：动画逐项证据。三个根模型及include共40个MDL/4248个sequence样本，包含campaign内容，不能当全游戏实际运行统计。
- `animation-native-evidence.json`、`animation-player-update.json`、`animation-retail-summary.json`：native pose/事件链与代表序列；完整字段在`animation-retail-data.json`。
- `weapon-findings.md`：武器参数到native运行路径、当前消费缺口；`weapons/weapon-source-hashes.json`与`weapons/native-*.c`包含155份配置来源和六个函数反编译证据。
- `titan-gameplay-findings.md`：零售脚本与C#对照；`titan/retail/manifest.json`记录494条脚本/配置的包来源和逐文件SHA256，不用Northstar覆盖冒充原版。
- `movement-findings.md`及`movement/*.c`：宿主边界、native movement入口、候选判定和traversal链。

实际检查的原版`client.dll` SHA256：`002b36487fec7c98882929fbccdb506d0146bbf3270cbb964a4d21c5edf7eebc`，地址基址`0x180000000`。这些地址只对应这个模块版本；反编译变量/函数自动命名不当作调试符号或完整ABI。
