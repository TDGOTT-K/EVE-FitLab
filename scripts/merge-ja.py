import json
from pathlib import Path
out={}
for i in range(4):
 source=json.loads(Path(f'output/locale-batches/source-{i}.json').read_text(encoding='utf-8-sig'));d=json.loads(Path(f'output/locale-batches/ja-{i}.json').read_text(encoding='utf-8-sig'));assert d.keys()==source.keys();assert all(isinstance(v,str) and v.strip() for v in d.values());out.update(d)
Path('locales/ja.json').write_text(json.dumps(out,ensure_ascii=False,indent=2),encoding='utf-8');print(len(out))
