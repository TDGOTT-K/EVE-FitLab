import json,subprocess,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
from nengine_bridge import NEngineBridge,NEngineError
from nengine_adapter import native_fit
class WorkbenchProtocol(unittest.TestCase):
 def test_read_edit_cli_parity_and_detail_values(self):
  with tempfile.TemporaryDirectory() as tmp:
   root=Path(tmp);c=NEngineBridge(state=root/'mcp')
   try:
    status=c.discover()
    f={'id':'workbench-test','shipId':587,'name':'Atomic test','skills':[{'skillTypeId':3327,'level':5},{'skillTypeId':3300,'level':3}], 'slots':[{'key':'high-0','kind':'high','item':2881,'ammo':185,'state':'Active'}]}
    with patch('nengine_adapter.bridge',return_value=c):native=native_fit(f,status['source']['source']['buildNumber'])
    request={'fit':native,'context':{'output':{'target':{'id':'target','distanceMeters':1200,'signatureMeters':40,'speedMetersPerSecond':100,'angularRadiansPerSecond':0.02,'layer':{'name':'armor','resonances':{'em':0.5,'thermal':0.6,'kinetic':0.7,'explosive':0.8}}}}},'effective':True}
    result=c.call('fit_workbench',{'request':request})['result'];a=result['analysis']
    full=c.call('fit_analyze',{'fit':native,'context':a['metricContext']})['result']
    self.assertEqual(a['inspector'],full['inspector']);self.assertEqual(a['outputContributions'],full['outputContributions'])
    self.assertEqual(a['errors'],full['errors']);self.assertEqual(a['staticCoverageComplete'],full['staticCoverageComplete'])
    for key,trace in a['attributes'].items():
     self.assertEqual(trace['value'],full['attributes'][key]['value']);self.assertEqual(trace['steps'],[])
    for req,expected in [(request,result),({**request,'operation':'apply','sessionId':'atomic','revision':0,'requestId':'one','commands':[{'kind':'setName','name':'Changed'}]},None)]:
     expected=expected or c.call('fit_workbench',{'request':req})['result']
     (root/'request.json').write_text(json.dumps(req),encoding='utf-8')
     args=[str(c.root/'.tools/dotnet/dotnet.exe'),str(c.root/'src/NEngine.Cli/bin/Debug/net10.0/NEngine.Cli.dll'),'sde-workbench','--data',str(c.root/c.baseline['dataDirectory']),'--request',str(root/'request.json'),'--out',str(root/'out.json')]
     if req.get('operation')=='apply':args+=['--session',str(root/'cli.json')]
     subprocess.run(args,check=True,capture_output=True)
     self.assertEqual(json.loads((root/'out.json').read_text(encoding='utf-8-sig')),expected)
    repeated=c.call('fit_workbench',{'request':req})['result'];self.assertTrue(repeated['replayed']);self.assertEqual(repeated['revision'],1)
    with self.assertRaises(NEngineError):c.call('fit_workbench',{'request':{**req,'installation':True}})
   finally:c.close()
if __name__=='__main__':unittest.main()
