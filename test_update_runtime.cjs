const assert=require('node:assert/strict');
const fs=require('node:fs'),vm=require('node:vm'),http=require('node:http');
const {createRequire}=require('node:module');
const path=require('node:path');
(async()=>{
 let requests=0,options;const order=[];
 const server=http.createServer((req,res)=>{
  requests++;assert.equal(req.url,'/api/update-backup');assert.equal(req.method,'POST');
  assert.equal(req.headers['x-fitlab-key'],'test-key');
  const valid=req.headers.origin==='http://'+req.headers.host;
  res.writeHead(valid?200:403,{'Content-Type':'application/json'});
  order.push('backup');res.end(JSON.stringify(valid?{files:1}:{error:'请求来源不匹配'}));
 });
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 try{
  const handlers=new Map(),webContents={mainFrame:{},send(channel,nonce){assert.equal(channel,'update:prepare');order.push('save');queueMicrotask(()=>handlers.get('update:prepared')({sender:webContents,senderFrame:webContents.mainFrame},{nonce,ok:true}));}};
  const nativeRequire=createRequire(path.resolve('desktop/update-runtime.cjs'));
  const sandbox={module:{exports:{}},__dirname:path.resolve('desktop'),fetch,AbortSignal,Buffer,setTimeout,clearTimeout,process:{platform:'win32',argv:['--smoke-test']},require(name){
   if(name==='node:fs')return {readFileSync:(_path,encoding)=>encoding?'{}':Buffer.from('{}')};
   if(name==='node:child_process')return {execFile(...args){order.push('agents');args.at(-1)(null,{stdout:'0'});}};
   if(name==='./update-controller.cjs')return {createUpdateController(o){options=o;return {state:()=>({phase:'idle'})};}};
   if(name==='electron-updater')return {autoUpdater:{}};
   if(name==='electron')return {net:{fetch}};
   return nativeRequire(name);
  }};
  vm.runInNewContext(fs.readFileSync('desktop/update-runtime.cjs','utf8'),sandbox);
  sandbox.module.exports.installUpdates({app:{getVersion:()=> '0.1.4',isPackaged:true},ipcMain:{handle:(name,fn)=>handlers.set(name,fn)},win:{webContents,isDestroyed:()=>false},apiPort:server.address().port,apiKey:'test-key',resources:process.cwd()});
  await options.prepare();assert.equal(requests,1);assert.deepEqual(order,['agents','save','backup','agents']);
  console.log('Update runtime preparation passed: save acknowledgement, authenticated same-origin backup, Agent checks');
 }finally{await new Promise(resolve=>server.close(resolve));}
})().catch(e=>{console.error(e);process.exitCode=1;});
