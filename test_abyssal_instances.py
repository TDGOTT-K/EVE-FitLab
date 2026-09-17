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
 def test_mock_is_marked_and_isolated(self):
  mock={'version':1,'tier':0,'roll':1,'attributes':[{'id':50,'label':'CPU','unit':'tf','highIsGood':False,'base':10,'value':11,'min':8,'max':12}]}
  record=save_instance({'baseTypeId':1,'name':'demo','uiMock':mock},self.lib,self.types,'now')
  self.assertEqual(record['status'],'mock');mock['attributes'][0]['value']=999
  self.assertEqual(self.lib['abyssalInstances'][0]['uiMock']['attributes'][0]['value'],11)
  with self.assertRaises(ValueError):save_instance({'baseTypeId':1,'name':'bad','uiMock':{'version':1,'tier':99,'roll':1}},self.lib,self.types,'now')
if __name__=='__main__':unittest.main()
