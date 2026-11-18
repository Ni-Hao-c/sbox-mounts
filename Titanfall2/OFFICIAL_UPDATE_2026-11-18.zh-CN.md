# 2026-11-18 官方同步记录

## 源码与同步范围

- 目标目录：`D:\sbox\sbox-mounts\Titanfall2`。
- 更新前主工作树为 `main @ e4f10cf`，Titanfall2 目录为空，Git 未跟踪该目录的文件。
- 从现有 `codex/titanfall2-resource-optimization @ 7ed0293` 建立
  `codex/titanfall2-official-update-20261118`，恢复已提交的完整 TF2 挂载。
- 合并官方 `Facepunch/sbox-mounts main @ 204f4e71dcf4944588556e05365b58da659971e3`，
  合并提交为 `62b9837f30e44c1fb3a44dcfb320d033cc313e90`，无冲突。
- 优化分支和其独立工作树保留。本记录对应的更新按用户要求推送至
  `Ni-Hao-c/sbox-mounts` 的 `main`；构建产物不纳入 Git。

用户指定的引擎范围为 `4f5d1db20b56dea13460b90027049c01b18e96ac`（含）之后的官方提交。
已 fetch 官方远端，检查至当前 `origin/master @ 5da179839`，共 19 个提交。
本地引擎 `7af16345e` 已包含该官方基线，因此不需要再次合并引擎仓库。

## 指定引擎范围的适配结论

| 官方提交 | TF2 影响与处理 |
| --- | --- |
| `4f5d1db20` 导航链接成本按实际绕行路径计价 | 修改引擎内部 NavQuery 搜索，不改变挂载调用接口。TF2 的 DeferredNavMeshBuilder 仅设置边界、标签、请求瓦片生成；查询由官方 NavMesh/agent 执行，不复制搜索算法，也不修改 TF2 的瓦片半径来冒充适配。 |
| `3b33a475e`、`592025a6c`、`079629de3`、`3e80ced85` 挂载类型注册 | 官方负责菜单/游戏类型库注册、重建恢复、失败回滚和 JSON upgrader 刷新。TF2 基线已有对应验证 fixture，并已移除临时本地引擎补丁前置要求；本次不重新引入反射注册或白名单绕过。 |
| `68332047d` / PR #6656 合并 opaque/shadow draws | 修改 SceneRenderer 的 SortAcrossPartitions 标志；现有 AggregateRenderer 和普通实例提交继续走官方管线，挂载无需复制排序逻辑。未测量性能收益。 |
| `9376fbeb7` / PR #6699 验证 AnimGraph2 节点 | TF2 挂载没有调用 GraphBuilder 状态机构建接口，无接口迁移点。 |
| `fcf3c407a` / PR #6690 导入 skeleton morph channels | 修改编辑器骨架导入工具，不是 TF2 的 MDL53 解析路径。 |
| `b777f9ffd` / PR #6619 Workshop addons | 修改官方包/Workshop 管理、脚本生成、地图包需求释放等。TF2 使用 MountContext/ResourceLoader 和宿主提供的只读文件系统，没有自建 Workshop 包生命周期；本次沙箱包编译和内容构建验证可用。 |
| `cf755bea4` / PR #6693、`a0c98ec0e` / PR #6683 | 官方 PlayerController 蹲伏/相机和脚步位置修复；挂载不复制其移动实现。 |
| 其余 PR #6682、#6684、#6686、#6689、#6692、#6695、#6698、#6706 | 主要涉及 gizmo、网格编辑、hotload 测试和内部整理。检查了涉及碰撞组件、组件 flags 的变化；没有要求改写 TF2 挂载接口。 |

指定范围没有要求修改 TF2 生产代码的破坏性 API 变化。重新构建包通过代表当前安装的编译兼容性；
不等于逐个上游功能的运行时验收。

## 官方改动的适用性

- [官方水面 PR #1](https://github.com/Facepunch/sbox-mounts/pull/1)：
  合并水面反射/折射、共享捕获、水下效果和相关物理、音效支持。
  后续 `204f4e7` 修正地图水面设置及天空盒水面渲染。这些改动属于 SourceEngine；
  同步保留其原始实现，没有将 Source 1 的 BSP 数据和材质语义替换到 TF2。
- [探针释放修复 449005f](https://github.com/Facepunch/sbox-mounts/commit/449005f5b420cd72d556e8847d75a6522e234dc4)：
  修复 SourceEngine 地图释放及重建时的资源清理。本次合并包含该修复。
  TF2 基线已经覆写官方 `SceneLoader.OnSceneUnloaded`，并有自己的构建资源释放路径；
  本次没有修改 TF2 的资源生命周期算法，也没有验证其与 SourceEngine 的行为完全等价。
- [挂载沙箱化 PR #6508](https://github.com/Facepunch/sbox-public/pull/6508)：
  TF2 基线已采用 `.sbproj`、只读游戏文件系统及官方 `SboxBuild build-mount` 流程。
- [运行时动画 PR #6648](https://github.com/Facepunch/sbox-public/pull/6648) 与
  官方子资源解析提交 `a00fe43ac` 提供新的可选接口；本次未新增 TF2 动画导入或子资源功能。
- [合并 opaque draws PR #6656](https://github.com/Facepunch/sbox-public/pull/6656)
  属于引擎 SceneRenderer 的管线改动，不需要复制到挂载代码中。

相对于 `7ed0293`，本次 TF2 生产代码保持一致；更新的实质是恢复目标目录、合并上游仓库，
并在当前本地引擎安装上重新构建验证。没有为制造差异而引入不必要的 API 改写。

## 验证证据

使用本地引擎源码 `D:\sbox\sbox-public @ 7af16345e` 及其现有 managed/native 工具运行：

```powershell
.\Titanfall2\Build\build.ps1 `
  -EngineSource D:\sbox\sbox-public `
  -OutputDirectory D:\sbox\_codex-build\tf2-official-update-20261118 `
  -Test
```

- 已有测试：122 通过、0 失败、15 跳过；跳过项需要零售游戏内容/显式安装参数。
- 官方沙箱挂载包构建成功。
- 30 个 shader 编译成功。
- 内容构建：1 compiled，0 unsupported。
- `git diff --check` 通过。未新增测试。
- 通过官方 SboxBuild 运行过滤测试：
  `test --no-build --filter "FullyQualifiedName~Nav.Lib.Tests|FullyQualifiedName~MountingTests"`。
  Navigation 259 项通过，Mounting integration 54 项通过；其他测试进程没有匹配项，
  不能把 runner 的全绿摘要解释成全引擎测试全部运行。
  测试使用已存在的 Release 测试程序集；本次没有重新编译全引擎及其测试程序集。
  首次启动因系统 TEMP 目录拒绝访问而失败，改用本次产物目录下 `.build-temp` 后运行成功。
- 官方测试日志：`D:\sbox\_codex-build\tf2-official-update-20261118-engine-tests.log`。
- 构建日志：`D:\sbox\_codex-build\tf2-official-update-20261118.log`。
- 原始测试明细：`Titanfall2\Tests\TestResults\migration.trx`（验证后已按清理要求删除，结果摘要保留在外部构建日志中）。
- 产物：`D:\sbox\_codex-build\tf2-official-update-20261118\Titanfall2`。
- `package.local.titanfall2.dll` SHA256：
  `1E2B63263C23268465A515E9EF9F4E832B586BE17C7EA2E50BD21226C2A64666`。

构建使用现有引擎安装，没有执行全引擎重建。未运行 TF2 专用原生生命周期 fixture、零售地图加载、
实机画面或性能 A/B；未将产物部署到游戏 mounts 目录，也未重启游戏/编辑器。
这些结果不能作为 FPS 提升、视觉一致性或完整资源生命周期验收的结论。
