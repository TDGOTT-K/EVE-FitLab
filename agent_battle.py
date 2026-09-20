"""Battle task entry: only compose public engine assembly and job calls."""
import time

STATIONARY = '''function tick({observation,memory}) {
 const intents=[];
 for(const ship of observation.ownedShips) {
  if(ship.destroyed)continue;
  const target=observation.contacts.filter(c=>c.entityKind==='ship' && c.team!==observation.team && !c.destroyed).sort((a,b)=>a.id.localeCompare(b.id))[0];
  if(!target)continue;
  const common={entityId:ship.id,observedRevision:observation.revision};
  if(ship.locks[target.id]===undefined)intents.push({...common,kind:'LockTarget',targetId:target.id,commandId:`${observation.timeUs}:${ship.id}:lock`});
  if(!ship.completedLocks.includes(target.id))continue;
  for(const ability of ship.abilities) {
   if(!['missile','direct'].includes(ability.mechanism)||ability.active||ability.reload?.dueAtUs!=null)continue;
   if(ability.ammo<ability.ammoPerActivation)continue;
   intents.push({...common,kind:'ActivateAbility',abilityId:ability.id,targetId:target.id,commandId:`${observation.timeUs}:${ship.id}:${ability.id}`});
  }
 }
 return {memory,intents};
}'''


def battle(agent,args):
    action=args['action']
    allowed={'action'}|({'id','seed','seconds','ships'} if action=='prepare' else {'draftId','jobId','policies','policyPreset'} if action=='start' else {'jobId','waitSeconds'} if action=='status' else {'jobId','offset','limit'} if action=='events' else {'jobId'})
    if set(args)-allowed:raise ValueError('Unexpected fields for battle '+action)
    required={'prepare':{'id','seed','seconds','ships'},'start':{'draftId','jobId'},'status':{'jobId'},'result':{'jobId'},'events':{'jobId'},'cancel':{'jobId'}}.get(action)
    if required is None:raise ValueError('Unknown battle action')
    if required-set(args):raise ValueError('Missing fields for '+action+': '+', '.join(sorted(required-set(args))))
    if action=='prepare':
        ships=args['ships']
        if not isinstance(ships,list) or not 2<=len(ships)<=32:raise ValueError('Provide 2..32 ships with sessionId, revision, id, team, position, reservePerWeapon.')
        participants=[];snapshots=[]
        for ship in ships:
            if set(ship)-{'sessionId','revision','id','team','position','velocity','reservePerWeapon','supplies','options'}:raise ValueError('Unexpected participant fields')
            snap=agent.native('fit_inspect',{'sessionId':ship['sessionId']})
            session=snap['session'];fit=session['working'];analysis=snap['analysis']
            if session['revision']!=ship['revision']:raise ValueError('Session revision changed: '+ship['sessionId']+'; read it again before preparing.')
            blockers=analysis['errors']+[w for w in analysis.get('warnings',[]) if w.get('code')=='RESOURCE_EXCEEDED']
            if blockers or not analysis['staticCoverageComplete']:
                from nengine_bridge import NEngineError
                raise NEngineError({'ok':False,'error':{'code':'BATTLE_FIT_NOT_READY','message':'Fix this fitting before battle: '+ship['sessionId'],'issues':blockers},'analysisResultId':agent.store(analysis)})
            supplies=ship.get('supplies')
            if supplies is None:
                if 'reservePerWeapon' not in ship:raise ValueError('Specify reservePerWeapon (for example 30), or explicit supplies per ability. There is no unlimited ammunition default.')
                reserve=ship['reservePerWeapon']
                if type(reserve)!=int or not 0<=reserve<=1000000:raise ValueError('reservePerWeapon must be a finite integer 0..1000000')
                supplies={}
                for key,weapon in analysis.get('weapons',{}).items():
                    reload=weapon.get('reload')
                    if reload is None:raise ValueError('This weapon requires explicit supplies and native policy options: '+key)
                    supplies[key]={'loaded':reload['magazineCharges'],'reserve':reserve,'automaticReload':True}
                if analysis.get('capacitorInjectors') or analysis.get('bombLaunchers'):raise ValueError('Injectors and bombs require explicit supplies and native policy options.')
            participant={'id':ship['id'],'team':ship['team'],'fit':fit,'position':ship['position'],'velocity':ship.get('velocity',{'x':0,'y':0,'z':0}),
                         'lockDurationSeconds':None,'targetingPolicy':'scan-signature-community-v1','shieldFraction':1,'armorFraction':1,'hullFraction':1,'capacitorFraction':1,'supplies':supplies}
            options=ship.get('options',{})
            if set(options)&{'id','team','fit','position','velocity','supplies'}:raise ValueError('Participant options cannot override identity, fitting or supply bindings.')
            participant.update(options);participants.append(participant)
            snapshots.append({'id':ship['id'],'team':ship['team'],'sessionId':ship['sessionId'],'revision':session['revision'],'fitHash':analysis['fitHash']})
        request={'id':args['id'],'seed':args['seed'],'horizonSeconds':args['seconds'],'participants':participants}
        assembly=agent.native('battle_assemble',{'request':request})
        status=agent.native('engine_status',{})
        draft={'kind':'fitlab-battle-draft-v1','engineVersion':status['engineVersion'],'runtime':status['publicContract']['controllerRuntimeId'],'snapshots':snapshots,'assembly':assembly}
        identity=agent.store(draft,durable=True)
        roster=[{'id':s['id'],'team':s['team'],'abilities':[{'id':a['id'],'mechanism':a['mechanism']} for a in s.get('abilities',[])]} for s in assembly['scenario']['ships']]
        return {'draftId':identity,'ships':snapshots,'roster':roster,'seconds':args['seconds'],'seed':args['seed'],'scriptTemplate':STATIONARY,
                'initialConditions':[{k:p[k] for k in ('id','position','velocity','shieldFraction','armorFraction','hullFraction','capacitorFraction','targetingPolicy','lockDurationSeconds','supplies')} for p in participants],
                'scope':assembly['scope'],'limitations':assembly['limitations'],'next':'Call fitlab_battle start with draftId, jobId and policyPreset="stationary-weapons-v1", or explicit policies keyed by team. The preset locks the first enemy and fires conventional guns/missiles; no movement, repair, EWAR or drones.'}
    if action=='start':
        draft=agent.load_result(args['draftId'])
        if not isinstance(draft,dict) or draft.get('kind')!='fitlab-battle-draft-v1':raise ValueError('Expected draftId from fitlab_battle prepare.')
        status=agent.native('engine_status',{})
        if draft['engineVersion']!=status['engineVersion'] or draft['runtime']!=status['publicContract']['controllerRuntimeId']:raise ValueError('Engine/runtime changed; prepare this battle again.')
        if ('policies' in args)==('policyPreset' in args):raise ValueError('Select exactly one: policies or policyPreset.')
        if 'policyPreset' in args:
            if args['policyPreset']!='stationary-weapons-v1':raise ValueError('Unknown preset')
            if any(a['mechanism'] not in ('direct','missile') for s in draft['assembly']['scenario']['ships'] for a in s.get('abilities',[])):
                raise ValueError('Stationary preset only supports conventional gun/missile batteries. Supply explicit policies for other abilities.')
            policies={p['team']:STATIONARY for p in draft['snapshots']}
        else:policies=args['policies']
        teams={p['team'] for p in draft['snapshots']}
        if set(policies)!=teams:raise ValueError('Policies must cover exactly all participant teams: '+','.join(sorted(teams)))
        request={'scenario':draft['assembly']['scenario'],'policies':policies,'endSeconds':draft['assembly']['request']['horizonSeconds']}
        result=agent.native('battle_start',{'jobId':args['jobId'],'request':request})
        return {'job':result,'draftId':args['draftId'],'policy':args.get('policyPreset','custom'),'next':'Poll fitlab_battle status; only complete is a completed run. Then result or events. Retry identical jobId/draftId/policies after an uncertain response.'}
    if action=='status':
        wait=args.get('waitSeconds',0)
        if type(wait) not in (int,float) or not 0<=wait<=10:raise ValueError('waitSeconds must be 0..10')
        deadline=time.monotonic()+wait
        while True:
            result=agent.native('battle_status',{'jobId':args['jobId']})
            if result['status'] not in ('queued','running') or time.monotonic()>=deadline:return agent.bounded(result)
            time.sleep(min(.2,max(0,deadline-time.monotonic())))
    if action in ('result','cancel'):
        return agent.bounded(agent.native('battle_'+action,{'jobId':args['jobId']}))
    if action=='events':
        return agent.bounded(agent.native('battle_events',{'jobId':args['jobId'],'offset':args.get('offset',0),'limit':args.get('limit',30)}))
    raise ValueError('Unknown battle action')
