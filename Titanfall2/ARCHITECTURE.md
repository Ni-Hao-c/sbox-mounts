# Titanfall 2 挂载：技术参考

本文记录挂载的数据流、零售版文件格式中确认过的结论、对象所有权规则和流式加载预算。功能概览和使用方法见 [README.md](README.md)，配置项见 [ENVIRONMENT_SWITCHES.md](ENVIRONMENT_SWITCHES.md)。

文中"零售版"指 Steam 上发行的 Titanfall 2 客户端及其资源文件。

## 官方构建

项目配置与其他挂载一致，统一入口为仓库根目录的 `BuildMounts.bat`。`Build/build.ps1` 调用官方 `SboxBuild build-mount`。项目启用正常沙箱编译，不调用私有缓存或 GPU 命令接口。构建、安装和测试方式见 README。

## 1. 分层和线程

代码分三层，依赖方向只能从上到下：

1. **Formats**（[Formats/](Code/Formats)）：容器与二进制格式读取。大部分解析输出普通 .NET 数据；当前 RPAK/VTF 纹理文件仍同时包含引擎纹理创建方法，调用方必须区分 CPU 读取与主线程创建入口。
2. **Resource**（[Resource/](Code/Resource)）：资源加载器和场景组件。这一层把解析结果转换成 `Texture`、`Material`、`Model`、`SoundFile` 和场景对象。
3. **场景**：`MapLoader.BuildScene` 把组件装配成可序列化的场景。

线程规则：

- 后台线程只读取文件、解压和准备 CPU 数据。
- `Texture`、`Material`、`Mesh`、`Model` 和场景对象只在主线程创建。
- 材质预取（[Resource/MaterialPrefetch.cs](Code/Resource/MaterialPrefetch.cs)）最多同时准备 4 个材质，以 128 MiB 为已准备但未消费数据的目标预算。所有保留的 payload 都记账，包括跨过目标的那一份；达到目标后停止后续贴图预取，未预取的绑定仍由普通加载器加载。已经开始的解码允许暂时超出目标，不把这个值当作硬内存上限。峰值、目标与释放后的剩余字节分别记录。

挂载经过官方项目编译器改写，回调接口由生成器加入，组件和 `[ConCmd]` 由正常 addon 加载流程注册。源代码不手工声明内部回调接口，不自行反射注册 TypeLibrary 或命令程序集。

## 2. 源码地图

| 文件 | 职责 |
|------|------|
| [GameMount.cs](Code/GameMount.cs) | 安装检测、资源注册、卸载清理。 |
| [Titanfall2StreamingSettings.cs](Code/Titanfall2StreamingSettings.cs) | 数据文件系统内的配置读取。 |
| [Formats/Vpk/VpkArchive.cs](Code/Formats/Vpk/VpkArchive.cs) | VPK 目录树和数据块。 |
| [Formats/Rpak/](Code/Formats/Rpak) | RPAK 解压、资源表、StarPak 索引、纹理解码、DXBC 反射。 |
| [Formats/Bsp/](Code/Formats/Bsp) | rBSP lump、CM 碰撞、光照、探针、雾体积、光照环境、实体文本。 |
| [Formats/Mdl/](Code/Formats/Mdl) | MDL v53、VTX、VVD、VPHY、动画。 |
| [Formats/Miles/](Code/Formats/Miles) | Miles 银行索引、BCF 解码、声音事件会话。 |
| [Resource/Map.cs](Code/Resource/Map.cs) 及 `Map.*.cs` | 地图场景构建。 |
| [Resource/StaticPropStreamer.cs](Code/Resource/StaticPropStreamer.cs) 及分部文件 | 静态道具加载、批次、可见性、碰撞流式。 |
| [Resource/MountedScenePerfKit.cs](Code/Resource/MountedScenePerfKit.cs) | 与游戏无关的实例批次、世界聚合和剔除工具，命名空间 `Sandbox.Mounting.PerfKit`。 |
| [Resource/Model.cs](Code/Resource/Model.cs)、[Resource/Material.cs](Code/Resource/Material.cs)、[Resource/Texture.cs](Code/Resource/Texture.cs) | 资源加载器。 |
| [Resource/ProcessedCache.cs](Code/Resource/ProcessedCache.cs) | 磁盘处理缓存。 |

## 3. 内容上下文、资源注册和归档

2026-11-08 起，挂载先通过 `Code/Content/ContentDiscovery.cs` 建立 `Titanfall2ContentSnapshot`，再由 `Code/Content/ContentMount.cs` 生成并注册资源目录。运行期间不重新扫描包或 Mod。全部 VPK 成员、loose 文件、没有独立 loader 的依赖以及 RPAK/Miles 逻辑资源保留在虚拟目录；引擎目录只发布可转换资产。

同一路径按固定层顺序 first-wins，候选来源保留用于 explain。基础顺序为全部启用 Northstar Mod 的 loose 覆盖（LoadPriority 降序；同级按绝对目录 ordinal 排序），再按同样 Mod 顺序处理 RPAK → VPK，然后 retail RPAK → retail VPK → 生成天空材质 → Miles。loose 优先于所有 VPK，符合 Northstar 文件替换 hook。旧原版路径保持兼容。

路径、BSP 分区、模型伴随文件、动画 include 和材质源从同一个固定快照读取。bound source 持有创建它的快照，不回查当前 mount。loose 文件有 SHA-256 身份；源内容改变后旧读取失败。包以长度、头尾采样和既有包格式校验管理版本，不能声称它等同于强内容哈希。RPAK/STARPAK 位置与版本在发布前固定；VPK 检查目录及实际引用的分卷。读取时使用独立文件流，没有跨卸载持有的共享 Stream.Position。

### VPK

- 目录树和数据区间都做边界检查，字符串必须有明确的终止符。
- 数据来源由条目的归档索引决定：`0x7FFF` 表示目录文件自身，其他值表示 `_NNN.vpk` 分卷。外部分卷读取失败时报错，不返回空数据。
- 本地化目录 `englishclient_*.vpk` 的数据分卷映射到 `client_*.vpk`。
- `vpk.json` 的 `MapOnly`（第三方约定，Northstar main 只读 `Preload`）与 `Preload` 一样预载对应包：未指定 `NORTHSTAR_MAP` 时按其声明地图载入并留下诊断，选中其他地图时仍推迟。`MapOnly` 声明的地图同时作为该 Mod 的 RPAK 地图上下文；`Preload`/`MapOnly` 的键容忍 `client_x`、`client_x.bsp` 与完整目录文件名三种写法。
- 压缩块使用 LZHAM，字典大小为 2^20。解压由 [Formats/Rpak/](Code/Formats/Rpak) 中的纯 C# 移植负责（`LzhamDecoder` → `LzhamDecompressor` → 符号解码、前缀码、码长生成和校验），不再有原生桥或随包 DLL。
- `depot/` 路径下的镜像条目不注册。

### RPAK 和 StarPak

- 纹理的低分辨率 mip 在 RPAK 中，高分辨率 mip 在 StarPak 中。
- StarPak 索引要求每个条目都位于数据区内。出现重复或越界条目时整个索引作废，并且不缓存这次结果。
- 着色器使用 DXBC 反射得到常量缓冲区和纹理寄存器布局。反射数据中没有 `D3D_SVF_USED` 标志的变量视为未使用。

### 纹理

- 纹理边长上限由 `SBOX_TITANFALL2_TEXTURE_MAX` 决定，超出的高分辨率 mip 不读取。
- BC 压缩纹理只上传到 4×4 的 mip 级别，更小的级别不上传（[Resource/TextureMipUpload.cs](Code/Resource/TextureMipUpload.cs)）。
- VTF 动画纹理额外注册为一张图集（`animatedVtfAtlas`），图集带完整 mip 链。

## 4. 地图构建流程

`MapLoader.BuildScene` 的步骤：

1. 读取 BSP，解析世界几何、CM 碰撞、光照贴图和静态道具光照探针。专用服务器不解析光照。
2. 判断 CM 碰撞是否完整：不为空，且没有被拒绝的图元。专用服务器在不完整时抛出异常。客户端在不完整时，从渲染几何构建备用碰撞。
3. 构建世界渲染几何：
   - 不透明几何聚合成少量大网格。
   - 半透明、贴花、godray、水面和只投射阴影的几何各自生成 `ModelRenderer`，水面使用 `Titanfall2WaterRenderer`。
4. 创建碰撞：
   - 按 contents 分组的 CM 碰撞体，配合 `Titanfall2CollisionManifest` 使用。
   - 或者使用备用碰撞。
5. 添加 `Titanfall2DeferredNavMeshBuilder`。
6. 非专用服务器上继续创建：
   - 环境：太阳、雾、显示后处理、颜色校正
   - 局部光源控制器
   - 立方体贴图探针
   - 3D 天空盒
7. 完整场景创建出生点和 `Titanfall2MapCamera`。纯世界场景不创建。
8. 创建粒子、地图音频（专用服务器除外）和动态道具（纯世界场景除外）。
9. 创建 `Titanfall2StaticPropStreamer`。普通地图默认也使用常驻碰撞；纯世界与专用服务器始终常驻。所有常驻模式添加 `Titanfall2WorldReady`，验证真实物理资源后才发布就绪标签。

### 编译式静态场景数据

新导入使用 TSP2 v9，保留原有实例、探针、skin、淡出和碰撞来源字段，附加编译好的空间分区、模型/skin 分组及 SHA256 校验。布局用稠密 InstanceId 引用实例；它与可能稀疏或缺失的 BSP SourceIndex 分开。实际场景直接恢复分组，旧 v3–v8 载荷在加载时编译一次。

TSV2 v2 保存定位树、门户与预计算的逐区域可达 mask，并带 SHA256 校验。运行期间只做区域定位和查表；旧 v1 在加载时编译可达表。深度限制、单向门户、AlwaysVisibleCells 与空 mask 的可见策略保持原语义。

`GetCompiledSceneStatistics()` 显式生成统计快照，包含场景代际、实例/模型/skin/空间分区数量、PVS 表大小、真实批次直方图、材质身份数和每模型 LOD 几何字节数。统计不在逐帧执行，也不保留 CPU 顶点数组或延长 native Model 生命周期。材质身份数不等于最终可合并的 GPU pipeline 组数。

粒子库索引和地图系统创建在组件 OnLoad 的分段预算内完成，OnUpdate 只处理已有系统的激活与模拟恢复。`ResourcesPrepared`、`PendingSystemCount` 和 `FailedSystemCount` 提供准备结果；坏载荷或缺失资源不会被记录为完整准备成功。

当前官方沙箱版本使用原生实例批次。世界材质表独立保留；GPU MDI 运行时提交已移除，特殊材质保留原生绘制。

坐标直接沿用 Source 约定：XYZ 不变换，1 单位 = 1 英寸。实体角度按 `new Angles( pitch, yaw, roll )` 转换为旋转。

### 实体来源

零售版把大部分实体放在 BSP 之外的分区文件中：

| 分区文件 | 内容 |
|----------|------|
| `<map>_env.ent` | 环境实体 |
| `<map>_spawn.ent` | 出生点 |
| `<map>_fx.ent` | 粒子 |
| `<map>_script.ent` | 脚本实体 |
| `<map>_snd.ent` | 声音 |

BSP 自身的 ENTITIES lump 很小，但总是包含 `worldspawn`，以及分区文件中没有重复的体积实体（例如 `fog_volume`）。环境分区缺失时，退回到 ENTITIES lump。

### 就绪信号

`Titanfall2WorldReady`（[Resource/WorldReady.cs](Code/Resource/WorldReady.cs)）在 `worldspawn` 上维护标签 `ttf2_map_ready`：

- 标签在 `OnLoad` 和 `OnEnabled` 时清除。
- 发布条件：
  - 道具流式组件已完成常驻碰撞预加载。
  - 世界碰撞体在物理世界中有带形状的物理体。
  - 物理世界中道具碰撞体的数量等于预期数量。
  - 碰撞清单中每个内容分组都有物理体。
- 发布后每帧确认缓存的物理体仍然有效，条件失效时立即撤销标签。
- 遍历物理世界的检查最多每 0.25 秒执行一次，碰撞清单检查也在这个节流之后执行。
- 道具数据损坏时，常驻预加载不会完成，标签也不会发布。

## 5. rBSP 格式要点

### CM 碰撞

代码在 [Formats/Bsp/Titanfall2BspCollisionReader.cs](Code/Formats/Bsp/Titanfall2BspCollisionReader.cs)。

- 每个碰撞图元引用是 32 位：
  - 位 0–7：contents 表索引
  - 位 8–28：21 位图元索引
  - 位 29–31：类型（0 = brush，2 = tricoll，3 = 静态道具）
- 模型 0（世界）使用几何集合的前缀 `[0, model1.first_geo_set)`。模型 N 使用 `CM_GRID_CELLS[grid_w * grid_h + N]`。
- 去重以完整的原始引用为键。
- 损坏的图元会被拒绝并计入 `RejectedPrimitiveCount`。
- 边数超过 128 的 brush 视为损坏。
- tricoll 的三角形绕序会转换为 s&box 的约定。碰撞是单面的。
- 如果所有分组的 contents 都为 0，地图加载失败（`InvalidDataException`），不会生成一个没有固体的世界。

### contents 和检测掩码

`Titanfall2Contents` 和 `Titanfall2TraceMask`（[Resource/CollisionContents.cs](Code/Resource/CollisionContents.cs)）的值取自零售版 client.dll 的脚本常量。常用值：

| 名称 | 值 |
|------|----|
| `PlayerSolid` 掩码 | `0x0201400B` |
| `TitanSolid` 掩码 | `0x0220400B` |
| `Shot` 掩码 | `0x46404033` |
| `PlayerClip` | `0x10000` |
| `MonsterClip` | `0x20000` |
| `NoClimb` | `0x100000` |
| `TitanClip` | `0x200000` |
| `BulletClip` | `0x400000` |
| `NoGrapple` | `0x800` |

每个碰撞对象的 `ModelCollider` 在 contents 与 `PlayerSolid` 掩码没有交集时设为触发器。这样普通 s&box 移动只被玩家固体阻挡，其他掩码通过 `Titanfall2CollisionQueries.Run` 查询。

`Titanfall2CollisionQueries.Run` 的实现细节：

- `PhysicsTrace` 的"任一标签"查询最多使用 8 个标签，因此掩码的置位被拆成每组 8 个标签，每组单独检测，取最近的命中。
- 拆分结果按掩码缓存。
- 空掩码不命中任何导入的碰撞。
- `includeOtherObjects` 为真时，还会检测场景中的普通物体。

### 静态道具 contents

sprp v13 中与碰撞有关的字段：

| 偏移 | 字段 |
|------|------|
| `+0x1e` | solid 类型 |
| `+0x38` | Reserved |
| `+0x3c` | Remove 掩码 |

solid 类型不为 0 时，有效 contents 由 `Titanfall2CollisionSource.StaticPropContents` 按零售版 engine.dll 的初始化逻辑计算，起点是 MDL contents：

1. 为 0 或含 `Solid` 位时：清除位 0，OR `0x00EB0280`。
2. 含 `Window` 位时：清除 `0x00080002`，OR `0x00E30240`。
3. 含 `Grate` 位时：清除 `0x00480008`，OR `0x00A30240`。
4. 最后与 `~Remove` 相与。

结果为 0 的道具不创建碰撞。

可读标签由 contents 推导：

- `playerclip` 和 `titanclip` 只在对象不阻挡子弹时添加。固体道具展开后带有全部 clip 位，不应被当作 clip。
- `nograpple` 对应 `NoGrapple` 位。
- `nowallrun` 对应 `NoClimb` 位。这一对应是推断，没有对照零售版墙跑代码确认。

### 光照贴图和局部光源

- `WORLD_LIGHTS` 有 v1、v2、v3 三种记录，大小分别为 100、108、112 字节。强度是线性 HDR 值，可以直接用于着色，不需要按 LDR 限幅。
- 实时光照页表在 lump 122 中，每页 63 个 `short`。
- 光照贴图的 t35 通道每个像素存 4 个 6 位光源索引和一个 8 位页号，索引 63 表示没有光源。t36 存储可双线性过滤的遮挡值。
- 页表中的索引指向 `flags & 8` 的光源组成的紧凑列表，不是 `WORLD_LIGHTS` 的原始下标（[Formats/Bsp/Titanfall2WorldLight.cs](Code/Formats/Bsp/Titanfall2WorldLight.cs)）。
- 地形按像素选择太阳遮挡通道。静态道具从光照探针获取太阳可见性。
- 探针中的灯光编号不计入 `SkyAmbientType` 记录。BSP 解析时先把共享探针 lump 中的编号映射到完整 `WORLD_LIGHTS` 下标，再供静态道具和动态实体采样；这与实时光照贴图页表的紧凑列表是两套映射。无效编号保持为 `0xFFFF`，不猜测附近灯光。
- 每个着色点最多计算 4 盏局部光源。光源数据通过 `Titanfall2LocalLightController` 上传为 GPU 缓冲区，不创建引擎的 `PointLight`。

### 静态道具光照探针

- 系数是带符号的 Q13 定点数（乘以 1/8192），按静态道具索引查找。
- 每个探针上传为 80 字节的 GPU 记录：3 个 `float4` RGB 系数、天空方向和太阳可见性、标志位、4 个字节权重和 4 个 `ushort` 光源 ID。
- 着色公式是 `max( dot( float4( n, 1 ), c ), 0 )` 加天空项。
- 原生探针体积的面顺序是 +X、+Y、+Z、-X、-Y、-Z。

### 光照环境

- `light_environment_volume` 由局部包围盒和一组凸平面定义。
- 不在任何体积内的位置，优先使用没有绑定体积的 `light_environment`。
- `Titanfall2EnvironmentController` 跟随当前相机，通过场景 RenderAttributes 更新太阳和天空颜色。

### 雾

- 导入的高度雾常量通过场景渲染属性绑定，并设置 `ImportedHeightFogBound`。`Titanfall2HeightFog` 禁用时写入全零雾参数，标记仍表示缓冲区已绑定。引擎的 `SpriteBatchSceneObject` 在场景没有该标记时绑定 5 个全零 `float4`，确保菜单和普通场景的 sprite shader 也始终有可读的雾常量缓冲区。
- `fog_volume` 的 `*N` 模型由 CM 数据重建为带优先级的凸体并集（[Formats/Bsp/Titanfall2BspFogVolumes.cs](Code/Formats/Bsp/Titanfall2BspFogVolumes.cs)）。
- 默认雾控制器取第一个 `env_fog_controller`，存在 `master` 控制器时取最后一个 `master`。
- 雾体积按优先级从高到低检查，`fogenable=0` 的体积清除雾。
- 选择雾时使用 `Camera.View.Position`。
- 3D 天空盒在 `useworldfog` 为真时使用同一份雾参数。

### PVS

- rBSP 包含 CellBSP 树和由 portal 连接的可见性单元。挂载最多支持 128 个单元（用 128 位掩码表示）。
- 道具可见性从相机所在单元沿 portal 遍历，最多 16 跳。
- 没有有效单元掩码的道具始终可见；批次中任一成员可见时，整个批次可见。

## 6. 材质和着色器

着色器在 `Assets/shaders/` 下。

材质着色器按零售版着色器语义选择，不按材质名猜测，例如水面按语义选择，不看名字中是否有 "water"。

### 层混合

- 层混合（Lyr）权重：`saturate( (ramp + 1) * vertexAlpha + ramp * (mask - 1) )`。
- 半透明层（Lyrtrans）覆盖率：`1 - weight`，再乘以 albedo alpha 和 opacity。
- 层法线的斜率解码：`xy * 2.867661 - 1.439453`，Z 取 1。

### 纹理槽位

- UV 选择字符串按绑定纹理寄存器的顺序对应。
- `Emul` 紧跟在 `Emission` 之后。
- `EmissiveMultiply` 和 `Cavity` 是两个独立语义。`Cavity` 只乘在镜面项上，中性值为 1。
- RPAK 的 RGB 镜面贴图直接作为 F0 使用。

### 渲染通道和 VMT 代理

- 深度/法线预通道在计算颜色和雾之前返回 `DepthNormals::Output`。
- VMT `TextureScroll` 代理应用 `texturescale`，并用 `(scale - 1) / 2` 的偏移补偿缩放中心。
- `Sine` 代理使用 `time - timeoffset`，只把周期 0 改成 1。

### UV 失真（草的摆动）

零售版的草（`models/foliage/grass_*_animated`）不移动顶点，顶点程序只做普通变换。摆动来自像素程序中的 `Uvd1` UV 失真：

- `uvDistortionTexture` 按它自己的 UV 选择符采样。草使用 UV2，`Uv2at` 让它按 `c_uv2Translate * c_gameTime` 滚动。
- 偏移量：`uv = uv1 + (rg * 2 - 1) * c_uvDistortionIntensity * (c_uv1RotScaleX.x, c_uv1RotScaleY.y)`，所有贴图都用这个 UV 采样。
- 草的材质参数：UV2 缩放 0.5，平移 (-0.25, 0) 或 (-0.3, 0)，强度 (0.2, 0)。

`Titanfall2MaterialDescriptor.GetDistortionUvChannel` 从着色器集名称解析失真贴图的选择符，写入 `g_flT2DistortionUvChannel`。目前只有 `titanfall2_cutout` 读取它，选择符无法解析时仍在 UV1 上采样失真贴图。

### 大气卡片

大气卡片材质的淡出：

- 距离淡出：`saturate( d * scale + bias )`，`scale` 和 `bias` 位于材质常量的 `0x90` 和 `0x94`。
- 视角淡出：`lerp( inner, outer, pow( 1 - |n·v|, exp ) )`。
- 背面剔除跟随材质的 `DoubleSided`。

### 远景（vista）

- 远景着色器按零售版 `c_maxLightingValue` 的方式限制峰值亮度。这个常量是引擎的相机参数，不存储在资源中，挂载默认使用 1。
- HDR 行星和太阳贴图另有固定的强度和峰值限制。

### 表面和贴花

- `Titanfall2Surface`（[Resource/Surface.cs](Code/Resource/Surface.cs)）把 surfaceprop 名归一化（去路径/扩展名/引号）、查别名表，再 `Surface.FindByName`；找不到回退 `default`，每个未知名字只提示一次。
- 数据来源：MDL 模型级 surfaceprop 传给 `ModelBuilder.AddBody` 的物理体，骨骼级传给 hitbox 的 `.WithSurface`；legacy VMT 的 `$surfaceprop` 写成材质的 `PhysicsSurfaceProperties` 属性。rBSP 的 CM 数据与 RPAK MATL 头不提供可解析的表面名，保持默认。
- `Titanfall2DecalFactory`（[Resource/Decals.cs](Code/Resource/Decals.cs)）从贴花材质属性（`g_tAlbedo`/`g_tNormal`/`g_tEmissive`）生成原生 `DecalDefinition`；原生贴花只吃贴图，层混合/LUT/独立不透明度贴图不还原，遮罩取颜色 alpha。

## 7. 显示管线

`Titanfall2Tonemapping`（[Resource/Tonemapping.cs](Code/Resource/Tonemapping.cs)）挂在一个无限范围、优先级 -100 的 `PostProcessVolume` 上，所以游戏自己的后处理体积可以覆盖它。函数定义在 `titanfall2_display.hlsl`。

### 曝光

1. 对颜色缓冲区做 7 级对数亮度降采样。
2. 由平均 log2 亮度计算目标曝光：`L = exp2( meanLog )`，`target = (1.03 - 2 / (ln( L + 1.0002 ) + 2)) / L * 2^EV`。`EV` 是 `SBOX_TITANFALL2_EXPOSURE_EV`，默认 0。
3. 每帧按相对差值向目标逼近，速率 0.1（零售版 `mat_autoexposure_speed`）。
4. 结果限制在 0.5–2。
5. 每个相机有独立的曝光历史。

挂载在渲染时关闭相机自带的自动曝光，并记录原来的设置，组件移除时恢复。

### 显示曲线

零售版 `engine_post` 的默认分支：`x * (10x + 0.3) / (x * (10x + 0.5) + 1.5)`，输入和输出都是线性值。

### 泛光

1. 预过滤：在 1/4 分辨率上计算 `pow( max( rgb - cutoff, 0 ), exponent )`。
2. 模糊：水平和垂直两次，每次 13 个双线性采样点，覆盖 25 像素，中心权重 0.122765。
3. 合成：乘以 `0.5 * BloomStrength` 加到场景颜色上，然后应用曝光和显示曲线。

默认强度 0.15、阈值 1、指数 1，这些是挂载的校准值，不是零售版数值。零售版的 wide 和 streak 分支没有实现。

### 颜色校正

- `color_correction` 实体指向一个 32³ RGB 查找表文件（98,304 字节），例如 `materials/correction/sp_beacon.raw`。
- 只加载全局启用的实体，即没有 `StartDisabled`，且没有 `minfalloff`/`maxfalloff`。
- 权重取 `maxweight`。
- 查找表本身按自己的内容路径注册成挂载体纹理（`mount://titanfall2/materials/correction/<名字>.raw.vtex`），
  场景里的 `Titanfall2ColorCorrection` 只保存这个资源路径。表不进场景数据，因此每张图的场景不再多出 98,304
  字节的样本；纹理属于挂载而不属于组件，缺挂载时该图只是不做校正。保留 32³ 精度，不重采样成引擎原生的 16³ grading LUT。

## 8. 模型

- 每个 body part 默认选择模型 0。`Mdl53BodyPart.Base` 是打包乘数，不是默认索引。
- VTX strip 的 `IndexOffset` 是组内索引数组的元素下标，不是字节偏移。
- VVD 修正表的区间越界时报错。内嵌 VTX 的解析错误向上传递，不静默生成空模型。
- 碰撞来源的优先级：VPHY 凸包，然后是 hitbox，最后是渲染网格。超过 8192 个三角形的渲染网格用一个包围凸包代替。
- 碰撞来源写入模型数据 `titanfall2_collision`（`Titanfall2ModelCollisionData`），诊断命令从这里读取。
- 每个 hitbox 带标签 `titanfall2_hitgroup_<n>`。critical override 不为 0 的 hitbox 还带 `titanfall2_critical_override`。
- 每个 MDL 还注册一个静态实例模型（`.t2static`），它在一个模型中包含全部 LOD，供实例批次按屏幕尺寸切换。
- 动画来自模型自身和它 include 的模型，按骨骼名称映射到目标骨架。每个序列只取第一个动画，并且总是先添加 `bindpose`。
- 模型缓存键包含挂载程序集的模块 ID（`ContentFingerprint`），重新编译挂载会使旧缓存失效。

## 9. 静态道具批次

- 同一模型、同一批次簇（4×4 个 512 单位单元）的道具组成一个实例批次。
- 索引缓冲区：
  - 恒等索引缓冲区只上传一次，按前缀复用。
  - LOD 重排和可见子集使用动态缓冲区。
  - 批次销毁时释放它的 3 个 GPU 缓冲区。
- 带光照探针的批次（`Titanfall2ProbeInstanceBatch`）在深度预通道中跳过探针绑定（`SkipDepthProbeBindings`），前向和阴影通道绑定全部数据。
- 模型太阳直射光在实时级联阴影覆盖范围内使用表面逐像素遮挡，不再重复乘上单个烘焙探针的太阳可见度。
  各级联球的边缘平滑回退到烘焙可见度；未覆盖或未启用实时阴影时仍使用探针。天空光方向、间接光和局部灯光权重保持各自含义。
- 场景中序列化的道具数据带魔数和版本号，读取时检查版本范围，并按实际字节数限制解码出的数量。

## 10. 粒子

- PCF 库在加载画面之后分帧解析。地图粒子实例在相机附近分帧激活。
- 名称包含 `sun`、`star`、`sky` 或 `weather` 的效果视为全局效果，不受激活半径限制。
- `orientation_type` 1 表示竖直公告板，2 表示世界 XY 平面。
- 支持以下渲染器和功能：
  - 绳索渲染器（`Titanfall2ParticleRopeRenderer`）
  - 速度拖尾
  - 带 `$ramptexture` 的 SpriteCard（`Titanfall2ParticleRampRenderer`）
- 控制点编号范围是 0–63，来自实体键 `cpoint1`..`cpoint63`。游戏代码可以通过 `Titanfall2ParticleControlPoints.SetControlPoint` 更新控制点。
- 每个系统遵守 PCF 中的最大绘制距离和"不可见时休眠"时间。唤醒的循环效果会预热，避免重新出现时是空的。

## 11. 音频

- `general.mbnk` 中的 BCF 声音由 [Formats/Miles/](Code/Formats/Miles) 的纯 C# Bink Audio 2 解码器解码：BCF 头（24 字节 + packet 尺寸表）→ 每个 packet 的位流、分带量化系数、逆 DCT 与 overlap-add → int16 PCM。结果按 `SoundFile.FromPcm` 交给引擎，没有原生桥或随包 DLL。
- 解码保真度：与零售 Miles 运行时对比，94.07% 的样本逐位相同，100% 在 ±2 LSB 以内（最大 2 LSB，平均 0.059 LSB）。这点差异来自零售 `binkawin64.dll` 自身的浮点与取整管线，在不复制 RAD 实现的前提下不可消除；用 FFmpeg 作为第三个 oracle 交叉验证，本实现比 FFmpeg 更接近零售。听感与零售一致。
- 声道：单/双声道逐样本对比；4/6 声道流先按原始声道解码，再在解码过程中折叠为双声道（Miles 近似：中心 0.7071、环绕 0.5、省略 LFE；4 声道另做 1/√2 等功率归一化），始终向 `SoundFile.FromPcm` 交付与旧包相同的 2 声道 PCM。4/6 声道电平与零售一致（RMS 比 0.999–1.000），波形相关系数中位数 1.0；3/5/7 声道布局返回失败并给出消息。
- 声音事件（Miles 事件解释器）依赖零售 Miles 运行时，属于原生代码，已随原生桥移除：`Titanfall2SoundEventPlayer`（组件名 `titanfall2_sound_event`）保留为兼容占位、不再播放；`CreateSoundEventSession` / `Titanfall2SoundEventSession` / `Titanfall2SoundEventHandle` 不再提供。`sound/<name>.vsnd` 样本、点声源和 soundscape 仍正常工作。
- 地图音频（[Resource/MapAudio.cs](Code/Resource/MapAudio.cs)）来自 `_snd.ent`：
  - `ambient_generic` 生成点声源。零售地图的 `radius` 都是 0，因此使用默认半径 1024。
  - `trigger_soundscape` 生成球形 `SoundscapeTrigger`。
  - 播放依赖场景中的 `Sound.Listener`。

## 12. 生命周期和所有权

- `_cachedWorldBuild` 与官方 loader 注册的 SceneFile 同寿命。临时构建场景销毁时不释放它；`OnSceneUnloaded()` 回调才清理生成模型、光照纹理和关联状态。
- 构建失败会回滚：`WorldBuildResult : IDisposable`，异常调用 `DiscardWorldBuild`，后续请求重新构建。
- `.bsp.scene` 和 `.bsp.world.scene` 的并发构建通过 `Titanfall2MapImport` 租约共享 BSP 解析，最后的解析租约释放后可清除 CPU 数据。失败条目不复用。
- 每个场景实例独立拥有局部光缓冲、原生道具对象和碰撞调度，销毁时只释放本实例对象。普通 loader 缓存仍由官方引擎管理，不反射清空，不强制 GC。
- 场景 lease 阻止在其他实例使用时清理共享 CPU 缓存。编辑器克隆和临时构建场景同样遵守资源所有权；没有以前的延迟重试/GC 回收循环。
- 共享粒子纹理在挂载关闭时解除引用；仍在运行的实例保留自己的引用。导航通过按 Scene 共享的状态恢复宿主设置。
- `Titanfall2Map` 只销毁自己创建的根对象和相机；加载时深拷贝 JSON 并重映射 ID，部分失败清理已创建的根对象。
- 专用服务器使用 CM 碰撞，跳过渲染、环境、粒子与地图音频。

## 13. 流式加载预算

这些数值是 [Resource/StaticPropStreamer.cs](Code/Resource/StaticPropStreamer.cs) 中的常量：

| 阶段 | 预算 |
|------|------|
| 加载画面预加载 | 每次让出最多 512 个道具、35 ms，最多 4 个碰撞体 |
| 加载后补全 | 每帧最多 16 个道具、3 ms |
| 运行时可见性 | 每帧最多 16 个单元、1.5 ms |
| 碰撞流式 | 每帧最多创建或释放 2 个碰撞体、1 ms；1536 单位内启用，2560 单位外释放 |
| 距离保留带 | 道具进入绘制距离后，再多保留 512 单位 |
| 大型道具 | 半径不小于 256 的道具不做距离剔除 |
| 阴影 | 道具阴影资格沿用半径 48 的门槛及原批次聚合。`PROP_SHADOW_DISTANCE` 默认 0（距离门控关闭）；开启后 CPU 按批次门控、GPU 按实例门控，恢复阈值约为关闭距离的一半并量化到 256 单位桶 |

普通地图默认在加载期准备并常驻静态道具碰撞（`RESIDENT_WORLD=1`）；设 0 才采用上表的距离流式碰撞。纯世界和专用服务器始终常驻。已稳定的常驻场景不因相机移动重建碰撞扫描队列；拥有的碰撞体丢失时触发恢复。缺失碰撞资源仍保留失败状态，不能发布就绪标签。

地图粒子目录索引和系统创建也在 `OnLoad` 的有界循环中完成，运行时保留激活/休眠及模拟。`PreparationComplete` 表示道具准备流程结束，`ResourcesPrepared` 表示渲染资源没有加载失败，`ResidentCollisionsReady` 则要求实际常驻碰撞完整；三者不互相替代。粒子组件独立报告 pending/failed 数量。

### 帧率预算

下方数字来自之前的 CPU 批次路径测量，仅作历史资料；本次官方沙箱适配没有新的 FPS 或 GPU 时间结论。

每帧的成本主要不是这些预算，而是**一次性创建、之后每帧被渲染线程提交的对象数**：`mp_angel_city` 实测 2,840 个渲染对象、3,044 draw、3.2M 三角形，其中 2,717 个是静态道具实例批次。C# 每帧回调（`Titanfall2StaticPropStreamer` 的三条流式通道合计 5.5 ms 预算）只占 `Update` 的 1.5 ms 量级。

因此调帧率改的是"提交多少"：`SBOX_TITANFALL2_FPS_PRESET` 一档设好下面几项，单项变量显式设置时优先于预设。

| 预设 | 说明 |
|------|------|
| `0` / `60` | 默认档：阴影距离 0（门控关闭 = 整批投影，与旧行为一致）、LOD 缩放 1、cell 2048、批次簇 4 |
| `120` | 阴影距离 1024、LOD 缩放 0.5、cell 4096、批次簇 8、绘制距离 6144，并关掉接触阴影与 Godray |

单项旋钮：`PROP_SHADOW_DISTANCE`（道具批次投影距离，0 = 关闭门控）、`PROP_LOD_SCALE`（乘到 1536/4096/8192 三个 LOD 切换距离）、`WORLD_CELL_SIZE`（世界几何 cell 边长，越大 fragment 越少、剔除越粗）、`BATCH_CLUSTER`（多少个 prop cell 合成一个实例批次）。

阴影门控不在可见性 sweep 里，而是独立 pass：只在相机移动 512 单位或换 PVS cell 时重算，重算只把差异排队，`ApplyPendingShadowChanges` 每帧最多写 32 次 `Flags.CastShadows`。一次 sweep 内批量翻转一百多个批次的标志会让整片实例在 shadow pass 进出，走动时表现为一顿一顿；这个上限是为此加的。阈值按 256 单位粗桶比较，关闭距离 D 与恢复距离 D×0.5 之间留足回滞。`Flags.CastShadows` 的切换走引擎原生的 `ChangeFlags` + `NotifyChanged(Flags)`，与 `LineRenderer`、`MeshComponent` 的运行时切换同一条路径。

LOD 判定使用 32 单位量化的相机位置：探针批次的逐实例 LOD 分组一变就要重传索引缓冲，量化把走动时的重排从每帧降到每跨一格。

### 官方沙箱的静态道具后端

静态道具使用上述原生实例批次。GPU MDI 的内部命令记录/播放兼容层已经移除，设置不能重新启用它。残留的几何布局、预算计算和 shader 为离线资料，不创建运行时 GPU scene。特殊材质不为合批而丢弃。

LOD、PVS、距离和阴影处理回到既有原生批次路径；不能沿用旧 MDI 的 CPU/GPU 性能结论。比较需要同进程、固定机位与分辨率、窗口聚焦，并记录帧时间及 GPU 时间。

## 14. 处理缓存

- 缓存文件格式：
  - 文件头：魔数 `T2FC`、版本号、32 字节 SHA-256 键。
  - 正文：GZip 压缩的数据。
- 文件先写临时路径，再通过 `FileSystem.Data` 发布；同进程读写互斥。公开文件系统没有原子 rename，失败或中断后通过读取校验回退。
- 读取时校验文件头，并读到 GZip 结尾以检查 CRC 和截断。任何异常都按未命中处理。
- 缓存种类：`model`、`texture`、`material`、`propfade`。地图几何和光照贴图不缓存。
- 缓存键包含两部分：
  - `ContentFingerprint`：挂载程序集的模块 ID，加上游戏文件的大小和修改时间。
  - 资源自身的来源标识。
- 命中时更新文件修改时间，每个文件每次会话只更新一次，供 LRU 淘汰使用。
- 淘汰在每 64 次写入时检查一次，删除最旧的文件，直到总大小不超过 `SBOX_TITANFALL2_CACHE_MB`。
- 缓存只保存派生数据，从不保存引擎或 GPU 对象。

## 15. 官方资源生命周期

地图 loader 继承官方 `SceneLoader<Titanfall2Mount>`，仅在 `OnSceneUnloaded()` 丢弃 WorldBuild。缓存的 SceneFile 和生成模型保持同寿命，临时构建 scene 销毁不会提前释放它们。

场景实例的局部光 GPU buffer、原生渲染对象和碰撞由各自 owner 在销毁时释放。普通 ResourceLoader 缓存与跨地图引用由官方引擎控制；不使用私有字段反射、强制 GC 或延迟重复 GC。现有 per-scene lease 仅用于避免在还有用户时清理解析缓存。

后台解析通过 `GameTask.RunInThreadAsync` 调度，以单独完成信号供同步导入等待，避免依赖主线程 continuation 而死锁；并行循环使用公开 `Sandbox.Utility.Parallel`。MDL 固定缓冲改为紧凑值字段，通过公开 ByteStream 读取，禁用 unsafe。

配置诊断记录转换格式 ID、包标识与 NativeInstanceBatch 后端；它不是实际 DLL 的 MVID。构建产物身份应在工具端用 SHA-256 验证。

## 17. 玩家画质与挂载配置边界（2026-11-09）

- `Titanfall2GraphicsSettings` 负责把已保存的玩家纹理质量转为不可变导入策略；挂载时读取一次，监听 `RenderSettings.OnVideoSettingsChanged`，卸载时取消订阅。后台解码只读原子发布的快照，不访问视频设置 Cookie。
- 独立运行使用公开 `Application.RenderSettings`；菜单/编辑器的该入口为 null，仅在挂载连接阶段集中访问既有托管 singleton。取不到时回退 1K，不引入对菜单/编辑器程序集的依赖，不改引擎源码。
- RPAK 缓存键和 mip 选择使用同一个局部分辨率快照，避免保存设置与 worker 解码交错时污染磁盘缓存。VTF 2D/cubemap 同样在单次解码入口固定策略。
- 方向光默认组件上限恢复为原生支持的 4 级，由引擎质量配置裁剪；不复制原生阴影、后处理、抗锯齿或超分控制器。显式兼容覆盖与旧性能预设保留。
- 删除无人调用的 `ITitanfall2TextureSource` / `TryCreateTexture` / RPAK 直接创建包装，正式链路统一为 source → CPU decode/cache → 主线程 upload。
- 质量改变不会销毁可见场景资源或强制切图。现有纹理在正常离图释放后重建；编辑器缓存仍遵守原有资源所有权规则。TF2 自定义水面、高度雾和粒子需要独立的效果质量设计，不能仅按菜单名称绑定。

玩家入口、覆盖优先级与生效时机见 [ENVIRONMENT_SWITCHES.md](ENVIRONMENT_SWITCHES.md)。

## 18. 高画质下的预取背压（2026-11-09）

`TexturePreparationBudget` 统一管理预取数据的保留量与实际峰值。材质描述缓存路径和原始 RPAK 绑定路径都遵守回调返回的停止信号。跨预算 payload 保留供主线程直接消费，并在上传或取消时返还其完整记账；共享贴图不重复读取、记账或释放。预算判断不等待主线程，避免主线程等待材质 worker 时形成循环等待。这个预算只覆盖 CPU 预取数据，不代表 GPU 显存预算，也不降低玩家选择的纹理分辨率。

## 19. 局部调色与 Bloom 分支（2026-11-09）