import {escapeHtml,metricText} from './scenario-display.js';

export function mountDpsChart(root,data,{mode='distance',onMode=()=>{}}={}){
 const damageUnit=data.attackMode==='edps'?'EDPS':'DPS';
 const keys=['distance','signature','angular'];let index=Math.max(0,keys.indexOf(mode));
 const fmtX=(x,s)=>metricText(s.key==='distance'?x/1000:x,s.unit,s.key==='angular'?5:2);
 function render(){
  const labels=['距离','目标信号半径','角速度'];
  let modes=root.querySelector('.dps-chart-modes');
  if(!modes){
   root.innerHTML='<div class="dps-chart-modes" role="group" aria-label="DPS 曲线类型">'+labels.map((label,i)=>'<button type="button" data-curve-mode="'+keys[i]+'">'+label+'</button>').join('')+'<small><kbd>Tab</kbd> 切换</small></div>';
   modes=root.querySelector('.dps-chart-modes');
   modes.querySelectorAll('button').forEach((button,i)=>button.onclick=()=>{if(index===i)return;index=i;onMode(keys[index]);render()});
  }
  for(const child of [...root.children])if(child!==modes)child.remove();
  modes.querySelectorAll('button').forEach((button,i)=>{button.classList.toggle('active',i===index);button.setAttribute('aria-pressed',String(i===index))});
  root.dataset.mode=keys[index];
  if(data.status!=='ready'){const p=document.createElement('p');p.className='dps-chart-empty';p.textContent=data.reason;root.append(p);return;}
  const s=data.series.find(s=>s.key===keys[index]);if(!s)return;
  const W=440,H=226,L=47,R=422,T=20,B=186;
  const magnitude=10**Math.floor(Math.log10(data.yMax||1)),top=Math.ceil(data.yMax*1.08/magnitude*2)/2*magnitude;
  const x=v=>L+(v-s.min)/(s.max-s.min)*(R-L),y=v=>B-v/top*(B-T);
  let path='',connected=false;
  for(const [px,py] of s.points){if(!Number.isFinite(py)){connected=false;continue}path+=(connected?'L':'M')+x(px).toFixed(3)+' '+y(py).toFixed(3);connected=true;}
  let grid='';for(let i=0;i<=4;i++){const value=top*i/4,sy=y(value);grid+='<line class="chart-grid" x1="'+L+'" x2="'+R+'" y1="'+sy+'" y2="'+sy+'"/><text x="'+(L-7)+'" y="'+(sy+4)+'" text-anchor="end">'+metricText(value,'',1)+'</text>';}
  for(let i=0;i<=4;i++){const value=s.min+(s.max-s.min)*i/4;grid+='<text x="'+x(value)+'" y="'+(B+19)+'" text-anchor="middle">'+metricText(s.key==='distance'?value/1000:value,'',s.key==='angular'?4:1)+'</text>';}
  const currentX=x(s.currentX),currentY=y(s.currentY);
  const currentMarkup=data.ideal?'': '<line class="dps-current-line" x1="'+currentX+'" x2="'+currentX+'" y1="'+T+'" y2="'+B+'"/><circle class="dps-current-point" cx="'+currentX+'" cy="'+currentY+'" r="4"/>';
  root.insertAdjacentHTML('beforeend','<div class="dps-chart-current">'+(data.ideal?'<span>理想条件</span><b>峰值 '+escapeHtml(metricText(Math.max(...s.points.map(p=>p[1])),damageUnit))+'</b>':'<span>当前 '+escapeHtml(fmtX(s.currentX,s))+'</span><b>'+escapeHtml(metricText(s.currentY,damageUnit))+'</b>')+'</div><svg class="dps-function-plot" viewBox="0 0 '+W+' '+H+'" role="img" aria-label="'+damageUnit+' 随'+s.label+'变化"><text x="'+L+'" y="11">'+damageUnit+'</text>'+grid+'<path class="dps-curve" d="'+path+'"/>'+currentMarkup+'<g class="dps-probe" visibility="hidden"><line y1="'+T+'" y2="'+B+'"/><circle r="3"/><text class="dps-probe-percent"/></g><text x="'+R+'" y="222" text-anchor="end">'+s.label+' / '+s.unit+'</text><rect class="dps-chart-hit" x="'+L+'" y="0" width="'+(R-L)+'" height="'+B+'"/></svg><div class="dps-chart-probe-value" aria-live="off">&nbsp;</div>');
  const t=data.target||{},fixed=[['distance','距离',t.distance/1000,'km'],['signature','信号半径',t.signature,'m'],['angular','角速度',t.angular,'rad/s']].filter(([key])=>key!==s.key).map(([key,label,value,unit])=>label+' '+metricText(value,unit,key==='angular'?5:2));
  fixed.push('相对速度 '+metricText(t.speed,'m/s'));
  const note=document.createElement('div');note.className='dps-chart-fixed';note.textContent=data.ideal?s.fixed:fixed.join(' · ');root.append(note);
  const scope=document.createElement('div');scope.className='dps-chart-scope';scope.textContent=data.ideal?'按当前配装与启用状态 · 不含换弹 · 参考半径非具体舰船实值':(data.attackMode==='edps'?'已扣目标抗性':'不扣目标抗性')+' · 不含换弹 · 其余条件固定';root.append(scope);
  const svg=root.querySelector('svg'),hit=root.querySelector('.dps-chart-hit'),probe=root.querySelector('.dps-probe'),readout=root.querySelector('.dps-chart-probe-value');
  hit.onpointermove=e=>{
   const p=new DOMPoint(e.clientX,e.clientY).matrixTransform(svg.getScreenCTM().inverse()),value=s.min+(p.x-L)/(R-L)*(s.max-s.min);
   const sample=s.points.reduce((best,row)=>Math.abs(row[0]-value)<Math.abs(best[0]-value)?row:best,s.points[0]);
   readout.textContent='采样 '+fmtX(sample[0],s)+' · '+metricText(sample[1],damageUnit);
   if(s.key==='angular'){
    const speed=document.createElement('span');speed.className='dps-angular-speed';
    speed.textContent='10 km 处横向速度 '+metricText(sample[0]*10000,'m/s',0);readout.append(speed);
   }
   if(!Number.isFinite(sample[1])){probe.setAttribute('visibility','hidden');return;}
   probe.setAttribute('visibility','visible');const line=probe.querySelector('line'),dot=probe.querySelector('circle');line.setAttribute('x1',x(sample[0]));line.setAttribute('x2',x(sample[0]));dot.setAttribute('cx',x(sample[0]));dot.setAttribute('cy',y(sample[1]));
   const label=probe.querySelector('.dps-probe-percent'),px=x(sample[0]),py=y(sample[1]),flip=px>R-75;
   label.textContent=Number.isFinite(data.totalDps)&&data.totalDps>0?metricText(sample[1]/data.totalDps*100,'%',1):'';
   label.setAttribute('text-anchor',flip?'end':'start');
   label.setAttribute('x',px+(flip?-8:8));
   label.setAttribute('y',py<T+18?py+16:py-9);
  };
  hit.onpointerleave=()=>{probe.setAttribute('visibility','hidden');readout.innerHTML='&nbsp;'};
 }
 render();return {cycle(direction=1){index=(index+direction+3)%3;onMode(keys[index]);render();},mode:()=>keys[index]};
}
