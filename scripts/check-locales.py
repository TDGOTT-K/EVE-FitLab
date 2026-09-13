import json
from pathlib import Path
source=json.loads(Path('locales/source.json').read_text(encoding='utf-8'))
for lang in ['en','ja','zh-TW']:
 d=json.loads(Path(f'locales/{lang}.json').read_text(encoding='utf-8'));assert set(d)==set(source),lang;assert all(isinstance(v,str) and v for v in d.values()),lang
 print(lang,len(d),'entries OK')
