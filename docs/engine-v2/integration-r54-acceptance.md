# r54独立接入记录

2026-09-19；新引擎D:/AI/GPT6/N号引擎-维护-0.201-r45，实际0.210.0/r54、SDE3503375、静态v59。

## 环境与来源

启动scripts/start-r54.ps1，地址http://127.0.0.1:56454/#library；原生状态output/integration-r54-ui-001/native，UI库output/integration-r54-ui-001/state，存储配置output/integration-r54-ui-001/storage.json。旧56432状态不迁移、不删除；新端口隔离浏览器草稿。新引擎源码未修改。交付UI-BASELINE声明LOCAL009已包含static-inspector-v2，不能将r44旧补丁覆盖回去。

## 已接线

- 原生fighters显式写location=tube/reserve、tubeIndex，管内待命与备用不再合并；现有保存、预览、编辑命令共用此转换。
- 分类显示读取fighterOccupancy.loadedClasses的totalCount/limit，提示改为“管内中队，包含待命”；保留旧资源部署口径，不混用。
- 舰载机区域由当前原生fighterBay驱动，未知能力保留等待提示；无人机区域不再与舰载机互斥，有库存时保留，未知投影不当不存在。
- 武器菜单按原生贡献身份关联能力，取消三个duration属性的白名单，白蚁炸弹不再因2349被过滤。不可用输出仍不能强开。
- 重型鱼雷和深渊方向使用原有接口直接受益于引擎修复。

## 验证

22项test_nengine_output、test_native_edit_commands、test_nengine_integration通过；7项test_fit_package、test_nengine_mutations通过；65模块检查通过。

scripts/verify-r54-integration.py实跑FitLab→公开MCP：独眼巨人II loadedCycleDps可用，白蚁默认requires_policy/显式政策可用，4队待命轻型loaded=4/deployed=0且有FIGHTER_LOADED_LIMIT_EXCEEDED，1255方向true，指挥航母catalog_item有hullConfiguration。结果output/integration-r54-ui-001/verification.json。技能为空的诊断样本不作为游戏合法装配；验证聚焦相应接口回执。既有输出专项含CLI对照，不宣称此脚本每项另跑了CLI。

浏览器新端口可加载装配库；完整交互矩阵及用户验收尚未完成。

## 剩余接入（不得关闭）

### 后续能力替换批次（同日）

新增/api/capabilities/:typeId只读转发catalog_item；浏览器capability-discovery按类型缓存、失败可重试。分析前读取当前舰体与已装模块能力，并用analysisVersion防止晚到结果覆盖新船。

已替换：舰体模式的四舰体ID映射；子系统组963/固定4槽/125+index候选判断；已装模块状态按钮及新安装默认状态；弹药组和尺寸的本地属性枚举；晶体识别改读宿主chargePolicy；取消Active请求自动降为Online；无人机出动控件不再固定5。安装合法性仍走既有原生分析/preview，发现元数据不是许可。

验证：能力缓存/未知状态/原生候选/显式状态保留JS专项通过；能力替换后16项引擎集成/编辑测试和5项预览/分享包测试通过，66模块检查通过。HTTP实查赫卡特候选模式、被动模块not_applicable；浏览器独立装配库可加载。没有宣称完整用户交互矩阵已通过。

下列原列表1、2、4已有上述部分完成，剩余包括：普通安装挂点/尺寸预筛仍有本地快速规则；未完成所有特殊能力组合的浏览器验收；目录初始canActivate字段仍为旧元数据快速展示，安装及分析前会由公开能力覆盖。特殊舱、导航数据、机制支持不因该替换自动完成。模式名称/图标仍为现有展示设计；如果引擎未来引入新的模式语义，需要补名称描述契约。

1. catalog_item.capabilities批量/按需缓存与UI模式、子系统、模块状态、装填发现替换；此前审计把入口写作catalog_type_details不准确，以新交付catalog_item为准。
2. 无人机固定5个操作控件仍需能力驱动改造；本次只消除机库互斥。
3. 炸弹显式参考政策需贯通UI选集、曲线、保存/分享及口径提示，当前只展示其不可用状态；不默认偷偷启用政策。
4. 错误信息需覆盖新增fighterLoadedClass资源中文描述；新能力加载/未知UI仍需逐项验收。
5. 末日新资源隔离结果、晶体政策、指挥航母/势力泰坦、多能力共存浏览器验收，以及完整用户场景回归待继续。
6. 技能点、特殊舱库存等交付调查边界仍不关闭。完整market树缺口同样保留。

技术接入不是发布；以上UI改动待用户逐项验收。
