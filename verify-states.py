import json,urllib.request
f={'shipId':587,'name':'state-check','skills':[],'slots':[{'key':'high-0','kind':'high','item':484,'ammo':185}]}
values={}
for state in ['Offline','Online','Active','Overload']:
 f['slots'][0]['state']=state
 req=urllib.request.Request('http://127.0.0.1:5207/api/analyze',data=json.dumps(f).encode(),headers={'Content-Type':'application/json','Origin':'http://127.0.0.1:5207'})
 d=json.load(urllib.request.urlopen(req));a=d['attributes'];values[state]=(a['cpuUsed'],a['appliedDamagePerSecond'],d['snapshot']['modules'][0]['state'])
print(values)
assert values['Offline'][0]==0
assert values['Online'][0]>0 and values['Online'][1]==0
assert values['Active'][1]>0
assert values['Overload'][1]>values['Active'][1]
