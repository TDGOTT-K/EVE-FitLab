import copy,json,subprocess,tempfile,unittest,uuid
from contextlib import ExitStack
from pathlib import Path
from unittest.mock import patch
import server
from fit_package import export_package,import_package,digest
from nengine_bridge import NEngineBridge
from nengine_persistence import save_to_library

class FitPackage(unittest.TestCase):
 def setUp(self):
  self.tmp=tempfile.TemporaryDirectory();self.root=Path(self.tmp.name)
  self.client=NEngineBridge(state=self.root/'native');self.patches=ExitStack()
  for module in ('nengine_adapter','nengine_capacitor','nengine_valuation','fit_package'):
   self.patches.enter_context(patch(module+'.bridge',return_value=self.client))
  self.library={'fits':[],'characters':[]}
  self.patches.enter_context(patch('server.read_library',side_effect=lambda:self.library))
  self.skills=next(c for c in server.characters() if c['id']=='all5')['skills']
  self.market={'source':{'provider':'fixture','url':'https://example.test/prices','currency':'ISK','priceKind':'average_price','fetchedAt':'2026-09-18T00:00:00Z','providerDate':None,'expiresAt':None,'contentSha256':'a'*64,'cacheState':'fresh','reason':None},'prices':{'587':100,'2881':5,'185':.1}}
 def tearDown(self):self.patches.close();self.client.close();self.tmp.cleanup()
 def fit(self):
  return {'id':str(uuid.uuid4()),'name':'Package fixture','notes':'Full notes','tags':['test'],'shipId':587,'skills':self.skills,
   'slots':[{'key':'high-0','kind':'high','item':2881,'ammo':185,'loadedCharges':10,'state':'Active'}],
   'cargo':[{'id':'ammo-stack','item':185,'quantity':20}],'drones':[],'capacitorHorizon':60}
 def export(self,fit):return export_package(fit,self.library['fits'],server.analyze,server.validate_fit,self.market)
 def restore(self,document):return import_package(document,server.analyze,server.validate_fit)
 def test_identity_context_prices_and_saved_cli_roundtrip(self):
  f=self.fit();target={**self.fit(),'id':'target','name':'Target','slots':[],'revision':2}
  native=json.loads((self.client.root/'examples/capacitor-scenarios/fitted-range-battery-query.json').read_text())['external'][0]['sourceFit']
  hostile={'id':'hostile','name':'Hostile','shipId':native['shipTypeId'],'skills':[{'skillTypeId':int(k),'level':v} for k,v in native['skills'].items()],
   'slots':[{'key':'high-'+str(i),'kind':'high','item':m['typeId'],'state':'Active'} for i,m in enumerate(native['items'])]}
  scenario={'targetFitId':'target','targetLayer':'armor','distance':1000,'speed':100,'angular':.01,'hostileFitId':'hostile','hostileDistance':1000}
  f.update(scenario=scenario,scenarios=[{'id':'s1','name':'Target','value':scenario}],activeScenarioId='s1',attackMode='edps')
  self.library['fits']=[target,hostile]
  original=server.analyze(f);self.assertEqual(original['capacitorScenario']['state'],'available')
  doc=self.export(f);self.assertNotIn('id',doc['fit']);self.assertEqual(doc['fit']['nativeFitId'],f['id'])
  self.library['fits']=[] # Source library is no longer available.
  wire=subprocess.run(['node','-e',"let s='';process.stdin.on('data',x=>s+=x);process.stdin.on('end',()=>process.stdout.write(JSON.stringify(JSON.parse(s))))"],input=json.dumps(doc),capture_output=True,text=True,check=True)
  restored=self.restore(json.loads(wire.stdout));report=restored['report']
  broken=copy.deepcopy(doc);del broken['fit']['scenarioSnapshots'][0]['sourceBinding']
  broken['contentSha256']=digest({k:v for k,v in broken.items() if k!='contentSha256'})
  with self.assertRaisesRegex(ValueError,'情景快照来源'):self.restore(broken)
  self.assertEqual(report['native'],original['native'])
  self.assertEqual(report['capacitorScenario']['result'],original['capacitorScenario']['result'])
  self.assertEqual(restored['fit']['notes'],f['notes'])
  self.assertEqual(restored['fit']['valuationSnapshot'],doc['replay']['valuation']['request']['snapshot'])
  writes=[];saved=save_to_library({**restored['fit'],'_saveRequestId':'package-save'},self.library,writes.append,'time',self.client)
  self.assertNotEqual(saved['id'],f['id']);self.assertEqual(saved['nativeSession']['fitHash'],original['native']['fitHash'])
  self.assertEqual(server.analyze(saved)['native'],original['native'])
  document=self.client.call('fit_export',{'sessionId':saved['nativeSession']['id'],'snapshot':'saved'})['result']
  # Public export materializes schema defaults; compare its full native analysis.
  exported_analysis=self.client.call('fit_analyze',{'fit':document['fit'],'context':original['native']['metricContext']})['result']
  self.assertEqual(exported_analysis,original['native'])
  for key,value in doc['replay']['analysis']['request'].items():(self.root/(key+'.json')).write_text(json.dumps(value),encoding='utf8')
  result=subprocess.run([str(self.client.root/'.tools/dotnet/dotnet.exe'),str(self.client.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),'sde-fit','--data',str(self.client.root/self.client.baseline['dataDirectory']),'--fit',str(self.root/'fit.json'),'--metrics',str(self.root/'context.json')],capture_output=True,check=True,text=True,encoding='utf8')
  self.assertEqual(json.loads(result.stdout),original['native'])
 def test_tamper_different_source_and_replay_mismatch_rejected(self):
  doc=self.export(self.fit());changed=copy.deepcopy(doc);changed['fit']['slots'][0]['loadedCharges']=0
  with self.assertRaisesRegex(ValueError,'校验失败'):self.restore(changed)
  changed['contentSha256']=digest({k:v for k,v in changed.items() if k!='contentSha256'})
  with self.assertRaisesRegex(ValueError,'重放结果'):self.restore(changed)
  changed=copy.deepcopy(doc);changed['sourceBinding']['revision']=0
  changed['contentSha256']=digest({k:v for k,v in changed.items() if k!='contentSha256'})
  with self.assertRaisesRegex(ValueError,'未自动重算'):self.restore(changed)
  self.assertEqual(self.library['fits'],[])
 def test_unknown_inventory_and_diagnostic_draft_remain_unknown(self):
  f=self.fit();del f['cargo'];del f['slots'][0]['loadedCharges'];f['slots'][0]['key']='high-7'
  doc=self.export(f);restored=self.restore(doc)
  self.assertNotIn('cargo',restored['fit']);self.assertNotIn('loadedCharges',restored['fit']['slots'][0])
  self.assertFalse(restored['report']['isValid']);self.assertEqual(restored['report']['native']['fitHash'],doc['replay']['analysis']['fitHash'])
 def test_crystals_plans_modes_and_fighters_roundtrip(self):
  crystal=self.fit();crystal['slots']=[{'key':'high-0','kind':'high','item':455,'ammo':247,'state':'Active'}]
  crystal['crystals']=[{'id':'crystal-stable','typeId':247,'damage':0,'moduleId':'high-0'}]
  crystal['loadoutPlan']={'id':'local-plan','name':'Snapshot','implants':[{'typeId':2082,'slot':6}],
   'boosters':[{'typeId':9950,'slot':1,'enabledSideEffects':[2737]}],'pilot':{'name':'Pilot','skills':self.skills}}
  mode={**self.fit(),'shipId':34317,'slots':[],'tacticalModeTypeId':34319}
  carrier={**self.fit(),'shipId':23913,'slots':[],'fighterLoadout':{'tubes':[{'id':'squad-stable','typeId':23055,'quantity':6,'active':True}],'reserve':[]}}
  for f in (crystal,mode,carrier):
   with self.subTest(ship=f['shipId']):
    original=server.analyze(f);doc=self.export(f);restored=self.restore(doc)
    self.assertEqual(restored['report']['native'],original['native'])
    self.assertEqual(restored['report']['capacitorScenario']['state'],original['capacitorScenario']['state'])
  self.assertNotIn('id',self.export(crystal)['fit']['loadoutPlan'])

if __name__=='__main__':unittest.main()
