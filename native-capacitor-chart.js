// Render native samples only. Percentages are display unit conversions.
import {escapeHtml,metricText} from './scenario-display.js';
export function mountNativeCapacitorChart(root,data){
 const esc=escapeHtml,points=data.samples||[],capacity=data.capacity;
 root.innerHTML='<div class="cap-verdict '+(data.stable===true?'cap-stable':data.stable===false?'cap-depletes':'')+'">'+esc(data.summary)+'</div>';
 if(!points.length||!Number.isFinite(capacity)||capacity<=0)return;
 const end=data.horizon||points.at(-1).timeSeconds||1,L=42,R=420,T=20,B=170;
 const x=t=>L+t/end*(R-L),y=q=>B-q/capacity*(B-T);
 let grid='';
 for(const percent of [0,25,50,75,100])grid+='<line class="chart-grid" x1="'+L+'" x2="'+R+'" y1="'+y(capacity*percent/100)+'" y2="'+y(capacity*percent/100)+'"/><text x="'+(L-7)+'" y="'+(y(capacity*percent/100)+4)+'" text-anchor="end">'+percent+'</text>';
 for(let i=0;i<=4;i++)grid+='<text x="'+x(end*i/4)+'" y="191" text-anchor="middle">'+esc(metricText(end*i/240,'',1))+'</text>';
 const path=points.map((s,i)=>(i?'L':'M')+x(s.timeSeconds)+' '+y(s.amountGj)).join('');
 root.insertAdjacentHTML('beforeend','<svg class="cap-function-plot dps-function-plot" viewBox="0 0 440 214" role="img" aria-label="电容百分比随时间变化"><text x="42" y="11">电容 %</text>'+grid+'<path class="dps-curve" d="'+path+'"/><g class="cap-probe" visibility="hidden"><line class="dps-current-line" y1="20" y2="170"/><circle class="dps-current-point" r="3"/></g><text x="420" y="210" text-anchor="end">时间 / min</text><rect class="cap-chart-hit dps-chart-hit" x="42" y="0" width="378" height="170"/></svg><div class="cap-probe-value">电量采样 · 移动光标查看</div>');
 const svg=root.querySelector('svg'),hit=root.querySelector('.cap-chart-hit'),probe=root.querySelector('.cap-probe'),readout=root.querySelector('.cap-probe-value');
 hit.onpointermove=e=>{
  const p=new DOMPoint(e.clientX,e.clientY).matrixTransform(svg.getScreenCTM().inverse()),t=(p.x-L)/(R-L)*end;
  const sample=points.reduce((best,s)=>Math.abs(s.timeSeconds-t)<Math.abs(best.timeSeconds-t)?s:best,points[0]);
  probe.setAttribute('visibility','visible');const line=probe.querySelector('line'),dot=probe.querySelector('circle');
  line.setAttribute('x1',x(sample.timeSeconds));line.setAttribute('x2',x(sample.timeSeconds));dot.setAttribute('cx',x(sample.timeSeconds));dot.setAttribute('cy',y(sample.amountGj));
  readout.textContent=metricText(sample.timeSeconds/60,'min',1)+' · '+metricText(sample.amountGj/capacity*100,'%',1)+' · '+metricText(sample.amountGj,'GJ',1);
 };
 hit.onpointerleave=()=>{probe.setAttribute('visibility','hidden');readout.textContent='电量采样 · 移动光标查看';};
 root.insertAdjacentHTML('beforeend','<div class="cap-flows">'+[['回充 · 峰值',data.peak],['净耗电 · 平均',data.drain]].filter(([,v])=>Number.isFinite(v)).map(([label,v])=>'<div><span>'+label+'</span><b>'+esc(metricText(v,'GJ/s'))+'</b></div>').join('')+'</div>');
}
