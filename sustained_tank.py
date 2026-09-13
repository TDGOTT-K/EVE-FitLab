
"""Explicit 30-minute test: modules retry on their next scheduled cycle, fitting order priority."""
import heapq
from capacitor import recharge
def sustained_tank(report,types,external=None):
 c=float(report['attributes']['capacitorCapacity']);tau=float(report['attributes']['capacitorRechargeSeconds'])
 if c<=0:return None
 events=[];fields=['shieldRepairPerSecond','armorRepairPerSecond','structureRepairPerSecond'];rates=[0,0,0]
 for i,m in enumerate(report['snapshot']['modules']):
  if m['state'] not in ('Active','Overload') or m.get('cycleTimeSeconds',0)<=0:continue
  t=types.get(m['dogmaTypeId'],{});cycle=float(m['cycleTimeSeconds']);repairs=[m.get(k,0)*cycle if t.get('group') not in [41,325,585] else 0 for k in fields]
  cost=m.get('capacitorUsagePerSecond',0)*cycle;charge=types.get((m.get('charge') or {}).get('dogmaTypeId'),{})
  income=m.get('capacitorTransferPerSecond',0)*cycle if t.get('group')==68 else charge.get('attrs',{}).get('67',0) if t.get('group')==76 else 0
  if not cost and not income and not any(repairs):continue
  mag=max(1,int((t.get('capacity') or 0)/(charge.get('volume') or 1))) if t.get('group')==76 else 1
  reload=m.get('reloadTimeSeconds',0) if t.get('group')==76 else 0
  heapq.heappush(events,(0,i,cycle,cost,income,repairs,mag,mag,reload))
 external=external or {}
 sources=external.get('externalEvents',[{'cycle':external.get('incomingCycle',5),'neut':external.get('incomingNeut',0),'transfer':external.get('incomingTransfer',0)}])
 for i,source in enumerate(sources):
  period=source['cycle'];net=source['transfer']-source['neut']*report['attributes'].get('attributeSnapshot',{}).get('energyWarfareResistance',1)
  if net:heapq.heappush(events,(period,-1-i,period,0,net,[0,0,0],1,1,0))
 if not any(any(e[5]) for e in events):return None
 q=c;last=0;attempts=success=0;count=0
 while events:
  time,i,cycle,cost,income,repairs,left,mag,reload=heapq.heappop(events)
  if time>=1800:break
  count+=1
  if count>200000:return {'status':'bounded'}
  q=recharge(q,c,time-last,tau);last=time
  paid=q+1e-8>=cost
  if time>=1200 and any(repairs):attempts+=1
  if paid:
   q=max(0,min(c,max(0,q-cost)+income))
   if time>=1200:
    if any(repairs):success+=1
    for j,v in enumerate(repairs):rates[j]+=v
   left-=1
  delay=cycle+(reload if paid and left==0 else 0)
  heapq.heappush(events,(time+delay,i,cycle,cost,income,repairs,mag if left==0 else left,mag,reload))
 return {'status':'sampled','warmupSeconds':1200,'sampleSeconds':600,'shield':rates[0]/600,'armor':rates[1]/600,'structure':rates[2]/600,'repairAttempts':attempts,'repairActivations':success,'endingPercent':q/c*100}
