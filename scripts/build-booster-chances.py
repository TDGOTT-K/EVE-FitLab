"""Attach SDE base per-effect chances to the existing booster catalog."""
import json
from pathlib import Path
root=Path(__file__).resolve().parent.parent;base=root/'desktop/build/sde'
def read(name):return {x['_key']:x for x in map(json.loads,(base/(name+'.jsonl')).read_text(encoding='utf-8').splitlines())}
effects=read('dogmaEffects');dogma=read('typeDogma');attrs=read('dogmaAttributes')
p=root/'booster-catalog.js';s=p.read_text(encoding='utf-8');items=json.loads(s[s.index('['):].rstrip(';\n'));missing=[]
for t in items:
 values={a['attributeID']:a['value'] for a in dogma[t['id']].get('dogmaAttributes',[])}
 for e in t['sideEffects']:
  id=effects[e['id']].get('fittingUsageChanceAttributeID');chance=values.get(id,attrs.get(id,{}).get('defaultValue'))
  e['chance']=chance if isinstance(chance,(int,float)) and 0<=chance<=1 else None
  if e['chance'] is None:missing.append((t['id'],e['id']))
p.write_text(s[:s.index('[')]+json.dumps(items,ensure_ascii=True,separators=(',',':'))+';\n',encoding='utf-8')
print('Missing chances:',missing)
