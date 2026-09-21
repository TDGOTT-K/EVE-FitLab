import {updateDownload,openDownloadCenter} from './download-center.js';
export function installAppUpdates({prepare}){
 const api=window.fitlabDesktop?.updates;
 let state={phase:'idle'},host=null,starting=false;
 const style=document.createElement('style');style.textContent='#app-settings[data-update-available="true"]::after{content:" ●";color:var(--accent)}';document.head.append(style);
 api?.onPrepare(prepare);
 const labels={cancelled:'已取消下载',idle:'尚未检查更新',checking:'正在检查官网更新…',current:'当前已是最新版本',available:'有新版本可下载',downloading:'正在下载更新…',downloaded:'下载完成，可保存并重启更新',preparing:'正在保存与备份，请勿关闭应用…',installing:'正在退出并安装更新…',blocked:'需要手动升级',error:'更新未完成，可重试'};
 function draw(){
  if(api)updateDownload(state,{start:()=>controls.start(),cancel:()=>api.cancel(),install:async()=>{try{receive(await api.install());}catch(e){state={...state,error:e.message};draw();}}});
  if(api)document.body.inert=['preparing','installing'].includes(state.phase);
  const entry=document.querySelector('#app-settings');if(entry){const available=['available','downloaded'].includes(state.phase);entry.dataset.updateAvailable=String(available);if(available)entry.setAttribute('aria-label','设置，有应用更新 v'+state.version);else entry.removeAttribute('aria-label');}
  if(!host?.isConnected)return;
  host.querySelector('[data-update-version]').textContent=(state.currentVersion?'v'+state.currentVersion:'正在读取版本…')+(state.version?' → v'+state.version:'');
  host.querySelector('[data-update-status]').textContent=state.error||(!api?'浏览器仅展示更新界面，请在桌面应用中检查和安装更新。':labels[state.phase]||state.phase);
  const notes=host.querySelector('[data-update-notes]');notes.replaceChildren();for(const note of state.notes||[]){const li=document.createElement('li');li.textContent=note;notes.append(li);}
  const busy=['checking','downloading','preparing','installing'].includes(state.phase);
  host.querySelector('[data-update-check]').disabled=!api||busy;
 }
 function receive(next){state=next;draw();}
 if(api){api.subscribe(receive);api.state().then(receive).catch(()=>{});}
 else fetch('/api/version').then(response=>{if(!response.ok)throw Error('无法读取当前版本');return response.json();}).then(info=>{state={...state,currentVersion:info.version};draw();}).catch(()=>{state={...state,error:'无法读取当前版本；浏览器仅展示更新界面。'};draw();});
 const controls={async start(){
  host?.scrollIntoView({block:'center',behavior:'smooth'});host?.querySelector('[data-update-check]')?.focus();
  if(!api||starting)return;starting=true;
  try{
   state=await api.state();const deadline=Date.now()+30000;
   while(state.phase==='checking'&&Date.now()<deadline){await new Promise(resolve=>setTimeout(resolve,250));state=await api.state();}
   if(['checking','downloading','downloaded','preparing','installing'].includes(state.phase)){draw();return;}
   if(state.phase!=='available')receive(await api.check());
   if(state.phase==='available'){openDownloadCenter();receive(await api.download());}
  }catch(e){state={...state,error:e.message};draw();}finally{starting=false;}
 },mount(parent){
  host=document.createElement('section');host.className='storage-settings';host.id='app-update-settings';
  host.innerHTML='<h3>应用更新</h3><p data-update-version translate="no"></p><p data-update-status role="status"></p><ul data-update-notes></ul><div class="storage-actions"><button type="button" data-update-check>检查更新</button><button type="button" data-update-center>前往下载中心</button></div><p class="profile-note">从 imfishman.com 检查版本，安装包由 GitHub 提供。下载后可稍后安装；更新前会备份数据。</p>';
  parent.append(host);host.querySelector('[data-update-center]').onclick=openDownloadCenter;host.querySelector('[data-update-check]').onclick=async()=>{if(!api)return;try{receive(await api.check());}catch(e){state={...state,error:e.message};draw();}};draw();
 }};return controls;
}
