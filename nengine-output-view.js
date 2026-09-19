import {numberAttributes} from './scenario-display.js';
import {panelReading,panelTip,panelAttributeTerm} from './native-panel-detail.js';
const fmt=n=>Number.isFinite(n)?n.toLocaleString('zh-CN',{maximumFractionDigits:1}):'—';
const reasonText=r=>({INSTALLED_CRYSTAL_NOT_CONSUMABLE_MAGAZINE:'当前晶体不是消耗式弹仓，未提供换弹投影',ZERO_BASELINE:'总输出为零，伤害比例无定义',DPS_METRIC_REQUIRED:'当前选择不是每秒伤害口径',FINITE_ABILITY_USE_LOADED_CYCLE_BASIS:'有限次数武器不能按无限续射处理',RELOAD_PROJECTION_UNAVAILABLE:'换弹投影尚不可用',STATIC_OUTPUT_BLOCKERS:'部分装备静态效果尚不完整'}[r]||r);
export function outputReading(selection){
 if(selection?.completeSelection&&Number.isFinite(selection.total))return fmt(selection.total);
 if(selection?.groups?.length===1)return fmt(selection.groups[0].subtotal)+'（部分）';
 return '—';
}
export function displayOutputSelection(report,reading,rows){
 // Empty sums are a display convention, gated by the public inspector's
 // complete typed damage result. Never turn unavailable contributions into 0.
 const damage=report.native?.inspector?.damagePerSecond;
 const known=damage&&['em','thermal','kinetic','explosive'].every(k=>damage[k]?.state==='available'&&Number.isFinite(damage[k].value));
 if(reading&&known&&Array.isArray(rows)&&rows.length===0&&reading.groups?.length===0&&reading.exclusions?.length===0&&report.native?.outputContributions?.staticBlockers?.length===0)
  return {...reading,total:0,completeSelection:true,displayConvention:'empty_selected_sum'};
 return reading;
}
export function outputHtml(report,catalog=[]){
 const unit=report.attackMode==='edps'?'EDPS':'DPS',metric=report.outputSelection?.metric||'nominalCycleDps';
 const selected=new Set(report.outputContext?.selection?.contributionIds||[]),selectedItems=(report.native.outputContributions?.items||[]).filter(i=>selected.has(i.id));
 const name=i=>catalog.find(t=>t.id===i.source.typeId)?.name||i.source.instanceId;
 const terms=rows=>rows.map(i=>{const r=i.metrics[metric],title=name(i);return [title,r?.state==='available'?fmt(r.value)+' '+unit:'—',null,{title,result:r?.state==='available'?fmt(r.value)+' '+unit:'—',terms:(i.attributeKeys||[]).map(k=>panelAttributeTerm(k,report,catalog)),conditions:[['口径',metric],['状态',r?.state||'未知'],...(r?.reason?[['未计入原因',r.reason]]:[])]}];});
 const stat=(title,reading,rows,baseline)=>{
  reading=displayOutputSelection(report,reading,rows);
  const detail=panelReading(title,reading?.total,unit,terms(rows),[['范围','已选分项'],...(reading?.exclusions||[]).map(e=>['未计入',e.reason])]);detail.result=outputReading(reading);
  return '<div class="stat-row" '+panelTip(detail)+'><span>'+title+'</span><b '+numberAttributes(reading?.total,baseline)+'>'+outputReading(reading)+'</b></div>';
 };
 let html='<div class="stat-block">';
 html+=stat('武器 '+unit,report.outputBreakdown?.weapons,selectedItems.filter(i=>(i.kind.startsWith('ship_')||i.kind==='smartbomb')),report.scenarioTarget?report.baselineOutputBreakdown?.weapons?.total:undefined);
 html+=stat('无人机 '+unit,report.outputBreakdown?.drones,selectedItems.filter(i=>i.kind==='drone'),report.scenarioTarget?report.baselineOutputBreakdown?.drones?.total:undefined);
 const missing=(title,units,reason,rows=[])=>'<div class="stat-row" '+panelTip(panelReading(title,null,units,rows,[['不可用原因',reason]]))+'><span>'+title+'</span><b>—</b></div>';
 const inspector=report.native.inspector,reload=inspector?.reloadDps;
 if(reload){const d=panelReading('含换弹 '+unit,reload.value,unit,[],[['口径','满弹仓、无限备弹；固定初始预热'],['范围','已选输出集合；不计晶体磨损'],...(reload.reason?[['未计入',reasonText(reload.reason)]]:[])]);html+='<div class="stat-row" '+panelTip(d)+'><span>含换弹 '+unit+'</span><b>'+fmt(reload.value)+'</b></div>';}
 else html+=missing('含换弹 '+unit,unit,'当前引擎未提供汇总接口');
 const volley=displayOutputSelection(report,report.legacyInspectorOutput?.volley,selectedItems.filter(i=>i.kind.startsWith('ship_')||i.kind==='smartbomb'));
 if(volley){const d=panelReading('齐射伤害 · DPH',volley.total,'HP',selectedItems.filter(i=>(i.kind.startsWith('ship_')||i.kind==='smartbomb')).map(i=>[name(i),fmt(i.metrics[report.legacyInspectorOutput.volleyMetric]?.value)+' HP']),[['范围','已选舰载武器齐射'],...(volley.exclusions||[]).map(e=>['未计入',e.reason])]);html+='<div class="stat-row" '+panelTip(d)+'><span>齐射伤害 · DPH</span><b>'+outputReading(volley)+(Number.isFinite(volley.total)?' HP':'')+'</b></div>';}else html+=missing('齐射伤害 · DPH','HP','尚未取得同口径的舰载武器齐射合计。');
 html+='<div class="damage-bars">'+['em','thermal','kinetic','explosive'].map((key,i)=>{const n=['电磁','热能','动能','爆炸'][i],v=inspector?.damagePerSecond?.[key],r=inspector?.damageFractions?.[key],percent=Number.isFinite(r?.value)?r.value*100:r?.reason==="ZERO_BASELINE"&&v?.state==="available"&&v.value===0?0:null;return '<div class="damage-cell" '+panelTip(panelReading(n+'伤害占比',percent,'%',[[n+' '+unit,fmt(v?.value)]],[...(v?.reason?[['数值不可用',v.reason]]:[]),...(r?.reason?[['比例不可用',r.reason]]:[])]))+'><span class="damage-label">'+n+'</span><div class="mini-bar damage-'+i+'">'+(Number.isFinite(percent)?'<i style="width:'+Math.max(0,Math.min(100,percent))+'%"></i>':'')+'<b>'+(Number.isFinite(percent)?percent.toFixed(0)+'%':'—')+'</b></div><span class="damage-component"><b>'+fmt(v?.value)+'</b><small>'+unit+'</small></span></div>';}).join('')+'</div>';
 if(report.scenarioTarget)html+='<p class="profile-note">'+(unit==='EDPS'?'EDPS · 已扣目标抗性':'DPS · 不扣抗性')+' · 不含换弹</p>';
 return html+'</div>';
}
