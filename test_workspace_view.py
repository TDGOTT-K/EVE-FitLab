import copy
import unittest
from capacitor import calculate_capacitor
from fitting_scenario import calculate_scenario
from workspace_view import attach_workspace_view


def fixture(drone=0, kind='Missile'):
    module = {'name': 'Launcher', 'dogmaTypeId': 1, 'workspaceSlotKey': 'high-2',
              'state': 'Active', 'applicationKind': kind, 'damagePerSecond': 80,
              'damageProfilePerSecond': {'em': 80}, 'volleyDamage': 160,
              'cycleTimeSeconds': 2, 'optimalRangeMeters': 2000,
              'missileExplosionRadiusMeters': 100, 'missileExplosionVelocityMetersPerSecond': 100,
              'missileDamageReductionFactor': .6, 'capacitorUsagePerSecond': 5,
              'magazineCapacity': 5, 'reloadTimeSeconds': 10, 'charge': {'dogmaTypeId': 2}}
    report = {'attributes': {'capacitorCapacity': 1000, 'capacitorRechargeSeconds': 100,
                            'appliedDroneDamagePerSecond': drone, 'volleyDamage': 160,
                            'appliedDamageProfilePerSecond': {'em': 80 + drone}},
              'snapshot': {'modules': [module]}, 'sustainedTank': None,
              'scenarioLinks': {'targetFitId': {'name': 'Target'}}}
    target = {'distance': 500, 'speed': 0, 'angular': 0, 'signature': 25, 'resistances': [50, 0, 0, 0]}
    report['scenarioAnalysis'] = calculate_scenario(report, target, {})
    report['capacitorAnalysis'] = calculate_capacitor(report, {}, {'incomingTransfer': 10, 'incomingCycle': 5})
    return report


class WorkspaceViewTests(unittest.TestCase):
    def test_application_baseline_slot_and_inspector_agree(self):
        r = fixture(); original = copy.deepcopy(r['attributes'])
        attach_workspace_view(r, {'scenario': {'targetFitId': 'target'}}, {})
        w = r['workspace']
        self.assertEqual(w['attack']['weapon'], 10)  # 80 * (25/100) * (1 - 50%)
        self.assertEqual(w['attack']['total'], 10)
        self.assertEqual(w['attack']['reload'], 5)  # 5*2 / (5*2 + 10)
        self.assertEqual(w['attack']['volley'], 20)
        self.assertEqual(w['attack']['profile']['em'], 10)
        self.assertEqual(w['baseline']['attack']['weapon'], 80)
        self.assertEqual(w['baseline']['attack']['reload'], 40)
        m = r['snapshot']['modules'][0]
        self.assertEqual(m['workspaceSlotKey'], 'high-2')
        self.assertEqual(m['scenarioMetrics']['damagePerSecond'], {'value': 10, 'baseline': 80})
        self.assertEqual(r['attributes'], original)
        self.assertEqual(r['capacitorAnalysis']['netUsagePerSecond'], 3)
        self.assertEqual(w['baseline']['capacitorAnalysis']['netUsagePerSecond'], 5)

    def test_unknown_drones_and_weapons_are_not_zero_or_partial_totals(self):
        for drone, kind in [(5, 'Missile'), (0, 'Unknown')]:
            r = fixture(drone, kind)
            attach_workspace_view(r, {'scenario': {'targetFitId': 'target'}}, {})
            self.assertIsNone(r['workspace']['attack']['total'])
            self.assertIsNone(r['workspace']['attack']['reload'])
            self.assertTrue(r['workspace']['issues'])

    def test_neutral_and_support_only_do_not_apply_an_implicit_target(self):
        for fit in [{'scenario': {}}, {'scenario': {'supportFitId': 'source'}}]:
            r = fixture(); r['scenarioLinks'] = {}
            attach_workspace_view(r, fit, {})
            self.assertEqual(r['workspace']['attack']['weapon'], 80)
            self.assertNotIn('scenarioMetrics', r['snapshot']['modules'][0])

    def test_identical_names_keep_individual_application(self):
        r = fixture(); second = copy.deepcopy(r['snapshot']['modules'][0])
        second.update(workspaceSlotKey='high-4', damagePerSecond=40, damageProfilePerSecond={'em': 40})
        r['snapshot']['modules'].append(second)
        r['scenarioAnalysis'] = calculate_scenario(r, {'signature': 25, 'distance': 500, 'speed': 0, 'resistances': [50,0,0,0]}, {})
        attach_workspace_view(r, {'scenario': {'targetFitId': 'target'}}, {})
        self.assertEqual([m['scenarioMetrics']['damagePerSecond']['value'] for m in r['snapshot']['modules']], [10, 5])
        self.assertEqual(r['workspace']['attack']['total'], 15)


if __name__ == '__main__':
    unittest.main()
