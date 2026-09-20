"""Present the public engine curve query. No reference, grid or peak computation here."""
import json
from functools import lru_cache
from nengine_adapter import bridge


def build_curves(report):
    request={'fit':report['nativeFit'],'query':{'selection':report['outputContext']['selection'],
        'target':report['scenarioTarget'],'referencePolicy':'weapon-size-ideal-v1','intervals':32}}
    return _build(json.dumps(request,sort_keys=True))


@lru_cache(maxsize=6)
def _build(key):
    request=json.loads(key);client=bridge();client.discover()
    result=client.call('fit_output_curves',request)['result']
    if result['state']!='ready':
        reasons={'INCOMPLETE_OUTPUT_SELECTION':'所选输出不完整，不能将部分小计画成完整 DPS 曲线。',
            'WEAPON_SIZE_REFERENCE_UNAVAILABLE':'所选武器缺少尺寸参考，请选择具体情景目标。',
            'TARGET_LAYER_REQUIRED':'EDPS 曲线需要明确目标防御层。',
            'CURRENT_APPLICATION_UNAVAILABLE':'当前目标下的输出不可计算，请查看原生输出诊断。',
            'TARGET_GEOMETRY_REQUIRED':'曲线需要完整的距离、信号半径、速度和角速度条件。',
            'TARGET_OUTSIDE_CURVE_POLICY_RANGE':'目标超出当前曲线政策的取样范围。',
            'CURVE_SAMPLE_LIMIT':'取样点数量超过接口上限。','CURVE_CONTRIBUTION_LIMIT':'输出分项数量超过曲线批量查询上限。'}
        return {'status':'unavailable','reason':reasons.get(result['reason'],result['reason']),'native':result,'request':request}
    target=result['referenceTarget'];baseline=result['baseline'];current=result['current'];ideal=result['ideal']
    effective=current['metric'].startswith('effective')
    series=[]
    for axis in result['series']:
        series.append({'key':axis['axis'],'label':{'distance':'距离','signature':'目标信号半径','angular':'角速度'}[axis['axis']],
            'unit':'km' if axis['axis']=='distance' else axis['unit'],'min':axis['minimum'],'max':axis['maximum'],
            'points':[[p['x'],p['selection']['total'] if (p.get('selection') or {}).get('completeSelection') else None,
                (p.get('comparison') or {}).get('ratio',{}).get('value')] for p in axis['points']],
            'sampledPeak':axis['sampledPeak'],'currentX':axis['currentX'],'currentY':current['total'],'currentRatio':(result.get('currentComparison') or {}).get('ratio',{}).get('value'),
            'fixed':('静止目标 · 其余条件理想化 · 参考信号半径 '+str(target['signatureMeters'])+' m') if ideal else '其余目标条件保持不变'})
    return {'status':'ready','attackMode':'edps' if effective else 'dps','ratiosFromEngine':True,'series':series,
        'angularLinearSpeedReferenceMeters':result['angularLinearSpeedReferenceMeters'],'ideal':ideal,'totalDps':baseline['total'],'target':{'distance':target['distanceMeters'],'signature':target['signatureMeters'],
        'angular':target['angularRadiansPerSecond'],'speed':target['speedMetersPerSecond']},
        'yMax':(1 if result['sampledPeak'] is None else max(1,result['sampledPeak'])),'sampledPeak':result['sampledPeak'],
        'scope':('固定'+{'shield':'护盾','armor':'装甲','hull':'结构'}.get((target.get('layer') or {}).get('name'),'目标')+'层 EDPS · 已扣抗性' if effective else 'N 引擎静态应用 · 不扣抗性')+' · 导弹按标称射程截断 · 不计追击与飞行延迟'+(' · 有限弹量周期，非持续输出' if baseline['metric']=='loadedCycleDps' else ''),
        'native':result,'request':request}
