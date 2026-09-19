import unittest
from eft_export import export_eft
from eft_import import parse_eft
class EftExportTests(unittest.TestCase):
 def test_standard_text_roundtrip(self):
  fit=dict(shipId=587,name='Roundtrip',slots=[dict(key='high-0',kind='high',item=2881,ammo=185),dict(key='high-2',kind='high',item=2881)],drones=[dict(item=2454,quantity=2)],cargo=[dict(item=185,quantity=100)])
  text=export_eft(fit)['text'];self.assertTrue(text.startswith('[Rifter, Roundtrip]'));self.assertIn('[Empty High slot]',text)
  parsed=parse_eft(text);self.assertEqual(parsed['issues'],[]);back=parsed['fit'];self.assertEqual(back['shipId'],587);self.assertEqual(back['slots'][0]['ammo'],185);self.assertIsNone(back['slots'][1]['item']);self.assertEqual(back['slots'][2]['item'],2881);self.assertEqual(back['cargo'],fit['cargo']);self.assertEqual(back['drones'][0]['quantity'],2)
 def test_mutation_is_explicitly_lossy_and_names_are_not_custom_labels(self):
  result=export_eft(dict(shipId=587,name='x',slots=[dict(key='high-0',kind='high',item=2881,abyssalName='custom',mutation={1:2})]));self.assertNotIn('custom',result['text']);self.assertEqual(len(result['notes']),2)
if __name__=='__main__':unittest.main()
