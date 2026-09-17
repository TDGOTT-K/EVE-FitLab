import {installPlanBrowser} from './plan-browser.js';
import {implantCatalog} from './implant-catalog.js';
import {boosterCatalog} from './booster-catalog.js';
import {escapeHtml as esc} from './scenario-display.js';
const catalogs={implants:implantCatalog.filter(t=>t.slot>=1&&t.slot<=10),boosters:boosterCatalog};
const find=(kind,id)=>catalogs[kind].find(t=>t.id===id);
const icon=t=>t?'<img loading="lazy" draggable="false" src="https://images.evetech.net/types/'+t.id+'/icon?size=64" alt="">':'<span class="plan-plus">＋</span>';

export function installLoadoutManager(host,{api}){
 let plans=[],draft=null,dirty=false,busy=false,drag=null,returnToFit=false;
 host.innerHTML='<div class="plan-workspace"><aside class="plan-library"><div class="plan-library-head"><button data-toggle-library aria-expanded="true" aria-controls="plan-library-body"><span class="plan-library-chevron">▾</span> 方案库</button><span class="plan-library-current"></span><button data-group-current disabled>分组</button><button data-new>＋ 新建</button></div><div id="plan-library-body"><input class="plan-search" aria-label="搜索方案" placeholder="搜索方案或分组"><div class="plan-list" tabindex="0" aria-label="脑插与增效剂方案列表"></div><small>Ctrl+C / V 复制粘贴</small></div></aside><aside class="plan-browser"></aside><section class="plan-editor"><div class="plan-toolbar"></div><p class="plan-message" role="status"></p><div class="plan-content"></div></section></div>';
 const $=s=>host.querySelector(s),message=text=>$('.plan-message').textContent=text;
 let libraryCollapsed=false;try{libraryCollapsed=localStorage.getItem('fitlab-plan-library-collapsed')==='true'}catch{}
 function updateLibraryFold(){
  $('#plan-library-body').hidden=libraryCollapsed;$('.plan-workspace').classList.toggle('library-collapsed',libraryCollapsed);
  $('[data-toggle-library]').setAttribute('aria-expanded',String(!libraryCollapsed));$('.plan-library-chevron').textContent=libraryCollapsed?'▸':'▾';
 }
 $('[data-toggle-library]').onclick=()=>{libraryCollapsed=!libraryCollapsed;updateLibraryFold();try{localStorage.setItem('fitlab-plan-library-collapsed',String(libraryCollapsed))}catch{}};
 updateLibraryFold();

 function installItem(kind,t){
  if(busy)return;
  if(!draft){draft={name:'新方案',folder:'',implants:[],boosters:[]};dirty=true;draw();}
  if(draft[kind].some(x=>x.typeId===t.id))return;
  draft[kind]=draft[kind].filter(x=>x.slot!==t.slot).concat({typeId:t.id,slot:t.slot,...(kind==='boosters'?{enabledSideEffects:[]}:{})});changed();drawSlots();
 }
 function unload(event){if(!drag||drag.source!=='installed'||!draft||busy)return false;if(event){draft[drag.kind]=draft[drag.kind].filter(x=>x.typeId!==drag.id);drag=null;changed();drawSlots();}return true;}
 const browser=installPlanBrowser($('.plan-browser'),{catalogs,onInstall:installItem,onDrag:value=>{drag=value;host.classList.add('plan-dragging');host.querySelectorAll('.plan-slot').forEach(el=>{const t=find(value.kind,value.id);el.classList.toggle('drop-compatible',el.dataset.kind===value.kind&&Number(el.dataset.slot)===t?.slot);});},onDragEnd:clearDrag,onUnload:unload,isInstalled:(kind,id)=>draft?.[kind].some(x=>x.typeId===id)});
 function clearDrag(){drag=null;host.classList.remove('plan-dragging');host.querySelectorAll('.drop-compatible,.drop-unload').forEach(el=>el.classList.remove('drop-compatible','drop-unload'));}
 $('.plan-content').ondragover=e=>{if(drag?.source==='browser'&&!e.target.closest('.plan-slot')){e.preventDefault();e.dataTransfer.dropEffect='copy'}};
 $('.plan-content').ondrop=e=>{if(drag?.source==='browser'&&!e.target.closest('.plan-slot')){e.preventDefault();const t=find(drag.kind,drag.id);if(t)installItem(drag.kind,t);clearDrag();}};

 function confirm(text){return new Promise(resolve=>{const dialog=document.createElement('dialog');dialog.className='character-action-dialog';dialog.innerHTML='<div class="flow-head"><b>确认操作</b></div><div class="flow-body"><p>'+esc(text)+'</p><button data-no>取消</button> <button data-yes>确定</button></div>';document.body.append(dialog);let yes=false;dialog.querySelector('[data-yes]').onclick=()=>{yes=true;dialog.close()};dialog.querySelector('[data-no]').onclick=()=>dialog.close();dialog.onclose=()=>{dialog.remove();resolve(yes)};dialog.showModal();});}
 const cache=()=>{try{if(dirty&&draft)sessionStorage.setItem('fitlab-loadout-draft',JSON.stringify(draft));else sessionStorage.removeItem('fitlab-loadout-draft')}catch{}};
 try{const saved=JSON.parse(sessionStorage.getItem('fitlab-loadout-draft')||'null');if(saved?.implants&&saved?.boosters){draft=saved;dirty=true;}}catch{}
 const guard=async()=>!dirty||await confirm('当前方案有未保存的修改，放弃这些修改？');
 function changed(){dirty=true;$('.plan-library-current').textContent=draft?.name||'';cache();$('.plan-dirty').textContent='未保存';}
 function drawList(){
  $('.plan-library-current').textContent=draft?.name||'';$('[data-group-current]').disabled=!draft;
  const q=$('.plan-search').value.trim().toLowerCase(),groups=[...new Set(plans.map(p=>p.folder||''))].sort();
  $('.plan-list').innerHTML=groups.map(folder=>{const rows=plans.filter(p=>(p.folder||'')===folder&&(p.name+' '+folder).toLowerCase().includes(q));return rows.length?'<div class="plan-group"><small>'+esc(folder||'未分组')+'</small>'+rows.map(p=>'<button class="plan-entry" data-plan="'+p.id+'" aria-pressed="'+(draft?.id===p.id)+'"><span>'+esc(p.name)+'</span><small>脑插 '+p.implants.length+' · 增效剂 '+p.boosters.length+'</small></button>').join('')+'</div>':'';}).join('')||'<p class="pilot-empty">'+(plans.length?'没有匹配方案':'还没有方案，点击“新建”开始')+'</p>';
  $('.plan-list').querySelectorAll('[data-plan]').forEach(b=>{
   b.onclick=async()=>{if(busy||!await guard())return;draft=structuredClone(plans.find(p=>p.id===b.dataset.plan));dirty=false;message('');draw();$('.plan-list').focus({preventScroll:true});};
   const menu=e=>{e.preventDefault();e.stopPropagation();openGroupMenu(b,plans.find(p=>p.id===b.dataset.plan));};b.oncontextmenu=menu;b.onkeydown=e=>{if(e.key==='ContextMenu'||e.shiftKey&&e.key==='F10')menu(e)};
  });
 }

 function openGroupMenu(anchor,plan){
  if(!plan||busy)return;document.querySelector('.plan-group-menu')?.remove();
  const menu=document.createElement('div');menu.className='plan-group-menu scenario-quick-menu';menu.setAttribute('popover','auto');menu.innerHTML='<div class="scenario-quick-title">移动到分组</div><div class="plan-group-options"></div><form><input aria-label="新分组名称" maxlength="40" placeholder="新分组名称" required><button type="submit">新建并移入</button></form><p role="status"></p>';document.body.append(menu);
  const close=()=>{menu.hidePopover();menu.remove()};
  async function move(folder){
   if(busy)return;
   if(!plan.id){draft.folder=folder;changed();drawList();close();return;}
   await run(async()=>{const current=plans.find(p=>p.id===plan.id);if(!current)throw Error('方案已删除');const saved=await api('loadout-plan',{...current,folder});plans=plans.map(p=>p.id===saved.id?saved:p);if(draft?.id===saved.id){draft.folder=folder;draft.revision=saved.revision;draft.updatedAt=saved.updatedAt;cache();}drawList();message('已移入“'+(folder||'未分组')+'”');notify();close();});
  }
  for(const folder of [...new Set(['',...plans.map(p=>p.folder||''),draft?.folder||''])]){const b=document.createElement('button');b.type='button';b.textContent=(plan.folder===folder||!plan.folder&&!folder?'✓ ':'')+(folder||'未分组');b.onclick=()=>move(folder);menu.querySelector('.plan-group-options').append(b);}
  menu.querySelector('form').onsubmit=e=>{e.preventDefault();const name=menu.querySelector('input').value.trim();if(name)move(name);};
  menu.addEventListener('toggle',e=>{if(e.newState==='closed')menu.remove()});menu.showPopover();const r=anchor.getBoundingClientRect();menu.style.left=Math.max(8,Math.min(r.left,innerWidth-menu.offsetWidth-8))+'px';menu.style.top=Math.max(8,Math.min(r.bottom+4,innerHeight-menu.offsetHeight-8))+'px';menu.querySelector('button').focus();
 }
 $('[data-group-current]').onclick=()=>openGroupMenu($('[data-group-current]'),draft);
 function openPlanActions(){
  if(!draft||busy)return;document.querySelector('.plan-actions-menu')?.remove();
  const menu=document.createElement('div');menu.className='plan-actions-menu scenario-quick-menu';menu.setAttribute('popover','auto');menu.setAttribute('role','menu');document.body.append(menu);
  const close=()=>{menu.hidePopover();menu.remove()};
  const add=(label,fn,danger=false)=>{const b=document.createElement('button');b.type='button';b.role='menuitem';b.textContent=label;if(danger)b.className='danger';b.onclick=()=>{close();fn()};menu.append(b);};
  add('另存为新方案',()=>run(async()=>{
   const names=new Set(plans.map(p=>p.name));let n=1,name;do{name=draft.name.slice(0,65)+' · 副本'+(n>1?' '+n:'');n++;}while(names.has(name));
   const saved=await api('loadout-plan',{name,folder:draft.folder||'',implants:structuredClone(draft.implants),boosters:structuredClone(draft.boosters)});
   plans.push(saved);draft=structuredClone(saved);dirty=false;draw();message('已另存为“'+saved.name+'”');notify();
  }));
  if(dirty)add(draft.id?'恢复已保存版本':'丢弃未保存方案',async()=>{
   if(!await confirm(draft.id?'恢复“'+draft.name+'”上次保存的内容？本次未保存的修改将丢弃。':'丢弃当前未保存方案？'))return;
   draft=draft.id?structuredClone(plans.find(p=>p.id===draft.id)):null;dirty=false;draw();message(draft?'已恢复上次保存的版本':'未保存方案已丢弃');
  });
  if(draft.id)add('删除此方案',async()=>{
   if(!await confirm('从方案库删除“'+draft.name+'”？已有装配中的快照仍会保留。'))return;
   await run(async()=>{await api('loadout-plan/delete',{id:draft.id,revision:draft.revision});plans=plans.filter(p=>p.id!==draft.id);draft=null;dirty=false;draw();message('方案已删除');notify();});
  },true);
  menu.onkeydown=e=>{const buttons=[...menu.querySelectorAll('button')],i=buttons.indexOf(document.activeElement);if(['ArrowDown','ArrowUp'].includes(e.key)){e.preventDefault();buttons[(i+(e.key==='ArrowDown'?1:buttons.length-1))%buttons.length].focus();}};
  menu.addEventListener('toggle',e=>{if(e.newState==='closed')menu.remove()});menu.showPopover();const r=$('[data-more]').getBoundingClientRect();menu.style.left=Math.max(8,Math.min(r.left,innerWidth-menu.offsetWidth-8))+'px';menu.style.top=Math.max(8,Math.min(r.bottom+4,innerHeight-menu.offsetHeight-8))+'px';menu.querySelector('button').focus();
 }
 function rename(){
  if(!draft||$('.plan-name-input'))return;
  const heading=$('.plan-name-row h2'),button=$('[data-rename]'),input=document.createElement('input');input.className='fit-name-input plan-name-input';input.setAttribute('aria-label','方案名称');input.maxLength=80;input.value=draft.name;
  heading.hidden=true;button.hidden=true;heading.after(input);input.focus();input.select();
  let finished=false;
  const finish=commit=>{if(finished)return;finished=true;const value=input.value.trim();if(commit&&value&&value!==draft.name){draft.name=value;changed();}heading.textContent=draft.name;heading.hidden=false;button.hidden=false;input.remove();};
  input.onblur=()=>finish(true);input.onkeydown=e=>{if(e.isComposing)return;if(e.key==='Enter'||e.key==='Escape'){e.preventDefault();e.stopPropagation();finish(e.key==='Enter');button.focus();}};
 }
 function draw(){
  cache();drawList();browser.refresh();
  if(!draft){$('.plan-toolbar').innerHTML=returnToFit?'<button data-back>返回装配</button>':'';$('.plan-content').innerHTML='<div class="character-empty">创建一套可跨装配调用的脑插与增效剂方案。</div>';host.querySelector('[data-back]')?.addEventListener('click',()=>location.hash='fitting');return;}
  $('.plan-toolbar').innerHTML='<div class="plan-name-row fit-name-row"><h2>'+esc(draft.name)+'</h2><button class="edit-name-icon" data-rename aria-label="编辑方案名称" title="编辑名称"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true"><path d="m15 5 4 4M4 20l4-1L20 7a2.8 2.8 0 0 0-4-4L4 15Z"/></svg></button></div><span class="plan-dirty">'+(dirty?'未保存':'已保存')+'</span><button data-more aria-haspopup="menu" aria-label="更多方案操作">更多 ···</button><button data-save>保存方案</button>'+(returnToFit?'<button data-use>应用到装配</button><button data-back>返回装配</button>':'');
  $('.plan-content').innerHTML='<div class="plan-meta"><small>交互原型 · 加成与技能校验待接入</small></div><div class="plan-section-head"><b>脑插</b><span>'+draft.implants.length+' / 10</span></div><div class="plan-slots"></div><div class="plan-section-head"><b>增效剂</b></div><div class="plan-boosters"></div><p class="plan-scope">固定副作用方案 · 不进行随机服用或倒计时</p><div class="plan-unload">拖到这里卸下</div>';
   $('[data-rename]').onclick=rename;$('.plan-name-row h2').onclick=rename;
  $('[data-save]').onclick=save;$('[data-more]').onclick=openPlanActions;
  $('[data-use]')?.addEventListener('click',()=>{document.dispatchEvent(new CustomEvent('fitlab-use-loadout',{detail:{...structuredClone(draft),customized:dirty}}));location.hash='fitting';});
  $('[data-back]')?.addEventListener('click',()=>location.hash='fitting');
  drawSlots();
  const unloadArea=$('.plan-unload');unloadArea.ondragover=e=>{if(unload(null)){e.preventDefault();e.dataTransfer.dropEffect='move'}};unloadArea.ondrop=e=>{if(drag?.source==='installed'){e.preventDefault();draft[drag.kind]=draft[drag.kind].filter(x=>x.typeId!==drag.id);clearDrag();changed();drawSlots();}};
 }
 function drawSlots(){
  browser.refresh();
  $('.plan-section-head span').textContent=draft.implants.length+' / 10';
  const card=(kind,slot)=>{const entry=draft[kind].find(x=>x.slot===slot),t=entry&&find(kind,entry.typeId);return '<div class="plan-slot" data-kind="'+kind+'" data-slot="'+slot+'"><button class="plan-slot-main" '+(entry?'draggable="true"':'')+' aria-label="'+(kind==='implants'?'脑插':'增效剂')+'槽位 '+slot+'"><small>'+String(slot).padStart(2,'0')+'</small>'+icon(t)+'<span>'+esc(t?.name||(entry?'未知物品 #'+entry.typeId:'选择'+(kind==='implants'?'脑插':'增效剂')))+'</span></button>'+(entry?'<button class="plan-remove" aria-label="卸下'+(kind==='implants'?'脑插':'增效剂')+' '+slot+'">×</button>':'')+'</div>';};
  $('.plan-slots').innerHTML=Array.from({length:10},(_,i)=>card('implants',i+1)).join('');
  $('.plan-boosters').innerHTML=[...new Set([1,2,3,...draft.boosters.map(b=>b.slot)])].sort((a,b)=>a-b).map(slot=>{
   const entry=draft.boosters.find(b=>b.slot===slot),t=entry&&find('boosters',entry.typeId);
   return '<div>'+card('boosters',slot)+(entry?'<div class="plan-side-effects">'+(t?.sideEffects.length?'<span>副作用选择</span>'+t.sideEffects.map(e=>'<label><input type="checkbox" data-booster="'+entry.typeId+'" data-effect="'+e.id+'" '+(entry.enabledSideEffects?.includes(e.id)?'checked':'')+'>'+esc(e.name)+'</label>').join(''):'<span>无可选副作用</span>')+'</div>':'')+'</div>';
  }).join('');
  host.querySelectorAll('.plan-slot').forEach(el=>{const kind=el.dataset.kind,slot=Number(el.dataset.slot),main=el.querySelector('.plan-slot-main');main.onclick=()=>browser.select(kind,slot);const context=e=>{const entry=draft[kind].find(x=>x.slot===slot),t=entry&&find(kind,entry.typeId);if(!t)return;e.preventDefault();e.stopPropagation();document.dispatchEvent(new CustomEvent('fitlab-loadout-menu',{detail:{event:e,item:t,origin:main,entries:[['卸下',()=>{draft[kind]=draft[kind].filter(x=>x.slot!==slot);changed();drawSlots();}]]}}));};main.oncontextmenu=context;main.onkeydown=e=>{if(e.key==='ContextMenu'||e.shiftKey&&e.key==='F10')context(e)};el.ondragover=e=>{const t=drag&&find(drag.kind,drag.id);if(drag?.source==='browser'&&drag.kind===kind&&t?.slot===slot){e.preventDefault();e.dataTransfer.dropEffect='copy';}};el.ondrop=e=>{e.stopPropagation();const t=drag&&find(drag.kind,drag.id);if(drag?.source==='browser'&&drag.kind===kind&&t?.slot===slot){e.preventDefault();installItem(kind,t);}clearDrag();};el.querySelector('.plan-remove')?.addEventListener('click',()=>{draft[kind]=draft[kind].filter(x=>x.slot!==slot);changed();drawSlots();});main.ondragstart=e=>{const entry=draft[kind].find(x=>x.slot===slot);if(!entry){e.preventDefault();return;}drag={kind,id:entry.typeId,source:'installed'};e.dataTransfer.setData('text/plain',String(entry.typeId));e.dataTransfer.effectAllowed='move';};main.ondragend=clearDrag;});
  host.querySelectorAll('[data-effect]').forEach(box=>box.onchange=()=>{const entry=draft.boosters.find(b=>b.typeId===Number(box.dataset.booster)),id=Number(box.dataset.effect);entry.enabledSideEffects=(entry.enabledSideEffects||[]).filter(e=>e!==id);if(box.checked)entry.enabledSideEffects.push(id);changed();});
 }
 const notify=()=>document.dispatchEvent(new Event('fitlab-loadout-library-changed'));
 async function run(fn){if(busy)return;busy=true;$('.plan-workspace').inert=true;try{await fn()}catch(e){message(e.message)}finally{busy=false;$('.plan-workspace').inert=false;}}
 async function save(){if(!draft.name.trim()){message('请填写方案名称');return;}await run(async()=>{const saved=await api('loadout-plan',draft);plans=plans.filter(p=>p.id!==saved.id).concat(saved);draft=structuredClone(saved);dirty=false;draw();message('方案已保存 · 已有装配快照不变');notify();});}
 function duplicate(){if(!draft)return;const names=new Set(plans.map(p=>p.name));let n=1,name;do{name=draft.name.slice(0,65)+' · 副本'+(n>1?' '+n:'');n++;}while(names.has(name));draft={name,folder:draft.folder||'',implants:structuredClone(draft.implants),boosters:structuredClone(draft.boosters)};dirty=true;draw();message('副本尚未保存');}
 $('[data-new]').onclick=async()=>{if(busy||!await guard())return;draft={name:'新方案',folder:'',implants:[],boosters:[]};dirty=true;draw();rename();};$('.plan-search').oninput=drawList;
 const scope=e=>!host.hidden&&!host.closest('[hidden]')&&!document.querySelector('dialog[open]')&&e.target.closest('.plan-library')&&!e.target.closest('input,textarea,[contenteditable]');
 document.addEventListener('copy',e=>{if(!scope(e)||!draft||!e.clipboardData)return;e.clipboardData.setData('text/plain',JSON.stringify({format:'fitlab-loadout',plan:draft}));e.preventDefault();message('方案已复制');});
 document.addEventListener('paste',async e=>{
  if(!scope(e)||!e.clipboardData)return;
  let data;try{data=JSON.parse(e.clipboardData.getData('text/plain'))}catch{return;}
  if(data?.format!=='fitlab-loadout')return;e.preventDefault();const p=data.plan;
  if(!p||typeof p.name!=='string'||!Array.isArray(p.implants)||!Array.isArray(p.boosters))return;
  for(const kind of ['implants','boosters']){
   if(new Set(p[kind].map(x=>x?.slot)).size!==p[kind].length)return;
   for(const x of p[kind]){const t=x&&find(kind,x.typeId);if(!t||t.slot!==x.slot)return;
    if(kind==='boosters'&&(!Array.isArray(x.enabledSideEffects)||x.enabledSideEffects.some(id=>!t.sideEffects.some(e=>e.id===id))))return;
   }
  }
  if(!await guard())return;draft=structuredClone(p);duplicate();
 });
 return {async show({id,initial,fromFit=false}={}){host.hidden=false;returnToFit=fromFit;try{plans=await api('loadout-plans');if(initial&&(!id||!plans.some(p=>p.id===id))&&await guard()){draft=structuredClone(initial);delete draft.id;delete draft.revision;dirty=true;id=null;}if(id&&(!draft||draft.id!==id)&&await guard()){draft=structuredClone(plans.find(p=>p.id===id)||null);dirty=false;}if(!draft&&plans.length)draft=structuredClone(plans[0]);draw();}catch(e){message(e.message);}},hide(){host.hidden=true;}};
}

export async function openLoadoutPicker(anchor,{api,snapshot,onSelect,onManage}){
 document.querySelector('.loadout-quick')?.remove();
 const menu=document.createElement('div');menu.className='loadout-quick scenario-quick-menu';menu.setAttribute('popover','auto');menu.setAttribute('role','menu');menu.innerHTML='<div class="scenario-quick-title">正在读取方案…</div>';document.body.append(menu);menu.showPopover();
 const position=()=>{const r=anchor.getBoundingClientRect();menu.style.left=Math.max(8,Math.min(r.right-menu.offsetWidth,innerWidth-menu.offsetWidth-8))+'px';menu.style.top=Math.max(8,Math.min(r.bottom+5,innerHeight-menu.offsetHeight-8))+'px';};position();
 menu.addEventListener('toggle',e=>{if(e.newState==='closed')menu.remove()});
 const close=()=>{menu.hidePopover();menu.remove()};
 try{
  const plans=await api('loadout-plans');if(!menu.isConnected)return;menu.innerHTML='<input class="loadout-search" aria-label="搜索可用方案" placeholder="搜索方案">';
  const add=(label,fn)=>{const b=document.createElement('button');b.role='menuitem';b.textContent=label;b.onclick=()=>{close();fn()};menu.append(b);return b;};
  add('不使用方案',()=>onSelect(null));
  for(const p of plans){const b=add((snapshot?.id===p.id?'✓ ':'')+p.name+(snapshot?.id===p.id?(snapshot.customized?' · 已自定义':snapshot.revision!==p.revision?' · 有更新':''):''),()=>onSelect(p));b.dataset.search=(p.name+' '+(p.folder||'')).toLowerCase();}
  const search=menu.querySelector('input');search.oninput=()=>{menu.querySelectorAll('[data-search]').forEach(b=>b.hidden=!b.dataset.search.includes(search.value.trim().toLowerCase()));position();};
  menu.onkeydown=e=>{if(!['ArrowDown','ArrowUp'].includes(e.key))return;e.preventDefault();const buttons=[...menu.querySelectorAll('button')].filter(b=>!b.hidden),index=buttons.indexOf(document.activeElement);buttons[(index+(e.key==='ArrowDown'?1:buttons.length-1)+buttons.length)%buttons.length]?.focus();};
  add('管理方案…',onManage);position();search.focus();
 }catch(e){menu.textContent=e.message;position();}
}
