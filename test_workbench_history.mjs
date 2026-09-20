import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import {createNativeEditHistory} from './native-edit-history.js';
const script=`import json,tempfile,sys
from pathlib import Path
from unittest.mock import patch
from nengine_bridge import NEngineBridge,NEngineError
from nengine_workbench import edit
from nengine_sessions import request
with tempfile.TemporaryDirectory() as tmp:
 c=NEngineBridge(state=Path(tmp))
 try:
  with patch('nengine_adapter.bridge',return_value=c),patch('nengine_workbench.bridge',return_value=c):
   for line in sys.stdin:
    m=json.loads(line)
    try:r=edit(m['args'],[]) if m['action']=='workbench-edit' else request(m['action'],m['args'],c)
    except NEngineError as e:r=e.payload
    except Exception as e:r={'ok':False,'error':{'message':str(e)}}
    print(json.dumps(r),flush=True)
 finally:c.close()
`;
const child=spawn('python',['-u','-c',script],{stdio:['pipe','pipe','inherit']}),lines=createInterface({input:child.stdout});
let waiting=null,lose=true;const requests=[];
lines.on('line',line=>{const p=waiting;waiting=null;p.resolve(JSON.parse(line))});
child.on('exit',code=>{if(waiting){waiting.reject(Error('Host closed '+code));waiting=null}});
async function api(path,args){
 const action=path.split('/').at(-1);requests.push({action,args:structuredClone(args)});
 const response=await new Promise((resolve,reject)=>{assert.equal(waiting,null);waiting={resolve,reject};child.stdin.write(JSON.stringify({action,args})+'\n')});
 if(!response.ok){const e=Error(response.error.message);e.code=response.error.code;throw e}
 if(action==='workbench-edit'&&args.operation==='apply'&&lose){lose=false;throw Error('lost atomic response')}
 return response;
}
try{
 const h=createNativeEditHistory(api,{workbench:true});
 const before={id:crypto.randomUUID(),name:'Atomic history',shipId:587,slots:[],skills:[{skillTypeId:3327,level:5},{skillTypeId:3300,level:3}]};
 const after={...before,slots:[{key:'high-0',kind:'high',item:2881,ammo:185,state:'Active'}]};
 await assert.rejects(h.apply(before,after),/lost atomic response/);assert.equal(h.state().pending,true);
 const retried=await h.retry();assert.equal(retried.result.replayed,true);assert.equal(h.state().revision,1);
 assert.deepEqual(requests[0],requests[1]);assert.equal(requests.length,2);assert(requests.every(r=>r.action==='workbench-edit'));
 assert.equal((await h.undo()).report.nativeFit.items.length,0);assert.equal((await h.redo()).report.nativeFit.items.length,1);
 const noOp=await h.apply(after,{...after,outputMetric:'loadedCycleDps'});assert.equal(noOp.changed,false);const rev=h.state().revision;
 await h.undo();await h.redo();assert.equal(h.state().revision,rev);
 console.log('Atomic workbench: one request, lost-response replay, undo/redo, sorted skills and UI-only no-op passed');
}finally{child.stdin.end();await new Promise(r=>child.exitCode!==null?r():child.once('exit',r));lines.close()}
