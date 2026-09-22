// Prerequisites come from the official dogma attributes, never inferred from names.
const pairs=[[182,277],[183,278],[184,279],[1285,1286],[1289,1287],[1290,1288]];
export function completeSkillPrerequisites(skills,catalog){
 const types=new Map(catalog.map(t=>[t.id,t])),levels=new Map(skills.map(s=>[s.skillTypeId,s.level])),raised=new Map();
 const queue=[...levels.keys()];
 while(queue.length){const id=queue.shift();if(!(levels.get(id)>0))continue;const type=types.get(id);
  for(const [skillAttr,levelAttr] of pairs){const required=Number(type?.attrs?.[skillAttr]),level=Number(type?.attrs?.[levelAttr]);
   if(!types.has(required)||!Number.isInteger(level)||level<1||level>5|| (levels.get(required)||0)>=level)continue;
   levels.set(required,level);raised.set(required,level);queue.push(required);
  }
 }
 return {skills:[...levels].filter(([,level])=>level>0).map(([skillTypeId,level])=>({skillTypeId,level})),raised:[...raised].map(([skillTypeId,level])=>({skillTypeId,level}))};
}
export function skillPreset(catalog,level){return completeSkillPrerequisites(catalog.filter(t=>t.kind==='skill').map(t=>({skillTypeId:t.id,level})),catalog);}
