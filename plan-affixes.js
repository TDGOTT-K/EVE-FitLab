import {getLocale} from './i18n.js';
const signed=value=>(value>0?'+':'')+value.toLocaleString(getLocale(),{maximumFractionDigits:3});
export function renderPlanAffixes(root,summary,{find}){
 if(!root)return;root.replaceChildren();root.title='';
 if(!summary){root.textContent='加成暂不可用';return;}
 const items=[...summary.items].sort((a,b)=>Number(a.domain==='charID'&&a.function==='ItemModifier')-Number(b.domain==='charID'&&b.function==='ItemModifier'));
 for(const item of items){
  const row=document.createElement('div');row.className='plan-affix-row '+item.direction;row.tabIndex=0;
  const label=document.createElement('span');label.textContent=item.name;
  if(!['舰船','角色'].includes(item.scope)){const scope=document.createElement('small');scope.textContent=item.scope;label.append(scope);}
  const value=document.createElement('b');
  if(item.state!=='available')value.textContent='—';
  else{
   const parts=[];
   if(item.percent!==0&&Number.isFinite(item.percent))parts.push(signed(item.percent)+'%');
   if(item.additive!==0&&Number.isFinite(item.additive))parts.push(signed(item.additive)+(item.unit?' '+item.unit:''));
   value.textContent=parts.join(' · ')||'0';
  }
  row.title=item.name+' · '+item.scope+'\n'+item.sources.map(source=>{
   const type=find(source.instanceId.startsWith('implant.')?'implants':'boosters',source.typeId);
   return (type?.name||String(source.typeId))+(source.sideEffect?'（已启用副作用）':'')+(source.trace?.steps?.length?' · 已计入套装/角色修正':'');
  }).join('\n')+(item.reason?'\n不可用：'+item.reason:'')+'\n同一作用条件内的方案合计；不同条件不直接相加。';
  row.append(label,value);root.append(row);
 }
 if(!summary.items.length)root.textContent=summary.complete?'暂无加成':'部分效果暂不可用';
 if(!summary.complete){const note=document.createElement('small');note.className='plan-affix-note';note.textContent='部分效果未计入';note.title=summary.unavailable.join('\n');root.append(note);}
}
