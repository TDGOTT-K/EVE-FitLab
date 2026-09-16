export function installTargetMapControls(root,{form,fits,ownShipId,shipName,onTargetChange,health,plane}){
 const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 const svg=root.querySelector('svg'),enemy=root.querySelector('.enemy-entity'),own=root.querySelector('.own-entity');
 const image=(id,x,y)=>'<image x="'+x+'" y="'+y+'" width="32" height="32" href="https://images.evetech.net/types/'+Number(id)+'/icon?size=64"/>';
 own.innerHTML=image(ownShipId,284,154)+'<text x="300" y="205" text-anchor="middle">本舰</text>';
 let shield=health?.shield??(form.elements.targetLayer.value==='shield'),armor=health?.armor??(form.elements.targetLayer.value!=='structure');
 const panel=document.createElement('div');panel.className='target-map-menu';panel.hidden=true;root.append(panel);
 const summary=document.createElement('div');summary.className='target-map-selection';root.querySelector('.target-plane-help').after(summary);
 const get=key=>fits.find(f=>f.id===form.elements[key].value);
 function sync(){const fit=get('targetFitId');plane.setTargetVisible(!!fit);enemy.innerHTML=(fit?image(fit.shipId,-16,-16):'<rect x="-14" y="-14" width="28" height="28" fill="transparent" stroke="currentColor" stroke-dasharray="3 2"/><text y="5" text-anchor="middle">?</text>')+'<text y="-24" text-anchor="middle">'+esc(fit?shipName(fit.shipId):'右键选择目标')+'</text>'+[['shield','护盾',shield],['armor','装甲',armor],['structure','结构',true]].map(([k,n,full],i)=>'<g class="target-health" data-health="'+k+'" '+(k!=='structure'?'role="button" tabindex="0"':'')+' aria-label="'+n+' '+(full?'100':'0')+'%"><rect x="-43" y="'+(26+i*19)+'" width="86" height="16" class="target-health-track"/><rect x="-43" y="'+(26+i*19)+'" width="'+(full?86:0)+'" height="16" class="target-health-fill"/><text x="0" y="'+(38+i*19)+'" text-anchor="middle">'+n+' '+(full?'100':'0')+'%</text></g>').join('');
  form.elements.targetLayer.value=shield?'shield':armor?'armor':'structure';
  for(const [key,label] of [['support','传电'],['hostile','毁电']]){const f=get(key+'FitId');plane.setSource(key,!!f,f?image(f.shipId,-16,-16)+'<text y="-25" text-anchor="middle">'+esc(label+' · '+f.name)+'</text><text y="34" text-anchor="middle" data-distance></text>':'');}
  summary.textContent=(fit?fit.name:'右键敌舰选择装配')+[['supportFitId','传电'],['hostileFitId','毁电']].map(([key,label])=>get(key)?' · '+label+'：'+get(key).name:'').join('');
  enemy.querySelectorAll('[data-health]').forEach(el=>{const toggle=e=>{e.preventDefault();e.stopPropagation();if(el.dataset.health==='shield')shield=!shield;else if(el.dataset.health==='armor')armor=!armor;sync()};el.onpointerdown=e=>e.stopPropagation();el.onclick=toggle;el.onkeydown=e=>{if(e.key==='Enter'||e.key===' '){toggle(e)}}});
 }
 root.addEventListener('scenario-geometry',()=>{for(const key of ['support','hostile']){const p=plane.sourceValue(key);form.elements[key+'Distance'].value=Math.hypot(p.x,p.y)}});
 function position(e){panel.hidden=false;const r=root.getBoundingClientRect();panel.style.left=Math.max(0,Math.min(e.clientX-r.left,root.clientWidth-280))+'px';panel.style.top=Math.max(0,Math.min(e.clientY-r.top,root.clientHeight-230))+'px'}
 function picker(key,label,e,place){panel.innerHTML='<div class="target-map-menu-title">'+label+'<button type="button" data-close aria-label="关闭装配选择">×</button></div><input data-search aria-label="搜索装配" placeholder="搜索装配…"><div class="target-map-choices"></div>';
  const draw=()=>{const q=panel.querySelector('[data-search]').value.toLowerCase(),list=panel.querySelector('.target-map-choices');list.innerHTML=fits.filter(f=>(f.name+' '+shipName(f.shipId)).toLowerCase().includes(q)).map(f=>'<button type="button" data-choice="'+esc(f.id)+'"><svg viewBox="0 0 32 32">'+image(f.shipId,0,0)+'</svg><span>'+esc(f.name)+'<small>'+esc(shipName(f.shipId))+'</small></span></button>').join('')||'<p>没有匹配的本地装配</p>';
   list.querySelectorAll('[data-choice]').forEach(b=>b.onclick=()=>{form.elements[key].value=b.dataset.choice;if(place){if(key==='targetFitId')plane.placeTarget(e);else plane.setSource(key==='supportFitId'?'support':'hostile',true,undefined,e)}panel.hidden=true;sync();form.dispatchEvent(new Event('input',{bubbles:true}));if(key==='targetFitId')onTargetChange()})};
  panel.querySelector('[data-close]').onclick=()=>panel.hidden=true;panel.querySelector('[data-search]').oninput=draw;draw();position(e);panel.querySelector('[data-search]').focus();
 }
 function menu(e){e.preventDefault();e.stopPropagation();const source=e.target.closest('.scenario-source'),isEnemy=!!e.target.closest('.enemy-entity'),key=isEnemy?'targetFitId':source?.classList.contains('support')?'supportFitId':source?'hostileFitId':null;
  panel.innerHTML='<div class="target-map-menu-title">'+(key?'情景对象':'放置到此处')+'</div>';
  const entries=key?[['更换装配',()=>picker(key,'选择本地装配',e,false)],['移除',()=>{form.elements[key].value='';panel.hidden=true;sync();form.dispatchEvent(new Event('input',{bubbles:true}));if(key==='targetFitId')onTargetChange()}]]:[['放置目标',()=>picker('targetFitId','目标装配',e,true)],['放置传电来源',()=>picker('supportFitId','传电来源装配',e,true)],['放置毁电来源',()=>picker('hostileFitId','毁电来源装配',e,true)]];
  for(const [name,action] of entries){const b=document.createElement('button');b.type='button';b.textContent=name;b.onclick=action;panel.append(b)}position(e);
 }
 svg.oncontextmenu=menu;svg.tabIndex=0;svg.setAttribute('aria-label','情景平面，右键或 Shift+F10 放置舰船');svg.addEventListener('keydown',e=>{if(e.key==='ContextMenu'||e.shiftKey&&e.key==='F10'){const r=e.target.getBoundingClientRect();menu({target:e.target,clientX:r.x+r.width/2,clientY:r.y+r.height/2,preventDefault:()=>e.preventDefault(),stopPropagation:()=>e.stopPropagation()})}});
 root.addEventListener('pointerdown',e=>{if(!panel.contains(e.target)&&e.button!==2)panel.hidden=true});panel.onkeydown=e=>{if(e.key==='Escape'){e.stopPropagation();panel.hidden=true}};
 sync();return {sync,health:()=>({shield,armor})};
}
