# 第一阶段引擎缺口审计：当前 r41 副本

日期：2026-09-19。结论：**确认 3 项可立即进入引擎冲刺的 P0 问题，均有原生 MCP/CLI 复现；不是 UI 猜测。** 不宣称已穷尽全部 EVE 装备、技能、组合和静态规则。未确认项不冒充缺口。

技术闭环完成后仍必须由用户主导 UI 重构、交互设计和亲自验收，再决定发布；本清单不是发布批准。

## 审计基线与证据

- 唯一被检查的引擎：`D:/AI/GPT6/N号引擎-UI接入-0.190-r33`。
- 当前源码提交 `3b02db6a36572501ebe4e6bdf18f2142d6a846cb`，实际 discovery：0.190.8-ui.1 / r41，33 工具，规则 eve-static-dogma-v54，SDE 3503375。
- 索引 SHA256：a73672fa5526f05d04095674b75d0d06b8763c1b0c542713b834daefee3d71da。
- 本次直接调用 fit_analyze，未经过 UI 的 analyze/nengine_adapter 映射。角色使用当前 published 技能全集 V；每个样本同船同装备只切 Active。在线对照 errors 为空。
- 17 个装备/舰船组合，在线与主动共 34 查询；主动组 6 个顶层错误、8 个静态覆盖不完整且资源/属性全空、3 个正常对照。17 个主动样本 CLI 与 MCP 完整结果或错误对象相等。
- 另做 2 个错误槽位查询，CLI 与 MCP 同样一致。总计 36 个 MCP 案例、19 个 CLI 对照。
- 原始请求、回执、实际 discovery、摘要：`output/phase1-engine-audit-r41/`。可执行复现：在 UI 仓库运行 `python scripts/audit-phase1-engine.py`。脚本只做静态查询，临时会话目录，不修改用户装配、引擎源码或主工作区。
- 之前的 84 项产品专项是已有链路证据，不作为这 36 个新案例的替代。

## E-P1-01 / P0：速度为零的静态模式导致整份配装分析失败

原生结果：`EVE_MOTION_PARAMETERS`，消息要求 mass、inertia、speed 全部为正。CLI 退出 2；没有返回资源、属性或诊断分析对象。

| 舰船 typeId | 装备 typeId | 装备 |
|---|---|---|
| Revelation 19720 | 20280 | Siege Module I |
| Rorqual 28352 | 28583 | Capital Industrial Core I |
| Apostle 37604 | 27951 | Triage Module I |

复现文件：上述装备 ID 的 `*-active-request.json` / `*-active-fit.json`；对应 `*-online-*` 是正常对照。

源码定位：`src/NEngine.Core/Dogma/EveFitting.cs:687` 将静态机动交给 EveMotion.Project；`EveMotion.cs:25` 拒绝非正速度。存在 MotionUnsupportedReason 字段，但当前零速度路径没有局部返回，异常中断整份分析。

验收：

- 明确区分合法的静止/禁用推进状态与非法负值/非有限值，不将合法零速度当坏输入。
- 返回引擎确认的当前速度及其他静态属性、CPU/PG、准入诊断。起步至 75% 在此条件下若无意义，返回不可用/不适用及原因，不伪造 0 秒。
- 在线/主动/取消主动、上述三种船、原普通推进器、非法数值均有回归；MCP/CLI 同输入一致。
- 不要求舰船运动、碰撞、跳跃或逐 tick 模拟。

## E-P1-02 / P0：统一电容消费者准入把非普通周期模块变成全局异常

原生结果：`EVE_CAPACITOR_EFFECT`，`module-1 lacks verified activation timing or discharge metadata.`。在线正常，主动时整份分析失败。

| 舰船 typeId | 装备 typeId | 装备 |
|---|---|---|
| Paladin 28659 | 33400 | Bastion Module I |
| Rifter 587 | 11370 | Prototype Cloaking Device I |
| Rorqual 28352 | 23735 | Clone Vat Bay I |

源码定位：`EveFitting.cs:657` 遍历所有在线主动模块，`:666` 强制每个默认效果同时具有 durationAttributeID 和 dischargeAttributeID；否则直接抛异常。

验收：

- 按已核实的效果语义处理非普通周期电容消费者。不能笼统把缺字段当零消耗，也不能为了避免错误强制 UI 把这些真实主动装备改成被动。
- 元数据不足时只将相关电容贡献或时序标记不可用，保留经依赖证明可用的静态参数及装配错误；存在明确无耗电规则时应提供其来源。
- 堡垒、隐形、克隆舱主动状态可作为静态配装保留、分析和诊断草稿往返，不因无关电容投影导致整船消失。
- 保留普通主动模块的耗电计算、真正错误元数据的诊断；MCP/CLI 一致。不要新增隐形/克隆等运行时机制。

## E-P1-03 / P0：未支持主动效果吞掉静态资源和结构诊断

以下装备 Active 时都返回 `staticCoverageComplete=false`、`resources=[]`、`attributes={}`；同输入 Online 返回 6 项资源和属性，errors 为空。

| 装备 typeId | 效果 ID | 原生名称 |
|---|---|---|
| 483、17482 | 67 | miningLaser |
| 42529 | 6733 | moduleBonusWarfareLinkShield |
| 4383 | 4921 | microJumpDrive |
| 34593 | 6063 | entosisLink |
| 22175、22177 | 1738 | doHacking |
| 25861 | 2757 | salvaging |

原因均为 IMPERATIVE_EFFECT_ADAPTER_MISSING。引擎有明确 coverage 诊断，所以不是把这些主动产出伪装为 0；**问题是缺失的运行时/效果投影连带阻断了整个静态装配**。

更小的结构错误复现：Rifter 587 + Miner I 483，slotIndex=7，全部技能 V。

- Online：errors 包含 `SLOT_UNAVAILABLE`，resources 有 6 项。
- Active：errors 变为 `[]`，resources 变为 `[]`。MCP 和 CLI 一致。

源码定位：`EveFitting.cs:485–486` 未解析依赖时提前返回；槽位占用、槽位容量、挂点、数量限制、资源检查在后续路径，故被跳过。原生 clients 若只读 errors 会误以为没有结构错误。

验收：

- 不依赖未知效果即可决定的错误始终返回，包括本例不存在的槽位。依赖未决的检查应报告未完成，不能用空 errors 表示已经完成检查。
- 对已证明不受未支持效果影响的 CPU/PG、槽位、实例信息、基础静态参数继续返回；确受影响的项保留 null/原因/依赖，不拿 Online 结果冒充 Active 结果。
- 区分运行时动作与静态修正；需要的静态修正若仍缺失，列出具体 effectId 和受影响属性，不笼统要求补整个采矿、骇客或舰队运行时。
- 增加未知效果叠加槽位冲突、资源超限、技能不足的诊断组合测试；诊断草稿 create/save/export/import 后结果一致。
- 同时测试正常的 Core Probe Launcher I 17938、Festival Launcher 19660、Warp Disruption Field Generator I 28654，避免粗暴禁用所有特殊模块。

## 第一阶段逐域核对与责任分界

下表“已有”指源码/公开契约及已有专项存在，不等于该领域全部物品已认证。

| 领域 | 当前公开能力 | 审计结论/归属 |
|---|---|---|
| 船体/模式/槽位/T3/模块状态 | EveFitRequest、fit_analyze / sde-fit、编辑命令 | 模型已有；模式主动分析有 E01/E02；全域规则覆盖仍需组合验收 |
| CPU/PG/校准/挂点/数量/技能/匹配 | 分析 errors、warnings、resources、coverage | 不能再报“没有这些接口”；E03 是已有规则执行顺序/隔离问题 |
| 弹药/实际弹量/货舱/晶体实体 | inventory、magazines、crystals；native_inventory 专项 | 已有；UI 无损交换、稳定身份仍需补齐 |
| 无人机/舰载机机库与静态贡献 | drones/fighters、bay、outputContributions、ability metadata | 已有主攻击和支持边界；特殊能力/追击/补员运行时不列本次冲刺 |
| 技能/脑插/药剂/副作用 | fit_analyze、booster_plan_analyze/roll/verify + CLI | 已有；独立方案源属性也已通过 LOCAL-004 补齐，不再重复报缺 |
| 深渊实例/随机/比较 | mutation_rule/roll 及 comparison、装配 mutation | 已有；不要让引擎重做 UI 库管理、分组和入口 |
| HP/EHP/抗性/速度/锁定/名义输出 | defense/motion/attributes/outputContributions | 已有；零速度属于 E01。部分 UI 展示未完不属于新机制 |
| 固定目标应用/EDPS/选集/差量/曲线 | fit_analyze.context.output、fit_output_curves / sde-output-curves | 已有；选集和比较、自动参考政策由 LOCAL-001/003 完成，不重复报缺 |
| 电容负载/窗口/来源/维修/传毁电静态参数 | capacitor_scenario、repairs/energy/remoteAssistance 等 | 已有接口；E02 是准入错误；静态维修等更多 UI 入口仍属 UI 欠账 |
| 估价及来源 | fit_valuation / sde-fit-valuation，外部报价快照 | 已有；历史报价随分享封装保存是 UI 闭环责任，不要求引擎联网造价 |
| 编辑/预览/撤销/重做/保存/导入导出 | 原生会话、fit_preview_input、fit_export/import 及 CLI | 已有，LOCAL-006/007/008 已补诊断草稿和预览；UI 事务等待体验由 UI 处理 |
| 名称/标签/备注/分析上下文/版本 | name/tags、source-bound document、独立 context/query | 备注和上下文可随 FitLab 封装保存；不因为核心 Fit 不含备注就判为游戏引擎缺失 |
| 无损分享/图片/EFT/稳定身份 | 原生 Id/成员 Id/fitHash/source 已可传递；UI EVF3 未完整保留分析语境 | UI 欠账：分离本地库身份、保留实例 ID、目标/外部来源/报价快照；不交引擎补“分享 UI” |
| 技能点总量 | 本副本缺专门 SP 查询 | 不在本次用户列出的最低静态战斗指标中；不得夹带成第一阶段 P0 |

**公开接口类新增阻断：本次未确认。** 已有 33 工具及对应 CLI 能力足以覆盖上述已实现查询；接口统一程度仍可改进，但不能把便利性改进冒充机制缺失。

## 尚不能下结论的部分

- 17 种模块不是全目录：其他舰船、装备、无人机/舰载机、变异属性和技能组合仍可能有额外缺口。
- 本轮证明了错误隔离/准入/诊断问题，没有证明每条数值公式符合游戏实际；公式正确性需要独立规则和固定数据预期，不能以 CLI=MCP 自我证明。
- 缺失效果的静态影响边界需要引擎维护者逐条确认。不能把上述所有效果简单改名为 runtime-only，就当问题修复。
- 不要求此轮开发战斗沙盒、星门/跨星系、逐 tick、舰载机成员资源/补员或完整舰队模拟。

## 给引擎维护任务的交付要求

优先修 E-P1-01、02、03；同根因可合并实现，验收案例不能省略。当前审计副本不代表主引擎最新状态：维护者先在其当前代码复现，已修项目提供回归证据，不重复造实现。

修复应包含公开状态/原因及必要上下文，MCP/CLI 同口径；若改变输出结构则按正式契约版本流程登记。交付新副本前核对本地 LOCAL-001～008（docs/ui-local-changes.md），不得覆盖已补的选集比较、曲线、突变比较、方案属性、估价和编辑接口。

本轮没有修改任何引擎代码，也没有向其他任务发送消息。
