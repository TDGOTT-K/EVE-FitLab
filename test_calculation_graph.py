import unittest
from calculation_graph import literal,formula
class GraphTests(unittest.TestCase):
 def test_reject_mismatch(self):
  n=formula('dps','divide',[literal('shot',20),literal('cycle',2)],expected=11)
  self.assertFalse(n['complete']);self.assertEqual(n['formulaValue'],10)
 def test_missing_input(self):
  n=formula('dps','divide',[{'title':'missing','complete':False},literal('cycle',2)],expected=10)
  self.assertFalse(n['complete'])
 def test_zero_cycle(self):
  self.assertFalse(formula('dps','divide',[literal('shot',20),literal('cycle',0)])['complete'])
 def test_nested(self):
  n=formula('dps','divide',[formula('shot','multiply',[literal('base',20),literal('multiplier',2)]),literal('cycle',4)],expected=10)
  self.assertTrue(n['complete'])
if __name__=='__main__':unittest.main()
