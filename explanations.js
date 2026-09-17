import {mountDpsChart} from './dps-chart.js';
import {getLocale} from './i18n.js';
// One explanation branch; each locked panel can own a deeper explanation.
export function installExplanations(){
 const chain=[],delay=800;let leaveTimer,lastPointer=null,chartMode='distance';
 const esc=t=>String(t).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 function closeFrom(depth){for(const node of chain.splice(depth)){cancelAnimationFrame(node.frame);node.anchor.removeAttribute('aria-describedby');node.panel.remove();node.bridge.remove();node.aura?.remove()}}
 function lock(node){if(!node.panel.isConnected)return;node.locked=true;node.panel.inert=false;node.panel.classList.add('locked');node.bridge.classList.add('locked');node.aura.classList.add('complete');node.panel.querySelector('.explain-lock').textContent='已锁定 · 可继续查看明细';}
 function place(node){const r=node.anchor.getBoundingClientRect(),p=node.panel,w=p.offsetWidth,h=p.offsetHeight;let x=r.left-w-10;if(x<8)x=r.right+10;if(x+w>innerWidth-8)x=Math.max(8,innerWidth-w-8);const y=Math.max(8,Math.min(r.top,innerHeight-h-8));p.style.left=x+'px';p.style.top=y+'px';const b=node.bridge,left=x+w<=r.left?x+w:r.right,right=x+w<=r.left?r.left:x;Object.assign(b.style,{left:left+'px',top:Math.max(y,r.top)+'px',width:Math.max(0,right-left)+'px',height:Math.max(0,Math.min(y+h,r.bottom)-Math.max(y,r.top))+'px'});}
 function show(anchor){const owner=anchor.closest('.stat-explanation'),depth=owner?Number(owner.dataset.depth)+1:0;if(owner&&!chain[depth-1]?.locked)return;if(chain[depth]?.anchor===anchor)return;clearTimeout(leaveTimer);closeFrom(depth);let detail;try{detail=JSON.parse(anchor.dataset.explain)}catch{return}
  const panel=document.createElement('section'),bridge=document.createElement('div');panel.className='stat-explanation';panel.inert=true;panel.id=depth?'stat-explanation-'+depth:'stat-explanation';panel.dataset.depth=depth;panel.role='tooltip';panel.setAttribute('aria-label',detail.title);panel.style.zIndex=150+depth*2;bridge.className='explain-bridge';bridge.style.zIndex=149+depth*2;
  const lines=rows=>(rows||[]).map(([label,value,kind,child])=>`<div class="explain-line ${kind==='source'?'explain-source':''} ${child?'has-detail':''}" ${child?`tabindex="0" data-explain="${esc(JSON.stringify(child))}"`:''}><span>${esc(label)}</span><b>${esc(value)}${child?' <span class="detail-arrow">›</span>':''}</b></div>`).join('');
  panel.innerHTML=`<div class="explain-heading"><span>${esc(detail.title)}</span></div><div class="explain-lock">停留以锁定</div><div class="explain-terms">${lines(detail.terms?.length?detail.terms:[['贡献项','0']])}</div><div class="explain-result"><span>结果</span><b class="${['scenario-increased','scenario-decreased'].includes(detail.resultClass)?detail.resultClass:''}">${esc(detail.result)}</b></div><div class="explain-conditions">${lines(detail.conditions)}</div>`;
  if(detail.chart){
   panel.classList.add('has-dps-chart');
   const terms=panel.querySelector('.explain-terms'),conditions=panel.querySelector('.explain-conditions'),result=panel.querySelector('.explain-result');
   result.querySelector('span').textContent='当前输出';panel.querySelector('.explain-lock').after(result);
   const host=document.createElement('div');host.className='dps-chart';result.after(host);
   const breakdown=document.createElement('details');breakdown.className='dps-chart-breakdown';breakdown.innerHTML='<summary>输出来源与计算条件</summary>';breakdown.append(terms,conditions);panel.append(breakdown);
  }
  const node={panel,bridge,anchor,locked:false,frame:null,aura:null,chart:null};
  if(detail.chart)node.chart=mountDpsChart(panel.querySelector('.dps-chart'),detail.chart,{mode:chartMode,onMode:mode=>{chartMode=mode}});chain.push(node);document.body.append(bridge,panel);anchor.setAttribute('aria-describedby',panel.id);place(node);animateLock(node);panel.querySelector('.dps-chart-breakdown')?.addEventListener('toggle',()=>{place(node);cancelAnimationFrame(node.frame);node.aura?.remove();if(node.locked){animateLock(node);cancelAnimationFrame(node.frame);lock(node)}else animateLock(node)});
 }
 function inChain(target){return target instanceof Node&&chain.some(n=>n.anchor.contains(target)||n.panel.contains(target)||n.bridge.contains(target))}
 function animateLock(node){
  const r=node.panel.getBoundingClientRect(),w=r.width+12,h=r.height+12,id='liquid-'+node.panel.dataset.depth;
  const aura=document.createElement('div');node.aura=aura;aura.className='lock-aura';aura.setAttribute('aria-hidden','true');
  Object.assign(aura.style,{left:r.left-6+'px',top:r.top-6+'px',width:w+'px',height:h+'px',zIndex:Number(node.panel.style.zIndex)+1});
  // Clockwise from twelve o'clock, along the actual rectangular perimeter.
  const d=`M ${w/2} 6 H ${w-10} Q ${w-6} 6 ${w-6} 10 V ${h-10} Q ${w-6} ${h-6} ${w-10} ${h-6} H 10 Q 6 ${h-6} 6 ${h-10} V 10 Q 6 6 10 6 Z`;
  aura.innerHTML=`<svg width="100%" height="100%" viewBox="0 0 ${w} ${h}"><defs><filter id="${id}" x="-40%" y="-40%" width="180%" height="180%"><feGaussianBlur stdDeviation="0.9"/><feColorMatrix type="matrix" values="1 0 0 0 0 0 1 0 0 0 0 0 1 0 0 0 0 0 2.8 -0.55"/></filter></defs><path class="gold-orbit" d="${d}"/><g class="liquid-tail" filter="url(#${id})">${Array.from({length:15},()=>'<ellipse fill="#e9b84d"/>').join('')}</g><ellipse class="liquid-head" fill="#fff1b2" rx="3" ry="1.4"/></svg>`;
  document.body.append(aura);const path=aura.querySelector('path'),length=path.getTotalLength(),drops=[...aura.querySelectorAll('.liquid-tail ellipse')],head=aura.querySelector('.liquid-head'),reduced=matchMedia('(prefers-reduced-motion: reduce)').matches;
  const start=performance.now();
  function frame(time){if(!node.panel.isConnected)return;const progress=Math.min(1,(time-start)/delay);aura.style.setProperty('--lock-progress',progress);
   if(!reduced){for(let i=0;i<drops.length;i++){const distance=(progress*length-i*3+length)%length,p=path.getPointAtLength(distance),next=path.getPointAtLength((distance+1)%length),width=2.7*(1-i/drops.length)+0.75,wave=Math.sin(time*.025-i*.8);drops[i].setAttribute('rx',String(width+1));drops[i].setAttribute('ry',String(width*.65+wave*.35));drops[i].setAttribute('opacity',String(1-i/drops.length*.85));drops[i].setAttribute('transform',`translate(${p.x} ${p.y}) rotate(${Math.atan2(next.y-p.y,next.x-p.x)*180/Math.PI})`)}const p=path.getPointAtLength(progress*length),next=path.getPointAtLength((progress*length+1)%length);head.setAttribute('transform',`translate(${p.x} ${p.y}) rotate(${Math.atan2(next.y-p.y,next.x-p.x)*180/Math.PI})`)}else{aura.classList.add('reduced');}
   if(progress<1)node.frame=requestAnimationFrame(frame);else lock(node);
  }node.frame=requestAnimationFrame(frame);
 }
 function track(target){
  lastPointer=target;clearTimeout(leaveTimer);let keep=0;
  for(let i=0;i<chain.length;i++){
   const n=chain[i];
   if(target instanceof Node&&(n.anchor.contains(target)||(n.locked&&(n.panel.contains(target)||n.bridge.contains(target)))))keep=i+1;
  }
  // Preview requires uninterrupted dwell on its anchor. Only locked panels
  // receive the crossing grace period and allow interaction inside the panel.
  const preview=chain.findIndex((n,i)=>i>=keep&&!n.locked);
  if(preview>=0)closeFrom(preview);
  if(keep<chain.length)leaveTimer=setTimeout(()=>closeFrom(keep),120);
 }
 document.addEventListener('fitlab-calculation-invalidated',()=>closeFrom(0));
 document.addEventListener('pointerover',e=>{track(e.target);const anchor=e.target.closest('[data-explain]');if(anchor)show(anchor)});
 document.addEventListener('pointerout',e=>track(e.relatedTarget));
 document.addEventListener('focusin',e=>{const anchor=e.target.closest('[data-explain]');if(anchor){show(anchor);const node=chain.find(n=>n.anchor===anchor);if(node){cancelAnimationFrame(node.frame);lock(node)}}});
 document.addEventListener('pointerdown',e=>{if(!inChain(e.target))closeFrom(0)},true);
 document.addEventListener('keydown',e=>{
  if(e.key!=='Tab'||e.ctrlKey||e.altKey||e.metaKey||e.repeat||e.target.closest?.('input,textarea,[contenteditable="true"]'))return;
  const node=chain.find(n=>n.chart&&((lastPointer instanceof Node&&(n.anchor.contains(lastPointer)||n.panel.contains(lastPointer)||n.bridge.contains(lastPointer)))||n.anchor===document.activeElement||n.panel.contains(document.activeElement)));
  if(!node)return;e.preventDefault();e.stopImmediatePropagation();node.chart.cycle(e.shiftKey?-1:1);
 },true);
 document.addEventListener('keydown',e=>{if(e.key==='Escape'&&chain.length){e.preventDefault();e.stopImmediatePropagation();closeFrom(chain.length-1)}},true);
 window.addEventListener('resize',()=>closeFrom(0));
 document.addEventListener('scroll',e=>{if(e.target instanceof Element&&e.target.closest('.stat-explanation'))return;closeFrom(0)},true);
 const observer=new MutationObserver(()=>{const i=chain.findIndex(n=>!n.anchor.isConnected||n.anchor.closest('[hidden]'));if(i>=0)closeFrom(i)});for(const root of document.querySelectorAll('.inspector,#info-window'))observer.observe(root,{childList:true,subtree:true,attributes:true,attributeFilter:['hidden']});
}

