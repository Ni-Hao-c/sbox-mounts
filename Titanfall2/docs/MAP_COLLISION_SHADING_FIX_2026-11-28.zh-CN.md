# 地图碰撞与物件着色修复验证（2026-11-28）

本轮修复了常驻物件碰撞反复重建的问题，并撤掉聚合路径中可能隐藏掩体、围栏和阴影的推断规则。已部署到 Steam s&box；着色改动没有测出稳定的明显 GPU 收益，不能据此宣称大幅提高 FPS 或已经达到引擎极限。

## 碰撞生命周期

原路径在主动销毁旧碰撞对象时保留了 `CollisionDestroyed` 回调。延迟执行的 `OnDestroy` 又将运行时标为 Dirty，下一次 Tick 触发全量重建，形成反馈循环。

`StaticPropCollisionRuntime` 现在在主动退休对象前解绑回调；意外丢失对象仍会发出修复请求。恢复过程保留原物理资源，只重建缺失的碰撞组，保留健康对象，并遵守现有每帧数量和时间预算。修复完成前不报告碰撞就绪。缓存加载后 render cell 为空的路径也覆盖在回归测试中。

## 战斗可见性

`StaticPropAggregate` 撤掉按小物件、无碰撞、cutout 属性推断远处可丢弃的规则，以及自行生成的 Far 简化网格。保留资源自带 LOD 和切换滞回，不用聚合距离或相机 PVS 硬隐藏整个聚合对象；正常视图裁剪仍交给引擎。

Shared、阴影及剩余特殊材质渲染对象不随上述相机 PVS/距离规则关闭。Cutout 阴影遵守资源标记。缓存版本升到 9，使旧缓存中被省略或简化的几何失效；第一次加载可能需要重新生成缓存。

这些修改覆盖本轮审计的静态物件聚合路径，不等于已完成全部地图、全部材质及真实 PvP 对战验收。

## 着色改动与性能边界

新增 `F_T2_FAST_NORMAL` 静态特性：利用法线归一化抵消切线框架中的行列式倒数幅度，保留镜像 UV 的符号，并相应调整退化判断阈值。参考实现保留，可在同一场景内切换比较。没有删除 alpha 测试或材质功能。

200,000 组 float32 随机比较包含 99,868 组镜像 UV；最大法线向量误差约 0.00001963，退化分支不一致和非有限结果均为 0。

下表是隔离渲染测试的 GPU 中位数（毫秒），按参考 → 优化 → 参考顺序采集，每段预热 120 帧、记录 240 帧：

| 地图/视点 | 参考前 | 优化 | 参考后 |
| --- | ---: | ---: | ---: |
| sp_training / dense0 | 8.78 | 8.40 | 6.81 |
| sp_training / dense1 | 8.08 | 8.04 | 8.10 |
| sp_crashsite / dense0 | 9.32 | 9.29 | 9.36 |
| sp_crashsite / dense1 | 9.62 | 9.55 | 9.55 |

训练图第一个视点存在明显启动漂移，其余差异接近噪声，不能建立稳定收益结论。恢复先前被错误隐藏的几何也可能增加渲染工作，不能把旧的不完整画面作为等画质性能基线。

测试加载 `.bsp.world.scene`，使用 ForceFlush 串行化 CPU/GPU，纹理质量在测试程序中回退为 1024；结果不是实际游戏 FPS。截图差异与重复参考采样的时序差异相当，不是逐像素等价证明；尚未做完整游戏阴影、AI 和 PvP 实战验收。

## 验证与证据

证据根目录：`D:\sbox\_codex-build\tf2-collision-shading-20261128`。

- 官方 MenuBuild 沙箱编译通过：`release-build.log`。
- 官方 ShaderCompiler 编译通过：`shaders-v2.log`。
- 定向测试 16 项通过，0 失败、0 跳过：`tests-current-final.log`、`results/current-final.trx`。
- 最终发布 DLL 的原生回归通过：`safety-release-results.json`。覆盖四轮主动清理不重新置脏、意外丢失通知、保留健康碰撞并恢复缺失对象，以及远距离/PVS 变化下掩体、围栏和阴影启用状态。
- `sp_training` 缓存运行完成：`training-v3/collision-end.json`，6 个常驻碰撞体，就绪且 Dirty=false，进程峰值工作集约 6.02 GB。
- `sp_crashsite` 缓存运行完成：`crashsite-final/collision-end.json`，15 个常驻碰撞体，就绪且 Dirty=false，完整加载及测试约 92 秒，峰值工作集约 14.20 GB，没有触发看门狗终止。
- 法线数学比较：`normal-math-check.json`；截图比较：`final-image-diff.json`。两张地图 dense0 优化截图已人工查看，未见明显渲染破损。

两张地图测试使用修复后的相同碰撞/着色代码；最终发布包还包含工作区同期其他任务的更新，并再次通过官方编译与原生安全回归。部署后未启动正式游戏加载地图，因此安装文件一致性与完整游戏验收分别记录。

## 部署

2026-11-28 11:22:18（Asia/Shanghai）部署到 `D:\Game\steams\steamapps\common\sbox\mounts\Titanfall2`，替换 DLL、default/cutout 的 shader 和 shader_c、normals.hlsl，共 6 个文件。部署后再次核对，6 个安装文件 SHA256 均与记录一致。

- DLL SHA256：`E12109C109ED8EED202BE0798CABD01AA5890EDC1A6A9125BCAB7524AA29D1BD`
- DLL MVID：`e857e626-b324-4a0e-8d78-f3817bcd8584`
- 部署清单：`D:\sbox\_codex-build\tf2-collision-shading-20261128\deployment-final.json`
- 备份：`D:\Game\steams\steamapps\common\sbox\mount-backups\Titanfall2-before-collision-shading-20261128-112218`

重新启动 s&box 并加载地图即可使用本版。本轮未提交 Git，未清理或覆盖工作区其他任务的修改。
