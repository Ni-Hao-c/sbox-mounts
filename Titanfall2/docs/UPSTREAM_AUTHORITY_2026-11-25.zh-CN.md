# 官方同步、地图预览、动画分层与宿主伤害归属

2026-11-25 拉取 `Facepunch/sbox-mounts` 后，当前分叉缺少 197 个上游提交，已合并到 `e38c19a`。合并提交为 `22635c0`；唯一冲突是根目录 `.gitignore`，保留双方规则。工作分支为 `codex/titanfall2-upstream-authority-20261125`。本轮没有推送或安装到游戏目录。

## 地图资源寿命

官方 `449005f` 属于挂载仓库的 SceneFile 生命周期修复，后续官方实现还考虑了仍在使用探针纹理的场景。完整合并保留这些后续修正。

TF2 原来已经使用 `SceneLoader.OnSceneUnloaded()`；本轮补上 `BuildScene()` 开始前释放旧构建的路径。缓存失效时把旧 WorldBuild 的资源租约交给仍使用该构建的场景，最后一个场景销毁后释放纹理。匹配同时检查挂载、资源路径和构建代号，避免其他地图或同地图另一代构建延长资源寿命。没有修改引擎，也没有反射清空引擎资源缓存或强制 GC。

## 使用原游戏地图预览

`Map.Thumbnail.cs` 接入官方 `SceneLoader.GetThumbnail()`。从本地游戏 `scripts/vscripts/ui/menu_private_match.nut` 读取地图和 `loadscreens/...` 的数据映射，不执行脚本；再从已有 RPak UI 图集读取对应图片。原版的 `mp_angel_city → mp_angle_city_r2_lobby`、`mp_lf_stacks → mp_stacks_lobby` 等别名得到保留。

预览不加载 BSP。每个 SceneLoader 获得独立的缩略图纹理，避免它在关闭时销毁 HUD 共用资源。没有重新拍摄、生成或随仓库分发游戏预览图。缺少原版图片时返回空值，仍由官方尝试 `Mounts/titanfall2/thumbs/<资源相对路径>.png`。`Metadata.StartupScene` 继续指向宿主 Sandbox 场景，与缩略图无关。

已原生验证 Angel City、Stacks、Homestead、Glitch、Colony、Forwardbase Kodai 六张多人地图，图片尺寸均为 479×269。其他映射沿用相同入口，但尚未逐张验收；战役章节选图未接入。

## 动画移植范围

官方文件现已随合并进入 `SourceEngine/Code/Gameplay/Source/`：

| 官方文件 | TF2 本轮处理 |
| --- | --- |
| `SourceAnimationPlayer.cs` | 采用双槽选择器、基础动作层和骨骼遮罩叠加方式，实现 `Titanfall2ActorAnimationPlayer` |
| `SourceNpcAnimator.cs` | 采用移动基础层、上半身开火、独立换弹层；近战和投掷占用身体层 |
| `SourceLinkAnimation.cs` | 保留为后续导航连接动作适配对象，未直接用于 TF2 |
| `SourceHoldPose.cs` / `SourceHandPose.cs` | 保留为持枪和手部姿态适配对象，未直接套用 Source 1 骨骼数据 |

TF2 gameplay 模型把已验证的 MDL53 动作角色发布为 `actor/<role>.anim` 子资源，并在骨架上注册上半身遮罩。移动动画去掉平面根位移，位移仍由 NPC/座舱逻辑控制。模型缺少可用 AG2 数据时保留 legacy 播放回退。士兵和 Monarch 已实际构建、运行 AG2 图并渲染检查，开火/换弹时基础层仍保持 `tf2_move`，分别测得 59、89 个骨骼发生姿态变化。

这不等于完整复刻 TF2 动画：自动序列 blend-grid、authored aim matrix、flinch autolayer、IK、双角色处决和各机体核心专属动作仍需 MDL53 数据适配及逐动作验证。不能仅复制 Source 1 文件就宣称这些能力已完成。

## 网络与充能

检查时当前 `CanEnter` 已要求 `Networking.IsHost && !player.IsProxy`，远端客户端驾驶本来就未开放。因此不存在每个客户端都成功运行当前 `TryEmbark` 的情况。本轮继续保留这一范围，新增宿主同步的预占驾驶员和 `SeatSerial`，拒绝重复预占、同一驾驶员同时预占多个座舱、跨场景及代理 Titan 的进入，并在取消、完成、离开时推进序号。

`Titanfall2HostDamage` 在宿主实际调用命中/范围伤害时建立同步调用凭据，绑定受击者、宿主 Titan 的武器、攻击者及当前驾驶员关系。`CreditDamage` 不再从任意 `damage.Attacker` 回溯并授予充能。凭据不通过网络传输，最多消费一次，调用结束必定清理；嵌套伤害单独结算。充能取命中完成后的实际生命值与护盾损失，继续拒绝友伤、核心伤害与过量击杀收益。这不是对整个 Sandbox 客户端命中协议的反作弊改造。

## 验证与交付

本地证据目录：`artifacts/upstream-authority-20261125/`。仓库已有规则忽略 `Tests/`、`Validation/` 和构建输出，本轮新增回归用例保存在这些本地验证目录。

- 官方 MenuBuild 沙箱编译：Titanfall2 和全部七个上游挂载项目。
- 托管回归：230 项通过，30 项跳过；跳过项不计为通过。结果在 `Titanfall2/Tests/TestResults/upstream-authority-final.trx`。
- `actor-layers/report.json`：原版地图缩略图、独立纹理所有权、无 BSP 构建，以及士兵/Monarch 实际 AG2 骨骼求值。
- `titan-cockpit/report.json`：按键分发、重复/第二驾驶员预占拒绝、取消后 Serial 推进、出口碰撞、禁用/销毁时脱离与 HUD 回归。
- `cores-final/report.json`：最终伤害结算版本、伪造来源不充能、友伤/核心伤害/过量伤害、六机体核心回归，以及真实 SceneFile 回调下的双场景资源租约交接。

上述原生检查使用隔离运行时。未进行两台机器联机验收，也未进行整轮多图显存压力测试。最终 DLL 是代码编译检查产物；没有把它描述为已部署、已发布或完整内容打包。
