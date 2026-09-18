import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {planAttributeInspection} from './plan-attribute-inspection.js';
const fixture=JSON.parse(execFileSync('python',['-c',[
 'import json',
 'from nengine_booster_plan import analyze_plan',
 'from nengine_catalog import item_metadata,indexed_item',
 'from nengine_adapter import bridge',
 "r=analyze_plan({'implants':[],'boosters':[{'typeId':9950,'slot':1,'enabledSideEffects':[]}],'pilot':{'name':'QA','skills':[{'skillTypeId':25530,'level':5}]}})",
 "print(json.dumps({'response':r,'metadata':item_metadata(indexed_item(9950))}))",
 'bridge().close()'
].join(';')],{encoding:'utf8',maxBuffer:8*1024*1024}));
const {response,metadata}=fixture,original=structuredClone(response);
const result=planAttributeInspection(response,'boosters',1,metadata.attributes);
assert.equal(result.planHash,response.analysis.planHash);
assert.deepEqual(response,original);
assert(result.items.some(r=>r.trace?.steps.length));
for(const row of result.items){
 assert.equal(row.query.itemId,'booster.boosters-1');
 assert.strictEqual(row.trace,response.analysis.attributes[row.query.itemId+'/'+row.query.attributeId]);
}
const missing=planAttributeInspection(response,'boosters',1,[{id:999999}]).items[0];
assert.equal(missing.state,'unavailable');assert.equal(missing.trace,null);assert(missing.reason);
const blocked=planAttributeInspection({...response,analysis:{...response.analysis,projectionComplete:false}},'boosters',1,metadata.attributes);
assert(blocked.items.every(r=>r.state==='blocked_static_dependencies'&&r.trace===null));
assert.throws(()=>planAttributeInspection(response,'boosters',2,metadata.attributes),/已不存在/);
console.log('Public plan traces, scope, unknown values and blocked projection checks passed');
