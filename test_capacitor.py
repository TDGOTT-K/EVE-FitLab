
import unittest
from capacitor import calculate_capacitor,recharge
def report(c,t,rate=0,cycle=1,state='Active'):
 return {'attributes':{'capacitorCapacity':c,'capacitorRechargeSeconds':t},'snapshot':{'modules':[{'state':state,'capacitorUsagePerSecond':rate,'cycleTimeSeconds':cycle}]}}
class CapacitorTests(unittest.TestCase):
 def test_recharge_exact(self):
  self.assertAlmostEqual(recharge(0,1000,100,100),986.5695059,places=6)
  self.assertEqual(recharge(1000,1000,100,100),1000)
 def test_passive(self):
  self.assertEqual(calculate_capacitor(report(1000,100),{})['lowPercent'],100)
 def test_stable_pulses(self):
  r=calculate_capacitor(report(1000,100,10,1),{})
  self.assertEqual(r['status'],'stable');self.assertGreater(r['lowPercent'],70)
 def test_peak_not_sufficient(self):
  r=calculate_capacitor(report(1000,100,20,60),{})
  self.assertEqual(r['status'],'depletes');self.assertEqual(r['seconds'],0)
 def test_no_recharge(self):
  r=calculate_capacitor(report(100,0,10,1),{})
  self.assertEqual(r['seconds'],10)
 def test_booster_reload(self):
  r=report(100,0,10,1)
  r['snapshot']['modules'].append({'state':'Active','dogmaTypeId':1,'cycleTimeSeconds':2,'reloadTimeSeconds':4,'charge':{'dogmaTypeId':2}})
  types={1:{'group':76,'capacity':2},2:{'volume':1,'attrs':{'67':30}}}
  out=calculate_capacitor(r,types)
  self.assertTrue(out['includesBoosters']);self.assertEqual(out['status'],'depletes')
 def test_nos_income_and_transfer_direction(self):
  r=report(100,0,10,1)
  r['snapshot']['modules'].append({'state':'Active','dogmaTypeId':1,'name':'nos','capacitorTransferPerSecond':8,'cycleTimeSeconds':1})
  out=calculate_capacitor(r,{1:{'group':68}})
  self.assertEqual(out['nosferatuPerSecond'],8);self.assertGreater(out['seconds'],10)
  remote=calculate_capacitor(r,{1:{'group':67}})
  self.assertEqual(remote['nosferatuPerSecond'],0);self.assertEqual(remote['seconds'],10)
  r['snapshot']['modules'][1]['state']='Online'
  self.assertEqual(calculate_capacitor(r,{1:{'group':68}})['nosferatuPerSecond'],0)
 def test_inactive(self):
  self.assertEqual(calculate_capacitor(report(100,100,100,1,'Online'),{})['status'],'stable')
if __name__=='__main__':unittest.main()
