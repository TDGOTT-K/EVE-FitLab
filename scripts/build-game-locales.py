"""Build display metadata from the same pinned, independent SDE as the UI API.

FITLAB_NENGINE_ROOT chooses the authorized engine copy. No engine files change.
"""
import json,sys,os,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from nengine_catalog import index_metadata,refresh_catalog
metadata=index_metadata()
catalog=refresh_catalog(json.loads((ROOT/'data/full-catalog.json').read_text(encoding='utf8')))
ids={row['id'] for row in catalog}|set(metadata['dynamicItemAttributes'])
ids.update(key for key,row in metadata['types'].items() if row.get('published') and metadata['groups'].get(row.get('groupID'),{}).get('categoryID') in [20,87])
terms={};names={};entities={}
for table in ['types','groups','categories','metaGroups','dogmaAttributes','dogmaEffects','dogmaUnits']:
 entities[table]={}
 for row in metadata[table].values():
  if table=='types' and row['_key'] not in ids:continue
  for field in ['name','displayName']:
   values=row.get(field)
   if not isinstance(values,dict):continue
   entities[table][str(row['_key'])]=values
   if table=='types':names[str(row['_key'])]=values
   if values.get('zh'):terms[values['zh']]={lang:values.get(lang) or values.get('en') or values['zh'] for lang in ['en','ja','de','ru','fr']}
result={'source':metadata['source'],'traditionalChinese':{'source':'OpenCC conversion of official zh with community UI glossary','official':False},'terms':terms,'names':names,'entities':{k:v for k,v in entities.items() if k!='types'}}
navigation_root=os.environ.get('FITLAB_NAVIGATION_SDE')
if navigation_root:
 navigation_file=Path(navigation_root)/'marketGroups.jsonl'
 rows={row['_key']:row for row in map(json.loads,navigation_file.read_text(encoding='utf8').splitlines())}
 result['entities']['marketGroups']={str(key):row['name'] for key,row in rows.items()}
 result['navigationPaths']={}
 for key,row in rows.items():
  path_labels=[];current=row;seen=set()
  while current and current['_key'] not in seen:
   seen.add(current['_key']);name=current['name'];path_labels.insert(0,name.get('zh') or name.get('en'));current=rows.get(current.get('parentGroupID'))
  result['navigationPaths']['/'+'/'.join(path_labels)]=key
  values=row['name']
  if values.get('zh'):result['terms'][values['zh']]={lang:values.get(lang) or values.get('en') or values['zh'] for lang in ['en','ja','de','ru','fr']}
 result['navigationSource']={'dataset':Path(navigation_root).name,'table':'marketGroups','sha256':hashlib.sha256(navigation_file.read_bytes()).hexdigest(),'scope':'legacy UI navigation only; not current engine game rules'}
else:
 previous=json.loads((ROOT/'data/locale-game.json').read_text(encoding='utf8'))
 if not previous.get('navigationSource'):raise SystemExit('Set FITLAB_NAVIGATION_SDE to the official SDE used for the legacy market paths')
 for key in ['navigationPaths','navigationSource']:result[key]=previous[key]
 result['entities']['marketGroups']=previous['entities']['marketGroups']
 for values in result['entities']['marketGroups'].values():
  if values.get('zh'):result['terms'][values['zh']]={lang:values.get(lang) or values.get('en') or values['zh'] for lang in ['en','ja','de','ru','fr']}
(ROOT/'data/locale-game.json').write_text(json.dumps(result,ensure_ascii=False,separators=(',',':')),encoding='utf8')
print(len(names),'type names;',len(terms),'exact legacy display terms')
