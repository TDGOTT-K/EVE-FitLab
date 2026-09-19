"""Rebuild the recorded preview through the public engine CLI. Requires a NEW evidence directory."""
import argparse,json,pathlib,subprocess,os,collections,hashlib
parser=argparse.ArgumentParser();parser.add_argument('--engine',required=True);parser.add_argument('--evidence',required=True);parser.add_argument('--sample-seconds',type=int,default=5);args=parser.parse_args()
ui=pathlib.Path(__file__).resolve().parents[1];root=pathlib.Path(args.engine).resolve();p=pathlib.Path(args.evidence).resolve();p.mkdir(parents=True,exist_ok=False)
out=ui/'data/sandbox-preview';request=json.loads((out/'request.json').read_text(encoding='utf-8'));env={**os.environ,'DOTNET_ROOT':str(root/'.tools/dotnet')}
def run(*args):
 subprocess.run([str(root/'.tools/dotnet/dotnet.exe'),str(root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),*map(str,args)],env=env,check=True,stdout=subprocess.DEVNULL)
run('sde-battle','--data',root/'artifacts/sde-mutation-index-002','--input',out/'request.json','--out',p/'assembly.json')
run('sde-battle-verify','--data',root/'artifacts/sde-mutation-index-002','--input',p/'assembly.json','--out',p/'verified.json')
scenario=json.loads((p/'assembly.json').read_text(encoding='utf-8-sig'))['scenario'];(p/'scenario.json').write_text(json.dumps(scenario),encoding='utf-8');(p/'policy.js').write_text((out/'policy.js').read_text(encoding='utf-8'),encoding='utf-8')
duration=request['horizonSeconds'];sample=args.sample_seconds
assert 1<=sample<=10 and duration%sample==0
run('run','--scenario',p/'scenario.json','--policy',p/'policy.js','--ticks',sample,'--out',p/f'frame-{sample:03d}')
for second in range(sample*2,duration+1,sample):run('resume','--checkpoint',p/f'frame-{second-sample:03d}'/'checkpoint.json','--ticks',sample,'--out',p/f'frame-{second:03d}')
frames=[];events=[]
def ship(s,initial=False):
 cap=s['capacitorRecharge'];cap=cap if initial else cap['definition']
 return {'id':s['id'],'position':s['position'],'velocity':s['velocity'],'destroyed':s.get('destroyed',False),'layers':[{k:l[k] for k in ['name','capacity','hitpoints']} for l in s['defense']['layers']],'capacitor':cap['capacity'] if initial else s['capacitor'],'capacitorCapacity':cap['capacity']}
frames.append({'time':0,'ships':[ship(s,True) for s in scenario['ships']]})
for sec in range(sample,duration+1,sample):
 d=p/f'frame-{sec:03d}';cp=json.loads((d/'checkpoint.json').read_text(encoding='utf-8-sig'));w=cp['world'];frames.append({'time':sec,'ships':[ship(s) for s in w['ships']]})
 events.extend(json.loads(l) for l in (d/'events.jsonl').read_text(encoding='utf-8-sig').splitlines())
assert not any(e['kind']=='ControllerFault' for e in events)
run('run','--scenario',p/'scenario.json','--policy',p/'policy.js','--ticks',duration,'--out',p/'continuous')
continuous=json.loads((p/'continuous/checkpoint.json').read_text(encoding='utf-8-sig'))
assert cp['stateHash']==continuous['stateHash'],'Continuous/stepped states differ'
assert events==[json.loads(l) for l in (p/'continuous/events.jsonl').read_text(encoding='utf-8-sig').splitlines()],'Event histories differ'
print(collections.Counter(e['kind'] for e in events).most_common(16))
effects=[{k:e[k] for k in ['sourceId','targetId','abilityId','startedAtUs','expiresAtUs','endedAtUs','channel']} for e in w['effects']]
missiles=[{k:m[k] for k in ['id','sourceId','targetId','launchedAtUs','expiresAtUs','launchPosition','status','position']} for m in w['missiles']]
meta=[{'id':r['id'],'team':r['team'],'typeId':r['fit']['shipTypeId']} for r in request['participants']]
catalog={t['id']:t for t in json.loads((ui/'data/full-catalog.json').read_text(encoding='utf-8'))}
for m in meta:
 t=catalog[m['typeId']];m['names']={'zh-CN':t['name'],'en':t['en']}

result={'version':1,'duration':duration,'sampleSeconds':sample,'engineVersion':cp['engineVersion'],'source':'SDE 3503375 / native battle assembly','scope':'experimental recorded battle; no full combat certification','seed':request['seed'],'ships':meta,'frames':frames,'events':[{k:e.get(k) for k in ['sequence','timeUs','kind','sourceId','targetId','reason','causeId','entityId','amount']} for e in events if e['kind'] in ['TurretResolved','MissileLaunched','MissileImpacted','MissileExpired','ShipDestroyed','EffectStarted','EffectEnded','DamageApplied','EnergyResolved','RepairApplied','RepairResolved','LockCompleted','CommandRejected','ActivationFailed']],'effects':effects,'missiles':missiles,'script':(p/'policy.js').read_text()}
result['requestSha256']=hashlib.sha256((out/'request.json').read_bytes()).hexdigest()
(ui/'sandbox-preview-data.js').write_text('export default '+json.dumps(result,ensure_ascii=False,separators=(',',':'))+';\n',encoding='utf-8')
(p/'verification.json').write_text(json.dumps({'stateParity':True,'eventParity':True,'events':len(events),'counts':dict(collections.Counter(e['kind'] for e in events))},indent=2),encoding='utf-8')
print('Preview generated:',ui/'sandbox-preview-data.js')
