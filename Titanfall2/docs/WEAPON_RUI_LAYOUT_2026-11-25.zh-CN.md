# 枪身弹药数字：按零售 RUI 运行代码恢复布局

目标是 Sandbox 中挂载枪械的枪身弹药数字。此前通用居中数字虽然显示了正确弹量，位置、比例和字体并未复刻原界面。用户指出偏位后，本批直接检查本机零售二进制与资源，未以截图目测偏移作为布局来源。

## 确认的三个坐标错误

1. MDL53 每个 face 的 32 字节是 `U[4] + V[4]`，不是两个 UV 起止点加缩放。原 `engine.dll!0x1800FF440` 由 map 的三个顶点及对应 UV 求仿射平面。
2. R201、CAR lower 的原 RUI 画布都是 256×256。实体屏幕只显示其中约上半高的 UV 窗口，不能把画布改成实体屏幕的长宽比，也不能把完整画布均匀拉进窗口。
3. 官方 WorldPanel shader 在乘面板空间矩阵前，还会对像素 Y 取反。只反演 `BuildPanelToObjectMatrix()` 中的 `Rotation.From(0,90,90)` 会遗漏这一层，造成字形上下镜像；此前角点检查也重复了这个不完整的变换。

| 原界面 | 完整 UV 高度（骨骼局部单位） | 可视 V 区间 | 文本 frame | 锚点（原画布像素） |
|---|---:|---|---:|---|
| `ui/r101_ammo_counter` | 0.820265 | -0.000235…0.518610 | 3 | 69.12, 56.32 |
| `ui/car_smg_ammo_counter` | 0.593118 | -0.001069…0.506248 | 6 | 128, 64 |

两者文字 pivot 都是 `(0.5, 0.5)`。原版位置计算是 `文字框左上角 = 锚点 - 真实测量尺寸 × pivot`。R201 的 X 锚点为画布宽度的 0.27，CAR 为 0.5，不能共用通用居中规则。

原仿射解使用 A、B、C 三点：

```
du1=U1-U0; du2=U2-U1; dv1=V1-V0; dv2=V2-V1;
det=du1*dv2-dv1*du2;
Uaxis=(dv2*(B-A)-dv1*(C-B))/det;
Vaxis=(du1*(C-B)-du2*(B-A))/det;
originUV0=A-Uaxis*U0-Vaxis*V0;
```

R201 作者数据有微小扭曲，第四点不适合强制四角完全仿射拟合。WorldPanel 使用正交 TRS，因此保留有界的小几何近似；不能把其角点误差误称为对原版完整 UI 映射的独立证明。

## WorldPanel 像素轴与方向验收

安装目录 `D:/Game/steams/steamapps/common/sbox/core/shaders/ui_cssbox_batched.shader` 第 263 行先执行 `o.vPositionPs.y *= -1.0`，第 265 行才乘 `g_matWorldPanel`。同一安装的 `ui/vertex.hlsl` 第 60 行也先反射 Y，再在第 64–66 行应用世界变换。这是官方实际 shader 的坐标约定，不是原 R201 模型特例。

因此，面板像素向下轴应为 `nativeRotation * (0,-1,0)`，而不是 `nativeRotation * (0,1,0)`；角点验证也必须把 `(pixelX,-pixelY,0)` 送入该旋转。独立轴缩放取绝对值，不受这个符号变化影响。原 atlas 和 CPU 覆盖率预览中的非对称数字 4 本身朝向正确；不应翻转 glyph 位图或为 R201 单独添加翻转。

用户提供的 R201“24”截图中，字序仍为 2、4，但字形上下镜像。此前 CAR“30”的截图不足以证明方向正确，因为 3 和 0 接近上下对称。此前基于“30”或同源角点公式作出的方向验收应撤回。

方向验收在两把枪上分别捕获非对称数字“24”，同时检查字序、字形上下方向、原画布锚点和窗口裁剪；不能只检查是否出现正确弹量、预热是否 Ready 或角点误差是否小。保留 CAR 原“30”的回归对照，但不以它替代非对称样本。最终原生捕获 `artifacts/weapon-hud-20261125/retail-async/{rspn101,car}-weapon-hud-24.png` 已由主代理逐图检查，两枪的“24”均正向可读。对应 `runtime-results.json` 为 `success=true`，MVID 为 `b31f7207-2f93-4cff-9d5a-025a84e5315d`；此结论仅覆盖所捕获的两枪与布局。

所核查的官方源文件 SHA256：`ui_cssbox_batched.shader` 为 `62b7ee7625cb6e17d3e0131e4f60dc190772bfadf954bfaa32f8d76a2b323ba3`；`ui/vertex.hlsl` 为 `80e6e9c55e975975b7ae42987f0ae3a13e6726ca0e0c831d1c9856ed634560dc`。这些源文件证明坐标公式，最终运行结果仍需以修正后的原生捕获单独验证。

## 原字型、文字度量与绘制

两 lower 的字体样式都是原 font ID 9，即 `MetronicProRegularFont`，默认字号参数 120；布局中的字号必须在原 256×256 画布上解释。原资源还提供归一化字形 advance、基线、plane bounds、字距及 SDF atlas。字符宽度不同，例如数字 1 的 advance 为 0.368421，0 为 0.578947，均再乘字号。字距方向是“当前 glyph 查前一个 Unicode”。

单样式数字行的原测量框高度为字号，基线为字号乘字体 ascent（本字体 48/57），不是 glyph 墨迹框高度。字体 ID、字号、锚点和 pivot 均从资源读出；字体不通过内部 FontManager 注册或强制回落 Roboto。

编译 `ui(11).dll` 的 `r101_ammo_counter`（RVA 0x74350）与 `car_smg_ammo_counter`（RVA 0x11B40）使用 `%i` 格式化，不补零。比例 >=0.6、>0.33、其余三个分支的基础颜色分别对应 sRGB `(135,235,255)`、`(255,128,64)`、`(255,72,64)`。完整低弹量闪烁、logo、panel bleed 等图层并不等同于弹药数字自身，不能猜测补画。

字体 atlas 是 4096×4160 的单通道 SDF，不能直接当透明字图贴上。原 DXBC font 分支在默认 weight 下的边界是 200/255，而非 0.5。数字位图适配使用该真实边界生成覆盖率；CPU 覆盖率、最终采样与原五点屏幕导数抗锯齿仍有差异，不宣称像素一致。

## 证据链与限制

- `client!0x1803D1C80`：WeaponData UiData 的 ui/mesh/Args 绑定。
- `client!0x18010EEE0`：根据 MDL map 构造 RUI mesh topology，传原 mesh 与骨骼变换。
- `client!0x180191F30` → VEngineClient013 slot 270 → `engine!0x180054B50`：取得 23 项 RUI API。
- `engine!0x1800FF440`：face 仿射平面。
- compiled callbacks `engine!0x1805F4320` 的 +0x20 → `0x1800F6980`：文字度量；+0x30 → `0x1800F56B0`：布局字节码解释。编译代码传入的 54/74 是字节码终点，不能当字号或绘制数量。
- `engine!0x1800F5840`：选择文字 frame；`0x1800F4460`：root 相对 anchor/pivot 布局。

源二进制 SHA256：client `002b36487fec7c98882929fbccdb506d0146bbf3270cbb964a4d21c5edf7eebc`；engine `58eb1a1b44b30275bdd21368de264d856bc310d37d93a0f76f111f0026913487`；ui(11) `ac211cf1b9452e998db88b6614feae7a1e13c6d37c84b247b00f5e4d1a74f135`。

原始导出、独立计算与复现脚本保存在 `artifacts/retail-rui-layout-20261125/`。CAR face 原数值和非法顶点 map 的定向回归 2/2 通过，但这不验证 WorldPanel shader 的最终像素方向。当前受限解析器只执行已证实的 root 相对文字 frame，其他布局不凭空套用默认偏移；这不是完整 RUI VM 或所有枪械屏幕的复刻。最终视觉状态须另行记录包含非对称数字的修正后原生截图和运行报告，不能沿用此前的“30”方向结论。
