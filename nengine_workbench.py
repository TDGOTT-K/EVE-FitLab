"""Single public workbench call per read or edit. No host fitting arithmetic."""
from nengine_adapter import bridge,native_fit,analyze,validate_source_binding
from nengine_scenario import resolve_context
from nengine_edit_commands import prepare_ui_edit
import json
from copy import deepcopy
from functools import lru_cache

@lru_cache(maxsize=8)
def _reference(key):
 # Only exact immutable library/snapshot inputs, never session reads or edits.
 report=read(json.loads(key),[]);native=report['native']
 return {'native':{'attributes':{k:v for k,v in native['attributes'].items() if k in ('ship/552','ship/6186')},
     'defense':native.get('defense'),'fitHash':native['fitHash']},'nativeFit':report['nativeFit'],'source':report['source']}

def reference(f):
 return deepcopy(_reference(json.dumps(f,ensure_ascii=False,separators=(',',':'))))

def policy(f,target):
 context={}
 profile=f.get('damageProfile')
 if f.get('defenseMode')=='targeted' and isinstance(profile,list) and len(profile)==4:context['incomingDamage']=dict(zip(('em','thermal','kinetic','explosive'),profile))
 if target is not None:context['output']={'target':target}
 fighters={}
 for location in ('tubes','reserve'):
  for index,row in enumerate((f.get('fighterLoadout') or {}).get(location,[])):
   if row:fighters[row.get('id') or f'fighter-{location}-{index}']={k:row.get(k,[]) for k in ('excludedAbilities','includedSecondaryAbilities')}
 return {'context':context,'metric':f.get('outputMetric','nominalCycleDps'),'effective':f.get('attackMode')=='edps','fighters':fighters}

def present(f,result,target,source):
 report=analyze(f,target=target,workbench_result=result)
 report['scenarioTargetSource']=source
 report['capacitorScenario']={'state':'pending','reason':'电容续航计算中…'}
 return report

def read(f,fits,client=None):
 client=client or bridge();status=client.discover();validate_source_binding(f,client)
 target,source=resolve_context(f,fits,reference)
 request={'fit':native_fit(f,status['source']['source']['buildNumber']),**policy(f,target)}
 result=client.call('fit_workbench',{'request':request})['result']
 return present(f,result,target,source)

def edit(body,fits):
 if not isinstance(body,dict) or set(body)-{'before','after','sessionId','revision','requestId','operation','initial','installation'}:raise ValueError('无效工作台编辑参数')
 f=body['after'];client=bridge();status=client.discover();validate_source_binding(f,client)
 target,source=resolve_context(f,fits,reference)
 request={k:body[k] for k in ('sessionId','revision','requestId','operation')};request.update(policy(f,target))
 request['installation']=bool(body.get('installation',False))
 if body['operation']=='apply':
  validate_source_binding(body['before'],client)
  prepared=prepare_ui_edit(body['before'],f,status['source']['source']['buildNumber'])
  if not prepared['commands']:return {'ok':True,'changed':False,'report':read(f,fits),'result':None}
  request['commands']=prepared['commands']
  if body.get('initial'):request['fit']=prepared['fit']
 result=client.call('fit_workbench',{'request':request})['result']
 receipt={k:result[k] for k in ('revision','appliedRevision','appliedFitHash','replayed')}
 receipt['analysis']={'fitHash':result['analysis']['fitHash']}
 return {'ok':True,'changed':True,'result':receipt,'report':present(f,result,target,source)}
