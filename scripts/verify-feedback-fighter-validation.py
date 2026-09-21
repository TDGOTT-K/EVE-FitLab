"""Hel / Dragonfly II sequential native reads and editor commits in disposable state."""
import argparse,copy,json,os,sys,tempfile,time
from pathlib import Path
root=Path(__file__).resolve().parents[1];sys.path.insert(0,str(root))
p=argparse.ArgumentParser();p.add_argument('--engine',required=True);args=p.parse_args()
os.environ['FITLAB_NENGINE_ROOT']=str(Path(args.engine).resolve())
with tempfile.TemporaryDirectory(prefix='fighter-feedback-') as state:
 os.environ['FITLAB_NENGINE_STATE']=state
 from nengine_adapter import bridge,fighter_catalog
 from nengine_workbench import read,edit
 fit={'id':'hel-feedback','name':'Hel feedback','shipId':22852,'skills':[],'slots':[],
      'fighterLoadout':{'tubes':[None]*5,'reserve':[]}}
 reports=[read(fit,[])];evidence=[];revision=0
 try:
  for index in range(2):
   after=copy.deepcopy(fit)
   after['fighterLoadout']['tubes'][index]={'id':'squadron-'+str(index),'typeId':40557,'quantity':6,'active':True}
   start=time.monotonic();r=read(after,[])
   blocking=[e for e in r['issues'] if e.get('code','').startswith(('FIGHTER_','EVE_FIGHTER')) or e.get('code')=='STATIC_COVERAGE_INCOMPLETE']
   assert not blocking,blocking
   result=edit({'before':fit,'after':after,'sessionId':'feedback-hel','revision':revision,'requestId':'install-'+str(index),
                'operation':'apply','initial':index==0},[])
   revision=result['result']['revision'];fit=after;reports.append(result['report'])
   assert len(result['report']['native']['fighterEntities'])==index+1
   evidence.append({'squadrons':index+1,'revision':revision,'seconds':round(time.monotonic()-start,3),'blockingIssues':blocking})
  directory=root/'output/playwright';directory.mkdir(parents=True,exist_ok=True)
  (directory/'fighter-feedback-data.json').write_text(json.dumps({'reports':reports,'catalog':fighter_catalog()},ensure_ascii=False),encoding='utf8')
  (root/'output/feedback-fighter-validation.json').write_text(json.dumps(evidence,indent=2),encoding='utf8')
  print(json.dumps(evidence))
 finally:bridge().close()
