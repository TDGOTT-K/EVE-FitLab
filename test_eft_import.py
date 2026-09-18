import unittest
from eft_import import parse_eft
from nengine_adapter import analyze,bridge
from nengine_catalog import index_metadata

EXAMPLE='''[Rifter, EFT import test]
Gyrostabilizer II
[Empty Low slot]
Damage Control II

1MN Afterburner II /offline

200mm AutoCannon II, EMP S
[Empty High slot]

Hobgoblin II x5

EMP S x1000
'''
class EftImport(unittest.TestCase):
    @classmethod
    def tearDownClass(cls):bridge().close()
    def test_standard_offline_gaps_ammo_and_cargo(self):
        result=parse_eft(EXAMPLE);self.assertEqual(result['issues'],[])
        fit=result['fit'];self.assertEqual(fit['name'],'EFT import test')
        bykey={s['key']:s for s in fit['slots']}
        self.assertIsNone(bykey['low-1']['item']);self.assertEqual(bykey['mid-0']['state'],'Offline')
        self.assertEqual(bykey['low-0']['state'],'Online');self.assertEqual(bykey['high-0']['state'],'Active')
        self.assertEqual(bykey['high-0']['ammo'],fit['cargo'][0]['item']);self.assertNotIn('loadedCharges',bykey['high-0'])
        self.assertEqual(fit['drones'][0]['active'],0);self.assertEqual(fit['drones'][0]['quantity'],5)
        self.assertEqual(fit['cargo'][0]['quantity'],1000)
        native=analyze(fit);self.assertEqual(native['nativeFit']['items'][1]['id'],'low-2')
        self.assertFalse(next(i for i in native['nativeFit']['items'] if i['id']=='mid-0')['online'])
    def test_chinese_and_duplicate_modules(self):
        result=parse_eft('[裂谷级, 中文测试]\n磁性力场稳定器 II\n磁性力场稳定器 II')
        self.assertFalse(result['issues']);self.assertEqual([s['item'] for s in result['fit']['slots']],[10190,10190])
    def test_no_silent_partial_import(self):
        for bad in ['Made up item','EMP S x0','[Rifter, second]','Gyrostabilizer II [1]','Gyrostabilizer II, Rifter']:
            result=parse_eft(EXAMPLE+'\n'+bad);self.assertIsNone(result['fit']);self.assertTrue(result['issues'])
            self.assertEqual(result['issues'][-1]['text'],bad)
        with self.assertRaises(ValueError):parse_eft('x'*100001)
        self.assertTrue(parse_eft('<fittings/>')['issues'])
    def test_pilot_items_and_fighter_reserve(self):
        d=index_metadata();implant=d['types'][27147]['name']['en']
        result=parse_eft('[Nyx, Fighter fit]\n'+implant+'\nStandard Blue Pill Booster\nTemplar I x12')
        self.assertFalse(result['issues'],result['issues']);fit=result['fit']
        self.assertEqual(fit['loadoutPlan']['implants'][0]['typeId'],27147)
        self.assertEqual(fit['loadoutPlan']['boosters'][0]['enabledSideEffects'],[])
        self.assertEqual(sum(s['quantity'] for s in fit['fighterLoadout']['reserve']),12)
        self.assertTrue(all(not s['active'] for s in fit['fighterLoadout']['reserve']))
if __name__=='__main__':unittest.main()
