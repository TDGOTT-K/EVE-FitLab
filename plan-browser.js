import {escapeHtml as esc} from './scenario-display.js';

export function installPlanBrowser(host,{catalogs,onInstall,onDrag,onDragEnd,onUnload,isInstalled}){
 let kind='implants',slot=null;const expanded=new Set();
 host.innerHTML='<div class="plan-browser-title"><b>物品浏览器</b><small class="plan-browser-count"></small></div><div class="plan-browser-tabs"><button data-kind="implants" aria-pressed="true">脑插</button><button data-kind="boosters" aria-pressed="false">增效剂</button></div><input class="plan-item-search" aria-label="搜索脑插或增效剂" placeholder="搜索名称、分类、型号"><div class="plan-browser-filter"></div><div class="plan-item-tree"></div><small class="plan-browser-hint">点击或拖入安装 · 拖回此处卸下</small>';
 const $=s=>host.querySelector(s);
 function render(){
  const tree=$('.plan-item-tree'),scroll=tree.scrollTop,q=$('.plan-item-search').value.trim().toLowerCase();
  host.querySelectorAll('[data-kind]').forEach(b=>b.setAttribute('aria-pressed',String(b.dataset.kind===kind)));
  $('.plan-browser-filter').innerHTML=slot?'<button aria-label="清除槽位筛选">槽位 '+slot+' ×</button>':'';
  $('.plan-browser-filter button')?.addEventListener('click',()=>{slot=null;render()});
  const items=catalogs[kind].filter(t=>(!slot||slot===t.slot)&&(!q||(t.name+' '+t.en+' '+t.group).toLowerCase().includes(q)));
  $('.plan-browser-count').textContent=items.length+' 件';
  const groups=new Map();for(const t of items){const category=kind==='implants'?t.group:(t.slot<=3?'战斗增效剂':'其他增效剂');if(!groups.has(category))groups.set(category,new Map());const slots=groups.get(category);if(!slots.has(t.slot))slots.set(t.slot,[]);slots.get(t.slot).push(t);}
  const row=t=>'<button class="plan-browser-item" draggable="true" data-id="'+t.id+'" title="'+esc(t.en)+'" aria-label="安装 '+esc(t.name)+'"><img loading="lazy" draggable="false" src="https://images.evetech.net/types/'+t.id+'/icon?size=64" alt=""><span>'+esc(t.name)+'</span><small>'+ (isInstalled(kind,t.id)?'已装':String(t.slot).padStart(2,'0'))+'</small></button>';
  tree.innerHTML=[...groups].map(([group,slots])=>{const key=kind+'/'+group,open=q||slot||expanded.has(key);return '<details data-key="'+esc(key)+'" '+(open?'open':'')+'><summary>'+esc(group)+' <small>'+[...slots.values()].reduce((n,a)=>n+a.length,0)+'</small></summary>'+(open?[...slots].sort(([a],[b])=>a-b).map(([n,rows])=>{const sub=key+'/'+n,show=q||slot||expanded.has(sub);return '<details data-key="'+esc(sub)+'" '+(show?'open':'')+'><summary>槽位 '+n+' <small>'+rows.length+'</small></summary>'+(show?rows.sort((a,b)=>a.name.localeCompare(b.name,'zh-CN')).map(row).join(''):'')+'</details>';}).join(''):'')+'</details>';}).join('')||'<p class="pilot-empty">没有匹配物品</p>';
  tree.scrollTop=scroll;
  tree.querySelectorAll('summary').forEach(s=>s.onclick=e=>{e.preventDefault();const d=s.parentElement,key=d.dataset.key;if(q||slot){d.open=!d.open;return;}if(expanded.has(key))expanded.delete(key);else expanded.add(key);render();});
  tree.querySelectorAll('[data-id]').forEach(b=>{const t=catalogs[kind].find(t=>t.id===Number(b.dataset.id));b.onclick=()=>onInstall(kind,t);b.ondragstart=e=>{onDrag({kind,id:t.id,source:'browser'});e.dataTransfer.setData('text/plain',String(t.id));e.dataTransfer.effectAllowed='copy';};b.ondragend=onDragEnd;});
 }
 host.querySelectorAll('[data-kind]').forEach(b=>b.onclick=()=>{kind=b.dataset.kind;slot=null;$('.plan-item-search').value='';render()});
 $('.plan-item-search').oninput=render;
 host.ondragover=e=>{if(onUnload(null)){e.preventDefault();e.dataTransfer.dropEffect='move';host.classList.add('drop-unload')}};
 host.ondragleave=e=>{if(!host.contains(e.relatedTarget))host.classList.remove('drop-unload')};
 host.ondrop=e=>{host.classList.remove('drop-unload');if(onUnload(e))e.preventDefault()};
 render();
 return {refresh:render,select(next,n){kind=next;slot=n||null;$('.plan-item-search').value='';render();$('.plan-item-search').focus();}};
}
