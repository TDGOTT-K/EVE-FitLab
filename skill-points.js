export const skillPointNote='按当前技能等级计算的最低累计 SP；不包含训练中的进度或未分配 SP。由 N 号引擎计算';
export function createSkillPointDisplay(api,onReady=()=>{}){
 const cache=new Map();
 const keyOf=character=>character.skillSnapshot?JSON.stringify(character.skillSnapshot):JSON.stringify(character.skills.map(s=>[s.skillTypeId,s.level]).sort((a,b)=>a[0]-b[0]));
 const display=character=>{
  const skills=character?.skills;if(!Array.isArray(skills))return '—';
  const key=keyOf(character);
  if(!cache.has(key)){
   cache.set(key,{pending:true});
   api(character.skillSnapshot?'character-skills':'skill-points',character.skillSnapshot?{snapshot:character.skillSnapshot}:{skills}).then(result=>{cache.set(key,result);onReady()}).catch(error=>{cache.set(key,{complete:false,total:null,reason:error.message});onReady()});
  }
  const result=cache.get(key);
  if(character.skillSnapshot)return result.totalSp?.state==='available'&&Number.isFinite(result.totalSp.value)?result.totalSp.value.toLocaleString('zh-CN'):'—';
  return result.complete&&Number.isFinite(result.total)?result.total.toLocaleString('zh-CN'):'—';
 };
 display.note=character=>{
  const result=Array.isArray(character?.skills)?cache.get(keyOf(character)):null;
  if(character?.skillSnapshot){
   const source=character.skillSnapshot.source;
   return 'EVE 官网已训练总 SP，采用 ESI 返回值；未分配 SP 单列、不计入总量。快照时间：'+source.fetchedAt+'；未分配 SP：'+(result?.unallocatedSp?.value??'未提供')+'；'+(result?.pending?'正在读取':result?.reason||result?.totalSp?.reason||'来源：'+source.url)+(result?.totalMatchesRecordedSkills===false?'；官网总量与逐项合计不一致，保留官网总量':'');
  }
  if(result?.complete)return skillPointNote;
  const reasons=result?.items?.filter(i=>i.reason).map(i=>i.typeId+': '+i.reason).join('；');
  return skillPointNote+'；'+(result?.pending?'正在读取':result?.reason||reasons||'技能点暂不可用');
 };
 return display;
}
