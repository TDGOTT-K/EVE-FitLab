import json,subprocess,tempfile,unittest
from pathlib import Path
from nengine_adapter import analyze,bridge
class GuidanceScripts(unittest.TestCase):
 def test_guidance_families_scripts_and_public_parity(self):
  try:
   for module in [35788,35790,94063]:
    def fit(script=None):
     return {'name':'Guidance regression','shipId':621,'skills':[],'cargo':[], 'slots':[{'key':'high-0','kind':'high','item':501,'ammo':209,'state':'Active'}, {'key':'mid-0','kind':'mid','item':module,'ammo':script,'state':'Active'}]}
    base=analyze(fit())['native']['weapons']['high-0'];precision=analyze(fit(35795));ranged=analyze(fit(35794));p=precision['native']['weapons']['high-0'];r=ranged['native']['weapons']['high-0']
    self.assertAlmostEqual(p['missileApplication']['explosionRadiusMeters'],140*(1-2*{35788:5.5,35790:8.25,94063:8.25}[module]/100))
    self.assertLess(p['missileApplication']['explosionRadiusMeters'],base['missileApplication']['explosionRadiusMeters'])
    self.assertGreater(r['missileFlight']['nominalPathMeters'],base['missileFlight']['nominalPathMeters'])
    self.assertNotIn('mid-0',ranged['native']['weapons']);self.assertEqual(ranged['native']['inventory']['magazines'],[])
   off=fit(35795);off['slots'][1]['state']='Online'
   self.assertEqual(analyze(off)['native']['weapons']['high-0']['missileApplication']['explosionRadiusMeters'],140)
   with self.assertRaises(Exception):analyze(fit(28999))
   c=bridge()
   with tempfile.TemporaryDirectory() as tmp:
    src=Path(tmp)/'fit.json';dst=Path(tmp)/'result.json';src.write_text(json.dumps(ranged['nativeFit']),encoding='utf-8')
    subprocess.run([str(c.root/'.tools/dotnet/dotnet.exe'),str(c.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),'sde-fit','--data',str(c.root/c.baseline['dataDirectory']),'--fit',str(src),'--out',str(dst)],check=True,capture_output=True)
    self.assertEqual(json.loads(dst.read_text(encoding='utf-8-sig')),c.call('fit_analyze',{'fit':ranged['nativeFit']})['result'])
  finally:bridge().close()
if __name__=='__main__':unittest.main()

