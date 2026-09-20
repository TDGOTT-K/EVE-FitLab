import {diagnosticText} from './localized-diagnostics.js';
import {getLocale} from './i18n.js';
// Inventory identity operations only. Legality, wear and volume are engine-owned.
export function detachUnmatchedCrystals(fit){
 let moved=0;
 for(const c of fit.crystals||[]){
  if(c.moduleId&&!fit.slots.some(s=>s.key===c.moduleId&&s.item&&s.ammo===c.typeId)){
   c.moduleId=null;moved++;
  }
 }
 return moved;
}
export function exchangeCrystalSlots(crystals,source,target){
 for(const c of crystals||[]){
  if(c.moduleId===source)c.moduleId=target;
  else if(c.moduleId===target)c.moduleId=source;
 }
}
export function mountCrystal(fit,id,key){
 const crystal=fit.crystals?.find(c=>c.id===id),slot=fit.slots.find(s=>s.key===key);
 if(!crystal||!slot?.item)throw Error('晶体或目标装备已不存在');
 if(crystal.moduleId&&crystal.moduleId!==key)throw Error('请先卸下已装载的晶体');
 for(const c of fit.crystals)if(c.moduleId===key)c.moduleId=null;
 crystal.moduleId=key;slot.ammo=crystal.typeId;delete slot.loadedCharges;
}
export function crystalProjection(report,id){
 return report?.native?.inventory?.crystals?.find(c=>c.item?.crystal?.id===id);
}
export function crystalWearText(projection){
 if(!projection)return '损伤待计算';
 const settings=projection.wearInput.settings;
 return (100*(1-projection.item.wear.initialDamage/settings.hitpoints)).toLocaleString(getLocale(),{maximumFractionDigits:2})+'%';
}
export function crystalErrorText(error){
 let diagnostic=error.diagnostic?.error;
 if(!diagnostic)try{diagnostic=JSON.parse(error.message)?.error}catch{}
 return diagnostic?diagnosticText(diagnostic):error.message;
}
