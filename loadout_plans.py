"""Local reusable UI plans; no legacy-engine effect calculations."""
import copy
import uuid


def save_plan(body, library, timestamp):
    plans = library.setdefault('loadoutPlans', [])
    previous = next((p for p in plans if p['id'] == body.get('id')), None)
    if body.get('id') and previous is None:
        raise ValueError('方案已删除，请重新打开')
    if previous and body.get('revision') != previous['revision']:
        raise ValueError('方案已在其他窗口修改，请重新加载后再保存')
    name, folder = body.get('name'), body.get('folder', '')
    if not isinstance(name, str) or not 1 <= len(name.strip()) <= 80:
        raise ValueError('请输入 1～80 字的方案名称')
    if not isinstance(folder, str) or len(folder) > 40:
        raise ValueError('分组名称不能超过 40 字')
    contents = {}
    for key, limit in [('implants', 10), ('boosters', 100)]:
        rows = body.get(key, [])
        if not isinstance(rows, list) or len(rows) > limit:
            raise ValueError('方案条目数量无效')
        clean, occupied = [], set()
        for row in rows:
            if not isinstance(row, dict) or type(row.get('typeId')) is not int or row['typeId'] <= 0:
                raise ValueError('物品类型无效')
            slot = row.get('slot')
            if type(slot) is not int or not 1 <= slot <= (10 if key == 'implants' else 1000) or slot in occupied:
                raise ValueError('槽位无效或重复')
            occupied.add(slot)
            item = {'typeId': row['typeId'], 'slot': slot}
            if key == 'boosters':
                effects = row.get('enabledSideEffects', [])
                if not isinstance(effects, list) or len(effects) > 32 or any(type(e) is not int or e <= 0 for e in effects) or len(effects) != len(set(effects)):
                    raise ValueError('副作用选择无效')
                item['enabledSideEffects'] = list(effects)
            clean.append(item)
        contents[key] = clean
    plan = dict(contents, id=previous['id'] if previous else str(uuid.uuid4()),
                name=name.strip(), folder=folder.strip(), revision=(previous['revision'] if previous else 0)+1,
                updatedAt=timestamp)
    library['loadoutPlans'] = [p for p in plans if p['id'] != plan['id']] + [copy.deepcopy(plan)]
    return plan
