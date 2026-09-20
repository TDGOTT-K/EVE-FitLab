# Agent MCP v1 / NUI-110

2026-09-20。针对 DSH 对话《测试FitLab MCP可用性》的真实调用轨迹：重启后71次工具调用，其中37次MCP、21次目录搜索；完整分析的负零被客户端拒收，两次编辑已落盘但回执失败；workbench仍被截至50000字符并转存约23万字节；battle_start定义约27万字符。

## 两层入口

原生NEngine公开r59的38工具保持不变，服务专业客户端及FitLab网页。新增独立FitLab agent facade，契约 `contracts/agent-mcp-v1/tools.json`，六个入口：

- fitlab_status：短状态、推荐流程及能力边界。无需启动UI；不完整EVE覆盖不等于无战斗工具。图片导出明确不可用。
- fitlab_search：最多30个名称批量检索，每个最多5候选，返回总数及下一页游标；不静默替用户选择同名物品。
- fitlab_fit：create/read/edit/undo/redo/save。读取只需sessionId，编辑仍必须revision、稳定requestId；批量commands原样交给原生工作台。create明确未列技能为未训练，不冒充全V。不默认保存。
- fitlab_tools：按需发现原生工具和输入schema，巨大schema通过引用和JSON pointer逐层查询。
- fitlab_call：原样调用已发现的原生工具，含战斗工具。没有旁路公式或直接读取原生会话文件。
- fitlab_result：通过不透明resultId和RFC6901 pointer读取完整结果；对象分页列键、数组分页、长字符串分片。需要整个文档的客户端仍可直接使用原生接口。

启动：`python agent_mcp.py --engine ENGINE_ROOT --state STATE_ROOT [--mcp-dll ISOLATED_MCP_DLL]`。仅用标准库及已有桥接器。此入口不依赖网页服务，不替换其接口，也未重新打包桌面候选。

## 摘要与可靠性

所有业务数值来自公开引擎回执。摘要保留资源、名义DPS及不可用原因、防御、电容、速度、模块状态、覆盖与输出选择；诊断显示数量和前5条，明确剩余数量与完整路径。超过10000字符的摘要也返回引用，不隐式截断。名义DPS不是实际应用或电容可持续DPS。

完整结果保存在独立 `STATE_ROOT/agent-results`，按内容哈希定位，保留最近128个，引用重启后有效，淘汰后明确报错。只清理facade生成的结果副本，不清理用户会话。单结果体积沿用引擎返回规模，不是整个进程/磁盘的严格字节预算。此版本串行处理请求，无额外自动重试；不改变原生请求的幂等/取消/保存语义。create已有会话返回冲突；不以冲突假装本次创建成功。其他不确定写入必须保留同一个requestId重试。

通用工具提供文本回执，避免依赖客户端structuredContent投影。原生修复UI-LOCAL-022仅在MCP输出边界将数值负零归一为零，内核公式、持久化、fitHash/检查点身份不变。CLI仍可能输出合法负零，数值对照按JSON零等价比较。静态r59冻结schema和文件哈希不变；版本0.211.1-ui.agent1。

## 验证

`test_agent_mcp.py`：查询/创建/批量编辑/读取/保存/撤销/重做、相同requestId重放、失败不推进revision、分页/转义pointer/路径拒绝/引用重启保留、冻结工具定义。

`test_agent_dsh.mjs`：调用实际安装的DSH插件、它的isJsonValue校验与output.render，使用用户幽灵级输入的只读副本，写入临时隔离会话。原生create/analyze/inspect/preview/execute/replay/attributes均通过，MCP分析与CLI逐字段一致（仅零符号标准化）。客户端输出定义约3178字符；幽灵级摘要约7795字符，原生分析约371866字符。没有通过真实模型对话重测成功率/总token/总耗时，不能据此宣称模型已完成整个配船任务。

战斗使用引擎自带 `examples/aoe-interception.json` 合成fixture，通过facade调用start→status→result完成3秒模拟。第一次测试脚本错误使用commands而不是intents，被JOB_CONTROLLER_FAULT正确拒绝；修正测试脚本后通过，并在工具说明附最小合法脚本。该验证不是幽灵级SDE战斗准入证明。

引擎3项冻结合同、3项workbench专项通过。未重复全游戏机制测试。

## 本机接入

DSH fitlab profile已备份并改为启动本facade，沿用原state目录及已有phantasm-test-01。新进程使用维护副本的 `artifacts/agent-mcp-001/NEngine.Mcp.dll`。实际配置探针发现6个工具、r59、0.211.1-ui.agent1。已重启DSH及网页开发后端，默认引擎构建也已同步；推荐新对话以避免旧工具定义和庞大历史继续占用上下文。旧配置备份可用于回退，未修改DSH安装包。

复现构建：在维护副本执行 `.tools/dotnet/dotnet.exe build src/NEngine.Mcp/NEngine.Mcp.csproj --no-restore -o artifacts/agent-mcp-001`。运行中的构建不可覆盖，换新目录并更新明确的--mcp-dll参数。测试环境变量见test_agent_dsh.mjs顶部。复现用户fixture仅本机提供FITLAB_AGENT_FIXTURE，未将用户装配提交入库。
