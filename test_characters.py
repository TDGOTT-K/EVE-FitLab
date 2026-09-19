import hashlib, base64, unittest, urllib.parse
from unittest.mock import patch
import eve_sso
from test_server import PersistenceTests

def skill_response(level=4):
 return {'skills':[{'skill_id':3300,'active_skill_level':level,'trained_skill_level':5,'skillpoints_in_skill':256123}],'total_sp':256123,'unallocated_sp':12345},{'provider':'ESI Tranquility','url':'https://esi.evetech.net/latest/characters/123/skills/?datasource=tranquility','fetchedAt':'2026-09-19T00:00:00Z','contentSha256':'a'*64,'providerDate':None,'expiresAt':None}

class CharacterTests(PersistenceTests):
 def test_character_edit_and_validation(self):
  c=self.call('character',{'name':'自定义测试','skills':[{'skillTypeId':3300,'level':3}]})
  self.assertEqual(c['source'],'自定义角色')
  changed=self.call('character',dict(c,name='改名',skills=[]));self.assertEqual(changed['id'],c['id']);self.assertEqual(changed['skills'],[])
  import urllib.error
  with self.assertRaises(urllib.error.HTTPError):self.call('character',c)
  with self.assertRaises(urllib.error.HTTPError):self.call('character',{'name':'无效','skills':[{'skillTypeId':3300,'level':9}]})
  with self.assertRaises(urllib.error.HTTPError):self.call('character',{'id':'all5','name':'内置','skills':[]})
  self.call('character/delete',{'id':c['id']})
  import server
  self.assertEqual(server.read_library()['characters'],[])
 def test_callback_import_and_reimport(self):
  import http.client,server,json
  for expected in [1,2]:
   conn=http.client.HTTPConnection('127.0.0.1',self.http.server_port)
   with patch('server.ensure_sso_callback'):
    conn.request('POST','/api/eve/login','{}',{'Origin':self.url,'Content-Type':'application/json'})
    r=conn.getresponse();raw=r.read()
   cookie=r.getheader('Set-Cookie').split(';')[0];url=json.loads(raw)['url'];state=urllib.parse.parse_qs(urllib.parse.urlsplit(url).query)['state'][0]
   self.assertEqual(urllib.parse.parse_qs(urllib.parse.urlsplit(url).query)['client_id'],[eve_sso.CLIENT_ID])
   with patch('eve_sso.request_json',side_effect=[{'access_token':'test'}, {'CharacterID':123,'CharacterName':'测试官网角色','Scopes':eve_sso.SCOPE}]), patch('eve_sso.request_skills',return_value=skill_response(4)):
    conn.request('GET','/api/eve/callback?state='+state+'&code=test',headers={'Cookie':cookie})
    r=conn.getresponse();self.assertEqual(r.status,303);self.assertIn('imported',r.getheader('Location'));r.read()
   saved=server.read_library()['characters'];self.assertEqual(len(saved),1);self.assertEqual(saved[0]['revision'],expected);self.assertNotIn('access_token',saved[0]);self.assertEqual(saved[0]['skillSnapshot']['totalSp'],256123);self.assertEqual(saved[0]['skills'][0]['level'],4);conn.close()

 def test_fixed_callback_returns_to_initiator(self):
  import http.client,server,json
  server.ensure_sso_callback(self.http)
  try:
   login=http.client.HTTPConnection('127.0.0.1',self.http.server_port)
   login.request('POST','/api/eve/login','{}',{'Origin':self.url,'Content-Type':'application/json'})
   r=login.getresponse();cookie=r.getheader('Set-Cookie').split(';')[0];url=json.loads(r.read())['url'];state=urllib.parse.parse_qs(urllib.parse.urlsplit(url).query)['state'][0]
   callback=http.client.HTTPConnection('127.0.0.1',5207)
   with patch('eve_sso.request_json',side_effect=[{'access_token':'test'}, {'CharacterID':123,'CharacterName':'测试官网角色','Scopes':eve_sso.SCOPE}]),patch('eve_sso.request_skills',return_value=skill_response()):
    callback.request('GET','/api/eve/callback?state='+state+'&code=test',headers={'Cookie':cookie})
    r=callback.getresponse();self.assertEqual(r.status,303);self.assertTrue(r.getheader('Location').startswith(self.url+'/#characters?imported='));r.read()
   callback.request('GET','/api/eve/callback?state='+state+'&code=test',headers={'Cookie':cookie})
   r=callback.getresponse();self.assertIn('authError=',r.getheader('Location'));r.read()
   self.assertEqual(server.read_library()['characters'][0]['revision'],1)
   callback.request('GET','/#characters')
   r=callback.getresponse();self.assertEqual(r.getheader('Location'),self.url+'/#characters');r.read()
   callback.close();login.close()
  finally:
   server._callback_server.shutdown();server._callback_server.server_close();server._callback_server=None

 def test_callback_failure_does_not_write(self):
  import http.client,server
  url,cookie=eve_sso.begin(eve_sso.CLIENT_ID);state=urllib.parse.parse_qs(urllib.parse.urlsplit(url).query)['state'][0]
  conn=http.client.HTTPConnection('127.0.0.1',self.http.server_port)
  conn.request('GET','/api/eve/callback?state='+state+'&error=access_denied',headers={'Cookie':'fitlab-sso='+cookie})
  r=conn.getresponse();self.assertIn('authError=',r.getheader('Location'));r.read();conn.close()
  self.assertEqual(server.read_library()['characters'],[])


class OAuthTests(unittest.TestCase):
 def test_pkce_browser_binding_and_single_use(self):
  url,cookie=eve_sso.begin('testclient123');q=urllib.parse.parse_qs(urllib.parse.urlsplit(url).query);state=q['state'][0]
  self.assertEqual(q['code_challenge_method'],['S256'])
  with self.assertRaises(ValueError):eve_sso.finish({'state':[state],'code':['code']},'wrong-browser')
  def request(url,data=None,token=None):
   if data:
    digest=base64.urlsafe_b64encode(hashlib.sha256(data['code_verifier'].encode()).digest()).decode().rstrip('=');self.assertEqual(digest,q['code_challenge'][0]);return {'access_token':'test'}
   if url.endswith('/verify'):return {'CharacterID':123,'CharacterName':'Pilot','Scopes':eve_sso.SCOPE}
   return {'skills':[{'skill_id':3300,'active_skill_level':3}]}
  with patch('eve_sso.request_json',side_effect=request),patch('eve_sso.request_skills',return_value=skill_response(3)):
   self.assertEqual(eve_sso.finish({'state':[state],'code':['code']},cookie)['skills'][0]['level'],3)
  with self.assertRaises(ValueError):eve_sso.finish({'state':[state],'code':['code']},cookie)
 def test_missing_scope_is_rejected(self):
  url,cookie=eve_sso.begin('testclient123');q=urllib.parse.parse_qs(urllib.parse.urlsplit(url).query)
  with patch('eve_sso.request_json',side_effect=[{'access_token':'test'},{'CharacterID':123,'CharacterName':'Pilot','Scopes':''}]) as request:
   with self.assertRaises(ValueError):eve_sso.finish({'state':q['state'],'code':['code']},cookie)
   self.assertEqual(request.call_count,2)
 def test_expired_authorization(self):
  url,cookie=eve_sso.begin('testclient123');q=urllib.parse.parse_qs(urllib.parse.urlsplit(url).query)
  with patch('eve_sso.time.time',return_value=10**12):
   with self.assertRaises(ValueError):eve_sso.finish({'state':q['state'],'code':['code']},cookie)
if __name__=='__main__':unittest.main()
