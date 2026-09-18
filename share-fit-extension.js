// Portable input snapshot only. No account/local-library links or game math.
const pick=(value,keys)=>Object.fromEntries(keys.filter(k=>Object.hasOwn(value||{},k)).map(k=>[k,structuredClone(value[k])]));
const mutation=value=>{if(value==null)return value;const attributes=value.attributes;
 if(!attributes||typeof attributes!=='object'||Array.isArray(attributes)||Object.entries(attributes).some(([k,v])=>!/^\d+$/.test(k)||typeof v!=='number'||!Number.isFinite(v)))throw Error('深渊属性数据无效');
 return {...pick(value,['baseTypeId','mutaplasmidTypeId','ruleVersion']),attributes:structuredClone(attributes)};
};
export function shareFitExtension(fit,options={}){
 const out=pick(fit,['tacticalModeTypeId','outputMetric','capacitorHorizon','defenseMode','damageProfile']);
 out.cargoDeclared=Object.hasOwn(fit,'cargo');
 out.slots=(fit.slots||[]).map(s=>({...pick(s,['key','loadedCharges','abyssalName']),...(s.mutation?{mutation:mutation(s.mutation)}:{})}));
 out.drones=(fit.drones||[]).map(d=>d.mutation?{mutation:mutation(d.mutation)}:{});
 out.cargo=(fit.cargo||[]).map(c=>pick(c,['id']));
 if(Object.hasOwn(fit,'crystals'))out.crystals=(fit.crystals||[]).map(c=>pick(c,['id','typeId','damage','moduleId']));
 if(fit.fighterLoadout)out.fighterLoadout=Object.fromEntries(['tubes','reserve'].map(k=>[k,(fit.fighterLoadout[k]||[]).map(s=>s?pick(s,['id','typeId','quantity','active','excludedAbilities','includedSecondaryAbilities']):null)]));
 if(fit.loadoutPlan){const p=fit.loadoutPlan;out.loadoutPlan={name:p.name||'分享方案',customized:true,
  implants:(p.implants||[]).map(i=>pick(i,['typeId','slot'])),boosters:(p.boosters||[]).map(b=>pick(b,['typeId','slot','enabledSideEffects']))};
  if(p.pilot)out.loadoutPlan.pilot={name:options.pilot?p.pilot.name||'':'分享方案角色',skills:(p.pilot.skills||[]).map(s=>pick(s,['skillTypeId','level']))};
 }else if(fit.implantPlan)out.implantPlan=fit.implantPlan.map(i=>pick(i,['typeId','slot']));
 return out;
}
export function applyShareFitExtension(fit,extra){
 if(!extra||!Array.isArray(extra.slots)||!Array.isArray(extra.drones)||!Array.isArray(extra.cargo)||extra.slots.length!==fit.slots.length||extra.drones.length!==fit.drones.length||extra.cargo.length!==fit.cargo.length)throw Error('分享扩展结构无效');
 for(let i=0;i<fit.slots.length;i++)if(extra.slots[i].key!==fit.slots[i].key)throw Error('分享槽位对应关系无效');
 const candidate={...fit,...pick(extra,['tacticalModeTypeId','outputMetric','capacitorHorizon','defenseMode','damageProfile','crystals','fighterLoadout','loadoutPlan','implantPlan']),
  slots:fit.slots.map((s,i)=>({...s,...pick(extra.slots[i],['loadedCharges','abyssalName','mutation'])})),
  drones:fit.drones.map((d,i)=>({...d,...pick(extra.drones[i],['mutation'])})),
  cargo:fit.cargo.map((c,i)=>({...c,...pick(extra.cargo[i],['id'])}))};
 if(!extra.cargoDeclared)delete candidate.cargo;
 // Run the same explicit projection on inbound data. Native validation follows.
 const clean=shareFitExtension(candidate,{pilot:true});
 const result={...fit,...pick(clean,['tacticalModeTypeId','outputMetric','capacitorHorizon','defenseMode','damageProfile','crystals','fighterLoadout','loadoutPlan','implantPlan']),
  slots:fit.slots.map((s,i)=>({...s,...clean.slots[i]})),drones:fit.drones.map((d,i)=>({...d,...clean.drones[i]})),cargo:candidate.cargo};
 if(!extra.cargoDeclared)delete result.cargo;
 return result;
}
