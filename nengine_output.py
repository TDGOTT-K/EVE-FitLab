"""Display selection only. NEngine computes every metric and the final reducer."""

METRICS = ('nominalCycleDps', 'loadedCycleDps')


def selected_ids(fit, output):
    rows = {}
    for location in ('tubes', 'reserve'):
        for index, row in enumerate((fit.get('fighterLoadout') or {}).get(location, [])):
            if not row: continue
            for field in ('excludedAbilities', 'includedSecondaryAbilities'):
                values = row.get(field, [])
                if not isinstance(values, list) or len(values) > 16 or any(type(v) is not int for v in values):
                    raise ValueError('舰载机武器选择无效')
            rows[row.get('id') or f'fighter-{location}-{index}'] = row
    selected = []
    for item in output['items']:
        source, kind = item['source'], item['kind']
        if source.get('deployed') is False: continue
        if kind.startswith('fighter_'):
            row = rows.get(source.get('squadronId'), {})
            ability = source.get('officialAbilityId')
            if kind in ('fighter_primary','fighter_missile_primary'):
                if ability in row.get('excludedAbilities', []): continue
            elif ability not in row.get('includedSecondaryAbilities', []): continue
        selected.append(item['id'])
    return selected


def grouped_reading(items, metric):
    """Contract-permitted addition; unlike legacy totals, null never becomes zero."""
    groups, exclusions = {}, []
    for item in items:
        reading = item['metrics'].get(metric, {})
        if reading.get('state') != 'available' or reading.get('value') is None or not reading.get('aggregationKey'):
            exclusions.append({'contributionId': item['id'], 'state': reading.get('state'), 'reason': reading.get('reason')})
            continue
        key = (reading['unit'], reading['aggregationKey'])
        group = groups.setdefault(key, {'unit': key[0], 'aggregationKey': key[1], 'subtotal': 0, 'contributionIds': []})
        group['subtotal'] += reading['value']
        group['contributionIds'].append(item['id'])
    values = list(groups.values())
    complete = not exclusions and len(values) == 1
    return {'total': values[0]['subtotal'] if complete else None,
            'groups': values, 'exclusions': exclusions, 'completeSelection': complete}
