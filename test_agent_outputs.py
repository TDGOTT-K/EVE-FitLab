import json
from pathlib import Path
import tempfile
import unittest
from agent_api import AgentApi
from agent_mcp import AgentMcp,encoded
from agent_contract import operation_for_tool


class OutputTests(unittest.TestCase):
    def test_carrier_output_and_recoverable_curves(self):
        c=json.loads(Path('agent-config.json').read_text(encoding='utf-8'))
        with tempfile.TemporaryDirectory() as state:
            api=AgentApi(AgentMcp(c['engine'],state,c['mcp_dll'],c['baseline']))
            def call(d,a,args):
                result=api.call(d,a,args);self.assertTrue(result['ok'],result);return result
            def follow(next_call):return call(*operation_for_tool(next_call['tool']),next_call['arguments'])
            try:
                call('fitting','create',{'sessionId':'carrier','shipTypeId':23911,'skillPreset':'all5'})
                fighters=[{'id':'sq'+str(n),'typeId':40558,'memberIds':['m'+str(n)+'-'+str(i) for i in range(6)],'deployed':True,'location':'tube','tubeIndex':n} for n in range(3)]
                args={'sessionId':'carrier','revision':0,'requestId':'load','changes':[{'kind':'setFighters','fighters':fighters}]}
                edited=call('fitting','edit',args)
                inspected=api.native('fit_inspect',{'sessionId':'carrier'})
                totals=edited['data']['summary']['outputTotals']
                self.assertEqual(totals['deployedFighterPrimaryNominalDps']['value'],inspected['analysis']['fighterPrimaryNominalDps'])
                self.assertGreater(totals['deployedFighterPrimaryNominalDps']['value'],0)
                replay=call('fitting','edit',args);self.assertEqual(replay['data']['summary']['outputTotals'],totals)
                outputs=call('fitting','outputs',{'sessionId':'carrier'})
                self.assertEqual(len(outputs['data']['outputs']),9)
                self.assertLess(len(encoded(outputs)),18000)
                raw=inspected['analysis']['outputContributions']['items']
                for row in outputs['data']['outputs']:
                    original=next(i for i in raw if i['id']==row['id'])
                    for key,value in row['metrics'].items():
                        for field,v in value.items():self.assertEqual(v,original['metrics'][key].get(field))
                first=call('fitting','outputs',{'sessionId':'carrier','limit':2});ids=[];page=first
                while True:
                    ids.extend(i['id'] for i in page['data']['outputs'])
                    if not page['next']:break
                    page=follow(page['next'][0])
                self.assertEqual(ids,[i['id'] for i in raw])
                chosen=[i['id'] for i in raw if i['kind'] in ('fighter_primary','fighter_rockets')]
                target={'distanceMeters':5000,'signatureMeters':385,'speedMetersPerSecond':0,'angularRadiansPerSecond':0}
                failed=call('fitting','curves',{'sessionId':'carrier','contributionIds':chosen,'metric':'appliedCycleDps','target':target,'intervals':4})
                self.assertEqual(failed['state'],'unavailable')
                reasons=[d for d in failed['data']['selectionDiagnostics'] if d.get('kind')=='fighter_rockets']
                self.assertTrue(all(d['reason']=='FINITE_ABILITY_USE_LOADED_CYCLE_BASIS' for d in reasons))
                self.assertEqual(len(failed['next']),2)
                recovered=[follow(n) for n in failed['next']]
                self.assertTrue(all(r['state']=='ready' for r in recovered),recovered)
                self.assertTrue(all(r['data']['query']['target']['signatureMeters']==385 for r in recovered))
                missing=call('fitting','curves',{'sessionId':'carrier','contributionIds':['unknown'],'target':target,'intervals':4})
                self.assertEqual(missing['state'],'unavailable');self.assertEqual(missing['data']['selectionDiagnostics'][0]['state'],'not_found')
                # Changed roster invalidates paginated/recovery reads; no stale experiment comparison.
                call('fitting','edit',{'sessionId':'carrier','revision':1,'requestId':'clear','changes':[{'kind':'setFighters','fighters':[]}]})
                stale=api.call(*operation_for_tool(first['next'][0]['tool']),first['next'][0]['arguments'])
                self.assertFalse(stale['ok']);self.assertEqual(stale['issues'][0]['code'],'FIT_CHANGED')
                stale=api.call(*operation_for_tool(failed['next'][0]['tool']),failed['next'][0]['arguments'])
                self.assertEqual(stale['issues'][0]['code'],'FIT_CHANGED')
                # All generic bounded responses expose their follow-up at envelope level.
                original_dispatch=api.dispatch
                api.dispatch=lambda *args:(api.bound({'large':'x'*30000}),'ready',[],[])
                bounded=call('help','overview',{})
                self.assertEqual(bounded['next'][0],bounded['data']['next'])
                api.dispatch=original_dispatch
                self.assertTrue(follow(bounded['next'][0])['data'])
                Path('output/agent-v7-output-verification.json').write_text(json.dumps({'editedTotals':totals,'outputs':outputs,'failedCurve':failed,'recoveredCurves':recovered,'boundedNavigation':bounded},ensure_ascii=False,indent=2),encoding='utf-8')
            finally:api.close()


if __name__=='__main__':unittest.main()
