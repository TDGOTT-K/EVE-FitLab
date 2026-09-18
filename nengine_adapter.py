"""FitLab v1 -> pinned NEngine r33. Mapping only; no duplicate fitting formulas."""
from functools import lru_cache
import json
from nengine_bridge import NEngineBridge
from nengine_catalog import effective_module_state
from nengine_output import METRICS, selected_ids, grouped_reading

@lru_cache(maxsize=1)
def bridge(): return NEngineBridge()

def native_fit(f, build):
    items=[]; subsystems=[]
    for s in f.get('slots',[]):
        if not s.get('item'): continue
        if s['kind']=='subsystem':
            subsystems.append({'id':s['key'],'typeId':s['item']}); continue
        state=effective_module_state(s)
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
    result={'id':f.get('id') or 'fitlab-draft','name':f.get('name'),'tags':f.get('tags',[]),'buildNumber':build,
        'shipTypeId':f['shipId'],'omittedSkills':'untrained',
        'skills':{str(s['skillTypeId']):s['level'] for s in f.get('skills',[])},
        'tacticalModeTypeId':f.get('tacticalModeTypeId'),'items':items,'subsystems':subsystems,
        'drones':drones,'fighters':fighters,
        'implants':[{'id':f'implant-{i}','typeId':x['typeId']} for i,x in enumerate(implants)],
        'boosters':[{'id':f'booster-{i}','typeId':x['typeId'],
            'enabledSideEffects':x.get('enabledSideEffects',[])} for i,x in enumerate(plan.get('boosters',[]))]}
    if 'cargo' in f or any('loadedCharges' in s for s in f.get('slots',[])) or 'crystals' in f:
        result['inventory']={'cargo':[{'id':e.get('id') or 'cargo-'+str(i),'typeId':e['item'],'quantity':e['quantity']} for i,e in enumerate(f.get('cargo',[]))],
            'magazines':[{'moduleId':s['key'],'typeId':s['ammo'],'loaded':s['loadedCharges']} for s in f.get('slots',[]) if s.get('item') and s.get('ammo') and 'loadedCharges' in s],
            'crystals':f.get('crystals',[])}
    return result

def analyze(f,target=None,native_query=None):
    client=bridge(); status=client.discover()
    build=status['source']['source']['buildNumber']
    native=native_fit(f,build)
    def query(context):
        return native_query(context) if native_query else client.call('fit_analyze',{'fit':native,'context':context})['result']
    context={}
    profile=f.get('damageProfile')
    if f.get('defenseMode')=='targeted' and isinstance(profile,list) and len(profile)==4:
        context['incomingDamage']=dict(zip(('em','thermal','kinetic','explosive'),profile))
    a=query(context)
    metric=f.get('outputMetric','nominalCycleDps')
    if metric not in METRICS: raise ValueError('输出口径无效')
    if 'outputContributions' not in a: raise ValueError('当前适配器需要 r24 分项输出接口，请检查独立副本路径')
    contribution_ids=selected_ids(f,a['outputContributions'])
    context['output']={'selection':{'metric':metric,'contributionIds':contribution_ids}}
    a=query(context)
    baseline=a['outputContributions']['selection']
    baseline_items=a['outputContributions']['items']
    effective=f.get('attackMode')=='edps' and target is not None
    if target is not None:
        metric=('effectiveLoadedCycleDps' if metric=='loadedCycleDps' else 'effectiveCycleDps') if effective else ('appliedLoadedCycleDps' if metric=='loadedCycleDps' else 'appliedCycleDps')
        context['output']={'selection':{'metric':metric,'contributionIds':contribution_ids},'target':target}
        a=query(context)
    output=a['outputContributions']
    selected=[item for item in output['items'] if item['id'] in contribution_ids]
    breakdown={kind:grouped_reading([item for item in selected if (
        item['kind'].startswith('fighter_') if kind=='fighters' else item['kind']=='drone' if kind=='drones' else item['kind'].startswith('ship_'))],metric)
        for kind in ('weapons','drones','fighters')}
    baseline_selected=[item for item in baseline_items if item['id'] in contribution_ids]
    baseline_breakdown={kind:grouped_reading([item for item in baseline_selected if (
        item['kind'].startswith('fighter_') if kind=='fighters' else item['kind']=='drone' if kind=='drones' else item['kind'].startswith('ship_'))],baseline['metric'])
        for kind in ('weapons','drones','fighters')}
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
        'dogmaTypeId':s['item'],'state':effective_module_state(s),
        'cpuUsage':value('module.'+s['key']+'/50'),'powergridUsage':value('module.'+s['key']+'/30')}
        for s in f.get('slots',[]) if s.get('item')]
    corrected=[s['key'] for s in f.get('slots',[]) if s.get('item') and s.get('state')=='Active' and effective_module_state(s)=='Online']
    notices=['已修正被动装备的旧启用状态为在线：'+', '.join(corrected)] if corrected else []
    if a.get('droneBay'):
        projection['attributeSnapshot']['maxActiveDrones']=a['droneBay']['maximumActive']
    if f.get('activeScenarioId') or f.get('scenario'):
        if target is None:notices.append('当前情景未配置目标，显示装配基准输出。')
        if target is not None:notices.append('情景按画布相对速度作静态应用参考；拦截及飞行时序未计入；EDPS 为选定固定层期望，不模拟层间推进。')
    if f.get('fighterUiMock') and not f.get('fighterLoadout'):
        notices.append('原舰载机示例保留在草稿中，请重新选择真实型号；示例不参与计算。')
    if 'inventory' in native:notices.append('静态库存已声明；未填写装弹量的模块仍为未知，不默认满弹或无限备弹。')
    # Native admission treats resource warnings as blocking, although fitting
    # analysis intentionally retains diagnostic attributes for over-limit drafts.
    issues=[*a['errors'],*(w for w in a['warnings'] if w['code']=='RESOURCE_EXCEEDED')]
    if not a['staticCoverageComplete']:
        issues.append({'code':'STATIC_COVERAGE_INCOMPLETE','message':'当前引擎副本未覆盖部分效果；分项是否可用以各自状态为准，完整装配尚未通过校验。'})
    selection=fighter_damage_selection(f,a)
    return {'provider':'nengine','contract':'fitlab-analysis-v2','engineVersion':status['engineVersion'],
        'fighterDamageSelection':selection,'outputSelection':output['selection'],'outputBreakdown':breakdown,'baselineOutputBreakdown':baseline_breakdown,
        'outputContext':context['output'],'baselineOutputSelection':baseline,'baselineOutputItems':baseline_items,
        'scenarioTarget':target,'attackMode':'edps' if effective else 'dps','curveRequest':f,
        'native':a,'nativeFit':native,'attributes':projection,'snapshot':{'modules':modules},
        'skillCount':len(native['skills']),'isValid':not issues,
        'issues':issues,'integrationNotices':notices,'source':{'buildNumber':build,'revision':client.baseline['revision']}}

def fighter_damage_selection(f,analysis):
    """Sum only selected engine-returned primary contributions; never model abilities.

    This is a view selection, not a change to deployment or engine combat state.
    """
    rows={}
    for location in ('tubes','reserve'):
        for i,row in enumerate((f.get('fighterLoadout') or {}).get(location,[])):
            if row: rows[row.get('id') or f'fighter-{location}-{i}']=row
    output=analysis['outputContributions']
    ids=set(selected_ids(f,output))
    primaries=[item for item in output['items'] if item['kind'] in ('fighter_primary','fighter_missile_primary') and item['source'].get('deployed')]
    selected=[item for item in primaries if item['id'] in ids]
    reading=grouped_reading(selected,'nominalCycleDps')
    return {'primaryDps':reading['total'],
        'includedSquadrons':[item['source']['squadronId'] for item in selected],
        'excludedSquadrons':[item['source']['squadronId'] for item in primaries if item['id'] not in ids],
        'scope':'selected_engine_primary_contributions_only'}

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
