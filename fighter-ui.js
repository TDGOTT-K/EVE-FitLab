// Native fighter loadout presenter; server validates every change through the pinned engine.
export const fighterHull = ship => [547,659].includes(ship.group);
const glyphs={light:'M12 3 21 20 12 16 3 20Z',heavy:'M12 2 21 10 19 21 12 17 5 21 3 10Z',support:'M12 3 20 9 20 17 12 21 4 17 4 9ZM4 9 20 17M20 9 4 17'};
let catalogError='';
const types=await fetch('./api/fighters').then(r=>{if(!r.ok)throw Error('舰载机目录暂不可用');return r.json()}).then(d=>d.items.map(t=>({...t,kind:({light:'轻型',heavy:'重型',support:'支援'})[t.class],path:glyphs[t.class]}))).catch(e=>{catalogError=e.message;return []});
const icon=t=>`<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${t.path}"/></svg>`;
const type=id=>types.find(t=>t.id===Number(id));
let drag=null,selected=null,owner=null;
export function mountFighters(root,{ship,fit,report,mutate,browse,say,validate}){
 root.querySelector('#fighter-config')?.remove();if(owner!==fit){owner=fit;selected=null;}
 if(!fighterHull(ship))return;
 const bay=report?.native?.fighterBay;const count=bay?.maximumSquadrons||0;
 const state=fit.fighterLoadout||{tubes:Array(count).fill(null),reserve:[]};
 const change=async fn=>{if(host._busy)return;host._busy=true;const next=structuredClone(state);fn(next);say('正在校验舰载机配置…');try{const result=await validate({...fit,fighterLoadout:next});if(!host.isConnected)return;const blocking=result.issues.filter(e=>e.code==='STATIC_COVERAGE_INCOMPLETE'||e.code.startsWith('FIGHTER_')||e.code.startsWith('EVE_FIGHTER'));if(blocking.length){say('无法装载：'+blocking.map(e=>e.message).join('；'));return}mutate(()=>{fit.fighterLoadout=next},'舰载机已更新 · N 号引擎校验')}catch(e){say(e.message)}finally{host._busy=false}};
 const host=document.createElement('section');host.id='fighter-config';root.prepend(host);if(!bay||!types.length){host.innerHTML='<div class="slot-heading">铁骑舰载机</div><p class="profile-note">'+(catalogError||'等待引擎返回发射管与机库参数…')+'</p>';return;}
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
 host.querySelectorAll('.fighter-row').forEach(el=>{
  const list=el.dataset.fighterList,index=Number(el.dataset.fighterIndex),entry=state[list][index];
  el.querySelector('.fighter-pick').onclick=()=>select(list,index);
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
}
export function drawFighterBrowser(root,q){
 const host=document.querySelector('#fighter-config');if(!host)return;
 document.querySelector('#count').textContent=types.length+' 型';
 root.innerHTML=['light','heavy','support'].map(k=>{const list=types.filter(t=>t.class===k&&(!q||(t.name+' '+t.en+' '+t.id).toLowerCase().includes(q.toLowerCase())));return list.length?`<details open><summary>${({light:'轻型',heavy:'重型',support:'支援'})[k]}舰载机</summary>${list.map(t=>`<div class="item fighter-catalog" draggable="true" role="button" tabindex="0" data-fighter-type="${t.id}"><img src="https://images.evetech.net/types/${t.id}/icon?size=64" alt=""><span>${t.name}<small>完整中队 ${t.max} 架</small></span></div>`).join('')}</details>`:''}).join('');
 root.querySelectorAll('[data-fighter-type]').forEach(el=>{const t=type(el.dataset.fighterType);el.ondblclick=()=>document.querySelector('#fighter-config')._install(t);el.onkeydown=e=>{if(e.key==='Enter')document.querySelector('#fighter-config')._install(t)};el.ondragstart=e=>{drag={type:t.id};e.dataTransfer.setData('application/x-fitlab-fighter','1');e.dataTransfer.effectAllowed='copy'};el.ondragend=()=>drag=null});
}
document.addEventListener('dragover',e=>{if(drag?.list&&e.target.closest('.browser')){e.preventDefault();e.dataTransfer.dropEffect='move'}},true);
document.addEventListener('drop',e=>{if(drag?.list&&e.target.closest('.browser')){e.preventDefault();e.stopImmediatePropagation();document.querySelector('.browser').onfighterremove?.()}},true);
document.addEventListener('contextmenu',e=>{if(drag){e.preventDefault();e.stopImmediatePropagation();drag=null;document.querySelectorAll('.fighter-drop').forEach(x=>x.classList.remove('fighter-drop'))}},true);
