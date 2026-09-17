import math
import copy
import unittest
from test_workspace_view import fixture
from fitting_scenario import calculate_scenario, turret_factor
from workspace_view import attach_workspace_view


class DpsCurveTests(unittest.TestCase):
    def ideal(self, report, types=None):
        report['scenarioLinks'] = {}
        attach_workspace_view(report, {'scenario': {}}, types or {1: {'attrs': {'128': 1}}})
        return report['workspace']['curves']

    def test_ideal_missile_full_application_and_signature(self):
        curves = self.ideal(fixture())
        self.assertTrue(curves['ideal'])
        for series in curves['series']:
            self.assertIsNone(series['currentX'])
            self.assertIsNone(series['currentY'])
        distance = dict(curves['series'][0]['points'])
        self.assertEqual(distance[0], 80)
        self.assertEqual(distance[2000], 80)
        self.assertEqual(distance[math.nextafter(2000, math.inf)], 0)
        signature = dict(curves['series'][1]['points'])
        self.assertEqual(signature[40], 32)
        self.assertEqual(signature[100], 80)
        self.assertTrue(all(y == 80 for _, y in curves['series'][2]['points']))

    def test_ideal_mixed_turrets_use_individual_class_references(self):
        report = fixture(kind='Turret')
        original = report['snapshot']['modules'][0]
        original.update(trackingSpeed=1, signatureResolutionMeters=100,
                        optimalRangeMeters=1000, falloffRangeMeters=1000)
        report['snapshot']['modules'] = [dict(copy.deepcopy(original), dogmaTypeId=i) for i in range(1, 5)]
        report['scenarioAnalysis'] = calculate_scenario(report, {}, {})
        curves = self.ideal(report, {i: {'attrs': {'128': float(i)}} for i in range(1, 5)})
        self.assertEqual(curves['references'], [('S / 护卫舰', 40), ('M / 巡洋舰', 125),
                                               ('L / 战列舰', 400), ('XL / 无畏舰', 3000)])
        for x, y in curves['series'][2]['points']:
            expected = sum(80 * turret_factor(0, x, radius, 1000, 1000, 1, 100)
                           for radius in (40, 125, 400, 3000))
            self.assertAlmostEqual(y, expected)
        for _, y in curves['series'][1]['points']:
            self.assertAlmostEqual(y, 320 * 1.01505)

    def test_ideal_empty_fit_and_unsupported_models(self):
        report = fixture()
        report['snapshot']['modules'] = []
        report['scenarioAnalysis'] = calculate_scenario(report, {}, {})
        curves = self.ideal(report)
        self.assertTrue(all(y == 0 for s in curves['series'] for _, y in s['points']))
        for report in (fixture(5), fixture(kind='Unknown')):
            self.assertEqual(self.ideal(report)['status'], 'unavailable')

    def build(self, report):
        attach_workspace_view(report, {'scenario': {'targetFitId': 'target'}}, {})
        return report['workspace']['curves']

    def test_current_points_and_missile_angular_invariance(self):
        report = fixture(); curves = self.build(report)
        for series in curves['series']:
            point = next(y for x, y in series['points'] if x == series['currentX'])
            self.assertEqual(point, 10)  # 80 DPS * 25/100 signature * 50% resistance
            self.assertEqual(point, series['currentY'])
        angular = next(s for s in curves['series'] if s['key'] == 'angular')
        self.assertTrue(all(y == 10 for x, y in angular['points']))
        signature = next(s for s in curves['series'] if s['key'] == 'signature')
        self.assertEqual(next(y for x, y in signature['points'] if x == 100), 40)

    def test_hard_range_boundary_is_not_smoothed(self):
        distance = self.build(fixture())['series'][0]
        points = dict(distance['points'])
        self.assertEqual(points[2000], 10)
        self.assertEqual(points[math.nextafter(2000, math.inf)], 0)

    def test_turret_range_and_tracking_independent_examples(self):
        r = fixture();m = r['snapshot']['modules'][0]
        m.update(applicationKind='Turret',trackingSpeed=1,signatureResolutionMeters=100,
                 optimalRangeMeters=1000,falloffRangeMeters=1000)
        r['scenarioAnalysis'] = calculate_scenario(r, {'distance':500,'signature':25,'angular':0,'speed':0,'resistances':[50,0,0,0]}, {})
        curves = self.build(r)
        distance = dict(curves['series'][0]['points']);angular = dict(curves['series'][2]['points'])
        self.assertAlmostEqual(distance[1000], 40.602)
        self.assertAlmostEqual(distance[3000], 2.305125)
        self.assertAlmostEqual(angular[.25], 15.802)

    def test_missing_target_or_model_does_not_emit_fake_curves(self):
        for r in [fixture(5), fixture(kind='Unknown')]:
            self.assertEqual(self.build(r)['status'], 'unavailable')
        r = fixture();r['scenarioLinks'] = {}
        self.assertEqual(self.build(r)['status'], 'unavailable')


if __name__ == '__main__':unittest.main()
