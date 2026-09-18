import assert from 'node:assert/strict';
import {createFitSaver,sameSavedContent} from './fit-save-controller.js';
const next=()=>new Promise(resolve=>setImmediate(resolve));
let current={name:'A',slots:[],loadoutPlan:{implants:[]}},writes=[],receipts=[],resolveWrite;
const saver=createFitSaver({read:()=>current,write:input=>{writes.push(structuredClone(input));return new Promise(resolve=>{resolveWrite=resolve;});},accept:(saved,state)=>{receipts.push({saved,state});current={...current,id:saved.id,revision:saved.revision};}});
const first=saver.save();await next();
current={name:'B',slots:[]};saver.reset();
resolveWrite({id:'a',revision:1});await first;
assert.equal(current.name,'B');assert.equal(current.id,undefined);assert.equal(receipts.length,0);
assert.equal(writes[0].name,'A');

// Two clicks capture different snapshots but allocate only one new fit identity.
current={name:'C',slots:[]};saver.reset();
const second=saver.save();current={...current,name:'C edited'};const third=saver.save();await next();
resolveWrite({id:'c',revision:1});await second;await next();
assert.equal(writes[1].name,'C');assert.equal(writes[2].name,'C edited');
assert.equal(writes[2].id,'c');assert.equal(writes[2].revision,1);
assert.equal(receipts[0].state.unchanged,false);
resolveWrite({id:'c',revision:2});await third;assert.equal(receipts[1].state.unchanged,true);

// An implant edit during saving remains visibly unsaved.
current={name:'D',slots:[],loadoutPlan:{implants:[]}};saver.reset();
const fourth=saver.save();await next();current.loadoutPlan.implants.push({typeId:2082});
resolveWrite({id:'d',revision:1});await fourth;
assert.equal(receipts.at(-1).state.unchanged,false);assert.deepEqual(writes.at(-1).loadoutPlan.implants,[]);
assert.equal(sameSavedContent({name:'x',tags:['a']},{tags:['a'],name:'x',id:'1',revision:2}),true);
assert.equal(sameSavedContent({cargo:[]},{cargo:[{item:185,quantity:1}]}),false);

// A failed write does not poison later saves or acknowledge a nonexistent revision.
let calls=0,accepted=0;
const failing=createFitSaver({read:()=>({name:'Retry'}),write:async input=>{assert.equal(input.revision,undefined);if(++calls===1)throw Error('offline');return {id:'retry',revision:1};},accept:()=>accepted++});
await assert.rejects(failing.save());await failing.save();assert.equal(accepted,1);
console.log('Save races: selection, queued snapshots, assigned identity, implant dirty state, failure recovery passed');
