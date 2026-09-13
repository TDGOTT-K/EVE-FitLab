from pathlib import Path
p=Path('engine-view.js');s=p.read_text(encoding='utf-8');s=s.replace("const row=(name,value,terms)=>`<div class=\"stat-row\" ${name==='锁定目标数'&&Number(value)===raw[192]?'':tip(name,value,terms)}>", "const unchanged=(name,value)=>{const id=({'锁定目标数':192,'回充时间':55,...(!ehp?{'护盾':263,'装甲':265,'结构':9}:{})})[name];return id!=null&&Math.abs(parseFloat(value)-(raw[id]/(id===55?1000:1)))<0.001};\n const row=(name,value,terms)=>`<div class=\"stat-row\" ${unchanged(name,value)?'':tip(name,value,terms)}>")
p.write_text(s,encoding='utf-8')
