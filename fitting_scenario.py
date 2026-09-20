"""Validate target form inputs; calculation belongs to NEngine."""

import math
DEFAULT={'distance':10000,'speed':200,'angular':0.01,'signature':125,'resistances':[0,0,0,0],'spoolSeconds':0,'sensorStrength':20,'ewarResistance':0,'incomingNeut':0,'incomingTransfer':0,'incomingCycle':5}
def validate_scenario(value):
 if not isinstance(value,dict):raise ValueError('目标条件无效')
 result=dict(DEFAULT)
 ranges={'distance':(0,1e7),'speed':(0,1e6),'angular':(0,100),'signature':(.1,1e7),'spoolSeconds':(0,86400),'sensorStrength':(.1,1e6),'ewarResistance':(0,100),'incomingNeut':(0,1e8),'incomingTransfer':(0,1e8),'incomingCycle':(.1,3600)}
 for k,(low,high) in ranges.items():
  v=value.get(k,result[k])
  if isinstance(v,bool) or not isinstance(v,(int,float)) or not math.isfinite(v) or not low<=v<=high:raise ValueError('目标参数超出范围：'+k)
  result[k]=v
 rs=value.get('resistances',result['resistances'])
 if not isinstance(rs,list) or len(rs)!=4 or any(type(v) not in (int,float) or not math.isfinite(v) or not 0<=v<=100 for v in rs):raise ValueError('目标抗性无效')
 result['resistances']=rs
 return result