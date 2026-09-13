from pathlib import Path
p=Path('engine-view.js');s=p.read_text(encoding='utf-8')
s=s.replace('const bars=(values,title,baselines=null)=>','const bars=(values,title,baselines=null,components=null)=>')
s=s.replace("[['比例',(v*100).toFixed(2)+'%']])", "[['比例',(v*100).toFixed(2)+'%'],...(components?[[names[i]+' DPS',components[i].toFixed(2)]]:[])])")
s=s.replace("<b>${(v*100).toFixed(0)}%</b></div></div>", "<b>${(v*100).toFixed(0)}%</b></div>${components?`<span class=\"damage-component\" aria-label=\"${names[i]} DPS\"><b>${components[i].toFixed(1)}</b><small>DPS</small></span>`:''}</div>")
s=s.replace("),'伤害占比')}", "),'伤害占比',null,keys.map(k=>a.appliedDamageProfilePerSecond[k]||0))}")
pos=s.index(' root.innerHTML=');s=s[:pos]+''' const repair=(name,rate,layer,terms)=>{
 const output=ehp?(rate===0?0:rate/layer.mean):rate;
 return row(name,output.toFixed(2)+(ehp?' EHP/s':' HP/s'),[...terms,...(ehp?[['原始修量',rate.toFixed(2)+' HP/s'],['加权伤害共振',`÷ ${layer.mean.toFixed(6)}`],['来伤分布','电 / 热 / 动 / 爆：各 25%']]:[])])};
'''+s[pos:]
s=s.replace("${row('主动回盾',Math.max(0,a.shieldRepairPerSecond-a.passiveShieldRechargePerSecond).toFixed(2)+' HP/s',contributions('shieldRepairPerSecond'))}", "${repair('主动回盾',Math.max(0,a.shieldRepairPerSecond-a.passiveShieldRechargePerSecond),layers[0],contributions('shieldRepairPerSecond'))}")
s=s.replace("${row('装甲维修',a.armorRepairPerSecond.toFixed(2)+' HP/s',contributions('armorRepairPerSecond'))}", "${repair('装甲维修',a.armorRepairPerSecond,layers[1],contributions('armorRepairPerSecond'))}")
s=s.replace("${row('被动回盾 · 峰值',shieldPeak.toFixed(2)+' HP/s',[", "${repair('被动回盾 · 峰值',shieldPeak,layers[0],[")
p.write_text(s,encoding='utf-8')
