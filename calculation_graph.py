
import math
def literal(title,value,unit='',source='计算常量'):
 return {'title':title,'value':value,'unit':unit,'source':source,'complete':True}
def attribute(obj,key,title,unit='',scale=1):
 trace=obj.get('attributeTraces',{}).get(key)
 if trace is None:return {'title':title,'unit':unit,'complete':False,'missing':'缺少 '+key+' 的属性执行记录'}
 return {'title':title,'value':trace['finalValue']/scale,'unit':unit,'trace':trace,'scale':scale,'complete':trace.get('isComplete',False),'missing':None if trace.get('isComplete') else key+' 的后处理未追踪'}
def formula(title,op,inputs,unit='',expected=None):
 complete=all(n.get('complete') and isinstance(n.get('value'),(int,float)) for n in inputs)
 value=None
 if complete:
  v=[n['value'] for n in inputs]
  if op=='sum':value=sum(v)
  elif op=='multiply':value=math.prod(v)
  elif op=='divide' and v[1]!=0:value=v[0]/v[1]
  else:complete=False
 node={'title':title,'operation':op,'inputs':inputs,'unit':unit,'value':value,'complete':complete}
 if expected is not None:
  node['value']=expected
  if value is not None and abs(value-expected)>max(.004,abs(expected)*1e-5):
   node['complete']=False;node['missing']='公式结果与引擎投射不同';node['formulaValue']=value
 return node
def attach_calculations(report):
 def item(m,drone=False):
  charge=m.get('charge') or {};active=drone or m.get('state') in ('Active','Overload')
  details={}
  cycle_key='speed' if m.get('attributeTraces',{}).get('speed',{}).get('finalValue',0)>0 else 'duration'
  cycle=attribute(m,cycle_key,'周期','s',1000)
  details['cycleTimeSeconds']=cycle
  if not active:
   for key in ['damagePerSecond','volleyDamage','capacitorUsagePerSecond','shieldRepairPerSecond','armorRepairPerSecond','structureRepairPerSecond']:
    details[key]=literal('模块未启用',0,source='装备状态：'+m.get('state',''))
  else:
   damage_source=charge if charge else m
   parts=[formula(n,'sum',[attribute(m,k,n+' · 模块','HP'),attribute(charge,k,n+' · 弹药','HP')],'HP') if charge else attribute(m,k,n,'HP') for k,n in [('emDamage','电磁伤害'),('thermalDamage','热能伤害'),('kineticDamage','动能伤害'),('explosiveDamage','爆炸伤害')]]
   multiplier=attribute(m,'damageMultiplier','伤害倍率')
   if multiplier.get('complete') and multiplier.get('value',0)<=0:
    multiplier=dict(literal('有效伤害倍率',1,source='引擎规则：非正倍率按 1 处理'),inputs=[multiplier])
   shot=formula('齐射伤害','multiply',[formula('伤害分量合计','sum',parts,'HP'),multiplier],'HP',m.get('volleyDamage',0)+charge.get('volleyDamageBonus',0))
   details['volleyDamage']=shot
   details['damagePerSecond']=formula('DPS','divide',[shot,cycle],'HP/s',m.get('damagePerSecond',0)+charge.get('damagePerSecondBonus',0))
   details['damageProfilePerSecond']={}
   details['volleyDamageProfile']={}
   for i,k in enumerate(['em','thermal','kinetic','explosive']):
    component=formula(parts[i]['title'],'multiply',[parts[i],multiplier],'HP',(m.get('volleyDamageProfile',{}).get(k,0)+charge.get('volleyDamageProfileBonus',{}).get(k,0)))
    details['volleyDamageProfile'][k]=component
    details['damageProfilePerSecond'][k]=formula(parts[i]['title']+' / 秒','divide',[component,cycle],'HP/s',m.get('damageProfilePerSecond',{}).get(k,0)+charge.get('damageProfileBonus',{}).get(k,0))
   for field,attr,title,unit in [('capacitorUsagePerSecond','capacitorNeed','电容消耗','GJ/s'),('shieldRepairPerSecond','shieldBonus','护盾维修','HP/s'),('armorRepairPerSecond','armorDamageAmount','装甲维修','HP/s'),('structureRepairPerSecond','structureDamageAmount','结构维修','HP/s'),('capacitorTransferPerSecond','powerTransferAmount','电容传输','GJ/s')]:
    if field in m:details[field]=formula(title,'divide',[attribute(m,attr,'单轮'+title,unit.split('/')[0]),cycle],unit,m[field])
  m['calculationDetails']=details
 for m in report['snapshot']['modules']:item(m)
 for d in report['snapshot']['droneBay']['drones']:item(d,True)
 modules=report['snapshot']['modules'];drones=report['snapshot']['droneBay']['drones']
 weapon=[m['calculationDetails']['damagePerSecond'] for m in modules if m.get('damagePerSecond',0)+(m.get('charge') or {}).get('damagePerSecondBonus',0)>0]
 launched=[formula(d['name'],'multiply',[d['calculationDetails']['damagePerSecond'],literal('出动数量',d['quantity'],'架','装配配置')],'HP/s') for d in drones if d['state']=='Active' and d['quantity']>0]
 a=report['attributes']
 a['calculationDetails']={
 'appliedDamagePerSecond':formula('总 DPS','sum',weapon+launched,'HP/s',a['appliedDamagePerSecond']),
 'appliedDroneDamagePerSecond':formula('无人机 DPS','sum',launched,'HP/s',a['appliedDroneDamagePerSecond']),
 'capacitorUsagePerSecond':formula('模块耗电','sum',[m['calculationDetails']['capacitorUsagePerSecond'] for m in modules if m.get('capacitorUsagePerSecond',0)>0],'GJ/s',a['capacitorUsagePerSecond'])
 }
