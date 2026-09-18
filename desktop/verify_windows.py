"""Smoke-test a packaged Windows app and its bundled public CLI in isolated state."""
import argparse,hashlib,json,os,subprocess
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('--app',required=True,help='win-unpacked directory')
parser.add_argument('--output',required=True,help='new isolated test directory')
args=parser.parse_args();app=Path(args.app).resolve();output=Path(args.output).resolve()
output.mkdir(parents=True,exist_ok=False)
engine=app/'resources/nengine'
baseline=json.loads((engine/'UI-LOCAL-BASELINE.json').read_text(encoding='utf-8-sig'))
env=dict(os.environ)
for key in ('FITLAB_NENGINE_ROOT','FITLAB_NENGINE_STATE','FITLAB_WEB_ROOT','FITLAB_STORAGE_CONFIG','FITLAB_DEFAULT_STATE','FITLAB_API_PORT','ELECTRON_RUN_AS_NODE'):env.pop(key,None)
env['FITLAB_TEST_ROOT']=str(output)
subprocess.run([str(app/'EVE FitLab.exe'),'--smoke-test'],env=env,cwd=app,check=True,timeout=180,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
smoke=json.loads((output/'desktop-smoke.json').read_text(encoding='utf-8-sig'))
if smoke.get('error'):raise RuntimeError(smoke['error'])
assert smoke['desktop'] and smoke['undoRedo'] and smoke['artifactModules']
assert smoke['unauthorizedStatus']==403
assert smoke['engineVersion']==baseline['engineVersion']
assert smoke['saved']['contractRevision']==baseline['revision']
assert smoke['saved']['fitHash']==smoke['previewHash']
session=output/'Data/nengine-ui-local-r40'/('session-'+smoke['saved']['id']+'.json')
env['DOTNET_ROOT']=str(engine/'.tools/dotnet')
result=subprocess.run([str(engine/'.tools/dotnet/dotnet.exe'),str(engine/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),
    'eve-export','--data',str(engine/baseline['dataDirectory']),'--session',str(session),'--snapshot','saved','--out',str(output/'cli-export.json')],
    env=env,cwd=output,check=True,capture_output=True,encoding='utf-8',timeout=60,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
document=json.loads(result.stdout);assert document['fitHash']==smoke['previewHash']
summary={'engineVersion':baseline['engineVersion'],'revision':baseline['revision'],'packagedDesktop':True,
 'previewApplySaveUndoRedo':True,'packagedCliExportMatches':True,'unauthorizedStatus':smoke['unauthorizedStatus'],
 'exeSha256':hashlib.sha256((app/'EVE FitLab.exe').read_bytes()).hexdigest(),'smoke':str(output/'desktop-smoke.json')}
(output/'verification.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
print(json.dumps(summary,ensure_ascii=False))
