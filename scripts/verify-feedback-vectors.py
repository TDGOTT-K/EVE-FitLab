"""Verify vector semantics against real native application, including MCP/CLI parity."""
import argparse,json,subprocess,sys,tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
parser=argparse.ArgumentParser();parser.add_argument('--engine',type=Path,required=True);args=parser.parse_args()
engine=args.engine.resolve()
import os
os.environ['FITLAB_NENGINE_ROOT']=str(engine)
import nengine_adapter
from nengine_bridge import NEngineBridge
from nengine_scenario import resolve_target
victim={'id':'victim','name':'Victim','shipId':587,'slots':[],'skills':[]}
results={}
with tempfile.TemporaryDirectory(prefix='feedback-vectors-') as directory:
    client=NEngineBridge(root=engine,state=directory);nengine_adapter.bridge=lambda:client
    try:
        for kind,module,charge in [('missile',499,269),('turret',2881,185)]:
            fit={'id':'shooter','name':kind,'shipId':621 if kind=='missile' else 587,'skills':[],
                 'slots':[{'key':'high-0','kind':'high','item':module,'ammo':charge,'state':'Active'}]}
            readings=[]
            for own,target_speed in [(0,200),(200,200),(-200,200),(200,0)]:
                scenario={'targetFitId':'victim','geometry':{'x':1000,'y':0,'vx':0,'vy':target_speed,'ownVx':0,'ownVy':own}}
                target=resolve_target({**fit,'scenario':scenario},[victim],nengine_adapter.analyze)
                report=nengine_adapter.analyze(fit,target=target)
                readings.append(report['outputSelection']['total'])
                fp=Path(directory)/'fit.json';cp=Path(directory)/'context.json'
                fp.write_text(json.dumps(report['nativeFit']),encoding='utf8')
                cp.write_text(json.dumps({'output':report['outputContext']}),encoding='utf8')
                raw=subprocess.run([str(engine/'.tools/dotnet/dotnet.exe'),str(engine/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),
                    'sde-fit','--data',str(engine/'artifacts/sde-mutation-index-002'),'--fit',str(fp),'--metrics',str(cp)],
                    capture_output=True,text=True,encoding='utf8',check=True)
                native=json.loads(raw.stdout)
                assert native==report['native'], 'Native MCP/CLI analysis mismatch'
            assert all(x is not None for x in readings),readings
            if kind=='missile':
                assert readings[0]==readings[1]==readings[2],readings
                assert readings[3]>readings[0],readings
            else:assert readings[1]>readings[0]>readings[2],readings
            results[kind]={'dps':readings,'mcpCliCases':4}
    finally:client.close()
(ROOT/'output/feedback-vectors-acceptance.json').write_text(json.dumps(results,indent=2),encoding='utf8')
print(json.dumps(results))
