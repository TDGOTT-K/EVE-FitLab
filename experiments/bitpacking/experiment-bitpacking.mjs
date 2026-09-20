// Read-only size experiment; this format is not emitted by the application.
import assert from 'node:assert/strict';
import {gzipSync} from 'node:zlib';
import {writeFile} from 'node:fs/promises';
import {packFit,unpackFit} from '../../fit-binary.js';
const racks=['high','mid','low','rig','subsystem'],states=[null,'Offline','Online','Active','Overload'];
function packed(fit,width,sparse=false){
 const base=packFit({...fit,slots:[]},{notes:true,pilot:true});const slots=fit.slots||[];const bits=[];
 const put=(n,w)=>{assert.ok(Number.isSafeInteger(n)&&n>=0&&n<2**w);for(let i=w-1;i>=0;i--)bits.push(Math.floor(n/2**i)%2)};
 for(const s of slots){const [rack,index]=s.key.split('-');put(racks.indexOf(rack),3);put(+index,10);put(states.indexOf(s.state||(s.online===false?'Offline':null)),3);if(sparse){put(s.item?1:0,1);put(s.ammo?1:0,1);}if(!sparse||s.item)put(s.item||0,width);if(!sparse||s.ammo)put(s.ammo||0,width)}
 const out=new Uint8Array(6+base.length+Math.ceil(bits.length/8));new DataView(out.buffer).setUint32(0,base.length);out[4]=width|(sparse?128:0);out[5]=slots.length;out.set(base,6);bits.forEach((b,i)=>out[6+base.length+(i>>3)]|=b<<(7-(i%8)));return out;
}
function unpack(bytes){const len=new DataView(bytes.buffer,bytes.byteOffset).getUint32(0),width=bytes[4]&63,sparse=!!(bytes[4]&128),n=bytes[5];const p=unpackFit(bytes.slice(6,6+len));let at=(6+len)*8;const get=w=>{let value=0;for(let i=0;i<w;i++){assert.ok(at<bytes.length*8);value=value*2+((bytes[at>>3]>>(7-at%8))&1);at++}return value};for(let i=0;i<n;i++){const rack=get(3),index=get(10),state=get(3);const hasItem=sparse?get(1):1,hasAmmo=sparse?get(1):1;p.slots.push([racks[rack]+'-'+index,hasItem?(get(width)||null):null,hasAmmo?(get(width)||null):null,states[state]])}return p;}
const actual=await fetch('http://127.0.0.1:5207/api/library').then(r=>r.json());const rows=[];
function check(name,fit){const old=packFit(fit,{notes:true,pilot:true});const max=Math.max(1,...fit.slots.flatMap(s=>[s.item||0,s.ammo||0]));const width=Math.ceil(Math.log2(max+1));const fixed=packed(fit,27),adaptive=packed(fit,width),sparse=packed(fit,27,true),adaptiveSparse=packed(fit,width,true);for(const b of [fixed,adaptive,sparse,adaptiveSparse])assert.deepEqual(unpack(b),unpackFit(old));const size=b=>Math.min(b.length,gzipSync(b).length);rows.push({name,slots:fit.slots.length,idBits:width,raw:[old.length,fixed.length,adaptive.length,sparse.length,adaptiveSparse.length],compressed:[size(old),size(fixed),size(adaptive),size(sparse),size(adaptiveSparse)]});}
for(const f of actual)check(f.name,f);
for(const [name,ids] of [['20个重复八位ID',Array(20).fill(99999999)],['20个不同八位ID',Array.from({length:20},(_,i)=>10000000+(i*4327913)%90000000)]])check(name,{shipId:587,name,slots:ids.map((item,i)=>({kind:'high',key:'high-'+i,item,ammo:null,state:'Active'})),skills:[],cargo:[],drones:[]});
const report={columns:['当前变长整数','固定27位ID','按本装配最大ID选择位宽','固定27位+省略空ID','自适应位宽+省略空ID'],rows,totals:rows.slice(0,actual.length).reduce((a,r)=>a.map((n,i)=>n+r.compressed[i]),[0,0,0,0,0])};await writeFile('output/bitpacking-comparison.json',JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
