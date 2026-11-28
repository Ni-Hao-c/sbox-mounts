# 铁御技能：IDA 当前模块只读核对（2026-11-28）

## 结论

当前 MCP 对应的是 Titanfall 2 的 `engine.dll`，不是 `client.dll` 或 `server.dll`。本轮只读分析证明该模块加载客户端预测和服务端玩法接口，但**没有取得 Grapple、Stim 速度映射或回血时序的原生函数证据**。因此没有据此修改技能运动、回血参数，也不能将现有适配实现称作原版原生逻辑。

本轮未切换 IDA 数据库，未重命名、改类型、写注释、patch 或写入游戏二进制。

## 当前数据库身份

- MCP `server_health`：`status=ok`，自动分析完成，Hex-Rays 可用。
- 输入：`D:\Game\steams\steamapps\common\Titanfall2\bin\x64_retail\engine.dll`。
- IDB：同目录 `engine.dll.i64`。
- 64 位，image base `0x180000000`。
- SHA-256：`58eb1a1b44b30275bdd21368de264d856bc310d37d93a0f76f111f0026913487`。
- 34,165 个函数、22,996 条缓存字符串。

## 直接读取的模块边界证据

| 函数地址 | 实际反编译行为 | 意义 |
| --- | --- | --- |
| `0x180050010` | `0x180050046` 以 `client.dll` 为实参调用模块加载接口；`0x18005008A` 向获得的 factory 请求 `VClient018` | 客户端实现来自独立 DLL |
| `0x18004FDB0` | `0x18004FE02` 从客户端 factory 请求 `VClientPrediction001`，另请求 `VClientEntityList003` | 预测与实体列表接口来自客户端模块 |
| `0x1801C1690` | `0x1801C16C7` 加载传入模块，`0x1801C1787` 请求 `ServerGameDLL005`；随后请求 `ServerGameEnts002`、`ServerGameClients004/003` | 服务端玩法接口来自独立模块 |

字符串及交叉引用定位：

- `client.dll`：`0x1805CAF08` → `0x180050030`。
- `VClient018`：`0x1805CAF18` → `0x18005007F`。
- `VClientPrediction001`：`0x1805CB188` → `0x18004FDF7`。
- `ServerGameDLL005`：`0x180605DE0` → `0x1801C177A`。
- `server.dll`：`0x180605E98` → `0x1801C167C`。

这些证据说明接口边界，不等于已经确认某个虚函数是技能实现。

## 技能检索结果与替代模块定位

在当前 IDA 字符串索引搜索 `grapple|stimpack|speedboost|stim_visual|speed_boost|health.*regen|regen.*health|PhaseShift|phase_shift`，得到 0 条。缺少字符串命中不能证明整个模块绝无相关计算，但不足以支持继续猜测匿名函数为技能逻辑。

进一步仅对磁盘 DLL 读取 ASCII 字符串和哈希，没有切换 IDA。下表偏移是**文件偏移，不是虚拟地址**：

| 文件 | SHA-256 | 线索 |
| --- | --- | --- |
| `bin\x64_retail\client.dll` | `002b36487fec7c98882929fbccdb506d0146bbf3270cbb964a4d21c5edf7eebc` | `grapple_detachLengthMin` @ `0x8E4F30`；`GetSuitGrapplePower` @ `0x8CC6B8`；`stim_visual_effect` @ `0x91BE18` |
| 游戏根目录 `server.dll` | `a5ca3a25c8ae56952a26141b0f6cdcb6c19086c8c39013f7cb345d5d723661af` | `grapple_detachLengthMin` @ `0x94BC38`；`GetSuitGrapplePower` @ `0x9584E8`；`stim_visual_effect` @ `0x8D04A8`；`speed_boost` @ `0x8D0498` |

两个 PE 的 Machine 均为 `0x8664`。本机 `server.dll` 在游戏根目录，**不在** `bin\x64_retail`。

`movement_speedboost_extraScale` 在上述两个磁盘 DLL 中均未找到完整 ASCII 字符串。`sh_stim.gnut` 对它的注释不能独立证明当前二进制仍采用该 ConVar 名称或对应公式。

## 后续原生还原需要的证据

1. 在客户端或服务端数据库中，从 `GetSuitGrapplePower` 注册表和 `grapple_detachLengthMin` 字段读取交叉引用定位真实执行函数，区分脚本绑定、字段注册和每帧运动更新。
2. 从 `speed_boost` 的枚举/状态注册继续追踪实际速度运算，确认 severity 合成和到期衰减；不能直接从 `0.4` 注释推导最终速度倍率。
3. 在服务端追踪玩家生命值写入与恢复计时，确认 Stim 是否修改常规恢复延迟或速率；不能用音效时长替代回血证据。

以上尚未完成；此次交付的脚本参数、原版资源映射和运行时测试与这些原生逻辑研究的验收层级分开记录。
