from pathlib import Path
p=Path('engine-view.js');s=p.read_text(encoding='utf-8');s=s.replace('ehp,onToggle){','ehp,onToggle,catalog=[]){');s=s.replace("['逐项加成溯源','尚未接入']", "['追踪来源','引擎执行记录']")
a=s.index(' const base=');b=s.index(' const head=',a)
s=s[:a]+''' const base=(name,value,initial,unit)=>{
 const attribute={'最大速度':'maxVelocity','信号半径':'signatureRadius','容量':'capacitorCapacity','锁定距离':'maxTargetRange','扫描分辨率':'scanResolution'}[name],scale=unit==='km'?1000:1,trace=a.attributeTraces?.[attribute];
 if(!trace?.isComplete||Math.abs(trace.finalValue/scale-value)>0.001)return row(name,`${Number(value).toFixed(1)} ${unit}`,[['计算记录',trace?'后处理尚未完整追踪':'此属性尚无执行记录']]);
 const terms=[['舰船基础',`${(trace.baseValue/scale).toFixed(2)} ${unit}`]];
 for(const step of trace.steps){
  const source=catalog.find(t=>t.id===step.sourceTypeId)?.name||'未命名来源',isSkill=step.sourceKind==='Skill',op=step.operation;
  const operation=['ModAdd','ModSub'].includes(op)?`${step.appliedValue>=0?'+ ':''}${(step.appliedValue/scale).toFixed(3)} ${unit}`:['PreAssign','PostAssign'].includes(op)?`= ${(step.appliedValue/scale).toFixed(3)} ${unit}`:`× ${step.appliedValue.toFixed(5)}`;
  const sourceTerms=isSkill?(step.modifyingAttributeId===280?[['技能等级',step.skillLevel]]:[['每级修正',step.sourceBaseValue],['技能等级',`× ${step.skillLevel}`]]):[['引擎求值',step.sourceValue]];
  const sourceDetail={title:source+' · 修正值',result:step.sourceValue,terms:sourceTerms,conditions:[['来源',isSkill?'技能等级运算':'Dogma 属性求值'],...(!isSkill?[['来源属性内部运算','尚未追踪']]:[])]};
  const detail={title:source,result:`${(step.after/scale).toFixed(3)} ${unit}`,terms:[['执行前',`${(step.before/scale).toFixed(3)} ${unit}`],['来源修正值',step.sourceValue,null,sourceDetail],['叠加惩罚系数',step.penaltyMultiplier.toFixed(6)],['实际运算',operation]],conditions:[['执行序号',step.order],['来源状态',isSkill?`技能 ${step.skillLevel} 级`:({'Active':'启动','Online':'在线','Passive':'被动','Offline':'离线','Overload':'超载'}[step.sourceState]||step.sourceState)]]};
  terms.push([source+(isSkill?` ${step.skillLevel} 级`:''),operation,'source',detail]);
 }
 return row(name,`${Number(value).toFixed(1)} ${unit}`,terms)};
'''+s[b:];p.write_text(s,encoding='utf-8')
p=Path('app.js');s=p.read_text(encoding='utf-8');s=s.replace('defenseEhp=!defenseEhp;renderEngineStats()});', 'defenseEhp=!defenseEhp;renderEngineStats()},catalog);');p.write_text(s,encoding='utf-8')
