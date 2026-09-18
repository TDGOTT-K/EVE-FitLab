import tempfile,unittest
from pathlib import Path
from unittest.mock import patch
from nengine_bridge import NEngineBridge,NEngineError

class ReadCache(unittest.TestCase):
 def test_exact_input_isolation_and_no_transaction_cache(self):
  with tempfile.TemporaryDirectory() as directory:
   client=NEngineBridge(state=Path(directory));count=0
   def rpc(*args):
    nonlocal count
    count+=1;return {'structuredContent':{'ok':True,'result':{'value':None,'reason':'missing','nested':[count]}}}
   try:
    with patch.object(client,'_start'),patch.object(client,'_rpc',side_effect=rpc):
     first=client.call('fit_analyze',{'fit':{'x':1}});first['result']['nested'][0]=999
     second=client.call('fit_analyze',{'fit':{'x':1}})
     self.assertEqual(count,1);self.assertEqual(second['result']['nested'],[1]);self.assertIsNone(second['result']['value'])
     client.call('fit_analyze',{'fit':{'x':2}});self.assertEqual(count,2)
     for context in ({'mode':'a'},{'mode':'b'}):client.call('fit_analyze',{'fit':{'x':1},'context':context})
     self.assertEqual(count,4)
     for _ in range(2):client.call('fit_inspect',{'sessionId':'s'});client.call('fit_execute',{'sessionId':'s'})
     self.assertEqual(count,8)
     for i in range(60):client.call('fit_analyze',{'fit':{'x':i}})
     self.assertLessEqual(len(client.read_cache),48);self.assertLessEqual(client.read_cache_bytes,16*1024*1024)
     client.close();self.assertEqual(len(client.read_cache),0)
   finally:client.close()
 def test_errors_are_not_cached(self):
  with tempfile.TemporaryDirectory() as directory:
   client=NEngineBridge(state=Path(directory))
   try:
    with patch.object(client,'_start'),patch.object(client,'_rpc',return_value={'isError':True,'structuredContent':{'ok':False,'error':{'code':'X'}}}) as rpc:
     for _ in range(2):
      with self.assertRaises(NEngineError):client.call('fit_analyze',{'fit':{}})
     self.assertEqual(rpc.call_count,2);self.assertEqual(len(client.read_cache),0)
   finally:client.close()

if __name__=='__main__':unittest.main()
