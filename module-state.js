// Catalog capabilities come from the pinned engine SDE default effect.
export function effectiveModuleState(slot,type){
 const state=slot.state||(slot.online===false?'Offline':type?.canActivate?'Active':'Online');
 // Repair legacy drafts that mistook the common online effect for activation.
 return state;
}
