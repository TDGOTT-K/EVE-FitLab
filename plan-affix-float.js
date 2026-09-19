export function installAffixFloat(host){
 const root=document.createElement('div');root.className='plan-affix-float';root.hidden=true;
 root.innerHTML='<button type="button" class="plan-affix-orb" aria-label="展开方案加成" aria-expanded="false" aria-controls="plan-affix-float-panel" title="方案加成 · 点击展开，拖动调整位置"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 7h8M5 12h14M5 17h10"/><circle cx="17" cy="7" r="2"/></svg><span>加成</span></button><section id="plan-affix-float-panel" class="plan-affixes" aria-label="方案效果汇总" hidden><div class="plan-affix-float-head"><span>方案加成</span><button type="button" aria-label="恢复默认面板大小" title="恢复默认大小"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M8 3H3v5m13-5h5v5M3 16v5h5m13-5v5h-5"/><rect x="8" y="8" width="8" height="8" rx="1"/></svg></button></div><div data-plan-affixes>查询引擎…</div></section>';
 host.append(root);
 const orb=root.querySelector('.plan-affix-orb'),panel=root.querySelector('.plan-affixes'),reset=panel.querySelector('button');
 let x=innerWidth-76,y=Math.round(innerHeight*.55),expanded=false,drag=null,suppressClick=false,width=330,height=null,offsetX=null,offsetY=null,resizing=null;
 try{const saved=JSON.parse(localStorage.getItem('fitlab-plan-affix-float')||'null');if(Number.isFinite(saved?.x)&&Number.isFinite(saved?.y)){x=saved.x;y=saved.y;expanded=saved.expanded===true;if(Number.isFinite(saved.width))width=saved.width;if(Number.isFinite(saved.height))height=saved.height;if(Number.isFinite(saved.offsetX)&&Number.isFinite(saved.offsetY)){offsetX=saved.offsetX;offsetY=saved.offsetY}}}catch{}
 const save=()=>{try{localStorage.setItem('fitlab-plan-affix-float',JSON.stringify({x,y,expanded,width,height,offsetX,offsetY}))}catch{}};
 function position(){
  x=Math.max(8,Math.min(x,innerWidth-60));y=Math.max(8,Math.min(y,innerHeight-60));
  root.style.left=x+'px';root.style.top=y+'px';
  const w=Math.min(Math.max(240,width),innerWidth-16),h=Math.min(height===null?360:Math.max(140,height),innerHeight-16);
  panel.style.width=w+'px';panel.style.height=height===null?'':h+'px';panel.style.maxHeight=h+'px';
  const left=offsetX===null?(x-w-12>=8?x-w-12:Math.min(innerWidth-w-8,x+64)):x+offsetX;
  panel.style.left=Math.max(8,Math.min(left,innerWidth-w-8))+'px';
  panel.style.top=Math.max(8,Math.min(offsetY===null?y:y+offsetY,innerHeight-h-8))+'px';
 }
 function render(){panel.hidden=!expanded;orb.setAttribute('aria-expanded',String(expanded));orb.setAttribute('aria-label',expanded?'收起方案加成':'展开方案加成');position()}
 function cancel(){if(!drag)return;x=drag.x;y=drag.y;suppressClick=drag.moved;drag=null;root.classList.remove('dragging');position()}
 orb.onpointerdown=e=>{if(e.button!==0)return;drag={id:e.pointerId,startX:e.clientX,startY:e.clientY,x,y,moved:false};suppressClick=false;orb.setPointerCapture(e.pointerId)};
 orb.onpointermove=e=>{if(!drag||e.pointerId!==drag.id)return;const dx=e.clientX-drag.startX,dy=e.clientY-drag.startY;if(!drag.moved&&Math.hypot(dx,dy)<6)return;drag.moved=true;root.classList.add('dragging');x=drag.x+dx;y=drag.y+dy;position()};
 orb.onpointerup=e=>{if(!drag||e.pointerId!==drag.id)return;suppressClick=drag.moved;drag=null;root.classList.remove('dragging');save()};
 orb.onpointercancel=cancel;orb.onlostpointercapture=()=>{if(drag)cancel()};
 orb.onclick=e=>{if(suppressClick&&e.detail!==0){suppressClick=false;return}suppressClick=false;expanded=!expanded;render();save()};
 orb.oncontextmenu=e=>{if(drag){e.preventDefault();cancel()}};
  reset.onclick=()=>{width=330;height=null;offsetX=null;offsetY=null;position();save()};
 function cancelResize(){
  if(!resizing)return;
  ({width,height,offsetX,offsetY}=resizing.before);resizing=null;panel.classList.remove('resizing');position();
 }
 function resizeTo(edge,rect,dx,dy){
  let left=rect.x,right=rect.x+rect.width,top=rect.y,bottom=rect.y+rect.height;
  const minW=Math.min(240,innerWidth-16),minH=Math.min(140,innerHeight-16);
  if(edge.includes('e'))right=Math.min(innerWidth-8,Math.max(left+minW,right+dx));
  if(edge.includes('w'))left=Math.max(8,Math.min(right-minW,left+dx));
  if(edge.includes('s'))bottom=Math.min(innerHeight-8,Math.max(top+minH,bottom+dy));
  if(edge.includes('n'))top=Math.max(8,Math.min(bottom-minH,top+dy));
  width=right-left;height=bottom-top;offsetX=left-x;offsetY=top-y;position();
 }
 for(const edge of ['n','e','s','w','ne','nw','se','sw']){
  const handle=document.createElement('div');handle.className='plan-affix-resize '+edge;handle.dataset.edge=edge;
  if(edge==='se'){handle.tabIndex=0;handle.setAttribute('role','button');handle.setAttribute('aria-label','调整面板大小，使用方向键');}
  else handle.setAttribute('aria-hidden','true');
  panel.append(handle);
  handle.onpointerdown=e=>{if(e.button!==0)return;e.preventDefault();e.stopPropagation();handle.focus();resizing={id:e.pointerId,edge,rect:panel.getBoundingClientRect(),startX:e.clientX,startY:e.clientY,before:{width,height,offsetX,offsetY}};handle.setPointerCapture(e.pointerId);panel.classList.add('resizing')};
  handle.onpointermove=e=>{if(resizing?.id===e.pointerId)resizeTo(edge,resizing.rect,e.clientX-resizing.startX,e.clientY-resizing.startY)};
  handle.onpointerup=e=>{if(resizing?.id!==e.pointerId)return;resizing=null;panel.classList.remove('resizing');save()};
  handle.onpointercancel=cancelResize;handle.onlostpointercapture=cancelResize;
  handle.oncontextmenu=e=>{if(resizing){e.preventDefault();cancelResize()}};
  handle.onkeydown=e=>{if(!['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(e.key))return;e.preventDefault();e.stopPropagation();resizeTo('se',panel.getBoundingClientRect(),e.key==='ArrowLeft'?-10:e.key==='ArrowRight'?10:0,e.key==='ArrowUp'?-10:e.key==='ArrowDown'?10:0);save()};
 }

 root.onkeydown=e=>{if(e.key==='Escape'){e.preventDefault();e.stopPropagation();if(resizing)cancelResize();else if(drag)cancel();else{expanded=false;render();save();orb.focus()}}};
 window.addEventListener('resize',()=>{cancelResize();position()});
 render();
 return {setVisible(value){if(!value){cancel();cancelResize()}root.hidden=!value;if(value)position()}};
}
