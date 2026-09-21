const semver=require('semver');
const {CancellationToken}=require('electron-updater');
const {sameBinding,verifyManifest,verifyInfo,verifyInstaller}=require('./update-security.cjs');
const FEED='https://imfishman.com/updates/stable/';
function createUpdateController({updater,currentVersion,binding,publicKey,readManifest,prepare,install,onState=()=>{},verifyFile=verifyInstaller}){
 let state={phase:'idle',currentVersion,channel:'stable'},manifest=null,file=null,busy=false,cancellation=null;
 const emit=patch=>{state={...state,...patch};onState({...state});return {...state};};
 updater.autoDownload=false;updater.autoInstallOnAppQuit=false;updater.allowPrerelease=false;updater.allowDowngrade=false;
 updater.disableWebInstaller=true;
 updater.setFeedURL({provider:'generic',url:FEED,channel:'latest',useMultipleRangeRequest:false});
 updater.on('error',error=>{if(!busy)emit({phase:file?'downloaded':'error',error:error.message});});
 updater.on('download-progress',p=>emit({percent:Math.max(0,Math.min(100,p.percent||0)),transferred:p.transferred,bytesPerSecond:p.bytesPerSecond}));
 async function run(fn){if(busy)throw Error('更新操作正在进行');busy=true;try{return await fn();}catch(e){emit({phase:file?'downloaded':'error',error:e.message});return {...state};}finally{busy=false}}
 return {
  state:()=>({...state}),
  cancel:()=>{if(state.phase!=='downloading'||!cancellation)throw Error('当前没有可取消的更新下载');cancellation.cancel();return {...state};},
  check:()=>run(async()=>{
   file=null;manifest=null;emit({phase:'checking',error:null,percent:0,version:null,notes:[]});
   const m=verifyManifest(await readManifest(FEED+'manifest.json'),publicKey);
   if(!semver.gt(m.version,currentVersion))return emit({phase:'current'});
   manifest=m;emit({version:m.version,notes:m.notes,size:m.installer.size});
   if(!sameBinding(binding,m.engineSource))return emit({phase:'blocked',error:'新版本的引擎或数据来源不同，请先备份并按发行说明手动升级。'});
   const result=await updater.checkForUpdates();verifyInfo(result?.updateInfo,m);
   return emit({phase:'available'});
  }),
  download:()=>run(async()=>{
   if(state.phase!=='available'||!manifest)throw Error('请先检查更新');
   emit({phase:'downloading',error:null,percent:0});
   cancellation=new CancellationToken();let paths;
   try{paths=await updater.downloadUpdate(cancellation);if(cancellation.cancelled)return emit({phase:'cancelled',error:null});}catch(e){if(cancellation.cancelled)return emit({phase:'cancelled',error:null});throw e;}finally{cancellation=null;}
   if(!paths?.[0])throw Error('更新文件尚未下载完成');
   await verifyFile(paths[0],manifest);file=paths[0];return emit({phase:'downloaded',percent:100});
  }),
  install:()=>run(async()=>{
   if(!file||!manifest)throw Error('请先下载更新');
   emit({phase:'preparing',error:null});
   await verifyFile(file,manifest);await prepare();
   // Recheck after backup: neither a stale metadata file nor a replaced installer is accepted.
   await verifyFile(file,manifest);emit({phase:'installing'});await install();return {...state};
  })
 };
}
module.exports={createUpdateController};
