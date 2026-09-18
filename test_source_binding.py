import copy,unittest
from nengine_adapter import analyze,bridge
from nengine_persistence import native_save
from nengine_attributes import inspect_attributes
from nengine_valuation import value_fit
from nengine_preview import preview_fit
from nengine_sessions import request

class SourceBinding(unittest.TestCase):
 @classmethod
 def tearDownClass(cls):bridge().close()
 def test_same_source_replays_and_different_source_rejects_analysis_and_save(self):
  fit={'name':'source binding','shipId':587,'slots':[],'skills':[]}
  original=analyze(fit)
  bound={**fit,'sourceBinding':original['sourceBinding']}
  self.assertEqual(analyze(bound)['native'],original['native'])
  for field in original['sourceBinding']:
   changed=copy.deepcopy(bound);changed['sourceBinding'][field]='different'
   with self.subTest(field=field):
    with self.assertRaisesRegex(ValueError,'未自动重算'):analyze(changed)
    with self.assertRaisesRegex(ValueError,'未自动重算'):native_save(changed,None,'no-write')
    with self.assertRaisesRegex(ValueError,'未自动重算'):inspect_attributes(changed,'ship',[76])
    with self.assertRaisesRegex(ValueError,'未自动重算'):value_fit(changed)
    with self.assertRaisesRegex(ValueError,'未自动重算'):preview_fit(changed,fit,analyze)
    with self.assertRaisesRegex(ValueError,'未自动重算'):request('prepare',{'before':fit,'after':changed})

if __name__=='__main__':unittest.main()
