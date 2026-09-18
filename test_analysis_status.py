import copy,unittest
from analysis_status import classify_report

class Status(unittest.TestCase):
 def report(self):
  return {'native':{'errors':[],'warnings':[],'staticCoverageComplete':True,'coverage':[], 'attributes':{'ship/76':{'value':0}}},'outputSelection':{'completeSelection':True}}
 def test_legal_partial_and_invalid_partial_are_distinct(self):
  r=self.report();self.assertEqual(classify_report(r)['state'],'valid')
  r['outputSelection']={'completeSelection':False,'status':'partial','exclusions':[{'reason':'UNKNOWN'}]}
  s=classify_report(r);self.assertEqual((s['state'],s['legality']),('partial','valid'))
  warning={'code':'RESOURCE_EXCEEDED','details':{'used':10,'limit':1}}
  r['native']['warnings']=[warning];s=classify_report(r)
  self.assertEqual((s['state'],s['legality'],s['completeness']),('invalid','invalid','partial'))
  self.assertEqual(s['admissionIssues'],[warning])
 def test_no_output_is_not_unknown_output_and_cap_can_be_partial(self):
  r=self.report();r['outputSelection']={'completeSelection':False,'status':'empty_selection'}
  self.assertEqual(classify_report(r)['state'],'valid')
  r['capacitorScenario']={'state':'unavailable','reason':'missing inventory'}
  self.assertEqual(classify_report(r)['state'],'partial')
 def test_uncovered_static_never_reports_valid(self):
  r=self.report();r['native']['staticCoverageComplete']=False;r['native']['attributes']={}
  before=copy.deepcopy(r);s=classify_report(r)
  self.assertEqual((s['state'],s['legality']),('unsupported','unknown'))
  self.assertEqual(r,before)
 def test_integrated_partial_validation_and_capacitor_contributions(self):
  r=self.report();r['native']['capacitorUnavailableReason']='CAPACITOR_CONTRIBUTIONS_INCOMPLETE'
  r['native']['capacitorContributions']=[{'instanceId':'high-0','state':'unavailable','reason':'EVE_CAPACITOR_EFFECT'}]
  s=classify_report(r);self.assertEqual((s['legality'],s['state']),('valid','partial'))
  self.assertEqual(s['unavailable'][0]['exclusions'][0]['instanceId'],'high-0')
  r['native']['validationState']='partial_static_dependencies'
  self.assertEqual(classify_report(r)['legality'],'unknown')

if __name__=='__main__':unittest.main()
