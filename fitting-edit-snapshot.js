// Preserve absence as well as values: undeclared inventory is not empty inventory.
const fields=['name','tags','skills','characterName','fighterLoadout','fighterUiMock','outputMetric','loadoutPlan','implantPlan','tacticalModeTypeId','drones','cargo','crystals'];
export function captureEditSnapshot(record,slots){
 const snapshot={slots:structuredClone(slots)};
 for(const key of fields)if(Object.hasOwn(record,key))snapshot[key]=structuredClone(record[key]);
 return snapshot;
}
export function restoreEditSnapshot(record,snapshot){
 for(const key of fields){if(Object.hasOwn(snapshot,key))record[key]=structuredClone(snapshot[key]);else delete record[key];}
 return structuredClone(snapshot.slots);
}

// Candidates and transaction baselines must own every nested inventory/instance.
export function cloneCurrentFit(record,shipId,slots,stateOf){
 const fit=structuredClone({...record,shipId,slots});
 fit.slots=fit.slots.map(s=>s.item?{...s,state:stateOf(s),online:stateOf(s)!=='Offline'}:s);
 return fit;
}
