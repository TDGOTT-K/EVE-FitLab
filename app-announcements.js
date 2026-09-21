const FEED='https://imfishman.com/announcements/feed.json';
const READ_KEY='fitlab.announcements.read.v1';
const VERSION=/^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$/;
function compare(a,b){const x=a.split('-')[0].split('.').map(Number),y=b.split('-')[0].split('.').map(Number);for(let i=0;i<3;i++)if(x[i]!==y[i])return x[i]-y[i];return a.includes('-')===b.includes('-')?0:a.includes('-')?-1:1;}
export function selectAnnouncements(feed,version,now=Date.now()){
 if(feed?.schemaVersion!==1||!Array.isArray(feed.announcements)||feed.announcements.length>100)throw Error('公告格式不受支持');
 const ids=new Set();return feed.announcements.filter(a=>{
  if(!a||typeof a.id!=='string'||!a.id||a.id.length>100||ids.has(a.id))return false;ids.add(a.id);
  if(!['info','update'].includes(a.kind)||typeof a.title!=='string'||!a.title||a.title.length>160||typeof a.body!=='string'||a.body.length>16000)return false;
  if(!Number.isFinite(Date.parse(a.publishedAt))||Date.parse(a.publishedAt)>now)return false;
  if(a.expiresAt&&(!Number.isFinite(Date.parse(a.expiresAt))||Date.parse(a.expiresAt)<=now))return false;
  if(a.minVersion&&(!VERSION.test(a.minVersion)||!VERSION.test(version)||compare(version,a.minVersion)<0))return false;
  if(a.maxVersion&&(!VERSION.test(a.maxVersion)||!VERSION.test(version)||compare(version,a.maxVersion)>0))return false;
  if(a.kind==='update'&&(!VERSION.test(a.version)||!VERSION.test(version)||compare(version,a.version)>=0))return false;
  return true;
 }).sort((a,b)=>Date.parse(b.publishedAt)-Date.parse(a.publishedAt));
}
export function installAnnouncements({openUpdates}){
 let items=[],error='',loading=null,dialog=null,settingsHost=null;
 let read=[];try{const saved=JSON.parse(localStorage.getItem(READ_KEY)||'[]');if(Array.isArray(saved))read=saved.filter(x=>typeof x==='string').slice(-300);}catch{}
 const style=document.createElement('style');style.textContent=`
 .announcement-dialog{width:min(580px,calc(100vw - 32px));max-height:80vh;padding:0;border:1px solid var(--line,#40505f);border-radius:14px;background:var(--panel,#17202b);color:var(--text,#e5edf5);box-shadow:0 24px 90px #0008}
 .announcement-dialog::backdrop{background:#0008;backdrop-filter:blur(4px)}
 .announcement-head{display:flex;align-items:center;justify-content:space-between;padding:18px 24px;border-bottom:1px solid var(--line,#40505f)}
 .announcement-head h2{font-size:18px;margin:0}.announcement-body{padding:0 24px;overflow:auto;max-height:52vh}
 .announcement-card{padding:20px 0;border-bottom:1px solid var(--line,#40505f)}.announcement-card h3{margin:0 0 8px;font-size:20px}.announcement-card time{opacity:.6;font-size:12px}
 .announcement-copy{white-space:pre-wrap;overflow-wrap:anywhere;line-height:1.8}.announcement-actions{display:flex;justify-content:flex-end;padding:16px 24px;gap:12px}
 .announcement-update{margin-top:8px}.announcement-status{opacity:.7;font-size:13px}
 `;document.head.append(style);
 function markRead(rows){read=[...new Set([...read,...rows.map(a=>a.id)])].slice(-300);try{localStorage.setItem(READ_KEY,JSON.stringify(read));}catch{}renderSettings();}
 function renderSettings(){if(settingsHost?.isConnected)settingsHost.querySelector('[data-announcement-status]').textContent=loading?'正在获取官网公告…':error||`共 ${items.length} 条公告，${items.filter(a=>!read.includes(a.id)).length} 条未读`;}
 async function refresh(){
  if(loading)return loading;
  loading=(async()=>{try{
   const [response,versionResponse]=await Promise.all([fetch(FEED,{cache:'no-store',credentials:'omit',signal:AbortSignal.timeout(8000)}),fetch('/api/version',{signal:AbortSignal.timeout(8000)})]);
   if(!response.ok||!versionResponse.ok)throw Error('官网公告暂不可用，请稍后重试。');
   const text=await response.text();if(text.length>200000)throw Error('公告内容过大');
   items=selectAnnouncements(JSON.parse(text),(await versionResponse.json()).version);error='';
  }catch{error='官网公告暂不可用，请稍后重试。';}finally{loading=null;renderSettings();}})();renderSettings();return loading;
 }
 function show(rows=items){
  if(dialog?.open)return;
  dialog=document.createElement('dialog');dialog.className='announcement-dialog';dialog.setAttribute('aria-labelledby','announcement-title');
  dialog.innerHTML='<header class="announcement-head"><h2 id="announcement-title">FitLab 公告</h2><button type="button" aria-label="关闭公告">×</button></header><div class="announcement-body"></div><footer class="announcement-actions"><button type="button">我知道了</button></footer>';
  const body=dialog.querySelector('.announcement-body');
  if(!rows.length){const p=document.createElement('p');p.textContent=error||'暂无公告';body.append(p);}
  for(const item of rows){
   const card=document.createElement('article');card.className='announcement-card';
   const title=document.createElement('h3');title.textContent=item.title;
   const date=document.createElement('time');date.dateTime=item.publishedAt;date.textContent=new Date(item.publishedAt).toLocaleDateString();
   const copy=document.createElement('p');copy.className='announcement-copy';copy.textContent=item.body;card.append(title,date,copy);
   if(item.kind==='update'){const button=document.createElement('button');button.type='button';button.className='announcement-update';button.textContent='立即更新';button.onclick=()=>{dialog.close();openUpdates();};card.append(button);}
   body.append(card);
  }
  dialog.querySelector('.announcement-head button').onclick=()=>dialog.close();dialog.querySelector('.announcement-actions button').onclick=()=>dialog.close();
  dialog.addEventListener('close',()=>{markRead(rows);dialog.remove();},{once:true});document.body.append(dialog);dialog.showModal();
 }
 // Never interrupt an existing modal or an active edit with a delayed startup response.
 const started=Date.now();refresh().then(()=>{const unread=items.filter(a=>a.startup===true&&!read.includes(a.id));if(unread.length&&Date.now()-started<10000&&!document.querySelector('dialog[open]')&&location.hash==='#library')show(unread);});
 return {mount(parent){
  settingsHost=document.createElement('section');settingsHost.className='storage-settings';settingsHost.innerHTML='<h3>公告</h3><p class="announcement-status" data-announcement-status role="status"></p><div class="storage-actions"><button type="button">查看公告</button></div>';
  settingsHost.querySelector('button').onclick=async()=>{await refresh();show();};parent.append(settingsHost);renderSettings();
 }};
}
