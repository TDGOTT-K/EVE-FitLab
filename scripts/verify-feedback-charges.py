"""Reproduce feedback via public MCP/CLI and the UI's native mapping, in disposable state."""
import argparse
import copy
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from agent_mcp import AgentMcp

parser = argparse.ArgumentParser()
parser.add_argument('--engine', type=Path, required=True)
args = parser.parse_args()
engine = args.engine.resolve()
build = engine / 'artifacts/feedback-charge-001/bin'
baseline = engine / 'UI-CHARGE-FEEDBACK-BASELINE.json'
os.environ['FITLAB_NENGINE_ROOT'] = str(engine)
from nengine_adapter import native_fit

template = json.loads((engine/'examples/static-inventory/crystal-fit.json').read_text(encoding='utf-8'))
skills = [{'skillTypeId':int(k),'level':v} for k,v in template['skills'].items()]
evidence = {'engineVersion':'0.213.1-ui.charge1','cases':[], 'scope':'source candidate, not published installer'}
with tempfile.TemporaryDirectory(prefix='fitlab-feedback-') as directory:
    client = AgentMcp(engine, directory, build/'NEngine.Mcp/debug/NEngine.Mcp.dll', baseline)
    try:
        assert client.bridge.discover()['engineVersion'] == evidence['engineVersion']
        for module, charge in [(499,269),(501,1818),(13320,1826),(28758,30013),(18639,30028)]:
            ui = {'id':f'feedback-{module}','shipId':638,'skills':skills,'cargo':[],
                  'slots':[{'key':'high-0','kind':'high','item':module,'ammo':charge,'state':'Active','loadedCharges':1}]}
            fit = native_fit(ui,3503375)
            analysis = client.native('fit_analyze',{'fit':fit})
            assert analysis['staticCoverageComplete'] and not analysis['errors'], analysis['errors']
            assert analysis['inventory']['magazines'][0]['loaded']==1
            path = Path(directory)/'fit.json'
            path.write_text(json.dumps(fit),encoding='utf-8')
            result = subprocess.run([str(engine/'.tools/dotnet/dotnet.exe'),str(build/'NEngine.Cli/debug/NEngine.Cli.dll'),
                'sde-fit','--data',str(engine/'artifacts/sde-mutation-index-002'),'--fit',str(path)],
                capture_output=True,text=True,encoding='utf-8',check=True)
            assert json.loads(result.stdout)==analysis, 'MCP / CLI mismatch'
            unloaded=copy.deepcopy(fit)
            unloaded['items'][0]['chargeTypeId']=None
            unloaded['inventory']['magazines']=[]
            commands=[{'kind':'setCharge','instanceId':'high-0','chargeTypeId':charge},
                      {'kind':'setInventory','inventory':fit['inventory']}]
            preview=client.native('fit_preview_input',{'fit':unloaded,'commands':commands})
            assert preview['analysis']['staticCoverageComplete'] and not preview['analysis']['errors']
            sid=f'feedback-{module}'
            client.native('fit_create',{'sessionId':sid,'fit':unloaded})
            applied=client.native('fit_workbench',{'request':{'operation':'apply','sessionId':sid,'revision':0,
                'requestId':'load','commands':commands,'installation':True}})
            assert applied['analysis']['staticCoverageComplete'] and not applied['analysis']['errors']
            assert applied['fit']['items'][0]['chargeTypeId']==charge
            client.native('fit_workbench',{'request':{'operation':'undo','sessionId':sid,'revision':applied['revision'],'requestId':'undo'}})
            evidence['cases'].append({'moduleTypeId':module,'chargeTypeId':charge,'nativeMapping':True,
                'mcpCliEqual':True,'previewApplyUndo':True,'finiteInventory':True})
    finally:
        client.close()
output=ROOT/'output/feedback-charges-acceptance.json'
output.parent.mkdir(exist_ok=True)
output.write_text(json.dumps(evidence,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(evidence,ensure_ascii=False))
