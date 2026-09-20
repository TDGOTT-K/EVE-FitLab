// Verify the installed DSH plugin's model-facing projection, not just Python results.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import {pathToFileURL} from 'node:url';
const modules=process.env.DSH_MODULES,root=process.env.FITLAB_NENGINE_ROOT,dll=process.env.FITLAB_AGENT_MCP_DLL,baseline=process.env.FITLAB_AGENT_BASELINE;
if(!modules||!root||!dll||!baseline)throw Error('Set DSH_MODULES and FITLAB engine/DLL/baseline variables');
const plugin=await import(pathToFileURL(path.join(modules,'@deepseek-ai/dsh-mcp-client/lib/index.js')));
const {isJsonValue}=await import(pathToFileURL(path.join(modules,'@deepseek-ai/dsh-util-values/lib/index.js')));
const tools=new Map(),disposers=[],evidence=[];
const ctx={root:{},logger:console,effect(fn){disposers.push(fn())},tools:{register(t){tools.set(t.name,t);return ()=>tools.delete(t.name)}}};
async function call(name,args){
 const t=tools.get('mcp__fitlab__'+name),start=performance.now();
 const raw=await t.execute(args,{signal:AbortSignal.timeout(90000)});assert(isJsonValue(raw));
 const text=(await t.output.render(args,raw)).map(x=>x.text||'').join('\n'),p=JSON.parse(text);
 assert(p.ok,text);evidence.push({name,characters:text.length,ms:performance.now()-start});return p.result;
}
try{
 await plugin.apply(ctx,{...plugin.Config({serverName:'fitlab',transport:'stdio',command:'python',args:[path.resolve('agent_mcp.py'),'--engine',root,'--state',path.resolve('output/catalog-dsh-state'),'--mcp-dll',dll,'--baseline',baseline],toolCallTimeoutMs:90000}),failOnStartupError:true});
 assert.equal(tools.size,8);
 const query={groupId:40,filters:[{attribute:'cpu',max:100}],attributes:['cpu','power'],sortBy:'power',limit:3};
 const first=await call('fitlab_search',{query});assert(first.value.items.length===3);assert(first.nextCall);
 const second=await call(first.nextCall.tool,first.nextCall.arguments);assert.notEqual(first.value.items[0].typeId,second.value.items[0].typeId);
 const defs=await call('fitlab_search',{query:{attributeSearch:'CPU',limit:3}});assert(defs.value.attributes.length);
 const batch=await call('fitlab_item',{typeIds:first.value.items.map(i=>i.typeId),names:['cpu','power']});assert.equal(batch.value.length,3);assert(batch.value.every(r=>r.result.attributes.length===2));
 const named=await call('fitlab_search',{names:['Shield']});assert(named.value[0].nextCall);await call(named.value[0].nextCall.tool,named.value[0].nextCall.arguments);
 fs.writeFileSync('output/catalog-dsh-verification.json',JSON.stringify({evidence,toolCount:tools.size},null,2));
 console.log(JSON.stringify(evidence,null,2));
}finally{for(const d of disposers.reverse())await d?.()}
