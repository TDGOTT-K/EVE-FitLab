import {getLocale} from './i18n.js';
const local=value=>typeof value==='string'?value:value?.[getLocale()]||value?.[getLocale()==='zh-CN'?'zh':'en']||value?.en||'';
export function skillBonusRows(data,level){
 const mods=(data.effects||[]).flatMap(e=>e.modifierInfo||[]);
 // Only declared skill-level multiplication is projected; unrelated attributes stay excluded.
 const scaled=new Set(mods.filter(m=>m.domain==='itemID'&&m.func==='ItemModifier'&&[276,280].includes(m.modifyingAttributeID)&&m.operation===0).map(m=>m.modifiedAttributeID));
 return (data.attributes||[]).filter(a=>scaled.has(a.id)&&Number.isFinite(a.value)).map(a=>{
  let value=a.value*level,unit=local(data.units?.[a.unitID]?.displayName);
  if([105,121,124,205].includes(a.unitID))unit='%';
  if(a.unitID===127){value*=100;unit='%';}
  if([108,111].includes(a.unitID)){value=(1-value)*100;unit='%';}
  if(a.unitID===109){value=(value-1)*100;unit='%';}
  if(a.unitID===101){value/=1000;unit='s';}
  return {name:local(a.displayName)||a.name,value,unit,sourceAttributeId:a.id};
 });
}
export function installSkillHover(api){
 const cache=new Map();let token=0;
 const tip=document.createElement('div');tip.className='skill-hover';tip.role='tooltip';tip.hidden=true;document.body.append(tip);
 const hide=()=>{token++;tip.hidden=true;};
 async function show(anchor,id,name,level){const request=++token;tip.replaceChildren();tip.hidden=false;
  const heading=document.createElement('strong');heading.textContent=name+(level?' · '+level+' 级总加成':' · 每级加成');tip.append(heading);
  const body=document.createElement('div');body.textContent='读取中…';tip.append(body);
  const position=()=>{const r=anchor.getBoundingClientRect();tip.style.left=Math.max(10,Math.min(r.left,innerWidth-tip.offsetWidth-10))+'px';tip.style.top=Math.max(10,Math.min(r.top-tip.offsetHeight-10,innerHeight-tip.offsetHeight-10))+'px';};position();
  try{if(!cache.has(id))cache.set(id,api('items/'+id).catch(e=>{cache.delete(id);throw e;}));const data=await cache.get(id);if(request!==token)return;
   body.replaceChildren();const rows=skillBonusRows(data,level||1);
   for(const row of rows){const p=document.createElement('p');p.textContent=row.name+' '+(row.value>0?'+':'')+Number(row.value.toFixed(6))+row.unit;body.append(p);}
   if(!level||!rows.length){const p=document.createElement('p');p.textContent=local(data.description).replace(/<br\s*\/?\s*>/gi,'\n').replace(/<[^>]*>/g,'');body.append(p);}
   const note=document.createElement('small');note.textContent=level?(rows.length?'本技能等级累计修正；实际装配效果另受装备与其他技能影响。':'未提供可直接累计的结构化加成；以上为官方每级说明。'):'官方每级说明';body.append(note);position();
  }catch(e){if(request===token)body.textContent='读取失败：'+e.message;}
 }
 document.addEventListener('scroll',hide,true);window.addEventListener('blur',hide);window.addEventListener('hashchange',hide);
 return {bind(anchor,id,name,level){anchor.onmouseenter=()=>show(anchor,id,name,level);anchor.onmouseleave=hide;anchor.onfocus=()=>show(anchor,id,name,level);anchor.onblur=hide;},hide};
}
