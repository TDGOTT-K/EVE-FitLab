import math
import unittest
from test_workspace_view import fixture
from fitting_scenario import calculate_scenario
from workspace_view import attach_workspace_view


class DpsCurveTests(unittest.TestCase):
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
