"""FitLab v1 -> NEngine r9. Mapping only; no duplicate fitting formulas."""
from functools import lru_cache
import json
from nengine_bridge import NEngineBridge

@lru_cache(maxsize=1)
def bridge(): return NEngineBridge()

def native_fit(f, build):
    items=[]; subsystems=[]
    for s in f.get('slots',[]):
        if not s.get('item'): continue
        if s['kind']=='subsystem':
            subsystems.append({'id':s['key'],'typeId':s['item']}); continue
        if s.get('state'): state=s['state']
        elif not s.get('online',True): state='Offline'
        else:
            from nengine_catalog import index_metadata
            metadata=index_metadata()
            effects=metadata['typeDogma'].get(s['item'],{}).get('dogmaEffects',[])
            active=any(metadata['dogmaEffects'].get(e['effectID'],{}).get('effectCategoryID') in (1,2,3) for e in effects)
            state='Active' if active else 'Online'
        item={'id':s['key'],'typeId':s['item'],'chargeTypeId':s.get('ammo'),
              'slotIndex':int(s['key'].split('-')[-1]),'online':state!='Offline',
              'active':state in ('Active','Overload'),'overheated':state=='Overload'}
        if s.get('mutation'): item['mutation']=s['mutation']
        items.append(item)
    drones=[]
    if sum(x['quantity'] for x in f.get('drones',[]))>1000:
        raise ValueError('本适配层一次静态查询最多展开 1000 架无人机，未改写库存。')
    for i,row in enumerate(f.get('drones',[])):
        for j in range(row['quantity']):
            d={'id':f'drone-{i}-{j}','typeId':row['item'],'deployed':j<row.get('active',0)}
            if row.get('mutation'): d['mutation']=row['mutation']
            drones.append(d)
    plan=f.get('loadoutPlan') or {}
    implants=plan.get('implants',f.get('implantPlan',[]))
    fighters=[]
    for location in ('tubes','reserve'):
        for i,row in enumerate((f.get('fighterLoadout') or {}).get(location,[])):
            if not row: continue
            if type(row.get('quantity')) is not int or not 1<=row['quantity']<=12:
                raise ValueError('舰载机中队数量必须为 1–12 的整数，实际型号上限由引擎校验')
            ident=row.get('id') or f'fighter-{location}-{i}'
            fighters.append({'id':ident,'typeId':row['typeId'],
                'memberIds':[f'{ident}-{j}' for j in range(row['quantity'])],
                'deployed':location=='tubes' and row.get('active',True)})
    if len(fighters)>200: raise ValueError('本适配层一次最多分析 200 个舰载机中队')
    return {'id':f.get('id') or 'fitlab-draft','name':f.get('name'),'buildNumber':build,
        'shipTypeId':f['shipId'],'omittedSkills':'untrained',
        'skills':{str(s['skillTypeId']):s['level'] for s in f.get('skills',[])},
        'tacticalModeTypeId':f.get('tacticalModeTypeId'),'items':items,'subsystems':subsystems,
        'drones':drones,'fighters':fighters,
        'implants':[{'id':f'implant-{i}','typeId':x['typeId']} for i,x in enumerate(implants)],
        'boosters':[{'id':f'booster-{i}','typeId':x['typeId'],
            'enabledSideEffects':x.get('enabledSideEffects',[])} for i,x in enumerate(plan.get('boosters',[]))]}

def analyze(f):
    client=bridge(); status=client.discover()
    build=status['source']['source']['buildNumber']
    native=native_fit(f,build)
    context={}
    profile=f.get('damageProfile')
    if f.get('defenseMode')=='targeted' and isinstance(profile,list) and len(profile)==4:
        context['incomingDamage']=dict(zip(('em','thermal','kinetic','explosive'),profile))
    a=client.call('fit_analyze',{'fit':native,'context':context})['result']
    attrs=a['attributes']
    def value(key):return attrs.get(key,{}).get('value')
    resources={r['id']:r for r in a['resources']}
    projection={'attributeSnapshot':{t['name']:t['value'] for k,t in attrs.items() if k.startswith('ship/')},
        'slotUsage':[{'kind':kind,'available':int(value('ship/'+str(id)))} for kind,id in [('High',14),('Mid',13),('Low',12),('Rig',1137)] if value('ship/'+str(id)) is not None],
        'turretHardpointsAvailable':value('ship/102'),'launcherHardpointsAvailable':value('ship/101'),
        'maxVelocity':a.get('motion',{}).get('maximumSpeedMetersPerSecond') if a.get('motion') else None}
    for old,new in [('cpu','cpu'),('powergrid','powergrid'),('droneBay','droneBay'),('droneBandwidth','droneBandwidth')]:
        r=resources.get(new,{})
        projection[old+'Used']=r.get('used');projection[old+'Available']=r.get('capacity')
    # Existing slot rows consume this thin shape until they migrate to native traces.
    modules=[{'workspaceSlotKey':s['key'],'slotKind':s['kind'].capitalize(),
        'dogmaTypeId':s['item'],'state':s.get('state','Online'),
        'cpuUsage':value('module.'+s['key']+'/50'),'powergridUsage':value('module.'+s['key']+'/30')}
        for s in f.get('slots',[]) if s.get('item')]
    notices=[]
    if a.get('droneBay'):
        projection['attributeSnapshot']['maxActiveDrones']=a['droneBay']['maximumActive']
    if f.get('activeScenarioId') or f.get('scenario'):
        notices.append('当前情景尚未接入 N 号静态查询，显示装配基准值；未套用旧引擎公式。')
    if f.get('fighterUiMock') and not f.get('fighterLoadout'):
        notices.append('原舰载机示例保留在草稿中，请重新选择真实型号；示例不参与计算。')
    if f.get('cargo'): notices.append('本批尚未接入货舱库存校验。')
    issues=list(a['errors'])
    if not a['staticCoverageComplete']:
        issues.append({'code':'STATIC_COVERAGE_INCOMPLETE','message':'当前引擎副本未覆盖部分装备或技能效果，数值不可用；未删减输入。'})
    return {'provider':'nengine','contract':'fitlab-analysis-v2','engineVersion':status['engineVersion'],
        'native':a,'nativeFit':native,'attributes':projection,'snapshot':{'modules':modules},
        'skillCount':len(native['skills']),'isValid':not issues,
        'issues':issues,'integrationNotices':notices,'source':{'buildNumber':build,'revision':9}}

@lru_cache(maxsize=1)
def fighter_catalog():
    client=bridge();client.discover()
    page=client.call('catalog_search',{'query':{'categoryId':87,'limit':100}})['result']
    # Read immutable indexed metadata from the SAME pinned copy, not legacy SDE.
    ids={x['typeId'] for x in page['items']}
    attrs={}
    index=client.root/client.baseline['dataDirectory']
    with (index/'typeDogma.jsonl').open(encoding='utf-8') as stream:
        for line in stream:
            record=json.loads(line)
            if record['_key'] in ids:
                attrs[record['_key']]={x['attributeID']:x['value'] for x in record.get('dogmaAttributes',[])}
    result=[]
    for item in page['items']:
        a=attrs.get(item['typeId'],{})
        kind=next((k for k,id in [('light',2212),('support',2213),('heavy',2214)] if a.get(id)==1),None)
        if kind is None or not a.get(2215): continue
        result.append({'id':item['typeId'],'typeId':item['typeId'],'name':item['names'].get('zh',item['names']['en']),
            'en':item['names']['en'],'class':kind,'max':int(a[2215]),'meta':item.get('metaGroupId')})
    return {'source':page['source'],'items':result}
