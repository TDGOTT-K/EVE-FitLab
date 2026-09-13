import argparse,shutil,json
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--engine',required=True);p.add_argument('--sde',required=True);args=p.parse_args()
root=Path(__file__).resolve().parent.parent;build=root/'desktop/build';web=build/'web';sde=build/'sde'
web.mkdir(parents=True,exist_ok=True);sde.mkdir(parents=True,exist_ok=True)
(web/'app-version.json').write_text(json.dumps({'version':json.loads((root/'package.json').read_text(encoding='utf-8'))['version']}),encoding='utf-8')
for name in ['index.html','style.css']+[f.name for f in root.glob('*.js')]:shutil.copy2(root/name,web/name)
(web/'data').mkdir(exist_ok=True)
for name in ['full-catalog.json','item-metadata.json','market-icons.json','locale-game.json']:shutil.copy2(root/'data'/name,web/'data'/name)
shutil.copytree(root/'locales',web/'locales',dirs_exist_ok=True)
shutil.copytree(root/'assets',web/'assets',dirs_exist_ok=True)
for name in ['types.jsonl','groups.jsonl','typeDogma.jsonl','dogmaAttributes.jsonl','dogmaEffects.jsonl','categories.jsonl','_sde.jsonl']:shutil.copy2(Path(args.sde)/name,sde/name)
rules=json.loads((Path(args.engine)/'data/static-data/fitting-combat/dogma-rules.local.json').read_text(encoding='utf-8-sig'))
if 'source' in rules:
 for k in list(rules['source']):
  if 'path' in k or 'root' in k:rules['source'][k]=None
(sde/'dogma-rules.json').write_text(json.dumps(rules,ensure_ascii=False),encoding='utf-8')
# Only static, explicitly selected files are staged; no state, accounts, or developer credentials.
print('Prepared static application resources.')
