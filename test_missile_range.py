import json,math,subprocess,tempfile,unittest
from pathlib import Path
from nengine_adapter import analyze,bridge
class MissileRange(unittest.TestCase):
 def test_curve_range_and_cli_parity(self):
  c=bridge()
  try:
   f={'shipId':621,'slots':[{'key':'high-0','kind':'high','item':501,'ammo':209,'state':'Active'}]}
   a=analyze(f);edge=a['native']['weapons']['high-0']['missileFlight']['nominalPathMeters']
   q={'selection':a['outputContext']['selection'],'target':{'id':'range-check','distanceMeters':edge+1,'signatureMeters':1000,'speedMetersPerSecond':0,'angularRadiansPerSecond':0},'intervals':32}
   result=c.call('fit_output_curves',{'fit':a['nativeFit'],'query':q})['result']
   points=next(s['points'] for s in result['series'] if s['axis']=='distance')
   self.assertEqual(result['current']['total'],0)
   self.assertGreater(next(p['selection']['total'] for p in points if p['x']==edge),0)
   self.assertEqual(next(p['selection']['total'] for p in points if p['x']==math.nextafter(edge,math.inf)),0)
   self.assertTrue(all(p['selection']['total']==0 for p in points if p['x']>edge))
   with tempfile.TemporaryDirectory() as tmp:
    d=Path(tmp);(d/'fit.json').write_text(json.dumps(a['nativeFit']),encoding='utf-8');(d/'query.json').write_text(json.dumps(q),encoding='utf-8')
    subprocess.run([str(c.root/'.tools/dotnet/dotnet.exe'),str(c.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),'sde-output-curves','--data',str(c.root/c.baseline['dataDirectory']),'--fit',str(d/'fit.json'),'--query',str(d/'query.json'),'--out',str(d/'out.json')],check=True,capture_output=True)
    self.assertEqual(json.loads((d/'out.json').read_text(encoding='utf-8-sig')),result)
  finally:c.close()
if __name__=='__main__':unittest.main()
