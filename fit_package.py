"""Portable UI snapshot plus public native replay requests. No numerical formulas."""
import copy,hashlib,json
from nengine_adapter import bridge,source_binding,validate_source_binding
from nengine_valuation import value_fit
from scenario_snapshots import context_fits

FORMAT='fitlab-static-fit-1'
FIELDS=('name','tags','notes','shipId','slots','skills','characterName','drones','cargo','crystals',
        'tacticalModeTypeId','fighterLoadout','loadoutPlan','implantPlan','outputMetric','attackMode',
        'defenseMode','damageProfile','capacitorHorizon','scenario','scenarios','activeScenarioId')
REFS=('targetFitId','supportFitId','hostileFitId')

def digest(value):
    # JSON browser roundtrips turn e.g. 1.0 into 1. Hash semantic JSON numbers,
    # not Python's choice of int/float spelling. This is not a game calculation.
    def normalize(v):
        if isinstance(v,float) and v.is_integer():return int(v)
        if isinstance(v,dict):return {k:normalize(x) for k,x in v.items()}
        if isinstance(v,list):return [normalize(x) for x in v]
        return v
    return hashlib.sha256(json.dumps(normalize(value),sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()).hexdigest()

def portable(fit,binding):
    result={k:copy.deepcopy(fit[k]) for k in FIELDS if k in fit}
    result['nativeFitId']=fit.get('nativeFitId') or fit.get('id') or 'fitlab-draft'
    result['sourceBinding']=copy.deepcopy(binding)
    # Plan library pointers and character account linkage are not portable inputs.
    plan=result.get('loadoutPlan')
    if isinstance(plan,dict):
        for key in list(plan):
            if key not in ('name','implants','boosters','pilot'):del plan[key]
        if isinstance(plan.get('pilot'),dict):
            plan['pilot']={k:v for k,v in plan['pilot'].items() if k in ('name','skills')}
        plan['customized']=True
    return result

def witness(report,valuation):
    cap=report['capacitorScenario']
    return {'analysis':{'request':{'fit':report['nativeFit'],'context':report['native']['metricContext']},
                        'fitHash':report['native']['fitHash'],'resultSha256':digest(report['native'])},
            'capacitor':{'request':cap.get('request'),'state':cap['state'],
                         'resultSha256':digest(cap.get('result')),'reason':cap.get('reason'),'diagnostic':cap.get('diagnostic')},
            'valuation':{'request':valuation['request'],'resultSha256':digest(valuation['valuation'])}}

def export_package(fit,library,analyze,validate,market=None):
    client=bridge();client.discover();validate_source_binding(fit,client)
    binding=source_binding(client);main=portable(fit,binding)
    candidates=context_fits(fit,library)
    if fit.get('id'):candidates=[fit]+[x for x in candidates if x.get('id')!=fit['id']]
    contexts=[main.get('scenario',{}),*(x['value'] for x in main.get('scenarios',[]))]
    references=sorted({c[k] for c in contexts for k in REFS if c.get(k)})
    frozen=[]
    for ident in references:
        source=next((x for x in candidates if x.get('id')==ident),None)
        if source is None:raise ValueError('无法导出：情景引用的装配已不存在：'+ident)
        validate_source_binding(source,client)
        row=portable(source,binding);row.update(id=ident,revision=source.get('revision'),scenario={},activeScenarioId=None)
        row.pop('scenarios',None);validate(row);frozen.append(row)
    if frozen:main['scenarioSnapshots']=frozen
    validate(main)
    report=analyze(main)
    quote=value_fit(main,market if market is not None else fit.get('valuationSnapshot'))
    main['valuationSnapshot']=quote['request']['snapshot']
    payload={'format':FORMAT,'sourceBinding':binding,'fit':main,'replay':witness(report,quote)}
    if len(json.dumps(payload,ensure_ascii=False,indent=2).encode())>1700000:raise ValueError('完整装配文件过大，未截断导出；请减少引用情景数量')
    return {**payload,'contentSha256':digest(payload)}

def import_package(document,analyze,validate):
    if not isinstance(document,dict) or set(document)!={'format','sourceBinding','fit','replay','contentSha256'} or document.get('format')!=FORMAT:
        raise ValueError('不是支持的 FitLab 装配文件')
    payload={k:v for k,v in document.items() if k!='contentSha256'}
    if digest(payload)!=document['contentSha256']:raise ValueError('装配文件校验失败，内容已变化')
    client=bridge();client.discover();validate_source_binding(document,client)
    fit=copy.deepcopy(document['fit'])
    if not isinstance(fit,dict) or set(fit)-set(FIELDS)-{'nativeFitId','sourceBinding','scenarioSnapshots','valuationSnapshot'}:
        raise ValueError('装配文件包含未知或本地管理字段')
    if fit.get('sourceBinding')!=document['sourceBinding']:raise ValueError('装配来源绑定不一致')
    for row in context_fits(fit,[]):
        if set(row)-set(FIELDS)-{'nativeFitId','sourceBinding','id','revision'}:raise ValueError('情景快照包含未知字段')
        if row.get('sourceBinding')!=document['sourceBinding']:raise ValueError('情景快照来源绑定不一致')
        validate_source_binding(row,client);validate(row)
    validate(fit)
    report=analyze(fit);quote=value_fit(fit)
    if witness(report,quote)!=document['replay']:raise ValueError('原生输入或重放结果与导出时不同，未导入')
    return {'fit':fit,'report':report,'verified':True,'notes':['原生输入、fitHash、分析、电容和报价快照已复核。','情景引用随文件冻结保存，不随本地其他装配改变。']}
