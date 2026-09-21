const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
function installDownloads({ipcMain,win,shell,root}){
 const file=path.join(root,'downloads.json'),live=new Map();let rows=[];
 try{rows=JSON.parse(fs.readFileSync(file,'utf8')).slice(0,100).map(r=>({...r,status:['downloading','paused','interrupted'].includes(r.status)?'cancelled':r.status,resumable:false}));}catch{}
 const snapshot=()=>rows.map(r=>({...r}));
 function emit(){try{fs.writeFileSync(file+'.tmp',JSON.stringify(rows.slice(0,100)));fs.renameSync(file+'.tmp',file);}catch{}if(!win.isDestroyed())win.webContents.send('downloads:state',snapshot());}
 win.webContents.session.on('will-download',(_event,item,contents)=>{
  if(contents!==win.webContents)return;
  const id=crypto.randomUUID(),row={id,kind:'file',name:item.getFilename(),time:Date.now(),status:'downloading',size:item.getTotalBytes(),percent:0};rows.unshift(row);live.set(id,item);emit();let last=0;
  item.on('updated',(_e,state)=>{row.status=item.isPaused()?'paused':state==='interrupted'?'interrupted':'downloading';row.size=item.getTotalBytes();row.percent=row.size?item.getReceivedBytes()/row.size*100:0;row.resumable=item.canResume();if(Date.now()-last>250||row.status!=='downloading'){last=Date.now();emit();}});
  item.once('done',(_e,state)=>{row.status=state==='completed'?'completed':state==='cancelled'?'cancelled':'error';row.path=state==='completed'?item.getSavePath():null;row.resumable=false;live.delete(id);emit();});
 });
 const trusted=e=>e.sender===win.webContents&&e.senderFrame===win.webContents.mainFrame;
 ipcMain.handle('downloads:list',e=>{if(!trusted(e))throw Error('Forbidden');return snapshot();});
 ipcMain.handle('downloads:action',(e,id,action)=>{
  if(!trusted(e))throw Error('Forbidden');const row=rows.find(r=>r.id===id),item=live.get(id);if(!row)throw Error('下载记录不存在');
  if(action==='pause'&&item){item.pause();row.status='paused';row.resumable=item.canResume();}
  else if(action==='resume'&&item&&item.canResume()){item.resume();row.status='downloading';}
  else if(action==='cancel'&&item)item.cancel();
  else if(action==='reveal'&&row.status==='completed'&&row.path){if(!fs.existsSync(row.path))throw Error('文件已移动或删除');shell.showItemInFolder(row.path);}
  else if(action==='remove'&&!item)rows=rows.filter(r=>r.id!==id);
  else throw Error('当前状态不支持此操作');emit();return snapshot();
 });
}
module.exports={installDownloads};
