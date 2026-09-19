// Discovery is metadata, never an approval of the current fit.
const cache=new Map();
export function discoverType(id){
 if(!cache.has(id))cache.set(id,fetch('./api/capabilities/'+id).then(async r=>{
  const data=await r.json();if(!r.ok)throw Error(data.error||'能力查询失败');return data;
 }).catch(e=>{cache.delete(id);throw e}));
 return cache.get(id);
}
export async function hydrateType(type){
 if(!type)return;
 const data=await discoverType(type.id);type.capabilities=data.capabilities;
 const m=type.capabilities?.moduleConfiguration;
 if(m){
  const known=s=>s?.state==='requires_fit_validation'||s?.state==='available';
  type.canActivate=m.states.active?.state==='unknown'?null:known(m.states.active);
  type.canOverload=m.states.overheated?.state==='unknown'?null:known(m.states.overheated);
 }
 return type.capabilities;
}
export const subsystemSlots=ship=>ship?.capabilities?.hullConfiguration?.subsystemSlots||[];
export const modeCandidates=ship=>ship?.capabilities?.hullConfiguration?.tacticalModes||[];
