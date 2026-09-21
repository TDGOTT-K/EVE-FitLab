import math
import unittest
from nengine_scenario import resolve_target
from scenario_presets import validate_presets

class FeedbackVectors(unittest.TestCase):
    def target(self, own, target=(0,200)):
        fit={'scenario':{'targetFitId':'target','speed':999,'angular':99,
             'geometry':{'x':10000,'y':0,'vx':target[0],'vy':target[1],'ownVx':own[0],'ownVy':own[1]}}}
        report={'native':{'attributes':{'ship/552':{'value':40}},'fitHash':'fixture'},'source':{},'nativeFit':{}}
        return resolve_target(fit,[{'id':'target','name':'Target'}],lambda _:report)

    def test_same_direction_has_zero_angular_but_nonzero_missile_speed(self):
        t=self.target((0,200))
        self.assertEqual(t['speedMetersPerSecond'],200)
        self.assertEqual(t['angularRadiansPerSecond'],0)

    def test_own_velocity_changes_only_angular(self):
        a=self.target((0,0));b=self.target((0,-200))
        self.assertEqual(a['speedMetersPerSecond'],b['speedMetersPerSecond'])
        self.assertAlmostEqual(a['angularRadiansPerSecond'],.02)
        self.assertAlmostEqual(b['angularRadiansPerSecond'],.04)

    def test_stationary_target_is_stationary_for_missiles(self):
        t=self.target((0,300),(0,0))
        self.assertEqual(t['speedMetersPerSecond'],0)
        self.assertAlmostEqual(t['angularRadiansPerSecond'],.03)

    def test_presets_reject_invalid_own_vector(self):
        for n in [float('nan'),float('inf'),True,'200',1e9]:
            row={'id':'one','name':'one','value':{'geometry':{'x':10000,'y':0,'vx':0,'vy':200,'ownVx':n,'ownVy':0}}}
            with self.assertRaises(ValueError):validate_presets({'scenarios':[row],'activeScenarioId':'one'})

if __name__=='__main__':unittest.main()
