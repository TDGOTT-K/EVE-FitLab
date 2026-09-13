export const damageNames=['电磁','热能','动能','爆炸'];
export function normalizeDamage(values){
 if(!Array.isArray(values)||values.length!==4||values.some(v=>typeof v!=='number'||!Number.isFinite(v)||v<0))throw Error('请输入四个非负伤害比例');
 const total=values.reduce((a,b)=>a+b,0);
 if(!Number.isFinite(total)||total<=0)throw Error('至少设置一种来伤，比例不能全部为 0');
 return values.map(v=>v/total);
}
export function savedDamage(values){try{return normalizeDamage(values)}catch{return [.25,.25,.25,.25]}}
export function damageResonance(resists,weights){return resists.reduce((sum,r,i)=>sum+weights[i]*(1-r),0)}
export function effectiveValue(value,resonance){return value===0?0:resonance<=0?Infinity:value/resonance}
export function defenseNumber(value,digits=0){return Number.isFinite(value)?value.toFixed(digits):'∞'}

function allocate(total,weights){
 const sum=weights.reduce((a,b)=>a+b,0),raw=weights.map(w=>total*(sum?w/sum:1/weights.length)),out=raw.map(Math.floor);
 const order=raw.map((v,i)=>i).sort((a,b)=>(raw[b]-out[b])-(raw[a]-out[a])||a-b);
 for(let n=total-out.reduce((a,b)=>a+b,0),i=0;i<n;i++)out[order[i]]++;
 return out;
}
export function damageTenths(profile){return allocate(1000,savedDamage(profile))}
export function redistributeDamage(amounts,locked,index,target){
 const peers=amounts.map((_,i)=>i).filter(i=>i!==index&&!locked[i]);
 if(locked[index]||!peers.length)return [...amounts];
 const budget=amounts[index]+peers.reduce((sum,i)=>sum+amounts[i],0),value=Math.max(0,Math.min(budget,Math.round(target)));
 const next=[...amounts];next[index]=value;
 const shares=allocate(budget-value,peers.map(i=>amounts[i]));
 peers.forEach((i,j)=>next[i]=shares[j]);return next;
}

export function quadrantDamage(x,y){
 x=Math.max(0,Math.min(1,x));y=Math.max(0,Math.min(1,y));
 return allocate(1000,[x*y,(1-x)*y,x*(1-y),(1-x)*(1-y)]);
}

export function splitDamage(topX,bottomX,y){
 const clamp=v=>Math.max(0,Math.min(1,v)),top=Math.round(clamp(y)*1000),bottom=1000-top;
 const em=Math.round(top*clamp(topX)),kinetic=Math.round(bottom*clamp(bottomX));
 return [em,top-em,kinetic,bottom-kinetic];
}
export function damageSplits(amounts){
 const top=amounts[0]+amounts[1],bottom=amounts[2]+amounts[3];
 return {topX:top?amounts[0]/top:.5,bottomX:bottom?amounts[2]/bottom:.5,y:top/1000};
}
