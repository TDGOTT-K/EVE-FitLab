import unittest
from fitting_scenario import validate_scenario
class ScenarioTests(unittest.TestCase):
 def test_invalid(self):
  for x in [{'signature':0},{'angular':float('nan')},{'resistances':[101,0,0,0]}]:
   with self.assertRaises(ValueError):validate_scenario(x)
