import {gameNameMarkup,matchesName,navigationMarkup,setMessage} from './i18n.js';
import {openAbyssalWorkbench} from './abyssal-workbench.js';
import {escapeHtml as esc} from './scenario-display.js';
export function installAbyssalLibrary({host,catalog,api,render,onLocate,onInfo,bindBase,onInstall,onPreview,onPreviewEnd,onDrag,onDragEnd}){
 const footer=host.querySelector('footer');setMessage(footer,'ui.dragToFitDoubleClickToFitQuicklyRight');
 const root=host.querySelector('#tree'),search=host.querySelector('#search');
 const eligible=t=>t&&['high','mid','low'].includes(t.kind)&&!!mutationOptions[t.id]?.length;
 let mutationOptions={},active=false,records=[],loaded=false,loading=false,error='',located=null;const open=new Set(),views={normal:{query:'',scroll:0},abyss:{query:'',scroll:0}};
 const heading=host.querySelector('.panel-title'),count=heading.querySelector('#count'),title=document.createElement('span'),switchButton=document.createElement('button');
 title.className='equipment-browser-name';title.textContent='装备浏览器';switchButton.className='equipment-browser-switch';switchButton.type='button';updateSwitch();heading.classList.add('equipment-browser-heading');heading.replaceChildren(title,switchButton,count);
 function updateSwitch(){
  const label=active?'返回装备':'进入深渊';
  const glyph=active?'<path d="M10 6 4 12l6 6M4 12h15"/>':'<path d="m12 2 7 5 2 7-7 8-9-5-2-7Z" opacity=".5"/><path d="m12 4-4 6 5 2-3 8 7-9-5-2 2-5Z" fill="currentColor" stroke="none"/>';
  switchButton.innerHTML='<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.4" aria-hidden="true">'+glyph+'</svg><span>'+label+'</span>';
  switchButton.setAttribute('aria-label',label);switchButton.title=label;switchButton.classList.toggle('enter-abyss',!active);
 }
 async function load(){if(loading)return;loading=true;error='';try{[records,mutationOptions]=await Promise.all([api('abyssal-instances'),api('mutation-options')]);loaded=true}catch(e){error=e.message}finally{loading=false;if(active)render()}}
 function switchTo(value){if(active===value)return;views[active?'abyss':'normal']={query:search.value,scroll:root.scrollTop};active=value;host.classList.toggle('abyssal-skin',active);setMessage(footer,active?'ui.doubleClickToFitDragToASlotRight':'ui.dragToFitDoubleClickToFitQuicklyRight');const view=views[active?'abyss':'normal'];search.value=view.query;title.textContent=active?'深渊装备库':'装备浏览器';updateSwitch();render();root.scrollTop=view.scroll;if(active)load();}
 switchButton.onclick=()=>switchTo(!active);
 const path=t=>[...t.path,t.meta||'未标注科技分类'];
 function reveal(t){let key='';for(const part of path(t)){key+='/'+part;open.add(key)}open.add('base/'+t.id);located=t.id;}
 function locate(t){onLocate();if(!active)switchTo(true);search.value='';reveal(t);render();if(!loaded)load();}
 function edit(t,record=null,copy=false){
  const current=record&&!copy?record:null;
  openAbyssalWorkbench({type:t,record,copy,api,options:mutationOptions[t.id]||[],onSave:async fields=>{const saved=await api('abyssal-instance',{...(current?{id:current.id,revision:current.revision}:{}),baseTypeId:t.id,...fields});records=records.filter(x=>x.id!==saved.id).concat(saved);loaded=true;reveal(t);search.value='';render();}});
 }
 function menu(event,t,record=null){event.preventDefault();event.stopPropagation();document.querySelector('.abyssal-menu')?.remove();const popup=document.createElement('div');popup.className='abyssal-menu scenario-quick-menu';popup.role='menu';popup.setAttribute('popover','auto');
  const actions=record?[['编辑实例',()=>edit(t,record)],['复制实例',()=>edit(t,record,true)],['原装备详细信息',()=>onInfo(t)],['删除实例',()=>remove(t,record)]]:[['创建深渊实例',()=>edit(t)],['原装备详细信息',()=>onInfo(t)]];
  if(record?.status==='generated')actions.unshift(['安装实例',()=>onInstall(record)]);
  for(const [label,fn] of actions){const b=document.createElement('button');b.role='menuitem';b.textContent=label;b.onclick=()=>{popup.hidePopover();popup.remove();fn()};popup.append(b)}popup.addEventListener('toggle',e=>{if(e.newState==='closed')popup.remove()});document.body.append(popup);popup.showPopover();const r=event.currentTarget.getBoundingClientRect(),x=event.type==='keydown'?r.left:event.clientX,y=event.type==='keydown'?r.bottom:event.clientY;popup.style.left=Math.max(8,Math.min(x,innerWidth-popup.offsetWidth-8))+'px';popup.style.top=Math.max(8,Math.min(y,innerHeight-popup.offsetHeight-8))+'px';popup.querySelector('button').focus();
 }
 function remove(t,record){const d=document.createElement('dialog');d.className='character-action-dialog';d.innerHTML='<div class="flow-head"><b>删除深渊实例</b></div><div class="flow-body"><p translate="no">'+esc(record.name)+'</p><p role="status"></p><button data-cancel>取消</button><button data-delete>删除</button></div>';document.body.append(d);d.querySelector('[data-cancel]').onclick=()=>d.close();d.onclose=()=>d.remove();d.querySelector('[data-delete]').onclick=async()=>{const b=d.querySelector('[data-delete]');b.disabled=true;try{await api('abyssal-instance/delete',{id:record.id,revision:record.revision});records=records.filter(x=>x.id!==record.id);render();d.close()}catch(e){d.querySelector('[role=status]').textContent=e.message;b.disabled=false}};d.showModal();}
 function draw(items,q){
  if(!active)return false;if(!loaded&&!loading&&!error)load();const scroll=root.scrollTop;
  if(!loaded){root.innerHTML='<div class="hint">'+esc(error||'正在读取深渊库…')+'</div>';if(error){const b=document.createElement('button');b.textContent='重试';b.onclick=load;root.append(b)}return true;}
  const bases=items.filter(eligible),tree={};for(const t of bases){let n=tree;for(const part of path(t))n=n[part]??={};(n.items??=[]).push(t)}
  const branch=(node,prefix='')=>Object.entries(node).map(([key,value])=>{if(key==='items')return value.map(t=>{const entries=records.filter(r=>r.baseTypeId===t.id&&(!q||(r.name+' '+r.notes).toLowerCase().includes(q)||(matchesName(t,q)||t.path.join(' ').toLowerCase().includes(q)))),expanded=q||open.has('base/'+t.id);return '<details class="abyssal-base" data-open-key="base/'+t.id+'" '+(expanded?'open':'')+'><summary data-base="'+t.id+'"><img src="https://images.evetech.net/types/'+t.id+'/icon?size=64" alt=""><span>'+gameNameMarkup(t)+'</span><small>'+entries.length+'</small></summary>'+entries.map(r=>'<button class="abyssal-instance" data-instance="'+esc(r.id)+'" '+(r.notes?'data-user-title ':'')+'title="'+esc(r.notes||'双击安装 · 右键编辑')+'"><span translate="no">◇ '+esc(r.name)+'</span><small>'+ (r.status==='generated'?(r.origin==='manual'?'手动编辑 · 可安装':'随机变异 · 可安装'):r.uiMock?'MOCK · 不可安装':'待生成')+'</small></button>').join('')+'<button class="abyssal-create" data-create="'+t.id+'">＋ 创建深渊实例</button>'+'</details>';}).join('');const p=prefix+'/'+key,expanded=q||open.has(p);return '<details data-open-key="'+esc(p)+'" '+(expanded?'open':'')+'><summary>'+navigationMarkup(p,key)+'</summary>'+(expanded?branch(value,p):'')+'</details>';}).join('');
  root.innerHTML='<div class="abyssal-library-note">实例库 · 独立保存每次变异</div>'+branch(tree)+(bases.length?'':'<div class="hint">当前筛选下没有原装备。</div>');delete count.dataset.i18n;delete count.dataset.i18nParams;host.querySelector('#count').textContent=records.filter(r=>bases.some(t=>t.id===r.baseTypeId)).length+' 个实例';root.scrollTop=scroll;
  root.querySelectorAll('summary').forEach(s=>{s.onclick=e=>{if(s.dataset.base){const key=s.parentElement.dataset.openKey;if(s.parentElement.open)open.delete(key);else open.add(key);return;}e.preventDefault();const key=s.parentElement.dataset.openKey;if(q){s.parentElement.open=!s.parentElement.open;return;}if(open.has(key))open.delete(key);else open.add(key);render()};if(s.dataset.base){const t=catalog.find(t=>t.id===Number(s.dataset.base));bindBase(s,t,[['创建深渊实例',()=>edit(t)]]);}});
  root.querySelectorAll('[data-create]').forEach(b=>b.onclick=()=>edit(catalog.find(t=>t.id===Number(b.dataset.create))));root.querySelectorAll('[data-instance]').forEach(b=>{const record=records.find(r=>r.id===b.dataset.instance),t=catalog.find(t=>t.id===record.baseTypeId);b.onclick=()=>{};b.ondblclick=()=>record.status==='generated'?onInstall(record):edit(t,record);if(record.status==='generated'){b.draggable=true;b.onpointerenter=()=>onPreview(record);b.onpointerleave=onPreviewEnd;b.onfocus=()=>onPreview(record);b.onblur=onPreviewEnd;b.ondragstart=e=>onDrag(e,record);b.ondragend=onDragEnd;}b.oncontextmenu=e=>menu(e,t,record);b.onkeydown=e=>{if(e.key==='Enter'){e.preventDefault();record.status==='generated'?onInstall(record):edit(t,record);}if(e.key==='ContextMenu'||e.shiftKey&&e.key==='F10')menu(e,t,record)}});
  if(located){const target=root.querySelector('[data-base="'+located+'"]');if(target){target.classList.add('located');target.scrollIntoView({block:'nearest'});located=null;}}
  return true;
 }
 load();
 return {get active(){return active},eligible,locate,draw,matches(t,q){return active&&records.some(r=>r.baseTypeId===t.id&&(r.name+' '+r.notes).toLowerCase().includes(q))}};
}
