import json,urllib.request,copy
from pathlib import Path
catalog=json.loads(Path('data/full-catalog.json').read_text(encoding='utf-8'));types={t['en']:t['id'] for t in catalog}
def analyze(f):
 r=urllib.request.Request('http://127.0.0.1:5207/api/analyze',data=json.dumps(f).encode(),headers={'Content-Type':'application/json','Origin':'http://127.0.0.1:5207'})
 return json.load(urllib.request.urlopen(r))['attributes']
f={'shipId':622,'name':'trace-check','skills':[{'skillTypeId':3449,'level':5}],'slots':[]}
a=analyze(f);t=a['attributeTraces']['maxVelocity'];assert t['steps'][0]['sourceTypeId']==3449 and t['steps'][0]['sourceValue']==25 and t['steps'][0]['appliedValue']==1.25
f['slots']=[{'key':'low-'+str(i),'kind':'low','item':types['Overdrive Injector System II'],'ammo':None,'online':True} for i in range(2)]
a=analyze(f);t=a['attributeTraces']['maxVelocity'];assert any(s['penaltyMultiplier']<1 for s in t['steps']),t
f['slots'][1]['online']=False
a=analyze(f);steps=a['attributeTraces']['maxVelocity']['steps'];assert len([s for s in steps if s['sourceTypeId']==types['Overdrive Injector System II']])==1
print('Live checks passed: skill source, stacking penalty, offline exclusion. No fits saved.')
