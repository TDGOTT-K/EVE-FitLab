// Read-only deployment check against the actual DSH profile, not a test configuration.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import {pathToFileURL} from 'node:url';
const modules=process.env.DSH_MODULES||'C:/Users/23779/AppData/Local/Programs/DSH Desktop/resources/app.asar.unpacked/node_modules';
const profile=process.argv[2]||'C:/Users/23779/.dsh/profiles/fitlab/cordis.patch.yml';
const {parse}=await import(pathToFileURL(path.join(modules,'yaml/dist/index.js')));
const plugin=await import(pathToFileURL(path.join(modules,'@deepseek-ai/dsh-mcp-client/lib/index.js')));
const row=parse(fs.readFileSync(profile,'utf8')).flatMap(p=>p.insert??[]).find(p=>p.id==='mcp-fitlab');assert(row);
const tools=new Map(),disposers=[];
const ctx={root:{},logger:console,effect(f){disposers.push(f())},tools:{register(t){tools.set(t.name,t);return ()=>tools.delete(t.name)}}};
try{
 await plugin.apply(ctx,{...plugin.Config(row.config),failOnStartupError:true});assert.equal(tools.size,8);
 const args={action:'read'},tool=tools.get('mcp__fitlab__fitlab_status');assert(tool);
 const raw=await tool.execute(args,{signal:AbortSignal.timeout(90000)});
 const reply=JSON.parse((await tool.output.render(args,raw)).map(b=>b.text||'').join('\n'));
 assert(reply.ok);assert.equal(reply.apiVersion,'fitlab-agent-v5');assert.equal(reply.data.engine.contractRevision,62);
 console.log(JSON.stringify({tools:tools.size,...reply.data},null,2));
}finally{for(const d of disposers.reverse())await d?.()}
