// EVF2 payload: unsigned LEB128 integers, UTF-8 strings, packed rack/state,
// ID-delta skill groups. No catalog-position IDs or account/local identifiers.
const racks=['high','mid','low','rig','subsystem'];
const states=[null,'Offline','Online','Active','Overload'];
export function packFit(fit,options={}){
 const bytes=[];const u=n=>{if(!Number.isSafeInteger(n)||n<0||n>0xffffffff)throw Error('装配整数超出范围');do{const b=n%128;n=Math.floor(n/128);bytes.push(b|(n?128:0))}while(n)};
 const str=(s,max)=>{if(typeof s!=='string'||s.length>max)throw Error('分享文字过长');const b=new TextEncoder().encode(s);u(b.length);bytes.push(...b)};
 u(fit.shipId);str(fit.name,120);str(options.notes?fit.notes||'':'',4000);str(options.pilot?fit.characterName||'':'',200);
 const tags=fit.tags||[];if(tags.length>100)throw Error('标签过多');u(tags.length);tags.forEach(s=>str(s,120));
 const slots=fit.slots||[];if(slots.length>100)throw Error('槽位过多');u(slots.length);
 for(const s of slots){const m=/^(high|mid|low|rig|subsystem)-(\d{1,3})$/.exec(s.key);if(!m||m[1]!==s.kind)throw Error('槽位无效');const state=s.state||(s.online===false?'Offline':null),si=states.indexOf(state);if(si<0)throw Error('装备状态无效');u(racks.indexOf(m[1])|(si<<3));u(+m[2]);u(s.item||0);u(s.ammo||0);}
 for(const key of ['drones','cargo']){const rows=fit[key]||[];if(rows.length>200)throw Error('舱内物品过多');u(rows.length);for(const r of rows){u(r.item);u(r.quantity);if(key==='drones')u(r.active||0)}}
 const levels=new Map();for(const s of fit.skills||[]){if(!Number.isInteger(s.level)||s.level<0||s.level>5)throw Error('技能等级无效');if(!levels.has(s.level))levels.set(s.level,[]);levels.get(s.level).push(s.skillTypeId)}if((fit.skills||[]).length>2000)throw Error('技能过多');u(levels.size);
 for(const [level,ids] of [...levels].sort((a,b)=>a[0]-b[0])){u(level);ids.sort((a,b)=>a-b);u(ids.length);let previous=0;for(const id of ids){u(id-previous);previous=id}}
 return Uint8Array.from(bytes);
}
export function unpackFit(bytes){
 let at=0;const u=()=>{let n=0,mul=1;for(let i=0;i<5;i++){if(at>=bytes.length)throw Error('二进制数据不完整');const b=bytes[at++];n+=(b&127)*mul;if(n>0xffffffff)throw Error('二进制整数溢出');if(!(b&128))return n;mul*=128}throw Error('二进制整数无效')};
 const count=max=>{const n=u();if(n>max)throw Error('二进制列表过大');return n};const str=max=>{const n=count(max*4);if(at+n>bytes.length)throw Error('分享文字不完整');const s=new TextDecoder('utf-8',{fatal:true}).decode(bytes.subarray(at,at+n));at+=n;if(s.length>max)throw Error('分享文字过长');return s};
 const p={v:1,ship:u(),name:str(120),notes:str(4000),pilot:str(200),tags:[],slots:[],drones:[],cargo:[],skills:[]};
 for(let n=count(100);n;n--)p.tags.push(str(120));
 for(let n=count(100);n;n--){const bits=u(),rack=bits&7,st=bits>>3;if(rack>=racks.length||st>=states.length)throw Error('二进制槽位无效');const index=count(999);p.slots.push([racks[rack]+'-'+index,u()||null,u()||null,states[st]])}
 for(const key of ['drones','cargo'])for(let n=count(200);n;n--){const row=[u(),u()];if(key==='drones')row.push(u());p[key].push(row)}
 const levels=new Set();for(let n=count(6);n;n--){const level=count(5);if(levels.has(level))throw Error('重复技能分组');levels.add(level);let id=0;for(let k=count(2000);k;k--){id+=u();if(id>0xffffffff||p.skills.length>=2000)throw Error('技能数据过大');p.skills.push([id,level])}}
 if(at!==bytes.length)throw Error('二进制数据含未知尾部');return p;
}
