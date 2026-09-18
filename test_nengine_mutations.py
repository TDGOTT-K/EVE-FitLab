"""Real pinned engine generation, persistence and fitted-value regression checks."""
import copy,json,subprocess,tempfile
from pathlib import Path
import unittest
from abyssal_instances import save_instance
from nengine_adapter import analyze, bridge
from nengine_mutations import generate, options,verify_receipt,review_receipt
from server import TYPES


class Mutations(unittest.TestCase):
    @classmethod
    def tearDownClass(cls): bridge().close()

    def test_generation_save_and_fitting(self):
        args = dict(baseTypeId=526, mutaplasmidTypeId=47699, seed='fitlab-abyssal-regression')
        a = generate(args, TYPES, roll=True)['data']
        self.assertEqual(a, generate(args, TYPES, roll=True)['data'])
        b = generate(dict(args, seed='fitlab-abyssal-other'), TYPES, roll=True)['data']
        self.assertNotEqual(a['mutation']['attributes'], b['mutation']['attributes'])
        for attr in a['rule']['attributes']:
            value = a['mutation']['attributes'][str(attr['attributeId'])]
            self.assertLessEqual(attr['minimumValue'], value)
            self.assertLessEqual(value, attr['maximumValue'])
        self.assertLess(a['mutation']['attributes']['20'], 0)
        library = {}
        body = dict(baseTypeId=526, name='Native', generationReceipt=a)
        record = save_instance(body, library, TYPES, 'now')
        self.assertEqual(record['status'], 'generated')
        renamed = save_instance(dict(record, name='Renamed'), library, TYPES, 'later')
        self.assertEqual(renamed['mutation'], a['mutation'])
        self.assertEqual(renamed['revision'], 2)
        with self.assertRaises(ValueError): save_instance(record, library, TYPES, 'stale')
        forged = copy.deepcopy(body)
        forged['generationReceipt']['mutation']['attributes']['50'] = 0
        before = copy.deepcopy(library)
        with self.assertRaises(ValueError): save_instance(forged, library, TYPES, 'forged')
        self.assertEqual(library, before)
        fit = dict(name='Mutation test', shipId=587, skills=[], slots=[dict(
            key='mid-0', kind='mid', item=record['resultTypeId'], state='Active', mutation=record['mutation'])])
        result = analyze(fit)
        self.assertTrue(result['native']['staticCoverageComplete'])
        cpu = next(r['used'] for r in result['native']['resources'] if r['id'] == 'cpu')
        self.assertAlmostEqual(cpu, record['mutation']['attributes']['50'])
        self.assertEqual(result['nativeFit']['items'][0]['mutation'], record['mutation'])

    def test_comparison_protocol_and_historical_receipt(self):
        args=dict(baseTypeId=526,mutaplasmidTypeId=47699,seed='public-comparison')
        current=generate(args,TYPES,True)['data'];counts=dict(improved=0,degraded=0,unchanged=0,unknown=0)
        for row in current['rolls']:
            comparison=row['comparison'];delta=row['value']-row['range']['baseValue']
            self.assertEqual(comparison['difference'],delta)
            if row['range']['baseValue']:self.assertEqual(comparison['relativeChange'],delta/abs(row['range']['baseValue']))
            counts[comparison['changeState']]+=1
        self.assertEqual({k:current['comparisonSummary'][k] for k in counts},counts)
        b=bridge()
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'roll.json'
            subprocess.run([str(b.root/'.tools/dotnet/dotnet.exe'),str(b.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),
                'sde-mutation-roll','--data',str(b.root/b.baseline['dataDirectory']),'--base','526','--mutaplasmid','47699','--seed',args['seed'],'--out',str(path)],check=True,capture_output=True)
            self.assertEqual(json.loads(path.read_text(encoding='utf-8-sig')),current)
        historical=copy.deepcopy(current);historical.pop('comparisonSummary')
        for row in historical['rolls']:row.pop('comparison')
        original=copy.deepcopy(historical)
        self.assertEqual(verify_receipt(historical,526,TYPES),original)
        reviewed=review_receipt({'baseTypeId':526,'generationReceipt':historical},TYPES)
        self.assertEqual(reviewed['data'],current);self.assertEqual(historical,original)
        record=save_instance(dict(baseTypeId=526,name='Historical',generationReceipt=historical),{},TYPES,'now')
        self.assertEqual(record['generationReceipt'],original)
        forged=copy.deepcopy(current);forged['comparisonSummary']['improved']+=1
        with self.assertRaises(ValueError):verify_receipt(forged,526,TYPES)
        forged=copy.deepcopy(current);forged['rolls'][0]['comparison']['difference']+=1
        with self.assertRaises(ValueError):verify_receipt(forged,526,TYPES)

    def test_wrong_mapping_rejected(self):
        self.assertTrue(options(TYPES)['526'])
        with self.assertRaises(ValueError):
            generate(dict(baseTypeId=587, mutaplasmidTypeId=47699), TYPES, True)


if __name__ == '__main__': unittest.main()
