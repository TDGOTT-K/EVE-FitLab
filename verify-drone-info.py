import server
skills=[{'skillTypeId':t['id'],'level':5} for t in server.CATALOG if t['kind']=='skill']
def calc(active,skills):return server.analyze({'shipId':622,'name':'drone-info','slots':[],'skills':skills,'drones':[{'item':2456,'quantity':5,'active':active}]})
base=calc(5,[]);full=calc(5,skills);idle=calc(0,skills)
b=base['snapshot']['droneBay']['drones'][0];f=full['snapshot']['droneBay']['drones'][0];i=idle['snapshot']['droneBay']['drones'][0]
assert f['damagePerSecond']>b['damagePerSecond']
assert abs(f['damagePerSecond']*5-full['attributes']['appliedDroneDamagePerSecond'])<.001
assert i['damagePerSecond']==f['damagePerSecond']
assert idle['attributes']['appliedDroneDamagePerSecond']==0
assert f['attributeTraces']['damageMultiplier']['isComplete']
print('Per drone base/all-V/idle',b['damagePerSecond'],f['damagePerSecond'],i['damagePerSecond'])
