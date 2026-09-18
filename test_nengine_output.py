"""r24 display integration checked against real handoff examples."""
import copy
import json
import unittest
from nengine_adapter import analyze, bridge
from nengine_output import grouped_reading


class OutputIntegration(unittest.TestCase):
    @classmethod
    def tearDownClass(cls): bridge().close()

    def fighter_fit(self):
        native=json.loads((bridge().root/'examples/output-contributions/primary-fit.json').read_text())
        return dict(name='Output QA', shipId=native['shipTypeId'], slots=[],
                    skills=[{'skillTypeId':int(k),'level':v} for k,v in native['skills'].items()],
                    fighterLoadout={'tubes':[{'id':'templar-1','typeId':23055,'quantity':6,'active':True}], 'reserve':[]})

    def test_primary_and_finite_basis(self):
        fit=self.fighter_fit()
        primary=analyze(fit)
        self.assertIsNone(primary['native']['nominalDps'])
        self.assertEqual(primary['outputSelection']['total'],219.375)
        self.assertTrue(primary['outputSelection']['completeSelection'])
        fit['fighterLoadout']['tubes'][0]['includedSecondaryAbilities']=[33]
        partial=analyze(fit)['outputSelection']
        self.assertIsNone(partial['total'])
        self.assertFalse(partial['completeSelection'])
        self.assertEqual(partial['groups'][0]['subtotal'],219.375)
        self.assertEqual(partial['exclusions'][0]['reason'],'FINITE_ABILITY_USE_LOADED_CYCLE_BASIS')
        fit['outputMetric']='loadedCycleDps'
        loaded=analyze(fit)
        self.assertEqual(loaded['outputSelection']['total'],362.8125)
        self.assertEqual(loaded['nativeFit'],primary['nativeFit'])
        self.assertEqual(loaded['native']['resources'],primary['native']['resources'])
        self.assertEqual(loaded['native']['outputContributions']['fitHash'],primary['native']['outputContributions']['fitHash'])

    def test_reserve_and_empty_selection(self):
        fit=self.fighter_fit()
        reserve=copy.deepcopy(fit['fighterLoadout']['tubes'][0]);reserve['id']='spare'
        fit['fighterLoadout']['reserve']=[reserve]
        a=analyze(fit)
        self.assertEqual(a['outputSelection']['total'],219.375)
        self.assertTrue(any('spare' in x['contributionId'] for x in a['outputSelection']['notSelected']))
        fit['fighterLoadout']['tubes'][0]['excludedAbilities']=[22]
        off=analyze(fit)
        self.assertEqual(off['outputSelection']['status'],'empty_selection')
        self.assertIsNone(off['outputSelection']['total'])

    def test_handoff_examples(self):
        root=bridge().root/'examples/output-contributions'
        for name in ['mixed','mixed-applied','drone','bomb','sustained']:
            with self.subTest(name=name):
                fit=json.loads((root/(name+'-fit.json')).read_text())
                context=json.loads((root/(name+'-context.json')).read_text())
                output=bridge().call('fit_analyze',{'fit':fit,'context':context})['result']['outputContributions']
                selection=output['selection'];ids=context['output']['selection']['contributionIds']
                local=grouped_reading([x for x in output['items'] if x['id'] in ids],selection['metric'])
                self.assertEqual(local['total'],selection['total'])
                self.assertEqual(local['completeSelection'],selection['completeSelection'])
                if name=='sustained':self.assertIsNone(selection['total'])

    def test_never_mix_keys_or_hide_null(self):
        def item(id,key,value,state='available',unit='hp/s'):
            return {'id':id,'metrics':{'nominalCycleDps':{'state':state,'value':value,'unit':unit,'aggregationKey':key}}}
        result=grouped_reading([item('a','one',4),item('b','two',5)],'nominalCycleDps')
        self.assertIsNone(result['total'])
        self.assertEqual(len(result['groups']),2)
        result=grouped_reading([item('a','one',4),item('b','one',None,'blocked')],'nominalCycleDps')
        self.assertIsNone(result['total'])
        self.assertEqual(result['groups'][0]['subtotal'],4)
        self.assertEqual(result['exclusions'][0]['state'],'blocked')


if __name__=='__main__':unittest.main()
