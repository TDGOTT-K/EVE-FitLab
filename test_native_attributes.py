import json,subprocess,tempfile,unittest
from pathlib import Path
from nengine_attributes import inspect_attributes
from nengine_adapter import bridge

class NativeAttributes(unittest.TestCase):
    @classmethod
    def tearDownClass(cls):bridge().close()
    def fit(self):
        native=json.loads((bridge().root/'examples/attribute-inspection/fit.json').read_text())
        return {'name':'Attribute QA','shipId':native['shipTypeId'],'skills':[{'skillTypeId':int(k),'level':v} for k,v in native['skills'].items()],
          'slots':[{'key':('low-' if i>1 else 'high-')+str(i-2 if i>1 else i),'kind':'low' if i>1 else 'high','item':m['typeId'],'ammo':m.get('chargeTypeId'),'state':'Online'} for i,m in enumerate(native['items'])]}
    def test_modified_trace_and_cli_parity(self):
        result=inspect_attributes(self.fit(),'module.high-0',[64,51,54,160]);inspection=result['inspection']
        trace=inspection['items'][0]['trace'];self.assertNotEqual(trace['value'],trace['baseValue']);self.assertTrue(trace['steps'])
        b=bridge()
        with tempfile.TemporaryDirectory() as directory:
            p=Path(directory)
            for key in ('fit','queries'):(p/(key+'.json')).write_text(json.dumps(result['requests'][0][key]),encoding='utf-8')
            subprocess.run([str(b.root/'.tools/dotnet/dotnet.exe'),str(b.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),'sde-fit-attributes','--data',str(b.root/b.baseline['dataDirectory']),'--fit',str(p/'fit.json'),'--queries',str(p/'queries.json'),'--out',str(p/'result.json')],check=True,capture_output=True)
            self.assertEqual(json.loads((p/'result.json').read_text(encoding='utf-8-sig')),inspection)
    def test_charge_identity_unknown_and_default(self):
        result=inspect_attributes(self.fit(),'charge.high-0',[114,118])['inspection']
        self.assertEqual(result['items'][0]['typeId'],178)
        self.assertTrue(all(r['state']=='available' for r in result['items']))
        unknown=inspect_attributes(self.fit(),'module.missing',[64])['inspection']['items'][0]
        self.assertEqual(unknown['state'],'unavailable');self.assertIsNone(unknown['trace']);self.assertTrue(unknown['reason'])
        default=inspect_attributes(self.fit(),'ship',[68])['inspection']['items'][0]
        self.assertEqual(default['state'],'requires_policy');self.assertIsNone(default['trace'])
    def test_batches_and_invalid_queries(self):
        result=inspect_attributes(self.fit(),'ship',list(range(1,258)))
        self.assertEqual(len(result['requests']),2);self.assertEqual(len(result['inspection']['items']),257)
        self.assertEqual([r['query']['attributeId'] for r in result['inspection']['items']],list(range(1,258)))
        for ids in ([],[1,1],[True]):
            with self.assertRaises(ValueError):inspect_attributes(self.fit(),'ship',ids)

if __name__=='__main__':unittest.main()
