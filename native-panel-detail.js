// Explain existing engine traces without re-evaluating Dogma formulas.
const fmt=(v,u='')=>Number.isFinite(v)?v.toLocaleString('zh-CN',{maximumFractionDigits:3})+(u?' '+u:''):'—';
export function sourceName(id,report,catalog=[]){
 if(id==='ship')return catalog.find(t=>t.id===report.nativeFit?.shipTypeId)?.name||'舰船';
 if(id==='mode')return '舰体模式';
 if(id?.startsWith('skill.'))return catalog.find(t=>t.id===Number(id.slice(6)))?.name||id;
 const key=id?.replace(/^module\./,''),item=report.nativeFit?.items?.find(x=>x.id===key);
 return catalog.find(t=>t.id===item?.typeId)?.name||id||'来源';
}
export function panelTrace(trace,title,unit,report,catalog=[],scale=1,depth=0){
 if(!trace)return {title,result:'—',terms:[],conditions:[['状态','当前未提供可用属性记录']]};
 const value=n=>fmt(Number.isFinite(n)?n/scale:null,unit);
 const operations={'-1':'前置赋值',0:'前置乘法',1:'前置除法',2:'加法',3:'减法',4:'后置乘法',5:'后置除法',6:'百分比',7:'后置赋值'};
 return {title,result:value(trace.value),terms:[['基础值',value(trace.baseValue)],...(trace.steps||[]).filter(s=>s.before!==s.after).map(s=>{
  const name=sourceName(s.sourceId,report,catalog);
  const skill=s.sourceId?.startsWith('skill.')?report.nativeFit?.skills?.[s.sourceId.slice(6)]:undefined;
  const sourceKey=(trace.dependencies||[]).find(k=>k.startsWith(s.sourceId+'/'));
  const sourceTrace=sourceKey?report.native?.attributes?.[sourceKey]:null;
  const sourceDetail=sourceTrace&&depth<2?panelTrace(sourceTrace,name+' · 修正值','',report,catalog,1,depth+1):{title:name+' · 修正值',result:fmt(s.sourceValue),terms:skill!==undefined?[['技能等级',String(skill)]]:[],conditions:[['来源','原生属性求值']]};
  const nested={title:name,result:value(s.after),terms:[['执行前',value(s.before)],['来源修正值',fmt(s.sourceValue),null,sourceDetail],['叠加惩罚系数',fmt(s.penalty)],['实际运算',operations[s.operation]??String(s.operation)]],conditions:skill!==undefined?[['来源状态','技能 '+skill+' 级']]:[['来源','装备属性修正']]};
  return [name+(skill!==undefined?' '+skill+' 级':''),value(s.before)+' → '+value(s.after),'source',depth<3?nested:null];
 })],conditions:[['角色',report.curveRequest?.characterName||'当前技能快照'],['来源','N '+report.engineVersion+' · 属性执行记录']]};
}
export function panelReading(title,value,unit,terms=[],conditions=[]){return {title,result:fmt(value,unit),terms,conditions};}
export function panelAttributeTerm(key,report,catalog=[]){
 const trace=report.native.attributes[key];
 const labels={4:['质量','t',1000],9:['结构血量','HP'],30:['能量栅格需求','MW'],37:['最大速度','m/s'],50:['CPU 需求','tf'],54:['最佳射程','km',1000],55:['回充时间','s',1000],64:['伤害倍率',''],70:['惯性系数',''],73:['周期','s',1000],114:['电磁伤害','HP'],116:['爆炸伤害','HP'],117:['动能伤害','HP'],118:['热能伤害','HP'],158:['失准距离','km',1000],160:['跟踪速度',''],263:['护盾容量','HP'],265:['装甲血量','HP'],482:['电容容量','GJ'],552:['信号半径','m']};
 const [label,unit,scale=1]=labels[trace?.attributeId]||[trace?.name||key,''];
 const detail=panelTrace(trace,label,unit,report,catalog,scale);
 return [label,detail.result,null,detail];
}
export const panelTip=detail=>'tabindex="0" data-explain="'+JSON.stringify(detail).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]))+'"';
