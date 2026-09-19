import copy,unittest
from plan_share import export_plan,import_plan

class PlanSharing(unittest.TestCase):
 def plan(self):return {'id':'local-private-id','revision':7,'folder':'private-folder','name':'共享蝰蛇与蓝丸','implants':[{'typeId':19540,'slot':1}],'boosters':[{'typeId':9950,'slot':1,'enabledSideEffects':[2737]}],'pilot':{'name':'private-character','eveCharacterId':123,'skills':[{'skillTypeId':3300,'level':4}]}}
 def test_roundtrip_and_identity(self):
  result=export_plan(self.plan());doc=result['document'];loaded=import_plan(doc)
  self.assertEqual(loaded['plan']['implants'],self.plan()['implants']);self.assertEqual(loaded['plan']['boosters'],self.plan()['boosters'])
  self.assertEqual(loaded['plan']['pilot']['skills'],self.plan()['pilot']['skills']);self.assertEqual(loaded['analysis']['planHash'],result['analysis']['planHash'])
  import json
  text=json.dumps(doc);self.assertNotIn('local-private-id',text);self.assertNotIn('private-folder',text);self.assertNotIn('private-character',text);self.assertNotIn('eveCharacterId',text)
  self.assertEqual(loaded['plan']['folder'],'');self.assertNotIn('id',loaded['plan'])
 def test_import_creates_independent_record(self):
  from loadout_plans import save_plan
  plan=import_plan(export_plan(self.plan())['document'])['plan']
  lib={'loadoutPlans':[{'id':'keep','name':'原方案','revision':1}]}
  saved=save_plan(plan,lib,'2026-09-20')
  self.assertNotEqual(saved['id'],'keep');self.assertEqual(len(lib['loadoutPlans']),2)
  self.assertEqual(saved['folder'],'');self.assertEqual(saved['boosters'],plan['boosters'])
 def test_modified_contents_and_source(self):
  doc=export_plan(self.plan())['document']
  bad=copy.deepcopy(doc);bad['source']['buildNumber']=1
  with self.assertRaises(ValueError):import_plan(bad)
  bad=copy.deepcopy(doc);bad['plan']['boosters'][0]['enabledSideEffects']=[]
  with self.assertRaises(ValueError):import_plan(bad)
 def test_wrong_slot_and_empty(self):
  wrong=self.plan();wrong['implants'][0]['slot']=2
  with self.assertRaises(ValueError):export_plan(wrong)
  self.assertEqual(import_plan(export_plan({'name':'空方案','implants':[],'boosters':[]})['document'])['summary']['items'],[])
if __name__=='__main__':unittest.main()
