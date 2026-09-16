import copy
import math
from fitting_scenario import validate_scenario

def validate_presets(body):
 rows=body.get('scenarios',[]);active=body.get('activeScenarioId')
 if not isinstance(rows,list) or len(rows)>50:raise ValueError('每份装配最多保存 50 个情景')
 ids=set();result=[]
 for row in rows:
  if not isinstance(row,dict):raise ValueError('情景格式无效')
  identity=row.get('id');name=row.get('name');value=row.get('value')
  if not isinstance(identity,str) or not 1<=len(identity)<=80 or identity in ids:raise ValueError('情景标识无效或重复')
  if not isinstance(name,str) or not 1<=len(name.strip())<=80:raise ValueError('情景名称应为 1–80 个字符')
  validate_scenario(value)
  for key in ['targetFitId','supportFitId','hostileFitId']:
   if key in value and (not isinstance(value[key],str) or len(value[key])>80):raise ValueError('情景装配引用无效')
  if value.get('targetLayer','shield') not in ['shield','armor','structure']:raise ValueError('目标防御层无效')
  for key in ['supportDistance','hostileDistance']:
   v=value.get(key,10000)
   if type(v) not in (int,float) or not math.isfinite(v) or not 0<=v<=500000:raise ValueError('来源距离超出范围')
  for key in ['geometry','supportGeometry','hostileGeometry']:
   if key not in value:continue
   g=value[key];keys=['x','y','vx','vy'] if key=='geometry' else ['x','y']
   if not isinstance(g,dict) or any(type(g.get(k)) not in (int,float) or not math.isfinite(g[k]) for k in keys):raise ValueError('情景位置或速度无效')
   if math.hypot(g['x'],g['y'])>500000.001:raise ValueError('情景位置超出范围')
  health=value.get('targetHealth',{})
  if not isinstance(health,dict) or any(type(v) is not bool for v in health.values()):raise ValueError('目标防御状态无效')
  ids.add(identity);result.append({'id':identity,'name':name.strip(),'value':copy.deepcopy(value)})
 if active is not None and (not isinstance(active,str) or active not in ids):raise ValueError('当前情景不存在')
 selected=next((r['value'] for r in result if r['id']==active),{})
 return {'scenarios':result,'activeScenarioId':active,'scenario':copy.deepcopy(selected)}
