import {escapeHtml,metricText} from './scenario-display.js';

const timeText=seconds=>seconds<60?metricText(seconds,'s',1):metricText(seconds/60,'min',1);
export function capacitorFlowDetail(title,report){
 const cap=report.capacitorAnalysis||{},remote=title==='友方传电'||title==='受到毁电';
 const field={'友方传电':'incomingTransferPerSecond','受到毁电':'incomingNeutPerSecond','吸电收益':'nosferatuPerSecond','注电收益':'injectionPerSecond'}[title];
 if(!field)return null;
 const terms=[],conditions=[],value=cap[field];
 if(remote){
  const link=report.scenarioLinks?.[title==='友方传电'?'supportFitId':'hostileFitId'];
  if(link){
   terms.push(['来源',link.name]);
   if(Number.isFinite(link.distance))terms.push(['距离',metricText(link.distance/1000,'km')]);
   const groups=new Map();
   for(const m of link.modules||[]){
    const key=m.name+'|'+m.status,group=groups.get(key)||{name:m.name,status:m.status,count:0,rate:0,known:true};
    group.count++;group.rate+=m.perSecond||0;group.known&&=Number.isFinite(m.perSecond);groups.set(key,group);
   }
   for(const m of groups.values())terms.push([m.name+(m.count>1?' ×'+m.count:''),m.known&&m.rate>0?metricText(m.rate*(title==='受到毁电'?(cap.energyWarfareResonance??1):1),'GJ/s')+(m.status==='失准期望值'?' · 射程折算':''):m.status]);
   if(link.note)conditions.push(['未生效',link.note]);
  }else conditions.push(['来源','未配置来源装配']);
  if(title==='受到毁电'&&Number.isFinite(cap.energyWarfareResonance)&&cap.energyWarfareResonance!==1)terms.push(['本舰毁电减免',metricText((1-cap.energyWarfareResonance)*100,'%',1)]);
  conditions.push(['口径',title==='受到毁电'?'平均损失 · 已计本舰抗性':'平均传入 · 来源持续运行']);
 }else if(title==='吸电收益'){
  for(const [name,rate] of cap.nosferatuSources||[])terms.push([name,metricText(rate,'GJ/s')]);
  conditions.push(['条件','目标电量充足，并满足吸电条件']);
 }else conditions.push(['口径','含弹夹与换弹 · 持续补充弹药']);
 return {title,result:metricText(value,'GJ/s'),terms,conditions,flow:true};
}

export function capacitorSummary(cap){
 if(!cap)return '等待计算';
 if(cap.status==='stable')return '稳定 · '+metricText(cap.lowPercent,'%',1)+(cap.highPercent-cap.lowPercent>.1?'–'+metricText(cap.highPercent,'%',1):'');
 if(cap.status==='depletes')return timeText(cap.seconds)+' 后启动供电不足';
 if(cap.status==='bounded')return timeText(cap.checkedSeconds)+' 内可运行 · 稳定性未确认';
 return cap.reason||'电容结果不可用';
}

export function capacitorDetail(report){
 const cap=report.capacitorAnalysis;
 return {title:'电容续航',result:capacitorSummary(cap),capacitor:cap||{},
  scenarioName:report.workspace?.active?report.workspace.name:'',
  baseline:report.workspace?.active?capacitorSummary(report.workspace.baseline.capacitorAnalysis):''};
}

export function mountCapacitorChart(root,detail){
 const cap=detail.capacitor,points=cap.timeline||[],esc=escapeHtml;
 root.innerHTML='<div class="cap-verdict '+(cap.status==='stable'?'cap-stable':cap.status==='depletes'?'cap-depletes':'')+'">'+esc(capacitorSummary(cap))+'</div>'+
  (detail.scenarioName?'<div class="cap-context">'+esc(detail.scenarioName)+(detail.baseline?' · 无情景：'+esc(detail.baseline):'')+'</div>':'');
 if(!['stable','depletes','bounded'].includes(cap.status))return;
 const end=Math.max(1,points.at(-1)?.[0]||0),L=42,R=420,T=20,B=170;
 const x=t=>L+t/end*(R-L),y=p=>B-p/100*(B-T);
 let grid='';
 for(const percent of [0,25,50,75,100])grid+='<line class="chart-grid" x1="'+L+'" x2="'+R+'" y1="'+y(percent)+'" y2="'+y(percent)+'"/><text x="'+(L-7)+'" y="'+(y(percent)+4)+'" text-anchor="end">'+percent+'</text>';
 for(let i=0;i<=4;i++)grid+='<text x="'+x(end*i/4)+'" y="191" text-anchor="middle">'+esc(metricText(end*i/4/(end>=120?60:1),'',1))+'</text>';
 // Samples retain both sides of module/external pulses; no smoothing of jumps.
 const path=points.map(([t,before,after,paid],i)=>(i?'L':'M')+x(t)+' '+y(before)+(Number.isFinite(paid)?'L'+x(t)+' '+y(paid):'')+'L'+x(t)+' '+y(after)).join('');
 if(points.length){
  root.insertAdjacentHTML('beforeend','<svg class="cap-function-plot dps-function-plot" viewBox="0 0 440 214" role="img" aria-label="电容百分比随时间变化"><text x="'+L+'" y="11">电容 %</text>'+grid+'<path class="dps-curve" d="'+path+'"/><g class="cap-probe" visibility="hidden"><line class="dps-current-line" y1="'+T+'" y2="'+B+'"/><circle class="dps-current-point" r="3"/></g><text x="'+R+'" y="210" text-anchor="end">时间 / '+(end>=120?'min':'s')+'</text><rect class="cap-chart-hit dps-chart-hit" x="'+L+'" y="0" width="'+(R-L)+'" height="'+B+'"/></svg><div class="cap-probe-value">满电起始 · 周期事件采样</div>');
  const svg=root.querySelector('svg'),hit=root.querySelector('.cap-chart-hit'),probe=root.querySelector('.cap-probe'),readout=root.querySelector('.cap-probe-value');
  hit.onpointermove=e=>{
   const p=new DOMPoint(e.clientX,e.clientY).matrixTransform(svg.getScreenCTM().inverse()),t=(p.x-L)/(R-L)*end;
   const sample=points.reduce((best,row)=>Math.abs(row[0]-t)<Math.abs(best[0]-t)?row:best,points[0]);
   probe.setAttribute('visibility','visible');
   const line=probe.querySelector('line'),dot=probe.querySelector('circle');
   line.setAttribute('x1',x(sample[0]));line.setAttribute('x2',x(sample[0]));dot.setAttribute('cx',x(sample[0]));dot.setAttribute('cy',y(sample[2]));
   readout.textContent=timeText(sample[0])+' · '+metricText(sample[2],'%',1)+' · '+metricText(sample[2]*cap.capacity/100,'GJ',1);
  };
  hit.onpointerleave=()=>{probe.setAttribute('visibility','hidden');readout.textContent='满电起始 · 周期事件采样';};
 }
 const flows=[['自身回充 · 峰值',cap.peakRechargePerSecond,1],['模块耗电',cap.usagePerSecond,-1],['吸电收益',cap.nosferatuPerSecond,1],['注电收益',cap.injectionPerSecond,1],['受到传电',cap.incomingTransferPerSecond,1],['受到毁电',cap.incomingNeutPerSecond,-1]];
 root.insertAdjacentHTML('beforeend','<div class="cap-flows">'+flows.filter(([,value])=>Number.isFinite(value)&&value>0).map(([label,value,sign])=>'<div><span>'+label+'</span><b class="'+(sign>0?'cap-income':'cap-expense')+'">'+(sign>0?'+':'−')+esc(metricText(value,'GJ/s'))+'</b></div>').join('')+'</div>');
 const notes=['回充随电量变化，峰值位于 25% 电容'];
 if(cap.status==='depletes')notes.push('终点为首次无法支付启动耗电，剩余 '+metricText(cap.remainingPercent,'%',1));
 if(cap.includesBoosters)notes.push('注电含换弹，假设弹药持续补充');
 if(cap.nosferatuPerSecond>0)notes.push('吸电假设目标电量充足且满足吸电条件');
 if(cap.incomingNeutPerSecond>0)notes.push('毁电已计本舰电容战抗性');
 if(cap.incomingTransferPerSecond>0||cap.incomingNeutPerSecond>0)notes.push('外部来源持续运行，仅判断本舰');
 root.insertAdjacentHTML('beforeend','<div class="cap-notes">'+notes.map(esc).join(' · ')+'</div>');
}
