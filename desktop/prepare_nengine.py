"""Stage an immutable engine COPY for Windows packaging; never build/edit its source."""
import argparse
import os
import json
from pathlib import Path
import shutil
import subprocess
from datetime import datetime,timezone

ROOT=Path(__file__).resolve().parent.parent
parser=argparse.ArgumentParser()
parser.add_argument('--engine',default=os.environ.get('FITLAB_NENGINE_ROOT', str(ROOT.parent/'NEngine')))
args=parser.parse_args()
source=Path(args.engine).resolve()
manifest=source/('UI-LOCAL-BASELINE.json' if (source/'UI-LOCAL-BASELINE.json').is_file() else 'UI-BASELINE.json')
baseline=json.loads(manifest.read_text(encoding='utf-8-sig'))
if not baseline.get('independentClone'): raise SystemExit('Expected an independent UI engine copy')
build=ROOT/'desktop/build'; target=build/'nengine';web=build/'web'
for directory in (web,target):
    if directory.exists() and any(directory.iterdir()):raise SystemExit('Use a fresh staging directory: '+str(directory))
web.mkdir(parents=True,exist_ok=True);target.mkdir(parents=True,exist_ok=True)
for pattern in ['*.js','*.css','*.html']:
    for p in ROOT.glob(pattern): shutil.copy2(p,web/p.name)
shutil.copy2(ROOT/'index.html',web/'index.html')
for name in ['data','assets','locales']:shutil.copytree(ROOT/name,web/name,dirs_exist_ok=True)
(web/'app-version.json').write_text(json.dumps({'version':json.loads((ROOT/'package.json').read_text(encoding='utf-8'))['version']}),encoding='utf-8')
shutil.copy2(source/'UI-BASELINE.json',target/'UI-BASELINE.json')
if manifest.name!='UI-BASELINE.json':shutil.copy2(manifest,target/manifest.name)
for relative in ['src/NEngine.Mcp/bin/Debug/net10.0','src/NEngine.Cli/bin/Debug/net10.0',baseline['dataDirectory'],'contracts','.tools/dotnet/host','.tools/dotnet/shared']:
    shutil.copytree(source/relative,target/relative,dirs_exist_ok=True)
shutil.copy2(source/'.tools/dotnet/dotnet.exe',target/'.tools/dotnet/dotnet.exe')
print('Staged pinned NEngine '+baseline['engineVersion']+' without changing engine source.')

def revision(path):
    frozen=path/'SOURCE-REVISION.txt'
    return frozen.read_text(encoding='utf-8').strip() if frozen.exists() else subprocess.check_output(['git','-C',str(path),'rev-parse','HEAD'],text=True).strip()
release={'version':json.loads((ROOT/'package.json').read_text(encoding='utf-8'))['version'],
 'channel':'stable','uiCommit':revision(ROOT),'engineCommit':revision(source),
 'engineVersion':baseline['engineVersion'],'contractDirectory':baseline['contractDirectory'],
 'contractHashes':baseline['contractHashes'],'staticRule':baseline['staticRule'],
 'buildNumber':baseline['buildNumber'],'indexSha256':baseline['indexSha256'],
 'createdAt':datetime.now(timezone.utc).isoformat(),'containsUserData':False}
(build/'release-manifest.json').write_text(json.dumps(release,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
