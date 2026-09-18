import copy,json,subprocess,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
from nengine_bridge import NEngineBridge
from nengine_adapter import analyze
from nengine_preview import preview_fit

class NativePreview(unittest.TestCase):
    def test_preview_matches_installed_analysis_and_cli_without_sessions(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);client=NEngineBridge(state=root/'state')
            before={'name':'Preview','shipId':587,'skills':[],'slots':[]}
            candidate={**before,'slots':[{'key':'high-0','kind':'high','item':2881,'ammo':185,'state':'Active'}]}
            target={'id':'target','distanceMeters':10000,'signatureMeters':40,'speedMetersPerSecond':200,'angularRadiansPerSecond':.02}
            try:
                with patch('nengine_adapter.bridge',return_value=client),patch('nengine_preview.bridge',return_value=client):
                    for case in ('install','unchanged','target','diagnostic'):
                        after=copy.deepcopy(candidate if case!='unchanged' else before)
                        if case=='diagnostic':after['slots'][0]['key']='high-7'
                        current_target=target if case=='target' else None
                        evaluate=lambda f,native_query=None:analyze(f,target=current_target,native_query=native_query)
                        original=copy.deepcopy(after)
                        preview=preview_fit(before,after,evaluate)
                        native_preview=preview.pop('editPreview');requests=preview.pop('editPreviewRequests')
                        self.assertEqual(preview,evaluate(after),case)
                        self.assertEqual(after,original)
                        self.assertNotIn('inventory',preview['nativeFit'])
                        if case=='diagnostic':self.assertFalse(preview['isValid'])
                        last=requests[-1]
                        for key in ('fit','commands','context'):(root/(key+'.json')).write_text(json.dumps(last[key]),encoding='utf-8')
                        b=client
                        result=subprocess.run([str(b.root/'.tools/dotnet/dotnet.exe'),str(b.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),
                          'eve-preview-input','--data',str(b.root/b.baseline['dataDirectory']),'--fit',str(root/'fit.json'),
                          '--commands',str(root/'commands.json'),'--metrics',str(root/'context.json')],check=True,capture_output=True,encoding='utf-8')
                        self.assertEqual(json.loads(result.stdout),native_preview,case)
                    self.assertEqual(list(client.state.glob('session-*.json')),[])
            finally:client.close()

if __name__=='__main__':unittest.main()
