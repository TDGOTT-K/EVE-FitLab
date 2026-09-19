# 能力驱动接入审计与缺口清单

日期：2026-09-19。UI审计基线4eb94fe；独立引擎0.200.1-ui.1 / r44 / 静态v59 / SDE3503375。

本次只研究、登记，不修改UI或引擎行为。覆盖装配编辑、浏览、机库、状态、装填、模式、子系统、输出、深渊及角色相关入口；不是对整个引擎机制覆盖率的穷尽证明。没有执行跨所有船型的实机回归。

## 结论与责任划分

不能用“出现groupId/typeId就是错误”作为审计准则。稳定身份查找、科技图标、市场分类、由引擎维护的已验证机制映射都可以保留。需要清理的是UI根据这些身份自行决定能力存在、动作合法或数值口径。

应分三层：引擎提供有版本和来源的事实与规则；适配层统一处理来源、状态和展示聚合；UI组件负责布局和操作体验。市场导航不能决定装备能做什么，类型元数据不能冒充装配后的结果，机制支持也不等于当前操作合法。

责任标记：

- U：现有原生结果可用，UI/适配漏接或写死。
- E：已确认公开契约不能表达/发现所需信息；不是已证明缺少游戏机制。
- M：部分已有、部分需补公开接口。
- V：游戏语义或完整覆盖待核实；不能直接要求实现新机制。

## 已核对的公开能力边界

1. r44 catalog_search支持text/group/category/meta/published/family/cursor，不是按当前配装筛选合法候选的查询。
2. catalog_type_details返回EveTypeCapabilities（static-type-capabilities-v2）。EveTypeCapabilityDiscovery.Describe当前处理category87舰载机和18无人机，其他类别返回null。含逐能力role、damageProjection、targetApplication及source，但Scope明确要求实际装配校验。
3. fit_analyze已有FighterBay、DroneBay、resources、TacticalMode、Subsystems、Inventory、weapons、repairs、energy、outputContributions及inspector。不能说这些全缺；但nullable投影没有统一的“没有能力/本次未知/不支持/未请求”说明。
4. fit_preview_input/fit_preview与EveEditor已能验证候选编辑，并返回分析与资源/指标差量。UI的nengine_preview.py已经调用，应该复用。没有发现适合整个浏览器按候选批量返回资格/原因的公开接口。
5. EveTacticalModes.Types内部已有候选映射；公开EveTacticalModeProjection主要是当前选中模式。EveSubsystems内部已有槽位、舰体关系及必选校验。它们不应在UI再维护第二份。
6. EveStaticInventory只表达Cargo/Magazines/Crystals，没有独立specialHold身份和逐舱内容模型。特殊舱属性可读不代表已支持该舱库存。

源码依据：引擎src/NEngine.Core/Data/EveCatalog.cs、EveTypeCapabilities.cs；Dogma/EveFitting.cs、EveEditor.cs、EveTacticalModes.cs、EveSubsystems.cs、EveStaticInventory.cs；contracts/headless-v1/r44/tool-schemas.json。

## 逐项审计

| ID | 责任/优先级 | 代码证据与问题 | 现有依据及最小改造 |
|---|---|---|---|
| CAP-01 | U+E / P1 | fighter-ui.js fighterHull从组白名单改为attrs[2055]>0，仍是基础目录启发式；app.js renderBayConfigBody以fighterHull决定隐藏无人机区 | 用装配后fighterBay/droneBay分别驱动，不互斥；保留已装对象。有效正能力可先接现有投影；空/缺失投影的原因需统一契约。不能用容量0直接推断“不存在”。 |
| CAP-02 | U / P1 | app.js无人机出动控件Array.from({length:5})、maxActiveDrones??5；drone-stacks.js按5拆分 | 改用原生DroneBay/resources的数量、带宽、容量。5可以是视觉分页大小，不能限制实际可表示数量；未知上限不默认5。 |
| CAP-03 | E+V / P1 | EveFitFighter只有Deployed；管内待命与备用映射相同；class/tubes只统计deployed | 已列ENG-44-06，不重复建机制。补位置/管位/部署独立表达；装载规则外部证据仍需核实。 |
| CAP-04 | M / P1 | app.js subsystem=group963?4:0，125+index，四种名称硬编码；模式tactical-mode.js维护4舰体ID字典 | 高中低改装槽已在syncEngineSlots接装配后slotUsage，保留。补空/非法草稿下的槽位需求、候选子系统和模式发现；复用引擎现有映射，不取消引擎已验证范围。 |
| CAP-05 | M / P1 | nengine_catalog.py activation_capabilities在Python解析唯一default effect和类别；缺/歧义变false；effective_module_state会把Active改Online | 当前被动装备修正有价值，但仍是第二处规则与信息压缩。公开普通装备state capabilities，区分被动、未知、不可用及原因；候选动作使用原生preview。不得把明确请求的非法状态静默当合法。 |
| CAP-06 | M / P1 | app.js needsAmmo/acceptsAmmo读604/605/606/609/610及128，isCrystal枚举86/374/375；canInstall用本地kind/尺寸/舰体关系 | 现有原生装填、晶体、弹仓投影与校验可复用。补未装入时的charge policies、兼容候选及实例需求发现；不能以没有当前Crystal投影判定非晶体。 |
| CAP-07 | M / P1 | installation-limits.js重复统计effect42/40挂点、1544同组/2431同型号限制，本地拒绝会挡住原生验证 | 保留快速视觉提示但不成为独有权威。复用fit_preview_input最终校验；需批量候选或资格摘要避免对上千物品逐个完整分析。覆盖替换释放资源、离线仍占挂点等条件。 |
| CAP-08 | U+M / P1 | fighter-ui.js按duration.attributeId 2233/2182/2401枚举可见武器；nengine_output.py及视图按kind前缀分组 | catalog_type_details.capabilities.abilities已有role和能力状态，outputContributions已有按abilityId身份的指标。先接现有元数据，未知能力保留不可用项；若稳定输出分类/默认选择语义缺失再补契约，不将新的kind静默丢掉。 |
| CAP-09 | M / P2 | nengine-view.js对外电子效果只认group65/52/209/208/379/201，并枚举属性名 | 传电/毁电/维修已有结构化energy/repairs，应复用。其他效果需公开静态展示描述（作用对象、单位、范围、口径、状态），不能从raw属性存在推断机制已支持。此项不要求补电子战运行时。 |
| CAP-10 | M / P2 | app.js特殊舱只有8个属性名白名单，仅显示容量；库存只写普通cargo | 容量读数可作为静态信息，但不能冒充特殊舱可用库存。补supported holds发现及容量来源；逐舱库存若属于缺机制，先登记范围和不支持原因，不能由UI重新实现搬运规则。 |
| CAP-11 | E / P2 | EveMutations只从dynamicItemAttributes.highIsGood取方向，忽略已有dogmaAttributes.highIsGood | 已实查50(cpu)=false、1255(droneDamageBonus)=true，而对应突变范围未填写。要求引擎组合已有元数据，显式记录优先级及来源；两处都缺失保留未知。不在UI按属性名硬编码好坏。 |
| CAP-12 | M / P2 | nengine_catalog.refresh_catalog依赖category/effect到kind映射，未识别类型被continue掉；市场路径借用旧SDE；app.js追加5120导航 | 导航与业务能力必须分离。catalog_search/type_details已有通用身份和meta/marketGroupId；未知类别应能浏览或明确标不支持，不能彻底消失。当前索引缺marketGroups完整树，属于导航数据交付需求，不是数值规则缺陷。 |
| CAP-13 | E / P2 | skill-points.js只有明确未学习的0，其余未知 | 目前未找到技能点查询。继续列能力缺口，不在前端写训练公式。需已有机制或来源快照提供逐技能/总SP，训练进度SP与按等级最低SP不能混称。参见NUI-70。 |
| CAP-14 | M / P2 | NUI-67/69空和显示0、零曲线与原生empty_selection/null存在不同展示语义 | 当前展示已明确以完整空选集证据判断，没有覆盖不可用贡献；仍应明确公开空和/零基准/曲线空选集政策，避免后续UI各自重复特判。不能把0/0比例定义为业务上的0%。 |

## 哪些不应作为引擎缺陷

- 身份查找、按category/meta/market分类、标签排序和图标选择，本身不是游戏计算。
- 官方SDE没有充分关联信息时，引擎可以维护带版本的人工验证映射；目标是只有一处权威，不是消灭所有枚举。
- 纯呈现维度（伤害四分量、单位格式、控件图标、每页显示5行）可由UI定义。
- 现有fit_analyze能返回某机制的投影，不等于可以直接支持整个战斗过程。
- catalog capability返回requires_fit_validation不代表可安装；preview返回可计算不等于全部装配合法。

## 建议交付契约（提案，不是当前字段）

不用一次重造全部接口，优先扩展现有catalog detail和analysis。

类型发现层：稳定capability ID、类型/实例适用范围、候选关系、supported/not_applicable/unsupported/unknown状态、reason、source和规则版本。实例有变异时不能沿用普通type的数值结果。

装配实例层：按对象身份返回slots/holds/modes/abilities及当前容量、占用、已选状态；每项注明是否完整、未计入对象。能力不存在、存在但容量0、缺少前置输入、查询失败必须分开。缺失字段不能被当作false。

操作层：可使用现有preview；返回allowed/denied/unknown、结构化规则原因、候选对象/位置、基准revision或fitHash、影响范围。数据未就绪时可以浏览、保存草稿；不得标成合法。不以API请求次数制造交互卡顿。

能力存在、机制支持、当前合法性必须是不同维度；UI pending/loading是请求状态，不应混进游戏机制状态。未知的新能力至少保留对象和原因，不悄悄隐藏。

## 执行顺序及验收

1. UI先做CAP-01/02/08已有能力的接线：独立机库、原生数量、能力元数据。沿用现有区域，不新增用户未确认的面板。
2. 引擎优先交付槽位/模式发现、状态/装填能力描述及缺失状态语义；把ENG-44-06独立处理，避免一个总接口把已知缺口吞掉。
3. 将公共适配集中为能力视图层，组件禁止自行按船型推断。事实按引擎/契约/SDE版本缓存，实例结果按fitHash+context+revision绑定；晚到响应不能覆盖新船。
4. 查询性能：类型能力按需加载/缓存，批量候选分页；只在用户选择或悬停时做必要preview。禁止渲染每个叶子都串行完整分析整船。记录改造前后请求数、页面切换/首次可交互/编辑响应，不在本次文档虚报性能结果。
5. 接口版本、MCP/CLI同输入结果、来源/完整性、冻结schema及本地补丁台账分别验收；UI和引擎分别提交。保持静态能力范围，不做新战斗机制或沙盒UI。

| 验收维度 | 必测情况 |
|---|---|
| 跨类型 | 普通舰船、普通航母、超级航母、指挥航母、由当前原生查询确认有舰载机能力的势力泰坦；不要以名字断言能力 |
| 能力组合 | 单无人机、单舰载机、两者共存；无能力；容量0但有已保存物品 |
| 动态结果 | 技能/模式/子系统/装备修改改变槽位或容量，超限对象仍可见可修复，不自动丢弃 |
| 非法草稿 | 未选必需模式/子系统、技能不足、未知类型、缺失分析；仍可发现解决问题所需的选项 |
| 状态与装填 | 被动、主动、可超载、默认效果歧义；普通弹仓、晶体、脚本、突变实例 |
| 输出 | 新kind/ability、不伤害能力、有限弹量、部分支持、真实0、空选集、零分母 |
| 位置 | 管内待命、部署、备用；跟ENG-44-06契约一起验收 |
| 无头与保存 | 原生输入保存/恢复后能力及身份不丢失，同输入MCP/CLI与UI依据一致 |

本次验证限于源码/冻结契约审计、先前指挥航母只读查询以及已记录问题的复核。尚未进行以上矩阵的全量实验，不宣称所有特殊船型已经支持。
