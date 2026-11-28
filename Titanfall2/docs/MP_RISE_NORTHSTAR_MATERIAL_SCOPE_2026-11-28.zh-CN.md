# mp_rise 顶部网板与 Northstar 条件材质覆盖

## 根因

红色管道上方的水平网板是 BSP world mesh 7448–7450，材质为 `WORLD/METAL/CHAIN_LINK_IMC_01`，主要高度约为 z=640.5503／639.9578。源几何、索引和顶点 alpha 完整，碰撞也存在；不是静态道具遗漏或 BSP 外置 lump 缺失。

未指定 `NORTHSTAR_MAP` 时，原内容发现逻辑把第三方 `vpk.json` 的 `MapOnly` 声明用于加载对应 RPAK，却同时授予这些包全局 Mod 覆盖优先级。安装的 `Allusive.TrainingGround` 因而在 `mp_rise` 覆盖同名链网材质。其 raster flags 为 `0x12`，材质被正常识别为 cutout decal；零售候选为 `0x02`，应为普通双面 cutout。错误发生在选择材质来源阶段，未修改 shader 或 decal 判别规则。

## 修复行为

- 无指定地图：分别解析全局规则和 `MapOnly` 地图上下文。仅由地图上下文增加的 RPAK（包括其 Postload 链）作为 fallback，排在全部全局 Mod／零售候选之后。
- 独有自定义地图资产继续注册；路径与 native GUID 都遵守同一候选顺序。
- 显式指定地图：匹配条件包保持原有 Mod 覆盖优先级，其他地图条件包仍推迟。
- 无条件 Preload，以及全局依赖触发的 Postload 保持原优先级。快照顺序改变会更新 revision，派生缓存随之失效。

浏览全图时，自定义地图如需覆盖共享资源，须显式设置对应 `NORTHSTAR_MAP`。此修复不改变 VPK／loose 覆盖规则；现有 RPAK 解析仍把所有零售包视为已加载依赖，不等同于完整 Northstar 运行时按地图装载模拟。

## 验证

最终联合回归 `ContentSnapshotTests`、`UiAtlasTests`、`WeaponOpticTests`：69 项通过，0 失败，0 跳过；包含注册真实安装文件系统后的本地库存测试。新增二进制 RPAK fixture 验证零售及全局 Mod 冲突优先级、native GUID 一致性、独有资源保留、两层 Postload 链、全局 Preload／Postload、显式同地图及其他地图。测试记录位于 `D:\sbox\_codex-build\rise-optics-20261128\tests\rise-optics-regression.trx`。

真实安装的只读来源审计：

| 地图上下文 | 链网材质赢家 | IsDecal | 独有 TrainingGround fallback 资源 |
| --- | --- | --- | --- |
| 未指定 | 零售 `mp_rise(07).rpak` | false | 3224 |
| `mp_rise` | 零售 `mp_rise(07).rpak` | false | 0 |
| `mp_training_ground` | Mod `mp_training_ground.rpak` | true | 0（按正常 Mod 注册） |

审计程序与结果位于 `D:\sbox\_codex-build\rise-profile-scope-20261128\`。同相机运行时 A/B 已确认：将该错误赢家的 descriptor 恢复为非 decal 后，缺失的链网与方格网板立即出现；该临时覆盖已恢复。截图位于 `D:\sbox\_codex-build\rise-grid-20261127\rise-live-before.png` 与 `rise-live-cutout.png`。该 A/B 是根因验证；最终官方包编译和部署后的画面验收由主任务独立记录。

最终官方编译包随后在独立原生进程完整加载 `mp_rise` 验证通过：保留自动 Northstar profile，未指定地图，未修改 descriptor；两台已定位相机实际 PNG 都显示网板。输出位于 `D:\sbox\_codex-build\rise-final-map-20261128\`（`roof-user.png`、`roof-overview.png`、`metadata.json`、`load.txt`）。包 MVID 为 `ffecc8b1-05c2-4b04-9f95-3c657f3d844f`，实际赢家为零售 `mp_rise(07).rpak`、`IsDecal=false`，TrainingGround 仍有 3836 个赢得注册的资源；20094 个静态道具准备完成，失败数为 0。未部署或操作用户游戏进程。
