// UI-only squadron snapshots. Replace mock definitions with engine capabilities.
export const fighterHull = ship => [547,659].includes(ship.group);
const types=[
 {id:'light',name:'轻型攻击中队',kind:'轻型',max:9,path:'M12 3 21 20 12 16 3 20Z'},
 {id:'heavy',name:'重型攻击中队',kind:'重型',max:6,path:'M12 2 21 10 19 21 12 17 5 21 3 10Z'},
 {id:'support',name:'支援中队',kind:'支援',max:3,path:'M12 3 20 9 20 17 12 21 4 17 4 9ZM4 9 20 17M20 9 4 17'}
];
const icon=t=>`<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${t.path}"/></svg>`;
const type=id=>types.find(t=>t.id===id);
let drag=null,selected=null;
export function mountFighters(root,{ship,fit,mutate,browse,say}){
 root.querySelector('#fighter-config')?.remove();
 if(!fighterHull(ship))return;
 const count=ship.group===659?5:3;
 const state=fit.fighterUiMock||{tubes:Array(count).fill(null),reserve:[]};
 const change=fn=>mutate(()=>{fit.fighterUiMock=structuredClone(state);fn(fit.fighterUiMock)},'舰载机配置已更新 · UI mock，未参与计算');
 const host=document.createElement('section');host.id='fighter-config';root.prepend(host);
 const row=(entry,index,reserve=false)=>{
  const t=entry&&type(entry.type),key=reserve?'reserve':'tubes';
  return `<div class="fighter-row ${entry?'filled':''}" data-fighter-index="${index}" data-fighter-list="${key}" ${entry?'draggable="true"':''}>
  <span class="fighter-tube">${reserve?'备':String(index+1).padStart(2,'0')}</span>
  <button class="fighter-pick" aria-label="${entry?t.name:'选择发射管 '+(index+1)+' 的舰载机'}">${t?icon(t):'<span class="fighter-empty">＋</span>'}<span>${t?t.name:'空发射管'}<small>${t?t.kind:'选择或拖入中队'}</small></span></button>
  ${entry?`<div class="fighter-number"><button data-delta="-1" aria-label="减少中队数量">−</button><b>${entry.quantity}<small> / ${t.max}</small></b><button data-delta="1" aria-label="增加中队数量">＋</button></div>${!reserve?`<button class="fighter-active ${entry.active?'on':''}" data-active aria-pressed="${entry.active}">${entry.active?'参战':'待命'}</button>`:''}`:''}</div>`;
 };
 host.innerHTML=`<div class="slot-heading"><button class="bay-filter" data-browse>铁骑舰载机</button><span class="fighter-mock" title="型号、数量及类型限制为交互占位，正式接入引擎后复核">UI MOCK</span><span>${state.tubes.filter(Boolean).length} / ${count}</span></div><div class="fighter-tubes">${Array.from({length:count},(_,i)=>row(state.tubes[i],i)).join('')}</div><details class="fighter-reserve" open><summary>备用机库 <span>${state.reserve.length} 中队</span></summary><div class="fighter-reserve-drop">${state.reserve.map((e,i)=>row(e,i,true)).join('')}<button class="fighter-reserve-add">＋ 添加备用中队</button></div></details>`;
 const select=(list,index)=>{selected={list,index};browse()};
 host.querySelector('[data-browse]').onclick=()=>select('tubes',state.tubes.findIndex(x=>!x));
 host.querySelector('.fighter-reserve-add').onclick=()=>select('reserve',state.reserve.length);
 const allowed=next=>next.tubes.filter(Boolean).every(e=>type(e.type))&&next.tubes.filter(e=>e?.type==='support').length<=1&&(ship.group===659||!next.tubes.some(e=>e?.type==='heavy'));
 const drop=(target,index)=>{
  if(!drag)return;
  const next=structuredClone(state);
  if(drag.list){const source=next[drag.list][drag.index];if(!source)return;const old=next[target][index]||null;next[target][index]=source;next[drag.list][drag.index]=old;if(drag.list==='reserve')next.reserve=next.reserve.filter(Boolean)}
  else next[target][index]={type:drag.type,quantity:type(drag.type).max,active:true};
  if(!allowed(next)){say('示例限制：支援最多 1 队，普通航母不装载重型中队');return}
  change(s=>Object.assign(s,next));
 };
 host.querySelectorAll('.fighter-row').forEach(el=>{
  const list=el.dataset.fighterList,index=Number(el.dataset.fighterIndex),entry=state[list][index];
  el.querySelector('.fighter-pick').onclick=()=>select(list,index);
  el.querySelectorAll('[data-delta]').forEach(b=>b.onclick=()=>change(s=>{s[list][index].quantity=Math.max(1,Math.min(type(entry.type).max,entry.quantity+Number(b.dataset.delta)))}));
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
 document.querySelector('#count').textContent='3 类 · MOCK';
 root.innerHTML=types.filter(t=>!q||t.name.includes(q)).map(t=>`<details open><summary>${t.kind}舰载机</summary><div class="item fighter-catalog" draggable="true" role="button" tabindex="0" data-fighter-type="${t.id}">${icon(t)}<span>${t.name}<small>示例中队 · ${t.max} 架</small></span></div></details>`).join('');
 root.querySelectorAll('[data-fighter-type]').forEach(el=>{const t=type(el.dataset.fighterType);el.ondblclick=()=>document.querySelector('#fighter-config')._install(t);el.onkeydown=e=>{if(e.key==='Enter')document.querySelector('#fighter-config')._install(t)};el.ondragstart=e=>{drag={type:t.id};e.dataTransfer.setData('application/x-fitlab-fighter','1');e.dataTransfer.effectAllowed='copy'};el.ondragend=()=>drag=null});
}
document.addEventListener('dragover',e=>{if(drag?.list&&e.target.closest('.browser')){e.preventDefault();e.dataTransfer.dropEffect='move'}},true);
document.addEventListener('drop',e=>{if(drag?.list&&e.target.closest('.browser')){e.preventDefault();e.stopImmediatePropagation();document.querySelector('.browser').onfighterremove?.()}},true);
document.addEventListener('contextmenu',e=>{if(drag){e.preventDefault();e.stopImmediatePropagation();drag=null;document.querySelectorAll('.fighter-drop').forEach(x=>x.classList.remove('fighter-drop'))}},true);
