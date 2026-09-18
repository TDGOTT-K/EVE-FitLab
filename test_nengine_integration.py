"""Real read-only calculations against the independent r33 copy; never battle jobs."""
import copy
import json
from pathlib import Path
import unittest
from nengine_adapter import analyze,bridge,fighter_catalog,native_fit

class NativeIntegration(unittest.TestCase):
    @classmethod
    def tearDownClass(cls): bridge().close()

    def fit(self,ship=587):
        return {'name':'adapter-test','shipId':ship,'slots':[],'skills':[]}

    def test_pinned_source_and_resources(self):
        r=analyze(self.fit())
        self.assertEqual(r['engineVersion'],bridge().baseline['engineVersion'])
        self.assertEqual(r['source']['revision'],bridge().baseline['revision'])
        self.assertEqual(r['source']['buildNumber'],3503375)
        self.assertEqual(r['attributes']['cpuAvailable'],r['native']['attributes']['ship/48']['value'])
        self.assertTrue(r['native']['staticCoverageComplete'])

    def test_weapon_state_ammo_and_skill(self):
        f=self.fit();f['slots']=[{'key':'high-0','kind':'high','item':2881,'ammo':185,'state':'Active'}]
        result=analyze(f);a=result['native'];self.assertGreater(a['nominalDps'],0)
        self.assertEqual(result['snapshot']['modules'][0]['cpuUsage'],next(r['used'] for r in a['resources'] if r['id']=='cpu'))
        f['skills']=[{'skillTypeId':3300,'level':5}]
        b=analyze(f)['native'];self.assertGreater(b['nominalDps'],a['nominalDps'])
        f['slots'][0]['state']='Offline'
        c=analyze(f)['native'];self.assertEqual(c['nominalDps'],0)
        self.assertEqual(next(r['used'] for r in c['resources'] if r['id']=='cpu'),0)

    def test_mode_reaches_engine(self):
        f=self.fit(34317);f['tacticalModeTypeId']=34319
        defense=analyze(f)['native'];f['tacticalModeTypeId']=34323
        speed=analyze(f)['native']
        self.assertLess(speed['motion']['inertiaModifier'],defense['motion']['inertiaModifier'])

    def test_implant_and_booster_snapshot(self):
        f=self.fit();base=analyze(f)['native'];f['loadoutPlan']={'implants':[{'typeId':2082}],'boosters':[]}
        enhanced=analyze(f)['native'];self.assertGreater(enhanced['capacitor']['recharge']['capacity'],base['capacitor']['recharge']['capacity'])
        f['loadoutPlan']['boosters']=[{'typeId':9950,'enabledSideEffects':[2737]}]
        boosted=analyze(f)['native']
        effects=boosted['boosters'][0]['sideEffects']
        self.assertTrue(next(e['enabled'] for e in effects if e['effectId']==2737))
        self.assertFalse(next(e['enabled'] for e in effects if e['effectId']==2739))

    def test_fighter_catalog_limits_and_unavailable_total(self):
        self.assertGreater(len(fighter_catalog()['items']),30)
        f=self.fit(23913);r=analyze(f);self.assertEqual(r['native']['fighterBay']['maximumSquadrons'],5)
        f['fighterLoadout']={'tubes':[{'typeId':23055,'quantity':6,'active':True}],'reserve':[]}
        r=analyze(f);self.assertGreater(r['native']['fighterPrimaryNominalDps'],0)
        self.assertIsNone(r['native']['nominalDps'])
        f['fighterLoadout']['tubes']*=4
        r=analyze(f);self.assertTrue(any(e['code']=='FIGHTER_LIMIT_EXCEEDED' for e in r['issues']))

    def test_weapon_selection_preserves_deployment_and_limits(self):
        f=self.fit(23913)
        f['fighterLoadout']={'tubes':[{'id':'shadow','typeId':2948,'quantity':6,'active':True}], 'reserve':[]}
        on=analyze(f)
        f['fighterLoadout']['tubes'][0]['excludedAbilities']=[22]
        off=analyze(f)
        self.assertGreater(on['fighterDamageSelection']['primaryDps'],0)
        self.assertIsNone(off['fighterDamageSelection']['primaryDps'])
        self.assertEqual(off['outputSelection']['status'],'empty_selection')
        self.assertEqual(on['nativeFit'],off['nativeFit'])
        self.assertEqual(on['native']['resources'],off['native']['resources'])
        self.assertEqual(off['native']['fighterPrimaryNominalDps'],on['native']['fighterPrimaryNominalDps'])
        self.assertIsNone(off['native']['nominalDps'])

    def test_full_skill_coverage_is_not_silently_filtered(self):
        c=json.loads(Path('data/full-catalog.json').read_text(encoding='utf-8'))
        f=self.fit();f['skills']=[{'skillTypeId':t['id'],'level':5} for t in c if t['kind']=='skill']
        r=analyze(f);self.assertTrue(r['isValid']);self.assertTrue(r['native']['staticCoverageComplete'])
        self.assertEqual(len(r['nativeFit']['skills']),len(f['skills']))
        self.assertTrue(r['attributes']['slotUsage'])

    def test_scenario_is_explicitly_unapplied(self):
        f=self.fit();f['scenario']={'distanceMeters':1000}
        r=analyze(f);self.assertTrue(r['integrationNotices'])
        self.assertNotIn('scenarioAnalysis',r)

    def test_no_battle_tool(self):
        with self.assertRaises(ValueError): bridge().call('battle_start',{})

if __name__=='__main__': unittest.main()
