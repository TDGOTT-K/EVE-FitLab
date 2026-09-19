# 战斗沙盒预览（NUI-99）

2026-09-20。用户授权范围：简陋二维战斗演示、官方舰船图标、轻量武器/电子战动画，吸引用户了解下一版本；不是正式沙盒编辑器。

## 入口与文件隔离

index.html只增加sandbox-preview-entry.js模块引用，入口动态放在装配库右侧。独立iframe浮层加载sandbox-preview.html，返回或选择其他主导航时清理，不改当前装配/草稿。所有样式、脚本和回放数据均以sandbox-preview命名；未修改i18n会话正在处理的app.js、style.css、server.py或语言包。预览独立文案目前有简中/繁中/英语/日文，其他语言回退英语；由主页面lang传入，后续可由本地化任务合入正式资源。

## 实际内容

4艘官方SDE配装，裂谷级炮击/激光、狞獾级重导弹、裂谷级网子/目标标记；两阵营复用公开tick策略，固定种子17，40秒。通过当前0.210.6-ui.repair1引擎CLI sde-battle组装及sde-battle-verify核验，逐秒step采样。展示实际位置、三层HP和电容；状态按整数秒取记录，位置做视觉插值。导弹从真实发射到命中时间作视觉插值，炮击/激光按TurretResolved记录绘制；电子战只在记录的有效区间显示。MISS同样会显示射击光束，不把光束当命中证据。

回放可暂停、重播、拖动时间、调速；缩放/平移/恢复视角、点选舰船、只读脚本。当前没有运行用户脚本、编辑配装场景或完整时空战斗功能。界面明确是实验记录预览，非完整实服模型。官方图标通过images.evetech.net实时加载，网络不可用时暂用阵营标记。

## 数据与复现

- 发布数据：sandbox-preview-data.js（约91KB）。为适配现有静态资源白名单采用JS模块，不改服务接口。
- 原输入/源码：data/sandbox-preview/request.json、policy.js。
- 重建：python scripts/build-sandbox-preview.py --engine D:/AI/GPT6/N号引擎-维护-0.201-r45 --evidence <新的空目录路径>
- 本轮证据：output/sandbox-preview-001；含assembly、verified、40个检查点、连续运行及verification.json。
- 827条完整事件，122次炮台结算，20次导弹发射及命中，16次电子战效果开始；无ControllerFault。
- 连续40秒与逐秒恢复最终stateHash及全部事件逐项一致。

## 验证

78个浏览器模块绑定/导入检查通过，新增JS语法检查及生成器Python编译检查通过。隔离浏览器1045×844及800×650验收：官方图标加载、播放/暂停、重播从零开始、时间轴、舰船选择、脚本展开、导航入口打开及返回不改变装配地址；无pageerror、窄窗口无横向溢出。截图output/sandbox-preview-001/final-preview.png。未打包、未修改引擎源码、未发布。
