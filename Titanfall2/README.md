# Titanfall 2 挂载

`Titanfall2Mount`（[GameMount.cs](Code/GameMount.cs)）以 `titanfall2` 为标识，把本机 Steam 安装的 Titanfall 2（AppId 1237970）中的资源注册成 s&box 资源。安装目录里同时存在 `r2/paks/Win64` 和 `vpk` 时才视为已安装。所有资源都在加载时从原始游戏文件转换，不需要预先解包。

实现细节、数据格式和生命周期规则见 [ARCHITECTURE.md](ARCHITECTURE.md)。配置项见 [ENVIRONMENT_SWITCHES.md](ENVIRONMENT_SWITCHES.md)。

阅读顺序：本页介绍挂载能力、使用方法和已知限制；技术参考解释解析、渲染与资源所有权；配置表用于调整实际运行设置。测试入口和验收边界见本页的“构建和测试”。

## 挂载的内容

注册顺序决定同名资源的优先级：先注册 RPAK，VPK 只补充 RPAK 中没有的名字。

| 来源 | 注册为 | 说明 |
|------|--------|------|
| `r2/paks/Win64/*.rpak` 和 StarPak | 纹理（`<name>.vtex`）、材质 | RPAK 的 TXTR/MATL 资源，高分辨率 mip 从 StarPak 读取。补丁包优先，然后是 `common.rpak`。 |
| `vpk/*_dir.vpk` | VMT 材质、VTF 纹理、模型、WAV 声音、场景 | `englishclient_` 目录映射到 `client_` 数据包，`depot/` 下的镜像条目被隐藏。 |
| MDL v53（含内嵌 VTX/VVD/VPHY） | `models/<x>.mdl.vmdl` | 默认 bodygroup 取每个 body part 的模型 0。带有附着点、hitbox set、皮肤（`Skin N`），以及模型本身和 include 模型中的动画序列。另注册一个包含全部 LOD 的静态实例模型，供静态道具批次使用。 |
| `r2/sound/general.mbnk` | `sound/<name>.vsnd` | Miles 银行中的 BCF 声音，由挂载内置的纯 C# Bink Audio 2 解码器解码为 PCM。 |
| PCF | 地图粒子系统 | 由地图实体创建，不单独注册为资源。 |
| rBSP 地图 | `maps/<map>.bsp.scene`、`maps/<map>.bsp.world.scene` | 见下一节。 |
| rBSP 内联模型 | `maps/<map>.bsp/brush/<index>.vmdl` | 门、移动平台等 `*N` 模型。 |

`skies/skycube.vmat` 是挂载生成的天空材质，它把零售版的天空立方体贴图包装成原生天空着色器。

## 地图加载

每张地图注册两个场景，都由 [Resource/Map.cs](Code/Resource/Map.cs) 中的 `MapLoader` 构建：

- `mount://titanfall2/maps/<map>.bsp.scene` 是完整场景。它包含世界几何、碰撞、环境、静态道具、粒子、地图音频、出生点、动态道具和一个旁观相机，可以直接打开运行。
- `mount://titanfall2/maps/<map>.bsp.world.scene` 是纯世界场景。它不创建出生点、相机和动态道具，不强制开启导航，并把所有静态道具碰撞常驻在物理世界中。它适合加载进游戏自己的场景。

在自己的场景里使用地图时，添加 `Titanfall2Map` 组件（[Resource/Titanfall2Map.cs](Code/Resource/Titanfall2Map.cs)），用法与 Hammer 地图的 `MapInstance` 相同：

- `MapName` 是地图名，例如 `mp_angel_city`。末尾的 `.bsp`、`.bsp.scene` 会被去掉。
- `PureWorld` 选择加载 `.world.scene`。
- `SpawnSpectatorCamera` 在场景没有主相机时，在第一个出生点上方创建旁观相机。纯世界模式不创建相机。
- `LoadInEditor` 让编辑器视图也加载地图。默认关闭，因为大地图要预加载数万个静态道具。

地图对象作为不保存的子对象加入场景，组件只销毁它自己创建的对象。`Titanfall2Map.GetAvailableMaps()` 返回当前挂载中所有可用的地图名。

纯世界场景加载完成后，`worldspawn` 对象上的 `Titanfall2WorldReady` 组件会在以下条件都满足时添加标签 `ttf2_map_ready`：世界碰撞体已进入物理世界，所有内容分组都有物理体，常驻道具碰撞全部创建完成。任一条件失效时标签会被移除。游戏代码应在看到这个标签后再放置玩家。

## 渲染

以下功能在默认设置（`SBOX_TITANFALL2_BSP_LIGHTMAPS=1`）下启用。

- **光照贴图和探针**：世界几何使用 BSP 中烘焙的 HDR 光照贴图。静态道具使用按道具索引存储的光照探针。
- **局部光源**：`WORLD_LIGHTS` 中的点光源和聚光灯通过实时光照页表在着色器中计算，每个像素最多 4 盏，不为每盏灯创建引擎光源。
- **太阳和天空**：方向光来自 `light_environment`。地图有多个 `light_environment_volume` 时，`Titanfall2EnvironmentController` 按相机位置切换太阳和天空颜色。
- **雾**：`env_fog_controller` 和 `fog_volume` 生成高度雾和方向雾，雾体积按相机位置选择。
- **3D 天空盒**：远景（vista）模型放进独立的天空盒世界，从地图的 `sky_camera` 观察。
- **水面**：按材质着色器语义识别，使用帧缓冲拷贝渲染。
- **贴花**：BSP 贴花几何单独渲染，不投射阴影。
- **显示变换**：`Titanfall2Tonemapping` 实现零售版的自动曝光和显示曲线，替代引擎的自动曝光。地图有全局 `color_correction` 实体时，加载它的 32³ RGB 查找表；该表是一个挂载体纹理资源，场景只引用它的路径。
- **泛光**：在曝光和显示曲线之前合成。

世界和静态道具默认开启方向光阴影，屏幕空间接触阴影也默认开启。半径小于 48 的静态道具不投射阴影。`SBOX_TITANFALL2_MAP_SHADOWS=0` 和 `SBOX_TITANFALL2_PROP_SHADOWS=0` 可分别关闭世界和道具阴影。Godray 光束面片默认显示，可用 `SBOX_TITANFALL2_GODRAYS=0` 关闭；它们随相机距离淡入，移动时可能出现闪烁。

## 静态道具

`Titanfall2StaticPropStreamer`（[Resource/StaticPropStreamer.cs](Code/Resource/StaticPropStreamer.cs)）在加载画面期间预加载地图中的全部静态道具，之后它们一直保留到场景销毁。

- **实例化**：相同模型的道具按 4×4 个 512 单位的单元分组，合并成一个实例批次绘制。
- **LOD**：静态实例模型包含 MDL/VTX 自带的全部 LOD。实例批次按每个实例到相机的距离选级（1536 / 4096 / 8192 单位切到 LOD1 / 2 / 3），按级分组绘制。
- **PVS**：沿 rBSP 的 cell/portal 图从相机所在单元向外遍历（最多 16 跳），隐藏不可见单元中的道具。没有有效单元的道具始终可见。
- **淡出距离**：每个道具使用 sprp 或 MDL 中设定的淡出距离，可用 `SBOX_TITANFALL2_PROP_AUTHORED_FADE_SCALE` 整体缩放。
- **全局距离**：超过 `SBOX_TITANFALL2_PROP_RENDER_DISTANCE`（默认 8192）的道具不再绘制。半径不小于 256 的大型道具不受这一限制。

## 碰撞

世界碰撞以 rBSP 的 CM 数据（brush 和 tricoll）为准：

- 碰撞几何按原始 contents 值分组，每组生成一个静态 `ModelCollider`。
- 不阻挡玩家（不含 `PLAYERSOLID` 位）的分组设为触发器，例如子弹阻挡和泰坦阻挡。
- CM 数据缺失或有被拒绝的图元时，客户端退回到经过过滤的渲染几何碰撞，并输出警告。专用服务器要求完整的 CM 碰撞，否则加载失败。

静态道具碰撞来自模型的 VPHY 凸包。没有 VPHY 时使用 hitbox；两者都没有时使用渲染网格，超过 8192 个三角形的网格用一个包围凸包代替。道具的有效 contents 由 sprp 的 solid 字段、MDL contents 和 remove 掩码计算，结果为 0 的道具没有碰撞。完整场景中，道具碰撞只在玩家附近启用：1536 单位内创建，超过 2560 单位释放。纯世界场景和专用服务器保留全部道具碰撞。

导入的碰撞对象带有以下标签：

- `ttf2_contents`
- 每个置位的 contents 位对应一个 `ttf2_content_<bit>`
- 可读标签 `playerclip`、`titanclip`、`nograpple`、`nowallrun`

用原始掩码做射线检测时，调用 `Titanfall2CollisionQueries.Run( trace, Titanfall2TraceMask.Shot )`（[Resource/CollisionQueries.cs](Code/Resource/CollisionQueries.cs)）。`Titanfall2Contents` 和 `Titanfall2TraceMask` 枚举（[Resource/CollisionContents.cs](Code/Resource/CollisionContents.cs)）使用零售版脚本中的常量值。

控制台命令 `titanfall2_collision_look [距离=512] [半径=0]` 从当前相机发出射线或球体扫描，以 JSON 输出命中物体的碰撞来源、contents、形状和标签。它只读取，不修改碰撞。

### 表面语义

挂载会把能读到的 surfaceprop 映射到引擎的 `Surface`（[Resource/Surface.cs](Code/Resource/Surface.cs)），用于物理摩擦/弹性/密度和脚步声：

- **MDL**：模型级 surfaceprop 用于物理体，骨骼级 surfaceprop 用于 hitbox。这是静态道具和大多数模型碰撞的表面来源。
- **VMT**：`$surfaceprop` 写入渲染材质的 `PhysicsSurfaceProperties` 属性。
- **rBSP 世界和 RPAK MATL**：CM 碰撞数据没有材质索引，零售 MATL 的表面名在资产头的字符串指针里、尚未解析，因此世界碰撞和 RPAK 材质保持引擎默认表面。
- 引擎没有的表面名按 [Resource/Surface.cs](Code/Resource/Surface.cs) 的别名表映射（例如 `concrete`→`dirt`、`tile`→`ceramic`）；未知名字回退到 `default`，每个名字只提示一次。

## 运行时贴花

`Titanfall2DecalFactory`（[Resource/Decals.cs](Code/Resource/Decals.cs)）把挂载的泰坦陨落 2 贴花材质解包成原生 `Decal` 需要的贴图，供游戏代码做弹孔、焦痕等动态效果：

- `Titanfall2DecalFactory.TryCreateDefinition( materialName, out definition, out error )` 接受材质名（可带 `materials/` 前缀或 `.vmt`），返回 `DecalDefinition`。
- `Titanfall2DecalFactory.Spawn( parent, materialName, position, rotation, size, depth )` 直接创建贴花对象。
- `Titanfall2DecalFactory.RotationForNormal( normal )` 生成贴花朝向。
- 调试命令：`titanfall2_decal <材质名> [大小=32] [深度=8]`，在相机准星命中处生成贴花。

原生 `Decal` 只吃颜色/法线/自发光贴图，因此泰坦陨落 2 材质的层混合、颜色 LUT 和独立不透明度贴图不会还原；遮挡遮罩取颜色贴图的 alpha。静态地图贴花仍走上面的 BSP 几何路径。


## 导航

`Titanfall2DeferredNavMeshBuilder`（[Resource/DeferredNavMeshBuilder.cs](Code/Resource/DeferredNavMeshBuilder.cs)）接管挂载地图的导航网格生成。

默认的按需模式下：

- 挂载跳过加载时的整图生成。
- 只使用带 `titanfall2_navmesh` 标签的世界碰撞作为输入。
- 玩家、NavMesh 代理和代理目标周围 `SBOX_TITANFALL2_NAVMESH_RADIUS`（默认 2048）单位内的 tile 在需要时才生成。

宿主场景没有开启导航时，挂载不会修改它。`SBOX_TITANFALL2_DEFERRED_NAVMESH=1` 让完整场景主动开启导航。`SBOX_TITANFALL2_NAVMESH_STATIC_PROPS=1` 把静态道具碰撞也加入导航输入，同时让道具碰撞常驻。同一场景中的多张挂载地图共享导航状态，最后一张地图移除时恢复宿主的设置。

## 模型导出

`titanfall2_export_model <mount模型路径> [数据目录内相对输出路径]` 生成 DMX/VMDL，默认写入 `FileSystem.Data/exports/titanfall2/Assets/`。可将输出手动复制到目标 addon's Assets 继续编辑；沙箱挂载不能直接写入任意项目目录。`TryExportModelToPath` 的参数也必须属于可写的数据文件系统。

## 处理缓存

派生数据写入 `FileSystem.Data/cache/titanfall2/processed`，预算由配置项 `SBOX_TITANFALL2_CACHE_MB` 控制。缓存包括地图解析数据、模型、贴图、材质描述和道具淡出距离；原生 Model、Texture、SceneObject 不序列化到缓存。

缓存键使用转换格式 ID 和内容快照。loose 覆盖采用完整 SHA-256；大型容器采用长度和头尾各最多 4 KiB 的采样，因为官方只读文件系统没有公开 mtime。它不能检测所有同长度的内部修改；手动替换包后应重启并清除派生缓存。修改转换规则时须更新 `Titanfall2BuildIdentity.VersionId`。

写入通过官方数据文件系统发布，同进程读写互斥，读取校验格式、GZip CRC 和结尾。公开 API 没有原子重命名；中断或失败的文件按未命中重新生成。最近使用顺序仅在当前进程中记录。

## 源码结构

| 目录 | 内容 |
|------|------|
| [Formats/](Code/Formats) | 纯解析代码，不依赖引擎资源：`Bsp`、`Mdl`、`Miles`、`Pcf`、`Rpak`、`Scripts`、`Vmt`、`Vpk`、`Vtf`。VPK 的 LZHAM 压缩块和 Miles 的 Bink Audio 流都由这里的纯 C# 解码器处理。 |
| [Resource/](Code/Resource) | 资源加载器和场景组件：地图、模型、材质、纹理、声音、粒子、静态道具、碰撞、导航、环境和后处理。 |
| [Build/](Build) 与 [Tests/](Tests) | 独立构建脚本（只引用官方程序集）与托管回归测试。 |
| [Titanfall2StreamingSettings.cs](Code/Titanfall2StreamingSettings.cs) | 从 `FileSystem.Data/titanfall2.settings.json` 读取配置。 |

着色器在 `Assets/shaders/` 下，文件名以 `titanfall2_` 开头。

## 构建和测试

`.sbproj` 与官方 SourceEngine 挂载相同，只有标题和标识不同。项目按正常 addon 编译，启用沙箱允许列表，禁用 unsafe。LZHAM 与 Bink Audio 均保留纯 C# 实现，无 C++、P/Invoke 或附带原生桥接 DLL。

需要包含新挂载接入方式的官方引擎源码与匹配的已构建运行库。本次适配基于官方提交 `07a81a85b1486a7c4ee68446d730f56ee478fce8`，其中 `742b5a5095f5af945055c486ecc772352f941e73` 引入沙箱 addon 挂载。

从仓库根目录构建单个挂载：

```powershell
.\BuildTitanfall2.bat -EngineSource "D:\sbox\sbox-official-mounts" -Test
```

该入口调用官方 `SboxBuild build-mount`，依次执行正常项目编译、着色器编译和内容构建。默认输出为 `artifacts/titanfall2-official/Titanfall2/`，含 `package.local.titanfall2.dll` 与编译后的 `Assets/`。`-OutputDirectory` 指定输出父目录；重复构建会重建其中的 `Titanfall2` 子目录。`-CompileShaders` 为旧调用保留，着色器现在始终由官方流程构建。

构建全部挂载可运行 `BuildMounts.bat --engine-source "D:\sbox\sbox-official-mounts"`；官方默认输出是该引擎的 `game/mounts/`。`BuildMounts.bat --list` 只列出项目。

`Build/Titanfall2.csproj` 用于辅助 C# 检查和托管测试，它的普通 dotnet 输出不能作为发布包。直接运行回归：

```powershell
dotnet test Titanfall2/Tests/Titanfall2.Tests.csproj -c Release -p:SboxEngineRoot="D:/sbox/sbox-official-mounts/game"
```

可选零售测试通过 `TF2_SKY_TEST_GAME` 指定本机 Titanfall 2 安装，验证天空盒选择与真实 MDL 几何解析。`TF2_CONTENT_TEST_GAME` 用于内容清单，`TF2_PROBE_AUDIT_ROOT` 用于已提取的探针数据；未提供的本地夹具会跳过。

`Validation/validate.ps1` 使用正式包的 DLL 在独立进程检查数据恢复、克隆、原生资源销毁、官方后台任务同步等待和公开天空盒生命周期。使用专门的官方源码构建目录或带 `.titanfall2-build-runtime` 标记的测试运行库，勿指向正在运行的编辑器。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Titanfall2/Validation/validate.ps1 -RuntimeRoot "D:/sbox/sbox-official-mounts/game" -PackageDirectory "artifacts/titanfall2-official/Titanfall2"
```

`-Graphics -GameDirectory "C:/path/to/Titanfall2" -MapName sp_boomtown_start` 可选检查真实地图的重复加载与渲染，需要完整且匹配的官方 core 内容。无图形检查不能证明画面或帧率等价。

## 官方引擎接入

安装内容通过 `InitializeContext.GetFileSystem(1237970)` 返回的只读文件系统读取。缓存、导出和设置通过 `FileSystem.Data`。Northstar profile 只能位于同一游戏文件系统内，配置值使用相对路径。

普通道具使用现有 `MountedInstanceBatch`，特殊材质仍使用原生对象，保留 LOD、PVS、距离和阴影策略。旧 GPU MDI 提交与 `GpuSceneCompatibility` 已移除，`GPU_STATIC_SCENE` 无法启用；保留的纯布局工具和 shader 不代表运行时使用 MDI。帧率影响需要固定机位实测。

地图生成资源跟随官方 `SceneLoader.OnSceneUnloaded()` 释放，场景实例各自释放局部光缓冲、渲染对象和碰撞。普通 loader 缓存由官方引擎管理；不反射清空缓存或强制 GC，因此不能保证与旧版相同的切图回收时机。3D 天空盒使用公开 `Skybox3D`，动画采样由官方 renderer 调度。

组件和控制台命令交给正常 addon 加载流程注册。无需引擎修改、私有接口反射、允许列表扩展或旁路程序集。

## 安装与使用

将完成构建的 `Titanfall2` 输出文件夹安装到匹配的新官方引擎 `game/mounts/Titanfall2/`。Steam 安装根就是此处的 `game`。新目录名是 **mounts**；旧 `mount/titanfall2` 位置不适用。构建脚本不自动部署。

挂载标识保持 `titanfall2`：

```text
mount://titanfall2/maps/mp_angel_city.bsp.scene
mount://titanfall2/maps/mp_angel_city.bsp.world.scene
```

## 已知限制

- **碰撞**：
  - surfaceprop 只覆盖 MDL（模型级用于物理体、骨骼级用于 hitbox）和 legacy VMT；rBSP 世界碰撞和 RPAK MATL 仍是默认表面。
  - `nowallrun` 标签由 `NOCLIMB` 位推断，没有对照零售版的墙跑代码确认。
  - 没有 VPHY 和 hitbox 的模型，其碰撞是渲染网格或包围凸包的近似。
- **游戏逻辑**：门、触发器、脚本和网络同步属于游戏代码，挂载只提供几何、碰撞和实体数据。
- **动画**：每个序列只导入它的第一个动画，混合序列的其余动画会被忽略。
- **粒子**：
  - 只实现了 PCF operator 和 initializer 的一部分。
  - 天空盒世界只承载 vista 模型，不包含粒子。
  - 控制点在加载时从实体数据确定，运行时移动需要游戏代码调用 `SetControlPoint`。
- **音频**：
  - 每个 `trigger_soundscape` 区域只播放一个循环声音，用引擎的球形 `SoundscapeTrigger` 实现，同一时间只有一个区域生效。
  - `ambient_generic` 发声点数量有上限（默认 12 个）。
  - 没有 DSP 效果。
  - **声音事件不可用**：零售的 Miles 事件解释器是原生运行时，已随原生桥移除。`titanfall2_sound_event` 组件保留但只记录一次警告；使用 `.vsnd` 样本、`Sound` 或 `Soundscape` 可以达到相近效果。
  - BCF 解码与零售 Miles 运行时 94.07% 样本逐位相同，100% 在 ±2 LSB 以内（最大 2 LSB），听感一致。
  - 4/6 声道 BCF 声音先用 C# 解码再折叠为双声道（Miles 近似：中心 0.7071、环绕 0.5、省略 LFE；4 声道另做 1/√2 等功率归一化）。与旧原生桥的 Miles 私有混音不完全相同（相关系数中位数 1.0，最低 0.9136，4/6 声道电平与零售一致），但这些声音不再丢失；3/5/7 声道布局会返回失败并跳过。
- **雾和后处理**：
  - 雾、光照环境和后处理在每个场景中按单个相机选择，不在多个相机或多张地图之间协调。
  - 不支持脚本或网络驱动的雾渐变；局部颜色校正导入静态范围与相机淡化，不执行零售脚本。
  - 泛光包含基础、wide 和 streak 分支；参数和画面仍需逐图对照零售版。
  - 地图实体中的曝光控制不会被导入。
- **材质**：VMT 代理只实现了一部分，不支持任意 2D 纹理变换和由实体驱动的输入。
- **显存**：没有跨地图的 GPU 资源 LRU 或显存预算。纹理上限跟随公开玩家画质设置，可用 `SBOX_TITANFALL2_TEXTURE_MAX` 覆盖；无法取得设置时回退为 1024，运行中不会升级到更高的 mip。
