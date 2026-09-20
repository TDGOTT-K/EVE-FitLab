import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from agent_api import AgentApi
from agent_mcp import AgentMcp
from agent_contract import tools,OPS


class AgentPlatformTests(unittest.TestCase):
    def test_frozen_contract(self):
        self.assertEqual(json.loads(Path('contracts/agent-mcp-v5/tools.json').read_text(encoding='utf-8'))['tools'],tools())
        self.assertEqual(json.loads(Path('contracts/agent-mcp-v5/operations.json').read_text(encoding='utf-8')),json.loads(json.dumps(OPS)))

    def test_general_tasks_and_cli_parity(self):
        c=json.loads(Path(os.environ.get('FITLAB_TEST_CONFIG','output/agent-v5-config.json')).read_text(encoding='utf-8'))
        with tempfile.TemporaryDirectory() as state:
            api=AgentApi(AgentMcp(c['engine'],state,c['mcp_dll'],c['baseline']))
            trace=[]
            def call(domain,action,args):
                r=api.call(domain,action,args);trace.append({'domain':domain,'action':action,'state':r['state']})
                self.assertTrue(r['ok'],r);return r
            try:
                traits=call('catalog','describe',{'name':'狞獾级'})
                self.assertEqual(traits['data']['typeId'],621)
                self.assertTrue(any(t['kind']=='skill_per_level' for t in traits['data']['traits']))
                ambiguous=call('catalog','describe',{'name':'Caracal Navy'})
                self.assertEqual(ambiguous['state'],'needs_selection')
                bad=api.call('fitting','edit',{'sessionId':'missing'})
                self.assertFalse(bad['ok']);self.assertTrue(bad['next'])
                samples=[('laser',597,3017,246),('hybrid',594,3170,222),('projectile',587,2889,185),('missile',602,2404,210)]
                curves={}
                target={'distanceMeters':1000,'signatureMeters':40,'speedMetersPerSecond':100,'angularRadiansPerSecond':0.02}
                for family,ship,weapon,charge in samples:
                    call('fitting','create',{'sessionId':family,'shipTypeId':ship,'skillPreset':'all5'})
                    changes=[{'kind':'install','item':{'id':'weapon','typeId':weapon,'slotIndex':0,'chargeTypeId':charge,'active':True}}]
                    preview=call('fitting','preview',{'sessionId':family,'revision':0,'changes':changes})
                    self.assertEqual(preview['state'],'preview')
                    self.assertEqual(call('fitting','read',{'sessionId':family})['data']['revision'],0)
                    edit=call('fitting','edit',{'sessionId':family,'revision':0,'requestId':'equip','changes':changes})
                    self.assertEqual(edit['data']['summary']['errors']['count'],0,edit)
                    replay=call('fitting','edit',{'sessionId':family,'revision':0,'requestId':'equip','changes':changes})
                    self.assertTrue(replay['data']['replayed'])
                    curves[family]=call('fitting','curves',{'sessionId':family,'target':target,'intervals':4})['data']
                    self.assertEqual(curves[family]['state'],'ready',curves[family])
                    self.assertEqual(len(curves[family]['series']),3)
                    self.assertEqual(curves[family]['query']['target']['speedMetersPerSecond'],100)
                    output=call('fitting','outputs',{'sessionId':family})
                    self.assertEqual(output['data']['outputs'][0]['id'],'module.weapon/attack')
                library=call('fitting','list',{'limit':2});self.assertEqual(library['data']['total'],4);self.assertTrue(library['next'])
                # No hand-made formulas: projected curve samples equal public engine values.
                full=api.core.load_result(curves['missile']['detailResultId'])
                self.assertEqual([p['value'] for p in curves['missile']['series'][0]['points']],[(p['selection'] or {}).get('total') for p in full['series'][0]['points']])
                # A small real battle, unrelated to Caracal, through task API and identity-bound report.
                request={'id':'frigate-study','jobId':'frigate-study','seed':42,'seconds':12,'waitSeconds':60,
                    'ships':[{'id':'a','team':'A','sessionId':'missile','revision':1,'position':{'x':0,'y':0,'z':0},'reservePerWeapon':10},
                             {'id':'b','team':'B','sessionId':'projectile','revision':1,'position':{'x':1000,'y':0,'z':0},'reservePerWeapon':10}]}
                battle=call('battle','run',request)
                while battle['state']=='pending':battle=call('battle','wait',{'jobId':'frigate-study','waitSeconds':60})
                self.assertEqual(battle['state'],'complete',battle)
                self.assertEqual(battle['data']['provenance']['state'],'bound')
                self.assertTrue(call('battle','run',request)['data']['replayed'])
                report=call('battle','report',{'jobId':'frigate-study','expectedDraftId':battle['data']['provenance']['draftId']})
                self.assertFalse(api.call('battle','report',{'jobId':'frigate-study','expectedDraftId':'wrong'})['ok'])
                self.assertTrue(all(s['atOtherShipDestruction']['state']=='unavailable' for s in report['data']['shipSnapshots']))
                jobs=call('jobs','list',{});self.assertEqual(jobs['data']['items'][0]['id'],'frigate-study')
                revision=jobs['data']['items'][0]['revision']
                for command,tool,extra,native_args in [
                    ('battle-list','battle_list',[],{}),
                    ('battle-manifest','battle_manifest',['--id','frigate-study'],{'jobId':'frigate-study'}),
                    ('battle-compact','battle_compact',['--id','frigate-study','--revision',str(revision)],{'jobId':'frigate-study','expectedRevision':revision})]:
                    run=subprocess.run([str(Path(c['engine'])/'.tools/dotnet/dotnet.exe'),str(Path(c['mcp_dll']).parent/'NEngine.Cli.dll'),command,'--state',state,*extra],capture_output=True,encoding='utf-8')
                    self.assertEqual(run.returncode,0,run.stderr)
                    self.assertEqual(json.loads(run.stdout),api.native(tool,native_args))

                dry=call('jobs','compact',{'jobId':'frigate-study','revision':revision})
                self.assertGreater(dry['data']['reclaimableBytes'],0)
                call('jobs','compact',{'jobId':'frigate-study','revision':revision,'dryRun':False})
                after=call('battle','report',{'jobId':'frigate-study'})
                self.assertEqual(after['data'],report['data'])
                # Same CLI dispatcher: no MCP wrapper/references needed to read a simple ship fact.
                config=Path(state)/'config.json';config.write_text(json.dumps({**c,'state':state}),encoding='utf-8')
                run=subprocess.run(['python','fitlab.py','--config',str(config),'catalog','describe','--name','狞獾级'],capture_output=True,encoding='utf-8')
                self.assertEqual(run.returncode,0,run.stderr)
                self.assertEqual(json.loads(run.stdout),traits)
                # CLI-created battle remains alive until completed, not cancelled by process shutdown.
                path=Path(state)/'request.json';path.write_text(json.dumps({**request,'jobId':'cli-frigates','id':'cli-frigates','seconds':3,'waitSeconds':0}),encoding='utf-8')
                run=subprocess.run(['python','fitlab.py','--config',str(config),'battle','run','--input',str(path)],capture_output=True,encoding='utf-8',timeout=120)
                self.assertEqual(run.returncode,0,run.stderr);self.assertEqual(json.loads(run.stdout)['state'],'complete')
                Path('output/agent-v5-acceptance.json').write_text(json.dumps({'trace':trace,'curves':curves,'battle':report,'cliParity':True,'cliWorkerLifecycle':True},ensure_ascii=False,indent=2),encoding='utf-8')
            finally:api.close()


if __name__=='__main__':unittest.main()
