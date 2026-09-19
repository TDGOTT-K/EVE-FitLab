"""Project the complete pinned fitting catalog; legacy data supplies navigation only."""
import json
from functools import lru_cache
from nengine_bridge import DEFAULT_ROOT
import os
from pathlib import Path

@lru_cache(maxsize=1)
def index_metadata():
    root=Path(os.environ.get('FITLAB_NENGINE_ROOT',DEFAULT_ROOT)).resolve()
    manifest=root/'UI-LOCAL-BASELINE.json'
    if not manifest.exists():manifest=root/'UI-BASELINE.json'
    baseline=json.loads(manifest.read_text(encoding='utf-8-sig'))
    if not baseline.get('independentClone'): raise ValueError('必须使用独立 UI 引擎副本')
    index=root/baseline['dataDirectory']
    def read(name):
        with (index/(name+'.jsonl')).open(encoding='utf-8') as stream:
            return {r['_key']:r for r in map(json.loads,stream)}
    data={name:read(name) for name in ['types','groups','categories','metaGroups','typeDogma','dogmaAttributes','dogmaEffects','dogmaUnits','dynamicItemAttributes']}
    manifest=json.loads((index/'manifest.json').read_text(encoding='utf-8-sig'))
    if manifest['indexSha256']!=baseline['indexSha256']:raise ValueError('目录索引与锁定引擎来源不一致')
    data['source']={'buildNumber':manifest['source']['buildNumber'],'indexSha256':manifest['indexSha256'],'archiveSha256':manifest['source']['sha256'],'sourceUrl':manifest['source']['sourceUrl'],'releaseDate':manifest['source']['releaseDate'],'scope':'source_metadata_not_gameplay_coverage','navigation':'legacy-market-tree-or-pinned-category-group'}
    return data

@lru_cache(maxsize=None)
def activation_capabilities(type_id):
    data=index_metadata()
    references=data['typeDogma'].get(type_id,{}).get('dogmaEffects',[])
    defaults=[r for r in references if r.get('isDefault')]
    default=defaults[0] if len(defaults)==1 else None
    effect=data['dogmaEffects'].get(default['effectID'],{}) if default else {}
    active=bool(default and effect.get('effectCategoryID') in (1,2,3))
    return {'canActivate':active,'canOverload':active and any(data['dogmaEffects'].get(r['effectID'],{}).get('effectCategoryID')==5 for r in references),
        'activationEffectId':default['effectID'] if active else None,
        'activationSource':'unique_default_effect_category_not_online_effect'}


def effective_module_state(slot):
    capability=native_type_capabilities(slot.get('item'))
    active=((capability or {}).get('moduleConfiguration') or {}).get('states',{}).get('active',{}).get('state')
    capability={'canActivate':active in ('available','requires_fit_validation')}
    state=slot.get('state') or ('Offline' if slot.get('online') is False else 'Active' if capability['canActivate'] else 'Online')
    if state not in ('Offline','Online','Active','Overload'):raise ValueError('无效装备状态')
    # Old FitLab catalogs treated non-default online effect 16 as activation.
    return state

@lru_cache(maxsize=4096)
def native_type_capabilities(type_id):
    from nengine_adapter import bridge
    return bridge().call('catalog_item',{'typeId':type_id})['result'].get('capabilities')


def catalog_kind(category,effects):
    if category in {6,8,16,18,32}:return {6:'ship',8:'ammo',16:'skill',18:'drone',32:'subsystem'}[category]
    if category==7:
        return next((kind for effect,kind in [(2663,'rig'),(12,'high'),(13,'mid'),(11,'low')] if effect in effects),None)
    return None


def refresh_catalog(catalog):
    data=index_metadata();result=[];legacy={t['id']:t for t in catalog}
    # New types share the same functional branch as existing types in their SDE
    # group. Meta level (including officer) is a leaf filter, not a root category.
    from collections import Counter,defaultdict
    paths=defaultdict(Counter)
    for item in catalog:
        if item.get('path') and item['path'][0] in ('舰船装备','军火和弹药','舰船和装备改装件','无人机'):
            paths[item.get('group')][tuple(item['path'])]+=1
    for ident,t in sorted(data['types'].items()):
        if not t.get('published'):continue
        group=data['groups'].get(t['groupID'],{});category=group.get('categoryID')
        dogma=data['typeDogma'].get(ident,{})
        effects=[x['effectID'] for x in dogma.get('dogmaEffects',[])]
        kind=catalog_kind(category,effects)
        if kind is None:continue
        old=legacy.get(ident,{})
        attrs={str(x['attributeID']):x['value'] for x in dogma.get('dogmaAttributes',[])}
        for key,field in [('4','mass'),('161','volume'),('38','capacity'),('162','radius')]:
            if field in t:attrs.setdefault(key,t[field])
        name=lambda row:row.get('name',{}).get('zh') or row.get('name',{}).get('en') or str(row.get('_key','未知'))
        # No marketGroups table is shipped in the pinned index. Retain historical
        # navigation for existing IDs, explicitly label its separate provenance.
        path=old.get('path') or [name(data['categories'].get(category,{})),name(group)]
        if kind in ('high','mid','low','rig','ammo','drone') and (not old.get('path') or path[0] in ('装备','弹药','未列入市场')):
            root={'rig':'舰船和装备改装件','ammo':'军火和弹药','drone':'无人机'}.get(kind,'舰船装备')
            path=list(paths[t['groupID']].most_common(1)[0][0]) if paths[t['groupID']] else [root,name(group)]
        meta=data['metaGroups'].get(t.get('metaGroupID'),{})
        result.append({'id':ident,'kind':kind,'attrs':attrs,'effects':effects,'group':t['groupID'],
            'category':category,'published':True,'name':name(t),'en':t['name'].get('en',name(t)),
            'names':t['name'],'volume':t.get('volume'),'capacity':t.get('capacity'),
            'meta':name(meta) if meta else None,'metaGroupId':t.get('metaGroupID'),
            'path':path,'marketGroupId':t.get('marketGroupID'),
            'navigationSource':'fitlab-functional-group-navigation-v1',
            'metadataSource':{'typeId':ident,'table':'types','dogmaTable':'typeDogma','buildNumber':data['source']['buildNumber'],'indexSha256':data['source']['indexSha256']},
            **activation_capabilities(ident)})
    return result


def item_metadata(item):
    data=index_metadata()
    return {'source':{**data['source'],'typeId':item['id'],'tables':['types','typeDogma','dogmaAttributes','dogmaEffects','dogmaUnits']},
        'attributes':[dict(data['dogmaAttributes'].get(int(k),{}),id=int(k),value=v) for k,v in item['attrs'].items()],
        'units':{str(k):v for k,v in data['dogmaUnits'].items()},
        'effects':[data['dogmaEffects'].get(i,{}) for i in item['effects']],
        'description':data['types'][item['id']].get('description',{})}

def indexed_item(type_id):
    data=index_metadata()
    if type_id not in data['types']: return None
    dogma=data['typeDogma'].get(type_id,{})
    return {'id':type_id,'attrs':{str(x['attributeID']):x['value'] for x in dogma.get('dogmaAttributes',[])},
        'effects':[x['effectID'] for x in dogma.get('dogmaEffects',[])]}
