export const skillPointNote='按当前技能等级计算的最低累计 SP；不包含训练中的进度或未分配 SP。由 N 号引擎计算';
export function createSkillPointDisplay(api,onReady=()=>{}){
 const cache=new Map();
 const keyOf=character=>JSON.stringify(character.skills.map(s=>[s.skillTypeId,s.level]).sort((a,b)=>a[0]-b[0]));
 const display=character=>{
  const skills=character?.skills;if(!Array.isArray(skills))return '—';
  const key=keyOf(character);
  if(!cache.has(key)){
   cache.set(key,{pending:true});
   api('skill-points',{skills}).then(result=>{cache.set(key,result);onReady()}).catch(error=>{cache.set(key,{complete:false,total:null,reason:error.message});onReady()});
  }
  const result=cache.get(key);
  return result.complete&&Number.isFinite(result.total)?result.total.toLocaleString('zh-CN'):'—';
 };
 display.note=character=>{
  const result=Array.isArray(character?.skills)?cache.get(keyOf(character)):null;
  if(result?.complete)return skillPointNote;
  const reasons=result?.items?.filter(i=>i.reason).map(i=>i.typeId+': '+i.reason).join('；');
  return skillPointNote+'；'+(result?.pending?'正在读取':result?.reason||reasons||'技能点暂不可用');
 };
 return display;
}
