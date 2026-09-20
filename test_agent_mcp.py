import json
import os
from pathlib import Path
import tempfile
import unittest
from agent_mcp import AgentMcp, TOOLS, encoded


class AgentTests(unittest.TestCase):
 def test_public_workflow_and_bounded_references(self):
  root=os.environ['FITLAB_NENGINE_ROOT'];dll=os.environ.get('FITLAB_AGENT_MCP_DLL')
  with tempfile.TemporaryDirectory() as state:
   agent=AgentMcp(root,state,dll)
   try:
    self.assertLess(len(encoded(TOOLS)),8000)
    frozen=json.loads(Path('contracts/agent-mcp-v2/tools.json').read_text(encoding='utf-8'))
    self.assertEqual(frozen['tools'],TOOLS)
    status=agent.call('fitlab_status',{});self.assertEqual(status['contractRevision'],60)
    searches=agent.call('fitlab_search',{'names':['Phantasm','Heavy Pulse Laser II']})
    self.assertIn(17718,[i['typeId'] for i in searches['value'][0]['candidates']])
    created=agent.call('fitlab_fit',{'action':'create','sessionId':'test-agent','shipTypeId':17718,'skills':{'3318':0,'3424':0}})
    self.assertEqual(created['revision'],0)
    edit={'action':'edit','sessionId':'test-agent','revision':0,'requestId':'install-1','commands':[{'kind':'install','item':{'id':'gun','typeId':3520,'chargeTypeId':254,'slotIndex':0,'active':True}},{'kind':'install','item':{'id':'cap','typeId':2032,'slotIndex':0}}]}
    result=agent.call('fitlab_fit',edit);self.assertEqual(result['revision'],1)
    retry=agent.call('fitlab_fit',edit);self.assertTrue(retry['replayed']);self.assertEqual(retry['revision'],1)
    self.assertLess(len(encoded(result)),16000)
    read=agent.call('fitlab_fit',{'action':'read','sessionId':'test-agent'})
    raw=agent.read_result({'resultId':read['fullResultId'],'pointer':'/analysis/nominalDps'})
    summary=agent.read_result({'resultId':read['summary']['resultId'],'pointer':'/nominalDps'})
    self.assertEqual(raw['value'],summary['value'])
    full=agent.native('fit_inspect',{'sessionId':'test-agent'})
    self.assertEqual(full['analysis']['nominalDps'],raw['value'])
    undo=agent.call('fitlab_fit',{'action':'undo','sessionId':'test-agent','revision':1,'requestId':'undo-1'})
    self.assertEqual(undo['revision'],2)
    redo=agent.call('fitlab_fit',{'action':'redo','sessionId':'test-agent','revision':2,'requestId':'redo-1'})
    self.assertEqual(redo['revision'],3)
    saved=agent.call('fitlab_fit',{'action':'save','sessionId':'test-agent','revision':3,'requestId':'save-1'})
    self.assertEqual(saved['revision'],4)
    with self.assertRaises(Exception):agent.call('fitlab_fit',{'action':'edit','sessionId':'test-agent','revision':4,'requestId':'bad-1','commands':[{'kind':'remove','instanceId':'missing'}]})
    self.assertEqual(agent.call('fitlab_fit',{'action':'read','sessionId':'test-agent'})['revision'],4)
    schema=agent.call('fitlab_tools',{'name':'battle_start'})
    self.assertLess(len(encoded(schema)),12000)
    value=agent.read_result({'resultId':schema['schema']['resultId'],'pointer':'/properties'})
    self.assertIn('entries',value)
    with self.assertRaises(ValueError):agent.read_result({'resultId':'../session-test-agent'})
    ref=agent.store({'a/b':{'~key':list(range(10000))},'nil':None})
    page=agent.read_result({'resultId':ref,'pointer':'/a~1b/~0key','offset':30,'limit':2})
    self.assertEqual([e['value'] for e in page['entries']],[30,31])
    self.assertEqual(page['nextOffset'],32)
    durable=agent.store({'kind':'test-durable'},durable=True)
    for i in range(130):agent.store({'eviction':i})
    self.assertEqual(agent.load_result(durable)['kind'],'test-durable')
    ref=agent.store({'nil':None})
   finally:agent.close()
   reopened=AgentMcp(root,state,dll)
   try:self.assertIsNone(reopened.read_result({'resultId':ref,'pointer':'/nil'})['value'])
   finally:reopened.close()


if __name__=='__main__':unittest.main()
