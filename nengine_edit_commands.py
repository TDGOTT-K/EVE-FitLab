"""Translate complete native input snapshots into existing public edit commands.

No fitting arithmetic or gameplay eligibility checks. All changes form one engine
transaction; inventory and module edits must never be committed separately.
"""
from copy import deepcopy
from nengine_adapter import native_fit

DEFAULTS={'schemaVersion':1,'name':None,'tags':[],'tacticalModeTypeId':None,
          'skills':{},'items':[],'subsystems':[],'drones':[],'fighters':[],
          'implants':[],'boosters':[],'inventory':None}
IMMUTABLE={'schemaVersion','id','buildNumber','omittedSkills'}
FIELDS=set(DEFAULTS)|IMMUTABLE|{'shipTypeId'}

def commands_between(before,after):
    if not isinstance(before,dict) or not isinstance(after,dict):raise ValueError('需要完整的原生装配输入')
    unknown=(set(before)|set(after))-FIELDS
    if unknown:raise ValueError('尚未映射的原生编辑字段：'+', '.join(sorted(unknown)))
    before={**deepcopy(DEFAULTS),**deepcopy(before)};after={**deepcopy(DEFAULTS),**deepcopy(after)}
    if any(before.get(k)!=after.get(k) for k in IMMUTABLE):raise ValueError('装配身份、数据源和技能省略策略不能通过编辑改变')
    commands=[]
    for field,kind in [('shipTypeId','setShip'),('tacticalModeTypeId','setTacticalMode'),
                       ('name','setName'),('tags','setTags'),('skills','setSkills'),
                       ('subsystems','setSubsystems'),('drones','setDrones'),
                       ('fighters','setFighters'),('implants','setImplants'),('boosters','setBoosters')]:
        if before[field]!=after[field]:
            if field=='name' and after[field] is None:raise ValueError('装配名称不能为空值')
            commands.append({'kind':kind,field:after[field]})
    old=before['items'];new=after['items']
    if not isinstance(old,list) or not isinstance(new,list):raise ValueError('装备列表必须为数组')
    def identities(rows):
        result=[row.get('id') if isinstance(row,dict) else None for row in rows]
        if any(not isinstance(i,str) or not i for i in result) or len(set(result))!=len(result):raise ValueError('装备实例身份必须唯一且非空')
        return result
    old_ids=identities(old);new_ids=identities(new)
    # Preserve exact request ordering (and consequently its hash), including swaps.
    remaining=[i for i in old_ids if i in new_ids]
    added=[i for i in new_ids if i not in old_ids]
    if remaining+added!=new_ids:
        commands.extend({'kind':'remove','instanceId':i} for i in old_ids)
        commands.extend({'kind':'install','item':item} for item in new)
    else:
        commands.extend({'kind':'remove','instanceId':i} for i in old_ids if i not in new_ids)
        by_id={item['id']:item for item in old}
        for item in new:
            if item['id'] not in by_id:commands.append({'kind':'install','item':item})
            elif item!=by_id[item['id']]:commands.append({'kind':'replace','instanceId':item['id'],'item':item})
    if before['inventory']!=after['inventory']:
        commands.append({'kind':'clearInventory'} if after['inventory'] is None else {'kind':'setInventory','inventory':after['inventory']})
    if len(commands)>128:raise ValueError('编辑超出引擎单次128条命令上限，未拆分或部分提交')
    return commands

def prepare_ui_edit(before,after,build):
    original=native_fit(before,build);candidate=native_fit(after,build)
    return {'fit':original,'candidate':candidate,'commands':commands_between(original,candidate)}
