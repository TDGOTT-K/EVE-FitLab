import tempfile,unittest
from pathlib import Path
from unittest.mock import patch
from nengine_bridge import NEngineBridge,NEngineError

class ReadCache(unittest.TestCase):
 def test_background_lane_rejects_writes_before_transport(self):
  with tempfile.TemporaryDirectory() as directory:
   c=NEngineBridge(state=Path(directory),read_only=True)
   try:
    with patch.object(c,'_start'),patch.object(c,'_rpc') as rpc:
     for name,args in [('fit_execute',{}),('fit_create',{}),('fit_workbench',{'request':{'operation':'apply'}})]:
      with self.assertRaises(ValueError):c.call(name,args)
     rpc.assert_not_called()
   finally:c.close()
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
 def test_preview_seeds_exact_reads_without_caching_transactions(self):
  with tempfile.TemporaryDirectory() as directory:
   c=NEngineBridge(state=Path(directory));before={'shipTypeId':621,'items':[]};after={'schemaVersion':1,'shipTypeId':621,'items':[{'id':'a','typeId':501,'mutation':None}]}
   response={'structuredContent':{'ok':True,'result':{'candidate':after,'baselineAnalysis':{'fitHash':'b','errors':[]},'analysis':{'fitHash':'a','errors':[]}}}}
   try:
    with patch.object(c,'_start'),patch.object(c,'_rpc',return_value=response) as rpc:
     result=c.call('fit_preview_input',{'fit':before,'commands':[{'kind':'install'}]})
     result['result']['analysis']['errors'].append('changed')
     self.assertEqual(c.call('fit_analyze',{'fit':before})['result']['fitHash'],'b')
     short={'shipTypeId':621,'items':[{'id':'a','typeId':501}]}
     self.assertEqual(c.call('fit_analyze',{'fit':short,'context':{}})['result']['errors'],[])
     self.assertEqual(rpc.call_count,1)
     for different in ({**short,'inventory':None},{**short,'inventory':{'cargo':[]}},{**short,'schemaVersion':True},{**short,'skills':None}):
      c.call('fit_analyze',{'fit':different})
     c.call('fit_analyze',{'fit':short,'context':False})
     self.assertEqual(rpc.call_count,6)
     c.call('fit_execute',{'sessionId':'s'});c.call('fit_execute',{'sessionId':'s'})
     self.assertEqual(rpc.call_count,8)
   finally:c.close()

if __name__=='__main__':unittest.main()
