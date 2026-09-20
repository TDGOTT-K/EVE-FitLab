import json
from pathlib import Path
import tempfile
import unittest
from agent_api import AgentApi
from agent_mcp import AgentMcp
from agent_contract import tools,validate,CHANGE


class V6Tests(unittest.TestCase):
    def test_truthful_schema(self):
        schemas={t['name']:t['inputSchema'] for t in tools()}
        self.assertIn('sessionId',schemas['fitlab_fitting_create']['required'])
        self.assertNotIn('requestId',schemas['fitlab_fitting_preview']['properties'])
        self.assertIn('requestId',schemas['fitlab_fitting_edit']['required'])
        for bad in [{'kind':'setActive','instanceId':'a'},{'kind':'setFighters','fighters':[],'item':{'id':'x','typeId':1,'slotIndex':0}}]:
            with self.assertRaises(ValueError):validate(bad,CHANGE)
        validate({'kind':'setFighters','fighters':[]},CHANGE)

    def test_carrier_and_native_hardener_policy(self):
        c=json.loads(Path('agent-config.json').read_text(encoding='utf-8'))
        with tempfile.TemporaryDirectory() as state:
            api=AgentApi(AgentMcp(c['engine'],state,c['mcp_dll'],c['baseline']))
            def call(d,o,a):
                r=api.call(d,o,a);self.assertTrue(r['ok'],r);return r
            try:
                call('fitting','create',{'sessionId':'carrier','shipTypeId':23911,'skillPreset':'all5'})
                fighters=[{'id':'sq'+str(n),'typeId':40558,'memberIds':['m'+str(n)+'-'+str(i) for i in range(6)],'deployed':True,'location':'tube','tubeIndex':n} for n in range(3)]
                change=[{'kind':'setFighters','fighters':fighters}]
                preview=call('fitting','preview',{'sessionId':'carrier','revision':0,'changes':change})
                self.assertEqual(preview['data']['errors'],[])
                self.assertEqual(call('fitting','roster',{'sessionId':'carrier'})['data']['collections']['fighters'],[])
                edit=call('fitting','edit',{'sessionId':'carrier','revision':0,'requestId':'fighters','changes':change});self.assertEqual(edit['state'],'ready')
                roster=call('fitting','roster',{'sessionId':'carrier'});self.assertEqual(roster['data']['errors'],[])
                self.assertGreater(roster['data']['projections']['fighterPrimaryNominalDps'],0)
                curves=call('fitting','curves',{'sessionId':'carrier','contributionIds':['fighter.sq0/primary','fighter.sq1/primary','fighter.sq2/primary'],
                    'target':{'distanceMeters':1000,'signatureMeters':400,'speedMetersPerSecond':0,'angularRadiansPerSecond':0},'intervals':4})
                self.assertEqual(curves['state'],'ready',curves)
                hardener,_=api.identity({'name':'Multispectrum Shield Hardener II'})
                call('fitting','create',{'sessionId':'defense','shipTypeId':621,'skillPreset':'all5'})
                call('fitting','edit',{'sessionId':'defense','revision':0,'requestId':'install','changes':[{'kind':'install','item':{'id':'hardener','typeId':hardener,'slotIndex':0,'active':True}}]})
                call('fitting','create',{'sessionId':'target','shipTypeId':621,'skillPreset':'all5'})
                prepared=call('battle','prepare',{'id':'self-defense','seed':1,'seconds':12,'ships':[
                    {'sessionId':'defense','revision':1,'id':'a','team':'A','position':{'x':0,'y':0,'z':0}},
                    {'sessionId':'target','revision':0,'id':'b','team':'B','position':{'x':1000,'y':0,'z':0}}]})
                draft=prepared['data']['draftId']
                support=call('battle','policies',{'draftId':draft})['data']['presets']
                self.assertFalse(support[0]['compatible']);self.assertTrue(support[1]['compatible'])
                denied=api.call('battle','start',{'draftId':draft,'jobId':'denied','policyPreset':'stationary-weapons-v1'})
                self.assertFalse(denied['ok']);self.assertEqual(denied['issues'][0]['code'],'POLICY_CAPABILITY_MISMATCH')
                result=call('battle','start',{'draftId':draft,'jobId':'defense-test','policyPreset':'stationary-weapons-defense-v1','waitSeconds':60})
                self.assertEqual(result['state'],'complete',result)
                raw=api.native('battle_result',{'jobId':'defense-test'})
                ability=next(s for s in raw['ships'] if s['id']=='a')['abilities'][0]
                self.assertGreater(ability['activations'],0,raw)
                self.assertEqual(call('fitting','read',{'sessionId':'defense'})['data']['revision'],1)
                Path('output/agent-v6-regression.json').write_text(json.dumps({'carrier':roster,'curves':curves,'policySupport':support,'battle':result,'hardenerActivations':ability['activations']},ensure_ascii=False,indent=2),encoding='utf-8')
            finally:api.close()


if __name__=='__main__':unittest.main()
