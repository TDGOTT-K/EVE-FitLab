import server
cases=[('turret',{'key':'high-0','kind':'high','item':484,'ammo':185,'state':'Active'}),('repair',{'key':'mid-0','kind':'mid','item':400,'state':'Active'})]
for name,slot in cases:
 r=server.analyze({'shipId':587,'name':'graph','slots':[slot],'skills':[{'skillTypeId':t['id'],'level':5} for t in server.CATALOG if t['kind']=='skill']})
 g=r['snapshot']['modules'][0]['calculationDetails']
 print(name,{k:(v.get('complete'),v.get('missing'),v.get('value')) for k,v in g.items() if k in ['damagePerSecond','volleyDamage','shieldRepairPerSecond','capacitorUsagePerSecond']})
 assert g['damagePerSecond' if name=='turret' else 'shieldRepairPerSecond']['complete']
 assert g['capacitorUsagePerSecond']['complete']
 if name=='turret':print(r['snapshot']['modules'][0]['charge']['attributeTraces'].keys())
