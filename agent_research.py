"""Lossless public-event projections. No combat formulas or replay guesses."""
from nengine_bridge import NEngineError


def unavailable(state):
    if state['status'] in ('complete','cancelled') or state.get('canResume'):return None
    return {'ready':False,'job':state,'nextCall':{'tool':'fitlab_battle','arguments':{'action':'status','jobId':state['id'],'waitSeconds':10}} if state['status'] in ('queued','running') else None,
            'reason':'Result is not published. Do not resubmit start with a new jobId.'}


def identity(state):
    return tuple(state.get(k) for k in ('attempt','revision','status','eventsHash','checkpointHash'))


def pages(agent,job_id,state,offset=0):
    while True:
        page=agent.native('battle_events',{'jobId':job_id,'offset':offset,'limit':100})
        if page['attempt']!=state['attempt']:raise ValueError('Battle attempt changed; repeat the read.')
        yield page
        if not page['hasMore']:break
        if page['nextOffset']<=offset:raise ValueError('Event cursor did not advance')
        offset=page['nextOffset']


def consistent(agent,job_id,state):
    if identity(agent.native('battle_status',{'jobId':job_id}))!=identity(state):
        raise NEngineError({'ok':False,'error':{'code':'JOB_REVISION','message':'Battle changed during read; repeat this read.'}})


def project_timeline(result,events):
    deaths=[];damage={};applications={}
    for event in events:
        if event['kind']=='ShipDestroyed':
            deaths.append({'shipId':event['targetId'],'timeUs':event['timeUs'],'timeSeconds':event['timeUs']/1000000,'eventSequence':event['sequence']})
        if event.get('layeredDamage') is not None and event['layeredDamage'].get('appliedHitpoints',0)>0:
            defense=event['layeredDamage'].get('defense')
            if defense is not None:
                damage[event['targetId']]={'timeUs':event['timeUs'],'timeSeconds':event['timeUs']/1000000,'eventSequence':event['sequence'],'hitpoints':defense['totalHitpoints'],
                    'layers':[{k:l[k] for k in ('name','capacity','hitpoints')} for l in defense['layers']],
                    'meaning':'Snapshot after this damage event only; not a snapshot at another ship death or simulation end.'}
        if event.get('missileApplication') is not None:
            key=(event.get('sourceId'),event.get('targetId'))
            if key not in applications:applications[key]={'sourceId':key[0],'targetId':key[1],'timeUs':event['timeUs'],'eventSequence':event['sequence'],
                'sample':event['missileApplication'],'sampling':'first matching event, not an average'}
    return {'resultTimeUs':result['job']['timeUs'],'resultTimeSeconds':result['job']['timeUs']/1000000,'attempt':result['job']['attempt'],'complete':result['job']['complete'],
        'coverage':'published attempt only; resumed attempts may contain only a suffix',
        'destructions':deaths,'ships':[{'shipId':s['id'],'lastDamageSnapshot':damage.get(s['id']),
            'atSimulationEnd':{'timeUs':result['job']['timeUs'],'timeSeconds':result['job']['timeUs']/1000000,'hitpoints':s['hitpoints'],'destroyed':s['destroyed']} if result['job']['complete'] else None,
            'atPartialCheckpoint':None if result['job']['complete'] else {'timeUs':result['job']['timeUs'],'hitpoints':s['hitpoints'],'destroyed':s['destroyed']},
            'atOtherShipDestruction':{'state':'unavailable','reason':'No exact simultaneous ship snapshot in this result; do not substitute last damage or end state.'}} for s in result['ships']],
        'firstMissileApplicationSamples':list(applications.values()),
        'interpretation':'End HP can include passive recharge after the opponent died and later in-flight impacts. Do not label end HP as victory-time HP. A single fitting, stationary policy and seed do not establish general ship strength; no power multiplier is computed.'}


def report(agent,job_id):
    state=agent.native('battle_status',{'jobId':job_id})
    waiting=unavailable(state)
    if waiting:return waiting
    result=agent.native('battle_result',{'jobId':job_id})
    if identity(result['job'])!=identity(state):raise ValueError('Battle changed; repeat read.')
    # Stream pages: retain only the small timeline, not the complete event stream.
    relevant=(event for page in pages(agent,job_id,state) for event in page['events'] if event['kind']=='ShipDestroyed' or event.get('layeredDamage') is not None or event.get('missileApplication') is not None)
    timeline=project_timeline(result,relevant)
    consistent(agent,job_id,state)
    return agent.bounded({**result,'timeline':timeline})


def filtered_events(agent,args):
    job_id=args['jobId'];state=agent.native('battle_status',{'jobId':job_id})
    waiting=unavailable(state)
    if waiting:return waiting
    kinds=args.get('kinds',[]);limit=args.get('limit',20);offset=args.get('offset',0)
    if not isinstance(kinds,list) or any(not isinstance(k,str) for k in kinds) or len(kinds)>20:raise ValueError('kinds must be at most 20 event names')
    if type(limit)!=int or not 1<=limit<=100 or type(offset)!=int or offset<0:raise ValueError('limit 1..100, offset >=0')
    selected=[];next_offset=offset;has_more=False
    for page in pages(agent,job_id,state,offset):
        for index,event in enumerate(page['events']):
            next_offset=page['offset']+index+1
            if kinds and event['kind'] not in kinds:continue
            if args.get('sourceId') is not None and event.get('sourceId')!=args['sourceId']:continue
            if args.get('targetId') is not None and event.get('targetId')!=args['targetId']:continue
            selected.append(event)
            if len(selected)==limit:
                has_more=index+1<len(page['events']) or page['hasMore'];break
        if len(selected)==limit:break
    consistent(agent,job_id,state)
    full=agent.store({'attempt':state['attempt'],'events':selected})
    # Null union payloads are omitted for readability; full original events remain addressable.
    compact=[{k:v for k,v in e.items() if v is not None} for e in selected]
    return agent.bounded({'attempt':state['attempt'],'events':compact,'nextOffset':next_offset,'hasMore':has_more,'cursorMeaning':'raw event offset; hasMore means more events to scan, not guaranteed matching events','fullResultId':full})
