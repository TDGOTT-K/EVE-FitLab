# 公开装配估价

本地接口 0.190.5-ui.1 / r38 新增只读 MCP `fit_valuation` 和 CLI `sde-fit-valuation`。不新增游戏机制，不运行装配准入；估价不能代替装配合法性校验。

## 无头复现

1. `python market_prices.py --snapshot --out prices.json` 导出 ESI Tranquility average_price 快照。保留供应者、URL、抓取时间、HTTP Date/Expires、原始响应 SHA256、缓存状态和失败原因。
2. POST `/api/fit-valuation`，输入 `{ "fit": 当前UI装配 }`。返回 `valuation` 及精确的 `request.fit`、`request.snapshot`，后者可直接用于公开接口重放。
3. MCP `fit_valuation` 使用该 fit 和 snapshot；CLI 使用 `sde-fit-valuation --data 固定SDE目录 --fit fit.json --snapshot snapshot.json --out valuation.json`。相同输入返回相同分项、数量、单价、小计、完整性和总量。

UI仅负责数值格式化。舰船、装备、子系统、全部无人机、舰载机成员、脑插、药剂及声明库存均计入；晶体按实际实体计数。未声明装弹量不推定满仓。缺报价、未知库存、深渊实例独立定价缺失均保留原因；无可用报价显示不可用，不显示0。真实零报价仍有效。深渊实例不拿普通装备价格替代。

ESI average_price不是即时采购价，也不是吉他卖单。缓存一小时；刷新失败保留明确标识的过期快照，无缓存则不可用。引擎不联网，也不认证调用者提供的行情真实性；结果绑定输入快照哈希。暂不接入深渊实例人工估价和实时订单簿。

验证：4项引擎估价专项、3项公开契约；Python3项测试含6组完整CLI/MCP结果对照（完整、未知数量、缺报价、真实零、深渊、过期）；JS显示测试及57模块检查通过。真实ESI读取15801项报价。5352隔离UI从未知弹量的部分估价3,409,793 ISK改为声明120发后的完整估价3,412,934 ISK，分享图1080×5855显示同值与来源。未修改5208用户装配库，未跑全量或重打包Windows。
