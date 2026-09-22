import assert from 'node:assert/strict';
import fs from 'node:fs';
import {completeSkillPrerequisites,skillPreset} from './character-skill-levels.js';
const catalog=[{id:1,kind:'skill',attrs:{}},{id:2,kind:'skill',attrs:{182:1,277:5}},{id:3,kind:'skill',attrs:{182:2,277:4}}];
const result=completeSkillPrerequisites([{skillTypeId:3,level:4}],catalog);
assert.deepEqual(new Map(result.skills.map(s=>[s.skillTypeId,s.level])),new Map([[3,4],[2,4],[1,5]]));
assert.equal(completeSkillPrerequisites([],catalog).skills.length,0);
for(let level=1;level<=5;level++){const p=skillPreset(catalog,level),m=new Map(p.skills.map(s=>[s.skillTypeId,s.level]));assert.equal(m.get(3),level);assert.equal(m.get(1),5);assert.equal(m.get(2),Math.max(level,4));}
const real=JSON.parse(fs.readFileSync('data/full-catalog.json','utf8')).filter(t=>t.kind==='skill');
for(let level=1;level<=5;level++){const p=skillPreset(real,level);assert.equal(p.skills.length,real.length);assert.equal(completeSkillPrerequisites(p.skills,real).raised.length,0);assert.ok(p.skills.every(s=>s.level>=level&&s.level<=5));}
console.log('Skill presets passed: recursive prerequisites, IV hull / V prerequisite, real catalog I–V closure.');
