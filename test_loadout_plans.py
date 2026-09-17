import unittest
from loadout_plans import save_plan
class Plans(unittest.TestCase):
 def test_revisions_and_snapshots(self):
  lib={};body={'name':'方案','implants':[{'typeId':1,'slot':1}],'boosters':[{'typeId':2,'slot':1,'enabledSideEffects':[9]}]}
  p=save_plan(body,lib,'now');p['implants'][0]['typeId']=3
  self.assertEqual(lib['loadoutPlans'][0]['implants'][0]['typeId'],1)
  updated=save_plan(p,lib,'later');self.assertEqual(updated['revision'],2)
  with self.assertRaises(ValueError):save_plan(p,lib,'stale')
 def test_invalid_rows(self):
  for rows in [[{'typeId':1,'slot':1},{'typeId':2,'slot':1}],[{'typeId':1,'slot':11}]]:
   with self.assertRaises(ValueError):save_plan({'name':'x','implants':rows},{},'now')
  with self.assertRaises(ValueError):save_plan({'name':'x','boosters':[{'typeId':1,'slot':1,'enabledSideEffects':[1,1]}]},{},'now')
if __name__=='__main__':unittest.main()
