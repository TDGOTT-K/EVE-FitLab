const KEY='fitlab.downloads.v1',ACTIVE=new Set(['downloading','paused','interrupted','checking','available','preparing','installing','downloaded']);
let rows=[],entry,popup,full,filter='all',query='',updateActions={},desktop=window.fitlabDesktop?.downloads;
const retries=new Map();
try{rows=JSON.parse(localStorage.getItem(KEY)||'[]').filter(r=>r.kind==='export').slice(0,100);}catch{}
const labels={handed:'已交给浏览器保存',completed:'已完成',downloading:'下载中',paused:'已暂停',interrupted:'连接中断',cancelled:'已取消',error:'失败',checking:'检查更新中',available:'可下载',downloaded:'等待安装',preparing:'保存与备份中',installing:'安装中',blocked:'需要手动升级'};
const bytes=n=>Number.isFinite(n)?n>=1048576?(n/1048576).toFixed(1)+' MB':(n/1024).toFixed(1)+' KB':'大小未知';
function persist(){try{localStorage.setItem(KEY,JSON.stringify(rows.filter(r=>r.kind==='export').slice(0,100)));}catch{}}
function upsert(row){rows=[row,...rows.filter(r=>r.id!==row.id)].sort((a,b)=>b.time-a.time);persist();render();}
function button(label,action){const b=document.createElement('button');b.type='button';b.textContent=label;b.onclick=async()=>{b.disabled=true;try{await action();}catch(e){b.closest('article')?.querySelector('.download-detail')?.replaceChildren(document.createTextNode(e.message));}finally{b.disabled=false;}};return b;}
export function saveDownload(blob,name){
 const run=()=>{const url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download=name;document.body.append(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),60000);};
 run();if(!desktop){const row={id:crypto.randomUUID(),name,kind:'export',status:'handed',size:blob.size,time:Date.now()};retries.set(row.id,()=>saveDownload(blob,name));if(retries.size>20)retries.delete(retries.keys().next().value);upsert(row);}
}
export function updateDownload(state,actions){
 updateActions=actions;
 if(['idle','current'].includes(state.phase)){rows=rows.filter(r=>r.id!=='application-update');render();return;}
 upsert({id:'application-update',kind:'update',name:'FitLab'+(state.version?' v'+state.version:'')+' 应用更新',status:state.phase,percent:state.percent,size:state.size,received:state.transferred,speed:state.bytesPerSecond,notes:state.notes,error:state.error,time:rows.find(r=>r.id==='application-update')?.time||Date.now()});
}
function remove(row){if(row.kind==='file')return desktop.action(row.id,'remove');retries.delete(row.id);rows=rows.filter(r=>r.id!==row.id);persist();render();}
function card(row){
 const article=document.createElement('article');article.className='download-card';
 const icon=document.createElement('span');icon.className='download-file-icon';icon.textContent=row.kind==='update'?'↻':'↓';
 const content=document.createElement('div');content.className='download-content';const title=document.createElement('strong');title.textContent=row.name;
 const detail=document.createElement('p');detail.className='download-detail';detail.textContent=(labels[row.status]||row.status)+' · '+bytes(row.size)+(row.error?' · '+row.error:'');
 content.append(title,detail);
 if(row.path){const path=document.createElement('p');path.className='download-detail';path.textContent=row.path;content.append(path);}
 if(row.notes?.length){const notes=document.createElement('details'),summary=document.createElement('summary');summary.textContent='更新说明';notes.append(summary);for(const note of row.notes){const p=document.createElement('p');p.textContent=note;notes.append(p);}content.append(notes);}
 if(row.status==='downloading'){const p=document.createElement('progress');p.max=100;if(Number.isFinite(row.percent))p.value=row.percent;content.append(p);detail.textContent+=' · '+(row.percent??0).toFixed(0)+'%';}
 const actions=document.createElement('div');actions.className='download-actions';
 if(row.kind==='update'){
  if(['available','error','blocked','cancelled'].includes(row.status))actions.append(button(row.status==='available'?'下载更新':'重新检查',updateActions.start));
  if(row.status==='downloading'&&updateActions.cancel)actions.append(button('取消下载',updateActions.cancel));
  if(row.status==='downloaded')actions.append(button('保存并重启更新',updateActions.install));
 }else if(row.kind==='file'){
  if(row.status==='downloading')actions.append(button('暂停',()=>desktop.action(row.id,'pause')));
  if(['paused','interrupted'].includes(row.status)&&row.resumable)actions.append(button('继续',()=>desktop.action(row.id,'resume')));
  if(['downloading','paused','interrupted'].includes(row.status))actions.append(button('取消',()=>desktop.action(row.id,'cancel')));
  if(row.status==='completed')actions.append(button('在文件夹中显示',()=>desktop.action(row.id,'reveal')));
 }else if(retries.has(row.id))actions.append(button('再次保存',retries.get(row.id)));
 if(!ACTIVE.has(row.status)&&row.kind!=='update')actions.append(button('移除记录',()=>remove(row)));
 const time=document.createElement('small');time.textContent=new Date(row.time).toLocaleString();content.append(time,actions);article.append(icon,content);return article;
}
function renderList(target,list){target.replaceChildren();if(!list.length){const empty=document.createElement('div');empty.className='download-empty';empty.textContent=filter!=='all'||query?'没有匹配的下载任务':'暂无下载任务';target.append(empty);}else for(const row of list)target.append(card(row));}
function render(){
 const count=rows.filter(r=>['downloading','checking','preparing'].includes(r.status)).length;if(entry){entry.dataset.active=String(count>0);entry.title=count?'下载中心 · '+count+' 项进行中':'下载中心';}
 if(popup)renderList(popup.querySelector('.download-list'),rows.slice(0,4));
 if(full){renderList(full.querySelector('.download-list'),rows.filter(r=>(filter==='all'||(filter==='active'?ACTIVE.has(r.status):!ACTIVE.has(r.status)))&&r.name.toLowerCase().includes(query.toLowerCase())));full.querySelector('[data-count]').textContent=rows.length+' 项下载事务';}
}
function closeFull(){full?.remove();full=null;entry?.focus();}
export function openDownloadCenter(){
 popup?.hidePopover();if(document.querySelector('#app-update-settings')?.closest('dialog')?.open)document.querySelector('#app-update-settings').closest('dialog').close();
 if(full)return;full=document.createElement('section');full.id='download-center';full.setAttribute('aria-label','下载中心');
 full.innerHTML='<div class="download-center-head"><div><small>DOWNLOAD CENTER</small><h1>下载中心</h1><p data-count></p></div></div><div class="download-tools"><input type="search" placeholder="搜索下载名称" aria-label="搜索下载名称"><select aria-label="筛选下载"><option value="all">全部</option><option value="active">进行中 / 待处理</option><option value="finished">已结束</option></select></div><p class="download-hint">移除记录不会删除文件。浏览器下载的保存结果请在浏览器中查看。</p><div class="download-list"></div>';
 full.querySelector('.download-center-head').append(button('返回',closeFull));
 full.querySelector('.download-tools').append(button('清除已结束记录',async()=>{for(const row of [...rows])if(!ACTIVE.has(row.status)&&row.kind!=='update')await remove(row);}));
 full.querySelector('input').value=query;full.querySelector('input').oninput=e=>{query=e.target.value;render();};full.querySelector('select').value=filter;full.querySelector('select').onchange=e=>{filter=e.target.value;render();};
 document.body.append(full);render();full.querySelector('input').focus();
}
export function installDownloadCenter(){
 const css=document.createElement('link');css.rel='stylesheet';css.href='download-center.css';document.head.append(css);
 entry=document.createElement('button');entry.id='download-entry';entry.type='button';entry.setAttribute('aria-label','下载中心');entry.setAttribute('aria-haspopup','dialog');entry.setAttribute('aria-expanded','false');entry.innerHTML='<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" aria-hidden="true"><path d="M12 3v12m-5-5 5 5 5-5M4 16v5h16v-5"/></svg>';
 document.querySelector('#app-settings').before(entry);
 popup=document.createElement('section');popup.id='download-popover';popup.setAttribute('popover','auto');popup.setAttribute('role','dialog');popup.setAttribute('aria-label','最近下载');popup.innerHTML='<div class="download-popover-head"><strong>下载</strong><span>最近的下载事务</span></div><div class="download-list"></div><a class="download-center-link" href="#downloads">前往下载中心 →</a>';
 document.body.append(popup);entry.setAttribute('popovertarget',popup.id);entry.setAttribute('popovertargetaction','toggle');entry.onclick=()=>{const rect=entry.getBoundingClientRect();popup.style.right=Math.max(12,innerWidth-rect.right)+'px';popup.style.top=rect.bottom+12+'px';};popup.addEventListener('toggle',e=>entry.setAttribute('aria-expanded',String(e.newState==='open')));
 popup.querySelector('a').onclick=e=>{e.preventDefault();openDownloadCenter();};
 document.addEventListener('click',e=>{if(e.target.closest?.('header a'))closeFull();},true);
 if(desktop){const receive=list=>{rows=[...rows.filter(r=>r.kind!=='file'),...list].sort((a,b)=>b.time-a.time);render();};desktop.subscribe(receive);desktop.list().then(receive).catch(()=>{});}
 render();
}
