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
 const needsLoaded=['tubes','reserve'].some(location=>(next[location]||[]).some((row,i)=>{
  if(!row)return false;const id=row.id||'fighter-'+location+'-'+i;
  return items.some(item=>item.source.squadronId===id&&item.metrics?.nominalCycleDps?.reason===finiteReason&&
   (['fighter_primary','fighter_missile_primary'].includes(item.kind)?!(row.excludedAbilities||[]).includes(item.source.officialAbilityId):(row.includedSecondaryAbilities||[]).includes(item.source.officialAbilityId)));
 }));
 return {loadout:next,metric:needsLoaded?'loadedCycleDps':'nominalCycleDps'};
}
