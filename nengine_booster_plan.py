"""Independent pilot plans: map UI inputs, leave probability/RNG to NEngine."""
import secrets
from nengine_adapter import bridge


def native_plan(body, roll=False):
    client=bridge();status=client.discover()
    pilot=body.get('pilot') or {'name':'无技能','skills':[]}
    skills=pilot.get('skills',[])
    if not isinstance(skills,list) or len(skills)>1000:raise ValueError('角色技能快照无效')
    mapped={}
    for skill in skills:
        id,level=skill.get('skillTypeId'),skill.get('level')
        if type(id) is not int or id<=0 or type(level) is not int or not 0<=level<=5 or str(id) in mapped:
            raise ValueError('角色技能快照无效或重复')
        mapped[str(id)]=level
    result={'schemaVersion':1,'buildNumber':status['source']['source']['buildNumber'],
            'omittedSkills':'untrained','skills':mapped,'implants':[],'boosters':[]}
    for kind in ('implants','boosters'):
        entries=body.get(kind,[])
        if not isinstance(entries,list) or len(entries)>32:raise ValueError('独立方案每类最多32个条目')
        for entry in entries:
            if type(entry.get('slot')) is not int or type(entry.get('typeId')) is not int:raise ValueError('方案条目无效')
            if roll and kind=='boosters' and body.get('rollTypeId') and entry['typeId']!=body['rollTypeId']:continue
            item={'id':kind+'-'+str(entry['slot']),'typeId':entry['typeId']}
            if kind=='boosters':item['enabledSideEffects']=[] if roll else entry.get('enabledSideEffects',[])
            result[kind].append(item)
    return result


def analyze_plan(body):
    plan=native_plan(body)
    return {'nativePlan':plan,'analysis':bridge().call('booster_plan_analyze',{'plan':plan})['result']}


def roll_plan(body):
    plan=native_plan(body,True)
    seed=body.get('seed',secrets.randbits(53))
    if type(seed) is not int or not 0<=seed<=9007199254740991:raise ValueError('种子必须是可无损保存的非负安全整数')
    return bridge().call('booster_plan_roll',{'plan':plan,'seed':seed})['result']


def verify_receipt(receipt):
    if not isinstance(receipt,dict) or type(receipt.get('seed')) is not int or not 0<=receipt['seed']<=9007199254740991:
        raise ValueError('随机回执或种子无效')
    bridge().discover()
    return bridge().call('booster_plan_verify',{'receipt':receipt})['result']
