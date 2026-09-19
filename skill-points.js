// No skill-point mechanism or source-bound SP snapshot is exposed by the current copy.
// Keep the display slot without inventing a UI-only training formula.
export const skillPointNote='未学习任何技能时为 0 SP；有技能角色的总技能点接口尚未接入，暂显示 —。技能等级仍参与装配计算';
export function createSkillPointDisplay(){
 return character=>Array.isArray(character?.skills)&&character.skills.every(s=>s.level===0)?'0':'—';
}
