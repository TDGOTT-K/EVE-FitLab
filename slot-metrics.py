from pathlib import Path
p=Path('app.js');s=p.read_text(encoding='utf-8').replace('report=null,analysisVersion=0','report=null,reportVersion=-1,analysisVersion=0')
pos=s.index('function renderSlots()');s=s[:pos]+'''function slotMetrics(s){
 const siblings=slots.filter(x=>x.kind===s.kind&&x.item===s.item),index=siblings.findIndex(x=>x.key===s.key);
 const m=reportVersion===analysisVersion?report?.snapshot.modules.filter(m=>m.slotKind.toLowerCase()===s.kind&&m.dogmaTypeId===s.item)[index]:null;
 const active=['Active','Overload'].includes(moduleState(s));
 const values=m?[moduleState(s)==='Offline'?0:m.cpuUsage,moduleState(s)==='Offline'?0:m.powergridUsage,active?m.capacitorUsagePerSecond:0,active?(m.damagePerSecond||0)+(m.charge?.damagePerSecondBonus||0):0]:[null,null,null,null];
 return [['CPU','tf','CPU 占用'],['栅格','MW','能量栅格占用'],['耗电','GJ/s','启用时每秒电容消耗'],['DPS','','当前输出伤害 / 秒']].map(([label,unit,title],i)=>`<span class="slot-metric" title="${title}"><span class="slot-metric-label">${label}</span><b>${values[i]==null?'—':new Intl.NumberFormat('zh-CN',{maximumFractionDigits:2}).format(values[i])}</b>${unit?`<span class="slot-metric-unit">${unit}</span>`:''}</span>`).join('');
}
function refreshSlotMetrics(){document.querySelectorAll('[data-module-metrics]').forEach(el=>{const s=slots.find(s=>s.key===el.dataset.moduleMetrics);if(s)el.innerHTML=slotMetrics(s)})}
'''+s[pos:]
s=s.replace("</small>`:''}</span></div>${needsAmmo(t)?", "</small>`:''}${t&&kind!=='rig'?`<span class=\"slot-metrics\" data-module-metrics=\"${s.key}\">${slotMetrics(s)}</span>`:''}</span></div>${needsAmmo(t)?")
s=s.replace("report=result;$('.inspector')", "report=result;reportVersion=version;refreshSlotMetrics();$('.inspector')")
s=s.replace("analysisVersion++;clearTimeout(analysisTimer)", "analysisVersion++;refreshSlotMetrics();clearTimeout(analysisTimer)")
p.write_text(s,encoding='utf-8')
