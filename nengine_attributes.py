"""Bounded public attribute queries; no fitted-attribute evaluation in the host."""
from nengine_adapter import native_fit,validate_source_binding
from nengine_background import background_bridge as bridge


def inspect_attributes(fit,item_id,attribute_ids):
    if not isinstance(item_id,str) or not 1<=len(item_id)<=160:
        raise ValueError('属性对象标识无效')
    if not isinstance(attribute_ids,list) or not 1<=len(attribute_ids)<=1024 or any(type(i) is not int or i<=0 for i in attribute_ids):
        raise ValueError('属性查询必须包含1–1024个正整数ID')
    if len(set(attribute_ids))!=len(attribute_ids):raise ValueError('属性查询重复')
    client=bridge();status=client.discover()
    validate_source_binding(fit,client)
    native=native_fit(fit,status['source']['source']['buildNumber'])
    merged=None;requests=[]
    for offset in range(0,len(attribute_ids),256):
        query=[{'itemId':item_id,'attributeId':i,'allowDeclaredDefault':False} for i in attribute_ids[offset:offset+256]]
        request={'fit':native,'queries':query}
        result=client.call('fit_attributes',request)['result'];requests.append(request)
        if merged is None:merged={**result,'items':list(result['items']),'traces':dict(result['traces'])}
        else:
            if any(result[k]!=merged[k] for k in ('source','fitHash','staticCoverageComplete','errors','warnings','coverage')):
                raise ValueError('属性分批查询来源不一致，请重新读取')
            merged['items'].extend(result['items']);merged['traces'].update(result['traces'])
    return {'inspection':merged,'requests':requests}
