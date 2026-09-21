"""Compact projections of public output readouts; no damage formulas or guessed totals."""

METRICS = ('nominalCycleDps', 'loadedCycleDps', 'volleyDamage', 'cycleSeconds', 'sustainedDps', 'actualAverageDps')
BASES = {'appliedCycleDps': 'nominalCycleDps', 'effectiveCycleDps': 'nominalCycleDps',
         'appliedLoadedCycleDps': 'loadedCycleDps', 'effectiveLoadedCycleDps': 'loadedCycleDps'}


def available(readout):
    return readout.get('state') == 'available' and readout.get('value') is not None


def compact_readout(value):
    return {k: value.get(k) for k in ('state', 'value', 'unit', 'reason', 'aggregationKey')}


def compact_item(item):
    source = item['source']
    return {'id': item['id'], 'kind': item['kind'], 'status': item['status'], 'reason': item.get('reason'),
            'fitAdmitted': item['fitAdmitted'],
            'source': {k: source.get(k) for k in ('typeId', 'instanceId', 'memberCount', 'online', 'active', 'deployed', 'chargeTypeId')},
            'metrics': {key: compact_readout(item['metrics'][key]) for key in METRICS if key in item['metrics']}}


def totals(analysis):
    # These totals have different scopes and must not be summed by the facade.
    def metric(key, reason, scope):
        value = analysis.get(key)
        return {'value': value, 'unit': 'hp/s', 'state': 'available' if value is not None else 'unavailable',
                'reason': analysis.get(reason) if value is None else None, 'scope': scope}
    return {'weaponAndDroneNominalDps': metric('nominalDps', 'nominalDpsUnavailableReason', 'native nominalDps; excludes fighter primary'),
            'deployedFighterPrimaryNominalDps': metric('fighterPrimaryNominalDps', 'fighterPrimaryNominalDpsUnavailableReason',
                'deployed fighter primary only; excludes rockets and utility abilities'),
            'staticCoverageComplete': analysis.get('staticCoverageComplete'),
            'fitHasErrors': bool(analysis.get('errors')),
            'interpretation': 'Nominal/loaded cycle output is not measured sustained DPS, all-ability DPS or ship equivalence. Null is not zero.'}


def diagnostics(items, ids, metric):
    by_id = {i['id']: i for i in items}
    base = BASES[metric]
    rows = []
    for identity in ids:
        item = by_id.get(identity)
        if item is None:
            rows.append({'id': identity, 'state': 'not_found', 'reason': 'UNKNOWN_CONTRIBUTION_ID', 'availableBaseMetrics': []})
            continue
        readout = item['metrics'].get(base, {})
        rows.append({'id': identity, 'kind': item['kind'], 'fitAdmitted': item['fitAdmitted'],
                     'basisMetric': base, **compact_readout(readout),
                     'availableBaseMetrics': [m for m in ('nominalCycleDps', 'loadedCycleDps') if available(item['metrics'].get(m, {}))]})
    return rows


def recovery_plans(items, ids, metric):
    """Offer explicit changed selections, never silently discard or change the metric."""
    by_id = {i['id']: i for i in items}
    groups = {}
    for identity in ids:
        item = by_id.get(identity)
        if item is None or not item['fitAdmitted']:
            continue
        basis = item['metrics'].get(BASES[metric], {})
        alternative = metric
        if not available(basis):
            alternative = 'effectiveLoadedCycleDps' if metric.startswith('effective') else 'appliedLoadedCycleDps'
            basis = item['metrics'].get('loadedCycleDps', {})
        if available(basis):
            key = (alternative, basis.get('aggregationKey'), basis.get('unit'))
            groups.setdefault(key, []).append(identity)
    return [{'metric': key[0], 'contributionIds': subset, 'excludedContributionIds': [i for i in ids if i not in subset],
             'basisAggregationKey': key[1], 'meaning': 'Loaded-cycle output assumes charges available; not sustained output.' if 'Loaded' in key[0]
             else 'Only this explicit subset; not the original combined total.',
             'validation': 'base readouts available; native curve call still validates target and application'}
            for key, subset in groups.items() if key[0] != metric or subset != ids]
