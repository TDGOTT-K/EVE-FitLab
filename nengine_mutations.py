"""Pinned mutation rules and reproducible generation receipts; no local RNG."""
import copy
from nengine_adapter import bridge
from nengine_catalog import index_metadata


def options(types):
    data = index_metadata()
    result = {}
    for plasmid, row in data['dynamicItemAttributes'].items():
        info = data['types'].get(plasmid, {})
        if not info.get('published'): continue
        name = info.get('name', {})
        for mapping in row.get('inputOutputMapping', []):
            for base in mapping['applicableTypes']:
                if types.get(base, {}).get('kind') not in ('high', 'mid', 'low'): continue
                if mapping['resultingType'] not in types: continue
                result.setdefault(str(base), []).append({
                    'id': plasmid, 'name': name.get('zh', name.get('en', str(plasmid))),
                    'resultTypeId': mapping['resultingType']})
    return result


def generate(body, types, roll=False):
    base, plasmid = body.get('baseTypeId'), body.get('mutaplasmidTypeId')
    if type(base) is not int or type(plasmid) is not int:
        raise ValueError('请选择原装备和突变质体')
    if not any(x['id'] == plasmid for x in options(types).get(str(base), [])):
        raise ValueError('该突变质体不适用于当前原装备')
    args = {'baseTypeId': base, 'mutaplasmidTypeId': plasmid}
    if roll and 'seed' in body: args['seed'] = body['seed']
    engine = bridge()
    engine.discover()
    result = engine.call('mutation_roll' if roll else 'mutation_rule', args)['result']
    rule = result['rule'] if roll else result
    data = index_metadata()
    # Metadata is display-only: the engine receipt remains byte-for-byte semantically intact.
    metadata = {}
    for attr in rule['attributes']:
        info = data['dogmaAttributes'].get(attr['attributeId'], {})
        label = info.get('displayName', {})
        unit = data['dogmaUnits'].get(attr.get('unitId'), {}).get('displayName', {})
        metadata[str(attr['attributeId'])] = {
            'label': label.get('zh', label.get('en', attr['name'])) if isinstance(label, dict) else label,
            'unit': unit.get('zh', unit.get('en', '')) if isinstance(unit, dict) else unit,
            'highIsGood': info.get('highIsGood')}
    return {'data': result, 'metadata': metadata}


def verify_receipt(receipt, base, types):
    if not isinstance(receipt, dict) or not isinstance(receipt.get('rule'), dict):
        raise ValueError('缺少引擎变异凭据，请重新生成')
    if receipt['rule'].get('baseTypeId') != base or not isinstance(receipt.get('seed'), str):
        raise ValueError('变异凭据与原装备不符')
    canonical = generate({'baseTypeId': base,
        'mutaplasmidTypeId': receipt['rule'].get('mutaplasmidTypeId'),
        'seed': receipt['seed']}, types, roll=True)['data']
    comparable=copy.deepcopy(canonical)
    if 'comparisonSummary' not in receipt:comparable.pop('comparisonSummary',None)
    if isinstance(receipt.get('rolls'),list):
        for current,historical in zip(comparable['rolls'],receipt['rolls']):
            if isinstance(historical,dict) and 'comparison' not in historical:current.pop('comparison',None)
    if comparable != receipt:
        raise ValueError('变异结果与当前引擎不一致，请重新生成')
    return copy.deepcopy(receipt)


def review_receipt(body,types):
    receipt=verify_receipt(body.get('generationReceipt'),body.get('baseTypeId'),types)
    return generate({'baseTypeId':body['baseTypeId'],'mutaplasmidTypeId':receipt['rule']['mutaplasmidTypeId'],'seed':receipt['seed']},types,roll=True)


def workbench(body,types):
    base,plasmid=body.get('baseTypeId'),body.get('mutaplasmidTypeId')
    if not any(x['id']==plasmid for x in options(types).get(str(base),[])):
        raise ValueError('突变道具不适用于当前装备')
    engine=bridge();engine.discover()
    return engine.call('mutation_workbench',{'request':body})['result']


def verify_edit(receipt,base,types):
    if not isinstance(receipt,dict) or receipt.get('rule',{}).get('baseTypeId')!=base:
        raise ValueError('编辑凭据与原装备不符')
    rule=receipt['rule']
    canonical=workbench({'operation':'edit','baseTypeId':base,'mutaplasmidTypeId':rule['mutaplasmidTypeId'],
        'values':receipt.get('mutation',{}).get('attributes')},types)['edit']
    if canonical!=receipt:raise ValueError('编辑凭据与当前引擎不一致，请重新应用编辑')
    return copy.deepcopy(canonical)
