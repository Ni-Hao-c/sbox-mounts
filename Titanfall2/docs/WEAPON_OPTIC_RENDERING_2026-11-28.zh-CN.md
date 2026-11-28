# 武器光学 RUI 渲染修复（2026-11-28）

用户截图中的大型右侧枪身、ATTENTION 字样、横向红色刻度和数字 1，与 `mp_weapon_arc_launcher`（Thunderbolt）原生渲染吻合。R201 使用 `mp_weapon_rspn101`，本次验证选择其 HCOG 配件。

## 确认的三个原因

- 原版 `ui(11).dll` 输出线性 HDR 颜色，而 s&box UI shader 把 Painter 的颜色参数视为 sRGB 再解码。直接传入线性红色 20 会变成约 1174。修复在 Painter 入口进行 linear→sRGB 编码，保留原始 HDR 强度和 alpha，不随意降低原版参数。
- 原图被图集裁掉的透明留白未恢复。`uimg` 头部偏移 16 指向每图 32 字节变换，第二个 float4 包含原始 UV→裁图 UV 的偏移和缩放。雷电炮 `aog_reticle_windage_bright` 的裁图是 511×49，应放回 512×512 画布的 (0,239)；HCOG `front_col_trans` 的 99×100 裁图应放回 256×256 的 (80,80)。新增 `rui_full/<原图名>.vtex` 路径恢复这些留白，并避开同名普通 TXTR 的注册优先级；普通 HUD 图标和地图预览路径不变。
- 实机日志显示 SCSS 缺失，旧面板根节点会收缩至宽度 0。同时 `WorldPanel` 默认 2 倍 CSS 比例，把 256 像素画布和 -128 偏移变成 512 和 -256。现在由代码设置根节点宽高及裁切，并只对 CSS 尺寸使用 `ScaleFromScreen`。Painter 和 MDL UV 均保持原始像素坐标。

## 验证

- 独立官方 `MenuBuild.exe` 编译通过。
- 37 项定向测试通过，覆盖颜色往返、真实裁图坐标、无裁切轴、越界输入及既有瞄具绑定。
- 原生夹具覆盖 Alternator HCOG、R201 HCOG、Thunderbolt 原生瞄具，以及用于排除枪型误判的 Charge Rifle。报告 `success=true`，MVID 为 `70ff6cc4-8f86-4903-8216-5e17ebfd42c4`。
- 实际检查 PNG：R201 红色准星和 13/12 数字可见；Thunderbolt 恢复细窄横向刻度，数字 1 可见。面板根宽度、256 像素画布、UV 偏移、骨骼对齐、射击后弹量和缺图回退均通过断言。
- 报告及截图：`D:\sbox\_codex-build\optic-regression-20261127\native\`。测试独立进程已退出，未部署、未重启用户游戏。

## 部署与范围

部署仍须包含 `Assets`，尤其 `Assets/gameplay/weapons/ui/weaponammodisplay.panel.cs.scss` 和所需 shader。根布局已不依赖此 SCSS，但这不替代整个挂载包的资源交付。

本次验证针对报告的缺显示、变形和异常泛光。原版 RUI 的程序化噪声仍采用既有静态中间值，不宣称与原游戏每一帧的闪烁效果完全一致。
