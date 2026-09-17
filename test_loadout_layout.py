import copy, unittest
from loadout_plans import save_layout
class Layout(unittest.TestCase):
 def setUp(self):self.lib={'loadoutPlans':[{'id':'a','name':'A','folder':'old','revision':2,'implants':[],'boosters':[]}]}
 def body(self):return {'revision':0,'folders':['outer','outer/inner'],'order':['f:outer','f:outer/inner','p:a'],'plans':[{'id':'a','revision':2,'folder':'outer/inner'}]}
 def test_atomic_move(self):
  result=save_layout(self.body(),self.lib,'now');self.assertEqual(result['plans'][0]['revision'],3);self.assertEqual(result['layout']['revision'],1)
 def test_reject_without_mutation(self):
  for change in [{'folders':['outer/inner']},{'order':['p:a','p:a']},{'plans':[{'id':'a','revision':1,'folder':'outer'}]},{'revision':9}]:
   body=self.body();body.update(change);before=copy.deepcopy(self.lib)
   with self.assertRaises(ValueError):save_layout(body,self.lib,'now')
   self.assertEqual(before,self.lib)
if __name__=='__main__':unittest.main()
