import copy,json,subprocess,tempfile,unittest,uuid
from pathlib import Path
from nengine_bridge import NEngineBridge,NEngineError
from nengine_persistence import save_to_library,SaveConflict

class NativePersistence(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.client=NEngineBridge(state=Path(self.temp.name)/'native')
        self.library={'fits':[],'characters':[]};self.writes=0
    def tearDown(self):self.client.close();self.temp.cleanup()
    def write(self,value):self.library=copy.deepcopy(value);self.writes+=1
    def fit(self):return {'id':str(uuid.uuid4()),'name':'Persistence','shipId':587,'slots':[],'skills':[],
                          '_saveRequestId':str(uuid.uuid4())}
    def save(self,fit,write=None):return save_to_library(fit,self.library,write or self.write,'test-time',self.client)
    def export(self,row):return self.client.call('fit_export',{'sessionId':row['nativeSession']['id'],'snapshot':'saved'})['result']
    def test_saved_snapshot_public_cli_and_replay(self):
        fit=self.fit();saved=self.save(fit);self.assertEqual(saved['revision'],1)
        document=self.export(saved);self.assertEqual(document['fitHash'],saved['nativeSession']['fitHash'])
        self.assertEqual(document['fit']['id'],saved['id']);self.assertTrue(document['allowIncompleteDraft'])
        self.assertEqual(self.save(fit),saved);self.assertEqual(self.writes,1)
        b=self.client;out=Path(self.temp.name)/'export.json'
        cli=subprocess.run([str(b.root/'.tools/dotnet/dotnet.exe'),str(b.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),
            'eve-export','--data',str(b.root/b.baseline['dataDirectory']),'--session',str(b.state/('session-'+saved['nativeSession']['id']+'.json')),
            '--snapshot','saved','--out',str(out)],check=True,capture_output=True,encoding='utf-8')
        self.assertEqual(json.loads(cli.stdout),document)
        with self.assertRaises(SaveConflict):self.save({**fit,'name':'Different contents same request'})
        changed={**saved,'name':'Second','_saveRequestId':str(uuid.uuid4())}
        next_saved=self.save(changed);self.assertEqual(next_saved['revision'],2)
        self.assertEqual(next_saved['nativeSession']['revision'],3)
        self.assertEqual(self.export(next_saved)['fit']['name'],'Second')
        with self.assertRaises(SaveConflict):self.save({**fit,'_saveRequestId':str(uuid.uuid4())})
    def test_library_failure_after_native_save_recovers_without_duplicate(self):
        fit=self.fit()
        def fail(_):raise OSError('disk write failed')
        with self.assertRaises(OSError):self.save(fit,fail)
        self.assertEqual(self.library['fits'],[])
        saved=self.save(fit);self.assertEqual(saved['nativeSession']['revision'],1);self.assertEqual(len(self.library['fits']),1)
        changed={**saved,'name':'Changed','_saveRequestId':str(uuid.uuid4())}
        with self.assertRaises(OSError):self.save(changed,fail)
        self.assertEqual(self.library['fits'][0]['name'],'Persistence')
        updated=self.save(changed);self.assertEqual(updated['nativeSession']['revision'],3)
        self.assertEqual(len(self.library['fits']),1)
    def test_native_conflict_and_diagnostic_draft_preserve_ui_library(self):
        fit=self.fit();fit['slots']=[{'key':'high-7','kind':'high','item':2881,'state':'Offline'}]
        saved=self.save(fit);before=copy.deepcopy(self.library)
        self.assertEqual(self.export(saved)['fit']['items'][0]['slotIndex'],7)
        reference=saved['nativeSession']
        self.client.call('fit_execute',{'sessionId':reference['id'],'revision':reference['revision'],
            'requestId':'external-edit','operation':'apply','commands':[{'kind':'setName','name':'External'}]})
        with self.assertRaises(NEngineError) as conflict:self.save({**saved,'name':'UI change','_saveRequestId':str(uuid.uuid4())})
        self.assertEqual(conflict.exception.error['code'],'STALE_REVISION');self.assertEqual(self.library,before)

if __name__=='__main__':unittest.main()
