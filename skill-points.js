import {getLocale} from './i18n.js';
export const skillPointNote='按已学等级计算，不含未完成等级的训练进度';
export function createSkillPointDisplay(catalog){
 const skillRanks=new Map(catalog.filter(t=>t.kind==='skill').map(t=>[t.id,t.attrs?.[275]]));
 return c=>{
  let total=0;
  for(const skill of c.skills){if(skill.level<=0)continue;const rank=skillRanks.get(skill.skillTypeId);if(!Number.isFinite(rank)||rank<=0)return '—';total+=Math.ceil(250*rank*2**(2.5*(skill.level-1)));}
  return total.toLocaleString(getLocale());
 };
}
