import json,subprocess,tempfile,unittest
from pathlib import Path
import server
from nengine_adapter import analyze,bridge
from nengine_capacitor import attach_capacitor

class Admission(unittest.TestCase):
    @classmethod
    def tearDownClass(cls):bridge().close()
    def fit(self,over=False):
        return {'name':'Admission QA','shipId':587,'skills':next(c for c in server.characters() if c['id']=='all5')['skills'],
            'slots':[{'key':'high-0','kind':'high','item':455 if over else 2881,'ammo':247 if over else 185,'state':'Active'}]}
    def test_resource_warning_blocks_admission_not_diagnostic_values(self):
        fit=self.fit(True);report=analyze(fit)
        self.assertEqual(report['native']['errors'],[])
        warning=next(w for w in report['native']['warnings'] if w['code']=='RESOURCE_EXCEEDED')
        self.assertFalse(report['isValid']);self.assertIn(warning,report['issues'])
        self.assertEqual(warning['details']['resourceId'],'powergrid')
        self.assertIsNotNone(report['outputSelection']['total'])
        attach_capacitor(fit,report,[])
        self.assertEqual(report['capacitorScenario']['state'],'unavailable')
        self.assertEqual(report['capacitorScenario']['diagnostic']['code'],'CAP_SCENARIO_FIT')
        b=bridge()
        with tempfile.TemporaryDirectory() as directory:
            p=Path(directory);(p/'fit.json').write_text(json.dumps(report['nativeFit']),encoding='utf-8')
            subprocess.run([str(b.root/'.tools/dotnet/dotnet.exe'),str(b.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),
                'sde-fit','--data',str(b.root/b.baseline['dataDirectory']),'--fit',str(p/'fit.json'),'--out',str(p/'result.json')],check=True,capture_output=True)
            cli=json.loads((p/'result.json').read_text(encoding='utf-8-sig'))
            self.assertEqual(cli['warnings'],report['native']['warnings'])
            self.assertEqual(cli['resources'],report['native']['resources'])
    def test_valid_fit_and_skill_errors(self):
        fit=self.fit();report=analyze(fit);self.assertTrue(report['isValid'],report['issues'])
        attach_capacitor(fit,report,[]);self.assertEqual(report['capacitorScenario']['state'],'available')
        fit['skills']=[];report=analyze(fit)
        self.assertFalse(report['isValid']);self.assertTrue(any(e['code']=='SKILL_REQUIRED' for e in report['issues']))

if __name__=='__main__':unittest.main()
