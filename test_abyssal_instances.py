import copy,unittest
from abyssal_instances import save_instance
class Instances(unittest.TestCase):
 def setUp(self):
  self.types={1:{'id':1,'kind':'low','group':62,'en':'Repairer'},2:{'id':2,'kind':'low','group':62,'en':'Abyssal Repairer'},3:{'id':3,'kind':'high','group':1,'en':'Other'}};self.lib={}
 def test_revision_and_identity(self):
  body={'baseTypeId':1,'name':'First','notes':'test'};a=save_instance(body,self.lib,self.types,'now');b=save_instance(body,self.lib,self.types,'now');self.assertNotEqual(a['id'],b['id']);self.assertEqual(a['status'],'draft')
  changed=save_instance(dict(a,name='Renamed'),self.lib,self.types,'later');self.assertEqual(changed['revision'],2)
  before=copy.deepcopy(self.lib)
  with self.assertRaises(ValueError):save_instance(a,self.lib,self.types,'stale')
  self.assertEqual(before,self.lib)
 def test_reject_invalid(self):
  for base in [2,3,999]:
   with self.assertRaises(ValueError):save_instance({'baseTypeId':base,'name':'x'},self.lib,self.types,'now')
  with self.assertRaises(ValueError):save_instance({'baseTypeId':1,'name':' '},self.lib,self.types,'now')
if __name__=='__main__':unittest.main()
