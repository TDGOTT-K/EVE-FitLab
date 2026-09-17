# 深渊装备库第一轮

装备浏览器新增普通装备/深渊装备双视图。适用装备右键“在深渊库中寻找”清除槽位筛选、切换并展开原装备路径。深渊视图沿用装备路径及科技层级，原装备变成可展开节点，下面是独立实例草稿和创建入口。支持名称/备注搜索、编辑、复制、删除；两种视图分别保留搜索与滚动位置。

本轮实例仅保存 id、baseTypeId、name、notes、status=draft、revision、updatedAt，独立存于 library.abyssalInstances。GET /api/abyssal-instances；POST /api/abyssal-instance 与 /api/abyssal-instance/delete，使用 revision 防止并发覆盖。新实例明确标注待录入属性；暂不提供拖动安装、实例属性计算或虚构的变异数值。

候选原装备使用现有目录中包含 Abyssal 装备的 high/mid/low 物品组，并排除 Abyssal 类型自身。这是浏览器原型候选集，不代表已验证突变质体输入资格。下一轮必须引入准确的突变质体适用表、属性范围及完整 mutation 数据后，才能开启有效实例安装。

验证：Python 新建/唯一身份、改名、并发冲突无写入、原装备资格和名称校验；独立 5209 页面核对普通→深渊定位、创建、复制、改名、实例搜索、双视图搜索恢复、删除及刷新后本地数据保留。未修改旧计算引擎。
