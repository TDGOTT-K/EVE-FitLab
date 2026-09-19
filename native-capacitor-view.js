import {panelTrace,panelReading,panelTip} from './native-panel-detail.js';
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const fmt=(n,u='')=>Number.isFinite(n)?n.toLocaleString('zh-CN',{maximumFractionDigits:2})+(u?' '+u:''):'—';
const duration=n=>n<60?fmt(n)+'秒':Math.floor(n/60)+'分'+(n%60?fmt(n%60)+'秒':'');
const row=(title,value,detail)=>'<div class="stat-row" '+(detail?panelTip(detail):'')+'><span>'+esc(title)+'</span><b>'+esc(value)+'</b></div>';
export function capacitorPresentation(report,catalog=[]){
 const data=report.capacitorScenario,r=data?.result,average=r?.average,native=report.native||{},local=native.capacitor;
 const external=['targetFitId','supportFitId','hostileFitId'].some(k=>report.curveRequest?.scenario?.[k]);
 const model=average?(average.complete?average:null):!external&&!native.capacitorUnavailableReason?local:null;
 const stable=model?.stableFromFullInAverageModel,fraction=model?.stableFraction;
 const missing=(native.capacitorContributions||[]).filter(c=>c.state!=='available');
 const name=id=>catalog.find(t=>t.id===report.snapshot?.modules?.find(m=>m.workspaceSlotKey===id)?.dogmaTypeId)?.name||id;
 const reason=missing.length?'缺少 '+missing.map(c=>name(c.instanceId)).join('、')+' 的完整耗电数据':average?.exclusions?.length?'部分耗电或供能条件尚未确定':data?.reason||native.capacitorUnavailableReason||'电容计算条件尚未完整';
 const failed=r?.firstFailedPaymentSeconds,zero=r?.firstZeroSeconds,supply=r?.firstSupplyStoppedSeconds;
 // A periodic failure is not a depletion time for the average model. Name it
 // exactly, as the legacy inspector did, while retaining both verdicts.
 const summary=stable===true?'稳定'+(Number.isFinite(fraction)?' · '+fmt(fraction*100,'%'):''):stable===false?(Number.isFinite(failed)?duration(failed)+' · 供电不足':'不稳定 · 续航待确定'):'暂无法确定';
 const recharge=r?.recharge||native.capacitorRecharge||local?.recharge;
 const capTrace=panelTrace(native.attributes?.['ship/482'],'电容容量','GJ',report,catalog),timeTrace=panelTrace(native.attributes?.['ship/55'],'回充时间','s',report,catalog,1000);
 const terms=[['电容容量',fmt(recharge?.capacity,'GJ'),null,capTrace],['回充时间',timeTrace.result,null,timeTrace]];
 const conditions=[['稳定性',stable===true?'平均负载稳定':stable===false?'平均负载不稳定':'未知'],['初始电量',fmt((r?.query?.initialFraction??report.curveRequest?.scenario?.ownCapacitorFraction??1)*100,'%')]];
 if(r){
  conditions.push(['周期查询范围',duration(r.query.horizonSeconds)],['启动供电',Number.isFinite(failed)?duration(failed)+' 后首次不足':'查询范围内均可支付'],['电容归零',Number.isFinite(zero)?duration(zero):'查询范围内未归零']);
  if(Number.isFinite(supply))conditions.push(['注电补给',duration(supply)+' 后首次停止']);
  conditions.push(['边界','使用实际注电弹仓与货舱；付款失败后停用该消费者。未模拟其他装备弹药、热量及完整战斗。']);
  if((data.sources||[]).length)conditions.push(['外部来源','固定对方电量与周期，只计算本舰']);
 }else conditions.push(['曲线不可用',data?.reason||reason]);
 if(stable==null)conditions.push(['不可用原因',reason]);
 if(data?.extensionUnavailableReason)conditions.push(['续航查询未完成',data.extensionUnavailableReason]);
 for(const c of missing)conditions.push(['未计入',name(c.instanceId)+' · '+c.reason]);
 for(const c of average?.exclusions||[])conditions.push(['未计入',c.id+' · '+c.reason]);
 const drain=average?.knownNetDrainGjPerSecond??local?.averageActiveDrain;
 const moduleDrain=local?.averageActiveDrain;
 const moduleTerms=(native.capacitorContributions||[]).map(c=>[name(c.instanceId),c.state==='available'?fmt(c.consumer?.averageDrain,'GJ/s'):'不可用',null,panelReading(name(c.instanceId),c.consumer?.averageDrain,'GJ/s',[['每次耗电',fmt(c.consumer?.capacitorPerCycle,'GJ')],['周期',fmt(c.consumer?.cycleSeconds,'s')]],c.reason?[['原因',c.reason]]:[])]);
 const peak=recharge?.peakRecharge??average?.peakRechargeGjPerSecond;
 let samples=r?.samples||[],horizon=r?.query?.horizonSeconds;
 if(stable===false&&Number.isFinite(failed)){
  const event=r.events?.find(e=>e.timeUs/1e6===failed&&e.payment?.energyReserved===false);
  if(event){samples=[...samples.filter(s=>s.timeSeconds<failed),{timeSeconds:failed,amountGj:event.before}];horizon=failed;}
 }
 const detail={title:'电容续航',result:summary,terms,conditions,nativeCapacitor:{summary,stable,capacity:recharge?.capacity,samples,horizon,failed,zero,peak,drain}};
 return {summary,stable,reason,recharge,capTrace,timeTrace,terms,detail,drain,moduleDrain,moduleTerms,peak,average};
}
export function capacitorHtml(report,catalog=[]){
 const p=capacitorPresentation(report,catalog);
 let html='<div class="panel-title"><span>电容</span><div class="section-summary" '+panelTip(p.detail)+'><b class="'+(p.stable===true?'cap-stable':p.stable===false?'cap-depletes':'cap-undetermined')+'">'+esc(p.summary)+'</b></div></div><div class="stat-block">';
 if(p.recharge)html+=row('容量',fmt(p.recharge.capacity,'GJ'),p.capTrace)+row('回充时间',p.timeTrace.result,p.timeTrace)+row('峰值回充',fmt(p.peak,'GJ/s'),panelReading('峰值回充',p.peak,'GJ/s',p.terms));
 if(Number.isFinite(p.moduleDrain))html+=row('模块耗电',fmt(p.moduleDrain,'GJ/s'),panelReading('模块耗电',p.moduleDrain,'GJ/s',p.moduleTerms,[['口径','已启用本舰模块 · 周期平均耗电']]));
 if(Number.isFinite(p.drain))html+=row(p.average&&!p.average.complete?'已知净耗电':'净耗电',fmt(p.drain,'GJ/s'),panelReading('平均净耗电',p.drain,'GJ/s',[],p.detail.conditions));
 html+='<p class="profile-note">'+(p.stable==null?esc(p.reason):(report.capacitorScenario?.result?.query?.initialFraction??1)===1?'满电起始 · 按模块周期计算':'按情景初始电量 · 按模块周期计算')+'</p></div>';
 return html;
}
