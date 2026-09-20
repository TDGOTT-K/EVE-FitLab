import {patchHtml} from './dom-patch.js';
import {panelTrace,panelReading,panelTip,panelAttributeTerm} from './native-panel-detail.js';
import {scaleReading} from './analysis-status.js';
import {capacitorHtml} from './native-capacitor-view.js';
import {numberAttributes,deltaClass} from './scenario-display.js';
import {outputHtml,outputReading,displayOutputSelection} from './nengine-output-view.js';
// Native report presenter: formatting and layout only; values belong to NEngine.
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const fmt=(n,u='')=>Number.isFinite(n)?n.toLocaleString('zh-CN',{maximumFractionDigits:2})+(u?' '+u:''):'—';
export function nativeDetail(trace,title,unit='',report){
 if(report?.nativeDetailMode?.startsWith('values_only'))return panelTrace(trace,title,unit,report);
 if(!trace)return null;
 const source=id=>id==='mode'?'舰体模式':id?.startsWith('skill.')?'技能 '+id.slice(6):id?.startsWith('module.')?'装备 '+id.slice(7):id||'修正';
 return {title,result:fmt(trace.value,unit),terms:[['基础值',fmt(trace.baseValue,unit)],...(trace.steps||[]).filter(s=>s.before!==s.after).map(s=>[source(s.sourceId),fmt(s.before)+' → '+fmt(s.after),null,{title:source(s.sourceId),result:fmt(s.after,unit),terms:[['来源修正值',fmt(s.sourceValue)],['叠加系数',fmt(s.penalty)]],conditions:[['效果',String(s.effectId)]]}])],conditions:[['来源','N 号引擎 · '+trace.key]]};
}
const tip=detail=>detail?`tabindex="0" data-explain="${esc(JSON.stringify(detail))}"`:'';
const row=(label,value,detail)=>`<div class="stat-row" ${tip(detail)}><span>${esc(label)}</span><b>${esc(value)}</b></div>`;
const head=(label,summary='')=>`<div class="panel-title"><span>${label}</span>${summary?`<div class="section-summary" aria-label="${label}摘要">${summary}</div>`:'<small>N</small>'}</div>`;
export function nativeSlotMetrics(report,slot,group){
 const a=report.native.attributes,w=report.native.weapons[slot.key];
 const contribution=report.native.outputContributions?.items.find(i=>i.source.instanceId===slot.key&&i.kind==='ship_weapon'),basis=report.baselineOutputSelection?.metric;
 const read=contribution?.metrics[report.outputSelection?.metric],dps=read?.state==='available'?read.value:null,base=contribution?.metrics[basis]?.value;
 const flight=w?.missileFlight;
 const missileMetric=(label,value,unit,detail)=>`<span class="slot-metric" ${tip(detail)}><span class="slot-metric-label">${label}</span><b>${fmt(value)}</b><span class="slot-metric-unit">${unit}</span></span>`;
 const missileFields=group!=='resources'&&flight?
  missileMetric('射程',scaleReading(flight.nominalPathMeters,1000),'km',{title:'导弹射程',result:fmt(scaleReading(flight.nominalPathMeters,1000),'km'),terms:[['飞行速度',fmt(flight.speedMetersPerSecond,'m/s')],['飞行时间',fmt(flight.lifetimeSeconds,'s')]],conditions:[['口径','直线飞行估算，不计加速和目标移动']]})+
  missileMetric('弹速',flight.speedMetersPerSecond,'m/s',nativeDetail(a['charge.'+slot.key+'/37'],'导弹飞行速度','m/s',report)):'';
 const fields=group==='resources'?[['CPU',50,'tf'],['PG',30,'MW']]:flight?[]:[['周期',73,'ms'],['最佳',54,'m'],['失准',158,'m']];
 return missileFields+fields.map(([label,id,unit])=>{const t=a['module.'+slot.key+'/'+id];return t?`<span class="slot-metric" ${tip(nativeDetail(t,label,unit,report))}><span class="slot-metric-label">${label}</span><b>${fmt(t.value)}</b><span class="slot-metric-unit">${unit}</span></span>`:''}).join('')+(group!=='resources'&&w?`<span class="slot-metric">${report.attackMode==='edps'?'EDPS':'DPS'} <b ${numberAttributes(dps,report.scenarioTarget?base:undefined)}>${fmt(dps)}</b></span><span class="slot-metric">周期 <b>${fmt(w.cycleSeconds,'s')}</b></span>`:'');
}
export function nativeResources(host,report){
 if(!report.native.resources.length){host.innerHTML='<p class="profile-note">当前输入包含未支持效果，资源数值不可用</p>';return;}host.innerHTML=report.native.resources.filter(r=>['cpu','powergrid'].includes(r.id)).map(r=>`<div class="meter ${r.withinCapacity===false?'over':''}"><label>${r.id==='cpu'?'CPU':'能量栅格'} 剩余<span>${fmt(r.remaining)} / ${fmt(r.capacity,r.id==='cpu'?'tf':'MW')}</span></label>${Number.isFinite(r.remaining)&&Number.isFinite(r.capacity)&&r.capacity>0?`<progress value="${Math.max(0,r.remaining)}" max="${r.capacity}"></progress>`:''}</div>`).join('');
}
export function mountNativeStats(root,report,{mode='hp',onMode,onDamageEdit,catalog=[]}={}){
 const a=report.native,attrs=a.attributes;
 const attr=(label,id,unit='',scale=1)=>{const t=attrs['ship/'+id];return row(label,(id===70&&Number.isFinite(t?.value)?t.value.toFixed(5):fmt(scaleReading(t?.value,scale),unit)),panelTrace(t,label,unit,report,catalog,scale))};
 const detail=(title,value,unit,terms=[],conditions=[])=>panelReading(title,value,unit,terms,conditions);
 let html='<div id="native-capacitor">'+capacitorHtml(report,catalog)+'</div>';
 const unit=report.attackMode==='edps'?'EDPS':'DPS';
 const current=displayOutputSelection(report,report.outputSelection,report.outputContext?.selection?.contributionIds),base=displayOutputSelection(report,report.baselineOutputSelection,report.outputContext?.selection?.contributionIds),comparison=report.native.outputContributions.comparison;
 const comparisons=report.scenarioTarget?[['无情景基准',outputReading(base)+' DPS'],['差量',fmt(comparison?.delta.value,unit)],['相对基准',comparison?.ratio.state==='available'?fmt(comparison.ratio.value*100,'%'):'不可用 · '+(comparison?.ratio.reason||'缺少比较结果')]]:[];
 const targetConditions=report.scenarioTargetSource?[['目标装配',report.scenarioTargetSource.name],['目标防御层',({shield:'护盾',armor:'装甲',hull:'结构'})[report.scenarioTargetSource.layer]],['目标版本',String(report.scenarioTargetSource.revision??'未保存')]]:[];
 const outputTerms=(a.outputContributions.items||[]).filter(i=>report.outputContext.selection.contributionIds.includes(i.id)).map(i=>{const r=i.metrics[current.metric],name=catalog.find(t=>t.id===i.source.typeId)?.name||i.source.instanceId;return [name,r?.state==='available'?fmt(r.value,unit):'不可用',null,{title:name,result:r?.state==='available'?fmt(r.value,unit):'—',terms:(i.attributeKeys||[]).map(k=>panelAttributeTerm(k,report,catalog)),conditions:[['口径',current.metric],['状态',r?.state||'未知'],...(r?.reason?[['原因',r.reason]]:[])]}];});
 const damageDetail={title:'总 '+unit,result:outputReading(current)+' '+unit,resultClass:report.scenarioTarget?deltaClass(current.total,base.total):'',terms:[...comparisons,...outputTerms],conditions:[...targetConditions,['口径',(report.attackMode==='edps'?'固定目标层 · 已扣抗性':'不扣目标抗性')+' · '+(base.metric==='loadedCycleDps'?'有限弹量周期':'名义周期')]],chart:{status:'loading',reason:'正在向 N 引擎查询曲线…',request:report.curveRequest,cacheKey:JSON.stringify([report.curveRequest,report.scenarioTarget,report.source,report.native.outputContributions.fitHash])}};
 if(current.displayConvention==="empty_selected_sum")damageDetail.chart={status:"empty",attackMode:report.attackMode};
 const attackModes='<span class="attack-modes defense-modes" role="group" aria-label="伤害显示模式">'+['dps','edps'].map(m=>'<button data-native-attack="'+m+'" aria-pressed="'+(report.attackMode===m)+'" '+(m==='edps'&&!report.scenarioTarget?'disabled':'')+'>'+m.toUpperCase()+'</button>').join('')+'</span>';
 html+='<div class="panel-title"><span>攻击</span>'+attackModes+`<span class="attack-total" ${tip(damageDetail)}><b ${numberAttributes(current.total,report.scenarioTarget?base.total:undefined)}>${outputReading(current)} ${unit}</b></span></div>`+outputHtml(report,catalog);
 const defense=a.defense;
 const resonanceIds={shield:{em:271,thermal:274,kinetic:273,explosive:272},armor:{em:267,thermal:270,kinetic:269,explosive:268},hull:{em:113,thermal:110,kinetic:109,explosive:111}};
 html+=head('防御','<span class="defense-modes" role="group" aria-label="防御显示模式">'+[['hp','HP'],['ehp','EHP'],['targeted','针对抗']].map(([key,label])=>`<button data-native-defense="${key}" aria-pressed="${mode===key}">${label}</button>`).join('')+'</span>')+'<div class="stat-block"><div class="profile-note">'+(mode==='targeted'?'<button data-native-damage>调整来伤比例</button><div>'+['em','thermal','kinetic','explosive'].map((k,i)=>['电磁','热能','动能','爆炸'][i]+' '+fmt(Number.isFinite(defense?.incomingDamage?.[k])?defense.incomingDamage[k]*100:null,'%')).join(' · ')+'</div>':mode==='ehp'?'均匀来伤 · 各 25%':'已应用技能与装备')+'</div>';
 if(defense){
  for(const layer of defense.layers){
   const label=({shield:'护盾',armor:'装甲',hull:'结构'})[layer.layer],hpId=({shield:263,armor:265,hull:9})[layer.layer];
   html+='<div class="defense-layer">'+row(label,fmt(mode==='hp'?layer.hitpoints:layer.effectiveHitpoints,mode==='hp'?'HP':'EHP'),mode==='hp'?panelTrace(attrs['ship/'+hpId],label,'HP',report,catalog):detail(label,layer.effectiveHitpoints,'EHP',[['装配后血量',fmt(layer.hitpoints,'HP'),null,panelTrace(attrs['ship/'+hpId],label,'HP',report,catalog)],['加权伤害共振',fmt(layer.weightedResonance)]],[['口径','固定来伤比例，不计维修及回充']]));
   html+='<div class="damage-bars">'+['em','thermal','kinetic','explosive'].map((k,i)=>`<div class="damage-cell" ${panelTip(detail(['电磁','热能','动能','爆炸'][i]+'抗性',layer.resistancesPercent[k],'%',[['伤害共振',fmt(layer.resonances[k]),null,panelTrace(attrs['ship/'+resonanceIds[layer.layer][k]],'伤害共振','',report,catalog)]]))}><span class="damage-label">${['电磁','热能','动能','爆炸'][i]}</span><div class="mini-bar damage-${i}"><i style="width:${layer.resistancesPercent[k]}%"></i><b>${Number.isFinite(layer.resistancesPercent[k])?layer.resistancesPercent[k].toFixed(0):'—'}%</b></div></div>`).join('')+'</div></div>';
  }
  html+=row('总 '+(mode==='hp'?'HP':'EHP'),fmt(mode==='hp'?defense.rawHitpoints:defense.effectiveHitpoints),detail('总 '+(mode==='hp'?'HP':'EHP'),mode==='hp'?defense.rawHitpoints:defense.effectiveHitpoints,'',defense.layers.map(l=>[({shield:'护盾',armor:'装甲',hull:'结构'})[l.layer],fmt(mode==='hp'?l.hitpoints:l.effectiveHitpoints)])));
  for(const [label,layer] of [['主动回盾','shield'],['装甲维修','armor'],['结构维修','hull'],['被动回盾 · 峰值','passiveShield']]){
   const metric=a.inspector?.repairs?.[layer]?.[mode==='hp'?'hpPerSecond':'ehpPerSecond'];
   const units=mode==='hp'?'HP/s':'EHP/s';
   const repairs=Object.values(a.repairs||{}).filter(r=>!r.remoteRange&&r.payload?.layer===layer);
   html+=row(label,fmt(metric?.value,units),detail(label,metric?.value,units,repairs.map(r=>[catalog.find(t=>t.id===r.typeId)?.name||r.instanceId,fmt(r.activeHpPerSecond,'HP/s')]),[['口径','本舰周期维修；不含电容可持续性与过量维修'],...(metric?.reason?[['不可用原因',metric.reason]]:[])]));
  }
 }else html+=row('防御','不可计算');
 html+='</div>'+head('机动',`<b ${panelTip(panelTrace(attrs['ship/37'],'最大速度','m/s',report,catalog))}>${fmt(a.motion?.maximumSpeedMetersPerSecond,'m/s')}</b>`);
 const motionReason=a.motion?.fromRestTo75PercentUnavailableReason;
 const acceleration=motionReason==='ZERO_MAXIMUM_SPEED_NO_FROM_REST_THRESHOLD'?'不适用 · 当前最大速度为 0':fmt(a.motion?.fromRestTo75PercentSeconds,'s');
 html+='<div class="stat-block">'+attr('质量',4,'t',1000)+attr('惯性系数',70)+row('起跳时间',acceleration,{title:'起跳时间',result:acceleration,terms:[['质量',fmt(a.motion?.massKilograms,'kg')],['惯性系数',fmt(a.motion?.inertiaModifier)]],conditions:[['条件','静止起步，不保证跃迁准入'],...(motionReason?[['原因',motionReason]]:[])]})+attr('跃迁速度',600,'AU/s')+attr('信号半径',552,'m')+'</div>';
 html+=head('锁定',`<b ${panelTip(panelTrace(attrs['ship/76'],'锁定距离','km',report,catalog,1000))}>${fmt(scaleReading(attrs['ship/76']?.value,1000),'km')}</b>`)+'<div class="stat-block">'+row('锁定目标数',fmt(a.targetCountLimits?.maximum))+attr('扫描分辨率',564,'mm')+'</div>';
 const bandwidth=a.resources.find(r=>r.id==='droneBandwidth');
 if(a.droneBay&&a.droneBay.capacityCubicMeters>0)html+=head('无人机',`<b>${fmt(bandwidth?.used)} / ${fmt(bandwidth?.capacity,'Mbit/s')}</b>`)+'<div class="stat-block">'+row('控制距离',fmt(scaleReading(a.droneBay.controlRangeMeters,1000),'km'))+row('最大操控数量',fmt(a.droneBay.maximumActive,'架'))+'</div>';
 const sensors=[['雷达强度',208],['磁力强度',210],['引力强度',211],['光雷达强度',209]].filter(([,id])=>attrs['ship/'+id]?.value>0);
 if(sensors.length)html+=head('感应系统')+'<div class="stat-block">'+sensors.map(([label,id])=>attr(label,id)).join('')+'</div>';
 // These conditional groups were already present in the legacy inspector.
 // Only project native values; do not reintroduce its local formulas.
 let external='';
 for(const item of report.nativeFit?.items||[]){
  if(!item.active)continue;
  const type=catalog.find(t=>t.id===item.typeId),energy=a.energy?.[item.id],repair=a.repairs?.[item.id];
  let body='';
  if(energy?.active){
   if(energy.operation==='neutralize')body+=row('单轮毁电',fmt(energy.amountGj,'GJ'))+row('平均毁电',fmt(energy.nominalGjPerSecond,'GJ/s'));
   else body+=row(energy.operation==='transmit'?'远程传电':'吸电',fmt(energy.nominalGjPerSecond,'GJ/s'));
  }
  if(repair?.remoteRange)body+=row(({shield:'远程回盾',armor:'远程修甲',hull:'远程修结构'})[repair.payload?.layer]||'远程维修',fmt(repair.activeHpPerSecond,'HP/s'));
  if([65,52,209,208,379,201].includes(type?.group))for(const [key,label,unit] of [['speedFactor','速度修正','%'],['warpScrambleStrength','跃迁扰断强度',''],['scanResolutionBonus','扫描分辨率修正','%'],['maxTargetRangeBonus','锁定距离修正','%'],['signatureRadiusBonus','信号半径修正','%'],['trackingSpeedBonus','跟踪修正','%']]){
   const t=Object.values(attrs).find(t=>t.key.startsWith('module.'+item.id+'/')&&t.name===key);if(t&&Number.isFinite(t.value)&&t.value!==0)body+=row(label,fmt(t.value,unit),panelTrace(t,label,unit,report,catalog));
  }
  if(body){for(const [id,label] of [[54,'作用距离'],[158,'失准距离']]){const t=attrs['module.'+item.id+'/'+id];if(t?.value>0)body+=row(label,fmt(t.value/1000,'km'),panelTrace(t,label,'km',report,catalog,1000));}external+='<details class="extended-stat-group" open><summary>'+esc(type?.name||item.id)+'</summary><div class="stat-block">'+body+'</div></details>';}
 }
 if(external)html+='<div class="panel-title">对外效果</div><p class="profile-note">最佳范围内 · 未计目标抗性与电子战叠加</p>'+external;
 const all=[...new Set([...report.integrationNotices,...report.issues.map(e=>e.code+' · '+e.message),...a.warnings.map(e=>e.code+' · '+e.message),...a.coverage.filter(c=>c.status==='unsupported_static').map(c=>c.name+' · '+c.reason)])];
 const notice=root.closest?.('.inspector')?.querySelector('.notice');
 if(notice){notice.tabIndex=0;notice.dataset.explain=JSON.stringify({title:'装配校验',result:notice.textContent,terms:all.map(s=>['说明',s]),conditions:[['来源','N '+report.engineVersion]]});}
 patchHtml(root,html);
 root.querySelectorAll('[data-native-defense]').forEach(b=>b.onclick=()=>onMode?.(b.dataset.nativeDefense));
 root.querySelectorAll('[data-native-attack]').forEach(b=>b.onclick=()=>document.dispatchEvent(new CustomEvent('fitlab-attack-mode',{detail:b.dataset.nativeAttack})));
 const damageButton=root.querySelector('[data-native-damage]');if(damageButton)damageButton.onclick=()=>onDamageEdit?.();
}
