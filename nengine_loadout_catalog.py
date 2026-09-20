"""Current published pilot equipment metadata, with UI-only navigation labels."""
from functools import lru_cache
from html import unescape
import re
from nengine_catalog import index_metadata
from loadout_taxonomy import classify


def local(value):
    return value.get('zh') or value.get('en') or ''


def description_text(value):
    text=re.sub(r'<br\s*/?>','\n',local(value),flags=re.I)
    return unescape(re.sub(r'<[^>]*>','',text)).strip()


@lru_cache(maxsize=1)
def loadout_catalog():
    data=index_metadata();result={'implants':[],'boosters':[],'source':data['source']}
    for ident,t in data['types'].items():
        if not t.get('published'):continue
        dogma=data['typeDogma'].get(ident,{})
        values={a['attributeID']:a['value'] for a in dogma.get('dogmaAttributes',[])}
        group=data['groups'].get(t['groupID'],{})
        if group.get('categoryID')!=20:continue
        if 331 in values:kind,slot='implants',values[331]
        elif 1087 in values:kind,slot='boosters',values[1087]
        else:continue
        if slot!=int(slot) or slot<=0:continue
        item={'id':ident,'name':local(t['name']),'en':t['name'].get('en',''),'names':t['name'],'descriptionNames':t.get('description',{}),
              'slot':int(slot),'group':local(group.get('name',{})),
              'metadataSource':{'buildNumber':data['source']['buildNumber'],'indexSha256':data['source']['indexSha256'],'typeId':ident},
              'navigationSource':'fitlab-benefit-taxonomy-v1'}
        classify(item,kind,data['typeDogma'],data['dogmaAttributes'],data['types'])
        # Keep source wording; never synthesize gameplay bonus values here.
        text=description_text(t.get('description',{}))
        lines=[line.strip() for line in text.splitlines() if line.strip()]
        benefits=[line for line in lines if re.search(r'\d+(?:\.\d+)?\s*[%％]|^(主要效果|次要效果|套件效果|套装效果|植入体套装效果)',line)]
        item['benefitTooltip']='\n'.join(benefits) if benefits else text or '暂无效果介绍；请查看详细信息'
        item['tooltipSource']='types.description'
        if kind=='boosters':
            item['sideEffects']=[]
            for ref in dogma.get('dogmaEffects',[]):
                effect=data['dogmaEffects'].get(ref['effectID'],{})
                chance=effect.get('fittingUsageChanceAttributeID')
                if chance is not None:
                    item['sideEffects'].append({'id':ref['effectID'],'name':local(effect.get('displayName',{})) or effect.get('name',str(ref['effectID'])),
                                               'names':effect.get('displayName',{}),'chanceAttributeId':chance})
        result[kind].append(item)
    return result
