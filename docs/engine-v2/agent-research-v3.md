# NUI-112：战斗研究摘要与属性查询

2026-09-21，agent-mcp-v3，8个工具。原生引擎仍为0.212.0-ui.battleentry1/r60，没有修改战斗规则、公开原生schema或旧任务。v1/v2冻结文件保留。

## 时间口径

fitlab_battle result现在组合公开battle_result和经过引擎校验的battle_events，返回timeline：

- destructions：ShipDestroyed原始事件时间与序号。
- lastDamageSnapshot：该船最后一次正伤害的LayeredDamageResolved.defense快照，原始HP、层容量及时间；不是其他船被击毁时的快照。
- atSimulationEnd：完整任务在声明终点的原生HP/毁灭状态。
- atPartialCheckpoint：取消或可恢复中断任务的已发布快照；此时atSimulationEnd为空，不冒充完整终局。
- atOtherShipDestruction：明确unavailable。当前原生结果没有任意时刻所有舰船的精确同步快照，不用最后受伤或终局值补齐。
- firstMissileApplicationSamples：每个攻击者/目标组合的首个原生应用事件，注明不是平均值。

所有业务数值直接来自公开原生回执，唯一转换是微秒到秒；不重算伤害、回充、百分比、EHP或“综合强度”。流式读事件，保留少量摘要；每页attempt和查询前后revision/hash一致，否则拒绝混合结果。跨resume任务只解释当前公开attempt的事件后缀，不假装含完整历史。

真实三场任务对照：狞獾66.2秒毁灭；海军型最后正伤害68.7秒后7679.942673694291HP；300秒终点13713.4755508305HP。二者不能互换。两场打小鹰的毁灭时刻分别85秒和50.51971秒。本轮未把这些单场结果推广成船体综合强度结论。

## 查询与交互

fitlab_battle events支持kinds/sourceId/targetId筛选，offset仍指原始事件游标；hasMore表示还有原始事件可扫描，不保证后面存在匹配项。默认返回去掉不适用null载荷的紧凑事件，完整原始事件保留fullResultId。典型查询：

```json
{"action":"events","jobId":"job-navy-vs-merlin","kinds":["ShipDestroyed"],"targetId":"merlin"}
```

result/events在任务未完成时返回ready:false、当前job和nextCall；失败任务不建议持续轮询，不把无结果伪装成空的成功结果。

新fitlab_item(typeId,names?,locale?)读取官方特性、模块配置和常见基础属性；默认明确为未装配源值，不是全V值。可按SDE内部名、官方本地化名或字符串ID查询；歧义返回候选而不是任意选择。只有原生type属性中可发现的名称能匹配，不虚构缺失值。

fitlab_fit attributes(sessionId,names,itemId?)解析名称后调用公开fit_attributes，返回state/reason、数值、原生单位元数据、fitHash及完整追溯引用。默认itemId=ship，支持module.<id>/charge.<id>/drone.<id>/fighter.<id>/subsystem.<id>。需要概览直接fitlab_fit read；create现在立即附计算摘要和nextCall，避免误把原始技能输入当船体属性。

fitlab_fit state支持instanceIds、active/online/overheated，转换为原生setActive/setOnline/setOverheated命令，沿用revision/requestId。被动模块错误提供active:false建议，不自动改写用户请求或卸下重装。summary附静态有效性提示；静态有效不等于战斗准入。无武器、注电器或炸弹的靶船可以省略reservePerWeapon；真实耗弹模块仍须显式声明补给。

错误分操作返回指引；无效结果路径提供最多30个可用键及总数，不再仅显示'ship'或对只读查询提示事务重试。

## 同口径CLI

facade新增单次调用CLI，和stdio MCP调用同一个方法，所有业务输入仍经原生MCP；不是绕过引擎读取私有会话文件：

```
python agent_mcp.py --engine ENGINE --state STATE --mcp-dll MCP_DLL --call fitlab_battle --arguments request.json --out result.json
```

命令不提供--call时仍运行stdio服务。原生MCP的battle_events/battle_result是原始数值来源；新增研究视图的统一入口是本facade CLI/MCP。

## 验证与边界

test_agent_research.py覆盖三真实任务的公开查询（可选本机FITLAB_RESEARCH_STATE）、时刻不可替换、取消/失败/运行中状态、名称解析、未知属性、直接修正被动模块和未装武器靶船。test_agent_mcp回归会话/引用/冻结v3契约。

test_research_agent_dsh.mjs调用实际DSH插件、isJsonValue和模型文本投影，三任务摘要均可直接内联；一次1v1摘要约5.8k字符，按目标筛选的毁灭事件约428字符，两项装配属性约798字符。facade CLI与MCP结果逐字段一致。test_battle_agent_dsh保留从目录/装配开始的完整新战斗回归，不用历史结果替代运行验证。原始证据位于output/research-*.json，仅本机。

这不是对DeepSeek最终叙述正确性的保证。模型仍须区分基础/装配值、耗时减少/速率增加、特定静止配装实验/普遍结论。本轮只改善查询与明确口径；不自动生成获胜时刻没有记录的数值，不自动计算船体综合强度倍数。DSH已重启，实际配置发现8工具；引擎未升级，旧任务继续可读。没有重新打包或发布。
