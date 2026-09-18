# 整合引擎 0.200 / r43：FitLab 隔离验收

日期：2026-09-19。引擎目录 `D:/AI/GPT6/N号引擎-整合-0.200-r43`，提交 `81adb76e0e8ee15a9e3a0ad162460290d3b76b11`。实际 discovery 为 0.200.0/r43、33 工具、静态 v59、SDE3503375；按交付 UI-BASELINE 核对索引和版本。未修改新旧引擎源码。

## 本地补丁核对

| 原 UI 补丁 | 整合声明 | 本次 FitLab 验证 |
|---|---|---|
| LOCAL-001 输出差量/比例 | 已纳入，保留主库贡献 v4 | output、EDPS、preview 专项通过 |
| LOCAL-002 突变比较 | 已纳入 | mutations 公开对照和历史回执专项通过 |
| LOCAL-003 曲线政策/采样峰值 | 已纳入 | 当前 r43 schema、MCP/CLI 完整曲线补测通过 |
| LOCAL-004 独立方案源属性 | 已纳入 | booster_plan 与属性详情专项通过 |
| LOCAL-005 估价 | 已纳入 | valuation 及含报价快照的完整文件往返通过 |
| LOCAL-006 诊断草稿 | 主库已有等效实现 | sessions/persistence 的草稿保存、导入、撤销与恢复通过 |
| LOCAL-007 预览完整性 | 已纳入 | 两侧缺项/真实零/基准分析专项通过 |
| LOCAL-008 无会话预览 | 已纳入 | preview_input 与实际安装/CLI、编辑历史专项通过 |

这张表取代“LOCAL补丁仍需全部移植”的旧结论，不改写旧副本的历史交接记录。没有再开发一套游戏公式或战斗机制。

## 三项 P0 实际复验

本次执行整合交付的 verify-phase1-static-audit.py，输出全新目录 `output/integration-r43-protocol-001`，没有只引用交付方旧日志。

- 36 个原生案例全部验证；19 个 CLI 对照相同。
- 5 个诊断草稿 MCP create/save/export/import 往返及一个 CLI 草稿往返通过。
- 固定 SDE 的单件 CPU/PG、技能后资源容量、隐形速度独立检查通过。
- E-P1-01：合法零速返回最大速度 0，起步至75%为 null/not_applicable。
- E-P1-02：堡垒/隐形/克隆舱静态结果保留，未确定的电容贡献有实例及原因；未把它们宣称为零耗电。
- E-P1-03：未知主动效果仍可返回 6 项资源，错误槽位仍报 SLOT_UNAVAILABLE；未决效果保留 partial_static_dependencies。

P0 的全局失败/诊断丢失问题可关闭于此候选；不能把保守隔离等同于所有特殊效果已实现。

## UI 适配与测试结果

新增消费 validationState、capacitorUnavailableReason、capacitorContributions。容量/峰值回充在周期查询不可用时仍从 capacitorRecharge 原样显示；不可用贡献列出实例与原因。零速起步时间显示“不适用 · 当前最大速度为 0”，保留原生状态/原因。修改旧电容失败提示，不再把电容周期准入不足误写成整份装配不合法。没有重设计布局。

运行 `FITLAB_NENGINE_ROOT` 指向新交付的统一门禁：`output/phase1-gate-20260918T180020Z`。首次 88 项 Python 中 87 通过、1 错误；10 个 JS 专项及模块检查通过。唯一错误是测试读取写死的旧 local-r36 schema，在主线 r36 中不存在 fit_output_curves；现改为按实际交付 revision 读取 schema，随后两项曲线专项通过。保留原失败日志，没有把首次门禁改写为全绿。

补测：4 项状态分类、空值/零速/局部电容显示 JS、5 项既有电容测试和 62 模块静态检查通过。未重复引擎全量、未打包 Windows。

实际隔离 UI：`http://127.0.0.1:56432/#library`。独立 UI 数据、引擎状态均在 `output/integration-r43-ui-001`，没有旧会话复制或来源字段改写。

4 个代表案例通过适配层保存/分析：堡垒 partial、围攻 valid、MJD partial、采矿器错误槽位 invalid，均保留6项资源。浏览器实际打开堡垒确认 N0.200.0、容量9375GJ、峰值31.25GJ/s、最大速度0与起步不适用、未计入high-0；打开采矿器slot7确认“装配不合法 · 当前结果不完整”且CPU/PG仍可见。适配摘要在 `output/integration-r43-ui-001/adapter-cases.json`。

## 当前切换边界

代码默认引擎仍为旧独立副本；5208 工作区未自动切换。本候选交付明确禁止自动迁移旧会话，v54→v59 不兼容。旧会话、回执和版本绑定分享文件不能通过修改版本字段继续使用。

可先使用上述新引擎隔离工作区。若决定切换正式测试入口，应明确使用新的引擎状态及 UI 工作区，保留旧目录；不自动迁移历史输入或清理用户数据。用户已说明无需投入旧数据兼容，但这不等于可以删除旧数据。

技术接入通过仍不代表发布：用户还要主导 UI 重构、交互设计和亲自验收。
