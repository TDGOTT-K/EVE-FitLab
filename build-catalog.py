import json,shutil
from pathlib import Path
root=Path(r'D:/IT/EVE/EdenOS/resource/sde/eve-online-static-data-3248221-jsonl')
def read(name):return [json.loads(l) for l in (root/(name+'.jsonl')).open(encoding='utf-8')]
groups={t['_key']:t for t in read('groups')};markets={t['_key']:t for t in read('marketGroups')};dogma={t['_key']:t for t in read('typeDogma')};out=[];icons={};metas={m['_key']:m['name'].get('zh',m['name']['en']) for m in read('metaGroups')}
def marketpath(i):
 p=[]
 while i in markets:
  g=markets[i];p.insert(0,g);i=g.get('parentGroupID')
 return p
effect_categories={e['_key']:e.get('effectCategoryID') for e in read('dogmaEffects')}
for t in read('types'):
 if not t.get('published'):continue
 category=groups.get(t['groupID'],{}).get('categoryID');d=dogma.get(t['_key'],{});effects=[e['effectID'] for e in d.get('dogmaEffects',[])];attrs={str(x['attributeID']):x['value'] for x in d.get('dogmaAttributes',[])}
 kind='drone' if category==18 else 'subsystem' if category==32 else 'ship' if category==6 else 'ammo' if category==8 else 'rig' if 2663 in effects else 'high' if 12 in effects else 'mid' if 13 in effects else 'low' if 11 in effects else 'skill' if category==16 else None
 if not kind:continue
 if kind in ['high','mid','low','rig'] and category!=7:continue
 path=marketpath(t.get('marketGroupID'));names=[x['name'].get('zh',x['name']['en']) for x in path]
 defaults=[e for e in d.get('dogmaEffects',[]) if e.get('isDefault')]
 active=len(defaults)==1 and effect_categories.get(defaults[0]['effectID']) in [1,2,3]
 out.append(dict(id=t['_key'],name=t['name'].get('zh',t['name']['en']),en=t['name']['en'],group=t['groupID'],kind=kind,meta=metas.get(t.get('metaGroupID',1),'科技 I'),path=names or ['未列入市场'],capacity=t.get('capacity'),volume=t.get('volume'),attrs=attrs,effects=effects,canActivate=active,canOverload=active and any(effect_categories.get(e)==5 for e in effects)))
 for i,g in enumerate(path):
  icon=g.get('iconID');src=Path(r'D:/IT/EVE/EdenOsRewrite/src/hosts/web/EdenOS.Hosts.Web/wwwroot/assets/market-icons')/f'{icon}.png'
  if src.exists():
   dst=Path('assets')/f'market-{icon}.png'
   if not dst.exists():shutil.copyfile(src,dst)
   icons['/'+'/'.join(names[:i+1])]=f'assets/market-{icon}.png'
Path('data/full-catalog.json').write_text(json.dumps(out,ensure_ascii=False,separators=(',',':')),encoding='utf-8');Path('data/market-icons.json').write_text(json.dumps(icons,ensure_ascii=False),encoding='utf-8')
print({kind:sum(t['kind']==kind for t in out) for kind in set(t['kind'] for t in out)})
