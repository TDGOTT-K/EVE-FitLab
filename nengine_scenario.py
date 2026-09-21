"""Static UI target geometry; all weapon application stays in NEngine."""
import math
from scenario_snapshots import context_fits


def resolve_context(fit, fits, analyze):
    fits=context_fits(fit,fits)
    value=fit.get('scenario') or {}
    if not value.get('targetFitId'): return None,None
    victim=next((f for f in fits if f.get('id')==value['targetFitId']),None)
    if victim is None: raise ValueError('情景目标装配已删除，请重新选择')
    clean={**victim,'scenario':{},'activeScenarioId':None}
    report=analyze(clean)
    signature=report['native']['attributes'].get('ship/552',{}).get('value')
    if signature is None: raise ValueError('目标信号半径不可计算，未使用默认半径代替')
    target={'id':str(victim['id']),'signatureMeters':signature}
    # Convert explicitly declared world-space vectors into the engine's target
    # inputs: missile speed is absolute, turret angular speed is relative.
    if 'geometry' in value:
        g=value['geometry']
        if not isinstance(g,dict):raise ValueError('情景位置或速度矢量无效')
        for key in ('x','y','vx','vy','ownVx','ownVy'):
            n=g.get(key,0) if key.startswith('own') else g.get(key)
            if type(n) not in (int,float) or not math.isfinite(n) or abs(n)>1e7:
                raise ValueError('情景位置或速度矢量无效：'+key)
        distance=math.hypot(g['x'],g['y'])
        rx=g['vx']-g.get('ownVx',0);ry=g['vy']-g.get('ownVy',0)
        value={**value,'distance':distance,'speed':math.hypot(g['vx'],g['vy']),
               'angular':abs(g['x']*ry-g['y']*rx)/(distance*distance) if distance else 0}
    for old,new,default,maximum in [('distance','distanceMeters',10000,1e7),('speed','speedMetersPerSecond',200,1e6),('angular','angularRadiansPerSecond',.01,100)]:
        n=value.get(old,default)
        if type(n) not in (float,int) or not math.isfinite(n) or not 0<=n<=maximum:
            raise ValueError('情景参数无效：'+old)
        target[new]=n
    layer_name={'structure':'hull','shield':'shield','armor':'armor','hull':'hull'}.get(value.get('targetLayer','shield'))
    if layer_name is None:raise ValueError('目标防御层无效')
    layers=(report['native'].get('defense') or {}).get('layers',[])
    layer=next((x for x in layers if x['layer']==layer_name),None)
    if layer:
        target['layer']={'name':layer_name,'resonances':layer['resonances']}
        receiver=report['native']['attributes'].get('ship/6186',{}).get('value')
        if receiver is not None:target['layer']['vortonReceiverMultiplier']=receiver
    elif fit.get('attackMode')=='edps':raise ValueError('目标防御层不可计算，不能显示 EDPS')
    source={'id':victim['id'],'name':victim['name'],'revision':victim.get('revision'),
            'fitHash':report['native']['fitHash'],'nativeFit':report['nativeFit'],
            'source':report['source'],'layer':layer_name}
    return target,source


def resolve_target(fit,fits,analyze):
    return resolve_context(fit,fits,analyze)[0]
