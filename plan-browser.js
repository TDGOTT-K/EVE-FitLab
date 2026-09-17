import {escapeHtml as esc} from './scenario-display.js';

export function installPlanBrowser(host,{catalogs,onInstall,onDrag,onDragEnd,onUnload,isInstalled}){
 let kind='implants',slot=null;const expanded=new Set();
 host.innerHTML='<div class="plan-browser-title"><b>物品浏览器</b><small class="plan-browser-count"></small></div><div class="plan-browser-tabs"><button data-kind="implants" aria-pressed="true">脑插</button><button data-kind="boosters" aria-pressed="false">增效剂</button></div><input class="plan-item-search" aria-label="搜索脑插或增效剂" placeholder="搜索名称、分类、型号"><div class="plan-browser-filter"></div><div class="plan-item-tree"></div><small class="plan-browser-hint">点击或拖入安装 · 拖回此处卸下</small>';
 const $=s=>host.querySelector(s);
 const order=['火力','防御','电容与装配','机动','锁定与电子','扫描与探索','采集与工业','指挥与后勤','训练与其他'];
 const aliases={'回电':'电容回充','跑得快':'速度','血量':'容量','回盾':'护盾回充'};
 function render(){
  const tree=$('.plan-item-tree'),scroll=tree.scrollTop,q=$('.plan-item-search').value.trim().toLowerCase();
  host.querySelectorAll('[data-kind]').forEach(b=>b.setAttribute('aria-pressed',String(b.dataset.kind===kind)));
  $('.plan-browser-filter').innerHTML=slot?'<button aria-label="清除槽位筛选">槽位 '+slot+' ×</button>':'';
  $('.plan-browser-filter button')?.addEventListener('click',()=>{slot=null;render()});
  const needle=aliases[q]||q;
  const matches=t=>(t.name+' '+t.en+' '+(t.benefitPaths||[]).flat().join(' ')+' '+(t.benefitLabels||[]).join(' ')).toLowerCase().includes(needle);
  const items=catalogs[kind].filter(t=>(!slot||slot===t.slot)&&(!q||matches(t)));
  $('.plan-browser-count').textContent=items.length+' 件';
  const groups=new Map();for(const t of items)for(const [category,benefit] of t.benefitPaths||[['训练与其他','其他特殊效果']]){if(!groups.has(category))groups.set(category,new Map());const leaves=groups.get(category);if(!leaves.has(benefit))leaves.set(benefit,[]);leaves.get(benefit).push(t);}
  const row=(t,benefit)=>{const labels=t.benefitLabels||[],summary=benefit&&benefit!=='白板'?benefit:labels.join(' · ');return '<button class="plan-browser-item" draggable="true" data-id="'+t.id+'" title="'+esc(t.en+'\n'+labels.join(' · '))+'" aria-label="安装 '+esc(t.name)+'"><img loading="lazy" draggable="false" src="https://images.evetech.net/types/'+t.id+'/icon?size=64" alt=""><span>'+esc(t.name)+'<em class="plan-benefit">'+esc(summary)+'</em></span><small>'+ (isInstalled(kind,t.id)?'已装':'槽 '+t.slot)+'</small></button>';};
  tree.innerHTML=q?items.map(t=>row(t)).join(''):[...groups].sort(([a],[b])=>order.indexOf(a)-order.indexOf(b)).map(([group,leaves])=>{const key=kind+'/'+group,open=slot||expanded.has(key);return '<details data-key="'+esc(key)+'" '+(open?'open':'')+'><summary>'+esc(group)+' <small>'+new Set([...leaves.values()].flat().map(t=>t.id)).size+'</small></summary>'+(open?[...leaves].map(([benefit,rows])=>{const sub=key+'/'+benefit,show=slot||expanded.has(sub);return '<details data-key="'+esc(sub)+'" '+(show?'open':'')+'><summary>'+esc(benefit)+' <small>'+rows.length+'</small></summary>'+(show?rows.sort((a,b)=>a.name.localeCompare(b.name,'zh-CN')).map(t=>row(t,benefit)).join(''):'')+'</details>';}).join(''):'')+'</details>';}).join('');
  if(!items.length)tree.innerHTML='<p class="pilot-empty">没有匹配物品</p>';
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
