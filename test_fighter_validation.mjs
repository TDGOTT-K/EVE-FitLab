import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import {validateFighterLoadout} from './fighter-validation.js';
let late,signal;
await assert.rejects(validateFighterLoadout(s=>{signal=s;return new Promise(resolve=>late=resolve)},{timeoutMs:5}),/超时/);
assert(signal.aborted);late({issues:[]});
assert.deepEqual(await validateFighterLoadout(()=>({issues:[]})),{issues:[]});
await assert.rejects(validateFighterLoadout(()=>{throw Error('offline')}),/offline/);
// Execute the actual controller, including preparation, guard and finally.
const source=fs.readFileSync(new URL('./fighter-ui.js',import.meta.url),'utf8');
const code=source.slice(source.indexOf(' const change=async'),source.indexOf(" const host=document.createElement('section')"))+';globalThis.change=change;';
const context={host:{isConnected:true},state:{tubes:[null,null],reserve:[]},fit:{},structuredClone,
 validateFighterLoadout:read=>validateFighterLoadout(read,{timeoutMs:5}),validate:()=>new Promise(()=>{}),
 say:message=>messages.push(message),mutate:fn=>{writes++;fn()},defaultFighterOutput:()=>null,fighterIssueMessage:()=>'',};
const messages=[];let writes=0;
vm.createContext(context);vm.runInContext(code,context);
await context.change(()=>{throw Error('prepare failed')});assert.equal(context.host._busy,false);
await context.change(()=>{});assert.equal(context.host._busy,false);assert.equal(writes,0);
let resolveLate;context.validate=()=>new Promise(r=>resolveLate=r);
await context.change(()=>{});resolveLate({issues:[]});await Promise.resolve();assert.equal(writes,0);
context.validate=async()=>({issues:[]});await context.change(()=>{});assert.equal(writes,1);
await context.change(()=>{});assert.equal(writes,2);assert.equal(context.host._busy,false);
context.validate=async()=>({issues:[{code:'FIGHTER_LOADED_LIMIT_EXCEEDED'}]});await context.change(()=>{});assert.equal(writes,2);assert.equal(context.host._busy,false);
context.host.isConnected=false;context.validate=async()=>({issues:[]});await context.change(()=>{});assert.equal(writes,2);
console.log('Fighter validation: preparation failure, read timeout, cancellation, late response, retry, sequential edits, quota rejection and detached view passed.');
