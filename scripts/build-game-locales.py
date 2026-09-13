import json
from pathlib import Path
root=Path('D:/IT/EVE/EdenOS/resource/sde/eve-online-static-data-3248221-jsonl')
catalog=json.loads(Path('data/full-catalog.json').read_text(encoding='utf-8'));ids={t['id'] for t in catalog};terms={};names={}
for filename in ['types','marketGroups','groups','metaGroups','dogmaAttributes']:
 for line in (root/(filename+'.jsonl')).open(encoding='utf-8'):
  row=json.loads(line)
  if filename=='types' and row['_key'] not in ids:continue
  for field in ['name','displayName']:
   n=row.get(field)
   if isinstance(n,dict) and n.get('zh'):
    terms[n['zh']]={'en':n.get('en',n['zh']),'ja':n.get('ja',n.get('en',n['zh']))}
    if filename=='types':names[str(row['_key'])]=n
Path('data/locale-game.json').write_text(json.dumps({'terms':terms,'names':names},ensure_ascii=False,separators=(',',':')),encoding='utf-8')
print(len(terms),len(names))
