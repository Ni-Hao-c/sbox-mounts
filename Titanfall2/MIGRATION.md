# Titanfall 2 官方沙箱挂载迁移

当前适配日期：2026-11-14。目标官方引擎：`07a81a85b1486a7c4ee68446d730f56ee478fce8`；新挂载接入提交：`742b5a5095f5af945055c486ecc772352f941e73`。

本次已切换正常 addon 编译及 `SboxBuild build-mount`，禁用 unsafe，保留用户的 C# LZHAM / Bink Audio。游戏内容由 `InitializeContext.GetFileSystem` 提供；缓存、设置和导出使用 `FileSystem.Data`。

按用户确认，静态道具采用现有原生实例批次。私有 GPU 命令兼容层、加载器缓存反射、天空盒反射、手动类型/命令注册和强制 GC 均已去除。地图构建产物跟随官方 `OnSceneUnloaded`；原生实例渲染对象和碰撞保持场景所有权。切图时普通资源缓存的回收时机由官方引擎决定。

配置改为 `FileSystem.Data/titanfall2.settings.json`，保持原键名和值的解析规则。profile 与导出目录限定在引擎提供的文件系统。大型包修订采用长度和头尾采样，不等价于完整内容摘要；派生缓存发布也不声称具备原子 rename。

第一轮官方沙箱编译、30 个着色器及内容构建成功。原生无图形生命周期、后台任务同步等待与公开 Skybox3D 生命周期已验证；Angel City/Eden 的零售天空盒选择和 3 个真实 MDL 的 LOD0 解析已验证。最终构建与测试结果见仓库 `artifacts/` 中本轮日志及报告。未据此作 FPS、完整画面对照或全地图连续切换结论，未部署到 Steam 安装。

构建及安装入口见 [README.md](README.md)。下方为之前迁移的历史记录，旧桥接、包名、安装目录与验证结果不适用于当前官方沙箱版本。

---

# Titanfall 2 独立挂载迁移记录

日期：2026-11-13。

当前状态：LZHAM 和 Bink Audio 已由后续改动替换为纯 C#，`Native/` 及桥接 DLL 已移除。
本次按其他挂载重新生成项目配置，保留这些托管实现和测试。下方初次迁移的原生桥接、
文件数量、产物和测试结果属于当时的历史记录，不能作为当前版本的验证结果。

## 项目配置重新生成（当前）

- `.sbproj` 按 SourceEngine 的配置重新生成，除 Title / Ident 外内容一致；移除无效的旧路径和 HasCode 字段。
- `BuildMounts.bat` 统一收集六个直接子目录中的挂载项目，不再对 Titanfall 2 单独分流。
- 增加 `--list` 检查项目列表，`--engine-source` / `SBOX_ENGINE_SOURCE` 明确指定构建工具源码目录。
- 保留已有纯 C# LZHAM / Bink Audio 实现和测试，辅助构建仍直接引用官方安装的程序集。
- 本次官方程序集构建成功，0 警告、0 错误；129 项托管测试中 115 项通过，14 项可选本机数据测试跳过。
- 本次纯托管验证包：`artifacts/titanfall2-project-regenerated-20261113/`，只含一个托管挂载 DLL、着色器源码和哈希清单，未执行着色器或原生运行验证。
- 当前本机 `sbox-public` 的 SboxBuild 未提供 `build-mount`，因此统一构建本次只验证了项目收集，不能作为六个项目构建通过的记录。
- 全量 C# 空白检查发现用户当前托管解码相关源码存在行尾格式问题；本次没有修改这些实现文件。

## 初次迁移历史

本次将现有泰坦陨落 2 挂载迁入本仓库的 `Titanfall2/`，保留原来的
`mount://titanfall2/` 资源标识。原引擎工作区保留，没有删除、重置或部署它的文件。

## 迁移内容

- `Code/`：144 个 C# 文件，包含内容发现、解析器、资源加载器、地图组件、GPU 道具、碰撞、音频和后处理。
- `Assets/shaders/`：30 个生产着色器和 18 个 HLSL 头文件。
- `Native/`：LZHAM、Miles/Bink 桥接源码及 LZHAM 的第三方文件和许可证。
- `Build/`：直接引用官方程序集的独立工程、打包和隔离着色器编译脚本。
- `Tests/`：迁移的回归测试，以及新增的官方 GPU 命令与天空盒桥接接口检查。
- `Validation/`：使用独立官方运行库副本的场景生命周期和实图加载验证。

当前项目已按其他挂载重新生成 `.sbproj`，由统一 `BuildMounts.bat` 收集和构建；
`BuildTitanfall2.bat` 保留为官方程序集的辅助验证入口。旧的 `HasCode` 设置已移除：
当前引擎根据 `Code/` 目录判断是否包含代码，该旧字段不能关闭编译。

## 排除对自定义引擎的依赖

托管构建仅引用目标安装中的 `Sandbox.Engine`、`Sandbox.System`、
`Sandbox.Filesystem`、`Sandbox.Mounting`、`Sandbox.Reflection`，没有引擎源码工程引用。
包中没有引擎 DLL，也没有修改引擎 API、生成的 interop 或编译器允许列表。

迁移后的 `LoaderCompatibility.cs` 只走官方兼容实现，去掉了调用自定义缓存 API 的
条件分支。GPU 命令执行、天空盒和缓存释放仍使用经过检查的官方内部接口；
这使现有功能能在当前官方版运行，但不能保证未来所有 Steam 版本都兼容。

旧挂载还引用了自定义引擎公共目录中的 `common/imported_height_fog.hlsl`。
本次将其移入挂载自己的 `titanfall2_height_fog.hlsl` 并改写引用，官方 core 无需增加文件。
全部生产着色器重新用官方编译器编译，没有复用开发引擎的 `.shader_c`。

## 实际验证

目标官方版本：`26.11.10a`，构建号 `43053683282`。
`Sandbox.Engine.dll` SHA-256：

```text
47002EDEB8068508C62FC70FE01D85FEF6B5687D50BED6A515A629A076CFAA50
```

- 托管编译：0 警告、0 错误；两个原生桥接 DLL 从本仓库源码构建成功。
- 托管回归：114 项中 103 项通过，11 项因没有提取的探针测试数据而跳过。
  已包含真实 `mp_eden`、`mp_angel_city` 天空盒选择、零售/Northstar 内容快照及官方内部接口检查。
- 内容快照：236,781 项来源记录，104,988 项获胜资源；配置和游戏资源只读。
- 官方着色器编译：30/30 成功，保留已有 `DoAtmospherics` 弃用警告。
- C# 空白格式检查：挂载与托管测试工程通过。
- 原生图形验证：`sp_boomtown_start.bsp.world.scene` 两轮加载、等待就绪、渲染和销毁通过。
- 无图形设备验证：数据恢复、独立克隆和资源持有者生命周期通过。

两轮地图验证的计数一致：

| 项目 | 第一次 | 第二次 |
| --- | ---: | ---: |
| 道具数 | 13,948 | 13,948 |
| GPU 实例数 | 13,924 | 13,924 |
| 其中混合材质实例 | 1,450 | 1,450 |
| 常驻碰撞实例 | 9,186 | 9,186 |
| contents 过滤的碰撞 | 6 | 6 |
| 失败道具 | 0 | 0 |

原生验证实际使用的挂载 MVID 为 `186a2c82-4703-48fc-884d-485865e61a61`，
验证进程加载的挂载 DLL 与最终包中的 DLL SHA-256 一致。

最终包位于仓库的 `artifacts/titanfall2-final/`，其中 `compatibility.json`
记录 81 个包文件及五个官方依赖程序集的 SHA-256。全部包文件的哈希已复查一致。
构建日志为 `artifacts/final-build.log`，托管测试结果为
`Tests/TestResults/migration-retail.trx`，原生结果为
`artifacts/native-6b0b3a9a556b474eacdad33170b99577/runtime-results.json` 和
`artifacts/native-580a6e3f76fb48598e3d83d3436257e3/runtime-results.json`。
产物和本机测试证据不纳入 Git。

## 验证范围

本次验证了当前官方版本上的独立构建、资源读取和上述地图的加载/渲染/释放。
测试图解析出的 PVS 表为空，不能据此验证有效 PVS 的地图；两轮渲染也不能证明
所有地图的视觉一致性、玩法、非居留碰撞模式、长期稳定性或整图性能。
首次图形测试的默认处理缓存目录出现权限不足，回退解析后测试通过；验证脚本现已将缓存
明确放到构建工作区，避免后续测试使用用户的默认缓存目录。

没有将结果部署到官方安装，没有关闭或重启现有编辑器，没有提交或推送 Git 改动。

## 纯托管迁移：移除 C++ 原生桥

日期：2026-11-13。项目不再允许使用 C++ 代码，因此把原有的两个自建原生桥全部改成
挂载程序集内的纯 C# 实现，并删除所有 C++ 源码、原生构建步骤、随包 DLL 和
`DllImport` / `NativeLibrary` 调用。

删除的内容：

- `Native/LzhamBridge/`：`lzham_bridge.cpp`、LZHAM alpha8 第三方头文件与
  `liblzham_x64.lib`，产物 `titanfall2_lzham.dll`。
- `Native/MilesBridge/`：`miles_bridge.cpp`、`miles_events.inl`，产物
  `titanfall2_miles.dll`（运行时加载零售 `mileswin64.dll` / `binkawin64.dll`）。

### LZHAM 解压

- 按零售 VPK 数据实际使用的 **LZHAM Alpha8** 解码器逐单元移植。Alpha8 与 v1.0 的
  位流参数不同（2 位流配置、prev-char 上下文、表更新节奏、polar code 等），
  不能用 v1.0 源码替代。
- 新增 `Code/Formats/Rpak/Lzham{Decoder,Decompressor,SymbolCodec,AdaptiveModel,DecoderTables,PrefixCoding,CodeLengthGenerator,Checksum}.cs`。
  `LzhamDecoder.TryDecompress` 的签名保持不变，`VpkArchive` 无需修改。
- 验证：
  - 735 个真实 VPK 压缩块与旧原生 DLL 输出逐字节一致（399.5 MiB 输出，覆盖全部
    38 个 `*_dir.vpk`，含 16 B–1 MiB 全部尺寸档与 369 个 1 MiB 块）。
  - 4 个完整 VPK 的 9,332 个条目全部重建（preload + 全部 chunk）后与 VPK 目录树
    的 CRC32 一致（16.7 GiB，含 1,184 个多 chunk 条目和跨卷 chunk）。
  - 吞吐约 90–100 MiB/s（旧原生桥约 97 MiB/s）；16 线程并发结果一致。
  - 1,285 例畸形/截断/随机损坏输入返回失败并带消息，无异常逃逸、无越界分配。
    单比特翻转有约 0.2% 会被接受并解出不同字节，原因是 LZHAM 自身的 Adler-32
    在结构化数据上碰撞；旧原生库行为完全相同。

### Bink Audio 解码

- BCF 是 RAD **Bink Audio 2（DCT 变体）**，独立 `binka` 容器：24 字节头 + u16
  packet 尺寸表 + 每 packet 的系数/分带量化/符号位/逆 DCT/overlap-add。纯 C# 实现
  位于 `Code/Formats/Miles/`，输出 int16 PCM 后仍交给 `SoundFile.FromPcm`。
- 验证（108 条单/双声道真实 BCF，32,195,975 个样本）：
  - 0 解码失败；帧数、采样率、声道数、PCM 长度与 Miles 运行时完全一致，两次运行结果相同。
  - 94.07% 样本逐位相同；99.9993% 在 ±1 LSB 以内；100% 在 ±2 LSB 以内；最大偏差
    2 LSB，平均绝对误差 0.059 LSB。
  - 用 FFmpeg 8.1 作为第三个 oracle 对比：本实现与 FFmpeg 的平均误差 0.034 LSB，
    而 FFmpeg 与 Miles 的平均误差 0.091 LSB（FFmpeg 对 Miles 只有 91.33% 逐位相同），
    说明本实现比 FFmpeg 更接近零售，剩余差异来自 `binkawin64.dll` 自身的浮点/取整管线；
    差异在样本上均匀分布，没有集中在 block 边界。
  - 吞吐约 343 倍实时（415.3 s PCM / 1,211 ms）。
  - 与 FFmpeg 的独立解码对比误差 <0.004 LSB，且本实现比 FFmpeg 更接近 Miles
    （FFmpeg 对 Miles 只有 91.33% 逐位相同），说明剩余差异来自零售
    `binkawin64.dll` 自身的浮点与取整管线。
- 声道：单/双声道逐样本对比；4/6 声道流先按原始声道解码，再在解码过程中折叠为双声道
  （Miles 近似混音，省略 LFE：6 声道中心 0.7071、环绕 0.5；4 声道 L=FL+0.7071×BL、
  R=FR+0.7071×BR，整体再乘 1/√2 做等功率归一化）。6 声道折叠的电平与零售一致
  （RMS 比中位数 1.0000），4 声道归一化后与零售 RMS 比 0.9989–0.9998；相关系数中位数
  1.0000，最低 0.9136（出现在零售混音会削波的大音量音效）。不静音、不产生垃圾样本；
  3/5/7 声道布局返回失败并给出消息。零售 `general.mbnk` 中 >2 声道的记录占 3.25%
  （占总时长 20.7%），这些声音现在都能解码播放。
- 声音事件：Miles 事件解释器只能在零售原生运行时里执行，已随原生桥移除。
  `Code/Formats/Miles/MilesEventSession.cs` 删除，`Titanfall2Mount.CreateSoundEventSession`
  与 `ReleaseSoundEventSessions` 不再提供；`MapAudio` 只保留 `.vsnd` 点声源与
  soundscape 路径；`Titanfall2SoundEventPlayer`（组件名 `titanfall2_sound_event`）
  保留为兼容占位并记录一次警告。

### 构建与仓库守卫

- 不再需要 Visual Studio C++ 构建工具；`Build/build.ps1` 只做托管构建与打包。
- `Build/build.ps1` 新增守卫：`Code/` 中出现 `DllImport`、`LibraryImport`、
  `NativeLibrary.`、`GetDelegateForFunctionPointer`、`GetProcAddress` 或
  `LoadLibrary`，或仓库中出现 `.cpp/.c/.h/.hpp/.inl/.lib/.dll/.exp`（`bin`、`obj`、
  `TestResults` 除外）时直接构建失败。
- `Tests/` 与 `Validation/` 工程移除了原生 DLL 复制项；包内容只剩 `titanfall2.dll`、
  `Assets/shaders/` 和 `compatibility.json`。
- 新增自包含回归：`Tests/LzhamDecoderTests.cs`（内嵌金标向量、截断/损坏/参数/并发）
  与 `Tests/MilesAudioTests.cs`（内嵌单/双声道 BCF、损坏头、畸形 packet 表、并发）。

### 实际验证

- `dotnet build`：0 警告、0 错误。
- 托管回归：115 通过、14 跳过、0 失败（测试进程需要可写 TEMP；`-Test` 已把测试
  临时目录放到构建工作区）。
- 反编译审计：挂载程序集内无 P/Invoke、无 extern 方法、无原生字符串引用；
  源码中没有 DllImport/NativeLibrary，仓库中没有 C++ 源码或原生二进制。
- 真实地图运行验证（独立进程 + 独立引擎副本，`-CompileShaders` 后用官方编译器
  编译全部 30 个着色器）：加载 `sp_boomtown_start.bsp.world.scene` 两轮，等待就绪、
  渲染并释放。两轮计数与原生桥时代的记录一致：道具 13,948、GPU 实例 13,924
  （其中混合材质 1,450）、常驻碰撞 9,186、contents 过滤 6、失败道具 0。
  这说明纯托管 LZHAM 解出的地图数据与旧原生桥完全一致。
- 真实数据交叉验证全部使用开发期语料，语料与原生 oracle 不在仓库内；
  包内不包含任何原生 DLL。

### 与旧包的差异

- 包不再包含 `titanfall2_lzham.dll` 与 `titanfall2_miles.dll`。
- BCF 解码与零售 Miles 差 ±1 LSB（见上），听感一致但不逐位相同。
- 4/6 声道声音先原生解码再折叠为双声道；与旧桥的 Miles 私有混音不同
  （相关系数 ≥0.91，逐样本不完全相同），但这部分声音不再丢失。
- Miles 声音事件不可用（旧桥依赖零售 Miles 事件解释器）。
