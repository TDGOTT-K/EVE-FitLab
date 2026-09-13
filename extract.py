import json,re,shutil
from pathlib import Path
root=Path(r'D:/IT/EVE/EdenOS/resource/sde/eve-online-static-data-3248221-jsonl')
rows=[json.loads(l) for l in (root/'types.jsonl').open(encoding='utf-8')]
pattern=r'^(125mm Gatling AutoCannon|150mm Light AutoCannon|200mm AutoCannon|200mm Railgun|Small Shield Booster|Small Armor Repairer|1MN Afterburner|Damage Control|Gyrostabilizer) (I|II)$|^(EMP|Fusion|Phased Plasma|Antimatter Charge|Titanium Sabot) S$|^(Small|Medium) (Capacitor Control Circuit|Core Defense Field Extender|Projectile Burst Aerator) (I|II)$'
selected=[t for t in rows if t['_key']==587 or (t.get('published') and re.search(pattern,t['name'].get('en','')))]
ids={t['_key'] for t in selected}
dogma={t['_key']:{str(a['attributeID']):a['value'] for a in t.get('dogmaAttributes',[])} for l in (root/'typeDogma.jsonl').open(encoding='utf-8') if (t:=json.loads(l))['_key'] in ids}
effects={t['_key']:[e['effectID'] for e in t.get('dogmaEffects',[])] for l in (root/'typeDogma.jsonl').open(encoding='utf-8') if (t:=json.loads(l))['_key'] in ids}
groups={t['_key']:t for l in (root/'marketGroups.jsonl').open(encoding='utf-8') if (t:=json.loads(l))}
def path(i):
 p=[]
 while i in groups:
  g=groups[i];p.insert(0,g['name'].get('zh',g['name']['en']));i=g.get('parentGroupID')
 return p
out=[]
for t in selected:
 en=t['name']['en'];kind='ship' if t['_key']==587 else 'rig' if 'Circuit' in en or 'Field Extender' in en or 'Burst Aerator' in en else 'ammo' if en.endswith(' S') else 'mid' if 'Shield Booster' in en or 'Afterburner' in en else 'low' if 'Control' in en or 'Gyrostabilizer' in en or 'Armor Repairer' in en else 'high'
 out.append(dict(id=t['_key'],name=t['name'].get('zh',en),en=en,group=t['groupID'],kind=kind,path=path(t.get('marketGroupID')),capacity=t.get('capacity'),volume=t.get('volume'),attrs=dogma.get(t['_key'],{}),effects=effects.get(t['_key'],[])))
Path('data/catalog.json').write_text(json.dumps(out,ensure_ascii=False),encoding='utf-8')
print([(t['id'],t['name']) for t in out])

icon_paths={}
source=Path(r'D:/IT/EVE/EdenOsRewrite/src/hosts/web/EdenOS.Hosts.Web/wwwroot/assets/market-icons')
for g in groups.values():
 parts=path(g['_key']);icon=g.get('iconID');src=source/f'{icon}.png'
 if src.exists() and any(t['path'][:len(parts)]==parts for t in out):
  shutil.copyfile(src,Path('assets')/f'market-{icon}.png');icon_paths['/'+('/'.join(parts))]=f'assets/market-{icon}.png'
Path('data/market-icons.json').write_text(json.dumps(icon_paths,ensure_ascii=False),encoding='utf-8')
