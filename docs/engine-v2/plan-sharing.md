# 脑插与增效剂方案分享 v1

文本头为 FITLAB-PLAN/1，随后是UTF-8 JSON。自定义软件格式，不是EVE官方格式。

document字段：
- format: EVE-FitLab-Plan；version: 1。
- source: buildNumber、indexSha256、staticRule，以及生成时engineVersion/revision。
- itemNames: Type ID到SDE名称的阅读辅助映射，不作为身份或计算依据。
- plan: name、implants[{typeId,slot}]、boosters[{typeId,slot,enabledSideEffects}]、可选pilot{匿名名称,skills[{skillTypeId,level}]}及已通过引擎校验的rollReceipt。
- nativePlanHash: 原生booster_plan_summary返回的planHash。导入同输入重算并核对。名称不参与原生数值输入。

导出过滤本地id、revision、folder、时间、本地链接及官网身份/实际SP快照。计算技能保留，角色名称改为“分享技能快照”。条目使用稳定Type ID及Effect ID；界面名称不能替代身份。导入比对SDE/索引/静态规则，生成最外层新方案，不覆盖已有记录。数据版本不同不做静默升级。原生角色条件或覆盖诊断保留，分享成功不意味着装配准入。

图片：PNG展示物品图标、名称、槽位、当前副作用、引擎词条、技能项数及版本来源；附完整数据QR。FPLAN1:<压缩数据SHA256>:<1-based分片编号>:<总片数>:<gzip JSON的Base64分片>。每片最多600字符，最多24片；解压上限200KB。二维码按整数原始像素输出，避免缩放损失。缺片、混合不同方案、冲突/损坏均拒绝。与装配EVFB/EVFL码区分，不改既有装配格式。

公开HTTP：POST /api/plan-share/export {plan}；POST /api/plan-share/import {document}，后者仅校验并返回新方案数据，保存继续调用已有/api/loadout-plan。数值与原生哈希来自booster_plan_summary；回执验证来自booster_plan_verify，两者均有公开CLI。Python plan_share只做字段过滤、来源匹配和元数据槽位校验，不定义游戏公式。

入口：编辑方案“更多→分享文字/分享图片”；方案库“导入”。文本可复制/保存；图片可复制/保存PNG。导入支持粘贴文本、选择文件、拖图、粘贴截图，多码可分批补齐。修改文本会清除上次校验结果。

验证：4项真实引擎Python往返/独立保存/版本及槽位/损坏拒绝；JS Unicode/CRLF、多片乱序重复、缺片混片损坏；既有装配分享回归；真实浏览器生成PNG后扫码校验及文字预览。output/plan-share-final.png（隔离测试方案，1084×2556）。未自动发送分享内容给他人。
