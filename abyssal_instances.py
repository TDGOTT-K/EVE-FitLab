"""Local abyssal instance drafts, separate from fitting and engine inputs."""
import uuid, copy, math

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
    mock=body.get('uiMock')
    if mock is not None:
        if not isinstance(mock,dict) or mock.get('version')!=1 or type(mock.get('tier')) is not int or mock['tier'] not in (0,1,2) or type(mock.get('roll')) is not int or not 1<=mock['roll']<=1000000:raise ValueError('演示状态无效')
        rows=mock.get('attributes')
        if not isinstance(rows,list) or not 1<=len(rows)<=16:raise ValueError('演示属性无效')
        for row in rows:
            if not isinstance(row,dict) or type(row.get('id')) is not int or type(row.get('highIsGood')) is not bool:raise ValueError('演示属性无效')
            if any(type(row.get(k)) not in (int,float) or not math.isfinite(row[k]) for k in ('base','value','min','max')):raise ValueError('演示数值无效')
            if any(not isinstance(row.get(k),str) or len(row[k])>80 for k in ('label','unit')):raise ValueError('演示属性文字无效')
        record['uiMock']=copy.deepcopy(mock)
        record['status']='mock'
    library['abyssalInstances']=[x for x in records if x['id']!=record['id']]+[record]
    return dict(record)
