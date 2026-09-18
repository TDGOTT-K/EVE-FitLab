import assert from 'node:assert/strict';
import {nativeAttributeDetail} from './native-attribute-detail.js';
const inspection={ruleVersion:'fit-attribute-inspection-v1'};
const blocked={query:{itemId:'module.gun',attributeId:64},state:'blocked_static_dependencies',reason:'UNSUPPORTED_EFFECT',trace:null};
const unavailable=nativeAttributeDetail(blocked,inspection,'伤害',()=>{throw Error('null formatted as number')},[]);
assert.equal(unavailable.value,'— · 依赖尚未支持的效果');assert.equal(unavailable.direction,'');assert(unavailable.detail.conditions.some(x=>x[1]==='UNSUPPORTED_EFFECT'));
const zero={...blocked,state:'available',reason:null,trace:{baseValue:1,value:0,steps:[],origin:'typeDogma'},metadata:{definition:{highIsGood:false}}};
const available=nativeAttributeDetail(zero,inspection,'需求',String,[]);assert.equal(available.value,'0');assert.equal(available.direction,'value-improved');
console.log('Native attribute null, zero, reason and direction passed');
