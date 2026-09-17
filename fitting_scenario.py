
import math
DAMAGE=['em','thermal','kinetic','explosive']
DEFAULT={'distance':10000,'speed':200,'angular':0.01,'signature':125,'resistances':[0,0,0,0],'spoolSeconds':0,'sensorStrength':20,'ewarResistance':0,'incomingNeut':0,'incomingTransfer':0,'incomingCycle':5}
def validate_scenario(value):
 if not isinstance(value,dict):raise ValueError('目标条件无效')
 result=dict(DEFAULT)
 ranges={'distance':(0,1e7),'speed':(0,1e6),'angular':(0,100),'signature':(.1,1e7),'spoolSeconds':(0,86400),'sensorStrength':(.1,1e6),'ewarResistance':(0,100),'incomingNeut':(0,1e8),'incomingTransfer':(0,1e8),'incomingCycle':(.1,3600)}
 for k,(low,high) in ranges.items():
  v=value.get(k,result[k])
  if isinstance(v,bool) or not isinstance(v,(int,float)) or not math.isfinite(v) or not low<=v<=high:raise ValueError('目标参数超出范围：'+k)
  result[k]=v
 rs=value.get('resistances',result['resistances'])
 if not isinstance(rs,list) or len(rs)!=4 or any(type(v) not in (int,float) or not math.isfinite(v) or not 0<=v<=100 for v in rs):raise ValueError('目标抗性无效')
 result['resistances']=rs
 return result
def trace_value(obj,key,default=0):
 return obj.get('attributeTraces',{}).get(key,{}).get('finalValue',default)
def turret_factor(distance,angular,signature,optimal,falloff,tracking,resolution):
 if tracking<=0:return None
 error=angular*resolution/(tracking*signature)
 if distance>optimal and falloff<=0:return 0
 range_error=max(0,distance-optimal)/falloff if falloff>0 else 0
 chance=0.5**min(1074,error*error+range_error*range_error)
 return 3*chance if chance<.01 else .5*chance*chance+.49*chance+.02505
def missile_factor(signature,speed,radius,velocity,exponent):
 if radius<=0 or velocity<=0 or exponent<=0:return None
 ratio=signature/radius
 return min(1,ratio,(ratio*velocity/speed)**exponent if speed>0 else 1)
def calculate_scenario(report,scenario,types):
 target=validate_scenario(scenario or {})
 rows=[];effects=[];base_total=reload_total=applied_total=max_total=0
 for module_index,m in enumerate(report['snapshot']['modules']):
  if m['state'] not in ['Active','Overload']:continue
  t=types.get(m['dogmaTypeId'],{});charge=m.get('charge') or {}
  cycle=m.get('cycleTimeSeconds',0);base=m.get('damagePerSecond',0)+charge.get('damagePerSecondBonus',0)
  profile={k:m.get('damageProfilePerSecond',{}).get(k,0)+charge.get('damageProfileBonus',{}).get(k,0) for k in DAMAGE}
  bonus=trace_value(m,'damageMultiplierBonusPerCycle');maximum=trace_value(m,'damageMultiplierBonusMax')
  multiplier=1+min(maximum,bonus*math.floor(target['spoolSeconds']/cycle)) if cycle>0 and bonus>0 else 1
  top=1+maximum if bonus>0 else 1
  reload_factor=1;magazine=m.get('magazineCapacity',0);reload=m.get('reloadTimeSeconds',0)
  if charge and magazine>0 and cycle>0 and reload>0:reload_factor=magazine*cycle/(magazine*cycle+reload)
  if base>0:
   kind=m.get('applicationKind');factor=None
   if kind=='Turret':
    resolution=m.get('signatureResolutionMeters',0) or 40000
    factor=turret_factor(target['distance'],target['angular'],target['signature'],m.get('optimalRangeMeters',0),m.get('falloffRangeMeters',0),m.get('trackingSpeed',0),resolution)
   elif kind=='Missile':
    factor=missile_factor(target['signature'],target['speed'],m.get('missileExplosionRadiusMeters',0),m.get('missileExplosionVelocityMetersPerSecond',0),m.get('missileDamageReductionFactor',0))
    if target['distance']>m.get('optimalRangeMeters',0):factor=0
   resisted=sum(profile[k]*(1-target['resistances'][i]/100) for i,k in enumerate(DAMAGE))
   applied=resisted*multiplier*reload_factor*factor if factor is not None else None
   rows.append({'moduleIndex':module_index,'slotKey':m.get('workspaceSlotKey'),'typeId':m.get('dogmaTypeId'),'chargeTypeId':charge.get('dogmaTypeId'),'baseVolley':m.get('volleyDamage',0)+charge.get('volleyDamageBonus',0),'baseProfile':profile,'appliedDpsWithoutReload':resisted*multiplier*factor if factor is not None else None,'appliedVolley':(m.get('volleyDamage',0)+charge.get('volleyDamageBonus',0))*multiplier*factor*resisted/base if factor is not None else None,'appliedProfile':{k:profile[k]*(1-target['resistances'][i]/100)*multiplier*factor for i,k in enumerate(DAMAGE)} if factor is not None else None,'reloadFactor':reload_factor,'name':m['name'],'baseDps':base,'spooledDps':base*multiplier,'maxDps':base*top,'reloadDps':base*multiplier*reload_factor,'appliedDps':applied,'factor':factor,'spoolTime':math.ceil(maximum/bonus)*cycle if bonus>0 else 0,'flightTime':target['distance']/trace_value(charge,'maxVelocity') if trace_value(charge,'maxVelocity')>0 else None})
   base_total+=base;reload_total+=base*multiplier*reload_factor;max_total+=base*top
   if applied is not None:applied_total+=applied
  # Range falloff for probabilistic EWAR; webs/scrams without falloff are hard range limited.
  optimal=m.get('optimalRangeMeters',0);falloff=m.get('falloffRangeMeters',0)
  range_factor=1 if target['distance']<=optimal else .5**min(1074,((target['distance']-optimal)/falloff)**2) if falloff>0 else 0
  for attr,label in [('speedFactor','减速'),('maxTargetRangeBonus','锁定距离'),('scanResolutionBonus','扫描分辨率'),('signatureRadiusBonus','信号半径'),('trackingSpeedBonus','跟踪')]:
   v=trace_value(m,attr)
   if t.get('group') in [65,52,209,208,379,201] and v:effects.append({'name':m['name'],'attribute':attr,'label':label,'percent':v,'rangeChance':range_factor})
  if t.get('group')==201:
   strength=max(trace_value(m,k) for k in ['scanRadarStrengthBonus','scanMagnetometricStrengthBonus','scanGravimetricStrengthBonus','scanLadarStrengthBonus'])
   if strength>0:effects.append({'name':m['name'],'attribute':'ecm','label':'ECM（匹配感应类型）','chance':min(1,strength/target['sensorStrength'])*range_factor})
 combined=[]
 for key in sorted(set(e['attribute'] for e in effects if e['attribute']!='ecm')):
  entries=[e for e in effects if e['attribute']==key]
  # Range falloff is a success probability; do not treat it as a deterministic strength reduction.
  certain=[e for e in entries if e['rangeChance']==1]
  factor=1
  for i,e in enumerate(sorted(certain,key=lambda e:abs(e['percent']),reverse=True)):
   penalty=math.exp(-(i/2.22292081)**2)
   factor*=1+e['percent']/100*(1-target['ewarResistance']/100)*penalty
  combined.append({'label':entries[0]['label'],'factor':factor,'count':len(certain),'probabilistic':len(entries)-len(certain)})
 ecm=1-math.prod(1-e['chance'] for e in effects if e['attribute']=='ecm')
 drones=report['attributes'].get('appliedDroneDamagePerSecond',0)
 return {'target':target,'weapons':rows,'baseWeaponDps':base_total,'reloadWeaponDps':reload_total,'maxWeaponDps':max_total,'appliedWeaponDps':applied_total,'unresolvedWeapons':sum(r['appliedDps'] is None for r in rows),'droneDps':drones,'effects':effects,'combinedEffects':combined,'ecmChance':ecm}
