import json,subprocess,tempfile,unittest
from pathlib import Path
from nengine_bridge import NEngineBridge,NEngineError
from nengine_sessions import request

class NativeSessions(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
        self.client=NEngineBridge(state=self.root/'state')
        self.fit={'schemaVersion':1,'id':'qa','buildNumber':self.client.baseline['buildNumber'],
                  'shipTypeId':587,'omittedSkills':'untrained','name':'Initial','skills':{},'items':[]}
    def tearDown(self):self.client.close();self.temp.cleanup()
    def call(self,action,**args):return request(action,args,self.client)['result']
    def cli(self,command,**options):
        b=self.client
        args=[str(b.root/'.tools/dotnet/dotnet.exe'),str(b.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),
              command,'--data',str(b.root/b.baseline['dataDirectory']),'--session',str(b.state/'session-qa.json')]
        for key,value in options.items():args.extend(['--'+key,str(value)])
        p=subprocess.run(args,check=True,capture_output=True,encoding='utf-8')
        return json.loads(p.stdout)
    def test_durable_preview_apply_replay_history_and_cli(self):
        initial=self.call('create',sessionId='qa',fit=self.fit)
        self.assertEqual(initial['revision'],0);self.assertTrue(initial['dirty'])
        commands=[{'kind':'setName','name':'Edited'}]
        path=self.root/'commands.json';path.write_text(json.dumps(commands),encoding='utf-8')
        preview=self.call('preview',sessionId='qa',revision=0,commands=commands)
        self.assertEqual(preview,self.cli('eve-preview',revision=0,commands=path))
        self.assertEqual(self.call('inspect',sessionId='qa')['session'],initial)
        args=dict(sessionId='qa',revision=0,requestId='apply-1',operation='apply',commands=commands)
        applied=self.call('execute',**args);self.assertEqual(applied['working']['name'],'Edited')
        self.assertEqual(applied['appliedFitHash'],preview['candidateHash'])
        # Simulate a response lost after commit, then reconnect and retry exactly.
        self.client.close()
        replay=self.call('execute',**args);self.assertTrue(replay['replayed']);self.assertEqual(replay['revision'],1)
        self.assertEqual(replay,self.cli('eve-apply',revision=0,request='apply-1',commands=path))
        with self.assertRaises(NEngineError) as stale:self.call('execute',sessionId='qa',revision=0,requestId='save-stale',operation='save')
        self.assertEqual(stale.exception.error['code'],'STALE_REVISION')
        with self.assertRaises(NEngineError) as conflict:self.call('execute',**{**args,'operation':'save','commands':None})
        self.assertEqual(conflict.exception.error['code'],'REQUEST_CONFLICT')
        undo=self.call('execute',sessionId='qa',revision=1,requestId='undo-1',operation='undo')
        self.assertEqual(undo['working']['name'],'Initial')
        redo=self.cli('eve-redo',revision=2,request='redo-1')
        self.assertEqual(redo['working']['name'],'Edited')
        saved=self.call('execute',sessionId='qa',revision=3,requestId='save-1',operation='save')
        self.assertFalse(saved['dirty'])
        self.assertEqual(self.call('inspect',sessionId='qa'),self.cli('eve-inspect'))
        export=self.call('export',sessionId='qa',snapshot='saved')
        self.assertEqual(export,self.cli('eve-export',snapshot='saved',out=self.root/'export.json'))
        imported=self.call('import',sessionId='copy',document=export)
        self.assertEqual(imported['working'],saved['working']);self.assertFalse(imported['dirty'])
    def test_rejected_candidate_and_unknown_transport_fields(self):
        initial=self.call('create',sessionId='qa',fit=self.fit)
        commands=[{'kind':'install','item':{'id':'bad','typeId':2881,'slotIndex':7,'online':False,'active':False,'overheated':False}}]
        preview=self.call('preview',sessionId='qa',revision=0,commands=commands)
        self.assertFalse(preview['committable']);self.assertTrue(preview['analysis']['errors'])
        with self.assertRaises(NEngineError):self.call('execute',sessionId='qa',revision=0,requestId='bad',operation='apply',commands=commands)
        self.assertEqual(self.call('inspect',sessionId='qa')['session'],initial)
        with self.assertRaises(ValueError):self.call('inspect',sessionId='qa',unexpected=True)
        with self.assertRaises(ValueError):request('battle',{},self.client)

    def test_diagnostic_draft_policy_public_parity_and_import(self):
        fit={**self.fit,'items':[{'id':'bad','typeId':2881,'slotIndex':7,'online':False,'active':False,'overheated':False}]}
        created=self.call('create',sessionId='qa',fit=fit,allowIncompleteDraft=True)
        self.assertTrue(created['allowIncompleteDraft'])
        self.assertTrue(self.call('inspect',sessionId='qa')['analysis']['errors'])
        commands=[{'kind':'setName','name':'Diagnostic saved'}]
        path=self.root/'commands.json';path.write_text(json.dumps(commands),encoding='utf-8')
        preview=self.call('preview',sessionId='qa',revision=0,commands=commands)
        self.assertTrue(preview['committable']);self.assertTrue(preview['analysis']['errors'])
        self.assertEqual(preview,self.cli('eve-preview',revision=0,commands=path))
        applied=self.call('execute',sessionId='qa',revision=0,requestId='edit',operation='apply',commands=commands)
        self.assertEqual(applied['appliedFitHash'],preview['candidateHash'])
        saved=self.cli('eve-save',revision=1,request='save')
        self.assertFalse(saved['dirty']);self.assertTrue(saved['analysis']['errors'])
        export=self.call('export',sessionId='qa',snapshot='saved')
        self.assertEqual(export,self.cli('eve-export',snapshot='saved',out=self.root/'export.json'))
        imported=self.call('import',sessionId='copy',document=export)
        self.assertTrue(imported['allowIncompleteDraft']);self.assertFalse(imported['dirty'])
        # CLI creates an equivalent policy-bound initial draft in a separate directory.
        cli_state=self.root/'cli-state';cli_state.mkdir();old=self.client.state
        fit_path=self.root/'fit.json';fit_path.write_text(json.dumps(fit),encoding='utf-8')
        self.client.state=cli_state
        try:cli_created=self.cli('eve-new',fit=fit_path,**{'allow-incomplete-draft':'true'})
        finally:self.client.state=old
        self.assertEqual(created,cli_created)

    def test_preview_missing_resources_remain_null_in_mcp_and_cli(self):
        self.call('create',sessionId='qa',fit=self.fit,allowIncompleteDraft=True)
        commands=[{'kind':'setShip','shipTypeId':23913}]
        path=self.root/'commands.json';path.write_text(json.dumps(commands),encoding='utf-8')
        preview=self.call('preview',sessionId='qa',revision=0,commands=commands)
        self.assertEqual(preview,self.cli('eve-preview',revision=0,commands=path))
        fighter=next(r for r in preview['resourceDeltas'] if r['id']=='fighterBay')
        self.assertIsNone(fighter['capacityBefore']);self.assertIsNone(fighter['usedBefore'])
        self.assertEqual(fighter['reasonBefore'],'BASELINE_RESOURCE_UNAVAILABLE')
        self.assertEqual(fighter['stateAfter'],'available');self.assertGreater(fighter['capacityAfter'],0)
        cpu=next(r for r in preview['resourceDeltas'] if r['id']=='cpu')
        self.assertEqual(cpu['usedBefore'],0);self.assertEqual(cpu['stateBefore'],'available')
        self.assertFalse(preview['resourceComparisonComplete'])
        self.assertEqual(preview['baselineAnalysis'],self.client.call('fit_analyze',{'fit':self.fit})['result'])
        self.call('execute',sessionId='qa',revision=0,requestId='carrier',operation='apply',commands=commands)
        commands=[{'kind':'setShip','shipTypeId':587}];path.write_text(json.dumps(commands),encoding='utf-8')
        reverse=self.call('preview',sessionId='qa',revision=1,commands=commands)
        self.assertEqual(reverse,self.cli('eve-preview',revision=1,commands=path))
        fighter=next(r for r in reverse['resourceDeltas'] if r['id']=='fighterBay')
        self.assertIsNone(fighter['capacityAfter']);self.assertEqual(fighter['reasonAfter'],'CANDIDATE_RESOURCE_UNAVAILABLE')

if __name__=='__main__':unittest.main()
