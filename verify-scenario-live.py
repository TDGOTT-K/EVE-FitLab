
import server
skills=[{'skillTypeId':t['id'],'level':5} for t in server.CATALOG if t['kind']=='skill']
f={'shipId':622,'name':'scenario-test','slots':[],'skills':skills}
d=server.analyze(f)
assert d['attributes']['attributeSnapshot']['droneControlDistance']==60000
assert d['attributes']['attributeSnapshot']['maxActiveDrones']==5
spool=server.analyze({'shipId':47269,'name':'spool','slots':[{'key':'high-0','kind':'high','item':47914,'ammo':47885,'state':'Active'}],'skills':[],'scenario':{'spoolSeconds':100,'distance':0,'angular':0}})
w=spool['scenarioAnalysis']['weapons'][0]
assert w['baseDps']<w['spooledDps']<=w['maxDps']
missile=server.analyze({'shipId':621,'name':'missile','slots':[{'key':'high-0','kind':'high','item':499,'ammo':212,'state':'Active'}],'skills':[]})
w=missile['scenarioAnalysis']['weapons'][0]
assert w['reloadDps']<w['baseDps']
assert w['flightTime']>0
base={'shipId':587,'name':'battery','slots':[{'key':'mid-0','kind':'mid','item':3488,'state':'Online'}],'skills':[],'scenario':{'incomingNeut':100,'incomingCycle':5}}
battery=server.analyze(base)
assert battery['capacitorAnalysis']['incomingNeutPerSecond']==15
print('Live: character drone limits, spool, missile reload/flight, and battery resistance passed')
