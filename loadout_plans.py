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
    if not valid_folder(folder):
        raise ValueError('分组路径无效')
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
    if 'pilot' in body:
        from nengine_booster_plan import native_plan
        native_plan(body)
        pilot=body['pilot']
        if not isinstance(pilot.get('name'),str) or len(pilot['name'])>100:raise ValueError('角色名称无效')
        contents['pilot']={'name':pilot['name'],'skills':copy.deepcopy(pilot['skills'])}
    if body.get('rollReceipt') is not None:
        from nengine_booster_plan import verify_receipt
        contents['rollReceipt']=verify_receipt(body['rollReceipt'])['receipt']
    plan = dict(contents, id=previous['id'] if previous else str(uuid.uuid4()),
                name=name.strip(), folder=folder.strip(), revision=(previous['revision'] if previous else 0)+1,
                updatedAt=timestamp)
    library['loadoutPlans'] = [p for p in plans if p['id'] != plan['id']] + [copy.deepcopy(plan)]
    return plan

def valid_folder(path):
    return isinstance(path, str) and len(path) <= 1000 and (not path or (
        len(path.split('/')) <= 16 and all(part.strip() == part and 0 < len(part) <= 40 for part in path.split('/'))))


def save_layout(body, library, timestamp):
    """Atomically save ordering/nesting and moved plan metadata, with optimistic locking."""
    previous = library.get('loadoutLayout', {'revision': 0})
    if body.get('revision') != previous.get('revision', 0):
        raise ValueError('方案库布局已在其他窗口修改，请重新打开后再操作')
    folders, order, placements = body.get('folders'), body.get('order'), body.get('plans')
    if not isinstance(folders, list) or len(folders) > 1000 or any(not f or not valid_folder(f) for f in folders) or len(set(folders)) != len(folders):
        raise ValueError('分组结构无效')
    folder_set = set(folders)
    if any('/' in f and f.rsplit('/', 1)[0] not in folder_set for f in folders):
        raise ValueError('父分组不存在')
    current = {p['id']: p for p in library.get('loadoutPlans', [])}
    if not isinstance(placements, list) or any(not isinstance(p, dict) or not isinstance(p.get('id'), str) for p in placements):
        raise ValueError('方案位置无效')
    if len(placements) != len(current) or {p['id'] for p in placements} != set(current):
        raise ValueError('方案库内容已变化，请重新打开后再操作')
    updated = []
    for item in placements:
        old = current[item['id']]
        if item.get('revision') != old.get('revision'):
            raise ValueError('方案已在其他窗口修改，请重新打开后再操作')
        folder = item.get('folder', '')
        if not valid_folder(folder) or (folder and folder not in folder_set):
            raise ValueError('目标分组不存在')
        plan = copy.deepcopy(old)
        if folder != old.get('folder', ''):
            plan.update(folder=folder, revision=old['revision']+1, updatedAt=timestamp)
        updated.append(plan)
    keys = {'f:'+f for f in folders} | {'p:'+id for id in current}
    if not isinstance(order, list) or any(not isinstance(k, str) or k not in keys for k in order) or len(set(order)) != len(order) or set(order) != keys:
        raise ValueError('排序数据无效')
    layout = {'revision': previous.get('revision', 0)+1, 'folders': folders[:], 'order': order[:]}
    library['loadoutPlans'] = updated
    library['loadoutLayout'] = layout
    return {'plans': updated, 'layout': layout}
