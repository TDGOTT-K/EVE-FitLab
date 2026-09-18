import unittest
from nengine_adapter import analyze,bridge
from nengine_scenario import resolve_target
from nengine_curves import build_curves


class NativeScenario(unittest.TestCase):
    @classmethod
    def tearDownClass(cls):bridge().close()

    def fit(self):
        return {'name':'Native scenario','shipId':587,'skills':[],
                'slots':[{'key':'high-0','kind':'high','item':2881,'ammo':185,'state':'Active'}]}

    def test_target_uses_native_signature_not_user_guess(self):
        target_fit={'id':'victim','name':'Target','shipId':587,'slots':[],'skills':[],
                    'scenario':{'targetFitId':'recursive'}}
        fit=self.fit();fit['scenario']={'targetFitId':'victim','signature':999999,'distance':10000,'speed':100,'angular':.02}
        target=resolve_target(fit,[target_fit],analyze)
        self.assertEqual(target['signatureMeters'],analyze({**target_fit,'scenario':{}})['native']['attributes']['ship/552']['value'])
        self.assertEqual(target['distanceMeters'],10000)
        with self.assertRaises(ValueError):resolve_target(fit,[],analyze)

    def test_sample_matches_current_and_keeps_fit(self):
        f=self.fit();target={'id':'target','distanceMeters':10000,'signatureMeters':40,'speedMetersPerSecond':200,'angularRadiansPerSecond':.02}
        base=analyze(f);applied=analyze(f,target=target)
        self.assertEqual(base['nativeFit'],applied['nativeFit'])
        self.assertLess(applied['outputSelection']['total'],base['outputSelection']['total'])
        self.assertEqual(applied['baselineOutputSelection'],base['outputSelection'])
        self.assertEqual(applied['native']['resources'],base['native']['resources'])
        curves=build_curves(applied)
        for series in curves['series']:
            current=next(y for x,y,*_ in series['points'] if x==series['currentX'])
            self.assertAlmostEqual(current,applied['outputSelection']['total'])
            ratio=next(row[2] for row in series['points'] if row[0]==series['currentX'])
            self.assertAlmostEqual(ratio,applied['native']['outputContributions']['comparison']['ratio']['value'])
        self.assertEqual(curves['totalDps'],base['outputSelection']['total'])
        self.assertTrue(build_curves(base)['ideal'])

    def test_partial_output_does_not_become_complete_curve(self):
        f={'name':'fighter','shipId':23913,'slots':[],'skills':[],
           'fighterLoadout':{'tubes':[{'id':'f','typeId':23055,'quantity':6,'active':True,'includedSecondaryAbilities':[33]}]}}
        a=analyze(f)
        self.assertFalse(a['outputSelection']['completeSelection'])
        self.assertEqual(build_curves(a)['status'],'unavailable')


if __name__=='__main__':unittest.main()
