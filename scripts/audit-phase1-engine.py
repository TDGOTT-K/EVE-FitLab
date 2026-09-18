import json,tempfile,sys,subprocess
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from nengine_bridge import NEngineBridge,NEngineError
from nengine_catalog import refresh_catalog
out=Path('output/phase1-engine-audit-r41');out.mkdir(parents=True,exist_ok=True)
catalog=refresh_catalog([]); byname={t['en']:t for t in catalog}; summaries=[]
cases=[('Bastion Module I','Paladin'),('Siege Module I','Revelation'),('Capital Industrial Core I','Rorqual'),('Shield Command Burst I','Rorqual'),('Miner I','Rifter'),('Strip Miner I','Retriever'),('Prototype Cloaking Device I','Rifter'),('Large Micro Jump Drive','Raven'),('Triage Module I','Apostle'),('Entosis Link I','Rifter'),('Data Analyzer I','Rifter'),('Relic Analyzer I','Rifter'),('Core Probe Launcher I','Rifter'),('Festival Launcher','Rifter'),('Clone Vat Bay I','Rorqual'),('Salvager I','Rifter'),('Warp Disruption Field Generator I','Devoter')]
with tempfile.TemporaryDirectory() as directory:
 b=NEngineBridge(state=directory);status=b.discover()
 assert status['engineVersion']=='0.190.8-ui.1' and b.baseline['revision']==41,'This audit is pinned to r41; create a new baseline before reusing it.'
 (out/'status.json').write_text(json.dumps(status,ensure_ascii=False,indent=2),encoding='utf8')
 try:
  for name,hull in cases:
   for active in (False,True):
    t=byname[name]; ident=str(t['id'])+('-active' if active else '-online')
    fit={'id':'audit','name':'phase1-audit','buildNumber':b.baseline['buildNumber'],'shipTypeId':byname[hull]['id'],'omittedSkills':'untrained','skills':{str(x['id']):5 for x in catalog if x['kind']=='skill'},'items':[{'id':'module-1','typeId':t['id'],'slotIndex':0,'online':True,'active':active,'overheated':False}]}
    request={'fit':fit};(out/(ident+'-request.json')).write_text(json.dumps(request,indent=2),encoding='utf8')
    try:
     response=b.call('fit_analyze',request);a=response['result'];row={'case':ident,'name':name,'hull':hull,'active':active,'coverage':a['staticCoverageComplete'],'unsupported':[x for x in a['coverage'] if x['status']=='unsupported_static'],'errors':[x['code'] for x in a['errors']],'resources':len(a['resources']),'attributes':len(a['attributes'])}
    except NEngineError as e:response=e.payload;row={'case':ident,'name':name,'hull':hull,'active':active,'error':e.error}
    (out/(ident+'-response.json')).write_text(json.dumps(response,ensure_ascii=False,indent=2),encoding='utf8');summaries.append(row)
    print(json.dumps(row,ensure_ascii=False),flush=True)
 finally:b.close()
(out/'summary.json').write_text(json.dumps(summaries,ensure_ascii=False,indent=2),encoding='utf8')
# Verify CLI transport parity independently from the UI adapter.
cli_checks=[]
for row in summaries:
 if not row['active']:continue
 ident=row['case'];request=json.loads((out/(ident+'-request.json')).read_text(encoding='utf8'))
 fitfile=out/(ident+'-fit.json');fitfile.write_text(json.dumps(request['fit'],indent=2),encoding='utf8')
 command=[str(b.root/'.tools/dotnet/dotnet.exe'),str(b.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),'sde-fit','--data',str(b.root/b.baseline['dataDirectory']),'--fit',str(fitfile)]
 run=subprocess.run(command,capture_output=True,text=True,encoding='utf8',timeout=60)
 payload=json.loads(run.stderr if run.returncode else run.stdout)
 mcp=json.loads((out/(ident+'-response.json')).read_text(encoding='utf8'))
 same=payload.get('error')==mcp.get('error') if run.returncode else payload==mcp.get('result')
 (out/(ident+'-cli.json')).write_text(json.dumps({'exitCode':run.returncode,'payload':payload,'sameAsMcp':same},ensure_ascii=False,indent=2),encoding='utf8')
 cli_checks.append({'case':ident,'exitCode':run.returncode,'sameAsMcp':same})
(out/'cli-summary.json').write_text(json.dumps(cli_checks,indent=2),encoding='utf8')
assert all(x['sameAsMcp'] for x in cli_checks),cli_checks
print('CLI parity:',len(cli_checks),'passed')
# Structural diagnostics must survive an unrelated unsupported activation.
slot_checks=[]
with tempfile.TemporaryDirectory() as directory:
 client=NEngineBridge(state=directory)
 try:
  for state in ('online','active'):
   ident='483-'+state+'-slot7';request=json.loads((out/('483-'+state+'-request.json')).read_text(encoding='utf8'));request['fit']['items'][0]['slotIndex']=7
   response=client.call('fit_analyze',request)
   (out/(ident+'-request.json')).write_text(json.dumps(request,indent=2),encoding='utf8')
   (out/(ident+'-response.json')).write_text(json.dumps(response,ensure_ascii=False,indent=2),encoding='utf8')
   fitfile=out/(ident+'-fit.json');fitfile.write_text(json.dumps(request['fit'],indent=2),encoding='utf8')
   run=subprocess.run([str(client.root/'.tools/dotnet/dotnet.exe'),str(client.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),'sde-fit','--data',str(client.root/client.baseline['dataDirectory']),'--fit',str(fitfile)],capture_output=True,text=True,encoding='utf8',timeout=60)
   payload=json.loads(run.stdout);same=run.returncode==0 and payload==response['result']
   (out/(ident+'-cli.json')).write_text(json.dumps({'exitCode':run.returncode,'payload':payload,'sameAsMcp':same},ensure_ascii=False,indent=2),encoding='utf8')
   slot_checks.append({'case':ident,'errors':[e['code'] for e in payload['errors']],'resources':len(payload['resources']),'sameAsMcp':same})
 finally:client.close()
(out/'slot-summary.json').write_text(json.dumps(slot_checks,indent=2),encoding='utf8')
assert all(x['sameAsMcp'] for x in slot_checks)
print('Structural diagnostics:',slot_checks)
