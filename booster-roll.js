// Each possible side effect is an independent SDE-base-probability trial.
export function rollBoosterSideEffects(entries,catalog,random=Math.random){
 const byId=new Map(catalog.map(t=>[t.id,t]));
 return entries.map(entry=>{const t=byId.get(entry.typeId);if(!t)throw Error('药剂资料缺失，无法随机服用');
  for(const e of t.sideEffects)if(!Number.isFinite(e.chance)||e.chance<0||e.chance>1)throw Error('药剂副作用概率缺失，无法随机服用');
  return {...entry,enabledSideEffects:t.sideEffects.filter(e=>random()<e.chance).map(e=>e.id)};
 });
}
