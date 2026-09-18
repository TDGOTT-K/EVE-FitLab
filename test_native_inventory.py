import json,subprocess,tempfile,unittest
from pathlib import Path
from nengine_adapter import analyze,native_fit,bridge


class Inventory(unittest.TestCase):
    @classmethod
    def tearDownClass(cls):bridge().close()
    def fit(self):return {'name':'Inventory UI','shipId':587,'skills':[],
        'slots':[{'key':'high-0','kind':'high','item':2881,'ammo':185,'state':'Active'}]}

    def test_undeclared_empty_and_known_magazine(self):
        fit=self.fit();self.assertNotIn('inventory',native_fit(fit,3503375))
        fit['cargo']=[]
        unknown=analyze(fit)
        self.assertEqual(unknown['native']['inventory']['magazines'],[])
        fit['slots'][0]['loadedCharges']=0
        empty=analyze(fit)
        self.assertEqual(empty['native']['inventory']['magazines'][0]['loaded'],0)
        fit['slots'][0]['loadedCharges']=5;fit['cargo']=[{'item':185,'quantity':100}]
        result=analyze(fit);inventory=result['native']['inventory']
        self.assertEqual(inventory['magazines'][0]['capacity'],160)
        self.assertAlmostEqual(float(inventory['usedCubicMeters']),.25)
        self.assertEqual(result['native']['outputContributions']['items'][0]['constraints']['loadedCharges']['value'],5)
        b=bridge()
        with tempfile.TemporaryDirectory() as directory:
            p=Path(directory);(p/'fit.json').write_text(json.dumps(result['nativeFit']),encoding='utf-8')
            subprocess.run([str(b.root/'.tools/dotnet/dotnet.exe'),str(b.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),
                'sde-fit','--data',str(b.root/b.baseline['dataDirectory']),'--fit',str(p/'fit.json'),'--out',str(p/'result.json')],check=True,capture_output=True)
            cli=json.loads((p/'result.json').read_text(encoding='utf-8-sig'))
            self.assertEqual(cli['inventory'],inventory)

    def test_limits_and_unsupported_plain_crystal(self):
        fit=self.fit();fit['cargo']=[{'item':185,'quantity':100000}];fit['slots'][0]['loadedCharges']=161
        result=analyze(fit)
        self.assertIn('FIT_INVENTORY_CARGO_OVERFLOW',[e['code'] for e in result['issues']])
        self.assertIn('FIT_INVENTORY_MAGAZINE_OVERFLOW',[e['code'] for e in result['issues']])
        self.assertFalse(result['isValid'])
        self.assertEqual(result['native']['inventory']['magazines'][0]['loaded'],161)
        fit['cargo']=[{'item':262,'quantity':1}]
        with self.assertRaises(ValueError):analyze(fit)


if __name__=='__main__':unittest.main()
