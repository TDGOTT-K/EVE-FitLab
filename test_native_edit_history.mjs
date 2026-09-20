import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import {createNativeEditHistory} from './native-edit-history.js';
const script=`import json,sys,tempfile
from pathlib import Path
from nengine_bridge import NEngineBridge,NEngineError
from nengine_sessions import request
from nengine_persistence import save_to_library
library={'fits':[]}
def store(value):
 global library
 library=value
with tempfile.TemporaryDirectory() as directory:
 client=NEngineBridge(state=Path(directory)/'state')
 try:
  for line in sys.stdin:
   message=json.loads(line)
   try:
    if message['action']=='save':result={'ok':True,'result':save_to_library(message['args'],library,store,'test-time',client)}
    else:result=request(message['action'],message['args'],client)
   except NEngineError as error:result=error.payload
   except Exception as error:result={'ok':False,'error':{'message':str(error)}}
   print(json.dumps(result),flush=True)
 finally:client.close()
`;
const child=spawn('python',['-u','-c',script],{stdio:['pipe','pipe','inherit']}),lines=createInterface({input:child.stdout});
let waiting=null;
lines.on('line',line=>{const pending=waiting;waiting=null;pending.resolve(JSON.parse(line));});
child.on('exit',code=>{if(waiting){waiting.reject(Error('Native test host exited '+code));waiting=null;}});
const requests=[];let loseApply=true,loseCreate=false,loseSave=false;
async function api(path,args){
 const action=path.split('/').at(-1);requests.push({action,args:structuredClone(args)});
 const response=await new Promise((resolve,reject)=>{assert.equal(waiting,null);waiting={resolve,reject};child.stdin.write(JSON.stringify({action,args})+'\n');});
 if(!response.ok){const error=Error(response.error.message);error.code=response.error.code;throw error;}
 if(action==='execute'&&args.operation==='apply'&&loseApply){loseApply=false;throw Error('Simulated lost apply response');}
 if(action==='create'&&loseCreate){loseCreate=false;throw Error('Simulated lost create response');}
 if(action==='save'&&loseSave){loseSave=false;throw Error('Simulated lost save response');}
 return response;
}
try{
 const history=createNativeEditHistory(api),before={id:crypto.randomUUID(),name:'History',shipId:587,slots:[],skills:[]};
 const after={...before,slots:[{key:'high-0',kind:'high',item:2881,ammo:185,state:'Active'}]};
 await assert.rejects(history.apply(before,after),/lost apply response/);
 assert.equal(history.state().pending,true);assert.equal(history.state().revision,0);
 await assert.rejects(history.undo(),/先重试/);
 const resumed=await history.retry();assert.equal(resumed.result.replayed,true);assert.equal(history.state().revision,1);
 const applies=requests.filter(r=>r.action==='execute'&&r.args.operation==='apply');assert.deepEqual(applies[0],applies[1]);
 const undone=await history.undo();assert.equal(undone.result.working.items.length,0);
 const redone=await history.redo();assert.equal(redone.result.working.items.length,1);assert.equal(history.state().revision,3);
 const unchanged=await history.apply(after,{...after,outputMetric:'loadedCycleDps'});assert.equal(unchanged.changed,false);
 await history.undo();assert.equal(history.state().revision,3);await history.redo();assert.equal(history.state().revision,3);
 const firstOwner=history.capture();history.reset();assert.equal(history.state().opened,false);assert.equal(history.state(firstOwner).revision,3);
 loseCreate=true;await assert.rejects(history.apply(before,after),/lost create response/);
 await history.retry();assert.equal(history.state().revision,1);
 assert.equal(history.state(firstOwner).revision,3);
 const saved=await history.save(after,async input=>(await api('save',{...input,_saveRequestId:crypto.randomUUID()})).result);
 assert.equal(saved.nativeSession.id,history.state().id);assert.equal(history.state().revision,2);
 const afterSaveUndo=await history.undo();assert.equal(afterSaveUndo.result.working.items.length,0);
 const savedUndo=await history.save({...before,revision:saved.revision},async input=>(await api('save',{...input,_saveRequestId:crypto.randomUUID()})).result);
 assert.equal(savedUndo.nativeSession.id,saved.nativeSession.id);assert.equal(savedUndo.nativeSession.revision,4);
 await history.redo();assert.equal(history.state().revision,5);
 const saveToken=crypto.randomUUID(),saveInput={...after,revision:savedUndo.revision};
 const writer=async input=>(await api('save',{...input,_saveRequestId:saveToken})).result;
 loseSave=true;await assert.rejects(history.save(saveInput,writer),/lost save response/);
 assert.equal(history.state().pendingSave,true);await assert.rejects(history.undo(),/先重试/);
 await history.save(saveInput,writer);assert.equal(history.state().revision,6);assert.equal(history.state().pending,false);
 const reloaded={...saveInput,revision:savedUndo.revision+1};history.reset();
 const reopenSaved=await history.save(reloaded,async input=>(await api('save',{...input,_saveRequestId:crypto.randomUUID()})).result);
 assert.equal(reopenSaved.nativeSession.id,history.state().id);assert.equal(history.state().revision,1);assert.equal(history.state().canUndo,false);
 history.reset();
 const prepared=(await api('prepare',{before,after})).result;
 const analysis=(await api('preview-input',{fit:prepared.fit,commands:prepared.commands})).result;
 await history.apply(before,after);await history.undo();
 const previewCount=requests.filter(r=>r.action==='preview-input').length;
 loseApply=true;
 await assert.rejects(history.apply(before,after,history.capture(),{beforeHash:analysis.baselineAnalysis.fitHash,afterHash:Promise.resolve(analysis.candidateHash)}),/lost apply response/);
 const shared=await history.retry();assert.equal(shared.result.replayed,true);
 assert.equal(requests.filter(r=>r.action==='preview-input').length,previewCount);
 assert.equal((await history.undo()).result.working.items.length,0);
 assert.equal((await history.redo()).result.working.items.length,1);
 console.log('Real native history: shared analysis hashes, apply/undo/redo, no-op UI changes, owner separation, lost create/apply recovery passed');
}finally{child.stdin.end();await new Promise(resolve=>{if(child.exitCode!==null)resolve();else child.once('exit',resolve);});lines.close();}
