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
 return fields.map(([label,id,unit])=>{const t=a['module.'+slot.key+'/'+id];return t?`<span class="slot-metric" ${tip(nativeDetail(t,label,unit))}><span class="slot-metric-label">${label}</span><b>${fmt(t.value)}</b><span class="slot-metric-unit">${unit}</span></span>`:''}).join('')+(group!=='resources'&&w?`<span class="slot-metric">DPS <b ${numberAttributes(dps,report.scenarioTarget?base:undefined)}>${fmt(dps)}</b></span><span class="slot-metric">周期 <b>${fmt(w.cycleSeconds,'s')}</b></span>`:'');
}
export function nativeResources(host,report){
 if(!report.native.resources.length){host.innerHTML='<p class="profile-note">当前输入包含未支持效果，资源数值不可用</p>';return;}host.innerHTML=report.native.resources.filter(r=>['cpu','powergrid'].includes(r.id)).map(r=>`<div class="meter ${r.withinCapacity?'':'over'}"><label>${r.id==='cpu'?'CPU':'能量栅格'} 剩余<span>${fmt(r.remaining)} / ${fmt(r.capacity,r.id==='cpu'?'tf':'MW')}</span></label><progress value="${Math.max(0,r.remaining)}" max="${r.capacity||1}"></progress></div>`).join('');
}
export function mountNativeStats(root,report,{mode='hp',onMode,onDamageEdit,onOutputMetric,catalog=[]}={}){
 const a=report.native,attrs=a.attributes;
 const attr=(label,id,unit='',scale=1)=>{const t=attrs['ship/'+id];return row(label,fmt(t? t.value/scale : null,unit),nativeDetail(t,label))};
 const cap=a.capacitor;
 let html=head('电容',cap?`<b style="color:${cap.stableFromFullInAverageModel?'#79d6ab':'#f18080'}">${cap.stableFromFullInAverageModel?'稳定 · '+fmt(cap.stableFraction*100)+'%':'不稳定'}</b>`:'—');
 html+='<div class="stat-block">'+(cap?row('容量',fmt(cap.recharge.capacity,'GJ'))+row('平均耗电',fmt(cap.averageActiveDrain,'GJ/s'))+row('峰值回充',fmt(cap.recharge.peakRecharge,'GJ/s'))+'<small class="profile-note">平均负载模型 · 非逐周期续航</small>':row('电容','不可计算'))+'</div>';
 const current=report.outputSelection,base=report.baselineOutputSelection;
 const damageDetail={title:'已选输出 DPS',result:outputReading(current)+' DPS',resultClass:report.scenarioTarget?deltaClass(current.total,base.total):'',terms:report.scenarioTarget?[['无情景基准',outputReading(base)+' DPS']]:[],conditions:[['口径','不扣目标抗性 · '+(base.metric==='loadedCycleDps'?'有限弹量周期':'名义周期')]],chart:{status:'loading',reason:'正在向 N 引擎查询曲线…',request:report.curveRequest,cacheKey:JSON.stringify([report.curveRequest,report.scenarioTarget,report.source,report.native.outputContributions.fitHash])}};
 html+=head('攻击',`<span class="attack-total" ${tip(damageDetail)}><b ${numberAttributes(current.total,report.scenarioTarget?base.total:undefined)}>${outputReading(current)} DPS</b></span>`)+outputHtml(report,catalog);
 const defense=a.defense;
 html+=head('防御')+'<div class="stat-block"><div class="defense-mode-switch">'+[['hp','HP'],['ehp','EHP'],['targeted','针对抗']].map(([key,label])=>`<button data-native-defense="${key}" aria-pressed="${mode===key}">${label}</button>`).join('')+'<button data-native-damage>来伤比例</button></div>';
 if(defense){
  for(const layer of defense.layers){
   html+=row(({shield:'护盾',armor:'装甲',hull:'结构'})[layer.layer],fmt(mode==='hp'?layer.hitpoints:layer.effectiveHitpoints,mode==='hp'?'HP':'EHP'));
   html+='<div class="damage-bars">'+['em','thermal','kinetic','explosive'].map((k,i)=>`<div class="damage-cell"><span class="damage-label">${['电磁','热能','动能','爆炸'][i]}</span><div class="mini-bar damage-${i}"><i style="width:${layer.resistancesPercent[k]}%"></i><b>${fmt(layer.resistancesPercent[k])}%</b></div></div>`).join('')+'</div>';
  }
 }else html+=row('防御','不可计算');
 html+='</div>'+head('机动',`<b>${fmt(a.motion?.maximumSpeedMetersPerSecond,'m/s')}</b>`);
 html+='<div class="stat-block">'+row('起步至 75%',fmt(a.motion?.fromRestTo75PercentSeconds,'s'))+attr('信号半径',552,'m')+attr('跃迁速度',600,'AU/s')+'</div>';
 html+=head('锁定',`<b>${fmt(attrs['ship/76']?attrs['ship/76'].value/1000:null,'km')}</b>`)+'<div class="stat-block">'+attr('扫描分辨率',564,'mm')+row('锁定目标数',fmt(a.targetCountLimits?.maximum))+'</div>';
 if(a.droneBay&&a.droneBay.capacityCubicMeters>0)html+=head('无人机')+'<div class="stat-block">'+row('控制距离',fmt(a.droneBay.controlRangeMeters/1000,'km'))+row('最多出动',fmt(a.droneBay.maximumActive))+'</div>';
 const all=[...report.integrationNotices,...report.issues.map(e=>e.code+' · '+e.message),...a.warnings.map(e=>e.code+' · '+e.message),...a.coverage.filter(c=>c.status==='unsupported_static').map(c=>c.name+' · '+c.reason)];
 if(all.length)html+='<details class="native-status"><summary>计算范围与待处理项 <b>'+all.length+'</b></summary>'+all.map(s=>'<p class="profile-note">'+esc(s)+'</p>').join('')+'</details>';
 root.innerHTML=html;
 root.querySelectorAll('[data-native-defense]').forEach(b=>b.onclick=()=>onMode?.(b.dataset.nativeDefense));
 root.querySelector('.native-output-metric').onchange=e=>onOutputMetric?.(e.target.value);
 root.querySelector('[data-native-damage]').onclick=()=>onDamageEdit?.();
}
export function nativeItemParameters(calculation,key){
 const prefix=key==='ship'?'ship':key.startsWith('drone-')?'drone.'+key:'module.'+key;
 return {attributeSnapshot:Object.fromEntries(Object.entries(calculation.native.attributes).filter(([k])=>k.startsWith(prefix+'/')).map(([,t])=>[t.name,t.value]))};
}
