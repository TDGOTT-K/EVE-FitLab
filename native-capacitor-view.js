const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const fmt=(n,u='')=>Number.isFinite(n)?n.toLocaleString('zh-CN',{maximumFractionDigits:2})+(u?' '+u:''):'—';
const row=(title,value)=>'<div class="stat-row"><span>'+esc(title)+'</span><b>'+esc(value)+'</b></div>';
export function capacitorHtml(report){
 const data=report.capacitorScenario,r=data?.result,average=r?.average;
 const failed=r?.firstFailedPaymentSeconds!=null;
 const stable=average?.complete?average.stableFromFullInAverageModel:null;
 const summary=failed?'付款失败 · '+fmt(r.firstFailedPaymentSeconds,'s'):stable===true?'平均稳定':stable===false?'平均不稳定':'稳定性未确定';
 let html='<div class="panel-title"><span>电容</span><div class="section-summary"><b style="color:'+(failed||stable===false?'#f18080':stable===true?'#79d6ab':'var(--muted)')+'">'+summary+'</b></div></div><div class="stat-block">';
 html+='<label class="native-cap-window">观察窗口 <select aria-label="电容观察窗口" data-cap-horizon>'+[60,300,900,3600].map(n=>'<option value="'+n+'" '+(n===(r?.query.horizonSeconds||report.curveRequest?.capacitorHorizon||300)?'selected':'')+'>'+n/60+' 分钟</option>').join('')+'</select></label>';
 if(!r){
  const recharge=report.native?.capacitorRecharge;
  if(recharge)html+=row('容量',fmt(recharge.capacity,'GJ'))+row('峰值回充',fmt(recharge.peakRecharge,'GJ/s'));
  const missing=(report.native?.capacitorContributions||[]).filter(c=>c.state!=='available');
  return html+'<p class="profile-note">周期查询不可用</p><details><summary>原因</summary><p class="profile-note">'+esc(data?.reason||report.native?.capacitorUnavailableReason||'电容查询尚未就绪')+'</p>'+missing.map(c=>'<p class="profile-note">未计入：'+esc(c.instanceId)+' · '+esc(c.reason)+'</p>').join('')+'</details></div>';
 }
 html+=row('容量',fmt(r.recharge.capacity,'GJ'))+row('已知净消耗',fmt(average.knownNetDrainGjPerSecond,'GJ/s'))+row('峰值回充',fmt(average.peakRechargeGjPerSecond,'GJ/s'));
 if(average.complete&&stable===true)html+=row('平均稳定电量',fmt(average.stableFraction*100,'%'));
 html+=row('窗口内最低',fmt(r.minimumAmountGj,'GJ'))+row('窗口结束电量',fmt(r.finalAmountGj,'GJ'));
 html+=row('逐次付款',failed?'首次失败 '+fmt(r.firstFailedPaymentSeconds,'s'):r.sources.some(s=>s.origin==='fitted_activation_cost')?'窗口内均可支付':'无本舰付款项');
 if(r.firstSupplyStoppedSeconds!==null)html+=row('供应首次停止',fmt(r.firstSupplyStoppedSeconds,'s'));
 if(r.firstZeroSeconds!==null)html+=row('首次归零',fmt(r.firstZeroSeconds,'s'));
 html+='<details class="native-cap-curve"><summary>电量曲线与条件</summary><svg viewBox="0 0 240 115" role="img" aria-label="电容随时间变化">';
 const samples=r.samples,high=r.recharge.capacity||1,end=r.query.horizonSeconds||1;
 const path=samples.map((s,i)=>(i?'L':'M')+(10+220*s.timeSeconds/end).toFixed(2)+' '+(90-75*s.amountGj/high).toFixed(2)).join(' ');
 html+='<path d="M10 10V90H230" fill="none" stroke="currentColor" opacity=".3"/><path d="'+path+'" fill="none" stroke="#79c8d3" stroke-width="1.5"/><text x="10" y="110">0 s</text><text x="230" y="110" text-anchor="end">'+fmt(r.query.horizonSeconds,'s')+'</text>';
 for(const sample of samples)html+='<circle cx="'+(10+220*sample.timeSeconds/end)+'" cy="'+(90-75*sample.amountGj/high)+'" r="2" fill="#79c8d3"><title>'+fmt(sample.timeSeconds,'s')+' · '+fmt(sample.amountGj,'GJ')+'</title></circle>';
 html+='</svg><p class="profile-note">初始电量 '+fmt(r.query.initialFraction*100,'%')+' · 使用已声明弹仓与共享货舱 · 付款失败后停止该消费者。平均稳定不保证每次付款；窗口结束不等于无限续航。</p>';
 for(const s of data.sources)html+=row(s.operation==='nos_target'?'吸电目标':s.operation==='transmit'?'传电来源':'毁电/吸电来源',s.name+' · '+fmt(s.distanceMeters/1000,'km')+(s.amountGj!=null?' · 固定 '+fmt(s.amountGj,'GJ'):''));
 if(data.sources.length)html+='<p class="profile-note">仅计算本舰。对方电量为固定边界，外部模块按固定周期作用；不联算对方付款或资源变化。</p>';
 for(const e of average.exclusions)html+='<p class="profile-note">平均模型未计入：'+esc(e.id)+' · '+esc(e.reason==='RESOURCE_AND_CONDITION_DEPENDENT_NOS'?'吸电收益依赖双方电量':e.reason)+'</p>';
 for(const supply of r.supplies)html+=row('注电器 '+supply.id,'弹仓 '+supply.loaded+(supply.cargoKey?' · 使用共享货舱':' · 私有储备 '+supply.reserve)+(supply.reserved?' · 已预约 '+supply.reserved:''));
 for(const [key,amount] of Object.entries(r.sharedCargo))html+=row('共享备弹 '+key,String(amount));
 html+='<p class="profile-note">同刻按来源 ID 顺序结算；不模拟非注电器弹药、动态热量及其他模块效果。</p></details></div>';
 return html;
}
