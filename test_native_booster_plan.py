import copy,json,subprocess,tempfile,unittest
from pathlib import Path
from nengine_adapter import bridge
from nengine_booster_plan import analyze_plan,roll_plan,verify_receipt
from loadout_plans import save_plan


class BoosterPlans(unittest.TestCase):
    @classmethod
    def tearDownClass(cls):bridge().close()

    def test_implant_only_attributes_cli_mcp(self):
        body={'implants':[{'typeId':27147,'slot':10}],'boosters':[],
              'pilot':{'name':'Implant QA','skills':[{'skillTypeId':3411,'level':5}]}}
        response=analyze_plan(body);analysis=response['analysis']
        self.assertTrue(analysis['projectionComplete'])
        self.assertTrue(any(k.startswith('implant.implants-10/') for k in analysis['attributes']))
        self.assertFalse(any(k.startswith('ship/') for k in analysis['attributes']))
        b=bridge()
        with tempfile.TemporaryDirectory() as directory:
            p=Path(directory);(p/'plan.json').write_text(json.dumps(response['nativePlan']),encoding='utf-8')
            subprocess.run([str(b.root/'.tools/dotnet/dotnet.exe'),str(b.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),
                'sde-booster-plan-summary','--data',str(b.root/b.baseline['dataDirectory']),'--plan',str(p/'plan.json'),'--out',str(p/'result.json')],check=True,capture_output=True)
            self.assertEqual(json.loads((p/'result.json').read_text(encoding='utf-8-sig')),{'analysis':analysis,**response['summary']})

    def plan(self):return {'name':'test','implants':[],'boosters':[{'slot':1,'typeId':9950,'enabledSideEffects':[]}], 'seed':123}

    def test_native_roll_replay_storage_and_cli(self):
        plan=self.plan();result=roll_plan(plan)
        self.assertEqual(result,roll_plan(plan))
        self.assertEqual(result,verify_receipt(result['receipt']))
        self.assertFalse(result['analysis']['pilotPrerequisitesSatisfied'])
        self.assertTrue(result['analysis']['projectionComplete'])
        self.assertNotIn('shipTypeId',result['receipt']['plan'])
        body={**plan,'rollReceipt':result['receipt'],'pilot':{'name':'无技能','skills':[]}}
        lib={};saved=save_plan(body,lib,'now')
        self.assertEqual(saved['rollReceipt'],result['receipt'])
        forged=copy.deepcopy(result['receipt']);forged['doses'][0]['rolls'][0]['applied']=True
        with self.assertRaises(ValueError):verify_receipt(forged)
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory);(path/'plan.json').write_text(json.dumps(result['receipt']['plan']),encoding='utf-8')
            b=bridge();subprocess.run([str(b.root/'.tools/dotnet/dotnet.exe'),str(b.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),
                'sde-booster-plan-roll','--data',str(b.root/b.baseline['dataDirectory']),'--plan',str(path/'plan.json'),
                '--seed','123','--out',str(path/'out.json')],check=True,capture_output=True)
            self.assertEqual(json.loads((path/'out.json').read_text(encoding='utf-8-sig')),result)

    def test_skills_and_manual_selection(self):
        plan=self.plan();base=analyze_plan(plan)['analysis']['boosters'][0]
        plan['pilot']={'name':'Skills','skills':[{'skillTypeId':3405,'level':5},{'skillTypeId':25530,'level':5}]}
        trained=analyze_plan(plan)['analysis']['boosters'][0]
        self.assertGreater(trained['durationSeconds'],base['durationSeconds'])
        self.assertLess(trained['sideEffects'][0]['probability'],base['sideEffects'][0]['probability'])
        plan['boosters'][0]['enabledSideEffects']=[2737]
        self.assertTrue(analyze_plan(plan)['analysis']['boosters'][0]['sideEffects'][0]['enabled'])
        self.assertEqual(roll_plan(plan)['receipt']['plan']['boosters'][0]['enabledSideEffects'],[])
        with self.assertRaises(ValueError):roll_plan(dict(plan,seed=2**64-1))


if __name__=='__main__':unittest.main()

