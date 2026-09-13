import urllib.request,json
from pathlib import Path
def call(name,body):
 req=urllib.request.Request('http://127.0.0.1:5210/api/actions/'+name,data=json.dumps(body).encode(),headers={'Content-Type':'application/json','Origin':'http://127.0.0.1:5210'})
 return json.load(urllib.request.urlopen(req))['data']
s=call('ImportFitText',{'text':'[Rifter, FitLab]\n125mm Gatling AutoCannon I, EMP S','locale':'en'})['snapshot'];d=call('ValidateFit',{'snapshot':s,'simulationMode':'SingleShip'});Path('data/engine-shape.json').write_text(json.dumps(d),encoding='utf-8');print(d['attributes']);
w=json.loads(Path(r'D:/IT/EVE/EdenOsRewrite/artifacts/visual-workbench-validation/workspaces.json').read_text(encoding='utf-8-sig'))['state']['workspaces'];target=next(x for x in w if x['workspace']['workspace_id']=='workspace_c49c0a7aa1704ea086941346a0366418');print(target.keys());print(str(target.get('characters',[]))[:500])
