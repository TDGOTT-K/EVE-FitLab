"""Read display metadata from the pinned index; retain FitLab's navigation taxonomy."""
import json
from functools import lru_cache
from nengine_bridge import DEFAULT_ROOT
import os
from pathlib import Path

@lru_cache(maxsize=1)
def index_metadata():
    root=Path(os.environ.get('FITLAB_NENGINE_ROOT',DEFAULT_ROOT)).resolve()
    baseline=json.loads((root/'UI-BASELINE.json').read_text(encoding='utf-8-sig'))
    if not baseline.get('independentClone'): raise ValueError('必须使用独立 UI 引擎副本')
    index=root/baseline['dataDirectory']
    def read(name):
        with (index/(name+'.jsonl')).open(encoding='utf-8') as stream:
            return {r['_key']:r for r in map(json.loads,stream)}
    return {name:read(name) for name in ['types','typeDogma','dogmaAttributes','dogmaEffects','dogmaUnits']}

def refresh_catalog(catalog):
    data=index_metadata();result=[]
    for old in catalog:
        t=data['types'].get(old['id'])
        if t is None: continue
        dogma=data['typeDogma'].get(old['id'],{})
        attrs={str(x['attributeID']):x['value'] for x in dogma.get('dogmaAttributes',[])}
        for key,field in [('4','mass'),('161','volume'),('38','capacity'),('162','radius')]:
            if field in t: attrs.setdefault(key,t[field])
        effects=[x['effectID'] for x in dogma.get('dogmaEffects',[])]
        result.append({**old,'attrs':attrs,'effects':effects,'group':t['groupID'],
            'name':t['name'].get('zh',t['name'].get('en',old['name'])),
            'en':t['name'].get('en',old['en']),'volume':t.get('volume',old.get('volume')),
            'canActivate':any(data['dogmaEffects'].get(e,{}).get('effectCategoryID') in (1,2,3) for e in effects),
            'canOverload':any(data['dogmaEffects'].get(e,{}).get('effectCategoryID')==5 for e in effects)})
    return result

def item_metadata(item):
    data=index_metadata()
    return {'attributes':[dict(data['dogmaAttributes'].get(int(k),{}),id=int(k),value=v) for k,v in item['attrs'].items()],
        'units':{str(k):v for k,v in data['dogmaUnits'].items()},
        'effects':[data['dogmaEffects'].get(i,{}) for i in item['effects']],
        'description':data['types'][item['id']].get('description',{})}
