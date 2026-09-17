"""Local abyssal instance drafts, separate from fitting and engine inputs."""
import uuid

def save_instance(body,library,types,stamp):
    records=library.get('abyssalInstances',[])
    previous=next((x for x in records if x['id']==body.get('id')),None)
    if body.get('id') and not previous:raise ValueError('深渊实例已删除')
    if previous and body.get('revision')!=previous['revision']:raise ValueError('实例已在其他窗口修改，请重新打开')
    if type(body.get('baseTypeId')) is not int:raise ValueError('原装备类型无效')
    base=types.get(body.get('baseTypeId'))
    groups={t['group'] for t in types.values() if 'Abyssal' in t.get('en','') and t.get('kind') in ['high','mid','low']}
    if not base or base.get('kind') not in ['high','mid','low'] or base['group'] not in groups or 'Abyssal' in base.get('en',''):raise ValueError('原装备不在当前深渊装备目录中')
    if previous and previous['baseTypeId']!=base['id']:raise ValueError('不能更换实例的原装备')
    name,notes=body.get('name'),body.get('notes','')
    if not isinstance(name,str) or not 1<=len(name.strip())<=100:raise ValueError('名称须为 1–100 字')
    if not isinstance(notes,str) or len(notes)>1000:raise ValueError('备注不能超过 1000 字')
    record={'id':previous['id'] if previous else str(uuid.uuid4()),'baseTypeId':base['id'],'name':name.strip(),'notes':notes.strip(),'status':'draft','revision':previous['revision']+1 if previous else 1,'updatedAt':stamp}
    library['abyssalInstances']=[x for x in records if x['id']!=record['id']]+[record]
    return dict(record)
