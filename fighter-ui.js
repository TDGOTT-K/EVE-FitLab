// Native fighter loadout presenter; server validates every change through the pinned engine.
export const fighterHull = ship => [547,659].includes(ship.group);
let catalogError='';
const types=await fetch('./api/fighters').then(r=>{if(!r.ok)throw Error('舰载机目录暂不可用');return r.json()}).then(d=>d.items.map(t=>({...t,kind:({light:'轻型',heavy:'重型',support:'支援'})[t.class]}))).catch(e=>{catalogError=e.message;return []});
const icon=t=>`<img class="fighter-type-icon" src="https://images.evetech.net/types/${t.id}/icon?size=64" alt="" width="32" height="32" draggable="false" loading="lazy">`;
const type=id=>types.find(t=>t.id===Number(id));
let drag=null,selected=null,owner=null;
function menu(event,origin,item,entries,header=null){
 event.preventDefault();event.stopPropagation();
 origin=origin.querySelector('.fighter-pick')||origin;
 document.dispatchEvent(new CustomEvent('fitlab-loadout-menu',{detail:{event,origin,item,entries,header,showDetails:false}}));
}
function bindMenu(element,open){
 element.oncontextmenu=open;
 element.addEventListener('keydown',e=>{if(e.key==='ContextMenu'||e.shiftKey&&e.key==='F10')open(e)});
}
export function mountFighters(root,{ship,fit,report,mutate,browse,say,validate,onInfo}){
 root.querySelector('#fighter-config')?.remove();if(owner!==fit){owner=fit;selected=null;}
 if(!fighterHull(ship))return;
 const bay=report?.native?.fighterBay;const count=bay?.maximumSquadrons||0;
 const state=fit.fighterLoadout||{tubes:Array(count).fill(null),reserve:[]};
 const change=async fn=>{if(host._busy)return;host._busy=true;const next=structuredClone(state);fn(next);say('正在校验舰载机配置…');try{const result=await validate({...fit,fighterLoadout:next});if(!host.isConnected)return;const blocking=result.issues.filter(e=>e.code==='STATIC_COVERAGE_INCOMPLETE'||e.code.startsWith('FIGHTER_')||e.code.startsWith('EVE_FIGHTER'));if(blocking.length){say('无法装载：'+blocking.map(e=>e.message).join('；'));return}mutate(()=>{fit.fighterLoadout=next},'舰载机已更新 · N 号引擎校验')}catch(e){say(e.message)}finally{host._busy=false}};
 const host=document.createElement('section');host.id='fighter-config';root.prepend(host);host._catalogMenu=(e,el,t)=>menu(e,el,t,[['装入发射管',()=>{},{disabled:true,title:'等待可用的装配计算结果'}],['加入备用机库',()=>{},{disabled:true,title:'等待可用的装配计算结果'}],['详细信息',()=>onInfo(t)]]);if(!bay||!types.length){host.innerHTML='<div class="slot-heading">铁骑舰载机</div><p class="profile-note">'+(catalogError||'等待引擎返回发射管与机库参数…')+'</p>';return;}
 const row=(entry,index,reserve=false)=>{
  const t=entry&&type(entry.typeId),key=reserve?'reserve':'tubes';
  return `<div class="fighter-row ${entry?'filled':''}" data-fighter-index="${index}" data-fighter-list="${key}" ${entry?'draggable="true"':''}>
  <span class="fighter-tube">${reserve?'备':String(index+1).padStart(2,'0')}</span>
  <button class="fighter-pick" aria-label="${entry?t.name:'选择发射管 '+(index+1)+' 的舰载机'}">${t?icon(t):'<span class="fighter-empty">＋</span>'}<span>${t?t.name:'空发射管'}<small>${t?t.kind:'选择或拖入中队'}</small></span></button>
  ${entry?`<div class="fighter-number"><button data-delta="-1" aria-label="减少中队数量">−</button><b>${entry.quantity}<small> / ${t.max}</small></b><button data-delta="1" aria-label="增加中队数量">＋</button></div>${!reserve?`<button class="fighter-active ${entry.active?'on':''}" data-active aria-pressed="${entry.active}">${entry.active?'参战':'待命'}</button>`:''}`:''}</div>`;
 };
 host.innerHTML=`<div class="slot-heading"><button class="bay-filter" data-browse>铁骑舰载机</button><span class="fighter-mock">N 引擎</span><span>${state.tubes.filter(Boolean).length} / ${count}</span></div><div class="fighter-quotas">${Object.entries(bay.classLimits).map(([k,n])=>({light:"轻型",support:"支援",heavy:"重型"})[k]+" "+n).join(" · ")}<span>机库 ${report.native.resources.find(r=>r.id==="fighterBay")?.used.toLocaleString()} / ${bay.capacityCubicMeters.toLocaleString()} m³</span></div><div class="fighter-tubes">${Array.from({length:count},(_,i)=>row(state.tubes[i],i)).join('')}</div><details class="fighter-reserve" open><summary>备用机库 <span>${state.reserve.length} 中队</span></summary><div class="fighter-reserve-drop">${state.reserve.map((e,i)=>row(e,i,true)).join('')}<button class="fighter-reserve-add">＋ 添加备用中队</button></div></details>`;
 const select=(list,index)=>{selected={list,index};browse()};
 host.querySelector('[data-browse]').onclick=()=>select('tubes',state.tubes.findIndex(x=>!x));
 host.querySelector('.fighter-reserve-add').onclick=()=>select('reserve',state.reserve.length);

 const drop=(target,index)=>{
  if(!drag)return;
  const next=structuredClone(state);
  if(drag.list){const source=next[drag.list][drag.index];if(!source)return;const old=next[target][index]||null;next[target][index]=source;next[drag.list][drag.index]=old;if(drag.list==='reserve')next.reserve=next.reserve.filter(Boolean)}
  else next[target][index]={id:'squadron-'+crypto.randomUUID(),typeId:drag.type,quantity:type(drag.type).max,active:true};
  change(s=>Object.assign(s,next));
 };
 function weaponHeader(t,entry,list,index){
  const ident=entry.id||'fighter-'+list+'-'+index,projection=report?.native?.fighters?.[ident];
  const abilities=projection?.abilityMetadata?.abilities?.filter(a=>[2233,2182,2401].includes(a.duration?.source?.attributeId))||[];
  if(!abilities.length)return null;
  const header=document.createElement('div');header.className='menu-title fighter-weapon-header';
  const caption=document.createElement('div');caption.className='fighter-weapon-caption';
  const name=document.createElement('span');name.textContent=t.name;const label=document.createElement('small');label.textContent='计入 DPS';caption.append(name,label);header.append(caption);
  const bar=document.createElement('div');bar.className='fighter-weapon-bar';header.append(bar);
  for(const a of abilities){
   const primary=a.duration.source.attributeId===2233,enabled=primary&&!(entry.excludedAbilities||[]).includes(a.abilityId);
   const button=document.createElement('button');button.type='button';button.role='menuitemcheckbox';button.disabled=!primary||host._busy;button.setAttribute('aria-checked',String(enabled));button.setAttribute('aria-label',(a.displayName.zh||a.displayName.en)+'计入DPS');
   button.title=primary?(list==='reserve'||!entry.active?'当前中队未参战；此选择在参战后生效':'点击切换是否计入此中队的主武器输出'):'当前引擎副本尚未提供此武器的静态 DPS，暂不能计入';
   const glyph=document.createElement('span');glyph.className='fighter-weapon-symbol';glyph.textContent=primary?'◎':a.duration.source.attributeId===2401?'✹':'↗';
   const text=document.createElement('span');text.className='fighter-weapon-name';text.textContent=a.displayName.zh||a.displayName.en;
   const stateLabel=document.createElement('small');stateLabel.textContent=!primary?'待接入':enabled?'已计入':'不计入';if(primary&&(list==='reserve'||!entry.active))stateLabel.textContent=enabled?'已选 · 待命':'不计入';
   button.append(glyph,text,stateLabel);
   button.onclick=()=>{document.querySelector('#menu').hidden=true;change(s=>{const row=s[list][index],excluded=new Set(row.excludedAbilities||[]);if(excluded.has(a.abilityId))excluded.delete(a.abilityId);else excluded.add(a.abilityId);row.excludedAbilities=[...excluded]})};bar.append(button);
  }
  return header;
 }
 host.querySelectorAll('.fighter-row').forEach(el=>{
  const list=el.dataset.fighterList,index=Number(el.dataset.fighterIndex),entry=state[list][index];
  el.querySelector('.fighter-pick').onclick=()=>select(list,index);
  bindMenu(el,e=>{
   if(!entry){menu(e,el,{name:'发射管 '+(index+1)},[['选择舰载机',()=>select(list,index)]]);return;}
   const t=type(entry.typeId),empty=Array.from({length:count},(_,i)=>i).find(i=>!state.tubes[i]);
   const actions=list==='tubes'?[
    [entry.active?'设为待命':'设为参战',()=>change(s=>s.tubes[index].active=!entry.active),{disabled:host._busy}],
    ['移入备用机库',()=>change(s=>{s.reserve.push({...s.tubes[index],active:false});s.tubes[index]=null}),{disabled:host._busy}],
   ]:[['装入空发射管',()=>change(s=>{s.tubes[empty]={...s.reserve[index],active:true};s.reserve.splice(index,1)}),{disabled:host._busy||empty===undefined,title:empty===undefined?'没有空发射管':''}]];
   menu(e,el,t,[...actions,['在浏览器中定位',()=>{select(list,index);document.querySelector('#search').value=t.name;document.querySelector('#search').dispatchEvent(new Event('input',{bubbles:true}))}],['卸下',()=>change(s=>{if(list==='reserve')s.reserve.splice(index,1);else s.tubes[index]=null}),{disabled:host._busy}],['详细信息',()=>onInfo(t)]],weaponHeader(t,entry,list,index));
  });
  el.querySelectorAll('[data-delta]').forEach(b=>b.onclick=()=>change(s=>{s[list][index].quantity=Math.max(1,Math.min(type(entry.typeId).max,entry.quantity+Number(b.dataset.delta)))}));
  const active=el.querySelector('[data-active]');if(active)active.onclick=()=>change(s=>s[list][index].active=!entry.active);
  el.ondragstart=e=>{drag={list,index};e.dataTransfer.setData('application/x-fitlab-fighter','1');e.dataTransfer.effectAllowed='move'};
  el.ondragend=()=>{drag=null;document.querySelectorAll('.fighter-drop').forEach(x=>x.classList.remove('fighter-drop'))};
  el.ondragover=e=>{if(!drag)return;e.preventDefault();e.stopPropagation();el.classList.add('fighter-drop')};
  el.ondragleave=()=>el.classList.remove('fighter-drop');
  el.ondrop=e=>{if(!drag)return;e.preventDefault();e.stopPropagation();drop(list,index);drag=null};
 });
 const reserve=host.querySelector('.fighter-reserve-drop');
 reserve.ondragover=e=>{if(!drag)return;e.preventDefault();reserve.classList.add('fighter-drop')};reserve.ondragleave=()=>reserve.classList.remove('fighter-drop');
 reserve.ondrop=e=>{if(!drag)return;e.preventDefault();e.stopPropagation();drop('reserve',state.reserve.length);drag=null};
 // Capture only fighter drags; equipment browser retains its ordinary handlers.
 const browser=document.querySelector('.browser');
 browser.onfighterremove=()=>{if(drag?.list)change(s=>{if(drag.list==='reserve')s.reserve.splice(drag.index,1);else s.tubes[drag.index]=null});drag=null};
 host._install=t=>{drag={type:t.id};let destination=selected||{list:'tubes',index:state.tubes.findIndex(x=>!x)};if(destination.index<0){say('发射管已满，请选择替换位置或备用机库');drag=null;return}drop(destination.list,destination.index);drag=null};
 host._catalogMenu=(e,el,t)=>{
  const index=selected?.list==='tubes'?selected.index:Array.from({length:count},(_,i)=>i).find(i=>!state.tubes[i]);
  const available=Number.isInteger(index)&&index>=0&&index<count;
  const install=(list,i)=>{drag={type:t.id};drop(list,i);drag=null};
  menu(e,el,t,[[available?(state.tubes[index]?'替换第 ':'装入第 ')+(index+1)+' 发射管':'装入发射管',()=>install('tubes',index),{disabled:host._busy||!available,title:available?'':'没有空发射管，请先选择替换位置'}],['加入备用机库',()=>install('reserve',state.reserve.length),{disabled:host._busy}],['详细信息',()=>onInfo(t)]]);
 };
}
export function drawFighterBrowser(root,q){
 const host=document.querySelector('#fighter-config');if(!host)return;
 document.querySelector('#count').textContent=types.length+' 型';
 root.innerHTML=['light','heavy','support'].map(k=>{const list=types.filter(t=>t.class===k&&(!q||(t.name+' '+t.en+' '+t.id).toLowerCase().includes(q.toLowerCase())));return list.length?`<details open><summary>${({light:'轻型',heavy:'重型',support:'支援'})[k]}舰载机</summary>${list.map(t=>`<div class="item fighter-catalog" draggable="true" role="button" tabindex="0" data-fighter-type="${t.id}"><img src="https://images.evetech.net/types/${t.id}/icon?size=64" alt=""><span>${t.name}<small>完整中队 ${t.max} 架</small></span></div>`).join('')}</details>`:''}).join('');
 root.querySelectorAll('[data-fighter-type]').forEach(el=>{const t=type(el.dataset.fighterType);el.ondblclick=()=>document.querySelector('#fighter-config')._install?.(t);el.onkeydown=e=>{if(e.key==='Enter')document.querySelector('#fighter-config')._install?.(t)};bindMenu(el,e=>document.querySelector('#fighter-config')._catalogMenu?.(e,el,t));el.ondragstart=e=>{drag={type:t.id};e.dataTransfer.setData('application/x-fitlab-fighter','1');e.dataTransfer.effectAllowed='copy'};el.ondragend=()=>drag=null});
}
document.addEventListener('dragover',e=>{if(drag?.list&&e.target.closest('.browser')){e.preventDefault();e.dataTransfer.dropEffect='move'}},true);
document.addEventListener('drop',e=>{if(drag?.list&&e.target.closest('.browser')){e.preventDefault();e.stopImmediatePropagation();document.querySelector('.browser').onfighterremove?.()}},true);
document.addEventListener('contextmenu',e=>{if(drag){e.preventDefault();e.stopImmediatePropagation();drag=null;document.querySelectorAll('.fighter-drop').forEach(x=>x.classList.remove('fighter-drop'))}},true);
