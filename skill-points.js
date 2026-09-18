// No skill-point mechanism or source-bound SP snapshot is exposed by the current copy.
// Keep the display slot without inventing a UI-only training formula.
export const skillPointNote='当前引擎未提供技能点总量，且未接入带来源的官网技能点快照；技能等级仍参与装配计算';
export function createSkillPointDisplay(){return ()=> '—';}
