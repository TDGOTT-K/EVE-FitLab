"""Static UI target geometry; all weapon application stays in NEngine."""
import math


def resolve_target(fit, fits, analyze):
    value=fit.get('scenario') or {}
    if not value.get('targetFitId'): return None
    victim=next((f for f in fits if f.get('id')==value['targetFitId']),None)
    if victim is None: raise ValueError('情景目标装配已删除，请重新选择')
    clean={**victim,'scenario':{},'activeScenarioId':None}
    report=analyze(clean)
    signature=report['native']['attributes'].get('ship/552',{}).get('value')
    if signature is None: raise ValueError('目标信号半径不可计算，未使用默认半径代替')
    target={'id':str(victim['id']),'signatureMeters':signature}
    for old,new,default,maximum in [('distance','distanceMeters',10000,1e7),('speed','speedMetersPerSecond',200,1e6),('angular','angularRadiansPerSecond',.01,100)]:
        n=value.get(old,default)
        if type(n) not in (float,int) or not math.isfinite(n) or not 0<=n<=maximum:
            raise ValueError('情景参数无效：'+old)
        target[new]=n
    return target
