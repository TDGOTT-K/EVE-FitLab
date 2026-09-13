export const damageNames=['电磁','热能','动能','爆炸'];
export function calculateBaseStats(ship,slots,catalog){
 const lookup=id=>catalog.find(t=>t.id===id),a=ship.attrs,damage=[0,0,0,0],volley=[0,0,0,0];
 let shieldRepair=0,armorRepair=0,capUse=0;const weapons=[],repairs=[],consumers=[];
 for(const s of slots){const t=lookup(s.item),ammo=lookup(s.ammo);if(!t||!s.online)continue;const v=t.attrs,cycle=(v[73]||v[51]||0)/1000;
  if(t.effects.includes(42)&&ammo&&v[51]>0){const part=[114,118,117,116].map(id=>(ammo.attrs[id]||0)*(v[64]||1));part.forEach((value,i)=>{volley[i]+=value;damage[i]+=value/(v[51]/1000)});weapons.push(`${t.name}：${part.reduce((x,y)=>x+y,0).toFixed(2)} HP ÷ ${(v[51]/1000).toFixed(2)} s`)}
  if(cycle&&v[68]){shieldRepair+=v[68]/cycle;repairs.push(`${t.name}：${v[68]} HP ÷ ${cycle} s`)}
  if(cycle&&v[84]){armorRepair+=v[84]/cycle;repairs.push(`${t.name}：${v[84]} HP ÷ ${cycle} s`)}
  if(cycle&&v[6]&&(!t.effects.includes(42)||ammo)){capUse+=v[6]/cycle;consumers.push(`${t.name}：${v[6]} GJ ÷ ${cycle} s`)}
 }
 const layers=[['护盾',a[263],[271,274,273,272]],['装甲',a[265],[267,270,269,268]],['结构',a[9],[113,110,109,111]]].map(([name,hp,ids])=>{const resonance=ids.map(id=>a[id]);const mean=resonance.reduce((x,y)=>x+y,0)/4;return {name,hp,resonance,ehp:hp/mean,mean}});
 return {damage,volley,dps:damage.reduce((x,y)=>x+y,0),alpha:volley.reduce((x,y)=>x+y,0),weapons,layers,shieldRepair,armorRepair,repairs,capUse,consumers,shieldPeak:2.5*a[263]/(a[479]/1000),capPeak:2.5*a[482]/(a[55]/1000)};
}
