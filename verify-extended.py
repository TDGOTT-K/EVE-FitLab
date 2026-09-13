import server
byname={t['en']:t['id'] for t in server.CATALOG}
base={'shipId':622,'name':'extended-acceptance','slots':[],'skills':[]}
d=server.analyze(dict(base,drones=[{'item':2456,'quantity':5,'active':5}],cargo=[{'item':185,'quantity':100}]))
assert d['attributes']['appliedDroneDamagePerSecond']>0
assert d['attributes']['droneBayUsed']==25
assert d['attributes']['attributeSnapshot']['scanLadarStrength']==13
assert d['attributes']['attributeSnapshot']['capacity']==420
assert d['cargoUsed']>0
idle=server.analyze(dict(base,drones=[{'item':2456,'quantity':5,'active':0}]))
assert idle['attributes']['appliedDroneDamagePerSecond']==0
assert idle['attributes']['droneBayUsed']==25
mods=['Miner II','Small Energy Neutralizer II','Small Remote Shield Booster II']
d=server.analyze(dict(base,slots=[{'key':'high-'+str(i),'kind':'high','item':byname[n],'state':'Active'} for i,n in enumerate(mods)]))
modules=d['snapshot']['modules']
assert modules[0]['attributeTraces']['miningAmount']['finalValue']==15
assert modules[1]['attributeTraces']['energyNeutralizerAmount']['finalValue']==55
assert modules[2]['shieldRepairPerSecond']>0
print('Final sensors/cargo, launched vs bay drones, mining, neutralizer and remote repair passed')

assert d['attributes']['remoteShieldRepairPerSecond']>0
assert abs(d['attributes']['shieldRepairPerSecond']-d['attributes']['passiveShieldRechargePerSecond'])<1e-6
print('Remote repair is excluded from self tank')
