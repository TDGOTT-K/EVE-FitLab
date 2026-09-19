import {panelTrace,panelReading,panelTip,panelAttributeTerm} from './native-panel-detail.js';
import {scaleReading} from './analysis-status.js';
import {capacitorHtml} from './native-capacitor-view.js';
import {numberAttributes,deltaClass} from './scenario-display.js';
import {outputHtml,outputReading} from './nengine-output-view.js';
// Native report presenter: formatting and layout only; values belong to NEngine.
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const fmt=(n,u='')=>Number.isFinite(n)?n.toLocaleString('zh-CN',{maximumFractionDigits:2})+(u?' '+u:''):'—';
export function nativeDetail(trace,title,unit=''){
 if(!trace)return null;
 const source=id=>id==='mode'?'舰体模式':id?.startsWith('skill.')?'技能 '+id.slice(6):id?.startsWith('module.')?'装备 '+id.slice(7):id||'修正';
 return {title,result:fmt(trace.value,unit),terms:[['基础值',fmt(trace.baseValue,unit)],...(trace.steps||[]).filter(s=>s.before!==s.after).map(s=>[source(s.sourceId),fmt(s.before)+' → '+fmt(s.after),null,{title:source(s.sourceId),result:fmt(s.after,unit),terms:[['来源修正值',fmt(s.sourceValue)],['叠加系数',fmt(s.penalty)]],conditions:[['效果',String(s.effectId)]]}])],conditions:[['来源','N 号引擎 · '+trace.key]]};
}
const tip=detail=>detail?`tabindex="0" data-explain="${esc(JSON.stringify(detail))}"`:'';
const row=(label,value,detail)=>`<div class="stat-row" ${tip(detail)}><span>${esc(label)}</span><b>${esc(value)}</b></div>`;
const head=(label,summary='')=>`<div class="panel-title"><span>${label}</span><div class="section-summary" aria-label="${label}摘要">${summary}</div></div>`;
export function nativeSlotMetrics(report,slot,group){
 const a=report.native.attributes,w=report.native.weapons[slot.key];
 const contribution=report.native.outputContributions?.items.find(i=>i.source.instanceId===slot.key&&i.kind==='ship_weapon'),basis=report.baselineOutputSelection?.metric;
 const read=contribution?.metrics[report.outputSelection?.metric],dps=read?.state==='available'?read.value:null,base=contribution?.metrics[basis]?.value;
 const fields=group==='resources'?[['CPU',50,'tf'],['PG',30,'MW']]:[['周期',73,'ms'],['最佳',54,'m'],['失准',158,'m']];
 return fields.map(([label,id,unit])=>{const t=a['module.'+slot.key+'/'+id];return t?`<span class="slot-metric" ${tip(nativeDetail(t,label,unit))}><span class="slot-metric-label">${label}</span><b>${fmt(t.value)}</b><span class="slot-metric-unit">${unit}</span></span>`:''}).join('')+(group!=='resources'&&w?`<span class="slot-metric">${report.attackMode==='edps'?'EDPS':'DPS'} <b ${numberAttributes(dps,report.scenarioTarget?base:undefined)}>${fmt(dps)}</b></span><span class="slot-metric">周期 <b>${fmt(w.cycleSeconds,'s')}</b></span>`:'');
}
export function nativeResources(host,report){
 if(!report.native.resources.length){host.innerHTML='<p class="profile-note">当前输入包含未支持效果，资源数值不可用</p>';return;}host.innerHTML=report.native.resources.filter(r=>['cpu','powergrid'].includes(r.id)).map(r=>`<div class="meter ${r.withinCapacity===false?'over':''}"><label>${r.id==='cpu'?'CPU':'能量栅格'} 剩余<span>${fmt(r.remaining)} / ${fmt(r.capacity,r.id==='cpu'?'tf':'MW')}</span></label>${Number.isFinite(r.remaining)&&Number.isFinite(r.capacity)&&r.capacity>0?`<progress value="${Math.max(0,r.remaining)}" max="${r.capacity}"></progress>`:''}</div>`).join('');
}
export function mountNativeStats(root,report,{mode='hp',onMode,onDamageEdit,onOutputMetric,onCapHorizon,catalog=[]}={}){
 const a=report.native,attrs=a.attributes;
 const attr=(label,id,unit='',scale=1)=>{const t=attrs['ship/'+id];return row(label,fmt(scaleReading(t?.value,scale),unit),panelTrace(t,label,unit,report,catalog,scale))};
 const detail=(title,value,unit,terms=[],conditions=[])=>panelReading(title,value,unit,terms,conditions);
 let html=capacitorHtml(report,catalog);
 const unit=report.attackMode==='edps'?'EDPS':'DPS';
 const current=report.outputSelection,base=report.baselineOutputSelection,comparison=report.native.outputContributions.comparison;
 const comparisons=report.scenarioTarget?[['无情景基准',outputReading(base)+' DPS'],['差量',fmt(comparison?.delta.value,unit)],['相对基准',comparison?.ratio.state==='available'?fmt(comparison.ratio.value*100,'%'):'不可用 · '+(comparison?.ratio.reason||'缺少比较结果')]]:[];
 const targetConditions=report.scenarioTargetSource?[['目标装配',report.scenarioTargetSource.name],['目标防御层',({shield:'护盾',armor:'装甲',hull:'结构'})[report.scenarioTargetSource.layer]],['目标版本',String(report.scenarioTargetSource.revision??'未保存')]]:[];
 const outputTerms=(a.outputContributions.items||[]).filter(i=>report.outputContext.selection.contributionIds.includes(i.id)).map(i=>{const r=i.metrics[current.metric],name=catalog.find(t=>t.id===i.source.typeId)?.name||i.source.instanceId;return [name,r?.state==='available'?fmt(r.value,unit):'不可用',null,{title:name,result:r?.state==='available'?fmt(r.value,unit):'—',terms:(i.attributeKeys||[]).map(k=>panelAttributeTerm(k,report,catalog)),conditions:[['口径',current.metric],['状态',r?.state||'未知'],...(r?.reason?[['原因',r.reason]]:[])]}];});
 const damageDetail={title:'总 '+unit,result:outputReading(current)+' '+unit,resultClass:report.scenarioTarget?deltaClass(current.total,base.total):'',terms:[...comparisons,...outputTerms],conditions:[...targetConditions,['口径',(report.attackMode==='edps'?'固定目标层 · 已扣抗性':'不扣目标抗性')+' · '+(base.metric==='loadedCycleDps'?'有限弹量周期':'名义周期')]],chart:{status:'loading',reason:'正在向 N 引擎查询曲线…',request:report.curveRequest,cacheKey:JSON.stringify([report.curveRequest,report.scenarioTarget,report.source,report.native.outputContributions.fitHash])}};
 const attackModes='<span class="attack-modes defense-modes" role="group" aria-label="伤害显示模式">'+['dps','edps'].map(m=>'<button data-native-attack="'+m+'" aria-pressed="'+(report.attackMode===m)+'" '+(m==='edps'&&!report.scenarioTarget?'disabled':'')+'>'+m.toUpperCase()+'</button>').join('')+'</span>';
 html+=head('攻击',attackModes+`<span class="attack-total" ${tip(damageDetail)}><b ${numberAttributes(current.total,report.scenarioTarget?base.total:undefined)}>${outputReading(current)}</b></span>`)+outputHtml(report,catalog);
 const defense=a.defense;
 const resonanceIds={shield:{em:271,thermal:274,kinetic:273,explosive:272},armor:{em:267,thermal:270,kinetic:269,explosive:268},hull:{em:113,thermal:110,kinetic:109,explosive:111}};
 html+=head('防御','<span class="defense-modes" role="group" aria-label="防御显示模式">'+[['hp','HP'],['ehp','EHP'],['targeted','针对抗']].map(([key,label])=>`<button data-native-defense="${key}" aria-pressed="${mode===key}">${label}</button>`).join('')+'</span>')+'<div class="stat-block"><div class="profile-note">'+(mode==='targeted'?'<button data-native-damage>调整来伤比例</button>':mode==='ehp'?'均匀来伤 · 各25%':'已应用技能与装备')+'</div>';
 if(defense){
  for(const layer of defense.layers){
   const label=({shield:'护盾',armor:'装甲',hull:'结构'})[layer.layer],hpId=({shield:263,armor:265,hull:9})[layer.layer];
   html+='<div class="defense-layer">'+row(label,fmt(mode==='hp'?layer.hitpoints:layer.effectiveHitpoints,mode==='hp'?'HP':'EHP'),mode==='hp'?panelTrace(attrs['ship/'+hpId],label,'HP',report,catalog):detail(label,layer.effectiveHitpoints,'EHP',[['装配后血量',fmt(layer.hitpoints,'HP'),null,panelTrace(attrs['ship/'+hpId],label,'HP',report,catalog)],['加权伤害共振',fmt(layer.weightedResonance)]],[['口径','固定来伤比例，不计维修及回充']]));
   html+='<div class="damage-bars">'+['em','thermal','kinetic','explosive'].map((k,i)=>`<div class="damage-cell" ${panelTip(detail(['电磁','热能','动能','爆炸'][i]+'抗性',layer.resistancesPercent[k],'%',[['伤害共振',fmt(layer.resonances[k]),null,panelTrace(attrs['ship/'+resonanceIds[layer.layer][k]],'伤害共振','',report,catalog)]]))}><span class="damage-label">${['电磁','热能','动能','爆炸'][i]}</span><div class="mini-bar damage-${i}"><i style="width:${layer.resistancesPercent[k]}%"></i><b>${fmt(layer.resistancesPercent[k])}%</b></div></div>`).join('')+'</div></div>';
  }
  html+=row('总 '+(mode==='hp'?'HP':'EHP'),fmt(mode==='hp'?defense.rawHitpoints:defense.effectiveHitpoints),detail('总 '+(mode==='hp'?'HP':'EHP'),mode==='hp'?defense.rawHitpoints:defense.effectiveHitpoints,'',defense.layers.map(l=>[({shield:'护盾',armor:'装甲',hull:'结构'})[l.layer],fmt(mode==='hp'?l.hitpoints:l.effectiveHitpoints)])));
  if(a.shieldRecharge)html+=row('被动回盾 · 峰值',fmt(a.shieldRecharge.peakRecharge,'HP/s'),detail('被动回盾 · 峰值',a.shieldRecharge.peakRecharge,'HP/s',[['护盾容量',fmt(a.shieldRecharge.capacity,'HP')],['回充时间',fmt(a.shieldRecharge.nominalRechargeSeconds,'s')]]));
 }else html+=row('防御','不可计算');
 html+='</div>'+head('机动',`<b ${panelTip(panelTrace(attrs['ship/37'],'最大速度','m/s',report,catalog))}>${fmt(a.motion?.maximumSpeedMetersPerSecond,'m/s')}</b>`);
 const motionReason=a.motion?.fromRestTo75PercentUnavailableReason;
 const acceleration=motionReason==='ZERO_MAXIMUM_SPEED_NO_FROM_REST_THRESHOLD'?'不适用 · 当前最大速度为 0':fmt(a.motion?.fromRestTo75PercentSeconds,'s');
 html+='<div class="stat-block">'+attr('质量',4,'t',1000)+attr('惯性系数',70)+row('起步至 75%',acceleration,{title:'起步至 75%',result:acceleration,terms:[['质量',fmt(a.motion?.massKilograms,'kg')],['惯性系数',fmt(a.motion?.inertiaModifier)]],conditions:[['条件','静止起步，不保证跃迁准入'],...(motionReason?[['原因',motionReason]]:[])]})+attr('信号半径',552,'m')+attr('跃迁速度',600,'AU/s')+'</div>';
 html+=head('锁定',`<b ${panelTip(panelTrace(attrs['ship/76'],'锁定距离','km',report,catalog,1000))}>${fmt(scaleReading(attrs['ship/76']?.value,1000),'km')}</b>`)+'<div class="stat-block">'+attr('扫描分辨率',564,'mm')+row('锁定目标数',fmt(a.targetCountLimits?.maximum))+'</div>';
 const bandwidth=a.resources.find(r=>r.id==='droneBandwidth');
 if(a.droneBay&&a.droneBay.capacityCubicMeters>0)html+=head('无人机',`<b>${fmt(bandwidth?.used)} / ${fmt(bandwidth?.capacity,'Mbit/s')}</b>`)+'<div class="stat-block">'+row('控制距离',fmt(scaleReading(a.droneBay.controlRangeMeters,1000),'km'))+row('最多出动',fmt(a.droneBay.maximumActive))+'</div>';
 const sensors=[['雷达强度',208],['磁力强度',210],['引力强度',211],['光雷达强度',209]].filter(([,id])=>attrs['ship/'+id]?.value>0);
 if(sensors.length)html+=head('感应系统')+'<div class="stat-block">'+sensors.map(([label,id])=>attr(label,id)).join('')+'</div>';
 const all=[...new Set([...report.integrationNotices,...report.issues.map(e=>e.code+' · '+e.message),...a.warnings.map(e=>e.code+' · '+e.message),...a.coverage.filter(c=>c.status==='unsupported_static').map(c=>c.name+' · '+c.reason)])];
 if(all.length)html+='<details class="native-status"><summary>计算范围与待处理项 <b>'+all.length+'</b></summary>'+all.map(s=>'<p class="profile-note">'+esc(s)+'</p>').join('')+'</details>';
 root.innerHTML=html;
 root.querySelectorAll('[data-native-defense]').forEach(b=>b.onclick=()=>onMode?.(b.dataset.nativeDefense));
 root.querySelectorAll('[data-native-attack]').forEach(b=>b.onclick=()=>document.dispatchEvent(new CustomEvent('fitlab-attack-mode',{detail:b.dataset.nativeAttack})));
 const capWindow=root.querySelector('[data-cap-horizon]');if(capWindow)capWindow.onchange=e=>onCapHorizon?.(Number(e.target.value));
 const metricSelect=root.querySelector('.native-output-metric');if(metricSelect)metricSelect.onchange=e=>onOutputMetric?.(e.target.value);
 const damageButton=root.querySelector('[data-native-damage]');if(damageButton)damageButton.onclick=()=>onDamageEdit?.();
}
