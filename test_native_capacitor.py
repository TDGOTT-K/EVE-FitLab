import json,subprocess,tempfile,unittest
from pathlib import Path
from nengine_adapter import analyze,bridge
from nengine_capacitor import attach_capacitor


class CapacitorScenario(unittest.TestCase):
    @classmethod
    def tearDownClass(cls):bridge().close()
    def fixture(self,name):return json.loads((bridge().root/('examples/capacitor-scenarios/'+name)).read_text(encoding='utf-8'))
    def ui(self,f):
        return {'name':f.get('name','Capacitor fixture'),'shipId':f['shipTypeId'],
            'skills':[{'skillTypeId':int(k),'level':v} for k,v in f['skills'].items()],
            'slots':[{'key':'high-'+str(i),'kind':'high','item':m['typeId'],'ammo':m.get('chargeTypeId'),
                'state':'Active' if m.get('active',True) else 'Online'} for i,m in enumerate(f.get('items',[]))]}

    def test_bound_neut_and_cli_parity(self):
        source_native=self.fixture('fitted-range-battery-query.json')['external'][0]['sourceFit']
        source=self.ui(source_native);source.update(id='neut',name='毁电来源')
        fit=self.ui({**source_native,'items':[]});fit['scenario']={'hostileFitId':'neut','hostileDistance':1000};fit['capacitorHorizon']=60
        report=analyze(fit);attach_capacitor(fit,report,[source]);cap=report['capacitorScenario']
        self.assertEqual(cap['state'],'available',cap.get('reason'))
        self.assertEqual(cap['request']['query']['external'][0]['moduleId'],'high-0')
        self.assertNotIn('payload',cap['request']['query']['external'][0])
        self.assertLess(cap['result']['minimumAmountGj'],cap['result']['recharge']['capacity'])
        self.assertEqual(cap['sources'][0]['id'],'neut')
        b=bridge()
        with tempfile.TemporaryDirectory() as directory:
            p=Path(directory)
            for key in ('fit','query'):(p/(key+'.json')).write_text(json.dumps(cap['request'][key]),encoding='utf-8')
            subprocess.run([str(b.root/'.tools/dotnet/dotnet.exe'),str(b.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),
                'sde-capacitor-scenario','--data',str(b.root/b.baseline['dataDirectory']),'--fit',str(p/'fit.json'),
                '--query',str(p/'query.json'),'--out',str(p/'result.json')],check=True,capture_output=True)
            self.assertEqual(json.loads((p/'result.json').read_text(encoding='utf-8-sig')),cap['result'])

    def test_local_and_incoming_nos_use_declared_boundaries(self):
        native=self.fixture('local-nos-fit.json');source=self.ui(native);source.update(id='nos-source',name='吸电来源')
        target=self.ui({**native,'items':[]});target.update(id='target',name='吸电目标')
        local={**source,'capacitorHorizon':1,'scenario':{'targetFitId':'target','distance':1000,'targetCapacitorGj':200,'ownCapacitorFraction':.1}}
        incoming={**target,'capacitorHorizon':1,'scenario':{'hostileFitId':'nos-source','hostileDistance':1000,'hostileCapacitorGj':100}}
        for fit in (local,incoming):
            report=analyze(fit);attach_capacitor(fit,report,[source,target]);cap=report['capacitorScenario']
            self.assertEqual(cap['state'],'available',cap.get('reason'))
            self.assertFalse(cap['result']['average']['complete'])
            energy=next(e for e in cap['result']['events'] if e['energy'] is not None)
            self.assertGreater(energy['energy']['applied'],0)
            if fit is local:
                self.assertEqual(cap['request']['query']['nosTargets']['high-0']['amountGj'],200)
                self.assertEqual(energy['inputs']['targetAmountGj'],200)
                self.assertEqual(energy['inputs']['sourceFraction'],.1)
            else:
                self.assertEqual(energy['inputs']['sourceAmountGj'],100)
            b=bridge()
            with tempfile.TemporaryDirectory() as directory:
                p=Path(directory)
                for key in ('fit','query'):(p/(key+'.json')).write_text(json.dumps(cap['request'][key]),encoding='utf-8')
                subprocess.run([str(b.root/'.tools/dotnet/dotnet.exe'),str(b.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),
                    'sde-capacitor-scenario','--data',str(b.root/b.baseline['dataDirectory']),'--fit',str(p/'fit.json'),
                    '--query',str(p/'query.json'),'--out',str(p/'result.json')],check=True,capture_output=True)
                self.assertEqual(json.loads((p/'result.json').read_text(encoding='utf-8-sig')),cap['result'])
        for field,bad in [('targetCapacitorGj',None),('targetCapacitorGj',1e10),('ownCapacitorFraction',-1)]:
            fit={**local,'scenario':{**local['scenario'],field:bad}};report=analyze(fit);attach_capacitor(fit,report,[target])
            self.assertEqual(report['capacitorScenario']['state'],'unavailable')
            self.assertNotIn('result',report['capacitorScenario'])

    def test_nos_conditions_survive_preset_storage(self):
        from scenario_presets import validate_presets
        conditions={'ownCapacitorFraction':.1,'targetCapacitorGj':0,'hostileCapacitorGj':None}
        body={'activeScenarioId':'nos','scenarios':[{'id':'nos','name':'NOS','value':conditions}]}
        self.assertEqual(validate_presets(body)['scenario'],conditions)
        for value in [-1,True,float('nan')]:
            body['scenarios'][0]['value']={**conditions,'targetCapacitorGj':value}
            with self.assertRaises(ValueError):validate_presets(body)

    def test_missing_source_is_not_green_stability(self):
        native=self.fixture('sampled-fit.json');fit=self.ui({**native,'items':[]})
        fit['scenario']={'supportFitId':'missing'};report=analyze(fit);attach_capacitor(fit,report,[])
        self.assertEqual(report['capacitorScenario']['state'],'unavailable')
        self.assertNotIn('result',report['capacitorScenario'])
        self.assertIsNotNone(report['outputSelection'])

    def test_finite_supply_and_payment_failure_are_not_mean_stability(self):
        b=bridge()
        for name in ('shared-stock','mean-stable-periodic-failure'):
            result=b.call('capacitor_scenario',{'fit':self.fixture(name+'-fit.json'),'query':self.fixture(name+'-query.json')})['result']
            if name=='shared-stock':
                self.assertFalse(result['average']['complete'])
                self.assertIsNone(result['average']['stableFromFullInAverageModel'])
                self.assertTrue(result['average']['exclusions'])
                self.assertIn('sharedCargo',result)
            else:
                self.assertTrue(result['average']['stableFromFullInAverageModel'])
                self.assertIsNotNone(result['firstFailedPaymentSeconds'])


if __name__=='__main__':unittest.main()
