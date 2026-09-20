"""Name discovery and compact views of public attribute metadata/readouts."""


def metadata_rows(agent,type_id,locale):
    data=agent.native('catalog_type_details',{'typeId':type_id});rows=[]
    for row in data['attributes']:
        value=row['value'];meta=row['metadata'];definition=meta['definition'];unit=meta.get('unit') or {}
        rows.append({'attributeId':value['attributeId'],'name':value['name'],'label':definition.get('displayName',{}).get(locale,definition.get('displayName',{}).get('en',value['name'])),
                     'value':value['value'],'unitId':value.get('unitId'),'unit':unit.get('displayName',{}).get(locale,unit.get('name')),
                     'aliases':[value['name'],str(value['attributeId']),*definition.get('displayName',{}).values()]})
    return data['source'],rows


def resolve(rows,names):
    selected=[];matches=[]
    for name in names:
        if not isinstance(name,str) or not name.strip():raise ValueError('Attribute names must be nonempty strings')
        query=name.casefold();exact=[r for r in rows if any(query==a.casefold() for a in r['aliases'])]
        found=exact or [r for r in rows if any(query in a.casefold() for a in r['aliases'])]
        matches.append({'query':name,'state':'resolved' if len(found)==1 else 'ambiguous' if found else 'not_found','candidates':[{k:r[k] for k in ('attributeId','name','label')} for r in found]})
        if len(found)==1 and found[0] not in selected:selected.append(found[0])
    return selected,matches


def item(agent,args):
    if 'names' in args and (not isinstance(args['names'],list) or not 1<=len(args['names'])<=30):raise ValueError('Provide 1..30 attribute names')
    locale=args.get('locale','zh');source,rows=metadata_rows(agent,args['typeId'],locale)
    detail=agent.native('catalog_item',{'typeId':args['typeId']})
    common={'cpu','power','cpuOutput','powerOutput','capacity','speed','duration','hp','shieldCapacity','armorHP','capacitorCapacity','maxVelocity','signatureRadius','hiSlots','medSlots','lowSlots','rigSlots','turretSlotsLeft','launcherSlotsLeft','upgradeCapacity','mass','agility','scanResolution','maxTargetRange'}
    selected,matches=resolve(rows,args['names']) if 'names' in args else ([r for r in rows if r['name'] in common],[])
    traits=[{k:v for k,v in t.items() if k!='text'}|{'text':t.get('text',{}).get(locale,t.get('text',{}).get('en'))} for t in detail.get('traits',[])]
    return agent.bounded({'typeId':args['typeId'],'names':{k:v for k,v in detail['item']['names'].items() if k in ('en',locale)},
        'source':source,'scope':'unfitted source values, not skilled/fitted attributes','traits':traits,
        'moduleConfiguration':(detail.get('capabilities') or {}).get('moduleConfiguration'),
        'matches':matches,'attributeCount':len(rows),'selection':'requested names' if 'names' in args else 'common fitting attributes; request other names explicitly','attributes':[{k:v for k,v in r.items() if k!='aliases'} for r in selected],
        'next':'For fitted values use fitlab_fit attributes(sessionId, names, itemId). For ordinary fitting totals use fitlab_fit read.'})


def fitted(agent,args):
    snap=agent.native('fit_inspect',{'sessionId':args['sessionId']});fit=snap['session']['working'];object_id=args.get('itemId','ship')
    if object_id=='ship':type_id=fit['shipTypeId']
    else:
        prefix,sep,ident=object_id.partition('.')
        entries={'module':('items','typeId'),'charge':('items','chargeTypeId'),'drone':('drones','typeId'),'fighter':('fighters','typeId'),'subsystem':('subsystems','typeId')}
        if prefix not in entries or not sep:raise ValueError('Use ship, module.<instanceId>, charge.<instanceId>, drone.<instanceId>, fighter.<instanceId>, subsystem.<instanceId>')
        field,key=entries[prefix];row=next((r for r in fit.get(field,[]) if r['id']==ident),None)
        if row is None or row.get(key) is None:raise ValueError('Fitted item not found: '+object_id)
        type_id=row[key]
    _,rows=metadata_rows(agent,type_id,args.get('locale','zh'));selected,matches=resolve(rows,args['names'])
    if not selected:return {'sessionId':args['sessionId'],'revision':snap['session']['revision'],'matches':matches,'attributes':[]}
    result=agent.native('fit_attributes',{'fit':fit,'queries':[{'itemId':object_id,'attributeId':r['attributeId']} for r in selected]})
    attributes=[]
    for read in result['items']:
        trace=read.get('trace');row=next(r for r in selected if r['attributeId']==read['query']['attributeId'])
        attributes.append({k:row[k] for k in ('attributeId','name','label','unitId','unit')}|{'value':trace.get('value') if trace else None,'state':read['state'],'reason':read.get('reason')})
    return {'sessionId':args['sessionId'],'revision':snap['session']['revision'],'fitHash':result['fitHash'],'itemId':object_id,'matches':matches,
            'attributes':attributes,'staticCoverageComplete':result['staticCoverageComplete'],'errors':result['errors'],'fullResultId':agent.store(result)}
