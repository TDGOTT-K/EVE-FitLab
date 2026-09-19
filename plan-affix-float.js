export function installAffixFloat(host){
 const root=document.createElement('div');root.className='plan-affix-float';root.hidden=true;
 root.innerHTML='<button type="button" class="plan-affix-orb" aria-label="展开方案加成" aria-expanded="false" aria-controls="plan-affix-float-panel" title="方案加成 · 点击展开，拖动调整位置"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 7h8M5 12h14M5 17h10"/><circle cx="17" cy="7" r="2"/></svg><span>加成</span></button><section id="plan-affix-float-panel" class="plan-affixes" aria-label="方案效果汇总" hidden><div class="plan-affix-float-head"><span>方案加成</span><button type="button" aria-label="收起方案加成" title="收起">−</button></div><div data-plan-affixes>查询引擎…</div></section>';
 host.append(root);
 const orb=root.querySelector('.plan-affix-orb'),panel=root.querySelector('.plan-affixes'),close=panel.querySelector('button');
 let x=innerWidth-76,y=Math.round(innerHeight*.55),expanded=false,drag=null,suppressClick=false;
 try{const saved=JSON.parse(localStorage.getItem('fitlab-plan-affix-float')||'null');if(Number.isFinite(saved?.x)&&Number.isFinite(saved?.y)){x=saved.x;y=saved.y;expanded=saved.expanded===true}}catch{}
 const save=()=>{try{localStorage.setItem('fitlab-plan-affix-float',JSON.stringify({x,y,expanded}))}catch{}};
 function position(){
  x=Math.max(8,Math.min(x,innerWidth-60));y=Math.max(8,Math.min(y,innerHeight-60));
  root.style.left=x+'px';root.style.top=y+'px';
  const width=Math.min(330,innerWidth-16),height=Math.min(360,innerHeight-16);
  panel.style.width=width+'px';panel.style.maxHeight=height+'px';
  const left=x-width-12>=8?x-width-12:Math.min(innerWidth-width-8,x+64);
  panel.style.left=Math.max(8,left)+'px';panel.style.top=Math.max(8,Math.min(y,innerHeight-height-8))+'px';
 }
 function render(){panel.hidden=!expanded;orb.setAttribute('aria-expanded',String(expanded));orb.setAttribute('aria-label',expanded?'收起方案加成':'展开方案加成');position()}
 function cancel(){if(!drag)return;x=drag.x;y=drag.y;suppressClick=drag.moved;drag=null;root.classList.remove('dragging');position()}
 orb.onpointerdown=e=>{if(e.button!==0)return;drag={id:e.pointerId,startX:e.clientX,startY:e.clientY,x,y,moved:false};suppressClick=false;orb.setPointerCapture(e.pointerId)};
 orb.onpointermove=e=>{if(!drag||e.pointerId!==drag.id)return;const dx=e.clientX-drag.startX,dy=e.clientY-drag.startY;if(!drag.moved&&Math.hypot(dx,dy)<6)return;drag.moved=true;root.classList.add('dragging');x=drag.x+dx;y=drag.y+dy;position()};
 orb.onpointerup=e=>{if(!drag||e.pointerId!==drag.id)return;suppressClick=drag.moved;drag=null;root.classList.remove('dragging');save()};
 orb.onpointercancel=cancel;orb.onlostpointercapture=()=>{if(drag)cancel()};
 orb.onclick=e=>{if(suppressClick&&e.detail!==0){suppressClick=false;return}suppressClick=false;expanded=!expanded;render();save()};
 orb.oncontextmenu=e=>{if(drag){e.preventDefault();cancel()}};
 close.onclick=()=>{expanded=false;render();save();orb.focus()};
 root.onkeydown=e=>{if(e.key==='Escape'){e.preventDefault();e.stopPropagation();if(drag)cancel();else{expanded=false;render();save();orb.focus()}}};
 window.addEventListener('resize',position);
 render();
 return {setVisible(value){if(!value)cancel();root.hidden=!value;if(value)position()}};
}
