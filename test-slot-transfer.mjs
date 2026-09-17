import assert from 'node:assert/strict';
import {transferSlots} from './slot-transfer.js';
const slots=[{key:'a',kind:'high',item:1,ammo:10,state:'Offline',online:false},{key:'b',kind:'high',item:2,ammo:20,state:'Overload',online:true},{key:'c',kind:'high',item:null}];
const original=JSON.stringify(slots),compatible=(id,key)=>key!=='no';
const swap=transferSlots(slots,'a','b',false,compatible);
assert.equal(swap[0].item,2);assert.equal(swap[0].state,'Overload');assert.equal(swap[1].ammo,10);assert.equal(swap[1].online,false);
assert.deepEqual(swap.map(s=>s.key),['a','b','c']);assert.equal(JSON.stringify(slots),original);
assert.equal(transferSlots(slots,'a','a',false,compatible),null);
assert.equal(transferSlots(slots,'a','b',true,(id,key)=>id===10),null); // reverse ammo incompatible
assert.equal(transferSlots(slots,'a','b',false,(id,key)=>id===1),null); // reverse module incompatible
const moved=transferSlots(slots,'a','c',false,compatible);assert.equal(moved[0].item,null);assert.equal(moved[2].state,'Offline');
assert.deepEqual(swap.filter(s=>s.item).map(s=>s.item).sort(),slots.filter(s=>s.item).map(s=>s.item).sort()); // no new fitted modules at full limits
console.log('Slot transfer cases passed');
