import json
import os
from pathlib import Path
import tempfile
import unittest
from agent_mcp import AgentMcp, error_payload
from agent_research import project_timeline, unavailable


class ResearchTests(unittest.TestCase):
 def test_timestamps_are_not_substituted(self):
  result={'job':{'attempt':1,'timeUs':300000000,'complete':True},'ships':[{'id':'navy','hitpoints':13713,'destroyed':False}]}
  events=[{'kind':'ShipDestroyed','targetId':'t1','timeUs':66200000,'sequence':1},
   {'kind':'LayeredDamageResolved','targetId':'navy','timeUs':68700000,'sequence':2,'layeredDamage':{'appliedHitpoints':777,'defense':{'totalHitpoints':7680,'layers':[{'name':'shield','capacity':12000,'hitpoints':2430}]}}}]
  view=project_timeline(result,events);ship=view['ships'][0]
  self.assertEqual(view['destructions'][0]['timeUs'],66200000)
  self.assertEqual(ship['lastDamageSnapshot']['timeUs'],68700000)
  self.assertEqual(ship['lastDamageSnapshot']['hitpoints'],7680)
  self.assertEqual(ship['atSimulationEnd']['hitpoints'],13713)
  self.assertEqual(ship['atOtherShipDestruction']['state'],'unavailable')
  pending=unavailable({'id':'x','status':'running'});self.assertFalse(pending['ready']);self.assertEqual(pending['nextCall']['arguments']['waitSeconds'],10)
  self.assertIsNone(unavailable({'id':'x','status':'failed'})['nextCall'])
  self.assertIsNone(unavailable({'id':'x','status':'cancelled'}))
  partial=project_timeline({**result,'job':{**result['job'],'complete':False}},events)
  self.assertIsNone(partial['ships'][0]['atSimulationEnd'])
  self.assertEqual(partial['ships'][0]['atPartialCheckpoint']['hitpoints'],13713)

 def test_named_fitted_attributes_and_direct_state(self):
  with tempfile.TemporaryDirectory() as state:
   agent=AgentMcp(os.environ['FITLAB_NENGINE_ROOT'],state,os.environ.get('FITLAB_AGENT_MCP_DLL'))
   try:
    created=agent.call('fitlab_fit',{'action':'create','sessionId':'named','shipTypeId':621,'skillPreset':'all5'})
    self.assertIn('summary',created)
    raw=agent.call('fitlab_item',{'typeId':621,'names':['maxVelocity','shieldCapacity']})['value']
    self.assertEqual(len(raw['attributes']),2)
    fitted=agent.call('fitlab_fit',{'action':'attributes','sessionId':'named','names':['maxVelocity','shieldCapacity']})
    self.assertEqual(len(fitted['attributes']),2)
    self.assertTrue(all(r['value'] is not None for r in fitted['attributes']))
    missing=agent.call('fitlab_fit',{'action':'attributes','sessionId':'named','names':['not-an-attribute']})
    self.assertEqual(missing['matches'][0]['state'],'not_found')
    edit=agent.call('fitlab_fit',{'action':'edit','sessionId':'named','revision':0,'requestId':'fit','commands':[{'kind':'install','item':{'id':'extender','typeId':3831,'slotIndex':0,'active':True}}]})
    updated=agent.call('fitlab_fit',{'action':'state','sessionId':'named','revision':edit['revision'],'requestId':'state','instanceIds':['extender'],'active':False})
    self.assertEqual(updated['summary']['value']['errors']['count'],0)
    twin=agent.call('fitlab_fit',{'action':'create','sessionId':'target','shipTypeId':621,'skillPreset':'all5'})
    draft=agent.call('fitlab_battle',{'action':'prepare','id':'unarmed','seed':1,'seconds':1,'ships':[
     {'sessionId':'named','revision':updated['revision'],'id':'a','team':'a','position':{'x':0,'y':0,'z':0}},
     {'sessionId':'target','revision':twin['revision'],'id':'b','team':'b','position':{'x':1000,'y':0,'z':0}}]})
    self.assertTrue(all(not s['supplies'] for s in draft['initialConditions']))
   finally:agent.close()

 @unittest.skipUnless(os.environ.get('FITLAB_RESEARCH_STATE'),'Historical jobs are optional local evidence')
 def test_three_real_published_jobs(self):
  agent=AgentMcp(os.environ['FITLAB_NENGINE_ROOT'],os.environ['FITLAB_RESEARCH_STATE'],os.environ.get('FITLAB_AGENT_MCP_DLL'))
  try:
   report=agent.call('fitlab_battle',{'action':'result','jobId':'job-caracal-1v1'})['value']
   timeline=report['timeline'];self.assertEqual(timeline['destructions'][0]['timeUs'],66200000)
   navy=next(s for s in timeline['ships'] if s['shipId']=='navy')
   self.assertAlmostEqual(navy['lastDamageSnapshot']['hitpoints'],7679.942673694291)
   self.assertAlmostEqual(navy['atSimulationEnd']['hitpoints'],13713.4755508305)
   for job,time_us in [('job-t1-vs-merlin',85000000),('job-navy-vs-merlin',50519710)]:
    value=agent.call('fitlab_battle',{'action':'result','jobId':job})['value']
    self.assertEqual(value['timeline']['destructions'][0]['timeUs'],time_us)
    filtered=agent.call('fitlab_battle',{'action':'events','jobId':job,'kinds':['ShipDestroyed'],'targetId':'merlin'})['value']
    self.assertEqual(len(filtered['events']),1);self.assertEqual(filtered['events'][0]['timeUs'],time_us)
   Path('output').mkdir(exist_ok=True)
   Path('output/research-timeline-verification.json').write_text(json.dumps(timeline,ensure_ascii=False,indent=2),encoding='utf-8')
  finally:agent.close()


if __name__=='__main__':unittest.main()
