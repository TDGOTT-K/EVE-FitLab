const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const {execFile}=require('node:child_process');
const {promisify}=require('node:util');
const {createUpdateController}=require('./update-controller.cjs');
function installUpdates({app,ipcMain,win,apiPort,apiKey,resources}){
 const pending=new Map();const trusted=e=>e.sender===win.webContents&&e.senderFrame===win.webContents.mainFrame;
 async function local(route,body){const r=await fetch(`http://127.0.0.1:${apiPort}/api/${route}`,{method:'POST',headers:{'X-FitLab-Key':apiKey,'Content-Type':'application/json'},body:JSON.stringify(body),signal:AbortSignal.timeout(300000)});const d=await r.json();if(!r.ok)throw Error(d.error?.message||d.error||'更新准备失败');return d;}
 async function noAgent(){
  if(process.platform!=='win32')return;
  const {stdout}=await promisify(execFile)('powershell.exe',['-NoProfile','-NonInteractive','-Command',"$root=$env:FITLAB_UPDATE_RESOURCES; @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object { ($_.ExecutablePath -and $_.ExecutablePath.StartsWith((Join-Path $root 'agent'),[StringComparison]::OrdinalIgnoreCase)) -or ($_.CommandLine -and $_.CommandLine.Contains((Join-Path $root 'nengine\\agent-runtime'))) }).Count"],{env:{...process.env,FITLAB_UPDATE_RESOURCES:resources},windowsHide:true,timeout:15000});
  if(!/^\d+$/.test(stdout.trim()))throw Error('无法确认 Agent 是否退出，请稍后重试');
  if(Number(stdout)>0)throw Error('包内 Agent 仍在运行，请先断开 MCP 客户端或结束 CLI 任务，再安装更新。');
 }
 function save(){return new Promise((resolve,reject)=>{const nonce=crypto.randomUUID();const timer=setTimeout(()=>{pending.delete(nonce);reject(Error('保存确认超时，本次未安装更新'));},90000);pending.set(nonce,{resolve,reject,timer});win.webContents.send('update:prepare',nonce);});}
 ipcMain.handle('update:prepared',(event,reply)=>{if(!trusted(event))throw Error('Forbidden');const p=pending.get(reply?.nonce);if(!p)return;clearTimeout(p.timer);pending.delete(reply.nonce);if(reply.ok===true)p.resolve();else p.reject(Error(String(reply.error||'装配未能保存')));});
 const {autoUpdater}=require('electron-updater');const {net}=require('electron');
 const baseline=JSON.parse(fs.readFileSync(path.join(resources,'nengine','UI-LOCAL-BASELINE.json'),'utf8').replace(/^\uFEFF/,''));
 const controller=createUpdateController({updater:autoUpdater,currentVersion:app.getVersion(),binding:baseline,
  publicKey:fs.readFileSync(path.join(__dirname,'update-public-key.pem')),
  readManifest:async url=>{const r=await net.fetch(url,{cache:'no-store',signal:AbortSignal.timeout(20000)});if(!r.ok)throw Error('官网更新信息暂不可用（'+r.status+'），可稍后重试');const text=await r.text();if(text.length>100000)throw Error('更新清单过大');return JSON.parse(text);},
  prepare:async()=>{await noAgent();await save();await local('update-backup',{});await noAgent();},
  install:()=>autoUpdater.quitAndInstall(true,true),
  onState:state=>{if(!win.isDestroyed())win.webContents.send('update:state',state);}
 });
 ipcMain.handle('update:action',async(event,action)=>{
  if(!trusted(event))throw Error('Forbidden');
  if(action==='state')return controller.state();
  if(!app.isPackaged)throw Error('开发环境不安装更新');
  if(!['check','download','install','cancel'].includes(action))throw Error('Unknown update action');
  return controller[action]();
 });
 if(!process.argv.includes('--smoke-test')){const timer=setTimeout(()=>{if(controller.state().phase==='idle')controller.check().catch(()=>{});},12000);timer.unref();}
 return controller;
}
module.exports={installUpdates};
