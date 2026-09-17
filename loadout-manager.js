import {parentFolder,folderName,normalizeLayout,relocateLibrary,installLibraryDrag} from './plan-library-tree.js';
import {installPlanResize} from './plan-resize.js';
import {installPlanBrowser} from './plan-browser.js';
import {implantCatalog} from './implant-catalog.js';
import {boosterCatalog} from './booster-catalog.js';
import {escapeHtml as esc} from './scenario-display.js';
const catalogs={implants:implantCatalog.filter(t=>t.slot>=1&&t.slot<=10),boosters:boosterCatalog};
const find=(kind,id)=>catalogs[kind].find(t=>t.id===id);
const icon=t=>t?'<img loading="lazy" draggable="false" src="https://images.evetech.net/types/'+t.id+'/icon?size=64" alt="">':'<span class="plan-plus">＋</span>';

export function installLoadoutManager(host,{api}){
 let plans=[],draft=null,dirty=false,busy=false,drag=null,returnToFit=false;
 host.innerHTML='<div class="plan-workspace"><aside class="plan-library"><div class="plan-library-head"><button data-toggle-library aria-expanded="true" aria-controls="plan-library-body"><span class="plan-library-chevron">▾</span> 方案库</button><span class="plan-library-current"></span></div><div id="plan-library-body"><input class="plan-search" aria-label="搜索方案" placeholder="搜索方案或分组"><div class="plan-list plan-folder-list" tabindex="0" aria-label="脑插与增效剂方案列表"></div><div class="plan-library-blank" tabindex="0" aria-label="方案库空白区域" title="右键新建分组"></div><small>Ctrl+C / V 复制粘贴</small></div></aside><aside class="plan-browser"></aside><section class="plan-editor"><div class="plan-toolbar"></div><p class="plan-message" role="status"></p><div class="plan-content"></div></section></div>';
 const $=s=>host.querySelector(s),message=text=>$('.plan-message').textContent=text;
 let layout={revision:0,folders:[],order:[]};const folderOpen=new Set(['']);let activeFolder=null,extraFolders=[];try{extraFolders=JSON.parse(localStorage.getItem('fitlab-plan-folders')||'[]').filter(x=>typeof x==='string')}catch{}
 const storeFolders=()=>localStorage.setItem('fitlab-plan-folders',JSON.stringify(extraFolders));
 let libraryCollapsed=false;try{libraryCollapsed=localStorage.getItem('fitlab-plan-library-collapsed')==='true'}catch{}
 function updateLibraryFold(){
  $('#plan-library-body').hidden=libraryCollapsed;$('.plan-workspace').classList.toggle('library-collapsed',libraryCollapsed);
  $('[data-toggle-library]').setAttribute('aria-expanded',String(!libraryCollapsed));$('.plan-library-chevron').textContent=libraryCollapsed?'▸':'▾';
 }
 $('[data-toggle-library]').onclick=()=>{libraryCollapsed=!libraryCollapsed;updateLibraryFold();try{localStorage.setItem('fitlab-plan-library-collapsed',String(libraryCollapsed))}catch{}};
 updateLibraryFold();
 installPlanResize($('.plan-workspace'));

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
  $('.plan-library-current').textContent=draft?.name||'';
  layout=normalizeLayout(layout,plans,extraFolders);
  const tree=$('.plan-list'),scroll=tree.scrollTop,q=$('.plan-search').value.trim().toLowerCase();
  const ordered=keys=>keys.sort((a,b)=>layout.order.indexOf(a)-layout.order.indexOf(b));
  const matches=p=>!q||(p.name+' '+(p.folder||'')).toLowerCase().includes(q);
  const planRow=p=>'<button class="plan-entry" draggable="true" data-library-key="p:'+p.id+'" data-plan="'+p.id+'" aria-pressed="'+(draft?.id===p.id)+'"><span>'+esc(p.name)+'</span><small>脑插 '+p.implants.length+' · 药剂 '+p.boosters.length+'</small></button>';
  function folderRow(folder){
   const nested=layout.folders.filter(f=>parentFolder(f)===folder),direct=plans.filter(p=>(p.folder||'')===folder),rows=ordered([...nested.map(f=>'f:'+f),...direct.filter(matches).map(p=>'p:'+p.id)]);
   const content=rows.map(k=>k.startsWith('f:')?folderRow(k.slice(2)):planRow(direct.find(p=>p.id===k.slice(2)))).join('');
   if(q&&!content&&!folder.toLowerCase().includes(q))return '';
   return '<details class="plan-tree-group" '+(q||folderOpen.has(folder)?'open':'')+'><summary draggable="'+Boolean(folder)+'" data-library-key="f:'+esc(folder)+'" data-folder-path="'+esc(folder)+'" '+(activeFolder===folder?'class="active-folder"':'')+'><span>'+esc(folderName(folder)||'未分组')+'</span><small>'+plans.filter(p=>p.folder===folder||folder&&p.folder?.startsWith(folder+'/')).length+'</small></summary><div class="plan-tree-children">'+(content||'<span class="plan-tree-empty">空分组</span>')+'</div></details>';
  }
  // The ungrouped bucket contains only root plans; root folders remain its siblings.
  const roots=ordered(layout.folders.filter(f=>!parentFolder(f)).map(f=>'f:'+f));
  const ungrouped=plans.filter(p=>!p.folder&&matches(p));
  tree.innerHTML='<details class="plan-tree-group" '+(q||folderOpen.has('')?'open':'')+'><summary data-library-key="f:" data-folder-path=""><span>未分组</span><small>'+ungrouped.length+'</small></summary><div class="plan-tree-children">'+ordered(ungrouped.map(p=>'p:'+p.id)).map(k=>planRow(ungrouped.find(p=>p.id===k.slice(2)))).join('')+'</div></details>'+roots.map(k=>folderRow(k.slice(2))).join('');tree.scrollTop=scroll;
  tree.querySelectorAll('summary').forEach(b=>{const folder=b.dataset.folderPath;b.onclick=e=>{e.preventDefault();activeFolder=folder;const open=!b.parentElement.open;b.parentElement.open=open;if(open)folderOpen.add(folder);else folderOpen.delete(folder);tree.querySelectorAll('summary').forEach(x=>x.classList.toggle('active-folder',x===b));};
   b.oncontextmenu=e=>{e.preventDefault();e.stopPropagation();folderMenu(b,folder,e)};b.onkeydown=e=>{if(e.key==='ContextMenu'||e.shiftKey&&e.key==='F10'){e.preventDefault();e.stopPropagation();folderMenu(b,folder,e)}};
  });
  tree.querySelectorAll('[data-plan]').forEach(b=>b.onclick=async()=>{if(busy||!await guard())return;draft=structuredClone(plans.find(p=>p.id===b.dataset.plan));activeFolder=draft.folder||'';dirty=false;message('');draw();$('.plan-list').focus({preventScroll:true});});
 }
 async function commitLayout(next,nextPlans=plans){
  next=normalizeLayout(next,nextPlans);
  const result=await api('loadout-layout',{...next,plans:nextPlans.map(p=>({id:p.id,revision:p.revision,folder:p.folder||''}))});
  plans=result.plans;layout=result.layout;extraFolders=layout.folders.slice();storeFolders();
  if(draft?.id){const saved=plans.find(p=>p.id===draft.id);if(saved){draft.folder=saved.folder;draft.revision=saved.revision;draft.updatedAt=saved.updatedAt;cache();}}
  drawList();notify();
 }
 installLibraryDrag($('.plan-list'),$('.plan-library-blank'),{
  canStart:()=>{if($('.plan-search').value.trim()){message('清除搜索后可拖动排序');return false;}return !busy;},
  validate:(source,target,mode)=>{if(source==='f:'||source?.startsWith('f:')&&target==='f:')throw Error('请选择分组或根空白处');return relocateLibrary(layout,plans,source,target,mode)},
  onDrop:async(source,target,mode)=>run(async()=>{const result=relocateLibrary(layout,plans,source,target,mode);await commitLayout(result.layout,result.plans);activeFolder=result.destination;folderOpen.add(result.destination);if(result.moved.startsWith('f:'))folderOpen.add(result.moved.slice(2));drawList();message(mode==='inside'?'已移入目标分组':'已调整顺序');}),onError:e=>message(e.message)
 });
 function editFolder(previous=null,parent=''){
  libraryCollapsed=false;updateLibraryFold();$('.plan-folder-editor')?.remove();
  const input=document.createElement('input');input.className='plan-folder-editor';input.setAttribute('aria-label',previous?'重命名分组':'新分组名称');input.placeholder='分组名称 · Enter 确认';input.maxLength=40;input.value=previous?folderName(previous):'';
  const anchor=[...$('.plan-list').querySelectorAll('summary')].find(x=>x.dataset.folderPath===(previous||parent));if(anchor&&parent){anchor.parentElement.open=true;anchor.after(input)}else $('.plan-list').prepend(input);input.focus();input.select();let done=false;
  const finish=async commit=>{if(done)return;done=true;const name=input.value.trim();input.remove();if(!commit||!name)return;
   const targetParent=previous?parentFolder(previous):parent,path=(targetParent?targetParent+'/':'')+name;
   if(path===previous)return;if(name.includes('/')||name==='未分组'||layout.folders.includes(path)||path.split('/').length>16){message('分组名称重复、含有斜杠或层级过深');return;}
   await run(async()=>{let next=structuredClone(layout),copies=structuredClone(plans);if(previous){const rewrite=f=>f===previous?path:f.startsWith(previous+'/')?path+f.slice(previous.length):f;next.folders=next.folders.map(rewrite);next.order=next.order.map(k=>k.startsWith('f:')?'f:'+rewrite(k.slice(2)):k);copies.forEach(p=>p.folder=rewrite(p.folder||''));}else next.folders.push(path);await commitLayout(next,copies);activeFolder=path;folderOpen.add(targetParent);folderOpen.add(path);drawList();});};
  input.onkeydown=e=>{if(e.isComposing)return;if(e.key==='Enter'||e.key==='Escape'){e.preventDefault();e.stopPropagation();finish(e.key==='Enter')}};input.onblur=()=>finish(true);
 }
 function folderMenu(anchor,name,event){
  document.querySelector('.plan-folder-menu')?.remove();const menu=document.createElement('div');menu.className='plan-folder-menu scenario-quick-menu';menu.setAttribute('popover','auto');menu.setAttribute('role','menu');
  const actions=name===null?[['新建分组',()=>editFolder()]]:[['新建方案',()=>newPlan(name)],...(name?[['新建子分组',()=>editFolder(null,name)],['重命名分组',()=>editFolder(name)],['解散分组',async()=>{if(!await confirm('解散“'+folderName(name)+'”？子分组及方案会移到上一级。'))return;run(async()=>{
   const parent=parentFolder(name),rewrite=f=>f===name?parent:f.startsWith(name+'/')?(parent?parent+'/':'')+f.slice(name.length+1):f;
   const next=structuredClone(layout);next.folders=next.folders.filter(f=>f!==name).map(rewrite);if(new Set(next.folders).size!==next.folders.length)throw Error('上一级存在同名分组，请先改名');next.order=next.order.filter(k=>k!=='f:'+name).map(k=>k.startsWith('f:')?'f:'+rewrite(k.slice(2)):k);const copies=plans.map(p=>({...p,folder:rewrite(p.folder||'')}));await commitLayout(next,copies);activeFolder=parent;folderOpen.add(parent);drawList();
  });}]]:[])];
  for(const [label,fn] of actions){const b=document.createElement('button');b.role='menuitem';b.textContent=label;b.onclick=()=>{menu.hidePopover();menu.remove();fn()};menu.append(b)}
  menu.addEventListener('toggle',e=>{if(e.newState==='closed')menu.remove()});document.body.append(menu);menu.showPopover();const r=anchor.getBoundingClientRect(),pointer=event?.type==='contextmenu'&&(event.clientX!==0||event.clientY!==0),x=pointer?event.clientX:r.left,y=pointer?event.clientY:r.bottom;menu.style.left=Math.max(8,Math.min(x,innerWidth-menu.offsetWidth-8))+'px';menu.style.top=Math.max(8,Math.min(y,innerHeight-menu.offsetHeight-8))+'px';menu.querySelector('button').focus();
 }
 const libraryBody=$('#plan-library-body');const blankMenu=e=>{if(e.target.closest('button,input,textarea,summary,.plan-tree-group'))return;e.preventDefault();e.stopPropagation();folderMenu(e.target.closest('[tabindex]')||libraryBody,null,e)};libraryBody.oncontextmenu=blankMenu;libraryBody.onkeydown=e=>{if(e.key==='ContextMenu'||e.shiftKey&&e.key==='F10')blankMenu(e)};
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
  $('.plan-toolbar').innerHTML='<div class="plan-name-row fit-name-row"><h2>'+esc(draft.name)+'</h2><button class="edit-name-icon" data-rename aria-label="编辑方案名称" title="编辑名称"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true"><path d="m15 5 4 4M4 20l4-1L20 7a2.8 2.8 0 0 0-4-4L4 15Z"/></svg></button></div><span class="plan-dirty">'+(dirty?'未保存':'已保存')+'</span><button data-save>保存方案</button><button data-more aria-haspopup="menu" aria-label="更多方案操作">更多 ···</button>'+(returnToFit?'<button data-use>应用到装配</button><button data-back>返回装配</button>':'');
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
 async function newPlan(folder){if(busy||!await guard())return;activeFolder=folder;folderOpen.add(folder);draft={name:'新方案',folder,implants:[],boosters:[]};dirty=true;draw();rename();}
 $('.plan-search').oninput=drawList;
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
 return {async show({id,initial,fromFit=false}={}){host.hidden=false;returnToFit=fromFit;try{[plans,layout]=await Promise.all([api('loadout-plans'),api('loadout-layout')]);if(layout.revision>0)extraFolders=layout.folders.slice();if(initial&&(!id||!plans.some(p=>p.id===id))&&await guard()){draft=structuredClone(initial);delete draft.id;delete draft.revision;dirty=true;id=null;}if(id&&(!draft||draft.id!==id)&&await guard()){draft=structuredClone(plans.find(p=>p.id===id)||null);dirty=false;}if(!draft&&plans.length)draft=structuredClone(plans[0]);draw();}catch(e){message(e.message);}},hide(){host.hidden=true;}};
}

export async function openLoadoutPicker(anchor,{api,snapshot,onSelect,onManage}){
 document.querySelector('.loadout-quick')?.remove();
 const menu=document.createElement('div');menu.className='loadout-quick scenario-quick-menu';menu.setAttribute('popover','auto');menu.setAttribute('role','menu');menu.innerHTML='<div class="scenario-quick-title">正在读取方案…</div>';document.body.append(menu);menu.showPopover();
 const position=()=>{const r=anchor.getBoundingClientRect();menu.style.left=Math.max(8,Math.min(r.right-menu.offsetWidth,innerWidth-menu.offsetWidth-8))+'px';menu.style.top=Math.max(8,Math.min(r.bottom+5,innerHeight-menu.offsetHeight-8))+'px';};position();
 menu.addEventListener('toggle',e=>{if(e.newState==='closed')menu.remove()});
 const close=()=>{menu.hidePopover();menu.remove()};
 try{
  const [plans,layout]=await Promise.all([api('loadout-plans'),api('loadout-layout')]);if(layout.revision>0)extraFolders=layout.folders.slice();if(!menu.isConnected)return;menu.innerHTML='<input class="loadout-search" aria-label="搜索可用方案" placeholder="搜索方案">';
  const add=(label,fn)=>{const b=document.createElement('button');b.role='menuitem';b.textContent=label;b.onclick=()=>{close();fn()};menu.append(b);return b;};
  add('不使用方案',()=>onSelect(null));
  for(const p of plans){const b=add((snapshot?.id===p.id?'✓ ':'')+p.name+(snapshot?.id===p.id?(snapshot.customized?' · 已自定义':snapshot.revision!==p.revision?' · 有更新':''):''),()=>onSelect(p));b.dataset.search=(p.name+' '+(p.folder||'')).toLowerCase();}
  const search=menu.querySelector('input');search.oninput=()=>{menu.querySelectorAll('[data-search]').forEach(b=>b.hidden=!b.dataset.search.includes(search.value.trim().toLowerCase()));position();};
  menu.onkeydown=e=>{if(!['ArrowDown','ArrowUp'].includes(e.key))return;e.preventDefault();const buttons=[...menu.querySelectorAll('button')].filter(b=>!b.hidden),index=buttons.indexOf(document.activeElement);buttons[(index+(e.key==='ArrowDown'?1:buttons.length-1)+buttons.length)%buttons.length]?.focus();};
  add('管理方案…',onManage);position();search.focus();
 }catch(e){menu.textContent=e.message;position();}
}
