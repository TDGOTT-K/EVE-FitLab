"""Validate translation structure and reject untranslated Western-language entries."""
import json,re,sys
from pathlib import Path
if hasattr(sys.stdout,'reconfigure'):sys.stdout.reconfigure(encoding='utf-8')
ROOT=Path(__file__).resolve().parents[1]
errors=[]
def read(path):
 def unique(pairs):
  result={}
  for key,value in pairs:
   if key in result:errors.append(f'{path.name}: duplicate key {key}')
   result[key]=value
  return result
 try:
  data=json.loads(path.read_text(encoding='utf-8-sig'),object_pairs_hook=unique)
  if not isinstance(data,dict):raise ValueError('resource must be an object')
  return data
 except (ValueError,OSError) as error:errors.append(f'{path.name}: {error}');return {}
source=read(ROOT/'locales/source.json')
for key in source:
 if not re.fullmatch(r'[a-z]+(?:\.[A-Za-z0-9]+)+',key):errors.append(f'Non-semantic resource key: {key}')
aliases=read(ROOT/'locales/legacy-aliases.json')
for alias,key in aliases.items():
 if key not in source:errors.append(f'Invalid legacy alias target: {key}')
parameters=lambda value:sorted(re.findall(r'\{([A-Za-z_]\w*)\}',value))
for lang in ['en','ja','zh-TW','de','ru','fr']:
 data=read(ROOT/f'locales/{lang}.json')
 for key in source.keys()-data.keys():errors.append(f'{lang}: missing {key}')
 for key in data.keys()-source.keys():errors.append(f'{lang}: extra {key}')
 for key,value in data.items():
  if not isinstance(value,str) or not value.strip():errors.append(f'{lang}: empty/non-string {key}');continue
  if key in source and parameters(value)!=parameters(source[key]):errors.append(f'{lang}: parameters {key}')
  if lang in ['en','de','ru','fr'] and re.search(r'[\u3400-\u9fff]',value) and source.get(key) not in ['深海的鱼','繁體中文','简体中文','日本語']:
   errors.append(f'{lang}: untranslated CJK in {key}: {value}')
 print(f'{lang}: {len(data)} entries')
if errors:
 print('\n'.join(errors[:60]));print(f'{len(errors)} total failures');sys.exit(1)
print('Keys, duplicate keys, named parameters, empty translations and Western-language CJK checks passed')
