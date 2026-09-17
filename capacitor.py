
import math,heapq
def recharge(q,c,t,tau):
 return c*(1-(1-math.sqrt(max(0,q/c)))*math.exp(-5*t/tau))**2 if tau>0 else q
def calculate_capacitor(report,types,external=None):
 a=report['attributes'];c=float(a['capacitorCapacity']);tau=float(a['capacitorRechargeSeconds'])
 if c<=0:return {'status':'unavailable','reason':'没有有效电容容量'}
 events=[];periods=[];usage=0;boosted=False;nos_income=0;injection_income=0;income_sources=[]
 for i,m in enumerate(report['snapshot']['modules']):
  if m['state'] not in ('Active','Overload'):continue
  rate=float(m.get('capacitorUsagePerSecond',0));cycle=float(m.get('cycleTimeSeconds',0))
  t=types.get(m.get('dogmaTypeId'),{});charge=m.get('charge') or {}
  ct=types.get(charge.get('dogmaTypeId'),{})
  injector=t.get('group')==76
  nos_rate=float(m.get('capacitorTransferPerSecond',0)) if t.get('group')==68 else 0
  boost=float(ct.get('attrs',{}).get('67',0)) if injector else nos_rate*cycle
  nos_income+=nos_rate
  if nos_rate:income_sources.append([m.get('name','吸电器'),nos_rate])
  if not rate and not boost:continue
  if cycle<=0:return {'status':'unavailable','reason':'耗电模块缺少有效周期'}
  p=max(1,round(cycle*1000));cost=rate*cycle;usage+=rate;boosted|=injector and boost>0
  mag=max(1,int(m.get('magazineCapacity') or 1))
  if injector and boost:
   volume=ct.get('volume') or 0
   mag=int((t.get('capacity') or 0)/volume+1e-8) if volume>0 else 0
   if mag<1:return {'status':'unavailable','reason':'注电弹药超过弹夹容量或体积数据缺失'}
  reload=max(0,round(float(m.get('reloadTimeSeconds') or 0)*1000)) if injector else 0
  if not injector:mag=1
  if injector and boost:injection_income+=boost*mag/(cycle*mag+reload/1000)
  periods.append(p*mag+reload if boost else p)
  heapq.heappush(events,(0,i,p,cost,boost,mag,mag,reload))
 external=external or {}
 resistance=float(a.get('attributeSnapshot',{}).get('energyWarfareResistance',1))
 incoming_cycle=1;neut=transfer=0
 sources=external.get('externalEvents',[{'cycle':external.get('incomingCycle',5),'neut':external.get('incomingNeut',0),'transfer':external.get('incomingTransfer',0)}])
 for i,source in enumerate(sources):
  cycle=source['cycle'];loss=source['neut']*resistance;gain=source['transfer'];neut+=loss/cycle;transfer+=gain/cycle
  if loss or gain:
   p=max(1,round(cycle*1000));periods.append(p);heapq.heappush(events,(p,-1-i,p,0,gain-loss,1,1,0))
 peak=2.5*c/tau if tau>0 else 0
 result={'events':[],'capacity':c,'rechargeSeconds':tau,'usagePerSecond':usage,'peakRechargePerSecond':peak,'includesBoosters':boosted,'nosferatuPerSecond':nos_income,'injectionPerSecond':injection_income,'netUsagePerSecond':usage-nos_income-injection_income+neut-transfer,'nosferatuSources':income_sources,'incomingNeutPerSecond':neut/incoming_cycle,'incomingTransferPerSecond':transfer/incoming_cycle,'energyWarfareResonance':resistance}
 if not events:return dict(result,status='stable',lowPercent=100,highPercent=100)
 period=1
 for p in periods:
  period=math.lcm(period,p)
  if period>3600000:break
 q=c;last=0;low=c;high=c;boundary=period;previous=None;count=0
 while events and count<200000:
  now=events[0][0]
  if now>21600000:break
  if period<=3600000 and boundary<=now:
   q=recharge(q,c,(boundary-last)/1000,tau);last=boundary
   if previous is not None and abs(q-previous)<max(1e-7,c*1e-8):
    return dict(result,status='stable',lowPercent=100*low/c,highPercent=100*high/c,checkedSeconds=boundary/1000)
   previous=q;low=q;high=q;boundary+=period;continue
  before=q;elapsed=(now-last)/1000;q=recharge(q,c,elapsed,tau);last=now;high=max(high,q)
  batch=[]
  while events and events[0][0]==now:batch.append(heapq.heappop(events))
  cost=sum(e[3] for e in batch)
  result['events'].append({'time':now/1000,'beforeRecharge':before,'elapsed':elapsed,'afterRecharge':q,'cost':cost,'income':sum(e[4] for e in batch),'canActivate':q+1e-8>=cost})
  result['events']=result['events'][-12:]
  if q+1e-8<cost:return dict(result,status='depletes',seconds=now/1000,remainingPercent=100*q/c)
  q-=cost;low=min(low,q);q=max(0,min(c,q+sum(e[4] for e in batch)));high=max(high,q)
  for _,i,p,cost,b,remaining,mag,reload in batch:
   remaining-=1
   heapq.heappush(events,(now+p+(reload if b and remaining==0 else 0),i,p,cost,b,mag if remaining==0 else remaining,mag,reload))
  count+=len(batch)
 return dict(result,status='bounded',checkedSeconds=last/1000,lowPercent=100*low/c,highPercent=100*high/c)
