const operations={'-1':'前置赋值',0:'前置乘法',1:'前置除法',2:'加法',3:'减法',4:'后置乘法',5:'后置除法',6:'百分比',7:'后置赋值'};
export function nativeAttributeDetail(readout,inspection,label,format,catalog){
 const trace=readout.trace,available=['available','available_declared_default'].includes(readout.state)&&Number.isFinite(trace?.value);
 const source=id=>id==='mode'?'舰体模式':id?.startsWith('skill.')?(catalog.find(t=>t.id===Number(id.slice(6)))?.name||id):id;
 const state={unavailable:'不可用',requires_policy:'未声明默认值策略',blocked_static_dependencies:'依赖尚未支持的效果'}[readout.state]||readout.state;
 const detail={title:label,result:available?format(trace.value):'—',terms:available?[
  ['基础值',format(trace.baseValue)],
  ...trace.steps.map(s=>[source(s.sourceId),format(s.before)+' → '+format(s.after),null,{title:source(s.sourceId),result:format(s.after),
   terms:[['操作',operations[s.operation]??String(s.operation)],['来源修正值',String(s.sourceValue)],['堆叠系数',String(s.penalty)]],
   conditions:[['效果 ID',String(s.effectId)],['修正记录',s.modifierId]]}])
 ]:[],conditions:[['状态',available?'可用':state],...(readout.reason?[['原因',readout.reason]]:[]),
  ['对象',readout.query.itemId],['属性 ID',String(readout.query.attributeId)],['来源','N 号引擎 · '+inspection.ruleVersion],...(trace?[['基础值来源',trace.origin]]:[])]};
 let direction='';const high=readout.metadata?.definition?.highIsGood;
 if(available&&typeof high==='boolean'&&trace.value!==trace.baseValue)direction=(high?trace.value>trace.baseValue:trace.value<trace.baseValue)?'value-improved':'value-worsened';
 return {value:available?format(trace.value):'— · '+state,detail,direction};
}
