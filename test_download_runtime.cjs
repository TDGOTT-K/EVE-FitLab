const assert=require('node:assert/strict'),{EventEmitter}=require('node:events'),fs=require('node:fs'),os=require('node:os'),path=require('node:path');
const {installDownloads}=require('./desktop/download-runtime.cjs');
const root=fs.mkdtempSync(path.join(os.tmpdir(),'fitlab-downloads-'));
try{
 const handlers={},session=new EventEmitter(),frame={},contents={session,mainFrame:frame,send(){}},win={webContents:contents,isDestroyed:()=>false};let revealed;
 installDownloads({ipcMain:{handle:(name,fn)=>handlers[name]=fn},win,shell:{showItemInFolder:p=>revealed=p},root});
 const e={sender:contents,senderFrame:frame},list=()=>handlers['downloads:list'](e);
 assert.throws(()=>handlers['downloads:list']({sender:contents,senderFrame:{}}),/Forbidden/);
 const item=new EventEmitter();let paused=false,cancelled=false;const target=path.join(root,'test.txt');fs.writeFileSync(target,'ok');
 Object.assign(item,{getFilename:()=> 'test.txt',getTotalBytes:()=>100,getReceivedBytes:()=>50,getSavePath:()=>target,isPaused:()=>paused,canResume:()=>true,pause:()=>paused=true,resume:()=>paused=false,cancel:()=>{cancelled=true;item.emit('done',{},'cancelled');}});
 session.emit('will-download',{},item,contents);const id=list()[0].id;
 item.emit('updated',{},'progressing');assert.equal(list()[0].percent,50);
 handlers['downloads:action'](e,id,'pause');assert.equal(list()[0].status,'paused');
 handlers['downloads:action'](e,id,'resume');assert.equal(list()[0].status,'downloading');
 item.emit('done',{},'completed');handlers['downloads:action'](e,id,'reveal');assert.equal(revealed,target);
 handlers['downloads:action'](e,id,'remove');assert.equal(list().length,0);assert.ok(fs.existsSync(target));
 session.emit('will-download',{},item,contents);handlers['downloads:action'](e,list()[0].id,'cancel');assert.ok(cancelled);assert.equal(list()[0].status,'cancelled');
 console.log('Download runtime passed: trusted IPC, progress, pause/resume, cancel, reveal, remove without deleting file.');
}finally{fs.rmSync(root,{recursive:true,force:true});}
