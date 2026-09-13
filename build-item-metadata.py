import json
from pathlib import Path
root=Path(r'D:/IT/EVE/EdenOS/resource/sde/eve-online-static-data-3248221-jsonl')
def read(n):return [json.loads(l) for l in (root/(n+'.jsonl')).open(encoding='utf-8')]
ids={t['id'] for t in json.loads(Path('data/full-catalog.json').read_text(encoding='utf-8'))}
data={'attributes':{t['_key']:t for t in read('dogmaAttributes')},'units':{t['_key']:t for t in read('dogmaUnits')},'effects':{t['_key']:t for t in read('dogmaEffects')},'descriptions':{t['_key']:t.get('description',{}) for t in read('types') if t['_key'] in ids}}
Path('data/item-metadata.json').write_text(json.dumps(data,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
