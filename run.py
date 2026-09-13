"""Start the local web UI with the bundled minimal Dogma host."""
import os, subprocess, time, urllib.request, runpy, secrets, shutil
from pathlib import Path
ROOT = Path(__file__).resolve().parent

def main():
 sde = Path(os.environ.get('FITLAB_SDE_ROOT', ROOT / 'sde')).resolve()
 required = ['types.jsonl','groups.jsonl','typeDogma.jsonl','dogmaAttributes.jsonl','dogmaEffects.jsonl','categories.jsonl']
 missing = [name for name in required if not (sde / name).is_file()]
 if missing:
  raise SystemExit('Set FITLAB_SDE_ROOT to the extracted official JSONL SDE directory. Missing: ' + ', '.join(missing))
 rules = sde / 'dogma-rules.json'
 if not rules.exists(): shutil.copy2(ROOT / 'engine/data/static-data/fitting-combat/dogma-rules.local.json', rules)
 key = secrets.token_urlsafe(32)
 env = dict(os.environ, FITLAB_SDE_ROOT=str(sde), FITLAB_ENGINE_STATE=str(ROOT / 'state/engine'), FITLAB_ENGINE_KEY=key)
 subprocess.run(['dotnet','build',str(ROOT / 'desktop/engine/FitLab.Engine.csproj'),'--nologo','-v','quiet'],check=True)
 process = subprocess.Popen(['dotnet',str(ROOT / 'desktop/engine/bin/Debug/net9.0/FitLab.Engine.dll'),'--urls','http://127.0.0.1:5210'],env=env)
 try:
  request = urllib.request.Request('http://127.0.0.1:5210/health',headers={'X-FitLab-Key':key})
  for _ in range(120):
   if process.poll() is not None: raise RuntimeError('Engine exited. Check whether port 5210 is already in use.')
   try:
    with urllib.request.urlopen(request,timeout=1) as response:
     if response.status == 200: break
   except Exception: time.sleep(.5)
  else: raise RuntimeError('Engine startup timeout')
  os.environ.update(FITLAB_ENGINE_KEY=key,FITLAB_ENGINE_URL='http://127.0.0.1:5210',FITLAB_CHARACTER_SOURCE=str(ROOT/'state/external-characters-disabled.json'))
  runpy.run_path(str(ROOT/'server.py'),run_name='__main__')
 finally:
  process.terminate()
  try: process.wait(timeout=10)
  except subprocess.TimeoutExpired: process.kill()

if __name__ == '__main__': main()
