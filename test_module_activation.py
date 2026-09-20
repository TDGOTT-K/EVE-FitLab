import copy,json,subprocess,tempfile,unittest
from pathlib import Path
import server
from nengine_catalog import index_metadata,activation_capabilities,effective_module_state
from nengine_adapter import analyze,bridge

class ModuleActivation(unittest.TestCase):
 @classmethod
 def tearDownClass(cls):bridge().close()
 def fit(self,item=10190,state='Active'):
  return {'name':'Activation regression','shipId':593,'skills':[],'slots':[{'key':'high-0','kind':'high','item':3162,'ammo':222,'state':'Active'},{'key':'low-0','kind':'low','item':item,'state':state}]}
 def test_catalog_audit(self):
  data=index_metadata();repaired=[]
  for t in server.CATALOG:
   if t['kind'] not in ('high','mid','low','rig','subsystem'):continue
   refs=data['typeDogma'].get(t['id'],{}).get('dogmaEffects',[])
   defaults=[r for r in refs if r.get('isDefault')]
   expected=len(defaults)==1 and data['dogmaEffects'][defaults[0]['effectID']].get('effectCategoryID') in (1,2,3)
   self.assertEqual(t['canActivate'],expected,t['en'])
   old=any(data['dogmaEffects'].get(r['effectID'],{}).get('effectCategoryID') in (1,2,3) for r in refs)
   if old and not expected:repaired.append(t['id'])
  self.assertEqual(len(repaired),971)
  for ident in [10190,519,520,377,380,393,394,1195,1185,1236,1244,1242,1246,1248,1254,1256]:self.assertIn(ident,repaired)
  self.assertTrue(activation_capabilities(438)['canActivate']);self.assertTrue(activation_capabilities(438)['canOverload'])
 def test_explicit_and_default_state_boundaries(self):
  for state in ['Online','Offline',None]:
   f=self.fit(state=state);original=copy.deepcopy(f);fixed=server.validate_fit(f)
   expected='Offline' if state=='Offline' else 'Online'
   self.assertEqual(fixed['slots'][1]['state'],expected);self.assertEqual(f,original)
   self.assertEqual(effective_module_state(f['slots'][1]),expected)
  for state in ['Active','Online','Offline','Overload']:
   self.assertEqual(effective_module_state({'item':438,'state':state}),state)
  # Explicit invalid activation must be rejected, never silently downgraded.
  self.assertEqual(effective_module_state(self.fit()['slots'][1]), 'Active')
  with self.assertRaises(ValueError):server.validate_fit(self.fit(state='Active'))
  with self.assertRaises(ValueError):server.validate_fit(self.fit(state='Overload'))
  with self.assertRaises(ValueError):server.validate_fit(self.fit(state='invalid'))
 def test_passive_bonus_and_public_parity(self):
  online=analyze(self.fit(state='Online'));offline=analyze(self.fit(state='Offline'))
  self.assertFalse(online['nativeFit']['items'][1]['active'])
  self.assertTrue(online['nativeFit']['items'][1]['online'])
  self.assertEqual(online['snapshot']['modules'][1]['state'],'Online')
  self.assertGreater(online['outputSelection']['total'],offline['outputSelection']['total'])
  b=bridge();args={'fit':online['nativeFit'],'context':{'output':online['outputContext']}}
  native=b.call('fit_analyze',args)['result']
  self.assertEqual(native,online['native'])
  with tempfile.TemporaryDirectory() as directory:
   p=Path(directory)
   for k,v in args.items():(p/(k+'.json')).write_text(json.dumps(v),encoding='utf-8')
   subprocess.run([str(b.root/'.tools/dotnet/dotnet.exe'),str(b.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),'sde-fit','--data',str(b.root/b.baseline['dataDirectory']),'--fit',str(p/'fit.json'),'--metrics',str(p/'context.json'),'--out',str(p/'result.json')],check=True,capture_output=True)
   self.assertEqual(json.loads((p/'result.json').read_text(encoding='utf-8-sig')),native)
  print('Magnetic stabilizer DPS:',offline['outputSelection']['total'],'->',online['outputSelection']['total'])
if __name__=='__main__':unittest.main()


