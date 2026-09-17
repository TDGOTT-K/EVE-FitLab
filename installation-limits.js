// Count fitted modules, including offline ones. Replacement frees its own slot.
export function installationLimitReason(item,key,slots,typeById,attributes){
 if(!item||item.kind==='ammo')return '';
 const fitted=slots.filter(s=>s.key!==key&&s.item).map(s=>typeById(s.item)).filter(Boolean);
 for(const [effect,capacity,label] of [[42,'turretHardpointsAvailable','炮塔'],[40,'launcherHardpointsAvailable','发射器']]){
  if(!item.effects?.includes(effect))continue;
  const max=attributes?.[capacity];
  if(!Number.isFinite(max))return label+'挂点上限尚未就绪';
  if(fitted.filter(t=>t.effects?.includes(effect)).length>=max)return label+'挂点已满（上限 '+max+'）';
 }
 for(const [attribute,same,label] of [[1544,t=>t.group===item.group,'同组装备'],[2431,t=>t.id===item.id,'同型号装备']]){
  const related=fitted.filter(same),limits=[item,...related].map(t=>Number(t.attrs?.[attribute])).filter(n=>n>0);
  if(limits.length&&related.length+1>Math.min(...limits))return label+'最多安装 '+Math.min(...limits)+' 件';
 }
 return '';
}
