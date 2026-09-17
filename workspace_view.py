"""One display context and a neutral baseline from the same evaluated fitting.

Do not mutate the nominal Dogma attributes or their source graphs. Applications
have separate fields, so missing target mechanics never masquerade as zero.
"""
from capacitor import calculate_capacitor
from sustained_tank import sustained_tank
from fitting_scenario import calculate_scenario, DAMAGE


def attach_workspace_view(report, fit, types):
    value = fit.get('scenario') or {}
    active = bool(fit.get('activeScenarioId')) if 'scenarios' in fit else bool(value)
    selected = next((s for s in fit.get('scenarios', []) if s['id'] == fit.get('activeScenarioId')), {})
    name = selected.get('name', '原有情景') if active else '不应用情景'
    target = active and bool(report.get('scenarioLinks', {}).get('targetFitId'))
    current = report['scenarioAnalysis']
    neutral = calculate_scenario(report, {}, types) if active else current
    drone = report['attributes'].get('appliedDroneDamagePerSecond', 0)

    def attack(data, applied):
        rows = data['weapons']
        complete = not applied or data['unresolvedWeapons'] == 0
        weapon = sum(r['appliedDpsWithoutReload'] for r in rows) if applied and complete else data['baseWeaponDps'] if not applied else None
        reload = data['appliedWeaponDps'] if applied and complete else data['reloadWeaponDps'] if not applied else None
        drone_value = None if applied and drone > 0 else drone
        profile = {k: sum(r['appliedProfile'][k] for r in rows) for k in DAMAGE} if applied and complete and drone_value == 0 else None
        if not applied:
            profile = report['attributes'].get('appliedDamageProfilePerSecond', {})
        return {
            'weapon': weapon, 'drone': drone_value,
            'total': weapon + drone_value if weapon is not None and drone_value is not None else None,
            'reload': reload + drone_value if reload is not None and drone_value is not None else None,
            'volley': sum(r['appliedVolley'] for r in rows) if applied and complete else report['attributes'].get('volleyDamage', 0) if not applied else None,
            'profile': profile,
        }

    baseline_attack = attack(neutral, False)
    attack_view = attack(current, True) if target else baseline_attack.copy()
    conditions = [['情景', name]]
    if target:
        t = current['target']
        conditions += [
            ['目标', report['scenarioLinks']['targetFitId']['name']],
            ['距离', f"{t['distance'] / 1000:.3f} km"],
            ['相对速度', f"{t['speed']:.2f} m/s"],
            ['角速度', f"{t['angular']:.5f} rad/s"],
            ['目标信号半径', f"{t['signature']:.2f} m"],
            ['目标防御层', {'shield': '护盾', 'armor': '装甲', 'structure': '结构'}.get(t.get('targetLayer'), '护盾')],
            ['模型', '固定几何、命中期望与目标抗性；DPS 不含换弹，持续输出另列'],
            ['速度口径', '当前为相对速度近似；导弹尚未使用独立的目标绝对速度'],
        ]
    issues = []
    if target and current['unresolvedWeapons']:
        issues.append('部分武器缺少应用模型，汇总值暂不可完整计算')
    if target and drone > 0:
        issues.append('无人机对目标的应用模型尚未接入，总 DPS 暂不可完整计算')
    if active and any(k in report.get('scenarioLinks', {}) for k in ['supportFitId', 'hostileFitId']):
        conditions += [[label, report['scenarioLinks'][key]['name']] for key, label in [('supportFitId', '传电来源'), ('hostileFitId', '毁电来源')] if key in report['scenarioLinks']]
        conditions.append(['外部电容', '按来源持续运转估算，尚未联算来源缺电停机'])
    if target:
        by_index = {r['moduleIndex']: r for r in current['weapons']}
        for i, module in enumerate(report['snapshot']['modules']):
            r = by_index.get(i)
            if r is None:
                continue
            module['scenarioMetrics'] = {
                'damagePerSecond': {'value': r['appliedDpsWithoutReload'], 'baseline': r['baseDps']},
                'volleyDamage': {'value': r['appliedVolley'], 'baseline': r['baseVolley']},
                'conditions': conditions,
                'profile': r['appliedProfile'],
                'baselineProfile': r['baseProfile'],
            }
            for key in DAMAGE:
                module['scenarioMetrics']['damageProfilePerSecond.' + key] = {'value': r['appliedProfile'][key] if r['appliedProfile'] else None, 'baseline': r['baseProfile'][key]}
    report['workspace'] = {
        'active': active, 'name': name, 'target': target, 'conditions': conditions,
        'attack': attack_view, 'issues': issues,
        'baseline': {
            'attack': baseline_attack,
            'capacitorAnalysis': calculate_capacitor(report, types, {}) if active else report['capacitorAnalysis'],
            'sustainedTank': sustained_tank(report, types, {}) if active else report.get('sustainedTank'),
        },
    }
