# mp_coliseum_column 外圈墙缺失排查与修复

日期：2026-11-27。定位并修复源码；没有部署到 Steam，也没有重启、切图或修改正在运行的游戏。

## 结论

外圈墙是静态物件 `models/levels_terrain/mp_coliseum/mp_coliseum_dome_01.mdl`，不是 BSP 普通墙面。
原始 VPK 中该模型的 LOD0 有 1272 个三角形，使用 `world/test/kodai_fake_screen_01_world` 特殊双层天空屏幕材质。
本次确认的缺失原因是静态物件合批缓存丢失了该模型的回退记录，并非材质被错误设置成透明。

## 证据和故障链

1. 当次游戏日志（18:35:42）报告缓存命中：10 个物件，9 个 aggregated，0 个 residual-batched，0 个 fallback，0 个 CPU batches，却仍标记 10/10 完成。
2. 直接读取安装目录的原版 `englishclient_mp_coliseum_column.bsp.pak000_dir.vpk`，排除天空远景后，穹顶是 `InstanceId=9`，原 BSP `SourceIndex=11`。
3. 光照探针将两个 16 位灯光索引打包到浮点字段；`0xFFFFFFFF` 等合法位模式是浮点 NaN，必须原样保留。
4. 旧代码的两个 `HashSet<StreamedProp>` 都使用完整值比较。实际穹顶数据的 `prop.Equals(prop)` 和插入后的 `set.Contains(prop)` 均为 false。
5. 因此冷加载虽然保留穹顶的独立绘制路径，写入缓存时却未找到其回退索引；热加载随后只恢复 9 个普通物件。即使单独修好缓存写入，恢复阶段的同类值比较也会再次漏掉穹顶。

## 修改

- `StaticPropSceneData.cs`：提供仅在同一编译场景内使用的 `InstanceIdentityComparer`，按稳定且连续的 `InstanceId` 匹配物件。
- `StaticPropStreamer.Batches.cs`：缓存写入时的回退集合改用该比较器。
- `StaticPropStreamer.Preparation.cs`：缓存恢复时的回退集合使用同一比较器。
- `StaticPropAggregate.Cache.cs`：缓存版本 4 → 5，自动绕过已经丢失回退索引的旧缓存，无须删除用户缓存。
- `Tests/StaticPropFallbackIdentityTests.cs`：新增 NaN 灯光位值、重复模型实例、冷/热回退筛选及两张原版竞技场地图的回归测试。Tests 目录按仓库既有规则忽略，测试文件保留在本机。

## 验证

在独立目录 `D:\sbox\_codex-build\coliseum-column-20261127` 完成，基线提交 `b34a4454e2f671a9f601ffcb0638a99561e32a00` 加本次四文件修复；没有使用或覆盖同事尚未提交的玩法修改。

- 定向回归：8/8 通过，无跳过。包含 `mp_coliseum_column` 和 `mp_coliseum` 的真实 VPK 数据，以及已有 `StaticPropSceneDataTests`。
- 官方 `MenuBuild.exe` 沙箱编译成功，退出码 0。
- 写回工作区的四个生产文件与已验证副本一致；`git diff --check` 通过。
- 原始诊断：`retail-audit.txt`；编译日志：`menubuild.log`；测试结果：`source/Titanfall2/Tests/TestResults/coliseum-regression.trx`，均在上述独立目录。

尚未进行修复后的游戏内画面对照。独立编译包只用于验证；最终安装应从包含并行修复的完整工作区统一构建，避免覆盖其他改动。运行验收应分别检查首次重建缓存和再次加载：穹顶保留，热加载回退列表包含该物件，外墙持续可见。
