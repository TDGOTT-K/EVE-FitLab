import assert from 'node:assert/strict';
import {normalizeDamage,damageResonance,effectiveValue} from './defense-profile.js';
assert.deepEqual(normalizeDamage([25,25,25,25]),[.25,.25,.25,.25]);
assert.deepEqual(normalizeDamage([10,0,30,0]),[.25,0,.75,0]);
for(const v of [[0,0,0,0],[-1,2,3,4],[NaN,1,1,1]])assert.throws(()=>normalizeDamage(v));
const resist=[.3,.44,.63,.74],single=normalizeDamage([100,0,0,0]);
assert.equal(damageResonance(resist,single),.7);
assert.ok(Math.abs(effectiveValue(8625,damageResonance(resist,single))-12321.428571428572)<1e-8);
assert.equal(effectiveValue(70,.7),100);
assert.equal(effectiveValue(0,0),0);
assert.equal(effectiveValue(100,0),Infinity);
console.log('Defense profile math passed');
import {damageTenths,redistributeDamage} from './defense-profile.js';
assert.deepEqual(redistributeDamage([250,250,250,250],[false,false,false,false],0,400),[400,200,200,200]);
assert.deepEqual(redistributeDamage([250,250,250,250],[false,true,false,false],0,550),[550,250,100,100]);
assert.deepEqual(redistributeDamage([250,250,250,250],[false,true,true,true],0,550),[250,250,250,250]);
assert.deepEqual(redistributeDamage([1000,0,0,0],[false,false,false,false],0,400),[400,200,200,200]);
assert.deepEqual(redistributeDamage([250,250,250,250],[false,true,false,false],0,1000),[750,250,0,0]);
let values=damageTenths([.3333,.3333,.3334,0]);
for(let n=0;n<1000;n++){
 const locks=[n%3===0,false,n%7===0,false],old=[...values];
 values=redistributeDamage(values,locks,n%4,(n*137)%1001);
 assert.equal(values.reduce((a,b)=>a+b),1000);
 values.forEach((v,i)=>{assert.ok(Number.isInteger(v)&&v>=0);if(locks[i])assert.equal(v,old[i])});
}
console.log('Locked redistribution invariants passed');
import {quadrantDamage} from './defense-profile.js';
assert.deepEqual(quadrantDamage(.5,.5),[250,250,250,250]);
assert.deepEqual(quadrantDamage(.25,.6),[150,450,100,300]);
assert.deepEqual(quadrantDamage(0,0),[0,0,0,1000]);
assert.deepEqual(quadrantDamage(1,1),[1000,0,0,0]);
for(let x=0;x<=100;x++)assert.equal(quadrantDamage(x/100,.337).reduce((a,b)=>a+b),1000);
console.log('Quadrant areas passed');
import {splitDamage,damageSplits} from './defense-profile.js';
assert.deepEqual(splitDamage(1,0,.5),[500,0,0,500]);
assert.deepEqual(splitDamage(.2,.8,.25),[50,200,600,150]);
for(let n=0;n<5000;n++){
 const a=n%1001,b=(n*17)%(1001-a),c=(n*31)%(1001-a-b),v=[a,b,c,1000-a-b-c],p=damageSplits(v);
 assert.deepEqual(splitDamage(p.topX,p.bottomX,p.y),v);
}
console.log('Independent splits: exact round-trip and zero-area cases passed');
