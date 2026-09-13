from pathlib import Path
p=Path('app.js');s=p.read_text(encoding='utf-8');s=s.replace("${s.online?'在线':'离线'} · ${t.attrs['50']||0} tf / ${t.attrs['30']||0} MW", "${s.online?'在线':'离线'}")
p.write_text(s,encoding='utf-8')
p=Path('README.md');p.write_text('''# EVE FitLab · 装配工作台

启动：在本目录执行 `python run.py`，打开 http://127.0.0.1:5207 。Python 3.9+、.NET 9 与已构建的 EdenOS.Hosts.Web 为当前依赖。

## 本轮完成

- 舰船市场分类树、中文/英文搜索、新建装配；普通舰船可选择。T3 子系统尚未接入，选择器明确禁用。
- 完整 SDE 目录中的舰船、高/中/低槽模块、改装件与弹药；按需展开市场树，科技/势力分类来自 SDE metaGroups。
- 保留拖装、兼容弹药过滤、批量装填、右键操作、改装件尺寸过滤及校准值显示。
- 独立装配库，名称、标签、舰船、槽位、弹药、在线状态和角色技能均保存。服务端原子写入、版本冲突检测；浏览器保留工作草稿。
- 选择无技能、全技能 V、EdenOS 已保存角色快照，或导入 ESI 技能 JSON。角色技能只复制到 FitLab 装配，不修改源角色。
- 接入真实 Dogma 验证结果：资源、攻击、防御、电容、机动与锁定。计算结果仅对发起时的当前版本生效。

## 数据与计算

`state/library.json` 是 FitLab 独立装配/角色存储，请备份整个 state 目录。EdenOS 源工作区只读，不迁移密钥或授权令牌。计算服务独立运行在 5210，使用 state/engine，不覆盖原项目状态。

`FITLAB_ENGINE_REPO` 可指定 EdenOS 代码目录（默认 D:/IT/EVE/EdenOsRewrite）；`FITLAB_CHARACTER_SOURCE` 可指定源工作区快照文件。当前角色读取目标是先前使用的 EdenOS 工作区。

`build-catalog.py` 从本机 SDE 3248221 重新生成完整目录及市场图标映射。目录共 412 艘舰船、1386 高槽、904 中槽、1005 低槽、642 改装件、1015 弹药、505 技能；目录数量不代表对当前舰船全部兼容。

## 验证及边界

浏览器已验证：裂谷级应用全技能 V 后 CPU 130 → 162.5；新建刺客级、装炮/中型弹药、应用技能、保存、刷新、装配库重新打开，字段保持。服务端测试覆盖磁盘往返、旧版本冲突、无效技能拒绝、私有状态不作为静态文件公开。

运行测试：`python -m unittest test_server.py -v`。

当前引擎仍复用 EdenOS，尚未独立打包。未接入 T3 子系统、无人机、植入体、增效剂与战斗实验室。EHP 按四类伤害各 25% 计算；电容稳定性暂不作可信结论。逐项 Dogma 加成溯源尚未接入，提示中的“技能与装备合计”是装配后数值与舰船基础值的差额。导弹换弹 DPS 仍受现有引擎限制，界面提示已注明。
''',encoding='utf-8')
