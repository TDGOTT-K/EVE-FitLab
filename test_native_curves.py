import json,subprocess,tempfile,unittest,sys
from pathlib import Path
from nengine_adapter import analyze,bridge
from nengine_curves import build_curves

class NativeCurves(unittest.TestCase):
    @classmethod
    def tearDownClass(cls):bridge().close()
    def fit(self):return {'name':'Curve policy','shipId':587,'skills':[],'slots':[{'key':'high-0','kind':'high','item':2881,'ammo':185,'state':'Active'}]}
    def test_ideal_and_edps_public_full_result_parity(self):
        b=bridge();sys.path.insert(0,str(b.root/'.tools/u3-python'));import jsonschema
        schema=json.loads((b.root/'contracts/headless-v1/r36/output-schemas.json').read_text())['schemas']['fit_output_curves']
        target={'id':'target','distanceMeters':1000,'signatureMeters':40,'speedMetersPerSecond':100,'angularRadiansPerSecond':.02,'layer':{'name':'armor','resonances':{'em':.5,'thermal':.8,'kinetic':.4,'explosive':.3}}}
        for chosen in (None,target):
            fit=self.fit();fit['attackMode']='edps' if chosen else 'dps';report=analyze(fit,target=chosen);view=build_curves(report);native=view['native']
            self.assertEqual(view['status'],'ready',view.get('reason'));jsonschema.Draft202012Validator(schema).validate(native)
            self.assertEqual(native['ideal'],chosen is None)
            for series,presented in zip(native['series'],view['series']):
                values=[p['selection']['total'] for p in series['points'] if p['selection']['completeSelection']]
                self.assertEqual(series['sampledPeak'],max(values));self.assertEqual(presented['sampledPeak'],series['sampledPeak'])
                self.assertTrue(any(p['x']==series['currentX'] for p in series['points']))
            self.assertEqual(view['sampledPeak'],max(s['sampledPeak'] for s in native['series']))
            with tempfile.TemporaryDirectory() as directory:
                p=Path(directory)
                for key,value in view['request'].items():(p/(key+'.json')).write_text(json.dumps(value),encoding='utf-8')
                subprocess.run([str(b.root/'.tools/dotnet/dotnet.exe'),str(b.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),'sde-output-curves','--data',str(b.root/b.baseline['dataDirectory']),'--fit',str(p/'fit.json'),'--query',str(p/'query.json'),'--out',str(p/'result.json')],check=True,capture_output=True)
                self.assertEqual(json.loads((p/'result.json').read_text(encoding='utf-8-sig')),native)
    def test_missing_size_and_partial_selection_preserve_diagnostics(self):
        fit=self.fit();fit['slots'][0].update(item=10629,ammo=266)
        view=build_curves(analyze(fit));self.assertEqual(view['status'],'unavailable')
        self.assertEqual(view['native']['reason'],'WEAPON_SIZE_REFERENCE_UNAVAILABLE')
        self.assertIsNone(view['native']['sampledPeak'])
        request=view['request'];request['query']['selection']['contributionIds'].append('missing')
        result=bridge().call('fit_output_curves',request)['result']
        self.assertEqual(result['reason'],'INCOMPLETE_OUTPUT_SELECTION');self.assertTrue(result['baseline']['exclusions']);self.assertFalse(result['series'])

if __name__=='__main__':unittest.main()
