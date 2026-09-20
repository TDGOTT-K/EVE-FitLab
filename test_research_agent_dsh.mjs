// Read-only replay of the three historical jobs through the real DSH projection.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import {pathToFileURL} from 'node:url';
import {spawnSync} from 'node:child_process';
const root=process.env.FITLAB_NENGINE_ROOT,dll=process.env.FITLAB_AGENT_MCP_DLL,state=process.env.FITLAB_RESEARCH_STATE,modules=process.env.DSH_MODULES;
if(!root||!dll||!state||!modules)throw Error('Set engine, DLL, historical state and DSH_MODULES');
const plugin=await import(pathToFileURL(path.join(modules,'@deepseek-ai/dsh-mcp-client/lib/index.js')));
const {isJsonValue}=await import(pathToFileURL(path.join(modules,'@deepseek-ai/dsh-util-values/lib/index.js')));
const tools=new Map(),disposers=[],evidence=[];
const ctx={root:{},logger:console,effect(fn){disposers.push(fn())},tools:{register(t){tools.set(t.name,t);return ()=>tools.delete(t.name)}}};
async function call(name,args){
 const t=tools.get('mcp__fitlab__'+name),raw=await t.execute(args,{signal:AbortSignal.timeout(90000)});assert(isJsonValue(raw));
 const text=(await t.output.render(args,raw)).map(x=>x.text||'').join('\n');const p=JSON.parse(text);assert(p.ok,text);
 evidence.push({name,characters:text.length});return p.result;
}
try{
 await plugin.apply(ctx,{...plugin.Config({serverName:'fitlab',transport:'stdio',command:'python',args:[path.resolve('agent_mcp.py'),'--engine',root,'--state',state,'--mcp-dll',dll],toolCallTimeoutMs:90000}),failOnStartupError:true});
 assert.equal(tools.size,8);
 const args={action:'result',jobId:'job-caracal-1v1'};
 const first=await call('fitlab_battle',args);assert(first.value,'Battle summary should be inline');
 const navy=first.value.timeline.ships.find(s=>s.shipId==='navy');
 assert.equal(navy.lastDamageSnapshot.timeSeconds,68.7);assert.equal(navy.atSimulationEnd.timeSeconds,300);assert.equal(navy.atOtherShipDestruction.state,'unavailable');
 for(const [job,time] of [['job-t1-vs-merlin',85],['job-navy-vs-merlin',50.51971]]){
  const result=await call('fitlab_battle',{action:'result',jobId:job});assert.equal(result.value.timeline.destructions[0].timeSeconds,time);
  const events=await call('fitlab_battle',{action:'events',jobId:job,kinds:['ShipDestroyed'],targetId:'merlin'});assert.equal(events.value.events.length,1);
 }
 const fitted=await call('fitlab_fit',{action:'attributes',sessionId:'caracal-base',names:['maxVelocity','shieldCapacity']});assert(fitted.attributes.every(a=>a.state==='available'));
 await call('fitlab_item',{typeId:3831,names:['cpu','power']});
 fs.mkdirSync('output',{recursive:true});const input=path.resolve('output/research-cli-arguments.json'),output=path.resolve('output/research-cli-result.json');fs.writeFileSync(input,JSON.stringify(args));
 const cli=spawnSync('python',['agent_mcp.py','--engine',root,'--state',state,'--mcp-dll',dll,'--call','fitlab_battle','--arguments',input,'--out',output],{encoding:'utf8'});assert.equal(cli.status,0,cli.stderr);
 assert.deepEqual(JSON.parse(fs.readFileSync(output,'utf8')).result,first);
 fs.writeFileSync('output/research-dsh-verification.json',JSON.stringify({evidence,cliParity:true,navy},null,2));
 console.log(JSON.stringify({evidence,cliParity:true},null,2));
}finally{for(const d of disposers.reverse())await d?.()}
