"""Static DPS sweeps through the same application calculator as the workspace."""
import math
from fitting_scenario import calculate_scenario, turret_factor, missile_factor


def build_dps_curves(report, types):
    view = report['workspace']
    if not view['target']:
        return build_ideal_curves(report, types)
    if view['attack']['total'] is None:
        return {'status': 'unavailable', 'reason': '；'.join(view['issues']) or '当前目标应用模型不完整。'}
    target = report['scenarioAnalysis']['target']
    modules = [report['snapshot']['modules'][r['moduleIndex']] for r in report['scenarioAnalysis']['weapons']]
    ranges = [m.get('optimalRangeMeters', 0) + 3 * m.get('falloffRangeMeters', 0) for m in modules]
    angular = [m.get('trackingSpeed', 0) * target['signature'] / (m.get('signatureResolutionMeters', 0) or 40000)
               for m in modules if m.get('applicationKind') == 'Turret']
    radius = [m.get('missileExplosionRadiusMeters', 0) for m in modules]
    definitions = [
        ('distance', '距离', 'km', 0, min(1e7, max(1000, target['distance'] * 1.5, *(r * 1.15 for r in ranges)))),
        ('signature', '目标信号半径', 'm', .1, min(1e7, max(400, target['signature'] * 2, *radius))),
        ('angular', '角速度', 'rad/s', 0, min(100, max(.02, target['angular'] * 2, *(x * 3 for x in angular)))),
    ]
    series = []
    for key, label, unit, low, high in definitions:
        # Include the exact current coordinate and mechanism boundaries, not just
        # uniformly spaced samples. Adjacent float samples preserve hard cutoffs.
        xs = {low + (high - low) * i / 96 for i in range(97)} | {target[key]}
        if key == 'distance':
            for m in modules:
                optimal, falloff = m.get('optimalRangeMeters', 0), m.get('falloffRangeMeters', 0)
                xs.update(optimal + falloff * i / 4 for i in range(13))
                if m.get('applicationKind') == 'Missile' or falloff == 0:
                    xs.update([optimal, math.nextafter(optimal, math.inf)])
        elif key == 'angular':
            xs.update(x * n / 8 for x in angular for n in range(1, 25))
        else:
            xs.update(radius)
        points = []
        for x in sorted(v for v in xs if low <= v <= high):
            data = calculate_scenario(report, dict(target, **{key: x}), types)
            y = None if data['unresolvedWeapons'] else sum(r['appliedDpsWithoutReload'] for r in data['weapons'])
            points.append([x, y])
        series.append({'key': key, 'label': label, 'unit': unit, 'min': low, 'max': high,
                       'currentX': target[key], 'currentY': view['attack']['total'], 'points': points})
    return {'status': 'ready', 'series': series, 'target': target,
            'targetName': report['scenarioLinks']['targetFitId']['name'],
            'yMax': max(1, *(y for s in series for _, y in s['points'] if y is not None))}


# Explicit illustrative class references, not the signature of every hull in a class.
IDEAL_SIGNATURES = {1: ('S / 护卫舰', 40), 2: ('M / 巡洋舰', 125),
                    3: ('L / 战列舰', 400), 4: ('XL / 无畏舰', 3000)}


def build_ideal_curves(report, types):
    rows = report['scenarioAnalysis']['weapons']
    weapons = []
    for row in rows:
        m = report['snapshot']['modules'][row['moduleIndex']]
        attrs = types.get(m.get('dogmaTypeId'), {}).get('attrs', {})
        size = attrs.get('128', attrs.get(128))
        reference = IDEAL_SIGNATURES.get(size)
        if m.get('applicationKind') not in ('Turret', 'Missile'):
            return {'status': 'unavailable', 'reason': '部分武器尚无应用模型，无法生成完整理想曲线。'}
        if reference is None:
            return {'status': 'unavailable', 'reason': '部分武器尺寸未识别，无法选择参考目标级别。'}
        weapons.append((m, row, reference))
    drone = report['attributes'].get('appliedDroneDamagePerSecond', 0)
    if drone > 0:
        return {'status': 'unavailable', 'reason': '无人机应用曲线尚未接入，无法生成完整总 DPS 曲线。'}
    references = sorted({(label, radius) for _, _, (label, radius) in weapons}, key=lambda r:r[1])
    ranges = [m.get('optimalRangeMeters', 0)+3*m.get('falloffRangeMeters', 0) for m,_,_ in weapons]
    angular = [m.get('trackingSpeed',0)*radius/(m.get('signatureResolutionMeters',0) or 40000)
               for m,_,(_,radius) in weapons if m['applicationKind']=='Turret']
    radii = [m.get('missileExplosionRadiusMeters',0) for m,_,_ in weapons]
    series = []
    definitions = [('distance','距离','km',0,min(1e7,max([1000]+[r*1.15 for r in ranges]))),
                   ('signature','目标信号半径','m',.1,min(1e7,max([400]+[r*2 for _,r in references]+[r*2 for r in radii]))),
                   ('angular','角速度','rad/s',0,min(100,max([.02]+[v*3 for v in angular])))]
    for key,label,unit,low,high in definitions:
        xs = {low+(high-low)*i/96 for i in range(97)}
        if key=='distance':
            for m,_,_ in weapons:
                optimal,falloff=m.get('optimalRangeMeters',0),m.get('falloffRangeMeters',0)
                xs.update(optimal+falloff*i/4 for i in range(13))
                if m['applicationKind']=='Missile' or not falloff:xs.update([optimal,math.nextafter(optimal,math.inf)])
        elif key=='angular':xs.update(v*i/8 for v in angular for i in range(25))
        else:xs.update(radii);xs.update(r for _,r in references)
        points=[]
        for x in sorted(v for v in xs if low<=v<=high):
            total=0
            for m,row,(_,reference) in weapons:
                if m['applicationKind']=='Turret':
                    factor=turret_factor(x if key=='distance' else 0,x if key=='angular' else 0,
                                        x if key=='signature' else reference,
                                        m.get('optimalRangeMeters',0),m.get('falloffRangeMeters',0),
                                        m.get('trackingSpeed',0),m.get('signatureResolutionMeters',0) or 40000)
                else:
                    radius=m.get('missileExplosionRadiusMeters',0)
                    factor=missile_factor(x if key=='signature' else max(reference,radius),0,radius,
                                          m.get('missileExplosionVelocityMetersPerSecond',0),m.get('missileDamageReductionFactor',0))
                    if key=='distance' and x>m.get('optimalRangeMeters',0):factor=0
                if factor is None:
                    return {'status':'unavailable','reason':'武器应用参数不完整，无法生成理想曲线。'}
                total+=row['spooledDps']*factor
            points.append([x,total])
        fixed = {'distance':'零角速度 · 导弹充分应用 · 零抗性',
                 'signature':'最佳距离 · 目标静止 · 零抗性',
                 'angular':'最佳距离 · 导弹目标静止 · 零抗性'}[key]
        if key=='angular' and references:fixed+=' · '+ ' / '.join(label+' '+str(radius)+' m' for label,radius in references)
        series.append({'key':key,'label':label,'unit':unit,'min':low,'max':high,
                       'currentX':None,'currentY':None,'points':points,'fixed':fixed})
    return {'status':'ready','ideal':True,'series':series,'references':references,
            'yMax':max(1,*(y for s in series for _,y in s['points']))}
