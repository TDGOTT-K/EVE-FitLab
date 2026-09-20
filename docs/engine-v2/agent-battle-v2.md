# NUI-111：从装配会话直接运行战斗

2026-09-21；agent-mcp-v2，7工具；引擎0.212.0-ui.battleentry1/r60，39原生工具。r59与agent-mcp-v1冻结文件保留。

## 修复的断点

真实DSH对话“你的MCP能干啥”共65次调用，其中56次MCP，35次读取battle_start schema，未创建装配、未启动战斗。旧facade把大schema切成小页，却仍让模型寻找如何从装配手工构造Scenario。已有EveBattleAssembler只通过CLI sde-battle暴露，MCP缺少桥梁。上一轮用合成现成Scenario验收不能覆盖这一断点。

新增原生只读battle_assemble(request)，直接调用EveBattleAssembler.Create，和CLI sde-battle同源。没有新增战斗公式、放宽准入或前端计算图生成。内部图、来源和哈希由引擎生成。

## Agent操作路径

1. fitlab_search一次搜索两船（categoryId:6），另一次搜索装备和弹药。
2. fitlab_fit create：sessionId、shipTypeId、skillPreset:"all5"。通过公开目录逐页读取所有已发布技能；默认仍未训练，明确选all5才启用，不能同时传skills。
3. fitlab_fit edit：revision、requestId及批量安装commands。
4. fitlab_battle prepare：id、seed、seconds、ships。每船填写sessionId、revision、id、team、position及reservePerWeapon，或者完整supplies。检查版本和引擎诊断，通过原生battle_assemble组装。不会启动战斗。
5. fitlab_battle start：draftId、jobId、policyPreset:"stationary-weapons-v1"，或显式policies按队伍提供JavaScript；二者不能同时提供。相同jobId和同一请求幂等重放。
6. fitlab_battle status：jobId、waitSeconds:10；最多等待10秒，减少模型轮询调用。result读取正式结果，events读取分页事件，cancel请求取消。

示例prepare的ships：

```json
[
  {"sessionId":"caracal","revision":1,"id":"regular","team":"red","position":{"x":0,"y":0,"z":0},"reservePerWeapon":30},
  {"sessionId":"navy","revision":1,"id":"navy","team":"blue","position":{"x":10000,"y":0,"z":0},"reservePerWeapon":30}
]
```

默认初始条件公开返回：满盾/甲/结构/电容，速度0，scan-signature-community-v1锁定政策；普通消耗弹药按引擎magazineCharges装满，每武器携带调用者声明的备用数量并自动装填。不是无限弹药。晶体、注电器、炸弹等需显式supplies和原生options；未知机制/静态库存映射等仍由引擎拒绝，不擅自转换或删除。position/velocity单位为m和m/s，seconds为仿真时域。options只能填写原生策略/比例参数，不能覆盖会话、船只、位置或补给绑定。

stationary-weapons-v1是公开研究控制策略：按ID选首个敌方舰船，锁定并发射常规炮/导弹；不移动、不启动维修/EWAR/无人机。有其他能力时拒绝使用该预设，要求显式脚本。prepare返回能力ID、初始条件、限制、可修改的完整脚本模板。不是游戏AI或通用最优策略。

draftId指向STATE/agent-battle-drafts的不可变内容哈希快照；start不重新读取变化后的装配。引擎/脚本runtime改变会要求重新prepare。战斗草稿不受128条普通查询缓存淘汰影响；普通结果仍保留最近128项。所有引用读取验证内容哈希。草稿没有自动清理策略，需要后续管理能力；不静默删用户准备的实验。

大对象分页现在同时返回小字段值和schema摘要，避免每个type/required字段单独调用。原生工具参数校验返回纯文本错误时保留NATIVE_TOOL_ERROR及原文，不再错误显示JSON解析异常。

## 验证

test_battle_agent_dsh.mjs只通过实际DSH插件完成：查询Caracal/Caracal Navy Issue及导弹装备→全V创建→双方各5门重导I→相距10km/静止/每门30发备用→引擎组装→30秒战斗→结果与事件。双方每门实际激活4次，读取了完成结果；包含相同jobId重放，attempt保持1。一次实测14次成功工具调用（含2次等待状态、额外重放和事件查询），0次schema浏览。不是模型自主完成深度研究的成功率承诺，也不能把单场对射当作舰船综合强度结论。

上述操作链完成后，测试才独立调用CLI sde-battle对同一request做逐字段对照，结果一致。验证还覆盖过期revision、空supplies、冲突policy选择、原生错误保留。test_agent_mcp覆盖冻结v2定义、持久草稿不被128项结果淘汰、引用重启、装配重试/撤销/保存。原生3项合同、3项工作台通过；DSH旧负零/CLI对照回归保留。

本机证据output/battle-agent-dsh-verification.json，不包含用户私人装配输入。测试未读取示例Scenario来代替装配组装。DSH profile备份后指向独立构建artifacts/battle-entry-001，原有装配会话保留；不自动续跑旧引擎检查点。默认构建和网页后端同步，不更新已冻结安装包、不发布。
