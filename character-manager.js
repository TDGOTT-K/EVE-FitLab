import {matchesName} from './i18n.js';
import {readPilotFolders,writePilotFolders,onPilotFoldersChanged} from './pilot-folders.js';
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const portrait='<svg viewBox="0 0 32 32" fill="none" stroke="currentColor" stroke-width="1.3" aria-hidden="true"><circle cx="16" cy="11" r="6"/><path d="M5 30v-4c0-8 22-8 22 0v4"/></svg>';
export function installCharacterManager({api,catalog,onReturn}){
 const root=document.createElement('main');root.id='characters-page';root.hidden=true;
 root.innerHTML='<div class="character-heading"><div><small>CHARACTER ROSTER / 角色管理</small><h1>角色管理</h1></div></div><div class="character-workspace"><aside class="character-roster"><div class="character-tree-title"><span>角色</span><button id="character-add" aria-label="新增角色或文件夹" title="新增角色或文件夹">＋</button></div><div id="character-folder-editor" hidden></div><input id="character-search" aria-label="搜索角色" placeholder="搜索角色或文件夹"><div id="character-list"></div><div class="character-tree-footer">拖入文件夹 · 右键管理</div></aside><section class="character-editor"><div id="character-status" role="status"></div><div id="character-detail"></div></section></div>';
 document.querySelector('header').after(root);
 const contextMenu=document.createElement('div');contextMenu.className='character-context-menu';contextMenu.role='menu';contextMenu.hidden=true;document.body.append(contextMenu);
 const $=s=>root.querySelector(s),skills=catalog.filter(t=>t.kind==='skill').sort((a,b)=>a.name.localeCompare(b.name,'zh-CN'));
 const group=t=>t.path?.at(-1)||'其他技能';
 let people=[],selected=null,draft=null,dirty=false,loaded=false,generation=0;
 let folders=readPilotFolders(),activeFolder='',draggedPerson=null;const skillOpen=new Set();
 const status=message=>$('#character-status').textContent=message;
 const editable=c=>c&&!['内置','EVE 官网','EdenOS 已保存快照'].includes(c.source);
 const current=()=>draft||selected;
 const modal=document.createElement('dialog');modal.className='character-action-dialog';document.body.append(modal);
 function ask(title,message,{value,accept='确定'}={}){return new Promise(resolve=>{
  modal.innerHTML='<form><div class="flow-head"><b>'+esc(title)+'</b><button type="button" data-cancel aria-label="关闭">×</button></div><div class="flow-body"><p>'+esc(message)+'</p>'+(value!==undefined?'<input aria-label="角色名称" maxlength="80" required value="'+esc(value)+'">':'')+'<div class="character-dialog-actions"><button type="button" data-cancel>取消</button><button type="submit">'+esc(accept)+'</button></div></div></form>';
  let answer=null;modal.onclose=()=>resolve(answer);modal.querySelectorAll('[data-cancel]').forEach(b=>b.onclick=()=>modal.close());modal.querySelector('form').onsubmit=e=>{e.preventDefault();const input=modal.querySelector('input');if(input&&!input.value.trim())return;answer=input?input.value.trim():true;modal.close()};modal.showModal();const input=modal.querySelector('input');if(input){input.focus();input.select()}
 })}
 const cacheDraft=()=>{try{if(draft&&dirty)sessionStorage.setItem('fitlab-character-draft',JSON.stringify(draft));else sessionStorage.removeItem('fitlab-character-draft')}catch{}};
 async function guard(){if(!dirty)return true;if(!await ask('未保存的修改','尚有未保存的角色修改，是否放弃？',{accept:'放弃修改'}))return false;dirty=false;draft=null;cacheDraft();drawList();drawDetail();status('');return true}

 const icon=c=>c?.eveCharacterId?'<img src="https://images.evetech.net/characters/'+Number(c.eveCharacterId)+'/portrait?size=128" alt="">':portrait;
 const folderIcon='<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" aria-hidden="true"><path d="M3 6h7l2 3h9v12H3Z"/></svg>';
 const folderOf=c=>folders.folders.some(f=>f.id===folders.assignment[String(c.id)])?folders.assignment[String(c.id)]:'';
 function saveFolders(){try{writePilotFolders(folders)}catch{status('文件夹保存失败，请检查浏览器存储')}}
 function movePerson(id,folder){folders=readPilotFolders();folders.assignment[String(id)]=folder;saveFolders()}
 function menu(e,entries){
  e.preventDefault();e.stopPropagation();contextMenu.innerHTML='';
  entries.forEach(([label,action,enabled=true])=>{const b=document.createElement('button');b.role='menuitem';b.textContent=label;b.disabled=!enabled;b.onclick=async()=>{contextMenu.hidden=true;try{await action()}catch(e){status(e.message)}};contextMenu.append(b)});
  contextMenu.hidden=false;const r=e.currentTarget.getBoundingClientRect();contextMenu.style.left=Math.max(8,Math.min(e.clientX||r.left,innerWidth-contextMenu.offsetWidth-8))+'px';contextMenu.style.top=Math.max(8,Math.min(e.clientY||r.bottom,innerHeight-contextMenu.offsetHeight-8))+'px';contextMenu.querySelector('button')?.focus();
 }
 function editFolder(folder=null){
  const box=$('#character-folder-editor');box.hidden=false;box.innerHTML='<form><input aria-label="文件夹名称" placeholder="文件夹名称" maxlength="40" required><button>确定</button><button type="button" aria-label="取消文件夹编辑">×</button></form>';
  const input=box.querySelector('input');input.value=folder?.name||'';input.focus();
  box.querySelector('form').onsubmit=e=>{e.preventDefault();const name=input.value.trim();if(!name)return;folders=readPilotFolders();if(folder){const f=folders.folders.find(f=>f.id===folder.id);if(f)f.name=name}else{activeFolder=crypto.randomUUID();folders.folders.push({id:activeFolder,name,open:true})}box.hidden=true;saveFolders()};
  box.querySelector('[type=button]').onclick=()=>box.hidden=true;
 }
 async function newCharacter(){if(!await guard())return;const name=await ask('新建角色','角色名称',{value:'',accept:'创建'});if(!name)return;selected=null;draft={name,skills:[],source:'自定义角色'};dirty=true;cacheDraft();status('设置技能后保存。');drawList();drawDetail()}
 async function editCharacter(c){if(!await guard())return;selected=c;activeFolder=folderOf(c);draft=structuredClone(c);dirty=false;status('');drawList();drawDetail()}
 async function copyCharacter(c){if(!await guard())return;const name=await ask('复制角色','副本名称',{value:(c.name+' · 副本').slice(0,80),accept:'复制'});if(!name)return;selected=null;activeFolder=folderOf(c);draft={name,skills:structuredClone(c.skills),source:'自定义角色'};dirty=true;cacheDraft();drawList();drawDetail();status('副本已准备，保存后加入角色列表。')}
 async function renameCharacter(c){if(!await guard())return;const name=await ask('修改角色名称','角色名称',{value:c.name,accept:'保存'});if(!name)return;const saved=await api('character',{...c,name});people=people.filter(p=>p.id!==c.id).concat(saved);selected=saved;drawList();drawDetail();status('名称已更新')}
 async function deleteCharacter(c){if(!await guard())return;if(!await ask('删除角色','删除「'+c.name+'」？已有装配保留自己的技能快照。',{accept:'删除角色'}))return;await api('character/delete',{id:c.id});people=people.filter(p=>p.id!==c.id);folders=readPilotFolders();delete folders.assignment[String(c.id)];saveFolders();if(selected?.id===c.id)selected=null;drawList();drawDetail();status('角色已删除')}

 const addEntries=()=>[['新建自定义角色',newCharacter],['新建文件夹',()=>editFolder()],['从 EVE 官网导入',login]];
 function drawList(){
  const q=$('#character-search').value.trim().toLowerCase();
  const row=c=>'<button class="character-person" draggable="true" data-character="'+esc(c.id)+'" aria-pressed="'+(selected?.id===c.id)+'">'+icon(c)+'<span>'+esc(c.name)+'<small>'+esc(c.source)+' · '+c.skills.filter(s=>s.level>0).length+' 项技能</small></span></button>';
  $('#character-list').innerHTML=folders.folders.map(f=>{const rows=people.filter(c=>folderOf(c)===f.id),matches=rows.filter(c=>c.name.toLowerCase().includes(q)||f.name.toLowerCase().includes(q));if(q&&!matches.length&&!f.name.toLowerCase().includes(q))return '';return '<details class="character-folder" data-folder="'+esc(f.id)+'" '+((q||f.open!==false)?'open':'')+'><summary>'+folderIcon+'<span>'+esc(f.name)+'</span><small>'+rows.length+'</small></summary>'+matches.map(row).join('')+(!matches.length?'<div class="pilot-empty">拖入角色</div>':'')+'</details>'}).join('')+'<div class="character-root" data-folder=""><div class="character-root-label">未分组</div>'+people.filter(c=>!folderOf(c)&&c.name.toLowerCase().includes(q)).map(row).join('')+'</div>';
  $('#character-list').querySelectorAll('[data-character]').forEach(b=>{
   const c=people.find(c=>String(c.id)===b.dataset.character);
   b.onclick=async()=>{if(!await guard())return;selected=c;activeFolder=folderOf(c);draft=null;dirty=false;status('');drawList();drawDetail()};
   const open=e=>menu(e,[['修改名称',()=>renameCharacter(c),editable(c)],['复制角色',()=>copyCharacter(c)],['编辑角色',()=>editCharacter(c),editable(c)],['删除角色',()=>deleteCharacter(c),!['内置','EdenOS 已保存快照'].includes(c.source)],...(c.source==='EVE 官网'?[['重新授权更新',login]]:[]),['移到未分组',()=>movePerson(c.id,'')],...folders.folders.map(f=>['移到 '+f.name,()=>movePerson(c.id,f.id)])]);
   b.oncontextmenu=open;b.onkeydown=e=>{if(e.key==='ContextMenu'||e.shiftKey&&e.key==='F10')open(e)};
   b.ondragstart=e=>{draggedPerson=c.id;e.dataTransfer.setData('text/plain',String(c.id));e.dataTransfer.effectAllowed='move'};b.ondragend=()=>{draggedPerson=null;root.querySelectorAll('.drop-target').forEach(e=>e.classList.remove('drop-target'))};
  });
  $('#character-list').querySelectorAll('[data-folder]').forEach(el=>{
   el.ondragover=e=>{if(draggedPerson===null)return;e.preventDefault();e.stopPropagation();el.classList.add('drop-target')};el.ondragleave=e=>{if(!el.contains(e.relatedTarget))el.classList.remove('drop-target')};el.ondrop=e=>{if(draggedPerson===null)return;e.preventDefault();e.stopPropagation();movePerson(draggedPerson,el.dataset.folder);draggedPerson=null};
   if(el.tagName==='DETAILS'){
    const f=folders.folders.find(f=>f.id===el.dataset.folder);el.querySelector('summary').onclick=()=>{activeFolder=f.id};
    el.ontoggle=()=>{if(!el.isConnected||q||f.open===el.open)return;f.open=el.open;saveFolders()};
    el.querySelector('summary').oncontextmenu=e=>{activeFolder=f.id;menu(e,[['在此新建角色',newCharacter],['重命名文件夹',()=>editFolder(f)],['删除文件夹（角色移到未分组）',()=>{folders=readPilotFolders();folders.folders=folders.folders.filter(x=>x.id!==f.id);for(const id in folders.assignment)if(folders.assignment[id]===f.id)delete folders.assignment[id];activeFolder='';saveFolders()}]])};
   }
  });
 }
 function drawSkills(){
  const c=current();if(!c)return;
  const q=$('#skill-search').value.trim().toLowerCase(),learned=$('#skills-learned').checked;
  const levels=new Map(c.skills.map(s=>[s.skillTypeId,s.level]));
  const rows=skills.filter(t=>(!q||matchesName(t,q))&&(!learned||q||(levels.get(t.id)||0)>0));
  $('#character-skill-count').textContent=c.skills.filter(s=>s.level>0).length+' 项已学技能';
  const skillRow=t=>{const level=levels.get(t.id)||0;return '<div class="character-skill"><span>'+esc(t.name)+'<small>'+esc(group(t))+'</small></span><div class="skill-level-boxes" role="group" aria-label="'+esc(t.name)+'等级" data-skill="'+t.id+'" data-level="'+level+'">'+[1,2,3,4,5].map(n=>'<button type="button" class="skill-level-box '+(n<=level?'lit':'')+'" data-level="'+n+'" aria-label="'+esc(t.name)+' '+n+' 级" aria-pressed="'+(n<=level)+'" title="'+n+' 级 · 再次点击当前等级可清零" '+(!draft?'disabled':'')+'></button>').join('')+'</div></div>'};
  $('#character-skills').innerHTML=[...new Set(rows.map(group))].map(g=>{const entries=rows.filter(t=>group(t)===g);return '<details class="character-skill-group" data-group="'+esc(g)+'" '+((q||skillOpen.has(g))?'open':'')+'><summary><span>'+esc(g)+'</span><small>'+entries.length+' 项</small></summary>'+entries.map(skillRow).join('')+'</details>'}).join('')||'<p class="pilot-empty">暂无已学技能。取消“仅已学习”或搜索技能以添加。</p>';
  $('#character-skills').querySelectorAll('details').forEach(el=>el.ontoggle=()=>{if(!el.isConnected||q )return;if(el.open)skillOpen.add(el.dataset.group);else skillOpen.delete(el.dataset.group)});
  $('#character-skills').querySelectorAll('[data-skill]').forEach(el=>el.querySelectorAll('button').forEach(button=>button.onclick=()=>{
   if(!draft)return;const id=Number(el.dataset.skill),n=Number(button.dataset.level),level=Number(el.dataset.level)===n?0:n;
   draft.skills=draft.skills.filter(s=>s.skillTypeId!==id);if(level)draft.skills.push({skillTypeId:id,level});el.dataset.level=level;
   el.querySelectorAll('button').forEach(b=>{const lit=Number(b.dataset.level)<=level;b.classList.toggle('lit',lit);b.setAttribute('aria-pressed',String(lit))});
   markDirty();$('#character-skill-count').textContent=draft.skills.length+' 项已学技能';
  }));
 }
 function markDirty(){dirty=true;cacheDraft();status('有未保存的修改');}
 function drawDetail(){
  const c=current();if(!c){$('#character-detail').innerHTML='<div class="character-empty">选择一个角色查看技能，或新建自定义角色。</div>';return}
  $('#character-detail').innerHTML='<div class="character-skill-toolbar"><b id="character-skill-count"></b>'+(draft?'<button id="skills-zero">全部未学习</button><button id="skills-five">全部 V</button>':'')+'</div><div class="character-skill-filters"><input id="skill-search" aria-label="搜索技能" placeholder="搜索技能"><label><input id="skills-learned" type="checkbox" '+(!draft?'checked':'')+'>仅已学习</label></div><div id="character-skills"></div>'+(draft?'<div class="character-edit-footer"><span>正在编辑 · '+esc(c.name)+'</span><button id="character-discard">结束编辑</button><button id="character-save">保存修改</button></div>':'');
  $('#skill-search').oninput=drawSkills;$('#skills-learned').onchange=drawSkills;
  if(draft){$('#skills-zero').onclick=()=>{draft.skills=[];markDirty();drawSkills()};$('#skills-five').onclick=()=>{draft.skills=skills.map(t=>({skillTypeId:t.id,level:5}));markDirty();drawSkills()};$('#character-discard').onclick=async()=>{if(!await guard())return;draft=null;drawDetail();status('')};$('#character-save').onclick=async()=>{const button=$('#character-save');button.disabled=true;try{const c=await api('character',draft);people=people.filter(p=>p.id!==c.id).concat(c);if(!draft.id&&activeFolder)movePerson(c.id,activeFolder);selected=c;draft=null;dirty=false;cacheDraft();drawList();drawDetail();status('角色已保存')}catch(e){status(e.message)}finally{if(button.isConnected)button.disabled=false}}}

  drawSkills();
 }
 async function login(){if(!await guard())return;try{const result=await api('eve/login',{});dirty=false;location.assign(result.url)}catch(e){status(e.message)}}
 $('#character-add').onclick=e=>menu(e,addEntries());
 $('#character-list').oncontextmenu=e=>{if(e.target.closest('[data-character],summary'))return;menu(e,addEntries())};
 onPilotFoldersChanged(()=>{folders=readPilotFolders();if(!root.hidden)drawList()});
 document.addEventListener('pointerdown',e=>{if(!contextMenu.contains(e.target))contextMenu.hidden=true});
 document.addEventListener('keydown',e=>{if(e.key==='Escape')contextMenu.hidden=true});
 $('#character-search').oninput=drawList;
 document.querySelector('#nav-library').addEventListener('click',async e=>{if(root.hidden)return;e.preventDefault();if(await guard()){dirty=false;if(onReturn)onReturn();else location.hash='library'}});

 return {async show(){root.hidden=false;if(loaded){drawList();drawDetail();return}const token=++generation;status('正在读取角色…');try{const list=await api('characters');if(token!==generation)return;people=list;loaded=true;const params=new URLSearchParams(location.hash.split('?')[1]||'');selected=people.find(c=>c.id===params.get('imported'))||people[0]||null;status(params.has('authError')?'官网授权未完成或技能读取失败，请重新授权。':params.has('imported')?'官网角色技能已导入。':'');try{const cached=JSON.parse(sessionStorage.getItem('fitlab-character-draft')||'null');if(cached){draft=cached;dirty=true;selected=people.find(c=>c.id===cached.id)||null;status('已恢复未保存的角色修改')}}catch{}drawList();drawDetail()}catch(e){status('读取失败：'+e.message)}},hide(){root.hidden=true;contextMenu.hidden=true;loaded=false;generation++;if(!dirty){draft=null;selected=null}}};
}
