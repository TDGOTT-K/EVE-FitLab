import json,subprocess,tempfile,unittest
from pathlib import Path
from nengine_adapter import analyze,bridge
from nengine_scenario import resolve_context
from nengine_curves import build_curves


class Edps(unittest.TestCase):
    @classmethod
    def tearDownClass(cls):bridge().close()

    def test_target_layers_query_parity_and_curves(self):
        fit={'name':'EDPS integration','shipId':587,'skills':[],
             'slots':[{'key':'high-0','kind':'high','item':2881,'ammo':185,'state':'Active'}],
             'scenario':{'targetFitId':'victim','distance':1000,'speed':100,'angular':.01}}
        victim={'id':'victim','name':'Target','shipId':587,'skills':[],'slots':[],'revision':3}
        results={}
        for layer in ['shield','armor','structure']:
            fit['scenario']['targetLayer']=layer
            target,source=resolve_context(fit,[victim],analyze)
            dps=analyze(fit,target=target)
            edps=analyze(dict(fit,attackMode='edps'),target=target)
            self.assertEqual(dps['nativeFit'],edps['nativeFit'])
            self.assertEqual(edps['outputContext']['selection']['metric'],'effectiveCycleDps')
            self.assertEqual(source['revision'],3)
            self.assertLess(edps['outputSelection']['total'],dps['outputSelection']['total'])
            result=bridge().call('fit_analyze',{'fit':edps['nativeFit'],'context':{'output':edps['outputContext']}})['result']
            self.assertEqual(result['outputContributions']['selection'],edps['outputSelection'])
            results[layer]=edps['outputSelection']['total']
        self.assertNotEqual(results['shield'],results['armor'])
        curves=build_curves(edps)
        self.assertEqual(curves['attackMode'],'edps')
        for series in curves['series']:
            row=next(row for row in series['points'] if row[0]==series['currentX'])
            self.assertAlmostEqual(row[1],edps['outputSelection']['total'])
            self.assertAlmostEqual(row[2],edps['native']['outputContributions']['comparison']['ratio']['value'])
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)
            (path/'fit.json').write_text(json.dumps(edps['nativeFit']),encoding='utf-8')
            (path/'context.json').write_text(json.dumps({'output':edps['outputContext']}),encoding='utf-8')
            client=bridge()
            subprocess.run([str(client.root/'.tools/dotnet/dotnet.exe'),str(client.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),
                'sde-fit','--data',str(client.root/client.baseline['dataDirectory']),'--fit',str(path/'fit.json'),
                '--metrics',str(path/'context.json'),'--out',str(path/'result.json')],check=True,capture_output=True)
            cli=json.loads((path/'result.json').read_text(encoding='utf-8-sig'))
            self.assertEqual(cli['outputContributions'],edps['native']['outputContributions'])

    def test_missing_layer_does_not_become_zero_and_mode_requires_target(self):
        fit={'name':'missing layer','shipId':587,'skills':[],
             'slots':[{'key':'high-0','kind':'high','item':2881,'ammo':185,'state':'Active'}],'attackMode':'edps'}
        self.assertEqual(analyze(fit)['attackMode'],'dps')
        target={'id':'t','distanceMeters':1000,'signatureMeters':40,'speedMetersPerSecond':0,'angularRadiansPerSecond':0}
        report=analyze(fit,target=target)
        self.assertIsNone(report['outputSelection']['total'])
        self.assertTrue(report['outputSelection']['exclusions'])


if __name__=='__main__':unittest.main()
