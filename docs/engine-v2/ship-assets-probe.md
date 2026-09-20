# 六舰素材样本进度

2026-09-20。预算：美术素材合计不超过1,000,000,000字节，工具和应用程序不计入。六舰获取、静态舰体转换、KTX2/meshopt压缩及浏览器加载已完成。此为素材样本，不是正式战斗UI接入。

## 最终结果

用户明确回复“接受”后，调用原插件 `carbon.eve_resource_accept_creator_terms(agree=True)` 成功；接受记录在 `output/ship-assets-probe/acceptance.json`。下方“当前停点”为早期记录，已解除。

生产样本目录 `output/ship-assets-probe/pack`，包括12个GLB、manifest和NOTICE，总计 **95,963,682字节（95.96MB）**。近景六舰合计48,160,196字节，当前LOD1独立GLB重复包含贴图，后续可改为共享贴图进一步节省空间。

| 舰船 | 原始依赖MB（各船独立计） | LOD0 MB | LOD0三角形 | LOD1三角形 |
|---|---:|---:|---:|---:|
| 黑鸦 | 19.51 | 7.42 | 5,830 | 3,941 |
| 凤凰 | 72.34 | 7.06 | 24,140 | 19,875 |
| 台风 | 26.70 | 7.91 | 12,090 | 9,006 |
| 巴戈龙 | 19.49 | 8.33 | 6,367 | 2,023 |
| 克勒斯 | 32.52 | 7.54 | 7,988 | 4,898 |
| 镰刀 | 149.34 | 9.89 | 75,716 | 51,410 |

资源版本3503375。6船源payload按SHA256去重为260,351,011字节；制作缓存445,917,249字节（还含索引、解码结果等）。源缓存不随样本包分发。完整字节账在 `output/ship-assets-probe/summary.json`，单船源路径/hash见 `ships/<id>/report.json`。

全部采用2K非重叠纹理图集，xatlas展开，烘焙Albedo/Roughness/Emission/Normal，静态姿态导出GLB；KTX2采用UASTC level2、RDO0.75、mipmap、Zstd；网格用meshopt。修正了直接用EVE重叠/越界UV烘焙会导致镰刀帆板贴错的问题。凤凰导出的一个零切线以法线正交单位向量修复并记录 `repairedTangents`。源场景的护盾效果不支持等组装提示不等同于网络缺失，六船fetch的problems均为空。

## 验证及限制

- 六个标准GLB经修正后glTF Validator均0错误、0警告；12个压缩GLB经NodeIO/meshopt解码回读成功，文件SHA256与清单逐项一致。
- 独立Three.js预览实际加载12个GLB成功、无pageerror；源材质与烘焙材质渲染图、浏览器截图均已检查。浏览器控制台有环境贴图shader浮点精度warning，未发现模型加载错误。
- 1440×960、本机浏览器、热缓存、同型实例复用：黑鸦LOD1 100/500艘约233/110 FPS；镰刀LOD1 100/500艘约39/9 FPS。只是短时开发测量，不是完整战斗或其他设备保证。原先加载截图显示数百毫秒，最终自动回测为热缓存0ms，不冒称冷启动统计。
- 当前LOD简化受误差边界限制，没有达到所有舰船25%的目标；尤其镰刀远景仍51,410三角形。正式战场需要单独更低精度LOD/小目标图标替代和视锥裁剪。
- 2K四纹理在常见8bpp块压缩并含mip时约21.3MiB/模型，实际设备转码格式不同会变化；尚未测整场景显存，Three.js纹理数量不是显存字节。
- 只含静态舰体，不含独立贴花、公司横幅、sprites、炮塔、尾焰、护盾命中与动态变形。烘焙材质不等同完整Carbon shader；源场景保留供继续制作。
- 本地样本可直接供UI试装；未修改现有战斗UI、引擎或发布应用，尚未宣称完成第三方App资产再分发范围核实。

## 查看和复现

预览：在 `output/ship-assets-probe` 运行 `python -m http.server 8766 --bind 127.0.0.1`，打开 `http://127.0.0.1:8766/`。可旋转、缩放，切换六舰/LOD及1/100/500艘。总览图 `six-ships-preview.jpg`；浏览器证据 `output/playwright/ship-assets`，结构化验收 `output/ship-assets-probe/browser-qa.json`。

脚本：`probe-ship-assets.py`取得并组装，`export-ship-sample.py`烘焙导出，`build-ship-pack.mjs`压缩及验证，`report-ship-assets.py`统计与总览。Blender脚本经 `ship-assets-blender.ps1 -Script <绝对路径>` 运行，以进程环境变量SHIP_PROBE_ID选择舰船。插件/依赖在隔离tools和blender-scripts目录；xatlas0.0.11、KTX4.4.2、glTF Transform4.5.0、Three0.186.0。KTX安装器静默安装到tools/ktx-portable，工具不随美术包交付。

## 初始环境准备记录（历史）

## 已实际验证

- 本机 Blender：`D:/Blender/blender5.1.2/blender.exe`，启动输出版本5.1.2。
- 官方作者 GitHub Release：CarbonEngineJS Blender Tools v0.8.0，ZIP 456,486字节，SHA256 `e2bc06fc2c0a2b780e0fc79d2153d632abad249ad7f739af82b8518108c419dd`。
- 在 `output/ship-assets-probe/blender-config`、`blender-scripts` 独立目录安装并启用成功，未改用户默认 Blender 配置。没有改游戏客户端、FitLab运行代码或引擎。
- 实际运行 `scripts/ship-assets-blender.ps1 -Prepare`，退出码0；预检报告 `output/ship-assets-probe/preflight.json`。Blender 对四个随包reader库提示缺少bl_info，但主插件成功注册。
- 从现有沙盒request生成六舰清单：Phoenix 19726、Typhoon 644、Bhaalgorn 17920、Keres 11174、Scythe 631、Crow 11176。DNA须从真实映射取得，当前留空。

## 当前停点

插件 `carbon_eve_resources/addon.py` 的 Build DNA 操作在 poll 中检查 `_context_terms_accepted`。接受操作会保存条款版本及UTC时间。当前预检 `termsAccepted=false`，未代用户接受、未绕过检查、未取模型。

条款：https://support.eveonline.com/hc/en-us/articles/8563917741084-EVE-Online-Content-Creation-Terms-of-Use 。本轮已打开官方页面，内容要求非商业使用、来源/非官方声明等，FAQ另外指向第三方开发者政策。插件保存的接受标识为 `eve-content-creation-terms-2024-08-07`，实际使用须以实时官方页面为准。接受此制作工具条款不等于已核实应用内再分发范围。

## 接续

用户确认接受条款后，通过原插件接受操作记录确认；先做黑鸦小型船样本，再做凤凰旗舰，最后扩至六舰。获取原始依赖并记录源版本、路径、字节与hash；检查材质，生成GLB及预览，实测纹理压缩前后容量。1GB上限计入要交付的美术与运行缓存；源素材/中间blend/预览另列制作占用，不混称发行包大小。

手工进入同一独立环境：`powershell -NoProfile -File scripts/ship-assets-blender.ps1`；在插件设置中可查看并接受条款。此命令仅提供给需要交互的用户，不由准备脚本自动弹窗。
