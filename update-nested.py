from pathlib import Path
p=Path('app.js');s=p.read_text(encoding='utf-8');s="import {installExplanations} from './explanations.js';\n"+s
s=s.replace("entries.push(['筛选此槽位装备',()=>toggleFilter(id)]);",'').replace("entries.push(['筛选兼容弹药',()=>toggleFilter(id,true)]);",'')
s=s.replace("const menu=$('#menu');menu.innerHTML", "const menu=$('#menu');if(!entries.length){closeMenu(false);return}menu.innerHTML")
s=s.replace('<input id="fit-name" placeholder="装配名称（可选）">','').replace("name=$('#fit-name').value.trim()||t.name+' · 新装配'", "name=t.name+' · 新装配'")
s=s.replace("Object.entries(n).map", "Object.entries(n).sort(([a],[b])=>(a==='未列入市场')-(b==='未列入市场')).map")
a=s.index("const explain=document.createElement");b=s.index('async function api',a);s=s[:a]+"installExplanations();\n\n"+s[b:];p.write_text(s,encoding='utf-8')
p=Path('engine-view.js');s=p.read_text(encoding='utf-8');a=s.index(' const base=');b=s.index(' const head=',a)
s=s[:a]+''' const base=(name,value,initial,unit)=>{
 const baseDetail={title:name+' · 舰船基础',result:`${initial} ${unit}`,terms:[['舰船',ship.name],['基础属性',`${initial} ${unit}`]],conditions:[['来源','SDE']]};
 const difference={title:name+' · 合计变化',result:`${value-initial>=0?'+ ':''}${(value-initial).toFixed(2)} ${unit}`,terms:[['装配后数值',`${value.toFixed(2)} ${unit}`],['舰船基础',`− ${initial} ${unit}`,null,baseDetail]],conditions:[['单项技能 / 模块分解','尚未接入']]};
 return row(name,`${Number(value).toFixed(1)} ${unit}`,[['舰船基础',`${initial} ${unit}`,null,baseDetail],['技能与装备合计',difference.result,null,difference]])};
'''+s[b:];p.write_text(s,encoding='utf-8')
