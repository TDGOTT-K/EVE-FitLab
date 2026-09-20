// Installed DSH client: schema discovery, model-visible values, simple and research tasks.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import assert from 'node:assert/strict';
import {pathToFileURL} from 'node:url';
import {spawnSync} from 'node:child_process';
const modules=process.env.DSH_MODULES||'C:/Users/23779/AppData/Local/Programs/DSH Desktop/resources/app.asar.unpacked/node_modules';
const plugin=await import(pathToFileURL(path.join(modules,'@deepseek-ai/dsh-mcp-client/lib/index.js')));
const {isJsonValue}=await import(pathToFileURL(path.join(modules,'@deepseek-ai/dsh-util-values/lib/index.js')));
const c=JSON.parse(fs.readFileSync('output/agent-v5-config.json','utf8'));
const state=fs.mkdtempSync(path.join(os.tmpdir(),'fitlab-platform-')),config=path.join(state,'config.json');
fs.writeFileSync(config,JSON.stringify({...c,state}));
const tools=new Map(),disposers=[],calls=[];
const ctx={root:{},logger:console,effect(fn){disposers.push(fn())},tools:{register(t){tools.set(t.name,t);return ()=>tools.delete(t.name)}}};
async function call(domain,action,args={}){
 const input={action,...args},tool=tools.get('mcp__fitlab__fitlab_'+domain),start=performance.now();
 assert(tool,domain);const raw=await tool.execute(input,{signal:AbortSignal.timeout(120000)});assert(isJsonValue(raw));
 const text=(await tool.output.render(input,raw)).map(b=>b.text||'').join('\n');const result=JSON.parse(text);
 assert(result.apiVersion==='fitlab-agent-v5');assert(result.ok,text);calls.push({domain,action,ms:performance.now()-start,characters:text.length});return result;
}
try{
 await plugin.apply(ctx,{...plugin.Config({serverName:'fitlab',transport:'stdio',command:'python',args:[path.resolve('fitlab.py'),'--config',config,'mcp'],toolCallTimeoutMs:120000}),failOnStartupError:true});
 assert.equal(tools.size,8);
 const description=await call('catalog','describe',{name:'狞獾级'});assert(description.data.traits.some(t=>t.kind==='skill_per_level'));
 const search=await call('catalog','search',{query:{groupId:40,sortBy:'cpu',limit:2}});assert.equal(search.data.items.length,2);
 const next=search.next[0];assert(next);await call('catalog',next.arguments.action,Object.fromEntries(Object.entries(next.arguments).filter(([k])=>k!=='action')));
 await call('fitting','create',{sessionId:'rifter-example',shipTypeId:587,skillPreset:'all5'});
 await call('fitting','edit',{sessionId:'rifter-example',revision:0,requestId:'weapon-1',changes:[{kind:'install',item:{id:'gun',typeId:2889,slotIndex:0,chargeTypeId:185,active:true}}]});
 const curve=await call('fitting','curves',{sessionId:'rifter-example',target:{distanceMeters:1000,signatureMeters:40,speedMetersPerSecond:100,angularRadiansPerSecond:0.02},intervals:4});
 assert.equal(curve.state,'ready');assert(curve.data.series.every(s=>s.points.length));
 await call('fitting','create',{sessionId:'target-example',shipTypeId:594,skillPreset:'all5'});
 const battle=await call('battle','run',{id:'dsh-frigates',jobId:'dsh-frigates',seed:7,seconds:3,waitSeconds:60,ships:[
  {id:'rifter',team:'A',sessionId:'rifter-example',revision:1,position:{x:0,y:0,z:0},reservePerWeapon:10},
  {id:'target',team:'B',sessionId:'target-example',revision:0,position:{x:1000,y:0,z:0}}]});
 assert.equal(battle.state,'complete');assert.equal(battle.data.provenance.state,'bound');
 const jobs=await call('jobs','list');assert.equal(jobs.data.items.length,1);
 const preview=await call('jobs','compact',{jobId:'dsh-frigates',revision:jobs.data.items[0].revision});assert.equal(preview.state,'preview');

 const cli=spawnSync('python',['fitlab.py','--config',config,'catalog','describe','--name','狞獾级'],{encoding:'utf8'});assert.equal(cli.status,0,cli.stderr);assert.deepEqual(JSON.parse(cli.stdout),description);
 fs.writeFileSync('output/agent-v5-dsh-verification.json',JSON.stringify({toolCount:tools.size,calls,cliParity:true},null,2));
 console.log(JSON.stringify({calls,cliParity:true},null,2));
}finally{for(const d of disposers.reverse())await d?.();fs.rmSync(state,{recursive:true,force:true})}
