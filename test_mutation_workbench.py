import unittest,copy
from abyssal_instances import save_instance
from nengine_mutations import generate,workbench
from nengine_adapter import analyze,bridge
from server import TYPES
class ManualMutation(unittest.TestCase):
 @classmethod
 def tearDownClass(cls):bridge().close()
 def test_manual_save_and_fitted_values_and_forgery(self):
  args=dict(baseTypeId=526,mutaplasmidTypeId=47699)
  rule=generate(args,TYPES)['data'];values={str(a['attributeId']):(a['minimumValue']+a['maximumValue'])/2 for a in rule['attributes']}
  receipt=workbench(dict(args,operation='edit',values=values),TYPES)['edit'];library={}
  record=save_instance(dict(baseTypeId=526,name='manual-test',editReceipt=receipt),library,TYPES,'now')
  self.assertEqual(record['origin'],'manual');self.assertNotIn('generationReceipt',record)
  renamed=save_instance(dict(record,name='renamed'),library,TYPES,'later');self.assertEqual(renamed['editReceipt'],receipt)
  fit=dict(name='manual-test',shipId=587,skills=[],slots=[dict(key='mid-0',kind='mid',item=record['resultTypeId'],state='Active',mutation=record['mutation'])])
  result=analyze(fit);self.assertAlmostEqual(next(r['used'] for r in result['native']['resources'] if r['id']=='cpu'),values['50'])
  bad=copy.deepcopy(receipt);bad['mutation']['attributes']['50']=0;original=copy.deepcopy(library)
  with self.assertRaises(Exception):save_instance(dict(baseTypeId=526,name='forged',editReceipt=bad),library,TYPES,'now')
  self.assertEqual(original,library)
if __name__=='__main__':unittest.main()
