export const parentFolder=path=>path.includes('/')?path.slice(0,path.lastIndexOf('/')):'';
export const folderName=path=>path.split('/').at(-1);
export function normalizeLayout(layout,plans,extra=[]){
 const folders=new Set([...(layout.folders||[]),...extra,...plans.map(p=>p.folder).filter(Boolean)]);
 for(const path of [...folders]){let p=parentFolder(path);while(p){folders.add(p);p=parentFolder(p)}}
 const keys=[...[...folders].map(f=>'f:'+f),...plans.map(p=>'p:'+p.id)],valid=new Set(keys);
 return {...layout,folders:[...folders],order:[...new Set([...(layout.order||[]).filter(k=>valid.has(k)),...keys])]};
}
export function relocateLibrary(layout,plans,source,target,mode){
 const next=normalizeLayout(structuredClone(layout),plans),copies=structuredClone(plans);
 const folder=source.startsWith('f:'),old=source.slice(2),keys=new Set(next.order);
 if(!keys.has(source)||source===target)throw Error('不能移动到自身');
 const targetFolder=target?.startsWith('f:')?target.slice(2):target?.startsWith('p:')?copies.find(p=>p.id===target.slice(2))?.folder||'':'';
 const destination=mode==='inside'?targetFolder:target?.startsWith('f:')?parentFolder(targetFolder):targetFolder;
 if(target&&target!=='f:'&&!keys.has(target))throw Error('目标已不存在');
 if(folder&&(destination===old||destination.startsWith(old+'/')))throw Error('不能把分组移入自身或子分组');
 let moved=source;
 if(folder){
  const path=(destination?destination+'/':'')+folderName(old);
  if(path!==old&&next.folders.includes(path))throw Error('目标位置已有同名分组');
  const rewrite=f=>f===old?path:f.startsWith(old+'/')?path+f.slice(old.length):f;
  next.folders=next.folders.map(rewrite);copies.forEach(p=>p.folder=rewrite(p.folder||''));next.order=next.order.map(k=>k.startsWith('f:')?'f:'+rewrite(k.slice(2)):k);moved='f:'+path;
  if(next.folders.some(f=>f.split('/').length>16||f.length>1000))throw Error('分组层级最多为 16 层');
 }else copies.find(p=>p.id===old).folder=destination;
 next.order=next.order.filter(k=>k!==moved);
 const index=target?next.order.indexOf(target):-1;
 if(mode==='inside'||index<0)next.order.push(moved);else next.order.splice(index+(mode==='after'?1:0),0,moved);
 return {layout:next,plans:copies,destination,moved};
}
export function installLibraryDrag(tree,blank,{canStart,validate,onDrop,onError}){
 let source=null,hover=null,timer=null,drop=null,cancelled=false,suppressContextUntil=0,press=null;
 const holdCursor=document.createElement('div');holdCursor.className='plan-hold-cursor';holdCursor.hidden=true;holdCursor.setAttribute('aria-hidden','true');holdCursor.innerHTML='<svg viewBox="0 0 24 24"><circle class="hold-track" cx="12" cy="12" r="9"/><circle class="hold-progress" cx="12" cy="12" r="9"/></svg>';document.body.append(holdCursor);
 let holdFrame=0;
 const positionHold=e=>{holdCursor.style.left=(e.clientX+14)+'px';holdCursor.style.top=(e.clientY+14)+'px'};
 function clearHold(){cancelAnimationFrame(holdFrame);holdFrame=0;holdCursor.hidden=true;document.body.classList.remove('plan-drag-ready');}
 function releasePress(){press=null;clearHold()}
 function tickHold(){if(!press)return;const progress=Math.min(1,(performance.now()-press.at)/200);holdCursor.querySelector('.hold-progress').style.strokeDashoffset=String(56.55*(1-progress));holdCursor.classList.toggle('ready',progress===1);if(progress===1){document.body.classList.add('plan-drag-ready');return;}holdFrame=requestAnimationFrame(tickHold);}
 tree.addEventListener('pointerdown',e=>{
  releasePress();const row=e.target.closest('[data-library-key]');
  if(e.button!==0||!row||row.dataset.libraryKey==='f:'||!canStart())return;
  press={key:row.dataset.libraryKey,at:performance.now()};positionHold(e);holdCursor.hidden=false;holdCursor.classList.remove('ready');tickHold();
 },true);
 document.addEventListener('pointermove',e=>{if(press&&!source)positionHold(e)},true);
 for(const type of ['pointerup','pointercancel'])document.addEventListener(type,releasePress,true);
 window.addEventListener('blur',releasePress);
 const marker=document.createElement('div');marker.className='plan-drop-marker';marker.hidden=true;document.body.append(marker);
 function clear(){clearTimeout(timer);timer=null;hover=null;drop=null;marker.hidden=true;tree.querySelectorAll('.library-drop-inside').forEach(el=>el.classList.remove('library-drop-inside'));}
 function cancelRightDrag(e){
  if(!source&&!press)return;releasePress();source=null;cancelled=true;suppressContextUntil=Date.now()+700;clear();if(e.cancelable)e.preventDefault();e.stopImmediatePropagation();
 }
 for(const type of ['pointerdown','mousedown'])document.addEventListener(type,e=>{if(e.button===2)cancelRightDrag(e)},true);
 document.addEventListener('contextmenu',e=>{
  if(source){cancelRightDrag(e);suppressContextUntil=0;return;}
  if(cancelled||Date.now()<suppressContextUntil){e.preventDefault();e.stopImmediatePropagation();suppressContextUntil=0;}
 },true);
 for(const type of ['drag','dragover'])document.addEventListener(type,e=>{if(source&&(e.buttons&2))cancelRightDrag(e)},true);
 function show(el,target,mode){
  tree.querySelectorAll('.library-drop-inside').forEach(el=>el.classList.remove('library-drop-inside'));drop=null;
  try{validate(source,target,mode)}catch{marker.hidden=true;return;}
  drop={target,mode};const r=el.getBoundingClientRect();marker.hidden=false;
  marker.classList.toggle('inside',mode==='inside');marker.style.left=r.left+'px';marker.style.width=r.width+'px';marker.style.top=(mode==='after'?r.bottom:r.top)+'px';
  marker.textContent=mode==='inside'?(target?'移入此分组':'移至最外层'):'调整顺序';
  if(mode==='inside')el.classList.add('library-drop-inside');
 }
 const over=e=>{
  if(!source)return;e.preventDefault();e.stopPropagation();e.dataTransfer.dropEffect='move';
  const el=e.target.closest('[data-library-key]')||(e.currentTarget===blank?blank:e.target===tree?tree:null);if(!el){clear();return}
  const target=el.dataset.libraryKey||null,r=el.getBoundingClientRect(),ratio=(e.clientY-r.top)/r.height;
  if(!target){clear();show(el,null,'inside');return}
  const center=target.startsWith('f:')&&ratio>.28&&ratio<.72&&e.clientX>r.left+24;
  const mode=ratio<.5?'before':'after';
  if(hover?.el!==el||hover?.center!==center){clear();hover={el,center};show(el,target,mode);if(center)timer=setTimeout(()=>{show(el,target,'inside');if(drop&&el.tagName==='SUMMARY')el.parentElement.open=true;},650);}
  else if(!center)show(el,target,mode);
  const bounds=tree.getBoundingClientRect();if(e.clientY<bounds.top+24)tree.scrollTop-=8;else if(e.clientY>bounds.bottom-24)tree.scrollTop+=8;
 };
 tree.ondragstart=e=>{const row=e.target.closest('[data-library-key]');if(!row||row.dataset.libraryKey==='f:'||press?.key!==row.dataset.libraryKey||performance.now()-press.at<200||!canStart()){e.preventDefault();return}clearHold();cancelled=false;suppressContextUntil=0;source=row.dataset.libraryKey;e.dataTransfer.setData('application/x-fitlab-library',source);e.dataTransfer.effectAllowed='move';};
 tree.ondragend=()=>{source=null;clear();if(cancelled)suppressContextUntil=Date.now()+700;cancelled=false;};
 for(const zone of [tree,blank]){zone.ondragover=over;zone.ondragleave=e=>{if(!zone.contains(e.relatedTarget))clear()};zone.ondrop=e=>{if(!source)return;e.preventDefault();e.stopPropagation();const intent=drop,s=source;source=null;clear();if(intent)Promise.resolve(onDrop(s,intent.target,intent.mode)).catch(onError)};}
 document.addEventListener('keydown',e=>{if(e.key==='Escape'){releasePress();source=null;clear()}});
}
