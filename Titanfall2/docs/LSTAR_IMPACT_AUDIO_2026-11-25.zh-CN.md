# L-STAR 原版 impact 声音精确映射与 Miles 引用

本轮处理 `weapon-presentation-borealis3.json` 中 10 类缺失声音。未部署、未启动引擎或 GPU 测试，也未操作用户正在测试的 Steam 进程。

## 原版文件证据

从用户安装的 Titanfall 2 只读提取：

- `scripts/weapons/mp_weapon_lstar.txt:47` 指定 `impact_effect_table = lstar`。SHA256：`68F6FFCD329BA4442E38BFCC803443E47D409055D7CE2C7DE51783372077D3BF`。
- `scripts/impacts/lstar.txt:75-110` 的 `Sound` 区段提供本轮 10 个 dotted 事件名。SHA256：`C85D2CAFD07DB7593EBF818950C0683B4A699C02CA4253093DF13C704032F5AE`。
- `r2/sound/general.mbnk` 的精确事件索引提供对应 underscore 名称。SHA256：`B5C3A149F728172DD9860472DD1030757B88D3DAB1B530480019566B0988220E`。

完整提取文件、脚本和 manifest 位于 `artifacts/lstar-audio-20261125/`。`exact-impact-events.json` 保留事件动作偏移、type 8 字节、引用目标、展开后的全部层和声音源。

## 有界名称映射

下表左侧均追加 `.BulletImpact_3P_vs_3P`；右侧均追加 `_bulletimpact_3p_vs_3p`。这里只增加这 10 个经过核对的映射，没有自动替换标点或模糊查找规则。

| 原 impact 名称前缀 | Miles 事件前缀 | 原库展开层数 |
| --- | --- | ---: |
| Concrete.Heavy | concrete_heavy | 4 |
| Dirt.Heavy | dirt_heavy | 3 |
| Titan.AmpedXO16 | titan_ampedxo16 | 2 |
| Flesh.LSTAR | flesh_lstar | 1 |
| MetalGrate.Heavy | metalgrate_heavy | 3 |
| Grass.Heavy | grass_heavy | 3 |
| Mud.Heavy | mud_heavy | 4 |
| Default.Heavy | default_heavy | 4 |
| SolidMetal.Heavy | solidmetal_heavy | 3 |
| Sand.Heavy | sand_heavy | 4 |

`Titan.AmpedXO16` 来自 L-STAR 原 impact 表的 `E`，因此保留它，没有自行换成名称更相似的 Titan LSTAR 录音。

## type 8 引用是额外根因

单加名称映射仍会使 Default、Grass、Mud、Sand 失败：原解析器只支持 type 0 播放动作。

| 事件 | type 8 文件偏移 | 原始 12 字节 | bank 内精确引用 |
| --- | --- | --- | --- |
| default_heavy… | `0x125f488` | `1803000000000000995D0300` | `/general/concrete_heavy_bulletimpact_3p_vs_3p` |
| grass_heavy… | `0x156cdf0` | `180300000000000061331600` | `/general/dirt_heavy_bulletimpact_3p_vs_3p` |
| mud_heavy… | `0x15fdf24` | `180300000000000061331600` | `/general/dirt_heavy_bulletimpact_3p_vs_3p` |
| sand_heavy… | `0x170c060` | `180300000000000061331600` | `/general/dirt_heavy_bulletimpact_3p_vs_3p` |

首字节低 4 位为 type 8，`0x10` 表示事件动作列表结束；第二字节为 3 个 DWORD；`+4` 为零设置，`+8` 为字符串表偏移。Mud/Sand 在这个引用动作之前各有一个 176 字节 type 0 层，因此结果必须是自己的 1 层加 Dirt 的 3 层，不能把整个事件替成 Dirt。

解析器现在只接受已证实的同 bank、12 字节、`+4 == 0` 引用形式。保留顺序与重复引用，以当前递归路径检测环；最大引用深度 16，每事件最多 64 个动作，展开最多 256 层。未知设置、跨 bank 或找不到精确目标时整个事件失败，不播放部分层。

## 离线验证

独立 `net10.0` probe 直接链接本轮 `MilesSoundEvents.cs`，不引用或启动 s&box 引擎：

```powershell
dotnet run --project artifacts/lstar-audio-20261125/probe/Probe.csproj
```

已通过：

- 10 个精确映射全部解析，层数与上表一致，全部叶声音源在原 bank 的 source table 中存在。
- 原 CAR 首枪、CAR loop、Defender energy impact 共 3 个已有事件保持可解析。
- 错拼名称拒绝，非零引用设置拒绝。
- 在内存副本构造引用环，整个事件失败且不返回部分层。
- 在内存副本构造两次独立 Dirt 引用，保留全部 6 层，未因全局 visited 去重。

另在本地 `Tests/MilesSoundEventTests.cs` 增加原 impact 组合及引用环/重复分支回归用例；该目录受现有 `.gitignore` 忽略。本轮未执行整个测试项目、PCM 解码或实际播放，因此没有宣称听感/完整预热运行验收通过。
