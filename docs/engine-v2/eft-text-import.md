# EFT 文本导入

入口：装配库“从文本导入”，或工作台“导入文本”。粘贴文本或选择 .txt/.eft 文件，选择计算角色，点击“解析并检查”，确认后“导入为新装配”。每次一份，不覆盖已有记录。

采用常用 EFT 语法：

```text
[Rifter, 示例装配]
Gyrostabilizer II
[Empty Low slot]

1MN Afterburner II /offline

200mm AutoCannon II, EMP S

Hobgoblin II x5

EMP S x1000
```

支持当前固定 SDE 的中英文及其他官方本地化物品名、空槽、装备和弹药、/offline、数量、无人机、舰载机备用库存、Pyfa 脑插/增效剂条目。物品名只精确匹配（忽略大小写与Unicode宽度），不以模糊匹配替换装备。子系统按来源槽位定义放置。x数量的普通物品按货舱解释，数量不会被视作额外已安装模块。

EFT未描述技能、具体弹仓剩余量、晶体损伤、无人机/舰载机出动选择、增效剂副作用。界面要求选择计算角色（默认全技能V）；主动模块默认Active，被动模块Online，显式/Offline保留；无人机/舰载机默认备用；副作用默认未选。未声明弹量与损伤不填满弹或全新。所有假设在导入预览说明，计算仍调用当前N引擎；条件不全时可保存草稿，但不声称已合法。

不认识、歧义或不支持的行整体阻止导入，列出原文与行号，不静默跳过。当前不支持XML、DNA、多装配批量粘贴或Pyfa深渊变异附表；当前货舱目录不支持的物品也明确报错。这些是格式/宿主输入支持范围，不代表物品在游戏中不合法。

格式研究（2026-09-18）：
- Pyfa公开EFT导入/导出实现：https://github.com/pyfa-org/Pyfa/blob/master/service/port/eft.py
- 下载核对的源文件：https://raw.githubusercontent.com/pyfa-org/Pyfa/master/service/port/eft.py
- 确认格式头、低中高/改装件/子系统顺序、空槽、离线后缀、无人机/舰载机及货舱数量、脑插/药剂独立行。
- 本实现独立编写，只参考格式行为；未复制Pyfa实现代码。
- EVE University页面及搜索引擎访问超时，未将未读页面当作已核验来源。
