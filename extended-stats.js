import {getLocale} from './i18n.js';

export function extendedStats(report,catalog){
 const a=report.attributes,types=new Map(catalog.map(t=>[t.id,t]));
 const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 const row=(n,v,u='')=>'<div class="stat-row"><span>'+esc(n)+'</span><b>'+Number(v).toLocaleString(getLocale(),{maximumFractionDigits:Math.abs(v)<1?5:2})+' '+u+'</b></div>';
 const group=(n,body)=>body?'<details class="extended-stat-group" open><summary>'+esc(n)+'</summary><div class="stat-block">'+body+'</div></details>':'';
 const distance=(n,v)=>v>0?row(n,v>1000?v/1000:v,v>1000?'km':'m'):'';
 let effects='',mining=0;
 for(const m of report.snapshot.modules){
  const t=types.get(m.dogmaTypeId),active=['Active','Overload'].includes(m.state);if(!t)continue;
  const attr=k=>m.attributeTraces?.[k]?.finalValue;
  if(!active)continue;
  let body='';
  for(const [groupId,label,field,unit] of [[41,'远程回盾','shieldRepairPerSecond','HP/s'],[325,'远程修甲','armorRepairPerSecond','HP/s'],[585,'远程修结构','structureRepairPerSecond','HP/s'],[67,'远程传电','capacitorTransferPerSecond','GJ/s'],[68,'吸电','capacitorTransferPerSecond','GJ/s']])if(t.group===groupId&&m[field]>0)body+=row(label,m[field],unit);
  const neut=attr('energyNeutralizerAmount');
  if(t.group===71&&neut>0&&m.cycleTimeSeconds>0)body+=row('单轮毁电',neut,'GJ')+row('平均毁电',neut/m.cycleTimeSeconds,'GJ/s');
  for(const [key,label,unit] of [['speedFactor','速度修正','%'],['warpScrambleStrength','跃迁扰断强度',''],['scanResolutionBonus','扫描分辨率修正','%'],['maxTargetRangeBonus','锁定距离修正','%'],['signatureRadiusBonus','信号半径修正','%'],['trackingSpeedBonus','跟踪修正','%']]){
   const v=attr(key);if([65,52,209,208,379,201].includes(t.group)&&Number.isFinite(v)&&v!==0)body+=row(label,v,unit);
  }
  for(const [key,label] of [['scanRadarStrengthBonus','雷达干扰强度'],['scanMagnetometricStrengthBonus','磁力干扰强度'],['scanGravimetricStrengthBonus','引力干扰强度'],['scanLadarStrengthBonus','光雷达干扰强度']]){const v=attr(key);if(v>0)body+=row(label,v)}
  if(body)effects+=group(t.name,body+distance('作用距离',m.optimalRangeMeters)+distance('失准距离',m.falloffRangeMeters));
  const yieldAmount=attr('miningAmount');
  if(yieldAmount>0&&m.cycleTimeSeconds>0)mining+=yieldAmount/m.cycleTimeSeconds;
 }
 let droneMining=0;
 for(const d of report.snapshot.droneBay.drones){if(d.state!=='Active'||d.quantity<=0)continue;const amount=d.attributeSnapshot?.miningAmount,cycle=d.cycleTimeSeconds;if(amount>0&&cycle>0)droneMining+=amount*d.quantity/cycle}
 const harvesting=mining||droneMining?'<div class="panel-title">采集</div><div class="stat-block">'+row('模块采集量',mining*60,'m³/min')+row('无人机采集量',droneMining*60,'m³/min')+row('总采集量',(mining+droneMining)*60,'m³/min')+'<p class="profile-note">最终单轮采集量 ÷ 周期 · 无人机连续作业，不含往返卸货。</p></div>':'';
 return harvesting+(effects?'<div class="panel-title">对外效果</div><p class="profile-note">最佳范围内 · 未计目标抗性与电子战叠加</p>'+effects:'');
}
