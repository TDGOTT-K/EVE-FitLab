import {numberAttributes} from './scenario-display.js';
import {panelReading,panelTip,panelAttributeTerm} from './native-panel-detail.js';
const fmt=n=>Number.isFinite(n)?n.toLocaleString('zh-CN',{maximumFractionDigits:1}):'—';
export function outputReading(selection){
 if(selection?.completeSelection&&Number.isFinite(selection.total))return fmt(selection.total);
 if(selection?.groups?.length===1)return fmt(selection.groups[0].subtotal)+'（部分）';
 return '—';
}
export function outputHtml(report,catalog=[]){
 const unit=report.attackMode==='edps'?'EDPS':'DPS',metric=report.outputSelection?.metric||'nominalCycleDps';
 const selected=new Set(report.outputContext?.selection?.contributionIds||[]),selectedItems=(report.native.outputContributions?.items||[]).filter(i=>selected.has(i.id));
 const name=i=>catalog.find(t=>t.id===i.source.typeId)?.name||i.source.instanceId;
 const terms=rows=>rows.map(i=>{const r=i.metrics[metric],title=name(i);return [title,r?.state==='available'?fmt(r.value)+' '+unit:'—',null,{title,result:r?.state==='available'?fmt(r.value)+' '+unit:'—',terms:(i.attributeKeys||[]).map(k=>panelAttributeTerm(k,report,catalog)),conditions:[['口径',metric],['状态',r?.state||'未知'],...(r?.reason?[['未计入原因',r.reason]]:[])]}];});
 const stat=(title,reading,rows,baseline)=>{
  const detail=panelReading(title,reading?.total,unit,terms(rows),[['范围','已选分项'],...(reading?.exclusions||[]).map(e=>['未计入',e.reason])]);detail.result=outputReading(reading);
  return '<div class="stat-row" '+panelTip(detail)+'><span>'+title+'</span><b '+numberAttributes(reading?.total,baseline)+'>'+outputReading(reading)+'</b></div>';
 };
 let html='<div class="stat-block">';
 html+=stat('武器 '+unit,report.outputBreakdown?.weapons,selectedItems.filter(i=>i.kind.startsWith('ship_')),report.scenarioTarget?report.baselineOutputBreakdown?.weapons?.total:undefined);
 html+=stat('无人机 '+unit,report.outputBreakdown?.drones,selectedItems.filter(i=>i.kind==='drone'),report.scenarioTarget?report.baselineOutputBreakdown?.drones?.total:undefined);
 const missing=(title,units,reason,rows=[])=>'<div class="stat-row" '+panelTip(panelReading(title,null,units,rows,[['不可用原因',reason]]))+'><span>'+title+'</span><b>—</b></div>';
 html+=missing('含换弹 '+unit,unit,'当前接入尚无同口径的完整换弹合计；不以名义输出替代。',selectedItems.map(i=>[name(i),i.metrics.reloadCycleDps?.state==='available'?fmt(i.metrics.reloadCycleDps.value)+' DPS':i.metrics.reloadCycleDps?.reason||'未提供']));
 const volley=report.legacyInspectorOutput?.volley;
 if(volley){const d=panelReading('齐射伤害 · DPH',volley.total,'HP',selectedItems.filter(i=>i.kind.startsWith('ship_')).map(i=>[name(i),fmt(i.metrics[report.legacyInspectorOutput.volleyMetric]?.value)+' HP']),[['范围','已选舰载武器齐射'],...(volley.exclusions||[]).map(e=>['未计入',e.reason])]);html+='<div class="stat-row" '+panelTip(d)+'><span>齐射伤害 · DPH</span><b>'+outputReading(volley)+(Number.isFinite(volley.total)?' HP':'')+'</b></div>';}else html+=missing('齐射伤害 · DPH','HP','尚未取得同口径的舰载武器齐射合计。');
 html+='<div class="damage-bars">'+['电磁','热能','动能','爆炸'].map((n,i)=>'<div class="damage-cell" '+panelTip(panelReading(n+'伤害占比',null,'%',[],[['不可用原因','尚未接入已选集合的同口径分类型DPS与比例。']]))+'><span class="damage-label">'+n+'</span><div class="mini-bar damage-'+i+'"><b>—</b></div><span class="damage-component"><b>—</b><small>'+unit+'</small></span></div>').join('')+'</div>';
 if(report.scenarioTarget)html+='<p class="profile-note">'+(unit==='EDPS'?'EDPS · 已扣目标抗性':'DPS · 不扣抗性')+' · 不含换弹</p>';
 return html+'</div>';
}
