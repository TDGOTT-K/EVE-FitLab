"""Stage an immutable engine COPY for Windows packaging; never build/edit its source."""
import argparse
import json
from pathlib import Path
import shutil

ROOT=Path(__file__).resolve().parent.parent
parser=argparse.ArgumentParser()
parser.add_argument('--engine',default=str(ROOT.parent/'N号引擎-UI接入'))
args=parser.parse_args()
source=Path(args.engine).resolve()
baseline=json.loads((source/'UI-BASELINE.json').read_text(encoding='utf-8-sig'))
if not baseline.get('independentClone'): raise SystemExit('Expected an independent UI engine copy')
build=ROOT/'desktop/build'; target=build/'nengine';web=build/'web'
web.mkdir(parents=True,exist_ok=True);target.mkdir(parents=True,exist_ok=True)
for pattern in ['*.js','*.css']:
    for p in ROOT.glob(pattern): shutil.copy2(p,web/p.name)
shutil.copy2(ROOT/'index.html',web/'index.html')
for name in ['data','assets','locales']:shutil.copytree(ROOT/name,web/name,dirs_exist_ok=True)
(web/'app-version.json').write_text(json.dumps({'version':json.loads((ROOT/'package.json').read_text(encoding='utf-8'))['version']}),encoding='utf-8')
shutil.copy2(source/'UI-BASELINE.json',target/'UI-BASELINE.json')
for relative in ['src/NEngine.Mcp/bin/Debug/net10.0',baseline['dataDirectory'],'.tools/dotnet/host','.tools/dotnet/shared']:
    shutil.copytree(source/relative,target/relative,dirs_exist_ok=True)
shutil.copy2(source/'.tools/dotnet/dotnet.exe',target/'.tools/dotnet/dotnet.exe')
print('Staged pinned NEngine '+baseline['engineVersion']+' without changing engine source.')
