import {getLocale} from './i18n.js';
export const escapeHtml=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
export const metricText=(value,unit='',digits=2)=>Number.isFinite(value)?Number(value).toLocaleString(getLocale(),{maximumFractionDigits:digits})+(unit?' '+unit:''):'—';
export function deltaClass(value,baseline){
 if(!Number.isFinite(value)||!Number.isFinite(baseline)||Math.abs(value-baseline)<=Math.max(1e-8,Math.abs(baseline)*1e-8))return '';
 return value>baseline?'scenario-increased':'scenario-decreased';
}
export function numberAttributes(value,baseline){
 const cls=deltaClass(value,baseline);return `class="${cls}"`+(Number.isFinite(value)?` data-value="${value}"`:'')+(Number.isFinite(baseline)?` data-baseline="${baseline}"`:'')+(cls?` title="${escapeHtml('无情景 '+metricText(baseline)+' → 当前 '+metricText(value))}"`:'');
}
export function scenarioDetail(title,result,value,baseline,terms=[],conditions=[],baselineText){
 return {title,result,resultClass:deltaClass(value,baseline),terms:[['无情景基准',baselineText??metricText(baseline)],...terms],conditions};
}
export const explanationAttributes=detail=>`tabindex="0" data-explain="${escapeHtml(JSON.stringify(detail))}"`;

export function renderWorkspaceAttack(report,catalog){
 const view=report.workspace,a=view.attack,b=view.baseline.attack;
 const comparison=key=>view.active?b[key]:undefined;
 const name=id=>catalog.find(t=>t.id===id)?.name;
 const weaponTerms=report.scenarioAnalysis.weapons.map(r=>[
  (name(r.typeId)||r.name)+(r.chargeTypeId?' · '+(name(r.chargeTypeId)||r.chargeTypeId):''),
  metricText(view.target?r.appliedDpsWithoutReload:r.baseDps,'DPS'),null,
  scenarioDetail(name(r.typeId)||r.name,metricText(view.target?r.appliedDpsWithoutReload:r.baseDps,'DPS'),view.target?r.appliedDpsWithoutReload:r.baseDps,view.active?r.baseDps:undefined,
   view.target?[['射程与运动应用系数',metricText(r.factor)],['换弹折算系数',metricText(r.reloadFactor)],['包含目标抗性','是']]:[],view.conditions)
 ]);
 function detail(key,title,unit,terms){
  return scenarioDetail(title,metricText(a[key],unit),a[key],comparison(key),terms,view.conditions,metricText(b[key],unit));
 }
 function row(key,title,unit,terms){return `<div class="stat-row" data-workspace-metric="${key}" ${explanationAttributes(detail(key,title,unit,terms))}><span>${title}</span><b ${numberAttributes(a[key],comparison(key))}>${metricText(a[key],unit,1)}</b></div>`;}
 let html=`<div class="panel-title"><span>攻击</span><span class="attack-total" data-workspace-metric="total" ${explanationAttributes(detail('total','总 DPS','DPS',weaponTerms.concat([['无人机',metricText(a.drone,'DPS')]])))}><b ${numberAttributes(a.total,comparison('total'))}>${metricText(a.total,'DPS',1)}</b></span></div><div class="stat-block">`;
 html+=row('weapon','武器 DPS','',weaponTerms)+row('drone','无人机 DPS','',[[view.target&&a.drone===null?'当前未支持':'范围',view.target&&a.drone===null?'缺少无人机自身运动与攻击应用模型':'已出动无人机']]);
 html+=row('reload','含换弹 DPS','',report.scenarioAnalysis.weapons.map(r=>[(name(r.typeId)||r.name),metricText(view.target?r.appliedDps:r.reloadDps,'DPS')]))+row('volley','齐射伤害 · DPH','HP',[[view.target?'口径':'范围',view.target?'目标抗性后的期望武器齐射伤害':'舰载武器齐射']]);
 if(a.profile){html+='<div class="damage-bars">'+['em','thermal','kinetic','explosive'].map((key,i)=>{const value=a.profile[key]??0,percent=a.total>0?value/a.total*100:0,base=b.profile?.[key],basePercent=b.total>0?(base??0)/b.total*100:0;return `<div class="damage-cell"><span class="damage-label">${['电磁','热能','动能','爆炸'][i]}</span><div class="mini-bar damage-${i}" role="progressbar" aria-label="${['电磁','热能','动能','爆炸'][i]}伤害占比" aria-valuenow="${percent}" aria-valuemin="0" aria-valuemax="100"><i style="width:${Math.max(0,Math.min(100,percent))}%"></i><b ${numberAttributes(percent,view.active?basePercent:undefined)}>${metricText(percent,'%',0)}</b></div><span class="damage-component"><b ${numberAttributes(value,view.active?base:undefined)}>${metricText(value,'',1)}</b><small>DPS</small></span></div>`}).join('')+'</div>';}
 if(view.target)html+='<p class="profile-note">目标抗性后 · 不含换弹；持续输出另列</p>';
 html+=view.issues.map(issue=>'<p class="scenario-coverage">'+escapeHtml(issue)+'</p>').join('');
 return html+'</div>';
}
