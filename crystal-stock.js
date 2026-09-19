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
 return (100*(1-projection.item.wear.initialDamage/settings.hitpoints)).toLocaleString('zh-CN',{maximumFractionDigits:2})+'%';
}
export function crystalErrorText(error){
 let diagnostic;
 try{diagnostic=JSON.parse(error.message)?.error}catch{}
 const messages={CRYSTAL_INITIAL_DAMAGE:'损伤必须不小于 0，且小于该晶体耐久上限。',FIT_INVENTORY_DESTROYED_CRYSTAL:'已损毁晶体不能作为现存库存。',FIT_INVENTORY_CRYSTAL_MOUNT:'晶体与当前装备的弹种不匹配。',FIT_INVENTORY_CRYSTAL:'旧晶体堆叠缺少实体身份和损伤，请逐枚重新声明。'};
 return diagnostic?((messages[diagnostic.code]||diagnostic.message)+'（'+diagnostic.code+'）'):error.message;
}
