import {capacitorDetail,capacitorFlowDetail} from './capacitor-chart.js';
import {renderWorkspaceAttack,numberAttributes,scenarioDetail,metricText} from './scenario-display.js';
import {getLocale} from './i18n.js';
import {extendedStats} from './extended-stats.js';
import {savedDamage,damageResonance,effectiveValue,defenseNumber} from './defense-profile.js';
export function attributeDetail(name,value,unit,attribute,a,catalog=[],pilot='',scale=1,baseLabel='舰船基础'){
 const trace=a.attributeTraces?.[attribute],context=[['角色',pilot||'无技能'],['来源','Dogma 执行记录']];
 if(!trace)return {title:name,result:`${Number(value).toLocaleString(getLocale(),{maximumFractionDigits:5})} ${unit}`,terms:[['计算记录',trace?'后处理尚未完整追踪':'此属性尚无执行记录']],conditions:context};
 const incomplete=!trace.isComplete||Math.abs(trace.finalValue/scale-value)>0.001;
 const terms=[[baseLabel,`${(trace.baseValue/scale).toFixed(2)} ${unit}`]];
 for(const step of trace.steps){
  const source=catalog.find(t=>t.id===step.sourceTypeId)?.name||'未命名来源',isSkill=step.sourceKind==='Skill',op=step.operation;
  const operation=['ModAdd','ModSub'].includes(op)?`${step.appliedValue>=0?'+ ':''}${(step.appliedValue/scale).toFixed(3)} ${unit}`:['PreAssign','PostAssign'].includes(op)?`= ${(step.appliedValue/scale).toFixed(3)} ${unit}`:`× ${step.appliedValue.toFixed(5)}`;
  const sourceUnit=op==='PostPercent'?'%':'';
  const sourceTerms=isSkill?(step.modifyingAttributeId===280?[['技能等级',step.skillLevel]]:[['每级修正',`${step.sourceBaseValue}${sourceUnit}`],['技能等级',`× ${step.skillLevel}`]]):[['引擎求值',step.sourceValue]];
  const sourceDetail={title:source+' · 修正值',result:`${step.sourceValue}${sourceUnit}`,terms:sourceTerms,conditions:[['来源',isSkill?'技能等级运算':'Dogma 属性求值'],...(!isSkill?[['来源属性内部运算','尚未追踪']]:[])]};
  const detail={title:source,result:`${(step.after/scale).toFixed(3)} ${unit}`,terms:[['执行前',`${(step.before/scale).toFixed(3)} ${unit}`],['来源修正值',`${step.sourceValue}${sourceUnit}`,null,sourceDetail],['叠加惩罚系数',step.penaltyMultiplier.toFixed(6)],['实际运算',operation]],conditions:[['执行序号',step.order],['来源状态',isSkill?`技能 ${step.skillLevel} 级`:({'Active':'启动','Online':'在线','Passive':'被动','Offline':'离线','Overload':'超载'}[step.sourceState]||step.sourceState)]]};
  terms.push([source+(isSkill?` ${step.skillLevel} 级`:''),operation,'source',detail]);
 }
 if(incomplete)terms.push(['最后记录值',((trace.steps.at(-1)?.after??trace.baseValue)/scale).toFixed(5)+' '+unit],['缺失环节',attribute+'：后处理或投射尚未连接到执行记录']);
 return {title:name,result:`${Number(value).toLocaleString(getLocale(),{maximumFractionDigits:5})} ${unit}`,terms,conditions:context};
}

export function calculationDetail(node,catalog=[],pilot=''){
 if(!node)return null;
 const fmt=(v,u)=>typeof v==='number'?Number(v).toLocaleString(getLocale(),{maximumFractionDigits:5})+(u?' '+u:''):'未求得';
 if(node.trace&&node.complete)return attributeDetail(node.title,node.value,node.unit,node.trace.attribute,{attributeTraces:{[node.trace.attribute]:node.trace}},catalog,pilot,node.scale||1,'基础属性');
 const terms=[];
 if(node.operation)terms.push(['运算',({sum:'各项相加',multiply:'各项相乘',divide:'第一项 ÷ 第二项'})[node.operation]]);
 for(const child of node.inputs||[])terms.push([child.title,fmt(child.value,child.unit),null,calculationDetail(child,catalog,pilot)]);
 if(node.source)terms.push(['来源',node.source]);
 if(!node.complete)terms.push(['待补环节',node.missing||'以下输入仍有缺失记录']);
 if(node.formulaValue!=null)terms.push(['公式求值',fmt(node.formulaValue,node.unit)]);
 return {title:node.title,result:fmt(node.value,node.unit),terms,conditions:[['角色',pilot||'无技能'],['核验',node.complete?'公式与引擎输出一致':'尚未闭合']]};
}

export function mountEngineStats(root,report,ship,pilot,mode,onToggle,catalog=[],profile,onEdit){
 const ehp=mode!=='hp',weights=mode==='targeted'?savedDamage(profile):[.25,.25,.25,.25];
 const distribution=['电磁','热能','动能','爆炸'].map((n,i)=>n+' '+(weights[i]*100).toFixed(1)+'%').join(' · ');
 const resonanceTerms=l=>l.resist.map((r,i)=>[['电磁','热能','动能','爆炸'][i],`${(weights[i]*100).toFixed(1)}% × (1 − ${(r*100).toFixed(2)}%)`]);
 const a=report.attributes,raw=ship.attrs,keys=['em','thermal','kinetic','explosive'],names=['电磁','热能','动能','爆炸'];
 const esc=t=>String(t).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 const context=[['角色',pilot||'无技能'],['计算来源','Dogma 引擎'],['明细范围','当前返回值']];
 const tip=(title,result,terms,comparison)=>`tabindex="0" data-explain="${esc(JSON.stringify(capacitorFlowDetail(title,report)||(['电容续航','净耗电'].includes(title)?capacitorDetail(report):comparison?scenarioDetail(title,result,comparison.value,comparison.baseline,terms,report.workspace?.conditions||context,comparison.baselineText):calculationDetail(a.calculationDetails?.[({'DPS':'appliedDamagePerSecond','无人机 DPS':'appliedDroneDamagePerSecond','模块耗电':'capacitorUsagePerSecond'})[title]],catalog,pilot)||{title,result,terms,conditions:context})))}"`;
 const unchanged=(name,value)=>{const id=({'锁定目标数':192,'回充时间':55,...(!ehp?{'护盾':263,'装甲':265,'结构':9}:{})})[name];return id!=null&&Math.abs(parseFloat(value)-(raw[id]/(id===55?1000:1)))<0.001};
 const row=(name,value,terms,comparison)=>`<div class="stat-row" ${!comparison&&unchanged(name,value)?'':tip(name,value,terms,comparison)}><span>${name}</span><b ${comparison?numberAttributes(comparison.value,comparison.baseline):''}>${value}</b></div>`;
 const base=(name,value,initial,unit)=>{if(Math.abs(value-initial)<0.001)return `<div class="stat-row"><span>${name}</span><b>${Number(value).toLocaleString(getLocale(),{maximumFractionDigits:5})} ${unit}</b></div>`;const attribute={'最大速度':'maxVelocity','信号半径':'signatureRadius','容量':'capacitorCapacity','锁定距离':'maxTargetRange','扫描分辨率':'scanResolution'}[name];const detail=attributeDetail(name,value,unit,attribute,a,catalog,pilot,unit==='km'?1000:1);return `<div class="stat-row" tabindex="0" data-explain="${esc(JSON.stringify(detail))}"><span>${name}</span><b>${detail.result}</b></div>`};
 const head=(name,note='Dogma')=>`<div class="panel-title">${name}<small>${note}</small></div>`;
 const bars=(values,title,baselines=null,components=null)=>`<div class="damage-bars">${values.map((v,i)=>`<div class="damage-cell" ${baselines&&Math.abs(v-baselines[i])<0.00001?'':tip(names[i]+' · '+title,(v*100).toFixed(1)+'%',[['比例',(v*100).toFixed(2)+'%'],...(components?[[names[i]+' DPS',components[i].toFixed(2)]]:[])])}><span class="damage-label">${names[i]}</span><div class="mini-bar damage-${i}" role="progressbar" aria-label="${names[i]} ${title}" aria-valuenow="${(v*100).toFixed(1)}" aria-valuemin="0" aria-valuemax="100"><i style="width:${Math.max(0,Math.min(100,v*100))}%"></i><b>${(v*100).toFixed(0)}%</b></div>${components?`<span class="damage-component" aria-label="${names[i]} DPS"><b>${components[i].toFixed(1)}</b><small>DPS</small></span>`:''}</div>`).join('')}</div>`;
 const layers=[['护盾','shield'],['装甲','armor'],['结构','structure']].map(([name,k])=>{const hp=a[k+'Hitpoints'],resist=keys.map(x=>a[k+'Resistances'][x+'Percent']),mean=damageResonance(resist,weights);return {name,hp,resist,mean,ehp:effectiveValue(hp,mean),baseResist:({shield:[271,274,273,272],armor:[267,270,269,268],structure:[113,110,109,111]})[k].map(id=>1-raw[id])}});
 const snapshot=a.attributeSnapshot||{};
 const extraAttribute=(name,key,unit,scale=1)=>{const value=snapshot[key];if(!Number.isFinite(value))return '';const trace=a.attributeTraces?.[key];if(trace?.isComplete&&Math.abs(trace.finalValue-trace.baseValue)>.000001){const detail=attributeDetail(name,value/scale,unit,key,a,catalog,pilot,scale);return '<div class="stat-row" tabindex="0" data-explain="'+esc(JSON.stringify(detail))+'"><span>'+name+'</span><b>'+detail.result+'</b></div>'}return '<div class="stat-row"><span>'+name+'</span><b>'+Number(value/scale).toFixed(2)+' '+unit+'</b></div>'};
 const align=Number.isFinite(snapshot.mass)&&Number.isFinite(snapshot.agility)?Math.log(4)*snapshot.mass*snapshot.agility/1000000:null;
 const mobilityExtra=extraAttribute('质量','mass','t',1000)+extraAttribute('惯性系数','agility','')+(align!==null?row('起跳时间',align.toFixed(2)+' s',[['装配后质量',snapshot.mass.toFixed(0)+' kg'],['惯性系数','× '+snapshot.agility],['达到最大速度的 75%','× ln(4) ÷ 1,000,000'],['条件','静止起步；不含朝向、碰撞与服务器 tick 取整']]):'')+extraAttribute('跃迁速度','warpSpeedMultiplier','AU/s');
 const cap=report.capacitorAnalysis,baseCap=report.workspace?.baseline.capacitorAnalysis;
 const capCompare=key=>report.workspace?.active?{value:cap?.[key],baseline:baseCap?.[key]}:undefined;
 const capTerms=cap?[['初始电容','100%'],['回充公式','C × [1 − (1 − √(Q/C)) × exp(−5t/T)]²'],['扣电时点','模块启动时；同时启动先合计扣电'],['换弹',cap.includesBoosters?'注电器计入弹夹与换弹，假设持续补充弹药':'耗电模块连续运行，不计武器换弹停机'],['吸电条件','己方电容百分比低于目标；目标有足够电容；最佳范围内，无目标抗性减免'],['电容电池','容量及回充使用 Dogma 最终属性；抗毁电效果仅在受攻击时生效'],['外部毁电',(cap.incomingNeutPerSecond||0).toFixed(2)+' GJ/s（已计本舰电容战抗性）'],['友方传电',(cap.incomingTransferPerSecond||0).toFixed(2)+' GJ/s'],['外部时序','首轮发生在设定周期结束；同刻本舰先支付启动耗电'],['稳定判据','同一周期相位的电容量收敛'],
 ['电容容量',a.capacitorCapacity+' GJ',null,attributeDetail('电容容量',a.capacitorCapacity,'GJ','capacitorCapacity',a,catalog,pilot)],
 ['回充时间',a.capacitorRechargeSeconds+' s',null,attributeDetail('回充时间',a.capacitorRechargeSeconds,'s','rechargeRate',a,catalog,pilot,1000)],
 ...((cap.events||[]).slice(-3).map(e=>['启动时刻',e.time+' s',null,{title:'电容事件 · '+e.time+' s',result:e.canActivate?'启动成功':'电容不足',terms:[['回充前',e.beforeRecharge.toFixed(4)+' GJ'],['间隔',e.elapsed+' s'],['回充公式','C × [1 − (1 − √(Q/C)) × exp(−5t/T)]²'],['回充后',e.afterRecharge.toFixed(4)+' GJ'],['同时启动扣电','− '+e.cost.toFixed(4)+' GJ'],['吸电与注电','+ '+e.income.toFixed(4)+' GJ']],conditions:[['来源','本次电容模拟实际事件']]}]))]:[];
 const capStatus=!cap?'等待计算':cap.status==='stable'?'稳定 · '+cap.lowPercent.toFixed(1)+'%'+(Math.abs(cap.highPercent-cap.lowPercent)>.1?'–'+cap.highPercent.toFixed(1)+'%':''):cap.status==='depletes'?Math.floor(cap.seconds/60)+'分 '+(cap.seconds%60).toFixed(1)+'秒':cap.status==='bounded'?'≥ '+(cap.checkedSeconds/3600).toFixed(1)+' 小时（未判定稳定）':cap.reason;
 const capTime=a.capacitorRechargeSeconds,shieldTime=(a.attributeSnapshot.shieldRechargeRate||raw[479])/1000;
 const capPeak=capTime>0?2.5*a.capacitorCapacity/capTime:0,shieldPeak=shieldTime>0?2.5*a.shieldHitpoints/shieldTime:0;
 const modules=report.snapshot.modules.filter(m=>!['Offline','Online'].includes(m.state));
 const contributions=field=>modules.filter(m=>m[field]&&!([41,325,585].includes(catalog.find(t=>t.id===m.dogmaTypeId)?.group)&&['shieldRepairPerSecond','armorRepairPerSecond','structureRepairPerSecond'].includes(field))).map(m=>[m.name,'+ '+Number(m[field]).toFixed(2),null,calculationDetail(m.calculationDetails?.[field],catalog,pilot)]);
 const repair=(name,rate,layer,terms)=>{
 const output=ehp?effectiveValue(rate,layer.mean):rate;
 return row(name,defenseNumber(output,2)+(ehp?' EHP/s':' HP/s'),[...terms,...(ehp?[['原始修量',rate.toFixed(2)+' HP/s'],['加权伤害共振',`÷ ${layer.mean.toFixed(6)}`],['来伤分布',distribution],...resonanceTerms(layer)]:[])])};
 root.innerHTML=(report.workspace?renderWorkspaceAttack(report,catalog):head('攻击')+`<div class="stat-block">${row('武器 DPS',Math.max(0,a.appliedDamagePerSecond-a.appliedDroneDamagePerSecond).toFixed(1),[['来源','总输出减无人机输出']])+row('无人机 DPS',a.appliedDroneDamagePerSecond.toFixed(1),[['来源','已出动无人机的 Dogma 输出']])+row('DPS',a.appliedDamagePerSecond.toFixed(1),[['已装配武器合计',a.appliedDamagePerSecond.toFixed(2)],['命中损失','未计入']])}${row('含换弹 DPS',(report.scenarioAnalysis?report.scenarioAnalysis.reloadWeaponDps+(a.appliedDroneDamagePerSecond||0):a.damagePerSecondWithReload).toFixed(1),[['公式','Σ（单件 DPS × 弹夹发数 × 周期 ÷（弹夹发数 × 周期 + 换弹时间））+ 无人机 DPS'],['预热时间',(report.scenarioAnalysis?.target.spoolSeconds||0)+' s']])}${row('齐射伤害 · DPH',a.volleyDamage.toFixed(1)+' HP',[['武器齐射合计',a.volleyDamage.toFixed(2)+' HP']])}${bars(keys.map(k=>a.appliedDamagePerSecond?a.appliedDamageProfilePerSecond[k]/a.appliedDamagePerSecond:0),'伤害占比',null,keys.map(k=>a.appliedDamageProfilePerSecond[k]||0))}</div>`)+
 head('防御',`<span class="defense-modes" role="group" aria-label="防御显示模式">${[['hp','HP'],['ehp','EHP'],['targeted','针对抗']].map(([v,n])=>`<button data-defense-mode="${v}" aria-pressed="${mode===v}">${n}</button>`).join('')}</span>`)+`<div class="stat-block"><div class="profile-note">${mode==='targeted'?`<button id="edit-damage-profile">调整来伤比例</button><div>${distribution}</div>`:ehp?'均匀来伤 · 各 25%':'已应用技能与装备'}</div>${layers.map(l=>`<div class="defense-layer">${row(l.name,defenseNumber(ehp?l.ehp:l.hp)+' '+(ehp?'EHP':'HP'),[['装配后血量',l.hp.toFixed(2)+' HP'],...(ehp?[...resonanceTerms(l),['加权伤害共振',`÷ ${l.mean.toFixed(6)}`]]:[])])}${bars(l.resist,'抗性',l.baseResist)}</div>`).join('')}${row('总 '+(ehp?'EHP':'HP'),defenseNumber(layers.reduce((n,l)=>n+(ehp?l.ehp:l.hp),0)),layers.map(l=>[l.name,defenseNumber(ehp?l.ehp:l.hp,2)]))}${repair('主动回盾',Math.max(0,a.shieldRepairPerSecond-a.passiveShieldRechargePerSecond),layers[0],contributions('shieldRepairPerSecond'))}${repair('装甲维修',a.armorRepairPerSecond,layers[1],contributions('armorRepairPerSecond'))+repair('结构维修',a.structureRepairPerSecond||0,layers[2],contributions('structureRepairPerSecond'))}${repair('被动回盾 · 峰值',shieldPeak,layers[0],[['护盾量',a.shieldHitpoints+' HP'],['回充时间',`÷ ${shieldTime} s`],['峰值系数','× 2.5'],['峰值位置','25% 护盾']])}</div>`+
 head('电容')+`<div class="stat-block">${base('容量',a.capacitorCapacity,raw[482]||0,'GJ')}${row('回充时间',capTime.toFixed(1)+' s',[['引擎回充时间',capTime+' s']])}${row('峰值回充',capPeak.toFixed(2)+' GJ/s',[['容量',a.capacitorCapacity+' GJ'],['回充时间',`÷ ${capTime} s`],['峰值系数','× 2.5']])}${row('模块耗电',a.capacitorUsagePerSecond.toFixed(2)+' GJ/s',contributions('capacitorUsagePerSecond'))}${cap?.nosferatuPerSecond>0?row('吸电收益',cap.nosferatuPerSecond.toFixed(2)+' GJ/s',cap.nosferatuSources.map(([n,v])=>[n,'+ '+v.toFixed(2)+' GJ/s']).concat(capTerms)):''}${cap?.injectionPerSecond>0?row('注电收益',cap.injectionPerSecond.toFixed(2)+' GJ/s',[['换弹折算','单发注电 × 弹夹数量 ÷（周期 × 弹夹数量 + 换弹时间）'],...capTerms]):''}${report.workspace?.active&&report.scenarioLinks?.supportFitId?row('友方传电',metricText(cap?.incomingTransferPerSecond,'GJ/s'),capTerms,capCompare('incomingTransferPerSecond')):''}${report.workspace?.active&&report.scenarioLinks?.hostileFitId?row('受到毁电',metricText(cap?.incomingNeutPerSecond,'GJ/s'),capTerms,capCompare('incomingNeutPerSecond')):''}${Number.isFinite(cap?.netUsagePerSecond)?row('净耗电',cap.netUsagePerSecond.toFixed(2)+' GJ/s',[['模块消耗',cap.usagePerSecond.toFixed(2)+' GJ/s'],['吸电收益','− '+cap.nosferatuPerSecond.toFixed(2)+' GJ/s'],['注电收益','− '+cap.injectionPerSecond.toFixed(2)+' GJ/s'],['受到毁电','+ '+cap.incomingNeutPerSecond.toFixed(2)+' GJ/s'],['友方传电','− '+cap.incomingTransferPerSecond.toFixed(2)+' GJ/s']],capCompare('netUsagePerSecond')):''}${row('电容续航',capStatus,capTerms,report.workspace?.active&&cap?.status===baseCap?.status&&['stable','depletes'].includes(cap?.status)?{value:cap.status==='stable'?cap.lowPercent:cap.seconds,baseline:cap.status==='stable'?baseCap.lowPercent:baseCap.seconds,baselineText:cap.status==='stable'?'稳定 · '+baseCap.lowPercent.toFixed(1)+'%':baseCap.seconds.toFixed(1)+' s'}:undefined)}<p class="profile-note">满电起始 · 按模块周期计算</p></div>`+
 head('机动')+`<div class="stat-block">${base('最大速度',a.maxVelocity,raw[37]||0,'m/s')+mobilityExtra}${base('信号半径',a.signatureRadius,raw[552]||0,'m')}</div>`+
 head('锁定')+`<div class="stat-block">${base('锁定距离',a.maxTargetRange/1000,(raw[76]||0)/1000,'km')}${row('锁定目标数',a.attributeSnapshot.maxLockedTargets||0,[['引擎输出',a.attributeSnapshot.maxLockedTargets||0]])}${base('扫描分辨率',a.attributeSnapshot.scanResolution||0,raw[564]||0,'mm')}</div>`;
 if(snapshot.droneControlDistance>0)root.insertAdjacentHTML('beforeend',head('无人机','<span class="section-summary" aria-label="无人机带宽：已用 / 总量" '+tip('无人机带宽',metricText(a.droneBandwidthUsed)+' / '+metricText(a.droneBandwidthAvailable,'Mbit/s'),[['已用带宽',metricText(a.droneBandwidthUsed,'Mbit/s')],['总带宽',metricText(a.droneBandwidthAvailable,'Mbit/s')]])+'><b class="'+(a.droneBandwidthUsed>a.droneBandwidthAvailable?'limit-over':'')+'">'+metricText(a.droneBandwidthUsed)+' / '+metricText(a.droneBandwidthAvailable)+' <span class="summary-unit">Mbit/s</span></b></span>')+'<div class="stat-block">'+extraAttribute('控制距离','droneControlDistance','km',1000)+extraAttribute('最大操控数量','maxActiveDrones','架')+'</div>');
 const sensors=[['雷达强度','scanRadarStrength'],['磁力强度','scanMagnetometricStrength'],['引力强度','scanGravimetricStrength'],['光雷达强度','scanLadarStrength']].filter(([n,k])=>snapshot[k]>0);
 if(sensors.length)root.insertAdjacentHTML('beforeend',head('感应系统')+'<div class="stat-block">'+sensors.map(([n,k])=>extraAttribute(n,k,'')).join('')+'</div>');
 const sustained=report.sustainedTank;
 if(sustained?.status==='sampled'){
 const body=[['持续回盾','shield',layers[0]],['持续修甲','armor',layers[1]],['持续修结构','structure',layers[2]]].filter(([n,k])=>sustained[k]>0||(report.workspace?.baseline.sustainedTank?.[k]||0)>0).map(([n,k,l])=>row(n,defenseNumber(ehp?effectiveValue(sustained[k],l.mean):sustained[k],2)+(ehp?' EHP/s':' HP/s'),[['预热','20 分钟'],['统计窗口','随后 10 分钟'],['调度规则','按装配顺序支付耗电；失败后下个周期重试'],['来伤分布',ehp?distribution:'不按抗性折算']],report.workspace?.active?{value:ehp?effectiveValue(sustained[k],l.mean):sustained[k],baseline:report.workspace.baseline.sustainedTank?.status==='sampled'?(ehp?effectiveValue(report.workspace.baseline.sustainedTank[k],l.mean):report.workspace.baseline.sustainedTank[k]):undefined}:undefined)).join('');
 root.insertAdjacentHTML('beforeend',head('持续维修 · 周期测试')+'<div class="stat-block">'+(body||'<p class="profile-note">统计窗口内未成功维修</p>')+'<p class="profile-note">按电容可支付周期采样，非理论峰值；不计过量维修。</p></div>');
 }
 // Promote each group's primary metric without rebuilding its explanation
 // or losing scenario comparison attributes on the original value.
 for(const [section,label] of [['电容','电容续航'],['机动','最大速度'],['锁定','锁定距离']]){
  const heading=[...root.querySelectorAll(':scope > .panel-title')].find(h=>h.firstChild?.textContent.trim()===section);
  const row=heading&&[...heading.nextElementSibling.querySelectorAll('.stat-row')].find(r=>r.querySelector(':scope > span')?.textContent===label);
  if(!row)continue;
  row.className='section-summary';row.setAttribute('aria-label',label);row.querySelector(':scope > span').remove();
  if(section==='电容'){
   const value=row.querySelector('b');
   value.classList.remove('scenario-increased','scenario-decreased');
   value.classList.add(cap?.status==='stable'?'cap-stable':cap?.status==='depletes'?'cap-depletes':'cap-undetermined');
   value.removeAttribute('title');
  }
  heading.querySelector('small')?.remove();heading.append(row);
 }
 const capacitorHeading=root.querySelector('.section-summary[aria-label="电容续航"]')?.closest('.panel-title');
 if(capacitorHeading){const body=capacitorHeading.nextElementSibling;root.prepend(capacitorHeading,body);}
 root.insertAdjacentHTML('beforeend',extendedStats(report,catalog));
 root.querySelectorAll('[data-attack-mode]').forEach(b=>b.onclick=()=>document.dispatchEvent(new CustomEvent('fitlab-attack-mode',{detail:b.dataset.attackMode})));
 root.querySelectorAll('[data-defense-mode]').forEach(b=>b.onclick=()=>onToggle(b.dataset.defenseMode));
 const edit=root.querySelector('#edit-damage-profile');if(edit)edit.onclick=onEdit;
}
