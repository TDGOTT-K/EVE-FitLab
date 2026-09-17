export function transferSlots(slots,sourceKey,targetKey,ammo,compatible){
 const source=slots.find(s=>s.key===sourceKey),target=slots.find(s=>s.key===targetKey);
 if(!source||!target||source===target)return null;
 if(ammo){
  if(!source.ammo||!compatible(source.ammo,target.key)||(target.ammo&&!compatible(target.ammo,source.key)))return null;
 }else if(!source.item||!compatible(source.item,target.key)||(target.item&&!compatible(target.item,source.key)))return null;
 const next=structuredClone(slots),a=next.find(s=>s.key===sourceKey),b=next.find(s=>s.key===targetKey);
 if(ammo)[a.ammo,b.ammo]=[b.ammo||null,a.ammo];
 else{
  const {key:ak,kind:at,...av}=a,{key:bk,kind:bt,...bv}=b;
  next[next.indexOf(a)]={...bv,key:ak,kind:at};next[next.indexOf(b)]={...av,key:bk,kind:bt};
 }
 return next;
}
