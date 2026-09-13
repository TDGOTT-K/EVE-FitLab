import unittest
from linked_scenario import resolve_fit_scenario
class LinkedScenarioTests(unittest.TestCase):
 def test_attributes_and_per_module_cycles(self):
  fits=[{'id':'target','name':'Target','shipId':1,'scenario':{'targetFitId':'source'}},{'id':'source','name':'Support','shipId':2}]
  calls=[]
  def analyze(f):
   calls.append(f);return {'attributes':{'signatureRadius':80,'armorResistances':dict(emPercent=.5,thermalPercent=.4,kineticPercent=.3,explosivePercent=.2),'attributeSnapshot':{'scanRadarStrength':22}},'snapshot':{'modules':[{'name':'Transfer','dogmaTypeId':5,'state':'Active','cycleTimeSeconds':4,'optimalRangeMeters':20000,'capacitorTransferPerSecond':12},{'name':'Offline','dogmaTypeId':5,'state':'Offline','cycleTimeSeconds':4,'capacitorTransferPerSecond':99}]}}
  result,links=resolve_fit_scenario({'targetFitId':'target','targetLayer':'armor','supportFitId':'source','supportDistance':1000,'incomingTransfer':999},fits,analyze,{5:{'group':67}})
  self.assertEqual(result['signature'],80);self.assertEqual(result['resistances'],[50,40,30,20]);self.assertEqual(result['externalEvents'],[{'cycle':4,'transfer':48,'neut':0}]);self.assertTrue(all(c['scenario']=={} for c in calls))
  result,_=resolve_fit_scenario({'supportFitId':'source','supportDistance':30000},fits,analyze,{5:{'group':67}});self.assertEqual(result['externalEvents'][0]['transfer'],0)
 def test_relative_speed_cap(self):
  fits=[{'id':'target','name':'Target','shipId':1}]
  def analyze(f):return {'attributes':{'maxVelocity':300,'signatureRadius':80,'shieldResistances':{k+'Percent':0 for k in ['em','thermal','kinetic','explosive']}},'snapshot':{'modules':[]}}
  target,_=resolve_fit_scenario({'targetFitId':'target','speed':2000,'angular':.2},fits,analyze,{},own_speed=200)
  self.assertEqual(target['speed'],500);self.assertAlmostEqual(target['angular'],.05)
 def test_missing_reference(self):
  with self.assertRaises(ValueError):resolve_fit_scenario({'targetFitId':'deleted'},[],lambda f:None,{})
if __name__=='__main__':unittest.main()
