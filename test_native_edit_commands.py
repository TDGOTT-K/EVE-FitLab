import copy,json,unittest
from nengine_edit_commands import commands_between,prepare_ui_edit
from nengine_sessions import request
import test_native_sessions as sessions

class EditMapping(unittest.TestCase):
    setUp=sessions.NativeSessions.setUp
    tearDown=sessions.NativeSessions.tearDown
    call=sessions.NativeSessions.call
    cli=sessions.NativeSessions.cli

    def base(self,ship=587):return {'name':'Editing','shipId':ship,'slots':[],'skills':[]}
    def verify(self,before,after):
        prepared=request('prepare',{'before':before,'after':after},self.client)['result']
        self.call('create',sessionId='qa',fit=prepared['fit'],allowIncompleteDraft=True)
        preview=self.call('preview',sessionId='qa',revision=0,commands=prepared['commands'])
        direct=self.client.call('fit_analyze',{'fit':prepared['candidate']})['result']
        self.assertEqual(preview['analysis'],direct)
        self.assertEqual(preview['candidateHash'],direct['fitHash'])
        path=self.root/'commands.json';path.write_text(json.dumps(prepared['commands']),encoding='utf-8')
        self.assertEqual(preview,self.cli('eve-preview',revision=0,commands=path))
        applied=self.call('execute',sessionId='qa',revision=0,requestId='change',operation='apply',commands=prepared['commands'])
        self.assertEqual(applied['analysis'],direct)
        return prepared

    def test_pilot_passive_module_and_declared_ammo_are_one_edit(self):
        before=self.base();after=copy.deepcopy(before)
        after.update(name='Pilot and inventory',tags=['Test'],skills=[{'skillTypeId':3300,'level':5}],
          slots=[{'key':'high-0','kind':'high','item':2881,'ammo':185,'state':'Active','loadedCharges':0},
                 {'key':'low-0','kind':'low','item':519,'state':'Online'}],cargo=[{'item':185,'quantity':100}],
          loadoutPlan={'implants':[{'typeId':2082}],'boosters':[{'typeId':9950,'enabledSideEffects':[2737]}]})
        prepared=self.verify(before,after)
        self.assertEqual(next(c for c in prepared['commands'] if c['kind']=='setInventory')['inventory']['magazines'][0]['loaded'],0)

    def test_module_reordering_crystals_and_inventory_unknown(self):
        before=self.base();before['slots']=[{'key':'high-0','kind':'high','item':455,'ammo':247,'state':'Active'},
                                          {'key':'high-1','kind':'high','item':2881,'ammo':185,'state':'Active'}]
        before['crystals']=[{'id':'crystal-1','typeId':247,'damage':0.3,'moduleId':'high-0'}]
        before['cargo']=[];after=copy.deepcopy(before)
        after['slots'].reverse();del after['crystals'];del after['cargo']
        prepared=self.verify(before,after)
        self.assertEqual(prepared['commands'][-1],{'kind':'clearInventory'})

    def test_tactical_mode(self):
        before=self.base(34317);before['tacticalModeTypeId']=34319
        after={**before,'tacticalModeTypeId':34323};self.verify(before,after)

    def test_mutation_instance_survives_command_mapping(self):
        rolled=self.client.call('mutation_roll',{'baseTypeId':526,'mutaplasmidTypeId':47699,'seed':'edit-mapping'})['result']
        before=self.base();after=copy.deepcopy(before)
        after['slots']=[{'key':'mid-0','kind':'mid','item':rolled['rule']['resultTypeId'],'state':'Active','mutation':rolled['mutation']}]
        self.verify(before,after)

    def test_fighter_members_and_deployment(self):
        before=self.base(23913)
        before['fighterLoadout']={'tubes':[{'id':'alpha','typeId':23055,'quantity':6,'active':True}], 'reserve':[]}
        after=copy.deepcopy(before);after['fighterLoadout']={'tubes':[], 'reserve':[{'id':'alpha','typeId':23055,'quantity':5,'active':False}]}
        self.verify(before,after)

    def test_analysis_and_ui_state_are_not_fitting_edits(self):
        before=self.base();after={**before,'defenseMode':'targeted','damageProfile':[100,0,0,0],
            'outputMetric':'loadedCycleDps','capacitorHorizon':60,'scenario':{'distance':10000},'notes':'UI-only note'}
        original=copy.deepcopy(before);candidate=copy.deepcopy(after)
        self.assertEqual(prepare_ui_edit(before,after,self.client.baseline['buildNumber'])['commands'],[])
        self.assertEqual(before,original);self.assertEqual(after,candidate)

    def test_new_fields_identity_and_transaction_limits_fail_explicitly(self):
        f={'id':'a','buildNumber':3503375,'omittedSkills':'untrained','shipTypeId':587,'items':[]}
        for after in ({**f,'id':'b'},{**f,'newMechanism':True}):
            with self.assertRaises(ValueError):commands_between(f,after)
        rows=[{'id':str(i),'typeId':2881} for i in range(128)]
        with self.assertRaises(ValueError):commands_between({**f,'items':rows},{**f,'items':list(reversed(rows))})
        with self.assertRaises(ValueError):commands_between(f,{**f,'items':[rows[0],rows[0]]})

if __name__=='__main__':unittest.main()
