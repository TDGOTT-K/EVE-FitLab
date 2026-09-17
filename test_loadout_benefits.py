import json,unittest
from pathlib import Path

def catalog(name):
 s=Path(name+'-catalog.js').read_text(encoding='utf-8');return json.loads(s[s.index('['):].rstrip(';\n'))
class Benefits(unittest.TestCase):
 def test_white_only_attributes(self):
  items=catalog('implant');white=[t for t in items if ['训练与其他','白板'] in t['benefitPaths']]
  self.assertGreater(len(white),20)
  for t in white:
   self.assertEqual(t['benefitPaths'],[['训练与其他','白板']])
   self.assertTrue(all(' +' in label for label in t['benefitLabels']))
  for t in items:
   if any(x in t['en'] for x in ['Nirvana','Snake','Genolution','Talisman']):self.assertNotIn(['训练与其他','白板'],t['benefitPaths'])
 def test_sets_and_booster(self):
  items=catalog('implant')
  for t in items:
   if 'Nirvana' in t['en']:self.assertIn(['防御','护盾容量'],t['benefitPaths'])
   if 'Snake' in t['en']:self.assertIn(['机动','速度与推进'],t['benefitPaths'])
   if 'Talisman' in t['en']:self.assertIn(['电容与装配','传电与能量战'],t['benefitPaths'])
  blue=next(t for t in catalog('booster') if t['en']=='Standard Blue Pill Booster')
  self.assertEqual(blue['benefitPaths'],[['防御','护盾维修']])
 def test_coverage(self):
  for t in catalog('implant')+catalog('booster'):
   self.assertTrue(t['benefitPaths']);self.assertEqual(len(t['benefitPaths']),len(set(map(tuple,t['benefitPaths']))))
if __name__=='__main__':unittest.main()
