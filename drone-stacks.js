
export function splitDroneStacks(entries){
 return entries.flatMap(e=>{let remaining=e.quantity,active=Math.min(e.active||0,e.quantity),rows=[];while(remaining>0){const quantity=Math.min(5,remaining),launched=Math.min(quantity,active);rows.push({...e,quantity,active:launched});remaining-=quantity;active-=launched}return rows});
}
export function maximumDroneQuantity(entries,index,capacity,volumeOf){
 const volume=volumeOf(entries[index].item);if(!(volume>0)||!Number.isFinite(capacity))return 0;
 const other=entries.reduce((s,e,i)=>s+(i===index?0:volumeOf(e.item)*e.quantity),0);
 return Math.max(0,Math.floor((capacity-other)/volume+1e-8));
}
