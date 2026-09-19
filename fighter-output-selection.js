const finiteReason='FINITE_ABILITY_USE_LOADED_CYCLE_BASIS';
const usable=r=>r?.state==='available'&&Number.isFinite(r.value)&&!!r.aggregationKey;
export function fighterOutputOption(contribution,metric='nominalCycleDps'){
 const reading=contribution?.metrics?.[metric],loaded=contribution?.metrics?.loadedCycleDps;
 return {reading,available:usable(reading)||(reading?.reason===finiteReason&&usable(loaded)),
  finite:contribution?.metrics?.nominalCycleDps?.reason===finiteReason};
}
export function toggleFighterOutput(loadout,items,list,index,abilityId,primary){
 const next=structuredClone(loadout),entry=next[list][index],field=primary?'excludedAbilities':'includedSecondaryAbilities';
 const selected=new Set(entry[field]||[]);if(selected.has(abilityId))selected.delete(abilityId);else selected.add(abilityId);entry[field]=[...selected];
 return {loadout:next,metric:fighterOutputMetric(next,items)};
}
function fighterOutputMetric(next,items){
 const needsLoaded=['tubes','reserve'].some(location=>(next[location]||[]).some((row,i)=>{
  if(!row)return false;const id=row.id||'fighter-'+location+'-'+i;
  return items.some(item=>item.source.squadronId===id&&item.metrics?.nominalCycleDps?.reason===finiteReason&&
   (['fighter_primary','fighter_missile_primary'].includes(item.kind)?!(row.excludedAbilities||[]).includes(item.source.officialAbilityId):(row.includedSecondaryAbilities||[]).includes(item.source.officialAbilityId)));
 }));
 return needsLoaded?'loadedCycleDps':'nominalCycleDps';
}
export function defaultFighterOutput(loadout,items,squadronId){
 const next=structuredClone(loadout);
 const entry=[...(next.tubes||[]),...(next.reserve||[])].find(row=>row?.id===squadronId);
 if(entry&&!Object.hasOwn(entry,'includedSecondaryAbilities')){
  entry.includedSecondaryAbilities=[...new Set(items.filter(item=>
   item.source.squadronId===squadronId&&item.kind.startsWith('fighter_')&&
   !['fighter_primary','fighter_missile_primary'].includes(item.kind)&&
   Number.isInteger(item.source.officialAbilityId)&&fighterOutputOption(item).available
  ).map(item=>item.source.officialAbilityId))];
 }
 return {loadout:next,metric:fighterOutputMetric(next,items)};
}
