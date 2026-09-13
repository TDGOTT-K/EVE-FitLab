import assert from 'node:assert/strict';
import {splitDroneStacks,maximumDroneQuantity} from './drone-stacks.js';
assert.deepEqual(splitDroneStacks([{item:1,quantity:12,active:4}]).map(e=>[e.quantity,e.active]),[[5,4],[5,0],[2,0]]);
assert.equal(maximumDroneQuantity([{item:1,quantity:1},{item:2,quantity:2}],0,50,id=>id===1?5:10),6);
console.log('Drone stack splitting and capacity limits passed');
