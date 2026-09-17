"""Static DPS sweeps through the same application calculator as the workspace."""
import math
from fitting_scenario import calculate_scenario


def build_dps_curves(report, types):
    view = report['workspace']
    if not view['target']:
        return {'status': 'unavailable', 'reason': '在情景设置中选择目标后查看应用 DPS 曲线。'}
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
