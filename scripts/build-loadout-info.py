"""Bundle static loadout information for the existing item-info UI."""
import json
from pathlib import Path
root=Path(__file__).resolve().parent.parent
base=root/'desktop/build/sde'
def read(name):return {t['_key']:t for t in map(json.loads,(base/(name+'.jsonl')).read_text(encoding='utf-8').splitlines())}
ids=set()
for name in ['implant','booster']:
 s=(root/(name+'-catalog.js')).read_text(encoding='utf-8');ids.update(t['id'] for t in json.loads(s[s.index('['):].rstrip(';\n')))
types=read('types');dogma=read('typeDogma');attrs=read('dogmaAttributes');effects=read('dogmaEffects');meta=json.loads((root/'data/item-metadata.json').read_text(encoding='utf-8'))
items={str(id):{'description':{k:v for k,v in types[id].get('description',{}).items() if k in ['zh','en']},'attributes':dogma[id].get('dogmaAttributes',[]),'effects':[e['effectID'] for e in dogma[id].get('dogmaEffects',[])]} for id in ids}
aids={a['attributeID'] for i in items.values() for a in i['attributes']};eids={e for i in items.values() for e in i['effects']}
def trim(t):return {k:({lang:text for lang,text in v.items() if lang in ['en','zh']} if isinstance(v,dict) else v) for k,v in t.items()}
bundle={'items':items,'attributes':{str(id):trim(attrs[id]) for id in aids},'effects':{str(id):trim(effects[id]) for id in eids},'units':meta['units']}
(root/'loadout-item-info.js').write_text('// Generated from bundled SDE; static base data only.\nconst data='+json.dumps(bundle,ensure_ascii=True,separators=(',',':'))+';\nexport function loadoutItemInfo(id){const t=data.items[id];if(!t)throw Error("物品资料不可用");return {description:t.description,units:data.units,attributes:t.attributes.map(a=>({...data.attributes[a.attributeID],value:a.value})),effects:t.effects.map(id=>data.effects[id])};}\n',encoding='utf-8')
