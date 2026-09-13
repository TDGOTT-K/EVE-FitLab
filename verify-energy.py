import server
def fit(slots):return server.analyze({'shipId':587,'name':'energy-check','slots':slots,'skills':[]})
shield={'key':'mid-0','kind':'mid','item':400,'state':'Active'}
nos={'key':'high-0','kind':'high','item':13001,'state':'Active'}
battery={'key':'mid-1','kind':'mid','item':3488,'state':'Online'}
for name,slots in [('shield',[shield]),('nos',[shield,nos]),('battery',[shield,battery]),('both',[shield,nos,battery]),('inactive nos',[shield,dict(nos,state='Online')])]:
 d=fit(slots);print(name,d['attributes']['capacitorCapacity'],d['capacitorAnalysis'])
