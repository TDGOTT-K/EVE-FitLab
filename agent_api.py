"""Task API shared by MCP and CLI. No game formulas or native storage-file access."""
import json
import hashlib
import time
from pathlib import Path

from agent_contract import OPS, validate
from agent_mcp import AgentMcp, encoded
from nengine_bridge import NEngineError


def step(route,action,**args):return {'tool':'fitlab_'+route+'_'+action,'arguments':args}


class AgentApi:
    def __init__(self,core):self.core=core
    def close(self):self.core.close()
    def unwrap(self,value):
        if isinstance(value,dict) and 'resultId' in value:
            return value['value'] if 'value' in value else self.core.load_result(value['resultId'])
        return value
    def legacy(self,name,args):return self.unwrap(self.core.call(name,args))
    def native(self,name,args):return self.core.native(name,args)
    def call(self,domain,action,args):
        try:
            if domain not in OPS or action not in OPS[domain]:raise ValueError('Unknown operation. Use help overview.')
            validate(args,OPS[domain][action][1])
            data,state,issues,next_actions=self.dispatch(domain,action,args)
            return {'apiVersion':'fitlab-agent-v6','ok':True,'state':state,'data':data,'issues':issues,'next':next_actions}
        except Exception as e:
            error=e.error if isinstance(e,NEngineError) else {'code':'INVALID_ARGUMENT','message':str(e)}
            next_actions=[step('help','operation',domain=domain,operation=action)] if domain in OPS and action in OPS[domain] else [step('help','overview')]
            if error.get('code')=='POLICY_CAPABILITY_MISMATCH':next_actions=[step('battle','policies',**({'draftId':args['draftId']} if 'draftId' in args else {}))]
            elif error.get('code') in ('JOB_STORAGE_QUOTA','STORAGE_LOW','JOB_STORAGE_JOB_LIMIT'):next_actions=[step('jobs','list')]
            elif error.get('code')=='EVE_MODULE_CATEGORY':
                error={**error,'hint':'install is for slot modules. Use setFighters/setDrones/setSubsystems/setImplants/setBoosters for other collections; inspect fitting roster first.'}
                next_actions=[step('fitting','roster',sessionId=args['sessionId'])] if args.get('sessionId') else next_actions
            elif error.get('code') in ('JOB_VERSION',):next_actions=[step('jobs','list')]
            elif error.get('code') in ('STALE_REVISION','OUTPUT_EXISTS') and args.get('sessionId'):next_actions=[step('fitting','read',sessionId=args['sessionId'])]
            elif error.get('code')=='CATALOG_ATTRIBUTE':next_actions=[step('catalog','attributes',text='')]
            return {'apiVersion':'fitlab-agent-v6','ok':False,'state':'error','data':None,'issues':[error],'next':next_actions}

    def identity(self,args):
        if ('typeId' in args)==('name' in args):raise ValueError('Supply exactly one of name or typeId.')
        if 'typeId' in args:return args['typeId'],None
        result=self.native('catalog_search',{'query':{'text':args['name'],'limit':100}})
        exact=[i for i in result['items'] if any(n.casefold()==args['name'].strip().casefold() for n in i['names'].values())]
        if len(exact)==1:return exact[0]['typeId'],None
        return None,{'query':args['name'],'total':result['total'],'candidates':[{'typeId':i['typeId'],'names':i['names']} for i in result['items'][:10]],
                     'reason':'No unique exact official name. Choose a returned typeId; nicknames are not silently guessed.'}

    def summary(self,value):
        result=dict(value)
        if 'summary' in result:
            result['summary']=self.unwrap(result['summary'])
            correction=result['summary'].get('suggestedCorrection')
            if correction:
                ids=correction['arguments']['instanceIds']
                result['summary']['suggestedCorrection']=step('fitting','edit',sessionId=result['sessionId'],revision=result['revision'],
                    requestId='disable-passive-'+str(result['revision']),changes=[{'kind':'setActive','instanceId':i,'active':False} for i in ids])
        return result

    def bound(self,value):
        if len(encoded(value))<=20000:return value
        identity=self.core.store(value)
        return {'detailResultId':identity,'keys':list(value) if isinstance(value,dict) else None,
                'next':step('result','read',resultId=identity,limit=20),'reason':'Full value retained; request a specific section.'}

    def dispatch(self,domain,action,a):
        if domain=='help':
            if action=='operation':
                description,schema=OPS[a['domain']][a['operation']]
                return {'description':description,'schema':schema},'ready',[],[]
            if action=='native':
                value=self.legacy('fitlab_tools',a)
                return value,'ready',[],[]
            return {'routes':{
                'ship bonuses':'catalog describe by exact official name; no fitting or battle required',
                'find/compare equipment':'catalog attributes/search/compare/charges/variants',
                'fit design':'fitting create -> preview/edit -> read; save explicitly',
                'weapon research':'catalog descriptions provide source material; fitting outputs/curves provides condition-bound calculations; battle is optional',
                'combat experiment':'battle run (or prepare/start) -> wait/report; jobs handles storage',
                'advanced supported mechanisms':'help native -> raw call; same engine authority'},
                'examples':[
                    step('catalog','describe',name='狞獾级'),
                    step('catalog','search',query={'groupId':40,'filters':[{'attribute':'cpu','max':100}],'sortBy':'power'}),
                    step('fitting','curves',sessionId='your-fit',target={'distanceMeters':10000,'signatureMeters':125,'speedMetersPerSecond':200,'angularRadiansPerSecond':0.02}),
                    step('jobs','list')],
                'meaning':{'base':'explicit SDE metadata','fitted':'skill/context-specific engine calculation','curve':'static chosen outputs versus explicit geometry; not actual combat or sustained DPS',
                           'battle':'one exact scenario/seed/policy, not a universal ship strength multiplier'},
                'readMore':'skills/fitlab-agent/SKILL.md; prompts/fitlab-agent-system.md'},'ready',[],[]
        if domain=='status':
            status=self.native('engine_status',{})
            native={'version':status['engineVersion'],'contractRevision':status['publicContract']['revision'],
                    'buildNumber':status['source']['source']['buildNumber'],'indexHash':status['source']['indexSha256'],
                    'fullFittingSupported':status['fullFittingSupported'],'fullCombatSupported':status['fullCombatSupported']}
            usage=self.native('battle_storage',{})
            return {'engine':native,'storage':usage,'capabilities':list(OPS),'interface':'v6 task API; engine is numerical authority'},'ready',self.storage_issues(usage),[step('help','overview')]
        if domain=='catalog':return self.catalog(action,a)
        if domain=='fitting':return self.fitting(action,a)
        if domain=='jobs':
            if action=='storage':
                usage=self.native('battle_storage',{})
                return usage,'ready',self.storage_issues(usage),[step('jobs','list')]
            if action=='list':
                result=self.native('battle_list',a);n=[]
                if result['nextAfterId']:n=[step('jobs','list',afterId=result['nextAfterId'],limit=a.get('limit',20))]
                return result,'ready',self.storage_issues(result['storage']),n
            result=self.native('battle_compact',{'jobId':a['jobId'],'expectedRevision':a['revision'],'dryRun':a.get('dryRun',True)})
            n=[step('jobs','compact',jobId=a['jobId'],revision=a['revision'],dryRun=False)] if result['dryRun'] and result['reclaimableBytes'] else []
            return result,'preview' if result['dryRun'] else 'ready',[],n
        if domain=='battle':return self.battle(action,a)
        if domain=='raw':return self.bound(self.native(a['name'],a['arguments'])),'ready',[],[]
        if domain=='result':
            result=self.core.read_result(a);n=[]
            if result.get('nextOffset') is not None:n=[step('result','read',**{**a,'offset':result['nextOffset']})]
            return result,'ready',[],n
        raise ValueError('Unknown domain')

    @staticmethod
    def storage_issues(usage):
        return [{'code':'STORAGE_LOW','remainingBytes':usage['availablePayloadBytes'],'message':'Low battle output capacity. List inactive jobs and preview compact before another run.'}] if usage['availablePayloadBytes']<max(16*1024*1024,usage['limits']['maximumBytes']*.05) else []

    def catalog(self,action,a):
        if action in ('search','attributes','resolve'):
            args={'names':a['names'],**({'categoryId':a['categoryId']} if 'categoryId' in a else {})} if action=='resolve' else {'query':a['query'] if action=='search' else {'attributeSearch':a['text'],**{k:a[k] for k in ('limit','cursor') if k in a}}}
            result=self.legacy('fitlab_search',args);n=[]
            def convert(call):
                if not call:return None
                if call['tool']=='fitlab_search':return step('catalog','search',**call['arguments'])
                return step('result','read',**call['arguments'])
            if isinstance(result,list):
                for row in result:
                    row['nextCall']=convert(row.get('nextCall'))
            else:
                result['nextCall']=convert(result.get('nextCall'));result['groupsNextCall']=convert(result.get('groupsNextCall'))
                if result['nextCall']:n.append(result['nextCall'])
            return self.bound(result),'ready',[],n
        if action=='compare':
            result=self.legacy('fitlab_item',{'typeIds':a['typeIds'],'names':a['attributes'],**({'locale':a['locale']} if 'locale' in a else {})})
            return self.bound(result),'ready',[],[]
        identity,ambiguous=self.identity(a)
        if ambiguous:return ambiguous,'needs_selection',[],[step('catalog','search',query={'text':a['name']})]
        if action=='variants':
            result=self.legacy('fitlab_search',{'query':{'familyOfTypeId':identity,**{k:a[k] for k in ('limit','cursor') if k in a}}})
            n=[step('catalog','search',**result['nextCall']['arguments'])] if result.get('nextCall') else []
            result.pop('nextCall',None)
            return self.bound(result),'ready',[],n
        result=self.legacy('fitlab_item',{'typeId':identity,**({'names':a['attributes']} if 'attributes' in a else {}),**({'locale':a['locale']} if 'locale' in a else {})})
        if action=='charges':
            config=result.get('moduleConfiguration')
            if not config:return {'typeId':identity,'reason':'No module charge metadata'},'unavailable',[],[]
            groups=config.get('declaredChargeGroups',[])
            return {'typeId':identity,'configuration':config},'ready',[],[step('catalog','search',query={'groupId':int(g['value'])}) for g in groups]
        # Official description and group context are often sufficient for a simple question.
        detail=self.native('catalog_item',{'typeId':identity})
        result['description']=detail['description'].get(a.get('locale','zh'),detail['description'].get('en'))
        result['groupNames']=detail['groupNames']
        result['capabilities']=detail.get('capabilities')
        result.pop('next',None)
        return self.bound(result),'ready',[],[]

    def fitting(self,action,a):
        if action=='roster':
            inspected=self.native('fit_inspect',{'sessionId':a['sessionId']});session=inspected['session'];analysis=inspected['analysis']
            return {'sessionId':a['sessionId'],'revision':session['revision'],'fitHash':analysis['fitHash'],
                'collections':{k:session['working'].get(k,[]) for k in ('fighters','drones','subsystems','implants','boosters')},
                'projections':{
                    **{k:analysis.get(k) for k in ('fighterBay','fighterPrimaryNominalDps','fighterPrimaryNominalDpsUnavailableReason','fighterReadoutIssues','droneBay')},
                    'fighters':{identity:{k:row.get(k) for k in ('instanceId','typeId','deployed','location','tubeIndex','squadronClass','maximumMembers','primaryNominalDps','cycleSeconds','optimalMeters','scope')} for identity,row in analysis.get('fighters',{}).items()},
                    'drones':{identity:{k:row.get(k) for k in ('instanceId','typeId','deployed','nominalDps','cycleSeconds','scope')} for identity,row in analysis.get('drones',{}).items()}},
                'detailResultId':self.core.store({'collections':{k:session['working'].get(k,[]) for k in ('fighters','drones','subsystems','implants','boosters')},'analysis':analysis}),
                'errors':analysis['errors'],'scope':'fitted primary output; not all fighter abilities or combat equivalence'},'needs_correction' if analysis['errors'] else 'ready',analysis['errors'],[]
        if action=='list':
            result=self.native('fit_library',{'query':a})
            n=[step('fitting','list',**{**a,'cursor':result['nextCursor']})] if result.get('nextCursor') else []
            return self.bound(result),'ready',[],n
        if action in ('create','read','save','edit'):
            args={**a,'action':action}
            if action=='edit':args['commands']=args.pop('changes')
            result=self.summary(self.legacy('fitlab_fit',args))
            summary=result.get('summary',{});issues=(summary.get('errors') or {}).get('first',[])
            return result,'needs_correction' if issues else 'ready',issues,[]
        if action=='attributes':
            args={**a,'action':'attributes','names':a['attributes']};args.pop('attributes')
            return self.legacy('fitlab_fit',args),'ready',[],[]
        if action=='preview':
            result=self.native('fit_preview',{'sessionId':a['sessionId'],'revision':a['revision'],'commands':a['changes']})
            return {'sessionId':a['sessionId'],'revision':result['baseRevision'],'candidateHash':result['candidateHash'],
                    'committable':result['committable'],'resourceDeltas':result['resourceDeltas'],'metricDeltas':result['metricDeltas'],
                    'errors':result['analysis']['errors'],'warnings':result['analysis']['warnings'],'detailResultId':self.core.store(result)},'preview',[],[]
        inspected=self.native('fit_inspect',{'sessionId':a['sessionId']});analysis=inspected['analysis'];fit=inspected['session']['working']
        output=analysis.get('outputContributions') or {};items=output.get('items',[])
        if action=='outputs':return self.bound({'sessionId':a['sessionId'],'revision':inspected['session']['revision'],'fitHash':analysis['fitHash'],'outputs':[{k:i.get(k) for k in ('id','kind','source','status','reason','fitAdmitted','metrics','application','assumptions')} for i in items],'scope':output.get('scope'),'detailResultId':self.core.store(output)}),'ready',[],[]
        ids=a.get('contributionIds',[i['id'] for i in items if i['kind']=='ship_weapon' and i['source'].get('online')])
        query={'selection':{'metric':a.get('metric','appliedCycleDps'),'contributionIds':ids},'intervals':a.get('intervals',16)}
        if 'target' in a:query['target']={'id':'research-target',**a['target']}
        full=self.native('fit_output_curves',{'fit':fit,'query':query})
        result={k:full.get(k) for k in ('source','fitHash','query','state','reason','ideal','referenceTarget','scope','peakConvention','errors','warnings')}
        result['series']=[{'axis':s['axis'],'unit':s['unit'],'minimum':s['minimum'],'maximum':s['maximum'],
            'points':[{'x':p['x'],'value':(p.get('selection') or {}).get('total'),'complete':(p.get('selection') or {}).get('completeSelection',False)} for p in s['points']]} for s in full['series']]
        result['selectionPolicy']='explicit_contribution_ids' if 'contributionIds' in a else 'online_ship_weapons_potential_output_not_activation_or_sustained'
        result['excludedContributionIds']=[i['id'] for i in items if i['id'] not in ids]
        result['detailResultId']=self.core.store(full)
        return result,full['state'],[],[]

    def binding_path(self,request_hash):
        if len(request_hash)!=64 or any(c not in '0123456789abcdef' for c in request_hash):raise ValueError('Invalid request hash')
        root=self.core.results.parent/'agent-experiments';root.mkdir(exist_ok=True)
        return root/(request_hash+'.json')

    def battle(self,action,a):
        if action=='policies':
            from agent_battle import preset_support
            presets=[{'id':'stationary-weapons-v1','supports':['direct','missile'],'behavior':'Locks first enemy, fires conventional weapons. No movement or self modules.'},
                     {'id':'stationary-weapons-defense-v1','supports':['direct','missile','native conventional hardeners'],'behavior':'Activates all admitted conventional hardeners on self and fires weapons. Paid cycles/capacitor enforced by engine; no propulsion/repair/EWAR/drones.'}]
            if 'draftId' in a:
                draft=self.core.load_result(a['draftId'])
                if draft.get('kind')!='fitlab-battle-draft-v1':raise ValueError('Expected prepared draftId')
                for p in presets:p['blockers']=preset_support(draft,p['id']);p['compatible']=not p['blockers']
            return {'presets':presets,'custom':'Explicit supported JavaScript policies remain available; no automatic refitting to make a preset pass.'},'ready',[],[]
        if action=='prepare':
            result=self.legacy('fitlab_battle',{'action':action,**a});result.pop('scriptTemplate',None);result.pop('next',None)
            return result,'prepared',[],[]
        if action in ('run','start'):
            usage=self.native('battle_storage',{})
            # Do not do expensive assembly when even a small new checkpoint is unlikely to fit.
            # This is a storage admission threshold, not an estimated simulation size.
            existing=None
            try:existing=self.native('battle_manifest',{'jobId':a['jobId']})
            except NEngineError as e:
                if e.error.get('code')!='JOB_NOT_FOUND':raise
            if not existing and usage['availablePayloadBytes']<16*1024*1024:
                raise NEngineError({'error':{'code':'STORAGE_LOW','message':'Less than 16 MiB of battle payload capacity remains. Preview compact on inactive jobs first.'}})
            if action=='run':
                prepared=self.legacy('fitlab_battle',{'action':'prepare',**{k:a[k] for k in ('id','seed','seconds','ships')}});draft_id=prepared['draftId']
            else:draft_id=a['draftId']
            policy={k:a[k] for k in ('policies','policyPreset') if k in a}
            if not policy:policy={'policyPreset':'stationary-weapons-v1'}
            accepted=self.legacy('fitlab_battle',{'action':'start','draftId':draft_id,'jobId':a['jobId'],**policy})
            draft=self.core.load_result(draft_id)
            binding={'draftId':draft_id,'requestHash':accepted['job']['requestHash'],'ships':draft['snapshots'],
                     'conditions':draft['assembly']['request'],'source':draft['assembly']['source'],'policy':policy}
            # The native receipt pins the exact request; this file is facade-owned metadata only.
            path=self.binding_path(binding['requestHash'])
            import tempfile
            with tempfile.NamedTemporaryFile(dir=path.parent,suffix='.tmp',delete=False) as stream:
                stream.write(encoded({'binding':binding,'sha256':hashlib.sha256(encoded(binding).encode()).hexdigest()}).encode());tmp=Path(stream.name)
            try:tmp.replace(path)
            finally:tmp.unlink(missing_ok=True)
            result,state,issues,n=self.wait(a['jobId'],a.get('waitSeconds',10))
            result['replayed']=existing is not None;result['draftId']=draft_id
            return result,state,issues,n
        if action=='wait':return self.wait(a['jobId'],a.get('waitSeconds',30))
        if action=='report':return self.report(a['jobId'],a.get('expectedDraftId'))
        if action=='events':
            result=self.legacy('fitlab_battle',{'action':'events',**a})
            n=[step('battle','events',**{**a,'offset':result['nextOffset']})] if result.get('hasMore') else []
            return self.bound(result),'ready' if result.get('ready',True) else 'pending',[],n
        if action=='resume':
            result=self.native('battle_resume',{'jobId':a['jobId'],'expectedRevision':a['revision']})
            return result,'pending',[],[step('battle','wait',jobId=a['jobId'],waitSeconds=30)]
        result=self.native('battle_cancel',{'jobId':a['jobId']})
        return result,'pending' if result['status'] in ('queued','running') else result['status'],[],[step('battle','wait',jobId=a['jobId'],waitSeconds=30)]

    def wait(self,job_id,seconds):
        deadline=time.monotonic()+seconds
        while True:
            state=self.native('battle_status',{'jobId':job_id})
            if state['status'] not in ('queued','running'):return self.report(job_id)
            if time.monotonic()>=deadline:
                return {'job':state},'pending',[],[step('battle','wait',jobId=job_id,waitSeconds=30)]
            time.sleep(min(.5,max(0,deadline-time.monotonic())))

    def report(self,job_id,expected=None):
        manifest=self.native('battle_manifest',{'jobId':job_id});state=manifest['job']
        path=self.binding_path(state['requestHash']);binding=None
        if path.exists():
            stored=json.loads(path.read_text(encoding='utf-8'));binding=stored.get('binding')
            if not isinstance(binding,dict) or hashlib.sha256(encoded(binding).encode()).hexdigest()!=stored.get('sha256') or binding.get('requestHash')!=state['requestHash']:
                raise NEngineError({'error':{'code':'EXPERIMENT_BINDING_INVALID','message':'Experiment binding integrity failed; do not infer fitting correspondence.'}})
        if expected is not None and (binding is None or binding['draftId']!=expected):raise NEngineError({'error':{'code':'EXPERIMENT_MISMATCH','message':'This job is not bound to the expected draft. Do not substitute another experiment.'}})
        provenance={'state':'bound' if binding else 'historical_unbound','requestHash':state['requestHash'],'scenarioHash':manifest['scenarioHash'],
                    'conditions':manifest,'fittingSnapshots':binding['ships'] if binding else None,'draftId':binding['draftId'] if binding else None}
        if binding:
            provenance['source']=binding['source']
            provenance['policy']=binding['policy'].get('policyPreset','custom; see policiesHash in conditions')
            provenance['declaredConditions']={**binding['conditions'],'participants':[{k:v for k,v in p.items() if k!='fit'} for p in binding['conditions']['participants']]}
        issues=[] if binding else [{'code':'FITTING_PROVENANCE_UNAVAILABLE','message':'Historical run: initial geometry is verified, fitting/session correspondence is unavailable. Do not pair with newly calculated fitting stats.'}]
        if state['status']=='failed':
            return {'job':state,'provenance':provenance},'failed',issues+[{'code':state['errorCode'],'message':'Infrastructure/execution failure; no completed combat conclusion.'}],[step('jobs','list')]
        if state['status'] in ('queued','running'):
            return {'job':state,'provenance':provenance},'pending',issues,[step('battle','wait',jobId=job_id,waitSeconds=30)]
        result=self.legacy('fitlab_battle',{'action':'result','jobId':job_id})
        if result.get('ready') is False:return {'job':state,'provenance':provenance},'unavailable',issues,[]
        if result['job']!=state:raise NEngineError({'error':{'code':'JOB_REVISION','message':'Battle changed while reading report; repeat this read.'}})
        timeline=result['timeline']
        applications=[{'sourceId':s['sourceId'],'targetId':s['targetId'],'timeSeconds':s['timeUs']/1000000,
                       'damageApplicationMultiplierBeforeResistance':s['sample']['multiplier'],'meaning':'damage fraction, NOT hit probability',
                       'sampling':s['sampling'],'parameters':s['sample']['settings'],'targetSignatureMeters':s['sample']['targetSignatureMeters'],
                       'targetSpeedMetersPerSecond':s['sample']['targetSpeedMetersPerSecond']} for s in timeline['firstMissileApplicationSamples']]
        data={'job':state,'provenance':provenance,'destructions':timeline['destructions'],'shipSnapshots':timeline['ships'],
              'missileApplications':applications,'scope':result['scope'],'fullCombatSupported':False,
              'interpretation':['Every HP snapshot is valid only at its attached time. Exact opponent-death HP remains unavailable.',
                                'Outcome applies only to these fits/conditions/seed/policy. No general strength multiplier.',
                                'Time-to-kill reduction and inverse-time efficiency increase are different metrics.'],
              'detailResultId':self.core.store(result)}
        data['citableFacts']=[{'kind':'destruction','text':f"{d['shipId']} was destroyed at t={d['timeSeconds']} s.",'evidence':d} for d in timeline['destructions']]
        for ship in timeline['ships']:
            damage=ship['lastDamageSnapshot']
            if damage:
                data['citableFacts'].append({'kind':'last_damage_snapshot','text':f"{ship['shipId']}: {damage['hitpoints']} HP immediately after its last recorded damaging hit at t={damage['timeSeconds']} s. This is NOT HP at the opponent's destruction.",
                    'evidence':{'shipId':ship['shipId'],'timeUs':damage['timeUs'],'eventSequence':damage['eventSequence']}})
        data['unavailableClaims']=['Exact surviving-ship HP at opponent destruction','Measured sustained DPS over a declared time window','Counterfactual time to kill for the defeated ship','Universal ship-equivalence multiplier']
        return data,'complete' if state['complete'] else 'partial',issues,[]
