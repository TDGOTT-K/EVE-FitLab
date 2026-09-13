import unittest
from fitting_scenario import validate_scenario,turret_factor,missile_factor,calculate_scenario
from sustained_tank import sustained_tank
class ScenarioTests(unittest.TestCase):
 def test_invalid(self):
  for x in [{'signature':0},{'angular':float('nan')},{'resistances':[101,0,0,0]}]:
   with self.assertRaises(ValueError):validate_scenario(x)
 def test_turret(self):
  self.assertAlmostEqual(turret_factor(0,0,100,1000,1000,1,40000),1.01505)
  self.assertLess(turret_factor(3000,0,100,1000,1000,1,40000),.1)
  self.assertEqual(turret_factor(1001,0,100,1000,0,1,40000),0)
 def test_missile(self):
  self.assertEqual(missile_factor(400,0,100,100,.6),1)
  self.assertEqual(missile_factor(25,0,100,100,.6),.25)
  self.assertLess(missile_factor(100,1000,100,100,.6),1)
 def test_reload_and_spool(self):
  m={'name':'test','state':'Active','dogmaTypeId':1,'applicationKind':'Turret','cycleTimeSeconds':2,'magazineCapacity':10,'reloadTimeSeconds':10,'charge':{'damagePerSecondBonus':10,'damageProfileBonus':{'em':10}},'trackingSpeed':100,'optimalRangeMeters':1000,'attributeTraces':{'damageMultiplierBonusPerCycle':{'finalValue':.1},'damageMultiplierBonusMax':{'finalValue':1}}}
  r={'attributes':{},'snapshot':{'modules':[m]}}
  d=calculate_scenario(r,{'distance':0,'angular':0,'spoolSeconds':100},{})
  self.assertAlmostEqual(d['reloadWeaponDps'],20*2/3)
  self.assertEqual(d['weapons'][0]['spoolTime'],20)
 def test_sustained_free(self):
  r={'attributes':{'capacitorCapacity':100,'capacitorRechargeSeconds':100},'snapshot':{'modules':[{'dogmaTypeId':1,'state':'Active','cycleTimeSeconds':2,'shieldRepairPerSecond':10}]}}
  self.assertEqual(sustained_tank(r,{})['shield'],10)
 def test_sustained_cap_limited(self):
  r={'attributes':{'capacitorCapacity':100,'capacitorRechargeSeconds':100},'snapshot':{'modules':[{'dogmaTypeId':1,'state':'Active','cycleTimeSeconds':2,'shieldRepairPerSecond':10,'capacitorUsagePerSecond':20}]}}
  self.assertLess(sustained_tank(r,{})['shield'],10)
class ExternalCapTests(unittest.TestCase):
 def test_external_drain_battery_resistance(self):
  from capacitor import calculate_capacitor
  def run(resistance):
   r={'attributes':{'capacitorCapacity':100,'capacitorRechargeSeconds':0,'attributeSnapshot':{'energyWarfareResistance':resistance}},'snapshot':{'modules':[{'state':'Active','cycleTimeSeconds':1,'capacitorUsagePerSecond':1}]}}
   return calculate_capacitor(r,{}, {'incomingNeut':10,'incomingCycle':5})
  plain,battery=run(1),run(.5)
  self.assertGreater(battery['seconds'],plain['seconds'])
  self.assertEqual(battery['incomingNeutPerSecond'],1)
if __name__=='__main__':unittest.main()
