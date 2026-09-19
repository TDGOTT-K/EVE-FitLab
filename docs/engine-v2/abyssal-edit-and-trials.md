# 深渊实例编辑与随机试验

2026-09-20，浏览器开发，未打包。引擎独立副本0.210.5-ui.mutation1 / r58-ui-mutation1，37工具；本地补丁LOCAL-014。UI验收NUI-90。

## 使用

装备浏览器→进入深渊→原装备下创建实例，或右键现有实例→编辑实例。选择适用突变质体后，标题旁切换到“编辑”模式，在允许范围内拖动滑条编辑属性；默认“正常”模式只读显示。应用编辑由引擎校验；保存时未应用的有效编辑也会自动校验。保存实例可安装，列表标明“手动编辑”，不伪造随机生成凭据。

合格条件勾选项须全部满足；默认达到编辑值或更好，方向由引擎声明。未知方向禁用该模式，改填范围。上下限包含端点；可取消勾选，全部不选表示任意结果均合格。

计算期望次数显示单次概率与几何分布平均次数，不是保底。使用现有社区独立均匀/53位采样模型，未声称CCP官方随机分布。不可达为0并解释；极低概率/期望溢出不伪造零。

Roll至合格每次真实采样，在首个满足全部条件的结果处停止。每批500次，API最多1000；批间归还界面事件循环。停止等待当前批次回执，随后可继续原种子和位置；更改条件/编辑值会开启新试验。关闭窗口停止继续发批，已在途的只读批次可能执行完毕。没有无限长阻塞后端请求，也不通过目标值反向制造结果。命中实例保留正常generationReceipt，可保存和安装。

## 公开接口

MCP：mutation_workbench，参数request对象。
CLI：sde-mutation-workbench --data DIRECTORY --request FILE。
HTTP适配：POST /api/mutation-workbench，正文直接为request。

公共字段：baseTypeId、mutaplasmidTypeId、operation。
- edit：values为完整attributeId→绝对原生值字典；结果edit包含rule、mutation、比较rows和manual来源。
- estimate：criteria数组，每项attributeId；mode=atLeastAsGood搭配value，或mode=range搭配minimum/maximum。
- trial：同criteria，seed可选；startAttempt默认0；attempts默认500、范围1～1000。读取trial.seed/nextAttempt续跑。found=true时match是可用mutation_roll独立重放的生成凭据。

示例：{"baseTypeId":526,"mutaplasmidTypeId":47699,"operation":"estimate","criteria":[{"attributeId":50,"mode":"range","minimum":23.75,"maximum":27.5}]}。

输入输出单位及源上下限在rule.attributes，概率分项在chance.attributes；probability、expectedAttempts、logProbability及state/reason全部引擎提供，UI不计算概率。界面只做原生单位与展示单位互换、格式化、范围条位置。

editReceipt在保存时经公开引擎重新校验且逐字段对照；编辑来源和随机来源不可混用。原有随机generationReceipt校验保留。

## 验证

引擎14项回归、3项契约测试，5组实际CLI/MCP完整结果对照：edit、estimate、无约束、不可达、trial；命中seed独立复算一致。恒定范围概率1、未知改善方向拒绝、半区间联合概率、分批/整批首命中一致均覆盖。
UI：6项原有变异/实例回归；新增手动保存、重命名、实际装配CPU数值与篡改拒绝测试；72模块检查。
浏览器：创建/编辑、手动保存并重新打开、随机首命中、停止/继续、不可达条件。四项中间值实测6.25%/16次；独立测试实例已删除。截图output/abyssal-editor-final.png。用户最终布局与交互待验收。

下次升级必须保留或上游合并LOCAL-014，不能用r57副本直接覆盖当前MCP能力。主引擎及rc.1/rc.2冻结包未修改。
