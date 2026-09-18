"""Real pinned engine generation, persistence and fitted-value regression checks."""
import copy
import unittest
from abyssal_instances import save_instance
from nengine_adapter import analyze, bridge
from nengine_mutations import generate, options
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

    def test_wrong_mapping_rejected(self):
        self.assertTrue(options(TYPES)['526'])
        with self.assertRaises(ValueError):
            generate(dict(baseTypeId=587, mutaplasmidTypeId=47699), TYPES, True)


if __name__ == '__main__': unittest.main()
