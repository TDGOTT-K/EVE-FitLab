import {deferDetail} from './deferred-details.js';
import {getLocale,formatMetric,gameName} from './i18n.js';
// Explain existing engine traces without re-evaluating Dogma formulas.
const fmt=(v,u='')=>formatMetric(v,u,{maximumFractionDigits:3});
export function sourceName(id,report,catalog=[]){
 if(id==='ship')return (catalog.find(t=>t.id===report.nativeFit?.shipTypeId)?gameName(catalog.find(t=>t.id===report.nativeFit.shipTypeId)):null)||'舰船';
 if(id==='mode')return '舰体模式';
 if(id?.startsWith('skill.'))return (catalog.find(t=>t.id===Number(id.slice(6)))?gameName(catalog.find(t=>t.id===Number(id.slice(6)))):null)||id;
 const key=id?.replace(/^module\./,''),item=report.nativeFit?.items?.find(x=>x.id===key);
 return (catalog.find(t=>t.id===item?.typeId)?gameName(catalog.find(t=>t.id===item.typeId)):null)||id||'来源';
}
export function panelTrace(trace,title,unit,report,catalog=[],scale=1,depth=0){
 if(!trace)return {title,result:'—',terms:[],conditions:[['状态','当前未提供可用属性记录']]};
 const value=n=>fmt(Number.isFinite(n)?n/scale:null,unit);
 if(report.nativeDetailMode?.startsWith('values_only')){
  const key=JSON.stringify([report.native.fitHash,trace.key,title,unit,scale,report.curveRequest?.characterName,globalThis.document?.documentElement?.lang||'zh-CN']);
  const deferred=deferDetail(key,async()=>{
   const slash=trace.key.lastIndexOf('/');
   const response=await fetch('/api/native-attributes',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({fit:report.curveRequest,itemId:trace.key.slice(0,slash),attributeIds:[Number(trace.key.slice(slash+1))]})});
   const data=await response.json();if(!response.ok)throw Error(data.error?.message||data.error||'属性读取失败');
   const inspection=data.inspection;if(inspection.fitHash!==report.native.fitHash)throw Error('装配快照已变化，请重新打开详情');
   return panelTrace(inspection.traces[trace.key],title,unit,{...report,nativeDetailMode:'full',native:{...report.native,attributes:inspection.traces}},catalog,scale,depth);
  });
  return {title,result:value(trace.value),terms:[],conditions:[['计算过程','正在读取完整计算过程…']],deferred};
 }
 const operations={'-1':'前置赋值',0:'前置乘法',1:'前置除法',2:'加法',3:'减法',4:'后置乘法',5:'后置除法',6:'百分比',7:'后置赋值'};
 return {title,result:value(trace.value),terms:[['基础值',value(trace.baseValue)],...(trace.steps||[]).filter(s=>s.before!==s.after).map(s=>{
  const name=sourceName(s.sourceId,report,catalog),typeId=s.sourceId==='ship'?report.nativeFit?.shipTypeId:s.sourceId?.startsWith('skill.')?Number(s.sourceId.slice(6)):report.nativeFit?.items?.find(x=>x.id===s.sourceId?.replace(/^module\./,''))?.typeId;
  const skill=s.sourceId?.startsWith('skill.')?report.nativeFit?.skills?.[s.sourceId.slice(6)]:undefined;
  const sourceKey=(trace.dependencies||[]).find(k=>k.startsWith(s.sourceId+'/'));
  const sourceTrace=sourceKey?report.native?.attributes?.[sourceKey]:null;
  const sourceDetail=sourceTrace&&depth<2?panelTrace(sourceTrace,name+' · 修正值','',report,catalog,1,depth+1):{title:name+' · 修正值',result:fmt(s.sourceValue),terms:skill!==undefined?[['技能等级',String(skill)]]:[],conditions:[['来源','原生属性求值']]};
  if(typeId){sourceDetail.titleTypeId=typeId;sourceDetail.titleSuffix=' · 修正值';}
  const nested={title:name,titleTypeId:typeId,result:value(s.after),terms:[['执行前',value(s.before)],['来源修正值',fmt(s.sourceValue),null,sourceDetail],['叠加惩罚系数',fmt(s.penalty)],['实际运算',operations[s.operation]??String(s.operation)]],conditions:skill!==undefined?[['来源状态','技能 '+skill+' 级']]:[['来源','装备属性修正']]};
  return [name+(skill!==undefined?' '+skill+' 级':''),value(s.before)+' → '+value(s.after),'source',depth<3?nested:null,{typeId,skillLevel:skill}];
 })],conditions:[['角色',report.curveRequest?.characterName||'当前技能快照',report.curveRequest?.characterName?'user':null],['来源','N '+report.engineVersion+' · 属性执行记录']]};
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
