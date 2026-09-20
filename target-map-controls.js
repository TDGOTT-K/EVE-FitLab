import {getLocale} from './i18n.js';
import {withoutScenario} from './scenario-presets.js';
export function installTargetMapControls(root,{form,fits,ownFit,calculate,ownShipId,shipName,onTargetChange,health,plane}){
 const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 const svg=root.querySelector('svg'),enemy=root.querySelector('.enemy-entity'),own=root.querySelector('.own-entity');
 const image=(id,x,y)=>'<image x="'+x+'" y="'+y+'" width="32" height="32" href="https://images.evetech.net/types/'+Number(id)+'/icon?size=64"/>';
 own.tabIndex=0;own.setAttribute('role','button');own.setAttribute('aria-label','本舰，右键设置初始电量');
 own.innerHTML=image(ownShipId,284,154)+'<text x="300" y="205" text-anchor="middle">本舰</text>';
 let shield=health?.shield??(form.elements.targetLayer.value==='shield'),armor=health?.armor??(form.elements.targetLayer.value!=='structure');
 const panel=document.createElement('div');panel.className='target-map-menu';panel.hidden=true;root.append(panel);
 let energyRequest=0;
 const get=key=>fits.find(f=>f.id===form.elements[key].value);
 function sync(){const fit=get('targetFitId');plane.setTargetVisible(!!fit);enemy.innerHTML='<circle r="20" class="target-hit"/>'+(fit?'<title>'+esc(fit.name)+'</title>'+image(fit.shipId,-16,-16):'<rect x="-14" y="-14" width="28" height="28" fill="transparent" stroke="currentColor" stroke-dasharray="3 2"/><text y="5" text-anchor="middle">?</text>')+'<text y="-24" text-anchor="middle">'+esc(fit?shipName(fit.shipId):'目标')+'</text>'+[['shield','护盾',shield],['armor','装甲',armor],['structure','结构',true]].map(([k,n,full],i)=>'<g class="target-health" transform="translate('+(i*29-43)+' 28)" data-health="'+k+'" '+(k!=='structure'?'role="button" tabindex="0"':'')+' aria-label="'+n+' '+(full?'100':'0')+'%"><title>'+n+' '+(full?'100':'0')+'%'+(k!=='structure'?' · 点击切换':'')+'</title><rect width="26" height="17" class="target-health-track"/><rect width="'+(full?26:0)+'" height="17" class="target-health-fill"/><text x="13" y="12" text-anchor="middle">'+n.slice(-1)+'</text></g>').join('');
  form.elements.targetLayer.value=shield?'shield':armor?'armor':'structure';
  for(const [key,label] of [['support','传电'],['hostile','毁电/吸电']]){const f=get(key+'FitId');plane.setSource(key,!!f,f?'<title>'+esc(f.name)+'</title>'+image(f.shipId,-16,-16)+'<text y="-25" text-anchor="middle">'+esc(label+' · '+shipName(f.shipId))+'</text><text y="34" text-anchor="middle" data-distance></text>':'');}
  enemy.querySelectorAll('[data-health]').forEach(el=>{const toggle=e=>{e.preventDefault();e.stopPropagation();if(el.dataset.health==='shield')shield=!shield;else if(el.dataset.health==='armor')armor=!armor;sync();form.dispatchEvent(new Event('input',{bubbles:true}))};el.onpointerdown=e=>e.stopPropagation();el.onclick=toggle;el.onkeydown=e=>{if(e.key==='Enter'||e.key===' '){toggle(e)}}});
 }
 root.addEventListener('scenario-geometry',()=>{for(const key of ['support','hostile']){const p=plane.sourceValue(key);form.elements[key+'Distance'].value=Math.hypot(p.x,p.y)}});
 function position(e){panel.hidden=false;const r=root.getBoundingClientRect();panel.style.left=Math.max(0,Math.min(e.clientX-r.left,root.clientWidth-280))+'px';panel.style.top=Math.max(0,Math.min(e.clientY-r.top,root.clientHeight-230))+'px'}
 function picker(key,label,e,place){panel.innerHTML='<div class="target-map-menu-title">'+label+'<button type="button" data-close aria-label="关闭装配选择">×</button></div><input data-search aria-label="搜索装配" placeholder="搜索装配…"><div class="target-map-choices"></div>';
  const draw=()=>{const q=panel.querySelector('[data-search]').value.toLowerCase(),list=panel.querySelector('.target-map-choices');list.innerHTML=fits.filter(f=>(f.name+' '+shipName(f.shipId)).toLowerCase().includes(q)).map(f=>'<button type="button" data-choice="'+esc(f.id)+'"><svg viewBox="0 0 32 32">'+image(f.shipId,0,0)+'</svg><span>'+esc(f.name)+'<small>'+esc(shipName(f.shipId))+'</small></span></button>').join('')||'<p>没有匹配的本地装配</p>';
   list.querySelectorAll('[data-choice]').forEach(b=>b.onclick=()=>{if(form.elements[key].value!==b.dataset.choice){const field={targetFitId:'targetCapacitorGj',hostileFitId:'hostileCapacitorGj'}[key];if(field)form.elements[field].value='';}form.elements[key].value=b.dataset.choice;if(place){if(key==='targetFitId')plane.placeTarget(e);else plane.setSource(key==='supportFitId'?'support':'hostile',true,undefined,e)}panel.hidden=true;sync();form.dispatchEvent(new Event('input',{bubbles:true}));if(key==='targetFitId')onTargetChange()})};
  panel.querySelector('[data-close]').onclick=()=>panel.hidden=true;panel.querySelector('[data-search]').oninput=draw;draw();position(e);panel.querySelector('[data-search]').focus();
 }
 async function energyMenu(key,e){
  const token=++energyRequest,local=key==='own',field=local?'ownCapacitorFraction':key==='targetFitId'?'targetCapacitorGj':'hostileCapacitorGj';
  panel.innerHTML='<div class="target-map-menu-title">'+(local?'本舰初始电量':'对方固定电量')+'</div><p>读取电容容量…</p>';position(e);
  try{
   const fit=local?ownFit:get(key);if(!fit)throw Error('请先选择装配');
   const report=await calculate(withoutScenario(fit));if(token!==energyRequest||panel.hidden)return;
   const capacity=report.native?.capacitor?.recharge?.capacity;
   if(!Number.isFinite(capacity)||capacity<=0)throw Error('该装配的电容容量不可用');
   const control=form.elements[field],declared=control.value!=='';
   const value=declared?Number(control.value)*(local?100:1):0,max=local?100:capacity,unit=local?'%':'GJ';
   panel.innerHTML='<div class="target-map-menu-title">'+(local?'本舰初始电量':'对方固定电量')+'<button type="button" data-close aria-label="关闭电量设置">×</button></div><div class="scenario-energy-control"><output>'+(declared?value+' '+unit:'未声明')+'</output><input type="range" min="0" max="'+max+'" step="any" value="'+value+'" aria-label="'+(local?'本舰初始电量百分比':'对方固定电量GJ')+'"><div class="scenario-energy-buttons"><button type="button" data-empty>空电</button><button type="button" data-full>满电</button>'+(!local?'<button type="button" data-clear>未声明</button>':'')+'</div><small>'+(local?'只设置观察窗口起点。':'固定边界，仅计算本舰；不模拟对方电量变化。')+'</small></div>';
   const slider=panel.querySelector('input'),output=panel.querySelector('output');
   const set=value=>{control.value=local?value/100:value;slider.value=value;output.textContent=Number(value).toLocaleString(getLocale(),{maximumFractionDigits:2})+' '+unit;form.dispatchEvent(new Event('input',{bubbles:true}));};
   slider.oninput=()=>set(Number(slider.value));panel.querySelector('[data-empty]').onclick=()=>set(0);panel.querySelector('[data-full]').onclick=()=>set(max);
   panel.querySelector('[data-clear]')?.addEventListener('click',()=>{control.value='';output.textContent='未声明';form.dispatchEvent(new Event('input',{bubbles:true}))});
   panel.querySelector('[data-close]').onclick=()=>panel.hidden=true;position(e);slider.focus();
  }catch(error){if(token===energyRequest&&!panel.hidden){panel.innerHTML='<p>'+esc(error.message)+'</p>';position(e)}}
 }
 function menu(e){energyRequest++;e.preventDefault();e.stopPropagation();const source=e.target.closest('.scenario-source'),isOwn=!!e.target.closest('.own-entity'),isEnemy=!!e.target.closest('.enemy-entity'),key=isEnemy?'targetFitId':source?.classList.contains('support')?'supportFitId':source?'hostileFitId':null;
  panel.innerHTML='<div class="target-map-menu-title">'+(isOwn?'本舰':key?'情景对象':'放置到此处')+'</div>';
  const entries=isOwn?[]:key?[['更换装配',()=>picker(key,'选择本地装配',e,false)],['移除',()=>{form.elements[key].value='';panel.hidden=true;sync();form.dispatchEvent(new Event('input',{bubbles:true}));if(key==='targetFitId')onTargetChange()}]]:[['放置目标',()=>picker('targetFitId','目标装配',e,true)],['放置传电来源',()=>picker('supportFitId','传电来源装配',e,true)],['放置毁电/吸电来源',()=>picker('hostileFitId','毁电/吸电来源装配',e,true)]];
  if(!isOwn&&!key)entries.push(['本舰初始电量',()=>energyMenu('own',e)]);
  if(isOwn||isEnemy||key==='hostileFitId')entries.unshift([isOwn?'初始电量':'固定电量 · 吸电条件',()=>energyMenu(isOwn?'own':key,e)]);
  if(isEnemy)entries.splice(1,0,['停止运动',()=>{plane.stop();panel.hidden=true}],['横向飞行',()=>{plane.tangent();panel.hidden=true}]);
  for(const [name,action] of entries){const b=document.createElement('button');b.type='button';b.textContent=name;b.onclick=action;if(name==='横向飞行')b.disabled=!plane.canSetVelocity();panel.append(b)}position(e);
 }
 svg.oncontextmenu=menu;svg.tabIndex=0;svg.setAttribute('aria-label','情景平面，右键或 Shift+F10 放置舰船');svg.addEventListener('keydown',e=>{if(e.key==='ContextMenu'||e.shiftKey&&e.key==='F10'){const r=e.target.getBoundingClientRect();menu({target:e.target,clientX:r.x+r.width/2,clientY:r.y+r.height/2,preventDefault:()=>e.preventDefault(),stopPropagation:()=>e.stopPropagation()})}});
 root.addEventListener('pointerdown',e=>{if(!panel.contains(e.target)&&e.button!==2)panel.hidden=true});panel.onkeydown=e=>{if(e.key==='Escape'){e.stopPropagation();panel.hidden=true}};
 sync();return {sync,health:()=>({shield,armor})};
}
