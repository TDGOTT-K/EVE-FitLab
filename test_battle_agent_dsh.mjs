import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import assert from 'node:assert/strict';
import {pathToFileURL} from 'node:url';
import {spawnSync} from 'node:child_process';
const root=process.env.FITLAB_NENGINE_ROOT,dll=process.env.FITLAB_AGENT_MCP_DLL,modules=process.env.DSH_MODULES;
if(!root||!dll||!modules)throw Error('Set engine, isolated MCP dll and DSH_MODULES');
const plugin=await import(pathToFileURL(path.join(modules,'@deepseek-ai/dsh-mcp-client/lib/index.js')));
const {isJsonValue}=await import(pathToFileURL(path.join(modules,'@deepseek-ai/dsh-util-values/lib/index.js')));
const state=fs.mkdtempSync(path.join(os.tmpdir(),'fitlab-battle-agent-')),tools=new Map(),disposers=[],calls=[];
const ctx={root:{},logger:console,effect(fn){disposers.push(fn())},tools:{register(t){tools.set(t.name,t);return ()=>tools.delete(t.name)}}};
async function call(name,args){
 const t=tools.get('mcp__fitlab__'+name);assert(t);
 const raw=await t.execute(args,{signal:AbortSignal.timeout(90000)});assert(isJsonValue(raw));
 const text=(await t.output.render(args,raw)).filter(x=>x.type==='text').map(x=>x.text).join('\n');
 const p=JSON.parse(text);assert(p.ok,text);calls.push({name,action:args.action,characters:text.length});return p.result;
}
try{
 await plugin.apply(ctx,{...plugin.Config({serverName:'fitlab',transport:'stdio',command:'python',args:[path.resolve('agent_mcp.py'),'--engine',root,'--state',state,'--mcp-dll',dll],toolCallTimeoutMs:90000}),failOnStartupError:true});
 assert.equal(tools.size,8);
 await call('fitlab_status',{});
 const ships=(await call('fitlab_search',{names:['Caracal','Caracal Navy Issue'],categoryId:6})).value;
 const items=(await call('fitlab_search',{names:['Heavy Missile Launcher I','Scourge Heavy Missile']})).value;
 const exact=(page,name)=>{const i=page.candidates.find(i=>i.names.en===name);assert(i,name);return i.typeId};
 const weapon=exact(items[0],'Heavy Missile Launcher I'),ammo=exact(items[1],'Scourge Heavy Missile');
 const refs=[];
 for(const [i,name] of ['Caracal','Caracal Navy Issue'].entries()){
  const sid='caracal-'+i;const created=await call('fitlab_fit',{action:'create',sessionId:sid,shipTypeId:exact(ships[i],name),skillPreset:'all5'});
  assert(created.skillCount>400);
  const commands=Array.from({length:5},(_,j)=>({kind:'install',item:{id:'launcher'+j,typeId:weapon,chargeTypeId:ammo,slotIndex:j,active:true}}));
  const edited=await call('fitlab_fit',{action:'edit',sessionId:sid,revision:created.revision,requestId:'install',commands});
  refs.push({sessionId:sid,revision:edited.revision,id:i?'navy':'regular',team:i?'blue':'red',position:{x:i?10000:0,y:0,z:0},reservePerWeapon:30});
 }
 const request={action:'prepare',id:'caracal-pair',seed:17,seconds:30,ships:refs};
 const draft=await call('fitlab_battle',request);assert.equal(draft.roster.length,2);
 await assert.rejects(call('fitlab_battle',{...request,ships:refs.map(s=>({...s,supplies:{}}))}),/SUPPLIES/);
 const start={action:'start',draftId:draft.draftId,jobId:'paired-combat',policyPreset:'stationary-weapons-v1'};
 await call('fitlab_battle',start);
 let result;
 for(let i=0;i<120;i++){
  const s=(await call('fitlab_battle',{action:'status',jobId:'paired-combat',waitSeconds:10})).value;
  if(!['queued','running'].includes(s.status)){assert.equal(s.status,'complete',JSON.stringify(s));break;}
  if(i===119)throw Error('Battle timeout');await new Promise(r=>setTimeout(r,500));
 }
 result=(await call('fitlab_battle',{action:'result',jobId:'paired-combat'})).value;
 assert(result.ships.every(s=>s.abilities.some(a=>a.activations>0)),'Both sides must actually fire');
 const replay=await call('fitlab_battle',start);assert.equal(replay.job.attempt,1);
 await call('fitlab_battle',{action:'events',jobId:'paired-combat',offset:0,limit:5});
 // Independent CLI parity verification, AFTER the entire agent workflow completed via MCP.
 const stored=JSON.parse(fs.readFileSync(path.join(state,'agent-battle-drafts',draft.draftId+'.json'),'utf8'));
 const input=path.join(state,'assembly-input.json'),output=path.join(state,'assembly-cli.json');fs.writeFileSync(input,JSON.stringify(stored.assembly.request));
 const cli=spawnSync(path.join(root,'.tools/dotnet/dotnet.exe'),[path.join(path.dirname(dll),'NEngine.Cli.dll'),'sde-battle','--data',path.join(root,'artifacts/sde-mutation-index-002'),'--input',input,'--out',output],{encoding:'utf8',env:{...process.env,DOTNET_ROOT:path.join(root,'.tools/dotnet')}});
 assert.equal(cli.status,0,cli.stderr);assert.deepEqual(JSON.parse(JSON.stringify(stored.assembly)),JSON.parse(JSON.stringify(JSON.parse(fs.readFileSync(output,'utf8')))));
 await assert.rejects(call('fitlab_battle',{...request,ships:refs.map((s,i)=>i?s:{...s,revision:s.revision+1})}),/revision/i);
 await assert.rejects(call('fitlab_battle',{...start,jobId:'missing-policies',policies:{}}),/exactly one/);
 await assert.rejects(call('fitlab_call',{name:'engine_status',arguments:{unexpected:true}}),/NATIVE_TOOL_ERROR/);
 fs.mkdirSync('output',{recursive:true});fs.writeFileSync('output/battle-agent-dsh-verification.json',JSON.stringify({calls,totalCalls:calls.length,schemaBrowsingCalls:calls.filter(c=>c.name==='fitlab_tools'||c.name==='fitlab_result').length,cliParity:true,conditions:draft.initialConditions,ships:result.ships,scope:result.scope},null,2));
 console.log(JSON.stringify({calls:calls.length,schemaBrowsingCalls:0,cliParity:true,ships:result.ships},null,2));
}finally{for(const d of disposers.reverse())await d?.()}
