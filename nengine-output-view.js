import {numberAttributes} from './scenario-display.js';
// Only formats engine readings/reducer states; no damage formulas.
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const fmt=n=>Number.isFinite(n)?n.toLocaleString('zh-CN',{maximumFractionDigits:2}):'—';
const states={available:'可用',requires_policy:'需要使用策略',requires_target:'缺少目标条件',requires_combat_record:'需要战斗记录',not_applicable:'不适用',unsupported:'尚未支持',blocked:'计算受限'};
const reasons={FINITE_ABILITY_USE_LOADED_CYCLE_BASIS:'有限弹量武器，请使用有限弹量周期口径',TIME_SUPPLY_CAPACITOR_AND_USE_POLICY_REQUIRED:'缺少时间、补给、电容与使用策略',METRIC_NOT_APPLICABLE:'此武器不适用该口径',BATTLE_DAMAGE_LEDGER_AND_TIME_WINDOW_REQUIRED:'需要实际战斗记录',EXCLUDED_BY_DISPLAY_SELECTION:'未选择'};
export function outputReading(selection){
 if(selection?.completeSelection&&Number.isFinite(selection.total))return fmt(selection.total);
 if(selection?.groups?.length===1)return fmt(selection.groups[0].subtotal)+'（部分）';
 return '—';
}
export function outputHtml(report,catalog){
 const selection=report.outputSelection,output=report.native.outputContributions,metric=selection?.metric||'nominalCycleDps',basis=report.baselineOutputSelection?.metric||metric;
 if(!output)return '<div class="stat-block">输出接口不可用</div>';
 const name=item=>{const type=catalog.find(t=>t.id===item.source.typeId),ability=report.native.fighterEntities[item.source.squadronId]?.abilityMetadata?.abilities?.find(a=>a.abilityId===item.source.officialAbilityId);return (type?.name||report.native.fighterEntities[item.source.squadronId]?.abilityMetadata?.name?.zh||report.native.fighterEntities[item.source.squadronId]?.abilityMetadata?.name?.en||item.source.instanceId)+(ability?' · '+(ability.displayName.zh||ability.displayName.en):'');};
 const selected=new Set(report.outputContext.selection.contributionIds),excluded=new Map((selection.exclusions||[]).map(x=>[x.contributionId,x]));
 let html='<div class="stat-block"><select class="native-output-metric" aria-label="输出计算口径"><option value="nominalCycleDps" '+(basis==='nominalCycleDps'?'selected':'')+'>名义周期 DPS</option><option value="loadedCycleDps" '+(basis==='loadedCycleDps'?'selected':'')+'>有限弹量周期 DPS</option></select>';
 for(const [key,label] of [['weapons','武器'],['drones','无人机'],['fighters','舰载机已选武器']]){
  const value=report.outputBreakdown[key];
  if(value.groups.length||value.exclusions.length)html+='<div class="stat-row"><span>'+label+' DPS</span><b '+numberAttributes(value.total,report.scenarioTarget?report.baselineOutputBreakdown[key].total:undefined)+'>'+outputReading(value)+'</b></div>';
 }
 const explanation=selection.status==='empty_selection'?(output.staticBlockers.length?'计算受限，未取得可选分项':'没有已选输出项'):selection.completeSelection?'仅汇总已选武器':'仅显示可计算小计';
 html+='<small class="profile-note">'+explanation+(report.scenarioTarget?' · 已应用情景':'')+' · 不扣抗性'+(basis==='loadedCycleDps'?' · 假定可发射，非持续输出':'')+'</small>';
 html+='<details class="native-output-details"><summary>输出分项 <span>'+selected.size+' / '+output.items.length+'</span></summary>';
 for(const item of output.items){
  const reading=item.metrics[metric],missing=excluded.get(item.id),chosen=selected.has(item.id),reason=missing?.reason||reading?.reason;
  const status=chosen?(missing?(reasons[reason]||states[reading?.state]||reason):'已计入'):item.source.deployed===false?'待命 · 不计入':'未选择';
  html+='<div class="native-output-item"><span>'+esc(name(item))+'</span><b '+numberAttributes(reading?.state==='available'?reading.value:null,report.scenarioTarget?report.baselineOutputItems.find(x=>x.id===item.id)?.metrics[basis]?.value:undefined)+'>'+fmt(reading?.state==='available'?reading.value:null)+'</b><small title="'+esc(reason||'')+'">'+esc(status)+'</small></div>';
 }
 html+='</details></div>';
 return html;
}
