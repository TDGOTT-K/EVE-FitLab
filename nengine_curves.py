"""Sample the public static engine API; never duplicate weapon formulas."""
import json
import math
from functools import lru_cache
from nengine_adapter import bridge
from nengine_catalog import index_metadata


def build_curves(report):
    payload={key:report[key] for key in ('nativeFit','outputContext','outputSelection','baselineOutputSelection','scenarioTarget','source')}
    payload['items']=report['native']['outputContributions']['items']
    return _build(json.dumps(payload,sort_keys=True))


@lru_cache(maxsize=6)
def _build(key):
    report=json.loads(key);selection=report['outputSelection'];baseline=report['baselineOutputSelection']
    if not selection['completeSelection'] or not baseline['completeSelection']:
        return {'status':'unavailable','reason':'所选输出不完整，不能将部分小计画成完整 DPS 曲线。'}
    ids=report['outputContext']['selection']['contributionIds']
    items=[i for i in report['items'] if i['id'] in ids]
    target=report['scenarioTarget'];ideal=target is None
    radii=[];ranges=[];angular=[];references=[]
    data=index_metadata()
    for item in items:
        app=item['application'];turret=app.get('turretParameters');missile=app.get('missileParameters') or (app.get('fighterParameters') or {}).get('application')
        if ideal:
            dogma=data['typeDogma'].get(item['source']['typeId'],{})
            size=next((x['value'] for x in dogma.get('dogmaAttributes',[]) if x['attributeID']==128),None)
            ref={1:40,2:125,3:400,4:3000}.get(size)
            if ref is None:return {'status':'unavailable','reason':'无情景曲线缺少所选武器的尺寸参考，请选择具体情景目标。'}
            references.append(ref)
        if missile:radii.append(missile['explosionRadiusMeters'])
        distance=app.get('rangeMeters') or (app.get('missileFlight') or {}).get('nominalPathMeters') or 0
        if turret:distance=turret['optimalMeters']+3*turret['settings']['falloffMeters']
        if app.get('fighterParameters'):distance+=3*app['fighterParameters'].get('falloffMeters',0)
        ranges.append(distance)
    if ideal:
        target={'id':'fitlab-weapon-class-reference','distanceMeters':0,'speedMetersPerSecond':0,
                'angularRadiansPerSecond':0,'signatureMeters':max(references+radii)}
    for item in items:
        settings=(item['application'].get('turretParameters') or {}).get('settings')
        if settings and settings.get('signatureResolutionMeters',0)>0:
            angular.append(settings['tracking']*target['signatureMeters']/settings['signatureResolutionMeters'])
    definitions=[('distance','距离','km','distanceMeters',0,min(1e7,max([1000,target['distanceMeters']*1.5]+[r*1.15 for r in ranges]))),
        ('signature','目标信号半径','m','signatureMeters',.1,min(1e7,max([400,target['signatureMeters']*2]+[r*2 for r in radii]))),
        ('angular','角速度','rad/s','angularRadiansPerSecond',0,min(100,max([.02,target['angularRadiansPerSecond']*2]+[v*3 for v in angular])))]
    metric='appliedLoadedCycleDps' if baseline['metric']=='loadedCycleDps' else 'appliedCycleDps'
    series=[]
    for name,label,unit,field,low,high in definitions:
        xs={low+(high-low)*i/32 for i in range(33)}
        if not ideal:xs.add(target[field])
        if name=='signature':xs.update(v for v in radii if low<=v<=high)
        if name=='distance':
            for item in items:
                app=item['application'];turret=app.get('turretParameters')
                boundary=turret['optimalMeters'] if turret else app.get('rangeMeters')
                if boundary is not None and low<=boundary<=high:xs.update([boundary,math.nextafter(boundary,math.inf)])
        points=[]
        for x in sorted(xs):
            context={'output':{'selection':{'metric':metric,'contributionIds':ids},'target':{**target,field:x}}}
            result=bridge().call('fit_analyze',{'fit':report['nativeFit'],'context':context})['result']['outputContributions']['selection']
            points.append([x,result['total'] if result['completeSelection'] else None])
        series.append({'key':name,'label':label,'unit':unit,'min':low,'max':high,'points':points,
                       'currentX':target[field],'currentY':selection['total'],
                       'fixed':'静止目标 · 其余条件理想化 · 参考信号半径 '+str(target['signatureMeters'])+' m'})
    return {'status':'ready','series':series,'ideal':ideal,'totalDps':baseline['total'],
            'target':{'distance':target['distanceMeters'],'signature':target['signatureMeters'],'angular':target['angularRadiansPerSecond'],'speed':target['speedMetersPerSecond']},
            'yMax':max([1]+[y for s in series for _,y in s['points'] if y is not None]),
            'scope':'N 引擎静态应用 · 不扣抗性 · 导弹假定成功交付，不推断拦截与飞行时序'+(' · 有限弹量周期，非持续输出' if metric=='appliedLoadedCycleDps' else '')}
