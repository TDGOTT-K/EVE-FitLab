// Optional installed-client integration. All writes go into a fresh temporary state.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import assert from 'node:assert/strict';
import {pathToFileURL} from 'node:url';
import {spawnSync} from 'node:child_process';
const root=process.env.FITLAB_NENGINE_ROOT;
const dll=process.env.FITLAB_AGENT_MCP_DLL;
const modules=process.env.DSH_MODULES;
if(!root||!dll||!modules)throw Error('Set FITLAB_NENGINE_ROOT, FITLAB_AGENT_MCP_DLL and DSH_MODULES');
const plugin=await import(pathToFileURL(path.join(modules,'@deepseek-ai/dsh-mcp-client/lib/index.js')));
const {isJsonValue}=await import(pathToFileURL(path.join(modules,'@deepseek-ai/dsh-util-values/lib/index.js')));
const state=fs.mkdtempSync(path.join(os.tmpdir(),'fitlab-agent-dsh-'));
const evidence=[];
async function connect(config,run){
 const tools=new Map(),disposers=[];
 const ctx={root:{},logger:console,effect(fn){disposers.push(fn())},tools:{register(t){tools.set(t.name,t);return ()=>tools.delete(t.name)}}};
 try{
  await plugin.apply(ctx,{...plugin.Config(config),failOnStartupError:true});
  async function call(name,args){
   const tool=tools.get('mcp__fitlab__'+name);assert(tool,name);
   const raw=await tool.execute(args,{signal:AbortSignal.timeout(90000)});
   assert(isJsonValue(raw),'DSH lossless JSON validation failed for '+name);
   const text=(await tool.output.render(args,raw)).filter(c=>c.type==='text').map(c=>c.text).join('\n');
   const payload=JSON.parse(text);assert(payload.ok,text.slice(0,500));
   evidence.push({tool:name,modelTextChars:text.length,lossless:true});return payload.result;
  }
  await run(call,tools);
 }finally{for(const dispose of disposers.reverse())await dispose?.()}
}
const fit=process.env.FITLAB_AGENT_FIXTURE?JSON.parse(fs.readFileSync(process.env.FITLAB_AGENT_FIXTURE,'utf8')):
 {id:'agent-phantasm',buildNumber:3503375,shipTypeId:17718,omittedSkills:'untrained',skills:{3318:0,3424:0},items:[{id:'gun',typeId:3520,chargeTypeId:254,slotIndex:0,active:true},{id:'cap',typeId:2032,slotIndex:0}]};
let nativeCount;
await connect({serverName:'fitlab',transport:'stdio',command:path.join(root,'.tools/dotnet/dotnet.exe'),args:[dll,'--data',path.join(root,'artifacts/sde-mutation-index-002'),'--state',path.join(state,'native')],env:{DOTNET_ROOT:path.join(root,'.tools/dotnet'),NENGINE_MCP_STRUCTURED_ONLY:'0'},toolCallTimeoutMs:90000},async(call,tools)=>{
 nativeCount=tools.size;
 const created=await call('fit_create',{sessionId:'negative-zero',fit,allowIncompleteDraft:true});
 const analyzed=await call('fit_analyze',{fit});
 await call('fit_inspect',{sessionId:'negative-zero'});
 const commands=[{kind:'setName',name:'Roundtrip'}];
 await call('fit_preview',{sessionId:'negative-zero',revision:created.revision,commands});
 const edited=await call('fit_execute',{sessionId:'negative-zero',revision:0,requestId:'edit-1',operation:'apply',commands});
 const replay=await call('fit_execute',{sessionId:'negative-zero',revision:0,requestId:'edit-1',operation:'apply',commands});
 assert(replay.replayed);assert.equal(edited.revision,replay.revision);
 await call('fit_attributes',{fit,queries:[{itemId:'module.'+fit.items[0].id,attributeId:50}]});
 const input=path.join(state,'fit.json'),output=path.join(state,'cli.json');fs.writeFileSync(input,JSON.stringify(fit));
 const cli=spawnSync(path.join(root,'.tools/dotnet/dotnet.exe'),[path.join(path.dirname(dll),'NEngine.Cli.dll'),'sde-fit','--data',path.join(root,'artifacts/sde-mutation-index-002'),'--fit',input,'--out',output],{encoding:'utf8',env:{...process.env,DOTNET_ROOT:path.join(root,'.tools/dotnet')}});
 assert.equal(cli.status,0,cli.stderr);
 // JSON stringify canonicalizes signed zero; no other result fields may change.
 assert.equal(JSON.stringify(analyzed),JSON.stringify(JSON.parse(fs.readFileSync(output,'utf8'))));
});
let facadeSchemaChars;
await connect({serverName:'fitlab',transport:'stdio',command:process.env.FITLAB_PYTHON||'python',args:[path.resolve('agent_mcp.py'),'--engine',root,'--state',path.join(state,'agent'),'--mcp-dll',dll],toolCallTimeoutMs:90000},async(call,tools)=>{
 assert.equal(tools.size,7);facadeSchemaChars=JSON.stringify([...tools.values()].map(t=>({name:t.name,description:t.description,parameters:t.parameters}))).length;
 assert(facadeSchemaChars<8000);
 await call('fitlab_status',{});
 await call('fitlab_search',{names:['Phantasm','Heavy Pulse Laser II','Multifrequency M']});
 await call('fitlab_fit',{action:'create',sessionId:'phantasm',shipTypeId:fit.shipTypeId,skills:fit.skills});
 const commands=fit.items.map(item=>({kind:'install',item}));
 if(fit.drones?.length)commands.push({kind:'setDrones',drones:fit.drones});
 const args={action:'edit',sessionId:'phantasm',revision:0,requestId:'install-1',commands};
 const edited=await call('fitlab_fit',args);assert(edited.summary.value,'Ordinary Phantasm summary should fit inline');
 const replay=await call('fitlab_fit',args);assert(replay.replayed);
 const read=await call('fitlab_fit',{action:'read',sessionId:'phantasm'});assert.equal(read.revision,1);
 await call('fitlab_result',{resultId:read.fullResultId,pointer:'/analysis/nominalDps'});
 const schema=await call('fitlab_tools',{name:'battle_start'});
 await call('fitlab_result',{resultId:schema.schema.resultId,pointer:'/properties'});
 // Engine-owned synthetic fixture checks transport/job lifecycle, not EVE fitting admission.
 const scenario=JSON.parse(fs.readFileSync(path.join(root,'examples/aoe-interception.json'),'utf8'));
 await call('fitlab_call',{name:'battle_start',arguments:{jobId:'agent-job',request:{scenario,policies:{red:'function tick({observation,memory}) { return {memory,intents:[]}; }'},endSeconds:3}}});
 let job;
 for(let i=0;i<30;i++){
  job=(await call('fitlab_call',{name:'battle_status',arguments:{jobId:'agent-job'}})).value;
  if(!['queued','running'].includes(job.status))break;
  await new Promise(r=>setTimeout(r,100));
 }
 assert.equal(job.status,'complete',JSON.stringify(job));
 await call('fitlab_call',{name:'battle_result',arguments:{jobId:'agent-job'}});
});
console.log(JSON.stringify({nativeCount,facadeToolCount:7,facadeSchemaChars,evidence},null,2));
fs.mkdirSync('output',{recursive:true});fs.writeFileSync('output/agent-dsh-verification.json',JSON.stringify({nativeCount,facadeSchemaChars,evidence},null,2));
