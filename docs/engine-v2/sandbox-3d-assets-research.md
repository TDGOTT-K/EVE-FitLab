# 战斗沙盒3D美术资源调研

日期：2026-09-20。范围：网上来源与技术路线调查、1GB预算。没有下载完整游戏资源、运行第三方提取工具或改动App；尚无已验证的完整模型转换样本。

## 本轮用户明确范围与新增来源

用户明确：目标是 FITlab 的 EVE 素材，1GB 仅限制美术素材合计，不限制整个应用安装体积。以下 App 体积记录仅为历史背景，不作为本次预算约束。以十进制 1,000,000,000 字节作为素材硬上限；生产素材与其运行缓存合计核算，制作工具不随 App 分发。

本轮重新核实官方/社区 README、网页与 GitHub Release 元数据，找到更具体的制作入口：

- https://github.com/carbonenginejs/tools-blender ：当前 Release v0.8.0，插件 ZIP 456,486 字节（约0.46MB），这是工具大小，不是船模包大小。README 声明可通过 SOF DNA 组装船体，按需取得几何、贴图、贴花和附件，不需要完整 EVE 安装；依赖其托管的组装服务。支持 GR2/CMF，要求 Blender 4.0+，作者称在5.0测试。尚未在本项目安装或运行验证。
- 材质限制：默认 Principled 材质只是近似；部分材质/涂装遮罩仅载入未连接，阵营色、花纹与热发光并不自动完整还原。应制作共享材质或离线烘焙再导出 GLB。README 仍列出0.7.2附件骨骼绑定问题，不能自行断言0.8.0已修复；CMF动画尚不能完整转为 Blender Actions。
- 插件要求使用者先接受 CCP 内容创作条款才下载。插件 MIT 许可与 EVE 美术使用许可分开；不能把工具能下载等同于允许在第三方应用中再分发。此次仅查资料，未代用户接受条款。
- https://github.com/carbonenginejs/format-gr2 ：已合并到 https://github.com/carbonenginejs/runtime 。旧 evegr2toobj 已弃用；https://github.com/cppctamber/carbonenginejs 当前仅为保留名称占位仓库，不应当作新转换器下载。
- 重新核实 ccpwgl2：原生资源渲染需要资源服务、着色器和按ID查询时的映射服务，DX11翻译仍标 alpha。适合作为还原效果的对照路线，不能将库本身当作离线全舰船包。

更新后的优先验证路线：CarbonEngineJS Blender Tools 按需取得六舰 → 检查材质并烘焙必要遮罩 → GLB + KTX2 → 本地清单、去重和预算检查。旧 TriExporter 仅保留为历史备选。六舰30–80MB、30–50船150–350MB仍是工程目标，不是已下载实测结果；当前不能承诺全舰船、全涂装在1GB内。

## 结论

EVE船模有现成资源链，不必从零建模。优先评估客户端原始资源+离线转换GLB/KTX2；CCPWGL2可用于原材质对照或直接渲染原型。不能将源码MIT许可证等同于CCP模型再分发许可。第一步应抽取/取得有明确使用依据的6艘样本，再测量完整依赖和转换效果，不能仅以仓库大小或单个几何文件大小推算全船库。

## 核实来源

1. CCP官方ccpwgl：https://github.com/ccpgames/ccpwgl
   README列舰船SOF、T3组合、炮台安装/射击、爆炸、多船性能演示。src/index.js配置的旧资源根为https://developers.eveonline.com/ccpwgl/assetpath/1097993/。本次对该根下一个船模路径请求返回404，不能当作仍可用的资源CDN保证；也不能据一个404断言所有资源都失效。它是渲染库，不是全量模型包。
2. 社区ccpwgl2：https://github.com/cppctamber/ccpwgl2
   本次README声称可以读取客户端资源及所有舰船，支持typeID/SOF DNA/graphicID；需要资源服务、着色器及ID映射服务。CCP服务器不提供所需CORS；DX11着色器翻译仍标alpha且有运行开销，行星功能README标为损坏。MIT适用于库源码，不转移CCP美术版权。直接集成未验证。
3. TriExporter：https://github.com/evem8/triexporter
   支持shared resource cache的历史分支，README明确不维护。最新release工具zip=2,594,245字节，这只是工具，不是船模容量。依赖历史FBX/DirectX SDK，不建议作为正式产品唯一生产管线。
4. 其他转换链：https://github.com/cppctamber/evegr2toobj 、https://github.com/cppctamber/evegr2tojson
   OBJ项目README已标Deprecated、替代CarbonEngineJS；旧https://github.com/cppctamber/eve-resource-proxy 也标2026年7月弃用。需实际评估新链，不把旧教程当现成可运行方案。
5. 在线参考：https://gamemodels3d.com/games/eveonline/
   当前页面列EVE23.02及多个舰种，包括凤凰https://gamemodels3d.com/games/eveonline/vehicles/19726 。可作外形/覆盖参考；本次未核实导出格式、完整贴图包字节量或再分发授权，不建议直接批量搬资源进产品。
6. 官方图片服务：https://developers.eveonline.com/docs/services/image-server/
   提供PNG图标/render等二维图像，适合缺模时回退，不能代替可旋转3D网格。官方文档允许直接使用CDN。

## 版权界限

https://support.eveonline.com/hc/en-us/articles/8563917741084-EVE-Online-Content-Creation-Terms-of-Use
允许在条件下使用CCP内容、要求非商业及来源声明；FAQ明确第三方App需另看开发者协议/第三方政策，且不额外提供高质量3D文件。
https://support.eveonline.com/hc/en-us/articles/8564030965660-Third-Party-Policies
另有客户端修改/反向工程/cache scraping限制。此处不能推断历史游戏缓存政策与shared资源提取完全等价，也不能据内容创作条款推断App中分发全套可提取模型已获得授权。商业收费/模型资产分发范围需单独明确；不要让第三方模型站的下载按钮代替来源授权。

## 体积实测与估算分开

实测当前rc.3 win-unpacked（逐文件逻辑字节求和，非磁盘簇占用）：
- App合计1,050,361,880字节=1050.4MB，约1001.7MiB，超过十进制1GB；虽小于1GiB，余量也不足。
- NEngine目录614.65MB：.NET209.37MB，CLI8.22MB，MCP17.31MB，数据artifacts210.05MB，contracts169.70MB。
- web34.80MB，Python backend15.44MB，其余主要为Electron等文件。
- 引擎目录>=100KB文件的内容重复量约10.35MB。不能承诺简单去重可腾出数百MB。
- 现有安装器208.4MiB（约218.5MB），不能用压缩安装包大小替代安装后大小。

以下是规划目标，不是已测船模包：
- 单个常用船体，约0.5–2MB几何+1–4MB贴图，目标2–6MB；旗舰可预留5–15MB，需样本转换后确认。
- 1K RGBA原始贴图4MiB，全mip约5.33MiB；4张=21.33MiB。相同1K纹理在8bpp块压缩+完整mip下每张约1.33MiB，4张约5.33MiB；2K像素量是1K的4倍。KTX2传输压缩大小取决于素材与编码，不应等同于GPU转码后占用。
- 艺术包限定1,000MB时建议工作预算700MB：船体网格/LOD120MB，贴图450MB，共享材质/环境50MB，特效40MB，索引及余量40MB；留下300MB增长余量。
- 初期6船样本资产目标30–80MB；30–50种常用船体目标150–350MB。均是待验证预算，不能推断覆盖全舰船及所有SKIN。
- 如果整个App安装后硬限1GB：应先审计contracts历史版本、CLI/MCP/runtime文件及数据可裁剪性，再决定资产配额；优先只内置这6船，无法保证在不瘦身的现有包中容纳。在线缓存也必须计入用户给定的总空间限制，不能用按需下载规避。

## 推荐管线

官方/获许可源资源 → 几何转换 → GLB(glTF2)+meshopt/Draco二选一 → 贴图降到常规512/1024、少数旗舰2048 → KTX2 → 按内容哈希去重共享 → manifest记录typeID/graphicID/源版本/许可依据/依赖/大小/hash。

T1/T2/势力变体能共享时共享几何，不能假定所有变体仅换色。基础涂装优先，不打包所有SKIN。远景LOD/实例复用减少显存和draw calls；磁盘、下载、显存应分别测量。激光/炮弹/电子战线条、喷焰与爆炸使用程序化几何+少量共享贴图，先不引入整套游戏特效资源。

正式开工前以凤凰、台风、巴戈龙、克勒斯、镰刀、黑鸦做6船资产探针：统计依赖闭包、转换后总字节、贴图观感、LOD、首屏时间、100/500实体显存与帧率，再冻结1GB预算。尚未选择或验证最终模型格式转换工具。
