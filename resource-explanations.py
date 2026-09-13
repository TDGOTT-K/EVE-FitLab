from pathlib import Path
p=Path('engine-view.js');s=p.read_text(encoding='utf-8');a=s.index(' const base=');b=s.index(' const head=',a);old=s[a:b];start=old.index(' if(!trace');body=old[start:];body=body.replace("return row(name,`${Number(value).toFixed(1)} ${unit}`,[['计算记录',trace?'后处理尚未完整追踪':'此属性尚无执行记录']]);", "return {title:name,result:`${Number(value).toFixed(1)} ${unit}`,terms:[['计算记录',trace?'后处理尚未完整追踪':'此属性尚无执行记录']],conditions:context};")
body=body.replace("return row(name,`${Number(value).toFixed(1)} ${unit}`,terms)};", "return {title:name,result:`${Number(value).toFixed(1)} ${unit}`,terms,conditions:context};\n}")
helper="export function attributeDetail(name,value,unit,attribute,a,catalog=[],pilot='',scale=1){\n const trace=a.attributeTraces?.[attribute],context=[['角色',pilot||'无技能'],['来源','Dogma 执行记录']];\n"+body
replacement=""" const base=(name,value,initial,unit)=>{const attribute={'最大速度':'maxVelocity','信号半径':'signatureRadius','容量':'capacitorCapacity','锁定距离':'maxTargetRange','扫描分辨率':'scanResolution'}[name];const detail=attributeDetail(name,value,unit,attribute,a,catalog,pilot,unit==='km'?1000:1);return `<div class="stat-row" tabindex="0" data-explain="${esc(JSON.stringify(detail))}"><span>${name}</span><b>${detail.result}</b></div>`};
"""
s=s[:a]+replacement+s[b:];s=helper+'\n'+s;p.write_text(s,encoding='utf-8')
p=Path('app.js');s=p.read_text(encoding='utf-8').replace('import {mountEngineStats}', 'import {mountEngineStats,attributeDetail}')
a=s.index('function renderResources(){');b=s.index('\nfunction hardpoints',a)
s=s[:a]+'''function renderResources(){if(!report){$('#resources').innerHTML='<p class="profile-note">等待计算服务…</p>';return}const a=report.attributes;
 $('#resources').innerHTML=[['CPU',a.cpuUsed,a.cpuAvailable,'tf','cpuOutput','cpuUsage'],['能量栅格',a.powergridUsed,a.powergridAvailable,'MW','powerOutput','powergridUsage']].map(([name,value,max,unit,attr,field])=>{
 const contributions=report.snapshot.modules.filter(m=>m.state!=='Offline'&&m[field]>0).map(m=>[byId(m.dogmaTypeId)?.name||m.name,`+ ${m[field].toFixed(2)} ${unit}`]);
 const sum=report.snapshot.modules.filter(m=>m.state!=='Offline').reduce((n,m)=>n+(m[field]||0),0);
 const usage={title:name+' · 占用',result:`${value.toFixed(2)} ${unit}`,terms:Math.abs(sum-value)<0.01?(contributions.length?contributions:[['已安装在线模块','0']]):[['模块明细','当前接口未提供可核对的完整分解']],conditions:[['来源','Dogma 装配结果'],['离线模块','不占用资源']]};
 const capacity=attributeDetail(name+' · 上限',max,unit,attr,a,catalog,fitRecord.characterName);
 const detail={title:name,result:`${value.toFixed(1)} / ${max} ${unit}`,terms:[['已用',`${value.toFixed(2)} ${unit}`,null,usage],['上限',`${max} ${unit}`,null,capacity],['剩余',`${(max-value).toFixed(2)} ${unit}`]],conditions:[['角色',fitRecord.characterName||'无技能']]};
 return `<div class="meter ${value>max?'over':''}" tabindex="0" data-explain="${esc(JSON.stringify(detail))}"><label>${name}<span>${value.toFixed(1)} / ${max} ${unit}</span></label><progress aria-label="${name}占用" value="${Math.min(value,max)}" max="${max||1}"></progress></div>`}).join('')}
'''+s[b:];p.write_text(s,encoding='utf-8')
p=Path('explanations.js');s=p.read_text(encoding='utf-8').replace("observe(document.querySelector('#ship-stats')", "observe(document.querySelector('.inspector')");p.write_text(s,encoding='utf-8')
