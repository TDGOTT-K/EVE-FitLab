
export function createLibraryTree(root,catalog,onChange,onHullMenu,{picker:pickerMode=false,initialPath=[]}={}){
 // Navigation labels mapped to SDE faction identities, not fitting rules.
 const factions={'艾玛':500003,'加达里':500001,'盖伦特':500004,'米玛塔尔':500002};
 const factionIcon=name=>factions[name]?'<img class="faction-tree-icon" src="https://images.evetech.net/corporations/'+factions[name]+'/logo?size=64" alt="" loading="lazy">':'';
 const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 let fits=[],filter='',selectedTags=new Set(),expanded=new Set(['舰船']);
 if(pickerMode){expanded=new Set(initialPath.length?initialPath.map((_,i)=>initialPath.slice(0,i+1).join(' › ')):['舰船']);filter=initialPath.length?'g:'+JSON.stringify(initialPath):'';}
 else try{expanded=new Set(JSON.parse(localStorage.getItem('fitlab-library-expanded')||'["舰船"]'))}catch{}
 const hulls=catalog.filter(t=>t.kind==='ship'),index=new Map(hulls.map(t=>[t.id,t])),tree={children:new Map(),ships:[],path:[]};
 for(const ship of hulls){let n=tree;for(const label of ship.path){if(!n.children.has(label))n.children.set(label,{name:label,path:[...n.path,label],children:new Map(),ships:[]});n=n.children.get(label)}n.ships.push(ship)}
 const inPath=(f,path)=>path.every((p,i)=>index.get(f.shipId)?.path[i]===p);
 const hullMatch=f=>!filter||filter.startsWith('h:')?(!filter||String(f.shipId)===filter.slice(2)):inPath(f,JSON.parse(filter.slice(2)));
 const match=f=>hullMatch(f)&&[...selectedTags].every(t=>t==='__untagged__'?!(f.tags||[]).length:(f.tags||[]).includes(t));
 function closeTagPicker(restore=false){const picker=root.querySelector('.library-tag-picker');if(picker)picker.hidden=true;const add=root.querySelector('.add-filter-tag');add?.setAttribute('aria-expanded','false');if(restore)add?.focus()}
 if(!pickerMode){document.addEventListener('pointerdown',e=>{if(!e.target.closest('.library-tag-filter'))closeTagPicker()});
 window.addEventListener('hashchange',()=>closeTagPicker());}
 function drawTags(){
  if(pickerMode)return;
  const scoped=fits.filter(hullMatch),tags=[...new Set(scoped.flatMap(f=>f.tags||[]))].sort((a,b)=>a.localeCompare(b,'zh'));
  const choices=[...tags.map(t=>[t,t]),...(scoped.some(f=>!(f.tags||[]).length)?[['__untagged__','无标签']]:[])];
  for(const t of selectedTags)if(!choices.some(([key])=>key===t))selectedTags.delete(t);
  const area=root.querySelector('.library-tag-filter');
  area.innerHTML='<div class="library-tag-caption">标签筛选</div><div class="library-selected-tags">'+[...selectedTags].map(t=>'<span class="filter-tag"><span>'+esc(t==='__untagged__'?'无标签':t)+'</span><button data-remove-tag="'+esc(t)+'" aria-label="移除筛选标签：'+esc(t==='__untagged__'?'无标签':t)+'">×</button></span>').join('')+(!selectedTags.size?'<span class="filter-tag-placeholder">不限标签</span>':'')+'<button class="add-fit-tag add-filter-tag" aria-label="添加筛选标签" aria-haspopup="dialog" aria-expanded="false">＋</button></div><div class="library-tag-picker" role="dialog" aria-label="添加筛选标签" hidden><input aria-label="搜索筛选标签" placeholder="搜索标签…"><div class="library-tag-choices"></div></div>';
  area.querySelectorAll('[data-remove-tag]').forEach(b=>b.onclick=()=>{selectedTags.delete(b.dataset.removeTag);drawTags();onChange();root.querySelector('.add-filter-tag').focus()});
  const picker=area.querySelector('.library-tag-picker'),input=picker.querySelector('input'),list=picker.querySelector('.library-tag-choices');
  function suggestions(){const q=input.value.trim().toLowerCase(),rows=choices.filter(([t,label])=>!selectedTags.has(t)&&label.toLowerCase().includes(q));
   list.innerHTML=rows.map(([t,label])=>'<button data-add-tag="'+esc(t)+'"><span>'+esc(label)+'</span><small>'+scoped.filter(f=>t==='__untagged__'?!(f.tags||[]).length:(f.tags||[]).includes(t)).length+'</small></button>').join('')||'<div class="tag-picker-empty">'+(q?'没有匹配标签':'没有更多可用标签')+'</div>';
   list.querySelectorAll('[data-add-tag]').forEach(b=>b.onclick=()=>{const t=b.dataset.addTag;if(t==='__untagged__')selectedTags.clear();else selectedTags.delete('__untagged__');selectedTags.add(t);drawTags();onChange();root.querySelector('.add-filter-tag').focus()});
  }
  area.querySelector('.add-filter-tag').onclick=e=>{const opening=picker.hidden;picker.hidden=!opening;e.currentTarget.setAttribute('aria-expanded',String(opening));if(opening){input.value='';suggestions();input.focus()}};
  input.oninput=suggestions;input.onkeydown=e=>{if(e.key==='ArrowDown'){e.preventDefault();list.querySelector('button')?.focus()}if(e.key==='Enter'){e.preventDefault();list.querySelector('button')?.click()}};
  picker.onkeydown=e=>{if(e.key==='Escape'){e.preventDefault();e.stopPropagation();closeTagPicker(true)}if(e.target.matches('button')&&['ArrowDown','ArrowUp'].includes(e.key)){e.preventDefault();const buttons=[...list.querySelectorAll('button')],i=buttons.indexOf(e.target);buttons[(i+(e.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length]?.focus()}};
 }

 const button=(key,label,count,icon='')=>'<button type="button" data-library-filter="'+esc(key)+'" class="'+(key===filter?'active':'')+'">'+icon+'<span>'+esc(label)+'</span>'+(pickerMode?'':'<small>'+count+'</small>')+'</button>';
 function branch(n){
  return [...n.children.values()].sort((a,b)=>(a.name==='未列入市场')-(b.name==='未列入市场')||a.name.localeCompare(b.name,'zh')).map(child=>{
   const key=JSON.stringify(child.path);return '<details data-branch="'+esc(key)+'" '+(expanded.has(child.path.join(' › '))?'open':'')+'><summary>'+button('g:'+key,child.name,fits.filter(f=>inPath(f,child.path)).length,factionIcon(child.name))+'</summary><div class="library-tree-children">'+branch(child)+'</div></details>';
  }).join('')+n.ships.sort((a,b)=>a.name.localeCompare(b.name,'zh')).map(ship=>button('h:'+ship.id,ship.name,fits.filter(f=>f.shipId===ship.id).length,'<img src="https://images.evetech.net/types/'+ship.id+'/icon?size=32" loading="lazy" alt="">')).join('');
 }
 function draw(){
  const scroll=root.querySelector('.library-tree-scroll')?.scrollTop||0;
  const search=pickerMode?null:document.querySelector('#library-search');
  root.innerHTML='<div class="library-nav-heading">舰船</div><div class="library-tag-filter"></div><div class="library-tree-scroll">'+button('','全部装配',fits.length)+branch(tree)+'</div>';
  if(search){const searchWrap=document.createElement('div');searchWrap.className='library-nav-search';searchWrap.append(search);root.querySelector('.library-nav-heading').after(searchWrap);}drawTags();
  if(pickerMode){root.querySelector('.library-tag-filter').remove();root.querySelector('[data-library-filter=""]').remove();}
  root.querySelector('.library-tree-scroll').scrollTop=scroll;

  root.querySelectorAll('[data-library-filter]').forEach(b=>b.onclick=e=>{filter=b.dataset.libraryFilter;if(filter.startsWith('g:')){e.preventDefault();const d=b.closest('details');d.open=!d.open}root.querySelectorAll('[data-library-filter]').forEach(x=>x.classList.toggle('active',x===b));drawTags();onChange()});
  root.querySelectorAll('[data-library-filter^="h:"]').forEach(b=>{
   const open=e=>{if(!onHullMenu)return;e.preventDefault();e.stopPropagation();onHullMenu(e,index.get(Number(b.dataset.libraryFilter.slice(2))),b)};
   b.oncontextmenu=open;
   b.onkeydown=e=>{if(e.key==='ContextMenu'||e.shiftKey&&e.key==='F10')open(e)};
  });
  root.querySelectorAll('details').forEach(d=>d.ontoggle=()=>{if(!d.isConnected)return;const key=JSON.parse(d.dataset.branch).join(' › ');if(d.open)expanded.add(key);else expanded.delete(key);if(!pickerMode)try{localStorage.setItem('fitlab-library-expanded',JSON.stringify([...expanded]))}catch{}});
 }
 return {matches:match,selectedPath(){return filter.startsWith('g:')?JSON.parse(filter.slice(2)):[]},explicitHull(){return filter.startsWith('h:')?index.get(Number(filter.slice(2))):null},scrollToSelection(){const b=[...root.querySelectorAll('[data-library-filter]')].find(b=>b.dataset.libraryFilter===filter);if(b){const area=root.querySelector('.library-tree-scroll');area.scrollTop+=b.getBoundingClientRect().top-area.getBoundingClientRect().top;}},selectedHull(){

  if(filter.startsWith('h:'))return index.get(Number(filter.slice(2)))||null;
  if(filter.startsWith('g:')){const path=JSON.parse(filter.slice(2)),matches=hulls.filter(h=>path.every((p,i)=>h.path[i]===p));return matches.length===1?matches[0]:null}
  return null;
 },revealHull(id){filter='h:'+id;selectedTags.clear();draw();onChange()},update(next){fits=next;draw()}};
}
