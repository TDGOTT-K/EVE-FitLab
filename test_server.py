import json, tempfile, threading, unittest, urllib.request, urllib.error
from pathlib import Path
import server

class PersistenceTests(unittest.TestCase):
 def setUp(self):
  self.tmp=tempfile.TemporaryDirectory();self.old=server.STATE;server.STATE=Path(self.tmp.name)
  self.http=server.ThreadingHTTPServer(('127.0.0.1',0),server.Handler)
  self.thread=threading.Thread(target=self.http.serve_forever,daemon=True);self.thread.start()
  self.url='http://127.0.0.1:'+str(self.http.server_port)
 def tearDown(self):
  self.http.shutdown();self.http.server_close();self.thread.join();server.STATE=self.old;self.tmp.cleanup()
 def call(self,path,body=None):
  req=urllib.request.Request(self.url+'/api/'+path,data=json.dumps(body).encode() if body is not None else None,headers={'Content-Type':'application/json','Origin':self.url})
  with urllib.request.urlopen(req) as r:return json.load(r)
 def sample(self):return {'shipId':587,'name':'持久化验证','skills':[{'skillTypeId':3300,'level':4}],'slots':[{'key':'high-0','kind':'high','item':484,'ammo':185,'online':True}],'tags':['验证'],'notes':'使用说明\n第二行'}
 def test_roundtrip_and_conflict(self):
  saved=self.call('save',self.sample());self.assertEqual(self.call('library')[0],saved)
  revised=self.call('save',saved);self.assertEqual(revised['revision'],2)
  with self.assertRaises(urllib.error.HTTPError) as error:self.call('save',saved)
  self.assertEqual(error.exception.code,409);self.assertEqual(self.call('library')[0],revised)
 def test_delete_revision_and_isolation(self):
  first=self.call('save',self.sample());second=self.call('save',self.sample())
  with self.assertRaises(urllib.error.HTTPError) as error:self.call('fit/delete',{'id':first['id'],'revision':0})
  self.assertEqual(error.exception.code,409)
  self.call('fit/delete',{'id':first['id'],'revision':first['revision']})
  self.assertEqual([f['id'] for f in self.call('library')],[second['id']])
  with self.assertRaises(urllib.error.HTTPError) as error:self.call('fit/delete',{'id':first['id'],'revision':first['revision']})
  self.assertEqual(error.exception.code,404)
 def test_invalid_skills_do_not_write(self):
  f=self.sample();f['skills'][0]['level']=6
  with self.assertRaises(urllib.error.HTTPError):self.call('save',f)
  self.assertFalse((server.STATE/'library.json').exists())
 def test_private_state_not_served(self):
  self.call('save',self.sample())
  with self.assertRaises(urllib.error.HTTPError) as error:urllib.request.urlopen(self.url+'/state/library.json')
  self.assertEqual(error.exception.code,404)
if __name__=='__main__':unittest.main()
