# 官方分支优化对比与 TF2 移动时模型突现排查

检查日期：2026-11-25（Asia/Shanghai）。用户确认现象发生在**地图加载完成后移动时**。

本轮执行 `git fetch upstream --prune`，取得 Facepunch/sbox-mounts 的 19 个分支引用；没有切换工作分支、合并实验分支或改动运行时代码。当前工作分支为 `codex/titanfall2-upstream-authority-20261125`，已有未提交的玩法、模型和预热改动保持原样。

结论：优先验证移动时的距离硬裁剪和分帧 PVS 恢复；优化方向优先考察静态道具的原生聚合提交。继续提高预加载数量，不能直接解决已经驻留的模型被隐藏后突然恢复的问题。下面区分源码事实、待验证假设和建议；没有声称本轮取得 FPS 提升。

## 1. 官方分支取得情况

完整 SHA、提交时间和与 main 的提交差异见 [branches.json](../../artifacts/upstream-performance-review-20261125/branches.json)。表中的分支名称省略 `upstream/`。

| 分支 | HEAD | 对本问题的价值 |
| --- | --- | --- |
| main | e38c19a | 当前官方基线；探针图集、水面捕获复用、NPC 首帧模型绑定已进入其历史 |
| ns2-map-support | 9693ab4 | 静态地图和道具按材质聚合；重复模型共用几何；合并物理资源，最值得优先实验 |
| ns2-npcs | 1a0b310 | NPC 功能线；不能把另一条 NS2 地图分支的实现自动视为此分支具备 |
| goldsrc-maps | 56ad57e | 原生 AggregateRenderer、空间碎片、并行 lightmap/texture 数据处理 |
| goldsrc-map-support | 17e4184 | 较早地图支持线；不宜覆盖当前 main 的后续改动 |
| atlas-probe-textures | ba281be | 早期探针纹理数量上限方案；不应替代已合并的 v2 |
| atlas-probe-textures-v2 | 8521d37 | 共享 3D 探针图集；已合并 main 和本地 HEAD |
| halo2-mount | ee8018d | 有界并行地图/动画解析、避免读取不用的 mip 数据、正确材质批次身份 |
| overgrowth-mount | 9318f10 | 动画帧并行生成；导航步进与渲染插值分开 |
| renderware | 66bfbdb | 空间 cell 内用 bindless 纹理索引合并几何，按不透明/裁剪/加法通道分组 |
| idtech3 | 3c55069 | 按材质、lightmap、fog 等状态分组；保留物理表面类别 |
| doom | 6009c54 | 静态世界和移动平面分别按材质合批，减少逐面对象 |
| doom-monsters | 599b71a | 以怪物与弹体功能为主，本轮未深入逐个玩法实现 |
| s1-water | f846274 | 同平面水面复用捕获，避免重复反射/折射；已合并 main |
| hl1-npcs-and-weapons | 27d2527 | 绑定首帧 NPC 模型的修复已进入 main；不是大地图稳态 FPS 方案 |
| deathmatch-classic-weapons | 0ca3af1 | 武器功能线，本轮不作为静态世界优化来源 |
| opposing-force-npcs | 4cb6821 | NPC/特效功能线，本轮不作为静态世界优化来源 |
| source-breakable-glass | abf44c5 | 玻璃玩法线，本轮不作为主要优化来源 |
| source1-weapons-npcs | a585b52 | 玩法扩展线，本轮不作为主要优化来源 |

上述是全部分支的拉取和范围筛选，不代表逐行审计每条分支的全部玩法代码。重点优化源文件已保存到 `artifacts/upstream-performance-review-20261125/<branch>/`；本地关键源码快照在该目录的 `local/` 下。

## 2. 值得移植的具体做法

### NS2：减少提交对象，同时保留 fragment 级裁剪

关键提交 `355c315`，并检查 `ns2-map-support@9693ab4` 的后续状态。

- `NaturalSelection2/Code/Resource/StaticGeometry.cs` 按 `(Material, Shadows)` 汇集不透明碎片，以 `AggregateRenderer.MaxFragments` 分块提交。
- 重复 Part 共用顶点范围；镜像实例单独选择反转后的索引范围，避免聚合后背面剔除错误。
- 每个 fragment 保留自己的 bounds 和 transform，不是简单把整张地图合成一个不可细分的大包围盒。
- 静态道具的透明部分继续单独处理，物理资源保留表面信息。

TF2 的世界 BSP 已经通过 `Titanfall2WorldAggregator` 和 `MountedAggregateBuilder` 使用同类原生聚合，并有后台几何准备。因此收益候选主要是**静态道具提交**，不能把“给整个 TF2 加聚合”当成全新优化。

本机引擎源码 `Sandbox.Engine/Scene/Components/Render/AggregateRenderer.cs` 与 `Systems/SceneSystem/SceneAggregateObject.cs` 将该路径定义为逐 fragment 裁剪、单次 indirect 提交。这里指官方公开组件路径；TF2 当前 `GpuStaticScene => false`，不是重新启用旧的私有 GPU 场景后端。

移植限制：TF2 道具有逐实例探针系数、皮肤、LOD、局部光照和特殊材质。NS2 的实现不自动提供这些语义。公开 `AggregateFragmentInfo` 主要给出 bounds/transform，不能假设简单替换组件就能保留 TF2 逐实例探针与 LOD。应先挑选一个保留光照、材质和轮廓的模型集合做 A/B；受支持的 native instancing 仍应作为其他模型的保底路径。

### RenderWare：用材质表减少材质切换

`renderware@66bfbdb` 的 `RenderWare/Code/GTA/Resource/World/WorldLoader.Geometry.cs`：按空间 cell 汇总静态世界，将 bindless 纹理索引写入顶点；不透明、alpha test 和 additive 分开处理。

不要照文件里“最多两次 draw”的旧注释推算实际 draw 数：当前代码还包含 additive 材质。TF2 世界已经有 `WorldMaterialTable`，因此应借鉴静态道具侧的分组/绑定策略，而不是重复建设世界材质表。TF2 多纹理、多通道 shader 不能直接套用 GTA 的顶点格式；cell 过大也会削弱裁剪精度。

### Halo 2 / Overgrowth：把准备工作放在加载阶段

- `4372cd2`：Halo 2 最多四个 worker 并行解析地图，在挂载线程按原文件顺序发布；纹理解码只读该加载器实际使用的 base mip 所需数据。它没有证明“删掉 TF2 的 mip 链就能提高 FPS”，也不是稳态渲染优化的证据。
- `deac742`：Halo 2 动画解码/构建提速。
- `fb95364`：Overgrowth 并行采样待挂载的动画帧，主要作用于资源准备。
- `9318f10`：Overgrowth 将导航步进和渲染插值区分开，可参考其调度方式，但不能直接推导 TF2 FPS 增幅。
- `001c539`：Halo 2 避免不同纹理/tint 绑定误用同一批次身份。合批必须保留材质语义；不能只看 shader 名相同就合并。

TF2 已有 processed cache、材质/纹理预取和世界几何并行准备。需要统计尚未命中的加载工作，再决定移植哪一段，不能仅增加线程数。

### Source：探针图集与水面捕获复用

- `8521d37` 把各叶子的 3D 探针纹理按尺寸/格式合并，主攻纹理槽压力和资源数量。该提交已在本地 HEAD；TF2 静态道具使用 `GpuBuffer<Titanfall2ProbeData>`，并非每个叶子三张纹理，所以不能照搬其纹理减少数字作为 TF2 收益。
- `f846274` 复用同平面水面捕获。只在水面反射/折射确为开销来源时具有针对性，不能解释所有地图的道具突现。
- `f9a4d41` 在 NPC 第一帧前绑定模型，适用于首帧完整性，不能解释用户确认的“加载完成后移动时”现象。

## 3. TF2 当前代码中与模型突现相关的事实

### 已有全量加载期准备

`StaticPropStreamer.Preparation.cs:5` 的 `OnLoad` 等待 `PrepareResources`；其中 `:45` 的循环处理所有静态道具，之后构造批次并在 `:73` 设 `PreparationComplete = true`。`ResidentWorld` 默认为 true，控制驻留碰撞；完整预加载静态渲染模型并不以该开关为条件。

`WorldMaterialStreamer.cs` 虽仍存在，但源码搜索没有发现把它添加到地图的调用；文件本身也明确写明目前未安装。不能仅凭类名就认定当前世界材质在运行时流式替换。

`StaticPropStreamer.Runtime.cs:43` 仍有 `!PreparationComplete` 时的分帧加载兜底。若实际使用的旧部署、动态实例化入口或加载失败绕过正常 OnLoad，仍需检查该分支是否运行。当前没有运行中的 s&box 实例（CLI `--list` 返回空数组，进程检查无 sbox 进程），无法确认用户复现时的部署版本和运行状态。

### 候选原因 A：距离硬裁剪

`StaticPropStreamer.Visibility.cs:229` 的有效绘制距离取全局上限与模型 authored fade distance 的较小值；`:236` 用距离比较决定保留与否。`:264` / `:267` 修改独立对象的 `RenderingEnabled`，`:284` 更新批次的保留实例集合。

- 默认全局距离为 8192；120 FPS 预设改为 6144。
- 达到阈值后是隐藏/显示，没有在这段逻辑中实现连续 alpha fade。
- 半径达到 256 的地标豁免，关键掩体未必达到这个尺寸。
- **`SBOX_TITANFALL2_PROP_RENDER_DISTANCE=0` 只取消全局上限，模型自身的 fade distance 仍会生效。**
- 设置从 `FileSystem.Data/titanfall2.settings.json` 读取，并在启动时固定部分值；不能假设设置进程环境变量或运行中改文件就已生效。

这段代码可以让已在内存中的道具在移动时突然出现。它是确定存在的行为；是否对应用户看到的具体道具，尚需复现定位。

### 候选原因 B：可见性恢复排队

`StaticPropStreamer.cs:31` 把运行时 sweep 限制为每帧最多 16 个 cell、约 1.5 ms；`:278` 的 `ProcessRuntimeClusterQueue` 同时处理隐藏与恢复。

`StaticPropStreamer.Visibility.cs:68` 在 PVS cell 或距离格子变化时更新可见集合；实际 `RenderingEnabled` 的写入要等 cluster 队列走到相应对象。队列不在每次移动时重置，这避免饿死远处 cluster，但也意味着新可见物体可能晚几帧才恢复。预算在 cluster 完成后检查，也不是严格的单帧耗时硬上限。

`ShouldRefreshCells` 还有 256 单位格子、192 单位移动迟滞和 512 单位兜底距离。若未跨 PVS cell，距离裁剪使用的 anchor 本身也会离散更新。这会造成“走一段才一起出现”的可能观感。

这是可从调度代码推出的延迟风险，不是本轮已经拍摄复现的结果。纯原地转向未必改变这套调度的 PVS/距离状态，需要与移动跨边界区分。

### 准备完成与 PvP 放行不是同一个条件

`WorldReady.cs:25` 主要核验世界/道具碰撞并发布 `ttf2_map_ready`。它没有直接核验当前摄像机的可见性恢复队列已应用，也不是 GPU/着色器首次使用完成的证明。`ResourcesPrepared` 可以表达成功加载静态道具，但当前 readiness 不能直接作为完整 PvP 画面就绪凭据。

## 4. FPS 的候选开销

`MountedScenePerfKit.cs:380` 的每次 `RenderSceneObject` 回调进入 `DrawPerInstanceLods`，按实例计算距离、生成四档 LOD 分组、重排 transform 和 source index，并逐个非空 LOD 提交。量化 camera 保持分组稳定，但这段函数没有因此跳过重新分组的循环。成本可能随视图、pass、批次数和实例数增加，需要 profiler 确认占比。

`StaticPropLightProbe.cs` 已有八槽索引表缓存和 depth-prepass 跳过探针绑定，不能把“添加探针缓存”作为尚不存在的优化。可评估的是按 draw-set 版本、摄像机格子和 LOD 策略缓存不可变分组；必须处理多视图并发、阴影视图与资源释放，不能使用一个全局共享可写数组。

推荐实验顺序：

1. **消除竞争性画面缺失。** 对关键掩体/遮挡几何取消距离消失，允许轮廓一致的低 LOD；把 authored fade 与全局距离开关分开。装饰物可另行使用更积极策略。
2. **新可见集合优先恢复，延迟隐藏。** PVS 切换后保守保留旧集合并及时启用新集合；昂贵实例重排可分帧。不要为遵守隐藏/回收预算而留下可见空洞。仍需验证 PVS 数据自身的正确性。
3. **减少重复 CPU 提交工作。** 对现有 native instance 路径做 LOD 分组缓存 A/B，同时以 NS2 原生 aggregate 做适配试验。两种方案都要保留逐实例光照、材质、LOD 和视图语义。
4. **再评估物理合并。** 借鉴 NS2 的静态碰撞分组，降低对象/物理体管理成本；保留 TF2 的 contents、surface、触发器和碰撞清单验证。不能直接把带有不同碰撞语义的所有道具并成一个 body。

不建议直接套 120 FPS 预设作为这次修复：它同时缩短绘制距离、提前 LOD、调整阴影、放大批次/世界 cell，难以定位收益与突现来源，也不能保证实际达到 120 FPS。

## 5. 可复核的运行时验收

本轮没有更改渲染实现，没有新增运行时 FPS 数据。下一轮固定同一地图、路线、分辨率、画质、硬件与部署版本，先用 Angel City 建基线，再补一张长视距地图：

- 加载结束记录 `PreparationComplete`、`ResourcesPrepared`、模型失败数、碰撞就绪、有效配置与部署标识。
- 同时记录模型加载/纹理解码计数和 visible/culled 数。出现突现时计数不增长、可见集合变化，才支持“裁剪而非重新加载”的现场判断。
- 分别对照：原配置；只取消全局与 authored 两层距离隐藏；只改变 PVS 恢复策略。每次只改一个因素，避免同时开启性能预设。
- 路线包含高速移动、门洞/街角、远距离掩体、传送、反向折返；录像对照是否出现“碰撞在、画面不在”。
- 记录 CPU/GPU 帧时间、p50/p95/p99、1% low、各 pass 批次数/实例数、显存峰值和运行时创建资源次数。仅缩短加载时间或降低 draw 数，都不能算已经提高实战 FPS。
- 冷加载与暖缓存各记录一次；开启正常阴影/光照验证，不把无阴影测试数字冒充完整画质收益。

若正常 OnLoad 的资源计数仍在移动时增长，再转查动态模型、武器/特效预热和实际部署版本；不会仅根据用户描述排除这些可能。

## 6. 本轮交付

- 官方 19 个远端分支已 fetch，保留当前工作区改动。
- 分支清单和 23 份重点源码快照保存在 `artifacts/upstream-performance-review-20261125/`。
- 本报告仅记录调查和实施优先级。尚未执行代码修复、构建、部署或同路线性能 A/B；尚未认定用户的具体突现原因。
