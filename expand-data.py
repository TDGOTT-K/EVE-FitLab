from pathlib import Path
p=Path('extract.py');s=p.read_text(encoding='utf-8');s=s.replace('import json,re','import json,re,shutil');s=s.replace('Small Shield Booster|','Small Shield Booster|Small Armor Repairer|');s=s.replace("S$'", "S$|^(Small|Medium) (Capacitor Control Circuit|Core Defense Field Extender|Projectile Burst Aerator) (I|II)$'")
s=s.replace("else 'ammo' if", "else 'rig' if 'Circuit' in en or 'Field Extender' in en or 'Burst Aerator' in en else 'ammo' if");s=s.replace("'Control' in en or 'Gyrostabilizer' in en", "'Control' in en or 'Gyrostabilizer' in en or 'Armor Repairer' in en")
s += '''
icon_paths={}
source=Path(r'D:/IT/EVE/EdenOsRewrite/src/hosts/web/EdenOS.Hosts.Web/wwwroot/assets/market-icons')
for g in groups.values():
 parts=path(g['_key']);icon=g.get('iconID');src=source/f'{icon}.png'
 if src.exists() and any(t['path'][:len(parts)]==parts for t in out):
  shutil.copyfile(src,Path('assets')/f'market-{icon}.png');icon_paths['/'+('/'.join(parts))]=f'assets/market-{icon}.png'
Path('data/market-icons.json').write_text(json.dumps(icon_paths,ensure_ascii=False),encoding='utf-8')
'''
p.write_text(s,encoding='utf-8')
