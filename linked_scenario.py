"""Resolve scenario inputs from other saved fits, without following their own links."""
from fitting_scenario import validate_scenario

def resolve_fit_scenario(value,fits,analyze,types,own_speed=None):
 target=validate_scenario({k:value[k] for k in ['distance','speed','angular','spoolSeconds'] if k in value})
 target.update(incomingNeut=0,incomingTransfer=0)
 target['externalEvents']=[];links={};cache={}
 def get(key):
  identity=value.get(key)
  if not identity:return None
  if not isinstance(identity,str):raise ValueError('关联装配标识无效')
  fit=next((f for f in fits if f['id']==identity),None)
  if fit is None:raise ValueError('关联装配已删除，请重新选择目标或传电来源')
  if identity not in cache:cache[identity]=analyze(dict(fit,scenario={}))
  links[key]={'id':identity,'name':fit['name'],'shipId':fit['shipId'],'revision':fit.get('revision')}
  return cache[identity]
 victim=get('targetFitId')
 if victim:
  a=victim['attributes'];layer=value.get('targetLayer','shield')
  if layer not in ['shield','armor','structure']:raise ValueError('目标防御层无效')
  target['signature']=a['signatureRadius'];target['resistances']=[a[layer+'Resistances'][k+'Percent']*100 for k in ['em','thermal','kinetic','explosive']]
  attrs=a.get('attributeSnapshot',{});target['sensorStrength']=max([attrs.get(k,0) for k in ['scanRadarStrength','scanMagnetometricStrength','scanGravimetricStrength','scanLadarStrength']]+[.1])
  target['targetLayer']=layer
  if own_speed is not None:
   cap=max(0,own_speed)+max(0,a['maxVelocity']);target['maxRelativeSpeed']=cap
   if target['speed']>cap:
    target['angular']*=cap/target['speed'];target['speed']=cap
 for key,distanceKey,group in [('supportFitId','supportDistance',67),('hostileFitId','hostileDistance',71)]:
  other=get(key)
  if other is None:continue
  distance=value.get(distanceKey,10000)
  validate_scenario({'distance':distance});links[key]['distance']=distance;links[key]['modules']=[]
  for m in other['snapshot']['modules']:
   if types.get(m.get('dogmaTypeId'),{}).get('group')!=group or m.get('state') not in ['Active','Overload']:continue
   cycle=m.get('cycleTimeSeconds',0);traces=m.get('attributeTraces',{})
   optimal=m.get('optimalRangeMeters',0) or traces.get('maxRange',{}).get('finalValue',0)
   falloff=m.get('falloffRangeMeters',0)
   if cycle<=0 or optimal<=0:links[key]['modules'].append({'name':m['name'],'status':'缺少周期或射程，未计入'});continue
   factor=1 if distance<=optimal else .5**min(1074,((distance-optimal)/falloff)**2) if falloff>0 else 0
   amount=m.get('capacitorTransferPerSecond',0)*cycle if group==67 else traces.get('energyNeutralizerAmount',{}).get('finalValue',0)
   if amount<=0:links[key]['modules'].append({'name':m['name'],'status':'缺少输出数值，未计入'});continue
   event={'cycle':cycle,'transfer':amount*factor if group==67 else 0,'neut':amount*factor if group==71 else 0}
   target['externalEvents'].append(event);links[key]['modules'].append({'name':m['name'],'status':'超出射程' if factor==0 else '失准期望值' if factor<1 else '生效','perSecond':amount*factor/cycle})
  if not links[key]['modules']:links[key]['note']='没有已启用的'+('传电装备' if group==67 else '毁电装备')
 return target,links
