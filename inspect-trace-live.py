import json,urllib.request
from pathlib import Path
fits=json.load(urllib.request.urlopen('http://127.0.0.1:5207/api/library'));f=next(f for f in fits if f['shipId']==622);r=urllib.request.Request('http://127.0.0.1:5207/api/analyze',data=json.dumps(f).encode(),headers={'Content-Type':'application/json','Origin':'http://127.0.0.1:5207'});d=json.load(urllib.request.urlopen(r));t=d['attributes']['attributeTraces']['maxVelocity'];print(json.dumps(t,ensure_ascii=True));Path('output/trace-live.json').write_text(json.dumps(d),encoding='utf-8')
